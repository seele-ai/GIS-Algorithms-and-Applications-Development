using System;
using System.Collections.Generic;
using GIS.PostGIS;

namespace GIS.PostGIS.Demo
{
    internal static class PostGISIntegrationChecks
    {
        private const int Srid = 4326;

        public static void Run(PostGISConnection connection)
        {
            Console.WriteLine();
            Console.WriteLine("开始模块5完整数据库检查……");
            PostGISExporter exporter = new PostGISExporter(connection);
            PostGISImporter importer = new PostGISImporter(connection);
            PostGISService service = new PostGISService(connection);

            Dictionary<string, string> cases = new Dictionary<string, string>
            {
                { "postgis_test_point", "POINT (10 20)" },
                { "postgis_test_linestring", "LINESTRING (0 0, 1 1, 2 0)" },
                { "postgis_test_polygon", "POLYGON ((0 0, 6 0, 6 6, 0 6, 0 0), (2 2, 4 2, 4 4, 2 4, 2 2))" },
                { "postgis_test_multipoint", "MULTIPOINT ((0 0), (1 1), (2 2))" },
                { "postgis_test_multilinestring", "MULTILINESTRING ((0 0, 1 1), (2 2, 3 3))" },
                { "postgis_test_multipolygon", "MULTIPOLYGON (((0 0, 2 0, 2 2, 0 2, 0 0)), ((3 3, 5 3, 5 5, 3 5, 3 3)))" }
            };

            foreach (KeyValuePair<string, string> item in cases)
            {
                global::GIS.Geometry geometry = global::GIS.Geometry.FromWKT(item.Value);
                global::GIS.FeatureClass source = CreateFeatureClass(item.Key, geometry, "原始要素", 1);
                exporter.SaveFeatureClass(source, item.Key, Srid, true);

                int loadedSrid;
                global::GIS.FeatureClass loaded = importer.LoadFeatureClass(item.Key, out loadedSrid);
                Check(loaded.Features.Count == 1, item.Key + " 要素数量不一致");
                Check(loaded.Fields.Count == 5, item.Key + " 字段数量不一致");
                Check(loaded.GeometryType == geometry.GeometryType, item.Key + " 几何类型不一致");
                Check(loadedSrid == Srid, item.Key + " SRID 不一致");
                Check(loaded.Features.GetItem(0).Attributes.GetItem("nullable_text") == null,
                    item.Key + " NULL 属性未正确恢复");
                Console.WriteLine("[通过] " + geometry.GeometryType + " 双向转换");
            }

            const string pointTable = "postgis_test_point";
            Check(service.TableExists(pointTable), "TableExists 失败");
            Check(service.GetFeatureCount(pointTable) == 1, "GetFeatureCount 失败");
            Check(service.GetGeometryType(pointTable) == global::GIS.GeometryTypeConstant.Point, "GetGeometryType 失败");
            Check(service.GetSrid(pointTable) == Srid, "GetSrid 失败");
            Check(service.GetFields(pointTable).Count == 5, "GetFields 失败");
            Check(service.GetSpatialTables().Contains("public." + pointTable), "GetSpatialTables 失败");
            Console.WriteLine("[通过] 空间表元数据与计数");

            global::GIS.FeatureClass pointSchema = importer.LoadFeatureClass(pointTable);
            global::GIS.Feature insertedFeature = CreateFeature(pointSchema, global::GIS.Geometry.FromWKT("POINT (12 22)"), "新增要素", 2);
            long insertedId = service.InsertFeature(pointSchema, pointTable, insertedFeature, Srid);
            Check(service.GetFeatureCount(pointTable) == 2, "INSERT 失败");

            global::GIS.Feature updatedFeature = CreateFeature(pointSchema, global::GIS.Geometry.FromWKT("POINT (13 23)"), "修改后要素", 3);
            Check(service.UpdateFeature(pointSchema, pointTable, insertedId, updatedFeature, Srid), "UPDATE 失败");
            List<PostGISFeatureRecord> records = service.LoadFeatureRecords(pointTable);
            PostGISFeatureRecord updatedRecord = records.Find(r => r.Id == insertedId);
            Check(updatedRecord != null, "主键映射读取失败");
            Check(Convert.ToString(updatedRecord.Feature.Attributes.GetItem("name")) == "修改后要素", "UPDATE 属性未生效");
            Check(service.DeleteFeature(pointTable, insertedId), "DELETE 失败");
            Check(service.GetFeatureCount(pointTable) == 1, "DELETE 后数量不正确");
            Console.WriteLine("[通过] INSERT / UPDATE / DELETE 与主键映射");

            int querySrid;
            global::GIS.FeatureClass intersects = service.QueryIntersects(pointTable,
                new global::GIS.Envelope(9, 11, 19, 21), out querySrid);
            Check(intersects.Features.Count == 1 && querySrid == Srid, "ST_Intersects 失败");

            global::GIS.FeatureClass nearby = service.QueryWithinDistance(pointTable,
                new global::GIS.Coordinate(10, 20), 0.1, out querySrid);
            Check(nearby.Features.Count == 1, "ST_DWithin 失败");

            global::GIS.Geometry container = global::GIS.Geometry.FromWKT(
                "POLYGON ((9 19, 11 19, 11 21, 9 21, 9 19))");
            global::GIS.FeatureClass contained = service.QueryContainedBy(pointTable, container, out querySrid);
            Check(contained.Features.Count == 1, "ST_Contains 失败");

            long originalId = service.LoadFeatureRecords(pointTable)[0].Id;
            double distance = service.GetDistance(pointTable, originalId,
                global::GIS.Geometry.FromWKT("POINT (10 20)"));
            Check(Math.Abs(distance) < 1e-12, "ST_Distance 失败");
            Console.WriteLine("[通过] ST_Intersects / ST_DWithin / ST_Contains / ST_Distance");

            const string managementTable = "postgis_test_management";
            global::GIS.FeatureClass empty = CreateEmptyFeatureClass(managementTable,
                global::GIS.GeometryTypeConstant.Point);
            exporter.CreateTableFromFeatureClass(empty, managementTable, Srid, true);
            Check(service.TableExists(managementTable), "创建管理测试表失败");
            service.ClearTable(managementTable);
            Check(service.GetFeatureCount(managementTable) == 0, "ClearTable 失败");
            service.DeleteTable(managementTable);
            Check(!service.TableExists(managementTable), "DeleteTable 失败");
            Console.WriteLine("[通过] ClearTable / DeleteTable");

            Console.WriteLine();
            Console.WriteLine("模块5完整数据库检查全部通过。");
        }

