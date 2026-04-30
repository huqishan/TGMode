using ControlLibrary;
using ControlLibrary.Controls.Navigation.Models;
using System.Collections.ObjectModel;

namespace WpfApp.Services.Navigation;

public static class NavigationCatalog
{
    public static List<ControlInfoDataItem> CreateItems()
    {
        return new List<ControlInfoDataItem>
        {
            new("Home", IconFactory.House, null, null, description: "Overview"),
            new("测试界面", IconFactory.FlaskConical, "Module.Views.TestInfoView, Module", null, description: "Sandbox"),
            new("MES", IconFactory.Boxes, null,
                new ObservableCollection<ControlInfoDataItem>
                {
                    new("接口配置", IconFactory.PlugZap, "Module.MES.Views.ApiConfigView, Module.MES", null),
                    new("结构配置", IconFactory.Network, "Module.MES.Views.DataStructureConfigView, Module.MES", null),
                    new("通讯配置", IconFactory.MessageSquareCode, "通讯配置", null)
                },
                description: "Manufacturing"),
            new("设备管理", IconFactory.Cpu, null,
                new ObservableCollection<ControlInfoDataItem>
                {
                    new("设备通讯配置", IconFactory.Router, "ControlLibrary.ControlViews.Communication.DeviceCommunicationConfigView, ControlLibrary", null),
                    new("流程图", IconFactory.Workflow, "ControlLibrary.ControlViews.Flowchar.FlowchartView, ControlLibrary", null),
                    new("协议配置", IconFactory.FileCog, "ControlLibrary.ControlViews.Protocol.ProtocolConfigView, ControlLibrary", null)
                },
                description: "Devices"),
            new("脚本管理", IconFactory.FlaskConical, "ControlLibrary.ControlViews.LuaScrip.LuaScriptView, ControlLibrary", null, description: "Lua"),
            new("数据管理", IconFactory.Cpu, null,
                new ObservableCollection<ControlInfoDataItem>
                {
                    new("测试数据", IconFactory.Router, "WpfApp.Views.DataManagement.TestDataView, WpfApp", null),
                    new("MES通讯数据", IconFactory.Workflow, string.Empty, null),
                    new("数据源配置", IconFactory.FileCog, "WpfApp.Views.DataManagement.DataSourceConfigView, WpfApp", null)
                },
                description: "Data"),
            new("用户管理", IconFactory.Cpu, null,
                new ObservableCollection<ControlInfoDataItem>
                {
                    new("账号", IconFactory.Router, "WpfApp.Views.UserManagement.AccountManagementView, WpfApp", null),
                    new("权限配置", IconFactory.Workflow, "WpfApp.Views.UserManagement.PermissionConfigurationView, WpfApp", null)
                },
                description: "Users"),
        };
    }
}
