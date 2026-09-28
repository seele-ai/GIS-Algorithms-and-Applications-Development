using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using ScreenPoint = System.Drawing.Point;

namespace GIS.Display.UI
{
    /// <summary>
    /// “投影到 WGS84”对话框：把图层数据从其当前地理坐标系（大地基准）转换到 WGS84。
    /// 系统默认只接受 WGS84 数据，来自北京54、GCJ-02 等其他坐标系的数据在导入后
    /// 通过本对话框完成基准转换（Bursa-Wolf 七参数，参数可在界面上填写）。
    /// </summary>
    public sealed class ProjectToWgs84Form : Form
    {
        private readonly Layer layer;
        private readonly bool projected;
        private readonly List<GeographicDatum> datums = GeographicDatum.All();
        private readonly ComboBox datumBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280 };
        private readonly NumericUpDown[] parameters = new NumericUpDown[7];
        private readonly Label estimate = new Label { Dock = DockStyle.Top, Height = 24, Padding = new Padding(12, 4, 0, 0) };
        private readonly Label current = new Label { Dock = DockStyle.Top, Height = 24, Padding = new Padding(12, 4, 0, 0) };
        private readonly Label note = new Label
        {
            Dock = DockStyle.Top, Height = 60, Padding = new Padding(12, 4, 12, 0),
            ForeColor = Color.FromArgb(90, 90, 90)
        };
        private Coordinate sample;

        /// <summary>用户在界面上确认的源地理坐标系（含填写的七参数）。</summary>
        public GeographicDatum SourceDatum { get; private set; }

        public ProjectToWgs84Form(Layer layer, bool projected)
        {
            this.layer = layer;
            this.projected = projected;
            SourceDatum = (layer.GeographicDatum ?? GeographicDatum.Wgs84).Clone();

            Text = "投影到 WGS84：" + layer.Name;
            Font = new Font("Microsoft YaHei UI", 9F);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(560, 430);
            MinimumSize = new Size(560, 430);
            Padding = new Padding(0);

            sample = SampleCoordinate();

            var hint = new Label
            {
                Dock = DockStyle.Top, Height = 66, Padding = new Padding(12, 8, 12, 0),
                Text = "本系统默认只接受 WGS84（GCS_WGS_1984）数据。若该图层数据来自其他大地坐标系，\r\n" +
                       "请选择其当前地理坐标系并填写本地区的转换参数，确定后将把全部要素转换到 WGS84。",
                TextAlign = ContentAlignment.TopLeft, BackColor = Color.FromArgb(247, 243, 239)
            };

            current.Text = string.Format("    图层要素数 {0}；当前坐标：{1}；已标记地理坐标系：{2}",
                layer.FeatureClass.Features.Count,
                projected ? "投影坐标（" + (layer.FeatureClass.ProjectionCS == null ? "-" : layer.FeatureClass.ProjectionCS.ProjCSName) + "）" : "经纬度",
                (layer.GeographicDatum ?? GeographicDatum.Wgs84).ShortName);

            var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, Padding = new Padding(12, 6, 0, 0), WrapContents = false };
            top.Controls.Add(new Label { Text = "该图层当前的地理坐标系", AutoSize = true, Margin = new Padding(0, 8, 6, 0) });
            foreach (GeographicDatum datum in datums) datumBox.Items.Add(datum.DisplayName);
            datumBox.SelectedIndex = 0;
            datumBox.SelectedIndexChanged += (s, e) => LoadParameters();
            top.Controls.Add(datumBox);

            var grid = new TableLayoutPanel { Dock = DockStyle.Top, Height = 150, ColumnCount = 4, Padding = new Padding(12, 6, 12, 0) };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            string[] captions =
            {
                "ΔX 平移（米）", "ΔY 平移（米）", "ΔZ 平移（米）",
                "X 旋转（角秒）", "Y 旋转（角秒）", "Z 旋转（角秒）", "尺度比（ppm）"
            };
            for (int i = 0; i < parameters.Length; i++)
            {
                parameters[i] = new NumericUpDown
                {
                    Minimum = i < 3 ? -2000m : (i < 6 ? -60m : -100m),
                    Maximum = i < 3 ? 2000m : (i < 6 ? 60m : 100m),
                    DecimalPlaces = i < 3 ? 3 : 6,
                    Increment = i < 3 ? 0.5m : 0.0001m,
                    Width = 100
                };
                parameters[i].ValueChanged += (s, e) => UpdateEstimate();
                grid.Controls.Add(new Label { Text = captions[i], AutoSize = true, Margin = new Padding(0, 8, 4, 0) }, (i % 2) * 2, i / 2);
                grid.Controls.Add(parameters[i], (i % 2) * 2 + 1, i / 2);
            }

