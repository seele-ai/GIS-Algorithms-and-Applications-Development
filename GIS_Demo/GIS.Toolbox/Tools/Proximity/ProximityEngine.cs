using System;
using System.Collections.Generic;
using System.Linq;
using GIS;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Buffer;
using NetTopologySuite.Operation.Union;
using NetTopologySuite.Triangulate;
using GisCoordinate = GIS.Coordinate;
using GisGeometry = GIS.Geometry;
using GisLineString = GIS.LineString;
using GisPoint = GIS.Point;
using GisPolygon = GIS.Polygon;
using NtsCoordinate = NetTopologySuite.Geometries.Coordinate;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;
using NtsLineString = NetTopologySuite.Geometries.LineString;
using NtsPoint = NetTopologySuite.Geometries.Point;
using NtsPolygon = NetTopologySuite.Geometries.Polygon;

namespace GIS.Toolbox.Tools.Proximity
{
    /// <summary>
    /// 邻近分析算法引擎。界面与项目仍使用自有 GIS.Geometry，复杂的二维缓冲、
    /// 融合和差运算在内部转换为 OGC 简单要素拓扑对象执行，再转换回来。
    /// </summary>
    public static class ProximityEngine
    {
        public enum BufferMethod
        {
            Planar,
            Geodesic
        }

        public enum BufferSide
        {
            Full,
            Left,
            Right
        }

        public enum BufferEnd
        {
            Round,
            Flat,
            Square
        }

        public sealed class BufferOptions
        {
            public BufferMethod Method = BufferMethod.Planar;
            public BufferSide Side = BufferSide.Full;
            public BufferEnd End = BufferEnd.Round;
            public ProjectionCS Projection;
            public int? Srid;
        }

        private static readonly GeometryFactory Factory =
            NtsGeometryServices.Instance.CreateGeometryFactory();

        private const double MeanEarthRadius = 6371008.8;

        /// <summary>一个泰森多边形站点及其来源要素。</summary>
        public sealed class ThiessenSite
        {
            public GisCoordinate Coordinate;
            public Feature Source;
            public int SourceIndex;
        }

        /// <summary>泰森多边形结果及对应站点。</summary>
        public sealed class ThiessenCell
        {
            public GisGeometry Geometry;
            public ThiessenSite Site;
        }

        /// <summary>为点、线、面及其复合类型创建正距离平面缓冲区。</summary>
        public static GisGeometry Buffer(GisGeometry geometry, double distance)
        {
            return Buffer(geometry, distance, new BufferOptions());
        }

        /// <summary>
        /// 按指定方法创建缓冲区。平面方法的距离使用几何坐标单位；测地线方法的距离使用米，
        /// 并在要素中心建立局部等距方位投影后计算。点缓冲的半径为严格球面距离，线面缓冲
        /// 为局部等距近似，适合不跨日期变更线的区域数据。
        /// </summary>
        public static GisGeometry Buffer(GisGeometry geometry, double distance, BufferOptions options)
        {
            if (geometry == null || geometry.IsEmpty)
                return null;
            if (double.IsNaN(distance) || double.IsInfinity(distance) || distance == 0)
                throw new ArgumentOutOfRangeException("distance", "缓冲距离不能为 0，且必须是有限数值。");

            options = options ?? new BufferOptions();
            if (options.Method == BufferMethod.Geodesic)
                return GeodesicBuffer(geometry, distance, options);
            return PlanarBuffer(geometry, distance, options);
        }

        private static GisGeometry PlanarBuffer(GisGeometry geometry, double distance, BufferOptions options)
        {
            NtsGeometry source = ToNts(geometry);
            if (source == null || source.IsEmpty) return null;
            BufferParameters parameters = CreateBufferParameters(options);
            double operationDistance = options.Side == BufferSide.Right ? -Math.Abs(distance) : distance;
            NtsGeometry buffered = BufferOp.Buffer(source, operationDistance, parameters);
            return FromPolygonalNts(buffered);
        }

