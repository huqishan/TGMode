using Module.Communication.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace Module.Communication.Views
{
    /// <summary>
    /// 设备通信配置界面的界面交互代码，仅保留必要的生命周期桥接。
    /// </summary>
    public partial class DeviceCommunicationConfigView : UserControl
    {
        #region 构造与生命周期
        public DeviceCommunicationConfigView()
        {
            InitializeComponent();
            Unloaded += DeviceCommunicationConfigView_Unloaded;
        }

        private DeviceCommunicationConfigViewModel? ViewModel => DataContext as DeviceCommunicationConfigViewModel;

        private void DeviceCommunicationConfigView_Unloaded(object sender, RoutedEventArgs e)
        {
            ViewModel?.OnViewUnloaded();
        }

        #endregion
    }
}
