using System;
using System.Collections.Generic;
using System.Linq;
using NetTopologySuite.Geometries;
using G = NetTopologySuite.Geometries.Geometry;
using C = NetTopologySuite.Geometries.Coordinate;
using L = NetTopologySuite.Geometries.LineString;
using P = NetTopologySuite.Geometries.Polygon;

namespace GIS.Toolbox.Tools.DataManagement
{
    /// <summary>白宇丹：复合要素拆分、折点分线及 Douglas-Peucker 概化。</summary>
    public static class FeatureProcessingEngine
    {
        private static readonly GeometryFactory Factory = new GeometryFactory();
        public static ManagementResult Run(ManagementOperation operation, IList<Feature> sources,
            FeatureClass schema, string name, double tolerance = 0)
        {
            if (operation < ManagementOperation.MultipartToSinglepart || operation > ManagementOperation.Generalize)
                throw new ArgumentException("不支持的操作。");
            if (schema == null || sources == null || sources.Count == 0) throw new ArgumentException("输入要素为空。");
            if (double.IsNaN(tolerance) || double.IsInfinity(tolerance) || tolerance < 0)
                throw new ArgumentException("容差必须为有限的非负数。");
            var geometries = sources.Select(f => f == null ? null : DataManagementEngine.ToNts(f.Geometry)).ToList();
            foreach (G g in geometries)
            {
                if (g == null || g.IsEmpty || !g.IsValid) throw new ArgumentException("输入含空或无效几何，请先修复。");
                if (g.Coordinates.Any(c => double.IsNaN(c.X) || double.IsNaN(c.Y) || double.IsInfinity(c.X) || double.IsInfinity(c.Y)))
                    throw new ArgumentException("输入含非有限坐标。");
                if (operation != ManagementOperation.MultipartToSinglepart && (int)g.Dimension < 1)
                    throw new ArgumentException("此工具只接受线或面。");
                if (g.Dimension != geometries[0].Dimension) throw new ArgumentException("输入必须属于相同几何维度。");
            }
            var type = operation == ManagementOperation.SplitAtVertices ? GeometryTypeConstant.LineString :
                (int)geometries[0].Dimension == 0 ? GeometryTypeConstant.Point :
                (int)geometries[0].Dimension == 1 ? GeometryTypeConstant.LineString : GeometryTypeConstant.Polygon;
            var output = new FeatureClass(name, type) { Srid = schema.Srid, ProjectionCS = schema.ProjectionCS };
            foreach (Field f in schema.Fields) output.Fields.Add(new Field(f.Name, f.ValueType, f.Alias));
            string origin = "ORIG_FID"; int suffix = 1;
            while (output.Fields.FindField(origin) >= 0) origin = "ORIG_FID_" + suffix++;
            output.Fields.Add(new Field(origin, FieldTypeConstant.Int32));
            int fallback = 0, skipped = 0;
            Action<G, int> add = (g, i) => {
                var f = new Feature(GeometryWkt.FromWKT(g.AsText()), output.Fields);
                foreach (Field field in schema.Fields) f.Attributes.SetItem(field.Name, sources[i].Attributes.GetItem(field.Name));
                f.Attributes.SetItem(origin, i); output.Add(f);
            };
            for (int i = 0; i < geometries.Count; i++)
            {
                G g = geometries[i];
                if (operation == ManagementOperation.MultipartToSinglepart)
                    for (int j = 0; j < g.NumGeometries; j++) add(g.GetGeometryN(j), i);
                else if (operation == ManagementOperation.SplitAtVertices)
                    foreach (L line in DataManagementEngine.Lines(g))
                    {
                        C[] cs = line.Coordinates;
                        for (int j = 1; j < cs.Length; j++)
                        {
                            if (cs[j - 1].Equals2D(cs[j])) { skipped++; continue; }
                            add(Factory.CreateLineString(new[] { cs[j - 1].Copy(), cs[j].Copy() }), i);
                        }
                    }
                else
                {
                    G simplified = Simplify(g, tolerance);
                    if (simplified.IsEmpty || !simplified.IsValid || simplified.Dimension != g.Dimension)
                    { simplified = g; fallback++; }
                    add(simplified, i);
                }
            }
            return new ManagementResult { FeatureClass = output, Note = "保留原属性及坐标系；" + origin +
                " 为本次输入序号（从0开始）。\r\n" +
                (operation == ManagementOperation.Generalize ? "容差采用输入坐标单位；概化仅删除原顶点，不保证相邻要素公共边界一致。保留原几何：" + fallback + " 个。" :
                 operation == ManagementOperation.SplitAtVertices ? "包含面外环和孔洞边界；跳过重复顶点形成的零长度段：" + skipped + " 个。" : "面孔洞随所属面保留，不作为独立面拆分。") };
        }
        // 迭代实现避免长折线递归栈溢出；距离是点到有限线段的距离。
        private static C[] Reduce(C[] cs, double tolerance)
        {
            if (cs.Length <= 2 || tolerance == 0) return cs.Select(c => c.Copy()).ToArray();
            var keep = new bool[cs.Length]; keep[0] = keep[cs.Length - 1] = true;
            var stack = new Stack<Tuple<int,int>>(); stack.Push(Tuple.Create(0, cs.Length - 1));
            while (stack.Count > 0)
            {
                var pair = stack.Pop(); int best = -1; double distance = tolerance;
                var segment = new LineSegment(cs[pair.Item1], cs[pair.Item2]);
                for (int i = pair.Item1 + 1; i < pair.Item2; i++)
                { double d = segment.Distance(cs[i]); if (d > distance) { distance = d; best = i; } }
                if (best < 0) continue;
                keep[best] = true; stack.Push(Tuple.Create(pair.Item1, best)); stack.Push(Tuple.Create(best, pair.Item2));
            }
            return cs.Where((c, i) => keep[i]).Select(c => c.Copy()).ToArray();
        }
        private static C[] ReduceRing(C[] cs, double tolerance)
        {
            if (tolerance == 0 || cs.Length <= 4) return cs.Select(c => c.Copy()).ToArray();
            int split = 1;
            for (int i = 2; i < cs.Length - 1; i++) if (cs[0].Distance(cs[i]) > cs[0].Distance(cs[split])) split = i;
            var a = Reduce(cs.Take(split + 1).ToArray(), tolerance);
            var b = Reduce(cs.Skip(split).ToArray(), tolerance);
            var result = a.Concat(b.Skip(1)).ToArray();
            return result.Length >= 4 ? result : cs.Select(c => c.Copy()).ToArray();
        }
        private static G Simplify(G g, double tolerance)
        {
            var p = g as P;
            if (p != null)
            {
                var shell = Factory.CreateLinearRing(ReduceRing(p.ExteriorRing.Coordinates, tolerance));
                var holes = Enumerable.Range(0, p.NumInteriorRings).Select(i => Factory.CreateLinearRing(ReduceRing(p.GetInteriorRingN(i).Coordinates, tolerance))).ToArray();
                return Factory.CreatePolygon(shell, holes);
            }
            var line = g as L;
            if (line != null) return Factory.CreateLineString(line.IsClosed ? ReduceRing(line.Coordinates, tolerance) : Reduce(line.Coordinates, tolerance));
            if (g is NetTopologySuite.Geometries.MultiLineString) return Factory.CreateMultiLineString(Enumerable.Range(0, g.NumGeometries).Select(i => (L)Simplify(g.GetGeometryN(i), tolerance)).ToArray());
            if (g is NetTopologySuite.Geometries.MultiPolygon) return Factory.CreateMultiPolygon(Enumerable.Range(0, g.NumGeometries).Select(i => (P)Simplify(g.GetGeometryN(i), tolerance)).ToArray());
            throw new ArgumentException("不支持的概化类型。");
        }
    }
}

