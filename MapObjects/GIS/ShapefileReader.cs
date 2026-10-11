using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace GIS.IO
{
    /// <summary>参考 SimpleGIS 的 SHP/DBF 二进制读取方式，适配共享 GIS 模型。</summary>
    public static class ShapefileReader
    {
        static int BigInt(BinaryReader r)
        {
            var b = r.ReadBytes(4);
            if (b.Length != 4) throw new InvalidDataException("文件意外结束。");
            Array.Reverse(b); return BitConverter.ToInt32(b, 0);
        }
        static int BaseType(int t)
        {
            if (t == 1 || t == 11 || t == 21) return 1;
            if (t == 3 || t == 13 || t == 23) return 3;
            if (t == 5 || t == 15 || t == 25) return 5;
            if (t == 8 || t == 18 || t == 28) return 8;
            throw new NotSupportedException("不支持的 SHP 类型：" + t);
        }
        public static Layer Read(string path, bool assumeWgs84 = false)
        {
            var convert = PrjReader.Read(Path.ChangeExtension(path, ".prj"), assumeWgs84);
            var shapes = new List<Geometry>();
            int type;
            using (var r = new BinaryReader(File.OpenRead(path)))
            {
                if (r.BaseStream.Length < 100 || BigInt(r) != 9994) throw new InvalidDataException("SHP 文件头错误。");
                r.BaseStream.Position = 24;
                if (BigInt(r) * 2L != r.BaseStream.Length || r.ReadInt32() != 1000) throw new InvalidDataException("SHP 长度或版本错误。");
                int headerType = r.ReadInt32(); type = BaseType(headerType);
                r.BaseStream.Position = 100;
                while (r.BaseStream.Position < r.BaseStream.Length)
                {
                    BigInt(r);
                    long size = BigInt(r) * 2L;
                    if (size < 4 || size > int.MaxValue || size > r.BaseStream.Length - r.BaseStream.Position)
                        throw new InvalidDataException("SHP 记录长度错误。");
                    using (var record = new BinaryReader(new MemoryStream(r.ReadBytes((int)size))))
                    {
                        int t = record.ReadInt32();
                        if (t == 0) { shapes.Add(null); continue; }
                        if (t != headerType) throw new InvalidDataException("SHP 记录类型不一致。");
                        shapes.Add(ReadGeometry(record, type, convert));
                    }
                }
            }
            var fc = new FeatureClass(Path.GetFileNameWithoutExtension(path), type == 1 ? GeometryTypeConstant.Point :
                type == 8 ? GeometryTypeConstant.MultiPoint : type == 3 ? GeometryTypeConstant.MultiLineString : GeometryTypeConstant.MultiPolygon);
            ReadDbf(Path.ChangeExtension(path, ".dbf"), fc, shapes);
            var layer = new Layer(fc); layer.CaptureGeographic(null); return layer;
        }
        static Coordinate ReadPoint(BinaryReader r, Func<Coordinate, Coordinate> convert)
        {
            var p = convert(new Coordinate(r.ReadDouble(), r.ReadDouble()));
            if (double.IsNaN(p.X) || double.IsInfinity(p.X) || double.IsNaN(p.Y) || double.IsInfinity(p.Y) ||
                Math.Abs(p.X) > 180 || Math.Abs(p.Y) > 90) throw new InvalidDataException("坐标超出经纬度范围，请检查源坐标系。");
            return p;
        }
        static Geometry ReadGeometry(BinaryReader r, int type, Func<Coordinate, Coordinate> convert)
        {
            if (type == 1) return new Point(ReadPoint(r, convert));
            if (r.ReadBytes(32).Length != 32) throw new InvalidDataException("SHP 记录不完整。");
            int parts = type == 8 ? 0 : r.ReadInt32(), count = r.ReadInt32();
            if (count < 0 || parts < 0 || parts > count || 4L * parts + 16L * count > r.BaseStream.Length - r.BaseStream.Position)
                throw new InvalidDataException("SHP 顶点数量错误。");
            var offsets = new int[parts + 1];
            for (int i = 0; i < parts; i++) offsets[i] = r.ReadInt32();
            offsets[parts] = count;
            var points = new Points();
            for (int i = 0; i < count; i++) points.Add(ReadPoint(r, convert));
            if (type == 8) return new MultiPoint(points);
            if (parts == 0 && count != 0) throw new InvalidDataException("SHP 缺少部件。");
            var rings = new List<Points>();
            for (int i = 0; i < parts; i++)
            {
                if ((i == 0 && offsets[i] != 0) || offsets[i] < 0 || offsets[i + 1] <= offsets[i] || offsets[i + 1] > count)
                    throw new InvalidDataException("SHP 部件索引错误。");
                var ring = new Points();
                for (int j = offsets[i]; j < offsets[i + 1]; j++) ring.Add(points[j]);
                if (ring.Count < (type == 5 ? 4 : 2)) throw new InvalidDataException("部件顶点不足。");
                rings.Add(ring);
            }
            if (type == 3)
            {
                var lines = new MultiLineString();
                foreach (var ring in rings) lines.Parts.Add(new LineString(ring));
                return lines;
            }
            // 以包含层级处理洞和岛，不依赖环顺序。
            var polygons = rings.Select(p => new Polygon(p)).ToArray();
            int[] parents = Enumerable.Repeat(-1, rings.Count).ToArray();
            for (int i = 0; i < rings.Count; i++)
                for (int j = 0; j < rings.Count; j++)
                    if (polygons[j].Area > polygons[i].Area && polygons[j].Contains(rings[i][0]) &&
                        (parents[i] < 0 || polygons[j].Area < polygons[parents[i]].Area)) parents[i] = j;
            var multi = new MultiPolygon();
            for (int i = 0; i < rings.Count; i++)
            {
                int depth = 0;
                for (int p = parents[i]; p >= 0; p = parents[p]) depth++;
                if (depth % 2 == 0) multi.Parts.Add(polygons[i]);
                else polygons[parents[i]].Holes.Add(rings[i]);
            }
            return multi;
        }
        static void ReadDbf(string path, FeatureClass fc, List<Geometry> shapes)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("缺少同名 .dbf 属性文件。", path);
            using (var r = new BinaryReader(File.OpenRead(path)))
            {
                if (r.BaseStream.Length < 33) throw new InvalidDataException("DBF 文件头不完整。");
                r.BaseStream.Position = 4;
                int rows = r.ReadInt32(), header = r.ReadUInt16(), width = r.ReadUInt16();
                if (rows != shapes.Count || header < 33 || (header - 33) % 32 != 0 || header + (long)rows * width > r.BaseStream.Length)
                    throw new InvalidDataException("DBF 记录数或长度与 SHP 不匹配。");
                r.BaseStream.Position = 29; byte language = r.ReadByte();
                var encoding = Encoding.GetEncoding(language == 3 || language == 0x57 ? 1252 : 936);
                string cpg = Path.ChangeExtension(path, ".cpg");
                if (File.Exists(cpg))
                {
                    string name = File.ReadAllText(cpg).Trim().Trim('\uFEFF'); int code;
                    encoding = int.TryParse(name, out code) ? Encoding.GetEncoding(code) : Encoding.GetEncoding(name);
                }
                int fields = (header - 33) / 32;
                var lengths = new int[fields]; var types = new char[fields];
                for (int i = 0; i < fields; i++)
                {
                    r.BaseStream.Position = 32 + i * 32;
                    string name = encoding.GetString(r.ReadBytes(11)).Split('\0')[0].Trim();
                    char t = types[i] = (char)r.ReadByte();
                    r.ReadInt32(); lengths[i] = r.ReadByte();
                    if (string.IsNullOrEmpty(name) || fc.Fields.FindField(name) >= 0) name = "FIELD_" + (i + 1);
                    if ("CNFLDI".IndexOf(t) < 0) throw new NotSupportedException("不支持的 DBF 字段类型：" + t);
                    fc.Fields.Add(new Field(name, t == 'L' ? FieldTypeConstant.Boolean : t == 'D' ? FieldTypeConstant.Date :
                        t == 'I' ? FieldTypeConstant.Int32 : t == 'N' || t == 'F' ? FieldTypeConstant.Double : FieldTypeConstant.Text));
                }
                if (lengths.Sum() + 1 != width) throw new InvalidDataException("DBF 字段宽度错误。");
                for (int row = 0; row < rows; row++)
                {
                    r.BaseStream.Position = header + (long)row * width;
                    bool deleted = r.ReadByte() == '*'; var values = new object[fields];
                    for (int i = 0; i < fields; i++)
                    {
                        byte[] b = r.ReadBytes(lengths[i]);
                        if (deleted || shapes[row] == null) continue;
                        string s = encoding.GetString(b).Trim(' ', '\0');
                        if (types[i] == 'I')
                        {
                            if (b.Length != 4) throw new InvalidDataException("DBF 整数字段宽度错误。");
                            values[i] = BitConverter.ToInt32(b, 0);
                        }
                        else if (s.Length == 0 || s.All(c => c == '*') || s == "?") values[i] = null;
                        else if (types[i] == 'N' || types[i] == 'F') values[i] = double.Parse(s, CultureInfo.InvariantCulture);
                        else if (types[i] == 'D') values[i] = DateTime.ParseExact(s, "yyyyMMdd", CultureInfo.InvariantCulture);
                        else if (types[i] == 'L') values[i] = s.Equals("Y", StringComparison.OrdinalIgnoreCase) || s.Equals("T", StringComparison.OrdinalIgnoreCase);
                        else values[i] = s;
                    }
                    if (!deleted && shapes[row] != null) fc.Add(new Feature(shapes[row], new Attributes(fc.Fields, values)));
                }
            }
        }
    }
}
