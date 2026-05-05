using ControlLibrary.Controls.FlowchartEditor.Models;
using Module.Business.Models;
using Module.Business.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Module.Business.Views
{
    /// <summary>
    /// FlowchartView.xaml interaction logic.
    /// </summary>
    public partial class FlowchartView : UserControl
    {
        private Button? _dragSourceButton;
        private Point _dragStartPoint;
        private WorkStepConfigurationViewModel? _nodeOperationEditorViewModel;
        private Guid? _editingNodeId;

        public FlowchartView()
        {
            InitializeComponent();
        }

        private FlowchartViewModel? ViewModel => DataContext as FlowchartViewModel;

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

        private void PaletteItem_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _dragSourceButton = null;
        }

        private void Editor_NodeDoubleClick(object sender, FlowchartNodeInteractionEventArgs e)
        {
            OpenNodeOperationEditor(e);
        }

        private void NodeOperationEditorSaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (_nodeOperationEditorViewModel is null || _editingNodeId is null || ViewModel?.SelectedFlowchart is null)
            {
                return;
            }

            if (!_nodeOperationEditorViewModel.TrySaveStandaloneOperationEdit())
            {
                return;
            }

            WorkStepOperation? operation = _nodeOperationEditorViewModel.CreateEditedOperationSnapshot();
            if (operation is null)
            {
                return;
            }

            FlowchartDocument document = Editor.CreateDocumentSnapshot();
            FlowchartNodeDocument? node = document.Nodes.FirstOrDefault(item => item.Id == _editingNodeId.Value);
            if (node is null)
            {
                CloseNodeOperationEditor(cancelChanges: false);
                return;
            }

            node.MetadataJson = JsonSerializer.Serialize(operation);
            node.Text = BuildNodeText(node.Kind, operation);

            ViewModel.SelectedFlowchart.Document = document;
            Editor.LoadDocumentSnapshot(document);

            CloseNodeOperationEditor(cancelChanges: false);
        }

        private void NodeOperationEditorCancelButton_Click(object sender, RoutedEventArgs e)
        {
            CloseNodeOperationEditor(cancelChanges: true);
        }

        private void NodeOperationEditorBackdrop_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            CloseNodeOperationEditor(cancelChanges: true);
        }

        private void CloseNodeOperationEditor(bool cancelChanges)
        {
            if (cancelChanges)
            {
                _nodeOperationEditorViewModel?.CancelStandaloneOperationEdit();
            }

            _nodeOperationEditorViewModel = null;
            _editingNodeId = null;
            NodeOperationEditorHost.Tag = null;
            NodeOperationEditorHost.Visibility = Visibility.Collapsed;
        }

        private static WorkStepOperation DeserializeNodeOperation(FlowchartNodeInteractionEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(e.MetadataJson))
            {
                try
                {
                    WorkStepOperation? operation = JsonSerializer.Deserialize<WorkStepOperation>(e.MetadataJson);
                    if (operation is not null)
                    {
                        return operation;
                    }
                }
                catch
                {
                    // Ignore broken node metadata and fall back to text parsing.
                }
            }

            string[] lines = (e.Text ?? string.Empty)
                .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
            string firstLine = lines
                .Select(line => line?.Trim() ?? string.Empty)
                .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line))
                ?? string.Empty;
            string summary = NormalizeInlineText(lines.Skip(1));

            return new WorkStepOperation
            {
                OperationObject = ResolveOperationObject(e.NodeKind, firstLine),
                InvokeMethod = string.Empty,
                ReturnValue = string.Empty,
                ShowDataToView = false,
                ViewDataName = string.Empty,
                ViewJudgeType = string.Empty,
                ViewJudgeCondition = string.Empty,
                DelayMilliseconds = 0,
                Remark = summary
            };
        }

        private static bool CanEditNode(FlowchartNodeKind nodeKind)
        {
            return nodeKind == FlowchartNodeKind.Process || nodeKind == FlowchartNodeKind.Decision;
        }

        private void OpenNodeOperationEditor(FlowchartNodeInteractionEventArgs e)
        {
            if (!CanEditNode(e.NodeKind) || ViewModel?.CanEdit != true || ViewModel.SelectedFlowchart is null)
            {
                return;
            }

            WorkStepOperation operation = DeserializeNodeOperation(e);
            _editingNodeId = e.NodeId;

            // 处理块与判断块共用同一个编辑步骤弹框，只通过模式参数切换判断方法相关行为。
            _nodeOperationEditorViewModel = new WorkStepConfigurationViewModel();
            _nodeOperationEditorViewModel.BeginStandaloneOperationEdit(
                operation,
                GetNodeEditorTitle(e.NodeKind),
                e.NodeKind == FlowchartNodeKind.Decision);

            NodeOperationEditorHost.Tag = _nodeOperationEditorViewModel;
            NodeOperationEditorHost.Visibility = Visibility.Visible;
        }

        private static string GetNodeEditorTitle(FlowchartNodeKind nodeKind)
        {
            return nodeKind == FlowchartNodeKind.Decision
                ? "流程图判断块"
                : "流程图处理块";
        }

        private static string BuildNodeText(FlowchartNodeKind nodeKind, WorkStepOperation operation)
        {
            string operationObject = string.IsNullOrWhiteSpace(operation.OperationObject)
                ? GetDefaultNodeText(nodeKind)
                : operation.OperationObject.Trim();
            string summary = NormalizeInlineText(operation.Remark);

            return string.IsNullOrWhiteSpace(summary)
                ? operationObject
                : $"{operationObject} {summary}";
        }

        private static string ResolveOperationObject(FlowchartNodeKind nodeKind, string firstLine)
        {
            if (string.IsNullOrWhiteSpace(firstLine))
            {
                return nodeKind == FlowchartNodeKind.Process ? "System" : GetDefaultNodeText(nodeKind);
            }

            if (nodeKind == FlowchartNodeKind.Process &&
                string.Equals(firstLine, "处理", StringComparison.Ordinal))
            {
                return "System";
            }

            return firstLine.Trim();
        }

        private static string GetDefaultNodeText(FlowchartNodeKind nodeKind)
        {
            return nodeKind switch
            {
                FlowchartNodeKind.Decision => "判断",
                FlowchartNodeKind.Start => "开始",
                FlowchartNodeKind.End => "结束",
                _ => "处理"
            };
        }

        private static string NormalizeInlineText(string? text)
        {
            return NormalizeInlineText((text ?? string.Empty)
                .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None));
        }

        private static string NormalizeInlineText(IEnumerable<string> values)
        {
            return string.Join(
                " ",
                values.Select(value => value?.Trim() ?? string.Empty)
                    .Where(value => !string.IsNullOrWhiteSpace(value)));
        }
    }
}
