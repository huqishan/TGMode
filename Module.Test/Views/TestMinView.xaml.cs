using Module.Test.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace Module.Test.Views
{
    /// <summary>
    /// TestMinView.xaml 的交互逻辑
    /// </summary>
    public partial class TestMinView : UserControl
    {
        private readonly TestMinViewModel _viewModel = new();

        public TestMinView()
        {
            InitializeComponent();
            DataContext = _viewModel;
            Unloaded += TestMinView_Unloaded;
        }

        private void TestMinView_Unloaded(object sender, RoutedEventArgs e)
        {
            _viewModel.Dispose();
        }
    }
}
