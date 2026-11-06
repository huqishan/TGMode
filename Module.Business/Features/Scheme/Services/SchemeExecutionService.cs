using ControlLibrary.Models.EventsModels.Test;
using ControlLibrary.Models.MediatorModels.Communication;
using Module.Business.Models;
using Module.Business.Services.BusinessOperations;
using Module.Business.Features.Scheme.ViewModels.PresentationModels;
using Module.Business.Features.OperationEditing.ViewModels.PresentationModels;
using Module.Business.Features.WorkStep.Services;
using Module.Business.Features.WorkStep.ViewModels.PresentationModels;
using Shared.Infrastructure.Communication;
using Shared.Infrastructure.Events;
using Shared.Infrastructure.Lua;
using Shared.Infrastructure.Mediator;
using Shared.Infrastructure.PackMethod;
using Shared.Models.Communication;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Module.Business.Services;

/// <summary>
/// 方案执行服务，负责按工位编排方案、工步及步骤执行，并提供暂停、继续、停止和状态通知能力。
/// </summary>
public static class SchemeExecutionService
{
    #region 常量与运行状态字段
    /// <summary>执行控制状态的轮询间隔，单位为毫秒。</summary>
    private const int ControlPollingIntervalMilliseconds = 50;

    /// <summary>用于解析命令文本中“{{参数名}}”占位符的正则表达式。</summary>
    private static readonly Regex PlaceholderRegex =
        new(@"\{\{\s*(?<name>[^{}\r\n]+?)\s*\}\}", RegexOptions.Compiled);

    /// <summary>当前正在执行的方案，以工位编号作为唯一占用键。</summary>
    private static readonly ConcurrentDictionary<string, SchemeExecutionContext> ActiveExecutions =
        new(StringComparer.OrdinalIgnoreCase);
    /// <summary>用于发布测试状态事件的事件聚合器。</summary>
    private static IEventAggregator? _eventAggregator;
    /// <summary>用于向通信模块发送请求的中介器。</summary>
    private static IMediator? _mediator;
    /// <summary>当前异步执行链的受控上下文，供嵌套业务工步复用暂停和取消状态。</summary>
    private static readonly AsyncLocal<IControlledExecutionContext?> CurrentExecutionContext = new();
    /// <summary>当前异步执行链的方案工步，供嵌套业务工步解析工步输入参数。</summary>
    private static readonly AsyncLocal<SchemeWorkStepItem?> CurrentSchemeStep = new();
    /// <summary>当前异步执行链中正在展开的工步名称，用于阻止配置循环递归。</summary>
    private static readonly AsyncLocal<IReadOnlyList<string>?> ActiveNamedWorkSteps = new();

    #endregion

    /// <summary>配置方案执行状态发布所使用的事件聚合器。</summary>
    public static void ConfigureEventAggregator(IEventAggregator eventAggregator)
    {
        _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
    }

