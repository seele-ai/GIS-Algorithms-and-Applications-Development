using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using GIS;
using GIS.Display;
using GIS.Display.Demo;
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
            TestSelectionAndNavigation();
            TestDrawing();
            string output = args.Length > 0 ? args[0] : Path.Combine("obj", "validation");
            Directory.CreateDirectory(output);
            using (var form = new MainForm())
            {
                // 创建真实控件句柄和完成布局，但不显示测试窗口。
                form.ShowInTaskbar = false;
                form.Opacity = 0;
                form.Show();
                Application.DoEvents();
                form.LoadSamples();
                Check(form.Map.Layers.Count == 5, "演示程序加载5个样例图层");
                Check(form.Map.Layers.Sum(l => l.FeatureClass.Features.Count) == 9, "演示程序包含9个样例要素");
                Check(form.Map.Visible && form.Map.Width > 500 && form.Map.Height > 250, "地图控件可见且布局尺寸有效");
                var manager = FindControl<LayerManagerControl>(form);
                var tree = FindControl<TreeView>(manager);
                Check(tree.Nodes.Count == 5 && tree.Nodes[0].Tag == form.Map.Layers.Last(), "图层面板按顶层优先列出");
                var firstLayer = (Layer)tree.Nodes[0].Tag;
                tree.Nodes[0].Checked = false;
                Check(!firstLayer.Visible, "图层面板勾选事件控制地图可见性");
                tree.Nodes[0].Checked = true;
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
            var layer = SampleData.MakeLayer("点", GeometryTypeConstant.Point, null, "POINT (20 20)", "POINT (80 80)");
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
            map.AddLayer(SampleData.MakeLayer("单点", GeometryTypeConstant.Point, null, "POINT (10 10)"));
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
            var polygon = SampleData.MakeLayer("带洞", GeometryTypeConstant.Polygon,
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
            var top = SampleData.MakeLayer("上层", GeometryTypeConstant.Polygon,
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
            var multiple = SampleData.MakeLayer("多个部件", GeometryTypeConstant.MultiPolygon,
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
}
