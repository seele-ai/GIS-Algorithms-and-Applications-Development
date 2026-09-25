using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace GIS.Display.UI
{
    /// <summary>
    /// 线符号编辑器：简单线型（实线/虚线/点线）+ 自定义虚线（多段线交替、边框、端点垂线）。
    /// 通过自定义虚线可拼出铁路线、国界线等符号。
    /// </summary>
    public sealed class LineSymbolEditor : Form
    {
        private readonly SimpleLineSymbol symbol;
        private readonly ComboBox styleBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
        private readonly NumericUpDown size = Num(0.05m, 20m, 0.05m);
        private readonly CheckBox customCheck = new CheckBox { Text = "使用自定义虚线" , Width = PanelWidth };
        /// <summary>自定义虚线列表的宽度：每段虚线连同其“偏移/延长/弧线”参数都要能一行显示完。</summary>
        public const int PanelWidth = 700;
        private readonly ListBox dashList = new ListBox { Width = PanelWidth, Height = 132, HorizontalScrollbar = true };
        private readonly ListBox offsetList = new ListBox { Width = PanelWidth, Height = 90, HorizontalScrollbar = true };
        private readonly Panel preview = new Panel { Size = new Size(PanelWidth, 80), BorderStyle = BorderStyle.FixedSingle, BackColor = Color.White };
        private readonly List<LineDashElement> savedDash = new List<LineDashElement>();   // 取消勾选时暂存的虚线图案

        private LineSymbolEditor(SimpleLineSymbol source)
        {
            symbol = (SimpleLineSymbol)source.Clone();
            Text = "线符号设置";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
            MinimumSize = new Size(PanelWidth + 60, 0);
            Padding = new Padding(12);

            styleBox.Items.AddRange(new object[] { "实线", "虚线", "点线" });
            styleBox.SelectedIndex = (int)symbol.Style;
            styleBox.SelectedIndexChanged += (s, e) => { symbol.Style = (SimpleLineSymbolStyleConstant)styleBox.SelectedIndex; RefreshPreview(); };

            var colorBtn = ColorButton(symbol.Color, "颜色", c => { symbol.Color = c; RefreshPreview(); });
            size.Value = (decimal)symbol.Size;
            size.ValueChanged += (s, e) => { symbol.Size = (double)size.Value; RefreshPreview(); };

            // 自定义虚线面板：固定高度，内部为列表 + 按钮行
            var dashPanel = new Panel { Dock = DockStyle.Top, Height = 170, Visible = customCheck.Checked };
            dashList.Location = new System.Drawing.Point(0, 0);
            dashList.Size = new Size(PanelWidth, 132);
            var dashBtns = new FlowLayoutPanel { Location = new System.Drawing.Point(0, 138), Size = new Size(PanelWidth, 30), FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            AddBtn(dashBtns, "增加线", AddDash);
            AddBtn(dashBtns, "删除线", RemoveDash);
            AddBtn(dashBtns, "编辑", EditDash);
            AddBtn(dashBtns, "上移", () => MoveDash(-1));
            AddBtn(dashBtns, "下移", () => MoveDash(1));
            dashPanel.Controls.Add(dashList);
            dashPanel.Controls.Add(dashBtns);
            dashList.DoubleClick += (s, e) => EditDash();

            customCheck.CheckedChanged += (s, e) => {
                if (customCheck.Checked)
                {
                    // 重新勾选：优先恢复取消勾选前的图案，否则新建一段默认虚线
                    if (symbol.DashElements.Count == 0)
                    {
                        if (savedDash.Count > 0)
                            foreach (LineDashElement element in savedDash) symbol.DashElements.Add(element.Clone());
                        else
                            symbol.DashElements.Add(new LineDashElement { Length = 5, Color = symbol.Color, Width = Math.Max(symbol.Size, 0.1) });
                    }
                }
                else
                {
                    // 取消勾选时必须清空虚线元素：符号是否按自定义虚线绘制取决于该集合是否为空，
                    // 否则取消勾选后仍然显示为自定义虚线，无法切回普通线型。
                    savedDash.Clear();
                    foreach (LineDashElement element in symbol.DashElements) savedDash.Add(element.Clone());
                    symbol.DashElements.Clear();
                }
                dashPanel.Visible = customCheck.Checked;
                RebuildDashList();
                RefreshPreview();
            };
            customCheck.Checked = symbol.HasCustomDash;   // 触发上面的事件，正确打开/关闭自定义虚线面板

            // 多线符号（偏移线）面板：一条线画出多条平行线，可用于国界线
            var offsetPanel = new Panel { Dock = DockStyle.Top, Height = 154 };
            var offsetHint = new Label
            {
                Text = "偏移线（毫米，正左负右）：设置了偏移线后按各条线绘制，本条线符号仅作为模板",
                Location = new System.Drawing.Point(0, 0),
                Size = new Size(PanelWidth, 18),
                ForeColor = Color.FromArgb(90, 90, 90)
            };
            offsetList.Location = new System.Drawing.Point(0, 20);
            offsetList.Size = new Size(PanelWidth, 90);
            var offsetBtns = new FlowLayoutPanel { Location = new System.Drawing.Point(0, 116), Size = new Size(PanelWidth, 30), FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            AddBtn(offsetBtns, "增加偏移线", AddOffset);
            AddBtn(offsetBtns, "删除偏移线", RemoveOffset);
            AddBtn(offsetBtns, "编辑", EditOffset);
            AddBtn(offsetBtns, "上移", () => MoveOffset(-1));
            AddBtn(offsetBtns, "下移", () => MoveOffset(1));
            offsetPanel.Controls.Add(offsetHint);
            offsetPanel.Controls.Add(offsetList);
            offsetPanel.Controls.Add(offsetBtns);
            offsetList.DoubleClick += (s, e) => EditOffset();

            preview.Paint += (s, e) => BasicGeometryDrawer.DrawSymbol(e.Graphics, symbol, preview.ClientRectangle);

            var grid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top };
            grid.Controls.Add(new Label { Text = "线型", AutoSize = true }, 0, 0);
            grid.Controls.Add(styleBox, 1, 0);
            grid.Controls.Add(new Label { Text = "颜色", AutoSize = true }, 0, 1);
            grid.Controls.Add(colorBtn, 1, 1);
            grid.Controls.Add(new Label { Text = "线宽（毫米）", AutoSize = true }, 0, 2);
            grid.Controls.Add(size, 1, 2);
            grid.Controls.Add(customCheck, 0, 3);
            grid.SetColumnSpan(customCheck, 2);

            var ok = new Button { Text = "确定", DialogResult = DialogResult.OK, Width = 84 };
            var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Width = 84 };
            var btns = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 8, 0, 0) };
            btns.Controls.Add(cancel); btns.Controls.Add(ok);

            preview.Dock = DockStyle.Top;
            Controls.Add(btns);
            Controls.Add(offsetPanel);
            Controls.Add(dashPanel);
            Controls.Add(grid);
            Controls.Add(preview);
            AcceptButton = ok; CancelButton = cancel;
            RebuildDashList();
            RebuildOffsetList();
            RefreshPreview();
        }

        private void AddOffset()
        {
            // 开始设置偏移线时，默认先添加一条“当前线符号的复制”作为新的偏移线（第一条偏移量为 0），
            // 再打开编辑器让用户微调；即使取消编辑，这条默认偏移线也已经生效。
            var line = (SimpleLineSymbol)symbol.Clone();
            line.Offsets.Clear();
            var added = new LineOffset(NextOffsetDefault(), line);
            symbol.Offsets.Add(added);
            RebuildOffsetList();
            RefreshPreview();
            offsetList.SelectedIndex = symbol.Offsets.Count - 1;

            var edited = LineOffsetEditor.Edit(this, added);
            if (edited != null) symbol.Offsets[symbol.Offsets.Count - 1] = edited;
            RebuildOffsetList();
            RefreshPreview();
        }

        /// <summary>
        /// 新偏移线的默认偏移量：取“当前各偏移线的（偏移量 + 线宽）”的最大值，
        /// 于是连续添加时会自然得到 0 → 2 → 4（对应 2mm 线宽）这样的平行线序列；
        /// 若已有偏移线都在负方向，则继续向负方向排开。没有偏移线时返回 0。
        /// </summary>
        private double NextOffsetDefault()
        {
            if (symbol.Offsets.Count == 0) return 0;
            double positive = double.MinValue, negative = double.MaxValue;
            bool hasPositive = false, hasNegative = false;
            foreach (LineOffset o in symbol.Offsets)
            {
                if (o == null) continue;
                double width = o.Line == null ? 0 : Math.Max(0, o.Line.Size);
                if (o.Offset >= 0) { positive = Math.Max(positive, o.Offset + width); hasPositive = true; }
                else { negative = Math.Min(negative, o.Offset - width); hasNegative = true; }
            }
            if (hasPositive) return Math.Round(positive, 3);
            if (hasNegative) return Math.Round(negative, 3);
            return 0;
        }
        private void EditOffset()
        {
            int i = offsetList.SelectedIndex;
            if (i < 0) return;
            var o = LineOffsetEditor.Edit(this, symbol.Offsets[i]);
            if (o != null) { symbol.Offsets[i] = o; RebuildOffsetList(); RefreshPreview(); }
        }
        private void RemoveOffset()
        {
            int i = offsetList.SelectedIndex;
            if (i < 0) return;
            symbol.Offsets.RemoveAt(i);
            RebuildOffsetList(); RefreshPreview();
        }
        private void MoveOffset(int delta)
        {
            int i = offsetList.SelectedIndex, j = i + delta;
            if (i < 0 || j < 0 || j >= symbol.Offsets.Count) return;
            var t = symbol.Offsets[i]; symbol.Offsets[i] = symbol.Offsets[j]; symbol.Offsets[j] = t;
            RebuildOffsetList(); offsetList.SelectedIndex = j; RefreshPreview();
        }
        private void RebuildOffsetList()
        {
            offsetList.Items.Clear();
            for (int i = 0; i < symbol.Offsets.Count; i++)
            {
                var o = symbol.Offsets[i];
                string style = o.Line != null && o.Line.HasCustomDash ? "自定义虚线" : "简单线";
                double w = o.Line == null ? 0 : o.Line.Size;
                offsetList.Items.Add(string.Format("[{0}] 偏移{1:F1}mm  {2} 宽{3:F1}mm", i + 1, o.Offset, style, w));
            }
        }

        private void AddDash()
        {
            // 新线段的默认值取自当前线符号的颜色与线宽
            var e = DashElementEditor.Edit(this, new LineDashElement { Length = 5, Color = symbol.Color, Width = Math.Max(symbol.Size, 0.1) });
            if (e != null) { symbol.DashElements.Add(e); RebuildDashList(); RefreshPreview(); }
        }
        private void EditDash()
        {
            int i = dashList.SelectedIndex;
            if (i < 0) return;
            var e = DashElementEditor.Edit(this, symbol.DashElements[i]);
            if (e != null) { symbol.DashElements[i] = e; RebuildDashList(); RefreshPreview(); }
        }
        private void RemoveDash()
        {
            int i = dashList.SelectedIndex;
            if (i < 0) return;
            symbol.DashElements.RemoveAt(i);
            RebuildDashList(); RefreshPreview();
        }
        private void MoveDash(int delta)
        {
            int i = dashList.SelectedIndex, j = i + delta;
            if (i < 0 || j < 0 || j >= symbol.DashElements.Count) return;
            var t = symbol.DashElements[i]; symbol.DashElements[i] = symbol.DashElements[j]; symbol.DashElements[j] = t;
            RebuildDashList(); dashList.SelectedIndex = j; RefreshPreview();
        }
        private void RebuildDashList()
        {
            dashList.Items.Clear();
            for (int i = 0; i < symbol.DashElements.Count; i++)
                dashList.Items.Add(DescribeDashElement(i, symbol.DashElements[i]));
        }

        /// <summary>
        /// 把一段虚线图案写成面板列表里的一行文字：基本的长/宽/边框/端点竖线之外，
        /// 该段启用过的“偏移 / 延长 / 弧线”参数也会显示出来（未启用就不显示），
        /// 文字保持紧凑，配合 <see cref="PanelWidth"/> 与横向滚动条不会超出面板宽度。
        /// </summary>
        public static string DescribeDashElement(int index, LineDashElement element)
        {
            var text = new System.Text.StringBuilder();
            text.AppendFormat("[{0}] 长{1:F1}mm 宽{2:F1}mm 边框{3:F1}mm 端点竖线{4:F1}mm",
                index + 1, element.Length, element.Width, element.OutlineWidth, element.TickLength);
            if (element.OffsetEnabled)
                text.AppendFormat("  偏移{0:+0.0;-0.0;0.0}mm", element.Offset);
            if (element.ExtendEnabled)
                text.AppendFormat("  延长{0:F1}/{1:F1}mm", element.ExtendLeft, element.ExtendRight);
            if (element.ArcEnabled)
                text.AppendFormat("  弧线{0:F1}mm×{1:F0}", element.ArcAmplitude, element.ArcHalfPeriods);
            return text.ToString();
        }

        private void RefreshPreview() => preview.Invalidate();

        private static NumericUpDown Num(decimal min, decimal max, decimal inc)
            => new NumericUpDown { Minimum = min, Maximum = max, Increment = inc, DecimalPlaces = 2, Width = 160 };

        private static void AddBtn(FlowLayoutPanel p, string text, Action a)
        {
            var b = new Button { Text = text, AutoSize = true };
            b.Click += (s, e) => a();
            p.Controls.Add(b);
        }

        private static Button ColorButton(Color color, string text, Action<Color> onPick)
            => SymbolUI.ColorButton(color, text, 330, onPick);

        /// <summary>创建线符号编辑器窗口（对话框与自动检查共用，避免两处布局不一致）。</summary>
        public static LineSymbolEditor Create(SimpleLineSymbol source) => new LineSymbolEditor(source);

        public static SimpleLineSymbol Edit(IWin32Window owner, SimpleLineSymbol source)
        {
            using (var f = Create(source))
                return f.ShowDialog(owner) == DialogResult.OK ? f.symbol : null;
        }
    }

    /// <summary>单个虚线段的编辑器。</summary>
    public sealed class DashElementEditor : Form
    {
        /// <summary>窗口最小宽度：要容得下最长的标签 + 参数输入框（“设置线段”曾因新增参数而放不下）。</summary>
        public const int EditorWidth = 660;
        /// <summary>参数输入框宽度（七参数之外的数值编辑框）。</summary>
        private const int ParameterBoxWidth = 200;
        private readonly LineDashElement element;
        private readonly NumericUpDown length = Num(0.1m, 100m, 0.1m);
        private readonly NumericUpDown width = Num(0.05m, 20m, 0.05m);
        private readonly NumericUpDown outlineWidth = Num(0m, 20m, 0.05m);
        private readonly NumericUpDown tickLength = Num(0m, 20m, 0.1m);
        private readonly CheckBox offsetCheck = new CheckBox { Text = "偏移（本段整体左右平移）", AutoSize = true };
        private readonly NumericUpDown offset = Num(-50m, 50m, 0.1m);
        private readonly CheckBox extendCheck = new CheckBox { Text = "线的延长（本段两端各自向外延长）", AutoSize = true };
        private readonly NumericUpDown extendLeft = Num(0m, 100m, 0.1m);
        private readonly NumericUpDown extendRight = Num(0m, 100m, 0.1m);
        private readonly CheckBox arcCheck = new CheckBox { Text = "设置为弧线（用椭圆绘制本段）", AutoSize = true };
        private readonly NumericUpDown arcAmplitude = Num(0m, 50m, 0.1m);
        private readonly NumericUpDown arcHalfPeriods = Num(1m, 50m, 1m);

        // 未勾选的特性其参数控件置灰，避免误改
        private void EnableFeatureControls()
        {
            offset.Enabled = offsetCheck.Checked;
            extendLeft.Enabled = extendRight.Enabled = extendCheck.Checked;
            arcAmplitude.Enabled = arcHalfPeriods.Enabled = arcCheck.Checked;
        }

        private DashElementEditor(LineDashElement source)
        {
            element = source.Clone();
            Text = "设置线段";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
            // 增加了“偏移/延长/弧线”三组参数后，参数标签变长，窗口必须留足宽度
            MinimumSize = new Size(EditorWidth, 0);
            Padding = new Padding(12);

            length.Value = (decimal)element.Length; length.ValueChanged += (s, e) => element.Length = (double)length.Value;
            width.Value = (decimal)element.Width; width.ValueChanged += (s, e) => element.Width = (double)width.Value;
            outlineWidth.Value = (decimal)element.OutlineWidth; outlineWidth.ValueChanged += (s, e) => element.OutlineWidth = (double)outlineWidth.Value;
            tickLength.Value = (decimal)element.TickLength; tickLength.ValueChanged += (s, e) => element.TickLength = (double)tickLength.Value;

            var colorBtn = ColorButton(element.Color, "线段颜色", c => element.Color = c);
            var outlineBtn = ColorButton(element.OutlineColor, "边框颜色", c => element.OutlineColor = c);
            var tickBtn = ColorButton(element.TickColor.A == 0 ? element.Color : element.TickColor, "端点竖线颜色", c => element.TickColor = c);

            var grid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ParameterBoxWidth + 40));
            grid.Controls.Add(new Label { Text = "线段长度（毫米）", AutoSize = true }, 0, 0); grid.Controls.Add(length, 1, 0);
            grid.Controls.Add(new Label { Text = "线段颜色", AutoSize = true }, 0, 1); grid.Controls.Add(colorBtn, 1, 1);
            grid.Controls.Add(new Label { Text = "线段宽度（毫米）", AutoSize = true }, 0, 2); grid.Controls.Add(width, 1, 2);
            grid.Controls.Add(new Label { Text = "边框颜色", AutoSize = true }, 0, 3); grid.Controls.Add(outlineBtn, 1, 3);
            grid.Controls.Add(new Label { Text = "边框宽度（毫米，0=无）", AutoSize = true }, 0, 4); grid.Controls.Add(outlineWidth, 1, 4);
            grid.Controls.Add(new Label { Text = "端点垂线长度（毫米，0=无）", AutoSize = true }, 0, 5); grid.Controls.Add(tickLength, 1, 5);
            grid.Controls.Add(new Label { Text = "端点垂线颜色", AutoSize = true }, 0, 6); grid.Controls.Add(tickBtn, 1, 6);

            // 三类可选特性：勾选后才可进一步设置（与“自定义虚线”的交互方式一致）
            grid.Controls.Add(offsetCheck, 0, 7);
            grid.SetColumnSpan(offsetCheck, 2);
            grid.Controls.Add(new Label { Text = "        偏移量（毫米，正=左 负=右）", AutoSize = true }, 0, 8);
            grid.Controls.Add(offset, 1, 8);
            grid.Controls.Add(extendCheck, 0, 9);
            grid.SetColumnSpan(extendCheck, 2);
            grid.Controls.Add(new Label { Text = "        向左延长（毫米）", AutoSize = true }, 0, 10);
            grid.Controls.Add(extendLeft, 1, 10);
            grid.Controls.Add(new Label { Text = "        向右延长（毫米）", AutoSize = true }, 0, 11);
            grid.Controls.Add(extendRight, 1, 11);
            grid.Controls.Add(arcCheck, 0, 12);
            grid.SetColumnSpan(arcCheck, 2);
            grid.Controls.Add(new Label { Text = "        振幅（毫米）", AutoSize = true }, 0, 13);
            grid.Controls.Add(arcAmplitude, 1, 13);
            grid.Controls.Add(new Label { Text = "        半周期数", AutoSize = true }, 0, 14);
            grid.Controls.Add(arcHalfPeriods, 1, 14);

            offsetCheck.Checked = element.OffsetEnabled;
            extendCheck.Checked = element.ExtendEnabled;
            arcCheck.Checked = element.ArcEnabled;
            offsetCheck.CheckedChanged += (s, e) => { element.OffsetEnabled = offsetCheck.Checked; EnableFeatureControls(); };
            extendCheck.CheckedChanged += (s, e) => { element.ExtendEnabled = extendCheck.Checked; EnableFeatureControls(); };
            arcCheck.CheckedChanged += (s, e) => { element.ArcEnabled = arcCheck.Checked; EnableFeatureControls(); };
            SetValue(offset, element.Offset);
            SetValue(extendLeft, element.ExtendLeft);
            SetValue(extendRight, element.ExtendRight);
            SetValue(arcAmplitude, element.ArcAmplitude);
            SetValue(arcHalfPeriods, element.ArcHalfPeriods);
            EnableFeatureControls();
            offset.ValueChanged += (s, e) => element.Offset = (double)offset.Value;
            extendLeft.ValueChanged += (s, e) => element.ExtendLeft = (double)extendLeft.Value;
            extendRight.ValueChanged += (s, e) => element.ExtendRight = (double)extendRight.Value;
            arcAmplitude.ValueChanged += (s, e) => element.ArcAmplitude = (double)arcAmplitude.Value;
            arcHalfPeriods.ValueChanged += (s, e) => element.ArcHalfPeriods = (double)arcHalfPeriods.Value;

            var ok = new Button { Text = "确定", DialogResult = DialogResult.OK, Width = 84 };
            var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Width = 84 };
            var btns = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 8, 0, 0) };
            btns.Controls.Add(cancel); btns.Controls.Add(ok);

            Controls.Add(btns);
            Controls.Add(grid);
            AcceptButton = ok; CancelButton = cancel;
        }

        private static NumericUpDown Num(decimal min, decimal max, decimal inc)
            => new NumericUpDown { Minimum = min, Maximum = max, Increment = inc, DecimalPlaces = 2, Width = ParameterBoxWidth };

        private static void SetValue(NumericUpDown box, double value)
        {
            decimal v = (decimal)Math.Max((double)box.Minimum, Math.Min((double)box.Maximum, value));
            box.Value = v;
        }

        private static Button ColorButton(Color color, string text, Action<Color> onPick)
            => SymbolUI.ColorButton(color, text, 330, onPick);

        /// <summary>创建“设置线段”窗口（编辑对话框与自动检查共用同一套布局）。</summary>
        public static DashElementEditor Create(LineDashElement source) => new DashElementEditor(source);

        public static LineDashElement Edit(IWin32Window owner, LineDashElement source)
        {
            using (var f = Create(source))
                return f.ShowDialog(owner) == DialogResult.OK ? f.element : null;
        }
    }
    /// <summary>线符号的一条偏移线编辑器：偏移量 + 线符号（调用线符号编辑器）。</summary>
    public sealed class LineOffsetEditor : Form
    {
        private readonly LineOffset offset;
        private readonly NumericUpDown offsetValue = new NumericUpDown { Minimum = -50, Maximum = 50, Increment = 0.5m, DecimalPlaces = 1, Width = 200 };
        private readonly Button lineBtn;
        private readonly Panel preview = new Panel { Dock = DockStyle.Top, Height = 70, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.White };

        private LineOffsetEditor(LineOffset source)
        {
            offset = source.Clone();
            if (offset.Line == null) offset.Line = new SimpleLineSymbol();
            offset.Line.Offsets.Clear();   // 偏移线自身的线符号不再嵌套偏移线
            Text = "设置偏移线";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
            MinimumSize = new Size(500, 0);
            Padding = new Padding(12);

            offsetValue.Value = (decimal)offset.Offset;
            offsetValue.ValueChanged += (s, e) => { offset.Offset = (double)offsetValue.Value; RefreshPreview(); };
            lineBtn = new Button { Text = "编辑线符号…", Width = 330 };
            lineBtn.Click += (s, e) => {
                var line = LineSymbolEditor.Edit(this, offset.Line);
                if (line != null) { line.Offsets.Clear(); offset.Line = line; RefreshPreview(); }
            };
            preview.Paint += (s, e) => DrawPreview(e.Graphics);

            var grid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top };
            grid.Controls.Add(new Label { Text = "垂直偏移（毫米）", AutoSize = true }, 0, 0);
            grid.Controls.Add(offsetValue, 1, 0);
            grid.Controls.Add(new Label { Text = "线符号", AutoSize = true }, 0, 1);
            grid.Controls.Add(lineBtn, 1, 1);

            var ok = new Button { Text = "确定", DialogResult = DialogResult.OK, Width = 84 };
            var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Width = 84 };
            var btns = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 8, 0, 0) };
            btns.Controls.Add(cancel); btns.Controls.Add(ok);

            Controls.Add(btns);
            Controls.Add(grid);
            Controls.Add(preview);
            AcceptButton = ok; CancelButton = cancel;
            RefreshPreview();
        }

        // 预览：中间一条基准线，再加上本偏移线。
        // 直接用与地图相同的绘制函数，保证预览里的符号（线型/虚线/线宽/颜色）与实际偏移线完全一致。
        private void DrawPreview(Graphics g)
        {
            g.Clear(Color.White);
            float cy = preview.ClientRectangle.Height / 2f;
            using (var basePen = new Pen(Color.Gainsboro, 1) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot })
                g.DrawLine(basePen, 6, cy, preview.ClientRectangle.Width - 6, cy);
            if (offset.Line == null) return;
            float dpm = g.DpiX / 0.0254f;
            float dy = (float)(offset.Offset / 1000.0 * dpm);
            var state = g.Save();
            try
            {
                g.TranslateTransform(0, -dy);
                BasicGeometryDrawer.DrawSymbol(g, offset.Line, preview.ClientRectangle);
            }
            finally { g.Restore(state); }
        }

        private void RefreshPreview() => preview.Invalidate();

        public static LineOffset Edit(IWin32Window owner, LineOffset source)
        {
            using (var f = new LineOffsetEditor(source))
                return f.ShowDialog(owner) == DialogResult.OK ? f.offset : null;
        }
    }
}
