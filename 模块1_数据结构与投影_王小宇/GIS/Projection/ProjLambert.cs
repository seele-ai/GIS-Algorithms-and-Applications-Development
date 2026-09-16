using System;

namespace GIS
{
    /// <summary>
    /// Lambert等角圆锥投影坐标系统（双标准纬线，即Lambert_Conformal_Conic_2SP）。
    /// 适用于沿纬线延伸的中纬度地区，如我国全图常采用标准纬线25°与47°。
    /// </summary>
    public class ProjLambert : ProjectionCS
    {
        /// <summary>标准纬线1（度）</summary>
        public double StandardParallelOne { get; private set; }

        /// <summary>标准纬线2（度）</summary>
        public double StandardParallelTwo { get; private set; }

        private double _N;          //圆锥常数
        private double _F;          //比例系数F
        private double _Rho0;       //原点纬度处的圆锥投影半径

        #region 构造函数

        /// <summary>
        /// 构造双标准纬线Lambert等角圆锥投影坐标系统
        /// </summary>
        /// <param name="projCSName">投影坐标系统名称</param>
        /// <param name="ellipsoid">椭球体参数</param>
        /// <param name="centralMeridian">中央经线（度）</param>
        /// <param name="originLatitude">原点纬度（度）</param>
        /// <param name="standardParallelOne">标准纬线1（度）</param>
        /// <param name="standardParallelTwo">标准纬线2（度）</param>
        /// <param name="falseEasting">东伪偏移（米）</param>
        /// <param name="falseNorthing">北伪偏移（米）</param>
        /// <param name="linearUnit">线性单位</param>
        public ProjLambert(string projCSName, Ellipsoid ellipsoid, double centralMeridian,
            double originLatitude, double standardParallelOne, double standardParallelTwo,
            double falseEasting = 0, double falseNorthing = 0,
            LinearUnitConstant linearUnit = LinearUnitConstant.Meter)
            : base(projCSName, ellipsoid, ProjectionTypeConstant.Lambert)
        {
            CentralMeridian = centralMeridian;
            OriginLatitude = originLatitude;
            StandardParallelOne = standardParallelOne;
            StandardParallelTwo = standardParallelTwo;
            FalseEasting = falseEasting;
            FalseNorthing = falseNorthing;
            ScaleFactor = 1;
            LinearUnit = linearUnit;
            //预计算圆锥常数n、系数F与原点纬度处的投影半径ρ0
            double sE2 = GetE2();
            double sE = Math.Sqrt(sE2);
            double sM1 = GetM(DegToRad(standardParallelOne), sE2);
            double sM2 = GetM(DegToRad(standardParallelTwo), sE2);
            double sT1 = GetT(DegToRad(standardParallelOne), sE);
            double sT2 = GetT(DegToRad(standardParallelTwo), sE);
            _N = (Math.Log(sM1) - Math.Log(sM2)) / (Math.Log(sT1) - Math.Log(sT2));
            _F = sM1 / (_N * Math.Pow(sT1, _N));
            _Rho0 = ConeRadius(DegToRad(originLatitude), sE);
        }

        #endregion

        #region 投影正反算

        /// <summary>
        /// 将经纬度坐标转换为Lambert平面直角坐标
        /// </summary>
        public override Coordinate TransferToProjCo(Coordinate lngLat)
        {
            double sE = Math.Sqrt(GetE2());
            double sTheta = _N * DegToRad(lngLat.X - CentralMeridian);
            double sRho = ConeRadius(DegToRad(lngLat.Y), sE);
            double sX = FalseEasting + sRho * Math.Sin(sTheta);
            double sY = FalseNorthing + _Rho0 - sRho * Math.Cos(sTheta);
            return new Coordinate(sX, sY);
        }

        /// <summary>
        /// 将Lambert平面直角坐标转换为经纬度坐标
        /// </summary>
        public override Coordinate TransferToLngLat(Coordinate projCo)
        {
            double sE = Math.Sqrt(GetE2());
            double sDX = projCo.X - FalseEasting;
            double sDY = FalseNorthing + _Rho0 - projCo.Y;
            double sTheta;
            double sRho;
            if (_N >= 0)
            {
                sTheta = Math.Atan2(sDX, sDY);
                sRho = Math.Sqrt(sDX * sDX + sDY * sDY);
            }
            else
            {
                sTheta = Math.Atan2(-sDX, -sDY);
                sRho = -Math.Sqrt(sDX * sDX + sDY * sDY);
            }
            //由ρ = a·F·t^n反求 t = (ρ/(a·F))^(1/n)
            double sT = Math.Pow(sRho / (SemiMajor * _F), 1 / _N);
            //迭代求解纬度
            double sPhi = Math.PI / 2 - 2 * Math.Atan(sT);
            for (Int32 i = 0; i <= 5; i++)
            {
                sPhi = Math.PI / 2 - 2 * Math.Atan(sT * Math.Pow((1 - sE * Math.Sin(sPhi)) / (1 + sE * Math.Sin(sPhi)), sE / 2));
            }
            double sLng = CentralMeridian + RadToDeg(sTheta / _N);
            //将经度规范到[-180,180]
            while (sLng > 180)
                sLng -= 360;
            while (sLng < -180)
                sLng += 360;
            return new Coordinate(sLng, RadToDeg(sPhi));
        }

        #endregion

        #region 私有函数

        //m(φ) = cosφ / (1 - e²sin²φ)^0.5
        private double GetM(double phi, double e2)
        {
            return Math.Cos(phi) / Math.Sqrt(1 - e2 * Math.Sin(phi) * Math.Sin(phi));
        }

        //t(φ) = tan(π/4 - φ/2) / ((1 - e·sinφ)/(1 + e·sinφ))^(e/2)
        private double GetT(double phi, double e)
        {
            return Math.Tan(Math.PI / 4 - phi / 2)
                / Math.Pow((1 - e * Math.Sin(phi)) / (1 + e * Math.Sin(phi)), e / 2);
        }

        //圆锥投影半径 ρ = a·F·t(φ)^n
        private double ConeRadius(double phi, double e)
        {
            return SemiMajor * _F * Math.Pow(GetT(phi, e), _N);
        }

        #endregion
    }
}
