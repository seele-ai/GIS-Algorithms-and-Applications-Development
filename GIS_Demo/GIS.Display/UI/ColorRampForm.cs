using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using ScreenPoint = System.Drawing.Point;

namespace GIS.Display.UI
{
    /// <summary>
    /// 色带显示条：上方为书签形状的竖向节点按钮，下方为 0~100 的坐标轴与用色带自身颜色渲染的色带。
    /// 支持点击选择节点、拖动节点改变位置。
    /// </summary>
    public sealed class RampStrip : Control
    {
        private const int BookmarkTop = 10;
        private const int BandHeight = 38;
        private const int AxisHeight = 22;

        private ColorRamp ramp;
        private ColorRampNode selected;
        private bool dragging;
        private Bitmap bandCache;      // 色带条缓存：逐像素取色较慢，只在色带变化或尺寸变化时重建
        private bool bandDirty = true;

        public RampStrip()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.White;
            Height = 116;
        }

        /// <summary>色带内容发生变化后调用：使色带条缓存失效并重绘。</summary>
        public void NotifyRampChanged()
        {
            bandDirty = true;
            Invalidate();
        }

        /// <summary>被编辑的色带。</summary>
        public ColorRamp Ramp
        {
            get { return ramp; }
            set
            {
                ramp = value;
                selected = ramp == null ? null : ramp.Head;
                bandDirty = true;
                Invalidate();
                OnSelectedNodeChanged();
            }
        }

        /// <summary>当前选中的节点。</summary>
        public ColorRampNode SelectedNode
        {
            get { return selected; }
            set { if (!ReferenceEquals(selected, value)) { selected = value; Invalidate(); OnSelectedNodeChanged(); } }
        }

        public event EventHandler SelectedNodeChanged;

