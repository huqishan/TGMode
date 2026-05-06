using ControlLibrary;
using Module.User.Models;
using Module.User.Services;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Media;

namespace Module.User.ViewModels;

/// <summary>
/// 账号管理界面的命令实现和业务方法，供 XAML Command 绑定调用。
/// </summary>
public sealed partial class AccountManagementViewModel
{
    #region 命令初始化

    /// <summary>
    /// 初始化账号管理界面所有可绑定命令。
    /// </summary>
    private void InitializeCommands()
    {
        NewAccountCommand = new RelayCommand(_ => NewAccount());
        SaveAccountCommand = new RelayCommand(_ => SaveEditingAccount());
        DeleteAccountCommand = new RelayCommand(_ => DeleteSelectedAccount());
    }

    private void NewAccount()
    {
        BeginNewAccount();
        SetPageStatus("正在新增账号", NeutralBrush);
    }

    #endregion

    #region 账号保存与删除方法

    /// <summary>
    /// 校验编辑区数据并保存新增或修改后的账号。
    /// </summary>
    private void SaveEditingAccount()
    {
        string account = EditingAccount.Trim();
        string name = EditingName.Trim();
        AccountRecord? existingAccount = string.IsNullOrWhiteSpace(_editingAccountId)
            ? null
            : _catalog.Accounts.FirstOrDefault(item => string.Equals(item.Id, _editingAccountId, StringComparison.Ordinal));

        bool isNew = existingAccount is null;

        if (string.IsNullOrWhiteSpace(account))
        {
            SetPageStatus("账号不能为空", WarningBrush);
            return;
        }

        if (AccountConfigurationStore.IsReservedBuiltInAccount(account))
        {
            SetPageStatus("账号 10086 是内置管理员账号，不能新增或修改为该账号", WarningBrush);
            return;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            SetPageStatus("姓名不能为空", WarningBrush);
            return;
        }

        if (AccountPermissionDisplay.FindProfile(_catalog.Permissions, EditingPermissionId) is null)
        {
            SetPageStatus("请选择有效角色", WarningBrush);
            return;
        }

        if (!CanManagePermission(EditingPermissionId))
        {
            SetPageStatus($"当前登录权限不能新增或修改为：{AccountPermissionDisplay.GetDisplayName(_catalog.Permissions, EditingPermissionId)}", WarningBrush);
            return;
        }

        if (!isNew && !CanManageAccount(existingAccount!))
        {
            SetPageStatus("当前登录权限不能修改该账号", WarningBrush);
            return;
        }

        if (AccountConfigurationStore.HasDuplicateAccount(_catalog, account, existingAccount?.Id))
        {
            SetPageStatus($"账号已存在：{account}", WarningBrush);
            return;
        }

        if (isNew && string.IsNullOrWhiteSpace(_editingPassword))
        {
            SetPageStatus("新增账号需要填写密码", WarningBrush);
            return;
        }

        if (!isNew &&
            string.IsNullOrWhiteSpace(existingAccount!.EncryptedPassword) &&
            string.IsNullOrWhiteSpace(_editingPassword))
        {
            SetPageStatus("当前账号还没有密码，请填写后保存", WarningBrush);
            return;
        }

        string? encryptedPassword = null;
        if (isNew || !string.IsNullOrWhiteSpace(_editingPassword))
        {
            encryptedPassword = AccountConfigurationStore.EncryptPassword(_editingPassword);
        }

        AccountRecord accountRecord = existingAccount ?? new AccountRecord
        {
            Id = Guid.NewGuid().ToString("N")
        };

        accountRecord.Account = account;
        accountRecord.Name = name;
        accountRecord.PermissionId = EditingPermissionId;
        accountRecord.PermissionDisplayName = AccountPermissionDisplay.GetDisplayName(_catalog.Permissions, EditingPermissionId);

        if (encryptedPassword is not null)
        {
            accountRecord.EncryptedPassword = encryptedPassword;
        }

        if (isNew)
        {
            _catalog.Accounts.Add(accountRecord);
        }

        string savedId = accountRecord.Id;
        AccountConfigurationStore.SaveCatalog(_catalog);
        LoadCatalog(AccountConfigurationStore.LoadCatalog(), savedId);
        SetPageStatus($"{(isNew ? "已新增" : "已修改")}账号：{account}", SuccessBrush);
    }

