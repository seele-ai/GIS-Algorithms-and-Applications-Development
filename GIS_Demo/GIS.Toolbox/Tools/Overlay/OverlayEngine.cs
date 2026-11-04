using System;
using System.Collections.Generic;
using System.Linq;
using GIS;

namespace GIS.Toolbox.Tools.Overlay
{
    /// <summary>
    /// 叠加分析算法引擎（纯几何运算，不含界面）：
    /// 裁剪（Clip）/ 擦除（Erase）/ 相交（Intersect）/ 交集取反（Symmetrical Difference）。
    ///
    /// 面×面的布尔运算采用"边界打断 + 中点归类 + 环重建"三步：
    ///   1) 两个多边形（含内环）的所有边在相互交点处打断成子线段；
    ///   2) 按子线段中点是否落在对方内部筛选保留段（相交=保留落在对方内部的段；
    ///      擦除=保留落在外部的段，且对方贡献的段方向取反）；
    ///   3) 保留的子线段按首尾相接重组为环，按带符号面积区分外环（逆时针）与内环（顺时针）。
    /// 线×面采用"参数打断 + 中点归类 + 链式拼接"；点运算按包含关系过滤；线×线相交取交点。
    /// 容差按数据范围自适应（外包对角线 × 1e-8）。
    /// 已知简化：共线重叠段不做处理；全等的两个面按退化情况返回空/原样的近似结果。
    /// </summary>
    public static class OverlayEngine
    {
        private struct Segment
        {
            public Coordinate From;
            public Coordinate To;

            public Segment(Coordinate from, Coordinate to)
            {
                From = from;
                To = to;
            }
        }

        /// <summary>一次叠加运算的输出项：结果几何 + 它来自的输入要素（用于拷贝属性）。</summary>
        public sealed class ResultItem
        {
            public ResultItem(Geometry geometry, Feature source)
            {
                Geometry = geometry;
                Source = source;
            }

            public Geometry Geometry { get; private set; }
            public Feature Source { get; private set; }
        }

        #region 类型辅助

        /// <summary>几何维数：点=0、线=1、面=2，其余=-1。</summary>
        public static int DimensionOf(Geometry geometry)
        {
            switch (geometry.GeometryType)
            {
                case GeometryTypeConstant.Point:
                case GeometryTypeConstant.MultiPoint:
                    return 0;
                case GeometryTypeConstant.LineString:
                case GeometryTypeConstant.MultiLineString:
                    return 1;
                case GeometryTypeConstant.Polygon:
                case GeometryTypeConstant.MultiPolygon:
                    return 2;
                default:
                    return -1;
            }
        }

        /// <summary>取出几何中的全部坐标（点/多点）。</summary>
        public static List<Coordinate> CoordinatesOf(Geometry geometry)
        {
            var coordinates = new List<Coordinate>();
            if (geometry == null)
                return coordinates;
            if (geometry.GeometryType == GeometryTypeConstant.Point)
            {
                Point point = (Point)geometry;
                coordinates.Add(new Coordinate(point.X, point.Y));
            }
            else if (geometry.GeometryType == GeometryTypeConstant.MultiPoint)
            {
                Points points = ((MultiPoint)geometry).Points;
                for (int i = 0; i <= points.Count - 1; i++) coordinates.Add(points.GetItem(i));
            }
            return coordinates;
        }

        /// <summary>取出几何中的全部折线（折线/复合折线的各条折线）。</summary>
        public static List<Points> LinesOf(Geometry geometry)
        {
            var lines = new List<Points>();
            if (geometry == null)
                return lines;
            if (geometry.GeometryType == GeometryTypeConstant.LineString)
            {
                lines.Add(((LineString)geometry).Points);
            }
            else if (geometry.GeometryType == GeometryTypeConstant.MultiLineString)
            {
                LineStrings parts = ((MultiLineString)geometry).Parts;
                for (int i = 0; i <= parts.Count - 1; i++) lines.Add(parts.GetItem(i).Points);
            }
            return lines;
        }

        /// <summary>取出几何中的全部多边形（面/复合面）。</summary>
        public static List<Polygon> PolygonsOf(Geometry geometry)
        {
            var polygons = new List<Polygon>();
            if (geometry == null)
                return polygons;
            if (geometry.GeometryType == GeometryTypeConstant.Polygon)
            {
                polygons.Add((Polygon)geometry);
            }
            else if (geometry.GeometryType == GeometryTypeConstant.MultiPolygon)
            {
                Polygons parts = ((MultiPolygon)geometry).Parts;
                for (int i = 0; i <= parts.Count - 1; i++) polygons.Add(parts.GetItem(i));
            }
            return polygons;
        }

        /// <summary>点到折线（点集）的最短距离。</summary>
        public static double PointToPolylineDistance(Coordinate point, Points vertices)
        {
            int n = vertices.Count;
            if (n == 0)
                return double.MaxValue;
            if (n == 1)
                return point.DistanceTo(vertices.GetItem(0));
            double min = double.MaxValue;
            for (int i = 0; i <= n - 2; i++)
            {
                double d = GeometryTools.GetDisFromPointToSegment(point, vertices.GetItem(i), vertices.GetItem(i + 1));
                if (d < min) min = d;
            }
            return min;
        }

