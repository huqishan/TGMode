using Module.Business.Models;
using Module.Business.ViewModels;
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Module.Business.Views
{
    /// <summary>
    /// 方案配置界面的交互逻辑。
    /// </summary>
    public partial class SchemeConfigurationView : UserControl
    {
        #region 常量与字段

        private const string SchemeStepDragDataFormat = "Module.Business.SchemeWorkStepItem";
        private const string OperationDragDataFormat = "Module.Business.WorkStepOperation";
        private const double OperationDrawerClosedOffset = 56d;

        private static readonly Duration OperationDrawerAnimationDuration =
            new(TimeSpan.FromMilliseconds(220));

        private static readonly IEasingFunction OperationDrawerEasing =
            new CubicEase { EasingMode = EasingMode.EaseOut };

        private Point _schemeStepDragStartPoint;
        private Point _operationDragStartPoint;
        private SchemeWorkStepItem? _pendingDraggedSchemeStep;
        private WorkStepOperation? _pendingDraggedOperation;

        #endregion

        #region 构造与生命周期

        public SchemeConfigurationView()
        {
            InitializeComponent();
            DataContext = new SchemeConfigurationViewModel();
            Loaded += SchemeConfigurationView_Loaded;
            Unloaded += SchemeConfigurationView_Unloaded;
            UpdateOperationDrawerVisual(animate: false);
        }

        private SchemeConfigurationViewModel? ViewModel => DataContext as SchemeConfigurationViewModel;

        private void SchemeConfigurationView_Loaded(object sender, RoutedEventArgs e)
        {
            if (ViewModel is not null)
            {
                ViewModel.SchemeStepEditor.PropertyChanged += SchemeStepEditor_PropertyChanged;
            }

            UpdateOperationDrawerVisual(animate: false);
        }

        private void SchemeConfigurationView_Unloaded(object sender, RoutedEventArgs e)
        {
            if (ViewModel is not null)
            {
                ViewModel.SchemeStepEditor.PropertyChanged -= SchemeStepEditor_PropertyChanged;
            }
        }

        #endregion

        #region 属性联动

        /// <summary>
        /// 切换当前方案时退出重命名态，避免文本框停留在旧方案上。
        /// </summary>
        /// <summary>
        /// 同步步骤抽屉开关动画。
        /// </summary>
        private void SchemeStepEditor_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(WorkStepConfigurationViewModel.IsOperationDrawerOpen))
            {
                UpdateOperationDrawerVisual(animate: true);
            }
        }

        #endregion

        #region 方案名称重命名

        /// <summary>
        /// 进入方案名称编辑态，显示文本框并自动全选。
        /// </summary>
        /// <summary>
        /// 文本框失焦后退出编辑态并保留当前输入。
        /// </summary>
        /// <summary>
        /// 回车确认，Esc 恢复编辑前名称。
        /// </summary>
        /// <summary>
        /// 控制方案名称的显示态与编辑态切换。
        /// </summary>
        #endregion

        #region 方案工步拖拽

        private void SaveSchemesButton_Click(object sender, RoutedEventArgs e)
        {
            CommitSchemeNameTextBoxes();

            if (ViewModel?.SaveSchemesCommand.CanExecute(null) == true)
            {
                ViewModel.SaveSchemesCommand.Execute(null);
            }
        }

        /// <summary>
        /// 淇濆瓨鍓嶆彁浜ゅ綋鍓嶆柟妗堝悕绉扮殑缁戝畾鍊硷紝閬垮厤鍥犳湭澶辩劍鑰屼涪澶辨渶鍚庝竴娆¤緭鍏ャ€?
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

        private void SchemeStepsDataGrid_DragOver(object sender, DragEventArgs e)
        {
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

        private void SchemeStepsDataGrid_DragLeave(object sender, DragEventArgs e)
        {
            HideSchemeStepDropIndicator();
        }

        private void SchemeStepsDataGrid_Drop(object sender, DragEventArgs e)
        {
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

        private void HideSchemeStepDropIndicator()
        {
            if (SchemeStepDropIndicator is not null)
            {
                SchemeStepDropIndicator.Visibility = Visibility.Collapsed;
            }
        }

        #endregion

        #region 步骤拖拽与双击编辑

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
            ViewModel?.SchemeStepEditor.OpenOperationDrawerForEdit(operation);
            e.Handled = true;
        }

        private void OperationsDataGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (IsOperationSelectionCheckBox(e.OriginalSource as DependencyObject) ||
                IsInlineEditableOperationCell(e.OriginalSource as DependencyObject))
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
                ViewModel?.SchemeStepEditor.MoveOperation(draggedOperation, targetOperation, insertAfter);
            }

            _pendingDraggedOperation = null;
            HideOperationDropIndicator();
            e.Handled = true;
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

        #endregion

        #region 抽屉动画

        private void OperationDrawerBackdrop_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (ViewModel?.SchemeStepEditor.CloseOperationDrawerCommand.CanExecute(null) == true)
            {
                ViewModel.SchemeStepEditor.CloseOperationDrawerCommand.Execute(null);
            }
        }

        /// <summary>
        /// 更新步骤编辑抽屉的显示状态。
        /// </summary>
        private void UpdateOperationDrawerVisual(bool animate)
        {
            if (OperationDrawerHost is null || OperationDrawerTranslateTransform is null)
            {
                return;
            }

            bool isOpen = ViewModel?.SchemeStepEditor.IsOperationDrawerOpen == true;
            double targetOpacity = isOpen ? 1d : 0d;
            double targetOffset = isOpen ? 0d : OperationDrawerClosedOffset;

            if (isOpen)
            {
                OperationDrawerHost.IsHitTestVisible = true;
            }

            if (!animate)
            {
                OperationDrawerHost.BeginAnimation(UIElement.OpacityProperty, null);
                OperationDrawerTranslateTransform.BeginAnimation(TranslateTransform.YProperty, null);
                OperationDrawerHost.Opacity = targetOpacity;
                OperationDrawerTranslateTransform.Y = targetOffset;
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
                    if (ViewModel?.SchemeStepEditor.IsOperationDrawerOpen != true)
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
            OperationDrawerTranslateTransform.BeginAnimation(TranslateTransform.YProperty, translateAnimation);
        }

        #endregion

        #region 交互辅助方法

        private static bool IsInlineEditableSchemeStepElement(DependencyObject? source)
        {
            return FindAncestor<TextBox>(source) is not null ||
                   FindAncestor<ComboBox>(source) is not null ||
                   FindAncestor<CheckBox>(source) is not null;
        }

        private static bool IsInlineEditableOperationCell(DependencyObject? source)
        {
            DataGridCell? cell = FindAncestor<DataGridCell>(source);
            if (cell?.Column is DataGridBoundColumn boundColumn &&
                boundColumn.Binding is Binding binding)
            {
                string? bindingPath = binding.Path?.Path;

                return string.Equals(bindingPath, nameof(WorkStepOperation.DelayMilliseconds), StringComparison.Ordinal) ||
                       string.Equals(bindingPath, nameof(WorkStepOperation.Remark), StringComparison.Ordinal);
            }

            return false;
        }

        private static bool IsOperationSelectionCheckBox(DependencyObject? source)
        {
            return FindAncestor<CheckBox>(source) is not null;
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
