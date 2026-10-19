using System;
using System.IO;
using System.Linq;
using System.Text;
using GIS;
using GIS.IO;
using GIS.Display.Demo;

internal static class ShapefileChecks
{
    static void Big(BinaryWriter w, int n) { var b = BitConverter.GetBytes(n); Array.Reverse(b); w.Write(b); }
    static void Fixture(string path)
    {
        // 空几何后跟一个点，验证空几何不会导致属性错位。
        using (var w = new BinaryWriter(File.Create(path)))
        {
            Big(w, 9994); w.Write(new byte[20]); Big(w, 70); w.Write(1000); w.Write(1); w.Write(new byte[64]);
            Big(w, 1); Big(w, 2); w.Write(0);
            Big(w, 2); Big(w, 10); w.Write(1); w.Write(116.4); w.Write(39.9);
        }
        using (var w = new BinaryWriter(File.Create(Path.ChangeExtension(path, ".dbf"))))
        {
            w.Write((byte)3); w.Write(new byte[3]); w.Write(2); w.Write((ushort)65); w.Write((ushort)21); w.Write(new byte[20]);
            var field = new byte[32]; Encoding.ASCII.GetBytes("NAME").CopyTo(field, 0); field[11] = (byte)'C'; field[16] = 20;
            w.Write(field); w.Write((byte)13);
            foreach (string s in new[] { "空记录", "北京站" })
            {
                w.Write((byte)32); var bytes = Enumerable.Repeat((byte)32, 20).ToArray(); Encoding.UTF8.GetBytes(s).CopyTo(bytes, 0); w.Write(bytes);
            }
            w.Write((byte)26);
        }
        File.WriteAllText(Path.ChangeExtension(path, ".cpg"), "UTF-8");
    }
    public static void Run(string output, Action<bool, string> check, string data)
    {
        string path = Path.Combine(output, "import-test.shp"); Fixture(path);
        bool rejected = false;
        try { ShapefileReader.Read(path); } catch (FileNotFoundException) { rejected = true; }
        check(rejected, "缺少 PRJ 时要求明确指定坐标系");
        var layer = ShapefileReader.Read(path, true);
        check(layer.FeatureClass.Features.Count == 1, "空 SHP 记录被跳过");
        var f = layer.FeatureClass.Features[0];
        check((string)f.Attributes.GetItem("NAME") == "北京站", "UTF8 中文属性及空记录对应关系");
        check(Math.Abs(((Point)f.Geometry).Coordinate.X - 116.4) < 1e-9, "点坐标读取正确");
        File.WriteAllBytes(Path.Combine(output, "invalid.shp"), new byte[100]);
        rejected = false;
        try { ShapefileReader.Read(Path.Combine(output, "invalid.shp"), true); } catch (InvalidDataException) { rejected = true; }
        check(rejected, "拒绝损坏的 SHP");
        CheckCoordinateSystems(output, check);
        if (data == null) return;
        using (var form = new MainForm())
        {
            foreach (string file in Directory.GetFiles(data, "*.shp"))
            {
                var item = form.ImportShapefile(file, confirmConversion: l => l.GeographicDatum.Clone());
                var box = item.GetGeographicEnvelope();
                check(item.FeatureClass.Features.Count > 0 && item.FeatureClass.Fields.Count > 0, Path.GetFileName(file) + " 导入几何与属性");
                check(box.MinX > 110 && box.MaxX < 125 && box.MinY > 30 && box.MaxY < 45, Path.GetFileName(file) + " Lambert 转换后位于北京附近");
                Console.WriteLine("DATA: " + item.Name + " features=" + item.FeatureClass.Features.Count);
            }
            form.ShowInTaskbar = false; form.Opacity = 0; form.Show();
            System.Windows.Forms.Application.DoEvents();
            form.Map.FullExtent();
            using (var image = new System.Drawing.Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(image, new System.Drawing.Rectangle(0, 0, form.Width, form.Height));
                image.Save(Path.Combine(output, "shapefile-import.png"));
            }
            int count = form.Map.Layers.Count;
            // 演示/案例功能已删除：原先用“加载案例”验证导入数据不被清掉，现在改为验证导入的图层都还在、且都绑定了符号。
            check(count > 0 && form.Map.Layers.All(l => l.Symbol != null || l.Renderer != null),
                "导入的真实数据全部保留且每层都有符号或渲染器");
        }
    }
    private static string GeographicPrj(string datum, string spheroid = "WGS_1984", double major = 6378137, double inverse = 298.257223563)
    {
        return string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "GEOGCS[\"test\",DATUM[\"{0}\",SPHEROID[\"{1}\",{2:R},{3:R}]],PRIMEM[\"Greenwich\",0],UNIT[\"Degree\",0.0174532925199433]]",
            datum, spheroid, major, inverse);
    }
    private static void CheckCoordinateSystems(string output, Action<bool, string> check)
    {
        string path = Path.Combine(output, "import-crs.shp"), prj = Path.ChangeExtension(path, ".prj");
        Fixture(path);
        var original = new Coordinate(116.4, 39.9);
        using (var form = new MainForm())
        {
            int prompts = 0;
            File.WriteAllText(prj, GeographicPrj("D_WGS_1984"));
            var wgs = form.ImportShapefile(path, confirmConversion: l => { prompts++; return l.GeographicDatum.Clone(); });
            check(prompts == 0 && ((Point)wgs.FeatureClass.Features[0].Geometry).Coordinate.X == original.X, "WGS84 地理坐标直接显示，不触发转换");
            File.WriteAllText(prj, GeographicPrj("D_Beijing_1954", "Krassowsky_1940", 6378245, 298.3));
            int count = form.Map.Layers.Count;
            var canceled = form.ImportShapefile(path, confirmConversion: l =>
            {
                prompts++;
                check(l.GeographicDatum.GeoCSName == GeographicDatum.Beijing54.GeoCSName && ((Point)l.FeatureClass.Features[0].Geometry).Coordinate.X == original.X,
                    "北京54先保留源坐标及基准，未静默转换");
                check(form.Map.Layers.Count == count, "转换确认前未加入地图");
                return null;
            });
            check(canceled == null && form.Map.Layers.Count == count, "取消转换不显示、不新增图层");
            var converted = form.ImportShapefile(path, confirmConversion: l => l.GeographicDatum.Clone());
            Coordinate expected = DatumTransform.ToWgs84(original, GeographicDatum.Beijing54);
            Coordinate actual = ((Point)converted.FeatureClass.Features[0].Geometry).Coordinate;
            check(Math.Abs(expected.X - actual.X) < 1e-10 && Math.Abs(expected.Y - actual.Y) < 1e-10 && converted.GeographicDatum.GeoCSName == GeographicDatum.Wgs84.GeoCSName,
                "北京54确认后复用现有转换逻辑，标记为 WGS84");
            check(((Point)converted.GeographicGeometries[0]).Coordinate.X == actual.X, "转换后的经纬度快照同步");
            converted.ApplyProjection(new ProjUTM(50, true, Ellipsoids.WGS84)); converted.ApplyProjection(null);
            check(((Point)converted.FeatureClass.Features[0].Geometry).Coordinate.X == actual.X, "切换投影不重复基准转换");
            File.WriteAllText(prj, GeographicPrj("GCJ_02"));
            converted = form.ImportShapefile(path, confirmConversion: l => { check(l.GeographicDatum.IsGcj02, "识别明确声明的 GCJ-02"); return l.GeographicDatum.Clone(); });
            expected = DatumTransform.ToWgs84(original, GeographicDatum.Gcj02);
            check(Math.Abs(((Point)converted.FeatureClass.Features[0].Geometry).Coordinate.X - expected.X) < 1e-10, "GCJ-02确认后采用非线性转换");
            File.WriteAllText(prj, GeographicPrj("D_China_2000", "CGCS2000", 6378137, 298.257222101));
            bool cgcsPrompt = false;
            converted = form.ImportShapefile(path, confirmConversion: l => { cgcsPrompt = l.GeographicDatum.DisplayName.Contains("CGCS2000"); return l.GeographicDatum.Clone(); });
            check(cgcsPrompt && ((Point)converted.FeatureClass.Features[0].Geometry).Coordinate.X == original.X, "CGCS2000明确确认项目约定后显示");
            // 投影名称含 WGS84 也不能覆盖实际 DATUM；保留源椭球供手工转换。
            string customWkt = GeographicPrj("D_Xian_1980", "IAG_75", 6378140, 298.257).Replace("test", "WGS84_misleading_name");
            File.WriteAllText(prj, customWkt);
            GeographicDatum manual = null;
            converted = form.ImportShapefile(path, confirmConversion: l =>
            {
                check(l.GeographicDatum.GeoCSName.StartsWith("USER_DEFINED:") && l.GeographicDatum.Ellipsoid.SemiMajor == 6378140,
                    "未知/西安80基准进入自定义转换且保留 PRJ 椭球");
                manual = l.GeographicDatum.Clone(); manual.Dx = 120; manual.Dy = -35; manual.Dz = 52;
                return manual;
            });
            expected = DatumTransform.ToWgs84(original, manual);
            check(Math.Abs(((Point)converted.FeatureClass.Features[0].Geometry).Coordinate.X - expected.X) < 1e-10, "自定义输入参数实际参与转换");
            var sourceLayer = ShapefileReader.Read(path, preserveSourceDatum: true);
            using (var dialog = new GIS.Display.UI.ProjectToWgs84Form(sourceLayer, false, detectedSource: true))
            {
                dialog.ShowInTaskbar = false; dialog.Opacity = 0; dialog.Show(); System.Windows.Forms.Application.DoEvents();
                var all = Descendants(dialog).ToArray();
                var combo = all.OfType<System.Windows.Forms.ComboBox>().Single();
                check(combo.Enabled && combo.Text.Contains("D_Xian_1980") && all.OfType<System.Windows.Forms.NumericUpDown>().All(n => n.Enabled),
                    "未知坐标系窗口预选源定义，开放坐标系选择和七参数编辑");
                all.OfType<System.Windows.Forms.NumericUpDown>().First().Value = 120;
                ((System.Windows.Forms.Button)dialog.AcceptButton).PerformClick();
                check(dialog.SourceDatum.Dx == 120 && dialog.SourceDatum.Ellipsoid.SemiMajor == 6378140, "转换窗口保存手填参数及源椭球");
            }
            File.WriteAllText(prj, GeographicPrj("D_Beijing_1954", "Krassowsky_1940", 6378245, 298.3));
            sourceLayer = ShapefileReader.Read(path, preserveSourceDatum: true);
            using (var dialog = new GIS.Display.UI.ProjectToWgs84Form(sourceLayer, false, detectedSource: true))
            {
                dialog.ShowInTaskbar = false; dialog.Opacity = 0; dialog.Show(); System.Windows.Forms.Application.DoEvents();
                check(Descendants(dialog).OfType<System.Windows.Forms.ComboBox>().Single().Text.Contains("北京54"), "已识别北京54窗口自动预选对应坐标系");
                using (var bmp = new System.Drawing.Bitmap(dialog.Width, dialog.Height))
                { dialog.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, dialog.Width, dialog.Height)); bmp.Save(Path.Combine(output, "import-datum-confirm.png")); }
            }
            File.Delete(prj);
            count = form.Map.Layers.Count;
            canceled = form.ImportShapefile(path, confirmConversion: l => { check(l.GeographicDatum.GeoCSName.Contains("MissingPrj"), "缺少 PRJ 也进入源坐标系确认"); return null; });
            check(canceled == null && form.Map.Layers.Count == count, "未知坐标系取消导入保持地图不变");
            // WGS84 UTM 是投影坐标，但大地基准仍然是 WGS84，不应要求基准转换。
            var projection = new ProjUTM(50, true, Ellipsoids.WGS84);
            Coordinate utm = projection.TransferToProjCo(original);
            using (var w = new BinaryWriter(File.Open(path, FileMode.Open, FileAccess.Write))) { w.BaseStream.Position = 124; w.Write(utm.X); w.Write(utm.Y); }
            File.WriteAllText(prj, "PROJCS[\"UTM\"," + GeographicPrj("D_WGS_1984") + ",PROJECTION[\"Transverse_Mercator\"],PARAMETER[\"Central_Meridian\",117],PARAMETER[\"Scale_Factor\",0.9996],PARAMETER[\"False_Easting\",500000],UNIT[\"Meter\",1]]");
            prompts = 0; converted = form.ImportShapefile(path, confirmConversion: l => { prompts++; return null; });
            check(prompts == 0 && Math.Abs(((Point)converted.FeatureClass.Features[0].Geometry).Coordinate.X - original.X) < 1e-6, "WGS84 UTM正确反投影，不误触发基准转换");
        }
    }
    private static System.Collections.Generic.IEnumerable<System.Windows.Forms.Control> Descendants(System.Windows.Forms.Control root)
    {
        foreach (System.Windows.Forms.Control c in root.Controls)
        { yield return c; foreach (var child in Descendants(c)) yield return child; }
    }
}