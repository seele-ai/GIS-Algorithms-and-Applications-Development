using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using GIS;
using GIS.Display;
using GIS.Display.Demo;

internal sealed class CreationProbe : FeatureCreationMap
{
    public void ClickAt(int x, int y, int clicks = 1) { OnMouseDown(new MouseEventArgs(MouseButtons.Left, clicks, x, y, 0)); OnMouseUp(new MouseEventArgs(MouseButtons.Left, clicks, x, y, 0)); }
    public void DoubleAt(int x, int y) { ClickAt(x,y,2); OnMouseDoubleClick(new MouseEventArgs(MouseButtons.Left,2,x,y,0)); }
    public void Key(Keys key) { var msg = new Message(); ProcessCmdKey(ref msg, key); }
}
internal static class FeatureCreationChecks
{
    private static Layer LayerOf(GeometryTypeConstant type)
    {
        var fc = new FeatureClass(type.ToString(), type); fc.Fields.Add(new Field("名称", FieldTypeConstant.Text));
        var layer = new Layer(fc); layer.CaptureGeographic(null); return layer;
    }
    public static void Run(string output, Action<bool,string> check)
    {
        using (var map = new CreationProbe())
        {
            map.Size = new Size(800,600); map.CreateControl();
            map.RequestAttributes = fields => new Attributes(fields, new object[] { "新要素" });
            var pointLayer = LayerOf(GeometryTypeConstant.Point); map.AddLayer(pointLayer);
            map.SetExtent(new Envelope(110,120,30,40));
            var location = new System.Drawing.Point(300,250); Coordinate expected = map.Transform.ScreenToMap(location);
            map.BeginCreation(pointLayer); map.ClickAt(location.X,location.Y);
            check(pointLayer.FeatureClass.Features.Count == 1 && map.GetSelection(pointLayer).Count == 1, "单击创建点并自动选中");
            var created = pointLayer.FeatureClass.Features[0];
            check(((GIS.Point)created.Geometry).Coordinate.X == expected.X && (string)created.Attributes.GetItem(0) == "新要素", "屏幕坐标正确转换且属性写入");
            map.DoubleAt(location.X,location.Y);
            check(pointLayer.FeatureClass.Features.Count == 1, "双击第二次按下不会重复创建点");
            var lineLayer = LayerOf(GeometryTypeConstant.LineString); map.AddLayer(lineLayer); map.BeginCreation(lineLayer);
            map.AddSketchVertex(new Coordinate(116,39)); check(!map.FinishSketch() && lineLayer.FeatureClass.Features.Count == 0, "线不足两个顶点不会入库");
            map.AddSketchVertex(new Coordinate(117,40)); map.Key(Keys.Back);
            check(map.SketchVertexCount == 1, "Backspace撤销末尾顶点");
            map.AddSketchVertex(new Coordinate(118,40)); map.Key(Keys.Enter);
            check(lineLayer.FeatureClass.Features.Count == 1 && ((LineString)lineLayer.FeatureClass.Features[0].Geometry).Points.Count == 2, "Enter完成线要素");
            map.AddSketchVertex(new Coordinate(119,40)); map.Key(Keys.Escape);
            check(map.SketchVertexCount == 0 && map.IsCreating && lineLayer.FeatureClass.Features.Count == 1, "Esc仅取消当前草图，不删除已创建要素");
            map.AddSketchVertex(new Coordinate(116,39)); map.AddSketchVertex(new Coordinate(117,39));
            map.RequestAttributes = fields => null;
            check(!map.FinishSketch() && lineLayer.FeatureClass.Features.Count == 1 && map.SketchVertexCount == 2, "取消属性录入不插入半成品，草图保留");
            map.RequestAttributes = fields => new Attributes(fields, new object[] { "继续创建" });
            check(map.FinishSketch() && lineLayer.FeatureClass.Features.Count == 2, "属性取消后可再次完成");
            map.BeginCreation(lineLayer); map.AddSketchVertex(new Coordinate(116,39)); map.InteractionMode = MapInteractionMode.Pan;
            check(!map.IsCreating && map.SketchVertexCount == 0, "切换浏览工具清理草图");
            var polygon = LayerOf(GeometryTypeConstant.Polygon); map.AddLayer(polygon); map.BeginCreation(polygon);
            map.AddSketchVertex(new Coordinate(116,39)); map.AddSketchVertex(new Coordinate(117,39)); map.AddSketchVertex(new Coordinate(117,40));
            check(map.FinishSketch(), "创建三角形面");
            var ring = ((Polygon)polygon.FeatureClass.Features[0].Geometry).ExteriorRing;
            check(ring.Count == 4 && ring[0].X == ring[3].X && ring[0].Y == ring[3].Y, "面自动闭合");
            map.AddSketchVertex(new Coordinate(116,39)); map.AddSketchVertex(new Coordinate(117,39)); map.AddSketchVertex(new Coordinate(118,39));
            check(!map.FinishSketch() && polygon.FeatureClass.Features.Count == 1, "共线零面积面拒绝创建"); map.CancelSketch();
            var cross = new[] { new Coordinate(116,39), new Coordinate(118,41), new Coordinate(116,41), new Coordinate(117,39) };
            foreach (var p in cross) map.AddSketchVertex(p);
            check(!map.FinishSketch(), "自相交面拒绝创建");
            foreach (var type in new[] { GeometryTypeConstant.MultiPoint, GeometryTypeConstant.MultiLineString, GeometryTypeConstant.MultiPolygon })
            {
                var layer = LayerOf(type); map.AddLayer(layer); map.BeginCreation(layer);
                map.AddSketchVertex(new Coordinate(110,30)); map.AddSketchVertex(new Coordinate(111,30));
                if (type == GeometryTypeConstant.MultiPolygon) map.AddSketchVertex(new Coordinate(111,31));
                if (type != GeometryTypeConstant.MultiPoint)
                {
                    map.FinishPart(); map.AddSketchVertex(new Coordinate(115,35)); map.AddSketchVertex(new Coordinate(116,35));
                    if (type == GeometryTypeConstant.MultiPolygon) map.AddSketchVertex(new Coordinate(116,36));
                }
                check(map.FinishSketch(), "创建复合要素：" + type);
                var geometry = layer.FeatureClass.Features[0].Geometry;
                check(type == GeometryTypeConstant.MultiPoint ? ((MultiPoint)geometry).Points.Count == 2 : type == GeometryTypeConstant.MultiLineString ? ((MultiLineString)geometry).Parts.Count == 2 : ((MultiPolygon)geometry).Parts.Count == 2, "复合要素部件/点数正确：" + type);
                string path = Path.Combine(output, "created-" + type + ".shp"); GIS.IO.ShapefileWriter.Write(path,layer,true);
                check(GIS.IO.ShapefileReader.Read(path).FeatureClass.Features.Count == 1, "新要素可以导出回读：" + type);
            }
            map.EndCreation(); map.SelectionLayer = pointLayer; map.InteractionMode = MapInteractionMode.Select;
            var overlap = LayerOf(GeometryTypeConstant.Point); overlap.FeatureClass.Add(new Feature(new GIS.Point(expected), overlap.FeatureClass.Fields)); overlap.CaptureGeographic(null); map.AddLayer(overlap);
            map.ClearSelection(); var screen = map.Transform.MapToScreen(expected); map.SelectAt(screen, SelectMethodConstant.CreateNew);
            check(map.GetSelection(pointLayer).Count == 1 && map.GetSelection(overlap).Count == 0, "点选仅命中目标图层，即使其他图层重叠");
            map.SelectionLayer = null; map.SelectBox(new Rectangle(screen.X-10,screen.Y-10,20,20), SelectMethodConstant.CreateNew);
            check(map.GetSelection(pointLayer).Count == 1 && map.GetSelection(overlap).Count == 1, "所有图层模式框选重叠要素");
            map.BeginCreation(pointLayer); map.SetLayerVisible(pointLayer,false);
            check(!map.IsCreating, "隐藏创建目标图层自动退出创建");
            bool refused = false; try { map.BeginCreation(pointLayer); } catch (InvalidOperationException) { refused = true; }
            check(refused, "不可见图层不能创建");
            map.SetLayerVisible(pointLayer,true); map.BeginCreation(pointLayer); map.RemoveLayer(pointLayer); check(!map.IsCreating, "移除目标图层自动退出创建");
        }
        using (var map = new CreationProbe())
        {
            var layer = LayerOf(GeometryTypeConstant.Point);
            layer.FeatureClass.Add(new Feature(new GIS.Point(116.4,39.9), layer.FeatureClass.Fields)); layer.CaptureGeographic(null);
            string snapshot = layer.GeographicGeometries[0].ToWKT();
            var projection = new ProjUTM(50,true,Ellipsoids.WGS84); layer.ApplyProjection(projection);
            Geometry existingDisplay = layer.FeatureClass.Features[0].Geometry;
            map.AddLayer(layer); map.RequestAttributes = fields => new Attributes(fields,new object[] { "投影点" }); map.BeginCreation(layer);
            map.AddSketchVertex(projection.TransferToProjCo(new Coordinate(116.5,39.8)));
            check(layer.FeatureClass.Features.Count == 2 && layer.HasGeographicGeometries, "投影显示下创建新点并同步经纬度快照");
            check(layer.GeographicGeometries[0].ToWKT() == snapshot && ReferenceEquals(existingDisplay,layer.FeatureClass.Features[0].Geometry), "新增不损伤旧快照及原显示几何");
            layer.ApplyProjection(null); var p = ((GIS.Point)layer.FeatureClass.Features[1].Geometry).Coordinate;
            check(Math.Abs(p.X-116.5)<1e-6 && Math.Abs(p.Y-39.8)<1e-6, "切回经纬度保留新点正确位置");
            string path = Path.Combine(output,"created-projected.shp"); GIS.IO.ShapefileWriter.Write(path,layer,true);
            check(GIS.IO.ShapefileReader.Read(path).FeatureClass.Features.Count == 2, "创建与SHP导出集成");
        }
        using (var dialog = new CreateLayerForm())
        {
            dialog.ShowInTaskbar=false; dialog.Opacity=0; dialog.Show(); Application.DoEvents();
            var controls=Descendants(dialog).ToArray(); controls.OfType<TextBox>().Single().Text="实测新图层";
            controls.OfType<ComboBox>().Single().SelectedIndex=2;
            using(var image=new Bitmap(dialog.Width,dialog.Height)) { dialog.DrawToBitmap(image,new Rectangle(0,0,dialog.Width,dialog.Height)); image.Save(Path.Combine(output,"create-layer-dialog.png")); }
            ((Button)dialog.AcceptButton).PerformClick();
            check(dialog.CreatedLayer != null && dialog.CreatedLayer.Name=="实测新图层" && dialog.CreatedLayer.FeatureClass.GeometryType==GeometryTypeConstant.Polygon && dialog.CreatedLayer.FeatureClass.Fields[0].Name=="名称", "新建图层窗口提交名称、类型和字段");
        }
        var attributeFields=new Fields(); attributeFields.Add(new Field("名称",FieldTypeConstant.Text)); attributeFields.Add(new Field("数量",FieldTypeConstant.Int32));
        using(var dialog=new NewFeatureAttributesForm(attributeFields))
        {
            dialog.ShowInTaskbar=false; dialog.Opacity=0; dialog.Show(); Application.DoEvents();
            var grid=Descendants(dialog).OfType<DataGridView>().Single(); grid.Rows[0].Cells[2].Value="测试站点"; grid.Rows[1].Cells[2].Value="42";
            using(var image=new Bitmap(dialog.Width,dialog.Height)) { dialog.DrawToBitmap(image,new Rectangle(0,0,dialog.Width,dialog.Height)); image.Save(Path.Combine(output,"create-attributes-dialog.png")); }
            ((Button)dialog.AcceptButton).PerformClick();
            check(dialog.Values!=null && (string)dialog.Values.GetItem(0)=="测试站点" && (int)dialog.Values.GetItem(1)==42,"属性窗口实际提交保持字段类型");
        }
        using(var map=new CreationProbe())
        {
            var layer=LayerOf(GeometryTypeConstant.LineString); map.AddLayer(layer); map.SetExtent(new Envelope(110,120,30,40)); map.RequestAttributes=f=>new Attributes(f);
            map.BeginCreation(layer); map.ClickAt(200,200); map.ClickAt(400,400); map.DoubleAt(400,400);
            check(layer.FeatureClass.Features.Count==1 && ((LineString)layer.FeatureClass.Features[0].Geometry).Points.Count==2,"真实双击事件顺序完成线且不重复末尾顶点");
        }
        using (var map = new CreationProbe())
        {
            var layer = LayerOf(GeometryTypeConstant.MultiPolygon); map.AddLayer(layer);
            map.Size = new Size(600, 600); map.CreateControl(); map.SetExtent(new Envelope(110,120,30,40));
            map.RequestAttributes = f => new Attributes(f); map.BeginCreation(layer);
            map.AddSketchVertex(new Coordinate(112,32)); map.AddSketchVertex(new Coordinate(118,32));
            map.FinishPart();
            check(map.SketchVertexCount == 2 && layer.FeatureClass.Features.Count == 0, "结束不足三点的面部件时保留草图，可继续补点");
            map.AddSketchVertex(new Coordinate(118,38)); map.FinishPart();
            // 未点击首点闭合时，结束部件即应画出末点→首点的边，而非等待完成整个要素。
            using (var bitmap = new Bitmap(map.Width,map.Height))
            {
                map.DrawToBitmap(bitmap,map.ClientRectangle);
                var middle = map.Transform.MapToScreen(new Coordinate(115,35));
                bool closingEdge = false;
                for (int x=middle.X-2;x<=middle.X+2;x++) for(int y=middle.Y-2;y<=middle.Y+2;y++)
                { Color color=bitmap.GetPixel(x,y); closingEdge |= color.R > 200 && color.G < 120 && color.B < 80; }
                check(closingEdge,"结束未手工闭合的面部件，草图立即显示闭合边");
                bitmap.Save(Path.Combine(output,"multipart-auto-close.png"));
            }
            int count=map.SketchVertexCount; map.FinishPart();
            check(map.SketchVertexCount==count,"重复结束空部件不丢失已闭合部件");
            map.UndoVertex(); check(map.SketchVertexCount==2,"撤销已闭合部件会去掉闭合副本并撤掉实际末顶点");
            map.AddSketchVertex(new Coordinate(118,38)); map.FinishPart();
            map.AddSketchVertex(new Coordinate(111,36)); map.AddSketchVertex(new Coordinate(113,38)); map.AddSketchVertex(new Coordinate(111,38)); map.FinishPart();
            check(map.FinishSketch(),"两个未手工闭合的面部件均可结束并完成要素");
            var multi=(MultiPolygon)layer.FeatureClass.Features[0].Geometry;
            check(multi.Parts.Count==2 && multi.Parts.All(p=>p.ExteriorRing.Count==4 && p.ExteriorRing[0].Equals(p.ExteriorRing[3])),"复合面所有部件均只有一个闭合顶点");
        }        bool overlapRejected=false;
        try
        {
            FeatureCreationMap.BuildGeometry(GeometryTypeConstant.MultiPolygon,
                new[] { new List<Coordinate> { new Coordinate(110,30),new Coordinate(120,30),new Coordinate(120,40),new Coordinate(110,40) } },
                new[] { new Coordinate(112,32),new Coordinate(114,32),new Coordinate(114,34) });
        }
        catch(ArgumentException) { overlapRejected=true; }
        check(overlapRejected,"复合面嵌套部件不被误当作外环写入");        check((int)NewFeatureAttributesForm.ParseValue("-12",FieldTypeConstant.Int32) == -12 && (bool)NewFeatureAttributesForm.ParseValue("true",FieldTypeConstant.Boolean) && (DateTime)NewFeatureAttributesForm.ParseValue("2026-10-17",FieldTypeConstant.Date) == new DateTime(2026,10,17), "属性按字段类型解析");
        bool invalid = false; try { NewFeatureAttributesForm.ParseValue("256",FieldTypeConstant.Byte); } catch (OverflowException) { invalid=true; } check(invalid,"属性溢出拒绝提交");
        invalid=false; try { NewFeatureAttributesForm.ParseValue("NaN",FieldTypeConstant.Double); } catch (FormatException) { invalid=true; } check(invalid,"非有限属性拒绝提交");
        using (var form = new MainForm())
        {
            form.ShowInTaskbar=false; form.Opacity=0; form.Show(); Application.DoEvents();
            check(form.Map.Layers.Count == 0, "新增创建功能不影响空地图启动");
            var toolbars = form.Controls.OfType<ToolStrip>().ToArray();
            check(toolbars.SelectMany(t=>t.Items.OfType<ToolStripButton>()).Any(b=>b.Text=="新建图层") && toolbars.SelectMany(t=>t.Items.OfType<ToolStripButton>()).Any(b=>b.Text=="创建要素"), "主界面提供新建图层和创建要素入口");
            var layer = LayerOf(GeometryTypeConstant.LineString); form.Map.AddLayer(layer);
            var combo = toolbars.SelectMany(t=>t.Items.OfType<ToolStripComboBox>()).Single(c=>c.Items.Contains("所有可选图层")); combo.SelectedItem=layer;
            check(form.Map.SelectionLayer==layer,"操作图层选择联动选择范围");
            form.Map.BeginCreation(layer); form.Map.AddSketchVertex(new Coordinate(116,39));
            var projectionCombo = toolbars.SelectMany(t=>t.Items.OfType<ToolStripComboBox>()).Single(c=>c.Items.Count>0 && Convert.ToString(c.Items[0])=="经纬度（度）"); projectionCombo.SelectedIndex=2;
            check(!form.Map.IsCreating && form.Map.SketchVertexCount==0,"切换地图投影自动退出创建并取消草图");
            using (var image=new Bitmap(form.Width,form.Height)) { form.DrawToBitmap(image,new Rectangle(0,0,form.Width,form.Height)); image.Save(Path.Combine(output,"feature-creation-main.png")); }
        }
    }
    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach(Control c in root.Controls) { yield return c; foreach(var child in Descendants(c)) yield return child; }
    }}
