# GIS 软件设计与实现（小组作业）

使用 C# 开发一款简易 GIS 软件，实现：交互式数据录入、外部数据导入导出、PostGIS 数据管理、图形基本显示（漫游/缩放）、鼠标交互选择（点选/框选）、符号及属性管理（选做）。

## 整体分工

依据《模块设计+分工.docx》《说明+分工（内部）.docx》，五个模块的调用链：

```
1 数据结构/投影 → 2 地图显示 → 3 图层渲染与编辑 → 4 数据编辑/IO ↔ 5 PostGIS
```

| 模块 | 负责人 | 内容 | 状态 |
|---|---|---|---|
| 模块1 数据结构设计与地图投影实现 | 王小宇 | OGC 简单要素数据结构 + 地图投影/坐标转换 | 已完成（194 项自测全部通过） |
| 模块2 图形显示与图层管理 | 白宇丹 | 地图绘制、图层管理、鼠标选择 | 已完成（135 项自动检查通过） |
| 模块3 图层渲染与编辑 | 赵佶昊 | 点/线/面符号编辑器、唯一值/分级渲染、注记、色带、分带选择、渲染符号存取、坐标系统一到 WGS84、GCJ-02、自定义线段特性 | 已完成（135 项自动检查通过） |
| 模块4 数据编辑与导入导出 | 李君迟 | shp 导入导出、要素选取/创建/编辑 | 待补充 |
| 模块5 与 PostGIS 交互 | 李雨晴 | 从数据库读取图层、保存图层 | 待补充 |

## 目录约定

每个模块一个顶层文件夹，命名格式：`模块N_模块名_姓名`。全组共用一个统一解决方案 `GIS.sln`（位于本目录根），各模块工程以项目引用方式纳入同一解决方案。

```
GIS_program/
├── GIS.sln                     统一解决方案（全组唯一入口）
├── README.md                   本文档
├── MapObjects/
│   ├── GIS/                    类库（netstandard2.0，全组共用）
│   │   ├── Renderer/           简单/唯一值/分级渲染器、色带 ColorRamp.cs、渲染符号文件 RendererFile.cs（模块3）
│   │   ├── Projection/         投影（高斯-克吕格/UTM/Lambert）、UTM 纬度行 UtmGridZone.cs、大地基准 GeographicDatum.cs
│   │   └── Symbol/             点/线/面符号（线符号含多条偏移线、面符号含多条边界）
│   ├── GISDemo/                控制台自测程序（194 项检查）
│   └── README.md
└── GIS_Demo/
    ├── GIS.Display/            地图控件类库（net48 WinForms）
    │   ├── BasicGeometryDrawer.cs  渲染器驱动的点线面绘制 + 注记 + 符号预览
    │   ├── LayerControl.cs         图层行控件 + 图层管理面板（模块3）
    │   └── UI/                     颜色选择器、点/线/面符号与渲染/注记编辑器、色带编辑器、分带选择、投影到 WGS84（模块3）
    ├── GIS.Display.Demo/       演示程序（WinExe，内置样例数据）
    ├── GIS.Display.Checks/     自动检查程序（135 项检查）
    └── README.md
```

解决方案包含 5 个工程：`GIS`、`GISDemo`、`GIS.Display`、`GIS.Display.Demo`、`GIS.Display.Checks`。

## 编译与运行

环境：Windows、.NET SDK 8.0.403（或更高）、.NET Framework 4.8 目标包（VS2022 “.NET 桌面开发”工作负载自带）。

在本目录（`GIS_program/`）执行：

```powershell
# 编译整个解决方案（Debug）
dotnet build GIS.sln -m:1

# 模块1 自测（194 项检查，控制台）
dotnet run --project MapObjects\GISDemo

# 模块2+3 自动检查（135 项检查）
.\GIS_Demo\GIS.Display.Checks\bin\Debug\net48\GIS.Display.Checks.exe `
  .\GIS_Demo\GIS.Display.Checks\obj\validation

