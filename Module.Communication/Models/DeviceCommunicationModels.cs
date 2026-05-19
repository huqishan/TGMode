using Module.Communication.ViewModels.PropertyVMs;
using Shared.Abstractions.Enum;
using Shared.Models.Communication;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;

namespace Module.Communication.Models
{
    public sealed class CommunicationTypeOption
    {
        public CommunicationTypeOption(CommuniactionType value, string displayName, string description)
        {
            Value = value;
            DisplayName = displayName;
            Description = description;
        }

        public CommuniactionType Value { get; }

        public string DisplayName { get; }

        public string Description { get; }
    }

    public sealed class SelectionOption
    {
        public SelectionOption(string value, string displayName)
        {
            Value = value;
            DisplayName = displayName;
        }

        public string Value { get; }

        public string DisplayName { get; }
    }

    public sealed class ConnectedClientOption
    {
        public ConnectedClientOption(string clientId, string displayName, string address, int port)
        {
            ClientId = clientId;
            DisplayName = displayName;
            Address = address;
            Port = port;
        }

        public string ClientId { get; }

        public string DisplayName { get; }

        public string Address { get; }

        public int Port { get; }
    }

    internal sealed class DeviceSupportedProtocolDocument
    {
        public string? ProtocolName { get; set; }

        public string? ProtocolFilePath { get; set; }

        public static DeviceSupportedProtocolDocument FromModel(DeviceSupportedProtocol protocol)
        {
            return new DeviceSupportedProtocolDocument
            {
                ProtocolName = protocol.ProtocolName,
                ProtocolFilePath = protocol.ProtocolFilePath
            };
        }

        public DeviceSupportedProtocol ToModel()
        {
            return new DeviceSupportedProtocol
            {
                ProtocolName = ProtocolName ?? string.Empty,
                ProtocolFilePath = ProtocolFilePath ?? string.Empty
            };
        }
    }

    public sealed class AvailableProtocolOption
    {
        public AvailableProtocolOption(string name, string filePath, string summary)
        {
            Name = name;
            FilePath = filePath;
            Summary = summary;
        }

        public string Name { get; }

        public string FilePath { get; }

        public string Summary { get; }
    }

    public sealed class SupportedProtocolCommandOption
    {
        public SupportedProtocolCommandOption(
            string protocolName,
            string protocolFilePath,
            string commandName,
            string summary,
            string previewMessage,
            string fillMessage,
            bool canFill)
        {
            ProtocolName = protocolName;
            ProtocolFilePath = protocolFilePath;
            CommandName = commandName;
            Summary = summary;
            PreviewMessage = previewMessage;
            FillMessage = fillMessage;
            CanFill = canFill;
        }

        public string ProtocolName { get; }

        public string ProtocolFilePath { get; }

        public string CommandName { get; }

        public string Summary { get; }

        public string PreviewMessage { get; }

        public string FillMessage { get; }

        public bool CanFill { get; }

        public string DisplayName => $"{ProtocolName} / {CommandName}";
    }

    internal sealed class DeviceCommunicationProfileDocument
    {
        public int Version { get; set; } = 2;

        public string? LocalName { get; set; }

        public CommuniactionType Type { get; set; } = CommuniactionType.TCPClient;

        public string? LocalIPAddress { get; set; }

        public string? LocalPort { get; set; }

        public string? RemoteIPAddress { get; set; }

        public string? RemotePort { get; set; }

        public string? PortName { get; set; }

        public string? BaudRate { get; set; }

        public string? Parity { get; set; }

        public string? DataBits { get; set; }

        public string? StopBits { get; set; }

        public string? PLCActLogicalStationNumber { get; set; }

        public string? PLCType { get; set; }

        public string? PLCPassword { get; set; }

        public List<DeviceSupportedProtocolDocument>? SupportedProtocols { get; set; }

