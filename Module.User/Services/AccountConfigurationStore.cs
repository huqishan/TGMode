using Module.User.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Module.User.Services;

/// <summary>
/// 账号配置存储服务，负责账号 JSON 读写、密码加密和登录校验。
/// </summary>
public static class AccountConfigurationStore
{
    #region 配置常量与序列化字段

    public const string BuiltInAdminAccount = "10086";
    public const string BuiltInAdminPassword = "10086";

    private static readonly string ConfigDirectory =
        Path.Combine(AppContext.BaseDirectory, "Config", "UserManagement");

    private static readonly string ConfigFilePath =
        Path.Combine(ConfigDirectory, "Accounts.json");

    private static readonly byte[] PasswordEntropy =
        Encoding.UTF8.GetBytes("WpfApp.UserManagement.AccountPassword.v1");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    #endregion

    #region 账号配置读写

    /// <summary>
    /// 加载账号配置，并对缺失字段、重复账号和角色引用做规范化处理。
    /// </summary>
    public static AccountCatalog LoadCatalog()
    {
        if (!File.Exists(ConfigFilePath))
        {
            return NormalizeCatalog(new AccountCatalog());
        }

        try
        {
            string json = File.ReadAllText(ConfigFilePath);
            AccountCatalog? catalog = JsonSerializer.Deserialize<AccountCatalog>(json, JsonOptions);
            return NormalizeCatalog(catalog);
        }
        catch
        {
            return NormalizeCatalog(new AccountCatalog());
        }
    }

    /// <summary>
    /// 保存账号配置，保存前会统一清洗账号和角色数据。
    /// </summary>
    public static void SaveCatalog(AccountCatalog catalog)
    {
        AccountCatalog normalized = NormalizeCatalog(catalog);
        Directory.CreateDirectory(ConfigDirectory);
        string json = JsonSerializer.Serialize(normalized, JsonOptions);
        File.WriteAllText(ConfigFilePath, json);
    }

    #endregion

    #region 登录认证与账号校验

    /// <summary>
    /// 使用当前 Windows 用户作用域加密账号密码。
    /// </summary>
    public static string EncryptPassword(string password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return string.Empty;
        }

        byte[] plainBytes = Encoding.UTF8.GetBytes(password);
        byte[] encryptedBytes = ProtectedData.Protect(
            plainBytes,
            PasswordEntropy,
            DataProtectionScope.CurrentUser);

