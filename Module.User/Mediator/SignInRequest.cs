using Shared.Infrastructure.Mediator;
using Module.User.Services;

namespace Module.User.Mediator;

/// <summary>
/// 请求执行账号登录。
/// </summary>
public sealed record SignInRequest(string Account, string Password) : IRequest<AuthenticationResult>;
