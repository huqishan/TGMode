using ControlLibrary;
using Module.Business.Models;
using Module.Business.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Windows.Input;
using System.Windows.Data;
using System.Windows.Media;

namespace Module.Business.ViewModels;

/// <summary>
/// 工步配置界面命令实现。
/// </summary>
public sealed partial class WorkStepConfigurationViewModel
{
    #region 构造与初始化

    public WorkStepConfigurationViewModel()
    {
        WorkSteps.CollectionChanged += WorkSteps_CollectionChanged;
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
        AddOperationCommand = new RelayCommand(_ => AddOperation(), _ => SelectedWorkStep is not null);
        DeleteOperationCommand = new RelayCommand(_ => DeleteSelectedOperation(), _ => SelectedWorkStep is not null && SelectedOperation is not null);
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
    /// 给当前工步新增一个操作步骤。
    /// </summary>
    private void AddOperation()
    {
        if (SelectedWorkStep is null)
        {
            return;
        }

        WorkStepOperation operation = new()
        {
            OperationObject = $"操作对象 {SelectedWorkStep.Steps.Count + 1}",
            InvokeMethod = "调用方法"
        };

        SelectedWorkStep.Steps.Add(operation);
        SelectedOperation = operation;
        SetPageStatus("已新增步骤，填写操作对象和调用方法。", SuccessBrush);
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
        SelectedWorkStep.Steps.Remove(SelectedOperation);
        SelectedOperation = SelectedWorkStep.Steps.Count == 0
            ? null
            : SelectedWorkStep.Steps[Math.Clamp(index, 0, SelectedWorkStep.Steps.Count - 1)];

        SetPageStatus("已删除步骤。", WarningBrush);
    }

    #endregion

    #region 工具方法

    private WorkStepProfile CreateWorkStep(string productName, string stepName)
    {
        WorkStepProfile workStep = new()
        {
            ProductName = productName,
            StepName = stepName
        };

        workStep.Steps.Add(new WorkStepOperation
        {
            OperationObject = "设备",
            InvokeMethod = "启动"
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
                    InvokeMethod = operation.InvokeMethod
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
                if (string.IsNullOrWhiteSpace(operation.OperationObject) ||
                    string.IsNullOrWhiteSpace(operation.InvokeMethod))
                {
                    message = $"工步“{workStep.StepName}”的操作对象和调用方法不能为空。";
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

    private void RaiseCommandStatesChanged()
    {
        RaiseCommandState(DuplicateWorkStepCommand);
        RaiseCommandState(DeleteWorkStepCommand);
        RaiseCommandState(AddOperationCommand);
        RaiseCommandState(DeleteOperationCommand);
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
