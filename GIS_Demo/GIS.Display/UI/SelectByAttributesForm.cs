using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace GIS.Display.UI
{
    /// <summary>
    /// 对一个已加载图层执行内存属性表达式查询，并把结果写入地图选择集。
    /// 表达式语法由 DataTable.Select 提供，不会向 PostGIS 或其他数据源发送 SQL。
    /// </summary>
    public sealed class SelectByAttributesForm : Form
    {
        private readonly MapControl map;
        private readonly ComboBox layerBox = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly ComboBox methodBox = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly DataGridView fieldGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            MultiSelect = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            EnableHeadersVisualStyles = false
        };
        private readonly TextBox expressionBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            AcceptsReturn = true,
            AcceptsTab = true,
            ScrollBars = ScrollBars.Vertical
        };
        private readonly Label statusLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(90, 90, 90),
            Padding = new Padding(8, 0, 0, 0)
        };
        private readonly Button verifyButton = new Button { Text = "验证" };
        private readonly Button applyButton = new Button { Text = "应用" };
        private readonly Button okButton = new Button { Text = "确定" };
        private bool refreshingLayers;

        public SelectByAttributesForm(MapControl map)
        {
            this.map = map ?? throw new ArgumentNullException(nameof(map));
            Text = "按属性选择";
            Font = new Font("Microsoft YaHei UI", 10F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(820, 560);
            MinimumSize = new Size(680, 460);
            StartPosition = FormStartPosition.CenterScreen;

            methodBox.Items.AddRange(new object[]
            {
                "新建选择",
                "添加到当前选择",
                "从当前选择中移除",
                "在当前选择中筛选"
            });
            methodBox.SelectedIndex = 0;

            ConfigureFieldGrid();
            Controls.Add(BuildLayout());

            layerBox.SelectedIndexChanged += (s, e) => { if (!refreshingLayers) RefreshFields(); };
            fieldGrid.CellDoubleClick += (s, e) => InsertSelectedField(e.RowIndex);
            verifyButton.Click += (s, e) => ExecuteQuery(false, false);
            applyButton.Click += (s, e) => ExecuteQuery(true, false);
            okButton.Click += (s, e) => ExecuteQuery(true, true);
            map.LayersChanged += MapLayersChanged;
            FormClosed += (s, e) => map.LayersChanged -= MapLayersChanged;

            RefreshLayers();
            DialogLayout.Apply(this);
        }

        private Control BuildLayout()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(12),
                ColumnCount = 1,
                RowCount = 4
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));

            var choices = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 2 };
            choices.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76));
            choices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            choices.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 94));
            choices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            choices.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            choices.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            choices.Controls.Add(new Label { Text = "图层", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            choices.Controls.Add(layerBox, 1, 0);
            choices.Controls.Add(new Label { Text = "选择方式", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 2, 0);
            choices.Controls.Add(methodBox, 3, 0);
            var syntax = new Label
            {
                Text = "输入类似 SQL WHERE 的条件，不要填写 SELECT 和 FROM。空表达式表示全部要素。",
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(90, 90, 90),
                TextAlign = ContentAlignment.MiddleLeft
            };
            choices.Controls.Add(syntax, 0, 1);
            choices.SetColumnSpan(syntax, 4);
            root.Controls.Add(choices, 0, 0);

            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                FixedPanel = FixedPanel.Panel1
            };
            Shown += (s, e) =>
            {
                split.Panel1MinSize = 230;
                split.Panel2MinSize = 300;
                int maximum = split.Width - split.Panel2MinSize - split.SplitterWidth;
                if (maximum >= split.Panel1MinSize)
                    split.SplitterDistance = Math.Min(300, maximum);
            };
            split.Panel1.Padding = new Padding(0, 0, 6, 0);
            split.Panel2.Padding = new Padding(6, 0, 0, 0);
            split.Panel1.Controls.Add(BuildFieldPanel());
            split.Panel2.Controls.Add(BuildExpressionPanel());
            root.Controls.Add(split, 0, 1);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Padding = new Padding(0, 6, 0, 0)
            };
            var closeButton = new Button { Text = "关闭" };
            var clearButton = new Button { Text = "清除" };
            closeButton.Click += (s, e) => Close();
            clearButton.Click += (s, e) => { expressionBox.Clear(); SetStatus("已清空查询表达式。", false); };
            buttons.Controls.Add(closeButton);
            buttons.Controls.Add(okButton);
            buttons.Controls.Add(applyButton);
            buttons.Controls.Add(verifyButton);
            buttons.Controls.Add(clearButton);
            root.Controls.Add(buttons, 0, 2);
            root.Controls.Add(statusLabel, 0, 3);
            return root;
        }

        private Control BuildFieldPanel()
        {
            var panel = new Panel { Dock = DockStyle.Fill };
            var title = new Label
            {
                Text = "字段（双击插入表达式）",
                Dock = DockStyle.Top,
                Height = 32,
                Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            };
            panel.Controls.Add(fieldGrid);
            panel.Controls.Add(title);
            return panel;
        }

        private Control BuildExpressionPanel()
        {
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
            panel.Controls.Add(new Label
            {
                Text = "查询表达式",
                Dock = DockStyle.Fill,
                Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, 0);
            panel.Controls.Add(expressionBox, 0, 1);

            var operators = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                WrapContents = true,
                Padding = new Padding(0, 5, 0, 0)
            };
            foreach (string token in new[] { "=", "<>", ">", ">=", "<", "<=", "AND", "OR", "NOT", "LIKE", "IS NULL", "IS NOT NULL" })
            {
                var button = new Button { Text = token, AutoSize = true, Tag = token };
                button.Click += (s, e) => InsertToken((string)((Button)s).Tag);
                operators.Controls.Add(button);
            }
            panel.Controls.Add(operators, 0, 2);
            return panel;
        }

        private void ConfigureFieldGrid()
        {
            fieldGrid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(240, 243, 247);
            fieldGrid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(35, 35, 35);
            fieldGrid.ColumnHeadersDefaultCellStyle.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            fieldGrid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(249, 250, 252);
            fieldGrid.Columns.Add("Name", "字段名");
            fieldGrid.Columns.Add("Alias", "别名");
            fieldGrid.Columns.Add("Type", "类型");
            fieldGrid.Columns[0].FillWeight = 110;
            fieldGrid.Columns[1].FillWeight = 100;
            fieldGrid.Columns[2].FillWeight = 70;
        }

        private void MapLayersChanged(object sender, EventArgs e)
        {
            RefreshLayers();
        }

        private void RefreshLayers()
        {
            Layer previous = SelectedLayer;
            refreshingLayers = true;
            try
            {
                layerBox.Items.Clear();
                foreach (Layer layer in map.Layers)
                    layerBox.Items.Add(new LayerItem(layer));
                int index = previous == null ? -1 : layerBox.Items.Cast<LayerItem>()
                    .Select((item, i) => new { item, i })
                    .Where(x => ReferenceEquals(x.item.Layer, previous))
                    .Select(x => x.i)
                    .DefaultIfEmpty(-1)
                    .First();
                layerBox.SelectedIndex = index >= 0 ? index : (layerBox.Items.Count > 0 ? 0 : -1);
            }
            finally
            {
                refreshingLayers = false;
            }
            RefreshFields();
            UpdateEnabledState();
        }

        private void RefreshFields()
        {
            fieldGrid.Rows.Clear();
            Layer layer = SelectedLayer;
            if (layer == null) { UpdateEnabledState(); return; }
            foreach (Field field in layer.FeatureClass.Fields)
                fieldGrid.Rows.Add(field.Name, field.Alias, FieldTypeName(field.ValueType));
            UpdateEnabledState();
            SetStatus(fieldGrid.Rows.Count == 0 ? "当前图层没有属性字段；空表达式仍可选择全部要素。" :
                "请选择字段并输入条件，或直接输入表达式。", false);
        }

        private void UpdateEnabledState()
        {
            bool enabled = SelectedLayer != null;
            verifyButton.Enabled = enabled;
            applyButton.Enabled = enabled;
            okButton.Enabled = enabled;
            expressionBox.Enabled = enabled;
            methodBox.Enabled = enabled;
            fieldGrid.Enabled = enabled;
        }

        private Layer SelectedLayer => (layerBox.SelectedItem as LayerItem)?.Layer;

        private void InsertSelectedField(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= fieldGrid.Rows.Count) return;
            string name = Convert.ToString(fieldGrid.Rows[rowIndex].Cells[0].Value);
            InsertToken("[" + EscapeFieldName(name) + "]");
        }

        private void InsertToken(string token)
        {
            if (!expressionBox.Enabled) return;
            int start = expressionBox.SelectionStart;
            int length = expressionBox.SelectionLength;
            string left = expressionBox.Text.Substring(0, start);
            string right = expressionBox.Text.Substring(start + length);
            string insertion = token;
            if (left.Length > 0 && !char.IsWhiteSpace(left[left.Length - 1])) insertion = " " + insertion;
            if (right.Length > 0 && !char.IsWhiteSpace(right[0])) insertion += " ";
            expressionBox.Text = left + insertion + right;
            expressionBox.SelectionStart = left.Length + insertion.Length;
            expressionBox.Focus();
        }

        private void ExecuteQuery(bool apply, bool closeAfterApply)
        {
            Layer layer = SelectedLayer;
            if (layer == null)
            {
                ShowError("当前没有可查询的图层。");
                return;
            }
            bool correct;
            Features result = layer.FeatureClass.SearchByExpression(expressionBox.Text, out correct);
            if (!correct)
            {
                ShowError("查询表达式不正确。请检查字段名、引号、运算符和数据类型。");
                return;
            }
            if (!apply)
            {
                SetStatus(string.Format("表达式有效，预计命中 {0:N0} 个要素。", result.Count), true);
                return;
            }
            if (!layer.Visible)
            {
                ShowError("目标图层当前不可见，请先显示该图层再执行选择。");
                return;
            }
            if (!map.IsLayerSelectable(layer))
            {
                ShowError("目标图层当前不可选择，请先在图层面板中启用选择。");
                return;
            }
            var method = (SelectMethodConstant)methodBox.SelectedIndex;
            map.SetSelection(layer, result, method);
            int selectedCount = map.GetSelection(layer).Count;
            SetStatus(string.Format("查询命中 {0:N0} 个要素，当前图层共选中 {1:N0} 个要素。", result.Count, selectedCount), true);
            if (closeAfterApply) Close();
        }

        private void ShowError(string message)
        {
            SetStatus(message, false);
            MessageBox.Show(this, message, "按属性选择", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void SetStatus(string text, bool success)
        {
            statusLabel.Text = text;
            statusLabel.ForeColor = success ? Color.FromArgb(36, 112, 70) : Color.FromArgb(90, 90, 90);
        }

        private static string EscapeFieldName(string name)
        {
            return (name ?? "").Replace("\\", "\\\\").Replace("]", "\\]");
        }

        private static string FieldTypeName(FieldTypeConstant type)
        {
            switch (type)
            {
                case FieldTypeConstant.Byte: return "字节";
                case FieldTypeConstant.Int16: return "短整型";
                case FieldTypeConstant.Int32: return "整型";
                case FieldTypeConstant.Int64: return "长整型";
                case FieldTypeConstant.Single: return "单精度";
                case FieldTypeConstant.Double: return "双精度";
                case FieldTypeConstant.Decimal: return "小数";
                case FieldTypeConstant.Text: return "文本";
                case FieldTypeConstant.Date: return "日期";
                case FieldTypeConstant.Boolean: return "布尔";
                default: return type.ToString();
            }
        }

        private sealed class LayerItem
        {
            public Layer Layer { get; }
            public LayerItem(Layer layer) { Layer = layer; }
            public override string ToString() { return Layer?.Name ?? ""; }
        }
    }
}
