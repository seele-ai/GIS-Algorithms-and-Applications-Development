using System;
using System.Collections.Generic;
using System.Drawing;

namespace GIS
{
    /// <summary>
    /// 线符号线型常数（简单线型；自定义虚线由 DashElements 实现）
    /// </summary>
    public enum SimpleLineSymbolStyleConstant
    {
        /// <summary>实线</summary>
        Solid,

        /// <summary>虚线</summary>
        Dash,

        /// <summary>点线</summary>
        Dot
    }

    /// <summary>
    /// 自定义虚线的一个“线段元素”。若干元素按顺序循环排列构成整条线的图案。
    /// 可设置线长、颜色、线宽、边框（casing）、以及在端点绘制垂直于线段的短划线（TickLength）。
    /// 另外每个线段还可以单独启用三类特性（点击选框后才生效，且不影响其他线段的位置）：
    /// 偏移（整体向左/右平移）、延长（两端各自向外延长）、弧线（画成由半椭圆组成的波浪）。
    /// </summary>
    public class LineDashElement
    {
        /// <summary>线段长度（单位毫米）</summary>
        public double Length = 5;

        /// <summary>线段颜色</summary>
        public Color Color = Color.Black;

        /// <summary>线段线宽（单位毫米）</summary>
        public double Width = 0.5;

        /// <summary>边框（casing）颜色，Transparent 表示无边框</summary>
        public Color OutlineColor = Color.Transparent;

        /// <summary>边框宽度（单位毫米）</summary>
        public double OutlineWidth = 0;

        /// <summary>端点垂直短划线的长度（单位毫米），0 表示不绘制</summary>
        public double TickLength = 0;

        /// <summary>端点短划线的颜色（默认与线段颜色一致）</summary>
        public Color TickColor = Color.Transparent;

        /// <summary>是否启用偏移</summary>
        public bool OffsetEnabled = false;

        /// <summary>偏移量（单位毫米，正值向左、负值向右）：启用后本段整体平移，不影响其他段</summary>
        public double Offset = 0;

        /// <summary>是否启用在两端延长</summary>
        public bool ExtendEnabled = false;

        /// <summary>向左（起点方向）延长的长度（单位毫米）</summary>
        public double ExtendLeft = 0;

        /// <summary>向右（终点方向）延长的长度（单位毫米）</summary>
        public double ExtendRight = 0;

        /// <summary>是否启用弧线（用椭圆绘制本段）</summary>
        public bool ArcEnabled = false;

        /// <summary>弧线振幅（单位毫米）：椭圆在垂直方向的半轴</summary>
        public double ArcAmplitude = 1;

        /// <summary>半周期数：本段被等分为该数量的半椭圆，隔段交替方向形成连续波浪</summary>
        public double ArcHalfPeriods = 1;

        public LineDashElement Clone()
        {
            return new LineDashElement
            {
                Length = Length,
                Color = Color,
                Width = Width,
                OutlineColor = OutlineColor,
                OutlineWidth = OutlineWidth,
                TickLength = TickLength,
                TickColor = TickColor,
                OffsetEnabled = OffsetEnabled,
                Offset = Offset,
                ExtendEnabled = ExtendEnabled,
                ExtendLeft = ExtendLeft,
                ExtendRight = ExtendRight,
                ArcEnabled = ArcEnabled,
                ArcAmplitude = ArcAmplitude,
                ArcHalfPeriods = ArcHalfPeriods
            };
        }
    }

    /// <summary>
    /// 线符号的一条“偏移线”。Offset 为该线相对原折线的垂直偏移量（毫米，正值向线左侧），
    /// Line 为该条线的线符号。一个线符号可包含多条偏移线，用于实现国界线等多平行线符号。
    /// </summary>
    public class LineOffset
    {
        /// <summary>垂直偏移量（单位毫米）</summary>
        public double Offset = 0;

        /// <summary>该条线的线符号</summary>
        public SimpleLineSymbol Line;

        public LineOffset()
        {
            Line = new SimpleLineSymbol();
        }

        public LineOffset(double offset, SimpleLineSymbol line)
        {
            Offset = offset;
            Line = line;
        }

        public LineOffset Clone()
        {
            return new LineOffset(Offset, (SimpleLineSymbol)(Line == null ? null : Line.Clone()));
        }
    }

    /// <summary>
    /// 简单线符号。当 DashElements 非空时按自定义虚线图案绘制；
    /// 否则按 Style（Solid/Dash/Dot）+ Color + Size 绘制。
    /// 当 Offsets 非空时，按各条偏移线分别绘制（自身属性作为第一条线的模板）。
    /// </summary>
    public class SimpleLineSymbol : Symbol
    {
        private SimpleLineSymbolStyleConstant _Style = SimpleLineSymbolStyleConstant.Solid;     //线型
        private Color _Color = Color.LightPink;   //颜色
        private double _Size = 0.35;    //线宽，单位毫米
        private List<LineDashElement> _DashElements = new List<LineDashElement>();
        private List<LineOffset> _Offsets = new List<LineOffset>();   //多条偏移线（非空时按偏移线绘制）

        #region 构造函数

        public SimpleLineSymbol()
        {
            CreateRandomColor();
        }

        public SimpleLineSymbol(string label)
        {
            Label = label;
            CreateRandomColor();
        }

        #endregion

        #region 属性

        /// <summary>获取符号类型</summary>
        public override SymbolTypeConstant SymbolType
        {
            get { return SymbolTypeConstant.SimpleLineSymbol; }
        }

        /// <summary>获取或设置线型</summary>
        public SimpleLineSymbolStyleConstant Style
        {
            get { return _Style; }
            set { _Style = value; }
        }

        /// <summary>获取或设置颜色</summary>
        public Color Color
        {
            get { return _Color; }
            set { _Color = value; }
        }

        /// <summary>获取或设置线宽（单位毫米）</summary>
        public double Size
        {
            get { return _Size; }
            set { _Size = value; }
        }

        /// <summary>获取自定义虚线元素集合（非空时按此图案绘制）</summary>
        public List<LineDashElement> DashElements
        {
            get { return _DashElements; }
        }

        /// <summary>是否使用自定义虚线</summary>
        public bool HasCustomDash
        {
            get { return _DashElements.Count > 0; }
        }

        /// <summary>获取多条偏移线集合（非空时按此集合逐条绘制）</summary>
        public List<LineOffset> Offsets
        {
            get { return _Offsets; }
        }

        /// <summary>是否使用多条偏移线</summary>
        public bool HasOffsets
        {
            get { return _Offsets.Count > 0; }
        }

        #endregion

        #region 方法

        /// <summary>
        /// 克隆
        /// </summary>
        public override Symbol Clone()
        {
            SimpleLineSymbol sSymbol = new SimpleLineSymbol();
            sSymbol.Label = Label;
            sSymbol.Visible = Visible;
            sSymbol._Style = _Style;
            sSymbol._Color = _Color;
            sSymbol._Size = _Size;
            foreach (LineDashElement element in _DashElements)
                sSymbol._DashElements.Add(element.Clone());
            foreach (LineOffset offset in _Offsets)
                sSymbol._Offsets.Add(offset.Clone());
            return sSymbol;
        }

        #endregion

        #region 私有函数

        //生成随机颜色
        private void CreateRandomColor()
        {
            _Color = CreateRandomColor(64);
        }

        #endregion
    }
}
