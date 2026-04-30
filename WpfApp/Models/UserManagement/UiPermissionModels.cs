using System.Text.Json.Serialization;

namespace WpfApp.Models.UserManagement;

public enum UiPermissionNodeKind
{
    Page,
    Dialog,
    Button
}

public sealed class UiPermissionNodeDefinition
{
    public string Key { get; init; } = string.Empty;

    public string ParentKey { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public UiPermissionNodeKind Kind { get; init; }

    public string ScopeTypeName { get; init; } = string.Empty;

    public string ElementIdentifier { get; init; } = string.Empty;

    public string SourcePath { get; init; } = string.Empty;

    public int Order { get; init; }

    [JsonIgnore]
    public string KindDisplayName => Kind switch
    {
        UiPermissionNodeKind.Page => "界面",
        UiPermissionNodeKind.Dialog => "弹窗",
        UiPermissionNodeKind.Button => "按钮",
        _ => "节点"
    };
}

public sealed class UiPermissionCatalog
{
    public List<UiPermissionRoleConfig> Roles { get; set; } = new();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<UiPermissionLevelConfig>? Levels { get; set; }
}

public sealed class UiPermissionRoleConfig
{
    public string RoleId { get; set; } = string.Empty;

    public List<UiPermissionElementSetting> Items { get; set; } = new();
}

public sealed class UiPermissionLevelConfig
{
    public int Level { get; set; }

    public List<UiPermissionElementSetting> Items { get; set; } = new();
}

public sealed class UiPermissionElementSetting
{
    public string Key { get; set; } = string.Empty;

    public bool IsVisible { get; set; } = true;

    public bool IsEnabled { get; set; } = true;
}

public readonly record struct UiPermissionResolvedSetting(bool IsVisible, bool IsEnabled);

public static class UiPermissionKeys
{
    public static string Page(string scopeTypeName)
    {
        return $"page:{NormalizeIdentifier(scopeTypeName)}";
    }

    public static string Dialog(string scopeTypeName, string elementIdentifier)
    {
        return $"dialog:{NormalizeIdentifier(scopeTypeName)}:{NormalizeIdentifier(elementIdentifier)}";
    }

    public static string Button(string scopeTypeName, string elementIdentifier)
    {
        return $"button:{NormalizeIdentifier(scopeTypeName)}:{NormalizeIdentifier(elementIdentifier)}";
    }

    public static string NormalizeIdentifier(string? value)
    {
        string source = value?.Trim() ?? string.Empty;
        if (source.Length == 0)
        {
            return "unknown";
        }

        return string.Join(" ", source.Split(default(string[]), StringSplitOptions.RemoveEmptyEntries));
    }
}
