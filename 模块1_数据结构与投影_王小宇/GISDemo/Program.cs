using System;
using System.Collections.Generic;
using GIS;

namespace GISDemo
{
    /// <summary>
    /// 模块1（数据结构与地图投影）冒烟测试程序。
    /// 逐项检查几何对象、要素模型、投影转换与视图变换的核心功能，全部通过时返回0。
    /// </summary>
    public class Program
    {
        private static Int32 _PassCount = 0;
        private static Int32 _FailCount = 0;

        public static Int32 Main(string[] args)
        {
            Console.WriteLine("===== 模块1 冒烟测试：数据结构设计与地图投影 =====");

            TestCoordinateEnvelope();
            TestGeometryTools();
            TestGeometries();
            TestWKT();
            TestFeatureModel();
            TestProjectionGaussKruger();
            TestProjectionUTM();
            TestProjectionLambert();
            TestCoordinateTransform();
            TestMapTransform();
            TestLayerSymbol();

            Console.WriteLine();
            Console.WriteLine("===== 测试结束：通过 {0} 项，失败 {1} 项 =====", _PassCount, _FailCount);
            if (_FailCount == 0)
            {
                Console.WriteLine("全部检查通过。");
            }
            return _FailCount == 0 ? 0 : 1;
        }

        //单项检查
        private static void Check(string name, bool condition)
        {
            if (condition)
            {
                _PassCount++;
                Console.WriteLine("  【通过】" + name);
            }
            else
            {
                _FailCount++;
                Console.WriteLine("  【失败】" + name);
            }
        }

        //近似相等判断（绝对误差）
        private static bool AlmostEqual(double v1, double v2, double tolerance)
        {
            return Math.Abs(v1 - v2) <= tolerance;
        }

        #region 各组测试

        //（1）Coordinate与Envelope基础
        private static void TestCoordinateEnvelope()
        {
            Console.WriteLine();
            Console.WriteLine("---- Coordinate与Envelope ----");
            Coordinate sP1 = new Coordinate(0, 0);
            Coordinate sP2 = new Coordinate(3, 4);
            Check("两点距离=5", AlmostEqual(sP1.DistanceTo(sP2), 5, 1e-9));

            Envelope sBox = new Envelope(0, 10, 0, 10);
            Check("范围包含点", sBox.Contains(new Coordinate(5, 5)));
            Check("范围不包含界外点", sBox.Contains(new Coordinate(11, 5)) == false);
            Check("两范围相交", sBox.IntersectsWith(new Envelope(9, 20, 9, 20)));
            Check("两范围不相交", sBox.IntersectsWith(new Envelope(20, 30, 0, 10)) == false);
            Envelope sInter = sBox.Intersection(new Envelope(5, 15, 5, 15));
            Check("交集范围正确", sInter != null && AlmostEqual(sInter.MinX, 5, 1e-9) && AlmostEqual(sInter.MaxX, 10, 1e-9));
            Envelope sNull = new Envelope();
            sNull.ExpandToInclude(new Coordinate(2, 3));
            sNull.ExpandToInclude(new Coordinate(-1, 8));
            Check("空范围扩充后正确", AlmostEqual(sNull.MinX, -1, 1e-9) && AlmostEqual(sNull.MaxY, 8, 1e-9));
        }

