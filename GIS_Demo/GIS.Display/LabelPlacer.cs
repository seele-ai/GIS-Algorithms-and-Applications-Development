using System;
using System.Collections.Generic;
using System.Drawing;

namespace GIS.Display
{
    /// <summary>
    /// 注记摆放与冲突检测（参考旧项目 MyMapObjects/moLabelTools 的做法，并按本项目补齐了旋转矩形的判交）。
    ///
    /// 思路：一条注记用一个“旋转矩形”表示（左上角锚点 + 宽高 + 角度），每放一条就记入已放置列表；
    /// 后面的注记先试首选位置，若与已放置的注记冲突就换下一个候选位置，全部候选都冲突则跳过不画，
    /// 从而“尽量”避免注记互相遮盖（宁可少画一条，也不叠成一团）。
    /// </summary>
    public static class LabelPlacer
    {
        /// <summary>注记之间的最小间隙（像素）：小于它就算冲突，避免两条注记贴在一起。</summary>
        public const float Padding = 2f;

        /// <summary>以锚点（左上角）与宽高、角度构造注记矩形。</summary>
        public static RectangleF Bounds(PointF location, SizeF size, double angle)
        {
            if (angle == 0) return new RectangleF(location.X, location.Y, size.Width, size.Height);
            // 旋转时返回 4 个角点的外接矩形（用于快速排除）
            PointF[] corners = Corners(location, size, angle);
            float minX = corners[0].X, maxX = corners[0].X, minY = corners[0].Y, maxY = corners[0].Y;
            for (int i = 1; i < corners.Length; i++)
            {
                minX = Math.Min(minX, corners[i].X); maxX = Math.Max(maxX, corners[i].X);
                minY = Math.Min(minY, corners[i].Y); maxY = Math.Max(maxY, corners[i].Y);
            }
            return RectangleF.FromLTRB(minX, minY, maxX, maxY);
        }

