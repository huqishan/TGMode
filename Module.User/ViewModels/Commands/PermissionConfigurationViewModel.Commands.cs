using ControlLibrary;
using ControlLibrary.Controls.Navigation.Models;
using Module.User.Models;
using Module.User.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;

namespace Module.User.ViewModels;

/// <summary>
/// 权限配置界面的命令实现和业务方法，供 XAML Command 绑定调用。
/// </summary>
public sealed partial class PermissionConfigurationViewModel
{
    #region 命令初始化

    /// <summary>
    /// 初始化角色维护、权限批量操作和树节点展开命令。
    /// </summary>
    private void InitializeCommands()
    {
        NewRoleCommand = new RelayCommand(_ => NewRole());
        DuplicateRoleCommand = new RelayCommand(_ => DuplicateRole());
        DeleteRoleCommand = new RelayCommand(_ => DeleteRole());
        SaveCommand = new RelayCommand(_ => SaveRoleAndPermissionConfiguration());
        ReloadCommand = new RelayCommand(_ => ReloadPermissionDefinitions("已重新扫描界面和按钮"));
        ShowAllCommand = new RelayCommand(_ => ShowAll());
        SelectAllCommand = new RelayCommand(_ => SelectAll());
        ClearSelectionCommand = new RelayCommand(_ => ClearSelection());
        ExpandAllCommand = new RelayCommand(_ => ExpandAll());
        CollapseAllCommand = new RelayCommand(_ => CollapseAll());
        ToggleNodeCommand = new RelayCommand(ToggleNode);
    }

    private void NewRole()
    {
        if (!CanEditPermissionConfiguration())
        {
            return;
        }

        StoreCurrentRoleSettingsInCatalog(SelectedRoleId);
        AccountPermissionProfile role = new()
        {
            Id = $"permission-{Guid.NewGuid():N}",
            Name = GenerateUniqueRoleName("新角色"),
            Level = GetDefaultNewRoleLevel()
        };

        _accountCatalog.Permissions.Add(role);
        RefreshPermissionRoles(role.Id);
        SetStatus($"已新建角色：{role.Name}，记得保存", SuccessBrush);
    }

    private void DuplicateRole()
    {
        if (!CanEditPermissionConfiguration())
        {
            return;
        }

        if (SelectedRole is null)
        {
            SetStatus("请先选择要复制的角色", WarningBrush);
            return;
        }

        StoreCurrentRoleSettingsInCatalog(SelectedRole.Id);
        AccountPermissionProfile copy = new()
        {
            Id = $"permission-{Guid.NewGuid():N}",
            Name = GenerateCopyRoleName(SelectedRole.Name),
            Level = SelectedRole.Level
        };

        _accountCatalog.Permissions.Add(copy);
        CopyRoleSettings(SelectedRole.Id, copy.Id);
        RefreshPermissionRoles(copy.Id);
        SetStatus($"已复制角色：{copy.Name}，记得保存", SuccessBrush);
    }

    private void DeleteRole()
    {
        if (!CanEditPermissionConfiguration())
        {
            return;
        }

        if (SelectedRole is null)
        {
            SetStatus("请先选择要删除的角色", WarningBrush);
            return;
        }

        if (PermissionRoles.Count <= 1)
        {
            SetStatus("至少需要保留一个角色", WarningBrush);
            return;
        }

        if (_accountCatalog.Accounts.Any(account => string.Equals(account.PermissionId, SelectedRole.Id, StringComparison.Ordinal)))
        {
            SetStatus("该角色已有账号使用，不能删除", WarningBrush);
            return;
        }

        int selectedIndex = Math.Max(0, PermissionRoles.IndexOf(SelectedRole));
        string deletedName = SelectedRole.Name;
        string deletedId = SelectedRole.Id;
        _accountCatalog.Permissions.Remove(SelectedRole);
        UiPermissionRoleConfig? roleConfig = _catalog.Roles.FirstOrDefault(role =>
            string.Equals(role.RoleId, deletedId, StringComparison.Ordinal));
        if (roleConfig is not null)
        {
            _catalog.Roles.Remove(roleConfig);
        }

        AccountPermissionProfile? nextRole = _accountCatalog.Permissions
            .OrderBy(role => role.Level)
            .ThenBy(role => role.Name, StringComparer.CurrentCultureIgnoreCase)
            .ElementAtOrDefault(Math.Clamp(selectedIndex, 0, Math.Max(0, _accountCatalog.Permissions.Count - 1)));

        RefreshPermissionRoles(nextRole?.Id);
        SetStatus($"已删除角色：{deletedName}，记得保存", WarningBrush);
    }

