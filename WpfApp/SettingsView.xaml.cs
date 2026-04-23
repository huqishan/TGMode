using System;
using System.Windows;
using System.Windows.Controls;

namespace WpfApp
{
    /// <summary>
    /// Interaction logic for SettingsView.xaml.
    /// </summary>
    public partial class SettingsView : UserControl
    {
        private bool _isUpdatingThemeSelection;

        public SettingsView()
        {
            InitializeComponent();
            Loaded += SettingsView_Loaded;
        }

        private void SettingsView_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateSelectedThemeRadioButton();
        }

        private void OnThemeRadioButtonChecked(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingThemeSelection)
            {
                return;
            }

            if (sender is RadioButton { Tag: string themeName } &&
                Enum.TryParse(themeName, ignoreCase: true, out AppTheme theme))
            {
                AppThemeManager.ApplyTheme(theme);
                UpdateSelectedThemeRadioButton();
            }
        }

        private void UpdateSelectedThemeRadioButton()
        {
            _isUpdatingThemeSelection = true;

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

            _isUpdatingThemeSelection = false;
        }
    }
}
