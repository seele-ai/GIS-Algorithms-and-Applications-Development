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

## 工程结构

```text
GIS.sln
├── MapObjects/GIS/                         核心 GIS 类库
├── GIS_Demo/GIS.Display/                   地图显示与交互控件
├── 模块5_PostGIS交互_李雨晴/GIS.PostGIS/   PostGIS 数据访问类库
└── GIS_Demo/GIS.Display.Demo/              桌面主程序
```

解决方案只保留以上四个运行工程。自动检查程序、控制台自测程序和独立 PostGIS 演示程序已从简洁版本中移除，不影响主程序功能。

## 主要功能

- 点、线、面及复合几何数据结构，支持 WKT 读写和空间范围计算。
- 图层显示、排序、显隐、缩放、平移、点选、框选和属性查看。
- 简单渲染、唯一值渲染、分级渲染、注记和符号编辑。
- 经纬度、高斯-克吕格、UTM、Lambert 投影显示与坐标转换。
- Shapefile 导入与导出，支持 SHP、SHX、DBF、PRJ、CPG 配套文件。
- 新建图层和要素，支持顶点编辑、属性编辑、移动、删除、撤销和重做。
- PostGIS 连接、空间表浏览、后台限量读取、非 4326 坐标转换，以及图层新建、覆盖或追加上传。

## 使用说明

主程序启动后是空地图：

1. 点击“导入要素”加载 Shapefile，或点击“PostGIS 数据源”连接数据库。
2. 使用图层面板管理图层，使用顶部工具栏缩放、选择和切换投影。
3. 使用第二行工具栏新建图层、创建要素或编辑已有要素。
4. 点击“导出要素”保存为 Shapefile；连接 PostGIS 后可点击“上传 PostGIS”把图层写入数据库。
5. PostGIS 数据源面板默认最多加载 50,000 个要素，可调整上限或取消正在执行的读取。

PostGIS 数据源需要 PostgreSQL、PostGIS 扩展和有效的数据库连接参数。模块接口说明见 `模块5_PostGIS交互_李雨晴/README_PostGIS.md`。

## 数据约定

- 地理坐标使用 WGS84，经度存入 X，纬度存入 Y。
- Shapefile 导出固定使用 WGS84 经纬度。
- 非 WGS84 数据导入时需要确认基准转换参数。
- PostGIS 保存接口需要调用方明确提供 SRID。
