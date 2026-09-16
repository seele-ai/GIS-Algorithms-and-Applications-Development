namespace GIS
{
    /// <summary>
    /// 点几何对象
    /// </summary>
    public class Point : Geometry
    {
        private Coordinate _Coordinate;     //点的坐标

        #region 构造函数

        public Point()
        {
            _Coordinate = new Coordinate(0, 0);
        }

        public Point(double x, double y)
        {
            _Coordinate = new Coordinate(x, y);
        }

        public Point(Coordinate coordinate)
        {
            _Coordinate = coordinate;
        }

        #endregion

        #region 属性

        /// <summary>获取几何对象类型</summary>
        public override GeometryTypeConstant GeometryType
        {
            get { return GeometryTypeConstant.Point; }
        }

        /// <summary>获取几何对象是否为空</summary>
        public override bool IsEmpty
        {
            get { return false; }
        }

        /// <summary>获取或设置点的X坐标</summary>
        public double X
        {
            get { return _Coordinate.X; }
            set { _Coordinate.X = value; }
        }

        /// <summary>获取或设置点的Y坐标</summary>
        public double Y
        {
            get { return _Coordinate.Y; }
            set { _Coordinate.Y = value; }
        }

        /// <summary>获取或设置点的坐标</summary>
        public Coordinate Coordinate
        {
            get { return _Coordinate; }
            set { _Coordinate = value; }
        }

        #endregion

        #region 方法

        /// <summary>
        /// 获取外接矩形（退化为一点的范围）
        /// </summary>
        public override Envelope GetEnvelope()
        {
            return new Envelope(_Coordinate.X, _Coordinate.X, _Coordinate.Y, _Coordinate.Y);
        }

        /// <summary>
        /// 判断在指定容限下，指定点是否位于本点上
        /// </summary>
        public override bool Contains(Coordinate point, double tolerance)
        {
            return GeometryTools.IsPointOnPoint(point, _Coordinate, tolerance);
        }

        /// <summary>
        /// 判断点对象与指定范围是否相交
        /// </summary>
        public override bool Intersects(Envelope box)
        {
            return box.Contains(_Coordinate);
        }

        /// <summary>
        /// 获取指定点到本点的距离
        /// </summary>
        public override double Distance(Coordinate point)
        {
            return _Coordinate.DistanceTo(point);
        }

        /// <summary>
        /// 平移点对象
        /// </summary>
        public override void Translate(double dX, double dY)
        {
            _Coordinate = new Coordinate(_Coordinate.X + dX, _Coordinate.Y + dY);
        }

        /// <summary>
        /// 获取WKT文本
        /// </summary>
        public override string ToWKT()
        {
            if (IsEmpty)
                return "POINT EMPTY";
            return "POINT (" + GeometryWkt.FormatCoordinate(_Coordinate) + ")";
        }

        /// <summary>
        /// 克隆点对象
        /// </summary>
        public override Geometry Clone()
        {
            return new Point(_Coordinate);
        }

        #endregion
    }
}
