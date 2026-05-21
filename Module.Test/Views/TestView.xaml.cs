using Module.Test.ViewModels;
using System;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Module.Test.Views
{
    /// <summary>
    /// TestView.xaml 的交互逻辑
    /// </summary>
    public partial class TestView : UserControl
    {
        private TestViewModel? _subscribedViewModel;

        public TestView()
        {
            InitializeComponent();
            InitializeViewEvents();
        }

        public TestView(TestViewModel viewModel)
        {
            InitializeComponent();
            InitializeViewEvents();
            DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        }

        private TestViewModel? ViewModel => DataContext as TestViewModel;

        private void InitializeViewEvents()
        {
            Loaded += TestView_Loaded;
            Unloaded += TestView_Unloaded;
            DataContextChanged += TestView_DataContextChanged;
        }

        private async void TestView_Loaded(object sender, RoutedEventArgs e)
        {
            SubscribeViewModel(ViewModel);

            if (ViewModel is not null)
            {
                await ViewModel.LoadStationsAsync();
                RefreshStationItemHeight();
            }
        }

        private void TestView_Unloaded(object sender, RoutedEventArgs e)
        {
            SubscribeViewModel(null);
            ViewModel?.Dispose();
        }

        private void TestView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            SubscribeViewModel(e.NewValue as TestViewModel);
            Dispatcher.BeginInvoke(RefreshStationItemHeight, DispatcherPriority.Loaded);
        }

        private void SubscribeViewModel(TestViewModel? viewModel)
        {
            if (ReferenceEquals(_subscribedViewModel, viewModel))
            {
                return;
            }

            if (_subscribedViewModel is not null)
            {
                _subscribedViewModel.Stations.CollectionChanged -= Stations_CollectionChanged;
            }

            _subscribedViewModel = viewModel;

            if (_subscribedViewModel is not null)
            {
                _subscribedViewModel.Stations.CollectionChanged += Stations_CollectionChanged;
            }
        }

        private void StationsScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            RefreshStationItemHeight(e.NewSize.Height);
        }

        private void Stations_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            Dispatcher.BeginInvoke(RefreshStationItemHeight, DispatcherPriority.Loaded);
        }

        private void RefreshStationItemHeight()
        {
            RefreshStationItemHeight(GetStationDisplayHeight());
        }

        private void RefreshStationItemHeight(double displayHeight)
        {
            ViewModel?.UpdateStationDisplayHeight(displayHeight);
        }

        private double GetStationDisplayHeight()
        {
            double viewportHeight = StationsScrollViewer.ViewportHeight;
            return !double.IsNaN(viewportHeight) && !double.IsInfinity(viewportHeight) && viewportHeight > 0
                ? viewportHeight
                : StationsScrollViewer.ActualHeight;
        }

    }
}
