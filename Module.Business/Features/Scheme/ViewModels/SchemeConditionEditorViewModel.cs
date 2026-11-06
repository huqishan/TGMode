using ControlLibrary;
using Module.Business.Features.OperationEditing.ViewModels.PresentationModels;
using Module.Business.Features.Scheme.ViewModels.PresentationModels;
using Module.Business.Features.BusinessConfiguration.ViewModels.PresentationModels;
using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Threading.Tasks;

namespace Module.Business.Features.Scheme.ViewModels;

/// <summary>
/// 方案判断条件编辑器，按业务连续展示并统一编辑方案业务的输入参数和返回参数。
/// 本编辑器只回写内存中的方案业务参数，不负责方案持久化。
/// </summary>
public sealed class SchemeConditionEditorViewModel : ViewModelProperties
{
    #region 私有字段

    private readonly List<SchemeConditionWorkStepGroup> _allGroups = new();
    private SchemeProfile? _editingScheme;
    private bool _isOpen;
    private string _searchText = string.Empty;
    private string _selectedParameterFilter = "全部参数";

    #endregion

    #region 构造与集合

    public SchemeConditionEditorViewModel()
    {
        ParameterFilters = new ObservableCollection<string> { "全部参数", "输入参数", "返回参数" };
        JudgeOperators = new ObservableCollection<string>
        {
            "NA", "=", "≠", ">", "≥", "<", "≤", "＜{0}＜", "≤{0}≤", "()", "!()", "黑名单", "白名单"
        };
        SaveCommand = new RelayCommand(_ => Save());
        CloseCommand = new RelayCommand(_ => Close());
    }

    /// <summary>当前筛选后显示的业务分组。</summary>
    public ObservableCollection<SchemeConditionWorkStepGroup> Groups { get; } = new();

    /// <summary>参数类型固定筛选集合。</summary>
    public ObservableCollection<string> ParameterFilters { get; }

    /// <summary>返回参数判断符号固定集合。</summary>
    public ObservableCollection<string> JudgeOperators { get; }

    #endregion

    #region 展示状态与命令

