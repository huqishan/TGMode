using Module.User.Models;

namespace Module.User.Services;

public sealed class AuthenticationService : IAuthenticationService
{
    public AuthenticationResult SignIn(string account, string password)
    {
        if (!AccountConfigurationStore.TryAuthenticate(
                account,
                password,
                out AuthenticatedUser? user,
                out string message))
        {
            return new AuthenticationResult(false, message);
        }

        if (user is null)
        {
            return new AuthenticationResult(false, "登录状态异常，请重新登录");
        }

        CurrentUserSession.SignIn(user);
        return new AuthenticationResult(true, $"{user.Name} 登录成功", user.Name);
    }
}
