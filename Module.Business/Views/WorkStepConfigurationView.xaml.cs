using Module.Business.Models;
using Module.Business.ViewModels;
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Module.Business.Views
{
    /// <summary>
    /// WorkStepConfigurationView.xaml 的交互逻辑。
    /// </summary>
    public partial class WorkStepConfigurationView : UserControl
    {
        private const string OperationDragDataFormat = "Module.Business.WorkStepOperation";
        private const double OperationDrawerClosedOffset = 56d;
        private static readonly Duration OperationDrawerAnimationDuration = new(TimeSpan.FromMilliseconds(220));
        private static readonly IEasingFunction OperationDrawerEasing = new CubicEase { EasingMode = EasingMode.EaseOut };
        private Point _operationDragStartPoint;
        private WorkStepOperation? _pendingDraggedOperation;

        public WorkStepConfigurationView()
        {
            InitializeComponent();
            Loaded += WorkStepConfigurationView_Loaded;
            Unloaded += WorkStepConfigurationView_Unloaded;
            UpdateOperationDrawerVisual(animate: false);
        }

        private WorkStepConfigurationViewModel? ViewModel => DataContext as WorkStepConfigurationViewModel;

        private void WorkStepConfigurationView_Loaded(object sender, RoutedEventArgs e)
        {
            if (ViewModel is not null)
            {
                ViewModel.PropertyChanged += ViewModel_PropertyChanged;
            }

            UpdateOperationDrawerVisual(animate: false);
        }

        private void WorkStepConfigurationView_Unloaded(object sender, RoutedEventArgs e)
        {
            if (ViewModel is not null)
            {
                ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
            }
        }

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(WorkStepConfigurationViewModel.IsOperationDrawerOpen))
            {
                UpdateOperationDrawerVisual(animate: true);
            }
        }

        private void OperationsDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (IsInlineEditableOperationCell(e.OriginalSource as DependencyObject))
            {
                return;
            }

            if (FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject)?.Item is not WorkStepOperation operation)
            {
                return;
            }

            OperationsDataGrid.SelectedItem = operation;
            ViewModel?.OpenOperationDrawerForEdit(operation);
            e.Handled = true;
        }

        private void OperationsDataGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (IsInlineEditableOperationCell(e.OriginalSource as DependencyObject))
            {
                _pendingDraggedOperation = null;
                return;
            }

            _operationDragStartPoint = e.GetPosition(OperationsDataGrid);
            _pendingDraggedOperation = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject)?.Item as WorkStepOperation;
        }

        private void OperationsDataGrid_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _pendingDraggedOperation is null)
            {
                return;
            }

            Point currentPoint = e.GetPosition(OperationsDataGrid);
            if (Math.Abs(currentPoint.X - _operationDragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(currentPoint.Y - _operationDragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            WorkStepOperation draggedOperation = _pendingDraggedOperation;
            _pendingDraggedOperation = null;

            DataObject dataObject = new();
            dataObject.SetData(OperationDragDataFormat, draggedOperation);
            DragDrop.DoDragDrop(OperationsDataGrid, dataObject, DragDropEffects.Move);
        }

        private void OperationsDataGrid_DragOver(object sender, DragEventArgs e)
        {
            if (!TryGetOperationDropInfo(e, out _, out _, out bool insertAfter))
            {
                HideOperationDropIndicator();
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            ShowOperationDropIndicator(FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject), insertAfter);
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }

        private void OperationsDataGrid_DragLeave(object sender, DragEventArgs e)
        {
            HideOperationDropIndicator();
        }

        private void OperationsDataGrid_Drop(object sender, DragEventArgs e)
        {
            if (TryGetOperationDropInfo(e, out WorkStepOperation? draggedOperation, out WorkStepOperation? targetOperation, out bool insertAfter) &&
                draggedOperation is not null &&
                targetOperation is not null)
            {
                ViewModel?.MoveOperation(draggedOperation, targetOperation, insertAfter);
            }

            _pendingDraggedOperation = null;
            HideOperationDropIndicator();
            e.Handled = true;
        }

        private void OperationDrawerBackdrop_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (ViewModel?.CloseOperationDrawerCommand.CanExecute(null) == true)
            {
                ViewModel.CloseOperationDrawerCommand.Execute(null);
            }
        }

        private bool TryGetOperationDropInfo(
            DragEventArgs e,
            out WorkStepOperation? draggedOperation,
            out WorkStepOperation? targetOperation,
            out bool insertAfter)
        {
            draggedOperation = e.Data.GetDataPresent(OperationDragDataFormat)
                ? e.Data.GetData(OperationDragDataFormat) as WorkStepOperation
                : null;
            targetOperation = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject)?.Item as WorkStepOperation;
            insertAfter = false;

            if (draggedOperation is null || targetOperation is null || ReferenceEquals(draggedOperation, targetOperation))
            {
                return false;
            }

            DataGridRow? targetRow = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
            if (targetRow is not null)
            {
                insertAfter = e.GetPosition(targetRow).Y > targetRow.ActualHeight / 2d;
            }

            return true;
        }

        private static bool IsInlineEditableOperationCell(DependencyObject? source)
        {
            DataGridCell? cell = FindAncestor<DataGridCell>(source);
            string? header = cell?.Column?.Header?.ToString();

            return string.Equals(header, "延时(ms)", StringComparison.Ordinal) ||
                   string.Equals(header, "描述", StringComparison.Ordinal);
        }

        private void ShowOperationDropIndicator(DataGridRow? targetRow, bool insertAfter)
        {
            if (targetRow is null || OperationDropIndicatorCanvas is null || OperationDropIndicator is null)
            {
                HideOperationDropIndicator();
                return;
            }

            double horizontalPadding = 8d;
            double indicatorHeight = 3d;
            double width = Math.Max(0d, OperationDropIndicatorCanvas.ActualWidth - horizontalPadding * 2);
            Point rowTopLeft = targetRow.TranslatePoint(new Point(0, 0), OperationDropIndicatorCanvas);
            double top = rowTopLeft.Y + (insertAfter ? targetRow.ActualHeight : 0d) - indicatorHeight / 2d;
            top = Math.Clamp(top, 0d, Math.Max(0d, OperationDropIndicatorCanvas.ActualHeight - indicatorHeight));

            OperationDropIndicator.Width = width;
            OperationDropIndicator.Height = indicatorHeight;
            Canvas.SetLeft(OperationDropIndicator, horizontalPadding);
            Canvas.SetTop(OperationDropIndicator, top);
            OperationDropIndicator.Visibility = Visibility.Visible;
        }

        private void HideOperationDropIndicator()
        {
            if (OperationDropIndicator is not null)
            {
                OperationDropIndicator.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateOperationDrawerVisual(bool animate)
        {
            if (OperationDrawerHost is null || OperationDrawerTranslateTransform is null)
            {
                return;
            }

            bool isOpen = ViewModel?.IsOperationDrawerOpen == true;
            double targetOpacity = isOpen ? 1d : 0d;
            double targetOffset = isOpen ? 0d : OperationDrawerClosedOffset;

            if (isOpen)
            {
                OperationDrawerHost.IsHitTestVisible = true;
            }

            if (!animate)
            {
                OperationDrawerHost.BeginAnimation(UIElement.OpacityProperty, null);
                OperationDrawerTranslateTransform.BeginAnimation(TranslateTransform.XProperty, null);
                OperationDrawerHost.Opacity = targetOpacity;
                OperationDrawerTranslateTransform.X = targetOffset;
                OperationDrawerHost.IsHitTestVisible = isOpen;
                return;
            }

            DoubleAnimation opacityAnimation = new(targetOpacity, OperationDrawerAnimationDuration)
            {
                EasingFunction = OperationDrawerEasing
            };

            if (!isOpen)
            {
                opacityAnimation.Completed += (_, _) =>
                {
                    if (ViewModel?.IsOperationDrawerOpen != true)
                    {
                        OperationDrawerHost.IsHitTestVisible = false;
                    }
                };
            }

            DoubleAnimation translateAnimation = new(targetOffset, OperationDrawerAnimationDuration)
            {
                EasingFunction = OperationDrawerEasing
            };

            OperationDrawerHost.BeginAnimation(UIElement.OpacityProperty, opacityAnimation);
            OperationDrawerTranslateTransform.BeginAnimation(TranslateTransform.XProperty, translateAnimation);
        }

        private static T? FindAncestor<T>(DependencyObject? current)
            where T : DependencyObject
        {
            while (current is not null)
            {
                if (current is T ancestor)
                {
                    return ancestor;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return null;
        }
    }
}
