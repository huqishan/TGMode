using System;

namespace ControlLibrary.Controls.FlowchartEditor.Models
{
    public class FlowchartConnectionModel
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid SourceNodeId { get; set; }
        public FlowchartAnchor SourceAnchor { get; set; }
        public Guid TargetNodeId { get; set; }
        public FlowchartAnchor TargetAnchor { get; set; }
    }
}
