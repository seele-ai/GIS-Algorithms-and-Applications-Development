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
        public static Func<Coordinate, Coordinate> Read(string path, bool assumeWgs84, out GeographicDatum sourceDatum, bool preserveSourceDatum = false)
        {
            sourceDatum = GeographicDatum.Wgs84.Clone();
            if (!File.Exists(path))
            {
                if (!assumeWgs84)
                {
                    if (!preserveSourceDatum) throw new FileNotFoundException("缺少 .prj 坐标系文件。", path);
                    sourceDatum = new GeographicDatum("USER_DEFINED:MissingPrj", "未知坐标系（缺少 PRJ）", Ellipsoids.WGS84)
                    { ParameterSource = "缺少 PRJ：需确认数据为经纬度并选择源坐标系；自定义项暂以 WGS84 椭球为起点。" };
                }
                return c => c;
            }
            string wkt = File.ReadAllText(path);
            string upper = wkt.ToUpperInvariant();
            if (!Regex.IsMatch(wkt, @"^\s*(GEOGCS|PROJCS)\s*\[", RegexOptions.IgnoreCase))
                throw new NotSupportedException("目前支持 ESRI/OGC WKT1 格式的 PRJ。");
            // 只识别实际 DATUM 节点，不从文件名、投影名称或任意子串猜测基准。
            var datumNode = Regex.Match(wkt, @"\bDATUM\s*\[\s*""([^""]+)""", RegexOptions.IgnoreCase);
            string datumName = datumNode.Success ? datumNode.Groups[1].Value : "";
            string key = Regex.Replace(datumName.ToUpperInvariant(), @"[\s_\-]", "");
            if (key.StartsWith("D")) key = key.Substring(1);
            bool beijing = key == "BEIJING1954", cgcs = key == "CHINA2000" || key == "CGCS2000" || key == "CHINAGEODETICCOORDINATESYSTEM2000";
            bool wgs = key == "WGS1984" || key == "WGS84" || key == "WORLDGEODETICSYSTEM1984";
            bool gcj = key == "GCJ02";
            bool unknown = !beijing && !cgcs && !wgs && !gcj;
            sourceDatum = beijing ? GeographicDatum.Beijing54.Clone() : gcj ? GeographicDatum.Gcj02.Clone() : GeographicDatum.Wgs84.Clone();
            if (cgcs) sourceDatum = new GeographicDatum("GCS_China_Geodetic_Coordinate_System_2000", "CGCS2000（按项目约定视作 WGS84）", Ellipsoids.CGCS2000)
                { ParameterSource = "项目现有约定：CGCS2000 经纬度按 WGS84 使用，不施加七参数偏移；非精密基准转换。" };
            if (unknown)
            {
                // 未知基准不等于未知椭球：保留 PRJ 参数，用于反投影和手工七参数转换。
                var spheroid = Regex.Match(wkt, @"SPHEROID\s*\[\s*""([^""]*)""\s*,\s*([-+0-9.eE]+)\s*,\s*([-+0-9.eE]+)", RegexOptions.IgnoreCase);
                if (!spheroid.Success) throw new InvalidDataException("PRJ 缺少有效 SPHEROID 椭球参数，无法进行源坐标反算。");
                double major = double.Parse(spheroid.Groups[2].Value, CultureInfo.InvariantCulture);
                double inverse = double.Parse(spheroid.Groups[3].Value, CultureInfo.InvariantCulture);
                if (double.IsNaN(major) || double.IsInfinity(major) || major <= 0 || double.IsNaN(inverse) || double.IsInfinity(inverse) || inverse <= 1)
                    throw new InvalidDataException("PRJ 椭球参数无效。");
                string name = datumName.Length == 0 ? "未命名基准" : datumName;
                sourceDatum = new GeographicDatum("USER_DEFINED:" + name, name + "（自定义转换）",
                    new Ellipsoid(spheroid.Groups[1].Value, name, name, major, inverse))
                    { ParameterSource = "源椭球读取自 PRJ；七参数由用户确认填写，位置矢量法。" };
                if (!preserveSourceDatum)
                    throw new InvalidOperationException("该地理坐标系需要在导入转换窗口中选择坐标系或填写转换参数。");
            }
            bool isProjected = Regex.IsMatch(wkt, @"^\s*PROJCS\s*\[", RegexOptions.IgnoreCase);
            if (gcj && isProjected) throw new NotSupportedException("GCJ-02 仅支持明确标注的地理坐标数据，不支持其投影 PRJ。");
            var prime = Regex.Match(wkt, @"PRIMEM\s*\[\s*""[^""]*""\s*,\s*([-+0-9.eE]+)", RegexOptions.IgnoreCase);
            if (!prime.Success || double.Parse(prime.Groups[1].Value, CultureInfo.InvariantCulture) != 0)
                throw new NotSupportedException("目前仅支持格林尼治本初子午线。");
            var units = Regex.Matches(wkt, @"UNIT\s*\[\s*""[^""]*""\s*,\s*([-+0-9.eE]+)", RegexOptions.IgnoreCase);
            if (units.Count == 0 || Math.Abs(double.Parse(units[0].Groups[1].Value, CultureInfo.InvariantCulture) - Math.PI / 180) > 1e-12)
                throw new NotSupportedException("PRJ 的角度单位必须为度。");
            ProjectionCS projection = null;
            double meters = 1;
            bool mercator = false;
            if (isProjected)
            {
                meters = double.Parse(units[units.Count - 1].Groups[1].Value, CultureInfo.InvariantCulture);
                if (meters <= 0) throw new InvalidDataException("PRJ 线性单位无效。");
                var ellipsoid = sourceDatum.Ellipsoid;
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
            GeographicDatum datum = sourceDatum;
            return coordinate =>
            {
                var p = new Coordinate(coordinate.X * meters, coordinate.Y * meters);
                if (mercator) p = new Coordinate(p.X / 6378137 * 180 / Math.PI, (2 * Math.Atan(Math.Exp(p.Y / 6378137)) - Math.PI / 2) * 180 / Math.PI);
                else if (projection != null) p = projection.TransferToLngLat(p);
                return preserveSourceDatum ? p : DatumTransform.ToWgs84(p, datum);
            };
        }
    }
}
