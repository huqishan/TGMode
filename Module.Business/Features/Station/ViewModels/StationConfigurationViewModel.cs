using ControlLibrary;
using ControlLibrary.ControlViews.Flowchart;
using ControlLibrary.Controls.FlowchartEditor.Control;
using ControlLibrary.Controls.FlowchartEditor.Models;
using Microsoft.Win32;
using Module.Business.Features.Scheme.ViewModels.PresentationModels;
using Module.Business.Features.Scheme.ViewModels;
using Module.Business.Features.OperationEditing.ViewModels.PresentationModels;
using Module.Business.Features.OperationEditing.ViewModels;
using Module.Business.Models;
using Module.Business.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace Module.Business.Features.StationConfiguration;

/// <summary>
/// 工位配置页面视图模型，负责工位列表、流程图导入导出和执行控制状态。
/// </summary>
public sealed class StationConfigurationViewModel : ViewModelProperties
{
    #region 样式字段

    /// <summary>
    /// 成功状态画刷。
    /// </summary>
    private static readonly Brush SuccessBrush =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16A34A"));

    /// <summary>
    /// 警告状态画刷。
    /// </summary>
    private static readonly Brush WarningBrush =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EA580C"));

    /// <summary>
    /// 中性状态画刷。
    /// </summary>
    private static readonly Brush NeutralBrush =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));

    /// <summary>
    /// 开始节点画刷。
    /// </summary>
    private static readonly Brush StartBrush =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2563EB"));

    /// <summary>
    /// 处理节点画刷。
    /// </summary>
    private static readonly Brush ProcessBrush =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0F766E"));

    /// <summary>
    /// 判断节点画刷。
    /// </summary>
    private static readonly Brush DecisionBrush =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A16207"));

    /// <summary>
    /// 结束节点画刷。
    /// </summary>
    private static readonly Brush EndBrush =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DC2626"));

    #endregion

    #region 数据与状态字段
    /// <summary>
    /// 工位配置页面执行动画中每个节点的高亮保留时间，单位为毫秒。
    /// </summary>
    private const int FlowchartNodeHighlightMilliseconds = 1000;

    /// <summary>
    /// 工位配置页面执行流程图时允许经过的最大节点数，用于阻止错误连线造成无限执行。
    /// </summary>
    private const int FlowchartMaximumExecutionSteps = 100;

    /// <summary>
    /// 当前工位配置目录。
    /// </summary>
    private readonly StationConfigurationCatalog _catalog = StationConfigurationStore.LoadCatalog();

    /// <summary>
    /// 当前选中的工位。
    /// </summary>
    private StationProfile? _selectedStation;

    /// <summary>
    /// 工位搜索关键字。
    /// </summary>
    private string _searchText = string.Empty;

    /// <summary>
    /// 页面顶部状态文本。
    /// </summary>
    private string _pageStatusText = "等待编辑";

    /// <summary>
    /// 页面顶部状态画刷。
    /// </summary>
    private Brush _pageStatusBrush = NeutralBrush;

    /// <summary>
    /// 是否正在执行流程图。
    /// </summary>
    private bool _isExecuting;

    /// <summary>
    /// 当前流程图是否已暂停。
    /// </summary>
    private bool _isPaused;

    /// <summary>
    /// 当前实际运行流程图所属的工位名称，执行期间不随界面选中项变化。
    /// </summary>
    private string _executingStationName = string.Empty;

    /// <summary>
    /// 当前流程图节点操作编辑器。
    /// </summary>
    private OperationEditorViewModel? _nodeOperationEditor;

    private WorkStepOperation? _savedNodeOperation;

    /// <summary>
    /// 当前正在编辑的流程图节点编号。
    /// </summary>
    private Guid? _editingNodeId;

    /// <summary>
    /// 当前正在编辑的流程图节点类型，用于在同一个弹框内切换处理块与判断块界面。
    /// </summary>
    private FlowchartNodeKind? _editingNodeKind;

    /// <summary>
    /// 上一次新建或复制命令触发时间，用于防止连点。
    /// </summary>
    private DateTime _lastCreateOrCopyCommandAt = DateTime.MinValue;

    #endregion

    #region 构造与初始化
    /// <summary>
    /// 初始化工位配置页面数据、集合视图和命令。
    /// </summary>
    public StationConfigurationViewModel()
    {
        Stations.CollectionChanged += Stations_CollectionChanged;
        HookStations(Stations);

        StationsView = CollectionViewSource.GetDefaultView(Stations);
        StationsView.Filter = FilterStations;

        InitializeNodeTemplates();

        SelectedStation = Stations.FirstOrDefault();
        SetPageStatus(
            Stations.Count == 0 ? "暂无工位配置，请点击新建。" : $"已加载 {Stations.Count} 个工位。",
            NeutralBrush);

        NewStationCommand = new RelayCommand(_ => NewStation());
        DuplicateStationCommand = new RelayCommand(_ => DuplicateSelectedStation(), _ => SelectedStation is not null);
        DeleteStationCommand = new RelayCommand(_ => DeleteSelectedStation(), _ => SelectedStation is not null);
        SaveStationCommand = new RelayCommand(_ => SaveStations());
        OpenFlowchartCommand = new RelayCommand(ImportFlowchart, _ => SelectedStation is not null);
        ExportFlowchartCommand = new RelayCommand(ExportFlowchart, _ => SelectedStation is not null);
        ExecuteFlowchartCommand = new RelayCommand(
            async parameter => await ExecuteFlowchartAsync(parameter),
            _ => SelectedStation is not null && !IsExecuting);
        PauseFlowchartCommand = new RelayCommand(
            TogglePauseFlowchart,
            _ => SelectedStation is not null && IsExecuting);
        StopFlowchartCommand = new RelayCommand(
            StopFlowchart,
            _ => SelectedStation is not null && IsExecuting);
    }

    #endregion

    #region 绑定属性
    /// <summary>
    /// 工位配置集合。
    /// </summary>
    public ObservableCollection<StationProfile> Stations => _catalog.Stations;

    /// <summary>
    /// 支持搜索过滤的工位集合视图。
    /// </summary>
    public ICollectionView StationsView { get; }

    /// <summary>
    /// 流程图节点模板集合。
    /// </summary>
    public ObservableCollection<FlowchartNodeTemplate> NodeTemplates { get; } = new();

    /// <summary>
    /// 工位搜索关键字。
    /// </summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetField(ref _searchText, value ?? string.Empty))
            {
                return;
            }

            StationsView.Refresh();
        }
    }

    /// <summary>
    /// 当前选中的工位配置。
    /// </summary>
    public StationProfile? SelectedStation
    {
        get => _selectedStation;
        set
        {
            if (ReferenceEquals(_selectedStation, value))
            {
                return;
            }

            _selectedStation = value;
            SetExecutionStatus("状态：等待操作", NeutralBrush);
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedStation));
            RaiseCommandStatesChanged();
        }
    }

    /// <summary>
    /// 页面状态提示文本。
    /// </summary>
    public string PageStatusText
    {
        get => _pageStatusText;
        private set => SetField(ref _pageStatusText, value);
    }

    /// <summary>
    /// 页面状态提示画刷。
    /// </summary>
    public Brush PageStatusBrush
    {
        get => _pageStatusBrush;
        private set => SetField(ref _pageStatusBrush, value);
    }

    /// <summary>
    /// 是否正在执行流程图。
    /// </summary>
    public bool IsExecuting
    {
        get => _isExecuting;
        private set
        {
            if (SetField(ref _isExecuting, value))
            {
                OnPropertyChanged(nameof(CanEdit));
                RaiseCommandStatesChanged();
            }
        }
    }

    /// <summary>
    /// 流程图执行是否暂停。
    /// </summary>
    public bool IsPaused
    {
        get => _isPaused;
        private set
        {
            if (SetField(ref _isPaused, value))
            {
                RaiseCommandStatesChanged();
            }
        }
    }

    /// <summary>
    /// 当前是否允许编辑工位配置。
    /// </summary>
    /// <summary>
    /// 当前流程图节点操作编辑器。
    /// </summary>
    public OperationEditorViewModel? NodeOperationEditor
    {
        get => _nodeOperationEditor;
        private set
        {
            if (SetField(ref _nodeOperationEditor, value))
            {
                OnPropertyChanged(nameof(IsNodeOperationEditorOpen));
            }
        }
    }

    /// <summary>
    /// 是否正在编辑流程图节点操作。
    /// </summary>
    public bool IsNodeOperationEditorOpen => NodeOperationEditor is not null;

    /// <summary>
    /// 当前弹框是否正在编辑判断块。
    /// </summary>
    public bool IsEditingDecisionNode => _editingNodeKind == FlowchartNodeKind.Decision;

    /// <summary>
    /// 当前节点编辑弹框标题。
    /// </summary>
    public string NodeOperationEditorTitle => IsEditingDecisionNode ? "编辑判断块" : "编辑步骤";

    public bool CanEdit => !IsExecuting;

    /// <summary>
    /// 工位数量显示文本。
    /// </summary>
    public string StationCountText => $"{Stations.Count} 个工位";

    /// <summary>
    /// 是否已选择工位。
    /// </summary>
    public bool HasSelectedStation => SelectedStation is not null;

    #endregion

    #region 页面命令
    /// <summary>
    /// 新建工位命令。
    /// </summary>
    public ICommand NewStationCommand { get; }

    /// <summary>
    /// 复制当前工位命令。
    /// </summary>
    public ICommand DuplicateStationCommand { get; }

    /// <summary>
    /// 删除当前工位命令。
    /// </summary>
    public ICommand DeleteStationCommand { get; }

    /// <summary>
    /// 保存工位配置命令。
    /// </summary>
    public ICommand SaveStationCommand { get; }

    /// <summary>
    /// 导入流程图命令。
    /// </summary>
    public ICommand OpenFlowchartCommand { get; }

    /// <summary>
    /// 导出流程图命令。
    /// </summary>
    public ICommand ExportFlowchartCommand { get; }

    /// <summary>
    /// 执行当前工位流程图命令。
    /// </summary>
    public ICommand ExecuteFlowchartCommand { get; }

    /// <summary>
    /// 暂停或继续当前流程图命令。
    /// </summary>
    public ICommand PauseFlowchartCommand { get; }

    /// <summary>
    /// 停止当前流程图命令。
    /// </summary>
    public ICommand StopFlowchartCommand { get; }

    #endregion

    #region 节点模板

    /// <summary>
    /// 初始化流程图节点模板。
    /// </summary>
    private void InitializeNodeTemplates()
    {
        NodeTemplates.Add(new FlowchartNodeTemplate("开始", "开始", FlowchartNodeKind.Start, StartBrush));
        NodeTemplates.Add(new FlowchartNodeTemplate("处理", "处理", FlowchartNodeKind.Process, ProcessBrush));
        NodeTemplates.Add(new FlowchartNodeTemplate("判断", "判断", FlowchartNodeKind.Decision, DecisionBrush));
        NodeTemplates.Add(new FlowchartNodeTemplate("结束", "结束", FlowchartNodeKind.End, EndBrush));
    }

    #endregion

    #region 工位命令处理

    /// <summary>
    /// 新建一个工位配置。
    /// </summary>
    private void NewStation()
    {
        if (!CanRunCreateOrCopyCommand())
        {
            return;
        }

        StationProfile station = new()
        {
            StationName = GenerateUniqueStationName(),
            StationCode = GenerateUniqueStationCode(),
            LastModifiedAt = DateTime.Now
        };

        Stations.Add(station);
        SelectCreatedStation(station);
        SetPageStatus("已新增工位，请继续编辑后保存。", SuccessBrush);
    }

    /// <summary>
    /// 复制当前选中的工位配置。
    /// </summary>
    private void DuplicateSelectedStation()
    {
        if (!CanRunCreateOrCopyCommand() || SelectedStation is null)
        {
            return;
        }

        StationProfile station = SelectedStation.CopyAsNew(
            GenerateCopyStationName(SelectedStation.StationName),
            GenerateUniqueStationCode(SelectedStation.StationCode));
        station.LastModifiedAt = DateTime.Now;

        Stations.Add(station);
        SelectCreatedStation(station);
        SetPageStatus($"已复制工位：{station.StationName}", SuccessBrush);
    }

    /// <summary>
    /// 删除当前选中的工位配置。
    /// </summary>
    private void DeleteSelectedStation()
    {
        if (SelectedStation is null)
        {
            return;
        }

        int index = Stations.IndexOf(SelectedStation);
        Stations.Remove(SelectedStation);
        SelectedStation = Stations.Count == 0
            ? null
            : Stations[Math.Clamp(index, 0, Stations.Count - 1)];

        SetPageStatus("已删除工位，点击保存后生效。", WarningBrush);
    }

    /// <summary>
    /// 校验并保存工位配置。
    /// </summary>
    private void SaveStations()
    {
        if (!ValidateStations(out string message))
        {
            SetPageStatus(message, WarningBrush);
            return;
        }

        StationConfigurationStore.SaveCatalog(_catalog);
        StationsView.Refresh();
        SetPageStatus($"已保存 {Stations.Count} 个工位。", SuccessBrush);
    }

    #endregion

    #region 流程图导入导出
    /// <summary>
    /// 从文件导入流程图到当前工位。
    /// </summary>
    /// <param name="parameter">流程图编辑器控件。</param>
    private void ImportFlowchart(object? parameter)
    {
        if (parameter is not FlowchartEditorControl editor || SelectedStation is null)
        {
            SetPageStatus("请先选择工位。", WarningBrush);
            return;
        }

        OpenFileDialog dialog = new()
        {
            Filter = "流程图文件 (*.flowchart.json)|*.flowchart.json|JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*",
            DefaultExt = ".flowchart.json"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            editor.LoadFromFile(dialog.FileName);
            SelectedStation.FlowchartDocument = editor.CreateDocumentSnapshot();
            SelectedStation.LastModifiedAt = DateTime.Now;
            SetPageStatus($"已导入流程图：{dialog.FileName}", SuccessBrush);
        }
        catch (Exception ex)
        {
            SetPageStatus($"导入流程图失败：{ex.Message}", WarningBrush);
        }
    }

    /// <summary>
    /// 将当前工位流程图导出到文件。
    /// </summary>
    /// <param name="parameter">流程图编辑器控件。</param>
    private void ExportFlowchart(object? parameter)
    {
        if (parameter is not FlowchartEditorControl editor || SelectedStation is null)
        {
            SetPageStatus("请先选择工位。", WarningBrush);
            return;
        }

        CaptureCurrentEditorDocument(editor);

        SaveFileDialog dialog = new()
        {
            Filter = "流程图文件 (*.flowchart.json)|*.flowchart.json|JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*",
            DefaultExt = ".flowchart.json",
            FileName = $"{SanitizeFileName(SelectedStation.StationName)}.flowchart.json"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            editor.SaveToFile(dialog.FileName);
            SetPageStatus($"已导出流程图：{dialog.FileName}", SuccessBrush);
        }
        catch (Exception ex)
        {
            SetPageStatus($"导出流程图失败：{ex.Message}", WarningBrush);
        }
    }

    #endregion

    #region 流程图执行控制
    #region 流程图节点操作编辑

    /// <summary>
    /// 打开流程图节点操作编辑器。
    /// </summary>
    public bool OpenNodeOperationEditor(FlowchartNodeInteractionEventArgs e, FlowchartDocument document)
    {
        if (!CanEditNode(e.NodeKind) || CanEdit != true || SelectedStation is null)
        {
            return false;
        }

        WorkStepOperation operation = DeserializeNodeOperation(e);
        _editingNodeId = e.NodeId;
        _editingNodeKind = e.NodeKind;

        // 判断块始终需要计算条件，界面不再提供关闭条件的入口，也不使用隐藏的 NG 步骤跳转。
        if (e.NodeKind == FlowchartNodeKind.Decision)
        {
            operation.OperationObjectName = "判断";
            operation.ConditionExecution.IsEnabled = true;
            operation.ConditionExecution.IsNgJumpEnabled = false;
            operation.ConditionExecution.NgJumpStepId = string.Empty;
        }

        OperationEditorViewModel operationEditor = new();
        operationEditor.OperationSaved += NodeOperationEditor_OperationSaved;
        operationEditor.SetExternalReturnValueOptions(GetFlowchartReturnValueOptions(document, operationEditor));
        WorkStepOperation editingOperation = operation.Clone();
        _savedNodeOperation = null;
        operationEditor.Open(editingOperation, isNewOperation: false, new[] { editingOperation });

        NodeOperationEditor = operationEditor;
        OnPropertyChanged(nameof(IsEditingDecisionNode));
        OnPropertyChanged(nameof(NodeOperationEditorTitle));
        return true;
    }

    /// <summary>
    /// 保存流程图节点操作编辑结果。
    /// </summary>
    public bool TrySaveNodeOperationEdit(FlowchartNodePanelView editor)
    {
        if (NodeOperationEditor is null || _editingNodeId is null || SelectedStation is null)
        {
            return false;
        }

        NodeOperationEditor.Save();
        WorkStepOperation? operation = _savedNodeOperation;
        if (operation is null)
        {
            return false;
        }

        FlowchartDocument document = editor.CreateDocumentSnapshot();
        FlowchartNodeDocument? node = document.Nodes.FirstOrDefault(item => item.Id == _editingNodeId.Value);
        if (node is null)
        {
            CloseNodeOperationEditor();
            return false;
        }

        node.MetadataJson = JsonSerializer.Serialize(operation);
        node.Text = BuildNodeText(node.Kind, operation);

        SelectedStation.FlowchartDocument = document;
        editor.LoadDocumentSnapshot(document);

        CloseNodeOperationEditor();
        return true;
    }

    /// <summary>
    /// 取消流程图节点操作编辑。
    /// </summary>
    public void CancelNodeOperationEdit()
    {
        NodeOperationEditor?.Close();
        CloseNodeOperationEditor();
    }

    private void CloseNodeOperationEditor()
    {
        if (NodeOperationEditor is not null)
        {
            NodeOperationEditor.OperationSaved -= NodeOperationEditor_OperationSaved;
            NodeOperationEditor.Close();
        }

        _editingNodeId = null;
        _editingNodeKind = null;
        _savedNodeOperation = null;
        NodeOperationEditor = null;
        OnPropertyChanged(nameof(IsEditingDecisionNode));
        OnPropertyChanged(nameof(NodeOperationEditorTitle));
    }

    private void NodeOperationEditor_OperationSaved(object? sender, OperationEditorSavedEventArgs e)
    {
        _savedNodeOperation = e.Operation;
    }

    private static WorkStepOperation DeserializeNodeOperation(FlowchartNodeInteractionEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(e.MetadataJson))
        {
            if (TryDeserializeNodeOperationMetadata(e.MetadataJson, out WorkStepOperation? operation) &&
                operation is not null)
            {
                return operation;
            }
        }

        string[] lines = (e.Text ?? string.Empty)
            .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
        string firstLine = lines
            .Select(line => line?.Trim() ?? string.Empty)
            .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line))
            ?? string.Empty;
        string operationObject = ResolveOperationObject(e.NodeKind, firstLine);
        WorkStepOperation operationTemplate = SchemeConfigurationViewModel.CreateDefaultOperation();
        operationTemplate.OperationObjectName = operationObject;
        return operationTemplate;
    }

    private static bool CanEditNode(FlowchartNodeKind nodeKind)
    {
        return nodeKind is FlowchartNodeKind.Process or FlowchartNodeKind.Decision;
    }

    private static string BuildNodeText(FlowchartNodeKind nodeKind, WorkStepOperation operation)
    {
        // 处理块和判断块直接展示编辑界面中的描述，使流程图能够表达节点的业务含义。
        if (nodeKind is FlowchartNodeKind.Process or FlowchartNodeKind.Decision &&
            !string.IsNullOrWhiteSpace(operation.Summary))
        {
            return operation.Summary.Trim();
        }

        if (nodeKind == FlowchartNodeKind.Decision)
        {
            return GetDefaultNodeText(nodeKind);
        }

        string operationObject = string.IsNullOrWhiteSpace(operation.OperationObjectName)
            ? GetDefaultNodeText(nodeKind)
            : operation.OperationObjectName.Trim();

        return operationObject;
    }

    private static string ResolveOperationObject(FlowchartNodeKind nodeKind, string firstLine)
    {
        if (string.IsNullOrWhiteSpace(firstLine))
        {
            return nodeKind == FlowchartNodeKind.Process
                ? "System"
                : GetDefaultNodeText(nodeKind);
        }

        if (nodeKind == FlowchartNodeKind.Process &&
            string.Equals(firstLine, "处理", StringComparison.Ordinal))
        {
            return "System";
        }

        return firstLine.Trim();
    }

    private static string GetDefaultNodeText(FlowchartNodeKind nodeKind)
    {
        return nodeKind switch
        {
            FlowchartNodeKind.Decision => "判断",
            FlowchartNodeKind.Start => "开始",
            FlowchartNodeKind.End => "结束",
            _ => "处理"
        };
    }

    private static IEnumerable<string> GetFlowchartReturnValueOptions(
        FlowchartDocument document,
        OperationEditorViewModel operationEditorViewModel)
    {
        return document.Nodes
            .SelectMany(node => GetNodeReturnValueOptions(node, operationEditorViewModel))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> GetNodeReturnValueOptions(
        FlowchartNodeDocument node,
        OperationEditorViewModel operationEditorViewModel)
    {
        if (!TryDeserializeNodeOperationMetadata(node.MetadataJson, out WorkStepOperation? operation) ||
            operation is null)
        {
            yield break;
        }

        string returnValueName = operation.ReturnValue?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(returnValueName))
        {
            yield break;
        }

        // 候选名称必须与执行服务写入返回值字典的键完全一致，处理块和判断块才能直接引用运行结果。
        IEnumerable<ReturnValue> returnValues = operation.ReturnValues
            .Concat(operationEditorViewModel.CreateReturnParametersFromOperation(operation));
        foreach (ReturnValue returnValue in returnValues)
        {
            if (!string.IsNullOrWhiteSpace(returnValue.ReturnParameterName))
            {
                yield return $"{returnValueName}_{returnValue.ReturnParameterName.Trim()}";
            }
        }
    }

    private static bool TryDeserializeNodeOperationMetadata(string? metadataJson, out WorkStepOperation? operation)
    {
        operation = null;
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return false;
        }

        try
        {
            operation = JsonSerializer.Deserialize<WorkStepOperation>(metadataJson);
            return operation is not null;
        }
        catch
        {
            return false;
        }
    }

    #endregion

    /// <summary>
    /// 保存当前画布并通过流程图执行服务运行真实节点操作。
    /// </summary>
    /// <param name="parameter">流程图编辑器控件。</param>
    /// <returns>异步执行任务。</returns>
    private async Task ExecuteFlowchartAsync(object? parameter)
    {
        if (parameter is not FlowchartEditorControl editor || SelectedStation is null)
        {
            SetExecutionStatus("状态：请先选择工位。", WarningBrush);
            return;
        }

        CaptureCurrentEditorDocument(editor);
        if (!ValidateStations(out string validationMessage))
        {
            SetExecutionStatus($"状态：{validationMessage}", WarningBrush);
            return;
        }

        IsExecuting = true;
        IsPaused = false;
        _executingStationName = SelectedStation.StationName.Trim();
        SetExecutionStatus("状态：正在启动流程图", NeutralBrush);

        Guid? previousNodeId = null;
        EventHandler<FlowchartExecutionEventArgs> nodeExecutingHandler = (_, eventArgs) =>
        {
            if (!eventArgs.NodeId.HasValue ||
                !string.Equals(eventArgs.StationName, _executingStationName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // 执行服务可能在后台线程触发事件，画布高亮必须切回编辑器所属的 UI 线程。
            editor.Dispatcher.Invoke(() =>
            {
                editor.ShowExecutionPosition(eventArgs.NodeId.Value, previousNodeId);
                previousNodeId = eventArgs.NodeId.Value;
            });
        };
        editor.ClearExecutionPosition();
        FlowchartExecutionService.NodeExecuting += nodeExecutingHandler;

        try
        {
            // 正式执行服务从持久化配置读取流程图，启动前先保存画布快照，确保执行内容与当前界面一致。
            await Task.Run(() => StationConfigurationStore.SaveCatalog(_catalog));
            FlowchartExecutionServiceResult result = await FlowchartExecutionService.ExecuteAsync(
                _executingStationName,
                FlowchartNodeHighlightMilliseconds,
                FlowchartMaximumExecutionSteps);

            SetExecutionStatus(
                $"状态：{result.Message}",
                result.IsSuccess ? SuccessBrush : WarningBrush);
        }
        catch (Exception ex)
        {
            SetExecutionStatus($"状态：流程图执行失败：{ex.Message}", WarningBrush);
        }
        finally
        {
            FlowchartExecutionService.NodeExecuting -= nodeExecutingHandler;
            editor.ClearExecutionPosition();
            IsPaused = false;
            IsExecuting = false;
            _executingStationName = string.Empty;
        }
    }

    /// <summary>
    /// 切换当前流程图的暂停和继续状态。
    /// </summary>
    /// <param name="parameter">命令参数，当前执行不依赖编辑器控件。</param>
    private void TogglePauseFlowchart(object? parameter)
    {
        if (string.IsNullOrWhiteSpace(_executingStationName))
        {
            SetExecutionStatus("状态：没有正在执行的流程图。", WarningBrush);
            return;
        }

        FlowchartExecutionControlActionResult result = IsPaused
            ? FlowchartExecutionService.Continue(_executingStationName)
            : FlowchartExecutionService.Pause(_executingStationName);
        if (result.IsSuccess)
        {
            IsPaused = !IsPaused;
        }

        SetExecutionStatus($"状态：{result.Message}", result.IsSuccess ? NeutralBrush : WarningBrush);
    }

    /// <summary>
    /// 停止当前正在执行的流程图。
    /// </summary>
    /// <param name="parameter">命令参数，当前执行不依赖编辑器控件。</param>
    private void StopFlowchart(object? parameter)
    {
        if (string.IsNullOrWhiteSpace(_executingStationName))
        {
            SetExecutionStatus("状态：没有正在执行的流程图。", WarningBrush);
            return;
        }

        FlowchartExecutionControlActionResult result = FlowchartExecutionService.Stop(_executingStationName);
        if (result.IsSuccess)
        {
            IsPaused = false;
        }

        SetExecutionStatus($"状态：{result.Message}", result.IsSuccess ? NeutralBrush : WarningBrush);
    }

    #endregion

    #region 编辑器文档同步
    /// <summary>
    /// 捕获当前编辑器中的流程图文档到选中工位。
    /// </summary>
    /// <param name="parameter">流程图编辑器控件。</param>
    public void CaptureCurrentEditorDocument(object? parameter)
    {
        if (parameter is FlowchartEditorControl editor && SelectedStation is not null)
        {
            SelectedStation.FlowchartDocument = editor.CreateDocumentSnapshot();
        }
    }

    #endregion

    #region 集合与属性变更处理
    /// <summary>
    /// 处理工位集合增删后的事件订阅和界面刷新。
    /// </summary>
    /// <param name="sender">参数 sender。</param>
    /// <param name="e">参数 e。</param>
    private void Stations_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (StationProfile station in e.OldItems.OfType<StationProfile>())
            {
                UnhookStation(station);
            }
        }

        if (e.NewItems is not null)
        {
            foreach (StationProfile station in e.NewItems.OfType<StationProfile>())
            {
                HookStation(station);
            }
        }

        OnPropertyChanged(nameof(StationCountText));
        StationsView.Refresh();
        RaiseCommandStatesChanged();
    }

    /// <summary>
    /// 为工位集合订阅属性变更事件。
    /// </summary>
    /// <param name="stations">参数 stations。</param>
    private void HookStations(IEnumerable<StationProfile> stations)
    {
        foreach (StationProfile station in stations)
        {
            HookStation(station);
        }
    }

    /// <summary>
    /// 为单个工位订阅属性变更事件。
    /// </summary>
    /// <param name="station">参数 station。</param>
    private void HookStation(StationProfile station)
    {
        station.PropertyChanged += Station_PropertyChanged;
    }

    /// <summary>
    /// 取消单个工位的属性变更订阅。
    /// </summary>
    /// <param name="station">参数 station。</param>
    private void UnhookStation(StationProfile station)
    {
        station.PropertyChanged -= Station_PropertyChanged;
    }

    /// <summary>
    /// 处理工位属性变化并刷新状态。
    /// </summary>
    /// <param name="sender">参数 sender。</param>
    /// <param name="e">参数 e。</param>
    private void Station_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not StationProfile station)
        {
            return;
        }

        if (ShouldRefreshLastModified(e.PropertyName))
        {
            station.LastModifiedAt = DateTime.Now;
        }

        StationsView.Refresh();
        SetPageStatus("工位配置已修改，记得保存。", NeutralBrush);
    }

    /// <summary>
    /// 判断指定属性变化是否需要刷新最后修改时间。
    /// </summary>
    /// <param name="propertyName">参数 propertyName。</param>
    /// <returns>需要刷新时返回 true。</returns>
    private static bool ShouldRefreshLastModified(string? propertyName)
    {
        return propertyName is nameof(StationProfile.StationName)
            or nameof(StationProfile.StationCode)
            or nameof(StationProfile.IsEnabled)
            or nameof(StationProfile.FlowchartDocument)
            or nameof(StationProfile.Summary);
    }

    #endregion

    #region 搜索与校验
    /// <summary>
    /// 根据搜索关键字过滤工位列表。
    /// </summary>
    /// <param name="item">参数 item。</param>
    /// <returns>符合过滤条件时返回 true。</returns>
    private bool FilterStations(object item)
    {
        if (item is not StationProfile station)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        string keyword = SearchText.Trim();
        return Contains(station.StationName, keyword) ||
               Contains(station.StationCode, keyword);
    }

    /// <summary>
    /// 判断文本是否包含搜索关键字。
    /// </summary>
    /// <param name="source">参数 source。</param>
    /// <param name="keyword">参数 keyword。</param>
    /// <returns>包含关键字时返回 true。</returns>
    private static bool Contains(string? source, string keyword)
    {
        return source?.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// 校验工位名称和编码是否完整且唯一。
    /// </summary>
    /// <param name="message">参数 message。</param>
    /// <returns>校验通过时返回 true。</returns>
    private bool ValidateStations(out string message)
    {
        HashSet<string> stationNames = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> stationCodes = new(StringComparer.OrdinalIgnoreCase);

        foreach (StationProfile station in Stations)
        {
            if (string.IsNullOrWhiteSpace(station.StationName))
            {
                message = "工位名称不能为空。";
                return false;
            }

            if (string.IsNullOrWhiteSpace(station.StationCode))
            {
                message = $"工位“{station.StationName}”的工位编码不能为空。";
                return false;
            }

            if (!stationNames.Add(station.StationName.Trim()))
            {
                message = $"工位名称不能重复：{station.StationName}";
                return false;
            }

            if (!stationCodes.Add(station.StationCode.Trim()))
            {
                message = $"工位编码不能重复：{station.StationCode}";
                return false;
            }
        }

        message = string.Empty;
        return true;
    }

    #endregion

    #region 工位名称与编码工具
    /// <summary>
    /// 选择刚创建或复制出的工位。
    /// </summary>
    /// <param name="station">参数 station。</param>
    private void SelectCreatedStation(StationProfile station)
    {
        SearchText = string.Empty;
        StationsView.Refresh();
        SelectedStation = station;
        StationsView.MoveCurrentTo(station);
    }

    /// <summary>
    /// 判断是否允许执行新建或复制命令。
    /// </summary>
    /// <returns>允许执行时返回 true。</returns>
    private bool CanRunCreateOrCopyCommand()
    {
        DateTime now = DateTime.UtcNow;
        if (now - _lastCreateOrCopyCommandAt < TimeSpan.FromMilliseconds(300))
        {
            return false;
        }

        _lastCreateOrCopyCommandAt = now;
        return true;
    }

    /// <summary>
    /// 生成不重复的新工位名称。
    /// </summary>
    /// <returns>唯一工位名称。</returns>
    private string GenerateUniqueStationName()
    {
        HashSet<string> existingNames = new(Stations.Select(station => station.StationName), StringComparer.OrdinalIgnoreCase);
        for (int index = 1; ; index++)
        {
            string candidate = $"工位 {index}";
            if (!existingNames.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>
    /// 基于原名称生成复制工位名称。
    /// </summary>
    /// <param name="baseName">参数 baseName。</param>
    /// <returns>唯一复制工位名称。</returns>
    private string GenerateCopyStationName(string baseName)
    {
        HashSet<string> existingNames = new(Stations.Select(station => station.StationName), StringComparer.OrdinalIgnoreCase);
        string copyName = $"{baseName.Trim()} 副本";
        if (!existingNames.Contains(copyName))
        {
            return copyName;
        }

        for (int index = 2; ; index++)
        {
            string candidate = $"{copyName} {index}";
            if (!existingNames.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>
    /// 生成不重复的工位编码。
    /// </summary>
    /// <param name="baseCode">参数 baseCode。</param>
    /// <returns>唯一工位编码。</returns>
    private string GenerateUniqueStationCode(string? baseCode = null)
    {
        HashSet<string> existingCodes = new(
            Stations.Select(station => station.StationCode?.Trim() ?? string.Empty),
            StringComparer.OrdinalIgnoreCase);

        string root = string.IsNullOrWhiteSpace(baseCode) ? "ST" : baseCode.Trim().ToUpperInvariant();
        for (int index = 1; ; index++)
        {
            string candidate = string.IsNullOrWhiteSpace(baseCode)
                ? $"ST-{index:00}"
                : $"{root}-{index:00}";
            if (!existingCodes.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    #endregion

    #region 状态与命令刷新

    /// <summary>
    /// 更新页面状态提示。
    /// </summary>
    /// <param name="text">参数 text。</param>
    /// <param name="brush">参数 brush。</param>
    private void SetPageStatus(string text, Brush brush)
    {
        PageStatusText = text;
        PageStatusBrush = brush;
    }

    /// <summary>
    /// 更新流程图执行状态。
    /// </summary>
    /// <param name="text">参数 text。</param>
    /// <param name="brush">参数 brush。</param>
    private void SetExecutionStatus(string text, Brush brush)
    {
        SetPageStatus(text, brush);
    }

    /// <summary>
    /// 刷新所有命令的可执行状态。
    /// </summary>
    private void RaiseCommandStatesChanged()
    {
        RaiseCommandState(DuplicateStationCommand);
        RaiseCommandState(DeleteStationCommand);
        RaiseCommandState(OpenFlowchartCommand);
        RaiseCommandState(ExportFlowchartCommand);
        RaiseCommandState(ExecuteFlowchartCommand);
        RaiseCommandState(PauseFlowchartCommand);
        RaiseCommandState(StopFlowchartCommand);
    }

    /// <summary>
    /// 刷新单个命令的可执行状态。
    /// </summary>
    /// <param name="command">参数 command。</param>
    private static void RaiseCommandState(ICommand? command)
    {
        if (command is RelayCommand relayCommand)
        {
            relayCommand.RaiseCanExecuteChanged();
        }
    }

    #endregion

    #region 文件名工具
    /// <summary>
    /// 清理流程图导出文件名中的非法字符。
    /// </summary>
    /// <param name="fileName">参数 fileName。</param>
    /// <returns>可用于文件系统的安全文件名。</returns>
    private static string SanitizeFileName(string fileName)
    {
        string safeName = string.IsNullOrWhiteSpace(fileName) ? "flowchart" : fileName.Trim();
        foreach (char invalidChar in Path.GetInvalidFileNameChars())
        {
            safeName = safeName.Replace(invalidChar, '_');
        }

        return safeName;
    }

    #endregion
}
