using System;
using System.Linq;
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
        private bool _isUpdatingLanguageSelection;
        private bool _isLanguageEventSubscribed;

        public SettingsView()
        {
            InitializeComponent();
            Loaded += SettingsView_Loaded;
            Unloaded += SettingsView_Unloaded;
        }

        private void SettingsView_Loaded(object sender, RoutedEventArgs e)
        {
            SubscribeLanguageChanged();
            UpdateSelectedThemeRadioButton();
            RefreshLanguageOptions();
        }

        private void SettingsView_Unloaded(object sender, RoutedEventArgs e)
        {
            UnsubscribeLanguageChanged();
        }

        private void AppLanguageManager_LanguageChanged(object? sender, EventArgs e)
        {
            RefreshLanguageOptions();
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

        private void OnLanguageSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingLanguageSelection)
            {
                return;
            }

            if (LanguageComboBox.SelectedValue is string languageKey)
            {
                AppLanguageManager.ApplyLanguage(languageKey);
                RefreshLanguageOptions();
            }
        }

        private void RefreshLanguageOptions()
        {
            _isUpdatingLanguageSelection = true;

            LanguageComboBox.ItemsSource = AppLanguageManager.SupportedLanguages
                .Select(language => new LanguageSelectionItem(
                    language.Key,
                    AppLanguageManager.GetString(language.DisplayNameResourceKey, language.Key)))
                .ToList();
            LanguageComboBox.SelectedValue = AppLanguageManager.CurrentLanguage;

            _isUpdatingLanguageSelection = false;
        }

        private sealed record LanguageSelectionItem(string Key, string DisplayName);

        private void SubscribeLanguageChanged()
        {
            if (_isLanguageEventSubscribed)
            {
                return;
            }

            AppLanguageManager.LanguageChanged += AppLanguageManager_LanguageChanged;
            _isLanguageEventSubscribed = true;
        }

        private void UnsubscribeLanguageChanged()
        {
            if (!_isLanguageEventSubscribed)
            {
                return;
            }

            AppLanguageManager.LanguageChanged -= AppLanguageManager_LanguageChanged;
            _isLanguageEventSubscribed = false;
        }
    }
}
