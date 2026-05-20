using ControlLibrary.Controls.FlowchartEditor.Models;
using Module.Business.Models;
using Module.Business.ViewModels;
using Module.Business.ViewModels.PropertyVMs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Module.Business.Views;

/// <summary>
/// Station configuration view with flowchart editor on the right side.
/// </summary>
public partial class StationConfigurationView : UserControl
{
    private SchemeConfigurationViewModel? _nodeOperationEditorViewModel;
    private Guid? _editingNodeId;

    public StationConfigurationView()
    {
        InitializeComponent();
    }

    public StationConfigurationView(StationConfigurationViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }

    private StationConfigurationViewModel? ViewModel => DataContext as StationConfigurationViewModel;

    private void Editor_NodeDoubleClick(object sender, FlowchartNodeInteractionEventArgs e)
    {
        OpenNodeOperationEditor(e);
    }

    private void NodeOperationEditorSaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_nodeOperationEditorViewModel is null || _editingNodeId is null || ViewModel?.SelectedStation is null)
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

        FlowchartDocument document = FlowchartPanel.CreateDocumentSnapshot();
        FlowchartNodeDocument? node = document.Nodes.FirstOrDefault(item => item.Id == _editingNodeId.Value);
        if (node is null)
        {
            CloseNodeOperationEditor(cancelChanges: false);
            return;
        }

        node.MetadataJson = JsonSerializer.Serialize(operation);
        node.Text = BuildNodeText(node.Kind, operation);

        ViewModel.SelectedStation.FlowchartDocument = document;
        FlowchartPanel.LoadDocumentSnapshot(document);

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
            if (TryDeserializeNodeOperationMetadata(e.MetadataJson, out WorkStepOperation? operation) &&
                operation is not null)
            {
                return operation;
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
        if (!CanEditNode(e.NodeKind) || ViewModel?.CanEdit != true || ViewModel.SelectedStation is null)
        {
            return;
        }

        WorkStepOperation operation = DeserializeNodeOperation(e);
        _editingNodeId = e.NodeId;

        _nodeOperationEditorViewModel = new SchemeConfigurationViewModel();
        _nodeOperationEditorViewModel.SetStandaloneReturnValueOptions(
            GetFlowchartReturnValueOptions(FlowchartPanel.CreateDocumentSnapshot(), _nodeOperationEditorViewModel));
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

    private static IEnumerable<string> GetFlowchartReturnValueOptions(
        FlowchartDocument document,
        SchemeConfigurationViewModel operationEditorViewModel)
    {
        return document.Nodes
            .SelectMany(node => GetNodeReturnValueOptions(node, operationEditorViewModel))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> GetNodeReturnValueOptions(
        FlowchartNodeDocument node,
        SchemeConfigurationViewModel operationEditorViewModel)
    {
        if (!TryDeserializeNodeOperationMetadata(node.MetadataJson, out WorkStepOperation? operation) ||
            operation is null)
        {
            yield break;
        }

        if (!string.IsNullOrWhiteSpace(operation.ReturnValue))
        {
            yield return operation.ReturnValue.Trim();
        }

        foreach (WorkStepOperationParameter parameter in operationEditorViewModel.CreateReturnParametersFromOperation(operation))
        {
            string value = string.IsNullOrWhiteSpace(parameter.ParameterName)
                ? parameter.Value
                : parameter.ParameterName;
            if (!string.IsNullOrWhiteSpace(value))
            {
                yield return value.Trim();
            }
        }
    }

    private static bool TryDeserializeNodeOperationMetadata(string? metadataJson, out WorkStepOperation? operation)
    {
        operation = null;
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return false;
        }

        try
        {
            operation = JsonSerializer.Deserialize<WorkStepOperation>(metadataJson);
            return operation is not null;
        }
        catch
        {
            return false;
        }
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
