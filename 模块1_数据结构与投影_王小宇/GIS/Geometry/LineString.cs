using System;

namespace GIS
{
    /// <summary>
    /// 折线几何对象（一系列有序坐标点依次连接而成）
    /// </summary>
    public class LineString : Geometry
    {
        private Points _Points = new Points();      //折线的顶点序列

        #region 构造函数

        public LineString()
        {
        }

        public LineString(Points points)
        {
            _Points = points.Clone();
        }

        #endregion

        #region 属性

        /// <summary>获取几何对象类型</summary>
        public override GeometryTypeConstant GeometryType
        {
            get { return GeometryTypeConstant.LineString; }
        }

        /// <summary>获取几何对象是否为空</summary>
        public override bool IsEmpty
        {
            get { return _Points.Count == 0; }
        }

        /// <summary>获取折线顶点集合</summary>
        public Points Points
        {
            get { return _Points; }
        }

        /// <summary>获取折线长度（各线段长度之和）</summary>
        public double Length
        {
            get
            {
                double sLength = 0;
                for (Int32 i = 0; i <= _Points.Count - 2; i++)
                {
                    sLength += _Points.GetItem(i).DistanceTo(_Points.GetItem(i + 1));
                }
                return sLength;
            }
        }

        /// <summary>获取折线是否闭合（首尾顶点重合且顶点数不少于4）</summary>
        public bool IsClosed
        {
            get
            {
                if (_Points.Count < 4)
                    return false;
                return _Points.GetItem(0) == _Points.GetItem(_Points.Count - 1);
            }
        }

        #endregion

        #region 方法

        /// <summary>
        /// 获取外接矩形
        /// </summary>
        public override Envelope GetEnvelope()
        {
            return _Points.GetEnvelope();
        }

        /// <summary>
        /// 判断在指定容限（地图单位）下，指定点是否位于折线上
        /// </summary>
        public override bool Contains(Coordinate point, double tolerance)
        {
            return GeometryTools.IsPointOnPolyline(point, _Points, tolerance);
        }

        /// <summary>
        /// 判断折线与指定范围是否相交（顶点落入范围或线段穿越范围均算相交）
        /// </summary>
        public override bool Intersects(Envelope box)
        {
            return GeometryTools.IsPolylinePartiallyWithinBox(_Points, box);
        }

        /// <summary>
        /// 获取指定点到折线的最短距离
        /// </summary>
        public override double Distance(Coordinate point)
        {
            if (_Points.Count == 0)
                return double.MaxValue;
            //单顶点时退化为点到点的距离
            if (_Points.Count == 1)
                return _Points.GetItem(0).DistanceTo(point);
            double sMinDis = double.MaxValue;
            for (Int32 i = 0; i <= _Points.Count - 2; i++)
            {
                double sDis = GeometryTools.GetDisFromPointToSegment(point, _Points.GetItem(i), _Points.GetItem(i + 1));
                if (sDis < sMinDis)
                    sMinDis = sDis;
            }
            return sMinDis;
        }

        /// <summary>
        /// 获取折线的中点（沿弧长中点），可作注记定位点
        /// </summary>
        public Coordinate GetMidPoint()
        {
            return GeometryTools.GetMidPointOfPolyline(_Points);
        }

        /// <summary>
        /// 平移折线
        /// </summary>
        public override void Translate(double dX, double dY)
        {
            for (Int32 i = 0; i <= _Points.Count - 1; i++)
            {
                Coordinate sCoordinate = _Points.GetItem(i);
                _Points.SetItem(i, new Coordinate(sCoordinate.X + dX, sCoordinate.Y + dY));
            }
        }

        /// <summary>
        /// 获取WKT文本
        /// </summary>
        public override string ToWKT()
        {
            if (IsEmpty)
                return "LINESTRING EMPTY";
            return "LINESTRING (" + GeometryWkt.FormatCoordinateSequence(_Points) + ")";
        }

        /// <summary>
        /// 克隆折线（深复制）
        /// </summary>
        public override Geometry Clone()
        {
            return new LineString(_Points);
        }

        #endregion
    }
}
