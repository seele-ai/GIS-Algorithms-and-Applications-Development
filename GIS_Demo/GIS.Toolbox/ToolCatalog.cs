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

            // 叠加分析工具箱（王小宇）：裁剪 / 擦除 / 相交 / 交集取反
            box.AddTool(ToolBoxControl.OverlayCategory, "裁剪（Clip）",
                map => new GIS.Toolbox.Tools.Overlay.ClipToolForm(map),
                "提取与裁剪要素相重叠的输入要素（输入为已选要素或整层，裁剪要素为面）");
            box.AddTool(ToolBoxControl.OverlayCategory, "擦除（Erase）",
                map => new GIS.Toolbox.Tools.Overlay.EraseToolForm(map),
                "只保留输入要素位于擦除面之外的部分（输入为已选要素或整层，擦除要素为面）");
            box.AddTool(ToolBoxControl.OverlayCategory, "相交（Intersect）",
                map => new GIS.Toolbox.Tools.Overlay.IntersectToolForm(map),
                "计算输入要素与相交图层的几何交集，输出维数取两者中较低的维数（线×线输出交点）");
            box.AddTool(ToolBoxControl.OverlayCategory, "交集取反（Symmetrical Difference）",
                map => new GIS.Toolbox.Tools.Overlay.SymDiffToolForm(map),
                "输出输入要素与更新要素中互不重叠的部分（两组要素维数需一致）");

            // 数据管理工具箱：按课程要求的前六项顺序登记。
            for (int i = 0; i < 6; i++)
            {
                var operation = (GIS.Toolbox.Tools.DataManagement.ManagementOperation)i;
                box.AddTool(ToolBoxControl.DataManagementCategory, GIS.Toolbox.Tools.DataManagement.DataManagementToolForm.Titles[i],
                    map => new GIS.Toolbox.Tools.DataManagement.DataManagementToolForm(map, operation), "对输入要素执行数据管理并生成新图层");
            }

            // 邻近分析工具箱：缓冲区 / 泰森多边形 / 多环缓冲区
            box.AddTool(ToolBoxControl.ProximityCategory, "缓冲区（Buffer）",
                map => new GIS.Toolbox.Tools.Proximity.BufferToolForm(map),
                "支持固定/字段距离、平面/球面近似测地线、负面缓冲、线单侧及三种融合方式");
            box.AddTool(ToolBoxControl.ProximityCategory, "泰森多边形（Thiessen）",
                map => new GIS.Toolbox.Tools.Proximity.ThiessenToolForm(map),
                "从点要素创建泰森多边形，可用默认外扩 10% 边界或当前地图范围");
            box.AddTool(ToolBoxControl.ProximityCategory, "多环缓冲区（Multiple Ring Buffer）",
                map => new GIS.Toolbox.Tools.Proximity.MultiRingBufferToolForm(map),
                "按多个距离创建平面或球面近似测地线非重叠环/重叠圆盘，可仅保留输入面外部区域");
        }
    }
}
