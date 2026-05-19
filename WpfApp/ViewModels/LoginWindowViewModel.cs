using ControlLibrary;
using Module.User.Mediator;
using Module.User.Services;
using Shared.Infrastructure.Mediator;
using System;
using System.Windows.Media;

namespace WpfApp.ViewModels;

public sealed class LoginWindowViewModel : ViewModelProperties
{
    private static readonly Brush SuccessBrush =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16A34A"));

    private static readonly Brush WarningBrush =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EA580C"));

    private static readonly Brush NeutralBrush =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));

    private string _account = AccountConfigurationStore.BuiltInAdminAccount;
    private string _statusText = "默认管理员：账号 10086，密码 10086";
    private Brush _statusBrush = NeutralBrush;
    private readonly IMediator _mediator;

    public LoginWindowViewModel(IMediator mediator)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
    }

    public string Account
    {
        get => _account;
        set => SetField(ref _account, value ?? string.Empty, true);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value ?? string.Empty);
    }

    public Brush StatusBrush
    {
        get => _statusBrush;
        private set => SetField(ref _statusBrush, value);
    }

    public bool TryLogin(string password)
    {
        AuthenticationResult result = _mediator
            .Send(new SignInRequest(Account, password))
            .GetAwaiter()
            .GetResult();

        if (!result.IsSuccess)
        {
            StatusText = result.Message;
            StatusBrush = WarningBrush;
            return false;
        }

        StatusText = result.Message;
        StatusBrush = SuccessBrush;
        return true;
    }
}