        private static global::GIS.FeatureClass CreateFeatureClass(string name,
            global::GIS.Geometry geometry, string label, int sequence)
        {
            global::GIS.FeatureClass featureClass = CreateEmptyFeatureClass(name, geometry.GeometryType);
            featureClass.Add(CreateFeature(featureClass, geometry, label, sequence));
            return featureClass;
        }

        private static global::GIS.FeatureClass CreateEmptyFeatureClass(string name,
            global::GIS.GeometryTypeConstant geometryType)
        {
            global::GIS.FeatureClass featureClass = new global::GIS.FeatureClass(name, geometryType);
            featureClass.Fields.Add(new global::GIS.Field("name", global::GIS.FieldTypeConstant.Text));
            featureClass.Fields.Add(new global::GIS.Field("sequence", global::GIS.FieldTypeConstant.Int32));
            featureClass.Fields.Add(new global::GIS.Field("value", global::GIS.FieldTypeConstant.Double));
            featureClass.Fields.Add(new global::GIS.Field("enabled", global::GIS.FieldTypeConstant.Boolean));
            featureClass.Fields.Add(new global::GIS.Field("nullable_text", global::GIS.FieldTypeConstant.Text));
            return featureClass;
        }

        private static global::GIS.Feature CreateFeature(global::GIS.FeatureClass featureClass,
            global::GIS.Geometry geometry, string label, int sequence)
        {
            global::GIS.Attributes attributes = new global::GIS.Attributes(featureClass.Fields,
                new object[] { label, sequence, sequence + 0.5, true, null });
            return new global::GIS.Feature(geometry, attributes);
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}