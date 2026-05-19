using ControlLibrary;
using Module.Communication.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Module.Communication.ViewModels.PropertyVMs
{
    public sealed class ProtocolConfigProfile : ViewModelProperties
    {
        private string _name = "协议 1";
        private ProtocolCommandConfig? _selectedCommand;

        public ProtocolConfigProfile()
        {
            AddCommand(new ProtocolCommandConfig());
        }

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
                OnPropertyChanged(nameof(IsResponseParserVisible));
            }
        }

        public bool IsParseOnly
        {
            get => CurrentCommand.IsParseOnly;
            set
            {
                CurrentCommand.IsParseOnly = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsResponseParserVisible));
                OnPropertyChanged(nameof(IsProtocolTemplateVisible));
            }
        }

        public ProtocolExecutionMode ExecutionMode
        {
            get => CurrentCommand.ExecutionMode;
            set
            {
                CurrentCommand.ExecutionMode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(WaitForResponse));
                OnPropertyChanged(nameof(IsParseOnly));
                OnPropertyChanged(nameof(IsResponseParserVisible));
                OnPropertyChanged(nameof(IsProtocolTemplateVisible));
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

        public ProtocolRequestSendMode RequestSendMode
        {
            get => CurrentCommand.RequestSendMode;
            set
            {
                CurrentCommand.RequestSendMode = value;
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

        public string RequestSendModeDisplayName => CurrentCommand.RequestSendModeDisplayName;

        public bool IsResponseParserVisible => CurrentCommand.WaitForResponse || CurrentCommand.IsParseOnly;

        public bool IsProtocolTemplateVisible => !CurrentCommand.IsParseOnly;

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
            if (string.IsNullOrWhiteSpace(e.PropertyName))
            {
                RaiseCommandStateChanged();
                return;
            }

            switch (e.PropertyName)
            {
                case nameof(ProtocolCommandConfig.ContentTemplate):
                case nameof(ProtocolCommandConfig.PlaceholderValuesText):
                case nameof(ProtocolCommandConfig.PlaceholderValues):
                case nameof(ProtocolCommandConfig.SampleResponseText):
                case nameof(ProtocolCommandConfig.ParseRulesText):
                    OnPropertyChanged(e.PropertyName);
                    return;
                default:
                    RaiseCommandStateChanged();
                    return;
            }
        }

        private void Command_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(e.PropertyName) ||
                e.PropertyName is nameof(ProtocolCommandConfig.Name)
                    or nameof(ProtocolCommandConfig.RequestFormat)
                    or nameof(ProtocolCommandConfig.ResponseFormat)
                    or nameof(ProtocolCommandConfig.ReplyAggregationMilliseconds)
                    or nameof(ProtocolCommandConfig.WaitForResponse)
                    or nameof(ProtocolCommandConfig.IsParseOnly)
                    or nameof(ProtocolCommandConfig.CrcMode)
                    or nameof(ProtocolCommandConfig.RequestSendMode))
            {
                OnPropertyChanged(nameof(Summary));
            }
        }

        private void RaiseCommandStateChanged()
        {
            OnPropertyChanged(nameof(CurrentCommand));
            OnPropertyChanged(nameof(CommandName));
            OnPropertyChanged(nameof(RequestFormat));
            OnPropertyChanged(nameof(ResponseFormat));
            OnPropertyChanged(nameof(ReplyAggregationMilliseconds));
            OnPropertyChanged(nameof(WaitForResponse));
            OnPropertyChanged(nameof(IsParseOnly));
            OnPropertyChanged(nameof(ExecutionMode));
            OnPropertyChanged(nameof(IsResponseParserVisible));
            OnPropertyChanged(nameof(IsProtocolTemplateVisible));
            OnPropertyChanged(nameof(CrcMode));
            OnPropertyChanged(nameof(RequestSendMode));
            OnPropertyChanged(nameof(ContentTemplate));
            OnPropertyChanged(nameof(PlaceholderValuesText));
            OnPropertyChanged(nameof(PlaceholderValues));
            OnPropertyChanged(nameof(SampleResponseText));
            OnPropertyChanged(nameof(ParseRulesText));
            OnPropertyChanged(nameof(RequestFormatDisplayName));
            OnPropertyChanged(nameof(ResponseFormatDisplayName));
            OnPropertyChanged(nameof(CrcDisplayName));
            OnPropertyChanged(nameof(RequestSendModeDisplayName));
            OnPropertyChanged(nameof(Summary));
        }
    }
    public sealed class ProtocolCommandConfig : ViewModelProperties
    {
        private static readonly Regex PlaceholderRegex =
            new Regex(@"\{\{\s*(?<name>[^{}\r\n]+?)\s*\}\}", RegexOptions.Compiled);

        private string _name = "指令 1";
        private ProtocolPayloadFormat _requestFormat = ProtocolPayloadFormat.Ascii;
        private ProtocolPayloadFormat _responseFormat = ProtocolPayloadFormat.Ascii;
        private string _replyAggregationMilliseconds = "200";
        private bool _waitForResponse = true;
        private bool _isParseOnly;
        private ProtocolCrcMode _crcMode = ProtocolCrcMode.None;
        private ProtocolRequestSendMode _requestSendMode = ProtocolRequestSendMode.SingleFrame;
        private string _contentTemplate = "AA {{Address}} {{Command}}";
        private string _placeholderValuesText = "Address=01\r\nCommand=03";
        private string _sampleResponseText = "AA 01 03";
        private string _parseRulesText = "return data;";
        private bool _isSyncingPlaceholders;

        public ProtocolCommandConfig()
        {
            PlaceholderValues.CollectionChanged += PlaceholderValues_CollectionChanged;
            SyncPlaceholderValuesFromTemplate(preferTextValues: true);
        }


        public ObservableCollection<ProtocolPlaceholderValue> PlaceholderValues { get; } = new ObservableCollection<ProtocolPlaceholderValue>();

        public ObservableCollection<string> ParsedResultKeys { get; } = new ObservableCollection<string>();

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

        public bool IsParseOnly
        {
            get => _isParseOnly;
            set => SetField(ref _isParseOnly, value);
        }

        public ProtocolExecutionMode ExecutionMode
        {
            get
            {
                if (IsParseOnly)
                {
                    return ProtocolExecutionMode.ParseOnly;
                }

                return WaitForResponse
                    ? ProtocolExecutionMode.SendAndWaitForResponse
                    : ProtocolExecutionMode.SendOnly;
            }
            set
            {
                switch (value)
                {
                    case ProtocolExecutionMode.ParseOnly:
                        IsParseOnly = true;
                        WaitForResponse = false;
                        break;
                    case ProtocolExecutionMode.SendAndWaitForResponse:
                        IsParseOnly = false;
                        WaitForResponse = true;
                        break;
                    default:
                        IsParseOnly = false;
                        WaitForResponse = false;
                        break;
                }

                OnPropertyChanged();
                RaiseStateChanged();
            }
        }

        public ProtocolCrcMode CrcMode
        {
            get => _crcMode;
            set => SetField(ref _crcMode, value);
        }

        public ProtocolRequestSendMode RequestSendMode
        {
            get => _requestSendMode;
            set => SetField(ref _requestSendMode, value);
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
            set => SetField(ref _sampleResponseText, value, raiseStateChanges: false);
        }

        public string ParseRulesText
        {
            get => _parseRulesText;
            set => SetField(ref _parseRulesText, value, raiseStateChanges: false);
        }

        public void ReplaceParsedResultKeys(IEnumerable<string> keys)
        {
            List<string> normalizedKeys = keys
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Select(key => key.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (ParsedResultKeys.SequenceEqual(normalizedKeys, StringComparer.OrdinalIgnoreCase))
            {
                return;
            }

            ParsedResultKeys.Clear();
            foreach (string key in normalizedKeys)
            {
                ParsedResultKeys.Add(key);
            }

            OnPropertyChanged(nameof(ParsedResultKeys));
        }

        public string RequestFormatDisplayName => ProtocolDisplayNames.GetPayloadFormatDisplayName(RequestFormat);

        public string ResponseFormatDisplayName => ProtocolDisplayNames.GetPayloadFormatDisplayName(ResponseFormat);

        public string CrcDisplayName => ProtocolDisplayNames.GetCrcDisplayName(CrcMode);

        public string RequestSendModeDisplayName => ProtocolDisplayNames.GetRequestSendModeDisplayName(RequestSendMode);

        public string ExecutionModeDisplayName => IsParseOnly ? "仅解析" : "发送";

        public string Summary =>
            IsParseOnly
                ? $"{ExecutionModeDisplayName} / 返回 {ResponseFormatDisplayName} / 拼接等待 {ReplyAggregationMilliseconds} ms"
                : $"{RequestFormatDisplayName} -> {ResponseFormatDisplayName} / {RequestSendModeDisplayName} / {CrcDisplayName} / 拼接等待 {ReplyAggregationMilliseconds} ms";

        public ProtocolCommandConfig Clone(string name)
        {
            ProtocolCommandConfig command = new ProtocolCommandConfig
            {
                Name = name,
                RequestFormat = RequestFormat,
                ResponseFormat = ResponseFormat,
                ReplyAggregationMilliseconds = ReplyAggregationMilliseconds,
                WaitForResponse = WaitForResponse,
                IsParseOnly = IsParseOnly,
                CrcMode = CrcMode,
                RequestSendMode = RequestSendMode,
                ContentTemplate = ContentTemplate,
                PlaceholderValuesText = PlaceholderValuesText,
                SampleResponseText = SampleResponseText,
                ParseRulesText = ParseRulesText
            };

            command.ReplaceParsedResultKeys(ParsedResultKeys);
            return command;
        }

        private bool SetField<T>(ref T field, T value, bool raiseStateChanges = true, [CallerMemberName] string? propertyName = null)
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
            OnPropertyChanged(nameof(RequestSendModeDisplayName));
            OnPropertyChanged(nameof(ExecutionModeDisplayName));
            OnPropertyChanged(nameof(ExecutionMode));
            OnPropertyChanged(nameof(Summary));
        }

    }
    public sealed class ProtocolPlaceholderValue : ViewModelProperties
    {
        private string _name = string.Empty;
        private string _value = string.Empty;

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
    }
}