# 模块2 演示程序（图形界面，启动后自动显示 5 个样例图层、9 个要素）
.\GIS_Demo\GIS.Display.Demo\bin\Debug\net48\GIS.Display.Demo.exe
```

或用 Visual Studio 2022 打开 `GIS.sln` 后直接按 **F5**（`GIS.Display.Demo` 已排在解决方案第一位，首次打开即为默认启动项目；若未自动选中，右键 `GIS.Display.Demo` → **设为启动项目** 再按 F5）。类库 `GIS` 目标框架为 `netstandard2.0`，WinForms 主程序无论用 .NET Framework 4.8 还是 .NET 6/8 都可直接项目引用。

---

## 模块1：数据结构设计与地图投影实现（王小宇）

> 本节内容合并自 `MapObjects/README.md`。

参考 OGC 简单要素标准设计空间要素的数据结构与内置函数，实现投影方法，完成经纬度坐标与地图坐标间的相互转换。本模块是全组开发的基础，`Geometry`、`Feature`、`FeatureClass`、`Layer`、`Envelope` 是各模块间提前约定的核心接口，其余模块直接引用本类库并行开发。

### 工程结构

```
MapObjects/
├── GIS/                    类库（netstandard2.0）
│   ├── Geometry/           坐标、范围、几何对象与几何算法
│   ├── Feature/            字段、属性、要素、要素类（含点选/框选/表达式查询）
│   ├── Layer/              图层
│   ├── Symbol/             符号体系（渲染模块在此基础上扩展）
│   ├── Projection/         投影坐标系统与坐标转换
│   └── Mapping/            地图窗口视图变换（屏幕坐标↔地图坐标）
└── GISDemo/                控制台自测程序（194 项检查，全部通过）
```

### 类型清单

| 类别 | 类型 | 说明 |
|---|---|---|
| 几何 | `Coordinate` | 地图坐标（X东/Y北），值类型 |
| 几何 | `Envelope` | 外接矩形（MinX/MaxX/MinY/MaxY），含 Contains/IntersectsWith/Intersection/ExpandToInclude |
| 几何 | `Geometry`（抽象） | GetEnvelope / Contains / Intersects / Distance / Translate / ToWKT / FromWKT |
| 几何 | `Point` `LineString` `Polygon` | 点、折线（Length）、多边形（外环+内环，Area/Perimeter） |
| 几何 | `MultiPoint` `MultiLineString` `MultiPolygon` | 复合几何对象 |
| 几何 | `GeometryTools`（静态） | 射线法判包含、点到线段距离、矩形/线段求交、鞋带面积、折线中点等 |
| 几何 | `GeometryWkt`（静态） | OGC WKT 文本读写（7 种几何类型，供导入导出模块复用） |
| 要素 | `Field` / `Fields` | 属性字段与表结构模式 |
| 要素 | `Attributes` | 属性值集合（与 Fields 按位置对应，可按字段名取值） |
| 要素 | `Feature` | 要素 = Geometry + Attributes |
| 要素 | `Features` / `SelectMethodConstant` / `SelectTools` | 要素集合与选择集四种集合运算 |
| 要素 | `FeatureClass` | Fields + Features + 几何类型约束 + ProjectionCS；SearchByPoint / SearchByBox / SearchByExpression |
| 图层 | `Layer` | FeatureClass + Symbol + Visible |
| 符号 | `Symbol`（抽象）及三个简单符号 | SimpleMarkerSymbol / SimpleLineSymbol / SimpleFillSymbol（渲染模块据此扩展） |
| 投影 | `ProjectionCS`（抽象） | 椭球参数 + TransferToProjCo / TransferToLngLat / ToUnits / ToMeters |
| 投影 | `ProjGauss_Kruger` | 高斯-克吕格（3度带/6度带，自动定中央经线；支持北京54/西安80/CGCS2000/WGS84椭球） |
| 投影 | `ProjLambert` | Lambert 等角圆锥投影（双标准纬线） |
| 投影 | `ProjUTM` | UTM（k0=0.9996，6度带，南北半球） |
| 投影 | `Ellipsoids` | WGS84 / CGCS2000 / 西安80 / 北京54（Krassowsky_1940）椭球常数 |
| 投影 | `CoordinateTransform` | Project / Unproject / 任意两坐标系 Transform |
| 视图 | `MapTransform` | MapToScreen / ScreenToMap / ZoomByCenter / ZoomToExtent / ZoomToScale / PanDelta / PanTo / GetExtent / FullExtent |

### 内置函数对照（分工 PPT 模块1 页约定的接口）

| PPT 约定 | 本实现 |
|---|---|
| `ScreenToMap(Point screenPoint)` | `MapTransform.ScreenToMap(System.Drawing.Point)` → `Coordinate` |
| `MapToScreen(Coordinate mapPoint)` | `MapTransform.MapToScreen(Coordinate)` → `System.Drawing.Point` |
| `Project(Coordinate)` | `CoordinateTransform.Project(Coordinate)` |
| `Unproject(Coordinate)` | `CoordinateTransform.Unproject(Coordinate)` |
| `GetEnvelope()` | `Geometry.GetEnvelope()` |
| `Contains(Coordinate point)` | `Geometry.Contains(Coordinate)`（另提供容限重载） |
| `Intersects(Envelope envelope)` | `Geometry.Intersects(Envelope)` |
| `Distance(Coordinate point)` | `Geometry.Distance(Coordinate)` |

### 与旧项目（gis/gisde 的 mo 类库）对照

| 旧项目 | 本模块 | 说明 |
|---|---|---|
| `moPoint` | `Coordinate`（值类型） | 地图坐标 |
| `moPoints` | `Points` | 点集 |
| `moRectangle` | `Envelope` | 构造函数同为 (MinX, MaxX, MinY, MaxY) |
| `moFeature` / `moFeatures` | `Feature` / `Features` | 集合运算 Replace/Union/Except/Intersect 均保留 |
| `moField` / `moAttributeFields` | `Field` / `Fields` | |
| `moMapLayer`（SearchByPoint/SearchByBox/SearchByExpression） | `FeatureClass` | 查询逻辑原样迁移，表达式查询仍走 DataTable.Select |
| `moMapControl` 的视图参数与缩放平移 | `MapTransform` | 比例尺+偏移量模型、dpm/mpu 换算、ZoomMapExtentToScreenExtent 均与旧代码一致 |
| `moSimpleMarkerSymbol` 等 | `SimpleMarkerSymbol` 等 | 含随机配色逻辑（渲染模块做唯一值渲染可直接用） |
| 课件第十章 `ProjectionCS`/`ProjLambert`/`ProjGauss_Kruger`/`ProjUTM` | 同名类 | 成员与课件类型设计一致，TransferToProjCo/TransferToLngLat 命名保留 |

### 给各组员的对接说明

- **白宇丹（模块2 图形显示与图层管理）**：新建 `MapControl` 时内嵌一个 `MapTransform`，鼠标事件里用 `ScreenToMap/MapToScreen` 做坐标换算；缩放平移直接调 `ZoomByCenter/ZoomToExtent/PanDelta`；图层列表用 `Layer.Visible` 控制显示；点选/框选调 `FeatureClass.SearchByPoint/SearchByBox`，选择集用 `SelectTools.ExcuteSelect` 按四种方式更新。参考旧项目 `IMapControl` 接口（AddLayer/RemoveLayer/Zoom/Pan/SetExtent/GetExtent/RefreshMap）。
- **赵佶昊（模块3 图层渲染与编辑）**：继承 `Symbol` 增加符号类型；`Geometry.GetEnvelope()` 做要素级粗判，`Polygon.Area`、`LineString.Length` 可用于分级渲染分段；注记定位点可用 `LineString.GetMidPoint()` 或旧项目 moMapTools 里的扫描线法。
- **李君迟（模块4 数据编辑与导入导出）**：shp 导入后按 `Field/Fields` 建表结构、按 `FeatureClass.Add` 入库（自动校验几何类型）；WKT 可作为中间格式（`Geometry.FromWKT/ToWKT`）；编辑用 `Feature.Clone()` 保留原要素、`Geometry.Translate` 平移、`Points.Insert/RemoveAt` 移动顶点。
- **李雨晴（模块5 PostGIS 交互）**：`FeatureClass.ProjectionCS` 记录图层坐标系，入库前可用 `CoordinateTransform.Transform` 统一投影；WKT 与 PostGIS 的 `ST_AsText/ST_GeomFromText` 直接对应；属性表结构由 `Fields` 描述，可映射为 `CREATE TABLE` 列定义。

### 注意事项

1. 本模块中的 `Point` 是几何点类；WinForms 代码里的屏幕点 `System.Drawing.Point` 与其重名，在同时用到两者的文件里请写全名或加 `using` 别名。
2. `Coordinate` 中经纬度的存放约定：X=经度、Y=纬度（单位度）；投影坐标 X=东方向（y，含东伪偏移）、Y=北方向（x）。
3. 投影正算输出恒为米；`LinearUnit` 属性仅描述坐标系统单位，配合 `ToUnits/ToMeters` 换算。
4. 投影公式采用 USGS/Snyder 横轴墨卡托与 Lambert 2SP 标准级数式，经测试（北京/上海/广州/成都/乌鲁木齐/哈尔滨/拉萨/武汉 8 点、4 种投影）正反算闭合误差均小于 1 毫米；跨带使用时请按点所在带号构造投影（`GetZone3/GetZone6/GetZone` 辅助函数已提供）。
5. 折线/多边形与 `Envelope` 的 `Intersects` 为快速相交判断（顶点入盒、线段穿盒、盒角入面），与旧项目 moMapTools 语义一致，可满足框选与显示裁剪需求；精确的 OGC 九交关系不在本模块范围。

---

## 模块2：图形显示与图层管理（白宇丹）

> 本节内容合并自 `GIS_Demo/README.md`。

将原 myGIS/miniGIS 工程中的地图浏览、图层树和选择交互思路，适配为引用小组模块1 `GIS` 类库的独立 Windows Forms 控件。采用 C# 7.3、.NET Framework 4.8；没有复制或修改模块1的几何、要素与投影类。

### 已实现功能

- 基于 `MapControl : UserControl` 的双缓冲地图显示。
- 点、折线、多边形、多点、复合折线、复合多边形的基本绘图；支持多边形孔洞。
- 点击放大/缩小、框放大、以鼠标位置为锚点的滚轮缩放、左键平移和临时中键平移。
- 图层添加、移除、上移/下移、显示/隐藏、定位、切换是否可选。
- 点选（5像素容限）、任意方向拖动框选、清空选择、高亮选择。
- 新建、添加、移除、交集四种选择方式。
- 地图坐标、比例尺、选中数量显示；只读的样例选择结果列表。
- 空地图、单点和零宽/零高范围处理；窗口变化保持地图中心；Esc 或失去鼠标捕获时取消未完成拖动。

缩小工具按释放位置缩小一半；不提供“框缩小”。选择作用于所有可见且可选图层，重叠要素可同时选中。框选为几何与框相交，不要求完全包含。

### 项目结构

| 路径 | 作用 |
|---|---|
| GIS.Display/MapControl.cs | 视图管理、鼠标交互、选择状态、重绘 |
| GIS.Display/LayerManagerControl.cs | 图层树和图层操作按钮 |
| GIS.Display/BasicGeometryDrawer.cs | 基本几何与简单符号绘制，供模块3替换/复用 |
| GIS.Display/IMapControl.cs | 地图接口、渲染扩展接口、事件参数 |
| GIS.Display.Demo/ | 独立演示程序和虚构样例数据 |
| GIS.Display.Checks/ | 不依赖测试框架包的自动检查程序 |

### 与模块1对接

`MapControl` 内嵌 `GIS.MapTransform`；缩放、平移及坐标换算调用现有方法。
`Layer.FeatureClass` 保存要素数据，空间查询调用 `SearchByPoint/SearchByBox`，
选择集更新调用 `SelectTools.ExcuteSelect`。

`Layers[0]` 在底部，最后一个图层在顶部；图层面板反向列出，所以屏幕列表顶部代表地图最上层。
重复添加同一 Layer 对象不会产生重复图层。

图层默认可选。隐藏或设为不可选时会清除该图层的选择。选择集合由地图实例按 Layer 对象管理，
`GetSelection(layer)` 返回集合快照，里面的 Feature 仍指向原始数据，便于编辑模块操作。

### 最小接入示例

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

### 与模块3（图层渲染与编辑）对接

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

### 从旧代码迁移了什么

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

### 验证

在本目录（`GIS_program/`）执行：

```powershell
dotnet build GIS.sln -m:1
.\GIS_Demo\GIS.Display.Checks\bin\Debug\net48\GIS.Display.Checks.exe `
  .\GIS_Demo\GIS.Display.Checks\obj\validation
```

