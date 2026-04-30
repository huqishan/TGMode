using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using WpfApp.Models.DataManagement;
using WpfApp.Services.DataManagement;

namespace WpfApp.Views.DataManagement;

/// <summary>
/// 测试数据页：按用户选择或已启用的数据配置动态生成 GridView 列。
/// </summary>
public partial class TestDataView : UserControl, INotifyPropertyChanged
{
    private static readonly Brush SuccessBrush =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16A34A"));

    private static readonly Brush NeutralBrush =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));

    private TestDataGridConfigurationCatalog _catalog =
        TestDataGridConfigurationCatalog.CreateDefault(TestDataGridConfigurationStore.BindingOptions);

    // 备注：这里的选中配置用于当前展示；切换后会保存为默认展示配置。
    private TestDataGridConfiguration? _selectedConfiguration;
    private string _pageStatusText = "等待加载";
    private Brush _pageStatusBrush = NeutralBrush;
    private bool _isListeningForConfigurationChanges;
    private bool _isReloadingConfigurations;

    public TestDataView()
    {
        InitializeComponent();

        Records = new ObservableCollection<TestDataRecord>(CreateRows());
        DataContext = this;

        Loaded += TestDataView_Loaded;
        Unloaded += TestDataView_Unloaded;
        ReloadConfigurations();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<TestDataRecord> Records { get; }

    public ObservableCollection<TestDataGridConfiguration> Configurations => _catalog.Configurations;

    // 备注：测试数据页切换配置时立即重建列，并记住用户选择。
    public TestDataGridConfiguration? SelectedConfiguration
    {
        get => _selectedConfiguration;
        set
        {
            if (ReferenceEquals(_selectedConfiguration, value))
            {
                return;
            }

            _selectedConfiguration = value;
            OnPropertyChanged();
            BuildColumns();

            if (!_isReloadingConfigurations && _selectedConfiguration is not null)
            {
                // 备注：下拉框切换展示配置时只更新启用 ID，不会改动列内容。
                TestDataGridConfigurationStore.SaveSelectedConfigurationId(_selectedConfiguration.Id);
                SetPageStatus($"已切换展示配置：{_selectedConfiguration.Name}", SuccessBrush);
            }
        }
    }

    public string PageStatusText
    {
        get => _pageStatusText;
        private set => SetField(ref _pageStatusText, value);
    }

    public Brush PageStatusBrush
    {
        get => _pageStatusBrush;
        private set => SetField(ref _pageStatusBrush, value);
    }

    private void TestDataView_Loaded(object sender, RoutedEventArgs e)
    {
        // 备注：订阅配置页保存/启用事件，使测试数据页能自动刷新列。
        if (!_isListeningForConfigurationChanges)
        {
            TestDataGridConfigurationStore.ConfigurationSaved += ConfigurationStore_ConfigurationSaved;
            _isListeningForConfigurationChanges = true;
        }

        ReloadConfigurations();
    }

    private void TestDataView_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_isListeningForConfigurationChanges)
        {
            TestDataGridConfigurationStore.ConfigurationSaved -= ConfigurationStore_ConfigurationSaved;
            _isListeningForConfigurationChanges = false;
        }
    }

    private void ConfigurationStore_ConfigurationSaved(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(ReloadConfigurations);
    }

    private void RefreshConfigButton_Click(object sender, RoutedEventArgs e)
    {
        ReloadConfigurations();
    }

    private void RefreshDataButton_Click(object sender, RoutedEventArgs e)
    {
        Records.Clear();
        foreach (TestDataRecord record in CreateRows())
        {
            Records.Add(record);
        }

        SetPageStatus($"已刷新 {Records.Count} 条测试数据。", SuccessBrush);
    }

    private void ReloadConfigurations()
    {
        // 备注：重新读取配置目录后，默认选中当前已启用配置。
        _isReloadingConfigurations = true;
        _catalog = TestDataGridConfigurationStore.LoadCatalog();
        OnPropertyChanged(nameof(Configurations));
        SelectedConfiguration = _catalog.SelectedConfiguration;
        _isReloadingConfigurations = false;

        SetPageStatus(
            SelectedConfiguration is null
                ? "未找到可用数据配置。"
                : $"已加载配置：{SelectedConfiguration.Name}，显示 {TestDataGrid.Columns.Count} 列。",
            SuccessBrush);
    }

    private void BuildColumns()
    {
        // 备注：表格列完全来自 SelectedConfiguration.Columns，顺序也按配置保存的顺序。
        if (TestDataGrid is null)
        {
            return;
        }

        TestDataGrid.Columns.Clear();
        if (SelectedConfiguration is null)
        {
            return;
        }

        foreach (TestDataGridColumnConfig column in SelectedConfiguration.Columns.Where(column => column.IsVisible))
        {
            TestDataGrid.Columns.Add(CreateColumn(column));
        }
    }

    private static DataGridTextColumn CreateColumn(TestDataGridColumnConfig column)
    {
        // 备注：运行时动态创建 DataGridTextColumn，用配置的 BindingPath 绑定模型属性。
        Binding binding = new(column.BindingPath)
        {
            StringFormat = GetStringFormat(column.BindingPath)
        };

        return new DataGridTextColumn
        {
            Header = string.IsNullOrWhiteSpace(column.ColumnName) ? column.BindingPath : column.ColumnName,
            Width = new DataGridLength(column.Width),
            Binding = binding
        };
    }

    private static string? GetStringFormat(string bindingPath)
    {
        Type? propertyType = typeof(TestDataRecord).GetProperty(bindingPath)?.PropertyType;
        Type? type = Nullable.GetUnderlyingType(propertyType ?? typeof(string)) ?? propertyType;

        if (type == typeof(DateTime))
        {
            return "{0:yyyy-MM-dd HH:mm:ss}";
        }

        if (type == typeof(double) || type == typeof(float) || type == typeof(decimal))
        {
            return "{0:0.###}";
        }

        return null;
    }

    private static IEnumerable<TestDataRecord> CreateRows()
    {
        // 备注：当前先生成本地模拟测试数据，后续可替换为真实数据源。
        DateTime baseTime = DateTime.Now.Date.AddHours(8);
        string[] items = ["电压", "电流", "气密", "扫码", "通信"];
        string[] stations = ["FCT-01", "FCT-02", "ICT-01"];

        for (int index = 1; index <= 24; index++)
        {
            string item = items[(index - 1) % items.Length];
            bool isFail = index % 9 == 0;
            double lowerLimit = item == "气密" ? 1.5 : 0.3;
            double upperLimit = item == "气密" ? 2.0 : 3.5;
            double value = isFail ? upperLimit + 0.18 : lowerLimit + ((index % 6) * 0.37);
            DateTime start = baseTime.AddMinutes(index * 3);

            yield return new TestDataRecord
            {
                Index = index,
                Barcode = $"SN20260430{index:0000}",
                WorkOrder = $"WO-260430-{(index <= 12 ? "A" : "B")}",
                ProductModel = index <= 12 ? "A100" : "B200",
                StationName = stations[(index - 1) % stations.Length],
                TestItem = item,
                TestValue = value,
                LowerLimit = lowerLimit,
                UpperLimit = upperLimit,
                Result = isFail ? "FAIL" : "PASS",
                OperatorName = $"OP{(index % 4) + 1:000}",
                StartTime = start,
                EndTime = start.AddMilliseconds(900 + index * 37),
                DurationMilliseconds = 900 + index * 37,
                EquipmentCode = $"EQ-{stations[(index - 1) % stations.Length]}",
                ErrorCode = isFail ? "LIMIT-01" : string.Empty,
                Remarks = isFail ? "待复测" : string.Empty
            };
        }
    }

    private void SetPageStatus(string text, Brush brush)
    {
        PageStatusText = text;
        PageStatusBrush = brush;
    }

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

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
