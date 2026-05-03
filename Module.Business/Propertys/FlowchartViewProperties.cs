using ControlLibrary;
using ControlLibrary.Controls.FlowchartEditor.Models;
using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Media;

namespace Module.Business.ViewModels;

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

    private string _executionStatusText = "状态：等待操作";
    private Brush _executionStatusBrush = NeutralBrush;
    private bool _isExecuting;
    private bool _isPaused;

    #endregion

    #region 集合属性

    public ObservableCollection<FlowchartNodeTemplate> NodeTemplates { get; } = new();

    public ObservableCollection<string> ExecutionLogs { get; } = new();

    #endregion

    #region 页面状态属性

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

    public ICommand SaveFlowchartCommand { get; private set; } = null!;

    public ICommand OpenFlowchartCommand { get; private set; } = null!;

    public ICommand ExecuteFlowchartCommand { get; private set; } = null!;

    public ICommand PauseFlowchartCommand { get; private set; } = null!;

    public ICommand StopFlowchartCommand { get; private set; } = null!;

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