已通过 **41项自动检查**，整套解决方案 Debug 编译 **0警告、0错误**。
检查覆盖选择集四种集合运算、隐藏/不可选图层、鼠标拖动与滚轮锚点、反向框选、
删除后的选择清理、窗口调整、空地图、单点范围、孔洞像素/选择行为、
图层覆盖顺序、复合面部件、渲染扩展接口和演示图层面板。

检查程序在透明测试窗体中完成控件创建和绘图，并将预览与结果存到 obj/validation（不入库）。
这不代替团队最终人工验收；建议运行演示检查拖动手感、缩放、图层勾选和与其他模块的集成。

### 范围与限制

- 所有图层坐标应预先统一到同一个平面坐标系。本模块不自动重投影。
- 默认 Mpu=1，即每地图单位1米；样例为虚构的米制坐标，不是真实校园地理数据。
- 基本符号绘制与内存要素遍历适合课程规模数据；未进行大数据性能测试。
- 未接入模块3的专题渲染、模块4的真实文件导入和编辑、模块5的数据库。
- 当前分工依据仓库两份 README；未取得 0915汇报.pptx，因此详细验收仍需对照课堂要求。

---

## 模块3：图层渲染与编辑（赵佶昊）

> 代码位置：`MapObjects/GIS/Renderer/`（渲染器、色带、渲染符号文件）、`MapObjects/GIS/Label/`（注记）、`MapObjects/GIS/Symbol/`（符号，在模块1 基础上扩展）、`GIS_Demo/GIS.Display/BasicGeometryDrawer.cs`（绘制）、`GIS_Demo/GIS.Display/LayerControl.cs`（图层面板）、`GIS_Demo/GIS.Display/UI/`（全部设置对话框）。
>
> 职责边界：本模块**不修改**模块1 已约定的 `Geometry`/`Feature`/`FeatureClass`/`Layer`/`Envelope` 接口，只在 `Symbol` 上扩展、在 `Renderer`/`Label` 上新增；通过模块2 预留的 `ILayerRenderer`/`ILayerLabelRenderer` 与 `MapControl.Renderer` 接入，不改动模块2 的交互逻辑。模块4/5 需要的接口见 **3.9**、**3.10**。

### 3.1 渲染总体结构：从 Feature 到 Layer 再到像素

| 层次 | 类型（模块） | 职责 |
|---|---|---|
| 坐标 | `Coordinate`、`Points`（模块1） | 地图坐标（经纬度或投影坐标），X=东/经度，Y=北/纬度 |
| 几何 | `Geometry` 及 `Point`/`LineString`/`Polygon`/`MultiPoint`/`MultiLineString`/`MultiPolygon`（模块1） | 点/线/面与复合几何 |
| 数据 | `Feature` = Geometry + `Attributes`；`FeatureClass` = `Fields` + `Features` + 几何类型 + `ProjectionCS`（模块1） | 要素与要素类（图层的数据源） |
| 图层 | `Layer` = FeatureClass + **Renderer** + **LabelRenderer** + `Symbol` + `Visible` + **GeographicDatum** + **WGS84 经纬度快照**（模块1 扩展） | 可显示、可渲染、可投影的单元 |
| 渲染 | `Renderer`（`SimpleRenderer` / `UniqueValueRenderer` / `ClassBreaksRenderer`）：`GetSymbolFor(feature)`（模块3） | 依要素属性决定该要素用什么**符号**画 |
| 注记 | `LabelRenderer` + `TextSymbol`：`DrawLayerLabels`（模块3） | 依要素属性决定**注记文本**及样式 |
| 符号 | `Symbol` → `SimpleMarkerSymbol` / `SimpleLineSymbol` / `SimpleFillSymbol`（模块1 定义 + 模块3 扩展） | 毫米刻度的样式描述（与设备无关） |
| 绘制 | `BasicGeometryDrawer : ILayerRenderer, ILayerLabelRenderer`（模块3） | 符号 + 几何 + `MapTransform` → GDI+ 图元 → 屏幕像素 |
| 视图 | `MapControl` + `MapTransform`（模块2/1） | 图层顺序、可见性、缩放平移、选择与高亮、重绘 |

