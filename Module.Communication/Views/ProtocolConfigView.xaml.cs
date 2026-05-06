using Module.Communication.Models;
using Module.Communication.ViewModels;
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Module.Communication.Views
{
    /// <summary>
    /// 协议配置界面的界面交互代码，仅保留必要的抽屉动画与生命周期桥接。
    /// </summary>
    public partial class ProtocolConfigView : UserControl
    {
        #region 抽屉动画字段
        private const double CommandDrawerClosedOffset = 56d;
        private static readonly Duration CommandDrawerAnimationDuration = new(TimeSpan.FromMilliseconds(220));
        private static readonly IEasingFunction CommandDrawerEasing = new CubicEase { EasingMode = EasingMode.EaseOut };

        #endregion

        #region 构造与生命周期
        public ProtocolConfigView()
        {
            InitializeComponent();

            if (ViewModel is not null)
            {
                ViewModel.PropertyChanged += ViewModel_PropertyChanged;
            }

            Loaded += ProtocolConfigView_Loaded;
            Unloaded += ProtocolConfigView_Unloaded;
        }

        private ProtocolConfigViewModel? ViewModel => DataContext as ProtocolConfigViewModel;

        private void ProtocolConfigView_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateCommandDrawerVisual(animate: false);
        }

        private void ProtocolConfigView_Unloaded(object sender, RoutedEventArgs e)
        {
            if (ViewModel is not null)
            {
                ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
                ViewModel.OnViewUnloaded();
            }
        }

        #endregion

        #region 纯界面交互
        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ProtocolConfigViewModel.IsCommandDrawerOpen))
            {
                UpdateCommandDrawerVisual(animate: true);
            }
        }

        private void CommandListBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is not ListBox listBox)
            {
                return;
            }

            ListBoxItem? clickedItem = ItemsControl.ContainerFromElement(
                listBox,
                e.OriginalSource as DependencyObject) as ListBoxItem;

            if (clickedItem?.DataContext is not ProtocolCommandConfig)
            {
                return;
            }

            ViewModel?.OpenCommandDrawer();
        }

        private void CommandDrawerBackdrop_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            ViewModel?.CloseCommandDrawer();
        }

        #endregion

        #region 抽屉动画方法
        private void UpdateCommandDrawerVisual(bool animate)
        {
            if (CommandDrawerHost is null || CommandDrawerTranslateTransform is null)
            {
                return;
            }

            bool isOpen = ViewModel?.IsCommandDrawerOpen == true;
            double targetOpacity = isOpen ? 1d : 0d;
            double targetOffset = isOpen ? 0d : CommandDrawerClosedOffset;

            if (isOpen)
            {
                CommandDrawerHost.IsHitTestVisible = true;
            }

            if (!animate)
            {
                CommandDrawerHost.BeginAnimation(UIElement.OpacityProperty, null);
                CommandDrawerTranslateTransform.BeginAnimation(TranslateTransform.YProperty, null);
                CommandDrawerHost.Opacity = targetOpacity;
                CommandDrawerTranslateTransform.Y = targetOffset;
                CommandDrawerHost.IsHitTestVisible = isOpen;
                return;
            }

            DoubleAnimation opacityAnimation = new()
            {
                To = targetOpacity,
                Duration = CommandDrawerAnimationDuration,
                EasingFunction = CommandDrawerEasing
            };

            if (!isOpen)
            {
                opacityAnimation.Completed += (_, _) =>
                {
                    if (ViewModel?.IsCommandDrawerOpen != true)
                    {
                        CommandDrawerHost.IsHitTestVisible = false;
                    }
                };
            }

            DoubleAnimation translateAnimation = new()
            {
                To = targetOffset,
                Duration = CommandDrawerAnimationDuration,
                EasingFunction = CommandDrawerEasing
            };

            CommandDrawerHost.BeginAnimation(UIElement.OpacityProperty, opacityAnimation);
            CommandDrawerTranslateTransform.BeginAnimation(TranslateTransform.YProperty, translateAnimation);
        }

        #endregion
    }
}
