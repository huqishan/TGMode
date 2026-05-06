using Module.User.Services;

namespace Module.User.ViewModels;

/// <summary>
/// 权限配置界面 ViewModel 入口，负责初始化命令和加载权限配置。
/// </summary>
public sealed partial class PermissionConfigurationViewModel
{
    #region 构造方法

    public PermissionConfigurationViewModel()
    {
        InitializeCommands();
        ReloadPermissionDefinitions("已加载权限配置");
        if (!CanEditPermissions)
        {
            SetStatus("只有内置管理员或管理员角色可以修改权限配置", WarningBrush);
        }
    }

    #endregion
}
