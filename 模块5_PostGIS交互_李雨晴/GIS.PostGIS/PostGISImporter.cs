using System;
using System.Collections.Generic;
using System.Data;
using System.Text;
using Npgsql;
using NpgsqlTypes;

namespace GIS.PostGIS
{
    /// <summary>从 PostgreSQL/PostGIS 读取空间表并构造 FeatureClass。</summary>
    public sealed class PostGISImporter
    {
        private readonly PostGISConnection _database;

        public PostGISImporter(PostGISConnection database)
        {
            _database = database ?? throw new ArgumentNullException(nameof(database));
        }

        public List<string> GetSpatialTables()
        {
            const string sql = @"
SELECT DISTINCT table_schema, table_name
FROM information_schema.columns
WHERE udt_name = 'geometry'
ORDER BY table_schema, table_name;";
            List<string> tables = new List<string>();
            using (NpgsqlConnection connection = _database.CreateConnection())
            {
                connection.Open();
                using (NpgsqlCommand command = new NpgsqlCommand(sql, connection))
                using (NpgsqlDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                        tables.Add(reader.GetString(0) + "." + reader.GetString(1));
                }
            }
            return tables;
        }

        public Fields ReadFields(string tableName, string schemaName = PostGISSchemaMapper.DefaultSchemaName)
        {
            GeometryMetadata geometry = ReadGeometryMetadata(tableName, schemaName);
            return ReadFieldsInternal(tableName, schemaName, geometry.ColumnName);
        }

        public GeometryTypeConstant ReadGeometryType(string tableName, string schemaName = PostGISSchemaMapper.DefaultSchemaName)
        {
            return ReadGeometryMetadata(tableName, schemaName).GeometryType;
        }

        public int ReadSrid(string tableName, string schemaName = PostGISSchemaMapper.DefaultSchemaName)
        {
            return ReadGeometryMetadata(tableName, schemaName).Srid;
        }

        public FeatureClass LoadFeatureClass(string tableName, out int srid,
            string schemaName = PostGISSchemaMapper.DefaultSchemaName)
        {
            return LoadFeatureClassWhere(tableName, out srid, schemaName, null, null);
        }

        public FeatureClass LoadFeatureClass(string tableName,
            string schemaName = PostGISSchemaMapper.DefaultSchemaName)
        {
            int ignored;
            return LoadFeatureClass(tableName, out ignored, schemaName);
        }

        /// <summary>读取数据库主键及 Feature，用于 UPDATE/DELETE 的稳定对应。</summary>
        public List<PostGISFeatureRecord> LoadFeatureRecords(string tableName,
            string schemaName = PostGISSchemaMapper.DefaultSchemaName)
        {
            GeometryMetadata geometry = ReadGeometryMetadata(tableName, schemaName);
            Fields fields = ReadFieldsInternal(tableName, schemaName, geometry.ColumnName);
            FeatureClass featureClass = CreateFeatureClass(tableName, geometry.GeometryType, fields);
            string sql = BuildSelectSql(fields, tableName, schemaName, geometry.ColumnName, true, null);
            List<PostGISFeatureRecord> records = new List<PostGISFeatureRecord>();

            using (NpgsqlConnection connection = _database.CreateConnection())
            {
                connection.Open();
                using (NpgsqlCommand command = new NpgsqlCommand(sql, connection))
                using (NpgsqlDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        long id = Convert.ToInt64(reader.GetValue(0));
                        Feature feature = ReadFeature(reader, 1, featureClass);
                        records.Add(new PostGISFeatureRecord(id, feature));
                    }
                }
            }
            return records;
        }

        internal FeatureClass LoadFeatureClassWhere(
            string tableName,
            out int srid,
            string schemaName,
            string whereSql,
            IList<NpgsqlParameter> parameters)
        {
            GeometryMetadata geometry = ReadGeometryMetadata(tableName, schemaName);
            Fields fields = ReadFieldsInternal(tableName, schemaName, geometry.ColumnName);
            FeatureClass featureClass = CreateFeatureClass(tableName, geometry.GeometryType, fields);
            string sql = BuildSelectSql(fields, tableName, schemaName, geometry.ColumnName, false, whereSql);

            using (NpgsqlConnection connection = _database.CreateConnection())
            {
                connection.Open();
                using (NpgsqlCommand command = new NpgsqlCommand(sql, connection))
                {
                    if (parameters != null)
                    {
                        foreach (NpgsqlParameter parameter in parameters)
                            command.Parameters.Add(parameter);
                    }
                    using (NpgsqlDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                            featureClass.Add(ReadFeature(reader, 0, featureClass));
                    }
                }
            }
            srid = geometry.Srid;
            return featureClass;
        }

