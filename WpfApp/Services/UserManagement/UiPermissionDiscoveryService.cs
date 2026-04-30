using ControlLibrary.Controls.Navigation.Models;
using System.IO;
using System.Xml.Linq;
using WpfApp.Models.UserManagement;

namespace WpfApp.Services.UserManagement;

public static class UiPermissionDiscoveryService
{
    private const string XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static readonly XName XClassAttribute = XName.Get("Class", XamlNamespace);
    private static readonly XName XNameAttribute = XName.Get("Name", XamlNamespace);

    public static IReadOnlyList<UiPermissionNodeDefinition> Discover(
        IEnumerable<ControlInfoDataItem>? navigationItems = null)
    {
        DirectoryInfo? solutionDirectory = FindSolutionDirectory();
        if (solutionDirectory is null)
        {
            return Array.Empty<UiPermissionNodeDefinition>();
        }

        Dictionary<string, NavigationPageInfo> navigationMap = BuildNavigationMap(navigationItems);
        HashSet<string> navigationScopeTypeNames = navigationMap.Keys.ToHashSet(StringComparer.Ordinal);
        bool restrictToNavigationScopes = navigationItems is not null;
        Dictionary<string, UiPermissionNodeDefinition> definitions = new(StringComparer.Ordinal);
        int fallbackPageOrder = 10000;

        foreach (string xamlFile in EnumerateXamlFiles(solutionDirectory.FullName))
        {
            XDocument document;
            try
            {
                document = XDocument.Load(xamlFile, LoadOptions.PreserveWhitespace);
            }
            catch
            {
                continue;
            }

            XElement? root = document.Root;
            string? scopeTypeName = root?.Attribute(XClassAttribute)?.Value.Trim();
            if (root is null ||
                string.IsNullOrWhiteSpace(scopeTypeName) ||
                !ShouldIncludeScope(
                    xamlFile,
                    scopeTypeName,
                    navigationScopeTypeNames,
                    restrictToNavigationScopes))
            {
                continue;
            }

            navigationMap.TryGetValue(scopeTypeName, out NavigationPageInfo navigationInfo);
            string relativePath = Path.GetRelativePath(solutionDirectory.FullName, xamlFile);
            int pageOrder = navigationInfo.Order > 0 ? navigationInfo.Order : fallbackPageOrder++;
            string pageKey = UiPermissionKeys.Page(scopeTypeName);

            AddDefinition(definitions, new UiPermissionNodeDefinition
            {
                Key = pageKey,
                DisplayName = navigationInfo.Title ?? BuildPageDisplayName(scopeTypeName),
                Kind = UiPermissionNodeKind.Page,
                ScopeTypeName = scopeTypeName,
                SourcePath = relativePath,
                Order = pageOrder * 1000
            });

            int elementOrder = 0;
            Dictionary<string, int> buttonOccurrences = new(StringComparer.Ordinal);
            WalkElement(
                root,
                scopeTypeName,
                pageKey,
                pageKey,
                relativePath,
                pageOrder,
                definitions,
                buttonOccurrences,
                ref elementOrder);
        }

        return definitions.Values
            .OrderBy(item => item.Order)
            .ThenBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static IReadOnlySet<string> DiscoverScopeTypeNames()
    {
        return Discover()
            .Where(item => item.Kind == UiPermissionNodeKind.Page)
            .Select(item => item.ScopeTypeName)
            .Where(scope => !string.IsNullOrWhiteSpace(scope))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static void WalkElement(
        XElement element,
        string scopeTypeName,
        string pageKey,
        string currentParentKey,
        string sourcePath,
        int pageOrder,
        IDictionary<string, UiPermissionNodeDefinition> definitions,
        IDictionary<string, int> buttonOccurrences,
        ref int elementOrder)
    {
        if (ShouldSkipSubtree(element))
        {
            return;
        }

        string parentKeyForChildren = currentParentKey;
        string? elementName = GetElementName(element);
        string localName = element.Name.LocalName;

        if (IsDialogElement(localName, elementName))
        {
            string dialogKey = UiPermissionKeys.Dialog(scopeTypeName, elementName ?? localName);
            AddDefinition(definitions, new UiPermissionNodeDefinition
            {
                Key = dialogKey,
                ParentKey = pageKey,
                DisplayName = BuildDialogDisplayName(elementName ?? localName),
                Kind = UiPermissionNodeKind.Dialog,
                ScopeTypeName = scopeTypeName,
                ElementIdentifier = elementName ?? localName,
                SourcePath = sourcePath,
                Order = pageOrder * 1000 + ++elementOrder
            });
            parentKeyForChildren = dialogKey;
        }

        if (IsButtonElement(localName))
        {
            string identifier = GetButtonIdentifier(element, elementOrder + 1, buttonOccurrences);
            string buttonKey = UiPermissionKeys.Button(scopeTypeName, identifier);
            AddDefinition(definitions, new UiPermissionNodeDefinition
            {
                Key = buttonKey,
                ParentKey = parentKeyForChildren,
                DisplayName = BuildButtonDisplayName(element, identifier),
                Kind = UiPermissionNodeKind.Button,
                ScopeTypeName = scopeTypeName,
                ElementIdentifier = identifier,
                SourcePath = sourcePath,
                Order = pageOrder * 1000 + ++elementOrder
            });
        }

        foreach (XElement child in element.Elements())
        {
            WalkElement(
                child,
                scopeTypeName,
                pageKey,
                parentKeyForChildren,
                sourcePath,
                pageOrder,
                definitions,
                buttonOccurrences,
                ref elementOrder);
        }
    }

    private static void AddDefinition(
        IDictionary<string, UiPermissionNodeDefinition> definitions,
        UiPermissionNodeDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.Key) || definitions.ContainsKey(definition.Key))
        {
            return;
        }

        definitions.Add(definition.Key, definition);
    }

