using System;
using System.Collections.Generic;
using System.Text;
using Npgsql;
using NpgsqlTypes;

namespace GIS.PostGIS
{
    /// <summary>模块5统一服务：空间表管理、逐要素同步和基础空间 SQL 查询。</summary>
    public sealed class PostGISService
    {
        private readonly PostGISConnection _database;
        private readonly PostGISImporter _importer;

        public PostGISService(PostGISConnection database)
        {
            _database = database ?? throw new ArgumentNullException(nameof(database));
            _importer = new PostGISImporter(database);
        }

        public List<string> GetSpatialTables() { return _importer.GetSpatialTables(); }
        public Fields GetFields(string tableName, string schemaName = PostGISSchemaMapper.DefaultSchemaName)
        { return _importer.ReadFields(tableName, schemaName); }
        public GeometryTypeConstant GetGeometryType(string tableName, string schemaName = PostGISSchemaMapper.DefaultSchemaName)
        { return _importer.ReadGeometryType(tableName, schemaName); }
        public int GetSrid(string tableName, string schemaName = PostGISSchemaMapper.DefaultSchemaName)
        { return _importer.ReadSrid(tableName, schemaName); }

        public bool TableExists(string tableName, string schemaName = PostGISSchemaMapper.DefaultSchemaName)
        {
            const string sql = @"SELECT EXISTS (
SELECT 1 FROM information_schema.tables
WHERE table_schema = @schema AND table_name = @table);";
            using (NpgsqlConnection connection = _database.CreateConnection())
            {
                connection.Open();
                using (NpgsqlCommand command = new NpgsqlCommand(sql, connection))
                {
                    command.Parameters.Add("schema", NpgsqlDbType.Text).Value = schemaName;
                    command.Parameters.Add("table", NpgsqlDbType.Text).Value = tableName;
                    return Convert.ToBoolean(command.ExecuteScalar());
                }
            }
        }

        public long GetFeatureCount(string tableName, string schemaName = PostGISSchemaMapper.DefaultSchemaName)
        {
            string sql = "SELECT COUNT(*) FROM " + PostGISSchemaMapper.GetQualifiedTableName(schemaName, tableName) + ";";
            return Convert.ToInt64(ExecuteScalar(sql));
        }

        public void DeleteTable(string tableName, string schemaName = PostGISSchemaMapper.DefaultSchemaName)
        {
            string sql = "DROP TABLE IF EXISTS " + PostGISSchemaMapper.GetQualifiedTableName(schemaName, tableName) + ";";
            ExecuteNonQuery(sql);
        }

        public void ClearTable(string tableName, string schemaName = PostGISSchemaMapper.DefaultSchemaName)
        {
            string sql = "TRUNCATE TABLE " + PostGISSchemaMapper.GetQualifiedTableName(schemaName, tableName) +
                         " RESTART IDENTITY;";
            ExecuteNonQuery(sql);
        }

        /// <summary>向已存在的模块5空间表插入要素，并返回数据库生成的 id。</summary>
        public long InsertFeature(FeatureClass featureClass, string tableName, Feature feature, int srid,
            string schemaName = PostGISSchemaMapper.DefaultSchemaName)
        {
            ValidateFeature(featureClass, feature);
            using (NpgsqlConnection connection = _database.CreateConnection())
            {
                connection.Open();
                using (NpgsqlTransaction transaction = connection.BeginTransaction())
                using (NpgsqlCommand command = CreateWriteCommand(connection, transaction, featureClass,
                    tableName, feature, srid, schemaName, null, true))
                {
                    try
                    {
                        long id = Convert.ToInt64(command.ExecuteScalar());
                        transaction.Commit();
                        return id;
                    }
                    catch { transaction.Rollback(); throw; }
                }
            }
        }

