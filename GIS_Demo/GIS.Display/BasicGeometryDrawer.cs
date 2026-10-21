using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;

namespace GIS.Display
{
    // 模块3（图层渲染与编辑）重写：渲染器驱动的点线面绘制、自定义虚线、多边界偏移面、注记与符号预览。
    public sealed class BasicGeometryDrawer : ILayerRenderer, ILayerLabelRenderer
    {
        public void DrawLayer(Graphics graphics, Layer layer, MapTransform transform, Rectangle viewport)
        {
            if (layer == null || layer.FeatureClass == null) return;
            Envelope extent = transform.GetExtent();
            foreach (Feature feature in layer.FeatureClass.Features)
            {
                if (feature.Geometry == null || feature.Geometry.IsEmpty) continue;
                Symbol symbol = GetSymbol(layer, feature);
                if (symbol != null && !symbol.Visible) continue;   // 绑定属性错误的符号不绘制到地图上
                double margin = SymbolMargin(symbol, transform) * transform.MapUnitsPerPixel();
                var expanded = new Envelope(extent.MinX - margin, extent.MaxX + margin,
                    extent.MinY - margin, extent.MaxY + margin);
                if (feature.GetEnvelope().IntersectsWith(expanded))
                    DrawGeometry(graphics, feature.Geometry, symbol, transform);
            }
        }

        public void DrawLayerLabels(Graphics graphics, Layer layer, MapTransform transform, Rectangle viewport)
        {
            if (layer == null || layer.FeatureClass == null) return;
            LabelRenderer labelRenderer = layer.LabelRenderer;
            if (labelRenderer == null || !labelRenderer.LabelFeatures) return;
            TextSymbol ts = labelRenderer.TextSymbol ?? new TextSymbol();
            double angle = labelRenderer.RotateAngle;
            double dpm = graphics.DpiX / 0.0254;
            bool avoid = labelRenderer.AvoidOverlap;
            IList<PointF[]> placed = null;
            if (avoid) BeginLabelPass(graphics, layer, transform, out placed);
            else { LastDrawnLabelCount = 0; LastSkippedLabelCount = 0; }

            foreach (Feature feature in layer.FeatureClass.Features)
            {
                if (feature.Geometry == null || feature.Geometry.IsEmpty) continue;
                string text = feature.Attributes == null ? null :
                    Convert.ToString(feature.Attributes.GetItem(labelRenderer.Field));
                if (string.IsNullOrEmpty(text)) continue;
                Symbol symbol = GetSymbol(layer, feature);
                SizeF size = MeasureLabel(graphics, text, ts, dpm);
                PointF[] candidates = LabelCandidates(feature.Geometry, symbol, transform, size, angle);
                if (candidates == null || candidates.Length == 0) continue;

                PointF location;
                if (!avoid)
                {
                    location = candidates[0];       // 不做避让：所有注记都按首选位置绘制
                }
                else if (!LabelPlacer.TryPlace(candidates, size, angle, placed, out location))
                {
                    LastSkippedLabelCount++;        // 候选位置全被占用：省略这条注记，避免叠字
                    continue;
                }
                DrawTextLabel(graphics, text, location, ts, angle);
                LastDrawnLabelCount++;
            }
        }

        /// <summary>
        /// 最近一次注记绘制过程中实际画出的注记条数（同一个绘制过程跨图层累计，供界面提示与自动检查使用）。
        /// </summary>
        public int LastDrawnLabelCount { get; private set; }

        /// <summary>最近一次注记绘制过程中因为避让冲突被省略的注记条数。</summary>
        public int LastSkippedLabelCount { get; private set; }

        #region 注记摆放（避让）

        // 一次“注记绘制过程”的范围：同一个 Graphics、同一次视窗/范围、相邻的连续调用视为同一帧。
        // 地图控件是按图层逐个调用 DrawLayerLabels 的，用这些条件把跨图层的注记也放进同一批做避让。
        private Graphics labelPassGraphics;
        private Layer labelPassFirstLayer;
        private Layer labelPassLastLayer;
        private double[] labelPassExtent;
        private DateTime labelPassTime = DateTime.MinValue;
        private readonly List<PointF[]> placedLabels = new List<PointF[]>();

        private static readonly TimeSpan LabelPassWindow = TimeSpan.FromMilliseconds(50);

        private void BeginLabelPass(Graphics graphics, Layer layer, MapTransform transform,
            out IList<PointF[]> placed)
        {
            Envelope extent = transform.GetExtent();
            bool sameExtent = labelPassExtent != null
                && Math.Abs(labelPassExtent[0] - extent.MinX) < 1e-9 && Math.Abs(labelPassExtent[1] - extent.MaxX) < 1e-9
                && Math.Abs(labelPassExtent[2] - extent.MinY) < 1e-9 && Math.Abs(labelPassExtent[3] - extent.MaxY) < 1e-9;
            bool newFrame = !ReferenceEquals(graphics, labelPassGraphics)
                || !sameExtent
                || ReferenceEquals(layer, labelPassLastLayer)          // 又回到同一图层 → 新的一帧
                || DateTime.UtcNow - labelPassTime > LabelPassWindow;
            if (newFrame)
            {
                placedLabels.Clear();
                labelPassGraphics = graphics;
                labelPassFirstLayer = layer;
                labelPassExtent = new[] { extent.MinX, extent.MaxX, extent.MinY, extent.MaxY };
                LastDrawnLabelCount = 0;
                LastSkippedLabelCount = 0;
            }
            labelPassLastLayer = layer;
            labelPassTime = DateTime.UtcNow;
            placed = placedLabels;
        }

        /// <summary>测量一条注记的屏幕尺寸（像素）：宽度按宽高比横向缩放。</summary>
        public static SizeF MeasureLabel(Graphics g, string text, TextSymbol ts, double dpm)
        {
            if (ts == null) ts = new TextSymbol();
            FontStyle style = FontStyle.Regular;
            if (ts.Bold) style |= FontStyle.Bold;
            if (ts.Italic) style |= FontStyle.Italic;
            using (var font = new Font(ts.FontName, ts.FontSize, style))
            {
                SizeF size = g.MeasureString(text, font);
                size.Width = (float)(size.Width * ts.FontRatio);
                return size;
            }
        }

