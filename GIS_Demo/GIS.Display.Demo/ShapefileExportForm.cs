using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using GIS.IO;

namespace GIS.Display.Demo
{
    /// <summary>模块4：选择整层，另存为标准 WGS84 Shapefile。</summary>
    internal sealed class ShapefileExportForm : Form
    {
        private readonly ComboBox layers = new ComboBox { Left = 20, Top = 42, Width = 440, DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = "Name" };
        public ShapefileExportForm(System.Collections.Generic.IEnumerable<Layer> items)
        {
            Text = "导出 SHP";
            Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);
            ClientSize = new System.Drawing.Size(480, 205);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            Controls.Add(new Label { Text = "选择要导出的图层（全部要素）", Left = 20, Top = 18, AutoSize = true });
            Controls.Add(layers);
            foreach (Layer layer in items.Reverse()) layers.Items.Add(layer);
            if (layers.Items.Count > 0) layers.SelectedIndex = 0;
            Controls.Add(new Label { Left = 20, Top = 82, Width = 440, Height = 50,
                Text = "输出坐标系：WGS84 经纬度，不随地图显示投影改变。\r\n同时保存 SHP、SHX、DBF、PRJ、CPG；属性采用 UTF-8。" });
            var export = new Button { Text = "选择保存位置…", Left = 220, Top = 153, Width = 145, Height = 30, Enabled = layers.Items.Count > 0 };
            export.Click += (s, e) => Export();
            var cancel = new Button { Text = "关闭", Left = 375, Top = 153, Width = 85, Height = 30, DialogResult = DialogResult.Cancel };
            Controls.Add(export); Controls.Add(cancel); AcceptButton = export; CancelButton = cancel;
            GIS.Display.UI.DialogLayout.Apply(this);
        }
        private void Export()
        {
            var layer = layers.SelectedItem as Layer;
            if (layer == null) return;
            string name = string.Concat((layer.Name ?? "layer").Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            using (var dialog = new SaveFileDialog { Title = "导出 WGS84 Shapefile", Filter = "Shapefile (*.shp)|*.shp", DefaultExt = "shp", AddExtension = true,
                FileName = string.IsNullOrWhiteSpace(name) ? "layer.shp" : name + ".shp", OverwritePrompt = false, RestoreDirectory = true })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                string path = Path.ChangeExtension(dialog.FileName, ".shp");
                string[] existing = ShapefileWriter.Extensions.Select(ext => Path.ChangeExtension(path, ext)).Where(File.Exists).ToArray();
                if (existing.Length > 0 && MessageBox.Show(this, "将覆盖以下已有文件：\r\n" + string.Join("\r\n", existing) + "\r\n\r\n是否继续？",
                    "确认覆盖 Shapefile 配套文件", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                try
                {
                    var renamed = ShapefileWriter.Write(path, layer, existing.Length > 0);
                    MessageBox.Show(this, "已导出 " + layer.FeatureClass.Features.Count + " 个要素（WGS84）。\r\n" + path +
                        (renamed.Count == 0 ? "" : "\r\n\r\nDBF 字段名最多10字节，已调整：\r\n" + string.Join("\r\n", renamed)), "导出成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "导出失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }
    }
}
