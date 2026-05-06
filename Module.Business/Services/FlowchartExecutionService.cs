using ControlLibrary.Controls.FlowchartEditor.Models;
using Module.Business.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Module.Business.Services;

/// <summary>
/// 流程图执行控制服务，负责按工位启动、暂停、继续和停止流程图。
/// </summary>
public static class FlowchartExecutionService
{
    #region 常量与运行状态字段

    // 执行步数上限用于兜底阻断错误连线形成的无限循环。
    private const int MaxExecutionSteps = 500;

    private const string JudgeOperationObjectName = "判断";

    // 工位名作为唯一占用键：不同工位可并发，同一工位同一时间只允许一个流程图实例。
    private static readonly ConcurrentDictionary<string, FlowchartExecutionContext> ActiveExecutions =
        new(StringComparer.OrdinalIgnoreCase);

    #endregion

    #region 流程图执行生命周期事件

    /// <summary>
    /// 流程图执行前事件，可通过 <see cref="FlowchartExecutionEventArgs.Cancel"/> 取消执行。
    /// </summary>
    public static event EventHandler<FlowchartExecutionEventArgs>? BeforeFlowchartExecuting;

    /// <summary>
    /// 流程图开始执行事件，在进入节点执行循环前触发。
    /// </summary>
    public static event EventHandler<FlowchartExecutionEventArgs>? FlowchartExecuting;

    /// <summary>
    /// 流程图执行后事件，包含执行结果、开始时间、结束时间和耗时。
    /// </summary>
    public static event EventHandler<FlowchartExecutionEventArgs>? AfterFlowchartExecuted;

    /// <summary>
    /// 节点执行前事件，可通过 <see cref="FlowchartExecutionEventArgs.Cancel"/> 取消执行。
    /// </summary>
    public static event EventHandler<FlowchartExecutionEventArgs>? BeforeNodeExecuting;

    /// <summary>
    /// 节点执行中事件，在节点操作正式执行前触发。
    /// </summary>
    public static event EventHandler<FlowchartExecutionEventArgs>? NodeExecuting;

    /// <summary>
    /// 节点执行后事件，包含单个节点操作结果。
    /// </summary>
    public static event EventHandler<FlowchartExecutionEventArgs>? AfterNodeExecuted;

    #endregion

    #region 对外执行与控制入口

    /// <summary>
    /// 根据工位名称和流程图名称读取流程图文件并执行；同一工位同时只允许一个流程图运行。
    /// </summary>
    public static async Task<FlowchartExecutionServiceResult> ExecuteAsync(string stationName, string flowchartName)
    {
        string normalizedStationName = NormalizeRequiredText(stationName);
        string normalizedFlowchartName = NormalizeRequiredText(flowchartName);
        DateTime startTime = DateTime.Now;

        if (string.IsNullOrWhiteSpace(normalizedStationName))
        {
            return FlowchartExecutionServiceResult.CreateFailure("Station name is required.", startTime: startTime, endTime: DateTime.Now);
        }

        if (string.IsNullOrWhiteSpace(normalizedFlowchartName))
        {
            return FlowchartExecutionServiceResult.CreateFailure("Flowchart name is required.", startTime: startTime, endTime: DateTime.Now);
        }

        FlowchartExecutionKey key = new(normalizedStationName, normalizedFlowchartName);
        FlowchartExecutionContext context = new(key, startTime);
        if (!ActiveExecutions.TryAdd(normalizedStationName, context))
        {
            string runningFlowchartName = ActiveExecutions.TryGetValue(normalizedStationName, out FlowchartExecutionContext? runningContext)
                ? runningContext.Key.FlowchartName
                : string.Empty;
            context.Dispose();
            return FlowchartExecutionServiceResult.CreateFailure(
                string.IsNullOrWhiteSpace(runningFlowchartName)
                    ? $"Station '{normalizedStationName}' already has a running flowchart."
                    : $"Station '{normalizedStationName}' is already running flowchart '{runningFlowchartName}'.",
                startTime: startTime,
                endTime: DateTime.Now);
        }

        try
        {
            FlowchartConfigurationCatalog catalog = FlowchartConfigurationStore.LoadCatalog();
            FlowchartProfile? flowchart = catalog.Flowcharts.FirstOrDefault(item =>
                string.Equals(item.Name?.Trim(), normalizedFlowchartName, StringComparison.OrdinalIgnoreCase));
            if (flowchart is null)
            {
                return FlowchartExecutionServiceResult.CreateFailure(
                    $"Flowchart '{normalizedFlowchartName}' was not found.",
                    startTime: startTime,
                    endTime: DateTime.Now);
            }

            return await ExecuteFlowchartAsync(context, flowchart.Clone()).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            DateTime endTime = DateTime.Now;
            FlowchartExecutionServiceResult result = FlowchartExecutionServiceResult.CreateCanceled(
                $"Flowchart '{normalizedFlowchartName}' on station '{normalizedStationName}' was stopped.",
                context.LogsSnapshot,
                startTime,
                endTime);
            Raise(AfterFlowchartExecuted, FlowchartExecutionEventArgs.CreateFlowchart(
                normalizedStationName,
                normalizedFlowchartName,
                false,
                result.Message,
                startTime,
                endTime));
            return result;
        }
        catch (Exception ex)
        {
            DateTime endTime = DateTime.Now;
            FlowchartExecutionServiceResult result = FlowchartExecutionServiceResult.CreateFailure(
                $"Flowchart '{normalizedFlowchartName}' on station '{normalizedStationName}' failed: {ex.Message}",
                context.LogsSnapshot,
                startTime,
                endTime);
            Raise(AfterFlowchartExecuted, FlowchartExecutionEventArgs.CreateFlowchart(
                normalizedStationName,
                normalizedFlowchartName,
                false,
                result.Message,
                startTime,
                endTime));
            return result;
        }
        finally
        {
            ActiveExecutions.TryRemove(normalizedStationName, out _);
            context.Dispose();
        }
    }