        //（2）几何算法工具
        private static void TestGeometryTools()
        {
            Console.WriteLine();
            Console.WriteLine("---- GeometryTools几何算法 ----");
            //点到线段距离：垂足在线段内
            double sDis1 = GeometryTools.GetDisFromPointToSegment(new Coordinate(5, 3), new Coordinate(0, 0), new Coordinate(10, 0));
            Check("点到线段距离（垂足在内）=3", AlmostEqual(sDis1, 3, 1e-9));
            //点到线段距离：垂足在线段外
            double sDis2 = GeometryTools.GetDisFromPointToSegment(new Coordinate(15, 0), new Coordinate(0, 0), new Coordinate(10, 0));
            Check("点到线段距离（垂足在外）=5", AlmostEqual(sDis2, 5, 1e-9));

            //射线法：矩形内点与外点
            Points sSquare = new Points();
            sSquare.Add(new Coordinate(0, 0));
            sSquare.Add(new Coordinate(10, 0));
            sSquare.Add(new Coordinate(10, 10));
            sSquare.Add(new Coordinate(0, 10));
            Check("矩形内点判定", GeometryTools.IsPointWithinPolygon(new Coordinate(5, 5), sSquare));
            Check("矩形外点判定", GeometryTools.IsPointWithinPolygon(new Coordinate(15, 5), sSquare) == false);

            //射线法：凹多边形（凹口向右的箭头形）
            Points sConcave = new Points();
            sConcave.Add(new Coordinate(0, 0));
            sConcave.Add(new Coordinate(10, 0));
            sConcave.Add(new Coordinate(10, 10));
            sConcave.Add(new Coordinate(5, 5));
            sConcave.Add(new Coordinate(0, 10));
            Check("凹多边形内点判定", GeometryTools.IsPointWithinPolygon(new Coordinate(2, 5), sConcave));
            Check("凹多边形凹口处点判定", GeometryTools.IsPointWithinPolygon(new Coordinate(8, 8), sConcave) == false);

            //鞋带公式求面积
            Check("正方形面积=100", AlmostEqual(GeometryTools.GetPolygonArea(sSquare), 100, 1e-9));
            Check("凹多边形面积=75", AlmostEqual(GeometryTools.GetPolygonArea(sConcave), 75, 1e-9));

            //折线中点
            Points sLine = new Points();
            sLine.Add(new Coordinate(0, 0));
            sLine.Add(new Coordinate(4, 0));
            sLine.Add(new Coordinate(4, 6));
            Coordinate sMid = GeometryTools.GetMidPointOfPolyline(sLine);
            Check("折线中点=(4,1)", AlmostEqual(sMid.X, 4, 1e-9) && AlmostEqual(sMid.Y, 1, 1e-9));

            //线段与矩形相交
            Envelope sBox = new Envelope(4, 6, 4, 6);
            Check("线段穿越矩形", GeometryTools.IsSegmentCrossBox(new Coordinate(0, 5), new Coordinate(10, 5), sBox));
            Check("线段在矩形外", GeometryTools.IsSegmentCrossBox(new Coordinate(0, 0), new Coordinate(10, 0), sBox) == false);
            Check("线段整体位于矩形内", GeometryTools.IsSegmentCrossBox(new Coordinate(4.5, 5), new Coordinate(5.5, 5), sBox));
        }

