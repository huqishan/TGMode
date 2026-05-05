using ControlLibrary.Controls.FlowchartEditor.Control;
using ControlLibrary.Controls.FlowchartEditor.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Module.Business.Services;

/// <summary>
/// 流程图编辑器运行态控制服务。
/// </summary>
/// <remarks>
/// <para>
/// 这个服务专门负责围绕 <see cref="FlowchartEditorControl"/> 的“运行、暂停、停止、继续”控制逻辑，
/// 目的是把页面命令里与执行态切换相关的代码集中到单独文件，避免 ViewModel 同时承担
/// “界面状态绑定”和“运行时控制”两类职责。
/// </para>
/// <para>
/// 这里不直接依赖页面上的按钮、画刷或其它界面元素，只返回纯结果对象。
/// 调用方可以根据返回结果决定如何刷新状态文本、日志面板和按钮可用性，
/// 这样后续无论是继续使用 WPF 界面按钮，还是改成工具栏、快捷键、远程触发，
/// 都可以复用同一套流程图运行控制逻辑。
/// </para>
/// <para>
/// 运行方法内部会临时订阅 <see cref="FlowchartEditorControl.ExecutionStepChanged"/>，
/// 把节点执行进度向外透传；执行结束后无论成功、失败还是异常，都会保证解除订阅，
/// 避免重复执行时事件处理器累积，导致日志重复写入或状态被多次刷新。
/// </para>
/// </remarks>
public static class FlowchartExecutionControlService
{
    /// <summary>
    /// 运行当前流程图，并把执行步骤通过回调实时通知给调用方。
    /// </summary>
    /// <param name="editor">
    /// 流程图编辑器实例。服务直接通过它调用底层的执行入口，并从中读取执行结果和步骤事件。
    /// </param>
    /// <param name="onStepChanged">
    /// 可选的步骤进度回调。每当编辑器执行到一个新节点时，服务都会把原始
    /// <see cref="FlowchartExecutionStepEventArgs"/> 传出，便于界面同步追加执行日志或刷新状态文字。
    /// </param>
    /// <returns>
    /// 返回一次完整的运行结果，其中包含成功状态、结果消息以及最终步骤日志快照。
    /// 如果编辑器为空或执行过程抛出异常，也会统一转换成失败结果，避免调用方再写重复的异常包装代码。
    /// </returns>
    public static async Task<FlowchartExecutionServiceResult> RunAsync(
        FlowchartEditorControl? editor,
        Action<FlowchartExecutionStepEventArgs>? onStepChanged = null)
    {
        if (editor is null)
        {
            return FlowchartExecutionServiceResult.CreateFailure("流程图编辑器未初始化。");
        }

        EventHandler<FlowchartExecutionStepEventArgs>? handler = null;
        if (onStepChanged is not null)
        {
            handler = (_, args) => onStepChanged(args);
            editor.ExecutionStepChanged += handler;
        }

        try
        {
            FlowchartExecutionResult result = await editor.ExecuteFlowAsync().ConfigureAwait(true);
            return new FlowchartExecutionServiceResult(result.IsSuccess, result.Message, result.Steps);
        }
        catch (Exception ex)
        {
            return FlowchartExecutionServiceResult.CreateFailure($"执行流程图失败：{ex.Message}");
        }
        finally
        {
            if (handler is not null)
            {
                editor.ExecutionStepChanged -= handler;
            }
        }
    }

    /// <summary>
    /// 暂停当前正在执行的流程图。
    /// </summary>
    /// <param name="editor">
    /// 当前流程图编辑器实例。只有当编辑器正处于执行态时，暂停请求才会生效。
    /// </param>
    /// <returns>
    /// 返回暂停动作结果。成功时表示编辑器已经切换到暂停状态；
    /// 失败通常意味着编辑器为空，或当前没有可暂停的执行流程。
    /// </returns>
    public static FlowchartExecutionControlActionResult Pause(FlowchartEditorControl? editor)
    {
        if (editor is null)
        {
            return FlowchartExecutionControlActionResult.CreateFailure("流程图编辑器未初始化。");
        }

        return editor.PauseExecution()
            ? FlowchartExecutionControlActionResult.CreateSuccess("流程图已暂停。")
            : FlowchartExecutionControlActionResult.CreateFailure("当前没有可暂停的流程图执行。");
    }

