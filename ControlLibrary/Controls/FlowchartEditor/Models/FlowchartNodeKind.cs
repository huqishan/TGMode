using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ControlLibrary.Controls.FlowchartEditor.Models
{
    /// <summary>
    /// 流程图节点类型。当前只有判断节点会影响外形，其他类型保留给后续扩展。
    /// </summary>
    public enum FlowchartNodeKind
    {
        Start,
        Process,
        Decision,
        End
    }
}
