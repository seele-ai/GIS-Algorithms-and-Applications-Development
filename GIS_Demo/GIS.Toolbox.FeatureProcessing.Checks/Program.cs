using System;
using System.Linq;
using GIS;
using GIS.Toolbox.Tools.DataManagement;
class Program
{
 static int count;
 static void Check(bool ok,string name) { if(!ok) throw new Exception(name); count++; Console.WriteLine("PASS " + name); }
 static ManagementResult Run(string wkt, ManagementOperation op,double t=0) {
  var g=GeometryWkt.FromWKT(wkt); var fc=new FeatureClass("input",g.GeometryType){Srid=4326};
  fc.Fields.Add(new Field("name",FieldTypeConstant.Text)); fc.Fields.Add(new Field("ORIG_FID",FieldTypeConstant.Int32));
  var f=new Feature(g,fc.Fields); f.Attributes.SetItem("name","source"); f.Attributes.SetItem("ORIG_FID",42); fc.Add(f);
  var result=FeatureProcessingEngine.Run(op,new[]{f},fc,"output",t);
  Check(g.ToWKT()==wkt || DataManagementEngine.ToNts(g).EqualsExact(DataManagementEngine.ToNts(GeometryWkt.FromWKT(wkt))),"input unchanged");
  Check(result.FeatureClass.Srid==4326,"coordinate metadata");
  Check(result.FeatureClass.Features.Cast<Feature>().All(x=>(string)x.Attributes.GetItem("name")=="source" && (int)x.Attributes.GetItem("ORIG_FID")==42 && (int)x.Attributes.GetItem("ORIG_FID_1")==0),"attributes and source field collision");
  return result;
 }
 [STAThread] static void Main() {
  var single=ManagementOperation.MultipartToSinglepart; var split=ManagementOperation.SplitAtVertices; var simplify=ManagementOperation.Generalize;
  Check(Run("MULTIPOINT ((0 0),(2 2),(4 4))",single).FeatureClass.Features.Count==3,"multipart points");
  Check(Run("MULTILINESTRING ((0 0,1 1),(2 2,3 3))",single).FeatureClass.Features.Count==2,"multipart lines");
  string polygon="POLYGON ((0 0,10 0,10 10,0 10,0 0),(2 2,2 4,4 4,4 2,2 2))";
  Check(Run(polygon,single).FeatureClass.Features.Count==1,"polygon hole stays with shell");
  Check(Run("MULTIPOLYGON (((0 0,2 0,2 2,0 2,0 0)),((4 0,6 0,6 2,4 2,4 0)))",single).FeatureClass.Features.Count==2,"multipart polygons");
  Check(Run(polygon,split).FeatureClass.Features.Count==8,"shell and hole closing segments");
  Check(Run("LINESTRING (0 0,1 0,1 0,2 0)",split).FeatureClass.Features.Count==2,"duplicate vertices skipped");
  var reduced=Run("LINESTRING (0 0,1 0.1,2 0)",simplify,0.2);
  Check(DataManagementEngine.ToNts(reduced.FeatureClass.Features[0].Geometry).NumPoints==2,"DP removes near vertex");
  Check(DataManagementEngine.ToNts(Run("LINESTRING (0 0,1 1,2 0)",simplify,0.2).FeatureClass.Features[0].Geometry).NumPoints==3,"DP retains distant vertex");
  Check(DataManagementEngine.ToNts(Run("LINESTRING (0 0,1 0,2 0)",simplify,0).FeatureClass.Features[0].Geometry).NumPoints==3,"zero tolerance identity");
  Check(DataManagementEngine.ToNts(Run(polygon,simplify,100).FeatureClass.Features[0].Geometry).IsValid,"large tolerance valid polygon");
  Check(Run("MULTILINESTRING ((0 0,1 0.1,2 0),(3 0,4 0.1,5 0))",simplify,0.2).FeatureClass.Features.Count==1,"multiline preserved");
  Check(Run("MULTIPOLYGON (((0 0,2 0,2 2,0 2,0 0)),((4 0,6 0,6 2,4 2,4 0)))",simplify,1).FeatureClass.Features.Count==1,"multipolygon preserved");
  foreach(double t in new[]{-1d,double.NaN,double.PositiveInfinity}) { bool failed=false; try{Run("LINESTRING (0 0,1 1)",simplify,t);}catch(ArgumentException){failed=true;} Check(failed,"invalid tolerance rejected"); }
  using(var map=new GIS.Display.MapControl()) {
   for(int i=6;i<9;i++) using(var form=new DataManagementToolForm(map,(ManagementOperation)i)) Check(form.InputCombo!=null && form.OutputBox!=null && (i!=8 || form.ToleranceBox!=null),"form constructs " + i);
  }
  Console.WriteLine("TOTAL " + count);
 }
}
