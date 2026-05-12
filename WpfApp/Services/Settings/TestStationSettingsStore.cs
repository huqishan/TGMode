using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using WpfApp.Models.Settings;

namespace WpfApp.Services.Settings
{
    /// <summary>
    /// 测试工位设置存储服务。
    /// </summary>
    public static class TestStationSettingsStore
    {
        #region 字段

        private static readonly string ConfigDirectory =
            Path.Combine(AppContext.BaseDirectory, "Config", "Settings");

        private static readonly string ConfigFilePath =
            Path.Combine(ConfigDirectory, "TestStationSettings.json");

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        #endregion

        #region 公共方法

        /// <summary>
        /// 加载测试工位设置。
        /// </summary>
        /// <returns>测试工位设置集合。</returns>
        public static TestStationSettingsCatalog Load()
        {
            if (!File.Exists(ConfigFilePath))
            {
                return CreateDefaultCatalog();
            }

            try
            {
                string json = File.ReadAllText(ConfigFilePath);
                TestStationSettingsCatalog? catalog =
                    JsonSerializer.Deserialize<TestStationSettingsCatalog>(json, JsonOptions);

                return NormalizeCatalog(catalog);
            }
            catch
            {
                return CreateDefaultCatalog();
            }
        }

        /// <summary>
        /// 保存测试工位设置。
        /// </summary>
        /// <param name="catalog">待保存的测试工位设置。</param>
        public static void Save(TestStationSettingsCatalog? catalog)
        {
            TestStationSettingsCatalog normalized = NormalizeCatalog(catalog);

            Directory.CreateDirectory(ConfigDirectory);
            string json = JsonSerializer.Serialize(normalized, JsonOptions);
            File.WriteAllText(ConfigFilePath, json);
        }

        #endregion

        #region 归一化辅助

        /// <summary>
        /// 创建默认测试工位配置。
        /// </summary>
        /// <returns>默认配置集合。</returns>
        private static TestStationSettingsCatalog CreateDefaultCatalog()
        {
            return new TestStationSettingsCatalog
            {
                Stations = new ObservableCollection<TestStationSetting>
                {
                    CreateDefaultStation(1)
                }
            };
        }

        /// <summary>
        /// 对工位配置集合进行归一化处理。
        /// </summary>
        /// <param name="catalog">原始配置集合。</param>
        /// <returns>归一化后的配置集合。</returns>
        private static TestStationSettingsCatalog NormalizeCatalog(TestStationSettingsCatalog? catalog)
        {
            if (catalog?.Stations is null || catalog.Stations.Count == 0)
            {
                return CreateDefaultCatalog();
            }

            TestStationSettingsCatalog normalized = new()
            {
                Stations = new ObservableCollection<TestStationSetting>(
                    catalog.Stations.Select(NormalizeStation))
            };

            if (normalized.Stations.Count == 0)
            {
                normalized.Stations.Add(CreateDefaultStation(1));
            }

            return normalized;
        }

        /// <summary>
        /// 对单个工位配置进行归一化处理。
        /// </summary>
        /// <param name="station">原始工位配置。</param>
        /// <returns>归一化后的工位配置。</returns>
        private static TestStationSetting NormalizeStation(TestStationSetting? station)
        {
            if (station is null)
            {
                return CreateDefaultStation(1);
            }

            return new TestStationSetting
            {
                Id = string.IsNullOrWhiteSpace(station.Id)
                    ? Guid.NewGuid().ToString("N")
                    : station.Id.Trim(),
                StationName = string.IsNullOrWhiteSpace(station.StationName)
                    ? CreateDefaultStationName(1)
                    : station.StationName.Trim(),
                IsSchemeMatchingEnabled = station.IsSchemeMatchingEnabled
            };
        }

        /// <summary>
        /// 创建默认工位配置。
        /// </summary>
        /// <param name="index">工位序号。</param>
        /// <returns>默认工位配置。</returns>
        private static TestStationSetting CreateDefaultStation(int index)
        {
            return new TestStationSetting
            {
                StationName = CreateDefaultStationName(index),
                IsSchemeMatchingEnabled = false
            };
        }

        /// <summary>
        /// 生成默认工位名称。
        /// </summary>
        /// <param name="index">工位序号。</param>
        /// <returns>默认工位名称。</returns>
        private static string CreateDefaultStationName(int index)
        {
            return $"Station-{index:00}";
        }

        #endregion
    }
}
