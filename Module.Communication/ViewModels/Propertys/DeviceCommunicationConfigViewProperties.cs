using Module.Communication.Models;
using Shared.Abstractions;
using Shared.Abstractions.Enum;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace Module.Communication.ViewModels;

/// <summary>
/// 设备通信配置界面的属性、字段和命令声明，供 XAML 绑定使用。
/// </summary>
public sealed partial class DeviceCommunicationConfigViewModel
{
    #region 常量与静态资源

    /// <summary>
    /// 接收日志允许保留的最大字符数，避免界面长期运行后文本过大。
    /// </summary>
    private const int MaxReceiveTextLength = 100_000;

    /// <summary>
    /// 通信配置文件本地存储目录。
    /// </summary>
    private static readonly string CommunicationConfigDirectory =
        Path.Combine(AppContext.BaseDirectory, "Config", "Communication");

    /// <summary>
    /// 协议配置文件本地存储目录。
    /// </summary>
    private static readonly string ProtocolConfigDirectory =
        Path.Combine(AppContext.BaseDirectory, "Config", "Protocol");

    /// <summary>
    /// 成功状态使用的高亮颜色。
    /// </summary>
    private static readonly Brush SuccessBrush =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16A34A"));

    /// <summary>
    /// 警告或失败状态使用的高亮颜色。
    /// </summary>
    private static readonly Brush WarningBrush =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EA580C"));