        // 注记的候选锚点（左上角）：点要素试四个角，线/面在定位点居中并允许上下微调。
        // 传入 angle：候选位置按“旋转后的平行四边形”反推，保证设置旋转角后注记仍然紧贴要素、不会远离。
        // 点要素的注记离符号中心的距离 = 符号半径 + 3 像素，但还有两个上限：
        //  · LabelPlacer.MaxAnchorDistance（屏幕像素，符号特别大时兜住）；
        //  · MaxLabelGroundOffsetMeters（实地距离，小比例尺时把偏移压到最小，见下面的注释）。
        private static PointF[] LabelCandidates(Geometry geometry, Symbol symbol, MapTransform transform,
            SizeF size, double angle)
        {
            float anchor = PointLabelAnchorDistance(transform, symbol);
            if (geometry is Point p)
                return LabelPlacer.AroundPoint(Screen(p.Coordinate, transform), size, anchor, 0f, angle);
            if (geometry is MultiPoint mp)
            {
                if (mp.Points.Count == 0) return null;
                return LabelPlacer.AroundPoint(Screen(mp.Points[0], transform), size, anchor, 0f, angle);
            }
            PointF center = GetLabelAnchor(geometry, transform);
            if (float.IsNaN(center.X)) return null;
            float step = LabelPlacer.RotatedSize(size, angle).Height + 4f;
            return LabelPlacer.AroundCenter(center, size, step, angle);
        }

        /// <summary>
        /// 注记与点的最大**实地**距离（米）。比例尺很小时 1 像素代表的实地距离非常大
        /// （1:9000 万、经纬度数据时 1 像素约 24 公里），此时“符号半径 + 3 像素”的固定屏幕偏移
        /// 折合实地就是几十到两百多公里——中国地图上昆明的注记会落到别的省去。
        /// 因此给偏移再加一个实地距离上限：比例尺越小，偏移越接近 0，注记就贴在点上。
        /// 比例尺较大（例如 1:5 万，1 像素约 13 米）时上限相当于 150 像素，不影响正常摆放。
        /// </summary>
        private const double MaxLabelGroundOffsetMeters = 2000;

        /// <summary>点注记离符号中心的基础偏移（像素）：符号半径 + 3 像素。</summary>
        private const float PointLabelGap = 3f;

        /// <summary>
        /// 点注记与符号中心的实际距离（像素）=（符号半径 + 3 像素）再按实地距离上限压缩。
        /// 自动检查用它验证“小比例尺下偏移被压小、正常比例尺下不受影响”。
        /// </summary>
        public static float PointLabelAnchorDistance(MapTransform transform, Symbol symbol)
        {
            var marker = symbol as SimpleMarkerSymbol;
            float radius = marker == null ? 0f : (float)(ToPixels(marker.Size, transform.Dpm) / 2);
            double metresPerPixel = transform.MapUnitsPerPixel() * transform.Mpu;   // Mpu：1 个地图单位代表多少米
            if (double.IsNaN(metresPerPixel) || metresPerPixel <= 0) return radius + PointLabelGap;
            double maximumPixels = MaxLabelGroundOffsetMeters / metresPerPixel;
            // 保留 0.1 像素的最小间隙，避免正好压在符号中心（此时注记仍紧贴点位）
            return (float)Math.Max(0.1, Math.Min(radius + PointLabelGap, maximumPixels));
        }

        /// <summary>线取折线中点、面取外包矩形中心的屏幕坐标（不含点要素的让开偏移）。</summary>
        private static PointF GetLabelAnchor(Geometry geometry, MapTransform transform)
        {
            if (geometry is LineString ls)
                return ls.IsEmpty ? new PointF(float.NaN, float.NaN) : Screen(ls.GetMidPoint(), transform);
            if (geometry is MultiLineString mls)
                return mls.Parts.Count == 0 || mls.Parts[0].IsEmpty
                    ? new PointF(float.NaN, float.NaN) : Screen(mls.Parts[0].GetMidPoint(), transform);
            Envelope env = geometry.GetEnvelope();
            return Screen(new Coordinate(env.CenterX, env.CenterY), transform);
        }

        #endregion

        private static Symbol GetSymbol(Layer layer, Feature feature)
        {
            if (layer.Renderer != null)
            {
                Symbol s = layer.Renderer.GetSymbolFor(feature);
                if (s != null) return s;
            }
            if (layer.Symbol != null) return layer.Symbol;
            return DefaultSymbol(feature.Geometry);
        }

        public static Symbol DefaultSymbol(Geometry geometry)
        {
            return DefaultSymbol(geometry == null ? GeometryTypeConstant.Point : geometry.GeometryType);
        }

        /// <summary>
        /// 按几何类型给出**确定**的默认符号（颜色、尺寸固定，不含随机量）。
        /// 图层面板与地图都用它：导入 shp 等“没有设置过符号”的图层时，
        /// 两侧显示同一个符号，且不会因为重绘而变色（此前图层面板每次都新建随机颜色的符号）。
        /// </summary>
        public static Symbol DefaultSymbol(GeometryTypeConstant type)
        {
            if (type == GeometryTypeConstant.Point || type == GeometryTypeConstant.MultiPoint)
                return new SimpleMarkerSymbol { Color = Color.FromArgb(183, 63, 63), Size = 3, Style = SimpleMarkerSymbolStyleConstant.Circle };
            if (type == GeometryTypeConstant.LineString || type == GeometryTypeConstant.MultiLineString)
                return new SimpleLineSymbol { Color = Color.FromArgb(65, 110, 166), Size = 0.6 };
            return new SimpleFillSymbol
            {
                Color = Color.FromArgb(216, 231, 207),
                Outline = new SimpleLineSymbol { Color = Color.FromArgb(89, 123, 84), Size = 0.3 }
            };
        }

        /// <summary>为新图层使用指定的识别色；面填充保留浅色，边界使用原色。</summary>
        public static Symbol DefaultSymbol(GeometryTypeConstant type, Color color)
        {
            if (type == GeometryTypeConstant.Point || type == GeometryTypeConstant.MultiPoint)
                return new SimpleMarkerSymbol { Color = color, Size = 3, Style = SimpleMarkerSymbolStyleConstant.Circle };
            if (type == GeometryTypeConstant.LineString || type == GeometryTypeConstant.MultiLineString)
                return new SimpleLineSymbol { Color = color, Size = 0.6 };
            return new SimpleFillSymbol
            {
                Color = Color.FromArgb((color.R + 2 * 255) / 3, (color.G + 2 * 255) / 3,
                    (color.B + 2 * 255) / 3),
                Outline = new SimpleLineSymbol { Color = color, Size = 0.3 }
            };
        }

        /// <summary>
        /// 图层“还没有设置过符号”时给它绑定一个默认符号（只绑一次，之后图层面板与地图共用同一个对象）。
        /// 返回是否新绑定了符号。
        /// </summary>
        public static bool EnsureDefaultSymbol(Layer layer)
        {
            if (layer == null || layer.FeatureClass == null) return false;
            if (layer.Renderer != null || layer.Symbol != null) return false;
            layer.Symbol = DefaultSymbol(layer.FeatureClass.GeometryType);
            return true;
        }

