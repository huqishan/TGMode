using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ControlLibrary.Controls.TestDataTable.Control;

public partial class TestDataTable : UserControl
{
    private const double MinFieldWidth = 100;
    private const double ResultWidth = 80;
    private const int FlexibleColumnCount = 4;
    private const double MinimumTableWidth = MinFieldWidth * FlexibleColumnCount + ResultWidth;
    private const double HeaderHeight = 30;
    private const double RowHeight = 32;
    private const double WorkStepMaxHeight = 400;
    private const int MaxVisibleMergedRows = (int)(WorkStepMaxHeight / RowHeight);
    private double _lastLayoutWidth;

    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(
            nameof(ItemsSource),
            typeof(IEnumerable),
            typeof(TestDataTable),
            new PropertyMetadata(null, OnItemsSourceChanged));

    public static readonly DependencyProperty WorkStepPathProperty =
        DependencyProperty.Register(
            nameof(WorkStepPath),
            typeof(string),
            typeof(TestDataTable),
            new PropertyMetadata("WorkStep", OnPathChanged));

    public static readonly DependencyProperty NamePathProperty =
        DependencyProperty.Register(
            nameof(NamePath),
            typeof(string),
            typeof(TestDataTable),
            new PropertyMetadata("Name", OnPathChanged));

    public static readonly DependencyProperty TestValuePathProperty =
        DependencyProperty.Register(
            nameof(TestValuePath),
            typeof(string),
            typeof(TestDataTable),
            new PropertyMetadata("TestValue", OnPathChanged));

    public static readonly DependencyProperty JudgmentConditionPathProperty =
        DependencyProperty.Register(
            nameof(JudgmentConditionPath),
            typeof(string),
            typeof(TestDataTable),
            new PropertyMetadata("JudgmentCondition", OnPathChanged));

    public static readonly DependencyProperty ResultPathProperty =
        DependencyProperty.Register(
            nameof(ResultPath),
            typeof(string),
            typeof(TestDataTable),
            new PropertyMetadata("Result", OnPathChanged));

    public static readonly DependencyProperty WorkStepElapsedTimePathProperty =
        DependencyProperty.Register(
            nameof(WorkStepElapsedTimePath),
            typeof(string),
            typeof(TestDataTable),
            new PropertyMetadata("WorkStepElapsedTime", OnPathChanged));

    private static readonly Brush FailureBrush =
        (Brush)new BrushConverter().ConvertFromString("#D14343")!;

    private INotifyCollectionChanged? _collectionChangedSource;

    public TestDataTable()
    {
        InitializeComponent();
        if (DesignerProperties.GetIsInDesignMode(this))
        {
            return;
        }

        SizeChanged += TestDataTable_SizeChanged;
        BuildTable();
    }

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public string WorkStepPath
    {
        get => (string?)GetValue(WorkStepPathProperty) ?? string.Empty;
        set => SetValue(WorkStepPathProperty, value ?? string.Empty);
    }

    public string NamePath
    {
        get => (string?)GetValue(NamePathProperty) ?? string.Empty;
        set => SetValue(NamePathProperty, value ?? string.Empty);
    }

    public string TestValuePath
    {
        get => (string?)GetValue(TestValuePathProperty) ?? string.Empty;
        set => SetValue(TestValuePathProperty, value ?? string.Empty);
    }

    public string JudgmentConditionPath
    {
        get => (string?)GetValue(JudgmentConditionPathProperty) ?? string.Empty;
        set => SetValue(JudgmentConditionPathProperty, value ?? string.Empty);
    }

    public string ResultPath
    {
        get => (string?)GetValue(ResultPathProperty) ?? string.Empty;
        set => SetValue(ResultPathProperty, value ?? string.Empty);
    }

    public string WorkStepElapsedTimePath
    {
        get => (string?)GetValue(WorkStepElapsedTimePathProperty) ?? string.Empty;
        set => SetValue(WorkStepElapsedTimePathProperty, value ?? string.Empty);
    }

