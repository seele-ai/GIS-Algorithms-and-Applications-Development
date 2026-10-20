using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace GIS.Display.Demo
{
    public sealed partial class MainForm
    {
        private sealed class EditHistory
        {
            public Layer Layer;
            public FeatureEditCommand Command;
        }
        private readonly Stack<EditHistory> editUndo = new Stack<EditHistory>();
        private readonly Stack<EditHistory> editRedo = new Stack<EditHistory>();
        private void BuildExistingEditingMenu()
        {
            layerManager.AttributeEditRequested += (layer, feature) => {
                try
                {
                    if (Map.IsCreating) throw new InvalidOperationException("请先完成或退出当前创建。");
                    if (!layer.Visible || !Map.IsLayerSelectable(layer)) throw new InvalidOperationException("目标图层必须可见且可选。");
                    using (var dialog = new NewFeatureAttributesForm(layer.FeatureClass.Fields, feature.Attributes))
                        if (dialog.ShowDialog(Form.ActiveForm ?? this) == DialogResult.OK)
                            RememberEdit(layer, FeatureEditCommand.Save(layer, feature, FeatureEditCommand.ReadGeometry(layer, feature), dialog.Values));
                }
                catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is OverflowException)
                { MessageBox.Show(this, ex.Message, "编辑属性", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            };
            layerManager.SaveEditsRequested += SavePendingEdits;
            var menu = new ToolStripDropDownButton("编辑");
            AddEditingMenuItem(menu,"编辑顶点 / 移动要素…", () => {
                var selection = EditingSelection(true); var feature = selection.Item2[0];
                using (var dialog = new FeatureGeometryForm(FeatureEditCommand.ReadGeometry(selection.Item1,feature)))
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                        RememberEdit(selection.Item1, FeatureEditCommand.Save(selection.Item1,feature,dialog.Result,feature.Attributes));
            });
            AddEditingMenuItem(menu,"编辑属性…", () => {
                var selection = EditingSelection(true); var feature = selection.Item2[0];
                using (var dialog = new NewFeatureAttributesForm(selection.Item1.FeatureClass.Fields,feature.Attributes))
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                        RememberEdit(selection.Item1,FeatureEditCommand.Save(selection.Item1,feature,FeatureEditCommand.ReadGeometry(selection.Item1,feature),dialog.Values));
            });
            AddEditingMenuItem(menu,"删除选中要素…", () => {
                var selection = EditingSelection(false);
                if (MessageBox.Show(this,"删除图层“" + selection.Item1.Name + "”中选中的 " + selection.Item2.Length + " 个要素？可通过“撤销编辑”恢复。","删除要素",MessageBoxButtons.YesNo,MessageBoxIcon.Question) == DialogResult.Yes)
                    RememberEdit(selection.Item1,FeatureEditCommand.Delete(selection.Item1,selection.Item2));
            });
            menu.DropDownItems.Add(new ToolStripSeparator());
            var undo = AddEditingMenuItem(menu,"撤销编辑", () => ReplayEdit(false));
            var redo = AddEditingMenuItem(menu,"重做编辑", () => ReplayEdit(true));
            menu.DropDownOpening += (s,e) => { undo.Enabled = editUndo.Count > 0; redo.Enabled = editRedo.Count > 0; };
            editToolbar.Items.Add(new ToolStripSeparator()); editToolbar.Items.Add(menu); editToolbar.Items.Add(new ToolStripSeparator());
        }
        private ToolStripMenuItem AddEditingMenuItem(ToolStripDropDownButton menu, string text, Action action)
        {
            var item = new ToolStripMenuItem(text); menu.DropDownItems.Add(item);
            item.Click += (s,e) => {
                try { if (Map.IsCreating) throw new InvalidOperationException("请先完成或退出当前创建，再编辑已有要素。"); action(); }
                catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is OverflowException)
                { MessageBox.Show(this,ex.Message,"编辑要素",MessageBoxButtons.OK,MessageBoxIcon.Warning); }
            };
            return item;
        }
        private Tuple<Layer,Feature[]> EditingSelection(bool single)
        {
            var layers = Map.Layers.Where(l => Map.GetSelection(l).Count > 0).ToArray();
            if (layers.Length != 1) throw new InvalidOperationException("请先选择同一图层中的要素；可用“操作/选择范围”限定图层。");
            var layer = layers[0];
            if (!layer.Visible || !Map.IsLayerSelectable(layer)) throw new InvalidOperationException("目标图层必须可见且可选。");
            var features = Map.GetSelection(layer).ToArray();
            if (single && features.Length != 1) throw new InvalidOperationException("编辑几何或属性时，请只选择一个要素。");
            return Tuple.Create(layer,features);
        }
        private void RememberEdit(Layer layer, FeatureEditCommand command)
        {
            editUndo.Push(new EditHistory { Layer = layer, Command = command }); editRedo.Clear(); MarkEdited(layer);
            EditingCompleted("修改已保存到内存，可撤销；点击主界面“保存编辑”或按 Ctrl+S 确认修改并清空撤销记录。");
        }
        private void ReplayEdit(bool redo)
        {
            var source = redo ? editRedo : editUndo; var destination = redo ? editUndo : editRedo;
            if (source.Count == 0) return;
            var edit = source.Peek();
            if (!Map.Layers.Contains(edit.Layer)) throw new InvalidOperationException("对应图层已移除，不能撤销或重做。");
            if (redo) edit.Command.Redo(); else edit.Command.Undo();
            source.Pop(); destination.Push(edit); MarkEdited(edit.Layer); EditingCompleted(redo ? "已重做编辑。" : "已撤销编辑。");
        }
        private void EditingCompleted(string message)
        {
            Map.RefreshMap(); layerManager.RefreshAttributeTables(); layerManager.Rebuild(); UpdateSelection(); UpdateStatus(); sketchHint.Text = message;
        }
    }
}