        private static GisGeometry GeodesicBuffer(GisGeometry geometry, double distance, BufferOptions options)
        {
            GisGeometry geographic = geometry.Clone();
            if (options.Projection != null)
                Layer.TransformGeometry(geographic, options.Projection, null);
            Envelope envelope = geographic.GetEnvelope();
            if (envelope.IsNull) return null;
            if (envelope.Width > 180)
                throw new InvalidOperationException("测地线缓冲暂不支持跨越 180° 日期变更线的要素。");

            double longitude0 = envelope.CenterX;
            double latitude0 = Math.Max(-89.999999, Math.Min(89.999999, envelope.CenterY));
            GisGeometry local = geographic.Clone();
            TransformGeometry(local, coordinate => AzimuthalEquidistantForward(coordinate, longitude0, latitude0));
            BufferOptions localOptions = new BufferOptions
            {
                Method = BufferMethod.Planar,
                Side = options.Side,
                End = options.End
            };
            GisGeometry localBuffer = PlanarBuffer(local, distance, localOptions);
            if (localBuffer == null) return null;
            TransformGeometry(localBuffer, coordinate => AzimuthalEquidistantInverse(coordinate, longitude0, latitude0));
            if (options.Projection != null)
                Layer.TransformGeometry(localBuffer, null, options.Projection);
            return localBuffer;
        }

        private static BufferParameters CreateBufferParameters(BufferOptions options)
        {
            var parameters = new BufferParameters
            {
                QuadrantSegments = 12,
                JoinStyle = JoinStyle.Round,
                IsSingleSided = options.Side != BufferSide.Full
            };
            switch (options.End)
            {
                case BufferEnd.Flat: parameters.EndCapStyle = EndCapStyle.Flat; break;
                case BufferEnd.Square: parameters.EndCapStyle = EndCapStyle.Square; break;
                default: parameters.EndCapStyle = EndCapStyle.Round; break;
            }
            return parameters;
        }

        private static GisCoordinate AzimuthalEquidistantForward(GisCoordinate coordinate,
            double longitude0Degrees, double latitude0Degrees)
        {
            double longitude = ProjectionCS.DegToRad(coordinate.X);
            double latitude = ProjectionCS.DegToRad(coordinate.Y);
            double longitude0 = ProjectionCS.DegToRad(longitude0Degrees);
            double latitude0 = ProjectionCS.DegToRad(latitude0Degrees);
            double deltaLongitude = longitude - longitude0;
            double sinLatitude = Math.Sin(latitude);
            double cosLatitude = Math.Cos(latitude);
            double sinLatitude0 = Math.Sin(latitude0);
            double cosLatitude0 = Math.Cos(latitude0);
            double cosC = sinLatitude0 * sinLatitude + cosLatitude0 * cosLatitude * Math.Cos(deltaLongitude);
            cosC = Math.Max(-1d, Math.Min(1d, cosC));
            double c = Math.Acos(cosC);
            double sinC = Math.Sin(c);
            double k = Math.Abs(c) < 1e-14 || Math.Abs(sinC) < 1e-14 ? 1d : c / sinC;
            double x = MeanEarthRadius * k * cosLatitude * Math.Sin(deltaLongitude);
            double y = MeanEarthRadius * k * (cosLatitude0 * sinLatitude -
                sinLatitude0 * cosLatitude * Math.Cos(deltaLongitude));
            return new GisCoordinate(x, y);
        }

        private static GisCoordinate AzimuthalEquidistantInverse(GisCoordinate coordinate,
            double longitude0Degrees, double latitude0Degrees)
        {
            double longitude0 = ProjectionCS.DegToRad(longitude0Degrees);
            double latitude0 = ProjectionCS.DegToRad(latitude0Degrees);
            double rho = Math.Sqrt(coordinate.X * coordinate.X + coordinate.Y * coordinate.Y);
            if (rho < 1e-12) return new GisCoordinate(longitude0Degrees, latitude0Degrees);
            double c = rho / MeanEarthRadius;
            double sinC = Math.Sin(c);
            double cosC = Math.Cos(c);
            double sinLatitude0 = Math.Sin(latitude0);
            double cosLatitude0 = Math.Cos(latitude0);
            double latitude = Math.Asin(cosC * sinLatitude0 +
                coordinate.Y * sinC * cosLatitude0 / rho);
            double longitude = longitude0 + Math.Atan2(coordinate.X * sinC,
                rho * cosLatitude0 * cosC - coordinate.Y * sinLatitude0 * sinC);
            double longitudeDegrees = ProjectionCS.RadToDeg(longitude);
            while (longitudeDegrees > 180) longitudeDegrees -= 360;
            while (longitudeDegrees < -180) longitudeDegrees += 360;
            return new GisCoordinate(longitudeDegrees, ProjectionCS.RadToDeg(latitude));
        }

