using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ControlLibrary.Controls.TestDataTable.Control;

public partial class TestDataTable : UserControl
{
    private const double FieldWidth = 220;
    private const double HeaderHeight = 30;
    private const double RowHeight = 32;
    private const double WorkStepMaxHeight = 400;

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

    private static readonly Brush FailureBrush =
        (Brush)new BrushConverter().ConvertFromString("#D14343")!;

    private INotifyCollectionChanged? _collectionChangedSource;

    public TestDataTable()
    {
        InitializeComponent();
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

    private static void OnItemsSourceChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is TestDataTable table)
        {
            table.UnhookCollectionChanged();
            table.HookCollectionChanged(e.NewValue as INotifyCollectionChanged);
            table.BuildTable();
        }
    }

    private static void OnPathChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is TestDataTable table)
        {
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
        BuildTable();
    }

    private void BuildTable()
    {
        if (TablePanel is null)
        {
            return;
        }

        List<TableRowData> rows = GetRows();
        TablePanel.Children.Clear();
        TablePanel.Children.Add(CreateHeaderGrid());

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
        Grid headerGrid = CreateGrid(columnCount: 5);
        headerGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(HeaderHeight) });

        AddHeaderCell(headerGrid, 0, "\u5de5\u6b65");
        AddHeaderCell(headerGrid, 1, "\u540d\u79f0");
        AddHeaderCell(headerGrid, 2, "\u6d4b\u8bd5\u503c");
        AddHeaderCell(headerGrid, 3, "\u5224\u65ad\u6761\u4ef6");
        AddHeaderCell(headerGrid, 4, "\u7ed3\u679c");
        return headerGrid;
    }

    private Grid CreateEmptyGrid()
    {
        Grid emptyGrid = CreateGrid(columnCount: 5);
        emptyGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(RowHeight) });

        Border emptyCell = CreateCellBorder(0, 0, 1, 5, false);
        TextBlock emptyText = CreateTextBlock("\u6682\u65e0\u6570\u636e", false, false, true);
        emptyText.SetResourceReference(TextBlock.ForegroundProperty, "AppMutedTextBrush");
        emptyCell.Child = emptyText;
        emptyGrid.Children.Add(emptyCell);
        return emptyGrid;
    }

    private Grid CreateWorkStepGroupGrid(WorkStepGroup group)
    {
        Grid groupGrid = new()
        {
            Width = FieldWidth * 5,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        groupGrid.ColumnDefinitions.Add(CreateColumn(FieldWidth));
        groupGrid.ColumnDefinitions.Add(CreateColumn(FieldWidth * 4));

        double groupHeight = Math.Min(group.Rows.Count * RowHeight, WorkStepMaxHeight);
        groupGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(groupHeight) });

        bool hasFailure = group.Rows.Any(row => IsFailure(row.Result));
        Border workStepCell = CreateCellBorder(0, 0, 1, 1, false);
        workStepCell.Child = CreateTextBlock(group.WorkStep, hasFailure, false, true);
        groupGrid.Children.Add(workStepCell);

        ScrollViewer detailScrollViewer = new()
        {
            MaxHeight = WorkStepMaxHeight,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = CreateDetailGrid(group.Rows)
        };

        Grid.SetRow(detailScrollViewer, 0);
        Grid.SetColumn(detailScrollViewer, 1);
        groupGrid.Children.Add(detailScrollViewer);
        return groupGrid;
    }

    private Grid CreateDetailGrid(IReadOnlyList<TableRowData> rows)
    {
        Grid detailGrid = CreateGrid(columnCount: 4);
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
            AddDataCell(detailGrid, i, 3, row.Result, isFailure, true);
        }

        return detailGrid;
    }

    private static Grid CreateGrid(int columnCount)
    {
        Grid grid = new()
        {
            Width = FieldWidth * columnCount,
            HorizontalAlignment = HorizontalAlignment.Left,
            SnapsToDevicePixels = true
        };

        for (int i = 0; i < columnCount; i++)
        {
            grid.ColumnDefinitions.Add(CreateColumn(FieldWidth));
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
            AddDataCell(detailGrid, startIndex, 2, value, hasFailure, true, span);
            startIndex += span;
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
            MaxWidth = FieldWidth,
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
                ResolveValue(item, ResultPath)))
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

            groups.Add(new WorkStepGroup(workStep, groupRows));
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

    private sealed record WorkStepGroup(string WorkStep, IReadOnlyList<TableRowData> Rows);

    private readonly record struct TableRowData(
        string WorkStep,
        string Name,
        string TestValue,
        string JudgmentCondition,
        string Result);
}
