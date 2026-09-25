using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using ScreenPoint = System.Drawing.Point;

namespace GIS.Display
{
    /// <summary>
    /// 单个图层的行控件。顶层一行：可见性勾选、名称（随长度伸缩）、上移/下移；
    /// 下方为符号区：简单渲染显示单一符号，唯一值/分级渲染显示“符号 + 属性”表格（可点击符号单独设置）。
    /// 右键任意位置弹出图层菜单。
    /// </summary>
    public sealed class LayerControl : UserControl
    {
        private readonly CheckBox check = new CheckBox { AutoSize = true };
        private readonly Label nameLabel = new Label { AutoSize = true };
        private readonly Button upBtn = new Button { Text = "↑", Size = new Size(22, 14) };
        private readonly Button downBtn = new Button { Text = "↓", Size = new Size(22, 14) };
        private readonly ContextMenuStrip menu = new ContextMenuStrip();
        private readonly ToolStripMenuItem mniLabelToggle = new ToolStripMenuItem();
        private readonly ToolStripMenuItem mniSelectableToggle = new ToolStripMenuItem();
        private Panel symbolArea;
        private bool updating;
        private bool selectable = true;

        public Layer Layer { get; }

        // 图层操作事件（由 LayerManagerControl 处理，因为需要访问地图）
        public event Action<LayerControl> MoveUpRequested;
        public event Action<LayerControl> MoveDownRequested;
        public event Action<LayerControl> RendererEditRequested;
        public event Action<LayerControl> LabelToggleRequested;
        public event Action<LayerControl> LabelEditRequested;
        public event Action<LayerControl> ZoomToLayerRequested;
        public event Action<LayerControl> SelectableToggleRequested;
        public event Action<LayerControl> SelectAllRequested;
        public event Action<LayerControl> SelectVisibleRequested;
        public event Action<LayerControl> SetOnlySelectableRequested;
        public event Action<LayerControl> RemoveRequested;
        public event Action<LayerControl> RenameRequested;
        public event Action<LayerControl> ProjectToWgs84Requested;
        public event Action<LayerControl, bool> VisibilityChanged;
        public event Action<LayerControl> RendererViewChanged;
        public event Action<LayerControl> OpenAttributesRequested;

        public LayerControl(Layer layer)
        {
            Layer = layer;
            Width = 224;

            check.Location = new ScreenPoint(6, 8);
            check.CheckedChanged += (s, e) => { if (!updating) VisibilityChanged?.Invoke(this, check.Checked); };

            nameLabel.Location = new ScreenPoint(28, 8);
            nameLabel.DoubleClick += (s, e) => BeginRename();

            upBtn.Location = new ScreenPoint(196, 2);
            downBtn.Location = new ScreenPoint(196, 16);
            upBtn.Click += (s, e) => MoveUpRequested?.Invoke(this);
            downBtn.Click += (s, e) => MoveDownRequested?.Invoke(this);

            Controls.Add(downBtn);
            Controls.Add(upBtn);
            Controls.Add(nameLabel);
            Controls.Add(check);

            BuildMenu();
            foreach (Control c in Controls) c.ContextMenuStrip = menu;
            ContextMenuStrip = menu;
            menu.Opening += (s, e) => RefreshMenuTexts();
            RefreshView();
        }

        #region 菜单