        private static void TransformGeometry(GisGeometry geometry, Func<GisCoordinate, GisCoordinate> transform)
        {
            if (geometry is GisPoint)
            {
                var point = (GisPoint)geometry;
                point.Coordinate = transform(point.Coordinate);
            }
            else if (geometry is GisLineString)
                TransformPoints(((GisLineString)geometry).Points, transform);
            else if (geometry is GisPolygon)
            {
                var polygon = (GisPolygon)geometry;
                TransformPoints(polygon.ExteriorRing, transform);
                foreach (Points hole in polygon.Holes) TransformPoints(hole, transform);
            }
            else if (geometry is GIS.MultiPoint)
                TransformPoints(((GIS.MultiPoint)geometry).Points, transform);
            else if (geometry is GIS.MultiLineString)
            {
                foreach (GisLineString line in ((GIS.MultiLineString)geometry).Parts)
                    TransformPoints(line.Points, transform);
            }
            else if (geometry is GIS.MultiPolygon)
            {
                foreach (GisPolygon polygon in ((GIS.MultiPolygon)geometry).Parts)
                {
                    TransformPoints(polygon.ExteriorRing, transform);
                    foreach (Points hole in polygon.Holes) TransformPoints(hole, transform);
                }
            }
        }

        private static void TransformPoints(Points points, Func<GisCoordinate, GisCoordinate> transform)
        {
            for (int i = 0; i < points.Count; i++) points.SetItem(i, transform(points[i]));
        }

        /// <summary>把若干面几何融合，删除重叠边界；不连通结果返回复合多边形。</summary>
        public static GisGeometry Union(IEnumerable<GisGeometry> geometries)
        {
            var items = new List<NtsGeometry>();
            if (geometries != null)
            {
                foreach (GisGeometry geometry in geometries)
                {
                    NtsGeometry nts = ToNts(geometry);
                    if (nts != null && !nts.IsEmpty) items.Add(nts);
                }
            }
            if (items.Count == 0) return null;
            NtsGeometry union = UnaryUnionOp.Union(items, Factory);
            return FromPolygonalNts(union);
        }

        /// <summary>返回 subject 减去 erase 后的面结果。</summary>
        public static GisGeometry Difference(GisGeometry subject, GisGeometry erase)
        {
            NtsGeometry left = ToNts(subject);
            if (left == null || left.IsEmpty) return null;
            NtsGeometry right = ToNts(erase);
            if (right == null || right.IsEmpty) return FromPolygonalNts(left);
            return FromPolygonalNts(left.Difference(right));
        }

        /// <summary>
        /// 按 ArcGIS Pro 的默认范围规则生成泰森多边形：输入点范围的宽、高各向两侧扩展 10%。
        /// 重复坐标只保留第一条站点记录，并通过 duplicateCount 返回跳过数量。
        /// </summary>
        public static List<ThiessenCell> CreateThiessen(
            IEnumerable<ThiessenSite> inputSites, out int duplicateCount)
        {
            return CreateThiessen(inputSites, null, out duplicateCount);
        }

