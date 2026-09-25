using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace GIS
{
    /// <summary>
    /// 分级渲染器：按绑定字段的数值落入的分级区间分配符号。
    /// BreakValues 为分割值（升序），符号数 = 分割值数；小于第一个分割值用第0个符号，依此类推。
    /// </summary>
    public class ClassBreaksRenderer : Renderer
    {
        private string _Field = "";
        private string _HeadTitle = "";
        private bool _ShowHead = true;
        private List<double> _BreakValues = new List<double>();
        private List<Symbol> _Symbols = new List<Symbol>();
        private Symbol _DefaultSymbol;
        private bool _ShowDefaultSymbol = true;

        public override RendererTypeConstant RendererType
        {
            get { return RendererTypeConstant.ClassBreaks; }
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

        /// <summary>分割值数目</summary>
        public int BreakCount
        {
            get { return _BreakValues.Count; }
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

        public double GetBreakValue(int index) { return _BreakValues[index]; }
        public void SetBreakValue(int index, double value) { _BreakValues[index] = value; }
        public Symbol GetSymbol(int index) { return _Symbols[index]; }
        public void SetSymbol(int index, Symbol symbol) { _Symbols[index] = symbol; }

        public void AddBreakValue(double value, Symbol symbol)
        {
            _BreakValues.Add(value);
            _Symbols.Add(symbol);
        }

        public void ClearBreakValues()
        {
            _BreakValues.Clear();
            _Symbols.Clear();
        }

        /// <summary>按数值查找符号，找不到返回默认符号</summary>
        public Symbol FindSymbol(double value)
        {
            int n = _BreakValues.Count;
            if (n == 0) return _DefaultSymbol;
            if (value < _BreakValues[0]) return _Symbols[0];
            for (int i = 1; i < n; i++)
                if (value < _BreakValues[i])
                    return _Symbols[i];
            return _Symbols[n - 1];   // 等于最大分割值的要素归入最后一类
        }

        public override Symbol GetSymbolFor(Feature feature)
        {
            // 未配置分级时回退到图层基础符号；但若正处于“绑定属性错误”状态，必须返回不可见符号，
            // 否则要素会被回落到图层基础符号而“误画”出来（修复：符号为 error 时不再画出要素）。
            if (_BreakValues.Count == 0) return HasBindingError ? EmptyBindingErrorSymbol() : null;
            if (feature == null || feature.Attributes == null) return ErrorOrDefault();
            object obj = feature.Attributes.GetItem(_Field);
            if (obj == null) return ErrorOrDefault();
            double value;
            try { value = Convert.ToDouble(obj); }
            catch { return ErrorOrDefault(); }
            return FindSymbol(value);
        }

        // 绑定字段错误时返回不可见的错误符号，保证要素不会被误画成图层基础符号
        private Symbol ErrorOrDefault()
        {
            if (HasBindingError && _Symbols.Count > 0) return _Symbols[0];
            return _DefaultSymbol;
        }

        /// <summary>为所有符号生成渐变色（按 HSV 插值）</summary>
        public void RampColor(Color startColor, Color endColor)
        {
            int n = _BreakValues.Count;
            if (n <= 0) return;
            Color[] colors = new Color[n];
            if (n == 1) colors[0] = startColor;
            else
            {
                double[] h1 = RGBToHSV(startColor.R, startColor.G, startColor.B);
                double[] h2 = RGBToHSV(endColor.R, endColor.G, endColor.B);
                colors[0] = startColor;
                colors[n - 1] = endColor;
                for (int i = 1; i <= n - 2; i++)
                {
                    double h = h1[0] + i * (h2[0] - h1[0]) / n;
                    double s = h1[1] + i * (h2[1] - h1[1]) / n;
                    double v = h1[2] + i * (h2[2] - h1[2]) / n;
                    byte[] rgb = HSVToRGB(h, s, v);
                    int a = startColor.A + i * (endColor.A - startColor.A) / n;
                    colors[i] = Color.FromArgb(a, rgb[0], rgb[1], rgb[2]);
                }
            }
            for (int i = 0; i < n; i++)
                SetSymbolColor(_Symbols[i], colors[i]);
        }

        /// <summary>按色带为所有分级符号配色（在色带上等间隔取色）。</summary>
        public void ApplyColorRamp(ColorRamp ramp)
        {
            if (ramp == null || _BreakValues.Count <= 0) return;
            Color[] colors = ramp.Sample(_BreakValues.Count);
            for (int i = 0; i < _BreakValues.Count && i < _Symbols.Count; i++)
                SetSymbolColor(_Symbols[i], colors[i]);
        }

        /// <summary>为所有符号生成渐变尺寸（仅点/线符号）</summary>
        public void RampSize(double startSize, double endSize)
        {
            int n = _BreakValues.Count;
            if (n <= 0) return;
            double[] sizes = new double[n];
            if (n == 1) sizes[0] = startSize;
            else
            {
                sizes[0] = startSize;
                sizes[n - 1] = endSize;
                for (int i = 1; i <= n - 2; i++)
                    sizes[i] = Math.Round(startSize + i * (endSize - startSize) / n, 1);
            }
            for (int i = 0; i < n; i++)
            {
                Symbol s = _Symbols[i];
                if (s is SimpleMarkerSymbol) ((SimpleMarkerSymbol)s).Size = sizes[i];
                else if (s is SimpleLineSymbol) ((SimpleLineSymbol)s).Size = sizes[i];
            }
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
            ClassBreaksRenderer r = new ClassBreaksRenderer();
            r._Field = _Field;
            r._HeadTitle = _HeadTitle;
            r._ShowHead = _ShowHead;
            r._ShowDefaultSymbol = _ShowDefaultSymbol;
            for (int i = 0; i < _BreakValues.Count; i++)
                r.AddBreakValue(_BreakValues[i], _Symbols[i] == null ? null : _Symbols[i].Clone());
            if (_DefaultSymbol != null) r._DefaultSymbol = _DefaultSymbol.Clone();
            CopyBindingErrorTo(r);
            return r;
        }

        private static void SetSymbolColor(Symbol symbol, Color color)
        {
            if (symbol == null) return;
            if (symbol is SimpleMarkerSymbol) ((SimpleMarkerSymbol)symbol).Color = color;
            else if (symbol is SimpleLineSymbol) ((SimpleLineSymbol)symbol).Color = color;
            else if (symbol is SimpleFillSymbol) ((SimpleFillSymbol)symbol).Color = color;
        }

        private double[] RGBToHSV(double R, double G, double B)
        {
            double max = new[] { R, G, B }.Max();
            double min = new[] { R, G, B }.Min();
            double V = max;
            double H, S;
            if (max != min)
            {
                S = (max - min) / max;
                if (R == max) H = (G - B) / (max - min) * 60;
                else if (G == max) H = 120 + (B - R) / (max - min) * 60;
                else H = 240 + (R - G) / (max - min) * 60;
                if (H < 0) H += 360;
            }
            else { S = 0; H = -1; }
            return new[] { H, S, V };
        }

        private byte[] HSVToRGB(double H, double S, double V)
        {
            double R, G, B;
            if (S == 0) { R = V; G = V; B = V; }
            else
            {
                H = H / 60;
                int i = (int)H;
                double f = H - i;
                double aa = V * (1 - S);
                double bb = V * (1 - S * f);
                double cc = V * (1 - S * (1 - f));
                switch (i)
                {
                    case 0: R = V; G = cc; B = aa; break;
                    case 1: R = bb; G = V; B = aa; break;
                    case 2: R = aa; G = V; B = cc; break;
                    case 3: R = aa; G = bb; B = V; break;
                    case 4: R = cc; G = aa; B = V; break;
                    default: R = V; G = aa; B = bb; break;
                }
            }
            return new[] { (byte)Math.Min(255, R), (byte)Math.Min(255, G), (byte)Math.Min(255, B) };
        }
    }
}
