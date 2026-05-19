using ControlLibrary;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Media;
using WpfApp.Models.DataManagement;
using WpfApp.Services.DataManagement;

namespace WpfApp.ViewModels;

public sealed class TestDataViewModel : ViewModelProperties
{
    private static readonly Brush SuccessBrush =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16A34A"));

    private static readonly Brush NeutralBrush =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));

    private TestDataGridConfigurationCatalog _catalog =
        TestDataGridConfigurationCatalog.CreateDefault(TestDataGridConfigurationStore.BindingOptions);
    private TestDataGridConfiguration? _selectedConfiguration;
    private string _pageStatusText = "等待加载";
    private Brush _pageStatusBrush = NeutralBrush;
    private bool _isListeningForConfigurationChanges;
    private bool _isReloadingConfigurations;

    public TestDataViewModel()
    {
        Records = new ObservableCollection<TestDataRecord>(CreateRows());
        RefreshConfigCommand = new RelayCommand(_ => ReloadConfigurations());
        RefreshDataCommand = new RelayCommand(_ => RefreshData());
    }

    public ObservableCollection<TestDataRecord> Records { get; }

    public ObservableCollection<TestDataGridConfiguration> Configurations => _catalog.Configurations;

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

            if (!_isReloadingConfigurations && _selectedConfiguration is not null)
            {
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

    public ICommand RefreshConfigCommand { get; }

    public ICommand RefreshDataCommand { get; }

    public void Load()
    {
        if (!_isListeningForConfigurationChanges)
        {
            TestDataGridConfigurationStore.ConfigurationSaved += ConfigurationStore_ConfigurationSaved;
            _isListeningForConfigurationChanges = true;
        }

        ReloadConfigurations();
    }

    public void Unload()
    {
        if (_isListeningForConfigurationChanges)
        {
            TestDataGridConfigurationStore.ConfigurationSaved -= ConfigurationStore_ConfigurationSaved;
            _isListeningForConfigurationChanges = false;
        }
    }

    private void ConfigurationStore_ConfigurationSaved(object? sender, EventArgs e)
    {
        ReloadConfigurations();
    }

    private void RefreshData()
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
        _isReloadingConfigurations = true;
        _catalog = TestDataGridConfigurationStore.LoadCatalog();
        OnPropertyChanged(nameof(Configurations));
        SelectedConfiguration = _catalog.SelectedConfiguration;
        _isReloadingConfigurations = false;

        SetPageStatus(
            SelectedConfiguration is null
                ? "未找到可用数据配置。"
                : $"已加载配置：{SelectedConfiguration.Name}。",
            SuccessBrush);
    }

    private static IEnumerable<TestDataRecord> CreateRows()
    {
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
}