        public static bool EnsureDefaultSymbol(Layer layer, Color color)
        {
            if (layer == null || layer.FeatureClass == null) return false;
            if (layer.Renderer != null || layer.Symbol != null) return false;
            layer.Symbol = DefaultSymbol(layer.FeatureClass.GeometryType, color);
            return true;
        }

        public static Symbol HighlightSymbol(Geometry geometry)
        {
            if (geometry is Point || geometry is MultiPoint)
                return new SimpleMarkerSymbol { Color = Color.Transparent, OutlineColor = Color.DeepPink,
                    OutlineWidth = 0.7, Size = 4.5, Style = SimpleMarkerSymbolStyleConstant.Circle };
            if (geometry is LineString || geometry is MultiLineString)
                return new SimpleLineSymbol { Color = Color.DeepPink, Size = 1 };
            return new SimpleFillSymbol
            {
                Color = Color.FromArgb(55, Color.DeepPink),
                Outline = new SimpleLineSymbol { Color = Color.DeepPink, Size = 0.8 }
            };
        }

        private static double SymbolMargin(Symbol symbol, MapTransform transform)
        {
            double size = 1;
            var marker = symbol as SimpleMarkerSymbol;
            var line = symbol as SimpleLineSymbol;
            var fill = symbol as SimpleFillSymbol;
            if (marker != null) size = Math.Max(marker.Size, Math.Max(marker.OutlineWidth, 0));
            else if (line != null)
            {
                // 有效线宽：自定义虚线时取最宽的一段（含边框），否则取 Size
                size = line.EffectiveWidth;
                foreach (LineDashElement e in line.DashElements)
                    if (e != null) size = Math.Max(size, Math.Max(e.TickLength, e.OutlineWidth));
                foreach (LineOffset o in line.Offsets)
                    if (o != null) size = Math.Max(size, Math.Abs(o.Offset) + (o.Line == null ? 0 : o.Line.EffectiveWidth));
            }
            else if (fill != null)
                foreach (FillOutline o in fill.Outlines)
                    if (o != null && o.Outline != null)
                        size = Math.Max(size, o.Outline.Size + Math.Abs(o.Offset));
            return Math.Max(2, size * transform.Dpm / 1000.0);
        }

        #region 地图坐标绘制

        public static void DrawGeometry(Graphics graphics, Geometry geometry, Symbol symbol, MapTransform transform)
        {
            if (geometry == null || geometry.IsEmpty) return;
            if (geometry is Point point)
                DrawMarker(graphics, Screen(point.Coordinate, transform), symbol as SimpleMarkerSymbol, transform.Dpm);
            else if (geometry is MultiPoint multiPoint)
                foreach (Coordinate p in multiPoint.Points)
                    DrawMarker(graphics, Screen(p, transform), symbol as SimpleMarkerSymbol, transform.Dpm);
            else if (geometry is LineString line)
                DrawLineScreen(graphics, ScreenPoints(line.Points, transform), symbol as SimpleLineSymbol, transform.Dpm);
            else if (geometry is MultiLineString multiLine)
                foreach (LineString part in multiLine.Parts)
                    DrawLineScreen(graphics, ScreenPoints(part.Points, transform), symbol as SimpleLineSymbol, transform.Dpm);
            else if (geometry is Polygon polygon)
                DrawPolygon(graphics, polygon, symbol as SimpleFillSymbol, transform);
            else if (geometry is MultiPolygon multiPolygon)
                foreach (Polygon part in multiPolygon.Parts)
                    DrawPolygon(graphics, part, symbol as SimpleFillSymbol, transform);
        }

        private static PointF Screen(Coordinate c, MapTransform transform)
        {
            double k = 1 / transform.MapUnitsPerPixel();
            return new PointF(ToScreenValue((c.X - transform.MapOffsetX) * k),
                ToScreenValue((transform.MapOffsetY - c.Y) * k));
        }

        // 限制屏幕坐标范围：高倍放大时远离视图的顶点会算出超大坐标，GDI+ 会因此报错
        private static float ToScreenValue(double v)
        {
            if (double.IsNaN(v)) return 0f;
            if (v > 1e6) return 1e6f;
            if (v < -1e6) return -1e6f;
            return (float)v;
        }

        private static PointF[] ScreenPoints(Points points, MapTransform transform)
        {
            PointF[] result = new PointF[points.Count];
            for (int i = 0; i < points.Count; i++)
                result[i] = Screen(points[i], transform);
            return result;
        }

        private static PointF[] ScreenRing(Points ring, MapTransform transform)
        {
            PointF[] result = new PointF[ring.Count];
            for (int i = 0; i < ring.Count; i++)
                result[i] = Screen(ring[i], transform);
            return result;
        }

        private static void DrawPolygon(Graphics g, Polygon polygon, SimpleFillSymbol symbol, MapTransform transform)
        {
            if (polygon.IsEmpty) return;
            if (symbol == null) symbol = (SimpleFillSymbol)DefaultSymbol(polygon);
            PointF[] exterior = ScreenRing(polygon.ExteriorRing, transform);
            var holes = new List<PointF[]>();
            foreach (Points hole in polygon.Holes)
                if (hole.Count >= 3)
                    holes.Add(ScreenRing(hole, transform));

            // 填充（外环 + 内环，Alternate 模式挖洞）
            if (symbol.Color.A > 0)
            {
                using (var path = new GraphicsPath(FillMode.Alternate))
                {
                    path.AddPolygon(exterior);
                    foreach (PointF[] h in holes) path.AddPolygon(h);
                    using (var brush = new SolidBrush(symbol.Color)) g.FillPath(brush, path);
                }
            }
            // 多条边界（可偏移）
            foreach (FillOutline outline in symbol.Outlines)
            {
                if (outline == null || outline.Outline == null) continue;
                float offsetPx = (float)ToPixels(outline.Offset, transform.Dpm);
                // 外围边界向外偏移；内部边界（洞）向内偏移，使面的偏移方向始终“背离实体内部”，
                // 即正偏移量统一表示把面整体扩张（外环外扩、洞收缩）。
                DrawFillOutlineRing(g, OffsetRing(exterior, offsetPx), outline.Outline, transform.Dpm);
                foreach (PointF[] h in holes)
                    DrawFillOutlineRing(g, OffsetRing(h, -offsetPx), outline.Outline, transform.Dpm);
            }
        }

