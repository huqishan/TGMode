namespace WpfApp.Models.UserManagement;

public sealed class AuthenticatedUser
{
    public AuthenticatedUser(
        string id,
        string account,
        string name,
        string permissionId,
        int permissionLevel,
        string permissionDisplayName,
        bool isBuiltIn)
    {
        Id = id;
        Account = account;
        Name = name;
        PermissionId = permissionId;
        PermissionLevel = isBuiltIn ? AccountPermissionDisplay.SystemAdministratorLevel : AccountPermissionDisplay.NormalizeLevel(permissionLevel);
        PermissionDisplayName = permissionDisplayName;
        IsBuiltIn = isBuiltIn;
    }

    public string Id { get; }

    public string Account { get; }

    public string Name { get; }

    public string PermissionId { get; }

    public int PermissionLevel { get; }

    public string PermissionDisplayName { get; }

    public bool IsBuiltIn { get; }
}
