using Module.Business.Features.Scheme.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Module.Business.Features.Scheme.Views;

/// <summary>
/// 方案判断条件右侧抽屉，负责遮罩交互和侧滑动画，条件业务状态由独立 ViewModel 管理。
/// </summary>
public partial class SchemeConditionEditorDrawerView : UserControl
{
    #region 动画配置与订阅

    private const double ClosedOffset = 56d;
    private static readonly Duration AnimationDuration = new(TimeSpan.FromMilliseconds(220));
    private static readonly IEasingFunction Easing = new CubicEase { EasingMode = EasingMode.EaseOut };
    private SchemeConditionEditorViewModel? _subscribedEditor;

    #endregion

    #region 依赖属性

    public static readonly DependencyProperty EditorProperty = DependencyProperty.Register(
        nameof(Editor),
        typeof(SchemeConditionEditorViewModel),
        typeof(SchemeConditionEditorDrawerView),
        new PropertyMetadata(null, OnEditorChanged));

    public SchemeConditionEditorViewModel? Editor
    {
        get => (SchemeConditionEditorViewModel?)GetValue(EditorProperty);
        set => SetValue(EditorProperty, value);
    }

    #endregion

    #region 构造与交互

    public SchemeConditionEditorDrawerView()
    {
        InitializeComponent();
        Loaded += SchemeConditionEditorDrawerView_Loaded;
        Unloaded += SchemeConditionEditorDrawerView_Unloaded;
    }

    private static void OnEditorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not SchemeConditionEditorDrawerView drawer)
        {
            return;
        }

        drawer.DetachEditor(e.OldValue as SchemeConditionEditorViewModel);
        drawer.AttachEditor(e.NewValue as SchemeConditionEditorViewModel);
        if (drawer.IsLoaded)
        {
            drawer.UpdateVisual(animate: false);
        }
    }

    private void SchemeConditionEditorDrawerView_Loaded(object sender, RoutedEventArgs e)
    {
        AttachEditor(Editor);
        UpdateVisual(animate: false);
    }

    private void SchemeConditionEditorDrawerView_Unloaded(object sender, RoutedEventArgs e)
    {
        DetachEditor(_subscribedEditor);
    }

    private void DrawerBackdrop_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (Editor?.CloseCommand.CanExecute(null) == true)
        {
            Editor.CloseCommand.Execute(null);
        }
    }

    #endregion

    #region 状态订阅与动画

    private void AttachEditor(SchemeConditionEditorViewModel? editor)
    {
        if (editor is null || ReferenceEquals(_subscribedEditor, editor))
        {
            return;
        }

        DetachEditor(_subscribedEditor);
        _subscribedEditor = editor;
        _subscribedEditor.PropertyChanged += Editor_PropertyChanged;
    }

    private void DetachEditor(SchemeConditionEditorViewModel? editor)
    {
        if (editor is null)
        {
            return;
        }

        editor.PropertyChanged -= Editor_PropertyChanged;
        if (ReferenceEquals(_subscribedEditor, editor))
        {
            _subscribedEditor = null;
        }
    }

    private void Editor_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SchemeConditionEditorViewModel.IsOpen))
        {
            UpdateVisual(animate: true);
        }
    }

    private void UpdateVisual(bool animate)
    {
        bool isOpen = Editor?.IsOpen == true;
        double targetOpacity = isOpen ? 1d : 0d;
        double targetOffset = isOpen ? 0d : ClosedOffset;
        if (isOpen)
        {
            DrawerHost.Visibility = Visibility.Visible;
            DrawerHost.IsHitTestVisible = true;
        }
        else
        {
            // 关闭开始后立即停止接收输入，避免动画期间重复触发关闭命令。
            DrawerHost.IsHitTestVisible = false;
        }

        if (!animate)
        {
            DrawerBackdrop.Opacity = targetOpacity;
            DrawerTranslateTransform.X = targetOffset;
            DrawerHost.IsHitTestVisible = isOpen;
            DrawerHost.Visibility = isOpen ? Visibility.Visible : Visibility.Collapsed;
            DrawerSheet.CacheMode = null;
            return;
        }

        // 参数表包含大量输入控件。动画期间缓存抽屉位图，只移动缓存结果，避免每一帧重新合成全部控件。
        double currentOpacity = DrawerBackdrop.Opacity;
        double currentOffset = DrawerTranslateTransform.X;
        DrawerSheet.CacheMode = new BitmapCache(1d);
        DrawerBackdrop.Opacity = targetOpacity;
        DrawerTranslateTransform.X = targetOffset;

        DoubleAnimation opacityAnimation = new(currentOpacity, targetOpacity, AnimationDuration)
        {
            EasingFunction = Easing,
            FillBehavior = FillBehavior.Stop
        };
        DoubleAnimation offsetAnimation = new(currentOffset, targetOffset, AnimationDuration)
        {
            EasingFunction = Easing,
            FillBehavior = FillBehavior.Stop
        };
        offsetAnimation.Completed += (_, _) =>
        {
            bool remainsOpen = Editor?.IsOpen == true;
            DrawerSheet.CacheMode = null;
            DrawerHost.IsHitTestVisible = remainsOpen;
            if (!remainsOpen)
            {
                // 动画结束后折叠复杂可视树，避免隐藏参数表继续参与父级布局和渲染。
                DrawerHost.Visibility = Visibility.Collapsed;
            }
        };

        DrawerBackdrop.BeginAnimation(OpacityProperty, opacityAnimation);
        DrawerTranslateTransform.BeginAnimation(
            TranslateTransform.XProperty,
            offsetAnimation);
    }

    #endregion
}