            // 说明文字由 LoadParameters() 按所选坐标系填写（构造函数末尾调用）
            var ok = new Button { Text = "开始转换", DialogResult = DialogResult.OK, Width = 96 };
            var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Width = 88 };
            // 未手工改动参数时按所选坐标系的公开参数自动转换
            ok.Click += (s, e) => SourceDatum = BuildDatum();
            var btns = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 46, FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(0, 6, 12, 0) };
            btns.Controls.Add(cancel);
            btns.Controls.Add(ok);
            AcceptButton = ok; CancelButton = cancel;

            Controls.Add(estimate);
            Controls.Add(note);
            Controls.Add(grid);
            Controls.Add(top);
            Controls.Add(current);
            Controls.Add(hint);
            Controls.Add(btns);

            LoadParameters();
        }

        // 取图层经纬度原始数据的中心作为估算点位（与当前显示投影无关）
        private Coordinate SampleCoordinate()
        {
            Envelope envelope = layer.GetGeographicEnvelope();
            if (envelope.IsNull) envelope = layer.GetEnvelope();
            return envelope.IsNull ? new Coordinate(0, 0) : new Coordinate(envelope.CenterX, envelope.CenterY);
        }

        // 选择地理坐标系后把内置参数填入编辑框；国测局坐标系不用七参数，直接禁用参数区
        private void LoadParameters()
        {
            GeographicDatum datum = datums[Math.Max(0, datumBox.SelectedIndex)];
            bool useParameters = !datum.IsGcj02 && !datum.IsWgs84;
            double[] values = { datum.Dx, datum.Dy, datum.Dz, datum.Rx, datum.Ry, datum.Rz, datum.ScalePpm };
            for (int i = 0; i < parameters.Length; i++)
            {
                decimal v = (decimal)Math.Max((double)parameters[i].Minimum,
                    Math.Min((double)parameters[i].Maximum, values[i]));
                parameters[i].Value = v;
                parameters[i].Enabled = useParameters;
            }
            note.Text = datum.IsGcj02
                ? "GCJ-02 不是换椭球，而是在经纬度上做的非线性加密偏移，因此不需要七参数：\r\n" +
                  "按公开的国测局算法直接换算（境内偏移约 300~700 m，境外不偏移）。\r\n" +
                  "参数区已禁用，直接点“开始转换”即可。"
                : "参数含义：本基准 → WGS84（Bursa-Wolf 七参数，位置矢量法）。\r\n" +
                  "参数已按公开来源（EPSG 等）自动填好，通常直接点“开始转换”即可；如需更高精度可改为本地区实测参数。\r\n" +
                  "系统内置的坐标系都带有明确转换方式与出处，没有可靠转换参数的坐标系（西安80、CGCS2000）已从列表中移除。";
            UpdateEstimate();
        }

        private GeographicDatum BuildDatum()
        {
            GeographicDatum template = datums[Math.Max(0, datumBox.SelectedIndex)];
            var values = new double[parameters.Length];
            for (int i = 0; i < parameters.Length; i++) values[i] = (double)parameters[i].Value;
            return ComposeDatum(template, values);
        }

        /// <summary>
        /// 用模板坐标系 + 界面上填写的七参数组装出最终坐标系。
        /// **必须保留模板的类型标记**（如 IsGcj02），否则国测局坐标系会被当成“零参数的 WGS84”，
        /// 转换时直接原样返回、看起来“没有变化”。
        /// </summary>
        public static GeographicDatum ComposeDatum(GeographicDatum template, double[] values)
        {
            var datum = new GeographicDatum(template.GeoCSName, template.DisplayName, template.Ellipsoid,
                values[0], values[1], values[2], values[3], values[4], values[5], values[6])
            {
                ParameterSource = template.ParameterSource,
                IsApproximate = template.IsApproximate,
                IsGcj02 = template.IsGcj02
            };
            return datum;
        }

        private void UpdateEstimate()
        {
            GeographicDatum datum = BuildDatum();
            double meters = DatumTransform.EstimateShiftMeters(sample, datum);
            string kind = datum.IsGcj02 ? "国测局加密偏移" : "椭球 " + datum.Ellipsoid.SpheroidName + " → WGS_1984";
            estimate.Text = string.Format("    预计该图层在数据中心的坐标变化：约 {0:F1} 米（{1}）    来源：{2}",
                meters, kind, datum.ParameterSource);
        }
    }
}
