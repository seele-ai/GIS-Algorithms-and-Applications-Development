using System.Drawing;

namespace GIS
{
    /// <summary>
    /// 文本符号（注记用）。为避免引入 System.Drawing.Font（netstandard2.0 不含），
    /// 以字体名称/字号/字重等基础字段描述，由绘制模块据此构造 Font。
    /// </summary>
    public class TextSymbol
    {
        private string _FontName = "微软雅黑";
        private float _FontSize = 9;
        private bool _Bold = false;
        private bool _Italic = false;
        private Color _FontColor = Color.Black;
        private double _FontRatio = 1;         //字体宽高比
        private bool _UseMask = false;         //是否描边
        private Color _MaskColor = Color.White;
        private double _MaskWidth = 0.5;       //描边宽度，单位毫米

        public string FontName
        {
            get { return _FontName; }
            set { _FontName = value; }
        }

        public float FontSize
        {
            get { return _FontSize; }
            set { _FontSize = value; }
        }

        public bool Bold
        {
            get { return _Bold; }
            set { _Bold = value; }
        }

        public bool Italic
        {
            get { return _Italic; }
            set { _Italic = value; }
        }

        public Color FontColor
        {
            get { return _FontColor; }
            set { _FontColor = value; }
        }

        public double FontRatio
        {
            get { return _FontRatio; }
            set { _FontRatio = value; }
        }

        public bool UseMask
        {
            get { return _UseMask; }
            set { _UseMask = value; }
        }

        public Color MaskColor
        {
            get { return _MaskColor; }
            set { _MaskColor = value; }
        }

        public double MaskWidth
        {
            get { return _MaskWidth; }
            set { _MaskWidth = value; }
        }

        public TextSymbol Clone()
        {
            return new TextSymbol
            {
                _FontName = _FontName,
                _FontSize = _FontSize,
                _Bold = _Bold,
                _Italic = _Italic,
                _FontColor = _FontColor,
                _FontRatio = _FontRatio,
                _UseMask = _UseMask,
                _MaskColor = _MaskColor,
                _MaskWidth = _MaskWidth
            };
        }
    }
}
