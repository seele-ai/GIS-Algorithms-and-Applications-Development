# 邻近分析工具箱（Proximity）

这里放**邻近分析工具箱**里的工具：一个工具 = 一个继承 `GIS.Toolbox.UI.ToolFormBase` 的界面类（命名空间建议
`GIS.Toolbox.Tools.Proximity`），然后在 `ToolCatalog.RegisterDefaults(...)` 里登记一行：

```csharp
box.AddTool(ToolBoxControl.ProximityCategory, "工具名", map => new GIS.Toolbox.Tools.Proximity.XxxToolForm(map), "一句话说明");
```

可放的工具（示例）：缓冲（Buffer）/ 泰森多边形（Thiessen）/ 最近要素（Nearest）/ 点距统计。

界面骨架直接照抄 `UI/TemplateToolForm.cs`；输入项顺序与校验规则见工程根目录的 `README.md`。
