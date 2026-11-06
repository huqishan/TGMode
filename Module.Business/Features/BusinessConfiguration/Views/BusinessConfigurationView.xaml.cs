using Module.Business.Features.OperationEditing.ViewModels.PresentationModels;
using Module.Business.Features.BusinessConfiguration.ViewModels;
using Module.Business.Features.BusinessConfiguration.ViewModels.PresentationModels;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Module.Business.Features.BusinessConfiguration.Views;

/// <summary>
/// 业务配置视图，负责提交界面编辑以及步骤拖拽交互。
/// </summary>
public partial class BusinessConfigurationView : UserControl
{
    #region 拖拽状态与构造

    /// <summary>步骤拖拽使用的 DataObject 数据格式标识。</summary>
    private const string OperationDragDataFormat = "Module.Business.WorkStep.WorkStepOperation";

    /// <summary>拖拽起始点，用于判断是否达到系统最小拖拽距离。</summary>
    private Point _dragStartPoint;

    /// <summary>按下时记录的待拖拽操作步骤。</summary>
    private WorkStepOperation? _pendingOperation;

    /// <summary>设计期构造函数，仅初始化界面元素。</summary>
    public BusinessConfigurationView()
    {
        InitializeComponent();
    }

    /// <summary>运行期构造函数，由视图工厂注入 ViewModel。</summary>
    /// <param name="viewModel">业务配置 ViewModel。</param>
    public BusinessConfigurationView(BusinessConfigurationViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }

    /// <summary>以强类型方式访问当前数据上下文。</summary>
    private BusinessConfigurationViewModel? ViewModel => DataContext as BusinessConfigurationViewModel;

    #endregion

    #region 保存与编辑

    /// <summary>
    /// 保存按钮：先提交 DataGrid 与名称编辑框中未落盘的编辑，再执行保存命令。
    /// </summary>
    private void SaveWorkStepsButton_Click(object sender, RoutedEventArgs e)
    {
        OperationsDataGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        OperationsDataGrid.CommitEdit(DataGridEditingUnit.Row, true);

        // ListBox 中的名称编辑框可能仍持有焦点，保存前显式提交绑定以保证落盘内容完整。
        foreach (TextBox textBox in FindVisualChildren<TextBox>(WorkStepsListBox))
        {
            BindingExpression? binding = textBox.GetBindingExpression(TextBox.TextProperty);
            if (binding?.ParentBinding?.Path?.Path == nameof(BusinessProfile.Name))
            {
                binding.UpdateSource();
            }
        }

        if (ViewModel?.SaveWorkStepsCommand.CanExecute(null) == true)
        {
            ViewModel.SaveWorkStepsCommand.Execute(null);
        }
    }

    /// <summary>双击操作行：打开该操作的业务编辑抽屉。</summary>
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

    /// <summary>按下鼠标：记录拖拽起始点与待拖拽的操作步骤。</summary>
    private void OperationsDataGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(OperationsDataGrid);
        _pendingOperation = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject)?.Item as WorkStepOperation;
    }

    /// <summary>移动鼠标：超过最小拖拽距离后启动 DoDragDrop 拖拽流程。</summary>
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

    /// <summary>拖拽经过：计算插入位置并显示插入指示线。</summary>
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

    /// <summary>拖拽离开：隐藏插入指示线。</summary>
    private void OperationsDataGrid_DragLeave(object sender, DragEventArgs e)
    {
        OperationDropIndicator.Visibility = Visibility.Collapsed;
    }

    /// <summary>拖拽放下：把拖拽步骤移动到目标位置并隐藏指示线。</summary>
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

    /// <summary>
    /// 解析拖拽事件中的源操作、目标行与插入方位（目标行上半部插入其前，下半部插入其后）。
    /// </summary>
    /// <returns>源与目标均有效且不同时返回 true。</returns>
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

    /// <summary>沿可视树向上查找指定类型的祖先元素。</summary>
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

    /// <summary>深度遍历可视树，枚举指定类型的全部子元素。</summary>
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
