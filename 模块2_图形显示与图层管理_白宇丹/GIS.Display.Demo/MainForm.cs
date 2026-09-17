using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace GIS.Display.Demo
{
    public sealed class MainForm : Form
    {
        public MapControl Map { get; } = new MapControl { Dock = DockStyle.Fill };
        private readonly LayerManagerControl layerManager = new LayerManagerControl { Dock = DockStyle.Fill };
        private readonly ToolStripStatusLabel scale = new ToolStripStatusLabel();
        private readonly ToolStripStatusLabel selected = new ToolStripStatusLabel();
        private readonly ToolStripStatusLabel position = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleRight };
        private readonly ListView selectionList = new ListView { Dock = DockStyle.Fill,
            View = View.Details, FullRowSelect = true, GridLines = true };
        private readonly ToolStrip toolbar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(8) };
        public MainForm()
        {
            Text = "GIS · 模块2 图形显示与图层管理 · 白宇丹";
            Font = new Font("Microsoft YaHei UI", 9F);
            ClientSize = new Size(1180, 760);
            MinimumSize = new Size(900, 600);
            StartPosition = FormStartPosition.CenterScreen;
            var heading = new Label { Text = "GIS  /  地图显示与图层管理", Dock = DockStyle.Top, Height = 55,
                BackColor = Color.FromArgb(126, 36, 47), ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 16, FontStyle.Bold),
                Padding = new Padding(18, 0, 0, 0), TextAlign = ContentAlignment.MiddleLeft };
            var hint = new Label { Text = "滚轮：以鼠标为中心缩放    中键：平移    选择工具：点选 / 拖框    Esc：取消本次拖动",
                Dock = DockStyle.Top, Height = 32, Padding = new Padding(12, 0, 0, 0),
                TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.FromArgb(247, 243, 239) };
            AddMode("平移", MapInteractionMode.Pan);
            AddMode("放大 / 框放大", MapInteractionMode.ZoomIn);
            AddMode("缩小", MapInteractionMode.ZoomOut);
            AddMode("选择 / 框选", MapInteractionMode.Select);
            toolbar.Items.Add(new ToolStripSeparator());
            AddAction("全图", () => Map.FullExtent());
            AddAction("清空选择", () => Map.ClearSelection());
            AddAction("重置样例", LoadSamples);
            toolbar.Items.Add(new ToolStripSeparator());
            toolbar.Items.Add(new ToolStripLabel("选择方式"));
            var selectMethod = new ToolStripComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 125 };
            selectMethod.Items.AddRange(new object[] { "新建选择", "添加到选择", "从选择中移除", "在选择中筛选" });
            selectMethod.SelectedIndex = 0;
            selectMethod.SelectedIndexChanged += (s, e) => Map.SelectionMethod = (SelectMethodConstant)selectMethod.SelectedIndex;
            toolbar.Items.Add(selectMethod);
            var split = new SplitContainer { Dock = DockStyle.Fill, Size = new Size(1180, 560),
                FixedPanel = FixedPanel.Panel1, SplitterDistance = 230, Panel1MinSize = 210, Panel2MinSize = 350 };
            split.Panel1.Controls.Add(layerManager);
            split.Panel2.Controls.Add(Map);
            var lower = new Panel { Dock = DockStyle.Bottom, Height = 130 };
            var label = new Label { Text = "选择结果", Dock = DockStyle.Top, Height = 25 };
            selectionList.Columns.Add("图层", 230);
            selectionList.Columns.Add("名称", 280);
            selectionList.Columns.Add("几何类型", 180);
            lower.Controls.Add(selectionList);
            lower.Controls.Add(label);
            var status = new StatusStrip();
            status.Items.Add(scale);
            status.Items.Add(selected);
            status.Items.Add(position);
            Controls.Add(split);
            Controls.Add(lower);
            Controls.Add(hint);
            Controls.Add(toolbar);
            Controls.Add(heading);
            Controls.Add(status);
            layerManager.Bind(Map);
            Map.ViewChanged += (s, e) => UpdateStatus();
            Map.SelectionChanged += (s, e) => UpdateSelection();
            Map.LayersChanged += (s, e) => UpdateStatus();
            Map.MapMouseMoved += (s, e) => position.Text = $"X {e.Coordinate.X:F2}   Y {e.Coordinate.Y:F2}  米";
            Shown += (s, e) => LoadSamples();
        }
        public void LoadSamples()
        {
            foreach (Layer layer in Map.Layers.ToArray()) Map.RemoveLayer(layer);
            foreach (Layer layer in SampleData.Create()) Map.AddLayer(layer);
            Map.FullExtent();
            UpdateSelection();
        }
        private void AddAction(string text, Action action)
        {
            var button = new ToolStripButton(text);
            button.Click += (s, e) => action();
            toolbar.Items.Add(button);
        }
        private void AddMode(string text, MapInteractionMode mode)
        {
            var button = new ToolStripButton(text) { Tag = mode, Checked = mode == MapInteractionMode.Pan };
            button.Click += (s, e) => {
                Map.InteractionMode = mode;
                foreach (ToolStripButton other in toolbar.Items.OfType<ToolStripButton>())
                    if (other.Tag is MapInteractionMode) other.Checked = Equals(other.Tag, mode);
            };
            toolbar.Items.Add(button);
        }
        private void UpdateStatus()
        {
            scale.Text = $"比例尺 1 : {Map.Transform.MapScale:N0}    |    图层 {Map.Layers.Count}    ";
            selected.Text = $"选中 {Map.Layers.Sum(l => Map.GetSelection(l).Count)} 个要素";
        }
        private void UpdateSelection()
        {
            selectionList.BeginUpdate();
            selectionList.Items.Clear();
            foreach (Layer layer in Map.Layers)
                foreach (Feature feature in Map.GetSelection(layer))
                    selectionList.Items.Add(new ListViewItem(new[] { layer.Name,
                        Convert.ToString(feature.Attributes?.GetItem("名称")), feature.Geometry.GeometryType.ToString() }));
            selectionList.EndUpdate();
            UpdateStatus();
        }
    }
}
