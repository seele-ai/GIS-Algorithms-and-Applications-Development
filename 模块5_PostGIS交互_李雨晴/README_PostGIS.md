# 模块5：PostGIS 交互（李雨晴）

本模块负责 `FeatureClass ⇄ PostgreSQL/PostGIS` 数据交互，不负责地图绘制、图层渲染、Shapefile 解析或桌面界面。

## 工程结构

```text
模块5_PostGIS交互_李雨晴/
├── GIS.PostGIS.sln
├── GIS.PostGIS/
│   ├── PostGISConnection.cs
│   ├── PostGISSchemaMapper.cs
│   ├── PostGISExporter.cs
│   ├── PostGISImporter.cs
│   ├── PostGISFeatureRecord.cs
│   └── PostGISService.cs
└── GIS.PostGIS.Demo/
    ├── Program.cs
    └── PostGISIntegrationChecks.cs
```

目标框架为 .NET Framework 4.8，引用模块1的 `GIS` 类库，数据库驱动为 `Npgsql 4.1.14`。

## 已实现功能

### 数据库连接

- 使用 Host、Port、Database、Username、Password 创建连接；
- 执行 `SELECT PostGIS_Version()` 测试 PostGIS；
- 为其他服务提供尚未打开的 `NpgsqlConnection`。

### Schema 映射

| FieldTypeConstant | PostgreSQL |
|---|---|
| Byte | SMALLINT |
| Int16 | SMALLINT |
| Int32 | INTEGER |
| Single | REAL |
| Double | DOUBLE PRECISION |
| Text | TEXT |
| Date | TIMESTAMP WITHOUT TIME ZONE |
| Boolean | BOOLEAN |

支持 Point、LineString、Polygon、MultiPoint、MultiLineString、MultiPolygon 六种 Geometry。

自动创建：

- `id BIGSERIAL PRIMARY KEY`；
- `geom geometry(类型, SRID)`；
- Geometry 列的 GiST 空间索引。

所有数据库标识符均使用双引号安全引用，属性值和 WKT 均通过参数传递。

### FeatureClass → PostGIS

`PostGISExporter` 支持：

- 创建空空间表；
- 保存完整 FeatureClass；
- 单要素追加；
- 显式选择是否覆盖同名表；
- 事务提交和失败回滚。

```csharp
PostGISConnection db = new PostGISConnection(
    "localhost", 5432, "postgres", "postgres", password);
PostGISExporter exporter = new PostGISExporter(db);

int count = exporter.SaveFeatureClass(
    featureClass,
    "roads",
    4326,
    overwriteExisting: true);
```

`overwriteExisting: true` 会删除并重建同名表，应只在调用方明确确认后使用。

### PostGIS → FeatureClass

`PostGISImporter` 支持：

- 获取空间表列表；
- 读取属性 Fields；
- 读取 Geometry 类型、Geometry 列名和 SRID；
- `ST_AsText` + `Geometry.FromWKT`；
- 构造 Attributes、Feature、FeatureClass；
- 使用 `PostGISFeatureRecord` 返回数据库 `id + Feature`。

```csharp
PostGISImporter importer = new PostGISImporter(db);
int srid;
FeatureClass featureClass = importer.LoadFeatureClass("roads", out srid);
```

### 空间表管理

`PostGISService` 提供：

```text
GetSpatialTables
TableExists
DeleteTable
ClearTable
GetFeatureCount
GetGeometryType
GetSrid
GetFields
```

### 数据库级要素同步

`PostGISService` 提供：

- `InsertFeature`：插入并返回数据库 id；
- `UpdateFeature`：根据 id 更新属性和 Geometry；
- `DeleteFeature`：根据 id 删除；
- `LoadFeatureRecords`：读取 id 与 Feature 的稳定对应。

共享的 `Feature` 类没有被修改，数据库主键只由模块5的 `PostGISFeatureRecord` 保存。

### 空间 SQL

`PostGISService` 提供：

- `QueryIntersects` → `ST_Intersects`；
- `QueryWithinDistance` → `ST_DWithin`；
- `QueryContainedBy` → `ST_Contains`；
- `GetDistance` → `ST_Distance`。

距离单位是空间表坐标系单位。SRID 4326 下为角度，不应把传入数值直接理解为米；按米查询应使用合适的投影坐标系或后续增加 geography 查询。

## 测试方法

运行 `GIS.PostGIS.Demo`：

1. 输入数据库连接参数；
2. 确认 PostGIS 连接成功；
3. 可选择 Point 简单闭环测试；
4. 可选择模块5完整数据库检查。

完整检查会覆盖：

- 六种 Geometry 双向转换；
- NULL 属性；
- Fields、Geometry 类型、SRID 和要素数量；
- INSERT、UPDATE、DELETE；
- 数据库 id 映射；
- ST_Intersects、ST_DWithin、ST_Contains、ST_Distance；
- ClearTable、DeleteTable。

检查会覆盖或创建以下专用测试表：

```text
public.postgis_test_point
public.postgis_test_linestring
public.postgis_test_polygon
public.postgis_test_multipoint
public.postgis_test_multilinestring
public.postgis_test_multipolygon
public.postgis_test_management（测试后删除）
```

不要把上述名称用于正式数据。

## 当前边界

- `ProjectionCS` 没有 EPSG/SRID 属性，因此保存时必须显式传入 SRID；读取时通过 `out int srid` 返回，暂不自动构造 ProjectionCS。
- 模块自动生成的 `id` 为 BIGSERIAL，不加入 `FeatureClass.Fields`，由 `PostGISFeatureRecord` 单独保存。
- 每张空间表目前支持一个 Geometry 字段。
- Geometry 不能为空。
- 导入普通外部表时，只支持本模块 FieldTypeConstant 能表达的数据库字段类型。
- `Byte` 和 `Int16` 在 PostgreSQL 中都保存为 SMALLINT，重新读取后统一表现为 Int16。
- 当前按单要素参数化 INSERT，适合课程项目与常规数据；超大批量导入可后续增加 PostgreSQL COPY。

## 模块边界

本模块不负责：

- 修改 Geometry、Feature、FeatureClass 等共享数据结构；
- Shapefile 或其他文件格式解析；
- 地图绘制、符号渲染、图层管理；
- 桌面窗体和交互界面；
- 屏幕坐标与地图坐标转换；
- 数据编辑工具本身。

其他模块只需通过 `FeatureClass` 与本模块交换数据。