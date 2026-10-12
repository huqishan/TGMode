using ControlLibrary;
using ControlLibrary.Controls.MessageDialog;
using Module.Business.Features.OperationEditing.Models;
using Module.Business.Features.OperationEditing.Services;
using Module.Business.Features.OperationEditing.ViewModels.PresentationModels;
using Module.Business.Features.Scheme.ViewModels.PresentationModels;
using Module.Business.Models;
using Module.Business.Services.BusinessOperations;
using Shared.Infrastructure.Lua;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace Module.Business.Features.OperationEditing.ViewModels;

/// <summary>
/// 步骤操作编辑器视图模型。
/// 编辑弹框的临时状态、参数集合和返回参数集合均由该类独立持有，方案页面只接收最终保存结果。
/// </summary>
public sealed class OperationEditorViewModel : ViewModelProperties
{
    #region 私有字段

    // 记录已订阅属性变化的返回参数，编辑对象切换时用于完整解除事件，避免旧对象继续影响当前弹框。
    private readonly HashSet<ReturnValue> _trackedReturnParameters = new();
    private InputParameter? _selectedEditingInvokeParameter;
    private ReturnValue? _selectedEditingReturnParameter;
    private bool _isInitializingOperationDrawer;
    private WorkStepOperation _editingOperation = new();
    private bool _isNewOperation;
    private bool _isOpen;
    // 返回值候选按来源分别保存，刷新界面时再统一去重，避免方案上下文与外部上下文互相覆盖。
    private readonly List<string> _parameterReturnValueOptions = new();
    private readonly List<string> _workStepValueOptions = new();
    private readonly List<string> _externalReturnValueOptions = new();
    private bool _isRefreshingMetadata;
    // 保留当前宿主步骤集合引用，连续新增或修改后可基于最新集合重新计算返回值名称候选。
    private IEnumerable<WorkStepOperation>? _operationContext;

    #endregion

    #region 构造与集合

    /// <summary>
    /// 初始化步骤编辑器命令、编辑副本监听及固定候选数据。
    /// </summary>
    public OperationEditorViewModel()
    {
        AttachEditingOperation(_editingOperation);
        SaveCommand = new RelayCommand(_ => Save());
        CloseCommand = new RelayCommand(_ => Close());
        AddParametersCommand = new RelayCommand(_ => AddParameters());
        DeleteParametersCommand = new RelayCommand(_ => DeleteParameters());
        RefreshOperationObjectOptions();
        RefreshLuaScriptTemplateOptions();
    }

    public ObservableCollection<string> OperationObjectOptions { get; } = new();

    public ObservableCollection<string> LuaScriptTemplateOptions { get; } = new();

    public ObservableCollection<StationOperationMethodItem> StationOperationMethodCollection { get; } = new();

    /// <summary>
    /// 输入参数的界面编辑行；业务数据仍以 EditingOperation.Parameters 为唯一保存来源。
    /// </summary>
    public ObservableCollection<InputParameterEditorItem> EditingParameterRows { get; } = new();

    /// <summary>
    /// 返回值类型的统一候选集合，输入参数和条件执行左右参数共用同一实例。
    /// </summary>
    public ObservableCollection<string> ParameterReturnValueOptions { get; } = new();

    /// <summary>
    /// 工步值类型的统一候选集合，输入参数和条件执行左右参数共用同一实例。
    /// </summary>
    public ObservableCollection<string> WorkStepValueOptions { get; } = new();

    public ObservableCollection<string> ParameterTypeOptions { get; } = new()
    {
        "设置值",
        "返回值",
        "工步值",
        "全局值",
    };

    /// <summary>
    /// 条件执行界面与判断操作共同使用的固定关系符。
    /// </summary>
    public ObservableCollection<string> JudgmentConditionOptions { get; } = new()
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

    public ObservableCollection<string> ProtocolOptions { get; } = new();

    public ObservableCollection<string> CommandOptions { get; } = new();

    public ObservableCollection<string> InvokeMethodOptions { get; } = new();

    public ObservableCollection<string> InvokeMethodRemarkOptions { get; } = new();

    public ObservableCollection<string> ReturnValueOptions { get; } = new();

    public bool HasVisibleReturnValueName => EditingOperation.ReturnValues.Any(item => item.IsShowView);

    #endregion

    #region 编辑状态

