using ControlLibrary;
using Module.Communication.ViewModels.PropertyVMs;
using NLua;
using Shared.Infrastructure.Extensions;
using Shared.Infrastructure.Lua;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Module.Communication.Models
{
    public enum ProtocolPayloadFormat
    {
        Hex,
        Ascii
    }

    public enum ProtocolCrcMode
    {
        None,
        ModbusCrc16,
        Crc16Ibm,
        Crc16CcittFalse,
        Crc32
    }

    public enum ProtocolExecutionMode
    {
        SendOnly,
        SendAndWaitForResponse,
        ParseOnly
    }

    public enum ProtocolRequestSendMode
    {
        SingleFrame,
        SplitByLine
    }

    public sealed class ProtocolOption<T>
    {
        public ProtocolOption(T value, string displayName, string description)
        {
            Value = value;
            DisplayName = displayName;
            Description = description;
        }

        public T Value { get; }

        public string DisplayName { get; }

        public string Description { get; }
    }

    internal sealed class ProtocolCommandConfigDocument
    {
        public string? Name { get; set; }

        public ProtocolPayloadFormat RequestFormat { get; set; } = ProtocolPayloadFormat.Ascii;

        public ProtocolPayloadFormat ResponseFormat { get; set; } = ProtocolPayloadFormat.Ascii;

        public string? ReplyAggregationMilliseconds { get; set; }

        public bool WaitForResponse { get; set; } = true;

        public bool IsParseOnly { get; set; }

        public ProtocolCrcMode CrcMode { get; set; } = ProtocolCrcMode.None;

        public ProtocolRequestSendMode RequestSendMode { get; set; } = ProtocolRequestSendMode.SingleFrame;

        public string? ContentTemplate { get; set; }

        public string? PlaceholderValuesText { get; set; }

        public string? SampleResponseText { get; set; }

        public string? ParseRulesText { get; set; }

        public List<string>? ParsedResultKeys { get; set; }

        public static ProtocolCommandConfigDocument FromCommand(ProtocolCommandConfig command)
        {
            return new ProtocolCommandConfigDocument
            {
                Name = command.Name,
                RequestFormat = command.RequestFormat,
                ResponseFormat = command.ResponseFormat,
                ReplyAggregationMilliseconds = command.ReplyAggregationMilliseconds,
                WaitForResponse = command.WaitForResponse,
                IsParseOnly = command.IsParseOnly,
                CrcMode = command.CrcMode,
                RequestSendMode = command.RequestSendMode,
                ContentTemplate = command.ContentTemplate,
                PlaceholderValuesText = command.PlaceholderValuesText,
                SampleResponseText = command.SampleResponseText,
                ParseRulesText = command.ParseRulesText,
                ParsedResultKeys = command.ParsedResultKeys.ToList()
            };
        }

        public ProtocolCommandConfig ToCommand()
        {
            ProtocolCommandConfig command = new ProtocolCommandConfig
            {
                Name = string.IsNullOrWhiteSpace(Name) ? "指令 1" : Name.Trim(),
                RequestFormat = RequestFormat,
                ResponseFormat = ResponseFormat,
                ReplyAggregationMilliseconds = string.IsNullOrWhiteSpace(ReplyAggregationMilliseconds)
                    ? "200"
                    : ReplyAggregationMilliseconds.Trim(),
                WaitForResponse = WaitForResponse,
                IsParseOnly = IsParseOnly,
                CrcMode = CrcMode,
                RequestSendMode = RequestSendMode,
                ContentTemplate = ContentTemplate ?? string.Empty,
                PlaceholderValuesText = PlaceholderValuesText ?? string.Empty,
                SampleResponseText = SampleResponseText ?? string.Empty,
                ParseRulesText = ParseRulesText ?? string.Empty
            };

            command.ReplaceParsedResultKeys(ParsedResultKeys ?? Enumerable.Empty<string>());
            return command;
        }
    }

    internal sealed class ProtocolConfigProfileDocument
    {
        public int Version { get; set; } = 2;

        public string? Name { get; set; }

        public List<ProtocolCommandConfigDocument>? Commands { get; set; }

        // Legacy single-command fields, kept so existing JSON can still load.
        public ProtocolPayloadFormat RequestFormat { get; set; } = ProtocolPayloadFormat.Ascii;

        public ProtocolPayloadFormat ResponseFormat { get; set; } = ProtocolPayloadFormat.Ascii;

        public string? ReplyAggregationMilliseconds { get; set; }

        public bool WaitForResponse { get; set; } = true;

        public bool IsParseOnly { get; set; }

        public ProtocolCrcMode CrcMode { get; set; } = ProtocolCrcMode.None;

        public ProtocolRequestSendMode RequestSendMode { get; set; } = ProtocolRequestSendMode.SingleFrame;

        public string? ContentTemplate { get; set; }

        public string? PlaceholderValuesText { get; set; }

        public string? SampleResponseText { get; set; }

        public string? ParseRulesText { get; set; }

        public static ProtocolConfigProfileDocument FromProfile(ProtocolConfigProfile profile)
        {
            ProtocolCommandConfig command = profile.CurrentCommand;
            return new ProtocolConfigProfileDocument
            {
                Name = profile.Name,
                Commands = profile.Commands.Select(ProtocolCommandConfigDocument.FromCommand).ToList(),
                RequestFormat = command.RequestFormat,
                ResponseFormat = command.ResponseFormat,
                ReplyAggregationMilliseconds = command.ReplyAggregationMilliseconds,
                WaitForResponse = command.WaitForResponse,
                IsParseOnly = command.IsParseOnly,
                CrcMode = command.CrcMode,
                RequestSendMode = command.RequestSendMode,
                ContentTemplate = command.ContentTemplate,
                PlaceholderValuesText = command.PlaceholderValuesText,
                SampleResponseText = command.SampleResponseText,
                ParseRulesText = command.ParseRulesText
            };
        }

        public ProtocolConfigProfile ToProfile()
        {
            ProtocolConfigProfile profile = new ProtocolConfigProfile
            {
                Name = string.IsNullOrWhiteSpace(Name) ? "协议 1" : Name.Trim()
            };

            profile.Commands.Clear();
            if (Commands is { Count: > 0 })
            {
                foreach (ProtocolCommandConfigDocument commandDocument in Commands)
                {
                    profile.AddCommand(commandDocument.ToCommand());
                }
            }
            else
            {
                profile.AddCommand(new ProtocolCommandConfig
                {
                    Name = "指令 1",
                    RequestFormat = RequestFormat,
                    ResponseFormat = ResponseFormat,
                    ReplyAggregationMilliseconds = string.IsNullOrWhiteSpace(ReplyAggregationMilliseconds)
                        ? "200"
                        : ReplyAggregationMilliseconds.Trim(),
                    WaitForResponse = WaitForResponse,
                    IsParseOnly = IsParseOnly,
                    CrcMode = CrcMode,
                    RequestSendMode = RequestSendMode,
                    ContentTemplate = ContentTemplate ?? string.Empty,
                    PlaceholderValuesText = PlaceholderValuesText ?? string.Empty,
                    SampleResponseText = SampleResponseText ?? string.Empty,
                    ParseRulesText = ParseRulesText ?? string.Empty
                });
            }

            profile.SelectedCommand = null;
            return profile;
        }
    }

    internal static class ProtocolDisplayNames
    {
        public static string GetPayloadFormatDisplayName(ProtocolPayloadFormat format)
        {
            return format switch
            {
                ProtocolPayloadFormat.Hex => "Hex",
                ProtocolPayloadFormat.Ascii => "ASCII",
                _ => format.ToString()
            };
        }

        public static string GetCrcDisplayName(ProtocolCrcMode crcMode)
        {
            return crcMode switch
            {
                ProtocolCrcMode.None => "无校验",
                ProtocolCrcMode.ModbusCrc16 => "Modbus CRC16",
                ProtocolCrcMode.Crc16Ibm => "CRC16-IBM",
                ProtocolCrcMode.Crc16CcittFalse => "CRC16-CCITT-FALSE",
                ProtocolCrcMode.Crc32 => "CRC32",
                _ => crcMode.ToString()
            };
        }

        public static string GetRequestSendModeDisplayName(ProtocolRequestSendMode sendMode)
        {
            return sendMode switch
            {
                ProtocolRequestSendMode.SingleFrame => "一条报文发送",
                ProtocolRequestSendMode.SplitByLine => "换行分开发送",
                _ => sendMode.ToString()
            };
        }
    }

    public sealed class ProtocolRequestFramePreview
    {
        public ProtocolRequestFramePreview(string renderedTemplate, string requestHex, string requestAscii)
        {
            RenderedTemplate = renderedTemplate;
            RequestHex = requestHex;
            RequestAscii = requestAscii;
        }

        public string RenderedTemplate { get; }

        public string RequestHex { get; }

        public string RequestAscii { get; }
    }

    public sealed class ProtocolRequestPreviewResult
    {
        public ProtocolRequestPreviewResult(
            string renderedTemplate,
            string requestHex,
            string requestAscii,
            IReadOnlyList<ProtocolRequestFramePreview>? frames = null)
        {
            RenderedTemplate = renderedTemplate;
            RequestHex = requestHex;
            RequestAscii = requestAscii;
            Frames = frames ?? CreateDefaultFrames(renderedTemplate, requestHex, requestAscii);
        }

        public string RenderedTemplate { get; }

        public string RequestHex { get; }

        public string RequestAscii { get; }

        public IReadOnlyList<ProtocolRequestFramePreview> Frames { get; }

        private static IReadOnlyList<ProtocolRequestFramePreview> CreateDefaultFrames(
            string renderedTemplate,
            string requestHex,
            string requestAscii)
        {
            return string.IsNullOrEmpty(renderedTemplate) &&
                   string.IsNullOrEmpty(requestHex) &&
                   string.IsNullOrEmpty(requestAscii)
                ? Array.Empty<ProtocolRequestFramePreview>()
                : new[] { new ProtocolRequestFramePreview(renderedTemplate, requestHex, requestAscii) };
        }
    }

    public sealed class ProtocolResponsePreviewResult
    {
        public ProtocolResponsePreviewResult(
            string responseHex,
            string responseAscii,
            string parsedJson,
            IEnumerable<string>? parsedKeys = null)
        {
            ResponseHex = responseHex;
            ResponseAscii = responseAscii;
            ParsedJson = parsedJson;
            ParsedKeys = (parsedKeys ?? Enumerable.Empty<string>())
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Select(key => key.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public string ResponseHex { get; }

        public string ResponseAscii { get; }

        public string ParsedJson { get; }

        public IReadOnlyList<string> ParsedKeys { get; }
    }

    public static class ProtocolPreviewEngine
    {
        private static readonly Regex PlaceholderRegex =
            new Regex(@"\{\{\s*(?<name>[^{}\r\n]+?)\s*\}\}", RegexOptions.Compiled);

        private static readonly Regex FunctionRegex =
            new Regex(@"^(?<name>[A-Za-z_][A-Za-z0-9_]*)\((?<args>.*)\)$", RegexOptions.Compiled);

        private static readonly JsonSerializerOptions ParsedJsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        public static bool TryBuildRequestPreview(
            ProtocolConfigProfile profile,
            out ProtocolRequestPreviewResult? result,
            out string message)
        {
            return TryBuildRequestPreview(profile.CurrentCommand, out result, out message);
        }

        public static bool TryBuildRequestPreview(
            ProtocolCommandConfig command,
            out ProtocolRequestPreviewResult? result,
            out string message)
        {
            result = null;
            if (command.IsParseOnly)
            {
                result = new ProtocolRequestPreviewResult(string.Empty, string.Empty, string.Empty);
                message = "当前指令为仅解析模式，不生成发送帧。";
                return true;
            }

            if (string.IsNullOrWhiteSpace(command.ContentTemplate))
            {
                message = "协议内容不能为空。";
                return false;
            }

            if (!TryParsePlaceholderValues(command.PlaceholderValuesText, out Dictionary<string, string> placeholderValues, out message))
            {
                return false;
            }

            if (!TryRenderTemplate(command.ContentTemplate, placeholderValues, out string renderedTemplate, out message))
            {
                return false;
            }

            if (!TryBuildFrameBytes(renderedTemplate, command.RequestFormat, command.CrcMode, out byte[] frameBytes, out message))
            {
                return false;
            }

            result = new ProtocolRequestPreviewResult(
                renderedTemplate,
                frameBytes.ByteArrayToHexString(),
                FormatBytesAsAscii(frameBytes));
            message = "发送帧预览已生成。";
            return true;
        }

        public static bool TryBuildResponsePreview(
            ProtocolConfigProfile profile,
            out ProtocolResponsePreviewResult? result,
            out string message)
        {
            return TryBuildResponsePreview(profile.CurrentCommand, out result, out message);
        }

        public static bool TryBuildResponsePreview(
            ProtocolCommandConfig command,
            out ProtocolResponsePreviewResult? result,
            out string message)
        {
            result = null;
            if (string.IsNullOrWhiteSpace(command.SampleResponseText))
            {
                result = new ProtocolResponsePreviewResult(string.Empty, string.Empty, "{ }");
                message = "未填写示例返回数据，暂不执行解析预览。";
                return true;
            }

            if (!TryConvertContentToBytes(
                    command.SampleResponseText,
                    command.ResponseFormat,
                    out byte[] responseBytes,
                    out string normalizedResponse,
                    out message))
            {
                return false;
            }

            object? parsedValue;
            if (string.IsNullOrWhiteSpace(command.ParseRulesText))
            {
                parsedValue = CreateDefaultParsedValues(responseBytes, normalizedResponse, command.ResponseFormat);
                message = "已生成返回预览，未填写 Lua 解析脚本。";
            }
            else
            {
                if (!TryApplyLuaParseRules(
                        command.ParseRulesText,
                        normalizedResponse,
                        out parsedValue,
                        out message))
                {
                    return false;
                }

                message = "返回数据 Lua 解析预览已生成。";
            }

            result = new ProtocolResponsePreviewResult(
                responseBytes.ByteArrayToHexString(),
                FormatBytesAsAscii(responseBytes),
                FormatParsedValue(parsedValue),
                ExtractParsedResultKeys(parsedValue));
            return true;
        }

        public static bool TryRefreshParsedResultKeys(ProtocolConfigProfile profile, out string message)
        {
            foreach (ProtocolCommandConfig command in profile.Commands)
            {
                if (!TryBuildResponsePreview(command, out ProtocolResponsePreviewResult? previewResult, out message) ||
                    previewResult is null)
                {
                    string protocolName = string.IsNullOrWhiteSpace(profile.Name) ? "未命名协议" : profile.Name.Trim();
                    string commandName = string.IsNullOrWhiteSpace(command.Name) ? "未命名指令" : command.Name.Trim();
                    message = $"协议 {protocolName} 的指令 {commandName} 解析失败：{message}";
                    return false;
                }

                command.ReplaceParsedResultKeys(previewResult.ParsedKeys);
            }

            message = string.Empty;
            return true;
        }

        private static bool TryParsePlaceholderValues(
            string placeholderValuesText,
            out Dictionary<string, string> values,
            out string message)
        {
            values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string[] lines = SplitLines(placeholderValuesText);
            for (int index = 0; index < lines.Length; index++)
            {
                string line = lines[index].Trim();
                if (string.IsNullOrWhiteSpace(line) ||
                    line.StartsWith("#", StringComparison.Ordinal) ||
                    line.StartsWith("//", StringComparison.Ordinal))
                {
                    continue;
                }

                int equalsIndex = line.IndexOf('=');
                if (equalsIndex <= 0)
                {
                    message = $"占位符值第 {index + 1} 行格式错误，请使用 Key=Value。";
                    return false;
                }

                string key = line[..equalsIndex].Trim();
                string value = line[(equalsIndex + 1)..].Trim();
                if (string.IsNullOrWhiteSpace(key))
                {
                    message = $"占位符值第 {index + 1} 行缺少键名。";
                    return false;
                }

                values[key] = value;
            }

            message = string.Empty;
            return true;
        }

        private static bool TryRenderTemplate(
            string contentTemplate,
            IReadOnlyDictionary<string, string> placeholderValues,
            out string renderedTemplate,
            out string message)
        {
            List<string> missingPlaceholders = new List<string>();
            renderedTemplate = PlaceholderRegex.Replace(contentTemplate, match =>
            {
                string placeholderName = match.Groups["name"].Value.Trim();
                if (placeholderValues.TryGetValue(placeholderName, out string? value))
                {
                    return value;
                }

                missingPlaceholders.Add(placeholderName);
                return match.Value;
            });

            if (missingPlaceholders.Count > 0)
            {
                message = $"占位符缺少值：{string.Join("、", missingPlaceholders.Distinct(StringComparer.OrdinalIgnoreCase))}。";
                return false;
            }

            message = string.Empty;
            return true;
        }

        private static bool TryBuildFrameBytes(
            string renderedTemplate,
            ProtocolPayloadFormat format,
            ProtocolCrcMode crcMode,
            out byte[] frameBytes,
            out string message)
        {
            if (!TryConvertContentToBytes(renderedTemplate, format, out byte[] payloadBytes, out _, out message))
            {
                frameBytes = Array.Empty<byte>();
                return false;
            }

            byte[] crcBytes = BuildChecksum(payloadBytes, crcMode);
            frameBytes = crcBytes.Length == 0
                ? payloadBytes
                : ArrayExtension.ConcatBytes(payloadBytes, crcBytes);
            message = string.Empty;
            return true;
        }

        private static bool TryConvertContentToBytes(
            string content,
            ProtocolPayloadFormat format,
            out byte[] bytes,
            out string normalizedText,
            out string message)
        {
            normalizedText = content;
            switch (format)
            {
                case ProtocolPayloadFormat.Hex:
                    string normalizedHex = NormalizeHexString(content);
                    if (string.IsNullOrWhiteSpace(normalizedHex))
                    {
                        bytes = Array.Empty<byte>();
                        message = "Hex 内容不能为空。";
                        return false;
                    }

                    if (!Regex.IsMatch(normalizedHex, @"\A[0-9A-Fa-f]+\z"))
                    {
                        bytes = Array.Empty<byte>();
                        message = "Hex 内容只能包含 0-9、A-F 以及常见分隔符。";
                        return false;
                    }

                    if (normalizedHex.Length % 2 != 0)
                    {
                        bytes = Array.Empty<byte>();
                        message = "Hex 内容长度必须为偶数。";
                        return false;
                    }

                    bytes = normalizedHex.HexStringToByteArray();
                    normalizedText = normalizedHex;
                    message = string.Empty;
                    return true;

                case ProtocolPayloadFormat.Ascii:
                    bytes = Encoding.ASCII.GetBytes(content);
                    message = string.Empty;
                    return true;

                default:
                    bytes = Array.Empty<byte>();
                    message = "暂不支持当前数据格式。";
                    return false;
            }
        }

        public static bool TryBuildLuaParseExecutionPrefix(
            ProtocolCommandConfig command,
            out string prefixScript,
            out string message)
        {
            prefixScript = string.Empty;
            if (string.IsNullOrWhiteSpace(command.SampleResponseText))
            {
                message = "请先填写示例返回数据。";
                return false;
            }

            if (!TryConvertContentToBytes(
                    command.SampleResponseText,
                    command.ResponseFormat,
                    out byte[] responseBytes,
                    out string normalizedResponse,
                    out message))
            {
                return false;
            }

            prefixScript = BuildLuaParsePrelude(normalizedResponse);
            message = string.Empty;
            return true;
        }

        private static bool TryApplyLuaParseRules(
            string parseRulesText,
            string data,
            out object? parsedValue,
            out string message)
        {
            parsedValue = null;
            string script = $"{BuildLuaParsePrelude(data)}{Environment.NewLine}{parseRulesText}";
            LuaManage lua = new();
            try
            {
                object[] results = lua.DoString(script);
                if (results.Length == 1 && results[0] is Exception exception)
                {
                    message = exception.Message;
                    return false;
                }

                if (results.Length == 0)
                {
                    parsedValue = null;
                    message = string.Empty;
                    return true;
                }

                parsedValue = ConvertLuaValue(results[0]);
                message = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                message = $"解析 Lua 脚本执行失败：{ex.Message}";
                return false;
            }
        }

        private static string BuildLuaParsePrelude(string data)
        {
            StringBuilder script = new();
            script.Append("data = ").Append(ToLuaStringLiteral(data)).AppendLine();
            return script.ToString();
        }

        private static object? ConvertLuaValue(object? value)
        {
            if (value is null)
            {
                return null;
            }

            if (value is LuaTable table)
            {
                Dictionary<string, object?> nested = new(StringComparer.OrdinalIgnoreCase);
                foreach (object key in table.Keys)
                {
                    string? fieldName = Convert.ToString(key);
                    if (!string.IsNullOrWhiteSpace(fieldName))
                    {
                        nested[fieldName] = ConvertLuaValue(table[key]);
                    }
                }

                return nested;
            }

            if (value is double doubleValue && Math.Abs(doubleValue - Math.Round(doubleValue)) < double.Epsilon)
            {
                return Convert.ToInt64(doubleValue);
            }

            return value;
        }

        private static string FormatParsedValue(object? value)
        {
            if (value is null)
            {
                return string.Empty;
            }

            if (value is IReadOnlyDictionary<string, object?> readOnlyDictionary)
            {
                return JsonSerializer.Serialize(readOnlyDictionary, ParsedJsonOptions);
            }

            if (value is IDictionary<string, object?> dictionary)
            {
                return JsonSerializer.Serialize(dictionary, ParsedJsonOptions);
            }

            Dictionary<string, object?> wrappedValue = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Data"] = value
            };
            return JsonSerializer.Serialize(wrappedValue, ParsedJsonOptions);
        }

        private static IEnumerable<string> ExtractParsedResultKeys(object? value)
        {
            if (value is null)
            {
                return Enumerable.Empty<string>();
            }

            if (value is IReadOnlyDictionary<string, object?> readOnlyDictionary)
            {
                return readOnlyDictionary.Keys;
            }

            if (value is IDictionary<string, object?> dictionary)
            {
                return dictionary.Keys;
            }

            return new[] { "Data" };
        }

        private static string ToLuaStringLiteral(string value)
        {
            StringBuilder builder = new(value.Length + 2);
            builder.Append('"');
            foreach (char character in value)
            {
                builder.Append(character switch
                {
                    '\\' => "\\\\",
                    '"' => "\\\"",
                    '\r' => "\\r",
                    '\n' => "\\n",
                    '\t' => "\\t",
                    _ => character.ToString()
                });
            }

            builder.Append('"');
            return builder.ToString();
        }

        private static bool TryApplyParseRules(
            string parseRulesText,
            byte[] responseBytes,
            string originalResponseText,
            ProtocolPayloadFormat responseFormat,
            out Dictionary<string, object?> parsedValues,
            out string message)
        {
            parsedValues = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            string[] lines = SplitLines(parseRulesText);
            for (int index = 0; index < lines.Length; index++)
            {
                string line = lines[index].Trim();
                if (string.IsNullOrWhiteSpace(line) ||
                    line.StartsWith("#", StringComparison.Ordinal) ||
                    line.StartsWith("//", StringComparison.Ordinal))
                {
                    continue;
                }

                int equalsIndex = line.IndexOf('=');
                if (equalsIndex <= 0)
                {
                    message = $"解析规则第 {index + 1} 行格式错误，请使用 Field=Expression。";
                    return false;
                }

                string fieldName = line[..equalsIndex].Trim();
                string expression = line[(equalsIndex + 1)..].Trim();
                if (string.IsNullOrWhiteSpace(fieldName))
                {
                    message = $"解析规则第 {index + 1} 行缺少字段名。";
                    return false;
                }

                if (!TryEvaluateExpression(
                        expression,
                        responseBytes,
                        originalResponseText,
                        responseFormat,
                        out string value,
                        out string expressionError))
                {
                    message = $"解析规则第 {index + 1} 行错误：{expressionError}";
                    return false;
                }

                parsedValues[fieldName] = value;
            }

            if (parsedValues.Count == 0)
            {
                parsedValues = CreateDefaultParsedValues(responseBytes, originalResponseText, responseFormat);
            }

            message = string.Empty;
            return true;
        }

        private static bool TryEvaluateExpression(
            string expression,
            byte[] responseBytes,
            string originalResponseText,
            ProtocolPayloadFormat responseFormat,
            out string value,
            out string message)
        {
            value = string.Empty;
            string normalizedExpression = expression.Trim();
            switch (normalizedExpression.ToLowerInvariant())
            {
                case "hex":
                    value = responseBytes.ByteArrayToHexString();
                    message = string.Empty;
                    return true;
                case "ascii":
                    value = FormatBytesAsAscii(responseBytes);
                    message = string.Empty;
                    return true;
                case "utf8":
                    value = Encoding.UTF8.GetString(responseBytes);
                    message = string.Empty;
                    return true;
                case "text":
                    value = responseFormat == ProtocolPayloadFormat.Ascii
                        ? originalResponseText
                        : responseBytes.ByteArrayToHexString();
                    message = string.Empty;
                    return true;
                case "len":
                    value = responseBytes.Length.ToString();
                    message = string.Empty;
                    return true;
            }

            Match functionMatch = FunctionRegex.Match(normalizedExpression);
            if (!functionMatch.Success)
            {
                message = $"不支持的表达式：{expression}";
                return false;
            }

            string functionName = functionMatch.Groups["name"].Value.ToLowerInvariant();
            string[] arguments = functionMatch.Groups["args"].Value
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

            switch (functionName)
            {
                case "hex":
                case "ascii":
                case "utf8":
                    if (!TryResolveSlice(responseBytes, arguments, out byte[] sliceBytes, out message))
                    {
                        return false;
                    }

                    value = functionName switch
                    {
                        "hex" => sliceBytes.ByteArrayToHexString(),
                        "ascii" => FormatBytesAsAscii(sliceBytes),
                        _ => Encoding.UTF8.GetString(sliceBytes)
                    };
                    message = string.Empty;
                    return true;

                case "u8":
                    if (!TryResolveIndex(responseBytes, arguments, 1, out int index, out message))
                    {
                        return false;
                    }

                    value = responseBytes[index].ToString();
                    message = string.Empty;
                    return true;

                case "u16le":
                case "u16be":
                    if (!TryResolveIndex(responseBytes, arguments, 2, out index, out message))
                    {
                        return false;
                    }

                    value = (functionName == "u16le"
                            ? BitConverter.ToUInt16(responseBytes, index)
                            : (ushort)((responseBytes[index] << 8) | responseBytes[index + 1]))
                        .ToString();
                    message = string.Empty;
                    return true;

                case "u32le":
                case "u32be":
                    if (!TryResolveIndex(responseBytes, arguments, 4, out index, out message))
                    {
                        return false;
                    }

                    value = (functionName == "u32le"
                            ? BitConverter.ToUInt32(responseBytes, index)
                            : ((uint)responseBytes[index] << 24) |
                              ((uint)responseBytes[index + 1] << 16) |
                              ((uint)responseBytes[index + 2] << 8) |
                              responseBytes[index + 3])
                        .ToString();
                    message = string.Empty;
                    return true;

                default:
                    message = $"不支持的函数：{functionName}";
                    return false;
            }
        }

        private static bool TryResolveSlice(
            byte[] source,
            IReadOnlyList<string> arguments,
            out byte[] sliceBytes,
            out string message)
        {
            sliceBytes = Array.Empty<byte>();
            if (arguments.Count != 2)
            {
                message = "切片函数需要两个参数：start,length。";
                return false;
            }

            if (!int.TryParse(arguments[0], out int start) || start < 0)
            {
                message = "切片起始位置必须是大于等于 0 的数字。";
                return false;
            }

            if (!int.TryParse(arguments[1], out int length))
            {
                message = "切片长度必须是数字，或使用 -1 表示截取到结尾。";
                return false;
            }

            if (start >= source.Length)
            {
                message = "切片起始位置超出返回数据长度。";
                return false;
            }

            if (length == -1)
            {
                length = source.Length - start;
            }

            if (length < 0 || start + length > source.Length)
            {
                message = "切片长度超出返回数据范围。";
                return false;
            }

            sliceBytes = source.Skip(start).Take(length).ToArray();
            message = string.Empty;
            return true;
        }

        private static bool TryResolveIndex(
            byte[] source,
            IReadOnlyList<string> arguments,
            int width,
            out int index,
            out string message)
        {
            index = 0;
            if (arguments.Count != 1)
            {
                message = "索引函数只接受一个起始字节位置参数。";
                return false;
            }

            if (!int.TryParse(arguments[0], out index) || index < 0)
            {
                message = "索引位置必须是大于等于 0 的数字。";
                return false;
            }

            if (index + width > source.Length)
            {
                message = "索引位置超出返回数据范围。";
                return false;
            }

            message = string.Empty;
            return true;
        }

        private static Dictionary<string, object?> CreateDefaultParsedValues(
            byte[] responseBytes,
            string originalResponseText,
            ProtocolPayloadFormat responseFormat)
        {
            return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["Length"] = responseBytes.Length,
                ["FullHex"] = responseBytes.ByteArrayToHexString(),
                ["FullAscii"] = FormatBytesAsAscii(responseBytes),
                ["Text"] = responseFormat == ProtocolPayloadFormat.Ascii
                    ? originalResponseText
                    : responseBytes.ByteArrayToHexString()
            };
        }

        private static byte[] BuildChecksum(byte[] payloadBytes, ProtocolCrcMode crcMode)
        {
            return crcMode switch
            {
                ProtocolCrcMode.None => Array.Empty<byte>(),
                ProtocolCrcMode.ModbusCrc16 => ComputeReflectedCrc16(payloadBytes, 0xFFFF),
                ProtocolCrcMode.Crc16Ibm => ComputeReflectedCrc16(payloadBytes, 0x0000),
                ProtocolCrcMode.Crc16CcittFalse => ComputeCrc16CcittFalse(payloadBytes),
                ProtocolCrcMode.Crc32 => ComputeCrc32LittleEndian(payloadBytes),
                _ => Array.Empty<byte>()
            };
        }

        private static byte[] ComputeReflectedCrc16(byte[] data, ushort seed)
        {
            ushort crc = seed;
            foreach (byte value in data)
            {
                crc ^= value;
                for (int bit = 0; bit < 8; bit++)
                {
                    crc = (crc & 0x0001) != 0
                        ? (ushort)((crc >> 1) ^ 0xA001)
                        : (ushort)(crc >> 1);
                }
            }

            return new[]
            {
                (byte)(crc & 0xFF),
                (byte)((crc >> 8) & 0xFF)
            };
        }

        private static byte[] ComputeCrc16CcittFalse(byte[] data)
        {
            ushort crc = 0xFFFF;
            foreach (byte value in data)
            {
                crc ^= (ushort)(value << 8);
                for (int bit = 0; bit < 8; bit++)
                {
                    crc = (crc & 0x8000) != 0
                        ? (ushort)((crc << 1) ^ 0x1021)
                        : (ushort)(crc << 1);
                }
            }

            return new[]
            {
                (byte)((crc >> 8) & 0xFF),
                (byte)(crc & 0xFF)
            };
        }

        private static byte[] ComputeCrc32LittleEndian(byte[] data)
        {
            uint crc = 0xFFFFFFFF;
            foreach (byte value in data)
            {
                crc ^= value;
                for (int bit = 0; bit < 8; bit++)
                {
                    crc = (crc & 0x00000001) != 0
                        ? (crc >> 1) ^ 0xEDB88320
                        : crc >> 1;
                }
            }

            crc ^= 0xFFFFFFFF;
            byte[] bytes = BitConverter.GetBytes(crc);
            return BitConverter.IsLittleEndian ? bytes : bytes.Reverse().ToArray();
        }

        private static string NormalizeHexString(string value)
        {
            string normalized = value.Replace("0x", string.Empty, StringComparison.OrdinalIgnoreCase);
            normalized = normalized.Replace(" ", string.Empty, StringComparison.OrdinalIgnoreCase);
            normalized = normalized.Replace("-", string.Empty, StringComparison.OrdinalIgnoreCase);
            normalized = normalized.Replace(",", string.Empty, StringComparison.OrdinalIgnoreCase);
            normalized = normalized.Replace("_", string.Empty, StringComparison.OrdinalIgnoreCase);
            normalized = normalized.Replace("\r", string.Empty, StringComparison.OrdinalIgnoreCase);
            normalized = normalized.Replace("\n", string.Empty, StringComparison.OrdinalIgnoreCase);
            normalized = normalized.Replace("\t", string.Empty, StringComparison.OrdinalIgnoreCase);
            return normalized.Trim();
        }

        private static string FormatBytesAsAscii(byte[] bytes)
        {
            StringBuilder builder = new StringBuilder(bytes.Length);
            foreach (byte value in bytes)
            {
                builder.Append(value is >= 32 and <= 126
                    ? ((char)value).ToString()
                    : $"\\x{value:X2}");
            }

            return builder.ToString();
        }

        private static string[] SplitLines(string value)
        {
            return value.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        }
    }
}
