using ControlLibrary;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace WpfApp.ViewModels;

public sealed class SettingsViewModel : ViewModelProperties
{
    private bool _isUpdatingLanguageSelection;
    private bool _isLanguageEventSubscribed;
    private ObservableCollection<LanguageSelectionItem> _languageOptions = new();
    private string _selectedLanguageKey = string.Empty;

    public ObservableCollection<LanguageSelectionItem> LanguageOptions
    {
        get => _languageOptions;
        private set => SetField(ref _languageOptions, value ?? new ObservableCollection<LanguageSelectionItem>());
    }

    public string SelectedLanguageKey
    {
        get => _selectedLanguageKey;
        set
        {
            if (!SetField(ref _selectedLanguageKey, value ?? string.Empty))
            {
                return;
            }

            if (!_isUpdatingLanguageSelection && !string.IsNullOrWhiteSpace(_selectedLanguageKey))
            {
                AppLanguageManager.ApplyLanguage(_selectedLanguageKey);
                RefreshLanguageOptions();
            }
        }
    }

    public void Load()
    {
        SubscribeLanguageChanged();
        RefreshLanguageOptions();
    }

    public void Unload()
    {
        UnsubscribeLanguageChanged();
    }

    private void AppLanguageManager_LanguageChanged(object? sender, EventArgs e)
    {
        RefreshLanguageOptions();
    }

    private void RefreshLanguageOptions()
    {
        _isUpdatingLanguageSelection = true;

        LanguageOptions = new ObservableCollection<LanguageSelectionItem>(
            AppLanguageManager.SupportedLanguages.Select(language => new LanguageSelectionItem(
                language.Key,
                AppLanguageManager.GetString(language.DisplayNameResourceKey, language.Key))));

        SelectedLanguageKey = AppLanguageManager.CurrentLanguage;
        _isUpdatingLanguageSelection = false;
    }

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
