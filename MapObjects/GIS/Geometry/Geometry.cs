namespace GIS
{
    /// <summary>
    /// 几何对象抽象基类（参考OGC简单要素标准设计）。
    /// 具体类型：Point / LineString / Polygon / MultiPoint / MultiLineString / MultiPolygon。
    /// </summary>
    public abstract class Geometry
    {
        #region 属性

        /// <summary>获取几何对象类型</summary>
        public abstract GeometryTypeConstant GeometryType { get; }

        /// <summary>获取几何对象是否为空</summary>
        public abstract bool IsEmpty { get; }

        #endregion

        #region 方法

        /// <summary>
        /// 获取几何对象的外接矩形
        /// </summary>
        public abstract Envelope GetEnvelope();

        /// <summary>
        /// 判断在指定容限（地图单位）下，指定点是否位于几何对象上（面为内部或边界上）
        /// </summary>
        public abstract bool Contains(Coordinate point, double tolerance);

        /// <summary>
        /// 判断指定点是否位于几何对象上（面为内部或边界上），容限为0
        /// </summary>
        public bool Contains(Coordinate point)
        {
            return Contains(point, 0);
        }

        /// <summary>
        /// 判断几何对象与指定范围是否相交（相交或被包含均算相交）
        /// </summary>
        public abstract bool Intersects(Envelope box);

        /// <summary>
        /// 获取指定点到几何对象的最短距离（点在几何对象内部或边界上时为0）
        /// </summary>
        public abstract double Distance(Coordinate point);

        /// <summary>
        /// 将几何对象平移指定的偏移量（地图单位），供数据编辑模块调用
        /// </summary>
        public abstract void Translate(double dX, double dY);

        /// <summary>
        /// 获取几何对象OGC标准的WKT文本
        /// </summary>
        public abstract string ToWKT();

        /// <summary>
        /// 克隆几何对象（深复制）
        /// </summary>
        public abstract Geometry Clone();

        /// <summary>
        /// 从OGC标准的WKT文本解析几何对象
        /// </summary>
        public static Geometry FromWKT(string wkt)
        {
            return GeometryWkt.FromWKT(wkt);
        }

        #endregion
    }
}
