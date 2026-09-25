using System;
using System.Collections.Generic;

namespace GIS
{
    /// <summary>
    /// 唯一值渲染器：按绑定字段的唯一值分配符号。
    /// </summary>
    public class UniqueValueRenderer : Renderer
    {
        private string _Field = "";
        private string _HeadTitle = "";
        private bool _ShowHead = true;
        private List<string> _Values = new List<string>();
        private List<Symbol> _Symbols = new List<Symbol>();
        private Symbol _DefaultSymbol;
        private bool _ShowDefaultSymbol = true;

        public override RendererTypeConstant RendererType
        {
            get { return RendererTypeConstant.UniqueValue; }
        }

        /// <summary>获取或设置绑定字段名</summary>
        /// <summary>
        /// 获取或设置绑定的属性字段名。图例标题（HeadTitle）默认跟随绑定字段：
        /// 标题为空（从未设置）或仍等于原字段名（自动生成的标题）时会一起更新，
        /// 只有显式改过标题才不再跟随。否则读取已有渲染符号文件后重新绑定字段时，
        /// 图层面板里显示的字段会一直停在旧字段名上（看起来“绑定字段改不了”）。
        /// </summary>
        public string Field
        {
            get { return _Field; }
            set
            {
                if (string.IsNullOrEmpty(_HeadTitle) || _HeadTitle == _Field) _HeadTitle = value;
                _Field = value;
            }
        }

        /// <summary>获取或设置图例标题</summary>
        public string HeadTitle
        {
            get { return _HeadTitle; }
            set { _HeadTitle = value; }
        }

        /// <summary>是否在图例中显示标题</summary>
        public bool ShowHead
        {
            get { return _ShowHead; }
            set { _ShowHead = value; }
        }

        /// <summary>唯一值数目</summary>
        public int ValueCount
        {
            get { return _Values.Count; }
        }

        /// <summary>获取或设置默认符号</summary>
        public Symbol DefaultSymbol
        {
            get { return _DefaultSymbol; }
            set { _DefaultSymbol = value; }
        }

        /// <summary>是否在图例中显示默认符号</summary>
        public bool ShowDefaultSymbol
        {
            get { return _ShowDefaultSymbol; }
            set { _ShowDefaultSymbol = value; }
        }

        public string GetValue(int index) { return _Values[index]; }
        public void SetValue(int index, string value) { _Values[index] = value; }
        public Symbol GetSymbol(int index) { return _Symbols[index]; }
        public void SetSymbol(int index, Symbol symbol) { _Symbols[index] = symbol; }

        public void AddValue(string value, Symbol symbol)
        {
            _Values.Add(value);
            _Symbols.Add(symbol);
        }

        public void RemoveValueAt(int index)
        {
            _Values.RemoveAt(index);
            _Symbols.RemoveAt(index);
        }

        public void ClearValues()
        {
            _Values.Clear();
            _Symbols.Clear();
        }

        /// <summary>按唯一值查找符号，找不到返回默认符号</summary>
        public Symbol FindSymbol(string value)
        {
            for (int i = 0; i < _Values.Count; i++)
                if (_Values[i] == value)
                    return _Symbols[i];
            return _DefaultSymbol;
        }

        public override Symbol GetSymbolFor(Feature feature)
        {
            // 未配置唯一值时回退到图层基础符号；但若正处于“绑定属性错误”状态，必须返回不可见符号，
            // 否则要素会被回落到图层基础符号而“误画”出来。
            if (_Values.Count == 0) return HasBindingError ? EmptyBindingErrorSymbol() : null;
            if (feature == null || feature.Attributes == null) return ErrorOrDefault();
            object obj = feature.Attributes.GetItem(_Field);
            if (obj == null) return ErrorOrDefault();
            return FindSymbol(obj.ToString()) ?? ErrorOrDefault();
        }

        // 绑定字段错误时返回不可见的错误符号，保证要素不会被误画成图层基础符号
        private Symbol ErrorOrDefault()
        {
            if (HasBindingError && _Symbols.Count > 0) return _Symbols[0];
            return _DefaultSymbol;
        }

        /// <summary>绑定的属性字段名</summary>
        public override string BoundField
        {
            get { return _Field; }
        }

        /// <summary>把各级符号替换为指定符号（用于绑定字段错误提示）。</summary>
        protected override void ReplaceAllSymbols(Symbol symbol)
        {
            for (int i = 0; i < _Symbols.Count; i++)
                _Symbols[i] = symbol == null ? null : symbol.Clone();
        }

        public override Renderer Clone()
        {
            UniqueValueRenderer r = new UniqueValueRenderer();
            r._Field = _Field;
            r._HeadTitle = _HeadTitle;
            r._ShowHead = _ShowHead;
            r._ShowDefaultSymbol = _ShowDefaultSymbol;
            for (int i = 0; i < _Values.Count; i++)
                r.AddValue(_Values[i], _Symbols[i] == null ? null : _Symbols[i].Clone());
            if (_DefaultSymbol != null) r._DefaultSymbol = _DefaultSymbol.Clone();
            CopyBindingErrorTo(r);
            return r;
        }
    }
}
