using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WpfApp.Models.UserManagement;
using WpfApp.Services.Navigation;
using WpfApp.Services.UserManagement;

namespace WpfApp.Views.UserManagement;

public partial class PermissionConfigurationView : UserControl, INotifyPropertyChanged
{
    private static readonly Brush SuccessBrush =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16A34A"));

    private static readonly Brush WarningBrush =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EA580C"));

    private static readonly Brush NeutralBrush =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));

    private IReadOnlyList<UiPermissionNodeDefinition> _definitions = Array.Empty<UiPermissionNodeDefinition>();
    private List<UiPermissionTreeNode> _rootNodes = new();
    private AccountCatalog _accountCatalog = new();
    private UiPermissionCatalog _catalog = new();
    private string _selectedRoleId = string.Empty;
    private string _statusText = "等待扫描";
    private Brush _statusBrush = NeutralBrush;

    public PermissionConfigurationView()
    {
        InitializeComponent();
        DataContext = this;
        SourceColumn.Visibility = CurrentUserSession.Current?.IsBuiltIn == true
            ? Visibility.Visible
            : Visibility.Collapsed;
        ReloadPermissionDefinitions("已加载权限配置");
        if (!CanEditPermissions)
        {
            SetStatus("只有内置管理员或管理员角色可以修改权限配置", WarningBrush);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<AccountPermissionOption> PermissionRoleOptions { get; } = new();

    public ObservableCollection<UiPermissionTreeNode> PermissionRows { get; } = new();

    public bool CanEditPermissions => AccountPermissionDisplay.CanConfigureUiPermissions(CurrentUserSession.Current);

    public string SelectedRoleId
    {
        get => _selectedRoleId;
        set
        {
            string normalizedValue = value?.Trim() ?? string.Empty;
            if (SetField(ref _selectedRoleId, normalizedValue))
            {
                LoadSelectedRole();
                OnPropertyChanged(nameof(SummaryText));
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    public Brush StatusBrush
    {
        get => _statusBrush;
        private set => SetField(ref _statusBrush, value);
    }

    public string SummaryText
    {
        get
        {
            int pageCount = _definitions.Count(item => item.Kind == UiPermissionNodeKind.Page);
            int dialogCount = _definitions.Count(item => item.Kind == UiPermissionNodeKind.Dialog);
            int buttonCount = _definitions.Count(item => item.Kind == UiPermissionNodeKind.Button);
            return string.IsNullOrWhiteSpace(SelectedRoleId)
                ? $"请选择角色。已发现 {pageCount} 个界面、{dialogCount} 个弹窗、{buttonCount} 个按钮。"
                : $"当前角色：{SelectedRoleName}。已发现 {pageCount} 个界面、{dialogCount} 个弹窗、{buttonCount} 个按钮。";
        }
    }

    private string SelectedRoleName =>
        PermissionRoleOptions.FirstOrDefault(option => string.Equals(option.Id, SelectedRoleId, StringComparison.Ordinal))
            ?.DisplayName ?? "未选择角色";

    private void ReloadButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanEditPermissionConfiguration())
        {
            return;
        }

        ReloadPermissionDefinitions("已重新扫描界面和按钮");
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        SaveSelectedRole();
    }

    private void ShowAllButton_Click(object sender, RoutedEventArgs e)
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

    private void EnableAllButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanEditPermissionConfiguration())
        {
            return;
        }

        foreach (UiPermissionTreeNode node in FlattenAllNodes(_rootNodes))
        {
            node.IsEnabled = true;
        }

        SetStatus($"角色 {SelectedRoleName} 已全部设置为可点击，记得保存", NeutralBrush);
    }

    private void ExpandAllButton_Click(object sender, RoutedEventArgs e)
    {
        SetExpandedRecursive(_rootNodes, isExpanded: true);
        RefreshVisibleRows();
    }

    private void CollapseAllButton_Click(object sender, RoutedEventArgs e)
    {
        SetExpandedRecursive(_rootNodes, isExpanded: false);
        RefreshVisibleRows();
    }

    private void ToggleNodeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: UiPermissionTreeNode node } || !node.HasChildren)
        {
            return;
        }

        node.IsExpanded = !node.IsExpanded;
        RefreshVisibleRows();
    }

    private void ReloadPermissionDefinitions(string statusText)
    {
        string preferredRoleId = SelectedRoleId;
        _definitions = UiPermissionDiscoveryService.Discover(NavigationCatalog.CreateItems());
        _accountCatalog = AccountConfigurationStore.LoadCatalog();
        _catalog = UiPermissionConfigurationStore.LoadCatalog();
        RefreshPermissionRoleOptions(preferredRoleId);
        LoadSelectedRole();
        SetStatus(statusText, SuccessBrush);
        OnPropertyChanged(nameof(SummaryText));
    }

    private void RefreshPermissionRoleOptions(string? preferredRoleId = null)
    {
        PermissionRoleOptions.Clear();

        foreach (AccountPermissionProfile role in _accountCatalog.Permissions
                     .OrderBy(role => role.Level)
                     .ThenBy(role => role.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            PermissionRoleOptions.Add(new AccountPermissionOption(role.Id, role.Name, role.Level));
        }

        string? nextRoleId = PermissionRoleOptions.Any(option => string.Equals(option.Id, preferredRoleId, StringComparison.Ordinal))
            ? preferredRoleId
            : PermissionRoleOptions.FirstOrDefault()?.Id;

        SelectedRoleId = nextRoleId ?? string.Empty;
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
                setting?.IsVisible ?? true,
                setting?.IsEnabled ?? true);
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

    private void SaveSelectedRole()
    {
        if (!CanEditPermissionConfiguration())
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedRoleId))
        {
            SetStatus("请先选择要配置的角色", WarningBrush);
            return;
        }

        List<UiPermissionElementSetting> settings = FlattenAllNodes(_rootNodes)
            .Select(node => new UiPermissionElementSetting
            {
                Key = node.Key,
                IsVisible = node.IsVisible,
                IsEnabled = node.IsEnabled
            })
            .ToList();

        UiPermissionConfigurationStore.SaveRoleSettings(_catalog, SelectedRoleId, settings);
        _catalog = UiPermissionConfigurationStore.LoadCatalog();
        SetStatus($"已保存角色 {SelectedRoleName} 的权限配置，重启软件后生效", SuccessBrush);
    }

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
}

