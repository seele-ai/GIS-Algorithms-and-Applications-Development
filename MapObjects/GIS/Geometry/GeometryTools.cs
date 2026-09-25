using System;

namespace GIS
{
    /// <summary>
    /// 几何算法工具类（静态类）。
    /// 提供距离计算、射线法判包含、矩形相交、线段求交等基础几何算法，
    /// 是各几何对象Contains/Intersects/Distance内置函数的实现基础。
    /// </summary>
    public static class GeometryTools
    {
        #region 距离计算

        /// <summary>
        /// 获取两点之间的距离
        /// </summary>
        public static double GetDisFromPointToPoint(Coordinate point1, Coordinate point2)
        {
            double sDis;
            sDis = (point1.X - point2.X) * (point1.X - point2.X)
                + (point1.Y - point2.Y) * (point1.Y - point2.Y);
            sDis = Math.Sqrt(sDis);
            return sDis;
        }

        /// <summary>
        /// 获取点到线段的最短距离
        /// </summary>
        /// <remarks>思路：采用向量内积法求垂足</remarks>
        public static double GetDisFromPointToSegment(Coordinate point, Coordinate sPoint, Coordinate ePoint)
        {
            double pX = ePoint.X - sPoint.X;
            double pY = ePoint.Y - sPoint.Y;        //线段两端点坐标之差
            double som = pX * pX + pY * pY;         //两端点坐标之差的平方和
            if (som == 0)
            {
                //线段起点与终点重合，退化为点到点的距离
                return point.DistanceTo(sPoint);
            }
            double u = ((point.X - sPoint.X) * pX + (point.Y - sPoint.Y) * pY) / som;
            if (u > 1)
                u = 1;
            else if (u < 0)
                u = 0;
            double x = sPoint.X + u * pX, y = sPoint.Y + u * pY;
            double dX = x - point.X, dY = y - point.Y;
            return Math.Sqrt(dX * dX + dY * dY);
        }

        /// <summary>
        /// 获取折线的中点（沿弧长方向的中点）
        /// </summary>
        public static Coordinate GetMidPointOfPolyline(Points points)
        {
            Int32 sPointCount = points.Count;
            if (sPointCount == 0)
                return new Coordinate(0, 0);
            if (sPointCount == 1)
                return points.GetItem(0);
            //累计各顶点至起点的距离
            double[] sDises = new double[sPointCount];
            sDises[0] = 0;
            for (Int32 i = 1; i <= sPointCount - 1; i++)
            {
                sDises[i] = sDises[i - 1] + points.GetItem(i).DistanceTo(points.GetItem(i - 1));
            }
            if (sDises[sPointCount - 1] == 0)
                return points.GetItem(0);
            //查找中点所在的线段下标
            double sMidDis = sDises[sPointCount - 1] / 2;
            Int32 sIndex = 0;
            for (Int32 i = 0; i <= sPointCount - 2; i++)
            {
                if (sMidDis >= sDises[i] && sMidDis <= sDises[i + 1])
                {
                    sIndex = i;
                    break;
                }
            }
            //按比例内插中点
            Coordinate sPoint1 = points.GetItem(sIndex);
            Coordinate sPoint2 = points.GetItem(sIndex + 1);
            double sSegDis = sDises[sIndex + 1] - sDises[sIndex];
            if (sSegDis == 0)
                return sPoint1;
            double sRatio = (sMidDis - sDises[sIndex]) / sSegDis;
            return new Coordinate(sPoint1.X + (sPoint2.X - sPoint1.X) * sRatio,
                sPoint1.Y + (sPoint2.Y - sPoint1.Y) * sRatio);
        }

        #endregion

        #region 点与矩形

        /// <summary>
        /// 指示指定点是否位于指定矩形范围内或边界上
        /// </summary>
        public static bool IsPointWithinBox(Coordinate point, Envelope box)
        {
            return box.Contains(point);
        }

        /// <summary>
        /// 指示在指定容限下，一个点是否位于另一个点上
        /// </summary>
        public static bool IsPointOnPoint(Coordinate point, Coordinate pointOverlapped, double tolerance)
        {
            return GetDisFromPointToPoint(point, pointOverlapped) <= tolerance;
        }

