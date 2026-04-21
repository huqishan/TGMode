using iNKORE.UI.WPF.Modern;
using iNKORE.UI.WPF.Modern.Common.IconKeys;
using iNKORE.UI.WPF.Modern.Controls;
using iNKORE.UI.WPF.Modern.Controls.Helpers;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using WpfApp.Navigation;

namespace WpfApp
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        //测试234
        private NavigationViewItem? _TestMenuItem;
        private NavigationViewItem? _HomeMenuItem;
        List<ControlInfoDataItem> _NavigationInfo = new List<ControlInfoDataItem>();
        public MainWindow()
        {
            InitializeComponent();
            _NavigationInfo = new List<ControlInfoDataItem>() {
            new ControlInfoDataItem("Home",SegoeFluentIcons.Home.Glyph,"Home",null),
            new ControlInfoDataItem("测试界面",SegoeFluentIcons.Work.Glyph,"Module.Views.TestInfoView, Module",null),
            new ControlInfoDataItem("MES",SegoeFluentIcons.Location.Glyph,null,
                new System.Collections.ObjectModel.ObservableCollection<ControlInfoDataItem>(){
                    new ControlInfoDataItem("接口配置",SegoeFluentIcons.Settings.Glyph,"Module.MES.Views.ApiConfigView, Module.MES",null),
                    new ControlInfoDataItem("结构配置",SegoeFluentIcons.Settings.Glyph,"结构配置",null),
                    new ControlInfoDataItem("通讯配置",SegoeFluentIcons.Communications.Glyph,"通讯配置",null)
             }),
             new ControlInfoDataItem("设备管理",SegoeFluentIcons.Devices.Glyph,null,
            new System.Collections.ObjectModel.ObservableCollection<ControlInfoDataItem>(){
             new ControlInfoDataItem("设备通讯配置",SegoeFluentIcons.Devices.Glyph,"Module.Views.CommunicationConfigView, Module",null),
             new ControlInfoDataItem("流程图",SegoeFluentIcons.Devices.Glyph,"WpfApp.Views.FlowchartView, WpfApp",null),
             new ControlInfoDataItem("协议配置",SegoeFluentIcons.Devices.Glyph,"协议配置",null)
             })
            };
            AddNavigationMenuItems();
            //iNKORE.UI.WPF.Modern.ThemeManager.SetRequestedTheme(this, App.GetEnum<ElementTheme>("Dark"));
        }
        private void AddNavigationMenuItems()
        {
            foreach (var group in _NavigationInfo.OrderBy(i => i.Title))
            {
                var itemGroup = new NavigationViewItem() { Content = group.Title, Tag = group.UniqueId, DataContext = group, Icon = GetIcon(group.ImageIconPath) };
                AutomationProperties.SetName(itemGroup, group.Title);
                foreach (var item in group.Items)
                {
                    var itemInGroup = new NavigationViewItem() { IsEnabled = item.IsEnable, Content = item.Title, Tag = item.UniqueId, DataContext = item, Icon = GetIcon(item.ImageIconPath) };
                    itemGroup.MenuItems.Add(itemInGroup);
                    AutomationProperties.SetName(itemInGroup, item.Title);
                }
                switch (group.Title)
                {
                    case "测试界面":
                        this._TestMenuItem = itemGroup;
                        break;
                    case "Home":
                        this._HomeMenuItem = itemGroup;
                        break;
                    default:
                        Navigation.MenuItems.Add(itemGroup);
                        break;
                }
            }
            Navigation.MenuItems.Insert(0, _TestMenuItem);
            Navigation.MenuItems.Insert(0, _HomeMenuItem);
            Navigation.MenuItems.Insert(2, new NavigationViewItemSeparator());
            Navigation.SelectedItem = _HomeMenuItem;
        }
        private static IconElement GetIcon(string imagePath)
        {
            return imagePath.ToLowerInvariant().EndsWith(".png") ?
                        (IconElement)new BitmapIcon() { UriSource = new Uri(imagePath, UriKind.RelativeOrAbsolute), ShowAsMonochrome = false } :
                        (IconElement)new FontIcon()
                        {
                            Glyph = imagePath,
                            FontSize = 16
                        };
        }
        private void NavigationViewControl_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.IsSettingsSelected)
            {
                TabItem tabitem = new System.Windows.Controls.TabItem() { Header = "Setting" };
                tabitem.Content = new SettingsView();
                tabControl.Items.Add(tabitem);
                tabControl.SelectedItem = tabitem;
            }
            else
            {
                var selectedItem = args.SelectedItemContainer;
                ControlInfoDataItem controlItem = (ControlInfoDataItem)selectedItem.DataContext;
                if (controlItem.Content == null) return;
                foreach (var item in tabControl.Items)
                {
                    if (item is TabItem tabItem && tabItem.Header.ToString() == controlItem.Title)
                    {
                        tabControl.SelectedItem = item;
                        return;
                    }
                }

                TabItem tabitem = new System.Windows.Controls.TabItem() { Header = controlItem.Title };
                TabItemHelper.SetIcon(tabitem, GetIcon(controlItem.ImageIconPath));
                Type type = Type.GetType(controlItem.Content);
                if (type != null)
                {
                    tabitem.Content = (FrameworkElement)Activator.CreateInstance(type);
                    tabControl.Items.Add(tabitem);
                    tabControl.SelectedItem = tabitem;
                }
            }
        }
    }
}
