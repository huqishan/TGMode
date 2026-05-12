using Module.Business.Models;
using Module.Business.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace Module.Business.ViewModels;

/// <summary>
/// 方案配置界面属性定义。
/// </summary>
public sealed partial class SchemeConfigurationViewModel
{
    #region 状态颜色

    private static readonly Brush SuccessBrush =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16A34A"));

    private static readonly Brush WarningBrush =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EA580C"));

    private static readonly Brush NeutralBrush =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));

    #endregion

    #region 私有字段

    private BusinessConfigurationCatalog _catalog = BusinessConfigurationStore.LoadCatalog();
    private SchemeProfile? _selectedScheme;
    private SchemeWorkStepItem? _selectedSchemeStep;
    private WorkStepProfile? _schemeStepEditorHostWorkStep;
    private readonly List<RemovedSchemeStepUndoItem> _removedSchemeStepUndoItems = [];
    private string _searchText = string.Empty;
    private string _pageStatusText = "等待编辑";
    private Brush _pageStatusBrush = NeutralBrush;
    private DateTime _lastCreateOrCopyCommandAt = DateTime.MinValue;

    #endregion

    #region 撤回记录

    /// <summary>
    /// 方案工步删除后的撤回记录。
    /// </summary>
    private sealed class RemovedSchemeStepUndoItem
    {
        public string SchemeId { get; init; } = string.Empty;

        public int StepIndex { get; init; }

        public SchemeWorkStepItem SchemeStep { get; init; } = new();
    }

    #endregion

    #region 集合属性

    public ObservableCollection<SchemeProfile> Schemes => _catalog.Schemes;

    public ICollectionView SchemesView { get; private set; } = null!;

    /// <summary>
    /// 复用工步配置页的步骤编辑能力。
    /// </summary>
    public WorkStepConfigurationViewModel SchemeStepEditor { get; } = new();

    #endregion

    #region 当前选择与搜索

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

            BindSchemeStepEditor();
            OnPropertyChanged();
            OnPropertyChanged(nameof(SchemeStepOperationCountText));
            OnPropertyChanged(nameof(AreAllSchemeStepsStartupEnabled));
            RaiseCommandStatesChanged();
        }
    }

    #endregion

    #region 页面展示属性

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

    public string SchemeStepCountText => SelectedScheme is null
        ? "未选择方案"
        : $"{SelectedScheme.StepCount} 个工步";

    public string SchemeStepOperationCountText => SelectedSchemeStep is null
        ? "未选择工步"
        : $"{SelectedSchemeStep.Operations.Count} 个步骤";

    /// <summary>
    /// 方案工步启用列头的全选状态。
    /// </summary>
    public bool AreAllSchemeStepsStartupEnabled
    {
        get => SelectedScheme is not null &&
               SelectedScheme.Steps.Count > 0 &&
               SelectedScheme.Steps.All(step => step.IsStartupEnabled);
        set
        {
            if (SelectedScheme is null)
            {
                return;
            }

            foreach (SchemeWorkStepItem step in SelectedScheme.Steps
                         .Where(step => step.IsStartupEnabled != value)
                         .ToList())
            {
                step.IsStartupEnabled = value;
            }

            OnPropertyChanged();
            RaiseCommandStatesChanged();
        }
    }

    #endregion

    #region 命令属性

    public ICommand NewSchemeCommand { get; private set; } = null!;

    public ICommand DuplicateSchemeCommand { get; private set; } = null!;

    public ICommand DeleteSchemeCommand { get; private set; } = null!;

    public ICommand SaveSchemesCommand { get; private set; } = null!;

    public ICommand ImportSchemeCommand { get; private set; } = null!;

    public ICommand ExportSchemeCommand { get; private set; } = null!;

    public ICommand AddWorkStepToSchemeCommand { get; private set; } = null!;

    public ICommand RemoveWorkStepFromSchemeCommand { get; private set; } = null!;

    public ICommand UndoRemoveSchemeStepCommand { get; private set; } = null!;

    #endregion

    #region 属性联动

    /// <summary>
    /// 方案自身属性变化时，刷新页面统计与筛选。
    /// </summary>
    private void SelectedScheme_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SchemeProfile.StepCount)
            or nameof(SchemeProfile.SchemeName)
            or nameof(SchemeProfile.LastModifiedAt)
            or nameof(SchemeProfile.LastModifiedText))
        {
            RaisePageSummaryChanged();
        }

        if (e.PropertyName is nameof(SchemeProfile.StepCount)
            or nameof(SchemeProfile.Steps))
        {
            SchemesView.Refresh();
        }

        if (e.PropertyName == nameof(SchemeProfile.Steps))
        {
            OnPropertyChanged(nameof(SchemeStepOperationCountText));
            OnPropertyChanged(nameof(AreAllSchemeStepsStartupEnabled));
        }
    }

    /// <summary>
    /// 当前选中方案工步变化时，同步右侧步骤编辑器与统计信息。
    /// </summary>
    private void SelectedSchemeStep_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SchemeWorkStepItem.Operations))
        {
            BindSchemeStepEditor();
        }

        if (e.PropertyName is nameof(SchemeWorkStepItem.StepName)
            or nameof(SchemeWorkStepItem.SchemeStepName))
        {
            if (_schemeStepEditorHostWorkStep is not null)
            {
                _schemeStepEditorHostWorkStep.StepName = SelectedSchemeStep?.SchemeStepName ?? string.Empty;
            }
        }

        if (e.PropertyName is nameof(SchemeWorkStepItem.IsStartupEnabled)
            or nameof(SchemeWorkStepItem.Operations)
            or nameof(SchemeWorkStepItem.LastModifiedAt)
            or nameof(SchemeWorkStepItem.LastModifiedText))
        {
            OnPropertyChanged(nameof(SchemeStepOperationCountText));
            OnPropertyChanged(nameof(AreAllSchemeStepsStartupEnabled));
        }
    }

    /// <summary>
    /// 刷新页面顶部统计文本。
    /// </summary>
    private void RaisePageSummaryChanged()
    {
        OnPropertyChanged(nameof(SchemeCountText));
        OnPropertyChanged(nameof(SchemeStepCountText));
        OnPropertyChanged(nameof(SchemeStepOperationCountText));
        OnPropertyChanged(nameof(AreAllSchemeStepsStartupEnabled));
    }

    /// <summary>
    /// 让方案工步直接复用工步步骤编辑器。
    /// </summary>
    private void BindSchemeStepEditor()
    {
        if (SchemeStepEditor.CloseOperationDrawerCommand.CanExecute(null))
        {
            SchemeStepEditor.CloseOperationDrawerCommand.Execute(null);
        }

        if (SelectedSchemeStep is null)
        {
            _schemeStepEditorHostWorkStep = null;
            SchemeStepEditor.SelectedOperation = null;
            SchemeStepEditor.SelectedWorkStep = null;
            return;
        }

        _schemeStepEditorHostWorkStep = new WorkStepProfile
        {
            StepName = SelectedSchemeStep.SchemeStepName,
            Steps = SelectedSchemeStep.Operations
        };

        SchemeStepEditor.SelectedWorkStep = _schemeStepEditorHostWorkStep;
        SchemeStepEditor.SelectedOperation = _schemeStepEditorHostWorkStep.Steps.FirstOrDefault();
    }

    #endregion
}
