using Module.Business.Features.OperationEditing.ViewModels;
using Module.Business.Features.OperationEditing.ViewModels.PresentationModels;
using Module.Business.Features.OperationEditing.Models;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Module.Business.Features.OperationEditing.Views;

public partial class OperationEditorView : UserControl
{
    #region 私有字段与当前 ViewModel

    private bool _isRefreshingReturnParameters;
    private bool _isRefreshingLuaScriptTemplates;
    private bool _isSynchronizingMethodSelection;
    private INotifyPropertyChanged? _subscribedViewModel;

    private OperationEditorViewModel? ViewModel => DataContext as OperationEditorViewModel;

    #endregion

    #region 构造与生命周期

    public OperationEditorView()
    {
        InitializeComponent();
        Loaded += OperationEditorView_Loaded;
        Unloaded += OperationEditorView_Unloaded;
        DataContextChanged += OperationEditorView_DataContextChanged;
    }

    /// <summary>
    /// 控件加载后订阅当前 ViewModel，并同步脚本模板、参数编辑状态和返回参数。
    /// </summary>
    private void OperationEditorView_Loaded(object sender, RoutedEventArgs e)
    {
        AttachViewModelSubscription();
        RefreshEditorState();
    }

    /// <summary>
    /// 控件离开视觉树时解除属性通知订阅，避免视图被旧 ViewModel 长期引用。
    /// </summary>
    private void OperationEditorView_Unloaded(object sender, RoutedEventArgs e)
    {
        DetachViewModelSubscription(_subscribedViewModel);
    }

    /// <summary>
    /// 数据上下文切换时解除旧订阅；控件已加载时再订阅并刷新新编辑状态。
    /// </summary>
    private void OperationEditorView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        DetachViewModelSubscription(e.OldValue as INotifyPropertyChanged);
        if (!IsLoaded)
        {
            return;
        }