        #endregion

        #region 对外入口：裁剪 / 擦除 / 相交 / 交集取反

        /// <summary>裁剪：输入几何与裁剪面重叠的部分。</summary>
        public static List<ResultItem> Clip(Geometry input, List<Polygon> clipPolygons, Feature source)
        {
            var output = new List<ResultItem>();
            if (input == null || clipPolygons.Count == 0)
                return output;
            switch (DimensionOf(input))
            {
                case 0:
                    foreach (Coordinate c in CoordinatesOf(input))
                        if (InsideAny(c, clipPolygons))
                            output.Add(new ResultItem(new Point(c), source));
                    break;
                case 1:
                    foreach (Points line in LinesOf(input))
                        foreach (Points chain in ClipPolyline(line, clipPolygons, true))
                            if (chain.Count >= 2)
                                output.Add(new ResultItem(ToLineString(chain), source));
                    break;
                case 2:
                    foreach (Polygon polygon in BooleanMulti(PolygonsOf(input), clipPolygons, true))
                        output.Add(new ResultItem(polygon, source));
                    break;
            }
            return output;
        }

        /// <summary>擦除：输入几何位于擦除面之外的部分。</summary>
        public static List<ResultItem> Erase(Geometry input, List<Polygon> erasePolygons, Feature source)
        {
            var output = new List<ResultItem>();
            if (input == null)
                return output;
            if (erasePolygons.Count == 0)
            {
                output.Add(new ResultItem(input.Clone(), source));    // 没有擦除面 = 原样保留
                return output;
            }
            switch (DimensionOf(input))
            {
                case 0:
                    foreach (Coordinate c in CoordinatesOf(input))
                        if (InsideAny(c, erasePolygons) == false)
                            output.Add(new ResultItem(new Point(c), source));
                    break;
                case 1:
                    foreach (Points line in LinesOf(input))
                        foreach (Points chain in ClipPolyline(line, erasePolygons, false))
                            if (chain.Count >= 2)
                                output.Add(new ResultItem(ToLineString(chain), source));
                    break;
                case 2:
                    foreach (Polygon polygon in BooleanMulti(PolygonsOf(input), erasePolygons, false))
                        output.Add(new ResultItem(polygon, source));
                    break;
            }
            return output;
        }

        /// <summary>相交：两个几何的交集（输出维数取两者中较低的维数）。</summary>
        public static List<ResultItem> Intersect(Geometry a, Geometry b, Feature sourceA, Feature sourceB)
        {
            var output = new List<ResultItem>();
            if (a == null || b == null)
                return output;
            int dimA = DimensionOf(a), dimB = DimensionOf(b);
            int outDim = Math.Min(dimA, dimB);
            if (outDim == 2)
            {
                // 面×面
                foreach (Polygon polygon in BooleanTwo(PolygonsOf(a)[0], PolygonsOf(b)[0], true))
                    output.Add(new ResultItem(polygon, sourceA));
            }
            else if (outDim == 1)
            {
                bool aIsLine = dimA == 1, bIsLine = dimB == 1;
                var polygonsB = PolygonsOf(b);
                var polygonsA = PolygonsOf(a);
                if (aIsLine && bIsLine)
                {
                    // 线×线：输出线段交点（降维为点）
                    double tol = ToleranceOf(a, b);
                    var points = new List<Coordinate>();
                    foreach (Points lineA in LinesOf(a))
                        foreach (Points lineB in LinesOf(b))
                            points.AddRange(LineCrossingPoints(lineA, lineB));
                    foreach (Coordinate c in Dedup(points, tol))
                        output.Add(new ResultItem(new Point(c), sourceA));
                }
                else if (bIsLine == false)
                {
                    // 线×面：线落在面内的部分
                    foreach (Points line in LinesOf(a))
                        foreach (Points chain in ClipPolyline(line, polygonsB, true))
                            if (chain.Count >= 2)
                                output.Add(new ResultItem(ToLineString(chain), sourceA));
                }
                else
                {
                    // 面×线：线落在面内的部分
                    foreach (Points line in LinesOf(b))
                        foreach (Points chain in ClipPolyline(line, polygonsA, true))
                            if (chain.Count >= 2)
                                output.Add(new ResultItem(ToLineString(chain), sourceB));
                }
            }
            else
            {
                // 输出为点
                if (dimA == 0 && dimB == 0)
                {
                    // 点×点：重合的点保留一个
                    var coordsA = CoordinatesOf(a);
                    var coordsB = CoordinatesOf(b);
                    double tol = ToleranceOf(a, b);
                    foreach (Coordinate c in coordsA)
                        if (coordsB.Any(other => c.DistanceTo(other) <= tol))
                            output.Add(new ResultItem(new Point(c), sourceA));
                }
                else if (dimA == 0 && dimB == 1)
                {
                    // 点×线：位于线上的点
                    double tol = ToleranceOf(a, b);
                    foreach (Coordinate c in CoordinatesOf(a))
                        if (LinesOf(b).Any(line => PointToPolylineDistance(c, line) <= tol))
                            output.Add(new ResultItem(new Point(c), sourceA));
                }
                else if (dimA == 1 && dimB == 0)
                {
                    // 线×点：线上与点重合的位置（对称处理，点归到点一侧）
                    double tol = ToleranceOf(a, b);
                    foreach (Coordinate c in CoordinatesOf(b))
                        if (LinesOf(a).Any(line => PointToPolylineDistance(c, line) <= tol))
                            output.Add(new ResultItem(new Point(c), sourceB));
                }
                else if (dimA == 1 && dimB == 1)
                {
                    // 线×线：输出线段交点
                    double tol = ToleranceOf(a, b);
                    var points = new List<Coordinate>();
                    foreach (Points lineA in LinesOf(a))
                        foreach (Points lineB in LinesOf(b))
                            points.AddRange(LineCrossingPoints(lineA, lineB));
                    foreach (Coordinate c in Dedup(points, tol))
                        output.Add(new ResultItem(new Point(c), sourceA));
                }
                else if (dimA == 0 && dimB == 2)
                {
                    // 点×面：面内的点
                    foreach (Coordinate c in CoordinatesOf(a))
                        if (InsideAny(c, PolygonsOf(b)))
                            output.Add(new ResultItem(new Point(c), sourceA));
                }
                else if (dimA == 2 && dimB == 0)
                {
                    // 面×点：面内的点
                    foreach (Coordinate c in CoordinatesOf(b))
                        if (InsideAny(c, PolygonsOf(a)))
                            output.Add(new ResultItem(new Point(c), sourceB));
                }
                else
                {
                    // 面×线 / 线×面：降维输出线被面裁剪的部分（与 outDim==1 相同的处理）
                    List<Points> lines = dimA == 1 ? LinesOf(a) : LinesOf(b);
                    List<Polygon> polygons = PolygonsOf(dimA == 1 ? b : a);
                    Feature source = dimA == 1 ? sourceA : sourceB;
                    foreach (Points line in lines)
                        foreach (Points chain in ClipPolyline(line, polygons, true))
                            if (chain.Count >= 2)
                                output.Add(new ResultItem(ToLineString(chain), source));
                }
            }
            return output;
        }