        /// <summary>
        /// 指示在指定容限下，指定点是否位于指定折线上
        /// </summary>
        public static bool IsPointOnPolyline(Coordinate point, Points points, double tolerance)
        {
            if (points.Count == 0)
                return false;
            //单顶点时退化为点与点的容限判断
            if (points.Count == 1)
                return IsPointOnPoint(point, points.GetItem(0), tolerance);
            //先按外包矩形加容限粗判
            Envelope sExtent = points.GetEnvelope();
            Envelope sBox = new Envelope(sExtent.MinX - tolerance, sExtent.MaxX + tolerance,
                sExtent.MinY - tolerance, sExtent.MaxY + tolerance);
            if (IsPointWithinBox(point, sBox) == false)
                return false;
            //逐线段判断
            for (Int32 i = 0; i <= points.Count - 2; i++)
            {
                if (GetDisFromPointToSegment(point, points.GetItem(i), points.GetItem(i + 1)) <= tolerance)
                    return true;
            }
            return false;
        }

        #endregion

        #region 射线法判包含

        /// <summary>
        /// 一条自指定点水平向右的射线是否与指定线段相交
        /// </summary>
        public static bool IsRayCrossSegment(Coordinate point, Coordinate sPoint, Coordinate ePoint)
        {
            if (sPoint.Y == ePoint.Y)
                //线段与射线平行
                return false;
            if (sPoint.Y > point.Y && ePoint.Y > point.Y)
                //线段位于射线上方
                return false;
            if (sPoint.Y < point.Y && ePoint.Y < point.Y)
                //线段位于射线下方
                return false;
            if (sPoint.Y == point.Y && ePoint.Y > point.Y)
                //交点为线段下端点，不计
                return false;
            if (ePoint.Y == point.Y && sPoint.Y > point.Y)
                //交点为线段下端点，不计
                return false;
            if (sPoint.X < point.X && ePoint.X < point.X)
                //线段位于起点的左侧
                return false;
            double x = ePoint.X - (ePoint.X - sPoint.X) * (ePoint.Y - point.Y) / (ePoint.Y - sPoint.Y);
            if (x < point.X)
                //交点在射线起点的左侧，视为无交点
                return false;
            return true;
        }

        /// <summary>
        /// 求自指定点水平向右的射线与指定环（按首尾闭合处理）的交点个数
        /// </summary>
        public static Int32 GetIntersectionCountBetweenRayAndPolygon(Coordinate point, Points ring)
        {
            Int32 sIntersectionCount = 0;
            Int32 sPointCount = ring.Count;
            if (sPointCount < 3)
                return 0;
            //首点与末点的连线
            if (IsRayCrossSegment(point, ring.GetItem(sPointCount - 1), ring.GetItem(0)) == true)
            {
                sIntersectionCount = sIntersectionCount + 1;
            }
            //其余各边
            for (Int32 i = 0; i <= sPointCount - 2; i++)
            {
                if (IsRayCrossSegment(point, ring.GetItem(i), ring.GetItem(i + 1)) == true)
                {
                    sIntersectionCount = sIntersectionCount + 1;
                }
            }
            return sIntersectionCount;
        }

        /// <summary>
        /// 指示指定点是否位于指定环内或边界上（射线法，交点个数为奇数则在内部）
        /// </summary>
        public static bool IsPointWithinPolygon(Coordinate point, Points ring)
        {
            //（1）判断点是否位于外包矩形内，如否则直接返回否
            Envelope sEnvelope = ring.GetEnvelope();
            if (IsPointWithinBox(point, sEnvelope) == false)
            {
                return false;
            }
            //（2）求射线与环的交点数
            Int32 sIntersectionCount = GetIntersectionCountBetweenRayAndPolygon(point, ring);
            //（3）交点数为奇数则位于多边形内
            return (sIntersectionCount % 2 == 1);
        }

        #endregion

        #region 矩形与线段求交

