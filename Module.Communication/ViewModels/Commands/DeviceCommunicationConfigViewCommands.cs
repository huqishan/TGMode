using ControlLibrary;
using Module.Communication.Models;
using Shared.Abstractions;
using Shared.Abstractions.Enum;
using Shared.Infrastructure.Communication;
using Shared.Infrastructure.PackMethod;
using Shared.Models.Communication;
using Shared.Models.Log;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Module.Communication.ViewModels;

/// <summary>
/// 设备通信配置界面的命令实现和业务方法，供 XAML Command 绑定调用。
/// </summary>
public sealed partial class DeviceCommunicationConfigViewModel
{
    #region 初始化与生命周期方法
    /// <summary>
    /// 初始化通信方式、串口参数和 PLC 数据类型等下拉选项。
    /// </summary>
    private void InitializeSelectionOptions()
    {
        CommunicationTypes.Add(new CommunicationTypeOption(CommuniactionType.TCPClient, "TCPClient", "主动连接远端设备。"));
        CommunicationTypes.Add(new CommunicationTypeOption(CommuniactionType.TCPServer, "TCPServer", "本地开启端口监听。"));
        CommunicationTypes.Add(new CommunicationTypeOption(CommuniactionType.UDP, "UDP", "轻量无连接报文通信。"));
        CommunicationTypes.Add(new CommunicationTypeOption(CommuniactionType.COM, "COM", "串口通信。"));
        CommunicationTypes.Add(new CommunicationTypeOption(CommuniactionType.PLC, "PLC", "PLC 通信。"));

        PLCTypes.Add(new SelectionOption(PlcCommunicationTypeNames.Modbus, PlcCommunicationTypeNames.Modbus));
        PLCTypes.Add(new SelectionOption(PlcCommunicationTypeNames.MX, PlcCommunicationTypeNames.MX));

        RefreshPortNameOptions(false);

        foreach (string baudRate in new[] { "1200", "2400", "4800", "9600", "19200", "38400", "57600", "115200" })
        {
            BaudRateOptions.Add(new SelectionOption(baudRate, baudRate));
        }

        ParityOptions.Add(new SelectionOption("0", "0 - None"));
        ParityOptions.Add(new SelectionOption("1", "1 - Odd"));
        ParityOptions.Add(new SelectionOption("2", "2 - Even"));
        ParityOptions.Add(new SelectionOption("3", "3 - Mark"));
        ParityOptions.Add(new SelectionOption("4", "4 - Space"));

        DataBitOptions.Add(new SelectionOption("5", "5"));
        DataBitOptions.Add(new SelectionOption("6", "6"));
        DataBitOptions.Add(new SelectionOption("7", "7"));
        DataBitOptions.Add(new SelectionOption("8", "8"));

        StopBitOptions.Add(new SelectionOption("0", "0 - None"));
        StopBitOptions.Add(new SelectionOption("1", "1 - One"));
        StopBitOptions.Add(new SelectionOption("2", "2 - Two"));
        StopBitOptions.Add(new SelectionOption("3", "3 - OnePointFive"));

        foreach (DataType type in Enum.GetValues<DataType>())
        {
            PlcDataTypeOptions.Add(new SelectionOption(type.ToString(), type.ToString()));
        }
    }

    /// <summary>
    /// 初始化页面所有按钮命令，避免在 View.xaml.cs 中写业务点击逻辑。
    /// </summary>
    private void InitializeCommands()
    {
        NewProfileCommand = new RelayCommand(_ => NewProfile());
        DuplicateProfileCommand = new RelayCommand(_ => DuplicateProfile(), _ => SelectedProfile is not null);
        DeleteProfileCommand = new RelayCommand(_ => DeleteProfile(), _ => SelectedProfile is not null);
        SaveProfilesCommand = new RelayCommand(_ => SaveProfiles());
        TestConnectionCommand = new RelayCommand(_ => TestConnection(), _ => SelectedProfile is not null);
        SendCommand = new RelayCommand(async _ => await SendAsync());
        SendAllCommand = new RelayCommand(async _ => await SendAllAsync());
        ReadPlcCommand = new RelayCommand(async _ => await ReadPlcAsync());
        WritePlcCommand = new RelayCommand(async _ => await WritePlcAsync());
        CloseConnectionCommand = new RelayCommand(_ => CloseConnection(), _ => _activeCommunication is not null);
        ClearReceiveCommand = new RelayCommand(_ => ClearReceive());
        RefreshPortsCommand = new RelayCommand(_ => RefreshPorts());
    }

    /// <summary>
    /// 视图卸载时关闭临时通信连接，避免后台连接残留。
    /// </summary>
    public void OnViewUnloaded()
    {
        if (_selectedProfile is not null)
        {
            _selectedProfile.PropertyChanged -= SelectedProfile_PropertyChanged;
        }

        CloseActiveCommunication(false);
    }

