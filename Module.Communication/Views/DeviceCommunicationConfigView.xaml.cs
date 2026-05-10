using Module.Communication.Models;
using Module.Communication.ViewModels;
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Module.Communication.Views
{
    /// <summary>
    /// 设备通信配置界面的界面交互代码，仅保留必要的生命周期桥接、抽屉动画与拖拽交互。
    /// </summary>
    public partial class DeviceCommunicationConfigView : UserControl
    {
        #region 字段与常量

        /// <summary>
        /// 协议拖拽数据格式名。
        /// </summary>
        private const string AvailableProtocolDragDataFormat = "Module.Communication.AvailableProtocol";

        /// <summary>
        /// 抽屉关闭时向下偏移的距离。
        /// </summary>
        private const double DrawerClosedOffset = 56d;

        /// <summary>
        /// 抽屉动画持续时间。
        /// </summary>
        private static readonly Duration DrawerAnimationDuration = new(TimeSpan.FromMilliseconds(220));

        /// <summary>
        /// 抽屉动画缓动函数。
        /// </summary>
        private static readonly IEasingFunction DrawerEasing = new CubicEase { EasingMode = EasingMode.EaseOut };

        /// <summary>
        /// 当前已绑定属性变化事件的 ViewModel。
        /// </summary>
        private DeviceCommunicationConfigViewModel? _attachedViewModel;

        /// <summary>
        /// 协议拖拽起始点。
        /// </summary>
        private Point _protocolDragStartPoint;

        /// <summary>
        /// 等待拖拽的协议对象。
        /// </summary>
        private AvailableProtocolOption? _pendingDraggedProtocol;

        #endregion

        #region 构造与属性

        public DeviceCommunicationConfigView()
        {
            InitializeComponent();
            DataContext = new DeviceCommunicationConfigViewModel();
            AttachViewModelEvents();
            DataContextChanged += DeviceCommunicationConfigView_DataContextChanged;
            Loaded += DeviceCommunicationConfigView_Loaded;
            Unloaded += DeviceCommunicationConfigView_Unloaded;
        }

        /// <summary>
        /// 当前页面绑定的 ViewModel。
        /// </summary>
        private DeviceCommunicationConfigViewModel? ViewModel => DataContext as DeviceCommunicationConfigViewModel;

        #endregion

        #region 生命周期

        private void DeviceCommunicationConfigView_Loaded(object sender, RoutedEventArgs e)
        {
            AttachViewModelEvents();
            UpdateProtocolLibraryVisual(animate: false);
            UpdateProtocolCommandLibraryVisual(animate: false);
        }

        private void DeviceCommunicationConfigView_Unloaded(object sender, RoutedEventArgs e)
        {
            ViewModel?.OnViewUnloaded();
            DetachViewModelEvents();
        }

        private void DeviceCommunicationConfigView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            AttachViewModelEvents();
            UpdateProtocolLibraryVisual(animate: false);
            UpdateProtocolCommandLibraryVisual(animate: false);
        }

        #endregion

        #region ViewModel 事件

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DeviceCommunicationConfigViewModel.IsProtocolLibraryOpen))
            {
                UpdateProtocolLibraryVisual(animate: true);
            }

            if (e.PropertyName == nameof(DeviceCommunicationConfigViewModel.IsProtocolCommandLibraryOpen))
            {
                UpdateProtocolCommandLibraryVisual(animate: true);
            }
        }

        private void AttachViewModelEvents()
        {
            DeviceCommunicationConfigViewModel? viewModel = ViewModel;
            if (ReferenceEquals(_attachedViewModel, viewModel))
            {
                return;
            }

            DetachViewModelEvents();

            if (viewModel is null)
            {
                return;
            }

            viewModel.PropertyChanged += ViewModel_PropertyChanged;
            _attachedViewModel = viewModel;
        }

        private void DetachViewModelEvents()
        {
            if (_attachedViewModel is null)
            {
                return;
            }

            _attachedViewModel.PropertyChanged -= ViewModel_PropertyChanged;
            _attachedViewModel = null;
        }

        #endregion

        #region 协议列表交互

        private void ProtocolLibraryListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _protocolDragStartPoint = e.GetPosition(ProtocolLibraryListBox);
            _pendingDraggedProtocol = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext as AvailableProtocolOption;
        }

        private void ProtocolLibraryListBox_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _pendingDraggedProtocol is null)
            {
                return;
            }

            Point currentPoint = e.GetPosition(ProtocolLibraryListBox);
            if (Math.Abs(currentPoint.X - _protocolDragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(currentPoint.Y - _protocolDragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            AvailableProtocolOption draggedProtocol = _pendingDraggedProtocol;
            _pendingDraggedProtocol = null;

            DataObject dataObject = new();
            dataObject.SetData(AvailableProtocolDragDataFormat, draggedProtocol);
            DragDrop.DoDragDrop(ProtocolLibraryListBox, dataObject, DragDropEffects.Copy);
        }

        private void ProtocolLibraryListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext is AvailableProtocolOption option)
            {
                ViewModel?.TryApplyAvailableProtocol(option, null);
                e.Handled = true;
            }
        }

        private void ProtocolLibraryBackdrop_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            ExecuteCommand(ViewModel?.CloseProtocolLibraryCommand);
        }

        private void SupportedProtocolsDataGrid_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(AvailableProtocolDragDataFormat)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
            e.Handled = true;
        }

        private void SupportedProtocolsDataGrid_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(AvailableProtocolDragDataFormat))
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            AvailableProtocolOption? option = e.Data.GetData(AvailableProtocolDragDataFormat) as AvailableProtocolOption;
            DeviceSupportedProtocol? targetProtocol = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject)?.Item as DeviceSupportedProtocol;
            ViewModel?.TryApplyAvailableProtocol(option, targetProtocol);

            _pendingDraggedProtocol = null;
            e.Handled = true;
        }

        #endregion

        #region 指令列表交互

        private void ProtocolCommandLibraryListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext is not SupportedProtocolCommandOption option)
            {
                return;
            }

            ICommand? command = ViewModel?.FillSupportedProtocolCommandCommand;
            if (command?.CanExecute(option) == true)
            {
                command.Execute(option);
                e.Handled = true;
            }
        }

        private void ProtocolCommandLibraryBackdrop_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            ExecuteCommand(ViewModel?.CloseProtocolCommandLibraryCommand);
        }

        #endregion

        #region 抽屉动画

        private void UpdateProtocolLibraryVisual(bool animate)
        {
            UpdateDrawerVisual(
                ProtocolLibraryHost,
                ProtocolLibraryTranslateTransform,
                ViewModel?.IsProtocolLibraryOpen == true,
                animate);
        }

        private void UpdateProtocolCommandLibraryVisual(bool animate)
        {
            UpdateDrawerVisual(
                ProtocolCommandLibraryHost,
                ProtocolCommandLibraryTranslateTransform,
                ViewModel?.IsProtocolCommandLibraryOpen == true,
                animate);
        }

        private static void UpdateDrawerVisual(
            Grid? host,
            TranslateTransform? translateTransform,
            bool isOpen,
            bool animate)
        {
            if (host is null || translateTransform is null)
            {
                return;
            }

            double targetOpacity = isOpen ? 1d : 0d;
            double targetOffset = isOpen ? 0d : DrawerClosedOffset;

            if (isOpen)
            {
                host.IsHitTestVisible = true;
            }

            if (!animate)
            {
                host.BeginAnimation(UIElement.OpacityProperty, null);
                translateTransform.BeginAnimation(TranslateTransform.YProperty, null);
                host.Opacity = targetOpacity;
                translateTransform.Y = targetOffset;
                host.IsHitTestVisible = isOpen;
                return;
            }

            DoubleAnimation opacityAnimation = new()
            {
                To = targetOpacity,
                Duration = DrawerAnimationDuration,
                EasingFunction = DrawerEasing
            };

            if (!isOpen)
            {
                opacityAnimation.Completed += (_, _) => host.IsHitTestVisible = false;
            }

            DoubleAnimation translateAnimation = new()
            {
                To = targetOffset,
                Duration = DrawerAnimationDuration,
                EasingFunction = DrawerEasing
            };

            host.BeginAnimation(UIElement.OpacityProperty, opacityAnimation);
            translateTransform.BeginAnimation(TranslateTransform.YProperty, translateAnimation);
        }

        #endregion

        #region 通用辅助

        private static void ExecuteCommand(ICommand? command, object? parameter = null)
        {
            if (command?.CanExecute(parameter) == true)
            {
                command.Execute(parameter);
            }
        }

        private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
        {
            while (source is not null)
            {
                if (source is T match)
                {
                    return match;
                }

                source = VisualTreeHelper.GetParent(source);
            }

            return null;
        }

        #endregion
    }
}
