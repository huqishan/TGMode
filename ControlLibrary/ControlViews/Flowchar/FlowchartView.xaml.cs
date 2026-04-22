using ControlLibrary.Controls.FlowchartEditor.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace ControlLibrary.ControlViews.Flowchar
{
    /// <summary>
    /// FlowchartView.xaml 的交互逻辑
    /// </summary>
    public partial class FlowchartView : UserControl
    {
        private Button? _dragSourceButton;
        private Point _dragStartPoint;

        public FlowchartView()
        {
            InitializeComponent();
        }

        private void PaletteItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragSourceButton = sender as Button;
            _dragStartPoint = e.GetPosition(this);
        }

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

            string paletteText = _dragSourceButton.Tag?.ToString() ?? _dragSourceButton.Content?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(paletteText))
            {
                return;
            }

            Button dragSourceButton = _dragSourceButton;
            _dragSourceButton = null;

            DataObject dataObject = new DataObject();
            dataObject.SetData(DataFormats.StringFormat, paletteText);
            dataObject.SetData(FlowchartDragDataFormats.PaletteText, paletteText);
            dataObject.SetData(FlowchartDragDataFormats.DragId, Guid.NewGuid().ToString("N"));

            DragDrop.DoDragDrop(dragSourceButton, dataObject, DragDropEffects.Copy);
            _dragSourceButton = null;
        }

        private void PaletteItem_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _dragSourceButton = null;
        }
    }
}
