using Module.Business.Models;
using Module.Business.Services;
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
    private string _selectedProductName = string.Empty;
    private string _searchText = string.Empty;
    private string _pageStatusText = "等待编辑";
    private Brush _pageStatusBrush = NeutralBrush;
    private DateTime _lastCreateOrCopyCommandAt = DateTime.MinValue;

    #endregion

    #region 集合属性

    public ObservableCollection<WorkStepProfile> WorkSteps => _catalog.WorkSteps;

    public ICollectionView WorkStepsView { get; private set; } = null!;

    public ObservableCollection<string> ProductOptions { get; } = new();

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

    public ICommand NewWorkStepCommand { get; private set; } = null!;

    public ICommand DuplicateWorkStepCommand { get; private set; } = null!;

    public ICommand DeleteWorkStepCommand { get; private set; } = null!;

    public ICommand SaveWorkStepsCommand { get; private set; } = null!;

    public ICommand RefreshProductsCommand { get; private set; } = null!;

    public ICommand AddOperationCommand { get; private set; } = null!;

    public ICommand DeleteOperationCommand { get; private set; } = null!;

    #endregion

    #region 属性联动方法

    private void SelectedWorkStep_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WorkStepProfile.OperationCount)
            or nameof(WorkStepProfile.OperationSummary)
            or nameof(WorkStepProfile.ProductName)
            or nameof(WorkStepProfile.StepName))
        {
            OnPropertyChanged(nameof(OperationCountText));
            OnPropertyChanged(nameof(WorkStepCountText));
            WorkStepsView.Refresh();
        }
    }

    private void RaisePageSummaryChanged()
    {
        OnPropertyChanged(nameof(WorkStepCountText));
        OnPropertyChanged(nameof(OperationCountText));
    }

    #endregion
}
