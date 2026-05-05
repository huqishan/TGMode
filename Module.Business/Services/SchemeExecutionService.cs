using Module.Business.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Module.Business.Services;

/// <summary>
/// 方案执行编排服务。
/// </summary>
/// <remarks>
/// <para>
/// 这个服务只负责“方案 -> 工步 -> 工步内操作”的执行编排、执行日志整理、返回值缓存以及方案判断参数校验，
/// 不直接依赖具体的 PLC、协议、脚本或系统方法实现。
/// </para>
/// <para>
/// 调用方只需要把真正执行单条 <see cref="WorkStepOperation"/> 的逻辑通过委托传进来，
/// 服务就会按方案顺序串行调用，并在每一步结束后自动做以下工作：
/// </para>
/// <list type="number">
/// <item>
/// 校验方案中的工步引用是否仍然有效，避免执行已删除或产品不匹配的工步。
/// </item>
/// <item>
/// 维护当前方案工步内的“输出值快照”，让后续判断参数可以通过参数名、判断类型或源标识读取执行结果。
/// </item>
/// <item>
/// 对参数类型为“判断值”的方案参数执行统一校验，目前默认按“结果值 == 判断条件”处理。
/// </item>
/// <item>
/// 汇总详细执行日志和结构化结果，方便后续直接接入界面日志、历史记录或调试面板。
/// </item>
/// </list>
/// <para>
/// 由于当前项目里方案参数模型没有单独保存“设置值”的实际值，所以这里不会反向修改工步原始参数，
/// 而是把方案参数更多地当成“执行完成后的结果判断配置”来使用。
/// </para>
/// </remarks>
public static class SchemeExecutionService
{
    private const string DisplayedViewDataSourceId = "__display_to_view__";