    /// <summary>
    /// 当前业务编辑副本；弹框取消时不会污染方案中的原步骤。
    /// </summary>
    public WorkStepOperation EditingOperation
    {
        get => _editingOperation;
        private set
        {
            if (ReferenceEquals(_editingOperation, value))
            {
                return;
            }

            DetachEditingOperation(_editingOperation);
            _editingOperation = value;
            AttachEditingOperation(_editingOperation);
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasVisibleReturnValueName));
        }
    }

    public InputParameter? SelectedEditingInvokeParameter { get => _selectedEditingInvokeParameter; set => SetEditorField(ref _selectedEditingInvokeParameter, value); }

    public ReturnValue? SelectedEditingReturnParameter { get => _selectedEditingReturnParameter; set => SetEditorField(ref _selectedEditingReturnParameter, value); }

    public bool IsInitializingOperationDrawer
    {
        get => _isInitializingOperationDrawer;
        internal set => SetEditorField(ref _isInitializingOperationDrawer, value);
    }

    public bool IsLuaOperationSelected => IsLuaOperationObject(EditingOperation.OperationObjectName);

    public bool IsSystemOperationSelected => IsSystemOperationObject(EditingOperation.OperationObjectName);

    public bool IsOpen
    {
        get => _isOpen;
        private set
        {
            if (_isOpen != value)
            {
                SetEditorField(ref _isOpen, value);
                OnPropertyChanged(nameof(Title));
                OnPropertyChanged(nameof(BtnTitle));
            }
        }
    }

    public string Title => _isNewOperation ? "新建步骤" : "编辑步骤";
    public string BtnTitle => _isNewOperation ? "新建" : "保存";
    public ICommand SaveCommand { get; }

    public ICommand CloseCommand { get; }
    public ICommand AddParametersCommand { get; }

    public ICommand DeleteParametersCommand { get; }
    #endregion

    #region 编辑器事件

    /// <summary>
    /// 编辑状态变化通知；方案层仅在确实依赖方案上下文时响应。
    /// </summary>
    public event PropertyChangedEventHandler? EditorStateChanged;

    /// <summary>
    /// 编辑器生成最终操作后的保存通知。
    /// </summary>
    public event EventHandler<OperationEditorSavedEventArgs>? OperationSaved;

    private void SetEditorField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (!SetField(ref field, value, propertyName))
        {
            return;
        }

        EditorStateChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        RefreshMetadataForProperty(propertyName);
    }

    /// <summary>
    /// 订阅业务编辑副本及其参数集合变化，将实体属性变化转换为编辑器联动通知。
    /// </summary>
    private void AttachEditingOperation(WorkStepOperation operation)
    {
        operation.PropertyChanged += EditingOperation_PropertyChanged;
        operation.Parameters.CollectionChanged += EditingInvokeParameters_CollectionChanged;
        operation.ReturnValues.CollectionChanged += EditingReturnParameters_CollectionChanged;

        // 返回参数的显示状态影响界面列展示，已有项需要在编辑副本接入时同步订阅。
        foreach (ReturnValue returnValue in operation.ReturnValues)
        {
            if (_trackedReturnParameters.Add(returnValue))
            {
                returnValue.PropertyChanged += EditingReturnParameter_PropertyChanged;
            }
        }

        SynchronizeEditingParameterRows();
    }

    /// <summary>
    /// 解除旧编辑副本及其参数项的事件订阅，避免对象切换后残留联动。
    /// </summary>
    private void DetachEditingOperation(WorkStepOperation operation)
    {
        operation.PropertyChanged -= EditingOperation_PropertyChanged;
        operation.Parameters.CollectionChanged -= EditingInvokeParameters_CollectionChanged;
        operation.ReturnValues.CollectionChanged -= EditingReturnParameters_CollectionChanged;

        // 编辑对象切换时同步解除返回参数订阅，避免旧步骤继续影响当前编辑器。
        foreach (ReturnValue returnValue in _trackedReturnParameters)
        {
            returnValue.PropertyChanged -= EditingReturnParameter_PropertyChanged;
        }

        _trackedReturnParameters.Clear();
    }

    /// <summary>
    /// 响应编辑步骤属性变化，并刷新相关选择状态和方法元数据。
    /// </summary>
    private void EditingOperation_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkStepOperation.Parameters))
        {
            return;
        }

        if (e.PropertyName == nameof(WorkStepOperation.ReturnValues))
        {
            OnPropertyChanged(nameof(HasVisibleReturnValueName));
            return;
        }

        if (e.PropertyName is not (nameof(WorkStepOperation.OperationObjectName)
            or nameof(WorkStepOperation.PCommandName)
            or nameof(WorkStepOperation.ReturnValue)
            or nameof(WorkStepOperation.LuaScript)
            or nameof(WorkStepOperation.Summary)))
        {
            return;
        }

        OnPropertyChanged(e.PropertyName);

        if (e.PropertyName == nameof(WorkStepOperation.OperationObjectName))
        {
            OnPropertyChanged(nameof(IsLuaOperationSelected));
            OnPropertyChanged(nameof(IsSystemOperationSelected));
        }

        EditorStateChanged?.Invoke(this, new PropertyChangedEventArgs(e.PropertyName));
        RefreshMetadataForProperty(e.PropertyName);
    }

    #endregion

    #region 模板与方法应用

    #region Command
    /// <summary>
    /// 将当前编辑状态一次性构建成最终业务实体并发布，编辑器不直接修改方案集合。
    /// </summary>
    public void Save()
    {
        ObservableCollection<ReturnValue> luaReturnValues = new();
        if (IsLuaOperationSelected)
        {
            IReadOnlyList<string> returnValueNames = LuaExecutionHelper.GetReturnValueNamesFromScript(EditingOperation.LuaScript);
            luaReturnValues = new ObservableCollection<ReturnValue>(returnValueNames
                .Select((name, index) => new ReturnValue { Num = index + 1, ReturnParameterName = name }));
        }

        // 返回值键只有在指定返回值集合名后才能形成可供后续步骤引用的完整名称。
        // Lua 脚本中未解析到返回键时不要求填写集合名。
        bool hasReturnValueKey = (IsLuaOperationSelected ? luaReturnValues : EditingOperation.ReturnValues)
            .Any(returnValue => !string.IsNullOrWhiteSpace(returnValue.ReturnParameterName));
        if (hasReturnValueKey && string.IsNullOrWhiteSpace(EditingOperation.ReturnValue))
        {
            MessageDialog.Show(
                "当前步骤存在返回值键，请先填写返回值集合名称。",
                "保存步骤",
                MessageDialogButtons.Ok,
                MessageDialogIcon.Warning);
            return;
        }

        WorkStepOperation result = EditingOperation.Clone();
        result.OperationObjectName = result.OperationObjectName.Trim();
        result.PCommandName = IsLuaOperationSelected ? "Lua" : result.PCommandName.Trim();
        result.ReturnValue = result.ReturnValue.Trim();
        result.LuaScript = result.LuaScript;
        result.Summary = result.Summary.Trim();
        result.Parameters = new ObservableCollection<InputParameter>(EditingOperation.Parameters.OrderBy(parameter => parameter.Num).Select(parameter => parameter.Clone()));
        result.ReturnValues = IsLuaOperationSelected
            ? luaReturnValues
            : new ObservableCollection<ReturnValue>(EditingOperation.ReturnValues.OrderBy(parameter => parameter.Num).Select(parameter => parameter.Clone()));

        OperationSaved?.Invoke(this, new OperationEditorSavedEventArgs(result, _isNewOperation));
        EditingOperation = result.Clone();
        if (_isNewOperation)
        {
            // 连续新增时当前保存结果已经成为前置步骤，新草稿使用新编号，使上下文包含刚保存的返回值。
            EditingOperation.Id = Guid.NewGuid().ToString("N");
        }
        foreach (InputParameter parameter in EditingOperation.Parameters)
        {
            parameter.Id = Guid.NewGuid().ToString("N");
        }

        // 保存事件会先由宿主把新增或修改结果写回步骤集合，此时重新读取上下文即可取得最新返回值名称。
        // 修改步骤时保留原步骤编号用于定位，只读取当前步骤之前的数据；连续新增则读取全部已保存步骤。
        // 可编辑 ComboBox 在候选集合刷新期间可能暂时失去选中项，并通过 Text 双向绑定回写空值。
        // 保存前后明确保护条件左右值，候选刷新不得改变用户已编辑的业务数据。
        string conditionLeftValue = EditingOperation.ConditionExecution.LeftValue;
        string conditionRightValue = EditingOperation.ConditionExecution.RightValue;
        RefreshOperationContext(EditingOperation, _operationContext);
        EditingOperation.ConditionExecution.LeftValue = conditionLeftValue;
        EditingOperation.ConditionExecution.RightValue = conditionRightValue;
        SelectedEditingInvokeParameter = EditingOperation.Parameters.FirstOrDefault();
        SelectedEditingReturnParameter = EditingOperation.ReturnValues.FirstOrDefault();
    }

    /// <summary>
    /// 根据当前输入参数生成 Lua 脚本执行前缀，编辑器预览和保存时使用相同格式。
    /// </summary>
    public string BuildLuaExecutionPrefixScript()
    {
        return string.Join(
            Environment.NewLine,
            EditingOperation.Parameters
                .OrderBy(parameter => parameter.Num)
                .Where(parameter => !string.IsNullOrWhiteSpace(parameter.ParameterName))
                .Select(parameter =>
                    $"{parameter.ParameterName.Trim()}={LuaExecutionHelper.ToLuaStringLiteral(parameter.Value?.Trim() ?? string.Empty)}"));
    }

    /// <summary>
    /// 关闭编辑器并清理当前编辑副本和选中状态。
    /// </summary>
    public void Close()
    {
        IsOpen = false;
        EditingOperation = new WorkStepOperation();
        _isNewOperation = false;
        _operationContext = null;
        EditingOperation.Parameters.Clear();
        EditingOperation.ReturnValues.Clear();
        SelectedEditingInvokeParameter = null;
        SelectedEditingReturnParameter = null;
    }

    /// <summary>
    /// 添加输入参数。
    /// </summary>
    public void AddParameters()
    {
        EditingOperation.Parameters.Add(new InputParameter
        {
            Num = EditingOperation.Parameters.Count + 1,
            ParameterName = $"data{EditingOperation.Parameters.Count + 1}",
            ParameterType = ParameterTypeOptions.First(),
        });
    }

    /// <summary>
    /// 删除输入参数。
    /// </summary>
    public void DeleteParameters()
    {
        EditingOperation.Parameters.Remove(SelectedEditingInvokeParameter);
    }
    #endregion

    #region 操作元数据与默认参数

    /// <summary>
    /// 在设备支持的协议范围内查找指定指令及其所属协议。
    /// </summary>
    private static bool TryFindDeviceCommand(string operationObject, string invokeMethod, out string protocolName, out string commandName)
    {
        protocolName = string.Empty;
        commandName = string.Empty;
        HashSet<string> allowedProtocols = new(
            OperationConfigurationStore.LoadDeviceSupportedProtocolNames(operationObject),
            StringComparer.OrdinalIgnoreCase);
        foreach (ProtocolSelectionItem protocol in OperationConfigurationStore.LoadProtocolSelectionItems()
                     .Where(protocol => allowedProtocols.Contains(protocol.Name)))
        {
            ProtocolCommandSelectionItem? command = protocol.Commands.FirstOrDefault(item =>
                string.Equals(item.Name?.Trim(), invokeMethod?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (command is null)
            {
                continue;
            }

            protocolName = protocol.Name;
            commandName = command.Name;
            return true;
        }

        return false;
    }

    /// <summary>
    /// 根据步骤操作类型和调用方法创建默认输入参数集合。
    /// </summary>
    public ObservableCollection<InputParameter> CreateDefaultOperationParameters(WorkStepOperation operation)
    {
        if (operation is null || IsLuaOperationObject(operation.OperationObjectName))
        {
            return new ObservableCollection<InputParameter>();
        }

        string operationObject = operation.OperationObjectName?.Trim() ?? string.Empty;
        string invokeMethod = operation.PCommandName?.Trim() ?? string.Empty;
        if (IsSystemOperationObject(operationObject))
        {
            BusinessOperationDescriptor? systemOperation = BusinessOperationCatalog.GetOperations("System")
                .FirstOrDefault(method => string.Equals(method.OperationId, invokeMethod, StringComparison.OrdinalIgnoreCase));
            return systemOperation is null
                ? new ObservableCollection<InputParameter>()
                : new ObservableCollection<InputParameter>(systemOperation.Parameters
                    .OrderBy(parameter => parameter.Sequence)
                    .Select(parameter => new InputParameter
                    {
                        Num = parameter.Sequence,
                        ParameterType = ParameterTypeOptions.First(),
                        ParameterName = parameter.Name,
                        Value = parameter.DefaultValue,
                        Description = string.IsNullOrWhiteSpace(parameter.Description) ? parameter.DisplayName : parameter.Description
                    }));
        }

        BusinessOperationDescriptor? businessOperation = BusinessOperationBindingResolver.FindOperationForOperationObject(operationObject, null, invokeMethod);
        if (businessOperation is not null)
        {
            return CreateOperationParametersFromBusinessOperation(businessOperation);
        }

        if (TryFindDeviceCommand(operationObject, invokeMethod, out string protocolName, out string commandName))
        {
            ObservableCollection<InputParameter> parameters = new();
            int num = 1;
            foreach (ProtocolPlaceholderDefinition placeholder in OperationConfigurationStore.LoadProtocolCommandPlaceholders(protocolName, commandName))
            {
                parameters.Add(new InputParameter
                {
                    Num = num++,
                    ParameterType = ParameterTypeOptions.First(),
                    ParameterName = placeholder.Name,
                    Value = placeholder.Value,
                    Description = placeholder.Name
                });
            }

            return parameters;
        }

        return new ObservableCollection<InputParameter>();
    }

    /// <summary>
    /// 将业务方法参数定义转换为步骤编辑器使用的输入参数集合。
    /// </summary>
    private ObservableCollection<InputParameter> CreateOperationParametersFromBusinessOperation(BusinessOperationDescriptor operation)
    {
        return new ObservableCollection<InputParameter>(operation.Parameters.OrderBy(parameter => parameter.Sequence).Select(parameter => new InputParameter
        {
            Num = parameter.Sequence,
            ParameterType = ParameterTypeOptions.First(),
            ParameterName = parameter.Name,
            Value = parameter.DefaultValue,
            Description = string.IsNullOrWhiteSpace(parameter.Description) ? parameter.DisplayName : parameter.Description
        }));
    }

    /// <summary>
    /// 判断操作对象是否表示系统方法。
    /// </summary>
    public static bool IsSystemOperationObject(string? operationObject)
    {
        return string.Equals(operationObject?.Trim(), "System", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(operationObject?.Trim(), "系统", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 判断操作对象是否表示 Lua 脚本。
    /// </summary>
    public static bool IsLuaOperationObject(string? operationObject)
    {
        return string.Equals(operationObject?.Trim(), "Lua", StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    /// <summary>
    /// 设置宿主提供的外部返回值候选，并刷新参数可选值。
    /// </summary>
    public void SetExternalReturnValueOptions(IEnumerable<string>? values)
    {
        _externalReturnValueOptions.Clear();
        _externalReturnValueOptions.AddRange((values ?? Enumerable.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase));
        ReplaceStringOptions(ReturnValueOptions, _parameterReturnValueOptions
            .Concat(_externalReturnValueOptions)
            .Concat(new[] { EditingOperation.ReturnValue }));
        ReplaceStringOptions(ParameterReturnValueOptions, _parameterReturnValueOptions.Concat(_externalReturnValueOptions));
    }

    /// <summary>
    /// 编辑字段变化时在编辑器内部刷新候选项和默认参数，避免方案 ViewModel 承担弹框联动。
    /// </summary>
    private void RefreshMetadataForProperty(string? propertyName)
    {
        if (_isRefreshingMetadata || IsInitializingOperationDrawer)
        {
            return;
        }

        _isRefreshingMetadata = true;
        try
        {
            if (propertyName == nameof(EditingOperation.OperationObjectName))
            {
                RefreshProtocolAndMethodOptions();
            }
            else if (propertyName == nameof(EditingOperation.PCommandName))
            {
                if (!IsSystemOperationSelected && !IsLuaOperationSelected)
                {
                    RefreshProtocolCommandParameters();
                }
                else
                {
                    RefreshSelectedMethodParameters();
                }
            }
        }
        finally
        {
            _isRefreshingMetadata = false;
        }
    }

    /// <summary>
    /// 根据当前编辑模式刷新可选操作对象。
    /// </summary>
    private void RefreshOperationObjectOptions()
    {
        IEnumerable<string> options = new[] { "System", "Lua" }
            .Concat(OperationConfigurationStore.LoadDeviceNames());
        ReplaceStringOptions(OperationObjectOptions, options);
        if (string.IsNullOrWhiteSpace(EditingOperation.OperationObjectName) || !OperationObjectOptions.Contains(EditingOperation.OperationObjectName))
        {
            EditingOperation.OperationObjectName = OperationObjectOptions.FirstOrDefault() ?? string.Empty;
        }
    }

    /// <summary>
    /// 根据当前操作对象刷新协议、指令和业务方法列表。
    /// </summary>
    private void RefreshProtocolAndMethodOptions()
    {
        ProtocolOptions.Clear();
        CommandOptions.Clear();
        InvokeMethodOptions.Clear();
        InvokeMethodRemarkOptions.Clear();
        StationOperationMethodCollection.Clear();

        switch (EditingOperation.OperationObjectName)
        {
            case "Lua":
                InvokeMethodOptions.Add("Lua");
                break;
            case "System":
                foreach (BusinessOperationDescriptor operation in BusinessOperationCatalog.GetOperations("System"))
                {
                    InvokeMethodOptions.Add(operation.OperationId);
                    InvokeMethodRemarkOptions.Add(string.IsNullOrWhiteSpace(operation.Description) ? operation.DisplayName : operation.Description);
                    StationOperationMethodCollection.Add(new StationOperationMethodItem
                    {
                        Kind = "方法",
                        OperationType = "系统",
                        OperationObject = "System",
                        InvokeMethod = operation.OperationId,
                        Summary = string.IsNullOrWhiteSpace(operation.Description) ? operation.DisplayName : operation.Description,
                        ParameterCount = operation.Parameters.Count
                    });
                }
                break;
            default:
                foreach (string protocol in OperationConfigurationStore.LoadDeviceSupportedProtocolNames(EditingOperation.OperationObjectName))
                {
                    ProtocolOptions.Add(protocol);
                    foreach (string command in OperationConfigurationStore.LoadProtocolCommandNames(protocol))
                    {
                        CommandOptions.Add(command);
                        InvokeMethodOptions.Add(command);
                        StationOperationMethodCollection.Add(new StationOperationMethodItem
                        {
                            Kind = "指令",
                            OperationType = "设备",
                            OperationObject = EditingOperation.OperationObjectName,
                            ProtocolName = protocol,
                            CommandName = command,
                            InvokeMethod = command,
                            Summary = protocol,
                            ParameterCount = OperationConfigurationStore.LoadProtocolCommandPlaceholders(protocol, command).Count
                        });
                    }
                }

                foreach (BusinessOperationDescriptor operation in BusinessOperationBindingResolver.GetOperationsForOperationObject(EditingOperation.OperationObjectName))
                {
                    InvokeMethodOptions.Add(operation.OperationId);
                    StationOperationMethodCollection.Add(new StationOperationMethodItem
                    {
                        Kind = "业务",
                        OperationType = "业务",
                        OperationObject = EditingOperation.OperationObjectName,
                        InvokeMethod = operation.OperationId,
                        Summary = string.IsNullOrWhiteSpace(operation.Description) ? operation.DisplayName : operation.Description,
                        ParameterCount = operation.Parameters.Count
                    });
                }
                break;
        }
        if (string.IsNullOrWhiteSpace(EditingOperation.PCommandName) || !InvokeMethodOptions.Contains(EditingOperation.PCommandName))
        {
            EditingOperation.Parameters?.Clear();
            EditingOperation.ReturnValues?.Clear();
            EditingOperation.PCommandName = string.Empty;
        }

        // 方法集合填充完成后通知视图恢复当前步骤对应的选中行。
        // 方法集合始终返回同一个实例，额外通知宿主刷新当前方法的选中状态。
        OnPropertyChanged(nameof(StationOperationMethodCollection));
    }

    /// <summary>
    /// 根据当前设备指令刷新占位参数及返回值键。
    /// </summary>
    private void RefreshProtocolCommandParameters()
    {
        if (!TryFindDeviceCommand(
                EditingOperation.OperationObjectName,
                EditingOperation.PCommandName,
                out string protocolName,
                out string commandName))
        {
            BusinessOperationDescriptor? businessOperation = BusinessOperationBindingResolver.FindOperationForOperationObject(
                EditingOperation.OperationObjectName,
                null,
                EditingOperation.PCommandName);
            if (businessOperation is not null)
            {
                ObservableCollection<InputParameter> parameters = CreateOperationParametersFromBusinessOperation(businessOperation);
                EditingOperation.Parameters.Clear();
                foreach (InputParameter parameter in parameters)
                {
                    EditingOperation.Parameters.Add(parameter);
                }
                SelectedEditingInvokeParameter = EditingOperation.Parameters.FirstOrDefault();
            }
            return;
        }

        EditingOperation.Parameters.Clear();
        int num = 1;
        foreach (ProtocolPlaceholderDefinition placeholder in OperationConfigurationStore.LoadProtocolCommandPlaceholders(protocolName, commandName))
        {
            EditingOperation.Parameters.Add(new InputParameter
            {
                Num = num++,
                ParameterType = ParameterTypeOptions.First(),
                ParameterName = placeholder.Name,
                Value = placeholder.Value,
                Description = placeholder.Name
            });
        }

        ReplaceStringOptions(ReturnValueOptions, OperationConfigurationStore.LoadProtocolCommandReturnValueKeys(protocolName, commandName));
        ReplaceStepEditorReturnParameterRows(new WorkStepOperation
        {
            ReturnValues = new ObservableCollection<ReturnValue>(ReturnValueOptions.Select((key, index) => new ReturnValue
            {
                Num = index + 1,
                ReturnParameterName = key
            }))
        });
        SelectedEditingInvokeParameter = EditingOperation.Parameters.FirstOrDefault();
    }

    /// <summary>
    /// 根据当前系统方法重新生成输入参数。
    /// </summary>
    private void RefreshSelectedMethodParameters()
    {
        if (!IsSystemOperationSelected)
        {
            return;
        }

        EditingOperation.Parameters.Clear();
        BusinessOperationDescriptor? operation = BusinessOperationCatalog.GetOperations("System")
            .FirstOrDefault(item => string.Equals(item.OperationId, EditingOperation.PCommandName, StringComparison.OrdinalIgnoreCase));
        if (operation is not null)
        {
            foreach (BusinessParameterDescriptor parameter in operation.Parameters.OrderBy(item => item.Sequence))
            {
                EditingOperation.Parameters.Add(new InputParameter
                {
                    Num = parameter.Sequence,
                    ParameterType = ParameterTypeOptions.First(),
                    ParameterName = parameter.Name,
                    Value = parameter.DefaultValue,
                    Description = string.IsNullOrWhiteSpace(parameter.Description) ? parameter.DisplayName : parameter.Description
                });
            }
        }

        SelectedEditingInvokeParameter = EditingOperation.Parameters.FirstOrDefault();
    }

    /// <summary>
    /// 打开编辑器并从业务实体建立独立编辑副本，避免取消编辑时污染原数据。
    /// </summary>
    public void Open(
        WorkStepOperation operation,
        bool isNewOperation,
        IEnumerable<WorkStepOperation>? operations = null)
    {
        ArgumentNullException.ThrowIfNull(operation);

        IsInitializingOperationDrawer = true;
        try
        {
            _operationContext = operations;
            EditingOperation = operation.Clone();
            _isNewOperation = isNewOperation;
            RefreshOperationContext(operation, operations);
            SelectedEditingInvokeParameter = EditingOperation.Parameters.FirstOrDefault();
            SelectedEditingReturnParameter = EditingOperation.ReturnValues.FirstOrDefault();
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(BtnTitle));
            IsOpen = true;
        }
        finally
        {
            IsInitializingOperationDrawer = false;
        }

        RefreshMetadataForProperty(nameof(EditingOperation.OperationObjectName));
    }

    /// <summary>
    /// 从配置存储重新加载 Lua 脚本模板名称。
    /// </summary>
    public void RefreshLuaScriptTemplateOptions()
    {
        ReplaceStringOptions(LuaScriptTemplateOptions, OperationConfigurationStore.LoadLuaScriptTemplateNames());
    }

    /// <summary>
    /// 将指定 Lua 模板内容应用到当前编辑步骤。
    /// </summary>
    public void ApplyLuaScriptTemplate(string? templateName)
    {
        if (string.IsNullOrWhiteSpace(templateName))
        {
            return;
        }

        LuaScriptProfileDocument? document = OperationConfigurationStore.LoadLuaScriptTemplate(templateName.Trim());
        if (document is not null)
        {
            EditingOperation.LuaScript = document.ScriptText ?? string.Empty;
        }
    }

    /// <summary>
    /// 根据方法列表项创建带默认输入参数和返回参数的新步骤。
    /// </summary>
    public WorkStepOperation? CreateOperationFromMethodItem(StationOperationMethodItem? item)
    {
        if (item is null)
        {
            return null;
        }

        WorkStepOperation operation = new()
        {
            OperationObjectName = item.OperationObject,
            PCommandName = item.InvokeMethod,
            Summary = item.Summary
        };

        // 选中新的方法或指令时必须基于该方法重新生成参数，不能复制上一个编辑方法的集合。
        operation.Parameters = CreateDefaultOperationParameters(operation);
        operation.ReturnValues = CreateReturnParametersFromOperation(operation);
        return operation;
    }

    /// <summary>
    /// 根据操作定义推导可输出的返回参数，供方案和流程图编辑场景复用。
    /// </summary>
    public ObservableCollection<ReturnValue> CreateReturnParametersFromOperation(WorkStepOperation? operation)
    {
        if (operation is null || string.Equals(operation.OperationObjectName, "Lua", StringComparison.OrdinalIgnoreCase))
        {
            return new ObservableCollection<ReturnValue>();
        }

        if (operation.ReturnValues.Count > 0)
        {
            return new ObservableCollection<ReturnValue>(operation.ReturnValues.Select(item => item.Clone()));
        }

        BusinessOperationDescriptor? businessOperation = BusinessOperationBindingResolver.FindOperationForOperationObject(
            operation.OperationObjectName,
            null,
            operation.PCommandName);
        if (businessOperation is not null && !string.Equals(businessOperation.ReturnTypeName, "void", StringComparison.OrdinalIgnoreCase))
        {
            return new ObservableCollection<ReturnValue>
            {
                new() { Num = 1, ReturnParameterName = businessOperation.OperationId }
            };
        }

        foreach (string protocol in OperationConfigurationStore.LoadDeviceSupportedProtocolNames(operation.OperationObjectName))
        {
            if (!OperationConfigurationStore.LoadProtocolCommandNames(protocol)
                    .Any(command => string.Equals(command, operation.PCommandName, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            return new ObservableCollection<ReturnValue>(OperationConfigurationStore
                .LoadProtocolCommandReturnValueKeys(protocol, operation.PCommandName)
                .Select((key, index) => new ReturnValue { Num = index + 1, ReturnParameterName = key }));
        }

        return new ObservableCollection<ReturnValue>();
    }

    #endregion

    #region 返回参数

    /// <summary>
    /// 使用指定步骤的有效返回值键替换编辑器返回参数行，并统一排序去重。
    /// </summary>
    public void ReplaceStepEditorReturnParameterRows(WorkStepOperation? operation)
    {
        // 先生成独立快照再清空界面集合，既避免传入当前 EditingOperation 时枚举源被清空，
        // 也按返回值键统一去重，防止方法元数据或重复刷新产生同名返回参数。
        List<ReturnValue> rows = (operation?.ReturnValues ?? Enumerable.Empty<ReturnValue>())
            .Where(item => !string.IsNullOrWhiteSpace(item.ReturnParameterName))
            .GroupBy(item => item.ReturnParameterName.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(item => item.Num).First().Clone())
            .OrderBy(item => item.Num)
            .ToList();

        EditingOperation.ReturnValues.Clear();
        for (int index = 0; index < rows.Count; index++)
        {
            rows[index].Num = index + 1;
            EditingOperation.ReturnValues.Add(rows[index]);
        }

        SelectedEditingReturnParameter = EditingOperation.ReturnValues.FirstOrDefault();
    }

    /// <summary>
    /// 清空当前步骤的返回参数行及选中项。
    /// </summary>
    public void ClearStepEditorReturnParameterRows()
    {
        EditingOperation.ReturnValues.Clear();
        SelectedEditingReturnParameter = null;
    }

    #endregion

    #region 参数集合监听

    /// <summary>
    /// 响应输入参数集合变化，维护序号和界面编辑行。
    /// </summary>
    private void EditingInvokeParameters_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        NormalizeInvokeParameterNums();
        SynchronizeEditingParameterRows();
    }

    /// <summary>
    /// 响应返回参数集合变化，并刷新返回值显示状态。
    /// </summary>
    private void EditingReturnParameters_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateTrackedItems(e, _trackedReturnParameters, EditingReturnParameter_PropertyChanged);
        OnPropertyChanged(nameof(HasVisibleReturnValueName));
    }

    private static void UpdateTrackedItems<T>(
        NotifyCollectionChangedEventArgs e,
        HashSet<T> trackedItems,
        PropertyChangedEventHandler handler)
        where T : INotifyPropertyChanged
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (T item in trackedItems.ToList())
            {
                item.PropertyChanged -= handler;
            }

            trackedItems.Clear();
        }

        if (e.NewItems is not null)
        {
            foreach (T item in e.NewItems.OfType<T>())
            {
                if (trackedItems.Add(item))
                {
                    item.PropertyChanged += handler;
                }
            }
        }

        if (e.OldItems is not null)
        {
            foreach (T item in e.OldItems.OfType<T>())
            {
                if (trackedItems.Remove(item))
                {
                    item.PropertyChanged -= handler;
                }
            }
        }
    }

    /// <summary>
    /// 响应返回参数显示配置变化。
    /// </summary>
    private void EditingReturnParameter_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ReturnValue.IsShowView))
        {
            OnPropertyChanged(nameof(HasVisibleReturnValueName));
        }
    }

    /// <summary>
    /// 修正输入参数中的无效或重复序号，同时保留已有有效序号。
    /// </summary>
    private void NormalizeInvokeParameterNums()
    {
        HashSet<int> used = new();
        int next = 1;
        foreach (InputParameter parameter in EditingOperation.Parameters)
        {
            if (parameter.Num <= 0 || !used.Add(parameter.Num))
            {
                while (used.Contains(next))
                {
                    next++;
                }

                parameter.Num = next;
                used.Add(next);
            }

            next = Math.Max(next, parameter.Num + 1);
        }
    }

    /// <summary>
    /// 根据业务参数集合重建编辑行。编辑行只保存界面候选，参数对象保持同一引用，
    /// 因此 DataGrid 编辑结果会直接进入 EditingOperation.Parameters，并由保存流程统一克隆。
    /// </summary>
    private void SynchronizeEditingParameterRows()
    {
        Dictionary<InputParameter, InputParameterEditorItem> existingRows = EditingParameterRows
            .ToDictionary(row => row.Parameter);

        EditingParameterRows.Clear();
        foreach (InputParameter parameter in EditingOperation.Parameters)
        {
            InputParameterEditorItem editingRow = existingRows.TryGetValue(parameter, out InputParameterEditorItem? row)
                ? row
                : new InputParameterEditorItem(parameter);
            EditingParameterRows.Add(editingRow);

        }
    }

    /// <summary>
    /// 使用当前尚未保存的编辑副本刷新工步值共享候选集合。
    /// 编辑已有步骤时按原位置替换宿主数据，新增步骤时追加到当前工步末尾，保持候选的步骤顺序。
    /// </summary>
    public void RefreshWorkStepValueOptionsFromEditingOperation()
    {
        RefreshOperationContext(EditingOperation, _operationContext);
    }

    /// <summary>
    /// 根据当前工步操作顺序建立编辑器所需的返回值上下文，方案层无需保存编辑临时状态。
    /// </summary>
    private void RefreshOperationContext(WorkStepOperation editingOperation, IEnumerable<WorkStepOperation>? operations)
    {
        List<WorkStepOperation> operationList = operations?.ToList() ?? new List<WorkStepOperation>();
        int editingIndex = operationList.FindIndex(item =>
            ReferenceEquals(item, editingOperation) || string.Equals(item.Id, editingOperation.Id, StringComparison.Ordinal));
        if (editingIndex < 0)
        {
            editingIndex = operationList.Count;
        }

        List<WorkStepOperation> previousOperations = operationList.Take(editingIndex).ToList();

        // 工步值需要实时反映当前弹框中尚未保存的输入。已有步骤按原位置替换，新增步骤则作为当前工步的最后一步。
        List<WorkStepOperation> workStepValueOperations = operationList.ToList();
        if (editingIndex < workStepValueOperations.Count)
        {
            workStepValueOperations[editingIndex] = EditingOperation;
        }
        else
        {
            workStepValueOperations.Add(EditingOperation);
        }

        // 工步值候选来自当前工步所有已有步骤的输入参数，不受当前编辑步骤位置限制。
        // 保持步骤顺序及步骤内参数顺序，重复值只保留首次出现的位置，不再按名称排序。
        _workStepValueOptions.Clear();
        _workStepValueOptions.AddRange(workStepValueOperations
            .SelectMany(item => item.Parameters)
            .Where(parameter => string.Equals(parameter.ParameterType?.Trim(), "工步值", StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(parameter.Value))
            .Select(parameter => parameter.Value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase));

        // 条件执行左右参数也可以定义工步值名称，当前弹框中尚未保存的值需立即追加到共享候选。
        // 左参数优先于右参数，与界面编辑顺序保持一致；已在输入参数中出现的名称不重复添加。
        ConditionExecution condition = EditingOperation.ConditionExecution;
        IEnumerable<string> conditionWorkStepValues = new[]
        {
            string.Equals(condition.LeftParameterType?.Trim(), "工步值", StringComparison.Ordinal)
                ? condition.LeftValue
                : string.Empty,
            string.Equals(condition.RightParameterType?.Trim(), "工步值", StringComparison.Ordinal)
                ? condition.RightValue
                : string.Empty
        };
        _workStepValueOptions.AddRange(conditionWorkStepValues
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Where(value => !_workStepValueOptions.Contains(value, StringComparer.OrdinalIgnoreCase)));

        _parameterReturnValueOptions.Clear();
        // 返回值候选保持前置步骤顺序及步骤内返回值键顺序，重复项仅保留首次出现的位置。
        _parameterReturnValueOptions.AddRange(previousOperations
            .Where(item => !string.IsNullOrWhiteSpace(item.ReturnValue))
            .SelectMany(item => item.ReturnValues
                .Where(returnValue => !string.IsNullOrWhiteSpace(returnValue.ReturnParameterName))
                .Select(returnValue => $"{item.ReturnValue.Trim()}_{returnValue.ReturnParameterName.Trim()}"))
            .Distinct(StringComparer.OrdinalIgnoreCase));
        ReplaceStringOptions(ParameterReturnValueOptions, _parameterReturnValueOptions.Concat(_externalReturnValueOptions));
        ReplaceStringOptions(WorkStepValueOptions, _workStepValueOptions);
        ReplaceStringOptions(ReturnValueOptions, _parameterReturnValueOptions
            .Concat(_externalReturnValueOptions)
            .Concat(new[] { EditingOperation.ReturnValue }));
    }

    /// <summary>
    /// 使用经过清理和去重的字符串替换目标候选集合。
    /// </summary>
    private static void ReplaceStringOptions(ObservableCollection<string> target, IEnumerable<string> values)
    {
        List<string> normalizedValues = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (target.SequenceEqual(normalizedValues, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        target.Clear();
        foreach (string value in normalizedValues)
        {
            target.Add(value);
        }
    }

    #endregion
}

/// <summary>
/// 操作编辑器保存结果。
/// </summary>
public sealed class OperationEditorSavedEventArgs : EventArgs
{
    /// <summary>
    /// 创建操作编辑器保存结果。
    /// </summary>
    public OperationEditorSavedEventArgs(WorkStepOperation operation, bool isNewOperation)
    {
        Operation = operation ?? throw new ArgumentNullException(nameof(operation));
        IsNewOperation = isNewOperation;
    }

    public WorkStepOperation Operation { get; }

    public bool IsNewOperation { get; }
}
