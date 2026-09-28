namespace GIS
{
    /// <summary>
    /// 坐标转换器。绑定一个目标投影坐标系统，提供经纬度与投影坐标间的
    /// Project/Unproject转换（即模块内置函数Project(Coordinate)/Unproject(Coordinate)）。
    /// </summary>
    public class CoordinateTransform
    {
        private ProjectionCS _Projection;       //绑定的投影坐标系统，null表示经纬度坐标（不做转换）

        #region 构造函数

        /// <summary>
        /// 按目标投影构造坐标转换器
        /// </summary>
        /// <param name="projection">投影坐标系统，null表示经纬度坐标</param>
        public CoordinateTransform(ProjectionCS projection)
        {
            _Projection = projection;
        }

        #endregion

        #region 属性

        /// <summary>获取绑定的投影坐标系统（null表示经纬度坐标）</summary>
        public ProjectionCS Projection
        {
            get { return _Projection; }
        }

        #endregion

        #region 方法

        /// <summary>
        /// 将经纬度坐标投影为投影坐标（未绑定投影时原样返回）
        /// </summary>
        public Coordinate Project(Coordinate lngLat)
        {
            if (_Projection == null)
                return lngLat;
            return _Projection.TransferToProjCo(lngLat);
        }

        /// <summary>
        /// 将投影坐标反投影为经纬度坐标（未绑定投影时原样返回）
        /// </summary>
        public Coordinate Unproject(Coordinate projCo)
        {
            if (_Projection == null)
                return projCo;
            return _Projection.TransferToLngLat(projCo);
        }

        /// <summary>
        /// 在任意两个坐标系统之间转换坐标（任一投影为null表示经纬度坐标）
        /// </summary>
        public static Coordinate Transform(Coordinate coordinate, ProjectionCS fromProjection, ProjectionCS toProjection)
        {
            Coordinate sLngLat;
            if (fromProjection == null)
            {
                sLngLat = coordinate;
            }
            else
            {
                sLngLat = fromProjection.TransferToLngLat(coordinate);
            }
            if (toProjection == null)
            {
                return sLngLat;
            }
            return toProjection.TransferToProjCo(sLngLat);
        }

        #endregion
    }
}
