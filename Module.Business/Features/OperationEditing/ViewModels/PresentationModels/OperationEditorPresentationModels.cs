using ControlLibrary;
using Newtonsoft.Json;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace Module.Business.Features.OperationEditing.ViewModels.PresentationModels
{
    /// <summary>
    /// 业务方案业务操作
    /// </summary>
    public sealed class WorkStepOperation : ViewModelProperties
    {
        #region 私有字段

        private string _id = Guid.NewGuid().ToString("N");
        private int _num = 1;
        private string _operationObjectName = "System";
        private string _pCommandName = string.Empty;
        private string _returnValue = string.Empty;
        private string _luaScript = string.Empty;
        private string _summary = string.Empty;
        private int _delayMilliseconds;
        private bool _isEditParameter;
        private bool _isChecked;
        private ObservableCollection<InputParameter> _parameters = new();
        private ObservableCollection<ReturnValue> _returnValues = new();
        private ConditionExecution _conditionExecution = new();

        #endregion

        #region 构造函数

        public WorkStepOperation()
        {
            AttachParameters(_parameters);
            AttachReturnValues(_returnValues);
            _conditionExecution.PropertyChanged += ConditionExecution_PropertyChanged;
        }

        #endregion

        #region 属性

        /// <summary>
        /// 步骤唯一标识
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
        /// 操作对象名称（如"System"或具体设备名称）
        /// </summary>
        public string OperationObjectName
        {
            get => _operationObjectName;
            set => SetField(ref _operationObjectName, value ?? string.Empty, true);
        }

        /// <summary>
        /// 协议名称/命令/调用方法
        /// </summary>
        public string PCommandName
        {
            get => _pCommandName;
            set => SetField(ref _pCommandName, value ?? string.Empty, true);
        }

        /// <summary>
        /// 返回值集合名称，与返回值键共同组成后续步骤引用的完整名称。
        /// </summary>
        public string ReturnValue
        {
            get => _returnValue;
            set => SetField(ref _returnValue, value ?? string.Empty, true);
        }

        /// <summary>
        /// Lua 脚本内容
        /// </summary>
        public string LuaScript
        {
            get => _luaScript;
            set => SetField(ref _luaScript, value ?? string.Empty);
        }

        /// <summary>
        /// 步骤摘要，用于配置界面展示、搜索以及执行过程记录。
        /// </summary>
        public string Summary
        {
            get => _summary;
            set => SetField(ref _summary, value ?? string.Empty, true);
        }

        /// <summary>
        /// 延时时间
        /// </summary>
        public int DelayMilliseconds
        {
            get => _delayMilliseconds;
            set => SetField(ref _delayMilliseconds, Math.Max(0, value));
        }

        /// <summary>
        /// 是否修改了参数
        /// </summary>
        public bool IsEditParameter
        {
            get => _isEditParameter;
            set => SetField(ref _isEditParameter, value);
        }

        /// <summary>
        /// 是否选中
        /// </summary>
        [JsonIgnore]
        public bool IsChecked
        {
            get => _isChecked;
            set => SetField(ref _isChecked, value);
        }

        /// <summary>
        /// 输入参数集合
        /// </summary>
        public ObservableCollection<InputParameter> Parameters
        {
            get => _parameters;
            set
            {
                if (ReferenceEquals(_parameters, value))
                {
                    return;
                }

                DetachParameters(_parameters);
                _parameters = value ?? new ObservableCollection<InputParameter>();
                AttachParameters(_parameters);
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// 返回值集合
        /// </summary>
        public ObservableCollection<ReturnValue> ReturnValues
        {
            get => _returnValues;
            set
            {
                if (ReferenceEquals(_returnValues, value))
                {
                    return;
                }

                DetachReturnValues(_returnValues);
                _returnValues = value ?? new ObservableCollection<ReturnValue>();
                AttachReturnValues(_returnValues);
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// 条件执行配置。该实体跟随步骤一起克隆和保存，未启用时执行器可直接忽略其余字段。
        /// </summary>
        public ConditionExecution ConditionExecution
        {
            get => _conditionExecution;
            set
            {
                ConditionExecution normalized = value ?? new ConditionExecution();
                if (ReferenceEquals(_conditionExecution, normalized))
                {
                    return;
                }

                _conditionExecution.PropertyChanged -= ConditionExecution_PropertyChanged;
                _conditionExecution = normalized;
                _conditionExecution.PropertyChanged += ConditionExecution_PropertyChanged;
                OnPropertyChanged();
            }
        }
        #endregion

        #region 集合通知

        private void AttachParameters(ObservableCollection<InputParameter> parameters)
        {
            parameters.CollectionChanged += Parameters_CollectionChanged;
            foreach (InputParameter parameter in parameters)
            {
                parameter.PropertyChanged += Parameter_PropertyChanged;
            }
        }

        private void DetachParameters(ObservableCollection<InputParameter> parameters)
        {
            parameters.CollectionChanged -= Parameters_CollectionChanged;
            foreach (InputParameter parameter in parameters)
            {
                parameter.PropertyChanged -= Parameter_PropertyChanged;
            }
        }

        private void Parameters_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems is not null)
            {
                foreach (InputParameter parameter in e.NewItems.OfType<InputParameter>())
                {
                    parameter.PropertyChanged += Parameter_PropertyChanged;
                }
            }

            if (e.OldItems is not null)
            {
                foreach (InputParameter parameter in e.OldItems.OfType<InputParameter>())
                {
                    parameter.PropertyChanged -= Parameter_PropertyChanged;
                }
            }
        }

        private void Parameter_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(InputParameter.ParameterName)
                or nameof(InputParameter.ParameterType)
                or nameof(InputParameter.Value)
                or nameof(InputParameter.Description))
            {
            }
        }

        private void AttachReturnValues(ObservableCollection<ReturnValue> returnValues)
        {
            returnValues.CollectionChanged += ReturnValues_CollectionChanged;
            foreach (ReturnValue returnValue in returnValues)
            {
                returnValue.PropertyChanged += ReturnValue_PropertyChanged;
            }
        }

        private void DetachReturnValues(ObservableCollection<ReturnValue> returnValues)
        {
            returnValues.CollectionChanged -= ReturnValues_CollectionChanged;
            foreach (ReturnValue returnValue in returnValues)
            {
                returnValue.PropertyChanged -= ReturnValue_PropertyChanged;
            }
        }

        private void ReturnValues_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems is not null)
            {
                foreach (ReturnValue returnValue in e.NewItems.OfType<ReturnValue>())
                {
                    returnValue.PropertyChanged += ReturnValue_PropertyChanged;
                }
            }

            if (e.OldItems is not null)
            {
                foreach (ReturnValue returnValue in e.OldItems.OfType<ReturnValue>())
                {
                    returnValue.PropertyChanged -= ReturnValue_PropertyChanged;
                }
            }
        }

        private void ReturnValue_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(global::Module.Business.Features.OperationEditing.ViewModels.PresentationModels.ReturnValue.ReturnParameterName)
                or nameof(global::Module.Business.Features.OperationEditing.ViewModels.PresentationModels.ReturnValue.IsShowView)
                or nameof(global::Module.Business.Features.OperationEditing.ViewModels.PresentationModels.ReturnValue.ViewDataName)
                or nameof(global::Module.Business.Features.OperationEditing.ViewModels.PresentationModels.ReturnValue.Unit))
            {
            }
        }

        /// <summary>
        /// 将条件子实体的字段变化提升为步骤实体变化，保证业务页面能够识别未保存修改。
        /// </summary>
        private void ConditionExecution_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            OnPropertyChanged(nameof(ConditionExecution));
        }

        #endregion

        #region 克隆

        public WorkStepOperation Clone()
        {
            return new WorkStepOperation
            {
                Id = Id,
                Num = Num,
                OperationObjectName = OperationObjectName,
                PCommandName = PCommandName,
                ReturnValue = ReturnValue,
                LuaScript = LuaScript,
                Summary = Summary,
                DelayMilliseconds = DelayMilliseconds,
                IsEditParameter = IsEditParameter,
                IsChecked = false,
                Parameters = new ObservableCollection<InputParameter>(Parameters.Select(parameter => parameter.Clone())),
                ReturnValues = new ObservableCollection<ReturnValue>(ReturnValues.Select(returnValue => returnValue.Clone())),
                ConditionExecution = ConditionExecution.Clone()
            };
        }

        #endregion

    }

    /// <summary>
    /// 步骤条件执行视图实体，保存条件两侧参数、关系符及判断失败后的跳转配置。
    /// </summary>
    public sealed class ConditionExecution : ViewModelProperties
    {
        #region 私有字段

        private bool _isEnabled;
        private string _leftParameterType = "设置值";
        private string _leftValue = string.Empty;
        private string _relationOperator = "NA";
        private string _rightParameterType = "设置值";
        private string _rightValue = string.Empty;
        private bool _isNgJumpEnabled;
        private string _ngJumpStepId = string.Empty;

        #endregion

        #region 属性

        public bool IsEnabled { get => _isEnabled; set => SetField(ref _isEnabled, value); }

        public string LeftParameterType { get => _leftParameterType; set => SetField(ref _leftParameterType, value ?? string.Empty, true); }

        public string LeftValue { get => _leftValue; set => SetField(ref _leftValue, value ?? string.Empty); }

        public string RelationOperator { get => _relationOperator; set => SetField(ref _relationOperator, value ?? string.Empty, true); }

        public string RightParameterType { get => _rightParameterType; set => SetField(ref _rightParameterType, value ?? string.Empty, true); }

        public string RightValue { get => _rightValue; set => SetField(ref _rightValue, value ?? string.Empty); }

        public bool IsNgJumpEnabled { get => _isNgJumpEnabled; set => SetField(ref _isNgJumpEnabled, value); }

        public string NgJumpStepId { get => _ngJumpStepId; set => SetField(ref _ngJumpStepId, value ?? string.Empty, true); }

        #endregion

        #region 克隆

        public ConditionExecution Clone()
        {
            return new ConditionExecution
            {
                IsEnabled = IsEnabled,
                LeftParameterType = LeftParameterType,
                LeftValue = LeftValue,
                RelationOperator = RelationOperator,
                RightParameterType = RightParameterType,
                RightValue = RightValue,
                IsNgJumpEnabled = IsNgJumpEnabled,
                NgJumpStepId = NgJumpStepId
            };
        }

        #endregion
    }

    /// <summary>
    /// 输入参数实体
    /// </summary>
    public sealed class InputParameter : ViewModelProperties
    {
        #region 私有字段

        private string _id = Guid.NewGuid().ToString("N");
        private int _num = 1;
        private string _parameterName = string.Empty;
        private string _parameterType = "设置值";
        private string _value = string.Empty;
        private string _description = string.Empty;

        #endregion

        #region 属性

        /// <summary>
        /// 步骤唯一标识
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
            set
            {
                SetField(ref _num, Math.Max(1, value));
            }
        }
        /// <summary>
        /// 参数名称
        /// </summary>
        public string ParameterName
        {
            get => _parameterName;
            set => SetField(ref _parameterName, value ?? string.Empty, true);
        }

        /// <summary>
        /// 参数类型（设置值/返回值/全局值）
        /// </summary>
        public string ParameterType
        {
            get => _parameterType;
            set => SetField(ref _parameterType, value ?? string.Empty, true);
        }

        /// <summary>
        /// 设置值或者返回值、全局值名称
        /// </summary>
        public string Value
        {
            get => _value;
            set => SetField(ref _value, value ?? string.Empty, true);
        }

        public string Description
        {
            get => _description;
            set => SetField(ref _description, value ?? string.Empty, true);
        }

        #endregion

        #region 克隆

        public InputParameter Clone()
        {
            return new InputParameter
            {
                Id = Id,
                Num = Num,
                ParameterName = ParameterName,
                ParameterType = ParameterType,
                Value = Value,
                Description = Description
            };
        }

        #endregion
    }

    /// <summary>
    /// 输入参数编辑行，仅包装持久化业务参数供 DataGrid 绑定。
    /// </summary>
    public sealed class InputParameterEditorItem
    {
        #region 构造与属性

        public InputParameterEditorItem(InputParameter parameter)
        {
            Parameter = parameter ?? throw new ArgumentNullException(nameof(parameter));
        }

        /// <summary>
        /// 当前编辑行对应的业务输入参数。
        /// </summary>
        public InputParameter Parameter { get; }

        /// <summary>
        #endregion
    }

    /// <summary>
    /// 返回值实体
    /// </summary>
    public sealed class ReturnValue : ViewModelProperties
    {
        #region 私有字段

        private string _id = Guid.NewGuid().ToString("N");
        private int _num = 1;
        private string _returnParameterName = string.Empty;
        private bool _isShowView;
        private string _viewDataName = string.Empty;
        private string _unit = string.Empty;

        #endregion

        #region 属性

        /// <summary>
        /// 步骤唯一标识
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
        /// 返回值参数名称
        /// </summary>
        public string ReturnParameterName
        {
            get => _returnParameterName;
            set
            {
                if (SetField(ref _returnParameterName, value ?? string.Empty, true))
                {
                    OnPropertyChanged(nameof(ViewDataName));
                }
            }
        }

        /// <summary>
        /// 是否显示到界面
        /// </summary>
        public bool IsShowView
        {
            get => _isShowView;
            set => SetField(ref _isShowView, value);
        }

        /// <summary>
        /// 显示到界面的数据名称；未单独设置时使用返回值参数名称。
        /// </summary>
        public string ViewDataName
        {
            get => string.IsNullOrWhiteSpace(_viewDataName) ? _returnParameterName : _viewDataName;
            set => SetField(ref _viewDataName, value ?? string.Empty, true);
        }

        /// <summary>
        /// 返回值单位。
        /// </summary>
        public string Unit
        {
            get => _unit;
            set => SetField(ref _unit, value ?? string.Empty, true);
        }

        #endregion

        #region 克隆

        public ReturnValue Clone()
        {
            return new ReturnValue
            {
                Id = Id,
                Num = Num,
                ReturnParameterName = ReturnParameterName,
                IsShowView = IsShowView,
                ViewDataName = ViewDataName,
                Unit = Unit
            };
        }

        #endregion
    }
}
