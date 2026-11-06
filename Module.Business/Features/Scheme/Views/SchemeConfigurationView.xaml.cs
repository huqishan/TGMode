using Module.Business.Features.Scheme.ViewModels;
using Module.Business.Features.Scheme.ViewModels.PresentationModels;
using Module.Business.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Module.Business.Features.Scheme.Views
{
    /// <summary>
    /// 方案配置视图，负责方案与方案业务的维护及拖拽排序。
    /// </summary>
    public partial class SchemeConfigurationView : UserControl
    {
        #region 拖拽数据格式
        private const string SchemeStepDragDataFormat = "Module.Business.SchemeWorkStepItem";
        private const string BuiltInWorkStepDragDataFormat = "Module.Business.BuiltInWorkStepName";
        private Point _schemeStepDragStartPoint;
        private SchemeWorkStepItem? _pendingDraggedSchemeStep;
        private Point _batchWorkStepDragStartPoint;
        private string? _pendingDraggedBuiltInWorkStepName;
        private static readonly Duration WorkStepDrawerAnimationDuration = new(TimeSpan.FromMilliseconds(220));

        #endregion

        #region 构造与生命周期

        /// <summary>
        /// 初始化方案配置视图。
        /// </summary>
        public SchemeConfigurationView()
        {
            InitializeComponent();
            InitializeWorkStepParameterDrawer();
        }

        /// <summary>
        /// 使用指定视图模型初始化方案配置视图。
        /// </summary>
        /// <param name="viewModel">方案配置视图模型。</param>
        public SchemeConfigurationView(SchemeConfigurationViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            InitializeWorkStepParameterDrawer();
        }

        private SchemeConfigurationViewModel? ViewModel => DataContext as SchemeConfigurationViewModel;

        /// <summary>
        /// 初始化页面抽屉的视图模型监听与初始位置。
        /// </summary>
        private void InitializeWorkStepParameterDrawer()
        {
            DataContextChanged += SchemeConfigurationView_DataContextChanged;
            Loaded += (_, _) =>
            {
                UpdateWorkStepParameterDrawerVisual(false);
                UpdateBatchWorkStepDrawerVisual(false);
            };
            if (ViewModel is not null)
            {
                ViewModel.PropertyChanged += ViewModel_PropertyChanged;
            }
        }

        /// <summary>
        /// 数据上下文切换时重新监听抽屉状态，避免旧视图模型继续持有视图。
        /// </summary>
        private void SchemeConfigurationView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is SchemeConfigurationViewModel oldViewModel)
            {
                oldViewModel.PropertyChanged -= ViewModel_PropertyChanged;
            }

            if (e.NewValue is SchemeConfigurationViewModel newViewModel)
            {
                newViewModel.PropertyChanged += ViewModel_PropertyChanged;
            }

            UpdateWorkStepParameterDrawerVisual(false);
            UpdateBatchWorkStepDrawerVisual(false);
        }

        /// <summary>
        /// 业务参数抽屉状态变化时播放底部侧滑动画。
        /// </summary>
        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SchemeConfigurationViewModel.IsWorkStepParameterDrawerOpen))
            {
                UpdateWorkStepParameterDrawerVisual(true);
            }

            if (e.PropertyName == nameof(SchemeConfigurationViewModel.IsBatchWorkStepDrawerOpen))
            {
                UpdateBatchWorkStepDrawerVisual(true);
            }
        }

        /// <summary>
        /// 点击遮罩时关闭业务参数抽屉。
        /// </summary>
        private void WorkStepParameterDrawerBackdrop_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            ViewModel?.CloseWorkStepParameterDrawerCommand.Execute(null);
        }

        /// <summary>
        /// 点击遮罩时关闭批量业务浏览抽屉。
        /// </summary>
        private void BatchWorkStepDrawerBackdrop_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            ViewModel?.CloseBatchWorkStepDrawerCommand.Execute(null);
        }

        /// <summary>
        /// 用户改变内置业务下拉选项时仅同步业务名称；输入、返回参数在保存后刷新。
        /// </summary>
        private void WorkStepTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is not ComboBox comboBox ||
                (!comboBox.IsKeyboardFocusWithin && !comboBox.IsDropDownOpen) ||
                comboBox.SelectedItem is not string workStepName ||
                ViewModel?.EditingSchemeWorkStep is null)
            {
                return;
            }

            ViewModel.EditingSchemeWorkStep.StepName = workStepName;
        }

        /// <summary>
        /// 根据视图模型状态更新抽屉透明度、纵向偏移和鼠标命中状态。
        /// </summary>
        private void UpdateWorkStepParameterDrawerVisual(bool animate)
        {
            if (WorkStepParameterDrawerHost is null || WorkStepParameterDrawerTranslateTransform is null)
            {
                return;
            }

            bool isOpen = ViewModel?.IsWorkStepParameterDrawerOpen == true;
            double targetOpacity = isOpen ? 1d : 0d;
            double targetOffset = isOpen ? 0d : 56d;
            if (isOpen)
            {
                WorkStepParameterDrawerHost.IsHitTestVisible = true;
            }

            if (!animate)
            {
                WorkStepParameterDrawerHost.Opacity = targetOpacity;
                WorkStepParameterDrawerTranslateTransform.Y = targetOffset;
                WorkStepParameterDrawerHost.IsHitTestVisible = isOpen;
                return;
            }

            DoubleAnimation opacityAnimation = new(targetOpacity, WorkStepDrawerAnimationDuration);
            if (!isOpen)
            {
                opacityAnimation.Completed += (_, _) => WorkStepParameterDrawerHost.IsHitTestVisible = false;
            }

            WorkStepParameterDrawerHost.BeginAnimation(OpacityProperty, opacityAnimation);
            WorkStepParameterDrawerTranslateTransform.BeginAnimation(
                TranslateTransform.YProperty,
                new DoubleAnimation(targetOffset, WorkStepDrawerAnimationDuration));
        }

        /// <summary>
        /// 根据视图模型状态更新批量业务抽屉的透明度、横向偏移和鼠标命中状态。
        /// </summary>
        private void UpdateBatchWorkStepDrawerVisual(bool animate)
        {
            if (BatchWorkStepDrawerHost is null || BatchWorkStepDrawerTranslateTransform is null)
            {
                return;
            }

            bool isOpen = ViewModel?.IsBatchWorkStepDrawerOpen == true;
            double targetOpacity = isOpen ? 1d : 0d;
            double targetOffset = isOpen ? 0d : -56d;
            if (isOpen)
            {
                BatchWorkStepDrawerHost.IsHitTestVisible = true;
            }

            if (!animate)
            {
                BatchWorkStepDrawerHost.Opacity = targetOpacity;
                BatchWorkStepDrawerTranslateTransform.X = targetOffset;
                BatchWorkStepDrawerHost.IsHitTestVisible = isOpen;
                return;
            }

            DoubleAnimation opacityAnimation = new(targetOpacity, WorkStepDrawerAnimationDuration);
            if (!isOpen)
            {
                opacityAnimation.Completed += (_, _) => BatchWorkStepDrawerHost.IsHitTestVisible = false;
            }

            BatchWorkStepDrawerHost.BeginAnimation(OpacityProperty, opacityAnimation);
            BatchWorkStepDrawerTranslateTransform.BeginAnimation(
                TranslateTransform.XProperty,
                new DoubleAnimation(targetOffset, WorkStepDrawerAnimationDuration));
        }

        /// <summary>
        /// 处理视图加载后的初始化逻辑。
        /// </summary>
        /// <summary>
        /// 处理视图加载后的初始化逻辑。
        /// </summary>
        #endregion

        #region 视图模型联动
        /// <summary>
        /// 响应视图模型属性变化并刷新界面状态。
        /// </summary>
        /// <summary>
        #endregion

        #region 方案业务拖拽

        /// <summary>
        /// 处理界面按钮点击事件。
        /// </summary>
        private void SaveSchemesButton_Click(object sender, RoutedEventArgs e)
        {
            CommitSchemeNameTextBoxes();
            CommitEditableDataGrids();

            if (ViewModel?.SaveSchemesCommand.CanExecute(null) == true)
            {
                ViewModel.SaveSchemesCommand.Execute(null);
            }
        }

        /// <summary>
        /// 提交输入参数和返回参数表格的当前编辑，再保存方案业务参数。
        /// </summary>
        private void SaveWorkStepParameterDrawerButton_Click(object sender, RoutedEventArgs e)
        {
            // 显式刷新编辑控件的绑定源，避免模板内控件仍持有界面值而参数对象还是默认值。
            foreach (TextBox textBox in FindVisualChildren<TextBox>(InvokeParameterDataGrid)
                         .Concat(FindVisualChildren<TextBox>(ReturnParameterDataGrid)))
            {
                textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            }

            foreach (ComboBox comboBox in FindVisualChildren<ComboBox>(ReturnParameterDataGrid))
            {
                comboBox.GetBindingExpression(ComboBox.SelectedItemProperty)?.UpdateSource();
                comboBox.GetBindingExpression(ComboBox.TextProperty)?.UpdateSource();
            }

            // 再结束 DataGrid 当前单元格和行的编辑事务，确保行级数据同步完成。
            InvokeParameterDataGrid?.CommitEdit(DataGridEditingUnit.Cell, true);
            InvokeParameterDataGrid?.CommitEdit(DataGridEditingUnit.Row, true);
            ReturnParameterDataGrid?.CommitEdit(DataGridEditingUnit.Cell, true);
            ReturnParameterDataGrid?.CommitEdit(DataGridEditingUnit.Row, true);

            if (ViewModel?.SaveWorkStepParameterDrawerCommand.CanExecute(null) == true)
            {
                ViewModel.SaveWorkStepParameterDrawerCommand.Execute(null);
            }
        }

        /// <summary>
        /// 提交页面内所有可编辑表格的当前编辑。
        /// </summary>
        private void CommitEditableDataGrids()
        {
            SchemeStepsDataGrid?.CommitEdit(DataGridEditingUnit.Cell, true);
            SchemeStepsDataGrid?.CommitEdit(DataGridEditingUnit.Row, true);
            InvokeParameterDataGrid?.CommitEdit(DataGridEditingUnit.Cell, true);
            InvokeParameterDataGrid?.CommitEdit(DataGridEditingUnit.Row, true);
            ReturnParameterDataGrid?.CommitEdit(DataGridEditingUnit.Cell, true);
            ReturnParameterDataGrid?.CommitEdit(DataGridEditingUnit.Row, true);
        }

        /// <summary>
        /// 提交方案名称文本框的当前输入。
        /// </summary>
        private void CommitSchemeNameTextBoxes()
        {
            if (SchemesListBox is null)
            {
                return;
            }

            foreach (TextBox textBox in FindVisualChildren<TextBox>(SchemesListBox))
            {
                BindingExpression? bindingExpression = textBox.GetBindingExpression(TextBox.TextProperty);
                if (bindingExpression?.ParentBinding?.Path?.Path == nameof(SchemeProfile.SchemeName))
                {
                    bindingExpression.UpdateSource();
                }
            }
        }

        /// <summary>
        /// 处理鼠标交互事件。
        /// </summary>
        private void SchemeStepsDataGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (IsInlineEditableSchemeStepElement(e.OriginalSource as DependencyObject))
            {
                _pendingDraggedSchemeStep = null;
                return;
            }

            _schemeStepDragStartPoint = e.GetPosition(SchemeStepsDataGrid);
            _pendingDraggedSchemeStep = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject)?.Item as SchemeWorkStepItem;
        }

        /// <summary>
        /// 处理鼠标交互事件。
        /// </summary>
        private void SchemeStepsDataGrid_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _pendingDraggedSchemeStep is null)
            {
                return;
            }

            Point currentPoint = e.GetPosition(SchemeStepsDataGrid);
            if (Math.Abs(currentPoint.X - _schemeStepDragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(currentPoint.Y - _schemeStepDragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            SchemeWorkStepItem draggedSchemeStep = _pendingDraggedSchemeStep;
            _pendingDraggedSchemeStep = null;

            DataObject dataObject = new();
            dataObject.SetData(SchemeStepDragDataFormat, draggedSchemeStep);
            DragDrop.DoDragDrop(SchemeStepsDataGrid, dataObject, DragDropEffects.Move);
        }

        /// <summary>
        /// 处理鼠标交互事件。
        /// </summary>
        private void SchemeStepsDataGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject) is not DataGridRow row)
            {
                return;
            }

            row.IsSelected = true;
            SchemeStepsDataGrid.SelectedItem = row.Item;
            row.Focus();
        }

        /// <summary>
        /// 处理拖拽交互逻辑。
        /// </summary>
        private void SchemeStepsDataGrid_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(BuiltInWorkStepDragDataFormat) &&
                e.Data.GetData(BuiltInWorkStepDragDataFormat) is string builtInWorkStepName &&
                !string.IsNullOrWhiteSpace(builtInWorkStepName))
            {
                DataGridRow? targetRow = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
                bool builtInInsertAfter = targetRow is not null && e.GetPosition(targetRow).Y > targetRow.ActualHeight / 2d;
                if (targetRow is null)
                {
                    HideSchemeStepDropIndicator();
                }
                else
                {
                    ShowSchemeStepDropIndicator(targetRow, builtInInsertAfter);
                }

                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
                return;
            }

            if (!TryGetSchemeStepDropInfo(e, out _, out _, out bool insertAfter))
            {
                HideSchemeStepDropIndicator();
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            ShowSchemeStepDropIndicator(FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject), insertAfter);
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }

        /// <summary>
        /// 处理拖拽交互逻辑。
        /// </summary>
        private void SchemeStepsDataGrid_DragLeave(object sender, DragEventArgs e)
        {
            HideSchemeStepDropIndicator();
        }

        /// <summary>
        /// 处理拖拽交互逻辑。
        /// </summary>
        private void SchemeStepsDataGrid_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(BuiltInWorkStepDragDataFormat) &&
                e.Data.GetData(BuiltInWorkStepDragDataFormat) is string builtInWorkStepName &&
                !string.IsNullOrWhiteSpace(builtInWorkStepName))
            {
                DataGridRow? targetRow = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
                SchemeWorkStepItem? builtInTargetSchemeStep = targetRow?.Item as SchemeWorkStepItem;
                bool builtInInsertAfter = targetRow is not null && e.GetPosition(targetRow).Y > targetRow.ActualHeight / 2d;
                ViewModel?.AddBuiltInWorkStepToScheme(builtInWorkStepName, builtInTargetSchemeStep, builtInInsertAfter);
                _pendingDraggedBuiltInWorkStepName = null;
                HideSchemeStepDropIndicator();
                e.Handled = true;
                return;
            }

            if (TryGetSchemeStepDropInfo(e, out SchemeWorkStepItem? draggedSchemeStep, out SchemeWorkStepItem? targetSchemeStep, out bool insertAfter) &&
                draggedSchemeStep is not null &&
                targetSchemeStep is not null)
            {
                ViewModel?.MoveSchemeStep(draggedSchemeStep, targetSchemeStep, insertAfter);
            }

            _pendingDraggedSchemeStep = null;
            HideSchemeStepDropIndicator();
            e.Handled = true;
        }

        /// <summary>
        /// 处理拖拽交互逻辑。
        /// </summary>
        private bool TryGetSchemeStepDropInfo(
            DragEventArgs e,
            out SchemeWorkStepItem? draggedSchemeStep,
            out SchemeWorkStepItem? targetSchemeStep,
            out bool insertAfter)
        {
            draggedSchemeStep = e.Data.GetDataPresent(SchemeStepDragDataFormat)
                ? e.Data.GetData(SchemeStepDragDataFormat) as SchemeWorkStepItem
                : null;
            targetSchemeStep = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject)?.Item as SchemeWorkStepItem;
            insertAfter = false;

            if (draggedSchemeStep is null || targetSchemeStep is null || ReferenceEquals(draggedSchemeStep, targetSchemeStep))
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

        /// <summary>
        /// 处理拖拽交互逻辑。
        /// </summary>
        private void ShowSchemeStepDropIndicator(DataGridRow? targetRow, bool insertAfter)
        {
            if (targetRow is null || SchemeStepDropIndicatorCanvas is null || SchemeStepDropIndicator is null)
            {
                HideSchemeStepDropIndicator();
                return;
            }

            double horizontalPadding = 8d;
            double indicatorHeight = 3d;
            double width = Math.Max(0d, SchemeStepDropIndicatorCanvas.ActualWidth - horizontalPadding * 2);
            Point rowTopLeft = targetRow.TranslatePoint(new Point(0, 0), SchemeStepDropIndicatorCanvas);
            double top = rowTopLeft.Y + (insertAfter ? targetRow.ActualHeight : 0d) - indicatorHeight / 2d;
            top = Math.Clamp(top, 0d, Math.Max(0d, SchemeStepDropIndicatorCanvas.ActualHeight - indicatorHeight));

            SchemeStepDropIndicator.Width = width;
            SchemeStepDropIndicator.Height = indicatorHeight;
            Canvas.SetLeft(SchemeStepDropIndicator, horizontalPadding);
            Canvas.SetTop(SchemeStepDropIndicator, top);
            SchemeStepDropIndicator.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// 处理拖拽交互逻辑。
        /// </summary>
        private void HideSchemeStepDropIndicator()
        {
            if (SchemeStepDropIndicator is not null)
            {
                SchemeStepDropIndicator.Visibility = Visibility.Collapsed;
            }
        }

        #endregion

        /// <summary>
        /// 双击方案业务行时打开底部参数编辑抽屉；编辑控件自身的双击仍保留原行为。
        /// </summary>
        private void SchemeStepsDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (IsInlineEditableSchemeStepElement(e.OriginalSource as DependencyObject) ||
                FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject)?.Item is not SchemeWorkStepItem workStep)
            {
                return;
            }

            SchemeStepsDataGrid.SelectedItem = workStep;
            ViewModel?.OpenWorkStepParameterDrawerCommand.Execute(workStep);
            e.Handled = true;
        }

        /// <summary>
        /// 记录批量业务列表中的拖拽起点和待添加的内置业务名称。
        /// </summary>
        private void BatchWorkStepsListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _batchWorkStepDragStartPoint = e.GetPosition(BatchWorkStepsListBox);
            _pendingDraggedBuiltInWorkStepName = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext as string;
        }

        /// <summary>
        /// 鼠标移动超过系统拖拽阈值后启动内置业务复制拖拽。
        /// </summary>
        private void BatchWorkStepsListBox_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || string.IsNullOrWhiteSpace(_pendingDraggedBuiltInWorkStepName))
            {
                return;
            }

            Point currentPoint = e.GetPosition(BatchWorkStepsListBox);
            if (Math.Abs(currentPoint.X - _batchWorkStepDragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(currentPoint.Y - _batchWorkStepDragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            string workStepName = _pendingDraggedBuiltInWorkStepName;
            _pendingDraggedBuiltInWorkStepName = null;
            DataObject dataObject = new();
            dataObject.SetData(BuiltInWorkStepDragDataFormat, workStepName);
            DragDrop.DoDragDrop(BatchWorkStepsListBox, dataObject, DragDropEffects.Copy);
        }

        /// <summary>
        /// 序号单元格提交后按照输入值移动业务，并重新生成连续序号。
        /// 延迟到 DataGrid 完成编辑事务后再移动集合，避免提交过程中修改 ItemsSource。
        /// </summary>
        private void SchemeStepsDataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit ||
                e.Column.SortMemberPath != nameof(SchemeWorkStepItem.Num) ||
                e.Row.Item is not SchemeWorkStepItem workStep)
            {
                return;
            }

            if (e.EditingElement is TextBox textBox)
            {
                textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            }

            int targetNumber = workStep.Num;
            Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(() => ViewModel?.MoveSchemeStepToNumber(workStep, targetNumber)));
        }

        #if false
        /// <summary>
        /// 记录步骤拖拽起点；复选框及可编辑单元格继续保留原有点击、编辑行为。
        /// </summary>
        private void OperationsDataGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DependencyObject? source = e.OriginalSource as DependencyObject;
            if (IsOperationSelectionCheckBox(source) || IsInlineEditableOperationCell(source))
            {
                _pendingDraggedOperation = null;
                return;
            }

            _operationDragStartPoint = e.GetPosition(OperationsDataGrid);
            _pendingDraggedOperation = FindAncestor<DataGridRow>(source)?.Item as WorkStepOperation;
        }

        /// <summary>
        /// 鼠标移动超过系统阈值后开始拖拽，避免普通单击被误判为排序。
        /// </summary>
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

        /// <summary>
        /// 根据目标行的上半区或下半区显示步骤插入位置。
        /// </summary>
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

        /// <summary>
        /// 鼠标离开列表时隐藏插入位置提示。
        /// </summary>
        private void OperationsDataGrid_DragLeave(object sender, DragEventArgs e)
        {
            HideOperationDropIndicator();
        }

        /// <summary>
        /// 完成当前业务内的步骤移动并清理拖拽状态。
        /// </summary>
        private void OperationsDataGrid_Drop(object sender, DragEventArgs e)
        {
            if (TryGetOperationDropInfo(e, out WorkStepOperation? draggedOperation, out WorkStepOperation? targetOperation, out bool insertAfter) &&
                draggedOperation is not null && targetOperation is not null)
            {
                ViewModel?.MoveOperationStep(draggedOperation, targetOperation, insertAfter);
            }

            _pendingDraggedOperation = null;
            HideOperationDropIndicator();
            e.Handled = true;
        }

        /// <summary>
        /// 解析拖拽步骤、目标步骤及插入方向。
        /// </summary>
        private static bool TryGetOperationDropInfo(DragEventArgs e, out WorkStepOperation? draggedOperation,
            out WorkStepOperation? targetOperation, out bool insertAfter)
        {
            draggedOperation = e.Data.GetDataPresent(OperationDragDataFormat)
                ? e.Data.GetData(OperationDragDataFormat) as WorkStepOperation : null;
            DataGridRow? targetRow = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
            targetOperation = targetRow?.Item as WorkStepOperation;
            insertAfter = targetRow is not null && e.GetPosition(targetRow).Y > targetRow.ActualHeight / 2d;
            return draggedOperation is not null && targetOperation is not null &&
                   !ReferenceEquals(draggedOperation, targetOperation);
        }

        /// <summary>
        /// 在目标行边缘绘制步骤插入提示线。
        /// </summary>
        private void ShowOperationDropIndicator(DataGridRow? targetRow, bool insertAfter)
        {
            if (targetRow is null || OperationDropIndicatorCanvas is null || OperationDropIndicator is null)
            {
                HideOperationDropIndicator();
                return;
            }

            const double horizontalPadding = 8d;
            const double indicatorHeight = 3d;
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

        /// <summary>
        /// 隐藏步骤插入位置提示线。
        /// </summary>
        private void HideOperationDropIndicator()
        {
            if (OperationDropIndicator is not null)
            {
                OperationDropIndicator.Visibility = Visibility.Collapsed;
            }
        }

        private void OperationsDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (IsOperationSelectionCheckBox(e.OriginalSource as DependencyObject) ||
                IsInlineEditableOperationCell(e.OriginalSource as DependencyObject))
            {
                return;
            }

            if (FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject)?.Item is not WorkStepOperation operation)
            {
                return;
            }

            OperationsDataGrid.SelectedItem = operation;
            ViewModel?.OpenOperationEditorForEdit(operation);
            e.Handled = true;
        }

        #endif

        #region 抽屉动画

        /// <summary>
        /// 处理鼠标交互事件。
        /// </summary>
        /// <summary>
        /// 更新操作编辑抽屉的显示状态。
        /// </summary>
        #endregion

        #region 交互辅助方法

        /// <summary>
        /// 判断是否满足指定业务条件。
        /// </summary>
        private static bool IsInlineEditableSchemeStepElement(DependencyObject? source)
        {
            return FindAncestor<TextBox>(source) is not null ||
                   FindAncestor<ComboBox>(source) is not null ||
                   FindAncestor<CheckBox>(source) is not null;
        }

        /// <summary>
        /// 判断是否满足指定业务条件。
        /// </summary>
        #if false
        private static bool IsInlineEditableOperationCell(DependencyObject? source)
        {
            if (FindAncestor<ComboBox>(source) is not null ||
                FindAncestor<TextBox>(source) is not null ||
                FindAncestor<Button>(source) is not null)
            {
                return true;
            }

            DataGridCell? cell = FindAncestor<DataGridCell>(source);
            string? bindingPath = null;
            if (cell?.Column is DataGridBoundColumn boundColumn &&
                boundColumn.Binding is Binding binding)
            {
                bindingPath = binding.Path?.Path;
            }
            else if (cell?.Column is DataGridTemplateColumn templateColumn)
            {
                bindingPath = templateColumn.SortMemberPath;
            }

            return string.Equals(bindingPath, nameof(WorkStepOperation.IsEditParameter), StringComparison.Ordinal) ||
                   string.Equals(bindingPath, nameof(WorkStepOperation.DelayMilliseconds), StringComparison.Ordinal);

        }

        /// <summary>
        /// 判断是否满足指定业务条件。
        /// </summary>
        private static bool IsOperationSelectionCheckBox(DependencyObject? source)
        {
            return FindAncestor<CheckBox>(source) is not null;
        }

        #endif
        private static T? FindAncestor<T>(DependencyObject? current)
            where T : DependencyObject
        {
            while (current is not null)
            {
                if (current is T ancestor)
                {
                    return ancestor;
                }

                current = GetParentObject(current);
            }

            return null;
        }

        private static System.Collections.Generic.IEnumerable<T> FindVisualChildren<T>(DependencyObject? root)
            where T : DependencyObject
        {
            if (root is null)
            {
                yield break;
            }

            int childCount = VisualTreeHelper.GetChildrenCount(root);
            for (int index = 0; index < childCount; index++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, index);
                if (child is T typedChild)
                {
                    yield return typedChild;
                }

                foreach (T descendant in FindVisualChildren<T>(child))
                {
                    yield return descendant;
                }
            }
        }

        /// <summary>
        /// 获取指定依赖对象的父级对象。
        /// </summary>
        private static DependencyObject? GetParentObject(DependencyObject source)
        {
            if (source is Visual or System.Windows.Media.Media3D.Visual3D)
            {
                return VisualTreeHelper.GetParent(source);
            }

            if (source is FrameworkContentElement frameworkContentElement)
            {
                return frameworkContentElement.Parent as DependencyObject;
            }

            return null;
        }

        #endregion
    }
}
