using ControlLibrary;
using Microsoft.Win32;
using Module.Communication.Models;
using Shared.Abstractions;
using Shared.Abstractions.Enum;
using Shared.Infrastructure.Communication;
using Shared.Infrastructure.Extensions;
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

public sealed partial class DeviceCommunicationConfigViewModel
{
    #region 初始化

    /// <summary>
    /// 初始化页面下拉选项。
    /// </summary>
    private void InitializeSelectionOptions()
    {
        CommunicationTypes.Add(new CommunicationTypeOption(CommuniactionType.TCPClient, "TCP客户端", "主动连接远端设备。"));
        CommunicationTypes.Add(new CommunicationTypeOption(CommuniactionType.TCPServer, "TCP服务端", "启动本地监听并等待外部连接。"));
        CommunicationTypes.Add(new CommunicationTypeOption(CommuniactionType.UDP, "UDP", "无连接数据报通信。"));
        CommunicationTypes.Add(new CommunicationTypeOption(CommuniactionType.COM, "串口", "串口通信。"));
        CommunicationTypes.Add(new CommunicationTypeOption(CommuniactionType.PLC, "PLC", "PLC通信。"));

        PLCTypes.Add(new SelectionOption(PlcCommunicationTypeNames.Modbus, PlcCommunicationTypeNames.Modbus));
        PLCTypes.Add(new SelectionOption(PlcCommunicationTypeNames.MX, PlcCommunicationTypeNames.MX));

        RefreshPortNameOptions(updateSelectedProfile: false);

        foreach (string baudRate in new[] { "1200", "2400", "4800", "9600", "19200", "38400", "57600", "115200" })
        {
            BaudRateOptions.Add(new SelectionOption(baudRate, baudRate));
        }

        ParityOptions.Add(new SelectionOption("0", "0 - 无"));
        ParityOptions.Add(new SelectionOption("1", "1 - 奇校验"));
        ParityOptions.Add(new SelectionOption("2", "2 - 偶校验"));
        ParityOptions.Add(new SelectionOption("3", "3 - 标记"));
        ParityOptions.Add(new SelectionOption("4", "4 - 空格"));

        DataBitOptions.Add(new SelectionOption("5", "5"));
        DataBitOptions.Add(new SelectionOption("6", "6"));
        DataBitOptions.Add(new SelectionOption("7", "7"));
        DataBitOptions.Add(new SelectionOption("8", "8"));

        StopBitOptions.Add(new SelectionOption("0", "0 - 无"));
        StopBitOptions.Add(new SelectionOption("1", "1 - 1位"));
        StopBitOptions.Add(new SelectionOption("2", "2 - 2位"));
        StopBitOptions.Add(new SelectionOption("3", "3 - 1.5位"));

        foreach (DataType type in Enum.GetValues<DataType>())
        {
            PlcDataTypeOptions.Add(new SelectionOption(type.ToString(), GetPlcDataTypeDisplayName(type)));
        }
    }

    /// <summary>
    /// 获取 PLC 数据类型的显示文本。
    /// </summary>
    private static string GetPlcDataTypeDisplayName(DataType type)
    {
        return type switch
        {
            DataType.Binary => "二进制",
            DataType.Octal => "八进制",
            DataType.Decimal => "十进制",
            DataType.Hexadecimal => "十六进制",
            DataType.Acsaii => "ASCII",
            DataType.String => "字符串",
            _ => type.ToString()
        };
    }

