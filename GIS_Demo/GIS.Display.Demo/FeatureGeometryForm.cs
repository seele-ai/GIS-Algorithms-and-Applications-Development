using System;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace GIS.Display.Demo
{
    /// <summary>只操作副本；关闭/取消窗口不会修改原图层。</summary>
    public sealed class FeatureGeometryForm : Form
    {
        private readonly Geometry original;
        private Geometry working;
        private readonly ComboBox part = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly DataGridView grid = new DataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false };
        private readonly VertexPreview preview = new VertexPreview { Dock = DockStyle.Fill };
        private readonly TextBox dx = new TextBox { Width = 85, Text = "0" }, dy = new TextBox { Width = 85, Text = "0" };
        private bool loading;
        private int activePart;
        private readonly Label selectionHint = new Label { Dock = DockStyle.Bottom, Height = 30, TextAlign = ContentAlignment.MiddleLeft };
        public Geometry Result { get; private set; }
        public FeatureGeometryForm(Geometry geographic)
        {
            if (geographic == null) throw new ArgumentException("空几何无法编辑。");
            original = geographic.Clone(); working = geographic.Clone();
            if (FeatureEditor.IsRing(working)) foreach (var ring in FeatureEditor.Parts(working)) if (ring.Count > 0 && !ring[0].Equals(ring[ring.Count-1])) ring.Add(ring[0]);
            Text = "编辑要素几何"; Font = new Font("Microsoft YaHei UI", 9); ClientSize = new Size(960, 580); MinimumSize = new Size(800,480); StartPosition = FormStartPosition.CenterParent;
            var hint = new Label { Dock = DockStyle.Top, Height = 56, Padding = new Padding(8), Text = "坐标采用 WGS84 经纬度（度）。选择部件后修改坐标，或在右侧拖动顶点。蓝色圆点为选中顶点。\n插入后自动选中新顶点；点击底部“保存编辑”应用修改，再点击主界面“保存编辑”确认修改并清空撤销记录。" };
            var split = new SplitContainer { Dock = DockStyle.Fill, Size = new Size(960,480), SplitterDistance = 380 };
            grid.Columns.Add("longitude", "经度 X（°）"); grid.Columns.Add("latitude", "纬度 Y（°）");
            split.Panel1.Controls.Add(grid); split.Panel1.Controls.Add(part); split.Panel2.Controls.Add(preview);
            var controls = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 80, Padding = new Padding(6), AutoScroll = true };
            var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(6), FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
            AddButton(controls,"插入顶点", () => ChangeVertex(true)); AddButton(controls,"删除顶点", () => ChangeVertex(false));
            controls.Controls.Add(new Label { Text = "整体移动 Δ经度", AutoSize = true, Padding = new Padding(0,6,0,0) }); controls.Controls.Add(dx);
            controls.Controls.Add(new Label { Text = "Δ纬度", AutoSize = true, Padding = new Padding(0,6,0,0) }); controls.Controls.Add(dy);
            AddButton(controls,"应用移动", () => { ReadGrid(); working.Translate(Parse(dx.Text), Parse(dy.Text)); LoadGrid(); });
            AddButton(controls,"恢复原始", () => { working = original.Clone(); if (FeatureEditor.IsRing(working)) foreach (var ring in FeatureEditor.Parts(working)) if (ring.Count > 0 && !ring[0].Equals(ring[ring.Count-1])) ring.Add(ring[0]); LoadGrid(); });
            
            var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel }; footer.Controls.Add(cancel); CancelButton = cancel;
            AddButton(footer,"保存编辑", () => { ReadGrid(); FeatureEditor.Validate(working); Result = working.Clone(); DialogResult = DialogResult.OK; });
            part.Items.AddRange(PartNames(working)); part.SelectedIndexChanged += (s,e) => {
                if (loading) return;
                try { ReadGrid(); activePart = part.SelectedIndex; LoadGrid(); }
                catch (Exception ex) when (ex is ArgumentException || ex is FormatException || ex is OverflowException)
                { loading = true; part.SelectedIndex = activePart; loading = false; ShowError(ex); }
            };
            loading = true; part.SelectedIndex = 0; loading = false;
            grid.CurrentCellChanged += (s,e) => { if (!loading) SelectVertex(grid.CurrentRow?.Index ?? -1); };
            preview.Resize += (s,e) => { if (preview.IsHandleCreated) preview.BeginInvoke(new Action(() => { if (!preview.IsDisposed) preview.FullExtent(); })); };
            preview.VertexSelected = index => SelectVertex(index);
            preview.VertexMoved = () => LoadGrid(preview.SelectedVertex);
            preview.BeforeDrag = () => { try { ReadGrid(); return true; } catch (Exception ex) when (ex is ArgumentException || ex is FormatException || ex is OverflowException) { ShowError(ex); return false; } };
            Controls.Add(split); Controls.Add(selectionHint); Controls.Add(controls); Controls.Add(footer); Controls.Add(hint);
            LoadGrid(); Shown += (s,e) => { split.SplitterDistance = (int)(split.Width * 0.4); preview.FullExtent(); };
        }
        private static string[] PartNames(Geometry geometry)
        {
            if (geometry is Polygon polygon) return new[] { "外环" }.Concat(polygon.Holes.Select((h,i) => "孔洞 " + (i+1))).ToArray();
            if (geometry is MultiPolygon m) return m.Parts.SelectMany((p,i) => new[] { "面 " + (i+1) + " 外环" }.Concat(p.Holes.Select((h,j) => "面 " + (i+1) + " 孔洞 " + (j+1)))).ToArray();
            return FeatureEditor.Parts(geometry).Select((p,i) => "部件 " + (i+1)).ToArray();
        }
        private void AddButton(Control parent, string text, Action action)
        {
            var button = new Button { Text = text, AutoSize = true }; parent.Controls.Add(button);
            button.Click += (s,e) => { try { action(); } catch (Exception ex) when (ex is ArgumentException || ex is FormatException || ex is OverflowException) { ShowError(ex); } };
        }
        private void ShowError(Exception ex) => MessageBox.Show(this, ex.Message, "无法完成编辑", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        private static double Parse(object value)
        {
            double result = double.Parse(Convert.ToString(value, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            if (!FeatureEditor.Finite(result)) throw new ArgumentException("请输入有限数值。"); return result;
        }
        private Points ActivePoints => FeatureEditor.Parts(working).ElementAt(activePart);
        private void ReadGrid()
        {
            grid.EndEdit();
            var coordinates = grid.Rows.Cast<DataGridViewRow>().Select(r => new Coordinate(Parse(r.Cells[0].Value), Parse(r.Cells[1].Value))).ToArray();
            if (working is GIS.Point point) { if (coordinates.Length != 1) throw new ArgumentException("点要素必须包含一个顶点。"); point.Coordinate = coordinates[0]; preview.SetGeometry(working,activePart); return; }
            var points = ActivePoints; points.Clear(); foreach (var c in coordinates) points.Add(c);
            if (FeatureEditor.IsRing(working) && points.Count > 0) points.Add(points[0]);
            preview.SetGeometry(working,activePart);
        }
        private void SelectVertex(int index)
        {
            if (index < 0 || index >= grid.Rows.Count) return;
            loading = true; grid.ClearSelection(); if (grid.CurrentRow?.Index != index) grid.CurrentCell = grid.Rows[index].Cells[0]; grid.Rows[index].Selected = true; loading = false;
            preview.SelectedVertex = index; preview.Invalidate();
            selectionHint.Text = "当前：" + part.SelectedItem + " · 顶点 " + (index+1) + "（蓝色高亮）；可拖动或修改经纬度。";
        }
        private void LoadGrid(int selectedIndex = -1)
        {
            if (selectedIndex < 0) selectedIndex = grid.CurrentRow?.Index ?? 0;
            loading = true; grid.Rows.Clear(); var points = ActivePoints;
            int count = points.Count;
            if (FeatureEditor.IsRing(working) && count > 1 && points[0].Equals(points[count-1])) count--;
            for (int i = 0; i < count; i++) grid.Rows.Add(points[i].X.ToString("R",CultureInfo.InvariantCulture),points[i].Y.ToString("R",CultureInfo.InvariantCulture));
            for (int i = 0; i < grid.Rows.Count; i++) grid.Rows[i].HeaderCell.Value = (i+1).ToString();
            grid.RowHeadersWidth = 55;
            loading = false; preview.SetGeometry(working,activePart);
            SelectVertex(Math.Min(selectedIndex,grid.Rows.Count-1));
        }
        private void ChangeVertex(bool insert)
        {
            if (working is GIS.Point) throw new ArgumentException("单点要素只能移动或修改坐标。");
            int index = grid.CurrentRow?.Index ?? 0; ReadGrid(); var points = ActivePoints;
            bool ring = FeatureEditor.IsRing(working); int count = points.Count - (ring ? 1 : 0);
            if (!insert && count <= (ring ? 3 : working is MultiPoint ? 1 : 2)) throw new ArgumentException("删除后顶点不足，不能删除该顶点。");
            if (ring) points.RemoveAt(points.Count-1);
            if (insert)
            {
                var a = points[index]; var b = points[(index+1)%points.Count];
                points.Insert(index+1, new Coordinate((a.X+b.X)/2,(a.Y+b.Y)/2));
            }
            else points.RemoveAt(index);
            if (ring) points.Add(points[0]); LoadGrid(insert ? index+1 : index);
        }
        private sealed class VertexPreview : MapControl
        {
            private Geometry geometry;
            private Layer layer;
            private int partIndex, vertex = -1;
            public int SelectedVertex { get; set; } = -1;
            public Action<int> VertexSelected;
            public Action VertexMoved;
            public Func<bool> BeforeDrag;
            public VertexPreview()
            {
                OverlayPaint += (s,e) => {
                    if (geometry == null) return;
                    var points = FeatureEditor.Parts(geometry).ElementAt(partIndex);
                    int count = points.Count - (FeatureEditor.IsRing(geometry) ? 1 : 0);
                    for (int i = 0; i < count; i++)
                    {
                        var screen = Transform.MapToScreen(points[i]);
                        e.Graphics.FillRectangle(Brushes.OrangeRed,screen.X-3,screen.Y-3,7,7);
                        e.Graphics.DrawString((i+1).ToString(),Font,Brushes.Black,screen.X+8,screen.Y+5);
                    }
                    if (SelectedVertex >= 0 && SelectedVertex < count)
                    {
                        var screen = Transform.MapToScreen(points[SelectedVertex]);
                        e.Graphics.FillEllipse(Brushes.White,screen.X-9,screen.Y-9,18,18);
                        e.Graphics.FillEllipse(Brushes.DodgerBlue,screen.X-6,screen.Y-6,12,12);
                        using (var pen = new Pen(Color.Navy,2)) e.Graphics.DrawEllipse(pen,screen.X-9,screen.Y-9,18,18);
                    }
                };
            }
            public void SetGeometry(Geometry value, int selectedPart)
            {
                geometry = value; partIndex = selectedPart;
                if (layer == null)
                {
                    var fc = new FeatureClass("编辑预览",value.GeometryType); fc.Add(new Feature(value,fc.Fields)); layer = new Layer(fc); AddLayer(layer);
                }
                else layer.FeatureClass.Features[0].Geometry = value;
                RefreshMap();
            }
            protected override void OnMouseDown(MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left && geometry != null)
                {
                    if (BeforeDrag != null && !BeforeDrag()) return;
                    var points = FeatureEditor.Parts(geometry).ElementAt(partIndex);
                    for (int i = 0; i < points.Count; i++)
                    {
                        var p = Transform.MapToScreen(points[i]);
                        if (Math.Abs(p.X-e.X) <= 7 && Math.Abs(p.Y-e.Y) <= 7) { vertex = FeatureEditor.IsRing(geometry) && i == points.Count-1 ? 0 : i; SelectedVertex = vertex; VertexSelected?.Invoke(vertex); Invalidate(); Capture = true; return; }
                    }
                }
                base.OnMouseDown(e);
            }
            protected override void OnMouseMove(MouseEventArgs e)
            {
                if (vertex < 0) { base.OnMouseMove(e); return; }
                var c = Transform.ScreenToMap(e.Location);
                if (geometry is GIS.Point p) p.Coordinate = c;
                else
                {
                    var points = FeatureEditor.Parts(geometry).ElementAt(partIndex); points[vertex] = c;
                    if (FeatureEditor.IsRing(geometry) && vertex == 0) points[points.Count-1] = c;
                }
                RefreshMap(); VertexMoved?.Invoke();
            }
            protected override void OnMouseUp(MouseEventArgs e)
            { if (vertex >= 0) { vertex = -1; Capture = false; return; } base.OnMouseUp(e); }
        }
    }
}