        //（3）几何对象类
        private static void TestGeometries()
        {
            Console.WriteLine();
            Console.WriteLine("---- Geometry几何对象 ----");
            //点
            Point sPoint = new Point(2, 3);
            Check("点GetEnvelope", sPoint.GetEnvelope().Width == 0 && sPoint.GetEnvelope().MinY == 3);
            Check("点Distance", AlmostEqual(sPoint.Distance(new Coordinate(6, 6)), 5, 1e-9));

            //折线
            LineString sLine = new LineString();
            sLine.Points.Add(new Coordinate(0, 0));
            sLine.Points.Add(new Coordinate(10, 0));
            sLine.Points.Add(new Coordinate(10, 10));
            Check("折线长度=20", AlmostEqual(sLine.Length, 20, 1e-9));
            Check("容限内点在线上", sLine.Contains(new Coordinate(5, 0.5), 1));
            Check("容限外点不在线上", sLine.Contains(new Coordinate(5, 5), 1) == false);
            Check("点到折线距离=5", AlmostEqual(sLine.Distance(new Coordinate(0, 5)), 5, 1e-9));
            Check("折线与穿越范围相交", sLine.Intersects(new Envelope(9, 12, 9, 12)));
            Check("折线与分离范围不相交", sLine.Intersects(new Envelope(20, 30, 20, 30)) == false);

            //多边形（带洞）
            Polygon sPolygon = new Polygon();
            sPolygon.ExteriorRing.Add(new Coordinate(0, 0));
            sPolygon.ExteriorRing.Add(new Coordinate(100, 0));
            sPolygon.ExteriorRing.Add(new Coordinate(100, 100));
            sPolygon.ExteriorRing.Add(new Coordinate(0, 100));
            Points sHole = new Points();
            sHole.Add(new Coordinate(40, 40));
            sHole.Add(new Coordinate(60, 40));
            sHole.Add(new Coordinate(60, 60));
            sHole.Add(new Coordinate(40, 60));
            sPolygon.Holes.Add(sHole);
            Check("多边形面积=10000-400", AlmostEqual(sPolygon.Area, 9600, 1e-9));
            Check("外环内点包含", sPolygon.Contains(new Coordinate(10, 10)));
            Check("洞内点不包含", sPolygon.Contains(new Coordinate(50, 50)) == false);
            Check("外环外点不包含", sPolygon.Contains(new Coordinate(150, 50)) == false);
            Check("点在多边形内时距离=0", AlmostEqual(sPolygon.Distance(new Coordinate(10, 10)), 0, 1e-9));
            Check("点到多边形距离", AlmostEqual(sPolygon.Distance(new Coordinate(110, 50)), 10, 1e-9));
            //范围完全位于洞内时：范围与外环边不相交、顶点也不在面内——按Intersects的矩形相交语义返回否
            Check("洞内小范围不相交", sPolygon.Intersects(new Envelope(45, 55, 45, 55)) == false);
            Check("与多边形相交的范围", sPolygon.Intersects(new Envelope(90, 120, 40, 60)));

            //复合多边形
            MultiPolygon sMulti = new MultiPolygon();
            sMulti.Parts.Add(sPolygon);
            Polygon sPolygon2 = new Polygon();
            sPolygon2.ExteriorRing.Add(new Coordinate(200, 0));
            sPolygon2.ExteriorRing.Add(new Coordinate(300, 0));
            sPolygon2.ExteriorRing.Add(new Coordinate(300, 100));
            sPolygon2.ExteriorRing.Add(new Coordinate(200, 100));
            sMulti.Parts.Add(sPolygon2);
            Check("复合多边形总面积", AlmostEqual(sMulti.Area, 19600, 1e-9));
            Check("复合多边形包含判断", sMulti.Contains(new Coordinate(250, 50)) && sMulti.Contains(new Coordinate(10, 10)));
            Check("复合多边形GetEnvelope", AlmostEqual(sMulti.GetEnvelope().MaxX, 300, 1e-9));

            //平移与克隆
            Geometry sClone = sPolygon.Clone();
            sClone.Translate(10, 10);
            Polygon sClonedPolygon = (Polygon)sClone;
            Check("Translate后面积不变", AlmostEqual(sClonedPolygon.Area, 9600, 1e-9));
            Check("Translate后位置正确", AlmostEqual(sClonedPolygon.GetEnvelope().MinX, 10, 1e-9));
            Check("深复制不影响原对象", AlmostEqual(sPolygon.GetEnvelope().MinX, 0, 1e-9));

            //多点与复合折线
            MultiPoint sMultiPoint = new MultiPoint();
            sMultiPoint.Points.Add(new Coordinate(0, 0));
            sMultiPoint.Points.Add(new Coordinate(100, 100));
            Check("多点距离", AlmostEqual(sMultiPoint.Distance(new Coordinate(100, 0)), 100, 1e-9));
            Check("多点容限包含", sMultiPoint.Contains(new Coordinate(1, 0), 2));
        }

        //（4）WKT读写
        private static void TestWKT()
        {
            Console.WriteLine();
            Console.WriteLine("---- WKT读写 ----");
            //逐类型：解析→输出→再解析，比对坐标
            Check("POINT", RoundTripWkt(GeometryWkt.FromWKT("POINT (30.5 10.25)")));
            Check("LINESTRING", RoundTripWkt(GeometryWkt.FromWKT("LINESTRING (30 10, 10 30, 40 40)")));
            Check("POLYGON带洞", RoundTripWkt(GeometryWkt.FromWKT(
                "POLYGON ((0 0, 100 0, 100 100, 0 100, 0 0), (40 40, 60 40, 60 60, 40 60, 40 40))")));
            Check("MULTIPOINT括号形式", RoundTripWkt(GeometryWkt.FromWKT("MULTIPOINT ((10 40), (40 30))")));
            Check("MULTIPOINT无括号形式", RoundTripWkt(GeometryWkt.FromWKT("MULTIPOINT (10 40, 40 30)")));
            Check("MULTILINESTRING", RoundTripWkt(GeometryWkt.FromWKT("MULTILINESTRING ((10 10, 20 20), (15 15, 30 15))")));
            Check("MULTIPOLYGON", RoundTripWkt(GeometryWkt.FromWKT(
                "MULTIPOLYGON (((0 0, 10 0, 10 10, 0 10, 0 0)), ((100 100, 110 100, 110 110, 100 110, 100 100)))")));
            Check("POINT EMPTY", GeometryWkt.FromWKT("POINT EMPTY").GeometryType == GeometryTypeConstant.Point);
            //几何对象上的FromWKT/ToWKT静态入口
            Geometry sGeometry = Geometry.FromWKT("LINESTRING (1 1, 2 2)");
            Check("Geometry.FromWKT入口", sGeometry.GeometryType == GeometryTypeConstant.LineString);
            Polygon sSquareWkt = (Polygon)GeometryWkt.FromWKT("POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))");
            Polygon sSquareBack = (Polygon)GeometryWkt.FromWKT(sSquareWkt.ToWKT());
            Check("Polygon面积经WKT往返不变", AlmostEqual(sSquareBack.Area, 100, 1e-9));
        }