    /// <summary>
    /// 初始化页面命令。
    /// </summary>
    private void InitializeCommands()
    {
        NewProfileCommand = new RelayCommand(_ => NewProfile());
        DuplicateProfileCommand = new RelayCommand(_ => DuplicateProfile(), _ => SelectedProfile is not null);
        DeleteProfileCommand = new RelayCommand(_ => DeleteProfile(), _ => SelectedProfile is not null);
        SaveProfilesCommand = new RelayCommand(_ => SaveProfiles());
        AddSupportedProtocolCommand = new RelayCommand(_ => AddSupportedProtocol(), _ => SelectedProfile is not null);
        AddAvailableProtocolCommand = new RelayCommand(
            parameter => AddAvailableProtocol(parameter as AvailableProtocolOption),
            parameter => SelectedProfile is not null && parameter is AvailableProtocolOption);
        DeleteSupportedProtocolCommand = new RelayCommand(
            parameter => DeleteSupportedProtocol(parameter as DeviceSupportedProtocol),
            parameter => SelectedProfile is not null && parameter is DeviceSupportedProtocol);
        LoadSupportedProtocolFileCommand = new RelayCommand(
            parameter => LoadSupportedProtocolFile(parameter as DeviceSupportedProtocol),
            parameter => SelectedProfile is not null && parameter is DeviceSupportedProtocol);
        OpenProtocolCommandLibraryCommand = new RelayCommand(
            _ => OpenProtocolCommandLibrary(),
            _ => SelectedProfile is not null && SupportedProtocolCommands.Count > 0);
        FillSupportedProtocolCommandCommand = new RelayCommand(
            parameter => FillSupportedProtocolCommand(parameter as SupportedProtocolCommandOption),
            parameter => parameter is SupportedProtocolCommandOption);
        CloseProtocolLibraryCommand = new RelayCommand(_ => CloseProtocolLibrary(), _ => IsProtocolLibraryOpen);
        CloseProtocolCommandLibraryCommand = new RelayCommand(_ => CloseProtocolCommandLibrary(), _ => IsProtocolCommandLibraryOpen);
        TestConnectionCommand = new RelayCommand(_ => TestConnection(), _ => SelectedProfile is not null);
        SendCommand = new RelayCommand(async _ => await SendAsync());
        SendAllCommand = new RelayCommand(async _ => await SendAllAsync());
        ReadPlcCommand = new RelayCommand(async _ => await ReadPlcAsync());
        WritePlcCommand = new RelayCommand(async _ => await WritePlcAsync());
        CloseConnectionCommand = new RelayCommand(_ => CloseConnection(), _ => _activeCommunication is not null);
        ClearReceiveCommand = new RelayCommand(_ => ClearReceive());
        RefreshPortsCommand = new RelayCommand(_ => RefreshPorts());
    }

    #endregion

    #region 页面生命周期

    /// <summary>
    /// 页面卸载时释放事件绑定和通信资源。
    /// </summary>
    public void OnViewUnloaded()
    {
        if (_selectedProfile is not null)
        {
            _selectedProfile.PropertyChanged -= SelectedProfile_PropertyChanged;
        }

        CloseActiveCommunication(updateStatus: false);
    }

    #endregion

    #region 配置管理

    /// <summary>
    /// 新建通信配置。
    /// </summary>
    private void NewProfile()
    {
        CommuniactionType type = SelectedProfile?.Type ?? CommuniactionType.TCPClient;
        DeviceCommunicationProfile profile = CreateProfile(type, GenerateUniqueName(type));
        AddProfile(profile);
        SelectedProfile = profile;
        AppendReceiveLine($"已创建设备通信配置：{profile.LocalName}。");
    }

