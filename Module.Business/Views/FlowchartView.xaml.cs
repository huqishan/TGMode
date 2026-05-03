using ControlLibrary.Controls.FlowchartEditor.Models;
using Microsoft.Win32;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Module.Business.Views
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
            Editor.ExecutionStepChanged += Editor_ExecutionStepChanged;
            UpdateExecutionButtons();
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
            dataObject.SetData(FlowchartDragDataFormats.PaletteNodeKind, ResolvePaletteNodeKind(paletteText).ToString());
            dataObject.SetData(FlowchartDragDataFormats.DragId, Guid.NewGuid().ToString("N"));

            DragDrop.DoDragDrop(dragSourceButton, dataObject, DragDropEffects.Copy);
            _dragSourceButton = null;
        }

        private void PaletteItem_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _dragSourceButton = null;
        }

        private void Editor_ExecutionStepChanged(object? sender, FlowchartExecutionStepEventArgs e)
        {
            ExecutionStatusText.Text = $"状态：{e.Message}";
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            // 保存当前画布里的节点和连线，输出为本地 JSON 文件。
            SaveFileDialog dialog = new SaveFileDialog
            {
                Filter = "流程图文件 (*.flowchart.json)|*.flowchart.json|JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*",
                DefaultExt = ".flowchart.json",
                FileName = "flowchart.flowchart.json"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            try
            {
                Editor.SaveToFile(dialog.FileName);
                ExecutionStatusText.Text = $"状态：已保存到 {dialog.FileName}";
            }
            catch (Exception exception)
            {
                MessageBox.Show($"保存流程图失败：{exception.Message}", "流程图", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenButton_Click(object sender, RoutedEventArgs e)
        {
            // 打开时会解析本地 JSON，并直接恢复为当前流程图画布。
            OpenFileDialog dialog = new OpenFileDialog
            {
                Filter = "流程图文件 (*.flowchart.json)|*.flowchart.json|JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*",
                DefaultExt = ".flowchart.json"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            try
            {
                Editor.LoadFromFile(dialog.FileName);
                ExecutionStatusText.Text = $"状态：已打开 {dialog.FileName}";
            }
            catch (Exception exception)
            {
                MessageBox.Show($"打开流程图失败：{exception.Message}", "流程图", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void ExecuteButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ExecutionStatusText.Text = "状态：开始执行流程图";

                // 调用后编辑器会立即切换到执行状态，这里同步刷新按钮可用性。
                Task<FlowchartExecutionResult> executionTask = Editor.ExecuteFlowAsync();
                UpdateExecutionButtons();

                FlowchartExecutionResult result = await executionTask;
                ExecutionStatusText.Text = $"状态：{result.Message}";
            }
            catch (Exception exception)
            {
                MessageBox.Show($"执行流程图失败：{exception.Message}", "流程图", MessageBoxButton.OK, MessageBoxImage.Error);
                ExecutionStatusText.Text = "状态：执行失败";
            }
            finally
            {
                UpdateExecutionButtons();
            }
        }

        private void PauseButton_Click(object sender, RoutedEventArgs e)
        {
            if (!Editor.IsExecuting)
            {
                return;
            }

            bool changed;
            if (Editor.IsExecutionPaused)
            {
                changed = Editor.ResumeExecution();
                if (changed)
                {
                    ExecutionStatusText.Text = "状态：继续执行流程图";
                }
            }
            else
            {
                changed = Editor.PauseExecution();
                if (changed)
                {
                    ExecutionStatusText.Text = "状态：流程图已暂停";
                }
            }

            if (changed)
            {
                UpdateExecutionButtons();
            }
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            if (!Editor.StopExecution())
            {
                return;
            }

            ExecutionStatusText.Text = "状态：正在结束执行";
            UpdateExecutionButtons();
        }

        private void UpdateExecutionButtons()
        {
            bool isExecuting = Editor.IsExecuting;
            bool isPaused = Editor.IsExecutionPaused;

            SaveButton.IsEnabled = !isExecuting;
            OpenButton.IsEnabled = !isExecuting;
            ExecuteButton.IsEnabled = !isExecuting;
            PauseButton.IsEnabled = isExecuting;
            StopButton.IsEnabled = isExecuting;
            PauseButton.Content = isPaused ? "继续" : "暂停";
        }

        private static FlowchartNodeKind ResolvePaletteNodeKind(string paletteText)
        {
            return paletteText.Trim() switch
            {
                "开始" => FlowchartNodeKind.Start,
                "判断" => FlowchartNodeKind.Decision,
                "结束" => FlowchartNodeKind.End,
                _ => FlowchartNodeKind.Process
            };
        }
    }
}
