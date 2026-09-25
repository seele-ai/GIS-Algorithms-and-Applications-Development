using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using ScreenPoint = System.Drawing.Point;

namespace GIS.Display.UI
{
    /// <summary>分带方式：高斯-克吕格 3 度带 / 高斯-克吕格 6 度带 / UTM。</summary>
    public enum ZoneSystem
    {
        GaussKruger3 = 0,
        GaussKruger6 = 1,
        Utm = 2
    }

    /// <summary>
    /// 球体分带选择控件。用正交投影把地球画成球体，球面上只绘制经纬线（不绘制海岸线等地物），
    /// 并叠加投影分带与数据外包矩形：单击球面即选中该处所属的分带，按住左键拖动可旋转地球。
    /// </summary>
    public sealed class ZoneGlobe : Control
    {
        private const double D2R = Math.PI / 180.0;
        private const double R2D = 180.0 / Math.PI;

        private ZoneSystem kind = ZoneSystem.Utm;
        private int zone = 50;
        private int rowIndex = 14;          // UTM 纬度行（默认 S 行：32°N~40°N）
        private Envelope dataExtent;        // 所有要素的外包矩形（经纬度）

        private double viewLon = 105;       // 视图中心经度
        private double viewLat = 20;        // 视图中心纬度

        private float cx, cy, r;
        private bool dragging;
        private ScreenPoint dragStart;
        private double dragLon;
        private double dragLat;
        private bool moved;

        // 只与经度/纬度有关的固定经纬网（预先算好，绘制时只做旋转投影，避免每次重算）
        private static readonly double[] MeridianSamples = BuildSamples(-180, 180, 5);
        private static readonly double[] ParallelSamples = BuildSamples(-88, 88, 4);
        private static readonly double[][] Meridians = BuildMeridians();
        private static readonly double[][] Parallels = BuildParallels();

        /// <summary>用户在地球上选择了新的分带时触发。</summary>
        public event EventHandler ZonePicked;

        public ZoneGlobe()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.FromArgb(247, 248, 251);
            Cursor = Cursors.Hand;
            TabStop = false;
        }

        /// <summary>分带方式。</summary>
        public ZoneSystem Kind
        {
            get { return kind; }
            set { if (kind == value) return; kind = value; Invalidate(); }
        }

        /// <summary>视图中心经度（改变后立即重绘）。</summary>
        public double ViewLon
        {
            get { return viewLon; }
            set
            {
                double v = wrap(value);
                if (Math.Abs(viewLon - v) < 1e-9) return;
                viewLon = v;
                Invalidate();
                RaiseViewChanged();
            }
        }

        /// <summary>视图中心纬度（改变后立即重绘）。</summary>
        public double ViewLat
        {
            get { return viewLat; }
            set
            {
                double v = Math.Max(-85, Math.Min(85, value));
                if (Math.Abs(viewLat - v) < 1e-9) return;
                viewLat = v;
                Invalidate();
                RaiseViewChanged();
            }
        }

        /// <summary>把视图移动到指定经纬度（用于选择分带后自动定位）。</summary>
        public void CenterOn(double lon, double lat)
        {
            bool changed = false;
            double v = wrap(lon);
            if (Math.Abs(viewLon - v) >= 1e-9) { viewLon = v; changed = true; }
            double la = Math.Max(-85, Math.Min(85, lat));
            if (Math.Abs(viewLat - la) >= 1e-9) { viewLat = la; changed = true; }
            if (!changed) return;
            Invalidate();
            RaiseViewChanged();
        }