    private void DuplicateProfile()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        DeviceCommunicationProfile profile = SelectedProfile.Clone(GenerateUniqueName(SelectedProfile.Type));
        AddProfile(profile);
        SelectedProfile = profile;
        AppendReceiveLine($"已复制设备通信配置：{profile.LocalName}。");
    }

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
            AddProfile(CreateProfile(CommuniactionType.TCPClient, GenerateUniqueName(CommuniactionType.TCPClient)));
        }

        SelectedProfile = Profiles[Math.Clamp(currentIndex, 0, Profiles.Count - 1)];
        AppendReceiveLine($"已删除设备通信配置：{deletedProfile.LocalName}。");
    }

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

    #region 协议管理

    /// <summary>
    /// 打开协议列表抽屉。
    /// </summary>
    private void AddSupportedProtocol()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        OpenProtocolLibrary();
        AppendReceiveLine("已打开协议列表。");
    }

    private void AddAvailableProtocol(AvailableProtocolOption? option)
    {
        TryApplyAvailableProtocol(option, null);
    }

    public bool TryApplyAvailableProtocol(AvailableProtocolOption? option, DeviceSupportedProtocol? targetProtocol)
    {
        if (SelectedProfile is null || option is null)
        {
            return false;
        }

        DeviceSupportedProtocol target = ResolveSupportedProtocolTarget(SelectedProfile, option.Name, option.FilePath, targetProtocol);
        target.ProtocolName = option.Name;
        target.ProtocolFilePath = option.FilePath;
        RefreshSupportedProtocolCommands();
        AppendReceiveLine($"已关联协议：{option.Name}。");
        return true;
    }

    private void DeleteSupportedProtocol(DeviceSupportedProtocol? protocol)
    {
        if (SelectedProfile is null || protocol is null)
        {
            return;
        }

        SelectedProfile.SupportedProtocols.Remove(protocol);
        RefreshSupportedProtocolCommands();
        AppendReceiveLine(string.IsNullOrWhiteSpace(protocol.ProtocolName)
            ? "已删除空协议行。"
            : $"已删除支持协议：{protocol.ProtocolName}。");
    }

    private void LoadSupportedProtocolFile(DeviceSupportedProtocol? targetProtocol)
    {
        if (SelectedProfile is null || targetProtocol is null)
        {
            return;
        }

        OpenFileDialog dialog = new()
        {
            Title = "选择协议文件",
            Filter = "协议文件 (*.json)|*.json|所有文件 (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (!TryReadProtocolProfileFromFile(dialog.FileName, out ProtocolConfigProfile? protocolProfile, out string message) ||
            protocolProfile is null)
        {
            MessageBox.Show(message, "加载协议失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            AppendReceiveLine($"加载协议文件失败：{message}");
            return;
        }

        if (!TrySaveProtocolProfileToLocalDirectory(protocolProfile, out string savedPath, out message))
        {
            if (!string.IsNullOrWhiteSpace(message))
            {
                AppendReceiveLine(message);
            }

            return;
        }

        DeviceSupportedProtocol target = ResolveSupportedProtocolTarget(SelectedProfile, protocolProfile.Name, savedPath, targetProtocol);
        target.ProtocolName = protocolProfile.Name;
        target.ProtocolFilePath = savedPath;
        RefreshAvailableProtocols();
        RefreshSupportedProtocolCommands();
        AppendReceiveLine($"已加载协议文件：{protocolProfile.Name} -> {savedPath}");
    }

    private void OpenProtocolLibrary()
    {
        CloseProtocolCommandLibrary();
        RefreshAvailableProtocols();
        IsProtocolLibraryOpen = true;
    }

    private void CloseProtocolLibrary()
    {
        IsProtocolLibraryOpen = false;
    }

    #endregion

    #region 指令填充

    /// <summary>
    /// 打开当前支持协议的指令列表抽屉。
    /// </summary>
    private void OpenProtocolCommandLibrary()
    {
        RefreshSupportedProtocolCommands();
        if (SupportedProtocolCommands.Count == 0)
        {
            AppendReceiveLine("未找到可填充的协议指令，请先为当前配置关联协议。");
            return;
        }

        CloseProtocolLibrary();
        IsProtocolCommandLibraryOpen = true;
    }

    /// <summary>
    /// 关闭指令列表抽屉。
    /// </summary>
    private void CloseProtocolCommandLibrary()
    {
        IsProtocolCommandLibraryOpen = false;
    }

    /// <summary>
    /// 将选中的协议指令填充到报文文本框。
    /// </summary>
    private bool FillSupportedProtocolCommand(SupportedProtocolCommandOption? option)
    {
        if (option is null)
        {
            return false;
        }

        if (!option.CanFill || string.IsNullOrWhiteSpace(option.FillMessage))
        {
            AppendReceiveLine($"指令 {option.DisplayName} 当前没有可发送报文。");
            return false;
        }

        SendText = option.FillMessage;
        CloseProtocolCommandLibrary();
        AppendReceiveLine($"已将指令 {option.DisplayName} 填充到报文文本框。");
        return true;
    }

    /// <summary>
    /// 刷新当前支持协议对应的全部指令列表。
    /// </summary>
    private void RefreshSupportedProtocolCommands()
    {
        List<SupportedProtocolCommandOption> commands = LoadSupportedProtocolCommands();

        SupportedProtocolCommands.Clear();
        foreach (SupportedProtocolCommandOption command in commands)
        {
            SupportedProtocolCommands.Add(command);
        }

        if (IsProtocolCommandLibraryOpen && SupportedProtocolCommands.Count == 0)
        {
            IsProtocolCommandLibraryOpen = false;
        }

        RaiseCommandStatesChanged();
    }

    /// <summary>
    /// 从当前支持协议中聚合可展示的全部指令。
    /// </summary>
    private List<SupportedProtocolCommandOption> LoadSupportedProtocolCommands()
    {
        List<SupportedProtocolCommandOption> commands = new();
        if (SelectedProfile is null)
        {
            return commands;
        }

        IEnumerable<DeviceSupportedProtocol> supportedProtocols = SelectedProfile.SupportedProtocols
            .Where(protocol =>
                !string.IsNullOrWhiteSpace(protocol.ProtocolName) &&
                !string.IsNullOrWhiteSpace(protocol.ProtocolFilePath))
            .GroupBy(
                protocol => $"{protocol.ProtocolName}|{protocol.ProtocolFilePath}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First());

        foreach (DeviceSupportedProtocol supportedProtocol in supportedProtocols)
        {
            if (!TryReadProtocolProfileFromFile(
                    supportedProtocol.ProtocolFilePath,
                    out ProtocolConfigProfile? profile,
                    out string message) ||
                profile is null)
            {
                commands.Add(new SupportedProtocolCommandOption(
                    supportedProtocol.ProtocolName,
                    supportedProtocol.ProtocolFilePath,
                    "协议读取失败",
                    message,
                    string.Empty,
                    string.Empty,
                    false));
                continue;
            }

            foreach (ProtocolCommandConfig command in profile.Commands.Where(item => !item.IsParseOnly))
            {
                commands.Add(BuildSupportedProtocolCommandOption(
                    profile.Name,
                    supportedProtocol.ProtocolFilePath,
                    command));
            }
        }

        return commands
            .OrderBy(command => command.ProtocolName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(command => command.CommandName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// 构建指令列表项，并预生成可直接发送的报文。
    /// </summary>
    private static SupportedProtocolCommandOption BuildSupportedProtocolCommandOption(
        string protocolName,
        string protocolFilePath,
        ProtocolCommandConfig command)
    {
        bool canFill = false;
        string fillMessage = string.Empty;
        string previewMessage;
        string buildMessage = string.Empty;

        if (!command.IsParseOnly &&
            ProtocolPreviewEngine.TryBuildRequestPreview(command, out ProtocolRequestPreviewResult? preview, out buildMessage) &&
            preview is not null)
        {
            fillMessage = command.RequestFormat == ProtocolPayloadFormat.Hex
                ? $"0x{preview.RequestHex}"
                : preview.RequestAscii;
            previewMessage = fillMessage;
            canFill = !string.IsNullOrWhiteSpace(fillMessage);
        }
        else if (command.IsParseOnly)
        {
            previewMessage = "该指令为仅解析模式，没有发送报文。";
        }
        else
        {
            previewMessage = buildMessage;
        }

        return new SupportedProtocolCommandOption(
            protocolName,
            protocolFilePath,
            command.Name,
            command.Summary,
            previewMessage,
            fillMessage,
            canFill);
    }

    #endregion

    #region 通信测试

    private void TestConnection()
    {
        if (SelectedProfile is null)
        {
            SetConnectionStatus("未选择配置", WarningBrush);
            AppendReceiveLine("通信测试失败：请先选择通信配置。");
            return;
        }

        if (!SelectedProfile.TryBuildRuntimeConfig(out CommuniactionConfigModel? config, out string message) || config is null)
        {
            SetConnectionStatus("配置无效", WarningBrush);
            AppendReceiveLine($"通信测试失败：{message}");
            return;
        }

        CloseActiveCommunication(updateStatus: false);

        try
        {
            ICommunication communication = CommunicationFactory.CreateCommuniactionProtocol(config);
            _activeCommunication = communication;
            _activeProfileName = config.LocalName;
            _activeCommunicationType = config.Type;

            RaiseCommunicationVisibilityChanged();
            RefreshConnectedServerClients(Array.Empty<CommunicationClientInfo>());
            AttachActiveCommunicationEvents(communication);

            SetConnectionStatus("连接中", NeutralBrush);
            AppendReceiveLine($"开始测试连接：{config.LocalName}（{config.Type}）。");

            bool started = communication.Start();
            SetConnectionStatus(
                started ? $"{config.LocalName} 已启动" : $"{config.LocalName} 启动失败",
                started ? SuccessBrush : WarningBrush);
            AppendReceiveLine(started
                ? "通信测试已启动。"
                : "通信测试启动失败，请检查配置或设备状态。");
        }
        catch (Exception ex)
        {
            CloseActiveCommunication(updateStatus: false);
            SetConnectionStatus("连接异常", WarningBrush);
            AppendReceiveLine($"通信测试异常：{ex.Message}");
        }

        RaiseCommandStatesChanged();
    }

    private async Task SendAsync()
    {
        try
        {
            ICommunication? communication = _activeCommunication;
            CommuniactionType? activeType = _activeCommunicationType;
            if (communication is null || activeType is null)
            {
                AppendReceiveLine("发送失败：请先执行通信测试。");
                return;
            }

            string message = SendText;
            if (string.IsNullOrWhiteSpace(message))
            {
                AppendReceiveLine("发送失败：报文不能为空。");
                return;
            }

            if (activeType == CommuniactionType.TCPServer)
            {
                if (SelectedServerClient is null)
                {
                    AppendReceiveLine("发送失败：请先选择已连接的 TCP 客户端。");
                    return;
                }

                await SendToServerClientAsync(communication, message, SelectedServerClient);
                return;
            }

            if (IsPlcCommunicationType(activeType))
            {
                AppendReceiveLine("发送失败：PLC 通信请使用 PLC 读写测试。");
                return;
            }

            ReadWriteModel readWriteModel = new(message);
            bool result = await communication.WriteAsync(readWriteModel);
            string resultText = readWriteModel.Result is null ? string.Empty : $"，响应：{FormatMessage(readWriteModel.Result)}";
            AppendReceiveLine($"已发送：{message}，结果：{(result ? "成功" : "失败")}{resultText}");
        }
        catch (Exception ex)
        {
            AppendReceiveLine($"发送异常：{ex.Message}");
        }
    }

    private async Task SendAllAsync()
    {
        try
        {
            ICommunication? communication = _activeCommunication;
            if (communication is null || _activeCommunicationType != CommuniactionType.TCPServer)
            {
                AppendReceiveLine("群发失败：请先启动 TCP 服务端测试连接。");
                return;
            }

            string message = SendText;
            if (string.IsNullOrWhiteSpace(message))
            {
                AppendReceiveLine("群发失败：报文不能为空。");
                return;
            }

            List<ConnectedClientOption> clients = ConnectedServerClients.ToList();
            if (clients.Count == 0)
            {
                AppendReceiveLine("群发失败：当前没有已连接客户端。");
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

            AppendReceiveLine($"群发完成：{successCount}/{clients.Count} 个客户端发送成功。");
        }
        catch (Exception ex)
        {
            AppendReceiveLine($"群发异常：{ex.Message}");
        }
    }

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

            AppendReceiveLine($"PLC 读取 {address}，长度 {length}，结果：{(result ? "成功" : "失败")}，响应：{FormatMessage(readWriteModel.Result)}");
        }
        catch (Exception ex)
        {
            AppendReceiveLine($"PLC 读取异常：{ex.Message}");
        }
    }

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
            AppendReceiveLine($"PLC 写入 {address}，值 {value}，结果：{(result ? "成功" : "失败")}，响应：{FormatMessage(readWriteModel.Result)}");
        }
        catch (Exception ex)
        {
            AppendReceiveLine($"PLC 写入异常：{ex.Message}");
        }
    }

    private void CloseConnection()
    {
        CloseActiveCommunication(updateStatus: true);
    }

    private void ClearReceive()
    {
        ReceiveText = string.Empty;
    }

    private void RefreshPorts()
    {
        RefreshPortNameOptions(updateSelectedProfile: true);
        AppendReceiveLine(PortNameOptions.Count == 0
            ? "未检测到串口，可手动输入端口名称。"
            : $"串口已刷新：{string.Join(", ", PortNameOptions)}。");
    }

    #endregion

    #region 配置与文件读写

    private void SeedProfiles()
    {
        AddProfile(CreateProfile(CommuniactionType.TCPClient, GenerateUniqueName(CommuniactionType.TCPClient)));
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
        ProfilesView?.Refresh();
    }

    private void RefreshAvailableProtocols()
    {
        List<AvailableProtocolOption> options = LoadAvailableProtocolsFromDisk();

        AvailableProtocols.Clear();
        foreach (AvailableProtocolOption option in options)
        {
            AvailableProtocols.Add(option);
        }
    }

    private List<AvailableProtocolOption> LoadAvailableProtocolsFromDisk()
    {
        List<AvailableProtocolOption> options = new();
        if (!Directory.Exists(ProtocolConfigDirectory))
        {
            return options;
        }

        foreach (string filePath in Directory.EnumerateFiles(ProtocolConfigDirectory, "*.json").OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            if (!TryReadProtocolProfileFromFile(filePath, out ProtocolConfigProfile? profile, out _) || profile is null)
            {
                continue;
            }

            options.Add(new AvailableProtocolOption(profile.Name, filePath, profile.Summary));
        }

        return options;
    }

    private static bool TryReadProtocolProfileFromFile(string filePath, out ProtocolConfigProfile? profile, out string message)
    {
        profile = null;
        message = string.Empty;

        if (!File.Exists(filePath))
        {
            message = $"未找到协议文件：{filePath}";
            return false;
        }

        try
        {
            string storageText = File.ReadAllText(filePath, Encoding.UTF8);
            if (TryDeserializeProtocolProfile(storageText, out ProtocolConfigProfileDocument? document) && document is not null)
            {
                profile = document.ToProfile();
                return true;
            }

            message = $"文件 {Path.GetFileName(filePath)} 不是有效的协议配置。";
            return false;
        }
        catch (Exception ex)
        {
            message = $"读取协议文件失败：{ex.Message}";
            return false;
        }
    }

    private static bool TryDeserializeProtocolProfile(string storageText, out ProtocolConfigProfileDocument? document)
    {
        document = null;
        if (string.IsNullOrWhiteSpace(storageText))
        {
            return false;
        }

        try
        {
            document = JsonHelper.DeserializeObject<ProtocolConfigProfileDocument>(storageText.DesDecrypt());
            if (document is not null)
            {
                return true;
            }
        }
        catch
        {
        }

        try
        {
            document = JsonHelper.DeserializeObject<ProtocolConfigProfileDocument>(storageText);
            return document is not null;
        }
        catch
        {
            return false;
        }
    }

    private bool TrySaveProtocolProfileToLocalDirectory(ProtocolConfigProfile profile, out string savedPath, out string message)
    {
        savedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            message = "协议名称不能为空。";
            return false;
        }

        try
        {
            Directory.CreateDirectory(ProtocolConfigDirectory);

            string fileName = BuildProtocolStorageFileName(profile.Name);
            savedPath = Path.Combine(ProtocolConfigDirectory, fileName);

            if (File.Exists(savedPath))
            {
                MessageBoxResult result = MessageBox.Show(
                    $"协议“{profile.Name}”已存在，是否覆盖？",
                    "协议文件已存在",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes)
                {
                    message = $"已取消覆盖协议“{profile.Name}”。";
                    savedPath = string.Empty;
                    return false;
                }
            }

            string storageText = JsonHelper.SerializeObject(ProtocolConfigProfileDocument.FromProfile(profile)).Encrypt();
            File.WriteAllText(savedPath, storageText, Encoding.UTF8);
            message = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            message = $"保存协议文件失败：{ex.Message}";
            savedPath = string.Empty;
            return false;
        }
    }

    private static string BuildProtocolStorageFileName(string protocolName)
    {
        string safeName = BuildSafeFileName(protocolName);
        if (string.Equals(safeName, "Communication", StringComparison.Ordinal))
        {
            safeName = "Protocol";
        }

        return $"{safeName}.json";
    }

    private DeviceSupportedProtocol ResolveSupportedProtocolTarget(
        DeviceCommunicationProfile profile,
        string protocolName,
        string protocolFilePath,
        DeviceSupportedProtocol? preferredTarget)
    {
        if (preferredTarget is not null && !profile.SupportedProtocols.Contains(preferredTarget))
        {
            preferredTarget = null;
        }

        DeviceSupportedProtocol? duplicate = profile.SupportedProtocols.FirstOrDefault(item =>
            !ReferenceEquals(item, preferredTarget) &&
            (string.Equals(item.ProtocolName, protocolName, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(item.ProtocolFilePath, protocolFilePath, StringComparison.OrdinalIgnoreCase)));

        if (duplicate is not null)
        {
            if (preferredTarget is not null && preferredTarget.IsEmpty)
            {
                profile.SupportedProtocols.Remove(preferredTarget);
            }

            return duplicate;
        }

        if (preferredTarget is not null)
        {
            return preferredTarget;
        }

        DeviceSupportedProtocol? placeholder = profile.SupportedProtocols.FirstOrDefault(item => item.IsEmpty);
        if (placeholder is not null)
        {
            return placeholder;
        }

        DeviceSupportedProtocol created = new();
        profile.SupportedProtocols.Add(created);
        return created;
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
                AppendReceiveLine($"读取通信配置失败：{Path.GetFileName(filePath)}。原因：{ex.Message}");
            }
        }

        return loadedCount;
    }

    private int SaveProfilesToDisk()
    {
        Directory.CreateDirectory(CommunicationConfigDirectory);

        HashSet<string> usedFileNames = new(StringComparer.OrdinalIgnoreCase);
        int savedCount = 0;
        foreach (DeviceCommunicationProfile profile in Profiles)
        {
            if (string.IsNullOrWhiteSpace(profile.LocalName))
            {
                throw new InvalidOperationException("保存前必须填写配置名称。");
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
            CommuniactionType.TCPClient => "TCP客户端",
            CommuniactionType.TCPServer => "TCP服务端",
            CommuniactionType.UDP => "UDP",
            CommuniactionType.COM => "串口",
            CommuniactionType.PLC => "PLC",
            _ => "通信配置"
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

    #region 通信对象事件

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
            AppendReceiveLine($"关闭连接时发生异常：{ex.Message}");
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
            SetConnectionStatus("未连接", NeutralBrush);
            AppendReceiveLine("已关闭当前测试连接。");
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

        AppendReceiveLine($"收到{endpointText}：{FormatMessage(message)}");
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
            string resultText = readWriteModel.Result is null ? string.Empty : $"，响应：{FormatMessage(readWriteModel.Result)}";
            AppendReceiveLine($"发送到 {client.DisplayName}：{message}，结果：{(result ? "成功" : "失败")}{resultText}");
            return result;
        }
        catch (Exception ex)
        {
            AppendReceiveLine($"发送到 {client.DisplayName} 失败：{ex.Message}");
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
        if (communication is not null && IsPlcCommunicationType(_activeCommunicationType))
        {
            return true;
        }

        AppendReceiveLine("PLC 测试失败：请先启动 PLC 测试连接。");
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
            AppendReceiveLine("PLC 测试失败：长度必须大于 0。");
            return false;
        }

        if (!Enum.TryParse(SelectedPlcDataType, out dataType))
        {
            dataType = DataType.Decimal;
        }

        return true;
    }

    #endregion

    #region 视图与辅助方法

    private bool FilterProfiles(object item)
    {
        if (item is not DeviceCommunicationProfile profile)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        string keyword = SearchText.Trim();
        return Contains(profile.LocalName, keyword) ||
               Contains(profile.TypeDisplayName, keyword) ||
               Contains(profile.Summary, keyword) ||
               profile.SupportedProtocols.Any(protocol =>
                   Contains(protocol.ProtocolName, keyword) ||
                   Contains(protocol.ProtocolFilePath, keyword));
    }

    private static bool Contains(string? source, string keyword)
    {
        return source?.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
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
        RaiseCommandState(AddSupportedProtocolCommand);
        RaiseCommandState(AddAvailableProtocolCommand);
        RaiseCommandState(DeleteSupportedProtocolCommand);
        RaiseCommandState(LoadSupportedProtocolFileCommand);
        RaiseCommandState(OpenProtocolCommandLibraryCommand);
        RaiseCommandState(FillSupportedProtocolCommandCommand);
        RaiseCommandState(CloseProtocolLibraryCommand);
        RaiseCommandState(CloseProtocolCommandLibraryCommand);
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