        /// <summary>使用指定裁剪范围生成泰森多边形；范围为 null 时使用默认外扩 10% 规则。</summary>
        public static List<ThiessenCell> CreateThiessen(
            IEnumerable<ThiessenSite> inputSites, Envelope requestedExtent, out int duplicateCount)
        {
            duplicateCount = 0;
            var sites = new List<ThiessenSite>();
            if (inputSites != null)
            {
                foreach (ThiessenSite site in inputSites)
                {
                    if (site == null || double.IsNaN(site.Coordinate.X) || double.IsNaN(site.Coordinate.Y) ||
                        double.IsInfinity(site.Coordinate.X) || double.IsInfinity(site.Coordinate.Y))
                        continue;
                    if (sites.Any(s => SameCoordinate(s.Coordinate, site.Coordinate)))
                    {
                        duplicateCount++;
                        continue;
                    }
                    sites.Add(site);
                }
            }
            if (sites.Count == 0)
                return new List<ThiessenCell>();

            NetTopologySuite.Geometries.Envelope extent = ThiessenExtent(sites, requestedExtent);
            NtsPolygon clipPolygon = Factory.ToGeometry(extent) as NtsPolygon;
            if (sites.Count == 1)
            {
                return new List<ThiessenCell>
                {
                    new ThiessenCell { Geometry = FromPolygonalNts(clipPolygon), Site = sites[0] }
                };
            }

            var coordinates = sites.Select(s => new NtsCoordinate(s.Coordinate.X, s.Coordinate.Y)).ToList();
            var builder = new VoronoiDiagramBuilder();
            builder.SetSites(coordinates);
            builder.ClipEnvelope = extent;
            builder.Tolerance = ExtentTolerance(extent);
            NtsGeometry diagram = builder.GetDiagram(Factory);

            var cells = new List<ThiessenCell>();
            for (int i = 0; i < diagram.NumGeometries; i++)
            {
                NtsGeometry face = diagram.GetGeometryN(i);
                NtsCoordinate tag = face.UserData as NtsCoordinate;
                ThiessenSite site = tag == null ? NearestSite(face.Centroid.Coordinate, sites)
                    : NearestSite(tag, sites);
                NtsGeometry clipped = face.Intersection(clipPolygon);
                GisGeometry geometry = FromPolygonalNts(clipped);
                if (geometry != null && !geometry.IsEmpty)
                    cells.Add(new ThiessenCell { Geometry = geometry, Site = site });
            }
            return cells;
        }

        private static NetTopologySuite.Geometries.Envelope ThiessenExtent(
            IList<ThiessenSite> sites, Envelope requestedExtent)
        {
            if (requestedExtent != null && !requestedExtent.IsNull &&
                requestedExtent.Width > 0 && requestedExtent.Height > 0)
            {
                return new NetTopologySuite.Geometries.Envelope(requestedExtent.MinX,
                    requestedExtent.MaxX, requestedExtent.MinY, requestedExtent.MaxY);
            }
            double minX = sites.Min(s => s.Coordinate.X);
            double maxX = sites.Max(s => s.Coordinate.X);
            double minY = sites.Min(s => s.Coordinate.Y);
            double maxY = sites.Max(s => s.Coordinate.Y);
            double width = maxX - minX;
            double height = maxY - minY;
            double reference = Math.Max(Math.Max(width, height), 1d);
            double expandX = width > 0 ? width * 0.1 : reference * 0.1;
            double expandY = height > 0 ? height * 0.1 : reference * 0.1;
            return new NetTopologySuite.Geometries.Envelope(
                minX - expandX, maxX + expandX, minY - expandY, maxY + expandY);
        }

        private static double ExtentTolerance(NetTopologySuite.Geometries.Envelope extent)
        {
            return Math.Max(extent.Width, extent.Height) * 1e-10;
        }

        private static bool SameCoordinate(GisCoordinate a, GisCoordinate b)
        {
            double scale = Math.Max(1d, Math.Max(Math.Abs(a.X), Math.Max(Math.Abs(a.Y),
                Math.Max(Math.Abs(b.X), Math.Abs(b.Y)))));
            double tolerance = scale * 1e-12;
            return Math.Abs(a.X - b.X) <= tolerance && Math.Abs(a.Y - b.Y) <= tolerance;
        }

        private static ThiessenSite NearestSite(NtsCoordinate coordinate, IList<ThiessenSite> sites)
        {
            ThiessenSite best = sites[0];
            double bestDistance = double.MaxValue;
            foreach (ThiessenSite site in sites)
            {
                double dx = coordinate.X - site.Coordinate.X;
                double dy = coordinate.Y - site.Coordinate.Y;
                double distance = dx * dx + dy * dy;
                if (distance < bestDistance)
                {
                    best = site;
                    bestDistance = distance;
                }
            }
            return best;
        }

