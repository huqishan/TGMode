using Newtonsoft.Json;
using Shared.Models.MES;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Module.MES.ViewModels.VMs
{
    public sealed class ApiOptionItem
    {
        public ApiOptionItem(string value, string displayName, string description = "")
        {
            Value = value;
            DisplayName = displayName;
            Description = description;
        }

        public string Value { get; }

        public string DisplayName { get; }

        public string Description { get; }
    }

    public sealed class ApiHeaderItem : INotifyPropertyChanged
    {
        private string _key = string.Empty;
        private string _value = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Key
        {
            get => _key;
            set => SetField(ref _key, value, trimString: true);
        }

        public string Value
        {
            get => _value;
            set => SetField(ref _value, value, trimString: false);
        }

        public ApiHeaderItem Clone()
        {
            return new ApiHeaderItem
            {
                Key = Key,
                Value = Value
            };
        }

        private bool SetField<T>(ref T field, T value, bool trimString, [CallerMemberName] string? propertyName = null)
        {
            object? normalizedValue = value;
            if (trimString && value is string stringValue)
            {
                normalizedValue = stringValue.Trim();
            }

            if (Equals(field, normalizedValue))
            {
                return false;
            }

            field = (T)normalizedValue!;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }
    }

    public sealed class ApiInterfaceProfile : INotifyPropertyChanged
    {
        private string _apiName = "MES 接口 1";
        private string _selectMesType = "WEBAPI";
        private string _resultCheck = string.Empty;
        private string _dataStructName = string.Empty;
        private bool _isEnabledApi = true;
        private bool _isCommunicationQueryVisible = true;
        private string _remarks = string.Empty;
        private string _lua = string.Empty;
        private bool _isEnter;
        private bool _isEnabledTcpKeepAlive;
        private string _tcpLocalIpAddress = "127.0.0.1";
        private string _tcpLocalPort = "0";
        private string _tcpRemoteIpAddress = "127.0.0.1";
        private string _tcpRemotePort = "0";
        private string _url = string.Empty;
        private string _userName = string.Empty;
        private string _password = string.Empty;
        private string _action = string.Empty;
        private string _tokenUrl = string.Empty;
        private string _tokenName = "accessToken";
        private string _webApiType = "POST";
        private string _downPath = string.Empty;
        private bool _isDown;
        private string _sampleRequestBody = string.Empty;
        private string _sampleResponseBody = string.Empty;
        private ApiHeaderItem? _selectedHeader;

        public ApiInterfaceProfile()
        {
            Heads.CollectionChanged += Heads_CollectionChanged;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public ObservableCollection<ApiHeaderItem> Heads { get; } = new();

        public string ApiName
        {
            get => _apiName;
            set => SetField(ref _apiName, value, trimString: true);
        }

        public string SelectMESType
        {
            get => _selectMesType;
            set
            {
                string normalizedValue = NormalizeTransportType(value);
                if (!SetField(ref _selectMesType, normalizedValue, trimString: false))
                {
                    return;
                }
                OnPropertyChanged(nameof(TypeDisplayName));
            }
        }

        public string ResultCheck
        {
            get => _resultCheck;
            set => SetField(ref _resultCheck, value, trimString: true);
        }

        public string DataStructName
        {
            get => _dataStructName;
            set => SetField(ref _dataStructName, value, trimString: true);
        }

        public bool IsEnabledAPI
        {
            get => _isEnabledApi;
            set => SetField(ref _isEnabledApi, value, trimString: false);
        }

        public bool IsCommunicationQueryVisible
        {
            get => _isCommunicationQueryVisible;
            set => SetField(ref _isCommunicationQueryVisible, value, trimString: false);
        }

        public string Remarks
        {
            get => _remarks;
            set => SetField(ref _remarks, value, trimString: true);
        }

        public string Lua
        {
            get => _lua;
            set => SetField(ref _lua, value, trimString: false);
        }

        public bool IsEnter
        {
            get => _isEnter;
            set => SetField(ref _isEnter, value, trimString: false);
        }

        public bool IsEnabledTCPKeepAlive
        {
            get => _isEnabledTcpKeepAlive;
            set => SetField(ref _isEnabledTcpKeepAlive, value, trimString: false);
        }

        public string TCPLocalIpAddress
        {
            get => _tcpLocalIpAddress;
            set => SetField(ref _tcpLocalIpAddress, value, trimString: true);
        }

        public string TCPLocalPort
        {
            get => _tcpLocalPort;
            set => SetField(ref _tcpLocalPort, value, trimString: true);
        }

        public string TCPRemoteIpAddress
        {
            get => _tcpRemoteIpAddress;
            set => SetField(ref _tcpRemoteIpAddress, value, trimString: true);
        }

        public string TCPRemotePort
        {
            get => _tcpRemotePort;
            set => SetField(ref _tcpRemotePort, value, trimString: true);
        }

        public string Url
        {
            get => _url;
            set => SetField(ref _url, value, trimString: false);
        }

        public string UserName
        {
            get => _userName;
            set => SetField(ref _userName, value, trimString: false);
        }

        public string Password
        {
            get => _password;
            set => SetField(ref _password, value, trimString: false);
        }

        public string Action
        {
            get => _action;
            set => SetField(ref _action, value, trimString: false);
        }

        public string TokenUrl
        {
            get => _tokenUrl;
            set => SetField(ref _tokenUrl, value, trimString: false);
        }

        public string TokenName
        {
            get => _tokenName;
            set => SetField(ref _tokenName, value, trimString: true);
        }

        public string WebApiType
        {
            get => _webApiType;
            set => SetField(ref _webApiType, NormalizeHttpMethod(value), trimString: false);
        }

        public string DownPath
        {
            get => _downPath;
            set => SetField(ref _downPath, value, trimString: false);
        }

        public bool IsDown
        {
            get => _isDown;
            set => SetField(ref _isDown, value, trimString: false);
        }
        [JsonIgnore]
        public string SampleRequestBody
        {
            get => _sampleRequestBody;
            set => SetField(ref _sampleRequestBody, value, trimString: false);
        }
        [JsonIgnore]
        public string SampleResponseBody
        {
            get => _sampleResponseBody;
            set => SetField(ref _sampleResponseBody, value, trimString: false);
        }

        public ApiHeaderItem? SelectedHeader
        {
            get => _selectedHeader;
            set
            {
                if (ReferenceEquals(_selectedHeader, value))
                {
                    return;
                }

                _selectedHeader = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSelectedHeader));
            }
        }

        public bool HasSelectedHeader => SelectedHeader is not null;

        public string TypeDisplayName =>
            SelectMESType.ToUpperInvariant() switch
            {
                "TCP CLIENT" => "TCP Client",
                "WEBAPI" => "WebApi",
                "WEBSERVICE" => "WebService",
                "FTP" => "FTP",
                "P-INVOKE" => "P-Invoke",
                _ => SelectMESType
            };

        public string Summary
        {
            get
            {
                string structureText = string.IsNullOrWhiteSpace(DataStructName) ? "未绑定结构" : DataStructName;
                string enabledText = IsEnabledAPI ? "已启用" : "未启用";
                return $"{TypeDisplayName} / {structureText} / {enabledText}";
            }
        }

        public string HeaderSummaryText =>
            Heads.Count == 0
                ? "未配置请求头"
                : $"已配置 {Heads.Count} 个请求头";

        public string LuaSummaryText
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Lua))
                {
                    return "未配置脚本，将按默认 SendMES 调用。";
                }

                int lineCount = Lua.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Length;
                return $"已配置脚本，共 {lineCount} 行。";
            }
        }

        public APIConfig ToApiConfig()
        {
            return new APIConfig
            {
                ApiName = ApiName,
                SelectMESType = NormalizeTransportType(SelectMESType),
                ResultCheck = ResultCheck,
                DataStructName = DataStructName,
                IsEnabledAPI = IsEnabledAPI,
                IsCommunicationQueryVisible = IsCommunicationQueryVisible,
                Remarks = Remarks,
                Lua = Lua,
                IsEnter = IsEnter,
                IsEnabledTCPKeepAlive = IsEnabledTCPKeepAlive,
                TCPLocalIpAddress = TCPLocalIpAddress,
                TCPLocalPort = ParsePort(TCPLocalPort),
                TCPRemoteIpAddress = TCPRemoteIpAddress,
                TCPRemotePort = ParsePort(TCPRemotePort),
                Url = Url,
                UserName = UserName,
                Password = Password,
                Action = Action,
                TokenUrl = TokenUrl,
                TokenName = TokenName,
                WebApiType = NormalizeHttpMethod(WebApiType),
                Heads = Heads
                    .Where(item => !string.IsNullOrWhiteSpace(item.Key))
                    .Select(item => new WebApiHeader
                    {
                        Key = item.Key.Trim(),
                        Value = item.Value ?? string.Empty
                    })
                    .ToList(),
                DownPath = DownPath,
                IsDown = IsDown
            };
        }

        public static ApiInterfaceProfile FromApiConfig(APIConfig? config, string fallbackName)
        {
            ApiInterfaceProfile profile = new()
            {
                ApiName = string.IsNullOrWhiteSpace(config?.ApiName) ? fallbackName : config.ApiName.Trim(),
                SelectMESType = NormalizeTransportType(config?.SelectMESType),
                ResultCheck = config?.ResultCheck ?? string.Empty,
                DataStructName = config?.DataStructName ?? string.Empty,
                IsEnabledAPI = config?.IsEnabledAPI ?? true,
                IsCommunicationQueryVisible = config?.IsCommunicationQueryVisible ?? true,
                Remarks = config?.Remarks ?? string.Empty,
                Lua = config?.Lua ?? string.Empty,
                IsEnter = config?.IsEnter ?? false,
                IsEnabledTCPKeepAlive = config?.IsEnabledTCPKeepAlive ?? false,
                TCPLocalIpAddress = string.IsNullOrWhiteSpace(config?.TCPLocalIpAddress) ? "127.0.0.1" : config.TCPLocalIpAddress.Trim(),
                TCPLocalPort = (config?.TCPLocalPort ?? 0).ToString(),
                TCPRemoteIpAddress = string.IsNullOrWhiteSpace(config?.TCPRemoteIpAddress) ? "127.0.0.1" : config.TCPRemoteIpAddress.Trim(),
                TCPRemotePort = (config?.TCPRemotePort ?? 0).ToString(),
                Url = config?.Url ?? string.Empty,
                UserName = config?.UserName ?? string.Empty,
                Password = config?.Password ?? string.Empty,
                Action = config?.Action ?? string.Empty,
                TokenUrl = config?.TokenUrl ?? string.Empty,
                TokenName = string.IsNullOrWhiteSpace(config?.TokenName) ? "accessToken" : config.TokenName.Trim(),
                WebApiType = NormalizeHttpMethod(config?.WebApiType),
                DownPath = config?.DownPath ?? string.Empty,
                IsDown = config?.IsDown ?? false
            };

            if (config?.Heads is not null)
            {
                foreach (WebApiHeader header in config.Heads)
                {
                    profile.Heads.Add(new ApiHeaderItem
                    {
                        Key = header.Key ?? string.Empty,
                        Value = header.Value ?? string.Empty
                    });
                }
            }

            return profile;
        }

        public ApiInterfaceProfile Clone(string newName)
        {
            ApiInterfaceProfile clone = FromApiConfig(ToApiConfig(), newName);
            clone.ApiName = newName;
            clone.SelectedHeader = clone.Heads.FirstOrDefault();
            return clone;
        }

        private static ushort ParsePort(string value)
        {
            return ushort.TryParse(value?.Trim(), out ushort port) ? port : (ushort)0;
        }

        private static string NormalizeHttpMethod(string? value)
        {
            return string.Equals(value?.Trim(), "GET", StringComparison.OrdinalIgnoreCase)
                ? "GET"
                : "POST";
        }

        public static string NormalizeTransportType(string? value)
        {
            string normalized = value?.Trim().Replace("_", " ").Replace("-", " ").ToUpperInvariant() ?? string.Empty;
            return normalized switch
            {
                "TCPCLIENT" => "TCP CLIENT",
                "TCP CLIENT" => "TCP CLIENT",
                "WEBAPI" => "WEBAPI",
                "WEBSERVICE" => "WEBSERVICE",
                "WEB SERVICE" => "WEBSERVICE",
                "FTP" => "FTP",
                "PINVOKE" => "P-INVOKE",
                "P INVOKE" => "P-INVOKE",
                "P-INVOKE" => "P-INVOKE",
                _ => string.IsNullOrWhiteSpace(normalized) ? "WEBAPI" : normalized
            };
        }

        private void Heads_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(Summary));
            OnPropertyChanged(nameof(HeaderSummaryText));
        }

        private bool SetField<T>(ref T field, T value, bool trimString, [CallerMemberName] string? propertyName = null)
        {
            object? normalizedValue = value;
            if (trimString && value is string stringValue)
            {
                normalizedValue = stringValue.Trim();
            }

            if (Equals(field, normalizedValue))
            {
                return false;
            }

            field = (T)normalizedValue!;
            OnPropertyChanged(propertyName);
            OnPropertyChanged(nameof(Summary));
            OnPropertyChanged(nameof(LuaSummaryText));
            return true;
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
