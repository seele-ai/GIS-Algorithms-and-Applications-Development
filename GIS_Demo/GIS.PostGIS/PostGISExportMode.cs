namespace GIS.PostGIS
{
    /// <summary>将图层写入 PostGIS 时对目标表的处理方式。</summary>
    public enum PostGISExportMode
    {
        CreateNew,
        Overwrite,
        Append
    }
}
