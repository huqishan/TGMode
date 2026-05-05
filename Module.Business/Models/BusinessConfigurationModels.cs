using ControlLibrary;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Text.Json.Serialization;

namespace Module.Business.Models;

/// <summary>
/// 业务配置根对象，统一保存工步配置和方案配置。
/// </summary>
public sealed class BusinessConfigurationCatalog
{
    public ObservableCollection<ProductProfile> Products { get; set; } = new();

    public ObservableCollection<WorkStepProfile> WorkSteps { get; set; } = new();

    public ObservableCollection<SchemeProfile> Schemes { get; set; } = new();
}

/// <summary>
/// 工步配置，按产品名称分类。
/// </summary>
public sealed class ProductProfile : ViewModelProperties
{
    #region 私有字段

    private string _id = Guid.NewGuid().ToString("N");
    private string _productName = "默认产品";
    private DateTime _lastModifiedAt = DateTime.Now;
    private ObservableCollection<ProductKeyValueItem> _keyValues = new();

    #endregion

    #region 构造方法

    public ProductProfile()
    {
        AttachKeyValues(_keyValues);
    }

    #endregion

    #region 绑定属性

    public string Id
    {
        get => _id;
        set => SetField(ref _id, string.IsNullOrWhiteSpace(value) ? Guid.NewGuid().ToString("N") : value.Trim());
    }

    public string ProductName
    {
        get => _productName;
        set => SetField(ref _productName, value ?? string.Empty, true);
    }

    public DateTime LastModifiedAt
    {
        get => _lastModifiedAt;
        set
        {
            DateTime normalizedValue = value == default ? DateTime.Now : value;
            if (SetField(ref _lastModifiedAt, normalizedValue))
            {
                OnPropertyChanged(nameof(LastModifiedText));
            }
        }
    }

    public ObservableCollection<ProductKeyValueItem> KeyValues
    {
        get => _keyValues;
        set
        {
            if (ReferenceEquals(_keyValues, value))
            {
                return;
            }

            DetachKeyValues(_keyValues);
            _keyValues = value ?? new ObservableCollection<ProductKeyValueItem>();
            AttachKeyValues(_keyValues);
            OnPropertyChanged();
            RaiseKeyValueSummaryChanged();
        }
    }

    [JsonIgnore]
    public int KeyValueCount => KeyValues.Count;

    [JsonIgnore]
    public string KeyValueSummary =>
        KeyValues.Count == 0
            ? "未配置键值对"
            : string.Join(" / ", KeyValues.Select(item => item.DisplayText));

    [JsonIgnore]
    public string LastModifiedText => $"最后修改：{LastModifiedAt:yyyy-MM-dd HH:mm:ss}";

    #endregion

    #region 集合通知

    private void AttachKeyValues(ObservableCollection<ProductKeyValueItem> keyValues)
    {
        keyValues.CollectionChanged += KeyValues_CollectionChanged;
        foreach (ProductKeyValueItem item in keyValues)
        {
            item.PropertyChanged += KeyValue_PropertyChanged;
        }
    }

    private void DetachKeyValues(ObservableCollection<ProductKeyValueItem> keyValues)
    {
        keyValues.CollectionChanged -= KeyValues_CollectionChanged;
        foreach (ProductKeyValueItem item in keyValues)
        {
            item.PropertyChanged -= KeyValue_PropertyChanged;
        }
    }

    private void KeyValues_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (ProductKeyValueItem item in e.NewItems.OfType<ProductKeyValueItem>())
            {
                item.PropertyChanged += KeyValue_PropertyChanged;
            }
        }

        if (e.OldItems is not null)
        {
            foreach (ProductKeyValueItem item in e.OldItems.OfType<ProductKeyValueItem>())
            {
                item.PropertyChanged -= KeyValue_PropertyChanged;
            }
        }

        RaiseKeyValueSummaryChanged();
    }

    private void KeyValue_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ProductKeyValueItem.Key)
            or nameof(ProductKeyValueItem.Value)
            or nameof(ProductKeyValueItem.DisplayText))
        {
            RaiseKeyValueSummaryChanged();
        }
    }

    private void RaiseKeyValueSummaryChanged()
    {
        OnPropertyChanged(nameof(KeyValueCount));
        OnPropertyChanged(nameof(KeyValueSummary));
    }

    #endregion

    #region 复制方法

    public ProductProfile Clone()
    {
        return new ProductProfile
        {
            Id = Id,
            ProductName = ProductName,
            LastModifiedAt = LastModifiedAt,
            KeyValues = new ObservableCollection<ProductKeyValueItem>(KeyValues.Select(item => item.Clone()))
        };
    }

    #endregion

    #region 修改时间方法

    public void MarkModified()
    {
        LastModifiedAt = DateTime.Now;
    }

    #endregion
}

public sealed class ProductKeyValueItem : ViewModelProperties
{
    #region 私有字段

    private string _id = Guid.NewGuid().ToString("N");
    private string _key = "键";
    private string _value = "值";

    #endregion

    #region 绑定属性

    public string Id
    {
        get => _id;
        set => SetField(ref _id, string.IsNullOrWhiteSpace(value) ? Guid.NewGuid().ToString("N") : value.Trim());
    }