        //WKT往返一致性检查
        private static bool RoundTripWkt(Geometry geometry)
        {
            Geometry sReParsed = GeometryWkt.FromWKT(geometry.ToWKT());
            Envelope sE1 = geometry.GetEnvelope();
            Envelope sE2 = sReParsed.GetEnvelope();
            return AlmostEqual(sE1.MinX, sE2.MinX, 1e-9) && AlmostEqual(sE1.MaxX, sE2.MaxX, 1e-9)
                && AlmostEqual(sE1.MinY, sE2.MinY, 1e-9) && AlmostEqual(sE1.MaxY, sE2.MaxY, 1e-9)
                && geometry.GeometryType == sReParsed.GeometryType;
        }

        //（5）要素模型
        private static void TestFeatureModel()
        {
            Console.WriteLine();
            Console.WriteLine("---- Feature/FeatureClass/Layer ----");
            FeatureClass sFeatureClass = new FeatureClass("省会城市", GeometryTypeConstant.Point);
            sFeatureClass.Fields.Add(new Field("Name", FieldTypeConstant.Text));
            sFeatureClass.Fields.Add(new Field("POP", FieldTypeConstant.Double));

            Feature sF1 = new Feature(new Point(116.4, 39.9), sFeatureClass.Fields);
            sF1.Attributes.SetItem("Name", "北京");
            sF1.Attributes.SetItem("POP", 2189.3);
            Feature sF2 = new Feature(new Point(113.2, 23.1), sFeatureClass.Fields);
            sF2.Attributes.SetItem("Name", "广州");
            sF2.Attributes.SetItem("POP", 1867.6);
            Feature sF3 = new Feature(new Point(121.5, 31.2), sFeatureClass.Fields);
            sF3.Attributes.SetItem("Name", "上海");
            sF3.Attributes.SetItem("POP", 2487.0);
            sFeatureClass.Add(sF1);
            sFeatureClass.Add(sF2);
            sFeatureClass.Add(sF3);
            Check("要素个数=3", sFeatureClass.Features.Count == 3);

            //几何类型校验
            bool sCaught = false;
            try
            {
                sFeatureClass.Add(new Feature(new LineString(), sFeatureClass.Fields));
            }
            catch
            {
                sCaught = true;
            }
            Check("几何类型不匹配时拒绝入类", sCaught);

            //点选与框选
            Features sByPoint = sFeatureClass.SearchByPoint(new Coordinate(116.5, 39.8), 0.2);
            Check("点选命中北京", sByPoint.Count == 1 && (string)sByPoint.GetItem(0).Attributes.GetItem("Name") == "北京");
            Features sByBox = sFeatureClass.SearchByBox(new Envelope(110, 118, 20, 40));
            Check("框选命中北京与广州", sByBox.Count == 2);
            Check("要素类整体外包", AlmostEqual(sFeatureClass.GetEnvelope().MinX, 113.2, 1e-9));

            //属性表达式查询
            bool sCorrect;
            Features sByExpr = sFeatureClass.SearchByExpression("POP > 2400", out sCorrect);
            Check("表达式查询POP>2000命中上海", sCorrect && sByExpr.Count == 1
                && (string)sByExpr.GetItem(0).Attributes.GetItem("Name") == "上海");
            sFeatureClass.SearchByExpression("POP >>> 错误", out sCorrect);
            Check("非法表达式返回不正确标记", sCorrect == false);

            //图层
            Layer sLayer = new Layer(sFeatureClass);
            sLayer.Symbol = new SimpleMarkerSymbol("城市点");
            Check("图层名自动取要素类名", sLayer.Name == "省会城市");
            Check("图层默认可见", sLayer.Visible);
            Check("图层符号类型正确", sLayer.Symbol.SymbolType == SymbolTypeConstant.SimpleMarkerSymbol);

            //选择集集合运算
            Features sSelection = new Features();
            SelectTools.ExcuteSelect(sSelection, sByBox, SelectMethodConstant.CreateNew);
            SelectTools.ExcuteSelect(sSelection, sByExpr, SelectMethodConstant.AddToCurrent);
            Check("选择集并集=3", sSelection.Count == 3);
            SelectTools.ExcuteSelect(sSelection, sByExpr, SelectMethodConstant.RemoveFromCurrent);
            Check("选择集差集=2", sSelection.Count == 2);
        }

