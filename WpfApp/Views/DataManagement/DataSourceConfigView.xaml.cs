using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WpfApp.Models.DataManagement;
using WpfApp.Services.DataManagement;

namespace WpfApp.Views.DataManagement;

/// <summary>
/// 数据源配置页：维护多套 GridView 列配置，并通过“启用配置”决定测试数据页默认使用哪套。
/// </summary>
public partial class DataSourceConfigView : UserControl, INotifyPropertyChanged
{
    private static readonly Brush SuccessBrush =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16A34A"));

    private static readonly Brush WarningBrush =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EA580C"));

    private static readonly Brush NeutralBrush =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));

    private const double PreviewDrawerClosedOffset = 56d;
    private static readonly Duration PreviewDrawerAnimationDuration = new(TimeSpan.FromMilliseconds(220));
    private static readonly IEasingFunction PreviewDrawerEasing = new CubicEase { EasingMode = EasingMode.EaseOut };

    private TestDataGridConfigurationCatalog _catalog =
        TestDataGridConfigurationCatalog.CreateDefault(TestDataGridConfigurationStore.BindingOptions);

    // 备注：SelectedConfiguration 是当前正在编辑的配置，不等同于已启用配置。
    private TestDataGridConfiguration? _selectedConfiguration;
    private TestDataGridColumnConfig? _selectedColumn;
    private bool _isPreviewDrawerOpen;
    private string _pageStatusText = "等待编辑";
    private Brush _pageStatusBrush = NeutralBrush;

    public DataSourceConfigView()
    {
        InitializeComponent();

        BindingOptions = TestDataGridConfigurationStore.BindingOptions;
        PreviewRows = new ObservableCollection<TestDataRecord>(CreatePreviewRows());

        DataContext = this;
        LoadCatalog(TestDataGridConfigurationStore.LoadCatalog());
        UpdatePreviewDrawerVisual(animate: false);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<GridBindingOption> BindingOptions { get; }

    public ObservableCollection<TestDataRecord> PreviewRows { get; }

    public ObservableCollection<TestDataGridConfiguration> Configurations => _catalog.Configurations;

    // 备注：切换编辑配置时只切换界面编辑对象，不修改 _catalog.SelectedConfigurationId。
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
            BuildPreviewColumns();
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

    private void AddConfigurationButton_Click(object sender, RoutedEventArgs e)
    {
        // 备注：新增配置默认包含 TestDataRecord 的全部可绑定字段。
        TestDataGridConfiguration configuration =
            TestDataGridConfiguration.CreateDefault(BindingOptions, GenerateUniqueConfigurationName("数据配置"));

        Configurations.Add(configuration);
        SelectedConfiguration = configuration;
        SetPageStatus($"已新增数据配置：{configuration.Name}", SuccessBrush);
    }

    private void DuplicateConfigurationButton_Click(object sender, RoutedEventArgs e)
    {
        // 备注：复制当前配置用于快速创建相近的显示方案。
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

    private void DeleteConfigurationButton_Click(object sender, RoutedEventArgs e)
    {
        // 备注：删除只影响配置目录；如果删除的是已启用配置，保存时会自动启用第一套配置。
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

    private void AddColumnButton_Click(object sender, RoutedEventArgs e)
    {
        // 备注：新增列优先使用当前配置里还没用过的模型字段。
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

    private void DeleteColumnButton_Click(object sender, RoutedEventArgs e)
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

    private void MoveUpButton_Click(object sender, RoutedEventArgs e)
    {
        MoveSelectedColumn(-1);
    }

    private void MoveDownButton_Click(object sender, RoutedEventArgs e)
    {
        MoveSelectedColumn(1);
    }

    private void OpenPreviewDrawerButton_Click(object sender, RoutedEventArgs e)
    {
        OpenPreviewDrawer();
    }

    private void ClosePreviewDrawerButton_Click(object sender, RoutedEventArgs e)
    {
        ClosePreviewDrawer();
    }

    private void PreviewDrawerBackdrop_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        ClosePreviewDrawer();
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        // 备注：重置只影响当前正在编辑的配置，不会删除其它数据配置。
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
        BuildPreviewColumns();
        SetPageStatus($"已按测试数据模型重置：{SelectedConfiguration.Name}", SuccessBrush);
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        // 备注：保存只持久化配置内容，不把当前配置设为启用配置。
        string? editingConfigurationId = SelectedConfiguration?.Id;

        TestDataGridConfigurationStore.SaveCatalog(_catalog);
        LoadCatalog(TestDataGridConfigurationStore.LoadCatalog(), editingConfigurationId);
        SetPageStatus("已保存全部数据配置。", SuccessBrush);
    }

    private void EnableConfigurationButton_Click(object sender, RoutedEventArgs e)
    {
        // 备注：只有这里会修改 SelectedConfigurationId，测试数据页默认展示随之改变。
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

    private void MoveSelectedColumn(int offset)
    {
        // 备注：通过 ObservableCollection.Move 调整列顺序，保存后展示页按同一顺序生成列。
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
        ColumnsDataGrid.ScrollIntoView(SelectedColumn);
        SetPageStatus($"已调整列顺序：{SelectedColumn.ColumnName}", NeutralBrush);
    }

    private void OpenPreviewDrawer()
    {
        _isPreviewDrawerOpen = true;
        BuildPreviewColumns();
        UpdatePreviewDrawerVisual(animate: true);
    }

    private void ClosePreviewDrawer()
    {
        _isPreviewDrawerOpen = false;
        UpdatePreviewDrawerVisual(animate: true);
    }

    private void UpdatePreviewDrawerVisual(bool animate)
    {
        if (PreviewDrawerHost is null || PreviewDrawerTranslateTransform is null)
        {
            return;
        }

        double targetOpacity = _isPreviewDrawerOpen ? 1d : 0d;
        double targetOffset = _isPreviewDrawerOpen ? 0d : PreviewDrawerClosedOffset;

        if (_isPreviewDrawerOpen)
        {
            PreviewDrawerHost.IsHitTestVisible = true;
        }

        if (!animate)
        {
            PreviewDrawerHost.BeginAnimation(UIElement.OpacityProperty, null);
            PreviewDrawerTranslateTransform.BeginAnimation(TranslateTransform.YProperty, null);
            PreviewDrawerHost.Opacity = targetOpacity;
            PreviewDrawerTranslateTransform.Y = targetOffset;
            PreviewDrawerHost.IsHitTestVisible = _isPreviewDrawerOpen;
            return;
        }

        DoubleAnimation opacityAnimation = new()
        {
            To = targetOpacity,
            Duration = PreviewDrawerAnimationDuration,
            EasingFunction = PreviewDrawerEasing
        };

        if (!_isPreviewDrawerOpen)
        {
            opacityAnimation.Completed += (_, _) =>
            {
                if (!_isPreviewDrawerOpen)
                {
                    PreviewDrawerHost.IsHitTestVisible = false;
                }
            };
        }

        DoubleAnimation translateAnimation = new()
        {
            To = targetOffset,
            Duration = PreviewDrawerAnimationDuration,
            EasingFunction = PreviewDrawerEasing
        };

        PreviewDrawerHost.BeginAnimation(UIElement.OpacityProperty, opacityAnimation);
        PreviewDrawerTranslateTransform.BeginAnimation(TranslateTransform.YProperty, translateAnimation);
    }

    private void LoadCatalog(TestDataGridConfigurationCatalog catalog, string? preferredSelectionId = null)
    {
        // 备注：重新读取配置后，优先保持用户刚才正在编辑的那套配置。
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
        // 备注：避免新增列时重复绑定同一个字段；字段用完后允许再次选择第一个字段。
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

    private void SelectedConfiguration_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        NotifyConfigurationSummaryChanged();
        if (e.PropertyName == nameof(TestDataGridConfiguration.Columns))
        {
            BuildPreviewColumns();
        }
    }

    private void HookColumns(ObservableCollection<TestDataGridColumnConfig> columns)
    {
        // 备注：监听列集合和列属性变化，实时刷新右侧预览表格。
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

        BuildPreviewColumns();
        NotifyConfigurationSummaryChanged();
        OnPropertyChanged(nameof(SelectedColumnSummary));
    }

    private void Column_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        BuildPreviewColumns();
        NotifyConfigurationSummaryChanged();
        if (ReferenceEquals(sender, SelectedColumn))
        {
            OnPropertyChanged(nameof(SelectedColumnSummary));
        }
    }

    private void BuildPreviewColumns()
    {
        // 备注：预览表格不使用 AutoGenerateColumns，而是完全按配置动态创建列。
        if (PreviewDataGrid is null)
        {
            return;
        }

        PreviewDataGrid.Columns.Clear();
        if (SelectedConfiguration is null)
        {
            return;
        }

        foreach (TestDataGridColumnConfig column in SelectedConfiguration.Columns.Where(column => column.IsVisible))
        {
            PreviewDataGrid.Columns.Add(CreatePreviewColumn(column));
        }
    }

    private static DataGridTextColumn CreatePreviewColumn(TestDataGridColumnConfig column)
    {
        // 备注：BindingPath 是模型属性名，列头使用用户配置的列名。
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
