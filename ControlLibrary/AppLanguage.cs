using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows;

namespace ControlLibrary
{
    public sealed record AppLanguageOption(string Key, string ResourceFileName, string DisplayNameResourceKey);

    public static class AppLanguageManager
    {
        private const string LanguagePathPrefix = "Resources/Language/";

        private static readonly IReadOnlyList<AppLanguageOption> Languages =
        [
            new AppLanguageOption("zh-CN", "ZhCN.xaml", "LanguageDisplayNameZhCN"),
            new AppLanguageOption("en-US", "EnUS.xaml", "LanguageDisplayNameEnUS")
        ];

        public static IReadOnlyList<AppLanguageOption> SupportedLanguages => Languages;

        public static string CurrentLanguage { get; private set; } = Languages[0].Key;

        public static event EventHandler? LanguageChanged;

        public static void ApplyLanguage(string languageKey)
        {
            AppLanguageOption language = Languages.FirstOrDefault(item =>
                    string.Equals(item.Key, languageKey, StringComparison.OrdinalIgnoreCase))
                ?? Languages[0];

            ResourceDictionary languageDictionary = new()
            {
                Source = new Uri($"{LanguagePathPrefix}{language.ResourceFileName}", UriKind.Relative)
            };

            var dictionaries = Application.Current.Resources.MergedDictionaries;
            ResourceDictionary? existingLanguageDictionary = dictionaries.FirstOrDefault(IsLanguageDictionary);

            if (existingLanguageDictionary is null)
            {
                dictionaries.Add(languageDictionary);
            }
            else
            {
                int languageIndex = dictionaries.IndexOf(existingLanguageDictionary);
                dictionaries[languageIndex] = languageDictionary;
            }

            SetCulture(language.Key);
            CurrentLanguage = language.Key;
            LanguageChanged?.Invoke(null, EventArgs.Empty);
        }

        public static string GetString(string resourceKey, string fallback)
        {
            return Application.Current.TryFindResource(resourceKey) as string
                ?? fallback;
        }

        private static bool IsLanguageDictionary(ResourceDictionary dictionary)
        {
            return dictionary.Source?.OriginalString.StartsWith(LanguagePathPrefix, StringComparison.OrdinalIgnoreCase) == true;
        }

        private static void SetCulture(string languageKey)
        {
            CultureInfo culture = CultureInfo.GetCultureInfo(languageKey);

            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;
        }
    }
}
