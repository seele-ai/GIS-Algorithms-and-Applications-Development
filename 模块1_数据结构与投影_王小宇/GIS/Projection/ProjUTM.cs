using System;

namespace GIS
{
    /// <summary>
    /// UTM（Universal Transverse Mercator）通用横轴墨卡托投影坐标系统。
    /// 与高斯-克吕格同属横轴等角圆柱投影，区别在于比例因子为0.9996，全球按6度分带（1~60带）。
    /// </summary>
    public class ProjUTM : ProjectionCS
    {
        /// <summary>UTM投影固定比例因子</summary>
        public const double UTM_SCALE_FACTOR = 0.9996;

        #region 构造函数

        /// <summary>
        /// 按带号构造UTM投影坐标系统
        /// </summary>
        /// <param name="zone">带号（1~60）</param>
        /// <param name="isNorthernHemisphere">是否北半球（南半球北伪偏移取10000000米）</param>
        /// <param name="ellipsoid">椭球体参数，默认WGS84</param>
        public ProjUTM(Int32 zone, bool isNorthernHemisphere = true, Ellipsoid ellipsoid = null)
            : base(MakeName(zone, isNorthernHemisphere), ellipsoid ?? Ellipsoids.WGS84, ProjectionTypeConstant.UTM)
        {
            OriginLatitude = 0;
            CentralMeridian = zone * 6.0 - 183;
            FalseEasting = 500000;
            FalseNorthing = isNorthernHemisphere ? 0 : 10000000;
            ScaleFactor = UTM_SCALE_FACTOR;
            LinearUnit = LinearUnitConstant.Meter;
        }

        //自动命名
        private static string MakeName(Int32 zone, bool isNorthernHemisphere)
        {
            return "WGS_1984 UTM Zone " + zone + (isNorthernHemisphere ? "N" : "S");
        }

        #endregion

        #region 工厂函数

        /// <summary>
        /// 根据经度计算UTM带号
        /// </summary>
        public static Int32 GetZone(double longitude)
        {
            return (Int32)Math.Floor((longitude + 180) / 6) + 1;
        }

        #endregion

        #region 投影正反算

        /// <summary>
        /// 将经纬度坐标转换为UTM平面直角坐标
        /// </summary>
        public override Coordinate TransferToProjCo(Coordinate lngLat)
        {
            return TransverseMercatorMath.Forward(SemiMajor, GetE2(), ScaleFactor,
                CentralMeridian, lngLat.X, lngLat.Y, FalseEasting, FalseNorthing);
        }

        /// <summary>
        /// 将UTM平面直角坐标转换为经纬度坐标
        /// </summary>
        public override Coordinate TransferToLngLat(Coordinate projCo)
        {
            return TransverseMercatorMath.Inverse(SemiMajor, GetE2(), ScaleFactor,
                CentralMeridian, projCo.X, projCo.Y, FalseEasting, FalseNorthing);
        }

        #endregion
    }
}
