using ControlLibrary;
using ControlLibrary.Controls.Navigation.Models;
using Module.User.Services;
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
        private const int HtCaption = 0x02;
        // The window keeps menu data and tab hosting only.
        private readonly List<ControlInfoDataItem> _navigationInfo;

        public MainWindow()
        {
            InitializeComponent();
            CommandBindings.Add(new CommandBinding(CloseTabCommand, CloseTabExecuted, CanCloseTabExecuted));
            StateChanged += (_, _) => UpdateMaximizeRestoreButton();
            Loaded += (_, _) => UpdateMaximizeRestoreButton();
            UpdateLoginUserDisplay();

            // Reuse the existing navigation item model instead of adding a new menu schema.
            _navigationInfo = NavigationCatalog.CreateItems();
            UiPermissionRuntime.ApplyToNavigation(_navigationInfo);

            // The custom navigation control handles visuals while the window owns page opening.
            NavigationBar.PaneTitle = "WpfApp";
            NavigationBar.ItemsSource = _navigationInfo;
            NavigationBar.SelectionChanged += NavigationBar_SelectionChanged;

            ControlInfoDataItem? homeItem = _navigationInfo.FirstOrDefault(item => item.Title == "Home");
            if (homeItem is not null)
            {
                NavigationBar.SelectItem(homeItem);
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
            MaximizeRestoreWindowButton.ToolTip = isMaximized ? "还原" : "最大化";
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
            const string settingsHeader = "Setting";

            // Reuse the settings tab if it already exists.
            foreach (object item in tabControl.Items)
            {
                if (item is TabItem existingTab && string.Equals(existingTab.Tag?.ToString(), SettingsTabKey, StringComparison.Ordinal))
                {
                    tabControl.SelectedItem = existingTab;
                    return;
                }
            }

            SettingsView settingsView = new SettingsView
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            UiPermissionRuntime.Attach(settingsView);

            TabItem tabItem = new TabItem
            {
                Header = settingsHeader,
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
                    $"无法打开“{controlItem.Title}”页面，未找到对应的视图类型。\r\n{controlItem.Content}",
                    "导航提示",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (Activator.CreateInstance(targetType) is not FrameworkElement content)
            {
                MessageBox.Show(
                    this,
                    $"无法打开“{controlItem.Title}”页面，对应视图创建失败。\r\n{controlItem.Content}",
                    "导航提示",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            content.HorizontalAlignment = HorizontalAlignment.Stretch;
            content.VerticalAlignment = VerticalAlignment.Stretch;
            UiPermissionRuntime.Attach(content);

            TabItem tabItem = new TabItem
            {
                Header = controlItem.Title,
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


