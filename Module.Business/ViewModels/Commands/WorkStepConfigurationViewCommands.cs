using ControlLibrary;
using Module.Business.Models;
using Module.Business.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Windows.Input;
using System.Windows.Data;
using System.Windows.Media;
using Shared.Infrastructure.Extensions;

namespace Module.Business.ViewModels;

/// <summary>
/// 工步配置界面命令实现。
/// </summary>
public sealed partial class WorkStepConfigurationViewModel
{
    private static readonly Regex ProtocolPlaceholderRegex =
        new Regex(@"\{\{\s*(?<name>[^{}\r\n]+?)\s*\}\}", RegexOptions.Compiled);

    private static readonly Regex SystemMethodSignatureRegex =
        new Regex(
            @"^\s*public\s+static\s+(?:async\s+)?(?<return>[A-Za-z_][\w\.<>,\[\]\?]*)\s+(?<name>[A-Za-z_]\w*(?:<[^>]+>)?)\s*\((?<parameters>.*)\)",
            RegexOptions.Compiled);

    #region 构造与初始化

    public WorkStepConfigurationViewModel()
    {
        WorkSteps.CollectionChanged += WorkSteps_CollectionChanged;
        EditingInvokeParameters.CollectionChanged += EditingInvokeParameters_CollectionChanged;
        WorkStepsView = CollectionViewSource.GetDefaultView(WorkSteps);
        WorkStepsView.Filter = FilterWorkSteps;
        RefreshProductOptions();
        InitializeCommands();
        SelectedProductName = ProductOptions.FirstOrDefault() ?? string.Empty;
        SelectFirstVisibleWorkStep();
        SetPageStatus(WorkSteps.Count == 0 ? "暂无工步配置，请点击新增。" : $"已读取 {WorkSteps.Count} 个工步", NeutralBrush);
    }

    /// <summary>
    /// 初始化页面命令，XAML 不绑定后台 Click 事件。
    /// </summary>
    private void InitializeCommands()
    {
        NewWorkStepCommand = new RelayCommand(_ => NewWorkStep());
        DuplicateWorkStepCommand = new RelayCommand(_ => DuplicateSelectedWorkStep(), _ => SelectedWorkStep is not null);
        DeleteWorkStepCommand = new RelayCommand(_ => DeleteSelectedWorkStep(), _ => SelectedWorkStep is not null);
        SaveWorkStepsCommand = new RelayCommand(_ => SaveWorkSteps());
        RefreshProductsCommand = new RelayCommand(_ => RefreshProducts());
        AddOperationCommand = new RelayCommand(_ => OpenOperationDrawerForNew(), _ => SelectedWorkStep is not null);
        CopyOperationCommand = new RelayCommand(_ => CopySelectedOperations(), _ => CanCopyOperations());
        PasteOperationCommand = new RelayCommand(_ => PasteCopiedOperations(), _ => CanPasteOperations());
        DeleteOperationCommand = new RelayCommand(_ => DeleteSelectedOperation(), _ => CanDeleteOperations());
        SaveOperationDrawerCommand = new RelayCommand(_ => SaveOperationDrawer(), _ => IsOperationDrawerOpen);
        CloseOperationDrawerCommand = new RelayCommand(_ => CloseOperationDrawer());
        RefreshOperationObjectsCommand = new RelayCommand(_ => RefreshOperationObjectOptions(updateStatus: true), _ => IsOperationDrawerOpen);
        AddInvokeParameterCommand = new RelayCommand(_ => AddInvokeParameter(), _ => IsOperationDrawerOpen && !IsLuaOperationSelected);
        DeleteInvokeParameterCommand = new RelayCommand(_ => DeleteSelectedInvokeParameter(), _ => IsOperationDrawerOpen && !IsLuaOperationSelected && SelectedEditingInvokeParameter is not null);
    }

    #endregion

    #region 工步命令方法

    /// <summary>
    /// 新增工步，默认沿用当前选中工步的产品名称。
    /// </summary>
    private void NewWorkStep()
    {
        if (!CanRunCreateOrCopyCommand())
        {
            return;
        }

        string productName = ResolveDefaultProductName();
        if (string.IsNullOrWhiteSpace(productName))
        {
            SetPageStatus("请先在产品配置界面新增产品，再配置工步。", WarningBrush);
            return;
        }

        WorkStepProfile workStep = CreateWorkStep(productName, GenerateUniqueStepName(productName, "工步"));
        WorkSteps.Add(workStep);
        SelectCreatedWorkStep(workStep);
        if (workStep.Steps.FirstOrDefault() is WorkStepOperation operation)
        {
            OpenOperationDrawerForEdit(operation);
        }

        SetPageStatus("已新增工步，编辑后点击保存。", SuccessBrush);
    }

    /// <summary>
    /// 复制当前选中工步及其步骤列表。
    /// </summary>
    private void DuplicateSelectedWorkStep()
    {
        if (!CanRunCreateOrCopyCommand())
        {
            return;
        }

        if (SelectedWorkStep is null)
        {
            return;
        }

        WorkStepProfile workStep = CreateCopyWorkStep(SelectedWorkStep);
        WorkSteps.Add(workStep);
        SelectCreatedWorkStep(workStep);
        SetPageStatus($"已复制工步：{workStep.StepName}。", SuccessBrush);
    }

    /// <summary>
    /// 删除当前选中工步。
    /// </summary>
    private void DeleteSelectedWorkStep()
    {
        if (SelectedWorkStep is null)
        {
            return;
        }

        WorkSteps.Remove(SelectedWorkStep);
        SelectFirstVisibleWorkStep();

        SetPageStatus("已删除工步，点击保存后同步到方案配置。", WarningBrush);
    }

    /// <summary>
    /// 校验并保存所有工步配置。
    /// </summary>
    private void SaveWorkSteps()
    {
        if (!ValidateWorkSteps(out string message))
        {
            SetPageStatus(message, WarningBrush);
            return;
        }

        BusinessConfigurationCatalog latestCatalog = BusinessConfigurationStore.LoadCatalog();
        _catalog.Products = latestCatalog.Products;
        RefreshProductOptions();

        BusinessConfigurationStore.SaveCatalog(_catalog);
        SetPageStatus($"已保存 {WorkSteps.Count} 个工步。", SuccessBrush);
    }

    /// <summary>
    /// 重新读取产品配置和工步列表。
    /// </summary>
    private void RefreshProducts()
    {
        BusinessConfigurationCatalog latestCatalog = BusinessConfigurationStore.LoadCatalog();
        string selectedWorkStepId = SelectedWorkStep?.Id ?? string.Empty;

        _catalog.Products = latestCatalog.Products;
        ReloadWorkSteps(latestCatalog.WorkSteps);
        RefreshProductOptions();
        WorkStepsView.Refresh();
        SelectVisibleWorkStep(selectedWorkStepId);
        RaisePageSummaryChanged();
        RaiseCommandStatesChanged();
        SetPageStatus("已刷新产品名称和工步列表。", SuccessBrush);
    }

    #endregion

    #region 步骤命令方法

    /// <summary>
    /// 打开抽屉，新建当前工步的操作步骤。
    /// </summary>
    private void OpenOperationDrawerForNew()
    {
        if (SelectedWorkStep is null)
        {
            return;
        }

        WorkStepOperation operation = new()
        {
            OperationObject = SystemOperationObjectName,
            InvokeMethod = string.Empty,
            ReturnValue = string.Empty,
            ShowDataToView = false,
            ViewDataName = string.Empty,
            ViewJudgeType = string.Empty,
            ViewJudgeCondition = string.Empty,
            DelayMilliseconds = 0,
            Remark = string.Empty
        };

        BeginOperationDrawer(operation, isNewOperation: true);
        SetPageStatus("正在新建步骤。", NeutralBrush);
    }

    /// <summary>
    /// 打开抽屉，编辑当前工步下的已有步骤。
    /// </summary>
    public void OpenOperationDrawerForEdit(WorkStepOperation operation)
    {
        if (SelectedWorkStep is null || !SelectedWorkStep.Steps.Contains(operation))
        {
            return;
        }

        SelectedOperation = operation;
        BeginOperationDrawer(operation, isNewOperation: false);
        SetPageStatus("正在编辑步骤。", NeutralBrush);
    }

    /// <summary>
    /// 将拖拽的步骤移动到目标步骤前后。
    /// </summary>
    public void MoveOperation(WorkStepOperation draggedOperation, WorkStepOperation targetOperation, bool insertAfter)
    {
        if (SelectedWorkStep is null)
        {
            return;
        }

        ObservableCollection<WorkStepOperation> steps = SelectedWorkStep.Steps;
        int oldIndex = steps.IndexOf(draggedOperation);
        int targetIndex = steps.IndexOf(targetOperation);
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
        SelectedOperation = draggedOperation;
        SetPageStatus("已调整步骤顺序。", SuccessBrush);
    }