    /// <summary>
    /// 删除当前选中的可管理账号。
    /// </summary>
    private void DeleteSelectedAccount()
    {
        AccountRecord? accountRecord = SelectedAccount ??
            _catalog.Accounts.FirstOrDefault(item => string.Equals(item.Id, _editingAccountId, StringComparison.Ordinal));

        if (accountRecord is null)
        {
            SetPageStatus("请先选择要删除的账号", WarningBrush);
            return;
        }

        if (!CanManageAccount(accountRecord))
        {
            SetPageStatus("当前登录权限不能删除该账号", WarningBrush);
            return;
        }

        int removedIndex = Math.Max(0, Accounts.IndexOf(accountRecord));
        string deletedAccount = accountRecord.Account;
        _catalog.Accounts.Remove(accountRecord);
        string? nextId = Accounts.Count == 0
            ? null
            : Accounts[Math.Clamp(removedIndex, 0, Accounts.Count - 1)].Id;

        AccountConfigurationStore.SaveCatalog(_catalog);
        LoadCatalog(AccountConfigurationStore.LoadCatalog(), nextId);
        SetPageStatus($"已删除账号：{deletedAccount}", WarningBrush);
    }

    #endregion

    #region 配置加载与编辑器方法

    /// <summary>
    /// 载入账号配置，并刷新列表、角色选项和编辑区。
    /// </summary>
    private void LoadCatalog(
        AccountCatalog catalog,
        string? preferredAccountId = null)
    {
        UnhookCatalog();
        _catalog = catalog;
        RefreshAccountPermissionDisplayNames();
        HookCatalog();
        RefreshPermissionOptions();
        RefreshVisibleAccounts(preferredAccountId);
    }

    private void RefreshVisibleAccounts(string? preferredAccountId = null)
    {
        _visibleAccounts.Clear();

        foreach (AccountRecord account in _catalog.Accounts.Where(CanManageAccount))
        {
            _visibleAccounts.Add(account);
        }

        OnPropertyChanged(nameof(Accounts));
        NotifyAccountCountChanged();

        AccountRecord? preferredAccount =
            _visibleAccounts.FirstOrDefault(account => string.Equals(account.Id, preferredAccountId, StringComparison.Ordinal)) ??
            _visibleAccounts.FirstOrDefault();

        SetSelectedAccountSilently(preferredAccount);
        if (preferredAccount is null)
        {
            BeginNewAccount(clearSelection: false);
            return;
        }

        LoadAccountIntoEditor(preferredAccount);
    }

    private void RefreshPermissionOptions()
    {
        string currentEditingPermissionId = EditingPermissionId;
        PermissionOptions.Clear();

        IReadOnlyList<AccountPermissionOption> assignableOptions = _catalog.Permissions
            .Where(CanManagePermission)
            .OrderBy(permission => permission.Level)
            .ThenBy(permission => permission.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(permission => new AccountPermissionOption(permission.Id, permission.DisplayName, permission.Level))
            .ToList();

        foreach (AccountPermissionOption option in assignableOptions)
        {
            PermissionOptions.Add(option);
        }

        if (PermissionOptions.Any(option => string.Equals(option.Id, currentEditingPermissionId, StringComparison.Ordinal)))
        {
            EditingPermissionId = currentEditingPermissionId;
            return;
        }

        EditingPermissionId = PermissionOptions.LastOrDefault()?.Id ?? string.Empty;
    }

    private void RefreshAccountPermissionDisplayNames()
    {
        foreach (AccountRecord account in _catalog.Accounts)
        {
            account.PermissionDisplayName = AccountPermissionDisplay.GetDisplayName(_catalog.Permissions, account.PermissionId);
        }
    }

    private void BeginNewAccount(bool clearSelection = true)
    {
        if (clearSelection)
        {
            SetSelectedAccountSilently(null);
        }

        _editingAccountId = null;
        EditingAccount = string.Empty;
        EditingName = string.Empty;
        EditingPermissionId = PermissionOptions.LastOrDefault()?.Id ?? string.Empty;
        ClearPasswordBox();
        NotifyEditorTextChanged();
    }

    private void LoadAccountIntoEditor(AccountRecord? accountRecord)
    {
        if (accountRecord is null)
        {
            BeginNewAccount(clearSelection: false);
            return;
        }

        _editingAccountId = accountRecord.Id;
        EditingAccount = accountRecord.Account;
        EditingName = accountRecord.Name;
        EditingPermissionId = accountRecord.PermissionId;
        ClearPasswordBox();
        NotifyEditorTextChanged();
    }

    private void ClearPasswordBox()
    {
        _isUpdatingPasswordBox = true;
        RequestClearPassword?.Invoke(this, EventArgs.Empty);
        _editingPassword = string.Empty;
        _isUpdatingPasswordBox = false;
    }

    #endregion

    #region 配置订阅与变更刷新方法

    /// <summary>
    /// 订阅账号和角色集合变化，保证界面显示与配置模型同步。
    /// </summary>
    private void HookCatalog()
    {
        _catalog.Accounts.CollectionChanged += Accounts_CollectionChanged;
        _catalog.Permissions.CollectionChanged += Permissions_CollectionChanged;

        foreach (AccountRecord account in _catalog.Accounts)
        {
            account.PropertyChanged += Account_PropertyChanged;
        }

        foreach (AccountPermissionProfile permission in _catalog.Permissions)
        {
            permission.PropertyChanged += Permission_PropertyChanged;
        }
    }

    private void UnhookCatalog()
    {
        _catalog.Accounts.CollectionChanged -= Accounts_CollectionChanged;
        _catalog.Permissions.CollectionChanged -= Permissions_CollectionChanged;

        foreach (AccountRecord account in _catalog.Accounts)
        {
            account.PropertyChanged -= Account_PropertyChanged;
        }

        foreach (AccountPermissionProfile permission in _catalog.Permissions)
        {
            permission.PropertyChanged -= Permission_PropertyChanged;
        }
    }

    private void Accounts_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (AccountRecord account in e.OldItems)
            {
                account.PropertyChanged -= Account_PropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (AccountRecord account in e.NewItems)
            {
                account.PropertyChanged += Account_PropertyChanged;
            }
        }

        RefreshVisibleAccounts(SelectedAccount?.Id);
    }

