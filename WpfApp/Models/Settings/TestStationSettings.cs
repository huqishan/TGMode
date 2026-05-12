using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WpfApp.Models.Settings
{
    /// <summary>
    /// 测试工位单项配置。
    /// </summary>
    public sealed class TestStationSetting : INotifyPropertyChanged
    {
        private string _id = Guid.NewGuid().ToString("N");
        private string _stationName = string.Empty;
        private bool _isSchemeMatchingEnabled;

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// 配置唯一标识。
        /// </summary>
        public string Id
        {
            get => _id;
            set => SetField(ref _id, value?.Trim() ?? string.Empty);
        }

        /// <summary>
        /// 测试工位名称。
        /// </summary>
        public string StationName
        {
            get => _stationName;
            set => SetField(ref _stationName, value?.Trim() ?? string.Empty);
        }

        /// <summary>
        /// 是否启用方案匹配。
        /// </summary>
        public bool IsSchemeMatchingEnabled
        {
            get => _isSchemeMatchingEnabled;
            set => SetField(ref _isSchemeMatchingEnabled, value);
        }

        /// <summary>
        /// 创建当前配置的副本，用于持久化保存。
        /// </summary>
        /// <returns>配置副本。</returns>
        public TestStationSetting Clone()
        {
            return new TestStationSetting
            {
                Id = Id,
                StationName = StationName,
                IsSchemeMatchingEnabled = IsSchemeMatchingEnabled
            };
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

    /// <summary>
    /// 测试工位配置集合。
    /// </summary>
    public sealed class TestStationSettingsCatalog
    {
        /// <summary>
        /// 测试工位列表。
        /// </summary>
        public ObservableCollection<TestStationSetting> Stations { get; set; } = new();
    }
}