一帧的绘制顺序（`MapControl.OnPaint`）：

```
背景 → 逐图层 DrawLayer（可见图层，逐要素：GetSymbolFor → DrawGeometry）
     → 逐图层 DrawLayerLabels（注记统一在最上层）
     → 选择高亮 → OverlayPaint（编辑草图）→ 交互拖框
```

符号的取用优先级（`BasicGeometryDrawer.GetSymbol`）：

```
layer.Renderer.GetSymbolFor(feature)    // 模块3 的渲染器（首选）
  → layer.Symbol                        // 模块2 的图层默认符号（Renderer 为空或无匹配时）
  → BasicGeometryDrawer.DefaultSymbol(geometry)   // 兜底默认样式
```

约定：**渲染器不持有 `Graphics`**（由地图控件拥有，渲染器不得 `Dispose`）；每个图层绘制前后保存/恢复 `Graphics` 状态；符号尺寸单位为**毫米**，按 `Graphics.DpiX` 换算像素；`Symbol.Visible=false` 的要素**不绘制到地图**（用于“绑定属性错误”）。

### 3.2 三种渲染器：各自包含哪些符号、能设置哪些属性

| 渲染器 | 符号构成 | 可设置的属性 |
|---|---|---|
| `SimpleRenderer`（简单渲染） | **1 个符号**（点/线/面之一），图层内所有要素共用 | `Symbol`；另可设 `Symbol.Label`（图例名）、`Symbol.Visible` |
| `UniqueValueRenderer`（唯一值渲染） | **N 个符号** = 每个唯一值一个 + 1 个默认符号 | `Field`（绑定字段）、`HeadTitle`（图例标题）、`ShowHead`、`DefaultSymbol`、`ShowDefaultSymbol`；数据接口 `AddValue(value, symbol)`/`RemoveValueAt`/`ClearValues`/`GetValue`/`SetValue`/`GetSymbol`/`SetSymbol`/`FindSymbol`/`ValueCount` |
| `ClassBreaksRenderer`（分级渲染） | **N 个符号** = 每个分级区间一个 + 1 个默认符号 | `Field`、`HeadTitle`、`ShowHead`、`DefaultSymbol`、`ShowDefaultSymbol`；断点接口 `AddBreakValue(value, symbol)`/`ClearBreakValues`/`GetBreakValue`/`SetBreakValue`/`GetSymbol`/`SetSymbol`/`FindSymbol`/`BreakCount`；配色与尺寸渐变 `ApplyColorRamp(ramp)`、`RampColor(start, end)`、`RampSize(start, end)` |
| `LabelRenderer`（注记，可独立于渲染器开关） | 1 个 `TextSymbol` | `LabelFeatures`（总开关）、`Field`（注记字段）、`RotateAngle`（旋转角）、`TextSymbol`（字体与描边，见 3.3） |

三种渲染器共同的基类能力（`Renderer`）：`RendererType`、`GetSymbolFor(feature)`、`Clone()`，以及**绑定属性错误**机制：

- `RendererFile.Load` 读入符号文件时，若绑定的字段在目标图层属性表中**不存在**，调用 `SetBindingError(field)`：所有符号被替换为专用的**红色感叹号符号**（`Style=Exclamation`、`Visible=false`，因此**不绘制到地图**），在图层面板中该行显示 **“绑定属性错误”**、**图层名变红**；在“渲染设置”里重新绑定字段并生成后调用 `ClearBindingError()` 恢复。此时渲染器可能**没有默认符号**（旧版文件不保存默认符号），渲染设置窗口会自动补一个基于图层当前符号的默认符号，因此默认符号始终可预览、可双击设置，在错误状态下重新绑定字段并点“生成 / 加载所有值”即可恢复，**不会再出现重新设置符号报 `NullReferenceException` 的情况**。
- 唯一值/分级渲染的**图例**在图层面板中逐项展开（符号预览 + 值/区间标签）；简单渲染只显示一个符号。

### 3.3 符号可设置属性一览

**点符号 `SimpleMarkerSymbol`**

| 属性 | 单位/取值 | 默认 | 说明 |
|---|---|---|---|
| `Style` | `Circle` / `Square` / `Triangle` / `Cross`（+ `Exclamation` 仅用于错误提示） | `Circle` | 形状 |
| `Color` | RGBA | 随机浅色 | **填充色**；`Transparent` 即空心符号 |
| `OutlineColor` | RGBA | `Transparent` | 边框颜色；`Transparent` 即无边框（实心） |
| `OutlineWidth` | 毫米 | 0.3 | 边框宽度 |
| `Size` | 毫米 | 3 | 符号尺寸 |
| `Label` / `Visible` | 文本 / 布尔 | 空 / true | 图例名、是否绘制 |

“实心圆/空心圆”统一为**填充色 + 边框**两个属性：填充不透明=实心，填充透明=空心，两者都设=带边框实心。

**线符号 `SimpleLineSymbol`**

| 属性 | 单位/取值 | 默认 | 说明 |
|---|---|---|---|
| `Style` | `Solid` / `Dash` / `Dot` | `Solid` | 简单线型（`DashElements` 非空时被自定义虚线取代） |
| `Color` | RGBA | 随机浅色 | 线颜色 |
| `Size` | 毫米 | 0.35 | 线宽 |
| `DashElements` | `List<LineDashElement>` | 空 | **自定义虚线**的线段图案，非空即启用（见下表） |
| `Offsets` | `List<LineOffset>` | 空 | **多条偏移线**：每条 = 偏移量（毫米，正=线左）+ 一条独立线符号；非空时按各条偏移线绘制 |

自定义虚线的一段 `LineDashElement`：