    /// <summary>
    /// 保存抽屉中的步骤编辑内容。
    /// </summary>
    private void SaveOperationDrawer()
    {
        if (SelectedWorkStep is null || _drawerOperation is null)
        {
            CloseOperationDrawer();
            return;
        }

        if (string.IsNullOrWhiteSpace(EditingOperationObject))
        {
            SetPageStatus("操作对象不能为空。", WarningBrush);
            return;
        }

        if (IsProtocolCommandSelectionVisible && string.IsNullOrWhiteSpace(EditingProtocolName))
        {
            SetPageStatus("协议不能为空。", WarningBrush);
            return;
        }

        if (IsProtocolCommandSelectionVisible && string.IsNullOrWhiteSpace(EditingCommandName))
        {
            SetPageStatus("指令不能为空。", WarningBrush);
            return;
        }

        string invokeMethod = IsLuaOperationSelected
            ? LuaOperationObjectName
            : IsSystemOrJudgeOperationSelected
                ? EditingInvokeMethod
                : EditingCommandName;
        if (string.IsNullOrWhiteSpace(invokeMethod))
        {
            SetPageStatus("调用方法不能为空。", WarningBrush);
            return;
        }

        if (!IsLuaOperationSelected &&
            EditingShowDataToView &&
            string.IsNullOrWhiteSpace(EditingViewDataName))
        {
            SetPageStatus("勾选显示到界面时，数据名称不能为空。", WarningBrush);
            return;
        }

        if (!int.TryParse(EditingDelayMillisecondsText, out int delayMilliseconds) || delayMilliseconds < 0)
        {
            SetPageStatus("延时(ms)必须是大于等于 0 的整数。", WarningBrush);
            return;
        }

        _drawerOperation.OperationType = IsLuaOperationSelected
            ? LuaOperationObjectName
            : IsJudgeOperationSelected
                ? JudgeOperationObjectName
                : IsSystemOperationSelected
                    ? "\u7CFB\u7EDF"
                    : "\u8BBE\u5907";
        _drawerOperation.OperationObject = IsLuaOperationSelected ? LuaOperationObjectName : EditingOperationObject.Trim();
        _drawerOperation.ProtocolName = IsProtocolCommandSelectionVisible ? EditingProtocolName.Trim() : string.Empty;
        _drawerOperation.CommandName = IsProtocolCommandSelectionVisible ? EditingCommandName.Trim() : string.Empty;
        _drawerOperation.InvokeMethod = invokeMethod.Trim();
        _drawerOperation.ReturnValue = IsLuaOperationSelected ? string.Empty : EditingReturnValue.Trim();
        _drawerOperation.ShowDataToView = !IsLuaOperationSelected && EditingShowDataToView;
        _drawerOperation.ViewDataName = IsLuaOperationSelected ? string.Empty : EditingViewDataName.Trim();
        _drawerOperation.ViewJudgeType = IsLuaOperationSelected ? string.Empty : EditingViewJudgeType.Trim();
        _drawerOperation.ViewJudgeCondition = IsLuaOperationSelected ? string.Empty : EditingViewJudgeCondition.Trim();
        _drawerOperation.LuaScript = IsLuaOperationSelected ? EditingLuaScript : string.Empty;
        _drawerOperation.DelayMilliseconds = delayMilliseconds;
        _drawerOperation.Remark = EditingRemark.Trim();
        if (IsLuaOperationSelected)
        {
            _drawerOperation.Parameters = new ObservableCollection<WorkStepOperationParameter>();
        }
        else
        {
            NormalizeInvokeParameterSequences();
            SortInvokeParametersBySequence();
            _drawerOperation.Parameters = new ObservableCollection<WorkStepOperationParameter>(
                EditingInvokeParameters
                    .OrderBy(parameter => parameter.Sequence)
                    .Select(parameter => parameter.Clone()));
        }

        if (_isNewOperationInDrawer)
        {
            SelectedWorkStep.Steps.Add(_drawerOperation);
        }

        SelectedOperation = _drawerOperation;
        bool savedNewOperation = _isNewOperationInDrawer;
        CloseOperationDrawer();
        SetPageStatus(savedNewOperation ? "已新增步骤。" : "已更新步骤。", SuccessBrush);
    }

    /// <summary>
    /// 关闭步骤编辑抽屉，不提交当前编辑缓存。
    /// </summary>
    private void CloseOperationDrawer()
    {
        IsOperationDrawerOpen = false;
        _drawerOperation = null;
        _isNewOperationInDrawer = false;
        EditingInvokeParameters.Clear();
        EditingInvokeMethodRemark = string.Empty;
        EditingShowDataToView = false;
        EditingViewDataName = string.Empty;
        EditingViewJudgeType = string.Empty;
        EditingViewJudgeCondition = string.Empty;
        SelectedEditingInvokeParameter = null;
        OnPropertyChanged(nameof(OperationDrawerTitle));
    }

    /// <summary>
    /// 删除当前选中的操作步骤。
    /// </summary>
    private void DeleteSelectedOperation()
    {
        if (SelectedWorkStep is null)
        {
            return;
        }

        ObservableCollection<WorkStepOperation> steps = SelectedWorkStep.Steps;
        List<WorkStepOperation> operationsToDelete = GetCheckedOperations(steps);
        if (operationsToDelete.Count == 0 && SelectedOperation is not null)
        {
            operationsToDelete.Add(SelectedOperation);
        }

        if (operationsToDelete.Count == 0)
        {
            return;
        }

        int targetIndex = operationsToDelete
            .Select(steps.IndexOf)
            .Where(index => index >= 0)
            .DefaultIfEmpty(-1)
            .Min();

        WorkStepOperation? operationToKeepSelected =
            SelectedOperation is not null && !operationsToDelete.Contains(SelectedOperation)
                ? SelectedOperation
                : null;

        if (_drawerOperation is not null &&
            operationsToDelete.Any(operation => ReferenceEquals(operation, _drawerOperation)))
        {
            CloseOperationDrawer();
        }

        foreach (WorkStepOperation operation in operationsToDelete
                     .Where(operation => steps.Contains(operation))
                     .OrderByDescending(operation => steps.IndexOf(operation))
                     .ToList())
        {
            steps.Remove(operation);
        }

        if (operationToKeepSelected is not null && steps.Contains(operationToKeepSelected))
        {
            SelectedOperation = operationToKeepSelected;
        }
        else
        {
            SelectedOperation = steps.Count == 0 || targetIndex < 0
                ? null
                : steps[Math.Clamp(targetIndex, 0, steps.Count - 1)];
        }

        SetPageStatus(operationsToDelete.Count == 1
            ? "已删除步骤。"
            : $"已删除 {operationsToDelete.Count} 个步骤。", WarningBrush);
    }

    private bool CanCopyOperations()
    {
        return SelectedWorkStep is not null && GetOperationsForClipboard().Count > 0;
    }

    private bool CanPasteOperations()
    {
        return SelectedWorkStep is not null && _copiedOperations.Count > 0;
    }

    private bool CanDeleteOperations()
    {
        return SelectedWorkStep is not null &&
               (SelectedOperation is not null || SelectedWorkStep.Steps.Any(operation => operation.IsChecked));
    }

    private void CopySelectedOperations()
    {
        List<WorkStepOperation> operationsToCopy = GetOperationsForClipboard();
        if (operationsToCopy.Count == 0)
        {
            return;
        }

        _copiedOperations.Clear();
        _copiedOperations.AddRange(operationsToCopy.Select(CreateClipboardOperation));
        RaiseCommandStatesChanged();

        SetPageStatus(operationsToCopy.Count == 1
            ? "已复制 1 个步骤。"
            : $"已复制 {operationsToCopy.Count} 个步骤。", SuccessBrush);
    }

    private void PasteCopiedOperations()
    {
        if (SelectedWorkStep is null || _copiedOperations.Count == 0)
        {
            return;
        }

        ObservableCollection<WorkStepOperation> steps = SelectedWorkStep.Steps;
        int insertIndex = ResolvePasteInsertIndex(steps);
        ClearCheckedOperations(steps);

        List<WorkStepOperation> operationsToPaste = _copiedOperations
            .Select(CreateClipboardOperation)
            .ToList();

        foreach (WorkStepOperation operation in operationsToPaste)
        {
            steps.Insert(insertIndex, operation);
            insertIndex++;
        }

        SelectedOperation = operationsToPaste.FirstOrDefault();
        SetPageStatus(operationsToPaste.Count == 1
            ? "已粘贴 1 个步骤。"
            : $"已粘贴 {operationsToPaste.Count} 个步骤。", SuccessBrush);
    }

    private List<WorkStepOperation> GetCheckedOperations(ObservableCollection<WorkStepOperation> steps)
    {
        return steps
            .Where(operation => operation.IsChecked)
            .ToList();
    }

    private List<WorkStepOperation> GetOperationsForClipboard()
    {
        if (SelectedWorkStep is null)
        {
            return new List<WorkStepOperation>();
        }

        List<WorkStepOperation> checkedOperations = GetCheckedOperations(SelectedWorkStep.Steps);
        if (checkedOperations.Count > 0)
        {
            return checkedOperations;
        }

        return SelectedOperation is null
            ? new List<WorkStepOperation>()
            : new List<WorkStepOperation> { SelectedOperation };
    }

    private int ResolvePasteInsertIndex(ObservableCollection<WorkStepOperation> steps)
    {
        List<WorkStepOperation> checkedOperations = GetCheckedOperations(steps);
        if (checkedOperations.Count > 0)
        {
            int lastCheckedIndex = checkedOperations
                .Select(steps.IndexOf)
                .DefaultIfEmpty(-1)
                .Max();
            if (lastCheckedIndex >= 0)
            {
                return Math.Min(lastCheckedIndex + 1, steps.Count);
            }
        }

        if (SelectedOperation is not null)
        {
            int selectedIndex = steps.IndexOf(SelectedOperation);
            if (selectedIndex >= 0)
            {
                return Math.Min(selectedIndex + 1, steps.Count);
            }
        }

        return steps.Count;
    }

    private void ClearCheckedOperations(ObservableCollection<WorkStepOperation> steps)
    {
        foreach (WorkStepOperation operation in steps.Where(item => item.IsChecked).ToList())
        {
            operation.IsChecked = false;
        }
    }

    private WorkStepOperation CreateClipboardOperation(WorkStepOperation source)
    {
        WorkStepOperation operation = source.Clone();
        operation.Id = Guid.NewGuid().ToString("N");
        operation.IsChecked = false;
        operation.Parameters = new ObservableCollection<WorkStepOperationParameter>(
            operation.Parameters.Select(parameter =>
            {
                parameter.Id = Guid.NewGuid().ToString("N");
                return parameter;
            }));

        return operation;
    }

