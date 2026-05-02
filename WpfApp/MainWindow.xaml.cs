using ControlLibrary;
using ControlLibrary.Controls.Navigation.Models;
using Module.User.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

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
        // The window keeps menu data and tab hosting only.
        private readonly List<ControlInfoDataItem> _navigationInfo;

        public MainWindow()
        {
            InitializeComponent();
            CommandBindings.Add(new CommandBinding(CloseTabCommand, CloseTabExecuted, CanCloseTabExecuted));
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
            string userName = GetCurrentUserDisplayName();
            loginuser.Text = GetLastTwoCharacters(userName);
            loginuser.ToolTip = string.IsNullOrWhiteSpace(userName) ? null : userName;
            loginuserHost.ToolTip = loginuser.ToolTip;
        }

        private static string GetCurrentUserDisplayName()
        {
            string? name = CurrentUserSession.Current?.Name;
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name.Trim();
            }

            return CurrentUserSession.Current?.Account?.Trim() ?? string.Empty;
        }

        private static string GetLastTwoCharacters(string text)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= 2)
            {
                return text;
            }

            return text[^2..];
        }

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

            Type? targetType = Type.GetType(controlItem.Content!);
            if (targetType is null)
            {
                // Some items are still placeholders and do not map to a real view type yet.
                return;
            }

            FrameworkElement content = (FrameworkElement)Activator.CreateInstance(targetType)!;
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


