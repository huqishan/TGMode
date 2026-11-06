using Module.Business.Features.OperationEditing.ViewModels;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Module.Business.Features.OperationEditing.Views;

/// <summary>
/// 操作编辑抽屉，统一封装遮罩、标题、命令和开关动画。
/// </summary>
public partial class OperationEditorDrawerView : UserControl
{
    #region 动画配置

    /// <summary>抽屉收起时的位移偏移量。</summary>
    private const double ClosedOffset = 56d;

    /// <summary>开关动画时长。</summary>
    private static readonly Duration AnimationDuration = new(TimeSpan.FromMilliseconds(220));

    /// <summary>开关动画缓动函数。</summary>
    private static readonly IEasingFunction Easing = new CubicEase { EasingMode = EasingMode.EaseOut };

    #endregion

    #region 依赖属性

    /// <summary>IsOpen 依赖属性定义，变化时触发开关动画。</summary>
    public static readonly DependencyProperty IsOpenProperty = DependencyProperty.Register(
        nameof(IsOpen), typeof(bool), typeof(OperationEditorDrawerView),
        new PropertyMetadata(false, OnIsOpenChanged));

    /// <summary>Title 依赖属性定义。</summary>
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(OperationEditorDrawerView), new PropertyMetadata(string.Empty));

    /// <summary>BtnTitle 依赖属性定义（抽屉底部按钮文案）。</summary>
    public static readonly DependencyProperty BtnTitleProperty = DependencyProperty.Register(
        nameof(BtnTitle), typeof(string), typeof(OperationEditorDrawerView), new PropertyMetadata(string.Empty));

    /// <summary>HostStepName 依赖属性定义（宿主业务名称，用于标题展示）。</summary>
    public static readonly DependencyProperty HostStepNameProperty = DependencyProperty.Register(
        nameof(HostStepName), typeof(string), typeof(OperationEditorDrawerView), new PropertyMetadata(string.Empty));

    /// <summary>Editor 依赖属性定义（绑定的操作编辑 ViewModel）。</summary>
    public static readonly DependencyProperty EditorProperty = DependencyProperty.Register(
        nameof(Editor), typeof(OperationEditorViewModel), typeof(OperationEditorDrawerView));

    /// <summary>SaveCommand 依赖属性定义（保存命令）。</summary>
    public static readonly DependencyProperty SaveCommandProperty = DependencyProperty.Register(
        nameof(SaveCommand), typeof(ICommand), typeof(OperationEditorDrawerView));

    /// <summary>CloseCommand 依赖属性定义（关闭命令）。</summary>
    public static readonly DependencyProperty CloseCommandProperty = DependencyProperty.Register(
        nameof(CloseCommand), typeof(ICommand), typeof(OperationEditorDrawerView));

    /// <summary>
    /// 是否从宿主区域右侧展开；默认值为 false，以保留方案配置页面原有的左侧抽屉行为。
    /// </summary>
    public static readonly DependencyProperty IsRightAlignedProperty = DependencyProperty.Register(
        nameof(IsRightAligned), typeof(bool), typeof(OperationEditorDrawerView),
        new PropertyMetadata(false, OnIsRightAlignedChanged));

    /// <summary>抽屉是否展开。</summary>
    public bool IsOpen { get => (bool)GetValue(IsOpenProperty); set => SetValue(IsOpenProperty, value); }

    /// <summary>抽屉标题。</summary>
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }

    /// <summary>抽屉底部按钮文案。</summary>
    public string BtnTitle { get => (string)GetValue(BtnTitleProperty); set => SetValue(BtnTitleProperty, value); }

    /// <summary>宿主业务名称，用于标题展示。</summary>
    public string HostStepName { get => (string)GetValue(HostStepNameProperty); set => SetValue(HostStepNameProperty, value); }

    /// <summary>绑定的操作编辑 ViewModel。</summary>
    public OperationEditorViewModel? Editor { get => (OperationEditorViewModel?)GetValue(EditorProperty); set => SetValue(EditorProperty, value); }

    /// <summary>保存命令。</summary>
    public ICommand? SaveCommand { get => (ICommand?)GetValue(SaveCommandProperty); set => SetValue(SaveCommandProperty, value); }

    /// <summary>关闭命令。</summary>
    public ICommand? CloseCommand { get => (ICommand?)GetValue(CloseCommandProperty); set => SetValue(CloseCommandProperty, value); }

    /// <summary>是否从宿主区域右侧展开。</summary>
    public bool IsRightAligned { get => (bool)GetValue(IsRightAlignedProperty); set => SetValue(IsRightAlignedProperty, value); }

    #endregion

    #region 构造与交互

    /// <summary>构造函数，加载完成后按当前状态无动画地刷新一次视觉。</summary>
    public OperationEditorDrawerView()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateVisual(animate: false);
    }

    /// <summary>IsOpen 变化回调：已加载时播放开关动画。</summary>
    private static void OnIsOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is OperationEditorDrawerView drawer && drawer.IsLoaded)
        {
            drawer.UpdateVisual(animate: true);
        }
    }

    /// <summary>对齐方式变化回调：切换抽屉宿主的水平对齐并刷新视觉。</summary>
    private static void OnIsRightAlignedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not OperationEditorDrawerView drawer)
        {
            return;
        }

        drawer.DrawerHost.HorizontalAlignment = drawer.IsRightAligned
            ? HorizontalAlignment.Right
            : HorizontalAlignment.Left;

        if (drawer.IsLoaded)
        {
            drawer.UpdateVisual(animate: false);
        }
    }

    /// <summary>点击遮罩层：执行关闭命令。</summary>
    private void DrawerBackdrop_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (CloseCommand?.CanExecute(null) == true)
        {
            CloseCommand.Execute(null);
        }
    }

    #endregion

    #region 动画

    /// <summary>
    /// 刷新抽屉视觉状态：更新透明度与位移；animate 为 false 时直接赋值，否则播放动画。
    /// </summary>
    /// <param name="animate">是否播放开关动画。</param>
    private void UpdateVisual(bool animate)
    {
        double targetOpacity = IsOpen ? 1d : 0d;
        double targetOffset = IsOpen ? 0d : IsRightAligned ? ClosedOffset : -ClosedOffset;
        if (IsOpen)
        {
            DrawerHost.IsHitTestVisible = true;
        }

        if (!animate)
        {
            DrawerHost.Opacity = targetOpacity;
            DrawerTranslateTransform.X = targetOffset;
            DrawerHost.IsHitTestVisible = IsOpen;
            return;
        }

        DoubleAnimation opacityAnimation = new(targetOpacity, AnimationDuration) { EasingFunction = Easing };
        if (!IsOpen)
        {
            opacityAnimation.Completed += (_, _) => DrawerHost.IsHitTestVisible = IsOpen;
        }

        DrawerHost.BeginAnimation(OpacityProperty, opacityAnimation);
        DrawerTranslateTransform.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(targetOffset, AnimationDuration) { EasingFunction = Easing });
    }

    #endregion
}
