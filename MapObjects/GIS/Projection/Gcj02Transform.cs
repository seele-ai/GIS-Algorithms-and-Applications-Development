using System;

namespace GIS
{
    /// <summary>
    /// GCJ-02（国测局坐标系，俗称“火星坐标”）与 WGS84 之间的转换。
    /// GCJ-02 并不是换椭球，而是在 WGS84 经纬度上加了一组与位置相关的**非线性偏移**
    /// （境内水平偏移量约 300~700 米），因此不能用 Bursa-Wolf 七参数描述，只能按算法换算。
    /// 这里采用公开的国测局偏移算法：
    ///   1) 由 (经度-105, 纬度-35) 按多项式 + 三角函数得到偏移量；
    ///   2) 再按当地子午圈/卯酉圈曲率半径把偏移量换算成角度加到经纬度上。
    /// 反算（GCJ→WGS）没有解析式，用迭代逼近，收敛到 1e-12 度以内。
    /// 中国大陆范围之外不加密（原样返回）。
    /// </summary>
    public static class Gcj02Transform
    {
        private const double PI = 3.14159265358979324;

        /// <summary>算法中使用的长半轴（克拉索夫斯基，国测局算法固定取该值）。</summary>
        private const double A = 6378245.0;

        /// <summary>算法中使用的第一偏心率平方。</summary>
        private const double EE = 0.00669342162296594323;

        /// <summary>判断经纬度是否在中国大陆加密范围之外（范围外不做偏移）。</summary>
        public static bool IsOutOfChina(double lon, double lat)
        {
            if (lon < 72.004 || lon > 137.8347) return true;
            if (lat < 0.8293 || lat > 55.8271) return true;
            return false;
        }

        private static double TransformLat(double x, double y)
        {
            double ret = -100.0 + 2.0 * x + 3.0 * y + 0.2 * y * y + 0.1 * x * y + 0.2 * Math.Sqrt(Math.Abs(x));
            ret += (20.0 * Math.Sin(6.0 * x * PI) + 20.0 * Math.Sin(2.0 * x * PI)) * 2.0 / 3.0;
            ret += (20.0 * Math.Sin(y * PI) + 40.0 * Math.Sin(y / 3.0 * PI)) * 2.0 / 3.0;
            ret += (160.0 * Math.Sin(y / 12.0 * PI) + 320.0 * Math.Sin(y * PI / 30.0)) * 2.0 / 3.0;
            return ret;
        }

        private static double TransformLon(double x, double y)
        {
            double ret = 300.0 + x + 2.0 * y + 0.1 * x * x + 0.1 * x * y + 0.1 * Math.Sqrt(Math.Abs(x));
            ret += (20.0 * Math.Sin(6.0 * x * PI) + 20.0 * Math.Sin(2.0 * x * PI)) * 2.0 / 3.0;
            ret += (20.0 * Math.Sin(x * PI) + 40.0 * Math.Sin(x / 3.0 * PI)) * 2.0 / 3.0;
            ret += (150.0 * Math.Sin(x / 12.0 * PI) + 300.0 * Math.Sin(x / 30.0 * PI)) * 2.0 / 3.0;
            return ret;
        }

        /// <summary>WGS84 经纬度 → GCJ-02 经纬度（境外原样返回）。</summary>
        public static Coordinate Wgs84ToGcj02(Coordinate wgs84)
        {
            return Wgs84ToGcj02(wgs84.X, wgs84.Y);
        }

        /// <summary>WGS84 经纬度 → GCJ-02 经纬度（境外原样返回）。</summary>
        public static Coordinate Wgs84ToGcj02(double lon, double lat)
        {
            if (IsOutOfChina(lon, lat)) return new Coordinate(lon, lat);

            double dLat = TransformLat(lon - 105.0, lat - 35.0);
            double dLon = TransformLon(lon - 105.0, lat - 35.0);
            double radLat = lat / 180.0 * PI;
            double magic = Math.Sin(radLat);
            magic = 1 - EE * magic * magic;
            double sqrtMagic = Math.Sqrt(magic);
            dLat = (dLat * 180.0) / ((A * (1 - EE)) / (magic * sqrtMagic) * PI);
            dLon = (dLon * 180.0) / (A / sqrtMagic * Math.Cos(radLat) * PI);
            return new Coordinate(lon + dLon, lat + dLat);
        }

        /// <summary>
        /// GCJ-02 经纬度 → WGS84 经纬度（境外原样返回）。
        /// 采用迭代反解：反复用正算结果修正初值，直到偏移量小于 1e-12 度（约 0.1 微米）。
        /// </summary>
        public static Coordinate Gcj02ToWgs84(Coordinate gcj02)
        {
            double lon = gcj02.X, lat = gcj02.Y;
            if (IsOutOfChina(lon, lat)) return new Coordinate(lon, lat);
            for (int i = 0; i < 30; i++)
            {
                Coordinate test = Wgs84ToGcj02(lon, lat);
                double dLon = test.X - gcj02.X;
                double dLat = test.Y - gcj02.Y;
                lon -= dLon;
                lat -= dLat;
                if (Math.Abs(dLon) < 1e-12 && Math.Abs(dLat) < 1e-12) break;
            }
            return new Coordinate(lon, lat);
        }
    }
}
