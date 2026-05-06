using Module.Business.Models;
using Shared.Abstractions;
using Shared.Infrastructure.Communication;
using Shared.Infrastructure.Extensions;
using Shared.Infrastructure.Lua;
using Shared.Models.Communication;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Module.Business.Services;

public static class SchemeExecutionService
{
    #region 常量与运行状态字段

    // 操作对象名称与控制轮询周期，统一放在这里避免散落魔法值。
    private const string SystemOperationObjectName = "System";
    private const string LuaOperationObjectName = "Lua";
    private const string JudgeOperationObjectName = "\u5224\u65AD";
    private const int ControlPollingIntervalMilliseconds = 50;

    private static readonly Regex PlaceholderRegex =
        new(@"\{\{\s*(?<name>[^{}\r\n]+?)\s*\}\}", RegexOptions.Compiled);

    private static readonly ConcurrentDictionary<string, SchemeExecutionContext> ActiveExecutions =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, string> GlobalValues = new(StringComparer.OrdinalIgnoreCase);

    #endregion

    #region 执行生命周期事件

    /// <summary>
    /// 方案执行前事件，可通过 <see cref="SchemeExecutionEventArgs.Cancel"/> 取消执行。
    /// </summary>
    public static event EventHandler<SchemeExecutionEventArgs>? BeforeSchemeExecuting;

    /// <summary>
    /// 方案执行中事件，在方案进入主执行循环后触发。
    /// </summary>
    public static event EventHandler<SchemeExecutionEventArgs>? SchemeExecuting;

    /// <summary>
    /// 方案执行后事件，包含执行开始时间、结束时间和耗时。
    /// </summary>
    public static event EventHandler<SchemeExecutionEventArgs>? AfterSchemeExecuted;

    /// <summary>
    /// 工步执行前事件，可通过 <see cref="SchemeExecutionEventArgs.Cancel"/> 取消执行。
    /// </summary>
    public static event EventHandler<SchemeExecutionEventArgs>? BeforeWorkStepExecuting;

    /// <summary>
    /// 工步执行中事件，在工步内步骤开始执行前触发。
    /// </summary>
    public static event EventHandler<SchemeExecutionEventArgs>? WorkStepExecuting;

    /// <summary>
    /// 工步执行后事件，包含执行开始时间、结束时间和耗时。
    /// </summary>
    public static event EventHandler<SchemeExecutionEventArgs>? AfterWorkStepExecuted;

    /// <summary>
    /// 步骤执行前事件，可通过 <see cref="SchemeExecutionEventArgs.Cancel"/> 取消执行。
    /// </summary>
    public static event EventHandler<SchemeExecutionEventArgs>? BeforeStepExecuting;

    /// <summary>
    /// 步骤执行中事件，在单条操作正式调用前触发。
    /// </summary>
    public static event EventHandler<SchemeExecutionEventArgs>? StepExecuting;

    /// <summary>
    /// 步骤执行后事件，包含执行开始时间、结束时间、耗时和执行结果。
    /// </summary>
    public static event EventHandler<SchemeExecutionEventArgs>? AfterStepExecuted;

    #endregion

    #region 对外执行与控制入口

    /// <summary>
    /// 根据工位号和方案名称读取方案文件并执行；同一工位同一时间只允许一个方案执行实例。
    /// </summary>
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

        SchemeExecutionKey key = new(normalizedStationNo, normalizedSchemeName);
        SchemeExecutionContext context = new(key, startTime);
        if (!ActiveExecutions.TryAdd(normalizedStationNo, context))
        {
            string runningSchemeName = ActiveExecutions.TryGetValue(normalizedStationNo, out SchemeExecutionContext? runningContext)
                ? runningContext.Key.SchemeName
                : string.Empty;
            context.Dispose();
            return SchemeExecutionResult.CreateFailure(
                string.IsNullOrWhiteSpace(runningSchemeName)
                    ? $"Station '{normalizedStationNo}' already has a running scheme."
                    : $"Station '{normalizedStationNo}' is already running scheme '{runningSchemeName}'.",
                startTime: startTime,
                endTime: DateTime.Now);
        }

