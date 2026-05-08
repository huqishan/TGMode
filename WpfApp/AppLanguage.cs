using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using ControlLibrary.Controls.SearchBox.Control;
using System.Windows;

namespace WpfApp
{
    public sealed record AppLanguageOption(string Key, string ResourceFileName, string DisplayNameResourceKey);

    public static class AppLanguageManager
    {
        private const string LanguagePathPrefix = "Resources/Language/";
        private static readonly ConditionalWeakTable<DependencyObject, Dictionary<DependencyProperty, LocalizedTextState>> OriginalTextValues = new();
        private static readonly ConditionalWeakTable<DependencyObject, LocalizedTextSubscriptions> TextSubscriptions = new();
        private static readonly Regex TemplatePlaceholderRegex = new(@"\{(?<index>\d+)\}", RegexOptions.Compiled);
        private static List<LocalizedTemplate> _localizedTemplates = new();
        private static bool _autoLocalizationEnabled;

        private static readonly IReadOnlyList<AppLanguageOption> Languages =
        [
            new AppLanguageOption("zh-CN", "ZhCN.xaml", "LanguageDisplayNameZhCN"),
            new AppLanguageOption("en-US", "EnUS.xaml", "LanguageDisplayNameEnUS")
        ];

        public static IReadOnlyList<AppLanguageOption> SupportedLanguages => Languages;

        public static string CurrentLanguage { get; private set; } = Languages[0].Key;

        public static event EventHandler? LanguageChanged;

        public static void EnableAutoLocalization()
        {
            if (_autoLocalizationEnabled)
            {
                return;
            }

            EventManager.RegisterClassHandler(
                typeof(FrameworkElement),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(OnFrameworkElementLoaded),
                true);

            _autoLocalizationEnabled = true;
        }

        public static void ApplyLanguage(string languageKey)
        {
            AppLanguageOption language = Languages.FirstOrDefault(item =>
                    string.Equals(item.Key, languageKey, StringComparison.OrdinalIgnoreCase))
                ?? Languages[0];

            ResourceDictionary languageDictionary = new ResourceDictionary
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
            RebuildLocalizedTemplates();
            LanguageChanged?.Invoke(null, EventArgs.Empty);
            LocalizeOpenWindows();
        }

        public static string GetString(string resourceKey, string fallback)
        {
            return Application.Current.TryFindResource(resourceKey) as string
                ?? TryGetStringFromTemplate(resourceKey)
                ?? fallback;
        }

        public static void LocalizeOpenWindows()
        {
            foreach (Window window in Application.Current.Windows.OfType<Window>().ToList())
            {
                LocalizeElement(window);
            }
        }

        public static void LocalizeElement(DependencyObject? root)
        {
            if (root is null)
            {
                return;
            }

            HashSet<DependencyObject> visited = new();
            LocalizeElement(root, visited);
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

        private static void OnFrameworkElementLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is DependencyObject dependencyObject)
            {
                LocalizeElement(dependencyObject);
            }
        }

        private static void LocalizeElement(DependencyObject root, HashSet<DependencyObject> visited)
        {
            if (!visited.Add(root))
            {
                return;
            }

            LocalizeObject(root);

            if (root is DataGrid dataGrid)
            {
                foreach (DataGridColumn column in dataGrid.Columns.ToList())
                {
                    LocalizeObject(column);
                }
            }

            if (root is TextBlock textBlock)
            {
                foreach (Inline inline in textBlock.Inlines.ToList())
                {
                    if (inline is DependencyObject inlineObject)
                    {
                        LocalizeElement(inlineObject, visited);
                    }
                }
            }

            foreach (object logicalChild in LogicalTreeHelper.GetChildren(root).Cast<object>().ToList())
            {
                if (logicalChild is DependencyObject logicalObject)
                {
                    LocalizeElement(logicalObject, visited);
                }
            }

            if (root is Visual || root is Visual3D)
            {
                int childCount = VisualTreeHelper.GetChildrenCount(root);
                List<DependencyObject> visualChildren = new(childCount);
                for (int i = 0; i < childCount; i++)
                {
                    visualChildren.Add(VisualTreeHelper.GetChild(root, i));
                }

                foreach (DependencyObject visualChild in visualChildren)
                {
                    LocalizeElement(visualChild, visited);
                }
            }
        }

