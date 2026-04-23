using System;
using System.Collections.Generic;

namespace ControlLibrary.Controls.FlowchartEditor.Models
{
    /// <summary>
    /// 流程图文件根对象。当前只持久化节点和连接关系，方便直接保存为本地 JSON。
    /// </summary>
    public sealed class FlowchartDocument
    {
        public int Version { get; set; } = 1;

        public List<FlowchartNodeDocument> Nodes { get; set; } = new List<FlowchartNodeDocument>();

        public List<FlowchartConnectionDocument> Connections { get; set; } = new List<FlowchartConnectionDocument>();
    }

    /// <summary>
    /// 单个节点的序列化结构。
    /// </summary>
    public sealed class FlowchartNodeDocument
    {
        public Guid Id { get; set; }

        public string Text { get; set; } = string.Empty;

        public FlowchartNodeKind Kind { get; set; }

        public double X { get; set; }

        public double Y { get; set; }

        public double Width { get; set; }

        public double Height { get; set; }
    }

    /// <summary>
    /// 单条连接线的序列化结构。
    /// </summary>
    public sealed class FlowchartConnectionDocument
    {
        public Guid Id { get; set; }

        public Guid SourceNodeId { get; set; }

        public FlowchartAnchor SourceAnchor { get; set; }

        public Guid TargetNodeId { get; set; }

        public FlowchartAnchor TargetAnchor { get; set; }
    }
}