        private void BuildMenu()
        {
            AddItem(menu, "修改图层符号/渲染", () => RendererEditRequested?.Invoke(this));
            mniLabelToggle.Text = "显示注记";
            mniLabelToggle.Click += (s, e) => LabelToggleRequested?.Invoke(this);
            menu.Items.Add(mniLabelToggle);
            AddItem(menu, "设置注记", () => LabelEditRequested?.Invoke(this));
            AddItem(menu, "打开属性表", () => OpenAttributesRequested?.Invoke(this));
            menu.Items.Add(new ToolStripSeparator());
            AddItem(menu, "缩放至图层", () => ZoomToLayerRequested?.Invoke(this));
            AddItem(menu, "选择所有要素", () => SelectAllRequested?.Invoke(this));
            AddItem(menu, "选择可见要素", () => SelectVisibleRequested?.Invoke(this));
            AddItem(menu, "将该图层设为唯一可选图层", () => SetOnlySelectableRequested?.Invoke(this));
            mniSelectableToggle.Text = "将图层设为不可选";
            mniSelectableToggle.Click += (s, e) => SelectableToggleRequested?.Invoke(this);
            menu.Items.Add(mniSelectableToggle);
            AddItem(menu, "重命名", () => RenameRequested?.Invoke(this));
            menu.Items.Add(new ToolStripSeparator());
            // 坐标系统一：其他坐标系（北京54/GCJ-02 等）的数据在这里转换到 WGS84，
            // 之后才参与地图投影——系统默认只接受 WGS84 数据。
            AddItem(menu, "投影到 WGS84", () => ProjectToWgs84Requested?.Invoke(this));
            menu.Items.Add(new ToolStripSeparator());
            AddItem(menu, "移除图层", () => RemoveRequested?.Invoke(this));
        }

        private void AddItem(ContextMenuStrip strip, string text, Action action)
        {
            var item = new ToolStripMenuItem(text);
            item.Click += (s, e) => action();
            strip.Items.Add(item);
        }

        /// <summary>根据当前状态更新动态文本（显示/关闭注记、可选/不可选）。</summary>
        private void RefreshMenuTexts()
        {
            bool labels = Layer.LabelRenderer != null && Layer.LabelRenderer.LabelFeatures;
            mniLabelToggle.Text = labels ? "关闭注记" : "显示注记";
            mniSelectableToggle.Text = selectable ? "将图层设为不可选" : "将图层设为可选";
        }

        public void SetSelectable(bool value)
        {
            selectable = value;
        }

        #endregion

        #region 重命名

        private void BeginRename()
        {
            var box = new TextBox { Bounds = nameLabel.Bounds, Text = Layer.Name };
            box.KeyDown += (s, e) => {
                if (e.KeyCode == Keys.Enter) CommitRename(box);
                else if (e.KeyCode == Keys.Escape) Controls.Remove(box);
            };
            box.LostFocus += (s, e) => CommitRename(box);
            Controls.Add(box);
            box.BringToFront();
            box.Focus();
            box.SelectAll();
        }

        private void CommitRename(TextBox box)
        {
            if (box.Parent == null) return;
            string name = box.Text.Trim();
            if (name.Length > 0) Layer.Name = name;
            Controls.Remove(box);
            RenameRequested?.Invoke(this);
        }

        #endregion

        public void SetChecked(bool visible)
        {
            updating = true;
            check.Checked = visible;
            updating = false;
        }

        public void SetMoveButtons(bool canUp, bool canDown)
        {
            upBtn.Enabled = canUp;
            downBtn.Enabled = canDown;
        }

        /// <summary>模拟用户点击可见性复选框（供自检程序使用）。</summary>
        public void ToggleVisible() => check.Checked = !check.Checked;

        public void RefreshView()
        {
            nameLabel.Text = Truncate(Layer.Name, 15);
            // 渲染符号文件导入后若绑定字段不存在，图层名用红色显示
            bool bindingError = Layer.Renderer != null && Layer.Renderer.HasBindingError;
            nameLabel.ForeColor = bindingError ? Color.Red : SystemColors.ControlText;
            // 非 WGS84 的数据用橙色标出，提示需要先“投影到 WGS84”
            if (!bindingError && !Layer.IsWgs84)
            {
                nameLabel.Text = Truncate(Layer.Name, 9) + "[" + Layer.GeographicDatum.ShortName + "]";
                nameLabel.ForeColor = Color.FromArgb(200, 110, 0);
            }
            RebuildSymbolArea();
        }

        private static string Truncate(string s, int max)
        {
            return s != null && s.Length > max ? s.Substring(0, max - 1) + "…" : s;
        }

        #region 符号区（单一符号 或 符号+属性表格）

