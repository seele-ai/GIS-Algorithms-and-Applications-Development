using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using GIS.Display;
using GIS.Display.UI;

namespace GIS.Toolbox.UI
{
    /// <summary>一次“运行”用到的全部输入（工具界面按顺序收集，算法主体只读它）。</summary>
    public sealed class ToolRunInputs
    {
        /// <summary>输入是否来自“当前所有图层的已选要素”。</summary>
        public bool FromSelection;
        /// <summary>输入要素（已选要素集合，或某个图层的全部要素）。</summary>
        public Features InputFeatures;
        /// <summary>输入要素所属图层；来自已选要素且跨图层时为 null（见 <see cref="InputLayers"/>）。</summary>
        public Layer InputLayer;
        /// <summary>输入要素来自的图层（已选要素可能跨多个图层）。</summary>
        public List<Layer> InputLayers = new List<Layer>();
        /// <summary>其他图层（只能是图层，不能是已选要素）。</summary>
        public Layer OtherLayer;
        /// <summary>必要参数（用户手动输入）。</summary>
        public string TextParameter;
        /// <summary>必要参数（用户选择）。</summary>
        public string ChoiceParameter;
        /// <summary>框选参数（勾选上的项）。</summary>
        public List<string> CheckedOptions = new List<string>();
        /// <summary>输出图层名。</summary>
        public string OutputLayerName;
        /// <summary>输入要素的几何类型统计（例如“点 2 个、面 1 个”），便于工具判断输入是否合法。</summary>
        public string GeometrySummary = "";

        /// <summary>输入要素条数。</summary>
        public int FeatureCount
        {
            get { return InputFeatures == null ? 0 : InputFeatures.Count; }
        }

        /// <summary>把收集到的输入整理成一段可读的摘要（显示在“运行结果”里）。</summary>
        public string Describe()
        {
            var text = new System.Text.StringBuilder();
            text.AppendLine("输入要素：" + (FromSelection
                ? "当前所有图层的已选要素"
                : (InputLayer == null ? "（未指定）" : InputLayer.Name))
                + "（" + FeatureCount + " 个要素"
                + (string.IsNullOrEmpty(GeometrySummary) ? "" : "：" + GeometrySummary) + "）");
            if (FromSelection && InputLayers.Count > 0)
                text.AppendLine("　　来自图层：" + string.Join("、", InputLayers.Select(l => l.Name).ToArray()));
            text.AppendLine("其他图层：" + (OtherLayer == null ? "（未指定）" : OtherLayer.Name));
            text.AppendLine("必要参数（手动输入）：" + TextParameter);
            text.AppendLine("必要参数（选择）：" + ChoiceParameter);
            text.AppendLine("框选参数：" + (CheckedOptions.Count == 0 ? "（未勾选）" : string.Join("、", CheckedOptions.ToArray())));
            text.AppendLine("输出图层名：" + OutputLayerName);
            return text.ToString();
        }
    }

    /// <summary>
    /// 工具界面模板基类：**统一按需求规定的顺序**摆放输入项，最右下角是“运行”。
    ///
    /// 顺序（派生类在构造函数里按这个顺序调用对应的 Add... 方法即可）：
    ///   1) 输入要素        —— 下拉框：第一项是“（当前所有图层的已选要素）”，其后是按图层顺序（顶层在前）的候选图层；
    ///   2) 其他图层        —— 下拉框：只能选图层，没有“已选要素”这一项；
    ///   3) 必要参数（手动输入）—— 文本框，用户自己敲；
    ///   4) 必要参数（选择）    —— 下拉框，从固定选项里选；
    ///   5) 框选参数        —— 一组勾选框；
    ///   6) 输出图层名      —— 文本框；
    ///   7) 最右下角“运行”  —— 收集并校验上面的输入，再调用 <see cref="Execute"/>。
    ///
    /// 派生类只需实现 <see cref="CollectInputs"/>（把控件读成 <see cref="ToolRunInputs"/> 并做校验）
    /// 与 <see cref="Execute"/>（真正的空间分析算法）。参考 <see cref="TemplateToolForm"/>。
    /// </summary>
    public abstract class ToolFormBase : Form
    {
        /// <summary>输入要素下拉框里“已选要素”那一项的文字。</summary>
        public const string SelectionOptionText = "（当前所有图层的已选要素）";
        /// <summary>“运行结果”区域的高度（像素）。</summary>
        private const int ResultHeight = 140;