    private void ShowAll()
    {
        if (!CanEditPermissionConfiguration())
        {
            return;
        }

        foreach (UiPermissionTreeNode node in FlattenAllNodes(_rootNodes))
        {
            node.IsVisible = true;
        }

        SetStatus($"角色 {SelectedRoleName} 已全部设置为显示，记得保存", NeutralBrush);
    }

    private void SelectAll()
    {
        if (!CanEditPermissionConfiguration())
        {
            return;
        }

        foreach (UiPermissionTreeNode node in FlattenAllNodes(_rootNodes))
        {
            node.IsVisible = true;
            node.IsEnabled = true;
        }

        SetStatus($"角色 {SelectedRoleName} 已全部选中，记得保存", NeutralBrush);
    }

    private void ClearSelection()
    {
        if (!CanEditPermissionConfiguration())
        {
            return;
        }

        foreach (UiPermissionTreeNode node in FlattenAllNodes(_rootNodes))
        {
            node.IsVisible = false;
            node.IsEnabled = false;
        }

        SetStatus($"角色 {SelectedRoleName} 已取消全部选中，记得保存", NeutralBrush);
    }

    private void ExpandAll()
    {
        SetExpandedRecursive(_rootNodes, isExpanded: true);
        RefreshVisibleRows();
    }

    private void CollapseAll()
    {
        SetExpandedRecursive(_rootNodes, isExpanded: false);
        RefreshVisibleRows();
    }

    private void ToggleNode(object? parameter)
    {
        if (parameter is not UiPermissionTreeNode node || !node.HasChildren)
        {
            return;
        }

        node.IsExpanded = !node.IsExpanded;
        RefreshVisibleRows();
    }

    #endregion

    #region 配置加载与角色刷新方法

    /// <summary>
    /// 重新扫描界面权限节点并加载账号、权限配置。
    /// </summary>
    private void ReloadPermissionDefinitions(string statusText)
    {
        string preferredRoleId = SelectedRoleId;
        _definitions = UiPermissionDiscoveryService.Discover(NavigationCatalog.CreateItems());
        _accountCatalog = AccountConfigurationStore.LoadCatalog();
        _catalog = UiPermissionConfigurationStore.LoadCatalog();
        RefreshPermissionRoles(preferredRoleId);
        SetStatus(statusText, SuccessBrush);
        OnPropertyChanged(nameof(SummaryText));
    }