        /// <summary>按数据库 id 同步属性和 Geometry。</summary>
        public bool UpdateFeature(FeatureClass featureClass, string tableName, long id, Feature feature, int srid,
            string schemaName = PostGISSchemaMapper.DefaultSchemaName)
        {
            if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
            ValidateFeature(featureClass, feature);
            using (NpgsqlConnection connection = _database.CreateConnection())
            {
                connection.Open();
                using (NpgsqlTransaction transaction = connection.BeginTransaction())
                using (NpgsqlCommand command = CreateWriteCommand(connection, transaction, featureClass,
                    tableName, feature, srid, schemaName, id, false))
                {
                    try
                    {
                        bool changed = command.ExecuteNonQuery() == 1;
                        transaction.Commit();
                        return changed;
                    }
                    catch { transaction.Rollback(); throw; }
                }
            }
        }

        public bool DeleteFeature(string tableName, long id,
            string schemaName = PostGISSchemaMapper.DefaultSchemaName)
        {
            if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
            string sql = "DELETE FROM " + PostGISSchemaMapper.GetQualifiedTableName(schemaName, tableName) +
                         " WHERE " + PostGISSchemaMapper.QuoteIdentifier(PostGISSchemaMapper.DefaultIdColumnName) + " = @id;";
            using (NpgsqlConnection connection = _database.CreateConnection())
            {
                connection.Open();
                using (NpgsqlCommand command = new NpgsqlCommand(sql, connection))
                {
                    command.Parameters.Add("id", NpgsqlDbType.Bigint).Value = id;
                    return command.ExecuteNonQuery() == 1;
                }
            }
        }

        public List<PostGISFeatureRecord> LoadFeatureRecords(string tableName,
            string schemaName = PostGISSchemaMapper.DefaultSchemaName)
        { return _importer.LoadFeatureRecords(tableName, schemaName); }

        /// <summary>ST_Intersects：查询与指定地图范围相交的要素。</summary>
        public FeatureClass QueryIntersects(string tableName, Envelope envelope, out int srid,
            string schemaName = PostGISSchemaMapper.DefaultSchemaName)
        {
            if (envelope == null || envelope.IsNull) throw new ArgumentException("查询范围不能为空。", nameof(envelope));
            string geom = PostGISSchemaMapper.QuoteIdentifier(_importer.GetGeometryColumnName(tableName, schemaName));
            string where = "ST_Intersects(" + geom + ", ST_MakeEnvelope(@min_x, @min_y, @max_x, @max_y, @srid))";
            List<NpgsqlParameter> parameters = new List<NpgsqlParameter>
            {
                Number("min_x", envelope.MinX), Number("min_y", envelope.MinY),
                Number("max_x", envelope.MaxX), Number("max_y", envelope.MaxY),
                Integer("srid", _importer.ReadSrid(tableName, schemaName))
            };
            return _importer.LoadFeatureClassWhere(tableName, out srid, schemaName, where, parameters);
        }

        /// <summary>ST_DWithin：查询到指定坐标距离不超过 distance 的要素，距离单位为表坐标系单位。</summary>
        public FeatureClass QueryWithinDistance(string tableName, Coordinate point, double distance, out int srid,
            string schemaName = PostGISSchemaMapper.DefaultSchemaName)
        {
            if (distance < 0) throw new ArgumentOutOfRangeException(nameof(distance));
            int tableSrid = _importer.ReadSrid(tableName, schemaName);
            string geom = PostGISSchemaMapper.QuoteIdentifier(_importer.GetGeometryColumnName(tableName, schemaName));
            string where = "ST_DWithin(" + geom + ", ST_SetSRID(ST_MakePoint(@x, @y), @srid), @distance)";
            List<NpgsqlParameter> parameters = new List<NpgsqlParameter>
            {
                Number("x", point.X), Number("y", point.Y), Integer("srid", tableSrid), Number("distance", distance)
            };
            return _importer.LoadFeatureClassWhere(tableName, out srid, schemaName, where, parameters);
        }