        // 绘制一条闭合边界（支持自定义虚线）
        private static void DrawFillOutlineRing(Graphics g, PointF[] ring, SimpleLineSymbol outline, double dpm)
        {
            if (ring == null || ring.Length < 2) return;
            PointF[] closed = CloseRing(ring);
            if (outline.HasCustomDash)
                DrawCustomDash(g, closed, outline, dpm);
            else
                using (Pen pen = LinePen(outline, dpm))
                    g.DrawLines(pen, closed);
        }

        // 按半径方向偏移一个环（正值向外）。用于多边界渐变效果，星形/凸多边形下近似正确。
        private static PointF[] OffsetRing(PointF[] ring, float offsetPx)
        {
            if (offsetPx == 0 || ring == null || ring.Length < 3) return ring;
            float cx = 0, cy = 0;
            foreach (PointF p in ring) { cx += p.X; cy += p.Y; }
            cx /= ring.Length; cy /= ring.Length;
            PointF[] result = new PointF[ring.Length];
            for (int i = 0; i < ring.Length; i++)
            {
                float dx = ring[i].X - cx, dy = ring[i].Y - cy;
                float len = (float)Math.Sqrt(dx * dx + dy * dy);
                if (len == 0) { result[i] = ring[i]; continue; }
                result[i] = new PointF(ring[i].X + dx / len * offsetPx, ring[i].Y + dy / len * offsetPx);
            }
            return result;
        }

        private static PointF[] CloseRing(PointF[] ring)
        {
            var list = new List<PointF>(ring);
            if (list.Count > 0 && list[0] != list[list.Count - 1])
                list.Add(list[0]);
            return list.ToArray();
        }

        #endregion

        #region 屏幕空间点线面绘制

        private static void DrawMarker(Graphics g, PointF center, SimpleMarkerSymbol symbol, double dpm)
        {
            if (symbol == null) symbol = new SimpleMarkerSymbol { Color = Color.Firebrick };
            float size = Math.Max(1, (float)ToPixels(symbol.Size, dpm));
            DrawMarkerAt(g, center, symbol, size);
        }

        private static void DrawMarkerAt(Graphics g, PointF center, SimpleMarkerSymbol symbol, float size)
        {
            float half = size / 2;
            var rect = new RectangleF(center.X - half, center.Y - half, size, size);
            bool fill = symbol.Color.A > 0;
            bool outline = symbol.OutlineColor.A > 0;
            // 边框宽度与符号尺寸同比例缩放，保证高 DPI 下一致
            float ratio = symbol.Size > 0.01f ? (float)(symbol.OutlineWidth / symbol.Size) : 0.1f;
            float outlineW = Math.Max(0.5f, size * ratio);
            if (symbol.Style == SimpleMarkerSymbolStyleConstant.Cross)
            {
                Color c = outline ? symbol.OutlineColor : (fill ? symbol.Color : Color.Black);
                using (var pen = new Pen(c, Math.Max(0.6f, size / 6)))
                {
                    g.DrawLine(pen, center.X - half, center.Y, center.X + half, center.Y);
                    g.DrawLine(pen, center.X, center.Y - half, center.X, center.Y + half);
                }
                return;
            }
            if (symbol.Style == SimpleMarkerSymbolStyleConstant.Exclamation)
            {
                // “绑定属性错误”专用的红色感叹号符号（不在图上绘制，仅用于图例显示）
                Color c = fill ? symbol.Color : (outline ? symbol.OutlineColor : Color.Firebrick);
                float barWidth = Math.Max(1.2f, size * 0.24f);
                float barHeight = size * 0.60f;
                using (var brush = new SolidBrush(c))
                {
                    g.FillRectangle(brush, center.X - barWidth / 2, center.Y - half, barWidth, barHeight);
                    float dot = Math.Max(1.2f, size * 0.20f);
                    g.FillEllipse(brush, center.X - dot / 2, center.Y - half + barHeight + dot * 0.35f, dot, dot);
                }
                return;
            }
            // 五角星（首都等）：一个角朝上，外接圆半径 = Size/2，凹角半径按正五角星比例 0.382
            if (symbol.Style == SimpleMarkerSymbolStyleConstant.Star)
            {
                PointF[] star = StarPoints(center, half);
                if (fill)
                    using (var brush = new SolidBrush(symbol.Color)) g.FillPolygon(brush, star);
                if (outline)
                    using (var pen = new Pen(symbol.OutlineColor, outlineW)) g.DrawPolygon(pen, star);
                return;
            }
            // 中心点 + 外围空心圈（省会＝中心实心点，普通城市＝中心空心点）
            if (symbol.Style == SimpleMarkerSymbolStyleConstant.SolidDotCircle
                || symbol.Style == SimpleMarkerSymbolStyleConstant.HollowDotCircle)
            {
                Color ink = fill ? symbol.Color : (outline ? symbol.OutlineColor : Color.Black);
                float ringW = Math.Max(0.8f, Math.Min(outlineW, size / 3f));
                using (var pen = new Pen(ink, ringW))
                    g.DrawEllipse(pen, new RectangleF(rect.X + ringW / 2, rect.Y + ringW / 2,
                        rect.Width - ringW, rect.Height - ringW));
                float dotR = Math.Max(size / 6f, ringW * 1.2f);
                dotR = Math.Min(dotR, half - ringW * 1.5f);
                if (dotR > 0.5f)
                {
                    var dot = new RectangleF(center.X - dotR, center.Y - dotR, dotR * 2, dotR * 2);
                    if (symbol.Style == SimpleMarkerSymbolStyleConstant.SolidDotCircle)
                        using (var brush = new SolidBrush(ink)) g.FillEllipse(brush, dot);
                    else
                        using (var pen = new Pen(ink, ringW)) g.DrawEllipse(pen, dot);
                }
                return;
            }
            PointF[] triangle = new[]
            {
                new PointF(center.X, center.Y - half),
                new PointF(center.X + half, center.Y + half),
                new PointF(center.X - half, center.Y + half)
            };
            if (fill)
            {
                using (var brush = new SolidBrush(symbol.Color))
                {
                    if (symbol.Style == SimpleMarkerSymbolStyleConstant.Square) g.FillRectangle(brush, rect);
                    else if (symbol.Style == SimpleMarkerSymbolStyleConstant.Triangle) g.FillPolygon(brush, triangle);
                    else g.FillEllipse(brush, rect);
                }
            }
            if (outline)
            {
                using (var pen = new Pen(symbol.OutlineColor, outlineW))
                {
                    if (symbol.Style == SimpleMarkerSymbolStyleConstant.Square) g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
                    else if (symbol.Style == SimpleMarkerSymbolStyleConstant.Triangle) g.DrawPolygon(pen, triangle);
                    else g.DrawEllipse(pen, rect);
                }
            }
        }