        /// <summary>
        /// 交集取反：两组几何中互不重叠的部分。要求两组维数一致（点×点 / 线×线 / 面×面），
        /// 维数不一致时输出可表示的一侧并在 note 中说明。
        /// </summary>
        public static List<ResultItem> SymDifference(IList<Geometry> aSet, IList<Geometry> bSet, Feature sourceA, Feature sourceB, out string note)
        {
            var output = new List<ResultItem>();
            note = null;
            int dimA = aSet.Count > 0 ? DimensionOf(aSet[0]) : -1;
            int dimB = bSet.Count > 0 ? DimensionOf(bSet[0]) : -1;

            if (aSet.Count == 0 && bSet.Count == 0)
            {
                note = "两组输入均为空。";
                return output;
            }
            if (aSet.Count == 0 || bSet.Count == 0 || dimA != dimB)
            {
                // 维数不一致（或一侧为空）：输出"输入侧不在更新要素范围内"的可表示部分
                if (aSet.Count > 0)
                {
                    var erasePolys = new List<Polygon>();
                    if (dimB == 2)
                        foreach (Geometry g in bSet) erasePolys.AddRange(PolygonsOf(g));
                    var otherLines = new List<Points>();
                    var otherPoints = new List<Coordinate>();
                    if (dimB == 1)
                        foreach (Geometry g in bSet) otherLines.AddRange(LinesOf(g));
                    if (dimB == 0)
                        foreach (Geometry g in bSet) otherPoints.AddRange(CoordinatesOf(g));
                    double tol = ToleranceOf(aSet[0], bSet.Count > 0 ? bSet[0] : null);
                    foreach (Geometry g in aSet)
                    {
                        if (dimB == 2)
                        {
                            foreach (ResultItem item in Erase(g, erasePolys, sourceA))
                                output.Add(item);
                        }
                        else if (dimB == 1 && DimensionOf(g) == 1)
                        {
                            foreach (Points line in LinesOf(g))
                                foreach (Points chain in SubtractPolylineByPolyline(line, otherLines, tol))
                                    if (chain.Count >= 2)
                                        output.Add(new ResultItem(ToLineString(chain), sourceA));
                        }
                        else if (DimensionOf(g) == 0)
                        {
                            foreach (Coordinate c in CoordinatesOf(g))
                            {
                                bool overlapped = dimB == 1
                                    ? otherLines.Any(line => PointToPolylineDistance(c, line) <= tol)
                                    : otherPoints.Any(o => c.DistanceTo(o) <= tol);
                                if (overlapped == false)
                                    output.Add(new ResultItem(new Point(c), sourceA));
                            }
                        }
                        else
                        {
                            note = "维数不一致：面输入减去较低维数更新要素不可表示，该部分已忽略。";
                        }
                    }
                    if (note == null)
                        note = "维数不一致，已输出输入要素中位于更新要素范围之外的部分（更新要素一侧无法用较低维数表示，已忽略）。";
                }
                else
                {
                    foreach (Geometry g in bSet)
                        output.Add(new ResultItem(g.Clone(), sourceB));
                    note = "输入要素为空，结果为更新要素的全部内容。";
                }
                return output;
            }

            if (dimA == 2)
            {
                // 面×面：A-B 全部 + B-A 全部
                var aPolys = new List<Polygon>();
                var bPolys = new List<Polygon>();
                foreach (Geometry g in aSet) aPolys.AddRange(PolygonsOf(g));
                foreach (Geometry g in bSet) bPolys.AddRange(PolygonsOf(g));
                foreach (Polygon p in BooleanMulti(aPolys, bPolys, false)) output.Add(new ResultItem(p, sourceA));
                foreach (Polygon p in BooleanMulti(bPolys, aPolys, false)) output.Add(new ResultItem(p, sourceB));
            }
            else if (dimA == 1)
            {
                // 线×线：各自减去与对方重合的部分
                double tol = ToleranceOf(aSet[0], bSet[0]);
                var bLines = new List<Points>();
                foreach (Geometry g in bSet) bLines.AddRange(LinesOf(g));
                foreach (Geometry g in aSet)
                    foreach (Points line in LinesOf(g))
                        foreach (Points chain in SubtractPolylineByPolyline(line, bLines, tol))
                            if (chain.Count >= 2)
                                output.Add(new ResultItem(ToLineString(chain), sourceA));
                var aLines = new List<Points>();
                foreach (Geometry g in aSet) aLines.AddRange(LinesOf(g));
                foreach (Geometry g in bSet)
                    foreach (Points line in LinesOf(g))
                        foreach (Points chain in SubtractPolylineByPolyline(line, aLines, tol))
                            if (chain.Count >= 2)
                                output.Add(new ResultItem(ToLineString(chain), sourceB));
            }
            else
            {
                // 点×点：互不重合的点都保留
                double tol = ToleranceOf(aSet[0], bSet[0]);
                var coordsB = new List<Coordinate>();
                foreach (Geometry g in bSet) coordsB.AddRange(CoordinatesOf(g));
                var coordsA = new List<Coordinate>();
                foreach (Geometry g in aSet) coordsA.AddRange(CoordinatesOf(g));
                foreach (Geometry g in aSet)
                    foreach (Coordinate c in CoordinatesOf(g))
                        if (coordsB.Any(o => c.DistanceTo(o) <= tol) == false)
                            output.Add(new ResultItem(new Point(c), sourceA));
                foreach (Geometry g in bSet)
                    foreach (Coordinate c in CoordinatesOf(g))
                        if (coordsA.Any(o => c.DistanceTo(o) <= tol) == false)
                            output.Add(new ResultItem(new Point(c), sourceB));
            }
            return output;
        }

