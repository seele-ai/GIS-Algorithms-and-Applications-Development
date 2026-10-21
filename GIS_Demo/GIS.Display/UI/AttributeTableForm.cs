using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace GIS.Display.UI
{
    /// <summary>可缩放属性表，选择与地图联动，编辑由宿主处理。</summary>
    public sealed class AttributeTableForm : Form
    {
        private readonly Layer layer;
        private readonly MapControl map;
        private bool showSelectedOnly;
        private readonly DataGridView grid = new DataGridView
        {
            Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
            AllowUserToDeleteRows = false, RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
            AllowUserToResizeColumns = true, AllowUserToResizeRows = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false,
            ScrollBars = ScrollBars.Both, BackgroundColor = Color.White
        };
        public event Action<Feature> FeatureSelected;
        public event Action<Feature> EditRequested;
        public event Action SaveEditsRequested;

        public AttributeTableForm(Layer layer) : this(layer, null) { }
        public AttributeTableForm(Layer layer, MapControl map)
        {
            this.layer = layer; this.map = map;
            Text = "属性表：" + layer.Name;
            Font = new Font("Microsoft YaHei UI", 10F);
            ClientSize = new Size(960, 540); MinimumSize = new Size(540, 320);
            FormBorderStyle = FormBorderStyle.Sizable; MaximizeBox = true;
            StartPosition = FormStartPosition.CenterParent;
            foreach (Field f in layer.FeatureClass.Fields)
                grid.Columns.Add(new DataGridViewTextBoxColumn {
                    HeaderText = string.IsNullOrEmpty(f.Alias) ? f.Name : f.Alias,
                    Name = f.Name, MinimumWidth = 90, Width = 130,
                    SortMode = DataGridViewColumnSortMode.NotSortable
                });
            // 无属性字段的要素也保留一行，可选择、打开编辑。
            if (grid.Columns.Count == 0) grid.Columns.Add("feature", "要素");
            var tools = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top };
            var fit = new ToolStripButton("适应列宽"); fit.Click += (s,e) => FitColumns();
            var selectedOnly = new ToolStripButton("仅显示选中") { CheckOnClick = true };
            selectedOnly.CheckedChanged += (s,e) => { showSelectedOnly = selectedOnly.Checked; ReloadRows(); };
            var save = new ToolStripButton("保存编辑") { ToolTipText = "确认当前页面修改并清空撤销记录，不写入文件" };
            save.Click += (s,e) => SaveEditsRequested?.Invoke();
            tools.Items.Add(fit); tools.Items.Add(selectedOnly); tools.Items.Add(save);
            grid.CellClick += (s,e) => { if (e.RowIndex >= 0) FeatureSelected?.Invoke((Feature)grid.Rows[e.RowIndex].Tag); };
            grid.CellDoubleClick += (s,e) => {
                if (e.RowIndex < 0) return;
                var feature = (Feature)grid.Rows[e.RowIndex].Tag;
                FeatureSelected?.Invoke(feature); EditRequested?.Invoke(feature);
                ReloadRows();
            };
            Controls.Add(grid); Controls.Add(tools);
            ReloadRows(); FitColumns();
            Activated += (s,e) => ReloadRows();
            if (map != null) { map.SelectionChanged += MapSelectionChanged; map.LayersChanged += MapLayersChanged; }
        }
        public void ReloadRows()
        {
            var current = grid.CurrentRow?.Tag as Feature;
            int first = grid.FirstDisplayedScrollingRowIndex;
            int horizontal = grid.HorizontalScrollingOffset;
            grid.Rows.Clear();
            var selected = showSelectedOnly && map != null && map.Layers.Contains(layer)
                ? map.GetSelection(layer).ToArray() : null;
            foreach (Feature feature in layer.FeatureClass.Features)
            {
                if (showSelectedOnly && (selected == null || !selected.Contains(feature))) continue;
                var values = new object[grid.Columns.Count];
                for (int i = 0; i < layer.FeatureClass.Fields.Count; i++)
                    values[i] = feature.Attributes?.GetItem(i);
                if (layer.FeatureClass.Fields.Count == 0) values[0] = "要素 " + (grid.Rows.Count + 1);
                grid.Rows[grid.Rows.Add(values)].Tag = feature;
            }
            foreach (DataGridViewRow row in grid.Rows)
                if (ReferenceEquals(row.Tag, current)) { grid.CurrentCell = row.Cells[0]; break; }
            grid.ClearSelection();
            if (first >= 0 && first < grid.Rows.Count) grid.FirstDisplayedScrollingRowIndex = first;
            grid.HorizontalScrollingOffset = horizontal;
            HighlightSelection();
        }
        private void FitColumns()
        {
            grid.AutoResizeColumns(DataGridViewAutoSizeColumnsMode.DisplayedCells);
            int available = Math.Max(90, (grid.ClientSize.Width - 24) / grid.Columns.Count);
            foreach (DataGridViewColumn column in grid.Columns)
                column.Width = Math.Max(90, Math.Max(Math.Min(column.Width, 300), available));
        }
        private void MapSelectionChanged(object sender, EventArgs e)
        {
            if (showSelectedOnly) ReloadRows(); else HighlightSelection();
        }
        private void MapLayersChanged(object sender, EventArgs e) { if (!map.Layers.Contains(layer)) Close(); }
        private void HighlightSelection()
        {
            var selected = map != null && map.Layers.Contains(layer) ? map.GetSelection(layer).ToArray() : new Feature[0];
            grid.ClearSelection();
            foreach (DataGridViewRow row in grid.Rows)
            {
                bool highlighted = selected.Contains((Feature)row.Tag);
                row.DefaultCellStyle.BackColor = highlighted ? Color.FromArgb(255, 235, 156) : Color.White;
                row.DefaultCellStyle.SelectionBackColor = highlighted ? Color.FromArgb(255, 235, 156) : SystemColors.Highlight;
                row.DefaultCellStyle.SelectionForeColor = highlighted ? Color.Black : SystemColors.HighlightText;
            }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && map != null) { map.SelectionChanged -= MapSelectionChanged; map.LayersChanged -= MapLayersChanged; }
            base.Dispose(disposing);
        }
    }
}
