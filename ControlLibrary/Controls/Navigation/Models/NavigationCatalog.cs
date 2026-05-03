using ControlLibrary;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ControlLibrary.Controls.Navigation.Models;

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
                    new("设备通信配置", IconFactory.Router, "ControlLibrary.ControlViews.Communication.DeviceCommunicationConfigView, ControlLibrary", null),
                    new("协议配置", IconFactory.FileCog, "ControlLibrary.ControlViews.Protocol.ProtocolConfigView, ControlLibrary", null)
                },
                description: "Devices"),
            new("业务管理", IconFactory.Cpu, null,
                new ObservableCollection<ControlInfoDataItem>
                {
                    new("产品配置", IconFactory.Boxes, "Module.Business.Views.ProductConfigurationView, Module.Business", null),
                    new("工步配置", IconFactory.Router, "Module.Business.Views.WorkStepConfigurationView, Module.Business", null),
                    new("方案配置", IconFactory.FileCog, "Module.Business.Views.SchemeConfigurationView, Module.Business", null),
                    new("流程图", IconFactory.Workflow, "Module.Business.Views.FlowchartView, Module.Business", null),
                },
                description: "Business"),
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
                    new("账号", IconFactory.Router, "Module.User.Views.AccountManagementView, Module.User", null),
                    new("权限配置", IconFactory.Workflow, "Module.User.Views.PermissionConfigurationView, Module.User", null)
                },
                description: "Users"),
        };
    }
}