        public static DeviceCommunicationProfileDocument FromProfile(DeviceCommunicationProfile profile)
        {
            return new DeviceCommunicationProfileDocument
            {
                LocalName = profile.LocalName,
                Type = profile.Type,
                LocalIPAddress = profile.LocalIPAddress,
                LocalPort = profile.LocalPort,
                RemoteIPAddress = profile.RemoteIPAddress,
                RemotePort = profile.RemotePort,
                PortName = profile.PortName,
                BaudRate = profile.BaudRate,
                Parity = profile.Parity,
                DataBits = profile.DataBits,
                StopBits = profile.StopBits,
                PLCActLogicalStationNumber = profile.PLCActLogicalStationNumber,
                PLCType = profile.PLCType,
                PLCPassword = profile.PLCPassword,
                SupportedProtocols = profile.SupportedProtocols
                    .Where(protocol => !protocol.IsEmpty)
                    .Select(DeviceSupportedProtocolDocument.FromModel)
                    .ToList()
            };
        }

        public DeviceCommunicationProfile ToProfile()
        {
            DeviceCommunicationProfile profile = new DeviceCommunicationProfile
            {
                LocalName = string.IsNullOrWhiteSpace(LocalName) ? "通信配置" : LocalName.Trim(),
                Type = Type
            };
            profile.ResetToCurrentTypeDefaults();

            profile.LocalIPAddress = string.IsNullOrWhiteSpace(LocalIPAddress) ? profile.LocalIPAddress : LocalIPAddress.Trim();
            profile.LocalPort = string.IsNullOrWhiteSpace(LocalPort) ? profile.LocalPort : LocalPort.Trim();
            profile.RemoteIPAddress = string.IsNullOrWhiteSpace(RemoteIPAddress) ? profile.RemoteIPAddress : RemoteIPAddress.Trim();
            profile.RemotePort = string.IsNullOrWhiteSpace(RemotePort) ? profile.RemotePort : RemotePort.Trim();
            profile.PortName = string.IsNullOrWhiteSpace(PortName) ? profile.PortName : PortName.Trim();
            profile.BaudRate = string.IsNullOrWhiteSpace(BaudRate) ? profile.BaudRate : BaudRate.Trim();
            profile.Parity = string.IsNullOrWhiteSpace(Parity) ? profile.Parity : Parity.Trim();
            profile.DataBits = string.IsNullOrWhiteSpace(DataBits) ? profile.DataBits : DataBits.Trim();
            profile.StopBits = string.IsNullOrWhiteSpace(StopBits) ? profile.StopBits : StopBits.Trim();
            profile.PLCActLogicalStationNumber = string.IsNullOrWhiteSpace(PLCActLogicalStationNumber)
                ? profile.PLCActLogicalStationNumber
                : PLCActLogicalStationNumber.Trim();
            profile.PLCType = string.IsNullOrWhiteSpace(PLCType)
                ? profile.PLCType
                : PLCType.Trim();
            profile.PLCPassword = string.IsNullOrWhiteSpace(PLCPassword) ? profile.PLCPassword : PLCPassword.Trim();

            if (SupportedProtocols is { Count: > 0 })
            {
                foreach (DeviceSupportedProtocolDocument protocolDocument in SupportedProtocols)
                {
                    DeviceSupportedProtocol protocol = protocolDocument.ToModel();
                    if (!protocol.IsEmpty)
                    {
                        profile.SupportedProtocols.Add(protocol);
                    }
                }
            }

            return profile;
        }
    }

    public sealed class DeviceCommunicationProfile : INotifyPropertyChanged
    {
        private string _localName = "TCP客户端 1";
        private CommuniactionType _type = CommuniactionType.TCPClient;
        private string _localIpAddress = "127.0.0.1";
        private string _localPort = "0";
        private string _remoteIpAddress = "127.0.0.1";
        private string _remotePort = "502";
        private string _portName = "COM1";
        private string _baudRate = "9600";
        private string _parity = "0";
        private string _dataBits = "8";
        private string _stopBits = "1";
        private string _plcActLogicalStationNumber = "0";
        private string _plcType = PlcCommunicationTypeNames.MX;
        private string _plcPassword = string.Empty;