    public bool IsOpen { get => _isOpen; private set => SetField(ref _isOpen, value); }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetField(ref _searchText, value ?? string.Empty)) RefreshGroups();
        }
    }

    public string SelectedParameterFilter
    {
        get => _selectedParameterFilter;
        set
        {
            if (SetField(ref _selectedParameterFilter, value ?? "全部参数")) RefreshGroups();
        }
    }

    public string Title => _editingScheme is null ? "判断条件配置" : $"判断条件配置 · {_editingScheme.SchemeName}";

    public int WorkStepCount => _allGroups.Count;

    public int InputParameterCount => _allGroups.Sum(group => group.Items.Count(item => item.IsInputParameter));

    public int ReturnParameterCount => _allGroups.Sum(group => group.Items.Count(item => item.IsReturnParameter));

    public ICommand SaveCommand { get; }

    public ICommand CloseCommand { get; }

    /// <summary>
    /// 判断条件编辑结果已经回写方案业务参数时触发，供方案配置页面刷新当前参数副本。
    /// </summary>
    public event Action<SchemeProfile>? ParametersSaved;

    #endregion

    #region 打开、保存与关闭

    /// <summary>
    /// 打开编辑器并从方案业务实例生成连续分组参数行。
    /// </summary>
    public async Task OpenAsync(SchemeProfile scheme, IReadOnlyCollection<BusinessProfile> configuredWorkSteps)
    {
        ArgumentNullException.ThrowIfNull(scheme);
        ArgumentNullException.ThrowIfNull(configuredWorkSteps);

        // 先把每个方案业务与最新内置业务参数对齐：同名参数保留方案已编辑值，新参数按配置默认值补入，
        // 已从内置业务删除的遗留参数继续保留但标记为停用，因此不会进入判断条件配置界面。
        foreach (SchemeWorkStepItem workStep in scheme.Steps)
        {
            BusinessProfile? configuredWorkStep = configuredWorkSteps.FirstOrDefault(item => string.Equals(
                item.Name,
                workStep.StepType,
                StringComparison.OrdinalIgnoreCase));
            if (configuredWorkStep is null)
            {
                foreach (SchemeWorkStepParameterItem parameter in workStep.InputParameters) parameter.IsUsed = false;
                foreach (SchemeWorkStepParameterItem parameter in workStep.ReturnParameters) parameter.IsUsed = false;
                continue;
            }

            List<SchemeWorkStepParameterItem> latestInputs = configuredWorkStep.Operations
                .OrderBy(operation => operation.Num)
                .SelectMany(operation => operation.Parameters.OrderBy(parameter => parameter.Num))
                .Where(parameter => string.Equals(parameter.ParameterType?.Trim(), "业务值", StringComparison.Ordinal))
                .DistinctBy(parameter => parameter.Value?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .Select(parameter => new SchemeWorkStepParameterItem
                {
                    Name = parameter.Value,
                    Value = parameter.ParameterName,
                    IsUsed = true
                })
                .ToList();
            HashSet<string> latestInputNames = new(
                latestInputs.Select(parameter => parameter.Name?.Trim() ?? string.Empty),
                StringComparer.OrdinalIgnoreCase);
            foreach (SchemeWorkStepParameterItem latestParameter in latestInputs)
            {
                SchemeWorkStepParameterItem? savedParameter = workStep.InputParameters.FirstOrDefault(parameter =>
                    string.Equals(parameter.Name?.Trim(), latestParameter.Name?.Trim(), StringComparison.OrdinalIgnoreCase));
                if (savedParameter is null)
                {
                    workStep.InputParameters.Add(latestParameter);
                }
                else
                {
                    savedParameter.IsUsed = true;
                }
            }

            foreach (SchemeWorkStepParameterItem parameter in workStep.InputParameters.Where(parameter =>
                         !latestInputNames.Contains(parameter.Name?.Trim() ?? string.Empty)))
            {
                parameter.IsUsed = false;
            }

            List<SchemeWorkStepParameterItem> latestReturns = configuredWorkStep.Operations
                .OrderBy(operation => operation.Num)
                .SelectMany(operation => operation.ReturnValues
                    .Where(returnValue => returnValue.IsShowView)
                    .OrderBy(returnValue => returnValue.Num)
                    .Select(returnValue => new SchemeWorkStepParameterItem
                    {
                        Name = string.IsNullOrWhiteSpace(operation.ReturnValue)
                            ? returnValue.ReturnParameterName
                            : $"{operation.ReturnValue}_{returnValue.ReturnParameterName}",
                        Value = string.IsNullOrWhiteSpace(operation.ReturnValue)
                            ? returnValue.ReturnParameterName
                            : $"{operation.ReturnValue}_{returnValue.ReturnParameterName}",
                        Unit = returnValue.Unit,
                        IsUsed = true
                    }))
                .DistinctBy(parameter => parameter.Value?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToList();
            HashSet<string> latestReturnNames = new(
                latestReturns.Select(parameter => parameter.Value?.Trim() ?? string.Empty),
                StringComparer.OrdinalIgnoreCase);
            foreach (SchemeWorkStepParameterItem latestParameter in latestReturns)
            {
                SchemeWorkStepParameterItem? savedParameter = workStep.ReturnParameters.FirstOrDefault(parameter =>
                    string.Equals(parameter.Value?.Trim(), latestParameter.Value?.Trim(), StringComparison.OrdinalIgnoreCase));
                if (savedParameter is null)
                {
                    workStep.ReturnParameters.Add(latestParameter);
                }
                else
                {
                    savedParameter.IsUsed = true;
                }
            }

            foreach (SchemeWorkStepParameterItem parameter in workStep.ReturnParameters.Where(parameter =>
                         !latestReturnNames.Contains(parameter.Value?.Trim() ?? string.Empty)))
            {
                parameter.IsUsed = false;
            }
        }

        List<SchemeConditionWorkStepGroup> preparedGroups = await Task.Run(() =>
        {
            // 数据匹配与行模型创建放到后台线程，主线程只负责最终集合绑定和弹框首帧布局。
            List<SchemeConditionWorkStepGroup> groups = new();
            foreach (SchemeWorkStepItem workStep in scheme.Steps.OrderBy(item => item.Num))
            {
                BusinessProfile? configuredWorkStep = configuredWorkSteps.FirstOrDefault(item => string.Equals(
                    item.Name,
                    workStep.StepType,
                    StringComparison.OrdinalIgnoreCase));
                IReadOnlyList<WorkStepOperation> operations = configuredWorkStep is null
                    ? Array.Empty<WorkStepOperation>()
                    : configuredWorkStep.Operations;
                List<InputParameter> configuredInputs = operations
                    .OrderBy(operation => operation.Num)
                    .SelectMany(operation => operation.Parameters.OrderBy(parameter => parameter.Num))
                    .Where(parameter => string.Equals(parameter.ParameterType?.Trim(), "业务值", StringComparison.Ordinal))
                    .DistinctBy(parameter => parameter.Value?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                List<SchemeConditionEditorItem> rows = new();
                foreach (InputParameter configuredInput in configuredInputs)
                {
                    string parameterName = configuredInput.Value?.Trim() ?? string.Empty;
                    SchemeWorkStepParameterItem? savedParameter = workStep.InputParameters.FirstOrDefault(parameter =>
                        string.Equals(parameter.Name?.Trim(), parameterName, StringComparison.OrdinalIgnoreCase));
                    if (savedParameter?.IsUsed == true)
                    {
                        rows.Add(new SchemeConditionEditorItem(savedParameter.Clone(), true));
                    }
                }

                List<string> configuredReturns = operations
                    .OrderBy(operation => operation.Num)
                    .SelectMany(operation => operation.ReturnValues
                        .Where(returnValue => returnValue.IsShowView)
                        .OrderBy(returnValue => returnValue.Num)
                        .Select(returnValue =>
                            string.IsNullOrWhiteSpace(operation.ReturnValue)
                                ? returnValue.ReturnParameterName
                                : $"{operation.ReturnValue}_{returnValue.ReturnParameterName}"))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (string fullName in configuredReturns)
                {
                    SchemeWorkStepParameterItem? savedParameter = workStep.ReturnParameters.FirstOrDefault(parameter =>
                        string.Equals(parameter.Value?.Trim(), fullName.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (savedParameter?.IsUsed == true)
                    {
                        rows.Add(new SchemeConditionEditorItem(savedParameter.Clone(), false));
                    }
                }

                groups.Add(new SchemeConditionWorkStepGroup(workStep, rows));
            }

            return groups;
        });

        _editingScheme = scheme;
        _allGroups.Clear();
        _allGroups.AddRange(preparedGroups);

        _searchText = string.Empty;
        _selectedParameterFilter = "全部参数";
        OnPropertyChanged(nameof(SearchText));
        OnPropertyChanged(nameof(SelectedParameterFilter));
        RefreshGroups();
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(WorkStepCount));
        OnPropertyChanged(nameof(InputParameterCount));
        OnPropertyChanged(nameof(ReturnParameterCount));
        IsOpen = true;
    }

    /// <summary>
    /// 将编辑行统一回写到对应方案业务参数集合，不执行方案持久化。
    /// </summary>
    private void Save()
    {
        if (_editingScheme is null) return;

        foreach (SchemeConditionWorkStepGroup group in _allGroups)
        {
            foreach (SchemeConditionEditorItem item in group.Items)
            {
                SchemeWorkStepParameterItem? target = item.IsInputParameter
                    ? group.Source.InputParameters.FirstOrDefault(parameter => string.Equals(
                        parameter.Name?.Trim(),
                        item.ParameterName.Trim(),
                        StringComparison.OrdinalIgnoreCase))
                    : group.Source.ReturnParameters.FirstOrDefault(parameter => string.Equals(
                        parameter.Value?.Trim(),
                        item.ParameterName.Trim(),
                        StringComparison.OrdinalIgnoreCase));
                if (target is null)
                {
                    continue;
                }

                if (item.IsInputParameter)
                {
                    target.Value = item.EditableValue.Trim();
                }
                else
                {
                    target.Unit = item.Unit.Trim();
                    target.Name = item.EditableValue.Trim();
                    target.Operator = item.Operator;
                    target.JudgeValue = item.JudgeValue.Trim();
                }
            }

        }

        ParametersSaved?.Invoke(_editingScheme);
        Close();
    }

    public void Close()
    {
        IsOpen = false;
        _editingScheme = null;
        OnPropertyChanged(nameof(Title));
    }

    #endregion

    #region 筛选与显示状态

    /// <summary>
    /// 按搜索文字、参数类型和判断条件筛选重建显示分组。
    /// </summary>
    private void RefreshGroups()
    {
        Groups.Clear();
        string keyword = SearchText.Trim();
        foreach (SchemeConditionWorkStepGroup group in _allGroups)
        {
            List<SchemeConditionEditorItem> visibleRows = group.Items.Where(item =>
                    (SelectedParameterFilter == "全部参数" || item.ParameterType == SelectedParameterFilter) &&
                    (string.IsNullOrWhiteSpace(keyword) ||
                     group.WorkStepName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                     item.ParameterName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                     item.EditableValue.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (visibleRows.Count > 0) Groups.Add(new SchemeConditionWorkStepGroup(group.Source, visibleRows));
        }
    }

    #endregion
}
