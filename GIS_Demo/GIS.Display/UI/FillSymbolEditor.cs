using System;
using System.Drawing;
using System.Windows.Forms;

namespace GIS.Display.UI
{
    /// <summary>
    /// 面符号编辑器：填充色 + 多条边界（每条边界可偏移，边界本身调用线符号编辑器）。
    /// </summary>
    public sealed class FillSymbolEditor : Form
    {
        private readonly SimpleFillSymbol symbol;
        private readonly ListBox outlineList = new ListBox { Width = 470, Height = 120 };
        private readonly Panel preview = new Panel { Size = new Size(470, 100), BorderStyle = BorderStyle.FixedSingle, BackColor = Color.White };

        private FillSymbolEditor(SimpleFillSymbol source)
        {
            symbol = (SimpleFillSymbol)source.Clone();
            Text = "面符号设置";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
            MinimumSize = new Size(520, 0);
            Padding = new Padding(12);

            var fillBtn = ColorButton(symbol.Color, "填充颜色", c => { symbol.Color = c; RefreshPreview(); });

            var btns = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top };
            AddBtn(btns, "添加边界", AddOutline);
            AddBtn(btns, "编辑边界", EditOutline);
            AddBtn(btns, "删除", RemoveOutline);
            AddBtn(btns, "上移", () => MoveOutline(-1));
            AddBtn(btns, "下移", () => MoveOutline(1));
            outlineList.DoubleClick += (s, e) => EditOutline();

            preview.Paint += (s, e) => BasicGeometryDrawer.DrawSymbol(e.Graphics, symbol, preview.ClientRectangle);

            var fillRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(0, 4, 0, 4) };
            fillRow.Controls.Add(new Label { Text = "填充颜色", AutoSize = true, Padding = new Padding(0, 4, 8, 0) });
            fillRow.Controls.Add(fillBtn);

