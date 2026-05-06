using ControlLibrary.Controls.FlowchartEditor.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace ControlLibrary.Controls.FlowchartEditor.Control
{
    /// <summary>
    /// FlowchartEditorControl.xaml 的交互逻辑
    /// </summary>
    // 流程图编辑器的交互和渲染逻辑。
    public partial class FlowchartEditorControl : UserControl
    {
        // 画布使用一个很大的固定工作区，再通过缩放和平移变换显示局部区域。
        private const double WorkspaceSize = 10000;
        private const double DefaultNodeWidth = 150;
        private const double DefaultNodeHeight = 70;
        private const double AnchorSize = 14;
        private const double MinZoom = 0.25;
        private const double MaxZoom = 3.0;
        private const int ExecutionStepDelayMilliseconds = 500;
        private const int ExecutionPollingIntervalMilliseconds = 50;
        private const int MaxExecutionSteps = 500;
        // 连线避障时，节点矩形会向外扩这个距离，避免折线贴着节点边缘走。
        private const double ConnectionClearance = FlowchartOrthogonalRouter.DefaultClearance;
        private static readonly JsonSerializerOptions DocumentJsonOptions = CreateDocumentJsonOptions();

        // _nodes 是模型数据；_nodeVisuals 和 _nodeOutlines 是模型到界面元素的索引。
        private readonly List<FlowchartNodeModel> _nodes = new List<FlowchartNodeModel>();
        private readonly List<FlowchartConnectionModel> _connections = new List<FlowchartConnectionModel>();
        private readonly Dictionary<Guid, FlowchartNodeModel> _nodesById = new Dictionary<Guid, FlowchartNodeModel>();
        private readonly Dictionary<Guid, FrameworkElement> _nodeVisuals = new Dictionary<Guid, FrameworkElement>();
        private readonly Dictionary<Guid, FrameworkElement> _nodeOutlines = new Dictionary<Guid, FrameworkElement>();

        private bool _isViewportInitialized;
        private bool _isPanning;
        private Point _panStartPoint;
        private double _panStartTranslateX;
        private double _panStartTranslateY;

        private FlowchartNodeModel? _draggingNode;
        private Point _nodeDragStartWorldPoint;
        private double _nodeDragStartX;
        private double _nodeDragStartY;

        // 创建连线时记录起点节点和起点锚点，鼠标移动时用它们生成预览折线。
        private bool _isConnecting;
        private FlowchartNodeModel? _connectionSourceNode;
        private FlowchartAnchor _connectionSourceAnchor;
        private Polyline? _previewLine;
        private Polygon? _previewArrow;

        private Guid? _selectedNodeId;
        private Guid? _selectedConnectionId;
        private Guid? _executingNodeId;
        private Guid? _executingConnectionId;
        private string? _lastProcessedDragId;
        private bool _isExecuting;
        private bool _isExecutionPaused;
        private bool _isApplyingDocumentProperty;
        private bool _isUpdatingDocumentProperty;
        private CancellationTokenSource? _executionCancellationTokenSource;
        private TaskCompletionSource<bool>? _executionResumeSignal;

        public FlowchartEditorControl()
        {
            InitializeComponent();
        }

        public static readonly DependencyProperty DocumentProperty =
            DependencyProperty.Register(
                nameof(Document),
                typeof(FlowchartDocument),
                typeof(FlowchartEditorControl),
                new FrameworkPropertyMetadata(
                    null,
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    OnDocumentChanged));

        public event EventHandler<FlowchartExecutionStepEventArgs>? ExecutionStepChanged;

        public event EventHandler<FlowchartNodeInteractionEventArgs>? NodeDoubleClick;

        public FlowchartDocument? Document
        {
            get => (FlowchartDocument?)GetValue(DocumentProperty);
            set => SetValue(DocumentProperty, value);
        }

        public bool IsExecuting => _isExecuting;

        public bool IsExecutionPaused => _isExecuting && _isExecutionPaused;

        #region 页面命令公开方法

        /// <summary>
        /// 供流程图页面命令在当前视口中心添加节点。
        /// </summary>
        public void AddNodeAtCenter(string text, FlowchartNodeKind kind)
        {
            EnsureWorkspaceInitialized();
            AddNode(text, kind, new Point(Viewport.ActualWidth / 2, Viewport.ActualHeight / 2));
        }

        /// <summary>
        /// 获取当前画布的纯数据快照，供配置页保存多个流程图。
        /// </summary>
        public FlowchartDocument CreateDocumentSnapshot()
        {
            return CloneDocument(CreateDocument());
        }

        /// <summary>
        /// 从纯数据快照重建画布，供配置页切换当前流程图。
        /// </summary>
        public void LoadDocumentSnapshot(FlowchartDocument? document)
        {
            ApplyDocument(document);
            SynchronizeDocumentProperty();
        }

        /// <summary>
        /// 供流程图页面命令清空当前文档。
        /// </summary>
        public void ClearDocument()
        {
            LoadDocumentSnapshot(new FlowchartDocument());
        }

        #endregion

        public void SaveToFile(string filePath)
        {
            // 先把当前画布模型整理成纯数据对象，再统一序列化成 JSON 文件。
            FlowchartDocument document = CreateDocument();
            string json = JsonSerializer.Serialize(document, DocumentJsonOptions);
            File.WriteAllText(filePath, json, Encoding.UTF8);
        }

        public void LoadFromFile(string filePath)
        {
            // 从本地文件读取后直接反序列化，再交给 LoadDocument 重建画布元素。
            string json = File.ReadAllText(filePath, Encoding.UTF8);
            FlowchartDocument? document = JsonSerializer.Deserialize<FlowchartDocument>(json, DocumentJsonOptions);
            if (document is null)
            {
                throw new InvalidDataException("流程图文件内容为空或格式不正确。");
            }

            LoadDocumentSnapshot(document);
        }

        public async Task<FlowchartExecutionResult> ExecuteFlowAsync()
        {
            if (_isExecuting)
            {
                return new FlowchartExecutionResult(false, "流程图正在执行中。", Array.Empty<string>());
            }

            _isExecuting = true;
            _isExecutionPaused = false;
            _executionResumeSignal = null;
            _executionCancellationTokenSource = new CancellationTokenSource();
            CancellationToken cancellationToken = _executionCancellationTokenSource.Token;
            List<string> steps = new List<string>();

            try
            {
                // 优先从开始节点启动；如果没有开始节点，就按左上到右下的顺序找第一个节点。
                FlowchartNodeModel? currentNode = GetExecutionStartNode();
                if (currentNode is null)
                {
                    return new FlowchartExecutionResult(false, "流程图为空，无法执行。", steps);
                }

                for (int stepIndex = 1; stepIndex <= MaxExecutionSteps; stepIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await WaitIfExecutionPausedAsync(cancellationToken);

                    SetExecutionHighlight(currentNode.Id, null);

                    string stepMessage = $"步骤 {stepIndex}: {currentNode.Text}";
                    steps.Add(stepMessage);
                    ExecutionStepChanged?.Invoke(
                        this,
                        new FlowchartExecutionStepEventArgs(stepIndex, currentNode.Id, currentNode.Text, currentNode.Kind, stepMessage));

                    await WaitForExecutionDelayAsync(ExecutionStepDelayMilliseconds, cancellationToken);

                    if (currentNode.Kind == FlowchartNodeKind.End)
                    {
                        return new FlowchartExecutionResult(true, $"执行完成，共执行 {stepIndex} 个步骤。", steps);
                    }

                    DecisionExecutionState decisionState = DecisionExecutionState.NotConfigured;
                    if (currentNode.Kind == FlowchartNodeKind.Decision)
                    {
                        if (!TryEvaluateDecisionNode(currentNode, out decisionState, out string decisionMessage))
                        {
                            steps.Add(decisionMessage);
                            return new FlowchartExecutionResult(false, decisionMessage, steps);
                        }

                        if (decisionState != DecisionExecutionState.NotConfigured)
                        {
                            steps.Add($"判断结果：{(decisionState == DecisionExecutionState.Success ? "是" : "否")}");
                        }
                    }

                    // 连线的选择规则统一封装，判断节点在配置判断方法后按 bool 结果走“是/否”分支。
                    FlowchartConnectionModel? nextConnection = GetNextExecutionConnection(currentNode, decisionState);
                    if (nextConnection is null)
                    {
                        return new FlowchartExecutionResult(true, $"执行停止：节点“{currentNode.Text}”没有后续连线。", steps);
                    }

                    SetExecutionHighlight(currentNode.Id, nextConnection.Id);
                    await WaitForExecutionDelayAsync(ExecutionStepDelayMilliseconds / 2, cancellationToken);

                    if (!_nodesById.TryGetValue(nextConnection.TargetNodeId, out FlowchartNodeModel? nextNode))
                    {
                        return new FlowchartExecutionResult(false, $"执行失败：节点“{currentNode.Text}”的下一节点不存在。", steps);
                    }

                    currentNode = nextNode;
                }

                return new FlowchartExecutionResult(false, $"执行停止：超过最大步骤数 {MaxExecutionSteps}，请检查是否存在循环。", steps);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return new FlowchartExecutionResult(false, $"执行已结束，共执行 {steps.Count} 个步骤。", steps);
            }
            finally
            {
                _isExecuting = false;
                _isExecutionPaused = false;
                _executionResumeSignal = null;
                _executionCancellationTokenSource?.Dispose();
                _executionCancellationTokenSource = null;
                SetExecutionHighlight(null, null);
            }
        }

        public bool PauseExecution()
        {
            if (!_isExecuting || _isExecutionPaused)
            {
                return false;
            }

            // 暂停时创建一个等待信号，执行循环会在安全点挂起，直到点击继续。
            _isExecutionPaused = true;
            _executionResumeSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            return true;
        }

        public bool ResumeExecution()
        {
            if (!_isExecuting || !_isExecutionPaused)
            {
                return false;
            }

            _isExecutionPaused = false;
            _executionResumeSignal?.TrySetResult(true);
            _executionResumeSignal = null;
            return true;
        }

        public bool StopExecution()
        {
            if (!_isExecuting)
            {
                return false;
            }

            // 结束执行要同时取消延时等待，并解除暂停状态，避免卡在暂停点上。
            _isExecutionPaused = false;
            _executionResumeSignal?.TrySetResult(true);
            _executionResumeSignal = null;
            _executionCancellationTokenSource?.Cancel();
            return true;
        }

        private void FlowchartEditorControl_Loaded(object sender, RoutedEventArgs e)
        {
            EnsureWorkspaceInitialized();
        }

        private void Viewport_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            EnsureWorkspaceInitialized();
        }

        private void EnsureWorkspaceInitialized()
        {
            if (_isViewportInitialized || Viewport.ActualWidth <= 0 || Viewport.ActualHeight <= 0)
            {
                return;
            }

            WorkspaceScaleTransform.ScaleX = 1;
            WorkspaceScaleTransform.ScaleY = 1;
            WorkspaceTranslateTransform.X = (Viewport.ActualWidth / 2) - (WorkspaceSize / 2);
            WorkspaceTranslateTransform.Y = (Viewport.ActualHeight / 2) - (WorkspaceSize / 2);
            _isViewportInitialized = true;
        }

        private static JsonSerializerOptions CreateDocumentJsonOptions()
        {
            JsonSerializerOptions options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNameCaseInsensitive = true
            };
            options.Converters.Add(new JsonStringEnumConverter());
            return options;
        }

        private static void OnDocumentChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
        {
            if (dependencyObject is not FlowchartEditorControl editor || editor._isUpdatingDocumentProperty)
            {
                return;
            }

            editor.ApplyDocument(e.NewValue as FlowchartDocument);
        }

        private void ApplyDocument(FlowchartDocument? document)
        {
            StopExecution();
            _isApplyingDocumentProperty = true;

            try
            {
                LoadDocument(CloneDocument(document));
                EnsureWorkspaceInitialized();
                Focus();
            }
            finally
            {
                _isApplyingDocumentProperty = false;
            }
        }

        private void SynchronizeDocumentProperty()
        {
            if (_isApplyingDocumentProperty)
            {
                return;
            }

            _isUpdatingDocumentProperty = true;

            try
            {
                SetCurrentValue(DocumentProperty, CreateDocumentSnapshot());
            }
            finally
            {
                _isUpdatingDocumentProperty = false;
            }
        }

        private static FlowchartDocument CloneDocument(FlowchartDocument? document)
        {
            if (document is null)
            {
                return new FlowchartDocument();
            }

            return new FlowchartDocument
            {
                Version = document.Version,
                Nodes = (document.Nodes ?? new List<FlowchartNodeDocument>())
                    .Select(node => new FlowchartNodeDocument
                    {
                        Id = node.Id,
                        Text = node.Text ?? string.Empty,
                        MetadataJson = node.MetadataJson ?? string.Empty,
                        Kind = node.Kind,
                        X = node.X,
                        Y = node.Y,
                        Width = node.Width,
                        Height = node.Height
                    })
                    .ToList(),
                Connections = (document.Connections ?? new List<FlowchartConnectionDocument>())
                    .Select(connection => new FlowchartConnectionDocument
                    {
                        Id = connection.Id,
                        SourceNodeId = connection.SourceNodeId,
                        SourceAnchor = connection.SourceAnchor,
                        TargetNodeId = connection.TargetNodeId,
                        TargetAnchor = connection.TargetAnchor
                    })
                    .ToList()
            };
        }

        private FlowchartDocument CreateDocument()
        {
            // 节点和连接分开保存，文件里不混入 WPF 控件对象，便于长期兼容和调试。
            return new FlowchartDocument
            {
                Nodes = _nodes.Select(node => new FlowchartNodeDocument
                {
                    Id = node.Id,
                    Text = node.Text,
                    MetadataJson = node.MetadataJson,
                    Kind = node.Kind,
                    X = node.X,
                    Y = node.Y,
                    Width = node.Width,
                    Height = node.Height
                }).ToList(),
                Connections = _connections.Select(connection => new FlowchartConnectionDocument
                {
                    Id = connection.Id,
                    SourceNodeId = connection.SourceNodeId,
                    SourceAnchor = connection.SourceAnchor,
                    TargetNodeId = connection.TargetNodeId,
                    TargetAnchor = connection.TargetAnchor
                }).ToList()
            };
        }

        private void LoadDocument(FlowchartDocument document)
        {
            // 先清空现有画布，再按照文件内容重建模型、映射表和可视元素。
            ClearPreviewConnection();
            _nodes.Clear();
            _connections.Clear();
            _nodesById.Clear();
            _nodeVisuals.Clear();
            _nodeOutlines.Clear();
            NodesCanvas.Children.Clear();
            ConnectionsCanvas.Children.Clear();
            _selectedNodeId = null;
            _selectedConnectionId = null;
            _executingNodeId = null;
            _executingConnectionId = null;

            foreach (FlowchartNodeDocument nodeDocument in document.Nodes)
            {
                FlowchartNodeModel node = new FlowchartNodeModel
                {
                    Id = nodeDocument.Id == Guid.Empty ? Guid.NewGuid() : nodeDocument.Id,
                    Text = nodeDocument.Text ?? string.Empty,
                    MetadataJson = nodeDocument.MetadataJson ?? string.Empty,
                    Kind = Enum.IsDefined(typeof(FlowchartNodeKind), nodeDocument.Kind) ? nodeDocument.Kind : FlowchartNodeKind.Process,
                    Width = nodeDocument.Width > 0 ? nodeDocument.Width : DefaultNodeWidth,
                    Height = nodeDocument.Height > 0 ? nodeDocument.Height : DefaultNodeHeight
                };
                node.X = Clamp(nodeDocument.X, 0, WorkspaceSize - node.Width);
                node.Y = Clamp(nodeDocument.Y, 0, WorkspaceSize - node.Height);

                _nodes.Add(node);
                _nodesById[node.Id] = node;
                CreateNodeVisual(node);
            }

            foreach (FlowchartConnectionDocument connectionDocument in document.Connections)
            {
                if (!_nodesById.TryGetValue(connectionDocument.SourceNodeId, out FlowchartNodeModel? sourceNode) ||
                    !_nodesById.TryGetValue(connectionDocument.TargetNodeId, out FlowchartNodeModel? targetNode))
                {
                    continue;
                }

                if (!CanUseAnchor(sourceNode, connectionDocument.SourceAnchor) ||
                    !CanUseAnchor(targetNode, connectionDocument.TargetAnchor))
                {
                    continue;
                }

                FlowchartConnectionModel connection = new FlowchartConnectionModel
                {
                    Id = connectionDocument.Id == Guid.Empty ? Guid.NewGuid() : connectionDocument.Id,
                    SourceNodeId = connectionDocument.SourceNodeId,
                    SourceAnchor = connectionDocument.SourceAnchor,
                    TargetNodeId = connectionDocument.TargetNodeId,
                    TargetAnchor = connectionDocument.TargetAnchor
                };

                _connections.Add(connection);
            }

            UpdateNodeSelectionVisuals();
            RenderConnections();
            Focus();
        }

        private FlowchartNodeModel? GetExecutionStartNode()
        {
            return _nodes
                .OrderByDescending(node => node.Kind == FlowchartNodeKind.Start)
                .ThenBy(node => node.Y)
                .ThenBy(node => node.X)
                .FirstOrDefault();
        }

        private FlowchartConnectionModel? GetNextExecutionConnection(
            FlowchartNodeModel node,
            DecisionExecutionState decisionState = DecisionExecutionState.NotConfigured)
        {
            // 这里只看当前节点的出边，不会跨节点搜索，保证执行顺序和连线关系一致。
            List<FlowchartConnectionModel> outgoingConnections = _connections
                .Where(connection => connection.SourceNodeId == node.Id)
                .ToList();

            if (outgoingConnections.Count == 0)
            {
                return null;
            }

            if (node.Kind == FlowchartNodeKind.Decision)
            {
                if (decisionState == DecisionExecutionState.Success)
                {
                    return outgoingConnections.FirstOrDefault(connection => connection.SourceAnchor == FlowchartAnchor.Right);
                }

                if (decisionState == DecisionExecutionState.Failure)
                {
                    return outgoingConnections.FirstOrDefault(connection => connection.SourceAnchor == FlowchartAnchor.Left);
                }

                return outgoingConnections.FirstOrDefault(connection => connection.SourceAnchor == FlowchartAnchor.Right) ??
                       outgoingConnections.FirstOrDefault(connection => connection.SourceAnchor == FlowchartAnchor.Left) ??
                       outgoingConnections.FirstOrDefault();
            }

            FlowchartAnchor[] priorityOrder = new[]
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

        private static bool TryEvaluateDecisionNode(
            FlowchartNodeModel node,
            out DecisionExecutionState decisionState,
            out string message)
        {
            decisionState = DecisionExecutionState.NotConfigured;
            message = string.Empty;

            if (string.IsNullOrWhiteSpace(node.MetadataJson))
            {
                return true;
            }

            try
            {
                using JsonDocument document = JsonDocument.Parse(node.MetadataJson);
                JsonElement root = document.RootElement;

                string operationObject = GetJsonString(root, "OperationObject");
                string invokeMethod = GetJsonString(root, "InvokeMethod");
                if (!IsJudgeOperationObject(operationObject) && !IsJudgeMethod(invokeMethod))
                {
                    return true;
                }

                if (string.IsNullOrWhiteSpace(invokeMethod))
                {
                    message = $"执行失败：判断节点“{ResolveExecutionNodeName(node)}”未配置判断方法。";
                    return false;
                }

                IReadOnlyList<DecisionParameter> parameters = ReadDecisionParameters(root);
                if (!TryEvaluateJudgeMethod(invokeMethod, parameters, out bool result, out string errorMessage))
                {
                    message = $"执行失败：判断节点“{ResolveExecutionNodeName(node)}”{errorMessage}";
                    return false;
                }

                decisionState = result ? DecisionExecutionState.Success : DecisionExecutionState.Failure;
                return true;
            }
            catch
            {
                return true;
            }
        }

        private static IReadOnlyList<DecisionParameter> ReadDecisionParameters(JsonElement root)
        {
            if (!root.TryGetProperty("Parameters", out JsonElement parametersElement) ||
                parametersElement.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<DecisionParameter>();
            }

            List<DecisionParameter> parameters = new List<DecisionParameter>();
            foreach (JsonElement parameterElement in parametersElement.EnumerateArray())
            {
                parameters.Add(new DecisionParameter(
                    GetJsonInt32(parameterElement, "Sequence"),
                    GetFirstNonEmpty(
                        GetJsonString(parameterElement, "ParameterName"),
                        GetJsonString(parameterElement, "Remark"),
                        GetJsonString(parameterElement, "Description"),
                        GetJsonString(parameterElement, "Name")),
                    GetJsonString(parameterElement, "Value")));
            }

            return parameters
                .OrderBy(parameter => parameter.Sequence)
                .ToList();
        }

        private static bool TryEvaluateJudgeMethod(
            string invokeMethod,
            IReadOnlyList<DecisionParameter> parameters,
            out bool result,
            out string message)
        {
            result = false;
            message = string.Empty;

            switch (invokeMethod.Trim())
            {
                case "等于判断":
                    if (!TryGetBinaryJudgeValues(parameters, "左值", "右值", out string equalsLeft, out string equalsRight, out message))
                    {
                        return false;
                    }

                    result = TextEquals(equalsLeft, equalsRight);
                    return true;
                case "不等判断":
                    if (!TryGetBinaryJudgeValues(parameters, "左值", "右值", out string notEqualsLeft, out string notEqualsRight, out message))
                    {
                        return false;
                    }

                    result = !TextEquals(notEqualsLeft, notEqualsRight);
                    return true;
                case "大于判断":
                    if (!TryGetBinaryJudgeValues(parameters, "左值", "右值", out string greaterLeft, out string greaterRight, out message))
                    {
                        return false;
                    }

                    result = CompareDecisionValues(greaterLeft, greaterRight) > 0;
                    return true;
                case "大于等于判断":
                    if (!TryGetBinaryJudgeValues(parameters, "左值", "右值", out string greaterThanOrEqualLeft, out string greaterThanOrEqualRight, out message))
                    {
                        return false;
                    }

                    result = CompareDecisionValues(greaterThanOrEqualLeft, greaterThanOrEqualRight) >= 0;
                    return true;
                case "小于判断":
                    if (!TryGetBinaryJudgeValues(parameters, "左值", "右值", out string lessLeft, out string lessRight, out message))
                    {
                        return false;
                    }

                    result = CompareDecisionValues(lessLeft, lessRight) < 0;
                    return true;
                case "小于等于判断":
                    if (!TryGetBinaryJudgeValues(parameters, "左值", "右值", out string lessThanOrEqualLeft, out string lessThanOrEqualRight, out message))
                    {
                        return false;
                    }

                    result = CompareDecisionValues(lessThanOrEqualLeft, lessThanOrEqualRight) <= 0;
                    return true;
                case "包含判断":
                    if (!TryGetBinaryJudgeValues(parameters, "待判断值", "关键字", out string containsSource, out string containsKeyword, out message))
                    {
                        return false;
                    }

                    result = containsSource.Contains(containsKeyword, StringComparison.OrdinalIgnoreCase);
                    return true;
                case "不包含判断":
                    if (!TryGetBinaryJudgeValues(parameters, "待判断值", "关键字", out string notContainsSource, out string notContainsKeyword, out message))
                    {
                        return false;
                    }

                    result = !notContainsSource.Contains(notContainsKeyword, StringComparison.OrdinalIgnoreCase);
                    return true;
                case "为空判断":
                    if (!TryGetUnaryJudgeValue(parameters, "待判断值", out string isEmptyValue, out message))
                    {
                        return false;
                    }

                    result = string.IsNullOrWhiteSpace(isEmptyValue);
                    return true;
                case "不为空判断":
                    if (!TryGetUnaryJudgeValue(parameters, "待判断值", out string isNotEmptyValue, out message))
                    {
                        return false;
                    }

                    result = !string.IsNullOrWhiteSpace(isNotEmptyValue);
                    return true;
                default:
                    message = $"配置了未识别的判断方法“{invokeMethod}”。";
                    return false;
            }
        }

        private static bool TryGetUnaryJudgeValue(
            IReadOnlyList<DecisionParameter> parameters,
            string parameterName,
            out string value,
            out string message)
        {
            if (TryGetDecisionParameterValue(parameters, 0, out value, parameterName))
            {
                message = string.Empty;
                return true;
            }

            message = $"缺少“{parameterName}”参数。";
            return false;
        }

        private static bool TryGetBinaryJudgeValues(
            IReadOnlyList<DecisionParameter> parameters,
            string leftParameterName,
            string rightParameterName,
            out string leftValue,
            out string rightValue,
            out string message)
        {
            if (!TryGetDecisionParameterValue(parameters, 0, out leftValue, leftParameterName))
            {
                rightValue = string.Empty;
                message = $"缺少“{leftParameterName}”参数。";
                return false;
            }

            if (!TryGetDecisionParameterValue(parameters, 1, out rightValue, rightParameterName))
            {
                message = $"缺少“{rightParameterName}”参数。";
                return false;
            }

            message = string.Empty;
            return true;
        }

        private static bool TryGetDecisionParameterValue(
            IReadOnlyList<DecisionParameter> parameters,
            int fallbackIndex,
            out string value,
            params string[] candidateNames)
        {
            foreach (string candidateName in candidateNames.Where(name => !string.IsNullOrWhiteSpace(name)))
            {
                DecisionParameter? matchedParameter = parameters.FirstOrDefault(parameter => TextEquals(parameter.Name, candidateName));
                if (matchedParameter is not null)
                {
                    value = matchedParameter.Value;
                    return true;
                }
            }

            if (fallbackIndex >= 0 && fallbackIndex < parameters.Count)
            {
                value = parameters[fallbackIndex].Value;
                return true;
            }

            value = string.Empty;
            return false;
        }

        private static int CompareDecisionValues(string leftValue, string rightValue)
        {
            if (TryParseDecimal(leftValue, out decimal leftNumber) &&
                TryParseDecimal(rightValue, out decimal rightNumber))
            {
                return leftNumber.CompareTo(rightNumber);
            }

            return string.Compare(leftValue?.Trim(), rightValue?.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryParseDecimal(string value, out decimal number)
        {
            return decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number) ||
                   decimal.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out number);
        }

        private static bool IsJudgeMethod(string invokeMethod)
        {
            return invokeMethod.Trim() switch
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

        private static bool IsJudgeOperationObject(string operationObject)
        {
            return string.Equals(operationObject?.Trim(), "判断", StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveExecutionNodeName(FlowchartNodeModel node)
        {
            string[] lines = (node.Text ?? string.Empty)
                .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);

            return lines
                .Select(line => line?.Trim() ?? string.Empty)
                .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line))
                ?? GetDefaultNodeTitle(node.Kind);
        }

        private void SetExecutionHighlight(Guid? nodeId, Guid? connectionId)
        {
            _executingNodeId = nodeId;
            _executingConnectionId = connectionId;
            UpdateNodeSelectionVisuals();
            RenderConnections();
        }

        private async Task WaitForExecutionDelayAsync(int delayMilliseconds, CancellationToken cancellationToken)
        {
            int remainingDelay = delayMilliseconds;
            while (remainingDelay > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await WaitIfExecutionPausedAsync(cancellationToken);

                int currentDelay = Math.Min(remainingDelay, ExecutionPollingIntervalMilliseconds);
                await Task.Delay(currentDelay, cancellationToken);
                remainingDelay -= currentDelay;
            }
        }

        private async Task WaitIfExecutionPausedAsync(CancellationToken cancellationToken)
        {
            while (_isExecutionPaused)
            {
                TaskCompletionSource<bool>? resumeSignal = _executionResumeSignal;
                if (resumeSignal is null)
                {
                    return;
                }

                await resumeSignal.Task.WaitAsync(cancellationToken);
            }
        }

        private void Viewport_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.StringFormat) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void Viewport_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.StringFormat))
            {
                return;
            }

            string? dragId = e.Data.GetDataPresent(FlowchartDragDataFormats.DragId)
                ? e.Data.GetData(FlowchartDragDataFormats.DragId)?.ToString()
                : null;

            if (!string.IsNullOrWhiteSpace(dragId) && string.Equals(_lastProcessedDragId, dragId, StringComparison.Ordinal))
            {
                e.Handled = true;
                return;
            }

            string nodeText = e.Data.GetDataPresent(FlowchartDragDataFormats.PaletteText)
                ? e.Data.GetData(FlowchartDragDataFormats.PaletteText)?.ToString() ?? string.Empty
                : e.Data.GetData(DataFormats.StringFormat)?.ToString() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(nodeText))
            {
                return;
            }

            // 新版拖拽数据会直接传节点类型；旧版只传文本时，通过文本兜底识别“判断”节点。
            FlowchartNodeKind nodeKind = ResolveNodeKind(nodeText);
            if (e.Data.GetDataPresent(FlowchartDragDataFormats.PaletteNodeKind))
            {
                string? nodeKindValue = e.Data.GetData(FlowchartDragDataFormats.PaletteNodeKind)?.ToString();
                if (Enum.TryParse(nodeKindValue, out FlowchartNodeKind parsedNodeKind))
                {
                    nodeKind = parsedNodeKind;
                }
            }

            _lastProcessedDragId = dragId;
            AddNode(nodeText, nodeKind, e.GetPosition(Viewport));
            e.Handled = true;
        }

        private void AddNode(string text, FlowchartNodeKind kind, Point viewportPoint)
        {
            Point worldPoint = ViewportToWorld(viewportPoint);
            FlowchartNodeModel node = new FlowchartNodeModel
            {
                Text = text,
                Kind = kind,
                Width = DefaultNodeWidth,
                Height = DefaultNodeHeight,
                X = Clamp(worldPoint.X - (DefaultNodeWidth / 2), 0, WorkspaceSize - DefaultNodeWidth),
                Y = Clamp(worldPoint.Y - (DefaultNodeHeight / 2), 0, WorkspaceSize - DefaultNodeHeight)
            };

            // 节点模型和界面元素分开保存：模型负责坐标/类型，界面元素负责实际绘制和鼠标事件。
            _nodes.Add(node);
            _nodesById[node.Id] = node;
            CreateNodeVisual(node);
            SelectNode(node.Id);
            Focus();
            SynchronizeDocumentProperty();
        }

        private void CreateNodeVisual(FlowchartNodeModel node)
        {
            // Grid 是整个节点的命中区域；内部的 Border/Polygon 只负责外形。
            Grid nodeRoot = new Grid
            {
                Width = node.Width,
                Height = node.Height,
                Background = Brushes.Transparent,
                Cursor = Cursors.SizeAll,
                Tag = node
            };

            // 判断节点用横向菱形，其他节点保持原来的圆角矩形。
            FrameworkElement nodeOutline = node.Kind == FlowchartNodeKind.Decision
                ? CreateDecisionOutline(node)
                : CreateRectangleOutline();

            nodeRoot.Children.Add(nodeOutline);
            if (node.Kind == FlowchartNodeKind.Decision)
            {
                nodeRoot.Children.Add(CreateDecisionBranchLabel("\u5426", HorizontalAlignment.Left));
                nodeRoot.Children.Add(CreateDecisionBranchLabel("\u662f", HorizontalAlignment.Right));
            }

            nodeRoot.Children.Add(CreateNodeContent(node));
            foreach (FlowchartAnchor anchor in GetAvailableAnchors(node))
            {
                nodeRoot.Children.Add(CreateAnchorHandle(node, anchor));
            }

            nodeRoot.MouseLeftButtonDown += NodeRoot_MouseLeftButtonDown;
            nodeRoot.MouseMove += NodeRoot_MouseMove;
            nodeRoot.MouseLeftButtonUp += NodeRoot_MouseLeftButtonUp;

            Canvas.SetLeft(nodeRoot, node.X);
            Canvas.SetTop(nodeRoot, node.Y);
            Canvas.SetZIndex(nodeRoot, 10);

            NodesCanvas.Children.Add(nodeRoot);
            _nodeVisuals[node.Id] = nodeRoot;
            _nodeOutlines[node.Id] = nodeOutline;
            UpdateNodeSelectionVisuals();
        }

        private static FrameworkElement CreateNodeContent(FlowchartNodeModel node)
        {
            (string title, string summary) = ResolveNodeDisplay(node);

            if (node.Kind == FlowchartNodeKind.Decision)
            {
                string decisionText = string.IsNullOrWhiteSpace(summary) ? title : summary;
                return CreateDecisionNodeContent(decisionText);
            }

            return node.Kind == FlowchartNodeKind.Process
                ? CreateProcessNodeContent(title, summary)
                : CreateDefaultNodeContent(string.IsNullOrWhiteSpace(summary) ? title : $"{title} {summary}");
        }

        private static TextBlock CreateDefaultNodeContent(string text)
        {
            TextBlock textBlock = new TextBlock
            {
                Text = text,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 8, 10, 8),
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center
            };
            textBlock.SetResourceReference(TextBlock.ForegroundProperty, "FlowchartNodeTextBrush");
            return textBlock;
        }

        private static FrameworkElement CreateProcessNodeContent(string operationObject, string summary)
        {
            StackPanel panel = new StackPanel
            {
                Margin = new Thickness(12, 8, 12, 8),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center
            };

            TextBlock titleBlock = new TextBlock
            {
                Text = operationObject,
                Foreground = Brushes.Black,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Left,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            titleBlock.SetResourceReference(FrameworkElement.StyleProperty, "SectionTitleStyle");
            panel.Children.Add(titleBlock);

            if (!string.IsNullOrWhiteSpace(summary))
            {
                TextBlock summaryBlock = new TextBlock
                {
                    Margin = new Thickness(0, 4, 0, 0),
                    Text = summary,
                    FontSize = 13,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    TextAlignment = TextAlignment.Left,
                    TextWrapping = TextWrapping.NoWrap,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                summaryBlock.SetResourceReference(TextBlock.ForegroundProperty, "FlowchartNodeTextBrush");
                panel.Children.Add(summaryBlock);
            }

            return panel;
        }

        private static FrameworkElement CreateDecisionNodeContent(string text)
        {
            TextBlock textBlock = new TextBlock
            {
                Text = text,
                Margin = new Thickness(12, 8, 12, 8),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            textBlock.SetResourceReference(FrameworkElement.StyleProperty, "SectionTitleStyle");
            textBlock.SetResourceReference(TextBlock.ForegroundProperty, "FlowchartNodeTextBrush");
            return textBlock;
        }

        private static (string Title, string Summary) ResolveNodeDisplay(FlowchartNodeModel node)
        {
            if (TryReadOperationDisplay(node.MetadataJson, out string operationObject, out string summary))
            {
                return (
                    string.IsNullOrWhiteSpace(operationObject) ? GetDefaultNodeTitle(node.Kind) : operationObject.Trim(),
                    summary);
            }

            string[] lines = (node.Text ?? string.Empty)
                .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None)
                .Select(line => line?.Trim() ?? string.Empty)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToArray();

            return (
                lines.FirstOrDefault() ?? GetDefaultNodeTitle(node.Kind),
                NormalizeInlineText(lines.Skip(1)));
        }

        private static bool TryReadOperationDisplay(string metadataJson, out string operationObject, out string summary)
        {
            operationObject = string.Empty;
            summary = string.Empty;

            if (string.IsNullOrWhiteSpace(metadataJson))
            {
                return false;
            }

            try
            {
                using JsonDocument document = JsonDocument.Parse(metadataJson);
                JsonElement root = document.RootElement;

                operationObject = GetJsonString(root, "OperationObject");
                summary = NormalizeInlineText(GetJsonString(root, "Remark"));

                return !string.IsNullOrWhiteSpace(operationObject) || !string.IsNullOrWhiteSpace(summary);
            }
            catch
            {
                return false;
            }
        }

        private static string GetJsonString(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out JsonElement property) ||
                property.ValueKind == JsonValueKind.Null ||
                property.ValueKind == JsonValueKind.Undefined)
            {
                return string.Empty;
            }

            return property.ToString() ?? string.Empty;
        }

        private static int GetJsonInt32(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out JsonElement property) ||
                property.ValueKind == JsonValueKind.Null ||
                property.ValueKind == JsonValueKind.Undefined)
            {
                return 0;
            }

            if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out int number))
            {
                return number;
            }

            return int.TryParse(property.ToString(), out int parsedNumber) ? parsedNumber : 0;
        }

        private static string GetFirstNonEmpty(params string[] values)
        {
            return values
                .Select(value => value?.Trim() ?? string.Empty)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
                ?? string.Empty;
        }

        private static string GetDefaultNodeTitle(FlowchartNodeKind nodeKind)
        {
            return nodeKind switch
            {
                FlowchartNodeKind.Decision => "判断",
                FlowchartNodeKind.Start => "开始",
                FlowchartNodeKind.End => "结束",
                _ => "处理"
            };
        }

        private static string NormalizeInlineText(string? text)
        {
            return NormalizeInlineText((text ?? string.Empty)
                .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None));
        }

        private static string NormalizeInlineText(IEnumerable<string> values)
        {
            return string.Join(
                " ",
                values.Select(value => value?.Trim() ?? string.Empty)
                    .Where(value => !string.IsNullOrWhiteSpace(value)));
        }

        private static bool TextEquals(string left, string right)
        {
            return string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static Border CreateRectangleOutline()
        {
            return new Border
            {
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(2),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B9B9B9")),
                Background = Brushes.White
            };
        }

        private static Polygon CreateDecisionOutline(FlowchartNodeModel node)
        {
            return new Polygon
            {
                // 四个点分别是上、右、下、左，所以视觉上是横向菱形。
                Points = new PointCollection(new[]
                {
                    new Point(node.Width / 2, 0),
                    new Point(node.Width, node.Height / 2),
                    new Point(node.Width / 2, node.Height),
                    new Point(0, node.Height / 2)
                }),
                Fill = Brushes.White,
                Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B9B9B9")),
                StrokeThickness = 2,
                Stretch = Stretch.None
            };
        }

        private TextBlock CreateDecisionBranchLabel(string text, HorizontalAlignment horizontalAlignment)
        {
            TextBlock label = new TextBlock
            {
                Text = text,
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = horizontalAlignment,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = horizontalAlignment == HorizontalAlignment.Left
                    ? new Thickness(-25, -25, 0, 0)
                    : new Thickness(0, -25, -25, 0),
                IsHitTestVisible = false
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, "FlowchartNodeTextBrush");
            return label;
        }

        private static IEnumerable<FlowchartAnchor> GetAvailableAnchors(FlowchartNodeModel node)
        {
            yield return FlowchartAnchor.Top;
            yield return FlowchartAnchor.Right;

            if (node.Kind != FlowchartNodeKind.Decision)
            {
                yield return FlowchartAnchor.Bottom;
            }

            yield return FlowchartAnchor.Left;
        }

        private static bool CanUseAnchor(FlowchartNodeModel node, FlowchartAnchor anchor)
        {
            return node.Kind != FlowchartNodeKind.Decision || anchor != FlowchartAnchor.Bottom;
        }

        private FrameworkElement CreateAnchorHandle(FlowchartNodeModel node, FlowchartAnchor anchor)
        {
            // 锚点小圆点放在节点四边中点；拖动它开始创建连线。
            Ellipse handle = new Ellipse
            {
                Width = AnchorSize,
                Height = AnchorSize,
                Fill = Brushes.White,
                Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2F80ED")),
                StrokeThickness = 2,
                Cursor = Cursors.Cross,
                Tag = new AnchorHandleInfo(node.Id, anchor),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            switch (anchor)
            {
                case FlowchartAnchor.Top:
                    handle.VerticalAlignment = VerticalAlignment.Top;
                    handle.Margin = new Thickness(0, -(AnchorSize / 2), 0, 0);
                    break;
                case FlowchartAnchor.Right:
                    handle.HorizontalAlignment = HorizontalAlignment.Right;
                    handle.Margin = new Thickness(0, 0, -(AnchorSize / 2), 0);
                    break;
                case FlowchartAnchor.Bottom:
                    handle.VerticalAlignment = VerticalAlignment.Bottom;
                    handle.Margin = new Thickness(0, 0, 0, -(AnchorSize / 2));
                    break;
                default:
                    handle.HorizontalAlignment = HorizontalAlignment.Left;
                    handle.Margin = new Thickness(-(AnchorSize / 2), 0, 0, 0);
                    break;
            }

            handle.MouseLeftButtonDown += AnchorHandle_MouseLeftButtonDown;
            return handle;
        }

        private void NodeRoot_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement element || element.Tag is not FlowchartNodeModel node)
            {
                return;
            }

            if (e.OriginalSource is FrameworkElement sourceElement && sourceElement.Tag is AnchorHandleInfo)
            {
                return;
            }

            Focus();
            SelectNode(node.Id);

            if (e.ClickCount >= 2)
            {
                NodeDoubleClick?.Invoke(
                    this,
                    new FlowchartNodeInteractionEventArgs(node.Id, node.Text, node.Kind, node.MetadataJson));
                e.Handled = true;
                return;
            }

            _draggingNode = node;
            _nodeDragStartWorldPoint = ViewportToWorld(e.GetPosition(Viewport));
            _nodeDragStartX = node.X;
            _nodeDragStartY = node.Y;
            element.CaptureMouse();
            e.Handled = true;
        }

        private void NodeRoot_MouseMove(object sender, MouseEventArgs e)
        {
            if (_draggingNode is null || sender is not FrameworkElement element || !ReferenceEquals(_draggingNode, element.Tag))
            {
                return;
            }

            if (e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            Point worldPoint = ViewportToWorld(e.GetPosition(Viewport));
            double offsetX = worldPoint.X - _nodeDragStartWorldPoint.X;
            double offsetY = worldPoint.Y - _nodeDragStartWorldPoint.Y;

            _draggingNode.X = Clamp(_nodeDragStartX + offsetX, 0, WorkspaceSize - _draggingNode.Width);
            _draggingNode.Y = Clamp(_nodeDragStartY + offsetY, 0, WorkspaceSize - _draggingNode.Height);
            UpdateNodeVisualPosition(_draggingNode);
            RenderConnections();
            e.Handled = true;
        }

        private void NodeRoot_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_draggingNode is null)
            {
                return;
            }

            if (sender is UIElement element)
            {
                element.ReleaseMouseCapture();
            }

            _draggingNode = null;
            SynchronizeDocumentProperty();
            e.Handled = true;
        }

        private void AnchorHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement element || element.Tag is not AnchorHandleInfo handleInfo)
            {
                return;
            }

            if (!_nodesById.TryGetValue(handleInfo.NodeId, out FlowchartNodeModel? sourceNode))
            {
                return;
            }

            if (!CanUseAnchor(sourceNode, handleInfo.Anchor))
            {
                return;
            }

            Focus();
            SelectNode(sourceNode.Id);
            _isConnecting = true;
            _connectionSourceNode = sourceNode;
            _connectionSourceAnchor = handleInfo.Anchor;

            // 先画一条长度为 0 的预览线，鼠标移动时再更新成真正的避障折线。
            Point startPoint = sourceNode.GetAnchorPoint(handleInfo.Anchor);
            ShowPreviewConnection(startPoint, startPoint);
            Viewport.CaptureMouse();
            e.Handled = true;
        }

        private void Viewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Focus();
            ClearSelection();

            _isPanning = true;
            _panStartPoint = e.GetPosition(Viewport);
            _panStartTranslateX = WorkspaceTranslateTransform.X;
            _panStartTranslateY = WorkspaceTranslateTransform.Y;
            Viewport.CaptureMouse();
            Cursor = Cursors.SizeAll;
            e.Handled = true;
        }

        private void Viewport_MouseMove(object sender, MouseEventArgs e)
        {
            Point viewportPoint = e.GetPosition(Viewport);

            if (_isConnecting && _connectionSourceNode is not null)
            {
                UpdatePreviewConnection(_connectionSourceNode.GetAnchorPoint(_connectionSourceAnchor), ViewportToWorld(viewportPoint));
                e.Handled = true;
                return;
            }

            if (!_isPanning || e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            Vector offset = viewportPoint - _panStartPoint;
            WorkspaceTranslateTransform.X = _panStartTranslateX + offset.X;
            WorkspaceTranslateTransform.Y = _panStartTranslateY + offset.Y;
            e.Handled = true;
        }

        private void Viewport_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isConnecting)
            {
                CompleteConnection(e.GetPosition(Viewport));
                Viewport.ReleaseMouseCapture();
                _isConnecting = false;
                _connectionSourceNode = null;
                ClearPreviewConnection();
                e.Handled = true;
                return;
            }

            if (!_isPanning)
            {
                return;
            }

            _isPanning = false;
            Cursor = Cursors.Arrow;
            Viewport.ReleaseMouseCapture();
            e.Handled = true;
        }

        private void CompleteConnection(Point viewportPoint)
        {
            if (_connectionSourceNode is null)
            {
                return;
            }

            // 只有鼠标松开时落在另一个节点的锚点上，才会真正创建连接。
            if (!TryGetAnchorHandleAt(viewportPoint, out FlowchartNodeModel? targetNode, out FlowchartAnchor targetAnchor))
            {
                return;
            }

            if (targetNode is null || targetNode.Id == _connectionSourceNode.Id)
            {
                return;
            }

            if (!CanUseAnchor(_connectionSourceNode, _connectionSourceAnchor) ||
                !CanUseAnchor(targetNode, targetAnchor))
            {
                return;
            }

            bool exists = _connections.Any(connection =>
                connection.SourceNodeId == _connectionSourceNode.Id &&
                connection.SourceAnchor == _connectionSourceAnchor &&
                connection.TargetNodeId == targetNode.Id &&
                connection.TargetAnchor == targetAnchor);

            if (exists)
            {
                return;
            }

            FlowchartConnectionModel connectionModel = new FlowchartConnectionModel
            {
                SourceNodeId = _connectionSourceNode.Id,
                SourceAnchor = _connectionSourceAnchor,
                TargetNodeId = targetNode.Id,
                TargetAnchor = targetAnchor
            };

            _connections.Add(connectionModel);
            SelectConnection(connectionModel.Id);
            RenderConnections();
            SynchronizeDocumentProperty();
        }

        private bool TryGetAnchorHandleAt(Point viewportPoint, out FlowchartNodeModel? node, out FlowchartAnchor anchor)
        {
            node = null;
            anchor = FlowchartAnchor.Top;

            // WPF 命中测试拿到的是最内层元素，需要一路向父级查找锚点 Tag。
            HitTestResult? hitTestResult = VisualTreeHelper.HitTest(Viewport, viewportPoint);
            DependencyObject? current = hitTestResult?.VisualHit;

            while (current is not null)
            {
                if (current is FrameworkElement element && element.Tag is AnchorHandleInfo handleInfo)
                {
                    if (_nodesById.TryGetValue(handleInfo.NodeId, out FlowchartNodeModel? targetNode))
                    {
                        if (!CanUseAnchor(targetNode, handleInfo.Anchor))
                        {
                            return false;
                        }

                        node = targetNode;
                        anchor = handleInfo.Anchor;
                        return true;
                    }
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return false;
        }

        private void Viewport_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            Point viewportPoint = e.GetPosition(Viewport);
            Point worldPoint = ViewportToWorld(viewportPoint);
            double currentScale = WorkspaceScaleTransform.ScaleX;
            double nextScale = e.Delta > 0 ? currentScale * 1.1 : currentScale / 1.1;
            nextScale = Clamp(nextScale, MinZoom, MaxZoom);

            if (Math.Abs(nextScale - currentScale) < 0.0001)
            {
                return;
            }

            WorkspaceScaleTransform.ScaleX = nextScale;
            WorkspaceScaleTransform.ScaleY = nextScale;
            // 缩放时保持鼠标指向的世界坐标不变，用户会感觉是在以鼠标位置为中心缩放。
            WorkspaceTranslateTransform.X = viewportPoint.X - (worldPoint.X * nextScale);
            WorkspaceTranslateTransform.Y = viewportPoint.Y - (worldPoint.Y * nextScale);
            e.Handled = true;
        }

        private void FlowchartEditorControl_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Delete)
            {
                return;
            }

            if (_selectedNodeId.HasValue)
            {
                RemoveNode(_selectedNodeId.Value);
                e.Handled = true;
                return;
            }

            if (_selectedConnectionId.HasValue)
            {
                RemoveConnection(_selectedConnectionId.Value);
                e.Handled = true;
            }
        }

        private void RemoveNode(Guid nodeId)
        {
            _nodes.RemoveAll(node => node.Id == nodeId);
            _nodesById.Remove(nodeId);
            _connections.RemoveAll(connection => connection.SourceNodeId == nodeId || connection.TargetNodeId == nodeId);

            if (_nodeVisuals.TryGetValue(nodeId, out FrameworkElement? nodeVisual))
            {
                NodesCanvas.Children.Remove(nodeVisual);
                _nodeVisuals.Remove(nodeId);
            }

            _nodeOutlines.Remove(nodeId);
            _selectedNodeId = null;
            _selectedConnectionId = null;
            UpdateNodeSelectionVisuals();
            RenderConnections();
            SynchronizeDocumentProperty();
        }

        private void RemoveConnection(Guid connectionId)
        {
            _connections.RemoveAll(connection => connection.Id == connectionId);
            _selectedConnectionId = null;
            RenderConnections();
            SynchronizeDocumentProperty();
        }

        private void SelectNode(Guid nodeId)
        {
            _selectedNodeId = nodeId;
            _selectedConnectionId = null;
            UpdateNodeSelectionVisuals();
            RenderConnections();
        }

        private void SelectConnection(Guid connectionId)
        {
            _selectedConnectionId = connectionId;
            _selectedNodeId = null;
            UpdateNodeSelectionVisuals();
            RenderConnections();
        }

        private void ClearSelection()
        {
            _selectedNodeId = null;
            _selectedConnectionId = null;
            UpdateNodeSelectionVisuals();
            RenderConnections();
        }

        private void UpdateNodeSelectionVisuals()
        {
            Brush selectedBorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2F80ED"));
            Brush defaultBorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B9B9B9"));
            Brush selectedBackground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EDF5FF"));

            foreach (KeyValuePair<Guid, FrameworkElement> item in _nodeOutlines)
            {
                bool isSelected = _selectedNodeId == item.Key || _executingNodeId == item.Key;
                // 矩形节点是 Border，判断节点是 Polygon；两种外形分别更新边框和填充。
                if (item.Value is Border border)
                {
                    border.BorderBrush = isSelected ? selectedBorderBrush : defaultBorderBrush;
                    border.Background = isSelected ? selectedBackground : Brushes.White;
                }
                else if (item.Value is Shape shape)
                {
                    shape.Stroke = isSelected ? selectedBorderBrush : defaultBorderBrush;
                    shape.Fill = isSelected ? selectedBackground : Brushes.White;
                }
            }
        }

        private void RenderConnections()
        {
            ConnectionsCanvas.Children.Clear();

            foreach (FlowchartConnectionModel connection in _connections)
            {
                if (!_nodesById.TryGetValue(connection.SourceNodeId, out FlowchartNodeModel? sourceNode) ||
                    !_nodesById.TryGetValue(connection.TargetNodeId, out FlowchartNodeModel? targetNode))
                {
                    continue;
                }

                Point startPoint = sourceNode.GetAnchorPoint(connection.SourceAnchor);
                Point endPoint = targetNode.GetAnchorPoint(connection.TargetAnchor);
                // 每次渲染都重新路由，这样拖动节点后折线会自动重新避开所有节点。
                IReadOnlyList<Point> route = CreateConnectionRoute(sourceNode, connection.SourceAnchor, targetNode, connection.TargetAnchor);

                if ((endPoint - startPoint).Length < 0.1 || route.Count < 2)
                {
                    continue;
                }

                bool isSelected = _selectedConnectionId == connection.Id || _executingConnectionId == connection.Id;
                Brush lineBrush = isSelected
                    ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2F80ED"))
                    : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#666666"));

                // 透明粗折线专门用来命中鼠标，避免细线很难点中。
                Polyline hitTarget = new Polyline
                {
                    Points = new PointCollection(route),
                    Stroke = Brushes.Transparent,
                    StrokeThickness = 14,
                    StrokeLineJoin = PenLineJoin.Round,
                    Tag = connection,
                    Cursor = Cursors.Hand
                };
                hitTarget.MouseLeftButtonDown += ConnectionElement_MouseLeftButtonDown;

                // 可见折线不参与命中，点击交给上面的透明粗折线处理。
                Polyline visibleLine = new Polyline
                {
                    Points = new PointCollection(route),
                    Stroke = lineBrush,
                    StrokeThickness = isSelected ? 3 : 2,
                    StrokeLineJoin = PenLineJoin.Round,
                    IsHitTestVisible = false
                };

                Polygon arrow = new Polygon
                {
                    Fill = lineBrush,
                    // 箭头方向取折线最后一段，避免拐弯后的箭头仍按首尾直线方向画。
                    Points = CreateArrowHead(route),
                    Tag = connection,
                    Cursor = Cursors.Hand
                };
                arrow.MouseLeftButtonDown += ConnectionElement_MouseLeftButtonDown;

                Canvas.SetZIndex(hitTarget, 1);
                Canvas.SetZIndex(visibleLine, 2);
                Canvas.SetZIndex(arrow, 3);

                ConnectionsCanvas.Children.Add(hitTarget);
                ConnectionsCanvas.Children.Add(visibleLine);
                ConnectionsCanvas.Children.Add(arrow);
            }
        }

        private void ConnectionElement_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement element || element.Tag is not FlowchartConnectionModel connection)
            {
                return;
            }

            Focus();
            SelectConnection(connection.Id);
            e.Handled = true;
        }

        private void ShowPreviewConnection(Point startPoint, Point endPoint)
        {
            ClearPreviewConnection();

            _previewLine = new Polyline
            {
                Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7A7A7A")),
                StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection(new[] { 5d, 4d }),
                StrokeLineJoin = PenLineJoin.Round,
                IsHitTestVisible = false
            };

            _previewArrow = new Polygon
            {
                Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7A7A7A")),
                IsHitTestVisible = false
            };

            ConnectionsCanvas.Children.Add(_previewLine);
            ConnectionsCanvas.Children.Add(_previewArrow);
            UpdatePreviewConnection(startPoint, endPoint);
        }

        private void UpdatePreviewConnection(Point startPoint, Point endPoint)
        {
            if (_previewLine is null || _previewArrow is null)
            {
                return;
            }

            // 预览线和正式连线使用同一个路由器，只是终点是当前鼠标位置。
            IReadOnlyList<Point> route = CreatePreviewRoute(startPoint, endPoint);
            _previewLine.Points = new PointCollection(route);
            _previewArrow.Points = CreateArrowHead(route);
        }

        private void ClearPreviewConnection()
        {
            if (_previewLine is not null)
            {
                ConnectionsCanvas.Children.Remove(_previewLine);
                _previewLine = null;
            }

            if (_previewArrow is not null)
            {
                ConnectionsCanvas.Children.Remove(_previewArrow);
                _previewArrow = null;
            }
        }

        private PointCollection CreateArrowHead(IReadOnlyList<Point> route)
        {
            if (route.Count < 2)
            {
                return new PointCollection();
            }

            Point endPoint = route[^1];
            // 从后往前找最后一段非零长度的线段，作为箭头朝向。
            for (int i = route.Count - 2; i >= 0; i--)
            {
                if ((endPoint - route[i]).Length >= 0.1)
                {
                    return CreateArrowHead(route[i], endPoint);
                }
            }

            return new PointCollection();
        }

        private static PointCollection CreateArrowHead(Point startPoint, Point endPoint)
        {
            Vector direction = startPoint - endPoint;
            if (direction.Length < 0.1)
            {
                return new PointCollection();
            }

            direction.Normalize();
            Vector perpendicular = new Vector(-direction.Y, direction.X);
            const double arrowLength = 14;
            const double arrowWidth = 6;

            Point basePoint = endPoint + (direction * arrowLength);
            Point point1 = basePoint + (perpendicular * arrowWidth);
            Point point2 = basePoint - (perpendicular * arrowWidth);

            return new PointCollection(new[] { endPoint, point1, point2 });
        }

        private IReadOnlyList<Point> CreateConnectionRoute(
            FlowchartNodeModel sourceNode,
            FlowchartAnchor sourceAnchor,
            FlowchartNodeModel targetNode,
            FlowchartAnchor targetAnchor)
        {
            // 路由器只关心几何信息：起终点、锚点方向、所有节点矩形和工作区范围。
            return FlowchartOrthogonalRouter.Route(
                sourceNode.GetAnchorPoint(sourceAnchor),
                sourceAnchor,
                targetNode.GetAnchorPoint(targetAnchor),
                targetAnchor,
                GetNodeBounds(),
                GetWorkspaceBounds(),
                ConnectionClearance);
        }

        private IReadOnlyList<Point> CreatePreviewRoute(Point startPoint, Point endPoint)
        {
            // 预览时没有目标锚点，路由器会根据鼠标相对起点的位置推断一个进入方向。
            return FlowchartOrthogonalRouter.RouteToPoint(
                startPoint,
                _connectionSourceAnchor,
                endPoint,
                GetNodeBounds(),
                GetWorkspaceBounds(),
                ConnectionClearance);
        }

        private IEnumerable<Rect> GetNodeBounds()
        {
            return _nodes.Select(node => node.GetBounds());
        }

        private static Rect GetWorkspaceBounds()
        {
            return new Rect(0, 0, WorkspaceSize, WorkspaceSize);
        }

        private static FlowchartNodeKind ResolveNodeKind(string text)
        {
            // 兼容旧的拖拽数据：如果只传了文本，也能把“判断”识别成菱形节点。
            return text.Trim() switch
            {
                "\u5f00\u59cb" => FlowchartNodeKind.Start,
                "\u5224\u65ad" => FlowchartNodeKind.Decision,
                "\u7ed3\u675f" => FlowchartNodeKind.End,
                _ => FlowchartNodeKind.Process
            };
        }

        private void UpdateNodeVisualPosition(FlowchartNodeModel node)
        {
            if (!_nodeVisuals.TryGetValue(node.Id, out FrameworkElement? nodeVisual))
            {
                return;
            }

            Canvas.SetLeft(nodeVisual, node.X);
            Canvas.SetTop(nodeVisual, node.Y);
        }

        private Point ViewportToWorld(Point viewportPoint)
        {
            double scale = WorkspaceScaleTransform.ScaleX;
            return new Point(
                (viewportPoint.X - WorkspaceTranslateTransform.X) / scale,
                (viewportPoint.Y - WorkspaceTranslateTransform.Y) / scale);
        }

        private static double Clamp(double value, double min, double max)
        {
            if (value < min)
            {
                return min;
            }

            if (value > max)
            {
                return max;
            }

            return value;
        }

        private sealed class AnchorHandleInfo
        {
            public AnchorHandleInfo(Guid nodeId, FlowchartAnchor anchor)
            {
                NodeId = nodeId;
                Anchor = anchor;
            }

            public Guid NodeId { get; }
            public FlowchartAnchor Anchor { get; }
        }

        private sealed class DecisionParameter
        {
            public DecisionParameter(int sequence, string name, string value)
            {
                Sequence = sequence;
                Name = name ?? string.Empty;
                Value = value ?? string.Empty;
            }

            public int Sequence { get; }
            public string Name { get; }
            public string Value { get; }
        }

        private enum DecisionExecutionState
        {
            NotConfigured,
            Success,
            Failure
        }
    }
}

