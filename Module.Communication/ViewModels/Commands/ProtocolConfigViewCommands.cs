using ControlLibrary;
using Module.Communication.Models;
using Shared.Infrastructure.Extensions;
using Shared.Infrastructure.PackMethod;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Input;
using System.Windows.Media;

namespace Module.Communication.ViewModels;

/// <summary>
/// 协议配置界面的命令实现和业务方法，供 XAML Command 绑定调用。
/// </summary>
public sealed partial class ProtocolConfigViewModel
{
    #region 初始化与生命周期方法
    /// <summary>
    /// 初始化协议页面的下拉选项。
    /// </summary>
    private void InitializeOptionCollections()
    {
        PayloadFormats.Add(new ProtocolOption<ProtocolPayloadFormat>(ProtocolPayloadFormat.Hex, "Hex", "按十六进制字节内容构建报文。"));
        PayloadFormats.Add(new ProtocolOption<ProtocolPayloadFormat>(ProtocolPayloadFormat.Ascii, "ASCII", "按 ASCII 文本内容构建报文。"));

        CrcModes.Add(new ProtocolOption<ProtocolCrcMode>(ProtocolCrcMode.None, "无校验", "不自动追加 CRC。"));
        CrcModes.Add(new ProtocolOption<ProtocolCrcMode>(ProtocolCrcMode.ModbusCrc16, "Modbus CRC16", "低字节在前，高字节在后。"));
        CrcModes.Add(new ProtocolOption<ProtocolCrcMode>(ProtocolCrcMode.Crc16Ibm, "CRC16-IBM", "IBM 反射模式，低字节在前。"));
        CrcModes.Add(new ProtocolOption<ProtocolCrcMode>(ProtocolCrcMode.Crc16CcittFalse, "CRC16-CCITT-FALSE", "高字节在前，常用于工业协议。"));
        CrcModes.Add(new ProtocolOption<ProtocolCrcMode>(ProtocolCrcMode.Crc32, "CRC32", "四字节 CRC32，小端追加。"));
    }

    /// <summary>
    /// 初始化页面全部按钮命令，避免在 View.xaml.cs 中保留业务点击逻辑。
    /// </summary>
    private void InitializeCommands()
    {
        NewProfileCommand = new RelayCommand(_ => NewProfile());
        DuplicateProfileCommand = new RelayCommand(_ => DuplicateProfile(), _ => SelectedProfile is not null);
        DeleteProfileCommand = new RelayCommand(_ => DeleteProfile(), _ => SelectedProfile is not null);
        SaveProfilesCommand = new RelayCommand(_ => SaveProfiles());
        NewCommandCommand = new RelayCommand(_ => NewCommand(), _ => SelectedProfile is not null);
        DuplicateCommandCommand = new RelayCommand(_ => DuplicateCommand(), _ => SelectedProfile?.SelectedCommand is not null);
        DeleteCommandCommand = new RelayCommand(_ => DeleteCommand(), _ => SelectedProfile?.SelectedCommand is not null);
        GenerateCommandCommand = new RelayCommand(_ => GenerateCommand(), _ => SelectedProfile?.SelectedCommand is not null);
        ParseResultCommand = new RelayCommand(_ => ParseResult(), _ => SelectedProfile?.SelectedCommand is not null);
        CloseCommandDrawerCommand = new RelayCommand(_ => CloseCommandDrawer(), _ => IsCommandDrawerOpen);
    }

    /// <summary>
    /// 视图卸载时取消对当前选中协议的事件订阅。
    /// </summary>
    public void OnViewUnloaded()
    {
        if (_selectedProfile is not null)
        {
            _selectedProfile.PropertyChanged -= SelectedProfile_PropertyChanged;
        }
    }

    /// <summary>
    /// 打开当前选中指令的抽屉编辑区。
    /// </summary>
    public void OpenCommandDrawer()
    {
        if (SelectedProfile?.SelectedCommand is null)
        {
            return;
        }

        IsCommandDrawerOpen = true;
    }

    /// <summary>
    /// 关闭当前指令抽屉编辑区。
    /// </summary>
    public void CloseCommandDrawer()
    {
        IsCommandDrawerOpen = false;
    }

