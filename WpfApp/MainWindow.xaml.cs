using ControlLibrary;
using ControlLibrary.Controls.Navigation.Models;
using Module.User.Features.Authentication.Services;
using Module.User.Services;
using Shared.Infrastructure.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using WpfApp.Infrastructure;

namespace WpfApp
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public static RoutedUICommand CloseTabCommand { get; } =
            new RoutedUICommand("Close tab", nameof(CloseTabCommand), typeof(MainWindow));

        private const string SettingsTabKey = "__settings__";
        private const int WmNclButtonDown = 0x00A1;
        private const int WmGetMinMaxInfo = 0x0024;
        private const int HtCaption = 0x02;
        private const uint MonitorDefaultToNearest = 0x00000002;
        // The window keeps menu data and tab hosting only.
        private readonly List<ControlInfoDataItem> _navigationInfo = [];
        private readonly IViewFactory? _viewFactory;

        public MainWindow()
        {
            InitializeComponent();
            SourceInitialized += MainWindow_SourceInitialized;
        }

        public MainWindow(IViewFactory viewFactory)
        {
            _viewFactory = viewFactory ?? throw new ArgumentNullException(nameof(viewFactory));
            InitializeComponent();
            SourceInitialized += MainWindow_SourceInitialized;
            CommandBindings.Add(new CommandBinding(CloseTabCommand, CloseTabExecuted, CanCloseTabExecuted));
            StateChanged += (_, _) => UpdateMaximizeRestoreButton();
            Loaded += (_, _) =>
            {
                UpdateMaximizeRestoreButton();
            };
            Closed += (_, _) => AppLanguageManager.LanguageChanged -= AppLanguageManager_LanguageChanged;
            AppLanguageManager.LanguageChanged += AppLanguageManager_LanguageChanged;
            UpdateLoginUserDisplay();

            // Reuse the existing navigation item model instead of adding a new menu schema.
            _navigationInfo = NavigationCatalog.CreateItems();
            UiPermissionRuntime.ApplyToNavigation(_navigationInfo);

            // The custom navigation control handles visuals while the window owns page opening.
            NavigationBar.PaneTitle = "WpfApp";
            NavigationBar.ItemsSource = _navigationInfo;
            NavigationBar.SelectionChanged += NavigationBar_SelectionChanged;

            ControlInfoDataItem? homeItem = _navigationInfo.FirstOrDefault(item => string.Equals(item.ImageIconPath, IconFactory.House, StringComparison.Ordinal));
            if (homeItem is not null)
            {
                NavigationBar.SelectItem(homeItem);
                OpenContentTab(homeItem);
            }
        }

        private void UpdateLoginUserDisplay()
        {
            string userName = GetCurrentUserName();
            loginuser.Text = GetLastTwoCharacters(userName);
            loginuser.ToolTip = string.IsNullOrWhiteSpace(userName) ? null : userName;
            loginuserHost.ToolTip = loginuser.ToolTip;
        }

        private void MinimizeWindowButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaximizeRestoreWindowButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        private void CloseWindowButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void TitleBar_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState != MouseButtonState.Pressed || IsWithinElement<Button>(e.OriginalSource as DependencyObject))
            {
                return;
            }

            if (e.ClickCount == 2)
            {
                MaximizeRestoreWindowButton_Click(sender, e);
                e.Handled = true;
                return;
            }

            BeginTitleBarDrag();
            e.Handled = true;
        }

        private void UpdateMaximizeRestoreButton()
        {
            if (MaximizeRestoreWindowButton is null)
            {
                return;
            }

            bool isMaximized = WindowState == WindowState.Maximized;
            MaximizeRestoreWindowButton.Content = isMaximized ? "❐" : "□";
            MaximizeRestoreWindowButton.ToolTip = isMaximized
                ? AppLanguageManager.GetString("WindowRestoreToolTip", "还原")
                : AppLanguageManager.GetString("WindowMaximizeToolTip", "最大化");
        }

        private void AppLanguageManager_LanguageChanged(object? sender, EventArgs e)
        {
            UpdateMaximizeRestoreButton();
            UpdateTabHeaders();
            NavigationBar.ItemsSource = _navigationInfo;
        }

        private static string GetCurrentUserName()
        {
            return CurrentUserSession.Current?.Name?.Trim() ?? string.Empty;
        }

        private static string GetLastTwoCharacters(string text)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= 2)
            {
                return text;
            }

            return text[^2..];
        }

        private static bool IsWithinElement<TElement>(DependencyObject? source)
            where TElement : DependencyObject
        {
            while (source is not null)
            {
                if (source is TElement)
                {
                    return true;
                }

                source = LogicalTreeHelper.GetParent(source)
                    ?? (source is Visual ? VisualTreeHelper.GetParent(source) : null);
            }

            return false;
        }

        private void BeginTitleBarDrag()
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            ReleaseCapture();
            SendMessage(handle, WmNclButtonDown, new IntPtr(HtCaption), IntPtr.Zero);
        }

        #region 无边框窗口最大化边界

        /// <summary>
        /// 窗口句柄创建后接入原生消息。无边框窗口需要显式处理最大化工作区，
        /// 否则部分 Windows 环境会使用整块显示器区域并覆盖任务栏。
        /// </summary>
        private void MainWindow_SourceInitialized(object? sender, EventArgs e)
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(handle)?.AddHook(MainWindowWindowProc);
        }

        /// <summary>
        /// 使用当前窗口所在显示器的 rcWork 计算最大化位置和尺寸。
        /// rcWork 已扣除任务栏区域，同时可以正确支持任务栏位于顶部、左侧、右侧以及多显示器场景。
        /// </summary>
        private static IntPtr MainWindowWindowProc(
            IntPtr hwnd,
            int message,
            IntPtr wParam,
            IntPtr lParam,
            ref bool handled)
        {
            if (message != WmGetMinMaxInfo || lParam == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            IntPtr monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
            if (monitor == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            MonitorInfo monitorInfo = new() { Size = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(monitor, ref monitorInfo))
            {
                return IntPtr.Zero;
            }

            MinMaxInfo minMaxInfo = Marshal.PtrToStructure<MinMaxInfo>(lParam);
            minMaxInfo.MaxPosition.X = monitorInfo.WorkArea.Left - monitorInfo.MonitorArea.Left;
            minMaxInfo.MaxPosition.Y = monitorInfo.WorkArea.Top - monitorInfo.MonitorArea.Top;
            minMaxInfo.MaxSize.X = monitorInfo.WorkArea.Right - monitorInfo.WorkArea.Left;
            minMaxInfo.MaxSize.Y = monitorInfo.WorkArea.Bottom - monitorInfo.WorkArea.Top;
            Marshal.StructureToPtr(minMaxInfo, lParam, false);

            // 只覆盖最大化位置和尺寸，其余字段保留消息中已有值；标记完成可避免默认过程再次改回整屏尺寸。
            handled = true;
            return IntPtr.Zero;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MinMaxInfo
        {
            public NativePoint Reserved;
            public NativePoint MaxSize;
            public NativePoint MaxPosition;
            public NativePoint MinTrackSize;
            public NativePoint MaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo
        {
            public int Size;
            public NativeRect MonitorArea;
            public NativeRect WorkArea;
            public uint Flags;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);

        #endregion

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private void NavigationBar_SelectionChanged(object? sender, ModernNavigationSelectionChangedEventArgs e)
        {
            // Settings uses a dedicated tab instead of the normal content activation flow.
            if (e.IsSettingsSelected)
            {
                OpenSettingsTab();
                return;
            }

            if (e.SelectedItem is null || string.IsNullOrWhiteSpace(e.SelectedItem.Content))
            {
                return;
            }

            OpenContentTab(e.SelectedItem);
        }

        private void OpenSettingsTab()
        {
            // Reuse the settings tab if it already exists.
            foreach (object item in tabControl.Items)
            {
                if (item is TabItem existingTab && string.Equals(existingTab.Tag?.ToString(), SettingsTabKey, StringComparison.Ordinal))
                {
                    tabControl.SelectedItem = existingTab;
                    return;
                }
            }

            SettingsView settingsView = _viewFactory?.Create(typeof(SettingsView)) as SettingsView
                ?? throw new InvalidOperationException("SettingsView was not registered in the view factory.");
            settingsView.HorizontalAlignment = HorizontalAlignment.Stretch;
            settingsView.VerticalAlignment = VerticalAlignment.Stretch;
            UiPermissionRuntime.Attach(settingsView);

            TabItem tabItem = new TabItem
            {
                Header = AppLanguageManager.GetString("SettingsTabHeader", "设置"),
                Tag = SettingsTabKey,
                Content = settingsView
            };

            tabControl.Items.Add(tabItem);
            tabControl.SelectedItem = tabItem;
        }

        private void OpenContentTab(ControlInfoDataItem controlItem)
        {
            if (!controlItem.IsVisibility || !controlItem.IsEnable)
            {
                return;
            }

            // Reuse an existing tab instead of creating duplicates.
            foreach (object item in tabControl.Items)
            {
                if (item is TabItem existingTab &&
                    existingTab.Tag is ControlInfoDataItem existingItem &&
                    string.Equals(existingItem.UniqueId, controlItem.UniqueId, StringComparison.Ordinal))
                {
                    tabControl.SelectedItem = existingTab;
                    return;
                }
            }

            Type? targetType = ResolveContentType(controlItem.Content!);
            if (targetType is null)
            {
                MessageBox.Show(
                    this,
                    string.Format(
                        AppLanguageManager.GetString("NavigationViewTypeNotFoundMessage", "无法打开“{0}”页面，未找到对应的视图类型。\r\n{1}"),
                        AppLanguageManager.GetString(controlItem.Title, controlItem.Title),
                        controlItem.Content),
                    AppLanguageManager.GetString("NavigationMessageTitle", "导航提示"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (CreateContentInstance(targetType) is not FrameworkElement content)
            {
                MessageBox.Show(
                    this,
                    string.Format(
                        AppLanguageManager.GetString("NavigationViewCreateFailedMessage", "无法打开“{0}”页面，对应视图创建失败。\r\n{1}"),
                        AppLanguageManager.GetString(controlItem.Title, controlItem.Title),
                        controlItem.Content),
                    AppLanguageManager.GetString("NavigationMessageTitle", "导航提示"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            content.HorizontalAlignment = HorizontalAlignment.Stretch;
            content.VerticalAlignment = VerticalAlignment.Stretch;
            UiPermissionRuntime.Attach(content);

            TabItem tabItem = new TabItem
            {
                Header = AppLanguageManager.GetString(controlItem.Title, controlItem.Title),
                Tag = controlItem,
                Content = content
            };

            tabControl.Items.Add(tabItem);
            tabControl.SelectedItem = tabItem;
        }

        private static Type? ResolveContentType(string contentTypeName)
        {
            Type? targetType = Type.GetType(contentTypeName, throwOnError: false);
            if (targetType is not null)
            {
                return targetType;
            }

            string[] typeParts = contentTypeName.Split(',', 2, StringSplitOptions.TrimEntries);
            if (typeParts.Length != 2 || string.IsNullOrWhiteSpace(typeParts[1]))
            {
                return null;
            }

            try
            {
                Assembly assembly = Assembly.Load(typeParts[1]);
                return assembly.GetType(typeParts[0], throwOnError: false);
            }
            catch
            {
                return null;
            }
        }

        private FrameworkElement? CreateContentInstance(Type targetType)
        {
            return _viewFactory?.Create(targetType);
        }

        private void UpdateTabHeaders()
        {
            foreach (object item in tabControl.Items)
            {
                if (item is not TabItem tabItem)
                {
                    continue;
                }

                if (string.Equals(tabItem.Tag?.ToString(), SettingsTabKey, StringComparison.Ordinal))
                {
                    tabItem.Header = AppLanguageManager.GetString("SettingsTabHeader", "设置");
                }
                else if (tabItem.Tag is ControlInfoDataItem controlItem)
                {
                    tabItem.Header = AppLanguageManager.GetString(controlItem.Title, controlItem.Title);
                }
            }
        }

        private void CanCloseTabExecuted(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = e.Parameter is TabItem;
        }

        private void CloseTabExecuted(object sender, ExecutedRoutedEventArgs e)
        {
            if (e.Parameter is not TabItem tabItem)
            {
                return;
            }

            int closedIndex = tabControl.Items.IndexOf(tabItem);
            bool wasSelected = ReferenceEquals(tabControl.SelectedItem, tabItem);
            tabControl.Items.Remove(tabItem);

            if (wasSelected && tabControl.Items.Count > 0)
            {
                tabControl.SelectedIndex = Math.Min(closedIndex, tabControl.Items.Count - 1);
            }
        }
    }
}


