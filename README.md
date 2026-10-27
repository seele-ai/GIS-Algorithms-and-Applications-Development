# 简易 GIS 系统

这是课程作业的功能融合版，使用 C# 和 WinForms 开发。主程序集成地图显示、图层管理、投影转换、Shapefile 导入导出、要素创建与编辑，以及 PostGIS 数据读取。

## 运行

使用 Visual Studio 2022 打开根目录的 `GIS.sln`，将 `GIS.Display.Demo` 设为启动项目后按 `F5`。

也可以在仓库根目录执行：

```powershell
dotnet build GIS.sln -c Release -m:1
```

编译完成后运行：

```text
GIS_Demo\GIS.Display.Demo\bin\Release\net48\GIS.Display.Demo.exe
```

环境要求：Windows、Visual Studio 2022 或 .NET SDK 8、.NET Framework 4.8 目标包。

主程序启用逐显示器高 DPI 支持，减少 Windows 缩放导致的文字模糊。若要复制编译后的程序，请将 `GIS.Display.Demo.exe.config` 与可执行文件放在同一目录，以保留该设置。

## 工程结构

```text
GIS.sln
├── MapObjects/GIS/                         核心 GIS 类库
├── GIS_Demo/GIS.Display/                   地图显示与交互控件
├── GIS_Demo/GIS.PostGIS/                    PostGIS 数据访问类库
└── GIS_Demo/GIS.Display.Demo/              桌面主程序
```

解决方案只保留以上四个运行工程。自动检查程序、控制台自测程序和独立 PostGIS 演示程序已从简洁版本中移除，不影响主程序功能。

## 主要功能

- 点、线、面及复合几何数据结构，支持 WKT 读写和空间范围计算。
- 图层显示、拖动排序、显隐、缩放、平移、点选、框选和属性查看；新图层自动使用不同的默认颜色，已有自定义符号不变。属性表嵌入主界面下方，可拖动调整高度、切换图层标签页、查看全部或仅选中要素、调整列宽、与地图选择联动，并可直接编辑属性。
- 简单渲染、唯一值渲染、分级渲染、注记和符号编辑。
- 经纬度、高斯-克吕格、UTM、Lambert 投影显示与坐标转换。
- Shapefile 导入与导出，支持 SHP、SHX、DBF、PRJ、CPG 配套文件。
- 新建图层和要素，支持顶点编辑、属性编辑、移动、删除、撤销和重做。
- PostGIS 连接、空间表浏览、后台读取、非 4326 坐标转换，以及图层新建、覆盖或追加上传。

### PostGIS 交互说明

PostGIS 图层读取时，程序会先读取空间字段、几何类型、坐标系和属性字段，再将数据库中的 Geometry 数据转换为软件内部的要素和图层，并在后台加入地图与属性表。非 WGS84 数据会在显示前转换为地图使用的经纬度坐标，读取过程中会显示进度并支持取消。

上传时，程序会创建当前图层的上传副本，先统一整理为 WGS84 坐标，再根据“存储坐标系”选择转换为 WGS84、Web Mercator 或 CGCS2000，最后写入 PostgreSQL/PostGIS。用户可以选择创建新表、替换已有表或追加到已有表；追加前会检查属性字段、几何类型和坐标系是否兼容。上传在一个数据库事务中完成，取消或失败时会整体回滚。

空间表主要由自增主键、属性列、Geometry 空间列和 GiST 空间索引组成。主键用于唯一标识要素，属性列保存普通信息，Geometry 列保存点、线或面及其空间参考编号，GiST 索引用于加快相交、包含、范围和距离等空间查询。WKT 是几何数据的文字交换格式，数据进入 PostGIS 后仍以原生 Geometry 类型保存。

当前已验证 PostGIS 连接、空间表列表读取和图层加载功能；在本机 PostgreSQL/PostGIS 环境中成功加载 `public.admin_city` 图层并读取 363 个要素。上传功能支持进度显示和取消，写入前会进行字段、几何和坐标系检查。

## 使用说明

主程序启动后是空地图：

1. 点击“导入要素”加载 Shapefile，或点击“PostGIS 数据源”连接数据库。
2. 使用图层面板管理图层；拖动图层名称可调整上下叠放顺序。顶部工具栏可缩放、选择和切换投影。
3. 使用第二行工具栏新建图层、创建要素或编辑已有要素。
   右键图层选择“打开属性表”后，表格显示在主界面下方；可用“仅显示选中”查看地图选择结果，拖动分隔线调整高度，点击图层标签右侧的 × 关闭属性表。关闭全部属性表后，下方面板自动收起。
4. 点击“导出要素”保存为 Shapefile；连接 PostGIS 后可点击“上传 PostGIS”把图层写入数据库。
5. PostGIS 图层在后台完整读取，读取过程中可查看进度或取消操作。

PostGIS 数据源需要 PostgreSQL、PostGIS 扩展和有效的数据库连接参数。模块接口说明见 `GIS_Demo/GIS.PostGIS/README_PostGIS.md`。

## 数据约定

- 地理坐标使用 WGS84，经度存入 X，纬度存入 Y。
- Shapefile 导出固定使用 WGS84 经纬度。
- 非 WGS84 数据导入时需要确认基准转换参数。
- PostGIS 保存接口需要调用方明确提供 SRID。
