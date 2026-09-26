using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using GIS;
using GIS.Display;
using GIS.Display.Demo;
using GIS.Display.UI;
using ScreenPoint = System.Drawing.Point;

internal sealed class ProbeMap : MapControl
{
    public void Down(int x, int y) => OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, x, y, 0));
    public void MoveTo(int x, int y) => OnMouseMove(new MouseEventArgs(MouseButtons.Left, 0, x, y, 0));
    public void Up(int x, int y) => OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, x, y, 0));
    public void Wheel(int x, int y, int delta) => OnMouseWheel(new MouseEventArgs(MouseButtons.None, 0, x, y, delta));
    public void Escape() { var message = new Message(); ProcessCmdKey(ref message, Keys.Escape); }
}

internal sealed class CountingRenderer : ILayerRenderer
{
    public int Calls;
    public void DrawLayer(Graphics g, Layer layer, MapTransform transform, Rectangle viewport) { Calls++; }
}

internal static class Program
{
    private static int passed;
    private static string outputDir;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        passed++;
        Console.WriteLine("PASS: " + message);
    }
    private static bool Near(double a, double b) => Math.Abs(a - b) < 0.00001;
    private static Bitmap Render(MapControl map)
    {
        var bitmap = new Bitmap(map.Width, map.Height);
        map.DrawToBitmap(bitmap, map.ClientRectangle);
        return bitmap;
    }
    private static bool ColorAt(Bitmap bitmap, ScreenPoint p, Color expected)
    {
        Color c = bitmap.GetPixel(p.X, p.Y);
        return Math.Abs(c.R - expected.R) < 5 && Math.Abs(c.G - expected.G) < 5 && Math.Abs(c.B - expected.B) < 5;
    }
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            string output = args.Length > 0 ? args[0] : Path.Combine("obj", "validation");
            outputDir = output;
            Directory.CreateDirectory(output);
            TestSelectionAndNavigation();
            TestDrawing();
            TestModule3Extensions();
            using (var form = new MainForm())
            {
                // 创建真实控件句柄和完成布局，但不显示测试窗口。
                form.ShowInTaskbar = false;
                form.Opacity = 0;
                form.Show();
                Application.DoEvents();
                form.LoadSamples();
                Check(form.Map.Layers.Count == 6, "演示程序加载6个样例图层");
                Check(form.Map.Layers.Sum(l => l.FeatureClass.Features.Count) == 12, "演示程序包含12个样例要素");
                Check(form.Map.Visible && form.Map.Width > 500 && form.Map.Height > 250, "地图控件可见且布局尺寸有效");
                var manager = FindControl<LayerManagerControl>(form);
                Check(manager.LayerRows.Count == 6, "图层面板列出6个图层");
                Check(manager.LayerRows[0].Layer == form.Map.Layers.Last(), "图层面板按顶层优先列出");
                var firstRow = manager.LayerRows[0];
                firstRow.ToggleVisible();
                Check(!firstRow.Layer.Visible, "图层面板勾选事件控制地图可见性");
                firstRow.ToggleVisible();
                // 工具条：投影下拉框 + “选择分带”按钮（仅高斯-克吕格/UTM 可用）
                var toolbar = FindControl<ToolStrip>(form);
                ToolStripButton zoneButton = null;
                ToolStripComboBox projectionCombo = null;
                foreach (ToolStripItem item in toolbar.Items)
                {
                    if (item is ToolStripButton button && button.Text == "选择分带") zoneButton = button;
                    if (item is ToolStripComboBox combo && combo.Items.Count == 4
                        && combo.Items[0].ToString().StartsWith("经纬度")) projectionCombo = combo;
                }
                Check(zoneButton != null && projectionCombo != null, "工具条含投影下拉框与“选择分带”按钮");
                projectionCombo.SelectedIndex = 0;
                Check(!zoneButton.Enabled, "经纬度投影时不提供分带选择");
                projectionCombo.SelectedIndex = 1;
                Check(zoneButton.Enabled && projectionCombo.Items[1].ToString().Contains("高斯-克吕格"),
                    "高斯-克吕格投影时可选择分带且带号显示在投影项上");
                projectionCombo.SelectedIndex = 2;
                Check(zoneButton.Enabled && projectionCombo.Items[2].ToString().StartsWith("UTM"),
                    "UTM投影时可选择分带且带号显示在投影项上");
                // 投影切换性能：切换 + 强制重绘地图，统计单次耗时
                projectionCombo.SelectedIndex = 0;
                form.Map.Refresh();
                long switchTicks = DateTime.Now.Ticks;
                const int switchCount = 12;
                for (int i = 0; i < switchCount; i++)
                {
                    projectionCombo.SelectedIndex = 1 + (i % 3);
                    form.Map.Refresh();
                }
                double switchMs = (DateTime.Now.Ticks - switchTicks) / 10000.0 / switchCount;
                Check(switchMs < 120, "切换投影并重绘单次耗时低于120毫秒（实测 " + switchMs.ToString("F1") + " ms）");

                // 外包矩形始终按经纬度计算：切换投影前后必须完全一致
                projectionCombo.SelectedIndex = 0;
                form.Map.Refresh();
                Envelope geoExtent = form.DataExtentLngLat();
                projectionCombo.SelectedIndex = 2;
                form.Map.Refresh();
                Envelope utmExtent = form.DataExtentLngLat();
                Check(geoExtent != null && utmExtent != null
                    && Math.Abs(geoExtent.MinX - utmExtent.MinX) < 1e-6 && Math.Abs(geoExtent.MaxX - utmExtent.MaxX) < 1e-6
                    && Math.Abs(geoExtent.MinY - utmExtent.MinY) < 1e-6 && Math.Abs(geoExtent.MaxY - utmExtent.MaxY) < 1e-6,
                    "所有要素外包矩形按经纬度计算，切换投影后位置与大小不变");
                // 切换投影后视图自动对准所有图层：各投影下要素中心都必须落在视野内，
                // 否则投影带来的大偏移会让符号“移动得很远”而跑出屏幕。
                var expectedProjections = new ProjectionCS[]
                {
                    null,
                    ProjGauss_Kruger.Create(Ellipsoids.WGS84, 39, true),
                    new ProjUTM(50, true, Ellipsoids.WGS84, 'S'),
                    new ProjLambert("WGS_1984 Lambert 中国", Ellipsoids.WGS84, 116.35, 0, 25, 47)
                };
                var dataCenter = new Coordinate((geoExtent.MinX + geoExtent.MaxX) / 2,
                    (geoExtent.MinY + geoExtent.MaxY) / 2);
                bool allVisible = true;
                for (int i = 0; i < expectedProjections.Length; i++)
                {
                    projectionCombo.SelectedIndex = i;
                    form.Map.Refresh();
                    Coordinate expected = expectedProjections[i] == null
                        ? dataCenter : expectedProjections[i].TransferToProjCo(dataCenter);
                    if (!form.Map.GetExtent().Contains(expected)) allVisible = false;
                }
                Check(allVisible, "切换投影后视图自动对准所有图层（要素始终在视野内）");
                projectionCombo.SelectedIndex = 0;
                using (var image = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(image, new Rectangle(0, 0, form.Width, form.Height));
                    image.Save(Path.Combine(output, "module2-preview.png"), ImageFormat.Png);
                }
            }
            File.WriteAllText(Path.Combine(output, "checks.txt"), passed + " checks passed.");
            Console.WriteLine("ALL PASSED: " + passed);
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static T FindControl<T>(Control parent) where T : Control
    {
        foreach (Control child in parent.Controls)
        {
            if (child is T result) return result;
            T nested = FindControl<T>(child);
            if (nested != null) return nested;
        }
        return null;
    }

    private static void TestSelectionAndNavigation()
    {
        using (var map = new ProbeMap { Size = new Size(600, 400) })
        {
            var handle = map.Handle;
            map.FullExtent();
            Check(map.GetExtent().Width > 0, "空地图全图操作安全");
            var layer = SampleData.MakePlanarLayer("点", GeometryTypeConstant.Point, null, "POINT (20 20)", "POINT (80 80)");
            map.AddLayer(layer);
            map.AddLayer(layer);
            Check(map.Layers.Count == 1, "同一图层不重复添加");
            map.SetExtent(new Envelope(0, 100, 0, 100));
            ScreenPoint first = map.Transform.MapToScreen(new Coordinate(20, 20));
            ScreenPoint second = map.Transform.MapToScreen(new Coordinate(80, 80));
            int selectionEvents = 0;
            map.SelectionChanged += (s, e) => selectionEvents++;
            map.SelectAt(new ScreenPoint(first.X + 4, first.Y), SelectMethodConstant.CreateNew);
            Check(map.GetSelection(layer).Count == 1, "点选5像素容限");
            map.SelectAt(second, SelectMethodConstant.AddToCurrent);
            Check(map.GetSelection(layer).Count == 2, "添加选择");
            map.SelectAt(first, SelectMethodConstant.RemoveFromCurrent);
            Check(map.GetSelection(layer).Count == 1 && map.GetSelection(layer)[0] == layer.FeatureClass.Features[1], "减去选择");
            map.SelectAt(first, SelectMethodConstant.SelectFromCurrent);
            Check(map.GetSelection(layer).Count == 0, "选择交集");
            map.SelectBox(map.ClientRectangle, SelectMethodConstant.CreateNew);
            Check(map.GetSelection(layer).Count == 2, "框选所有点");
            Features snapshot = map.GetSelection(layer);
            snapshot.Clear();
            Check(map.GetSelection(layer).Count == 2, "修改返回集合不污染地图选择集");
            map.SetLayerVisible(layer, false);
            Check(map.GetSelection(layer).Count == 0, "隐藏图层清除选择");
            map.SelectBox(map.ClientRectangle, SelectMethodConstant.CreateNew);
            Check(map.GetSelection(layer).Count == 0, "隐藏图层不参与框选");
            map.SetLayerVisible(layer, true);
            map.SetLayerSelectable(layer, false);
            map.SelectAt(first, SelectMethodConstant.CreateNew);
            Check(map.GetSelection(layer).Count == 0, "不可选图层不参与点选");
            map.SetLayerSelectable(layer, true);
            map.SelectAt(first, SelectMethodConstant.CreateNew);
            Feature selected = layer.FeatureClass.Features[0];
            layer.FeatureClass.Features.Remove(selected);
            map.RefreshMap();
            Check(map.GetSelection(layer).Count == 0, "编辑删除后清除失效选择引用");
            Check(selectionEvents > 0, "选择变化向外通知");

            ScreenPoint anchor = new ScreenPoint(160, 130);
            Coordinate before = map.Transform.ScreenToMap(anchor);
            double scale = map.Transform.MapScale;
            map.Wheel(anchor.X, anchor.Y, 120);
            Coordinate after = map.Transform.ScreenToMap(anchor);
            Check(Near(before.X, after.X) && Near(before.Y, after.Y), "滚轮缩放保持鼠标锚点");
            Check(Near(map.Transform.MapScale, scale / 1.2), "滚轮比例正确");
            map.SetExtent(new Envelope(0, 100, 0, 100));
            double unit = map.Transform.MapUnitsPerPixel();
            double offsetX = map.Transform.MapOffsetX, offsetY = map.Transform.MapOffsetY;
            map.Down(100, 100); map.MoveTo(130, 120); map.Up(130, 120);
            Check(Near(map.Transform.MapOffsetX, offsetX - 30 * unit) &&
                Near(map.Transform.MapOffsetY, offsetY + 20 * unit), "鼠标平移方向与距离");
            offsetX = map.Transform.MapOffsetX;
            offsetY = map.Transform.MapOffsetY;
            map.Down(100, 100); map.MoveTo(150, 145); map.Escape();
            Check(Near(map.Transform.MapOffsetX, offsetX) && Near(map.Transform.MapOffsetY, offsetY), "Esc撤销本次平移");
            Coordinate center = map.GetExtent().Center;
            map.Size = new Size(800, 500);
            Check(Near(center.X, map.GetExtent().CenterX) && Near(center.Y, map.GetExtent().CenterY), "调整窗口保持地图中心");

            map.InteractionMode = MapInteractionMode.Select;
            second = map.Transform.MapToScreen(new Coordinate(80, 80));
            map.Down(second.X, second.Y); map.Up(second.X + 1, second.Y + 1);
            Check(map.GetSelection(layer).Count == 1, "轻微鼠标抖动仍作为点选");
            map.ClearSelection();
            map.Down(second.X + 15, second.Y + 15);
            map.MoveTo(second.X - 15, second.Y - 15);
            map.Up(second.X - 15, second.Y - 15);
            Check(map.GetSelection(layer).Count == 1, "反向拖框正确选中");
            Check(map.RemoveLayer(layer) && map.Layers.Count == 0, "移除图层");
            map.FullExtent();
            map.AddLayer(SampleData.MakePlanarLayer("单点", GeometryTypeConstant.Point, null, "POINT (10 10)"));
            map.FullExtent();
            Check(map.GetExtent().Contains(new Coordinate(10, 10)) && map.GetExtent().Width > 0, "单点图层全图范围");
            map.SetExtent(new Envelope(0, 100, 10, 10));
            Check(map.GetExtent().Height > 0, "水平线零高度范围可定位");
            bool rejected = false;
            try { map.Zoom(0); } catch (ArgumentOutOfRangeException) { rejected = true; }
            Check(rejected, "拒绝无效缩放比例");
        }
    }

    private static void TestDrawing()
    {
        using (var map = new MapControl { Size = new Size(600, 400) })
        {
            var handle = map.Handle;
            var polygon = SampleData.MakePlanarLayer("带洞", GeometryTypeConstant.Polygon,
                new SimpleFillSymbol { Color = Color.Red, Outline = new SimpleLineSymbol { Color = Color.Red } },
                "POLYGON ((10 10, 90 10, 90 90, 10 90, 10 10), (40 40, 60 40, 60 60, 40 60, 40 40))");
            map.AddLayer(polygon);
            map.SetExtent(new Envelope(0, 100, 0, 100));
            ScreenPoint hole = map.Transform.MapToScreen(new Coordinate(50, 50));
            ScreenPoint solid = map.Transform.MapToScreen(new Coordinate(25, 25));
            using (Bitmap image = Render(map))
            {
                Check(ColorAt(image, hole, Color.White), "多边形洞保持透明");
                Check(ColorAt(image, solid, Color.Red), "多边形外环正确填色");
            }
            map.SelectAt(hole, SelectMethodConstant.CreateNew);
            Check(map.GetSelection(polygon).Count == 0, "多边形洞内不选中");
            map.SelectAt(solid, SelectMethodConstant.CreateNew);
            Check(map.GetSelection(polygon).Count == 1, "多边形实体内选中");
            map.ClearSelection();
            var top = SampleData.MakePlanarLayer("上层", GeometryTypeConstant.Polygon,
                new SimpleFillSymbol { Color = Color.Blue, Outline = new SimpleLineSymbol { Color = Color.Blue } },
                "POLYGON ((15 15, 35 15, 35 35, 15 35, 15 15))");
            map.AddLayer(top);
            using (Bitmap image = Render(map)) Check(ColorAt(image, solid, Color.Blue), "新增图层覆盖下层");
            map.MoveLayer(top, 0);
            using (Bitmap image = Render(map)) Check(ColorAt(image, solid, Color.Red), "改变顺序改变覆盖关系");
            map.SetLayerVisible(polygon, false);
            using (Bitmap image = Render(map)) Check(ColorAt(image, solid, Color.Blue), "隐藏上层露出下层");
            var custom = new CountingRenderer();
            map.Renderer = custom;
            using (Bitmap image = Render(map)) Check(custom.Calls == 1, "模块3渲染接口仅接收可见图层");
            map.Renderer = new BasicGeometryDrawer();
            foreach (Layer layer in map.Layers.ToArray()) map.RemoveLayer(layer);
            foreach (Layer layer in SampleData.Create()) map.AddLayer(layer);
            map.FullExtent();
            using (Bitmap image = Render(map))
                Check(image.Width == 600, "六种几何类型样例绘制成功");
            // 用像素检查复合面的独立部件，防止意外连接为一个面。
            foreach (Layer layer in map.Layers.ToArray()) map.RemoveLayer(layer);
            var multiple = SampleData.MakePlanarLayer("多个部件", GeometryTypeConstant.MultiPolygon,
                new SimpleFillSymbol { Color = Color.Green, Outline = new SimpleLineSymbol { Color = Color.Green } },
                "MULTIPOLYGON (((10 10, 30 10, 30 30, 10 30, 10 10)), ((70 70, 90 70, 90 90, 70 90, 70 70)))");
            map.AddLayer(multiple);
            map.SetExtent(new Envelope(0, 100, 0, 100));
            using (Bitmap image = Render(map))
            {
                Check(ColorAt(image, map.Transform.MapToScreen(new Coordinate(20, 20)), Color.Green), "复合面第一部件");
                Check(ColorAt(image, map.Transform.MapToScreen(new Coordinate(80, 80)), Color.Green), "复合面第二部件");
                Check(ColorAt(image, map.Transform.MapToScreen(new Coordinate(50, 50)), Color.White), "复合面部件之间不错误连面");
            }
        }
    }

    // 模块3扩展：线符号多条偏移线、分带选择球体、色带系统、渲染符号文件与绑定属性错误
    private static void TestModule3Extensions()
    {
        // 1) 线符号的多个偏移量 → 一个图层绘制出多条平行线（国界线样式）
        using (var map = new MapControl { Size = new Size(600, 400) })
        {
            var handle = map.Handle;
            var symbol = new SimpleLineSymbol { Color = Color.Black, Size = 0.35 };
            symbol.Offsets.Add(new LineOffset(0, new SimpleLineSymbol { Color = Color.Black, Size = 0.35 }));
            symbol.Offsets.Add(new LineOffset(2, new SimpleLineSymbol { Color = Color.Red, Size = 1 }));
            symbol.Offsets.Add(new LineOffset(-2, new SimpleLineSymbol { Color = Color.Blue, Size = 1 }));
            var line = SampleData.MakePlanarLayer("国界", GeometryTypeConstant.LineString, symbol, "LINESTRING (10 50, 90 50)");
            map.AddLayer(line);
            map.SetExtent(new Envelope(0, 100, 0, 100));
            int offset = (int)Math.Round(2.0 / 1000.0 * map.Transform.Dpm);
            ScreenPoint mid = map.Transform.MapToScreen(new Coordinate(50, 50));
            using (Bitmap image = Render(map))
            {
                bool upRed = ColorAt(image, new ScreenPoint(mid.X, mid.Y - offset), Color.Red);
                bool downRed = ColorAt(image, new ScreenPoint(mid.X, mid.Y + offset), Color.Red);
                bool upBlue = ColorAt(image, new ScreenPoint(mid.X, mid.Y - offset), Color.Blue);
                bool downBlue = ColorAt(image, new ScreenPoint(mid.X, mid.Y + offset), Color.Blue);
                Check(ColorAt(image, mid, Color.Black) && upRed != downRed && upBlue != downBlue && upRed != upBlue,
                    "线符号偏移量绘制出多条平行线");
            }
        }

        // 2) 分带选择：UTM 经度带 × 纬度行、球体控件与分带数学
        Check(UtmGridZone.RowCount == 20 && UtmGridZone.RowLetters == "CDEFGHJKLMNPQRSTUVWX",
            "UTM纬度行为C~X共20行且不含I/O");
        Check(UtmGridZone.LetterOfLatitude(39.9) == 'S' && UtmGridZone.LetterOfLatitude(-33) == 'H'
            && UtmGridZone.LetterOfLatitude(0) == 'N' && UtmGridZone.LetterOfLatitude(80) == 'X',
            "UTM按纬度反算行字母");
        Check(UtmGridZone.MinOf(14) == 32 && UtmGridZone.MaxOf(14) == 40
            && UtmGridZone.MinOf(19) == 72 && UtmGridZone.MaxOf(19) == 84,
            "UTM行纬度范围（S行32°N~40°N，X行72°N~84°N共12°）");
        Check(UtmGridZone.IndexOfLatitude(-80) == 0 && UtmGridZone.IndexOfLatitude(-85) == 0
            && UtmGridZone.IndexOfLatitude(85) == 19, "UTM纬度超出范围时钳制到首末行");
        Check(UtmGridZone.IsNorthern(UtmGridZone.IndexOfLetter('N')) && !UtmGridZone.IsNorthern(UtmGridZone.IndexOfLetter('M')),
            "UTM北半球自N行开始");
        Check(UtmGridZone.Designator(50, 14) == "50S" && UtmGridZone.Designator(116.35, 39.9) == "50S",
            "UTM四边形标记（北京为50S）");

        Check(ZoneGlobe.ZoneOf(ZoneSystem.GaussKruger3, 116.35) == 39
            && Math.Abs(ZoneGlobe.ZoneCentral(ZoneSystem.GaussKruger3, 39) - 117) < 1e-9,
            "高斯-克吕格3度带按经度反算带号与中央经线");
        Check(ZoneGlobe.ZoneOf(ZoneSystem.GaussKruger6, 116.35) == 20
            && Math.Abs(ZoneGlobe.ZoneCentral(ZoneSystem.GaussKruger6, 20) - 117) < 1e-9,
            "高斯-克吕格6度带按经度反算带号与中央经线");
        Check(ZoneGlobe.ZoneOf(ZoneSystem.Utm, 116.35) == 50
            && Math.Abs(ZoneGlobe.ZoneCentral(ZoneSystem.Utm, 50) - 117) < 1e-9,
            "UTM按经度反算带号与中央经线");
        Check(ZoneGlobe.ZoneOf(ZoneSystem.Utm, -179.9) == 1 && ZoneGlobe.ZoneOf(ZoneSystem.Utm, 179.9) == 60,
            "UTM全球分带范围1~60");
        using (var globe = new ZoneGlobe { Size = new Size(300, 300) })
        {
            var handle = globe.Handle;
            globe.Kind = ZoneSystem.Utm;
            globe.Zone = 50;
            globe.RowIndex = 14;
            globe.ViewLon = 117;
            globe.DataExtent = new Envelope(116.25, 116.45, 39.85, 40.05);
            using (var bitmap = new Bitmap(300, 300))
            {
                globe.DrawToBitmap(bitmap, new Rectangle(0, 0, 300, 300));
                bool drawn = false;
                for (int y = 0; y < 300 && !drawn; y += 7)
                    for (int x = 0; x < 300 && !drawn; x += 7)
                    {
                        Color c = bitmap.GetPixel(x, y);
                        if (c.B > c.R + 5) drawn = true;
                    }
                Check(drawn, "球体分带视图绘制球面与经纬网");
                globe.DataExtent = new Envelope(116.25, 116.45, 39.85, 40.05);
                globe.ViewLon = 116.35;
                globe.ViewLat = 39.95;
                globe.DrawToBitmap(bitmap, new Rectangle(0, 0, 300, 300));
                bool caption = false;
                for (int y = 270; y < 300 && !caption; y++)
                    for (int x = 0; x < 260 && !caption; x++)
                    {
                        Color c = bitmap.GetPixel(x, y);
                        if (c.G > c.R + 20) caption = true;
                    }
                Check(caption, "数据范围方框与左下角说明文字已绘制");
                long ticks = DateTime.Now.Ticks;
                for (int i = 0; i < 8; i++) globe.DrawToBitmap(bitmap, new Rectangle(0, 0, 300, 300));
                double ms = (DateTime.Now.Ticks - ticks) / 10000.0 / 8;
                Check(ms < 60, "球体分带视图单次重绘耗时低于60毫秒（实测 " + ms.ToString("F1") + " ms）");
            }
        }
        // 地球视图：滑块/选择分带后必须能移动地球
        using (var view = new ZoneGlobe { Size = new Size(300, 300) })
        {
            var viewHandle = view.Handle;
            int notified = 0;
            view.ViewChanged += (s, e) => notified++;
            double before = view.ViewLon;
            view.ViewLon = before + 40;
            Check(Math.Abs(view.ViewLon - before - 40) < 1e-9 && notified == 1, "改变视图经度立即生效并通知滑块");
            view.CenterOn(116.35, 39.9);
            Check(Math.Abs(view.ViewLon - 116.35) < 1e-9 && Math.Abs(view.ViewLat - 39.9) < 1e-9,
                "CenterOn 把地球移动到指定经纬度");
        }

        // 高亮分带只绘制在地球表面：黄色像素反算出的经纬度必须落在所选带内，
        // 不能因为用直线弦闭合裁剪结果而横穿球体（此前会呈现为扇形/饼状）。
        using (var globe = new ZoneGlobe { Size = new Size(320, 320) })
        {
            var handle = globe.Handle;
            globe.Kind = ZoneSystem.GaussKruger6;
            globe.Zone = 20;                        // 6 度带 20 带：114°E ~ 120°E
            globe.ViewLat = 5;                      // 低视角时带子会横跨可见经度边界，最容易暴露“饼状”裁剪
            double bandStart = ZoneGlobe.ZoneStart(ZoneSystem.GaussKruger6, 20);
            double bandCentral = ZoneGlobe.ZoneCentral(ZoneSystem.GaussKruger6, 20);
            int total = 0, outside = 0;
            foreach (double offset in new[] { 0.0, -91.0, 91.0, -60.0, 60.0 })
            {
                globe.ViewLon = bandCentral + offset;
                using (var bitmap = new Bitmap(320, 320))
                {
                    globe.DrawToBitmap(bitmap, new Rectangle(0, 0, 320, 320));
                    bitmap.Save(Path.Combine(outputDir, "globe-band-" + ((int)offset) + ".png"), ImageFormat.Png);
                    for (int y = 0; y < 320; y++)
                        for (int x = 0; x < 320; x++)
                        {
                            Color c = bitmap.GetPixel(x, y);
                            if (!(c.R > c.B + 30 && c.G > c.B + 15)) continue;   // 黄色高亮（含橙色边界与带号标注）
                            // 球面边缘附近反算经纬度严重病态（1 像素误差对应几十度），排除紧邻边缘的像素
                            float dx = x - globe.DiscCenter.X, dy = y - globe.DiscCenter.Y;
                            if (dx * dx + dy * dy > (globe.DiscRadius - 2.5) * (globe.DiscRadius - 2.5)) continue;
                            double lon, lat;
                            if (!globe.UnprojectPoint(x, y, out lon, out lat)) continue;
                            total++;
                            double d = lon - bandStart;
                            while (d < -180) d += 360;
                            while (d > 180) d -= 360;
                            if (d < -6 || d > 12) outside++;   // 6 度带宽 + 6 度容差（标注文字与抗锯齿）
                        }
                }
            }
            Check(total > 500, "分带高亮在球面上可见（" + total + " 个像素）");
            Check(outside * 100 <= total * 2,
                "分带高亮只绘制在地球表面的分带范围内（越界像素 " + outside + "/" + total + "）");
        }
        using (var picker = new ZonePickerForm(true))
        {
            picker.Zone = 50;
            picker.RowIndex = 14;
            Check(picker.Zone == 50 && picker.RowIndex == 14 && picker.IsNorth && picker.Kind == ZoneSystem.Utm,
                "UTM分带选择界面读写带号与纬度行");
            picker.RowIndex = 7;   // K 行：24°S~16°S
            Check(!picker.IsNorth && UtmGridZone.LetterAt(picker.RowIndex) == 'K', "UTM纬度行决定南北半球");
            Check(picker.ViewLat < -16 && picker.ViewLat > -24, "选择纬度行后地球移动到该行");
            picker.Zone = 30;      // 中央经线 6×30−183 = −3°
            Check(Math.Abs(picker.ViewLon + 3) < 1e-9, "选择带号后地球移动到该带中央经线");
            picker.DataExtent = new Envelope(116.25, 116.45, 39.85, 40.05);
            Check(picker.Zone == 50 && picker.RowIndex == 14 && Math.Abs(picker.ViewLon - 116.35) < 1e-9
                && picker.ViewLat > 35 && picker.ViewLat < 41,
                "按数据范围选择后带号/纬度行/地球位置一起更新");
            picker.DataExtent = null;
        }
        using (var picker = new ZonePickerForm(false))
        {
            picker.Is3Degree = false;
            picker.Zone = 20;
            bool six = picker.Zone == 20 && picker.Kind == ZoneSystem.GaussKruger6;
            picker.Is3Degree = true;
            picker.Zone = 39;
            Check(six && picker.Zone == 39 && picker.Kind == ZoneSystem.GaussKruger3, "分带选择界面可切换3度带/6度带");
        }

        // 4.8 的 ComboBox UIA 提供程序派生自非 COM 可见类，会触发 NonComVisibleBaseClass 托管调试助手；
        // App.config 中已通过 AppContext 开关恢复旧版辅助功能实现，这里做一次回归检查。
        using (var probe = new ComboBox())
        {
            var handle = probe.Handle;
            string accessibleType = probe.AccessibilityObject.GetType().Name;
            Check(accessibleType != "ComboBoxUiaProvider",
                "已避免使用 4.8 的 ComboBox UIA 提供程序（当前为 " + accessibleType + "）");
        }

        // 3) 色带系统
        using (var combo = new ColorRampComboBox())
        {
            Check(combo.Ramps.Count >= 5 && combo.Items.Count == combo.Ramps.Count + 1,
                "色带下拉框列出内置色带并带“自定义色带”项");
            Check(combo.Items[combo.Items.Count - 1].ToString() == ColorRampComboBox.CustomItemText,
                "色带下拉框最后一项为自定义色带");
            combo.SelectedIndex = combo.Items.Count - 1;
            Check(combo.IsCustomSelected && combo.SelectedRamp == null, "选中自定义色带项时进入色带编辑器");
        }
        using (var editor = new ColorRampEditor(ColorRamp.CreateDefault()))
        {
            var texts = AllButtons(editor).Select(b => b.Text).ToList();
            Check(texts.Contains("新增节点") && texts.Contains("删除节点") && texts.Contains("均匀分布节点")
                && texts.Contains("设置节点位置") && texts.Contains("设置节点颜色")
                && texts.Contains("设置与上一个节点间的插值方法"), "色带编辑器含六个节点编辑按钮");
            Check(texts.Contains("应用") && texts.Contains("确定") && texts.Contains("取消") && texts.Contains("退出")
                && texts.Contains("保存为色带文件") && texts.Contains("读取色带文件"),
                "色带编辑器含应用/确定/取消/退出与保存/读取按钮");
            Check(editor.Working.Count == 3, "色带编辑器在工作副本上编辑节点链表");
        }
        var strip = new RampStrip { Size = new Size(400, 116), Ramp = ColorRamp.CreateDefault() };
        Check(strip.Ramp.Head != null && strip.SelectedNode == strip.Ramp.Head, "色带显示条默认选中首节点");
        strip.Dispose();

        // 2.1) 高斯-克吕格西半球分带（负带号）
        Check(ZoneGlobe.ZoneOf(ZoneSystem.GaussKruger3, -100) == -33
            && Math.Abs(ZoneGlobe.ZoneCentral(ZoneSystem.GaussKruger3, -33) + 99) < 1e-9,
            "高斯-克吕格3度带可反算西半球负带号");
        Check(ZoneGlobe.ZoneOf(ZoneSystem.GaussKruger6, -100) == -16
            && Math.Abs(ZoneGlobe.ZoneCentral(ZoneSystem.GaussKruger6, -16) + 99) < 1e-9,
            "高斯-克吕格6度带可反算西半球负带号");
        Check(ZoneGlobe.MinZone(ZoneSystem.GaussKruger3) == -60 && ZoneGlobe.MaxZone(ZoneSystem.GaussKruger3) == 60
            && ZoneGlobe.MinZone(ZoneSystem.Utm) == 1, "高斯-克吕格带号范围含负值，UTM 仍为 1~60");
        using (var westGlobe = new ZoneGlobe { Size = new Size(300, 300) })
        {
            var westHandle = westGlobe.Handle;
            westGlobe.Kind = ZoneSystem.GaussKruger3;
            westGlobe.Zone = -33;
            westGlobe.ViewLon = -99;
            westGlobe.ViewLat = 20;
            int yellow = 0;
            using (var bitmap = new Bitmap(300, 300))
            {
                westGlobe.DrawToBitmap(bitmap, new Rectangle(0, 0, 300, 300));
                for (int y = 0; y < 300; y++)
                    for (int x = 0; x < 300; x++)
                    {
                        Color c = bitmap.GetPixel(x, y);
                        if (c.R > c.B + 30 && c.G > c.B + 15) yellow++;
                    }
            }
            Check(westGlobe.Zone == -33 && yellow > 200, "西半球分带可在球面上选中并高亮");
        }
        using (var westPicker = new ZonePickerForm(false))
        {
            westPicker.Is3Degree = true;
            westPicker.Zone = -33;
            Check(westPicker.Zone == -33 && Math.Abs(westPicker.ViewLon + 99) < 1e-9,
                "分带界面可选择西半球负带号并定位到该带");
        }

        // 3.3) 自定义线符号的新特性：偏移 / 延长 / 弧线，都必须真实影响绘制
        var plainLine = DashLine(false, false, false);
        var arcLine = DashLine(true, false, false);
        var offsetLine = DashLine(false, true, false);
        var extendLine = DashLine(false, false, true);
        using (var plain = DrawLineSample(plainLine))
        using (var arc = DrawLineSample(arcLine))
        using (var offset = DrawLineSample(offsetLine))
        using (var extend = DrawLineSample(extendLine))
        {
            Check(DiffPixels(plain, arc) > 100, "弧线特性会实际把线段画成波浪（" + DiffPixels(plain, arc) + " 像素不同）");
            Check(DiffPixels(plain, offset) > 20, "偏移特性会把本段整体平移");
            Check(DiffPixels(plain, extend) > 100, "延长特性会把本段两端向外加长");
            Check(DiffPixels(arc, offset) > 100, "弧线与偏移是互相独立的特性");
        }

        // 3.5) 新增的三种“城市点位”符号：五角星（首都）/ 实心点圆环（省会）/ 空心点圆环（普通城市）
        var starSymbol = new SimpleMarkerSymbol { Style = SimpleMarkerSymbolStyleConstant.Star, Color = Color.FromArgb(186, 58, 45), Size = 20 };
        var dotRingSymbol = new SimpleMarkerSymbol { Style = SimpleMarkerSymbolStyleConstant.SolidDotCircle, Color = Color.FromArgb(214, 122, 30), Size = 20 };
        var hollowRingSymbol = new SimpleMarkerSymbol { Style = SimpleMarkerSymbolStyleConstant.HollowDotCircle, Color = Color.FromArgb(70, 104, 150), Size = 20 };
        var circleSymbol = new SimpleMarkerSymbol { Style = SimpleMarkerSymbolStyleConstant.Circle, Color = Color.FromArgb(186, 58, 45), Size = 20 };
        using (var star = DrawMarkerSample(starSymbol))
        using (var dotRing = DrawMarkerSample(dotRingSymbol))
        using (var hollowRing = DrawMarkerSample(hollowRingSymbol))
        using (var circle = DrawMarkerSample(circleSymbol))
        {
            Check(InkAt(star, 60, 60) && InkAt(star, 60, 40) && !InkAt(star, 75, 40) && !InkAt(star, 60, 18),
                "五角星：中心与上方尖角有墨、右上凹口与外部无墨（是星形而不是圆）");
            Check(InkAt(circle, 75, 40), "同一位置圆形符号仍是实心的（对照，说明检查有效）");
            Check(InkAt(dotRing, 60, 60) && !InkInRow(dotRing, 60, 60, 20, 32) && InkInRow(dotRing, 60, 60, 35, 39),
                "中心实心点圆环：中心实心、点与圈之间为空、外圈有墨");
            Check(!InkAt(hollowRing, 60, 60) && InkInRow(hollowRing, 60, 60, 10, 16)
                && !InkInRow(hollowRing, 60, 60, 20, 32) && InkInRow(hollowRing, 60, 60, 35, 39),
                "中心空心点圆环：中心为空、中心小圈与外圈有墨、两者之间为空");
            Check(DiffPixels(star, circle) > 200 && DiffPixels(dotRing, hollowRing) > 100,
                "三种新符号与圆形符号、彼此之间的绘制结果都不同");

            // 把七种点符号画成一张对照图存到输出目录，便于人工核对形状
            var samples = new[]
            {
                new SimpleMarkerSymbol { Style = SimpleMarkerSymbolStyleConstant.Circle, Color = Color.FromArgb(70, 104, 150), Size = 14 },
                new SimpleMarkerSymbol { Style = SimpleMarkerSymbolStyleConstant.Square, Color = Color.FromArgb(70, 104, 150), Size = 14 },
                new SimpleMarkerSymbol { Style = SimpleMarkerSymbolStyleConstant.Triangle, Color = Color.FromArgb(70, 104, 150), Size = 14 },
                new SimpleMarkerSymbol { Style = SimpleMarkerSymbolStyleConstant.Cross, Color = Color.FromArgb(70, 104, 150), Size = 14 },
                new SimpleMarkerSymbol { Style = SimpleMarkerSymbolStyleConstant.Star, Color = Color.FromArgb(186, 58, 45), Size = 14 },
                new SimpleMarkerSymbol { Style = SimpleMarkerSymbolStyleConstant.SolidDotCircle, Color = Color.FromArgb(214, 122, 30), Size = 14 },
                new SimpleMarkerSymbol { Style = SimpleMarkerSymbolStyleConstant.HollowDotCircle, Color = Color.FromArgb(70, 104, 150), Size = 14 }
            };
            var captions = new[] { "圆形", "方形", "三角形", "十字", "五角星", "实心点圆环", "空心点圆环" };
            using (var markerStrip = new Bitmap(samples.Length * 96, 116))
            {
                using (var graphics = Graphics.FromImage(markerStrip))
                {
                    graphics.Clear(Color.White);
                    graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    for (int i = 0; i < samples.Length; i++)
                    {
                        BasicGeometryDrawer.DrawSymbol(graphics, samples[i], new Rectangle(i * 96, 0, 96, 88));
                        using (var font = new Font("Microsoft YaHei UI", 9F))
                        using (var format = new StringFormat { Alignment = StringAlignment.Center })
                            graphics.DrawString(captions[i], font, Brushes.Black, i * 96 + 48, 92, format);
                    }
                }
                markerStrip.Save(Path.Combine(outputDir, "marker-symbols.png"), ImageFormat.Png);
            }
        }

        // 3.6) 注记渲染：开关 / 字号 / 颜色 / 宽高比 / 旋转 / 描边 / 定位
        using (var labelMap = new MapControl { Size = new Size(420, 320) })
        {
            var labelHandle = labelMap.Handle;
            var pointLayer = SampleData.MakePlanarLayer("注记点", GeometryTypeConstant.Point,
                new SimpleMarkerSymbol { Color = Color.Blue, Size = 4 }, "POINT (50 50)");
            pointLayer.LabelRenderer = new LabelRenderer
            {
                LabelFeatures = true,
                Field = "名称",
                TextSymbol = new TextSymbol
                {
                    FontName = "Microsoft YaHei UI", FontSize = 16,
                    FontColor = Color.Red, MaskColor = Color.Yellow
                }
            };
            labelMap.AddLayer(pointLayer);
            labelMap.SetExtent(new Envelope(0, 100, 0, 100));
            Application.DoEvents();
            ScreenPoint pointOnScreen = labelMap.Transform.MapToScreen(new Coordinate(50, 50));
            using (Bitmap labels = Render(labelMap))
                Check(CountColor(labels, Color.Red, 60) > 20, "开启注记后文字被绘制出来");
            pointLayer.LabelRenderer.LabelFeatures = false;
            using (Bitmap labels = Render(labelMap))
                Check(CountColor(labels, Color.Red, 60) == 0, "关闭注记后文字不再绘制");
            pointLayer.LabelRenderer.LabelFeatures = true;

            int width16, height16;
            using (Bitmap labels = Render(labelMap))
            {
                Rectangle bounds = ColorBounds(labels, Color.Red, 60);
                width16 = bounds.Width; height16 = bounds.Height;
            }
            pointLayer.LabelRenderer.TextSymbol.FontSize = 28;
            using (Bitmap labels = Render(labelMap))
            {
                Rectangle bounds = ColorBounds(labels, Color.Red, 60);
                Check(bounds.Width > width16 * 1.3 && bounds.Height > height16 * 1.3,
                    "字号越大注记越大（28 号墨迹 " + bounds.Width + "×" + bounds.Height + " 明显大于 16 号 " + width16 + "×" + height16 + "）");
            }
            pointLayer.LabelRenderer.TextSymbol.FontSize = 16;

            pointLayer.LabelRenderer.TextSymbol.FontRatio = 0.6;
            int narrowWidth, narrowHeight;
            using (Bitmap labels = Render(labelMap))
            {
                Rectangle bounds = ColorBounds(labels, Color.Red, 60);
                narrowWidth = bounds.Width; narrowHeight = bounds.Height;
            }
            pointLayer.LabelRenderer.TextSymbol.FontRatio = 1.8;
            using (Bitmap labels = Render(labelMap))
            {
                Rectangle bounds = ColorBounds(labels, Color.Red, 60);
                Check(bounds.Width > narrowWidth * 1.6 && Math.Abs(bounds.Height - narrowHeight) <= 6,
                    "宽高比只改水平方向：1.8 比 0.6 明显更宽、高度基本不变（" + narrowWidth + " → " + bounds.Width + " 像素）");
            }
            pointLayer.LabelRenderer.TextSymbol.FontRatio = 1;

            pointLayer.LabelRenderer.RotateAngle = 90;
            using (Bitmap labels = Render(labelMap))
            {
                Rectangle rotated = ColorBounds(labels, Color.Red, 60);
                Check(rotated.Height > rotated.Width && width16 > height16, "旋转 90° 后注记的横向/纵向包围盒互换");
            }
            pointLayer.LabelRenderer.RotateAngle = 0;
            pointLayer.LabelRenderer.TextSymbol.UseMask = true;   // 打开描边（晕圈）
            using (Bitmap labels = Render(labelMap))
            {
                Check(CountColor(labels, Color.Yellow, 60) > 20, "开启描边后文字周围出现描边颜色（晕圈）");
                Rectangle pointInk = ColorBounds(labels, Color.Red, 60);
                Check(pointInk.Left >= pointOnScreen.X + 3 && pointInk.Bottom <= pointOnScreen.Y,
                    "点要素的注记整体落在符号右上方（点位 x=" + pointOnScreen.X + "，注记左边界 " + pointInk.Left + "，下边界 " + pointInk.Bottom + "）");
            }
            pointLayer.LabelRenderer.TextSymbol.UseMask = false;

            labelMap.RemoveLayer(pointLayer);
            var lineLayer = SampleData.MakePlanarLayer("注记线", GeometryTypeConstant.LineString,
                new SimpleLineSymbol { Color = Color.Blue, Size = 1 }, "LINESTRING (10 60, 90 60)");
            lineLayer.LabelRenderer = new LabelRenderer
            {
                LabelFeatures = true, Field = "名称",
                TextSymbol = new TextSymbol
                {
                    FontName = "Microsoft YaHei UI", FontSize = 16, FontColor = Color.Red, MaskColor = Color.Yellow
                }
            };
            labelMap.AddLayer(lineLayer);
            Application.DoEvents();
            ScreenPoint lineMiddle = labelMap.Transform.MapToScreen(new Coordinate(50, 60));
            using (Bitmap labels = Render(labelMap))
            {
                Rectangle lineInk = ColorBounds(labels, Color.Red, 60);
                Check(Math.Abs(lineInk.Left + lineInk.Width / 2f - lineMiddle.X) < 12
                    && Math.Abs(lineInk.Top + lineInk.Height / 2f - lineMiddle.Y) < 12,
                    "线要素的注记以折线中点为中心居中摆放");
            }

            labelMap.RemoveLayer(lineLayer);
            var areaLayer = SampleData.MakePlanarLayer("注记面", GeometryTypeConstant.Polygon,
                new SimpleFillSymbol { Color = Color.FromArgb(60, Color.Blue) }, "POLYGON ((10 10, 90 10, 90 90, 10 90, 10 10))");
            areaLayer.LabelRenderer = new LabelRenderer
            {
                LabelFeatures = true, Field = "名称",
                TextSymbol = new TextSymbol
                {
                    FontName = "Microsoft YaHei UI", FontSize = 16, FontColor = Color.Red, MaskColor = Color.Yellow
                }
            };
            labelMap.AddLayer(areaLayer);
            Application.DoEvents();
            ScreenPoint areaMiddle = labelMap.Transform.MapToScreen(new Coordinate(50, 50));
            using (Bitmap labels = Render(labelMap))
            {
                Rectangle areaInk = ColorBounds(labels, Color.Red, 60);
                Check(Math.Abs(areaInk.Left + areaInk.Width / 2f - areaMiddle.X) < 14
                    && Math.Abs(areaInk.Top + areaInk.Height / 2f - areaMiddle.Y) < 14,
                    "面要素的注记以外包矩形中心为中心居中摆放");
            }
        }

        // 3.7.1 冲突判定：用注记实际占据的平行四边形（不是外接矩形）
        {
            var size = new SizeF(40, 16);
            PointF[] a = LabelPlacer.Quad(new PointF(0, 0), size, 0);
            PointF[] b = LabelPlacer.Quad(new PointF(30, 8), size, 0);
            PointF[] far = LabelPlacer.Quad(new PointF(200, 200), size, 0);
            Check(LabelPlacer.Conflicts(a, b) && !LabelPlacer.Conflicts(a, far),
                "注记冲突判定：相交判为冲突、远离不冲突");
            // 旋转 90°：横排在下方、互不冲突的两条注记，其中一条竖排后会转上来压住另一条
            PointF[] belowFlat = LabelPlacer.Quad(new PointF(0, 30), size, 0);
            PointF[] belowTurned = LabelPlacer.Quad(new PointF(0, 30), size, 90);
            Check(!LabelPlacer.Conflicts(a, belowFlat) && LabelPlacer.Conflicts(a, belowTurned),
                "注记冲突判定考虑旋转：横排不冲突的两条注记，其中一条竖排后会判为冲突");
            // 关键回归：两条 45° 注记的外接矩形相交，但实际平行四边形并不相接 → 不应误报冲突
            PointF[] diagA = LabelPlacer.Quad(new PointF(0, 0), size, 45);
            PointF[] diagB = LabelPlacer.Quad(new PointF(20, 20), size, 45);
            Check(LabelPlacer.Bounds(new PointF(0, 0), size, 45).IntersectsWith(LabelPlacer.Bounds(new PointF(20, 20), size, 45))
                && !LabelPlacer.Conflicts(diagA, diagB, 0),
                "压盖检验用实际平行四边形：外接矩形相交但形状不冲突时不误报");
            PointF[] diagNear = LabelPlacer.Quad(new PointF(5, 5), size, 45);
            Check(LabelPlacer.Conflicts(diagA, diagNear, 0), "实际平行四边形相交时仍判为冲突");
            // 间隙：水平相距 1 像素
            PointF[] nearly = LabelPlacer.Quad(new PointF(41, 0), size, 0);
            Check(!LabelPlacer.Conflicts(a, nearly, 0) && LabelPlacer.Conflicts(a, nearly, LabelPlacer.Padding),
                "注记之间保留最小间隙（默认 " + LabelPlacer.Padding + " 像素）");
        }

        // 3.7.2 避让效果：8 个几乎重合的点要素 —— 关闭避让时注记全部叠在一起（只看到一条），
        //       开启避让后摊到 4 个候选位置（其余候选全被占用 → 省略，而不是叠字）
        using (var avoidMap = new MapControl { Size = new Size(420, 320) })
        {
            var avoidHandle = avoidMap.Handle;
            var wkts = new System.Collections.Generic.List<string>();
            for (int i = 0; i < 8; i++) wkts.Add("POINT (" + (50 + i * 0.05).ToString("0.##") + " 50)");
            var crowded = SampleData.MakePlanarLayer("密集注记点", GeometryTypeConstant.Point,
                new SimpleMarkerSymbol { Color = Color.Blue, Size = 4 }, wkts.ToArray());
            crowded.LabelRenderer = new LabelRenderer
            {
                LabelFeatures = true, Field = "名称", AvoidOverlap = true,
                TextSymbol = new TextSymbol { FontName = "Microsoft YaHei UI", FontSize = 14, FontColor = Color.Red }
            };
            avoidMap.AddLayer(crowded);
            avoidMap.SetExtent(new Envelope(0, 100, 0, 100));
            Application.DoEvents();
            var drawer = avoidMap.Renderer as BasicGeometryDrawer;
            int inkAvoid, inkAll;
            using (Bitmap labels = Render(avoidMap))
                inkAvoid = CountColor(labels, Color.Red, 60);
            int drawnAvoid = drawer.LastDrawnLabelCount, skippedAvoid = drawer.LastSkippedLabelCount;
            Check(drawnAvoid == 4 && skippedAvoid == 4,
                "8 个重合要素：避让时摊到 4 个候选位置、其余 4 条省略（画出 " + drawnAvoid + "、省略 " + skippedAvoid + "）");
            crowded.LabelRenderer.AvoidOverlap = false;
            using (Bitmap labels = Render(avoidMap))
                inkAll = CountColor(labels, Color.Red, 60);
            Check(drawer.LastDrawnLabelCount == 8 && drawer.LastSkippedLabelCount == 0,
                "关闭避让后 8 条注记全部绘制（画在同一位置，叠成一条）");
            Check(inkAvoid > inkAll * 1.3,
                "避让让注记摊开而不是叠在一起：红色墨迹 " + inkAll + " → " + inkAvoid + " 像素");
        }

        // 3.7.3 注记设置窗口：按钮齐备、“应用”立即写回图层、“取消”回滚、预览随设置变化
        {
            var formLayer = SampleData.MakePlanarLayer("注记窗口", GeometryTypeConstant.Point,
                new SimpleMarkerSymbol { Color = Color.Blue, Size = 4 }, "POINT (50 50)");
            int appliedCount = 0;
            using (var form = LabelRendererForm.Create(formLayer, () => appliedCount++))
            {
                form.ShowInTaskbar = false;
                form.Opacity = 0;
                form.Show();
                Application.DoEvents();
                var texts = AllButtons(form).Select(x => x.Text).ToList();
                Check(texts.Contains("应用") && texts.Contains("退出") && texts.Contains("确定") && texts.Contains("取消"),
                    "注记设置窗口提供 应用 / 确定 / 取消 / 退出 四个按钮");

                // 预览必须随宽高比、旋转角度变化（此前这两项不影响预览）
                var ratioBox = AllNumerics(form).FirstOrDefault(n => n.Minimum == 0.5m && n.Maximum == 3m);
                var rotateBox = AllNumerics(form).FirstOrDefault(n => n.Minimum == -360m && n.Maximum == 360m);
                Check(ratioBox != null && rotateBox != null, "注记设置窗口有宽高比与旋转角度输入框");
                ratioBox.Value = 0.6m;
                Application.DoEvents();
                int narrow;
                using (Bitmap p = form.RenderPreview()) narrow = InkBounds(p, Color.White).Width;
                ratioBox.Value = 1.8m;
                Application.DoEvents();
                int wide;
                using (Bitmap p = form.RenderPreview()) wide = InkBounds(p, Color.White).Width;
                Check(wide > narrow * 1.5, "改宽高比后预览立即变化（" + narrow + " → " + wide + " 像素宽）");
                ratioBox.Value = 1m;

                rotateBox.Value = 0m;
                Application.DoEvents();
                Rectangle flat;
                using (Bitmap p = form.RenderPreview()) flat = InkBounds(p, Color.White);
                rotateBox.Value = 90m;
                Application.DoEvents();
                Rectangle turned;
                using (Bitmap p = form.RenderPreview()) turned = InkBounds(p, Color.White);
                Check(turned.Height > turned.Width && flat.Width > flat.Height,
                    "改旋转角度后预览立即变化（0° 时 " + flat.Width + "×" + flat.Height + "，90° 时 " + turned.Width + "×" + turned.Height + "）");
                rotateBox.Value = 0m;

                // “应用”：立即写回图层并通知外部刷新
                foreach (Button b in AllButtons(form)) if (b.Text == "应用") b.PerformClick();
                Check(appliedCount > 0 && formLayer.LabelRenderer != null
                    && Math.Abs(formLayer.LabelRenderer.TextSymbol.FontRatio - 1) < 1e-9,
                    "点“应用”立即写回图层（宽高比已按界面值 1.0 保存）并通知刷新");
                rotateBox.Value = 45m;
                foreach (Button b in AllButtons(form)) if (b.Text == "应用") b.PerformClick();
                Check(formLayer.LabelRenderer.RotateAngle == 45, "再次点“应用”会把新的旋转角度写回图层");
                foreach (Button b in AllButtons(form)) if (b.Text == "取消") b.PerformClick();
                Check(formLayer.LabelRenderer == null || Math.Abs(formLayer.LabelRenderer.RotateAngle) < 1e-9,
                    "点“取消”回滚到打开窗口时的状态（打开前没有注记设置 → 回滚后仍为空）");
                form.Hide();
            }
        }

        // 3.7.4 设置旋转角后按方位重新计算锚点：把“实际平行四边形离要素最近的角”贴到偏移位置上
        //       （此前按外接矩形摆放，旋转后注记会与要素忽远忽近）
        {
            var boxSize = new SizeF(90, 20);
            var centre = new PointF(210, 160);
            const float radius = 7.6f, gap = 3f, near = 1.2f;
            float axis = radius + gap;                     // 每个轴上的偏移量
            float diagonal = (float)(axis * Math.Sqrt(2)); // 最近角到要素中心的距离（斜向）
            bool allQuadrants = true, allAngles = true, sameAsFlat = true;
            var angles = new[] { 0.0, 10.0, 30.0, 45.0, 90.0, -45.0, 135.0 };
            foreach (double angle in angles)
            {
                PointF[] candidates = LabelPlacer.AroundPoint(centre, boxSize, radius, gap, angle);
                // 右上、右下、左上、左下：各自期望最近角所在的方位
                var expectRight = new[] { true, true, false, false };
                var expectTop = new[] { true, false, true, false };
                for (int i = 0; i < candidates.Length; i++)
                {
                    PointF[] quad = LabelPlacer.Quad(candidates[i], boxSize, angle);
                    float nearest = float.MaxValue;
                    PointF nearestPoint = quad[0];
                    foreach (PointF v in quad)
                    {
                        float d = (float)Math.Sqrt((v.X - centre.X) * (v.X - centre.X) + (v.Y - centre.Y) * (v.Y - centre.Y));
                        if (d < nearest) { nearest = d; nearestPoint = v; }
                    }
                    // 最近角与要素中心的距离应恰好是 √(dx²+dy²)（即贴住要素外侧的偏移点）
                    if (Math.Abs(nearest - diagonal) > near) allQuadrants = false;
                    // 且该角确实落在期望的方位上（右/左、上/下）
                    bool right = nearestPoint.X > centre.X, top = nearestPoint.Y < centre.Y;
                    if (right != expectRight[i] || top != expectTop[i]) allAngles = false;
                    // 角度为 0 时，最近角必须与不旋转时完全相同（不改变原有布局）
                    if (angle == 0)
                    {
                        float flatDiagonal = (float)Math.Sqrt(axis * axis + axis * axis);
                        if (Math.Abs(nearest - flatDiagonal) > 0.01f) sameAsFlat = false;
                    }
                }
            }
            Check(allQuadrants && allAngles,
                "旋转后按方位反推锚点：0°/10°/30°/45°/90°/-45°/135° 下四个方位的注记，离要素最近的角都恰好贴在偏移点上（斜向距离 " + diagonal.ToString("0.0") + " 像素）");
            Check(sameAsFlat, "角度为 0 时最近角与不旋转时完全一致（不改变原有布局）");

            // 线/面注记同理：旋转后仍以定位点为中心
            bool centered = true;
            foreach (double angle in angles)
            {
                PointF[] candidates = LabelPlacer.AroundCenter(centre, boxSize, 30f, angle);
                PointF[] quad = LabelPlacer.Quad(candidates[0], boxSize, angle);
                float minX = quad[0].X, maxX = quad[0].X, minY = quad[0].Y, maxY = quad[0].Y;
                foreach (PointF v in quad)
                {
                    minX = Math.Min(minX, v.X); maxX = Math.Max(maxX, v.X);
                    minY = Math.Min(minY, v.Y); maxY = Math.Max(maxY, v.Y);
                }
                if (Math.Abs((minX + maxX) / 2f - centre.X) > near) centered = false;
                if (Math.Abs((minY + maxY) / 2f - centre.Y) > near) centered = false;
            }
            Check(centered, "旋转后线/面注记仍以定位点为中心（不再按未旋转的宽高偏移）");
        }

        // 3.7.5 画出来验证：旋转 90° 的点注记紧贴符号右上方，而不是被“转出去”很远
        using (var rotateMap = new MapControl { Size = new Size(420, 320) })
        {
            var rotateHandle = rotateMap.Handle;
            var rotateLayer = SampleData.MakePlanarLayer("旋转注记", GeometryTypeConstant.Point,
                new SimpleMarkerSymbol { Color = Color.Blue, Size = 4 }, "POINT (50 50)");
            rotateLayer.LabelRenderer = new LabelRenderer
            {
                LabelFeatures = true, Field = "名称", RotateAngle = 90,
                TextSymbol = new TextSymbol { FontName = "Microsoft YaHei UI", FontSize = 14, FontColor = Color.Red }
            };
            rotateMap.AddLayer(rotateLayer);
            rotateMap.SetExtent(new Envelope(0, 100, 0, 100));
            Application.DoEvents();
            ScreenPoint rotatePoint = rotateMap.Transform.MapToScreen(new Coordinate(50, 50));
            using (Bitmap labels = Render(rotateMap))
            {
                Rectangle ink = ColorBounds(labels, Color.Red, 60);
                // 符号半径 ≈ 7.6 像素、间隙 3 像素：旋转后的注记左边界与下边界都应贴着这个偏移
                float dx = ink.Left - rotatePoint.X, dy = rotatePoint.Y - ink.Bottom;
                Check(ink.Left > rotatePoint.X && ink.Bottom < rotatePoint.Y && dx < 16 && dy < 16,
                    "旋转 90° 的点注记紧贴符号右上方（水平让开 " + dx.ToString("0.0") + " 像素、垂直让开 " + dy.ToString("0.0") + " 像素）");
            }
        }

        // 3.4) 面符号多边界：外环向外偏移、洞向内偏移（正偏移量 = 面整体扩张）
        using (var map = new MapControl { Size = new Size(600, 600) })
        {
            var mapHandle = map.Handle;
            int grownSpan = 0, erodedSpan = 0;
            foreach (int offsetValue in new[] { 3, -3 })
            {
                foreach (Layer old in map.Layers.ToArray()) map.RemoveLayer(old);
                var layer = SampleData.MakePlanarLayer("带洞面", GeometryTypeConstant.Polygon, null,
                    "POLYGON ((10 10, 90 10, 90 90, 10 90, 10 10), (40 40, 60 40, 60 60, 40 60, 40 40))");
                var symbol = new SimpleFillSymbol { Color = Color.Transparent };
                symbol.Outlines.Clear();
                symbol.Outlines.Add(new FillOutline(offsetValue, new SimpleLineSymbol { Color = Color.Red, Size = 0.5 }));
                layer.Symbol = symbol;
                layer.Renderer = null;
                map.AddLayer(layer);
                map.SetExtent(new Envelope(0, 100, 0, 100));
                using (var image = Render(map))
                {
                    ScreenPoint holeCenter = map.Transform.MapToScreen(new Coordinate(50, 50));
                    int span = BlankRunAt(image, holeCenter.Y, holeCenter.X);
                    if (offsetValue > 0) grownSpan = span; else erodedSpan = span;
                }
            }
            Check(grownSpan > 0 && erodedSpan > 0 && erodedSpan - grownSpan > 20,
                "面符号正偏移时外环向外、洞向内收缩（洞宽 " + grownSpan + " → " + erodedSpan + " 像素）");
        }

        // 3.1) 分级生成：符号数量与色带取色数量一致（含只有一个要素的图层）
        double[] flatBreaks = LayerRendererForm.BuildClassBreaks(new double[] { 39.9 }, 5, 0);
        Check(flatBreaks != null && flatBreaks.Length == 5 && flatBreaks[4] > flatBreaks[0],
            "图层只有一个要素（取值全同）时仍按分级数生成多个分级符号");
        Check(LayerRendererForm.BuildClassBreaks(new double[] { 2, 2, 2 }, 4, 1).Length == 4,
            "全部取值相同时不退化为一类");
        double[] normal = LayerRendererForm.BuildClassBreaks(new double[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }, 5, 0);
        Check(normal.Length == 5 && Math.Abs(normal[4] - 10) < 1e-9, "常规数据等间距分级为5级且末级为最大值");
        ColorRamp sampling = ColorRamp.CreateDefault();
        Color[] fiveColors = sampling.Sample(5);
        Check(fiveColors.Length == 5 && fiveColors[0] == sampling.GetColor(0)
            && fiveColors[1] == sampling.GetColor(0.25) && fiveColors[2] == sampling.GetColor(0.5)
            && fiveColors[3] == sampling.GetColor(0.75) && fiveColors[4] == sampling.GetColor(1),
            "按符号数量在色带上从左往右等间隔取色");

        // 3.2) 线符号取消“自定义虚线”后必须恢复为普通线型
        var dashSymbol = new SimpleLineSymbol { Color = Color.Black, Size = 1.5 };
        dashSymbol.DashElements.Add(new LineDashElement { Length = 3, Color = Color.Red, Width = 1.5 });
        dashSymbol.DashElements.Add(new LineDashElement { Length = 3, Color = Color.Transparent, Width = 0.01 });
        Check(dashSymbol.HasCustomDash, "含虚线元素时按自定义虚线绘制");
        Bitmap dashImage = new Bitmap(140, 40);
        using (var graphics = Graphics.FromImage(dashImage))
            BasicGeometryDrawer.DrawSymbol(graphics, dashSymbol, new Rectangle(0, 0, 140, 40));
        dashSymbol.DashElements.Clear();
        Check(!dashSymbol.HasCustomDash, "清空虚线元素后 HasCustomDash 为 false");
        Bitmap plainImage = new Bitmap(140, 40);
        using (var graphics = Graphics.FromImage(plainImage))
            BasicGeometryDrawer.DrawSymbol(graphics, dashSymbol, new Rectangle(0, 0, 140, 40));
        bool changed = false;
        for (int x = 4; x < 136 && !changed; x++)
        {
            Color a = dashImage.GetPixel(x, 20), b = plainImage.GetPixel(x, 20);
            if (Math.Abs(a.R - b.R) > 8 || Math.Abs(a.G - b.G) > 8 || Math.Abs(a.B - b.B) > 8) changed = true;
        }
        Check(changed, "取消自定义虚线后线符号恢复为普通线型绘制");
        dashImage.Dispose();
        plainImage.Dispose();

        // 4) 渲染符号文件 + 绑定属性错误
        using (var map = new MapControl { Size = new Size(400, 300) })
        using (var manager = new LayerManagerControl { Size = new Size(240, 400) })
        {
            var handle = map.Handle;
            manager.Bind(map);
            var broken = new ClassBreaksRenderer();
            broken.Field = "高程";   // 样例图层的字段为 名称/类型/数值，"高程"不存在
            broken.AddBreakValue(10, new SimpleFillSymbol { Color = Color.Red });
            broken.AddBreakValue(20, new SimpleFillSymbol { Color = Color.Blue });
            var layer = SampleData.MakePlanarLayer("分区", GeometryTypeConstant.Polygon,
                new SimpleFillSymbol { Color = Color.Green }, "POLYGON ((10 10, 90 10, 90 90, 10 90, 10 10))");
            layer.Renderer = RendererFile.Parse(
                RendererFile.ToLines(broken, GeometryTypeConstant.Polygon), RendererFile.PolygonExtension, layer.FeatureClass);
            map.AddLayer(layer);
            map.SetExtent(new Envelope(0, 100, 0, 100));
            Check(layer.Renderer.HasBindingError && layer.Renderer.BindingErrorField == "高程",
                "导入渲染符号后绑定字段缺失被标记为绑定属性错误");
            using (Bitmap image = Render(map))
                Check(ColorAt(image, map.Transform.MapToScreen(new Coordinate(50, 50)), Color.White),
                    "绑定属性错误的符号不绘制到地图上");
            Check(manager.LayerRows.Count == 1, "图层面板包含该图层");
            bool nameRed = false, errorText = false;
            foreach (Label label in AllLabels(manager.LayerRows[0]))
            {
                if (label.Text.StartsWith("分区") && label.ForeColor.ToArgb() == Color.Red.ToArgb()) nameRed = true;
                if (label.Text.Contains(Renderer.BindingErrorText) && label.ForeColor.ToArgb() == Color.Red.ToArgb()) errorText = true;
            }
            Check(nameRed, "绑定属性错误时图层名以红色显示");
            Check(errorText, "绑定属性错误时图例中符号后显示“绑定属性错误”");
            // 重新绑定字段后可恢复
            var fixedRenderer = RendererFile.Parse(RendererFile.ToLines(broken, GeometryTypeConstant.Polygon),
                RendererFile.PolygonExtension, null);
            Check(!fixedRenderer.HasBindingError, "不校验目标图层时导入结果不带绑定错误");
            fixedRenderer.ClearBindingError();
            layer.Renderer = fixedRenderer;
            map.RefreshMap();
            Application.DoEvents();
            foreach (LayerControl row in manager.LayerRows)
                row.RefreshView();
            bool stillRed = false;
            foreach (Label label in AllLabels(manager.LayerRows[0]))
                if (label.ForeColor.ToArgb() == Color.Red.ToArgb()) stillRed = true;
            Check(!stillRed, "重新绑定字段后图层名恢复为常规颜色");
        }

        // 4b) 回归：分段渲染处于“绑定属性错误 / 没有默认符号”时，重新生成符号不得报错，且默认符号要能继续设置
        //     现象：符号为 error 后，在渲染设置里点“生成 / 加载所有值”抛 NullReferenceException，
        //     默认符号为空、双击也设不了。根因是渲染符号文件不保存默认符号，且界面按“非空”使用它。
        var breakFc = new FeatureClass("分级图层", GeometryTypeConstant.Point);
        breakFc.Fields.Add(new Field("名称", FieldTypeConstant.Text));
        breakFc.Fields.Add(new Field("数值", FieldTypeConstant.Double));
        for (int i = 0; i < 3; i++)
        {
            var f = new Feature(new GIS.Point(new Coordinate(10 + i * 5, 10)), breakFc.Fields);
            f.Attributes.SetItem("名称", "点" + (i + 1));
            f.Attributes.SetItem("数值", (i + 1) * 10.0);
            breakFc.Add(f);
        }
        var breakLayer = new Layer("分级图层", breakFc) { Symbol = new SimpleMarkerSymbol { Color = Color.Gray } };
        var legacyRenderer = new ClassBreaksRenderer();
        legacyRenderer.Field = "高程";   // 图层里没有该字段 → 读入后进入“绑定属性错误”
        legacyRenderer.AddBreakValue(10, new SimpleMarkerSymbol { Color = Color.Red });
        breakLayer.Renderer = RendererFile.Parse(
            RendererFile.ToLines(legacyRenderer, GeometryTypeConstant.Point), RendererFile.PointExtension, breakFc);
        Check(breakLayer.Renderer.HasBindingError
            && ((ClassBreaksRenderer)breakLayer.Renderer).DefaultSymbol == null,
            "旧版文件读入的分级渲染器：既没有默认符号，又处于绑定属性错误状态");
        using (var rendererForm = LayerRendererForm.Create(breakLayer))
        {
            rendererForm.ShowInTaskbar = false;
            rendererForm.Opacity = 0;
            rendererForm.Show();
            Application.DoEvents();
            var tabs = FindControl<TabControl>(rendererForm);
            Button find(Control root, string text)
            {
                foreach (Button b in AllButtons(root)) if (b.Text == text) return b;
                return null;
            }
            tabs.SelectedIndex = 2;   // 分级渲染选项卡
            Button gen = find(rendererForm, "生成");
            Check(gen != null, "分级渲染选项卡提供“生成”按钮");
            gen.PerformClick();       // 修复前这里抛 NullReferenceException（克隆空默认符号）
            Button apply = find(rendererForm, "应用");
            apply.PerformClick();
            var applied = breakLayer.Renderer as ClassBreaksRenderer;
            Check(applied != null && applied.BreakCount > 0 && !applied.HasBindingError,
                "绑定错误状态下重新“生成”不再报错，分级符号建立且错误状态被清除");
            Check(applied != null && applied.DefaultSymbol != null,
                "重新生成后默认符号不再为空（可以继续设置）");
            rendererForm.Hide();
        }

        // 4c) 回归：唯一值渲染的默认符号为空时（旧版文件读入的情形），“加载所有值”同样不得报错
        var uniqueFc = new FeatureClass("唯一值图层", GeometryTypeConstant.Point);
        uniqueFc.Fields.Add(new Field("名称", FieldTypeConstant.Text));
        for (int i = 0; i < 3; i++)
        {
            var f = new Feature(new GIS.Point(new Coordinate(10 + i * 5, 10)), uniqueFc.Fields);
            f.Attributes.SetItem("名称", "点" + (i + 1));
            uniqueFc.Add(f);
        }
        var uniqueLayer = new Layer("唯一值图层", uniqueFc) { Symbol = new SimpleMarkerSymbol { Color = Color.Gray } };
        var uniqueSeed = new UniqueValueRenderer();
        uniqueSeed.Field = "名称";
        uniqueSeed.AddValue("点1", new SimpleMarkerSymbol { Color = Color.Red });
        uniqueLayer.Renderer = uniqueSeed;   // DefaultSymbol 为 null，相当于旧版文件读入
        Check(((UniqueValueRenderer)uniqueLayer.Renderer).DefaultSymbol == null,
            "唯一值渲染器可以没有默认符号（旧版文件读入的情形）");
        using (var uniqueForm = LayerRendererForm.Create(uniqueLayer))
        {
            uniqueForm.ShowInTaskbar = false;
            uniqueForm.Opacity = 0;
            uniqueForm.Show();
            Application.DoEvents();
            FindControl<TabControl>(uniqueForm).SelectedIndex = 1;   // 唯一值渲染选项卡
            Button load = null, apply2 = null;
            foreach (Button b in AllButtons(uniqueForm))
            {
                if (b.Text == "加载所有值") load = b;
                if (b.Text == "应用") apply2 = b;
            }
            load.PerformClick();       // 修复前这里抛 NullReferenceException
            apply2.PerformClick();
            var appliedUnique = uniqueLayer.Renderer as UniqueValueRenderer;
            Check(appliedUnique != null && appliedUnique.ValueCount == 3 && appliedUnique.DefaultSymbol != null,
                "默认符号为空时也能“加载所有值”，并补上可编辑的默认符号");
            uniqueForm.Hide();
        }

        // 4d) 回归：读取已有分级渲染符号后，图层面板下方显示的绑定字段必须能随重新绑定而更新
        //     （图例标题此前只在为空时跟随字段，读入文件后标题被写成旧字段名，于是看起来“字段固定、改不了”）
        {
            var fc2 = new FeatureClass("分级图层2", GeometryTypeConstant.Point);
            fc2.Fields.Add(new Field("名称", FieldTypeConstant.Text));
            fc2.Fields.Add(new Field("数值", FieldTypeConstant.Double));
            for (int i = 0; i < 3; i++)
            {
                var f = new Feature(new GIS.Point(new Coordinate(10 + i * 5, 10)), fc2.Fields);
                f.Attributes.SetItem("名称", "点" + (i + 1));
                f.Attributes.SetItem("数值", (i + 1) * 10.0);
                fc2.Add(f);
            }
            var layer2 = new Layer("分级图层2", fc2) { Symbol = new SimpleMarkerSymbol { Color = Color.Gray } };
            var seedRenderer = new ClassBreaksRenderer();
            seedRenderer.Field = "高程";   // 文件里绑定的字段（当前图层中不存在）
            seedRenderer.AddBreakValue(10, new SimpleMarkerSymbol { Color = Color.Red });
            layer2.Renderer = RendererFile.Parse(
                RendererFile.ToLines(seedRenderer, GeometryTypeConstant.Point), RendererFile.PointExtension, null);

            using (var map = new MapControl { Size = new Size(400, 300) })
            using (var manager = new LayerManagerControl { Size = new Size(240, 400) })
            {
                var handle = map.Handle;
                manager.Bind(map);
                map.AddLayer(layer2);
                map.RefreshMap();
                Application.DoEvents();
                foreach (LayerControl row in manager.LayerRows) row.RefreshView();
                Check(LegendHasText(manager, "高程"), "读取分级渲染符号后，图层面板显示文件里的绑定字段");

                using (var form = LayerRendererForm.Create(layer2))
                {
                    form.ShowInTaskbar = false;
                    form.Opacity = 0;
                    form.Show();
                    Application.DoEvents();
                    var tabs2 = FindControl<TabControl>(form);
                    tabs2.SelectedIndex = 2;                                        // 分级渲染选项卡
                    var fieldCombo = FindControl<ComboBox>(tabs2.TabPages[2]);      // 该选项卡里的“字段”下拉框
                    fieldCombo.SelectedItem = "数值";
                    Button gen2 = null, apply3 = null;
                    foreach (Button b in AllButtons(form))
                    {
                        if (b.Text == "生成") gen2 = b;
                        if (b.Text == "应用") apply3 = b;
                    }
                    gen2.PerformClick();
                    apply3.PerformClick();
                    form.Hide();
                }
                Check(layer2.Renderer is ClassBreaksRenderer && layer2.Renderer.BoundField == "数值",
                    "重新绑定后渲染器的绑定字段为“数值”");
                map.RefreshMap();
                Application.DoEvents();
                foreach (LayerControl row in manager.LayerRows) row.RefreshView();
                Check(LegendHasText(manager, "数值") && !LegendHasText(manager, "高程"),
                    "重新绑定字段后图层面板显示的字段同步更新（不再固定为旧字段）");
            }
        }

        // 4e) 回归：绑定属性错误时，图例里的错误符号不能再点开单独设置
        //     （此前点它会用“感叹号”形状去打开点符号编辑器，形状下拉框越界报错）
        {
            var errFc = new FeatureClass("错误图层", GeometryTypeConstant.Point);
            errFc.Fields.Add(new Field("名称", FieldTypeConstant.Text));
            errFc.Fields.Add(new Field("数值", FieldTypeConstant.Double));
            var errFeature = new Feature(new GIS.Point(new Coordinate(10, 10)), errFc.Fields);
            errFeature.Attributes.SetItem("名称", "点1");
            errFeature.Attributes.SetItem("数值", 5.0);
            errFc.Add(errFeature);
            var errLayer = new Layer("错误图层", errFc) { Symbol = new SimpleMarkerSymbol { Color = Color.Gray } };
            var errSeed = new ClassBreaksRenderer();
            errSeed.Field = "高程";
            errSeed.AddBreakValue(10, new SimpleMarkerSymbol { Color = Color.Red });
            errLayer.Renderer = RendererFile.Parse(RendererFile.ToLines(errSeed, GeometryTypeConstant.Point),
                RendererFile.PointExtension, errFc);
            Check(errLayer.Renderer.HasBindingError && !LayerControl.CanEditSymbolDirectly(errLayer.Renderer),
                "绑定属性错误时图例符号不可直接点击设置");
            Check(LayerControl.CanEditSymbolDirectly(new SimpleRenderer { Symbol = new SimpleMarkerSymbol() }),
                "正常渲染时图例符号仍可点击单独设置");

            // 点符号编辑器本身也要能容错打开“红色感叹号”错误符号（形状下拉框只有四种形状）
            using (var markerEditor = MarkerSymbolEditor.Create(Renderer.BindingErrorSymbol()))
            {
                markerEditor.ShowInTaskbar = false;
                markerEditor.Opacity = 0;
                markerEditor.Show();
                Application.DoEvents();
                var styleCombo = FindControl<ComboBox>(markerEditor);
                Check(styleCombo.SelectedIndex >= 0 && styleCombo.SelectedIndex < styleCombo.Items.Count,
                    "点符号编辑器打开“红色感叹号”符号不再越界（形状下拉框钳到合法项）");
                markerEditor.Hide();
            }

            using (var map = new MapControl { Size = new Size(400, 300) })
            using (var manager = new LayerManagerControl { Size = new Size(240, 400) })
            {
                var handle = map.Handle;
                manager.Bind(map);
                map.AddLayer(errLayer);
                map.RefreshMap();
                Application.DoEvents();
                foreach (LayerControl row in manager.LayerRows) row.RefreshView();
                int legendPanels = 0, clickable = 0, withMenu = 0;
                bool editItem = false;
                foreach (Control c in AllControls(manager.LayerRows[0]))
                {
                    if (!(c is Panel p) || p.Width != 60 || p.Height != 24) continue;   // 图例里的符号预览面板
                    legendPanels++;
                    if (p.Cursor == Cursors.Hand) clickable++;
                    if (p.ContextMenuStrip == null) continue;
                    withMenu++;
                    foreach (ToolStripItem item in p.ContextMenuStrip.Items)
                        if (item.Text != null && item.Text.Contains("修改")) editItem = true;
                }
                Check(legendPanels > 0 && clickable == 0, "错误符号不再显示为可点击（手型光标已取消）");
                Check(withMenu == legendPanels, "错误符号行仍挂着右键菜单，可进入渲染设置");
                Check(editItem, "右键菜单里有“修改图层符号/渲染”入口");
            }
        }

        // 5) 坐标系统一：非 WGS84 图层在面板中以橙色标注坐标系，转换到 WGS84 后恢复
        using (var map = new MapControl { Size = new Size(400, 300) })
        using (var manager = new LayerManagerControl { Size = new Size(240, 400) })
        {
            var handle = map.Handle;
            manager.Bind(map);
            var layer = SampleData.MakePlanarLayer("北京54数据", GeometryTypeConstant.Point, null, "POINT (116.3 39.9)");
            Check(layer.IsWgs84, "新建图层默认标记为 WGS84");
            layer.GeographicDatum = GeographicDatum.Find("GCS_Beijing_1954");
            map.AddLayer(layer);
            map.RefreshMap();
            foreach (LayerControl row in manager.LayerRows) row.RefreshView();
            Check(!layer.IsWgs84 && layer.GeographicDatum.ShortName == "北京54", "图层可标记为北京54地理坐标系");
            bool marked = false;
            foreach (Label label in AllLabels(manager.LayerRows[0]))
                if (label.Text.Contains("北京54") && label.ForeColor.ToArgb() == Color.FromArgb(200, 110, 0).ToArgb())
                    marked = true;
            Check(marked, "非 WGS84 图层在面板中以橙色标注坐标系");
            layer.GeographicDatum = GeographicDatum.Wgs84;
            map.RefreshMap();
            foreach (LayerControl row in manager.LayerRows) row.RefreshView();
            bool cleared = true;
            foreach (Label label in AllLabels(manager.LayerRows[0]))
                if (label.ForeColor.ToArgb() == Color.FromArgb(200, 110, 0).ToArgb()) cleared = false;
            Check(cleared, "转换到 WGS84 后橙色标注消失");
        }

        // 5b) “投影到 WGS84”对话框：选中国测局坐标系后必须仍走加密偏移算法
        //     回归：ComposeDatum 曾丢掉 IsGcj02 标记，使 GCJ-02 被当成“零参数 WGS84”，转换后坐标毫无变化
        var gcjLayer = SampleData.MakePlanarLayer("GCJ02数据", GeometryTypeConstant.Point, null, "POINT (116.4 39.9)");
        using (var dialog = new ProjectToWgs84Form(gcjLayer, false))
        {
            dialog.ShowInTaskbar = false;
            dialog.Opacity = 0;
            dialog.Show();
            Application.DoEvents();
            var box = FindControl<ComboBox>(dialog);
            int gcjIndex = -1;
            for (int i = 0; i < box.Items.Count; i++)
                if (box.Items[i].ToString().Contains("GCJ")) gcjIndex = i;
            Check(box.Items.Count == GeographicDatum.All().Count && gcjIndex >= 0,
                "“投影到 WGS84”只列出有明确转换方式的坐标系，且含 GCJ-02");
            box.SelectedIndex = gcjIndex;
            Application.DoEvents();
            bool paramsDisabled = true;
            foreach (NumericUpDown number in AllNumerics(dialog)) if (number.Enabled) paramsDisabled = false;
            Check(paramsDisabled, "GCJ-02 不使用七参数，参数输入框自动禁用");
            Button okButton = null;
            foreach (Button button in AllButtons(dialog)) if (button.Text == "开始转换") okButton = button;
            Check(okButton != null, "对话框提供“开始转换”按钮");
            okButton.PerformClick();
            Check(dialog.SourceDatum.IsGcj02, "选中 GCJ-02 后仍按国测局算法转换（不丢失坐标系类型）");
            Coordinate gcjWgs = DatumTransform.ToWgs84(new Coordinate(116.4, 39.9), dialog.SourceDatum);
            Check(Math.Abs(gcjWgs.X - 116.4) > 1e-6 || Math.Abs(gcjWgs.Y - 39.9) > 1e-6,
                "GCJ-02 → WGS84 的坐标确实发生变化（百米量级加密偏移）");
            dialog.Hide();
        }

        // 5c) “设置线段”窗口宽度：新增的偏移/延长/弧线三组参数必须完整显示，不能被截断
        var featureElement = new LineDashElement
        {
            Length = 6, Width = 1.2, OutlineWidth = 0.3, TickLength = 1.5,
            OffsetEnabled = true, Offset = -2.5,
            ExtendEnabled = true, ExtendLeft = 1, ExtendRight = 2,
            ArcEnabled = true, ArcAmplitude = 1.2, ArcHalfPeriods = 3
        };
        using (var segment = DashElementEditor.Create(featureElement))
        {
            segment.ShowInTaskbar = false;
            segment.Opacity = 0;
            segment.Show();
            Application.DoEvents();
            Check(segment.ClientSize.Width >= DashElementEditor.EditorWidth - 40,
                "“设置线段”窗口已按新增的三组参数加宽");
            int overflow = 0;
            foreach (Control control in AllControls(segment))
            {
                if (!control.Visible || control.Parent == null) continue;
                Rectangle bounds = segment.RectangleToClient(control.Parent.RectangleToScreen(control.Bounds));
                if (bounds.Width > 0 && (bounds.Right > segment.ClientSize.Width || bounds.Left < 0)) overflow++;
            }
            Check(overflow == 0, "“设置线段”窗口内所有参数控件都完整落在可视宽度内");
            int enabledBoxes = 0;
            foreach (NumericUpDown number in AllNumerics(segment)) if (number.Enabled) enabledBoxes++;
            Check(enabledBoxes == 9, "三组参数全部启用时9个数值框（4基础+偏移1+延长2+弧线2）都可编辑");
            int unreadable = 0;
            foreach (Button button in AllButtons(segment))
            {
                if (button.BackColor == SystemColors.Control) continue;   // 只有颜色按钮自带底色
                int luminance = (button.BackColor.R * 299 + button.BackColor.G * 587 + button.BackColor.B * 114) / 1000;
                if ((luminance < 128) != (button.ForeColor == Color.White)) unreadable++;
            }
            Check(unreadable == 0, "颜色按钮随底色明暗自动切换文字颜色（深色底不会出现黑底黑字）");
            using (var image = new Bitmap(segment.Width, segment.Height))
            {
                segment.DrawToBitmap(image, new Rectangle(0, 0, segment.Width, segment.Height));
                image.Save(Path.Combine(outputDir, "segment-editor.png"), ImageFormat.Png);
            }
            segment.Hide();
        }

        // 5d) 线符号面板：虚线列表要把每段启用过的偏移/延长/弧线参数显示出来，且不超出面板宽度
        var panelSymbol = new SimpleLineSymbol();
        panelSymbol.DashElements.Add(featureElement.Clone());
        panelSymbol.DashElements.Add(new LineDashElement { Length = 5, Color = Color.Black, Width = 0.8 });
        using (var lineEditor = LineSymbolEditor.Create(panelSymbol))
        {
            lineEditor.ShowInTaskbar = false;
            lineEditor.Opacity = 0;
            lineEditor.Show();
            Application.DoEvents();
            ListBox dashBox = null;
            foreach (ListBox box in AllListBoxes(lineEditor))
                foreach (object item in box.Items)
                    if (item.ToString().Contains("端点竖线")) dashBox = box;
            Check(dashBox != null && dashBox.Items.Count == 2, "线符号面板列出自定义虚线的每一段");
            string described = dashBox.Items[0].ToString();
            Check(described.Contains("偏移-2.5") && described.Contains("延长1.0/2.0") && described.Contains("弧线1.2"),
                "虚线列表显示该段设置过的偏移/延长/弧线参数");
            Check(!dashBox.Items[1].ToString().Contains("偏移") && !dashBox.Items[1].ToString().Contains("延长")
                && !dashBox.Items[1].ToString().Contains("弧线"),
                "没有设置这些参数的虚线段不显示多余信息");
            int textWidth;
            using (var font = new Font("Microsoft YaHei UI", 9F))
                textWidth = TextRenderer.MeasureText(described, font).Width;
            Check(textWidth <= dashBox.ClientSize.Width, "虚线描述单行显示且不超出列表面板宽度");
            int lineOverflow = 0;
            foreach (Control control in AllControls(lineEditor))
            {
                if (!control.Visible || control.Parent == null) continue;
                Rectangle bounds = lineEditor.RectangleToClient(control.Parent.RectangleToScreen(control.Bounds));
                if (bounds.Width > 0 && (bounds.Right > lineEditor.ClientSize.Width || bounds.Left < 0)) lineOverflow++;
            }
            Check(lineOverflow == 0, "线符号设置窗口内所有面板都在可视宽度内");
            using (var image = new Bitmap(lineEditor.Width, lineEditor.Height))
            {
                lineEditor.DrawToBitmap(image, new Rectangle(0, 0, lineEditor.Width, lineEditor.Height));
                image.Save(Path.Combine(outputDir, "line-symbol-editor.png"), ImageFormat.Png);
            }
            lineEditor.Hide();
        }

        // 6) 投影只从“图层固有的 WGS84 经纬度快照”出发：反复切换（含与数据不匹配的南半球带）不改变原始经纬度
        var geoLayer = new Layer(new FeatureClass("投影测试", GeometryTypeConstant.Point));
        geoLayer.FeatureClass.Fields.Add(new Field("名称", FieldTypeConstant.Text));
        geoLayer.FeatureClass.Features.Add(new Feature(new GIS.Point(new Coordinate(116.35, 39.9)), geoLayer.FeatureClass.Fields));
        geoLayer.CaptureGeographic(null);
        Check(geoLayer.HasGeographicGeometries && geoLayer.GeographicGeometries.Count == 1, "图层保存 WGS84 经纬度原始数据");
        Check(geoLayer.GetGeographicEnvelope().Contains(new Coordinate(116.35, 39.9)), "经纬度外包矩形直接由原始数据给出");

        var northZone = new ProjUTM(50, true, Ellipsoids.WGS84, 'S');
        var southZone = new ProjUTM(50, false, Ellipsoids.WGS84, 'K');   // 与北半球数据不匹配的投影
        geoLayer.ApplyProjection(southZone);
        Coordinate southCoords = ((GIS.Point)geoLayer.FeatureClass.Features[0].Geometry).Coordinate;
        Coordinate expectedSouth = southZone.TransferToProjCo(new Coordinate(116.35, 39.9));
        Check(Math.Abs(southCoords.X - expectedSouth.X) < 1e-6 && Math.Abs(southCoords.Y - expectedSouth.Y) < 1e-6,
            "显示坐标恒为“经纬度 → 该投影”的正算结果（不经过其它投影）");
        geoLayer.ApplyProjection(northZone);
        geoLayer.ApplyProjection(southZone);
        geoLayer.ApplyProjection(null);
        Coordinate restored = ((GIS.Point)geoLayer.FeatureClass.Features[0].Geometry).Coordinate;
        Check(Math.Abs(restored.X - 116.35) < 1e-9 && Math.Abs(restored.Y - 39.9) < 1e-9,
            "反复切换投影（含不匹配的南半球带）后经纬度完全恢复");
        var snapshot = geoLayer.GeographicGeometries[0] as GIS.Point;
        Check(snapshot != null && Math.Abs(snapshot.Coordinate.X - 116.35) < 1e-12
            && Math.Abs(snapshot.Coordinate.Y - 39.9) < 1e-12,
            "经纬度原始数据在投影过程中始终不被修改");
        Check(geoLayer.FeatureClass.ProjectionCS == null, "切回经纬度后图层记录的坐标系同步更新");
    }

    // 构造一段自定义虚线：可见段 + 间隙段；可选启用 弧线/偏移/延长 之一
    private static SimpleLineSymbol DashLine(bool arc, bool offset, bool extend)
    {
        var line = new SimpleLineSymbol { Color = Color.Black, Size = 0.6 };
        var visible = new LineDashElement { Length = 10, Color = Color.Black, Width = 0.8 };
        if (arc) { visible.ArcEnabled = true; visible.ArcAmplitude = 2; visible.ArcHalfPeriods = 3; }
        if (offset) { visible.OffsetEnabled = true; visible.Offset = 2; }
        if (extend) { visible.ExtendEnabled = true; visible.ExtendLeft = 8; visible.ExtendRight = 8; }
        line.DashElements.Add(visible);
        line.DashElements.Add(new LineDashElement { Length = 10, Color = Color.Transparent, Width = 0.01 });
        return line;
    }

    private static Bitmap DrawLineSample(SimpleLineSymbol symbol)
    {
        var bitmap = new Bitmap(200, 80);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.White);
            BasicGeometryDrawer.DrawSymbol(graphics, symbol, new Rectangle(0, 0, 200, 80));
        }
        return bitmap;
    }

    private static Bitmap DrawMarkerSample(SimpleMarkerSymbol symbol)
    {
        var bitmap = new Bitmap(121, 121);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.White);
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            BasicGeometryDrawer.DrawSymbol(graphics, symbol, new Rectangle(0, 0, 121, 121));
        }
        return bitmap;
    }

    // 指定位置是否“有墨”（非空白），用于点符号形状的像素级检查
    private static bool InkAt(Bitmap bitmap, int x, int y)
    {
        return x >= 0 && y >= 0 && x < bitmap.Width && y < bitmap.Height && !IsBlank(bitmap.GetPixel(x, y));
    }

    // 以 (cx,cy) 为心、沿水平向右的 dx ∈ [from,to] 区间内是否存在墨（用于探测圆环这类细线）
    private static bool InkInRow(Bitmap bitmap, int cx, int cy, int from, int to)
    {
        for (int dx = from; dx <= to; dx++)
            if (InkAt(bitmap, cx + dx, cy)) return true;
        return false;
    }

    // 统计与指定颜色接近（容差 tol）的像素个数，用于注记的文字颜色 / 描边颜色
    private static int CountColor(Bitmap bitmap, Color expected, int tol)
    {
        int count = 0;
        for (int y = 0; y < bitmap.Height; y++)
            for (int x = 0; x < bitmap.Width; x++)
            {
                Color c = bitmap.GetPixel(x, y);
                if (Math.Abs(c.R - expected.R) <= tol && Math.Abs(c.G - expected.G) <= tol && Math.Abs(c.B - expected.B) <= tol)
                    count++;
            }
        return count;
    }

    // 非背景像素（与 background 差异明显）的外接矩形，用于衡量注记的墨迹范围
    private static Rectangle InkBounds(Bitmap bitmap, Color background)
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        for (int y = 0; y < bitmap.Height; y++)
            for (int x = 0; x < bitmap.Width; x++)
            {
                Color c = bitmap.GetPixel(x, y);
                if (Math.Abs(c.R - background.R) <= 24 && Math.Abs(c.G - background.G) <= 24 && Math.Abs(c.B - background.B) <= 24)
                    continue;
                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
            }
        return maxX < 0 ? Rectangle.Empty : Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
    }

    // 指定颜色（容差 tol）像素的外接矩形：用于只量注记文字本身，排除符号与描边
    private static Rectangle ColorBounds(Bitmap bitmap, Color expected, int tol)
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        for (int y = 0; y < bitmap.Height; y++)
            for (int x = 0; x < bitmap.Width; x++)
            {
                Color c = bitmap.GetPixel(x, y);
                if (Math.Abs(c.R - expected.R) > tol || Math.Abs(c.G - expected.G) > tol || Math.Abs(c.B - expected.B) > tol)
                    continue;
                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
            }
        return maxX < 0 ? Rectangle.Empty : Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
    }

    private static int DiffPixels(Bitmap a, Bitmap b)
    {
        int diff = 0;
        for (int y = 0; y < a.Height; y++)
            for (int x = 0; x < a.Width; x++)
            {
                Color ca = a.GetPixel(x, y), cb = b.GetPixel(x, y);
                if (Math.Abs(ca.R - cb.R) > 30 || Math.Abs(ca.G - cb.G) > 30 || Math.Abs(ca.B - cb.B) > 30) diff++;
            }
        return diff;
    }

    // 沿给定行统计包含指定点的连续空白（未被绘制）像素个数
    private static int BlankRunAt(Bitmap bitmap, int y, int x)
    {
        if (y < 0 || y >= bitmap.Height || x < 0 || x >= bitmap.Width) return 0;
        if (!IsBlank(bitmap.GetPixel(x, y))) return 0;
        int left = x, right = x;
        while (left > 0 && IsBlank(bitmap.GetPixel(left - 1, y))) left--;
        while (right < bitmap.Width - 1 && IsBlank(bitmap.GetPixel(right + 1, y))) right++;
        return right - left + 1;
    }

    private static bool IsBlank(Color c) { return c.R > 240 && c.G > 240 && c.B > 240; }

    private static System.Collections.Generic.List<Button> AllButtons(Control parent)
    {
        var list = new System.Collections.Generic.List<Button>();
        foreach (Control child in parent.Controls)
        {
            if (child is Button button) list.Add(button);
            list.AddRange(AllButtons(child));
        }
        return list;
    }

    private static System.Collections.Generic.List<Label> AllLabels(Control parent)
    {
        var list = new System.Collections.Generic.List<Label>();
        foreach (Control child in parent.Controls)
        {
            if (child is Label label) list.Add(label);
            list.AddRange(AllLabels(child));
        }
        return list;
    }

    private static System.Collections.Generic.List<NumericUpDown> AllNumerics(Control parent)
    {
        var list = new System.Collections.Generic.List<NumericUpDown>();
        foreach (Control child in parent.Controls)
        {
            if (child is NumericUpDown number) list.Add(number);
            list.AddRange(AllNumerics(child));
        }
        return list;
    }

    private static System.Collections.Generic.List<Control> AllControls(Control parent)
    {
        var list = new System.Collections.Generic.List<Control>();
        foreach (Control child in parent.Controls)
        {
            list.Add(child);
            list.AddRange(AllControls(child));
        }
        return list;
    }

    private static System.Collections.Generic.List<ListBox> AllListBoxes(Control parent)
    {
        var list = new System.Collections.Generic.List<ListBox>();
        foreach (Control child in parent.Controls)
        {
            if (child is ListBox box) list.Add(box);
            list.AddRange(AllListBoxes(child));
        }
        return list;
    }

    // 图层面板里是否存在文本完全等于 text 的标签（用于检查图例标题＝绑定字段）
    private static bool LegendHasText(LayerManagerControl manager, string text)
    {
        foreach (LayerControl row in manager.LayerRows)
            foreach (Label label in AllLabels(row))
                if (label.Text == text) return true;
        return false;
    }
}