        internal string GetGeometryColumnName(string tableName, string schemaName)
        {
            return ReadGeometryMetadata(tableName, schemaName).ColumnName;
        }

        private Fields ReadFieldsInternal(string tableName, string schemaName, string geometryColumnName)
        {
            const string sql = @"
SELECT column_name, data_type, udt_name
FROM information_schema.columns
WHERE table_schema = @schema AND table_name = @table
ORDER BY ordinal_position;";
            Fields fields = new Fields();
            using (NpgsqlConnection connection = _database.CreateConnection())
            {
                connection.Open();
                using (NpgsqlCommand command = new NpgsqlCommand(sql, connection))
                {
                    command.Parameters.Add("schema", NpgsqlDbType.Text).Value = schemaName;
                    command.Parameters.Add("table", NpgsqlDbType.Text).Value = tableName;
                    using (NpgsqlDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string name = reader.GetString(0);
                            if (string.Equals(name, geometryColumnName, StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(name, PostGISSchemaMapper.DefaultIdColumnName, StringComparison.OrdinalIgnoreCase))
                                continue;
                            fields.Add(new Field(name, MapPostgreSqlType(reader.GetString(1), reader.GetString(2))));
                        }
                    }
                }
            }
            return fields;
        }

        private GeometryMetadata ReadGeometryMetadata(string tableName, string schemaName)
        {
            if (string.IsNullOrWhiteSpace(tableName))
                throw new ArgumentException("表名不能为空。", nameof(tableName));
            if (string.IsNullOrWhiteSpace(schemaName))
                throw new ArgumentException("Schema 名称不能为空。", nameof(schemaName));

            const string sql = @"
SELECT a.attname, pg_catalog.format_type(a.atttypid, a.atttypmod)
FROM pg_catalog.pg_attribute a
JOIN pg_catalog.pg_class c ON c.oid = a.attrelid
JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
JOIN pg_catalog.pg_type t ON t.oid = a.atttypid
WHERE n.nspname = @schema AND c.relname = @table
  AND t.typname = 'geometry' AND a.attnum > 0 AND NOT a.attisdropped
ORDER BY a.attnum;";
            List<GeometryMetadata> results = new List<GeometryMetadata>();
            using (NpgsqlConnection connection = _database.CreateConnection())
            {
                connection.Open();
                using (NpgsqlCommand command = new NpgsqlCommand(sql, connection))
                {
                    command.Parameters.Add("schema", NpgsqlDbType.Text).Value = schemaName;
                    command.Parameters.Add("table", NpgsqlDbType.Text).Value = tableName;
                    using (NpgsqlDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string formattedType = reader.GetString(1);
                            results.Add(ParseGeometryMetadata(reader.GetString(0), formattedType));
                        }
                    }
                }
            }
            if (results.Count == 0)
                throw new InvalidOperationException("未找到空间表 " + schemaName + "." + tableName + " 的 Geometry 字段。");
            if (results.Count > 1)
                throw new NotSupportedException("当前版本仅支持每张表一个 Geometry 字段。");
            return results[0];
        }

        private static GeometryMetadata ParseGeometryMetadata(string columnName, string formattedType)
        {
            int left = formattedType.IndexOf('(');
            int comma = formattedType.LastIndexOf(',');
            int right = formattedType.LastIndexOf(')');
            if (left < 0 || comma <= left || right <= comma)
                throw new NotSupportedException("Geometry 字段缺少类型或 SRID 约束：" + formattedType);
            string type = formattedType.Substring(left + 1, comma - left - 1).Trim();
            string sridText = formattedType.Substring(comma + 1, right - comma - 1).Trim();
            int srid;
            if (!int.TryParse(sridText, out srid))
                throw new DataException("无法读取 Geometry SRID：" + formattedType);
            return new GeometryMetadata { ColumnName = columnName, GeometryType = ParseGeometryType(type), Srid = srid };
        }

        private static FeatureClass CreateFeatureClass(string tableName, GeometryTypeConstant type, Fields fields)
        {
            FeatureClass featureClass = new FeatureClass(tableName, type);
            for (int i = 0; i < fields.Count; i++)
                featureClass.Fields.Add(fields.GetItem(i));
            return featureClass;
        }

        private static Feature ReadFeature(NpgsqlDataReader reader, int offset, FeatureClass featureClass)
        {
            object[] values = new object[featureClass.Fields.Count];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = reader.IsDBNull(offset + i) ? null :
                    ConvertFieldValue(reader.GetValue(offset + i), featureClass.Fields.GetItem(i).ValueType);
            }
            int wktIndex = offset + featureClass.Fields.Count;
            if (reader.IsDBNull(wktIndex))
                throw new DataException("空间表中存在空 Geometry，当前 FeatureClass 不支持空几何对象。");
            Geometry geometry = Geometry.FromWKT(reader.GetString(wktIndex));
            return new Feature(geometry, new Attributes(featureClass.Fields, values));
        }

        private static string BuildSelectSql(Fields fields, string tableName, string schemaName,
            string geometryColumnName, bool includeId, string whereSql)
        {
            StringBuilder sql = new StringBuilder("SELECT ");
            bool hasPrevious = false;
            if (includeId)
            {
                sql.Append(PostGISSchemaMapper.QuoteIdentifier(PostGISSchemaMapper.DefaultIdColumnName));
                hasPrevious = true;
            }
            for (int i = 0; i < fields.Count; i++)
            {
                if (hasPrevious) sql.Append(", ");
                sql.Append(PostGISSchemaMapper.QuoteIdentifier(fields.GetItem(i).Name));
                hasPrevious = true;
            }
            if (hasPrevious) sql.Append(", ");
            sql.Append("ST_AsText(").Append(PostGISSchemaMapper.QuoteIdentifier(geometryColumnName))
               .Append(") AS geometry_wkt FROM ")
               .Append(PostGISSchemaMapper.GetQualifiedTableName(schemaName, tableName));
            if (!string.IsNullOrWhiteSpace(whereSql))
                sql.Append(" WHERE ").Append(whereSql);
            sql.Append(';');
            return sql.ToString();
        }

        private static FieldTypeConstant MapPostgreSqlType(string dataType, string udtName)
        {
            switch (dataType)
            {
                case "smallint": return FieldTypeConstant.Int16;
                case "integer": return FieldTypeConstant.Int32;
                case "real": return FieldTypeConstant.Single;
                case "double precision": return FieldTypeConstant.Double;
                case "text":
                case "character varying":
                case "character": return FieldTypeConstant.Text;
                case "date":
                case "timestamp without time zone":
                case "timestamp with time zone": return FieldTypeConstant.Date;
                case "boolean": return FieldTypeConstant.Boolean;
                default: throw new NotSupportedException("不支持的 PostgreSQL 字段类型：" + dataType + " (" + udtName + ")");
            }
        }

        private static GeometryTypeConstant ParseGeometryType(string value)
        {
            switch (value.ToUpperInvariant())
            {
                case "POINT": return GeometryTypeConstant.Point;
                case "LINESTRING": return GeometryTypeConstant.LineString;
                case "POLYGON": return GeometryTypeConstant.Polygon;
                case "MULTIPOINT": return GeometryTypeConstant.MultiPoint;
                case "MULTILINESTRING": return GeometryTypeConstant.MultiLineString;
                case "MULTIPOLYGON": return GeometryTypeConstant.MultiPolygon;
                default: throw new NotSupportedException("不支持的 PostGIS Geometry 类型：" + value);
            }
        }

        private static object ConvertFieldValue(object value, FieldTypeConstant type)
        {
            switch (type)
            {
                case FieldTypeConstant.Byte: return Convert.ToByte(value);
                case FieldTypeConstant.Int16: return Convert.ToInt16(value);
                case FieldTypeConstant.Int32: return Convert.ToInt32(value);
                case FieldTypeConstant.Single: return Convert.ToSingle(value);
                case FieldTypeConstant.Double: return Convert.ToDouble(value);
                case FieldTypeConstant.Text: return Convert.ToString(value);
                case FieldTypeConstant.Date: return Convert.ToDateTime(value);
                case FieldTypeConstant.Boolean: return Convert.ToBoolean(value);
                default: return value;
            }
        }

        private sealed class GeometryMetadata
        {
            public string ColumnName { get; set; }
            public GeometryTypeConstant GeometryType { get; set; }
            public int Srid { get; set; }
        }
    }
}