using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace GIS.Display
{
    /// <summary>模块4草图交互，沿用旧 SimpleGIS 的操作图层、结束部件、完成后选中流程。</summary>
    public class FeatureCreationMap : MapControl
    {
        private readonly List<List<Coordinate>> parts = new List<List<Coordinate>>();
        private readonly List<Coordinate> current = new List<Coordinate>();
        private System.Drawing.Point pointer;
        public Layer CreationLayer { get; private set; }
        public bool IsCreating => CreationLayer != null;
        public int SketchVertexCount => current.Count + parts.Sum(p => p.Count);
        public Func<Fields, Attributes> RequestAttributes { get; set; }
        public event Action<string> SketchChanged;
        public event Action<Layer, Feature> FeatureCreated;
        public FeatureCreationMap()
        {
            OverlayPaint += DrawSketch;
            LayersChanged += (s, e) => { if (IsCreating && (!Layers.Contains(CreationLayer) || !CreationLayer.Visible || !IsLayerSelectable(CreationLayer))) EndCreation(); };
        }
        public new MapInteractionMode InteractionMode
        {
            get => base.InteractionMode;
            set { EndCreation(); base.InteractionMode = value; }
        }
        public void BeginCreation(Layer layer)
        {
            if (layer == null || !Layers.Contains(layer) || !layer.Visible || !IsLayerSelectable(layer))
                throw new InvalidOperationException("请选择一个可见、可选的目标图层。");
            if (layer.GeographicDatum.GeoCSName != GeographicDatum.Wgs84.GeoCSName)
                throw new InvalidOperationException("请先将图层转换为 WGS84。");
            EndCreation(); CreationLayer = layer; base.InteractionMode = MapInteractionMode.Select;
            Changed("创建：单击加点；双击/Enter 完成；Backspace 撤点；Esc 取消草图；中键平移。"); Focus();
        }
        public void EndCreation()
        {
            if (!IsCreating) return;
            current.Clear(); parts.Clear(); CreationLayer = null; Changed("已退出创建。");
        }
        public void CancelSketch() { current.Clear(); parts.Clear(); Changed("草图已取消，可重新描绘。"); }
        public void UndoVertex()
        {
            if (current.Count == 0 && parts.Count > 0)
            {
                current.AddRange(parts[parts.Count - 1]); parts.RemoveAt(parts.Count - 1);
                // 已结束的面部件含自动闭合点；重新打开时先去掉它，再撤销实际顶点。
                if (CreationLayer?.FeatureClass.GeometryType == GeometryTypeConstant.MultiPolygon &&
                    current.Count > 1 && Same(current[0], current[current.Count - 1])) current.RemoveAt(current.Count - 1);
            }
            if (current.Count > 0) current.RemoveAt(current.Count - 1);
            Changed("已撤销末尾顶点。");
        }
        public void AddSketchVertex(Coordinate coordinate)
        {
            if (!IsCreating) return;
            if (!Finite(coordinate.X) || !Finite(coordinate.Y)) throw new ArgumentException("坐标无效。");
            if (current.Count == 0 || !Same(current[current.Count - 1], coordinate)) current.Add(coordinate);
            Changed("草图 " + SketchVertexCount + " 个顶点；双击或 Enter 完成。");
            if (CreationLayer.FeatureClass.GeometryType == GeometryTypeConstant.Point) FinishSketch();
        }
        public void FinishPart()
        {
            if (!IsCreating) return;
            var type = CreationLayer.FeatureClass.GeometryType;
            if (type != GeometryTypeConstant.MultiLineString && type != GeometryTypeConstant.MultiPolygon)
            { Changed("只有复合线、复合面支持结束部件。"); return; }
            if (current.Count == 0) { Changed("请先绘制当前部件的顶点；已有部件可直接完成要素。"); return; }
            try { ValidatePart(current, type == GeometryTypeConstant.MultiPolygon); }
            catch (ArgumentException ex) { Changed(ex.Message); return; }
            var completed = new List<Coordinate>(current);
            if (type == GeometryTypeConstant.MultiPolygon && !Same(completed[0], completed[completed.Count - 1]))
                completed.Add(completed[0]);
            parts.Add(completed); current.Clear();
            Changed(type == GeometryTypeConstant.MultiPolygon
                ? "面部件已自动闭合，可开始下一个部件或完成要素。"
                : "部件已结束，可开始下一个部件或完成要素。");
        }
        public bool FinishSketch()
        {
            if (!IsCreating || SketchVertexCount == 0) return false;
            try
            {
                Layer target = CreationLayer;
                Geometry geometry = BuildGeometry(target.FeatureClass.GeometryType, parts, current);
                Geometry geographic = geometry.Clone();
                Layer.TransformGeometry(geographic, target.FeatureClass.ProjectionCS, null);
                foreach (Coordinate p in Vertices(geographic))
                    if (!Finite(p.X) || !Finite(p.Y) || Math.Abs(p.X) > 180 || Math.Abs(p.Y) > 90)
                        throw new ArgumentException("草图超出有效经纬度范围，请调整视图或投影。");
                // 反算在投影有效范围之外可能被钳位；必须能回到原始点击位置。
                Geometry roundTrip = geographic.Clone(); Layer.TransformGeometry(roundTrip, null, target.FeatureClass.ProjectionCS);
                var before = Vertices(geometry).ToArray(); var after = Vertices(roundTrip).ToArray();
                double tolerance = target.FeatureClass.ProjectionCS == null ? 1e-8 : Math.Max(0.01, Transform.MapUnitsPerPixel() * 0.1);
                if (before.Where((p, i) => Math.Abs(p.X - after[i].X) > tolerance || Math.Abs(p.Y - after[i].Y) > tolerance).Any())
                    throw new ArgumentException("当前位置超出当前投影的可靠反算范围，请选择合适分带后重新创建。");
                Attributes attributes = RequestAttributes?.Invoke(target.FeatureClass.Fields);
                if (attributes == null) { Changed("属性录入已取消，草图保留；可继续修改或取消。"); return false; }
                if (!Layers.Contains(target) || !target.Visible || !IsLayerSelectable(target)) throw new ArgumentException("目标图层已不可编辑。");
                var feature = AddFeature(target, geographic, attributes);
                current.Clear(); parts.Clear();
                ClearSelection(); var selection = new Features(); selection.Add(feature);
                SetSelection(target, selection, SelectMethodConstant.CreateNew); RefreshMap();
                FeatureCreated?.Invoke(target, feature); Changed("已创建并选中要素；可继续描绘，退出创建后点击“保存编辑”确认当前页面的修改。");
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is OverflowException)
            { Changed(ex.Message); return false; }
        }
        /// <summary>保留已有经纬度快照和显示几何，新要素单独从 WGS84 正算。</summary>
        public static Feature AddFeature(Layer layer, Geometry geographic, Attributes attributes)
        {
            var fc = layer.FeatureClass;
            if (!fc.IsGeometryTypeAllowed(geographic)) throw new ArgumentException("草图类型与图层不匹配。");
            if (attributes == null || attributes.Count != fc.Fields.Count) throw new ArgumentException("属性字段数量不匹配。");
            Geometry display = geographic.Clone(); Layer.TransformGeometry(display, null, fc.ProjectionCS);
            var oldDisplay = fc.Features.Select(f => f.Geometry).ToArray();
            if (!layer.HasGeographicGeometries) layer.CaptureGeographic(fc.ProjectionCS);
            var oldGeographic = layer.GeographicGeometries.ToArray();
            var feature = new Feature(geographic, attributes);
            try
            {
                for (int i = 0; i < oldDisplay.Length; i++) fc.Features[i].Geometry = oldGeographic[i];
                fc.Add(feature); layer.CaptureGeographic(null);
            }
            catch { fc.Features.Remove(feature); throw; }
            finally { for (int i = 0; i < oldDisplay.Length; i++) fc.Features[i].Geometry = oldDisplay[i]; }
            feature.Geometry = display;
            return feature;
        }
        public static Geometry BuildGeometry(GeometryTypeConstant type, IEnumerable<List<Coordinate>> completed, IList<Coordinate> active)
        {
            var all = completed.Select(p => new List<Coordinate>(p)).ToList();
            if (active.Count > 0) all.Add(new List<Coordinate>(active));
            if (all.Count == 0) throw new ArgumentException("请先描绘顶点。");
            if (type == GeometryTypeConstant.Point) return new Point(all[0][0]);
            if (type == GeometryTypeConstant.MultiPoint) return new MultiPoint(ToPoints(all.SelectMany(p => p)));
            bool polygon = type == GeometryTypeConstant.Polygon || type == GeometryTypeConstant.MultiPolygon;
            foreach (var part in all) ValidatePart(part, polygon);
            if (type == GeometryTypeConstant.LineString) return new LineString(ToPoints(all.Single()));
            if (type == GeometryTypeConstant.Polygon) return MakePolygon(all.Single());
            if (type == GeometryTypeConstant.MultiLineString)
            { var result = new MultiLineString(); foreach (var part in all) result.Parts.Add(new LineString(ToPoints(part))); return result; }
            var multi = new MultiPolygon();
            foreach (var part in all)
            {
                var polygonPart = MakePolygon(part);
                foreach (var previous in multi.Parts)
                {
                    var a = previous.ExteriorRing; var b = polygonPart.ExteriorRing;
                    bool overlap = previous.Contains(b[0]) || polygonPart.Contains(a[0]);
                    for (int i = 0; i < a.Count-1 && !overlap; i++) for (int j = 0; j < b.Count-1 && !overlap; j++)
                        overlap = Intersects(a[i], a[i+1], b[j], b[j+1]);
                    if (overlap) throw new ArgumentException("复合面的各部件须彼此分离；本工具不支持以嵌套环创建孔洞。");
                }
                multi.Parts.Add(polygonPart);
            }
            return multi;
        }
        private static Polygon MakePolygon(List<Coordinate> points)
        { var ring = ToPoints(points); if (!Same(ring[0], ring[ring.Count - 1])) ring.Add(ring[0]); return new Polygon(ring); }
        private static Points ToPoints(IEnumerable<Coordinate> source) { var p = new Points(); foreach (var c in source) p.Add(c); return p; }
        private static void ValidatePart(IList<Coordinate> points, bool polygon)
        {
            var p = points.ToList();
            if (polygon && p.Count > 1 && Same(p[0], p[p.Count - 1])) p.RemoveAt(p.Count - 1);
            if (p.Distinct().Count() < (polygon ? 3 : 2)) throw new ArgumentException(polygon ? "面至少需要三个不同顶点。" : "线至少需要两个不同顶点。");
            if (!polygon) return;
            if (MakePolygon(p).Area <= 0) throw new ArgumentException("不能创建面积为零的面。");
            for (int i = 0; i < p.Count; i++) for (int j = i + 1; j < p.Count; j++)
            {
                if (j == i + 1 || (i == 0 && j == p.Count - 1)) continue;
                if (Intersects(p[i], p[(i + 1) % p.Count], p[j], p[(j + 1) % p.Count]))
                    throw new ArgumentException("面边界自相交，请撤销顶点后重新描绘。");
            }
        }
        private static double Cross(Coordinate a, Coordinate b, Coordinate c) => (b.X-a.X)*(c.Y-a.Y)-(b.Y-a.Y)*(c.X-a.X);
        private static bool Intersects(Coordinate a, Coordinate b, Coordinate c, Coordinate d)
        {
            return Math.Max(Math.Min(a.X,b.X),Math.Min(c.X,d.X)) <= Math.Min(Math.Max(a.X,b.X),Math.Max(c.X,d.X)) &&
                Math.Max(Math.Min(a.Y,b.Y),Math.Min(c.Y,d.Y)) <= Math.Min(Math.Max(a.Y,b.Y),Math.Max(c.Y,d.Y)) &&
                Cross(a,b,c)*Cross(a,b,d) <= 0 && Cross(c,d,a)*Cross(c,d,b) <= 0;
        }
        private static IEnumerable<Coordinate> Vertices(Geometry g)
        {
            if (g is Point p) return new[] { p.Coordinate };
            if (g is MultiPoint mp) return mp.Points;
            if (g is LineString l) return l.Points;
            if (g is MultiLineString ml) return ml.Parts.SelectMany(x => x.Points);
            if (g is Polygon poly) return poly.ExteriorRing;
            return ((MultiPolygon)g).Parts.SelectMany(x => x.ExteriorRing);
        }
        private static bool Same(Coordinate a, Coordinate b) => a.X == b.X && a.Y == b.Y;
        private static bool Finite(double x) => !double.IsNaN(x) && !double.IsInfinity(x);
        private void Changed(string text) { Invalidate(); SketchChanged?.Invoke(text); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (!IsCreating || e.Button == MouseButtons.Middle) { base.OnMouseDown(e); return; }
            Focus(); pointer = e.Location;
            if (e.Button == MouseButtons.Left && e.Clicks == 1) AddSketchVertex(Transform.ScreenToMap(e.Location));
            if (e.Button == MouseButtons.Right) FinishSketch();
        }
        protected override void OnMouseDoubleClick(MouseEventArgs e)
        { if (IsCreating && e.Button == MouseButtons.Left) FinishSketch(); else base.OnMouseDoubleClick(e); }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); pointer = e.Location; if (IsCreating) Invalidate(); }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (IsCreating)
            {
                if (keyData == Keys.Escape) { base.ProcessCmdKey(ref msg, keyData); CancelSketch(); return true; }
                if (keyData == Keys.Back) { UndoVertex(); return true; }
                if (keyData == Keys.Enter) { FinishSketch(); return true; }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        private void DrawSketch(object sender, MapOverlayEventArgs e)
        {
            if (!IsCreating) return;
            using (var pen = new Pen(Color.OrangeRed, 2))
            using (var brush = new SolidBrush(Color.OrangeRed))
            {
                bool pointsOnly = CreationLayer.FeatureClass.GeometryType == GeometryTypeConstant.Point || CreationLayer.FeatureClass.GeometryType == GeometryTypeConstant.MultiPoint;
                foreach (var part in parts.Concat(new[] { current }))
                {
                    var screen = part.Select(p => e.Transform.MapToScreen(p)).ToArray();
                    if (!pointsOnly && screen.Length > 1) e.Graphics.DrawLines(pen, screen);
                    foreach (var p in screen) e.Graphics.FillRectangle(brush, p.X - 3, p.Y - 3, 6, 6);
                }
                if (!pointsOnly && current.Count > 0)
                { pen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash; e.Graphics.DrawLine(pen, e.Transform.MapToScreen(current.Last()), pointer); }
            }
        }
    }
}
