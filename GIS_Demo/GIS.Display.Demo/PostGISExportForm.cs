using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GIS.PostGIS;

namespace GIS.Display.Demo
{
    /// <summary>将当前地图中的完整图层上传到已经连接的 PostGIS 数据库。</summary>
    internal sealed class PostGISExportForm : Form
    {
        private readonly PostGISConnection connection;
        private readonly ComboBox layers = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = "Name", Dock = DockStyle.Fill };
        private readonly TextBox schema = new TextBox { Text = PostGISSchemaMapper.DefaultSchemaName, Dock = DockStyle.Fill };
        private readonly TextBox table = new TextBox { Dock = DockStyle.Fill };
        private readonly NumericUpDown targetSrid = new NumericUpDown { Minimum = 1, Maximum = 999999, Value = 4326, Dock = DockStyle.Fill };
        private readonly ComboBox mode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        private readonly ProgressBar progress = new ProgressBar { Dock = DockStyle.Fill, Minimum = 0 };
        private readonly Label status = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Text = "等待上传" };
        private readonly Button export = new Button { Text = "开始上传", Width = 100, Height = 30 };
        private readonly Button cancel = new Button { Text = "关闭", Width = 90, Height = 30 };
        private CancellationTokenSource cancellation;

        public PostGISExportForm(PostGISConnection connection, IEnumerable<Layer> mapLayers)
        {
            this.connection = connection ?? throw new ArgumentNullException(nameof(connection));
            Text = "上传图层到 PostGIS";
            Font = new Font("Microsoft YaHei UI", 9F);
            ClientSize = new Size(500, 330);
            MinimumSize = new Size(500, 330);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;

            mode.Items.AddRange(new object[] { "新建表（同名时报错）", "覆盖同名表", "追加到已有表" });
            mode.SelectedIndex = 0;
            foreach (Layer layer in mapLayers.Reverse()) layers.Items.Add(layer);
            layers.SelectedIndexChanged += (s, e) => SuggestTableName();
            if (layers.Items.Count > 0) layers.SelectedIndex = 0;

            var fields = new TableLayoutPanel { Dock = DockStyle.Top, Height = 205, Padding = new Padding(14, 12, 14, 4), ColumnCount = 2, RowCount = 5 };
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 5; i++) fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 37));
            AddRow(fields, 0, "地图图层", layers);
            AddRow(fields, 1, "Schema", schema);
            AddRow(fields, 2, "目标表名", table);
            AddRow(fields, 3, "目标 SRID", targetSrid);
            AddRow(fields, 4, "写入方式", mode);

            var note = new Label
            {
                Dock = DockStyle.Top,
                Height = 43,
                Padding = new Padding(14, 3, 14, 0),
                Text = "上传使用图层保存的 WGS84 原始坐标；目标不是 EPSG:4326 时由 PostGIS 执行坐标转换。"
            };
            var progressPanel = new TableLayoutPanel { Dock = DockStyle.Top, Height = 32, Padding = new Padding(14, 3, 14, 3), ColumnCount = 2 };
            progressPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
            progressPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
            progressPanel.Controls.Add(progress, 0, 0);
            progressPanel.Controls.Add(status, 1, 0);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(14, 7, 14, 7), FlowDirection = FlowDirection.RightToLeft };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(export);
            export.Click += async (s, e) => await ExportAsync();
            cancel.Click += (s, e) =>
            {
                if (cancellation == null) Close();
                else cancellation.Cancel();
            };
            FormClosing += (s, e) =>
            {
                if (cancellation == null) return;
                cancellation.Cancel();
                e.Cancel = true;
            };

            Controls.Add(buttons);
            Controls.Add(progressPanel);
            Controls.Add(note);
            Controls.Add(fields);
            AcceptButton = export;
            CancelButton = cancel;
        }

        private static void AddRow(TableLayoutPanel panel, int row, string text, Control control)
        {
            panel.Controls.Add(new Label { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
            panel.Controls.Add(control, 1, row);
        }

        private void SuggestTableName()
        {
            Layer layer = layers.SelectedItem as Layer;
            if (layer == null) return;
            table.Text = string.IsNullOrWhiteSpace(layer.Name) ? "layer" : layer.Name.Trim();
        }

        private async Task ExportAsync()
        {
            Layer layer = layers.SelectedItem as Layer;
            string schemaName = schema.Text.Trim();
            string tableName = table.Text.Trim();
            if (layer == null || string.IsNullOrWhiteSpace(schemaName) || string.IsNullOrWhiteSpace(tableName))
            {
                MessageBox.Show(this, "请选择图层，并填写 Schema 和目标表名。", "上传 PostGIS", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            PostGISExportMode exportMode = (PostGISExportMode)mode.SelectedIndex;
            if (exportMode == PostGISExportMode.Overwrite &&
                MessageBox.Show(this, "将删除并重建目标表 " + schemaName + "." + tableName + "，是否继续？",
                    "确认覆盖", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;

            FeatureClass source;
            try { source = CreateWgs84FeatureClass(layer); }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "准备上传数据失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            cancellation = new CancellationTokenSource();
            SetBusy(true, source.Features.Count);
            var report = new Progress<int>(count =>
            {
                progress.Value = Math.Min(progress.Maximum, count);
                status.Text = "已写入 " + count + " / " + source.Features.Count;
            });
            try
            {
                int written = await Task.Run(() => new PostGISExporter(connection).ExportFeatureClass(
                    source, tableName, 4326, (int)targetSrid.Value, exportMode, schemaName,
                    cancellation.Token, report));
                progress.Value = progress.Maximum;
                status.Text = "上传完成";
                MessageBox.Show(this, "已向 " + schemaName + "." + tableName + " 写入 " + written + " 个要素。",
                    "上传成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (OperationCanceledException)
            {
                status.Text = "已取消，事务已回滚";
            }
            catch (Exception ex)
            {
                status.Text = "上传失败";
                MessageBox.Show(this, ex.Message, "上传 PostGIS 失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                cancellation.Dispose();
                cancellation = null;
                SetBusy(false, source.Features.Count);
            }
        }

        private void SetBusy(bool busy, int featureCount)
        {
            layers.Enabled = schema.Enabled = table.Enabled = targetSrid.Enabled = mode.Enabled = !busy;
            export.Enabled = !busy;
            cancel.Text = busy ? "取消上传" : "关闭";
            progress.Maximum = Math.Max(1, featureCount);
            if (busy)
            {
                progress.Value = 0;
                status.Text = "正在连接并写入…";
            }
        }

        private static FeatureClass CreateWgs84FeatureClass(Layer layer)
        {
            FeatureClass original = layer.FeatureClass;
            var result = new FeatureClass(original.Name, original.GeometryType) { Srid = 4326 };
            for (int i = 0; i < original.Fields.Count; i++)
            {
                Field field = original.Fields.GetItem(i);
                result.Fields.Add(new Field(field.Name, field.ValueType));
            }

            bool hasSnapshots = layer.HasGeographicGeometries;
            for (int i = 0; i < original.Features.Count; i++)
            {
                Feature originalFeature = original.Features.GetItem(i);
                Geometry geometry = hasSnapshots ? layer.GeographicGeometries[i]?.Clone() : originalFeature.Geometry?.Clone();
                if (geometry == null)
                    throw new InvalidOperationException("图层中存在空几何，无法上传。");
                if (!hasSnapshots && original.ProjectionCS != null)
                    Layer.TransformGeometry(geometry, original.ProjectionCS, null);
                object[] values = originalFeature.Attributes == null
                    ? new object[result.Fields.Count]
                    : originalFeature.Attributes.ToArray();
                result.Add(new Feature(geometry, new Attributes(result.Fields, values)));
            }
            return result;
        }
    }
}
