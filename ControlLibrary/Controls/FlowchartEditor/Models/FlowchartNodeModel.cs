using System;
using System.Windows;

namespace ControlLibrary.Controls.FlowchartEditor.Models
{
    /// <summary>
    /// 流程图节点的模型数据，负责保存节点文本、类型、位置和尺寸。
    /// </summary>
    public class FlowchartNodeModel
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Text { get; set; } = string.Empty;
        public string MetadataJson { get; set; } = string.Empty;
        // 判断节点会画成横向菱形，其他节点保持圆角矩形。
        public FlowchartNodeKind Kind { get; set; } = FlowchartNodeKind.Process;
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; } = 150;
        public double Height { get; set; } = 70;

        public Rect GetBounds()
        {
            // 路由器用这个矩形作为避障基础，再向外扩一定距离。
            return new Rect(X, Y, Width, Height);
        }

        public Point GetAnchorPoint(FlowchartAnchor anchor)
        {
            // 四个连接点固定在节点外接矩形的上下左右中点。
            // 判断节点的菱形顶点正好也落在这四个位置。
            return anchor switch
            {
                FlowchartAnchor.Top => new Point(X + (Width / 2), Y),
                FlowchartAnchor.Right => new Point(X + Width, Y + (Height / 2)),
                FlowchartAnchor.Bottom => new Point(X + (Width / 2), Y + Height),
                _ => new Point(X, Y + (Height / 2))
            };
        }
    }
}

