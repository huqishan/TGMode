using ControlLibrary.Controls.FlowchartEditor.Models;
using Module.Business.Features.StationConfiguration;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Module.Business.Features.Station.Views;

/// <summary>
/// 工位配置界面。
/// 左侧维护工位列表，右侧通过流程图编辑器维护工位的业务执行流程，
/// 节点双击可打开业务操作编辑抽屉。
/// </summary>
public partial class StationConfigurationView : UserControl
{
    /// <summary>
    /// 设计期构造函数，仅初始化界面元素。
    /// </summary>
    public StationConfigurationView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 运行期构造函数，由视图工厂注入 ViewModel 并建立数据上下文。
    /// </summary>
    /// <param name="viewModel">工位配置 ViewModel。</param>
    public StationConfigurationView(StationConfigurationViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }

    /// <summary>以强类型方式访问当前数据上下文，便于事件处理调用 ViewModel 方法。</summary>
    private StationConfigurationViewModel? ViewModel => DataContext as StationConfigurationViewModel;

    /// <summary>
    /// 流程图节点双击事件：处理块与判断块共用业务实体，双击后打开业务操作编辑抽屉；
    /// 判断块由弹框切换为精简的条件编辑界面。
    /// </summary>
    private void Editor_NodeDoubleClick(object sender, FlowchartNodeInteractionEventArgs e)
    {
        // 处理块与判断块共用步骤实体；判断块由弹框切换为精简的条件编辑界面。
        if (e.NodeKind is FlowchartNodeKind.Process or FlowchartNodeKind.Decision)
        {
            ViewModel?.OpenNodeOperationEditor(e, FlowchartPanel.CreateDocumentSnapshot());
        }
    }

    /// <summary>业务操作编辑抽屉“保存”按钮：把编辑内容写回流程图节点。</summary>
    private void NodeOperationEditorSaveButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.TrySaveNodeOperationEdit(FlowchartPanel);
    }

    /// <summary>业务操作编辑抽屉“取消”按钮：放弃本次编辑并关闭抽屉。</summary>
    private void NodeOperationEditorCancelButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.CancelNodeOperationEdit();
    }

    /// <summary>点击抽屉遮罩层：等同于取消，关闭业务操作编辑抽屉。</summary>
    private void NodeOperationEditorBackdrop_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        ViewModel?.CancelNodeOperationEdit();
    }
}
