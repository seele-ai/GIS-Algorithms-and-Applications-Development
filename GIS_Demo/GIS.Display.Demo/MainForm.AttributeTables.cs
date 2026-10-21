using System;
using System.Linq;
using System.Windows.Forms;
using GIS.Display.UI;

namespace GIS.Display.Demo
{
    public sealed partial class MainForm
    {
        private void SetBottomHeight(int requestedHeight)
        {
            if (workspaceSplit == null || workspaceSplit.Height == 0) return;
            int height = Math.Min(requestedHeight, workspaceSplit.Height - workspaceSplit.Panel1MinSize - workspaceSplit.SplitterWidth);
            if (height < workspaceSplit.Panel2MinSize) return;
            workspaceSplit.SplitterDistance = workspaceSplit.Height - height - workspaceSplit.SplitterWidth;
        }

        private void OpenAttributeTable(Layer layer)
        {
            if (attributeTables.TryGetValue(layer, out var existing) && !existing.IsDisposed)
            {
                bottomTabs.SelectedTab = (TabPage)existing.Parent;
                return;
            }

            var page = new TabPage(layer.Name);
            var table = new AttributeTableForm(layer, Map) {
                TopLevel = false, FormBorderStyle = FormBorderStyle.None, Dock = DockStyle.Fill
            };
            attributeTables[layer] = table;
            table.FeatureSelected += feature => {
                var selection = new Features();
                selection.Add(feature);
                Map.SetSelection(layer, selection, SelectMethodConstant.CreateNew);
            };
            table.EditRequested += feature => EditAttributeTableFeature(layer, feature);
            table.SaveEditsRequested += SavePendingEdits;
            table.FormClosed += (sender, args) => {
                attributeTables.Remove(layer);
                bottomTabs.TabPages.Remove(page);
                page.Dispose();
                if (attributeTables.Count == 0) SetBottomHeight(130);
            };
            page.Controls.Add(table);
            bottomTabs.TabPages.Add(page);
            bottomTabs.SelectedTab = page;
            table.Show();
            SetBottomHeight(300);
        }

        private void EditAttributeTableFeature(Layer layer, Feature feature)
        {
            try
            {
                if (Map.IsCreating) throw new InvalidOperationException("请先完成或退出当前创建。");
                if (!layer.Visible || !Map.IsLayerSelectable(layer)) throw new InvalidOperationException("目标图层必须可见且可选。");
                using (var dialog = new NewFeatureAttributesForm(layer.FeatureClass.Fields, feature.Attributes))
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                        RememberEdit(layer, FeatureEditCommand.Save(layer, feature,
                            FeatureEditCommand.ReadGeometry(layer, feature), dialog.Values));
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is OverflowException)
            {
                MessageBox.Show(this, ex.Message, "编辑属性", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void RefreshAttributeTables()
        {
            foreach (var table in attributeTables.Values.ToArray())
                if (!table.IsDisposed) table.ReloadRows();
        }

        private void CloseSelectedAttributeTable()
        {
            var page = bottomTabs.SelectedTab;
            if (page == null || page == selectionTab) return;
            var table = page.Controls.OfType<AttributeTableForm>().FirstOrDefault();
            table?.Close();
        }
    }
}
