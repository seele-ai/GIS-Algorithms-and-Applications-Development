using System;
using System.Collections.Generic;
using System.Data;
using GIS.PostGIS;
using Npgsql;

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

            RunReadOnlyCompatibilityChecks(connection);

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

            PostGISLayerContext layerContext = importer.LoadLayerContext(pointTable);
            Check(layerContext.Source.SchemaName == "public", "图层来源 Schema 不正确");
            Check(layerContext.Source.TableName == pointTable, "图层来源表名不正确");
            Check(layerContext.Source.Srid == Srid, "图层来源 SRID 不正确");
            Check(layerContext.RecordCount == 1, "图层主键映射数量不正确");
            global::GIS.Feature mappedFeature = layerContext.Layer.FeatureClass.Features.GetItem(0);
            long mappedId = layerContext.GetDatabaseId(mappedFeature);
            global::GIS.Feature reverseMappedFeature;
            Check(layerContext.TryGetFeature(mappedId, out reverseMappedFeature) &&
                  object.ReferenceEquals(mappedFeature, reverseMappedFeature), "图层主键双向映射失败");
            layerContext.Layer.Name = "用户自定义显示名称";
            Check(layerContext.Source.TableName == pointTable, "图层改名不应改变数据库来源");
            Console.WriteLine("[通过] PostGIS 图层来源与主键映射");

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

            long deleteInBatchId = service.InsertFeature(pointSchema, pointTable,
                CreateFeature(pointSchema, global::GIS.Geometry.FromWKT("POINT (14 24)"), "待批量删除", 4), Srid);
            PostGISLayerContext batchContext = importer.LoadLayerContext(pointTable);
            global::GIS.Feature updateInBatch;
            Check(batchContext.TryGetFeature(mappedId, out updateInBatch), "未找到待批量修改要素");
            updateInBatch.Attributes.SetItem("name", "批量修改后要素");
            global::GIS.Feature deleteInBatch;
            Check(batchContext.TryGetFeature(deleteInBatchId, out deleteInBatch), "未找到待批量删除要素");
            batchContext.Layer.FeatureClass.Features.Remove(deleteInBatch);
            global::GIS.Feature insertInBatch = CreateFeature(batchContext.Layer.FeatureClass,
                global::GIS.Geometry.FromWKT("POINT (30 40)"), "批量新增要素", 5);
            batchContext.Layer.FeatureClass.Add(insertInBatch);

            PostGISChangeSet batchChanges = new PostGISChangeSet();
            batchChanges.AddInsert(insertInBatch);
            batchChanges.AddUpdate(batchContext, updateInBatch);
            batchChanges.AddDelete(batchContext, deleteInBatch);
            PostGISCommitResult batchResult = service.CommitChanges(batchContext, batchChanges);
            Check(batchResult.InsertedCount == 1 && batchResult.UpdatedCount == 1 && batchResult.DeletedCount == 1,
                "批量提交计数不正确");
            long batchInsertedId = batchContext.GetDatabaseId(insertInBatch);
            global::GIS.Feature ignoredDeletedFeature;
            Check(!batchContext.TryGetFeature(deleteInBatchId, out ignoredDeletedFeature), "删除后主键映射未清理");
            Check(service.GetFeatureCount(pointTable) == 2, "批量提交后要素数量不正确");
            Console.WriteLine("[通过] 新增 / 修改 / 删除单事务提交");

            PostGISLayerContext rollbackContext = importer.LoadLayerContext(pointTable);
            global::GIS.Feature rollbackInsert = CreateFeature(rollbackContext.Layer.FeatureClass,
                global::GIS.Geometry.FromWKT("POINT (50 60)"), "应被回滚的新增", 6);
            rollbackContext.Layer.FeatureClass.Add(rollbackInsert);
            global::GIS.Feature rollbackUpdate;
            Check(rollbackContext.TryGetFeature(batchInsertedId, out rollbackUpdate), "未找到回滚测试要素");
            long countBeforeRollback = service.GetFeatureCount(pointTable);
            PostGISChangeSet failingChanges = new PostGISChangeSet();
            failingChanges.AddInsert(rollbackInsert);
            failingChanges.AddUpdate(long.MaxValue, rollbackUpdate);
            bool conflictRaised = false;
            try { service.CommitChanges(rollbackContext, failingChanges); }
            catch (DBConcurrencyException) { conflictRaised = true; }
            Check(conflictRaised, "不存在的更新目标应报告并发冲突");
            Check(service.GetFeatureCount(pointTable) == countBeforeRollback, "冲突后事务未完整回滚");
            long unexpectedId;
            Check(!rollbackContext.TryGetDatabaseId(rollbackInsert, out unexpectedId), "回滚后不应登记新增主键");
            Console.WriteLine("[通过] 批量提交失败整体回滚");

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

            double distance = service.GetDistance(pointTable, mappedId,
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

        private static void RunReadOnlyCompatibilityChecks(PostGISConnection database)
        {
            string schema = "postgis_m5_compat_" + Guid.NewGuid().ToString("N").Substring(0, 12);
            string qSchema = PostGISSchemaMapper.QuoteIdentifier(schema);
            using (NpgsqlConnection connection = database.CreateConnection())
            {
                connection.Open();
                Execute(connection, "CREATE SCHEMA " + qSchema + ";");
                try
                {
                    string noKey = qSchema + ".\"no_key\"";
                    Execute(connection, "CREATE TABLE " + noKey +
                        " (name text, geom geometry(Point, 4326));");
                    Execute(connection, "INSERT INTO " + noKey +
                        " (name, geom) VALUES ('无主键', ST_GeomFromText('POINT (10 20)', 4326));");

                    string empty = qSchema + ".\"empty_table\"";
                    Execute(connection, "CREATE TABLE " + empty +
                        " (geom geometry(Point, 4326));");

                    string otherSrid = qSchema + ".\"other_srid\"";
                    Execute(connection, "CREATE TABLE " + otherSrid +
                        " (geom geometry(Point, 3857));");

                    string genericGeometry = qSchema + ".\"generic_geometry\"";
                    Execute(connection, "CREATE TABLE " + genericGeometry +
                        " (geom geometry);");

                    string integerKey = qSchema + ".\"integer_key\"";
                    Execute(connection, "CREATE TABLE " + integerKey +
                        " (id integer PRIMARY KEY, name text, geom geometry(Point, 4326));");
                    Execute(connection, "INSERT INTO " + integerKey +
                        " (id, name, geom) VALUES (0, '整数主键', ST_GeomFromText('POINT (11 21)', 4326));");

                    string uuidKey = qSchema + ".\"uuid_key\"";
                    Execute(connection, "CREATE TABLE " + uuidKey +
                        " (key_id uuid PRIMARY KEY, big_value bigint, decimal_value numeric(12, 3), " +
                        "json_value jsonb, binary_value bytea, clock_value time, nullable_text text, " +
                        "geom geometry(Point, 4326));");
                    Execute(connection, "INSERT INTO " + uuidKey +
                        " (key_id, big_value, decimal_value, json_value, binary_value, clock_value, nullable_text, geom) " +
                        "VALUES ('11111111-1111-1111-1111-111111111111', 9223372036854770000, " +
                        "12.345, '{\"ok\": true}'::jsonb, decode('abcd', 'hex'), TIME '12:00', NULL, " +
                        "ST_GeomFromText('POINT (12 22)', 4326));");

                    string view = qSchema + ".\"no_key_view\"";
                    Execute(connection, "CREATE VIEW " + view + " AS SELECT name, geom FROM " + noKey + ";");

                    PostGISImporter importer = new PostGISImporter(database);
                    PostGISReadOnlyLayerContext noKeyContext = importer.LoadReadOnlyLayerContext("no_key", schema);
                    Check(!noKeyContext.Source.HasKey && noKeyContext.Layer.FeatureClass.Features.Count == 1,
                        "无主键空间表只读加载失败");

                    PostGISReadOnlyLayerContext emptyContext = importer.LoadReadOnlyLayerContext("empty_table", schema);
                    Check(emptyContext.Layer.FeatureClass.Features.Count == 0, "空空间表加载失败");

                    PostGISReadOnlyLayerContext otherSridContext = importer.LoadReadOnlyLayerContext("other_srid", schema);
                    Check(otherSridContext.Source.Srid == 3857, "非 4326 SRID 读取失败");

                    PostGISReadOnlyLayerContext uuidContext = importer.LoadReadOnlyLayerContext("uuid_key", schema);
                    Check(uuidContext.Layer.FeatureClass.Fields.GetItem("big_value") != null,
                        "bigint 字段未导入");
                    Check(uuidContext.Layer.FeatureClass.Fields.GetItem("decimal_value") != null,
                        "numeric 字段未导入");
                    Check(uuidContext.Layer.FeatureClass.Fields.GetItem("json_value") != null,
                        "jsonb 字段未导入");
                    Check(uuidContext.Layer.FeatureClass.Fields.GetItem("nullable_text") != null &&
                          uuidContext.Layer.FeatureClass.Features.GetItem(0).Attributes.GetItem("nullable_text") == null,
                        "NULL 属性未正确保留");
                    Check(uuidContext.Warnings.Count >= 2, "不支持字段未给出明确提示");

                    PostGISReadOnlyLayerContext viewContext = importer.LoadReadOnlyLayerContext("no_key_view", schema);
                    Check(!viewContext.Source.HasKey && viewContext.Layer.FeatureClass.Features.Count == 1,
                        "空间视图只读加载失败");

                    bool unsupportedGeometryRaised = false;
                    try { importer.LoadReadOnlyLayerContext("generic_geometry", schema); }
                    catch (NotSupportedException ex)
                    {
                        unsupportedGeometryRaised = ex.Message.Contains(schema + ".generic_geometry");
                    }
                    Check(unsupportedGeometryRaised, "无类型约束 Geometry 未给出清晰错误");

                    bool missingTableRaised = false;
                    try { importer.LoadReadOnlyLayerContext("missing_table", schema); }
                    catch (InvalidOperationException ex)
                    {
                        missingTableRaised = ex.Message.Contains(schema + ".missing_table");
                    }
                    Check(missingTableRaised, "不存在的空间表未给出清晰错误");

                    PostGISLayerContext integerContext = importer.LoadLayerContext("integer_key", schema);
                    Check(integerContext.GetDatabaseId(integerContext.Layer.FeatureClass.Features.GetItem(0)) == 0,
                        "integer 主键 0 未正确映射");
                    Console.WriteLine("[通过] 只读无主键/空间视图/整数与 UUID 主键/扩展字段类型");
                }
                finally
                {
                    Execute(connection, "DROP SCHEMA IF EXISTS " + qSchema + " CASCADE;");
                }
            }
        }

        private static void Execute(NpgsqlConnection connection, string sql)
        {
            using (NpgsqlCommand command = new NpgsqlCommand(sql, connection))
                command.ExecuteNonQuery();
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
