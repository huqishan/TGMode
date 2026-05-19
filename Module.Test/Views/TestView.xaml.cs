using System;
using System.Windows.Controls;
using Module.Test.ViewModels;

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

        public TestView(TestMaxViewModel viewModel)
        {
            InitializeComponent();
            if (MinView.DataContext is TestMaxViewModel oldViewModel)
            {
                oldViewModel.Dispose();
            }

            MinView.DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        }
    }
}