    private void BeginOperationDrawer(WorkStepOperation operation, bool isNewOperation)
    {
        _drawerOperation = operation;
        _isNewOperationInDrawer = isNewOperation;
        _isInitializingOperationDrawer = true;
        try
        {
            string operationObject = ResolveOperationObjectForEditing(operation);
            if (_isDecisionOperationMode &&
                !IsJudgeOperationObject(operationObject) &&
                string.IsNullOrWhiteSpace(operation.OperationObject))
            {
                operationObject = JudgeOperationObjectName;
            }

            EnsureOperationObjectOption(operationObject);
            EditingOperationObject = operationObject;
            EditingProtocolName = operation.ProtocolName;
            EnsureProtocolOption(EditingProtocolName);
            EditingCommandName = string.IsNullOrWhiteSpace(operation.CommandName)
                ? operation.InvokeMethod
                : operation.CommandName;
            EnsureCommandOption(EditingCommandName);
            EditingInvokeMethod = IsSystemOrJudgeOperationSelected ? operation.InvokeMethod : EditingCommandName;
            RefreshProtocolOptions(updateStatus: false);
            RefreshInvokeMethodOptions(updateStatus: false);
            EditingReturnValue = operation.ReturnValue;
            EditingShowDataToView = operation.ShowDataToView;
            EditingViewDataName = operation.ViewDataName;
            EditingViewJudgeType = operation.ViewJudgeType;
            EditingViewJudgeCondition = operation.ViewJudgeCondition;
            EditingLuaScript = operation.LuaScript;
            EditingDelayMillisecondsText = operation.DelayMilliseconds.ToString();
            EditingRemark = operation.Remark;
            EditingInvokeParameters.Clear();
            foreach (WorkStepOperationParameter parameter in IsLuaOperationSelected
                         ? Enumerable.Empty<WorkStepOperationParameter>()
                         : operation.Parameters.Select(parameter => parameter.Clone()))
            {
                EditingInvokeParameters.Add(parameter);
            }
        }
        finally
        {
            _isInitializingOperationDrawer = false;
        }

        NormalizeInvokeParameterSequences();
        SortInvokeParametersBySequence();

        if (IsLuaOperationSelected)
        {
            EditingProtocolName = string.Empty;
            EditingCommandName = string.Empty;
            EditingInvokeMethod = LuaOperationObjectName;
            EditingInvokeMethodRemark = string.Empty;
            EditingReturnValue = string.Empty;
            EditingShowDataToView = false;
            EditingViewDataName = string.Empty;
            EditingViewJudgeType = string.Empty;
            EditingViewJudgeCondition = string.Empty;
            EditingInvokeParameters.Clear();
        }
        else if (IsJudgeOperationSelected)
        {
            EditingProtocolName = string.Empty;
            EditingCommandName = string.Empty;
            SyncJudgeInvokeMethodRemarkFromMethod();
            if (EditingInvokeParameters.Count == 0)
            {
                RefreshInvokeParametersFromSelectedJudgeMethod(clearWhenNoMetadata: false);
            }
        }
        else if (IsSystemOperationSelected)
        {
            SyncSystemInvokeMethodRemarkFromMethod();
            if (EditingInvokeParameters.Count == 0)
            {
                RefreshInvokeParametersFromSelectedSystemMethod(clearWhenNoMetadata: false);
            }
        }
        else if (EditingInvokeParameters.Count == 0)
        {
            RefreshInvokeParametersFromSelectedCommand();
        }

        SelectedEditingInvokeParameter = EditingInvokeParameters.FirstOrDefault();
        OnPropertyChanged(nameof(OperationDrawerTitle));
        IsOperationDrawerOpen = true;
    }

    private void AddInvokeParameter()
    {
        WorkStepOperationParameter parameter = new()
        {
            Sequence = GetNextInvokeParameterSequence(),
            Name = ParameterTypeOptions.FirstOrDefault() ?? "设置值",
            Value = string.Empty,
            Remark = string.Empty
        };

        EditingInvokeParameters.Add(parameter);
        SortInvokeParametersBySequence();
        SelectedEditingInvokeParameter = parameter;
        SetPageStatus("已新增调用方法参数。", SuccessBrush);
    }

    private void DeleteSelectedInvokeParameter()
    {
        if (SelectedEditingInvokeParameter is null)
        {
            return;
        }

        int index = EditingInvokeParameters.IndexOf(SelectedEditingInvokeParameter);
        EditingInvokeParameters.Remove(SelectedEditingInvokeParameter);
        SelectedEditingInvokeParameter = EditingInvokeParameters.Count == 0
            ? null
            : EditingInvokeParameters[Math.Clamp(index, 0, EditingInvokeParameters.Count - 1)];
        SetPageStatus("已删除调用方法参数。", WarningBrush);
    }

