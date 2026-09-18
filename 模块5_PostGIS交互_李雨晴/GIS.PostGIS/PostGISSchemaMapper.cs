using System;
using System.Collections.Generic;
using System.Text;

namespace GIS.PostGIS
{
    /// <summary>
    /// 负责 GIS 数据结构与 PostgreSQL/PostGIS Schema 之间的映射。
    /// </summary>
    public static class PostGISSchemaMapper
    {
        public const string DefaultSchemaName = "public";
        public const string DefaultIdColumnName = "id";
        public const string DefaultGeometryColumnName = "geom";

        /// <summary>
        /// 将 GIS 字段类型映射为 PostgreSQL 字段类型。
        /// </summary>
        public static string GetPostgreSqlType(Field field)
        {
            if (field == null)
                throw new ArgumentNullException(nameof(field));

            switch (field.ValueType)
            {
                case FieldTypeConstant.Byte:
                    return "SMALLINT";
                case FieldTypeConstant.Int16:
                    return "SMALLINT";
                case FieldTypeConstant.Int32:
                    return "INTEGER";
                case FieldTypeConstant.Single:
                    return "REAL";
                case FieldTypeConstant.Double:
                    return "DOUBLE PRECISION";
                case FieldTypeConstant.Text:
                    return "TEXT";
                case FieldTypeConstant.Date:
                    return "TIMESTAMP WITHOUT TIME ZONE";
                case FieldTypeConstant.Boolean:
                    return "BOOLEAN";
                default:
                    throw new NotSupportedException("不支持的字段类型：" + field.ValueType);
            }
        }

        /// <summary>
        /// 将 GIS 几何类型映射为 PostGIS Geometry 类型名称。
        /// </summary>
        public static string GetPostGisGeometryType(GeometryTypeConstant geometryType)
        {
            switch (geometryType)
            {
                case GeometryTypeConstant.Point:
                    return "POINT";
                case GeometryTypeConstant.LineString:
                    return "LINESTRING";
                case GeometryTypeConstant.Polygon:
                    return "POLYGON";
                case GeometryTypeConstant.MultiPoint:
                    return "MULTIPOINT";
                case GeometryTypeConstant.MultiLineString:
                    return "MULTILINESTRING";
                case GeometryTypeConstant.MultiPolygon:
                    return "MULTIPOLYGON";
                default:
                    throw new NotSupportedException("不支持的几何类型：" + geometryType);
            }
        }

        /// <summary>
        /// 安全引用 PostgreSQL 标识符，可处理中文、空格、保留字和双引号。
        /// </summary>
        public static string QuoteIdentifier(string identifier)
        {
            ValidateIdentifier(identifier, nameof(identifier));
            return "\"" + identifier.Replace("\"", "\"\"") + "\"";
        }

        /// <summary>
        /// 根据 FeatureClass 生成空间表创建语句。属性列允许 NULL。
        /// </summary>
        public static string BuildCreateTableSql(
            FeatureClass featureClass,
            string tableName,
            int srid,
            string schemaName = DefaultSchemaName,
            string idColumnName = DefaultIdColumnName,
            string geometryColumnName = DefaultGeometryColumnName)
        {
            if (featureClass == null)
                throw new ArgumentNullException(nameof(featureClass));
            if (srid < 0)
                throw new ArgumentOutOfRangeException(nameof(srid), "SRID 不能为负数；未知坐标系请使用 0。");

            ValidateIdentifier(schemaName, nameof(schemaName));
            ValidateIdentifier(tableName, nameof(tableName));
            ValidateIdentifier(idColumnName, nameof(idColumnName));
            ValidateIdentifier(geometryColumnName, nameof(geometryColumnName));
            ValidateFields(featureClass.Fields, idColumnName, geometryColumnName);

            StringBuilder sql = new StringBuilder();
            sql.Append("CREATE TABLE ")
               .Append(GetQualifiedTableName(schemaName, tableName))
               .AppendLine(" (");
            sql.Append("    ")
               .Append(QuoteIdentifier(idColumnName))
               .AppendLine(" BIGSERIAL PRIMARY KEY,");

            for (int i = 0; i < featureClass.Fields.Count; i++)
            {
                Field field = featureClass.Fields.GetItem(i);
                sql.Append("    ")
                   .Append(QuoteIdentifier(field.Name))
                   .Append(' ')
                   .Append(GetPostgreSqlType(field))
                   .AppendLine(",");
            }

            sql.Append("    ")
               .Append(QuoteIdentifier(geometryColumnName))
               .Append(" geometry(")
               .Append(GetPostGisGeometryType(featureClass.GeometryType))
               .Append(", ")
               .Append(srid)
               .AppendLine(")");
            sql.Append(");");
            return sql.ToString();
        }

        /// <summary>
        /// 生成 Geometry 列的 GiST 空间索引创建语句。
        /// </summary>
        public static string BuildCreateSpatialIndexSql(
            string tableName,
            string schemaName = DefaultSchemaName,
            string geometryColumnName = DefaultGeometryColumnName)
        {
            ValidateIdentifier(schemaName, nameof(schemaName));
            ValidateIdentifier(tableName, nameof(tableName));
            ValidateIdentifier(geometryColumnName, nameof(geometryColumnName));

            string indexName = "idx_" + tableName + "_" + geometryColumnName;
            return "CREATE INDEX " + QuoteIdentifier(indexName) +
                   " ON " + GetQualifiedTableName(schemaName, tableName) +
                   " USING GIST (" + QuoteIdentifier(geometryColumnName) + ");";
        }

        /// <summary>
        /// 返回经过安全引用的 schema.table 名称。
        /// </summary>
        public static string GetQualifiedTableName(string schemaName, string tableName)
        {
            return QuoteIdentifier(schemaName) + "." + QuoteIdentifier(tableName);
        }

        private static void ValidateFields(Fields fields, string idColumnName, string geometryColumnName)
        {
            if (fields == null)
                throw new ArgumentNullException(nameof(fields));

            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            names.Add(idColumnName);
            names.Add(geometryColumnName);

            for (int i = 0; i < fields.Count; i++)
            {
                Field field = fields.GetItem(i);
                if (field == null)
                    throw new ArgumentException("Fields 中不能包含 null 字段。", nameof(fields));

                ValidateIdentifier(field.Name, "field.Name");
                if (!names.Add(field.Name))
                    throw new ArgumentException("字段名称重复或与系统字段冲突：" + field.Name, nameof(fields));
            }
        }

        private static void ValidateIdentifier(string identifier, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(identifier))
                throw new ArgumentException("数据库标识符不能为空。", parameterName);
            if (identifier.IndexOf('\0') >= 0)
                throw new ArgumentException("数据库标识符不能包含空字符。", parameterName);
        }
    }
}