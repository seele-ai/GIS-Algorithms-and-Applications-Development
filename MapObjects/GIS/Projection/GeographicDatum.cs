using System;
using System.Collections.Generic;

namespace GIS
{
    /// <summary>
    /// 地理坐标系统（大地基准）。
    /// 用 Bursa-Wolf 七参数描述本基准与 WGS84 的几何关系：
    /// 三个地心平移量（米）、三个坐标轴旋转量（角秒）、一个尺度比（ppm）。
    /// 参数全部为 0 表示只做椭球换算、不做基准平移——此时同一组大地坐标在不同椭球上
    /// 表示的是空间中不同的点，在中国境内与 WGS84 的差异约为 50~150 米。
    /// 若要达到米级精度，需要按地区实测/向测绘部门收集本地区的三参数或七参数后填入。
    /// </summary>
    public class GeographicDatum
    {
        /// <summary>地理坐标系统名称，如 GCS_Beijing_1954。</summary>
        public string GeoCSName;

        /// <summary>中文简称，如“北京54”。</summary>
        public string DisplayName;

        /// <summary>所属椭球体。</summary>
        public Ellipsoid Ellipsoid;

        /// <summary>X 方向平移量（米，本基准 → WGS84）。</summary>
        public double Dx;

        /// <summary>Y 方向平移量（米，本基准 → WGS84）。</summary>
        public double Dy;

        /// <summary>Z 方向平移量（米，本基准 → WGS84）。</summary>
        public double Dz;

        /// <summary>X 轴旋转量（角秒，本基准 → WGS84）。</summary>
        public double Rx;

        /// <summary>Y 轴旋转量（角秒，本基准 → WGS84）。</summary>
        public double Ry;

        /// <summary>Z 轴旋转量（角秒，本基准 → WGS84）。</summary>
        public double Rz;

        /// <summary>尺度比改正（百万分之一，本基准 → WGS84）。</summary>
        public double ScalePpm;

        /// <summary>参数来源说明（在界面与文档中显示，便于核对）。</summary>
        public string ParameterSource = "";

        /// <summary>参数是否为近似值（全国平均或借用同量级参数，精度有限）。</summary>
        public bool IsApproximate;

        /// <summary>
        /// 是否为 GCJ-02（国测局坐标系/火星坐标）。它不是换椭球，而是在经纬度上直接做加密偏移，
        /// 因此转换走 <see cref="Gcj02Transform"/>，不使用七参数。
        /// </summary>
        public bool IsGcj02;

        public GeographicDatum(string geoCSName, string displayName, Ellipsoid ellipsoid)
        {
            GeoCSName = geoCSName;
            DisplayName = displayName;
            Ellipsoid = ellipsoid;
        }

        public GeographicDatum(string geoCSName, string displayName, Ellipsoid ellipsoid,
            double dx, double dy, double dz, double rx, double ry, double rz, double scalePpm)
            : this(geoCSName, displayName, ellipsoid)
        {
            Dx = dx; Dy = dy; Dz = dz;
            Rx = rx; Ry = ry; Rz = rz;
            ScalePpm = scalePpm;
        }

        /// <summary>是否为 WGS84（本系统的默认地理坐标系）。</summary>
        public bool IsWgs84
        {
            get
            {
                if (IsGcj02) return false;   // GCJ-02 椭球与 WGS84 相同，但坐标是被加密偏移过的
                return Math.Abs(Dx) < 1e-9 && Math.Abs(Dy) < 1e-9 && Math.Abs(Dz) < 1e-9 &&
                       Math.Abs(Rx) < 1e-12 && Math.Abs(Ry) < 1e-12 && Math.Abs(Rz) < 1e-12 &&
                       Math.Abs(ScalePpm) < 1e-12 &&
                       Math.Abs(Ellipsoid.SemiMajor - Ellipsoids.WGS84.SemiMajor) < 1e-6;
            }
        }