    public string Key
    {
        get => _key;
        set
        {
            if (SetField(ref _key, value ?? string.Empty, true))
            {
                OnPropertyChanged(nameof(DisplayText));
            }
        }
    }

    public string Value
    {
        get => _value;
        set
        {
            if (SetField(ref _value, value ?? string.Empty, true))
            {
                OnPropertyChanged(nameof(DisplayText));
            }
        }
    }

    [JsonIgnore]
    public string DisplayText => $"{Key}={Value}";

    #endregion

    #region 复制方法

    public ProductKeyValueItem Clone()
    {
        return new ProductKeyValueItem
        {
            Id = Id,
            Key = Key,
            Value = Value
        };
    }

    #endregion
}

/// <summary>
/// 宸ユ閰嶇疆锛屾寜浜у搧鍚嶇О鍒嗙被銆?/// </summary>
public sealed class WorkStepProfile : ViewModelProperties
{
    #region 私有字段

    private string _id = Guid.NewGuid().ToString("N");
    private string _productName = "默认产品";
    private string _stepName = "工步 1";
    private DateTime _lastModifiedAt = DateTime.Now;
    private ObservableCollection<WorkStepOperation> _steps = new();

    #endregion

    #region 构造方法

    public WorkStepProfile()
    {
        AttachSteps(_steps);
    }

    #endregion

    #region 基础属性

    public string Id
    {
        get => _id;
        set => SetField(ref _id, string.IsNullOrWhiteSpace(value) ? Guid.NewGuid().ToString("N") : value.Trim());
    }

    public string ProductName
    {
        get => _productName;
        set => SetField(ref _productName, value ?? string.Empty, true);
    }

    public string StepName
    {
        get => _stepName;
        set => SetField(ref _stepName, value ?? string.Empty, true);
    }

    public DateTime LastModifiedAt
    {
        get => _lastModifiedAt;
        set
        {
            DateTime normalizedValue = value == default ? DateTime.Now : value;
            if (SetField(ref _lastModifiedAt, normalizedValue))
            {
                OnPropertyChanged(nameof(LastModifiedText));
            }
        }
    }

    public ObservableCollection<WorkStepOperation> Steps
    {
        get => _steps;
        set
        {
            if (ReferenceEquals(_steps, value))
            {
                return;
            }

            DetachSteps(_steps);
            _steps = value ?? new ObservableCollection<WorkStepOperation>();
            AttachSteps(_steps);
            OnPropertyChanged();
            RaiseStepSummaryChanged();
        }
    }

    [JsonIgnore]
    public int OperationCount => Steps.Count;

    [JsonIgnore]
    public string OperationSummary =>
        Steps.Count == 0
            ? "未配置步骤"
            : string.Join(" / ", Steps.Select(step => step.DisplayText));

    [JsonIgnore]
    public string LastModifiedText => $"最后修改：{LastModifiedAt:yyyy-MM-dd HH:mm:ss}";

    #endregion

    #region 集合通知

    private void AttachSteps(ObservableCollection<WorkStepOperation> steps)
    {
        steps.CollectionChanged += Steps_CollectionChanged;
        foreach (WorkStepOperation step in steps)
        {
            step.PropertyChanged += Step_PropertyChanged;
        }

        RefreshOperationDisplayOrders(steps);
    }

    private void DetachSteps(ObservableCollection<WorkStepOperation> steps)
    {
        steps.CollectionChanged -= Steps_CollectionChanged;
        foreach (WorkStepOperation step in steps)
        {
            step.PropertyChanged -= Step_PropertyChanged;
        }
    }

    private void Steps_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Move)
        {
            if (sender is ObservableCollection<WorkStepOperation> movedSteps)
            {
                RefreshOperationDisplayOrders(movedSteps);
            }

            RaiseStepSummaryChanged();
            return;
        }

        if (e.NewItems is not null)
        {
            foreach (WorkStepOperation step in e.NewItems.OfType<WorkStepOperation>())
            {
                step.PropertyChanged += Step_PropertyChanged;
            }
        }

        if (e.OldItems is not null)
        {
            foreach (WorkStepOperation step in e.OldItems.OfType<WorkStepOperation>())
            {
                step.PropertyChanged -= Step_PropertyChanged;
            }
        }

        if (sender is ObservableCollection<WorkStepOperation> changedSteps)
        {
            RefreshOperationDisplayOrders(changedSteps);
        }

        RaiseStepSummaryChanged();
    }

    private void Step_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WorkStepOperation.OperationObject)
            or nameof(WorkStepOperation.ProtocolName)
            or nameof(WorkStepOperation.CommandName)
            or nameof(WorkStepOperation.InvokeMethod)
            or nameof(WorkStepOperation.ReturnValue)
            or nameof(WorkStepOperation.ShowDataToView)
            or nameof(WorkStepOperation.ViewDataName)
            or nameof(WorkStepOperation.ViewJudgeType)
            or nameof(WorkStepOperation.ViewJudgeCondition)
            or nameof(WorkStepOperation.LuaScript)
            or nameof(WorkStepOperation.DelayMilliseconds)
            or nameof(WorkStepOperation.Remark)
            or nameof(WorkStepOperation.ParameterCount)
            or nameof(WorkStepOperation.DisplayText))
        {
            RaiseStepSummaryChanged();
        }

        if (e.PropertyName is nameof(WorkStepOperation.IsChecked)
            or nameof(WorkStepOperation.DisplayOrder))
        {
            OnPropertyChanged(nameof(Steps));
        }
    }

    private static void RefreshOperationDisplayOrders(ObservableCollection<WorkStepOperation> steps)
    {
        for (int index = 0; index < steps.Count; index++)
        {
            steps[index].DisplayOrder = index + 1;
        }
    }

    private void RaiseStepSummaryChanged()
    {
        OnPropertyChanged(nameof(OperationCount));
        OnPropertyChanged(nameof(OperationSummary));
    }

    #endregion

    #region 复制方法

    public WorkStepProfile Clone()
    {
        WorkStepProfile clone = new()
        {
            Id = Id,
            ProductName = ProductName,
            StepName = StepName,
            LastModifiedAt = LastModifiedAt,
            Steps = new ObservableCollection<WorkStepOperation>(Steps.Select(step => step.Clone()))
        };

        return clone;
    }

    #endregion

    #region 修改时间方法

    public void MarkModified()
    {
        LastModifiedAt = DateTime.Now;
    }

    #endregion
}

