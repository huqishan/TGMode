using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace WpfApp.Models.DataManagement;

/// <summary>
/// 一套可启用的数据展示配置，包含配置名称和一组列定义。
/// </summary>
public sealed class TestDataGridConfiguration : INotifyPropertyChanged
{
    private string _id = string.Empty;
    private string _name = string.Empty;
    private ObservableCollection<TestDataGridColumnConfig> _columns = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id
    {
        get => _id;
        set => SetField(ref _id, value?.Trim() ?? string.Empty);
    }

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value?.Trim() ?? string.Empty);
    }

    public ObservableCollection<TestDataGridColumnConfig> Columns
    {
        get => _columns;
        set => SetField(ref _columns, value ?? new ObservableCollection<TestDataGridColumnConfig>());
    }

    // 备注：默认配置按模型字段生成列，保证首次打开页面时 GridView 可直接显示。
    public static TestDataGridConfiguration CreateDefault(IReadOnlyList<GridBindingOption> bindingOptions, string name = "默认配置")
    {
        return new TestDataGridConfiguration
        {
            Id = System.Guid.NewGuid().ToString("N"),
            Name = name,
            Columns = new ObservableCollection<TestDataGridColumnConfig>(
                bindingOptions.Select(option => new TestDataGridColumnConfig
                {
                    ColumnName = option.DisplayName,
                    BindingPath = option.PropertyName,
                    IsVisible = true,
                    Width = GetDefaultWidth(option.PropertyType)
                }))
        };
    }

    public TestDataGridConfiguration Clone(string name)
    {
        return new TestDataGridConfiguration
        {
            Id = System.Guid.NewGuid().ToString("N"),
            Name = name,
            Columns = new ObservableCollection<TestDataGridColumnConfig>(
                Columns.Select(column => column.Clone()))
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

    private static double GetDefaultWidth(System.Type propertyType)
    {
        // 备注：不同字段类型给一个适合阅读的默认宽度，后续仍可在界面里调整。
        System.Type type = System.Nullable.GetUnderlyingType(propertyType) ?? propertyType;
        if (type == typeof(System.DateTime))
        {
            return 180d;
        }

        if (type == typeof(string))
        {
            return 160d;
        }

        return 120d;
    }
}

/// <summary>
/// 多套数据展示配置的目录，SelectedConfigurationId 表示当前启用的配置。
/// </summary>
public sealed class TestDataGridConfigurationCatalog
{
    public ObservableCollection<TestDataGridConfiguration> Configurations { get; set; } = new();

    public string SelectedConfigurationId { get; set; } = string.Empty;

    // 备注：这是运行时便捷属性，不写入 JSON，避免保存冗余对象。
    [JsonIgnore]
    public TestDataGridConfiguration? SelectedConfiguration =>
        Configurations.FirstOrDefault(configuration => configuration.Id == SelectedConfigurationId) ??
        Configurations.FirstOrDefault();

    // 备注：配置文件不存在或损坏时，用模型字段创建一套可用的默认配置。
    public static TestDataGridConfigurationCatalog CreateDefault(IReadOnlyList<GridBindingOption> bindingOptions)
    {
        TestDataGridConfiguration configuration =
            TestDataGridConfiguration.CreateDefault(bindingOptions);

        return new TestDataGridConfigurationCatalog
        {
            SelectedConfigurationId = configuration.Id,
            Configurations = new ObservableCollection<TestDataGridConfiguration> { configuration }
        };
    }
}
