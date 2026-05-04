using Shared.Infrastructure.Extensions;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ControlLibrary.ControlViews.Protocol.Models
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

        public ProtocolPayloadFormat RequestFormat { get; set; } = ProtocolPayloadFormat.Hex;

        public ProtocolPayloadFormat ResponseFormat { get; set; } = ProtocolPayloadFormat.Hex;

        public string? ReplyAggregationMilliseconds { get; set; }

        public bool WaitForResponse { get; set; } = true;

        public ProtocolCrcMode CrcMode { get; set; } = ProtocolCrcMode.None;

        public string? ContentTemplate { get; set; }

        public string? PlaceholderValuesText { get; set; }

        public string? SampleResponseText { get; set; }

        public string? ParseRulesText { get; set; }

        public static ProtocolCommandConfigDocument FromCommand(ProtocolCommandConfig command)
        {
            return new ProtocolCommandConfigDocument
            {
                Name = command.Name,
                RequestFormat = command.RequestFormat,
                ResponseFormat = command.ResponseFormat,
                ReplyAggregationMilliseconds = command.ReplyAggregationMilliseconds,
                WaitForResponse = command.WaitForResponse,
                CrcMode = command.CrcMode,
                ContentTemplate = command.ContentTemplate,
                PlaceholderValuesText = command.PlaceholderValuesText,
                SampleResponseText = command.SampleResponseText,
                ParseRulesText = command.ParseRulesText
            };
        }

        public ProtocolCommandConfig ToCommand()
        {
            return new ProtocolCommandConfig
            {
                Name = string.IsNullOrWhiteSpace(Name) ? "指令 1" : Name.Trim(),
                RequestFormat = RequestFormat,
                ResponseFormat = ResponseFormat,
                ReplyAggregationMilliseconds = string.IsNullOrWhiteSpace(ReplyAggregationMilliseconds)
                    ? "200"
                    : ReplyAggregationMilliseconds.Trim(),
                WaitForResponse = WaitForResponse,
                CrcMode = CrcMode,
                ContentTemplate = ContentTemplate ?? string.Empty,
                PlaceholderValuesText = PlaceholderValuesText ?? string.Empty,
                SampleResponseText = SampleResponseText ?? string.Empty,
                ParseRulesText = ParseRulesText ?? string.Empty
            };
        }
    }

    internal sealed class ProtocolConfigProfileDocument
    {
        public int Version { get; set; } = 2;

        public string? Name { get; set; }

        public List<ProtocolCommandConfigDocument>? Commands { get; set; }

        // Legacy single-command fields, kept so existing JSON can still load.
        public ProtocolPayloadFormat RequestFormat { get; set; } = ProtocolPayloadFormat.Hex;

        public ProtocolPayloadFormat ResponseFormat { get; set; } = ProtocolPayloadFormat.Hex;

        public string? ReplyAggregationMilliseconds { get; set; }

        public bool WaitForResponse { get; set; } = true;

        public ProtocolCrcMode CrcMode { get; set; } = ProtocolCrcMode.None;

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
                CrcMode = command.CrcMode,
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
                    CrcMode = CrcMode,
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

    public sealed class ProtocolPlaceholderValue : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        private string _value = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Name
        {
            get => _name;
            set => SetField(ref _name, value?.Trim() ?? string.Empty);
        }

        public string Value
        {
            get => _value;
            set => SetField(ref _value, value ?? string.Empty);
        }

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value))
            {
                return false;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }
    }

    public sealed class ProtocolCommandConfig : INotifyPropertyChanged
    {
        private static readonly Regex PlaceholderRegex =
            new Regex(@"\{\{\s*(?<name>[^{}\r\n]+?)\s*\}\}", RegexOptions.Compiled);

        private string _name = "指令 1";
        private ProtocolPayloadFormat _requestFormat = ProtocolPayloadFormat.Hex;
        private ProtocolPayloadFormat _responseFormat = ProtocolPayloadFormat.Hex;
        private string _replyAggregationMilliseconds = "200";
        private bool _waitForResponse = true;
        private ProtocolCrcMode _crcMode = ProtocolCrcMode.None;
        private string _contentTemplate = "AA {{Address}} {{Command}}";
        private string _placeholderValuesText = "Address=01\r\nCommand=03";
        private string _sampleResponseText = "AA 01 03";
        private string _parseRulesText = "FullHex = hex\r\nLength = len";
        private bool _isSyncingPlaceholders;

        public ProtocolCommandConfig()
        {
            PlaceholderValues.CollectionChanged += PlaceholderValues_CollectionChanged;
            SyncPlaceholderValuesFromTemplate(preferTextValues: true);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public ObservableCollection<ProtocolPlaceholderValue> PlaceholderValues { get; } = new ObservableCollection<ProtocolPlaceholderValue>();

        public string Name
        {
            get => _name;
            set => SetField(ref _name, value);
        }

        public ProtocolPayloadFormat RequestFormat
        {
            get => _requestFormat;
            set => SetField(ref _requestFormat, value);
        }

        public ProtocolPayloadFormat ResponseFormat
        {
            get => _responseFormat;
            set => SetField(ref _responseFormat, value);
        }

        public string ReplyAggregationMilliseconds
        {
            get => _replyAggregationMilliseconds;
            set => SetField(ref _replyAggregationMilliseconds, value);
        }

        public bool WaitForResponse
        {
            get => _waitForResponse;
            set => SetField(ref _waitForResponse, value);
        }

        public ProtocolCrcMode CrcMode
        {
            get => _crcMode;
            set => SetField(ref _crcMode, value);
        }

        public string ContentTemplate
        {
            get => _contentTemplate;
            set
            {
                if (SetField(ref _contentTemplate, value ?? string.Empty))
                {
                    SyncPlaceholderValuesFromTemplate(preferTextValues: false);
                }
            }
        }

        public string PlaceholderValuesText
        {
            get => _placeholderValuesText;
            set
            {
                if (SetField(ref _placeholderValuesText, value ?? string.Empty))
                {
                    SyncPlaceholderValuesFromTemplate(preferTextValues: true);
                }
            }
        }

        public string SampleResponseText
        {
            get => _sampleResponseText;
            set => SetField(ref _sampleResponseText, value);
        }

        public string ParseRulesText
        {
            get => _parseRulesText;
            set => SetField(ref _parseRulesText, value);
        }

        public string RequestFormatDisplayName => ProtocolDisplayNames.GetPayloadFormatDisplayName(RequestFormat);

        public string ResponseFormatDisplayName => ProtocolDisplayNames.GetPayloadFormatDisplayName(ResponseFormat);

        public string CrcDisplayName => ProtocolDisplayNames.GetCrcDisplayName(CrcMode);

        public string Summary =>
            $"{RequestFormatDisplayName} -> {ResponseFormatDisplayName} / {CrcDisplayName} / 拼接等待 {ReplyAggregationMilliseconds} ms";

        public ProtocolCommandConfig Clone(string name)
        {
            return new ProtocolCommandConfig
            {
                Name = name,
                RequestFormat = RequestFormat,
                ResponseFormat = ResponseFormat,
                ReplyAggregationMilliseconds = ReplyAggregationMilliseconds,
                WaitForResponse = WaitForResponse,
                CrcMode = CrcMode,
                ContentTemplate = ContentTemplate,
                PlaceholderValuesText = PlaceholderValuesText,
                SampleResponseText = SampleResponseText,
                ParseRulesText = ParseRulesText
            };
        }

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value))
            {
                return false;
            }

            field = value;
            OnPropertyChanged(propertyName);
            RaiseStateChanged();
            return true;
        }

        private void SyncPlaceholderValuesFromTemplate(bool preferTextValues)
        {
            if (_isSyncingPlaceholders)
            {
                return;
            }

            _isSyncingPlaceholders = true;
            try
            {
                Dictionary<string, string> valuesByName = ParsePlaceholderValuesText(_placeholderValuesText);
                Dictionary<string, ProtocolPlaceholderValue> existingByName = PlaceholderValues
                    .Where(item => !string.IsNullOrWhiteSpace(item.Name))
                    .GroupBy(item => item.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

                if (!preferTextValues)
                {
                    foreach (ProtocolPlaceholderValue item in PlaceholderValues)
                    {
                        if (!string.IsNullOrWhiteSpace(item.Name))
                        {
                            valuesByName[item.Name.Trim()] = item.Value;
                        }
                    }
                }

                List<string> placeholderNames = ExtractPlaceholderNames(_contentTemplate).ToList();
                PlaceholderValues.CollectionChanged -= PlaceholderValues_CollectionChanged;
                foreach (ProtocolPlaceholderValue item in PlaceholderValues)
                {
                    item.PropertyChanged -= PlaceholderValue_PropertyChanged;
                }

                PlaceholderValues.Clear();
                foreach (string placeholderName in placeholderNames)
                {
                    ProtocolPlaceholderValue item = existingByName.TryGetValue(placeholderName, out ProtocolPlaceholderValue? existing)
                        ? existing
                        : new ProtocolPlaceholderValue();

                    item.PropertyChanged -= PlaceholderValue_PropertyChanged;
                    item.Name = placeholderName;
                    if (valuesByName.TryGetValue(placeholderName, out string? value))
                    {
                        item.Value = value;
                    }

                    item.PropertyChanged += PlaceholderValue_PropertyChanged;
                    PlaceholderValues.Add(item);
                }

                PlaceholderValues.CollectionChanged += PlaceholderValues_CollectionChanged;
                UpdatePlaceholderValuesTextFromCollection();
            }
            finally
            {
                _isSyncingPlaceholders = false;
            }

            OnPropertyChanged(nameof(PlaceholderValues));
        }

        private void PlaceholderValues_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems is not null)
            {
                foreach (ProtocolPlaceholderValue item in e.NewItems.OfType<ProtocolPlaceholderValue>())
                {
                    item.PropertyChanged += PlaceholderValue_PropertyChanged;
                }
            }

            if (e.OldItems is not null)
            {
                foreach (ProtocolPlaceholderValue item in e.OldItems.OfType<ProtocolPlaceholderValue>())
                {
                    item.PropertyChanged -= PlaceholderValue_PropertyChanged;
                }
            }

            if (!_isSyncingPlaceholders)
            {
                UpdatePlaceholderValuesTextFromCollection();
            }
        }

        private void PlaceholderValue_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (!_isSyncingPlaceholders &&
                e.PropertyName is nameof(ProtocolPlaceholderValue.Name) or nameof(ProtocolPlaceholderValue.Value))
            {
                UpdatePlaceholderValuesTextFromCollection();
            }
        }

        private void UpdatePlaceholderValuesTextFromCollection()
        {
            string text = string.Join(
                Environment.NewLine,
                PlaceholderValues
                    .Where(item => !string.IsNullOrWhiteSpace(item.Name))
                    .Select(item => $"{item.Name.Trim()}={item.Value}"));

            if (string.Equals(_placeholderValuesText, text, StringComparison.Ordinal))
            {
                return;
            }

            _placeholderValuesText = text;
            OnPropertyChanged(nameof(PlaceholderValuesText));
            RaiseStateChanged();
        }

        private static IEnumerable<string> ExtractPlaceholderNames(string contentTemplate)
        {
            HashSet<string> seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match match in PlaceholderRegex.Matches(contentTemplate ?? string.Empty))
            {
                string placeholderName = match.Groups["name"].Value.Trim();
                if (!string.IsNullOrWhiteSpace(placeholderName) && seenNames.Add(placeholderName))
                {
                    yield return placeholderName;
                }
            }
        }

        private static Dictionary<string, string> ParsePlaceholderValuesText(string placeholderValuesText)
        {
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string[] lines = (placeholderValuesText ?? string.Empty)
                .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);

            foreach (string rawLine in lines)
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

                string key = line[..equalsIndex].Trim();
                string value = line[(equalsIndex + 1)..].Trim();
                if (!string.IsNullOrWhiteSpace(key))
                {
                    values[key] = value;
                }
            }

            return values;
        }

        private void RaiseStateChanged()
        {
            OnPropertyChanged(nameof(RequestFormatDisplayName));
            OnPropertyChanged(nameof(ResponseFormatDisplayName));
            OnPropertyChanged(nameof(CrcDisplayName));
            OnPropertyChanged(nameof(Summary));
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public sealed class ProtocolConfigProfile : INotifyPropertyChanged
    {
        private string _name = "协议 1";
        private ProtocolCommandConfig? _selectedCommand;

        public ProtocolConfigProfile()
        {
            AddCommand(new ProtocolCommandConfig());
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public ObservableCollection<ProtocolCommandConfig> Commands { get; } = new ObservableCollection<ProtocolCommandConfig>();

        public string Name
        {
            get => _name;
            set => SetField(ref _name, value, true);
        }

        public ProtocolCommandConfig? SelectedCommand
        {
            get => _selectedCommand;
            set
            {
                if (ReferenceEquals(_selectedCommand, value))
                {
                    return;
                }

                if (_selectedCommand is not null)
                {
                    _selectedCommand.PropertyChanged -= SelectedCommand_PropertyChanged;
                }

                _selectedCommand = value;
                if (_selectedCommand is not null)
                {
                    _selectedCommand.PropertyChanged += SelectedCommand_PropertyChanged;
                }

                OnPropertyChanged();
                RaiseCommandStateChanged();
            }
        }

        public ProtocolCommandConfig CurrentCommand => GetCurrentCommandOrFallback();

        // Backward-compatible selected-command passthrough properties.
        public string CommandName
        {
            get => CurrentCommand.Name;
            set
            {
                CurrentCommand.Name = value;
                OnPropertyChanged();
            }
        }

        public ProtocolPayloadFormat RequestFormat
        {
            get => CurrentCommand.RequestFormat;
            set
            {
                CurrentCommand.RequestFormat = value;
                OnPropertyChanged();
            }
        }

        public ProtocolPayloadFormat ResponseFormat
        {
            get => CurrentCommand.ResponseFormat;
            set
            {
                CurrentCommand.ResponseFormat = value;
                OnPropertyChanged();
            }
        }

        public string ReplyAggregationMilliseconds
        {
            get => CurrentCommand.ReplyAggregationMilliseconds;
            set
            {
                CurrentCommand.ReplyAggregationMilliseconds = value;
                OnPropertyChanged();
            }
        }

        public bool WaitForResponse
        {
            get => CurrentCommand.WaitForResponse;
            set
            {
                CurrentCommand.WaitForResponse = value;
                OnPropertyChanged();
            }
        }

        public ProtocolCrcMode CrcMode
        {
            get => CurrentCommand.CrcMode;
            set
            {
                CurrentCommand.CrcMode = value;
                OnPropertyChanged();
            }
        }

        public string ContentTemplate
        {
            get => CurrentCommand.ContentTemplate;
            set
            {
                CurrentCommand.ContentTemplate = value;
                OnPropertyChanged();
            }
        }

        public string PlaceholderValuesText
        {
            get => CurrentCommand.PlaceholderValuesText;
            set
            {
                CurrentCommand.PlaceholderValuesText = value;
                OnPropertyChanged();
            }
        }

        public ObservableCollection<ProtocolPlaceholderValue> PlaceholderValues => CurrentCommand.PlaceholderValues;

        public string SampleResponseText
        {
            get => CurrentCommand.SampleResponseText;
            set
            {
                CurrentCommand.SampleResponseText = value;
                OnPropertyChanged();
            }
        }

        public string ParseRulesText
        {
            get => CurrentCommand.ParseRulesText;
            set
            {
                CurrentCommand.ParseRulesText = value;
                OnPropertyChanged();
            }
        }

        public string RequestFormatDisplayName => CurrentCommand.RequestFormatDisplayName;

        public string ResponseFormatDisplayName => CurrentCommand.ResponseFormatDisplayName;

        public string CrcDisplayName => CurrentCommand.CrcDisplayName;

        public string Summary
        {
            get
            {
                ProtocolCommandConfig command = GetCurrentCommandOrFallback();
                if (SelectedCommand is null)
                {
                    return $"{Commands.Count} 条指令 / 当前：未选中指令";
                }

                return $"{Commands.Count} 条指令 / 当前：{command.Name} / {command.Summary}";
            }
        }

        public void AddCommand(ProtocolCommandConfig command)
        {
            command.PropertyChanged += Command_PropertyChanged;
            Commands.Add(command);
            RaiseCommandStateChanged();
        }

        public void RemoveCommand(ProtocolCommandConfig command)
        {
            command.PropertyChanged -= Command_PropertyChanged;
            if (!Commands.Remove(command))
            {
                return;
            }

            if (ReferenceEquals(SelectedCommand, command))
            {
                SelectedCommand = null;
            }

            RaiseCommandStateChanged();
        }

        public ProtocolConfigProfile Clone(string name)
        {
            ProtocolConfigProfile profile = new ProtocolConfigProfile
            {
                Name = name
            };

            foreach (ProtocolCommandConfig command in profile.Commands.ToList())
            {
                profile.RemoveCommand(command);
            }

            foreach (ProtocolCommandConfig command in Commands)
            {
                profile.AddCommand(command.Clone(command.Name));
            }

            profile.SelectedCommand = null;
            return profile;
        }

        private ProtocolCommandConfig GetCurrentCommandOrFallback()
        {
            if (SelectedCommand is not null)
            {
                return SelectedCommand;
            }

            return Commands.FirstOrDefault() ?? new ProtocolCommandConfig();
        }

        private void SelectedCommand_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            RaiseCommandStateChanged();
        }

        private void Command_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            OnPropertyChanged(nameof(Summary));
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
                RaiseCommandStateChanged();
            }

            return true;
        }

        private void RaiseCommandStateChanged()
        {
            OnPropertyChanged(nameof(CurrentCommand));
            OnPropertyChanged(nameof(CommandName));
            OnPropertyChanged(nameof(RequestFormat));
            OnPropertyChanged(nameof(ResponseFormat));
            OnPropertyChanged(nameof(ReplyAggregationMilliseconds));
            OnPropertyChanged(nameof(WaitForResponse));
            OnPropertyChanged(nameof(CrcMode));
            OnPropertyChanged(nameof(ContentTemplate));
            OnPropertyChanged(nameof(PlaceholderValuesText));
            OnPropertyChanged(nameof(PlaceholderValues));
            OnPropertyChanged(nameof(SampleResponseText));
            OnPropertyChanged(nameof(ParseRulesText));
            OnPropertyChanged(nameof(RequestFormatDisplayName));
            OnPropertyChanged(nameof(ResponseFormatDisplayName));
            OnPropertyChanged(nameof(CrcDisplayName));
            OnPropertyChanged(nameof(Summary));
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
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
    }

    public sealed class ProtocolRequestPreviewResult
    {
        public ProtocolRequestPreviewResult(string renderedTemplate, string requestHex, string requestAscii)
        {
            RenderedTemplate = renderedTemplate;
            RequestHex = requestHex;
            RequestAscii = requestAscii;
        }

        public string RenderedTemplate { get; }

        public string RequestHex { get; }

        public string RequestAscii { get; }
    }

    public sealed class ProtocolResponsePreviewResult
    {
        public ProtocolResponsePreviewResult(string responseHex, string responseAscii, string parsedJson)
        {
            ResponseHex = responseHex;
            ResponseAscii = responseAscii;
            ParsedJson = parsedJson;
        }

        public string ResponseHex { get; }

        public string ResponseAscii { get; }

        public string ParsedJson { get; }
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

            Dictionary<string, string> parsedValues;
            if (string.IsNullOrWhiteSpace(command.ParseRulesText))
            {
                parsedValues = CreateDefaultParsedValues(responseBytes, normalizedResponse, command.ResponseFormat);
                message = "已生成返回预览，未填写解析规则。";
            }
            else
            {
                if (!TryApplyParseRules(
                        command.ParseRulesText,
                        responseBytes,
                        normalizedResponse,
                        command.ResponseFormat,
                        out parsedValues,
                        out message))
                {
                    return false;
                }

                message = "返回数据解析预览已生成。";
            }

            result = new ProtocolResponsePreviewResult(
                responseBytes.ByteArrayToHexString(),
                FormatBytesAsAscii(responseBytes),
                JsonSerializer.Serialize(parsedValues, ParsedJsonOptions));
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

        private static bool TryApplyParseRules(
            string parseRulesText,
            byte[] responseBytes,
            string originalResponseText,
            ProtocolPayloadFormat responseFormat,
            out Dictionary<string, string> parsedValues,
            out string message)
        {
            parsedValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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

        private static Dictionary<string, string> CreateDefaultParsedValues(
            byte[] responseBytes,
            string originalResponseText,
            ProtocolPayloadFormat responseFormat)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Length"] = responseBytes.Length.ToString(),
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
