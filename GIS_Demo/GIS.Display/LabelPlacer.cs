using System;
using System.Collections.Generic;
using System.Drawing;

namespace GIS.Display
{
    /// <summary>
    /// 注记摆放与冲突检测（参考旧项目 MyMapObjects/moLabelTools 的思路，并把“用外接矩形判断”
    /// 改成直接用<tspan>注记实际占据的平行四边形（旋转矩形）</tspan>）。
    ///
    /// 为什么不用外接矩形：
    ///  · 摆放：外接矩形的角上其实是空的，按它去贴要素会让注记（尤其是带旋转角时）离要素忽远忽近；
    ///  · 压盖：两条都带旋转角的注记，外接矩形相交往往并不代表注记真的压在一起，会误报冲突。
    /// 因此这里统一用 4 个角点表示的平行四边形：摆放时把“离要素最近的那个角”贴到要素外侧，
    /// 判交时用分离轴定理（SAT）+ 形状间最短距离，保证既不过远、也不误报。
    /// </summary>
    public static class LabelPlacer
    {
        /// <summary>注记之间的最小间隙（像素）：小于它就算冲突，避免两条注记贴在一起。</summary>
        public const float Padding = 2f;

        /// <summary>注记的 4 个角点（按锚点、宽高与角度旋转后的平行四边形，顺序为左上→右上→右下→左下）。</summary>
        public static PointF[] Quad(PointF location, SizeF size, double angle)
        {
            var points = new[]
            {
                new PointF(location.X, location.Y),
                new PointF(location.X + size.Width, location.Y),
                new PointF(location.X + size.Width, location.Y + size.Height),
                new PointF(location.X, location.Y + size.Height)
            };
            if (angle == 0) return points;
            double rad = angle * Math.PI / 180.0;
            double cos = Math.Cos(rad), sin = Math.Sin(rad);
            for (int i = 0; i < points.Length; i++)
            {
                double dx = points[i].X - location.X, dy = points[i].Y - location.Y;
                // 与绘制保持一致：逆时针为正
                points[i] = new PointF(
                    (float)(location.X + dx * cos + dy * sin),
                    (float)(location.Y - dx * sin + dy * cos));
            }
            return points;
        }

        /// <summary>注记平行四边形的外接矩形（仅用于快速排除与界面提示，判交请用 Conflicts）。</summary>
        public static RectangleF Bounds(PointF location, SizeF size, double angle)
        {
            PointF[] quad = Quad(location, size, angle);
            float minX = quad[0].X, maxX = quad[0].X, minY = quad[0].Y, maxY = quad[0].Y;
            for (int i = 1; i < quad.Length; i++)
            {
                minX = Math.Min(minX, quad[i].X); maxX = Math.Max(maxX, quad[i].X);
                minY = Math.Min(minY, quad[i].Y); maxY = Math.Max(maxY, quad[i].Y);
            }
            return RectangleF.FromLTRB(minX, minY, maxX, maxY);
        }

        /// <summary>注记按角度旋转后的外接矩形尺寸（与锚点位置无关）。</summary>
        public static SizeF RotatedSize(SizeF size, double angle)
        {
            PointF[] quad = Quad(new PointF(0, 0), size, angle);
            float minX = quad[0].X, maxX = quad[0].X, minY = quad[0].Y, maxY = quad[0].Y;
            for (int i = 1; i < quad.Length; i++)
            {
                minX = Math.Min(minX, quad[i].X); maxX = Math.Max(maxX, quad[i].X);
                minY = Math.Min(minY, quad[i].Y); maxY = Math.Max(maxY, quad[i].Y);
            }
            return new SizeF(maxX - minX, maxY - minY);
        }

        /// <summary>
        /// 两条注记（各自的平行四边形）是否冲突：实际形状相交，或形状之间最短距离小于 padding。
        /// </summary>
        public static bool Conflicts(PointF[] a, PointF[] b, float padding)
        {
            if (a == null || b == null) return false;
            if (SatOverlap(a, b)) return true;                  // 实际形状相交
            return PolygonDistance(a, b) < padding;             // 分离但靠得太近
        }

        public static bool Conflicts(PointF[] a, PointF[] b)
        {
            return Conflicts(a, b, Padding);
        }

        /// <summary>与已放置的注记逐条比较，只要有一条冲突就算冲突。</summary>
        public static bool ConflictsWithAny(PointF[] candidate, IList<PointF[]> placed, float padding)
        {
            if (placed == null) return false;
            for (int i = 0; i < placed.Count; i++)
                if (Conflicts(candidate, placed[i], padding)) return true;
            return false;
        }

