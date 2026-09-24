using System;

namespace ControlLibrary.Controls.Loading;

/// <summary>
/// 提供覆盖主窗口的全局加载状态，业务界面只负责显示和关闭，不需要了解遮罩控件所在位置。
/// </summary>
public static class LoadingOverlayService
{
    #region 私有字段

    private static int _requestCount;
    private static string _message = "正在加载，请稍候…";

    #endregion

    #region 状态与事件

    /// <summary>当前是否存在尚未结束的加载请求。</summary>
    public static bool IsVisible => _requestCount > 0;

    /// <summary>遮罩中显示的提示文字。</summary>
    public static string Message => _message;

    /// <summary>全局加载状态发生变化时通知主窗口遮罩。</summary>
    public static event EventHandler? StateChanged;

    #endregion

    #region 显示与关闭

    /// <summary>
    /// 显示全局加载遮罩。支持嵌套调用，只有所有调用都完成关闭后遮罩才会消失。
    /// </summary>
    public static void Show(string? message = null)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            _message = message.Trim();
        }

        _requestCount++;
        StateChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>结束一次加载请求，并在没有剩余请求时关闭全局遮罩。</summary>
    public static void Close()
    {
        if (_requestCount > 0)
        {
            _requestCount--;
        }

        StateChanged?.Invoke(null, EventArgs.Empty);
    }

    #endregion
}