    #endregion

    #region 配置命令方法
    /// <summary>
    /// 新建一个通用协议模板配置。
    /// </summary>
    private void NewProfile()
    {
        ProtocolConfigProfile profile = CreateGenericProfile(GenerateUniqueName("协议"));
        AddProfile(profile);
        SelectedProfile = profile;
        SetPageStatus($"已新建协议配置：{profile.Name}。", SuccessBrush);
    }

    /// <summary>
    /// 复制当前协议配置及其全部指令。
    /// </summary>
    private void DuplicateProfile()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        ProtocolConfigProfile profile = SelectedProfile.Clone(GenerateCopyName(SelectedProfile.Name));
        AddProfile(profile);
        SelectedProfile = profile;
        SetPageStatus($"已复制协议配置：{profile.Name}。", SuccessBrush);
    }

    /// <summary>
    /// 删除当前协议配置，并同步删除本地存储文件。
    /// </summary>
    private void DeleteProfile()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        int currentIndex = Profiles.IndexOf(SelectedProfile);
        ProtocolConfigProfile deletedProfile = SelectedProfile;
        Profiles.Remove(deletedProfile);
        DeleteStoredProfileFile(deletedProfile);

        if (Profiles.Count == 0)
        {
            ProtocolConfigProfile profile = CreateGenericProfile(GenerateUniqueName("协议"));
            AddProfile(profile);
        }

        SelectedProfile = Profiles[Math.Clamp(currentIndex, 0, Profiles.Count - 1)];
        SetPageStatus($"已删除协议配置：{deletedProfile.Name}。", NeutralBrush);
    }

    /// <summary>
    /// 新建当前协议下的一条指令，并自动打开抽屉编辑。
    /// </summary>
    private void NewCommand()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        ProtocolCommandConfig command = new()
        {
            Name = GenerateUniqueCommandName(SelectedProfile, "指令")
        };
        SelectedProfile.AddCommand(command);
        SelectedProfile.SelectedCommand = command;
        ClearGeneratedOutputs();
        OpenCommandDrawer();
        SetPageStatus($"已新建指令：{command.Name}。", SuccessBrush);
    }

    /// <summary>
    /// 复制当前选中的指令，并自动打开抽屉编辑。
    /// </summary>
    private void DuplicateCommand()
    {
        ProtocolConfigProfile? profile = SelectedProfile;
        ProtocolCommandConfig? selectedCommand = profile?.SelectedCommand;
        if (profile is null || selectedCommand is null)
        {
            return;
        }

        ProtocolCommandConfig command = selectedCommand.Clone(GenerateUniqueCommandName(profile, $"{selectedCommand.Name} 副本"));
        profile.AddCommand(command);
        profile.SelectedCommand = command;
        ClearGeneratedOutputs();
        OpenCommandDrawer();
        SetPageStatus($"已复制指令：{command.Name}。", SuccessBrush);
    }

    /// <summary>
    /// 删除当前选中的指令，保证每个协议至少保留一条指令。
    /// </summary>
    private void DeleteCommand()
    {
        ProtocolConfigProfile? profile = SelectedProfile;
        ProtocolCommandConfig? selectedCommand = profile?.SelectedCommand;
        if (profile is null || selectedCommand is null)
        {
            return;
        }

        ClearGeneratedOutputs();
        profile.RemoveCommand(selectedCommand);
        if (profile.Commands.Count == 0)
        {
            ProtocolCommandConfig command = new()
            {
                Name = GenerateUniqueCommandName(profile, "指令")
            };
            profile.AddCommand(command);
        }

        SetPageStatus($"已删除指令：{selectedCommand.Name}。", NeutralBrush);
    }

    /// <summary>
    /// 根据当前模板、占位符和 CRC 规则生成最终发送指令。
    /// </summary>
    private void GenerateCommand()
    {
        ProtocolCommandConfig? selectedCommand = SelectedProfile?.SelectedCommand;
        if (selectedCommand is null)
        {
            GeneratedCommandText = string.Empty;
            SetPageStatus("请先选择设备指令后再生成实际指令。", WarningBrush);
            return;
        }

        if (ProtocolPreviewEngine.TryBuildRequestPreview(selectedCommand, out ProtocolRequestPreviewResult? previewResult, out string message) &&
            previewResult is not null)
        {
            GeneratedCommandText = BuildGeneratedCommandText(selectedCommand, previewResult);
            SetPageStatus(message, SuccessBrush);
            return;
        }

        GeneratedCommandText = string.Empty;
        SetPageStatus(message, WarningBrush);
    }

    /// <summary>
    /// 根据当前返回示例和解析规则执行结果解析预览。
    /// </summary>
    private void ParseResult()
    {
        ProtocolCommandConfig? selectedCommand = SelectedProfile?.SelectedCommand;
        if (selectedCommand is null)
        {
            ParsedResultText = string.Empty;
            SetPageStatus("请先选择设备指令后再解析返回数据。", WarningBrush);
            return;
        }

        if (!selectedCommand.WaitForResponse)
        {
            ParsedResultText = string.Empty;
            SetPageStatus("当前指令未启用等待数据返回。", WarningBrush);
            return;
        }

        if (ProtocolPreviewEngine.TryBuildResponsePreview(selectedCommand, out ProtocolResponsePreviewResult? previewResult, out string message) &&
            previewResult is not null)
        {
            ParsedResultText = previewResult.ParsedJson;
            SetPageStatus(message, SuccessBrush);
            return;
        }

        ParsedResultText = string.Empty;
        SetPageStatus(message, WarningBrush);
    }

    /// <summary>
    /// 保存当前全部协议配置到本地目录。
    /// </summary>
    private void SaveProfiles()
    {
        try
        {
            int savedCount = SaveProfilesToDisk();
            SetPageStatus($"已保存 {savedCount} 个协议配置到 {ProtocolConfigDirectory}。", SuccessBrush);
        }
        catch (Exception ex)
        {
            SetPageStatus($"保存协议配置失败：{ex.Message}", WarningBrush);
        }
    }

    #endregion

    #region 预览与状态辅助方法
    private void ClearGeneratedOutputs()
    {
        GeneratedCommandText = string.Empty;
        ParsedResultText = string.Empty;
    }

    private static string BuildGeneratedCommandText(ProtocolCommandConfig command, ProtocolRequestPreviewResult previewResult)
    {
        return command.RequestFormat == ProtocolPayloadFormat.Hex
            ? previewResult.RequestHex
            : previewResult.RequestAscii;
    }

    /// <summary>
    /// 根据当前选中协议刷新发送预览和返回解析预览。
    /// </summary>
    private void UpdatePreviews()
    {
        ProtocolConfigProfile? profile = SelectedProfile;
        if (profile is null)
        {
            RequestPreviewText = "请先选择或创建一个协议配置。";
            ResponsePreviewText = "请先选择或创建一个协议配置。";
            SetRequestPreviewStatus("未选择配置", NeutralBrush);
            SetResponsePreviewStatus("未选择配置", NeutralBrush);
            return;
        }

        if (ProtocolPreviewEngine.TryBuildRequestPreview(profile, out ProtocolRequestPreviewResult? requestResult, out string requestMessage) &&
            requestResult is not null)
        {
            RequestPreviewText = BuildRequestPreviewText(requestResult);
            SetRequestPreviewStatus(requestMessage, SuccessBrush);
        }
        else
        {
            RequestPreviewText = $"当前配置无法生成发送帧预览。{Environment.NewLine}{Environment.NewLine}{requestMessage}";
            SetRequestPreviewStatus(requestMessage, WarningBrush);
        }

        if (ProtocolPreviewEngine.TryBuildResponsePreview(profile, out ProtocolResponsePreviewResult? responseResult, out string responseMessage) &&
            responseResult is not null)
        {
            ResponsePreviewText = BuildResponsePreviewText(responseResult);
            Brush responseBrush = string.IsNullOrWhiteSpace(profile.SampleResponseText) ||
                                 string.IsNullOrWhiteSpace(profile.ParseRulesText)
                ? NeutralBrush
                : SuccessBrush;
            SetResponsePreviewStatus(responseMessage, responseBrush);
        }
        else
        {
            ResponsePreviewText = $"当前配置无法生成返回解析预览。{Environment.NewLine}{Environment.NewLine}{responseMessage}";
            SetResponsePreviewStatus(responseMessage, WarningBrush);
        }
    }

    private static string BuildRequestPreviewText(ProtocolRequestPreviewResult previewResult)
    {
        return string.Join(
            Environment.NewLine + Environment.NewLine,
            "渲染模板",
            previewResult.RenderedTemplate,
            "发送 Hex",
            previewResult.RequestHex,
            "发送 ASCII",
            previewResult.RequestAscii);
    }

    private static string BuildResponsePreviewText(ProtocolResponsePreviewResult previewResult)
    {
        return string.Join(
            Environment.NewLine + Environment.NewLine,
            "返回 Hex",
            previewResult.ResponseHex,
            "返回 ASCII",
            previewResult.ResponseAscii,
            "解析结果(JSON)",
            previewResult.ParsedJson);
    }

    private bool FilterProfiles(object item)
    {
        if (item is not ProtocolConfigProfile profile)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        string keyword = SearchText.Trim();
        return Contains(profile.Name, keyword) ||
               Contains(profile.Summary, keyword) ||
               profile.Commands.Any(command => Contains(command.Name, keyword));
    }

    private static bool Contains(string? source, string keyword)
    {
        return source?.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void SetPageStatus(string text, Brush brush)
    {
        PageStatusText = text;
        PageStatusBrush = brush;
    }

    private void SetRequestPreviewStatus(string text, Brush brush)
    {
        RequestPreviewStatusText = text;
        RequestPreviewStatusBrush = brush;
    }

    private void SetResponsePreviewStatus(string text, Brush brush)
    {
        ResponsePreviewStatusText = text;
        ResponsePreviewStatusBrush = brush;
    }

    #endregion

    #region 配置加载与保存方法
    /// <summary>
    /// 在首次使用时生成默认示例协议配置。
    /// </summary>
    private void SeedProfiles()
    {
        AddProfile(CreateModbusDemoProfile("Modbus 读寄存器"));
        AddProfile(CreateAsciiDemoProfile("ASCII 文本协议"));
    }

    private static ProtocolConfigProfile CreateGenericProfile(string name)
    {
        return new ProtocolConfigProfile
        {
            Name = name,
            RequestFormat = ProtocolPayloadFormat.Hex,
            ResponseFormat = ProtocolPayloadFormat.Hex,
            ReplyAggregationMilliseconds = "200",
            CrcMode = ProtocolCrcMode.None,
            ContentTemplate = "AA {{Address}} {{Command}}",
            PlaceholderValuesText = "Address=01\r\nCommand=03",
            SampleResponseText = "AA 01 03",
            ParseRulesText = "FullHex = hex\r\nLength = len"
        };
    }

    private static ProtocolConfigProfile CreateModbusDemoProfile(string name)
    {
        ProtocolConfigProfile profile = new()
        {
            Name = name,
            CommandName = "读保持寄存器",
            RequestFormat = ProtocolPayloadFormat.Hex,
            ResponseFormat = ProtocolPayloadFormat.Hex,
            ReplyAggregationMilliseconds = "200",
            CrcMode = ProtocolCrcMode.ModbusCrc16,
            ContentTemplate = "{{Station}} {{Function}} {{AddressHi}} {{AddressLo}} {{CountHi}} {{CountLo}}",
            PlaceholderValuesText = "Station=01\r\nFunction=03\r\nAddressHi=00\r\nAddressLo=00\r\nCountHi=00\r\nCountLo=02",
            SampleResponseText = "01 03 04 00 0A 00 14",
            ParseRulesText = "Station = u8(0)\r\nFunction = u8(1)\r\nByteCount = u8(2)\r\nDataHex = hex(3,-1)"
        };

        profile.AddCommand(new ProtocolCommandConfig
        {
            Name = "写单个寄存器",
            RequestFormat = ProtocolPayloadFormat.Hex,
            ResponseFormat = ProtocolPayloadFormat.Hex,
            ReplyAggregationMilliseconds = "200",
            CrcMode = ProtocolCrcMode.ModbusCrc16,
            ContentTemplate = "{{Station}} 06 {{AddressHi}} {{AddressLo}} {{ValueHi}} {{ValueLo}}",
            PlaceholderValuesText = "Station=01\r\nAddressHi=00\r\nAddressLo=01\r\nValueHi=00\r\nValueLo=0A",
            SampleResponseText = "01 06 00 01 00 0A",
            ParseRulesText = "Station = u8(0)\r\nFunction = u8(1)\r\nAddress = hex(2,2)\r\nValue = hex(4,2)"
        });

        profile.SelectedCommand = null;
        return profile;
    }

    private static ProtocolConfigProfile CreateAsciiDemoProfile(string name)
    {
        ProtocolConfigProfile profile = new()
        {
            Name = name,
            CommandName = "读取通道",
            RequestFormat = ProtocolPayloadFormat.Ascii,
            ResponseFormat = ProtocolPayloadFormat.Ascii,
            ReplyAggregationMilliseconds = "300",
            CrcMode = ProtocolCrcMode.None,
            ContentTemplate = "READ {{Channel}}",
            PlaceholderValuesText = "Channel=T1",
            SampleResponseText = "OK,T1,25.6",
            ParseRulesText = "FullText = text\r\nLength = len\r\nPrefix = ascii(0,2)"
        };

        profile.SelectedCommand = null;
        return profile;
    }

    private void AddProfile(ProtocolConfigProfile profile)
    {
        Profiles.Add(profile);
    }

    /// <summary>
    /// 从本地协议配置目录读取全部协议文件。
    /// </summary>
    private int LoadProfilesFromDisk()
    {
        if (!Directory.Exists(ProtocolConfigDirectory))
        {
            return 0;
        }

        int loadedCount = 0;
        foreach (string filePath in Directory.EnumerateFiles(ProtocolConfigDirectory, "*.json").OrderBy(Path.GetFileName))
        {
            try
            {
                string storageText = File.ReadAllText(filePath, Encoding.UTF8);
                ProtocolConfigProfileDocument? document =
                    JsonHelper.DeserializeObject<ProtocolConfigProfileDocument>(storageText.DesDecrypt());
                if (document is null)
                {
                    continue;
                }

                ProtocolConfigProfile profile = document.ToProfile();
                AddProfile(profile);
                _profileStorageFileNames[profile] = Path.GetFileName(filePath);
                loadedCount++;
            }
            catch (Exception ex)
            {
                SetPageStatus($"读取协议配置失败：{Path.GetFileName(filePath)}，原因：{ex.Message}", WarningBrush);
            }
        }

        return loadedCount;
    }

    /// <summary>
    /// 将当前协议集合保存为加密 JSON 配置文件。
    /// </summary>
    private int SaveProfilesToDisk()
    {
        Directory.CreateDirectory(ProtocolConfigDirectory);

        HashSet<string> usedFileNames = new(StringComparer.OrdinalIgnoreCase);
        int savedCount = 0;
        foreach (ProtocolConfigProfile profile in Profiles)
        {
            ValidateProfileForSave(profile);

            string fileName = BuildUniqueStorageFileName(profile.Name, usedFileNames);
            string filePath = Path.Combine(ProtocolConfigDirectory, fileName);
            string storageText = JsonHelper.SerializeObject(ProtocolConfigProfileDocument.FromProfile(profile)).Encrypt();
            File.WriteAllText(filePath, storageText, Encoding.UTF8);

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

    private void DeleteStoredProfileFile(ProtocolConfigProfile profile)
    {
        if (!_profileStorageFileNames.TryGetValue(profile, out string? fileName))
        {
            return;
        }

        TryDeleteStorageFile(fileName);
        _profileStorageFileNames.Remove(profile);
    }

    private static void ValidateProfileForSave(ProtocolConfigProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            throw new InvalidOperationException("协议名称不能为空。");
        }

        if (profile.Commands.Count == 0)
        {
            throw new InvalidOperationException($"协议 {profile.Name} 至少需要包含一条指令。");
        }

        foreach (ProtocolCommandConfig command in profile.Commands)
        {
            if (string.IsNullOrWhiteSpace(command.Name))
            {
                throw new InvalidOperationException($"协议 {profile.Name} 存在未命名指令。");
            }

            if (!int.TryParse(command.ReplyAggregationMilliseconds.Trim(), out int replyWait) || replyWait < 0)
            {
                throw new InvalidOperationException($"指令 {command.Name} 的强制等待拼接时长必须是大于等于 0 的整数毫秒。");
            }
        }
    }

    private static void TryDeleteStorageFile(string fileName)
    {
        try
        {
            string filePath = Path.Combine(ProtocolConfigDirectory, fileName);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch
        {
            // 删除旧文件失败时不影响界面继续使用，后续可以人工清理。
        }
    }

    private static string BuildUniqueStorageFileName(string profileName, HashSet<string> usedFileNames)
    {
        string safeName = BuildSafeFileName(profileName);
        string fileName = $"{safeName}.json";
        for (int index = 2; usedFileNames.Contains(fileName); index++)
        {
            fileName = $"{safeName}_{index}.json";
        }

        usedFileNames.Add(fileName);
        return fileName;
    }

    private static string BuildSafeFileName(string value)
    {
        HashSet<char> invalidChars = new(Path.GetInvalidFileNameChars());
        StringBuilder builder = new(value.Trim().Length);
        foreach (char current in value.Trim())
        {
            builder.Append(invalidChars.Contains(current) || char.IsControl(current)
                ? '_'
                : char.IsWhiteSpace(current) ? '_' : current);
        }

        string safeName = builder.ToString().Trim(' ', '.');
        if (string.IsNullOrWhiteSpace(safeName))
        {
            safeName = "Protocol";
        }

        return safeName.Length <= 80 ? safeName : safeName[..80];
    }

    private string GenerateUniqueName(string prefix)
    {
        for (int index = 1; ; index++)
        {
            string name = $"{prefix} {index}";
            if (!Profiles.Any(profile => string.Equals(profile.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                return name;
            }
        }
    }

    private static string GenerateUniqueCommandName(ProtocolConfigProfile profile, string prefix)
    {
        string baseName = string.IsNullOrWhiteSpace(prefix) ? "指令" : prefix.Trim();
        if (!profile.Commands.Any(command => string.Equals(command.Name, baseName, StringComparison.OrdinalIgnoreCase)))
        {
            return baseName;
        }

        for (int index = 2; ; index++)
        {
            string name = $"{baseName} {index}";
            if (!profile.Commands.Any(command => string.Equals(command.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                return name;
            }
        }
    }

    private string GenerateCopyName(string baseName)
    {
        string prefix = string.IsNullOrWhiteSpace(baseName) ? "协议" : baseName.Trim();
        string firstName = $"{prefix} 副本";
        if (!Profiles.Any(profile => string.Equals(profile.Name, firstName, StringComparison.OrdinalIgnoreCase)))
        {
            return firstName;
        }

        for (int index = 2; ; index++)
        {
            string name = $"{prefix} 副本 {index}";
            if (!Profiles.Any(profile => string.Equals(profile.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                return name;
            }
        }
    }

    #endregion

    #region 命令状态方法
    private void RaiseCommandStatesChanged()
    {
        RaiseCommandState(NewProfileCommand);
        RaiseCommandState(DuplicateProfileCommand);
        RaiseCommandState(DeleteProfileCommand);
        RaiseCommandState(SaveProfilesCommand);
        RaiseCommandState(NewCommandCommand);
        RaiseCommandState(DuplicateCommandCommand);
        RaiseCommandState(DeleteCommandCommand);
        RaiseCommandState(GenerateCommandCommand);
        RaiseCommandState(ParseResultCommand);
        RaiseCommandState(CloseCommandDrawerCommand);
    }

    private static void RaiseCommandState(ICommand? command)
    {
        if (command is RelayCommand relayCommand)
        {
            relayCommand.RaiseCanExecuteChanged();
        }
    }

    #endregion
}
