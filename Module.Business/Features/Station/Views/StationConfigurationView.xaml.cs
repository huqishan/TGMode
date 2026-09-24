using ControlLibrary.Controls.FlowchartEditor.Models;
using Module.Business.Features.StationConfiguration;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Module.Business.Features.Station.Views;

public partial class StationConfigurationView : UserControl
{
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
        // 处理块与判断块共用步骤实体；判断块由弹框切换为精简的条件编辑界面。
        if (e.NodeKind is FlowchartNodeKind.Process or FlowchartNodeKind.Decision)
        {
            ViewModel?.OpenNodeOperationEditor(e, FlowchartPanel.CreateDocumentSnapshot());
        }
    }

    private void NodeOperationEditorSaveButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.TrySaveNodeOperationEdit(FlowchartPanel);
    }

    private void NodeOperationEditorCancelButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.CancelNodeOperationEdit();
    }

    private void NodeOperationEditorBackdrop_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        ViewModel?.CancelNodeOperationEdit();
    }
}
