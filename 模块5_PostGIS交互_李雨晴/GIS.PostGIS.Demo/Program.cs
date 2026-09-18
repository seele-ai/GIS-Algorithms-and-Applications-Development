using System;
using GIS.PostGIS;

namespace GIS.PostGIS.Demo
{
    internal static class Program
    {
        private const string TestTableName = "postgis_export_test";

        private static void Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine("PostGIS 连接测试");
            Console.WriteLine("----------------");

            string host = ReadValue("数据库地址", "localhost");
            int port = ReadPort("端口", 5432);
            string database = ReadValue("数据库名称", "postgres");
            string username = ReadValue("用户名", "postgres");

            Console.Write("密码：");
            string password = ReadPassword();

            try
            {
                PostGISConnection connection = new PostGISConnection(
                    host,
                    port,
                    database,
                    username,
                    password);

                string version = connection.TestConnection();
                Console.WriteLine();
                Console.WriteLine("连接成功！");
                Console.WriteLine("PostGIS 版本：" + version);

                Console.WriteLine();
                Console.Write("是否执行 Point 导出测试？这会重建 public." + TestTableName + " 表 [y/N]：");
                string answer = Console.ReadLine();
                if (string.Equals(answer, "y", StringComparison.OrdinalIgnoreCase))
                {
                    RunPointExportTest(connection);
                }
                else
                {
                    Console.WriteLine("已跳过导出测试。");
                }

                Console.WriteLine();
                Console.Write("是否执行模块5完整数据库检查？这会重建 public.postgis_test_* 测试表 [y/N]：");
                answer = Console.ReadLine();
                if (string.Equals(answer, "y", StringComparison.OrdinalIgnoreCase))
                    PostGISIntegrationChecks.Run(connection);
                else
                    Console.WriteLine("已跳过完整数据库检查。");
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine("操作失败：" + ex.Message);
            }

            Console.WriteLine();
            Console.WriteLine("按任意键退出……");
            Console.ReadKey(true);
        }

        private static void RunPointExportTest(PostGISConnection connection)
        {
            global::GIS.FeatureClass featureClass = new global::GIS.FeatureClass(
                "测试点",
                global::GIS.GeometryTypeConstant.Point);

            featureClass.Fields.Add(new global::GIS.Field(
                "name", global::GIS.FieldTypeConstant.Text));
            featureClass.Fields.Add(new global::GIS.Field(
                "value", global::GIS.FieldTypeConstant.Double));
            featureClass.Fields.Add(new global::GIS.Field(
                "active", global::GIS.FieldTypeConstant.Boolean));
            featureClass.Fields.Add(new global::GIS.Field(
                "created_at", global::GIS.FieldTypeConstant.Date));

            AddPoint(featureClass, 116.391, 39.907, "北京测试点", 100.5, true);
            AddPoint(featureClass, 121.474, 31.230, "上海测试点", 200.25, true);
            AddPoint(featureClass, 113.264, 23.129, "广州测试点", 300.75, false);

            PostGISExporter exporter = new PostGISExporter(connection);
            int count = exporter.SaveFeatureClass(
                featureClass,
                TestTableName,
                4326,
                true);

            Console.WriteLine("导出成功！");
            Console.WriteLine("数据表：public." + TestTableName);
            Console.WriteLine("写入要素数：" + count);
            Console.WriteLine("Geometry：POINT，SRID：4326");

            PostGISImporter importer = new PostGISImporter(connection);
            int loadedSrid;
            global::GIS.FeatureClass loaded = importer.LoadFeatureClass(
                TestTableName,
                out loadedSrid);

            Console.WriteLine();
            Console.WriteLine("从 PostGIS 读取成功！");
            Console.WriteLine("读取字段数：" + loaded.Fields.Count);
            Console.WriteLine("读取要素数：" + loaded.Features.Count);
            Console.WriteLine("读取几何类型：" + loaded.GeometryType);
            Console.WriteLine("读取 SRID：" + loadedSrid);

            if (loaded.Features.Count != count || loadedSrid != 4326)
                throw new InvalidOperationException("写入与读回的结果不一致。");
            Console.WriteLine("FeatureClass → PostGIS → FeatureClass 闭环验证通过。");
        }

        private static void AddPoint(
            global::GIS.FeatureClass featureClass,
            double x,
            double y,
            string name,
            double value,
            bool active)
        {
            global::GIS.Attributes attributes = new global::GIS.Attributes(
                featureClass.Fields,
                new object[] { name, value, active, DateTime.Now });
            global::GIS.Feature feature = new global::GIS.Feature(
                new global::GIS.Point(x, y),
                attributes);
            featureClass.Add(feature);
        }

        private static string ReadValue(string label, string defaultValue)
        {
            Console.Write(label + " [" + defaultValue + "]：");
            string value = Console.ReadLine();
            return string.IsNullOrWhiteSpace(value) ? defaultValue : value.Trim();
        }

        private static int ReadPort(string label, int defaultValue)
        {
            while (true)
            {
                string value = ReadValue(label, defaultValue.ToString());
                int port;
                if (int.TryParse(value, out port) && port > 0 && port <= 65535)
                    return port;
                Console.WriteLine("请输入有效的端口号。");
            }
        }

        private static string ReadPassword()
        {
            string password = string.Empty;
            while (true)
            {
                ConsoleKeyInfo key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Enter)
                {
                    Console.WriteLine();
                    return password;
                }

                if (key.Key == ConsoleKey.Backspace)
                {
                    if (password.Length > 0)
                    {
                        password = password.Substring(0, password.Length - 1);
                        Console.Write("\b \b");
                    }
                    continue;
                }

                if (!char.IsControl(key.KeyChar))
                {
                    password += key.KeyChar;
                    Console.Write("*");
                }
            }
        }
    }
}