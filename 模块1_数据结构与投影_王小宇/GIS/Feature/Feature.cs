namespace GIS
{
    /// <summary>
    /// 要素 = 几何对象 + 属性集合
    /// </summary>
    public class Feature
    {
        private Geometry _Geometry;                         //几何对象
        private Attributes _Attributes;                     //属性集合

        #region 构造函数

        /// <summary>
        /// 按几何对象与字段模式构造要素（属性初始化为null）
        /// </summary>
        public Feature(Geometry geometry, Fields fields)
        {
            _Geometry = geometry;
            _Attributes = new Attributes(fields);
        }

        /// <summary>
        /// 按几何对象与属性集合构造要素
        /// </summary>
        public Feature(Geometry geometry, Attributes attributes)
        {
            _Geometry = geometry;
            _Attributes = attributes;
        }

        #endregion

        #region 属性

        /// <summary>获取或设置几何对象</summary>
        public Geometry Geometry
        {
            get { return _Geometry; }
            set { _Geometry = value; }
        }

        /// <summary>获取或设置属性集合</summary>
        public Attributes Attributes
        {
            get { return _Attributes; }
            set { _Attributes = value; }
        }

        #endregion

        #region 方法

        /// <summary>
        /// 获取要素的外接矩形
        /// </summary>
        public Envelope GetEnvelope()
        {
            if (_Geometry == null)
                return new Envelope();
            return _Geometry.GetEnvelope();
        }

        /// <summary>
        /// 克隆要素（几何对象与属性均深复制；属性值为引用类型时仍共享引用）
        /// </summary>
        public Feature Clone()
        {
            Feature sFeature = new Feature(_Geometry == null ? null : _Geometry.Clone(), (Attributes)null);
            if (_Attributes != null)
            {
                Fields sFields = _Attributes.Fields;
                if (sFields != null)
                {
                    sFeature._Attributes = new Attributes(sFields, _Attributes.ToArray());
                }
            }
            return sFeature;
        }

        #endregion
    }
}
