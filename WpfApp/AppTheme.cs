using System;
using System.Linq;
using System.Windows;

namespace WpfApp
{
    public enum AppTheme
    {
        Dark,
        Light
    }

    public static class AppThemeManager
    {
        private const string ThemePathPrefix = "Resources/Themes/";

        public static AppTheme CurrentTheme { get; private set; } = AppTheme.Dark;

        public static void ApplyTheme(AppTheme theme)
        {
            ResourceDictionary themeDictionary = new ResourceDictionary
            {
                Source = new Uri($"{ThemePathPrefix}{theme}Theme.xaml", UriKind.Relative)
            };

            var dictionaries = Application.Current.Resources.MergedDictionaries;
            ResourceDictionary? existingThemeDictionary = dictionaries.FirstOrDefault(IsThemeDictionary);

            if (existingThemeDictionary is null)
            {
                dictionaries.Insert(0, themeDictionary);
            }
            else
            {
                int themeIndex = dictionaries.IndexOf(existingThemeDictionary);
                dictionaries[themeIndex] = themeDictionary;
            }

            CurrentTheme = theme;
        }

        private static bool IsThemeDictionary(ResourceDictionary dictionary)
        {
            return dictionary.Source?.OriginalString.StartsWith(ThemePathPrefix, StringComparison.OrdinalIgnoreCase) == true;
        }
    }
}
