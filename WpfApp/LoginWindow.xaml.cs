using Module.User.Models;
using Module.User.Services;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace WpfApp;

public partial class LoginWindow : Window, INotifyPropertyChanged
{
    private static readonly Brush SuccessBrush =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16A34A"));

    private static readonly Brush WarningBrush =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EA580C"));

    private static readonly Brush NeutralBrush =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));

    private string _statusText = "默认管理员：账号 10086，密码 10086";
    private Brush _statusBrush = NeutralBrush;

    public LoginWindow()
    {
        InitializeComponent();
        DataContext = this;
        Loaded += (_, _) =>
        {
            PasswordInput.Focus();
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    public Brush StatusBrush
    {
        get => _statusBrush;
        private set => SetField(ref _statusBrush, value);
    }

    private void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        TryLogin();
    }

    private void ExitButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void CloseCaptionButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void WindowDragArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed)
        {
            return;
        }

        DragMove();
    }

    private void PasswordInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            TryLogin();
            e.Handled = true;
        }
    }

    private void TryLogin()
    {
        if (!AccountConfigurationStore.TryAuthenticate(
                AccountInput.Text,
                PasswordInput.Password,
                out AuthenticatedUser? user,
                out string message))
        {
            StatusText = message;
            StatusBrush = WarningBrush;
            PasswordInput.SelectAll();
            PasswordInput.Focus();
            return;
        }

        if (user is null)
        {
            StatusText = "登录状态异常，请重新登录";
            StatusBrush = WarningBrush;
            return;
        }

        CurrentUserSession.SignIn(user);
        StatusText = $"{user.Name} 登录成功";
        StatusBrush = SuccessBrush;
        DialogResult = true;
        Close();
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