        AttachViewModelSubscription();
        RefreshEditorState();
    }

    /// <summary>
    /// 将所有依赖当前 ViewModel 的界面状态同步到最新值。
    /// </summary>
    private void RefreshEditorState()
    {
        RefreshLuaScriptTemplateOptions();
        SynchronizeSupportedMethodSelection();
        EnableParameterEditing();
        RefreshReturnParametersFromEditorState();
    }

    #endregion

    #region 方法与模板选择事件

    private void OperationMethodDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshingReturnParameters ||
            _isSynchronizingMethodSelection ||
            ViewModel?.IsInitializingOperationDrawer == true ||
            OperationMethodDataGrid.SelectedItem is not StationOperationMethodItem methodItem)
        {
            return;
        }

        ApplyMethod(methodItem);
    }

    private void OperationMethodDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        StationOperationMethodItem? methodItem =
            FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject)?.Item as StationOperationMethodItem;
        if (methodItem is null)
        {
            return;
        }

        ApplyMethod(methodItem);
        e.Handled = true;
    }

    private void LuaScriptTemplateComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshingLuaScriptTemplates ||
            LuaScriptTemplateComboBox.SelectedItem is not string templateName)
        {
            return;
        }

        ViewModel?.ApplyLuaScriptTemplate(templateName);
    }

    private void RefreshLuaScriptTemplateOptions()
    {
        _isRefreshingLuaScriptTemplates = true;
        try
        {
            ViewModel?.RefreshLuaScriptTemplateOptions();
            LuaScriptTemplateComboBox.SelectedItem = null;
        }
        finally
        {
            _isRefreshingLuaScriptTemplates = false;
        }
    }

    private void InvokeParameterDataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        EnableParameterEditing();

        // CellEditEnding 发生时编辑控件尚未将最新参数类型和参数值回写到实体，延后到本次提交完成后刷新共享业务值集合。
        Dispatcher.BeginInvoke(() => ViewModel?.RefreshWorkStepValueOptionsFromEditingOperation());
    }

    /// <summary>
    /// Lua 编辑器执行脚本前提交参数表，并生成 name=value 格式的执行前缀。
    /// </summary>
    private void LuaEditView_BeforeExecute(object? sender, EventArgs e)
    {
        if (ViewModel?.IsLuaOperationSelected != true)
        {
            return;
        }
        luaEditView.ExecutionPrefixScript = ViewModel.BuildLuaExecutionPrefixScript();
    }

    /// <summary>
    /// 在任一参数值下拉框展开前刷新共享业务值集合，确保当前弹框中刚输入且尚未保存的新业务值立即可选。
    /// </summary>
    private void ParameterValueComboBox_DropDownOpened(object sender, EventArgs e)
    {
        ViewModel?.RefreshWorkStepValueOptionsFromEditingOperation();
    }

    #endregion

    #region 方法应用

    /// <summary>
    /// 将方法表选中项转换成步骤操作，并应用到当前编辑状态。
    /// </summary>
    private void ApplyMethod(StationOperationMethodItem methodItem)
    {
        if (ViewModel is null)
        {
            return;
        }

        WorkStepOperation? operation = ViewModel.CreateOperationFromMethodItem(methodItem);
        if (operation is null)
        {
            return;
        }

        ApplyMethod(operation);
    }

    /// <summary>
    /// 将步骤操作中的对象、方法和参数完整同步到编辑器。
    /// </summary>
    private void ApplyMethod(WorkStepOperation operation)
    {
        if (ViewModel is null)
        {
            return;
        }

        ViewModel.EditingOperation.OperationObjectName = operation.OperationObjectName;
        if (ViewModel.IsSystemOperationSelected || ViewModel.IsLuaOperationSelected)
        {
            ViewModel.EditingOperation.PCommandName = string.Empty;
        }
        ViewModel.EditingOperation.PCommandName = operation.PCommandName;
        ViewModel.EditingOperation.Summary = operation.Summary;
        ViewModel.EditingOperation.ReturnValue = operation.ReturnValue;
        ViewModel.EditingOperation.IsEditParameter = true;
        ViewModel.EditingOperation.Parameters.Clear();

        foreach (InputParameter parameter in operation.Parameters.OrderBy(parameter => parameter.Num))
        {
            ViewModel.EditingOperation.Parameters.Add(parameter.Clone());
        }

        ViewModel.SelectedEditingInvokeParameter = ViewModel.EditingOperation.Parameters.FirstOrDefault();
        RefreshReturnParameters(operation);
    }

    #endregion

    #region ViewModel 属性通知

    private void AttachViewModelSubscription()
    {
        if (ViewModel is not INotifyPropertyChanged propertyChanged || ReferenceEquals(_subscribedViewModel, propertyChanged))
        {
            return;
        }

        DetachViewModelSubscription(_subscribedViewModel);
        _subscribedViewModel = propertyChanged;
        _subscribedViewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    private void DetachViewModelSubscription(INotifyPropertyChanged? propertyChanged)
    {
        if (propertyChanged is null)
        {
            return;
        }

        propertyChanged.PropertyChanged -= ViewModel_PropertyChanged;
        if (ReferenceEquals(_subscribedViewModel, propertyChanged))
        {
            _subscribedViewModel = null;
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OperationEditorViewModel.EditingOperation)
            or nameof(OperationEditorViewModel.StationOperationMethodCollection)
            or nameof(WorkStepOperation.OperationObjectName)
            or nameof(WorkStepOperation.PCommandName))
        {
            SynchronizeSupportedMethodSelection();
        }

        if (e.PropertyName is nameof(WorkStepOperation.OperationObjectName)
            or nameof(WorkStepOperation.PCommandName)
            or nameof(WorkStepOperation.ReturnValue))
        {
            RefreshReturnParametersFromEditorState();
        }
    }

    #endregion

    #region 编辑状态同步

    /// <summary>
    /// 根据当前步骤保存的操作对象和方法名称恢复“支持的方法/指令”选中行。
    /// 此处仅同步 DataGrid 的视觉状态，不重新应用方法，避免编辑步骤时覆盖已经保存的输入参数和返回参数。
    /// </summary>
    private void SynchronizeSupportedMethodSelection()
    {
        if (ViewModel is null)
        {
            return;
        }

        StationOperationMethodItem? selectedMethod = ViewModel.StationOperationMethodCollection.FirstOrDefault(item =>
            string.Equals(item.OperationObject, ViewModel.EditingOperation.OperationObjectName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.InvokeMethod, ViewModel.EditingOperation.PCommandName, StringComparison.OrdinalIgnoreCase));

        _isSynchronizingMethodSelection = true;
        try
        {
            OperationMethodDataGrid.SelectedItem = selectedMethod;
            if (selectedMethod is not null)
            {
                OperationMethodDataGrid.ScrollIntoView(selectedMethod);
            }
        }
        finally
        {
            _isSynchronizingMethodSelection = false;
        }
    }

    private void EnableParameterEditing()
    {
        if (ViewModel is null || ViewModel.IsLuaOperationSelected)
        {
            return;
        }

        ViewModel.EditingOperation.IsEditParameter = true;
    }

    private void RefreshReturnParametersFromEditorState()
    {
        if (ViewModel is null)
        {
            return;
        }

        if (ViewModel.IsLuaOperationSelected)
        {
            _isRefreshingReturnParameters = true;
            try
            {
                ViewModel.ClearStepEditorReturnParameterRows();
            }
            finally
            {
                _isRefreshingReturnParameters = false;
            }

            return;
        }

        WorkStepOperation editingOperation = new()
        {
            OperationObjectName = ViewModel.EditingOperation.OperationObjectName,
            PCommandName = ViewModel.EditingOperation.PCommandName,
            ReturnValues = new ObservableCollection<ReturnValue>(
                ViewModel.EditingOperation.ReturnValues
                    .OrderBy(returnValue => returnValue.Num)
                    .Select(returnValue => returnValue.Clone()))
        };
        RefreshReturnParameters(editingOperation);
    }

    private void RefreshReturnParameters(WorkStepOperation? operation)
    {
        if (ViewModel is null)
        {
            return;
        }

        _isRefreshingReturnParameters = true;
        try
        {
            ViewModel.ReplaceStepEditorReturnParameterRows(operation);
        }
        finally
        {
            _isRefreshingReturnParameters = false;
        }
    }

    #endregion

    #region 视觉树辅助

    /// <summary>
    /// 从事件源向上查找指定类型的视觉树父级。
    /// </summary>
    private static T? FindAncestor<T>(DependencyObject? source)
        where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T target)
            {
                return target;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    #endregion
}
