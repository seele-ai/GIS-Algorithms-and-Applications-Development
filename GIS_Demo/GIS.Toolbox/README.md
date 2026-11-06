# GIS.Toolbox（工具箱）

主窗口**右侧**“工具箱”面板所在的独立工程。后续实现具体空间分析工具的同学，**代码和文件都放在本工程里**，不需要改动模块1/2/3 的任何代码。

## 目录结构

```
GIS.Toolbox/
├── GIS.Toolbox.csproj        工程文件（net48 + WinForms，引用 GIS 与 GIS.Display）
├── ToolBoxControl.cs         工具箱面板：树状结构（文件夹 = 工具箱、叶子 = 工具）、工具注册与打开
├── ToolCatalog.cs            ★ 工具登记表：后续同学**只需要改这个文件**来登记自己的工具
├── UI/
│   ├── ToolFormBase.cs       工具界面基类：统一六类输入项的顺序 + 右下角“运行” + 校验/结果摘要
│   └── TemplateToolForm.cs   模板工具（模板工具箱里那一个，照它写自己的工具界面）
└── Tools/                    ★ 后续同学把自己的工具放在这里（按工具箱分子目录）
    ├── Overlay/              叠加分析工具箱 → 叠加 / 裁剪 / 相交 / 擦除 …
    ├── Proximity/            邻近分析工具箱 → 缓冲 / 泰森多边形 / 最近要素 …
    └── DataManagement/       数据管理工具箱 → 合并 / 转投影 / 数据检查 / 查询 …
```

## 加一个工具（三步）

1. **写工具界面**：在 `Tools/<对应工具箱>/` 下新建类，继承 `GIS.Toolbox.UI.ToolFormBase`
   （命名空间建议 `GIS.Toolbox.Tools.Overlay` 这种），在构造函数里按需求规定的顺序添加六类输入项：

   ```csharp
   public sealed class IntersectToolForm : ToolFormBase
   {
       private readonly ComboBox input, other;

       public IntersectToolForm(MapControl map) : base(map, "相交（Intersect）")
       {
           input = AddInputFeaturesField("输入要素", null);          // 第一项是“当前所有图层的已选要素”，其后按图层顺序
           other = AddLayerField("其他图层");                        // 只有图层，没有“已选要素”
           AddTextParameterField("必要参数（手动输入）", "例如：0", "0");
           AddChoiceParameterField("必要参数（选择）", "保留全部", "只保留相交部分");
           AddCheckParametersField("框选参数", "保留属性", "结果加到地图");
           AddOutputNameField("输出图层名", "相交结果");
           FinishLayout();                                            // 放好结果区与右下角“运行”
       }

       protected override bool CollectInputs(out ToolRunInputs inputs, out string error)
       { /* 从上面的控件读入并校验，见 TemplateToolForm 的实现 */ }

       protected override void Execute(ToolRunInputs inputs)
       { /* 真正的算法：算完 map.AddLayer(新图层)，再 ShowResult("...") */ }
   }
   ```

   基类已经提供：`CandidateLayers(filter)`（顶层在前的候选图层）、`SelectedLayer(combo)`、
   `IsSelectionOption(combo)`、`SummarizeGeometry(features)`（“点 2 个、面 1 个”）、
   `AcceptedInputTypes`（声明本工具接受的几何类型，运行时自动校验）、`ShowResult(text)`。

2. **登记到工具箱**：在 `ToolCatalog.RegisterDefaults(...)` 里加一行

   ```csharp
   box.AddTool(ToolBoxControl.OverlayCategory, "相交（Intersect）",
       map => new GIS.Toolbox.Tools.Overlay.IntersectToolForm(map), "两个面图层求交，输出面图层");
   ```

3. **编译并运行**：`dotnet build GIS_program\GIS.sln`，启动 `GIS.Display.Demo` → 右侧工具箱 → 点对应工具箱的 `+` → 点工具名。

## 约定

- **一个工具一个界面类 + 一个登记行**；算法与界面放在一起（`Tools/<工具箱>/`），不要改模块3 的渲染代码。
- 输出图层统一通过 `Map.AddLayer(layer)` 加入地图，名字取界面上的“输出图层名”，避免与已有图层重名（基类会校验）。
- 输入要素既可以来自“当前所有图层的已选要素”（`ToolRunInputs.FromSelection`），也可以来自某个图层的全部要素（`ToolRunInputs.InputLayer`）。
- 工具跑完后请用 `ShowResult(...)` 把结果摘要写到窗口里，方便用户确认。

## 已实现的邻近分析工具

`Tools/Proximity/` 现已新增缓冲区、泰森多边形和多环缓冲区三个工具，并登记到 `ToolCatalog.RegisterDefaults(...)`。三个工具支持从当前选中要素或单个完整图层读取输入，结果统一作为新面图层加入地图。

- 缓冲区支持固定或数值字段距离、距离单位、平面或球面近似测地线、负面缓冲、线单侧与端头、仅保留输入面外部，以及不融合、全部融合和按字段融合。
- 泰森多边形支持默认外扩 10% 边界或当前地图范围，并可选择只输出来源编号或复制全部属性。
- 多环缓冲区支持非重叠环或重叠圆盘、距离去重排序、自定义距离字段名称及仅保留输入面外部。
- 工具会提示经纬度或未知坐标系风险，并对单个空几何、无效距离或计算失败进行跳过和汇总，不中断其余有效要素。

更详细的参数语义与当前算法边界见 `Tools/Proximity/README.md`。
