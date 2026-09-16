using System;
using System.Collections.Generic;

namespace GIS
{
    /// <summary>
    /// 多边形几何对象，由一个外环和零至多个内环（洞）组成。
    /// 参考OGC标准：内环挖空区域不属于多边形内部。
    /// </summary>
    public class Polygon : Geometry
    {
        private Points _ExteriorRing = new Points();            //外环
        private List<Points> _Holes = new List<Points>();       //内环（洞）序列

        #region 构造函数

        public Polygon()
        {
        }

        /// <summary>
        /// 按外环构造简单多边形（无洞）
        /// </summary>
        public Polygon(Points exteriorRing)
        {
            _ExteriorRing = exteriorRing.Clone();
        }

        #endregion

        #region 属性

        /// <summary>获取几何对象类型</summary>
        public override GeometryTypeConstant GeometryType
        {
            get { return GeometryTypeConstant.Polygon; }
        }

        /// <summary>获取几何对象是否为空（外环顶点数不足3视为空）</summary>
        public override bool IsEmpty
        {
            get { return _ExteriorRing.Count < 3; }
        }

        /// <summary>获取外环顶点集合</summary>
        public Points ExteriorRing
        {
            get { return _ExteriorRing; }
        }

        /// <summary>获取内环（洞）序列，每个内环为一个Points</summary>
        public List<Points> Holes
        {
            get { return _Holes; }
        }

        /// <summary>获取多边形面积（外环面积减去全部内环面积）</summary>
        public double Area
        {
            get
            {
                if (IsEmpty)
                    return 0;
                double sArea = GeometryTools.GetPolygonArea(_ExteriorRing);
                for (Int32 i = 0; i <= _Holes.Count - 1; i++)
                {
                    sArea -= GeometryTools.GetPolygonArea(_Holes[i]);
                }
                return sArea;
            }
        }

        /// <summary>获取多边形周长（外环与全部内环长度之和）</summary>
        public double Perimeter
        {
            get
            {
                if (IsEmpty)
                    return 0;
                double sLength = new LineString(_ExteriorRing).Length;
                for (Int32 i = 0; i <= _Holes.Count - 1; i++)
                {
                    sLength += new LineString(_Holes[i]).Length;
                }
                return sLength;
            }
        }

        #endregion

        #region 方法

        /// <summary>
        /// 获取外接矩形
        /// </summary>
        public override Envelope GetEnvelope()
        {
            return _ExteriorRing.GetEnvelope();
        }

        /// <summary>
        /// 判断指定点是否位于多边形内或边界上（内环内部不算，容限不起作用）
        /// </summary>
        public override bool Contains(Coordinate point, double tolerance)
        {
            if (IsEmpty)
                return false;
            //（1）先判断外包矩形
            if (GeometryTools.IsPointWithinBox(point, GetEnvelope()) == false)
                return false;
            //（2）点必须位于外环内
            if (GeometryTools.IsPointWithinPolygon(point, _ExteriorRing) == false)
                return false;
            //（3）点不能位于任何一个内环内
            for (Int32 i = 0; i <= _Holes.Count - 1; i++)
            {
                if (GeometryTools.IsPointWithinPolygon(point, _Holes[i]))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// 判断多边形与指定范围是否相交
        /// </summary>
        public override bool Intersects(Envelope box)
        {
            return GeometryTools.IsPolygonPartiallyWithinBox(_ExteriorRing, _Holes, box);
        }

        /// <summary>
        /// 获取指定点到多边形的最短距离（点在内部或边界上时为0，只考虑外环）
        /// </summary>
        public override double Distance(Coordinate point)
        {
            if (IsEmpty)
                return double.MaxValue;
            //点位于多边形内时距离为0
            if (Contains(point) == true)
                return 0;
            //否则取到外环各线段的最短距离
            return DistanceToRing(point, _ExteriorRing);
        }

        //计算指定点到指定环（按首尾闭合处理）的最短距离
        private double DistanceToRing(Coordinate point, Points ring)
        {
            Int32 sPointCount = ring.Count;
            if (sPointCount == 0)
                return double.MaxValue;
            if (sPointCount == 1)
                return ring.GetItem(0).DistanceTo(point);
            double sMinDis = double.MaxValue;
            for (Int32 i = 0; i <= sPointCount - 2; i++)
            {
                double sDis = GeometryTools.GetDisFromPointToSegment(point, ring.GetItem(i), ring.GetItem(i + 1));
                if (sDis < sMinDis)
                    sMinDis = sDis;
            }
            //首尾连线
            double sCloseDis = GeometryTools.GetDisFromPointToSegment(point, ring.GetItem(sPointCount - 1), ring.GetItem(0));
            if (sCloseDis < sMinDis)
                sMinDis = sCloseDis;
            return sMinDis;
        }

        /// <summary>
        /// 平移多边形（外环与全部内环一起平移）
        /// </summary>
        public override void Translate(double dX, double dY)
        {
            TranslateRing(_ExteriorRing, dX, dY);
            for (Int32 i = 0; i <= _Holes.Count - 1; i++)
            {
                TranslateRing(_Holes[i], dX, dY);
            }
        }

        //平移单个环
        private void TranslateRing(Points ring, double dX, double dY)
        {
            for (Int32 i = 0; i <= ring.Count - 1; i++)
            {
                Coordinate sCoordinate = ring.GetItem(i);
                ring.SetItem(i, new Coordinate(sCoordinate.X + dX, sCoordinate.Y + dY));
            }
        }

        /// <summary>
        /// 获取WKT文本
        /// </summary>
        public override string ToWKT()
        {
            if (IsEmpty)
                return "POLYGON EMPTY";
            string sWkt = "POLYGON (";
            sWkt += "(" + GeometryWkt.FormatClosedRing(_ExteriorRing) + ")";
            for (Int32 i = 0; i <= _Holes.Count - 1; i++)
            {
                sWkt += ", (" + GeometryWkt.FormatClosedRing(_Holes[i]) + ")";
            }
            sWkt += ")";
            return sWkt;
        }

        /// <summary>
        /// 克隆多边形（深复制）
        /// </summary>
        public override Geometry Clone()
        {
            Polygon sPolygon = new Polygon(_ExteriorRing);
            for (Int32 i = 0; i <= _Holes.Count - 1; i++)
            {
                sPolygon._Holes.Add(_Holes[i].Clone());
            }
            return sPolygon;
        }

        #endregion
    }
}