        #endregion

        #region 面布尔运算

        /// <summary>subject 依次与每个 other 做布尔运算（intersectMode=真为相交，假为擦除）。</summary>
        public static List<Polygon> BooleanMulti(List<Polygon> subject, List<Polygon> others, bool intersectMode)
        {
            if (others.Count == 0)
                return subject.Select(p => (Polygon)p.Clone()).ToList();
            var accumulator = new List<Polygon>();
            foreach (Polygon polygon in subject)
                accumulator.AddRange(BooleanTwo(polygon, others[0], intersectMode));
            for (int i = 1; i <= others.Count - 1; i++)
            {
                var next = new List<Polygon>();
                foreach (Polygon polygon in accumulator)
                    next.AddRange(BooleanTwo(polygon, others[i], intersectMode));
                accumulator = next;
            }
            return accumulator;
        }

        /// <summary>两个多边形的布尔运算（intersectMode=真为相交，假为擦除）。</summary>
        public static List<Polygon> BooleanTwo(Polygon subjectPoly, Polygon otherPoly, bool intersectMode)
        {
            double eps = ToleranceOf(subjectPoly, otherPoly);

            // 快速路径：边界无交点时按包含关系直接给出结果。
            // 代表点取外环外包盒中心（避免取顶点时恰好落在对方边界上导致的射线法退化）。
            bool hasCrossing = RingsHaveCrossing(RingsOf(subjectPoly), RingsOf(otherPoly));
            bool aInsideB = otherPoly.Contains(ExteriorCenter(subjectPoly), 0);
            bool bInsideA = subjectPoly.Contains(ExteriorCenter(otherPoly), 0);
            if (hasCrossing == false)
            {
                if (intersectMode)
                {
                    if (aInsideB) return new List<Polygon> { (Polygon)subjectPoly.Clone() };
                    if (bInsideA) return new List<Polygon> { (Polygon)otherPoly.Clone() };
                    return new List<Polygon>();                     // 相离
                }
                // 擦除：A 完全在 B 内被擦掉；相离则原样保留；B 在 A 内部（生成带洞结果）走通用路径
                if (aInsideB)
                    return new List<Polygon>();
                if (aInsideB == false && bInsideA == false)
                    return new List<Polygon> { (Polygon)subjectPoly.Clone() };
            }

            Polygon a = NormalizedCopy(subjectPoly);
            Polygon b = NormalizedCopy(otherPoly);
            var ringsA = RingsOf(a);
            var ringsB = RingsOf(b);

            // 1) 打断：A 的边在 B 边交点处、B 的边在 A 边交点处
            var segsA = new List<Segment>();
            var segsB = new List<Segment>();
            foreach (List<Coordinate> ring in ringsA) segsA.AddRange(SubdivideRing(ring, ringsB, true));
            foreach (List<Coordinate> ring in ringsB) segsB.AddRange(SubdivideRing(ring, ringsA, false));

            // 2) 中点归类筛选
            var kept = new List<Segment>();
            foreach (Segment seg in segsA)
            {
                Coordinate mid = Lerp(seg.From, seg.To, 0.5);
                bool insideB = b.Contains(mid, 0);
                if (intersectMode ? insideB : (insideB == false))
                    kept.Add(seg);
            }
            foreach (Segment seg in segsB)
            {
                Coordinate mid = Lerp(seg.From, seg.To, 0.5);
                bool insideA = a.Contains(mid, 0);
                if (insideA)
                    kept.Add(intersectMode ? seg : new Segment(seg.To, seg.From));  // 擦除时对方贡献的段方向取反
            }

            // 3) 重组环并生成多边形
            double areaEps = Math.Max(1e-12, eps * eps);
            return BuildPolygons(AssembleRings(kept, eps), areaEps);
        }

