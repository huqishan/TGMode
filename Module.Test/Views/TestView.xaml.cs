using Module.Test.ViewModels;
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
            Unloaded += TestView_Unloaded;
        }

        private void TestView_Unloaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is TestViewModel viewModel)
            {
                viewModel.Dispose();
            }
        }
    }
}