/// <summary>
/// 工步内的单个步骤。
/// </summary>
public sealed class WorkStepOperation : ViewModelProperties
{
    #region 私有字段

    private string _id = Guid.NewGuid().ToString("N");
    private string _operationType = "设备";
    private string _operationObject = "System";
    private string _protocolName = string.Empty;
    private string _commandName = string.Empty;
    private string _invokeMethod = "等待";
    private string _returnValue = string.Empty;
    private bool _showDataToView;
    private string _viewDataName = string.Empty;
    private string _viewJudgeType = string.Empty;
    private string _viewJudgeCondition = string.Empty;
    private string _luaScript = string.Empty;
    private int _delayMilliseconds;
    private string _remark = string.Empty;
    private bool _isChecked;
    private int _displayOrder = 1;
    private ObservableCollection<WorkStepOperationParameter> _parameters = new();

    #endregion

    #region 构造方法

    public WorkStepOperation()
    {
        AttachParameters(_parameters);
    }

    #endregion

    #region 绑定属性

    public string Id
    {
        get => _id;
        set => SetField(ref _id, string.IsNullOrWhiteSpace(value) ? Guid.NewGuid().ToString("N") : value.Trim());
    }

    public string OperationType
    {
        get => _operationType;
        set
        {
            if (SetField(ref _operationType, string.IsNullOrWhiteSpace(value) ? "设备" : value, true))
            {
                OnPropertyChanged(nameof(DisplayText));
            }
        }
    }

    public string OperationObject
    {
        get => _operationObject;
        set
        {
            if (SetField(ref _operationObject, value ?? string.Empty, true))
            {
                OnPropertyChanged(nameof(DisplayText));
            }
        }
    }

    public string ProtocolName
    {
        get => _protocolName;
        set
        {
            if (SetField(ref _protocolName, value ?? string.Empty, true))
            {
                OnPropertyChanged(nameof(DisplayText));
            }
        }
    }

    public string CommandName
    {
        get => _commandName;
        set
        {
            if (SetField(ref _commandName, value ?? string.Empty, true))
            {
                OnPropertyChanged(nameof(DisplayText));
            }
        }
    }

    public string InvokeMethod
    {
        get => _invokeMethod;
        set
        {
            if (SetField(ref _invokeMethod, value ?? string.Empty, true))
            {
                OnPropertyChanged(nameof(DisplayText));
            }
        }
    }

    public string ReturnValue
    {
        get => _returnValue;
        set
        {
            if (SetField(ref _returnValue, value ?? string.Empty, true))
            {
                OnPropertyChanged(nameof(DisplayText));
            }
        }
    }

    public bool ShowDataToView
    {
        get => _showDataToView;
        set => SetField(ref _showDataToView, value);
    }

    public string ViewDataName
    {
        get => _viewDataName;
        set => SetField(ref _viewDataName, value ?? string.Empty, true);
    }

    public string ViewJudgeType
    {
        get => _viewJudgeType;
        set => SetField(ref _viewJudgeType, value ?? string.Empty, true);
    }

    public string ViewJudgeCondition
    {
        get => _viewJudgeCondition;
        set => SetField(ref _viewJudgeCondition, value ?? string.Empty, true);
    }

    public string LuaScript
    {
        get => _luaScript;
        set
        {
            if (SetField(ref _luaScript, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(DisplayText));
            }
        }
    }

    public int DelayMilliseconds
    {
        get => _delayMilliseconds;
        set
        {
            int normalizedValue = Math.Max(0, value);
            if (SetField(ref _delayMilliseconds, normalizedValue))
            {
                OnPropertyChanged(nameof(DisplayText));
            }
        }
    }

    public string Remark
    {
        get => _remark;
        set
        {
            if (SetField(ref _remark, value ?? string.Empty, true))
            {
                OnPropertyChanged(nameof(DisplayText));
            }
        }
    }

    [JsonIgnore]
    public bool IsChecked
    {
        get => _isChecked;
        set => SetField(ref _isChecked, value);
    }