public sealed class UiPermissionTreeNode : INotifyPropertyChanged
{
    private bool _isVisible;
    private bool _isEnabled;
    private bool _isExpanded = true;
    private int _level;

    public UiPermissionTreeNode(UiPermissionNodeDefinition definition, bool isVisible, bool isEnabled)
    {
        Definition = definition;
        _isVisible = isVisible;
        _isEnabled = isEnabled;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public UiPermissionNodeDefinition Definition { get; }

    public ObservableCollection<UiPermissionTreeNode> Children { get; } = new();

    public string Key => Definition.Key;

    public string DisplayName => Definition.DisplayName;

    public string KindDisplayName => Definition.KindDisplayName;

    public string ElementIdentifier =>
        Definition.Kind == UiPermissionNodeKind.Page ? string.Empty : Definition.ElementIdentifier;

    public string SourcePath => Definition.SourcePath;

    public bool HasChildren => Children.Count > 0;

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetField(ref _isExpanded, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ExpandGlyph)));
            }
        }
    }

    public string ExpandGlyph => !HasChildren
        ? string.Empty
        : IsExpanded ? "v" : ">";

    public int Level
    {
        get => _level;
        set
        {
            if (SetField(ref _level, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IndentWidth)));
            }
        }
    }

    public double IndentWidth => Math.Min(Level, 4) * 22d;

    public bool IsVisible
    {
        get => _isVisible;
        set => SetField(ref _isVisible, value);
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetField(ref _isEnabled, value);
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
