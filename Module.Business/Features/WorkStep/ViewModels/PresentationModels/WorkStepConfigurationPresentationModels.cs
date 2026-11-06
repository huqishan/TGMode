using ControlLibrary;
using Module.Business.Features.OperationEditing.ViewModels.PresentationModels;
using Newtonsoft.Json;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace Module.Business.Features.WorkStep.ViewModels.PresentationModels;

/// <summary>
/// 独立业务配置。业务只描述可复用的操作步骤，不承载方案中的启用、排序和重测配置。
/// </summary>
public sealed class WorkStepProfile : ViewModelProperties
{
    #region 私有字段

    private string _id = Guid.NewGuid().ToString("N");
    private string _name = "业务 1";
    private DateTime _lastModifiedAt = DateTime.Now;
    private ObservableCollection<WorkStepOperation> _operations = new();
    private bool _isModified;

    #endregion

    #region 构造与属性

    public WorkStepProfile()
    {
        AttachOperations(_operations);
    }

    public string Id
    {
        get => _id;
        set => SetField(ref _id, string.IsNullOrWhiteSpace(value) ? Guid.NewGuid().ToString("N") : value.Trim());
    }

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

    [JsonIgnore]
    public int OperationCount => Operations.Count;

    [JsonIgnore]
    public string LastModifiedText => $"最后修改：{LastModifiedAt:yyyy-MM-dd HH:mm:ss}";

    [JsonIgnore]
    public bool IsModified => _isModified;

    #endregion

    #region 步骤变更跟踪

    private void AttachOperations(ObservableCollection<WorkStepOperation> operations)
    {
        operations.CollectionChanged += Operations_CollectionChanged;
        foreach (WorkStepOperation operation in operations)
        {
            operation.PropertyChanged += Operation_PropertyChanged;
        }
    }

    private void DetachOperations(ObservableCollection<WorkStepOperation> operations)
    {
        operations.CollectionChanged -= Operations_CollectionChanged;
        foreach (WorkStepOperation operation in operations)
        {
            operation.PropertyChanged -= Operation_PropertyChanged;
        }
    }

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

    public void MarkModified()
    {
        if (_isModified)
        {
            return;
        }

        _isModified = true;
        OnPropertyChanged(nameof(IsModified));
    }

    public void AcceptChanges(DateTime savedAt)
    {
        LastModifiedAt = savedAt;
        AcceptChanges();
    }

    public void AcceptChanges()
    {
        if (!_isModified)
        {
            return;
        }

        _isModified = false;
        OnPropertyChanged(nameof(IsModified));
    }

    public WorkStepProfile Clone()
    {
        WorkStepProfile clone = new()
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
