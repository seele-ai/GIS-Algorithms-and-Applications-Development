namespace GIS.PostGIS
{
    /// <summary>新建 PostGIS 空间表时使用的系统列名。</summary>
    public sealed class PostGISColumnNames
    {
        public PostGISColumnNames(string idColumnName, string geometryColumnName)
        {
            IdColumnName = idColumnName;
            GeometryColumnName = geometryColumnName;
        }

        public string IdColumnName { get; }
        public string GeometryColumnName { get; }
    }
}
