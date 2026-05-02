using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Module.MES.Views
{
    /// <summary>
    /// 接口配置页视图；业务逻辑已迁移到 ApiConfigViewModel。
    /// </summary>
    public partial class ApiConfigView : UserControl
    {
        #region Lua 弹框字段

        private const double CommandDrawerClosedOffset = 56d;
        private static readonly Duration CommandDrawerAnimationDuration = new(TimeSpan.FromMilliseconds(220));
        private static readonly IEasingFunction CommandDrawerEasing = new CubicEase { EasingMode = EasingMode.EaseOut };
        private bool _isCommandDrawerOpen;

        #endregion

        #region 构造方法

        public ApiConfigView()
        {
            InitializeComponent();
        }

        #endregion

        #region Lua 弹框事件

        private void CloseLuaDrawerButton_Click(object sender, RoutedEventArgs e)
        {
            CloseCommandDrawer();
        }

        private void CommandDrawerBackdrop_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            CloseCommandDrawer();
        }

        private void LuaButton_Click(object sender, RoutedEventArgs e)
        {
            OpenCommandDrawer();
        }

        #endregion

        #region Lua 弹框动画

        /// <summary>
        /// 关闭 Lua 编辑弹框，并播放原有收起动画。
        /// </summary>
        private void CloseCommandDrawer()
        {
            _isCommandDrawerOpen = false;
            UpdateCommandDrawerVisual(animate: true);
        }

        /// <summary>
        /// 打开 Lua 编辑弹框，并播放原有展开动画。
        /// </summary>
        private void OpenCommandDrawer()
        {
            _isCommandDrawerOpen = true;
            UpdateCommandDrawerVisual(animate: true);
        }

        /// <summary>
        /// 使用原来的透明度和位移动画刷新 Lua 弹框显示状态。
        /// </summary>
        private void UpdateCommandDrawerVisual(bool animate)
        {
            if (CommandDrawerHost is null || CommandDrawerTranslateTransform is null)
            {
                return;
            }

            double targetOpacity = _isCommandDrawerOpen ? 1d : 0d;
            double targetOffset = _isCommandDrawerOpen ? 0d : CommandDrawerClosedOffset;

            if (_isCommandDrawerOpen)
            {
                CommandDrawerHost.IsHitTestVisible = true;
            }

            if (!animate)
            {
                CommandDrawerHost.BeginAnimation(UIElement.OpacityProperty, null);
                CommandDrawerTranslateTransform.BeginAnimation(TranslateTransform.YProperty, null);
                CommandDrawerHost.Opacity = targetOpacity;
                CommandDrawerTranslateTransform.Y = targetOffset;
                CommandDrawerHost.IsHitTestVisible = _isCommandDrawerOpen;
                return;
            }

            DoubleAnimation opacityAnimation = new()
            {
                To = targetOpacity,
                Duration = CommandDrawerAnimationDuration,
                EasingFunction = CommandDrawerEasing
            };

            if (!_isCommandDrawerOpen)
            {
                opacityAnimation.Completed += (_, _) =>
                {
                    if (!_isCommandDrawerOpen)
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