        /// <summary>以锚点为左上角、按逆时针角度旋转后的 4 个角点。</summary>
        public static PointF[] Corners(PointF location, SizeF size, double angle)
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
                // 与绘制保持一致：逆时针为正当作屏幕上的逆时针
                points[i] = new PointF(
                    (float)(location.X + dx * cos + dy * sin),
                    (float)(location.Y - dx * sin + dy * cos));
            }
            return points;
        }

        /// <summary>两条注记是否冲突（含 padding 间隙）；角度为 0 时退化为外接矩形判交。</summary>
        public static bool Conflicts(RectangleF a, double angleA, RectangleF b, double angleB, float padding)
        {
            RectangleF inflated = Inflate(a, padding);
            RectangleF other = Inflate(b, padding);
            if (angleA == 0 && angleB == 0)
            {
                return inflated.IntersectsWith(other);
            }
            // 旋转矩形：分离轴定理（两条矩形各两条轴，共 4 条轴）
            PointF[] pa = Corners(inflated.Location, inflated.Size, angleA);
            PointF[] pb = Corners(other.Location, other.Size, angleB);
            return SatOverlap(pa, pb);
        }

        public static bool Conflicts(RectangleF a, double angleA, RectangleF b, double angleB)
        {
            return Conflicts(a, angleA, b, angleB, Padding);
        }

        /// <summary>与已放置的注记集合逐条比较，只要有一条冲突就算冲突。</summary>
        public static bool ConflictsWithAny(RectangleF candidate, double angle,
            IList<RectangleF> placed, IList<double> placedAngles, float padding)
        {
            if (placed == null) return false;
            for (int i = 0; i < placed.Count; i++)
            {
                double otherAngle = placedAngles != null && i < placedAngles.Count ? placedAngles[i] : 0;
                if (Conflicts(candidate, angle, placed[i], otherAngle, padding)) return true;
            }
            return false;
        }

        /// <summary>
        /// 依次尝试候选锚点，返回第一个不冲突的位置；全部冲突时返回 false（该注记不绘制）。
        /// </summary>
        public static bool TryPlace(PointF[] candidates, SizeF size, double angle,
            IList<RectangleF> placed, IList<double> placedAngles, out PointF chosen)
        {
            chosen = PointF.Empty;
            if (candidates == null || candidates.Length == 0) return false;
            for (int i = 0; i < candidates.Length; i++)
            {
                var bounds = Bounds(candidates[i], size, angle);
                if (!ConflictsWithAny(bounds, angle, placed, placedAngles, Padding))
                {
                    chosen = candidates[i];
                    if (placed != null)
                    {
                        placed.Add(bounds);
                        if (placedAngles != null) placedAngles.Add(angle);
                    }
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 点符号的四个候选锚点（注记左上角）：右上 → 右下 → 左上 → 左下。
        /// 锚点按<tspan>旋转后的外接矩形</tspan>反推：
        /// 旋转时先把注记矩形绕锚点转到目标角度，再让这个“转好之后”的矩形贴到符号外侧，
        /// 因此四个方位在任意角度下都与符号保持「符号半径 + 间隙」的距离
        /// （若按未旋转的宽高摆放，设置旋转角后注记会离符号过远，甚至压住符号）。
        /// </summary>
        public static PointF[] AroundPoint(PointF point, SizeF size, float symbolRadius, float gap, double angle)
        {
            RelativeBox(size, angle, out float minX, out float maxX, out float minY, out float maxY);
            float dx = symbolRadius + gap, dy = symbolRadius + gap;
            return new[]
            {
                new PointF(point.X + dx - minX, point.Y - dy - maxY),   // 右上
                new PointF(point.X + dx - minX, point.Y + dy - minY),   // 右下
                new PointF(point.X - dx - maxX, point.Y - dy - maxY),   // 左上
                new PointF(point.X - dx - maxX, point.Y + dy - minY)    // 左下
            };
        }

        /// <summary>
        /// 线/面要素的候选锚点：以定位点为<tspan>旋转后外接矩形</tspan>的中心，
        /// 再沿垂直方向微调（抬高/降低一个注记高度 + 4 像素），让被占用的注记有机会挪开而不是直接省略。
        /// </summary>
        public static PointF[] AroundCenter(PointF center, SizeF size, float step, double angle)
        {
            RelativeBox(size, angle, out float minX, out float maxX, out float minY, out float maxY);
            float width = maxX - minX, height = maxY - minY;
            float x = center.X - width / 2f - minX;
            float y = center.Y - height / 2f - minY;
            return new[]
            {
                new PointF(x, y),
                new PointF(x, y - step),
                new PointF(x, y + step)
            };
        }

        /// <summary>注记按角度旋转后的外接矩形尺寸（与锚点位置无关）。</summary>
        public static SizeF RotatedSize(SizeF size, double angle)
        {
            RelativeBox(size, angle, out float minX, out float maxX, out float minY, out float maxY);
            return new SizeF(maxX - minX, maxY - minY);
        }

        /// <summary>
        /// 旋转后的注记“相对锚点”占据的范围：以锚点为原点，返回 minX/maxX/minY/maxY。
        /// 角度为 0 时就是 (0,0)-(Width,Height)。
        /// </summary>
        private static void RelativeBox(SizeF size, double angle,
            out float minX, out float maxX, out float minY, out float maxY)
        {
            PointF[] corners = Corners(new PointF(0, 0), size, angle);
            minX = maxX = corners[0].X;
            minY = maxY = corners[0].Y;
            for (int i = 1; i < corners.Length; i++)
            {
                minX = Math.Min(minX, corners[i].X); maxX = Math.Max(maxX, corners[i].X);
                minY = Math.Min(minY, corners[i].Y); maxY = Math.Max(maxY, corners[i].Y);
            }
        }

        private static RectangleF Inflate(RectangleF rect, float padding)
        {
            if (padding <= 0) return rect;
            return RectangleF.FromLTRB(rect.Left - padding, rect.Top - padding,
                rect.Right + padding, rect.Bottom + padding);
        }

        // 分离轴定理：若存在一条轴能把两条凸多边形分开，则不相交
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