        /// <summary>
        /// 五角星的 10 个顶点：外接圆半径为 r、一个角朝上（-90°），凹角半径为 0.382r（正五角星的比例）。
        /// </summary>
        private static PointF[] StarPoints(PointF center, float r)
        {
            var points = new PointF[10];
            double inner = r * 0.382;
            for (int i = 0; i < points.Length; i++)
            {
                double radius = i % 2 == 0 ? r : inner;
                double angle = -Math.PI / 2 + i * Math.PI / 5.0;
                points[i] = new PointF(
                    (float)(center.X + radius * Math.Cos(angle)),
                    (float)(center.Y + radius * Math.Sin(angle)));
            }
            return points;
        }

        private static void DrawLineScreen(Graphics g, PointF[] pts, SimpleLineSymbol symbol, double dpm)
        {
            if (pts == null || pts.Length < 2) return;
            if (symbol == null) symbol = new SimpleLineSymbol { Color = Color.SlateGray };
            // 多条偏移线：按各条线的偏移量平移后分别绘制（可实现国界线等多平行线）
            if (symbol.HasOffsets)
            {
                foreach (LineOffset offset in symbol.Offsets)
                {
                    if (offset == null || offset.Line == null) continue;
                    float offsetPx = (float)ToPixels(offset.Offset, dpm);
                    if (offset.Line.HasCustomDash)
                        // 自定义虚线：**图案统一在基准线上排布**，每条偏移线只是把自己那些段整体平移过去
                        // （而不是先平移折线再各排各的图案）。这样各条偏移线的虚实分界落在同一位置上，
                        // 段长也完全一致；否则拐角处内外侧长度不同会累积相位差，小比例尺下就错开了。
                        DrawCustomDash(g, pts, offset.Line, dpm, offsetPx);
                    else
                        using (Pen pen = LinePen(offset.Line, dpm))
                            g.DrawLines(pen, OffsetPolyline(pts, offsetPx));
                }
                return;
            }
            if (symbol.HasCustomDash)
                DrawCustomDash(g, pts, symbol, dpm);
            else
                using (Pen pen = LinePen(symbol, dpm))
                    g.DrawLines(pen, pts);
        }

        // 把折线整体沿法线方向平移指定像素（各顶点取相邻边的平均法线，近似偏移）
        private static PointF[] OffsetPolyline(PointF[] pts, float offset)
        {
            if (offset == 0 || pts == null || pts.Length < 2) return pts;
            int count = pts.Length;
            var result = new PointF[count];
            for (int i = 0; i < count; i++)
            {
                // 找“离本顶点足够远”的前后各一个顶点来确定方向。
                // 小比例尺下相邻顶点经常落在同一个像素上（方向向量为 0），
                // 若直接对它们求方向会得到零法线 → 该顶点根本不平移，偏移线就与原线重合、彼此也对不齐。
                int before = -1, after = -1;
                for (int k = i - 1; k >= 0; k--) if (DistanceOf(pts[k], pts[i]) > OffsetMinSegment) { before = k; break; }
                for (int k = i + 1; k < count; k++) if (DistanceOf(pts[k], pts[i]) > OffsetMinSegment) { after = k; break; }

                PointF beforeNormal = PointF.Empty, afterNormal = PointF.Empty;
                bool hasBefore = before >= 0 && UnitLeftNormal(pts[before], pts[i], out beforeNormal);
                bool hasAfter = after >= 0 && UnitLeftNormal(pts[i], pts[after], out afterNormal);

                PointF normal;
                float scale = 1f;
                if (hasBefore && hasAfter)
                {
                    // 取两段法线的角平分线方向，并按 1/cos(半角) 放大（斜接）：
                    // 这样偏移后的顶点到两条相邻边的**垂距都等于 offset**，
                    // 各条偏移线才是真正的平行线（否则拐角处会缩水，内/外侧缩水还不一样，看起来就没对齐）。
                    float sx = beforeNormal.X + afterNormal.X, sy = beforeNormal.Y + afterNormal.Y;
                    float length = (float)Math.Sqrt(sx * sx + sy * sy);
                    if (length < 1e-4f) normal = beforeNormal;              // 近似折返（约 180°）
                    else
                    {
                        normal = new PointF(sx / length, sy / length);
                        float cos = normal.X * beforeNormal.X + normal.Y * beforeNormal.Y;
                        scale = cos > 1f / OffsetMiterLimit ? 1f / cos : OffsetMiterLimit;
                    }
                }
                else if (hasAfter) normal = afterNormal;
                else if (hasBefore) normal = beforeNormal;
                else { result[i] = pts[i]; continue; }                      // 整条折线退化成一个点：保持原样

                result[i] = new PointF(pts[i].X + normal.X * offset * scale, pts[i].Y + normal.Y * offset * scale);
            }
            return result;
        }

        /// <summary>斜接（miter）限幅：尖角处最多把顶点沿角平分线放大到这个倍数，避免出现过长的尖刺。</summary>
        private const float OffsetMiterLimit = 4f;

        /// <summary>判断“两个屏幕顶点是否重合”的最小距离（像素）。</summary>
        private const float OffsetMinSegment = 1e-4f;

        private static float DistanceOf(PointF a, PointF b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>由 from→to 求单位左法线；两点重合（退化段）时返回 false。</summary>
        private static bool UnitLeftNormal(PointF from, PointF to, out PointF normal)
        {
            float dx = to.X - from.X, dy = to.Y - from.Y;
            float length = (float)Math.Sqrt(dx * dx + dy * dy);
            if (length <= OffsetMinSegment) { normal = PointF.Empty; return false; }
            normal = new PointF(dy / length, -dx / length);      // 屏幕 y 轴向下：左侧 = (dy, -dx)
            return true;
        }

        // 由 from→to 方向求单位法线。
        // 约定：取**线的左侧**——屏幕上 y 轴向下，方向 (dx,dy) 的左侧是 (dy,-dx)：
        // 例如水平向右的线，(dy,-dx) = (0,-1) 即向上（人面向右时左手朝上）。
        // 这里必须与界面上的“偏移量（毫米，正=左 负=右）”以及 LineOffset 的“正值向线左侧”一致；
        // 此前写成 (-dy,dx)（即线的右侧），导致“自定义虚线里设置正偏移，线段却往右下方跑”。
        private static PointF NormalOf(PointF to, PointF from)
        {
            float dx = to.X - from.X, dy = to.Y - from.Y;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len == 0) return new PointF(0, 0);
            return new PointF(dy / len, -dx / len);
        }

