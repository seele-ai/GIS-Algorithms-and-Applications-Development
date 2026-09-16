using System;
using System.Collections.Generic;

namespace GIS
{
    /// <summary>
    /// 折线集合
    /// </summary>
    public class LineStrings : IEnumerable<LineString>
    {
        private List<LineString> _Items = new List<LineString>();

        /// <summary>折线个数</summary>
        public Int32 Count
        {
            get { return _Items.Count; }
        }

        /// <summary>按下标访问折线</summary>
        public LineString this[Int32 index]
        {
            get { return _Items[index]; }
            set { _Items[index] = value; }
        }

        /// <summary>
        /// 获取指定下标的折线
        /// </summary>
        public LineString GetItem(Int32 index)
        {
            return _Items[index];
        }

        /// <summary>
        /// 追加一条折线
        /// </summary>
        public void Add(LineString lineString)
        {
            _Items.Add(lineString);
        }

        /// <summary>
        /// 删除指定下标的折线
        /// </summary>
        public void RemoveAt(Int32 index)
        {
            _Items.RemoveAt(index);
        }

        /// <summary>
        /// 清空所有折线
        /// </summary>
        public void Clear()
        {
            _Items.Clear();
        }

        public IEnumerator<LineString> GetEnumerator()
        {
            return _Items.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return _Items.GetEnumerator();
        }
    }

    /// <summary>
    /// 复合折线几何对象（若干条折线的集合）
    /// </summary>
    public class MultiLineString : Geometry
    {
        private LineStrings _Parts = new LineStrings();     //组成复合折线的各条折线

        #region 构造函数

        public MultiLineString()
        {
        }

        #endregion

        #region 属性

        /// <summary>获取几何对象类型</summary>
        public override GeometryTypeConstant GeometryType
        {
            get { return GeometryTypeConstant.MultiLineString; }
        }

        /// <summary>获取几何对象是否为空</summary>
        public override bool IsEmpty
        {
            get { return _Parts.Count == 0; }
        }

        /// <summary>获取组成复合折线的折线集合</summary>
        public LineStrings Parts
        {
            get { return _Parts; }
        }

        /// <summary>获取复合折线总长度</summary>
        public double Length
        {
            get
            {
                double sLength = 0;
                for (Int32 i = 0; i <= _Parts.Count - 1; i++)
                {
                    sLength += _Parts.GetItem(i).Length;
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
            Envelope sEnvelope = new Envelope();
            for (Int32 i = 0; i <= _Parts.Count - 1; i++)
            {
                sEnvelope.ExpandToInclude(_Parts.GetItem(i).GetEnvelope());
            }
            return sEnvelope;
        }

        /// <summary>
        /// 判断在指定容限下，指定点是否位于复合折线上
        /// </summary>
        public override bool Contains(Coordinate point, double tolerance)
        {
            for (Int32 i = 0; i <= _Parts.Count - 1; i++)
            {
                if (_Parts.GetItem(i).Contains(point, tolerance) == true)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 判断复合折线与指定范围是否相交
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
        /// 获取指定点到复合折线的最短距离
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
        /// 平移复合折线
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
                return "MULTILINESTRING EMPTY";
            string sWkt = "MULTILINESTRING (";
            for (Int32 i = 0; i <= _Parts.Count - 1; i++)
            {
                if (i > 0)
                    sWkt += ", ";
                sWkt += "(" + GeometryWkt.FormatCoordinateSequence(_Parts.GetItem(i).Points) + ")";
            }
            sWkt += ")";
            return sWkt;
        }

        /// <summary>
        /// 克隆复合折线（深复制）
        /// </summary>
        public override Geometry Clone()
        {
            MultiLineString sMultiLineString = new MultiLineString();
            for (Int32 i = 0; i <= _Parts.Count - 1; i++)
            {
                sMultiLineString._Parts.Add((LineString)_Parts.GetItem(i).Clone());
            }
            return sMultiLineString;
        }

        #endregion
    }
}