    /// <summary>
    /// 按方案定义顺序执行整个方案。
    /// </summary>
    /// <param name="scheme">
    /// 要执行的方案快照。建议传入当前已保存或已经通过界面校验的方案对象。
    /// </param>
    /// <param name="workSteps">
    /// 当前可用的工步集合。服务会根据 <see cref="SchemeWorkStepItem.WorkStepId"/> 在这里解析真实工步。
    /// </param>
    /// <param name="executeOperationAsync">
    /// 单条工步操作的实际执行委托。
    /// 调用方可以在这里接入 PLC 指令、协议发送、系统方法调用、Lua 执行等真实逻辑。
    /// </param>
    /// <param name="cancellationToken">
    /// 取消令牌。用于在外部主动中断整个方案执行。
    /// </param>
    /// <returns>
    /// 返回完整的方案执行结果，包含成功状态、汇总消息、日志以及每个方案工步的结构化结果。
    /// </returns>
    public static async Task<SchemeExecutionResult> ExecuteSchemeAsync(
        SchemeProfile? scheme,
        IEnumerable<WorkStepProfile>? workSteps,
        Func<SchemeOperationExecutionContext, CancellationToken, Task<SchemeOperationExecutionResult>> executeOperationAsync,
        CancellationToken cancellationToken = default)
    {
        if (scheme is null)
        {
            return SchemeExecutionResult.CreateFailure("方案为空，无法执行。");
        }

        if (executeOperationAsync is null)
        {
            return SchemeExecutionResult.CreateFailure("未提供工步操作执行委托，无法执行方案。");
        }

        List<WorkStepProfile> workStepSnapshots = (workSteps ?? Enumerable.Empty<WorkStepProfile>())
            .Where(workStep => workStep is not null)
            .Select(workStep => workStep.Clone())
            .ToList();

        if (scheme.Steps.Count == 0)
        {
            return SchemeExecutionResult.CreateFailure(
                $"方案“{scheme.SchemeName}”未配置任何工步，无法执行。");
        }

        Dictionary<string, WorkStepProfile> workStepById = workStepSnapshots
            .Where(workStep => !string.IsNullOrWhiteSpace(workStep.Id))
            .GroupBy(workStep => workStep.Id.Trim(), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        List<string> logs = new();
        List<SchemeStepExecutionResult> stepResults = new();

        try
        {
            for (int schemeStepIndex = 0; schemeStepIndex < scheme.Steps.Count; schemeStepIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                SchemeWorkStepItem schemeStep = scheme.Steps[schemeStepIndex];
                if (!TryResolveWorkStep(scheme, schemeStep, workStepById, out WorkStepProfile? workStep, out string resolveMessage))
                {
                    logs.Add(resolveMessage);
                    return SchemeExecutionResult.CreateFailure(resolveMessage, logs, stepResults);
                }

                WorkStepProfile resolvedWorkStep = workStep!;
                string schemeStepName = ResolveSchemeStepName(schemeStep);
                logs.Add($"开始执行方案工步 {schemeStepIndex + 1}/{scheme.Steps.Count}：{schemeStepName}");

                Dictionary<string, string?> stepOutputValues = new(StringComparer.OrdinalIgnoreCase);
                List<SchemeOperationExecutionResult> operationResults = new();

                for (int operationIndex = 0; operationIndex < resolvedWorkStep.Steps.Count; operationIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    WorkStepOperation operation = resolvedWorkStep.Steps[operationIndex].Clone();
                    SchemeOperationExecutionContext context = new(
                        scheme,
                        schemeStep,
                        resolvedWorkStep,
                        operation,
                        schemeStepIndex + 1,
                        operationIndex + 1,
                        new Dictionary<string, string?>(stepOutputValues, StringComparer.OrdinalIgnoreCase));

                    logs.Add($"执行操作 {schemeStepIndex + 1}.{operationIndex + 1}：{operation.DisplayText}");

                    SchemeOperationExecutionResult operationResult =
                        await executeOperationAsync(context, cancellationToken).ConfigureAwait(false)
                        ?? SchemeOperationExecutionResult.CreateFailure("执行委托返回了空结果。");

                    operationResults.Add(operationResult);
                    logs.Add(operationResult.Message);

                    MergeOperationOutputs(stepOutputValues, operation, operationResult);

                    if (!operationResult.IsSuccess)
                    {
                        SchemeStepExecutionResult failedStepResult = new(
                            schemeStep.Id,
                            resolvedWorkStep.Id,
                            schemeStepName,
                            false,
                            $"方案工步“{schemeStepName}”执行失败：{operationResult.Message}",
                            operationResults,
                            Array.Empty<SchemeParameterEvaluationResult>(),
                            new Dictionary<string, string?>(stepOutputValues, StringComparer.OrdinalIgnoreCase));
                        stepResults.Add(failedStepResult);

                        return SchemeExecutionResult.CreateFailure(
                            failedStepResult.Message,
                            logs,
                            stepResults);
                    }

                    if (operation.DelayMilliseconds > 0)
                    {
                        logs.Add($"操作延时 {operation.DelayMilliseconds}ms");
                        await Task.Delay(operation.DelayMilliseconds, cancellationToken).ConfigureAwait(false);
                    }
                }

                List<SchemeParameterEvaluationResult> parameterEvaluations =
                    EvaluateSchemeStepParameters(schemeStep.Parameters, stepOutputValues);

                foreach (SchemeParameterEvaluationResult evaluation in parameterEvaluations)
                {
                    logs.Add(evaluation.Message);
                }

                bool isStepSuccess = parameterEvaluations
                    .Where(evaluation => evaluation.ParticipatesInJudgement)
                    .All(evaluation => evaluation.IsMatched);

                string stepMessage = isStepSuccess
                    ? $"方案工步“{schemeStepName}”执行完成。"
                    : $"方案工步“{schemeStepName}”判断失败。";

                SchemeStepExecutionResult stepResult = new(
                    schemeStep.Id,
                    resolvedWorkStep.Id,
                    schemeStepName,
                    isStepSuccess,
                    stepMessage,
                    operationResults,
                    parameterEvaluations,
                    new Dictionary<string, string?>(stepOutputValues, StringComparer.OrdinalIgnoreCase));
                stepResults.Add(stepResult);
                logs.Add(stepMessage);

                if (!isStepSuccess)
                {
                    return SchemeExecutionResult.CreateFailure(stepMessage, logs, stepResults);
                }
            }
        }
        catch (OperationCanceledException)
        {
            logs.Add("方案执行已取消。");
            return SchemeExecutionResult.CreateFailure("方案执行已取消。", logs, stepResults);
        }
        catch (Exception ex)
        {
            logs.Add($"方案执行异常：{ex.Message}");
            return SchemeExecutionResult.CreateFailure($"方案执行异常：{ex.Message}", logs, stepResults);
        }

        string successMessage = $"方案“{scheme.SchemeName}”执行完成，共执行 {stepResults.Count} 个方案工步。";
        logs.Add(successMessage);
        return SchemeExecutionResult.CreateSuccess(successMessage, logs, stepResults);
    }

    /// <summary>
    /// 校验方案工步能否在当前工步集合里解析出真实工步。
    /// </summary>
    private static bool TryResolveWorkStep(
        SchemeProfile scheme,
        SchemeWorkStepItem schemeStep,
        IReadOnlyDictionary<string, WorkStepProfile> workStepById,
        out WorkStepProfile? workStep,
        out string message)
    {
        workStep = null;

        if (string.IsNullOrWhiteSpace(schemeStep.WorkStepId) ||
            !workStepById.TryGetValue(schemeStep.WorkStepId.Trim(), out WorkStepProfile? resolvedWorkStep))
        {
            message = $"方案“{scheme.SchemeName}”中的工步“{ResolveSchemeStepName(schemeStep)}”已不存在，无法执行。";
            return false;
        }

        if (!string.Equals(resolvedWorkStep.ProductName, scheme.ProductName, StringComparison.OrdinalIgnoreCase))
        {
            message =
                $"方案“{scheme.SchemeName}”中的工步“{ResolveSchemeStepName(schemeStep)}”与方案产品“{scheme.ProductName}”不匹配，无法执行。";
            return false;
        }

        workStep = resolvedWorkStep;
        message = string.Empty;
        return true;
    }

    /// <summary>
    /// 把单条操作执行结果写回到当前方案工步的输出值缓存中。
    /// </summary>
    /// <remarks>
    /// 这里会同时维护几类键，目的是兼容后续不同来源的判断方式：
    /// 1. 调用方显式返回的 <see cref="SchemeOperationExecutionResult.OutputValues"/>。
    /// 2. 操作返回值变量名，例如工步里配置的 <see cref="WorkStepOperation.ReturnValue"/>。
    /// 3. “显示到界面”配置里的数据名称 / 判断类型，方便方案判断参数直接按界面配置取值。
    /// 4. 内部源标识键，格式为“SourceOperationId::SourceParameterId”，便于后续精确定位值来源。
    /// </remarks>
    private static void MergeOperationOutputs(
        IDictionary<string, string?> stepOutputValues,
        WorkStepOperation operation,
        SchemeOperationExecutionResult operationResult)
    {
        if (!string.IsNullOrWhiteSpace(operationResult.ReturnValue))
        {
            RegisterOutputValue(stepOutputValues, operation.ReturnValue, operationResult.ReturnValue);

            if (operation.ShowDataToView)
            {
                RegisterOutputValue(stepOutputValues, operation.ViewDataName, operationResult.ReturnValue);
                RegisterOutputValue(stepOutputValues, operation.ViewJudgeType, operationResult.ReturnValue);
                RegisterOutputValue(
                    stepOutputValues,
                    BuildParameterSourceKey(DisplayedViewDataSourceId, ResolveDisplayedValueKey(operation)),
                    operationResult.ReturnValue);
            }
        }

        foreach ((string key, string? value) in operationResult.OutputValues)
        {
            RegisterOutputValue(stepOutputValues, key, value);
        }
    }

    /// <summary>
    /// 对当前方案工步下的参数做执行后判断。
    /// </summary>
    /// <remarks>
    /// 当前模型里只有“判断条件”而没有更复杂的表达式，因此默认按字符串等值比较。
    /// 如果未来需要支持大于、小于、范围、正则或多条件组合，可以在这里继续扩展，
    /// 而不需要修改整个方案执行主流程。
    /// </remarks>
    private static List<SchemeParameterEvaluationResult> EvaluateSchemeStepParameters(
        IEnumerable<SchemeWorkStepParameter> parameters,
        IReadOnlyDictionary<string, string?> stepOutputValues)
    {
        List<SchemeParameterEvaluationResult> results = new();

        foreach (SchemeWorkStepParameter parameter in parameters.Where(parameter => parameter is not null))
        {
            string? actualValue = ResolveParameterActualValue(parameter, stepOutputValues);
            bool participatesInJudgement = IsJudgementParameter(parameter);

            if (!participatesInJudgement)
            {
                results.Add(new SchemeParameterEvaluationResult(
                    parameter.ParameterName,
                    parameter.ParameterType,
                    parameter.JudgeType,
                    parameter.JudgeCondition,
                    actualValue,
                    true,
                    false,
                    $"参数“{parameter.ParameterName}”为设置值，不参与执行判断。"));
                continue;
            }

            bool isMatched = TextEquals(actualValue, parameter.JudgeCondition);
            string message = isMatched
                ? $"参数“{parameter.ParameterName}”判断通过，实际值“{actualValue ?? string.Empty}”与条件“{parameter.JudgeCondition}”一致。"
                : $"参数“{parameter.ParameterName}”判断失败，实际值“{actualValue ?? string.Empty}”与条件“{parameter.JudgeCondition}”不一致。";

            results.Add(new SchemeParameterEvaluationResult(
                parameter.ParameterName,
                parameter.ParameterType,
                parameter.JudgeType,
                parameter.JudgeCondition,
                actualValue,
                isMatched,
                true,
                message));
        }

        return results;
    }

    /// <summary>
    /// 解析方案参数当前应该读取哪个输出值。
    /// </summary>
    /// <remarks>
    /// 解析顺序按“最精确 -> 最宽松”处理：
    /// 1. 源操作 ID + 源参数 ID。
    /// 2. 判断类型。
    /// 3. 参数名称。
    /// 这样既支持严格绑定，也兼容界面上按名称或按判断类型配置的使用习惯。
    /// </remarks>
    private static string? ResolveParameterActualValue(
        SchemeWorkStepParameter parameter,
        IReadOnlyDictionary<string, string?> stepOutputValues)
    {
        string sourceKey = BuildParameterSourceKey(parameter.SourceOperationId, parameter.SourceParameterId);
        if (!string.IsNullOrWhiteSpace(sourceKey) &&
            stepOutputValues.TryGetValue(sourceKey, out string? sourceValue))
        {
            return sourceValue;
        }

        if (!string.IsNullOrWhiteSpace(parameter.JudgeType) &&
            stepOutputValues.TryGetValue(parameter.JudgeType.Trim(), out string? judgeTypeValue))
        {
            return judgeTypeValue;
        }

        if (!string.IsNullOrWhiteSpace(parameter.ParameterName) &&
            stepOutputValues.TryGetValue(parameter.ParameterName.Trim(), out string? parameterNameValue))
        {
            return parameterNameValue;
        }

        return null;
    }

    private static string ResolveSchemeStepName(SchemeWorkStepItem schemeStep)
    {
        return string.IsNullOrWhiteSpace(schemeStep.SchemeStepName)
            ? schemeStep.StepName
            : schemeStep.SchemeStepName.Trim();
    }

    private static bool IsJudgementParameter(SchemeWorkStepParameter parameter)
    {
        return string.Equals(parameter.ParameterType?.Trim(), "判断值", StringComparison.OrdinalIgnoreCase);
    }

    private static void RegisterOutputValue(
        IDictionary<string, string?> stepOutputValues,
        string? key,
        string? value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        stepOutputValues[key.Trim()] = value;
    }

    private static string ResolveDisplayedValueKey(WorkStepOperation operation)
    {
        if (!string.IsNullOrWhiteSpace(operation.ViewJudgeType))
        {
            return operation.ViewJudgeType.Trim();
        }

        if (!string.IsNullOrWhiteSpace(operation.ViewDataName))
        {
            return operation.ViewDataName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(operation.ReturnValue))
        {
            return operation.ReturnValue.Trim();
        }

        return operation.Id;
    }

    private static string BuildParameterSourceKey(string? sourceOperationId, string? sourceParameterId)
    {
        string operationId = sourceOperationId?.Trim() ?? string.Empty;
        string parameterId = sourceParameterId?.Trim() ?? string.Empty;
        return string.IsNullOrWhiteSpace(operationId) && string.IsNullOrWhiteSpace(parameterId)
            ? string.Empty
            : $"{operationId}::{parameterId}";
    }

    private static bool TextEquals(string? left, string? right)
    {
        return string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// 方案执行最终结果。
/// </summary>
public sealed class SchemeExecutionResult
{
    public SchemeExecutionResult(
        bool isSuccess,
        string message,
        IReadOnlyList<string> logs,
        IReadOnlyList<SchemeStepExecutionResult> stepResults)
    {
        IsSuccess = isSuccess;
        Message = message ?? string.Empty;
        Logs = logs ?? Array.Empty<string>();
        StepResults = stepResults ?? Array.Empty<SchemeStepExecutionResult>();
    }

    public bool IsSuccess { get; }

    public string Message { get; }

    public IReadOnlyList<string> Logs { get; }

    public IReadOnlyList<SchemeStepExecutionResult> StepResults { get; }

    public static SchemeExecutionResult CreateSuccess(
        string message,
        IReadOnlyList<string>? logs = null,
        IReadOnlyList<SchemeStepExecutionResult>? stepResults = null)
    {
        return new SchemeExecutionResult(
            true,
            message,
            logs ?? Array.Empty<string>(),
            stepResults ?? Array.Empty<SchemeStepExecutionResult>());
    }

    public static SchemeExecutionResult CreateFailure(
        string message,
        IReadOnlyList<string>? logs = null,
        IReadOnlyList<SchemeStepExecutionResult>? stepResults = null)
    {
        return new SchemeExecutionResult(
            false,
            message,
            logs ?? Array.Empty<string>(),
            stepResults ?? Array.Empty<SchemeStepExecutionResult>());
    }
}

/// <summary>
/// 单个方案工步的执行结果。
/// </summary>
public sealed class SchemeStepExecutionResult
{
    public SchemeStepExecutionResult(
        string schemeStepId,
        string workStepId,
        string stepName,
        bool isSuccess,
        string message,
        IReadOnlyList<SchemeOperationExecutionResult> operationResults,
        IReadOnlyList<SchemeParameterEvaluationResult> parameterEvaluations,
        IReadOnlyDictionary<string, string?> outputValues)
    {
        SchemeStepId = schemeStepId ?? string.Empty;
        WorkStepId = workStepId ?? string.Empty;
        StepName = stepName ?? string.Empty;
        IsSuccess = isSuccess;
        Message = message ?? string.Empty;
        OperationResults = operationResults ?? Array.Empty<SchemeOperationExecutionResult>();
        ParameterEvaluations = parameterEvaluations ?? Array.Empty<SchemeParameterEvaluationResult>();
        OutputValues = outputValues ?? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
    }

    public string SchemeStepId { get; }

    public string WorkStepId { get; }

    public string StepName { get; }

    public bool IsSuccess { get; }

    public string Message { get; }

    public IReadOnlyList<SchemeOperationExecutionResult> OperationResults { get; }

    public IReadOnlyList<SchemeParameterEvaluationResult> ParameterEvaluations { get; }

    public IReadOnlyDictionary<string, string?> OutputValues { get; }
}

/// <summary>
/// 单条工步操作执行前传给外部执行器的上下文。
/// </summary>
public sealed class SchemeOperationExecutionContext
{
    public SchemeOperationExecutionContext(
        SchemeProfile scheme,
        SchemeWorkStepItem schemeStep,
        WorkStepProfile workStep,
        WorkStepOperation operation,
        int schemeStepIndex,
        int operationIndex,
        IReadOnlyDictionary<string, string?> stepOutputValues)
    {
        Scheme = scheme;
        SchemeStep = schemeStep;
        WorkStep = workStep;
        Operation = operation;
        SchemeStepIndex = schemeStepIndex;
        OperationIndex = operationIndex;
        StepOutputValues = stepOutputValues;
    }

    public SchemeProfile Scheme { get; }

    public SchemeWorkStepItem SchemeStep { get; }

    public WorkStepProfile WorkStep { get; }

    public WorkStepOperation Operation { get; }

    public int SchemeStepIndex { get; }

    public int OperationIndex { get; }

    /// <summary>
    /// 当前方案工步内，前面已经执行完成的输出值快照。
    /// </summary>
    public IReadOnlyDictionary<string, string?> StepOutputValues { get; }
}

/// <summary>
/// 单条工步操作的执行结果。
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ReturnValue"/> 用于承接操作的主返回值，通常会自动映射到工步里配置的返回值变量名、
/// “显示到界面”的数据名称和判断类型。
/// </para>
/// <para>
/// 如果调用方拿到了更多命名结果，例如协议解析出来的多个字段，可以继续放进 <see cref="OutputValues"/>，
/// 服务会一并写入当前方案工步的输出值缓存。
/// </para>
/// </remarks>
public sealed class SchemeOperationExecutionResult
{
    public SchemeOperationExecutionResult(
        bool isSuccess,
        string message,
        string? returnValue = null,
        IReadOnlyDictionary<string, string?>? outputValues = null,
        object? rawResult = null)
    {
        IsSuccess = isSuccess;
        Message = message ?? string.Empty;
        ReturnValue = returnValue;
        OutputValues = outputValues ?? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        RawResult = rawResult;
    }

    public bool IsSuccess { get; }

    public string Message { get; }

    public string? ReturnValue { get; }

    public IReadOnlyDictionary<string, string?> OutputValues { get; }

    public object? RawResult { get; }

    public static SchemeOperationExecutionResult CreateSuccess(
        string message,
        string? returnValue = null,
        IReadOnlyDictionary<string, string?>? outputValues = null,
        object? rawResult = null)
    {
        return new SchemeOperationExecutionResult(true, message, returnValue, outputValues, rawResult);
    }

    public static SchemeOperationExecutionResult CreateFailure(
        string message,
        string? returnValue = null,
        IReadOnlyDictionary<string, string?>? outputValues = null,
        object? rawResult = null)
    {
        return new SchemeOperationExecutionResult(false, message, returnValue, outputValues, rawResult);
    }
}

/// <summary>
/// 单个方案参数的执行判断结果。
/// </summary>
public sealed class SchemeParameterEvaluationResult
{
    public SchemeParameterEvaluationResult(
        string parameterName,
        string parameterType,
        string judgeType,
        string judgeCondition,
        string? actualValue,
        bool isMatched,
        bool participatesInJudgement,
        string message)
    {
        ParameterName = parameterName ?? string.Empty;
        ParameterType = parameterType ?? string.Empty;
        JudgeType = judgeType ?? string.Empty;
        JudgeCondition = judgeCondition ?? string.Empty;
        ActualValue = actualValue;
        IsMatched = isMatched;
        ParticipatesInJudgement = participatesInJudgement;
        Message = message ?? string.Empty;
    }

    public string ParameterName { get; }

    public string ParameterType { get; }

    public string JudgeType { get; }

    public string JudgeCondition { get; }

    public string? ActualValue { get; }

    public bool IsMatched { get; }

    public bool ParticipatesInJudgement { get; }

    public string Message { get; }
}