        // 普通线型（实线/虚线/点线）的画笔：**端头一律用 Flat（平头）**。
        // 圆头（Round）会让每一条线/每一个虚线段沿线方向多出半个线宽，端头还是半圆；
        // 平头则严格止于端点，虚线段与点线画出来就是方正的短块，与自定义虚线的填充带来回一致。
        // 折点仍用 Round 连接（只在拐角处过渡，不会溢出）。
        private static Pen LinePen(SimpleLineSymbol symbol, double dpm)
        {
            var pen = new Pen(symbol.Color, (float)Math.Max(0.5, ToPixels(symbol.Size, dpm)));
            pen.DashStyle = symbol.Style == SimpleLineSymbolStyleConstant.Dash ? DashStyle.Dash :
                symbol.Style == SimpleLineSymbolStyleConstant.Dot ? DashStyle.Dot : DashStyle.Solid;
            pen.LineJoin = LineJoin.Round;
            pen.StartCap = LineCap.Flat;
            pen.EndCap = LineCap.Flat;
            // GDI+ 对“非实线”的画笔不看 StartCap/EndCap，虚线段/点线的端头由 DashCap 决定，这里显式指定平头
            pen.DashCap = DashCap.Flat;
            return pen;
        }

        // 自定义虚线：沿折线按 DashElements 循环绘制，支持边框(casing)与端点垂直短划线。
        // 每段都用**填充带状多边形**画（而不是 GDI+ 画笔）：画笔的两端是圆头，会把每段画成
        // “半圆 + 矩形 + 半圆”，并且沿线的方向超出段长半个线宽（边框更宽、溢出更多）；
        // 填充方式得到的是端头平整的矩形/带状，正好限制在本段之内。
        //
        // perpendicularOffset：本段画完之后整体沿法线平移多少像素（画偏移线时用）。
        // 注意图案是**在传入的这条折线上排布**的，偏移线传的是基准线 + 平移量，
        // 因此各条偏移线的虚实分界与段长完全一致（拐角处也不会因内外侧长短不同而错开）。
        private static void DrawCustomDash(Graphics g, PointF[] pts, SimpleLineSymbol symbol, double dpm,
            float perpendicularOffset = 0f)
        {
            int n = symbol.DashElements.Count;
            if (n == 0) return;
            double[] cum = Cumulative(pts);
            double total = cum[cum.Length - 1];
            if (total <= 0) return;

            var coreWidths = new float[n];
            var casingWidths = new float[n];
            var coreColors = new Color[n];
            var casingColors = new Color[n];
            var tickPens = new Pen[n];
            for (int i = 0; i < n; i++)
            {
                LineDashElement el = symbol.DashElements[i];
                // 边框（casing）画在**本段线宽的两端**：边框宽度是“线宽两侧各加多少毫米”，
                // 因此边框总宽 = 线宽 + 2×边框宽（此前直接拿边框宽当总宽，导致边框没有包住线宽两侧）。
                bool hasCasing = el.OutlineWidth > 0 && el.OutlineColor.A > 0;
                casingWidths[i] = hasCasing ? (float)Math.Max(0.5, ToPixels(Math.Max(el.Width, 0.01) + 2 * el.OutlineWidth, dpm)) : 0f;
                casingColors[i] = el.OutlineColor;
                coreWidths[i] = (float)Math.Max(0.5, ToPixels(Math.Max(el.Width, 0.01), dpm));
                coreColors[i] = el.Color;
                tickPens[i] = MakeLinePen(el.TickColor.A == 0 ? el.Color : el.TickColor, Math.Max(el.Width, 0.01), dpm);
            }
            try
            {
                double cursor = 0;
                int idx = 0;
                while (cursor < total)
                {
                    int i = idx % n;
                    idx++;
                    LineDashElement element = symbol.DashElements[i];
                    double len = ToPixels(element.Length, dpm);
                    if (len <= 0.01) len = 0.01;
                    double to = Math.Min(cursor + len, total);
                    // 该段的最终几何：水平偏移 → 延长 → 弧线 → 垂直偏移
                    PointF[] sub = ElementGeometry(pts, cum, cursor, to, element, dpm);
                    // 偏移线：把这一段整体沿法线平移（图案本身仍在基准线上排布，见方法说明）
                    if (sub != null && sub.Length >= 2 && perpendicularOffset != 0f)
                        sub = OffsetPolyline(sub, perpendicularOffset);
                    if (sub != null && sub.Length >= 2)
                    {
                        if (casingWidths[i] > 0) FillRibbon(g, sub, casingWidths[i], casingColors[i]);
                        FillRibbon(g, sub, coreWidths[i], coreColors[i]);
                        if (element.TickLength > 0)
                        {
                            float tickPx = (float)Math.Max(0.5, ToPixels(element.TickLength, dpm));
                            DrawTick(g, sub[0], DirectionAt(sub, 0), tickPx, tickPens[i]);
                            DrawTick(g, sub[sub.Length - 1], DirectionAt(sub, sub.Length - 2), tickPx, tickPens[i]);
                        }
                    }
                    cursor = to;
                    if (to >= total) break;
                }
            }
            finally
            {
                for (int i = 0; i < n; i++) tickPens[i].Dispose();
            }
        }

        /// <summary>
        /// 沿折线画一条**两端平整**的带状（线段就是矩形）：把每个顶点按法线向两侧各让开 width/2，
        /// 一侧正序、另一侧逆序组成多边形后填充。折点处取两段法线的平均，拐弯不会撕开。
        /// </summary>
        private static void FillRibbon(Graphics g, PointF[] pts, float width, Color color)
        {
            if (pts == null || pts.Length < 2 || width <= 0.01f || color.A == 0) return;
            int n = pts.Length;
            float half = width / 2f;
            var polygon = new PointF[n * 2];
            for (int i = 0; i < n; i++)
            {
                PointF normal = RibbonNormal(pts, i);
                polygon[i] = new PointF(pts[i].X + normal.X * half, pts[i].Y + normal.Y * half);
                polygon[n * 2 - 1 - i] = new PointF(pts[i].X - normal.X * half, pts[i].Y - normal.Y * half);
            }
            using (var brush = new SolidBrush(color)) g.FillPolygon(brush, polygon);
        }

