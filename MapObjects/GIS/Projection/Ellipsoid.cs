namespace GIS
{
    /// <summary>
    /// 地球椭球体参数（长半轴、扁率倒数等）
    /// </summary>
    public class Ellipsoid
    {
        /// <summary>椭球体名称</summary>
        public string SpheroidName;

        /// <summary>大地基准面名称</summary>
        public string DatumName;

        /// <summary>地理坐标系统名称</summary>
        public string GeoCSName;

        /// <summary>长半轴（米）</summary>
        public double SemiMajor;

        /// <summary>扁率倒数</summary>
        public double InverseFlattening;

        #region 构造函数

        public Ellipsoid(string spheroidName, string datumName, string geoCSName,
            double semiMajor, double inverseFlattening)
        {
            SpheroidName = spheroidName;
            DatumName = datumName;
            GeoCSName = geoCSName;
            SemiMajor = semiMajor;
            InverseFlattening = inverseFlattening;
        }

        #endregion

        #region 方法

        /// <summary>
        /// 获取短半轴（米）
        /// </summary>
        public double GetSemiMinor()
        {
            return SemiMajor * (1 - 1 / InverseFlattening);
        }

        /// <summary>
        /// 获取第一偏心率的平方
        /// </summary>
        public double GetE2()
        {
            double b = GetSemiMinor();
            return (SemiMajor * SemiMajor - b * b) / (SemiMajor * SemiMajor);
        }

        #endregion
    }

    /// <summary>
    /// 常用椭球体参数常数（长半轴与扁率倒数为公开常用值）
    /// </summary>
    public static class Ellipsoids
    {
        /// <summary>WGS84椭球（GPS所用）</summary>
        public static readonly Ellipsoid WGS84 = new Ellipsoid("WGS_1984", "D_WGS_1984", "GCS_WGS_1984",
            6378137.0, 298.257223563);

        /// <summary>CGCS2000国家大地坐标系椭球</summary>
        public static readonly Ellipsoid CGCS2000 = new Ellipsoid("CGCS2000", "D_China_2000", "GCS_China_2000",
            6378137.0, 298.257222101);

        /// <summary>IAG-75椭球（1980西安坐标系所用）</summary>
        public static readonly Ellipsoid Xian80 = new Ellipsoid("IAG_75", "D_Xian_1980", "GCS_Xian_1980",
            6378140.0, 298.257);

        /// <summary>克拉索夫斯基椭球（1954北京坐标系所用）</summary>
        public static readonly Ellipsoid Beijing54 = new Ellipsoid("Krassowsky_1940", "D_Beijing_1954", "GCS_Beijing_1954",
            6378245.0, 298.3);
    }
}
