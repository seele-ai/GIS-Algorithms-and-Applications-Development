# 数据管理工具箱（DataManagement）

这里放**数据管理工具箱**里的工具：一个工具 = 一个继承 `GIS.Toolbox.UI.ToolFormBase` 的界面类（命名空间建议
`GIS.Toolbox.Tools.DataManagement`），然后在 `ToolCatalog.RegisterDefaults(...)` 里登记一行：

```csharp
box.AddTool(ToolBoxControl.DataManagementCategory, "工具名", map => new GIS.Toolbox.Tools.DataManagement.XxxToolForm(map), "一句话说明");
```

可放的工具（示例）：合并图层（Merge）/ 转投影 / 几何检查与修复 / 按属性筛选导出。

界面骨架直接照抄 `UI/TemplateToolForm.cs`；输入项顺序与校验规则见工程根目录的 `README.md`。
