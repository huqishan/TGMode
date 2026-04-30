using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WpfApp.Models.UserManagement;
using WpfApp.Services.UserManagement;

namespace WpfApp.Views.UserManagement;

public partial class AccountManagementView : UserControl, INotifyPropertyChanged
{
    private static readonly Brush SuccessBrush =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16A34A"));

    private static readonly Brush WarningBrush =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EA580C"));

    private static readonly Brush NeutralBrush =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));

    private const double PermissionDrawerClosedOffset = 56d;
    private static readonly Duration PermissionDrawerAnimationDuration = new(TimeSpan.FromMilliseconds(220));
    private static readonly IEasingFunction PermissionDrawerEasing = new CubicEase { EasingMode = EasingMode.EaseOut };

    private readonly ObservableCollection<AccountRecord> _visibleAccounts = new();
    private readonly ObservableCollection<AccountPermissionProfile> _visiblePermissions = new();
    private readonly AuthenticatedUser _currentUser;
    private AccountCatalog _catalog = new();
    private AccountRecord? _selectedAccount;
    private AccountPermissionProfile? _selectedPermissionProfile;
    private string? _editingAccountId;
    private string? _editingPermissionProfileId;
    private string _editingAccount = string.Empty;
    private string _editingName = string.Empty;
    private string _editingPassword = string.Empty;
    private string _editingPermissionId = AccountPermissionDisplay.DefaultEmployeePermissionId;
    private string _editingPermissionName = string.Empty;
    private int _editingPermissionLevel = AccountPermissionDisplay.LowestLevel;
    private string _pageStatusText = "等待编辑";
    private string _permissionDrawerStatusText = "等待编辑权限";
    private Brush _pageStatusBrush = NeutralBrush;
    private Brush _permissionDrawerStatusBrush = NeutralBrush;
    private bool _isUpdatingSelection;
    private bool _isUpdatingPermissionSelection;
    private bool _isUpdatingPasswordBox;
    private bool _isPermissionDrawerOpen;

    public AccountManagementView()
    {
        InitializeComponent();

        _currentUser = CurrentUserSession.RequireCurrentUser();
        PermissionLevelOptions = AccountPermissionDisplay.LevelOptions;

        DataContext = this;
        LoadCatalog(AccountConfigurationStore.LoadCatalog());
        UpdatePermissionDrawerVisual(animate: false);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<AccountPermissionOption> PermissionOptions { get; } = new();

    public IReadOnlyList<PermissionLevelOption> PermissionLevelOptions { get; }

    public ObservableCollection<AccountRecord> Accounts => _visibleAccounts;

    public ObservableCollection<AccountPermissionProfile> PermissionProfiles => _visiblePermissions;

    public AccountRecord? SelectedAccount
    {
        get => _selectedAccount;
        set
        {
            if (!SetField(ref _selectedAccount, value))
            {
                return;
            }

            if (!_isUpdatingSelection)
            {
                LoadAccountIntoEditor(value);
            }
        }
    }

    public AccountPermissionProfile? SelectedPermissionProfile
    {
        get => _selectedPermissionProfile;
        set
        {
            if (!SetField(ref _selectedPermissionProfile, value))
            {
                return;
            }

            if (!_isUpdatingPermissionSelection)
            {
                LoadPermissionIntoEditor(value);
            }
        }
    }

    public string EditingAccount
    {
        get => _editingAccount;
        set
        {
            if (SetField(ref _editingAccount, value))
            {
                NotifyEditorTextChanged();
            }
        }
    }

    public string EditingName
    {
        get => _editingName;
        set
        {
            if (SetField(ref _editingName, value))
            {
                NotifyEditorTextChanged();
            }
        }
    }

    public string EditingPermissionId
    {
        get => _editingPermissionId;
        set
        {
            if (SetField(ref _editingPermissionId, value?.Trim() ?? string.Empty))
            {
                NotifyEditorTextChanged();
            }
        }
    }

    public string EditingPermissionName
    {
        get => _editingPermissionName;
        set
        {
            if (SetField(ref _editingPermissionName, value))
            {
                NotifyPermissionEditorTextChanged();
            }
        }
    }

    public int EditingPermissionLevel
    {
        get => _editingPermissionLevel;
        set
        {
            if (SetField(ref _editingPermissionLevel, AccountPermissionDisplay.NormalizeLevel(value)))
            {
                NotifyPermissionEditorTextChanged();
            }
        }
    }

    public string PageStatusText
    {
        get => _pageStatusText;
        private set => SetField(ref _pageStatusText, value);
    }

    public Brush PageStatusBrush
    {
        get => _pageStatusBrush;
        private set => SetField(ref _pageStatusBrush, value);
    }

    public string PermissionDrawerStatusText
    {
        get => _permissionDrawerStatusText;
        private set => SetField(ref _permissionDrawerStatusText, value);
    }

    public Brush PermissionDrawerStatusBrush
    {
        get => _permissionDrawerStatusBrush;
        private set => SetField(ref _permissionDrawerStatusBrush, value);
    }

    public bool IsPermissionDrawerOpen
    {
        get => _isPermissionDrawerOpen;
        private set => SetField(ref _isPermissionDrawerOpen, value);
    }

    public Visibility PermissionConfigButtonVisibility =>
        CanConfigurePermissions ? Visibility.Visible : Visibility.Collapsed;

    public string EditorTitle =>
        string.IsNullOrWhiteSpace(_editingAccountId) ? "新增账号" : "修改账号";

    public string PermissionEditorTitle =>
        string.IsNullOrWhiteSpace(_editingPermissionProfileId) ? "新增权限" : "修改权限";

    public string EditorSummaryText
    {
        get
        {
            string account = string.IsNullOrWhiteSpace(EditingAccount) ? "未填写账号" : EditingAccount.Trim();
            string name = string.IsNullOrWhiteSpace(EditingName) ? "未填写姓名" : EditingName.Trim();
            string permission = AccountPermissionDisplay.GetDisplayName(_catalog.Permissions, EditingPermissionId);
            return $"{account} / {name} / {permission}";
        }
    }

    public string PermissionEditorSummaryText
    {
        get
        {
            string name = string.IsNullOrWhiteSpace(EditingPermissionName) ? "未填写权限名称" : EditingPermissionName.Trim();
            int lowerLevelCount = AccountPermissionDisplay.LowestLevel - EditingPermissionLevel;
            return $"{name} / {EditingPermissionLevel}级 / 可管理下方 {lowerLevelCount} 个等级";
        }
    }

    public string AccountSummaryText =>
        $"当前登录：{_currentUser.Name}（{_currentUser.PermissionDisplayName}），只显示当前权限可管理的账号。";

    public string AccountCountText => $"共 {Accounts.Count} 个可见账号";

    public string PermissionCountText => $"共 {PermissionProfiles.Count} 个可配置权限";

    private void NewButton_Click(object sender, RoutedEventArgs e)
    {
        BeginNewAccount();
        SetPageStatus("正在新增账号", NeutralBrush);
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        SaveEditingAccount();
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        DeleteSelectedAccount();
    }

    private void OpenPermissionDrawerButton_Click(object sender, RoutedEventArgs e)
    {
        OpenPermissionDrawer();
    }

    private void ClosePermissionDrawerButton_Click(object sender, RoutedEventArgs e)
    {
        ClosePermissionDrawer();
    }

    private void PermissionDrawerBackdrop_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        ClosePermissionDrawer();
    }

    private void NewPermissionButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanConfigurePermissions)
        {
            SetPermissionDrawerStatus("只有内置管理员可以配置权限", WarningBrush);
            return;
        }

        BeginNewPermissionProfile();
        SetPermissionDrawerStatus("正在新增权限", NeutralBrush);
    }

    private void SavePermissionButton_Click(object sender, RoutedEventArgs e)
    {
        SaveEditingPermissionProfile();
    }

    private void DeletePermissionButton_Click(object sender, RoutedEventArgs e)
    {
        DeleteSelectedPermissionProfile();
    }

    private void PasswordInput_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingPasswordBox)
        {
            return;
        }

        _editingPassword = PasswordInput.Password;
    }

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
            SetPageStatus("请选择有效权限", WarningBrush);
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
        LoadCatalog(AccountConfigurationStore.LoadCatalog(), savedId, SelectedPermissionProfile?.Id);
        SetPageStatus($"{(isNew ? "已新增" : "已修改")}账号：{account}", SuccessBrush);
    }

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
        LoadCatalog(AccountConfigurationStore.LoadCatalog(), nextId, SelectedPermissionProfile?.Id);
        SetPageStatus($"已删除账号：{deletedAccount}", WarningBrush);
    }

    private void SaveEditingPermissionProfile()
    {
        if (!CanConfigurePermissions)
        {
            SetPermissionDrawerStatus("只有内置管理员可以配置权限", WarningBrush);
            return;
        }

        string name = EditingPermissionName.Trim();
        AccountPermissionProfile? existingPermission = string.IsNullOrWhiteSpace(_editingPermissionProfileId)
            ? null
            : _catalog.Permissions.FirstOrDefault(item => string.Equals(item.Id, _editingPermissionProfileId, StringComparison.Ordinal));

        bool isNew = existingPermission is null;

        if (string.IsNullOrWhiteSpace(name))
        {
            SetPermissionDrawerStatus("权限名称不能为空", WarningBrush);
            return;
        }

        if (!AccountPermissionDisplay.IsValidLevel(EditingPermissionLevel))
        {
            SetPermissionDrawerStatus("权限等级只能在 1 到 10 之间", WarningBrush);
            return;
        }

        if (_catalog.Permissions.Any(permission =>
                !string.Equals(permission.Id, existingPermission?.Id, StringComparison.Ordinal) &&
                string.Equals(permission.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            SetPermissionDrawerStatus($"权限名称已存在：{name}", WarningBrush);
            return;
        }

        AccountPermissionProfile permissionProfile = existingPermission ?? new AccountPermissionProfile
        {
            Id = $"permission-{Guid.NewGuid():N}"
        };

        permissionProfile.Name = name;
        permissionProfile.Level = EditingPermissionLevel;

        if (isNew)
        {
            _catalog.Permissions.Add(permissionProfile);
        }

        string savedId = permissionProfile.Id;
        string? selectedAccountId = SelectedAccount?.Id ?? _editingAccountId;
        AccountConfigurationStore.SaveCatalog(_catalog);
        LoadCatalog(AccountConfigurationStore.LoadCatalog(), selectedAccountId, savedId);
        SetPermissionDrawerStatus($"{(isNew ? "已新增" : "已修改")}权限：{name}", SuccessBrush);
        SetPageStatus("权限配置已更新", SuccessBrush);
    }

    private void DeleteSelectedPermissionProfile()
    {
        if (!CanConfigurePermissions)
        {
            SetPermissionDrawerStatus("只有内置管理员可以配置权限", WarningBrush);
            return;
        }

        AccountPermissionProfile? permissionProfile = SelectedPermissionProfile ??
            _catalog.Permissions.FirstOrDefault(item => string.Equals(item.Id, _editingPermissionProfileId, StringComparison.Ordinal));

        if (permissionProfile is null)
        {
            SetPermissionDrawerStatus("请先选择要删除的权限", WarningBrush);
            return;
        }

        if (_catalog.Accounts.Any(account => string.Equals(account.PermissionId, permissionProfile.Id, StringComparison.Ordinal)))
        {
            SetPermissionDrawerStatus("该权限已有账号使用，不能删除", WarningBrush);
            return;
        }

        int removedIndex = Math.Max(0, PermissionProfiles.IndexOf(permissionProfile));
        string deletedName = permissionProfile.Name;
        _catalog.Permissions.Remove(permissionProfile);
        string? nextId = PermissionProfiles.Count == 0
            ? null
            : PermissionProfiles[Math.Clamp(removedIndex, 0, PermissionProfiles.Count - 1)].Id;

        string? selectedAccountId = SelectedAccount?.Id ?? _editingAccountId;
        AccountConfigurationStore.SaveCatalog(_catalog);
        LoadCatalog(AccountConfigurationStore.LoadCatalog(), selectedAccountId, nextId);
        SetPermissionDrawerStatus($"已删除权限：{deletedName}", WarningBrush);
        SetPageStatus("权限配置已更新", SuccessBrush);
    }

    private void LoadCatalog(
        AccountCatalog catalog,
        string? preferredAccountId = null,
        string? preferredPermissionId = null)
    {
        UnhookCatalog();
        _catalog = catalog;
        RefreshAccountPermissionDisplayNames();
        HookCatalog();
        RefreshPermissionOptions();
        RefreshVisiblePermissions(preferredPermissionId);
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

    private void RefreshVisiblePermissions(string? preferredPermissionId = null)
    {
        _visiblePermissions.Clear();

        foreach (AccountPermissionProfile permission in _catalog.Permissions.Where(CanManagePermissionProfile))
        {
            _visiblePermissions.Add(permission);
        }

        OnPropertyChanged(nameof(PermissionProfiles));
        NotifyPermissionCountChanged();

        AccountPermissionProfile? preferredPermission =
            _visiblePermissions.FirstOrDefault(permission => string.Equals(permission.Id, preferredPermissionId, StringComparison.Ordinal)) ??
            _visiblePermissions.FirstOrDefault();

        SetSelectedPermissionSilently(preferredPermission);
        if (preferredPermission is null)
        {
            BeginNewPermissionProfile(clearSelection: false);
            return;
        }

        LoadPermissionIntoEditor(preferredPermission);
    }

    private void RefreshPermissionOptions()
    {
        string currentEditingPermissionId = EditingPermissionId;
        PermissionOptions.Clear();

        IReadOnlyList<AccountPermissionOption> assignableOptions = CanConfigurePermissions
            ? _catalog.Permissions
                .OrderBy(permission => permission.Level)
                .ThenBy(permission => permission.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(permission => new AccountPermissionOption(permission.Id, permission.DisplayName, permission.Level))
                .ToList()
            : AccountPermissionDisplay.GetAssignableOptions(_catalog.Permissions, _currentUser.PermissionLevel);

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

    private void BeginNewPermissionProfile(bool clearSelection = true)
    {
        if (clearSelection)
        {
            SetSelectedPermissionSilently(null);
        }

        _editingPermissionProfileId = null;
        EditingPermissionName = string.Empty;
        EditingPermissionLevel = AccountPermissionDisplay.NormalizeLevel(_currentUser.PermissionLevel + 1);
        NotifyPermissionEditorTextChanged();
    }

    private void LoadPermissionIntoEditor(AccountPermissionProfile? permissionProfile)
    {
        if (permissionProfile is null)
        {
            BeginNewPermissionProfile(clearSelection: false);
            return;
        }

        _editingPermissionProfileId = permissionProfile.Id;
        EditingPermissionName = permissionProfile.Name;
        EditingPermissionLevel = permissionProfile.Level;
        NotifyPermissionEditorTextChanged();
    }

    private void ClearPasswordBox()
    {
        _isUpdatingPasswordBox = true;
        PasswordInput.Clear();
        _editingPassword = string.Empty;
        _isUpdatingPasswordBox = false;
    }

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
        RefreshVisiblePermissions(SelectedPermissionProfile?.Id);
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
            RefreshVisiblePermissions(SelectedPermissionProfile?.Id);
            RefreshVisibleAccounts(SelectedAccount?.Id);
            NotifyPermissionEditorTextChanged();
        }
    }

    private bool CanManageAccount(AccountRecord account)
    {
        return CanManagePermission(account.PermissionId);
    }

    private bool CanManagePermissionProfile(AccountPermissionProfile permission)
    {
        return CanConfigurePermissions || AccountPermissionDisplay.CanManageLevel(_currentUser.PermissionLevel, permission.Level);
    }

    private bool CanManagePermission(string permissionId)
    {
        if (CanConfigurePermissions)
        {
            return true;
        }

        int targetLevel = AccountPermissionDisplay.GetPermissionLevel(_catalog.Permissions, permissionId);
        return AccountPermissionDisplay.CanManageLevel(_currentUser.PermissionLevel, targetLevel);
    }

    private void SetSelectedAccountSilently(AccountRecord? account)
    {
        _isUpdatingSelection = true;
        SelectedAccount = account;
        _isUpdatingSelection = false;
    }

    private void SetSelectedPermissionSilently(AccountPermissionProfile? permission)
    {
        _isUpdatingPermissionSelection = true;
        SelectedPermissionProfile = permission;
        _isUpdatingPermissionSelection = false;
    }

    private void NotifyEditorTextChanged()
    {
        OnPropertyChanged(nameof(EditorTitle));
        OnPropertyChanged(nameof(EditorSummaryText));
    }

    private void NotifyPermissionEditorTextChanged()
    {
        OnPropertyChanged(nameof(PermissionEditorTitle));
        OnPropertyChanged(nameof(PermissionEditorSummaryText));
    }

    private void NotifyAccountCountChanged()
    {
        OnPropertyChanged(nameof(AccountCountText));
        OnPropertyChanged(nameof(AccountSummaryText));
    }

    private void NotifyPermissionCountChanged()
    {
        OnPropertyChanged(nameof(PermissionCountText));
    }

    private void SetPageStatus(string text, Brush brush)
    {
        PageStatusText = text;
        PageStatusBrush = brush;
    }

    private void SetPermissionDrawerStatus(string text, Brush brush)
    {
        PermissionDrawerStatusText = text;
        PermissionDrawerStatusBrush = brush;
    }

    private void OpenPermissionDrawer()
    {
        if (!CanConfigurePermissions)
        {
            SetPageStatus("只有内置管理员可以配置权限", WarningBrush);
            return;
        }

        IsPermissionDrawerOpen = true;
        UpdatePermissionDrawerVisual(animate: true);
    }

    private void ClosePermissionDrawer()
    {
        IsPermissionDrawerOpen = false;
        UpdatePermissionDrawerVisual(animate: true);
    }

    private void UpdatePermissionDrawerVisual(bool animate)
    {
        if (PermissionDrawerHost is null || PermissionDrawerTranslateTransform is null)
        {
            return;
        }

        double targetOpacity = IsPermissionDrawerOpen ? 1d : 0d;
        double targetOffset = IsPermissionDrawerOpen ? 0d : PermissionDrawerClosedOffset;

        if (IsPermissionDrawerOpen)
        {
            PermissionDrawerHost.IsHitTestVisible = true;
        }

        if (!animate)
        {
            PermissionDrawerHost.BeginAnimation(UIElement.OpacityProperty, null);
            PermissionDrawerTranslateTransform.BeginAnimation(TranslateTransform.YProperty, null);
            PermissionDrawerHost.Opacity = targetOpacity;
            PermissionDrawerTranslateTransform.Y = targetOffset;
            PermissionDrawerHost.IsHitTestVisible = IsPermissionDrawerOpen;
            return;
        }

        DoubleAnimation opacityAnimation = new()
        {
            To = targetOpacity,
            Duration = PermissionDrawerAnimationDuration,
            EasingFunction = PermissionDrawerEasing
        };

        if (!IsPermissionDrawerOpen)
        {
            opacityAnimation.Completed += (_, _) =>
            {
                if (!IsPermissionDrawerOpen)
                {
                    PermissionDrawerHost.IsHitTestVisible = false;
                }
            };
        }

        DoubleAnimation translateAnimation = new()
        {
            To = targetOffset,
            Duration = PermissionDrawerAnimationDuration,
            EasingFunction = PermissionDrawerEasing
        };

        PermissionDrawerHost.BeginAnimation(UIElement.OpacityProperty, opacityAnimation);
        PermissionDrawerTranslateTransform.BeginAnimation(TranslateTransform.YProperty, translateAnimation);
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private bool CanConfigurePermissions => _currentUser.IsBuiltIn;
}
