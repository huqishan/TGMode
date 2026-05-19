using System;
using System.ComponentModel;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Data;
using WpfApp.Models.DataManagement;
using WpfApp.ViewModels;

namespace WpfApp.Views.DataManagement;

/// <summary>
/// Test data page. The view only builds dynamic grid columns from the selected configuration.
/// </summary>
public partial class TestDataView : UserControl
{
    public TestDataView()
    {
        InitializeComponent();
    }

    public TestDataView(TestDataViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        Loaded += TestDataView_Loaded;
        Unloaded += TestDataView_Unloaded;
    }

    private TestDataViewModel ViewModel => (TestDataViewModel)DataContext;

    private void TestDataView_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {
        ViewModel.Load();
        BuildColumns();
    }

    private void TestDataView_Unloaded(object sender, System.Windows.RoutedEventArgs e)
    {
        ViewModel.Unload();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TestDataViewModel.SelectedConfiguration) or nameof(TestDataViewModel.Configurations))
        {
            BuildColumns();
        }
    }

    private void BuildColumns()
    {
        if (TestDataGrid is null)
        {
            return;
        }

        TestDataGrid.Columns.Clear();
        if (ViewModel.SelectedConfiguration is null)
        {
            return;
        }

        foreach (TestDataGridColumnConfig column in ViewModel.SelectedConfiguration.Columns.Where(column => column.IsVisible))
        {
            TestDataGrid.Columns.Add(CreateColumn(column));
        }
    }

    private static DataGridTextColumn CreateColumn(TestDataGridColumnConfig column)
    {
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
}
