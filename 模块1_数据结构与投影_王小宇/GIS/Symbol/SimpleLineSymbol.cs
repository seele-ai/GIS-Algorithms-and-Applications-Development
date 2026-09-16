using System;

namespace GIS
{
    /// <summary>
    /// 线符号线型常数
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
    /// 简单线符号
    /// </summary>
    public class SimpleLineSymbol : Symbol
    {
        private SimpleLineSymbolStyleConstant _Style = SimpleLineSymbolStyleConstant.Solid;     //线型
        private System.Drawing.Color _Color = System.Drawing.Color.LightPink;   //颜色
        private double _Size = 0.35;    //线宽，单位毫米

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
        public System.Drawing.Color Color
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

        #endregion

        #region 方法

        /// <summary>
        /// 克隆
        /// </summary>
        public override Symbol Clone()
        {
            SimpleLineSymbol sSymbol = new SimpleLineSymbol();
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