        /// <summary>
        /// 指示两个矩形是否相交（含边界接触）
        /// </summary>
        public static bool AreBoxesCross(Envelope box1, Envelope box2)
        {
            if (box1.MinX > box2.MaxX || box1.MaxX < box2.MinX)
                return false;
            else if (box1.MinY > box2.MaxY || box1.MaxY < box2.MinY)
                return false;
            else
                return true;
        }

        /// <summary>
        /// 指示两条线段是否有交点（参数方程法）
        /// </summary>
        public static bool AreSegmentsCross(Coordinate sPoint1, Coordinate ePoint1, Coordinate sPoint2, Coordinate ePoint2)
        {
            double deltaX1 = ePoint1.X - sPoint1.X, deltaY1 = ePoint1.Y - sPoint1.Y;
            double deltaX2 = ePoint2.X - sPoint2.X, deltaY2 = ePoint2.Y - sPoint2.Y;

            //两条线段平行时视为无交点
            if (deltaY2 * deltaX1 - deltaX2 * deltaY1 == 0)
                return false;
            //求交点参数
            double t, l;
            t = (deltaX2 * (sPoint1.Y - sPoint2.Y) - deltaY2 * (sPoint1.X - sPoint2.X)) / (deltaX1 * deltaY2 - deltaX2 * deltaY1);
            l = (deltaX1 * (sPoint1.Y - sPoint2.Y) - deltaY1 * (sPoint1.X - sPoint2.X)) / (deltaX1 * deltaY2 - deltaX2 * deltaY1);
            if (t >= 0 && t <= 1 && l >= 0 && l <= 1)
                return true;
            else
                return false;
        }

        /// <summary>
        /// 指示指定线段是否与指定矩形相交（端点落入矩形也算）
        /// </summary>
        public static bool IsSegmentCrossBox(Coordinate sPoint, Coordinate ePoint, Envelope box)
        {
            //（1）线段两端点全部位于矩形某条边的外侧时无交点
            if (sPoint.X < box.MinX && ePoint.X < box.MinX)
                return false;
            if (sPoint.X > box.MaxX && ePoint.X > box.MaxX)
                return false;
            if (sPoint.Y < box.MinY && ePoint.Y < box.MinY)
                return false;
            if (sPoint.Y > box.MaxY && ePoint.Y > box.MaxY)
                return false;
            //（2）定义矩形四个顶点
            Coordinate sPoint1 = new Coordinate(box.MinX, box.MaxY);        //左上点
            Coordinate sPoint2 = new Coordinate(box.MaxX, box.MaxY);        //右上点
            Coordinate sPoint3 = new Coordinate(box.MaxX, box.MinY);        //右下点
            Coordinate sPoint4 = new Coordinate(box.MinX, box.MinY);        //左下点
            //（3）线段与矩形每条边求交
            if (AreSegmentsCross(sPoint, ePoint, sPoint1, sPoint2) == true)
                return true;
            if (AreSegmentsCross(sPoint, ePoint, sPoint3, sPoint4) == true)
                return true;
            if (AreSegmentsCross(sPoint, ePoint, sPoint1, sPoint4) == true)
                return true;
            if (AreSegmentsCross(sPoint, ePoint, sPoint2, sPoint3) == true)
                return true;
            //（4）线段可能整体位于矩形内部，用任一端点判断
            if (IsPointWithinBox(sPoint, box) == true)
                return true;
            return false;
        }

        #endregion

        #region 折线/多边形与矩形相交

        /// <summary>
        /// 指示指定折线是否部分或完全位于指定矩形内
        /// </summary>
        public static bool IsPolylinePartiallyWithinBox(Points points, Envelope box)
        {
            //（1）外包矩形与矩形无交点则返回否
            if (AreBoxesCross(points.GetEnvelope(), box) == false)
                return false;
            //（2）任何一个顶点位于矩形内，返回是
            for (Int32 i = 0; i <= points.Count - 1; i++)
            {
                if (IsPointWithinBox(points.GetItem(i), box) == true)
                    return true;
            }
            //（3）任何一条线段与矩形有交点，返回是
            for (Int32 i = 0; i <= points.Count - 2; i++)
            {
                if (IsSegmentCrossBox(points.GetItem(i), points.GetItem(i + 1), box) == true)
                    return true;
            }
            //（4）都不满足，返回否
            return false;
        }

