using ControlLibrary.ControlViews.Communication.Models;
using Shared.Abstractions;
using Shared.Abstractions.Enum;
using Shared.Infrastructure.Communication;
using Shared.Infrastructure.Extensions;
using Shared.Infrastructure.PackMethod;
using Shared.Models.Communication;
using Shared.Models.Log;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ControlLibrary.ControlViews.Communication
{
    /// <summary>
    /// 设备通信配置页面，负责配置持久化和临时连接测试。
    /// </summary>
    public partial class DeviceCommunicationConfigView : UserControl, INotifyPropertyChanged
    {
        private const int MaxReceiveTextLength = 100_000;

        private static readonly string CommunicationConfigDirectory =
            Path.Combine(AppContext.BaseDirectory, "Config", "Communication");

        private static readonly JsonSerializerOptions StorageJsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            Converters = { new JsonStringEnumConverter() }
        };

        private static readonly Brush SuccessBrush =
            new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16A34A"));

        private static readonly Brush WarningBrush =
            new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EA580C"));

        private static readonly Brush NeutralBrush =
            new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));

        private DeviceCommunicationProfile? _selectedProfile;
        private readonly Dictionary<DeviceCommunicationProfile, string> _profileStorageFileNames = new Dictionary<DeviceCommunicationProfile, string>();
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

        public DeviceCommunicationConfigView()
        {
            InitializeComponent();

            CommunicationTypes = new ObservableCollection<CommunicationTypeOption>
            {
                new CommunicationTypeOption(CommuniactionType.TCPClient, "TCPClient", "主动连接远端设备。"),
                new CommunicationTypeOption(CommuniactionType.TCPServer, "TCPServer", "本地开启端口监听。"),
                new CommunicationTypeOption(CommuniactionType.UDP, "UDP", "轻量无连接报文通信。"),
                new CommunicationTypeOption(CommuniactionType.COM, "COM", "串口通信。"),
                new CommunicationTypeOption(CommuniactionType.PLC, "PLC", "PLC通信。")
            };

            PortNameOptions = new ObservableCollection<string>();
            RefreshPortNameOptions(false);
            BaudRateOptions = new ObservableCollection<SelectionOption>
            {
                new SelectionOption("1200", "1200"),
                new SelectionOption("2400", "2400"),
                new SelectionOption("4800", "4800"),
                new SelectionOption("9600", "9600"),
                new SelectionOption("19200", "19200"),
                new SelectionOption("38400", "38400"),
                new SelectionOption("57600", "57600"),
                new SelectionOption("115200", "115200")
            };
            ParityOptions = new ObservableCollection<SelectionOption>
            {
                new SelectionOption("0", "0 - None"),
                new SelectionOption("1", "1 - Odd"),
                new SelectionOption("2", "2 - Even"),
                new SelectionOption("3", "3 - Mark"),
                new SelectionOption("4", "4 - Space")
            };
            DataBitOptions = new ObservableCollection<SelectionOption>
            {
                new SelectionOption("5", "5"),
                new SelectionOption("6", "6"),
                new SelectionOption("7", "7"),
                new SelectionOption("8", "8")
            };
            StopBitOptions = new ObservableCollection<SelectionOption>
            {
                new SelectionOption("0", "0 - None"),
                new SelectionOption("1", "1 - One"),
                new SelectionOption("2", "2 - Two"),
                new SelectionOption("3", "3 - OnePointFive")
            };
            PlcDataTypeOptions = new ObservableCollection<SelectionOption>(
                Enum.GetValues<DataType>()
                    .Select(type => new SelectionOption(type.ToString(), type.ToString())));

            int loadedProfileCount = LoadProfilesFromDisk();
            if (loadedProfileCount == 0)
            {
                SeedProfiles();
            }

            SelectedProfile = Profiles.FirstOrDefault();
            DataContext = this;
            Unloaded += DeviceCommunicationConfigView_Unloaded;

            AppendReceiveLine(
                loadedProfileCount > 0
                    ? $"已从 {CommunicationConfigDirectory} 读取 {loadedProfileCount} 个通信配置。"
                    : $"未发现本地通信配置，已创建默认配置。保存后会写入 {CommunicationConfigDirectory}。");
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public ObservableCollection<DeviceCommunicationProfile> Profiles { get; } = new ObservableCollection<DeviceCommunicationProfile>();

        public ObservableCollection<CommunicationTypeOption> CommunicationTypes { get; }

        public ObservableCollection<string> PortNameOptions { get; }

        public ObservableCollection<SelectionOption> BaudRateOptions { get; }

        public ObservableCollection<SelectionOption> ParityOptions { get; }

        public ObservableCollection<SelectionOption> DataBitOptions { get; }

        public ObservableCollection<SelectionOption> StopBitOptions { get; }

        public ObservableCollection<SelectionOption> PlcDataTypeOptions { get; }

        public ObservableCollection<ConnectedClientOption> ConnectedServerClients { get; } = new ObservableCollection<ConnectedClientOption>();

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
                OnPropertyChanged(nameof(IsTcpServerClientSelectionVisible));
                OnPropertyChanged(nameof(IsPlcTestVisible));
                OnPropertyChanged(nameof(IsGenericSendTestVisible));
            }
        }

        public string ConnectionStatusText
        {
            get => _connectionStatusText;
            private set
            {
                if (_connectionStatusText == value)
                {
                    return;
                }

                _connectionStatusText = value;
                OnPropertyChanged();
            }
        }

        public Brush ConnectionStatusBrush
        {
            get => _connectionStatusBrush;
            private set
            {
                if (ReferenceEquals(_connectionStatusBrush, value))
                {
                    return;
                }

                _connectionStatusBrush = value;
                OnPropertyChanged();
            }
        }

        public string SendText
        {
            get => _sendText;
            set
            {
                if (_sendText == value)
                {
                    return;
                }

                _sendText = value;
                OnPropertyChanged();
            }
        }

        public string ReceiveText
        {
            get => _receiveText;
            private set
            {
                if (_receiveText == value)
                {
                    return;
                }

                _receiveText = value;
                OnPropertyChanged();
            }
        }

        public string PlcAddress
        {
            get => _plcAddress;
            set
            {
                if (_plcAddress == value)
                {
                    return;
                }

                _plcAddress = value;
                OnPropertyChanged();
            }
        }

        public string PlcLength
        {
            get => _plcLength;
            set
            {
                if (_plcLength == value)
                {
                    return;
                }

                _plcLength = value;
                OnPropertyChanged();
            }
        }

        public string PlcWriteValue
        {
            get => _plcWriteValue;
            set
            {
                if (_plcWriteValue == value)
                {
                    return;
                }

                _plcWriteValue = value;
                OnPropertyChanged();
            }
        }

        public string SelectedPlcDataType
        {
            get => _selectedPlcDataType;
            set
            {
                if (_selectedPlcDataType == value)
                {
                    return;
                }

                _selectedPlcDataType = value;
                OnPropertyChanged();
            }
        }

        public ConnectedClientOption? SelectedServerClient
        {
            get => _selectedServerClient;
            set
            {
                if (ReferenceEquals(_selectedServerClient, value))
                {
                    return;
                }

                _selectedServerClient = value;
                OnPropertyChanged();
            }
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

        private void NewProfileButton_Click(object sender, RoutedEventArgs e)
        {
            CommuniactionType type = SelectedProfile?.Type ?? CommuniactionType.TCPClient;
            DeviceCommunicationProfile profile = CreateProfile(type, GenerateUniqueName(type));
            AddProfile(profile);
            SelectedProfile = profile;
        }

        private void DuplicateProfileButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedProfile is null)
            {
                return;
            }

            DeviceCommunicationProfile profile = SelectedProfile.Clone(GenerateUniqueName(SelectedProfile.Type));
            AddProfile(profile);
            SelectedProfile = profile;
        }

        private void SaveProfilesButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                int savedCount = SaveProfilesToDisk();
                AppendReceiveLine($"已保存 {savedCount} 个通信配置到 {CommunicationConfigDirectory}。");
            }
            catch (Exception ex)
            {
                AppendReceiveLine($"保存通信配置失败：{ex.Message}");
            }
        }

        private void DeleteProfileButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedProfile is null)
            {
                return;
            }

            int currentIndex = Profiles.IndexOf(SelectedProfile);
            DeviceCommunicationProfile deletedProfile = SelectedProfile;
            Profiles.Remove(deletedProfile);
            DeleteStoredProfileFile(deletedProfile);

            if (Profiles.Count == 0)
            {
                DeviceCommunicationProfile profile = CreateProfile(CommuniactionType.TCPClient, GenerateUniqueName(CommuniactionType.TCPClient));
                AddProfile(profile);
            }

            SelectedProfile = Profiles[Math.Clamp(currentIndex, 0, Profiles.Count - 1)];
        }

        private void TestConnectionButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedProfile is null)
            {
                SetConnectionStatus("未选择配置", WarningBrush);
                AppendReceiveLine("测试连接失败：请先选择一个通信配置。");
                return;
            }

            if (!SelectedProfile.TryBuildRuntimeConfig(out CommuniactionConfigModel? config, out string message) || config is null)
            {
                SetConnectionStatus("配置无效", WarningBrush);
                AppendReceiveLine($"测试连接失败：{message}");
                return;
            }

            CloseActiveCommunication(false);

            try
            {
                ICommunication communication = CommunicationFactory.CreateCommuniactionProtocol(config);
                _activeCommunication = communication;
                _activeProfileName = config.LocalName;
                _activeCommunicationType = config.Type;
                OnPropertyChanged(nameof(IsTcpServerClientSelectionVisible));
                RefreshConnectedServerClients(Array.Empty<CommunicationClientInfo>());
                AttachActiveCommunicationEvents(communication);

                SetConnectionStatus("正在连接", NeutralBrush);
                AppendReceiveLine($"开始测试连接：{config.LocalName} ({config.Type})。");

                bool started = communication.Start();
                SetConnectionStatus(started ? $"{config.LocalName} 已启动" : $"{config.LocalName} 启动失败", started ? SuccessBrush : WarningBrush);
                AppendReceiveLine(started ? "测试连接已启动。" : "测试连接启动失败，请查看日志或确认设备参数。");
            }
            catch (Exception ex)
            {
                CloseActiveCommunication(false);
                SetConnectionStatus("连接异常", WarningBrush);
                AppendReceiveLine($"测试连接异常：{ex.Message}");
            }
        }

        private async void SendButton_Click(object sender, RoutedEventArgs e)
        {
            ICommunication? communication = _activeCommunication;
            CommuniactionType? activeType = _activeCommunicationType;
            if (communication is null || activeType is null)
            {
                AppendReceiveLine("发送失败：请先测试连接。");
                return;
            }

            string message = SendText;
            if (string.IsNullOrWhiteSpace(message))
            {
                AppendReceiveLine("发送失败：发送内容不能为空。");
                return;
            }

            if (activeType == CommuniactionType.TCPServer)
            {
                ConnectedClientOption? selectedClient = SelectedServerClient;
                if (selectedClient is null)
                {
                    AppendReceiveLine("发送失败：请先选择一个已连接客户端。");
                    return;
                }

                await SendToServerClientAsync(communication, message, selectedClient);
                return;
            }

            if (activeType == CommuniactionType.PLC)
            {
                AppendReceiveLine("发送失败：PLC 通信请使用 PLC 测试区的读取或写入。");
                return;
            }

            try
            {
                ReadWriteModel readWriteModel = new ReadWriteModel(message);

                bool result = await communication.WriteAsync(readWriteModel);
                string resultText = readWriteModel.Result is null ? string.Empty : $"，反馈：{FormatMessage(readWriteModel.Result)}";
                AppendReceiveLine($"发送：{message}，结果：{(result ? "成功" : "失败")}{resultText}");
            }
            catch (Exception ex)
            {
                AppendReceiveLine($"发送异常：{ex.Message}");
            }
        }

        private async void SendAllButton_Click(object sender, RoutedEventArgs e)
        {
            ICommunication? communication = _activeCommunication;
            if (communication is null || _activeCommunicationType != CommuniactionType.TCPServer)
            {
                AppendReceiveLine("全部发送失败：请先启动 TCP Server 测试连接。");
                return;
            }

            string message = SendText;
            if (string.IsNullOrWhiteSpace(message))
            {
                AppendReceiveLine("全部发送失败：发送内容不能为空。");
                return;
            }

            List<ConnectedClientOption> clients = ConnectedServerClients.ToList();
            if (clients.Count == 0)
            {
                AppendReceiveLine("全部发送失败：当前没有已连接客户端。");
                return;
            }

            int successCount = 0;
            foreach (ConnectedClientOption client in clients)
            {
                if (await SendToServerClientAsync(communication, message, client))
                {
                    successCount++;
                }
            }

            AppendReceiveLine($"全部发送完成：{successCount}/{clients.Count} 个客户端发送成功。");
        }

        private async void ReadPlcButton_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetActivePlcCommunication(out ICommunication? communication) ||
                !TryGetPlcTestArguments(out string address, out int length, out DataType dataType))
            {
                return;
            }

            ReadWriteModel readWriteModel = new ReadWriteModel(string.Empty, address, length, dataType);
            bool result = await Task.Run(() =>
            {
                ReadWriteModel model = readWriteModel;
                return communication!.Read(ref model);
            });

            AppendReceiveLine($"PLC 读取 {address}，长度 {length}，结果：{(result ? "成功" : "失败")}，反馈：{FormatMessage(readWriteModel.Result)}");
        }

        private async void WritePlcButton_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetActivePlcCommunication(out ICommunication? communication) ||
                !TryGetPlcTestArguments(out string address, out int length, out DataType dataType))
            {
                return;
            }

            string value = PlcWriteValue.Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                AppendReceiveLine("PLC 写入失败：写入值不能为空。");
                return;
            }

            ReadWriteModel readWriteModel = new ReadWriteModel(value, address, length, dataType);
            bool result = await communication!.WriteAsync(readWriteModel);
            AppendReceiveLine($"PLC 写入 {address}，值：{value}，结果：{(result ? "成功" : "失败")}，反馈：{FormatMessage(readWriteModel.Result)}");
        }

        private void CloseConnectionButton_Click(object sender, RoutedEventArgs e)
        {
            CloseActiveCommunication(true);
        }

        private void ClearReceiveButton_Click(object sender, RoutedEventArgs e)
        {
            ReceiveText = string.Empty;
        }

        private void RefreshPortsButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshPortNameOptions(true);
            AppendReceiveLine(PortNameOptions.Count == 0
                ? "未检测到可用串口，可手动输入串口名称。"
                : $"已刷新串口列表：{string.Join(", ", PortNameOptions)}。");
        }

        private void DeviceCommunicationConfigView_Unloaded(object sender, RoutedEventArgs e)
        {
            CloseActiveCommunication(false);
        }

        private void ResetProfileButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedProfile is null)
            {
                return;
            }

            // Reset only type specific fields and keep the profile name for easier reuse.
            SelectedProfile.ResetToCurrentTypeDefaults();
        }

        private void SeedProfiles()
        {
            AddProfile(CreateProfile(CommuniactionType.TCPClient, "TCPClient 1"));
        }

        private DeviceCommunicationProfile CreateProfile(CommuniactionType type, string name)
        {
            DeviceCommunicationProfile profile = new DeviceCommunicationProfile
            {
                LocalName = name,
                Type = type
            };
            profile.ResetToCurrentTypeDefaults();
            return profile;
        }

        private void AddProfile(DeviceCommunicationProfile profile)
        {
            Profiles.Add(profile);
        }

        private void SelectedProfile_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DeviceCommunicationProfile.Type))
            {
                RefreshPortNameOptions(SelectedProfile?.IsSerialType == true);
                OnPropertyChanged(nameof(IsTcpServerClientSelectionVisible));
                OnPropertyChanged(nameof(IsPlcTestVisible));
                OnPropertyChanged(nameof(IsGenericSendTestVisible));
            }
        }

        private void RefreshPortNameOptions(bool updateSelectedProfile)
        {
            List<string> detectedPortNames = GetDetectedSerialPortNames();
            DeviceCommunicationProfile? profile = SelectedProfile;
            if (profile?.IsSerialType == true)
            {
                string selectedPortName = profile.PortName.Trim();
                if (updateSelectedProfile &&
                    detectedPortNames.Count > 0 &&
                    (string.IsNullOrWhiteSpace(selectedPortName) ||
                     (string.Equals(selectedPortName, "COM1", StringComparison.OrdinalIgnoreCase) &&
                      !ContainsPortName(detectedPortNames, selectedPortName))))
                {
                    profile.PortName = detectedPortNames[0];
                }
            }

            PortNameOptions.Clear();
            foreach (string portName in detectedPortNames)
            {
                PortNameOptions.Add(portName);
            }
        }

        private static List<string> GetDetectedSerialPortNames()
        {
            try
            {
                return SerialPort.GetPortNames()
                    .Where(portName => !string.IsNullOrWhiteSpace(portName))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(GetSerialPortSortNumber)
                    .ThenBy(portName => portName, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch
            {
                return new List<string>();
            }
        }

        private static bool ContainsPortName(IEnumerable<string> portNames, string portName)
        {
            return portNames.Any(value => string.Equals(value, portName, StringComparison.OrdinalIgnoreCase));
        }

        private static int GetSerialPortSortNumber(string portName)
        {
            if (portName.StartsWith("COM", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(portName.Substring(3), out int number))
            {
                return number;
            }

            return int.MaxValue;
        }

        private int LoadProfilesFromDisk()
        {
            if (!Directory.Exists(CommunicationConfigDirectory))
            {
                return 0;
            }

            int loadedCount = 0;
            foreach (string filePath in Directory.EnumerateFiles(CommunicationConfigDirectory, "*.json").OrderBy(Path.GetFileName))
            {
                try
                {
                    DeviceCommunicationProfileDocument? document = JsonHelper.ReadJson<DeviceCommunicationProfileDocument>(filePath);
                    //string json = File.ReadAllText(filePath, Encoding.UTF8);
                    //DeviceCommunicationProfileDocument? document =
                    //    JsonSerializer.Deserialize<DeviceCommunicationProfileDocument>(json, StorageJsonOptions);
                    if (document is null || !IsSupportedCommunicationType(document.Type))
                    {
                        continue;
                    }

                    DeviceCommunicationProfile profile = document.ToProfile();
                    AddProfile(profile);
                    _profileStorageFileNames[profile] = Path.GetFileName(filePath);
                    loadedCount++;
                }
                catch (Exception ex)
                {
                    AppendReceiveLine($"读取通信配置失败：{Path.GetFileName(filePath)}，原因：{ex.Message}");
                }
            }

            return loadedCount;
        }

        private int SaveProfilesToDisk()
        {
            Directory.CreateDirectory(CommunicationConfigDirectory);

            HashSet<string> usedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int savedCount = 0;
            foreach (DeviceCommunicationProfile profile in Profiles)
            {
                if (string.IsNullOrWhiteSpace(profile.LocalName))
                {
                    throw new InvalidOperationException("配置名称不能为空，保存前请先填写名称。");
                }

                string fileName = BuildUniqueStorageFileName(profile.LocalName, usedFileNames);
                string filePath = Path.Combine(CommunicationConfigDirectory, fileName);
                JsonHelper.SaveJson(DeviceCommunicationProfileDocument.FromProfile(profile), filePath);
                //string json = JsonHelper.SaveJson(DeviceCommunicationProfileDocument.FromProfile(profile), filePath);
                //File.WriteAllText(filePath, json, Encoding.UTF8);

                if (_profileStorageFileNames.TryGetValue(profile, out string? oldFileName) &&
                    !string.Equals(oldFileName, fileName, StringComparison.OrdinalIgnoreCase))
                {
                    TryDeleteStorageFile(oldFileName);
                }

                _profileStorageFileNames[profile] = fileName;
                savedCount++;
            }

            return savedCount;
        }

        private void DeleteStoredProfileFile(DeviceCommunicationProfile profile)
        {
            if (!_profileStorageFileNames.TryGetValue(profile, out string? fileName))
            {
                return;
            }

            TryDeleteStorageFile(fileName);
            _profileStorageFileNames.Remove(profile);
        }

        private static void TryDeleteStorageFile(string fileName)
        {
            try
            {
                string filePath = Path.Combine(CommunicationConfigDirectory, fileName);
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }
            catch
            {
                // 删除旧配置失败不能阻断界面操作，文件后续可以手动清理。
            }
        }

        private static string BuildUniqueStorageFileName(string localName, HashSet<string> usedFileNames)
        {
            string safeName = BuildSafeFileName(localName);
            string fileName = $"{safeName}.json";
            for (int index = 2; usedFileNames.Contains(fileName); index++)
            {
                fileName = $"{safeName}_{index}.json";
            }

            usedFileNames.Add(fileName);
            return fileName;
        }

        private static string BuildSafeFileName(string localName)
        {
            HashSet<char> invalidChars = new HashSet<char>(Path.GetInvalidFileNameChars());
            StringBuilder builder = new StringBuilder(localName.Trim().Length);
            foreach (char value in localName.Trim())
            {
                builder.Append(invalidChars.Contains(value) || char.IsControl(value)
                    ? '_'
                    : char.IsWhiteSpace(value) ? '_' : value);
            }

            string safeName = builder.ToString().Trim(' ', '.');
            if (string.IsNullOrWhiteSpace(safeName))
            {
                safeName = "Communication";
            }

            return safeName.Length <= 80 ? safeName : safeName.Substring(0, 80);
        }

        private string GenerateUniqueName(CommuniactionType type)
        {
            string prefix = type switch
            {
                CommuniactionType.TCPClient => "TCPClient",
                CommuniactionType.TCPServer => "TCPServer",
                CommuniactionType.UDP => "UDP",
                CommuniactionType.COM => "COM",
                CommuniactionType.PLC => "PLC",
                _ => "Communication"
            };

            for (int index = 1; ; index++)
            {
                string name = $"{prefix} {index}";
                if (!Profiles.Any(profile => string.Equals(profile.LocalName, name, StringComparison.OrdinalIgnoreCase)))
                {
                    return name;
                }
            }
        }

        private void AttachActiveCommunicationEvents(ICommunication communication)
        {
            communication.OnReceive += ActiveCommunication_OnReceive;
            communication.StateChange += ActiveCommunication_StateChange;
            communication.OnLog += ActiveCommunication_OnLog;

            if (communication is ICommunicationClientSource clientSource)
            {
                _activeClientSource = clientSource;
                clientSource.ClientsChanged += ActiveCommunication_ClientsChanged;
                RefreshConnectedServerClients(clientSource.GetConnectedClients());
            }
        }

        private void DetachActiveCommunicationEvents(ICommunication communication)
        {
            communication.OnReceive -= ActiveCommunication_OnReceive;
            communication.StateChange -= ActiveCommunication_StateChange;
            communication.OnLog -= ActiveCommunication_OnLog;

            if (_activeClientSource is not null)
            {
                _activeClientSource.ClientsChanged -= ActiveCommunication_ClientsChanged;
                _activeClientSource = null;
            }
        }

        private void CloseActiveCommunication(bool updateStatus)
        {
            ICommunication? communication = _activeCommunication;
            string? profileName = _activeProfileName;
            if (communication is null)
            {
                if (updateStatus)
                {
                    SetConnectionStatus("未连接", NeutralBrush);
                }

                return;
            }

            try
            {
                DetachActiveCommunicationEvents(communication);
                if (!string.IsNullOrWhiteSpace(profileName))
                {
                    CommunicationFactory.Remove(profileName);
                }
                else
                {
                    communication.Close();
                }
            }
            catch (Exception ex)
            {
                AppendReceiveLine($"关闭连接异常：{ex.Message}");
            }
            finally
            {
                _activeCommunication = null;
                _activeProfileName = null;
                _activeCommunicationType = null;
                RefreshConnectedServerClients(Array.Empty<CommunicationClientInfo>());
                OnPropertyChanged(nameof(IsTcpServerClientSelectionVisible));
                OnPropertyChanged(nameof(IsPlcTestVisible));
                OnPropertyChanged(nameof(IsGenericSendTestVisible));
            }

            if (updateStatus)
            {
                SetConnectionStatus("已断开", NeutralBrush);
                AppendReceiveLine("当前测试连接已断开。");
            }
        }

        private string ActiveCommunication_OnReceive(object message, params object[] param)
        {
            string endpointText = FormatEndpoint(param);
            if (_activeCommunicationType == CommuniactionType.TCPServer && param.Length > 0)
            {
                SelectServerClient(param[0]?.ToString());
            }

            AppendReceiveLine($"接收{endpointText}：{FormatMessage(message)}");
            return string.Empty;
        }

        private void ActiveCommunication_StateChange(ConnectState connectState, string localName)
        {
            string stateText = connectState == ConnectState.Connected ? "已连接" : "已断开";
            Brush stateBrush = connectState == ConnectState.Connected ? SuccessBrush : WarningBrush;
            SetConnectionStatus($"{localName} {stateText}", stateBrush);
            AppendReceiveLine($"状态变化：{localName} {stateText}。");
        }

        private void ActiveCommunication_OnLog(LogMessageModel log)
        {
            AppendReceiveLine($"日志 {log.Type}：{log.Message}");
        }

        private void ActiveCommunication_ClientsChanged(IReadOnlyList<CommunicationClientInfo> clients)
        {
            RefreshConnectedServerClients(clients);
        }

        private async Task<bool> SendToServerClientAsync(ICommunication communication, string message, ConnectedClientOption client)
        {
            try
            {
                ReadWriteModel readWriteModel = new ReadWriteModel(message, client.ClientId);
                bool result = await communication.WriteAsync(readWriteModel);
                string resultText = readWriteModel.Result is null ? string.Empty : $"，反馈：{FormatMessage(readWriteModel.Result)}";
                AppendReceiveLine($"发送 -> {client.DisplayName}：{message}，结果：{(result ? "成功" : "失败")}{resultText}");
                return result;
            }
            catch (Exception ex)
            {
                AppendReceiveLine($"发送到 {client.DisplayName} 异常：{ex.Message}");
                return false;
            }
        }

        private void RefreshConnectedServerClients(IReadOnlyList<CommunicationClientInfo> clients)
        {
            RunOnUiThread(() =>
            {
                string? selectedClientId = SelectedServerClient?.ClientId;
                List<ConnectedClientOption> options = clients
                    .Select(client => new ConnectedClientOption(client.ClientId, client.DisplayName, client.Address, client.Port))
                    .ToList();

                ConnectedServerClients.Clear();
                foreach (ConnectedClientOption option in options)
                {
                    ConnectedServerClients.Add(option);
                }

                SelectedServerClient = options.FirstOrDefault(option =>
                                         string.Equals(option.ClientId, selectedClientId, StringComparison.OrdinalIgnoreCase))
                                     ?? options.FirstOrDefault();

                OnPropertyChanged(nameof(ConnectedServerClientStatusText));
            });
        }

        private void SelectServerClient(string? clientId)
        {
            if (string.IsNullOrWhiteSpace(clientId))
            {
                return;
            }

            RunOnUiThread(() =>
            {
                ConnectedClientOption? matchedClient = ConnectedServerClients.FirstOrDefault(option =>
                    string.Equals(option.ClientId, clientId, StringComparison.OrdinalIgnoreCase));

                if (matchedClient is not null)
                {
                    SelectedServerClient = matchedClient;
                }
            });
        }

        private bool TryGetActivePlcCommunication(out ICommunication? communication)
        {
            communication = _activeCommunication;
            if (communication is not null && _activeCommunicationType == CommuniactionType.PLC)
            {
                return true;
            }

            AppendReceiveLine("PLC 测试失败：请先测试连接 PLC。");
            return false;
        }

        private bool TryGetPlcTestArguments(out string address, out int length, out DataType dataType)
        {
            address = PlcAddress.Trim();
            length = 0;
            dataType = DataType.Decimal;

            if (string.IsNullOrWhiteSpace(address))
            {
                AppendReceiveLine("PLC 测试失败：PLC 地址不能为空。");
                return false;
            }

            if (!int.TryParse(PlcLength.Trim(), out length) || length <= 0)
            {
                AppendReceiveLine("PLC 测试失败：读取长度需要是大于 0 的数字。");
                return false;
            }

            if (!Enum.TryParse(SelectedPlcDataType, out dataType))
            {
                dataType = DataType.Decimal;
            }

            return true;
        }

        private void SetConnectionStatus(string text, Brush brush)
        {
            RunOnUiThread(() =>
            {
                ConnectionStatusText = text;
                ConnectionStatusBrush = brush;
            });
        }

        private void AppendReceiveLine(string message)
        {
            RunOnUiThread(() =>
            {
                string line = $"[{DateTime.Now:HH:mm:ss}] {message}";
                ReceiveText = string.IsNullOrEmpty(ReceiveText)
                    ? line
                    : $"{ReceiveText}{Environment.NewLine}{line}";

                if (ReceiveText.Length > MaxReceiveTextLength)
                {
                    ReceiveText = ReceiveText.Substring(ReceiveText.Length - MaxReceiveTextLength);
                }
            });
        }

        private void RunOnUiThread(Action action)
        {
            if (Dispatcher.CheckAccess())
            {
                action();
                return;
            }

            Dispatcher.BeginInvoke(action);
        }

        private static string FormatEndpoint(object[] param)
        {
            if (param.Length >= 3)
            {
                return $" [{param[1]}:{param[2]}]";
            }

            return param.Length > 0 ? $" [{FormatMessage(param[0])}]" : string.Empty;
        }

        private static string FormatMessage(object? message)
        {
            if (message is null)
            {
                return string.Empty;
            }

            if (message is byte[] bytes)
            {
                string text = Encoding.UTF8.GetString(bytes);
                string hex = BitConverter.ToString(bytes);
                return string.IsNullOrWhiteSpace(text) ? hex : $"{text} ({hex})";
            }

            return message.ToString() ?? string.Empty;
        }

        private static bool IsSupportedCommunicationType(CommuniactionType type)
        {
            return type is CommuniactionType.TCPClient
                or CommuniactionType.TCPServer
                or CommuniactionType.UDP
                or CommuniactionType.COM
                or CommuniactionType.PLC;
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
