using System;

namespace GIS
{
    /// <summary>
    /// 投影坐标系统抽象基类（类型设计参照课程课件第十章"投影坐标与地理坐标转换"）。
    /// 子类实现具体的投影正算（TransferToProjCo：经纬度→投影坐标）
    /// 与反算（TransferToLngLat：投影坐标→经纬度）。
    /// </summary>
    public abstract class ProjectionCS
    {
        //——投影坐标系统与地理坐标系统描述
        /// <summary>投影坐标系统名称</summary>
        public string ProjCSName { get; protected set; }

        /// <summary>地理坐标系统名称</summary>
        public string GeoCSName { get; protected set; }

        /// <summary>大地基准面名称</summary>
        public string DatumName { get; protected set; }

        /// <summary>椭球体名称</summary>
        public string SpheroidName { get; protected set; }

        /// <summary>主经线（一般为格林尼治0度）</summary>
        public double PrimeMeridian { get; protected set; }

        //——椭球体参数
        /// <summary>长半轴（米）</summary>
        public double SemiMajor { get; protected set; }

        /// <summary>短半轴（米）</summary>
        public double SemiMinor { get; protected set; }

        /// <summary>扁率倒数</summary>
        public double InverseFlattening { get; protected set; }

        //——投影参数
        /// <summary>投影类型</summary>
        public ProjectionTypeConstant ProjType { get; protected set; }

        /// <summary>原点纬度（度）</summary>
        public double OriginLatitude { get; protected set; }

        /// <summary>中央经线（度）</summary>
        public double CentralMeridian { get; protected set; }

        /// <summary>东伪偏移（米）</summary>
        public double FalseEasting { get; protected set; }

        /// <summary>北伪偏移（米）</summary>
        public double FalseNorthing { get; protected set; }

        /// <summary>比例因子</summary>
        public double ScaleFactor { get; protected set; }

        /// <summary>线性单位</summary>
        public LinearUnitConstant LinearUnit { get; protected set; }

        #region 构造函数

        protected ProjectionCS(string projCSName, Ellipsoid ellipsoid, ProjectionTypeConstant projType)
        {
            ProjCSName = projCSName;
            GeoCSName = ellipsoid.GeoCSName;
            DatumName = ellipsoid.DatumName;
            SpheroidName = ellipsoid.SpheroidName;
            PrimeMeridian = 0;
            SemiMajor = ellipsoid.SemiMajor;
            SemiMinor = ellipsoid.GetSemiMinor();
            InverseFlattening = ellipsoid.InverseFlattening;
            ProjType = projType;
            OriginLatitude = 0;
            CentralMeridian = 0;
            FalseEasting = 0;
            FalseNorthing = 0;
            ScaleFactor = 1;
            LinearUnit = LinearUnitConstant.Meter;
        }

        #endregion

        #region 抽象方法

        /// <summary>
        /// 将经纬度坐标转换为投影坐标（入参X为经度、Y为纬度，单位度；返回X为东方向、Y为北方向）
        /// </summary>
        public abstract Coordinate TransferToProjCo(Coordinate lngLat);

        /// <summary>
        /// 将投影坐标转换为经纬度坐标（入参X为东方向、Y为北方向；返回X为经度、Y为纬度，单位度）
        /// </summary>
        public abstract Coordinate TransferToLngLat(Coordinate projCo);

        #endregion

        #region 方法

        /// <summary>
        /// 将米为单位的数值转换为本投影线性单位的数值
        /// </summary>
        public double ToUnits(double dataIn)
        {
            return dataIn / LinearUnitTools.GetMetersPerUnit(LinearUnit);
        }

        /// <summary>
        /// 将本投影线性单位的数值转换为米的数值
        /// </summary>
        public double ToMeters(double dataIn)
        {
            return dataIn * LinearUnitTools.GetMetersPerUnit(LinearUnit);
        }

        /// <summary>
        /// 获取第一偏心率的平方
        /// </summary>
        public double GetE2()
        {
            return (SemiMajor * SemiMajor - SemiMinor * SemiMinor) / (SemiMajor * SemiMajor);
        }

        #endregion

        #region 辅助函数

        /// <summary>角度转弧度</summary>
        public static double DegToRad(double deg)
        {
            return deg * Math.PI / 180;
        }

        /// <summary>弧度转角度</summary>
        public static double RadToDeg(double rad)
        {
            return rad * 180 / Math.PI;
        }

        #endregion

        #region Object成员

        public override string ToString()
        {
            return ProjCSName;
        }

        #endregion
    }
}
