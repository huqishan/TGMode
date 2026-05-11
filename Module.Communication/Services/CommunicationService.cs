using ControlLibrary;
using Module.Communication.Models;
using Shared.Abstractions;
using Shared.Abstractions.Enum;
using Shared.Infrastructure.Communication;
using Shared.Infrastructure.Extensions;
using Shared.Infrastructure.Events;
using Shared.Infrastructure.PackMethod;
using Shared.Models.Communication;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Module.Communication.Services
{
    /// <summary>
    /// 提供设备初始化、发送数据和解析结果查询能力。
    /// </summary>
    public class CommunicationService : ModuleService
    {
        #region 字段

        private const string DefaultParsedDataKey = "Data";

        private static readonly string CommunicationConfigDirectory =
            Path.Combine(AppContext.BaseDirectory, "Config", "Communication");

        private static readonly ConcurrentDictionary<string, DeviceRuntimeContext> ActiveDevices =
            new(StringComparer.OrdinalIgnoreCase);

        #endregion

        #region 构造函数

        /// <summary>
        /// 初始化通信服务实例。
        /// </summary>
        /// <param name="eventAggregator">事件聚合器。</param>
        public CommunicationService(IEventAggregator eventAggregator)
        {
            _EventAggregator = eventAggregator;
        }

        #endregion

        #region 事件

        /// <summary>
        /// 设备连接状态发生变化时触发。
        /// </summary>
        public event EventHandler<DeviceConnectionStateChangedEventArgs>? DeviceConnectionStateChanged;

        #endregion

        #region 设备管理

        /// <summary>
        /// 初始化通信配置目录中的全部设备。
        /// </summary>
        /// <returns>全部设备的初始化结果。</returns>
        public DeviceInitializationResult InitializeAllDevices()
        {
            IReadOnlyList<DeviceCommunicationProfile> profiles = LoadDeviceProfiles();
            List<DeviceExecutionActionResult> results = new(profiles.Count);

            foreach (DeviceCommunicationProfile profile in profiles)
            {
                results.Add(InitializeDevice(profile));
            }

            return new DeviceInitializationResult(results);
        }

        /// <summary>
        /// 按设备名称初始化单个设备。
        /// </summary>
        /// <param name="deviceName">设备名称。</param>
        /// <returns>设备初始化结果。</returns>
        public DeviceExecutionActionResult InitializeDeviceByName(string deviceName)
        {
            string normalizedDeviceName = NormalizeRequiredText(deviceName);
            if (string.IsNullOrWhiteSpace(normalizedDeviceName))
            {
                return DeviceExecutionActionResult.CreateFailure("Device name is required.");
            }

            DeviceCommunicationProfile? profile = FindDeviceProfileByName(normalizedDeviceName);
            if (profile is null)
            {
                return DeviceExecutionActionResult.CreateFailure(
                    $"Device '{normalizedDeviceName}' was not found in communication configuration.",
                    normalizedDeviceName);
            }

            return InitializeDevice(profile);
        }

        /// <summary>
        /// 按设备名称关闭单个已初始化设备。
        /// </summary>
        /// <param name="deviceName">设备名称。</param>
        /// <returns>设备关闭结果。</returns>
        public DeviceExecutionActionResult CloseDeviceByName(string deviceName)
        {
            string normalizedDeviceName = NormalizeRequiredText(deviceName);
            if (string.IsNullOrWhiteSpace(normalizedDeviceName))
            {
                return DeviceExecutionActionResult.CreateFailure("Device name is required.");
            }

            if (!ActiveDevices.TryRemove(normalizedDeviceName, out DeviceRuntimeContext? context))
            {
                return DeviceExecutionActionResult.CreateFailure(
                    $"Device '{normalizedDeviceName}' is not initialized.",
                    normalizedDeviceName);
            }

            DisposeRuntimeContext(context);
            CommunicationFactory.Remove(normalizedDeviceName);

            return DeviceExecutionActionResult.CreateSuccess(
                $"Device '{normalizedDeviceName}' closed.",
                normalizedDeviceName);
        }

        /// <summary>
        /// 关闭当前全部已初始化设备。
        /// </summary>
        public void CloseAllDevices()
        {
            foreach (string deviceName in ActiveDevices.Keys.ToArray())
            {
                CloseDeviceByName(deviceName);
            }
        }

        #endregion

        #region 发送与查询

        /// <summary>
        /// 向指定设备发送数据，并可选择是否等待回复。
        /// </summary>
        /// <param name="deviceName">设备名称。</param>
        /// <param name="readWriteModel">读写数据模型。</param>
        /// <param name="isWait">是否等待设备回复。</param>
        /// <returns>发送结果。</returns>
        public DeviceExecutionActionResult SendData(string deviceName, ReadWriteModel readWriteModel, bool isWait = false)
        {
            if (readWriteModel is null)
            {
                return DeviceExecutionActionResult.CreateFailure(
                    "ReadWriteModel is required.",
                    NormalizeRequiredText(deviceName));
            }

            if (!TryGetDevice(deviceName, out DeviceRuntimeContext? context, out DeviceExecutionActionResult failure))
            {
                return failure;
            }

            return ExecuteSend(context, readWriteModel, isWait);
        }

        /// <summary>
        /// 获取指定设备指定键的最新解析结果。
        /// </summary>
        /// <param name="deviceName">设备名称。</param>
        /// <param name="key">解析结果键。</param>
        /// <returns>匹配到的解析结果，未找到时返回 <c>null</c>。</returns>
        public ParsedDeviceDataEntity? GetParsedDataByNameAndKey(string deviceName, string key)
        {
            if (!TryGetDevice(deviceName, out DeviceRuntimeContext? context, out _))
            {
                return null;
            }

            string normalizedKey = NormalizeRequiredText(key);
            if (string.IsNullOrWhiteSpace(normalizedKey))
            {
                return null;
            }

            return context.ParsedDataByKey.TryGetValue(normalizedKey, out ParsedDeviceDataEntity? entity)
                ? entity
                : null;
        }

        #endregion

        #region 初始化辅助

        /// <summary>
        /// 根据设备配置初始化运行时通信实例。
        /// </summary>
        /// <param name="profile">设备通信配置。</param>
        /// <returns>初始化结果。</returns>
        private DeviceExecutionActionResult InitializeDevice(DeviceCommunicationProfile profile)
        {
            if (!TryBuildRuntimeConfig(profile, out CommuniactionConfigModel? config, out DeviceExecutionActionResult failure))
            {
                return failure;
            }

            if (!TryCreateRuntimeContext(profile, config, out DeviceRuntimeContext? context, out failure))
            {
                return failure;
            }

            ActiveDevices[config.LocalName] = context;
            return StartDevice(context.Communication, config.LocalName);
        }

        /// <summary>
        /// 根据设备名称查找设备配置。
        /// </summary>
        /// <param name="deviceName">设备名称。</param>
        /// <returns>匹配到的设备配置，未找到时返回 <c>null</c>。</returns>
        private static DeviceCommunicationProfile? FindDeviceProfileByName(string deviceName)
        {
            return LoadDeviceProfiles().FirstOrDefault(item =>
                string.Equals(item.LocalName, deviceName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 构建设备运行时所需的通信配置。
        /// </summary>
        /// <param name="profile">设备通信配置。</param>
        /// <param name="config">生成后的运行时配置。</param>
        /// <param name="failure">失败时的结果信息。</param>
        /// <returns>是否构建成功。</returns>
        private static bool TryBuildRuntimeConfig(
            DeviceCommunicationProfile profile,
            out CommuniactionConfigModel config,
            out DeviceExecutionActionResult failure)
        {
            config = null!;
            failure = default!;

            if (profile.TryBuildRuntimeConfig(out CommuniactionConfigModel? runtimeConfig, out string validationMessage) &&
                runtimeConfig is not null)
            {
                config = runtimeConfig;
                return true;
            }

            failure = DeviceExecutionActionResult.CreateFailure(validationMessage, profile.LocalName);
            return false;
        }

        /// <summary>
        /// 创建并绑定设备运行时上下文。
        /// </summary>
        /// <param name="profile">设备通信配置。</param>
        /// <param name="config">运行时通信配置。</param>
        /// <param name="context">创建后的运行时上下文。</param>
        /// <param name="failure">失败时的结果信息。</param>
        /// <returns>是否创建成功。</returns>
        private bool TryCreateRuntimeContext(
            DeviceCommunicationProfile profile,
            CommuniactionConfigModel config,
            out DeviceRuntimeContext context,
            out DeviceExecutionActionResult failure)
        {
            context = null!;
            failure = default!;

            DeviceRuntimeContext? oldContext = RemoveExistingRuntimeContext(config.LocalName);
            ICommunication? communication = null;

            try
            {
                DisposeRuntimeContext(oldContext);
                CommunicationFactory.Remove(config.LocalName);

                communication = CommunicationFactory.CreateCommuniactionProtocol(config);
                context = new DeviceRuntimeContext(profile.Clone(config.LocalName), communication, config.Type);
                AttachCommunicationEvents(context);
                return true;
            }
            catch (Exception ex)
            {
                communication?.Close();
                failure = DeviceExecutionActionResult.CreateFailure(
                    $"Device '{config.LocalName}' initialize exception: {ex.Message}",
                    config.LocalName);
                return false;
            }
        }

        /// <summary>
        /// 移除旧的运行时上下文，便于重新初始化设备。
        /// </summary>
        /// <param name="deviceName">设备名称。</param>
        /// <returns>旧的运行时上下文。</returns>
        private static DeviceRuntimeContext? RemoveExistingRuntimeContext(string deviceName)
        {
            return ActiveDevices.TryRemove(deviceName, out DeviceRuntimeContext? context)
                ? context
                : null;
        }

        /// <summary>
        /// 启动通信实例并生成统一的执行结果。
        /// </summary>
        /// <param name="communication">通信实例。</param>
        /// <param name="deviceName">设备名称。</param>
        /// <returns>启动结果。</returns>
        private static DeviceExecutionActionResult StartDevice(ICommunication communication, string deviceName)
        {
            bool started = communication.Start();

            return DeviceExecutionActionResult.Create(
                started,
                started
                    ? $"Device '{deviceName}' started."
                    : $"Device '{deviceName}' start failed.",
                deviceName,
                communication.IsConnected);
        }

        /// <summary>
        /// 从通信配置目录加载全部设备配置。
        /// </summary>
        /// <returns>设备配置集合。</returns>
        private static IReadOnlyList<DeviceCommunicationProfile> LoadDeviceProfiles()
        {
            if (!Directory.Exists(CommunicationConfigDirectory))
            {
                return Array.Empty<DeviceCommunicationProfile>();
            }

            List<DeviceCommunicationProfile> profiles = new();

            foreach (string filePath in EnumerateCommunicationConfigFiles())
            {
                DeviceCommunicationProfile? profile = TryLoadDeviceProfile(filePath);
                if (profile is not null)
                {
                    profiles.Add(profile);
                }
            }

            return profiles;
        }

        /// <summary>
        /// 枚举通信配置目录中的全部设备配置文件。
        /// </summary>
        /// <returns>配置文件路径集合。</returns>
        private static IEnumerable<string> EnumerateCommunicationConfigFiles()
        {
            return Directory.EnumerateFiles(CommunicationConfigDirectory, "*.json", SearchOption.TopDirectoryOnly)
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 尝试从文件中加载单个设备配置。
        /// </summary>
        /// <param name="filePath">配置文件路径。</param>
        /// <returns>设备配置，加载失败时返回 <c>null</c>。</returns>
        private static DeviceCommunicationProfile? TryLoadDeviceProfile(string filePath)
        {
            try
            {
                DeviceCommunicationProfileDocument? document =
                    JsonHelper.ReadJson<DeviceCommunicationProfileDocument>(filePath);

                return document?.ToProfile();
            }
            catch
            {
                return null;
            }
        }

        #endregion

        #region 协议解析辅助

        /// <summary>
        /// 尝试使用已绑定的仅解析指令处理收到的原始消息。
        /// </summary>
        /// <param name="context">设备运行时上下文。</param>
        /// <param name="message">接收到的原始消息。</param>
        private static void TryParseIncomingProtocolMessage(DeviceRuntimeContext context, object message)
        {
            if (context.ParseOnlyCommands.Count == 0)
            {
                return;
            }

            string rawMessage = BuildRawProtocolData(message);

            foreach (BoundParseOnlyCommand parseCommand in context.ParseOnlyCommands)
            {
                if (!TryBuildProtocolPreviewResult(parseCommand, message, rawMessage, out ProtocolResponsePreviewResult? previewResult) ||
                    previewResult is null)
                {
                    continue;
                }

                AppendParsedProtocolResults(
                    context,
                    previewResult.ParsedJson,
                    rawMessage,
                    parseCommand.ProtocolName,
                    parseCommand.CommandName);
            }
        }

        /// <summary>
        /// 尝试生成单条仅解析指令的预览结果。
        /// </summary>
        /// <param name="parseCommand">仅解析指令绑定信息。</param>
        /// <param name="message">接收到的原始消息。</param>
        /// <param name="rawMessage">原始报文文本。</param>
        /// <param name="previewResult">生成的预览结果。</param>
        /// <returns>是否生成成功。</returns>
        private static bool TryBuildProtocolPreviewResult(
            BoundParseOnlyCommand parseCommand,
            object message,
            string rawMessage,
            out ProtocolResponsePreviewResult? previewResult)
        {
            previewResult = null!;

            string responseText = BuildProtocolResponseText(message, rawMessage, parseCommand.Command.ResponseFormat);
            if (string.IsNullOrWhiteSpace(responseText))
            {
                return false;
            }

            ProtocolCommandConfig command = parseCommand.Command.Clone(parseCommand.CommandName);
            command.SampleResponseText = responseText;

            return ProtocolPreviewEngine.TryBuildResponsePreview(command, out previewResult, out _) &&
                   previewResult is not null;
        }

        /// <summary>
        /// 将解析后的 JSON 结果写入设备解析缓存。
        /// </summary>
        /// <param name="context">设备运行时上下文。</param>
        /// <param name="parsedJson">解析后的 JSON 文本。</param>
        /// <param name="rawMessage">原始报文。</param>
        /// <param name="protocolName">协议名称。</param>
        /// <param name="commandName">指令名称。</param>
        private static void AppendParsedProtocolResults(
            DeviceRuntimeContext context,
            string parsedJson,
            string rawMessage,
            string protocolName,
            string commandName)
        {
            if (string.IsNullOrWhiteSpace(parsedJson))
            {
                return;
            }

            if (TryAppendJsonParsedResults(context, parsedJson, rawMessage, protocolName, commandName))
            {
                return;
            }

            UpdateParsedData(context, DefaultParsedDataKey, parsedJson, rawMessage, protocolName, commandName);
        }

        /// <summary>
        /// 尝试将 JSON 文本拆分为键值后写入缓存。
        /// </summary>
        /// <param name="context">设备运行时上下文。</param>
        /// <param name="parsedJson">解析后的 JSON 文本。</param>
        /// <param name="rawMessage">原始报文。</param>
        /// <param name="protocolName">协议名称。</param>
        /// <param name="commandName">指令名称。</param>
        /// <returns>是否成功按 JSON 结构写入缓存。</returns>
        private static bool TryAppendJsonParsedResults(
            DeviceRuntimeContext context,
            string parsedJson,
            string rawMessage,
            string protocolName,
            string commandName)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(parsedJson);

                if (document.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (JsonProperty property in document.RootElement.EnumerateObject())
                    {
                        UpdateParsedData(
                            context,
                            property.Name,
                            FormatJsonValue(property.Value),
                            rawMessage,
                            protocolName,
                            commandName);
                    }

                    return true;
                }

                UpdateParsedData(
                    context,
                    DefaultParsedDataKey,
                    FormatJsonValue(document.RootElement),
                    rawMessage,
                    protocolName,
                    commandName);

                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        /// <summary>
        /// 更新指定键对应的解析缓存数据。
        /// </summary>
        /// <param name="context">设备运行时上下文。</param>
        /// <param name="key">解析结果键。</param>
        /// <param name="value">解析结果值。</param>
        /// <param name="rawMessage">原始报文。</param>
        /// <param name="protocolName">协议名称。</param>
        /// <param name="commandName">指令名称。</param>
        private static void UpdateParsedData(
            DeviceRuntimeContext context,
            string key,
            string value,
            string rawMessage,
            string protocolName,
            string commandName)
        {
            ParsedDeviceDataEntity entity = new(
                context.DeviceName,
                key ?? string.Empty,
                value ?? string.Empty,
                rawMessage ?? string.Empty,
                protocolName ?? string.Empty,
                commandName ?? string.Empty,
                DateTime.Now);

            context.ParsedDataByKey[entity.Key] = entity;
        }

        /// <summary>
        /// 从设备支持的协议中加载全部仅解析指令。
        /// </summary>
        /// <param name="profile">设备通信配置。</param>
        /// <returns>仅解析指令列表。</returns>
        private static List<BoundParseOnlyCommand> LoadParseOnlyCommands(DeviceCommunicationProfile profile)
        {
            List<BoundParseOnlyCommand> commands = new();

            foreach (DeviceSupportedProtocol supportedProtocol in EnumerateDistinctSupportedProtocols(profile))
            {
                if (!TryReadProtocolProfileFromFile(
                        supportedProtocol.ProtocolFilePath,
                        out ProtocolConfigProfile? protocolProfile,
                        out _) ||
                    protocolProfile is null)
                {
                    continue;
                }

                commands.AddRange(CreateParseOnlyCommands(protocolProfile));
            }

            return commands;
        }

        /// <summary>
        /// 去重并筛选设备支持的有效协议。
        /// </summary>
        /// <param name="profile">设备通信配置。</param>
        /// <returns>有效协议集合。</returns>
        private static IEnumerable<DeviceSupportedProtocol> EnumerateDistinctSupportedProtocols(DeviceCommunicationProfile profile)
        {
            return profile.SupportedProtocols
                .Where(protocol =>
                    !string.IsNullOrWhiteSpace(protocol.ProtocolName) &&
                    !string.IsNullOrWhiteSpace(protocol.ProtocolFilePath))
                .GroupBy(
                    protocol => $"{protocol.ProtocolName}|{protocol.ProtocolFilePath}",
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First());
        }

        /// <summary>
        /// 从协议配置中提取全部仅解析指令绑定信息。
        /// </summary>
        /// <param name="protocolProfile">协议配置。</param>
        /// <returns>仅解析指令绑定集合。</returns>
        private static IEnumerable<BoundParseOnlyCommand> CreateParseOnlyCommands(ProtocolConfigProfile protocolProfile)
        {
            return protocolProfile.Commands
                .Where(command => command.IsParseOnly)
                .Select(command => new BoundParseOnlyCommand(
                    protocolProfile.Name,
                    command.Name,
                    command.Clone(command.Name)));
        }

        #endregion

        #region 协议文件加载

        /// <summary>
        /// 从协议文件中读取协议配置。
        /// </summary>
        /// <param name="filePath">协议文件路径。</param>
        /// <param name="profile">读取成功后的协议配置。</param>
        /// <param name="message">读取结果说明。</param>
        /// <returns>是否读取成功。</returns>
        private static bool TryReadProtocolProfileFromFile(
            string filePath,
            out ProtocolConfigProfile? profile,
            out string message)
        {
            profile = null;
            message = string.Empty;

            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                message = $"Protocol file '{filePath}' was not found.";
                return false;
            }

            try
            {
                string storageText = File.ReadAllText(filePath, Encoding.UTF8);
                if (!TryReadProtocolProfileDocument(storageText, out ProtocolConfigProfileDocument? document) || document is null)
                {
                    message = "Protocol file format is invalid.";
                    return false;
                }

                profile = document.ToProfile();
                return true;
            }
            catch (Exception ex)
            {
                message = $"Read protocol file failed: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// 尝试从存储文本中反序列化协议配置文档。
        /// </summary>
        /// <param name="storageText">协议存储文本。</param>
        /// <param name="document">反序列化后的协议配置文档。</param>
        /// <returns>是否反序列化成功。</returns>
        private static bool TryReadProtocolProfileDocument(string storageText, out ProtocolConfigProfileDocument? document)
        {
            document = null;

            if (string.IsNullOrWhiteSpace(storageText))
            {
                return false;
            }

            return TryDecryptProtocolProfileText(storageText, out string? decryptedText) &&
                   TryDeserializeProtocolProfileDocument(decryptedText, out document) ||
                   TryDeserializeProtocolProfileDocument(storageText, out document);
        }

        /// <summary>
        /// 尝试对协议存储文本进行解密。
        /// </summary>
        /// <param name="storageText">协议存储文本。</param>
        /// <param name="decryptedText">解密后的文本。</param>
        /// <returns>是否解密成功。</returns>
        private static bool TryDecryptProtocolProfileText(string storageText, out string decryptedText)
        {
            decryptedText = string.Empty;

            try
            {
                decryptedText = storageText.DesDecrypt();
                return !string.IsNullOrWhiteSpace(decryptedText);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 尝试将文本反序列化为协议配置文档。
        /// </summary>
        /// <param name="text">待反序列化文本。</param>
        /// <param name="document">反序列化后的协议配置文档。</param>
        /// <returns>是否反序列化成功。</returns>
        private static bool TryDeserializeProtocolProfileDocument(string text, out ProtocolConfigProfileDocument? document)
        {
            document = null;

            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            try
            {
                document = JsonHelper.DeserializeObject<ProtocolConfigProfileDocument>(text);
                return document is not null;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region 运行时查找与事件

        /// <summary>
        /// 按设备名称获取运行时上下文。
        /// </summary>
        /// <param name="deviceName">设备名称。</param>
        /// <param name="context">查找到的运行时上下文。</param>
        /// <param name="failure">失败时返回的结果信息。</param>
        /// <returns>是否查找成功。</returns>
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
                failure = DeviceExecutionActionResult.CreateFailure(
                    $"Device '{normalizedDeviceName}' is not initialized.",
                    normalizedDeviceName);
                return false;
            }

            context = runtimeContext;
            failure = DeviceExecutionActionResult.CreateSuccess(string.Empty, normalizedDeviceName);
            return true;
        }

        /// <summary>
        /// 为通信实例绑定接收和状态变更事件。
        /// </summary>
        /// <param name="context">设备运行时上下文。</param>
        private void AttachCommunicationEvents(DeviceRuntimeContext context)
        {
            context.ReceiveHandler = CreateReceiveHandler(context);
            context.StateChangedHandler = CreateStateChangedHandler();

            context.Communication.OnReceive += context.ReceiveHandler;
            context.Communication.StateChange += context.StateChangedHandler;
        }

        /// <summary>
        /// 创建接收消息事件处理器。
        /// </summary>
        /// <param name="context">设备运行时上下文。</param>
        /// <returns>接收消息处理委托。</returns>
        private static ReceiveData CreateReceiveHandler(DeviceRuntimeContext context)
        {
            return (message, _) =>
            {
                // 仅解析指令统一在接收回调中处理，并将结果缓存起来。
                TryParseIncomingProtocolMessage(context, message);
                return string.Empty;
            };
        }

        /// <summary>
        /// 创建连接状态变化事件处理器。
        /// </summary>
        /// <returns>状态变化处理委托。</returns>
        private StateChanged CreateStateChangedHandler()
        {
            return (connectState, localName) =>
            {
                DeviceConnectionStateChanged?.Invoke(
                    this,
                    new DeviceConnectionStateChangedEventArgs(localName, connectState, DateTime.Now));
            };
        }

        /// <summary>
        /// 解除通信实例上已绑定的事件处理。
        /// </summary>
        /// <param name="context">设备运行时上下文。</param>
        private static void DetachCommunicationEvents(DeviceRuntimeContext context)
        {
            if (context.ReceiveHandler is not null)
            {
                context.Communication.OnReceive -= context.ReceiveHandler;
            }

            if (context.StateChangedHandler is not null)
            {
                context.Communication.StateChange -= context.StateChangedHandler;
            }
        }

        /// <summary>
        /// 释放运行时上下文占用的事件和连接资源。
        /// </summary>
        /// <param name="context">设备运行时上下文。</param>
        private static void DisposeRuntimeContext(DeviceRuntimeContext? context)
        {
            context?.Dispose();
        }

        /// <summary>
        /// 执行发送操作，并统一封装执行结果。
        /// </summary>
        /// <param name="context">设备运行时上下文。</param>
        /// <param name="readWriteModel">读写数据模型。</param>
        /// <param name="isWait">是否等待回复。</param>
        /// <returns>发送结果。</returns>
        private static DeviceExecutionActionResult ExecuteSend(
            DeviceRuntimeContext context,
            ReadWriteModel readWriteModel,
            bool isWait)
        {
            try
            {
                ReadWriteModel model = readWriteModel;
                bool isSuccess = context.Communication.Write(ref model, isWait);

                return DeviceExecutionActionResult.Create(
                    isSuccess,
                    isSuccess
                        ? $"Device '{context.DeviceName}' send succeeded."
                        : $"Device '{context.DeviceName}' send failed.",
                    context.DeviceName,
                    model.Result);
            }
            catch (Exception ex)
            {
                return DeviceExecutionActionResult.CreateFailure(
                    $"Device '{context.DeviceName}' send exception: {ex.Message}",
                    context.DeviceName);
            }
        }

        #endregion

        #region 文本与格式化辅助

        /// <summary>
        /// 规范化必填文本，空值时返回空字符串。
        /// </summary>
        /// <param name="value">原始文本。</param>
        /// <returns>去除首尾空白后的文本。</returns>
        private static string NormalizeRequiredText(string? value)
        {
            return value?.Trim() ?? string.Empty;
        }

        /// <summary>
        /// 将接收到的消息转换为统一的原始报文文本。
        /// </summary>
        /// <param name="message">接收到的原始消息。</param>
        /// <returns>原始报文文本。</returns>
        private static string BuildRawProtocolData(object? message)
        {
            return message switch
            {
                null => string.Empty,
                byte[] bytes => BitConverter.ToString(bytes).Replace("-", string.Empty, StringComparison.Ordinal),
                _ => message.ToString() ?? string.Empty
            };
        }

        /// <summary>
        /// 按响应格式构建用于协议解析的响应文本。
        /// </summary>
        /// <param name="message">接收到的原始消息。</param>
        /// <param name="rawMessage">原始报文文本。</param>
        /// <param name="responseFormat">响应格式。</param>
        /// <returns>用于解析的响应文本。</returns>
        private static string BuildProtocolResponseText(
            object? message,
            string rawMessage,
            ProtocolPayloadFormat responseFormat)
        {
            if (message is byte[] bytes)
            {
                return responseFormat == ProtocolPayloadFormat.Hex
                    ? BitConverter.ToString(bytes).Replace("-", string.Empty, StringComparison.Ordinal)
                    : Encoding.UTF8.GetString(bytes);
            }

            return rawMessage;
        }

        /// <summary>
        /// 将 JSON 节点转换为可缓存的字符串值。
        /// </summary>
        /// <param name="value">JSON 节点。</param>
        /// <returns>格式化后的字符串值。</returns>
        private static string FormatJsonValue(JsonElement value)
        {
            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString() ?? string.Empty,
                JsonValueKind.Number => value.GetRawText(),
                JsonValueKind.True => bool.TrueString,
                JsonValueKind.False => bool.FalseString,
                JsonValueKind.Null => string.Empty,
                _ => value.GetRawText()
            };
        }

        #endregion

        #region 运行时上下文

        /// <summary>
        /// 保存单个设备运行期依赖和缓存数据。
        /// </summary>
        private sealed class DeviceRuntimeContext : IDisposable
        {
            /// <summary>
            /// 初始化设备运行时上下文。
            /// </summary>
            /// <param name="profile">设备配置。</param>
            /// <param name="communication">通信实例。</param>
            /// <param name="communicationType">通信类型。</param>
            public DeviceRuntimeContext(
                DeviceCommunicationProfile profile,
                ICommunication communication,
                CommuniactionType communicationType)
            {
                Profile = profile;
                Communication = communication;
                CommunicationType = communicationType;
                ParseOnlyCommands = LoadParseOnlyCommands(profile);
            }

            /// <summary>
            /// 设备配置快照。
            /// </summary>
            public DeviceCommunicationProfile Profile { get; }

            /// <summary>
            /// 当前通信实例。
            /// </summary>
            public ICommunication Communication { get; }

            /// <summary>
            /// 当前通信类型。
            /// </summary>
            public CommuniactionType CommunicationType { get; }

            /// <summary>
            /// 当前设备名称。
            /// </summary>
            public string DeviceName => Communication.LocalName;

            /// <summary>
            /// 当前设备绑定的仅解析指令集合。
            /// </summary>
            public List<BoundParseOnlyCommand> ParseOnlyCommands { get; }

            /// <summary>
            /// 解析结果缓存。
            /// </summary>
            public ConcurrentDictionary<string, ParsedDeviceDataEntity> ParsedDataByKey { get; } =
                new(StringComparer.OrdinalIgnoreCase);

            /// <summary>
            /// 接收消息事件处理器。
            /// </summary>
            public ReceiveData? ReceiveHandler { get; set; }

            /// <summary>
            /// 状态变化事件处理器。
            /// </summary>
            public StateChanged? StateChangedHandler { get; set; }

            /// <summary>
            /// 释放运行时上下文占用的事件和连接资源。
            /// </summary>
            public void Dispose()
            {
                DetachCommunicationEvents(this);
                Communication.Close();
            }
        }

        /// <summary>
        /// 表示协议中的仅解析指令与所属协议信息的绑定关系。
        /// </summary>
        private sealed record BoundParseOnlyCommand(
            string ProtocolName,
            string CommandName,
            ProtocolCommandConfig Command);

        #endregion
    }

    #region 结果模型

    /// <summary>
    /// 设备连接状态变化事件参数。
    /// </summary>
    public sealed class DeviceConnectionStateChangedEventArgs : EventArgs
    {
        /// <summary>
        /// 初始化设备连接状态变化事件参数。
        /// </summary>
        /// <param name="deviceName">设备名称。</param>
        /// <param name="connectState">连接状态。</param>
        /// <param name="changedAt">状态变更时间。</param>
        public DeviceConnectionStateChangedEventArgs(string deviceName, ConnectState connectState, DateTime changedAt)
        {
            DeviceName = deviceName;
            ConnectState = connectState;
            ChangedAt = changedAt;
        }

        /// <summary>
        /// 设备名称。
        /// </summary>
        public string DeviceName { get; }

        /// <summary>
        /// 连接状态。
        /// </summary>
        public ConnectState ConnectState { get; }

        /// <summary>
        /// 状态变更时间。
        /// </summary>
        public DateTime ChangedAt { get; }
    }

    /// <summary>
    /// 批量初始化设备后的汇总结果。
    /// </summary>
    public sealed class DeviceInitializationResult
    {
        /// <summary>
        /// 初始化批量设备执行结果。
        /// </summary>
        /// <param name="deviceResults">各设备执行结果集合。</param>
        public DeviceInitializationResult(IReadOnlyList<DeviceExecutionActionResult> deviceResults)
        {
            DeviceResults = deviceResults;
        }

        /// <summary>
        /// 各设备执行结果集合。
        /// </summary>
        public IReadOnlyList<DeviceExecutionActionResult> DeviceResults { get; }

        /// <summary>
        /// 设备总数。
        /// </summary>
        public int TotalCount => DeviceResults.Count;

        /// <summary>
        /// 成功数量。
        /// </summary>
        public int SuccessCount => DeviceResults.Count(item => item.IsSuccess);

        /// <summary>
        /// 是否全部成功。
        /// </summary>
        public bool IsSuccess => TotalCount > 0 && SuccessCount == TotalCount;
    }

    /// <summary>
    /// 单个设备操作的执行结果。
    /// </summary>
    public sealed class DeviceExecutionActionResult
    {
        /// <summary>
        /// 初始化设备操作结果。
        /// </summary>
        /// <param name="isSuccess">是否执行成功。</param>
        /// <param name="message">结果说明。</param>
        /// <param name="deviceName">设备名称。</param>
        /// <param name="result">附加结果对象。</param>
        public DeviceExecutionActionResult(
            bool isSuccess,
            string message,
            string deviceName = "",
            object? result = null)
        {
            IsSuccess = isSuccess;
            Message = message ?? string.Empty;
            DeviceName = deviceName ?? string.Empty;
            Result = result;
        }

        /// <summary>
        /// 是否执行成功。
        /// </summary>
        public bool IsSuccess { get; }

        /// <summary>
        /// 结果说明。
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// 设备名称。
        /// </summary>
        public string DeviceName { get; }

        /// <summary>
        /// 附加结果对象。
        /// </summary>
        public object? Result { get; }

        /// <summary>
        /// 创建一个设备操作结果实例。
        /// </summary>
        /// <param name="isSuccess">是否执行成功。</param>
        /// <param name="message">结果说明。</param>
        /// <param name="deviceName">设备名称。</param>
        /// <param name="result">附加结果对象。</param>
        /// <returns>设备操作结果。</returns>
        public static DeviceExecutionActionResult Create(
            bool isSuccess,
            string message,
            string deviceName = "",
            object? result = null)
        {
            return new DeviceExecutionActionResult(isSuccess, message, deviceName, result);
        }

        /// <summary>
        /// 创建一个成功的设备操作结果。
        /// </summary>
        /// <param name="message">结果说明。</param>
        /// <param name="deviceName">设备名称。</param>
        /// <param name="result">附加结果对象。</param>
        /// <returns>成功结果。</returns>
        public static DeviceExecutionActionResult CreateSuccess(
            string message,
            string deviceName = "",
            object? result = null)
        {
            return new DeviceExecutionActionResult(true, message, deviceName, result);
        }

        /// <summary>
        /// 创建一个失败的设备操作结果。
        /// </summary>
        /// <param name="message">结果说明。</param>
        /// <param name="deviceName">设备名称。</param>
        /// <param name="result">附加结果对象。</param>
        /// <returns>失败结果。</returns>
        public static DeviceExecutionActionResult CreateFailure(
            string message,
            string deviceName = "",
            object? result = null)
        {
            return new DeviceExecutionActionResult(false, message, deviceName, result);
        }
    }

    /// <summary>
    /// 设备协议消息解析后的缓存数据实体。
    /// </summary>
    public sealed class ParsedDeviceDataEntity
    {
        /// <summary>
        /// 初始化解析结果缓存实体。
        /// </summary>
        /// <param name="deviceName">设备名称。</param>
        /// <param name="key">解析结果键。</param>
        /// <param name="value">解析结果值。</param>
        /// <param name="data">完整原始报文。</param>
        /// <param name="protocolName">协议名称。</param>
        /// <param name="commandName">指令名称。</param>
        /// <param name="parsedAt">解析时间。</param>
        public ParsedDeviceDataEntity(
            string deviceName,
            string key,
            string value,
            string data,
            string protocolName,
            string commandName,
            DateTime parsedAt)
        {
            DeviceName = deviceName;
            Key = key;
            Value = value;
            Data = data;
            ProtocolName = protocolName;
            CommandName = commandName;
            ParsedAt = parsedAt;
        }

        /// <summary>
        /// 设备名称。
        /// </summary>
        public string DeviceName { get; }

        /// <summary>
        /// 解析结果键。
        /// </summary>
        public string Key { get; }

        /// <summary>
        /// 解析结果值。
        /// </summary>
        public string Value { get; }

        /// <summary>
        /// 完整原始报文。
        /// </summary>
        public string Data { get; }

        /// <summary>
        /// 协议名称。
        /// </summary>
        public string ProtocolName { get; }

        /// <summary>
        /// 指令名称。
        /// </summary>
        public string CommandName { get; }

        /// <summary>
        /// 解析时间。
        /// </summary>
        public DateTime ParsedAt { get; }
    }

    #endregion
}
