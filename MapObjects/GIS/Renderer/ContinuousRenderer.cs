using System;
using System.Collections.Generic;
using System.Drawing;

namespace GIS
{
    /// <summary>
    /// 连续渲染器（拉伸渲染）：把绑定字段的数值线性拉伸到 0~1，用色带在该位置取色，
    /// 再把颜色套到“默认符号”（模板符号）上——几何形状、边框、尺寸都来自模板符号，
    /// 只有颜色随数值变化。点 / 线 / 面三种几何共用这一个渲染器。
    ///
    /// 值域 [Minimum, Maximum] 默认取字段的最小/最大值（见 <see cref="UpdateRange(FeatureClass)"/>），
    /// 全图层数值相同时按 ±1 构造一个退化区间，保证还能取色。
    /// </summary>
    public class ContinuousRenderer : Renderer
    {
        private string _Field = "";
        private string _HeadTitle = "";
        private bool _ShowHead = true;
        private ColorRamp _Ramp;
        private Symbol _Symbol;
        private double _Minimum;
        private double _Maximum;
        private bool _HasRange;

        // 颜色缓存：连续渲染每个要素的颜色都不同，逐要素 Clone 符号太慢，
        // 这里按拉伸后的 0~127 共 128 级缓存符号副本（同一级重复使用同一个符号）。
        private const int ColorLevels = 128;
        private readonly Dictionary<int, Symbol> _cache = new Dictionary<int, Symbol>();

        public override RendererTypeConstant RendererType
        {
            get { return RendererTypeConstant.Continuous; }
        }

        /// <summary>
        /// 绑定的属性字段名。图例标题默认跟随绑定字段（与唯一值/分级一致）。
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

        /// <summary>图例标题。</summary>
        public string HeadTitle
        {
            get { return _HeadTitle; }
            set { _HeadTitle = value; }
        }

        /// <summary>是否在图例中显示标题。</summary>
        public bool ShowHead
        {
            get { return _ShowHead; }
            set { _ShowHead = value; }
        }

        /// <summary>色带：0 对应 Minimum，1 对应 Maximum。</summary>
        public ColorRamp Ramp
        {
            get { return _Ramp; }
            set { _Ramp = value; _cache.Clear(); }
        }

        /// <summary>模板符号（默认符号）：只有颜色会被色带替换。</summary>
        public Symbol Symbol
        {
            get { return _Symbol; }
            set { _Symbol = value; _cache.Clear(); }
        }

        /// <summary>拉伸区间下界。</summary>
        public double Minimum
        {
            get { return _Minimum; }
        }

        /// <summary>拉伸区间上界。</summary>
        public double Maximum
        {
            get { return _Maximum; }
        }

        /// <summary>是否已经确定拉伸区间（否则所有要素都用色带中点色）。</summary>
        public bool HasRange
        {
            get { return _HasRange; }
        }

        /// <summary>设置拉伸区间（最大值必须大于最小值）。全同值时围绕该值撑开 ±跨度的一半，使所有要素取色带中间色。</summary>
        public void SetRange(double minimum, double maximum)
        {
            if (double.IsNaN(minimum) || double.IsNaN(maximum)) { _HasRange = false; return; }
            if (maximum <= minimum)
            {
                double span = Math.Max(Math.Abs(minimum) * 0.1, 1.0);
                double center = minimum;
                minimum = center - span / 2;
                maximum = center + span / 2;
            }
            _Minimum = minimum;
            _Maximum = maximum;
            _HasRange = true;
            _cache.Clear();
        }

        /// <summary>按绑定字段的数值自动确定拉伸区间（只看数值型、可解析的值）。</summary>
        public void UpdateRange(FeatureClass featureClass)
        {
            double[] values = ValuesOf(featureClass, _Field);
            if (values.Length == 0) { _HasRange = false; _cache.Clear(); return; }
            double min = values[0], max = values[0];
            for (int i = 1; i < values.Length; i++)
            {
                if (values[i] < min) min = values[i];
                if (values[i] > max) max = values[i];
            }
            SetRange(min, max);
        }

        /// <summary>取某字段的全部数值（无法解析的值为 null 的要素直接跳过）。</summary>
        public static double[] ValuesOf(FeatureClass featureClass, string field)
        {
            if (featureClass == null || featureClass.Features == null || string.IsNullOrEmpty(field))
                return new double[0];
            var values = new List<double>();
            foreach (Feature feature in featureClass.Features)
            {
                double value;
                if (TryGetValue(feature, field, out value)) values.Add(value);
            }
            return values.ToArray();
        }

