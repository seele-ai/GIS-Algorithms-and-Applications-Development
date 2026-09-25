namespace GIS
{
    /// <summary>
    /// 注记渲染器：为图层配置一个绑定字段的注记。
    /// </summary>
    public class LabelRenderer
    {
        private bool _LabelFeatures = false;
        private TextSymbol _TextSymbol = new TextSymbol();
        private string _Field = "";
        private double _RotateAngle = 0;

        /// <summary>是否为图层启用注记</summary>
        public bool LabelFeatures
        {
            get { return _LabelFeatures; }
            set { _LabelFeatures = value; }
        }

        /// <summary>获取或设置文本符号</summary>
        public TextSymbol TextSymbol
        {
            get { return _TextSymbol; }
            set { _TextSymbol = value; }
        }

        /// <summary>获取或设置绑定字段</summary>
        public string Field
        {
            get { return _Field; }
            set { _Field = value; }
        }

        /// <summary>获取或设置旋转角度</summary>
        public double RotateAngle
        {
            get { return _RotateAngle; }
            set { _RotateAngle = value; }
        }

        public LabelRenderer Clone()
        {
            LabelRenderer r = new LabelRenderer();
            r._LabelFeatures = _LabelFeatures;
            r._TextSymbol = _TextSymbol.Clone();
            r._Field = _Field;
            r._RotateAngle = _RotateAngle;
            return r;
        }
    }
}