        #endregion

        #region 线裁剪与线求交

        /// <summary>折线按多边形组裁剪：keepInside=保留落在面内的部分（裁剪），否则保留外部的部分（擦除）。</summary>
        public static List<Points> ClipPolyline(Points vertices, List<Polygon> polygons, bool keepInside)
        {
            var output = new List<Points>();
            if (vertices == null || vertices.Count < 2 || polygons.Count == 0)
                return output;
            Points chain = null;
            foreach (Segment piece in CutByRings(LineToCoords(vertices), CutRingsOf(polygons)))
            {
                Coordinate mid = Lerp(piece.From, piece.To, 0.5);
                bool inside = InsideAny(mid, polygons);
                if ((inside == keepInside) == false)
                {
                    chain = null;
                    continue;
                }
                if (chain == null || chain.GetItem(chain.Count - 1).DistanceTo(piece.From) > 1e-7)
                {
                    chain = new Points();
                    chain.Add(piece.From);
                    output.Add(chain);
                }
                chain.Add(piece.To);
            }
            return output;
        }

        /// <summary>折线减去折线组：保留距对方全部线段都大于容差的子段（交集取反的线×线用）。</summary>
        public static List<Points> SubtractPolylineByPolyline(Points vertices, List<Points> otherLines, double tol)
        {
            var output = new List<Points>();
            var otherRings = new List<List<Coordinate>>();
            foreach (Points line in otherLines) otherRings.Add(LineToCoords(line));
            Points chain = null;
            foreach (Segment piece in CutByPolylines(LineToCoords(vertices), otherRings))
            {
                Coordinate mid = Lerp(piece.From, piece.To, 0.5);
                bool near = DistanceToLines(mid, otherLines) <= tol;
                if (near)
                {
                    chain = null;
                    continue;
                }
                if (chain == null || chain.GetItem(chain.Count - 1).DistanceTo(piece.From) > 1e-7)
                {
                    chain = new Points();
                    chain.Add(piece.From);
                    output.Add(chain);
                }
                chain.Add(piece.To);
            }
            return output;
        }

        /// <summary>两条折线的交点（含端点接触，去重后返回）。</summary>
        public static List<Coordinate> LineCrossingPoints(Points lineA, Points lineB)
        {
            var a = LineToCoords(lineA);
            var b = LineToCoords(lineB);
            var points = new List<Coordinate>();
            for (int i = 0; i <= a.Count - 2; i++)
                for (int j = 0; j <= b.Count - 2; j++)
                {
                    double t, s;
                    Coordinate point;
                    if (TryCrossAny(a[i], a[i + 1], b[j], b[j + 1], out t, out s, out point))
                        points.Add(point);
                }
            return Dedup(points, 1e-7);
        }

        /// <summary>按开放折线组打断折线（相邻点连线，不闭合）。</summary>
        private static List<Segment> CutByPolylines(List<Coordinate> vertices, List<List<Coordinate>> otherLines)
        {
            var pieces = new List<Segment>();
            int n = vertices.Count;
            for (int i = 0; i <= n - 2; i++)
            {
                Coordinate p = vertices[i], q = vertices[i + 1];
                var cuts = new List<double>();
                foreach (List<Coordinate> other in otherLines)
                {
                    int m = other.Count;
                    for (int j = 0; j <= m - 2; j++)
                    {
                        double t;
                        if (TryCrossStrict(p, q, other[j], other[j + 1], out t))
                            cuts.Add(t);
                    }
                }
                cuts.Sort();
                double last = 0;
                foreach (double t in cuts)
                {
                    if (t - last <= 1e-12) continue;
                    pieces.Add(new Segment(Lerp(p, q, last), Lerp(p, q, t)));
                    last = t;
                }
                pieces.Add(new Segment(Lerp(p, q, last), q));
            }
            return pieces;
        }

