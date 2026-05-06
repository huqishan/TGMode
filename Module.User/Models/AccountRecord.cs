using ControlLibrary;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json.Serialization;

namespace Module.User.Models;

public sealed class AccountRecord : ViewModelProperties
{
    private string _id = string.Empty;
    private string _account = string.Empty;
    private string _name = string.Empty;
    private string _permissionId = AccountPermissionDisplay.DefaultEmployeePermissionId;
    private string _permissionDisplayName = string.Empty;
    private string _encryptedPassword = string.Empty;

    public string Id
    {
        get => _id;
        set => SetField(ref _id, value?.Trim() ?? string.Empty);
    }

    public string Account
    {
        get => _account;
        set => SetField(ref _account, value?.Trim() ?? string.Empty);
    }

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value?.Trim() ?? string.Empty);
    }

    public string PermissionId
    {
        get => _permissionId;
        set
        {
            string normalizedValue = value?.Trim() ?? string.Empty;
            if (SetField(ref _permissionId, normalizedValue))
            {
                _permissionDisplayName = string.Empty;
                OnPropertyChanged(nameof(PermissionDisplayName));
            }
        }
    }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AccountPermission? Permission
    {
        get => null;
        set
        {
            if (value.HasValue)
            {
                PermissionId = AccountPermissionDisplay.GetLegacyPermissionId(value.Value);
            }
        }
    }

    public string EncryptedPassword
    {
        get => _encryptedPassword;
        set => SetField(ref _encryptedPassword, value?.Trim() ?? string.Empty);
    }

    [JsonIgnore]
    public string PermissionDisplayName
    {
        get => string.IsNullOrWhiteSpace(_permissionDisplayName) ? PermissionId : _permissionDisplayName;
        set => SetField(ref _permissionDisplayName, value?.Trim() ?? string.Empty);
    }

    public AccountRecord Clone()
    {
        return new AccountRecord
        {
            Id = Id,
            Account = Account,
            Name = Name,
            PermissionId = PermissionId,
            PermissionDisplayName = PermissionDisplayName,
            EncryptedPassword = EncryptedPassword
        };
    }
}

public sealed class AccountCatalog : ViewModelProperties
{
    public ObservableCollection<AccountRecord> Accounts { get; set; } = new();

    public ObservableCollection<AccountPermissionProfile> Permissions { get; set; } = new();

    [JsonIgnore]
    public AccountRecord? FirstAccount => Accounts.FirstOrDefault();
}
