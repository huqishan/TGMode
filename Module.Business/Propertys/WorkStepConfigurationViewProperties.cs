using Module.Business.Models;
using Module.Business.Services;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System;
using System.Windows.Input;
using System.Windows.Data;
using System.Windows.Media;

namespace Module.Business.ViewModels;

/// <summary>
/// 工步配置界面的属性集中声明。
/// </summary>
public sealed partial class WorkStepConfigurationViewModel
{
    #region 状态颜色字段

    private static readonly Brush SuccessBrush =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16A34A"));

    private static readonly Brush WarningBrush =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EA580C"));

    private static readonly Brush NeutralBrush =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));

    #endregion

    #region 私有状态字段

    private BusinessConfigurationCatalog _catalog = BusinessConfigurationStore.LoadCatalog();
    private WorkStepProfile? _selectedWorkStep;
    private WorkStepOperation? _selectedOperation;
    private WorkStepOperation? _drawerOperation;
    private WorkStepOperationParameter? _selectedEditingInvokeParameter;
    private string _selectedProductName = string.Empty;
    private string _searchText = string.Empty;
    private string _pageStatusText = "等待编辑";
    private string _editingOperationObject = string.Empty;
    private string _editingProtocolName = string.Empty;
    private string _editingCommandName = string.Empty;
    private string _editingInvokeMethod = string.Empty;
    private string _editingInvokeMethodRemark = string.Empty;
    private string _editingReturnValue = string.Empty;
    private bool _editingShowDataToView;
    private string _editingViewDataName = string.Empty;
    private string _editingViewJudgeType = string.Empty;
    private string _editingViewJudgeCondition = string.Empty;
    private string _editingLuaScript = string.Empty;
    private string _editingDelayMillisecondsText = "0";
    private string _editingRemark = string.Empty;
    private Brush _pageStatusBrush = NeutralBrush;
    private DateTime _lastCreateOrCopyCommandAt = DateTime.MinValue;
    private bool _isOperationDrawerOpen;
    private bool _isNewOperationInDrawer;
    private bool _isSortingInvokeParameters;
    private bool _isInitializingOperationDrawer;
    private bool _isSyncingSystemInvokeMethodSelection;
    private readonly HashSet<WorkStepOperationParameter> _trackedEditingInvokeParameters = new();
    private readonly List<WorkStepOperation> _copiedOperations = new();

    #endregion

    #region 集合属性

    public ObservableCollection<WorkStepProfile> WorkSteps => _catalog.WorkSteps;

    public ICollectionView WorkStepsView { get; private set; } = null!;

    public ObservableCollection<string> ProductOptions { get; } = new();

    public ObservableCollection<string> OperationObjectOptions { get; } = new();

    public ObservableCollection<string> ProtocolOptions { get; } = new();

    public ObservableCollection<string> CommandOptions { get; } = new();

    public ObservableCollection<string> InvokeMethodOptions { get; } = new();

    public ObservableCollection<string> InvokeMethodRemarkOptions { get; } = new();

    public ObservableCollection<string> ParameterTypeOptions { get; } = new()
    {
        "设置值",
        "工步值",
        "返回值",
        "全局值",
        "系统值",
        "产品值"
    };

    public ObservableCollection<WorkStepOperationParameter> EditingInvokeParameters { get; } = new();

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

            WorkStepsView.Refresh();
            SelectFirstVisibleWorkStep();
            RefreshParameterValueOptions();
        }
    }

    public string SelectedProductName
    {
        get => _selectedProductName;
        set
        {
            if (!SetField(ref _selectedProductName, value ?? string.Empty))
            {
                return;
            }

            WorkStepsView.Refresh();
            SelectFirstVisibleWorkStep();
        }
    }

    public WorkStepProfile? SelectedWorkStep
    {
        get => _selectedWorkStep;
        set
        {
            if (ReferenceEquals(_selectedWorkStep, value))
            {
                return;
            }

            if (_selectedWorkStep is not null)
            {
                _selectedWorkStep.PropertyChanged -= SelectedWorkStep_PropertyChanged;
            }

            _selectedWorkStep = value;

            if (_selectedWorkStep is not null)
            {
                _selectedWorkStep.PropertyChanged += SelectedWorkStep_PropertyChanged;
            }

            SelectedOperation = _selectedWorkStep?.Steps.FirstOrDefault();
            OnPropertyChanged();
            OnPropertyChanged(nameof(OperationCountText));
            OnPropertyChanged(nameof(AreAllOperationsChecked));
            RefreshParameterValueOptions();
            RaiseCommandStatesChanged();
        }
    }

    public WorkStepOperation? SelectedOperation
    {
        get => _selectedOperation;
        set
        {
            if (SetField(ref _selectedOperation, value))
            {
                RaiseCommandStatesChanged();
            }
        }
    }

    public bool IsOperationDrawerOpen
    {
        get => _isOperationDrawerOpen;
        private set
        {
            if (SetField(ref _isOperationDrawerOpen, value))
            {
                OnPropertyChanged(nameof(OperationDrawerTitle));
                RaiseCommandStatesChanged();
            }
        }
    }

    public string OperationDrawerTitle => _isNewOperationInDrawer ? "新建步骤" : "编辑步骤";

    public string EditingOperationObject
    {
        get => _editingOperationObject;
        set
        {
            if (SetField(ref _editingOperationObject, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(IsSystemOperationSelected));
                OnPropertyChanged(nameof(IsLuaOperationSelected));
                OnPropertyChanged(nameof(IsProtocolCommandSelectionVisible));
                OnPropertyChanged(nameof(IsInvokeParameterEditorVisible));
                OnPropertyChanged(nameof(IsReturnValueVisible));
                RefreshProtocolOptions(updateStatus: false);
                RefreshInvokeMethodOptions(updateStatus: false);
                RaiseCommandStatesChanged();
            }
        }
    }

    public bool IsSystemOperationSelected => IsSystemOperationObject(EditingOperationObject);

    public bool IsLuaOperationSelected => IsLuaOperationObject(EditingOperationObject);

    public bool IsProtocolCommandSelectionVisible => !IsSystemOperationSelected && !IsLuaOperationSelected;

    public bool IsInvokeParameterEditorVisible => !IsLuaOperationSelected;

    public bool IsReturnValueVisible => !IsLuaOperationSelected;

    public string EditingProtocolName
    {
        get => _editingProtocolName;
        set
        {
            if (SetField(ref _editingProtocolName, value ?? string.Empty))
            {
                RefreshCommandOptions(updateStatus: false);
            }
        }
    }

    public string EditingCommandName
    {
        get => _editingCommandName;
        set
        {
            if (SetField(ref _editingCommandName, value ?? string.Empty) &&
                !IsSystemOperationSelected &&
                !IsLuaOperationSelected)
            {
                EditingInvokeMethod = _editingCommandName;
                RefreshInvokeParametersFromSelectedCommand();
            }
        }
    }

    public string EditingInvokeMethod
    {
        get => _editingInvokeMethod;
        set
        {
            if (!SetField(ref _editingInvokeMethod, value ?? string.Empty))
            {
                return;
            }

            if (IsSystemOperationSelected &&
                !_isInitializingOperationDrawer &&
                !_isSyncingSystemInvokeMethodSelection)
            {
                SyncSystemInvokeMethodRemarkFromMethod();
                RefreshInvokeParametersFromSelectedSystemMethod(clearWhenNoMetadata: true);
            }
        }
    }

    public string EditingInvokeMethodRemark
    {
        get => _editingInvokeMethodRemark;
        set
        {
            if (!SetField(ref _editingInvokeMethodRemark, value ?? string.Empty))
            {
                return;
            }

            if (IsSystemOperationSelected &&
                !_isInitializingOperationDrawer &&
                !_isSyncingSystemInvokeMethodSelection)
            {
                SyncSystemInvokeMethodFromRemark();
                RefreshInvokeParametersFromSelectedSystemMethod(clearWhenNoMetadata: true);
            }
        }
    }

    public string EditingReturnValue
    {
        get => _editingReturnValue;
        set
        {
            if (SetField(ref _editingReturnValue, value ?? string.Empty))
            {
                RefreshParameterValueOptions();
            }
        }
    }

    public bool EditingShowDataToView
    {
        get => _editingShowDataToView;
        set => SetField(ref _editingShowDataToView, value);
    }

    public string EditingViewDataName
    {
        get => _editingViewDataName;
        set => SetField(ref _editingViewDataName, value ?? string.Empty);
    }

    public string EditingViewJudgeType
    {
        get => _editingViewJudgeType;
        set => SetField(ref _editingViewJudgeType, value ?? string.Empty);
    }

    public string EditingViewJudgeCondition
    {
        get => _editingViewJudgeCondition;
        set => SetField(ref _editingViewJudgeCondition, value ?? string.Empty);
    }

    public string EditingLuaScript
    {
        get => _editingLuaScript;
        set => SetField(ref _editingLuaScript, value ?? string.Empty);
    }

    public string EditingDelayMillisecondsText
    {
        get => _editingDelayMillisecondsText;
        set => SetField(ref _editingDelayMillisecondsText, value ?? string.Empty);
    }

    public string EditingRemark
    {
        get => _editingRemark;
        set => SetField(ref _editingRemark, value ?? string.Empty);
    }

    public WorkStepOperationParameter? SelectedEditingInvokeParameter
    {
        get => _selectedEditingInvokeParameter;
        set
        {
            if (SetField(ref _selectedEditingInvokeParameter, value))
            {
                RaiseCommandStatesChanged();
            }
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

    public string WorkStepCountText => $"{WorkSteps.Count} 个工步";

    public string OperationCountText => SelectedWorkStep is null
        ? "未选择工步"
        : $"{SelectedWorkStep.OperationCount} 个步骤";

    #endregion

    #region 命令属性

    public bool AreAllOperationsChecked
    {
        get => SelectedWorkStep is not null &&
               SelectedWorkStep.Steps.Count > 0 &&
               SelectedWorkStep.Steps.All(operation => operation.IsChecked);
        set
        {
            if (SelectedWorkStep is null)
            {
                return;
            }

            foreach (WorkStepOperation operation in SelectedWorkStep.Steps
                         .Where(operation => operation.IsChecked != value)
                         .ToList())
            {
                operation.IsChecked = value;
            }

            OnPropertyChanged();
            RaiseCommandStatesChanged();
        }
    }

    public ICommand NewWorkStepCommand { get; private set; } = null!;

    public ICommand DuplicateWorkStepCommand { get; private set; } = null!;

    public ICommand DeleteWorkStepCommand { get; private set; } = null!;

    public ICommand SaveWorkStepsCommand { get; private set; } = null!;

    public ICommand RefreshProductsCommand { get; private set; } = null!;

    public ICommand AddOperationCommand { get; private set; } = null!;

    public ICommand CopyOperationCommand { get; private set; } = null!;

    public ICommand PasteOperationCommand { get; private set; } = null!;

    public ICommand DeleteOperationCommand { get; private set; } = null!;

    public ICommand SaveOperationDrawerCommand { get; private set; } = null!;

    public ICommand CloseOperationDrawerCommand { get; private set; } = null!;

    public ICommand RefreshOperationObjectsCommand { get; private set; } = null!;

    public ICommand AddInvokeParameterCommand { get; private set; } = null!;

    public ICommand DeleteInvokeParameterCommand { get; private set; } = null!;

    #endregion

    #region 属性联动方法

    private void SelectedWorkStep_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WorkStepProfile.OperationCount)
            or nameof(WorkStepProfile.OperationSummary)
            or nameof(WorkStepProfile.ProductName)
            or nameof(WorkStepProfile.StepName))
        {
            SelectedWorkStep?.MarkModified();
        }

        if (e.PropertyName is nameof(WorkStepProfile.OperationCount)
            or nameof(WorkStepProfile.OperationSummary)
            or nameof(WorkStepProfile.ProductName)
            or nameof(WorkStepProfile.StepName)
            or nameof(WorkStepProfile.LastModifiedAt)
            or nameof(WorkStepProfile.LastModifiedText)
            or nameof(WorkStepProfile.Steps))
        {
            OnPropertyChanged(nameof(OperationCountText));
            OnPropertyChanged(nameof(WorkStepCountText));
            OnPropertyChanged(nameof(AreAllOperationsChecked));
            WorkStepsView.Refresh();
            RaiseCommandStatesChanged();
        }
    }

    private void RaisePageSummaryChanged()
    {
        OnPropertyChanged(nameof(WorkStepCountText));
        OnPropertyChanged(nameof(OperationCountText));
    }

    #endregion
}
