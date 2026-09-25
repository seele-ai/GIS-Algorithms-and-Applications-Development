using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace GIS.Display.UI
{
    /// <summary>注记设置：绑定字段、字体、颜色、描边与旋转角度，带实时预览。</summary>
    public sealed class LabelRendererForm : Form
    {
        private readonly LabelRenderer renderer;
        private readonly ComboBox field = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 250 };
        private readonly ComboBox fontName = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 250 };
        private readonly NumericUpDown fontSize = new NumericUpDown { Minimum = 4, Maximum = 72, Width = 110 };
        private readonly CheckBox bold = new CheckBox { Text = "加粗" };
        private readonly CheckBox italic = new CheckBox { Text = "倾斜" };
        private readonly NumericUpDown fontRatio = new NumericUpDown { Minimum = 0.5m, Maximum = 3m, Increment = 0.1m, DecimalPlaces = 1, Width = 110 };
        private readonly CheckBox useMask = new CheckBox { Text = "描边" };
        private readonly NumericUpDown maskWidth = new NumericUpDown { Minimum = 0.1m, Maximum = 5m, Increment = 0.1m, DecimalPlaces = 1, Width = 110 };
        private readonly NumericUpDown rotate = new NumericUpDown { Minimum = -360, Maximum = 360, Width = 110 };
        private readonly Panel preview = new Panel { Dock = DockStyle.Top, Height = 60, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.White };
        private Button fontColorBtn;
        private Button maskColorBtn;

        private LabelRendererForm(Layer layer)
        {
            renderer = layer.LabelRenderer == null ? new LabelRenderer { LabelFeatures = true, Field = FirstField(layer) } : layer.LabelRenderer.Clone();
            Text = "注记设置：" + layer.Name;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
            MinimumSize = new Size(500, 0);
            Padding = new Padding(12);

            foreach (Field f in layer.FeatureClass.Fields) field.Items.Add(f.Name);
            foreach (var name in FontFamily.Families) fontName.Items.Add(name.Name);

            var ts = renderer.TextSymbol ?? new TextSymbol();
            field.SelectedItem = renderer.Field;
            fontName.Text = ts.FontName;
            fontSize.Value = (decimal)ts.FontSize;
            bold.Checked = ts.Bold;
            italic.Checked = ts.Italic;
            fontRatio.Value = (decimal)ts.FontRatio;
            useMask.Checked = ts.UseMask;
            maskWidth.Value = (decimal)ts.MaskWidth;
            rotate.Value = (decimal)renderer.RotateAngle;

            fontColorBtn = ColorButton(ts.FontColor, "字体颜色", c => ts.FontColor = c);
            maskColorBtn = ColorButton(ts.MaskColor, "描边颜色", c => ts.MaskColor = c);

            preview.Paint += (s, e) => DrawPreview(e.Graphics, preview.ClientRectangle);
            field.SelectedIndexChanged += (s, e) => preview.Invalidate();
            fontName.SelectedIndexChanged += (s, e) => preview.Invalidate();
            fontName.TextChanged += (s, e) => preview.Invalidate();
            fontSize.ValueChanged += (s, e) => preview.Invalidate();
            bold.CheckedChanged += (s, e) => preview.Invalidate();
            italic.CheckedChanged += (s, e) => preview.Invalidate();
            fontRatio.ValueChanged += (s, e) => preview.Invalidate();
            useMask.CheckedChanged += (s, e) => preview.Invalidate();
            maskWidth.ValueChanged += (s, e) => preview.Invalidate();

            var grid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top };
            grid.Controls.Add(new Label { Text = "注记字段", AutoSize = true }, 0, 0); grid.Controls.Add(field, 1, 0);
            grid.Controls.Add(new Label { Text = "字体", AutoSize = true }, 0, 1); grid.Controls.Add(fontName, 1, 1);
            grid.Controls.Add(new Label { Text = "字号", AutoSize = true }, 0, 2); grid.Controls.Add(fontSize, 1, 2);
            grid.Controls.Add(new Label { Text = "字体颜色", AutoSize = true }, 0, 3); grid.Controls.Add(fontColorBtn, 1, 3);
            grid.Controls.Add(new Label { Text = "宽高比", AutoSize = true }, 0, 4); grid.Controls.Add(fontRatio, 1, 4);
            grid.Controls.Add(new Label { Text = "旋转角度", AutoSize = true }, 0, 5); grid.Controls.Add(rotate, 1, 5);
            grid.Controls.Add(bold, 0, 6); grid.Controls.Add(italic, 1, 6);
            grid.Controls.Add(useMask, 0, 7); grid.Controls.Add(new Label { Text = "描边宽度(毫米)", AutoSize = true }, 1, 7);
            grid.Controls.Add(new Label { Text = "描边颜色", AutoSize = true }, 0, 8); grid.Controls.Add(maskColorBtn, 1, 8);
            grid.Controls.Add(new Label { Text = "描边宽度", AutoSize = true }, 0, 9); grid.Controls.Add(maskWidth, 1, 9);

            var ok = new Button { Text = "确定", DialogResult = DialogResult.OK, Width = 84 };
            var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Width = 84 };
            var btns = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 8, 0, 0) };
            btns.Controls.Add(cancel); btns.Controls.Add(ok);

            Controls.Add(btns);
            Controls.Add(grid);
            Controls.Add(preview);
            AcceptButton = ok; CancelButton = cancel;
        }

        private void DrawPreview(Graphics g, Rectangle rect)
        {
            g.Clear(Color.White);
            string sample = "注记示例 Aa 123";
            FontStyle style = FontStyle.Regular;
            if (bold.Checked) style |= FontStyle.Bold;
            if (italic.Checked) style |= FontStyle.Italic;
            using (var font = new Font(fontName.Text, (float)fontSize.Value, style))
            {
                float dpm = g.DpiX / 0.0254f;
                var loc = new PointF(8, rect.Height / 2f - font.GetHeight(g) / 2f);
                if (useMask.Checked)
                {
                    using (var path = new GraphicsPath())
                    {
                        path.AddString(sample, font.FontFamily, (int)font.Style, font.SizeInPoints, loc, StringFormat.GenericDefault);
                        using (var pen = new Pen(maskColorBtn.BackColor, Math.Max(0.5f, (float)(maskWidth.Value / 1000m) * dpm)))
                            g.DrawPath(pen, path);
                        using (var brush = new SolidBrush(fontColorBtn.BackColor)) g.FillPath(brush, path);
                    }
                }
                else
                {
                    using (var brush = new SolidBrush(fontColorBtn.BackColor))
                        g.DrawString(sample, font, brush, loc);
                }
            }
        }

        private void Commit(TextSymbol ts)
        {
            renderer.Field = field.SelectedItem == null ? "" : field.SelectedItem.ToString();
            renderer.RotateAngle = (double)rotate.Value;
            ts.FontName = fontName.Text;
            ts.FontSize = (float)fontSize.Value;
            ts.Bold = bold.Checked;
            ts.Italic = italic.Checked;
            ts.FontRatio = (double)fontRatio.Value;
            ts.UseMask = useMask.Checked;
            ts.MaskWidth = (double)maskWidth.Value;
            renderer.TextSymbol = ts;
        }

        private static string FirstField(Layer layer)
        {
            return layer.FeatureClass.Fields.Count > 0 ? layer.FeatureClass.Fields[0].Name : "";
        }

        private Button ColorButton(Color color, string text, Action<Color> onPick)
        {
            var b = new Button { Text = text, Width = 250, BackColor = color, FlatStyle = FlatStyle.Flat };
            SymbolUI.ApplyReadableText(b);
            b.Click += (s, e) => {
                Color c = ColorPickerForm.Pick(b, b.BackColor);
                if (c != Color.Empty) { b.BackColor = c; SymbolUI.ApplyReadableText(b); onPick(c); preview.Invalidate(); }
            };
            return b;
        }

        /// <summary>编辑并写回图层的注记；取消返回 false。</summary>
        public static bool Edit(IWin32Window owner, Layer layer)
        {
            using (var f = new LabelRendererForm(layer))
            {
                if (f.ShowDialog(owner) != DialogResult.OK) return false;
                f.Commit(f.renderer.TextSymbol ?? new TextSymbol());
                layer.LabelRenderer = f.renderer;
                return true;
            }
        }
    }
}
