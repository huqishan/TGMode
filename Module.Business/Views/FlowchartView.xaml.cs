using ControlLibrary.Controls.FlowchartEditor.Models;
using Module.Business.ViewModels;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Module.Business.Views
{
    /// <summary>
    /// FlowchartView.xaml 的纯界面交互逻辑。
    /// </summary>
    public partial class FlowchartView : UserControl
    {
        #region 拖拽字段

        private Button? _dragSourceButton;
        private Point _dragStartPoint;

        #endregion

        #region 构造方法

        public FlowchartView()
        {
            InitializeComponent();
        }

        #endregion

        #region 节点模板拖拽交互

        /// <summary>
        /// 记录节点模板拖拽的起始位置。
        /// </summary>
        private void PaletteItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragSourceButton = sender as Button;
            _dragStartPoint = e.GetPosition(this);
        }

        /// <summary>
        /// 鼠标移动超过系统拖拽阈值后，向流程图编辑器传递节点模板数据。
        /// </summary>
        private void PaletteItem_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_dragSourceButton is null || e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            Point currentPoint = e.GetPosition(this);
            if (Math.Abs(currentPoint.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(currentPoint.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            if (_dragSourceButton.Tag is not FlowchartNodeTemplate template)
            {
                _dragSourceButton = null;
                return;
            }

            Button dragSourceButton = _dragSourceButton;
            _dragSourceButton = null;

            DataObject dataObject = new();
            dataObject.SetData(DataFormats.StringFormat, template.NodeText);
            dataObject.SetData(FlowchartDragDataFormats.PaletteText, template.NodeText);
            dataObject.SetData(FlowchartDragDataFormats.PaletteNodeKind, template.NodeKind.ToString());
            dataObject.SetData(FlowchartDragDataFormats.DragId, Guid.NewGuid().ToString("N"));

            DragDrop.DoDragDrop(dragSourceButton, dataObject, DragDropEffects.Copy);
        }

        /// <summary>
        /// 鼠标释放时清理本次拖拽状态。
        /// </summary>
        private void PaletteItem_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _dragSourceButton = null;
        }

        #endregion
    }
}