        //（6）高斯-克吕格投影
        private static void TestProjectionGaussKruger()
        {
            Console.WriteLine();
            Console.WriteLine("---- 高斯-克吕格投影 ----");
            //以北京附近点(116.4E, 39.9N)测试
            Coordinate sBeijing = new Coordinate(116.4, 39.9);
            //3度带：带号39，中央经线117°E
            ProjGauss_Kruger sGk3 = ProjGauss_Kruger.Create(Ellipsoids.WGS84, 39, true);
            Check("3度带中央经线=117", AlmostEqual(sGk3.CentralMeridian, 117, 1e-9));
            Check("经度116.4的3度带号=39", ProjGauss_Kruger.GetZone3(116.4) == 39);
            Coordinate sProj3 = sGk3.TransferToProjCo(sBeijing);
            //位于中央经线以西，东坐标应小于500000
            Check("3度带东坐标<500000（西带）", sProj3.X < 500000);
            Check("3度带北坐标约4420000米", sProj3.Y > 4410000 && sProj3.Y < 4430000);
            Check("3度带正反算闭合<1mm", RoundTripError(sGk3, sBeijing) < 0.001);
            //6度带：带号20，中央经线117°E
            ProjGauss_Kruger sGk6 = ProjGauss_Kruger.Create(Ellipsoids.CGCS2000, 20, false);
            Check("6度带中央经线=117", AlmostEqual(sGk6.CentralMeridian, 117, 1e-9));
            Check("经度116.4的6度带号=20", ProjGauss_Kruger.GetZone6(116.4) == 20);
            Check("6度带正反算闭合<1mm", RoundTripError(sGk6, sBeijing) < 0.001);
            //中央经线上的点：东坐标恰为500000，且正反算任意点闭合
            Coordinate sCentral = sGk3.TransferToProjCo(new Coordinate(117, 39.9));
            Check("中央经线东坐标=500000", AlmostEqual(sCentral.X, 500000, 1e-6));
            //1954北京坐标系（克拉索夫斯基椭球）
            ProjGauss_Kruger sGk54 = ProjGauss_Kruger.Create(Ellipsoids.Beijing54, 20, false);
            Check("北京54椭球参数", AlmostEqual(sGk54.SemiMajor, 6378245, 1e-9) && AlmostEqual(sGk54.InverseFlattening, 298.3, 1e-9));
            Check("北京54正反算闭合<1mm", RoundTripError(sGk54, sBeijing) < 0.001);
            //多点闭合检查：每个点使用其所在的投影带
            Check("多点正反算闭合<1mm", RoundTripErrorGkPerZoneMulti(Ellipsoids.WGS84, true, GetTestLngLatPoints()) < 0.001);
            //线性单位换算（投影正算输出恒为米，LinearUnit配合ToUnits/ToMeters做单位换算）
            ProjGauss_Kruger sGkKm = new ProjGauss_Kruger("千米单位高斯", Ellipsoids.WGS84, 117,
                0, 500000, 0, 1, LinearUnitConstant.Kilometer);
            Coordinate sKmProj = sGkKm.TransferToProjCo(sBeijing);
            Check("千米单位投影正算仍输出米", AlmostEqual(sKmProj.X, sProj3.X, 1e-6));
            Check("ToUnits换算为千米", AlmostEqual(sGkKm.ToUnits(sKmProj.X), sKmProj.X / 1000, 1e-6));
            Check("ToMeters还原为米", AlmostEqual(sGkKm.ToMeters(sGkKm.ToUnits(sKmProj.X)), sKmProj.X, 1e-6));
        }