        private void RebuildSymbolArea()
        {
            if (symbolArea != null) { Controls.Remove(symbolArea); symbolArea.Dispose(); }
            symbolArea = new ScrollPanel { Location = new ScreenPoint(0, 30), Width = 224 };
            Renderer r = Layer.Renderer;
            bool bindingError = r != null && r.HasBindingError;

            // 图例标题就是绑定字段名（ShowHead=false 时不显示标题）：
            // 重新绑定字段后标题会跟着更新，因此这里显示的字段永远与当前绑定一致。
            if (r is UniqueValueRenderer ur && ur.ValueCount > 0)
                symbolArea.Height = AttachLegend(ur.ShowHead ? ur.HeadTitle : "", ur.ValueCount, i => ur.GetSymbol(i), i => LabelOf(ur.GetSymbol(i), ur.GetValue(i)), ur, ur.DefaultSymbol, "（其他值）", bindingError);
            else if (r is ClassBreaksRenderer cr && cr.BreakCount > 0)
                symbolArea.Height = AttachLegend(cr.ShowHead ? cr.HeadTitle : "", cr.BreakCount, i => cr.GetSymbol(i), i => LabelOf(cr.GetSymbol(i), RangeLabel(cr, i)), cr, null, null, bindingError);
            else
                symbolArea.Height = AttachSingleSymbol();

            Controls.Add(symbolArea);
            symbolArea.BringToFront();
            Height = 30 + symbolArea.Height + 2;
        }

        // 简单渲染：左下角放置一个单一符号，点击进入渲染设置
        private int AttachSingleSymbol()
        {
            var p = new Panel
            {
                Location = new ScreenPoint(4, 2),
                Size = new Size(60, 30),
                BackColor = Color.White,
                Cursor = Cursors.Hand,
                BorderStyle = BorderStyle.FixedSingle
            };
            p.Paint += (s, e) => {
                Symbol sym = GetPreviewSymbol();
                if (sym != null) BasicGeometryDrawer.DrawSymbol(e.Graphics, sym, p.ClientRectangle);
            };
            p.Click += (s, e) => RendererEditRequested?.Invoke(this);
            p.ContextMenuStrip = menu;
            symbolArea.Controls.Add(p);
            return 36;
        }

        // 唯一值/分级渲染：标题 + 符号/属性列表（不用 DataGridView，避免 COM 可见性 MDA），点击符号可单独设置。
        // 行 Margin 必须为 0，否则 FlowLayoutPanel 的默认外边距会把末行挤出计算高度、导致最后一行无法显示。
        private int AttachLegend(string title, int count, Func<int, Symbol> getSymbol, Func<int, string> getLabel,
            Renderer renderer, Symbol extraSymbol, string extraLabel, bool bindingError)
        {
            const int rowHeight = 26;
            int rowH = bindingError ? 42 : rowHeight;   // 绑定错误时符号行加高，便于显示“绑定属性错误”
            int headerHeight = string.IsNullOrEmpty(title) ? 0 : 20;
            int rowCount = count + (extraSymbol != null ? 1 : 0);
            int fullHeight = headerHeight + rowCount * rowH + 20;   // 底部留余量，保证最后一行可完整滚到
            int maxHeight = headerHeight + 4 * rowH;                 // 高度阈值：最多显示 4 个符号行
            var list = new FlowLayoutPanel
            {
                Location = new ScreenPoint(0, 0),
                Size = new Size(205, fullHeight),
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Margin = new Padding(0)
            };
            if (headerHeight > 0)
            {
                list.Controls.Add(new Label
                {
                    Text = title,
                    AutoSize = false,
                    Size = new Size(205, 20),
                    Margin = new Padding(0),
                    Font = new Font("Microsoft YaHei UI", 9, FontStyle.Bold),
                    TextAlign = ContentAlignment.MiddleLeft,
                    Padding = new Padding(4, 0, 0, 0)
                });
            }
            for (int i = 0; i < rowCount; i++)
            {
                int index = i;
                bool isExtra = i >= count;   // 末行：唯一值渲染的“（其他值）”默认符号行
                var row = new FlowLayoutPanel
                {
                    Size = new Size(205, rowH),
                    Margin = new Padding(0),
                    FlowDirection = FlowDirection.LeftToRight,
                    WrapContents = false
                };
                var sym = new Panel
                {
                    Size = new Size(60, 24),
                    BackColor = Color.White,
                    BorderStyle = BorderStyle.FixedSingle,
                    Cursor = Cursors.Hand,
                    Margin = new Padding(4, 1, 8, 0)
                };
                sym.Paint += (s, e) => {
                    Symbol s2 = isExtra ? extraSymbol : getSymbol(index);
                    if (s2 != null) BasicGeometryDrawer.DrawSymbol(e.Graphics, s2, sym.ClientRectangle);
                };
                sym.Click += (s, e) => {
                    if (isExtra) EditDefaultSymbol(renderer);
                    else EditLegendSymbol(renderer, index, getSymbol);
                };
                sym.ContextMenuStrip = menu;
                var lbl = new Label { Text = isExtra ? extraLabel : getLabel(index), AutoSize = true, Margin = new Padding(0, 4, 0, 0) };
                if (bindingError && !isExtra)
                {
                    // 绑定字段不存在的分级/唯一值符号：以红色感叹号符号表示，文字后面跟“绑定属性错误”
                    lbl.Text = getLabel(index) + "  " + Renderer.BindingErrorText;
                    lbl.ForeColor = Color.Red;
                    lbl.MaximumSize = new Size(130, 0);
                }
                row.Controls.Add(sym);
                row.Controls.Add(lbl);
                list.Controls.Add(row);
            }
            symbolArea.AutoScrollMinSize = new Size(0, fullHeight);   // 显式设定滚动内容高度，保证能滚到末行
            symbolArea.Controls.Add(list);
            return Math.Min(fullHeight, maxHeight);
        }

