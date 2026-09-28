using System;
using System.Collections.Generic;
using System.Globalization;

namespace GIS
{
    /// <summary>
    /// WKT（Well-Known Text，OGC简单要素标准的文本表示）读写工具类（静态类）。
    /// 支持Point/LineString/Polygon/MultiPoint/MultiLineString/MultiPolygon的读写，供导入导出模块复用。
    /// </summary>
    public static class GeometryWkt
    {
        #region 解析

        /// <summary>
        /// 从WKT文本解析几何对象，格式非法时抛出异常
        /// </summary>
        public static Geometry FromWKT(string wkt)
        {
            if (string.IsNullOrEmpty(wkt))
                throw new ArgumentException("WKT文本为空");
            string sText = wkt.Trim();
            //分离类型关键字与坐标体
            Int32 sIndex = sText.IndexOf('(');
            string sKeyword;
            string sBody;
            if (sIndex < 0)
            {
                //无括号的情况，形如"POINT EMPTY"
                string[] sTokens = sText.Split(new char[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                sKeyword = sTokens[0].ToUpper();
                sBody = (sTokens.Length >= 2 && sTokens[1].ToUpper() == "EMPTY") ? "EMPTY" : "";
            }
            else
            {
                sKeyword = sText.Substring(0, sIndex).Trim().ToUpper();
                sBody = sText.Substring(sIndex);
            }
            switch (sKeyword)
            {
                case "POINT":
                    {
                        if (IsEmptyMark(sBody))
                            return new Point();
                        List<Coordinate> sCoordinates = ParseCoordinateList(StripOuter(sBody));
                        if (sCoordinates.Count != 1)
                            throw new ArgumentException("POINT应只包含一个坐标点");
                        return new Point(sCoordinates[0]);
                    }
                case "LINESTRING":
                    {
                        if (IsEmptyMark(sBody))
                            return new LineString();
                        return new LineString(CoordinatesToPoints(ParseCoordinateList(StripOuter(sBody))));
                    }
                case "POLYGON":
                    {
                        if (IsEmptyMark(sBody))
                            return new Polygon();
                        List<string> sGroups = SplitTopLevel(StripOuter(sBody));
                        if (sGroups.Count < 1)
                            throw new ArgumentException("POLYGON至少需要一个环");
                        Polygon sPolygon = new Polygon(CoordinatesToPoints(ParseCoordinateList(StripOuter(sGroups[0]))));
                        for (Int32 i = 1; i <= sGroups.Count - 1; i++)
                        {
                            sPolygon.Holes.Add(CoordinatesToPoints(ParseCoordinateList(StripOuter(sGroups[i]))));
                        }
                        return sPolygon;
                    }
                case "MULTIPOINT":
                    {
                        if (IsEmptyMark(sBody))
                            return new MultiPoint();
                        //兼容"MULTIPOINT ((30 10), (40 30))"与"MULTIPOINT (30 10, 40 30)"两种写法
                        string sInner = StripOuter(sBody);
                        Points sPoints = new Points();
                        if (sInner.TrimStart().StartsWith("("))
                        {
                            List<string> sPtGroups = SplitTopLevel(sInner);
                            for (Int32 i = 0; i <= sPtGroups.Count - 1; i++)
                            {
                                List<Coordinate> sOne = ParseCoordinateList(StripOuter(sPtGroups[i]));
                                for (Int32 j = 0; j <= sOne.Count - 1; j++)
                                {
                                    sPoints.Add(sOne[j]);
                                }
                            }
                        }
                        else
                        {
                            sPoints = CoordinatesToPoints(ParseCoordinateList(sInner));
                        }
                        return new MultiPoint(sPoints);
                    }
                case "MULTILINESTRING":
                    {
                        if (IsEmptyMark(sBody))
                            return new MultiLineString();
                        MultiLineString sMultiLine = new MultiLineString();
                        List<string> sLineGroups = SplitTopLevel(StripOuter(sBody));
                        for (Int32 i = 0; i <= sLineGroups.Count - 1; i++)
                        {
                            sMultiLine.Parts.Add(new LineString(CoordinatesToPoints(ParseCoordinateList(StripOuter(sLineGroups[i])))));
                        }
                        return sMultiLine;
                    }
                case "MULTIPOLYGON":
                    {
                        if (IsEmptyMark(sBody))
                            return new MultiPolygon();
                        MultiPolygon sMultiPolygon = new MultiPolygon();
                        List<string> sPolyGroups = SplitTopLevel(StripOuter(sBody));
                        for (Int32 i = 0; i <= sPolyGroups.Count - 1; i++)
                        {
                            List<string> sRingGroups = SplitTopLevel(StripOuter(sPolyGroups[i]));
                            if (sRingGroups.Count < 1)
                                throw new ArgumentException("MULTIPOLYGON中的多边形至少需要一个环");
                            Polygon sPolygon = new Polygon(CoordinatesToPoints(ParseCoordinateList(StripOuter(sRingGroups[0]))));
                            for (Int32 j = 1; j <= sRingGroups.Count - 1; j++)
                            {
                                sPolygon.Holes.Add(CoordinatesToPoints(ParseCoordinateList(StripOuter(sRingGroups[j]))));
                            }
                            sMultiPolygon.Parts.Add(sPolygon);
                        }
                        return sMultiPolygon;
                    }
                default:
                    throw new ArgumentException("不支持的WKT类型：" + sKeyword);
            }
        }

        //判断是否为EMPTY标记
        private static bool IsEmptyMark(string body)
        {
            return (body == "" || body.Trim().ToUpper() == "EMPTY");
        }

        //去掉最外层的一对括号
        private static string StripOuter(string body)
        {
            string s = body.Trim();
            if (s.StartsWith("(") && s.EndsWith(")"))
                return s.Substring(1, s.Length - 2);
            throw new ArgumentException("WKT括号不匹配：" + body);
        }

        //按最外层逗号拆分括号组
        private static List<string> SplitTopLevel(string text)
        {
            List<string> sGroups = new List<string>();
            Int32 sDepth = 0;
            Int32 sStart = 0;
            for (Int32 i = 0; i <= text.Length - 1; i++)
            {
                char c = text[i];
                if (c == '(')
                    sDepth++;
                else if (c == ')')
                    sDepth--;
                else if (c == ',' && sDepth == 0)
                {
                    sGroups.Add(text.Substring(sStart, i - sStart));
                    sStart = i + 1;
                }
            }
            sGroups.Add(text.Substring(sStart));
            return sGroups;
        }

        //解析坐标序列文本（形如"30 10, 10 30, 40 40"），支持携带Z/M的冗余数值（忽略）
        private static List<Coordinate> ParseCoordinateList(string text)
        {
            List<Coordinate> sCoordinates = new List<Coordinate>();
            string[] sParts = text.Split(',');
            for (Int32 i = 0; i <= sParts.Length - 1; i++)
            {
                string[] sNumbers = sParts[i].Split(new char[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                if (sNumbers.Length < 2)
                    throw new ArgumentException("坐标点至少需要X与Y两个数值：" + sParts[i]);
                double sX = double.Parse(sNumbers[0], NumberStyles.Float, CultureInfo.InvariantCulture);
                double sY = double.Parse(sNumbers[1], NumberStyles.Float, CultureInfo.InvariantCulture);
                sCoordinates.Add(new Coordinate(sX, sY));
            }
            return sCoordinates;
        }

        private static Points CoordinatesToPoints(List<Coordinate> coordinates)
        {
            Points sPoints = new Points();
            for (Int32 i = 0; i <= coordinates.Count - 1; i++)
            {
                sPoints.Add(coordinates[i]);
            }
            return sPoints;
        }

        #endregion

        #region 输出

        /// <summary>
        /// 格式化单个坐标点（不变文化，避免小数点符号差异）
        /// </summary>
        public static string FormatCoordinate(Coordinate coordinate)
        {
            return coordinate.X.ToString("R", CultureInfo.InvariantCulture)
                + " " + coordinate.Y.ToString("R", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 格式化坐标序列（形如"30 10, 10 30"）
        /// </summary>
        public static string FormatCoordinateSequence(Points points)
        {
            string sText = "";
            for (Int32 i = 0; i <= points.Count - 1; i++)
            {
                if (i > 0)
                    sText += ", ";
                sText += FormatCoordinate(points.GetItem(i));
            }
            return sText;
        }

        /// <summary>
        /// 格式化闭合环（若首尾未闭合则自动补上首点）
        /// </summary>
        public static string FormatClosedRing(Points ring)
        {
            string sText = FormatCoordinateSequence(ring);
            if (ring.Count >= 3 && ring.GetItem(0) != ring.GetItem(ring.Count - 1))
            {
                sText += ", " + FormatCoordinate(ring.GetItem(0));
            }
            return sText;
        }

        #endregion
    }
}
