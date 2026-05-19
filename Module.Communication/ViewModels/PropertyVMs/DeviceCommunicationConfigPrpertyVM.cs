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
    public sealed class DeviceSupportedProtocol : ViewModelProperties
    {
        private string _protocolName = string.Empty;
        private string _protocolFilePath = string.Empty;

        public string ProtocolName
        {
            get => _protocolName;
            set
            {
                if (SetField(ref _protocolName, value?.Trim() ?? string.Empty))
                {
                    OnPropertyChanged(nameof(DisplayProtocolName));
                }
            }
        }

        public string ProtocolFilePath
        {
            get => _protocolFilePath;
            set
            {
                if (SetField(ref _protocolFilePath, value?.Trim() ?? string.Empty))
                {
                    OnPropertyChanged(nameof(DisplayProtocolFilePath));
                }
            }
        }

        public string DisplayProtocolName =>
            string.IsNullOrWhiteSpace(ProtocolName) ? "未选择协议" : ProtocolName;

        public string DisplayProtocolFilePath =>
            string.IsNullOrWhiteSpace(ProtocolFilePath) ? "点击加载协议文件" : ProtocolFilePath;

        public bool IsEmpty =>
            string.IsNullOrWhiteSpace(ProtocolName) &&
            string.IsNullOrWhiteSpace(ProtocolFilePath);

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value))
            {
                return false;
            }

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