        public DeviceCommunicationProfile()
        {
            SupportedProtocols.CollectionChanged += SupportedProtocols_CollectionChanged;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public ObservableCollection<DeviceSupportedProtocol> SupportedProtocols { get; } = new ObservableCollection<DeviceSupportedProtocol>();

        public string LocalName
        {
            get => _localName;
            set => SetField(ref _localName, value, true);
        }

        public CommuniactionType Type
        {
            get => _type;
            set
            {
                if (SetField(ref _type, value, false))
                {
                    RaiseTypeStateChanged();
                }
            }
        }

        public string LocalIPAddress
        {
            get => _localIpAddress;
            set => SetField(ref _localIpAddress, value, true);
        }

        public string LocalPort
        {
            get => _localPort;
            set => SetField(ref _localPort, value, true);
        }

        public string RemoteIPAddress
        {
            get => _remoteIpAddress;
            set => SetField(ref _remoteIpAddress, value, true);
        }

        public string RemotePort
        {
            get => _remotePort;
            set => SetField(ref _remotePort, value, true);
        }

        public string PortName
        {
            get => _portName;
            set => SetField(ref _portName, value, true);
        }

        public string BaudRate
        {
            get => _baudRate;
            set => SetField(ref _baudRate, value, true);
        }

        public string Parity
        {
            get => _parity;
            set => SetField(ref _parity, value, true);
        }

        public string DataBits
        {
            get => _dataBits;
            set => SetField(ref _dataBits, value, true);
        }

        public string StopBits
        {
            get => _stopBits;
            set => SetField(ref _stopBits, value, true);
        }

        public string PLCActLogicalStationNumber
        {
            get => _plcActLogicalStationNumber;
            set => SetField(ref _plcActLogicalStationNumber, value, true);
        }

        public string PLCType
        {
            get => _plcType;
            set
            {
                if (SetField(ref _plcType, PlcCommunicationTypeNames.Normalize(value), true))
                {
                    OnPropertyChanged(nameof(TypeDescription));
                }
            }
        }

        public string PLCPassword
        {
            get => _plcPassword;
            set => SetField(ref _plcPassword, value, true);
        }

        public bool IsNetworkType => Type is CommuniactionType.TCPClient or CommuniactionType.TCPServer or CommuniactionType.UDP;

        public bool UsesRemoteEndpoint => Type is CommuniactionType.TCPClient or CommuniactionType.UDP or CommuniactionType.PLC;

        public bool UsesLocalEndpoint => Type is CommuniactionType.TCPClient or CommuniactionType.TCPServer or CommuniactionType.UDP or CommuniactionType.PLC;

        public bool IsSerialType => Type == CommuniactionType.COM;

        public bool IsPlcType => Type == CommuniactionType.PLC;

        public string TypeDisplayName => Type switch
        {
            CommuniactionType.TCPClient => "TCP客户端",
            CommuniactionType.TCPServer => "TCP服务端",
            CommuniactionType.UDP => "UDP",
            CommuniactionType.COM => "串口",
            CommuniactionType.PLC => "PLC",
            _ => Type.ToString()
        };

        public string TypeDescription => Type switch
        {
            CommuniactionType.TCPClient => "主动连接远端设备。",
            CommuniactionType.TCPServer => "启动本地监听端口并等待客户端接入。",
            CommuniactionType.UDP => "使用无连接报文进行轻量级设备通信。",
            CommuniactionType.COM => "用于串口设备通信。",
            CommuniactionType.PLC => PlcCommunicationTypeNames.IsModbus(PLCType)
                ? "使用 Modbus TCP 的 PLC 通信。"
                : "使用三菱 MX 逻辑站模式的 PLC 通信。",
            _ => "当前通信类型暂无描述。"
        };

        public string Summary => Type switch
        {
            CommuniactionType.TCPClient => $"远端 {RemoteIPAddress}:{RemotePort}  本地 {LocalIPAddress}:{LocalPort}",
            CommuniactionType.TCPServer => $"监听 {LocalIPAddress}:{LocalPort}",
            CommuniactionType.UDP => $"远端 {RemoteIPAddress}:{RemotePort}  本地 {LocalIPAddress}:{LocalPort}",
            CommuniactionType.COM => $"{PortName}  波特率 {BaudRate}bps  校验位 {Parity}  数据位 {DataBits}  停止位 {StopBits}",
            CommuniactionType.PLC => PlcCommunicationTypeNames.IsModbus(PLCType)
                ? $"PLC {PLCType}  远端 {RemoteIPAddress}:{RemotePort}"
                : $"PLC {PLCType}  逻辑站号 {PLCActLogicalStationNumber}",
            _ => "未配置"
        };

        public string SupportedProtocolsSummary
        {
            get
            {
                string[] protocolNames = SupportedProtocols
                    .Select(item => item.ProtocolName?.Trim() ?? string.Empty)
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                return protocolNames.Length == 0 ? "无" : string.Join("&", protocolNames);
            }
        }

        public string SupportedProtocolsDisplayText => $"支持协议：{SupportedProtocolsSummary}";

        public DeviceCommunicationProfile Clone(string localName)
        {
            DeviceCommunicationProfile clone = new DeviceCommunicationProfile
            {
                LocalName = localName,
                Type = Type,
                LocalIPAddress = LocalIPAddress,
                LocalPort = LocalPort,
                RemoteIPAddress = RemoteIPAddress,
                RemotePort = RemotePort,
                PortName = PortName,
                BaudRate = BaudRate,
                Parity = Parity,
                DataBits = DataBits,
                StopBits = StopBits,
                PLCActLogicalStationNumber = PLCActLogicalStationNumber,
                PLCType = PLCType,
                PLCPassword = PLCPassword
            };

            foreach (DeviceSupportedProtocol protocol in SupportedProtocols.Where(item => !item.IsEmpty))
            {
                clone.SupportedProtocols.Add(new DeviceSupportedProtocol
                {
                    ProtocolName = protocol.ProtocolName,
                    ProtocolFilePath = protocol.ProtocolFilePath
                });
            }

            return clone;
        }

        public void ResetToCurrentTypeDefaults()
        {
            switch (Type)
            {
                case CommuniactionType.TCPClient:
                    LocalIPAddress = "127.0.0.1";
                    LocalPort = "0";
                    RemoteIPAddress = "127.0.0.1";
                    RemotePort = "502";
                    break;
                case CommuniactionType.TCPServer:
                    LocalIPAddress = "127.0.0.1";
                    LocalPort = "6000";
                    break;
                case CommuniactionType.UDP:
                    LocalIPAddress = "127.0.0.1";
                    LocalPort = "7001";
                    RemoteIPAddress = "127.0.0.1";
                    RemotePort = "7000";
                    break;
                case CommuniactionType.COM:
                    PortName = "COM1";
                    BaudRate = "9600";
                    Parity = "0";
                    DataBits = "8";
                    StopBits = "1";
                    break;
                case CommuniactionType.PLC:
                    LocalIPAddress = "127.0.0.1";
                    LocalPort = "0";
                    RemoteIPAddress = "127.0.0.1";
                    RemotePort = "502";
                    PLCActLogicalStationNumber = "0";
                    PLCType = PlcCommunicationTypeNames.Normalize(PLCType);
                    PLCPassword = string.Empty;
                    break;
            }
        }

        public bool TryBuildRuntimeConfig(out CommuniactionConfigModel? config, out string validationMessage)
        {
            config = null;
            if (string.IsNullOrWhiteSpace(LocalName))
            {
                validationMessage = "配置名称不能为空。";
                return false;
            }

            switch (Type)
            {
                case CommuniactionType.TCPClient:
                case CommuniactionType.UDP:
                    if (!TryValidateIpAddress(RemoteIPAddress, "远端 IP 地址", out validationMessage) ||
                        !TryValidatePort(RemotePort, "远端端口", true, out int remotePort, out validationMessage) ||
                        !TryValidateIpAddress(LocalIPAddress, "本地 IP 地址", out validationMessage) ||
                        !TryValidatePort(LocalPort, "本地端口", false, out int localPort, out validationMessage))
                    {
                        return false;
                    }

                    config = new CommuniactionConfigModel(
                        Type == CommuniactionType.UDP,
                        LocalName.Trim(),
                        RemoteIPAddress.Trim(),
                        remotePort,
                        LocalIPAddress.Trim(),
                        localPort);
                    validationMessage = $"{TypeDisplayName}配置有效。";
                    return true;

                case CommuniactionType.TCPServer:
                    if (!TryValidateIpAddress(LocalIPAddress, "本地监听 IP 地址", out validationMessage) ||
                        !TryValidatePort(LocalPort, "本地监听端口", true, out int serverPort, out validationMessage))
                    {
                        return false;
                    }

                    config = new CommuniactionConfigModel(false, LocalName.Trim(), LocalIPAddress.Trim(), (ushort)serverPort);
                    validationMessage = "TCP服务端配置有效。";
                    return true;

                case CommuniactionType.COM:
                    if (string.IsNullOrWhiteSpace(PortName))
                    {
                        validationMessage = "端口名称不能为空。";
                        return false;
                    }

                    if (!TryValidatePositiveNumber(BaudRate, "波特率", out int baudRate, out validationMessage) ||
                        !TryValidateNumberInRange(Parity, "校验位", 0, 4, out int parity, out validationMessage) ||
                        !TryValidatePositiveNumber(DataBits, "数据位", out int dataBits, out validationMessage) ||
                        !TryValidateNumberInRange(StopBits, "停止位", 0, 3, out int stopBits, out validationMessage))
                    {
                        return false;
                    }

                    config = new CommuniactionConfigModel(LocalName.Trim(), PortName.Trim(), baudRate, parity, dataBits, stopBits);
                    validationMessage = "串口配置有效。";
                    return true;

                case CommuniactionType.PLC:
                    string plcType = PlcCommunicationTypeNames.Normalize(PLCType);
                    if (PlcCommunicationTypeNames.IsModbus(plcType))
                    {
                        if (!TryValidateIpAddress(RemoteIPAddress, "远端 IP 地址", out validationMessage) ||
                            !TryValidatePort(RemotePort, "远端端口", true, out int plcRemotePort, out validationMessage) ||
                            !TryValidateIpAddress(LocalIPAddress, "本地 IP 地址", out validationMessage) ||
                            !TryValidatePort(LocalPort, "本地端口", false, out int plcLocalPort, out validationMessage))
                        {
                            return false;
                        }

                        config = new CommuniactionConfigModel(
                            CommuniactionType.PLC,
                            LocalName.Trim(),
                            RemoteIPAddress.Trim(),
                            plcRemotePort,
                            LocalIPAddress.Trim(),
                            plcLocalPort,
                            plcType);
                        validationMessage = "PLC Modbus 配置有效。";
                        return true;
                    }

                    if (!TryValidateNumberInRange(
                            PLCActLogicalStationNumber,
                            "PLC 逻辑站号",
                            0,
                            1023,
                            out int stationNumber,
                            out validationMessage))
                    {
                        return false;
                    }

                    config = new CommuniactionConfigModel(
                        CommuniactionType.MX,
                        LocalName.Trim(),
                        stationNumber,
                        string.IsNullOrWhiteSpace(PLCPassword) ? string.Empty : PLCPassword.Trim());
                    validationMessage = "PLC MX 配置有效。";
                    return true;

                default:
                    validationMessage = "当前通信类型暂不支持。";
                    return false;
            }
        }

        private bool SetField<T>(ref T field, T value, bool raiseStateChanges, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value))
            {
                return false;
            }