        // 折线上第 i 个顶点处的单位法线：端点用相邻段的法线，中间点取两侧法线的平均
        private static PointF RibbonNormal(PointF[] pts, int i)        {
            PointF previous = i > 0 ? UnitNormal(pts[i - 1], pts[i]) : PointF.Empty;
            PointF next = i < pts.Length - 1 ? UnitNormal(pts[i], pts[i + 1]) : PointF.Empty;
            float x = previous.X + next.X, y = previous.Y + next.Y;
            float length = (float)Math.Sqrt(x * x + y * y);
            if (length < 1e-4f) return i > 0 ? previous : next;
            return new PointF(x / length, y / length);
        }

        private static PointF UnitNormal(PointF from, PointF to)
        {
            float dx = to.X - from.X, dy = to.Y - from.Y;
            float length = (float)Math.Sqrt(dx * dx + dy * dy);
            if (length < 1e-6f) return PointF.Empty;
            return new PointF(dy / length, -dx / length);   // 左法线
        }

        // 端点竖线的画笔：同样用平头，短划线的长度才正好是 TickLength（圆头会多出半个线宽）
        private static Pen MakeLinePen(Color color, double mmWidth, double dpm)
        {
            var pen = new Pen(color, (float)Math.Max(0.5, ToPixels(mmWidth, dpm)));
            pen.LineJoin = LineJoin.Round;
            pen.StartCap = LineCap.Flat;
            pen.EndCap = LineCap.Flat;
            return pen;
        }

        // 单段自定义线符号的最终几何：水平偏移 → 延长 → 弧线 → 垂直偏移
        // （每一步只改本段的绘制结果，段与段之间的“格子”由外层循环的 cursor 决定，因此互不影响）
        private static PointF[] ElementGeometry(PointF[] pts, double[] cum, double from, double to,
            LineDashElement element, double dpm)
        {
            double total = cum[cum.Length - 1];
            double start = from, end = to;
            // 水平偏移：本段整体沿线平移。本段占有的格子宽度不变，其他段仍从各自的格子开始，
            // 所以本段让出的那一段就空着（例如 A、B 各 5 毫米，A 水平偏移 2 毫米 → 0~2 空、2~7 A、7~10 B）。
            if (element.HorizontalOffsetEnabled && Math.Abs(element.HorizontalOffset) > 1e-6)
            {
                double shift = ToPixels(element.HorizontalOffset, dpm);
                start += shift;
                end += shift;
            }
            if (element.ExtendEnabled)
            {
                start -= Math.Max(0, ToPixels(element.ExtendLeft, dpm));
                end += Math.Max(0, ToPixels(element.ExtendRight, dpm));
            }
            start = Math.Max(0, start);
            end = Math.Min(total, end);
            if (end - start <= 0.01) return null;

            PointF[] result;
            if (element.ArcEnabled && element.ArcAmplitude != 0)
                result = BuildArcPolyline(pts, cum, start, end, element, dpm);
            else
            {
                PointF fromDir, toDir;
                result = SubPolyline(pts, cum, start, end, out fromDir, out toDir);
            }
            if (result == null || result.Length < 2) return result;

            if (element.OffsetEnabled && Math.Abs(element.Offset) > 1e-6)      // 垂直偏移（不改变本段沿线的位置）
                result = OffsetPolyline(result, (float)ToPixels(element.Offset, dpm));
            return result;
        }

        // 把一段线画成“弧线”：按半周期数把该段等分，每一份画半个椭圆（隔段交替方向，形成连续波浪）
        private static PointF[] BuildArcPolyline(PointF[] pts, double[] cum, double from, double to,
            LineDashElement element, double dpm)
        {
            int halfPeriods = Math.Max(1, (int)Math.Round(element.ArcHalfPeriods));
            double length = to - from;
            double period = length / halfPeriods;              // 每个半周期的长度
            double halfAxis = period / 2;                      // 椭圆的水平半轴
            float amplitude = (float)ToPixels(element.ArcAmplitude, dpm);
            const int steps = 12;
            var list = new List<PointF>((halfPeriods * steps) + 1);
            for (int k = 0; k < halfPeriods; k++)
            {
                double sign = (k % 2 == 0) ? 1 : -1;           // 交替方向 → 连续波浪
                for (int s = (k == 0 ? 0 : 1); s <= steps; s++)
                {
                    double theta = Math.PI * s / steps;
                    double along = halfAxis * (1 - Math.Cos(theta));   // 椭圆沿线的位置
                    double side = amplitude * Math.Sin(theta) * sign;  // 椭圆的垂向偏移
                    PointF point, dir;
                    SampleAt(pts, cum, from + k * period + along, out point, out dir);
                    var normal = new PointF(-dir.Y, dir.X);
                    list.Add(new PointF(point.X + normal.X * (float)side, point.Y + normal.Y * (float)side));
                }
            }
            return list.ToArray();
        }

        // 取折线上指定弧长处的位置与该处的单位切向
        private static void SampleAt(PointF[] pts, double[] cum, double d, out PointF point, out PointF dir)
        {
            int n = pts.Length;
            double total = cum[n - 1];
            if (d <= 0) { point = pts[0]; dir = DirectionAt(pts, 0); return; }
            if (d >= total) { point = pts[n - 1]; dir = DirectionAt(pts, n - 2); return; }
            int i = 1;
            while (i < n - 1 && cum[i] < d) i++;
            double segLen = cum[i] - cum[i - 1];
            double t = segLen <= 0 ? 0 : (d - cum[i - 1]) / segLen;
            point = new PointF((float)(pts[i - 1].X + t * (pts[i].X - pts[i - 1].X)),
                               (float)(pts[i - 1].Y + t * (pts[i].Y - pts[i - 1].Y)));
            dir = DirectionAt(pts, i - 1);
        }

        private static PointF DirectionAt(PointF[] pts, int index)
        {
            if (pts.Length < 2) return new PointF(1, 0);
            int i = Math.Max(0, Math.Min(pts.Length - 2, index));
            float dx = pts[i + 1].X - pts[i].X, dy = pts[i + 1].Y - pts[i].Y;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            return len <= 0 ? new PointF(1, 0) : new PointF(dx / len, dy / len);
        }

        private static void DrawTick(Graphics g, PointF p, PointF dir, float tickPx, Pen pen)
        {
            float px = -dir.Y, py = dir.X;
            var a = new PointF(p.X - px * tickPx / 2, p.Y - py * tickPx / 2);
            var b = new PointF(p.X + px * tickPx / 2, p.Y + py * tickPx / 2);
            g.DrawLine(pen, a, b);
        }

        private static double[] Cumulative(PointF[] pts)
        {
            double[] cum = new double[pts.Length];
            cum[0] = 0;
            for (int i = 1; i < pts.Length; i++)
            {
                float dx = pts[i].X - pts[i - 1].X, dy = pts[i].Y - pts[i - 1].Y;
                cum[i] = cum[i - 1] + Math.Sqrt(dx * dx + dy * dy);
            }
            return cum;
        }

