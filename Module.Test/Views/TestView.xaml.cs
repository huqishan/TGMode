using Module.Test.ViewModels;
using System;
using System.Windows;
using System.Windows.Controls;

namespace Module.Test.Views
{
    /// <summary>
    /// TestView.xaml 的交互逻辑
    /// </summary>
    public partial class TestView : UserControl
    {
        public TestView()
        {
            InitializeComponent();
        }

        public TestView(TestViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            Loaded += TestView_Loaded;
            Unloaded += TestView_Unloaded;
        }

        private TestViewModel? ViewModel => DataContext as TestViewModel;

        private async void TestView_Loaded(object sender, RoutedEventArgs e)
        {
            if (ViewModel is not null)
            {
                await ViewModel.LoadStationsAsync();
            }
        }

        private void TestView_Unloaded(object sender, RoutedEventArgs e)
        {
            ViewModel?.Dispose();
        }

        private void StationsScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ViewModel?.UpdateStationDisplayWidth(e.NewSize.Width);
        }
    }
}
