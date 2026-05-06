using ControlLibrary;
using Module.User.Models;
using Module.User.Services;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Module.User.ViewModels;

/// <summary>
/// 权限配置界面属性、字段、命令和页面模型声明，供 XAML 绑定使用。
/// </summary>
public sealed partial class PermissionConfigurationViewModel : ViewModelProperties
{
    #region 状态颜色字段

    private static readonly Brush SuccessBrush =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16A34A"));

    private static readonly Brush WarningBrush =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EA580C"));

    private static readonly Brush NeutralBrush =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));

    #endregion

    #region 私有状态字段

    private IReadOnlyList<UiPermissionNodeDefinition> _definitions = Array.Empty<UiPermissionNodeDefinition>();
    private List<UiPermissionTreeNode> _rootNodes = new();
    private AccountCatalog _accountCatalog = new();
    private UiPermissionCatalog _catalog = new();
    private AccountPermissionProfile? _selectedRole;
    private string _statusText = "等待扫描";
    private Brush _statusBrush = NeutralBrush;
    private bool _isRefreshingRoles;

    #endregion

    #region 集合属性

    public ObservableCollection<AccountPermissionProfile> PermissionRoles { get; } = new();

    public IReadOnlyList<PermissionLevelOption> PermissionLevelOptions => AccountPermissionDisplay.LevelOptions;

    public ObservableCollection<UiPermissionTreeNode> PermissionRows { get; } = new();

    #endregion

    #region 当前角色属性

    public AccountPermissionProfile? SelectedRole
    {
        get => _selectedRole;
        set
        {
            if (ReferenceEquals(_selectedRole, value))
            {
                return;
            }

            if (!_isRefreshingRoles && _selectedRole is not null)
            {
                StoreCurrentRoleSettingsInCatalog(_selectedRole.Id);
            }

            if (_selectedRole is not null)
            {
                _selectedRole.PropertyChanged -= SelectedRole_PropertyChanged;
            }

            _selectedRole = value;

            if (_selectedRole is not null)
            {
                _selectedRole.PropertyChanged += SelectedRole_PropertyChanged;
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedRoleName));
            OnPropertyChanged(nameof(SummaryText));
            LoadSelectedRole();
        }
    }

    public string SelectedRoleName => SelectedRole?.Name ?? "未选择角色";

    private string SelectedRoleId => SelectedRole?.Id ?? string.Empty;

    #endregion

    #region 页面状态属性

    public bool CanEditPermissions => AccountPermissionDisplay.CanConfigureUiPermissions(CurrentUserSession.Current);

    public Visibility SourceColumnVisibility =>
        CurrentUserSession.Current?.IsBuiltIn == true ? Visibility.Visible : Visibility.Collapsed;

    public string RoleCountText => $"共 {PermissionRoles.Count} 个角色";

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
            return SelectedRole is null
                ? $"请选择角色。已发现 {pageCount} 个界面、{dialogCount} 个弹窗、{buttonCount} 个按钮。"
                : $"当前角色：{SelectedRoleName}。已发现 {pageCount} 个界面、{dialogCount} 个弹窗、{buttonCount} 个按钮。";
        }
    }

    #endregion

    #region 命令属性

    public ICommand NewRoleCommand { get; private set; } = null!;

    public ICommand DuplicateRoleCommand { get; private set; } = null!;

    public ICommand DeleteRoleCommand { get; private set; } = null!;

    public ICommand SaveCommand { get; private set; } = null!;

    public ICommand ReloadCommand { get; private set; } = null!;

    public ICommand ShowAllCommand { get; private set; } = null!;

    public ICommand SelectAllCommand { get; private set; } = null!;

    public ICommand ClearSelectionCommand { get; private set; } = null!;

    public ICommand ExpandAllCommand { get; private set; } = null!;

    public ICommand CollapseAllCommand { get; private set; } = null!;

    public ICommand ToggleNodeCommand { get; private set; } = null!;

    #endregion
}
