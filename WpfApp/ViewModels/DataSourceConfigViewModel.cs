using ControlLibrary;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Windows.Input;
using System.Windows.Media;
using WpfApp.Models.DataManagement;
using WpfApp.Services.DataManagement;

namespace WpfApp.ViewModels;

public sealed class DataSourceConfigViewModel : ViewModelProperties
{
    private static readonly Brush SuccessBrush =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16A34A"));

    private static readonly Brush WarningBrush =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EA580C"));

    private static readonly Brush NeutralBrush =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));

    private TestDataGridConfigurationCatalog _catalog =
        TestDataGridConfigurationCatalog.CreateDefault(TestDataGridConfigurationStore.BindingOptions);
    private TestDataGridConfiguration? _selectedConfiguration;
    private TestDataGridColumnConfig? _selectedColumn;
    private bool _isPreviewDrawerOpen;
    private string _pageStatusText = "等待编辑";
    private Brush _pageStatusBrush = NeutralBrush;

    public DataSourceConfigViewModel()
    {
        BindingOptions = TestDataGridConfigurationStore.BindingOptions;
        PreviewRows = new ObservableCollection<TestDataRecord>(CreatePreviewRows());

        AddConfigurationCommand = new RelayCommand(_ => AddConfiguration());
        DuplicateConfigurationCommand = new RelayCommand(_ => DuplicateConfiguration());
        DeleteConfigurationCommand = new RelayCommand(_ => DeleteConfiguration());
        SaveCommand = new RelayCommand(_ => Save());
        EnableConfigurationCommand = new RelayCommand(_ => EnableConfiguration());
        AddColumnCommand = new RelayCommand(_ => AddColumn());
        DeleteColumnCommand = new RelayCommand(_ => DeleteColumn());
        MoveUpCommand = new RelayCommand(_ => MoveSelectedColumn(-1));
        MoveDownCommand = new RelayCommand(_ => MoveSelectedColumn(1));
        ResetCommand = new RelayCommand(_ => Reset());
        OpenPreviewDrawerCommand = new RelayCommand(_ => OpenPreviewDrawer());
        ClosePreviewDrawerCommand = new RelayCommand(_ => ClosePreviewDrawer());

        LoadCatalog(TestDataGridConfigurationStore.LoadCatalog());
    }

    public event EventHandler? PreviewColumnsChanged;

    public event EventHandler? PreviewDrawerStateChanged;

    public event EventHandler? SelectedColumnScrollRequested;

    public IReadOnlyList<GridBindingOption> BindingOptions { get; }

    public ObservableCollection<TestDataRecord> PreviewRows { get; }

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

            if (_selectedConfiguration is not null)
            {
                _selectedConfiguration.PropertyChanged -= SelectedConfiguration_PropertyChanged;
                UnhookColumns(_selectedConfiguration.Columns);
            }

            _selectedConfiguration = value;
            if (_selectedConfiguration is not null)
            {
                _selectedConfiguration.PropertyChanged += SelectedConfiguration_PropertyChanged;
                HookColumns(_selectedConfiguration.Columns);
            }

            SelectedColumn = _selectedConfiguration?.Columns.FirstOrDefault();
            OnPropertyChanged();
            NotifyConfigurationSummaryChanged();
            RequestPreviewColumnsChanged();
        }
    }

    public TestDataGridColumnConfig? SelectedColumn
    {
        get => _selectedColumn;
        set
        {
            if (SetField(ref _selectedColumn, value))
            {
                OnPropertyChanged(nameof(SelectedColumnSummary));
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

    public string CurrentConfigurationDescription =>
        SelectedConfiguration is null
            ? "请选择一套数据配置后再编辑列定义。"
            : "保存会持久化当前列定义；启用当前配置后，测试数据页会默认按这套方案展示。";

    public string SelectedConfigurationSummary
    {
        get
        {
            if (SelectedConfiguration is null)
            {
                return "未选择数据配置。";
            }

            int totalCount = SelectedConfiguration.Columns.Count;
            int visibleCount = SelectedConfiguration.Columns.Count(column => column.IsVisible);
            int hiddenCount = totalCount - visibleCount;
            return $"{SelectedConfiguration.Name}：共 {totalCount} 列，显示 {visibleCount} 列，隐藏 {hiddenCount} 列";
        }
    }

    public string EnabledConfigurationText =>
        _catalog.SelectedConfiguration is null
            ? "当前启用：未设置"
            : $"当前启用：{_catalog.SelectedConfiguration.Name}";

    public string VisibleColumnCountText
    {
        get
        {
            if (SelectedConfiguration is null)
            {
                return "预览 0 列";
            }

            int visibleCount = SelectedConfiguration.Columns.Count(column => column.IsVisible);
            return $"预览 {visibleCount} 列";
        }
    }

    public string SelectedColumnSummary
    {
        get
        {
            if (SelectedColumn is null)
            {
                return "未选择列。";
            }

            GridBindingOption? option = BindingOptions.FirstOrDefault(bindingOption =>
                string.Equals(bindingOption.PropertyName, SelectedColumn.BindingPath, StringComparison.Ordinal));
            string bindingText = option is null
                ? SelectedColumn.BindingPath
                : $"{option.DisplayName} ({option.PropertyName})";
            string visibleText = SelectedColumn.IsVisible ? "显示" : "隐藏";

            return $"{SelectedColumn.ColumnName} / {bindingText} / {SelectedColumn.Width:0}px / {visibleText}";
        }
    }

    public ICommand AddConfigurationCommand { get; }

    public ICommand DuplicateConfigurationCommand { get; }

    public ICommand DeleteConfigurationCommand { get; }

    public ICommand SaveCommand { get; }

    public ICommand EnableConfigurationCommand { get; }

    public ICommand AddColumnCommand { get; }

    public ICommand DeleteColumnCommand { get; }

    public ICommand MoveUpCommand { get; }

    public ICommand MoveDownCommand { get; }

    public ICommand ResetCommand { get; }

    public ICommand OpenPreviewDrawerCommand { get; }

    public ICommand ClosePreviewDrawerCommand { get; }

    private void AddConfiguration()
    {
        TestDataGridConfiguration configuration =
            TestDataGridConfiguration.CreateDefault(BindingOptions, GenerateUniqueConfigurationName("数据配置"));

        Configurations.Add(configuration);
        SelectedConfiguration = configuration;
        SetPageStatus($"已新增数据配置：{configuration.Name}", SuccessBrush);
    }

    private void DuplicateConfiguration()
    {
        if (SelectedConfiguration is null)
        {
            SetPageStatus("请先选择需要复制的数据配置。", WarningBrush);
            return;
        }

        TestDataGridConfiguration copy =
            SelectedConfiguration.Clone(GenerateCopyConfigurationName(SelectedConfiguration.Name));

        Configurations.Add(copy);
        SelectedConfiguration = copy;
        SetPageStatus($"已复制数据配置：{copy.Name}", SuccessBrush);
    }

    private void DeleteConfiguration()
    {
        if (SelectedConfiguration is null)
        {
            SetPageStatus("请先选择需要删除的数据配置。", WarningBrush);
            return;
        }

        if (Configurations.Count <= 1)
        {
            SetPageStatus("至少需要保留一个数据配置。", WarningBrush);
            return;
        }

        int index = Configurations.IndexOf(SelectedConfiguration);
        string configurationName = SelectedConfiguration.Name;
        TestDataGridConfiguration deletedConfiguration = SelectedConfiguration;
        Configurations.Remove(deletedConfiguration);
        SelectedConfiguration = Configurations[Math.Clamp(index, 0, Configurations.Count - 1)];
        SetPageStatus($"已删除数据配置：{configurationName}", WarningBrush);
    }

    private void AddColumn()
    {
        if (SelectedConfiguration is null)
        {
            SetPageStatus("请先选择一个数据配置。", WarningBrush);
            return;
        }

        GridBindingOption option = FindFirstUnusedOption() ?? BindingOptions.First();
        TestDataGridColumnConfig column = new()
        {
            ColumnName = option.DisplayName,
            BindingPath = option.PropertyName,
            IsVisible = true,
            Width = option.PropertyType == typeof(DateTime) ? 180d : 150d
        };

        SelectedConfiguration.Columns.Add(column);
        SelectedColumn = column;
        SetPageStatus($"已新增列：{column.ColumnName}", SuccessBrush);
    }

    private void DeleteColumn()
    {
        if (SelectedConfiguration is null || SelectedColumn is null)
        {
            SetPageStatus("请先选择需要删除的列。", WarningBrush);
            return;
        }

        int index = SelectedConfiguration.Columns.IndexOf(SelectedColumn);
        string columnName = SelectedColumn.ColumnName;
        SelectedConfiguration.Columns.Remove(SelectedColumn);
        SelectedColumn = SelectedConfiguration.Columns.Count == 0
            ? null
            : SelectedConfiguration.Columns[Math.Clamp(index, 0, SelectedConfiguration.Columns.Count - 1)];
        SetPageStatus($"已删除列：{columnName}", WarningBrush);
    }

    private void MoveSelectedColumn(int offset)
    {
        if (SelectedConfiguration is null || SelectedColumn is null)
        {
            SetPageStatus("请先选择需要调整顺序的列。", WarningBrush);
            return;
        }

        int oldIndex = SelectedConfiguration.Columns.IndexOf(SelectedColumn);
        int newIndex = oldIndex + offset;
        if (oldIndex < 0 || newIndex < 0 || newIndex >= SelectedConfiguration.Columns.Count)
        {
            return;
        }

        SelectedConfiguration.Columns.Move(oldIndex, newIndex);
        SelectedColumn = SelectedConfiguration.Columns[newIndex];
        SelectedColumnScrollRequested?.Invoke(this, EventArgs.Empty);
        SetPageStatus($"已调整列顺序：{SelectedColumn.ColumnName}", NeutralBrush);
    }

    private void Reset()
    {
        if (SelectedConfiguration is null)
        {
            SetPageStatus("请先选择需要重置的数据配置。", WarningBrush);
            return;
        }

        UnhookColumns(SelectedConfiguration.Columns);
        TestDataGridConfigurationStore.ResetColumnsToModelFields(SelectedConfiguration);
        HookColumns(SelectedConfiguration.Columns);
        SelectedColumn = SelectedConfiguration.Columns.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedConfiguration));
        RequestPreviewColumnsChanged();
        SetPageStatus($"已按测试数据模型重置：{SelectedConfiguration.Name}", SuccessBrush);
    }

    private void Save()
    {
        string? editingConfigurationId = SelectedConfiguration?.Id;

        TestDataGridConfigurationStore.SaveCatalog(_catalog);
        LoadCatalog(TestDataGridConfigurationStore.LoadCatalog(), editingConfigurationId);
        SetPageStatus("已保存全部数据配置。", SuccessBrush);
    }

    private void EnableConfiguration()
    {
        if (SelectedConfiguration is null)
        {
            SetPageStatus("请先选择需要启用的数据配置。", WarningBrush);
            return;
        }

        _catalog.SelectedConfigurationId = SelectedConfiguration.Id;
        TestDataGridConfigurationStore.SaveCatalog(_catalog);
        LoadCatalog(TestDataGridConfigurationStore.LoadCatalog(), SelectedConfiguration.Id);
        SetPageStatus($"已启用数据配置：{SelectedConfiguration.Name}", SuccessBrush);
    }

    private void OpenPreviewDrawer()
    {
        _isPreviewDrawerOpen = true;
        RequestPreviewColumnsChanged();
        PreviewDrawerStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ClosePreviewDrawer()
    {
        _isPreviewDrawerOpen = false;
        PreviewDrawerStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void LoadCatalog(TestDataGridConfigurationCatalog catalog, string? preferredSelectionId = null)
    {
        if (SelectedConfiguration is not null)
        {
            UnhookColumns(SelectedConfiguration.Columns);
        }

        _catalog = catalog;
        OnPropertyChanged(nameof(Configurations));
        SelectedConfiguration =
            _catalog.Configurations.FirstOrDefault(configuration => configuration.Id == preferredSelectionId) ??
            _catalog.SelectedConfiguration;
    }

    private GridBindingOption? FindFirstUnusedOption()
    {
        HashSet<string> usedBindings = SelectedConfiguration?.Columns
            .Select(column => column.BindingPath)
            .ToHashSet(StringComparer.Ordinal) ?? new HashSet<string>(StringComparer.Ordinal);

        return BindingOptions.FirstOrDefault(option => !usedBindings.Contains(option.PropertyName));
    }

    private string GenerateUniqueConfigurationName(string prefix)
    {
        for (int index = 1; ; index++)
        {
            string name = $"{prefix} {index}";
            if (!Configurations.Any(configuration => string.Equals(configuration.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                return name;
            }
        }
    }

    private string GenerateCopyConfigurationName(string sourceName)
    {
        string baseName = string.IsNullOrWhiteSpace(sourceName) ? "数据配置" : sourceName.Trim();
        string copyName = $"{baseName} 副本";
        if (!Configurations.Any(configuration => string.Equals(configuration.Name, copyName, StringComparison.OrdinalIgnoreCase)))
        {
            return copyName;
        }

        for (int index = 2; ; index++)
        {
            string name = $"{copyName} {index}";
            if (!Configurations.Any(configuration => string.Equals(configuration.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                return name;
            }
        }
    }

    private void SelectedConfiguration_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        NotifyConfigurationSummaryChanged();
        if (e.PropertyName == nameof(TestDataGridConfiguration.Columns))
        {
            RequestPreviewColumnsChanged();
        }
    }

    private void HookColumns(ObservableCollection<TestDataGridColumnConfig> columns)
    {
        columns.CollectionChanged += Columns_CollectionChanged;
        foreach (TestDataGridColumnConfig column in columns)
        {
            column.PropertyChanged += Column_PropertyChanged;
        }
    }

    private void UnhookColumns(ObservableCollection<TestDataGridColumnConfig> columns)
    {
        columns.CollectionChanged -= Columns_CollectionChanged;
        foreach (TestDataGridColumnConfig column in columns)
        {
            column.PropertyChanged -= Column_PropertyChanged;
        }
    }

    private void Columns_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (TestDataGridColumnConfig column in e.OldItems)
            {
                column.PropertyChanged -= Column_PropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (TestDataGridColumnConfig column in e.NewItems)
            {
                column.PropertyChanged += Column_PropertyChanged;
            }
        }

        RequestPreviewColumnsChanged();
        NotifyConfigurationSummaryChanged();
        OnPropertyChanged(nameof(SelectedColumnSummary));
    }

    private void Column_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        RequestPreviewColumnsChanged();
        NotifyConfigurationSummaryChanged();
        if (ReferenceEquals(sender, SelectedColumn))
        {
            OnPropertyChanged(nameof(SelectedColumnSummary));
        }
    }

    private static IEnumerable<TestDataRecord> CreatePreviewRows()
    {
        DateTime now = DateTime.Now;
        return new[]
        {
            new TestDataRecord
            {
                Index = 1,
                Barcode = "SN202604300001",
                WorkOrder = "WO-260430-A",
                ProductModel = "A100",
                StationName = "FCT-01",
                TestItem = "电压",
                TestValue = 3.31,
                UpperLimit = 3.5,
                LowerLimit = 3.1,
                Result = "PASS",
                OperatorName = "OP001",
                StartTime = now.AddMinutes(-11),
                EndTime = now.AddMinutes(-10),
                DurationMilliseconds = 1240,
                EquipmentCode = "EQ-FCT-01",
                ErrorCode = string.Empty,
                Remarks = "首件"
            },
            new TestDataRecord
            {
                Index = 2,
                Barcode = "SN202604300002",
                WorkOrder = "WO-260430-A",
                ProductModel = "A100",
                StationName = "FCT-01",
                TestItem = "电流",
                TestValue = 0.48,
                UpperLimit = 0.6,
                LowerLimit = 0.3,
                Result = "PASS",
                OperatorName = "OP001",
                StartTime = now.AddMinutes(-9),
                EndTime = now.AddMinutes(-8),
                DurationMilliseconds = 1186,
                EquipmentCode = "EQ-FCT-01",
                ErrorCode = string.Empty,
                Remarks = string.Empty
            },
            new TestDataRecord
            {
                Index = 3,
                Barcode = "SN202604300003",
                WorkOrder = "WO-260430-A",
                ProductModel = "A100",
                StationName = "FCT-02",
                TestItem = "气密",
                TestValue = 1.87,
                UpperLimit = 2,
                LowerLimit = 1.5,
                Result = "FAIL",
                OperatorName = "OP002",
                StartTime = now.AddMinutes(-7),
                EndTime = now.AddMinutes(-6),
                DurationMilliseconds = 1560,
                EquipmentCode = "EQ-FCT-02",
                ErrorCode = "LEAK-02",
                Remarks = "待复测"
            }
        };
    }

    private void NotifyConfigurationSummaryChanged()
    {
        OnPropertyChanged(nameof(CurrentConfigurationDescription));
        OnPropertyChanged(nameof(SelectedConfigurationSummary));
        OnPropertyChanged(nameof(EnabledConfigurationText));
        OnPropertyChanged(nameof(VisibleColumnCountText));
    }

    private void RequestPreviewColumnsChanged()
    {
        PreviewColumnsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetPageStatus(string text, Brush brush)
    {
        PageStatusText = text;
        PageStatusBrush = brush;
    }

    public bool IsPreviewDrawerOpen => _isPreviewDrawerOpen;
}
