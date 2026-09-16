using System;

namespace GIS
{
    /// <summary>
    /// 点符号形状常数
    /// </summary>
    public enum SimpleMarkerSymbolStyleConstant
    {
        /// <summary>实心圆</summary>
        SolidCircle,

        /// <summary>空心圆</summary>
        HollowCircle,

        /// <summary>实心正方形</summary>
        SolidSquare,

        /// <summary>实心三角形</summary>
        SolidTriangle,

        /// <summary>十字</summary>
        Cross
    }

    /// <summary>
    /// 简单点符号
    /// </summary>
    public class SimpleMarkerSymbol : Symbol
    {
        private SimpleMarkerSymbolStyleConstant _Style = SimpleMarkerSymbolStyleConstant.SolidCircle;
        private System.Drawing.Color _Color = System.Drawing.Color.LightPink;    //颜色
        private double _Size = 3;       //尺寸，单位毫米

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

        /// <summary>获取或设置颜色</summary>
        public System.Drawing.Color Color
        {
            get { return _Color; }
            set { _Color = value; }
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
            sSymbol._Style = _Style;
            sSymbol._Color = _Color;
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