| 属性组 | 属性 | 单位/取值 | 说明 |
|---|---|---|---|
| 基本 | `Length` | 毫米 | 本段长度 |
| 基本 | `Color` / `Width` | RGBA / 毫米 | 本段颜色与线宽 |
| 基本 | `OutlineColor` / `OutlineWidth` | RGBA / 毫米 | 边框（casing）颜色与宽度 |
| 基本 | `TickLength` / `TickColor` | 毫米 / RGBA | 端点**垂直短划线**长度与颜色（0=不画，可拼铁路线） |
| 可选①偏移 | `OffsetEnabled` + `Offset` | 毫米（正=左、负=右） | 本段整体沿法线平移，不影响其他段 |
| 可选②延长 | `ExtendEnabled` + `ExtendLeft`/`ExtendRight` | 毫米 | 本段两端各自向外延长 |
| 可选③弧线 | `ArcEnabled` + `ArcAmplitude`/`ArcHalfPeriods` | 毫米 / 个数 | 用半椭圆画成波浪；振幅=垂向半轴，半周期数=本段等分个数，隔段交替方向 |

三段图案按顺序**循环排列**构成整条线；每段的三类特性**可单独启用**，绘制管线固定为 **延长 → 弧线 → 偏移**，互不干扰。典型用法：`长5.0 + 端点竖线` 拼铁路；`弧线` 拼波浪边界；`多条偏移线` 拼国界线。

**面符号 `SimpleFillSymbol`**

| 属性 | 单位/取值 | 默认 | 说明 |
|---|---|---|---|
| `Color` | RGBA | 随机深色 | 填充色（`Transparent` 即只画边界） |
| `Outlines` | `List<FillOutline>` | 1 条（深灰、偏移 0） | **多条边界**：每条 = 偏移量（毫米）+ 一条独立线符号 |

边界偏移方向规则：**外环向外偏移 `+offsetPx`、内环（洞）向内偏移 `-offsetPx`**，因此“正偏移量”统一表示**面整体扩张**（外环外扩、洞收缩）；多条边界叠加可做渐变/多环边界。为兼容旧代码保留的 `Outline` 属性等价于第一条边界。

**注记符号 `TextSymbol`**（配合 `LabelRenderer`）

| 属性 | 单位/取值 | 默认 | 说明 |
|---|---|---|---|
| `FontName` / `FontSize` | 字体名 / 磅 | 微软雅黑 / — | 字体与字号 |
| `Bold` / `Italic` | 布尔 | false | 粗体/斜体 |
| `FontColor` | RGBA | — | 文字颜色 |
| `FontRatio` | 倍率 | 1 | 字宽比例（拉伸/压扁） |
| `UseMask` / `MaskColor` / `MaskWidth` | 布尔 / RGBA / 毫米 | false | **文字描边（晕圈）**开关、颜色与宽度，用于压在深色底图上仍可读 |

注记定位：点取点位；线取折线中点；面取外环形心，并按符号尺寸让注记落在符号外侧。

### 3.4 绘制实现要点（`BasicGeometryDrawer`）

- **渲染器驱动**：逐要素调用 `Renderer.GetSymbolFor`，按几何类型分派到点（`FillEllipse`/多边形/自绘十字等）、线（`DrawLines`/`DrawPath`）、面（`FillPath` + 逐条边界 `DrawPath`），复合几何逐部件绘制。
- **自定义虚线**：把每段按屏幕长度循环铺设，段内先做“延长”，再用 `BuildArcPolyline` 采样出波浪折线，最后沿法线做“偏移”，边框用“先粗后细”的两遍描边实现（casing）。
- **面多边界**：按多边形每条环的顶点法线计算偏移后的新环（外环外扩、洞内缩），再分别用各自的线符号绘制。
- **注记**：几何绘制完成后统一在最上层绘制；支持旋转、字宽比例与 `UseMask` 描边。
- **绘制裁剪**：要素外包矩形与视图范围（按符号尺寸外扩）不相交时跳过，避免大图层无谓的路径构建。
- **健壮性**（应对极端视图）：`MapTransform` 给出比例尺上下限 `MinMapScale=1` / `MaxMapScale=5亿`，`Zoom`/`ZoomToExtent` 等统一经 `GetValidMapScale()` 钳制；屏幕坐标换算统一经 `ToScreenValue()` 限幅（±1e6），避免放大到 1:10 附近时 GDI+ 溢出异常；`MapControl.SetExtent/FullExtent` 使用按范围中心量级取的相对下限，避免经纬度数据被放大到 10°。
- **符号预览**：`BasicGeometryDrawer.DrawSymbol(g, symbol, rect)` 与地图用**同一套绘制代码**，因此编辑器里的预览（含偏移线编辑器、注记编辑器）与地图显示完全一致。

### 3.5 图层面板与设置对话框

图层面板 `LayerManagerControl` / `LayerControl`：**一个图层一行**（名称自动截断 + 可见性勾选框 + 上下移按钮 + 符号/图例预览），支持多选与批量操作，右键菜单：修改渲染 / 显示·设置注记 / 缩放至图层 / 可选切换 / 全选·选择可见·仅设可选 / 重命名 / **投影到 WGS84** / 移除图层 / 打开属性表。唯一值、分级渲染时行内**展开图例**；发生绑定属性错误时该行显示错误符号与“绑定属性错误”并**将图层名标红**。

| 对话框（`GIS.Demo/GIS.Display/UI/`） | 设置对象 | 主要内容 |
|---|---|---|
| `MarkerSymbolEditor` | `SimpleMarkerSymbol` | 形状、填充色、边框色/宽、尺寸 + 预览 |
| `LineSymbolEditor` | `SimpleLineSymbol` | 线型、颜色、线宽；**自定义虚线**列表（增加/删除/编辑/上移/下移）+ **偏移线**列表（增加/删除/编辑/上移/下移）+ 预览 |
| `DashElementEditor`（“设置线段”） | 一段 `LineDashElement` | 长/色/宽/边框/端点竖线，以及**偏移、延长、弧线**三组可勾选特性（未勾选时参数置灰） |
| `LineOffsetEditor`（“设置偏移线”） | 一条 `LineOffset` | 偏移量 + 该条线的线符号（再进 `LineSymbolEditor`）+ 与地图一致的预览 |
| `FillSymbolEditor` | `SimpleFillSymbol` | 填充色 + 多条边界（偏移量、线符号）+ 预览 |
| `LayerRendererForm` | `Renderer` | 简单/唯一值/分级三个选项卡；唯一值：绑定字段、取值列表与逐值符号、默认符号；分级：绑定字段、分级数/断点、色带、尺寸渐变；底部为**保存/读取渲染符号** |
| `LabelRendererForm` | `LabelRenderer` + `TextSymbol` | 注记开关、注记字段、字体/字号/粗斜体/颜色/字宽比例、描边（晕圈）、旋转角 |
| `ColorRampForm`（`ColorRampEditor`） | `ColorRamp` | 书签式色带节点（可拖动）+ 0~100 坐标轴；新增/删除/均匀分布节点、设置节点位置/颜色、设置与上一节点之间的**插值方式**；应用/确定/取消/退出 + 保存·读取·载入内置 |
| `ColorPickerForm` | `Color` | 调色板 + `#RRGGBB` / `#RRGGBBAA` 文本输入（含透明度） |
| `ZonePickerForm` | 分带选择 | 球体上选经纬度带、经/纬度滑块、3 度带/6 度带切换、按数据范围选择（见 3.8） |
| `ProjectToWgs84Form` | `GeographicDatum` | 选择图层当前地理坐标系、显示/填写七参数、预估转换量级（见 3.7） |
| `AttributeTableForm` | `FeatureClass` | 属性表浏览（只读），选中行抛 `FeatureSelected` 事件，供选择联动 |