        try
        {
            BusinessConfigurationCatalog catalog = BusinessConfigurationStore.LoadCatalog();
            SchemeProfile? scheme = catalog.Schemes.FirstOrDefault(item =>
                string.Equals(item.SchemeName?.Trim(), normalizedSchemeName, StringComparison.OrdinalIgnoreCase));
            if (scheme is null)
            {
                return SchemeExecutionResult.CreateFailure(
                    $"Scheme '{normalizedSchemeName}' was not found.",
                    startTime: startTime,
                    endTime: DateTime.Now);
            }

            ProductProfile? product = catalog.Products.FirstOrDefault(item =>
                string.Equals(item.ProductName?.Trim(), scheme.ProductName?.Trim(), StringComparison.OrdinalIgnoreCase));

            IReadOnlyDictionary<string, WorkStepProfile> workStepsById = catalog.WorkSteps
                .Where(item => !string.IsNullOrWhiteSpace(item.Id))
                .GroupBy(item => item.Id, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

            return await ExecuteSchemeAsync(context, scheme.Clone(), product?.Clone(), workStepsById)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return SchemeExecutionResult.CreateCanceled(
                $"Scheme '{normalizedSchemeName}' on station '{normalizedStationNo}' was stopped.",
                context.Logs,
                startTime,
                DateTime.Now);
        }
        catch (Exception ex)
        {
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
    /// <summary>
    /// 暂停指定工位下所有正在运行的方案执行实例。
    /// </summary>
    public static SchemeExecutionControlActionResult Pause(string stationNo)
    {
        if (!TryGetStationContexts(stationNo, out List<SchemeExecutionContext> contexts, out string message))
        {
            return SchemeExecutionControlActionResult.CreateFailure(message);
        }

        string normalizedStationNo = NormalizeRequiredText(stationNo);
        int pausedCount = contexts.Count(context => context.Pause());
        return pausedCount > 0
            ? SchemeExecutionControlActionResult.CreateSuccess(
                $"Station '{normalizedStationNo}' paused {pausedCount}/{contexts.Count} execution(s).")
            : SchemeExecutionControlActionResult.CreateSuccess(
                $"Station '{normalizedStationNo}' executions are already paused.");
    }
    /// <summary>
    /// 继续指定工位下所有已暂停的方案执行实例。
    /// </summary>
    public static SchemeExecutionControlActionResult Continue(string stationNo)
    {
        if (!TryGetStationContexts(stationNo, out List<SchemeExecutionContext> contexts, out string message))
        {
            return SchemeExecutionControlActionResult.CreateFailure(message);
        }

        string normalizedStationNo = NormalizeRequiredText(stationNo);
        int resumedCount = contexts.Count(context => context.Resume());
        return resumedCount > 0
            ? SchemeExecutionControlActionResult.CreateSuccess(
                $"Station '{normalizedStationNo}' resumed {resumedCount}/{contexts.Count} execution(s).")
            : SchemeExecutionControlActionResult.CreateSuccess(
                $"Station '{normalizedStationNo}' executions are not paused.");
    }
    /// <summary>
    /// 停止指定工位下所有正在运行的方案执行实例。
    /// </summary>
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
        return SchemeExecutionControlActionResult.CreateSuccess(
            $"Stop request sent to {contexts.Count} execution(s) on station '{normalizedStationNo}'.");
    }

    /// <summary>
    /// 获取当前正在运行的方案执行快照。
    /// </summary>
    public static IReadOnlyList<SchemeExecutionSnapshot> GetActiveExecutions()
    {
        return ActiveExecutions.Values
            .Select(context => context.CreateSnapshot())
            .OrderBy(item => item.StationNo, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.SchemeName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    #endregion

    #region 方案、工步、步骤执行编排

    private static async Task<SchemeExecutionResult> ExecuteSchemeAsync(
        SchemeExecutionContext context,
        SchemeProfile scheme,
        ProductProfile? product,
        IReadOnlyDictionary<string, WorkStepProfile> workStepsById)
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

        for (int workStepIndex = 0; workStepIndex < scheme.Steps.Count; workStepIndex++)
        {
            await context.WaitIfPausedAsync().ConfigureAwait(false);
            context.ThrowIfCancellationRequested();

            SchemeWorkStepItem schemeStep = scheme.Steps[workStepIndex];
            WorkStepProfile? workStep = ResolveWorkStep(schemeStep, workStepsById);
            if (workStep is null)
            {
                string failureMessage = $"Work step '{schemeStep.SchemeStepName}' was not found.";
                DateTime failedAt = DateTime.Now;
                Raise(AfterSchemeExecuted, SchemeExecutionEventArgs.CreateScheme(
                    context.Key.StationNo,
                    scheme,
                    false,
                    failureMessage,
                    startTime,
                    failedAt));
                return SchemeExecutionResult.CreateFailure(failureMessage, context.Logs, startTime, failedAt);
            }

            SchemeExecutionResult workStepResult = await ExecuteWorkStepAsync(
                    context,
                    scheme,
                    schemeStep,
                    workStep,
                    product,
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
        return SchemeExecutionResult.CreateSuccess(message, context.Logs, startTime, endTime);
    }

    private static async Task<SchemeExecutionResult> ExecuteWorkStepAsync(
        SchemeExecutionContext context,
        SchemeProfile scheme,
        SchemeWorkStepItem schemeStep,
        WorkStepProfile workStep,
        ProductProfile? product,
        int workStepIndex)
    {
        DateTime startTime = DateTime.Now;
        SchemeExecutionEventArgs beforeWorkStepArgs = SchemeExecutionEventArgs.CreateWorkStep(
            context.Key.StationNo,
            scheme,
            schemeStep,
            workStep,
            workStepIndex,
            startTime: startTime);
        Raise(BeforeWorkStepExecuting, beforeWorkStepArgs);
        if (beforeWorkStepArgs.Cancel)
        {
            DateTime canceledAt = DateTime.Now;
            return SchemeExecutionResult.CreateCanceled(
                "Work step execution was canceled before start.",
                context.Logs,
                startTime,
                canceledAt);
        }

        context.AddLog($"Start work step {workStepIndex}: {schemeStep.SchemeStepName}.");
        Raise(WorkStepExecuting, SchemeExecutionEventArgs.CreateWorkStep(
            context.Key.StationNo,
            scheme,
            schemeStep,
            workStep,
            workStepIndex,
            message: "Work step is executing.",
            startTime: startTime));

        Dictionary<string, string> returnValues = new(StringComparer.OrdinalIgnoreCase);
        for (int stepIndex = 0; stepIndex < workStep.Steps.Count; stepIndex++)
        {
            await context.WaitIfPausedAsync().ConfigureAwait(false);
            context.ThrowIfCancellationRequested();

            WorkStepOperation operation = workStep.Steps[stepIndex];
            SchemeExecutionResult stepResult = await ExecuteStepAsync(
                    context,
                    scheme,
                    schemeStep,
                    workStep,
                    operation,
                    product,
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
                    workStep,
                    workStepIndex,
                    false,
                    stepResult.Message,
                    startTime,
                    failedAt));
                return stepResult;
            }
        }

        string message = $"Work step {workStepIndex} finished: {schemeStep.SchemeStepName}.";
        DateTime endTime = DateTime.Now;
        context.AddLog(message);
        Raise(AfterWorkStepExecuted, SchemeExecutionEventArgs.CreateWorkStep(
            context.Key.StationNo,
            scheme,
            schemeStep,
            workStep,
            workStepIndex,
            true,
            message,
            startTime,
            endTime));
        return SchemeExecutionResult.CreateSuccess(message, context.Logs, startTime, endTime);
    }

    private static async Task<SchemeExecutionResult> ExecuteStepAsync(
        SchemeExecutionContext context,
        SchemeProfile scheme,
        SchemeWorkStepItem schemeStep,
        WorkStepProfile workStep,
        WorkStepOperation operation,
        ProductProfile? product,
        Dictionary<string, string> returnValues,
        int workStepIndex,
        int stepIndex)
    {
        DateTime startTime = DateTime.Now;
        SchemeExecutionEventArgs beforeStepArgs = SchemeExecutionEventArgs.CreateStep(
            context.Key.StationNo,
            scheme,
            schemeStep,
            workStep,
            operation,
            workStepIndex,
            stepIndex,
            startTime: startTime);
        Raise(BeforeStepExecuting, beforeStepArgs);
        if (beforeStepArgs.Cancel)
        {
            DateTime canceledAt = DateTime.Now;
            return SchemeExecutionResult.CreateCanceled(
                "Step execution was canceled before start.",
                context.Logs,
                startTime,
                canceledAt);
        }

        context.AddLog($"Start step {workStepIndex}.{stepIndex}: {operation.DisplayText}.");
        Raise(StepExecuting, SchemeExecutionEventArgs.CreateStep(
            context.Key.StationNo,
            scheme,
            schemeStep,
            workStep,
            operation,
            workStepIndex,
            stepIndex,
            message: "Step is executing.",
            startTime: startTime));

        SchemeStepExecutionOutput output;
        try
        {
            output = await ExecuteOperationAsync(context, operation, schemeStep, product, returnValues)
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
        if (!string.IsNullOrWhiteSpace(operation.ReturnValue))
        {
            returnValues[operation.ReturnValue.Trim()] = resultText;
        }

        if (operation.ShowDataToView)
        {
            Module.Business.Business.System.SendDataToView(
                ResolveDisplayDataName(operation),
                operation.ViewJudgeType,
                resultText,
                operation.ViewJudgeCondition);
        }

        if (operation.DelayMilliseconds > 0)
        {
            await DelayWithControlAsync(context, operation.DelayMilliseconds).ConfigureAwait(false);
        }

        string message = output.IsSuccess
            ? $"Step {workStepIndex}.{stepIndex} finished: {operation.DisplayText}."
            : $"Step {workStepIndex}.{stepIndex} failed: {output.Message}";
        DateTime endTime = DateTime.Now;
        context.AddLog(message);
        Raise(AfterStepExecuted, SchemeExecutionEventArgs.CreateStep(
            context.Key.StationNo,
            scheme,
            schemeStep,
            workStep,
            operation,
            workStepIndex,
            stepIndex,
            output.IsSuccess,
            message,
            output.Result,
            startTime,
            endTime));

        return output.IsSuccess
            ? SchemeExecutionResult.CreateSuccess(message, context.Logs, startTime, endTime)
            : SchemeExecutionResult.CreateFailure(message, context.Logs, startTime, endTime);
    }

    #endregion

    #region 单步骤操作执行

    private static async Task<SchemeStepExecutionOutput> ExecuteOperationAsync(
        SchemeExecutionContext context,
        WorkStepOperation operation,
        SchemeWorkStepItem schemeStep,
        ProductProfile? product,
        IReadOnlyDictionary<string, string> returnValues)
    {
        if (IsLuaOperation(operation))
        {
            return ExecuteLua(operation);
        }

        if (IsJudgeOperation(operation))
        {
            return ExecuteJudge(operation, schemeStep, product, returnValues);
        }

        if (IsSystemOperation(operation))
        {
            return await ExecuteSystemMethodAsync(operation, schemeStep, product, returnValues)
                .ConfigureAwait(false);
        }

        return await ExecuteDeviceOperationAsync(context, operation, schemeStep, product, returnValues)
            .ConfigureAwait(false);
    }

    private static SchemeStepExecutionOutput ExecuteLua(WorkStepOperation operation)
    {
        object[] results = new LuaManage().DoString(operation.LuaScript ?? string.Empty);
        if (results.Length == 1 && results[0] is Exception ex)
        {
            return SchemeStepExecutionOutput.Failure(ex.Message, ex);
        }

        string resultText = string.Join(", ", results.Select(item => item?.ToString() ?? string.Empty));
        return SchemeStepExecutionOutput.Success(resultText);
    }

    private static async Task<SchemeStepExecutionOutput> ExecuteSystemMethodAsync(
        WorkStepOperation operation,
        SchemeWorkStepItem schemeStep,
        ProductProfile? product,
        IReadOnlyDictionary<string, string> returnValues)
    {
        string methodName = operation.InvokeMethod?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(methodName) || string.Equals(methodName, "\u7B49\u5F85", StringComparison.OrdinalIgnoreCase))
        {
            return SchemeStepExecutionOutput.Success(string.Empty);
        }

        MethodInfo? method = typeof(Module.Business.Business.System)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(item => string.Equals(item.Name, methodName, StringComparison.OrdinalIgnoreCase));
        if (method is null)
        {
            return SchemeStepExecutionOutput.Failure($"System method '{methodName}' was not found.");
        }

        object?[] args = BuildMethodArguments(method, operation, schemeStep, product, returnValues);
        object? value = method.Invoke(null, args);
        if (value is Task task)
        {
            await task.ConfigureAwait(false);
            value = task.GetType().IsGenericType
                ? task.GetType().GetProperty("Result")?.GetValue(task)
                : null;
        }

        return SchemeStepExecutionOutput.Success(value);
    }

    private static SchemeStepExecutionOutput ExecuteJudge(
        WorkStepOperation operation,
        SchemeWorkStepItem schemeStep,
        ProductProfile? product,
        IReadOnlyDictionary<string, string> returnValues)
    {
        List<string> values = operation.Parameters
            .OrderBy(parameter => parameter.Sequence)
            .Select(parameter => ResolveParameterValue(parameter, schemeStep, product, returnValues))
            .ToList();

        string methodName = operation.InvokeMethod?.Trim() ?? string.Empty;
        bool result = methodName switch
        {
            "\u7B49\u4E8E\u5224\u65AD" => TextEquals(GetValue(values, 0), GetValue(values, 1)),
            "\u4E0D\u7B49\u5224\u65AD" => !TextEquals(GetValue(values, 0), GetValue(values, 1)),
            "\u5927\u4E8E\u5224\u65AD" => CompareNumbers(GetValue(values, 0), GetValue(values, 1)) > 0,
            "\u5927\u4E8E\u7B49\u4E8E\u5224\u65AD" => CompareNumbers(GetValue(values, 0), GetValue(values, 1)) >= 0,
            "\u5C0F\u4E8E\u5224\u65AD" => CompareNumbers(GetValue(values, 0), GetValue(values, 1)) < 0,
            "\u5C0F\u4E8E\u7B49\u4E8E\u5224\u65AD" => CompareNumbers(GetValue(values, 0), GetValue(values, 1)) <= 0,
            "\u5305\u542B\u5224\u65AD" => GetValue(values, 0).IndexOf(GetValue(values, 1), StringComparison.OrdinalIgnoreCase) >= 0,
            "\u4E0D\u5305\u542B\u5224\u65AD" => GetValue(values, 0).IndexOf(GetValue(values, 1), StringComparison.OrdinalIgnoreCase) < 0,
            "\u4E3A\u7A7A\u5224\u65AD" => string.IsNullOrWhiteSpace(GetValue(values, 0)),
            "\u4E0D\u4E3A\u7A7A\u5224\u65AD" => !string.IsNullOrWhiteSpace(GetValue(values, 0)),
            _ => false
        };

        return result
            ? SchemeStepExecutionOutput.Success(true)
            : SchemeStepExecutionOutput.Failure($"Judge method '{methodName}' returned false.", false);
    }

    private static async Task<SchemeStepExecutionOutput> ExecuteDeviceOperationAsync(
        SchemeExecutionContext context,
        WorkStepOperation operation,
        SchemeWorkStepItem schemeStep,
        ProductProfile? product,
        IReadOnlyDictionary<string, string> returnValues)
    {
        string communicationName = operation.OperationObject?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(communicationName))
        {
            return SchemeStepExecutionOutput.Failure("Communication name is required.");
        }

        ICommunication? communication = CommunicationFactory.Get(communicationName);
        if (communication is null)
        {
            return SchemeStepExecutionOutput.Failure($"Communication '{communicationName}' is not running.");
        }

        string message = BuildDeviceMessage(operation, schemeStep, product, returnValues);
        ReadWriteModel readWriteModel = new(message);
        bool result = await communication.WriteAsync(readWriteModel).ConfigureAwait(false);
        context.ThrowIfCancellationRequested();

        return result
            ? SchemeStepExecutionOutput.Success(readWriteModel.Result ?? string.Empty)
            : SchemeStepExecutionOutput.Failure(
                string.IsNullOrWhiteSpace(readWriteModel.Result?.ToString())
                    ? $"Communication '{communicationName}' write failed."
                    : readWriteModel.Result.ToString()!,
                readWriteModel.Result);
    }

    private static string BuildDeviceMessage(
        WorkStepOperation operation,
        SchemeWorkStepItem schemeStep,
        ProductProfile? product,
        IReadOnlyDictionary<string, string> returnValues)
    {
        Dictionary<string, string> parameterValues = operation.Parameters
            .OrderBy(parameter => parameter.Sequence)
            .Where(parameter => !string.IsNullOrWhiteSpace(parameter.ParameterName))
            .GroupBy(parameter => parameter.ParameterName.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => ResolveParameterValue(group.First(), schemeStep, product, returnValues),
                StringComparer.OrdinalIgnoreCase);

        if (TryResolveProtocolCommand(operation.ProtocolName, operation.CommandName, parameterValues, out string message))
        {
            return message;
        }

        string fallback = string.IsNullOrWhiteSpace(operation.CommandName)
            ? operation.InvokeMethod
            : operation.CommandName;
        return PlaceholderRegex.Replace(fallback ?? string.Empty, match =>
        {
            string name = match.Groups["name"].Value.Trim();
            return parameterValues.TryGetValue(name, out string? value) ? value : match.Value;
        });
    }

    #endregion

    #region 协议配置读取与报文生成

    private static bool TryResolveProtocolCommand(
        string protocolName,
        string commandName,
        IReadOnlyDictionary<string, string> parameterValues,
        out string message)
    {
        message = string.Empty;
        if (string.IsNullOrWhiteSpace(protocolName) || string.IsNullOrWhiteSpace(commandName))
        {
            return false;
        }

        JsonElement? command = FindProtocolCommand(protocolName.Trim(), commandName.Trim());
        if (command is null)
        {
            return false;
        }

        string template = GetJsonString(command.Value, "ContentTemplate");
        if (string.IsNullOrWhiteSpace(template))
        {
            return false;
        }

        Dictionary<string, string> values = ParseKeyValueLines(GetJsonString(command.Value, "PlaceholderValuesText"));
        foreach (KeyValuePair<string, string> parameter in parameterValues)
        {
            values[parameter.Key] = parameter.Value;
        }

        string rendered = PlaceholderRegex.Replace(template, match =>
        {
            string name = match.Groups["name"].Value.Trim();
            return values.TryGetValue(name, out string? value) ? value : match.Value;
        });

        string requestFormat = GetJsonString(command.Value, "RequestFormat");
        string crcMode = GetJsonString(command.Value, "CrcMode");
        if (IsHexRequestFormat(requestFormat))
        {
            string normalizedHex = NormalizeHexString(rendered);
            byte[] payloadBytes = normalizedHex.HexStringToByteArray();
            byte[] checksum = BuildChecksum(payloadBytes, crcMode);
            message = "0x" + payloadBytes.Concat(checksum).ToArray().ByteArrayToHexString();
            return true;
        }

        message = rendered;
        return true;
    }

    private static JsonElement? FindProtocolCommand(string protocolName, string commandName)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Config", "Protocol");
        if (!Directory.Exists(directory))
        {
            return null;
        }

        foreach (string filePath in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(ReadPossiblyEncryptedText(filePath));
                JsonElement root = document.RootElement;
                if (!string.Equals(GetJsonString(root, "Name"), protocolName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (root.TryGetProperty("Commands", out JsonElement commandsElement) &&
                    commandsElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement commandElement in commandsElement.EnumerateArray())
                    {
                        if (string.Equals(GetJsonString(commandElement, "Name"), commandName, StringComparison.OrdinalIgnoreCase))
                        {
                            return commandElement.Clone();
                        }
                    }
                }

                if (string.Equals(GetJsonString(root, "CommandName"), commandName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(commandName, "\u6307\u4EE4 1", StringComparison.OrdinalIgnoreCase))
                {
                    return root.Clone();
                }
            }
            catch
            {
                // Ignore broken protocol files during runtime lookup.
            }
        }

        return null;
    }

    #endregion

    #region 系统方法参数与运行值解析

    private static object?[] BuildMethodArguments(
        MethodInfo method,
        WorkStepOperation operation,
        SchemeWorkStepItem schemeStep,
        ProductProfile? product,
        IReadOnlyDictionary<string, string> returnValues)
    {
        Dictionary<string, WorkStepOperationParameter> configuredParameters = operation.Parameters
            .Where(parameter => !string.IsNullOrWhiteSpace(parameter.ParameterName))
            .GroupBy(parameter => parameter.ParameterName.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        List<WorkStepOperationParameter> orderedParameters = operation.Parameters
            .OrderBy(parameter => parameter.Sequence)
            .ToList();

        ParameterInfo[] methodParameters = method.GetParameters();
        object?[] args = new object?[methodParameters.Length];
        for (int index = 0; index < methodParameters.Length; index++)
        {
            ParameterInfo parameterInfo = methodParameters[index];
            WorkStepOperationParameter? configuredParameter =
                configuredParameters.TryGetValue(parameterInfo.Name ?? string.Empty, out WorkStepOperationParameter? matched)
                    ? matched
                    : orderedParameters.ElementAtOrDefault(index);
            string value = configuredParameter is null
                ? string.Empty
                : ResolveParameterValue(configuredParameter, schemeStep, product, returnValues);
            args[index] = ConvertToParameterType(value, parameterInfo.ParameterType);
        }

        return args;
    }

    private static string ResolveParameterValue(
        WorkStepOperationParameter parameter,
        SchemeWorkStepItem schemeStep,
        ProductProfile? product,
        IReadOnlyDictionary<string, string> returnValues)
    {
        string type = parameter.Type?.Trim() ?? string.Empty;
        string value = parameter.Value?.Trim() ?? string.Empty;

        return type switch
        {
            "\u5DE5\u6B65\u503C" => ResolveSchemeStepParameterValue(parameter, schemeStep),
            "\u8FD4\u56DE\u503C" => returnValues.TryGetValue(value, out string? returnValue) ? returnValue : string.Empty,
            "\u4EA7\u54C1\u503C" => ResolveProductValue(product, value),
            "\u5168\u5C40\u503C" or "\u7CFB\u7EDF\u503C" => GlobalValues.TryGetValue(value, out string? globalValue) ? globalValue : string.Empty,
            _ => value
        };
    }

    private static string ResolveSchemeStepParameterValue(
        WorkStepOperationParameter parameter,
        SchemeWorkStepItem schemeStep)
    {
        SchemeWorkStepParameter? schemeParameter = schemeStep.Parameters.FirstOrDefault(item =>
            string.Equals(item.SourceParameterId?.Trim(), parameter.Id?.Trim(), StringComparison.Ordinal)) ??
            schemeStep.Parameters.FirstOrDefault(item =>
                string.Equals(item.ParameterName?.Trim(), ResolveParameterDisplayName(parameter), StringComparison.OrdinalIgnoreCase));

        return schemeParameter?.JudgeCondition?.Trim() ?? string.Empty;
    }

    private static string ResolveProductValue(ProductProfile? product, string key)
    {
        if (product is null || string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        return product.KeyValues.FirstOrDefault(item =>
            string.Equals(item.Key?.Trim(), key, StringComparison.OrdinalIgnoreCase))?.Value ?? string.Empty;
    }

    #endregion

    #region 方案工步解析与执行控制工具

    private static WorkStepProfile? ResolveWorkStep(
        SchemeWorkStepItem schemeStep,
        IReadOnlyDictionary<string, WorkStepProfile> workStepsById)
    {
        if (!string.IsNullOrWhiteSpace(schemeStep.WorkStepId) &&
            workStepsById.TryGetValue(schemeStep.WorkStepId, out WorkStepProfile? workStep))
        {
            return workStep.Clone();
        }

        return workStepsById.Values.FirstOrDefault(item =>
            string.Equals(item.ProductName?.Trim(), schemeStep.ProductName?.Trim(), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.StepName?.Trim(), schemeStep.StepName?.Trim(), StringComparison.OrdinalIgnoreCase))
            ?.Clone();
    }

    private static async Task DelayWithControlAsync(SchemeExecutionContext context, int delayMilliseconds)
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

    #region 操作类型、事件和通用工具

    private static bool IsSystemOperation(WorkStepOperation operation)
    {
        return string.Equals(operation.OperationObject?.Trim(), SystemOperationObjectName, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(operation.OperationObject?.Trim(), "\u7CFB\u7EDF", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(operation.OperationType?.Trim(), "\u7CFB\u7EDF", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLuaOperation(WorkStepOperation operation)
    {
        return string.Equals(operation.OperationObject?.Trim(), LuaOperationObjectName, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(operation.OperationType?.Trim(), LuaOperationObjectName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsJudgeOperation(WorkStepOperation operation)
    {
        return string.Equals(operation.OperationObject?.Trim(), JudgeOperationObjectName, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(operation.OperationType?.Trim(), JudgeOperationObjectName, StringComparison.OrdinalIgnoreCase);
    }

    private static void Raise(EventHandler<SchemeExecutionEventArgs>? handler, SchemeExecutionEventArgs args)
    {
        handler?.Invoke(null, args);
    }

    private static object? ConvertToParameterType(string value, Type targetType)
    {
        Type type = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (type == typeof(string))
        {
            return value;
        }

        if (type == typeof(int))
        {
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result) ? result : 0;
        }

        if (type == typeof(double))
        {
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double result) ? result : 0d;
        }

        if (type == typeof(decimal))
        {
            return decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal result) ? result : 0m;
        }

        if (type == typeof(bool))
        {
            return bool.TryParse(value, out bool result) && result;
        }

        if (type.IsEnum)
        {
            return Enum.TryParse(type, value, true, out object? enumValue)
                ? enumValue
                : Activator.CreateInstance(type);
        }

        return Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
    }

    private static Dictionary<string, string> ParseKeyValueLines(string text)
    {
        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
        foreach (string rawLine in (text ?? string.Empty).Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None))
        {
            string line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) ||
                line.StartsWith("#", StringComparison.Ordinal) ||
                line.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            int equalsIndex = line.IndexOf('=');
            if (equalsIndex <= 0)
            {
                continue;
            }

            values[line[..equalsIndex].Trim()] = line[(equalsIndex + 1)..].Trim();
        }

        return values;
    }

    private static byte[] BuildChecksum(byte[] payloadBytes, string crcMode)
    {
        return crcMode switch
        {
            "ModbusCrc16" or "1" => ComputeReflectedCrc16(payloadBytes, 0xFFFF),
            "Crc16Ibm" or "2" => ComputeReflectedCrc16(payloadBytes, 0x0000),
            "Crc16CcittFalse" or "3" => ComputeCrc16CcittFalse(payloadBytes),
            "Crc32" or "4" => ComputeCrc32LittleEndian(payloadBytes),
            _ => Array.Empty<byte>()
        };
    }

    private static byte[] ComputeReflectedCrc16(byte[] data, ushort seed)
    {
        ushort crc = seed;
        foreach (byte value in data)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc & 0x0001) != 0
                    ? (ushort)((crc >> 1) ^ 0xA001)
                    : (ushort)(crc >> 1);
            }
        }

        return new[] { (byte)(crc & 0xFF), (byte)((crc >> 8) & 0xFF) };
    }

    private static byte[] ComputeCrc16CcittFalse(byte[] data)
    {
        ushort crc = 0xFFFF;
        foreach (byte value in data)
        {
            crc ^= (ushort)(value << 8);
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc & 0x8000) != 0
                    ? (ushort)((crc << 1) ^ 0x1021)
                    : (ushort)(crc << 1);
            }
        }

        return new[] { (byte)((crc >> 8) & 0xFF), (byte)(crc & 0xFF) };
    }

    private static byte[] ComputeCrc32LittleEndian(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte value in data)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc & 0x00000001) != 0
                    ? (crc >> 1) ^ 0xEDB88320
                    : crc >> 1;
            }
        }

        crc ^= 0xFFFFFFFF;
        byte[] bytes = BitConverter.GetBytes(crc);
        return BitConverter.IsLittleEndian ? bytes : bytes.Reverse().ToArray();
    }

    private static string ReadPossiblyEncryptedText(string filePath)
    {
        string text = File.ReadAllText(filePath, Encoding.UTF8);
        try
        {
            return text.DesDecrypt();
        }
        catch
        {
            return text;
        }
    }

    private static string GetJsonString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement propertyElement))
        {
            return string.Empty;
        }

        return propertyElement.ValueKind switch
        {
            JsonValueKind.String => propertyElement.GetString() ?? string.Empty,
            JsonValueKind.Number => propertyElement.GetRawText(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => string.Empty
        };
    }

    private static bool IsHexRequestFormat(string requestFormat)
    {
        return string.Equals(requestFormat, "Hex", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(requestFormat, "0", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeHexString(string value)
    {
        string normalized = value.Replace("0x", string.Empty, StringComparison.OrdinalIgnoreCase);
        foreach (string separator in new[] { " ", "-", ",", "_", "\r", "\n", "\t" })
        {
            normalized = normalized.Replace(separator, string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        return normalized.Trim();
    }

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

    private static string GetValue(IReadOnlyList<string> values, int index)
    {
        return index >= 0 && index < values.Count ? values[index] : string.Empty;
    }

    private static bool TextEquals(string? left, string? right)
    {
        return string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveDisplayDataName(WorkStepOperation operation)
    {
        if (!string.IsNullOrWhiteSpace(operation.ViewDataName))
        {
            return operation.ViewDataName.Trim();
        }

        return string.IsNullOrWhiteSpace(operation.ReturnValue)
            ? operation.DisplayText
            : operation.ReturnValue.Trim();
    }

    private static string ResolveParameterDisplayName(WorkStepOperationParameter parameter)
    {
        if (!string.IsNullOrWhiteSpace(parameter.Value))
        {
            return parameter.Value.Trim();
        }

        if (!string.IsNullOrWhiteSpace(parameter.ParameterName))
        {
            return parameter.ParameterName.Trim();
        }

        return parameter.Description?.Trim() ?? string.Empty;
    }

    private static string NormalizeRequiredText(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }

    #endregion

    #region 内部执行上下文

    private sealed class SchemeExecutionKey : IEquatable<SchemeExecutionKey>
    {
        public SchemeExecutionKey(string stationNo, string schemeName)
        {
            StationNo = stationNo;
            SchemeName = schemeName;
        }

        public string StationNo { get; }

        public string SchemeName { get; }

        public bool Equals(SchemeExecutionKey? other)
        {
            return other is not null &&
                   string.Equals(StationNo, other.StationNo, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(SchemeName, other.SchemeName, StringComparison.OrdinalIgnoreCase);
        }

        public override bool Equals(object? obj)
        {
            return Equals(obj as SchemeExecutionKey);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(StationNo),
                StringComparer.OrdinalIgnoreCase.GetHashCode(SchemeName));
        }
    }

    private sealed class SchemeExecutionContext : IDisposable
    {
        private readonly object _pauseLock = new();
        private TaskCompletionSource<bool>? _resumeSignal;
        private bool _isPaused;

        public SchemeExecutionContext(SchemeExecutionKey key, DateTime startTime)
        {
            Key = key;
            StartTime = startTime;
        }

        public SchemeExecutionKey Key { get; }

        public DateTime StartTime { get; }

        public CancellationTokenSource CancellationTokenSource { get; } = new();

        public CancellationToken CancellationToken => CancellationTokenSource.Token;

        public List<string> Logs { get; } = new();

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

        public void Stop()
        {
            CancellationTokenSource.Cancel();
            Resume();
        }

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

        public void ThrowIfCancellationRequested()
        {
            CancellationToken.ThrowIfCancellationRequested();
        }

        public void AddLog(string message)
        {
            lock (Logs)
            {
                Logs.Add(message);
            }
        }

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

        public void Dispose()
        {
            CancellationTokenSource.Dispose();
        }
    }

    #endregion
}

#region 执行结果与事件模型

public sealed class SchemeExecutionEventArgs : EventArgs
{
    private SchemeExecutionEventArgs(
        string stationNo,
        string schemeName,
        string productName,
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
        ProductName = productName;
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

    public string StationNo { get; }

    public string SchemeName { get; }

    public string ProductName { get; }

    public string WorkStepName { get; }

    public string StepName { get; }

    public int WorkStepIndex { get; }

    public int StepIndex { get; }

    public bool? IsSuccess { get; }

    public string Message { get; }

    public object? Result { get; }

    public DateTime? StartTime { get; }

    public DateTime? EndTime { get; }

    public TimeSpan? ExecutionTime { get; }

    public bool Cancel { get; set; }

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
            scheme.ProductName,
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

    internal static SchemeExecutionEventArgs CreateWorkStep(
        string stationNo,
        SchemeProfile scheme,
        SchemeWorkStepItem schemeStep,
        WorkStepProfile workStep,
        int workStepIndex,
        bool? isSuccess = null,
        string message = "",
        DateTime? startTime = null,
        DateTime? endTime = null)
    {
        return new SchemeExecutionEventArgs(
            stationNo,
            scheme.SchemeName,
            scheme.ProductName,
            schemeStep.SchemeStepName,
            null,
            workStepIndex,
            0,
            isSuccess,
            message,
            workStep,
            startTime,
            endTime);
    }

    internal static SchemeExecutionEventArgs CreateStep(
        string stationNo,
        SchemeProfile scheme,
        SchemeWorkStepItem schemeStep,
        WorkStepProfile workStep,
        WorkStepOperation operation,
        int workStepIndex,
        int stepIndex,
        bool? isSuccess = null,
        string message = "",
        object? result = null,
        DateTime? startTime = null,
        DateTime? endTime = null)
    {
        _ = workStep;
        return new SchemeExecutionEventArgs(
            stationNo,
            scheme.SchemeName,
            scheme.ProductName,
            schemeStep.SchemeStepName,
            operation.DisplayText,
            workStepIndex,
            stepIndex,
            isSuccess,
            message,
            result,
            startTime,
            endTime);
    }
}

public sealed class SchemeExecutionResult
{
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

    public bool IsSuccess { get; }

    public bool IsCanceled { get; }

    public string Message { get; }

    public IReadOnlyList<string> Steps { get; }

    public DateTime? StartTime { get; }

    public DateTime? EndTime { get; }

    public TimeSpan? ExecutionTime { get; }

    public static SchemeExecutionResult CreateSuccess(
        string message,
        IReadOnlyList<string>? steps = null,
        DateTime? startTime = null,
        DateTime? endTime = null)
    {
        return new SchemeExecutionResult(true, false, message, steps, startTime, endTime);
    }

    public static SchemeExecutionResult CreateFailure(
        string message,
        IReadOnlyList<string>? steps = null,
        DateTime? startTime = null,
        DateTime? endTime = null)
    {
        return new SchemeExecutionResult(false, false, message, steps, startTime, endTime);
    }

    public static SchemeExecutionResult CreateCanceled(
        string message,
        IReadOnlyList<string>? steps = null,
        DateTime? startTime = null,
        DateTime? endTime = null)
    {
        return new SchemeExecutionResult(false, true, message, steps, startTime, endTime);
    }
}

public sealed class SchemeExecutionControlActionResult
{
    public SchemeExecutionControlActionResult(bool isSuccess, string message)
    {
        IsSuccess = isSuccess;
        Message = message ?? string.Empty;
    }

    public bool IsSuccess { get; }

    public string Message { get; }

    public static SchemeExecutionControlActionResult CreateSuccess(string message)
    {
        return new SchemeExecutionControlActionResult(true, message);
    }

    public static SchemeExecutionControlActionResult CreateFailure(string message)
    {
        return new SchemeExecutionControlActionResult(false, message);
    }
}

public sealed record SchemeExecutionSnapshot(
    string StationNo,
    string SchemeName,
    bool IsPaused,
    DateTime StartTime,
    DateTime? EndTime,
    TimeSpan ExecutionTime,
    IReadOnlyList<string> Steps);

internal sealed class SchemeStepExecutionOutput
{
    private SchemeStepExecutionOutput(bool isSuccess, string message, object? result)
    {
        IsSuccess = isSuccess;
        Message = message ?? string.Empty;
        Result = result;
    }

    public bool IsSuccess { get; }

    public string Message { get; }

    public object? Result { get; }

    public static SchemeStepExecutionOutput Success(object? result)
    {
        return new SchemeStepExecutionOutput(true, string.Empty, result);
    }

    public static SchemeStepExecutionOutput Failure(string message, object? result = null)
    {
        return new SchemeStepExecutionOutput(false, message, result);
    }
}

#endregion