    /// <summary>
    /// 中性状态使用的高亮颜色。
    /// </summary>
    private static readonly Brush NeutralBrush =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));

    public bool IsProtocolConfigurationEditable => !_isConnectionEstablished;

    public string ProtocolConfigurationEditHint =>
        IsProtocolConfigurationEditable
            ? "Protocols can be edited."
            : "Close the active test connection before editing protocols.";

    #endregion

    #region 私有字段

    /// <summary>
    /// 记录通信配置与其落盘文件名的映射，便于保存和删除时同步处理旧文件。
    /// </summary>
    private readonly Dictionary<DeviceCommunicationProfile, string> _profileStorageFileNames = new();

    /// <summary>
    /// 当前选中的通信配置。
    /// </summary>
    private DeviceCommunicationProfile? _selectedProfile;

    /// <summary>
    /// 当前已经创建并启动的通信对象。
    /// </summary>
    private ICommunication? _activeCommunication;

    /// <summary>
    /// 当前通信对象的客户端来源，供 TCP 服务端刷新客户端列表。
    /// </summary>
    private ICommunicationClientSource? _activeClientSource;

    /// <summary>
    /// 当前活动通信对象对应的配置名称。
    /// </summary>
    private string? _activeProfileName;

    /// <summary>
    /// 当前活动通信对象的通信类型。
    /// </summary>
    private CommuniactionType? _activeCommunicationType;

    /// <summary>
    /// TCP 服务端模式下当前选中的客户端。
    /// </summary>
    private ConnectedClientOption? _selectedServerClient;

    /// <summary>
    /// 顶部通信状态文案。
    /// </summary>
    private string _connectionStatusText = "未连接";

    /// <summary>
    /// 顶部通信状态文案对应的画刷。
    /// </summary>
    private Brush _connectionStatusBrush = NeutralBrush;

    /// <summary>
    /// 报文发送文本框当前内容。
    /// </summary>
    private string _sendText = string.Empty;

    /// <summary>
    /// 接收区和日志区的完整文本内容。
    /// </summary>
    private string _receiveText = string.Empty;

    /// <summary>
    /// PLC 读写测试默认地址。
    /// </summary>
    private string _plcAddress = "D100";

    /// <summary>
    /// PLC 读写测试默认长度。
    /// </summary>
    private string _plcLength = "1";

    /// <summary>
    /// PLC 写入测试默认值。
    /// </summary>
    private string _plcWriteValue = "0";

    /// <summary>
    /// PLC 读写测试当前选择的数据类型。
    /// </summary>
    private string _selectedPlcDataType = DataType.Decimal.ToString();

    /// <summary>
    /// 左侧配置列表搜索关键字。
    /// </summary>
    private string _searchText = string.Empty;
    private string _availableProtocolSearchText = string.Empty;
    private string _supportedProtocolCommandSearchText = string.Empty;

    /// <summary>
    /// 协议列表抽屉是否处于打开状态。
    /// </summary>
    private bool _isProtocolLibraryOpen;

    /// <summary>
    /// 指令列表抽屉是否处于打开状态。
    /// </summary>
    private bool _isProtocolCommandLibraryOpen;
    private bool _isConnectionEstablished;
    private readonly List<BoundParseOnlyCommand> _activeParseOnlyCommands = new();

    #endregion

    #region 集合属性

    /// <summary>
    /// 当前页面维护的全部通信配置集合。
    /// </summary>
    public ObservableCollection<DeviceCommunicationProfile> Profiles { get; } = new();

    /// <summary>
    /// 通信配置列表视图，支持搜索过滤。
    /// </summary>
    public ICollectionView ProfilesView { get; private set; } = null!;

    public ICollectionView AvailableProtocolsView { get; private set; } = null!;

    public ICollectionView SupportedProtocolCommandsView { get; private set; } = null!;

    /// <summary>
    /// 通信类型下拉选项集合。
    /// </summary>
    public ObservableCollection<CommunicationTypeOption> CommunicationTypes { get; } = new();

    /// <summary>
    /// PLC 类型下拉选项集合。
    /// </summary>
    public ObservableCollection<SelectionOption> PLCTypes { get; } = new();

    /// <summary>
    /// 串口名称候选集合。
    /// </summary>
    public ObservableCollection<string> PortNameOptions { get; } = new();

    /// <summary>
    /// 波特率候选集合。
    /// </summary>
    public ObservableCollection<SelectionOption> BaudRateOptions { get; } = new();

    /// <summary>
    /// 串口校验位候选集合。
    /// </summary>
    public ObservableCollection<SelectionOption> ParityOptions { get; } = new();

    /// <summary>
    /// 串口数据位候选集合。
    /// </summary>
    public ObservableCollection<SelectionOption> DataBitOptions { get; } = new();

    /// <summary>
    /// 串口停止位候选集合。
    /// </summary>
    public ObservableCollection<SelectionOption> StopBitOptions { get; } = new();

    /// <summary>
    /// PLC 数据类型候选集合。
    /// </summary>
    public ObservableCollection<SelectionOption> PlcDataTypeOptions { get; } = new();

    /// <summary>
    /// TCP 服务端当前已连接客户端集合。
    /// </summary>
    public ObservableCollection<ConnectedClientOption> ConnectedServerClients { get; } = new();

    /// <summary>
    /// 本地协议库中可供关联的协议集合。
    /// </summary>
    public ObservableCollection<AvailableProtocolOption> AvailableProtocols { get; } = new();

    /// <summary>
    /// 当前选中通信配置所支持协议中的全部指令集合。
    /// </summary>
    public ObservableCollection<SupportedProtocolCommandOption> SupportedProtocolCommands { get; } = new();

    #endregion

    #region 页面状态属性

    /// <summary>
    /// 当前选中的通信配置。
    /// </summary>
    public DeviceCommunicationProfile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (ReferenceEquals(_selectedProfile, value))
            {
                return;
            }

            if (_selectedProfile is not null)
            {
                _selectedProfile.PropertyChanged -= SelectedProfile_PropertyChanged;
            }

            _selectedProfile = value;

            if (_selectedProfile is not null)
            {
                _selectedProfile.PropertyChanged += SelectedProfile_PropertyChanged;
                RefreshPortNameOptions(_selectedProfile.IsSerialType);
            }

            RefreshSupportedProtocolCommands();
            CloseProtocolCommandLibrary();
            OnPropertyChanged();
            RaiseCommunicationVisibilityChanged();
            RaiseCommandStatesChanged();
        }
    }

    /// <summary>
    /// 当前选中的 TCP 服务端客户端。
    /// </summary>
    public ConnectedClientOption? SelectedServerClient
    {
        get => _selectedServerClient;
        set => SetField(ref _selectedServerClient, value);
    }

    /// <summary>
    /// 配置列表搜索关键字。
    /// </summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetField(ref _searchText, value ?? string.Empty))
            {
                return;
            }

            ProfilesView?.Refresh();
        }
    }

    public string AvailableProtocolSearchText
    {
        get => _availableProtocolSearchText;
        set
        {
            if (!SetField(ref _availableProtocolSearchText, value ?? string.Empty))
            {
                return;
            }

            AvailableProtocolsView?.Refresh();
        }
    }

    public string SupportedProtocolCommandSearchText
    {
        get => _supportedProtocolCommandSearchText;
        set
        {
            if (!SetField(ref _supportedProtocolCommandSearchText, value ?? string.Empty))
            {
                return;
            }

            SupportedProtocolCommandsView?.Refresh();
        }
    }

    /// <summary>
    /// 当前待发送报文。
    /// </summary>
    public string SendText
    {
        get => _sendText;
        set => SetField(ref _sendText, value ?? string.Empty);
    }

    /// <summary>
    /// 当前接收日志文本。
    /// </summary>
    public string ReceiveText
    {
        get => _receiveText;
        private set => SetField(ref _receiveText, value ?? string.Empty);
    }

    /// <summary>
    /// PLC 测试地址。
    /// </summary>
    public string PlcAddress
    {
        get => _plcAddress;
        set => SetField(ref _plcAddress, value ?? string.Empty);
    }

    /// <summary>
    /// PLC 测试长度。
    /// </summary>
    public string PlcLength
    {
        get => _plcLength;
        set => SetField(ref _plcLength, value ?? string.Empty);
    }

    /// <summary>
    /// PLC 写入值。
    /// </summary>
    public string PlcWriteValue
    {
        get => _plcWriteValue;
        set => SetField(ref _plcWriteValue, value ?? string.Empty);
    }

    /// <summary>
    /// PLC 测试数据类型。
    /// </summary>
    public string SelectedPlcDataType
    {
        get => _selectedPlcDataType;
        set => SetField(ref _selectedPlcDataType, value ?? DataType.Decimal.ToString());
    }

    /// <summary>
    /// 协议列表抽屉是否打开。
    /// </summary>
    public bool IsProtocolLibraryOpen
    {
        get => _isProtocolLibraryOpen;
        private set
        {
            if (SetField(ref _isProtocolLibraryOpen, value))
            {
                RaiseCommandStatesChanged();
            }
        }
    }

    /// <summary>
    /// 指令列表抽屉是否打开。
    /// </summary>
    public bool IsProtocolCommandLibraryOpen
    {
        get => _isProtocolCommandLibraryOpen;
        private set
        {
            if (SetField(ref _isProtocolCommandLibraryOpen, value))
            {
                RaiseCommandStatesChanged();
            }
        }
    }

    /// <summary>
    /// 通信状态文案。
    /// </summary>
    public string ConnectionStatusText
    {
        get => _connectionStatusText;
        private set => SetField(ref _connectionStatusText, value);
    }

    /// <summary>
    /// 通信状态颜色。
    /// </summary>
    public Brush ConnectionStatusBrush
    {
        get => _connectionStatusBrush;
        private set => SetField(ref _connectionStatusBrush, value);
    }

    /// <summary>
    /// 是否显示 TCP 服务端客户端选择区域。
    /// </summary>
    public bool IsTcpServerClientSelectionVisible =>
        SelectedProfile?.Type == CommuniactionType.TCPServer ||
        _activeCommunicationType == CommuniactionType.TCPServer;

    /// <summary>
    /// 是否显示 PLC 读写测试区域。
    /// </summary>
    public bool IsPlcTestVisible => SelectedProfile?.Type == CommuniactionType.PLC;

    /// <summary>
    /// 是否显示通用报文发送区域。
    /// </summary>
    public bool IsGenericSendTestVisible => !IsPlcTestVisible;

    /// <summary>
    /// 已连接 TCP 服务端客户端状态文案。
    /// </summary>
    public string ConnectedServerClientStatusText =>
        ConnectedServerClients.Count == 0
            ? "暂无已连接客户端"
            : $"已连接 {ConnectedServerClients.Count} 个客户端";

    #endregion

    #region 命令属性

    /// <summary>
    /// 新建通信配置命令。
    /// </summary>
    public ICommand NewProfileCommand { get; private set; } = null!;

    /// <summary>
    /// 复制通信配置命令。
    /// </summary>
    public ICommand DuplicateProfileCommand { get; private set; } = null!;

    /// <summary>
    /// 删除通信配置命令。
    /// </summary>
    public ICommand DeleteProfileCommand { get; private set; } = null!;

    /// <summary>
    /// 保存通信配置命令。
    /// </summary>
    public ICommand SaveProfilesCommand { get; private set; } = null!;

    /// <summary>
    /// 打开协议列表抽屉命令。
    /// </summary>
    public ICommand AddSupportedProtocolCommand { get; private set; } = null!;

    /// <summary>
    /// 将协议库中的协议添加到当前通信配置命令。
    /// </summary>
    public ICommand AddAvailableProtocolCommand { get; private set; } = null!;

    /// <summary>
    /// 删除支持协议命令。
    /// </summary>
    public ICommand DeleteSupportedProtocolCommand { get; private set; } = null!;

    /// <summary>
    /// 加载本地协议文件命令。
    /// </summary>
    public ICommand LoadSupportedProtocolFileCommand { get; private set; } = null!;

    /// <summary>
    /// 打开指令列表抽屉命令。
    /// </summary>
    public ICommand OpenProtocolCommandLibraryCommand { get; private set; } = null!;

    /// <summary>
    /// 双击指令后填充报文命令。
    /// </summary>
    public ICommand FillSupportedProtocolCommandCommand { get; private set; } = null!;

    /// <summary>
    /// 关闭协议列表抽屉命令。
    /// </summary>
    public ICommand CloseProtocolLibraryCommand { get; private set; } = null!;

    /// <summary>
    /// 关闭指令列表抽屉命令。
    /// </summary>
    public ICommand CloseProtocolCommandLibraryCommand { get; private set; } = null!;

    /// <summary>
    /// 创建并测试通信连接命令。
    /// </summary>
    public ICommand TestConnectionCommand { get; private set; } = null!;

    /// <summary>
    /// 发送当前报文命令。
    /// </summary>
    public ICommand SendCommand { get; private set; } = null!;

    /// <summary>
    /// TCP 服务端群发命令。
    /// </summary>
    public ICommand SendAllCommand { get; private set; } = null!;

    /// <summary>
    /// PLC 读取命令。
    /// </summary>
    public ICommand ReadPlcCommand { get; private set; } = null!;

    /// <summary>
    /// PLC 写入命令。
    /// </summary>
    public ICommand WritePlcCommand { get; private set; } = null!;

    /// <summary>
    /// 关闭当前测试连接命令。
    /// </summary>
    public ICommand CloseConnectionCommand { get; private set; } = null!;

    /// <summary>
    /// 清空日志命令。
    /// </summary>
    public ICommand ClearReceiveCommand { get; private set; } = null!;

    /// <summary>
    /// 刷新串口列表命令。
    /// </summary>
    public ICommand RefreshPortsCommand { get; private set; } = null!;

    #endregion

    #region 属性变更处理

    /// <summary>
    /// 监听当前选中通信配置的属性变化，并刷新界面依赖状态。
    /// </summary>
    private void SelectedProfile_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DeviceCommunicationProfile.Type))
        {
            RefreshPortNameOptions(SelectedProfile?.IsSerialType == true);
            RaiseCommunicationVisibilityChanged();
        }

        if (e.PropertyName is nameof(DeviceCommunicationProfile.LocalName) or
            nameof(DeviceCommunicationProfile.Summary) or
            nameof(DeviceCommunicationProfile.SupportedProtocolsSummary))
        {
            ProfilesView?.Refresh();
        }

        if (e.PropertyName is nameof(DeviceCommunicationProfile.SupportedProtocolsSummary))
        {
            RefreshSupportedProtocolCommands();
        }

        RaiseCommandStatesChanged();
    }

    /// <summary>
    /// 通知界面刷新不同通信模式下的可见性绑定。
    /// </summary>
    private void RaiseCommunicationVisibilityChanged()
    {
        OnPropertyChanged(nameof(IsTcpServerClientSelectionVisible));
        OnPropertyChanged(nameof(IsPlcTestVisible));
        OnPropertyChanged(nameof(IsGenericSendTestVisible));
    }

    private sealed record BoundParseOnlyCommand(
        string ProtocolName,
        string CommandName,
        ProtocolCommandConfig Command);

    #endregion
}
