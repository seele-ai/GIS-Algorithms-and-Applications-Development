namespace GIS
{
    /// <summary>
    /// 图层 = 要素类 + 符号 + 可见性。
    /// 渲染器（Renderer）与注记由图层渲染模块在Symbol体系上扩展。
    /// </summary>
    public class Layer
    {
        private string _Name = "";                  //图层名称
        private FeatureClass _FeatureClass;         //图层的要素类
        private bool _Visible = true;               //图层是否可见
        private Symbol _Symbol;                     //图层的默认符号

        #region 构造函数

        /// <summary>
        /// 按要素类构造图层，图层名取要素类名称
        /// </summary>
        public Layer(FeatureClass featureClass)
        {
            _FeatureClass = featureClass;
            _Name = featureClass.Name;
        }

        /// <summary>
        /// 按名称与要素类构造图层
        /// </summary>
        public Layer(string name, FeatureClass featureClass)
        {
            _Name = name;
            _FeatureClass = featureClass;
        }

        #endregion

        #region 属性

        /// <summary>获取或设置图层名称</summary>
        public string Name
        {
            get { return _Name; }
            set { _Name = value; }
        }

        /// <summary>获取图层的要素类</summary>
        public FeatureClass FeatureClass
        {
            get { return _FeatureClass; }
        }

        /// <summary>获取或设置图层是否可见</summary>
        public bool Visible
        {
            get { return _Visible; }
            set { _Visible = value; }
        }

        /// <summary>获取或设置图层的默认符号</summary>
        public Symbol Symbol
        {
            get { return _Symbol; }
            set { _Symbol = value; }
        }

        #endregion

        #region 方法

        /// <summary>
        /// 获取图层的外接矩形
        /// </summary>
        public Envelope GetEnvelope()
        {
            return _FeatureClass.GetEnvelope();
        }

        #endregion
    }
}