        private void EditLegendSymbol(Renderer renderer, int index, Func<int, Symbol> getSymbol)
        {
            var edited = GIS.Display.UI.SymbolUI.EditSymbol(this, getSymbol(index));
            if (edited != null)
            {
                if (renderer is UniqueValueRenderer u) u.SetSymbol(index, edited);
                else if (renderer is ClassBreaksRenderer c) c.SetSymbol(index, edited);
                RebuildSymbolArea();
                RendererViewChanged?.Invoke(this);
            }
        }

        // 编辑唯一值渲染的默认符号（图例末行“（其他值）”）
        private void EditDefaultSymbol(Renderer renderer)
        {
            var unique = renderer as UniqueValueRenderer;
            if (unique == null || unique.DefaultSymbol == null) return;
            var edited = GIS.Display.UI.SymbolUI.EditSymbol(this, unique.DefaultSymbol);
            if (edited != null)
            {
                unique.DefaultSymbol = edited;
                RebuildSymbolArea();
                RendererViewChanged?.Invoke(this);
            }
        }

        private Symbol GetPreviewSymbol()
        {
            if (Layer.Renderer is SimpleRenderer sr) return sr.Symbol;
            return Layer.Symbol ?? GIS.Display.UI.SymbolUI.DefaultFor(Layer.FeatureClass.GeometryType);
        }

        private static string LabelOf(Symbol symbol, string fallback)
        {
            return symbol != null && !string.IsNullOrEmpty(symbol.Label) ? symbol.Label : fallback;
        }

        private static string RangeLabel(ClassBreaksRenderer cr, int i)
        {
            return i == 0 ? "< " + cr.GetBreakValue(i).ToString("0.##")
                : cr.GetBreakValue(i - 1).ToString("0.##") + " - " + cr.GetBreakValue(i).ToString("0.##");
        }

        #endregion
    }

    /// <summary>
    /// 支持精细滚轮滚动的面板（内部图例滚动用）。
    /// 通过消息过滤器拦截鼠标滚轮：在内部图例上滚动时只滚动内部，不再连带滚动外层图层列表。
    /// </summary>
    internal sealed class ScrollPanel : Panel
    {
        private const int WheelStep = 13;   // 每次滚轮约滚动半行
        private static bool filterInstalled;

        public ScrollPanel()
        {
            AutoScroll = true;
            InstallFilter();
        }