    private void RefreshPermissionRoles(string? preferredRoleId = null)
    {
        _isRefreshingRoles = true;
        if (_selectedRole is not null)
        {
            _selectedRole.PropertyChanged -= SelectedRole_PropertyChanged;
        }

        _selectedRole = null;
        PermissionRoles.Clear();

        foreach (AccountPermissionProfile role in _accountCatalog.Permissions
                     .OrderBy(role => role.Level)
                     .ThenBy(role => role.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            PermissionRoles.Add(role);
        }

        _isRefreshingRoles = false;
        AccountPermissionProfile? nextRole =
            PermissionRoles.FirstOrDefault(role => string.Equals(role.Id, preferredRoleId, StringComparison.Ordinal)) ??
            PermissionRoles.FirstOrDefault();
        SelectedRole = nextRole;
        OnPropertyChanged(nameof(RoleCountText));
        OnPropertyChanged(nameof(SelectedRoleName));
        OnPropertyChanged(nameof(SummaryText));
    }

    private void LoadSelectedRole()
    {
        PermissionRows.Clear();
        _rootNodes = new List<UiPermissionTreeNode>();

        if (_definitions.Count == 0 || string.IsNullOrWhiteSpace(SelectedRoleId))
        {
            return;
        }

        Dictionary<string, UiPermissionElementSetting> settings =
            UiPermissionConfigurationStore.GetRoleSettingMap(_catalog, SelectedRoleId);

        Dictionary<string, UiPermissionTreeNode> nodes = new(StringComparer.Ordinal);
        foreach (UiPermissionNodeDefinition definition in _definitions.OrderBy(item => item.Order))
        {
            settings.TryGetValue(definition.Key, out UiPermissionElementSetting? setting);
            nodes[definition.Key] = new UiPermissionTreeNode(
                definition,
                setting?.IsVisible ?? false,
                setting?.IsEnabled ?? false);
        }

        foreach (UiPermissionNodeDefinition definition in _definitions.OrderBy(item => item.Order))
        {
            UiPermissionTreeNode node = nodes[definition.Key];
            if (!string.IsNullOrWhiteSpace(definition.ParentKey) &&
                nodes.TryGetValue(definition.ParentKey, out UiPermissionTreeNode? parentNode))
            {
                parentNode.Children.Add(node);
                continue;
            }

            _rootNodes.Add(node);
        }

        RefreshVisibleRows();
    }

    #endregion

    #region 保存与权限配置转换方法

    /// <summary>
    /// 保存角色列表和当前角色的界面权限配置。
    /// </summary>
    private void SaveRoleAndPermissionConfiguration()
    {
        if (!CanEditPermissionConfiguration())
        {
            return;
        }

        if (SelectedRole is null)
        {
            SetStatus("请先选择要配置的角色", WarningBrush);
            return;
        }

        Dictionary<string, bool> expansionState = CaptureExpansionState();
        StoreCurrentRoleSettingsInCatalog(SelectedRole.Id);
        if (!ValidateRoles())
        {
            return;
        }

        AccountConfigurationStore.SaveCatalog(_accountCatalog);
        UiPermissionConfigurationStore.SaveCatalog(_catalog);
        string selectedRoleId = SelectedRole.Id;
        _accountCatalog = AccountConfigurationStore.LoadCatalog();
        _catalog = UiPermissionConfigurationStore.LoadCatalog();
        RefreshPermissionRoles(selectedRoleId);
        RestoreExpansionState(expansionState);
        SetStatus($"已保存角色 {SelectedRoleName} 的权限配置，重启软件后生效", SuccessBrush);
    }

    private bool ValidateRoles()
    {
        if (_accountCatalog.Permissions.Count == 0)
        {
            SetStatus("至少需要保留一个角色", WarningBrush);
            return false;
        }

        AccountPermissionProfile? emptyRole = _accountCatalog.Permissions.FirstOrDefault(role =>
            string.IsNullOrWhiteSpace(role.Name));
        if (emptyRole is not null)
        {
            SetStatus("角色名称不能为空", WarningBrush);
            SelectedRole = emptyRole;
            return false;
        }

        AccountPermissionProfile? duplicatedRole = _accountCatalog.Permissions
            .GroupBy(role => role.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1)
            ?.FirstOrDefault();
        if (duplicatedRole is not null)
        {
            SetStatus($"角色名称已存在：{duplicatedRole.Name}", WarningBrush);
            SelectedRole = duplicatedRole;
            return false;
        }

        return true;
    }

    private void StoreCurrentRoleSettingsInCatalog(string? roleId)
    {
        string normalizedRoleId = roleId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedRoleId) || _rootNodes.Count == 0)
        {
            return;
        }

        UiPermissionRoleConfig? roleConfig = _catalog.Roles.FirstOrDefault(role =>
            string.Equals(role.RoleId, normalizedRoleId, StringComparison.Ordinal));
        if (roleConfig is null)
        {
            roleConfig = new UiPermissionRoleConfig
            {
                RoleId = normalizedRoleId
            };
            _catalog.Roles.Add(roleConfig);
        }

