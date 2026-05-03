using ControlLibrary;
using ControlLibrary.Controls.FlowchartEditor.Models;
using Module.Business.Models;
using Module.Business.Services;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace Module.Business.ViewModels;

/// <summary>
/// 流程图页面的属性集中声明。
/// </summary>
public sealed partial class FlowchartViewModel
{
    #region 状态颜色字段

    private static readonly Brush SuccessBrush =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16A34A"));

    private static readonly Brush WarningBrush =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EA580C"));

    private static readonly Brush NeutralBrush =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));

    private static readonly Brush StartBrush =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2563EB"));

    private static readonly Brush ProcessBrush =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0F766E"));

    private static readonly Brush DecisionBrush =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A16207"));

    private static readonly Brush EndBrush =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DC2626"));

    #endregion

    #region 私有状态字段

    private FlowchartConfigurationCatalog _catalog = FlowchartConfigurationStore.LoadCatalog();
    private FlowchartProfile? _selectedFlowchart;
    private string _searchText = string.Empty;
    private string _pageStatusText = "等待编辑";
    private Brush _pageStatusBrush = NeutralBrush;
    private string _executionStatusText = "状态：等待操作";
    private Brush _executionStatusBrush = NeutralBrush;
    private bool _isExecuting;
    private bool _isPaused;
    private DateTime _lastCreateOrCopyCommandAt = DateTime.MinValue;

    #endregion

    #region 集合属性

    public ObservableCollection<FlowchartProfile> Flowcharts => _catalog.Flowcharts;

    public ICollectionView FlowchartsView { get; private set; } = null!;

    public ObservableCollection<FlowchartNodeTemplate> NodeTemplates { get; } = new();

    public ObservableCollection<string> ExecutionLogs { get; } = new();

    #endregion

    #region 搜索与当前编辑属性

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetField(ref _searchText, value ?? string.Empty))
            {
                return;
            }

            FlowchartsView.Refresh();
        }
    }

    public FlowchartProfile? SelectedFlowchart
    {
        get => _selectedFlowchart;
        set
        {
            if (ReferenceEquals(_selectedFlowchart, value))
            {
                return;
            }

            if (_selectedFlowchart is not null)
            {
                _selectedFlowchart.PropertyChanged -= SelectedFlowchart_PropertyChanged;
            }

            _selectedFlowchart = value;

            if (_selectedFlowchart is not null)
            {
                _selectedFlowchart.PropertyChanged += SelectedFlowchart_PropertyChanged;
            }

            ExecutionLogs.Clear();
            SetExecutionStatus("状态：等待操作", NeutralBrush);
            OnPropertyChanged();
            RaisePageSummaryChanged();
            RaiseCommandStatesChanged();
        }
    }

    #endregion

    #region 页面状态属性

    public string PageStatusText
    {
        get => _pageStatusText;
        private set => SetField(ref _pageStatusText, value);
    }

    public Brush PageStatusBrush
    {
        get => _pageStatusBrush;
        private set => SetField(ref _pageStatusBrush, value);
    }

    public string FlowchartCountText => $"{Flowcharts.Count} 个流程图";

    public string CurrentFlowchartSummary => SelectedFlowchart?.Summary ?? "未选择流程图";

    #endregion

    #region 执行状态属性

    public string ExecutionStatusText
    {
        get => _executionStatusText;
        private set => SetField(ref _executionStatusText, value);
    }

    public Brush ExecutionStatusBrush
    {
        get => _executionStatusBrush;
        private set => SetField(ref _executionStatusBrush, value);
    }

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

    public bool CanEdit => !IsExecuting;

    #endregion

    #region 命令属性

    public ICommand NewFlowchartCommand { get; private set; } = null!;

    public ICommand DuplicateFlowchartCommand { get; private set; } = null!;

    public ICommand DeleteFlowchartCommand { get; private set; } = null!;

    public ICommand SaveFlowchartCommand { get; private set; } = null!;

    public ICommand OpenFlowchartCommand { get; private set; } = null!;

    public ICommand ExportFlowchartCommand { get; private set; } = null!;

    public ICommand ExecuteFlowchartCommand { get; private set; } = null!;

    public ICommand PauseFlowchartCommand { get; private set; } = null!;

    public ICommand StopFlowchartCommand { get; private set; } = null!;

    #endregion

    #region 属性联动方法

    private void SelectedFlowchart_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FlowchartProfile.Name)
            or nameof(FlowchartProfile.NodeCount)
            or nameof(FlowchartProfile.ConnectionCount)
            or nameof(FlowchartProfile.Summary)
            or nameof(FlowchartProfile.Document))
        {
            RaisePageSummaryChanged();
            FlowchartsView.Refresh();
        }
    }

    private void RaisePageSummaryChanged()
    {
        OnPropertyChanged(nameof(FlowchartCountText));
        OnPropertyChanged(nameof(CurrentFlowchartSummary));
    }

    #endregion
}

/// <summary>
/// 流程图节点模板模型。
/// </summary>
public sealed class FlowchartNodeTemplate : ViewModelProperties
{
    #region 构造方法

    public FlowchartNodeTemplate(string displayName, string nodeText, FlowchartNodeKind nodeKind, Brush accentBrush)
    {
        DisplayName = displayName;
        NodeText = nodeText;
        NodeKind = nodeKind;
        AccentBrush = accentBrush;
    }

    #endregion

    #region 绑定属性

    public string DisplayName { get; }

    public string NodeText { get; }

    public FlowchartNodeKind NodeKind { get; }

    public Brush AccentBrush { get; }

    #endregion
}