        private static void LocalizeObject(DependencyObject element)
        {
            switch (element)
            {
                case Window window:
                    LocalizeStringProperty(window, Window.TitleProperty, () => window.Title, value => window.SetCurrentValue(Window.TitleProperty, value));
                    break;
                case TextBlock textBlock:
                    SubscribeTextChanged(textBlock, TextBlock.TextProperty);
                    LocalizeStringProperty(textBlock, TextBlock.TextProperty, () => textBlock.Text, value => textBlock.SetCurrentValue(TextBlock.TextProperty, value));
                    break;
                case Run run:
                    SubscribeTextChanged(run, Run.TextProperty);
                    LocalizeStringProperty(run, Run.TextProperty, () => run.Text, value => run.SetCurrentValue(Run.TextProperty, value));
                    break;
                case SearchBox searchBox:
                    LocalizeStringProperty(searchBox, SearchBox.PlaceholderTextProperty, () => searchBox.PlaceholderText, value => searchBox.SetCurrentValue(SearchBox.PlaceholderTextProperty, value));
                    break;
                case HeaderedContentControl headeredContentControl:
                    LocalizeObjectProperty(headeredContentControl, HeaderedContentControl.HeaderProperty, () => headeredContentControl.Header, value => headeredContentControl.SetCurrentValue(HeaderedContentControl.HeaderProperty, value));
                    break;
                case HeaderedItemsControl headeredItemsControl:
                    LocalizeObjectProperty(headeredItemsControl, HeaderedItemsControl.HeaderProperty, () => headeredItemsControl.Header, value => headeredItemsControl.SetCurrentValue(HeaderedItemsControl.HeaderProperty, value));
                    break;
                case ContentControl contentControl:
                    LocalizeObjectProperty(contentControl, ContentControl.ContentProperty, () => contentControl.Content, value => contentControl.SetCurrentValue(ContentControl.ContentProperty, value));
                    break;
                case DataGridColumn dataGridColumn:
                    LocalizeObjectProperty(dataGridColumn, DataGridColumn.HeaderProperty, () => dataGridColumn.Header, value => dataGridColumn.SetCurrentValue(DataGridColumn.HeaderProperty, value));
                    break;
            }

            if (element is FrameworkElement frameworkElement)
            {
                LocalizeObjectProperty(frameworkElement, FrameworkElement.ToolTipProperty, () => frameworkElement.ToolTip, value => frameworkElement.SetCurrentValue(FrameworkElement.ToolTipProperty, value));
            }
        }

        private static void LocalizeStringProperty(
            DependencyObject element,
            DependencyProperty property,
            Func<string?> getValue,
            Action<string> setValue)
        {
            LocalizeText(element, property, getValue(), setValue);
        }

        private static void LocalizeObjectProperty(
            DependencyObject element,
            DependencyProperty property,
            Func<object?> getValue,
            Action<object?> setValue)
        {
            if (getValue() is string value)
            {
                LocalizeText(element, property, value, localizedValue => setValue(localizedValue));
            }
        }

        private static void LocalizeText(
            DependencyObject element,
            DependencyProperty property,
            string? currentValue,
            Action<string> setValue)
        {
            if (string.IsNullOrWhiteSpace(currentValue))
            {
                return;
            }

            LocalizedTextState state = GetTextState(element, property);
            if (state.IsUpdating)
            {
                return;
            }

            bool isCurrentPreviousLocalizedValue = state.HasOriginal &&
                string.Equals(currentValue, state.LastLocalizedValue, StringComparison.Ordinal);

            if (!state.HasOriginal || !isCurrentPreviousLocalizedValue)
            {
                state.OriginalValue = currentValue;
                state.HasOriginal = true;
            }

            string localizedValue = GetString(state.OriginalValue, state.OriginalValue);
            state.LastLocalizedValue = localizedValue;
            if (!string.Equals(currentValue, localizedValue, StringComparison.Ordinal))
            {
                state.IsUpdating = true;
                try
                {
                    setValue(localizedValue);
                }
                finally
                {
                    state.IsUpdating = false;
                }
            }
        }

