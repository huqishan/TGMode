using Module.MES.ViewModels;
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
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
        private static readonly Duration StructureDrawerAnimationDuration = new(TimeSpan.FromMilliseconds(220));
        private static readonly IEasingFunction StructureDrawerEasing = new CubicEase { EasingMode = EasingMode.EaseOut };

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