        /// <summary>是否设置了基准平移/旋转/尺度参数（否则只做椭球换算）。</summary>
        public bool HasShiftParameters
        {
            get
            {
                return Math.Abs(Dx) > 1e-9 || Math.Abs(Dy) > 1e-9 || Math.Abs(Dz) > 1e-9 ||
                       Math.Abs(Rx) > 1e-12 || Math.Abs(Ry) > 1e-12 || Math.Abs(Rz) > 1e-12 ||
                       Math.Abs(ScalePpm) > 1e-12;
            }
        }

        public GeographicDatum Clone()
        {
            return new GeographicDatum(GeoCSName, DisplayName, Ellipsoid, Dx, Dy, Dz, Rx, Ry, Rz, ScalePpm)
            {
                ParameterSource = ParameterSource,
                IsApproximate = IsApproximate,
                IsGcj02 = IsGcj02
            };
        }

        /// <summary>参数与来源的一行说明，用于界面提示。</summary>
        public string ParameterText()
        {
            return string.Format(
                "ΔX={0} ΔY={1} ΔZ={2} 旋转=({3}, {4}, {5})″ 尺度={6} ppm",
                Dx, Dy, Dz, Rx, Ry, Rz, ScalePpm);
        }

        /// <summary>显示名称，非 WGS84 时附带椭球名。</summary>
        public override string ToString()
        {
            return DisplayName + "（" + GeoCSName + "）";
        }

        /// <summary>简短名称，用于界面上的紧凑标识，如“北京54”。</summary>
        public string ShortName
        {
            get
            {
                if (string.IsNullOrEmpty(DisplayName)) return GeoCSName;
                int index = DisplayName.IndexOf('（');
                return index > 0 ? DisplayName.Substring(0, index) : DisplayName;
            }
        }

        /// <summary>WGS84 大地基准（本系统的默认地理坐标系统）。</summary>
        public static readonly GeographicDatum Wgs84 =
            new GeographicDatum("GCS_WGS_1984", "WGS84", Ellipsoids.WGS84)
            {
                ParameterSource = "本系统基准，无需转换"
            };

        /// <summary>
        /// 1954北京坐标系（克拉索夫斯基椭球）。
        /// 采用 EPSG:15920 “Beijing 1954 to WGS 84 (3)”（位置矢量法，中国，精度 15 m）：
        /// ΔX=31.4、ΔY=-144.3、ΔZ=-74.8、Z 旋转=0.814″、尺度=-0.38 ppm。
        /// 来源：EPSG 数据集（见 https://epsg.io/2414-15920 ）。
        /// </summary>
        public static readonly GeographicDatum Beijing54 =
            new GeographicDatum("GCS_Beijing_1954", "北京54", Ellipsoids.Beijing54,
                31.4, -144.3, -74.8, 0, 0, 0.814, -0.38)
            {
                ParameterSource = "EPSG:15920 Beijing 1954 to WGS 84 (3)，中国，精度 15 m"
            };

        /// <summary>
        /// GCJ-02 国测局坐标系（“火星坐标”）。国内地图服务（高德/腾讯等）使用的加密坐标，
        /// 与 WGS84 的差异是**按位置非线性加密**的水平偏移（境内约 300~700 米），
        /// 不属于椭球/基准转换，转换由 <see cref="Gcj02Transform"/> 完成。
        /// </summary>
        public static readonly GeographicDatum Gcj02 =
            new GeographicDatum("GCJ_02", "GCJ-02（国测局坐标系）", Ellipsoids.WGS84)
            {
                IsGcj02 = true,
                ParameterSource = "国测局加密偏移算法（经纬度非线性偏移，境内约 300~700 m；境外不偏移）"
            };

        /// <summary>
        /// 系统内置的全部地理坐标系统。
        /// 说明：**只保留有明确转换方式的坐标系**——
        ///  · WGS84：本系统基准；
        ///  · 北京54：EPSG:15920 给出全国参数（15 m），可直接换算；
        ///  · GCJ-02：有公开的国测局偏移算法，可直接换算。
        /// 西安80 已删除：EPSG 未发布“Xian 1980 to WGS 84”转换（检索 0 条），
        /// 相关论文对其参数做了保密处理（见 OSGeo《China datums》：
        /// https://wiki.osgeo.org/w/index.php?title=China_datums&oldid=130239 ），
        /// 拿不到可靠的全国通用参数，因此不提供以免给出错误结果。
        /// CGCS2000 已删除：它与 WGS84 同属 ITRF 地心框架、椭球仅有极小差异，
        /// 实际坐标差异为毫米级，EPSG 也未发布二者之间的转换参数；
        /// CGCS2000 数据可直接当作 WGS84 使用。
        /// </summary>
        public static List<GeographicDatum> All()
        {
            return new List<GeographicDatum> { Wgs84, Beijing54, Gcj02 };
        }