    /// <summary>
    /// 继续执行一个已经处于暂停状态的流程图。
    /// </summary>
    /// <param name="editor">
    /// 当前流程图编辑器实例。只有在编辑器已经暂停时，继续请求才会真正恢复执行。
    /// </param>
    /// <returns>
    /// 返回继续动作结果。成功时表示暂停信号已被释放，流程图会从上一次安全暂停点继续执行；
    /// 失败通常表示编辑器为空，或者当前流程图并未处于暂停状态。
    /// </returns>
    public static FlowchartExecutionControlActionResult Continue(FlowchartEditorControl? editor)
    {
        if (editor is null)
        {
            return FlowchartExecutionControlActionResult.CreateFailure("流程图编辑器未初始化。");
        }

        return editor.ResumeExecution()
            ? FlowchartExecutionControlActionResult.CreateSuccess("继续执行流程图。")
            : FlowchartExecutionControlActionResult.CreateFailure("当前流程图未处于暂停状态。");
    }

    /// <summary>
    /// 停止当前流程图执行。
    /// </summary>
    /// <param name="editor">
    /// 当前流程图编辑器实例。服务会请求编辑器取消执行，并同步解除暂停等待，确保流程能够尽快结束。
    /// </param>
    /// <returns>
    /// 返回停止动作结果。成功时只表示“停止请求已经发出”，
    /// 真正的执行结束消息仍然会由运行任务在结束时返回，便于调用方保留完整的结束日志。
    /// </returns>
    public static FlowchartExecutionControlActionResult Stop(FlowchartEditorControl? editor)
    {
        if (editor is null)
        {
            return FlowchartExecutionControlActionResult.CreateFailure("流程图编辑器未初始化。");
        }

        return editor.StopExecution()
            ? FlowchartExecutionControlActionResult.CreateSuccess("正在结束执行。")
            : FlowchartExecutionControlActionResult.CreateFailure("当前没有正在执行的流程图。");
    }
}

/// <summary>
/// 一次流程图运行后的统一结果。
/// </summary>
/// <remarks>
/// 这个结果对象是页面层与运行控制服务之间的稳定契约。
/// 页面不需要知道底层执行器是通过事件、任务还是其它机制实现，只需要消费这里的成功状态、
/// 结果消息和步骤日志即可。
/// </remarks>
public sealed class FlowchartExecutionServiceResult
{
    public FlowchartExecutionServiceResult(bool isSuccess, string message, IReadOnlyList<string>? steps = null)
    {
        IsSuccess = isSuccess;
        Message = message ?? string.Empty;
        Steps = steps ?? Array.Empty<string>();
    }

    public bool IsSuccess { get; }

    public string Message { get; }

    public IReadOnlyList<string> Steps { get; }

    public static FlowchartExecutionServiceResult CreateFailure(
        string message,
        IReadOnlyList<string>? steps = null)
    {
        return new FlowchartExecutionServiceResult(false, message, steps);
    }
}

/// <summary>
/// 流程图暂停、继续、停止等控制动作的统一返回值。
/// </summary>
/// <remarks>
/// 这类动作通常是“立即返回”的控制请求，不像运行流程那样需要返回完整步骤列表，
/// 因此这里只保留是否成功和动作说明，供界面直接刷新提示文本或决定是否切换按钮状态。
/// </remarks>
public sealed class FlowchartExecutionControlActionResult
{
    public FlowchartExecutionControlActionResult(bool isSuccess, string message)
    {
        IsSuccess = isSuccess;
        Message = message ?? string.Empty;
    }

    public bool IsSuccess { get; }

    public string Message { get; }

    public static FlowchartExecutionControlActionResult CreateSuccess(string message)
    {
        return new FlowchartExecutionControlActionResult(true, message);
    }

    public static FlowchartExecutionControlActionResult CreateFailure(string message)
    {
        return new FlowchartExecutionControlActionResult(false, message);
    }
}
