using System;

namespace GIS
{
    /// <summary>
    /// 横轴等角圆柱投影（Transverse Mercator）正反算数学工具（内部类）。
    /// 高斯-克吕格投影与UTM投影同属横轴等角圆柱投影，仅参数不同（比例因子、分带），共用本套公式。
    /// </summary>
    internal static class TransverseMercatorMath
    {
        /// <summary>
        /// 投影正算：经纬度（度）→ 投影坐标（米）
        /// </summary>
        /// <param name="a">长半轴</param>
        /// <param name="e2">第一偏心率平方</param>
        /// <param name="k0">比例因子</param>
        /// <param name="lng0">中央经线（度）</param>
        /// <param name="lng">经度（度）</param>
        /// <param name="lat">纬度（度）</param>
        /// <param name="falseEasting">东伪偏移</param>
        /// <param name="falseNorthing">北伪偏移</param>
        public static Coordinate Forward(double a, double e2, double k0,
            double lng0, double lng, double lat, double falseEasting, double falseNorthing)
        {
            double phi = DegToRad(lat);
            double dLng = DegToRad(lng - lng0);
            //将经差规范到[-180°,180°]
            while (dLng > Math.PI)
                dLng -= 2 * Math.PI;
            while (dLng < -Math.PI)
                dLng += 2 * Math.PI;

            double sinPhi = Math.Sin(phi), cosPhi = Math.Cos(phi), tanPhi = Math.Sin(phi) / Math.Cos(phi);
            double ep2 = e2 / (1 - e2);                             //第二偏心率平方
            double N = a / Math.Sqrt(1 - e2 * sinPhi * sinPhi);     //卯酉圈曲率半径
            double T = tanPhi * tanPhi;
            double C = ep2 * cosPhi * cosPhi;
            double A = dLng * cosPhi;
            //自赤道起的子午线弧长
            double M = MeridianArc(phi, a, e2);

            double xNorth = k0 * (M + N * tanPhi * (A * A / 2
                + (5 - T + 9 * C + 4 * C * C) * Math.Pow(A, 4) / 24
                + (61 - 58 * T + T * T + 600 * C - 330 * ep2) * Math.Pow(A, 6) / 720));
            double yEast = k0 * N * (A
                + (1 - T + C) * Math.Pow(A, 3) / 6
                + (5 - 18 * T + T * T + 72 * C - 58 * ep2) * Math.Pow(A, 5) / 120)
                + falseEasting;
            xNorth += falseNorthing;
            return new Coordinate(yEast, xNorth);
        }

        /// <summary>
        /// 投影反算：投影坐标（米）→ 经纬度（度）
        /// </summary>
        public static Coordinate Inverse(double a, double e2, double k0,
            double lng0, double easting, double northing, double falseEasting, double falseNorthing)
        {
            double ep2 = e2 / (1 - e2);
            double yEast = easting - falseEasting;
            double M = (northing - falseNorthing) / k0;             //自赤道起的子午线弧长
            // 限定在投影有效范围内：子午线弧长最大到南北纬 89.5°，避免传入远离投影带的坐标时
            // 级数发散、算出 -29929639° 这类荒谬的纬度值（横向等角圆柱投影本身也不用于极区）。
            double limit = MeridianArc(89.5 * Math.PI / 180, a, e2);
            if (M > limit) M = limit;
            else if (M < -limit) M = -limit;
            //计算底点纬度
            double phi1 = FootpointLatitude(M, a, e2);

            double sinPhi1 = Math.Sin(phi1), cosPhi1 = Math.Cos(phi1), tanPhi1 = sinPhi1 / cosPhi1;
            double C1 = ep2 * cosPhi1 * cosPhi1;
            double T1 = tanPhi1 * tanPhi1;
            double N1 = a / Math.Sqrt(1 - e2 * sinPhi1 * sinPhi1);
            double R1 = a * (1 - e2) / Math.Pow(1 - e2 * sinPhi1 * sinPhi1, 1.5);       //子午圈曲率半径
            double D = yEast / (N1 * k0);
            // 远离中央经线时高阶级数同样不可靠，按 UTM/高斯分带的有效宽度限制
            if (D > 0.3) D = 0.3;
            else if (D < -0.3) D = -0.3;

            double phi = phi1 - (N1 * tanPhi1 / R1) * (D * D / 2
                - (5 + 3 * T1 + 10 * C1 - 4 * C1 * C1 - 9 * ep2) * Math.Pow(D, 4) / 24
                + (61 + 90 * T1 + 298 * C1 + 45 * T1 * T1 - 252 * ep2 - 3 * C1 * C1) * Math.Pow(D, 6) / 720);
            double lng = DegToRad(lng0) + (D
                - (1 + 2 * T1 + C1) * Math.Pow(D, 3) / 6
                + (5 - 2 * C1 + 28 * T1 - 3 * C1 * C1 + 8 * ep2 + 24 * T1 * T1) * Math.Pow(D, 5) / 120) / cosPhi1;

            double latitude = RadToDeg(phi);
            if (latitude > 90) latitude = 90;
            else if (latitude < -90) latitude = -90;
            return new Coordinate(NormalizeLng(RadToDeg(lng)), latitude);
        }

        /// <summary>
        /// 获取自赤道起的子午线弧长
        /// </summary>
        public static double MeridianArc(double phi, double a, double e2)
        {
            double e2_2 = e2 * e2, e2_3 = e2_2 * e2;
            double m0 = 1 - e2 / 4 - 3 * e2_2 / 64 - 5 * e2_3 / 256;
            return a * (m0 * phi
                - (3 * e2 / 8 + 3 * e2_2 / 32 + 45 * e2_3 / 1024) * Math.Sin(2 * phi)
                + (15 * e2_2 / 256 + 45 * e2_3 / 1024) * Math.Sin(4 * phi)
                - (35 * e2_3 / 3072) * Math.Sin(6 * phi));
        }

        /// <summary>
        /// 由子午线弧长迭代求底点纬度
        /// </summary>
        public static double FootpointLatitude(double M, double a, double e2)
        {
            double mu = M / (a * (1 - e2 / 4 - 3 * e2 * e2 / 64 - 5 * Math.Pow(e2, 3) / 256));
            double e1 = (1 - Math.Sqrt(1 - e2)) / (1 + Math.Sqrt(1 - e2));
            double e1_2 = e1 * e1, e1_3 = e1_2 * e1, e1_4 = e1_3 * e1;
            return mu
                + (3 * e1 / 2 - 27 * e1_3 / 32) * Math.Sin(2 * mu)
                + (21 * e1_2 / 16 - 55 * e1_4 / 32) * Math.Sin(4 * mu)
                + (151 * e1_3 / 96) * Math.Sin(6 * mu)
                + (1097 * e1_4 / 512) * Math.Sin(8 * mu);
        }

        private static double DegToRad(double deg)
        {
            return deg * Math.PI / 180;
        }

        private static double RadToDeg(double rad)
        {
            return rad * 180 / Math.PI;
        }

        //将经度规范到[-180,180]
        private static double NormalizeLng(double lng)
        {
            while (lng > 180)
                lng -= 360;
            while (lng < -180)
                lng += 360;
            return lng;
        }
    }
}
