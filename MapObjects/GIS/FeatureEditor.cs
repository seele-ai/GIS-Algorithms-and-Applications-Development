using System;
using System.Collections.Generic;
using System.Linq;

namespace GIS
{
    /// <summary>以经纬度为编辑真值；提交、撤销和重做同时维护要素与坐标快照。</summary>
    public sealed class FeatureEditCommand
    {
        private sealed class State
        {
            public Feature[] Features;
            public Geometry[] Geometry;
            public Attributes[] Attributes;
        }
        private readonly Layer layer;
        private readonly State before, after;
        private bool applied;
        private FeatureEditCommand(Layer layer, State before, State after)
        { this.layer = layer; this.before = before; this.after = after; Apply(after); applied = true; }
        public static Geometry ReadGeometry(Layer layer, Feature feature)
        {
            int index = layer.FeatureClass.Features.ToList().IndexOf(feature);
            if (index < 0) throw new ArgumentException("要素已不在图层中。");
            if (layer.HasGeographicGeometries) return layer.GeographicGeometries[index]?.Clone();
            var geometry = feature.Geometry?.Clone();
            Layer.TransformGeometry(geometry, layer.FeatureClass.ProjectionCS, null);
            return geometry;
        }
        private static State Capture(Layer layer)
        {
            var features = layer.FeatureClass.Features.ToArray();
            return new State { Features = features, Geometry = features.Select((f,i) => {
                    if (layer.HasGeographicGeometries) return layer.GeographicGeometries[i]?.Clone();
                    var geometry = f.Geometry?.Clone(); Layer.TransformGeometry(geometry,layer.FeatureClass.ProjectionCS,null); return geometry;
                }).ToArray(),
                Attributes = features.Select(f => new Attributes(layer.FeatureClass.Fields, f.Attributes.ToArray())).ToArray() };
        }
        public static FeatureEditCommand Save(Layer layer, Feature feature, Geometry geographic, Attributes attributes)
        {
            if (!layer.IsWgs84) throw new ArgumentException("请先转换为 WGS84。");
            FeatureEditor.Validate(geographic);
            if (!layer.FeatureClass.IsGeometryTypeAllowed(geographic)) throw new ArgumentException("几何类型与图层不匹配。");
            if (attributes == null || attributes.Count != layer.FeatureClass.Fields.Count) throw new ArgumentException("属性字段数量不匹配。");
            var before = Capture(layer); var after = Capture(layer);
            int index = Array.IndexOf(after.Features, feature);
            if (index < 0) throw new ArgumentException("要素已不在图层中。");
            after.Geometry[index] = geographic.Clone();
            after.Attributes[index] = new Attributes(layer.FeatureClass.Fields, attributes.ToArray());
            return new FeatureEditCommand(layer, before, after);
        }
        public static FeatureEditCommand Delete(Layer layer, IEnumerable<Feature> features)
        {
            var removed = new HashSet<Feature>(features);
            var before = Capture(layer);
            if (removed.Count == 0 || removed.Any(f => !before.Features.Contains(f))) throw new ArgumentException("请选择当前图层中的要素。");
            var indexes = Enumerable.Range(0, before.Features.Length).Where(i => !removed.Contains(before.Features[i])).ToArray();
            var after = new State { Features = indexes.Select(i => before.Features[i]).ToArray(),
                Geometry = indexes.Select(i => before.Geometry[i]).ToArray(), Attributes = indexes.Select(i => before.Attributes[i]).ToArray() };
            return new FeatureEditCommand(layer, before, after);
        }
        public void Undo() { if (!applied) return; Verify(after); Apply(before); applied = false; }
        public void Redo() { if (applied) return; Verify(before); Apply(after); applied = true; }
        private void Verify(State expected)
        {
            var actual = Capture(layer);
            if (!actual.Features.SequenceEqual(expected.Features) || Enumerable.Range(0, actual.Features.Length).Any(i =>
                actual.Geometry[i]?.ToWKT() != expected.Geometry[i]?.ToWKT() || !actual.Attributes[i].ToArray().SequenceEqual(expected.Attributes[i].ToArray())))
                throw new InvalidOperationException("图层数据已被其他操作修改，不能撤销或重做这次编辑。");
        }
        private void Apply(State state)
        {
            // 在修改集合之前完成所有投影，避免失败时留下半提交数据。
            var display = state.Geometry.Select(g => g?.Clone()).ToArray();
            foreach (var g in display)
            {
                Layer.TransformGeometry(g, null, layer.FeatureClass.ProjectionCS);
                if (g != null && FeatureEditor.Parts(g).SelectMany(p => p).Any(c => !FeatureEditor.Finite(c.X) || !FeatureEditor.Finite(c.Y)))
                    throw new ArgumentException("编辑结果无法在当前投影显示。");
            }
            var fc = layer.FeatureClass;
            fc.Features.Clear();
            for (int i = 0; i < state.Features.Length; i++)
            {
                var feature = state.Features[i]; feature.Geometry = state.Geometry[i]?.Clone();
                feature.Attributes = new Attributes(fc.Fields, state.Attributes[i].ToArray()); fc.Features.Add(feature);
            }
            layer.CaptureGeographic(null);
            for (int i = 0; i < state.Features.Length; i++) state.Features[i].Geometry = display[i];
        }
    }

