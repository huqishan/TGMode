using System.IO;
using Module.User.Models;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Module.User.Services;

/// <summary>
/// 界面权限配置存储服务，负责角色权限 JSON 的读写、迁移和规范化。
/// </summary>
public static class UiPermissionConfigurationStore
{
    #region 配置路径与序列化字段

    private static readonly string ConfigDirectory =
        Path.Combine(AppContext.BaseDirectory, "Config", "UserManagement");

    private static readonly string ConfigFilePath =
        Path.Combine(ConfigDirectory, "UiPermissions.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    #endregion

    #region 权限配置读写

    /// <summary>
    /// 加载界面权限配置，并兼容旧版按权限等级保存的数据。
    /// </summary>
    public static UiPermissionCatalog LoadCatalog()
    {
        if (!File.Exists(ConfigFilePath))
        {
            return NormalizeCatalog(new UiPermissionCatalog());
        }

        try
        {
            string json = File.ReadAllText(ConfigFilePath);
            UiPermissionCatalog? catalog = JsonSerializer.Deserialize<UiPermissionCatalog>(json, JsonOptions);
            return NormalizeCatalog(catalog);
        }
        catch
        {
            return NormalizeCatalog(new UiPermissionCatalog());
        }
    }

    /// <summary>
    /// 保存界面权限配置，保存前会统一清洗重复或空白节点。
    /// </summary>
    public static void SaveCatalog(UiPermissionCatalog catalog)
    {
        UiPermissionCatalog normalized = NormalizeCatalog(catalog);
        Directory.CreateDirectory(ConfigDirectory);
        string json = JsonSerializer.Serialize(normalized, JsonOptions);
        File.WriteAllText(ConfigFilePath, json);
    }

    #endregion

    #region 角色权限查询与保存

    /// <summary>
    /// 获取指定角色和界面元素的最终可见、可用配置。
    /// </summary>
    public static UiPermissionResolvedSetting GetSetting(string roleId, string key)
    {
        UiPermissionCatalog catalog = LoadCatalog();
        UiPermissionElementSetting? setting = catalog.Roles
            .FirstOrDefault(item => string.Equals(item.RoleId, NormalizeRoleId(roleId), StringComparison.Ordinal))
            ?.Items
            .FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.Ordinal));

