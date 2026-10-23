using System;
using System.Collections.Generic;
using System.ComponentModel;
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
        private bool syncingSelection;
        private bool selectionOriginatedFromGrid;
        private bool initialDisplayComplete;
        private string sortColumnName;
        private ListSortDirection? sortDirection;
        private int contextColumnIndex = -1;
        private readonly ContextMenuStrip columnMenu = new ContextMenuStrip();
        private readonly DataGridView grid = new DataGridView
        {
            Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
            AllowUserToDeleteRows = false, RowHeadersVisible = true, RowHeadersWidth = 58,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
            AllowUserToResizeColumns = true, AllowUserToResizeRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = true,
            ScrollBars = ScrollBars.Both, BackgroundColor = Color.White,
            BorderStyle = BorderStyle.None, EnableHeadersVisualStyles = false,
            ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText
        };
        private readonly ToolStripStatusLabel recordStatus = new ToolStripStatusLabel
        {
            Spring = true, TextAlign = ContentAlignment.MiddleLeft
        };
        private readonly ToolStripButton zoomToSelection = new ToolStripButton("缩放至选择");
        public event Action<Features> SelectionRequested;
        public event Action<Feature> EditRequested;
        public event Action AddFieldRequested;
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
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(240, 243, 247);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(35, 35, 35);
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(240, 243, 247);
            grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Color.FromArgb(35, 35, 35);
            grid.ColumnHeadersDefaultCellStyle.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(205, 224, 252);
            grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(25, 25, 25);
            grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(249, 250, 252);
            grid.RowHeadersDefaultCellStyle.BackColor = Color.FromArgb(240, 243, 247);
            grid.RowHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(205, 224, 252);
            grid.RowHeadersDefaultCellStyle.SelectionForeColor = Color.FromArgb(25, 25, 25);
            EnsureColumns();
            var tools = new ToolStrip {
                GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top,
                Padding = new Padding(6, 3, 6, 3), BackColor = Color.FromArgb(248, 249, 251)
            };
            var clearSelection = new ToolStripButton("清除选择") { ToolTipText = "清除此图层的选择集" };
            clearSelection.Click += (s,e) => RequestSelection(new Features());
            zoomToSelection.ToolTipText = "将地图缩放至当前选中要素";
            zoomToSelection.Click += (s,e) => ZoomToCurrentSelection();
            var selectedOnly = new ToolStripButton("仅显示选中") { CheckOnClick = true };
            selectedOnly.CheckedChanged += (s,e) => { showSelectedOnly = selectedOnly.Checked; ReloadRows(); };
            var fit = new ToolStripButton("适应列宽") { ToolTipText = "根据当前显示内容调整列宽" };
            fit.Click += (s,e) => FitColumns();
            var addField = new ToolStripButton("添加字段") { ToolTipText = "向当前图层添加属性字段" };
            addField.Click += (s,e) => AddFieldRequested?.Invoke();
            var save = new ToolStripButton("保存编辑") { ToolTipText = "确认当前页面修改并清空撤销记录，不写入文件" };
            save.Click += (s,e) => SaveEditsRequested?.Invoke();
            tools.Items.Add(clearSelection);
            tools.Items.Add(zoomToSelection);
            tools.Items.Add(selectedOnly);
            tools.Items.Add(new ToolStripSeparator());
            tools.Items.Add(fit);
            tools.Items.Add(addField);
            tools.Items.Add(new ToolStripSeparator());
            tools.Items.Add(save);
            var status = new StatusStrip { SizingGrip = false, BackColor = Color.FromArgb(248, 249, 251) };
            status.Items.Add(recordStatus);
            var sortAscending = new ToolStripMenuItem("升序排列");
            var sortDescending = new ToolStripMenuItem("降序排列");
            var clearSort = new ToolStripMenuItem("清除排序");
            var fitThisColumn = new ToolStripMenuItem("适应该列宽");
            sortAscending.Click += (s,e) => SetSort(contextColumnIndex, ListSortDirection.Ascending);
            sortDescending.Click += (s,e) => SetSort(contextColumnIndex, ListSortDirection.Descending);
            clearSort.Click += (s,e) => ClearSort();
            fitThisColumn.Click += (s,e) => FitColumn(contextColumnIndex);
            columnMenu.Items.Add(sortAscending);
            columnMenu.Items.Add(sortDescending);
            columnMenu.Items.Add(new ToolStripSeparator());
            columnMenu.Items.Add(clearSort);
            columnMenu.Items.Add(new ToolStripSeparator());
            columnMenu.Items.Add(fitThisColumn);
            columnMenu.Opening += (s,e) => {
                if (contextColumnIndex < 0 || contextColumnIndex >= grid.Columns.Count) { e.Cancel = true; return; }
                string columnName = grid.Columns[contextColumnIndex].Name;
                sortAscending.Checked = sortColumnName == columnName && sortDirection == ListSortDirection.Ascending;
                sortDescending.Checked = sortColumnName == columnName && sortDirection == ListSortDirection.Descending;
                clearSort.Enabled = sortDirection.HasValue;
            };
            grid.SelectionChanged += (s,e) => GridSelectionChanged();
            grid.CellMouseDown += ShowColumnMenu;
            grid.RowPostPaint += DrawRowNumber;
            grid.CellDoubleClick += (s,e) => {
                if (e.RowIndex < 0) return;
                var feature = (Feature)grid.Rows[e.RowIndex].Tag;
                EditRequested?.Invoke(feature);
                ReloadRows();
            };
            Controls.Add(grid); Controls.Add(status); Controls.Add(tools);
            ReloadRows(); FitColumns();
            Shown += (s,e) => ClearAutomaticFirstRowSelection();
            if (map != null) { map.SelectionChanged += MapSelectionChanged; map.LayersChanged += MapLayersChanged; }
            DialogLayout.Apply(this);
        }
        public void ReloadRows()
        {
            var current = grid.CurrentRow?.Tag as Feature;
            int first = grid.FirstDisplayedScrollingRowIndex;
            int horizontal = grid.HorizontalScrollingOffset;
            syncingSelection = true;
            try
            {
                grid.Rows.Clear();
                EnsureColumns();
                var selected = showSelectedOnly && map != null && map.Layers.Contains(layer)
                    ? new HashSet<Feature>(map.GetSelection(layer)) : null;
                var rows = new List<DataGridViewRow>(showSelectedOnly ? 0 : layer.FeatureClass.Features.Count);
                int displayIndex = 0;
                foreach (Feature feature in layer.FeatureClass.Features)
                {
                    if (showSelectedOnly && (selected == null || !selected.Contains(feature))) continue;
                    var values = new object[grid.Columns.Count];
                    for (int i = 0; i < layer.FeatureClass.Fields.Count; i++)
                        values[i] = feature.Attributes?.GetItem(i);
                    if (layer.FeatureClass.Fields.Count == 0) values[0] = "要素 " + (++displayIndex);
                    var row = new DataGridViewRow { Tag = feature };
                    row.CreateCells(grid, values);
                    rows.Add(row);
                }
                if (rows.Count > 0) grid.Rows.AddRange(rows.ToArray());
                ApplyCurrentSort();
                foreach (DataGridViewRow row in grid.Rows)
                    if (ReferenceEquals(row.Tag, current)) { grid.CurrentCell = row.Cells[0]; break; }
                if (current == null) grid.CurrentCell = null;
                grid.ClearSelection();
                if (first >= 0 && first < grid.Rows.Count) grid.FirstDisplayedScrollingRowIndex = first;
                grid.HorizontalScrollingOffset = horizontal;
            }
            finally { syncingSelection = false; }
            HighlightSelection();
        }

        private void EnsureColumns()
        {
            var fields = layer.FeatureClass.Fields;
            bool matches = fields.Count == grid.Columns.Count;
            if (fields.Count == 0)
                matches = grid.Columns.Count == 1 && grid.Columns[0].Name == "feature";
            else if (matches)
                for (int i = 0; i < fields.Count; i++)
                    if (!string.Equals(grid.Columns[i].Name, fields[i].Name, StringComparison.Ordinal))
                    { matches = false; break; }
            if (matches) return;

            var widths = grid.Columns.Cast<DataGridViewColumn>()
                .ToDictionary(c => c.Name, c => c.Width, StringComparer.Ordinal);
            grid.Columns.Clear();
            foreach (Field field in fields)
            {
                int width;
                grid.Columns.Add(new DataGridViewTextBoxColumn {
                    HeaderText = string.IsNullOrEmpty(field.Alias) ? field.Name : field.Alias,
                    Name = field.Name, MinimumWidth = 90,
                    Width = widths.TryGetValue(field.Name, out width) ? width : 130,
                    SortMode = DataGridViewColumnSortMode.Programmatic
                });
            }
            if (grid.Columns.Count == 0)
            {
                grid.Columns.Add("feature", "要素");
                grid.Columns[0].SortMode = DataGridViewColumnSortMode.Programmatic;
            }
            if (!string.IsNullOrEmpty(sortColumnName) && !grid.Columns.Contains(sortColumnName))
            {
                sortColumnName = null;
                sortDirection = null;
            }
            contextColumnIndex = -1;
        }
        private void ClearAutomaticFirstRowSelection()
        {
            syncingSelection = true;
            try
            {
                grid.CurrentCell = null;
                grid.ClearSelection();
            }
            finally { syncingSelection = false; }
            HighlightSelection();
            initialDisplayComplete = true;
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
            if (selectionOriginatedFromGrid && !showSelectedOnly) { UpdateRecordStatus(); return; }
            if (showSelectedOnly) ReloadRows(); else HighlightSelection();
        }
        private void MapLayersChanged(object sender, EventArgs e) { if (!map.Layers.Contains(layer)) Close(); }
        private void HighlightSelection()
        {
            var selected = map != null && map.Layers.Contains(layer)
                ? new HashSet<Feature>(map.GetSelection(layer)) : new HashSet<Feature>();
            syncingSelection = true;
            try
            {
                foreach (DataGridViewRow row in grid.Rows)
                {
                    bool shouldSelect = selected.Contains((Feature)row.Tag);
                    if (row.Selected != shouldSelect) row.Selected = shouldSelect;
                }
            }
            finally { syncingSelection = false; }
            UpdateRecordStatus();
        }
        private void GridSelectionChanged()
        {
            if (syncingSelection || !initialDisplayComplete) return;
            var features = new Features();
            foreach (DataGridViewRow row in grid.SelectedRows)
                if (row.Tag is Feature feature) features.Add(feature);
            selectionOriginatedFromGrid = true;
            try { RequestSelection(features); }
            finally { selectionOriginatedFromGrid = false; }
        }
        private void RequestSelection(Features features)
        {
            SelectionRequested?.Invoke(features);
            if (map == null) UpdateRecordStatus();
        }
        private void ZoomToCurrentSelection()
        {
            if (map == null || !map.Layers.Contains(layer)) return;
            Features features = map.GetSelection(layer);
            if (features.Count == 0) return;
            Envelope extent = features.GetEnvelope();
            if (!extent.IsNull) map.SetExtent(extent);
        }
        private void UpdateRecordStatus()
        {
            int selectedCount = map != null && map.Layers.Contains(layer)
                ? map.GetSelection(layer).Count : grid.SelectedRows.Count;
            recordStatus.Text = string.Format("显示 {0:N0} / 共 {1:N0} 条    已选择 {2:N0} 条",
                grid.Rows.Count, layer.FeatureClass.Features.Count, selectedCount);
            zoomToSelection.Enabled = selectedCount > 0;
        }
        private void ShowColumnMenu(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right || e.RowIndex != -1 || e.ColumnIndex < 0) return;
            contextColumnIndex = e.ColumnIndex;
            columnMenu.Show(Cursor.Position);
        }
        private void SetSort(int columnIndex, ListSortDirection direction)
        {
            if (columnIndex < 0 || columnIndex >= grid.Columns.Count) return;
            sortColumnName = grid.Columns[columnIndex].Name;
            sortDirection = direction;
            syncingSelection = true;
            try { ApplyCurrentSort(); }
            finally { syncingSelection = false; }
            HighlightSelection();
        }
        private void ClearSort()
        {
            sortColumnName = null;
            sortDirection = null;
            ReloadRows();
        }
        private void FitColumn(int columnIndex)
        {
            if (columnIndex < 0 || columnIndex >= grid.Columns.Count) return;
            grid.AutoResizeColumn(columnIndex, DataGridViewAutoSizeColumnMode.DisplayedCells);
            grid.Columns[columnIndex].Width = Math.Max(90, Math.Min(grid.Columns[columnIndex].Width, 300));
        }
        private void ApplyCurrentSort()
        {
            foreach (DataGridViewColumn column in grid.Columns)
                column.HeaderCell.SortGlyphDirection = SortOrder.None;
            if (string.IsNullOrEmpty(sortColumnName) || !sortDirection.HasValue ||
                !grid.Columns.Contains(sortColumnName) || grid.Rows.Count < 2) return;
            DataGridViewColumn sorted = grid.Columns[sortColumnName];
            grid.Sort(sorted, sortDirection.Value);
            sorted.HeaderCell.SortGlyphDirection = sortDirection == ListSortDirection.Ascending
                ? SortOrder.Ascending : SortOrder.Descending;
        }
        private void DrawRowNumber(object sender, DataGridViewRowPostPaintEventArgs e)
        {
            string text = (e.RowIndex + 1).ToString();
            Rectangle bounds = new Rectangle(e.RowBounds.Left, e.RowBounds.Top,
                grid.RowHeadersWidth - 7, e.RowBounds.Height);
            TextRenderer.DrawText(e.Graphics, text, grid.Font, bounds,
                Color.FromArgb(80, 80, 80), TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                columnMenu.Dispose();
                if (map != null) { map.SelectionChanged -= MapSelectionChanged; map.LayersChanged -= MapLayersChanged; }
            }
            base.Dispose(disposing);
        }
    }
}
