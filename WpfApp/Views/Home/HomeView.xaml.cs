using System;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
    #region 初始化

    /// <summary>初始化设计器使用的首页视图。</summary>
    public HomeView()
    {
        InitializeComponent();
        //DeviceStatusPopup.CustomPopupPlacementCallback = DeviceStatusPopup_CustomPopupPlacement;
    }

    /// <summary>初始化首页视图并绑定生产总览 ViewModel。</summary>
    public HomeView(HomeViewModel viewModel)
    {
        InitializeComponent();
        //DeviceStatusPopup.CustomPopupPlacementCallback = DeviceStatusPopup_CustomPopupPlacement;
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }

    #endregion

    #region 设备状态弹框动画

    /// <summary>
    /// 将设备状态弹框右对齐，并让底边贴合首页视口底边。
    /// </summary>
    private static CustomPopupPlacement[] DeviceStatusPopup_CustomPopupPlacement(
        Size popupSize,
        Size targetSize,
        Point offset)
    {
        Point location = new(
            Math.Max(16, targetSize.Width - popupSize.Width - 16),
            Math.Max(0, targetSize.Height - popupSize.Height));

        return [new CustomPopupPlacement(location, PopupPrimaryAxis.Vertical)];
    }

    /// <summary>
    /// 弹框打开后从首页底边之外向上滑入可视区域。
    /// </summary>
    private void DeviceStatusPopup_Opened(object? sender, EventArgs e)
    {
        if (DeviceStatusPopupPanel.RenderTransform is not TranslateTransform transform)
        {
            return;
        }

        transform.BeginAnimation(TranslateTransform.YProperty, null);
        transform.Y = DeviceStatusPopupPanel.ActualHeight;
        transform.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(
                DeviceStatusPopupPanel.ActualHeight,
                0,
                TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
    }

    #endregion
}
