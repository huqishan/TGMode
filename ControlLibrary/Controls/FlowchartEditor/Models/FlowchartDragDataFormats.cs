namespace ControlLibrary.Controls.FlowchartEditor.Models
{
    /// <summary>
    /// 拖拽节点模板时使用的数据键。
    /// </summary>
    public static class FlowchartDragDataFormats
    {
        // 节点显示文本，旧版逻辑主要靠这个字段识别节点。
        public const string PaletteText = "WpfApp.Flowchart.PaletteText";
        // 节点类型，新版逻辑用它区分普通节点和判断节点。
        public const string PaletteNodeKind = "WpfApp.Flowchart.PaletteNodeKind";
        // 防止同一次拖拽 Drop 被重复处理。
        public const string DragId = "WpfApp.Flowchart.DragId";
    }
}
