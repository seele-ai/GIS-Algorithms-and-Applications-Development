using System;

namespace GIS.Toolbox
{
    /// <summary>
    /// 工具箱的**登记表**——后续同学实现工具箱里的具体工具时，只需要改这一个文件。
    ///
    /// 每个工具一行：所属工具箱 / 工具名（显示在树里）/ 打开哪个工具界面 / 悬停说明。
    /// 工具界面从 <see cref="GIS.Toolbox.UI.ToolFormBase"/> 派生（照抄
    /// <see cref="GIS.Toolbox.UI.TemplateToolForm"/> 的骨架），放在
    /// <c>GIS.Toolbox\Tools\&lt;对应工具箱&gt;\</c> 目录下即可。
    ///
    /// 例：
    ///   box.AddTool(ToolBoxControl.OverlayCategory, "相交（Intersect）",
    ///       map => new GIS.Toolbox.Tools.Overlay.IntersectToolForm(map), "两个面图层求交");
    /// </summary>
    public static class ToolCatalog
    {
        /// <summary>登记默认的工具箱与工具（由 <see cref="ToolBoxControl"/> 在构造时调用）。</summary>
        public static void RegisterDefaults(ToolBoxControl box)
        {
            if (box == null) return;

            // 三个分析工具箱：先把节点建出来（空工具箱里会自动放“（暂无工具）”占位，+/- 依然可用）
            box.AddCategory(ToolBoxControl.OverlayCategory);
            box.AddCategory(ToolBoxControl.ProximityCategory);
            box.AddCategory(ToolBoxControl.DataManagementCategory);

            // 模板工具箱 → 模板工具：工具界面模板（后续同学照它写自己的工具界面）
            box.AddCategory(ToolBoxControl.TemplateCategory);
            box.AddTool(ToolBoxControl.TemplateCategory, ToolBoxControl.TemplateToolName,
                map => new GIS.Toolbox.UI.TemplateToolForm(map),
                "工具界面模板：输入要素 → 其他图层 → 必要参数（手动输入 / 选择）→ 框选参数 → 输出图层名 → 运行");

            // ↓↓↓↓↓↓ 后续同学在这里登记自己的工具（下面两行是示例，换成自己的类名后取消注释即可）↓↓↓↓↓↓
            // box.AddTool(ToolBoxControl.OverlayCategory, "相交（Intersect）",
            //     map => new GIS.Toolbox.Tools.Overlay.IntersectToolForm(map), "两个面图层求交，输出面图层");
            // box.AddTool(ToolBoxControl.ProximityCategory, "缓冲分析（Buffer）",
            //     map => new GIS.Toolbox.Tools.Proximity.BufferToolForm(map), "按给定距离生成缓冲面");
        }
    }
}
