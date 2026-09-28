using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using GIS.Display.UI;

namespace GIS.Display.Demo
{
    public sealed class MainForm : Form
    {
        // 1度约111.32km（按纬度折算），用于把经纬度换算为地图比例尺所需的米
        private const double MetersPerDegree = 111320.0;

        public MapControl Map { get; } = new MapControl { Dock = DockStyle.Fill };
        private readonly LayerManagerControl layerManager = new LayerManagerControl { Dock = DockStyle.Fill };
        private readonly ToolStripStatusLabel scale = new ToolStripStatusLabel();
        private readonly ToolStripStatusLabel selected = new ToolStripStatusLabel();
        private readonly ToolStripStatusLabel position = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleRight };
        private readonly ListView selectionList = new ListView { Dock = DockStyle.Fill,
            View = View.Details, FullRowSelect = true, GridLines = true };
        private readonly ToolStrip toolbar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(8) };

        // 当前地图显示坐标系：null 表示经纬度（地理坐标），否则为选定投影
        private ProjectionCS currentProjection;
        private int currentProjectionIndex = -1;

        // 投影工具条与分带参数
        private ToolStripComboBox projectionCombo;
        private ToolStripButton zoneButton;
        private bool updatingProjection;
        private int gaussZone = 39;            // 高斯-克吕格带号（默认 3 度带 39 带，中央经线 117°E）
        private bool gaussIs3Degree = true;    // 是否 3 度带
        private int utmZone = 50;              // UTM 带号（默认 50 带）
        private int utmRow = 14;               // UTM 纬度行序号（14 = S 行：32°N~40°N，覆盖北京）

        public MainForm()
        {
            Text = "简易 GIS 系统";
            Font = new Font("Microsoft YaHei UI", 9F);
            ClientSize = new Size(1180, 760);
            MinimumSize = new Size(900, 600);
            StartPosition = FormStartPosition.CenterScreen;
            var heading = new Label { Text = "简易 GIS 系统", Dock = DockStyle.Top, Height = 55,
                BackColor = Color.FromArgb(126, 36, 47), ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 16, FontStyle.Bold),
                Padding = new Padding(18, 0, 0, 0), TextAlign = ContentAlignment.MiddleLeft };
            var hint = new Label { Text = "滚轮：以鼠标为中心缩放    中键：平移    选择工具：点选 / 拖框    Esc：取消本次拖动    顶部“投影”可切换显示坐标系；切换到高斯-克吕格 / UTM 后可点“选择分带”在地球上选取分带",
                Dock = DockStyle.Top, Height = 32, Padding = new Padding(12, 0, 0, 0),
                TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.FromArgb(247, 243, 239) };
            AddMode("平移", MapInteractionMode.Pan);
            AddMode("放大 / 框放大", MapInteractionMode.ZoomIn);
            AddMode("缩小", MapInteractionMode.ZoomOut);
            AddMode("选择 / 框选", MapInteractionMode.Select);
            toolbar.Items.Add(new ToolStripSeparator());
            AddAction("全图", () => ZoomToAllLayers());
            AddAction("清空选择", () => Map.ClearSelection());
            AddAction("重置样例", LoadSamples);
            toolbar.Items.Add(new ToolStripSeparator());
            toolbar.Items.Add(new ToolStripLabel("投影"));
            projectionCombo = new ToolStripComboBox { DropDownStyle = ComboBoxStyle.DropDownList, AutoSize = false, Width = 232 };
            projectionCombo.Items.AddRange(new object[] { "经纬度（度）", "高斯-克吕格", "UTM", "Lambert 中国" });
            projectionCombo.SelectedIndexChanged += (s, e) =>
            {
                if (!updatingProjection) SetProjection(projectionCombo.SelectedIndex, false);
            };
            toolbar.Items.Add(projectionCombo);
            zoneButton = new ToolStripButton("选择分带") { Enabled = false, ToolTipText = "选择高斯-克吕格中央经线 / UTM 分带" };
            zoneButton.Click += (s, e) => PickZone();
            toolbar.Items.Add(zoneButton);
            UpdateProjectionText();
            projectionCombo.SelectedIndex = 0;
            SetProjection(0, false);
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
            Map.MapMouseMoved += (s, e) => UpdatePosition(e.Coordinate);
            Shown += (s, e) => LoadSamples();
        }

        /// <summary>
        /// 加载样例图层。样例数据本身是 WGS84 经纬度，载入时先保存为图层的经纬度快照
        /// （固有属性），再按当前投影生成显示几何。
        /// </summary>
        public void LoadSamples()
        {
            foreach (Layer layer in Map.Layers.ToArray()) Map.RemoveLayer(layer);
            foreach (Layer layer in SampleData.Create())
            {
                layer.CaptureGeographic(null);        // 原始数据即经纬度
                layer.ApplyProjection(currentProjection);
                Map.AddLayer(layer);
            }
            Map.FullExtent();
            UpdateSelection();
        }

        #region 投影

        private void SetProjection(int index, bool force)
        {
            if (!force && index == currentProjectionIndex) return;
            ProjectionCS oldProjection = currentProjection;

            ProjectionCS newProjection = CreateProjection(index);
            // 投影的唯一数据来源是图层中保存的 WGS84 经纬度快照：每次都从经纬度重新投影，
            // 不做“投影 → 投影”的换算。因此反复切换投影不会累积误差，即使中途切到与数据不匹配的
            // 投影（例如用南半球 UTM 带显示北半球数据）也不会破坏数据，切回来即完全恢复。
            foreach (Layer layer in Map.Layers)
            {
                if (!layer.HasGeographicGeometries) layer.CaptureGeographic(oldProjection);
                layer.ApplyProjection(newProjection);
            }
            currentProjectionIndex = index;
            currentProjection = newProjection;
            Map.Transform.Mpu = newProjection == null ? MetersPerDegree : 1.0;

            // 视图自动对准所有图层（全图范围）：换投影后坐标数值可能整体平移上百万米，
            // 若不重新取景，符号就会跑到视野之外（表现为“移动得很远”）。
            ZoomToAllLayers();
            if (zoneButton != null) zoneButton.Enabled = index == 1 || index == 2;
            // 切换投影后立即重绘，不必等用户再拖动或点击
            Map.RefreshMap();
            Map.Update();
            UpdateStatus();
        }

        /// <summary>把界面取景到所有图层（正在显示的图层）的整体范围，四周留 5% 边距。</summary>
        private void ZoomToAllLayers()
        {
            if (Map.Layers.Count == 0) return;
            try
            {
                Map.FullExtent();
            }
            catch (ArgumentException)
            {
                // 极端情况下范围无效时保留原视图，但后续仍会重绘
            }
        }

        /// <summary>打开分带选择界面（球体可视化），并把结果应用到当前投影。</summary>
        private void PickZone()
        {
            int index = projectionCombo.SelectedIndex;
            if (index != 1 && index != 2) return;
            bool utm = index == 2;
            Envelope data = DataExtentLngLat();
            using (var form = new ZonePickerForm(utm))
            {
                if (utm) { form.Zone = utmZone; form.RowIndex = utmRow; }
                else { form.Is3Degree = gaussIs3Degree; form.Zone = gaussZone; }
                // 传入所有要素的外包矩形：绘制在球面上并把地球移到数据所在位置，便于选取
                form.DataExtent = data;
                if (form.ShowDialog(this) != DialogResult.OK) return;
                if (utm)
                {
                    utmZone = form.Zone;
                    utmRow = form.RowIndex;
                    // 纬度行与数据不在同一纬度范围时给出提示：按此投影要素会落到该带的有效范围之外
                    int dataRow = data == null ? -1 : UtmGridZone.IndexOfLatitude(data.CenterY);
                    if (dataRow >= 0 && !RowCoversLatitude(utmRow, data))
                    {
                        DialogResult answer = MessageBox.Show(this,
                            "所选纬度行 " + UtmGridZone.LetterAt(utmRow) + "（" + RowRangeText(utmRow) + "）与当前数据范围（" +
                            UtmGridZone.LatitudeText(data.MinY) + "~" + UtmGridZone.LatitudeText(data.MaxY) +
                            "）不在同一纬度范围。\r\n按此投影，要素会落到该 UTM 带的有效范围之外，经纬度显示会异常。\r\n\r\n" +
                            "是否改为数据所在的 " + UtmGridZone.LetterAt(dataRow) + " 行（" + RowRangeText(dataRow) + "）？",
                            "选择 UTM 分带", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                        if (answer == DialogResult.Yes) utmRow = dataRow;
                    }
                }
                else { gaussZone = form.Zone; gaussIs3Degree = form.Is3Degree; }
            }
            UpdateProjectionText();
            SetProjection(index, true);
        }

        private static bool RowCoversLatitude(int row, Envelope extent)
        {
            return extent.MaxY >= UtmGridZone.MinOf(row) && extent.MinY <= UtmGridZone.MaxOf(row);
        }

        private static string RowRangeText(int row)
        {
            return UtmGridZone.LatitudeText(UtmGridZone.MinOf(row)) + "~" + UtmGridZone.LatitudeText(UtmGridZone.MaxOf(row));
        }

        /// <summary>
        /// 所有图层要素合在一起的外包矩形，直接读取图层保存的 WGS84 经纬度快照，
        /// 因此**与当前显示投影完全无关**：切换投影后球体上的绿色方框位置与大小保持不变。
        /// </summary>
        public Envelope DataExtentLngLat()
        {
            var extent = new Envelope();
            bool any = false;
            foreach (Layer layer in Map.Layers)
            {
                Envelope box = layer.GetGeographicEnvelope();
                if (box.IsNull) continue;
                extent.ExpandToInclude(box);
                any = true;
            }
            return any ? extent : null;
        }

        // 把分带信息刷新到投影下拉框的文字上
        private void UpdateProjectionText()
        {
            if (projectionCombo == null) return;
            bool old = updatingProjection;
            updatingProjection = true;
            projectionCombo.Items[1] = "高斯-克吕格 " + (gaussIs3Degree ? "3度带 第" : "6度带 第") + gaussZone + "带";
            projectionCombo.Items[2] = "UTM " + UtmGridZone.Designator(utmZone, utmRow);
            updatingProjection = old;
        }

        private ProjectionCS CreateProjection(int index)
        {
            // 所有投影统一使用 WGS84 椭球/基准：系统默认只接受 WGS84 数据，
            // 其他坐标系（北京54、GCJ-02 等）的数据请在图层右键“投影到 WGS84”中先做转换。
            switch (index)
            {
                case 1: return ProjGauss_Kruger.Create(Ellipsoids.WGS84, gaussZone, gaussIs3Degree);
                case 2: return new ProjUTM(utmZone, UtmGridZone.IsNorthern(utmRow), Ellipsoids.WGS84,
                            UtmGridZone.LetterAt(utmRow));
                case 3: return new ProjLambert("WGS_1984 Lambert 中国", Ellipsoids.WGS84, 116.35, 0, 25, 47);
                default: return null;   // 经纬度（地理坐标，不投影）
            }
        }

        private string ProjectionName(int index)
        {
            switch (index)
            {
                case 1: return (gaussIs3Degree ? "高斯-克吕格 3度带 第" + gaussZone + "带" : "高斯-克吕格 6度带 第" + gaussZone + "带");
                case 2: return "UTM " + UtmGridZone.Designator(utmZone, utmRow);
                case 3: return "Lambert 中国（WGS84）";
                default: return "经纬度（WGS84）";
            }
        }

        #endregion

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
            scale.Text = $"坐标系 {ProjectionName(currentProjectionIndex)}    |    比例尺 1 : {Map.Transform.MapScale:N0}    |    图层 {Map.Layers.Count}    ";
            selected.Text = $"选中 {Map.Layers.Sum(l => Map.GetSelection(l).Count)} 个要素";
        }

        // 右下角始终显示经纬度；若已投影，附带显示当前投影坐标（投影名称与上方选框保持一致）
        private void UpdatePosition(Coordinate mapCoordinate)
        {
            Coordinate lngLat = currentProjection == null ? mapCoordinate : currentProjection.TransferToLngLat(mapCoordinate);
            string text;
            if (double.IsNaN(lngLat.X) || double.IsNaN(lngLat.Y) ||
                Math.Abs(lngLat.Y) >= 89.4 || Math.Abs(lngLat.X) > 180.0001)
            {
                // 该点落在当前投影的有效范围之外（例如用南半球 UTM 带显示北半球数据）
                text = "经纬度 ——（当前位置超出该投影的有效范围）";
            }
            else
            {
                text = "经度 " + Math.Abs(lngLat.X).ToString("F6") + (lngLat.X >= 0 ? "°E" : "°W") +
                       "   纬度 " + Math.Abs(lngLat.Y).ToString("F6") + (lngLat.Y >= 0 ? "°N" : "°S");
            }
            if (currentProjection != null)
                text += $"    |    {ProjectionName(currentProjectionIndex)}  X {mapCoordinate.X:F1}  Y {mapCoordinate.Y:F1}";
            position.Text = text;
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
