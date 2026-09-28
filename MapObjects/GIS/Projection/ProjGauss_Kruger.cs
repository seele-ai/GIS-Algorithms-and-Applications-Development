using System;

namespace GIS
{
    /// <summary>
    /// 高斯-克吕格投影坐标系统。
    /// 我国采用3度带与6度带分带，比例因子为1，东伪偏移一般为500000米。
    /// </summary>
    public class ProjGauss_Kruger : ProjectionCS
    {
        #region 构造函数

        /// <summary>
        /// 按中央经线等参数构造高斯-克吕格投影坐标系统
        /// </summary>
        /// <param name="projCSName">投影坐标系统名称</param>
        /// <param name="ellipsoid">椭球体参数</param>
        /// <param name="centralMeridian">中央经线（度）</param>
        /// <param name="originLatitude">原点纬度（度），一般为0</param>
        /// <param name="falseEasting">东伪偏移（米），一般为500000</param>
        /// <param name="falseNorthing">北伪偏移（米），一般为0</param>
        /// <param name="scaleFactor">比例因子，高斯-克吕格为1</param>
        /// <param name="linearUnit">线性单位</param>
        public ProjGauss_Kruger(string projCSName, Ellipsoid ellipsoid, double centralMeridian,
            double originLatitude = 0, double falseEasting = 500000, double falseNorthing = 0,
            double scaleFactor = 1, LinearUnitConstant linearUnit = LinearUnitConstant.Meter)
            : base(projCSName, ellipsoid, ProjectionTypeConstant.GaussKruger)
        {
            OriginLatitude = originLatitude;
            CentralMeridian = centralMeridian;
            FalseEasting = falseEasting;
            FalseNorthing = falseNorthing;
            ScaleFactor = scaleFactor;
            LinearUnit = linearUnit;
        }

        #endregion

        #region 工厂函数

        /// <summary>
        /// 按分带构造投影坐标系统（自动命名）
        /// </summary>
        /// <param name="ellipsoid">椭球体参数</param>
        /// <param name="zone">带号（从1开始）</param>
        /// <param name="is3Degree">是否3度带（否为6度带）</param>
        public static ProjGauss_Kruger Create(Ellipsoid ellipsoid, Int32 zone, bool is3Degree)
        {
            double sCentralMeridian = is3Degree ? 3.0 * zone : 6.0 * zone - 3;
            string sName = ellipsoid.GeoCSName + (is3Degree ? " 3度带 CM=" : " 6度带 CM=") + sCentralMeridian + "°E";
            return new ProjGauss_Kruger(sName, ellipsoid, sCentralMeridian);
        }

        /// <summary>
        /// 根据经度计算3度带带号
        /// </summary>
        public static Int32 GetZone3(double longitude)
        {
            return (Int32)Math.Round(longitude / 3);
        }

        /// <summary>
        /// 根据经度计算6度带带号
        /// </summary>
        public static Int32 GetZone6(double longitude)
        {
            return (Int32)Math.Floor(longitude / 6) + 1;
        }

        #endregion

        #region 投影正反算

        /// <summary>
        /// 将经纬度坐标转换为高斯平面直角坐标（X为东方向即通常意义的y，Y为北方向即x）
        /// </summary>
        public override Coordinate TransferToProjCo(Coordinate lngLat)
        {
            return TransverseMercatorMath.Forward(SemiMajor, GetE2(), ScaleFactor,
                CentralMeridian, lngLat.X, lngLat.Y, FalseEasting, FalseNorthing);
        }

        /// <summary>
        /// 将高斯平面直角坐标转换为经纬度坐标
        /// </summary>
        public override Coordinate TransferToLngLat(Coordinate projCo)
        {
            return TransverseMercatorMath.Inverse(SemiMajor, GetE2(), ScaleFactor,
                CentralMeridian, projCo.X, projCo.Y, FalseEasting, FalseNorthing);
        }

        #endregion
    }
}
