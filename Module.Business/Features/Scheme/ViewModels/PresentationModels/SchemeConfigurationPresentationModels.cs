using ControlLibrary;
using Module.Business.Features.OperationEditing.ViewModels.PresentationModels;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Module.Business.Features.Scheme.ViewModels.PresentationModels
{
    /// <summary>
    /// 方案业务抽屉中的内置业务参数展示行。
    /// </summary>
    public sealed class SchemeWorkStepParameterItem : ViewModelProperties
    {
        private string _name = string.Empty;
        private string _value = string.Empty;
        private string _unit = string.Empty;
        private string _operator = "NA";
        private string _judgeValue = string.Empty;
        private bool _isUsed = true;

        /// <summary>参数或界面显示名称。</summary>
        public string Name { get => _name; set => SetField(ref _name, value ?? string.Empty, true); }

        /// <summary>参数对应的值或来源字段。</summary>
        public string Value { get => _value; set => SetField(ref _value, value ?? string.Empty, true); }

        /// <summary>参数单位。</summary>
        public string Unit { get => _unit; set => SetField(ref _unit, value ?? string.Empty, true); }

        /// <summary>返回参数判断符号。</summary>
        public string Operator { get => _operator; set => SetField(ref _operator, value ?? "=", true); }

        /// <summary>返回参数判断值。</summary>
        public string JudgeValue { get => _judgeValue; set => SetField(ref _judgeValue, value ?? string.Empty, true); }

        /// <summary>方案执行时是否继续使用该参数；切换内置业务后遗留参数默认为不使用。</summary>
        public bool IsUsed { get => _isUsed; set => SetField(ref _isUsed, value, true); }

        /// <summary>创建参数配置副本。</summary>
        public SchemeWorkStepParameterItem Clone() => new()
        {
            Name = Name,
            Value = Value,
            Unit = Unit,
            Operator = Operator,
            JudgeValue = JudgeValue,
            IsUsed = IsUsed
        };
    }

    /// <summary>
    /// 判断条件表格参数行，统一承载输入参数和返回参数。
    /// 编辑过程使用独立字段，只有点击保存后才回写方案业务。
    /// </summary>
    public sealed class SchemeConditionEditorItem : ViewModelProperties
    {
        #region 私有字段

        private string _editableValue = string.Empty;
        private string _unit = string.Empty;
        private string _operator = "NA";
        private string _judgeValue = string.Empty;

        #endregion

        #region 构造与属性

        public SchemeConditionEditorItem(
            SchemeWorkStepParameterItem parameter,
            bool isInputParameter)
        {
            ArgumentNullException.ThrowIfNull(parameter);
            IsInputParameter = isInputParameter;
            ParameterName = isInputParameter ? parameter.Name : parameter.Value;
            _unit = isInputParameter ? string.Empty : parameter.Unit;
            _editableValue = isInputParameter ? parameter.Value : parameter.Name;
            _operator = parameter.Operator;
            _judgeValue = parameter.JudgeValue;
        }

        public bool IsInputParameter { get; }

        public bool IsReturnParameter => !IsInputParameter;

        public string ParameterType => IsInputParameter ? "输入参数" : "返回参数";

        public string ParameterName { get; }

        public string Unit
        {
            get => _unit;
            set => SetField(ref _unit, value ?? string.Empty, true);
        }

        public string EditableValue
        {
            get => _editableValue;
            set => SetField(ref _editableValue, value ?? string.Empty, true);
        }

        public string Operator
        {
            get => _operator;
            set => SetField(ref _operator, value ?? "NA", true);
        }

        public string JudgeValue
        {
            get => _judgeValue;
            set => SetField(ref _judgeValue, value ?? string.Empty, true);
        }

        #endregion
    }

    /// <summary>
    /// 判断条件表格中的业务分组，业务信息作为纵向合并单元格显示。
    /// </summary>
    public sealed class SchemeConditionWorkStepGroup
    {
        public SchemeConditionWorkStepGroup(SchemeWorkStepItem source, IEnumerable<SchemeConditionEditorItem> items)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            Items = new ObservableCollection<SchemeConditionEditorItem>(items ?? Enumerable.Empty<SchemeConditionEditorItem>());
        }

        internal SchemeWorkStepItem Source { get; }

        public string WorkStepName => $"{Source.Num:00} {Source.StepName}";

        public string ParameterSummary =>
            $"输入 {Items.Count(item => item.IsInputParameter)} · 返回 {Items.Count(item => item.IsReturnParameter)}";

        public ObservableCollection<SchemeConditionEditorItem> Items { get; }
    }

    /// <summary>
    /// 业务方案，承载工作流程和执行顺序的顶层容器
    /// </summary>
    public sealed class SchemeProfile : ViewModelProperties
    {
        #region 私有字段

        private string _id = Guid.NewGuid().ToString("N");
        private string _schemeName = "方案 1";
        private DateTime _lastModifiedAt = DateTime.Now;
        private ObservableCollection<SchemeWorkStepItem> _steps = new();
        private bool _isModified;

        #endregion

        #region 构造函数

        public SchemeProfile()
        {
            AttachSteps(_steps);
        }

        #endregion

        #region 属性

        /// <summary>
        /// 方案唯一标识
        /// </summary>
        public string Id
        {
            get => _id;
            set => SetField(ref _id, string.IsNullOrWhiteSpace(value) ? Guid.NewGuid().ToString("N") : value.Trim());
        }

        /// <summary>
        /// 方案名称
        /// </summary>
        public string SchemeName
        {
            get => _schemeName;
            set
            {
                if (SetField(ref _schemeName, value ?? string.Empty, true))
                {
                    MarkModified();
                }
            }
        }

        /// <summary>
        /// 最后修改时间
        /// </summary>
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

        /// <summary>
        /// 业务项集合
        /// </summary>
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
                MarkModified();
            }
        }

        [JsonIgnore]
        public int StepCount => Steps.Count;

        [JsonIgnore]
        public string LastModifiedText => $"最后修改：{LastModifiedAt:yyyy-MM-dd HH:mm:ss}";

        /// <summary>
        /// 当前方案内容是否存在尚未保存的修改；该状态仅用于页面保存流程，不写入配置文件。
        /// </summary>
        [JsonIgnore]
        public bool IsModified => _isModified;

        #endregion

        #region 集合通知

        private void AttachSteps(ObservableCollection<SchemeWorkStepItem> steps)
        {
            steps.CollectionChanged += Steps_CollectionChanged;
            foreach (SchemeWorkStepItem step in steps)
            {
                step.PropertyChanged += Step_PropertyChanged;
            }

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
                OnPropertyChanged(nameof(Steps));
                MarkModified();
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

            OnPropertyChanged(nameof(Steps));
            OnPropertyChanged(nameof(StepCount));
            MarkModified();
        }

        private void Step_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(SchemeWorkStepItem.StepName)
                or nameof(SchemeWorkStepItem.IsStartupEnabled)
                or nameof(SchemeWorkStepItem.IsReTestEnabled)
                or nameof(SchemeWorkStepItem.ReTestCount)
                or nameof(SchemeWorkStepItem.IsConfirmReTest))
            {
                OnPropertyChanged(nameof(Steps));
                MarkModified();
            }
        }

        #endregion

        #region 克隆

        public SchemeProfile Clone()
        {
            SchemeProfile clone = new()
            {
                Id = Id,
                SchemeName = SchemeName,
                LastModifiedAt = LastModifiedAt,
                Steps = new ObservableCollection<SchemeWorkStepItem>(Steps.Select(step => step.Clone()))
            };

            if (!IsModified)
            {
                clone.AcceptChanges();
            }

            return clone;
        }

        #endregion

        #region 修改时间戳

        /// <summary>
        /// 标记方案存在尚未保存的修改。最后修改时间只在配置成功保存时更新。
        /// </summary>
        public void MarkModified()
        {
            if (_isModified)
            {
                return;
            }

            _isModified = true;
            OnPropertyChanged(nameof(IsModified));
        }

        /// <summary>
        /// 使用本次成功保存的时间提交修改，并清除未保存标记。
        /// </summary>
        public void AcceptChanges(DateTime savedAt)
        {
            LastModifiedAt = savedAt;
            AcceptChanges();
        }

        /// <summary>
        /// 保留当前最后修改时间，仅清除加载、克隆或保存后的临时修改标记。
        /// </summary>
        public void AcceptChanges()
        {
            if (!_isModified)
            {
                return;
            }

            _isModified = false;
            OnPropertyChanged(nameof(IsModified));
        }

        #endregion
    }

    /// <summary>
    /// 方案中的业务项
    /// </summary>
    public sealed class SchemeWorkStepItem : ViewModelProperties
    {
        #region 私有字段

        private string _id = Guid.NewGuid().ToString("N");
        private int _num = 1;
        private string _stepName = string.Empty;
        private string _stepType = "初始化";
        private bool _isChecked;
        private bool _isStartupEnabled = true;
        private bool _isReTestEnabled;
        private int _reTestCount = 1;
        private bool _isConfirmReTest;
        private ObservableCollection<SchemeWorkStepParameterItem> _inputParameters = new();
        private ObservableCollection<SchemeWorkStepParameterItem> _returnParameters = new();

        #endregion

        #region 属性

        /// <summary>
        /// 业务唯一标识
        /// </summary>
        public string Id
        {
            get => _id;
            set => SetField(ref _id, string.IsNullOrWhiteSpace(value) ? Guid.NewGuid().ToString("N") : value.Trim());
        }

        /// <summary>
        /// 序号
        /// </summary>
        public int Num
        {
            get => _num;
            set => SetField(ref _num, Math.Max(1, value));
        }

        /// <summary>
        /// 业务名称
        /// </summary>
        public string StepName
        {
            get => _stepName;
            set => SetField(ref _stepName, value ?? string.Empty, true);
        }

        /// <summary>
        /// 业务类型。
        /// </summary>
        public string StepType
        {
            get => _stepType;
            set => SetField(ref _stepType, value ?? string.Empty, true);
        }

        /// <summary>
        /// 界面批量操作勾选状态，不参与配置序列化。
        /// </summary>
        [JsonIgnore]
        public bool IsChecked
        {
            get => _isChecked;
            set => SetField(ref _isChecked, value);
        }

        /// <summary>
        /// 是否启用
        /// </summary>
        public bool IsStartupEnabled
        {
            get => _isStartupEnabled;
            set => SetField(ref _isStartupEnabled, value);
        }

        /// <summary>
        /// 是否NG重测
        /// </summary>
        public bool IsReTestEnabled
        {
            get => _isReTestEnabled;
            set => SetField(ref _isReTestEnabled, value);
        }

        /// <summary>
        /// 重测次数
        /// </summary>
        public int ReTestCount
        {
            get => _reTestCount;
            set => SetField(ref _reTestCount, Math.Max(1, value));
        }

        /// <summary>
        /// 弹框确认是否重测
        /// </summary>
        public bool IsConfirmReTest
        {
            get => _isConfirmReTest;
            set => SetField(ref _isConfirmReTest, value);
        }

        /// <summary>方案业务实例的可编辑输入参数。</summary>
        public ObservableCollection<SchemeWorkStepParameterItem> InputParameters
        {
            get => _inputParameters;
            set => SetField(ref _inputParameters, value ?? new ObservableCollection<SchemeWorkStepParameterItem>());
        }

        /// <summary>方案业务实例的可编辑显示返回参数。</summary>
        public ObservableCollection<SchemeWorkStepParameterItem> ReturnParameters
        {
            get => _returnParameters;
            set => SetField(ref _returnParameters, value ?? new ObservableCollection<SchemeWorkStepParameterItem>());
        }

        #endregion

        #region 克隆

        public SchemeWorkStepItem Clone()
        {
            return new SchemeWorkStepItem
            {
                Id = Id,
                Num = Num,
                StepName = StepName,
                StepType = StepType,
                IsStartupEnabled = IsStartupEnabled,
                IsReTestEnabled = IsReTestEnabled,
                ReTestCount = ReTestCount,
                IsConfirmReTest = IsConfirmReTest,
                InputParameters = new ObservableCollection<SchemeWorkStepParameterItem>(InputParameters.Select(item => item.Clone())),
                ReturnParameters = new ObservableCollection<SchemeWorkStepParameterItem>(ReturnParameters.Select(item => item.Clone()))
            };
        }

        #endregion
    }

}
