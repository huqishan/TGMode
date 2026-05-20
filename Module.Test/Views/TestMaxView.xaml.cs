using Module.Test.ViewModels;
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace Module.Test.Views
{
    /// <summary>
    /// TestMaxView.xaml 的交互逻辑
    /// </summary>
    public partial class TestMaxView : UserControl
    {
        public TestMaxView()
        {
            InitializeView();
        }

        public TestMaxView(TestMaxViewModel viewModel)
        {
            InitializeView();
            if (!DesignerProperties.GetIsInDesignMode(this))
            {
                DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            }
        }

        private void InitializeView()
        {
            InitializeComponent();
            Loaded += TestMaxView_Loaded;
            Unloaded += TestMinView_Unloaded;
        }

        private async void TestMaxView_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is TestMaxViewModel viewModel)
            {
                await viewModel.LoadSchemesAsync();
            }
        }

        private void TestMinView_Unloaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is TestMaxViewModel viewModel)
            {
                viewModel.Dispose();
            }
        }
    }
}
