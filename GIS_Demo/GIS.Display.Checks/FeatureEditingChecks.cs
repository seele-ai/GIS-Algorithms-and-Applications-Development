using System;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using GIS;
using GIS.Display.Demo;

internal static class FeatureEditingChecks
{
    private static System.Collections.Generic.IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls) { yield return child; foreach (Control nested in Descendants(child)) yield return nested; }
    }
    public static void Run(string output, Action<bool,string> check)
    {
        foreach (var wkt in new[] { "POINT (116 39)", "MULTIPOINT ((116 39), (117 40))", "LINESTRING (116 39, 117 40)", "MULTILINESTRING ((116 39,117 40),(118 39,119 40))", "POLYGON ((110 30,114 30,114 34,110 34,110 30),(111 31,111 32,112 32,112 31,111 31))", "MULTIPOLYGON (((110 30,112 30,112 32,110 32,110 30)),((114 30,116 30,116 32,114 32,114 30)))" })
        {
            var geometry = Geometry.FromWKT(wkt); var fc = new FeatureClass("编辑测试",geometry.GeometryType);
            fc.Fields.Add(new Field("名称",FieldTypeConstant.Text));
            var feature = new Feature(geometry,new Attributes(fc.Fields,new object[] { "原始" })); fc.Add(feature);
            var other = feature.Clone(); fc.Add(other);
            var layer = new Layer(fc); layer.CaptureGeographic(null); string original = feature.Geometry.ToWKT();
            layer.ApplyProjection(new ProjUTM(50,true,Ellipsoids.WGS84));
            var changed = FeatureEditCommand.ReadGeometry(layer,feature); changed.Translate(0.01,0.02);
            var command = FeatureEditCommand.Save(layer,feature,changed,new Attributes(fc.Fields,new object[] { "修改" }));
            check(layer.GeographicGeometries[0].ToWKT() == changed.ToWKT() && layer.GeographicGeometries[1].ToWKT() == original,"编辑同步快照并保留未编辑要素："+geometry.GeometryType);
            layer.ApplyProjection(null); check(feature.Geometry.ToWKT() == changed.ToWKT(),"切换投影保留编辑："+geometry.GeometryType);
            command.Undo(); check(feature.Geometry.ToWKT() == original && (string)feature.Attributes.GetItem(0) == "原始","撤销几何和属性："+geometry.GeometryType);
            command.Redo(); check(feature.Geometry.ToWKT() == changed.ToWKT(),"重做编辑："+geometry.GeometryType);
            string path = Path.Combine(output,"edited-"+geometry.GeometryType+".shp"); GIS.IO.ShapefileWriter.Write(path,layer,true);
            var imported = GIS.IO.ShapefileReader.Read(path);
            check((string)imported.FeatureClass.Features[0].Attributes.GetItem(0) == "修改" && Math.Abs(imported.FeatureClass.Features[0].Geometry.GetEnvelope().MinX - changed.GetEnvelope().MinX) < 1e-8,"编辑后导出回读："+geometry.GeometryType);
            var deletion = FeatureEditCommand.Delete(layer,new[] { feature });
            check(fc.Features.Count == 1 && ReferenceEquals(fc.Features[0],other) && layer.GeographicGeometries[0].ToWKT() == original,"删除同步索引："+geometry.GeometryType);
            layer.ApplyProjection(new ProjUTM(50,true,Ellipsoids.WGS84)); deletion.Undo();
            check(fc.Features.Count == 2 && ReferenceEquals(fc.Features[0],feature) && layer.GeographicGeometries[0].ToWKT() == changed.ToWKT(),"切换投影后恢复删除："+geometry.GeometryType);
            deletion.Redo(); check(fc.Features.Count == 1,"重做删除："+geometry.GeometryType);
            if (geometry is Polygon)
            {
                using (var form = new FeatureGeometryForm(geometry))
                {
                    form.ShowInTaskbar = false; form.Opacity = 0; form.Show(); Application.DoEvents();
                    using (var bitmap = new System.Drawing.Bitmap(form.Width,form.Height)) { form.DrawToBitmap(bitmap,new System.Drawing.Rectangle(0,0,form.Width,form.Height)); bitmap.Save(Path.Combine(output,"feature-editing.png"),ImageFormat.Png); }
                                        var grid = Descendants(form).OfType<DataGridView>().Single();
                    var buttons = Descendants(form).OfType<Button>().ToArray();
                                        var preview = Descendants(form).OfType<GIS.Display.MapControl>().Single();
                    grid.CurrentCell = grid.Rows[2].Cells[0];
                    check((int)preview.GetType().GetProperty("SelectedVertex").GetValue(preview) == 2,"表格选择联动预览高亮");
                    form.ClientSize = new System.Drawing.Size(800,480); Application.DoEvents();
                    var save = buttons.Single(b => b.Text == "保存编辑");
                    check(save.Visible && save.Parent.ClientRectangle.Contains(save.Bounds),"最小窗口保存编辑按钮完整可见");
                                        using (var bitmap = new System.Drawing.Bitmap(form.Width,form.Height)) { form.DrawToBitmap(bitmap,new System.Drawing.Rectangle(0,0,form.Width,form.Height)); bitmap.Save(Path.Combine(output,"feature-editing-small.png"),ImageFormat.Png); }
                    var vertexScreen = preview.Transform.MapToScreen(((Polygon)geometry).ExteriorRing[1]);
                    var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                    preview.GetType().GetMethod("OnMouseDown",flags).Invoke(preview,new object[] { new MouseEventArgs(MouseButtons.Left,1,vertexScreen.X,vertexScreen.Y,0) });
                    preview.GetType().GetMethod("OnMouseUp",flags).Invoke(preview,new object[] { new MouseEventArgs(MouseButtons.Left,1,vertexScreen.X,vertexScreen.Y,0) });
                    check(grid.CurrentRow.Index == 1,"图上点击顶点反向选中表格行");
                    grid.CurrentCell = grid.Rows[0].Cells[1];
                    check(grid.CurrentCell.ColumnIndex == 1,"选择高亮不抢占纬度编辑单元格");
                    grid.CurrentCell = grid.Rows[0].Cells[0];
                    int rows = grid.Rows.Count;
                    buttons.Single(b => b.Text == "插入顶点").PerformClick();
                    check(grid.Rows.Count == rows+1,"编辑窗口插入顶点");
                    check(grid.CurrentRow.Index == 1 && (int)preview.GetType().GetProperty("SelectedVertex").GetValue(preview) == 1,"新插入顶点自动选中并高亮");
                    buttons.Single(b => b.Text == "删除顶点").PerformClick();
                    check(grid.Rows.Count == rows,"编辑窗口删除顶点");
                    grid.Rows[0].Cells[0].Value = "109.9";
                    buttons.Single(b => b.Text == "保存编辑").PerformClick();
                    check(form.Result != null && ((Polygon)form.Result).ExteriorRing[0].X == 109.9 && ((Polygon)form.Result).ExteriorRing.Last().X == 109.9,"窗口保存修改并自动闭合面环");
                    check(((Polygon)form.Result).Holes[0].SequenceEqual(((Polygon)geometry).Holes[0]),"编辑外环保留孔洞");
                    form.Close();
                }
                check(geometry.ToWKT() == original,"关闭编辑窗口不修改原几何（含孔洞）");
            }
        }
        foreach (var wkt in new[] { "POINT (181 30)", "LINESTRING (1 1,1 1)", "POLYGON ((0 0,4 4,0 4,4 0,0 0))", "POLYGON ((0 0,4 0,4 4,0 4,0 0),(3 3,5 3,5 5,3 5,3 3))", "MULTIPOLYGON (((0 0,4 0,4 4,0 4,0 0)),((3 3,5 3,5 5,3 5,3 3)))" })
        {
            bool rejected = false; try { FeatureEditor.Validate(Geometry.FromWKT(wkt)); } catch (ArgumentException) { rejected = true; }
            check(rejected,"拒绝无效编辑："+wkt);
        }
        using (var form = new MainForm())
        {
            var fc = new FeatureClass("保存测试",GeometryTypeConstant.Point); fc.Add(new Feature(new GIS.Point(116,39),fc.Fields));
            var source = new Layer(fc); source.CaptureGeographic(null);
            string path = Path.Combine(output,"save-edit-source.shp"); GIS.IO.ShapefileWriter.Write(path,source,true);
            var layer = form.ImportShapefile(path);
            var deletion = FeatureEditCommand.Delete(layer,new[] { layer.FeatureClass.Features[0] });
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            typeof(MainForm).GetMethod("RememberEdit",flags).Invoke(form,new object[] { layer,deletion });
            check(form.HasUnsavedEdits(layer),"删除后标记未保存，不依赖选中要素");
            var originalFiles = GIS.IO.ShapefileWriter.Extensions.ToDictionary(ext => ext,ext => File.ReadAllBytes(Path.ChangeExtension(path,ext)));
            form.CommitEdits();
            check(!form.HasUnsavedEdits(layer) && layer.FeatureClass.Features.Count == 0,"保存编辑保留当前页面的删除结果并清除待确认标记");
            check(originalFiles.All(pair => File.ReadAllBytes(Path.ChangeExtension(path,pair.Key)).SequenceEqual(pair.Value)),"保存编辑不修改任何源文件");
            typeof(MainForm).GetMethod("ReplayEdit",flags).Invoke(form,new object[] { false });
            check(layer.FeatureClass.Features.Count == 0,"保存后不能撤销之前的删除");
            var fresh = new Layer(new FeatureClass("新图层",GeometryTypeConstant.Point)); fresh.CaptureGeographic(null); form.Map.AddLayer(fresh);
            var item = GIS.Display.FeatureCreationMap.AddFeature(fresh,new GIS.Point(116,39),new Attributes(fresh.FeatureClass.Fields));
            var edit = FeatureEditCommand.Save(fresh,item,new GIS.Point(117,39),item.Attributes);
            typeof(MainForm).GetMethod("RememberEdit",flags).Invoke(form,new object[] { fresh,edit });
            typeof(MainForm).GetMethod("ReplayEdit",flags).Invoke(form,new object[] { false });
            check(((GIS.Point)item.Geometry).X == 116,"保存后新的编辑仍可撤销");
            form.CommitEdits();
            typeof(MainForm).GetMethod("ReplayEdit",flags).Invoke(form,new object[] { true });
            check(((GIS.Point)item.Geometry).X == 116 && !form.HasUnsavedEdits(fresh),"保存同时清空重做记录，新图层无需文件路径");
            var nextEdit = FeatureEditCommand.Save(fresh,item,new GIS.Point(118,39),item.Attributes);
            typeof(MainForm).GetMethod("RememberEdit",flags).Invoke(form,new object[] { fresh,nextEdit });
            form.Map.BeginCreation(fresh);
            bool refusedCommit = false; try { form.CommitEdits(); } catch (InvalidOperationException) { refusedCommit = true; }
            check(refusedCommit && form.HasUnsavedEdits(fresh),"未退出草图时阻止保存且保留历史");
            form.Map.EndCreation();
            typeof(MainForm).GetMethod("ReplayEdit",flags).Invoke(form,new object[] { false });
            check(((GIS.Point)item.Geometry).X == 116,"保存失败后仍可撤销本次编辑");
        }
        var simple = new FeatureClass("冲突",GeometryTypeConstant.Point); var f = new Feature(new GIS.Point(116,39),simple.Fields); simple.Add(f);
        var l = new Layer(simple); l.CaptureGeographic(null); var cmd = FeatureEditCommand.Save(l,f,new GIS.Point(117,39),f.Attributes);
        simple.Add(new Feature(new GIS.Point(118,39),simple.Fields));
        bool conflict = false; try { cmd.Undo(); } catch (InvalidOperationException) { conflict = true; }
        check(conflict && simple.Features.Count == 2,"其他操作改变图层后拒绝覆盖式撤销");
        var invalid = new GIS.Point(200,39); string before = f.Geometry.ToWKT(); bool refused = false;
        try { FeatureEditCommand.Save(l,f,invalid,f.Attributes); } catch (ArgumentException) { refused = true; }
        check(refused && f.Geometry.ToWKT() == before,"无效保存不会部分修改原要素");
    }
}
