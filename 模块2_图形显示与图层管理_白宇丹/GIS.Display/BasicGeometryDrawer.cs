using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;

namespace GIS.Display
{
    // 从旧mgisMapDrawingTools的绘图思路适配：统一使用GIS.Geometry和GIS.Symbol。
    public sealed class BasicGeometryDrawer : ILayerRenderer
    {
        public void DrawLayer(Graphics graphics, Layer layer, MapTransform transform, Rectangle viewport)
        {
            Envelope extent = transform.GetExtent();
            foreach (Feature feature in layer.FeatureClass.Features)
            {
                if (feature.Geometry == null || feature.Geometry.IsEmpty) continue;
                // 留出符号尺寸，避免中心刚好在视窗外的点突然消失。
                Symbol symbol = layer.Symbol ?? DefaultSymbol(feature.Geometry);
                double margin = SymbolMargin(symbol, transform) * transform.MapUnitsPerPixel();
                var expanded = new Envelope(extent.MinX - margin, extent.MaxX + margin,
                    extent.MinY - margin, extent.MaxY + margin);
                if (feature.GetEnvelope().IntersectsWith(expanded))
                    DrawGeometry(graphics, feature.Geometry, symbol, transform);
            }
        }

        public static Symbol DefaultSymbol(Geometry geometry)
        {
            if (geometry is Point || geometry is MultiPoint)
                return new SimpleMarkerSymbol { Color = Color.FromArgb(183, 63, 63), Size = 3 };
            if (geometry is LineString || geometry is MultiLineString)
                return new SimpleLineSymbol { Color = Color.FromArgb(65, 110, 166), Size = 0.6 };
            return new SimpleFillSymbol {
                Color = Color.FromArgb(216, 231, 207),
                Outline = new SimpleLineSymbol { Color = Color.FromArgb(89, 123, 84), Size = 0.3 }
            };
        }

        public static Symbol HighlightSymbol(Geometry geometry)
        {
            if (geometry is Point || geometry is MultiPoint)
                return new SimpleMarkerSymbol { Color = Color.DeepPink, Size = 4.5,
                    Style = SimpleMarkerSymbolStyleConstant.HollowCircle };
            if (geometry is LineString || geometry is MultiLineString)
                return new SimpleLineSymbol { Color = Color.DeepPink, Size = 1 };
            return new SimpleFillSymbol {
                Color = Color.FromArgb(55, Color.DeepPink),
                Outline = new SimpleLineSymbol { Color = Color.DeepPink, Size = 0.8 }
            };
        }

        private static double SymbolMargin(Symbol symbol, MapTransform transform)
        {
            var marker = symbol as SimpleMarkerSymbol;
            var line = symbol as SimpleLineSymbol;
            var fill = symbol as SimpleFillSymbol;
            double size = marker != null ? marker.Size : line != null ? line.Size :
                fill?.Outline != null ? fill.Outline.Size : 1;
            return Math.Max(2, size * transform.Dpm / 1000.0);
        }

        public static void DrawGeometry(Graphics graphics, Geometry geometry, Symbol symbol, MapTransform transform)
        {
            if (geometry == null || geometry.IsEmpty) return;
            if (geometry is Point point)
                DrawPoint(graphics, new Coordinate(point.X, point.Y), symbol as SimpleMarkerSymbol, transform);
            else if (geometry is MultiPoint multiPoint)
                foreach (Coordinate p in multiPoint.Points)
                    DrawPoint(graphics, p, symbol as SimpleMarkerSymbol, transform);
            else if (geometry is LineString line)
                DrawLine(graphics, line, symbol as SimpleLineSymbol, transform);
            else if (geometry is MultiLineString multiLine)
                foreach (LineString part in multiLine.Parts)
                    DrawLine(graphics, part, symbol as SimpleLineSymbol, transform);
            else if (geometry is Polygon polygon)
                DrawPolygon(graphics, polygon, symbol as SimpleFillSymbol, transform);
            else if (geometry is MultiPolygon multiPolygon)
                foreach (Polygon part in multiPolygon.Parts)
                    DrawPolygon(graphics, part, symbol as SimpleFillSymbol, transform);
        }