    private void EditingInvokeParameters_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Move)
        {
            return;
        }

        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (WorkStepOperationParameter parameter in _trackedEditingInvokeParameters.ToList())
            {
                parameter.PropertyChanged -= EditingInvokeParameter_PropertyChanged;
            }

            _trackedEditingInvokeParameters.Clear();
        }

        if (e.NewItems is not null)
        {
            foreach (WorkStepOperationParameter parameter in e.NewItems.OfType<WorkStepOperationParameter>())
            {
                if (_trackedEditingInvokeParameters.Add(parameter))
                {
                    parameter.PropertyChanged += EditingInvokeParameter_PropertyChanged;
                }

                UpdateParameterValueOptions(parameter);
            }
        }

        if (e.OldItems is not null)
        {
            foreach (WorkStepOperationParameter parameter in e.OldItems.OfType<WorkStepOperationParameter>())
            {
                if (_trackedEditingInvokeParameters.Remove(parameter))
                {
                    parameter.PropertyChanged -= EditingInvokeParameter_PropertyChanged;
                }
            }
        }
    }

    private void EditingInvokeParameter_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not WorkStepOperationParameter parameter)
        {
            return;
        }

        if (e.PropertyName is nameof(WorkStepOperationParameter.Name) or nameof(WorkStepOperationParameter.Type))
        {
            UpdateParameterValueOptions(parameter);
        }

        if (e.PropertyName == nameof(WorkStepOperationParameter.Sequence))
        {
            SortInvokeParametersBySequence();
        }
    }

    #endregion

    #region 工具方法

    private void NormalizeInvokeParameterSequences()
    {
        bool wasSorting = _isSortingInvokeParameters;
        _isSortingInvokeParameters = true;
        try
        {
            HashSet<int> usedSequences = new();
            int nextSequence = 1;
            foreach (WorkStepOperationParameter parameter in EditingInvokeParameters)
            {
                if (parameter.Sequence <= 0 || !usedSequences.Add(parameter.Sequence))
                {
                    while (usedSequences.Contains(nextSequence))
                    {
                        nextSequence++;
                    }

                    parameter.Sequence = nextSequence;
                    usedSequences.Add(parameter.Sequence);
                }

                nextSequence = Math.Max(nextSequence, parameter.Sequence + 1);
            }
        }
        finally
        {
            _isSortingInvokeParameters = wasSorting;
        }
    }

    private int GetNextInvokeParameterSequence()
    {
        return EditingInvokeParameters.Count == 0
            ? 1
            : EditingInvokeParameters.Max(parameter => parameter.Sequence) + 1;
    }

    private void SortInvokeParametersBySequence()
    {
        if (_isSortingInvokeParameters || EditingInvokeParameters.Count < 2)
        {
            return;
        }

        _isSortingInvokeParameters = true;
        try
        {
            List<WorkStepOperationParameter> orderedParameters = EditingInvokeParameters
                .Select((parameter, index) => new { Parameter = parameter, Index = index })
                .OrderBy(item => item.Parameter.Sequence)
                .ThenBy(item => item.Index)
                .Select(item => item.Parameter)
                .ToList();

            for (int targetIndex = 0; targetIndex < orderedParameters.Count; targetIndex++)
            {
                WorkStepOperationParameter parameter = orderedParameters[targetIndex];
                int currentIndex = EditingInvokeParameters.IndexOf(parameter);
                if (currentIndex >= 0 && currentIndex != targetIndex)
                {
                    EditingInvokeParameters.Move(currentIndex, targetIndex);
                }
            }
        }
        finally
        {
            _isSortingInvokeParameters = false;
        }
    }

    private void RefreshParameterValueOptions()
    {
        foreach (WorkStepOperationParameter parameter in EditingInvokeParameters)
        {
            UpdateParameterValueOptions(parameter);
        }
    }

    private void UpdateParameterValueOptions(WorkStepOperationParameter parameter)
    {
        ReplaceStringOptions(parameter.ValueOptions, BuildParameterValueOptions(parameter.Type));
    }

    private IEnumerable<string> BuildParameterValueOptions(string parameterType)
    {
        string normalizedType = parameterType?.Trim() ?? string.Empty;
        return normalizedType switch
        {
            "返回值" => BuildReturnValueOptions(),
            "产品值" => BuildProductValueOptions(),
            _ => Enumerable.Empty<string>()
        };
    }

    private IEnumerable<string> BuildReturnValueOptions()
    {
        IEnumerable<string> savedReturnValues = SelectedWorkStep?.Steps
            .Select(step => step.ReturnValue)
            .Where(value => !string.IsNullOrWhiteSpace(value)) ?? Enumerable.Empty<string>();

        IEnumerable<string> editingReturnValues = string.IsNullOrWhiteSpace(EditingReturnValue)
            ? Enumerable.Empty<string>()
            : new[] { EditingReturnValue };

        return savedReturnValues
            .Concat(ExternalReturnValueOptions)
            .Concat(editingReturnValues)
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase);
    }

    private IEnumerable<string> BuildProductValueOptions()
    {
        return _catalog.Products
            .Where(product => string.Equals(product.ProductName, SelectedProductName, StringComparison.OrdinalIgnoreCase))
            .SelectMany(product => product.KeyValues)
            .Select(item => item.Key)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => key.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase);
    }

    private static void ReplaceStringOptions(ObservableCollection<string> target, IEnumerable<string> source)
    {
        List<string> options = source
            .Where(option => !string.IsNullOrWhiteSpace(option))
            .Select(option => option.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
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

    private WorkStepProfile CreateWorkStep(string productName, string stepName)
    {
        WorkStepProfile workStep = new()
        {
            ProductName = productName,
            StepName = stepName,
            LastModifiedAt = DateTime.Now
        };

        workStep.Steps.Add(new WorkStepOperation
        {
            OperationObject = SystemOperationObjectName,
            InvokeMethod = "等待",
            ReturnValue = string.Empty,
            ShowDataToView = false,
            ViewDataName = string.Empty,
            ViewJudgeType = string.Empty,
            ViewJudgeCondition = string.Empty,
            DelayMilliseconds = 0,
            Remark = string.Empty
        });

        return workStep;
    }

    private WorkStepProfile CreateCopyWorkStep(WorkStepProfile source)
    {
        return new WorkStepProfile
        {
            Id = Guid.NewGuid().ToString("N"),
            ProductName = source.ProductName,
            StepName = GenerateCopyStepName(source.ProductName, source.StepName),
            LastModifiedAt = DateTime.Now,
            Steps = new ObservableCollection<WorkStepOperation>(
                source.Steps.Select(operation => new WorkStepOperation
                {
                    Id = Guid.NewGuid().ToString("N"),
                    OperationType = operation.OperationType,
                    OperationObject = operation.OperationObject,
                    ProtocolName = operation.ProtocolName,
                    CommandName = operation.CommandName,
                    InvokeMethod = operation.InvokeMethod,
                    ReturnValue = operation.ReturnValue,
                    ShowDataToView = operation.ShowDataToView,
                    ViewDataName = operation.ViewDataName,
                    ViewJudgeType = operation.ViewJudgeType,
                    ViewJudgeCondition = operation.ViewJudgeCondition,
                    LuaScript = operation.LuaScript,
                    DelayMilliseconds = operation.DelayMilliseconds,
                    Remark = operation.Remark,
                    Parameters = new ObservableCollection<WorkStepOperationParameter>(
                        operation.Parameters.Select(parameter => new WorkStepOperationParameter
                        {
                            Id = Guid.NewGuid().ToString("N"),
                            Sequence = parameter.Sequence,
                            Name = parameter.Name,
                            ParameterName = parameter.ParameterName,
                            Value = parameter.Value,
                            Remark = parameter.Remark
                        }))
                }))
        };
    }

    private void ReloadWorkSteps(ObservableCollection<WorkStepProfile> latestWorkSteps)
    {
        WorkSteps.CollectionChanged -= WorkSteps_CollectionChanged;
        try
        {
            WorkSteps.Clear();
            foreach (WorkStepProfile workStep in latestWorkSteps)
            {
                WorkSteps.Add(workStep);
            }
        }
        finally
        {
            WorkSteps.CollectionChanged += WorkSteps_CollectionChanged;
        }
    }

    private void SelectCreatedWorkStep(WorkStepProfile workStep)
    {
        SearchText = string.Empty;
        SelectedProductName = workStep.ProductName;
        WorkStepsView.Refresh();
        SelectedWorkStep = workStep;
        WorkStepsView.MoveCurrentTo(workStep);
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

    private void RefreshProductOptions()
    {
        ProductOptions.Clear();

        foreach (string productName in _catalog.Products
                     .Select(product => product.ProductName)
                     .Where(name => !string.IsNullOrWhiteSpace(name))
                     .Select(name => name.Trim())
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(name => name))
        {
            ProductOptions.Add(productName);
        }

        if (string.IsNullOrWhiteSpace(SelectedProductName) ||
            !ProductOptions.Any(name => string.Equals(name, SelectedProductName, StringComparison.OrdinalIgnoreCase)))
        {
            SelectedProductName = ProductOptions.FirstOrDefault() ?? string.Empty;
        }
    }

    private string ResolveDefaultProductName()
    {
        if (!string.IsNullOrWhiteSpace(SelectedProductName) &&
            ProductOptions.Any(name => string.Equals(name, SelectedProductName, StringComparison.OrdinalIgnoreCase)))
        {
            return SelectedProductName;
        }

        return ProductOptions.FirstOrDefault() ?? string.Empty;
    }

    private string GenerateUniqueStepName(string productName, string prefix)
    {
        HashSet<string> existingNames = new(
            WorkSteps
                .Where(step => string.Equals(step.ProductName, productName, StringComparison.OrdinalIgnoreCase))
                .Select(step => step.StepName),
            StringComparer.OrdinalIgnoreCase);

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

    private string GenerateCopyStepName(string productName, string baseName)
    {
        HashSet<string> existingNames = new(
            WorkSteps
                .Where(step => string.Equals(step.ProductName, productName, StringComparison.OrdinalIgnoreCase))
                .Select(step => step.StepName),
            StringComparer.OrdinalIgnoreCase);

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

    private bool FilterWorkSteps(object item)
    {
        if (item is not WorkStepProfile workStep)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(SelectedProductName))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(SelectedProductName) &&
            !string.Equals(workStep.ProductName, SelectedProductName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        string keyword = SearchText.Trim();
        return Contains(workStep.ProductName, keyword) ||
               Contains(workStep.StepName, keyword) ||
               Contains(workStep.OperationSummary, keyword) ||
               Contains(workStep.LastModifiedText, keyword);
    }

    private static bool Contains(string? source, string keyword)
    {
        return source?.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private bool ValidateWorkSteps(out string message)
    {
        if (WorkSteps.Count == 0)
        {
            message = "请至少新增一个工步。";
            return false;
        }

        HashSet<string> productStepKeys = new(StringComparer.OrdinalIgnoreCase);
        foreach (WorkStepProfile workStep in WorkSteps)
        {
            if (string.IsNullOrWhiteSpace(workStep.ProductName))
            {
                message = "产品名称不能为空。";
                return false;
            }

            if (string.IsNullOrWhiteSpace(workStep.StepName))
            {
                message = "工步名称不能为空。";
                return false;
            }

            string key = $"{workStep.ProductName.Trim()}::{workStep.StepName.Trim()}";
            if (!productStepKeys.Add(key))
            {
                message = $"同一产品下工步名称不能重复：{workStep.ProductName} / {workStep.StepName}";
                return false;
            }

            foreach (WorkStepOperation operation in workStep.Steps)
            {
                if (string.IsNullOrWhiteSpace(operation.OperationObject))
                {
                    message = $"工步“{workStep.StepName}”的操作对象不能为空。";
                    return false;
                }

                if (!IsLuaOperationObject(operation.OperationObject) &&
                    string.IsNullOrWhiteSpace(operation.InvokeMethod))
                {
                    message = $"工步“{workStep.StepName}”的调用方法不能为空。";
                    return false;
                }

                if (!IsSystemOperationObject(operation.OperationObject) &&
                    !IsLuaOperationObject(operation.OperationObject) &&
                    (string.IsNullOrWhiteSpace(operation.ProtocolName) ||
                     string.IsNullOrWhiteSpace(operation.CommandName)))
                {
                    message = $"工步“{workStep.StepName}”的设备步骤需要选择协议和指令。";
                    return false;
                }

                if (operation.DelayMilliseconds < 0)
                {
                    message = $"工步“{workStep.StepName}”的步骤延时不能小于 0。";
                    return false;
                }
            }
        }

        message = string.Empty;
        return true;
    }

    private void WorkSteps_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RaisePageSummaryChanged();
        RefreshProductOptions();
        WorkStepsView.Refresh();
        SelectFirstVisibleWorkStep();
        RaiseCommandStatesChanged();
    }

    private void SelectFirstVisibleWorkStep()
    {
        SelectVisibleWorkStep(SelectedWorkStep?.Id);
    }

    private void SelectVisibleWorkStep(string? preferredWorkStepId)
    {
        WorkStepProfile? preferredWorkStep = WorkStepsView
            .OfType<WorkStepProfile>()
            .FirstOrDefault(workStep =>
                !string.IsNullOrWhiteSpace(preferredWorkStepId) &&
                string.Equals(workStep.Id, preferredWorkStepId, StringComparison.Ordinal));

        if (preferredWorkStep is not null)
        {
            SelectedWorkStep = preferredWorkStep;
            WorkStepsView.MoveCurrentTo(preferredWorkStep);
            return;
        }

        SelectedWorkStep = WorkStepsView.OfType<WorkStepProfile>().FirstOrDefault();
        if (SelectedWorkStep is not null)
        {
            WorkStepsView.MoveCurrentTo(SelectedWorkStep);
        }
    }

    private void SetPageStatus(string text, Brush brush)
    {
        PageStatusText = text;
        PageStatusBrush = brush;
    }

    private void RefreshOperationObjectOptions(bool updateStatus)
    {
        string previousSelection = EditingOperationObject;
        OperationObjectOptions.Clear();

        if (_isDecisionOperationMode)
        {
            OperationObjectOptions.Add(JudgeOperationObjectName);
        }
        else
        {
            OperationObjectOptions.Add(SystemOperationObjectName);
            OperationObjectOptions.Add(LuaOperationObjectName);
            foreach (string option in LoadDeviceOperationObjectOptions()
                         .Where(option => !string.IsNullOrWhiteSpace(option))
                         .Select(option => option.Trim())
                         .Where(option => !IsSystemOperationObject(option))
                         .Where(option => !IsLuaOperationObject(option))
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .OrderBy(option => option, StringComparer.OrdinalIgnoreCase))
            {
                OperationObjectOptions.Add(option);
            }
        }

        if (!string.IsNullOrWhiteSpace(previousSelection) &&
            OperationObjectOptions.Any(option => string.Equals(option, previousSelection, StringComparison.OrdinalIgnoreCase)))
        {
            EditingOperationObject = previousSelection;
        }
        else if (_isDecisionOperationMode)
        {
            EditingOperationObject = JudgeOperationObjectName;
        }
        else
        {
            EditingOperationObject = SystemOperationObjectName;
        }

        RefreshProtocolOptions(updateStatus: false);
        RefreshInvokeMethodOptions(updateStatus: false);

        if (updateStatus)
        {
            SetPageStatus("已刷新操作对象。", SuccessBrush);
        }
    }

    private void RefreshProtocolOptions(bool updateStatus)
    {
        string previousSelection = EditingProtocolName;
        ProtocolOptions.Clear();

        if (IsSystemOrJudgeOperationSelected || IsLuaOperationSelected)
        {
            EditingProtocolName = string.Empty;
            RefreshCommandOptions(updateStatus: false);
            return;
        }

        foreach (string option in LoadProtocolOptions())
        {
            ProtocolOptions.Add(option);
        }

        if (!string.IsNullOrWhiteSpace(previousSelection) &&
            ProtocolOptions.Any(option => string.Equals(option, previousSelection, StringComparison.OrdinalIgnoreCase)))
        {
            EditingProtocolName = previousSelection;
        }
        else
        {
            EditingProtocolName = ProtocolOptions.FirstOrDefault() ?? string.Empty;
        }

        RefreshCommandOptions(updateStatus: false);

        if (updateStatus)
        {
            SetPageStatus("已刷新协议列表。", SuccessBrush);
        }
    }

    private void RefreshCommandOptions(bool updateStatus)
    {
        string previousSelection = EditingCommandName;
        CommandOptions.Clear();

        if (IsSystemOrJudgeOperationSelected || IsLuaOperationSelected || string.IsNullOrWhiteSpace(EditingProtocolName))
        {
            EditingCommandName = string.Empty;
            return;
        }

        foreach (string option in LoadProtocolCommandOptions(EditingProtocolName))
        {
            CommandOptions.Add(option);
        }

        if (!string.IsNullOrWhiteSpace(previousSelection) &&
            CommandOptions.Any(option => string.Equals(option, previousSelection, StringComparison.OrdinalIgnoreCase)))
        {
            EditingCommandName = previousSelection;
        }
        else
        {
            EditingCommandName = CommandOptions.FirstOrDefault() ?? string.Empty;
        }

        EditingInvokeMethod = EditingCommandName;
        RefreshInvokeParametersFromSelectedCommand();

        if (updateStatus)
        {
            SetPageStatus($"已按协议“{EditingProtocolName}”刷新指令。", SuccessBrush);
        }
    }

    private void RefreshInvokeParametersFromSelectedCommand()
    {
        if (IsSystemOrJudgeOperationSelected || IsLuaOperationSelected)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(EditingProtocolName) ||
            string.IsNullOrWhiteSpace(EditingCommandName))
        {
            EditingInvokeParameters.Clear();
            SelectedEditingInvokeParameter = null;
            return;
        }

        IReadOnlyList<ProtocolPlaceholderSelectionItem> placeholders =
            LoadProtocolCommandPlaceholders(EditingProtocolName, EditingCommandName);

        Dictionary<string, WorkStepOperationParameter> existingByPlaceholder = EditingInvokeParameters
            .Where(parameter => !string.IsNullOrWhiteSpace(parameter.Description))
            .GroupBy(parameter => parameter.Description.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        string? previousSelectedId = SelectedEditingInvokeParameter?.Id;
        EditingInvokeParameters.Clear();
        int sequence = 1;
        foreach (ProtocolPlaceholderSelectionItem placeholder in placeholders)
        {
            if (string.IsNullOrWhiteSpace(placeholder.Name))
            {
                continue;
            }

            WorkStepOperationParameter parameter;
            if (existingByPlaceholder.TryGetValue(placeholder.Name, out WorkStepOperationParameter? existing))
            {
                parameter = existing.Clone();
                parameter.ParameterName = placeholder.Name;
                parameter.Description = placeholder.Name;
                if (parameter.Sequence <= 0)
                {
                    parameter.Sequence = sequence;
                }
            }
            else
            {
                parameter = new WorkStepOperationParameter
                {
                    Sequence = sequence,
                    Name = ParameterTypeOptions.FirstOrDefault() ?? "设置值",
                    ParameterName = placeholder.Name,
                    Value = placeholder.Value,
                    Remark = placeholder.Name
                };
            }

            EditingInvokeParameters.Add(parameter);
            sequence++;
        }

        NormalizeInvokeParameterSequences();
        SortInvokeParametersBySequence();
        SelectedEditingInvokeParameter = EditingInvokeParameters
            .FirstOrDefault(parameter => string.Equals(parameter.Id, previousSelectedId, StringComparison.OrdinalIgnoreCase))
            ?? EditingInvokeParameters.FirstOrDefault();
    }

    private void RefreshInvokeParametersFromSelectedSystemMethod(bool clearWhenNoMetadata)
    {
        if (!IsSystemOperationSelected)
        {
            return;
        }

        SystemMethodSelectionItem? method = FindSystemMethodByName(EditingInvokeMethod);
        if (method is null)
        {
            if (clearWhenNoMetadata)
            {
                EditingInvokeParameters.Clear();
                SelectedEditingInvokeParameter = null;
            }

            return;
        }

        EditingInvokeParameters.Clear();
        int sequence = 1;
        foreach (SystemMethodParameterSelectionItem parameterMetadata in method.Parameters)
        {
            EditingInvokeParameters.Add(new WorkStepOperationParameter
            {
                Sequence = sequence,
                Name = ParameterTypeOptions.FirstOrDefault() ?? "设置值",
                ParameterName = parameterMetadata.Name,
                Value = parameterMetadata.Type,
                Remark = parameterMetadata.Description
            });
            sequence++;
        }

        NormalizeInvokeParameterSequences();
        SortInvokeParametersBySequence();
        SelectedEditingInvokeParameter = EditingInvokeParameters.FirstOrDefault();
    }

    private void RefreshInvokeParametersFromSelectedJudgeMethod(bool clearWhenNoMetadata)
    {
        if (!IsJudgeOperationSelected)
        {
            return;
        }

        SystemMethodSelectionItem? method = FindJudgeMethodByName(EditingInvokeMethod);
        if (method is null)
        {
            if (clearWhenNoMetadata)
            {
                EditingInvokeParameters.Clear();
                SelectedEditingInvokeParameter = null;
            }

            return;
        }

        EditingInvokeParameters.Clear();
        int sequence = 1;
        foreach (SystemMethodParameterSelectionItem parameterMetadata in method.Parameters)
        {
            EditingInvokeParameters.Add(new WorkStepOperationParameter
            {
                Sequence = sequence,
                Name = ParameterTypeOptions.FirstOrDefault() ?? "\u8BBE\u7F6E\u503C",
                ParameterName = parameterMetadata.Name,
                Value = string.Empty,
                Remark = parameterMetadata.Description
            });
            sequence++;
        }

        NormalizeInvokeParameterSequences();
        SortInvokeParametersBySequence();
        SelectedEditingInvokeParameter = EditingInvokeParameters.FirstOrDefault();
    }

    private void SyncSystemInvokeMethodRemarkFromMethod()
    {
        SystemMethodSelectionItem? method = FindSystemMethodByName(EditingInvokeMethod);
        string remark = method?.Summary ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(remark) &&
            !InvokeMethodRemarkOptions.Any(option => string.Equals(option, remark, StringComparison.OrdinalIgnoreCase)))
        {
            InvokeMethodRemarkOptions.Add(remark);
        }

        _isSyncingSystemInvokeMethodSelection = true;
        try
        {
            EditingInvokeMethodRemark = remark;
        }
        finally
        {
            _isSyncingSystemInvokeMethodSelection = false;
        }
    }

    private void SyncJudgeInvokeMethodRemarkFromMethod()
    {
        SystemMethodSelectionItem? method = FindJudgeMethodByName(EditingInvokeMethod);
        string remark = method?.Summary ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(remark) &&
            !InvokeMethodRemarkOptions.Any(option => string.Equals(option, remark, StringComparison.OrdinalIgnoreCase)))
        {
            InvokeMethodRemarkOptions.Add(remark);
        }

        _isSyncingSystemInvokeMethodSelection = true;
        try
        {
            EditingInvokeMethodRemark = remark;
        }
        finally
        {
            _isSyncingSystemInvokeMethodSelection = false;
        }
    }

    private void SyncSystemInvokeMethodFromRemark()
    {
        if (string.IsNullOrWhiteSpace(EditingInvokeMethodRemark))
        {
            return;
        }

        SystemMethodSelectionItem? method = LoadSystemMethodSelectionItems()
            .FirstOrDefault(item => TextEquals(item.Summary, EditingInvokeMethodRemark));
        if (method is null)
        {
            return;
        }

        if (!InvokeMethodOptions.Any(option => string.Equals(option, method.Name, StringComparison.OrdinalIgnoreCase)))
        {
            InvokeMethodOptions.Add(method.Name);
        }

        _isSyncingSystemInvokeMethodSelection = true;
        try
        {
            EditingInvokeMethod = method.Name;
        }
        finally
        {
            _isSyncingSystemInvokeMethodSelection = false;
        }
    }

    private void SyncJudgeInvokeMethodFromRemark()
    {
        if (string.IsNullOrWhiteSpace(EditingInvokeMethodRemark))
        {
            return;
        }

        SystemMethodSelectionItem? method = LoadJudgeMethodSelectionItems()
            .FirstOrDefault(item => TextEquals(item.Summary, EditingInvokeMethodRemark));
        if (method is null)
        {
            return;
        }

        if (!InvokeMethodOptions.Any(option => string.Equals(option, method.Name, StringComparison.OrdinalIgnoreCase)))
        {
            InvokeMethodOptions.Add(method.Name);
        }

        _isSyncingSystemInvokeMethodSelection = true;
        try
        {
            EditingInvokeMethod = method.Name;
        }
        finally
        {
            _isSyncingSystemInvokeMethodSelection = false;
        }
    }

    private void RefreshInvokeMethodOptions(bool updateStatus)
    {
        string previousSelection = null;
        bool hasPreviousSelection = false;
        if (IsJudgeOperationSelected)
        {
            previousSelection = EditingInvokeMethod;
            InvokeMethodOptions.Clear();
            InvokeMethodRemarkOptions.Clear();
            IReadOnlyList<SystemMethodSelectionItem> judgeMethods = LoadJudgeMethodSelectionItems();

            foreach (string option in judgeMethods
                         .Select(method => method.Name)
                         .Where(option => !string.IsNullOrWhiteSpace(option))
                         .Select(option => option.Trim())
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                InvokeMethodOptions.Add(option);
            }

            foreach (string option in judgeMethods
                         .Select(method => method.Summary)
                         .Where(option => !string.IsNullOrWhiteSpace(option))
                         .Select(option => option.Trim())
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                InvokeMethodRemarkOptions.Add(option);
            }

            hasPreviousSelection =
                !string.IsNullOrWhiteSpace(previousSelection) &&
                InvokeMethodOptions.Any(option => string.Equals(option, previousSelection, StringComparison.OrdinalIgnoreCase));

            if (InvokeMethodOptions.Count == 0)
            {
                EditingInvokeMethod = string.Empty;
            }
            else if (hasPreviousSelection)
            {
                EditingInvokeMethod = previousSelection.Trim();
            }
            else
            {
                EditingInvokeMethod = InvokeMethodOptions.First();
            }

            SyncJudgeInvokeMethodRemarkFromMethod();
            if (!_isInitializingOperationDrawer)
            {
                RefreshInvokeParametersFromSelectedJudgeMethod(clearWhenNoMetadata: true);
            }

            if (updateStatus)
            {
                SetPageStatus($"已按“{EditingOperationObject}”刷新调用方法。", SuccessBrush);
            }

            return;
        }

        if (IsLuaOperationSelected)
        {
            InvokeMethodOptions.Clear();
            InvokeMethodRemarkOptions.Clear();
            EditingInvokeMethodRemark = string.Empty;
            EditingInvokeMethod = LuaOperationObjectName;
            EditingInvokeParameters.Clear();
            SelectedEditingInvokeParameter = null;
            return;
        }

        if (!IsSystemOperationSelected)
        {
            InvokeMethodOptions.Clear();
            InvokeMethodRemarkOptions.Clear();
            _isSyncingSystemInvokeMethodSelection = true;
            try
            {
                EditingInvokeMethodRemark = string.Empty;
            }
            finally
            {
                _isSyncingSystemInvokeMethodSelection = false;
            }

            EditingInvokeMethod = EditingCommandName;
            return;
        }

        previousSelection = EditingInvokeMethod;
        InvokeMethodOptions.Clear();
        InvokeMethodRemarkOptions.Clear();
        IReadOnlyList<SystemMethodSelectionItem> systemMethods = LoadSystemMethodSelectionItems();

        foreach (string option in systemMethods
                     .Select(method => method.Name)
                     .DefaultIfEmpty()
                     .Concat(systemMethods.Count == 0 ? GetSystemInvokeMethodOptions() : Enumerable.Empty<string>())
                     .Where(option => !string.IsNullOrWhiteSpace(option))
                     .Select(option => option!.Trim())
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            InvokeMethodOptions.Add(option);
        }

        foreach (string option in systemMethods
                     .Select(method => method.Summary)
                     .Where(option => !string.IsNullOrWhiteSpace(option))
                     .Select(option => option!.Trim())
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            InvokeMethodRemarkOptions.Add(option);
        }

        hasPreviousSelection =
            !string.IsNullOrWhiteSpace(previousSelection) &&
            InvokeMethodOptions.Any(option => string.Equals(option, previousSelection, StringComparison.OrdinalIgnoreCase));

        //if (!hasPreviousSelection &&
        //    !IsPlaceholderInvokeMethod(previousSelection) &&
        //    !string.IsNullOrWhiteSpace(previousSelection))
        //{
        //    InvokeMethodOptions.Add(previousSelection.Trim());
        //    hasPreviousSelection = true;
        //}

        if (InvokeMethodOptions.Count == 0)
        {
            EditingInvokeMethod = IsPlaceholderInvokeMethod(previousSelection)
                ? string.Empty
                : previousSelection;
        }
        else if (hasPreviousSelection && !IsPlaceholderInvokeMethod(previousSelection))
        {
            EditingInvokeMethod = previousSelection.Trim();
        }
        else
        {
            EditingInvokeMethod = InvokeMethodOptions.First();
        }

        SyncSystemInvokeMethodRemarkFromMethod();
        if (!_isInitializingOperationDrawer)
        {
            RefreshInvokeParametersFromSelectedSystemMethod(clearWhenNoMetadata: true);
        }

        if (updateStatus)
        {
            SetPageStatus($"已按“{EditingOperationObject}”刷新调用方法。", SuccessBrush);
        }
    }

    private static IEnumerable<string> GetSystemInvokeMethodOptions()
    {
        return new[] { "等待", "跳转", "写入日志", "读取系统值", "设置系统值", "开始", "停止" };
    }

    private static SystemMethodSelectionItem? FindSystemMethodByName(string methodName)
    {
        if (string.IsNullOrWhiteSpace(methodName))
        {
            return null;
        }

        return LoadSystemMethodSelectionItems()
            .FirstOrDefault(method => TextEquals(method.Name, methodName));
    }

    private static SystemMethodSelectionItem? FindJudgeMethodByName(string methodName)
    {
        if (string.IsNullOrWhiteSpace(methodName))
        {
            return null;
        }

        return LoadJudgeMethodSelectionItems()
            .FirstOrDefault(method => TextEquals(method.Name, methodName));
    }

    private static IReadOnlyList<SystemMethodSelectionItem> LoadJudgeMethodSelectionItems()
    {
        return new[]
        {
            CreateJudgeMethod(
                "\u7B49\u4E8E\u5224\u65AD",
                "\u5224\u65AD\u4E24\u4E2A\u503C\u662F\u5426\u76F8\u7B49",
                ("\u5DE6\u503C", "\u5DE6\u4FA7\u5F85\u6BD4\u8F83\u7684\u503C"),
                ("\u53F3\u503C", "\u53F3\u4FA7\u5F85\u6BD4\u8F83\u7684\u503C")),
            CreateJudgeMethod(
                "\u4E0D\u7B49\u5224\u65AD",
                "\u5224\u65AD\u4E24\u4E2A\u503C\u662F\u5426\u4E0D\u76F8\u7B49",
                ("\u5DE6\u503C", "\u5DE6\u4FA7\u5F85\u6BD4\u8F83\u7684\u503C"),
                ("\u53F3\u503C", "\u53F3\u4FA7\u5F85\u6BD4\u8F83\u7684\u503C")),
            CreateJudgeMethod(
                "\u5927\u4E8E\u5224\u65AD",
                "\u5224\u65AD\u5DE6\u503C\u662F\u5426\u5927\u4E8E\u53F3\u503C",
                ("\u5DE6\u503C", "\u5DE6\u4FA7\u5F85\u6BD4\u8F83\u7684\u503C"),
                ("\u53F3\u503C", "\u53F3\u4FA7\u5F85\u6BD4\u8F83\u7684\u503C")),
            CreateJudgeMethod(
                "\u5927\u4E8E\u7B49\u4E8E\u5224\u65AD",
                "\u5224\u65AD\u5DE6\u503C\u662F\u5426\u5927\u4E8E\u7B49\u4E8E\u53F3\u503C",
                ("\u5DE6\u503C", "\u5DE6\u4FA7\u5F85\u6BD4\u8F83\u7684\u503C"),
                ("\u53F3\u503C", "\u53F3\u4FA7\u5F85\u6BD4\u8F83\u7684\u503C")),
            CreateJudgeMethod(
                "\u5C0F\u4E8E\u5224\u65AD",
                "\u5224\u65AD\u5DE6\u503C\u662F\u5426\u5C0F\u4E8E\u53F3\u503C",
                ("\u5DE6\u503C", "\u5DE6\u4FA7\u5F85\u6BD4\u8F83\u7684\u503C"),
                ("\u53F3\u503C", "\u53F3\u4FA7\u5F85\u6BD4\u8F83\u7684\u503C")),
            CreateJudgeMethod(
                "\u5C0F\u4E8E\u7B49\u4E8E\u5224\u65AD",
                "\u5224\u65AD\u5DE6\u503C\u662F\u5426\u5C0F\u4E8E\u7B49\u4E8E\u53F3\u503C",
                ("\u5DE6\u503C", "\u5DE6\u4FA7\u5F85\u6BD4\u8F83\u7684\u503C"),
                ("\u53F3\u503C", "\u53F3\u4FA7\u5F85\u6BD4\u8F83\u7684\u503C")),
            CreateJudgeMethod(
                "\u5305\u542B\u5224\u65AD",
                "\u5224\u65AD\u6587\u672C\u662F\u5426\u5305\u542B\u6307\u5B9A\u5173\u952E\u5B57",
                ("\u5F85\u5224\u65AD\u503C", "\u5F85\u68C0\u67E5\u7684\u6587\u672C"),
                ("\u5173\u952E\u5B57", "\u7528\u4E8E\u5339\u914D\u7684\u5173\u952E\u5B57")),
            CreateJudgeMethod(
                "\u4E0D\u5305\u542B\u5224\u65AD",
                "\u5224\u65AD\u6587\u672C\u662F\u5426\u4E0D\u5305\u542B\u6307\u5B9A\u5173\u952E\u5B57",
                ("\u5F85\u5224\u65AD\u503C", "\u5F85\u68C0\u67E5\u7684\u6587\u672C"),
                ("\u5173\u952E\u5B57", "\u7528\u4E8E\u5339\u914D\u7684\u5173\u952E\u5B57")),
            CreateJudgeMethod(
                "\u4E3A\u7A7A\u5224\u65AD",
                "\u5224\u65AD\u6307\u5B9A\u503C\u662F\u5426\u4E3A\u7A7A",
                ("\u5F85\u5224\u65AD\u503C", "\u5F85\u68C0\u67E5\u7684\u503C")),
            CreateJudgeMethod(
                "\u4E0D\u4E3A\u7A7A\u5224\u65AD",
                "\u5224\u65AD\u6307\u5B9A\u503C\u662F\u5426\u4E0D\u4E3A\u7A7A",
                ("\u5F85\u5224\u65AD\u503C", "\u5F85\u68C0\u67E5\u7684\u503C"))
        };
    }

    private static SystemMethodSelectionItem CreateJudgeMethod(
        string name,
        string summary,
        params (string Name, string Description)[] parameters)
    {
        return new SystemMethodSelectionItem(
            name,
            summary,
            parameters.Select(parameter => new SystemMethodParameterSelectionItem(
                parameter.Name,
                string.Empty,
                parameter.Description)));
    }

    private static IReadOnlyList<SystemMethodSelectionItem> LoadSystemMethodSelectionItems()
    {
        string? filePath = GetSystemMethodSourceFileCandidates().FirstOrDefault(File.Exists);
        if (filePath is null)
        {
            return Array.Empty<SystemMethodSelectionItem>();
        }

        try
        {
            return ParseSystemMethodSelectionItems(File.ReadAllText(filePath, Encoding.UTF8));
        }
        catch
        {
            return Array.Empty<SystemMethodSelectionItem>();
        }
    }

    private static IEnumerable<string> GetSystemMethodSourceFileCandidates()
    {
        HashSet<string> seenPaths = new(StringComparer.OrdinalIgnoreCase);
        foreach (string root in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() }
                     .Where(root => !string.IsNullOrWhiteSpace(root)))
        {
            DirectoryInfo? directory = new(root);
            while (directory is not null)
            {
                foreach (string relativePath in new[]
                         {
                             Path.Combine("Business", "System.cs"),
                             Path.Combine("Business", "System"),
                             Path.Combine("Module.Business", "Business", "System.cs"),
                             Path.Combine("Module.Business", "Business", "System")
                         })
                {
                    string candidate = Path.Combine(directory.FullName, relativePath);
                    if (seenPaths.Add(candidate))
                    {
                        yield return candidate;
                    }
                }

                directory = directory.Parent;
            }
        }
    }

    private static IReadOnlyList<SystemMethodSelectionItem> ParseSystemMethodSelectionItems(string sourceText)
    {
        List<SystemMethodSelectionItem> methods = new();
        List<string> documentationLines = new();
        string[] lines = (sourceText ?? string.Empty)
            .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);

        for (int index = 0; index < lines.Length; index++)
        {
            string line = lines[index];
            string trimmedLine = line.TrimStart();
            if (trimmedLine.StartsWith("///", StringComparison.Ordinal))
            {
                documentationLines.Add(line);
                continue;
            }

            if (string.IsNullOrWhiteSpace(trimmedLine) ||
                trimmedLine.StartsWith("[", StringComparison.Ordinal))
            {
                continue;
            }

            if (!TryReadSystemMethodSignature(lines, ref index, out Match match))
            {
                documentationLines.Clear();
                continue;
            }

            string methodName = match.Groups["name"].Value.Trim();
            if (string.IsNullOrWhiteSpace(methodName) || methodName.Contains('<', StringComparison.Ordinal))
            {
                documentationLines.Clear();
                continue;
            }

            (string summary, Dictionary<string, string> parameterDescriptions) =
                ParseSystemMethodDocumentation(string.Join(Environment.NewLine, documentationLines));
            IReadOnlyList<SystemMethodParameterSelectionItem> parameters =
                ParseSystemMethodParameters(match.Groups["parameters"].Value, parameterDescriptions);

            methods.Add(new SystemMethodSelectionItem(methodName, summary, parameters));
            documentationLines.Clear();
        }

        return methods;
    }

    private static bool TryReadSystemMethodSignature(string[] lines, ref int index, out Match match)
    {
        StringBuilder signatureBuilder = new(lines[index].Trim());
        int parenthesisDepth = CountParenthesisDepth(signatureBuilder.ToString());
        while (parenthesisDepth > 0 && index + 1 < lines.Length)
        {
            index++;
            string nextLine = lines[index].Trim();
            signatureBuilder.Append(' ').Append(nextLine);
            parenthesisDepth += CountParenthesisDepth(nextLine);
        }

        string signature = signatureBuilder.ToString();
        int closeParenthesisIndex = signature.IndexOf(')');
        if (closeParenthesisIndex >= 0)
        {
            signature = signature[..(closeParenthesisIndex + 1)];
        }

        match = SystemMethodSignatureRegex.Match(signature);
        return match.Success;
    }

    private static int CountParenthesisDepth(string text)
    {
        int depth = 0;
        foreach (char value in text)
        {
            if (value == '(')
            {
                depth++;
            }
            else if (value == ')')
            {
                depth--;
            }
        }

        return depth;
    }

    private static (string Summary, Dictionary<string, string> ParameterDescriptions) ParseSystemMethodDocumentation(string documentationText)
    {
        string xmlText = string.Join(
            Environment.NewLine,
            (documentationText ?? string.Empty)
                .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None)
                .Select(line => Regex.Replace(line, @"^\s*///\s?", string.Empty)));

        try
        {
            XElement document = XElement.Parse($"<doc>{xmlText}</doc>");
            string summary = NormalizeDocumentationText(document.Element("summary")?.Value);
            Dictionary<string, string> parameterDescriptions = document
                .Elements("param")
                .Where(element => element.Attribute("name") is not null)
                .GroupBy(
                    element => element.Attribute("name")!.Value.Trim(),
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => NormalizeDocumentationText(group.First().Value),
                    StringComparer.OrdinalIgnoreCase);

            return (summary, parameterDescriptions);
        }
        catch
        {
            return ParseSystemMethodDocumentationFallback(xmlText);
        }
    }

    private static (string Summary, Dictionary<string, string> ParameterDescriptions) ParseSystemMethodDocumentationFallback(string xmlText)
    {
        string summary = NormalizeDocumentationText(
            Regex.Match(xmlText ?? string.Empty, @"<summary>(?<value>.*?)</summary>", RegexOptions.Singleline)
                .Groups["value"]
                .Value);

        Dictionary<string, string> parameterDescriptions = new(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Regex.Matches(
                     xmlText ?? string.Empty,
                     @"<param\s+name=""(?<name>[^""]+)"">(?<value>.*?)</param>",
                     RegexOptions.Singleline))
        {
            string name = match.Groups["name"].Value.Trim();
            if (!string.IsNullOrWhiteSpace(name))
            {
                parameterDescriptions[name] = NormalizeDocumentationText(match.Groups["value"].Value);
            }
        }

        return (summary, parameterDescriptions);
    }

    private static IReadOnlyList<SystemMethodParameterSelectionItem> ParseSystemMethodParameters(
        string parameterText,
        IReadOnlyDictionary<string, string> parameterDescriptions)
    {
        List<SystemMethodParameterSelectionItem> parameters = new();
        foreach (string rawParameter in SplitSystemMethodParameters(parameterText))
        {
            string parameter = rawParameter.Trim();
            if (string.IsNullOrWhiteSpace(parameter))
            {
                continue;
            }

            int defaultValueIndex = parameter.IndexOf('=');
            if (defaultValueIndex >= 0)
            {
                parameter = parameter[..defaultValueIndex].Trim();
            }

            string[] parts = Regex.Replace(parameter, @"\s+", " ")
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                continue;
            }

            string name = parts[^1].Trim().TrimStart('@');
            string type = string.Join(
                " ",
                parts
                    .Take(parts.Length - 1)
                    .Where(part => !IsParameterModifier(part)));
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(type))
            {
                continue;
            }

            string description = parameterDescriptions.TryGetValue(name, out string? parameterDescription) &&
                                 !string.IsNullOrWhiteSpace(parameterDescription)
                ? parameterDescription
                : name;
            parameters.Add(new SystemMethodParameterSelectionItem(name, type, description));
        }

        return parameters;
    }

    private static IEnumerable<string> SplitSystemMethodParameters(string parameterText)
    {
        if (string.IsNullOrWhiteSpace(parameterText))
        {
            yield break;
        }

        int genericDepth = 0;
        int startIndex = 0;
        for (int index = 0; index < parameterText.Length; index++)
        {
            char current = parameterText[index];
            if (current == '<')
            {
                genericDepth++;
            }
            else if (current == '>')
            {
                genericDepth = Math.Max(0, genericDepth - 1);
            }
            else if (current == ',' && genericDepth == 0)
            {
                yield return parameterText[startIndex..index];
                startIndex = index + 1;
            }
        }

        yield return parameterText[startIndex..];
    }

    private static bool IsParameterModifier(string value)
    {
        return value is "ref" or "out" or "in" or "params" or "this";
    }

    private static string NormalizeDocumentationText(string? value)
    {
        string text = Regex.Replace(value ?? string.Empty, "<.*?>", string.Empty);
        return Regex.Replace(text, @"\s+", " ").Trim();
    }

    private static bool TextEquals(string? left, string? right)
    {
        return string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> LoadDeviceOperationObjectOptions()
    {
        string communicationConfigDirectory = Path.Combine(AppContext.BaseDirectory, "Config", "Communication");
        if (!Directory.Exists(communicationConfigDirectory))
        {
            return Enumerable.Empty<string>();
        }

        List<string> names = new();
        foreach (string filePath in Directory.EnumerateFiles(communicationConfigDirectory, "*.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(filePath));
                if (document.RootElement.TryGetProperty("LocalName", out JsonElement localNameElement))
                {
                    string? localName = localNameElement.GetString();
                    if (!string.IsNullOrWhiteSpace(localName))
                    {
                        names.Add(localName.Trim());
                    }
                }
            }
            catch
            {
                // 忽略损坏或非通信配置 JSON，刷新下拉时不阻断编辑流程。
            }
        }

        return names;
    }

    private static IEnumerable<string> LoadProtocolOptions()
    {
        return LoadProtocolSelectionItems()
            .Select(item => item.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> LoadProtocolCommandOptions(string protocolName)
    {
        return LoadProtocolSelectionItems()
            .Where(item => string.Equals(item.Name, protocolName?.Trim(), StringComparison.OrdinalIgnoreCase))
            .SelectMany(item => item.Commands.Select(command => command.Name))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<ProtocolPlaceholderSelectionItem> LoadProtocolCommandPlaceholders(
        string protocolName,
        string commandName)
    {
        if (string.IsNullOrWhiteSpace(protocolName) || string.IsNullOrWhiteSpace(commandName))
        {
            return Array.Empty<ProtocolPlaceholderSelectionItem>();
        }

        ProtocolCommandSelectionItem? command = LoadProtocolSelectionItems()
            .Where(item => string.Equals(item.Name, protocolName.Trim(), StringComparison.OrdinalIgnoreCase))
            .SelectMany(item => item.Commands)
            .FirstOrDefault(command => string.Equals(command.Name, commandName.Trim(), StringComparison.OrdinalIgnoreCase));

        return command is null
            ? Array.Empty<ProtocolPlaceholderSelectionItem>()
            : command.Placeholders;
    }

    private static IEnumerable<ProtocolSelectionItem> LoadProtocolSelectionItems()
    {
        string protocolConfigDirectory = Path.Combine(AppContext.BaseDirectory, "Config", "Protocol");
        if (!Directory.Exists(protocolConfigDirectory))
        {
            return Enumerable.Empty<ProtocolSelectionItem>();
        }

        List<ProtocolSelectionItem> items = new();
        foreach (string filePath in Directory.EnumerateFiles(protocolConfigDirectory, "*.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                string storageText = File.ReadAllText(filePath, Encoding.UTF8);
                string json = TryReadProtocolJson(storageText);
                using JsonDocument document = JsonDocument.Parse(json);

                if (!document.RootElement.TryGetProperty("Name", out JsonElement nameElement))
                {
                    continue;
                }

                string? protocolName = nameElement.GetString();
                if (string.IsNullOrWhiteSpace(protocolName))
                {
                    continue;
                }

                List<ProtocolCommandSelectionItem> commands = new();
                if (document.RootElement.TryGetProperty("Commands", out JsonElement commandsElement) &&
                    commandsElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement commandElement in commandsElement.EnumerateArray())
                    {
                        if (commandElement.TryGetProperty("Name", out JsonElement commandNameElement) &&
                            !string.IsNullOrWhiteSpace(commandNameElement.GetString()))
                        {
                            string commandName = commandNameElement.GetString()!.Trim();
                            string contentTemplate = GetJsonString(commandElement, "ContentTemplate");
                            string placeholderValuesText = GetJsonString(commandElement, "PlaceholderValuesText");
                            commands.Add(new ProtocolCommandSelectionItem(
                                commandName,
                                BuildProtocolPlaceholderSelectionItems(contentTemplate, placeholderValuesText)));
                        }
                    }
                }

                if (commands.Count == 0)
                {
                    commands.Add(new ProtocolCommandSelectionItem(
                        "指令 1",
                        BuildProtocolPlaceholderSelectionItems(
                            GetJsonString(document.RootElement, "ContentTemplate"),
                            GetJsonString(document.RootElement, "PlaceholderValuesText"))));
                }

                items.Add(new ProtocolSelectionItem(protocolName.Trim(), commands));
            }
            catch
            {
                // 忽略损坏或无法解密的协议配置，避免阻断工步编辑。
            }
        }

        return items;
    }

    private static string TryReadProtocolJson(string storageText)
    {
        try
        {
            return storageText.DesDecrypt();
        }
        catch
        {
            return storageText;
        }
    }

    private static string GetJsonString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement propertyElement)
            ? propertyElement.GetString() ?? string.Empty
            : string.Empty;
    }

    private static IReadOnlyList<ProtocolPlaceholderSelectionItem> BuildProtocolPlaceholderSelectionItems(
        string contentTemplate,
        string placeholderValuesText)
    {
        Dictionary<string, string> valuesByName = ParseProtocolPlaceholderValues(placeholderValuesText);
        List<ProtocolPlaceholderSelectionItem> placeholders = new();
        foreach (string placeholderName in ExtractProtocolPlaceholderNames(contentTemplate))
        {
            valuesByName.TryGetValue(placeholderName, out string? value);
            placeholders.Add(new ProtocolPlaceholderSelectionItem(placeholderName, value ?? string.Empty));
        }

        return placeholders;
    }

    private static IEnumerable<string> ExtractProtocolPlaceholderNames(string contentTemplate)
    {
        HashSet<string> seenNames = new(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in ProtocolPlaceholderRegex.Matches(contentTemplate ?? string.Empty))
        {
            string placeholderName = match.Groups["name"].Value.Trim();
            if (!string.IsNullOrWhiteSpace(placeholderName) && seenNames.Add(placeholderName))
            {
                yield return placeholderName;
            }
        }
    }

    private static Dictionary<string, string> ParseProtocolPlaceholderValues(string placeholderValuesText)
    {
        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
        string[] lines = (placeholderValuesText ?? string.Empty)
            .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);

        foreach (string rawLine in lines)
        {
            string line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) ||
                line.StartsWith("#", StringComparison.Ordinal) ||
                line.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            int equalsIndex = line.IndexOf('=');
            if (equalsIndex <= 0)
            {
                continue;
            }

            string key = line[..equalsIndex].Trim();
            string value = line[(equalsIndex + 1)..].Trim();
            if (!string.IsNullOrWhiteSpace(key))
            {
                values[key] = value;
            }
        }

        return values;
    }

    private void EnsureOperationObjectOption(string operationObject)
    {
        RefreshOperationObjectOptions(updateStatus: false);
        if (!string.IsNullOrWhiteSpace(operationObject) &&
            !OperationObjectOptions.Any(option => string.Equals(option, operationObject, StringComparison.OrdinalIgnoreCase)))
        {
            OperationObjectOptions.Add(operationObject.Trim());
        }
    }

    private void EnsureProtocolOption(string protocolName)
    {
        RefreshProtocolOptions(updateStatus: false);
        if (!string.IsNullOrWhiteSpace(protocolName) &&
            !ProtocolOptions.Any(option => string.Equals(option, protocolName, StringComparison.OrdinalIgnoreCase)))
        {
            ProtocolOptions.Add(protocolName.Trim());
        }

        if (!string.IsNullOrWhiteSpace(protocolName))
        {
            EditingProtocolName = protocolName.Trim();
        }
    }

    private void EnsureCommandOption(string commandName)
    {
        RefreshCommandOptions(updateStatus: false);
        if (!string.IsNullOrWhiteSpace(commandName) &&
            !CommandOptions.Any(option => string.Equals(option, commandName, StringComparison.OrdinalIgnoreCase)))
        {
            CommandOptions.Add(commandName.Trim());
        }

        if (!string.IsNullOrWhiteSpace(commandName))
        {
            EditingCommandName = commandName.Trim();
        }
    }

    private static string ResolveOperationObjectForEditing(WorkStepOperation operation)
    {
        if (IsLuaOperationObject(operation.OperationType) ||
            IsLuaOperationObject(operation.OperationObject))
        {
            return LuaOperationObjectName;
        }

        if (IsJudgeOperationObject(operation.OperationType) ||
            IsJudgeOperationObject(operation.OperationObject))
        {
            return JudgeOperationObjectName;
        }

        if (IsLegacySystemOperationType(operation.OperationType) ||
            IsSystemOperationObject(operation.OperationObject))
        {
            return SystemOperationObjectName;
        }

        return string.IsNullOrWhiteSpace(operation.OperationObject)
            ? SystemOperationObjectName
            : operation.OperationObject.Trim();
    }

    private static bool IsLegacySystemOperationType(string? operationType)
    {
        return string.Equals(operationType?.Trim(), "系统", StringComparison.OrdinalIgnoreCase);
    }

    private const string SystemOperationObjectName = "System";

    private const string JudgeOperationObjectName = "\u5224\u65AD";

    private const string LuaOperationObjectName = "Lua";

    private static bool IsSystemOperationObject(string? operationObject)
    {
        return string.Equals(operationObject?.Trim(), SystemOperationObjectName, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(operationObject?.Trim(), "系统", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsJudgeOperationObject(string? operationObject)
    {
        return string.Equals(operationObject?.Trim(), JudgeOperationObjectName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLuaOperationObject(string? operationObject)
    {
        return string.Equals(operationObject?.Trim(), LuaOperationObjectName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPlaceholderInvokeMethod(string? invokeMethod)
    {
        return string.IsNullOrWhiteSpace(invokeMethod) ||
               string.Equals(invokeMethod.Trim(), "调用方法", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class SystemMethodSelectionItem
    {
        public SystemMethodSelectionItem(
            string name,
            string summary,
            IEnumerable<SystemMethodParameterSelectionItem> parameters)
        {
            Name = name;
            Summary = summary;
            Parameters = parameters.ToList();
        }

        public string Name { get; }

        public string Summary { get; }

        public List<SystemMethodParameterSelectionItem> Parameters { get; }
    }

    private sealed class SystemMethodParameterSelectionItem
    {
        public SystemMethodParameterSelectionItem(string name, string type, string description)
        {
            Name = name;
            Type = type;
            Description = description;
        }

        public string Name { get; }

        public string Type { get; }

        public string Description { get; }
    }

    private sealed class ProtocolSelectionItem
    {
        public ProtocolSelectionItem(string name, IEnumerable<ProtocolCommandSelectionItem> commands)
        {
            Name = name;
            Commands = commands.ToList();
        }

        public string Name { get; }

        public List<ProtocolCommandSelectionItem> Commands { get; }
    }

    private sealed class ProtocolCommandSelectionItem
    {
        public ProtocolCommandSelectionItem(string name, IEnumerable<ProtocolPlaceholderSelectionItem> placeholders)
        {
            Name = name;
            Placeholders = placeholders.ToList();
        }

        public string Name { get; }

        public List<ProtocolPlaceholderSelectionItem> Placeholders { get; }
    }

    private sealed class ProtocolPlaceholderSelectionItem
    {
        public ProtocolPlaceholderSelectionItem(string name, string value)
        {
            Name = name;
            Value = value;
        }

        public string Name { get; }

        public string Value { get; }
    }

    private void RaiseCommandStatesChanged()
    {
        RaiseCommandState(DuplicateWorkStepCommand);
        RaiseCommandState(DeleteWorkStepCommand);
        RaiseCommandState(AddOperationCommand);
        RaiseCommandState(CopyOperationCommand);
        RaiseCommandState(PasteOperationCommand);
        RaiseCommandState(DeleteOperationCommand);
        RaiseCommandState(SaveOperationDrawerCommand);
        RaiseCommandState(CloseOperationDrawerCommand);
        RaiseCommandState(RefreshOperationObjectsCommand);
        RaiseCommandState(AddInvokeParameterCommand);
        RaiseCommandState(DeleteInvokeParameterCommand);
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