    /// <summary>配置设备通信所使用的中介器。</summary>
    public static void ConfigureMediator(IMediator mediator)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
    }

    #region 执行生命周期事件

    /// <summary>方案执行前事件，可由订阅方取消执行。</summary>
    public static event EventHandler<SchemeExecutionEventArgs>? BeforeSchemeExecuting;

    /// <summary>方案开始执行事件。</summary>
    public static event EventHandler<SchemeExecutionEventArgs>? SchemeExecuting;

    /// <summary>方案执行完成事件，包含执行结果和时间信息。</summary>
    public static event EventHandler<SchemeExecutionEventArgs>? AfterSchemeExecuted;

    /// <summary>工步执行前事件，可由订阅方取消执行。</summary>
    public static event EventHandler<SchemeExecutionEventArgs>? BeforeWorkStepExecuting;

    /// <summary>工步开始执行事件。</summary>
    public static event EventHandler<SchemeExecutionEventArgs>? WorkStepExecuting;

    /// <summary>工步执行完成事件，包含执行结果和时间信息。</summary>
    public static event EventHandler<SchemeExecutionEventArgs>? AfterWorkStepExecuted;

    /// <summary>步骤执行前事件，可由订阅方取消执行。</summary>
    public static event EventHandler<SchemeExecutionEventArgs>? BeforeStepExecuting;

    /// <summary>步骤开始执行事件。</summary>
    public static event EventHandler<SchemeExecutionEventArgs>? StepExecuting;

    /// <summary>步骤执行完成事件，包含执行结果、耗时和返回值。</summary>
    public static event EventHandler<SchemeExecutionEventArgs>? AfterStepExecuted;

    #endregion

    #region 方案执行与控制入口

    /// <summary>根据工位编号和方案名称读取配置并执行，同一工位同时只允许一个方案运行。</summary>
    public static async Task<SchemeExecutionResult> ExecuteAsync(string stationNo, string schemeName)
    {
        string normalizedStationNo = NormalizeRequiredText(stationNo);
        string normalizedSchemeName = NormalizeRequiredText(schemeName);
        DateTime startTime = DateTime.Now;
        if (string.IsNullOrWhiteSpace(normalizedStationNo))
        {
            return SchemeExecutionResult.CreateFailure("Station number is required.", startTime: startTime, endTime: DateTime.Now);
        }

        if (string.IsNullOrWhiteSpace(normalizedSchemeName))
        {
            return SchemeExecutionResult.CreateFailure("Scheme name is required.", startTime: startTime, endTime: DateTime.Now);
        }

        PublishTestStatus(
            normalizedStationNo,
            "准备测试",
            string.Empty,
            normalizedSchemeName,
            string.Empty,
            $"准备执行测试方案：{normalizedSchemeName}");

        SchemeExecutionKey key = new(normalizedStationNo, normalizedSchemeName);
        SchemeExecutionContext context = new(key, startTime);
        if (!ActiveExecutions.TryAdd(normalizedStationNo, context))
        {
            string runningSchemeName = ActiveExecutions.TryGetValue(normalizedStationNo, out SchemeExecutionContext? runningContext)
                ? runningContext.Key.SchemeName
                : string.Empty;
            context.Dispose();
            string message = string.IsNullOrWhiteSpace(runningSchemeName)
                ? $"Station '{normalizedStationNo}' already has a running scheme."
                : $"Station '{normalizedStationNo}' is already running scheme '{runningSchemeName}'.";
            PublishTestStatus(
                normalizedStationNo,
                "启动失败",
                string.Empty,
                normalizedSchemeName,
                string.Empty,
                message,
                false);
            return SchemeExecutionResult.CreateFailure(
                message,
                startTime: startTime,
                endTime: DateTime.Now);
        }

        try
        {
            SchemeConfigurationCatalog catalog = SchemeConfigurationStore.LoadCatalog();
            SchemeProfile? scheme = catalog.Schemes.FirstOrDefault(item =>
                string.Equals(item.SchemeName?.Trim(), normalizedSchemeName, StringComparison.OrdinalIgnoreCase));
            if (scheme is null)
            {
                PublishTestStatus(
                    normalizedStationNo,
                    "加载失败",
                    string.Empty,
                    normalizedSchemeName,
                    string.Empty,
                    $"Scheme '{normalizedSchemeName}' was not found.",
                    false);
                return SchemeExecutionResult.CreateFailure(
                    $"Scheme '{normalizedSchemeName}' was not found.",
                    startTime: startTime,
                    endTime: DateTime.Now);
            }

            return await ExecuteSchemeAsync(context, scheme.Clone(), WorkStepConfigurationStore.Load())
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            PublishTestStatus(
                normalizedStationNo,
                "已停止",
                string.Empty,
                normalizedSchemeName,
                string.Empty,
                $"Scheme '{normalizedSchemeName}' on station '{normalizedStationNo}' was stopped.",
                false);
            return SchemeExecutionResult.CreateCanceled(
                $"Scheme '{normalizedSchemeName}' on station '{normalizedStationNo}' was stopped.",
                context.Logs,
                startTime,
                DateTime.Now);
        }
        catch (Exception ex)
        {
            PublishTestStatus(
                normalizedStationNo,
                "执行失败",
                string.Empty,
                normalizedSchemeName,
                string.Empty,
                $"Scheme '{normalizedSchemeName}' on station '{normalizedStationNo}' failed: {ex.Message}",
                false);
            return SchemeExecutionResult.CreateFailure(
                $"Scheme '{normalizedSchemeName}' on station '{normalizedStationNo}' failed: {ex.Message}",
                context.Logs,
                startTime,
                DateTime.Now);
        }
        finally
        {
            ActiveExecutions.TryRemove(normalizedStationNo, out _);
            context.Dispose();
        }
    }
    /// <summary>按方案与工步标识执行选中的单个工步。</summary>
    public static async Task<SchemeExecutionResult> ExecuteWorkStepAsync(
        string stationNo,
        string schemeId,
        string workStepId)
    {
        string normalizedStationNo = NormalizeRequiredText(stationNo);
        string normalizedSchemeId = NormalizeRequiredText(schemeId);
        string normalizedWorkStepId = NormalizeRequiredText(workStepId);
        DateTime startTime = DateTime.Now;
        if (string.IsNullOrWhiteSpace(normalizedStationNo) ||
            string.IsNullOrWhiteSpace(normalizedSchemeId) ||
            string.IsNullOrWhiteSpace(normalizedWorkStepId))
        {
            return SchemeExecutionResult.CreateFailure(
                "工位、方案和工步标识不能为空。",
                startTime: startTime,
                endTime: DateTime.Now);
        }

        SchemeProfile? scheme;
        try
        {
            SchemeConfigurationCatalog catalog = await Task.Run(SchemeConfigurationStore.LoadCatalog)
                .ConfigureAwait(false);
            scheme = catalog.Schemes.FirstOrDefault(item =>
                string.Equals(item.Id, normalizedSchemeId, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            string message = $"加载方案失败：{ex.Message}";
            PublishTestStatus(normalizedStationNo, "执行失败", string.Empty, string.Empty, string.Empty, message, false);
            return SchemeExecutionResult.CreateFailure(message, startTime: startTime, endTime: DateTime.Now);
        }

        if (scheme is null)
        {
            string message = $"未找到方案标识 {normalizedSchemeId}。";
            PublishTestStatus(normalizedStationNo, "执行失败", string.Empty, string.Empty, string.Empty, message, false);
            return SchemeExecutionResult.CreateFailure(message, startTime: startTime, endTime: DateTime.Now);
        }

        SchemeExecutionKey key = new(normalizedStationNo, scheme.SchemeName);
        SchemeExecutionContext context = new(key, startTime);
        if (!ActiveExecutions.TryAdd(normalizedStationNo, context))
        {
            context.Dispose();
            string message = $"工位 {normalizedStationNo} 已有正在执行的方案。";
            PublishSchemeStatus(normalizedStationNo, "启动失败", scheme, message, false);
            return SchemeExecutionResult.CreateFailure(message, startTime: startTime, endTime: DateTime.Now);
        }

        try
        {
            int workStepIndex = -1;
            SchemeWorkStepItem? schemeStep = null;
            for (int index = 0; index < scheme.Steps.Count; index++)
            {
                if (!string.Equals(scheme.Steps[index].Id, normalizedWorkStepId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                schemeStep = scheme.Steps[index];
                workStepIndex = index + 1;
                break;
            }

            if (schemeStep is null)
            {
                string message = $"方案 {scheme.SchemeName} 中未找到选中的工步。";
                PublishSchemeStatus(normalizedStationNo, "执行失败", scheme, message, false);
                return SchemeExecutionResult.CreateFailure(message, context.Logs, startTime, DateTime.Now);
            }

            IReadOnlyCollection<WorkStepProfile> configuredWorkSteps = await Task.Run(WorkStepConfigurationStore.Load)
                .ConfigureAwait(false);
            WorkStepProfile? configuredWorkStep = configuredWorkSteps.FirstOrDefault(item =>
                string.Equals(item.Name, schemeStep.StepType, StringComparison.OrdinalIgnoreCase));
            if (configuredWorkStep is null || configuredWorkStep.Operations.Count == 0)
            {
                string message = $"工步 {schemeStep.StepName} 未配置可执行步骤。";
                PublishSchemeStatus(normalizedStationNo, "执行失败", scheme, message, false);
                return SchemeExecutionResult.CreateFailure(message, context.Logs, startTime, DateTime.Now);
            }

            PublishSchemeStatus(normalizedStationNo, "单步测试中", scheme, $"开始执行工步：{schemeStep.StepName}");
            SchemeExecutionResult result = await ExecuteWorkStepAsync(
                    context,
                    scheme,
                    schemeStep,
                    configuredWorkStep.Operations,
                    workStepIndex)
                .ConfigureAwait(false);

            string finalStatus = result.IsSuccess ? "单步测试通过" : result.IsCanceled ? "单步测试已停止" : "单步测试失败";
            PublishSchemeStatus(normalizedStationNo, finalStatus, scheme, result.Message, result.IsSuccess);
            return result;
        }
        catch (OperationCanceledException)
        {
            string message = $"工位 {normalizedStationNo} 的工步执行已停止。";
            PublishSchemeStatus(normalizedStationNo, "单步测试已停止", scheme, message, false);
            return SchemeExecutionResult.CreateCanceled(message, context.Logs, startTime, DateTime.Now);
        }
        catch (Exception ex)
        {
            string message = $"工步 {scheme.SchemeName} 执行失败：{ex.Message}";
            PublishSchemeStatus(normalizedStationNo, "单步测试失败", scheme, message, false);
            return SchemeExecutionResult.CreateFailure(message, context.Logs, startTime, DateTime.Now);
        }
        finally
        {
            ActiveExecutions.TryRemove(normalizedStationNo, out _);
            context.Dispose();
        }
    }

    /// <summary>暂停指定工位当前正在运行的方案。</summary>
    public static SchemeExecutionControlActionResult Pause(string stationNo)
    {
        if (!TryGetStationContexts(stationNo, out List<SchemeExecutionContext> contexts, out string message))
        {
            return SchemeExecutionControlActionResult.CreateFailure(message);
        }

        string normalizedStationNo = NormalizeRequiredText(stationNo);
        int pausedCount = contexts.Count(context => context.Pause());
        if (pausedCount > 0)
        {
            PublishExecutionControlStatus(normalizedStationNo, "方案暂停", contexts.First().Key.SchemeName, "方案已暂停");
        }

        return pausedCount > 0
            ? SchemeExecutionControlActionResult.CreateSuccess(
                $"Station '{normalizedStationNo}' paused {pausedCount}/{contexts.Count} execution(s).")
            : SchemeExecutionControlActionResult.CreateSuccess(
                $"Station '{normalizedStationNo}' executions are already paused.");
    }
    /// <summary>继续指定工位当前已暂停的方案。</summary>
    public static SchemeExecutionControlActionResult Continue(string stationNo)
    {
        if (!TryGetStationContexts(stationNo, out List<SchemeExecutionContext> contexts, out string message))
        {
            return SchemeExecutionControlActionResult.CreateFailure(message);
        }

        string normalizedStationNo = NormalizeRequiredText(stationNo);
        int resumedCount = contexts.Count(context => context.Resume());
        if (resumedCount > 0)
        {
            PublishExecutionControlStatus(normalizedStationNo, "方案继续", contexts.First().Key.SchemeName, "方案已继续");
        }

        return resumedCount > 0
            ? SchemeExecutionControlActionResult.CreateSuccess(
                $"Station '{normalizedStationNo}' resumed {resumedCount}/{contexts.Count} execution(s).")
            : SchemeExecutionControlActionResult.CreateSuccess(
                $"Station '{normalizedStationNo}' executions are not paused.");
    }
    /// <summary>请求停止指定工位当前正在运行的方案。</summary>
    public static SchemeExecutionControlActionResult Stop(string stationNo)
    {
        if (!TryGetStationContexts(stationNo, out List<SchemeExecutionContext> contexts, out string message))
        {
            return SchemeExecutionControlActionResult.CreateFailure(message);
        }

        foreach (SchemeExecutionContext context in contexts)
        {
            context.Stop();
        }

        string normalizedStationNo = NormalizeRequiredText(stationNo);
        PublishExecutionControlStatus(normalizedStationNo, "停止中", contexts.First().Key.SchemeName, "已发送停止方案请求");
        return SchemeExecutionControlActionResult.CreateSuccess(
            $"Stop request sent to {contexts.Count} execution(s) on station '{normalizedStationNo}'.");
    }

    /// <summary>获取当前所有正在运行的方案快照。</summary>
    public static IReadOnlyList<SchemeExecutionSnapshot> GetActiveExecutions()
    {
        return ActiveExecutions.Values
            .Select(context => context.CreateSnapshot())
            .OrderBy(item => item.StationNo, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.SchemeName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    #endregion

    #region 方案、工步与步骤执行编排

    /// <summary>按顺序执行方案中的全部工步，并汇总方案级执行结果。</summary>
    private static async Task<SchemeExecutionResult> ExecuteSchemeAsync(
        SchemeExecutionContext context,
        SchemeProfile scheme,
        IReadOnlyCollection<WorkStepProfile> configuredWorkSteps)
    {
        DateTime startTime = DateTime.Now;
        SchemeExecutionEventArgs beforeSchemeArgs = SchemeExecutionEventArgs.CreateScheme(
            context.Key.StationNo,
            scheme,
            startTime: startTime);
        Raise(BeforeSchemeExecuting, beforeSchemeArgs);
        if (beforeSchemeArgs.Cancel)
        {
            DateTime canceledAt = DateTime.Now;
            PublishSchemeStatus(context.Key.StationNo, "已取消", scheme, "Scheme execution was canceled before start.", false);
            return SchemeExecutionResult.CreateCanceled(
                "Scheme execution was canceled before start.",
                context.Logs,
                startTime,
                canceledAt);
        }

        context.AddLog($"Start scheme '{scheme.SchemeName}' on station '{context.Key.StationNo}'.");
        Raise(SchemeExecuting, SchemeExecutionEventArgs.CreateScheme(
            context.Key.StationNo,
            scheme,
            message: "Scheme is executing.",
            startTime: startTime));
        PublishSchemeStatus(context.Key.StationNo, "测试中", scheme, $"测试方案执行中：{scheme.SchemeName}");

        for (int workStepIndex = 0; workStepIndex < scheme.Steps.Count; workStepIndex++)
        {
            await context.WaitIfPausedAsync().ConfigureAwait(false);
            context.ThrowIfCancellationRequested();

            SchemeWorkStepItem schemeStep = scheme.Steps[workStepIndex];
            if (!schemeStep.IsStartupEnabled)
            {
                context.AddLog($"Skip work step {workStepIndex + 1}: {schemeStep.StepName}.");
                continue;
            }

            WorkStepProfile? configuredWorkStep = configuredWorkSteps.FirstOrDefault(item => string.Equals(
                item.Name,
                schemeStep.StepType,
                StringComparison.OrdinalIgnoreCase));
            if (configuredWorkStep is null || configuredWorkStep.Operations.Count == 0)
            {
                string failureMessage = $"Work step '{schemeStep.StepName}' has no operations.";
                DateTime failedAt = DateTime.Now;
                Raise(AfterSchemeExecuted, SchemeExecutionEventArgs.CreateScheme(
                    context.Key.StationNo,
                    scheme,
                    false,
                    failureMessage,
                    startTime,
                    failedAt));
                PublishSchemeStatus(context.Key.StationNo, "测试失败", scheme, failureMessage, false);
                return SchemeExecutionResult.CreateFailure(failureMessage, context.Logs, startTime, failedAt);
            }

            SchemeExecutionResult workStepResult = await ExecuteWorkStepAsync(
                    context,
                    scheme,
                    schemeStep,
                    configuredWorkStep.Operations,
                    workStepIndex + 1)
                .ConfigureAwait(false);
            if (!workStepResult.IsSuccess)
            {
                DateTime failedAt = DateTime.Now;
                Raise(AfterSchemeExecuted, SchemeExecutionEventArgs.CreateScheme(
                    context.Key.StationNo,
                    scheme,
                    false,
                    workStepResult.Message,
                    startTime,
                    failedAt));
                PublishSchemeStatus(context.Key.StationNo, "测试失败", scheme, workStepResult.Message, false);
                return workStepResult;
            }
        }

        string message = $"Scheme '{scheme.SchemeName}' finished.";
        DateTime endTime = DateTime.Now;
        context.AddLog(message);
        Raise(AfterSchemeExecuted, SchemeExecutionEventArgs.CreateScheme(
            context.Key.StationNo,
            scheme,
            true,
            message,
            startTime,
            endTime));
        PublishSchemeStatus(context.Key.StationNo, "测试通过", scheme, message, true);
        return SchemeExecutionResult.CreateSuccess(message, context.Logs, startTime, endTime);
    }

    /// <summary>执行单个工步中的步骤集合，并处理工步级事件、日志和失败结果。</summary>
    private static async Task<SchemeExecutionResult> ExecuteWorkStepAsync(
        SchemeExecutionContext context,
        SchemeProfile scheme,
        SchemeWorkStepItem schemeStep,
        IReadOnlyList<WorkStepOperation> operations,
        int workStepIndex)
    {
        DateTime startTime = DateTime.Now;
        SchemeExecutionEventArgs beforeWorkStepArgs = SchemeExecutionEventArgs.CreateWorkStep(
            context.Key.StationNo,
            scheme,
            schemeStep,
            workStepIndex,
            startTime: startTime);
        Raise(BeforeWorkStepExecuting, beforeWorkStepArgs);
        if (beforeWorkStepArgs.Cancel)
        {
            DateTime canceledAt = DateTime.Now;
            PublishSchemeStatus(context.Key.StationNo, "已取消", scheme, "Work step execution was canceled before start.", false);
            return SchemeExecutionResult.CreateCanceled(
                "Work step execution was canceled before start.",
                context.Logs,
                startTime,
                canceledAt);
        }

        context.AddLog($"Start work step {workStepIndex}: {schemeStep.StepName}.");
        Raise(WorkStepExecuting, SchemeExecutionEventArgs.CreateWorkStep(
            context.Key.StationNo,
            scheme,
            schemeStep,
            workStepIndex,
            message: "Work step is executing.",
            startTime: startTime));
        PublishSchemeStatus(
            context.Key.StationNo,
            "工步执行中",
            scheme,
            $"正在执行工步 {workStepIndex}：{schemeStep.StepName}");

        Dictionary<string, string> returnValues = new(StringComparer.OrdinalIgnoreCase);
        for (int stepIndex = 0; stepIndex < operations.Count; stepIndex++)
        {
            await context.WaitIfPausedAsync().ConfigureAwait(false);
            context.ThrowIfCancellationRequested();

            WorkStepOperation operation = operations[stepIndex];
            SchemeExecutionResult stepResult = await ExecuteStepAsync(
                    context,
                    scheme,
                    schemeStep,
                    operation,
                    returnValues,
                    workStepIndex,
                    stepIndex + 1)
                .ConfigureAwait(false);
            if (!stepResult.IsSuccess)
            {
                DateTime failedAt = DateTime.Now;
                Raise(AfterWorkStepExecuted, SchemeExecutionEventArgs.CreateWorkStep(
                    context.Key.StationNo,
                    scheme,
                    schemeStep,
                    workStepIndex,
                    false,
                    stepResult.Message,
                    startTime,
                    failedAt));
                return stepResult;
            }
        }

        string message = $"Work step {workStepIndex} finished: {schemeStep.StepName}.";
        DateTime endTime = DateTime.Now;
        context.AddLog(message);
        Raise(AfterWorkStepExecuted, SchemeExecutionEventArgs.CreateWorkStep(
            context.Key.StationNo,
            scheme,
            schemeStep,
            workStepIndex,
            true,
            message,
            startTime,
            endTime));
        return SchemeExecutionResult.CreateSuccess(message, context.Logs, startTime, endTime);
    }

    /// <summary>执行单个步骤，处理执行前条件、操作调用、延时以及返回值回写。</summary>
    private static async Task<SchemeExecutionResult> ExecuteStepAsync(
        SchemeExecutionContext context,
        SchemeProfile scheme,
        SchemeWorkStepItem schemeStep,
        WorkStepOperation operation,
        Dictionary<string, string> returnValues,
        int workStepIndex,
        int stepIndex)
    {
        DateTime startTime = DateTime.Now;
        SchemeExecutionEventArgs beforeStepArgs = SchemeExecutionEventArgs.CreateStep(
            context.Key.StationNo,
            scheme,
            schemeStep,
            operation,
            workStepIndex,
            stepIndex,
            startTime: startTime);
        Raise(BeforeStepExecuting, beforeStepArgs);
        if (beforeStepArgs.Cancel)
        {
            DateTime canceledAt = DateTime.Now;
            PublishSchemeStatus(context.Key.StationNo, "已取消", scheme, "Step execution was canceled before start.", false);
            return SchemeExecutionResult.CreateCanceled(
                "Step execution was canceled before start.",
                context.Logs,
                startTime,
                canceledAt);
        }

        context.AddLog($"Start step {workStepIndex}.{stepIndex}: {operation.Summary}.");
        Raise(StepExecuting, SchemeExecutionEventArgs.CreateStep(
            context.Key.StationNo,
            scheme,
            schemeStep,
            operation,
            workStepIndex,
            stepIndex,
            message: "Step is executing.",
            startTime: startTime));
        PublishSchemeStatus(
            context.Key.StationNo,
            "步骤执行中",
            scheme,
            $"正在执行步骤 {workStepIndex}.{stepIndex}：{operation.Summary}");

        SchemeStepExecutionOutput output;
        try
        {
            output = await ExecuteOperationAsync(context, operation, schemeStep, returnValues)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            output = SchemeStepExecutionOutput.Failure(ex.Message);
        }

        string resultText = output.Result?.ToString() ?? string.Empty;
        foreach (var rv in operation.ReturnValues)
        {
            if (!string.IsNullOrWhiteSpace(operation.ReturnValue) &&
                !string.IsNullOrWhiteSpace(rv.ReturnParameterName))
            {
                // 返回值字典与参数下拉框使用同一命名规则，后续步骤可直接按“返回值_返回值键”引用。
                string returnValueName = $"{operation.ReturnValue.Trim()}_{rv.ReturnParameterName.Trim()}";
                returnValues[returnValueName] = resultText;
            }
        }

        foreach (var rv in operation.ReturnValues.Where(rv => rv.IsShowView))
        {
            Module.Business.Business.System.SendDataToView(
                rv.ViewDataName,
                resultText);
        }

        if (operation.DelayMilliseconds > 0)
        {
            await DelayWithControlAsync(context, operation.DelayMilliseconds).ConfigureAwait(false);
        }

        string message = output.IsSuccess
            ? $"Step {workStepIndex}.{stepIndex} finished: {operation.Summary}."
            : $"Step {workStepIndex}.{stepIndex} failed: {output.Message}";
        DateTime endTime = DateTime.Now;
        context.AddLog(message);
        Raise(AfterStepExecuted, SchemeExecutionEventArgs.CreateStep(
            context.Key.StationNo,
            scheme,
            schemeStep,
            operation,
            workStepIndex,
            stepIndex,
            output.IsSuccess,
            message,
            output.Result,
            startTime,
            endTime));
        if (!output.IsSuccess)
        {
            PublishSchemeStatus(context.Key.StationNo, "测试失败", scheme, message, false);
        }

        return output.IsSuccess
            ? SchemeExecutionResult.CreateSuccess(message, context.Logs, startTime, endTime)
            : SchemeExecutionResult.CreateFailure(message, context.Logs, startTime, endTime);
    }

    #endregion

    #region 单步骤操作执行

    /// <summary>为流程图等独立调用场景复用单个步骤的执行逻辑。</summary>
    internal static async Task<SchemeStepExecutionOutput> ExecuteStandaloneStepAsync(
        IControlledExecutionContext context,
        WorkStepOperation operation,
        Dictionary<string, string> returnValues,
        SchemeWorkStepItem? schemeStep = null)
    {
        SchemeWorkStepItem executionStep = schemeStep ?? CreateStandaloneSchemeStep();
        SchemeStepExecutionOutput output = await ExecuteOperationAsync(context, operation, executionStep, returnValues)
            .ConfigureAwait(false);

        string resultText = output.Result?.ToString() ?? string.Empty;
        foreach (var rv in operation.ReturnValues)
        {
            if (!string.IsNullOrWhiteSpace(operation.ReturnValue) &&
                !string.IsNullOrWhiteSpace(rv.ReturnParameterName))
            {
                // 独立步骤执行与方案执行保持一致，统一使用“返回值_返回值键”作为字典键。
                string returnValueName = $"{operation.ReturnValue.Trim()}_{rv.ReturnParameterName.Trim()}";
                returnValues[returnValueName] = resultText;
            }
        }

        foreach (var rv in operation.ReturnValues.Where(rv => rv.IsShowView))
        {
            Module.Business.Business.System.SendDataToView(
                rv.ViewDataName,
                resultText);
        }

        if (operation.DelayMilliseconds > 0)
        {
            await DelayWithControlAsync(context, operation.DelayMilliseconds).ConfigureAwait(false);
        }

        return output;
    }

    /// <summary>根据工步配置名称执行其中的全部操作，并复用当前执行链的控制状态。</summary>
    internal static async Task ExecuteWorkStepByNameAsync(string workStepName)
    {
        string normalizedWorkStepName = NormalizeRequiredText(workStepName);
        if (string.IsNullOrWhiteSpace(normalizedWorkStepName))
        {
            throw new ArgumentException("工步名称不能为空。", nameof(workStepName));
        }

        IControlledExecutionContext? context = CurrentExecutionContext.Value;
        if (context is null)
        {
            throw new InvalidOperationException("只能在受控的方案或流程图执行过程中调用此工步操作。");
        }

        IReadOnlyList<string> activeWorkSteps = ActiveNamedWorkSteps.Value ?? Array.Empty<string>();
        if (activeWorkSteps.Contains(normalizedWorkStepName, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"检测到工步递归调用：{normalizedWorkStepName}。");
        }

        IReadOnlyList<string> previousWorkSteps = ActiveNamedWorkSteps.Value ?? Array.Empty<string>();
        ActiveNamedWorkSteps.Value = previousWorkSteps
            .Append(normalizedWorkStepName)
            .ToArray();

        try
        {
            context.ThrowIfCancellationRequested();
            IReadOnlyCollection<WorkStepProfile> configuredWorkSteps = await Task.Run(WorkStepConfigurationStore.Load)
                .ConfigureAwait(false);
            WorkStepProfile? workStep = configuredWorkSteps.FirstOrDefault(item =>
                string.Equals(item.Name?.Trim(), normalizedWorkStepName, StringComparison.OrdinalIgnoreCase));
            if (workStep is null)
            {
                throw new InvalidOperationException($"未找到工步：{normalizedWorkStepName}。");
            }

            if (workStep.Operations.Count == 0)
            {
                throw new InvalidOperationException($"工步“{normalizedWorkStepName}”没有可执行操作。");
            }

            SchemeWorkStepItem schemeStep = CurrentSchemeStep.Value ?? CreateStandaloneSchemeStep();
            Dictionary<string, string> returnValues = new(StringComparer.OrdinalIgnoreCase);
            foreach (WorkStepOperation operation in workStep.Operations)
            {
                await context.WaitIfPausedAsync().ConfigureAwait(false);
                context.ThrowIfCancellationRequested();

                SchemeStepExecutionOutput output = await ExecuteStandaloneStepAsync(
                        context,
                        operation,
                        returnValues,
                        schemeStep)
                    .ConfigureAwait(false);
                if (!output.IsSuccess)
                {
                    throw new InvalidOperationException(
                        $"工步“{normalizedWorkStepName}”执行失败：{output.Message}");
                }
            }
        }
        finally
        {
            ActiveNamedWorkSteps.Value = previousWorkSteps;
        }
    }

    /// <summary>根据操作对象类型将步骤分派到 Lua、判断、业务目录或设备通信执行器。</summary>
    private static async Task<SchemeStepExecutionOutput> ExecuteOperationAsync(
        IControlledExecutionContext context,
        WorkStepOperation operation,
        SchemeWorkStepItem schemeStep,
        IReadOnlyDictionary<string, string> returnValues)
    {
        return operation.OperationObjectName switch
        {
            "Lua" => ExecuteLua(operation, schemeStep, returnValues),
            "System" => await ExecuteCatalogBusinessOperationAsync(context,operation,schemeStep,returnValues,"System",operation.PCommandName?.Trim() ?? string.Empty).ConfigureAwait(false),
            "判断" => ExecuteJudge(operation, schemeStep, returnValues),
            _ => TryResolveBusinessOperation(operation, out string deviceId, out string operationId)
                ? await ExecuteCatalogBusinessOperationAsync(context, operation, schemeStep, returnValues, deviceId, operationId).ConfigureAwait(false)
                : await ExecuteDeviceOperationAsync(context, operation, schemeStep, returnValues).ConfigureAwait(false)
        };
    }

    /// <summary>将输入参数注入 Lua 脚本前缀，执行脚本并返回脚本结果。</summary>
    private static SchemeStepExecutionOutput ExecuteLua(
        WorkStepOperation operation,
        SchemeWorkStepItem schemeStep,
        IReadOnlyDictionary<string, string> returnValues)
    {
        string parameterAssignments = string.Join(
            Environment.NewLine,
            operation.Parameters
                .OrderBy(parameter => parameter.Num)
                .Where(parameter => !string.IsNullOrWhiteSpace(parameter.ParameterName))
                .Select(parameter =>
                    $"{parameter.ParameterName.Trim()}=\"{ResolveParameterValue(parameter, schemeStep, returnValues)}\""));
        string script = string.IsNullOrEmpty(parameterAssignments)
            ? operation.LuaScript ?? string.Empty
            : $"{parameterAssignments}{Environment.NewLine}{operation.LuaScript}";

        object[] results = new LuaManage().DoString(script);
        if (results.Length == 1 && results[0] is Exception ex)
        {
            return SchemeStepExecutionOutput.Failure(ex.Message, ex);
        }

        string resultText = string.Join(", ", results.Select(item => item?.ToString() ?? string.Empty));
        return SchemeStepExecutionOutput.Success(resultText);
    }

    /// <summary>尝试从业务操作目录解析当前步骤对应的业务方法。</summary>
    private static bool TryResolveBusinessOperation(
        WorkStepOperation operation,
        out string deviceId,
        out string operationId)
    {
        deviceId = BusinessOperationBindingResolver.ResolveCatalogDeviceId(
            operation.OperationObjectName);
        operationId = operation.PCommandName?.Trim() ?? string.Empty;

        return BusinessOperationCatalog.Find(deviceId, operationId) is not null;
    }

    /// <summary>调用业务操作目录中已注册的方法，并转换为统一的步骤执行结果。</summary>
    private static async Task<SchemeStepExecutionOutput> ExecuteCatalogBusinessOperationAsync(
        IControlledExecutionContext context,
        WorkStepOperation operation,
        SchemeWorkStepItem schemeStep,
        IReadOnlyDictionary<string, string> returnValues,
        string deviceId,
        string operationId)
    {
        Dictionary<string, string> parameterValues = operation.Parameters
            .OrderBy(parameter => parameter.Num)
            .Where(parameter => !string.IsNullOrWhiteSpace(parameter.ParameterName))
            .GroupBy(parameter => parameter.ParameterName.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => ResolveParameterValue(group.First(), schemeStep, returnValues),
                StringComparer.OrdinalIgnoreCase);
        string? operationObjectName = string.IsNullOrWhiteSpace(operation.OperationObjectName)
            ? null
            : operation.OperationObjectName.Trim();

        IControlledExecutionContext? previousContext = CurrentExecutionContext.Value;
        SchemeWorkStepItem? previousSchemeStep = CurrentSchemeStep.Value;
        CurrentExecutionContext.Value = context;
        CurrentSchemeStep.Value = schemeStep;
        BusinessOperationInvocationResult result;
        try
        {
            result = await BusinessOperationInvoker
                .InvokeAsync(deviceId, operationId, parameterValues, operationObjectName, context.CancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            CurrentExecutionContext.Value = previousContext;
            CurrentSchemeStep.Value = previousSchemeStep;
        }

        context.ThrowIfCancellationRequested();

        return result.IsSuccess
            ? SchemeStepExecutionOutput.Success(result.Result)
            : SchemeStepExecutionOutput.Failure(result.Message, result.Result);
    }

    /// <summary>根据判断操作的关系符和参数计算布尔结果。</summary>
    private static SchemeStepExecutionOutput ExecuteJudge(
        WorkStepOperation operation,
        SchemeWorkStepItem schemeStep,
        IReadOnlyDictionary<string, string> returnValues)
    {
        List<string> values = operation.Parameters
            .OrderBy(parameter => parameter.Num)
            .Select(parameter => ResolveParameterValue(parameter, schemeStep, returnValues))
            .ToList();

        string methodName = operation.PCommandName?.Trim() ?? string.Empty;
        bool result = methodName switch
        {
            "=" => TextEquals(GetValue(values, 0), GetValue(values, 1)),
            "≠" => !TextEquals(GetValue(values, 0), GetValue(values, 1)),
            ">" => CompareNumbers(GetValue(values, 0), GetValue(values, 1)) > 0,
            "≥" => CompareNumbers(GetValue(values, 0), GetValue(values, 1)) >= 0,
            "<" => CompareNumbers(GetValue(values, 0), GetValue(values, 1)) < 0,
            "≤" => CompareNumbers(GetValue(values, 0), GetValue(values, 1)) <= 0,
            "∈" => CompareNumbers(GetValue(values, 0), GetValue(values, 1)) >= 0 && CompareNumbers(GetValue(values, 0), GetValue(values, 2)) <= 0,
            "∉" => CompareNumbers(GetValue(values, 0), GetValue(values, 1)) < 0 || CompareNumbers(GetValue(values, 0), GetValue(values, 2)) > 0,
            "∋" => GetValue(values, 0).IndexOf(GetValue(values, 1), StringComparison.OrdinalIgnoreCase) >= 0,
            "∌" => GetValue(values, 0).IndexOf(GetValue(values, 1), StringComparison.OrdinalIgnoreCase) < 0,
            "= ∅" => string.IsNullOrWhiteSpace(GetValue(values, 0)),
            "≠ ∅" => !string.IsNullOrWhiteSpace(GetValue(values, 0)),
            _ => false
        };

        return result
            ? SchemeStepExecutionOutput.Success(true)
            : SchemeStepExecutionOutput.Failure($"Judge method '{methodName}' returned false.", false);
    }

    /// <summary>构建设备报文并通过通信中介器执行设备读写操作。</summary>
    private static async Task<SchemeStepExecutionOutput> ExecuteDeviceOperationAsync(
        IControlledExecutionContext context,
        WorkStepOperation operation,
        SchemeWorkStepItem schemeStep,
        IReadOnlyDictionary<string, string> returnValues)
    {
        string communicationName = operation.OperationObjectName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(communicationName))
        {
            return SchemeStepExecutionOutput.Failure("Communication name is required.");
        }

        if (_mediator is null)
        {
            return SchemeStepExecutionOutput.Failure("Device communication mediator is not configured.");
        }

        string message = BuildDeviceMessage(operation, schemeStep, returnValues);
        SendReceiveModel send = new(message);
        DeviceExecutionActionResult result = await _mediator
            .Send(new SendDeviceDataRequest(communicationName, send), context.CancellationToken)
            .ConfigureAwait(false);
        context.ThrowIfCancellationRequested();

        return result.IsSuccess
            ? SchemeStepExecutionOutput.Success(result.Result ?? string.Empty)
            : SchemeStepExecutionOutput.Failure(
                string.IsNullOrWhiteSpace(result.Message)
                    ? $"Communication '{communicationName}' write failed."
                    : result.Message,
                result.Result);
    }

    /// <summary>根据协议命令、步骤参数和校验配置生成最终设备报文。</summary>
    private static string BuildDeviceMessage(
        WorkStepOperation operation,
        SchemeWorkStepItem schemeStep,
        IReadOnlyDictionary<string, string> returnValues)
    {
        Dictionary<string, string> parameterValues = operation.Parameters
            .OrderBy(parameter => parameter.Num)
            .Where(parameter => !string.IsNullOrWhiteSpace(parameter.ParameterName))
            .GroupBy(parameter => parameter.ParameterName.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => ResolveParameterValue(group.First(), schemeStep, returnValues),
                StringComparer.OrdinalIgnoreCase);

        return PlaceholderRegex.Replace(operation.PCommandName ?? string.Empty, match =>
        {
            string name = match.Groups["name"].Value.Trim();
            return parameterValues.TryGetValue(name, out string? value) ? value : match.Value;
        });
    }

    #endregion

    #region 步骤参数解析与返回值处理

    /// <summary>根据参数类型从设置值、工步值、返回值或全局值中解析实际值。</summary>
    private static string ResolveParameterValue(
        InputParameter parameter,
        SchemeWorkStepItem schemeStep,
        IReadOnlyDictionary<string, string> returnValues)
    {
        string type = parameter.ParameterType?.Trim() ?? string.Empty;
        string value = parameter.Value?.Trim() ?? string.Empty;

        return type switch
        {
            "设置值" => value,
            "工步值" => ResolveSchemeStepParameterValue(parameter, schemeStep),
            "返回值" => returnValues.TryGetValue(value, out string? returnValue) ? returnValue : string.Empty,
            "全局值" => string.Empty,
            _ => value
        };
    }

    /// <summary>从当前方案工步的输入参数中解析指定工步值。</summary>
    private static string ResolveSchemeStepParameterValue(
        InputParameter parameter,
        SchemeWorkStepItem schemeStep)
    {
        string parameterName = parameter.Value?.Trim() ?? string.Empty;
        return schemeStep.InputParameters.FirstOrDefault(item => string.Equals(
                   item.Name?.Trim(),
                   parameterName,
                   StringComparison.OrdinalIgnoreCase))?.Value?.Trim()
               ?? parameter.ParameterName?.Trim()
               ?? string.Empty;
    }

    #endregion

    #region 独立步骤与执行控制工具

    /// <summary>执行可响应暂停与取消请求的分段延时。</summary>
    private static async Task DelayWithControlAsync(IControlledExecutionContext context, int delayMilliseconds)
    {
        int remaining = Math.Max(0, delayMilliseconds);
        while (remaining > 0)
        {
            await context.WaitIfPausedAsync().ConfigureAwait(false);
            context.ThrowIfCancellationRequested();

            int delay = Math.Min(remaining, ControlPollingIntervalMilliseconds);
            await Task.Delay(delay, context.CancellationToken).ConfigureAwait(false);
            remaining -= delay;
        }
    }

    /// <summary>创建流程图独立步骤执行时使用的临时工步上下文。</summary>
    private static SchemeWorkStepItem CreateStandaloneSchemeStep()
    {
        return new SchemeWorkStepItem
        {
            StepName = "临时步骤"
        };
    }

    /// <summary>获取指定工位当前运行的方案上下文，支持按规范化工位编号查找。</summary>
    private static bool TryGetStationContexts(
        string stationNo,
        out List<SchemeExecutionContext> contexts,
        out string message)
    {
        string normalizedStationNo = NormalizeRequiredText(stationNo);
        if (string.IsNullOrWhiteSpace(normalizedStationNo))
        {
            contexts = new List<SchemeExecutionContext>();
            message = "Station number is required.";
            return false;
        }

        if (!ActiveExecutions.TryGetValue(normalizedStationNo, out SchemeExecutionContext? context))
        {
            contexts = new List<SchemeExecutionContext>();
            message = $"No schemes are running on station '{normalizedStationNo}'.";
            return false;
        }

        contexts = new List<SchemeExecutionContext> { context };
        message = string.Empty;
        return true;
    }

    #endregion

    #region 操作类型、事件与通用工具
    /// <summary>安全触发方案执行生命周期事件。</summary>
    private static void Raise(EventHandler<SchemeExecutionEventArgs>? handler, SchemeExecutionEventArgs args)
    {
        handler?.Invoke(null, args);
    }
    /// <summary>发布方案级执行状态，供业务界面同步当前测试状态。</summary>
    private static void PublishSchemeStatus(
        string stationNo,
        string testStatus,
        SchemeProfile scheme,
        string message,
        bool? isSuccess = null)
    {
        PublishTestStatus(
            stationNo,
            testStatus,
            string.Empty,
            scheme.SchemeName,
            string.Empty,
            message,
            isSuccess);
    }
    /// <summary>向事件聚合器发布统一的测试状态消息。</summary>
    private static void PublishTestStatus(
        string stationNo,
        string testStatus,
        string productBarcode,
        string schemeName,
        string productName,
        string message,
        bool? isSuccess = null)
    {
        try
        {
            _eventAggregator?
                .GetEvent<TestExecutionStatusChangedEvent>()
                .Publish(new TestExecutionStatusMessage(
                    stationNo,
                    testStatus,
                    productBarcode,
                    schemeName,
                    productName,
                    message,
                    isSuccess));
        }
        catch
        {
            // Status publication must not interrupt the actual test execution.
        }
    }

    /// <summary>发布暂停、继续或停止操作产生的控制状态。</summary>
    private static void PublishExecutionControlStatus(
        string stationNo,
        string testStatus,
        string schemeName,
        string message)
    {
        PublishTestStatus(
            stationNo,
            testStatus,
            string.Empty,
            schemeName,
            string.Empty,
            message);
    }

    /// <summary>将两个文本按十进制数值进行比较。</summary>
    private static int CompareNumbers(string left, string right)
    {
        decimal leftValue = decimal.TryParse(left, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal parsedLeft)
            ? parsedLeft
            : 0m;
        decimal rightValue = decimal.TryParse(right, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal parsedRight)
            ? parsedRight
            : 0m;
        return leftValue.CompareTo(rightValue);
    }

    /// <summary>安全读取指定索引的判断参数，不存在时返回空字符串。</summary>
    private static string GetValue(IReadOnlyList<string> values, int index)
    {
        return index >= 0 && index < values.Count ? values[index] : string.Empty;
    }

    /// <summary>忽略首尾空白和大小写比较两个文本。</summary>
    private static bool TextEquals(string? left, string? right)
    {
        return string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>规范化必填文本，空值统一转换为空字符串。</summary>
    private static string NormalizeRequiredText(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }

    #endregion

    #region 内部执行上下文

    /// <summary>用于标识工位方案执行实例的复合键。</summary>
    private sealed class SchemeExecutionKey
    {
        /// <summary>创建方案执行键。</summary>
        public SchemeExecutionKey(string stationNo, string schemeName)
        {
            StationNo = stationNo;
            SchemeName = schemeName;
        }

        /// <summary>获取工位编号。</summary>
        public string StationNo { get; }

        /// <summary>获取方案名称。</summary>
        public string SchemeName { get; }

    }

    /// <summary>保存单次方案运行所需的控制状态、取消令牌和执行日志。</summary>
    private sealed class SchemeExecutionContext : IControlledExecutionContext, IDisposable
    {
        /// <summary>保护暂停状态和恢复信号的同步锁。</summary>
        private readonly object _pauseLock = new();
        /// <summary>暂停期间用于等待继续命令的异步信号。</summary>
        private TaskCompletionSource<bool>? _resumeSignal;
        /// <summary>指示当前执行是否处于暂停状态。</summary>
        private bool _isPaused;

        /// <summary>创建方案执行上下文。</summary>
        public SchemeExecutionContext(SchemeExecutionKey key, DateTime startTime)
        {
            Key = key;
            StartTime = startTime;
        }

        /// <summary>获取当前方案执行键。</summary>
        public SchemeExecutionKey Key { get; }

        /// <summary>获取方案开始执行时间。</summary>
        public DateTime StartTime { get; }

        /// <summary>获取用于终止执行的取消令牌源。</summary>
        public CancellationTokenSource CancellationTokenSource { get; } = new();

        /// <summary>获取当前执行的取消令牌。</summary>
        public CancellationToken CancellationToken => CancellationTokenSource.Token;

        /// <summary>获取本次方案执行产生的日志集合。</summary>
        public List<string> Logs { get; } = new();

        /// <summary>获取当前是否处于暂停状态。</summary>
        public bool IsPaused
        {
            get
            {
                lock (_pauseLock)
                {
                    return _isPaused;
                }
            }
        }

        /// <summary>将当前执行切换为暂停状态。</summary>
        public bool Pause()
        {
            lock (_pauseLock)
            {
                if (_isPaused)
                {
                    return false;
                }

                _isPaused = true;
                _resumeSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                return true;
            }
        }

        /// <summary>恢复已经暂停的方案执行。</summary>
        public bool Resume()
        {
            TaskCompletionSource<bool>? resumeSignal;
            lock (_pauseLock)
            {
                if (!_isPaused)
                {
                    return false;
                }

                _isPaused = false;
                resumeSignal = _resumeSignal;
                _resumeSignal = null;
            }

            resumeSignal?.TrySetResult(true);
            return true;
        }

        /// <summary>请求停止当前方案执行。</summary>
        public void Stop()
        {
            CancellationTokenSource.Cancel();
            Resume();
        }

        /// <summary>暂停时异步等待继续信号，同时响应取消请求。</summary>
        public async Task WaitIfPausedAsync()
        {
            while (true)
            {
                TaskCompletionSource<bool>? resumeSignal;
                lock (_pauseLock)
                {
                    if (!_isPaused)
                    {
                        return;
                    }

                    resumeSignal = _resumeSignal;
                }

                if (resumeSignal is null)
                {
                    return;
                }

                await resumeSignal.Task.WaitAsync(CancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>检查当前执行是否已收到取消请求。</summary>
        public void ThrowIfCancellationRequested()
        {
            CancellationToken.ThrowIfCancellationRequested();
        }

        /// <summary>追加一条方案执行日志。</summary>
        public void AddLog(string message)
        {
            lock (Logs)
            {
                Logs.Add(message);
            }
        }

        /// <summary>创建当前运行状态的只读快照。</summary>
        public SchemeExecutionSnapshot CreateSnapshot()
        {
            DateTime snapshotTime = DateTime.Now;
            return new SchemeExecutionSnapshot(
                Key.StationNo,
                Key.SchemeName,
                IsPaused,
                StartTime,
                null,
                snapshotTime - StartTime,
                Logs.ToList());
        }

        /// <summary>释放执行上下文持有的取消令牌资源。</summary>
        public void Dispose()
        {
            CancellationTokenSource.Dispose();
        }
    }

    #endregion
}

#region 执行结果与事件模型

/// <summary>方案、工步和步骤生命周期事件共用的事件参数。</summary>
public sealed class SchemeExecutionEventArgs : EventArgs
{
    /// <summary>创建方案执行事件参数。</summary>
    private SchemeExecutionEventArgs(
        string stationNo,
        string schemeName,
        string? workStepName,
        string? stepName,
        int workStepIndex,
        int stepIndex,
        bool? isSuccess,
        string message,
        object? result,
        DateTime? startTime,
        DateTime? endTime)
    {
        StationNo = stationNo;
        SchemeName = schemeName;
        WorkStepName = workStepName ?? string.Empty;
        StepName = stepName ?? string.Empty;
        WorkStepIndex = workStepIndex;
        StepIndex = stepIndex;
        IsSuccess = isSuccess;
        Message = message;
        Result = result;
        StartTime = startTime;
        EndTime = endTime;
        ExecutionTime = startTime.HasValue && endTime.HasValue
            ? endTime.Value - startTime.Value
            : null;
    }

    /// <summary>获取工位编号。</summary>
    public string StationNo { get; }

    /// <summary>获取方案名称。</summary>
    public string SchemeName { get; }

    /// <summary>获取工步名称。</summary>
    public string WorkStepName { get; }

    /// <summary>获取步骤名称。</summary>
    public string StepName { get; }

    /// <summary>获取工步序号。</summary>
    public int WorkStepIndex { get; }

    /// <summary>获取步骤序号。</summary>
    public int StepIndex { get; }

    /// <summary>获取执行是否成功；执行前或执行中事件可能为空。</summary>
    public bool? IsSuccess { get; }

    /// <summary>获取执行状态消息。</summary>
    public string Message { get; }

    /// <summary>获取步骤返回结果。</summary>
    public object? Result { get; }

    /// <summary>获取执行开始时间。</summary>
    public DateTime? StartTime { get; }

    /// <summary>获取执行结束时间。</summary>
    public DateTime? EndTime { get; }

    /// <summary>获取本次执行耗时。</summary>
    public TimeSpan? ExecutionTime { get; }

    /// <summary>获取或设置订阅方是否取消后续执行。</summary>
    public bool Cancel { get; set; }

    /// <summary>创建方案级生命周期事件参数。</summary>
    internal static SchemeExecutionEventArgs CreateScheme(
        string stationNo,
        SchemeProfile scheme,
        bool? isSuccess = null,
        string message = "",
        DateTime? startTime = null,
        DateTime? endTime = null)
    {
        return new SchemeExecutionEventArgs(
            stationNo,
            scheme.SchemeName,
            null,
            null,
            0,
            0,
            isSuccess,
            message,
            null,
            startTime,
            endTime);
    }

    /// <summary>创建工步级生命周期事件参数。</summary>
    internal static SchemeExecutionEventArgs CreateWorkStep(
        string stationNo,
        SchemeProfile scheme,
        SchemeWorkStepItem schemeStep,
        int workStepIndex,
        bool? isSuccess = null,
        string message = "",
        DateTime? startTime = null,
        DateTime? endTime = null)
    {
        return new SchemeExecutionEventArgs(
            stationNo,
            scheme.SchemeName,
            schemeStep.StepName,
            null,
            workStepIndex,
            0,
            isSuccess,
            message,
            null,
            startTime,
            endTime);
    }

    /// <summary>创建步骤级生命周期事件参数。</summary>
    internal static SchemeExecutionEventArgs CreateStep(
        string stationNo,
        SchemeProfile scheme,
        SchemeWorkStepItem schemeStep,
        WorkStepOperation operation,
        int workStepIndex,
        int stepIndex,
        bool? isSuccess = null,
        string message = "",
        object? result = null,
        DateTime? startTime = null,
        DateTime? endTime = null)
    {
        return new SchemeExecutionEventArgs(
            stationNo,
            scheme.SchemeName,
            schemeStep.StepName,
            operation.Summary,
            workStepIndex,
            stepIndex,
            isSuccess,
            message,
            result,
            startTime,
            endTime);
    }
}

/// <summary>方案或工步执行完成后的统一结果。</summary>
public sealed class SchemeExecutionResult
{
    /// <summary>创建方案执行结果。</summary>
    public SchemeExecutionResult(
        bool isSuccess,
        bool isCanceled,
        string message,
        IReadOnlyList<string>? steps = null,
        DateTime? startTime = null,
        DateTime? endTime = null)
    {
        IsSuccess = isSuccess;
        IsCanceled = isCanceled;
        Message = message ?? string.Empty;
        Steps = steps ?? Array.Empty<string>();
        StartTime = startTime;
        EndTime = endTime;
        ExecutionTime = startTime.HasValue && endTime.HasValue
            ? endTime.Value - startTime.Value
            : null;
    }

    /// <summary>获取执行是否成功。</summary>
    public bool IsSuccess { get; }

    /// <summary>获取执行是否被取消。</summary>
    public bool IsCanceled { get; }

    /// <summary>获取结果说明。</summary>
    public string Message { get; }

    /// <summary>获取执行日志或步骤信息。</summary>
    public IReadOnlyList<string> Steps { get; }

    /// <summary>获取开始时间。</summary>
    public DateTime? StartTime { get; }

    /// <summary>获取结束时间。</summary>
    public DateTime? EndTime { get; }

    /// <summary>获取执行耗时。</summary>
    public TimeSpan? ExecutionTime { get; }

    /// <summary>创建成功的方案执行结果。</summary>
    public static SchemeExecutionResult CreateSuccess(
        string message,
        IReadOnlyList<string>? steps = null,
        DateTime? startTime = null,
        DateTime? endTime = null)
    {
        return new SchemeExecutionResult(true, false, message, steps, startTime, endTime);
    }

    /// <summary>创建失败的方案执行结果。</summary>
    public static SchemeExecutionResult CreateFailure(
        string message,
        IReadOnlyList<string>? steps = null,
        DateTime? startTime = null,
        DateTime? endTime = null)
    {
        return new SchemeExecutionResult(false, false, message, steps, startTime, endTime);
    }

    /// <summary>创建已取消的方案执行结果。</summary>
    public static SchemeExecutionResult CreateCanceled(
        string message,
        IReadOnlyList<string>? steps = null,
        DateTime? startTime = null,
        DateTime? endTime = null)
    {
        return new SchemeExecutionResult(false, true, message, steps, startTime, endTime);
    }
}

/// <summary>暂停、继续和停止等执行控制命令的结果。</summary>
public sealed class SchemeExecutionControlActionResult
{
    /// <summary>创建执行控制结果。</summary>
    public SchemeExecutionControlActionResult(bool isSuccess, string message)
    {
        IsSuccess = isSuccess;
        Message = message ?? string.Empty;
    }

    /// <summary>获取控制命令是否执行成功。</summary>
    public bool IsSuccess { get; }

    /// <summary>获取控制结果说明。</summary>
    public string Message { get; }

    /// <summary>创建成功的执行控制结果。</summary>
    public static SchemeExecutionControlActionResult CreateSuccess(string message)
    {
        return new SchemeExecutionControlActionResult(true, message);
    }

    /// <summary>创建失败的执行控制结果。</summary>
    public static SchemeExecutionControlActionResult CreateFailure(string message)
    {
        return new SchemeExecutionControlActionResult(false, message);
    }
}

/// <summary>描述当前工位方案运行状态的只读快照。</summary>
public sealed record SchemeExecutionSnapshot(
    string StationNo,
    string SchemeName,
    bool IsPaused,
    DateTime StartTime,
    DateTime? EndTime,
    TimeSpan ExecutionTime,
    IReadOnlyList<string> Steps);

/// <summary>为步骤执行提供暂停、取消和日志能力的受控执行上下文。</summary>
internal interface IControlledExecutionContext
{
    /// <summary>获取当前执行的取消令牌。</summary>
    CancellationToken CancellationToken { get; }

    /// <summary>若执行已暂停，则异步等待恢复。</summary>
    Task WaitIfPausedAsync();

    /// <summary>检查并抛出当前执行的取消请求。</summary>
    void ThrowIfCancellationRequested();
}

/// <summary>单个步骤操作执行后的内部统一输出。</summary>
internal sealed class SchemeStepExecutionOutput
{
    /// <summary>创建步骤执行输出。</summary>
    private SchemeStepExecutionOutput(bool isSuccess, string message, object? result)
    {
        IsSuccess = isSuccess;
        Message = message ?? string.Empty;
        Result = result;
    }

    /// <summary>获取步骤操作是否成功。</summary>
    public bool IsSuccess { get; }

    /// <summary>获取步骤操作结果说明。</summary>
    public string Message { get; }

    /// <summary>获取步骤操作返回值。</summary>
    public object? Result { get; }

    /// <summary>创建成功的步骤执行输出。</summary>
    public static SchemeStepExecutionOutput Success(object? result)
    {
        return new SchemeStepExecutionOutput(true, string.Empty, result);
    }

    /// <summary>创建失败的步骤执行输出。</summary>
    public static SchemeStepExecutionOutput Failure(string message, object? result = null)
    {
        return new SchemeStepExecutionOutput(false, message, result);
    }
}

#endregion