            var ok = new Button { Text = "确定", DialogResult = DialogResult.OK, Width = 84 };
            var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Width = 84 };
            var okRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 8, 0, 0) };
            okRow.Controls.Add(cancel); okRow.Controls.Add(ok);

            preview.Dock = DockStyle.Top;
            outlineList.Dock = DockStyle.Top;
            Controls.Add(okRow);
            Controls.Add(btns);
            Controls.Add(outlineList);
            Controls.Add(fillRow);
            Controls.Add(preview);
            AcceptButton = ok; CancelButton = cancel;
            RebuildList();
            RefreshPreview();
        }

        private void AddOutline()
        {
            // 默认偏移量 = 当前各边界的（偏移量 + 线宽）最大值，便于连续叠加出 0 → 2 → 4 这样的多层边界
            var line = new SimpleLineSymbol { Color = Color.DarkGray, Size = 0.35 };
            var o = FillOutlineEditor.Edit(this, new FillOutline(NextOffsetDefault(), line));
            if (o != null) { symbol.Outlines.Add(o); RebuildList(); RefreshPreview(); }
        }

        /// <summary>新边界的默认偏移量：当前各边界（偏移量 + 线宽）的最大值。</summary>
        private double NextOffsetDefault()
        {
            if (symbol.Outlines.Count == 0) return 0;
            double positive = double.MinValue, negative = double.MaxValue;
            bool hasPositive = false, hasNegative = false;
            foreach (FillOutline o in symbol.Outlines)
            {
                if (o == null) continue;
                double width = o.Outline == null ? 0 : Math.Max(0, o.Outline.Size);
                if (o.Offset >= 0) { positive = Math.Max(positive, o.Offset + width); hasPositive = true; }
                else { negative = Math.Min(negative, o.Offset - width); hasNegative = true; }
            }
            if (hasPositive) return Math.Round(positive, 3);
            if (hasNegative) return Math.Round(negative, 3);
            return 0;
        }
        private void EditOutline()
        {
            int i = outlineList.SelectedIndex;
            if (i < 0) return;
            var o = FillOutlineEditor.Edit(this, symbol.Outlines[i]);
            if (o != null) { symbol.Outlines[i] = o; RebuildList(); RefreshPreview(); }
        }
        private void RemoveOutline()
        {
            int i = outlineList.SelectedIndex;
            if (i < 0 || symbol.Outlines.Count <= 1) return;
            symbol.Outlines.RemoveAt(i);
            RebuildList(); RefreshPreview();
        }
        private void MoveOutline(int delta)
        {
            int i = outlineList.SelectedIndex, j = i + delta;
            if (i < 0 || j < 0 || j >= symbol.Outlines.Count) return;
            var t = symbol.Outlines[i]; symbol.Outlines[i] = symbol.Outlines[j]; symbol.Outlines[j] = t;
            RebuildList(); outlineList.SelectedIndex = j; RefreshPreview();
        }
        private void RebuildList()
        {
            outlineList.Items.Clear();
            for (int i = 0; i < symbol.Outlines.Count; i++)
            {
                var o = symbol.Outlines[i];
                outlineList.Items.Add(string.Format("[{0}] 偏移{1:F1}mm 线宽{2:F1}mm", i + 1, o.Offset, o.Outline == null ? 0 : o.Outline.Size));
            }
        }

        private void RefreshPreview() => preview.Invalidate();

        private static void AddBtn(FlowLayoutPanel p, string text, Action a)
        {
            var b = new Button { Text = text, AutoSize = true };
            b.Click += (s, e) => a();
            p.Controls.Add(b);
        }

        private static Button ColorButton(Color color, string text, Action<Color> onPick)
            => SymbolUI.ColorButton(color, text, 250, onPick);

        public static SimpleFillSymbol Edit(IWin32Window owner, SimpleFillSymbol source)
        {
            using (var f = new FillSymbolEditor(source))
                return f.ShowDialog(owner) == DialogResult.OK ? f.symbol : null;
        }
    }

    /// <summary>面符号单条边界的编辑器：偏移量 + 线符号（调用线符号编辑器）。</summary>
    public sealed class FillOutlineEditor : Form
    {
        private readonly FillOutline outline;
        private readonly NumericUpDown offset = new NumericUpDown { Minimum = -50, Maximum = 50, Increment = 0.1m, DecimalPlaces = 2, Width = 160 };
        private readonly Button lineBtn;
        private readonly Panel preview = new Panel { Size = new Size(420, 80), BorderStyle = BorderStyle.FixedSingle, BackColor = Color.White };

        private FillOutlineEditor(FillOutline source)
        {
            outline = source.Clone();
            Text = "设置边界";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
            MinimumSize = new Size(440, 0);
            Padding = new Padding(12);

            offset.Value = (decimal)outline.Offset;
            offset.ValueChanged += (s, e) => { outline.Offset = (double)offset.Value; RefreshPreview(); };
            lineBtn = new Button { Text = "编辑线符号…", Width = 250 };
            lineBtn.Click += (s, e) => {
                var line = LineSymbolEditor.Edit(this, outline.Outline);
                if (line != null) { outline.Outline = line; RefreshPreview(); }
            };
            preview.Paint += (s, e) => {
                var temp = new SimpleFillSymbol { Color = Color.Transparent };
                temp.Outlines.Clear();
                temp.Outlines.Add(outline.Clone());
                BasicGeometryDrawer.DrawSymbol(e.Graphics, temp, preview.ClientRectangle);
            };

            var grid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top };
            grid.Controls.Add(new Label { Text = "向外偏移（毫米）", AutoSize = true }, 0, 0);
            grid.Controls.Add(offset, 1, 0);
            grid.Controls.Add(new Label { Text = "边界符号", AutoSize = true }, 0, 1);
            grid.Controls.Add(lineBtn, 1, 1);

            var ok = new Button { Text = "确定", DialogResult = DialogResult.OK, Width = 84 };
            var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Width = 84 };
            var btns = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 8, 0, 0) };
            btns.Controls.Add(cancel); btns.Controls.Add(ok);

            preview.Dock = DockStyle.Top;
            Controls.Add(btns);
            Controls.Add(grid);
            Controls.Add(preview);
            AcceptButton = ok; CancelButton = cancel;
            RefreshPreview();
        }

        private void RefreshPreview() => preview.Invalidate();

        public static FillOutline Edit(IWin32Window owner, FillOutline source)
        {
            using (var f = new FillOutlineEditor(source))
                return f.ShowDialog(owner) == DialogResult.OK ? f.outline : null;
        }
    }
}