        /// <summary>ST_Contains：查询完全位于指定 Polygon/MultiPolygon 内的表要素。</summary>
        public FeatureClass QueryContainedBy(string tableName, Geometry container, out int srid,
            string schemaName = PostGISSchemaMapper.DefaultSchemaName)
        {
            if (container == null) throw new ArgumentNullException(nameof(container));
            if (container.GeometryType != GeometryTypeConstant.Polygon && container.GeometryType != GeometryTypeConstant.MultiPolygon)
                throw new ArgumentException("包含查询需要 Polygon 或 MultiPolygon。", nameof(container));
            int tableSrid = _importer.ReadSrid(tableName, schemaName);
            string geom = PostGISSchemaMapper.QuoteIdentifier(_importer.GetGeometryColumnName(tableName, schemaName));
            string where = "ST_Contains(ST_GeomFromText(@query_wkt, @srid), " + geom + ")";
            List<NpgsqlParameter> parameters = new List<NpgsqlParameter>
            {
                Text("query_wkt", container.ToWKT()), Integer("srid", tableSrid)
            };
            return _importer.LoadFeatureClassWhere(tableName, out srid, schemaName, where, parameters);
        }

        /// <summary>ST_Distance：计算指定数据库要素与查询几何的距离。</summary>
        public double GetDistance(string tableName, long id, Geometry other,
            string schemaName = PostGISSchemaMapper.DefaultSchemaName)
        {
            if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
            if (other == null) throw new ArgumentNullException(nameof(other));
            int srid = _importer.ReadSrid(tableName, schemaName);
            string geom = PostGISSchemaMapper.QuoteIdentifier(_importer.GetGeometryColumnName(tableName, schemaName));
            string sql = "SELECT ST_Distance(" + geom + ", ST_GeomFromText(@wkt, @srid)) FROM " +
                PostGISSchemaMapper.GetQualifiedTableName(schemaName, tableName) + " WHERE " +
                PostGISSchemaMapper.QuoteIdentifier(PostGISSchemaMapper.DefaultIdColumnName) + " = @id;";
            using (NpgsqlConnection connection = _database.CreateConnection())
            {
                connection.Open();
                using (NpgsqlCommand command = new NpgsqlCommand(sql, connection))
                {
                    command.Parameters.Add(Text("wkt", other.ToWKT()));
                    command.Parameters.Add(Integer("srid", srid));
                    command.Parameters.Add("id", NpgsqlDbType.Bigint).Value = id;
                    object result = command.ExecuteScalar();
                    if (result == null || result == DBNull.Value) throw new KeyNotFoundException("没有找到 id=" + id + " 的要素。");
                    return Convert.ToDouble(result);
                }
            }
        }

        private static NpgsqlCommand CreateWriteCommand(NpgsqlConnection connection, NpgsqlTransaction transaction,
            FeatureClass featureClass, string tableName, Feature feature, int srid, string schemaName,
            long? updateId, bool returnId)
        {
            StringBuilder sql = new StringBuilder();
            if (updateId.HasValue)
            {
                sql.Append("UPDATE ").Append(PostGISSchemaMapper.GetQualifiedTableName(schemaName, tableName)).Append(" SET ");
                for (int i = 0; i < featureClass.Fields.Count; i++)
                {
                    if (i > 0) sql.Append(", ");
                    sql.Append(PostGISSchemaMapper.QuoteIdentifier(featureClass.Fields.GetItem(i).Name)).Append(" = @p").Append(i);
                }
                if (featureClass.Fields.Count > 0) sql.Append(", ");
                sql.Append(PostGISSchemaMapper.QuoteIdentifier(PostGISSchemaMapper.DefaultGeometryColumnName))
                   .Append(" = ST_GeomFromText(@wkt, @srid) WHERE ")
                   .Append(PostGISSchemaMapper.QuoteIdentifier(PostGISSchemaMapper.DefaultIdColumnName)).Append(" = @id;");
            }
            else
            {
                sql.Append("INSERT INTO ").Append(PostGISSchemaMapper.GetQualifiedTableName(schemaName, tableName)).Append(" (");
                for (int i = 0; i < featureClass.Fields.Count; i++)
                {
                    if (i > 0) sql.Append(", ");
                    sql.Append(PostGISSchemaMapper.QuoteIdentifier(featureClass.Fields.GetItem(i).Name));
                }
                if (featureClass.Fields.Count > 0) sql.Append(", ");
                sql.Append(PostGISSchemaMapper.QuoteIdentifier(PostGISSchemaMapper.DefaultGeometryColumnName)).Append(") VALUES (");
                for (int i = 0; i < featureClass.Fields.Count; i++)
                {
                    if (i > 0) sql.Append(", ");
                    sql.Append("@p").Append(i);
                }
                if (featureClass.Fields.Count > 0) sql.Append(", ");
                sql.Append("ST_GeomFromText(@wkt, @srid))");
                if (returnId) sql.Append(" RETURNING ").Append(PostGISSchemaMapper.QuoteIdentifier(PostGISSchemaMapper.DefaultIdColumnName));
                sql.Append(';');
            }
            NpgsqlCommand command = new NpgsqlCommand(sql.ToString(), connection, transaction);
            for (int i = 0; i < featureClass.Fields.Count; i++)
            {
                NpgsqlParameter parameter = command.Parameters.Add("p" + i, GetDbType(featureClass.Fields.GetItem(i).ValueType));
                object value = feature.Attributes.GetItem(i);
                if (value == null)
                    parameter.Value = DBNull.Value;
                else if (featureClass.Fields.GetItem(i).ValueType == FieldTypeConstant.Byte)
                    parameter.Value = Convert.ToInt16(value);
                else
                    parameter.Value = value;
            }
            command.Parameters.Add("wkt", NpgsqlDbType.Text).Value = feature.Geometry.ToWKT();
            command.Parameters.Add("srid", NpgsqlDbType.Integer).Value = srid;
            if (updateId.HasValue) command.Parameters.Add("id", NpgsqlDbType.Bigint).Value = updateId.Value;
            return command;
        }