        private static double DistanceToLines(Coordinate point, List<Points> lines)
        {
            double min = double.MaxValue;
            foreach (Points line in lines)
            {
                double d = PointToPolylineDistance(point, line);
                if (d < min) min = d;
            }
            return min;
        }

        private static List<Segment> CutByRings(List<Coordinate> vertices, List<List<Coordinate>> rings)
        {
            var pieces = new List<Segment>();
            int n = vertices.Count;
            for (int i = 0; i <= n - 2; i++)
            {
                Coordinate p = vertices[i], q = vertices[i + 1];
                var cuts = new List<double>();
                foreach (List<Coordinate> ring in rings)
                {
                    int m = ring.Count;
                    if (m < 3) continue;
                    for (int j = 0; j <= m - 1; j++)
                    {
                        double t;
                        if (TryCrossStrict(p, q, ring[j], ring[(j + 1) % m], out t))
                            cuts.Add(t);
                    }
                }
                cuts.Sort();
                double last = 0;
                foreach (double t in cuts)
                {
                    if (t - last <= 1e-12) continue;
                    pieces.Add(new Segment(Lerp(p, q, last), Lerp(p, q, t)));
                    last = t;
                }
                pieces.Add(new Segment(Lerp(p, q, last), q));
            }
            return pieces;
        }

        private static List<List<Coordinate>> CutRingsOf(List<Polygon> polygons)
        {
            var rings = new List<List<Coordinate>>();
            foreach (Polygon polygon in polygons)
                rings.AddRange(RingsOf(polygon));
            return rings;
        }

        private static double DistanceToRings(Coordinate point, List<List<Coordinate>> rings)
        {
            double min = double.MaxValue;
            foreach (List<Coordinate> ring in rings)
            {
                int m = ring.Count;
                for (int j = 0; j <= m - 1; j++)
                {
                    double d = GeometryTools.GetDisFromPointToSegment(point, ring[j], ring[(j + 1) % m]);
                    if (d < min) min = d;
                }
            }
            return min;
        }

        #endregion

        #region 基础几何运算

        private static bool TryCrossStrict(Coordinate p1, Coordinate p2, Coordinate q1, Coordinate q2, out double t)
        {
            double s;
            return TryCross(p1, p2, q1, q2, true, true, out t, out s);
        }

        private static bool TryCrossAny(Coordinate p1, Coordinate p2, Coordinate q1, Coordinate q2,
            out double t, out double s, out Coordinate point)
        {
            point = new Coordinate();
            if (TryCross(p1, p2, q1, q2, false, false, out t, out s))
            {
                point = Lerp(p1, p2, t);
                return true;
            }
            return false;
        }

        // 两线段求交：t 对 (p1,p2)、s 对 (q1,q2)。strictP/strictQ 控制对应参数是否要求严格内部 (0,1)。
        private static bool TryCross(Coordinate p1, Coordinate p2, Coordinate q1, Coordinate q2,
            bool strictP, bool strictQ, out double t, out double s)
        {
            t = 0;
            s = 0;
            double dX1 = p2.X - p1.X, dY1 = p2.Y - p1.Y;
            double dX2 = q2.X - q1.X, dY2 = q2.Y - q1.Y;
            double denom = dX1 * dY2 - dX2 * dY1;
            if (denom == 0)
                return false;                       // 平行或共线（共线重叠属于退化情况，未处理）
            t = ((q1.X - p1.X) * dY2 - (q1.Y - p1.Y) * dX2) / denom;
            s = ((q1.X - p1.X) * dY1 - (q1.Y - p1.Y) * dX1) / denom;
            if (strictP && (t <= 0 || t >= 1)) return false;
            if (strictQ && (s <= 0 || s >= 1)) return false;
            if (t < 0 || t > 1 || s < 0 || s > 1) return false;
            return true;
        }

        private static Coordinate Lerp(Coordinate p, Coordinate q, double t)
        {
            return new Coordinate(p.X + (q.X - p.X) * t, p.Y + (q.Y - p.Y) * t);
        }

        private static List<Coordinate> Dedup(List<Coordinate> points, double tol)
        {
            var output = new List<Coordinate>();
            foreach (Coordinate c in points)
                if (output.Any(o => o.DistanceTo(c) <= tol) == false)
                    output.Add(c);
            return output;
        }

