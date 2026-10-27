using System;

namespace GIS.PostGIS
{
    /// <summary>PostGIS 图层的稳定数据来源；图层改名不会改变此信息。</summary>
    public sealed class PostGISLayerSource
    {
        public PostGISLayerSource(string schemaName, string tableName,
            string geometryColumnName, string keyColumnName, int srid)
        {
            SchemaName = RequireName(schemaName, nameof(schemaName));
            TableName = RequireName(tableName, nameof(tableName));
            GeometryColumnName = RequireName(geometryColumnName, nameof(geometryColumnName));
            KeyColumnName = string.IsNullOrWhiteSpace(keyColumnName) ? null : keyColumnName;
            if (srid < 0)
                throw new ArgumentOutOfRangeException(nameof(srid), "SRID 不能小于 0。");
            Srid = srid;
        }

        public string SchemaName { get; }
        public string TableName { get; }
        public string GeometryColumnName { get; }
        public string KeyColumnName { get; }
        public bool HasKey { get { return !string.IsNullOrWhiteSpace(KeyColumnName); } }
        public int Srid { get; }
        public string QualifiedName { get { return SchemaName + "." + TableName; } }

        private static string RequireName(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("数据库对象名称不能为空。", parameterName);
            return value;
        }
    }
}
