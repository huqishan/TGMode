using Module.User.ViewModels;
using System;
using System.Windows;
using System.Windows.Controls;

namespace Module.User.Views;

public partial class AccountManagementView : UserControl
{
    #region 构造与 ViewModel 订阅

    public AccountManagementView()
    {
        InitializeComponent();

        if (ViewModel is not null)
        {
            ViewModel.RequestClearPassword += ViewModel_RequestClearPassword;
        }

        Unloaded += AccountManagementView_Unloaded;
    }

    private AccountManagementViewModel? ViewModel => DataContext as AccountManagementViewModel;

    private void AccountManagementView_Unloaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null)
        {
            ViewModel.RequestClearPassword -= ViewModel_RequestClearPassword;
        }
    }

    #endregion

    #region 密码输入框交互

    private void PasswordInput_PasswordChanged(object sender, RoutedEventArgs e)
    {
        ViewModel?.SetEditingPassword(PasswordInput.Password);
    }

    private void ViewModel_RequestClearPassword(object? sender, EventArgs e)
    {
        PasswordInput.Clear();
    }

    #endregion
}
