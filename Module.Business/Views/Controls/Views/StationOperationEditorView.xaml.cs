using Module.Business.Models;
using Module.Business.ViewModels;
using Module.Business.ViewModels.PropertyVMs;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Module.Business.Views.Controls.Views;

/// <summary>
/// 工站步骤编辑器。
/// </summary>
public partial class StationOperationEditorView : UserControl
{
    private bool _isRefreshingReturnParameters;
    private INotifyPropertyChanged? _subscribedViewModel;

    public StationOperationEditorView()
    {
        InitializeComponent();
        Loaded += StationOperationEditorView_Loaded;
        DataContextChanged += StationOperationEditorView_DataContextChanged;
    }

    private SchemeConfigurationViewModel? ViewModel => DataContext as SchemeConfigurationViewModel;

    private void StationOperationEditorView_Loaded(object sender, RoutedEventArgs e)
    {
        AttachViewModelSubscription();
        EnableParameterEditing();
        RefreshReturnParametersFromEditorState();
    }

    private void StationOperationEditorView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        DetachViewModelSubscription(e.OldValue as INotifyPropertyChanged);
        AttachViewModelSubscription();
        EnableParameterEditing();
        RefreshReturnParametersFromEditorState();
    }

    private void OperationMethodDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshingReturnParameters || OperationMethodDataGrid.SelectedItem is not StationOperationMethodItem methodItem)
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

    private void InvokeParameterDataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        EnableParameterEditing();
    }

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

    private void ApplyMethod(WorkStepOperation operation, ObservableCollection<WorkStepOperationParameter>? parameters = null)
    {
        if (ViewModel is null)
        {
            return;
        }

        ViewModel.EditingOperationObject = operation.OperationObject;
        ViewModel.EditingProtocolName = operation.ProtocolName;
        ViewModel.EditingCommandName = string.IsNullOrWhiteSpace(operation.CommandName)
            ? operation.InvokeMethod
            : operation.CommandName;
        ViewModel.EditingInvokeMethod = operation.InvokeMethod;
        ViewModel.EditingReturnValue = operation.ReturnValue;
        ViewModel.EditingModifyInvokeParameters = true;
        ViewModel.EditingInvokeParameters.Clear();

        IEnumerable<WorkStepOperationParameter> sourceParameters = parameters is null
            ? operation.Parameters.OrderBy(parameter => parameter.Sequence)
            : parameters.OrderBy(parameter => parameter.Sequence);

        foreach (WorkStepOperationParameter parameter in sourceParameters)
        {
            ViewModel.EditingInvokeParameters.Add(parameter.Clone());
        }

        ViewModel.SelectedEditingInvokeParameter = ViewModel.EditingInvokeParameters.FirstOrDefault();
        ClearDisplayOptions();
        RefreshReturnParameters(operation);
    }

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
        if (e.PropertyName is nameof(SchemeConfigurationViewModel.EditingOperationObject)
            or nameof(SchemeConfigurationViewModel.EditingProtocolName)
            or nameof(SchemeConfigurationViewModel.EditingCommandName)
            or nameof(SchemeConfigurationViewModel.EditingInvokeMethod)
            or nameof(SchemeConfigurationViewModel.EditingReturnValue))
        {
            RefreshReturnParametersFromEditorState();
        }
    }

    private void EnableParameterEditing()
    {
        if (ViewModel is null || ViewModel.IsLuaOperationSelected)
        {
            return;
        }

        ViewModel.EditingModifyInvokeParameters = true;
        ClearDisplayOptions();
    }

    private void ClearDisplayOptions()
    {
        if (ViewModel is null)
        {
            return;
        }

        ViewModel.EditingShowDataToView = false;
        ViewModel.EditingViewDataName = string.Empty;
        ViewModel.EditingViewJudgeType = string.Empty;
        ViewModel.EditingViewJudgeCondition = string.Empty;
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
                ViewModel.EditingReturnValue = string.Empty;
                ViewModel.ClearEditingReturnParameters();
            }
            finally
            {
                _isRefreshingReturnParameters = false;
            }

            return;
        }

        RefreshReturnParameters(CreateEditingOperationSnapshot());
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
            ObservableCollection<WorkStepOperationParameter> parameters =
                ViewModel.CreateReturnParametersFromOperation(operation);
            ViewModel.ReplaceEditingReturnParameters(parameters);
            ViewModel.SelectedEditingReturnParameter = null;
        }
        finally
        {
            _isRefreshingReturnParameters = false;
        }
    }

    private WorkStepOperation CreateEditingOperationSnapshot()
    {
        return new WorkStepOperation
        {
            OperationObject = ViewModel?.EditingOperationObject ?? string.Empty,
            ProtocolName = ViewModel?.EditingProtocolName ?? string.Empty,
            CommandName = ViewModel?.EditingCommandName ?? string.Empty,
            InvokeMethod = ViewModel?.EditingInvokeMethod ?? string.Empty,
            ReturnValue = ViewModel?.EditingReturnValue ?? string.Empty
        };
    }

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
}
