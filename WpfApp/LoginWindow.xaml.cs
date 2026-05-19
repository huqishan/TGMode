using System;
using System.Windows;
using System.Windows.Input;
using WpfApp.ViewModels;

namespace WpfApp;

public partial class LoginWindow : Window
{
    public LoginWindow()
    {
        InitializeComponent();
    }

    public LoginWindow(LoginWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        Loaded += (_, _) =>
        {
            PasswordInput.Focus();
        };
    }

    private LoginWindowViewModel ViewModel => (LoginWindowViewModel)DataContext;

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
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
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
        if (ViewModel.TryLogin(PasswordInput.Password))
        {
            DialogResult = true;
            Close();
            return;
        }

        PasswordInput.SelectAll();
        PasswordInput.Focus();
    }
}