        /// <summary>
        /// 指示指定多边形（外环+内环）是否部分或完全位于指定矩形内
        /// </summary>
        public static bool IsPolygonPartiallyWithinBox(Points exteriorRing, System.Collections.Generic.List<Points> holes, Envelope box)
        {
            //（1）外包矩形与矩形无交点则返回否
            if (AreBoxesCross(exteriorRing.GetEnvelope(), box) == false)
                return false;
            //（2）任何一个顶点（外环或内环）位于矩形内，返回是
            if (IsRingPartiallyWithinBox(exteriorRing, box) == true)
                return true;
            if (holes != null)
            {
                for (Int32 i = 0; i <= holes.Count - 1; i++)
                {
                    if (IsRingPartiallyWithinBox(holes[i], box) == true)
                        return true;
                }
            }
            //（3）矩形的任何一个顶点位于多边形内（外环内且不在任何内环内），返回是
            Coordinate sRectPoint = new Coordinate(box.MinX, box.MinY);     //左下点
            if (IsPointInPolygonWithHoles(sRectPoint, exteriorRing, holes) == true)
                return true;
            sRectPoint = new Coordinate(box.MinX, box.MaxY);                //左上点
            if (IsPointInPolygonWithHoles(sRectPoint, exteriorRing, holes) == true)
                return true;
            sRectPoint = new Coordinate(box.MaxX, box.MaxY);                //右上点
            if (IsPointInPolygonWithHoles(sRectPoint, exteriorRing, holes) == true)
                return true;
            sRectPoint = new Coordinate(box.MaxX, box.MinY);                //右下点
            if (IsPointInPolygonWithHoles(sRectPoint, exteriorRing, holes) == true)
                return true;
            //（4）都不满足，返回否
            return false;
        }

        /// <summary>
        /// 指示指定点是否位于带内环的多边形内
        /// </summary>
        public static bool IsPointInPolygonWithHoles(Coordinate point, Points exteriorRing, System.Collections.Generic.List<Points> holes)
        {
            if (IsPointWithinPolygon(point, exteriorRing) == false)
                return false;
            if (holes != null)
            {
                for (Int32 i = 0; i <= holes.Count - 1; i++)
                {
                    if (IsPointWithinPolygon(point, holes[i]) == true)
                        return false;
                }
            }
            return true;
        }

        //指示环（按首尾闭合处理）的任一顶点位于矩形内或任一边穿越矩形
        private static bool IsRingPartiallyWithinBox(Points ring, Envelope box)
        {
            Int32 sPointCount = ring.Count;
            //（1）任何顶点位于矩形内
            for (Int32 i = 0; i <= sPointCount - 1; i++)
            {
                if (IsPointWithinBox(ring.GetItem(i), box) == true)
                    return true;
            }
            //（2）任何一条边与矩形有交点（首尾闭合）
            for (Int32 i = 0; i <= sPointCount - 2; i++)
            {
                if (IsSegmentCrossBox(ring.GetItem(i), ring.GetItem(i + 1), box) == true)
                    return true;
            }
            if (sPointCount >= 3)
            {
                if (IsSegmentCrossBox(ring.GetItem(sPointCount - 1), ring.GetItem(0), box) == true)
                    return true;
            }
            return false;
        }

        #endregion

        #region 面积计算

        /// <summary>
        /// 求指定环（按首尾闭合处理）的面积（鞋带公式，恒为正值）
        /// </summary>
        public static double GetPolygonArea(Points ring)
        {
            Int32 sPointCount = ring.Count;
            if (sPointCount < 3)
                return 0;
            double s = 0;
            double x0 = ring.GetItem(0).X, y0 = ring.GetItem(0).Y;
            for (Int32 i = 1; i <= sPointCount - 2; i++)
            {
                s = s + (ring.GetItem(i).X - x0) * (ring.GetItem(i + 1).Y - y0)
                    - (ring.GetItem(i + 1).X - x0) * (ring.GetItem(i).Y - y0);
            }
            s = Math.Abs(s / 2);
            return s;
        }

        #endregion
    }
}
