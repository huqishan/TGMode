using ControlLibrary;
using ControlLibrary.Controls.Navigation.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

            // Reuse the existing navigation item model instead of adding a new menu schema.
            _navigationInfo = new List<ControlInfoDataItem>
            {
                new ControlInfoDataItem("Home", IconFactory.House, null, null, description: "Overview"),
                new ControlInfoDataItem("测试界面", IconFactory.FlaskConical, "Module.Views.TestInfoView, Module", null, description: "Sandbox"),
                new ControlInfoDataItem("MES", IconFactory.Boxes, null,
                    new ObservableCollection<ControlInfoDataItem>
                    {
                        new ControlInfoDataItem("接口配置", IconFactory.PlugZap, "Module.MES.Views.ApiConfigView, Module.MES", null),
                        new ControlInfoDataItem("结构配置", IconFactory.Network, "结构配置", null),
                        new ControlInfoDataItem("通讯配置", IconFactory.MessageSquareCode, "通讯配置", null)
                    },
                    description: "Manufacturing"),
                new ControlInfoDataItem("设备管理", IconFactory.Cpu, null,
                    new ObservableCollection<ControlInfoDataItem>
                    {
                        new ControlInfoDataItem("设备通信配置", IconFactory.Router, "ControlLibrary.ControlViews.Communication.DeviceCommunicationConfigView, ControlLibrary", null),
                        new ControlInfoDataItem("流程图", IconFactory.Workflow, "ControlLibrary.ControlViews.Flowchar.FlowchartView, ControlLibrary", null),
                        new ControlInfoDataItem("协议配置", IconFactory.FileCog, "协议配置", null)
                    },
                    description: "Devices")
            };

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

            TabItem tabItem = new TabItem
            {
                Header = settingsHeader,
                Tag = SettingsTabKey,
                Content = new SettingsView()
            };

            tabControl.Items.Add(tabItem);
            tabControl.SelectedItem = tabItem;
        }

        private void OpenContentTab(ControlInfoDataItem controlItem)
        {
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

            TabItem tabItem = new TabItem
            {
                Header = controlItem.Title,
                Tag = controlItem,
                Content = (FrameworkElement)Activator.CreateInstance(targetType)!
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