        private static PointF Screen(Coordinate c, MapTransform transform)
        {
            // 不先取整，保持细线和平移过程平滑。
            double pixelsPerUnit = 1 / transform.MapUnitsPerPixel();
            return new PointF((float)((c.X - transform.MapOffsetX) * pixelsPerUnit),
                (float)((transform.MapOffsetY - c.Y) * pixelsPerUnit));
        }

        private static Pen LinePen(SimpleLineSymbol symbol, MapTransform transform)
        {
            if (symbol == null) symbol = new SimpleLineSymbol { Color = Color.SlateGray };
            var pen = new Pen(symbol.Color, (float)Math.Max(0.5, symbol.Size * transform.Dpm / 1000));
            pen.DashStyle = symbol.Style == SimpleLineSymbolStyleConstant.Dash ? DashStyle.Dash :
                symbol.Style == SimpleLineSymbolStyleConstant.Dot ? DashStyle.Dot : DashStyle.Solid;
            pen.LineJoin = LineJoin.Round;
            return pen;
        }

        private static void DrawLine(Graphics g, LineString line, SimpleLineSymbol symbol, MapTransform transform)
        {
            if (line.IsEmpty) return;
            using (Pen pen = LinePen(symbol, transform))
                g.DrawLines(pen, line.Points.Select(p => Screen(p, transform)).ToArray());
        }

        private static void DrawPolygon(Graphics g, Polygon polygon, SimpleFillSymbol symbol, MapTransform transform)
        {
            if (polygon.IsEmpty) return;
            if (symbol == null) symbol = (SimpleFillSymbol)DefaultSymbol(polygon);
            using (var path = new GraphicsPath(FillMode.Alternate))
            {
                path.AddPolygon(polygon.ExteriorRing.Select(p => Screen(p, transform)).ToArray());
                foreach (Points hole in polygon.Holes)
                    if (hole.Count >= 3)
                        path.AddPolygon(hole.Select(p => Screen(p, transform)).ToArray());
                using (var brush = new SolidBrush(symbol.Color)) g.FillPath(brush, path);
                if (symbol.Outline != null)
                    using (Pen pen = LinePen(symbol.Outline, transform)) g.DrawPath(pen, path);
            }
        }

        private static void DrawPoint(Graphics g, Coordinate coordinate, SimpleMarkerSymbol symbol, MapTransform transform)
        {
            if (symbol == null) symbol = new SimpleMarkerSymbol { Color = Color.Firebrick };
            PointF p = Screen(coordinate, transform);
            float size = (float)Math.Max(1, symbol.Size * transform.Dpm / 1000);
            float half = size / 2;
            var rect = new RectangleF(p.X - half, p.Y - half, size, size);
            using (var brush = new SolidBrush(symbol.Color))
            using (var pen = new Pen(symbol.Color, Math.Max(1, size / 7)))
            {
                switch (symbol.Style)
                {
                    case SimpleMarkerSymbolStyleConstant.HollowCircle: g.DrawEllipse(pen, rect); break;
                    case SimpleMarkerSymbolStyleConstant.SolidSquare: g.FillRectangle(brush, rect); break;
                    case SimpleMarkerSymbolStyleConstant.SolidTriangle:
                        g.FillPolygon(brush, new[] { new PointF(p.X, p.Y - half),
                            new PointF(p.X + half, p.Y + half), new PointF(p.X - half, p.Y + half) }); break;
                    case SimpleMarkerSymbolStyleConstant.Cross:
                        g.DrawLine(pen, p.X - half, p.Y, p.X + half, p.Y);
                        g.DrawLine(pen, p.X, p.Y - half, p.X, p.Y + half); break;
                    default: g.FillEllipse(brush, rect); break;
                }
            }
        }
    }
}