        private static void InstallFilter()
        {
            if (filterInstalled) return;
            filterInstalled = true;
            Application.AddMessageFilter(new WheelRouter());
        }

        internal void ScrollByWheel(int delta)
        {
            int current = -AutoScrollPosition.Y;
            int step = -(delta / 120) * WheelStep;
            AutoScrollPosition = new ScreenPoint(0, Math.Max(0, current + step));
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (VerticalScroll.Visible) ScrollByWheel(e.Delta);
            else base.OnMouseWheel(e);   // 内容不足一屏时，交给外层列表滚动
        }

        // 找到鼠标下方的 ScrollPanel 并滚动，返回 true 阻止消息继续向外层容器传递
        private sealed class WheelRouter : IMessageFilter
        {
            private const int WM_MOUSEWHEEL = 0x020A;

            [System.Runtime.InteropServices.DllImport("user32.dll")]
            private static extern IntPtr WindowFromPoint(ScreenPoint point);

            public bool PreFilterMessage(ref Message m)
            {
                if (m.Msg != WM_MOUSEWHEEL) return false;
                IntPtr handle = WindowFromPoint(Control.MousePosition);
                Control c = handle == IntPtr.Zero ? null : Control.FromHandle(handle);
                while (c != null)
                {
                    if (c is ScrollPanel panel && panel.VerticalScroll.Visible)
                    {
                        int delta = (short)((long)m.WParam >> 16);
                        panel.ScrollByWheel(delta);
                        return true;
                    }
                    c = c.Parent;
                }
                return false;
            }
        }
    }

    /// <summary>
    /// 图层管理面板：绑定地图，左侧按“上层在上”列出所有图层行。
    /// </summary>
    public sealed class LayerManagerControl : UserControl
    {
        private readonly FlowLayoutPanel rowsPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true
        };
        private readonly List<LayerControl> rows = new List<LayerControl>();
        private MapControl map;

        public IReadOnlyList<LayerControl> LayerRows => rows;

