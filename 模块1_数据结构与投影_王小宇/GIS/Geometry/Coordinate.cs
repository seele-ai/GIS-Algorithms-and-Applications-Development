using System;

namespace GIS
{
    /// <summary>
    /// 地图坐标。X为东方向（投影坐标的东西分量或经度），Y为北方向（投影坐标的南北分量或纬度）。
    /// </summary>
    public struct Coordinate
    {
        /// <summary>东方向坐标（经度或投影坐标X）</summary>
        public double X;

        /// <summary>北方向坐标（纬度或投影坐标Y）</summary>
        public double Y;

        #region 构造函数

        public Coordinate(double x, double y)
        {
            X = x;
            Y = y;
        }

        #endregion

        #region 属性

        /// <summary>坐标原点</summary>
        public static Coordinate Zero
        {
            get { return new Coordinate(0, 0); }
        }

        #endregion

        #region 方法

        /// <summary>
        /// 获取与另一点的直线距离
        /// </summary>
        public double DistanceTo(Coordinate other)
        {
            double dX = X - other.X, dY = Y - other.Y;
            return Math.Sqrt(dX * dX + dY * dY);
        }

        #endregion

        #region 运算符

        public static Coordinate operator +(Coordinate a, Coordinate b) { return new Coordinate(a.X + b.X, a.Y + b.Y); }
        public static Coordinate operator -(Coordinate a, Coordinate b) { return new Coordinate(a.X - b.X, a.Y - b.Y); }
        public static Coordinate operator *(Coordinate a, double k) { return new Coordinate(a.X * k, a.Y * k); }
        public static bool operator ==(Coordinate a, Coordinate b) { return a.X == b.X && a.Y == b.Y; }
        public static bool operator !=(Coordinate a, Coordinate b) { return !(a == b); }

        #endregion

        #region Object成员

        public override bool Equals(object obj)
        {
            if (obj is Coordinate)
                return this == (Coordinate)obj;
            return false;
        }

        public override int GetHashCode()
        {
            return X.GetHashCode() ^ Y.GetHashCode();
        }

        public override string ToString()
        {
            return string.Format("({0}, {1})", X, Y);
        }

        #endregion
    }
}
