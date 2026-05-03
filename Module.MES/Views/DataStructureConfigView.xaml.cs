using Module.MES.ViewModels;
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Module.MES.Views
{
    /// <summary>
    /// 数据结构配置页视图；业务逻辑由 DataStructureConfigViewModel 承载。
    /// </summary>
    public partial class DataStructureConfigView : UserControl
    {
        #region 抽屉动画字段

        private const double StructureDrawerClosedOffset = 56d;
        private const double TreeItemHeaderHeight = 34d;
        private const double TreeItemDropEdgeHeight = 10d;
        private static readonly Duration StructureDrawerAnimationDuration = new(TimeSpan.FromMilliseconds(220));
        private static readonly IEasingFunction StructureDrawerEasing = new CubicEase { EasingMode = EasingMode.EaseOut };
        private Point _treeDragStartPoint;
        private DataStructureLayout? _pendingDraggedField;

        #endregion

        #region 构造与 ViewModel 订阅

        public DataStructureConfigView()
        {
            InitializeComponent();

            if (ViewModel is not null)
            {
                ViewModel.PropertyChanged += ViewModel_PropertyChanged;
            }

            Unloaded += DataStructureConfigView_Unloaded;
        }

        private DataStructureConfigViewModel? ViewModel => DataContext as DataStructureConfigViewModel;

        private void DataStructureConfigView_Unloaded(object sender, RoutedEventArgs e)
        {
            if (ViewModel is not null)
            {
                ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
            }
        }

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DataStructureConfigViewModel.IsStructureDrawerOpen))
            {
                UpdateCommandDrawerVisual(animate: true);
            }
        }

        #endregion

        #region TreeView 交互

        private void StructureTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (ViewModel is not null)
            {
                ViewModel.SelectedField = e.NewValue as DataStructureLayout;
            }
        }

        private void StructureTreeView_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!IsTreeViewItemSource(e.OriginalSource as DependencyObject) && ViewModel is not null)
            {
                ViewModel.SelectedField = null;
            }
        }

        private void StructureTreeView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _pendingDraggedField = null;

            DependencyObject? source = e.OriginalSource as DependencyObject;
            if (IsInputElementSource(source))
            {
                return;
            }

            TreeViewItem? item = GetTreeViewItemFromSource(source);
            if (item?.DataContext is not DataStructureLayout field)
            {
                return;
            }

            _treeDragStartPoint = e.GetPosition(StructureTreeView);
            _pendingDraggedField = field;
        }

        private void StructureTreeView_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _pendingDraggedField is null)
            {
                return;
            }

            Point currentPoint = e.GetPosition(StructureTreeView);
            if (Math.Abs(currentPoint.X - _treeDragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(currentPoint.Y - _treeDragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            DataStructureLayout draggedField = _pendingDraggedField;
            _pendingDraggedField = null;
            DragDrop.DoDragDrop(StructureTreeView, draggedField, DragDropEffects.Move);
            HideDropIndicator();
        }

        private void StructureTreeView_DragOver(object sender, DragEventArgs e)
        {
            TreeViewItem? targetItem = GetTreeViewItemFromSource(e.OriginalSource as DependencyObject);
            if (!TryGetDropInfo(e, out DataStructureLayout? draggedField, out DataStructureLayout? targetField, out DataStructureFieldDropMode dropMode) ||
                ViewModel?.CanMoveField(draggedField, targetField, dropMode) != true)
            {
                HideDropIndicator();
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            ShowDropIndicator(targetItem, e, dropMode);
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }

        private void StructureTreeView_DragLeave(object sender, DragEventArgs e)
        {
            HideDropIndicator();
            e.Handled = true;
        }

        private void StructureTreeView_Drop(object sender, DragEventArgs e)
        {
            if (!TryGetDropInfo(e, out DataStructureLayout? draggedField, out DataStructureLayout? targetField, out DataStructureFieldDropMode dropMode))
            {
                HideDropIndicator();
                e.Handled = true;
                return;
            }

            TreeViewItem? targetItem = GetTreeViewItemFromSource(e.OriginalSource as DependencyObject);
            bool moved = ViewModel?.MoveField(draggedField, targetField, dropMode) == true;
            if (moved && targetItem is not null && dropMode == DataStructureFieldDropMode.AsChild)
            {
                targetItem.IsExpanded = true;
            }

            HideDropIndicator();
            e.Handled = true;
        }

        private void DataStructureTreeViewItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not TreeViewItem item)
            {
                return;
            }

            if (!ReferenceEquals(item, GetTreeViewItemFromSource(e.OriginalSource as DependencyObject)))
            {
                return;
            }

            item.IsSelected = true;
            item.Focus();
            e.Handled = true;
        }

        private void DataStructureFieldEditor_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            SelectTreeViewItemFromSource(e.OriginalSource as DependencyObject ?? sender as DependencyObject);
        }

        private void StructureTreeView_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (!IsTreeViewItemSource(e.OriginalSource as DependencyObject) ||
                ViewModel?.SelectedField is null ||
                !ViewModel.OpenStructureDrawerCommand.CanExecute(null))
            {
                return;
            }

            e.Handled = true;
            ViewModel.OpenStructureDrawerCommand.Execute(null);
        }

        private void SelectTreeViewItemFromSource(DependencyObject? source)
        {
            TreeViewItem? item = GetTreeViewItemFromSource(source);
            if (item is null)
            {
                return;
            }

            item.IsSelected = true;
            if (item.DataContext is DataStructureLayout field && ViewModel is not null)
            {
                ViewModel.SelectedField = field;
            }
        }

        private static bool IsTreeViewItemSource(DependencyObject? source)
        {
            return GetTreeViewItemFromSource(source) is not null;
        }

        private static bool TryGetDropInfo(
            DragEventArgs e,
            out DataStructureLayout draggedField,
            out DataStructureLayout? targetField,
            out DataStructureFieldDropMode dropMode)
        {
            draggedField = null!;
            targetField = null;
            dropMode = DataStructureFieldDropMode.AsRoot;

            if (e.Data.GetData(typeof(DataStructureLayout)) is not DataStructureLayout field)
            {
                return false;
            }

            draggedField = field;
            TreeViewItem? targetItem = GetTreeViewItemFromSource(e.OriginalSource as DependencyObject);
            targetField = targetItem?.DataContext as DataStructureLayout;
            dropMode = GetDropMode(targetItem, e);
            return true;
        }

        private static DataStructureFieldDropMode GetDropMode(TreeViewItem? targetItem, DragEventArgs e)
        {
            if (targetItem is null)
            {
                return DataStructureFieldDropMode.AsRoot;
            }

            Point targetPoint = e.GetPosition(targetItem);
            if (targetPoint.Y <= TreeItemDropEdgeHeight)
            {
                return DataStructureFieldDropMode.Before;
            }

            if (targetPoint.Y >= TreeItemHeaderHeight - TreeItemDropEdgeHeight)
            {
                return DataStructureFieldDropMode.After;
            }

            return DataStructureFieldDropMode.AsChild;
        }

        private static bool IsInputElementSource(DependencyObject? source)
        {
            while (source is not null)
            {
                if (source is TextBoxBase or ComboBox or ButtonBase)
                {
                    return true;
                }

                source = VisualTreeHelper.GetParent(source);
            }

            return false;
        }

        private void ShowDropIndicator(TreeViewItem? targetItem, DragEventArgs e, DataStructureFieldDropMode dropMode)
        {
            if (StructureDropIndicatorCanvas is null || StructureDropIndicator is null)
            {
                return;
            }

            const double sidePadding = 6d;
            double width = Math.Max(0d, StructureDropIndicatorCanvas.ActualWidth - sidePadding * 2);
            double left = sidePadding;
            double top;

            if (dropMode == DataStructureFieldDropMode.AsChild && targetItem is not null)
            {
                Point targetPoint = targetItem.TranslatePoint(new Point(0, 0), StructureDropIndicatorCanvas);
                top = Math.Clamp(targetPoint.Y, 0d, Math.Max(0d, StructureDropIndicatorCanvas.ActualHeight - TreeItemHeaderHeight));
                StructureDropIndicator.Width = width;
                StructureDropIndicator.Height = TreeItemHeaderHeight;
                StructureDropIndicator.Background = Brushes.Transparent;
                StructureDropIndicator.BorderThickness = new Thickness(2);
                StructureDropIndicator.SetResourceReference(Border.BorderBrushProperty, "PrimaryButtonBrush");
            }
            else
            {
                top = GetDropLineTop(targetItem, e, dropMode);
                StructureDropIndicator.Width = width;
                StructureDropIndicator.Height = 3;
                StructureDropIndicator.BorderThickness = new Thickness(0);
                StructureDropIndicator.SetResourceReference(Border.BackgroundProperty, "PrimaryButtonBrush");
                StructureDropIndicator.SetResourceReference(Border.BorderBrushProperty, "PrimaryButtonBrush");
            }

            Canvas.SetLeft(StructureDropIndicator, left);
            Canvas.SetTop(StructureDropIndicator, top);
            StructureDropIndicator.Visibility = Visibility.Visible;
        }

        private double GetDropLineTop(TreeViewItem? targetItem, DragEventArgs e, DataStructureFieldDropMode dropMode)
        {
            const double indicatorHeight = 3d;
            double rawTop;

            if (targetItem is null)
            {
                rawTop = e.GetPosition(StructureDropIndicatorCanvas).Y;
            }
            else
            {
                Point targetPoint = targetItem.TranslatePoint(new Point(0, 0), StructureDropIndicatorCanvas);
                rawTop = dropMode == DataStructureFieldDropMode.After
                    ? targetPoint.Y + TreeItemHeaderHeight
                    : targetPoint.Y;
            }

            return Math.Clamp(
                rawTop - indicatorHeight / 2,
                0d,
                Math.Max(0d, StructureDropIndicatorCanvas.ActualHeight - indicatorHeight));
        }

        private void HideDropIndicator()
        {
            if (StructureDropIndicator is not null)
            {
                StructureDropIndicator.Visibility = Visibility.Collapsed;
            }
        }

        private static TreeViewItem? GetTreeViewItemFromSource(DependencyObject? source)
        {
            while (source is not null)
            {
                if (source is TreeViewItem item)
                {
                    return item;
                }

                source = VisualTreeHelper.GetParent(source);
            }

            return null;
        }

        #endregion

        #region 抽屉动画

        /// <summary>
        /// 根据 ViewModel 的抽屉状态播放透明度和位移动画。
        /// </summary>
        private void UpdateCommandDrawerVisual(bool animate)
        {
            if (DataStructureDrawerHost is null || DataStructureDrawerTranslateTransform is null)
            {
                return;
            }

            bool isOpen = ViewModel?.IsStructureDrawerOpen == true;
            double targetOpacity = isOpen ? 1d : 0d;
            double targetOffset = isOpen ? 0d : StructureDrawerClosedOffset;

            if (isOpen)
            {
                DataStructureDrawerHost.IsHitTestVisible = true;
            }

            if (!animate)
            {
                DataStructureDrawerHost.BeginAnimation(UIElement.OpacityProperty, null);
                DataStructureDrawerTranslateTransform.BeginAnimation(TranslateTransform.YProperty, null);
                DataStructureDrawerHost.Opacity = targetOpacity;
                DataStructureDrawerTranslateTransform.Y = targetOffset;
                DataStructureDrawerHost.IsHitTestVisible = isOpen;
                return;
            }

            DoubleAnimation opacityAnimation = new()
            {
                To = targetOpacity,
                Duration = StructureDrawerAnimationDuration,
                EasingFunction = StructureDrawerEasing
            };

            if (!isOpen)
            {
                opacityAnimation.Completed += (_, _) =>
                {
                    if (ViewModel?.IsStructureDrawerOpen != true)
                    {
                        DataStructureDrawerHost.IsHitTestVisible = false;
                    }
                };
            }

            DoubleAnimation translateAnimation = new()
            {
                To = targetOffset,
                Duration = StructureDrawerAnimationDuration,
                EasingFunction = StructureDrawerEasing
            };

            DataStructureDrawerHost.BeginAnimation(UIElement.OpacityProperty, opacityAnimation);
            DataStructureDrawerTranslateTransform.BeginAnimation(TranslateTransform.YProperty, translateAnimation);
        }

        #endregion
    }
}