        private static PointF PointAt(PointF[] pts, double[] cum, double d)
        {
            if (d <= 0) return pts[0];
            if (d >= cum[cum.Length - 1]) return pts[pts.Length - 1];
            for (int i = 0; i < cum.Length - 1; i++)
            {
                if (d >= cum[i] && d <= cum[i + 1])
                {
                    double t = (cum[i + 1] - cum[i]) == 0 ? 0 : (d - cum[i]) / (cum[i + 1] - cum[i]);
                    return new PointF((float)(pts[i].X + (pts[i + 1].X - pts[i].X) * t),
                        (float)(pts[i].Y + (pts[i + 1].Y - pts[i].Y) * t));
                }
            }
            return pts[pts.Length - 1];
        }

        private static PointF DirectionAt(PointF[] pts, double[] cum, double d)
        {
            int seg = 0;
            for (int i = 0; i < cum.Length - 1; i++) { if (d <= cum[i + 1]) { seg = i; break; } }
            if (seg >= pts.Length - 1) seg = pts.Length - 2;
            float dx = pts[seg + 1].X - pts[seg].X, dy = pts[seg + 1].Y - pts[seg].Y;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            return len == 0 ? new PointF(1, 0) : new PointF(dx / len, dy / len);
        }

        private static PointF[] SubPolyline(PointF[] pts, double[] cum, double from, double to,
            out PointF dirFrom, out PointF dirTo)
        {
            var list = new List<PointF>();
            list.Add(PointAt(pts, cum, from));
            dirFrom = DirectionAt(pts, cum, from);
            for (int i = 0; i < cum.Length; i++)
                if (cum[i] > from && cum[i] < to) list.Add(pts[i]);
            PointF end = PointAt(pts, cum, to);
            if (list.Count == 0 || list[list.Count - 1] != end) list.Add(end);
            dirTo = DirectionAt(pts, cum, to);
            return list.ToArray();
        }

        #endregion

        #region 注记

        /// <summary>绘制一条注记（地图与编辑器预览共用同一套代码，保证“预览所见即地图所得”）。</summary>
        public static void DrawTextLabel(Graphics g, string text, PointF location, TextSymbol ts, double angle)
        {
            if (ts == null) ts = new TextSymbol();
            FontStyle style = FontStyle.Regular;
            if (ts.Bold) style |= FontStyle.Bold;
            if (ts.Italic) style |= FontStyle.Italic;
            using (var font = new Font(ts.FontName, ts.FontSize, style))
            {
                var state = g.Save();
                try
                {
                    if (angle != 0)
                    {
                        // 围绕注记锚点旋转（RotateAngle 为逆时针角度，GDI+ 的 RotateTransform 顺时针为正，故取负）
                        g.TranslateTransform(location.X, location.Y);
                        g.RotateTransform((float)-angle);
                        g.TranslateTransform(-location.X, -location.Y);
                    }
                    bool stretch = Math.Abs(ts.FontRatio - 1.0) > 1e-6;   // 宽高比 ≠ 1 时需要按文字轮廓拉伸
                    if (!stretch && !ts.UseMask)
                    {
                        using (var brush = new SolidBrush(ts.FontColor))
                            g.DrawString(text, font, brush, location);
                    }
                    else
                    {
                        // 取文字轮廓路径：既能描边（晕圈），也能按宽高比水平缩放
                        using (var path = new GraphicsPath())
                        {
                            path.AddString(text, font.FontFamily, (int)font.Style, font.SizeInPoints,
                                location, StringFormat.GenericDefault);
                            if (stretch)
                            {
                                using (var matrix = new Matrix())
                                {
                                    matrix.Translate(-location.X, -location.Y, MatrixOrder.Append);
                                    matrix.Scale((float)ts.FontRatio, 1f, MatrixOrder.Append);
                                    matrix.Translate(location.X, location.Y, MatrixOrder.Append);
                                    path.Transform(matrix);
                                }
                            }
                            if (ts.UseMask)
                            {
                                using (var pen = new Pen(ts.MaskColor, (float)Math.Max(0.5, ToPixels(ts.MaskWidth, g.DpiX / 0.0254))))
                                    g.DrawPath(pen, path);
                            }
                            using (var brush = new SolidBrush(ts.FontColor))
                                g.FillPath(brush, path);
                        }
                    }
                }
                finally { g.Restore(state); }
            }
        }

        #endregion

        #region 符号预览（图例 / LayerControl / 编辑器）

        public static void DrawSymbol(Graphics g, Symbol symbol, Rectangle rect)
        {
            if (symbol == null) return;
            double dpm = g.DpiX / 0.0254;   // 每米点数；符号尺寸单位为毫米，必须用 dpm 而非 dpi
            if (symbol is SimpleMarkerSymbol marker)
            {
                float size = (float)ToPixels(marker.Size, dpm);
                size = Math.Max(2, Math.Min(size, Math.Min(rect.Width, rect.Height)));
                DrawMarkerAt(g, new PointF(rect.Left + rect.Width / 2f, rect.Top + rect.Height / 2f), marker, size);
            }
            else if (symbol is SimpleLineSymbol line)
            {
                var pts = new[] { new PointF(rect.Left + 3, rect.Top + rect.Height / 2f),
                    new PointF(rect.Right - 3, rect.Top + rect.Height / 2f) };
                DrawLineScreen(g, pts, line, dpm);
            }
            else if (symbol is SimpleFillSymbol fill)
            {
                var r = new RectangleF(rect.Left + 3, rect.Top + 3, rect.Width - 6, rect.Height - 6);
                if (fill.Color.A > 0) using (var b = new SolidBrush(fill.Color)) g.FillRectangle(b, r);
                foreach (FillOutline o in fill.Outlines)
                {
                    if (o == null || o.Outline == null) continue;
                    float off = (float)ToPixels(o.Offset, dpm);
                    var r2 = RectangleF.Inflate(r, off, off);
                    var ring = new[] { new PointF(r2.Left, r2.Top), new PointF(r2.Right, r2.Top),
                        new PointF(r2.Right, r2.Bottom), new PointF(r2.Left, r2.Bottom), new PointF(r2.Left, r2.Top) };
                    DrawFillOutlineRing(g, ring, o.Outline, dpm);
                }
            }
        }

        #endregion

        private static double ToPixels(double mm, double dpm)
        {
            return mm / 1000.0 * dpm;
        }
    }
}