    /// <summary>
    /// 暂停指定工位正在运行的流程图。
    /// </summary>
    public static FlowchartExecutionControlActionResult Pause(string stationName)
    {
        if (!TryGetStationContext(stationName, out FlowchartExecutionContext? context, out string message))
        {
            return FlowchartExecutionControlActionResult.CreateFailure(message);
        }

        return context.Pause()
            ? FlowchartExecutionControlActionResult.CreateSuccess($"Station '{context.Key.StationName}' flowchart paused.")
            : FlowchartExecutionControlActionResult.CreateSuccess($"Station '{context.Key.StationName}' flowchart is already paused.");
    }

    /// <summary>
    /// 继续指定工位已暂停的流程图。
    /// </summary>
    public static FlowchartExecutionControlActionResult Continue(string stationName)
    {
        if (!TryGetStationContext(stationName, out FlowchartExecutionContext? context, out string message))
        {
            return FlowchartExecutionControlActionResult.CreateFailure(message);
        }

        return context.Resume()
            ? FlowchartExecutionControlActionResult.CreateSuccess($"Station '{context.Key.StationName}' flowchart resumed.")
            : FlowchartExecutionControlActionResult.CreateSuccess($"Station '{context.Key.StationName}' flowchart is not paused.");
    }

    /// <summary>
    /// 停止指定工位正在运行的流程图。
    /// </summary>
    public static FlowchartExecutionControlActionResult Stop(string stationName)
    {
        if (!TryGetStationContext(stationName, out FlowchartExecutionContext? context, out string message))
        {
            return FlowchartExecutionControlActionResult.CreateFailure(message);
        }

        context.Stop();
        return FlowchartExecutionControlActionResult.CreateSuccess(
            $"Stop request sent to flowchart '{context.Key.FlowchartName}' on station '{context.Key.StationName}'.");
    }