    private static IEnumerable<string> EnumerateXamlFiles(string solutionDirectory)
    {
        return Directory.EnumerateFiles(solutionDirectory, "*.xaml", SearchOption.AllDirectories)
            .Where(file =>
            {
                string normalized = file.Replace(Path.DirectorySeparatorChar, '/');
                return !normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase) &&
                       !normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase) &&
                       !normalized.Contains("/.git/", StringComparison.OrdinalIgnoreCase) &&
                       !normalized.Contains("/.vs/", StringComparison.OrdinalIgnoreCase) &&
                       !normalized.Contains("/MigrationBackup/", StringComparison.OrdinalIgnoreCase);
            });
    }

    private static bool ShouldIncludeScope(
        string xamlFile,
        string scopeTypeName,
        ISet<string> navigationScopeTypeNames,
        bool restrictToNavigationScopes)
    {
        string normalized = xamlFile.Replace(Path.DirectorySeparatorChar, '/');
        if (normalized.Contains("/Resources/", StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith("/App.xaml", StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith("/MainWindow.xaml", StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith("/LoginWindow.xaml", StringComparison.OrdinalIgnoreCase) ||
            scopeTypeName.EndsWith(".ModernNavigationBar", StringComparison.Ordinal) ||
            scopeTypeName.EndsWith(".SearchBox", StringComparison.Ordinal))
        {
            return false;
        }

        if (restrictToNavigationScopes &&
            !navigationScopeTypeNames.Contains(scopeTypeName) &&
            !IsImplicitNavigationScope(scopeTypeName))
        {
            return false;
        }

        return true;
    }

    private static bool IsImplicitNavigationScope(string scopeTypeName)
    {
        return string.Equals(scopeTypeName, "WpfApp.SettingsView", StringComparison.Ordinal);
    }

    private static bool ShouldSkipSubtree(XElement element)
    {
        string localName = element.Name.LocalName;
        return localName.EndsWith(".Resources", StringComparison.Ordinal) ||
               localName is "ResourceDictionary" or "Style" or "ControlTemplate" or "DataTemplate"
                   or "HierarchicalDataTemplate" or "ItemsPanelTemplate";
    }

    private static bool IsButtonElement(string localName)
    {
        return localName is "Button" or "ToggleButton" or "RepeatButton" or "RadioButton";
    }

    private static bool IsDialogElement(string localName, string? elementName)
    {
        if (localName is "Popup" or "ContextMenu")
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(elementName))
        {
            return false;
        }

        string name = elementName.Trim();
        if (name.Contains("Backdrop", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("TranslateTransform", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("Sheet", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return name.EndsWith("DrawerHost", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith("DialogHost", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith("PopupHost", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Flyout", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Modal", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetButtonIdentifier(
        XElement element,
        int fallbackOrder,
        IDictionary<string, int> buttonOccurrences)
    {
        string? explicitName = GetElementName(element);
        if (!string.IsNullOrWhiteSpace(explicitName))
        {
            return explicitName;
        }

        string baseIdentifier = FirstMeaningfulValue(
                                    GetDisplayAttribute(element, "Content"),
                                    GetDisplayAttribute(element, "ToolTip"),
                                    GetDisplayAttribute(element, "Header"),
                                    GetFirstTextBlockText(element),
                                    GetDisplayAttribute(element, "Click")) ??
                                $"button-{fallbackOrder}";

        string normalizedIdentifier = UiPermissionKeys.NormalizeIdentifier(baseIdentifier);
        buttonOccurrences.TryGetValue(normalizedIdentifier, out int occurrence);
        occurrence++;
        buttonOccurrences[normalizedIdentifier] = occurrence;

        return occurrence == 1
            ? baseIdentifier
            : $"{baseIdentifier}#{occurrence}";
    }

    private static string BuildButtonDisplayName(XElement element, string identifier)
    {
        string? displayText = FirstMeaningfulValue(
            GetDisplayAttribute(element, "Content"),
            GetDisplayAttribute(element, "ToolTip"),
            GetDisplayAttribute(element, "Header"),
            GetFirstTextBlockText(element),
            GetElementName(element));

        return string.IsNullOrWhiteSpace(displayText)
            ? $"按钮 {identifier}"
            : displayText;
    }

    private static string BuildDialogDisplayName(string identifier)
    {
        string name = identifier
            .Replace("DrawerHost", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("DialogHost", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("PopupHost", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("Flyout", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();

        return string.IsNullOrWhiteSpace(name)
            ? $"弹窗 {identifier}"
            : $"弹窗 {name}";
    }

    private static string BuildPageDisplayName(string scopeTypeName)
    {
        string name = scopeTypeName.Split('.').LastOrDefault() ?? scopeTypeName;
        foreach (string suffix in new[] { "View", "Window", "Control" })
        {
            if (name.EndsWith(suffix, StringComparison.Ordinal) && name.Length > suffix.Length)
            {
                name = name[..^suffix.Length];
                break;
            }
        }

        return name;
    }

    private static string? GetElementName(XElement element)
    {
        return FirstMeaningfulValue(
            element.Attribute(XNameAttribute)?.Value,
            element.Attribute("Name")?.Value);
    }

    private static string? GetDisplayAttribute(XElement element, string attributeName)
    {
        string? value = element.Attribute(attributeName)?.Value;
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string trimmed = value.Trim();
        return trimmed.StartsWith('{') ? null : trimmed;
    }

    private static string? GetFirstTextBlockText(XElement element)
    {
        return element
            .Descendants()
            .Where(child => child.Name.LocalName == "TextBlock")
            .Select(child => GetDisplayAttribute(child, "Text"))
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    private static string? FirstMeaningfulValue(params string?[] values)
    {
        return values
            .Select(value => value?.Trim())
            .FirstOrDefault(IsMeaningfulDisplayValue);
    }

    private static bool IsMeaningfulDisplayValue(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
               !string.Equals(value, "...", StringComparison.Ordinal) &&
               !string.Equals(value, "…", StringComparison.Ordinal);
    }

    private static Dictionary<string, NavigationPageInfo> BuildNavigationMap(
        IEnumerable<ControlInfoDataItem>? navigationItems)
    {
        Dictionary<string, NavigationPageInfo> map = new(StringComparer.Ordinal);
        if (navigationItems is null)
        {
            return map;
        }

        int order = 1;
        foreach (ControlInfoDataItem item in FlattenNavigationItems(navigationItems))
        {
            string? typeName = ParseContentTypeName(item.Content);
            if (string.IsNullOrWhiteSpace(typeName) || map.ContainsKey(typeName))
            {
                continue;
            }

            map[typeName] = new NavigationPageInfo(item.Title, order++);
        }

        return map;
    }

    private static IEnumerable<ControlInfoDataItem> FlattenNavigationItems(IEnumerable<ControlInfoDataItem> items)
    {
        foreach (ControlInfoDataItem item in items)
        {
            yield return item;

            foreach (ControlInfoDataItem child in FlattenNavigationItems(item.Items))
            {
                yield return child;
            }
        }
    }

    private static string? ParseContentTypeName(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        string typeName = content.Split(',')[0].Trim();
        return typeName.Contains('.', StringComparison.Ordinal) ? typeName : null;
    }

    private static DirectoryInfo? FindSolutionDirectory()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "WpfApp.sln")))
            {
                return directory;
            }

            directory = directory.Parent;
        }

        directory = new(Environment.CurrentDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "WpfApp.sln")))
            {
                return directory;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private readonly record struct NavigationPageInfo(string? Title, int Order);
}
