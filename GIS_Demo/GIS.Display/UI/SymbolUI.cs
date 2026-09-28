using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace GIS.Display.UI
{
    /// <summary>符号相关的 UI 辅助方法。</summary>
    public static class SymbolUI
    {
        /// <summary>生成指定几何类型对应的默认符号。</summary>
        public static Symbol DefaultFor(GeometryTypeConstant type)
        {
            if (type == GeometryTypeConstant.Point || type == GeometryTypeConstant.MultiPoint)
                return new SimpleMarkerSymbol("默认符号");
            if (type == GeometryTypeConstant.LineString || type == GeometryTypeConstant.MultiLineString)
                return new SimpleLineSymbol("默认符号");
            return new SimpleFillSymbol("默认符号");
        }

        /// <summary>生成符号预览位图。</summary>
        public static Bitmap Preview(Symbol symbol, Size size)
        {
            var bmp = new Bitmap(size.Width, size.Height);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                BasicGeometryDrawer.DrawSymbol(g, symbol, new Rectangle(0, 0, size.Width, size.Height));
            }
            return bmp;
        }

        /// <summary>
        /// 颜色按钮：按钮底色即当前颜色，点击后用取色对话框改色。
        /// 底色变深时文字自动转白，否则深色底上的黑字会看不见（黑色是符号的常用默认色）。
        /// </summary>
        public static Button ColorButton(Color color, string text, int width, Action<Color> onPick)
        {
            var button = new Button { Text = text, Width = width, BackColor = color, FlatStyle = FlatStyle.Flat };
            ApplyReadableText(button);
            button.Click += (s, e) =>
            {
                Color picked = ColorPickerForm.Pick(button, button.BackColor);
                if (picked == Color.Empty) return;
                button.BackColor = picked;
                ApplyReadableText(button);
                onPick(picked);
            };
            return button;
        }

        /// <summary>按按钮底色的明暗设置文字颜色：深色底用白字、浅色底用黑字。</summary>
        public static void ApplyReadableText(Button button)
        {
            Color back = button.BackColor;
            int luminance = (back.R * 299 + back.G * 587 + back.B * 114) / 1000;
            button.ForeColor = luminance < 128 ? Color.White : Color.Black;
        }

        /// <summary>按符号类型分派到对应编辑器，返回编辑后的符号；取消返回 null。</summary>
        public static Symbol EditSymbol(IWin32Window owner, Symbol symbol)
        {
            if (symbol is SimpleMarkerSymbol) return MarkerSymbolEditor.Edit(owner, (SimpleMarkerSymbol)symbol);
            if (symbol is SimpleLineSymbol) return LineSymbolEditor.Edit(owner, (SimpleLineSymbol)symbol);
            if (symbol is SimpleFillSymbol) return FillSymbolEditor.Edit(owner, (SimpleFillSymbol)symbol);
            return null;
        }
    }
}
