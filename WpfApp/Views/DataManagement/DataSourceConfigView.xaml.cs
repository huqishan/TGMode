using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WpfApp.Models.DataManagement;
using WpfApp.ViewModels;

namespace WpfApp.Views.DataManagement;

/// <summary>
/// Data source configuration page. The view keeps only UI mechanics: dynamic preview columns and drawer animation.
/// </summary>
public partial class DataSourceConfigView : UserControl
{
    private const double PreviewDrawerClosedOffset = 56d;
    private static readonly Duration PreviewDrawerAnimationDuration = new(TimeSpan.FromMilliseconds(220));
    private static readonly IEasingFunction PreviewDrawerEasing = new CubicEase { EasingMode = EasingMode.EaseOut };
    private bool _viewModelEventsAttached;

    public DataSourceConfigView()
    {
        InitializeComponent();
    }

    public DataSourceConfigView(DataSourceConfigViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));

        Loaded += DataSourceConfigView_Loaded;
        Unloaded += DataSourceConfigView_Unloaded;
        AttachViewModelEvents();

        BuildPreviewColumns();
        UpdatePreviewDrawerVisual(animate: false);
    }

    private DataSourceConfigViewModel ViewModel => (DataSourceConfigViewModel)DataContext;

    private void DataSourceConfigView_Loaded(object sender, RoutedEventArgs e)
    {
        AttachViewModelEvents();
        BuildPreviewColumns();
        UpdatePreviewDrawerVisual(animate: false);
    }

    private void DataSourceConfigView_Unloaded(object sender, RoutedEventArgs e)
    {
        DetachViewModelEvents();
    }

    private void AttachViewModelEvents()
    {
        if (_viewModelEventsAttached)
        {
            return;
        }

        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        ViewModel.PreviewColumnsChanged += ViewModel_PreviewColumnsChanged;
        ViewModel.PreviewDrawerStateChanged += ViewModel_PreviewDrawerStateChanged;
        ViewModel.SelectedColumnScrollRequested += ViewModel_SelectedColumnScrollRequested;
        _viewModelEventsAttached = true;
    }

    private void DetachViewModelEvents()
    {
        if (!_viewModelEventsAttached)
        {
            return;
        }

        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        ViewModel.PreviewColumnsChanged -= ViewModel_PreviewColumnsChanged;
        ViewModel.PreviewDrawerStateChanged -= ViewModel_PreviewDrawerStateChanged;
        ViewModel.SelectedColumnScrollRequested -= ViewModel_SelectedColumnScrollRequested;
        _viewModelEventsAttached = false;
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DataSourceConfigViewModel.SelectedConfiguration))
        {
            BuildPreviewColumns();
        }
    }

    private void ViewModel_PreviewColumnsChanged(object? sender, EventArgs e)
    {
        BuildPreviewColumns();
    }

    private void ViewModel_PreviewDrawerStateChanged(object? sender, EventArgs e)
    {
        UpdatePreviewDrawerVisual(animate: true);
    }

    private void ViewModel_SelectedColumnScrollRequested(object? sender, EventArgs e)
    {
        if (ViewModel.SelectedColumn is not null)
        {
            ColumnsDataGrid.ScrollIntoView(ViewModel.SelectedColumn);
        }
    }

    private void PreviewDrawerBackdrop_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        ViewModel.ClosePreviewDrawerCommand.Execute(null);
    }

    private void UpdatePreviewDrawerVisual(bool animate)
    {
        if (PreviewDrawerHost is null || PreviewDrawerTranslateTransform is null)
        {
            return;
        }

        bool isOpen = ViewModel.IsPreviewDrawerOpen;
        double targetOpacity = isOpen ? 1d : 0d;
        double targetOffset = isOpen ? 0d : PreviewDrawerClosedOffset;

        if (isOpen)
        {
            PreviewDrawerHost.IsHitTestVisible = true;
        }

        if (!animate)
        {
            PreviewDrawerHost.BeginAnimation(OpacityProperty, null);
            PreviewDrawerTranslateTransform.BeginAnimation(TranslateTransform.YProperty, null);
            PreviewDrawerHost.Opacity = targetOpacity;
            PreviewDrawerTranslateTransform.Y = targetOffset;
            PreviewDrawerHost.IsHitTestVisible = isOpen;
            return;
        }

        DoubleAnimation opacityAnimation = new()
        {
            To = targetOpacity,
            Duration = PreviewDrawerAnimationDuration,
            EasingFunction = PreviewDrawerEasing
        };

        if (!isOpen)
        {
            opacityAnimation.Completed += (_, _) =>
            {
                if (!ViewModel.IsPreviewDrawerOpen)
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

        PreviewDrawerHost.BeginAnimation(OpacityProperty, opacityAnimation);
        PreviewDrawerTranslateTransform.BeginAnimation(TranslateTransform.YProperty, translateAnimation);
    }

    private void BuildPreviewColumns()
    {
        if (PreviewDataGrid is null)
        {
            return;
        }

        PreviewDataGrid.Columns.Clear();
        if (ViewModel.SelectedConfiguration is null)
        {
            return;
        }

        foreach (TestDataGridColumnConfig column in ViewModel.SelectedConfiguration.Columns.Where(column => column.IsVisible))
        {
            PreviewDataGrid.Columns.Add(CreatePreviewColumn(column));
        }
    }

    private static DataGridTextColumn CreatePreviewColumn(TestDataGridColumnConfig column)
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
