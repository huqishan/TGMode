using Module.Communication.Models;
using Shared.Abstractions;
using Shared.Abstractions.Enum;
using Shared.Infrastructure.Communication;
using Shared.Infrastructure.Extensions;
using Shared.Infrastructure.PackMethod;
using Shared.Models.Communication;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Module.Communication.Services
{
    /// <summary>
    /// 设备运行服务，负责设备初始化连接、直接收发、PLC 读写以及按协议指令收发。
    /// </summary>
    public static class CommunicationExecutionService
    {
        #region 配置路径与运行状态字段

        private static readonly string CommunicationConfigDirectory =
            Path.Combine(AppContext.BaseDirectory, "Config", "Communication");

        private static readonly string ProtocolConfigDirectory =
            Path.Combine(AppContext.BaseDirectory, "Config", "Protocol");

        // 以设备名称保存运行时通信对象，便于外部按设备名称收发。
        private static readonly ConcurrentDictionary<string, DeviceRuntimeContext> ActiveDevices =
            new(StringComparer.OrdinalIgnoreCase);

        #endregion

        #region 设备连接状态事件

        /// <summary>
        /// 设备连接状态改变事件；任意设备 Start、Close 或断线重连状态变化时触发。
        /// </summary>
        public static event EventHandler<DeviceConnectionStateChangedEventArgs>? DeviceConnectionStateChanged;

        #endregion

        #region 初始化与设备管理入口

        /// <summary>
        /// 读取全部设备通信配置并逐个创建连接。
        /// </summary>
        public static DeviceInitializationResult Initialize()
        {
            List<DeviceExecutionActionResult> results = new();
            foreach (DeviceCommunicationProfile profile in LoadDeviceProfiles())
            {
                results.Add(InitializeDevice(profile));
            }

            return new DeviceInitializationResult(results);
        }
        /// <summary>
        /// 获取当前已加载到运行时的设备快照。
        /// </summary>
        public static IReadOnlyList<DeviceExecutionSnapshot> GetActiveDevices()
        {
            return ActiveDevices.Values
                .Select(context => context.CreateSnapshot())
                .OrderBy(item => item.DeviceName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// 关闭指定设备连接，并从运行时设备集合移除。
        /// </summary>
        public static DeviceExecutionActionResult Close(string deviceName)
        {
            string normalizedDeviceName = NormalizeRequiredText(deviceName);
            if (string.IsNullOrWhiteSpace(normalizedDeviceName))
            {
                return DeviceExecutionActionResult.CreateFailure("Device name is required.");
            }

            if (!ActiveDevices.TryRemove(normalizedDeviceName, out DeviceRuntimeContext? context))
            {
                return DeviceExecutionActionResult.CreateFailure($"Device '{normalizedDeviceName}' is not initialized.");
            }

            context.Dispose();
            CommunicationFactory.Remove(normalizedDeviceName);
            return DeviceExecutionActionResult.CreateSuccess($"Device '{normalizedDeviceName}' closed.");
        }

        /// <summary>
        /// 关闭全部运行时设备连接。
        /// </summary>
        public static void CloseAll()
        {
            foreach (string deviceName in ActiveDevices.Keys.ToList())
            {
                Close(deviceName);
            }
        }

        #endregion

        #region 直接发送与接收入口

        /// <summary>
        /// 根据设备名称直接发送报文；TCP/UDP/串口可传文本或 0x 开头 Hex。
        /// </summary>
        public static async Task<DeviceExecutionActionResult> SendAsync(
            string deviceName,
            string message,
            bool waitForResponse = false,
            int waitTime = 10000,
            object? clientId = null)
        {
            if (!TryGetDevice(deviceName, out DeviceRuntimeContext? context, out DeviceExecutionActionResult failure))
            {
                return failure;
            }

            if (string.IsNullOrWhiteSpace(message))
            {
                return DeviceExecutionActionResult.CreateFailure("Send message is required.", context.DeviceName);
            }

            try
            {
                ReadWriteModel readWriteModel = clientId is null
                    ? new ReadWriteModel(message, waitTime)
                    : new ReadWriteModel(message, clientId);
                readWriteModel.WaitTime = Math.Max(1, waitTime);

                bool isSuccess = await Task.Run(() =>
                {
                    ReadWriteModel model = readWriteModel;
                    bool result = context.Communication.Write(ref model, waitForResponse);
                    readWriteModel = model;
                    return result;
                }).ConfigureAwait(false);

                return DeviceExecutionActionResult.Create(
                    isSuccess,
                    isSuccess
                        ? $"Device '{context.DeviceName}' send succeeded."
                        : $"Device '{context.DeviceName}' send failed.",
                    context.DeviceName,
                    readWriteModel.Result);
            }
            catch (Exception ex)
            {
                return DeviceExecutionActionResult.CreateFailure(
                    $"Device '{context.DeviceName}' send exception: {ex.Message}",
                    context.DeviceName);
            }
        }

        /// <summary>
        /// 根据设备名称直接接收一条返回数据。
        /// </summary>
        public static async Task<DeviceExecutionActionResult> ReceiveAsync(string deviceName, int waitTime = 10000)
        {
            if (!TryGetDevice(deviceName, out DeviceRuntimeContext? context, out DeviceExecutionActionResult failure))
            {
                return failure;
            }

            try
            {
                ReadWriteModel readWriteModel = new(string.Empty, Math.Max(1, waitTime));
                bool isSuccess = await Task.Run(() =>
                {
                    ReadWriteModel model = readWriteModel;
                    bool result = context.Communication.Read(ref model);
                    readWriteModel = model;
                    return result;
                }).ConfigureAwait(false);

                return DeviceExecutionActionResult.Create(
                    isSuccess,
                    isSuccess
                        ? $"Device '{context.DeviceName}' receive succeeded."
                        : $"Device '{context.DeviceName}' receive failed.",
                    context.DeviceName,
                    readWriteModel.Result);
            }
            catch (Exception ex)
            {
                return DeviceExecutionActionResult.CreateFailure(
                    $"Device '{context.DeviceName}' receive exception: {ex.Message}",
                    context.DeviceName);
            }
        }

        #endregion

        #region PLC 读写入口

        /// <summary>
        /// 根据设备名称写入 PLC 地址。
        /// </summary>
        public static async Task<DeviceExecutionActionResult> WritePlcAsync(
            string deviceName,
            string address,
            string value,
            int length = 1,
            DataType dataType = DataType.Decimal)
        {
            if (!TryGetDevice(deviceName, out DeviceRuntimeContext? context, out DeviceExecutionActionResult failure))
            {
                return failure;
            }

            if (!context.IsPlc)
            {
                return DeviceExecutionActionResult.CreateFailure($"Device '{context.DeviceName}' is not a PLC device.", context.DeviceName);
            }

            if (string.IsNullOrWhiteSpace(address))
            {
                return DeviceExecutionActionResult.CreateFailure("PLC address is required.", context.DeviceName);
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                return DeviceExecutionActionResult.CreateFailure("PLC write value is required.", context.DeviceName);
            }

            ReadWriteModel readWriteModel = new(value.Trim(), address.Trim(), Math.Max(1, length), dataType);
            bool isSuccess = await context.Communication.WriteAsync(readWriteModel).ConfigureAwait(false);
            return DeviceExecutionActionResult.Create(
                isSuccess,
                isSuccess
                    ? $"PLC '{context.DeviceName}' write '{address}' succeeded."
                    : $"PLC '{context.DeviceName}' write '{address}' failed.",
                context.DeviceName,
                readWriteModel.Result);
        }

        /// <summary>
        /// 根据设备名称读取 PLC 地址。
        /// </summary>
        public static async Task<DeviceExecutionActionResult> ReadPlcAsync(
            string deviceName,
            string address,
            int length = 1,
            DataType dataType = DataType.Decimal)
        {
            if (!TryGetDevice(deviceName, out DeviceRuntimeContext? context, out DeviceExecutionActionResult failure))
            {
                return failure;
            }

            if (!context.IsPlc)
            {
                return DeviceExecutionActionResult.CreateFailure($"Device '{context.DeviceName}' is not a PLC device.", context.DeviceName);
            }

            if (string.IsNullOrWhiteSpace(address))
            {
                return DeviceExecutionActionResult.CreateFailure("PLC address is required.", context.DeviceName);
            }

            ReadWriteModel readWriteModel = new(string.Empty, address.Trim(), Math.Max(1, length), dataType);
            bool isSuccess = await Task.Run(() =>
            {
                ReadWriteModel model = readWriteModel;
                bool result = context.Communication.Read(ref model);
                readWriteModel = model;
                return result;
            }).ConfigureAwait(false);

            return DeviceExecutionActionResult.Create(
                isSuccess,
                isSuccess
                    ? $"PLC '{context.DeviceName}' read '{address}' succeeded."
                    : $"PLC '{context.DeviceName}' read '{address}' failed.",
                context.DeviceName,
                readWriteModel.Result);
        }

        #endregion

        #region 协议指令发送与接收入口

        /// <summary>
        /// 根据协议文件中的协议名称和指令名称生成报文并发送。
        /// </summary>
        public static async Task<DeviceExecutionActionResult> SendProtocolAsync(
            string deviceName,
            string protocolName,
            string commandName,
            IReadOnlyDictionary<string, string>? placeholderValues = null,
            int waitTime = 10000,
            object? clientId = null)
        {
            if (!TryLoadProtocolCommand(protocolName, commandName, placeholderValues, out ProtocolCommandConfig? command, out string message))
            {
                return DeviceExecutionActionResult.CreateFailure(message, NormalizeRequiredText(deviceName));
            }

            if (!ProtocolPreviewEngine.TryBuildRequestPreview(command, out ProtocolRequestPreviewResult? preview, out message) ||
                preview is null)
            {
                return DeviceExecutionActionResult.CreateFailure(message, NormalizeRequiredText(deviceName));
            }

            string sendMessage = command.RequestFormat == ProtocolPayloadFormat.Hex
                ? $"0x{preview.RequestHex}"
                : preview.RequestAscii;

            DeviceExecutionActionResult sendResult = await SendAsync(
                    deviceName,
                    sendMessage,
                    command.WaitForResponse,
                    waitTime,
                    clientId)
                .ConfigureAwait(false);

            if (!sendResult.IsSuccess || !command.WaitForResponse || sendResult.Result is null)
            {
                return sendResult;
            }

            string responseText = sendResult.Result.ToString() ?? string.Empty;
            DeviceExecutionActionResult parseResult = ParseProtocolResponse(command, responseText, sendResult.DeviceName);
            return parseResult.IsSuccess
                ? DeviceExecutionActionResult.CreateSuccess(
                    sendResult.Message,
                    sendResult.DeviceName,
                    sendResult.Result,
                    parseResult.ParsedValuesJson)
                : sendResult;
        }

        /// <summary>
        /// 根据协议文件中的解析规则接收并解析一条返回数据。
        /// </summary>
        public static async Task<DeviceExecutionActionResult> ReceiveProtocolAsync(
            string deviceName,
            string protocolName,
            string commandName,
            int waitTime = 10000)
        {
            if (!TryLoadProtocolCommand(protocolName, commandName, null, out ProtocolCommandConfig? command, out string message))
            {
                return DeviceExecutionActionResult.CreateFailure(message, NormalizeRequiredText(deviceName));
            }

            DeviceExecutionActionResult receiveResult = await ReceiveAsync(deviceName, waitTime).ConfigureAwait(false);
            if (!receiveResult.IsSuccess || receiveResult.Result is null)
            {
                return receiveResult;
            }

            return ParseProtocolResponse(command, receiveResult.Result.ToString() ?? string.Empty, receiveResult.DeviceName);
        }

        #endregion

        #region 配置加载与设备初始化方法

        private static DeviceExecutionActionResult InitializeDevice(DeviceCommunicationProfile profile)
        {
            if (!profile.TryBuildRuntimeConfig(out CommuniactionConfigModel? config, out string validationMessage) || config is null)
            {
                return DeviceExecutionActionResult.CreateFailure(validationMessage, profile.LocalName);
            }

            DeviceRuntimeContext? oldContext = null;
            if (ActiveDevices.TryRemove(config.LocalName, out DeviceRuntimeContext? removedContext))
            {
                oldContext = removedContext;
            }

            try
            {
                oldContext?.Dispose();
                CommunicationFactory.Remove(config.LocalName);

                ICommunication communication = CommunicationFactory.CreateCommuniactionProtocol(config);
                DeviceRuntimeContext context = new(profile.Clone(config.LocalName), communication, config.Type);
                AttachCommunicationEvents(context);
                ActiveDevices[config.LocalName] = context;

                bool started = communication.Start();
                return DeviceExecutionActionResult.Create(
                    started,
                    started
                        ? $"Device '{config.LocalName}' started."
                        : $"Device '{config.LocalName}' start failed.",
                    config.LocalName,
                    communication.IsConnected);
            }
            catch (Exception ex)
            {
                oldContext?.Dispose();
                return DeviceExecutionActionResult.CreateFailure(
                    $"Device '{config.LocalName}' initialize exception: {ex.Message}",
                    config.LocalName);
            }
        }

        private static IReadOnlyList<DeviceCommunicationProfile> LoadDeviceProfiles()
        {
            if (!Directory.Exists(CommunicationConfigDirectory))
            {
                return Array.Empty<DeviceCommunicationProfile>();
            }

            List<DeviceCommunicationProfile> profiles = new();
            foreach (string filePath in Directory.EnumerateFiles(CommunicationConfigDirectory, "*.json", SearchOption.TopDirectoryOnly)
                         .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    DeviceCommunicationProfileDocument? document = JsonHelper.ReadJson<DeviceCommunicationProfileDocument>(filePath);
                    DeviceCommunicationProfile? profile = document?.ToProfile();
                    if (profile is not null)
                    {
                        profiles.Add(profile);
                    }
                }
                catch
                {
                    // 单个设备配置损坏时跳过，避免影响其他设备初始化。
                }
            }

            return profiles;
        }

        #endregion

        #region 协议文件读取与报文解析方法

        private static bool TryLoadProtocolCommand(
            string protocolName,
            string commandName,
            IReadOnlyDictionary<string, string>? placeholderValues,
            out ProtocolCommandConfig command,
            out string message)
        {
            command = new ProtocolCommandConfig();
            string normalizedProtocolName = NormalizeRequiredText(protocolName);
            string normalizedCommandName = NormalizeRequiredText(commandName);
            if (string.IsNullOrWhiteSpace(normalizedProtocolName))
            {
                message = "Protocol name is required.";
                return false;
            }

            ProtocolConfigProfile? profile = LoadProtocolProfiles().FirstOrDefault(item =>
                string.Equals(item.Name?.Trim(), normalizedProtocolName, StringComparison.OrdinalIgnoreCase));
            if (profile is null)
            {
                message = $"Protocol '{normalizedProtocolName}' was not found.";
                return false;
            }

            ProtocolCommandConfig? selectedCommand = string.IsNullOrWhiteSpace(normalizedCommandName)
                ? profile.CurrentCommand
                : profile.Commands.FirstOrDefault(item =>
                    string.Equals(item.Name?.Trim(), normalizedCommandName, StringComparison.OrdinalIgnoreCase));
            if (selectedCommand is null)
            {
                message = $"Command '{normalizedCommandName}' was not found in protocol '{normalizedProtocolName}'.";
                return false;
            }

            command = selectedCommand.Clone(selectedCommand.Name);
            if (placeholderValues is not null && placeholderValues.Count > 0)
            {
                command.PlaceholderValuesText = MergePlaceholderValues(command.PlaceholderValuesText, placeholderValues);
            }

            message = string.Empty;
            return true;
        }

        private static IReadOnlyList<ProtocolConfigProfile> LoadProtocolProfiles()
        {
            if (!Directory.Exists(ProtocolConfigDirectory))
            {
                return Array.Empty<ProtocolConfigProfile>();
            }

            List<ProtocolConfigProfile> profiles = new();
            foreach (string filePath in Directory.EnumerateFiles(ProtocolConfigDirectory, "*.json", SearchOption.TopDirectoryOnly)
                         .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    string storageText = File.ReadAllText(filePath, Encoding.UTF8);
                    ProtocolConfigProfileDocument? document =
                        JsonHelper.DeserializeObject<ProtocolConfigProfileDocument>(storageText.DesDecrypt());
                    ProtocolConfigProfile? profile = document?.ToProfile();
                    if (profile is not null)
                    {
                        profiles.Add(profile);
                    }
                }
                catch
                {
                    // 单个协议配置损坏时跳过，避免影响其他协议指令执行。
                }
            }

            return profiles;
        }

        private static DeviceExecutionActionResult ParseProtocolResponse(
            ProtocolCommandConfig command,
            string responseText,
            string deviceName)
        {
            ProtocolCommandConfig parseCommand = command.Clone(command.Name);
            parseCommand.SampleResponseText = responseText;

            if (!ProtocolPreviewEngine.TryBuildResponsePreview(parseCommand, out ProtocolResponsePreviewResult? responsePreview, out string message) ||
                responsePreview is null)
            {
                return DeviceExecutionActionResult.CreateFailure(message, deviceName, responseText);
            }

            return DeviceExecutionActionResult.CreateSuccess(
                $"Device '{deviceName}' protocol response parsed.",
                deviceName,
                responseText,
                responsePreview.ParsedJson);
        }

        private static string MergePlaceholderValues(
            string existingValuesText,
            IReadOnlyDictionary<string, string> overrideValues)
        {
            Dictionary<string, string> values = ParseKeyValueLines(existingValuesText);
            foreach (KeyValuePair<string, string> item in overrideValues)
            {
                if (!string.IsNullOrWhiteSpace(item.Key))
                {
                    values[item.Key.Trim()] = item.Value ?? string.Empty;
                }
            }

            return string.Join(Environment.NewLine, values.Select(item => $"{item.Key}={item.Value}"));
        }

        private static Dictionary<string, string> ParseKeyValueLines(string text)
        {
            Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
            foreach (string rawLine in (text ?? string.Empty).Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None))
            {
                string line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line) ||
                    line.StartsWith("#", StringComparison.Ordinal) ||
                    line.StartsWith("//", StringComparison.Ordinal))
                {
                    continue;
                }

                int equalsIndex = line.IndexOf('=');
                if (equalsIndex <= 0)
                {
                    continue;
                }

                values[line[..equalsIndex].Trim()] = line[(equalsIndex + 1)..].Trim();
            }

            return values;
        }

        #endregion

        #region 运行时事件与通用工具

        private static bool TryGetDevice(
            string deviceName,
            out DeviceRuntimeContext context,
            out DeviceExecutionActionResult failure)
        {
            string normalizedDeviceName = NormalizeRequiredText(deviceName);
            if (string.IsNullOrWhiteSpace(normalizedDeviceName))
            {
                context = null!;
                failure = DeviceExecutionActionResult.CreateFailure("Device name is required.");
                return false;
            }

            if (!ActiveDevices.TryGetValue(normalizedDeviceName, out DeviceRuntimeContext? runtimeContext))
            {
                context = null!;
                failure = DeviceExecutionActionResult.CreateFailure($"Device '{normalizedDeviceName}' is not initialized.", normalizedDeviceName);
                return false;
            }

            context = runtimeContext;
            failure = DeviceExecutionActionResult.CreateSuccess(string.Empty, normalizedDeviceName);
            return true;
        }

        private static void AttachCommunicationEvents(DeviceRuntimeContext context)
        {
            context.Communication.StateChange += Communication_StateChange;
        }

        private static void DetachCommunicationEvents(DeviceRuntimeContext context)
        {
            context.Communication.StateChange -= Communication_StateChange;
        }

        private static void Communication_StateChange(ConnectState connectState, string localName)
        {
            DeviceConnectionStateChanged?.Invoke(
                null,
                new DeviceConnectionStateChangedEventArgs(localName, connectState, DateTime.Now));
        }

        private static string NormalizeRequiredText(string? value)
        {
            return value?.Trim() ?? string.Empty;
        }

        #endregion

        #region 内部运行时上下文

        private sealed class DeviceRuntimeContext : IDisposable
        {
            public DeviceRuntimeContext(
                DeviceCommunicationProfile profile,
                ICommunication communication,
                CommuniactionType communicationType)
            {
                Profile = profile;
                Communication = communication;
                CommunicationType = communicationType;
                StartedAt = DateTime.Now;
            }

            public DeviceCommunicationProfile Profile { get; }

            public ICommunication Communication { get; }

            public CommuniactionType CommunicationType { get; }

            public DateTime StartedAt { get; }

            public string DeviceName => Communication.LocalName;

            public bool IsPlc => CommunicationType is CommuniactionType.PLC or CommuniactionType.MX;

            public DeviceExecutionSnapshot CreateSnapshot()
            {
                return new DeviceExecutionSnapshot(
                    DeviceName,
                    CommunicationType,
                    Communication.IsConnected,
                    StartedAt,
                    Profile.Summary);
            }

            public void Dispose()
            {
                DetachCommunicationEvents(this);
                Communication.Close();
            }
        }

        #endregion
    }

    #region 设备执行结果与事件模型

    public sealed class DeviceConnectionStateChangedEventArgs : EventArgs
    {
        public DeviceConnectionStateChangedEventArgs(string deviceName, ConnectState connectState, DateTime changedAt)
        {
            DeviceName = deviceName;
            ConnectState = connectState;
            ChangedAt = changedAt;
        }

        public string DeviceName { get; }

        public ConnectState ConnectState { get; }

        public DateTime ChangedAt { get; }
    }

    public sealed class DeviceInitializationResult
    {
        public DeviceInitializationResult(IReadOnlyList<DeviceExecutionActionResult> deviceResults)
        {
            DeviceResults = deviceResults;
        }

        public IReadOnlyList<DeviceExecutionActionResult> DeviceResults { get; }

        public int TotalCount => DeviceResults.Count;

        public int SuccessCount => DeviceResults.Count(item => item.IsSuccess);

        public bool IsSuccess => TotalCount > 0 && SuccessCount == TotalCount;
    }

    public sealed class DeviceExecutionActionResult
    {
        public DeviceExecutionActionResult(
            bool isSuccess,
            string message,
            string deviceName = "",
            object? result = null,
            string parsedValuesJson = "")
        {
            IsSuccess = isSuccess;
            Message = message ?? string.Empty;
            DeviceName = deviceName ?? string.Empty;
            Result = result;
            ParsedValuesJson = parsedValuesJson ?? string.Empty;
        }

        public bool IsSuccess { get; }

        public string Message { get; }

        public string DeviceName { get; }

        public object? Result { get; }

        public string ParsedValuesJson { get; }

        public static DeviceExecutionActionResult Create(
            bool isSuccess,
            string message,
            string deviceName = "",
            object? result = null,
            string parsedValuesJson = "")
        {
            return new DeviceExecutionActionResult(isSuccess, message, deviceName, result, parsedValuesJson);
        }

        public static DeviceExecutionActionResult CreateSuccess(
            string message,
            string deviceName = "",
            object? result = null,
            string parsedValuesJson = "")
        {
            return new DeviceExecutionActionResult(true, message, deviceName, result, parsedValuesJson);
        }

        public static DeviceExecutionActionResult CreateFailure(
            string message,
            string deviceName = "",
            object? result = null,
            string parsedValuesJson = "")
        {
            return new DeviceExecutionActionResult(false, message, deviceName, result, parsedValuesJson);
        }
    }

    public sealed record DeviceExecutionSnapshot(
        string DeviceName,
        CommuniactionType CommunicationType,
        ConnectState ConnectState,
        DateTime StartedAt,
        string Summary);

    #endregion
}
