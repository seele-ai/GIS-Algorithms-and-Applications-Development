using System;
using System.Collections.Generic;
using System.Linq;
using NetTopologySuite.Algorithm;
using NetTopologySuite.Geometries;
using NetTopologySuite.LinearReferencing;
using NetTopologySuite.Operation.Polygonize;
using NetTopologySuite.Operation.Union;
using GIS.Toolbox.Tools.Proximity;
using G = NetTopologySuite.Geometries.Geometry;
using C = NetTopologySuite.Geometries.Coordinate;
using L = NetTopologySuite.Geometries.LineString;
using P = NetTopologySuite.Geometries.Polygon;

namespace GIS.Toolbox.Tools.DataManagement
{
    public enum ManagementOperation { Envelope, MinimumBoundary, ToLine, ToPoint, ToPolygon, Vertices }
    public sealed class ManagementResult
    {
        public FeatureClass FeatureClass;
        public string Note;
    }
    /// <summary>二维平面数据管理算法。所有操作生成新要素，绝不改写输入几何。</summary>
    public static class DataManagementEngine
    {
        private static readonly GeometryFactory Factory = new GeometryFactory();
        public static G ToNts(GIS.Geometry geometry) { return ProximityEngine.ToNts(geometry); }
        public static IEnumerable<L> Lines(G g)
        {
            if (g is L) { yield return (L)g; yield break; }
            var p = g as P;
            if (p != null)
            {
                yield return p.ExteriorRing;
                for (int i = 0; i < p.NumInteriorRings; i++) yield return p.GetInteriorRingN(i);
                yield break;
            }
            for (int i = 0; i < g.NumGeometries; i++)
                if (!ReferenceEquals(g, g.GetGeometryN(i)))
                    foreach (var line in Lines(g.GetGeometryN(i))) yield return line;
        }
        private static string FieldName(Fields fields, string name)
        {
            string candidate = name; int n = 1;
            while (fields.FindField(candidate) >= 0) candidate = name + "_" + n++;
            return candidate;
        }
        public static ManagementResult Run(ManagementOperation operation, IList<Feature> sources,
            FeatureClass schema, string outputName, int mode = 0, bool inside = false, bool groupAll = false)
        {
            if (schema == null || sources == null || sources.Count == 0) throw new ArgumentException("输入要素为空。");
            if (operation == ManagementOperation.MinimumBoundary && (mode < 0 || mode > 4) ||
                operation == ManagementOperation.Vertices && (mode < 0 || mode > 4))
                throw new ArgumentException("参数选项无效。");
            var geometries = new List<G>();
            for (int i = 0; i < sources.Count; i++)
            {
                var g = ToNts(sources[i].Geometry);
                if (g == null || g.IsEmpty) throw new ArgumentException("第 " + (i + 1) + " 个要素的几何为空。");
                if (g.Coordinates.Any(c => double.IsNaN(c.X) || double.IsNaN(c.Y) || double.IsInfinity(c.X) || double.IsInfinity(c.Y)))
                    throw new ArgumentException("输入含非有限坐标。");
                if (!g.IsValid) throw new ArgumentException("第 " + (i + 1) + " 个几何无效，请先修复。");
                if ((operation == ManagementOperation.ToLine || operation == ManagementOperation.ToPolygon || operation == ManagementOperation.Vertices) && (int)g.Dimension < 1)
                    throw new ArgumentException("本工具只接受线或面要素。");
                geometries.Add(g);
            }
            bool aggregate = operation == ManagementOperation.ToLine || operation == ManagementOperation.ToPolygon ||
                operation == ManagementOperation.MinimumBoundary && groupAll;
            var type = operation == ManagementOperation.ToPoint || operation == ManagementOperation.Vertices
                ? GeometryTypeConstant.Point : operation == ManagementOperation.ToLine ? GeometryTypeConstant.LineString : GeometryTypeConstant.Polygon;
            var output = new FeatureClass(outputName, type) { Srid = schema.Srid, ProjectionCS = schema.ProjectionCS };
            if (!aggregate) foreach (Field f in schema.Fields) output.Fields.Add(new Field(f.Name, f.ValueType, f.Alias));
            string origin = FieldName(output.Fields, aggregate ? "SOURCE_IDS" : "ORIG_FID");
            output.Fields.Add(new Field(origin, aggregate ? FieldTypeConstant.Text : FieldTypeConstant.Int32));
            int padded = 0;
            Action<G, int, string> add = (g, index, ids) =>
            {
                if (g == null || g.IsEmpty) return;
                var feature = new Feature(GeometryWkt.FromWKT(g.AsText()), output.Fields);
                if (!aggregate)
                    foreach (Field f in schema.Fields) feature.Attributes.SetItem(f.Name, sources[index].Attributes.GetItem(f.Name));
                feature.Attributes.SetItem(origin, aggregate ? (object)ids : index);
                output.Features.Add(feature);
            };
            if (operation == ManagementOperation.ToLine || operation == ManagementOperation.ToPolygon)
            {
                var linework = geometries.SelectMany(Lines).Cast<G>().ToList();
                G noded = UnaryUnionOp.Union(linework);
                if (operation == ManagementOperation.ToLine)
                {
                    var originalLines = geometries.Select(g => Factory.BuildGeometry(Lines(g).Cast<G>().ToList())).ToList();
                    foreach (L line in Lines(noded))
                    {
                        var ids = Enumerable.Range(0, geometries.Count).Where(i => originalLines[i].Covers(line)).Select(i => i.ToString());
                        add(line, 0, string.Join(",", ids));
                    }
                }
                else
                {
                    var polygonizer = new Polygonizer(); polygonizer.Add(noded);
                    foreach (P polygon in polygonizer.GetPolygons())
                    {
                        var ids = Enumerable.Range(0, geometries.Count).Where(i => geometries[i].Intersects(polygon.Boundary)).Select(i => i.ToString());
                        add(polygon, 0, string.Join(",", ids));
                    }
                    if (output.Features.Count == 0) throw new ArgumentException("输入线未围成闭合区域，没有可生成的面。");
                }
            }
            else
            {
                var work = groupAll && operation == ManagementOperation.MinimumBoundary
                    ? new List<G> { Factory.BuildGeometry(geometries) } : geometries;
                for (int i = 0; i < work.Count; i++)
                {
                    G g = work[i]; string ids = aggregate ? string.Join(",", Enumerable.Range(0, sources.Count)) : null;
                    if (operation == ManagementOperation.ToPoint)
                        add(inside ? g.InteriorPoint : g.Centroid, i, ids);
                    else if (operation == ManagementOperation.Vertices)
                    {
                        foreach (L line in Lines(g))
                        {
                            C[] coords = line.Coordinates;
                            if (mode == 0) foreach (C c in coords) add(Factory.CreatePoint(c.Copy()), i, ids);
                            if (mode == 1) add(Factory.CreatePoint(new LengthIndexedLine(line).ExtractPoint(line.Length / 2)), i, ids);
                            if (mode == 2 || mode == 4) add(Factory.CreatePoint(coords[0].Copy()), i, ids);
                            if (mode == 3 || mode == 4) add(Factory.CreatePoint(coords[coords.Length - 1].Copy()), i, ids);
                        }
                    }
                    else
                    {
                        G boundary;
                        if (operation == ManagementOperation.Envelope || mode == 4) boundary = g.Envelope;
                        else if (mode == 0) boundary = MinimumAreaRectangle.GetMinimumRectangle(g);
                        else if (mode == 1) boundary = MinimumDiameter.GetMinimumRectangle(g);
                        else if (mode == 2) boundary = g.ConvexHull();
                        else
                        {
                            // 外切 128 边形近似最小包围圆，避免内接多边形漏掉极值点。
                            var circle = new MinimumBoundingCircle(g); C centre = circle.GetCentre();
                            double radius = circle.GetRadius();
                            if (radius == 0) boundary = Factory.CreatePoint(centre);
                            else
                            {
                                int count = 128; double r = radius / Math.Cos(Math.PI / count);
                                var cs = new C[count + 1];
                                for (int k = 0; k < count; k++) cs[k] = new C(centre.X + r * Math.Cos(2 * Math.PI * k / count), centre.Y + r * Math.Sin(2 * Math.PI * k / count));
                                cs[count] = cs[0].Copy(); boundary = Factory.CreatePolygon(cs);
                            }
                        }
                        if ((int)boundary.Dimension < 2 || boundary.Area == 0)
                        {
                            // 点/共线数据没有非零面积边界，采用明确的微小容差以保持面输出。
                            var env = g.EnvelopeInternal.Copy();
                            double magnitude = Math.Max(Math.Max(Math.Abs(env.MinX), Math.Abs(env.MaxX)), Math.Max(Math.Abs(env.MinY), Math.Abs(env.MaxY)));
                            double epsilon = Math.Max(1e-8, Math.Max(Math.Max(env.Width, env.Height) * 1e-8, magnitude * 1e-14));
                            env.ExpandBy(epsilon); boundary = Factory.ToGeometry(env); padded++;
                        }
                        add(boundary, i, ids);
                    }
                }
            }
            return new ManagementResult { FeatureClass = output, Note = "二维平面计算；输出继承输入坐标系。" +
                (aggregate ? "来源索引记录在 " + origin + "（从 0 开始），聚合输出不复制单一来源属性。" : "保留输入属性；" + origin + " 为本次输入序号（从 0 开始）。") +
                (padded > 0 ? "有 " + padded + " 个退化边界使用微小容差扩展为面。" : "") +
                (operation == ManagementOperation.MinimumBoundary && mode == 3 ? "圆采用外切 128 边形近似。" : "") };
        }
    }
}
