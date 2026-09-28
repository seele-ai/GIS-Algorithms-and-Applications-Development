using System;

namespace GIS
{
    /// <summary>
    /// 外接矩形（范围盒）。(MinX, MinY)为左下角，(MaxX, MaxY)为右上角。
    /// </summary>
    public class Envelope
    {
        /// <summary>最小X坐标（西边界）</summary>
        public double MinX;

        /// <summary>最大X坐标（东边界）</summary>
        public double MaxX;

        /// <summary>最小Y坐标（南边界）</summary>
        public double MinY;

        /// <summary>最大Y坐标（北边界）</summary>
        public double MaxY;

        #region 构造函数

        /// <summary>
        /// 构造一个空范围（MinX大于MaxX）
        /// </summary>
        public Envelope()
        {
            MinX = double.MaxValue;
            MaxX = double.MinValue;
            MinY = double.MaxValue;
            MaxY = double.MinValue;
        }

        /// <summary>
        /// 按四个边界值构造范围
        /// </summary>
        public Envelope(double minX, double maxX, double minY, double maxY)
        {
            MinX = minX;
            MaxX = maxX;
            MinY = minY;
            MaxY = maxY;
        }

        /// <summary>
        /// 按两个角点构造范围（自动判断相对位置）
        /// </summary>
        public Envelope(Coordinate point1, Coordinate point2)
        {
            if (point1.X <= point2.X)
            {
                MinX = point1.X; MaxX = point2.X;
            }
            else
            {
                MinX = point2.X; MaxX = point1.X;
            }
            if (point1.Y <= point2.Y)
            {
                MinY = point1.Y; MaxY = point2.Y;
            }
            else
            {
                MinY = point2.Y; MaxY = point1.Y;
            }
        }

        #endregion

        #region 属性

        /// <summary>宽度（MaxX-MinX）</summary>
        public double Width
        {
            get { return MaxX - MinX; }
        }

        /// <summary>高度（MaxY-MinY）</summary>
        public double Height
        {
            get { return MaxY - MinY; }
        }

        /// <summary>中心点X坐标</summary>
        public double CenterX
        {
            get { return (MinX + MaxX) / 2; }
        }

        /// <summary>中心点Y坐标</summary>
        public double CenterY
        {
            get { return (MinY + MaxY) / 2; }
        }

        /// <summary>获取范围中心点坐标</summary>
        public Coordinate Center
        {
            get { return new Coordinate(CenterX, CenterY); }
        }

        /// <summary>是否为空范围（未包含任何有效坐标）</summary>
        public bool IsNull
        {
            get { return MinX > MaxX || MinY > MaxY; }
        }

        #endregion

        #region 方法

        /// <summary>
        /// 判断指定点是否位于范围内（含边界）
        /// </summary>
        public bool Contains(Coordinate point)
        {
            return point.X >= MinX && point.X <= MaxX && point.Y >= MinY && point.Y <= MaxY;
        }

        /// <summary>
        /// 判断本范围与另一范围是否相交（含边界接触）
        /// </summary>
        public bool IntersectsWith(Envelope other)
        {
            if (IsNull || other.IsNull)
                return false;
            return GeometryTools.AreBoxesCross(this, other);
        }

        /// <summary>
        /// 获取本范围与另一范围的交集范围，不相交时返回null
        /// </summary>
        public Envelope Intersection(Envelope other)
        {
            if (IntersectsWith(other) == false)
                return null;
            return new Envelope(Math.Max(MinX, other.MinX), Math.Min(MaxX, other.MaxX),
                Math.Max(MinY, other.MinY), Math.Min(MaxY, other.MaxY));
        }

        /// <summary>
        /// 将指定点扩充进本范围
        /// </summary>
        public void ExpandToInclude(Coordinate point)
        {
            if (point.X < MinX) MinX = point.X;
            if (point.X > MaxX) MaxX = point.X;
            if (point.Y < MinY) MinY = point.Y;
            if (point.Y > MaxY) MaxY = point.Y;
        }

        /// <summary>
        /// 将另一范围扩充进本范围
        /// </summary>
        public void ExpandToInclude(Envelope other)
        {
            if (other.IsNull)
                return;
            if (IsNull)
            {
                MinX = other.MinX; MaxX = other.MaxX;
                MinY = other.MinY; MaxY = other.MaxY;
                return;
            }
            if (other.MinX < MinX) MinX = other.MinX;
            if (other.MaxX > MaxX) MaxX = other.MaxX;
            if (other.MinY < MinY) MinY = other.MinY;
            if (other.MaxY > MaxY) MaxY = other.MaxY;
        }

        /// <summary>
        /// 获取四周向外膨胀指定量的新范围
        /// </summary>
        public Envelope Buffer(double amount)
        {
            return new Envelope(MinX - amount, MaxX + amount, MinY - amount, MaxY + amount);
        }

        /// <summary>
        /// 克隆本范围
        /// </summary>
        public Envelope Clone()
        {
            return new Envelope(MinX, MaxX, MinY, MaxY);
        }

        #endregion

        #region Object成员

        public override string ToString()
        {
            return string.Format("Envelope({0}, {1}, {2}, {3})", MinX, MaxX, MinY, MaxY);
        }

        #endregion
    }
}
