namespace Module.User.Services;

public interface IAuthenticationService
{
    AuthenticationResult SignIn(string account, string password);
}

public sealed record AuthenticationResult(bool IsSuccess, string Message, string UserName = "");
