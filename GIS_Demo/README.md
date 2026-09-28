# 模块2：图形显示与图层管理（白宇丹）

> **说明**：本模块已并入根目录统一解决方案 [`GIS.sln`](../GIS.sln)。整体分工、目录结构与构建/运行命令以 [根目录 README](../README.md) 为准；下文为本模块的详细设计说明。

本模块将原 myGIS/miniGIS 工程中的地图浏览、图层树和选择交互思路，适配为引用小组模块1 `GIS` 类库的独立 Windows Forms 控件。采用 C# 7.3、.NET Framework 4.8；没有复制或修改模块1的几何、要素与投影类。

## 打开与运行

1. 在 Visual Studio 2022 中打开本目录的 `GIS.Display.sln`。
2. 右键 `GIS.Display.Demo` → **设为启动项目**。
3. 按 **F5** 或 **Ctrl+F5**。启动后自动显示5个样例图层、9个要素，不需要外部 shp 文件或数据库。
4. 运行环境需 Windows 和 .NET Framework 4.8；开发环境需“.NET 桌面开发”工作负载、4.8 Targeting Pack 和支持 SDK 风格项目的 .NET SDK。
5. 必须保留本模块与 `MapObjects` 的同级目录关系。引用的是 `GIS.csproj`，不是某台电脑上的 DLL。

本机验证环境：Visual Studio 2022 配套的 .NET Framework 4.8 参考程序集、.NET SDK 9.0.101。
图形项目不依赖模块1的 net8.0 控制台自测程序。若团队以后将主界面统一为 .NET 8，需要另行统一本模块项目目标框架；当前交付为 net48。

命令行（在本目录执行）：

```powershell
dotnet build GIS.Display.sln -m:1
.\GIS.Display.Demo\bin\Debug\net48\GIS.Display.Demo.exe
```

## 已实现功能

- 基于 `MapControl : UserControl` 的双缓冲地图显示。
- 点、折线、多边形、多点、复合折线、复合多边形的基本绘图；支持多边形孔洞。
- 点击放大/缩小、框放大、以鼠标位置为锚点的滚轮缩放、左键平移和临时中键平移。
- 图层添加、移除、上移/下移、显示/隐藏、定位、切换是否可选。
- 点选（5像素容限）、任意方向拖动框选、清空选择、高亮选择。
- 新建、添加、移除、交集四种选择方式。
- 地图坐标、比例尺、选中数量显示；只读的样例选择结果列表。
- 空地图、单点和零宽/零高范围处理；窗口变化保持地图中心；Esc或失去鼠标捕获时取消未完成拖动。

缩小工具按释放位置缩小一半；不提供“框缩小”。选择作用于所有可见且可选图层，重叠要素可同时选中。框选为几何与框相交，不要求完全包含。

## 项目结构

| 路径 | 作用 |
|---|---|
| GIS.Display.sln | 本模块解决方案，含模块1的项目引用 |
| GIS.Display/MapControl.cs | 视图管理、鼠标交互、选择状态、重绘 |
| GIS.Display/LayerManagerControl.cs | 图层树和图层操作按钮 |
| GIS.Display/BasicGeometryDrawer.cs | 基本几何与简单符号绘制，供模块3替换/复用 |
| GIS.Display/IMapControl.cs | 地图接口、渲染扩展接口、事件参数 |
| GIS.Display.Demo/ | 独立演示程序和虚构样例数据 |
| GIS.Display.Checks/ | 不依赖测试框架包的自动检查程序 |
| 上传步骤.md | 白宇丹提交到小组仓库的操作步骤 |

## 与模块1对接

`MapControl` 内嵌 `GIS.MapTransform`；缩放、平移及坐标换算调用现有方法。
`Layer.FeatureClass` 保存要素数据，空间查询调用 `SearchByPoint/SearchByBox`，
选择集更新调用 `SelectTools.ExcuteSelect`。

`Layers[0]` 在底部，最后一个图层在顶部；图层面板反向列出，所以屏幕列表顶部代表地图最上层。
重复添加同一 Layer 对象不会产生重复图层。

图层默认可选。隐藏或设为不可选时会清除该图层的选择。选择集合由地图实例按 Layer 对象管理，
`GetSelection(layer)` 返回集合快照，里面的 Feature 仍指向原始数据，便于编辑模块操作。

## 最小接入示例

```csharp
using GIS;
using GIS.Display;
using System.Windows.Forms;

var map = new MapControl { Dock = DockStyle.Fill };
Controls.Add(map);

var fc = new FeatureClass("观测点", GeometryTypeConstant.Point);
fc.Add(new Feature(new GIS.Point(100, 200), fc.Fields));
var layer = new Layer("观测点", fc);
map.AddLayer(layer);
map.FullExtent();

map.SelectionChanged += (sender, args) =>
{
    Features selected = map.GetSelection(layer);
    // 将选中要素传给属性窗口或编辑工具。
};
```