        private void RaiseViewChanged()
        {
            var handler = ViewChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        /// <summary>当前选中的带号。</summary>
        public int Zone
        {
            get { return zone; }
            set
            {
                int v = Math.Max(MinZone(kind), Math.Min(MaxZone(kind), value));
                if (v == zone) return;
                zone = v;
                Invalidate();
            }
        }

        /// <summary>UTM 纬度行序号（0 为 C 行）。高斯-克吕格不使用。</summary>
        public int RowIndex
        {
            get { return rowIndex; }
            set
            {
                int v = Math.Max(0, Math.Min(UtmGridZone.RowCount - 1, value));
                if (v == rowIndex) return;
                rowIndex = v;
                Invalidate();
            }
        }

        /// <summary>所有要素合在一起的外包矩形（经纬度），非空时绘制在球面上，便于用户选取。</summary>
        public Envelope DataExtent
        {
            get { return dataExtent; }
            set { dataExtent = value; Invalidate(); }
        }

        #region 分带数学

        public static double BandWidth(ZoneSystem k) { return k == ZoneSystem.GaussKruger3 ? 3.0 : 6.0; }

        /// <summary>允许的最小带号。高斯-克吕格向东西两侧延伸（西半球为负带号），UTM 固定 1~60。</summary>
        public static int MinZone(ZoneSystem k)
        {
            if (k == ZoneSystem.Utm) return 1;
            return k == ZoneSystem.GaussKruger3 ? -60 : -29;   // 3度带覆盖 ±180°，6度带覆盖 -174°~180°
        }

        /// <summary>允许的最大带号。</summary>
        public static int MaxZone(ZoneSystem k)
        {
            if (k == ZoneSystem.Utm) return 60;
            return k == ZoneSystem.GaussKruger3 ? 60 : 30;
        }

        /// <summary>该带的起始经度（西边界）。</summary>
        public static double ZoneStart(ZoneSystem k, int z)
        {
            if (k == ZoneSystem.Utm) return -180.0 + 6.0 * (z - 1);
            if (k == ZoneSystem.GaussKruger6) return 6.0 * z - 6.0;
            return 3.0 * z - 1.5;
        }

        /// <summary>该带的中央经线。</summary>
        public static double ZoneCentral(ZoneSystem k, int z)
        {
            if (k == ZoneSystem.Utm) return 6.0 * z - 183.0;
            if (k == ZoneSystem.GaussKruger6) return 6.0 * z - 3.0;
            return 3.0 * z;
        }

        /// <summary>
        /// 由经度反算带号。高斯-克吕格允许**负带号**（西半球）：带号 n 的中央经线为 3n（3度带）
        /// 或 6n-3（6度带），因此西经同样可以直接分带并被点击选中。
        /// </summary>
        public static int ZoneOf(ZoneSystem k, double lon)
        {
            while (lon > 180) lon -= 360;
            while (lon < -180) lon += 360;
            int z;
            if (k == ZoneSystem.GaussKruger3) z = (int)Math.Round(lon / 3.0);
            else if (k == ZoneSystem.GaussKruger6) z = (int)Math.Floor(lon / 6.0) + 1;
            else z = (int)Math.Floor((lon + 180.0) / 6.0) + 1;
            return Math.Max(MinZone(k), Math.Min(MaxZone(k), z));
        }

        public static string CentralText(double cm)
        {
            if (Math.Abs(cm) < 1e-9) return "0°";
            return Math.Abs(cm).ToString("0.#") + (cm > 0 ? "°E" : "°W");
        }

        /// <summary>带号说明（负数表示西半球）。</summary>
        public static string ZoneText(ZoneSystem k, int z)
        {
            if (k == ZoneSystem.Utm) return z.ToString();
            return z + (z < 0 ? " 带（西半球）" : (z > 0 ? " 带（东半球）" : " 带（0°附近）"));
        }

        #endregion

        #region 正交投影正反算

        private double Depth(double lon, double lat)
        {
            double la = lat * D2R, la0 = ViewLat * D2R, dlo = (lon - ViewLon) * D2R;
            return Math.Sin(la0) * Math.Sin(la) + Math.Cos(la0) * Math.Cos(la) * Math.Cos(dlo);
        }

        private void Project(double lon, double lat, out float x, out float y)
        {
            double la = lat * D2R, la0 = ViewLat * D2R, dlo = (lon - ViewLon) * D2R;
            double cl = Math.Cos(la), sl = Math.Sin(la);
            double cl0 = Math.Cos(la0), sl0 = Math.Sin(la0);
            double vx = cl * Math.Sin(dlo);
            double vy = cl0 * sl - sl0 * cl * Math.Cos(dlo);
            x = (float)(cx + r * vx);
            y = (float)(cy - r * vy);
        }

        private void Unproject(double px, double py, out double lon, out double lat)
        {
            double x = (px - cx) / r, y = (cy - py) / r;
            double rho = Math.Sqrt(x * x + y * y);
            double la0 = ViewLat * D2R;
            if (rho < 1e-9) { lon = ViewLon; lat = ViewLat; return; }
            if (rho > 1) rho = 1;
            double c = Math.Asin(rho), sc = Math.Sin(c), cc = Math.Cos(c);
            double s = cc * Math.Sin(la0) + y * sc * Math.Cos(la0) / rho;
            s = Math.Max(-1, Math.Min(1, s));
            lat = Math.Asin(s) * R2D;
            lon = ViewLon + Math.Atan2(x * sc, rho * cc * Math.Cos(la0) - y * sc * Math.Sin(la0)) * R2D;
            while (lon > 180) lon -= 360;
            while (lon < -180) lon += 360;
        }

        // 求两点之间 Depth = 0 的位置（即地平圈上的交点）
        private PointF Crossing(double lon1, double lat1, double lon2, double lat2)
        {
            double lo2 = lon2;
            if (lo2 - lon1 > 180) lo2 -= 360;
            else if (lon1 - lo2 > 180) lo2 += 360;
            double loa = lon1, laa = lat1, lob = lo2, lab = lat2;
            for (int i = 0; i < 14; i++)
            {
                double lo = (loa + lob) / 2, la = (laa + lab) / 2;
                if (Depth(lo, la) > 0) { loa = lo; laa = la; } else { lob = lo; lab = la; }
            }
            float px, py;
            Project((loa + lob) / 2, (laa + lab) / 2, out px, out py);
            return new PointF(px, py);
        }

        // 可见半球内的一段折线。StartedAtHorizon/EndedAtHorizon 标记该段的首尾是否落在
        // 地平圈（球面边缘）上——这种段在填充时必须沿边缘闭合，否则会退化成一条弦（扇形/饼状）。
        private sealed class GeoRun
        {
            public readonly List<PointF> Points = new List<PointF>();
            public bool StartedAtHorizon;
            public bool EndedAtHorizon;
        }

        // 把一段经纬度折线裁剪到可见半球后描边（fill 非空时同时填充闭合区域）
        private void DrawGeo(Graphics g, double[] lons, double[] lats, Brush fill, Pen stroke)
        {
            DrawGeo(g, lons, lats, fill, stroke, null);
        }

        // band 非空时表示这是一条经纬度矩形带（lon1,lon2,lat1,lat2）：
        // 被地平圈裁掉的可见部分会沿球面边缘补上弧线，只在地球表面绘制。
        private void DrawGeo(Graphics g, double[] lons, double[] lats, Brush fill, Pen stroke, double[] band)
        {
            int n = lons.Length;
            if (n < 2) return;
            var runs = SplitVisibleRuns(lons, lats);
            for (int k = 0; k < runs.Count; k++)
            {
                GeoRun run = runs[k];
                if (fill != null && band != null && run.StartedAtHorizon && run.EndedAtHorizon)
                    AppendLimbArc(run.Points, band);
                PointF[] pts = run.Points.ToArray();
                if (pts.Length < 2) continue;
                if (fill != null && pts.Length >= 3)
                {
                    try { g.FillPolygon(fill, pts); } catch (ArgumentException) { }
                }
                if (stroke != null) g.DrawLines(stroke, pts);
            }
        }

        // 把折线按可见半球（Depth > 0）切成若干段，并记录每段是否为地平圈交点起止
        private List<GeoRun> SplitVisibleRuns(double[] lons, double[] lats)
        {
            int n = lons.Length;
            var runs = new List<GeoRun>(2);
            GeoRun cur = null;
            for (int i = 0; i < n; i++)
            {
                if (Depth(lons[i], lats[i]) > 0)
                {
                    float px, py;
                    Project(lons[i], lats[i], out px, out py);
                    if (cur == null) { cur = new GeoRun(); runs.Add(cur); }
                    cur.Points.Add(new PointF(px, py));
                }
                else
                {
                    if (cur != null)
                    {
                        cur.Points.Add(Crossing(lons[i - 1], lats[i - 1], lons[i], lats[i]));
                        cur.EndedAtHorizon = true;
                        cur = null;
                    }
                    if (i + 1 < n && Depth(lons[i + 1], lats[i + 1]) > 0)
                    {
                        cur = new GeoRun { StartedAtHorizon = true };
                        cur.Points.Add(Crossing(lons[i], lats[i], lons[i + 1], lats[i + 1]));
                        runs.Add(cur);
                    }
                }
            }
            // 多边形首尾相接：若第一段不是从地平圈交点开始、最后一段也不以交点结束，
            // 说明二者通过多边形的闭合边相连，应先合并再判断是否需要沿边缘闭合。
            if (runs.Count >= 2)
            {
                GeoRun first = runs[0], last = runs[runs.Count - 1];
                if (!first.StartedAtHorizon && !last.EndedAtHorizon)
                {
                    var merged = new GeoRun
                    {
                        StartedAtHorizon = last.StartedAtHorizon,
                        EndedAtHorizon = first.EndedAtHorizon
                    };
                    merged.Points.AddRange(last.Points);
                    merged.Points.AddRange(first.Points);
                    runs.RemoveAt(runs.Count - 1);
                    runs.RemoveAt(0);
                    runs.Insert(0, merged);
                }
            }
            return runs;
        }

        // 用球面边缘（地平圈）上的弧把折线从末端闭合回起点：
        // 两个方向各取一条弧，选“弧上各点仍落在该经纬度带内”的那条，
        // 这样填充区域严格贴合球面，不会出现横跨球体的直线边（饼状扇形）。
        private void AppendLimbArc(List<PointF> points, double[] band)
        {
            if (points.Count < 2) return;
            PointF start = points[0], end = points[points.Count - 1];
            double a0 = Math.Atan2(start.Y - cy, start.X - cx);   // 起点在边缘上的角度
            double a1 = Math.Atan2(end.Y - cy, end.X - cx);       // 终点在边缘上的角度
            const int steps = 24;
            for (int dir = 0; dir < 2; dir++)
            {
                // 注意方向：必须从“终点”沿边缘走回“起点”，否则多边形会来回折返而退化
                double sign = dir == 0 ? 1.0 : -1.0;
                double delta = a0 - a1;
                if (sign > 0) { while (delta <= 0) delta += 2 * Math.PI; }
                else { while (delta >= 0) delta -= 2 * Math.PI; }
                var arc = new List<PointF>(steps - 1);
                int inside = 0;
                for (int i = 1; i < steps; i++)
                {
                    double a = a1 + delta * i / steps;
                    float px = (float)(cx + r * Math.Cos(a));
                    float py = (float)(cy + r * Math.Sin(a));
                    double lon, lat;
                    Unproject(px, py, out lon, out lat);
                    arc.Add(new PointF(px, py));
                    if (IsInsideBand(lon, lat, band)) inside++;
                }
                if (inside * 2 > steps - 1)
                {
                    points.AddRange(arc);
                    return;
                }
            }
        }

        private static bool IsInsideBand(double lon, double lat, double[] band)
        {
            if (lat < band[2] - 1e-6 || lat > band[3] + 1e-6) return false;
            double d = lon - band[0];
            while (d < -180) d += 360;
            while (d > 180) d -= 360;
            return d >= -1e-6 && d <= band[1] - band[0] + 1e-6;
        }

        /// <summary>把控件坐标反算为经纬度；点落在球面之外时返回 false（供自检与交互使用）。</summary>
        public bool UnprojectPoint(float x, float y, out double lon, out double lat)
        {
            lon = 0; lat = 0;
            if (r <= 1) return false;
            double dx = (x - cx) / r, dy = (cy - y) / r;
            if (dx * dx + dy * dy > 1.0 + 1e-9) return false;
            Unproject(x, y, out lon, out lat);
            return true;
        }

        /// <summary>球面在控件中的圆心（由最近一次绘制确定）。</summary>
        public PointF DiscCenter { get { return new PointF(cx, cy); } }

        /// <summary>球面半径（由最近一次绘制确定）。</summary>
        public float DiscRadius { get { return r; } }

        private void DrawMeridian(Graphics g, double lon, Pen pen)
        {
            DrawGeo(g, Fill(lon, ParallelSamples), ParallelSamples, null, pen);
        }

        private void DrawParallel(Graphics g, double lat, Pen pen)
        {
            DrawGeo(g, MeridianSamples, Fill(lat, MeridianSamples), null, pen);
        }

        private static double[] BuildSamples(double from, double to, double step)
        {
            int count = (int)Math.Round((to - from) / step) + 1;
            var list = new double[count];
            for (int i = 0; i < count; i++) list[i] = from + i * step;
            return list;
        }

        private static double[] Fill(double value, double[] template)
        {
            var list = new double[template.Length];
            for (int i = 0; i < list.Length; i++) list[i] = value;
            return list;
        }

        private static double[][] BuildMeridians()
        {
            var list = new List<double[]>();
            for (double lon = -180; lon < 180; lon += 15) list.Add(Fill(lon, ParallelSamples));
            return list.ToArray();
        }

        private static double[][] BuildParallels()
        {
            var list = new List<double[]>();
            for (double lat = -75; lat <= 75.0001; lat += 15) list.Add(Fill(lat, MeridianSamples));
            return list.ToArray();
        }

        // 由闭合的经纬度多边形（西南、东南、东北、西北）生成加密后的边界点，便于在球面上贴合
        private static void BuildBox(double lon1, double lon2, double lat1, double lat2,
            out double[] lons, out double[] lats)
        {
            var ls = new List<double>();
            var ts = new List<double>();
            const double step = 2.0;
            for (double lon = lon1; lon < lon2; lon += step) { ls.Add(lon); ts.Add(lat1); }
            ls.Add(lon2); ts.Add(lat1);
            for (double lat = lat1; lat < lat2; lat += step) { ls.Add(lon2); ts.Add(lat); }
            ls.Add(lon2); ts.Add(lat2);
            for (double lon = lon2; lon > lon1; lon -= step) { ls.Add(lon); ts.Add(lat2); }
            ls.Add(lon1); ts.Add(lat2);
            for (double lat = lat2; lat > lat1; lat -= step) { ls.Add(lon1); ts.Add(lat); }
            ls.Add(lon1); ts.Add(lat1);
            lons = ls.ToArray();
            lats = ts.ToArray();
        }

        #endregion

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            cx = ClientSize.Width / 2f;
            cy = ClientSize.Height / 2f;
            r = Math.Min(cx, cy) - 6f;
            if (r < 20) return;

            // 球面
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(cx - r, cy - r, 2 * r, 2 * r);
                using (var brush = new LinearGradientBrush(new RectangleF(cx - r, cy - r, 2 * r, 2 * r),
                           Color.FromArgb(233, 241, 250), Color.FromArgb(206, 224, 244), 55f))
                    g.FillPath(brush, path);
            }

