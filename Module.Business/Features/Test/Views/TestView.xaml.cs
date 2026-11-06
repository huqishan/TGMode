using Module.Business.Test.ViewModels;
using System;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Module.Business.Test.Views
{
    /// <summary>
    /// 通信测试主界面。
    /// 以卡片网格方式展示各工位的测试面板，并根据可视区域高度驱动卡片高度自适应，
    /// 同时负责界面的加载/卸载生命周期与工位集合变化监听。
    /// </summary>
    public partial class TestView : UserControl
    {
        /// <summary>当前已订阅集合变更事件的 ViewModel，避免重复订阅。</summary>
        private TestViewModel? _subscribedViewModel;

        /// <summary>
        /// 设计期构造函数，仅初始化界面与生命周期事件。
        /// </summary>
        public TestView()
        {
            InitializeComponent();
            InitializeViewEvents();
        }

        /// <summary>
        /// 运行期构造函数，由视图工厂注入 ViewModel。
        /// </summary>
        /// <param name="viewModel">通信测试主页 ViewModel。</param>
        public TestView(TestViewModel viewModel)
        {
            InitializeComponent();
            InitializeViewEvents();
            DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        }

        /// <summary>以强类型方式访问当前数据上下文。</summary>
        private TestViewModel? ViewModel => DataContext as TestViewModel;

        #region 生命周期事件

        /// <summary>注册 Loaded / Unloaded / DataContextChanged 生命周期事件。</summary>
        private void InitializeViewEvents()
        {
            Loaded += TestView_Loaded;
            Unloaded += TestView_Unloaded;
            DataContextChanged += TestView_DataContextChanged;
        }

        /// <summary>界面加载：订阅工位集合变更并从工位配置加载启用工位。</summary>
        private async void TestView_Loaded(object sender, RoutedEventArgs e)
        {
            SubscribeViewModel(ViewModel);

            if (ViewModel is not null)
            {
                await ViewModel.LoadStationsAsync();
                RefreshStationItemHeight();
            }
        }

        /// <summary>界面卸载：取消订阅并释放 ViewModel 资源。</summary>
        private void TestView_Unloaded(object sender, RoutedEventArgs e)
        {
            SubscribeViewModel(null);
            ViewModel?.Dispose();
        }

        /// <summary>数据上下文变化：重新订阅集合并延后刷新一次卡片高度（等布局完成）。</summary>
        private void TestView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            SubscribeViewModel(e.NewValue as TestViewModel);
            Dispatcher.BeginInvoke(RefreshStationItemHeight, DispatcherPriority.Loaded);
        }

        #endregion

        #region 卡片高度自适应

        /// <summary>
        /// 订阅/切换工位集合变更监听，重复订阅同一 ViewModel 时直接跳过。
        /// </summary>
        /// <param name="viewModel">目标 ViewModel，传 null 表示取消订阅。</param>
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

        /// <summary>卡片容器尺寸变化：把新的可视高度同步给 ViewModel。</summary>
        private void StationsScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            RefreshStationItemHeight(e.NewSize.Height);
        }

        /// <summary>工位集合变化：延后到布局完成再刷新卡片高度。</summary>
        private void Stations_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            Dispatcher.BeginInvoke(RefreshStationItemHeight, DispatcherPriority.Loaded);
        }

        /// <summary>按当前可视区域刷新卡片高度。</summary>
        private void RefreshStationItemHeight()
        {
            RefreshStationItemHeight(GetStationDisplayHeight());
        }

        /// <summary>把可视高度传给 ViewModel 重新计算单张卡片高度。</summary>
        private void RefreshStationItemHeight(double displayHeight)
        {
            ViewModel?.UpdateStationDisplayHeight(displayHeight);
        }

        /// <summary>
        /// 获取卡片区域可用高度：优先取视口高度，视口未就绪时回退为控件实际高度。
        /// </summary>
        private double GetStationDisplayHeight()
        {
            double viewportHeight = StationsScrollViewer.ViewportHeight;
            return !double.IsNaN(viewportHeight) && !double.IsInfinity(viewportHeight) && viewportHeight > 0
                ? viewportHeight
                : StationsScrollViewer.ActualHeight;
        }

        #endregion

    }
}