        internal static NtsGeometry ToNts(GisGeometry geometry)
        {
            if (geometry == null) return null;
            switch (geometry.GeometryType)
            {
                case GeometryTypeConstant.Point:
                    GisCoordinate point = ((GisPoint)geometry).Coordinate;
                    return Factory.CreatePoint(new NtsCoordinate(point.X, point.Y));
                case GeometryTypeConstant.MultiPoint:
                    return Factory.CreateMultiPointFromCoords(ToNtsCoordinates(((GIS.MultiPoint)geometry).Points, false));
                case GeometryTypeConstant.LineString:
                    return CreateNtsLine(((GisLineString)geometry).Points);
                case GeometryTypeConstant.MultiLineString:
                    var lines = new List<NtsLineString>();
                    foreach (GisLineString line in ((GIS.MultiLineString)geometry).Parts)
                    {
                        NtsLineString ntsLine = CreateNtsLine(line.Points);
                        if (ntsLine != null) lines.Add(ntsLine);
                    }
                    return Factory.CreateMultiLineString(lines.ToArray());
                case GeometryTypeConstant.Polygon:
                    return CreateNtsPolygon((GisPolygon)geometry);
                case GeometryTypeConstant.MultiPolygon:
                    var polygons = new List<NtsPolygon>();
                    foreach (GisPolygon polygon in ((GIS.MultiPolygon)geometry).Parts)
                    {
                        NtsPolygon ntsPolygon = CreateNtsPolygon(polygon);
                        if (ntsPolygon != null) polygons.Add(ntsPolygon);
                    }
                    return Factory.CreateMultiPolygon(polygons.ToArray());
                default:
                    return null;
            }
        }

        private static NtsLineString CreateNtsLine(Points points)
        {
            NtsCoordinate[] coordinates = ToNtsCoordinates(points, false);
            if (coordinates.Length < 2) return null;
            return Factory.CreateLineString(coordinates);
        }

        private static NtsPolygon CreateNtsPolygon(GisPolygon polygon)
        {
            if (polygon == null || polygon.ExteriorRing.Count < 3) return null;
            LinearRing shell = Factory.CreateLinearRing(ToNtsCoordinates(polygon.ExteriorRing, true));
            var holes = new List<LinearRing>();
            foreach (Points hole in polygon.Holes)
            {
                if (hole.Count >= 3)
                    holes.Add(Factory.CreateLinearRing(ToNtsCoordinates(hole, true)));
            }
            return Factory.CreatePolygon(shell, holes.ToArray());
        }

        private static NtsCoordinate[] ToNtsCoordinates(Points points, bool close)
        {
            var coordinates = new List<NtsCoordinate>();
            if (points != null)
            {
                foreach (GisCoordinate coordinate in points)
                    coordinates.Add(new NtsCoordinate(coordinate.X, coordinate.Y));
            }
            if (close && coordinates.Count > 0 && !coordinates[0].Equals2D(coordinates[coordinates.Count - 1]))
                coordinates.Add(new NtsCoordinate(coordinates[0]));
            return coordinates.ToArray();
        }

        internal static GisGeometry FromPolygonalNts(NtsGeometry geometry)
        {
            if (geometry == null || geometry.IsEmpty) return null;
            var polygons = new List<NtsPolygon>();
            CollectPolygons(geometry, polygons);
            if (polygons.Count == 0) return null;
            if (polygons.Count == 1) return ToGisPolygon(polygons[0]);

            var multi = new GIS.MultiPolygon();
            foreach (NtsPolygon polygon in polygons) multi.Parts.Add(ToGisPolygon(polygon));
            return multi;
        }

        private static void CollectPolygons(NtsGeometry geometry, IList<NtsPolygon> output)
        {
            NtsPolygon polygon = geometry as NtsPolygon;
            if (polygon != null)
            {
                output.Add(polygon);
                return;
            }
            for (int i = 0; i < geometry.NumGeometries; i++)
            {
                NtsGeometry part = geometry.GetGeometryN(i);
                if (!ReferenceEquals(part, geometry)) CollectPolygons(part, output);
            }
        }

        private static GisPolygon ToGisPolygon(NtsPolygon polygon)
        {
            var result = new GisPolygon(ToGisPoints(polygon.ExteriorRing.Coordinates));
            for (int i = 0; i < polygon.NumInteriorRings; i++)
                result.Holes.Add(ToGisPoints(polygon.GetInteriorRingN(i).Coordinates));
            return result;
        }

        private static Points ToGisPoints(NtsCoordinate[] coordinates)
        {
            var points = new Points();
            int count = coordinates.Length;
            if (count > 1 && coordinates[0].Equals2D(coordinates[count - 1])) count--;
            for (int i = 0; i < count; i++)
                points.Add(new GisCoordinate(coordinates[i].X, coordinates[i].Y));
            return points;
        }
    }
}