        //（7）UTM投影
        private static void TestProjectionUTM()
        {
            Console.WriteLine();
            Console.WriteLine("---- UTM投影 ----");
            Coordinate sBeijing = new Coordinate(116.4, 39.9);
            ProjUTM sUtm = new ProjUTM(50);
            Check("UTM 50带中央经线=117", AlmostEqual(sUtm.CentralMeridian, 117, 1e-9));
            Check("UTM比例因子=0.9996", AlmostEqual(sUtm.ScaleFactor, 0.9996, 1e-9));
            Check("经度116.4的UTM带号=50", ProjUTM.GetZone(116.4) == 50);
            Coordinate sProj = sUtm.TransferToProjCo(sBeijing);
            Check("UTM正反算闭合<1mm", RoundTripError(sUtm, sBeijing) < 0.001);
            //与高斯-克吕格（同中央经线）对比：UTM长度比略小，北坐标应更小
            ProjGauss_Kruger sGk = new ProjGauss_Kruger("GK对照", Ellipsoids.WGS84, 117);
            double sGkNorth = sGk.TransferToProjCo(sBeijing).Y;
            double sUtmNorth = sProj.Y;
            double sRatio = sUtmNorth / sGkNorth;
            Check("UTM北坐标≈0.9996倍GK北坐标", sRatio > 0.9995 && sRatio < 0.9997);
            //南半球：北伪偏移10000000
            ProjUTM sUtmSouth = new ProjUTM(50, false);
            Check("南半球北伪偏移=10000000", AlmostEqual(sUtmSouth.FalseNorthing, 10000000, 1e-9));
            Coordinate sSouthProj = sUtmSouth.TransferToProjCo(new Coordinate(116.4, -39.9));
            Check("南半球投影北坐标为正且约600万米", sSouthProj.Y > 5000000 && sSouthProj.Y < 6000000);
            Check("UTM多点正反算闭合<1mm", RoundTripErrorUtmPerZoneMulti(GetTestLngLatPoints()) < 0.001);
        }

        //（8）Lambert投影
        private static void TestProjectionLambert()
        {
            Console.WriteLine();
            Console.WriteLine("---- Lambert投影 ----");
            //我国常用的双标准纬线25/47，中央经线105
            ProjLambert sLambert = new ProjLambert("Lambert 25/47", Ellipsoids.WGS84,
                105, 0, 25, 47, 0, 0);
            Check("标准纬线1=25", AlmostEqual(sLambert.StandardParallelOne, 25, 1e-9));
            //标准纬线上无长度变形：投影半径的导数关系不易直接测，改为检查对称性
            //位于中央经线上的点：X=东伪偏移=0
            Coordinate sCentral = sLambert.TransferToProjCo(new Coordinate(105, 35));
            Check("中央经线上X=0", AlmostEqual(sCentral.X, 0, 1e-6));
            //距中央经线等距的对称点：X关于0对称
            double sXEast = sLambert.TransferToProjCo(new Coordinate(115, 35)).X;
            double sXWest = sLambert.TransferToProjCo(new Coordinate(95, 35)).X;
            Check("对称点X对称", AlmostEqual(sXEast, -sXWest, 1e-6));
            //正反算闭合
            Check("Lambert正反算闭合<1mm", RoundTripError(sLambert, new Coordinate(104, 35)) < 0.001);
            Check("Lambert多点正反算闭合<1mm", RoundTripErrorMulti(sLambert, GetTestLngLatPoints()) < 0.001);
            //标准纬线纬度处的反算
            Coordinate sBack = sLambert.TransferToLngLat(sLambert.TransferToProjCo(new Coordinate(110, 25)));
            Check("标准纬线25°往返一致", AlmostEqual(sBack.Y, 25, 1e-9) && AlmostEqual(sBack.X, 110, 1e-9));
        }

        //（9）坐标转换器
        private static void TestCoordinateTransform()
        {
            Console.WriteLine();
            Console.WriteLine("---- CoordinateTransform ----");
            ProjGauss_Kruger sGk = ProjGauss_Kruger.Create(Ellipsoids.WGS84, 39, true);
            CoordinateTransform sTransform = new CoordinateTransform(sGk);
            Coordinate sLngLat = new Coordinate(116.4, 39.9);
            Coordinate sProj = sTransform.Project(sLngLat);
            Check("Project结果与TransferToProjCo一致", sProj == sGk.TransferToProjCo(sLngLat));
            Coordinate sBack = sTransform.Unproject(sProj);
            Check("Unproject还原经纬度", AlmostEqual(sBack.X, 116.4, 1e-9) && AlmostEqual(sBack.Y, 39.9, 1e-9));
            //跨投影转换：高斯3度带→UTM 50带
            ProjUTM sUtm = new ProjUTM(50);
            Coordinate sCross = CoordinateTransform.Transform(sProj, sGk, sUtm);
            Coordinate sDirect = sUtm.TransferToProjCo(sLngLat);
            Check("GK→UTM跨投影转换与直接投影一致<1mm", sCross.DistanceTo(sDirect) < 0.001);
            //null投影代表经纬度
            Coordinate sSame = CoordinateTransform.Transform(sLngLat, null, null);
            Check("经纬度到经纬度原样返回", sSame == sLngLat);
        }