        roleConfig.Items = FlattenAllNodes(_rootNodes)
            .Select(node => new UiPermissionElementSetting
            {
                Key = node.Key,
                IsVisible = node.IsVisible,
                IsEnabled = node.IsEnabled
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Key))
            .GroupBy(item => item.Key.Trim(), StringComparer.Ordinal)
            .Select(group => group.Last())
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .ToList();
    }

    private void CopyRoleSettings(string sourceRoleId, string targetRoleId)
    {
        UiPermissionRoleConfig? sourceConfig = _catalog.Roles.FirstOrDefault(role =>
            string.Equals(role.RoleId, sourceRoleId, StringComparison.Ordinal));
        if (sourceConfig is null)
        {
            return;
        }

        UiPermissionRoleConfig targetConfig = new()
        {
            RoleId = targetRoleId,
            Items = sourceConfig.Items
                .Select(item => new UiPermissionElementSetting
                {
                    Key = item.Key,
                    IsVisible = item.IsVisible,
                    IsEnabled = item.IsEnabled
                })
                .ToList()
        };

        _catalog.Roles.RemoveAll(role => string.Equals(role.RoleId, targetRoleId, StringComparison.Ordinal));
        _catalog.Roles.Add(targetConfig);
    }

    #endregion

    #region 角色状态与命名方法

    /// <summary>
    /// 同步当前编辑角色的名称变化到页面状态。
    /// </summary>
    private void SelectedRole_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AccountPermissionProfile.Name))
        {
            OnPropertyChanged(nameof(SelectedRoleName));
            OnPropertyChanged(nameof(SummaryText));
            SetStatus($"正在编辑角色：{SelectedRoleName}", NeutralBrush);
        }
    }

    private int GetDefaultNewRoleLevel()
    {
        AuthenticatedUser? user = CurrentUserSession.Current;
        int currentLevel = user?.PermissionLevel ?? AccountPermissionDisplay.SystemAdministratorLevel;
        return AccountPermissionDisplay.NormalizeLevel(currentLevel + 1);
    }

    private string GenerateUniqueRoleName(string prefix)
    {
        for (int index = 1; ; index++)
        {
            string name = $"{prefix} {index}";
            if (!_accountCatalog.Permissions.Any(role => string.Equals(role.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                return name;
            }
        }
    }

    private string GenerateCopyRoleName(string baseName)
    {
        string prefix = string.IsNullOrWhiteSpace(baseName) ? "角色" : baseName.Trim();
        string firstName = $"{prefix} 副本";
        if (!_accountCatalog.Permissions.Any(role => string.Equals(role.Name, firstName, StringComparison.OrdinalIgnoreCase)))
        {
            return firstName;
        }

        for (int index = 2; ; index++)
        {
            string name = $"{firstName} {index}";
            if (!_accountCatalog.Permissions.Any(role => string.Equals(role.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                return name;
            }
        }
    }

    #endregion

    #region 权限树展开与状态方法

    /// <summary>
    /// 根据展开状态刷新权限树表格的可见行。
    /// </summary>
    private void RefreshVisibleRows()
    {
        PermissionRows.Clear();

        foreach (UiPermissionTreeNode node in FlattenVisibleNodes(_rootNodes))
        {
            PermissionRows.Add(node);
        }
    }

    private static IEnumerable<UiPermissionTreeNode> FlattenVisibleNodes(
        IEnumerable<UiPermissionTreeNode> nodes,
        int level = 0)
    {
        foreach (UiPermissionTreeNode node in nodes)
        {
            node.Level = level;
            yield return node;

            if (!node.IsExpanded)
            {
                continue;
            }

            foreach (UiPermissionTreeNode child in FlattenVisibleNodes(node.Children, level + 1))
            {
                yield return child;
            }
        }
    }

    private static IEnumerable<UiPermissionTreeNode> FlattenAllNodes(IEnumerable<UiPermissionTreeNode> nodes)
    {
        foreach (UiPermissionTreeNode node in nodes)
        {
            yield return node;

            foreach (UiPermissionTreeNode child in FlattenAllNodes(node.Children))
            {
                yield return child;
            }
        }
    }

    private Dictionary<string, bool> CaptureExpansionState()
    {
        return FlattenAllNodes(_rootNodes)
            .Where(node => !string.IsNullOrWhiteSpace(node.Key))
            .GroupBy(node => node.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last().IsExpanded, StringComparer.Ordinal);
    }

    private void RestoreExpansionState(IReadOnlyDictionary<string, bool> expansionState)
    {
        if (expansionState.Count == 0)
        {
            return;
        }

        foreach (UiPermissionTreeNode node in FlattenAllNodes(_rootNodes))
        {
            if (expansionState.TryGetValue(node.Key, out bool isExpanded))
            {
                node.IsExpanded = isExpanded;
            }
        }

        RefreshVisibleRows();
    }

    private static void SetExpandedRecursive(IEnumerable<UiPermissionTreeNode> nodes, bool isExpanded)
    {
        foreach (UiPermissionTreeNode node in nodes)
        {
            node.IsExpanded = isExpanded;
            SetExpandedRecursive(node.Children, isExpanded);
        }
    }

    private void SetStatus(string text, Brush brush)
    {
        StatusText = text;
        StatusBrush = brush;
    }

    private bool CanEditPermissionConfiguration()
    {
        if (CanEditPermissions)
        {
            return true;
        }

        SetStatus("只有内置管理员或管理员角色可以修改权限配置", WarningBrush);
        return false;
    }

    #endregion
}
