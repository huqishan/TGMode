using Module.Business.Features.OperationEditing.ViewModels.PresentationModels;
using Module.Business.Features.WorkStep.ViewModels;
using Module.Business.Features.WorkStep.ViewModels.PresentationModels;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Module.Business.Features.WorkStep.Views;

/// <summary>
/// 业务配置视图，负责提交界面编辑以及步骤拖拽交互。
/// </summary>
public partial class WorkStepConfigurationView : UserControl
{
    #region 拖拽状态与构造

    private const string OperationDragDataFormat = "Module.Business.WorkStep.WorkStepOperation";
    private Point _dragStartPoint;
    private WorkStepOperation? _pendingOperation;

    public WorkStepConfigurationView()
    {
        InitializeComponent();
    }

    public WorkStepConfigurationView(WorkStepConfigurationViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }

    private WorkStepConfigurationViewModel? ViewModel => DataContext as WorkStepConfigurationViewModel;

    #endregion

    #region 保存与编辑

    private void SaveWorkStepsButton_Click(object sender, RoutedEventArgs e)
    {
        OperationsDataGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        OperationsDataGrid.CommitEdit(DataGridEditingUnit.Row, true);

        // ListBox 中的名称编辑框可能仍持有焦点，保存前显式提交绑定以保证落盘内容完整。
        foreach (TextBox textBox in FindVisualChildren<TextBox>(WorkStepsListBox))
        {
            BindingExpression? binding = textBox.GetBindingExpression(TextBox.TextProperty);
            if (binding?.ParentBinding?.Path?.Path == nameof(WorkStepProfile.Name))
            {
                binding.UpdateSource();
            }
        }

        if (ViewModel?.SaveWorkStepsCommand.CanExecute(null) == true)
        {
            ViewModel.SaveWorkStepsCommand.Execute(null);
        }
    }

    private void OperationsDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject)?.Item is WorkStepOperation operation)
        {
            ViewModel?.OpenOperationEditorForEdit(operation);
        }
    }

    /// <summary>
    /// 序号提交后刷新集合位置。延迟到 DataGrid 完成本次提交后再移动集合，
    /// 避免单元格仍处于编辑事务时修改 ItemsSource 引发提交状态冲突。
    /// </summary>
    private void OperationsDataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit ||
            e.Column.SortMemberPath != nameof(WorkStepOperation.Num) ||
            e.Row.Item is not WorkStepOperation operation)
        {
            return;
        }

        if (e.EditingElement is TextBox textBox)
        {
            textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        }

        Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() => ViewModel?.MoveOperationToNumber(operation)));
    }

    #endregion

    #region 步骤拖拽

    private void OperationsDataGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(OperationsDataGrid);
        _pendingOperation = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject)?.Item as WorkStepOperation;
    }

    private void OperationsDataGrid_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _pendingOperation is null)
        {
            return;
        }

        Point current = e.GetPosition(OperationsDataGrid);
        if (Math.Abs(current.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        WorkStepOperation operation = _pendingOperation;
        _pendingOperation = null;
        DataObject data = new();
        data.SetData(OperationDragDataFormat, operation);
        DragDrop.DoDragDrop(OperationsDataGrid, data, DragDropEffects.Move);
    }

    private void OperationsDataGrid_DragOver(object sender, DragEventArgs e)
    {
        if (!TryGetDropInfo(e, out _, out _, out bool insertAfter))
        {
            OperationDropIndicator.Visibility = Visibility.Collapsed;
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        DataGridRow? row = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row is not null)
        {
            Point topLeft = row.TranslatePoint(new Point(0, 0), OperationDropIndicatorCanvas);
            double top = topLeft.Y + (insertAfter ? row.ActualHeight : 0d) - 1.5d;
            OperationDropIndicator.Width = Math.Max(0d, OperationDropIndicatorCanvas.ActualWidth - 16d);
            Canvas.SetLeft(OperationDropIndicator, 8d);
            Canvas.SetTop(OperationDropIndicator, Math.Max(0d, top));
            OperationDropIndicator.Visibility = Visibility.Visible;
        }

        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void OperationsDataGrid_DragLeave(object sender, DragEventArgs e)
    {
        OperationDropIndicator.Visibility = Visibility.Collapsed;
    }

    private void OperationsDataGrid_Drop(object sender, DragEventArgs e)
    {
        if (TryGetDropInfo(e, out WorkStepOperation? source, out WorkStepOperation? target, out bool insertAfter) &&
            source is not null && target is not null)
        {
            ViewModel?.MoveOperation(source, target, insertAfter);
        }

        _pendingOperation = null;
        OperationDropIndicator.Visibility = Visibility.Collapsed;
        e.Handled = true;
    }

    private static bool TryGetDropInfo(DragEventArgs e, out WorkStepOperation? source,
        out WorkStepOperation? target, out bool insertAfter)
    {
        source = e.Data.GetDataPresent(OperationDragDataFormat)
            ? e.Data.GetData(OperationDragDataFormat) as WorkStepOperation
            : null;
        DataGridRow? row = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
        target = row?.Item as WorkStepOperation;
        insertAfter = row is not null && e.GetPosition(row).Y > row.ActualHeight / 2d;
        return source is not null && target is not null && !ReferenceEquals(source, target);
    }

    #endregion

    #region 可视树辅助

    private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
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

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, index);
            if (child is T result)
            {
                yield return result;
            }

            foreach (T descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }

    #endregion
}
