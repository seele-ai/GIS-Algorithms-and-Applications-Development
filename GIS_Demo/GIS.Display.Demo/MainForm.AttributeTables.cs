using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;
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
                workspaceSplit.Panel2Collapsed = false;
                bottomTabs.SelectedTab = (TabPage)existing.Parent;
                return;
            }

            var page = new TabPage(layer.Name);
            var table = new AttributeTableForm(layer, Map) {
                TopLevel = false, FormBorderStyle = FormBorderStyle.None, Dock = DockStyle.Fill
            };
            attributeTables[layer] = table;
            table.SelectionRequested += selection =>
                Map.SetSelection(layer, selection, SelectMethodConstant.CreateNew);
            table.EditRequested += feature => EditAttributeTableFeature(layer, feature);
            table.AddFieldRequested += () => AddAttributeField(layer);
            table.SaveEditsRequested += SavePendingEdits;
            table.FormClosed += (sender, args) => {
                attributeTables.Remove(layer);
                bottomTabs.TabPages.Remove(page);
                page.Dispose();
                if (attributeTables.Count == 0) workspaceSplit.Panel2Collapsed = true;
            };
            page.Controls.Add(table);
            bottomTabs.TabPages.Add(page);
            bottomTabs.SelectedTab = page;
            workspaceSplit.Panel2Collapsed = false;
            table.Show();
            SetBottomHeight(300);
        }

        private void AddAttributeField(Layer layer)
        {
            try
            {
                if (Map.IsCreating) throw new InvalidOperationException("请先完成或退出当前创建。");
                if (!Map.Layers.Contains(layer)) throw new InvalidOperationException("目标图层已移除。");
                using (var dialog = new AddFieldForm(layer.FeatureClass.Fields))
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                        RememberEdit(layer, AddFieldEditCommand.Create(layer, dialog.ResultField, dialog.DefaultValue));
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is OverflowException)
            {
                MessageBox.Show(this, ex.Message, "添加字段", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
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

        private Rectangle AttributeTabCloseBounds(int index)
        {
            Rectangle tab = bottomTabs.GetTabRect(index);
            return new Rectangle(tab.Right - 23, tab.Top + (tab.Height - 16) / 2, 16, 16);
        }

        private void DrawBottomTab(object sender, DrawItemEventArgs e)
        {
            var page = bottomTabs.TabPages[e.Index];
            bool active = (e.State & DrawItemState.Selected) != 0;
            if (TabRenderer.IsSupported)
                TabRenderer.DrawTabItem(e.Graphics, e.Bounds, active ? TabItemState.Selected : TabItemState.Normal);
            else
            {
                e.Graphics.FillRectangle(SystemBrushes.Control, e.Bounds);
                e.Graphics.DrawRectangle(SystemPens.ControlDark, e.Bounds);
            }

            Rectangle textBounds = e.Bounds;
            textBounds.X += 9;
            textBounds.Width -= 35;
            TextRenderer.DrawText(e.Graphics, page.Text, bottomTabs.Font, textBounds,
                SystemColors.ControlText, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            Rectangle close = AttributeTabCloseBounds(e.Index);
            using (var pen = new Pen(SystemColors.ControlText, 1.5f))
            {
                e.Graphics.DrawLine(pen, close.Left + 4, close.Top + 4, close.Right - 5, close.Bottom - 5);
                e.Graphics.DrawLine(pen, close.Right - 5, close.Top + 4, close.Left + 4, close.Bottom - 5);
            }
        }

        private void BottomTabsMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            for (int i = 0; i < bottomTabs.TabPages.Count; i++)
            {
                if (!AttributeTabCloseBounds(i).Contains(e.Location)) continue;
                CloseAttributeTable(bottomTabs.TabPages[i]);
                return;
            }
        }

        private void BottomTabsMouseMove(object sender, MouseEventArgs e)
        {
            bottomTabs.Cursor = Enumerable.Range(0, bottomTabs.TabPages.Count)
                .Any(i => AttributeTabCloseBounds(i).Contains(e.Location)) ? Cursors.Hand : Cursors.Default;
        }

        private static void CloseAttributeTable(TabPage page)
        {
            var table = page.Controls.OfType<AttributeTableForm>().FirstOrDefault();
            table?.Close();
        }
    }
}
