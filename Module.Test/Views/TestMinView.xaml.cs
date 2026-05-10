using Module.Test.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace Module.Test.Views
{
    /// <summary>
    /// TestMinView.xaml 的交互逻辑
    /// </summary>
    public partial class TestMinView : UserControl
    {
        public TestMinView()
        {
            InitializeComponent();
            if (!DesignerProperties.GetIsInDesignMode(this))
            {
                DataContext = new TestMinViewModel();
            }

            Unloaded += TestMinView_Unloaded;
        }

        private void TestMinView_Unloaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is TestMinViewModel viewModel)
            {
                viewModel.Dispose();
            }
        }
    }
}