        /// <summary>按名称查找内置地理坐标系统，找不到返回 WGS84。</summary>
        public static GeographicDatum Find(string geoCSName)
        {
            foreach (GeographicDatum datum in All())
                if (string.Equals(datum.GeoCSName, geoCSName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(datum.DisplayName, geoCSName, StringComparison.OrdinalIgnoreCase))
                    return datum.Clone();
            return Wgs84.Clone();
        }
    }

    /// <summary>
    /// 大地基准之间的坐标转换（Bursa-Wolf 七参数模型，采用位置矢量约定）：
    ///   先把大地坐标 (B,L,H) 按本基准椭球换算为空间直角坐标 (X,Y,Z)，
    ///   再作七参数相似变换到 WGS84 的空间直角坐标，最后按 WGS84 椭球换算回大地坐标；
    ///   反方向转换取参数的相反数（小角度近似，厘米级精度足够）。
    /// </summary>
    public static class DatumTransform
    {
        private const double SecToRad = Math.PI / (180.0 * 3600.0);

        /// <summary>把本基准下的经纬度转换为 WGS84 经纬度。</summary>
        public static Coordinate ToWgs84(Coordinate lngLat, GeographicDatum from)
        {
            if (from == null) return lngLat;
            if (from.IsGcj02) return Gcj02Transform.Gcj02ToWgs84(lngLat);   // 国测局加密偏移，非七参数
            if (from.IsWgs84) return lngLat;
            double x, y, z;
            GeodeticToEcef(lngLat.X, lngLat.Y, 0, from.Ellipsoid, out x, out y, out z);
            ApplyParameters(ref x, ref y, ref z, from);
            return EcefToGeodetic(x, y, z, Ellipsoids.WGS84);
        }

        /// <summary>把 WGS84 经纬度转换到本基准下的经纬度。</summary>
        public static Coordinate FromWgs84(Coordinate lngLat, GeographicDatum to)
        {
            if (to == null) return lngLat;
            if (to.IsGcj02) return Gcj02Transform.Wgs84ToGcj02(lngLat);
            if (to.IsWgs84) return lngLat;
            double x, y, z;
            GeodeticToEcef(lngLat.X, lngLat.Y, 0, Ellipsoids.WGS84, out x, out y, out z);

            GeographicDatum inverse = Negate(to);
            // 初值：u0 = R⁻¹(x − t)
            double ux = x - to.Dx, uy = y - to.Dy, uz = z - to.Dz;
            ApplyRotation(ref ux, ref uy, ref uz, inverse);
            for (int i = 0; i < 3; i++)                // 迭代消除小角度近似的残余误差
            {
                double fx = ux, fy = uy, fz = uz;
                ApplyParameters(ref fx, ref fy, ref fz, to);
                double cx = x - fx, cy = y - fy, cz = z - fz;
                ApplyRotation(ref cx, ref cy, ref cz, inverse);   // 修正量只做旋转/尺度反变换
                ux += cx; uy += cy; uz += cz;
            }
            return EcefToGeodetic(ux, uy, uz, to.Ellipsoid);
        }

        /// <summary>在任意两个大地基准之间转换经纬度。</summary>
        public static Coordinate Transform(Coordinate lngLat, GeographicDatum from, GeographicDatum to)
        {
            if (from == null) from = GeographicDatum.Wgs84;
            if (to == null) to = GeographicDatum.Wgs84;
            if (ReferenceEquals(from, to) || string.Equals(from.GeoCSName, to.GeoCSName, StringComparison.Ordinal))
                return lngLat;
            return FromWgs84(ToWgs84(lngLat, from), to);
        }