        private static void ValidateFeature(FeatureClass featureClass, Feature feature)
        {
            if (featureClass == null) throw new ArgumentNullException(nameof(featureClass));
            if (feature == null || feature.Geometry == null || feature.Attributes == null)
                throw new ArgumentException("Feature、Geometry 和 Attributes 均不能为空。", nameof(feature));
            if (feature.Geometry.GeometryType != featureClass.GeometryType)
                throw new ArgumentException("Feature Geometry 类型与 FeatureClass 不一致。", nameof(feature));
            if (feature.Attributes.Count != featureClass.Fields.Count)
                throw new ArgumentException("Feature 属性数量与 Fields 不一致。", nameof(feature));
        }

        private object ExecuteScalar(string sql)
        {
            using (NpgsqlConnection connection = _database.CreateConnection())
            { connection.Open(); using (NpgsqlCommand command = new NpgsqlCommand(sql, connection)) return command.ExecuteScalar(); }
        }
        private void ExecuteNonQuery(string sql)
        {
            using (NpgsqlConnection connection = _database.CreateConnection())
            { connection.Open(); using (NpgsqlCommand command = new NpgsqlCommand(sql, connection)) command.ExecuteNonQuery(); }
        }
        private static NpgsqlParameter Number(string name, double value)
        { return new NpgsqlParameter(name, NpgsqlDbType.Double) { Value = value }; }
        private static NpgsqlParameter Integer(string name, int value)
        { return new NpgsqlParameter(name, NpgsqlDbType.Integer) { Value = value }; }
        private static NpgsqlParameter Text(string name, string value)
        { return new NpgsqlParameter(name, NpgsqlDbType.Text) { Value = value }; }

        private static NpgsqlDbType GetDbType(FieldTypeConstant type)
        {
            switch (type)
            {
                case FieldTypeConstant.Byte:
                case FieldTypeConstant.Int16: return NpgsqlDbType.Smallint;
                case FieldTypeConstant.Int32: return NpgsqlDbType.Integer;
                case FieldTypeConstant.Single: return NpgsqlDbType.Real;
                case FieldTypeConstant.Double: return NpgsqlDbType.Double;
                case FieldTypeConstant.Text: return NpgsqlDbType.Text;
                case FieldTypeConstant.Date: return NpgsqlDbType.Timestamp;
                case FieldTypeConstant.Boolean: return NpgsqlDbType.Boolean;
                default: throw new NotSupportedException("不支持的字段类型：" + type);
            }
        }
    }
}