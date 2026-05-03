using ControlLibrary;
using ControlLibrary.Controls.FlowchartEditor.Control;
using ControlLibrary.Controls.FlowchartEditor.Models;
using Microsoft.Win32;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;

namespace Module.Business.ViewModels;

/// <summary>
/// 流程图页面命令实现，供 XAML Command 绑定调用。
/// </summary>
public sealed partial class FlowchartViewModel
{
    #region 构造与初始化

    public FlowchartViewModel()
    {
        InitializeNodeTemplates();
        InitializeCommands();
    }

    /// <summary>
    /// 初始化左侧可拖拽的节点模板。
    /// </summary>
    private void InitializeNodeTemplates()
    {
        NodeTemplates.Add(new FlowchartNodeTemplate("开始", "开始", FlowchartNodeKind.Start, StartBrush));
        NodeTemplates.Add(new FlowchartNodeTemplate("处理", "处理", FlowchartNodeKind.Process, ProcessBrush));
        NodeTemplates.Add(new FlowchartNodeTemplate("判断", "判断", FlowchartNodeKind.Decision, DecisionBrush));
        NodeTemplates.Add(new FlowchartNodeTemplate("结束", "结束", FlowchartNodeKind.End, EndBrush));
    }

    /// <summary>
    /// 初始化页面全部按钮命令，XAML 不再绑定后台 Click 事件。
    /// </summary>
    private void InitializeCommands()
    {
        NewFlowchartCommand = new RelayCommand(NewFlowchart, CanEditFlowchart);
        SaveFlowchartCommand = new RelayCommand(SaveFlowchart, CanEditFlowchart);
        OpenFlowchartCommand = new RelayCommand(OpenFlowchart, CanEditFlowchart);
        ExecuteFlowchartCommand = new RelayCommand(async parameter => await ExecuteFlowchartAsync(parameter), CanExecuteFlowchart);
        PauseFlowchartCommand = new RelayCommand(TogglePauseFlowchart, CanPauseFlowchart);
        StopFlowchartCommand = new RelayCommand(StopFlowchart, CanStopFlowchart);
    }

    #endregion

    #region 文件命令方法

    /// <summary>
    /// 清空当前画布并创建空白流程图。
    /// </summary>
    private void NewFlowchart(object? parameter)
    {
        if (parameter is not FlowchartEditorControl editor)
        {
            SetExecutionStatus("状态：流程图编辑器未初始化", WarningBrush);
            return;
        }

        editor.ClearDocument();
        ExecutionLogs.Clear();
        SetExecutionStatus("状态：已新建空白流程图", SuccessBrush);
    }

