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

    #endregion

    #region 集合属性

    public ObservableCollection<SchemeProfile> Schemes => _catalog.Schemes;

    public ICollectionView SchemesView { get; private set; } = null!;

    public ObservableCollection<WorkStepProfile> WorkSteps => _catalog.WorkSteps;

    public ObservableCollection<WorkStepProfile> AvailableWorkSteps { get; } = new();

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
            RaisePageSummaryChanged();
            RaiseCommandStatesChanged();
        }
    }

    public SchemeWorkStepItem? SelectedSchemeStep
    {
        get => _selectedSchemeStep;
        set
        {
            if (SetField(ref _selectedSchemeStep, value))
            {
                RaiseCommandStatesChanged();
            }
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
        if (e.PropertyName == nameof(SchemeProfile.ProductName))
        {
            RefreshAvailableWorkSteps();
        }

        if (e.PropertyName is nameof(SchemeProfile.StepCount)
            or nameof(SchemeProfile.ProductName)
            or nameof(SchemeProfile.SchemeName))
        {
            RaisePageSummaryChanged();
            SchemesView.Refresh();
        }
    }

    private void RaisePageSummaryChanged()
    {
        OnPropertyChanged(nameof(SchemeCountText));
        OnPropertyChanged(nameof(AvailableWorkStepCountText));
        OnPropertyChanged(nameof(SchemeStepCountText));
    }

    #endregion
}
