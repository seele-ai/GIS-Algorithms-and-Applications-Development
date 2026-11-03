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

        /// <summary>
        /// 绘图横轴的下界：比 <see cref="Min"/> 再往外扩一点（约 3% 跨度）。
        /// KDE 曲线在真实最大/最小值之外仍有密度，扩一点才不会被两端硬裁断；
        /// 色带仍然只覆盖 [Min, Max]，所以色带条画在横轴的中间一段。
        /// </summary>
        public double AxisMin;

        /// <summary>绘图横轴的上界（<see cref="Max"/> 再往外扩一点）。</summary>
        public double AxisMax;

        /// <summary>横轴两端各扩出去多少。</summary>
        public double AxisPad;

        /// <summary>统计量。</summary>
        public ContinuousStatistics Statistics;

        /// <summary>特殊值虚线（已合并、区间外的画在端点）。</summary>
        public List<ContinuousMark> Marks = new List<ContinuousMark>();

        /// <summary>KDE 曲线（0~1，按 <see cref="CurveSamples"/> 个采样点，覆盖 [AxisMin, AxisMax]）。</summary>
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
            // 横轴在数据范围外再扩 3%（全同值时按数值量级给一个最小扩展量）
            double span = context.Max - context.Min;
            context.AxisPad = span > 0 ? span * 0.03 : Math.Max(Math.Abs(context.Min) * 0.01, 0.5);
            context.AxisMin = context.Min - context.AxisPad;
            context.AxisMax = context.Max + context.AxisPad;
            context.Marks = ContinuousMarks.Build(context.Statistics);
            return context;
        }

        /// <summary>把数值拉伸到 0~1（区间退化时取 0.5）。用于色带取色。</summary>
        public double Normalize(double value)
        {
            double span = Max - Min;
            if (span <= 0) return 0.5;
            double t = (value - Min) / span;
            return t < 0 ? 0 : (t > 1 ? 1 : t);
        }

        /// <summary>把数值映射到**横轴**上的 0~1（横轴比色带范围两端各宽一点，不夹紧）。</summary>
        public double NormalizeAxis(double value)
        {
            double span = AxisMax - AxisMin;
            if (span <= 0) return 0.5;
            double t = (value - AxisMin) / span;
            return t < 0 ? 0 : (t > 1 ? 1 : t);
        }

        /// <summary>横轴 0~1 位置对应的数值。</summary>
        public double ValueOfAxis(double t)
        {
            return AxisMin + (AxisMax - AxisMin) * t;
        }

        /// <summary>色带位置（0~1，相对 [Min, Max]）→ 数值。</summary>
        public double ValueOfRamp(double position)
        {
            return Min + (Max - Min) * position;
        }

        /// <summary>数值 → 色带位置（0~1，夹紧）。</summary>
        public double RampPositionOf(double value)
        {
            return Normalize(value);
        }

        /// <summary>按采样点数重算 KDE 曲线（只在采样点数变化时重算）；曲线覆盖整个横轴（含两端扩展区）。</summary>
        public void EnsureDensity(int samples)
        {
            if (samples < 8) samples = 8;
            if (CurveSamples == samples && Density.Length == samples) return;
            Density = KernelDensity.Estimate(Values, samples, AxisMin, AxisMax);
            CurveSamples = samples;
        }

        /// <summary>某条特殊值虚线的显示文字。</summary>
        public string LabelOf(ContinuousMark mark)
        {
            return mark == null ? "" : mark.Label;
        }

        /// <summary>区间外的特殊值文字（显示在信息栏里，避免挤在图上）。</summary>
        public string OutsideText()
        {
            var names = new List<string>();
            foreach (ContinuousMark mark in Marks)
            {
                if (!mark.Outside) continue;
                foreach (ContinuousSpecialValue value in mark.Values)
                    names.Add(ContinuousMarks.NameOf(value) + " " + ContinuousMarks.FormatValue(mark.DataValue));
            }
            return names.Count == 0 ? "" : "区间外的特殊值：" + string.Join("、", names.ToArray());
        }
    }

    /// <summary>
    /// 色带显示条的连续渲染部分（partial）：带 X/Y 轴的 KDE 分布 + 特殊值虚线 + 可绑定的标签，下方色带。
    /// 无上下文时保持原来的“色带 + 书签”外观，不影响分级/唯一值页面的色带编辑。
    ///
    /// 版面（自上而下）：KDE 绘图区（左 Y 轴、下 X 轴）→ X 轴刻度文字 → 已绑定标签行 → 未绑定标签行 → 色带 → 0~100 轴。
    /// 所有位置都用同一套横向映射（两侧留 buffer），所以最小/最大值也有地方放标签。
    /// </summary>
    public sealed partial class RampStrip
    {
        /// <summary>未绑定标签行的高度。</summary>
        public const int TagHeight = 24;

        /// <summary>已绑定标签行的高度（在未绑定标签行之上，更靠近 X 轴）。</summary>
        public const int LockedTagHeight = 22;

        /// <summary>X 轴刻度文字高度。</summary>
        public const int AxisLabelHeight = 14;

        /// <summary>连续模式下的默认高度（KDE + 轴 + 两行标签 + 色带 + 坐标轴）。</summary>
        public const int ContinuousHeight = 300;

        /// <summary>吸附距离（像素）：标签拖到距虚线这么近就自动贴上。</summary>
        public const float SnapBuffer = 14f;

        /// <summary>特殊值标注的字号（比正文大，便于阅读）。</summary>
        public const float MarkLabelSize = 10f;

        /// <summary>已绑定标签被向下拖动超过这么多像素才解锁（防止误触）。</summary>
        public const int UnlockDragDistance = 16;

        /// <summary>标签位置变化超过这么多像素才算“真的挪开了”。</summary>
        public const float UnbindPixelThreshold = 3f;

        private const int PlotTop = 6;
        private const int ContinuousMarginLeft = 34;    // 左边留给 Y 轴刻度文字
        private const int ContinuousMarginRight = 14;
        private const float TagHalfWidth = 12f;         // 绑定标签（带锁）的半宽
        private const float FreeTagHalfWidth = 8f;      // 未绑定标签（书签）的半宽
        private const int TagLaneGap = 18;              // 绑定标签贴太紧时的横向错开量

        private ContinuousStripContext context;
        private ColorRampNode dragNode;
        private ContinuousMark dragTarget;
        private readonly HashSet<ContinuousMark> snappedMarks = new HashSet<ContinuousMark>();
        private bool dragStartedBound;
        private float dragStartPixelX;
        private int dragStartMouseY;
        private int lastDragY;

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

        // 色带上方依次是：未绑定标签行、已绑定标签行、X 轴刻度文字、X 轴、KDE 绘图区
        private int FreeRowBottom { get { return BandRect.Top - 2; } }
        private int FreeRowTop { get { return FreeRowBottom - TagHeight; } }
        private int LockedRowBottom { get { return FreeRowTop; } }
        private int LockedRowTop { get { return LockedRowBottom - LockedTagHeight; } }
        private int XAxisY { get { return LockedRowTop; } }
        private int AxisLabelTop { get { return XAxisY + 1; } }
        private int PlotBottom { get { return XAxisY - AxisLabelHeight; } }

        private Rectangle KdeRect
        {
            get
            {
                Rectangle band = BandRect;
                int bottom = PlotBottom;
                int top = Math.Min(PlotTop, bottom - 24);
                return new Rectangle(band.Left, top, band.Width, Math.Max(24, bottom - top));
            }
        }

        /// <summary>数值 → 横坐标（整条横轴，含两端扩展区）。</summary>
        public float XOfValue(double value)
        {
            if (context == null) return XOf(0);
            return AxisXOf(context.NormalizeAxis(value));
        }

        /// <summary>横轴 0~1 → 横坐标。</summary>
        private float AxisXOf(double t)
        {
            Rectangle band = BandRect;
            return band.Left + (float)(t * (band.Width - 1));
        }

        /// <summary>横坐标 → 数值（拖动标签时用）。</summary>
        private double ValueAt(float x)
        {
            Rectangle band = BandRect;
            double t = band.Width <= 1 ? 0 : (x - band.Left) / (double)(band.Width - 1);
            if (t < 0) t = 0;
            if (t > 1) t = 1;
            return context.ValueOfAxis(t);
        }

        /// <summary>横坐标 → 色带位置（0~1，相对数据范围）。</summary>
        private double RampPositionAt(float x)
        {
            if (context == null) return PositionOf(x);
            return context.RampPositionOf(ValueAt(x));
        }

        /// <summary>
        /// 特殊值虚线的横坐标。区间内的按数值落在横轴上；**区间外的（μ±2σ、μ±3σ、数据不含 0 时的“零”等）
        /// 直接落在最新横轴范围的两端**（左端/右端），因为它们超出了有颜色的数据范围。
        /// </summary>
        public float MarkX(ContinuousMark mark)
        {
            if (mark == null) return float.NaN;
            if (context == null) return XOf(mark.Position);
            if (mark.Outside) return AxisXOf(mark.DataValue <= context.Min ? 0 : 1);
            return XOfValue(mark.DataValue);
        }

        /// <summary>节点位置对应的横坐标（节点位置是色带上的 0~1，先换成数值再映射到横轴）。</summary>
        public float XOfNode(ColorRampNode node)
        {
            if (node == null) return float.NaN;
            if (context == null) return XOf(node.Position);
            return XOfValue(context.ValueOfRamp(node.Position));
        }

        // 连续模式下色带条只覆盖数据范围 [Min, Max]，横轴两端多出来的部分留白
        private RectangleF ContinuousRampRect()
        {
            Rectangle band = BandRect;
            float left = XOfValue(context.Min), right = XOfValue(context.Max);
            if (right < left) { float t = left; left = right; right = t; }
            return new RectangleF(left, band.Top, Math.Max(2f, right - left + 1), band.Height);
        }

        // 色带某一列对应的色带位置（0~1）；在数据范围之外返回 -1（该列不画色）
        private double ContinuousRampColumn(int x, int width)
        {
            if (context == null) return width <= 1 ? 0 : (double)x / (width - 1);
            RectangleF rect = ContinuousRampRect();
            double t = width <= 1 ? 0 : (double)x / (width - 1);
            double value = context.AxisMin + (context.AxisMax - context.AxisMin) *
                ((rect.Left - BandRect.Left + t * (rect.Width - 1)) / Math.Max(1, BandRect.Width - 1));
            double position = context.RampPositionOf(value);
            if (value < context.Min - 1e-12 || value > context.Max + 1e-12) return -1;
            return position;
        }

        // 色带位置（0~1）对应的横坐标（0~100 坐标轴用）
        private float XOfRampPositionContinuous(double t)
        {
            if (context == null) return XOf(t);
            return XOfValue(context.ValueOfRamp(t));
        }

        /// <summary>标签的横坐标（考虑避开重叠后的偏移）。</summary>
        public float TagCenterOf(ColorRampNode node)
        {
            return TagCenter(node).X;
        }

        /// <summary>标签所在的纵向中心（未绑定在下行，已绑定在上行）。</summary>
        public int TagTopOf(ColorRampNode node)
        {
            return node != null && node.IsBound ? LockedRowTop : FreeRowTop;
        }

        /// <summary>KDE 绘图区（含左侧 Y 轴区域）。</summary>
        public Rectangle PlotArea
        {
            get { return KdeRect; }
        }

        /// <summary>X 轴所在纵坐标。</summary>
        public int XAxisLineY
        {
            get { return XAxisY; }
        }

        /// <summary>已绑定标签行的顶边。</summary>
        public int LockedRowTopY
        {
            get { return LockedRowTop; }
        }

        /// <summary>未绑定标签行的顶边。</summary>
        public int FreeRowTopY
        {
            get { return FreeRowTop; }
        }

        #endregion

        #region 绘制

        // 坐标轴 + 曲线渐变填充 + 特殊值虚线（画在色带、标签之下）
        private void DrawContinuousBackground(Graphics g)
        {
            if (context == null) return;
            Rectangle kde = KdeRect;
            context.EnsureDensity(Math.Max(24, kde.Width));

            DrawAxes(g, kde);

            // 曲线下方：用色带颜色从左到右填充
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
                    var clip = g.Save();
                    try
                    {
                        g.SetClip(path, CombineMode.Intersect);
                        using (Brush brush = BuildGradientBrush(kde)) g.FillRectangle(brush, kde);
                    }
                    finally
                    {
                        g.Restore(clip);
                    }
                }
                using (var pen = new Pen(Color.FromArgb(200, 70, 70, 70), 1.2f))
                    g.DrawLines(pen, curve.ToArray());
            }
            else if (context.Values.Length == 0)
            {
                using (var brush = new SolidBrush(Color.FromArgb(120, 120, 120)))
                    g.DrawString("（该字段没有数值）", Font, brush, kde.Left + 8, kde.Top + 6);
            }

            DrawMarks(g, kde);
        }

        // Y 轴（密度 0~1）+ X 轴（数值刻度）
        private void DrawAxes(Graphics g, Rectangle kde)
        {
            using (var axisPen = new Pen(Color.FromArgb(160, 160, 160)))
            using (var tickPen = new Pen(Color.FromArgb(150, 150, 150)))
            using (var brush = new SolidBrush(Color.FromArgb(90, 90, 90)))
            using (var font = new Font("Microsoft YaHei UI", 7.5f))
            {
                // Y 轴
                g.DrawLine(axisPen, kde.Left, kde.Top, kde.Left, kde.Bottom);
                for (int i = 0; i <= 2; i++)
                {
                    float y = kde.Bottom - (float)(kde.Height - 1) * i / 2f;
                    g.DrawLine(tickPen, kde.Left - 3, y, kde.Left, y);
                    string text = (i == 0 ? "0" : (i == 1 ? "0.5" : "1"));
                    SizeF size = g.MeasureString(text, font);
                    g.DrawString(text, font, brush, kde.Left - 5 - size.Width, y - size.Height / 2);
                }
                // Y 轴标题（竖排）
                var state = g.Save();
                try
                {
                    g.TranslateTransform(8, kde.Top + (kde.Height + 40) / 2f);
                    g.RotateTransform(-90);
                    using (var titleFont = new Font("Microsoft YaHei UI", 7.5f))
                        g.DrawString("密度", titleFont, brush, 0, 0);
                }
                finally
                {
                    g.Restore(state);
                }

                // X 轴
                g.DrawLine(axisPen, kde.Left, XAxisY, kde.Right, XAxisY);
                DrawValueTicks(g, kde, tickPen, brush, font);
            }
        }

        // X 轴刻度：5 个等分点，写数值（相邻太近就跳过）
        private void DrawValueTicks(Graphics g, Rectangle kde, Pen tickPen, Brush brush, Font font)
        {
            const int ticks = 5;
            float lastRight = float.MinValue;
            for (int i = 0; i < ticks; i++)
            {
                double t = (double)i / (ticks - 1);
                float x = kde.Left + (float)(t * (kde.Width - 1));
                double value = context.AxisMin + (context.AxisMax - context.AxisMin) * t;
                string text = ContinuousMarks.FormatValue(value);
                SizeF size = g.MeasureString(text, font);
                g.DrawLine(tickPen, x, XAxisY, x, XAxisY + 3);
                float left = x - size.Width / 2;
                if (left < kde.Left) left = kde.Left;
                if (left + size.Width > kde.Right) left = kde.Right - size.Width;
                if (left < lastRight + 2) continue;   // 与上一个刻度文字撞车就跳过
                g.DrawString(text, font, brush, left, AxisLabelTop);
                lastRight = left + size.Width;
            }
        }

        /// <summary>
        /// 画一条特殊值标注（竖排文字）。**锚点用矩形的右边界**：RotateTransform(90) 之后文字是从锚点
        /// 向左铺开的（实测墨迹在锚点左侧约 4~15 像素内），用右边界作锚点，文字才正好落在预留矩形里。
        /// 单独抽出来也方便自检直接验证“墨迹 ⊂ 矩形”。
        /// </summary>
        public void DrawMarkLabel(Graphics graphics, RectangleF rect, string text, bool outside)
        {
            if (graphics == null || string.IsNullOrEmpty(text)) return;
            using (var font = new Font("Microsoft YaHei UI", MarkLabelSize))
            using (var brush = new SolidBrush(outside ? Color.FromArgb(150, 120, 80) : Color.FromArgb(60, 60, 140)))
            {
                var state = graphics.Save();
                try
                {
                    graphics.TranslateTransform(rect.Right - 1, rect.Y);
                    graphics.RotateTransform(90);
                    graphics.DrawString(text, font, brush, 0, 0);
                }
                finally
                {
                    graphics.Restore(state);
                }
            }
        }

        // 特殊值虚线 + 竖排文字（顶端；先试线的右侧，撞车再试左侧，两边都放不下才跳过）
        private void DrawMarks(Graphics g, Rectangle kde)
        {
            List<RectangleF> rects = ComputeMarkLabelRects(g);
            using (var dashPen = new Pen(Color.FromArgb(150, 60, 60, 160), 1f) { DashStyle = DashStyle.Dash })
            using (var outsidePen = new Pen(Color.FromArgb(150, 160, 120, 60), 1f) { DashStyle = DashStyle.Dot })
            using (var brush = new SolidBrush(Color.FromArgb(60, 60, 140)))
            using (var outsideBrush = new SolidBrush(Color.FromArgb(150, 120, 80)))
            using (var font = new Font("Microsoft YaHei UI", MarkLabelSize))
            {
                var state = g.Save();
                try
                {
                    g.SetClip(kde, CombineMode.Intersect);   // 文字不越出绘图区
                    for (int i = 0; i < Marks.Count; i++)
                    {
                        ContinuousMark mark = Marks[i];
                        float x = MarkX(mark);
                        g.DrawLine(mark.Outside ? outsidePen : dashPen, x, kde.Top + 1, x, XAxisY - 1);
                        RectangleF rect = rects[i];
                        if (rect.IsEmpty) continue;           // 左右都放不下 → 放弃该条文字
                        DrawMarkLabel(g, rect, mark.Label, mark.Outside);
                    }
                }
                finally
                {
                    g.Restore(state);
                }
            }
        }

        /// <summary>
        /// 计算每条特殊值标注（竖排文字）的矩形。
        /// 两条线靠得很近时，**左边那条的标注放到线的左侧、右边那条放到线的右侧**（从左到右读仍是
        /// “标注—线—线—标注”，顺序不会颠倒）；首选的一侧放不下才试另一侧，两侧都放不下就放弃。
        /// 返回顺序与 <see cref="Marks"/> 一致。
        /// </summary>
        public List<RectangleF> ComputeMarkLabelRects(Graphics graphics)
        {
            var result = new List<RectangleF>();
            var placed = new List<RectangleF>();
            Rectangle plot = KdeRect;
            var xs = new float[Marks.Count];
            var widths = new float[Marks.Count];
            var heights = new float[Marks.Count];
            using (var font = new Font("Microsoft YaHei UI", MarkLabelSize))
            {
                for (int i = 0; i < Marks.Count; i++)
                {
                    SizeF size = graphics.MeasureString(Marks[i].Label, font);
                    widths[i] = size.Height + 2;    // 竖排：宽 = 字高
                    heights[i] = size.Width + 2;    // 竖排：高 = 文字长度
                    xs[i] = MarkX(Marks[i]);
                }
                for (int i = 0; i < Marks.Count; i++)
                {
                    float x = xs[i], w = widths[i], h = heights[i];
                    // 与右侧最近的一条虚线靠得太近（放不下两个标注）→ 自己优先放左侧，把右侧让给它
                    float nextGap = float.MaxValue;
                    for (int j = 0; j < Marks.Count; j++)
                        if (j != i && xs[j] > x + 0.5f) nextGap = Math.Min(nextGap, xs[j] - x);
                    bool preferLeft = nextGap < 2 * w + 6;

                    var leftRect = new RectangleF(x - 3 - w, plot.Top + 2, w, h);
                    var rightRect = new RectangleF(x + 3, plot.Top + 2, w, h);
                    RectangleF[] order = preferLeft
                        ? new[] { leftRect, rightRect }
                        : new[] { rightRect, leftRect };
                    RectangleF chosen = RectangleF.Empty;
                    foreach (RectangleF candidate in order)
                    {
                        if (candidate.Left < plot.Left || candidate.Right > plot.Right) continue;
                        if (placed.Any(r => r.IntersectsWith(candidate))) continue;
                        chosen = candidate;
                        placed.Add(candidate);
                        break;
                    }
                    result.Add(chosen);
                }
            }
            return result;
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
            return new LinearGradientBrush(
                new Rectangle(area.Left, area.Top, Math.Max(2, area.Width), Math.Max(2, area.Height)),
                colors[0], colors[stops - 1], LinearGradientMode.Horizontal)
            { InterpolationColors = blend };
        }

        // 连续模式下的标签：已绑定在上行（朝上 + 锁），未绑定在下行（朝下书签）
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
            if (dragTarget != null)
            {
                float x = MarkX(dragTarget);
                using (var pen = new Pen(Color.FromArgb(220, 30, 120, 60), 2f))
                    g.DrawLine(pen, x, LockedRowTop - 2, x, FreeRowBottom + 2);
            }
        }

        // 未绑定：朝下的书签（不再写位置数值——X 轴已给出数值）
        private void DrawFreeTag(Graphics g, ColorRampNode node, PointF center, bool isSelected)
        {
            float x = center.X;
            int top = FreeRowTop + 2;
            int bottom = FreeRowBottom;
            const float hw = FreeTagHalfWidth;
            var points = new[]
            {
                new PointF(x - hw, top), new PointF(x + hw, top),
                new PointF(x + hw, bottom - 6), new PointF(x, bottom), new PointF(x - hw, bottom - 6)
            };
            using (var brush = new SolidBrush(node.Color))
            using (var path = new GraphicsPath())
            {
                path.AddPolygon(points);
                g.FillPath(brush, path);
                using (var pen = new Pen(isSelected ? Color.FromArgb(20, 80, 160) : Color.FromArgb(120, 120, 120), isSelected ? 2.4f : 1.2f))
                    g.DrawPath(pen, path);
            }
        }

        // 已绑定：朝上的标签（尖端指向虚线），带锁
        private void DrawBoundTag(Graphics g, ColorRampNode node, PointF center, bool isSelected)
        {
            float x = center.X;
            float realX = XOfNode(node);
            int top = LockedRowTop + 1;
            int bottom = LockedRowBottom;
            const float hw = TagHalfWidth;
            var points = new[]
            {
                new PointF(x, top), new PointF(x + hw, top + 7),
                new PointF(x + hw, bottom), new PointF(x - hw, bottom),
                new PointF(x - hw, top + 7)
            };
            using (var brush = new SolidBrush(node.Color))
            using (var path = new GraphicsPath())
            {
                path.AddPolygon(points);
                g.FillPath(brush, path);
                using (var pen = new Pen(isSelected ? Color.FromArgb(20, 80, 160) : Color.FromArgb(60, 60, 60), isSelected ? 2.2f : 1.1f))
                    g.DrawPath(pen, path);
            }
            if (Math.Abs(x - realX) > 1.5f)
            {
                using (var pen = new Pen(Color.FromArgb(160, 90, 90, 160), 1f))
                    g.DrawLine(pen, x, top + 1, realX, XAxisY + 1);
            }
            int luminance = (node.Color.R * 299 + node.Color.G * 587 + node.Color.B * 114) / 1000;
            DrawLock(g, new RectangleF(x - 4.5f, top + 7, 9, 11),
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

        // 标签中心：绑定标签以“线”为中心（最左/最右也允许半个标签伸出色带范围，
        // 因为两侧留了 buffer；只要不超出控件即可），相邻标签贴太紧时再横向错开。
        private PointF TagCenter(ColorRampNode node)
        {
            if (node == null) return new PointF(0, 0);
            if (!node.IsBound || context == null) return new PointF(XOfNode(node), FreeRowTop);
            float realX = XOfNode(node);
            float half = TagHalfWidth;
            var placed = new List<float>();
            int order = TagOrder(node);
            foreach (ColorRampNode other in ramp.Nodes)
            {
                if (!other.IsBound || ReferenceEquals(other, node)) continue;
                if (TagOrder(other) >= order) continue;
                placed.Add(TagCenterOfLane(other));
            }
            float[] candidates = { 0f, TagLaneGap, -TagLaneGap, 2 * TagLaneGap, -2 * TagLaneGap };
            float chosen = ClampTag(realX, half);
            foreach (float candidate in candidates)
            {
                float x = ClampTag(realX + candidate, half);
                bool clash = false;
                foreach (float other in placed)
                    if (Math.Abs(other - x) < 2 * half + 2) { clash = true; break; }
                if (!clash) { chosen = x; break; }
            }
            return new PointF(chosen, LockedRowTop);
        }

        // 只保证标签不超出控件（不再把它挤进色带范围，否则端点处会变成“边对齐”而不是“中心对齐”）
        private float ClampTag(float x, float half)
        {
            if (x - half < 1) return 1 + half;
            if (x + half > Width - 1) return Width - 1 - half;
            return x;
        }

        // 已绑定标签的理想横坐标（用于避让计算，不含避让偏移）
        private float TagCenterOfLane(ColorRampNode node)
        {
            return ClampTag(XOfNode(node), TagHalfWidth);
        }

        // 已绑定标签的排布顺序（按位置从左到右，位置相同按绑定标识）
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

        /// <summary>命中测试：返回鼠标下的标签节点（同时检查两行标签）。</summary>
        public ColorRampNode HitTestTag(int px, int py)
        {
            if (ramp == null) return null;
            ColorRampNode best = null;
            float bestDistance = float.MaxValue;
            foreach (ColorRampNode node in ramp.Nodes)
            {
                PointF center = TagCenter(node);
                bool bound = node.IsBound && MarkOf(node) != null;
                float half = bound ? TagHalfWidth : FreeTagHalfWidth;
                int top = bound ? LockedRowTop : FreeRowTop;
                int bottom = bound ? LockedRowBottom : FreeRowBottom;
                var bounds = new RectangleF(center.X - half, top, 2 * half, bottom - top + 4);
                if (!bounds.Contains(px, py)) continue;
                float distance = Math.Abs(px - center.X);
                if (distance < bestDistance) { bestDistance = distance; best = node; }
            }
            return best;
        }

        /// <summary>开始拖动一个标签（鼠标按下时调用）。</summary>
        public void BeginDragTag(ColorRampNode node)
        {
            BeginDragTag(node, 0);
        }

        /// <summary>开始拖动一个标签，y 为按下时的鼠标纵坐标（用于判断“向下拖动解锁”）。</summary>
        public void BeginDragTag(ColorRampNode node, int mouseY)
        {
            dragNode = node;
            dragTarget = null;
            snappedMarks.Clear();
            dragStartedBound = node != null && node.IsBound;
            dragStartPixelX = node == null ? 0 : XOfNode(node);
            dragStartMouseY = mouseY;
            lastDragY = mouseY;
        }

        /// <summary>
        /// 拖动中。未绑定标签：跟手移动，靠近特殊值虚线（14 像素内）时吸附；
        /// **每条虚线在一次拖动中只吸附一次**（贴过又拖开，再路过它就不再吸附，方便设精确值）。
        /// 已绑定的标签：不能自由移动，只有向下拖动（超过 16 像素）才会在松手时解锁。
        /// </summary>
        public void DragTagTo(int px, int py)
        {
            if (dragNode == null || ramp == null) return;
            lastDragY = py;
            if (dragStartedBound)
            {
                // 已绑定：位置锁定在虚线上，不接受横向/向上的拖动
                dragTarget = null;
                Invalidate();
                return;
            }
            bool inTagZone = py < FreeRowBottom + 4;
            ContinuousMark candidate = null;
            if (context != null && inTagZone)
            {
                float best = SnapBuffer;
                foreach (ContinuousMark mark in Marks)
                {
                    if (snappedMarks.Contains(mark)) continue;
                    float dx = Math.Abs(MarkX(mark) - px);
                    if (dx <= best) { best = dx; candidate = mark; }
                }
            }
            // 本次拖动已经贴过的虚线：离开它的吸附范围后记下来，之后不再吸附它
            if (dragTarget != null && candidate != dragTarget)
                snappedMarks.Add(dragTarget);
            if (candidate != null && candidate != dragTarget)
            {
                dragTarget = candidate;
                dragNode.Position = candidate.Position;
            }
            else if (candidate == null)
            {
                dragTarget = null;
                dragNode.Position = RampPositionAt(px);
            }
            ramp.Sort();
            bandDirty = true;
            Invalidate();
        }

        /// <summary>结束拖动：未绑定标签吸附到虚线则绑定；已绑定标签向下拖（>16px）则解锁。</summary>
        public void EndDragTag()
        {
            if (dragNode == null) return;
            ColorRampNode node = dragNode;
            ContinuousMark target = dragTarget;
            bool draggedDown = lastDragY - dragStartMouseY >= UnlockDragDistance;
            bool wasBound = dragStartedBound;
            dragNode = null;
            dragTarget = null;
            snappedMarks.Clear();
            if (wasBound)
            {
                // 锁定形态只能“向下拖动解锁”，其余拖动一律保持锁定与位置
                if (draggedDown) Unbind(node);
            }
            else if (target != null)
            {
                BindTo(node, target);
            }
            else if (node.IsBound && Math.Abs(XOfNode(node) - dragStartPixelX) > UnbindPixelThreshold)
            {
                Unbind(node);
            }
            Invalidate();
        }

        /// <summary>取消当前拖动（例如 Esc）。</summary>
        public void CancelDragTag()
        {
            dragNode = null;
            dragTarget = null;
            snappedMarks.Clear();
            Invalidate();
        }

        #endregion
    }
}