        private void OnSelectedNodeChanged()
        {
            var handler = SelectedNodeChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private int BandTop2 { get { return Height - AxisHeight - BandHeight - 4; } }

        private Rectangle BandRect
        {
            get
            {
                int top = Height - AxisHeight - BandHeight - 4;
                return new Rectangle(16, top, Math.Max(24, Width - 32), BandHeight);
            }
        }

        private float XOf(double position)
        {
            Rectangle band = BandRect;
            return band.Left + (float)(position * (band.Width - 1));
        }

        private double PositionOf(float x)
        {
            Rectangle band = BandRect;
            double t = band.Width <= 1 ? 0 : (x - band.Left) / (double)(band.Width - 1);
            return t < 0 ? 0 : (t > 1 ? 1 : t);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            if (ramp == null || ramp.Count == 0)
            {
                using (var brush = new SolidBrush(Color.Gray))
                    g.DrawString("（色带为空）", Font, brush, 16, BandRect.Top + 8);
                return;
            }

            Rectangle band = BandRect;

            // 色带本体：逐列按色带取色，即“用色带自身的颜色”渲染（结果缓存为位图，避免每次重绘都逐像素取色）
            if (bandDirty || bandCache == null || bandCache.Width != band.Width || bandCache.Height != band.Height)
            {
                if (bandCache != null) bandCache.Dispose();
                bandCache = new Bitmap(Math.Max(1, band.Width), Math.Max(1, band.Height));
                using (Graphics cache = Graphics.FromImage(bandCache))
                {
                    for (int x = 0; x < band.Width; x++)
                    {
                        double t = band.Width <= 1 ? 0 : (double)x / (band.Width - 1);
                        using (var brush = new SolidBrush(ramp.GetColor(t)))
                            cache.FillRectangle(brush, x, 0, 1, band.Height);
                    }
                }
                bandDirty = false;
            }
            g.DrawImageUnscaled(bandCache, band.Left, band.Top);
            using (var pen = new Pen(Color.FromArgb(120, 120, 120)))
                g.DrawRectangle(pen, band.Left - 1, band.Top - 1, band.Width + 1, band.Height + 1);

            // 坐标轴
            int axisY = band.Bottom + 2;
            using (var pen = new Pen(Color.FromArgb(140, 140, 140)))
            {
                g.DrawLine(pen, band.Left, axisY, band.Right - 1, axisY);
            }
            using (var tickPen = new Pen(Color.FromArgb(140, 140, 140)))
            using (var brush = new SolidBrush(Color.FromArgb(90, 90, 90)))
            using (var font = new Font("Microsoft YaHei UI", 7.5f))
            {
                for (int v = 0; v <= 100; v += 10)
                {
                    float x = XOf(v / 100.0);
                    g.DrawLine(tickPen, x, axisY, x, axisY + 4);
                    string text = v.ToString();
                    SizeF size = g.MeasureString(text, font);
                    g.DrawString(text, font, brush, x - size.Width / 2, axisY + 5);
                }
            }

            // 选中节点的竖向参考线
            if (selected != null)
            {
                float sx = XOf(selected.Position);
                using (var pen = new Pen(Color.FromArgb(70, 120, 180), 1f) { DashStyle = DashStyle.Dot })
                    g.DrawLine(pen, sx, BookmarkTop, sx, band.Bottom);
            }

            // 书签形状的节点按钮
            foreach (ColorRampNode node in ramp.Nodes)
                DrawBookmark(g, node, ReferenceEquals(node, selected));
        }

        private void DrawBookmark(Graphics g, ColorRampNode node, bool isSelected)
        {
            float x = XOf(node.Position);
            int top = BookmarkTop + 2;
            int bottom = BandRect.Top - 6;
            const float hw = 9f;
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
            // 位置数值
            using (var font = new Font("Microsoft YaHei UI", 7.5f))
            using (var brush = new SolidBrush(isSelected ? Color.FromArgb(20, 60, 140) : Color.FromArgb(110, 110, 110)))
            {
                string text = (node.Position * 100).ToString("F0");
                SizeF size = g.MeasureString(text, font);
                g.DrawString(text, font, brush, x - size.Width / 2, top - 12);
            }
        }

        private static RectangleF BookmarkBounds(float x, int top, int bottom)
        {
            return new RectangleF(x - 12, top - 14, 24, bottom - top + 20);
        }

        private ColorRampNode HitTest(int px, int py)
        {
            if (ramp == null) return null;
            int top = BookmarkTop + 2, bottom = BandRect.Top - 6;
            ColorRampNode best = null;
            double bestDistance = double.MaxValue;
            foreach (ColorRampNode node in ramp.Nodes)
            {
                float x = XOf(node.Position);
                if (BookmarkBounds(x, top, bottom).Contains(px, py))
                {
                    double d = Math.Abs(px - x);
                    if (d < bestDistance) { bestDistance = d; best = node; }
                }
            }
            return best;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || ramp == null) return;
            ColorRampNode node = HitTest(e.X, e.Y);
            if (node == null) return;
            SelectedNode = node;
            dragging = true;
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!dragging || selected == null) return;
            selected.Position = PositionOf(e.X);
            ramp.Sort();
            bandDirty = true;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!dragging) return;
            dragging = false;
            Capture = false;
            OnSelectedNodeChanged();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && bandCache != null) { bandCache.Dispose(); bandCache = null; }
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// 色带下拉框：每一项用该色带自身的颜色画出一条渐变条，最后一项为“自定义色带…”。
    /// </summary>
    public sealed class ColorRampComboBox : ComboBox
    {
        /// <summary>下拉框最下方“自定义色带”项的文本。</summary>
        public const string CustomItemText = "自定义色带…";

        private readonly List<ColorRamp> ramps = new List<ColorRamp>();
        private readonly Dictionary<ColorRamp, Bitmap> gradientCache = new Dictionary<ColorRamp, Bitmap>();
        private const int GradientWidth = 256;

        public ColorRampComboBox()
        {
            DropDownStyle = ComboBoxStyle.DropDownList;
            DrawMode = DrawMode.OwnerDrawFixed;
            ItemHeight = 24;
            foreach (ColorRamp ramp in ColorRamp.BuiltIn()) AddRamp(ramp);
            Items.Add(CustomItemText);
        }

        /// <summary>下拉框中列出的色带（不含“自定义色带”项）。</summary>
        public IList<ColorRamp> Ramps { get { return ramps; } }

        /// <summary>当前选中的色带；选中“自定义色带”项时返回 null。</summary>
        public ColorRamp SelectedRamp
        {
            get { return SelectedIndex >= 0 && SelectedIndex < ramps.Count ? ramps[SelectedIndex] : null; }
        }

        /// <summary>是否选中了最下方的“自定义色带”项。</summary>
        public bool IsCustomSelected { get { return SelectedIndex == ramps.Count; } }

        /// <summary>追加一条色带并选中它。</summary>
        public void AddRamp(ColorRamp ramp)
        {
            ramps.Add(ramp);
            Items.Insert(ramps.Count - 1, ramp.Name);
        }

        /// <summary>选择指定的色带对象。</summary>
        public void SelectRamp(ColorRamp ramp)
        {
            for (int i = 0; i < ramps.Count; i++)
            {
                if (ReferenceEquals(ramps[i], ramp)) { SelectedIndex = i; return; }
            }
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            e.DrawBackground();
            if (e.Index < 0) { e.DrawFocusRectangle(); return; }
            Graphics g = e.Graphics;
            Rectangle b = e.Bounds;
            if (e.Index < ramps.Count)
            {
                ColorRamp ramp = ramps[e.Index];
                Rectangle bar = new Rectangle(b.Left + 4, b.Top + 3, Math.Max(20, b.Width - 8), Math.Max(6, b.Height - 6));
                g.DrawImage(RampGradient(ramp), bar);
                using (var pen = new Pen(Color.FromArgb(130, 130, 130)))
                    g.DrawRectangle(pen, bar.Left - 1, bar.Top - 1, bar.Width + 1, bar.Height + 1);
            }
            else
            {
                using (var brush = new SolidBrush(Color.FromArgb(40, 40, 40)))
                using (var font = new Font(e.Font, FontStyle.Italic))
                    g.DrawString(CustomItemText, font, brush, b.Left + 6, b.Top + 4);
            }
            e.DrawFocusRectangle();
        }

        // 按色带生成 256×1 的渐变位图并缓存（色带对象加入后不再变化，可安全缓存）
        private Bitmap RampGradient(ColorRamp ramp)
        {
            Bitmap bitmap;
            if (gradientCache.TryGetValue(ramp, out bitmap)) return bitmap;
            bitmap = new Bitmap(GradientWidth, 1);
            for (int x = 0; x < GradientWidth; x++)
            {
                double t = (double)x / (GradientWidth - 1);
                bitmap.SetPixel(x, 0, ramp.GetColor(t));
            }
            gradientCache[ramp] = bitmap;
            return bitmap;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (Bitmap bitmap in gradientCache.Values) bitmap.Dispose();
                gradientCache.Clear();
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>内置色带选择对话框（带渐变预览）。</summary>
    public sealed class BuiltInRampForm : Form
    {
        private readonly ListBox list = new ListBox { Dock = DockStyle.Fill, DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = 28, BorderStyle = BorderStyle.FixedSingle, IntegralHeight = false };

        private BuiltInRampForm(IList<ColorRamp> ramps)
        {
            Text = "载入内置色带";
            Font = new Font("Microsoft YaHei UI", 9F);
            ClientSize = new Size(360, 300);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            foreach (ColorRamp ramp in ramps) list.Items.Add(ramp);
            if (list.Items.Count > 0) list.SelectedIndex = 0;
            list.DrawItem += (s, e) =>
            {
                e.DrawBackground();
                if (e.Index < 0) return;
                ColorRamp ramp = (ColorRamp)list.Items[e.Index];
                Rectangle b = e.Bounds;
                Rectangle bar = new Rectangle(b.Left + 8, b.Top + 4, Math.Max(20, b.Width - 16), b.Height - 8);
                for (int x = 0; x < bar.Width; x++)
                {
                    double t = bar.Width <= 1 ? 0 : (double)x / (bar.Width - 1);
                    using (var brush = new SolidBrush(ramp.GetColor(t)))
                        e.Graphics.FillRectangle(brush, bar.Left + x, bar.Top, 1, bar.Height);
                }
                using (var pen = new Pen(Color.FromArgb(130, 130, 130)))
                    e.Graphics.DrawRectangle(pen, bar.Left - 1, bar.Top - 1, bar.Width + 1, bar.Height + 1);
                e.DrawFocusRectangle();
            };
            var ok = new Button { Text = "确定", DialogResult = DialogResult.OK, Width = 84 };
            var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Width = 84 };
            var btns = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(0, 6, 10, 0) };
            btns.Controls.Add(cancel);
            btns.Controls.Add(ok);
            Controls.Add(list);
            Controls.Add(btns);
            AcceptButton = ok; CancelButton = cancel;
        }

        /// <summary>弹出内置色带选择框，返回选中的色带副本。</summary>
        public static ColorRamp Pick(IWin32Window owner, IList<ColorRamp> ramps)
        {
            if (ramps == null || ramps.Count == 0) return null;
            using (var f = new BuiltInRampForm(ramps))
                return f.ShowDialog(owner) == DialogResult.OK && f.list.SelectedItem != null
                    ? ((ColorRamp)f.list.SelectedItem).Clone() : null;
        }
    }

    /// <summary>
    /// 色带编辑器：上方为色带（含书签形状的节点按钮与 0~100 坐标轴），
    /// 中部为节点编辑按钮，下方为保存/读取与 应用 / 确定 / 取消 / 退出。
    /// </summary>
    public sealed class ColorRampEditor : Form
    {
        private readonly RampStrip strip = new RampStrip { Dock = DockStyle.Top, Height = 116 };
        private readonly Label info = new Label { Dock = DockStyle.Top, Height = 44, Padding = new Padding(14, 4, 0, 0) };
        private ColorRamp working;
        private bool hasApplied;

        /// <summary>正在编辑的色带（实时反映界面上的修改）。</summary>
        public ColorRamp Working { get { return working; } }

        /// <summary>是否至少按过一次“应用”。</summary>
        public bool HasApplied { get { return hasApplied; } }

        /// <summary>按下“应用”或“确定”时触发。</summary>
        public event EventHandler RampApplied;

        public ColorRampEditor(ColorRamp source)
        {
            Name = "色带编辑器";
            Text = "色带编辑器";
            Font = new Font("Microsoft YaHei UI", 9F);
            ClientSize = new Size(980, 372);
            MinimumSize = new Size(980, 372);
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false; ShowInTaskbar = false;

            working = source == null ? ColorRamp.CreateDefault() : source.Clone();

            strip.SelectedNodeChanged += (s, e) => UpdateInfo();

            var hint = new Label
            {
                Dock = DockStyle.Top, Height = 26, Padding = new Padding(14, 4, 0, 0),
                Text = "色带节点（书签）可拖动改变位置，下方的色带即按各节点的颜色与插值方式渲染。",
                ForeColor = Color.FromArgb(90, 90, 90)
            };

            var row1 = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(12, 6, 0, 0), WrapContents = false };
            AddButton(row1, "新增节点", (s, e) => AddNode());
            AddButton(row1, "删除节点", (s, e) => DeleteNode());
            AddButton(row1, "均匀分布节点", (s, e) => { working.DistributeEvenly(); Refresh1(); });
            AddButton(row1, "设置节点位置", (s, e) => SetPosition());
            AddButton(row1, "设置节点颜色", (s, e) => SetColor());
            AddButton(row1, "设置与上一个节点间的插值方法", (s, e) => SetInterpolation());

            var row2 = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(12, 0, 0, 0), WrapContents = false };
            AddButton(row2, "保存为色带文件", (s, e) => SaveRamp());
            AddButton(row2, "读取色带文件", (s, e) => LoadRamp());
            AddButton(row2, "载入内置色带", (s, e) => LoadBuiltIn());

            var apply = new Button { Text = "应用", Width = 84 };
            apply.Click += (s, e) => Apply(false);
            var ok = new Button { Text = "确定", Width = 84 };
            ok.Click += (s, e) => { if (Apply(true)) DialogResult = DialogResult.OK; };
            var cancel = new Button { Text = "取消", Width = 84 };
            cancel.Click += (s, e) => DialogResult = DialogResult.Cancel;
            var exit = new Button { Text = "退出", Width = 84 };
            exit.Click += (s, e) => DialogResult = hasApplied ? DialogResult.OK : DialogResult.Cancel;
            var btns = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 46, FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(0, 6, 10, 0) };
            btns.Controls.Add(exit);
            btns.Controls.Add(cancel);
            btns.Controls.Add(ok);
            btns.Controls.Add(apply);
            CancelButton = cancel;

            // Dock 顺序：后添加者先布置，因此按“从内到外”的逆序添加
            Controls.Add(row2);
            Controls.Add(row1);
            Controls.Add(info);
            Controls.Add(hint);
            Controls.Add(strip);
            Controls.Add(btns);

            strip.Ramp = working;
            UpdateInfo();
        }

        private static void AddButton(Control host, string text, EventHandler handler)
        {
            var button = new Button
            {
                Text = text,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, 0, 6, 0),
                Padding = new Padding(6, 2, 6, 2)
            };
            button.Click += handler;
            host.Controls.Add(button);
        }

        #region 节点操作

        private void AddNode()
        {
            ColorRampNode sel = strip.SelectedNode;
            double position;
            RampInterpolation interpolation = RampInterpolation.Rgb;
            if (working.Count == 0) position = 0.5;
            else if (sel == null) position = working.Tail.Position >= 1 ? 0.5 : (working.Tail.Position + 1) / 2;
            else if (sel.Next == null)
                position = sel.Position >= 1
                    ? (sel.Previous == null ? 0.5 : (sel.Previous.Position + sel.Position) / 2)
                    : (sel.Position + 1) / 2;
            else position = (sel.Position + sel.Next.Position) / 2;
            if (sel != null && sel.Next != null) interpolation = sel.Next.Interpolation;
            ColorRampNode node = working.InsertNode(position, working.GetColor(position), interpolation);
            strip.SelectedNode = node;
            Refresh1();
        }

        private void DeleteNode()
        {
            if (working.Count <= 2)
            {
                MessageBox.Show(this, "色带至少需要保留 2 个节点。", "色带编辑器", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            ColorRampNode sel = strip.SelectedNode;
            if (sel == null) return;
            ColorRampNode next = sel.Next ?? sel.Previous;
            working.RemoveNode(sel);
            strip.SelectedNode = next;
            Refresh1();
        }

        private void SetPosition()
        {
            ColorRampNode sel = strip.SelectedNode;
            if (sel == null) { MessageBox.Show(this, "请先选择要设置位置的节点。", "色带编辑器"); return; }
            double value = sel.Position * 100;
            if (!AskPosition(this, ref value)) return;
            sel.Position = value / 100.0;
            working.Sort();
            Refresh1();
        }

        private void SetColor()
        {
            ColorRampNode sel = strip.SelectedNode;
            if (sel == null) { MessageBox.Show(this, "请先选择要设置颜色的节点。", "色带编辑器"); return; }
            Color color = ColorPickerForm.Pick(this, sel.Color, "选择色带节点颜色（可设置透明度）");
            if (color == Color.Empty) return;
            sel.Color = color;
            Refresh1();
        }

        private void SetInterpolation()
        {
            ColorRampNode sel = strip.SelectedNode;
            if (sel == null) { MessageBox.Show(this, "请先选择节点。", "色带编辑器"); return; }
            if (sel.Previous == null)
            {
                MessageBox.Show(this, "第一个节点之前没有线段，无法设置与上一个节点间的插值方法。", "色带编辑器");
                return;
            }
            RampInterpolation value = sel.Interpolation;
            if (!AskInterpolation(this, sel.Previous.Color, sel.Color, ref value)) return;
            sel.Interpolation = value;
            Refresh1();
        }

        #endregion

        #region 文件与内置色带

        private void SaveRamp()
        {
            using (var dialog = new SaveFileDialog())
            {
                dialog.Title = "保存为自定义色带文件";
                dialog.Filter = ColorRamp.FileFilter;
                dialog.DefaultExt = "gcr";
                dialog.AddExtension = true;
                dialog.FileName = string.IsNullOrEmpty(working.Name) ? "自定义色带" : working.Name;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    working.Save(dialog.FileName);
                    MessageBox.Show(this, "已保存到：\r\n" + dialog.FileName, "色带编辑器",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "保存失败：" + ex.Message, "色带编辑器",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private void LoadRamp()
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "读取自定义色带文件";
                dialog.Filter = ColorRamp.FileFilter;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    working = ColorRamp.Load(dialog.FileName);
                    strip.Ramp = working;
                    Refresh1();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "读取失败：" + ex.Message, "色带编辑器",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private void LoadBuiltIn()
        {
            ColorRamp ramp = BuiltInRampForm.Pick(this, ColorRamp.BuiltIn());
            if (ramp == null) return;
            working = ramp;
            strip.Ramp = working;
            Refresh1();
        }

        #endregion

        private bool Apply(bool fromOk)
        {
            if (working.Count < 2)
            {
                MessageBox.Show(this, "色带至少需要 2 个节点。", "色带编辑器",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            hasApplied = true;
            working.Sort();
            var handler = RampApplied;
            if (handler != null) handler(this, EventArgs.Empty);
            return true;
        }

        private void Refresh1()
        {
            strip.NotifyRampChanged();
            UpdateInfo();
        }

        private void UpdateInfo()
        {
            ColorRampNode sel = strip.SelectedNode;
            if (sel == null)
            {
                info.Text = string.Format("色带“{0}”：共 {1} 个节点", working.Name, working.Count);
                return;
            }
            info.Text = string.Format(
                "色带“{0}”：共 {1} 个节点    当前节点：位置 {2:F1}%    颜色 #{3:X2}{4:X2}{5:X2}（A={6}）    与上一个节点间：{7}",
                working.Name, working.Count, sel.Position * 100, sel.Color.R, sel.Color.G, sel.Color.B, sel.Color.A,
                sel.Previous == null ? "（首个节点）" : sel.InterpolationName);
        }

        /// <summary>打开色带编辑器。返回编辑结果；用户取消且从未应用过时返回 null。</summary>
        public static ColorRamp Edit(IWin32Window owner, ColorRamp source, Action<ColorRamp> onApplied)
        {
            using (var form = new ColorRampEditor(source))
            {
                if (onApplied != null)
                    form.RampApplied += (s, e) => onApplied(form.Working.Clone());
                DialogResult result = form.ShowDialog(owner);
                if (result == DialogResult.OK) return form.Working.Clone();
                return form.HasApplied ? form.Working.Clone() : null;
            }
        }

        #region 小输入对话框

        private static bool AskPosition(IWin32Window owner, ref double position)
        {
            using (var form = new Form())
            {
                form.Text = "设置节点位置";
                form.Font = new Font("Microsoft YaHei UI", 9F);
                form.ClientSize = new Size(300, 118);
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.StartPosition = FormStartPosition.CenterParent;
                form.MaximizeBox = false; form.MinimizeBox = false; form.ShowInTaskbar = false;
                var label = new Label { Text = "位置（0 ~ 100）：", Dock = DockStyle.Top, Height = 30, Padding = new Padding(12, 8, 0, 0) };
                var box = new NumericUpDown { Minimum = 0, Maximum = 100, DecimalPlaces = 1, Increment = 0.5m,
                    Value = (decimal)Math.Max(0, Math.Min(100, position)), Width = 120, Dock = DockStyle.Top };
                var ok = new Button { Text = "确定", DialogResult = DialogResult.OK, Width = 84 };
                var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Width = 84 };
                var btns = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft,
                    Padding = new Padding(0, 8, 10, 0) };
                btns.Controls.Add(cancel); btns.Controls.Add(ok);
                form.Controls.Add(btns); form.Controls.Add(box); form.Controls.Add(label);
                form.AcceptButton = ok; form.CancelButton = cancel;
                if (form.ShowDialog(owner) != DialogResult.OK) return false;
                position = (double)box.Value;
                return true;
            }
        }

        private static bool AskInterpolation(IWin32Window owner, Color from, Color to, ref RampInterpolation value)
        {
            using (var form = new Form())
            {
                form.Text = "设置插值方法";
                form.Font = new Font("Microsoft YaHei UI", 9F);
                form.ClientSize = new Size(330, 168);
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.StartPosition = FormStartPosition.CenterParent;
                form.MaximizeBox = false; form.MinimizeBox = false; form.ShowInTaskbar = false;
                var label = new Label
                {
                    Text = "选择该节点与上一个节点之间的插值方法：",
                    Dock = DockStyle.Top, Height = 30, Padding = new Padding(12, 8, 0, 0)
                };
                var preview = new Panel { Dock = DockStyle.Top, Height = 40, BorderStyle = BorderStyle.FixedSingle };
                var rgb = new RadioButton { Text = "RGB 插值（三通道线性过渡）", Dock = DockStyle.Top, Height = 24, Padding = new Padding(12, 0, 0, 0),
                    Checked = value == RampInterpolation.Rgb };
                var hsv = new RadioButton { Text = "HSV 插值（沿色相环过渡）", Dock = DockStyle.Top, Height = 24, Padding = new Padding(12, 0, 0, 0),
                    Checked = value == RampInterpolation.Hsv };
                Action repaint = () =>
                {
                    var bmp = new Bitmap(Math.Max(1, preview.ClientSize.Width), Math.Max(1, preview.ClientSize.Height));
                    using (var g = Graphics.FromImage(bmp))
                    {
                        for (int x = 0; x < bmp.Width; x++)
                        {
                            double t = bmp.Width <= 1 ? 0 : (double)x / (bmp.Width - 1);
                            Color c = rgb.Checked ? ColorRamp.LerpRgb(from, to, t) : ColorRamp.LerpHsv(from, to, t);
                            using (var brush = new SolidBrush(c)) g.FillRectangle(brush, x, 0, 1, bmp.Height);
                        }
                    }
                    preview.BackgroundImage = bmp;
                };
                rgb.CheckedChanged += (s, e) => repaint();
                hsv.CheckedChanged += (s, e) => repaint();
                preview.SizeChanged += (s, e) => repaint();
                var ok = new Button { Text = "确定", DialogResult = DialogResult.OK, Width = 84 };
                var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Width = 84 };
                var btns = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft,
                    Padding = new Padding(0, 8, 10, 0) };
                btns.Controls.Add(cancel); btns.Controls.Add(ok);
                form.Controls.Add(btns);
                form.Controls.Add(preview);
                form.Controls.Add(hsv);
                form.Controls.Add(rgb);
                form.Controls.Add(label);
                form.AcceptButton = ok; form.CancelButton = cancel;
                repaint();
                if (form.ShowDialog(owner) != DialogResult.OK) return false;
                value = rgb.Checked ? RampInterpolation.Rgb : RampInterpolation.Hsv;
                return true;
            }
        }

        #endregion
    }
}
