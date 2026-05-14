using Module.Business.Models;
using Module.Business.ViewModels;
using System;
using System.Collections.ObjectModel;
using System.Data;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Module.Business.Views.Controls.Views
{
    /// <summary>
    /// 可复用的步骤编辑内容区。
    /// </summary>
    public partial class OperationEditorView : UserControl
    {
        private const string OperationDragDataFormat = "Module.Business.WorkStepOperation";
        private const double MethodParameterDrawerClosedOffset = 56d;

        private static readonly Duration MethodParameterDrawerAnimationDuration =
            new(TimeSpan.FromMilliseconds(220));

        private static readonly IEasingFunction MethodParameterDrawerEasing =
            new CubicEase { EasingMode = EasingMode.EaseOut };

        private Point _methodDragStartPoint;
        private DataRowView? _pendingDraggedMethodRow;
        private bool _isMethodParameterDrawerOpen;

        public OperationEditorView()
        {
            InitializeComponent();
            UpdateMethodParameterDrawerVisual(animate: false);
        }

        private WorkStepConfigurationViewModel? ViewModel => DataContext as WorkStepConfigurationViewModel;

        private void OperationMethodDataGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _methodDragStartPoint = e.GetPosition(OperationMethodDataGrid);
            _pendingDraggedMethodRow = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject)?.Item as DataRowView;
        }

        private void OperationMethodDataGrid_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _pendingDraggedMethodRow is null)
            {
                return;
            }

            Point currentPoint = e.GetPosition(OperationMethodDataGrid);
            if (Math.Abs(currentPoint.X - _methodDragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(currentPoint.Y - _methodDragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            WorkStepOperation? operation = ViewModel?.CreateOperationFromMethodTableRow(_pendingDraggedMethodRow);
            _pendingDraggedMethodRow = null;
            if (operation is null)
            {
                return;
            }

            DataObject dataObject = new();
            dataObject.SetData(OperationDragDataFormat, operation);
            dataObject.SetData(DataFormats.StringFormat, operation.DisplayText);
            DragDrop.DoDragDrop(OperationMethodDataGrid, dataObject, DragDropEffects.Copy);
        }

        private void OperationMethodDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            DataRowView? rowView = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject)?.Item as DataRowView;
            if (rowView is null)
            {
                return;
            }

            WorkStepOperation? operation = ViewModel?.CreateOperationFromMethodTableRow(rowView);
            if (operation is null)
            {
                return;
            }

            MethodParameterDrawerSheet.Tag = new MethodParameterPreviewState(
                operation,
                GetDataRowValue(rowView, "Summary"));
            OpenMethodParameterDrawer();
            e.Handled = true;
        }

        private void MethodParameterDrawerBackdrop_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            CloseMethodParameterDrawer();
        }

        private void CloseMethodParameterDrawerButton_Click(object sender, RoutedEventArgs e)
        {
            CloseMethodParameterDrawer();
        }

        private void ApplyMethodParameterDrawerButton_Click(object sender, RoutedEventArgs e)
        {
            MethodParameterPreviewDataGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            MethodParameterPreviewDataGrid.CommitEdit(DataGridEditingUnit.Row, true);

            if (ViewModel is null || MethodParameterDrawerSheet.Tag is not MethodParameterPreviewState state)
            {
                return;
            }

            WorkStepOperation operation = state.SourceOperation.Clone();
            operation.Parameters = new ObservableCollection<WorkStepOperationParameter>(
                state.Parameters
                    .OrderBy(parameter => parameter.Sequence)
                    .Select(parameter => parameter.Clone()));

            ViewModel.EditingOperationObject = operation.OperationObject;
            ViewModel.EditingProtocolName = operation.ProtocolName;
            ViewModel.EditingCommandName = string.IsNullOrWhiteSpace(operation.CommandName)
                ? operation.InvokeMethod
                : operation.CommandName;
            ViewModel.EditingInvokeMethod = operation.InvokeMethod;
            ViewModel.EditingModifyInvokeParameters = true;
            ViewModel.EditingInvokeParameters.Clear();
            foreach (WorkStepOperationParameter parameter in operation.Parameters)
            {
                ViewModel.EditingInvokeParameters.Add(parameter.Clone());
            }

            ViewModel.SelectedEditingInvokeParameter = ViewModel.EditingInvokeParameters.FirstOrDefault();
            CloseMethodParameterDrawer();
        }

        private void OpenMethodParameterDrawer()
        {
            _isMethodParameterDrawerOpen = true;
            UpdateMethodParameterDrawerVisual(animate: true);
        }

        private void CloseMethodParameterDrawer()
        {
            _isMethodParameterDrawerOpen = false;
            UpdateMethodParameterDrawerVisual(animate: true);
        }

        private void UpdateMethodParameterDrawerVisual(bool animate)
        {
            if (MethodParameterDrawerHost is null || MethodParameterDrawerTranslateTransform is null)
            {
                return;
            }

            double targetOpacity = _isMethodParameterDrawerOpen ? 1d : 0d;
            double targetOffset = _isMethodParameterDrawerOpen ? 0d : MethodParameterDrawerClosedOffset;

            if (_isMethodParameterDrawerOpen)
            {
                MethodParameterDrawerHost.IsHitTestVisible = true;
            }

            if (!animate)
            {
                MethodParameterDrawerHost.BeginAnimation(UIElement.OpacityProperty, null);
                MethodParameterDrawerTranslateTransform.BeginAnimation(TranslateTransform.YProperty, null);
                MethodParameterDrawerHost.Opacity = targetOpacity;
                MethodParameterDrawerTranslateTransform.Y = targetOffset;
                MethodParameterDrawerHost.IsHitTestVisible = _isMethodParameterDrawerOpen;
                return;
            }

            DoubleAnimation opacityAnimation = new(targetOpacity, MethodParameterDrawerAnimationDuration)
            {
                EasingFunction = MethodParameterDrawerEasing
            };

            if (!_isMethodParameterDrawerOpen)
            {
                opacityAnimation.Completed += (_, _) =>
                {
                    if (!_isMethodParameterDrawerOpen)
                    {
                        MethodParameterDrawerHost.IsHitTestVisible = false;
                    }
                };
            }

            DoubleAnimation translateAnimation = new(targetOffset, MethodParameterDrawerAnimationDuration)
            {
                EasingFunction = MethodParameterDrawerEasing
            };

            MethodParameterDrawerHost.BeginAnimation(UIElement.OpacityProperty, opacityAnimation);
            MethodParameterDrawerTranslateTransform.BeginAnimation(TranslateTransform.YProperty, translateAnimation);
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

        private static string GetDataRowValue(DataRowView rowView, string columnName)
        {
            return rowView.Row.Table.Columns.Contains(columnName)
                ? rowView.Row[columnName]?.ToString()?.Trim() ?? string.Empty
                : string.Empty;
        }

        public sealed class MethodParameterPreviewState
        {
            public MethodParameterPreviewState(WorkStepOperation operation, string operationSummary)
            {
                SourceOperation = operation.Clone();
                OperationTitle = operation.DisplayText;
                OperationSummary = operationSummary;
                Parameters = new ObservableCollection<WorkStepOperationParameter>(
                    operation.Parameters
                        .OrderBy(parameter => parameter.Sequence)
                        .Select(parameter => parameter.Clone()));
                ParameterSummary = Parameters.Count == 0 ? "无参数" : $"{Parameters.Count} 个参数";
            }

            public string OperationTitle { get; }

            public WorkStepOperation SourceOperation { get; }

            public string OperationSummary { get; }

            public string ParameterSummary { get; }

            public ObservableCollection<WorkStepOperationParameter> Parameters { get; }
        }
    }
}
