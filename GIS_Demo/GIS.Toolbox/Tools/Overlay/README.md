# 叠加分析工具箱（Overlay）

这里放**叠加分析工具箱**里的工具：一个工具 = 一个继承 `GIS.Toolbox.UI.ToolFormBase` 的界面类（命名空间建议
`GIS.Toolbox.Tools.Overlay`），然后在 `ToolCatalog.RegisterDefaults(...)` 里登记一行：

```csharp
box.AddTool(ToolBoxControl.OverlayCategory, "工具名", map => new GIS.Toolbox.Tools.Overlay.XxxToolForm(map), "一句话说明");
```

可放的工具（示例）：叠加（Overlay）/ 相交（Intersect）/ 裁剪（Clip）/ 擦除（Erase）/ 合并（Union）/ 对称差（SymDiff）。

界面骨架直接照抄 `UI/TemplateToolForm.cs`；输入项顺序与校验规则见工程根目录的 `README.md`。

## 已实现（王小宇）

- 裁剪（Clip）：输入要素（点线面）与裁剪面（面图层，取已选要素，无则整层）重叠的部分；输出图层名可自定义。
- 擦除（Erase）：只保留输入要素位于擦除面之外的部分。
- 相交（Intersect）：输入要素与相交图层的几何交集，输出维数取两者中较低的（面×面输出面、线×面输出线、点×线/点×面输出点、线×线输出交点）。
- 交集取反（Symmetrical Difference）：输出输入要素与更新要素中互不重叠的部分（两组要素维数需一致）。

算法引擎在 Tools/Overlay/OverlayEngine.cs（面×面布尔：边界打断 + 中点归类 + 环重建；容差按数据范围自适应）。
输入要素优先取"当前所有图层的已选要素"，未选则取整层；结果要素保留输入要素的属性。