    private void Permissions_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (AccountPermissionProfile permission in e.OldItems)
            {
                permission.PropertyChanged -= Permission_PropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (AccountPermissionProfile permission in e.NewItems)
            {
                permission.PropertyChanged += Permission_PropertyChanged;
            }
        }

        RefreshAccountPermissionDisplayNames();
        RefreshPermissionOptions();
        RefreshVisibleAccounts(SelectedAccount?.Id);
    }

    private void Account_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AccountRecord.PermissionId))
        {
            RefreshVisibleAccounts(SelectedAccount?.Id);
            return;
        }

        if (ReferenceEquals(sender, SelectedAccount))
        {
            OnPropertyChanged(nameof(EditorSummaryText));
        }
    }

    private void Permission_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AccountPermissionProfile.Name) or nameof(AccountPermissionProfile.Level))
        {
            RefreshAccountPermissionDisplayNames();
            RefreshPermissionOptions();
            RefreshVisibleAccounts(SelectedAccount?.Id);
        }
    }

    #endregion

    #region 权限判断与状态通知方法

    /// <summary>
    /// 判断当前登录用户是否可以管理目标账号。
    /// </summary>
    private bool CanManageAccount(AccountRecord account)
    {
        return CanManagePermission(account.PermissionId);
    }

    private bool CanManagePermission(string permissionId)
    {
        AccountPermissionProfile? permission = AccountPermissionDisplay.FindProfile(_catalog.Permissions, permissionId);
        return permission is not null && CanManagePermission(permission);
    }

    private bool CanManagePermission(AccountPermissionProfile permission)
    {
        return CanAssignAllRoles ||
               AccountPermissionDisplay.CanManageLevel(_currentUser.PermissionLevel, permission.Level);
    }

    private void SetSelectedAccountSilently(AccountRecord? account)
    {
        _isUpdatingSelection = true;
        SelectedAccount = account;
        _isUpdatingSelection = false;
    }

    private void NotifyEditorTextChanged()
    {
        OnPropertyChanged(nameof(EditorTitle));
        OnPropertyChanged(nameof(EditorSummaryText));
    }

    private void NotifyAccountCountChanged()
    {
        OnPropertyChanged(nameof(AccountCountText));
        OnPropertyChanged(nameof(AccountSummaryText));
    }

    private void SetPageStatus(string text, Brush brush)
    {
        PageStatusText = text;
        PageStatusBrush = brush;
    }

    private bool CanAssignAllRoles => _currentUser.IsBuiltIn;

    #endregion
}
