using System;
using System.Drawing;

namespace GIS
{
    /// <summary>
    /// 点符号形状常数。模块3（图层渲染与编辑）将“实心/空心”统一为“填充色+边框”：
    /// 填充色透明即为空心，边框透明即为实心，两者同时设置即为带边框的实心符号。
    /// </summary>
    public enum SimpleMarkerSymbolStyleConstant
    {
        /// <summary>圆形</summary>
        Circle,

        /// <summary>正方形</summary>
        Square,

        /// <summary>三角形</summary>
        Triangle,

        /// <summary>十字</summary>
        Cross,

        /// <summary>感叹号（用于“绑定属性错误”提示符号，不在图上绘制）</summary>
        Exclamation
    }

    /// <summary>
    /// 简单点符号。Color 为填充色，OutlineColor/OutlineWidth 为边框颜色与宽度（毫米）。
    /// </summary>
    public class SimpleMarkerSymbol : Symbol
    {
        private SimpleMarkerSymbolStyleConstant _Style = SimpleMarkerSymbolStyleConstant.Circle;
        private Color _Color = Color.LightPink;             //填充颜色
        private Color _OutlineColor = Color.Transparent;    //边框颜色
        private double _OutlineWidth = 0.3;                 //边框宽度，单位毫米
        private double _Size = 3;                           //尺寸，单位毫米

        #region 构造函数

        public SimpleMarkerSymbol()
        {
            CreateRandomColor();
        }

        public SimpleMarkerSymbol(string label)
        {
            Label = label;
            CreateRandomColor();
        }

        #endregion

        #region 属性

        /// <summary>获取符号类型</summary>
        public override SymbolTypeConstant SymbolType
        {
            get { return SymbolTypeConstant.SimpleMarkerSymbol; }
        }

        /// <summary>获取或设置形状</summary>
        public SimpleMarkerSymbolStyleConstant Style
        {
            get { return _Style; }
            set { _Style = value; }
        }

        /// <summary>获取或设置填充颜色</summary>
        public Color Color
        {
            get { return _Color; }
            set { _Color = value; }
        }

        /// <summary>获取或设置边框颜色（Transparent 表示无边框）</summary>
        public Color OutlineColor
        {
            get { return _OutlineColor; }
            set { _OutlineColor = value; }
        }

        /// <summary>获取或设置边框宽度（单位毫米）</summary>
        public double OutlineWidth
        {
            get { return _OutlineWidth; }
            set { _OutlineWidth = value; }
        }

        /// <summary>获取或设置尺寸（单位毫米）</summary>
        public double Size
        {
            get { return _Size; }
            set { _Size = value; }
        }

        #endregion

        #region 方法

        /// <summary>
        /// 克隆
        /// </summary>
        public override Symbol Clone()
        {
            SimpleMarkerSymbol sSymbol = new SimpleMarkerSymbol();
            sSymbol.Label = Label;
            sSymbol.Visible = Visible;
            sSymbol._Style = _Style;
            sSymbol._Color = _Color;
            sSymbol._OutlineColor = _OutlineColor;
            sSymbol._OutlineWidth = _OutlineWidth;
            sSymbol._Size = _Size;
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
