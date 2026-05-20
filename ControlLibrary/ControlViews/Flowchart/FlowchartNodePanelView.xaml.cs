using ControlLibrary.Controls.FlowchartEditor.Control;
using ControlLibrary.Controls.FlowchartEditor.Models;
using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ControlLibrary.ControlViews.Flowchart;

/// <summary>
/// Shared flowchart node palette and preview command panel.
/// </summary>
public partial class FlowchartNodePanelView : UserControl
{
    private Button? _dragSourceButton;
    private Point _dragStartPoint;

    public FlowchartNodePanelView()
    {
        InitializeComponent();
        PART_Editor.NodeDoubleClick += (_, e) => NodeDoubleClick?.Invoke(this, e);
    }

    public static readonly DependencyProperty DocumentProperty =
        DependencyProperty.Register(
            nameof(Document),
            typeof(FlowchartDocument),
            typeof(FlowchartNodePanelView),
            new FrameworkPropertyMetadata(
                null,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public event EventHandler<FlowchartNodeInteractionEventArgs>? NodeDoubleClick;

    public FlowchartDocument? Document
    {
        get => (FlowchartDocument?)GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    public FlowchartDocument CreateDocumentSnapshot()
    {
        return PART_Editor.CreateDocumentSnapshot();
    }

    public void LoadDocumentSnapshot(FlowchartDocument? document)
    {
        PART_Editor.LoadDocumentSnapshot(document);
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

        if (!TryReadTemplate(_dragSourceButton.Tag, out string nodeText, out string nodeKind))
        {
            _dragSourceButton = null;
            return;
        }

        Button dragSourceButton = _dragSourceButton;
        _dragSourceButton = null;

        DataObject dataObject = new();
        dataObject.SetData(DataFormats.StringFormat, nodeText);
        dataObject.SetData(FlowchartDragDataFormats.PaletteText, nodeText);
        dataObject.SetData(FlowchartDragDataFormats.PaletteNodeKind, nodeKind);
        dataObject.SetData(FlowchartDragDataFormats.DragId, Guid.NewGuid().ToString("N"));

        DragDrop.DoDragDrop(dragSourceButton, dataObject, DragDropEffects.Copy);
    }

    private void PaletteItem_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _dragSourceButton = null;
    }

    private static bool TryReadTemplate(object? template, out string nodeText, out string nodeKind)
    {
        nodeText = string.Empty;
        nodeKind = FlowchartNodeKind.Process.ToString();

        if (template is null)
        {
            return false;
        }

        Type templateType = template.GetType();
        nodeText = ReadProperty(template, templateType, "NodeText") ?? string.Empty;
        nodeKind = ReadProperty(template, templateType, "NodeKind") ?? FlowchartNodeKind.Process.ToString();

        return !string.IsNullOrWhiteSpace(nodeText);
    }

    private static string? ReadProperty(object source, Type sourceType, string propertyName)
    {
        PropertyInfo? property = sourceType.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        object? value = property?.GetValue(source);
        return value?.ToString();
    }
}