    /// <summary>
    /// 获取当前正在运行的流程图快照。
    /// </summary>
    public static IReadOnlyList<FlowchartExecutionSnapshot> GetActiveExecutions()
    {
        return ActiveExecutions.Values
            .Select(context => context.CreateSnapshot())
            .OrderBy(item => item.StationName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.FlowchartName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    #endregion

    #region 流程图与节点执行编排

    private static async Task<FlowchartExecutionServiceResult> ExecuteFlowchartAsync(
        FlowchartExecutionContext context,
        FlowchartProfile flowchart)
    {
        DateTime startTime = DateTime.Now;
        FlowchartExecutionEventArgs beforeArgs = FlowchartExecutionEventArgs.CreateFlowchart(
            context.Key.StationName,
            flowchart.Name,
            startTime: startTime);
        Raise(BeforeFlowchartExecuting, beforeArgs);
        if (beforeArgs.Cancel)
        {
            DateTime canceledAt = DateTime.Now;
            FlowchartExecutionServiceResult canceledResult = FlowchartExecutionServiceResult.CreateCanceled(
                "Flowchart execution was canceled before start.",
                context.LogsSnapshot,
                startTime,
                canceledAt);
            Raise(AfterFlowchartExecuted, FlowchartExecutionEventArgs.CreateFlowchart(
                context.Key.StationName,
                flowchart.Name,
                false,
                canceledResult.Message,
                startTime,
                canceledAt));
            return canceledResult;
        }

        context.AddLog($"Start flowchart '{flowchart.Name}' on station '{context.Key.StationName}'.");
        Raise(FlowchartExecuting, FlowchartExecutionEventArgs.CreateFlowchart(
            context.Key.StationName,
            flowchart.Name,
            message: "Flowchart is executing.",
            startTime: startTime));

        FlowchartDocument document = FlowchartProfile.CloneDocument(flowchart.Document);
        Dictionary<Guid, FlowchartNodeDocument> nodesById = document.Nodes
            .Where(node => node is not null)
            .GroupBy(node => node.Id)
            .ToDictionary(group => group.Key, group => group.First());
        FlowchartNodeDocument? currentNode = GetExecutionStartNode(document);
        if (currentNode is null)
        {
            return FinishFlowchart(context, flowchart.Name, false, "Flowchart is empty.", startTime);
        }

        Dictionary<string, string> returnValues = new(StringComparer.OrdinalIgnoreCase);
        for (int stepIndex = 1; stepIndex <= MaxExecutionSteps; stepIndex++)
        {
            await context.WaitIfPausedAsync().ConfigureAwait(false);
            context.ThrowIfCancellationRequested();

            FlowchartNodeExecutionResult nodeResult = await ExecuteNodeAsync(
                    context,
                    flowchart.Name,
                    currentNode,
                    stepIndex,
                    returnValues)
                .ConfigureAwait(false);
            if (nodeResult.IsCanceled)
            {
                return FinishFlowchart(context, flowchart.Name, false, nodeResult.Message, startTime, isCanceled: true);
            }

            if (!nodeResult.IsSuccess)
            {
                return FinishFlowchart(context, flowchart.Name, false, nodeResult.Message, startTime);
            }

            if (currentNode.Kind == FlowchartNodeKind.End)
            {
                return FinishFlowchart(context, flowchart.Name, true, $"Flowchart '{flowchart.Name}' finished in {stepIndex} node(s).", startTime);
            }

            FlowchartConnectionDocument? nextConnection = GetNextExecutionConnection(
                currentNode,
                document.Connections,
                nodeResult.DecisionState);
            if (nextConnection is null)
            {
                return FinishFlowchart(context, flowchart.Name, true, $"Flowchart stopped at node '{ResolveNodeName(currentNode)}' because no outgoing connection was found.", startTime);
            }

            if (!nodesById.TryGetValue(nextConnection.TargetNodeId, out FlowchartNodeDocument? nextNode))
            {
                return FinishFlowchart(context, flowchart.Name, false, $"Next node of '{ResolveNodeName(currentNode)}' was not found.", startTime);
            }

            currentNode = nextNode;
        }

        return FinishFlowchart(context, flowchart.Name, false, $"Flowchart stopped after reaching max step count {MaxExecutionSteps}.", startTime);
    }

    private static async Task<FlowchartNodeExecutionResult> ExecuteNodeAsync(
        FlowchartExecutionContext context,
        string flowchartName,
        FlowchartNodeDocument node,
        int stepIndex,
        Dictionary<string, string> returnValues)
    {
        DateTime startTime = DateTime.Now;
        string nodeName = ResolveNodeName(node);
        FlowchartExecutionEventArgs beforeArgs = FlowchartExecutionEventArgs.CreateNode(
            context.Key.StationName,
            flowchartName,
            node,
            stepIndex,
            startTime: startTime);
        Raise(BeforeNodeExecuting, beforeArgs);
        if (beforeArgs.Cancel)
        {
            return FlowchartNodeExecutionResult.Canceled($"Node '{nodeName}' execution was canceled before start.");
        }

        context.AddLog($"Start node {stepIndex}: {nodeName}.");
        Raise(NodeExecuting, FlowchartExecutionEventArgs.CreateNode(
            context.Key.StationName,
            flowchartName,
            node,
            stepIndex,
            message: "Node is executing.",
            startTime: startTime));

        FlowchartNodeDecisionState decisionState = FlowchartNodeDecisionState.NotConfigured;
        object? result = null;
        string message = $"Node {stepIndex} finished: {nodeName}.";
        bool isSuccess = true;

        if (TryReadNodeOperation(node, out WorkStepOperation? operation))
        {
            if (node.Kind == FlowchartNodeKind.Decision && IsJudgeOperation(operation))
            {
                isSuccess = TryEvaluateDecisionOperation(operation, returnValues, out bool decisionResult, out message);
                decisionState = decisionResult ? FlowchartNodeDecisionState.Success : FlowchartNodeDecisionState.Failure;
                result = decisionResult;
                if (isSuccess)
                {
                    message = $"Node {stepIndex} decision result: {(decisionResult ? "True" : "False")}.";
                }
            }
            else if (node.Kind is FlowchartNodeKind.Process or FlowchartNodeKind.Decision)
            {
                SchemeStepExecutionOutput output = await SchemeExecutionService
                    .ExecuteStandaloneStepAsync(context, operation, returnValues)
                    .ConfigureAwait(false);
                result = output.Result;
                isSuccess = output.IsSuccess;
                message = output.IsSuccess
                    ? $"Node {stepIndex} finished: {operation.DisplayText}."
                    : $"Node {stepIndex} failed: {output.Message}";
            }
        }

        DateTime endTime = DateTime.Now;
        context.AddLog(message);
        Raise(AfterNodeExecuted, FlowchartExecutionEventArgs.CreateNode(
            context.Key.StationName,
            flowchartName,
            node,
            stepIndex,
            isSuccess,
            message,
            result,
            startTime,
            endTime));

        return isSuccess
            ? FlowchartNodeExecutionResult.Success(message, decisionState)
            : FlowchartNodeExecutionResult.Failure(message);
    }

    #endregion

    #region 节点连线与判断规则

    private static FlowchartNodeDocument? GetExecutionStartNode(FlowchartDocument document)
    {
        return document.Nodes
            .OrderByDescending(node => node.Kind == FlowchartNodeKind.Start)
            .ThenBy(node => node.Y)
            .ThenBy(node => node.X)
            .FirstOrDefault();
    }

    private static FlowchartConnectionDocument? GetNextExecutionConnection(
        FlowchartNodeDocument node,
        IReadOnlyList<FlowchartConnectionDocument> connections,
        FlowchartNodeDecisionState decisionState)
    {
        List<FlowchartConnectionDocument> outgoingConnections = connections
            .Where(connection => connection.SourceNodeId == node.Id)
            .ToList();

        if (outgoingConnections.Count == 0)
        {
            return null;
        }

        if (node.Kind == FlowchartNodeKind.Decision)
        {
            if (decisionState == FlowchartNodeDecisionState.Success)
            {
                return outgoingConnections.FirstOrDefault(connection => connection.SourceAnchor == FlowchartAnchor.Right);
            }

            if (decisionState == FlowchartNodeDecisionState.Failure)
            {
                return outgoingConnections.FirstOrDefault(connection => connection.SourceAnchor == FlowchartAnchor.Left);
            }

            return outgoingConnections.FirstOrDefault(connection => connection.SourceAnchor == FlowchartAnchor.Right) ??
                   outgoingConnections.FirstOrDefault(connection => connection.SourceAnchor == FlowchartAnchor.Left) ??
                   outgoingConnections.FirstOrDefault();
        }

        FlowchartAnchor[] priorityOrder =
        {
            FlowchartAnchor.Bottom,
            FlowchartAnchor.Right,
            FlowchartAnchor.Left,
            FlowchartAnchor.Top
        };

        return outgoingConnections
            .OrderBy(connection => Array.IndexOf(priorityOrder, connection.SourceAnchor))
            .FirstOrDefault();
    }

    private static bool TryEvaluateDecisionOperation(
        WorkStepOperation operation,
        IReadOnlyDictionary<string, string> returnValues,
        out bool result,
        out string message)
    {
        List<string> values = operation.Parameters
            .OrderBy(parameter => parameter.Sequence)
            .Select(parameter => ResolveParameterValue(parameter, returnValues))
            .ToList();

        string methodName = operation.InvokeMethod?.Trim() ?? string.Empty;
        result = methodName switch
        {
            "等于判断" => TextEquals(GetValue(values, 0), GetValue(values, 1)),
            "不等判断" => !TextEquals(GetValue(values, 0), GetValue(values, 1)),
            "大于判断" => CompareNumbers(GetValue(values, 0), GetValue(values, 1)) > 0,
            "大于等于判断" => CompareNumbers(GetValue(values, 0), GetValue(values, 1)) >= 0,
            "小于判断" => CompareNumbers(GetValue(values, 0), GetValue(values, 1)) < 0,
            "小于等于判断" => CompareNumbers(GetValue(values, 0), GetValue(values, 1)) <= 0,
            "包含判断" => GetValue(values, 0).IndexOf(GetValue(values, 1), StringComparison.OrdinalIgnoreCase) >= 0,
            "不包含判断" => GetValue(values, 0).IndexOf(GetValue(values, 1), StringComparison.OrdinalIgnoreCase) < 0,
            "为空判断" => string.IsNullOrWhiteSpace(GetValue(values, 0)),
            "不为空判断" => !string.IsNullOrWhiteSpace(GetValue(values, 0)),
            _ => false
        };

        message = string.IsNullOrWhiteSpace(methodName)
            ? "Decision method is required."
            : $"Unknown decision method '{methodName}'.";
        return IsSupportedJudgeMethod(methodName);
    }

    #endregion

    #region 操作解析与通用工具

    private static FlowchartExecutionServiceResult FinishFlowchart(
        FlowchartExecutionContext context,
        string flowchartName,
        bool isSuccess,
        string message,
        DateTime startTime,
        bool isCanceled = false)
    {
        DateTime endTime = DateTime.Now;
        context.AddLog(message);
        Raise(AfterFlowchartExecuted, FlowchartExecutionEventArgs.CreateFlowchart(
            context.Key.StationName,
            flowchartName,
            isSuccess,
            message,
            startTime,
            endTime));

        if (isCanceled)
        {
            return FlowchartExecutionServiceResult.CreateCanceled(message, context.LogsSnapshot, startTime, endTime);
        }

        return isSuccess
            ? FlowchartExecutionServiceResult.CreateSuccess(message, context.LogsSnapshot, startTime, endTime)
            : FlowchartExecutionServiceResult.CreateFailure(message, context.LogsSnapshot, startTime, endTime);
    }

    private static bool TryReadNodeOperation(FlowchartNodeDocument node, out WorkStepOperation operation)
    {
        operation = new WorkStepOperation();
        if (string.IsNullOrWhiteSpace(node.MetadataJson))
        {
            return false;
        }

        try
        {
            WorkStepOperation? parsed = JsonSerializer.Deserialize<WorkStepOperation>(node.MetadataJson);
            if (parsed is null)
            {
                return false;
            }

            operation = parsed;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string ResolveParameterValue(
        WorkStepOperationParameter parameter,
        IReadOnlyDictionary<string, string> returnValues)
    {
        string type = parameter.Type?.Trim() ?? string.Empty;
        string value = parameter.Value?.Trim() ?? string.Empty;

        return type switch
        {
            "返回值" => returnValues.TryGetValue(value, out string? returnValue) ? returnValue : string.Empty,
            _ => value
        };
    }

    private static bool IsJudgeOperation(WorkStepOperation operation)
    {
        return string.Equals(operation.OperationObject?.Trim(), JudgeOperationObjectName, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(operation.OperationType?.Trim(), JudgeOperationObjectName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSupportedJudgeMethod(string methodName)
    {
        return methodName.Trim() switch
        {
            "等于判断" => true,
            "不等判断" => true,
            "大于判断" => true,
            "大于等于判断" => true,
            "小于判断" => true,
            "小于等于判断" => true,
            "包含判断" => true,
            "不包含判断" => true,
            "为空判断" => true,
            "不为空判断" => true,
            _ => false
        };
    }

    private static int CompareNumbers(string left, string right)
    {
        decimal leftValue = decimal.TryParse(left, out decimal parsedLeft) ? parsedLeft : 0m;
        decimal rightValue = decimal.TryParse(right, out decimal parsedRight) ? parsedRight : 0m;
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

    private static string ResolveNodeName(FlowchartNodeDocument node)
    {
        string[] lines = (node.Text ?? string.Empty)
            .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);

        return lines
            .Select(line => line?.Trim() ?? string.Empty)
            .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line))
            ?? GetDefaultNodeName(node.Kind);
    }

    private static string GetDefaultNodeName(FlowchartNodeKind nodeKind)
    {
        return nodeKind switch
        {
            FlowchartNodeKind.Start => "开始",
            FlowchartNodeKind.Decision => "判断",
            FlowchartNodeKind.End => "结束",
            _ => "处理"
        };
    }

    private static bool TryGetStationContext(
        string stationName,
        out FlowchartExecutionContext context,
        out string message)
    {
        string normalizedStationName = NormalizeRequiredText(stationName);
        if (string.IsNullOrWhiteSpace(normalizedStationName))
        {
            context = null!;
            message = "Station name is required.";
            return false;
        }

        if (!ActiveExecutions.TryGetValue(normalizedStationName, out FlowchartExecutionContext? runningContext))
        {
            context = null!;
            message = $"No flowchart is running on station '{normalizedStationName}'.";
            return false;
        }

        context = runningContext;
        message = string.Empty;
        return true;
    }

    private static void Raise(EventHandler<FlowchartExecutionEventArgs>? handler, FlowchartExecutionEventArgs args)
    {
        handler?.Invoke(null, args);
    }

    private static string NormalizeRequiredText(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }

    #endregion

    #region 内部执行上下文

    private sealed class FlowchartExecutionKey : IEquatable<FlowchartExecutionKey>
    {
        public FlowchartExecutionKey(string stationName, string flowchartName)
        {
            StationName = stationName;
            FlowchartName = flowchartName;
        }

        public string StationName { get; }

        public string FlowchartName { get; }

        public bool Equals(FlowchartExecutionKey? other)
        {
            return other is not null &&
                   string.Equals(StationName, other.StationName, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(FlowchartName, other.FlowchartName, StringComparison.OrdinalIgnoreCase);
        }

        public override bool Equals(object? obj)
        {
            return Equals(obj as FlowchartExecutionKey);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(StationName),
                StringComparer.OrdinalIgnoreCase.GetHashCode(FlowchartName));
        }
    }

    private sealed class FlowchartExecutionContext : IControlledExecutionContext, IDisposable
    {
        private readonly object _pauseLock = new();
        private readonly object _logLock = new();
        private readonly List<string> _logs = new();
        private TaskCompletionSource<bool>? _resumeSignal;
        private bool _isPaused;

        public FlowchartExecutionContext(FlowchartExecutionKey key, DateTime startTime)
        {
            Key = key;
            StartTime = startTime;
        }

        public FlowchartExecutionKey Key { get; }

        public DateTime StartTime { get; }

        public CancellationTokenSource CancellationTokenSource { get; } = new();

        public CancellationToken CancellationToken => CancellationTokenSource.Token;

        public IReadOnlyList<string> LogsSnapshot
        {
            get
            {
                lock (_logLock)
                {
                    return _logs.ToList();
                }
            }
        }

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
            lock (_logLock)
            {
                _logs.Add(message);
            }
        }

        public FlowchartExecutionSnapshot CreateSnapshot()
        {
            DateTime snapshotTime = DateTime.Now;
            return new FlowchartExecutionSnapshot(
                Key.StationName,
                Key.FlowchartName,
                IsPaused,
                StartTime,
                null,
                snapshotTime - StartTime,
                LogsSnapshot);
        }

        public void Dispose()
        {
            CancellationTokenSource.Dispose();
        }
    }

    private sealed class FlowchartNodeExecutionResult
    {
        private FlowchartNodeExecutionResult(
            bool isSuccess,
            bool isCanceled,
            string message,
            FlowchartNodeDecisionState decisionState)
        {
            IsSuccess = isSuccess;
            IsCanceled = isCanceled;
            Message = message;
            DecisionState = decisionState;
        }

        public bool IsSuccess { get; }

        public bool IsCanceled { get; }

        public string Message { get; }

        public FlowchartNodeDecisionState DecisionState { get; }

        public static FlowchartNodeExecutionResult Success(string message, FlowchartNodeDecisionState decisionState)
        {
            return new FlowchartNodeExecutionResult(true, false, message, decisionState);
        }

        public static FlowchartNodeExecutionResult Failure(string message)
        {
            return new FlowchartNodeExecutionResult(false, false, message, FlowchartNodeDecisionState.NotConfigured);
        }

        public static FlowchartNodeExecutionResult Canceled(string message)
        {
            return new FlowchartNodeExecutionResult(false, true, message, FlowchartNodeDecisionState.NotConfigured);
        }
    }

    private enum FlowchartNodeDecisionState
    {
        NotConfigured,
        Success,
        Failure
    }

    #endregion
}

#region 流程图执行结果与事件模型

public sealed class FlowchartExecutionEventArgs : EventArgs
{
    private FlowchartExecutionEventArgs(
        string stationName,
        string flowchartName,
        Guid? nodeId,
        string nodeName,
        FlowchartNodeKind? nodeKind,
        int nodeIndex,
        bool? isSuccess,
        string message,
        object? result,
        DateTime? startTime,
        DateTime? endTime)
    {
        StationName = stationName;
        FlowchartName = flowchartName;
        NodeId = nodeId;
        NodeName = nodeName;
        NodeKind = nodeKind;
        NodeIndex = nodeIndex;
        IsSuccess = isSuccess;
        Message = message;
        Result = result;
        StartTime = startTime;
        EndTime = endTime;
        ExecutionTime = startTime.HasValue && endTime.HasValue
            ? endTime.Value - startTime.Value
            : null;
    }

    public string StationName { get; }

    public string FlowchartName { get; }

    public Guid? NodeId { get; }

    public string NodeName { get; }

    public FlowchartNodeKind? NodeKind { get; }

    public int NodeIndex { get; }

    public bool? IsSuccess { get; }

    public string Message { get; }

    public object? Result { get; }

    public DateTime? StartTime { get; }

    public DateTime? EndTime { get; }

    public TimeSpan? ExecutionTime { get; }

    public bool Cancel { get; set; }

    internal static FlowchartExecutionEventArgs CreateFlowchart(
        string stationName,
        string flowchartName,
        bool? isSuccess = null,
        string message = "",
        DateTime? startTime = null,
        DateTime? endTime = null)
    {
        return new FlowchartExecutionEventArgs(
            stationName,
            flowchartName,
            null,
            string.Empty,
            null,
            0,
            isSuccess,
            message,
            null,
            startTime,
            endTime);
    }

    internal static FlowchartExecutionEventArgs CreateNode(
        string stationName,
        string flowchartName,
        FlowchartNodeDocument node,
        int nodeIndex,
        bool? isSuccess = null,
        string message = "",
        object? result = null,
        DateTime? startTime = null,
        DateTime? endTime = null)
    {
        return new FlowchartExecutionEventArgs(
            stationName,
            flowchartName,
            node.Id,
            node.Text ?? string.Empty,
            node.Kind,
            nodeIndex,
            isSuccess,
            message,
            result,
            startTime,
            endTime);
    }
}

public sealed class FlowchartExecutionServiceResult
{
    public FlowchartExecutionServiceResult(
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

    public static FlowchartExecutionServiceResult CreateSuccess(
        string message,
        IReadOnlyList<string>? steps = null,
        DateTime? startTime = null,
        DateTime? endTime = null)
    {
        return new FlowchartExecutionServiceResult(true, false, message, steps, startTime, endTime);
    }

    public static FlowchartExecutionServiceResult CreateFailure(
        string message,
        IReadOnlyList<string>? steps = null,
        DateTime? startTime = null,
        DateTime? endTime = null)
    {
        return new FlowchartExecutionServiceResult(false, false, message, steps, startTime, endTime);
    }

    public static FlowchartExecutionServiceResult CreateCanceled(
        string message,
        IReadOnlyList<string>? steps = null,
        DateTime? startTime = null,
        DateTime? endTime = null)
    {
        return new FlowchartExecutionServiceResult(false, true, message, steps, startTime, endTime);
    }
}

public sealed class FlowchartExecutionControlActionResult
{
    public FlowchartExecutionControlActionResult(bool isSuccess, string message)
    {
        IsSuccess = isSuccess;
        Message = message ?? string.Empty;
    }

    public bool IsSuccess { get; }

    public string Message { get; }

    public static FlowchartExecutionControlActionResult CreateSuccess(string message)
    {
        return new FlowchartExecutionControlActionResult(true, message);
    }

    public static FlowchartExecutionControlActionResult CreateFailure(string message)
    {
        return new FlowchartExecutionControlActionResult(false, message);
    }
}

public sealed record FlowchartExecutionSnapshot(
    string StationName,
    string FlowchartName,
    bool IsPaused,
    DateTime StartTime,
    DateTime? EndTime,
    TimeSpan ExecutionTime,
    IReadOnlyList<string> Steps);

#endregion
