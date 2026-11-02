# 叠加分析工具箱（Overlay）

这里放**叠加分析工具箱**里的工具：一个工具 = 一个继承 `GIS.Toolbox.UI.ToolFormBase` 的界面类（命名空间建议
`GIS.Toolbox.Tools.Overlay`），然后在 `ToolCatalog.RegisterDefaults(...)` 里登记一行：

```csharp
box.AddTool(ToolBoxControl.OverlayCategory, "工具名", map => new GIS.Toolbox.Tools.Overlay.XxxToolForm(map), "一句话说明");
```

可放的工具（示例）：叠加（Overlay）/ 相交（Intersect）/ 裁剪（Clip）/ 擦除（Erase）/ 合并（Union）/ 对称差（SymDiff）。

界面骨架直接照抄 `UI/TemplateToolForm.cs`；输入项顺序与校验规则见工程根目录的 `README.md`。
