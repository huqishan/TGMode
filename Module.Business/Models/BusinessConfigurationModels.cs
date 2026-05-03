using ControlLibrary;
using System;
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

    #endregion

    #region 集合通知

    private void AttachSteps(ObservableCollection<WorkStepOperation> steps)
    {
        steps.CollectionChanged += Steps_CollectionChanged;
        foreach (WorkStepOperation step in steps)
        {
            step.PropertyChanged += Step_PropertyChanged;
        }
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

        RaiseStepSummaryChanged();
    }

    private void Step_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WorkStepOperation.OperationObject)
            or nameof(WorkStepOperation.InvokeMethod)
            or nameof(WorkStepOperation.DisplayText))
        {
            RaiseStepSummaryChanged();
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
            Steps = new ObservableCollection<WorkStepOperation>(Steps.Select(step => step.Clone()))
        };

        return clone;
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
    private string _operationObject = "操作对象";
    private string _invokeMethod = "调用方法";

    #endregion

    #region 绑定属性

    public string Id
    {
        get => _id;
        set => SetField(ref _id, string.IsNullOrWhiteSpace(value) ? Guid.NewGuid().ToString("N") : value.Trim());
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

    [JsonIgnore]
    public string DisplayText => $"{OperationObject}.{InvokeMethod}";

    #endregion

    #region 复制方法

    public WorkStepOperation Clone()
    {
        return new WorkStepOperation
        {
            Id = Id,
            OperationObject = OperationObject,
            InvokeMethod = InvokeMethod
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
    }

    private void DetachSteps(ObservableCollection<SchemeWorkStepItem> steps)
    {
        steps.CollectionChanged -= Steps_CollectionChanged;
    }

    private void Steps_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(StepCount));
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
    #region 私有字段

    private string _id = Guid.NewGuid().ToString("N");
    private string _workStepId = string.Empty;
    private string _productName = string.Empty;
    private string _stepName = string.Empty;
    private string _operationSummary = string.Empty;

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
        set => SetField(ref _stepName, value ?? string.Empty, true);
    }

    public string OperationSummary
    {
        get => _operationSummary;
        set => SetField(ref _operationSummary, value ?? string.Empty, true);
    }

    #endregion

    #region 工厂与复制方法

    public static SchemeWorkStepItem FromWorkStep(WorkStepProfile workStep)
    {
        return new SchemeWorkStepItem
        {
            WorkStepId = workStep.Id,
            ProductName = workStep.ProductName,
            StepName = workStep.StepName,
            OperationSummary = workStep.OperationSummary
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
            OperationSummary = OperationSummary
        };
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