        private readonly TableLayoutPanel grid;
        private readonly TableLayoutPanel root;
        private readonly TextBox resultBox;
        private readonly Button runButton;
        private readonly Button closeButton;
        private TableLayoutPanel body;
        private FlowLayoutPanel buttons;
        private int row;

        /// <summary>当前地图（工具从它取图层与已选要素，可能为 null）。</summary>
        protected MapControl Map { get; }

        /// <summary>最近一次成功收集到的输入（自动检查用）。</summary>
        public ToolRunInputs LastInputs { get; private set; }

        /// <summary>“运行”按钮（自动检查用）。</summary>
        public Button RunButton
        {
            get { return runButton; }
        }

        /// <summary>“运行结果”文本框（自动检查用）。</summary>
        public TextBox ResultBox
        {
            get { return resultBox; }
        }

        /// <summary>参数区（两列：标签 / 控件），自动检查按行读标签顺序。</summary>
        public TableLayoutPanel FieldGrid
        {
            get { return grid; }
        }

        protected ToolFormBase(MapControl map, string title)
        {
            Map = map;
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            AutoScaleMode = AutoScaleMode.Dpi;
            MinimumSize = new Size(760, 0);
            Padding = new Padding(12);

            grid = new TableLayoutPanel
            {
                ColumnCount = 2,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 430));

            resultBox = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Height = 108,
                Width = 640,
                BackColor = Color.FromArgb(248, 248, 248),
                ForeColor = Color.FromArgb(60, 60, 60),
                WordWrap = true,
                Text = "尚未运行。填好上面的输入后点右下角的“运行”。"
            };
            runButton = new Button { Text = "运行", Width = 96 };
            closeButton = new Button { Text = "关闭", Width = 96, DialogResult = DialogResult.Cancel };
            runButton.Click += (s, e) => Run();
            AcceptButton = runButton;
            CancelButton = closeButton;

