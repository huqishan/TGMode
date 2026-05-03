using ControlLibrary;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Module.MES.ViewModels
{
    public sealed partial class DataStructureConfigViewModel : ViewModelProperties
    {

    }

    public enum DataStructureFieldDropMode
    {
        AsRoot,
        Before,
        After,
        AsChild
    }
    #region 数据结构选项模型

    /// <summary>
    /// 数据结构配置页下拉选项模型。
    /// </summary>
    public sealed class DataStructureTypeOption : ViewModelProperties
    {
        #region 构造方法

        public DataStructureTypeOption(string value, string displayName)
        {
            Value = value;
            DisplayName = displayName;
        }

        #endregion

        #region 选项属性

        public string Value { get; }

        public string DisplayName { get; }

        #endregion
    }

    #endregion

    #region 结构类型常量

    /// <summary>
    /// 数据结构配置支持的结构类型。
    /// </summary>
    public static class DataStructureTypes
    {
        #region 类型字段

        public const string Soap = "SOAP";
        public const string Json = "JSON";
        public const string Join = "JOIN";

        #endregion

        #region 类型选项

        public static IReadOnlyList<DataStructureTypeOption> Options { get; } = new[]
        {
            new DataStructureTypeOption(Soap, Soap),
            new DataStructureTypeOption(Json, Json),
            new DataStructureTypeOption(Join, Join)
        };

        #endregion
    }

    #endregion

    #region 字段类型常量

    /// <summary>
    /// 数据结构字段支持的数据类型。
    /// </summary>
    public static class DataStructureFieldDataTypes
    {
        #region 类型字段

        public const string Json = "JSON";
        public const string String = "String";
        public const string List = "List";
        public const string Model = "Model";
        public const string Array = "Array";
        public const string Bool = "Bool";
        public const string Int = "Int";
        public const string Double = "Double";
        public const string DateTime = "DateTime";
        public const string XmlNull = "XMLNULL";
        public const string XmlNamespace = "XMLNamespac";
        public const string StepModel = "StepModel";

        #endregion

        #region 类型选项

        public static IReadOnlyList<DataStructureTypeOption> Options { get; } = new[]
        {
            new DataStructureTypeOption(Json, Json),
            new DataStructureTypeOption(String, String),
            new DataStructureTypeOption(List, List),
            new DataStructureTypeOption(Model, Model),
            new DataStructureTypeOption(Array, Array),
            new DataStructureTypeOption(Bool, Bool),
            new DataStructureTypeOption(Int, Int),
            new DataStructureTypeOption(Double, Double),
            new DataStructureTypeOption(DateTime, DateTime),
            new DataStructureTypeOption(XmlNull, XmlNull),
            new DataStructureTypeOption(XmlNamespace, XmlNamespace),
            new DataStructureTypeOption(StepModel, StepModel)
        };

        #endregion

        #region 类型标准化方法

        public static string Normalize(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return String;
            }

            string normalized = value.Trim();
            return Options.FirstOrDefault(option =>
                string.Equals(option.Value, normalized, StringComparison.OrdinalIgnoreCase))?.Value ?? normalized;
        }

        #endregion
    }

    #endregion

    #region 数据结构配置模型

    /// <summary>
    /// 单个数据结构配置，供列表和编辑区绑定。
    /// </summary>
    public sealed class DataStructureProfile : ViewModelProperties
    {
        #region 字段

        private string _name = string.Empty;
        private string _structureType = DataStructureTypes.Json;
        private DateTime _lastModifiedAt = DateTime.Now;
        private ObservableCollection<DataStructureLayout> _structure = new();

        #endregion

        #region 构造方法

        public DataStructureProfile()
        {
            HookChildren(_structure);
        }

        #endregion

        #region 基础属性

        public string Name
        {
            get => _name;
            set
            {
                if (SetField(ref _name, value?.Trim() ?? string.Empty))
                {
                    Touch();
                }
            }
        }

        public string StructureType
        {
            get => _structureType;
            set
            {
                if (SetField(ref _structureType, value))
                {
                    Touch();
                }
            }
        }

        public DateTime LastModifiedAt
        {
            get => _lastModifiedAt;
            set
            {
                if (SetField(ref _lastModifiedAt, value == default ? DateTime.Now : value))
                {
                    OnPropertyChanged(nameof(Summary));
                }
            }
        }

        public ObservableCollection<DataStructureLayout> Structure
        {
            get => _structure;
            set
            {
                ObservableCollection<DataStructureLayout> nextChildren = value ?? new ObservableCollection<DataStructureLayout>();
                if (ReferenceEquals(_structure, nextChildren))
                {
                    return;
                }

                UnhookChildren(_structure);
                _structure = nextChildren;
                HookChildren(_structure);
                OnPropertyChanged();
                Touch();
            }
        }

        public string Summary => $"{StructureType} · 修改于 {LastModifiedAt:yyyy-MM-dd HH:mm}";

        #endregion

        #region 复制与加载方法

        public DataStructureProfile Clone(string name)
        {
            DataStructureProfile clone = new()
            {
                Name = name,
                StructureType = StructureType,
                LastModifiedAt = DateTime.Now
            };

            foreach (DataStructureLayout field in Structure)
            {
                clone.Structure.Add(field.Clone());
            }

            return clone;
        }

        internal void AcceptLoadedState(DateTime lastModifiedAt)
        {
            _lastModifiedAt = lastModifiedAt == default ? DateTime.Now : lastModifiedAt;
            OnPropertyChanged(nameof(LastModifiedAt));
            OnPropertyChanged(nameof(Summary));
        }

        #endregion

        #region 子节点订阅方法

        private void Children_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems is not null)
            {
                foreach (DataStructureLayout field in e.OldItems)
                {
                    field.ContentChanged -= Field_ContentChanged;
                }
            }

            if (e.NewItems is not null)
            {
                foreach (DataStructureLayout field in e.NewItems)
                {
                    field.ContentChanged += Field_ContentChanged;
                }
            }

            OnPropertyChanged(nameof(Structure));
            Touch();
        }

        private void Field_ContentChanged(object? sender, EventArgs e)
        {
            Touch();
        }

        private void HookChildren(ObservableCollection<DataStructureLayout> children)
        {
            children.CollectionChanged += Children_CollectionChanged;
            foreach (DataStructureLayout field in children)
            {
                field.ContentChanged += Field_ContentChanged;
            }
        }

        private void UnhookChildren(ObservableCollection<DataStructureLayout> children)
        {
            children.CollectionChanged -= Children_CollectionChanged;
            foreach (DataStructureLayout field in children)
            {
                field.ContentChanged -= Field_ContentChanged;
            }
        }

        private void Touch()
        {
            LastModifiedAt = DateTime.Now;
        }

        #endregion
    }

    #endregion

    #region 数据结构配置存储模型

    /// <summary>
    /// 数据结构配置的本地文件存储模型。
    /// </summary>
    public sealed class DataStructureProfileDocument : ViewModelProperties
    {
        #region 存储属性

        public string? Name { get; set; }

        public string? StructureType { get; set; }

        public DateTime LastModifiedAt { get; set; }

        public List<DataStructureLayoutDocument>? Structure { get; set; }

        #endregion

        #region 转换方法

        public static DataStructureProfileDocument FromProfile(DataStructureProfile profile)
        {
            return new DataStructureProfileDocument
            {
                Name = profile.Name,
                StructureType = profile.StructureType,
                LastModifiedAt = profile.LastModifiedAt,
                Structure = profile.Structure.Select(DataStructureLayoutDocument.FromLayout).ToList()
            };
        }

        public DataStructureProfile ToProfile(string fallbackName, DateTime fallbackModifiedAt)
        {
            string name = string.IsNullOrWhiteSpace(fallbackName) ? "数据结构" : fallbackName.Trim();
            DataStructureProfile profile = new()
            {
                Name = name,
                StructureType = string.IsNullOrWhiteSpace(StructureType)
                    ? "JSON"
                    : StructureType
            };

            foreach (DataStructureLayoutDocument field in Structure ?? Enumerable.Empty<DataStructureLayoutDocument>())
            {
                profile.Structure.Add(field.ToLayout());
            }

            DateTime lastModifiedAt = LastModifiedAt == default ? fallbackModifiedAt : LastModifiedAt;
            profile.AcceptLoadedState(lastModifiedAt);
            return profile;
        }

        #endregion
    }

    #endregion

    #region 数据结构字段存储模型

    /// <summary>
    /// 数据结构字段的本地文件存储模型。
    /// </summary>
    public sealed class DataStructureLayoutDocument : ViewModelProperties
    {
        #region 存储属性

        public string? ClientCode { get; set; }

        public string? MesCode { get; set; }

        public string? DataType { get; set; }

        public string? DefaultValue { get; set; }

        public bool IsNull { get; set; }

        public bool IsWhile { get; set; }

        public int WhileCount { get; set; }

        public int KeepCount { get; set; }

        public string? XmlNamespace { get; set; }

        public string? JudgeValue { get; set; }

        public string? OKText { get; set; }

        public string? NGText { get; set; }

        public List<DataStructureLayoutDocument>? Children { get; set; }

        #endregion

        #region 转换方法

        public static DataStructureLayoutDocument FromLayout(DataStructureLayout layout)
        {
            return new DataStructureLayoutDocument
            {
                ClientCode = layout.ClientCode,
                MesCode = layout.MesCode,
                DataType = layout.DataType,
                DefaultValue = layout.DefaultValue,
                IsNull = layout.IsNull,
                IsWhile = layout.IsWhile,
                WhileCount = layout.WhileCount,
                KeepCount = layout.KeepCount,
                XmlNamespace = layout.XmlNamespace,
                JudgeValue = layout.JudgeValue,
                OKText = layout.OKText,
                NGText = layout.NGText,
                Children = layout.Children.Select(FromLayout).ToList()
            };
        }

        public DataStructureLayout ToLayout()
        {
            DataStructureLayout layout = new()
            {
                ClientCode = ClientCode ?? string.Empty,
                MesCode = MesCode ?? string.Empty,
                DataType = DataStructureFieldDataTypes.Normalize(DataType),
                DefaultValue = DefaultValue ?? string.Empty,
                IsNull = IsNull,
                WhileCount = IsWhile && WhileCount <= 0 ? 1 : WhileCount,
                KeepCount = KeepCount,
                XmlNamespace = XmlNamespace ?? string.Empty,
                JudgeValue = JudgeValue ?? string.Empty,
                OKText = OKText ?? string.Empty,
                NGText = NGText ?? string.Empty
            };

            foreach (DataStructureLayoutDocument child in Children ?? Enumerable.Empty<DataStructureLayoutDocument>())
            {
                layout.Children.Add(child.ToLayout());
            }

            return layout;
        }

        #endregion
    }

    #endregion

    #region 数据结构字段模型

    /// <summary>
    /// 数据结构字段节点，供 TreeView 和详情抽屉绑定。
    /// </summary>
    public sealed class DataStructureLayout : ViewModelProperties
    {
        #region 字段

        private string _clientCode = string.Empty;
        private string _mesCode = string.Empty;
        private string _dataType = DataStructureFieldDataTypes.String;
        private string _defaultValue = string.Empty;
        private bool _isNull;
        private int _whileCount;
        private int _keepCount;
        private string _xmlNamespace = string.Empty;
        private string _judgeValue = string.Empty;
        private string _okText = string.Empty;
        private string _ngText = string.Empty;
        private bool _isSelected;
        private ObservableCollection<DataStructureLayout> _children = new();

        #endregion

        #region 构造与事件

        public DataStructureLayout()
        {
            HookChildren(_children);
        }

        internal event EventHandler? ContentChanged;

        #endregion

        #region 绑定属性

        [JsonIgnore]
        public IReadOnlyList<DataStructureTypeOption> DataTypes => DataStructureFieldDataTypes.Options;

        [JsonIgnore]
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (Equals(_isSelected, value))
                {
                    return;
                }

                _isSelected = value;
                OnPropertyChanged();
            }
        }

        public string ClientCode
        {
            get => _clientCode;
            set => SetContentField(ref _clientCode, value ?? string.Empty);
        }

        public string MesCode
        {
            get => _mesCode;
            set => SetContentField(ref _mesCode, value ?? string.Empty);
        }

        public string DataType
        {
            get => _dataType;
            set => SetContentField(ref _dataType, DataStructureFieldDataTypes.Normalize(value));
        }

        public string DefaultValue
        {
            get => _defaultValue;
            set => SetContentField(ref _defaultValue, value ?? string.Empty);
        }

        public bool IsNull
        {
            get => _isNull;
            set => SetContentField(ref _isNull, value);
        }
        public bool IsWhile
        {
            get => _whileCount > 0;
        }

        public int WhileCount
        {
            get => _whileCount;
            set => SetContentField(ref _whileCount, value);
        }

        public int KeepCount
        {
            get => _keepCount;
            set => SetContentField(ref _keepCount, value);
        }

        public string XmlNamespace
        {
            get => _xmlNamespace;
            set => SetContentField(ref _xmlNamespace, value ?? string.Empty);
        }

        public string JudgeValue
        {
            get => _judgeValue;
            set => SetContentField(ref _judgeValue, value ?? string.Empty);
        }

        public string OKText
        {
            get => _okText;
            set => SetContentField(ref _okText, value ?? string.Empty);
        }

        public string NGText
        {
            get => _ngText;
            set => SetContentField(ref _ngText, value ?? string.Empty);
        }

        public ObservableCollection<DataStructureLayout> Children
        {
            get => _children;
            set
            {
                ObservableCollection<DataStructureLayout> nextChildren = value ?? new ObservableCollection<DataStructureLayout>();
                if (ReferenceEquals(_children, nextChildren))
                {
                    return;
                }

                UnhookChildren(_children);
                _children = nextChildren;
                HookChildren(_children);
                OnPropertyChanged();
                OnContentChanged();
            }
        }

        #endregion

        #region 复制方法

        public DataStructureLayout Clone()
        {
            DataStructureLayout clone = new()
            {
                MesCode = MesCode,
                ClientCode = ClientCode,
                DataType = DataType,
                DefaultValue = DefaultValue,
                IsNull = IsNull,
                WhileCount = WhileCount,
                KeepCount = KeepCount,
                XmlNamespace = XmlNamespace,
                JudgeValue = JudgeValue,
                OKText = OKText,
                NGText = NGText
            };

            foreach (DataStructureLayout child in Children)
            {
                clone.Children.Add(child.Clone());
            }

            return clone;
        }

        #endregion

        #region 子节点订阅方法

        private void Children_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems is not null)
            {
                foreach (DataStructureLayout field in e.OldItems)
                {
                    field.ContentChanged -= Child_ContentChanged;
                }
            }

            if (e.NewItems is not null)
            {
                foreach (DataStructureLayout field in e.NewItems)
                {
                    field.ContentChanged += Child_ContentChanged;
                }
            }

            OnPropertyChanged(nameof(Children));
            OnContentChanged();
        }

        private void Child_ContentChanged(object? sender, EventArgs e)
        {
            OnContentChanged();
        }

        private void HookChildren(ObservableCollection<DataStructureLayout> children)
        {
            children.CollectionChanged += Children_CollectionChanged;
            foreach (DataStructureLayout field in children)
            {
                field.ContentChanged += Child_ContentChanged;
            }
        }

        private void UnhookChildren(ObservableCollection<DataStructureLayout> children)
        {
            children.CollectionChanged -= Children_CollectionChanged;
            foreach (DataStructureLayout field in children)
            {
                field.ContentChanged -= Child_ContentChanged;
            }
        }

        #endregion

        #region 内容变更通知方法

        private bool SetContentField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (!SetField(ref field, value, propertyName))
            {
                return false;
            }

            OnContentChanged();
            return true;
        }

        private void OnContentChanged()
        {
            ContentChanged?.Invoke(this, EventArgs.Empty);
        }

        #endregion
    }

    #endregion
}
