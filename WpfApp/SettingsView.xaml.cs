using System;
using System.Windows;
using System.Windows.Controls;
using WpfApp.ViewModels;

namespace WpfApp;

/// <summary>
/// 应用设置页；业务状态由 <see cref="SettingsViewModel"/> 承载。
/// </summary>
public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    public SettingsView(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        Loaded += SettingsView_Loaded;
        Unloaded += SettingsView_Unloaded;
    }

    private SettingsViewModel ViewModel => (SettingsViewModel)DataContext;

    private void SettingsView_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.Load();
        UpdateSelectedThemeRadioButton();
    }

    private void SettingsView_Unloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.Unload();
    }

    private void OnThemeRadioButtonChecked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string themeName } &&
            Enum.TryParse(themeName, true, out AppTheme theme))
        {
            AppThemeManager.ApplyTheme(theme);
            UpdateSelectedThemeRadioButton();
        }
    }

    private void UpdateSelectedThemeRadioButton()
    {
        foreach (object child in ThemePanel.Children)
        {
            if (child is RadioButton radioButton && radioButton.Tag is string themeName)
            {
                radioButton.IsChecked = string.Equals(
                    themeName,
                    AppThemeManager.CurrentTheme.ToString(),
                    StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
