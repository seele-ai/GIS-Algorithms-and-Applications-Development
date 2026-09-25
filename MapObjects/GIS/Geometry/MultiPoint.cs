using System;

namespace GIS
{
    /// <summary>
    /// 多点几何对象（若干个点的集合）
    /// </summary>
    public class MultiPoint : Geometry
    {
        private Points _Points = new Points();      //组成多点的坐标点

        #region 构造函数

        public MultiPoint()
        {
        }

        public MultiPoint(Points points)
        {
            _Points = points.Clone();
        }

        #endregion

        #region 属性

        /// <summary>获取几何对象类型</summary>
        public override GeometryTypeConstant GeometryType
        {
            get { return GeometryTypeConstant.MultiPoint; }
        }

        /// <summary>获取几何对象是否为空</summary>
        public override bool IsEmpty
        {
            get { return _Points.Count == 0; }
        }

        /// <summary>获取组成多点的坐标点集合</summary>
        public Points Points
        {
            get { return _Points; }
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
        /// 判断在指定容限下，指定点是否位于某个组成点上
        /// </summary>
        public override bool Contains(Coordinate point, double tolerance)
        {
            for (Int32 i = 0; i <= _Points.Count - 1; i++)
            {
                if (GeometryTools.IsPointOnPoint(point, _Points.GetItem(i), tolerance) == true)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 判断多点与指定范围是否相交
        /// </summary>
        public override bool Intersects(Envelope box)
        {
            if (GetEnvelope().IntersectsWith(box) == false)
                return false;
            for (Int32 i = 0; i <= _Points.Count - 1; i++)
            {
                if (box.Contains(_Points.GetItem(i)) == true)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 获取指定点到多点的最短距离
        /// </summary>
        public override double Distance(Coordinate point)
        {
            double sMinDis = double.MaxValue;
            for (Int32 i = 0; i <= _Points.Count - 1; i++)
            {
                double sDis = _Points.GetItem(i).DistanceTo(point);
                if (sDis < sMinDis)
                    sMinDis = sDis;
            }
            return sMinDis;
        }

        /// <summary>
        /// 平移多点
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
                return "MULTIPOINT EMPTY";
            string sWkt = "MULTIPOINT (";
            for (Int32 i = 0; i <= _Points.Count - 1; i++)
            {
                if (i > 0)
                    sWkt += ", ";
                sWkt += "(" + GeometryWkt.FormatCoordinate(_Points.GetItem(i)) + ")";
            }
            sWkt += ")";
            return sWkt;
        }

        /// <summary>
        /// 克隆多点（深复制）
        /// </summary>
        public override Geometry Clone()
        {
            return new MultiPoint(_Points);
        }

        #endregion
    }
}
