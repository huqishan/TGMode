using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using WpfApp.Models.DataManagement;

namespace WpfApp.Services.DataManagement;

/// <summary>
/// 负责数据配置的读取、保存、兼容旧格式和启用配置切换。
/// </summary>
public static class TestDataGridConfigurationStore
{
    // 备注：配置保存到程序运行目录，避免写入源码目录。
    private static readonly string ConfigDirectory =
        Path.Combine(AppContext.BaseDirectory, "Config", "DataManagement");

    private static readonly string ConfigFilePath =
        Path.Combine(ConfigDirectory, "TestDataGridColumns.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static event EventHandler? ConfigurationSaved;

    // 备注：绑定字段统一从 TestDataRecord 生成，配置页和展示页共用同一组选项。
    public static IReadOnlyList<GridBindingOption> BindingOptions { get; } =
        GridBindingOption.FromModel<TestDataRecord>();

    // 备注：读取多配置目录；如果发现旧的单配置 JSON，会自动迁移成默认配置。
    public static TestDataGridConfigurationCatalog LoadCatalog()
    {
        if (!File.Exists(ConfigFilePath))
        {
            return TestDataGridConfigurationCatalog.CreateDefault(BindingOptions);
        }

        try
        {
            string json = File.ReadAllText(ConfigFilePath);
            TestDataGridConfigurationCatalog? catalog =
                JsonSerializer.Deserialize<TestDataGridConfigurationCatalog>(json, JsonOptions);

            if (catalog?.Configurations is not null && catalog.Configurations.Count > 0)
            {
                return NormalizeCatalog(catalog);
            }

            TestDataGridConfiguration? legacyConfiguration =
                JsonSerializer.Deserialize<TestDataGridConfiguration>(json, JsonOptions);

            if (legacyConfiguration?.Columns is not null)
            {
                // 备注：兼容早期只保存 Columns 的单配置文件，避免用户已有配置丢失。
                legacyConfiguration.Name = string.IsNullOrWhiteSpace(legacyConfiguration.Name)
                    ? "默认配置"
                    : legacyConfiguration.Name;
                legacyConfiguration.Id = string.IsNullOrWhiteSpace(legacyConfiguration.Id)
                    ? Guid.NewGuid().ToString("N")
                    : legacyConfiguration.Id;

                TestDataGridConfigurationCatalog legacyCatalog = new()
                {
                    SelectedConfigurationId = legacyConfiguration.Id,
                    Configurations = new ObservableCollection<TestDataGridConfiguration>
                    {
                        legacyConfiguration
                    }
                };

                return NormalizeCatalog(legacyCatalog);
            }
        }
        catch
        {
        }

        return TestDataGridConfigurationCatalog.CreateDefault(BindingOptions);
    }

    // 备注：保留单配置读取入口，给旧调用点或简单场景使用。
    public static TestDataGridConfiguration Load()
    {
        return LoadCatalog().SelectedConfiguration ??
               TestDataGridConfiguration.CreateDefault(BindingOptions);
    }

    public static void SaveCatalog(TestDataGridConfigurationCatalog catalog)
    {
        // 备注：保存前做规范化，保证配置名称、ID、绑定字段都有效。
        TestDataGridConfigurationCatalog normalized = NormalizeCatalog(catalog);
        WriteCatalog(normalized, notify: true);
    }

    // 备注：保留单配置保存入口，会包装成一个配置目录后写入。
    public static void Save(TestDataGridConfiguration configuration)
    {
        TestDataGridConfigurationCatalog catalog = new()
        {
            SelectedConfigurationId = configuration.Id,
            Configurations = new ObservableCollection<TestDataGridConfiguration>
            {
                configuration
            }
        };

        SaveCatalog(catalog);
    }

    public static void SaveSelectedConfigurationId(string configurationId)
    {
        // 备注：测试数据页切换展示配置时只更新启用 ID，不触发配置刷新事件。
        TestDataGridConfigurationCatalog catalog = LoadCatalog();
        if (catalog.Configurations.Any(configuration => configuration.Id == configurationId))
        {
            catalog.SelectedConfigurationId = configurationId;
            WriteCatalog(NormalizeCatalog(catalog), notify: false);
        }
    }

    public static TestDataGridConfiguration ResetToModelFields()
    {
        TestDataGridConfiguration configuration = TestDataGridConfiguration.CreateDefault(BindingOptions);
        Save(configuration);
        return configuration;
    }

    public static void ResetColumnsToModelFields(TestDataGridConfiguration configuration)
    {
        // 备注：只重置当前配置的列，不改变配置名称和启用关系。
        TestDataGridConfiguration reset = TestDataGridConfiguration.CreateDefault(BindingOptions, configuration.Name);
        configuration.Columns = new ObservableCollection<TestDataGridColumnConfig>(
            reset.Columns.Select(column => column.Clone()));
    }

    private static TestDataGridConfigurationCatalog NormalizeCatalog(TestDataGridConfigurationCatalog? catalog)
    {
        // 备注：配置目录为空时直接补一套默认配置，防止界面无可选项。
        if (catalog?.Configurations is null || catalog.Configurations.Count == 0)
        {
            return TestDataGridConfigurationCatalog.CreateDefault(BindingOptions);
        }

        HashSet<string> usedIds = new(StringComparer.Ordinal);
        HashSet<string> usedNames = new(StringComparer.OrdinalIgnoreCase);
        int index = 1;

        foreach (TestDataGridConfiguration configuration in catalog.Configurations)
        {
            if (string.IsNullOrWhiteSpace(configuration.Id) || !usedIds.Add(configuration.Id))
            {
                // 备注：每套配置必须有唯一 ID，启用配置就是靠这个 ID 定位。
                configuration.Id = Guid.NewGuid().ToString("N");
                usedIds.Add(configuration.Id);
            }

            string fallbackName = $"数据配置 {index}";
            configuration.Name = BuildUniqueName(
                string.IsNullOrWhiteSpace(configuration.Name) ? fallbackName : configuration.Name,
                usedNames);
            Normalize(configuration);
            index++;
        }

        if (!catalog.Configurations.Any(configuration => configuration.Id == catalog.SelectedConfigurationId))
        {
            // 备注：已启用的配置被删除时，自动启用第一套可用配置。
            catalog.SelectedConfigurationId = catalog.Configurations.First().Id;
        }

        return catalog;
    }

    private static TestDataGridConfiguration Normalize(TestDataGridConfiguration? configuration)
    {
        // 备注：单套配置也要规范化，避免无列、无效绑定字段或非法宽度导致表格异常。
        if (configuration is null)
        {
            return TestDataGridConfiguration.CreateDefault(BindingOptions);
        }

        if (configuration.Columns is null || configuration.Columns.Count == 0)
        {
            TestDataGridConfiguration fallback =
                TestDataGridConfiguration.CreateDefault(BindingOptions, configuration.Name);
            configuration.Columns = new ObservableCollection<TestDataGridColumnConfig>(
                fallback.Columns.Select(column => column.Clone()));
        }

        HashSet<string> validBindings = BindingOptions
            .Select(option => option.PropertyName)
            .ToHashSet(StringComparer.Ordinal);

        string fallbackBinding = BindingOptions.FirstOrDefault()?.PropertyName ?? string.Empty;
        Dictionary<string, GridBindingOption> optionByProperty = BindingOptions
            .ToDictionary(option => option.PropertyName, StringComparer.Ordinal);

        foreach (TestDataGridColumnConfig column in configuration.Columns)
        {
            if (string.IsNullOrWhiteSpace(column.BindingPath) ||
                !validBindings.Contains(column.BindingPath))
            {
                column.BindingPath = fallbackBinding;
            }

            if (string.IsNullOrWhiteSpace(column.ColumnName) &&
                optionByProperty.TryGetValue(column.BindingPath, out GridBindingOption? option))
            {
                column.ColumnName = option.DisplayName;
            }

            if (column.Width <= 0)
            {
                column.Width = 150d;
            }
        }

        return configuration;
    }

    private static string BuildUniqueName(string sourceName, HashSet<string> usedNames)
    {
        // 备注：保存时自动处理重名配置，防止下拉框里出现两个完全同名项。
        string baseName = string.IsNullOrWhiteSpace(sourceName)
            ? "数据配置"
            : sourceName.Trim();

        string name = baseName;
        for (int index = 2; !usedNames.Add(name); index++)
        {
            name = $"{baseName} {index}";
        }

        return name;
    }

    private static void WriteCatalog(TestDataGridConfigurationCatalog catalog, bool notify)
    {
        // 备注：notify=true 用于保存/启用配置后通知测试数据页刷新列。
        Directory.CreateDirectory(ConfigDirectory);
        string json = JsonSerializer.Serialize(catalog, JsonOptions);
        File.WriteAllText(ConfigFilePath, json);

        if (notify)
        {
            ConfigurationSaved?.Invoke(null, EventArgs.Empty);
        }
    }
}
