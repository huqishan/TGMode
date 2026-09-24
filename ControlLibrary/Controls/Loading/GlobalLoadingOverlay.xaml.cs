using System;
using System.Windows;
using System.Windows.Controls;

namespace ControlLibrary.Controls.Loading;

/// <summary>
/// 全局加载等待遮罩。控件应放在主窗口最外层布局的最后，以覆盖整个软件界面。
/// </summary>
public partial class GlobalLoadingOverlay : UserControl
{
    #region 构造与事件

    public GlobalLoadingOverlay()
    {
        InitializeComponent();
        Loaded += GlobalLoadingOverlay_Loaded;
        Unloaded += GlobalLoadingOverlay_Unloaded;
    }

    /// <summary>控件加载后订阅全局状态，并立即同步当前显示状态。</summary>
    private void GlobalLoadingOverlay_Loaded(object sender, RoutedEventArgs e)
    {
        LoadingOverlayService.StateChanged -= LoadingOverlayService_StateChanged;
        LoadingOverlayService.StateChanged += LoadingOverlayService_StateChanged;
        UpdateState();
    }

    /// <summary>控件移出可视树时取消订阅，避免主窗口被静态事件长期引用。</summary>
    private void GlobalLoadingOverlay_Unloaded(object sender, RoutedEventArgs e)
    {
        LoadingOverlayService.StateChanged -= LoadingOverlayService_StateChanged;
    }

    /// <summary>在控件所属调度器中刷新遮罩，兼容后台任务发出的状态通知。</summary>
    private void LoadingOverlayService_StateChanged(object? sender, EventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(UpdateState);
            return;
        }

        UpdateState();
    }

    /// <summary>根据全局加载状态更新提示文字、可见性和界面输入拦截。</summary>
    private void UpdateState()
    {
        MessageText.Text = LoadingOverlayService.Message;
        Visibility = LoadingOverlayService.IsVisible ? Visibility.Visible : Visibility.Collapsed;
        IsHitTestVisible = LoadingOverlayService.IsVisible;
    }

    #endregion
}
