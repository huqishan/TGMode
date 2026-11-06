using Module.Business.Test.ViewModels;
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace Module.Business.Test.Views
{
    /// <summary>
    /// 单工位通信测试面板。
    /// 展示单个工位的方案选择、连接测试与报文收发界面，加载/卸载时同步方案数据与资源释放。
    /// </summary>
    public partial class TestMaxView : UserControl
    {
        /// <summary>
        /// 设计期构造函数，仅初始化界面与生命周期事件。
        /// </summary>
        public TestMaxView()
        {
            InitializeView();
        }

        /// <summary>
        /// 运行期构造函数，由视图工厂注入 ViewModel（设计期不设置数据上下文）。
        /// </summary>
        /// <param name="viewModel">单工位测试 ViewModel。</param>
        public TestMaxView(TestMaxViewModel viewModel)
        {
            InitializeView();
            if (!DesignerProperties.GetIsInDesignMode(this))
            {
                DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            }
        }

        #region 生命周期事件

        /// <summary>初始化界面元素并注册加载/卸载事件。</summary>
        private void InitializeView()
        {
            InitializeComponent();
            Loaded += TestMaxView_Loaded;
            Unloaded += TestMinView_Unloaded;
        }

        /// <summary>界面加载：异步加载该工位可选的方案列表。</summary>
        private async void TestMaxView_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is TestMaxViewModel viewModel)
            {
                await viewModel.LoadSchemesAsync();
            }
        }

        /// <summary>界面卸载：释放 ViewModel 订阅的通信事件等资源。</summary>
        private void TestMinView_Unloaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is TestMaxViewModel viewModel)
            {
                viewModel.Dispose();
            }
        }

        #endregion
    }
}
