using ControlLibrary;
using ControlLibrary.Controls.Loading;
using Module.Business.Models;
using Module.Business.Features.OperationEditing.ViewModels.PresentationModels;
using Module.Business.Features.Scheme.ViewModels.PresentationModels;
using Module.Business.Features.BusinessConfiguration.Services;
using Module.Business.Features.BusinessConfiguration.ViewModels.PresentationModels;
using Module.Business.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Module.Business.Features.Scheme.ViewModels;
public sealed class SchemeConfigurationViewModel : ViewModelProperties
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

    private SchemeConfigurationCatalog _catalog = SchemeConfigurationStore.LoadCatalog();
    private DateTime _lastCreateOrCopyCommandAt = DateTime.MinValue;
    private bool _isWorkStepParameterDrawerOpen;
    private bool _isBatchWorkStepDrawerOpen;
    private string _batchWorkStepSearchText = string.Empty;
    private SchemeWorkStepItem? _editingSchemeWorkStep;
    private SchemeWorkStepItem? _originalSchemeWorkStep;
    private bool _isNewSchemeWorkStep;
    private readonly ObservableCollection<BusinessProfile> _workStepProfiles = BusinessConfigurationStore.Load();

    #endregion

    /// <summary>
    /// 独立的方案判断条件编辑器，由方案页面组合并通过右侧抽屉展示。
    /// </summary>
    public SchemeConditionEditorViewModel ConditionEditor { get; }

    #region 集合属性

    public ObservableCollection<SchemeProfile> Schemes => _catalog.Schemes;

    /// <summary>
    /// 业务配置中已有的业务名称集合，供方案业务类型下拉选择。
    /// </summary>
    public ObservableCollection<string> WorkStepTypes { get; } = new(
        BusinessConfigurationStore.Load()
            .Select(workStep => workStep.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name));

    /// <summary>当前内置业务中类型为“业务值”的输入参数。</summary>
    public ObservableCollection<SchemeWorkStepParameterItem> WorkStepInputParameters { get; } = new();

    /// <summary>当前内置业务中勾选显示到界面的返回参数。</summary>
    public ObservableCollection<SchemeWorkStepParameterItem> WorkStepReturnParameters { get; } = new();

    /// <summary>返回参数判断符号固定集合。</summary>
    public ObservableCollection<string> JudgeOperators { get; } = new()
    {
        "NA",
        "=",
        "≠",
        ">",
        "≥",
        "<",
        "≤",
        "＜{0}＜",
        "≤{0}≤",
        "()",
        "!()",
        "黑名单",
        "白名单",
    };
    /// <summary>
    /// 根据内置业务名称刷新抽屉中的输入参数和返回参数展示。
    /// </summary>
    public void SelectBuiltInWorkStep(string workStepName)
    {
        WorkStepInputParameters.Clear();
        WorkStepReturnParameters.Clear();
        BusinessProfile? workStep = _workStepProfiles.FirstOrDefault(item =>
            string.Equals(item.Name, workStepName, StringComparison.OrdinalIgnoreCase));
        if (workStep is null)
        {
            return;
        }

        foreach (InputParameter parameter in workStep.Operations
                     .SelectMany(operation => operation.Parameters)
                     .Where(parameter => string.Equals(parameter.ParameterType?.Trim(), "业务值", StringComparison.Ordinal))
                     // 同一内置业务的多个步骤可能引用同名业务值，界面只保留首次出现项。
                     .DistinctBy(
                         parameter => parameter.Value?.Trim() ?? string.Empty,
                         StringComparer.OrdinalIgnoreCase))
        {
            WorkStepInputParameters.Add(new SchemeWorkStepParameterItem
            {
                Name = parameter.Value,
                Value = parameter.ParameterName
            });
        }

        foreach ((WorkStepOperation operation, ReturnValue returnValue) in workStep.Operations
                     .SelectMany(operation => operation.ReturnValues
                         .Where(returnValue => returnValue.IsShowView)
                         .Select(returnValue => (operation, returnValue)))
                     // 多个步骤可能配置出相同的完整返回值名称，页面和方案实例只保留首次出现项。
                     .DistinctBy(
                         item => string.IsNullOrWhiteSpace(item.operation.ReturnValue)
                             ? item.returnValue.ReturnParameterName?.Trim() ?? string.Empty
                             : $"{item.operation.ReturnValue.Trim()}_{item.returnValue.ReturnParameterName?.Trim()}",
                         StringComparer.OrdinalIgnoreCase))
        {
            string returnValueName = string.IsNullOrWhiteSpace(operation.ReturnValue)
                ? returnValue.ReturnParameterName
                : $"{operation.ReturnValue}_{returnValue.ReturnParameterName}";
            WorkStepReturnParameters.Add(new SchemeWorkStepParameterItem
            {
                Name = returnValueName,
                Value = returnValueName,
                Unit = returnValue.Unit
            });
        }
    }

    /// <summary>
    /// 切换内置业务并合并参数；新业务中不存在的旧参数继续保留，但标记为不使用。
    /// </summary>
    public void SwitchBuiltInWorkStep(string workStepName)
    {
        List<SchemeWorkStepParameterItem> previousInputParameters = WorkStepInputParameters
            .Select(parameter => parameter.Clone())
            .ToList();
        List<SchemeWorkStepParameterItem> previousReturnParameters = WorkStepReturnParameters
            .Select(parameter => parameter.Clone())
            .ToList();

        SelectBuiltInWorkStep(workStepName);

        HashSet<string> currentInputNames = new(
            WorkStepInputParameters.Select(parameter => parameter.Name?.Trim() ?? string.Empty),
            StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < WorkStepInputParameters.Count; index++)
        {
            string parameterName = WorkStepInputParameters[index].Name?.Trim() ?? string.Empty;
            SchemeWorkStepParameterItem? previousParameter = previousInputParameters.FirstOrDefault(parameter =>
                string.Equals(parameter.Name?.Trim(), parameterName, StringComparison.OrdinalIgnoreCase));
            if (previousParameter is not null)
            {
                previousParameter.IsUsed = true;
                WorkStepInputParameters[index] = previousParameter;
            }
        }

        foreach (SchemeWorkStepParameterItem legacyParameter in previousInputParameters.Where(parameter =>
                     !currentInputNames.Contains(parameter.Name?.Trim() ?? string.Empty)))
        {
            legacyParameter.IsUsed = false;
            WorkStepInputParameters.Add(legacyParameter);
        }

        HashSet<string> currentReturnNames = new(
            WorkStepReturnParameters.Select(parameter => parameter.Value?.Trim() ?? string.Empty),
            StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < WorkStepReturnParameters.Count; index++)
        {
            string returnValueName = WorkStepReturnParameters[index].Value?.Trim() ?? string.Empty;
            SchemeWorkStepParameterItem? previousParameter = previousReturnParameters.FirstOrDefault(parameter =>
                string.Equals(parameter.Value?.Trim(), returnValueName, StringComparison.OrdinalIgnoreCase));
            if (previousParameter is not null)
            {
                previousParameter.IsUsed = true;
                WorkStepReturnParameters[index] = previousParameter;
            }
        }

        foreach (SchemeWorkStepParameterItem legacyParameter in previousReturnParameters.Where(parameter =>
                     !currentReturnNames.Contains(parameter.Value?.Trim() ?? string.Empty)))
        {
            legacyParameter.IsUsed = false;
            WorkStepReturnParameters.Add(legacyParameter);
        }
    }

    public ICollectionView SchemesView { get; private set; } = null!;

    /// <summary>
    /// 批量业务抽屉中经过名称筛选的内置业务视图。
    /// </summary>
    public ICollectionView BatchWorkStepsView { get; private set; } = null!;

    public string CurrentSchemeStepName => SelectedSchemeStep?.StepName ?? string.Empty;

    /// <summary>
    /// 是否显示方案业务参数配置抽屉。
    /// </summary>
    public bool IsWorkStepParameterDrawerOpen
    {
        get => _isWorkStepParameterDrawerOpen;
        private set => SetField(ref _isWorkStepParameterDrawerOpen, value);
    }

    /// <summary>
    /// 是否显示批量业务浏览抽屉。
    /// </summary>
    public bool IsBatchWorkStepDrawerOpen
    {
        get => _isBatchWorkStepDrawerOpen;
        private set => SetField(ref _isBatchWorkStepDrawerOpen, value);
    }

    /// <summary>
    /// 批量业务抽屉中的名称筛选文本。
    /// </summary>
    public string BatchWorkStepSearchText
    {
        get => _batchWorkStepSearchText;
        set
        {
            if (SetField(ref _batchWorkStepSearchText, value ?? string.Empty))
            {
                BatchWorkStepsView.Refresh();
            }
        }
    }

    /// <summary>
    /// 抽屉中正在编辑的业务副本；保存前不会修改方案集合。
    /// </summary>
    public SchemeWorkStepItem? EditingSchemeWorkStep
    {
        get => _editingSchemeWorkStep;
        private set => SetField(ref _editingSchemeWorkStep, value);
    }

    #endregion

    #region 当前选择与搜索

    #region 搜索文本

    private string _searchText = string.Empty;


    /// 搜索文本。
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

            SchemesView.Refresh();
        }
    }

    #endregion

    #region 当前方案

    private SchemeProfile? _selectedScheme;

    /// <summary>
    /// 当前方案。
    /// </summary>
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

    #endregion

    #region 当前方案业务

    private SchemeWorkStepItem? _selectedSchemeStep;

    /// <summary>
    /// 当前方案业务。
    /// </summary>
    public SchemeWorkStepItem? SelectedSchemeStep
    {
        get => _selectedSchemeStep;
        set
        {
            if (ReferenceEquals(_selectedSchemeStep, value))
            {
                return;
            }

            // 右侧参数表格直接编辑页面集合，切换业务前必须先回写旧业务，避免被新业务默认值覆盖。
            SaveDisplayedParametersToSelectedSchemeStep();
            if (_selectedSchemeStep is not null)
            {
                _selectedSchemeStep.PropertyChanged -= SelectedSchemeStep_PropertyChanged;
            }

            _selectedSchemeStep = value;

            if (_selectedSchemeStep is not null)
            {
                _selectedSchemeStep.PropertyChanged += SelectedSchemeStep_PropertyChanged;
            }

            // 表格选中业务切换时先生成最新默认参数，再按参数名称合并方案业务已保存的数据。
            // 已存在的名称保留实例值，仅补充内置业务后来新增的参数名称。
            WorkStepInputParameters.Clear();
            WorkStepReturnParameters.Clear();
            if (_selectedSchemeStep is not null)
            {
                // 每次切换方案业务都重新加载业务配置，确保参数结构来自当前最新内置业务。
                RefreshBuiltInWorkSteps();
                SelectBuiltInWorkStep(_selectedSchemeStep.StepType);
                if (_selectedSchemeStep.InputParameters.Count > 0)
                {
                    List<SchemeWorkStepParameterItem> savedInputParameters = _selectedSchemeStep.InputParameters
                        .DistinctBy(
                            parameter => parameter.Name?.Trim() ?? string.Empty,
                            StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    HashSet<string> defaultInputNames = new(
                        WorkStepInputParameters.Select(parameter => parameter.Name?.Trim() ?? string.Empty),
                        StringComparer.OrdinalIgnoreCase);

                    for (int index = 0; index < WorkStepInputParameters.Count; index++)
                    {
                        string parameterName = WorkStepInputParameters[index].Name?.Trim() ?? string.Empty;
                        SchemeWorkStepParameterItem? savedParameter = savedInputParameters.FirstOrDefault(parameter =>
                            string.Equals(parameter.Name?.Trim(), parameterName, StringComparison.OrdinalIgnoreCase));
                        if (savedParameter is not null)
                        {
                            savedParameter.IsUsed = true;
                            WorkStepInputParameters[index] = savedParameter.Clone();
                        }
                    }

                    foreach (SchemeWorkStepParameterItem savedParameter in savedInputParameters.Where(parameter =>
                                 !defaultInputNames.Contains(parameter.Name?.Trim() ?? string.Empty)))
                    {
                        savedParameter.IsUsed = false;
                        WorkStepInputParameters.Add(savedParameter.Clone());
                    }
                }

                if (_selectedSchemeStep.ReturnParameters.Count > 0)
                {
                    List<SchemeWorkStepParameterItem> savedReturnParameters = _selectedSchemeStep.ReturnParameters
                        .DistinctBy(
                            parameter => parameter.Value?.Trim() ?? string.Empty,
                            StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    HashSet<string> defaultReturnNames = new(
                        WorkStepReturnParameters.Select(parameter => parameter.Value?.Trim() ?? string.Empty),
                        StringComparer.OrdinalIgnoreCase);

                    for (int index = 0; index < WorkStepReturnParameters.Count; index++)
                    {
                        string returnValueName = WorkStepReturnParameters[index].Value?.Trim() ?? string.Empty;
                        SchemeWorkStepParameterItem? savedParameter = savedReturnParameters.FirstOrDefault(parameter =>
                            string.Equals(parameter.Value?.Trim(), returnValueName, StringComparison.OrdinalIgnoreCase));
                        if (savedParameter is not null)
                        {
                            savedParameter.IsUsed = true;
                            WorkStepReturnParameters[index] = savedParameter.Clone();
                        }
                    }

                    foreach (SchemeWorkStepParameterItem savedParameter in savedReturnParameters.Where(parameter =>
                                 !defaultReturnNames.Contains(parameter.Value?.Trim() ?? string.Empty)))
                    {
                        savedParameter.IsUsed = false;
                        WorkStepReturnParameters.Add(savedParameter.Clone());
                    }
                }
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentSchemeStepName));
            OnPropertyChanged(nameof(AreAllSchemeStepsStartupEnabled));
            RaiseCommandStatesChanged();
        }
    }

    #endregion

    #endregion

    #region 页面展示属性

    #region 页面状态文本

    private string _pageStatusText = "等待编辑";

    /// <summary>
    /// 页面状态文本。
    /// </summary>
    public string PageStatusText
    {
        get => _pageStatusText;
        private set => SetField(ref _pageStatusText, value);
    }

    #endregion

    #region 页面状态颜色

    private Brush _pageStatusBrush = NeutralBrush;

    /// <summary>
    /// 页面状态颜色。
    /// </summary>
    public Brush PageStatusBrush
    {
        get => _pageStatusBrush;
        private set => SetField(ref _pageStatusBrush, value);
    }

    #endregion

    #region 方案业务启用列头的全选状态
    /// <summary>
    /// 方案业务启用列头的全选状态。
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

    #endregion

    #region 命令属性
    /// <summary>
    /// 新增方案。
    /// </summary>
    public ICommand NewSchemeCommand { get; private set; } = null!;

    /// <summary>
    /// 复制当前选中的方案。
    /// </summary>
    public ICommand DuplicateSchemeCommand { get; private set; } = null!;

    /// <summary>
    /// 删除当前选中的方案。
    /// </summary>
    public ICommand DeleteSchemeCommand { get; private set; } = null!;

    /// <summary>
    /// 保存全部方案配置。
    /// </summary>
    public ICommand SaveSchemesCommand { get; private set; } = null!;

    /// <summary>
    /// 打开命令参数所对应方案的判断条件编辑抽屉。
    /// </summary>
    public ICommand OpenConditionEditorCommand { get; private set; } = null!;

    /// <summary>
    /// 向当前方案新增一个业务。
    /// </summary>
    public ICommand AddWorkStepToSchemeCommand { get; private set; } = null!;

    /// <summary>
    /// 打开批量业务浏览抽屉。
    /// </summary>
    public ICommand OpenBatchWorkStepDrawerCommand { get; private set; } = null!;

    /// <summary>
    /// 关闭批量业务浏览抽屉。
    /// </summary>
    public ICommand CloseBatchWorkStepDrawerCommand { get; private set; } = null!;

    /// <summary>
    /// 从当前方案移除选中的业务。
    /// </summary>
    public ICommand RemoveWorkStepFromSchemeCommand { get; private set; } = null!;

    /// <summary>
    /// 打开当前方案业务的参数配置抽屉。
    /// </summary>
    public ICommand OpenWorkStepParameterDrawerCommand { get; private set; } = null!;

    /// <summary>
    /// 关闭方案业务参数配置抽屉。
    /// </summary>
    public ICommand CloseWorkStepParameterDrawerCommand { get; private set; } = null!;

    /// <summary>
    /// 保存业务编辑副本，并在新建时插入方案集合。
    /// </summary>
    public ICommand SaveWorkStepParameterDrawerCommand { get; private set; } = null!;

    /// <summary>
    /// 清理当前业务中已经标记为不使用的输入参数和返回参数。
    /// </summary>
    public ICommand CleanupUnusedParametersCommand { get; private set; } = null!;

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
            OnPropertyChanged(nameof(AreAllSchemeStepsStartupEnabled));
        }
    }

    /// <summary>
    /// 当前选中方案业务变化时，同步右侧步骤编辑器与统计信息。
    /// </summary>
    private void SelectedSchemeStep_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SchemeWorkStepItem.StepName))
        {
            OnPropertyChanged(nameof(CurrentSchemeStepName));
        }

        if (e.PropertyName is nameof(SchemeWorkStepItem.IsStartupEnabled))
        {
            OnPropertyChanged(nameof(AreAllSchemeStepsStartupEnabled));
        }
    }

    public static WorkStepOperation CreateDefaultOperation()
    {
        return new WorkStepOperation
        {
            OperationObjectName = "System",
            PCommandName = string.Empty,
            LuaScript = string.Empty,
            DelayMilliseconds = 0,
            IsEditParameter = false
        };
    }

    private void RaisePageSummaryChanged()
    {
        OnPropertyChanged(nameof(AreAllSchemeStepsStartupEnabled));
    }

    #endregion

    #region 构造与初始化

    public SchemeConfigurationViewModel()
    {
        ConditionEditor = new SchemeConditionEditorViewModel();
        ConditionEditor.ParametersSaved += ConditionEditor_ParametersSaved;
        Schemes.CollectionChanged += Schemes_CollectionChanged;
        SchemesView = CollectionViewSource.GetDefaultView(Schemes);
        SchemesView.Filter = FilterSchemes;
        // 使用独立视图，避免抽屉筛选影响业务类型下拉框中的完整名称集合。
        BatchWorkStepsView = new CollectionViewSource { Source = WorkStepTypes }.View;
        BatchWorkStepsView.Filter = item => item is string workStepName &&
            (string.IsNullOrWhiteSpace(BatchWorkStepSearchText) ||
             workStepName.Contains(BatchWorkStepSearchText.Trim(), StringComparison.OrdinalIgnoreCase));
        InitializeCommands();
        SelectedScheme = Schemes.FirstOrDefault();
        SetPageStatus(
            Schemes.Count == 0 ? "暂无方案配置，请点击新增。" : $"已加载 {Schemes.Count} 个方案。",
            NeutralBrush);
    }

    /// <summary>
    /// 初始化页面命令。
    /// </summary>
    private void InitializeCommands()
    {
        NewSchemeCommand = new RelayCommand(_ => NewScheme());
        DuplicateSchemeCommand = new RelayCommand(_ => DuplicateSelectedScheme(), _ => SelectedScheme is not null);
        DeleteSchemeCommand = new RelayCommand(_ => DeleteSelectedScheme(), _ => SelectedScheme is not null);
        SaveSchemesCommand = new RelayCommand(_ => SaveSchemes());
        OpenConditionEditorCommand = new AsyncRelayCommand(
            async parameter =>
            {
                if (parameter is SchemeProfile scheme)
                {
                    // 判断条件始终对应当前方案；按钮位于方案卡片内时，先同步当前选中方案。
                    if (!ReferenceEquals(SelectedScheme, scheme))
                    {
                        SelectedScheme = scheme;
                    }

                    // 右侧参数表格使用独立页面集合，打开判断条件前先回写当前方案业务。
                    SaveDisplayedParametersToSelectedSchemeStep();
                    if (SelectedScheme is not null)
                    {
                        // 判断条件配置面向方案中的全部业务，打开前重新读取业务配置，确保新增参数能够立即参与合并。
                        RefreshBuiltInWorkSteps();
                        LoadingOverlayService.Show("正在加载判断条件配置…");
                        try
                        {
                            // 先让主窗口完成遮罩首帧渲染，再生成判断条件数据，避免点击后出现界面无响应的观感。
                            await System.Windows.Application.Current.Dispatcher.InvokeAsync(
                                () => { },
                                DispatcherPriority.Render);
                            await ConditionEditor.OpenAsync(SelectedScheme, _workStepProfiles);

                            // 弹框数据和首帧布局完成后再关闭遮罩，用户看到的即为可操作的完整界面。
                            await System.Windows.Application.Current.Dispatcher.InvokeAsync(
                                () => { },
                                DispatcherPriority.ContextIdle);
                        }
                        finally
                        {
                            LoadingOverlayService.Close();
                        }
                    }
                }
            });
        AddWorkStepToSchemeCommand = new RelayCommand(_ => AddWorkStepToScheme(), _ => SelectedScheme is not null);
        OpenBatchWorkStepDrawerCommand = new RelayCommand(
            _ =>
            {
                RefreshBuiltInWorkSteps();
                BatchWorkStepSearchText = string.Empty;
                IsBatchWorkStepDrawerOpen = true;
            });
        CloseBatchWorkStepDrawerCommand = new RelayCommand(_ => IsBatchWorkStepDrawerOpen = false);
        RemoveWorkStepFromSchemeCommand = new RelayCommand(
            _ => RemoveSelectedSchemeStep(),
            _ => SelectedScheme is not null && (SelectedSchemeStep is not null || SelectedScheme.Steps.Any(step => step.IsChecked)));
        OpenWorkStepParameterDrawerCommand = new RelayCommand(
            parameter =>
            {
                if (parameter is SchemeWorkStepItem workStep)
                {
                    RefreshBuiltInWorkSteps();
                    SelectedSchemeStep = workStep;
                    _originalSchemeWorkStep = workStep;
                    _isNewSchemeWorkStep = false;
                    EditingSchemeWorkStep = workStep.Clone();
                }

                if (EditingSchemeWorkStep is not null)
                {
                    IsWorkStepParameterDrawerOpen = true;
                }
            });
        CloseWorkStepParameterDrawerCommand = new RelayCommand(_ => CloseWorkStepParameterDrawer());
        SaveWorkStepParameterDrawerCommand = new RelayCommand(_ => SaveWorkStepParameterDrawer());
        CleanupUnusedParametersCommand = new RelayCommand(_ => CleanupUnusedParameters());
    }

    #endregion

    #region 判断条件参数同步

    /// <summary>
    /// 判断条件保存后刷新当前选中业务的页面参数副本，避免保存方案时旧副本覆盖新值。
    /// </summary>
    private void ConditionEditor_ParametersSaved(SchemeProfile scheme)
    {
        if (!ReferenceEquals(SelectedScheme, scheme) || SelectedSchemeStep is null)
        {
            return;
        }

        WorkStepInputParameters.Clear();
        foreach (SchemeWorkStepParameterItem parameter in SelectedSchemeStep.InputParameters)
        {
            WorkStepInputParameters.Add(parameter.Clone());
        }

        WorkStepReturnParameters.Clear();
        foreach (SchemeWorkStepParameterItem parameter in SelectedSchemeStep.ReturnParameters)
        {
            WorkStepReturnParameters.Add(parameter.Clone());
        }
    }

    #endregion

    #region 方案配置命令

    /// <summary>
    /// 清理切换内置业务后遗留且不再使用的输入参数和返回参数。
    /// </summary>
    private void CleanupUnusedParameters()
    {
        int removedCount = WorkStepInputParameters.Count(parameter => !parameter.IsUsed) +
                           WorkStepReturnParameters.Count(parameter => !parameter.IsUsed);

        // 反向移除集合项，避免删除过程中索引移动导致遗漏。
        for (int index = WorkStepInputParameters.Count - 1; index >= 0; index--)
        {
            if (!WorkStepInputParameters[index].IsUsed)
            {
                WorkStepInputParameters.RemoveAt(index);
            }
        }

        for (int index = WorkStepReturnParameters.Count - 1; index >= 0; index--)
        {
            if (!WorkStepReturnParameters[index].IsUsed)
            {
                WorkStepReturnParameters.RemoveAt(index);
            }
        }

        SetPageStatus(
            removedCount > 0 ? $"已清理 {removedCount} 个无用参数。" : "当前没有需要清理的无用参数。",
            removedCount > 0 ? SuccessBrush : WarningBrush);
    }

    /// <summary>
    /// 新增一个默认方案并立即选中。
    /// </summary>
    private void NewScheme()
    {
        if (!CanRunCreateOrCopyCommand())
        {
            return;
        }

        SchemeProfile scheme = new()
        {
            SchemeName = GenerateUniqueSchemeName("方案")
        };
        Schemes.Add(scheme);
        SelectCreatedScheme(scheme);
        SetPageStatus("已新增方案。", SuccessBrush);
    }

    /// <summary>
    /// 复制当前选中的方案及其业务。
    /// </summary>
    private void DuplicateSelectedScheme()
    {
        if (!CanRunCreateOrCopyCommand() || SelectedScheme is null)
        {
            return;
        }

        SchemeProfile scheme = CreateCopyScheme(SelectedScheme);
        Schemes.Add(scheme);
        SelectCreatedScheme(scheme);
        SetPageStatus($"已复制方案：{scheme.SchemeName}。", SuccessBrush);
    }

    /// <summary>
    /// 删除当前选中的方案。
    /// </summary>
    private void DeleteSelectedScheme()
    {
        if (SelectedScheme is null)
        {
            return;
        }

        int index = Schemes.IndexOf(SelectedScheme);

        Schemes.Remove(SelectedScheme);
        SelectedScheme = Schemes.Count == 0
            ? null
            : Schemes[Math.Clamp(index, 0, Schemes.Count - 1)];

        SetPageStatus("已删除方案，保存后生效。", WarningBrush);
    }

    /// <summary>
    /// 保存全部方案配置。
    /// </summary>
    private void SaveSchemes()
    {
        // 页面右侧参数表格不属于业务编辑抽屉，保存方案前同步当前显示参数。
        SaveDisplayedParametersToSelectedSchemeStep();
        if (!ValidateSchemes(out string message))
        {
            SetPageStatus(message, WarningBrush);
            return;
        }

        List<(SchemeProfile Scheme, DateTime PreviousTime)> modifiedSchemes = Schemes
            .Where(scheme => scheme.IsModified)
            .Select(scheme => (scheme, scheme.LastModifiedAt))
            .ToList();
        DateTime savedAt = DateTime.Now;

        // 保存文件前写入时间，确保落盘数据与界面一致；保存失败时恢复原时间和未保存状态。
        foreach ((SchemeProfile scheme, _) in modifiedSchemes)
        {
            scheme.LastModifiedAt = savedAt;
        }

        try
        {
            SchemeConfigurationStore.SaveCatalog(_catalog);
            foreach ((SchemeProfile scheme, _) in modifiedSchemes)
            {
                scheme.AcceptChanges(savedAt);
            }
        }
        catch
        {
            foreach ((SchemeProfile scheme, DateTime previousTime) in modifiedSchemes)
            {
                scheme.LastModifiedAt = previousTime;
                scheme.MarkModified();
            }

            throw;
        }

        SetPageStatus($"已保存 {Schemes.Count} 个方案。", SuccessBrush);
    }

    #endregion

    #region 方案业务命令

    /// <summary>
    /// 在当前方案中新增业务。
    /// </summary>
    private void AddWorkStepToScheme()
    {
        if (SelectedScheme is null)
        {
            return;
        }

        SaveDisplayedParametersToSelectedSchemeStep();
        RefreshBuiltInWorkSteps();
        EditingSchemeWorkStep = new SchemeWorkStepItem
        {
            StepName = GenerateUniqueSchemeStepName("业务"),
            IsStartupEnabled = true
        };
        _originalSchemeWorkStep = null;
        _isNewSchemeWorkStep = true;
        WorkStepInputParameters.Clear();
        WorkStepReturnParameters.Clear();
        IsWorkStepParameterDrawerOpen = true;
    }

    /// <summary>
    /// 将右侧参数表格当前显示的数据回写到选中的方案业务实例。
    /// </summary>
    private void SaveDisplayedParametersToSelectedSchemeStep()
    {
        if (_selectedSchemeStep is null)
        {
            return;
        }

        _selectedSchemeStep.InputParameters = new ObservableCollection<SchemeWorkStepParameterItem>(
            WorkStepInputParameters.Select(parameter => parameter.Clone()));
        _selectedSchemeStep.ReturnParameters = new ObservableCollection<SchemeWorkStepParameterItem>(
            WorkStepReturnParameters.Select(parameter => parameter.Clone()));
    }

    /// <summary>
    /// 将指定内置业务复制为方案业务，并按表格拖放位置插入当前方案。
    /// </summary>
    /// <param name="workStepName">内置业务名称。</param>
    /// <param name="targetSchemeStep">拖放命中的目标方案业务；为空时追加到末尾。</param>
    /// <param name="insertAfter">是否插入目标业务之后。</param>
    public void AddBuiltInWorkStepToScheme(
        string workStepName,
        SchemeWorkStepItem? targetSchemeStep,
        bool insertAfter)
    {
        if (SelectedScheme is null || string.IsNullOrWhiteSpace(workStepName))
        {
            return;
        }

        BusinessProfile? builtInWorkStep = _workStepProfiles.FirstOrDefault(workStep =>
            string.Equals(workStep.Name, workStepName, StringComparison.OrdinalIgnoreCase));
        if (builtInWorkStep is null)
        {
            SetPageStatus($"未找到内置业务：{workStepName}。", WarningBrush);
            return;
        }

        // 复用现有参数生成规则，并为方案业务保存独立副本，避免后续编辑影响内置业务配置。
        SelectBuiltInWorkStep(builtInWorkStep.Name);
        SchemeWorkStepItem newWorkStep = new()
        {
            StepName = builtInWorkStep.Name,
            StepType = builtInWorkStep.Name,
            IsStartupEnabled = true,
            InputParameters = new ObservableCollection<SchemeWorkStepParameterItem>(
                WorkStepInputParameters.Select(parameter => parameter.Clone())),
            ReturnParameters = new ObservableCollection<SchemeWorkStepParameterItem>(
                WorkStepReturnParameters.Select(parameter => parameter.Clone()))
        };

        ObservableCollection<SchemeWorkStepItem> schemeSteps = SelectedScheme.Steps;
        int targetIndex = targetSchemeStep is null ? -1 : schemeSteps.IndexOf(targetSchemeStep);
        int insertIndex = targetIndex < 0
            ? schemeSteps.Count
            : Math.Clamp(targetIndex + (insertAfter ? 1 : 0), 0, schemeSteps.Count);
        schemeSteps.Insert(insertIndex, newWorkStep);
        RefreshSchemeStepNumbers();
        SelectedSchemeStep = newWorkStep;
        SetPageStatus($"已添加内置业务：{builtInWorkStep.Name}。", SuccessBrush);
        RaiseCommandStatesChanged();
    }

    /// <summary>
    /// 从业务配置重新加载内置业务及名称集合，确保方案新增、编辑时使用最新配置。
    /// </summary>
    private void RefreshBuiltInWorkSteps()
    {
        ObservableCollection<BusinessProfile> latestWorkSteps = BusinessConfigurationStore.Load();
        _workStepProfiles.Clear();
        foreach (BusinessProfile workStep in latestWorkSteps)
        {
            _workStepProfiles.Add(workStep);
        }

        WorkStepTypes.Clear();
        foreach (string workStepName in latestWorkSteps
                     .Select(workStep => workStep.Name)
                     .Where(name => !string.IsNullOrWhiteSpace(name))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(name => name))
        {
            WorkStepTypes.Add(workStepName);
        }
    }

    /// <summary>当前方案中的业务是否全部被勾选。</summary>
    public bool AreAllSchemeStepsChecked
    {
        get => SelectedScheme is not null && SelectedScheme.Steps.Count > 0 && SelectedScheme.Steps.All(step => step.IsChecked);
        set
        {
            if (SelectedScheme is null) return;
            foreach (SchemeWorkStepItem step in SelectedScheme.Steps) step.IsChecked = value;
            OnPropertyChanged();
            RaiseCommandStatesChanged();
        }
    }

    /// <summary>
    /// 保存业务抽屉中的编辑副本；新增业务只在此处写入方案集合。
    /// </summary>
    private void SaveWorkStepParameterDrawer()
    {
        if (SelectedScheme is null || EditingSchemeWorkStep is null ||
            string.IsNullOrWhiteSpace(EditingSchemeWorkStep.StepName))
        {
            SetPageStatus("业务名称不能为空。", WarningBrush);
            return;
        }

        string selectedBuiltInWorkStepName = EditingSchemeWorkStep.StepType?.Trim() ?? string.Empty;
        bool isBuiltInWorkStepChanged = _isNewSchemeWorkStep ||
            (_originalSchemeWorkStep is not null &&
             !string.Equals(
                 _originalSchemeWorkStep.StepType?.Trim(),
                 selectedBuiltInWorkStepName,
                 StringComparison.OrdinalIgnoreCase));

        // 新增业务或改变业务类型时，必须关联一个实际存在的内置业务。
        if (isBuiltInWorkStepChanged &&
            (string.IsNullOrWhiteSpace(selectedBuiltInWorkStepName) ||
             !_workStepProfiles.Any(workStep => string.Equals(
                 workStep.Name,
                 selectedBuiltInWorkStepName,
                 StringComparison.OrdinalIgnoreCase))))
        {
            SetPageStatus("请选择内置业务后再保存。", WarningBrush);
            return;
        }

        // 下拉框改变时不立即影响参数；确认保存后再合并新内置业务与已有参数。
        if (isBuiltInWorkStepChanged)
        {
            SwitchBuiltInWorkStep(selectedBuiltInWorkStepName);
        }

        if (_isNewSchemeWorkStep)
        {
            EditingSchemeWorkStep.InputParameters = new ObservableCollection<SchemeWorkStepParameterItem>(WorkStepInputParameters.Select(item => item.Clone()));
            EditingSchemeWorkStep.ReturnParameters = new ObservableCollection<SchemeWorkStepParameterItem>(WorkStepReturnParameters.Select(item => item.Clone()));
            SchemeWorkStepItem newWorkStep = EditingSchemeWorkStep.Clone();
            SelectedScheme.Steps.Add(newWorkStep);
            RefreshSchemeStepNumbers();
            SelectedSchemeStep = newWorkStep;
        }
        else if (_originalSchemeWorkStep is not null)
        {
            int targetNumber = EditingSchemeWorkStep.Num;
            _originalSchemeWorkStep.StepName = EditingSchemeWorkStep.StepName;
            _originalSchemeWorkStep.StepType = selectedBuiltInWorkStepName;
            _originalSchemeWorkStep.IsStartupEnabled = EditingSchemeWorkStep.IsStartupEnabled;
            _originalSchemeWorkStep.IsReTestEnabled = EditingSchemeWorkStep.IsReTestEnabled;
            _originalSchemeWorkStep.ReTestCount = EditingSchemeWorkStep.ReTestCount;
            _originalSchemeWorkStep.IsConfirmReTest = EditingSchemeWorkStep.IsConfirmReTest;
            _originalSchemeWorkStep.InputParameters = new ObservableCollection<SchemeWorkStepParameterItem>(WorkStepInputParameters.Select(item => item.Clone()));
            _originalSchemeWorkStep.ReturnParameters = new ObservableCollection<SchemeWorkStepParameterItem>(WorkStepReturnParameters.Select(item => item.Clone()));
            MoveSchemeStepToNumber(_originalSchemeWorkStep, targetNumber);
        }

        SetPageStatus($"已保存业务：{EditingSchemeWorkStep.StepName}。", SuccessBrush);
        CloseWorkStepParameterDrawer();
    }

    /// <summary>
    /// 关闭抽屉并丢弃尚未保存的编辑副本。
    /// </summary>
    private void CloseWorkStepParameterDrawer()
    {
        IsWorkStepParameterDrawerOpen = false;
        EditingSchemeWorkStep = null;
        _originalSchemeWorkStep = null;
        _isNewSchemeWorkStep = false;
    }

    /// <summary>
    /// 删除当前选中的方案业务。
    /// </summary>
    private void RemoveSelectedSchemeStep()
    {
        if (SelectedScheme is null)
        {
            return;
        }

        List<SchemeWorkStepItem> stepsToRemove = SelectedScheme.Steps.Where(step => step.IsChecked).ToList();
        if (stepsToRemove.Count == 0 && SelectedSchemeStep is not null) stepsToRemove.Add(SelectedSchemeStep);
        if (stepsToRemove.Count == 0) return;
        int index = stepsToRemove.Select(SelectedScheme.Steps.IndexOf).Where(item => item >= 0).DefaultIfEmpty(0).Min();
        foreach (SchemeWorkStepItem step in stepsToRemove) SelectedScheme.Steps.Remove(step);
        SelectedSchemeStep = SelectedScheme.Steps.Count == 0
            ? null
            : SelectedScheme.Steps[Math.Clamp(index, 0, SelectedScheme.Steps.Count - 1)];

        RefreshSchemeStepNumbers();
        SetPageStatus($"已删除 {stepsToRemove.Count} 个方案业务。", WarningBrush);
    }

    /// <summary>
    /// 调整方案业务顺序。
    /// </summary>
    public void MoveSchemeStep(SchemeWorkStepItem draggedSchemeStep, SchemeWorkStepItem targetSchemeStep, bool insertAfter)
    {
        if (SelectedScheme is null)
        {
            return;
        }

        ObservableCollection<SchemeWorkStepItem> steps = SelectedScheme.Steps;
        int oldIndex = steps.IndexOf(draggedSchemeStep);
        int targetIndex = steps.IndexOf(targetSchemeStep);
        if (oldIndex < 0 || targetIndex < 0 || oldIndex == targetIndex)
        {
            return;
        }

        int newIndex = targetIndex + (insertAfter ? 1 : 0);
        if (oldIndex < newIndex)
        {
            newIndex--;
        }

        newIndex = Math.Clamp(newIndex, 0, steps.Count - 1);
        if (oldIndex == newIndex)
        {
            return;
        }

        steps.Move(oldIndex, newIndex);
        RefreshSchemeStepNumbers();
        SelectedSchemeStep = draggedSchemeStep;
        SetPageStatus("已调整业务顺序。", SuccessBrush);
        RaiseCommandStatesChanged();
    }

    /// <summary>
    /// 将编辑业务输入的序号解释为目标位置，并在移动后统一生成连续序号。
    /// 处理方式与业务配置页面的步骤序号排序保持一致。
    /// </summary>
    public void MoveSchemeStepToNumber(SchemeWorkStepItem workStep, int targetNumber)
    {
        if (SelectedScheme is null || !SelectedScheme.Steps.Contains(workStep))
        {
            return;
        }

        ObservableCollection<SchemeWorkStepItem> steps = SelectedScheme.Steps;
        int oldIndex = steps.IndexOf(workStep);
        int targetIndex = Math.Clamp(targetNumber - 1, 0, steps.Count - 1);
        if (oldIndex != targetIndex)
        {
            steps.Move(oldIndex, targetIndex);
        }

        // 即使目标位置未变化，也重新编号，以修复输入的重复、零值或越界序号。
        RefreshSchemeStepNumbers();
        SelectedSchemeStep = workStep;
    }

    /// <summary>按照当前集合顺序连续刷新方案业务序号。</summary>
    private void RefreshSchemeStepNumbers()
    {
        if (SelectedScheme is null) return;
        for (int index = 0; index < SelectedScheme.Steps.Count; index++)
        {
            SelectedScheme.Steps[index].Num = index + 1;
        }
    }

    #endregion

    #region 校验与搜索

    /// <summary>
    /// 保存前校验方案数据。
    /// </summary>
    private bool ValidateSchemes(out string message)
    {
        if (Schemes.Count == 0)
        {
            message = "请至少保留一个方案。";
            return false;
        }

        HashSet<string> schemeNames = new(StringComparer.OrdinalIgnoreCase);

        foreach (SchemeProfile scheme in Schemes)
        {
            if (string.IsNullOrWhiteSpace(scheme.SchemeName))
            {
                message = "方案名称不能为空。";
                return false;
            }

            if (!schemeNames.Add(scheme.SchemeName.Trim()))
            {
                message = $"方案名称重复：{scheme.SchemeName}";
                return false;
            }

            foreach (SchemeWorkStepItem schemeStep in scheme.Steps)
            {
                if (string.IsNullOrWhiteSpace(schemeStep.StepName))
                {
                    message = $"方案“{scheme.SchemeName}”存在未命名业务。";
                    return false;
                }
            }
        }

        message = string.Empty;
        return true;
    }

    /// <summary>
    /// 按关键字过滤方案列表。
    /// </summary>
    private bool FilterSchemes(object item)
    {
        if (item is not SchemeProfile scheme)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        string keyword = SearchText.Trim();
        return Contains(scheme.SchemeName, keyword) ||
               scheme.Steps.Any(step =>
                   Contains(step.StepName, keyword) ||
                   _workStepProfiles.FirstOrDefault(workStep => string.Equals(
                           workStep.Name,
                           step.StepType,
                           StringComparison.OrdinalIgnoreCase))?
                       .Operations.Any(operation => Contains(operation.Summary, keyword)) == true);
    }

    private static bool Contains(string? source, string keyword)
    {
        return source?.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    #endregion

    #region 工厂与命名

    /// <summary>
    /// 深拷贝指定方案及其方案业务。
    /// </summary>
    private SchemeProfile CreateCopyScheme(SchemeProfile source)
    {
        return new SchemeProfile
        {
            Id = Guid.NewGuid().ToString("N"),
            SchemeName = GenerateCopySchemeName(source.SchemeName),
            Steps = new ObservableCollection<SchemeWorkStepItem>(
                source.Steps.Select(step =>
                {
                    SchemeWorkStepItem clone = step.Clone();
                    clone.Id = Guid.NewGuid().ToString("N");
                    return clone;
                }))
        };
    }

    /// <summary>
    /// 选中刚创建的方案，并让列表视图定位到该方案。
    /// </summary>
    private void SelectCreatedScheme(SchemeProfile scheme)
    {
        SearchText = string.Empty;
        SchemesView.Refresh();
        SelectedScheme = scheme;
        SchemesView.MoveCurrentTo(scheme);
    }

    /// <summary>
    /// 限制新增和复制命令的触发频率，避免连续点击重复创建。
    /// </summary>
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
    /// 根据前缀生成当前方案列表内唯一的方案名称。
    /// </summary>
    private string GenerateUniqueSchemeName(string prefix)
    {
        HashSet<string> existingNames = new(Schemes.Select(scheme => scheme.SchemeName), StringComparer.OrdinalIgnoreCase);
        int index = existingNames.Count + 1;
        string candidate;

        do
        {
            candidate = $"{prefix} {index}";
            index++;
        }
        while (existingNames.Contains(candidate));

        return candidate;
    }

    /// <summary>
    /// 根据原方案名称生成当前列表内唯一的复制方案名称。
    /// </summary>
    private string GenerateCopySchemeName(string baseName)
    {
        HashSet<string> existingNames = new(Schemes.Select(scheme => scheme.SchemeName), StringComparer.OrdinalIgnoreCase);
        string normalizedName = string.IsNullOrWhiteSpace(baseName) ? "方案" : baseName.Trim();
        string copyName = $"{normalizedName} 副本";
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
    /// 根据前缀生成目标方案内唯一的方案业务名称。
    /// </summary>
    private string GenerateUniqueSchemeStepName(string prefix, SchemeProfile? targetScheme = null)
    {
        SchemeProfile? scheme = targetScheme ?? SelectedScheme;
        HashSet<string> existingNames = new(
            scheme?.Steps.Select(step => step.StepName) ?? Enumerable.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);

        string baseName = string.IsNullOrWhiteSpace(prefix) ? "业务" : prefix.Trim();
        string candidate = baseName;
        int index = 1;

        while (existingNames.Contains(candidate))
        {
            index++;
            candidate = $"{baseName} {index}";
        }

        return candidate;
    }

    #endregion

    #region 页面状态与命令刷新

    private void Schemes_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RaisePageSummaryChanged();
        SchemesView.Refresh();
        RaiseCommandStatesChanged();
    }

    private void SetPageStatus(string text, Brush brush)
    {
        PageStatusText = text;
        PageStatusBrush = brush;
    }

    private void RaiseCommandStatesChanged()
    {
        RaiseCommandState(DuplicateSchemeCommand);
        RaiseCommandState(DeleteSchemeCommand);
        RaiseCommandState(AddWorkStepToSchemeCommand);
        RaiseCommandState(RemoveWorkStepFromSchemeCommand);
    }

    private static void RaiseCommandState(ICommand? command)
    {
        if (command is RelayCommand relayCommand)
        {
            relayCommand.RaiseCanExecuteChanged();
        }
    }

    #endregion

}



