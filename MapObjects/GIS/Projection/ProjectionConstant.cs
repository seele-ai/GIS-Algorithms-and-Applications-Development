namespace GIS
{
    /// <summary>
    /// 投影类型常数
    /// </summary>
    public enum ProjectionTypeConstant
    {
        /// <summary>Lambert等角圆锥投影（双标准纬线）</summary>
        Lambert,

        /// <summary>高斯-克吕格投影（横轴等角切圆柱投影）</summary>
        GaussKruger,

        /// <summary>UTM通用横轴墨卡托投影</summary>
        UTM
    }

    /// <summary>
    /// 线性单位常数
    /// </summary>
    public enum LinearUnitConstant
    {
        /// <summary>米</summary>
        Meter = 1,

        /// <summary>千米</summary>
        Kilometer = 1000
    }

    /// <summary>
    /// 线性单位辅助类
    /// </summary>
    public static class LinearUnitTools
    {
        /// <summary>
        /// 获取线性单位对应的米数因子
        /// </summary>
        public static double GetMetersPerUnit(LinearUnitConstant linearUnit)
        {
            return (double)linearUnit;
        }
    }
}