    [JsonIgnore]
    public int DisplayOrder
    {
        get => _displayOrder;
        set => SetField(ref _displayOrder, Math.Max(1, value));
    }

    public ObservableCollection<WorkStepOperationParameter> Parameters
    {
        get => _parameters;
        set
        {
            if (ReferenceEquals(_parameters, value))
            {
                return;
            }

            DetachParameters(_parameters);
            _parameters = value ?? new ObservableCollection<WorkStepOperationParameter>();
            AttachParameters(_parameters);
            OnPropertyChanged();
            OnPropertyChanged(nameof(ParameterCount));
            OnPropertyChanged(nameof(DisplayText));
        }
    }

    [JsonIgnore]
    public int ParameterCount => Parameters.Count;

    [JsonIgnore]
    public string DisplayText
    {
        get
        {
            string returnText = string.IsNullOrWhiteSpace(ReturnValue) ? string.Empty : $" -> {ReturnValue}";
            string delayText = DelayMilliseconds <= 0 ? string.Empty : $" / {DelayMilliseconds}ms";
            string remarkText = string.IsNullOrWhiteSpace(Remark) ? string.Empty : $" / {Remark}";
            string parameterText = ParameterCount == 0 ? string.Empty : $" / 参数{ParameterCount}";
            if (IsLuaOperationObject(OperationObject) || IsLuaOperationObject(OperationType))
            {
                return $"Lua{delayText}{remarkText}";
            }

            string methodText = string.IsNullOrWhiteSpace(CommandName) ? InvokeMethod : CommandName;
            string protocolText = string.IsNullOrWhiteSpace(ProtocolName) ? string.Empty : $"{ProtocolName}.";
            string actionText = IsSystemOperationObject(OperationObject)
                ? InvokeMethod
                : $"{protocolText}{methodText}";
            string operationPath = string.IsNullOrWhiteSpace(OperationObject)
                ? actionText
                : $"{OperationObject}.{actionText}";

            return $"{operationPath}{returnText}{delayText}{remarkText}{parameterText}";
        }
    }

    #endregion

    #region 集合通知

    private void AttachParameters(ObservableCollection<WorkStepOperationParameter> parameters)
    {
        parameters.CollectionChanged += Parameters_CollectionChanged;
        foreach (WorkStepOperationParameter parameter in parameters)
        {
            parameter.PropertyChanged += Parameter_PropertyChanged;
        }
    }

    private void DetachParameters(ObservableCollection<WorkStepOperationParameter> parameters)
    {
        parameters.CollectionChanged -= Parameters_CollectionChanged;
        foreach (WorkStepOperationParameter parameter in parameters)
        {
            parameter.PropertyChanged -= Parameter_PropertyChanged;
        }
    }

