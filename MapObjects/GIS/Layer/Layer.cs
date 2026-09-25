namespace GIS
{
    /// <summary>
    /// 图层 = 要素类 + 符号 + 可见性。
    /// 渲染器（Renderer）与注记由图层渲染模块在Symbol体系上扩展。
    /// </summary>
    public class Layer
    {
        private string _Name = "";                  //图层名称
        private FeatureClass _FeatureClass;         //图层的要素类
        private bool _Visible = true;               //图层是否可见
        private Symbol _Symbol;                     //图层的默认符号
        private Renderer _Renderer;                 //图层渲染器（模块3，可为null，null时用Symbol）
        private LabelRenderer _LabelRenderer;       //注记渲染器（模块3，可为null）
        private GeographicDatum _GeographicDatum = GeographicDatum.Wgs84;  //图层数据所属地理坐标系（默认WGS84）

        #region 构造函数

        /// <summary>
        /// 按要素类构造图层，图层名取要素类名称
        /// </summary>
        public Layer(FeatureClass featureClass)
        {
            _FeatureClass = featureClass;
            _Name = featureClass.Name;
        }

        /// <summary>
        /// 按名称与要素类构造图层
        /// </summary>
        public Layer(string name, FeatureClass featureClass)
        {
            _Name = name;
            _FeatureClass = featureClass;
        }

        #endregion

        #region 属性

        /// <summary>获取或设置图层名称</summary>
        public string Name
        {
            get { return _Name; }
            set { _Name = value; }
        }

        /// <summary>获取图层的要素类</summary>
        public FeatureClass FeatureClass
        {
            get { return _FeatureClass; }
        }

        /// <summary>获取或设置图层是否可见</summary>
        public bool Visible
        {
            get { return _Visible; }
            set { _Visible = value; }
        }

        /// <summary>获取或设置图层的默认符号</summary>
        public Symbol Symbol
        {
            get { return _Symbol; }
            set { _Symbol = value; }
        }

        /// <summary>获取或设置图层渲染器（可为null，null时使用 Symbol）</summary>
        public Renderer Renderer
        {
            get { return _Renderer; }
            set { _Renderer = value; }
        }

        /// <summary>获取或设置注记渲染器（可为null）</summary>
        public LabelRenderer LabelRenderer
        {
            get { return _LabelRenderer; }
            set { _LabelRenderer = value; }
        }

        /// <summary>
        /// 获取或设置该图层数据所属的地理坐标系（大地基准），默认 WGS84。
        /// 本系统默认只接受 WGS84 数据；若图层数据来自其他坐标系（北京54、GCJ-02 等），
        /// 应通过“投影到 WGS84”命令转换后再参与地图投影。
        /// </summary>
        public GeographicDatum GeographicDatum
        {
            get { return _GeographicDatum; }
            set { _GeographicDatum = value ?? GeographicDatum.Wgs84; }
        }

        /// <summary>该图层数据是否已是 WGS84。</summary>
        public bool IsWgs84
        {
            get { return _GeographicDatum == null || _GeographicDatum.IsWgs84; }
        }

        #endregion

        #region 经纬度原始数据（不随投影改变）

        /// <summary>
        /// 图层要素的 WGS84 经纬度原始几何，与 FeatureClass.Features 一一对应（深复制）。
        /// **经纬度是图层的固有属性**：它不随显示投影改变，每次切换投影都由这份快照重新投影，
        /// 因此不会出现“用一个投影推算另一个投影”造成的累积误差或数据损坏。
        /// </summary>
        private readonly System.Collections.Generic.List<Geometry> _GeographicGeometries =
            new System.Collections.Generic.List<Geometry>();

        /// <summary>获取 WGS84 经纬度原始几何（只读）。</summary>
        public System.Collections.Generic.IReadOnlyList<Geometry> GeographicGeometries
        {
            get { return _GeographicGeometries; }
        }

        /// <summary>经纬度快照是否与当前要素一一对应。</summary>
        public bool HasGeographicGeometries
        {
            get { return _GeographicGeometries.Count == _FeatureClass.Features.Count; }
        }

        /// <summary>
        /// 用当前要素几何建立/刷新 WGS84 经纬度快照。
        /// projection 为 null 表示当前几何本身就是经纬度；否则先把每个几何按该投影反算为经纬度。
        /// </summary>
        public void CaptureGeographic(ProjectionCS projection)
        {
            _GeographicGeometries.Clear();
            foreach (Feature feature in _FeatureClass.Features)
            {
                if (feature.Geometry == null) { _GeographicGeometries.Add(null); continue; }
                Geometry snapshot = feature.Geometry.Clone();
                TransformGeometry(snapshot, projection, null);
                _GeographicGeometries.Add(snapshot);
            }
        }

        /// <summary>
        /// 按经纬度快照重新生成要素几何：先把快照深复制一份，再整体投影到 projection
        /// （projection 为 null 表示直接使用经纬度）。每次调用都从快照出发，与上一次的投影无关。
        /// </summary>
        public void ApplyProjection(ProjectionCS projection)
        {
            int index = 0;
            foreach (Feature feature in _FeatureClass.Features)
            {
                Geometry snapshot = index < _GeographicGeometries.Count ? _GeographicGeometries[index] : null;
                index++;
                if (snapshot == null) continue;
                Geometry display = snapshot.Clone();
                TransformGeometry(display, null, projection);
                feature.Geometry = display;
            }
            _FeatureClass.ProjectionCS = projection;
        }

        /// <summary>经纬度快照的外包矩形（与当前显示投影无关）。</summary>
        public Envelope GetGeographicEnvelope()
        {
            var extent = new Envelope();
            foreach (Geometry geometry in _GeographicGeometries)
                if (geometry != null && !geometry.IsEmpty) extent.ExpandToInclude(geometry.GetEnvelope());
            return extent;
        }

        /// <summary>把几何对象的所有坐标就地转换到另一个坐标系（任一投影为 null 表示经纬度）。</summary>
        public static void TransformGeometry(Geometry geometry, ProjectionCS from, ProjectionCS to)
        {
            if (geometry == null) return;
            if (ReferenceEquals(from, to)) return;
            if (geometry is Point point) { point.Coordinate = Project(point.Coordinate, from, to); return; }
            if (geometry is LineString line) { TransformPoints(line.Points, from, to); return; }
            if (geometry is Polygon polygon)
            {
                TransformPoints(polygon.ExteriorRing, from, to);
                foreach (Points hole in polygon.Holes) TransformPoints(hole, from, to);
                return;
            }
            if (geometry is MultiPoint multiPoint) { TransformPoints(multiPoint.Points, from, to); return; }
            if (geometry is MultiLineString multiLine)
            {
                foreach (LineString part in multiLine.Parts) TransformPoints(part.Points, from, to);
                return;
            }
            if (geometry is MultiPolygon multiPolygon)
            {
                foreach (Polygon part in multiPolygon.Parts)
                {
                    TransformPoints(part.ExteriorRing, from, to);
                    foreach (Points hole in part.Holes) TransformPoints(hole, from, to);
                }
            }
        }

        private static void TransformPoints(Points points, ProjectionCS from, ProjectionCS to)
        {
            for (int i = 0; i < points.Count; i++)
                points.SetItem(i, Project(points[i], from, to));
        }

        private static Coordinate Project(Coordinate coordinate, ProjectionCS from, ProjectionCS to)
        {
            Coordinate lngLat = from == null ? coordinate : from.TransferToLngLat(coordinate);
            return to == null ? lngLat : to.TransferToProjCo(lngLat);
        }

        #endregion

        #region 方法

        /// <summary>
        /// 获取图层的外接矩形（当前显示坐标）
        /// </summary>
        public Envelope GetEnvelope()
        {
            return _FeatureClass.GetEnvelope();
        }

        #endregion
    }
}
