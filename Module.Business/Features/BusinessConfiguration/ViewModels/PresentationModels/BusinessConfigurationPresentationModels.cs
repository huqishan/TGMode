using ControlLibrary;
using Module.Business.Features.OperationEditing.ViewModels.PresentationModels;
using Newtonsoft.Json;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace Module.Business.Features.BusinessConfiguration.ViewModels.PresentationModels;

/// <summary>
/// 独立业务配置。业务只描述可复用的操作步骤，不承载方案中的启用、排序和重测配置。
/// </summary>
public sealed class BusinessProfile : ViewModelProperties
{
    #region 私有字段

    /// <summary>业务唯一标识，新建时自动生成。</summary>
    private string _id = Guid.NewGuid().ToString("N");

    /// <summary>业务名称。</summary>
    private string _name = "业务 1";

    /// <summary>最后修改时间。</summary>
    private DateTime _lastModifiedAt = DateTime.Now;

    /// <summary>业务包含的操作步骤集合。</summary>
    private ObservableCollection<WorkStepOperation> _operations = new();

    /// <summary>是否有未保存的修改。</summary>
    private bool _isModified;

    #endregion

    #region 构造与属性

    /// <summary>构造函数，默认挂接操作集合的变更监听。</summary>
    public BusinessProfile()
    {
        AttachOperations(_operations);
    }

    /// <summary>业务唯一标识，空值时回退为自动生成。</summary>
    public string Id
    {
        get => _id;
        set => SetField(ref _id, string.IsNullOrWhiteSpace(value) ? Guid.NewGuid().ToString("N") : value.Trim());
    }

    /// <summary>业务名称，变化时标记为已修改。</summary>
    public string Name
    {
        get => _name;
        set
        {
            if (SetField(ref _name, value ?? string.Empty, true))
            {
                MarkModified();
            }
        }
    }

    /// <summary>最后修改时间，缺省值回退为当前时间。</summary>
    public DateTime LastModifiedAt
    {
        get => _lastModifiedAt;
        set
        {
            DateTime normalized = value == default ? DateTime.Now : value;
            if (SetField(ref _lastModifiedAt, normalized))
            {
                OnPropertyChanged(nameof(LastModifiedText));
            }
        }
    }

    /// <summary>业务包含的操作步骤集合，替换时重新挂接变更监听。</summary>
    public ObservableCollection<WorkStepOperation> Operations
    {
        get => _operations;
        set
        {
            if (ReferenceEquals(_operations, value))
            {
                return;
            }

            DetachOperations(_operations);
            _operations = value ?? new ObservableCollection<WorkStepOperation>();
            AttachOperations(_operations);
            OnPropertyChanged();
            OnPropertyChanged(nameof(OperationCount));
            MarkModified();
        }
    }

    /// <summary>操作数量，仅用于界面展示，不参与序列化。</summary>
    [JsonIgnore]
    public int OperationCount => Operations.Count;

    /// <summary>最后修改时间展示文案。</summary>
    [JsonIgnore]
    public string LastModifiedText => $"最后修改：{LastModifiedAt:yyyy-MM-dd HH:mm:ss}";

    /// <summary>是否有未保存的修改，仅用于界面提示。</summary>
    [JsonIgnore]
    public bool IsModified => _isModified;

    #endregion

    #region 步骤变更跟踪

    /// <summary>挂接操作集合与每个操作的变更监听，用于跟踪修改状态。</summary>
    private void AttachOperations(ObservableCollection<WorkStepOperation> operations)
    {
        operations.CollectionChanged += Operations_CollectionChanged;
        foreach (WorkStepOperation operation in operations)
        {
            operation.PropertyChanged += Operation_PropertyChanged;
        }
    }

    /// <summary>解除操作集合与每个操作的变更监听。</summary>
    private void DetachOperations(ObservableCollection<WorkStepOperation> operations)
    {
        operations.CollectionChanged -= Operations_CollectionChanged;
        foreach (WorkStepOperation operation in operations)
        {
            operation.PropertyChanged -= Operation_PropertyChanged;
        }
    }

    /// <summary>操作集合变化：同步挂接/解除新元素的监听并标记修改。</summary>
    private void Operations_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (WorkStepOperation operation in e.NewItems.OfType<WorkStepOperation>())
            {
                operation.PropertyChanged += Operation_PropertyChanged;
            }
        }

        if (e.OldItems is not null)
        {
            foreach (WorkStepOperation operation in e.OldItems.OfType<WorkStepOperation>())
            {
                operation.PropertyChanged -= Operation_PropertyChanged;
            }
        }

        OnPropertyChanged(nameof(Operations));
        OnPropertyChanged(nameof(OperationCount));
        MarkModified();
    }

    /// <summary>单个操作属性变化：勾选状态除外，均视为业务内容变化并标记修改。</summary>
    private void Operation_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 勾选状态仅服务于界面批量操作，不属于业务业务内容，不应触发最后修改时间变化。
        if (e.PropertyName != nameof(WorkStepOperation.IsChecked))
        {
            OnPropertyChanged(nameof(Operations));
            MarkModified();
        }
    }

    #endregion

    #region 保存状态与克隆

    /// <summary>标记为已修改并通知界面刷新提示状态。</summary>
    public void MarkModified()
    {
        if (_isModified)
        {
            return;
        }

        _isModified = true;
        OnPropertyChanged(nameof(IsModified));
    }

    /// <summary>保存成功后按保存时间记录最后修改时间并清除修改标记。</summary>
    /// <param name="savedAt">保存时间。</param>
    public void AcceptChanges(DateTime savedAt)
    {
        LastModifiedAt = savedAt;
        AcceptChanges();
    }

    /// <summary>清除修改标记，表示当前内容已与磁盘一致。</summary>
    public void AcceptChanges()
    {
        if (!_isModified)
        {
            return;
        }

        _isModified = false;
        OnPropertyChanged(nameof(IsModified));
    }

    /// <summary>深拷贝当前业务配置（操作步骤逐个克隆），用于复制业务。</summary>
    public BusinessProfile Clone()
    {
        BusinessProfile clone = new()
        {
            Id = Id,
            Name = Name,
            LastModifiedAt = LastModifiedAt,
            Operations = new ObservableCollection<WorkStepOperation>(Operations.Select(operation => operation.Clone()))
        };
        clone.AcceptChanges();
        return clone;
    }

    #endregion
}
