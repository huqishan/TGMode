using Shared.Abstractions.Enum;
using Shared.Models.Communication;
using System;
using System.ComponentModel;
using System.Net;
using System.Runtime.CompilerServices;

namespace ControlLibrary.ControlViews.Communication.Models
{
    /// <summary>
    /// Communication type option shown in the editor selector.
    /// </summary>
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

    /// <summary>
    /// Simple key/value option for serial parameter dropdowns.
    /// </summary>
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


    /// <summary>
    /// TCP Server 当前已连接客户端的界面绑定对象。
    /// </summary>
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

    /// <summary>
    /// 一个通信配置对应一个 JSON 文件；这里使用独立 DTO，避免直接序列化只读绑定属性。
    /// </summary>
    internal sealed class DeviceCommunicationProfileDocument
    {
        public int Version { get; set; } = 1;

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
                StopBits = profile.StopBits
            };
        }

        public DeviceCommunicationProfile ToProfile()
        {
            DeviceCommunicationProfile profile = new DeviceCommunicationProfile
            {
                LocalName = string.IsNullOrWhiteSpace(LocalName) ? "Communication" : LocalName.Trim(),
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
            return profile;
        }
    }

    /// <summary>
    /// Editable communication profile used by the configuration page.
    /// </summary>
    public sealed class DeviceCommunicationProfile : INotifyPropertyChanged
    {
        private string _localName = "TCP Client 1";
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

        public event PropertyChangedEventHandler? PropertyChanged;

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

        public bool IsNetworkType => Type is CommuniactionType.TCPClient or CommuniactionType.TCPServer or CommuniactionType.UDP;

        public bool UsesRemoteEndpoint => Type is CommuniactionType.TCPClient or CommuniactionType.UDP;

        public bool UsesLocalEndpoint => Type is CommuniactionType.TCPClient or CommuniactionType.TCPServer or CommuniactionType.UDP;

        public bool IsSerialType => Type == CommuniactionType.COM;

        public string TypeDisplayName => Type switch
        {
            CommuniactionType.TCPClient => "TCP Client",
            CommuniactionType.TCPServer => "TCP Server",
            CommuniactionType.UDP => "UDP",
            CommuniactionType.COM => "COM",
            _ => Type.ToString()
        };

        public string TypeDescription => Type switch
        {
            CommuniactionType.TCPClient => "主动连接远端设备，适合 PLC、扫码枪或传感器客户端。",
            CommuniactionType.TCPServer => "本地开启监听端口，等待设备或上位机主动接入。",
            CommuniactionType.UDP => "无连接报文通信，适合广播、状态上报和轻量级设备交互。",
            CommuniactionType.COM => "串口通信，适合天平、扫码器、打印机等串口设备。",
            _ => "当前通信方式暂未提供说明。"
        };

        public string Summary => Type switch
        {
            CommuniactionType.TCPClient => $"远端 {RemoteIPAddress}:{RemotePort}  本地 {LocalIPAddress}:{LocalPort}",
            CommuniactionType.TCPServer => $"监听 {LocalIPAddress}:{LocalPort}",
            CommuniactionType.UDP => $"远端 {RemoteIPAddress}:{RemotePort}  本地 {LocalIPAddress}:{LocalPort}",
            CommuniactionType.COM => $"{PortName}  {BaudRate}bps  Parity {Parity}  Data {DataBits}  Stop {StopBits}",
            _ => "未配置"
        };

        public DeviceCommunicationProfile Clone(string localName)
        {
            return new DeviceCommunicationProfile
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
                StopBits = StopBits
            };
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
                default:
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
                    if (!TryValidateIpAddress(RemoteIPAddress, "远程 IP 地址", out validationMessage) ||
                        !TryValidatePort(RemotePort, "远程端口", true, out int remotePort, out validationMessage) ||
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
                    validationMessage = $"{TypeDisplayName} 配置有效，可生成运行时通信对象。";
                    return true;

                case CommuniactionType.TCPServer:
                    if (!TryValidateIpAddress(LocalIPAddress, "本地监听 IP 地址", out validationMessage) ||
                        !TryValidatePort(LocalPort, "本地监听端口", true, out int serverPort, out validationMessage))
                    {
                        return false;
                    }

                    config = new CommuniactionConfigModel(false, LocalName.Trim(), LocalIPAddress.Trim(), (ushort)serverPort);
                    validationMessage = "TCP Server 配置有效，可生成运行时通信对象。";
                    return true;

                case CommuniactionType.COM:
                    if (string.IsNullOrWhiteSpace(PortName))
                    {
                        validationMessage = "串口名称不能为空。";
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
                    validationMessage = "COM 配置有效，可生成运行时通信对象。";
                    return true;

                default:
                    validationMessage = "当前通信方式暂不支持生成运行时配置。";
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
            OnPropertyChanged(nameof(TypeDisplayName));
            OnPropertyChanged(nameof(TypeDescription));
        }

        private void RaiseStateChanged()
        {
            OnPropertyChanged(nameof(Summary));
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private static bool TryValidateIpAddress(string value, string fieldName, out string validationMessage)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                validationMessage = $"{fieldName} 不能为空。";
                return false;
            }

            if (!IPAddress.TryParse(value.Trim(), out _))
            {
                validationMessage = $"{fieldName} 格式不正确。";
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
                validationMessage = $"{fieldName} 不能为空。";
                return false;
            }

            if (!int.TryParse(value.Trim(), out port) || port < 0 || port > ushort.MaxValue)
            {
                validationMessage = $"{fieldName} 需要是 0 到 65535 的数字。";
                return false;
            }

            if (isRequired && port == 0)
            {
                validationMessage = $"{fieldName} 需要大于 0。";
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
                validationMessage = $"{fieldName} 不能为空。";
                return false;
            }

            if (!int.TryParse(value.Trim(), out number) || number <= 0)
            {
                validationMessage = $"{fieldName} 需要是大于 0 的数字。";
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
                validationMessage = $"{fieldName} 不能为空。";
                return false;
            }

            if (!int.TryParse(value.Trim(), out number) || number < minValue || number > maxValue)
            {
                validationMessage = $"{fieldName} 需要在 {minValue} 到 {maxValue} 之间。";
                return false;
            }

            validationMessage = string.Empty;
            return true;
        }
    }
}