        /// <summary>估算该基准与 WGS84 在一个点位上的差异（米），用于界面上提示转换量级。</summary>
        public static double EstimateShiftMeters(Coordinate lngLat, GeographicDatum datum)
        {
            if (datum == null) return 0;
            Coordinate shifted = ToWgs84(lngLat, datum);
            double metersPerDegreeLon = 111320.0 * Math.Cos(lngLat.Y * Math.PI / 180.0);
            double dx = (shifted.X - lngLat.X) * metersPerDegreeLon;
            double dy = (shifted.Y - lngLat.Y) * 110540.0;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        // 位置矢量约定的 Bursa-Wolf 七参数变换（本基准 → WGS84）：t + (1+s)·R·u
        private static void ApplyParameters(ref double x, ref double y, ref double z, GeographicDatum p)
        {
            double sx = p.Dx, sy = p.Dy, sz = p.Dz;
            ApplyRotation(ref x, ref y, ref z, p);
            x += sx; y += sy; z += sz;
        }

        // 只做旋转与尺度部分（用于反变换的初值与迭代修正）
        private static void ApplyRotation(ref double x, ref double y, ref double z, GeographicDatum p)
        {
            double rx = p.Rx * SecToRad, ry = p.Ry * SecToRad, rz = p.Rz * SecToRad;
            double scale = 1.0 + p.ScalePpm * 1e-6;
            double sx = scale * (x - rz * y + ry * z);
            double sy = scale * (rz * x + y - rx * z);
            double sz = scale * (-ry * x + rx * y + z);
            x = sx; y = sy; z = sz;
        }

        private static GeographicDatum Negate(GeographicDatum d)
        {
            return new GeographicDatum(d.GeoCSName, d.DisplayName, d.Ellipsoid,
                -d.Dx, -d.Dy, -d.Dz, -d.Rx, -d.Ry, -d.Rz, -d.ScalePpm);
        }

        /// <summary>大地坐标（度、度、米）→ 空间直角坐标（米）。</summary>
        public static void GeodeticToEcef(double lon, double lat, double height, Ellipsoid ellipsoid,
            out double x, out double y, out double z)
        {
            double a = ellipsoid.SemiMajor;
            double e2 = ellipsoid.GetE2();
            double radLon = lon * Math.PI / 180.0, radLat = lat * Math.PI / 180.0;
            double sinLat = Math.Sin(radLat), cosLat = Math.Cos(radLat);
            double n = a / Math.Sqrt(1 - e2 * sinLat * sinLat);
            x = (n + height) * cosLat * Math.Cos(radLon);
            y = (n + height) * cosLat * Math.Sin(radLon);
            z = (n * (1 - e2) + height) * sinLat;
        }

        /// <summary>空间直角坐标（米）→ 大地坐标（度、度；高程取 0）。</summary>
        public static Coordinate EcefToGeodetic(double x, double y, double z, Ellipsoid ellipsoid)
        {
            double a = ellipsoid.SemiMajor;
            double b = ellipsoid.GetSemiMinor();
            double e2 = ellipsoid.GetE2();
            double ep2 = (a * a - b * b) / (b * b);
            double p = Math.Sqrt(x * x + y * y);
            double lon = Math.Atan2(y, x);
            double lat;
            if (p < 1e-9)
            {
                lat = z >= 0 ? Math.PI / 2 : -Math.PI / 2;
            }
            else
            {
                // Bowring 近似，再做几次迭代收敛到亚毫米
                double theta = Math.Atan2(z * a, p * b);
                lat = Math.Atan2(z + ep2 * b * Math.Pow(Math.Sin(theta), 3),
                    p - e2 * a * Math.Pow(Math.Cos(theta), 3));
                for (int i = 0; i < 5; i++)
                {
                    double sinLat = Math.Sin(lat);
                    double n = a / Math.Sqrt(1 - e2 * sinLat * sinLat);
                    lat = Math.Atan2(z + e2 * n * sinLat, p);
                }
            }
            return new Coordinate(lon * 180.0 / Math.PI, lat * 180.0 / Math.PI);
        }
    }
}
