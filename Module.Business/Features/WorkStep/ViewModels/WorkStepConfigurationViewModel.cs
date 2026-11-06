using ControlLibrary;
using Module.Business.Features.OperationEditing.ViewModels;
using Module.Business.Features.OperationEditing.ViewModels.PresentationModels;
using Module.Business.Features.WorkStep.Services;
using Module.Business.Features.WorkStep.ViewModels.PresentationModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace Module.Business.Features.WorkStep.ViewModels;

/// <summary>
/// 业务配置页面视图模型，独立维护业务模板并组合现有步骤编辑器。
/// </summary>
public sealed class WorkStepConfigurationViewModel : ViewModelProperties
{
    #region 状态字段

    private static readonly Brush SuccessBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16A34A"));
    private static readonly Brush WarningBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EA580C"));
    private static readonly Brush NeutralBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));
    private readonly List<WorkStepOperation> _copiedOperations = new();
    private WorkStepProfile? _selectedWorkStep;
    private WorkStepOperation? _selectedOperation;
    private string _searchText = string.Empty;
    private string _pageStatusText = "等待编辑";
    private Brush _pageStatusBrush = NeutralBrush;

    #endregion

    #region 构造与集合

    public WorkStepConfigurationViewModel()
    {
        WorkSteps = WorkStepConfigurationStore.Load();
        WorkStepsView = CollectionViewSource.GetDefaultView(WorkSteps);
        WorkStepsView.Filter = FilterWorkSteps;
        OperationEditor = new OperationEditorViewModel();
        OperationEditor.OperationSaved += OperationEditor_OperationSaved;

        NewWorkStepCommand = new RelayCommand(_ => NewWorkStep());
        DuplicateWorkStepCommand = new RelayCommand(_ => DuplicateWorkStep(), _ => SelectedWorkStep is not null);
        DeleteWorkStepCommand = new RelayCommand(_ => DeleteWorkStep(), _ => SelectedWorkStep is not null);
        SaveWorkStepsCommand = new RelayCommand(_ => SaveWorkSteps());
        AddStepCommand = new RelayCommand(_ => OpenNewOperation(), _ => SelectedWorkStep is not null);
        CopyStepCommand = new RelayCommand(_ => CopyOperations(), _ => GetOperationsForClipboard().Count > 0);
        PasteStepCommand = new RelayCommand(_ => PasteOperations(), _ => SelectedWorkStep is not null && _copiedOperations.Count > 0);
        DeleteStepCommand = new RelayCommand(_ => DeleteOperations(), _ => SelectedWorkStep is not null &&
            (SelectedOperation is not null || SelectedWorkStep.Operations.Any(operation => operation.IsChecked)));

        SelectedWorkStep = WorkSteps.FirstOrDefault();
        SetStatus(WorkSteps.Count == 0 ? "暂无业务配置，请点击新建。" : $"已加载 {WorkSteps.Count} 个业务。", NeutralBrush);
    }

    public ObservableCollection<WorkStepProfile> WorkSteps { get; }

    public ICollectionView WorkStepsView { get; }

    public OperationEditorViewModel OperationEditor { get; }

    #endregion

    #region 页面属性

    public WorkStepProfile? SelectedWorkStep
    {
        get => _selectedWorkStep;
        set
        {
            if (ReferenceEquals(_selectedWorkStep, value))
            {
                return;
            }

            _selectedWorkStep = value;
            SelectedOperation = value?.Operations.FirstOrDefault();
            OnPropertyChanged();
            OnPropertyChanged(nameof(StepCollection));
            OnPropertyChanged(nameof(AreAllStepsChecked));
            RaiseCommandStatesChanged();
        }
    }

    public WorkStepOperation? SelectedOperation
    {
        get => _selectedOperation;
        set
        {
            if (ReferenceEquals(_selectedOperation, value))
            {
                return;
            }

            _selectedOperation = value;
            OnPropertyChanged();
            RaiseCommandStatesChanged();
        }
    }

    public ObservableCollection<WorkStepOperation>? StepCollection => SelectedWorkStep?.Operations;

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetField(ref _searchText, value ?? string.Empty))
            {
                WorkStepsView.Refresh();
            }
        }
    }

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

    public bool AreAllStepsChecked
    {
        get => SelectedWorkStep is not null && SelectedWorkStep.Operations.Count > 0 &&
               SelectedWorkStep.Operations.All(operation => operation.IsChecked);
        set
        {
            if (SelectedWorkStep is null)
            {
                return;
            }

            foreach (WorkStepOperation operation in SelectedWorkStep.Operations)
            {
                operation.IsChecked = value;
            }

            OnPropertyChanged();
            RaiseCommandStatesChanged();
        }
    }

    #endregion

    #region 命令

    public ICommand NewWorkStepCommand { get; }
    public ICommand DuplicateWorkStepCommand { get; }
    public ICommand DeleteWorkStepCommand { get; }
    public ICommand SaveWorkStepsCommand { get; }
    public ICommand AddStepCommand { get; }
    public ICommand CopyStepCommand { get; }
    public ICommand PasteStepCommand { get; }
    public ICommand DeleteStepCommand { get; }

    #endregion

    #region 业务管理

    private void NewWorkStep()
    {
        WorkStepProfile workStep = new() { Name = GenerateUniqueName("业务") };
        WorkSteps.Add(workStep);
        SearchText = string.Empty;
        WorkStepsView.Refresh();
        SelectedWorkStep = workStep;
        SetStatus("已新增业务，保存后生效。", SuccessBrush);
    }

    private void DuplicateWorkStep()
    {
        if (SelectedWorkStep is null)
        {
            return;
        }

        WorkStepProfile copy = SelectedWorkStep.Clone();
        copy.Id = Guid.NewGuid().ToString("N");
        copy.Name = GenerateUniqueName($"{SelectedWorkStep.Name} 副本");
        foreach (WorkStepOperation operation in copy.Operations)
        {
            operation.Id = Guid.NewGuid().ToString("N");
            foreach (InputParameter parameter in operation.Parameters)
            {
                parameter.Id = Guid.NewGuid().ToString("N");
            }

            foreach (ReturnValue returnValue in operation.ReturnValues)
            {
                returnValue.Id = Guid.NewGuid().ToString("N");
            }
        }

        copy.MarkModified();
        WorkSteps.Add(copy);
        SearchText = string.Empty;
        WorkStepsView.Refresh();
        SelectedWorkStep = copy;
        SetStatus($"已复制业务：{copy.Name}。", SuccessBrush);
    }

    private void DeleteWorkStep()
    {
        if (SelectedWorkStep is null)
        {
            return;
        }

        int index = WorkSteps.IndexOf(SelectedWorkStep);
        WorkSteps.Remove(SelectedWorkStep);
        SelectedWorkStep = WorkSteps.Count == 0 ? null : WorkSteps[Math.Clamp(index, 0, WorkSteps.Count - 1)];
        SetStatus("已删除业务，保存后生效。", WarningBrush);
    }

    private void SaveWorkSteps()
    {
        // 业务名称作为方案配置引用内置业务的标识，保存前按去除首尾空格且忽略大小写的规则判重。
        string? duplicateName = WorkSteps
            .Where(workStep => !string.IsNullOrWhiteSpace(workStep.Name))
            .GroupBy(workStep => workStep.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1)
            ?.Key;
        if (!string.IsNullOrWhiteSpace(duplicateName))
        {
            SetStatus($"业务名称“{duplicateName}”重复，请修改后再保存。", WarningBrush);
            return;
        }

        DateTime savedAt = DateTime.Now;
        List<(WorkStepProfile WorkStep, DateTime PreviousTime)> modified = WorkSteps
            .Where(workStep => workStep.IsModified)
            .Select(workStep => (workStep, workStep.LastModifiedAt))
            .ToList();

        foreach ((WorkStepProfile workStep, _) in modified)
        {
            workStep.LastModifiedAt = savedAt;
        }

        try
        {
            WorkStepConfigurationStore.Save(WorkSteps);
            foreach ((WorkStepProfile workStep, _) in modified)
            {
                workStep.AcceptChanges(savedAt);
            }
        }
        catch
        {
            foreach ((WorkStepProfile workStep, DateTime previousTime) in modified)
            {
                workStep.LastModifiedAt = previousTime;
                workStep.MarkModified();
            }

            throw;
        }

        SetStatus($"已保存 {WorkSteps.Count} 个业务。", SuccessBrush);
    }

    #endregion

    #region 步骤编辑

    private void OpenNewOperation()
    {
        if (SelectedWorkStep is null)
        {
            return;
        }

        SelectedOperation = null;
        OperationEditor.Open(new WorkStepOperation { OperationObjectName = "System" }, true, SelectedWorkStep.Operations);
        SetStatus("正在新建步骤。", NeutralBrush);
    }

    public void OpenOperationEditorForEdit(WorkStepOperation operation)
    {
        if (SelectedWorkStep is null || !SelectedWorkStep.Operations.Contains(operation))
        {
            return;
        }

        SelectedOperation = operation;
        OperationEditor.Open(operation, false, SelectedWorkStep.Operations);
        SetStatus("正在编辑步骤。", NeutralBrush);
    }

    private void OperationEditor_OperationSaved(object? sender, OperationEditorSavedEventArgs e)
    {
        if (SelectedWorkStep is null)
        {
            return;
        }

        if (e.IsNewOperation)
        {
            SelectedWorkStep.Operations.Add(e.Operation);
            SelectedOperation = e.Operation;
        }
        else if (SelectedOperation is not null)
        {
            int index = SelectedWorkStep.Operations.IndexOf(SelectedOperation);
            if (index >= 0)
            {
                SelectedWorkStep.Operations[index] = e.Operation;
                SelectedOperation = e.Operation;
            }
        }

        RenumberOperations();
        SetStatus("步骤内容已更新，保存业务后生效。", SuccessBrush);
    }

    public void MoveOperation(WorkStepOperation source, WorkStepOperation target, bool insertAfter)
    {
        if (SelectedWorkStep is null)
        {
            return;
        }

        ObservableCollection<WorkStepOperation> operations = SelectedWorkStep.Operations;
        int oldIndex = operations.IndexOf(source);
        int targetIndex = operations.IndexOf(target);
        if (oldIndex < 0 || targetIndex < 0 || oldIndex == targetIndex)
        {
            return;
        }

        int newIndex = targetIndex + (insertAfter ? 1 : 0);
        if (oldIndex < newIndex)
        {
            newIndex--;
        }

        operations.Move(oldIndex, Math.Clamp(newIndex, 0, operations.Count - 1));
        RenumberOperations();
        SelectedOperation = source;
        SetStatus("已调整步骤顺序。", SuccessBrush);
    }

    /// <summary>
    /// 将用户输入的序号解释为步骤在当前业务中的目标位置。
    /// 移动完成后统一连续编号，避免直接排序产生重复序号和位置不确定的问题。
    /// </summary>
    public void MoveOperationToNumber(WorkStepOperation operation)
    {
        if (SelectedWorkStep is null || !SelectedWorkStep.Operations.Contains(operation))
        {
            return;
        }

        ObservableCollection<WorkStepOperation> operations = SelectedWorkStep.Operations;
        int oldIndex = operations.IndexOf(operation);
        int targetIndex = Math.Clamp(operation.Num - 1, 0, operations.Count - 1);

        if (oldIndex != targetIndex)
        {
            operations.Move(oldIndex, targetIndex);
        }

        // 无论是否实际移动，都重新生成连续序号，修复手工输入造成的重复或越界序号。
        RenumberOperations();
        SelectedOperation = operation;
        SetStatus("已按序号重新排列步骤。", SuccessBrush);
    }

    private void DeleteOperations()
    {
        if (SelectedWorkStep is null)
        {
            return;
        }

        List<WorkStepOperation> selected = SelectedWorkStep.Operations.Where(operation => operation.IsChecked).ToList();
        if (selected.Count == 0 && SelectedOperation is not null)
        {
            selected.Add(SelectedOperation);
        }

        foreach (WorkStepOperation operation in selected)
        {
            SelectedWorkStep.Operations.Remove(operation);
        }

        RenumberOperations();
        SelectedOperation = SelectedWorkStep.Operations.FirstOrDefault();
        SetStatus(selected.Count > 0 ? $"已删除 {selected.Count} 个步骤。" : "未选择步骤。", WarningBrush);
    }

    private void CopyOperations()
    {
        List<WorkStepOperation> selected = GetOperationsForClipboard();
        _copiedOperations.Clear();
        _copiedOperations.AddRange(selected.Select(operation => operation.Clone()));
        RaiseCommandStatesChanged();
        SetStatus($"已复制 {selected.Count} 个步骤。", SuccessBrush);
    }

    private void PasteOperations()
    {
        if (SelectedWorkStep is null || _copiedOperations.Count == 0)
        {
            return;
        }

        int insertIndex = SelectedOperation is null
            ? SelectedWorkStep.Operations.Count
            : SelectedWorkStep.Operations.IndexOf(SelectedOperation) + 1;
        foreach (WorkStepOperation source in _copiedOperations)
        {
            WorkStepOperation copy = source.Clone();
            copy.Id = Guid.NewGuid().ToString("N");
            SelectedWorkStep.Operations.Insert(insertIndex++, copy);
            SelectedOperation = copy;
        }

        RenumberOperations();
        SetStatus($"已粘贴 {_copiedOperations.Count} 个步骤。", SuccessBrush);
    }

    private List<WorkStepOperation> GetOperationsForClipboard()
    {
        if (SelectedWorkStep is null)
        {
            return new List<WorkStepOperation>();
        }

        List<WorkStepOperation> checkedOperations = SelectedWorkStep.Operations.Where(operation => operation.IsChecked).ToList();
        return checkedOperations.Count > 0
            ? checkedOperations
            : SelectedOperation is null ? new List<WorkStepOperation>() : new List<WorkStepOperation> { SelectedOperation };
    }

    private void RenumberOperations()
    {
        if (SelectedWorkStep is null)
        {
            return;
        }

        for (int index = 0; index < SelectedWorkStep.Operations.Count; index++)
        {
            SelectedWorkStep.Operations[index].Num = index + 1;
        }

        OnPropertyChanged(nameof(AreAllStepsChecked));
        RaiseCommandStatesChanged();
    }

    #endregion

    #region 搜索与界面辅助

    private bool FilterWorkSteps(object item)
    {
        if (item is not WorkStepProfile workStep || string.IsNullOrWhiteSpace(SearchText))
        {
            return item is WorkStepProfile;
        }

        string keyword = SearchText.Trim();
        return workStep.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               workStep.Operations.Any(operation => operation.Summary.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private string GenerateUniqueName(string baseName)
    {
        string normalizedBaseName = string.IsNullOrWhiteSpace(baseName) ? "业务" : baseName.Trim();
        if (WorkSteps.All(workStep => !string.Equals(workStep.Name, normalizedBaseName, StringComparison.OrdinalIgnoreCase)))
        {
            return normalizedBaseName;
        }

        for (int number = 2; ; number++)
        {
            string candidate = $"{normalizedBaseName} {number}";
            if (WorkSteps.All(workStep => !string.Equals(workStep.Name, candidate, StringComparison.OrdinalIgnoreCase)))
            {
                return candidate;
            }
        }
    }

    private void SetStatus(string text, Brush brush)
    {
        PageStatusText = text;
        PageStatusBrush = brush;
    }

    private void RaiseCommandStatesChanged()
    {
        foreach (ICommand command in new[]
                 {
                     DuplicateWorkStepCommand, DeleteWorkStepCommand, AddStepCommand,
                     CopyStepCommand, PasteStepCommand, DeleteStepCommand
                 })
        {
            if (command is RelayCommand relayCommand)
            {
                relayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    #endregion
}
