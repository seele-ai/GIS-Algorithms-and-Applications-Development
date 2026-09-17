using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace GIS.Display
{
    public sealed class LayerManagerControl : UserControl
    {
        private readonly TreeView tree = new TreeView { Dock = DockStyle.Fill, CheckBoxes = true,
            HideSelection = false, FullRowSelect = true };
        private readonly Button selectable = new Button { Text = "切换可选", AutoSize = true };
        private MapControl map;
        private bool updating;
        public Layer SelectedLayer => tree.SelectedNode?.Tag as Layer;

        public LayerManagerControl()
        {
            var title = new Label { Text = "图层  ·  上层覆盖下层", Dock = DockStyle.Top, Height = 36,
                TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 0, 0, 0) };
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 78, Padding = new Padding(4) };
            AddButton(buttons, "上移", () => MoveSelectedLayer(1));
            AddButton(buttons, "下移", () => MoveSelectedLayer(-1));
            AddButton(buttons, "移除", () => { if (SelectedLayer != null) map.RemoveLayer(SelectedLayer); });
            AddButton(buttons, "定位", () => {
                if (SelectedLayer != null && !SelectedLayer.GetEnvelope().IsNull)
                    map.SetExtent(SelectedLayer.GetEnvelope());
            });
            buttons.Controls.Add(selectable);
            selectable.Click += (s, e) => {
                if (SelectedLayer != null) map.SetLayerSelectable(SelectedLayer, !map.IsLayerSelectable(SelectedLayer));
            };
            Controls.Add(tree);
            Controls.Add(buttons);
            Controls.Add(title);
            tree.AfterCheck += (s, e) => {
                if (!updating && map != null) map.SetLayerVisible((Layer)e.Node.Tag, e.Node.Checked);
            };
        }
        public void Bind(MapControl control)
        {
            if (map != null) map.LayersChanged -= OnLayersChanged;
            map = control;
            if (map != null) map.LayersChanged += OnLayersChanged;
            Rebuild();
        }
        private void OnLayersChanged(object sender, EventArgs e) => Rebuild();
        private void Rebuild()
        {
            Layer selected = SelectedLayer;
            updating = true;
            tree.BeginUpdate();
            try
            {
                tree.Nodes.Clear();
                if (map == null) return;
                foreach (Layer layer in map.Layers.Reverse())
                {
                    var node = new TreeNode(layer.Name + (map.IsLayerSelectable(layer) ? "" : " [不可选]")) {
                        Tag = layer, Checked = layer.Visible
                    };
                    tree.Nodes.Add(node);
                    if (layer == selected) tree.SelectedNode = node;
                }
                if (tree.SelectedNode == null && tree.Nodes.Count > 0) tree.SelectedNode = tree.Nodes[0];
            }
            finally { tree.EndUpdate(); updating = false; }
        }
        private void MoveSelectedLayer(int delta)
        {
            if (SelectedLayer == null) return;
            int index = map.Layers.ToList().IndexOf(SelectedLayer);
            int target = index + delta;
            if (target >= 0 && target < map.Layers.Count) map.MoveLayer(SelectedLayer, target);
        }
        private static void AddButton(FlowLayoutPanel panel, string text, Action action)
        {
            var button = new Button { Text = text, AutoSize = true, Width = 54 };
            button.Click += (s, e) => action();
            panel.Controls.Add(button);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && map != null) map.LayersChanged -= OnLayersChanged;
            base.Dispose(disposing);
        }
    }
}
