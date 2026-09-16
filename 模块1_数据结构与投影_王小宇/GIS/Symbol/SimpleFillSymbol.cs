using System;

namespace GIS
{
    /// <summary>
    /// 简单面符号
    /// </summary>
    public class SimpleFillSymbol : Symbol
    {
        private System.Drawing.Color _Color = System.Drawing.Color.LightPink;   //填充颜色
        private SimpleLineSymbol _Outline;                                      //边界符号

        #region 构造函数

        public SimpleFillSymbol()
        {
            CreateRandomColor();
            InitializeOutline();
        }

        public SimpleFillSymbol(string label)
        {
            Label = label;
            CreateRandomColor();
            InitializeOutline();
        }

        #endregion

        #region 属性

        /// <summary>获取符号类型</summary>
        public override SymbolTypeConstant SymbolType
        {
            get { return SymbolTypeConstant.SimpleFillSymbol; }
        }

        /// <summary>获取或设置填充颜色</summary>
        public System.Drawing.Color Color
        {
            get { return _Color; }
            set { _Color = value; }
        }

        /// <summary>获取或设置边界符号</summary>
        public SimpleLineSymbol Outline
        {
            get { return _Outline; }
            set { _Outline = value; }
        }

        #endregion

        #region 方法

        /// <summary>
        /// 克隆
        /// </summary>
        public override Symbol Clone()
        {
            SimpleFillSymbol sSymbol = new SimpleFillSymbol();
            sSymbol.Label = Label;
            sSymbol._Color = _Color;
            sSymbol.Outline = (SimpleLineSymbol)_Outline.Clone();
            return sSymbol;
        }

        #endregion

        #region 私有函数

        //生成随机颜色（固定通道取252，确保填充色偏深）
        private void CreateRandomColor()
        {
            _Color = CreateRandomColor(252);
        }

        //初始化边界符号
        private void InitializeOutline()
        {
            _Outline = new SimpleLineSymbol();
            _Outline.Color = System.Drawing.Color.DarkGray;
        }

        #endregion
    }
}
