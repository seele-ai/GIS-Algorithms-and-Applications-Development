using System.Collections.Generic;
using System.Drawing;

namespace GIS.Display.Demo
{
    // 全部为虚构的米制平面坐标，不依赖本机数据文件或模块4。
    public static class SampleData
    {
        public static Layer MakeLayer(string name, GeometryTypeConstant type, Symbol symbol, params string[] wkts)
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

        public static IEnumerable<Layer> Create()
        {
            yield return MakeLayer("校园地块（含孔洞）", GeometryTypeConstant.Polygon,
                new SimpleFillSymbol { Color = Color.FromArgb(217, 231, 208),
                    Outline = new SimpleLineSymbol { Color = Color.FromArgb(102, 133, 91), Size = 0.4 } },
                "POLYGON ((50 50, 450 50, 450 340, 50 340, 50 50), (170 130, 170 240, 300 240, 300 130, 170 130))",
                "POLYGON ((520 90, 850 90, 850 280, 700 390, 520 280, 520 90))");
            yield return MakeLayer("建筑组团（复合面）", GeometryTypeConstant.MultiPolygon,
                new SimpleFillSymbol { Color = Color.FromArgb(235, 215, 196),
                    Outline = new SimpleLineSymbol { Color = Color.FromArgb(163, 124, 97), Size = 0.35 } },
                "MULTIPOLYGON (((90 400, 250 400, 250 510, 90 510, 90 400)), ((300 410, 430 410, 430 550, 300 550, 300 410)))");
            yield return MakeLayer("道路", GeometryTypeConstant.LineString,
                new SimpleLineSymbol { Color = Color.FromArgb(93, 121, 157), Size = 0.9 },
                "LINESTRING (20 370, 480 370, 850 440)",
                "MULTILINESTRING ((480 20, 480 580), (560 460, 750 560, 880 500))");
            yield return MakeLayer("设施（复合点）", GeometryTypeConstant.MultiPoint,
                new SimpleMarkerSymbol { Color = Color.FromArgb(179, 131, 43), Size = 3,
                    Style = SimpleMarkerSymbolStyleConstant.SolidSquare },
                "MULTIPOINT ((120 100), (350 290), (600 170), (800 160))");
            yield return MakeLayer("观测点", GeometryTypeConstant.Point,
                new SimpleMarkerSymbol { Color = Color.FromArgb(153, 47, 57), Size = 3.6 },
                "POINT (150 450)", "POINT (370 480)", "POINT (700 300)");
        }
    }
}

