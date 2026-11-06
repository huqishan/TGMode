using ControlLibrary;
using Module.Business.Features.OperationEditing.ViewModels;
using Module.Business.Features.OperationEditing.ViewModels.PresentationModels;
using Module.Business.Features.BusinessConfiguration.Services;
using Module.Business.Features.BusinessConfiguration.ViewModels.PresentationModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace Module.Business.Features.BusinessConfiguration.ViewModels;

/// <summary>
/// 业务配置页面视图模型，独立维护业务模板并组合现有步骤编辑器。
/// </summary>
public sealed class BusinessConfigurationViewModel : ViewModelProperties
{
    #region 状态字段

    /// <summary>成功状态提示颜色。</summary>
    private static readonly Brush SuccessBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16A34A"));

    /// <summary>警告状态提示颜色。</summary>
    private static readonly Brush WarningBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EA580C"));

    /// <summary>中性状态提示颜色。</summary>
    private static readonly Brush NeutralBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));

    /// <summary>剪贴板中复制的操作步骤，供粘贴到其他业务。</summary>
    private readonly List<WorkStepOperation> _copiedOperations = new();

    /// <summary>当前选中的业务。</summary>
    private BusinessProfile? _selectedWorkStep;

    /// <summary>当前选中的操作步骤。</summary>
    private WorkStepOperation? _selectedOperation;

    /// <summary>搜索关键字。</summary>
    private string _searchText = string.Empty;

    /// <summary>页面状态提示文案。</summary>
    private string _pageStatusText = "等待编辑";

    /// <summary>页面状态提示颜色。</summary>
    private Brush _pageStatusBrush = NeutralBrush;

    #endregion

    #region 构造与集合

    /// <summary>构造函数：从磁盘加载业务配置、初始化编辑器与命令，并选中首个业务。</summary>
    public BusinessConfigurationViewModel()
    {
        WorkSteps = BusinessConfigurationStore.Load();
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

    /// <summary>业务配置集合。</summary>
    public ObservableCollection<BusinessProfile> WorkSteps { get; }

    /// <summary>业务列表视图，支持按关键字过滤。</summary>
    public ICollectionView WorkStepsView { get; }

    /// <summary>操作步骤编辑器。</summary>
    public OperationEditorViewModel OperationEditor { get; }

    #endregion

    #region 页面属性

    /// <summary>当前选中的业务，变化时同步选中操作并刷新命令状态。</summary>
    public BusinessProfile? SelectedWorkStep
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

    /// <summary>当前选中的操作步骤。</summary>
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

    /// <summary>选中业务的操作步骤集合，供界面绑定。</summary>
    public ObservableCollection<WorkStepOperation>? StepCollection => SelectedWorkStep?.Operations;

    /// <summary>搜索关键字，变化时刷新业务列表过滤。</summary>
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

    /// <summary>页面状态提示文案。</summary>
    public string PageStatusText
    {
        get => _pageStatusText;
        private set => SetField(ref _pageStatusText, value);
    }

    /// <summary>页面状态提示颜色。</summary>
    public Brush PageStatusBrush
    {
        get => _pageStatusBrush;
        private set => SetField(ref _pageStatusBrush, value);
    }

    /// <summary>当前业务操作是否全部勾选，支持表头全选/全不选。</summary>
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

    /// <summary>新建业务命令。</summary>
    public ICommand NewWorkStepCommand { get; }

    /// <summary>复制业务命令。</summary>
    public ICommand DuplicateWorkStepCommand { get; }

    /// <summary>删除业务命令。</summary>
    public ICommand DeleteWorkStepCommand { get; }

    /// <summary>保存全部业务命令。</summary>
    public ICommand SaveWorkStepsCommand { get; }

    /// <summary>新建操作步骤命令。</summary>
    public ICommand AddStepCommand { get; }

    /// <summary>复制操作步骤命令。</summary>
    public ICommand CopyStepCommand { get; }

    /// <summary>粘贴操作步骤命令。</summary>
    public ICommand PasteStepCommand { get; }

    /// <summary>删除操作步骤命令。</summary>
    public ICommand DeleteStepCommand { get; }

    #endregion

    #region 业务管理

    /// <summary>新建业务：以不重复的默认名称加入集合并选中。</summary>
    private void NewWorkStep()
    {
        BusinessProfile workStep = new() { Name = GenerateUniqueName("业务") };
        WorkSteps.Add(workStep);
        SearchText = string.Empty;
        WorkStepsView.Refresh();
        SelectedWorkStep = workStep;
        SetStatus("已新增业务，保存后生效。", SuccessBrush);
    }

    /// <summary>复制当前业务：深拷贝并重新生成业务、操作与参数的唯一标识。</summary>
    private void DuplicateWorkStep()
    {
        if (SelectedWorkStep is null)
        {
            return;
        }

        BusinessProfile copy = SelectedWorkStep.Clone();
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

    /// <summary>删除当前选中业务，并就近选中相邻业务。</summary>
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

    /// <summary>保存全部业务：先校验名称唯一性，统一刷新修改时间后写盘，失败时回滚时间与修改标记。</summary>
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
        List<(BusinessProfile WorkStep, DateTime PreviousTime)> modified = WorkSteps
            .Where(workStep => workStep.IsModified)
            .Select(workStep => (workStep, workStep.LastModifiedAt))
            .ToList();

        foreach ((BusinessProfile workStep, _) in modified)
        {
            workStep.LastModifiedAt = savedAt;
        }

        try
        {
            BusinessConfigurationStore.Save(WorkSteps);
            foreach ((BusinessProfile workStep, _) in modified)
            {
                workStep.AcceptChanges(savedAt);
            }
        }
        catch
        {
            foreach ((BusinessProfile workStep, DateTime previousTime) in modified)
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

    /// <summary>新建操作步骤：以空白操作打开编辑抽屉。</summary>
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

    /// <summary>编辑既有操作步骤：以该操作打开编辑抽屉（供界面双击调用）。</summary>
    /// <param name="operation">待编辑的操作步骤。</param>
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

    /// <summary>编辑器保存回调：新增则追加，编辑则替换原位置，随后统一重新编号。</summary>
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

    /// <summary>拖拽移动操作步骤到目标位置（目标行前/后），完成后重新编号。</summary>
    /// <param name="source">拖拽的操作步骤。</param>
    /// <param name="target">目标操作步骤。</param>
    /// <param name="insertAfter">是否插入到目标之后。</param>
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

    /// <summary>删除勾选的操作步骤；无勾选时删除当前选中步骤，完成后重新编号。</summary>
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

    /// <summary>复制勾选（或当前选中）的操作步骤到内部剪贴板。</summary>
    private void CopyOperations()
    {
        List<WorkStepOperation> selected = GetOperationsForClipboard();
        _copiedOperations.Clear();
        _copiedOperations.AddRange(selected.Select(operation => operation.Clone()));
        RaiseCommandStatesChanged();
        SetStatus($"已复制 {selected.Count} 个步骤。", SuccessBrush);
    }

    /// <summary>粘贴剪贴板中的操作步骤：逐个克隆并重新生成标识，插入到当前步骤之后。</summary>
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

    /// <summary>获取参与复制/剪贴的操作步骤：优先取勾选项，无勾选时取当前选中项。</summary>
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

    /// <summary>按集合顺序重新生成连续序号，并刷新命令状态。</summary>
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

    /// <summary>业务列表过滤：按名称或操作摘要匹配搜索关键字（忽略大小写）。</summary>
    private bool FilterWorkSteps(object item)
    {
        if (item is not BusinessProfile workStep || string.IsNullOrWhiteSpace(SearchText))
        {
            return item is BusinessProfile;
        }

        string keyword = SearchText.Trim();
        return workStep.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               workStep.Operations.Any(operation => operation.Summary.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>生成不与现有业务重名的名称：重名时追加递增序号。</summary>
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

    /// <summary>更新页面状态提示的文案与颜色。</summary>
    private void SetStatus(string text, Brush brush)
    {
        PageStatusText = text;
        PageStatusBrush = brush;
    }

    /// <summary>统一刷新各命令的可用状态。</summary>
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