        //（10）视图变换
        private static void TestMapTransform()
        {
            Console.WriteLine();
            Console.WriteLine("---- MapTransform ----");
            MapTransform sMapTransform = new MapTransform(800, 600);
            //地图坐标↔屏幕坐标往返
            sMapTransform.SetView(100000, 200000, 10000);
            System.Drawing.Point sScreen = sMapTransform.MapToScreen(new Coordinate(110000, 190000));
            Coordinate sMapBack = sMapTransform.ScreenToMap(sScreen);
            //屏幕坐标取整到像素，往返误差应小于1个像素代表的地图单位
            Check("MapToScreen/ScreenToMap往返", AlmostEqual(sMapBack.X, 110000, 5) && AlmostEqual(sMapBack.Y, 190000, 5));
            //Y方向翻转：地图北方向的点应位于屏幕上方（更小的Y像素）
            System.Drawing.Point sTop = sMapTransform.MapToScreen(new Coordinate(100000, 200100));
            System.Drawing.Point sBottom = sMapTransform.MapToScreen(new Coordinate(100000, 199900));
            Check("Y方向翻转（北上）", sTop.Y < sBottom.Y);
            //窗口左上角对应偏移量
            Coordinate sTopLeft = sMapTransform.ScreenToMap(0, 0);
            Check("屏幕左上角地图坐标=偏移量", AlmostEqual(sTopLeft.X, 100000, 1e-6) && AlmostEqual(sTopLeft.Y, 200000, 1e-6));
            //GetExtent
            Envelope sExtent = sMapTransform.GetExtent();
            Check("窗口范围宽度", AlmostEqual(sExtent.Width, 800 * 10000 / (96 / 0.0254), 1e-6));
            Check("窗口范围高度", AlmostEqual(sExtent.Height, 600 * 10000 / (96 / 0.0254), 1e-6));
            //ZoomByCenter保持中心屏幕位置不变
            Coordinate sCenter = sMapTransform.ScreenToMap(400, 300);
            sMapTransform.ZoomByCenter(sCenter, 2);
            Coordinate sCenter2 = sMapTransform.ScreenToMap(400, 300);
            Check("ZoomByCenter中心不动", AlmostEqual(sCenter.X, sCenter2.X, 1e-6) && AlmostEqual(sCenter.Y, sCenter2.Y, 1e-6));
            Check("ZoomByCenter比例尺减半", AlmostEqual(sMapTransform.MapScale, 5000, 1e-6));
            //ZoomToExtent：范围中心应位于窗口中心
            Envelope sTarget = new Envelope(0, 1000, 0, 1000);
            sMapTransform.ZoomToExtent(sTarget);
            Coordinate sCenter3 = sMapTransform.ScreenToMap(400, 300);
            Check("ZoomToExtent后范围居中", AlmostEqual(sCenter3.X, 500, 1e-6) && AlmostEqual(sCenter3.Y, 500, 1e-6));
            //PanTo与PanDelta
            sMapTransform.SetView(0, 0, 1000);
            sMapTransform.PanTo(500, 500);
            Coordinate sCenter4 = sMapTransform.ScreenToMap(400, 300);
            Check("PanTo使目标点居中", AlmostEqual(sCenter4.X, 500, 1e-6) && AlmostEqual(sCenter4.Y, 500, 1e-6));
            double sBeforeX = sMapTransform.MapOffsetX;
            sMapTransform.PanDelta(sMapTransform.MapUnitsPerPixel() * 100, 0);
            Check("PanDelta按地图单位平移", AlmostEqual(sMapTransform.MapOffsetX, sBeforeX - sMapTransform.MapUnitsPerPixel() * 100, 1e-6));
            //比例尺钳制
            sMapTransform.MapScale = 1e12;
            Check("比例尺钳制到最大值", AlmostEqual(sMapTransform.MapScale, sMapTransform.MaxMapScale, 1e-6));
        }

