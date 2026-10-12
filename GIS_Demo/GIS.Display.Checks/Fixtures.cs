using System.Collections.Generic;
using System.Drawing;
using GIS;

// 自动检查专用的测试夹具（原来放在演示程序里的 SampleData）。
// 演示程序已经不再加载样例图层（正式数据改用“导入 SHP”），但这些夹具对自检仍然有用：
//  · MakePlanarLayer：虚构平面坐标（0~900 × 0~600），用于绘制/注记/缩放等像素级检查；
//  · MakeGeoLayer / GeoLayers：经纬度数据（围绕 116.35°E / 39.95°N），用于投影、外包矩形、
//    图层面板与“缩放至图层”等检查。
// 夹具只出现在自检工程里，不参与应用程序。
internal static class Fixtures
{
    // 经纬度范围（约 17km × 22km），由下方虚构平面坐标线性映射而来
    private const double LonMin = 116.25, LonMax = 116.45;
    private const double LatMin = 39.85, LatMax = 40.05;
    private const double SrcWidth = 900, SrcHeight = 600;

    /// <summary>经纬度图层：平面坐标 WKT 会被线性映射到经纬度，字段为 名称/类型/数值。</summary>
    public static Layer MakeGeoLayer(string name, GeometryTypeConstant type, Symbol symbol,
        string category, double baseValue, params string[] wkts)
    {
        var featureClass = new FeatureClass(name, type);
        featureClass.Fields.Add(new Field("名称", FieldTypeConstant.Text));
        featureClass.Fields.Add(new Field("类型", FieldTypeConstant.Text));
        featureClass.Fields.Add(new Field("数值", FieldTypeConstant.Double));
        for (int i = 0; i < wkts.Length; i++)
        {
            var feature = new Feature(Geo(wkts[i]), featureClass.Fields);
            feature.Attributes.SetItem("名称", name + " " + (i + 1));
            feature.Attributes.SetItem("类型", category);
            feature.Attributes.SetItem("数值", baseValue + i);
            featureClass.Add(feature);
        }
        return new Layer(name, featureClass) { Symbol = symbol };
    }

    /// <summary>平面坐标图层：保留原始虚构平面坐标，不做经纬度转换。</summary>
    public static Layer MakePlanarLayer(string name, GeometryTypeConstant type, Symbol symbol, params string[] wkts)
    {
        var featureClass = new FeatureClass(name, type);
        featureClass.Fields.Add(new Field("名称", FieldTypeConstant.Text));
        for (int i = 0; i < wkts.Length; i++)
        {
            var feature = new Feature(Geometry.FromWKT(wkts[i]), featureClass.Fields);
            feature.Attributes.SetItem("名称", name + " " + (i + 1));
            featureClass.Add(feature);
        }
        return new Layer(name, featureClass) { Symbol = symbol };
    }