        public LayerManagerControl()
        {
            var title = new Label
            {
                Text = "图层", Dock = DockStyle.Top, Height = 30, TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 0, 0), Font = new Font("Microsoft YaHei UI", 10, FontStyle.Bold)
            };
            Controls.Add(rowsPanel);
            Controls.Add(title);
        }

        public void Bind(MapControl control)
        {
            if (map != null) map.LayersChanged -= OnLayersChanged;
            map = control;
            if (map != null) map.LayersChanged += OnLayersChanged;
            Rebuild();
        }

        private void OnLayersChanged(object sender, EventArgs e) => Rebuild();

        public void Rebuild()
        {
            rows.Clear();
            rowsPanel.Controls.Clear();
            if (map == null) return;
            var layers = map.Layers.Reverse().ToList(); // 顶层在前
            for (int i = 0; i < layers.Count; i++)
            {
                LayerControl lc = new LayerControl(layers[i]);
                lc.SetChecked(layers[i].Visible);
                lc.SetMoveButtons(i > 0, i < layers.Count - 1);
                lc.SetSelectable(map.IsLayerSelectable(layers[i]));
                lc.VisibilityChanged += (c, v) => map.SetLayerVisible(c.Layer, v);
                lc.MoveUpRequested += c => MoveLayer(c, 1);
                lc.MoveDownRequested += c => MoveLayer(c, -1);
                lc.RendererEditRequested += c => EditRenderer(c);
                lc.LabelToggleRequested += c => ToggleLabel(c);
                lc.LabelEditRequested += c => EditLabel(c);
                lc.ZoomToLayerRequested += c => ZoomToLayer(c);
                lc.SelectableToggleRequested += c => ToggleSelectable(c);
                lc.SelectAllRequested += c => SelectAll(c);
                lc.SelectVisibleRequested += c => SelectVisible(c);
                lc.SetOnlySelectableRequested += c => SetOnlySelectable(c);
                lc.RemoveRequested += c => map.RemoveLayer(c.Layer);
                lc.RenameRequested += c => map.Invalidate();
                lc.ProjectToWgs84Requested += c => ProjectToWgs84(c);
                lc.RendererViewChanged += c => map.RefreshMap();
                lc.OpenAttributesRequested += c => OpenAttributes(c);
                rowsPanel.Controls.Add(lc);
                rows.Add(lc);
            }
        }

        private int IndexOf(Layer layer)
        {
            for (int i = 0; i < map.Layers.Count; i++)
                if (map.Layers[i] == layer) return i;
            return -1;
        }

        private void MoveLayer(LayerControl c, int delta)
        {
            int idx = IndexOf(c.Layer);
            int target = idx + delta;
            if (target >= 0 && target < map.Layers.Count)
                map.MoveLayer(c.Layer, target);
        }

        // 图层右键“投影到 WGS84”：把非 WGS84 坐标系的图层数据自动转换到 WGS84。
        // 系统默认只接受 WGS84 数据，其他坐标系的数据导入后在这里完成基准转换。
        // 若图层已标记了具体坐标系（如北京54/GCJ-02），直接用内置的公开参数自动转换，无需手工填参数；
        // 若图层仍标记为 WGS84，则弹出一个已自动填好参数的对话框，让用户指明数据实际所属坐标系。
        private void ProjectToWgs84(LayerControl c)
        {
            Layer layer = c.Layer;
            ProjectionCS projection = layer.FeatureClass.ProjectionCS;
            GeographicDatum source = layer.GeographicDatum ?? GeographicDatum.Wgs84;
            bool declared = !string.Equals(source.GeoCSName, GeographicDatum.Wgs84.GeoCSName, StringComparison.Ordinal);

            if (!declared)
            {
                using (var dialog = new GIS.Display.UI.ProjectToWgs84Form(layer, projection != null))
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    source = dialog.SourceDatum;
                }
                if (string.Equals(source.GeoCSName, GeographicDatum.Wgs84.GeoCSName, StringComparison.Ordinal))
                {
                    MessageBox.Show(this,
                        "已选择 WGS84：图层“" + layer.Name + "”无需基准转换。\r\n" +
                        "若该数据实际来自其他坐标系，请在对话框中选择其当前坐标系。",
                        "投影到 WGS84", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
            }

            int count = ConvertLayerToWgs84(layer, projection, source);
            map.RefreshMap();
            Rebuild();
            MessageBox.Show(this,
                "已把 " + count + " 个要素从【" + source.DisplayName + "】自动转换到【WGS84】。\r\n\r\n" +
                (source.IsGcj02
                    ? "换算方式：国测局公开加密偏移算法（经纬度非线性偏移，非七参数）\r\n"
                    : "椭球：" + source.Ellipsoid.SpheroidName + " → WGS_1984\r\n" +
                      "转换参数：" + source.ParameterText() + "\r\n") +
                "参数来源：" + source.ParameterSource +
                (source.IsApproximate
                    ? "\r\n\r\n注意：该参数为近似值，如需米级精度请在对话框“投影到 WGS84”中填入本地区参数。"
                    : ""),
                "投影到 WGS84", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        /// <summary>
        /// 基准转换只作用在图层保存的**经纬度原始数据**上（图层的固有属性），
        /// 转换完再按当前投影重新生成显示几何；因此转换结果与当前显示投影无关，
        /// 之后反复切换投影也不会把它“转回去”。
        /// </summary>
        private static int ConvertLayerToWgs84(Layer layer, ProjectionCS projection, GeographicDatum source)
        {
            if (!layer.HasGeographicGeometries) layer.CaptureGeographic(projection);
            int count = 0;
            foreach (Geometry geometry in layer.GeographicGeometries)
            {
                if (geometry == null || geometry.IsEmpty) continue;
                ConvertGeometryDatum(geometry, source);
                count++;
            }
            layer.GeographicDatum = GeographicDatum.Wgs84;
            layer.ApplyProjection(projection);   // 用转换后的经纬度重新生成显示几何
            return count;
        }

        // 把几何对象上的每个经纬度点从源基准转换到 WGS84
        private static void ConvertGeometryDatum(Geometry geometry, GeographicDatum source)
        {
            if (geometry is Point point) { point.Coordinate = DatumTransform.ToWgs84(point.Coordinate, source); return; }
            if (geometry is LineString line) { ConvertDatumPoints(line.Points, source); return; }
            if (geometry is Polygon polygon)
            {
                ConvertDatumPoints(polygon.ExteriorRing, source);
                foreach (Points hole in polygon.Holes) ConvertDatumPoints(hole, source);
                return;
            }
            if (geometry is MultiPoint multiPoint) { ConvertDatumPoints(multiPoint.Points, source); return; }
            if (geometry is MultiLineString multiLine) { foreach (LineString part in multiLine.Parts) ConvertDatumPoints(part.Points, source); return; }
            if (geometry is MultiPolygon multiPolygon)
            {
                foreach (Polygon part in multiPolygon.Parts)
                {
                    ConvertDatumPoints(part.ExteriorRing, source);
                    foreach (Points hole in part.Holes) ConvertDatumPoints(hole, source);
                }
            }
        }

        private static void ConvertDatumPoints(Points points, GeographicDatum source)
        {
            for (int i = 0; i < points.Count; i++)
                points.SetItem(i, DatumTransform.ToWgs84(points[i], source));
        }

        private void EditRenderer(LayerControl c)
        {
            // “应用”时实时刷新地图与图层面板，便于测试渲染效果
            if (GIS.Display.UI.LayerRendererForm.Edit(this, c.Layer, () => { map.RefreshMap(); Rebuild(); }))
            {
                map.RefreshMap();
                Rebuild();
            }
        }

        private void ToggleLabel(LayerControl c)
        {
            // 初始无注记：首次点击即开启
            var lr = c.Layer.LabelRenderer ?? new LabelRenderer { Field = FirstField(c.Layer) };
            lr.LabelFeatures = !lr.LabelFeatures;
            c.Layer.LabelRenderer = lr;
            map.RefreshMap();
        }

        private void EditLabel(LayerControl c)
        {
            if (GIS.Display.UI.LabelRendererForm.Edit(this, c.Layer))
            {
                map.RefreshMap();
                Rebuild();
            }
        }

        private void ZoomToLayer(LayerControl c)
        {
            Envelope env = c.Layer.GetEnvelope();
            if (env.IsNull) return;
            // 四周留 10% 边距，保证图层完整显示在窗口内
            double pad = Math.Max(Math.Max(env.Width, env.Height) * 0.1, 1);
            map.SetExtent(new Envelope(env.MinX - pad, env.MaxX + pad, env.MinY - pad, env.MaxY + pad));
        }

        private void ToggleSelectable(LayerControl c)
        {
            map.SetLayerSelectable(c.Layer, !map.IsLayerSelectable(c.Layer));
        }

        private void SelectAll(LayerControl c)
        {
            var all = new Features();
            all.Union(c.Layer.FeatureClass.Features);
            map.SetSelection(c.Layer, all, SelectMethodConstant.CreateNew);
        }

        private void SelectVisible(LayerControl c)
        {
            Features visible = c.Layer.FeatureClass.SearchByBox(map.GetExtent());
            map.SetSelection(c.Layer, visible, SelectMethodConstant.CreateNew);
        }

        private void SetOnlySelectable(LayerControl c)
        {
            foreach (Layer layer in map.Layers)
                map.SetLayerSelectable(layer, layer == c.Layer);
        }

        private void OpenAttributes(LayerControl c)
        {
            var form = new GIS.Display.UI.AttributeTableForm(c.Layer);
            form.FeatureSelected += feature => {
                var sel = new Features();
                sel.Add(feature);
                map.SetSelection(c.Layer, sel, SelectMethodConstant.CreateNew);
                // 仅选中要素，不缩放地图，保持地图位置不变
            };
            form.Show(this);
        }

        private static string FirstField(Layer layer)
        {
            return layer.FeatureClass.Fields.Count > 0 ? layer.FeatureClass.Fields[0].Name : "";
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && map != null) map.LayersChanged -= OnLayersChanged;
            base.Dispose(disposing);
        }
    }
}
