using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using iNKORE.UI.WPF.Modern.Common.IconKeys;
using iNKORE.UI.WPF.Modern.Controls;
using iNKORE.UI.WPF.Modern.Controls.Helpers;
using WpfApp.Navigation;

namespace WpfApp
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        // The window keeps menu data and tab hosting only.
        private readonly List<ControlInfoDataItem> _navigationInfo;

        public MainWindow()
        {
            InitializeComponent();

            // Reuse the existing navigation item model instead of adding a new menu schema.
            _navigationInfo = new List<ControlInfoDataItem>
            {
                new ControlInfoDataItem("Home", SegoeFluentIcons.Home.Glyph, null, null, description: "Overview"),
                new ControlInfoDataItem("测试界面", SegoeFluentIcons.Work.Glyph, "Module.Views.TestInfoView, Module", null, description: "Sandbox"),
                new ControlInfoDataItem("MES", SegoeFluentIcons.Location.Glyph, null,
                    new ObservableCollection<ControlInfoDataItem>
                    {
                        new ControlInfoDataItem("接口配置", SegoeFluentIcons.Settings.Glyph, "Module.MES.Views.ApiConfigView, Module.MES", null),
                        new ControlInfoDataItem("结构配置", SegoeFluentIcons.Settings.Glyph, "结构配置", null),
                        new ControlInfoDataItem("通讯配置", SegoeFluentIcons.Communications.Glyph, "通讯配置", null)
                    },
                    description: "Manufacturing"),
                new ControlInfoDataItem("设备管理", SegoeFluentIcons.Devices.Glyph, null,
                    new ObservableCollection<ControlInfoDataItem>
                    {
                        new ControlInfoDataItem("设备通讯配置", SegoeFluentIcons.Devices.Glyph, "Module.Views.CommunicationConfigView, Module", null),
                        new ControlInfoDataItem("流程图", SegoeFluentIcons.Devices.Glyph, "ControlLibrary.ControlViews.Flowchar.FlowchartView, ControlLibrary", null),
                        new ControlInfoDataItem("协议配置", SegoeFluentIcons.Devices.Glyph, "协议配置", null)
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

        private static IconElement GetIcon(string imagePath)
        {
            // Keep icon behavior aligned with the previous implementation.
            return imagePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                ? new BitmapIcon
                {
                    UriSource = new Uri(imagePath, UriKind.RelativeOrAbsolute),
                    ShowAsMonochrome = false
                }
                : new FontIcon
                {
                    Glyph = imagePath,
                    FontSize = 16
                };
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
                if (item is TabItem existingTab && string.Equals(existingTab.Header?.ToString(), settingsHeader, StringComparison.Ordinal))
                {
                    tabControl.SelectedItem = existingTab;
                    return;
                }
            }

            TabItem tabItem = new TabItem
            {
                Header = settingsHeader,
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
                if (item is TabItem existingTab && string.Equals(existingTab.Header?.ToString(), controlItem.Title, StringComparison.Ordinal))
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
                Content = (FrameworkElement)Activator.CreateInstance(targetType)!
            };

            TabItemHelper.SetIcon(tabItem, GetIcon(controlItem.ImageIconPath));
            tabControl.Items.Add(tabItem);
            tabControl.SelectedItem = tabItem;
        }
    }
}
