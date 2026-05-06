using Module.Business.Models;
using Module.Business.Services;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using System.Windows.Data;
using System.Windows.Media;

namespace Module.Business.ViewModels;

/// <summary>
/// 方案配置界面的属性集中声明。
/// </summary>
public sealed partial class SchemeConfigurationViewModel
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
    private SchemeProfile? _selectedScheme;
    private SchemeWorkStepItem? _selectedSchemeStep;
    private WorkStepProfile? _selectedAvailableWorkStep;
    private string _searchText = string.Empty;
    private string _pageStatusText = "等待编辑";
    private Brush _pageStatusBrush = NeutralBrush;
    private DateTime _lastCreateOrCopyCommandAt = DateTime.MinValue;
    private bool _isSynchronizingSchemeStepSnapshots;

    #endregion

    #region 集合属性

    public ObservableCollection<SchemeProfile> Schemes => _catalog.Schemes;

    public ICollectionView SchemesView { get; private set; } = null!;

    public ObservableCollection<WorkStepProfile> WorkSteps => _catalog.WorkSteps;

    public ObservableCollection<WorkStepProfile> AvailableWorkSteps { get; } = new();

    public ObservableCollection<string> ProductOptions { get; } = new();

    public ObservableCollection<string> SchemeStepParameterTypeOptions { get; } = new()
    {
        "设置值",
        "判断值"
    };
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

            SchemesView.Refresh();
        }
    }

    public SchemeProfile? SelectedScheme
    {
        get => _selectedScheme;
        set
        {
            if (ReferenceEquals(_selectedScheme, value))
            {
                return;
            }

            if (_selectedScheme is not null)
            {
                _selectedScheme.PropertyChanged -= SelectedScheme_PropertyChanged;
            }

            _selectedScheme = value;

            if (_selectedScheme is not null)
            {
                _selectedScheme.PropertyChanged += SelectedScheme_PropertyChanged;
            }

            SelectedSchemeStep = _selectedScheme?.Steps.FirstOrDefault();
            OnPropertyChanged();
            RefreshProductOptions();
            RefreshAvailableWorkSteps();
            SynchronizeSelectedSchemeWorkStepSnapshots();
            RaisePageSummaryChanged();
            RaiseCommandStatesChanged();
        }
    }

    public SchemeWorkStepItem? SelectedSchemeStep
    {
        get => _selectedSchemeStep;
        set
        {
            if (ReferenceEquals(_selectedSchemeStep, value))
            {
                return;
            }

            if (_selectedSchemeStep is not null)
            {
                _selectedSchemeStep.PropertyChanged -= SelectedSchemeStep_PropertyChanged;
            }

            _selectedSchemeStep = value;

            if (_selectedSchemeStep is not null)
            {
                _selectedSchemeStep.PropertyChanged += SelectedSchemeStep_PropertyChanged;
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayedSchemeStepParameters));
            OnPropertyChanged(nameof(SchemeStepParameterCountText));
            RaiseCommandStatesChanged();
        }
    }

    public WorkStepProfile? SelectedAvailableWorkStep
    {
        get => _selectedAvailableWorkStep;
        set
        {
            if (SetField(ref _selectedAvailableWorkStep, value))
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

    public string SchemeCountText => $"{Schemes.Count} 个方案";

    public string AvailableWorkStepCountText => SelectedScheme is null
        ? "未选择方案"
        : $"{AvailableWorkSteps.Count} 个可选工步";

    public string SchemeStepCountText => SelectedScheme is null
        ? "未选择方案"
        : $"{SelectedScheme.StepCount} 个方案工步";

    public string SchemeStepParameterCountText => SelectedSchemeStep is null
        ? "未选择工步"
        : $"{DisplayedSchemeStepParameters.Count} 个工步参数";

    public ObservableCollection<SchemeWorkStepParameter> DisplayedSchemeStepParameters =>
        FindCurrentWorkStep(SelectedSchemeStep) is null
            ? EmptySchemeStepParameters
            : SelectedSchemeStep?.Parameters ?? EmptySchemeStepParameters;

    #endregion

    #region 命令属性

    public ICommand NewSchemeCommand { get; private set; } = null!;

    public ICommand DuplicateSchemeCommand { get; private set; } = null!;

    public ICommand DeleteSchemeCommand { get; private set; } = null!;

    public ICommand SaveSchemesCommand { get; private set; } = null!;

    public ICommand ImportSchemeCommand { get; private set; } = null!;

    public ICommand ExportSchemeCommand { get; private set; } = null!;

    public ICommand RefreshWorkStepsCommand { get; private set; } = null!;

    public ICommand AddWorkStepToSchemeCommand { get; private set; } = null!;

    public ICommand RemoveWorkStepFromSchemeCommand { get; private set; } = null!;

    public ICommand MoveSchemeStepUpCommand { get; private set; } = null!;

    public ICommand MoveSchemeStepDownCommand { get; private set; } = null!;

    #endregion

    #region 属性联动方法

    private void SelectedScheme_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SchemeProfile.Steps))
        {
            SynchronizeSelectedSchemeWorkStepSnapshots();
        }

        if (e.PropertyName == nameof(SchemeProfile.ProductName))
        {
            RefreshAvailableWorkSteps();
            SynchronizeSelectedSchemeWorkStepSnapshots();
        }

        if (e.PropertyName is nameof(SchemeProfile.StepCount)
            or nameof(SchemeProfile.ProductName)
            or nameof(SchemeProfile.SchemeName))
        {
            RaisePageSummaryChanged();
        }

        if (e.PropertyName is nameof(SchemeProfile.StepCount)
            or nameof(SchemeProfile.ProductName)
            or nameof(SchemeProfile.SchemeName)
            or nameof(SchemeProfile.Steps))
        {
            SchemesView.Refresh();
        }

        if (e.PropertyName is nameof(SchemeProfile.ProductName)
            or nameof(SchemeProfile.Steps))
        {
            OnPropertyChanged(nameof(DisplayedSchemeStepParameters));
            OnPropertyChanged(nameof(SchemeStepParameterCountText));
        }
    }

    private void SelectedSchemeStep_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SchemeWorkStepItem.WorkStepId)
            or nameof(SchemeWorkStepItem.Parameters)
            or nameof(SchemeWorkStepItem.SchemeStepName))
        {
            OnPropertyChanged(nameof(DisplayedSchemeStepParameters));
            OnPropertyChanged(nameof(SchemeStepParameterCountText));
        }
    }

    private void RaisePageSummaryChanged()
    {
        OnPropertyChanged(nameof(SchemeCountText));
        OnPropertyChanged(nameof(AvailableWorkStepCountText));
        OnPropertyChanged(nameof(SchemeStepCountText));
        OnPropertyChanged(nameof(SchemeStepParameterCountText));
    }

    private void SynchronizeSelectedSchemeWorkStepSnapshots()
    {
        if (_isSynchronizingSchemeStepSnapshots || SelectedScheme is null)
        {
            return;
        }

        try
        {
            _isSynchronizingSchemeStepSnapshots = true;

            foreach (SchemeWorkStepItem schemeStep in SelectedScheme.Steps)
            {
                WorkStepProfile? workStep = FindCurrentWorkStep(schemeStep);
                if (workStep is null)
                {
                    continue;
                }

                ObservableCollection<SchemeWorkStepParameter> synchronizedParameters =
                    SchemeWorkStepItem.CreateParametersFromWorkStep(workStep, schemeStep.Parameters);
                bool needsSync =
                    !string.Equals(schemeStep.ProductName, workStep.ProductName, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(schemeStep.StepName, workStep.StepName, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(schemeStep.OperationSummary, workStep.OperationSummary, StringComparison.OrdinalIgnoreCase) ||
                    schemeStep.LastModifiedAt != workStep.LastModifiedAt ||
                    !HasSameSchemeStepParameters(schemeStep.Parameters, synchronizedParameters);

                if (!needsSync)
                {
                    continue;
                }

                schemeStep.ProductName = workStep.ProductName;
                schemeStep.StepName = workStep.StepName;
                schemeStep.OperationSummary = workStep.OperationSummary;
                schemeStep.LastModifiedAt = workStep.LastModifiedAt;
                schemeStep.Parameters = synchronizedParameters;
            }
        }
        finally
        {
            _isSynchronizingSchemeStepSnapshots = false;
        }
    }

    private WorkStepProfile? FindCurrentWorkStep(SchemeWorkStepItem? schemeStep)
    {
        if (SelectedScheme is null || schemeStep is null || string.IsNullOrWhiteSpace(schemeStep.WorkStepId))
        {
            return null;
        }

        WorkStepProfile? workStep = WorkSteps.FirstOrDefault(step =>
            string.Equals(step.Id, schemeStep.WorkStepId, StringComparison.Ordinal));
        if (workStep is null)
        {
            return null;
        }

        return string.Equals(workStep.ProductName, SelectedScheme.ProductName, StringComparison.OrdinalIgnoreCase)
            ? workStep
            : null;
    }

    private static bool HasSameSchemeStepParameters(
        ObservableCollection<SchemeWorkStepParameter> left,
        ObservableCollection<SchemeWorkStepParameter> right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left.Count != right.Count)
        {
            return false;
        }

        for (int index = 0; index < left.Count; index++)
        {
            SchemeWorkStepParameter leftParameter = left[index];
            SchemeWorkStepParameter rightParameter = right[index];
            if (!string.Equals(leftParameter.SourceOperationId, rightParameter.SourceOperationId, StringComparison.Ordinal) ||
                !string.Equals(leftParameter.SourceParameterId, rightParameter.SourceParameterId, StringComparison.Ordinal) ||
                !string.Equals(leftParameter.ParameterName, rightParameter.ParameterName, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(leftParameter.ParameterType, rightParameter.ParameterType, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(leftParameter.JudgeType, rightParameter.JudgeType, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(leftParameter.JudgeCondition, rightParameter.JudgeCondition, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static readonly ObservableCollection<SchemeWorkStepParameter> EmptySchemeStepParameters = new();

    #endregion
}
