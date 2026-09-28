using System;
using System.Collections.Generic;
using System.Drawing;

namespace GIS
{
    /// <summary>
    /// 面符号的一条边界。Offset 为边界相对多边形轮廓向外偏移的距离（毫米，正值向外），
    /// Outline 为该边界的线符号。多条边界叠加可实现渐变/多环边界效果。
    /// </summary>
    public class FillOutline
    {
        /// <summary>向外偏移量（单位毫米，0 表示紧贴轮廓）</summary>
        public double Offset = 0;

        /// <summary>边界的线符号</summary>
        public SimpleLineSymbol Outline;

        public FillOutline()
        {
            Outline = new SimpleLineSymbol();
            Outline.Color = Color.DarkGray;
        }

        public FillOutline(double offset, SimpleLineSymbol outline)
        {
            Offset = offset;
            Outline = outline;
        }

        public FillOutline Clone()
        {
            return new FillOutline(Offset, (SimpleLineSymbol)(Outline == null ? null : Outline.Clone()));
        }
    }

    /// <summary>
    /// 简单面符号。Color 为填充色，Outlines 为边界集合（可多条、可偏移）。
    /// Outline 属性为兼容旧代码保留，等价于第一条边界（偏移为 0）。
    /// </summary>
    public class SimpleFillSymbol : Symbol
    {
        private Color _Color = Color.LightPink;   //填充颜色
        private List<FillOutline> _Outlines = new List<FillOutline>();  //边界集合

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
        public Color Color
        {
            get { return _Color; }
            set { _Color = value; }
        }

        /// <summary>获取边界集合</summary>
        public List<FillOutline> Outlines
        {
            get { return _Outlines; }
        }

        /// <summary>获取或设置（第一条）边界符号，兼容旧代码</summary>
        public SimpleLineSymbol Outline
        {
            get
            {
                if (_Outlines.Count == 0)
                    return null;
                return _Outlines[0].Outline;
            }
            set
            {
                _Outlines.Clear();
                if (value != null)
                    _Outlines.Add(new FillOutline(0, value));
            }
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
            sSymbol.Visible = Visible;
            sSymbol._Color = _Color;
            sSymbol._Outlines.Clear();
            foreach (FillOutline outline in _Outlines)
                sSymbol._Outlines.Add(outline.Clone());
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
            _Outlines.Add(new FillOutline(0, new SimpleLineSymbol { Color = Color.DarkGray }));
        }

        #endregion
    }
}
