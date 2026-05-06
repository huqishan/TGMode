using Module.User.Models;
using System;

namespace Module.User.Services;

/// <summary>
/// 当前登录用户会话，供登录窗口、主窗口和权限运行时共享当前账号信息。
/// </summary>
public static class CurrentUserSession
{
    #region 当前用户状态

    public static AuthenticatedUser? Current { get; private set; }

    #endregion

    #region 登录状态切换

    public static void SignIn(AuthenticatedUser user)
    {
        Current = user;
    }

    public static void SignOut()
    {
        Current = null;
    }

    public static AuthenticatedUser RequireCurrentUser()
    {
        return Current ?? throw new InvalidOperationException("No user is signed in.");
    }

    #endregion
}