            // 经纬网（只画经纬线，不画海岸线）
            using (var gridPen = new Pen(Color.FromArgb(150, 150, 180, 205), 1f))
            {
                for (int i = 0; i < Meridians.Length; i++) DrawMeridian(g, Meridians[i][0], gridPen);
                for (int i = 0; i < Parallels.Length; i++) DrawParallel(g, Parallels[i][0], gridPen);
            }
            using (var axisPen = new Pen(Color.FromArgb(190, 110, 140, 180), 1.4f))
            {
                DrawMeridian(g, 0, axisPen);
                DrawParallel(g, 0, axisPen);
            }

            DrawSelection(g);
            DrawRowLetters(g);
            DrawDataExtent(g);

            // 地球边缘
            using (var pen = new Pen(Color.FromArgb(90, 110, 140), 1.4f))
                g.DrawEllipse(pen, cx - r, cy - r, 2 * r, 2 * r);
        }

        // 高亮当前选中的分带（UTM 为“带 × 纬度行”的四边形，高斯-克吕格为整个经度带）
        private void DrawSelection(Graphics g)
        {
            double width = BandWidth(kind);
            double lon1 = ZoneStart(kind, zone), lon2 = lon1 + width;
            double lat1 = -90, lat2 = 90;
            if (kind == ZoneSystem.Utm)
            {
                lat1 = UtmGridZone.MinOf(rowIndex);
                lat2 = UtmGridZone.MaxOf(rowIndex);
            }

            double[] lons, lats;
            BuildBox(lon1, lon2, lat1, lat2, out lons, out lats);
            using (var fill = new SolidBrush(Color.FromArgb(kind == ZoneSystem.Utm ? 130 : 110, 255, 196, 70)))
            using (var pen = new Pen(Color.FromArgb(215, 176, 96, 0), 2f))
            {
                // 传入带的范围：被地平圈裁掉的部分沿球面边缘闭合，黄色只落在地球表面
                DrawGeo(g, lons, lats, fill, null, new[] { lon1, lon2, lat1, lat2 });
                DrawMeridian(g, lon1, pen);
                DrawMeridian(g, lon2, pen);
                if (kind == ZoneSystem.Utm)
                {
                    DrawParallel(g, lat1, pen);
                    DrawParallel(g, lat2, pen);
                }
            }

            // 标注：UTM 为“带号 + 行字母”，高斯-克吕格为带号
            double labelLon = ZoneCentral(kind, zone);
            double labelLat = kind == ZoneSystem.Utm ? (lat1 + lat2) / 2 : ViewLat;
            string text = kind == ZoneSystem.Utm
                ? UtmGridZone.Designator(zone, rowIndex)
                : zone.ToString();
            DrawLabel(g, text, labelLon, labelLat, Color.FromArgb(150, 60, 20), 11f, true);
        }

        // 所有要素的外包矩形：只画一个方框标出位置，说明文字放在左下角
        private void DrawDataExtent(Graphics g)
        {
            if (dataExtent == null || dataExtent.IsNull) return;
            double[] lons, lats;
            BuildBox(dataExtent.MinX, dataExtent.MaxX, dataExtent.MinY, dataExtent.MaxY, out lons, out lats);
            using (var pen = new Pen(Color.FromArgb(240, 20, 130, 60), 2f))
                DrawGeo(g, lons, lats, null, pen);

            using (var font = new Font("Microsoft YaHei UI", 8.5f))
            using (var brush = new SolidBrush(Color.FromArgb(15, 110, 55)))
            using (var back = new SolidBrush(Color.FromArgb(225, 255, 255, 255)))
            {
                const string text = "□ 绿色方框：所有要素的外包矩形（数据范围）";
                SizeF size = g.MeasureString(text, font);
                float x = 6, y = ClientSize.Height - size.Height - 6;
                g.FillRectangle(back, x - 3, y - 2, size.Width + 6, size.Height + 4);
                g.DrawString(text, font, brush, x, y);
            }
        }

        // UTM 纬度行字母（沿视图中心经线标注，便于用户识别各行）
        private void DrawRowLetters(Graphics g)
        {
            if (kind != ZoneSystem.Utm) return;
            using (var font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold))
            using (var brush = new SolidBrush(Color.FromArgb(170, 70, 90, 140)))
            {
                for (int i = 0; i < UtmGridZone.RowCount; i++)
                {
                    double lat = (UtmGridZone.MinOf(i) + UtmGridZone.MaxOf(i)) / 2;
                    if (Depth(ViewLon, lat) < 0.25) continue;
                    float px, py;
                    Project(ViewLon, lat, out px, out py);
                    if (py < 14 || py > ClientSize.Height - 14) continue;
                    string text = UtmGridZone.LetterAt(i).ToString();
                    SizeF size = g.MeasureString(text, font);
                    g.DrawString(text, font, brush, px - size.Width / 2, py - size.Height / 2);
                }
            }
        }

        private void DrawLabel(Graphics g, string text, double lon, double lat, Color color, float size, bool bold)
        {
            if (Depth(lon, lat) < 0.15) return;
            float px, py;
            Project(lon, lat, out px, out py);
            using (var font = new Font("Microsoft YaHei UI", size, bold ? FontStyle.Bold : FontStyle.Regular))
            using (var brush = new SolidBrush(color))
            using (var back = new SolidBrush(Color.FromArgb(210, 255, 255, 255)))
            {
                SizeF measure = g.MeasureString(text, font);
                g.FillRectangle(back, px - measure.Width / 2 - 2, py - measure.Height / 2 - 1, measure.Width + 4, measure.Height + 2);
                g.DrawString(text, font, brush, px - measure.Width / 2, py - measure.Height / 2);
            }
        }

        #region 交互

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            dragging = true;
            moved = false;
            dragStart = e.Location;
            dragLon = ViewLon;
            dragLat = ViewLat;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!dragging) return;
            int dx = e.X - dragStart.X, dy = e.Y - dragStart.Y;
            if (!moved && Math.Abs(dx) + Math.Abs(dy) < 4) return;
            moved = true;
            double span = 180.0 / Math.Max(40.0, r);
            ViewLon = dragLon - dx * span;
            ViewLat = dragLat + dy * span;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!dragging) return;
            dragging = false;
            if (moved) return;
            if (Math.Sqrt(Math.Pow(e.X - cx, 2) + Math.Pow(e.Y - cy, 2)) > r) return;
            double lon, lat;
            Unproject(e.X, e.Y, out lon, out lat);
            bool changed = false;
            int z = ZoneOf(kind, lon);
            if (z != zone) { zone = z; changed = true; }
            if (kind == ZoneSystem.Utm)
            {
                int row = UtmGridZone.IndexOfLatitude(lat);
                if (row != rowIndex) { rowIndex = row; changed = true; }
            }
            Invalidate();
            if (changed)
            {
                var handler = ZonePicked;
                if (handler != null) handler(this, EventArgs.Empty);
            }
        }

        /// <summary>视图（中心经纬度）被拖动改变时触发，供下方滑块同步。</summary>
        public event EventHandler ViewChanged;

        private static double wrap(double lon)
        {
            while (lon > 180) lon -= 360;
            while (lon < -180) lon += 360;
            return lon;
        }

        #endregion
    }

    /// <summary>
    /// 分带选择对话框。地球以球体显示（只绘制经纬线，并叠加所有要素的外包矩形），
    /// 用户可直接在地球上点击选择分带，也可拖动地球或拖动下方滑块改变显示经纬度。
    /// UTM 按定义同时选择 6° 经度带（1~60）与 8° 纬度行（C~X，不含 I/O），二者组合即一个四边形。
    /// </summary>
    public sealed class ZonePickerForm : Form
    {
        private readonly ZoneGlobe globe = new ZoneGlobe { Dock = DockStyle.Fill };
        private readonly TrackBar lonSlider = new TrackBar { Minimum = -180, Maximum = 180, TickFrequency = 30,
            SmallChange = 5, LargeChange = 15, Dock = DockStyle.Top, Height = 42 };
        private readonly TrackBar latSlider = new TrackBar { Minimum = -85, Maximum = 85, TickFrequency = 15,
            SmallChange = 5, LargeChange = 15, Dock = DockStyle.Top, Height = 42 };
        private readonly Label sliderInfo = new Label { Dock = DockStyle.Top, Height = 22, TextAlign = ContentAlignment.MiddleLeft };
        private readonly Label summary = new Label { Dock = DockStyle.Top, Height = 46, TextAlign = ContentAlignment.MiddleLeft };
        private readonly NumericUpDown zoneBox = new NumericUpDown { Minimum = 1, Maximum = 60, Width = 80 };
        private readonly ComboBox rowBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 168 };
        private readonly ComboBox degreeBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 250 };
        private readonly Button zoomToData = new Button { Text = "按数据范围选择", Width = 130, Visible = false };
        private readonly bool utmMode;
        private bool sync;

        public ZonePickerForm(bool utmMode)
        {
            this.utmMode = utmMode;
            Text = utmMode ? "选择 UTM 分带（经度带 × 纬度行）" : "选择高斯-克吕格分带";
            Font = new Font("Microsoft YaHei UI", 9F);
            ClientSize = new Size(560, 770);
            MinimumSize = new Size(560, 770);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            ShowInTaskbar = false;

            var hint = new Label
            {
                Dock = DockStyle.Top, Height = 46, Padding = new Padding(10, 6, 10, 0),
                Text = utmMode
                    ? "UTM：自 180° 起向东每 6° 为一带（1~60 带）；自南纬 80° 起每 8° 为一行（C~X，不含 I、O，\r\n末行 X 为 72°N~84°N）。带号与行字母组合即一个四边形，例如北京为 50S。"
                    : "高斯-克吕格：3 度带自 1.5°E 起每 3° 一带，6 度带自 0° 起每 6° 一带。\r\n单击地球上的位置即可选择该处所属分带，也可拖动地球或拖动下方滑块改变显示经纬度。",
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.FromArgb(247, 243, 239)
            };

            var globeHost = new Panel { Dock = DockStyle.Top, Height = 440, Padding = new Padding(8, 0, 8, 0) };
            globeHost.Controls.Add(globe);

            lonSlider.ValueChanged += (s, e) => { if (!sync) globe.ViewLon = lonSlider.Value; UpdateSliderInfo(); };
            latSlider.ValueChanged += (s, e) => { if (!sync) globe.ViewLat = latSlider.Value; UpdateSliderInfo(); };
            globe.ViewChanged += (s, e) =>
            {
                sync = true;
                lonSlider.Value = (int)Math.Round(Math.Max(-180, Math.Min(180, globe.ViewLon)));
                latSlider.Value = (int)Math.Round(Math.Max(-85, Math.Min(85, globe.ViewLat)));
                sync = false;
                UpdateSliderInfo();
            };
            globe.ZonePicked += (s, e) => SyncFromGlobe();

            var optPanel = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38, Padding = new Padding(10, 4, 0, 0), WrapContents = false };
            optPanel.Controls.Add(new Label { Text = "带号", AutoSize = true, Margin = new Padding(0, 8, 4, 0) });
            zoneBox.ValueChanged += (s, e) =>
            {
                if (sync) return;
                globe.Zone = (int)zoneBox.Value;
                CenterOnSelection();   // 选带后把地球移动到该带
                UpdateSummary();
            };
            optPanel.Controls.Add(zoneBox);

            if (utmMode)
            {
                for (int i = 0; i < UtmGridZone.RowCount; i++)
                    rowBox.Items.Add(UtmGridZone.LetterAt(i) + "  " + UtmGridZone.LatitudeText(UtmGridZone.MinOf(i))
                        + " ~ " + UtmGridZone.LatitudeText(UtmGridZone.MaxOf(i)));
                rowBox.SelectedIndex = 14;   // S 行：32°N~40°N
                rowBox.Margin = new Padding(0, 4, 0, 0);
                rowBox.SelectedIndexChanged += (s, e) =>
                {
                    if (sync) return;
                    globe.RowIndex = rowBox.SelectedIndex;
                    CenterOnSelection();   // 换行后把地球移动到该行
                    UpdateSummary();
                };
                optPanel.Controls.Add(new Label { Text = "    纬度行", AutoSize = true, Margin = new Padding(0, 8, 4, 0) });
                optPanel.Controls.Add(rowBox);
                zoomToData.Visible = true;
                zoomToData.Margin = new Padding(10, 4, 0, 0);
                zoomToData.Click += (s, e) => SelectByData();
            }
            else
            {
                degreeBox.Items.AddRange(new object[] { "3 度带（中央经线 = 3°×带号）", "6 度带（中央经线 = 6°×带号 - 3°）" });
                degreeBox.SelectedIndex = 1;
                degreeBox.Margin = new Padding(0, 4, 0, 0);
                degreeBox.SelectedIndexChanged += (s, e) =>
                {
                    if (sync) return;
                    globe.Kind = degreeBox.SelectedIndex == 0 ? ZoneSystem.GaussKruger3 : ZoneSystem.GaussKruger6;
                    sync = true;
                    zoneBox.Minimum = ZoneGlobe.MinZone(globe.Kind);
                zoneBox.Maximum = ZoneGlobe.MaxZone(globe.Kind);
                    if (zoneBox.Value > zoneBox.Maximum) zoneBox.Value = zoneBox.Maximum;
                    sync = false;
                    globe.Zone = (int)zoneBox.Value;
                    CenterOnSelection();
                    UpdateSummary();
                };
                optPanel.Controls.Add(new Label { Text = "    分带方式", AutoSize = true, Margin = new Padding(0, 8, 4, 0) });
                optPanel.Controls.Add(degreeBox);
            }
            if (utmMode) optPanel.Controls.Add(zoomToData);

            var ok = new Button { Text = "确定", DialogResult = DialogResult.OK, Width = 88 };
            var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Width = 88 };
            var btns = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 46, FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(0, 6, 10, 0) };
            btns.Controls.Add(cancel);
            btns.Controls.Add(ok);
            AcceptButton = ok; CancelButton = cancel;

            Controls.Add(summary);
            Controls.Add(optPanel);
            Controls.Add(sliderInfo);
            Controls.Add(latSlider);
            Controls.Add(lonSlider);
            Controls.Add(globeHost);
            Controls.Add(hint);
            Controls.Add(btns);

            globe.Kind = utmMode ? ZoneSystem.Utm : ZoneSystem.GaussKruger6;
            zoneBox.Minimum = ZoneGlobe.MinZone(globe.Kind);   // 高斯-克吕格允许负带号（西半球）
            zoneBox.Maximum = ZoneGlobe.MaxZone(globe.Kind);
            if (zoneBox.Value > zoneBox.Maximum) zoneBox.Value = zoneBox.Maximum;
            UpdateSliderInfo();
            UpdateSummary();
        }

        /// <summary>当前分带方式。</summary>
        public ZoneSystem Kind { get { return globe.Kind; } }

        /// <summary>选中的带号。</summary>
        public int Zone
        {
            get { return (int)zoneBox.Value; }
            set
            {
                sync = true;
                zoneBox.Minimum = ZoneGlobe.MinZone(globe.Kind);
                zoneBox.Maximum = ZoneGlobe.MaxZone(globe.Kind);
                zoneBox.Value = Math.Max(zoneBox.Minimum, Math.Min(zoneBox.Maximum, value));
                sync = false;
                globe.Zone = (int)zoneBox.Value;
                CenterOnSelection();
                UpdateSummary();
            }
        }

        /// <summary>UTM 纬度行序号（0 为 C 行）。</summary>
        public int RowIndex
        {
            get { return utmMode ? rowBox.SelectedIndex : 0; }
            set
            {
                if (!utmMode) return;
                sync = true;
                rowBox.SelectedIndex = Math.Max(0, Math.Min(UtmGridZone.RowCount - 1, value));
                sync = false;
                globe.RowIndex = rowBox.SelectedIndex;
                CenterOnSelection();
                UpdateSummary();
            }
        }

        /// <summary>是否北半球（由纬度行决定，仅 UTM 有意义）。</summary>
        public bool IsNorth { get { return utmMode ? UtmGridZone.IsNorthern(RowIndex) : true; } }

        /// <summary>当前地球视图的中心经度（只读，便于自检）。</summary>
        public double ViewLon { get { return globe.ViewLon; } }

        /// <summary>当前地球视图的中心纬度（只读，便于自检）。</summary>
        public double ViewLat { get { return globe.ViewLat; } }

        /// <summary>是否 3 度带（仅高斯-克吕格有意义）。</summary>
        public bool Is3Degree
        {
            get { return !utmMode && degreeBox.SelectedIndex == 0; }
            set { if (!utmMode) degreeBox.SelectedIndex = value ? 0 : 1; }
        }

        /// <summary>所有要素合在一起的外包矩形（经纬度），会绘制到球面上便于选取。</summary>
        public Envelope DataExtent
        {
            get { return globe.DataExtent; }
            set
            {
                globe.DataExtent = value;
                // 打开界面时按数据范围自动选好带号/纬度行，并把地球移到数据所在区域，
                // 便于用户直接看到并点击（用户仍可再改）。
                if (value != null && !value.IsNull) SelectByData();
            }
        }

        // 把地球移动到当前选中的分带/纬度行（并同步下方两个滑块）
        private void CenterOnSelection()
        {
            double central = ZoneGlobe.ZoneCentral(globe.Kind, globe.Zone);
            double latitude = globe.ViewLat;
            if (utmMode)
                latitude = (UtmGridZone.MinOf(globe.RowIndex) + UtmGridZone.MaxOf(globe.RowIndex)) / 2;
            globe.CenterOn(central, latitude);
            SyncSliders();
        }

        private void SyncSliders()
        {
            sync = true;
            lonSlider.Value = (int)Math.Round(Math.Max(-180, Math.Min(180, globe.ViewLon)));
            latSlider.Value = (int)Math.Round(Math.Max(-85, Math.Min(85, globe.ViewLat)));
            sync = false;
            UpdateSliderInfo();
        }

        // 按数据外包矩形的中心选择带号与纬度行，并把地球移过去
        private void SelectByData()
        {
            Envelope extent = globe.DataExtent;
            if (extent == null || extent.IsNull) return;
            sync = true;
            zoneBox.Value = Math.Max(zoneBox.Minimum,
                Math.Min(zoneBox.Maximum, ZoneGlobe.ZoneOf(globe.Kind, extent.CenterX)));
            if (utmMode && rowBox.Items.Count > 0)
                rowBox.SelectedIndex = UtmGridZone.IndexOfLatitude(extent.CenterY);
            sync = false;
            globe.Zone = (int)zoneBox.Value;
            if (utmMode) globe.RowIndex = rowBox.SelectedIndex;
            double latitude = utmMode
                ? (UtmGridZone.MinOf(globe.RowIndex) + UtmGridZone.MaxOf(globe.RowIndex)) / 2
                : extent.CenterY;
            globe.CenterOn(extent.CenterX, latitude);
            SyncSliders();
            UpdateSummary();
        }

        // 用户在地球上点击后，把结果同步到控件
        private void SyncFromGlobe()
        {
            sync = true;
            zoneBox.Value = Math.Max(zoneBox.Minimum, Math.Min(zoneBox.Maximum, globe.Zone));
            if (utmMode && rowBox.SelectedIndex != globe.RowIndex) rowBox.SelectedIndex = globe.RowIndex;
            sync = false;
            UpdateSummary();
        }

        private void UpdateSliderInfo()
        {
            sliderInfo.Text = string.Format("    显示经度范围  {0} ~ {1}（滑块：视图中心经度 {2}°，纬度 {3}°）",
                ZoneGlobe.CentralText(globe.ViewLon - 90), ZoneGlobe.CentralText(globe.ViewLon + 90),
                globe.ViewLon.ToString("0.#"), globe.ViewLat.ToString("0.#"));
        }

        private void UpdateSummary()
        {
            double width = ZoneGlobe.BandWidth(globe.Kind);
            double central = ZoneGlobe.ZoneCentral(globe.Kind, globe.Zone);
            string text;
            if (utmMode)
                text = string.Format("    当前四边形：{0}（北半球：{1}）\r\n    {2} 带：中央经线 {3}，经度范围 {4} ~ {5}；{6} 行：纬度范围 {7} ~ {8}",
                    UtmGridZone.Designator(globe.Zone, globe.RowIndex),
                    UtmGridZone.IsNorthern(globe.RowIndex) ? "是" : "否",
                    globe.Zone, ZoneGlobe.CentralText(central),
                    ZoneGlobe.CentralText(central - width / 2), ZoneGlobe.CentralText(central + width / 2),
                    UtmGridZone.LetterAt(globe.RowIndex),
                    UtmGridZone.LatitudeText(UtmGridZone.MinOf(globe.RowIndex)),
                    UtmGridZone.LatitudeText(UtmGridZone.MaxOf(globe.RowIndex)));
            else
                text = string.Format("    当前分带：{0} 第{1}\r\n    中央经线 {2}    经度范围 {3} ~ {4}",
                    globe.Kind == ZoneSystem.GaussKruger3 ? "3 度带" : "6 度带",
                    ZoneGlobe.ZoneText(globe.Kind, globe.Zone),
                    ZoneGlobe.CentralText(central),
                    ZoneGlobe.CentralText(central - width / 2), ZoneGlobe.CentralText(central + width / 2));
            summary.Text = text;
        }
    }
}