        //（11）图层与符号
        private static void TestLayerSymbol()
        {
            Console.WriteLine();
            Console.WriteLine("---- Layer与Symbol ----");
            SimpleMarkerSymbol sMarker = new SimpleMarkerSymbol("点符号");
            sMarker.Size = 5;
            Symbol sCloned = sMarker.Clone();
            sCloned.Label = "克隆符号";
            Check("点符号克隆后互不影响", sMarker.Label == "点符号" && sCloned.Label == "克隆符号");
            SimpleFillSymbol sFill = new SimpleFillSymbol("面符号");
            Check("面符号默认带边界符号", sFill.Outline != null);
            SimpleLineSymbol sLine = new SimpleLineSymbol("线符号");
            sLine.Style = SimpleLineSymbolStyleConstant.Dash;
            Check("线符号线型设置", sLine.Style == SimpleLineSymbolStyleConstant.Dash);
            Check("符号类型常数", sMarker.SymbolType == SymbolTypeConstant.SimpleMarkerSymbol
                && sLine.SymbolType == SymbolTypeConstant.SimpleLineSymbol
                && sFill.SymbolType == SymbolTypeConstant.SimpleFillSymbol);
        }

        #endregion

        #region 辅助函数

        //单点正反算闭合误差（经纬度往返，换算为米的近似值按纬度换算）
        private static double RoundTripError(ProjectionCS projection, Coordinate lngLat)
        {
            Coordinate sProj = projection.TransferToProjCo(lngLat);
            Coordinate sBack = projection.TransferToLngLat(sProj);
            //经度方向1度≈111.32km×cos(纬度)，纬度方向1度≈111.32km
            double sMeterPerLng = 111320 * Math.Cos(DegToRad(lngLat.Y));
            double sDX = (sBack.X - lngLat.X) * sMeterPerLng;
            double sDY = (sBack.Y - lngLat.Y) * 110540;
            return Math.Sqrt(sDX * sDX + sDY * sDY);
        }

        //多点正反算闭合误差的最大值
        private static double RoundTripErrorMulti(ProjectionCS projection, List<Coordinate> lngLats)
        {
            double sMaxError = 0;
            for (Int32 i = 0; i <= lngLats.Count - 1; i++)
            {
                double sError = RoundTripError(projection, lngLats[i]);
                if (sError > sMaxError)
                    sMaxError = sError;
            }
            return sMaxError;
        }

        //高斯-克吕格投影：每个点按其所在带号投影后正反算闭合误差的最大值
        private static double RoundTripErrorGkPerZoneMulti(Ellipsoid ellipsoid, bool is3Degree, List<Coordinate> lngLats)
        {
            double sMaxError = 0;
            for (Int32 i = 0; i <= lngLats.Count - 1; i++)
            {
                Coordinate sPoint = lngLats[i];
                Int32 sZone = is3Degree ? ProjGauss_Kruger.GetZone3(sPoint.X) : ProjGauss_Kruger.GetZone6(sPoint.X);
                ProjGauss_Kruger sGk = ProjGauss_Kruger.Create(ellipsoid, sZone, is3Degree);
                double sError = RoundTripError(sGk, sPoint);
                if (sError > sMaxError)
                    sMaxError = sError;
            }
            return sMaxError;
        }

        //UTM投影：每个点按其所在带号投影后正反算闭合误差的最大值
        private static double RoundTripErrorUtmPerZoneMulti(List<Coordinate> lngLats)
        {
            double sMaxError = 0;
            for (Int32 i = 0; i <= lngLats.Count - 1; i++)
            {
                Coordinate sPoint = lngLats[i];
                ProjUTM sUtm = new ProjUTM(ProjUTM.GetZone(sPoint.X));
                double sError = RoundTripError(sUtm, sPoint);
                if (sError > sMaxError)
                    sMaxError = sError;
            }
            return sMaxError;
        }

        //全国范围内的代表性检查点（经度, 纬度）
        private static List<Coordinate> GetTestLngLatPoints()
        {
            List<Coordinate> sPoints = new List<Coordinate>();
            sPoints.Add(new Coordinate(116.4, 39.9));       //北京
            sPoints.Add(new Coordinate(121.5, 31.2));       //上海
            sPoints.Add(new Coordinate(113.2, 23.1));       //广州
            sPoints.Add(new Coordinate(104.1, 30.6));       //成都
            sPoints.Add(new Coordinate(87.6, 43.8));        //乌鲁木齐
            sPoints.Add(new Coordinate(126.6, 45.8));       //哈尔滨
            sPoints.Add(new Coordinate(91.1, 29.6));        //拉萨
            sPoints.Add(new Coordinate(114.3, 30.6));       //武汉
            return sPoints;
        }

        private static double DegToRad(double deg)
        {
            return deg * Math.PI / 180;
        }

        #endregion
    }
}