    /// <summary>
    /// 保存当前画布为本地 JSON 文件。
    /// </summary>
    private void SaveFlowchart(object? parameter)
    {
        if (parameter is not FlowchartEditorControl editor)
        {
            SetExecutionStatus("状态：流程图编辑器未初始化", WarningBrush);
            return;
        }

        SaveFileDialog dialog = new()
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
            editor.SaveToFile(dialog.FileName);
            SetExecutionStatus($"状态：已保存到 {dialog.FileName}", SuccessBrush);
        }
        catch (Exception ex)
        {
            SetExecutionStatus($"状态：保存流程图失败：{ex.Message}", WarningBrush);
        }
    }

    /// <summary>
    /// 从本地 JSON 文件打开流程图。
    /// </summary>
    private void OpenFlowchart(object? parameter)
    {
        if (parameter is not FlowchartEditorControl editor)
        {
            SetExecutionStatus("状态：流程图编辑器未初始化", WarningBrush);
            return;
        }

        OpenFileDialog dialog = new()
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
            editor.LoadFromFile(dialog.FileName);
            ExecutionLogs.Clear();
            SetExecutionStatus($"状态：已打开 {dialog.FileName}", SuccessBrush);
        }
        catch (Exception ex)
        {
            SetExecutionStatus($"状态：打开流程图失败：{ex.Message}", WarningBrush);
        }
    }

    #endregion

    #region 执行命令方法

    /// <summary>
    /// 执行当前流程图，并把执行步骤同步到绑定属性。
    /// </summary>
    private async Task ExecuteFlowchartAsync(object? parameter)
    {
        if (parameter is not FlowchartEditorControl editor)
        {
            SetExecutionStatus("状态：流程图编辑器未初始化", WarningBrush);
            return;
        }

        IsExecuting = true;
        IsPaused = false;
        ExecutionLogs.Clear();
        SetExecutionStatus("状态：开始执行流程图", NeutralBrush);

        void OnExecutionStepChanged(object? sender, FlowchartExecutionStepEventArgs e)
        {
            ExecutionLogs.Add(e.Message);
            SetExecutionStatus($"状态：{e.Message}", NeutralBrush);
        }

        editor.ExecutionStepChanged += OnExecutionStepChanged;

        try
        {
            FlowchartExecutionResult result = await editor.ExecuteFlowAsync();
            if (!ExecutionLogs.Any())
            {
                foreach (string step in result.Steps)
                {
                    ExecutionLogs.Add(step);
                }
            }

            SetExecutionStatus($"状态：{result.Message}", result.IsSuccess ? SuccessBrush : WarningBrush);
        }
        catch (Exception ex)
        {
            SetExecutionStatus($"状态：执行流程图失败：{ex.Message}", WarningBrush);
        }
        finally
        {
            editor.ExecutionStepChanged -= OnExecutionStepChanged;
            IsPaused = false;
            IsExecuting = false;
        }
    }

    /// <summary>
    /// 暂停或继续当前执行中的流程图。
    /// </summary>
    private void TogglePauseFlowchart(object? parameter)
    {
        if (parameter is not FlowchartEditorControl editor)
        {
            return;
        }

        if (IsPaused)
        {
            if (editor.ResumeExecution())
            {
                IsPaused = false;
                SetExecutionStatus("状态：继续执行流程图", NeutralBrush);
            }

            return;
        }

        if (editor.PauseExecution())
        {
            IsPaused = true;
            SetExecutionStatus("状态：流程图已暂停", NeutralBrush);
        }
    }

    /// <summary>
    /// 请求结束当前流程图执行。
    /// </summary>
    private void StopFlowchart(object? parameter)
    {
        if (parameter is FlowchartEditorControl editor && editor.StopExecution())
        {
            IsPaused = false;
            SetExecutionStatus("状态：正在结束执行", WarningBrush);
        }
    }

    #endregion

    #region 命令状态方法

    private bool CanUseEditor(object? parameter)
    {
        return parameter is FlowchartEditorControl;
    }

    private bool CanEditFlowchart(object? parameter)
    {
        return CanUseEditor(parameter) && CanEdit;
    }

    private bool CanExecuteFlowchart(object? parameter)
    {
        return CanUseEditor(parameter) && !IsExecuting;
    }

    private bool CanPauseFlowchart(object? parameter)
    {
        return CanUseEditor(parameter) && IsExecuting;
    }

    private bool CanStopFlowchart(object? parameter)
    {
        return CanUseEditor(parameter) && IsExecuting;
    }

    private void RaiseCommandStatesChanged()
    {
        RaiseCommandState(NewFlowchartCommand);
        RaiseCommandState(SaveFlowchartCommand);
        RaiseCommandState(OpenFlowchartCommand);
        RaiseCommandState(ExecuteFlowchartCommand);
        RaiseCommandState(PauseFlowchartCommand);
        RaiseCommandState(StopFlowchartCommand);
    }

    private static void RaiseCommandState(ICommand? command)
    {
        if (command is RelayCommand relayCommand)
        {
            relayCommand.RaiseCanExecuteChanged();
        }
    }

    private void SetExecutionStatus(string text, Brush brush)
    {
        ExecutionStatusText = text;
        ExecutionStatusBrush = brush;
    }

    #endregion
}