    private void Parameters_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (WorkStepOperationParameter parameter in e.NewItems.OfType<WorkStepOperationParameter>())
            {
                parameter.PropertyChanged += Parameter_PropertyChanged;
            }
        }

        if (e.OldItems is not null)
        {
            foreach (WorkStepOperationParameter parameter in e.OldItems.OfType<WorkStepOperationParameter>())
            {
                parameter.PropertyChanged -= Parameter_PropertyChanged;
            }
        }

        OnPropertyChanged(nameof(ParameterCount));
        OnPropertyChanged(nameof(DisplayText));
    }

    private void Parameter_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WorkStepOperationParameter.Name)
            or nameof(WorkStepOperationParameter.Type)
            or nameof(WorkStepOperationParameter.ParameterName)
            or nameof(WorkStepOperationParameter.Sequence)
            or nameof(WorkStepOperationParameter.Value)
            or nameof(WorkStepOperationParameter.Remark)
            or nameof(WorkStepOperationParameter.Description))
        {
            OnPropertyChanged(nameof(DisplayText));
        }
    }

    #endregion

    #region 复制方法

    public WorkStepOperation Clone()
    {
        return new WorkStepOperation
        {
            Id = Id,
            OperationType = OperationType,
            OperationObject = OperationObject,
            ProtocolName = ProtocolName,
            CommandName = CommandName,
            InvokeMethod = InvokeMethod,
            ReturnValue = ReturnValue,
            ShowDataToView = ShowDataToView,
            ViewDataName = ViewDataName,
            ViewJudgeType = ViewJudgeType,
            ViewJudgeCondition = ViewJudgeCondition,
            LuaScript = LuaScript,
            DelayMilliseconds = DelayMilliseconds,
            Remark = Remark,
            IsChecked = false,
            Parameters = new ObservableCollection<WorkStepOperationParameter>(Parameters.Select(parameter => parameter.Clone()))
        };
    }

    #endregion

    #region 静态工具

    private static bool IsSystemOperationObject(string? operationObject)
    {
        return string.Equals(operationObject?.Trim(), "System", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(operationObject?.Trim(), "系统", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLuaOperationObject(string? operationObject)
    {
        return string.Equals(operationObject?.Trim(), "Lua", StringComparison.OrdinalIgnoreCase);
    }

    #endregion
}

/// <summary>
/// 工步步骤调用方法参数。
/// </summary>
public sealed class WorkStepOperationParameter : ViewModelProperties
{
    #region 私有字段

    private string _id = Guid.NewGuid().ToString("N");
    private int _sequence = 1;
    private string _name = "设置值";
    private string _parameterName = string.Empty;
    private string _value = string.Empty;
    private string _remark = string.Empty;

    #endregion

    #region 绑定属性

    public string Id
    {
        get => _id;
        set => SetField(ref _id, string.IsNullOrWhiteSpace(value) ? Guid.NewGuid().ToString("N") : value.Trim());
    }

    public int Sequence
    {
        get => _sequence;
        set => SetField(ref _sequence, Math.Max(1, value));
    }

    public string Name
    {
        get => _name;
        set => SetParameterType(value, nameof(Name));
    }

    [JsonIgnore]
    public string Type
    {
        get => _name;
        set => SetParameterType(value, nameof(Type));
    }

    public string Value
    {
        get => _value;
        set => SetField(ref _value, value ?? string.Empty, true);
    }

    public string ParameterName
    {
        get => _parameterName;
        set => SetField(ref _parameterName, value ?? string.Empty, true);
    }

    public string Remark
    {
        get => _remark;
        set => SetParameterDescription(value, nameof(Remark));
    }

    [JsonIgnore]
    public string Description
    {
        get => _remark;
        set => SetParameterDescription(value, nameof(Description));
    }

    [JsonIgnore]
    public ObservableCollection<string> ValueOptions { get; } = new();

    [JsonIgnore]
    public bool UsesTextValueEditor =>
        string.Equals(Type, "设置值", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Type, "工步值", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool UsesComboValueEditor => !UsesTextValueEditor;

    #endregion

    #region 属性别名方法

    private void SetParameterType(string? value, string propertyName)
    {
        string normalizedValue = string.IsNullOrWhiteSpace(value) ? "设置值" : value.Trim();
        if (!SetField(ref _name, normalizedValue, propertyName))
        {
            return;
        }

        OnPropertyChanged(propertyName == nameof(Name) ? nameof(Type) : nameof(Name));
        OnPropertyChanged(nameof(UsesTextValueEditor));
        OnPropertyChanged(nameof(UsesComboValueEditor));
    }

    private void SetParameterDescription(string? value, string propertyName)
    {
        string normalizedValue = value?.Trim() ?? string.Empty;
        if (!SetField(ref _remark, normalizedValue, propertyName))
        {
            return;
        }

        OnPropertyChanged(propertyName == nameof(Remark) ? nameof(Description) : nameof(Remark));
    }

    #endregion

    #region 复制方法

    public WorkStepOperationParameter Clone()
    {
        return new WorkStepOperationParameter
        {
            Id = Id,
            Sequence = Sequence,
            Name = Name,
            ParameterName = ParameterName,
            Value = Value,
            Remark = Remark
        };
    }

    #endregion
}

/// <summary>
/// 方案工步参数。
/// </summary>
public sealed class SchemeWorkStepParameter : ViewModelProperties
{
    private static readonly string[] DefaultJudgeTypeOptions =
    {
        "等于"
    };

    #region 私有字段

    private string _id = Guid.NewGuid().ToString("N");
    private string _sourceOperationId = string.Empty;
    private string _sourceParameterId = string.Empty;
    private string _parameterName = "参数";
    private string _parameterType = "设置值";
    private string _judgeType = string.Empty;
    private string _judgeCondition = string.Empty;

    #endregion

    #region 绑定属性

    public string Id
    {
        get => _id;
        set => SetField(ref _id, string.IsNullOrWhiteSpace(value) ? Guid.NewGuid().ToString("N") : value.Trim());
    }

    public string SourceOperationId
    {
        get => _sourceOperationId;
        set => SetField(ref _sourceOperationId, value ?? string.Empty, true);
    }

    public string SourceParameterId
    {
        get => _sourceParameterId;
        set => SetField(ref _sourceParameterId, value ?? string.Empty, true);
    }

    public string ParameterName
    {
        get => _parameterName;
        set => SetField(ref _parameterName, string.IsNullOrWhiteSpace(value) ? "参数" : value, true);
    }

    public string ParameterType
    {
        get => _parameterType;
        set
        {
            string normalizedValue = string.IsNullOrWhiteSpace(value) ? "设置值" : value.Trim();
            if (!SetField(ref _parameterType, normalizedValue, nameof(ParameterType)))
            {
                return;
            }

            if (UsesJudgeType && string.IsNullOrWhiteSpace(_judgeType))
            {
                _judgeType = "等于";
                OnPropertyChanged(nameof(JudgeType));
            }
            else if (!UsesJudgeType && !string.IsNullOrWhiteSpace(_judgeType))
            {
                _judgeType = string.Empty;
                OnPropertyChanged(nameof(JudgeType));
            }

            OnPropertyChanged(nameof(UsesJudgeType));
        }
    }

    public string JudgeType
    {
        get => _judgeType;
        set => SetField(ref _judgeType, value ?? string.Empty, true);
    }

    public string JudgeCondition
    {
        get => _judgeCondition;
        set => SetField(ref _judgeCondition, value ?? string.Empty, true);
    }

    [JsonIgnore]
    public bool UsesJudgeType => string.Equals(ParameterType, "判断值", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public ObservableCollection<string> JudgeTypeOptions { get; } = new(DefaultJudgeTypeOptions);

    public void ReplaceJudgeTypeOptions(IEnumerable<string> options)
    {
        List<string> normalizedOptions = options
            .Where(option => !string.IsNullOrWhiteSpace(option))
            .Select(option => option.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalizedOptions.Count == 0)
        {
            normalizedOptions = DefaultJudgeTypeOptions.ToList();
        }

        if (JudgeTypeOptions.SequenceEqual(normalizedOptions, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        JudgeTypeOptions.Clear();
        foreach (string option in normalizedOptions)
        {
            JudgeTypeOptions.Add(option);
        }
    }

    #endregion

    #region 复制方法

    public SchemeWorkStepParameter Clone()
    {
        return new SchemeWorkStepParameter
        {
            Id = Id,
            SourceOperationId = SourceOperationId,
            SourceParameterId = SourceParameterId,
            ParameterName = ParameterName,
            ParameterType = ParameterType,
            JudgeType = JudgeType,
            JudgeCondition = JudgeCondition
        };
    }

    #endregion
}

/// <summary>
/// 方案配置，按产品名称筛选可添加工步。
/// </summary>
public sealed class SchemeProfile : ViewModelProperties
{
    #region 私有字段

    private string _id = Guid.NewGuid().ToString("N");
    private string _schemeName = "方案 1";
    private string _productName = "默认产品";
    private ObservableCollection<SchemeWorkStepItem> _steps = new();

    #endregion

    #region 构造方法

    public SchemeProfile()
    {
        AttachSteps(_steps);
    }

    #endregion

    #region 绑定属性

    public string Id
    {
        get => _id;
        set => SetField(ref _id, string.IsNullOrWhiteSpace(value) ? Guid.NewGuid().ToString("N") : value.Trim());
    }

    public string SchemeName
    {
        get => _schemeName;
        set => SetField(ref _schemeName, value ?? string.Empty, true);
    }

    public string ProductName
    {
        get => _productName;
        set => SetField(ref _productName, value ?? string.Empty, true);
    }

    public ObservableCollection<SchemeWorkStepItem> Steps
    {
        get => _steps;
        set
        {
            if (ReferenceEquals(_steps, value))
            {
                return;
            }

            DetachSteps(_steps);
            _steps = value ?? new ObservableCollection<SchemeWorkStepItem>();
            AttachSteps(_steps);
            OnPropertyChanged();
            OnPropertyChanged(nameof(StepCount));
        }
    }

    [JsonIgnore]
    public int StepCount => Steps.Count;

    #endregion

    #region 集合通知

    private void AttachSteps(ObservableCollection<SchemeWorkStepItem> steps)
    {
        steps.CollectionChanged += Steps_CollectionChanged;
        foreach (SchemeWorkStepItem step in steps)
        {
            step.PropertyChanged += Step_PropertyChanged;
        }

        RefreshStepDisplayOrders(steps);
    }

    private void DetachSteps(ObservableCollection<SchemeWorkStepItem> steps)
    {
        steps.CollectionChanged -= Steps_CollectionChanged;
        foreach (SchemeWorkStepItem step in steps)
        {
            step.PropertyChanged -= Step_PropertyChanged;
        }
    }

    private void Steps_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Move)
        {
            if (sender is ObservableCollection<SchemeWorkStepItem> movedSteps)
            {
                RefreshStepDisplayOrders(movedSteps);
            }

            OnPropertyChanged(nameof(Steps));
            return;
        }

        if (e.NewItems is not null)
        {
            foreach (SchemeWorkStepItem step in e.NewItems.OfType<SchemeWorkStepItem>())
            {
                step.PropertyChanged += Step_PropertyChanged;
            }
        }

        if (e.OldItems is not null)
        {
            foreach (SchemeWorkStepItem step in e.OldItems.OfType<SchemeWorkStepItem>())
            {
                step.PropertyChanged -= Step_PropertyChanged;
            }
        }

        if (sender is ObservableCollection<SchemeWorkStepItem> changedSteps)
        {
            RefreshStepDisplayOrders(changedSteps);
        }

        OnPropertyChanged(nameof(Steps));
        OnPropertyChanged(nameof(StepCount));
    }

    private void Step_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SchemeWorkStepItem.WorkStepId)
            or nameof(SchemeWorkStepItem.ProductName)
            or nameof(SchemeWorkStepItem.SchemeStepName)
            or nameof(SchemeWorkStepItem.OperationSummary))
        {
            OnPropertyChanged(nameof(Steps));
        }
    }

    private static void RefreshStepDisplayOrders(ObservableCollection<SchemeWorkStepItem> steps)
    {
        for (int index = 0; index < steps.Count; index++)
        {
            steps[index].DisplayOrder = index + 1;
        }
    }

    #endregion

    #region 复制方法

    public SchemeProfile Clone()
    {
        return new SchemeProfile
        {
            Id = Id,
            SchemeName = SchemeName,
            ProductName = ProductName,
            Steps = new ObservableCollection<SchemeWorkStepItem>(Steps.Select(step => step.Clone()))
        };
    }

    #endregion
}

/// <summary>
/// 方案中的工步引用快照。
/// </summary>
public sealed class SchemeWorkStepItem : ViewModelProperties
{
    private const string DisplayedViewDataSourceId = "__display_to_view__";

    #region 私有字段

    private string _id = Guid.NewGuid().ToString("N");
    private string _workStepId = string.Empty;
    private string _productName = string.Empty;
    private string _stepName = string.Empty;
    private string _schemeStepName = string.Empty;
    private DateTime _lastModifiedAt = DateTime.Now;
    private string _operationSummary = string.Empty;
    private int _displayOrder = 1;
    private ObservableCollection<SchemeWorkStepParameter> _parameters = new();

    #endregion

    #region 绑定属性

    public string Id
    {
        get => _id;
        set => SetField(ref _id, string.IsNullOrWhiteSpace(value) ? Guid.NewGuid().ToString("N") : value.Trim());
    }

    public string WorkStepId
    {
        get => _workStepId;
        set => SetField(ref _workStepId, value ?? string.Empty, true);
    }

    public string ProductName
    {
        get => _productName;
        set => SetField(ref _productName, value ?? string.Empty, true);
    }

    public string StepName
    {
        get => _stepName;
        set
        {
            if (SetField(ref _stepName, value ?? string.Empty, true))
            {
                OnPropertyChanged(nameof(SchemeStepName));
            }
        }
    }

    public string SchemeStepName
    {
        get => string.IsNullOrWhiteSpace(_schemeStepName) ? StepName : _schemeStepName;
        set
        {
            string normalizedValue = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
            string storedValue = string.Equals(normalizedValue, StepName, StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : normalizedValue;

            if (SetField(ref _schemeStepName, storedValue, true))
            {
                LastModifiedAt = DateTime.Now;
            }
        }
    }

    public DateTime LastModifiedAt
    {
        get => _lastModifiedAt;
        set
        {
            DateTime normalizedValue = value == default ? DateTime.Now : value;
            if (SetField(ref _lastModifiedAt, normalizedValue))
            {
                OnPropertyChanged(nameof(LastModifiedText));
            }
        }
    }

    public string OperationSummary
    {
        get => _operationSummary;
        set => SetField(ref _operationSummary, value ?? string.Empty, true);
    }

    [JsonIgnore]
    public int DisplayOrder
    {
        get => _displayOrder;
        set => SetField(ref _displayOrder, Math.Max(1, value));
    }

    public ObservableCollection<SchemeWorkStepParameter> Parameters
    {
        get => _parameters;
        set
        {
            if (ReferenceEquals(_parameters, value))
            {
                return;
            }

            _parameters = value ?? new ObservableCollection<SchemeWorkStepParameter>();
            OnPropertyChanged();
        }
    }

    [JsonIgnore]
    public string LastModifiedText => $"最后修改：{LastModifiedAt:yyyy-MM-dd HH:mm:ss}";

    #endregion

    #region 工厂与复制方法

    public static SchemeWorkStepItem FromWorkStep(WorkStepProfile workStep)
    {
        return new SchemeWorkStepItem
        {
            WorkStepId = workStep.Id,
            ProductName = workStep.ProductName,
            StepName = workStep.StepName,
            LastModifiedAt = workStep.LastModifiedAt,
            OperationSummary = workStep.OperationSummary,
            Parameters = CreateParametersFromWorkStep(workStep)
        };
    }

    public SchemeWorkStepItem Clone()
    {
        return new SchemeWorkStepItem
        {
            Id = Id,
            WorkStepId = WorkStepId,
            ProductName = ProductName,
            StepName = StepName,
            OperationSummary = OperationSummary,
            SchemeStepName = _schemeStepName,
            LastModifiedAt = LastModifiedAt,
            Parameters = new ObservableCollection<SchemeWorkStepParameter>(Parameters.Select(parameter => parameter.Clone()))
        };
    }

    public static ObservableCollection<SchemeWorkStepParameter> CreateParametersFromWorkStep(
        WorkStepProfile workStep,
        IEnumerable<SchemeWorkStepParameter>? existingParameters = null)
    {
        List<string> displayJudgeTypeOptions = workStep.Steps
            .Where(operation => operation.ShowDataToView && !string.IsNullOrWhiteSpace(operation.ViewJudgeType))
            .Select(operation => operation.ViewJudgeType.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Dictionary<string, SchemeWorkStepParameter> existingBySource = (existingParameters ?? Enumerable.Empty<SchemeWorkStepParameter>())
            .Where(parameter => parameter is not null)
            .GroupBy(parameter => BuildParameterSourceKey(parameter.SourceOperationId, parameter.SourceParameterId), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        Dictionary<string, SchemeWorkStepParameter> existingByName = (existingParameters ?? Enumerable.Empty<SchemeWorkStepParameter>())
            .Where(parameter => parameter is not null && !string.IsNullOrWhiteSpace(parameter.ParameterName))
            .GroupBy(parameter => parameter.ParameterName.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        ObservableCollection<SchemeWorkStepParameter> parameters = new();
        int parameterIndex = 1;
        HashSet<string> addedDisplayedTypeKeys = new(StringComparer.OrdinalIgnoreCase);

        foreach (WorkStepOperation operation in workStep.Steps)
        {
            foreach (WorkStepOperationParameter parameter in operation.Parameters.OrderBy(item => item.Sequence))
            {
                if (!IsSchemeVisibleParameter(parameter))
                {
                    continue;
                }

                string parameterName = ResolveParameterName(parameter, parameterIndex);
                string sourceKey = BuildParameterSourceKey(operation.Id, parameter.Id);

                if (!existingBySource.TryGetValue(sourceKey, out SchemeWorkStepParameter? existingParameter) &&
                    !string.IsNullOrWhiteSpace(parameterName))
                {
                    existingByName.TryGetValue(parameterName, out existingParameter);
                }

                SchemeWorkStepParameter schemeParameter = existingParameter?.Clone() ?? new SchemeWorkStepParameter();
                schemeParameter.SourceOperationId = operation.Id;
                schemeParameter.SourceParameterId = parameter.Id;
                schemeParameter.ParameterName = parameterName;
                schemeParameter.ReplaceJudgeTypeOptions(Array.Empty<string>());
                parameters.Add(schemeParameter);
                parameterIndex++;
            }

            if (!IsSchemeVisibleOperation(operation))
            {
                continue;
            }

            string displayedParameterName = ResolveDisplayedViewDataName(operation, parameterIndex);
            string displayedTypeKey = ResolveDisplayedViewDataKey(operation, displayedParameterName, parameterIndex);
            if (!addedDisplayedTypeKeys.Add(displayedTypeKey))
            {
                continue;
            }

            string displayedSourceKey = BuildParameterSourceKey(DisplayedViewDataSourceId, displayedTypeKey);

            if (!existingBySource.TryGetValue(displayedSourceKey, out SchemeWorkStepParameter? displayedExistingParameter) &&
                !string.IsNullOrWhiteSpace(displayedParameterName))
            {
                existingByName.TryGetValue(displayedParameterName, out displayedExistingParameter);
            }

            bool isNewDisplayedParameter = displayedExistingParameter is null;
            SchemeWorkStepParameter displayedSchemeParameter = displayedExistingParameter?.Clone() ?? new SchemeWorkStepParameter();
            displayedSchemeParameter.SourceOperationId = DisplayedViewDataSourceId;
            displayedSchemeParameter.SourceParameterId = displayedTypeKey;
            displayedSchemeParameter.ParameterName = displayedParameterName;
            displayedSchemeParameter.ParameterType = isNewDisplayedParameter ? "判断值" : displayedSchemeParameter.ParameterType;
            displayedSchemeParameter.JudgeType = operation.ViewJudgeType;
            displayedSchemeParameter.JudgeCondition = operation.ViewJudgeCondition;
            displayedSchemeParameter.ReplaceJudgeTypeOptions(displayJudgeTypeOptions);
            parameters.Add(displayedSchemeParameter);
            parameterIndex++;
        }

        return parameters;
    }

    private static bool IsSchemeVisibleOperation(WorkStepOperation operation)
    {
        return operation.ShowDataToView;
    }

    private static bool IsSchemeVisibleParameter(WorkStepOperationParameter parameter)
    {
        return string.Equals(parameter.Type, "工步值", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveParameterName(WorkStepOperationParameter parameter, int index)
    {
        if (!string.IsNullOrWhiteSpace(parameter.Value))
        {
            return parameter.Value.Trim();
        }

        if (!string.IsNullOrWhiteSpace(parameter.ParameterName))
        {
            return parameter.ParameterName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(parameter.Description))
        {
            return parameter.Description.Trim();
        }

        return $"参数 {index}";
    }

    private static string ResolveDisplayedViewDataName(WorkStepOperation operation, int index)
    {
        if (!string.IsNullOrWhiteSpace(operation.ViewJudgeType))
        {
            return operation.ViewJudgeType.Trim();
        }

        if (!string.IsNullOrWhiteSpace(operation.ViewDataName))
        {
            return operation.ViewDataName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(operation.ReturnValue))
        {
            return operation.ReturnValue.Trim();
        }

        return $"显示数据 {index}";
    }

    private static string ResolveDisplayedViewDataKey(WorkStepOperation operation, string displayedParameterName, int index)
    {
        if (!string.IsNullOrWhiteSpace(operation.ViewJudgeType))
        {
            return operation.ViewJudgeType.Trim();
        }

        if (!string.IsNullOrWhiteSpace(displayedParameterName))
        {
            return displayedParameterName.Trim();
        }

        return $"显示数据_{index}";
    }

    private static string BuildParameterSourceKey(string? operationId, string? parameterId)
    {
        return $"{operationId?.Trim() ?? string.Empty}::{parameterId?.Trim() ?? string.Empty}";
    }

    #endregion
}

/// <summary>
/// 方案导入导出包，包含方案本体、产品和完整工步内容。
/// </summary>
public sealed class SchemeConfigurationPackage
{
    public int Version { get; set; } = 1;

    public SchemeProfile? Scheme { get; set; }

    public ProductProfile? Product { get; set; }

    public ObservableCollection<WorkStepProfile> WorkSteps { get; set; } = new();
}

/// <summary>
/// 单个产品下的工步配置文件。
/// </summary>
public sealed class ProductWorkStepConfiguration
{
    public string ProductName { get; set; } = string.Empty;

    public ObservableCollection<WorkStepProfile> WorkSteps { get; set; } = new();
}
