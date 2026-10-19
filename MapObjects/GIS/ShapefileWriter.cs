using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace GIS.IO
{
    /// <summary>标准二维 Shapefile 导出。参考旧 SimpleGIS 的几何/属性分离写入方式。</summary>
    public static class ShapefileWriter
    {
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
        public static readonly IReadOnlyList<string> Extensions = Array.AsReadOnly(new[] { ".shp", ".shx", ".dbf", ".prj", ".cpg" });
        private const string Wgs84 = "GEOGCS[\"GCS_WGS_1984\",DATUM[\"D_WGS_1984\",SPHEROID[\"WGS_1984\",6378137,298.257223563]],PRIMEM[\"Greenwich\",0],UNIT[\"Degree\",0.0174532925199433]]";

        /// <summary>整层导出为 WGS84 经纬度，返回缩短/去重后的字段名映射。
        /// 不修改图层。默认拒绝覆盖；校验完成后才写入，普通写入失败尝试恢复原文件。</summary>
        public static IReadOnlyList<string> Write(string path, Layer layer, bool overwrite = false)
        {
            if (layer == null) throw new ArgumentNullException(nameof(layer));
            if (!layer.IsWgs84) throw new InvalidOperationException("请先将图层转换为 WGS84 再导出。");
            path = Path.GetFullPath(Path.ChangeExtension(path, ".shp"));
            string[] paths = Extensions.Select(e => Path.ChangeExtension(path, e)).ToArray();
            if (!overwrite && paths.Any(File.Exists)) throw new IOException("存在同名配套文件，请另选文件名或确认覆盖。");
            FeatureClass fc = layer.FeatureClass;
            int type = ShapeType(fc.GeometryType);
            var shapes = new List<Geometry>();
            bool snapshot = layer.HasGeographicGeometries;
            for (int i = 0; i < fc.Features.Count; i++)
            {
                Geometry g = (snapshot ? layer.GeographicGeometries[i] : fc.Features[i].Geometry)?.Clone();
                if (!snapshot) Layer.TransformGeometry(g, fc.ProjectionCS, null);
                shapes.Add(g);
            }
            var records = shapes.Select(g => Record(g, type)).ToList();
            var extent = new Envelope();
            foreach (Geometry g in shapes) if (g != null && !g.IsEmpty) extent.ExpandToInclude(g.GetEnvelope());
            byte[] shp, shx;
            using (var s = new MemoryStream())
            using (var x = new MemoryStream())
            using (var sw = new BinaryWriter(s))
            using (var xw = new BinaryWriter(x))
            {
                Header(sw, checked(100 + records.Sum(r => checked(8 + r.Length))) / 2, type, extent);
                Header(xw, checked(50 + records.Count * 4), type, extent);
                for (int i = 0; i < records.Count; i++)
                {
                    Big(xw, checked((int)(s.Position / 2))); Big(xw, records[i].Length / 2);
                    Big(sw, i + 1); Big(sw, records[i].Length / 2); sw.Write(records[i]);
                }
                shp = s.ToArray(); shx = x.ToArray();
            }
            var warnings = new List<string>();
            byte[][] contents = { shp, shx, Dbf(fc, warnings), Utf8.GetBytes(Wgs84), Utf8.GetBytes("UTF-8") };
            byte[][] original = paths.Select(p => File.Exists(p) ? File.ReadAllBytes(p) : null).ToArray();
            int touched = -1;
            try
            {
                for (int i = 0; i < paths.Length; i++)
                    using (var stream = new FileStream(paths[i], overwrite ? FileMode.Create : FileMode.CreateNew, FileAccess.Write))
                    { touched = i; stream.Write(contents[i], 0, contents[i].Length); }
            }
            catch (Exception writeError)
            {
                var errors = new List<Exception> { writeError };
                for (int i = touched; i >= 0; i--)
                    try { if (original[i] == null) File.Delete(paths[i]); else File.WriteAllBytes(paths[i], original[i]); }
                    catch (Exception restoreError) { errors.Add(restoreError); }
                if (errors.Count > 1) throw new AggregateException("导出失败，部分文件无法恢复，请检查目标目录。", errors);
                throw;
            }
            return warnings;
        }
        private static int ShapeType(GeometryTypeConstant type)
        {
            switch (type)
            {
                case GeometryTypeConstant.Point: return 1;
                case GeometryTypeConstant.MultiPoint: return 8;
                case GeometryTypeConstant.LineString: case GeometryTypeConstant.MultiLineString: return 3;
                case GeometryTypeConstant.Polygon: case GeometryTypeConstant.MultiPolygon: return 5;
                default: throw new NotSupportedException("不支持的几何类型。");
            }
        }
        private static void Big(BinaryWriter w, int n)
        { w.Write((byte)(n >> 24)); w.Write((byte)(n >> 16)); w.Write((byte)(n >> 8)); w.Write((byte)n); }
        private static void Box(BinaryWriter w, Envelope e)
        { w.Write(e.IsNull ? 0 : e.MinX); w.Write(e.IsNull ? 0 : e.MinY); w.Write(e.IsNull ? 0 : e.MaxX); w.Write(e.IsNull ? 0 : e.MaxY); }
        private static void Header(BinaryWriter w, int length, int type, Envelope e)
        { Big(w, 9994); w.Write(new byte[20]); Big(w, length); w.Write(1000); w.Write(type); Box(w, e); w.Write(new byte[32]); }
        private static void XY(BinaryWriter w, Coordinate p)
        {
            if (double.IsNaN(p.X) || double.IsInfinity(p.X) || double.IsNaN(p.Y) || double.IsInfinity(p.Y) || Math.Abs(p.X) > 180 || Math.Abs(p.Y) > 90)
                throw new InvalidDataException("导出坐标不是有效的 WGS84 经纬度。");
            w.Write(p.X); w.Write(p.Y);
        }
        private static void Ring(List<List<Coordinate>> parts, Points source, bool hole)
        {
            var ring = source.ToList();
            if (ring.Count > 0 && (ring[0].X != ring[ring.Count - 1].X || ring[0].Y != ring[ring.Count - 1].Y)) ring.Add(ring[0]);
            if (ring.Count < 4 || ring.Take(ring.Count - 1).Distinct().Count() < 3) throw new InvalidDataException("面环至少需要三个不同顶点。");
            double area = 0;
            for (int i = 1; i < ring.Count; i++) area += (ring[i - 1].X - ring[0].X) * (ring[i].Y - ring[0].Y) - (ring[i].X - ring[0].X) * (ring[i - 1].Y - ring[0].Y);
            if (area == 0) throw new InvalidDataException("不能导出面积为零的面环。");
            // ESRI 规范：外环顺时针，内环逆时针；仅调整副本。
            if ((area > 0) != hole) ring.Reverse();
            parts.Add(ring);
        }
        private static byte[] Record(Geometry g, int type)
        {
            using (var stream = new MemoryStream())
            using (var w = new BinaryWriter(stream))
            {
                if (g == null || g.IsEmpty) { w.Write(0); return stream.ToArray(); }
                if (ShapeType(g.GeometryType) != type) throw new InvalidDataException("几何类型与图层不一致。");
                w.Write(type);
                if (type == 1) XY(w, ((Point)g).Coordinate);
                else
                {
                    var parts = new List<List<Coordinate>>();
                    if (g is MultiPoint mp) parts.Add(mp.Points.ToList());
                    if (g is LineString line) parts.Add(line.Points.ToList());
                    if (g is MultiLineString ml) foreach (LineString p in ml.Parts) parts.Add(p.Points.ToList());
                    var polygons = new List<Polygon>();
                    if (g is Polygon polygon) polygons.Add(polygon);
                    if (g is MultiPolygon multi) polygons.AddRange(multi.Parts);
                    foreach (Polygon p in polygons) { Ring(parts, p.ExteriorRing, false); foreach (Points h in p.Holes) Ring(parts, h, true); }
                    if (type == 3 && parts.Any(p => p.Count < 2)) throw new InvalidDataException("线部件至少需要两个顶点。");
                    Box(w, g.GetEnvelope());
                    if (type != 8) w.Write(parts.Count);
                    w.Write(parts.Sum(p => p.Count));
                    int offset = 0;
                    if (type != 8) foreach (var part in parts) { w.Write(offset); offset += part.Count; }
                    foreach (var part in parts) foreach (Coordinate p in part) XY(w, p);
                }
                return stream.ToArray();
            }
        }
        private static string Shorten(string s)
        { return s.Substring(0, s.Length - (s.Length >= 2 && char.IsSurrogatePair(s, s.Length - 2) ? 2 : 1)); }
        private static byte[] Dbf(FeatureClass fc, List<string> warnings)
        {
            int count = fc.Fields.Count;
            if (count > 255) throw new InvalidDataException("DBF 最多支持255个字段。");
            var names = new List<string>(); var types = new char[count]; var widths = new int[count];
            var values = new string[fc.Features.Count, count];
            for (int j = 0; j < count; j++)
            {
                Field field = fc.Fields[j];
                string original = field.Name ?? "", name = original;
                if (string.IsNullOrWhiteSpace(name) || name.Any(char.IsControl)) name = "FIELD";
                while (Utf8.GetByteCount(name) > 10) name = Shorten(name);
                string stem = name;
                for (int n = 1; names.Any(s => s.Equals(name, StringComparison.OrdinalIgnoreCase)); n++)
                {
                    string suffix = "_" + n.ToString(CultureInfo.InvariantCulture);
                    while (Utf8.GetByteCount(stem + suffix) > 10) stem = Shorten(stem);
                    name = stem + suffix;
                }
                names.Add(name);
                if (name != original) warnings.Add(original + " → " + name);
                switch (field.ValueType)
                {
                    case FieldTypeConstant.Text: types[j] = 'C'; widths[j] = 1; break;
                    case FieldTypeConstant.Boolean: types[j] = 'L'; widths[j] = 1; break;
                    case FieldTypeConstant.Date: types[j] = 'D'; widths[j] = 8; break;
                    case FieldTypeConstant.Byte: case FieldTypeConstant.Int16: case FieldTypeConstant.Int32: types[j] = 'N'; widths[j] = 11; break;
                    case FieldTypeConstant.Single: case FieldTypeConstant.Double: types[j] = 'F'; widths[j] = 24; break;
                    default: throw new NotSupportedException("不支持的字段类型：" + field.ValueType);
                }
                for (int i = 0; i < fc.Features.Count; i++)
                {
                    var attributes = fc.Features[i].Attributes;
                    if (attributes != null && attributes.Count != count) throw new InvalidDataException("属性个数与字段个数不一致。");
                    object value = attributes?.GetItem(j); string text = "";
                    if (value != null && value != DBNull.Value)
                    {
                        if (types[j] == 'D') text = Convert.ToDateTime(value, CultureInfo.InvariantCulture).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
                        else if (types[j] == 'L') text = Convert.ToBoolean(value, CultureInfo.InvariantCulture) ? "T" : "F";
                        else if (types[j] == 'F')
                        {
                            double n = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                            if (double.IsNaN(n) || double.IsInfinity(n)) throw new InvalidDataException("DBF 不支持非有限数值。");
                            text = n.ToString("G17", CultureInfo.InvariantCulture);
                        }
                        else text = Convert.ToString(value, CultureInfo.InvariantCulture);
                    }
                    if (types[j] == 'C')
                    {
                        if (text.IndexOf('\0') >= 0) throw new InvalidDataException("文本含有 NUL 字符。");
                        widths[j] = Math.Max(widths[j], Utf8.GetByteCount(text));
                        if (widths[j] > 254) throw new InvalidDataException("字段“" + original + "”的文本超过 DBF 254字节上限，未导出，原文件不变。");
                    }
                    else if (text.Length > widths[j]) throw new InvalidDataException("字段“" + original + "”的数值超出 DBF 宽度。");
                    values[i, j] = text;
                }
            }
            int rowLength = widths.Sum() + 1;
            if (rowLength > 65535) throw new InvalidDataException("DBF 记录长度超限。");
            using (var stream = new MemoryStream())
            using (var w = new BinaryWriter(stream))
            {
                DateTime now = DateTime.Today;
                w.Write((byte)3); w.Write((byte)(now.Year - 1900)); w.Write((byte)now.Month); w.Write((byte)now.Day);
                w.Write(fc.Features.Count); w.Write((ushort)(33 + count * 32)); w.Write((ushort)rowLength); w.Write(new byte[20]);
                for (int j = 0; j < count; j++)
                {
                    var d = new byte[32]; Utf8.GetBytes(names[j]).CopyTo(d, 0); d[11] = (byte)types[j]; d[16] = (byte)widths[j]; d[17] = (byte)(types[j] == 'F' ? 15 : 0); w.Write(d);
                }
                w.Write((byte)13);
                for (int i = 0; i < fc.Features.Count; i++)
                {
                    w.Write((byte)32);
                    for (int j = 0; j < count; j++)
                    {
                        byte[] bytes = Utf8.GetBytes(values[i, j]), padding = Enumerable.Repeat((byte)32, widths[j] - bytes.Length).ToArray();
                        if (types[j] == 'N' || types[j] == 'F') w.Write(padding);
                        w.Write(bytes);
                        if (types[j] != 'N' && types[j] != 'F') w.Write(padding);
                    }
                }
                w.Write((byte)26); return stream.ToArray();
            }
        }
    }
}
