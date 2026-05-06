using ControlLibrary;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Module.User.Models;

public enum AccountPermission
{
    Administrator,
    Process,
    Employee
}

public sealed class AccountPermissionProfile : ViewModelProperties
{
    private string _id = string.Empty;
    private string _name = string.Empty;
    private int _level = AccountPermissionDisplay.LowestLevel;

    public string Id
    {
        get => _id;
        set => SetField(ref _id, value?.Trim() ?? string.Empty);
    }

    public string Name
    {
        get => _name;
        set
        {
            if (SetField(ref _name, value?.Trim() ?? string.Empty))
            {
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    public int Level
    {
        get => _level;
        set
        {
            if (SetField(ref _level, AccountPermissionDisplay.NormalizeLevel(value)))
            {
                OnPropertyChanged(nameof(LevelDisplayName));
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    [JsonIgnore]
    public string LevelDisplayName => $"{Level}级";

    [JsonIgnore]
    public string DisplayName => Name;

    public AccountPermissionProfile Clone()
    {
        return new AccountPermissionProfile
        {
            Id = Id,
            Name = Name,
            Level = Level
        };
    }
}

public sealed class AccountPermissionOption : ViewModelProperties
{
    public AccountPermissionOption(string id, string displayName, int level)
    {
        Id = id;
        DisplayName = displayName;
        Level = level;
    }

    public string Id { get; }

    public string DisplayName { get; }

    public int Level { get; }
}

public sealed class PermissionLevelOption : ViewModelProperties
{
    public PermissionLevelOption(int level)
    {
        Level = level;
        DisplayName = $"{level}级";
    }

    public int Level { get; }

    public string DisplayName { get; }
}

public static class AccountPermissionDisplay
{
    public const int SystemAdministratorLevel = 0;
    public const int HighestLevel = 1;
    public const int LowestLevel = 10;
    public const string BuiltInAdministratorPermissionId = "__builtin_admin_permission__";
    public const string DefaultEmployeePermissionId = "level-10";

    public static IReadOnlyList<PermissionLevelOption> LevelOptions { get; } =
        Enumerable.Range(HighestLevel, LowestLevel)
            .Select(level => new PermissionLevelOption(level))
            .ToList();

    public static IReadOnlyList<AccountPermissionOption> GetAssignableOptions(
        IEnumerable<AccountPermissionProfile> permissions,
        int currentLevel)
    {
        return permissions
            .Where(permission => CanManageLevel(currentLevel, permission.Level))
            .OrderBy(permission => permission.Level)
            .ThenBy(permission => permission.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(permission => new AccountPermissionOption(permission.Id, permission.DisplayName, permission.Level))
            .ToList();
    }

    public static bool CanManageLevel(int currentLevel, int targetLevel)
    {
        return NormalizeLevel(targetLevel) > NormalizeLevel(currentLevel);
    }

    public static bool CanConfigureUiPermissions(AuthenticatedUser? user)
    {
        return user is not null &&
               (user.IsBuiltIn || user.PermissionLevel <= HighestLevel);
    }

    public static string GetDisplayName(IEnumerable<AccountPermissionProfile> permissions, string? permissionId)
    {
        AccountPermissionProfile? permission = FindProfile(permissions, permissionId);
        return permission?.DisplayName ?? "未配置权限";
    }

    public static int GetPermissionLevel(IEnumerable<AccountPermissionProfile> permissions, string? permissionId)
    {
        AccountPermissionProfile? permission = FindProfile(permissions, permissionId);
        return permission?.Level ?? LowestLevel;
    }

    public static AccountPermissionProfile? FindProfile(
        IEnumerable<AccountPermissionProfile> permissions,
        string? permissionId)
    {
        if (string.IsNullOrWhiteSpace(permissionId))
        {
            return null;
        }

        return permissions.FirstOrDefault(permission =>
            string.Equals(permission.Id, permissionId.Trim(), StringComparison.Ordinal));
    }

    public static string GetLegacyPermissionId(AccountPermission permission)
    {
        return permission switch
        {
            AccountPermission.Administrator => BuildDefaultPermissionId(HighestLevel),
            AccountPermission.Process => BuildDefaultPermissionId(5),
            _ => DefaultEmployeePermissionId
        };
    }

    public static string BuildDefaultPermissionId(int level)
    {
        return $"level-{NormalizeLevel(level)}";
    }

    public static string BuildDefaultPermissionName(int level)
    {
        return $"{NormalizeLevel(level)}级权限";
    }

    public static bool TryGetDefaultPermissionLevel(string? permissionId, out int level)
    {
        level = LowestLevel;

        if (string.IsNullOrWhiteSpace(permissionId) ||
            !permissionId.Trim().StartsWith("level-", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string rawLevel = permissionId.Trim()["level-".Length..];
        return int.TryParse(rawLevel, out level) && IsValidLevel(level);
    }

    public static bool IsValidLevel(int level)
    {
        return level is >= HighestLevel and <= LowestLevel;
    }

    public static int NormalizeLevel(int level)
    {
        return Math.Clamp(level, HighestLevel, LowestLevel);
    }
}
