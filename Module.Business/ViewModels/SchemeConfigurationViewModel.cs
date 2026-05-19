using ControlLibrary;
using Module.Business.Models;
using Module.Business.Services;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Linq;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using System.Collections.Specialized;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using Module.Business.ViewModels.PropertyVMs;

namespace Module.Business.ViewModels;

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

    private SchemeConfigurationCatalog _catalog = BusinessConfigurationStore.LoadCatalog();
    private readonly SchemeStepEditorState _schemeStepEditor = new();
    private SchemeProfile? _selectedScheme;
    private SchemeWorkStepItem? _selectedSchemeStep;
    private WorkStepProfile? _schemeStepEditorHostWorkStep;
    private WorkStepOperation? _trackedInlineOperation;
    private readonly List<RemovedSchemeStepUndoItem> _removedSchemeStepUndoItems = [];
    private string _searchText = string.Empty;
    private string _pageStatusText = "等待编辑";
    private Brush _pageStatusBrush = NeutralBrush;
    private DateTime _lastCreateOrCopyCommandAt = DateTime.MinValue;
    private bool _isSynchronizingInlineOperationSelection;
    private string _activeParameterOperationSummary = string.Empty;
    private IEnumerable? _activeInputParameterCollection;
    private IEnumerable? _activeReturnParameterCollection;
    private WorkStepOperationParameter? _selectedEditingReturnParameter;

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
    /// 复用步骤编辑器能力。
    /// </summary>

    public ObservableCollection<string> InlineOperationObjectOptions { get; } = new();

    public ObservableCollection<string> InlineInvokeMethodOptions { get; } = new();

    public ObservableCollection<SchemeProfile> SchemeNameCollection => Schemes;

    public ObservableCollection<SchemeWorkStepItem>? SchemeWorkStepCollection => SelectedScheme?.Steps;

    public ObservableCollection<WorkStepOperation>? StepCollection => _schemeStepEditor.SelectedWorkStep?.Steps;

    public WorkStepProfile? SelectedWorkStep => _schemeStepEditor.SelectedWorkStep;

    public ObservableCollection<string> OperationObjectCollection => _schemeStepEditor.OperationObjectOptions;

    public ObservableCollection<string> OperationObjectOptions => _schemeStepEditor.OperationObjectOptions;

    public DataView InvokeMethodCollection => _schemeStepEditor.OperationMethodTable.DefaultView;

    public DataTable OperationMethodTable => _schemeStepEditor.OperationMethodTable;

    public ObservableCollection<StationOperationMethodItem> StationOperationMethodCollection { get; } = new();

    public ObservableCollection<WorkStepOperationParameter> InputParameterCollection => _schemeStepEditor.EditingInvokeParameters;

    public ObservableCollection<WorkStepOperationParameter> EditingInvokeParameters => _schemeStepEditor.EditingInvokeParameters;

    public ObservableCollection<WorkStepOperationParameter> EditingReturnParameters { get; } = new();

    public IEnumerable? ActiveInputParameterCollection
    {
        get => _activeInputParameterCollection;
        private set => SetField(ref _activeInputParameterCollection, value);
    }

    public IEnumerable? ActiveReturnParameterCollection
    {
        get => _activeReturnParameterCollection;
        private set => SetField(ref _activeReturnParameterCollection, value);
    }

    public string ActiveParameterOperationSummary
    {
        get => _activeParameterOperationSummary;
        private set => SetField(ref _activeParameterOperationSummary, value);
    }

    public ObservableCollection<string> ParameterTypeCollection => _schemeStepEditor.ParameterTypeOptions;

    public ObservableCollection<string> ParameterTypeOptions => _schemeStepEditor.ParameterTypeOptions;

    public ObservableCollection<string> ReturnValueOptions => _schemeStepEditor.ReturnValueOptions;

    public DataRowView? SelectedInvokeMethodRow
    {
        get => _schemeStepEditor.SelectedOperationMethodRow;
        set => _schemeStepEditor.SelectedOperationMethodRow = value;
    }

    private StationOperationMethodItem? _selectedStationOperationMethod;

    public StationOperationMethodItem? SelectedStationOperationMethod
    {
        get => _selectedStationOperationMethod;
        set => SetField(ref _selectedStationOperationMethod, value);
    }

    public WorkStepOperation? SelectedStep
    {
        get => _schemeStepEditor.SelectedOperation;
        set => _schemeStepEditor.SelectedOperation = value;
    }

    public bool AreAllStepsChecked
    {
        get => _schemeStepEditor.AreAllOperationsChecked;
        set => _schemeStepEditor.AreAllOperationsChecked = value;
    }

    public string CurrentSchemeStepName => SelectedSchemeStep?.SchemeStepName ?? string.Empty;

    public string StepEditorTitle => _schemeStepEditor.OperationDrawerTitle;

    public string StepEditorHostStepName => _schemeStepEditor.SelectedWorkStep?.StepName ?? string.Empty;

    public bool IsStepEditorOpen => _schemeStepEditor.IsOperationDrawerOpen;

    public string EditorPageStatusText => _schemeStepEditor.PageStatusText;

    public Brush EditorPageStatusBrush => _schemeStepEditor.PageStatusBrush;

    public string EditingOperationObject
    {
        get => _schemeStepEditor.EditingOperationObject;
        set => _schemeStepEditor.EditingOperationObject = value;
    }

    public string EditingProtocolName
    {
        get => _schemeStepEditor.EditingProtocolName;
        set => _schemeStepEditor.EditingProtocolName = value;
    }

    public string EditingCommandName
    {
        get => _schemeStepEditor.EditingCommandName;
        set => _schemeStepEditor.EditingCommandName = value;
    }

    public string EditingInvokeMethod
    {
        get => _schemeStepEditor.EditingInvokeMethod;
        set => _schemeStepEditor.EditingInvokeMethod = value;
    }

    public bool EditingModifyInvokeParameters
    {
        get => _schemeStepEditor.EditingModifyInvokeParameters;
        set => _schemeStepEditor.EditingModifyInvokeParameters = value;
    }

    public string EditingReturnValue
    {
        get => _schemeStepEditor.EditingReturnValue;
        set => _schemeStepEditor.EditingReturnValue = value;
    }

    public bool EditingShowDataToView
    {
        get => _schemeStepEditor.EditingShowDataToView;
        set => _schemeStepEditor.EditingShowDataToView = value;
    }

    public string EditingViewDataName
    {
        get => _schemeStepEditor.EditingViewDataName;
        set => _schemeStepEditor.EditingViewDataName = value;
    }

    public string EditingViewJudgeType
    {
        get => _schemeStepEditor.EditingViewJudgeType;
        set => _schemeStepEditor.EditingViewJudgeType = value;
    }

    public string EditingViewJudgeCondition
    {
        get => _schemeStepEditor.EditingViewJudgeCondition;
        set => _schemeStepEditor.EditingViewJudgeCondition = value;
    }

    public string EditingLuaScript
    {
        get => _schemeStepEditor.EditingLuaScript;
        set => _schemeStepEditor.EditingLuaScript = value;
    }

    public string EditingDelayMillisecondsText
    {
        get => _schemeStepEditor.EditingDelayMillisecondsText;
        set => _schemeStepEditor.EditingDelayMillisecondsText = value;
    }

    public string EditingRemark
    {
        get => _schemeStepEditor.EditingRemark;
        set => _schemeStepEditor.EditingRemark = value;
    }

    public WorkStepOperationParameter? SelectedEditingInvokeParameter
    {
        get => _schemeStepEditor.SelectedEditingInvokeParameter;
        set => _schemeStepEditor.SelectedEditingInvokeParameter = value;
    }

    public WorkStepOperationParameter? SelectedEditingReturnParameter
    {
        get => _selectedEditingReturnParameter;
        set => SetField(ref _selectedEditingReturnParameter, value);
    }

    public bool IsLuaOperationSelected => _schemeStepEditor.IsLuaOperationSelected;

    public ICommand AddStepCommand => _schemeStepEditor.AddOperationCommand;

    public ICommand CopyStepCommand => _schemeStepEditor.CopyOperationCommand;

    public ICommand PasteStepCommand => _schemeStepEditor.PasteOperationCommand;

    public ICommand DeleteStepCommand => _schemeStepEditor.DeleteOperationCommand;

    public ICommand SaveStepEditorCommand => _schemeStepEditor.SaveOperationDrawerCommand;

    public ICommand CloseStepEditorCommand => _schemeStepEditor.CloseOperationDrawerCommand;

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
    /// 同步步骤编辑器属性变化到页面状态。
    /// </summary>
    private void SchemeStepEditor_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SchemeStepEditorState.SelectedOperation))
        {
            TrackInlineOperation(_schemeStepEditor.SelectedOperation);
            RefreshInlineEditingOptions();
            OnPropertyChanged(nameof(SelectedStep));
        }

        if (e.PropertyName is nameof(SchemeStepEditorState.SelectedWorkStep)
            or nameof(SchemeStepEditorState.OperationCountText))
        {
            RefreshInlineEditingOptions();
        }

        if (e.PropertyName == nameof(SchemeStepEditorState.SelectedWorkStep))
        {
            OnPropertyChanged(nameof(SelectedWorkStep));
            OnPropertyChanged(nameof(StepCollection));
        }

        if (e.PropertyName is nameof(SchemeStepEditorState.IsOperationDrawerOpen)
            or nameof(SchemeStepEditorState.OperationDrawerTitle)
            or nameof(SchemeStepEditorState.SelectedWorkStep))
        {
            OnPropertyChanged(nameof(IsStepEditorOpen));
            OnPropertyChanged(nameof(StepEditorTitle));
            OnPropertyChanged(nameof(StepEditorHostStepName));
        }

        if (e.PropertyName == nameof(SchemeStepEditorState.SelectedOperationMethodRow))
        {
            OnPropertyChanged(nameof(SelectedInvokeMethodRow));
        }

        if (e.PropertyName == nameof(SchemeStepEditorState.PageStatusText))
        {
            OnPropertyChanged(nameof(EditorPageStatusText));
        }

        if (e.PropertyName == nameof(SchemeStepEditorState.PageStatusBrush))
        {
            OnPropertyChanged(nameof(EditorPageStatusBrush));
        }

        if (e.PropertyName == nameof(SchemeStepEditorState.EditingOperationObject))
        {
            OnPropertyChanged(nameof(EditingOperationObject));
            OnPropertyChanged(nameof(IsLuaOperationSelected));
        }

        if (e.PropertyName == nameof(SchemeStepEditorState.EditingProtocolName))
        {
            OnPropertyChanged(nameof(EditingProtocolName));
        }

        if (e.PropertyName == nameof(SchemeStepEditorState.EditingCommandName))
        {
            OnPropertyChanged(nameof(EditingCommandName));
        }

        if (e.PropertyName == nameof(SchemeStepEditorState.EditingInvokeMethod))
        {
            OnPropertyChanged(nameof(EditingInvokeMethod));
        }

        if (e.PropertyName == nameof(SchemeStepEditorState.EditingModifyInvokeParameters))
        {
            OnPropertyChanged(nameof(EditingModifyInvokeParameters));
        }

        if (e.PropertyName == nameof(SchemeStepEditorState.EditingReturnValue))
        {
            OnPropertyChanged(nameof(EditingReturnValue));
        }

        if (e.PropertyName == nameof(SchemeStepEditorState.EditingShowDataToView))
        {
            OnPropertyChanged(nameof(EditingShowDataToView));
        }

        if (e.PropertyName == nameof(SchemeStepEditorState.EditingViewDataName))
        {
            OnPropertyChanged(nameof(EditingViewDataName));
        }

        if (e.PropertyName == nameof(SchemeStepEditorState.EditingViewJudgeType))
        {
            OnPropertyChanged(nameof(EditingViewJudgeType));
        }

        if (e.PropertyName == nameof(SchemeStepEditorState.EditingViewJudgeCondition))
        {
            OnPropertyChanged(nameof(EditingViewJudgeCondition));
        }

        if (e.PropertyName == nameof(SchemeStepEditorState.EditingLuaScript))
        {
            OnPropertyChanged(nameof(EditingLuaScript));
        }

        if (e.PropertyName == nameof(SchemeStepEditorState.EditingDelayMillisecondsText))
        {
            OnPropertyChanged(nameof(EditingDelayMillisecondsText));
        }

        if (e.PropertyName == nameof(SchemeStepEditorState.EditingRemark))
        {
            OnPropertyChanged(nameof(EditingRemark));
        }

        if (e.PropertyName == nameof(SchemeStepEditorState.SelectedEditingInvokeParameter))
        {
            OnPropertyChanged(nameof(SelectedEditingInvokeParameter));
        }

        if (e.PropertyName == nameof(SchemeStepEditorState.OperationMethodTable))
        {
            RefreshStationOperationMethodCollection();
            OnPropertyChanged(nameof(OperationMethodTable));
            OnPropertyChanged(nameof(InvokeMethodCollection));
        }
    }

    private void TrackInlineOperation(WorkStepOperation? operation)
    {
        if (ReferenceEquals(_trackedInlineOperation, operation))
        {
            return;
        }

        if (_trackedInlineOperation is not null)
        {
            _trackedInlineOperation.PropertyChanged -= InlineOperation_PropertyChanged;
        }

        _trackedInlineOperation = operation;

        if (_trackedInlineOperation is not null)
        {
            _trackedInlineOperation.PropertyChanged += InlineOperation_PropertyChanged;
        }
    }

    private void InlineOperation_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, _trackedInlineOperation) ||
            _isSynchronizingInlineOperationSelection)
        {
            return;
        }

        if (e.PropertyName is nameof(WorkStepOperation.OperationObject)
            or nameof(WorkStepOperation.InvokeMethod))
        {
            RefreshInlineEditingOptions();
            if (_trackedInlineOperation is not null)
            {
                _schemeStepEditor.ResetOperationParametersToDefault(_trackedInlineOperation);
            }
        }
    }

    private void RefreshInlineEditingOptions()
    {
        IEnumerable<WorkStepOperation> currentOperations =
            _schemeStepEditor.SelectedWorkStep?.Steps ?? Enumerable.Empty<WorkStepOperation>();

        ReplaceStringOptions(
            InlineOperationObjectOptions,
            new[]
            {
                SchemeStepEditorState.SystemOperationObjectName,
                SchemeStepEditorState.LuaOperationObjectName
            }
            .Concat(_schemeStepEditor.LoadDeviceOperationObjectNames())
            .Concat(currentOperations.Select(operation => operation.OperationObject))
            .Where(option => !SchemeStepEditorState.IsJudgeOperationObject(option)));

        List<string> invokeMethodOptions = _schemeStepEditor
            .LoadInvokeMethodOptionsForOperationObject(_schemeStepEditor.SelectedOperation?.OperationObject)
            .Where(option => !string.IsNullOrWhiteSpace(option))
            .Select(option => option.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        ReplaceStringOptions(InlineInvokeMethodOptions, invokeMethodOptions);
        SynchronizeInlineOperation(invokeMethodOptions);
    }

    private void SynchronizeInlineOperation(IReadOnlyList<string> invokeMethodOptions)
    {
        if (_isSynchronizingInlineOperationSelection ||
            _schemeStepEditor.SelectedOperation is null)
        {
            return;
        }

        _isSynchronizingInlineOperationSelection = true;
        try
        {
            _schemeStepEditor.SynchronizeOperationMetadata(
                _schemeStepEditor.SelectedOperation,
                invokeMethodOptions);
        }
        finally
        {
            _isSynchronizingInlineOperationSelection = false;
        }
    }

    private static void ReplaceStringOptions(ObservableCollection<string> target, IEnumerable<string> source)
    {
        List<string> options = source
            .Where(option => !string.IsNullOrWhiteSpace(option))
            .Select(option => option.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(option => option, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (target.SequenceEqual(options, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        target.Clear();
        foreach (string option in options)
        {
            target.Add(option);
        }
    }

    public WorkStepOperation? CreateStepFromInvokeMethodRow(DataRowView? rowView)
    {
        return _schemeStepEditor.CreateOperationFromMethodTableRow(rowView);
    }

    public WorkStepOperation? CreateOperationFromMethodItem(StationOperationMethodItem? item)
    {
        return _schemeStepEditor.CreateOperationFromMethodItem(item);
    }

    public WorkStepOperation? CreateOperationFromMethodTableRow(DataRowView? rowView)
    {
        return _schemeStepEditor.CreateOperationFromMethodTableRow(rowView);
    }

    public ObservableCollection<WorkStepOperationParameter> CreateReturnParametersFromOperation(WorkStepOperation? operation)
    {
        return _schemeStepEditor.CreateReturnParametersFromOperation(operation);
    }

    public void OpenStepEditorForEdit(WorkStepOperation operation)
    {
        _schemeStepEditor.OpenOperationDrawerForEdit(operation);
    }

    public void CloseStepEditor()
    {
        if (_schemeStepEditor.CloseOperationDrawerCommand.CanExecute(null))
        {
            _schemeStepEditor.CloseOperationDrawerCommand.Execute(null);
        }
    }

    public void MoveStep(WorkStepOperation draggedOperation, WorkStepOperation targetOperation, bool insertAfter)
    {
        _schemeStepEditor.MoveOperation(draggedOperation, targetOperation, insertAfter);
    }

    public void InsertStep(WorkStepOperation operation, WorkStepOperation? targetOperation, bool insertAfter)
    {
        _schemeStepEditor.InsertOperation(operation, targetOperation, insertAfter);
    }

    public bool ContainsCurrentStep(WorkStepOperation operation)
    {
        return StepCollection?.Contains(operation) == true;
    }

    public bool HasCurrentSchemeStep()
    {
        return SelectedSchemeStep is not null;
    }

    public bool HasModifiedStepParameters(
        WorkStepOperation operation,
        ObservableCollection<WorkStepOperationParameter> parameters)
    {
        return _schemeStepEditor.HasModifiedOperationParameters(operation, parameters);
    }

    public bool HasModifiedOperationParameters(WorkStepOperation operation)
    {
        return _schemeStepEditor.HasModifiedOperationParameters(operation);
    }

    public void RefreshOperationParameterModifiedStates(IEnumerable<WorkStepOperation> operations)
    {
        _schemeStepEditor.RefreshOperationParameterModifiedStates(operations);
    }

    public void SetStandaloneReturnValueOptions(IEnumerable<string> returnValues)
    {
        _schemeStepEditor.SetStandaloneReturnValueOptions(returnValues);
    }

    public void BeginStandaloneOperationEdit(
        WorkStepOperation? operation,
        string stepName = "工步",
        bool isDecisionOperationMode = false)
    {
        _schemeStepEditor.BeginStandaloneOperationEdit(operation, stepName, isDecisionOperationMode);
    }

    public bool TrySaveStandaloneOperationEdit()
    {
        return _schemeStepEditor.TrySaveStandaloneOperationEdit();
    }

    public void CancelStandaloneOperationEdit()
    {
        _schemeStepEditor.CancelStandaloneOperationEdit();
    }

    public WorkStepOperation? CreateEditedOperationSnapshot()
    {
        return _schemeStepEditor.CreateEditedOperationSnapshot();
    }

    public void ReplaceEditingReturnParameters(IEnumerable<WorkStepOperationParameter>? parameters)
    {
        EditingReturnParameters.Clear();
        foreach (WorkStepOperationParameter parameter in parameters ?? Enumerable.Empty<WorkStepOperationParameter>())
        {
            EditingReturnParameters.Add(parameter);
        }

        SelectedEditingReturnParameter = EditingReturnParameters.FirstOrDefault();
    }

    public void ClearEditingReturnParameters()
    {
        EditingReturnParameters.Clear();
        SelectedEditingReturnParameter = null;
    }

    public void SetActiveParameterCollections(
        string operationSummary,
        IEnumerable? inputParameterRows,
        IEnumerable? returnParameterRows)
    {
        ActiveParameterOperationSummary = operationSummary;
        ActiveInputParameterCollection = inputParameterRows;
        ActiveReturnParameterCollection = returnParameterRows;
    }

    public void ClearActiveParameterCollections()
    {
        ActiveParameterOperationSummary = string.Empty;
        ActiveInputParameterCollection = null;
        ActiveReturnParameterCollection = null;
    }

    private void RaisePageSummaryChanged()
    {
        OnPropertyChanged(nameof(SchemeCountText));
        OnPropertyChanged(nameof(SchemeStepCountText));
        OnPropertyChanged(nameof(SchemeStepOperationCountText));
        OnPropertyChanged(nameof(AreAllSchemeStepsStartupEnabled));
        OnPropertyChanged(nameof(SchemeWorkStepCollection));
    }

    /// <summary>
    /// 让共享步骤编辑器重新绑定到当前方案工步。
    /// </summary>
    private void BindSchemeStepEditor()
    {
        if (_schemeStepEditor.CloseOperationDrawerCommand.CanExecute(null))
        {
            _schemeStepEditor.CloseOperationDrawerCommand.Execute(null);
        }

        if (SelectedSchemeStep is null)
        {
            _schemeStepEditorHostWorkStep = null;
            TrackInlineOperation(null);
            _schemeStepEditor.SelectedOperation = null;
            _schemeStepEditor.SelectedWorkStep = null;
            RefreshInlineEditingOptions();
            return;
        }

        _schemeStepEditorHostWorkStep = new WorkStepProfile
        {
            StepName = SelectedSchemeStep.SchemeStepName,
            Steps = SelectedSchemeStep.Operations
        };

        _schemeStepEditor.SelectedWorkStep = _schemeStepEditorHostWorkStep;
        _schemeStepEditor.SelectedOperation = _schemeStepEditorHostWorkStep.Steps.FirstOrDefault();
        RefreshInlineEditingOptions();
    }

    #endregion

    #region 序列化配置

    private static readonly JsonSerializerOptions SchemePackageJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    #endregion

    #region 构造与初始化

    public SchemeConfigurationViewModel()
    {
        _schemeStepEditor.PropertyChanged += SchemeStepEditor_PropertyChanged;
        Schemes.CollectionChanged += Schemes_CollectionChanged;
        SchemesView = CollectionViewSource.GetDefaultView(Schemes);
        SchemesView.Filter = FilterSchemes;
        InitializeCommands();
        RefreshStationOperationMethodCollection();
        SelectedScheme = Schemes.FirstOrDefault();
        SetPageStatus(
            Schemes.Count == 0 ? "暂无方案配置，请点击新增。" : $"已加载 {Schemes.Count} 个方案。",
            NeutralBrush);
    }

    private void RefreshStationOperationMethodCollection()
    {
        string selectedOperationObject = SelectedStationOperationMethod?.OperationObject?.Trim() ?? string.Empty;
        string selectedProtocolName = SelectedStationOperationMethod?.ProtocolName?.Trim() ?? string.Empty;
        string selectedCommandName = SelectedStationOperationMethod?.CommandName?.Trim() ?? string.Empty;
        string selectedInvokeMethod = SelectedStationOperationMethod?.InvokeMethod?.Trim() ?? string.Empty;

        StationOperationMethodCollection.Clear();
        foreach (DataRowView rowView in _schemeStepEditor.OperationMethodTable.DefaultView.Cast<DataRowView>())
        {
            StationOperationMethodCollection.Add(new StationOperationMethodItem
            {
                Kind = GetOperationMethodRowValue(rowView, "Kind"),
                OperationType = GetOperationMethodRowValue(rowView, "OperationType"),
                OperationObject = GetOperationMethodRowValue(rowView, "OperationObject"),
                ProtocolName = GetOperationMethodRowValue(rowView, "ProtocolName"),
                CommandName = GetOperationMethodRowValue(rowView, "CommandName"),
                InvokeMethod = GetOperationMethodRowValue(rowView, "InvokeMethod"),
                Summary = GetOperationMethodRowValue(rowView, "Summary"),
                ParameterCount = GetOperationMethodRowIntValue(rowView, "ParameterCount")
            });
        }

        if (string.IsNullOrWhiteSpace(selectedInvokeMethod))
        {
            SelectedStationOperationMethod = StationOperationMethodCollection.FirstOrDefault();
            return;
        }

        StationOperationMethodItem? matchedItem = StationOperationMethodCollection.FirstOrDefault(item =>
            string.Equals(item.OperationObject, selectedOperationObject, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.ProtocolName, selectedProtocolName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.CommandName, selectedCommandName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.InvokeMethod, selectedInvokeMethod, StringComparison.OrdinalIgnoreCase));

        SelectedStationOperationMethod = matchedItem ?? StationOperationMethodCollection.FirstOrDefault();
    }

    private static string GetOperationMethodRowValue(DataRowView? rowView, string columnName)
    {
        if (rowView?.Row.Table.Columns.Contains(columnName) != true)
        {
            return string.Empty;
        }

        return rowView.Row[columnName]?.ToString()?.Trim() ?? string.Empty;
    }

    private static int GetOperationMethodRowIntValue(DataRowView? rowView, string columnName)
    {
        if (rowView?.Row.Table.Columns.Contains(columnName) != true)
        {
            return 0;
        }

        return rowView.Row[columnName] is int value ? value : 0;
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
        ImportSchemeCommand = new RelayCommand(_ => ImportScheme());
        ExportSchemeCommand = new RelayCommand(_ => ExportSelectedScheme(), _ => SelectedScheme is not null);
        AddWorkStepToSchemeCommand = new RelayCommand(_ => AddWorkStepToScheme(), _ => SelectedScheme is not null);
        RemoveWorkStepFromSchemeCommand = new RelayCommand(
            _ => RemoveSelectedSchemeStep(),
            _ => SelectedScheme is not null && SelectedSchemeStep is not null);
        UndoRemoveSchemeStepCommand = new RelayCommand(_ => UndoRemoveSchemeStep(), _ => CanUndoRemoveSchemeStep());
    }

    #endregion

    #region 方案配置命令

    /// <summary>
    /// 新增一个默认方案并立即选中。
    /// </summary>
    private void NewScheme()
    {
        if (!CanRunCreateOrCopyCommand())
        {
            return;
        }

        SchemeProfile scheme = CreateScheme(GenerateUniqueSchemeName("方案"));
        Schemes.Add(scheme);
        SelectCreatedScheme(scheme);
        SetPageStatus("已新增方案。", SuccessBrush);
    }

    /// <summary>
    /// 复制当前选中的方案及其工步。
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

        string deletedSchemeId = SelectedScheme.Id;
        int index = Schemes.IndexOf(SelectedScheme);

        Schemes.Remove(SelectedScheme);
        SelectedScheme = Schemes.Count == 0
            ? null
            : Schemes[Math.Clamp(index, 0, Schemes.Count - 1)];

        ClearRemovedSchemeStepUndo(deletedSchemeId);
        SetPageStatus("已删除方案，保存后生效。", WarningBrush);
    }

    /// <summary>
    /// 保存全部方案配置。
    /// </summary>
    private void SaveSchemes()
    {
        if (!ValidateSchemes(out string message))
        {
            SetPageStatus(message, WarningBrush);
            return;
        }

        BusinessConfigurationStore.SaveCatalog(_catalog);
        SetPageStatus($"已保存 {Schemes.Count} 个方案。", SuccessBrush);
    }

    /// <summary>
    /// 从本地文件导入方案配置。
    /// </summary>
    private void ImportScheme()
    {
        OpenFileDialog dialog = new()
        {
            Filter = "方案文件 (*.scheme.json)|*.scheme.json|JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*",
            DefaultExt = ".scheme.json"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            string json = File.ReadAllText(dialog.FileName);
            SchemeConfigurationPackage? package =
                JsonSerializer.Deserialize<SchemeConfigurationPackage>(json, SchemePackageJsonOptions);

            if (package?.Scheme is null)
            {
                SetPageStatus("导入失败：方案文件为空或格式无效。", WarningBrush);
                return;
            }

            ImportSchemePackage(package);
        }
        catch (Exception ex)
        {
            SetPageStatus($"导入方案失败：{ex.Message}", WarningBrush);
        }
    }

    /// <summary>
    /// 导出当前选中的方案。
    /// </summary>
    private void ExportSelectedScheme()
    {
        if (SelectedScheme is null)
        {
            SetPageStatus("请先选择要导出的方案。", WarningBrush);
            return;
        }

        SaveFileDialog dialog = new()
        {
            Filter = "方案文件 (*.scheme.json)|*.scheme.json|JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*",
            DefaultExt = ".scheme.json",
            FileName = $"{SanitizeFileName(SelectedScheme.SchemeName)}.scheme.json"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            SchemeConfigurationPackage package = CreateSchemePackage(SelectedScheme);
            string json = JsonSerializer.Serialize(package, SchemePackageJsonOptions);
            File.WriteAllText(dialog.FileName, json);
            SetPageStatus($"已导出方案：{dialog.FileName}", SuccessBrush);
        }
        catch (Exception ex)
        {
            SetPageStatus($"导出方案失败：{ex.Message}", WarningBrush);
        }
    }

    #endregion

    #region 方案工步命令

    /// <summary>
    /// 在当前方案中新增工步。
    /// </summary>
    private void AddWorkStepToScheme()
    {
        if (SelectedScheme is null)
        {
            return;
        }

        SchemeWorkStepItem schemeStep = CreateEmptySchemeStep(GenerateUniqueSchemeStepName("工步"));
        int insertIndex = SelectedSchemeStep is null
            ? SelectedScheme.Steps.Count
            : Math.Clamp(SelectedScheme.Steps.IndexOf(SelectedSchemeStep) + 1, 0, SelectedScheme.Steps.Count);

        SelectedScheme.Steps.Insert(insertIndex, schemeStep);
        SelectedSchemeStep = schemeStep;
        SetPageStatus($"已新增方案工步：{schemeStep.SchemeStepName}。", SuccessBrush);
    }

    /// <summary>
    /// 删除当前选中的方案工步，并写入撤回栈。
    /// </summary>
    private void RemoveSelectedSchemeStep()
    {
        if (SelectedScheme is null || SelectedSchemeStep is null)
        {
            return;
        }

        int index = SelectedScheme.Steps.IndexOf(SelectedSchemeStep);
        RememberRemovedSchemeStep(SelectedSchemeStep, index, SelectedScheme);
        SelectedScheme.Steps.Remove(SelectedSchemeStep);
        SelectedSchemeStep = SelectedScheme.Steps.Count == 0
            ? null
            : SelectedScheme.Steps[Math.Clamp(index, 0, SelectedScheme.Steps.Count - 1)];

        SetPageStatus("已删除方案工步。", WarningBrush);
    }

    /// <summary>
    /// 撤回当前方案最近一次删除的工步，可连续撤回。
    /// </summary>
    private void UndoRemoveSchemeStep()
    {
        if (SelectedScheme is null || !TryPopRemovedSchemeStepUndo(SelectedScheme, out RemovedSchemeStepUndoItem? undoItem))
        {
            return;
        }

        int insertIndex = Math.Clamp(undoItem.StepIndex, 0, SelectedScheme.Steps.Count);
        SchemeWorkStepItem restoredStep = undoItem.SchemeStep.Clone();
        restoredStep.Id = Guid.NewGuid().ToString("N");
        SelectedScheme.Steps.Insert(insertIndex, restoredStep);
        SelectedSchemeStep = restoredStep;

        SetPageStatus($"已撤回删除的工步：{restoredStep.SchemeStepName}。", SuccessBrush);
        RaiseCommandStatesChanged();
    }

    /// <summary>
    /// 调整方案工步顺序。
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
        SelectedSchemeStep = draggedSchemeStep;
        SetPageStatus("已调整工步顺序。", SuccessBrush);
        RaiseCommandStatesChanged();
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
                if (string.IsNullOrWhiteSpace(schemeStep.SchemeStepName))
                {
                    message = $"方案“{scheme.SchemeName}”存在未命名工步。";
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
                   Contains(step.SchemeStepName, keyword) ||
                   Contains(step.StepName, keyword) ||
                   step.Operations.Any(operation => Contains(operation.DisplayText, keyword)));
    }

    private static bool Contains(string? source, string keyword)
    {
        return source?.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    #endregion

    #region 导入导出辅助

    private SchemeConfigurationPackage CreateSchemePackage(SchemeProfile scheme)
    {
        return new SchemeConfigurationPackage
        {
            Scheme = scheme.Clone(),
            WorkSteps = new ObservableCollection<WorkStepProfile>(
                scheme.Steps.Select(step => step.ToWorkStepProfile()))
        };
    }

    /// <summary>
    /// 导入方案包，并在必要时补齐内嵌工步快照。
    /// </summary>
    private void ImportSchemePackage(SchemeConfigurationPackage package)
    {
        SchemeProfile scheme = package.Scheme!.Clone();
        scheme.Id = Guid.NewGuid().ToString("N");
        scheme.SchemeName = GenerateUniqueImportedSchemeName(scheme.SchemeName);

        foreach (SchemeWorkStepItem schemeStep in scheme.Steps)
        {
            schemeStep.Id = Guid.NewGuid().ToString("N");

            if (schemeStep.Operations.Count == 0)
            {
                WorkStepProfile? sourceWorkStep = FindPackageWorkStep(package, schemeStep);
                if (sourceWorkStep is null)
                {
                    SetPageStatus($"导入失败：工步“{schemeStep.SchemeStepName}”缺少步骤内容。", WarningBrush);
                    return;
                }

                schemeStep.Operations = new ObservableCollection<WorkStepOperation>(
                    sourceWorkStep.Steps.Select(operation => operation.Clone()));

                if (string.IsNullOrWhiteSpace(schemeStep.StepName))
                {
                    schemeStep.StepName = sourceWorkStep.StepName;
                }
            }

            if (string.IsNullOrWhiteSpace(schemeStep.StepName))
            {
                schemeStep.StepName = GenerateUniqueSchemeStepName("工步", scheme);
            }
        }

        Schemes.Add(scheme);
        SelectCreatedScheme(scheme);
        SetPageStatus($"已导入方案：{scheme.SchemeName}。", SuccessBrush);
    }

    private static WorkStepProfile? FindPackageWorkStep(SchemeConfigurationPackage package, SchemeWorkStepItem schemeStep)
    {
        IEnumerable<WorkStepProfile> packageWorkSteps = package.WorkSteps ?? new ObservableCollection<WorkStepProfile>();
        string operationSummary = BuildOperationSummary(schemeStep.Operations);

        return packageWorkSteps.FirstOrDefault(workStep =>
                   string.Equals(workStep.Id, schemeStep.WorkStepId, StringComparison.Ordinal) &&
                   MatchesSchemeStepSnapshot(workStep, schemeStep)) ??
               packageWorkSteps.FirstOrDefault(workStep =>
                   TextEquals(workStep.StepName, schemeStep.StepName) &&
                   TextEquals(workStep.OperationSummary, operationSummary)) ??
               (string.IsNullOrWhiteSpace(operationSummary)
                   ? packageWorkSteps.FirstOrDefault(workStep => TextEquals(workStep.StepName, schemeStep.StepName))
                   : null) ??
               (string.IsNullOrWhiteSpace(schemeStep.StepName)
                   ? packageWorkSteps.FirstOrDefault(workStep => TextEquals(workStep.OperationSummary, operationSummary))
                   : null);
    }

    private static bool MatchesSchemeStepSnapshot(WorkStepProfile workStep, SchemeWorkStepItem schemeStep)
    {
        if (!string.IsNullOrWhiteSpace(schemeStep.StepName) &&
            !string.IsNullOrWhiteSpace(workStep.StepName) &&
            !TextEquals(workStep.StepName, schemeStep.StepName))
        {
            return false;
        }

        string operationSummary = BuildOperationSummary(schemeStep.Operations);
        if (!string.IsNullOrWhiteSpace(operationSummary) &&
            !TextEquals(workStep.OperationSummary, operationSummary))
        {
            return false;
        }

        return true;
    }

    private static string BuildOperationSummary(IEnumerable<WorkStepOperation> operations)
    {
        List<string> items = operations
            .Where(operation => operation is not null)
            .Select(operation => operation.DisplayText)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Select(text => text.Trim())
            .ToList();

        return items.Count == 0 ? string.Empty : string.Join(" / ", items);
    }

    private static bool TextEquals(string? left, string? right)
    {
        return string.Equals(NormalizeText(left), NormalizeText(right), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeText(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }

    private static string SanitizeFileName(string fileName)
    {
        string safeName = string.IsNullOrWhiteSpace(fileName) ? "方案" : fileName.Trim();
        foreach (char invalidChar in Path.GetInvalidFileNameChars())
        {
            safeName = safeName.Replace(invalidChar, '_');
        }

        return safeName;
    }

    #endregion

    #region 工厂与命名

    private SchemeProfile CreateScheme(string schemeName)
    {
        return new SchemeProfile
        {
            SchemeName = schemeName
        };
    }

    private SchemeWorkStepItem CreateEmptySchemeStep(string schemeStepName)
    {
        return new SchemeWorkStepItem
        {
            StepName = schemeStepName,
            IsStartupEnabled = true,
            LastModifiedAt = DateTime.Now
        };
    }

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

    private void SelectCreatedScheme(SchemeProfile scheme)
    {
        SearchText = string.Empty;
        SchemesView.Refresh();
        SelectedScheme = scheme;
        SchemesView.MoveCurrentTo(scheme);
    }

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

    private string GenerateUniqueImportedSchemeName(string schemeName)
    {
        HashSet<string> existingNames = new(Schemes.Select(scheme => scheme.SchemeName), StringComparer.OrdinalIgnoreCase);
        string baseName = string.IsNullOrWhiteSpace(schemeName) ? "方案" : schemeName.Trim();
        string candidate = baseName;
        int index = 2;

        while (existingNames.Contains(candidate))
        {
            candidate = $"{baseName} {index}";
            index++;
        }

        return candidate;
    }

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

    private string GenerateUniqueSchemeStepName(string prefix, SchemeProfile? targetScheme = null)
    {
        SchemeProfile? scheme = targetScheme ?? SelectedScheme;
        HashSet<string> existingNames = new(
            scheme?.Steps.Select(step => step.SchemeStepName) ?? Enumerable.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);

        string baseName = string.IsNullOrWhiteSpace(prefix) ? "工步" : prefix.Trim();
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

    #region 删除撤回

    private bool CanUndoRemoveSchemeStep()
    {
        return SelectedScheme is not null &&
               _removedSchemeStepUndoItems.Any(item =>
                   string.Equals(item.SchemeId, SelectedScheme.Id, StringComparison.Ordinal));
    }

    private void RememberRemovedSchemeStep(SchemeWorkStepItem schemeStep, int index, SchemeProfile scheme)
    {
        _removedSchemeStepUndoItems.Add(new RemovedSchemeStepUndoItem
        {
            SchemeId = scheme.Id,
            StepIndex = Math.Max(0, index),
            SchemeStep = schemeStep.Clone()
        });

        RaiseCommandStatesChanged();
    }

    private bool TryPopRemovedSchemeStepUndo(SchemeProfile scheme, out RemovedSchemeStepUndoItem? undoItem)
    {
        for (int index = _removedSchemeStepUndoItems.Count - 1; index >= 0; index--)
        {
            RemovedSchemeStepUndoItem currentItem = _removedSchemeStepUndoItems[index];
            if (!string.Equals(currentItem.SchemeId, scheme.Id, StringComparison.Ordinal))
            {
                continue;
            }

            _removedSchemeStepUndoItems.RemoveAt(index);
            undoItem = currentItem;
            return true;
        }

        undoItem = null;
        return false;
    }

    private void ClearRemovedSchemeStepUndo(string? schemeId = null)
    {
        if (string.IsNullOrWhiteSpace(schemeId))
        {
            _removedSchemeStepUndoItems.Clear();
        }
        else
        {
            _removedSchemeStepUndoItems.RemoveAll(item =>
                string.Equals(item.SchemeId, schemeId, StringComparison.Ordinal));
        }

        RaiseCommandStatesChanged();
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
        RaiseCommandState(ImportSchemeCommand);
        RaiseCommandState(ExportSchemeCommand);
        RaiseCommandState(AddWorkStepToSchemeCommand);
        RaiseCommandState(RemoveWorkStepFromSchemeCommand);
        RaiseCommandState(UndoRemoveSchemeStepCommand);
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