        /// <summary>读取要素某个字段的数值；字段不存在或不是数值时返回 false。</summary>
        public static bool TryGetValue(Feature feature, string field, out double value)
        {
            value = 0;
            if (feature == null || feature.Attributes == null || string.IsNullOrEmpty(field)) return false;
            object obj = feature.Attributes.GetItem(field);
            if (obj == null) return false;
            if (obj is double) { value = (double)obj; return true; }
            if (obj is float) { value = (float)obj; return true; }
            if (obj is decimal) { value = (double)(decimal)obj; return true; }
            if (obj is int || obj is long || obj is short || obj is byte || obj is sbyte) { value = Convert.ToDouble(obj); return true; }
            return double.TryParse(Convert.ToString(obj), out value);
        }

        /// <summary>把数值拉伸到 0~1（超出区间的截断到端点）。</summary>
        public double Normalize(double value)
        {
            if (!_HasRange || _Maximum <= _Minimum) return 0.5;
            double t = (value - _Minimum) / (_Maximum - _Minimum);
            if (t < 0) return 0;
            if (t > 1) return 1;
            return t;
        }

        /// <summary>按拉伸位置（0~1）取符号；没有色带或模板符号时返回 null。</summary>
        public Symbol SymbolAt(double position)
        {
            if (_Ramp == null || _Symbol == null) return null;
            if (position < 0) position = 0;
            else if (position > 1) position = 1;
            int level = (int)Math.Round(position * (ColorLevels - 1));
            Symbol cached;
            if (_cache.TryGetValue(level, out cached)) return cached;
            Symbol tinted = Tint(_Symbol.Clone(), _Ramp.GetColor(level / (double)(ColorLevels - 1)));
            _cache[level] = tinted;
            return tinted;
        }

        /// <summary>按数值取符号（图例与自检用）。</summary>
        public Symbol SymbolForValue(double value)
        {
            return SymbolAt(Normalize(value));
        }

        public override Symbol GetSymbolFor(Feature feature)
        {
            if (_Ramp == null || _Symbol == null) return HasBindingError ? EmptyBindingErrorSymbol() : null;
            double value;
            if (!TryGetValue(feature, _Field, out value)) return _Symbol;
            return SymbolAt(Normalize(value));
        }

        /// <summary>绑定的属性字段名。</summary>
        public override string BoundField
        {
            get { return _Field; }
        }

        /// <summary>把模板符号替换为指定符号（用于绑定字段错误提示）。</summary>
        protected override void ReplaceAllSymbols(Symbol symbol)
        {
            _Symbol = symbol == null ? null : symbol.Clone();
            _cache.Clear();
        }

        public override Renderer Clone()
        {
            ContinuousRenderer r = new ContinuousRenderer();
            r._Field = _Field;
            r._HeadTitle = _HeadTitle;
            r._ShowHead = _ShowHead;
            r._Ramp = _Ramp == null ? null : _Ramp.Clone();
            r._Symbol = _Symbol == null ? null : _Symbol.Clone();
            r._Minimum = _Minimum;
            r._Maximum = _Maximum;
            r._HasRange = _HasRange;
            CopyBindingErrorTo(r);
            return r;
        }

        /// <summary>把颜色套到符号上：点/面的填充色、线的颜色（含自定义虚线各段与偏移线）都改成新颜色。</summary>
        public static Symbol Tint(Symbol symbol, Color color)
        {
            if (symbol == null) return null;
            SimpleMarkerSymbol marker = symbol as SimpleMarkerSymbol;
            if (marker != null) { marker.Color = color; return marker; }
            SimpleFillSymbol fill = symbol as SimpleFillSymbol;
            if (fill != null) { fill.Color = color; return fill; }
            SimpleLineSymbol line = symbol as SimpleLineSymbol;
            if (line != null)
            {
                TintLine(line, color);
                return line;
            }
            return symbol;
        }

        private static void TintLine(SimpleLineSymbol line, Color color)
        {
            line.Color = color;
            foreach (LineDashElement element in line.DashElements)
                if (element != null) element.Color = color;
            foreach (LineOffset offset in line.Offsets)
                if (offset != null && offset.Line != null) TintLine(offset.Line, color);
        }
    }
}
