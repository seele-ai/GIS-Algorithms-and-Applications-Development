using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace GIS.Display.UI
{
    /// <summary>
    /// 注记设置：绑定字段、字体、颜色、宽高比、旋转、描边与“避免注记相互遮盖”，带实时预览。
    /// 按钮语义：应用 = 写回图层但不关窗；确定 = 写回并关闭；取消 = 回滚到打开时的注记设置并关闭；
    /// 退出 = 直接关闭（已经“应用”过的结果保留）。
    /// </summary>
    public sealed class LabelRendererForm : Form
    {
        private readonly Layer layer;
        private readonly LabelRenderer renderer;
        private readonly LabelRenderer backup;      // 打开窗口时的注记设置，用于“取消”回滚
        private readonly Action onApplied;          // “应用/确定/取消”后通知外部刷新地图与图层面板
        private bool applied;                       // 是否已经应用过（用于“退出”时提示/保留）

        private readonly ComboBox field = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 250 };
        private readonly ComboBox fontName = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 250 };
        private readonly NumericUpDown fontSize = new NumericUpDown { Minimum = 4, Maximum = 72, Width = 110 };
        private readonly CheckBox bold = new CheckBox { Text = "加粗" };
        private readonly CheckBox italic = new CheckBox { Text = "倾斜" };
        private readonly NumericUpDown fontRatio = new NumericUpDown { Minimum = 0.5m, Maximum = 3m, Increment = 0.1m, DecimalPlaces = 1, Width = 110 };
        private readonly CheckBox useMask = new CheckBox { Text = "描边" };
        private readonly NumericUpDown maskWidth = new NumericUpDown { Minimum = 0.1m, Maximum = 5m, Increment = 0.1m, DecimalPlaces = 1, Width = 110 };
        private readonly NumericUpDown rotate = new NumericUpDown { Minimum = -360, Maximum = 360, Width = 110 };
        private readonly CheckBox avoidOverlap = new CheckBox { Text = "避免注记相互遮盖（冲突时换位置或省略）", AutoSize = true };
        private readonly Panel preview = new Panel { Dock = DockStyle.Top, Height = 76, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.White };
        private Button fontColorBtn;
        private Button maskColorBtn;

        private LabelRendererForm(Layer layer, Action onApplied)
        {
            this.layer = layer;
            this.onApplied = onApplied;
            backup = layer.LabelRenderer == null ? null : layer.LabelRenderer.Clone();
            renderer = layer.LabelRenderer == null
                ? new LabelRenderer { LabelFeatures = true, Field = FirstField(layer) }
                : layer.LabelRenderer.Clone();
            Text = "注记设置：" + layer.Name;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
            MinimumSize = new Size(520, 0);
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
            avoidOverlap.Checked = renderer.AvoidOverlap;

            fontColorBtn = ColorButton(ts.FontColor, "字体颜色", c => ts.FontColor = c);
            maskColorBtn = ColorButton(ts.MaskColor, "描边颜色", c => ts.MaskColor = c);

            preview.Paint += (s, e) => DrawPreview(e.Graphics, preview.ClientRectangle);
            // 任何一个设置变化都要刷新预览：尤其是宽高比与旋转角度（此前这两项不影响预览）
            field.SelectedIndexChanged += (s, e) => preview.Invalidate();
            fontName.SelectedIndexChanged += (s, e) => preview.Invalidate();
            fontName.TextChanged += (s, e) => preview.Invalidate();
            fontSize.ValueChanged += (s, e) => preview.Invalidate();
            bold.CheckedChanged += (s, e) => preview.Invalidate();
            italic.CheckedChanged += (s, e) => preview.Invalidate();
            fontRatio.ValueChanged += (s, e) => preview.Invalidate();
            rotate.ValueChanged += (s, e) => preview.Invalidate();
            useMask.CheckedChanged += (s, e) => preview.Invalidate();
            maskWidth.ValueChanged += (s, e) => preview.Invalidate();

            var grid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top };
            grid.Controls.Add(new Label { Text = "注记字段", AutoSize = true }, 0, 0); grid.Controls.Add(field, 1, 0);
            grid.Controls.Add(new Label { Text = "字体", AutoSize = true }, 0, 1); grid.Controls.Add(fontName, 1, 1);
            grid.Controls.Add(new Label { Text = "字号", AutoSize = true }, 0, 2); grid.Controls.Add(fontSize, 1, 2);
            grid.Controls.Add(new Label { Text = "字体颜色", AutoSize = true }, 0, 3); grid.Controls.Add(fontColorBtn, 1, 3);
            grid.Controls.Add(new Label { Text = "宽高比", AutoSize = true }, 0, 4); grid.Controls.Add(fontRatio, 1, 4);
            grid.Controls.Add(new Label { Text = "旋转角度（逆时针为正）", AutoSize = true }, 0, 5); grid.Controls.Add(rotate, 1, 5);
            grid.Controls.Add(bold, 0, 6); grid.Controls.Add(italic, 1, 6);
            grid.Controls.Add(useMask, 0, 7); grid.Controls.Add(new Label { Text = "描边宽度（毫米）", AutoSize = true }, 1, 7);
            grid.Controls.Add(new Label { Text = "描边颜色", AutoSize = true }, 0, 8); grid.Controls.Add(maskColorBtn, 1, 8);
            grid.Controls.Add(new Label { Text = "描边宽度", AutoSize = true }, 0, 9); grid.Controls.Add(maskWidth, 1, 9);
            grid.Controls.Add(avoidOverlap, 0, 10);
            grid.SetColumnSpan(avoidOverlap, 2);

            var hint = new Label
            {
                Dock = DockStyle.Top, AutoSize = true, ForeColor = Color.FromArgb(90, 90, 90),
                Text = "应用：写回图层但不关闭窗口；确定：写回并关闭；取消：回滚到打开时的设置；退出：直接关闭（保留已应用的结果）。"
            };

            var apply = new Button { Text = "应用", Width = 84 };
            var ok = new Button { Text = "确定", Width = 84 };
            var cancel = new Button { Text = "取消", Width = 84 };
            var quit = new Button { Text = "退出", Width = 84 };
            apply.Click += (s, e) => Apply();
            ok.Click += (s, e) => { Apply(); DialogResult = DialogResult.OK; };
            cancel.Click += (s, e) => { Rollback(); DialogResult = DialogResult.Cancel; };
            quit.Click += (s, e) => { DialogResult = applied ? DialogResult.OK : DialogResult.Cancel; };
            var btns = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 8, 0, 0) };
            btns.Controls.Add(quit); btns.Controls.Add(cancel); btns.Controls.Add(ok); btns.Controls.Add(apply);

            Controls.Add(btns);
            Controls.Add(hint);
            Controls.Add(grid);
            Controls.Add(preview);
            AcceptButton = ok; CancelButton = cancel;
        }

        /// <summary>把当前界面设置写回图层（不关闭窗口），并通知外部刷新。</summary>
        private void Apply()
        {
            Commit(renderer.TextSymbol ?? new TextSymbol());
            layer.LabelRenderer = renderer.Clone();
            applied = true;
            onApplied?.Invoke();
        }

        // 取消：回滚到打开窗口时的注记设置（包括之前“应用”过的改动）
        private void Rollback()
        {
            layer.LabelRenderer = backup == null ? null : backup.Clone();
            onApplied?.Invoke();
        }

        /// <summary>
        /// 预览：与地图共用 BasicGeometryDrawer 的测量与绘制代码，因此宽高比、旋转、描边、颜色
        /// 都会立即反映在预览里（此前预览固定画水平、未缩放的文字）。
        /// </summary>
        private void DrawPreview(Graphics g, Rectangle rect)
        {
            g.Clear(Color.White);
            var ts = new TextSymbol
            {
                FontName = fontName.Text,
                FontSize = (float)fontSize.Value,
                Bold = bold.Checked,
                Italic = italic.Checked,
                FontColor = fontColorBtn.BackColor,
                FontRatio = (double)fontRatio.Value,
                UseMask = useMask.Checked,
                MaskColor = maskColorBtn.BackColor,
                MaskWidth = (double)maskWidth.Value
            };
            string sample = "注记示例 Aa 123";
            double dpm = g.DpiX / 0.0254;
            SizeF size = BasicGeometryDrawer.MeasureLabel(g, sample, ts, dpm);
            // 文字按锚点绘制、向右下延伸；预览里把它居中放置，便于观察旋转与宽高比
            var location = new PointF(rect.Left + Math.Max(6f, (rect.Width - size.Width) / 2f),
                rect.Top + Math.Max(6f, (rect.Height - size.Height) / 2f));
            BasicGeometryDrawer.DrawTextLabel(g, sample, location, ts, (double)rotate.Value);
        }

        /// <summary>把预览画到一张位图上（供自动检查使用，验证“改设置后预览会变化”）。</summary>
        public Bitmap RenderPreview()
        {
            var bitmap = new Bitmap(Math.Max(80, preview.Width), Math.Max(40, preview.Height));
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                DrawPreview(g, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
            }
            return bitmap;
        }

        private void Commit(TextSymbol ts)
        {
            renderer.Field = field.SelectedItem == null ? "" : field.SelectedItem.ToString();
            renderer.RotateAngle = (double)rotate.Value;
            renderer.AvoidOverlap = avoidOverlap.Checked;
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

        /// <summary>创建注记设置窗口（对话框与自动检查共用）。</summary>
        public static LabelRendererForm Create(Layer layer, Action onApplied = null) => new LabelRendererForm(layer, onApplied);

        /// <summary>编辑并写回图层的注记；未应用任何修改时返回 false。</summary>
        public static bool Edit(IWin32Window owner, Layer layer, Action onApplied = null)
        {
            using (var f = Create(layer, onApplied))
                return f.ShowDialog(owner) == DialogResult.OK;
        }
    }
}