        return Convert.ToBase64String(encryptedBytes);
    }

    /// <summary>
    /// 校验账号密码，成功时输出当前登录用户信息。
    /// </summary>
    public static bool TryAuthenticate( string account, string password, out AuthenticatedUser? user, out string message)
    {
        user = null;
        string trimmedAccount = account.Trim();

        if (string.IsNullOrWhiteSpace(trimmedAccount) || string.IsNullOrWhiteSpace(password))
        {
            message = "请输入账号和密码";
            return false;
        }

        if (string.Equals(trimmedAccount, BuiltInAdminAccount, StringComparison.OrdinalIgnoreCase))
        {
            if (SecureEquals(password, BuiltInAdminPassword))
            {
                user = new AuthenticatedUser(
                    "__builtin_admin__",
                    BuiltInAdminAccount,
                    "内置管理员",
                    AccountPermissionDisplay.BuiltInAdministratorPermissionId,
                    AccountPermissionDisplay.SystemAdministratorLevel,
                    "内置管理员",
                    isBuiltIn: true);
                message = "登录成功";
                return true;
            }

            message = "账号或密码错误";
            return false;
        }

        AccountCatalog catalog = LoadCatalog();
        AccountRecord? accountRecord = catalog.Accounts.FirstOrDefault(existing =>
            string.Equals(existing.Account, trimmedAccount, StringComparison.OrdinalIgnoreCase));

        if (accountRecord is null || !VerifyPassword(password, accountRecord.EncryptedPassword))
        {
            message = "账号或密码错误";
            return false;
        }

        user = new AuthenticatedUser(
            accountRecord.Id,
            accountRecord.Account,
            accountRecord.Name,
            accountRecord.PermissionId,
            AccountPermissionDisplay.GetPermissionLevel(catalog.Permissions, accountRecord.PermissionId),
            AccountPermissionDisplay.GetDisplayName(catalog.Permissions, accountRecord.PermissionId),
            isBuiltIn: false);
        message = "登录成功";
        return true;
    }

    /// <summary>
    /// 判断账号是否已存在，同时保留内置管理员账号不可复用。
    /// </summary>
    public static bool HasDuplicateAccount(
        AccountCatalog catalog,
        string account,
        string? ignoreAccountId = null)
    {
        if (IsReservedBuiltInAccount(account))
        {
            return true;
        }

        return catalog.Accounts.Any(existing =>
            !string.Equals(existing.Id, ignoreAccountId, StringComparison.Ordinal) &&
            string.Equals(existing.Account, account.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 判断账号是否为系统保留的内置管理员账号。
    /// </summary>
    public static bool IsReservedBuiltInAccount(string account)
    {
        return string.Equals(account.Trim(), BuiltInAdminAccount, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region 配置规范化

    private static AccountCatalog NormalizeCatalog(AccountCatalog? catalog)
    {
        AccountCatalog normalized = new()
        {
            Accounts = new ObservableCollection<AccountRecord>(
                (catalog?.Accounts ?? new ObservableCollection<AccountRecord>())
                    .Where(account => account is not null)
                    .Select(account => account.Clone()))
        };

        HashSet<string> usedIds = new(StringComparer.Ordinal);
        HashSet<string> usedAccounts = new(StringComparer.OrdinalIgnoreCase)
        {
            BuiltInAdminAccount
        };
        int index = 1;

        foreach (AccountRecord account in normalized.Accounts)
        {
            if (string.IsNullOrWhiteSpace(account.Id) || !usedIds.Add(account.Id))
            {
                account.Id = Guid.NewGuid().ToString("N");
                usedIds.Add(account.Id);
            }

            string fallbackAccount = $"user{index:000}";
            account.Account = BuildUniqueAccount(
                string.IsNullOrWhiteSpace(account.Account) ? fallbackAccount : account.Account,
                usedAccounts);

            if (string.IsNullOrWhiteSpace(account.Name))
            {
                account.Name = account.Account;
            }

            if (string.IsNullOrWhiteSpace(account.PermissionId))
            {
                account.PermissionId = AccountPermissionDisplay.DefaultEmployeePermissionId;
            }

            account.EncryptedPassword = account.EncryptedPassword?.Trim() ?? string.Empty;
            index++;
        }

        HashSet<string> usedPermissionIds = new(
            normalized.Accounts
                .Select(account => account.PermissionId)
                .Where(permissionId => !string.IsNullOrWhiteSpace(permissionId))
                .Select(permissionId => permissionId!),
            StringComparer.Ordinal);

        normalized.Permissions = NormalizePermissions(catalog?.Permissions, usedPermissionIds);

        foreach (AccountRecord account in normalized.Accounts)
        {
            if (AccountPermissionDisplay.FindProfile(normalized.Permissions, account.PermissionId) is null)
            {
                AccountPermissionProfile fallbackPermission = EnsureMissingPermission(
                    normalized.Permissions,
                    account.PermissionId);
                account.PermissionId = fallbackPermission.Id;
            }

            account.PermissionDisplayName = AccountPermissionDisplay.GetDisplayName(normalized.Permissions, account.PermissionId);
        }

        return normalized;
    }

    private static ObservableCollection<AccountPermissionProfile> NormalizePermissions(
        IEnumerable<AccountPermissionProfile>? sourcePermissions,
        ISet<string> usedPermissionIds)
    {
        List<AccountPermissionProfile> source = (sourcePermissions ?? Array.Empty<AccountPermissionProfile>())
            .Where(permission => permission is not null)
            .Select(permission => permission.Clone())
            .ToList();

        HashSet<string> usedIds = new(StringComparer.Ordinal);
        List<AccountPermissionProfile> normalized = new();

        foreach (AccountPermissionProfile permission in source)
        {
            int level = AccountPermissionDisplay.NormalizeLevel(permission.Level);
            string id = permission.Id?.Trim() ?? string.Empty;
            string name = permission.Name?.Trim() ?? string.Empty;

            if (IsUnusedGeneratedPermission(id, name, usedPermissionIds))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(id) || !usedIds.Add(id))
            {
                id = $"permission-{Guid.NewGuid():N}";
                usedIds.Add(id);
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                name = AccountPermissionDisplay.BuildDefaultPermissionName(level);
            }

            normalized.Add(new AccountPermissionProfile
            {
                Id = id,
                Name = name,
                Level = level
            });
        }

        return new ObservableCollection<AccountPermissionProfile>(
            normalized
                .OrderBy(permission => permission.Level)
                .ThenBy(permission => permission.Name, StringComparer.CurrentCultureIgnoreCase));
    }

    private static bool IsUnusedGeneratedPermission(
        string permissionId,
        string permissionName,
        ISet<string> usedPermissionIds)
    {
        return AccountPermissionDisplay.TryGetDefaultPermissionLevel(permissionId, out int level) &&
               !usedPermissionIds.Contains(permissionId) &&
               string.Equals(
                   permissionName,
                   AccountPermissionDisplay.BuildDefaultPermissionName(level),
                   StringComparison.Ordinal);
    }

    private static AccountPermissionProfile EnsureMissingPermission(
        ObservableCollection<AccountPermissionProfile> permissions,
        string permissionId)
    {
        AccountPermissionProfile? existingPermission = AccountPermissionDisplay.FindProfile(permissions, permissionId);
        if (existingPermission is not null)
        {
            return existingPermission;
        }

        int level = AccountPermissionDisplay.LowestLevel;
        string name = "未配置权限";
        string id = string.IsNullOrWhiteSpace(permissionId)
            ? AccountPermissionDisplay.DefaultEmployeePermissionId
            : permissionId.Trim();

        if (AccountPermissionDisplay.TryGetDefaultPermissionLevel(id, out int legacyLevel))
        {
            level = legacyLevel;
            name = AccountPermissionDisplay.BuildDefaultPermissionName(legacyLevel);
        }

        AccountPermissionProfile missingPermission = new()
        {
            Id = id,
            Name = name,
            Level = level
        };

        permissions.Add(missingPermission);
        return missingPermission;
    }

    #endregion

    #region 密码安全校验

    private static bool VerifyPassword(string password, string encryptedPassword)
    {
        if (string.IsNullOrWhiteSpace(encryptedPassword))
        {
            return false;
        }

        byte[]? plainBytes = null;
        byte[] inputBytes = Encoding.UTF8.GetBytes(password);

        try
        {
            byte[] encryptedBytes = Convert.FromBase64String(encryptedPassword);
            plainBytes = ProtectedData.Unprotect(
                encryptedBytes,
                PasswordEntropy,
                DataProtectionScope.CurrentUser);

            return plainBytes.Length == inputBytes.Length &&
                   CryptographicOperations.FixedTimeEquals(plainBytes, inputBytes);
        }
        catch
        {
            return false;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(inputBytes);
            if (plainBytes is not null)
            {
                CryptographicOperations.ZeroMemory(plainBytes);
            }
        }
    }

    private static bool SecureEquals(string left, string right)
    {
        byte[] leftBytes = Encoding.UTF8.GetBytes(left);
        byte[] rightBytes = Encoding.UTF8.GetBytes(right);

        try
        {
            return leftBytes.Length == rightBytes.Length &&
                   CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(leftBytes);
            CryptographicOperations.ZeroMemory(rightBytes);
        }
    }

    #endregion

    #region 账号名称生成

    private static string BuildUniqueAccount(string sourceAccount, HashSet<string> usedAccounts)
    {
        string baseAccount = string.IsNullOrWhiteSpace(sourceAccount)
            ? "user"
            : sourceAccount.Trim();

        string account = baseAccount;
        for (int index = 2; !usedAccounts.Add(account); index++)
        {
            account = $"{baseAccount}{index}";
        }

        return account;
    }

    #endregion
}
