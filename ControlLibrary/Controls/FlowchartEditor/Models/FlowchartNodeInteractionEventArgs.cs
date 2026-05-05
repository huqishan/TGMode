using System;

namespace ControlLibrary.Controls.FlowchartEditor.Models
{
    /// <summary>
    /// 节点双击等交互事件参数。
    /// </summary>
    public sealed class FlowchartNodeInteractionEventArgs : EventArgs
    {
        public FlowchartNodeInteractionEventArgs(Guid nodeId, string text, FlowchartNodeKind nodeKind, string metadataJson)
        {
            NodeId = nodeId;
            Text = text ?? string.Empty;
            NodeKind = nodeKind;
            MetadataJson = metadataJson ?? string.Empty;
        }

        public Guid NodeId { get; }

        public string Text { get; }

        public FlowchartNodeKind NodeKind { get; }

        public string MetadataJson { get; }
    }
}