注意：`GIS.Point` 是几何点；屏幕点为 `System.Drawing.Point`；`GIS.Coordinate` 是地图坐标值。

### 地图接口

`IMapControl` 是本模块新增的对接接口，不是修改模块1的接口。主要方法：

- `AddLayer / RemoveLayer / MoveLayer / SetLayerVisible / SetLayerSelectable`
- `Zoom(ratio) / Pan(deltaX, deltaY) / SetExtent / GetExtent / FullExtent / RefreshMap`
- `GetSelection / SetSelection / ClearSelection`
- `LayersChanged / ViewChanged / SelectionChanged` 事件

`Zoom` 大于1为放大，`Pan` 参数为地图单位，语义遵循模块1的 `MapTransform.PanDelta`。
`MapControl` 另外提供 `SelectAt / SelectBox / ZoomAt / MapMouseMoved / OverlayPaint`。

所有控件操作在 UI 线程执行。导入或数据库查询可在后台完成，加入地图时用窗体的 Invoke/BeginInvoke。

### 与模块3（图层渲染）对接

`ILayerRenderer.DrawLayer(Graphics, Layer, MapTransform, Rectangle)` 接收可见图层与当前视图。
实现该接口后赋给 `map.Renderer`，即可替换默认绘图。模块2统一负责图层顺序、视图和选择高亮；
模块3可负责唯一值、分级渲染、标签等。也可在自定义渲染器中调用 `BasicGeometryDrawer.DrawGeometry`。

Graphics 由地图控件拥有，渲染器不得 Dispose。每个图层绘制前后保存/恢复 Graphics 状态。
简单符号尺寸遵循模块1的毫米约定。图层 Symbol 为空时使用固定的默认点线面样式。

### 与模块4/5对接

- 导入或查询得到 `FeatureClass` 后包装为 `Layer`，调用 `map.AddLayer(layer)`。
- 编辑使用 `GetSelection` 获取原 Feature 引用，更新数据后调用 `RefreshMap`；删除后自动清理失效选择。
- 绘制编辑草图可以订阅 `OverlayPaint`（高亮之后、拖框之前绘制）。
- 工具切换、交互编辑模式由后续集成统一约定；本模块不实现要素新建、顶点编辑、文件导入导出或数据库连接。
- 改变 Layer.Visible 请优先调用 `SetLayerVisible`，保证图层面板同步。

## 从旧代码迁移了什么

| 旧文件/逻辑 | 本模块处理 |
|---|---|
| miniGIS/mgisMap.cs | 保留地图控件、分层绘图、高亮和缓冲的设计；视图数学改用 MapTransform |
| miniGIS/mgisMapDrawingTools.cs | 改为 GIS.Geometry / Symbol，新增单体/复合类型分派和显式孔洞绘制 |
| myGIS/frmMain.cs 的地图鼠标事件 | 拆入 MapControl，滚轮改为以鼠标为锚点，增加点击抖动容限和取消拖动 |
| frmMain.cs 图层树事件 | 拆成 LayerManagerControl |
| MapOpManager.cs 的浏览/选择状态 | 精简为 MapInteractionMode，不引入旧编辑工具依赖 |
| 旧 mgisFeature/mgisMapLayer/投影类 | 不复制，直接使用模块1的类型 |
| IO、编辑、分类渲染与注记窗体 | 不放入本模块，避免与模块3/4职责重复 |

这是按旧实现思路重新拆分、适配后的模块，不是把原项目整体搬入仓库；旧工程未被改动。

## 验证

在本目录执行：

```powershell
dotnet build GIS.Display.sln -m:1
.\GIS.Display.Checks\bin\Debug\net48\GIS.Display.Checks.exe .\GIS.Display.Checks\obj\validation
```

已在本机通过 **41项自动检查**，整套解决方案 Debug 编译 **0警告、0错误**。
检查覆盖选择集四种集合运算、隐藏/不可选图层、鼠标拖动与滚轮锚点、反向框选、
删除后的选择清理、窗口调整、空地图、单点范围、孔洞像素/选择行为、
图层覆盖顺序、复合面部件、渲染扩展接口和演示图层面板。

检查程序在透明测试窗体中完成控件创建和绘图，并将预览与结果存到 obj/validation（不入库）。
这不代替团队最终人工验收；建议运行演示检查拖动手感、缩放、图层勾选和与其他模块的集成。

## 范围与限制

- 所有图层坐标应预先统一到同一个平面坐标系。本模块不自动重投影。
- 默认 Mpu=1，即每地图单位1米；样例为虚构的米制坐标，不是真实校园地理数据。
- 基本符号绘制与内存要素遍历适合课程规模数据；未进行大数据性能测试。
- 未接入模块3的专题渲染、模块4的真实文件导入和编辑、模块5的数据库。
- 当前分工依据仓库两份 README；未取得 0915汇报.pptx，因此详细验收仍需对照课堂要求。