            field = value;
            OnPropertyChanged(propertyName);
            if (raiseStateChanges)
            {
                RaiseStateChanged();
            }

            return true;
        }

        private void RaiseTypeStateChanged()
        {
            RaiseStateChanged();
            OnPropertyChanged(nameof(IsNetworkType));
            OnPropertyChanged(nameof(UsesRemoteEndpoint));
            OnPropertyChanged(nameof(UsesLocalEndpoint));
            OnPropertyChanged(nameof(IsSerialType));
            OnPropertyChanged(nameof(IsPlcType));
            OnPropertyChanged(nameof(TypeDisplayName));
            OnPropertyChanged(nameof(TypeDescription));
        }

        private void RaiseStateChanged()
        {
            OnPropertyChanged(nameof(Summary));
            OnPropertyChanged(nameof(SupportedProtocolsSummary));
            OnPropertyChanged(nameof(SupportedProtocolsDisplayText));
        }

        private void SupportedProtocols_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems is not null)
            {
                foreach (DeviceSupportedProtocol protocol in e.NewItems.OfType<DeviceSupportedProtocol>())
                {
                    protocol.PropertyChanged += SupportedProtocol_PropertyChanged;
                }
            }

            if (e.OldItems is not null)
            {
                foreach (DeviceSupportedProtocol protocol in e.OldItems.OfType<DeviceSupportedProtocol>())
                {
                    protocol.PropertyChanged -= SupportedProtocol_PropertyChanged;
                }
            }

            RaiseStateChanged();
        }

        private void SupportedProtocol_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(DeviceSupportedProtocol.ProtocolName) or nameof(DeviceSupportedProtocol.ProtocolFilePath))
            {
                RaiseStateChanged();
            }
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private static bool TryValidateIpAddress(string value, string fieldName, out string validationMessage)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                validationMessage = $"{fieldName}不能为空。";
                return false;
            }

            if (!IPAddress.TryParse(value.Trim(), out _))
            {
                validationMessage = $"{fieldName}格式无效。";
                return false;
            }

            validationMessage = string.Empty;
            return true;
        }

        private static bool TryValidatePort(string value, string fieldName, bool isRequired, out int port, out string validationMessage)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                if (!isRequired)
                {
                    port = 0;
                    validationMessage = string.Empty;
                    return true;
                }

                port = 0;
                validationMessage = $"{fieldName}不能为空。";
                return false;
            }

            if (!int.TryParse(value.Trim(), out port) || port < 0 || port > ushort.MaxValue)
            {
                validationMessage = $"{fieldName}必须是 0 到 65535 之间的数字。";
                return false;
            }

            if (isRequired && port == 0)
            {
                validationMessage = $"{fieldName}必须大于 0。";
                return false;
            }

            validationMessage = string.Empty;
            return true;
        }

        private static bool TryValidatePositiveNumber(string value, string fieldName, out int number, out string validationMessage)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                number = 0;
                validationMessage = $"{fieldName}不能为空。";
                return false;
            }

            if (!int.TryParse(value.Trim(), out number) || number <= 0)
            {
                validationMessage = $"{fieldName}必须大于 0。";
                return false;
            }

            validationMessage = string.Empty;
            return true;
        }

        private static bool TryValidateNumberInRange(string value, string fieldName, int minValue, int maxValue, out int number, out string validationMessage)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                number = 0;
                validationMessage = $"{fieldName}不能为空。";
                return false;
            }

            if (!int.TryParse(value.Trim(), out number) || number < minValue || number > maxValue)
            {
                validationMessage = $"{fieldName}必须在 {minValue} 到 {maxValue} 之间。";
                return false;
            }

            validationMessage = string.Empty;
            return true;
        }
    }
}
