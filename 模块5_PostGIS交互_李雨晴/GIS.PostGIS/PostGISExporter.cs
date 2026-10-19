using System;
using System.Data;
using System.Text;
using System.Threading;
using Npgsql;
using NpgsqlTypes;

namespace GIS.PostGIS
{
    /// <summary>
    /// 将 FeatureClass 创建并写入 PostgreSQL/PostGIS。
    /// </summary>
    public sealed class PostGISExporter
    {
        private readonly PostGISConnection _database;

        public PostGISExporter(PostGISConnection database)
        {
            _database = database ?? throw new ArgumentNullException(nameof(database));
        }

        /// <summary>
        /// 根据 FeatureClass 创建空空间表及 GiST 空间索引。
        /// 同名表存在时由 overwriteExisting 决定是否删除后重建。
        /// </summary>
        public void CreateTableFromFeatureClass(
            FeatureClass featureClass,
            string tableName,
            int srid,
            bool overwriteExisting = false,
            string schemaName = PostGISSchemaMapper.DefaultSchemaName)
        {
            ValidateFeatureClass(featureClass);

            using (NpgsqlConnection connection = _database.CreateConnection())
            {
                connection.Open();
                using (NpgsqlTransaction transaction = connection.BeginTransaction())
                {
                    try
                    {
                        PrepareTable(connection, transaction, featureClass, tableName, srid,
                            overwriteExisting, schemaName);
                        transaction.Commit();
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
        }

        /// <summary>
        /// 在一个事务中创建空间表、创建索引并写入全部要素。
        /// 返回成功写入的要素数量。
        /// </summary>
        public int SaveFeatureClass(
            FeatureClass featureClass,
            string tableName,
            int srid,
            bool overwriteExisting = false,
            string schemaName = PostGISSchemaMapper.DefaultSchemaName)
        {
            return ExportFeatureClass(featureClass, tableName, srid, srid,
                overwriteExisting ? PostGISExportMode.Overwrite : PostGISExportMode.CreateNew,
                schemaName, CancellationToken.None, null);
        }

        /// <summary>
        /// 将一个完整图层写入 PostGIS。输入几何采用 sourceSrid；当目标 SRID 不同时，
        /// 由 PostGIS 在事务内使用 ST_Transform 转换。追加模式不创建或删除目标表。
        /// </summary>
        public int ExportFeatureClass(
            FeatureClass featureClass,
            string tableName,
            int sourceSrid,
            int targetSrid,
            PostGISExportMode mode,
            string schemaName = PostGISSchemaMapper.DefaultSchemaName,
            CancellationToken cancellationToken = default(CancellationToken),
            IProgress<int> progress = null)
        {
            ValidateFeatureClass(featureClass);
            if (sourceSrid < 0)
                throw new ArgumentOutOfRangeException(nameof(sourceSrid), "源 SRID 不能为负数。");
            if (targetSrid < 0)
                throw new ArgumentOutOfRangeException(nameof(targetSrid), "目标 SRID 不能为负数。");
            if (sourceSrid != targetSrid && (sourceSrid == 0 || targetSrid == 0))
                throw new ArgumentException("执行坐标转换时，源和目标 SRID 都必须为有效的正整数。");
            if (!Enum.IsDefined(typeof(PostGISExportMode), mode))
                throw new ArgumentOutOfRangeException(nameof(mode));

            cancellationToken.ThrowIfCancellationRequested();
            using (NpgsqlConnection connection = _database.CreateConnection())
            {
                connection.Open();
                using (NpgsqlTransaction transaction = connection.BeginTransaction())
                {
                    try
                    {
                        if (mode != PostGISExportMode.Append)
                        {
                            PrepareTable(connection, transaction, featureClass, tableName, targetSrid,
                                mode == PostGISExportMode.Overwrite, schemaName);
                        }

                        int insertedCount = 0;
                        using (NpgsqlCommand command = CreateInsertCommand(connection, transaction,
                            featureClass, tableName, sourceSrid, targetSrid, schemaName))
                        using (cancellationToken.Register(() => { try { command.Cancel(); } catch { } }))
                        {
                            for (int i = 0; i < featureClass.Features.Count; i++)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                Feature feature = featureClass.Features.GetItem(i);
                                ValidateFeature(featureClass, feature, i);
                                SetParameterValues(command, featureClass, feature);
                                command.ExecuteNonQuery();
                                insertedCount++;
                                if (progress != null && (insertedCount == featureClass.Features.Count || insertedCount % 100 == 0))
                                    progress.Report(insertedCount);
                            }
                        }

                        transaction.Commit();
                        return insertedCount;
                    }
                    catch (Exception ex)
                    {
                        try { transaction.Rollback(); } catch { }
                        if (cancellationToken.IsCancellationRequested)
                            throw new OperationCanceledException("上传已取消，数据库事务已回滚。", ex, cancellationToken);
                        throw;
                    }
                }
            }
        }

        /// <summary>
        /// 向已存在且结构匹配的空间表追加一个要素。
        /// 返回数据库受影响的行数。
        /// </summary>
        public int InsertFeature(
            FeatureClass featureClass,
            string tableName,
            Feature feature,
            int srid,
            string schemaName = PostGISSchemaMapper.DefaultSchemaName)
        {
            ValidateFeatureClass(featureClass);
            ValidateFeature(featureClass, feature, -1);

            using (NpgsqlConnection connection = _database.CreateConnection())
            {
                connection.Open();
                using (NpgsqlTransaction transaction = connection.BeginTransaction())
                {
                    try
                    {
                        int affected;
                        using (NpgsqlCommand command = CreateInsertCommand(
                            connection, transaction, featureClass, tableName, srid, srid, schemaName))
                        {
                            SetParameterValues(command, featureClass, feature);
                            affected = command.ExecuteNonQuery();
                        }
                        transaction.Commit();
                        return affected;
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
        }

        private static void PrepareTable(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            FeatureClass featureClass,
            string tableName,
            int srid,
            bool overwriteExisting,
            string schemaName)
        {
            if (overwriteExisting)
            {
                string dropSql = "DROP TABLE IF EXISTS " +
                    PostGISSchemaMapper.GetQualifiedTableName(schemaName, tableName) + ";";
                ExecuteNonQuery(connection, transaction, dropSql);
            }

            string createTableSql = PostGISSchemaMapper.BuildCreateTableSql(
                featureClass, tableName, srid, schemaName);
            ExecuteNonQuery(connection, transaction, createTableSql);

            string createIndexSql = PostGISSchemaMapper.BuildCreateSpatialIndexSql(
                tableName, schemaName);
            ExecuteNonQuery(connection, transaction, createIndexSql);
        }

        private static NpgsqlCommand CreateInsertCommand(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            FeatureClass featureClass,
            string tableName,
            int sourceSrid,
            int targetSrid,
            string schemaName)
        {
            if (sourceSrid < 0 || targetSrid < 0 ||
                (sourceSrid != targetSrid && (sourceSrid == 0 || targetSrid == 0)))
                throw new ArgumentOutOfRangeException(nameof(sourceSrid), "源和目标 SRID 无效。");

            StringBuilder sql = new StringBuilder();
            sql.Append("INSERT INTO ")
               .Append(PostGISSchemaMapper.GetQualifiedTableName(schemaName, tableName))
               .Append(" (");

            for (int i = 0; i < featureClass.Fields.Count; i++)
            {
                if (i > 0)
                    sql.Append(", ");
                sql.Append(PostGISSchemaMapper.QuoteIdentifier(featureClass.Fields.GetItem(i).Name));
            }
            if (featureClass.Fields.Count > 0)
                sql.Append(", ");
            sql.Append(PostGISSchemaMapper.QuoteIdentifier(PostGISSchemaMapper.DefaultGeometryColumnName));
            sql.Append(") VALUES (");

            for (int i = 0; i < featureClass.Fields.Count; i++)
            {
                if (i > 0)
                    sql.Append(", ");
                sql.Append("@p").Append(i);
            }
            if (featureClass.Fields.Count > 0)
                sql.Append(", ");
            if (sourceSrid == targetSrid)
                sql.Append("ST_GeomFromText(@geometry_wkt, @source_srid));");
            else
                sql.Append("ST_Transform(ST_GeomFromText(@geometry_wkt, @source_srid), @target_srid));");

            NpgsqlCommand command = new NpgsqlCommand(sql.ToString(), connection, transaction);
            for (int i = 0; i < featureClass.Fields.Count; i++)
            {
                Field field = featureClass.Fields.GetItem(i);
                command.Parameters.Add("p" + i, GetNpgsqlDbType(field.ValueType));
            }
            command.Parameters.Add("geometry_wkt", NpgsqlDbType.Text);
            command.Parameters.Add("source_srid", NpgsqlDbType.Integer).Value = sourceSrid;
            if (sourceSrid != targetSrid)
                command.Parameters.Add("target_srid", NpgsqlDbType.Integer).Value = targetSrid;
            return command;
        }

        private static void SetParameterValues(
            NpgsqlCommand command,
            FeatureClass featureClass,
            Feature feature)
        {
            for (int i = 0; i < featureClass.Fields.Count; i++)
            {
                object value = feature.Attributes.GetItem(i);
                if (value == null)
                {
                    command.Parameters["p" + i].Value = DBNull.Value;
                }
                else if (featureClass.Fields.GetItem(i).ValueType == FieldTypeConstant.Byte)
                {
                    command.Parameters["p" + i].Value = Convert.ToInt16(value);
                }
                else
                {
                    command.Parameters["p" + i].Value = value;
                }
            }

            command.Parameters["geometry_wkt"].Value = feature.Geometry.ToWKT();
        }

        private static void ExecuteNonQuery(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            string sql)
        {
            using (NpgsqlCommand command = new NpgsqlCommand(sql, connection, transaction))
            {
                command.ExecuteNonQuery();
            }
        }

        private static void ValidateFeatureClass(FeatureClass featureClass)
        {
            if (featureClass == null)
                throw new ArgumentNullException(nameof(featureClass));
            if (featureClass.Fields == null)
                throw new ArgumentException("FeatureClass.Fields 不能为空。", nameof(featureClass));
            if (featureClass.Features == null)
                throw new ArgumentException("FeatureClass.Features 不能为空。", nameof(featureClass));

            // 复用 SchemaMapper 的完整字段和几何类型校验，但不执行 SQL。
            PostGISSchemaMapper.BuildCreateTableSql(featureClass, "validation_table", 0);
        }

        private static void ValidateFeature(FeatureClass featureClass, Feature feature, int featureIndex)
        {
            string position = featureIndex >= 0 ? "，位置：" + featureIndex : string.Empty;
            if (feature == null)
                throw new ArgumentException("Feature 不能为空" + position + "。", nameof(feature));
            if (feature.Geometry == null)
                throw new ArgumentException("Feature.Geometry 不能为空" + position + "。", nameof(feature));
            if (feature.Geometry.GeometryType != featureClass.GeometryType)
            {
                throw new ArgumentException(
                    "要素几何类型 " + feature.Geometry.GeometryType +
                    " 与空间表类型 " + featureClass.GeometryType + " 不一致" + position + "。", nameof(feature));
            }
            if (feature.Attributes == null)
                throw new ArgumentException("Feature.Attributes 不能为空" + position + "。", nameof(feature));
            if (feature.Attributes.Count != featureClass.Fields.Count)
            {
                throw new ArgumentException(
                    "要素属性数量与 FeatureClass.Fields 不一致" + position + "。", nameof(feature));
            }
        }

        private static NpgsqlDbType GetNpgsqlDbType(FieldTypeConstant fieldType)
        {
            switch (fieldType)
            {
                case FieldTypeConstant.Byte:
                case FieldTypeConstant.Int16:
                    return NpgsqlDbType.Smallint;
                case FieldTypeConstant.Int32:
                    return NpgsqlDbType.Integer;
                case FieldTypeConstant.Int64:
                    return NpgsqlDbType.Bigint;
                case FieldTypeConstant.Single:
                    return NpgsqlDbType.Real;
                case FieldTypeConstant.Double:
                    return NpgsqlDbType.Double;
                case FieldTypeConstant.Decimal:
                    return NpgsqlDbType.Numeric;
                case FieldTypeConstant.Text:
                    return NpgsqlDbType.Text;
                case FieldTypeConstant.Date:
                    return NpgsqlDbType.Timestamp;
                case FieldTypeConstant.Boolean:
                    return NpgsqlDbType.Boolean;
                default:
                    throw new NotSupportedException("不支持的字段类型：" + fieldType);
            }
        }
    }
}
