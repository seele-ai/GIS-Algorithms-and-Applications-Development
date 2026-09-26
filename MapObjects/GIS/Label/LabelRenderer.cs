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
        private bool _AvoidOverlap = true;

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

        /// <summary>
        /// 是否避免注记相互遮盖（默认开启）：开启时每条注记先试首选位置，
        /// 若与已放置的注记冲突就换候选位置，全部候选都冲突则跳过不画（宁可少画一条也不叠成一团）。
        /// 关闭时所有注记都按首选位置绘制（可能与相邻注记重叠）。
        /// </summary>
        public bool AvoidOverlap
        {
            get { return _AvoidOverlap; }
            set { _AvoidOverlap = value; }
        }

        public LabelRenderer Clone()
        {
            LabelRenderer r = new LabelRenderer();
            r._LabelFeatures = _LabelFeatures;
            r._TextSymbol = _TextSymbol.Clone();
            r._Field = _Field;
            r._RotateAngle = _RotateAngle;
            r._AvoidOverlap = _AvoidOverlap;
            return r;
        }
    }
}
