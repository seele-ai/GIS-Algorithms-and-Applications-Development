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
        private readonly ComboBox targetSrid = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        private readonly ComboBox mode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        private readonly ProgressBar progress = new ProgressBar { Dock = DockStyle.Fill, Minimum = 0 };
        private readonly Label status = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Text = "等待上传" };
        private readonly Button export = new Button
        {
            Text = "上传",
            AutoSize = false,
            Size = new Size(104, 32),
            MinimumSize = new Size(104, 32)
        };
        private readonly Button cancel = new Button
        {
            Text = "取消",
            AutoSize = false,
            Size = new Size(116, 32),
            MinimumSize = new Size(116, 32)
        };
        private CancellationTokenSource cancellation;

        public PostGISExportForm(PostGISConnection connection, IEnumerable<Layer> mapLayers)
        {
            this.connection = connection ?? throw new ArgumentNullException(nameof(connection));
            Text = "上传图层到 PostGIS";
            Font = new Font("Microsoft YaHei UI", 10F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(540, 360);
            MinimumSize = new Size(540, 360);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;

            targetSrid.Items.AddRange(new object[]
            {
                new CoordinateSystemOption("WGS 84（EPSG:4326，推荐）", 4326),
                new CoordinateSystemOption("Web Mercator（EPSG:3857）", 3857),
                new CoordinateSystemOption("CGCS2000（EPSG:4490）", 4490)
            });
            targetSrid.SelectedIndex = 0;

            mode.Items.AddRange(new object[] { "创建新表（已存在则提示）", "替换已有表", "向已有表追加数据" });
            mode.SelectedIndex = 0;
            foreach (Layer layer in mapLayers.Reverse()) layers.Items.Add(layer);
            layers.SelectedIndexChanged += (s, e) => SuggestTableName();
            if (layers.Items.Count > 0) layers.SelectedIndex = 0;

            var fields = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14, 12, 14, 4), ColumnCount = 2, RowCount = 5 };
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 5; i++) fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            AddRow(fields, 0, "上传图层", layers);
            AddRow(fields, 1, "Schema", schema);
            AddRow(fields, 2, "数据表名", table);
            AddRow(fields, 3, "存储坐标系", targetSrid);
            AddRow(fields, 4, "同名表处理", mode);
            var progressPanel = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14, 4, 14, 4), ColumnCount = 2 };
            progressPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
            progressPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
            progressPanel.Controls.Add(progress, 0, 0);
            progressPanel.Controls.Add(status, 1, 0);
            var buttons = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(14, 7, 14, 7),
                ColumnCount = 3,
                RowCount = 1
            };
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116));
            buttons.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            export.Anchor = AnchorStyles.None;
            cancel.Anchor = AnchorStyles.None;
            buttons.Controls.Add(export, 1, 0);
            buttons.Controls.Add(cancel, 2, 0);
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

            var content = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 220));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            content.Controls.Add(fields, 0, 0);
            content.Controls.Add(progressPanel, 0, 1);
            content.Controls.Add(buttons, 0, 2);
            Controls.Add(content);
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
            PostGISColumnNames newTableColumns;
            try
            {
                source = CreateWgs84FeatureClass(layer);
                newTableColumns = exportMode == PostGISExportMode.Append
                    ? null : PostGISSchemaMapper.ResolveSystemColumnNames(source.Fields);
            }
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
                CoordinateSystemOption coordinateSystem = targetSrid.SelectedItem as CoordinateSystemOption;
                if (coordinateSystem == null)
                    throw new InvalidOperationException("请选择存储坐标系。");
                int written = await Task.Run(() => new PostGISExporter(connection).ExportFeatureClass(
                    source, tableName, 4326, coordinateSystem.Srid, exportMode, schemaName,
                    cancellation.Token, report));
                progress.Value = progress.Maximum;
                status.Text = "上传完成";
                string columnNotice = newTableColumns == null ? string.Empty :
                    "\r\n数据库主键：" + newTableColumns.IdColumnName +
                    "；Geometry 列：" + newTableColumns.GeometryColumnName + "。";
                MessageBox.Show(this, "已向 " + schemaName + "." + tableName + " 写入 " + written + " 个要素。" +
                    columnNotice,
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
            cancel.Text = busy ? "取消上传" : "取消";
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

        private sealed class CoordinateSystemOption
        {
            public CoordinateSystemOption(string displayName, int srid)
            {
                DisplayName = displayName;
                Srid = srid;
            }

            public string DisplayName { get; }
            public int Srid { get; }

            public override string ToString()
            {
                return DisplayName;
            }
        }
    }
}