    #endregion

    #region 配置命令方法
    /// <summary>
    /// 新建一个与当前通信类型一致的通信配置。
    /// </summary>
    private void NewProfile()
    {
        CommuniactionType type = SelectedProfile?.Type ?? CommuniactionType.TCPClient;
        DeviceCommunicationProfile profile = CreateProfile(type, GenerateUniqueName(type));
        AddProfile(profile);
        SelectedProfile = profile;
        AppendReceiveLine($"已新建通信配置：{profile.LocalName}。");
    }

    /// <summary>
    /// 复制当前选中的通信配置，并生成新的唯一名称。
    /// </summary>
    private void DuplicateProfile()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        DeviceCommunicationProfile profile = SelectedProfile.Clone(GenerateUniqueName(SelectedProfile.Type));
        AddProfile(profile);
        SelectedProfile = profile;
        AppendReceiveLine($"已复制通信配置：{profile.LocalName}。");
    }

    /// <summary>
    /// 删除当前选中的通信配置，并同步清理对应的本地文件。
    /// </summary>
    private void DeleteProfile()
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
        AppendReceiveLine($"已删除通信配置：{deletedProfile.LocalName}。");
    }

    /// <summary>
    /// 将当前所有通信配置保存到本地配置目录。
    /// </summary>
    private void SaveProfiles()
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

    #endregion

    #region 通信测试命令方法
    /// <summary>
    /// 按当前配置创建并启动通信对象，用于现场连通性测试。
    /// </summary>
    private void TestConnection()
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

            RaiseCommunicationVisibilityChanged();
            RefreshConnectedServerClients(Array.Empty<CommunicationClientInfo>());
            AttachActiveCommunicationEvents(communication);

            SetConnectionStatus("正在连接", NeutralBrush);
            AppendReceiveLine($"开始测试连接：{config.LocalName} ({config.Type})。");

            bool started = communication.Start();
            SetConnectionStatus(
                started ? $"{config.LocalName} 已启动" : $"{config.LocalName} 启动失败",
                started ? SuccessBrush : WarningBrush);
            AppendReceiveLine(started ? "测试连接已启动。" : "测试连接启动失败，请查看日志或确认设备参数。");
        }
        catch (Exception ex)
        {
            CloseActiveCommunication(false);
            SetConnectionStatus("连接异常", WarningBrush);
            AppendReceiveLine($"测试连接异常：{ex.Message}");
        }

        RaiseCommandStatesChanged();
    }

    /// <summary>
    /// 对当前激活的通信对象执行发送测试。
    /// </summary>
    private async Task SendAsync()
    {
        try
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

            if (IsPlcCommunicationType(activeType))
            {
                AppendReceiveLine("发送失败：PLC 通信请使用 PLC 测试区的读取或写入。");
                return;
            }

            ReadWriteModel readWriteModel = new(message);
            bool result = await communication.WriteAsync(readWriteModel);
            string resultText = readWriteModel.Result is null ? string.Empty : $"，反馈：{FormatMessage(readWriteModel.Result)}";
            AppendReceiveLine($"发送：{message}，结果：{(result ? "成功" : "失败")}{resultText}");
        }
        catch (Exception ex)
        {
            AppendReceiveLine($"发送异常：{ex.Message}");
        }
    }

    /// <summary>
    /// 在 TCP Server 模式下，向全部已连接客户端广播当前发送内容。
    /// </summary>
    private async Task SendAllAsync()
    {
        try
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
        catch (Exception ex)
        {
            AppendReceiveLine($"全部发送异常：{ex.Message}");
        }
    }

    /// <summary>
    /// 执行 PLC 读取测试，并把读取结果追加到日志区。
    /// </summary>
    private async Task ReadPlcAsync()
    {
        try
        {
            if (!TryGetActivePlcCommunication(out ICommunication? communication) ||
                !TryGetPlcTestArguments(out string address, out int length, out DataType dataType))
            {
                return;
            }

            ReadWriteModel readWriteModel = new(string.Empty, address, length, dataType);
            bool result = await Task.Run(() =>
            {
                ReadWriteModel model = readWriteModel;
                bool readResult = communication!.Read(ref model);
                readWriteModel = model;
                return readResult;
            });

            AppendReceiveLine($"PLC 读取 {address}，长度 {length}，结果：{(result ? "成功" : "失败")}，反馈：{FormatMessage(readWriteModel.Result)}");
        }
        catch (Exception ex)
        {
            AppendReceiveLine($"PLC 读取异常：{ex.Message}");
        }
    }

    /// <summary>
    /// 执行 PLC 写入测试，并把写入结果追加到日志区。
    /// </summary>
    private async Task WritePlcAsync()
    {
        try
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

            ReadWriteModel readWriteModel = new(value, address, length, dataType);
            bool result = await communication!.WriteAsync(readWriteModel);
            AppendReceiveLine($"PLC 写入 {address}，值：{value}，结果：{(result ? "成功" : "失败")}，反馈：{FormatMessage(readWriteModel.Result)}");
        }
        catch (Exception ex)
        {
            AppendReceiveLine($"PLC 写入异常：{ex.Message}");
        }
    }

    /// <summary>
    /// 主动关闭当前测试连接并恢复页面状态。
    /// </summary>
    private void CloseConnection()
    {
        CloseActiveCommunication(true);
    }

    /// <summary>
    /// 清空接收与日志文本框内容。
    /// </summary>
    private void ClearReceive()
    {
        ReceiveText = string.Empty;
    }

    /// <summary>
    /// 重新扫描本机串口，并尝试同步当前串口配置。
    /// </summary>
    private void RefreshPorts()
    {
        RefreshPortNameOptions(true);
        AppendReceiveLine(PortNameOptions.Count == 0
            ? "未检测到可用串口，可手动输入串口名称。"
            : $"已刷新串口列表：{string.Join(", ", PortNameOptions)}。");
    }

    #endregion

    #region 配置加载与保存方法
    /// <summary>
    /// 在首次使用时生成默认通信配置。
    /// </summary>
    private void SeedProfiles()
    {
        AddProfile(CreateProfile(CommuniactionType.TCPClient, "TCPClient 1"));
    }

    private DeviceCommunicationProfile CreateProfile(CommuniactionType type, string name)
    {
        DeviceCommunicationProfile profile = new()
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

    /// <summary>
    /// 从本地通信配置目录读取全部 JSON 配置。
    /// </summary>
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

    /// <summary>
    /// 将当前通信配置集合逐个保存为本地 JSON 文件。
    /// </summary>
    private int SaveProfilesToDisk()
    {
        Directory.CreateDirectory(CommunicationConfigDirectory);

        HashSet<string> usedFileNames = new(StringComparer.OrdinalIgnoreCase);
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
            // 删除旧配置文件失败时不阻断界面继续使用，后续可以手动清理。
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
        HashSet<char> invalidChars = new(Path.GetInvalidFileNameChars());
        StringBuilder builder = new(localName.Trim().Length);
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

        return safeName.Length <= 80 ? safeName : safeName[..80];
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

    #endregion

    #region 通信对象管理方法
    /// <summary>
    /// 订阅当前激活通信对象的接收、状态和日志事件。
    /// </summary>
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

    /// <summary>
    /// 关闭当前激活的通信对象，并同步清空相关页面状态。
    /// </summary>
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

            RaiseCommandStatesChanged();
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
            RaiseCommunicationVisibilityChanged();
        }

        if (updateStatus)
        {
            SetConnectionStatus("已断开", NeutralBrush);
            AppendReceiveLine("当前测试连接已断开。");
        }

        RaiseCommandStatesChanged();
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
        RaiseCommandStatesChanged();
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
            ReadWriteModel readWriteModel = new(message, client.ClientId);
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

    #endregion

    #region PLC 测试与日志辅助方法
    private bool TryGetActivePlcCommunication(out ICommunication? communication)
    {
        communication = _activeCommunication;
        if (communication is not null && IsPlcCommunicationType(_activeCommunicationType))
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
                ReceiveText = ReceiveText[^MaxReceiveTextLength..];
            }
        });
    }

    private void RunOnUiThread(Action action)
    {
        Dispatcher dispatcher = GetUiDispatcher();
        if (dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.BeginInvoke(action);
    }

    private static Dispatcher GetUiDispatcher()
    {
        return Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
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
            or CommuniactionType.MX
            or CommuniactionType.PLC;
    }

    private static bool IsPlcCommunicationType(CommuniactionType? type)
    {
        return type is CommuniactionType.PLC or CommuniactionType.MX;
    }

    private void RaiseCommandStatesChanged()
    {
        RaiseCommandState(NewProfileCommand);
        RaiseCommandState(DuplicateProfileCommand);
        RaiseCommandState(DeleteProfileCommand);
        RaiseCommandState(SaveProfilesCommand);
        RaiseCommandState(TestConnectionCommand);
        RaiseCommandState(CloseConnectionCommand);
    }

    private static void RaiseCommandState(ICommand? command)
    {
        if (command is RelayCommand relayCommand)
        {
            relayCommand.RaiseCanExecuteChanged();
        }
    }

    #endregion

    #region 串口辅助方法
    /// <summary>
    /// 刷新串口候选列表，并在需要时自动回填当前配置的串口名。
    /// </summary>
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
            int.TryParse(portName[3..], out int number))
        {
            return number;
        }

        return int.MaxValue;
    }

    #endregion
}
