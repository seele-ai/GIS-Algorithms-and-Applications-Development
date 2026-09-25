namespace GIS
{
    /// <summary>
    /// 渲染类型常数
    /// </summary>
    public enum RendererTypeConstant
    {
        /// <summary>简单渲染（全图层同一符号）</summary>
        Simple,

        /// <summary>唯一值渲染</summary>
        UniqueValue,

        /// <summary>分级渲染</summary>
        ClassBreaks
    }

    /// <summary>
    /// 渲染器抽象基类。由图层渲染模块（模块3）在 Symbol 体系上扩展，
    /// 用于根据要素属性确定其绘制符号。
    /// </summary>
    public abstract class Renderer
    {
        private bool _HasBindingError;
        private string _BindingErrorField = "";

        /// <summary>获取渲染类型</summary>
        public abstract RendererTypeConstant RendererType { get; }

        /// <summary>
        /// 从渲染符号文件读入后，若绑定字段在目标图层中不存在则为 true。
        /// 此时所有符号被替换为“绑定属性错误”符号（不可见，仅在图例中显示），
        /// 直到在渲染设置中重新绑定字段为止。
        /// </summary>
        public bool HasBindingError
        {
            get { return _HasBindingError; }
        }

        /// <summary>发生绑定错误时缺失的字段名。</summary>
        public string BindingErrorField
        {
            get { return _BindingErrorField; }
        }

        /// <summary>获取绑定字段名（简单渲染无绑定字段，返回空串）。</summary>
        public virtual string BoundField
        {
            get { return ""; }
        }

        /// <summary>获取在图例中显示的“绑定属性错误”文本。</summary>
        public const string BindingErrorText = "绑定属性错误";

        /// <summary>
        /// 标记绑定字段错误：把渲染器内所有符号替换为专用的“红色感叹号”符号
        /// （Visible=false，因此不会绘制到地图上，只显示在图例中）。
        /// </summary>
        public void SetBindingError(string field)
        {
            _HasBindingError = true;
            _BindingErrorField = field ?? "";
            ReplaceAllSymbols(BindingErrorSymbol());
        }

        /// <summary>清除绑定错误状态（重新绑定字段后调用）。</summary>
        public void ClearBindingError()
        {
            _HasBindingError = false;
            _BindingErrorField = "";
        }

        protected void CopyBindingErrorTo(Renderer target)
        {
            target._HasBindingError = _HasBindingError;
            target._BindingErrorField = _BindingErrorField;
        }

        /// <summary>把渲染器内所有符号替换为指定符号（默认符号除外）。</summary>
        protected virtual void ReplaceAllSymbols(Symbol symbol)
        {
        }

        /// <summary>专用的“绑定属性错误”符号：红色感叹号，且不可见（不绘制到地图）。</summary>
        public static SimpleMarkerSymbol BindingErrorSymbol()
        {
            return new SimpleMarkerSymbol
            {
                Style = SimpleMarkerSymbolStyleConstant.Exclamation,
                Color = System.Drawing.Color.FromArgb(200, 30, 30),
                OutlineColor = System.Drawing.Color.FromArgb(150, 0, 0),
                OutlineWidth = 0.25,
                Size = 4.5,
                Visible = false,
                Label = BindingErrorText
            };
        }

        private Symbol _EmptyBindingErrorSymbol;

        /// <summary>
        /// 符号列表为空、但已处于“绑定属性错误”状态时使用的不可见符号。
        /// 此时若返回 null，绘制层会回落到图层基础符号，把要素“误画”出来，因此必须给一个不可见符号。
        /// </summary>
        protected Symbol EmptyBindingErrorSymbol()
        {
            if (_EmptyBindingErrorSymbol == null) _EmptyBindingErrorSymbol = BindingErrorSymbol();
            return _EmptyBindingErrorSymbol;
        }

        /// <summary>
        /// 获取指定要素应使用的符号（无法匹配时返回默认符号）
        /// </summary>
        public abstract Symbol GetSymbolFor(Feature feature);

        /// <summary>
        /// 克隆渲染器（深复制）
        /// </summary>
        public abstract Renderer Clone();
    }
}