    public static class FeatureEditor
    {
        public static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        public static IEnumerable<Points> Parts(Geometry geometry)
        {
            if (geometry is Point point) { var points = new Points(); points.Add(point.Coordinate); yield return points; }
            else if (geometry is MultiPoint mp) yield return mp.Points;
            else if (geometry is LineString line) yield return line.Points;
            else if (geometry is MultiLineString ml) { foreach (var part in ml.Parts) yield return part.Points; }
            else if (geometry is Polygon polygon) { yield return polygon.ExteriorRing; foreach (var hole in polygon.Holes) yield return hole; }
            else if (geometry is MultiPolygon multi) { foreach (var part in multi.Parts) foreach (var ring in Parts(part)) yield return ring; }
        }
        public static bool IsRing(Geometry geometry) => geometry is Polygon || geometry is MultiPolygon;
        public static void Validate(Geometry geometry)
        {
            if (geometry == null || geometry.IsEmpty) throw new ArgumentException("几何不能为空。");
            var parts = Parts(geometry).ToArray();
            if (parts.Length == 0 || parts.SelectMany(p => p).Any(p => !Finite(p.X) || !Finite(p.Y) || Math.Abs(p.X) > 180 || Math.Abs(p.Y) > 90))
                throw new ArgumentException("经纬度必须是有限数值，经度在 ±180°、纬度在 ±90° 内。");
            bool ring = IsRing(geometry);
            foreach (var points in parts)
            {
                if (points.Count == 0) throw new ArgumentException("部件不能为空。");
                if (ring) ValidateRing(points);
                else if (!(geometry is Point) && !(geometry is MultiPoint) && points.Distinct().Count() < 2)
                    throw new ArgumentException("线至少需要两个不同顶点。");
            }
            var polygons = geometry is Polygon singlePolygon ? new[] { singlePolygon } : geometry is MultiPolygon m ? m.Parts.ToArray() : new Polygon[0];
            foreach (var polygon in polygons)
            {
                var outer = new Polygon(polygon.ExteriorRing);
                for (int i = 0; i < polygon.Holes.Count; i++)
                {
                    var hole = polygon.Holes[i];
                    if (!outer.Contains(hole[0]) || RingsIntersect(polygon.ExteriorRing, hole)) throw new ArgumentException("孔洞必须完全位于外环内部，不能接触外环。");
                    for (int j = 0; j < i; j++) if (Overlap(hole, polygon.Holes[j])) throw new ArgumentException("孔洞之间不能相交或嵌套。");
                }
            }
            for (int i = 0; i < polygons.Length; i++) for (int j = 0; j < i; j++)
            {
                var a = polygons[i]; var b = polygons[j];
                if (Parts(a).Any(x => Parts(b).Any(y => RingsIntersect(x,y))) || a.Contains(b.ExteriorRing[0]) || b.Contains(a.ExteriorRing[0]))
                    throw new ArgumentException("复合面的部件不能相交或重叠。");
            }
        }
        private static void ValidateRing(Points points)
        {
            if (points.Count < 4 || !points[0].Equals(points[points.Count-1]) || points.Take(points.Count-1).Distinct().Count() < 3)
                throw new ArgumentException("面环至少需要三个不同顶点并首尾闭合。");
            if (new Polygon(points).Area <= 0) throw new ArgumentException("面环面积不能为零。");
            int n = points.Count - 1;
            for (int i = 0; i < n; i++)
            {
                if (points[i].Equals(points[i+1])) throw new ArgumentException("相邻顶点不能重合。");
                var a = points[(i+n-1)%n]; var b = points[i]; var c = points[(i+1)%n];
                if (Cross(a,b,c) == 0 && (a.X-b.X)*(c.X-b.X)+(a.Y-b.Y)*(c.Y-b.Y) > 0) throw new ArgumentException("面边界不能折返重叠。");
                for (int j = i+2; j < n; j++) if (!(i == 0 && j == n-1) && Intersects(points[i],points[i+1],points[j],points[j+1]))
                    throw new ArgumentException("面边界不能自相交。");
            }
        }
        private static double Cross(Coordinate a, Coordinate b, Coordinate c) => (b.X-a.X)*(c.Y-a.Y)-(b.Y-a.Y)*(c.X-a.X);
        private static bool Intersects(Coordinate a, Coordinate b, Coordinate c, Coordinate d) =>
            Math.Max(Math.Min(a.X,b.X),Math.Min(c.X,d.X)) <= Math.Min(Math.Max(a.X,b.X),Math.Max(c.X,d.X)) &&
            Math.Max(Math.Min(a.Y,b.Y),Math.Min(c.Y,d.Y)) <= Math.Min(Math.Max(a.Y,b.Y),Math.Max(c.Y,d.Y)) &&
            Cross(a,b,c)*Cross(a,b,d) <= 0 && Cross(c,d,a)*Cross(c,d,b) <= 0;
        private static bool RingsIntersect(Points a, Points b) => Enumerable.Range(0,a.Count-1).Any(i => Enumerable.Range(0,b.Count-1).Any(j => Intersects(a[i],a[i+1],b[j],b[j+1])));
        private static bool Overlap(Points a, Points b) => RingsIntersect(a,b) || new Polygon(a).Contains(b[0]) || new Polygon(b).Contains(a[0]);
    }
}
