using System;
using System.Collections.Generic;

namespace ControlLibrary.Controls.FlowchartEditor.Models
{
    /// <summary>
    /// 执行流程图后的结果摘要，供界面显示最终状态和执行日志。
    /// </summary>
    public sealed class FlowchartExecutionResult
    {
        public FlowchartExecutionResult(bool isSuccess, string message, IReadOnlyList<string> steps)
        {
            IsSuccess = isSuccess;
            Message = message;
            Steps = steps;
        }

        public bool IsSuccess { get; }

        public string Message { get; }

        public IReadOnlyList<string> Steps { get; }
    }

    /// <summary>
    /// 执行到某个节点时抛出的事件参数，界面可以据此刷新状态和高亮。
    /// </summary>
    public sealed class FlowchartExecutionStepEventArgs : EventArgs
    {
        public FlowchartExecutionStepEventArgs(int stepIndex, Guid nodeId, string nodeText, FlowchartNodeKind nodeKind, string message)
        {
            StepIndex = stepIndex;
            NodeId = nodeId;
            NodeText = nodeText;
            NodeKind = nodeKind;
            Message = message;
        }

        public int StepIndex { get; }

        public Guid NodeId { get; }

        public string NodeText { get; }

        public FlowchartNodeKind NodeKind { get; }

        public string Message { get; }
    }
}
