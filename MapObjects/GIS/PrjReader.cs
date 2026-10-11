using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace GIS.IO
{
    internal static class PrjReader
    {
        static double Parameter(string wkt, string name, double? fallback = null)
        {
            var m = Regex.Match(wkt, @"PARAMETER\s*\[\s*""" + name + @"""\s*,\s*([-+0-9.eE]+)", RegexOptions.IgnoreCase);
            if (m.Success) return double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            if (fallback.HasValue) return fallback.Value;
            throw new NotSupportedException("PRJ 缺少参数：" + name);
        }
        public static Func<Coordinate, Coordinate> Read(string path, bool assumeWgs84)
        {
            if (!File.Exists(path))
            {
                if (!assumeWgs84) throw new FileNotFoundException("缺少 .prj 坐标系文件。", path);
                return c => c;
            }
            string wkt = File.ReadAllText(path);
            string upper = wkt.ToUpperInvariant();
            bool beijing = upper.Contains("BEIJING") || upper.Contains("BEIJING_1954");
            bool cgcs = upper.Contains("CGCS2000") || upper.Contains("CHINA_2000");
            bool wgs = upper.Contains("WGS_1984") || upper.Contains("WGS 84") || upper.Contains("WGS84");
            if (!beijing && !cgcs && !wgs) throw new NotSupportedException("暂不支持该 PRJ 基准。支持 WGS84、CGCS2000、北京54。");
            if (!upper.TrimStart().StartsWith("GEOGCS[") && !upper.TrimStart().StartsWith("PROJCS["))
                throw new NotSupportedException("目前支持 ESRI/OGC WKT1 格式的 PRJ。");
            var prime = Regex.Match(wkt, @"PRIMEM\s*\[\s*""[^""]*""\s*,\s*([-+0-9.eE]+)", RegexOptions.IgnoreCase);
            if (!prime.Success || double.Parse(prime.Groups[1].Value, CultureInfo.InvariantCulture) != 0)
                throw new NotSupportedException("目前仅支持格林尼治本初子午线。");
            var units = Regex.Matches(wkt, @"UNIT\s*\[\s*""[^""]*""\s*,\s*([-+0-9.eE]+)", RegexOptions.IgnoreCase);
            if (units.Count == 0 || Math.Abs(double.Parse(units[0].Groups[1].Value, CultureInfo.InvariantCulture) - Math.PI / 180) > 1e-12)
                throw new NotSupportedException("PRJ 的角度单位必须为度。");
            ProjectionCS projection = null;
            double meters = 1;
            bool mercator = false;
            if (upper.TrimStart().StartsWith("PROJCS["))
            {
                meters = double.Parse(units[units.Count - 1].Groups[1].Value, CultureInfo.InvariantCulture);
                if (meters <= 0) throw new InvalidDataException("PRJ 线性单位无效。");
                var ellipsoid = beijing ? Ellipsoids.Beijing54 : cgcs ? Ellipsoids.CGCS2000 : Ellipsoids.WGS84;
                var method = Regex.Match(wkt, @"PROJECTION\s*\[\s*""([^""]+)""", RegexOptions.IgnoreCase).Groups[1].Value.ToUpperInvariant();
                double cm = Parameter(wkt, "Central_Meridian", 0), lat = Parameter(wkt, "Latitude_Of_Origin", 0);
                double fe = Parameter(wkt, "False_Easting", 0) * meters, fn = Parameter(wkt, "False_Northing", 0) * meters;
                if (method == "LAMBERT_CONFORMAL_CONIC" || method == "LAMBERT_CONFORMAL_CONIC_2SP")
                    projection = new ProjLambert("导入 Lambert", ellipsoid, cm, lat, Parameter(wkt, "Standard_Parallel_1"), Parameter(wkt, "Standard_Parallel_2"), fe, fn);
                else if (method == "TRANSVERSE_MERCATOR" || method == "GAUSS_KRUGER")
                {
                    if (lat != 0) throw new NotSupportedException("横轴墨卡托目前仅支持纬度原点 0°。");
                    projection = new ProjGauss_Kruger("导入横轴墨卡托", ellipsoid, cm, lat, fe, fn, Parameter(wkt, "Scale_Factor", 1));
                }
                else if (wgs && (method == "MERCATOR_AUXILIARY_SPHERE" || upper.Contains("3857")))
                {
                    if (cm != 0 || fe != 0 || fn != 0) throw new NotSupportedException("仅支持标准 Web Mercator 参数。");
                    mercator = true;
                }
                else throw new NotSupportedException("暂不支持 PRJ 投影：" + method);
            }
            return coordinate =>
            {
                var p = new Coordinate(coordinate.X * meters, coordinate.Y * meters);
                if (mercator) p = new Coordinate(p.X / 6378137 * 180 / Math.PI, (2 * Math.Atan(Math.Exp(p.Y / 6378137)) - Math.PI / 2) * 180 / Math.PI);
                else if (projection != null) p = projection.TransferToLngLat(p);
                return beijing ? DatumTransform.ToWgs84(p, GeographicDatum.Beijing54) : p;
            };
        }
    }
}
