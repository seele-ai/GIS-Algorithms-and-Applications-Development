using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace GIS.Display.Demo
{
    public sealed partial class MainForm
    {
        private readonly HashSet<Layer> unsavedLayers = new HashSet<Layer>();
        private ToolStripButton saveEditsButton;
        public bool HasUnsavedEdits(Layer layer) => unsavedLayers.Contains(layer);
        private void MarkEdited(Layer layer)
        {
            unsavedLayers.Add(layer); UpdateSaveButton();
        }
        private void UpdateSaveButton()
        {
            if (saveEditsButton == null) return;
            bool pending = unsavedLayers.Any(l => Map.Layers.Contains(l)) || editUndo.Count > 0 || editRedo.Count > 0;
            saveEditsButton.Enabled = pending;
            saveEditsButton.Text = pending ? "保存编辑 *" : "保存编辑";
            saveEditsButton.ToolTipText = "确认当前页面的修改并清空撤销、重做记录（Ctrl+S）。";
        }
        private void BuildSaveEditingButton()
        {
            saveEditsButton = new ToolStripButton("保存编辑") { Enabled = false };
            saveEditsButton.Click += (s,e) => SavePendingEdits();
            editToolbar.Items.Add(saveEditsButton);
            Map.LayersChanged += (s,e) => UpdateSaveButton();
        }
        /// <summary>以当前页面数据为新的编辑基线，清空全部编辑历史，不读写文件。</summary>
        public void CommitEdits()
        {
            if (Map.IsCreating) throw new InvalidOperationException("请先完成或退出当前草图，再保存编辑。");
            editUndo.Clear(); editRedo.Clear(); unsavedLayers.Clear(); UpdateSaveButton();
            EditingCompleted("编辑已保存到当前页面，之前的操作不能再撤销或重做。需要文件时请使用“导出要素”。");
        }
        private void SavePendingEdits()
        {
            try { CommitEdits(); }
            catch (InvalidOperationException ex) { MessageBox.Show(this,ex.Message,"保存编辑"); }
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.S)) { SavePendingEdits(); return true; }
            return base.ProcessCmdKey(ref msg,keyData);
        }
    }
}