**编辑约定**：所有编辑器都在**克隆**上编辑，只有“确定”才写回原对象，**“取消”完全还原**；颜色按钮以所选颜色为底色并按明暗自动切换文字颜色。

### 3.6 渲染符号文件与色带文件

| 文件 | 扩展名 | 内容 |
|---|---|---|
| 渲染符号 | `.gps`（点）/ `.gls`（线）/ `.gfs`（面） | 文件头（几何类型 + 渲染类型）→ 渲染器专有信息（唯一值：绑定字段 + 值个数；分级：绑定字段 + 分级数）→ 每个符号的属性；唯一值/分级渲染若有默认符号，最后再写一个 `[默认符号]` 块。点：形状/大小/填充色/边框色/边框宽；线：首行标注“简单符号”或“自定义虚线”，自定义虚线按“元素个数 + 每元素 7 个基本属性 + 偏移/延长/弧线各 3 个字段”写出，并附多条偏移线；面：填充色 + 各条边界的偏移量与线符号（线符号递归保存） |
| 色带 | `.gcr` | 文件头（格式标识、名称、节点数）+ 每个节点的位置、A、R、G、B、与上一节点间的插值方式 |

- 保存/读取 API：`RendererFile.Save(path, renderer, geometryType)` / `Load(path, featureClass)`，另有 `ExtensionFor`/`FilterFor`/`IsCompatible`（按几何类型过滤对话框）与纯文本 `ToLines`/`Parse`（便于存库或走网络传输）。
- **兼容性**：线符号的虚线元素行从 7 个字段扩展到 15 个、并在末尾增加了 `[默认符号]` 块，**旧格式文件仍可正常读取**（缺省的虚线元素字段视为“未启用”，没有默认符号块的按“无默认符号”处理）。
- **默认符号也会存盘**：唯一值/分级渲染的默认符号（未匹配值使用的符号）随文件一起保存，读回后完全一致；旧版文件没有这一段时，渲染设置窗口会自动补一个基于图层当前符号的默认符号，因此**默认符号永远可预览、可双击设置**（此前读回后默认符号为 `null`，界面上既看不到也设不了）。
- **校验**：`ValidateBinding(renderer, featureClass)` 在读取后检查绑定字段是否存在，不存在则进入“绑定属性错误”状态（见 3.2）；处于错误状态且还没有任何分级/唯一值符号时，`GetSymbolFor` 返回**不可见**的错误符号而不是 `null`，避免要素被回落到图层基础符号而误画出来。
- 色带内置 9 条（红-白-蓝、红-黄-绿、蓝-青-绿-黄-红、浅黄-橙-红、浅蓝-深蓝、绿-黄-红、白-灰-黑、紫-白-绿、棕-黄-绿），分级渲染默认按当前色带**等间隔取色**；色带下拉框每一项都用该色带自身颜色画出渐变条，最后一项为“自定义色带…”。

### 3.7 坐标系统一到 WGS84（图层右键“投影到 WGS84”）

