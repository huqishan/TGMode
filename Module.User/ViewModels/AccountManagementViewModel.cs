using Module.User.Services;

namespace Module.User.ViewModels;

/// <summary>
/// 账号管理界面 ViewModel 入口，负责初始化命令和加载账号配置。
/// </summary>
public sealed partial class AccountManagementViewModel
{
    #region 构造方法

    public AccountManagementViewModel()
    {
        _currentUser = CurrentUserSession.RequireCurrentUser();
        InitializeCommands();
        LoadCatalog(AccountConfigurationStore.LoadCatalog());
    }

    #endregion
}
