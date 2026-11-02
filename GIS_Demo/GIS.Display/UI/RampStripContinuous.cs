using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace GIS.Display.UI
{
    /// <summary>
    /// 连续渲染的绘制上下文：一组数值 + 拉伸区间 + 统计量 + 特殊值虚线 + KDE 曲线。
    /// 曲线只与数值有关（换字段才变），色带只影响曲线下方填充的颜色（改色带时实时刷新）。
    /// </summary>
    public sealed class ContinuousStripContext
    {
        /// <summary>参与统计的数值（已过滤非数值）。</summary>
        public double[] Values = new double[0];

        /// <summary>字段名（显示在提示文字里）。</summary>
        public string FieldName = "";

        /// <summary>拉伸区间下界（对应色带 0）。</summary>
        public double Min;

        /// <summary>拉伸区间上界（对应色带 1）。</summary>
        public double Max;

        /// <summary>统计量。</summary>
        public ContinuousStatistics Statistics;

        /// <summary>特殊值虚线（已合并、已剔除区间外的值）。</summary>
        public List<ContinuousMark> Marks = new List<ContinuousMark>();

        /// <summary>KDE 曲线（0~1，按 <see cref="CurveSamples"/> 个采样点）。</summary>
        public double[] Density = new double[0];

        /// <summary>曲线采样点数。</summary>
        public int CurveSamples { get; private set; }

        /// <summary>用一组数值建立上下文（字段名只用于显示）。</summary>
        public static ContinuousStripContext Create(IEnumerable<double> values, string fieldName)
        {
            var context = new ContinuousStripContext();
            context.FieldName = fieldName ?? "";
            context.Values = values == null ? new double[0] : values.Where(v => !double.IsNaN(v) && !double.IsInfinity(v)).ToArray();
            context.Statistics = ContinuousStatistics.Compute(context.Values);
            if (context.Statistics.Count == 0)
            {
                context.Min = 0;
                context.Max = 1;
            }
            else
            {
                context.Min = context.Statistics.Min;
                context.Max = context.Statistics.Max;
            }
            context.Marks = ContinuousMarks.Build(context.Statistics);
            return context;
        }

        /// <summary>把数值拉伸到 0~1（区间退化时取 0.5）。</summary>
        public double Normalize(double value)
        {
            double span = Max - Min;
            if (span <= 0) return 0.5;
            double t = (value - Min) / span;
            return t < 0 ? 0 : (t > 1 ? 1 : t);
        }

        /// <summary>按采样点数重算 KDE 曲线（只在采样点数变化时重算）。</summary>
        public void EnsureDensity(int samples)
        {
            if (samples < 8) samples = 8;
            if (CurveSamples == samples && Density.Length == samples) return;
            Density = KernelDensity.Estimate(Values, samples, Min, Max);
            CurveSamples = samples;
        }

        /// <summary>某条特殊值虚线的显示文字。</summary>
        public string LabelOf(ContinuousMark mark)
        {
            return mark == null ? "" : mark.Label;
        }
    }

    /// <summary>
    /// 色带显示条的连续渲染部分（partial）：上方 KDE 分布 + 特殊值虚线 + 可绑定的标签，下方色带。
    /// 无上下文时保持原来的“色带 + 书签”外观，不影响分级/唯一值页面的色带编辑。
    /// </summary>
    public sealed partial class RampStrip
    {
        /// <summary>连续模式下标签行的高度。</summary>
        public const int TagHeight = 30;

        /// <summary>连续模式下的默认高度（KDE + 标签 + 色带 + 坐标轴）。</summary>
        public const int ContinuousHeight = 214;

        /// <summary>吸附距离（像素）：标签拖到距虚线这么近就自动贴上。</summary>
        public const float SnapBuffer = 14f;

        private const int KdeTop = 8;
        private const float TagHalfWidth = 12f;      // 绑定标签（带锁）的半宽
        private const float FreeTagHalfWidth = 9f;   // 未绑定标签（书签）的半宽

        private ContinuousStripContext context;
        private ColorRampNode dragNode;
        private ContinuousMark dragTarget;
        private ContinuousMark suppressedMark;
        private int lastDragY;
        private bool dragStartedBound;
        private float dragStartPixelX;

        /// <summary>标签被移动超过这么多像素才算“真的挪开了”（小于它视为单击，保持绑定）。</summary>
        public const float UnbindPixelThreshold = 3f;

        /// <summary>标签与特殊值虚线的绑定关系发生变化。</summary>
        public event EventHandler BindingChanged;

        /// <summary>连续渲染上下文；为 null 时本控件退回普通色带条外观。</summary>
        public ContinuousStripContext Context
        {
            get { return context; }
            set
            {
                context = value;
                if (context != null && Height < ContinuousHeight) Height = ContinuousHeight;
                Invalidate();
            }
        }

        /// <summary>当前是否处于连续渲染模式（有数值上下文）。</summary>
        public bool IsContinuous
        {
            get { return context != null; }
        }

        /// <summary>特殊值虚线条数。</summary>
        public int MarkCount
        {
            get { return context == null || context.Marks == null ? 0 : context.Marks.Count; }
        }

        /// <summary>特殊值虚线列表。</summary>
        public IReadOnlyList<ContinuousMark> Marks
        {
            get
            {
                return context == null || context.Marks == null
                    ? (IReadOnlyList<ContinuousMark>)new ContinuousMark[0]
                    : context.Marks;
            }
        }

        /// <summary>正在拖动的标签（没有拖动时为 null）。</summary>
        public ColorRampNode DraggingNode
        {
            get { return dragNode; }
        }

        #region 布局

        // 标签行：贴在色带上方
        private int TagBottom { get { return BandRect.Top - 4; } }
        private int TagTop { get { return TagBottom - TagHeight; } }

        // KDE 分布区：标签行以上
        private Rectangle KdeRect
        {
            get
            {
                int top = KdeTop;
                int bottom = TagTop - 16;   // 给特殊值文字留一行
                return new Rectangle(BandRect.Left, top, BandRect.Width, Math.Max(24, bottom - top));
            }
        }

        /// <summary>数值 → 横坐标（与色带同一条轴）。</summary>
        public float XOfValue(double value)
        {
            if (context == null) return XOf(0);
            return XOf(context.Normalize(value));
        }

        /// <summary>特殊值虚线的横坐标。</summary>
        public float MarkX(ContinuousMark mark)
        {
            if (mark == null) return float.NaN;
            return XOf(mark.Position);
        }

        /// <summary>标签的横坐标（考虑避开重叠后的偏移）。</summary>
        public float TagCenterOf(ColorRampNode node)
        {
            return TagCenter(node).X;
        }

        #endregion

        #region 绘制

        // KDE 曲线 + 渐变填充 + 特殊值虚线（在色带之前画，色带与标签盖在上面）
        private void DrawContinuousBackground(Graphics g)
        {
            if (context == null) return;
            Rectangle kde = KdeRect;
            context.EnsureDensity(Math.Max(24, kde.Width));

            // 曲线下方：用色带颜色从左到右填充（改色带时实时变化；曲线本身不变）
            var curve = new List<PointF>();
            int samples = context.Density.Length;
            for (int i = 0; i < samples; i++)
            {
                double t = samples <= 1 ? 0 : (double)i / (samples - 1);
                float x = kde.Left + (float)(t * (kde.Width - 1));
                float y = kde.Bottom - (float)(context.Density[i] * (kde.Height - 2));
                curve.Add(new PointF(x, y));
            }
            if (curve.Count >= 2 && ramp != null && ramp.Count > 0)
            {
                using (var path = new GraphicsPath())
                {
                    path.AddLines(curve.ToArray());
                    path.AddLine(curve[curve.Count - 1].X, kde.Bottom, curve[0].X, kde.Bottom);
                    path.CloseFigure();
                    using (var brush = BuildGradientBrush(kde))
                    {
                        GraphicsState state = g.Save();
                        try
                        {
                            g.SetClip(path, CombineMode.Intersect);
                            g.FillRectangle(brush, kde);
                        }
                        finally
                        {
                            g.Restore(state);
                        }
                    }
                    using (var pen = new Pen(Color.FromArgb(200, 70, 70, 70), 1.2f))
                        g.DrawLines(pen, curve.ToArray());
                }
            }
            else if (context.Values.Length == 0)
            {
                using (var brush = new SolidBrush(Color.FromArgb(120, 120, 120)))
                    g.DrawString("（该字段没有数值）", Font, brush, kde.Left + 8, kde.Top + 6);
            }

            // 基线
            using (var pen = new Pen(Color.FromArgb(150, 150, 150)))
                g.DrawLine(pen, kde.Left, kde.Bottom, kde.Right, kde.Bottom);

            // 特殊值虚线 + 文字（文字撞在一起时跳过，避免糊成一团）
            var labelRects = new List<RectangleF>();
            using (var dashPen = new Pen(Color.FromArgb(150, 60, 60, 160), 1f) { DashStyle = DashStyle.Dash })
            using (var outsidePen = new Pen(Color.FromArgb(150, 150, 120, 60), 1f) { DashStyle = DashStyle.Dot })
            using (var brush = new SolidBrush(Color.FromArgb(60, 60, 140)))
            using (var outsideBrush = new SolidBrush(Color.FromArgb(140, 110, 70)))
            using (var font = new Font("Microsoft YaHei UI", 7.5f))
            using (var back = new SolidBrush(Color.FromArgb(235, 255, 255, 255)))
            {
                foreach (ContinuousMark mark in Marks)
                {
                    float x = MarkX(mark);
                    // 区间外的特殊值（常见于 μ±3σ、零）画在端点，用点线并标注“超出范围”
                    g.DrawLine(mark.Outside ? outsidePen : dashPen, x, kde.Top + 2, x, TagTop - 4);
                    SizeF size = g.MeasureString(mark.Label, font);
                    float left = x - size.Width / 2;
                    if (left < kde.Left) left = kde.Left;
                    if (left + size.Width > kde.Right) left = kde.Right - size.Width;
                    var rect = new RectangleF(left - 1, kde.Bottom - 1, size.Width + 2, size.Height);
                    bool overlap = labelRects.Any(r => r.IntersectsWith(rect));
                    if (!overlap)
                    {
                        g.FillRectangle(back, rect);
                        g.DrawString(mark.Label, font, mark.Outside ? outsideBrush : brush, left, kde.Bottom - 1);
                        labelRects.Add(rect);
                    }
                }
            }
        }

        // 用色带自身的颜色构造水平渐变刷（左 → 右）
        private Brush BuildGradientBrush(Rectangle area)
        {
            const int stops = 32;
            var colors = new Color[stops];
            var positions = new float[stops];
            for (int i = 0; i < stops; i++)
            {
                double t = (double)i / (stops - 1);
                colors[i] = ramp.GetColor(t);
                positions[i] = (float)t;
            }
            var blend = new ColorBlend { Colors = colors, Positions = positions };
            var brush = new LinearGradientBrush(
                new Rectangle(area.Left, area.Top, Math.Max(2, area.Width), Math.Max(2, area.Height)),
                colors[0], colors[stops - 1], LinearGradientMode.Horizontal)
            { InterpolationColors = blend };
            return brush;
        }

        // 连续模式下的标签：未绑定为向下书签，已绑定为向上书签 + 锁
        private void DrawContinuousTags(Graphics g)
        {
            if (ramp == null) return;
            foreach (ColorRampNode node in ramp.Nodes)
            {
                bool isSelected = ReferenceEquals(node, selected);
                var center = TagCenter(node);
                bool bound = node.IsBound && MarkOf(node) != null;
                if (bound) DrawBoundTag(g, node, center, isSelected);
                else DrawFreeTag(g, node, center, isSelected);
            }
            // 正在拖动并吸附到某条特殊值虚线时，画一条提示线
            if (dragTarget != null)
            {
                float x = MarkX(dragTarget);
                using (var pen = new Pen(Color.FromArgb(220, 30, 120, 60), 2f))
                    g.DrawLine(pen, x, TagTop - 2, x, TagBottom + 2);
            }
        }

        private void DrawFreeTag(Graphics g, ColorRampNode node, PointF center, bool isSelected)
        {
            float x = center.X;
            int top = TagTop + 2;
            int bottom = TagBottom;
            const float hw = FreeTagHalfWidth;
            var points = new[]
            {
                new PointF(x - hw, top), new PointF(x + hw, top),
                new PointF(x + hw, bottom - 7), new PointF(x, bottom), new PointF(x - hw, bottom - 7)
            };
            using (var brush = new SolidBrush(node.Color))
            using (var path = new GraphicsPath())
            {
                path.AddPolygon(points);
                g.FillPath(brush, path);
                using (var pen = new Pen(isSelected ? Color.FromArgb(20, 80, 160) : Color.FromArgb(120, 120, 120), isSelected ? 2.4f : 1.2f))
                    g.DrawPath(pen, path);
            }
            using (var font = new Font("Microsoft YaHei UI", 7.5f))
            using (var brush = new SolidBrush(isSelected ? Color.FromArgb(20, 60, 140) : Color.FromArgb(110, 110, 110)))
            {
                string text = (node.Position * 100).ToString("F0");
                SizeF size = g.MeasureString(text, font);
                g.DrawString(text, font, brush, x - size.Width / 2, top - 13);
            }
        }

        // 绑定形态：向上指的标签（尖端指向虚线），并在标签里画一把锁
        private void DrawBoundTag(Graphics g, ColorRampNode node, PointF center, bool isSelected)
        {
            float x = center.X;
            float realX = XOf(node.Position);
            int top = TagTop;
            int bottom = TagBottom;
            const float hw = TagHalfWidth;
            var points = new[]
            {
                new PointF(x, top), new PointF(x + hw, top + 8),
                new PointF(x + hw, bottom), new PointF(x - hw, bottom),
                new PointF(x - hw, top + 8)
            };
            using (var brush = new SolidBrush(node.Color))
            using (var path = new GraphicsPath())
            {
                path.AddPolygon(points);
                g.FillPath(brush, path);
                using (var pen = new Pen(isSelected ? Color.FromArgb(20, 80, 160) : Color.FromArgb(60, 60, 60), isSelected ? 2.2f : 1.1f))
                    g.DrawPath(pen, path);
            }
            // 标签被横向挪开时，用一条细引线连回虚线
            if (Math.Abs(x - realX) > 1.5f)
            {
                using (var pen = new Pen(Color.FromArgb(160, 90, 90, 160), 1f))
                    g.DrawLine(pen, x, top, realX, top - 6);
            }
            // 锁定符号（.NET 的 SystemIcons 里没有锁，这里直接用 GDI+ 画一把小锁）
            int luminance = (node.Color.R * 299 + node.Color.G * 587 + node.Color.B * 114) / 1000;
            DrawLock(g, new RectangleF(x - 5, top + 9, 10, 12),
                luminance < 128 ? Color.White : Color.FromArgb(40, 40, 40));
        }

        // 画一把小锁：上面是锁环（半圆），下面是锁体
        private static void DrawLock(Graphics g, RectangleF box, Color color)
        {
            float w = box.Width, h = box.Height;
            float penWidth = Math.Max(1.2f, w / 7f);
            using (var pen = new Pen(color, penWidth))
            using (var brush = new SolidBrush(color))
            {
                var shackle = new RectangleF(box.X + w * 0.2f, box.Y, w * 0.6f, h * 0.7f);
                g.DrawArc(pen, shackle, 180, 180);
                var body = new RectangleF(box.X + w * 0.08f, box.Y + h * 0.45f, w * 0.84f, h * 0.5f);
                g.FillRectangle(brush, body);
            }
        }

        // 标签中心：绑定节点要避开相邻绑定标签（贴太紧时左右错开），用确定性的贪心排布
        private PointF TagCenter(ColorRampNode node)
        {
            if (node == null) return new PointF(0, 0);
            if (!node.IsBound || context == null) return new PointF(XOf(node.Position), TagTop);
            Rectangle band = BandRect;
            float realX = XOf(node.Position);
            float half = TagHalfWidth;
            var placed = new List<float>();
            foreach (ColorRampNode other in ramp.Nodes)
            {
                if (!other.IsBound || ReferenceEquals(other, node)) continue;
                if (TagOrder(other) >= TagOrder(node)) continue;
                placed.Add(TagCenterWithoutOffset(other));
            }
            float[] candidates = { 0f, 18f, -18f, 40f, -40f };
            float chosen = realX;
            foreach (float candidate in candidates)
            {
                float x = realX + candidate;
                if (x - half < band.Left + 2) x = band.Left + 2 + half;
                if (x + half > band.Right - 2) x = band.Right - 2 - half;
                bool clash = false;
                foreach (float other in placed)
                    if (Math.Abs(other - x) < 2 * half + 2) { clash = true; break; }
                if (!clash) { chosen = x; break; }
            }
            return new PointF(chosen, TagTop);
        }

        // 绑定标签的“原始”横坐标（不含避让偏移），用于贪心排布
        private float TagCenterWithoutOffset(ColorRampNode node)
        {
            return XOf(node.Position);
        }

        // 绑定标签的排布顺序（按位置从左到右，位置相同按绑定标识）
        private int TagOrder(ColorRampNode node)
        {
            int order = 0;
            foreach (ColorRampNode other in ramp.Nodes)
            {
                if (ReferenceEquals(other, node)) return order;
                if (!other.IsBound) continue;
                if (other.Position < node.Position ||
                    (other.Position == node.Position && string.CompareOrdinal(other.BindingKey, node.BindingKey) < 0))
                    order++;
            }
            return order;
        }

        #endregion

        #region 绑定

        /// <summary>节点当前绑定的特殊值虚线（未绑定返回 null）。</summary>
        public ContinuousMark MarkOf(ColorRampNode node)
        {
            if (node == null || context == null || string.IsNullOrEmpty(node.BindingKey)) return null;
            foreach (ContinuousMark mark in Marks)
                foreach (ContinuousSpecialValue value in mark.Values)
                    if (value.ToString() == node.BindingKey) return mark;
            return null;
        }

        /// <summary>把标签绑定到某条特殊值虚线（位置对齐到该特殊值，并记下绑定标识）。</summary>
        public void BindTo(ColorRampNode node, ContinuousMark mark)
        {
            if (node == null || mark == null) return;
            // 同一根虚线上只保留一个绑定标签：原来的解绑
            foreach (ColorRampNode other in ramp.Nodes)
                if (!ReferenceEquals(other, node) && other.BindingKey == mark.Value.ToString())
                    other.BindingKey = null;
            node.BindingKey = mark.Value.ToString();
            node.Position = mark.Position;
            ramp.Sort();
            bandDirty = true;
            Invalidate();
            RaiseBindingChanged();
        }

        /// <summary>解除绑定（标签回到可自由拖动的位置）。</summary>
        public void Unbind(ColorRampNode node)
        {
            if (node == null || string.IsNullOrEmpty(node.BindingKey)) return;
            node.BindingKey = null;
            bandDirty = true;
            Invalidate();
            RaiseBindingChanged();
        }

        private void RaiseBindingChanged()
        {
            var handler = BindingChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        /// <summary>标签的文字说明（显示在信息栏里）。</summary>
        public string DescribeTag(ColorRampNode node)
        {
            if (node == null) return "";
            ContinuousMark mark = MarkOf(node);
            if (mark == null) return string.Format("位置 {0:F1}%", node.Position * 100);
            return string.Format("已绑定 {0}（位置 {1:F1}%）", mark.Label, node.Position * 100);
        }

        #endregion

        #region 鼠标拖动

        /// <summary>命中测试：返回鼠标下的标签节点。</summary>
        public ColorRampNode HitTestTag(int px, int py)
        {
            if (ramp == null) return null;
            ColorRampNode best = null;
            float bestDistance = float.MaxValue;
            foreach (ColorRampNode node in ramp.Nodes)
            {
                PointF center = TagCenter(node);
                float half = node.IsBound ? TagHalfWidth : FreeTagHalfWidth;
                var bounds = new RectangleF(center.X - half, TagTop, 2 * half, TagBottom - TagTop + 6);
                if (!bounds.Contains(px, py)) continue;
                float distance = Math.Abs(px - center.X);
                if (distance < bestDistance) { bestDistance = distance; best = node; }
            }
            return best;
        }

        /// <summary>开始拖动一个标签（鼠标按下时调用）。</summary>
        public void BeginDragTag(ColorRampNode node)
        {
            dragNode = node;
            dragTarget = null;
            dragStartedBound = node != null && node.IsBound;
            dragStartPixelX = node == null ? 0 : XOf(node.Position);
            // 已绑定的标签再次拖动时，本次拖动**不再**吸附回同一条虚线，
            // 这样用户想把它拖到具体数值时不会被自动贴合干扰
            suppressedMark = dragStartedBound ? MarkOf(node) : null;
        }

        /// <summary>拖动中：水平位置跟随鼠标；靠近特殊值虚线时自动吸附，拖到标签行以下则解除绑定。</summary>
        public void DragTagTo(int px, int py)
        {
            if (dragNode == null || ramp == null) return;
            lastDragY = py;
            dragTarget = null;
            bool inTagZone = py < TagBottom + 2;
            if (context != null && inTagZone)
            {
                float best = SnapBuffer;
                foreach (ContinuousMark mark in Marks)
                {
                    if (ReferenceEquals(mark, suppressedMark)) continue;
                    float dx = Math.Abs(MarkX(mark) - px);
                    if (dx <= best) { best = dx; dragTarget = mark; }
                }
            }
            if (dragTarget != null) dragNode.Position = dragTarget.Position;
            else dragNode.Position = PositionOf(px);
            ramp.Sort();
            bandDirty = true;
            Invalidate();
        }

        /// <summary>结束拖动：吸附到了虚线就绑定；已绑定的标签被拖离该虚线（或拖到下方）则解除绑定。</summary>
        public void EndDragTag()
        {
            if (dragNode == null) return;
            ColorRampNode node = dragNode;
            ContinuousMark target = dragTarget;
            bool belowTagZone = lastDragY >= TagBottom + 2;
            float movedPixels = Math.Abs(XOf(node.Position) - dragStartPixelX);
            bool movedAway = node.IsBound && movedPixels > UnbindPixelThreshold;
            dragNode = null;
            dragTarget = null;
            suppressedMark = null;
            if (target != null) BindTo(node, target);
            else if (node.IsBound && (belowTagZone || movedAway)) Unbind(node);
            Invalidate();
        }

        /// <summary>取消当前拖动（例如 Esc）。</summary>
        public void CancelDragTag()
        {
            dragNode = null;
            dragTarget = null;
            suppressedMark = null;
            Invalidate();
        }

        #endregion
    }
}

