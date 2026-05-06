using ControlLibrary;
using Module.User.Models;
using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Media;

namespace Module.User.ViewModels;

/// <summary>
/// 账号管理界面属性、字段和命令声明，供 XAML 绑定使用。
/// </summary>
public sealed partial class AccountManagementViewModel : ViewModelProperties
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

    private readonly ObservableCollection<AccountRecord> _visibleAccounts = new();
    private readonly AuthenticatedUser _currentUser;
    private AccountCatalog _catalog = new();
    private AccountRecord? _selectedAccount;
    private string? _editingAccountId;
    private string _editingAccount = string.Empty;
    private string _editingName = string.Empty;
    private string _editingPassword = string.Empty;
    private string _editingPermissionId = AccountPermissionDisplay.DefaultEmployeePermissionId;
    private string _pageStatusText = "等待编辑";
    private Brush _pageStatusBrush = NeutralBrush;
    private bool _isUpdatingSelection;
    private bool _isUpdatingPasswordBox;

    #endregion

    #region 密码输入框交互事件

    public event EventHandler? RequestClearPassword;

    public void SetEditingPassword(string password)
    {
        if (_isUpdatingPasswordBox)
        {
            return;
        }

        _editingPassword = password;
    }

    #endregion

    #region 集合属性

    public ObservableCollection<AccountPermissionOption> PermissionOptions { get; } = new();

    public ObservableCollection<AccountRecord> Accounts => _visibleAccounts;

    #endregion

    #region 当前编辑属性

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

    #endregion

    #region 页面状态属性

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

    public string EditorTitle =>
        string.IsNullOrWhiteSpace(_editingAccountId) ? "新增账号" : "修改账号";

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

    public string AccountSummaryText =>
        $"当前登录：{_currentUser.Name}（{_currentUser.PermissionDisplayName}），只显示当前权限可管理的账号。";

    public string AccountCountText => $"共 {Accounts.Count} 个可见账号";

    #endregion

    #region 命令属性

    public ICommand NewAccountCommand { get; private set; } = null!;

    public ICommand SaveAccountCommand { get; private set; } = null!;

    public ICommand DeleteAccountCommand { get; private set; } = null!;

    #endregion
}
