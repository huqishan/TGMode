using System;
using System.ComponentModel;
using System.Windows.Controls;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WpfApp.ViewModels;

namespace WpfApp.Views.Home;

/// <summary>
/// 首页生产总览视图，负责将界面与首页 ViewModel 关联。
/// </summary>
public partial class HomeView : UserControl
{
    #region 设备状态抽屉动画配置

    /// <summary>抽屉关闭时相对打开位置向下偏移的距离。</summary>
    private const double DeviceDrawerClosedOffset = 56d;

    /// <summary>抽屉显示状态切换时使用的动画时长。</summary>
    private static readonly Duration DeviceDrawerAnimationDuration = new(TimeSpan.FromMilliseconds(220));

    /// <summary>抽屉滑动动画使用的缓动函数。</summary>
    private static readonly IEasingFunction DeviceDrawerEasing = new CubicEase { EasingMode = EasingMode.EaseOut };

    #endregion

    #region 初始化

    /// <summary>初始化设计器使用的首页视图。</summary>
    public HomeView()
    {
        InitializeComponent();
        Loaded += HomeView_Loaded;
        Unloaded += HomeView_Unloaded;
    }

    /// <summary>初始化首页视图并绑定生产总览 ViewModel。</summary>
    public HomeView(HomeViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        Loaded += HomeView_Loaded;
        Unloaded += HomeView_Unloaded;
    }

    #endregion

    #region 设备状态抽屉状态与动画

    /// <summary>
    /// 视图加载时订阅首页状态变化，并立即同步抽屉当前显示状态。
    /// </summary>
    private void HomeView_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is INotifyPropertyChanged viewModel)
        {
            viewModel.PropertyChanged -= HomeViewModel_PropertyChanged;
            viewModel.PropertyChanged += HomeViewModel_PropertyChanged;
        }

        UpdateDeviceStatusDrawerVisual(animate: false);
    }

    /// <summary>
    /// 视图卸载时取消首页状态订阅，避免离开页面后继续持有事件引用。
    /// </summary>
    private void HomeView_Unloaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is INotifyPropertyChanged viewModel)
        {
            viewModel.PropertyChanged -= HomeViewModel_PropertyChanged;
        }
    }

    /// <summary>
    /// 首页设备状态属性变化时，按 ViewModel 的开合状态更新抽屉动画。
    /// </summary>
    private void HomeViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HomeViewModel.IsDeviceStatusPopupOpen))
        {
            UpdateDeviceStatusDrawerVisual(animate: true);
        }
    }

    /// <summary>
    /// 按结构配置页面的抽屉模式更新遮罩交互、透明度和垂直滑动位置。
    /// </summary>
    private void UpdateDeviceStatusDrawerVisual(bool animate)
    {
        if (DeviceStatusDrawerHost is null || DeviceStatusDrawerTranslateTransform is null)
        {
            return;
        }

        bool isOpen = (DataContext as HomeViewModel)?.IsDeviceStatusPopupOpen == true;
        double targetOpacity = isOpen ? 1d : 0d;
        double targetOffset = isOpen ? 0d : DeviceDrawerClosedOffset;

        if (isOpen)
        {
            DeviceStatusDrawerHost.IsHitTestVisible = true;
        }

        if (!animate)
        {
            DeviceStatusDrawerHost.BeginAnimation(UIElement.OpacityProperty, null);
            DeviceStatusDrawerTranslateTransform.BeginAnimation(TranslateTransform.YProperty, null);
            DeviceStatusDrawerHost.Opacity = targetOpacity;
            DeviceStatusDrawerTranslateTransform.Y = targetOffset;
            DeviceStatusDrawerHost.IsHitTestVisible = isOpen;
            return;
        }

        DoubleAnimation opacityAnimation = new()
        {
            To = targetOpacity,
            Duration = DeviceDrawerAnimationDuration,
            EasingFunction = DeviceDrawerEasing
        };

        if (!isOpen)
        {
            opacityAnimation.Completed += (_, _) =>
            {
                if ((DataContext as HomeViewModel)?.IsDeviceStatusPopupOpen != true)
                {
                    DeviceStatusDrawerHost.IsHitTestVisible = false;
                }
            };
        }

        DoubleAnimation translateAnimation = new()
        {
            To = targetOffset,
            Duration = DeviceDrawerAnimationDuration,
            EasingFunction = DeviceDrawerEasing
        };

        DeviceStatusDrawerHost.BeginAnimation(UIElement.OpacityProperty, opacityAnimation);
        DeviceStatusDrawerTranslateTransform.BeginAnimation(TranslateTransform.YProperty, translateAnimation);
    }

    #endregion
}