        /// <summary>
        /// 依次尝试候选锚点，返回第一个不与已放置注记冲突的位置；全部冲突时返回 false（该注记省略）。
        /// </summary>
        public static bool TryPlace(PointF[] candidates, SizeF size, double angle,
            IList<PointF[]> placed, out PointF chosen)
        {
            chosen = PointF.Empty;
            if (candidates == null || candidates.Length == 0) return false;
            for (int i = 0; i < candidates.Length; i++)
            {
                PointF[] quad = Quad(candidates[i], size, angle);
                if (!ConflictsWithAny(quad, placed, Padding))
                {
                    chosen = candidates[i];
                    placed?.Add(quad);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 点符号的四个候选锚点（注记左上角）：右上 → 右下 → 左上 → 左下。
        ///
        /// 关键点：取注记<tspan>实际平行四边形的 4 个角</tspan>，把“朝向该方位、离符号最近的那个角”
        /// 放到与符号相距「符号半径 + 间隙」的位置上：
        ///  · 右上 → 取 x−y 最小的角（最靠右上）　· 右下 → 取 x+y 最小的角（最靠左上）
        ///  · 左上 → 取 x+y 最大的角（最靠右下）　· 左下 → 取 x−y 最大的角（最靠右上）
        /// 因此无论旋转多少度，注记离要素最近的那个角总是紧贴要素外侧，
        /// 既不会像“按外接矩形摆放”那样忽远忽近，也不会压住符号；角度为 0 时与不旋转的摆放完全一致。
        /// </summary>
        public static PointF[] AroundPoint(PointF point, SizeF size, float symbolRadius, float gap, double angle)
        {
            PointF[] quad = Quad(new PointF(0, 0), size, angle);
            float dx = symbolRadius + gap, dy = symbolRadius + gap;
            PointF rightTop = ExtremeCorner(quad, 1f, -1f, false);
            PointF rightBottom = ExtremeCorner(quad, 1f, 1f, false);
            PointF leftTop = ExtremeCorner(quad, 1f, 1f, true);
            PointF leftBottom = ExtremeCorner(quad, 1f, -1f, true);
            return new[]
            {
                new PointF(point.X + dx - rightTop.X, point.Y - dy - rightTop.Y),      // 右上
                new PointF(point.X + dx - rightBottom.X, point.Y + dy - rightBottom.Y),// 右下
                new PointF(point.X - dx - leftTop.X, point.Y - dy - leftTop.Y),        // 左上
                new PointF(point.X - dx - leftBottom.X, point.Y + dy - leftBottom.Y)   // 左下
            };
        }

        /// <summary>
        /// 线/面要素的候选锚点：让注记平行四边形的<tspan>中心</tspan>（= 外接矩形中心）对准定位点，
        /// 再沿垂直方向微调（抬高/降低一个注记高度 + 4 像素），让被占用的注记有机会挪开而不是直接省略。
        /// </summary>
        public static PointF[] AroundCenter(PointF center, SizeF size, float step, double angle)
        {
            SizeF box = RotatedSize(size, angle);
            PointF[] quad = Quad(new PointF(0, 0), size, angle);
            float minX = quad[0].X, minY = quad[0].Y;
            for (int i = 1; i < quad.Length; i++)
            {
                minX = Math.Min(minX, quad[i].X);
                minY = Math.Min(minY, quad[i].Y);
            }
            // 让平行四边形（与其外接矩形同心）的中心落在定位点上
            float x = center.X - box.Width / 2f - minX;
            float y = center.Y - box.Height / 2f - minY;
            return new[]
            {
                new PointF(x, y),
                new PointF(x, y - step),
                new PointF(x, y + step)
            };
        }

        // 在 4 个角中取 (kx·x + ky·y) 最小/最大的那个角
        private static PointF ExtremeCorner(PointF[] quad, float kx, float ky, bool max)
        {
            PointF best = quad[0];
            float bestValue = kx * best.X + ky * best.Y;
            for (int i = 1; i < quad.Length; i++)
            {
                float value = kx * quad[i].X + ky * quad[i].Y;
                if (max ? value > bestValue : value < bestValue) { bestValue = value; best = quad[i]; }
            }
            return best;
        }

        // 两个凸多边形之间的最短距离（相交返回 0）
        private static float PolygonDistance(PointF[] a, PointF[] b)
        {
            if (SatOverlap(a, b)) return 0f;
            return Math.Min(MinVertexEdgeDistance(a, b), MinVertexEdgeDistance(b, a));
        }

        private static float MinVertexEdgeDistance(PointF[] from, PointF[] to)
        {
            float min = float.MaxValue;
            for (int i = 0; i < from.Length; i++)
                for (int j = 0; j < to.Length; j++)
                {
                    float d = PointSegmentDistance(from[i], to[j], to[(j + 1) % to.Length]);
                    if (d < min) min = d;
                }
            return min;
        }

        private static float PointSegmentDistance(PointF p, PointF a, PointF b)
        {
            float vx = b.X - a.X, vy = b.Y - a.Y;
            float lengthSquared = vx * vx + vy * vy;
            float t = lengthSquared <= 1e-9f ? 0f : ((p.X - a.X) * vx + (p.Y - a.Y) * vy) / lengthSquared;
            t = Math.Max(0f, Math.Min(1f, t));
            float dx = p.X - (a.X + t * vx), dy = p.Y - (a.Y + t * vy);
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        // 分离轴定理：若存在一条轴能把两个凸多边形分开，则不相交
        private static bool SatOverlap(PointF[] a, PointF[] b)
        {
            return !HasSeparatingAxis(a, b) && !HasSeparatingAxis(b, a);
        }

        private static bool HasSeparatingAxis(PointF[] from, PointF[] other)
        {
            for (int i = 0; i < from.Length; i++)
            {
                PointF p1 = from[i], p2 = from[(i + 1) % from.Length];
                float axisX = -(p2.Y - p1.Y), axisY = p2.X - p1.X;   // 边的法线
                float len = (float)Math.Sqrt(axisX * axisX + axisY * axisY);
                if (len < 1e-6f) continue;
                axisX /= len; axisY /= len;
                Project(from, axisX, axisY, out float minA, out float maxA);
                Project(other, axisX, axisY, out float minB, out float maxB);
                if (maxA < minB || maxB < minA) return true;   // 该轴上分离
            }
            return false;
        }

        private static void Project(PointF[] points, float axisX, float axisY, out float min, out float max)
        {
            min = max = points[0].X * axisX + points[0].Y * axisY;
            for (int i = 1; i < points.Length; i++)
            {
                float v = points[i].X * axisX + points[i].Y * axisY;
                if (v < min) min = v;
                if (v > max) max = v;
            }
        }
    }
}
