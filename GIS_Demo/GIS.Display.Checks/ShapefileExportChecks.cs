using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using GIS;
using GIS.IO;
using GIS.Display.Demo;

internal static class ShapefileExportChecks
{
    private static int Big(BinaryReader r)
    { return (r.ReadByte() << 24) | (r.ReadByte() << 16) | (r.ReadByte() << 8) | r.ReadByte(); }
    public static void Run(string output, Action<bool, string> check)
    {
        string[] wkts = {
            "POINT(116.4 39.9)", "MULTIPOINT((116 39),(117 40))",
            "LINESTRING(116 39,117 40)", "MULTILINESTRING((116 39,117 40),(118 39,119 40))",
            "POLYGON((110 30,120 30,120 40,110 40,110 30),(112 32,114 32,114 34,112 34,112 32))",
            "MULTIPOLYGON(((110 30,115 30,115 35,110 35,110 30)),((116 36,120 36,120 40,116 40,116 36)))"
        };
        for (int n = 0; n < wkts.Length; n++)
        {
            Geometry g = Geometry.FromWKT(wkts[n]);
            var fc = new FeatureClass("导出测试", g.GeometryType);
            fc.Fields.Add(new Field("名称", FieldTypeConstant.Text));
            fc.Fields.Add(new Field("COUNT", FieldTypeConstant.Int32));
            fc.Fields.Add(new Field("VALUE", FieldTypeConstant.Double));
            fc.Fields.Add(new Field("DAY", FieldTypeConstant.Date));
            fc.Fields.Add(new Field("OK", FieldTypeConstant.Boolean));
            fc.Add(new Feature(g, new Attributes(fc.Fields, new object[] { "北京站😀", -123, 1.23456789012345e-20, new DateTime(2026, 10, 17), true })));
            var layer = new Layer(fc); layer.CaptureGeographic(null);
            layer.ApplyProjection(new ProjUTM(50, true, Ellipsoids.WGS84));
            string before = fc.Features[0].Geometry.ToWKT(), path = Path.Combine(output, "export-" + n + ".shp");
            var oldCulture = CultureInfo.CurrentCulture;
            try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR"); ShapefileWriter.Write(path, layer, true); }
            finally { CultureInfo.CurrentCulture = oldCulture; }
            var read = ShapefileReader.Read(path);
            check(read.FeatureClass.Features.Count == 1 && read.FeatureClass.Fields.Count == 5, "导出往返要素/字段：" + g.GeometryType);
            var a = read.FeatureClass.Features[0].Attributes;
            check((string)a.GetItem(0) == "北京站😀" && Convert.ToInt32(a.GetItem(1)) == -123 &&
                Math.Abs(Convert.ToDouble(a.GetItem(2)) / 1.23456789012345e-20 - 1) < 1e-14 &&
                (DateTime)a.GetItem(3) == new DateTime(2026, 10, 17) && (bool)a.GetItem(4), "中文/表情/数值/日期/布尔在非英文区域设置下往返：" + g.GeometryType);
            Envelope box = read.GetEnvelope(), original = g.GetEnvelope();
            check(box.MinX == original.MinX && box.MaxX == original.MaxX && box.MinY == original.MinY && box.MaxY == original.MaxY && fc.Features[0].Geometry.ToWKT() == before,
                "UTM 显示下导出原始经纬度且不修改图层：" + g.GeometryType);
            using (var s = new BinaryReader(File.OpenRead(path)))
            using (var x = new BinaryReader(File.OpenRead(Path.ChangeExtension(path, ".shx"))))
            {
                x.BaseStream.Position = 24; check(Big(x) * 2 == x.BaseStream.Length && x.BaseStream.Length == 108, "SHX 文件长度：" + g.GeometryType);
                x.BaseStream.Position = 100; int offset = Big(x), length = Big(x); s.BaseStream.Position = offset * 2;
                check(Big(s) == 1 && Big(s) == length && offset * 2 + 8 + length * 2 == s.BaseStream.Length, "SHX 指向完整 SHP 记录：" + g.GeometryType);
            }
            if (n == 4)
            {
                var polygon = ((MultiPolygon)read.FeatureClass.Features[0].Geometry).Parts[0];
                check(polygon.Holes.Count == 1 && !polygon.Contains(new Coordinate(113, 33)) && Math.Abs(polygon.Area - 96) < 1e-9, "面孔洞及面积保留");
                using (var r = new BinaryReader(File.OpenRead(path)))
                {
                    r.BaseStream.Position = 160;
                    double[] areas = new double[2];
                    for (int ring = 0; ring < 2; ring++)
                    {
                        double px = r.ReadDouble(), py = r.ReadDouble();
                        for (int i = 1; i < 5; i++) { double cx = r.ReadDouble(), cy = r.ReadDouble(); areas[ring] += px * cy - cx * py; px = cx; py = cy; }
                    }
                    check(areas[0] < 0 && areas[1] > 0, "二进制验证外环顺时针/洞逆时针");
                }
            }
        }
        var empty = new FeatureClass("empty", GeometryTypeConstant.Point);
        string emptyPath = Path.Combine(output, "export-empty.shp");
        ShapefileWriter.Write(emptyPath, new Layer(empty), true);
        check(ShapefileReader.Read(emptyPath).FeatureClass.Features.Count == 0, "空图层导出回读");
        var fcNull = new FeatureClass("nulls", GeometryTypeConstant.Point);
        fcNull.Fields.Add(new Field("NULL", FieldTypeConstant.Text));
        fcNull.Add(new Feature(new Point(116, 39), new Attributes(fcNull.Fields, new object[] { null })));
        string nullPath = Path.Combine(output, "export-null.shp");
        ShapefileWriter.Write(nullPath, new Layer(fcNull), true);
        check(ShapefileReader.Read(nullPath).FeatureClass.Features[0].Attributes.GetItem(0) == null, "空属性保留");
        byte[][] saved = ShapefileWriter.Extensions.Select(e => File.ReadAllBytes(Path.ChangeExtension(nullPath, e))).ToArray();
        bool refused = false;
        try { ShapefileWriter.Write(nullPath, new Layer(fcNull)); } catch (IOException) { refused = true; }
        check(refused, "默认拒绝覆盖");
        fcNull.Features[0].Attributes.SetItem(0, new string('中', 85));
        refused = false;
        try { ShapefileWriter.Write(nullPath, new Layer(fcNull), true); } catch (InvalidDataException) { refused = true; }
        check(refused && saved[2].SequenceEqual(File.ReadAllBytes(Path.ChangeExtension(nullPath, ".dbf"))), "超长中文拒绝截断且不覆盖原文件");
        fcNull.Features[0].Attributes.SetItem(0, "ok"); fcNull.Features[0].Geometry = new Point(118, 40);
        using (var locked = new FileStream(Path.ChangeExtension(nullPath, ".dbf"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            refused = false;
            try { ShapefileWriter.Write(nullPath, new Layer(fcNull), true); } catch (IOException) { refused = true; }
            check(refused && ShapefileWriter.Extensions.Select((e, i) => saved[i].SequenceEqual(File.ReadAllBytes(Path.ChangeExtension(nullPath, e)))).All(v => v),
                "DBF 占用时恢复全部配套文件（新旧几何不同）");
        }
        var names = new FeatureClass("names", GeometryTypeConstant.Point);
        names.Fields.Add(new Field("中文字段名称甲", FieldTypeConstant.Text)); names.Fields.Add(new Field("中文字段名称乙", FieldTypeConstant.Text));
        names.Add(new Feature(new Point(116, 39), new Attributes(names.Fields, new object[] { "甲", "乙" })));
        string namesPath = Path.Combine(output, "export-names.shp");
        var renamed = ShapefileWriter.Write(namesPath, new Layer(names), true); var result = ShapefileReader.Read(namesPath).FeatureClass;
        check(renamed.Count == 2 && result.Fields[0].Name != result.Fields[1].Name && (string)result.Features[0].Attributes.GetItem(1) == "乙", "长中文字段名缩短去重并返回映射");
        names.Fields[0].Name = "😀😀😀"; ShapefileWriter.Write(namesPath, new Layer(names), true);
        check(ShapefileReader.Read(namesPath).FeatureClass.Fields[0].Name == "😀😀", "字段名缩短不拆开 Unicode 代理项对");
        refused = false;
        try { ShapefileWriter.Write(namesPath, new Layer(names) { GeographicDatum = GeographicDatum.Beijing54 }, true); } catch (InvalidOperationException) { refused = true; }
        check(refused, "拒绝非 WGS84 基准误标");
        var rows = new FeatureClass("rows", GeometryTypeConstant.Point); rows.Fields.Add(new Field("ID", FieldTypeConstant.Int32));
        for (int i = 0; i < 3; i++) rows.Add(new Feature(new Point(116 + i, 39), new Attributes(rows.Fields, new object[] { i })));
        rows.Features[0].Geometry = null;
        string rowsPath = Path.Combine(output, "export-rows.shp"); ShapefileWriter.Write(rowsPath, new Layer(rows), true);
        var readRows = ShapefileReader.Read(rowsPath).FeatureClass;
        check(readRows.Features.Count == 2 && Convert.ToInt32(readRows.Features[0].Attributes.GetItem(0)) == 1 && Convert.ToInt32(readRows.Features[1].Attributes.GetItem(0)) == 2, "空几何不导致属性错位");
        using (var x = new BinaryReader(File.OpenRead(Path.ChangeExtension(rowsPath, ".shx"))))
        using (var s = new BinaryReader(File.OpenRead(rowsPath)))
        {
            x.BaseStream.Position = 100; int expected = 50;
            for (int i = 0; i < 3; i++)
            {
                int offset = Big(x), length = Big(x); s.BaseStream.Position = offset * 2;
                check(offset == expected && Big(s) == i + 1 && Big(s) == length, "多记录索引/编号：" + i); expected += 4 + length;
            }
        }
        // 新版不再包含案例入口，直接向空地图加入独立测试图层。
        using (var main = new MainForm())
        {
            main.Map.AddLayer(new Layer(names)); main.Map.AddLayer(new Layer(fcNull));
            check(main.Controls.OfType<ToolStrip>().SelectMany(t => t.Items.OfType<ToolStripButton>()).Any(b => b.Text == "导出 SHP"), "新版主界面接入导出按钮");
            var type = typeof(MainForm).Assembly.GetType("GIS.Display.Demo.ShapefileExportForm");
            using (var dialog = (Form)Activator.CreateInstance(type, new object[] { main.Map.Layers }))
            {
                dialog.ShowInTaskbar = false; dialog.Opacity = 0; dialog.Show(); Application.DoEvents();
                var combo = dialog.Controls.OfType<ComboBox>().Single();
                check(combo.Items.Count == 2 && combo.SelectedItem == main.Map.Layers.Last(), "导出窗口图层顺序及默认选择");
                check(dialog.Controls.Cast<Control>().All(c => dialog.ClientRectangle.Contains(c.Bounds)), "导出窗口无控件越界");
                using (var image = new System.Drawing.Bitmap(dialog.Width, dialog.Height))
                { dialog.DrawToBitmap(image, new System.Drawing.Rectangle(0, 0, dialog.Width, dialog.Height)); image.Save(Path.Combine(output, "shapefile-export-dialog.png")); }
                dialog.Close();
            }
        }
        var projected = new FeatureClass("projected", GeometryTypeConstant.Point) { ProjectionCS = new ProjUTM(50, true, Ellipsoids.WGS84) };
        projected.Add(new Feature(new Point(projected.ProjectionCS.TransferToProjCo(new Coordinate(116, 39))), projected.Fields));
        ShapefileWriter.Write(emptyPath, new Layer(projected), true);
        var restored = ((Point)ShapefileReader.Read(emptyPath).FeatureClass.Features[0].Geometry).Coordinate;
        check(Math.Abs(restored.X - 116) < 1e-6 && Math.Abs(restored.Y - 39) < 1e-6, "没有快照时反算投影坐标副本");
    }
}
