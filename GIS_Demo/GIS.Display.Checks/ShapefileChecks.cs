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
        if (data == null) return;
        using (var form = new MainForm())
        {
            foreach (string file in Directory.GetFiles(data, "*.shp"))
            {
                var item = form.ImportShapefile(file);
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
            form.LoadSamples(); form.ToggleSamples(); form.LoadSamples();
            check(form.Map.Layers.Count == count + 6, "案例操作保留全部真实导入数据");
        }
    }
}
