# 模块1：数据结构设计与地图投影实现（王小宇）

> **说明**：本模块已并入根目录统一解决方案 [`GIS.sln`](../GIS.sln)。整体分工、目录结构与构建/运行命令以 [根目录 README](../README.md) 为准；下文为本模块的详细设计说明。

GIS 小组作业（0915汇报.pptx）模块 1 的实现：参考 OGC 简单要素标准设计空间要素的数据结构与内置函数，实现投影方法，完成经纬度坐标与地图坐标间的相互转换。

本模块是全组开发的基础。按分工 PPT 的约定，`Geometry`、`Feature`、`FeatureClass`、`Layer`、`Envelope` 这几个类就是各模块间提前约定的核心接口，其余四个模块可直接引用本类库并行开发。

## 工程结构

```
MapObjects/
├── GIS.sln                 解决方案（VS2022 直接打开）
├── GIS/                    类库（netstandard2.0，可被 .NET Framework 4.6.1+ 与 .NET 6/8 引用）
│   ├── Geometry/           坐标、范围、几何对象与几何算法
│   ├── Feature/            字段、属性、要素、要素类（含点选/框选/表达式查询）
│   ├── Layer/              图层
│   ├── Symbol/             符号体系（渲染模块在此基础上扩展）
│   ├── Projection/         投影坐标系统与坐标转换
│   └── Mapping/            地图窗口视图变换（屏幕坐标↔地图坐标）
├── GISDemo/                控制台自测程序（113 项检查，全部通过）
└── README.md
```

编译与运行：

```
dotnet build GIS.sln
dotnet run --project GISDemo
```

或用 Visual Studio 2022 打开 `GIS.sln`。类库目标框架为 `netstandard2.0`，WinForms 主程序无论用 .NET Framework 4.8 还是 .NET 6/8 都可直接项目引用。

## 类型清单

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

## 内置函数对照（分工 PPT 模块1 页约定的接口）

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

## 与旧项目（gis/gisde 的 mo 类库）对照

实现思路与代码风格沿用旧项目，类型名按新分工 PPT 的 OGC 风格命名：

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

## 给各组员的对接说明

- **白宇丹（模块2 图形显示与图层管理）**：新建 `MapControl` 时内嵌一个 `MapTransform`，鼠标事件里用 `ScreenToMap/MapToScreen` 做坐标换算；缩放平移直接调 `ZoomByCenter/ZoomToExtent/PanDelta`；图层列表用 `Layer.Visible` 控制显示；点选/框选调 `FeatureClass.SearchByPoint/SearchByBox`，选择集用 `SelectTools.ExcuteSelect` 按四种方式更新。参考旧项目 `IMapControl` 接口（AddLayer/RemoveLayer/Zoom/Pan/SetExtent/GetExtent/RefreshMap）。
- **赵佶昊（模块3 图层渲染）**：继承 `Symbol` 增加符号类型；`Geometry.GetEnvelope()` 做要素级粗判，`Polygon.Area`、`LineString.Length` 可用于分级渲染分段；注记定位点可用 `LineString.GetMidPoint()` 或旧项目 moMapTools 里的扫描线法。
- **李君迟（模块4 数据编辑与导入导出）**：shp 导入后按 `Field/Fields` 建表结构、按 `FeatureClass.Add` 入库（自动校验几何类型）；WKT 可作为中间格式（`Geometry.FromWKT/ToWKT`）；编辑用 `Feature.Clone()` 保留原要素、`Geometry.Translate` 平移、`Points.Insert/RemoveAt` 移动顶点。
- **李雨晴（模块5 PostGIS 交互）**：`FeatureClass.ProjectionCS` 记录图层坐标系，入库前可用 `CoordinateTransform.Transform` 统一投影；WKT 与 PostGIS 的 `ST_AsText/ST_GeomFromText` 直接对应；属性表结构由 `Fields` 描述，可映射为 `CREATE TABLE` 列定义。

## 注意事项

1. 本模块中的 `Point` 是几何点类；WinForms 代码里的屏幕点 `System.Drawing.Point` 与其重名，在同时用到两者的文件里请写全名或加 `using` 别名。
2. `Coordinate` 中经纬度的存放约定：X=经度、Y=纬度（单位度）；投影坐标 X=东方向（y，含东伪偏移）、Y=北方向（x）。
3. 投影正算输出恒为米；`LinearUnit` 属性仅描述坐标系统单位，配合 `ToUnits/ToMeters` 换算。
4. 投影公式采用 USGS/Snyder 横轴墨卡托与 Lambert 2SP 标准级数式，经测试（北京/上海/广州/成都/乌鲁木齐/哈尔滨/拉萨/武汉 8 点、4 种投影）正反算闭合误差均小于 1 毫米；跨带使用时请按点所在带号构造投影（`GetZone3/GetZone6/GetZone` 辅助函数已提供）。
5. 折线/多边形与 `Envelope` 的 `Intersects` 为快速相交判断（顶点入盒、线段穿盒、盒角入面），与旧项目 moMapTools 语义一致，可满足框选与显示裁剪需求；精确的 OGC 九交关系不在本模块范围。