        private static double ToleranceOf(Geometry a, Geometry b)
        {
            Envelope envelope = a == null ? null : a.GetEnvelope();
            if (b != null)
            {
                if (envelope == null) envelope = b.GetEnvelope();
                else envelope.ExpandToInclude(b.GetEnvelope());
            }
            if (envelope == null || envelope.IsNull)
                return 1e-7;
            double diagonal = Math.Sqrt(envelope.Width * envelope.Width + envelope.Height * envelope.Height);
            return Math.Max(1e-9, diagonal * 1e-8);
        }

        #endregion

        #region 环与多边形构建

        /// <summary>外环外包盒的中心点（作为包含关系判定的代表点）。</summary>
        private static Coordinate ExteriorCenter(Polygon polygon)
        {
            Envelope envelope = polygon.ExteriorRing.GetEnvelope();
            if (envelope == null || envelope.IsNull)
                return new Coordinate();
            return new Coordinate((envelope.MinX + envelope.MaxX) / 2, (envelope.MinY + envelope.MaxY) / 2);
        }

        private static List<List<Coordinate>> RingsOf(Polygon polygon)
        {
            var rings = new List<List<Coordinate>>();
            rings.Add(LineToCoords(polygon.ExteriorRing));
            foreach (Points hole in polygon.Holes)
                rings.Add(LineToCoords(hole));
            return rings;
        }

        private static List<Coordinate> LineToCoords(Points points)
        {
            var list = new List<Coordinate>();
            if (points == null)
                return list;
            for (int i = 0; i <= points.Count - 1; i++)
                list.Add(points.GetItem(i));
            return list;
        }

        private static LineString ToLineString(Points points)
        {
            var lineString = new LineString();
            for (int i = 0; i <= points.Count - 1; i++) lineString.Points.Add(points.GetItem(i));
            return lineString;
        }

        /// <summary>按交点打断闭合环（外环/内环通用），返回子线段。</summary>
        private static List<Segment> SubdivideRing(List<Coordinate> ring, List<List<Coordinate>> otherRings, bool selfStrict)
        {
            var segments = new List<Segment>();
            int n = ring.Count;
            if (n < 3)
                return segments;
            for (int i = 0; i <= n - 1; i++)
            {
                Coordinate p = ring[i], q = ring[(i + 1) % n];
                var cuts = new List<double>();
                foreach (List<Coordinate> other in otherRings)
                {
                    int m = other.Count;
                    if (m < 3) continue;
                    for (int j = 0; j <= m - 1; j++)
                    {
                        double t, s;
                        // 本环边要求交点在内部；对方边的参数允许取到端点（对方顶点落在本边上时也要打断）
                        if (TryCross(p, q, other[j], other[(j + 1) % m], selfStrict, selfStrict == false, out t, out s))
                            cuts.Add(t);
                    }
                }
                cuts.Sort();
                double last = 0;
                foreach (double t in cuts)
                {
                    if (t - last <= 1e-12) continue;
                    segments.Add(new Segment(Lerp(p, q, last), Lerp(p, q, t)));
                    last = t;
                }
                if (1 - last > 1e-12)
                    segments.Add(new Segment(Lerp(p, q, last), q));
            }
            return segments;
        }

        /// <summary>把保留的子线段按首尾相接重组为环。</summary>
        private static List<List<Coordinate>> AssembleRings(List<Segment> kept, double eps)
        {
            var rings = new List<List<Coordinate>>();
            var byStart = new Dictionary<long, List<int>>();
            for (int i = 0; i <= kept.Count - 1; i++)
            {
                long key = CoordKey(kept[i].From, eps);
                if (byStart.ContainsKey(key) == false) byStart[key] = new List<int>();
                byStart[key].Add(i);
            }
            var used = new bool[kept.Count];
            for (int seed = 0; seed <= kept.Count - 1; seed++)
            {
                if (used[seed]) continue;
                var ring = new List<Coordinate>();
                used[seed] = true;
                ring.Add(kept[seed].From);
                Segment current = kept[seed];
                ring.Add(current.To);
                while (true)
                {
                    long key = CoordKey(current.To, eps);
                    int next = -1;
                    List<int> candidates;
                    if (byStart.TryGetValue(key, out candidates))
                    {
                        int ambiguous = 0;
                        foreach (int c in candidates)
                        {
                            if (used[c]) continue;
                            // 不立即沿原路返回（同一条边的反向孪生段）
                            if (kept[c].To.DistanceTo(current.From) <= eps) { ambiguous++; continue; }
                            next = c;
                            ambiguous++;
                        }
                        if (candidates.Count(ck => used[ck] == false) > 1 && next >= 0)
                            next = PickMostClockwise(kept,
                                candidates.Where(ck => used[ck] == false && kept[ck].To.DistanceTo(current.From) > eps).ToList(),
                                current);
                    }
                    if (next < 0)
                        break;                              // 无后续段：环在此闭合
                    used[next] = true;
                    current = kept[next];
                    ring.Add(current.To);
                }
                rings.Add(ring);
            }
            return rings;
        }

