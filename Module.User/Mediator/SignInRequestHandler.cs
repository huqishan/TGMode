using Module.User.Services;
using Shared.Infrastructure.Mediator;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Module.User.Mediator;

/// <summary>
/// 处理登录请求。
/// </summary>
public sealed class SignInRequestHandler : IRequestHandler<SignInRequest, AuthenticationResult>
{
    private readonly IAuthenticationService _authenticationService;

    public SignInRequestHandler(IAuthenticationService authenticationService)
    {
        _authenticationService = authenticationService ??
                                 throw new ArgumentNullException(nameof(authenticationService));
    }

    public Task<AuthenticationResult> Handle(SignInRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        AuthenticationResult result = _authenticationService.SignIn(request.Account, request.Password);
        return Task.FromResult(result);
    }
}
