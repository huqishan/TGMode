using ControlLibrary.Controls.Navigation.Models;
using Module.User.Models;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace Module.User.Services;

/// <summary>
/// 界面权限运行时服务，负责把已保存的角色权限应用到导航、页面和按钮控件。
/// </summary>
public static class UiPermissionRuntime
{
    #region 权限运行时字段

    private const string PermissionConfigurationViewTypeName =
        "Module.User.Views.PermissionConfigurationView";

    private static readonly object OriginalVisibilityNotCaptured = new();
    private static readonly object OriginalIsEnabledNotCaptured = new();

    private static readonly DependencyProperty IsVisibilityAllowedProperty =
        DependencyProperty.RegisterAttached(
            "IsVisibilityAllowed",
            typeof(bool),
            typeof(UiPermissionRuntime),
            new PropertyMetadata(true));

    private static readonly DependencyProperty IsEnabledAllowedProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabledAllowed",
            typeof(bool),
            typeof(UiPermissionRuntime),
            new PropertyMetadata(true));

    private static readonly DependencyProperty IsPermissionHookedProperty =
        DependencyProperty.RegisterAttached(
            "IsPermissionHooked",
            typeof(bool),
            typeof(UiPermissionRuntime),
            new PropertyMetadata(false));

    private static readonly DependencyProperty OriginalVisibilityProperty =
        DependencyProperty.RegisterAttached(
            "OriginalVisibility",
            typeof(object),
            typeof(UiPermissionRuntime),
            new PropertyMetadata(OriginalVisibilityNotCaptured));

    private static readonly DependencyProperty OriginalIsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "OriginalIsEnabled",
            typeof(object),
            typeof(UiPermissionRuntime),
            new PropertyMetadata(OriginalIsEnabledNotCaptured));

    private static UiPermissionCatalog? _catalog;
    private static string? _cachedRoleId;
    private static Dictionary<string, UiPermissionElementSetting>? _cachedRoleMap;

    #endregion

    #region 对外应用入口

    /// <summary>
    /// 清理运行时缓存，下次应用权限时重新读取配置。
    /// </summary>
    public static void RefreshCache()
    {
        _catalog = null;
        _cachedRoleId = null;
        _cachedRoleMap = null;
    }

    /// <summary>
    /// 根据当前用户角色过滤主导航菜单。
    /// </summary>
    public static void ApplyToNavigation(IEnumerable<ControlInfoDataItem> items)
    {
        if (ShouldBypassPermissions())
        {
            return;
        }

        foreach (ControlInfoDataItem item in items)
        {
            ApplyToNavigationItem(item);
        }
    }

    /// <summary>
    /// 在页面加载完成后延迟应用界面权限，避免视觉树尚未生成。
    /// </summary>
    public static void Attach(FrameworkElement element)
    {
        if (ShouldBypassPermissions() || ShouldBypassPermissionConfiguration(element))
        {
            return;
        }

        if (element.IsLoaded)
        {
            QueueApplyToElement(element);
        }

        element.Loaded += (_, _) =>
        {
            QueueApplyToElement(element);
        };
    }

    /// <summary>
    /// 对已加载的页面根节点应用当前角色权限。
    /// </summary>
    public static void ApplyToElement(FrameworkElement root)
    {
        if (ShouldBypassPermissions() || ShouldBypassPermissionConfiguration(root))
        {
            return;
        }

        string? rootScopeTypeName = root.GetType().FullName;
        HashSet<DependencyObject> visited = new(ReferenceEqualityComparer.Instance);
        Dictionary<string, Dictionary<string, int>> buttonOccurrencesByScope = new(StringComparer.Ordinal);
        ApplyToSubtree(root, root, rootScopeTypeName, visited, buttonOccurrencesByScope);
    }

    #endregion

    #region 权限应用遍历

    private static void QueueApplyToElement(FrameworkElement element)
    {
        element.Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() => ApplyToElement(element)));
    }

    private static void ApplyToSubtree(
        DependencyObject dependencyObject,
        FrameworkElement root,
        string? currentScopeTypeName,
        ISet<DependencyObject> visited,
        IDictionary<string, Dictionary<string, int>> buttonOccurrencesByScope)
    {
        if (!visited.Add(dependencyObject))
        {
            return;
        }

        string? nextScopeTypeName = currentScopeTypeName;
        if (dependencyObject is FrameworkElement element)
        {
            string? elementTypeName = element.GetType().FullName;
            if (!string.IsNullOrWhiteSpace(elementTypeName) &&
                (ReferenceEquals(element, root) || IsRuntimeScopeElement(element)))
            {
                nextScopeTypeName = elementTypeName;
                ApplySetting(element, ResolveSetting(UiPermissionKeys.Page(elementTypeName)));
            }

            if (!string.IsNullOrWhiteSpace(nextScopeTypeName))
            {
                if (IsDialogElement(element))
                {
                    ApplySetting(
                        element,
                        ResolveSetting(UiPermissionKeys.Dialog(nextScopeTypeName, element.Name)));
                }

                if (element is ButtonBase button && ShouldApplyButtonPermission(button))
                {
                    string? identifier = GetButtonIdentifier(button, nextScopeTypeName, buttonOccurrencesByScope);
                    if (!string.IsNullOrWhiteSpace(identifier))
                    {
                        ApplySetting(
                            button,
                            ResolveSetting(UiPermissionKeys.Button(nextScopeTypeName, identifier)));
                    }
                }
            }
        }

        foreach (DependencyObject child in EnumerateChildren(dependencyObject))
        {
            ApplyToSubtree(child, root, nextScopeTypeName, visited, buttonOccurrencesByScope);
        }
    }

    private static void ApplyToNavigationItem(ControlInfoDataItem item)
    {
        foreach (ControlInfoDataItem child in item.Items)
        {
            ApplyToNavigationItem(child);
        }

        UiPermissionResolvedSetting setting = ResolveNavigationSetting(item);
        if (item.Items.Count > 0)
        {
            item.IsVisibility = setting.IsVisible && item.Items.Any(child => child.IsVisibility);
            item.IsEnable = setting.IsEnabled && item.Items.Any(child => child.IsVisibility && child.IsEnable);
            return;
        }

        item.IsVisibility = setting.IsVisible;
        item.IsEnable = setting.IsEnabled;
    }

    #endregion

    #region 权限配置解析

    private static UiPermissionResolvedSetting ResolveNavigationSetting(ControlInfoDataItem item)
    {
        string? typeName = ParseContentTypeName(item.Content);
        if (string.Equals(typeName, PermissionConfigurationViewTypeName, StringComparison.Ordinal))
        {
            return AccountPermissionDisplay.CanConfigureUiPermissions(CurrentUserSession.Current)
                ? new UiPermissionResolvedSetting(true, true)
                : new UiPermissionResolvedSetting(false, false);
        }

        return string.IsNullOrWhiteSpace(typeName)
            ? new UiPermissionResolvedSetting(true, true)
            : ResolveSetting(UiPermissionKeys.Page(typeName));
    }

    private static UiPermissionResolvedSetting ResolveSetting(string key)
    {
        Dictionary<string, UiPermissionElementSetting> map = GetCurrentRoleMap();
        if (!map.TryGetValue(key, out UiPermissionElementSetting? setting))
        {
            return new UiPermissionResolvedSetting(false, false);
        }

        return new UiPermissionResolvedSetting(setting.IsVisible, setting.IsEnabled);
    }

    private static Dictionary<string, UiPermissionElementSetting> GetCurrentRoleMap()
    {
        AuthenticatedUser? user = CurrentUserSession.Current;
        string roleId = user?.PermissionId ?? string.Empty;
        if (string.Equals(_cachedRoleId, roleId, StringComparison.Ordinal) && _cachedRoleMap is not null)
        {
            return _cachedRoleMap;
        }

        _catalog ??= UiPermissionConfigurationStore.LoadCatalog();
        _cachedRoleId = roleId;
        _cachedRoleMap = UiPermissionConfigurationStore.GetRoleSettingMap(_catalog, roleId);
        return _cachedRoleMap;
    }

    #endregion

    #region 控件状态应用

    private static void ApplySetting(FrameworkElement element, UiPermissionResolvedSetting setting)
    {
        CaptureOriginalValues(element);
        EnsurePermissionHooks(element);

        object originalVisibility = element.GetValue(OriginalVisibilityProperty);
        object originalIsEnabled = element.GetValue(OriginalIsEnabledProperty);

        element.SetValue(IsVisibilityAllowedProperty, setting.IsVisible);
        element.SetValue(IsEnabledAllowedProperty, setting.IsEnabled);

        element.Visibility = setting.IsVisible && originalVisibility is Visibility visibility
            ? visibility
            : Visibility.Collapsed;

        if (element is UIElement uiElement)
        {
            uiElement.IsEnabled = setting.IsEnabled && originalIsEnabled is bool isEnabled && isEnabled;
        }
    }

    private static void EnsurePermissionHooks(FrameworkElement element)
    {
        if ((bool)element.GetValue(IsPermissionHookedProperty))
        {
            return;
        }

        element.SetValue(IsPermissionHookedProperty, true);
        element.IsVisibleChanged += PermissionElement_IsVisibleChanged;

        if (element is UIElement uiElement)
        {
            uiElement.IsEnabledChanged += PermissionElement_IsEnabledChanged;
        }
    }

    private static void PermissionElement_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is FrameworkElement element &&
            !(bool)element.GetValue(IsVisibilityAllowedProperty) &&
            element.Visibility != Visibility.Collapsed)
        {
            element.Visibility = Visibility.Collapsed;
        }
    }

    private static void PermissionElement_IsEnabledChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is UIElement element &&
            !(bool)element.GetValue(IsEnabledAllowedProperty) &&
            element.IsEnabled)
        {
            element.IsEnabled = false;
        }
    }

    private static void CaptureOriginalValues(FrameworkElement element)
    {
        if (ReferenceEquals(element.GetValue(OriginalVisibilityProperty), OriginalVisibilityNotCaptured))
        {
            element.SetValue(OriginalVisibilityProperty, element.Visibility);
        }

        if (ReferenceEquals(element.GetValue(OriginalIsEnabledProperty), OriginalIsEnabledNotCaptured) &&
            element is UIElement uiElement)
        {
            element.SetValue(OriginalIsEnabledProperty, uiElement.IsEnabled);
        }
    }

    #endregion

    #region 视觉树与元素识别

    private static IEnumerable<DependencyObject> EnumerateChildren(DependencyObject root)
    {
        if (root is Popup popup && popup.Child is not null)
        {
            yield return popup.Child;
        }

        int visualChildren = 0;
        try
        {
            visualChildren = VisualTreeHelper.GetChildrenCount(root);
        }
        catch
        {
            visualChildren = 0;
        }

        for (int i = 0; i < visualChildren; i++)
        {
            yield return VisualTreeHelper.GetChild(root, i);
        }

        foreach (object logicalChild in LogicalTreeHelper.GetChildren(root))
        {
            if (logicalChild is DependencyObject dependencyChild)
            {
                yield return dependencyChild;
            }
        }
    }

    private static bool IsRuntimeScopeElement(FrameworkElement element)
    {
        return element is UserControl or Window;
    }

    private static bool IsDialogElement(FrameworkElement element)
    {
        if (element is Popup)
        {
            return true;
        }

        string name = element.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        if (name.Contains("Backdrop", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("TranslateTransform", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("Sheet", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return name.EndsWith("DrawerHost", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith("DialogHost", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith("PopupHost", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Flyout", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Modal", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldApplyButtonPermission(ButtonBase button)
    {
        if (ContainsBackdropMarker(button.Name) ||
            ContainsBackdropMarker(button.Style?.ToString()))
        {
            return false;
        }

        return FirstMeaningfulValue(
                   button.Name,
                   ExtractText(button.Content),
                   ExtractText(button.ToolTip)) is not null;
    }

    private static bool ContainsBackdropMarker(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
               value.Contains("Backdrop", StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region 按钮标识与文本提取

    private static string? GetButtonIdentifier(
        ButtonBase button,
        string scopeTypeName,
        IDictionary<string, Dictionary<string, int>> buttonOccurrencesByScope)
    {
        string? explicitName = FirstMeaningfulValue(button.Name);
        if (!string.IsNullOrWhiteSpace(explicitName))
        {
            return explicitName;
        }

        string? baseIdentifier = FirstMeaningfulValue(
            ExtractText(button.Content),
            ExtractText(button.ToolTip));

        if (string.IsNullOrWhiteSpace(baseIdentifier))
        {
            return null;
        }

        if (!buttonOccurrencesByScope.TryGetValue(scopeTypeName, out Dictionary<string, int>? buttonOccurrences))
        {
            buttonOccurrences = new Dictionary<string, int>(StringComparer.Ordinal);
            buttonOccurrencesByScope[scopeTypeName] = buttonOccurrences;
        }

        string normalizedIdentifier = UiPermissionKeys.NormalizeIdentifier(baseIdentifier);
        buttonOccurrences.TryGetValue(normalizedIdentifier, out int occurrence);
        occurrence++;
        buttonOccurrences[normalizedIdentifier] = occurrence;

        return occurrence == 1
            ? baseIdentifier
            : $"{baseIdentifier}#{occurrence}";
    }

    private static string? ExtractText(object? value)
    {
        return value switch
        {
            null => null,
            string text => text,
            TextBlock textBlock => textBlock.Text,
            AccessText accessText => accessText.Text,
            ContentControl contentControl => ExtractText(contentControl.Content),
            Panel panel => FirstMeaningfulValue(panel.Children.OfType<object>().Select(ExtractText).ToArray()),
            _ => value.ToString()
        };
    }

    private static string? FirstMeaningfulValue(params string?[] values)
    {
        return values
            .Select(value => value?.Trim())
            .FirstOrDefault(IsMeaningfulDisplayValue);
    }

    private static bool IsMeaningfulDisplayValue(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
               !string.Equals(value, "...", StringComparison.Ordinal) &&
               !string.Equals(value, "…", StringComparison.Ordinal);
    }

    private static string? ParseContentTypeName(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        string typeName = content.Split(',')[0].Trim();
        return typeName.Contains('.', StringComparison.Ordinal) ? typeName : null;
    }

    #endregion

    #region 权限绕过规则

    private static bool ShouldBypassPermissions()
    {
        return CurrentUserSession.Current?.IsBuiltIn == true;
    }

    private static bool ShouldBypassPermissionConfiguration(FrameworkElement element)
    {
        return AccountPermissionDisplay.CanConfigureUiPermissions(CurrentUserSession.Current) &&
               string.Equals(element.GetType().FullName, PermissionConfigurationViewTypeName, StringComparison.Ordinal);
    }

    #endregion
}