        return setting is null
            ? new UiPermissionResolvedSetting(false, false)
            : new UiPermissionResolvedSetting(setting.IsVisible, setting.IsEnabled);
    }

    /// <summary>
    /// 获取指定角色的权限节点映射，便于 ViewModel 快速构造权限树。
    /// </summary>
    public static Dictionary<string, UiPermissionElementSetting> GetRoleSettingMap(
        UiPermissionCatalog catalog,
        string? roleId)
    {
        string normalizedRoleId = NormalizeRoleId(roleId);
        if (string.IsNullOrWhiteSpace(normalizedRoleId))
        {
            return new Dictionary<string, UiPermissionElementSetting>(StringComparer.Ordinal);
        }

        UiPermissionRoleConfig roleConfig = EnsureRoleConfig(catalog, normalizedRoleId);
        return roleConfig.Items
            .Where(item => !string.IsNullOrWhiteSpace(item.Key))
            .GroupBy(item => item.Key.Trim(), StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Last(),
                StringComparer.Ordinal);
    }

    /// <summary>
    /// 保存单个角色的权限节点配置。
    /// </summary>
    public static void SaveRoleSettings(
        UiPermissionCatalog catalog,
        string roleId,
        IEnumerable<UiPermissionElementSetting> settings)
    {
        string normalizedRoleId = NormalizeRoleId(roleId);
        if (string.IsNullOrWhiteSpace(normalizedRoleId))
        {
            return;
        }

        UiPermissionRoleConfig roleConfig = EnsureRoleConfig(catalog, normalizedRoleId);
        roleConfig.Items = NormalizeItems(settings);
        SaveCatalog(catalog);
    }

    #endregion

    #region 配置规范化

    private static UiPermissionCatalog NormalizeCatalog(UiPermissionCatalog? catalog)
    {
        UiPermissionCatalog normalized = new();
        Dictionary<string, AccountPermissionProfile> roleLookup = LoadRoleLookup();
        Dictionary<int, List<UiPermissionElementSetting>> legacyLevelItems =
            NormalizeLegacyLevelItems(catalog?.Levels);

        foreach (UiPermissionRoleConfig sourceRole in catalog?.Roles ?? new List<UiPermissionRoleConfig>())
        {
            string roleId = NormalizeRoleId(sourceRole.RoleId);
            if (string.IsNullOrWhiteSpace(roleId))
            {
                continue;
            }

            UiPermissionRoleConfig targetRole = EnsureRoleConfig(normalized, roleId);
            targetRole.Items = NormalizeItems(sourceRole.Items);
        }

        foreach (AccountPermissionProfile role in roleLookup.Values)
        {
            UiPermissionRoleConfig targetRole = EnsureRoleConfig(normalized, role.Id);
            if (targetRole.Items.Count == 0 &&
                legacyLevelItems.TryGetValue(AccountPermissionDisplay.NormalizeLevel(role.Level), out List<UiPermissionElementSetting>? items))
            {
                targetRole.Items = CloneItems(items);
            }
        }

        normalized.Roles = normalized.Roles
            .OrderBy(item => GetRoleSortLevel(roleLookup, item.RoleId))
            .ThenBy(item => GetRoleSortName(roleLookup, item.RoleId), StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.RoleId, StringComparer.Ordinal)
            .ToList();
        normalized.Levels = null;
        return normalized;
    }

    private static List<UiPermissionElementSetting> NormalizeItems(
        IEnumerable<UiPermissionElementSetting>? settings)
    {
        return (settings ?? Array.Empty<UiPermissionElementSetting>())
            .Where(item => !string.IsNullOrWhiteSpace(item.Key))
            .GroupBy(item => item.Key.Trim(), StringComparer.Ordinal)
            .Select(group =>
            {
                UiPermissionElementSetting setting = group.Last();
                return new UiPermissionElementSetting
                {
                    Key = setting.Key.Trim(),
                    IsVisible = setting.IsVisible,
                    IsEnabled = setting.IsEnabled
                };
            })
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .ToList();
    }

    private static Dictionary<int, List<UiPermissionElementSetting>> NormalizeLegacyLevelItems(
        IEnumerable<UiPermissionLevelConfig>? levels)
    {
        Dictionary<int, List<UiPermissionElementSetting>> itemsByLevel = new();
        foreach (UiPermissionLevelConfig sourceLevel in levels ?? Array.Empty<UiPermissionLevelConfig>())
        {
            itemsByLevel[AccountPermissionDisplay.NormalizeLevel(sourceLevel.Level)] =
                NormalizeItems(sourceLevel.Items);
        }

        return itemsByLevel;
    }

    private static UiPermissionRoleConfig EnsureRoleConfig(UiPermissionCatalog catalog, string roleId)
    {
        string normalizedRoleId = NormalizeRoleId(roleId);
        UiPermissionRoleConfig? roleConfig = catalog.Roles.FirstOrDefault(item =>
            string.Equals(item.RoleId, normalizedRoleId, StringComparison.Ordinal));
        if (roleConfig is not null)
        {
            return roleConfig;
        }

        roleConfig = new UiPermissionRoleConfig
        {
            RoleId = normalizedRoleId
        };
        catalog.Roles.Add(roleConfig);
        return roleConfig;
    }

    private static List<UiPermissionElementSetting> CloneItems(IEnumerable<UiPermissionElementSetting> items)
    {
        return items
            .Select(item => new UiPermissionElementSetting
            {
                Key = item.Key,
                IsVisible = item.IsVisible,
                IsEnabled = item.IsEnabled
            })
            .ToList();
    }

    #endregion

    #region 角色排序与标识

    private static Dictionary<string, AccountPermissionProfile> LoadRoleLookup()
    {
        try
        {
            return AccountConfigurationStore.LoadCatalog()
                .Permissions
                .Where(role => !string.IsNullOrWhiteSpace(role.Id))
                .GroupBy(role => role.Id.Trim(), StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
        }
        catch
        {
            return new Dictionary<string, AccountPermissionProfile>(StringComparer.Ordinal);
        }
    }

    private static int GetRoleSortLevel(
        IReadOnlyDictionary<string, AccountPermissionProfile> roleLookup,
        string roleId)
    {
        return roleLookup.TryGetValue(roleId, out AccountPermissionProfile? role)
            ? role.Level
            : AccountPermissionDisplay.LowestLevel + 1;
    }

    private static string GetRoleSortName(
        IReadOnlyDictionary<string, AccountPermissionProfile> roleLookup,
        string roleId)
    {
        return roleLookup.TryGetValue(roleId, out AccountPermissionProfile? role)
            ? role.Name
            : roleId;
    }

    private static string NormalizeRoleId(string? roleId)
    {
        return roleId?.Trim() ?? string.Empty;
    }

    #endregion
}
