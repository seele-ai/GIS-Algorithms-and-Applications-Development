using System;
using System.Drawing;
using System.Windows.Forms;

namespace GIS.Display.UI
{
    /// <summary>
    /// 点符号编辑器：填充色与边框分别设置（借此统一“实心圆/空心圆”）。
    /// 在克隆符号上编辑，确定后才返回；取消不影响原符号。
    /// </summary>
    public sealed class MarkerSymbolEditor : Form
    {
        private readonly SimpleMarkerSymbol symbol;
        private readonly ComboBox styleBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
        private readonly NumericUpDown size = Num(0.1m, 100m, 0.1m);
        private readonly NumericUpDown outlineWidth = Num(0m, 20m, 0.1m);
        private readonly Panel preview = new Panel { Size = new Size(220, 90), BorderStyle = BorderStyle.FixedSingle, BackColor = Color.White };

        private MarkerSymbolEditor(SimpleMarkerSymbol source)
        {
            symbol = (SimpleMarkerSymbol)source.Clone();
            Text = "点符号设置";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
            MinimumSize = new Size(420, 0);
            Padding = new Padding(12);

            styleBox.Items.AddRange(new object[] { "圆形", "方形", "三角形", "十字" });
            styleBox.SelectedIndex = (int)symbol.Style;
            styleBox.SelectedIndexChanged += (s, e) => { symbol.Style = (SimpleMarkerSymbolStyleConstant)styleBox.SelectedIndex; RefreshPreview(); };

            var fillBtn = ColorButton(symbol.Color, "填充颜色", c => { symbol.Color = c; RefreshPreview(); });
            var outlineBtn = ColorButton(symbol.OutlineColor, "边框颜色", c => { symbol.OutlineColor = c; RefreshPreview(); });

            size.Value = (decimal)symbol.Size;
            size.ValueChanged += (s, e) => { symbol.Size = (double)size.Value; RefreshPreview(); };
            outlineWidth.Value = (decimal)symbol.OutlineWidth;
            outlineWidth.ValueChanged += (s, e) => { symbol.OutlineWidth = (double)outlineWidth.Value; RefreshPreview(); };

            preview.Paint += (s, e) => BasicGeometryDrawer.DrawSymbol(e.Graphics, symbol, preview.ClientRectangle);

            var grid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top };
            grid.Controls.Add(new Label { Text = "形状", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
            grid.Controls.Add(styleBox, 1, 0);
            grid.Controls.Add(new Label { Text = "尺寸（毫米）", AutoSize = true }, 0, 1);
            grid.Controls.Add(size, 1, 1);
            grid.Controls.Add(new Label { Text = "填充颜色", AutoSize = true }, 0, 2);
            grid.Controls.Add(fillBtn, 1, 2);
            grid.Controls.Add(new Label { Text = "边框颜色", AutoSize = true }, 0, 3);
            grid.Controls.Add(outlineBtn, 1, 3);
            grid.Controls.Add(new Label { Text = "边框宽度（毫米）", AutoSize = true }, 0, 4);
            grid.Controls.Add(outlineWidth, 1, 4);

            var ok = new Button { Text = "确定", DialogResult = DialogResult.OK, Width = 84 };
            var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Width = 84 };
            var btns = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 8, 0, 0) };
            btns.Controls.Add(cancel); btns.Controls.Add(ok);

            preview.Dock = DockStyle.Top;
            Controls.Add(btns);
            Controls.Add(grid);
            Controls.Add(preview);
            AcceptButton = ok;
            CancelButton = cancel;
            RefreshPreview();
        }

        private void RefreshPreview() => preview.Invalidate();

        private static NumericUpDown Num(decimal min, decimal max, decimal inc)
        {
            return new NumericUpDown { Minimum = min, Maximum = max, Increment = inc, DecimalPlaces = 2, Width = 160 };
        }

        private static Button ColorButton(Color color, string text, Action<Color> onPick)
            => SymbolUI.ColorButton(color, text, 250, onPick);

        /// <summary>返回编辑后的符号；取消返回 null。</summary>
        public static SimpleMarkerSymbol Edit(IWin32Window owner, SimpleMarkerSymbol source)
        {
            using (var f = new MarkerSymbolEditor(source))
                return f.ShowDialog(owner) == DialogResult.OK ? f.symbol : null;
        }
    }
}