            root = new TableLayoutPanel
            {
                ColumnCount = 1,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top
            };
            Shown += (s, e) => BeginInvoke(new Action(RefreshContentLayout));
        }

        /// <summary>收尾：把参数区、运行结果、按钮行按顺序放进窗口（派生类构造函数最后调用）。</summary>
        protected void FinishLayout()
        {
            buttons = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,   // 第一个加进去的在最右边 → “运行”在最右下角
                Padding = new Padding(0, 10, 0, 0)
            };
            buttons.Controls.Add(runButton);
            buttons.Controls.Add(closeButton);
            int buttonsHeight = buttons.GetPreferredSize(Size.Empty).Height;

            // 上面参数区（按内容高度，绝不被裁掉）+ 运行结果（占满剩余高度），下面按钮行用 Dock=Bottom 贴住窗口底部
            body = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2
            };
            body.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // 参数区：行高按内容，不会被裁
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, ResultHeight));
            resultBox.Dock = DockStyle.Fill;
            body.Controls.Add(grid, 0, 0);
            body.Controls.Add(resultBox, 0, 1);
            // 按内容把窗口收到刚好放下：参数区高度 + 运行结果 + 按钮行。
            // 尺寸必须在 SuspendLayout 期间设好、再 ResumeLayout，否则按钮行不会重新贴到底部。
            int gridWidth = Math.Max(640, grid.GetPreferredSize(new Size(640, 0)).Width);
            int gridHeight = grid.GetPreferredSize(new Size(gridWidth, 0)).Height;
            int width = Math.Max(760, Padding.Horizontal + gridWidth + 10);
            int height = Padding.Vertical + gridHeight + ResultHeight + buttonsHeight + 12;
            SuspendLayout();
            AutoSize = false;
            Controls.Add(body);
            Controls.Add(buttons);
            ClientSize = new Size(width, height);
            ResumeLayout(true);
            DialogLayout.Apply(this);
            PerformLayout();
            // DialogLayout 会统一调整控件高度，调整后按实际内容再校正一次窗口高度，避免最后一个输入项被裁掉
            int measured = Padding.Vertical + grid.GetPreferredSize(new Size(grid.Width > 0 ? grid.Width : gridWidth, 0)).Height
                + ResultHeight + buttons.Height + 12;
            if (measured > ClientSize.Height) ClientSize = new Size(ClientSize.Width, measured);
            PerformLayout();
        }

        /// <summary>添加由派生工具自行组合的紧凑参数行。</summary>
        protected Label AddCustomField(string label, Control control)
        {
            return AddRow(label, control);
        }

        /// <summary>参数显隐改变后重新收紧窗口高度。</summary>
        protected void RefreshContentLayout()
        {
            if (body == null || buttons == null) return;
            grid.PerformLayout();
            int gridWidth = Math.Max(640, grid.GetPreferredSize(new Size(640, 0)).Width);
            int gridHeight = grid.GetPreferredSize(new Size(gridWidth, 0)).Height;
            int width = Math.Max(760, Padding.Horizontal + gridWidth + 10);
            int height = Padding.Vertical + gridHeight + ResultHeight + buttons.Height + 12;
            ClientSize = new Size(width, height);
            PerformLayout();
        }

        #region 六类输入项（严格按需求顺序使用）

        /// <summary>
        /// ① 输入要素：下拉框。第一项固定是“（当前所有图层的已选要素……）”——
        /// 括号里会列出**当前已选要素的几何类型与个数**（例如“点 2 个、面 1 个，共 3 个”“无”），
        /// 用户和工具据此判断输入是否合法；展开下拉时自动刷新。
        /// 之后是按图层顺序（顶层在前）列出所有“符合要求”的图层。
        /// </summary>
        /// <param name="label">标签文字。</param>
        /// <param name="layerFilter">图层过滤（例如只要面图层）；null 表示不过滤。</param>
        protected ComboBox AddInputFeaturesField(string label, Func<Layer, bool> layerFilter)
        {
            var combo = NewCombo();
            combo.Items.Add(SelectionSummaryText());
            foreach (Layer layer in CandidateLayers(layerFilter)) combo.Items.Add(new LayerItem(layer));
            combo.SelectedIndex = 0;
            combo.DropDown += (s, e) => RefreshSelectionSummaryItem(combo);
            combo.Enter += (s, e) => RefreshSelectionSummaryItem(combo);
            AddRow(label, combo);
            return combo;
        }

        /// <summary>把“已选要素”那一项的文字刷新成当前几何类型统计（选中状态保持不变）。</summary>
        protected void RefreshSelectionSummaryItem(ComboBox combo)
        {
            if (combo == null || combo.Items.Count == 0) return;
            if (combo.SelectedItem is LayerItem) return;          // 当前选的是图层，不动第一项
            string text = SelectionSummaryText();
            if (string.Equals(combo.Items[0].ToString(), text)) return;
            int selected = combo.SelectedIndex;
            combo.Items[0] = text;                                // 直接替换元素，选中项仍是“已选要素”
            combo.SelectedIndex = selected < 0 ? 0 : selected;
        }

        /// <summary>
        /// “当前所有图层的已选要素”这一项的文字：列出已选要素的**几何类型与个数**，
        /// 便于用户与工具判断输入是否合法（没有已选要素时明确写“无”）。
        /// </summary>
        protected string SelectionSummaryText()
        {
            var all = new Features();
            if (Map != null)
            {
                foreach (Layer layer in Map.Layers)
                {
                    Features selected = Map.GetSelection(layer);
                    if (selected == null) continue;
                    foreach (Feature feature in selected) all.Add(feature);
                }
            }
            // 收起状态使用短摘要，避免高 DPI/较大字体时文字被 ComboBox 右侧箭头遮住。
            if (all.Count == 0) return "当前已选要素：无";
            string compact = SummarizeGeometry(all).Replace(" 个", "").Replace(" ", "");
            return "当前已选：" + compact + "（共" + all.Count + "）";
        }

        /// <summary>要素的几何类型（几何为空时退回图层类型）。</summary>
        protected static GeometryTypeConstant FeatureGeometryType(Layer layer, Feature feature)
        {
            if (feature != null && feature.Geometry != null) return feature.Geometry.GeometryType;
            return layer == null || layer.FeatureClass == null ? GeometryTypeConstant.Point : layer.FeatureClass.GeometryType;
        }

        /// <summary>② 其他图层：下拉框，**只有图层**（没有“已选要素”这一项）。默认不选，必须手动选。</summary>
        /// <param name="label">标签文字。</param>
        /// <param name="layerFilter">图层过滤；null 表示不过滤。</param>
        /// <param name="excluded">需要从列表里排除的图层（例如已作为输入要素的图层）；可为 null。</param>
        protected ComboBox AddLayerField(string label, Func<Layer, bool> layerFilter = null, Layer excluded = null)
        {
            var combo = NewCombo();
            foreach (Layer layer in CandidateLayers(layerFilter))
                if (!ReferenceEquals(layer, excluded)) combo.Items.Add(new LayerItem(layer));
            combo.SelectedIndex = -1;
            AddRow(label, combo);
            return combo;
        }

        /// <summary>③ 必要参数（用户手动输入）：单行文本框。</summary>
        protected TextBox AddTextParameterField(string label, string placeholder, string defaultValue = "")
        {
            var box = new TextBox { Width = 430, Text = defaultValue };
            // .NET Framework 的兼容占位实现会占用 Text；已有默认值时不能再用占位文字覆盖它。
            if (!string.IsNullOrEmpty(placeholder) && string.IsNullOrEmpty(defaultValue))
                box.PlaceholderTextCompat(placeholder);
            AddRow(label, box);
            return box;
        }

        /// <summary>④ 必要参数（用户选择）：下拉框，只能从给定选项里选，默认选第一项。</summary>
        protected ComboBox AddChoiceParameterField(string label, params string[] choices)
        {
            var combo = NewCombo();
            if (choices != null) combo.Items.AddRange(choices);
            if (combo.Items.Count > 0) combo.SelectedIndex = 0;
            AddRow(label, combo);
            return combo;
        }

        /// <summary>⑤ 框选参数：一组勾选框（每个勾选框各占一行，标签与第一个勾选框同一行）。</summary>
        protected CheckBox[] AddCheckParametersField(string label, params string[] options)
        {
            int count = options == null ? 0 : options.Length;
            var checks = new CheckBox[count];
            for (int i = 0; i < count; i++)
            {
                checks[i] = new CheckBox { Text = options[i], AutoSize = true };
                if (grid.RowCount <= row) grid.RowCount = row + 1;
                while (grid.RowStyles.Count <= row) grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                if (i == 0) grid.Controls.Add(new Label { Text = label, AutoSize = true, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
                grid.Controls.Add(checks[i], 1, row);
                row++;
            }
            if (count == 0) AddRow(label, new Label { Text = "（无）", AutoSize = true });
            return checks;
        }

        /// <summary>⑥ 输出图层名：单行文本框。</summary>
        protected TextBox AddOutputNameField(string label, string defaultName)
        {
            var box = new TextBox { Width = 430, Text = defaultName };
            AddRow(label, box);
            return box;
        }

        #endregion

        #region 运行

        /// <summary>
        /// ⑦ 运行：先校验（失败时把原因写进“运行结果”），再调用 <see cref="Execute"/>。
        /// 返回是否真正执行了算法主体。
        /// </summary>
        public bool TryRun(out string message)
        {
            message = "";
            ToolRunInputs inputs;
            string error;
            if (!CollectInputs(out inputs, out error))
            {
                message = "无法运行：" + error;
                ShowResult(message);
                return false;
            }
            // 统计输入要素的几何类型（用户与工具据此判断输入是否合法），并按工具声明的要求校验
            inputs.GeometrySummary = SummarizeGeometry(inputs.InputFeatures);
            string rejected = DescribeUnacceptedGeometry(inputs);
            if (rejected != null)
            {
                message = "无法运行：输入要素的几何类型不符合本工具要求（" + rejected + "）。";
                ShowResult(message);
                return false;
            }
            LastInputs = inputs;
            try
            {
                Execute(inputs);
                message = resultBox.Text;
                return true;
            }
            catch (Exception ex)
            {
                message = "运行失败：" + ex.Message;
                ShowResult(message);
                return false;
            }
        }

        /// <summary>工具接受的输入要素几何类型（null 或空 = 不限）。派生类在构造函数里设置即可自动校验。</summary>
        protected GeometryTypeConstant[] AcceptedInputTypes { get; set; }

        /// <summary>某个几何类型是否被本工具接受（默认全部接受；派生类可覆盖或设置 <see cref="AcceptedInputTypes"/>）。</summary>
        protected virtual bool AcceptsGeometryType(GeometryTypeConstant type)
        {
            if (AcceptedInputTypes == null || AcceptedInputTypes.Length == 0) return true;
            return AcceptedInputTypes.Contains(type);
        }

        /// <summary>输入要素的几何类型统计，例如“点 2 个、面 1 个”。</summary>
        protected static string SummarizeGeometry(Features features)
        {
            if (features == null || features.Count == 0) return "无";
            var counts = new Dictionary<GeometryTypeConstant, int>();
            foreach (Feature feature in features)
            {
                GeometryTypeConstant type = feature == null || feature.Geometry == null
                    ? GeometryTypeConstant.Point : feature.Geometry.GeometryType;
                counts[type] = counts.ContainsKey(type) ? counts[type] + 1 : 1;
            }
            return string.Join("、", counts.OrderBy(pair => (int)pair.Key)
                .Select(pair => GeometryText(pair.Key) + " " + pair.Value + " 个").ToArray());
        }

        // 返回不符合要求的几何类型说明；全部符合时返回 null
        private string DescribeUnacceptedGeometry(ToolRunInputs inputs)
        {
            if (inputs == null || inputs.InputFeatures == null || inputs.InputFeatures.Count == 0) return null;
            var rejected = new List<string>();
            foreach (Feature feature in inputs.InputFeatures)
            {
                GeometryTypeConstant type = feature == null || feature.Geometry == null
                    ? GeometryTypeConstant.Point : feature.Geometry.GeometryType;
                if (AcceptsGeometryType(type)) continue;
                string text = GeometryText(type);
                if (!rejected.Contains(text)) rejected.Add(text);
            }
            if (rejected.Count == 0) return null;
            string wanted = AcceptedInputTypes == null
                ? "" : string.Join("、", AcceptedInputTypes.Select(GeometryText).ToArray());
            return "需要 " + wanted + "，实际包含 " + string.Join("、", rejected.ToArray());
        }

        private void Run()
        {
            string message;
            TryRun(out message);
        }

        /// <summary>把文字写到“运行结果”区域。</summary>
        protected void ShowResult(string text)
        {
            resultBox.Text = text == null ? "" : text;
            resultBox.SelectionStart = 0;
            resultBox.SelectionLength = 0;
        }

        /// <summary>收集并校验输入：成功返回 true 并给出快照，失败返回 false 并说明原因。</summary>
        protected abstract bool CollectInputs(out ToolRunInputs inputs, out string error);

        /// <summary>工具主体：真正的空间分析算法（模板工具里是占位实现）。</summary>
        protected abstract void Execute(ToolRunInputs inputs);

        #endregion

        #region 辅助

        /// <summary>可选的图层：按图层顺序（顶层在前，与图层面板一致），并按需要过滤几何类型。</summary>
        protected IEnumerable<Layer> CandidateLayers(Func<Layer, bool> layerFilter)
        {
            if (Map == null) yield break;
            var layers = Map.Layers.Reverse().ToList();   // 顶层在前
            foreach (Layer layer in layers)
            {
                if (layer == null || layer.FeatureClass == null) continue;
                if (layerFilter != null && !layerFilter(layer)) continue;
                yield return layer;
            }
        }

        /// <summary>把下拉框的选中项读成图层；选中“已选要素”或未选时返回 null。</summary>
        protected static Layer SelectedLayer(ComboBox combo)
        {
            if (combo == null) return null;
            var item = combo.SelectedItem as LayerItem;
            return item == null ? null : item.Layer;
        }

        /// <summary>输入要素下拉框是否选中了“当前所有图层的已选要素”。</summary>
        protected static bool IsSelectionOption(ComboBox combo)
        {
            return combo != null && combo.SelectedIndex == 0;
        }

        /// <summary>图层显示名：名称（几何类型，要素数）。</summary>
        protected static string LayerText(Layer layer)
        {
            if (layer == null) return "";
            return layer.Name + "（" + GeometryText(layer.FeatureClass.GeometryType) + "，"
                + layer.FeatureClass.Features.Count + " 个要素）";
        }

        /// <summary>几何类型的中文名。</summary>
        protected static string GeometryText(GeometryTypeConstant type)
        {
            switch (type)
            {
                case GeometryTypeConstant.Point: return "点";
                case GeometryTypeConstant.MultiPoint: return "多点";
                case GeometryTypeConstant.LineString: return "折线";
                case GeometryTypeConstant.MultiLineString: return "复合折线";
                case GeometryTypeConstant.Polygon: return "面";
                case GeometryTypeConstant.MultiPolygon: return "复合面";
                default: return type.ToString();
            }
        }

        private static ComboBox NewCombo()
        {
            return new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 430,
                DropDownHeight = 260
            };
        }

        private Label AddRow(string label, Control control)
        {
            // TableLayoutPanel 不会因为 SetCellPosition 自动增长行数，这里显式维护，
            // 外部（自动检查 / 后续同学）按 RowCount 遍历时就能看到全部输入项。
            if (grid.RowCount <= row) grid.RowCount = row + 1;
            while (grid.RowStyles.Count <= row) grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var caption = new Label { Text = label, AutoSize = true, TextAlign = ContentAlignment.MiddleLeft };
            grid.Controls.Add(caption, 0, row);
            grid.Controls.Add(control, 1, row);
            row++;
            return caption;
        }

        /// <summary>下拉框里的一个图层选项（ComboBox 显示 ToString()）。</summary>
        protected sealed class LayerItem
        {
            public LayerItem(Layer layer)
            {
                Layer = layer;
            }

            public Layer Layer { get; }

            public override string ToString()
            {
                return LayerText(Layer);
            }
        }

        #endregion
    }

    /// <summary>TextBox 的占位提示（.NET Framework 4.8 的 WinForms 没有 PlaceholderText，用浅色提示代替）。</summary>
    internal static class TextBoxPlaceholder
    {
        public static void PlaceholderTextCompat(this TextBox box, string placeholder)
        {
            if (box == null || string.IsNullOrEmpty(placeholder)) return;
            var normal = box.ForeColor;
            box.Text = placeholder;
            box.ForeColor = Color.FromArgb(150, 150, 150);
            box.Enter += (s, e) =>
            {
                if (box.Text == placeholder)
                {
                    box.Text = "";
                    box.ForeColor = normal;
                }
            };
            box.Leave += (s, e) =>
            {
                if (string.IsNullOrEmpty(box.Text))
                {
                    box.Text = placeholder;
                    box.ForeColor = Color.FromArgb(150, 150, 150);
                }
            };
        }
    }
}