    private static void OnItemsSourceChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is TestDataTable table)
        {
            if (DesignerProperties.GetIsInDesignMode(table))
            {
                return;
            }

            table.UnhookCollectionChanged();
            table.HookCollectionChanged(e.NewValue as INotifyCollectionChanged);
            table.BuildTable();
        }
    }

    private static void OnPathChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is TestDataTable table)
        {
            if (DesignerProperties.GetIsInDesignMode(table))
            {
                return;
            }

            table.BuildTable();
        }
    }

    private void HookCollectionChanged(INotifyCollectionChanged? collection)
    {
        _collectionChangedSource = collection;
        if (_collectionChangedSource is not null)
        {
            _collectionChangedSource.CollectionChanged += ItemsSource_CollectionChanged;
        }
    }

    private void UnhookCollectionChanged()
    {
        if (_collectionChangedSource is not null)
        {
            _collectionChangedSource.CollectionChanged -= ItemsSource_CollectionChanged;
            _collectionChangedSource = null;
        }
    }

    private void ItemsSource_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (DesignerProperties.GetIsInDesignMode(this))
        {
            return;
        }

        BuildTable();
    }

    private void TestDataTable_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (DesignerProperties.GetIsInDesignMode(this))
        {
            return;
        }

        if (Math.Abs(e.NewSize.Width - _lastLayoutWidth) < 0.5)
        {
            return;
        }

        _lastLayoutWidth = e.NewSize.Width;
        BuildTable();
    }

    private void BuildTable()
    {
        if (DesignerProperties.GetIsInDesignMode(this))
        {
            return;
        }

        if (HeaderHost is null || TablePanel is null)
        {
            return;
        }

        List<TableRowData> rows = GetRows();
        HeaderHost.Content = CreateHeaderGrid();
        TablePanel.Children.Clear();

        if (rows.Count == 0)
        {
            TablePanel.Children.Add(CreateEmptyGrid());
            return;
        }

        foreach (WorkStepGroup group in CreateWorkStepGroups(rows))
        {
            TablePanel.Children.Add(CreateWorkStepGroupGrid(group));
        }
    }

    private Grid CreateHeaderGrid()
    {
        ColumnWidths columnWidths = GetColumnWidths();
        Grid headerGrid = CreateGrid(
            columnWidths.WorkStep,
            columnWidths.Name,
            columnWidths.TestValue,
            columnWidths.Result,
            columnWidths.JudgmentCondition);
        headerGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(HeaderHeight) });

        AddHeaderCell(headerGrid, 0, "工步");
        AddHeaderCell(headerGrid, 1, "名称");
        AddHeaderCell(headerGrid, 2, "测试值");
        AddHeaderCell(headerGrid, 3, "结果");
        AddHeaderCell(headerGrid, 4, "判断条件");
        return headerGrid;
    }

    private Grid CreateEmptyGrid()
    {
        ColumnWidths columnWidths = GetColumnWidths();
        Grid emptyGrid = CreateGrid(
            columnWidths.WorkStep,
            columnWidths.Name,
            columnWidths.TestValue,
            columnWidths.Result,
            columnWidths.JudgmentCondition);
        emptyGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(RowHeight) });

        Border emptyCell = CreateCellBorder(0, 0, 1, 5, false);
        TextBlock emptyText = CreateTextBlock("暂无数据", false, false, true);
        emptyText.SetResourceReference(TextBlock.ForegroundProperty, "AppMutedTextBrush");
        emptyCell.Child = emptyText;
        emptyGrid.Children.Add(emptyCell);
        return emptyGrid;
    }

    private Grid CreateWorkStepGroupGrid(WorkStepGroup group)
    {
        ColumnWidths columnWidths = GetColumnWidths();
        Grid groupGrid = new()
        {
            Width = columnWidths.Total,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        groupGrid.ColumnDefinitions.Add(CreateColumn(columnWidths.WorkStep));
        groupGrid.ColumnDefinitions.Add(CreateColumn(columnWidths.Detail));

        double groupHeight = Math.Min(group.Rows.Count * RowHeight, WorkStepMaxHeight);
        groupGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(groupHeight) });

        bool hasFailure = group.Rows.Any(row => IsFailure(row.Result));
        Border workStepCell = CreateCellBorder(0, 0, 1, 1, false);
        workStepCell.Child = CreateWorkStepContent(group.WorkStep, group.ElapsedTime, hasFailure);
        groupGrid.Children.Add(workStepCell);

        ScrollViewer detailScrollViewer = new()
        {
            Height = groupHeight,
            MaxHeight = WorkStepMaxHeight,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = CreateDetailGrid(group.Rows, columnWidths)
        };
        detailScrollViewer.PreviewMouseWheel += ScrollViewer_PreviewMouseWheel;

        Grid.SetRow(detailScrollViewer, 0);
        Grid.SetColumn(detailScrollViewer, 1);
        groupGrid.Children.Add(detailScrollViewer);
        return groupGrid;
    }

    private void BodyScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        HeaderScrollViewer?.ScrollToHorizontalOffset(e.HorizontalOffset);
    }

    private void HeaderScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (BodyScrollViewer is null)
        {
            return;
        }

        if (CanScrollVertically(BodyScrollViewer, e.Delta))
        {
            e.Handled = true;
            BodyScrollViewer.ScrollToVerticalOffset(BodyScrollViewer.VerticalOffset - e.Delta);
            return;
        }

        e.Handled = true;
        ScrollBodyViewer(e.Delta);
    }

    private void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer)
        {
            return;
        }

        if (ReferenceEquals(scrollViewer, BodyScrollViewer) &&
            IsOriginalSourceInsideNestedScrollViewer(e.OriginalSource, scrollViewer))
        {
            return;
        }

        if (CanScrollVertically(scrollViewer, e.Delta))
        {
            e.Handled = true;
            scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta);
            return;
        }

        e.Handled = true;
        if (!ReferenceEquals(scrollViewer, BodyScrollViewer))
        {
            ScrollBodyViewer(e.Delta);
        }
    }

    private static bool CanScrollVertically(ScrollViewer scrollViewer, int delta)
    {
        if (scrollViewer.ScrollableHeight <= 0)
        {
            return false;
        }

        return delta > 0
            ? scrollViewer.VerticalOffset > 0
            : scrollViewer.VerticalOffset < scrollViewer.ScrollableHeight;
    }

    private static bool IsOriginalSourceInsideNestedScrollViewer(object? originalSource, ScrollViewer boundary)
    {
        if (originalSource is not DependencyObject current)
        {
            return false;
        }

        while (current is not null && !ReferenceEquals(current, boundary))
        {
            if (current is ScrollViewer)
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private void ScrollBodyViewer(int delta)
    {
        if (BodyScrollViewer is null)
        {
            return;
        }

        if (CanScrollVertically(BodyScrollViewer, delta))
        {
            BodyScrollViewer.ScrollToVerticalOffset(BodyScrollViewer.VerticalOffset - delta);
        }
    }

    private Grid CreateDetailGrid(IReadOnlyList<TableRowData> rows, ColumnWidths columnWidths)
    {
        Grid detailGrid = CreateGrid(
            columnWidths.Name,
            columnWidths.TestValue,
            columnWidths.Result,
            columnWidths.JudgmentCondition);
        for (int i = 0; i < rows.Count; i++)
        {
            detailGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(RowHeight) });
        }

        AddMergedJudgmentCells(detailGrid, rows);

        for (int i = 0; i < rows.Count; i++)
        {
            TableRowData row = rows[i];
            bool isFailure = IsFailure(row.Result);
            AddDataCell(detailGrid, i, 0, row.Name, isFailure, false);
            AddDataCell(detailGrid, i, 1, row.TestValue, isFailure, false);
            AddDataCell(detailGrid, i, 2, row.Result, isFailure, true);
        }

        return detailGrid;
    }

    private static Grid CreateGrid(params double[] columnWidths)
    {
        Grid grid = new()
        {
            Width = columnWidths.Sum(),
            HorizontalAlignment = HorizontalAlignment.Left,
            SnapsToDevicePixels = true
        };

        foreach (double columnWidth in columnWidths)
        {
            grid.ColumnDefinitions.Add(CreateColumn(columnWidth));
        }

        return grid;
    }

    private static ColumnDefinition CreateColumn(double width)
    {
        return new ColumnDefinition
        {
            Width = new GridLength(width),
            MaxWidth = width
        };
    }

    private ColumnWidths GetColumnWidths()
    {
        double availableWidth = ActualWidth;
        if (double.IsNaN(availableWidth) || availableWidth <= 0)
        {
            availableWidth = MinimumTableWidth;
        }

        double flexibleColumnWidth = Math.Max(
            MinFieldWidth,
            (availableWidth - ResultWidth) / FlexibleColumnCount);

        return new ColumnWidths(
            flexibleColumnWidth,
            flexibleColumnWidth,
            flexibleColumnWidth,
            ResultWidth,
            flexibleColumnWidth);
    }

    private void AddHeaderCell(Grid targetGrid, int column, string text)
    {
        Border border = CreateCellBorder(0, column, 1, 1, true);
        border.Child = CreateTextBlock(text, false, true, true);
        border.SetResourceReference(Border.BackgroundProperty, "TabItemSelectedBrush");
        targetGrid.Children.Add(border);
    }

    private void AddMergedJudgmentCells(Grid detailGrid, IReadOnlyList<TableRowData> rows)
    {
        int startIndex = 0;
        while (startIndex < rows.Count)
        {
            string value = rows[startIndex].JudgmentCondition;
            int span = 1;

            while (startIndex + span < rows.Count &&
                   string.Equals(value, rows[startIndex + span].JudgmentCondition, StringComparison.Ordinal))
            {
                span++;
            }

            bool hasFailure = rows.Skip(startIndex).Take(span).Any(row => IsFailure(row.Result));
            AddJudgmentCells(detailGrid, startIndex, span, value, hasFailure);
            startIndex += span;
        }
    }

    private void AddJudgmentCells(
        Grid detailGrid,
        int startRow,
        int totalSpan,
        string text,
        bool isFailure)
    {
        int remainingSpan = totalSpan;
        int currentRow = startRow;

        while (remainingSpan > 0)
        {
            int span = Math.Min(remainingSpan, MaxVisibleMergedRows);
            AddDataCell(detailGrid, currentRow, 3, text, isFailure, true, span);
            currentRow += span;
            remainingSpan -= span;
        }
    }

    private void AddDataCell(
        Grid targetGrid,
        int row,
        int column,
        string text,
        bool isFailure,
        bool center,
        int rowSpan = 1)
    {
        Border border = CreateCellBorder(row, column, rowSpan, 1, false);
        border.Child = CreateTextBlock(text, isFailure, false, center);
        targetGrid.Children.Add(border);
    }

    private Border CreateCellBorder(int row, int column, int rowSpan, int columnSpan, bool isHeader)
    {
        Border border = new()
        {
            MinHeight = isHeader ? HeaderHeight : RowHeight,
            Padding = isHeader ? new Thickness(8, 0, 8, 0) : new Thickness(10, 0, 10, 0),
            BorderThickness = new Thickness(0, 0, 1, 1)
        };

        border.SetResourceReference(Border.BackgroundProperty, isHeader ? "TabItemSelectedBrush" : "TabCardBackgroundBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "TabItemBorderBrush");

        Grid.SetRow(border, row);
        Grid.SetColumn(border, column);
        Grid.SetRowSpan(border, rowSpan);
        Grid.SetColumnSpan(border, columnSpan);
        return border;
    }

    private TextBlock CreateTextBlock(string text, bool isFailure, bool isHeader, bool center)
    {
        TextBlock textBlock = new()
        {
            Text = text,
            FontSize = 12,
            FontWeight = isHeader ? FontWeights.SemiBold : FontWeights.Normal,
            HorizontalAlignment = center ? HorizontalAlignment.Center : HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = center ? TextAlignment.Center : TextAlignment.Left,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
            ToolTip = text
        };

        if (isFailure)
        {
            textBlock.Foreground = FailureBrush;
        }
        else
        {
            textBlock.SetResourceReference(TextBlock.ForegroundProperty, "AppContentTextBrush");
        }

        return textBlock;
    }

    private List<TableRowData> GetRows()
    {
        if (ItemsSource is null)
        {
            return new List<TableRowData>();
        }

        return ItemsSource
            .Cast<object>()
            .Select(item => new TableRowData(
                ResolveValue(item, WorkStepPath),
                ResolveValue(item, NamePath),
                ResolveValue(item, TestValuePath),
                ResolveValue(item, JudgmentConditionPath),
                ResolveValue(item, ResultPath),
                ResolveValue(item, WorkStepElapsedTimePath)))
            .ToList();
    }

    private static List<WorkStepGroup> CreateWorkStepGroups(IReadOnlyList<TableRowData> rows)
    {
        List<WorkStepGroup> groups = new();
        int startIndex = 0;

        while (startIndex < rows.Count)
        {
            string workStep = rows[startIndex].WorkStep;
            List<TableRowData> groupRows = new();

            while (startIndex < rows.Count &&
                   string.Equals(workStep, rows[startIndex].WorkStep, StringComparison.Ordinal))
            {
                groupRows.Add(rows[startIndex]);
                startIndex++;
            }

            string elapsedTime = groupRows
                .Select(row => row.WorkStepElapsedTime)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

            groups.Add(new WorkStepGroup(workStep, elapsedTime, groupRows));
        }

        return groups;
    }

    private static string ResolveValue(object item, string propertyPath)
    {
        if (item is null || string.IsNullOrWhiteSpace(propertyPath))
        {
            return string.Empty;
        }

        object? value = item;
        foreach (string propertyName in propertyPath.Split('.'))
        {
            if (value is null)
            {
                return string.Empty;
            }

            PropertyDescriptor? property = TypeDescriptor.GetProperties(value)[propertyName.Trim()];
            if (property is null)
            {
                return string.Empty;
            }

            value = property.GetValue(value);
        }

        return value?.ToString()?.Trim() ?? string.Empty;
    }

    private static bool IsFailure(string result)
    {
        return string.Equals(result?.Trim(), "NG", StringComparison.OrdinalIgnoreCase);
    }

    private UIElement CreateWorkStepContent(string workStep, string elapsedTime, bool hasFailure)
    {
        if (string.IsNullOrWhiteSpace(elapsedTime))
        {
            return CreateTextBlock(workStep, hasFailure, false, true);
        }

        StackPanel panel = new()
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        panel.Children.Add(CreateTextBlock(workStep, hasFailure, false, true));

        TextBlock elapsedText = CreateTextBlock($"耗时 {elapsedTime}", hasFailure, false, true);
        elapsedText.Margin = new Thickness(0, 4, 0, 0);
        elapsedText.FontSize = 11;
        elapsedText.SetResourceReference(TextBlock.ForegroundProperty, hasFailure ? "AppContentTextBrush" : "AppMutedTextBrush");
        panel.Children.Add(elapsedText);

        return panel;
    }

    private sealed record WorkStepGroup(string WorkStep, string ElapsedTime, IReadOnlyList<TableRowData> Rows);

    private readonly record struct TableRowData(
        string WorkStep,
        string Name,
        string TestValue,
        string JudgmentCondition,
        string Result,
        string WorkStepElapsedTime);

    private readonly record struct ColumnWidths(
        double WorkStep,
        double Name,
        double TestValue,
        double Result,
        double JudgmentCondition)
    {
        public double Detail => Name + TestValue + Result + JudgmentCondition;

        public double Total => WorkStep + Detail;
    }
}
