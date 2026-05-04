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
        DeleteOperationCommand = new RelayCommand(_ => DeleteSelectedOperation(), _ => SelectedWorkStep is not null && SelectedOperation is not null);
        SaveOperationDrawerCommand = new RelayCommand(_ => SaveOperationDrawer(), _ => IsOperationDrawerOpen);
        CloseOperationDrawerCommand = new RelayCommand(_ => CloseOperationDrawer());
        RefreshOperationObjectsCommand = new RelayCommand(_ => RefreshOperationObjectOptions(updateStatus: true), _ => IsOperationDrawerOpen);
        AddInvokeParameterCommand = new RelayCommand(_ => AddInvokeParameter(), _ => IsOperationDrawerOpen);
        DeleteInvokeParameterCommand = new RelayCommand(_ => DeleteSelectedInvokeParameter(), _ => IsOperationDrawerOpen && SelectedEditingInvokeParameter is not null);
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

        if (!IsSystemOperationSelected && string.IsNullOrWhiteSpace(EditingProtocolName))
        {
            SetPageStatus("协议不能为空。", WarningBrush);
            return;
        }

        if (!IsSystemOperationSelected && string.IsNullOrWhiteSpace(EditingCommandName))
        {
            SetPageStatus("指令不能为空。", WarningBrush);
            return;
        }

        string invokeMethod = IsSystemOperationSelected ? EditingInvokeMethod : EditingCommandName;
        if (string.IsNullOrWhiteSpace(invokeMethod))
        {
            SetPageStatus("调用方法不能为空。", WarningBrush);
            return;
        }

        if (!int.TryParse(EditingDelayMillisecondsText, out int delayMilliseconds) || delayMilliseconds < 0)
        {
            SetPageStatus("延时(ms)必须是大于等于 0 的整数。", WarningBrush);
            return;
        }

        _drawerOperation.OperationObject = EditingOperationObject.Trim();
        _drawerOperation.ProtocolName = IsSystemOperationSelected ? string.Empty : EditingProtocolName.Trim();
        _drawerOperation.CommandName = IsSystemOperationSelected ? string.Empty : EditingCommandName.Trim();
        _drawerOperation.InvokeMethod = invokeMethod.Trim();
        _drawerOperation.ReturnValue = EditingReturnValue.Trim();
        _drawerOperation.DelayMilliseconds = delayMilliseconds;
        _drawerOperation.Remark = EditingRemark.Trim();
        NormalizeInvokeParameterSequences();
        SortInvokeParametersBySequence();
        _drawerOperation.Parameters = new ObservableCollection<WorkStepOperationParameter>(
            EditingInvokeParameters
                .OrderBy(parameter => parameter.Sequence)
                .Select(parameter => parameter.Clone()));

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
        SelectedEditingInvokeParameter = null;
        OnPropertyChanged(nameof(OperationDrawerTitle));
    }

    /// <summary>
    /// 删除当前选中的操作步骤。
    /// </summary>
    private void DeleteSelectedOperation()
    {
        if (SelectedWorkStep is null || SelectedOperation is null)
        {
            return;
        }

        int index = SelectedWorkStep.Steps.IndexOf(SelectedOperation);
        if (ReferenceEquals(_drawerOperation, SelectedOperation))
        {
            CloseOperationDrawer();
        }

        SelectedWorkStep.Steps.Remove(SelectedOperation);
        SelectedOperation = SelectedWorkStep.Steps.Count == 0
            ? null
            : SelectedWorkStep.Steps[Math.Clamp(index, 0, SelectedWorkStep.Steps.Count - 1)];

        SetPageStatus("已删除步骤。", WarningBrush);
    }

    private void BeginOperationDrawer(WorkStepOperation operation, bool isNewOperation)
    {
        _drawerOperation = operation;
        _isNewOperationInDrawer = isNewOperation;
        string operationObject = ResolveOperationObjectForEditing(operation);
        EnsureOperationObjectOption(operationObject);
        EditingOperationObject = operationObject;
        EditingProtocolName = operation.ProtocolName;
        EnsureProtocolOption(EditingProtocolName);
        EditingCommandName = string.IsNullOrWhiteSpace(operation.CommandName)
            ? operation.InvokeMethod
            : operation.CommandName;
        EnsureCommandOption(EditingCommandName);
        EditingInvokeMethod = IsSystemOperationSelected ? operation.InvokeMethod : EditingCommandName;
        RefreshProtocolOptions(updateStatus: false);
        RefreshInvokeMethodOptions(updateStatus: false);
        EditingReturnValue = operation.ReturnValue;
        EditingDelayMillisecondsText = operation.DelayMilliseconds.ToString();
        EditingRemark = operation.Remark;
        EditingInvokeParameters.Clear();
        foreach (WorkStepOperationParameter parameter in operation.Parameters.Select(parameter => parameter.Clone()))
        {
            EditingInvokeParameters.Add(parameter);
        }

        NormalizeInvokeParameterSequences();
        SortInvokeParametersBySequence();

        if (!IsSystemOperationSelected && EditingInvokeParameters.Count == 0)
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
            StepName = stepName
        };

        workStep.Steps.Add(new WorkStepOperation
        {
            OperationObject = SystemOperationObjectName,
            InvokeMethod = "等待",
            ReturnValue = string.Empty,
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
            Steps = new ObservableCollection<WorkStepOperation>(
                source.Steps.Select(operation => new WorkStepOperation
                {
                    Id = Guid.NewGuid().ToString("N"),
                    OperationObject = operation.OperationObject,
                    ProtocolName = operation.ProtocolName,
                    CommandName = operation.CommandName,
                    InvokeMethod = operation.InvokeMethod,
                    ReturnValue = operation.ReturnValue,
                    DelayMilliseconds = operation.DelayMilliseconds,
                    Remark = operation.Remark,
                    Parameters = new ObservableCollection<WorkStepOperationParameter>(
                        operation.Parameters.Select(parameter => new WorkStepOperationParameter
                        {
                            Id = Guid.NewGuid().ToString("N"),
                            Sequence = parameter.Sequence,
                            Name = parameter.Name,
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
               Contains(workStep.OperationSummary, keyword);
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

                if (string.IsNullOrWhiteSpace(operation.InvokeMethod))
                {
                    message = $"工步“{workStep.StepName}”的调用方法不能为空。";
                    return false;
                }

                if (!IsSystemOperationObject(operation.OperationObject) &&
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

        OperationObjectOptions.Add(SystemOperationObjectName);
        foreach (string option in LoadDeviceOperationObjectOptions()
                     .Where(option => !string.IsNullOrWhiteSpace(option))
                     .Select(option => option.Trim())
                     .Where(option => !IsSystemOperationObject(option))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(option => option, StringComparer.OrdinalIgnoreCase))
        {
            OperationObjectOptions.Add(option);
        }

        if (!string.IsNullOrWhiteSpace(previousSelection) &&
            OperationObjectOptions.Any(option => string.Equals(option, previousSelection, StringComparison.OrdinalIgnoreCase)))
        {
            EditingOperationObject = previousSelection;
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

        if (IsSystemOperationSelected)
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

        if (IsSystemOperationSelected || string.IsNullOrWhiteSpace(EditingProtocolName))
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
        if (IsSystemOperationSelected)
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

    private void RefreshInvokeMethodOptions(bool updateStatus)
    {
        if (!IsSystemOperationSelected)
        {
            InvokeMethodOptions.Clear();
            EditingInvokeMethod = EditingCommandName;
            return;
        }

        string previousSelection = EditingInvokeMethod;
        InvokeMethodOptions.Clear();

        foreach (string option in GetSystemInvokeMethodOptions()
                     .Where(option => !string.IsNullOrWhiteSpace(option))
                     .Select(option => option.Trim())
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            InvokeMethodOptions.Add(option);
        }

        bool hasPreviousSelection =
            !string.IsNullOrWhiteSpace(previousSelection) &&
            InvokeMethodOptions.Any(option => string.Equals(option, previousSelection, StringComparison.OrdinalIgnoreCase));

        if (!hasPreviousSelection &&
            !IsPlaceholderInvokeMethod(previousSelection) &&
            !string.IsNullOrWhiteSpace(previousSelection))
        {
            InvokeMethodOptions.Add(previousSelection.Trim());
            hasPreviousSelection = true;
        }

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

        if (updateStatus)
        {
            SetPageStatus($"已按“{EditingOperationObject}”刷新调用方法。", SuccessBrush);
        }
    }

    private static IEnumerable<string> GetSystemInvokeMethodOptions()
    {
        return new[] { "等待", "跳转", "写入日志", "读取系统值", "设置系统值", "开始", "停止" };
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

    private static bool IsSystemOperationObject(string? operationObject)
    {
        return string.Equals(operationObject?.Trim(), SystemOperationObjectName, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(operationObject?.Trim(), "系统", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPlaceholderInvokeMethod(string? invokeMethod)
    {
        return string.IsNullOrWhiteSpace(invokeMethod) ||
               string.Equals(invokeMethod.Trim(), "调用方法", StringComparison.OrdinalIgnoreCase);
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