        /// <summary>顶点歧义时选"最顺时针"的出边（面向以逆时针保存的结果区域行走）。</summary>
        private static int PickMostClockwise(List<Segment> kept, List<int> candidates, Segment current)
        {
            double baseAngle = Math.Atan2(current.To.Y - current.From.Y, current.To.X - current.From.X);
            int best = candidates.Count > 0 ? candidates[0] : -1;
            double bestAngle = double.MaxValue;
            foreach (int c in candidates)
            {
                double angle = Math.Atan2(kept[c].To.Y - kept[c].From.Y, kept[c].To.X - kept[c].From.X) - baseAngle;
                while (angle >= 0) angle -= 2 * Math.PI;
                while (angle < -2 * Math.PI) angle += 2 * Math.PI;
                if (angle > bestAngle) { bestAngle = angle; best = c; }
            }
            return best;
        }

        private static long CoordKey(Coordinate c, double eps)
        {
            long kx = Convert.ToInt64(Math.Round(c.X / eps));
            long ky = Convert.ToInt64(Math.Round(c.Y / eps));
            return kx * 1000000007L + ky;
        }

        /// <summary>由重组出的环构建多边形：带符号面积区分外环与内环，内环分配给包含它的最小外环。</summary>
        private static List<Polygon> BuildPolygons(List<List<Coordinate>> rings, double areaEps)
        {
            var exteriors = new List<List<Coordinate>>();
            var holes = new List<List<Coordinate>>();
            foreach (List<Coordinate> ring in rings)
            {
                double area = SignedArea(ring);
                if (Math.Abs(area) <= areaEps)
                    continue;                               // 退化环（零面积/极小裂缝）丢弃
                if (area > 0)
                    exteriors.Add(ring);
                else
                    holes.Add(ring);
            }
            exteriors.Sort((x, y) => SignedArea(y).CompareTo(SignedArea(x)));
            var polygons = new List<Polygon>();
            foreach (List<Coordinate> exterior in exteriors)
            {
                var polygon = new Polygon();
                foreach (Coordinate c in exterior) polygon.ExteriorRing.Add(c);
                polygons.Add(polygon);
            }
            foreach (List<Coordinate> hole in holes)
            {
                Coordinate probe = hole[0];
                Polygon owner = null;
                double ownerArea = double.MaxValue;
                foreach (Polygon polygon in polygons)
                {
                    double area = SignedArea(polygon.ExteriorRing);
                    if (GeometryTools.IsPointWithinPolygon(probe, polygon.ExteriorRing) && area < ownerArea)
                    {
                        owner = polygon;
                        ownerArea = area;
                    }
                }
                if (owner != null)
                {
                    var ring = new Points();
                    foreach (Coordinate c in hole) ring.Add(c);
                    owner.Holes.Add(ring);
                }
                // 找不到所属外环的孤立内环丢弃
            }
            return polygons;
        }

        private static double SignedArea(List<Coordinate> ring)
        {
            int n = ring.Count;
            if (n < 3)
                return 0;
            double s = 0;
            for (int i = 0; i <= n - 1; i++)
            {
                Coordinate p1 = ring[i], p2 = ring[(i + 1) % n];
                s += p1.X * p2.Y - p2.X * p1.Y;
            }
            return s / 2;
        }

        private static double SignedArea(Points ring)
        {
            var list = LineToCoords(ring);
            return SignedArea(list);
        }

        /// <summary>规整方向：外环转逆时针、内环转顺时针的深拷贝。</summary>
        private static Polygon NormalizedCopy(Polygon polygon)
        {
            var copy = (Polygon)polygon.Clone();
            if (copy.ExteriorRing.Count >= 3 && SignedArea(copy.ExteriorRing) < 0)
                ReverseRing(copy.ExteriorRing);
            foreach (Points hole in copy.Holes)
                if (hole.Count >= 3 && SignedArea(hole) > 0)
                    ReverseRing(hole);
            return copy;
        }

        private static void ReverseRing(Points ring)
        {
            int n = ring.Count;
            for (int i = 0; i <= n / 2 - 1; i++)
            {
                Coordinate a = ring.GetItem(i), b = ring.GetItem(n - 1 - i);
                ring.SetItem(i, b);
                ring.SetItem(n - 1 - i, a);
            }
        }

        private static bool InsideAny(Coordinate c, IEnumerable<Polygon> polygons)
        {
            foreach (Polygon polygon in polygons)
                if (polygon.Contains(c, 0))
                    return true;
            return false;
        }

        private static bool RingsHaveCrossing(List<List<Coordinate>> ringsA, List<List<Coordinate>> ringsB)
        {
            foreach (List<Coordinate> ringA in ringsA)
            {
                int na = ringA.Count;
                if (na < 3) continue;
                for (int i = 0; i <= na - 1; i++)
                    foreach (List<Coordinate> ringB in ringsB)
                    {
                        int nb = ringB.Count;
                        if (nb < 3) continue;
                        for (int j = 0; j <= nb - 1; j++)
                        {
                            double t, s;
                            if (TryCross(ringA[i], ringA[(i + 1) % na], ringB[j], ringB[(j + 1) % nb], true, true, out t, out s))
                                return true;
                        }
                    }
            }
            return false;
        }

        #endregion
    }
}
