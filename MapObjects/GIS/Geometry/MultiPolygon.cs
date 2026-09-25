using System;
using System.Collections.Generic;

namespace GIS
{
    /// <summary>
    /// 多边形集合
    /// </summary>
    public class Polygons : IEnumerable<Polygon>
    {
        private List<Polygon> _Items = new List<Polygon>();

        /// <summary>多边形个数</summary>
        public Int32 Count
        {
            get { return _Items.Count; }
        }

        /// <summary>按下标访问多边形</summary>
        public Polygon this[Int32 index]
        {
            get { return _Items[index]; }
            set { _Items[index] = value; }
        }

        /// <summary>
        /// 获取指定下标的多边形
        /// </summary>
        public Polygon GetItem(Int32 index)
        {
            return _Items[index];
        }

        /// <summary>
        /// 追加一个多边形
        /// </summary>
        public void Add(Polygon polygon)
        {
            _Items.Add(polygon);
        }

        /// <summary>
        /// 删除指定下标的多边形
        /// </summary>
        public void RemoveAt(Int32 index)
        {
            _Items.RemoveAt(index);
        }

        /// <summary>
        /// 清空所有多边形
        /// </summary>
        public void Clear()
        {
            _Items.Clear();
        }

        public IEnumerator<Polygon> GetEnumerator()
        {
            return _Items.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return _Items.GetEnumerator();
        }
    }

    /// <summary>
    /// 复合多边形几何对象（若干个多边形的集合）
    /// </summary>
    public class MultiPolygon : Geometry
    {
        private Polygons _Parts = new Polygons();       //组成复合多边形的各个多边形

        #region 构造函数

        public MultiPolygon()
        {
        }

        #endregion

        #region 属性

        /// <summary>获取几何对象类型</summary>
        public override GeometryTypeConstant GeometryType
        {
            get { return GeometryTypeConstant.MultiPolygon; }
        }

        /// <summary>获取几何对象是否为空</summary>
        public override bool IsEmpty
        {
            get { return _Parts.Count == 0; }
        }

        /// <summary>获取组成复合多边形的多边形集合</summary>
        public Polygons Parts
        {
            get { return _Parts; }
        }

        /// <summary>获取复合多边形总面积</summary>
        public double Area
        {
            get
            {
                double sArea = 0;
                for (Int32 i = 0; i <= _Parts.Count - 1; i++)
                {
                    sArea += _Parts.GetItem(i).Area;
                }
                return sArea;
            }
        }

        #endregion

        #region 方法

        /// <summary>
        /// 获取外接矩形
        /// </summary>
        public override Envelope GetEnvelope()
        {
            Envelope sEnvelope = new Envelope();
            for (Int32 i = 0; i <= _Parts.Count - 1; i++)
            {
                sEnvelope.ExpandToInclude(_Parts.GetItem(i).GetEnvelope());
            }
            return sEnvelope;
        }

        /// <summary>
        /// 判断指定点是否位于某个组成多边形内或边界上
        /// </summary>
        public override bool Contains(Coordinate point, double tolerance)
        {
            for (Int32 i = 0; i <= _Parts.Count - 1; i++)
            {
                if (_Parts.GetItem(i).Contains(point) == true)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 判断复合多边形与指定范围是否相交
        /// </summary>
        public override bool Intersects(Envelope box)
        {
            if (GetEnvelope().IntersectsWith(box) == false)
                return false;
            for (Int32 i = 0; i <= _Parts.Count - 1; i++)
            {
                if (_Parts.GetItem(i).Intersects(box) == true)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 获取指定点到复合多边形的最短距离
        /// </summary>
        public override double Distance(Coordinate point)
        {
            double sMinDis = double.MaxValue;
            for (Int32 i = 0; i <= _Parts.Count - 1; i++)
            {
                double sDis = _Parts.GetItem(i).Distance(point);
                if (sDis < sMinDis)
                    sMinDis = sDis;
            }
            return sMinDis;
        }

        /// <summary>
        /// 平移复合多边形
        /// </summary>
        public override void Translate(double dX, double dY)
        {
            for (Int32 i = 0; i <= _Parts.Count - 1; i++)
            {
                _Parts.GetItem(i).Translate(dX, dY);
            }
        }

        /// <summary>
        /// 获取WKT文本
        /// </summary>
        public override string ToWKT()
        {
            if (IsEmpty)
                return "MULTIPOLYGON EMPTY";
            //借助组成多边形的WKT文本，去掉"POLYGON"关键字并拼接
            string sWkt = "MULTIPOLYGON (";
            for (Int32 i = 0; i <= _Parts.Count - 1; i++)
            {
                if (i > 0)
                    sWkt += ", ";
                string sPartWkt = _Parts.GetItem(i).ToWKT();
                sWkt += sPartWkt.Substring(sPartWkt.IndexOf('('));
            }
            sWkt += ")";
            return sWkt;
        }

        /// <summary>
        /// 克隆复合多边形（深复制）
        /// </summary>
        public override Geometry Clone()
        {
            MultiPolygon sMultiPolygon = new MultiPolygon();
            for (Int32 i = 0; i <= _Parts.Count - 1; i++)
            {
                sMultiPolygon._Parts.Add((Polygon)_Parts.GetItem(i).Clone());
            }
            return sMultiPolygon;
        }

        #endregion
    }
}