    /// <summary>一组覆盖点/线/面（含复合几何与唯一值渲染）的经纬度图层，供投影与图层面板检查使用。</summary>
    public static IEnumerable<Layer> GeoLayers()
    {
        yield return MakeGeoLayer("校园地块（含孔洞）", GeometryTypeConstant.Polygon,
            new SimpleFillSymbol { Color = Color.FromArgb(217, 231, 208),
                Outline = new SimpleLineSymbol { Color = Color.FromArgb(102, 133, 91), Size = 0.4 } },
            "绿地", 100,
            "POLYGON ((50 50, 450 50, 450 340, 50 340, 50 50), (170 130, 170 240, 300 240, 300 130, 170 130))",
            "POLYGON ((520 90, 850 90, 850 280, 700 390, 520 280, 520 90))");
        yield return MakeGeoLayer("建筑组团（复合面）", GeometryTypeConstant.MultiPolygon,
            new SimpleFillSymbol { Color = Color.FromArgb(235, 215, 196),
                Outline = new SimpleLineSymbol { Color = Color.FromArgb(163, 124, 97), Size = 0.35 } },
            "建筑", 200,
            "MULTIPOLYGON (((90 400, 250 400, 250 510, 90 510, 90 400)), ((300 410, 430 410, 430 550, 300 550, 300 410)))");
        yield return MakeGeoLayer("道路", GeometryTypeConstant.LineString,
            new SimpleLineSymbol { Color = Color.FromArgb(93, 121, 157), Size = 0.9 },
            "道路", 300,
            "LINESTRING (20 370, 480 370, 850 440)",
            "MULTILINESTRING ((480 20, 480 580), (560 460, 750 560, 880 500))");
        yield return MakeGeoLayer("设施（复合点）", GeometryTypeConstant.MultiPoint,
            new SimpleMarkerSymbol { Color = Color.FromArgb(179, 131, 43), Size = 3,
                Style = SimpleMarkerSymbolStyleConstant.Square },
            "设施", 400,
            "MULTIPOINT ((120 100), (350 290), (600 170), (800 160))");
        yield return MakeGeoLayer("观测点", GeometryTypeConstant.Point,
            new SimpleMarkerSymbol { Color = Color.FromArgb(153, 47, 57), Size = 3.6 },
            "观测站", 500,
            "POINT (150 450)", "POINT (370 480)", "POINT (700 300)");

        // 城市：唯一值渲染的三种典型点位符号
        // 五角星＝首都、中心实心点+外围空心圈＝省会、中心空心点+外围空心圆＝普通城市
        var cityLayer = MakeGeoLayer("城市（典型点位符号）", GeometryTypeConstant.Point,
            new SimpleMarkerSymbol { Style = SimpleMarkerSymbolStyleConstant.Star, Color = Color.FromArgb(186, 58, 45), Size = 5 },
            "首都", 600,
            "POINT (250 470)", "POINT (470 250)", "POINT (690 130)");
        cityLayer.FeatureClass.Features[1].Attributes.SetItem("类型", "省会");
        cityLayer.FeatureClass.Features[2].Attributes.SetItem("类型", "普通城市");
        var cityRenderer = new UniqueValueRenderer { Field = "类型" };
        cityRenderer.AddValue("首都", new SimpleMarkerSymbol
        {
            Style = SimpleMarkerSymbolStyleConstant.Star, Color = Color.FromArgb(186, 58, 45), Size = 5,
            OutlineColor = Color.FromArgb(120, 30, 20), OutlineWidth = 0.25
        });
        cityRenderer.AddValue("省会", new SimpleMarkerSymbol
        {
            Style = SimpleMarkerSymbolStyleConstant.SolidDotCircle, Color = Color.FromArgb(214, 122, 30), Size = 4
        });
        cityRenderer.AddValue("普通城市", new SimpleMarkerSymbol
        {
            Style = SimpleMarkerSymbolStyleConstant.HollowDotCircle, Color = Color.FromArgb(70, 104, 150), Size = 3.6
        });
        cityLayer.Renderer = cityRenderer;
        yield return cityLayer;
    }

    // 把虚构平面坐标（0~900 × 0~600）线性映射为经纬度，便于阅读 WKT 样例
    private static Geometry Geo(string planarWkt)
    {
        Geometry g = Geometry.FromWKT(planarWkt);
        ToGeo(g);
        return g;
    }

    private static void ToGeo(Geometry g)
    {
        if (g is GIS.Point p) { p.Coordinate = Map(p.Coordinate); return; }
        if (g is LineString ls) { ToGeo(ls.Points); return; }
        if (g is Polygon poly) { ToGeo(poly.ExteriorRing); foreach (Points h in poly.Holes) ToGeo(h); return; }
        if (g is MultiPoint mp) { ToGeo(mp.Points); return; }
        if (g is MultiLineString mls) { foreach (LineString part in mls.Parts) ToGeo(part.Points); return; }
        if (g is MultiPolygon mpoly)
        {
            foreach (Polygon part in mpoly.Parts) { ToGeo(part.ExteriorRing); foreach (Points h in part.Holes) ToGeo(h); }
        }
    }

    private static void ToGeo(Points pts)
    {
        for (int i = 0; i < pts.Count; i++) pts.SetItem(i, Map(pts[i]));
    }

    private static Coordinate Map(Coordinate planar)
    {
        return new Coordinate(
            LonMin + planar.X / SrcWidth * (LonMax - LonMin),
            LatMin + planar.Y / SrcHeight * (LatMax - LatMin));
    }
}
