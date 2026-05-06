using Module.Communication.Models;
using Shared.Abstractions;
using Shared.Abstractions.Enum;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Input;
using System.Windows.Media;

namespace Module.Communication.ViewModels;

/// <summary>
/// 设备通信配置界面的属性、字段和命令声明，供 XAML 绑定使用。
/// </summary>
public sealed partial class DeviceCommunicationConfigViewModel
{
    #region 状态颜色与路径字段
    private const int MaxReceiveTextLength = 100_000;

    private static readonly string CommunicationConfigDirectory =
        Path.Combine(AppContext.BaseDirectory, "Config", "Communication");

    private static readonly Brush SuccessBrush =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16A34A"));

    private static readonly Brush WarningBrush =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EA580C"));

    private static readonly Brush NeutralBrush =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));

    #endregion

    #region 私有状态字段
    private readonly Dictionary<DeviceCommunicationProfile, string> _profileStorageFileNames = new();
    private DeviceCommunicationProfile? _selectedProfile;
    private ICommunication? _activeCommunication;
    private ICommunicationClientSource? _activeClientSource;
    private string? _activeProfileName;
    private CommuniactionType? _activeCommunicationType;
    private ConnectedClientOption? _selectedServerClient;
    private string _connectionStatusText = "未连接";
    private Brush _connectionStatusBrush = NeutralBrush;
    private string _sendText = string.Empty;
    private string _receiveText = string.Empty;
    private string _plcAddress = "D100";
    private string _plcLength = "1";
    private string _plcWriteValue = "0";
    private string _selectedPlcDataType = DataType.Decimal.ToString();

    #endregion

    #region 集合属性
    public ObservableCollection<DeviceCommunicationProfile> Profiles { get; } = new();

    public ObservableCollection<CommunicationTypeOption> CommunicationTypes { get; } = new();

    public ObservableCollection<SelectionOption> PLCTypes { get; } = new();

    public ObservableCollection<string> PortNameOptions { get; } = new();

    public ObservableCollection<SelectionOption> BaudRateOptions { get; } = new();

    public ObservableCollection<SelectionOption> ParityOptions { get; } = new();

    public ObservableCollection<SelectionOption> DataBitOptions { get; } = new();

    public ObservableCollection<SelectionOption> StopBitOptions { get; } = new();

    public ObservableCollection<SelectionOption> PlcDataTypeOptions { get; } = new();

    public ObservableCollection<ConnectedClientOption> ConnectedServerClients { get; } = new();

    #endregion

    #region 当前编辑属性
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

            OnPropertyChanged();
            RaiseCommunicationVisibilityChanged();
            RaiseCommandStatesChanged();
        }
    }

    public ConnectedClientOption? SelectedServerClient
    {
        get => _selectedServerClient;
        set => SetField(ref _selectedServerClient, value);
    }

    public string SendText
    {
        get => _sendText;
        set => SetField(ref _sendText, value ?? string.Empty);
    }

    public string ReceiveText
    {
        get => _receiveText;
        private set => SetField(ref _receiveText, value ?? string.Empty);
    }

    public string PlcAddress
    {
        get => _plcAddress;
        set => SetField(ref _plcAddress, value ?? string.Empty);
    }

    public string PlcLength
    {
        get => _plcLength;
        set => SetField(ref _plcLength, value ?? string.Empty);
    }

    public string PlcWriteValue
    {
        get => _plcWriteValue;
        set => SetField(ref _plcWriteValue, value ?? string.Empty);
    }

    public string SelectedPlcDataType
    {
        get => _selectedPlcDataType;
        set => SetField(ref _selectedPlcDataType, value ?? DataType.Decimal.ToString());
    }

    #endregion

    #region 页面状态属性
    public string ConnectionStatusText
    {
        get => _connectionStatusText;
        private set => SetField(ref _connectionStatusText, value);
    }

    public Brush ConnectionStatusBrush
    {
        get => _connectionStatusBrush;
        private set => SetField(ref _connectionStatusBrush, value);
    }

    public bool IsTcpServerClientSelectionVisible =>
        SelectedProfile?.Type == CommuniactionType.TCPServer ||
        _activeCommunicationType == CommuniactionType.TCPServer;

    public bool IsPlcTestVisible => SelectedProfile?.Type == CommuniactionType.PLC;

    public bool IsGenericSendTestVisible => !IsPlcTestVisible;

    public string ConnectedServerClientStatusText =>
        ConnectedServerClients.Count == 0
            ? "当前无已连接客户端"
            : $"当前已连接 {ConnectedServerClients.Count} 个客户端";

    #endregion

    #region 命令属性
    public ICommand NewProfileCommand { get; private set; } = null!;

    public ICommand DuplicateProfileCommand { get; private set; } = null!;

    public ICommand DeleteProfileCommand { get; private set; } = null!;

    public ICommand SaveProfilesCommand { get; private set; } = null!;

    public ICommand TestConnectionCommand { get; private set; } = null!;

    public ICommand SendCommand { get; private set; } = null!;

    public ICommand SendAllCommand { get; private set; } = null!;

    public ICommand ReadPlcCommand { get; private set; } = null!;

    public ICommand WritePlcCommand { get; private set; } = null!;

    public ICommand CloseConnectionCommand { get; private set; } = null!;

    public ICommand ClearReceiveCommand { get; private set; } = null!;

    public ICommand RefreshPortsCommand { get; private set; } = null!;

    #endregion

    #region 属性联动方法
    private void SelectedProfile_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DeviceCommunicationProfile.Type))
        {
            RefreshPortNameOptions(SelectedProfile?.IsSerialType == true);
            RaiseCommunicationVisibilityChanged();
        }

        RaiseCommandStatesChanged();
    }

    private void RaiseCommunicationVisibilityChanged()
    {
        OnPropertyChanged(nameof(IsTcpServerClientSelectionVisible));
        OnPropertyChanged(nameof(IsPlcTestVisible));
        OnPropertyChanged(nameof(IsGenericSendTestVisible));
    }

    #endregion
}
