using System;
using System.Drawing;
using System.Windows.Forms;

namespace GIS.Display.UI
{
    /// <summary>
    /// 颜色选择对话框：预设色板、#RRGGBB / #RRGGBBAA 十六进制输入、透明度（Alpha）。
    /// 静态方法 Pick 返回 Color；用户取消时返回 Color.Empty。
    /// </summary>
    public sealed class ColorPickerForm : Form
    {
        private readonly TextBox hexBox = new TextBox { Width = 240 };
        private readonly TrackBar alphaBar = new TrackBar { Minimum = 0, Maximum = 255, TickFrequency = 16, Width = 420 };
        private readonly Label alphaLabel = new Label { AutoSize = true };
        private readonly Panel preview = new Panel { Size = new Size(110, 28), BorderStyle = BorderStyle.FixedSingle };
        private Color result = Color.Black;
        private bool updating;

        private static readonly Color[] Palette =
        {
            Color.Black, Color.White, Color.Gray, Color.DarkGray, Color.LightGray,
            Color.Red, Color.OrangeRed, Color.Orange, Color.Gold, Color.Yellow,
            Color.LimeGreen, Color.Green, Color.DarkGreen, Color.Cyan, Color.DodgerBlue,
            Color.Blue, Color.DarkBlue, Color.Purple, Color.Magenta, Color.DeepPink,
            Color.SaddleBrown, Color.Chocolate, Color.Tan, Color.MistyRose, Color.Transparent
        };

        private ColorPickerForm(string title, Color initial)
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            MinimumSize = new Size(440, 0);
            Padding = new Padding(12);

            var table = new TableLayoutPanel { ColumnCount = 5, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top };
            int idx = 0;
            foreach (Color c in Palette)
            {
                var swatch = new Panel { Width = 30, Height = 30, Margin = new Padding(2),
                    BackColor = c, BorderStyle = BorderStyle.FixedSingle, Cursor = Cursors.Hand, Tag = c };
                swatch.Click += (s, e) => SetColor((Color)((Control)s).Tag);
                table.Controls.Add(swatch, idx % 5, idx / 5);
                idx++;
            }

            var hexRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(0, 6, 0, 0) };
            hexRow.Controls.Add(new Label { Text = "#", AutoSize = true, Padding = new Padding(0, 6, 2, 0) });
            hexRow.Controls.Add(hexBox);
            hexRow.Controls.Add(new Label { Text = "  RRGGBB 或 RRGGBBAA", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });

            alphaBar.Dock = DockStyle.Top;
            alphaLabel.Dock = DockStyle.Top;

            var previewRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(0, 6, 0, 0) };
            previewRow.Controls.Add(new Label { Text = "预览", AutoSize = true, Padding = new Padding(0, 6, 8, 0) });
            previewRow.Controls.Add(preview);

            var ok = new Button { Text = "确定", DialogResult = DialogResult.OK, Width = 84 };
            var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Width = 84 };
            var btnRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 10, 0, 0) };
            btnRow.Controls.Add(cancel);
            btnRow.Controls.Add(ok);

            Controls.Add(btnRow);
            Controls.Add(previewRow);
            Controls.Add(alphaLabel);
            Controls.Add(alphaBar);
            Controls.Add(hexRow);
            Controls.Add(table);

            AcceptButton = ok;
            CancelButton = cancel;

            hexBox.TextChanged += (s, e) => OnHexChanged();
            alphaBar.ValueChanged += (s, e) => OnAlphaChanged();
            alphaBar.MouseUp += (s, e) => FinalizeAlpha();
            alphaBar.KeyUp += (s, e) => FinalizeAlpha();
            SetColor(initial.A == 0 && initial == Color.Empty ? Color.Black : initial);
        }

        private void SetColor(Color c)
        {
            updating = true;
            result = Color.FromArgb(c.A, c.R, c.G, c.B);
            alphaBar.Value = c.A;
            alphaLabel.Text = "不透明度 " + c.A;
            preview.BackColor = result;
            hexBox.Text = "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2") + c.A.ToString("X2");
            updating = false;
        }

        private void OnHexChanged()
        {
            if (updating) return;
            string t = hexBox.Text.Trim().TrimStart('#');
            if (t.Length != 6 && t.Length != 8) return;
            try
            {
                int rgb = Convert.ToInt32(t.Substring(0, 6), 16);
                int r = (rgb >> 16) & 255, g = (rgb >> 8) & 255, b = rgb & 255;
                int a = t.Length == 8 ? Convert.ToInt32(t.Substring(6, 2), 16) : alphaBar.Value;
                SetColor(Color.FromArgb(a, r, g, b));
            }
            catch { }
        }

        private void OnAlphaChanged()
        {
            if (updating) return;
            // 拖动过程中仅更新数值与标签，预览在松手后统一刷新，避免闪烁。
            result = Color.FromArgb(alphaBar.Value, result.R, result.G, result.B);
            alphaLabel.Text = "不透明度 " + alphaBar.Value;
        }

        private void FinalizeAlpha()
        {
            preview.BackColor = result;
            hexBox.Text = "#" + result.R.ToString("X2") + result.G.ToString("X2") + result.B.ToString("X2") + result.A.ToString("X2");
        }

        public static Color Pick(IWin32Window owner, Color initial, string title = "选择颜色")
        {
            using (var f = new ColorPickerForm(title, initial))
                return f.ShowDialog(owner) == DialogResult.OK ? f.result : Color.Empty;
        }
    }
}