        private static LocalizedTextState GetTextState(DependencyObject element, DependencyProperty property)
        {
            Dictionary<DependencyProperty, LocalizedTextState> states = OriginalTextValues.GetOrCreateValue(element);
            if (!states.TryGetValue(property, out LocalizedTextState? state))
            {
                state = new LocalizedTextState();
                states[property] = state;
            }

            return state;
        }

        private static void SubscribeTextChanged(DependencyObject element, DependencyProperty property)
        {
            LocalizedTextSubscriptions subscriptions = TextSubscriptions.GetOrCreateValue(element);
            if (!subscriptions.Properties.Add(property))
            {
                return;
            }

            DependencyPropertyDescriptor? descriptor = DependencyPropertyDescriptor.FromProperty(property, element.GetType());
            descriptor?.AddValueChanged(element, (_, _) => LocalizeObject(element));
        }

        private static void RebuildLocalizedTemplates()
        {
            List<LocalizedTemplate> templates = new();
            foreach (ResourceDictionary dictionary in EnumerateResourceDictionaries(Application.Current.Resources))
            {
                foreach (object key in dictionary.Keys)
                {
                    if (key is not string sourceTemplate ||
                        !sourceTemplate.Contains("{0}", StringComparison.Ordinal) ||
                        dictionary[key] is not string localizedTemplate)
                    {
                        continue;
                    }

                    templates.Add(new LocalizedTemplate(sourceTemplate, localizedTemplate, CreateTemplateRegex(sourceTemplate)));
                }
            }

            _localizedTemplates = templates
                .OrderByDescending(template => template.SourceTemplate.Length)
                .ToList();
        }

        private static IEnumerable<ResourceDictionary> EnumerateResourceDictionaries(ResourceDictionary dictionary)
        {
            yield return dictionary;

            foreach (ResourceDictionary mergedDictionary in dictionary.MergedDictionaries)
            {
                foreach (ResourceDictionary childDictionary in EnumerateResourceDictionaries(mergedDictionary))
                {
                    yield return childDictionary;
                }
            }
        }

        private static Regex CreateTemplateRegex(string sourceTemplate)
        {
            int currentIndex = 0;
            System.Text.StringBuilder patternBuilder = new();
            foreach (Match match in TemplatePlaceholderRegex.Matches(sourceTemplate))
            {
                patternBuilder.Append(Regex.Escape(sourceTemplate[currentIndex..match.Index]));
                patternBuilder.Append($"(?<p{match.Groups["index"].Value}>.+?)");
                currentIndex = match.Index + match.Length;
            }

            patternBuilder.Append(Regex.Escape(sourceTemplate[currentIndex..]));

            return new Regex($"^{patternBuilder}$", RegexOptions.Compiled);
        }

        private static string? TryGetStringFromTemplate(string sourceText)
        {
            foreach (LocalizedTemplate template in _localizedTemplates)
            {
                Match match = template.Regex.Match(sourceText);
                if (!match.Success)
                {
                    continue;
                }

                object?[] values = Enumerable.Range(0, template.PlaceholderCount)
                    .Select(index => (object?)match.Groups[$"p{index}"].Value)
                    .ToArray();

                return string.Format(CultureInfo.CurrentCulture, template.TargetTemplate, values);
            }

            return null;
        }

        private sealed class LocalizedTextState
        {
            public string OriginalValue { get; set; } = string.Empty;
            public string? LastLocalizedValue { get; set; }
            public bool HasOriginal { get; set; }
            public bool IsUpdating { get; set; }
        }

        private sealed class LocalizedTextSubscriptions
        {
            public HashSet<DependencyProperty> Properties { get; } = new();
        }

        private sealed class LocalizedTemplate
        {
            public LocalizedTemplate(string sourceTemplate, string localizedTemplate, Regex regex)
            {
                SourceTemplate = sourceTemplate;
                TargetTemplate = localizedTemplate;
                Regex = regex;
                PlaceholderCount = TemplatePlaceholderRegex.Matches(sourceTemplate)
                    .Select(match => int.Parse(match.Groups["index"].Value, CultureInfo.InvariantCulture))
                    .DefaultIfEmpty(-1)
                    .Max() + 1;
            }

            public string SourceTemplate { get; }
            public string TargetTemplate { get; }
            public Regex Regex { get; }
            public int PlaceholderCount { get; }
        }
    }
}
