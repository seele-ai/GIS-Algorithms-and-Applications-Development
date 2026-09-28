namespace GIS
{
    /// <summary>
    /// 简单渲染器：图层内所有要素使用同一符号。
    /// </summary>
    public class SimpleRenderer : Renderer
    {
        private Symbol _Symbol;

        /// <summary>获取渲染类型</summary>
        public override RendererTypeConstant RendererType
        {
            get { return RendererTypeConstant.Simple; }
        }

        /// <summary>获取或设置符号</summary>
        public Symbol Symbol
        {
            get { return _Symbol; }
            set { _Symbol = value; }
        }

        public override Symbol GetSymbolFor(Feature feature)
        {
            return _Symbol;
        }

        public override Renderer Clone()
        {
            SimpleRenderer sRenderer = new SimpleRenderer();
            if (_Symbol != null)
                sRenderer._Symbol = _Symbol.Clone();
            return sRenderer;
        }
    }
}