- **所有投影统一使用 WGS84 椭球**：高斯-克吕格、UTM、Lambert 都用 `Ellipsoids.WGS84`（此前高斯-克吕格用 CGCS2000），地图内不再“不同球体混用”；系统默认**只接受 WGS84 数据**。
- `GeographicDatum` + `DatumTransform`：用 **Bursa-Wolf 七参数**（3 个地心平移 / 3 个轴旋转 / 1 个尺度比，位置矢量约定）描述本基准与 WGS84 的关系，转换过程是“大地坐标 →（本基准椭球）空间直角坐标 → 七参数变换 →（WGS84 椭球）大地坐标”，反方向用迭代求逆；`EstimateShiftMeters` 给出该点的转换量级（界面用来提示）。
- **只保留有明确转换方式的坐标系**（三项，全部自动填好参数，`ParameterSource` 记录出处）：
  - **WGS84**：系统基准，恒等；
  - **北京54**：**EPSG:15920 “Beijing 1954 to WGS 84 (3)”**（中国陆域，精度 15 m）：ΔX=31.4、ΔY=−144.3、ΔZ=−74.8、Z 旋转=0.814″、尺度=−0.38 ppm，来源 [epsg.io/2414-15920](https://epsg.io/2414-15920)；
  - **GCJ-02（国测局坐标系/火星坐标）**：不是换椭球，而是按公开的国测局算法做**非线性加密偏移**（境内约 300~700 m，境外不偏移），因此**不使用七参数**（界面把参数区置灰），换算走 `Gcj02Transform`（正算闭式、反算迭代，闭合到 1e-9°）。
- **没有具体转换方式的坐标系已删除**（按“若某一坐标系无具体转换方式则从系统中删除”的要求）：**西安80**（EPSG 未发布 “Xian 1980 to WGS 84” 转换，检索 0 条；OSGeo《China datums》等资料说明该参数随地区变化、不存在全国通用值，见 [OSGeo wiki](https://wiki.osgeo.org/w/index.php?title=China_datums&oldid=130239)。此前曾借用北京54 参数做近似，会让两个坐标系显示**完全相同的参数**，容易误导，故删除）；**CGCS2000**（与 WGS84 同属 ITRF 地心框架、椭球差异毫米级，EPSG 也未发布二者之间的转换参数，不存在“具体转换方式”，其数据可直接按 WGS84 使用）。说明：`Ellipsoids.Xian80 / CGCS2000` **椭球常数仍保留**（高斯-克吕格等按椭球计算时可用），删除的只是“地理坐标系（基准）”条目。
- 图层右键 **“投影到 WGS84”**：若图层已标记了具体坐标系（图层面板中该图层名后以**橙色**标注，如“分区[北京54]”），直接按内置公开参数**自动转换**并弹出结果说明（要素数、换椭球参数或国测局算法及其出处）；若图层仍标记为 WGS84，则弹出**参数已自动填好**的对话框让用户指明数据实际所属坐标系再转换。
- **经纬度是图层的固有属性，投影只从经纬度出发**（重构要点）：`Layer` 保存一份 **WGS84 经纬度快照**（`CaptureGeographic` / `GeographicGeometries` / `GetGeographicEnvelope`），`ApplyProjection(projection)` **每次从快照整体重新投影**得到显示几何，不再做“投影 → 投影”的换算。因此反复切换任意投影（包括与数据不匹配的南半球 UTM 带）后切回经纬度，**原始经纬度可原样恢复**；显示坐标永远是“经纬度 → 当前投影”的正算结果。基准转换同样只作用在快照上，转换后按当前投影重新生成显示几何。
- 其他相关修正：投影反算把子午线弧长与经差限制在有效范围内（纬度 ≤89.5°、|D| ≤0.3），避免不匹配的南半球带出现 −29929639° 这样的荒谬纬度；切换投影后统一自动取景到所有可见图层（`ZoomToAllLayers`）并立即重绘，不会再出现“切完跑到视野外/要拖动才刷新”；右下角经纬度显示 N/S、E/W，超出该投影有效范围时给出文字提示。

### 3.8 分带可视化选择（高斯-克吕格 / UTM）

- `UI/ZonePickerForm.cs` 的 `ZoneGlobe` 用正交投影把地球画成球体，球面上只绘制经纬网（每 15° 一条，赤道与本初子午线略粗），并叠加**当前分带**与**所有要素合一的外包矩形**（绿色方框）；单击球面即按经纬度反算选中该处所属分带，也可按住左键拖动旋转地球；对话框下方有**经度、纬度滑块**，与球面双向同步，选择带号/切换纬度行/按数据范围选择后地球自动移到对应区域。可见半球之外的带子沿**地平圈补弧**闭合，因此高亮始终贴合球面，不会出现“饼状”扇形。
- 高斯-克吕格界面可切换 **3 度带 / 6 度带**（中央经线 = 3°×带号 / 6°×带号 − 3°），**支持西半球负带号**（3 度带 −60~60、6 度带 −29~30）。
- UTM 按定义完整实现：**经度带**自 180° 起每 6° 一带（1~60 带，中央经线 = 6°×带号 − 183°，带心横坐标 500000 m）；**纬度行**自 80°S 起每 8° 一行，用字母 **C~X（不含 I、O）** 共 20 行，末行 **X 覆盖 72°N~84°N（12°）**；北半球赤道纵坐标 0、南半球 10000000 m。带号 + 行字母 = 一个四边形（例如北京为 **50S**），南北半球由所选行自动决定；纬度行数学在 `GIS/Projection/UtmGridZone.cs`。选择与数据所在纬度范围不相交的行时会提示并支持一键改为数据所在行。
- 工具条“投影”下拉框选中高斯-克吕格或 UTM 时右侧出现**“选择分带”**按钮，结果写回下拉框文字（如“高斯-克吕格 3度带 第39带”“UTM 50S”）并立即重新投影已有图层；下拉框、状态栏与右下角坐标三处的投影名称完全一致（`ProjUTM` 的名称改用纬度行字母，不再用容易与行字母混淆的半球字母）。
- 性能：经纬网与分带边界均为预计算折线，绘制时只做旋转投影，球体单次重绘约 3 ms；切换投影 + 强制重绘地图单次约 2.5~13 ms（自动检查中有 120 ms 上限断言）。

### 3.9 给模块4（数据编辑与导入导出，李君迟）的接口说明

模块3 已经把“数据 → 显示”这一段的开关都准备好了，模块4 只需在**数据侧**操作，然后通知刷新即可。可用的落点：

| 需求 | 用这些 API |
|---|---|
| 导入 shp/其他文件后建表并入库 | `new FeatureClass(name, geometryType)` → `fc.Fields.Add(new Field(名称, FieldTypeConstant.Text))` → `fc.Add(new Feature(geometry, fc.Fields))`（`Add` 会**自动校验几何类型**并写属性）；`Feature.Attributes` 按字段名/下标取值 |
| 中间格式 | `Geometry.FromWKT(string)` / `geometry.ToWKT()`（`GeometryWkt` 支持 7 种几何），可直接用于文件或剪贴板交换 |
| 把数据加入地图 | `var layer = new Layer("名称", fc); map.AddLayer(layer); map.FullExtent();`（`MapControl` 自动重绘并触发 `LayersChanged`） |
| 编辑要素后刷新 | 直接改 `Feature.Geometry`/`Attributes` 后调用 `map.RefreshMap()`；**改了几何结构（增删顶点、换坐标系）后请调用 `layer.CaptureGeographic(projection)` 重建经纬度快照**，否则再次切换投影会以旧快照为准 |
| 新建/删除要素后刷新图层面板与图例 | 删除后调用 `map.RefreshMap()`（`MapControl` 会自动清理失效选择）；需要重建行控件时用 `LayerManagerControl.Rebuild()`，或订阅 `map.LayersChanged` |
| 选择集联动 | `map.GetSelection(layer)`（返回 `Features` 快照，元素仍指向原始 `Feature` 对象，可直接改）→ 编辑后用 `map.SetSelection(layer, features, SelectMethodConstant.CreateNew)` 或 `map.ClearSelection()`；点选/框选走 `map.SelectAt/SelectBox`，键盘/工具触发可用 `FeatureClass.SearchByPoint/SearchByBox` + `SelectTools.ExcuteSelect` |
| 编辑草图/顶点提示 | 订阅 `map.OverlayPaint`，在 `e.Graphics` 上用 `e.Transform.MapToScreen(...)` 画临时图形（该事件在选择高亮之后、拖框之前触发；**不要 Dispose 传入的 Graphics**） |
| 属性表窗口 | `new AttributeTableForm(layer).Show()`，窗体是只读浏览，选中行会抛 `FeatureSelected` 事件，可与地图选择联动 |
| 视图与交互 | `map.SetExtent/GetExtent/FullExtent/Zoom/Pan`、`map.InteractionMode`（`Pan`/`ZoomIn`/`ZoomOut`/`Select`）、`map.SelectionMethod` |
| 沿用渲染能力 | 新图层默认走 `Layer.Symbol`（模块2 默认样式）；要设专题渲染：`layer.Renderer = new SimpleRenderer { Symbol = ... }`，或用 `RendererFile.Load(path, fc)` 读取现成的 `.gps/.gls/.gfs`（字段不匹配会返回带“绑定属性错误”的渲染器，可用 `HasBindingError` 判断），再 `map.RefreshMap()` |
| 坐标系转换 | 非 WGS84 数据先转换：`DatumTransform.ToWgs84(new Coordinate(lng, lat), GeographicDatum.Beijing54)`；把整套数据统一到 WGS84 可直接抄 `LayerControl.ConvertLayerToWgs84` 的做法（对 `layer.GeographicGeometries` 转换后 `layer.GeographicDatum = GeographicDatum.Wgs84; layer.ApplyProjection(projection)`） |

约定与注意：

1. **不要直接替换 `Layer.Symbol` 来改样式**——优先设置 `Layer.Renderer`；`Layer.Symbol` 只在 `Renderer` 为空时作为回落样式。
2. 图层顺序：`map.Layers[0]` 在**最底部**，最后一个在最顶部；图层面板反向列出。增删/移动请走 `map.AddLayer/RemoveLayer/MoveLayer`，可见性请走 `map.SetLayerVisible`，以便面板同步。
3. 所有控件操作在 UI 线程执行；导入/查询可放后台，但 `AddLayer`/`RefreshMap` 请用 `Invoke/BeginInvoke` 回到 UI 线程。
4. 批量导入大量要素时建议先把 `Layer.Visible` 置 false、导入后再打开并 `map.FullExtent()`，避免逐条触发重绘。

### 3.10 给模块5（与 PostGIS 交互，李雨晴）的接口说明

模块3 提供的都是**纯内存 + 纯文本**的落点，不需要改动渲染代码即可与数据库打通：

| 需求 | 用这些 API |
|---|---|
| 表结构 → 建表语句 | `FeatureClass.Fields`（`Field.Name` + `FieldTypeConstant`）逐个映射为列定义；`FeatureClass.Name` 作表名 |
| 读取要素 | 每行构造 `Geometry`（`Geometry.FromWKT(reader.GetString(geomCol))` 或按坐标数组手工构造 `Point`/`LineString`/`Polygon`）→ `new Feature(geometry, fc.Fields)` → `fc.Add(feature)`（自动按字段顺序读属性） |
| 写出几何 | `feature.Geometry.ToWKT()` 直接用于 `ST_GeomFromText(:wkt, :srid)`；查询时用 `ST_AsText(geom)` 取回 |
| 空间查询下推（可选） | 地图当前范围 = `map.GetExtent()`；经纬度范围 = `MainForm.DataExtentLngLat()` 或 `layer.GetGeographicEnvelope()`，可转成 `ST_MakeEnvelope(...)` 做粗过滤，再用 `FeatureClass.SearchByBox` 精过滤 |
| SRID / 坐标系 | 系统内统一 WGS84，对应 **SRID 4326**；入库前若非 4326 数据，用 `DatumTransform.ToWgs84` 或 `CoordinateTransform.Transform(coord, from, to)` 统一；`layer.GeographicDatum` 记录数据所属基准，`layer.FeatureClass.ProjectionCS` 记录投影坐标系 |
| 图层入库顺序 | 从底部到顶部依次写出（`map.Layers` 顺序即绘制顺序），读回时按同样顺序 `AddLayer` |
| 渲染样式随图层入库（可选扩展） | `RendererFile.ToLines(renderer, geometryType)` 得到**纯文本行数组**，可直接存进 `text` 列；读回时 `RendererFile.Parse(lines, extension, featureClass)` 还原渲染器，再赋给 `layer.Renderer`。色带同理用 `ColorRamp.ToLines()`/`ColorRamp.Parse(lines)`（`.gcr` 的内容就是这些行） |
| 注记样式入库（可选） | `LabelRenderer` 的字段都是简单类型，可拆列存储（`Field`/`RotateAngle` + `TextSymbol` 的字体、字号、颜色、描边等） |
| 读回后显示 | `map.AddLayer(layer); map.RefreshMap(); map.FullExtent();`；图层面板用 `LayerManagerControl.Rebuild()` 刷新（面板已绑定 `map.LayersChanged` 时会自动刷新） |

约定与注意：

1. 连接、查询、异常处理都在模块5 内完成；模块3 只接收构造好的 `FeatureClass`/`Layer`，**不负责数据库连接**。
2. 属性值类型请落到 `FieldTypeConstant` 支持的集合（Text / Integer / Double / Date 等），`Attributes` 与 `Fields` **按位置对应**，`fc.Add` 时属性个数要与字段数一致，否则会抛异常。
3. 多边形入库请保持“外环 + 内环”的顺序（`Polygon.ExteriorRing`、`Polygon.Holes`），WKT 输出即 `POLYGON((外环),(内环)...)`，可直接被 PostGIS 接受。
4. 从数据库读回的坐标若是经纬度，`Layer.GeographicDatum` 默认 WGS84 即可，图层可直接参与任何投影；若数据来自其他基准，先按 3.9 的方式转换再入库/显示。

### 3.11 验证与演示

```powershell
# 模块3 的自动检查（135 项，含窗口截图输出）
.\GIS_Demo\GIS.Display.Checks\bin\Debug\net48\GIS.Display.Checks.exe .\GIS_Demo\GIS.Display.Checks\obj\validation
```

- 验证结果：`dotnet build GIS.sln -m:1` 为 **0 警告 0 错误**；`GISDemo` 自测 **194 项**、`GIS.Display.Checks` 自动检查 **135 项**，全部通过。
- 自动检查程序覆盖：三种渲染器的符号分配与图例、绑定属性错误与恢复（含“分段渲染的符号为 error 且没有默认符号时，重新生成不再报错、默认符号自动补上”的回归检查）、色带取样与文件往返、默认符号随渲染符号文件往返、点/线/面符号各项属性对绘制结果的影响（含像素级断言，例如“面正偏移时洞收缩 101→133 像素”“自定义虚线的偏移/延长/弧线确实改变绘制”）、渲染符号文件往返与**旧格式兼容**、注记开关与描边、图层面板橙色坐标系标注与错误标红、坐标系统一与经纬度快照的“反复切换投影后可原样恢复”、西半球负带号与球面高亮补弧、分带选择与数据范围匹配、“设置线段”/“线符号设置”窗口控件不超出可视宽度、颜色按钮文字色与底色明暗一致等，并把 `module2-preview.png`、`segment-editor.png`、`line-symbol-editor.png` 等截图写入输出目录便于人工核对。
- 演示：运行 `GIS.Display.Demo`（启动即有 5 个样例图层、9 个要素，字段为 名称/类型/数值）。在左侧图层面板**单击符号**或**右键图层**即可进入渲染/符号/注记设置；工具条“投影”切到高斯-克吕格 / UTM 后点“选择分带”打开球体分带选择；非 WGS84 数据在该图层上右键选“投影到 WGS84”完成转换。

---

## 协作约定

- 模块1 的类库（`GIS/`）是全组共用的数据结构基础，核心接口（`Geometry`/`Feature`/`FeatureClass`/`Layer`/`Envelope`）已在分工文档中约定；各模块改动接口前请先在小组内同步。
- 模块2 通过 `ILayerRenderer` 预留了渲染扩展点；模块3（图层渲染与编辑）在 `map.Renderer` 上实现，模块4/5 通过 `AddLayer`/`GetSelection`/`RefreshMap` 接入，无需改动模块1/2 的接口。
- `bin/`、`obj/`、`.vs/`、`*.user` 等编译产物不入库（已配置 .gitignore）。
- 各模块在自己的文件夹内开发，跨模块改动先与对应成员沟通；统一在根目录 `GIS.sln` 中集成与验证。
