using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;

namespace GIS
{
    /// <summary>
    /// 渲染符号文件的读写。
    /// 文件扩展名区分几何类型（点 .gps / 线 .gls / 面 .gfs）；
    /// 文件头依次保存几何类型、渲染器类型（单一符号 / 唯一值 / 分级），
    /// 随后是该渲染器的专有信息（唯一值：绑定字段 + 值个数；分级：绑定字段 + 分级数），
    /// 最后按顺序保存每一个符号的属性。
    /// 线符号块的第一行标注“简单符号”或“自定义虚线”，自定义虚线再依次保存每个虚线元素的属性
    /// （与自定义色带文件一样先给出元素个数）。
    /// 导入时若绑定字段在目标图层中不存在，则把符号替换为“绑定属性错误”符号（不可见）。
    /// </summary>
    public static class RendererFile
    {
        /// <summary>点渲染符号文件扩展名</summary>
        public const string PointExtension = ".gps";

        /// <summary>线渲染符号文件扩展名</summary>
        public const string LineExtension = ".gls";

        /// <summary>面（多边形）渲染符号文件扩展名</summary>
        public const string PolygonExtension = ".gfs";

        private const string Header = "GISRENDERER/1";
        private const string SymbolMarker = "[符号]";

        #region 扩展名与文件对话框筛选器

        /// <summary>取几何类型对应的文件扩展名。</summary>
        public static string ExtensionFor(GeometryTypeConstant geometryType)
        {
            switch (geometryType)
            {
                case GeometryTypeConstant.Point:
                case GeometryTypeConstant.MultiPoint:
                    return PointExtension;
                case GeometryTypeConstant.LineString:
                case GeometryTypeConstant.MultiLineString:
                    return LineExtension;
                default:
                    return PolygonExtension;
            }
        }

        /// <summary>取几何类型对应的中文名称。</summary>
        public static string GeometryName(GeometryTypeConstant geometryType)
        {
            switch (geometryType)
            {
                case GeometryTypeConstant.Point:
                case GeometryTypeConstant.MultiPoint:
                    return "点";
                case GeometryTypeConstant.LineString:
                case GeometryTypeConstant.MultiLineString:
                    return "线";
                default:
                    return "面";
            }
        }

        /// <summary>取几何类型对应的文件对话框筛选器。</summary>
        public static string FilterFor(GeometryTypeConstant geometryType)
        {
            string name = GeometryName(geometryType);
            string ext = ExtensionFor(geometryType);
            return name + "渲染符号文件 (*" + ext + ")|*" + ext + "|所有文件 (*.*)|*.*";
        }

        /// <summary>判断文件扩展名是否与几何类型匹配。</summary>
        public static bool IsCompatible(string path, GeometryTypeConstant geometryType)
        {
            if (string.IsNullOrEmpty(path)) return false;
            return string.Equals(Path.GetExtension(path), ExtensionFor(geometryType), StringComparison.OrdinalIgnoreCase);
        }

        #endregion

        #region 保存

        /// <summary>把渲染器保存为渲染符号文件。</summary>
        public static void Save(string path, Renderer renderer, GeometryTypeConstant geometryType)
        {
            if (renderer == null) throw new ArgumentNullException("renderer");
            File.WriteAllLines(path, ToLines(renderer, geometryType), new UTF8Encoding(false));
        }

        /// <summary>把渲染器转换为渲染符号文件的文本行。</summary>
        public static string[] ToLines(Renderer renderer, GeometryTypeConstant geometryType)
        {
            var lines = new List<string>();
            lines.Add(Header);
            lines.Add("几何类型=" + GeometryName(geometryType));

            UniqueValueRenderer unique = renderer as UniqueValueRenderer;
            ClassBreaksRenderer breaks = renderer as ClassBreaksRenderer;

            if (unique != null)
            {
                lines.Add("渲染类型=唯一值");
                lines.Add("绑定字段=" + unique.Field);
                lines.Add("值个数=" + unique.ValueCount);
                for (int i = 0; i < unique.ValueCount; i++)
                {
                    lines.Add(SymbolMarker);
                    lines.Add("值=" + unique.GetValue(i));
                    WriteSymbol(lines, unique.GetSymbol(i), geometryType);
                }
            }
            else if (breaks != null)
            {
                lines.Add("渲染类型=分级");
                lines.Add("绑定字段=" + breaks.Field);
                lines.Add("分级数=" + breaks.BreakCount);
                for (int i = 0; i < breaks.BreakCount; i++)
                {
                    lines.Add(SymbolMarker);
                    lines.Add("分级上限=" + Num(breaks.GetBreakValue(i)));
                    WriteSymbol(lines, breaks.GetSymbol(i), geometryType);
                }
            }
            else
            {
                lines.Add("渲染类型=单一符号");
                lines.Add(SymbolMarker);
                SimpleRenderer simpleRenderer = renderer as SimpleRenderer;
                WriteSymbol(lines, simpleRenderer == null ? null : simpleRenderer.Symbol, geometryType);
            }
            return lines.ToArray();
        }

        private static void WriteSymbol(List<string> lines, Symbol symbol, GeometryTypeConstant geometryType)
        {
            if (symbol is SimpleMarkerSymbol marker)
            {
                lines.Add("形状=" + MarkerStyleName(marker.Style));
                lines.Add("大小=" + Num(marker.Size));
                lines.Add("颜色=" + ColorText(marker.Color));
                lines.Add("边框颜色=" + ColorText(marker.OutlineColor));
                lines.Add("边框宽度=" + Num(marker.OutlineWidth));
            }
            else if (symbol is SimpleLineSymbol line)
            {
                WriteLineSymbol(lines, line, "");
            }
            else if (symbol is SimpleFillSymbol fill)
            {
                lines.Add("填充颜色=" + ColorText(fill.Color));
                lines.Add("边界数=" + fill.Outlines.Count);
                for (int i = 0; i < fill.Outlines.Count; i++)
                {
                    string bp = "边界" + i;
                    FillOutline outline = fill.Outlines[i];
                    lines.Add(bp + "偏移=" + Num(outline == null ? 0 : outline.Offset));
                    WriteLineSymbol(lines, outline == null ? null : outline.Outline, bp);
                }
            }
        }

        // 线符号：第一行说明是简单符号还是自定义虚线，自定义虚线再依次写出每个元素的属性
        private static void WriteLineSymbol(List<string> lines, SimpleLineSymbol line, string prefix)
        {
            if (line == null)
            {
                lines.Add(prefix + "线符号=简单符号");
                lines.Add(prefix + "样式=实线");
                lines.Add(prefix + "颜色=" + ColorText(Color.Black));
                lines.Add(prefix + "线宽=" + Num(0.35));
                return;
            }
            lines.Add(prefix + "线符号=" + (line.HasCustomDash ? "自定义虚线" : "简单符号"));
            lines.Add(prefix + "颜色=" + ColorText(line.Color));
            lines.Add(prefix + "线宽=" + Num(line.Size));
            if (!line.HasCustomDash)
                lines.Add(prefix + "样式=" + LineStyleName(line.Style));
            else
            {
                lines.Add(prefix + "虚线元素数=" + line.DashElements.Count);
                for (int i = 0; i < line.DashElements.Count; i++)
                {
                    // 元素内各属性以分号分隔（颜色本身用逗号分隔，避免歧义）。
                    // 前 7 项为基本属性；后 8 项为可选特性：偏移(启用,值)、延长(启用,左,右)、弧线(启用,振幅,半周期数)。
                    LineDashElement element = line.DashElements[i];
                    lines.Add(prefix + "虚线元素" + i + "=" + string.Join(";", new[]
                    {
                        Num(element.Length), ColorText(element.Color), Num(element.Width),
                        ColorText(element.OutlineColor), Num(element.OutlineWidth),
                        Num(element.TickLength), ColorText(element.TickColor),
                        element.OffsetEnabled ? "1" : "0", Num(element.Offset),
                        element.ExtendEnabled ? "1" : "0", Num(element.ExtendLeft), Num(element.ExtendRight),
                        element.ArcEnabled ? "1" : "0", Num(element.ArcAmplitude), Num(element.ArcHalfPeriods)
                    }));
                }
            }
            // 多条偏移线（国界线等多平行线）：每条偏移线的属性用下标前缀区分，避免同名冲突
            lines.Add(prefix + "偏移线数=" + line.Offsets.Count);
            for (int i = 0; i < line.Offsets.Count; i++)
            {
                string op = prefix + "偏移" + i;
                LineOffset offset = line.Offsets[i];
                lines.Add(op + "量=" + Num(offset == null ? 0 : offset.Offset));
                WriteLineSymbol(lines, offset == null ? null : offset.Line, op);
            }
        }

        #endregion

        #region 读取

        /// <summary>
        /// 从渲染符号文件读入渲染器。target 不为 null 时校验绑定字段：
        /// 若字段不存在，则符号被替换为“绑定属性错误”符号（不可见），
        /// 直到在渲染设置中重新绑定字段。
        /// </summary>
        public static Renderer Load(string path, FeatureClass target)
        {
            string extension = Path.GetExtension(path);
            return Parse(File.ReadAllLines(path, Encoding.UTF8), extension, target);
        }

        /// <summary>解析渲染符号文件内容。</summary>
        public static Renderer Parse(string[] lines, string extension, FeatureClass target)
        {
            if (lines == null || lines.Length == 0) throw new FormatException("渲染符号文件内容为空。");
            if (!lines[0].Trim().StartsWith("GISRENDERER", StringComparison.Ordinal))
                throw new FormatException("不是有效的渲染符号文件（缺少文件头 GISRENDERER）。");

            GeometryTypeConstant fileGeometry = GeometryTypeConstant.Polygon;
            string rendererType = "单一符号";
            string field = "";
            int expected = 0;
            var blocks = new List<List<string>>();
            List<string> current = null;

            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0) continue;
                if (line == SymbolMarker) { current = new List<string>(); blocks.Add(current); continue; }
                if (current == null)
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq), value = line.Substring(eq + 1);
                    switch (key)
                    {
                        case "几何类型": fileGeometry = ParseGeometryName(value); break;
                        case "渲染类型": rendererType = value; break;
                        case "绑定字段": field = value; break;
                        case "值个数":
                        case "分级数":
                            int parsed;
                            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)) expected = parsed;
                            break;
                    }
                }
                else current.Add(line);
            }

            if (expected > 0 && blocks.Count != expected)
                throw new FormatException("渲染符号文件中符号的个数与文件头声明的数量不一致。");

            if (!string.IsNullOrEmpty(extension) && target != null &&
                !string.Equals(extension, ExtensionFor(target.GeometryType), StringComparison.OrdinalIgnoreCase))
                throw new FormatException("渲染符号文件的类型（" + GeometryName(fileGeometry) + "）与当前图层（" +
                    GeometryName(target.GeometryType) + "）不一致。");

            Renderer renderer;
            if (rendererType == "唯一值")
            {
                var unique = new UniqueValueRenderer();
                unique.Field = field;
                foreach (List<string> block in blocks)
                {
                    string value = ValueOf(block, "值", "");
                    unique.AddValue(value, ReadSymbol(block, fileGeometry));
                }
                renderer = unique;
            }
            else if (rendererType == "分级")
            {
                var breaks = new ClassBreaksRenderer();
                breaks.Field = field;
                foreach (List<string> block in blocks)
                {
                    double upper;
                    double.TryParse(ValueOf(block, "分级上限", "0"), NumberStyles.Float, CultureInfo.InvariantCulture, out upper);
                    breaks.AddBreakValue(upper, ReadSymbol(block, fileGeometry));
                }
                renderer = breaks;
            }
            else
            {
                var simple = new SimpleRenderer();
                simple.Symbol = blocks.Count > 0 ? ReadSymbol(blocks[0], fileGeometry) : null;
                renderer = simple;
            }

            // 绑定字段校验：字段不存在 → 符号设为不可见，并标记“绑定属性错误”
            if (target != null && !string.IsNullOrEmpty(field) && target.Fields != null && target.Fields.FindField(field) < 0)
                MarkBindingError(renderer, field);

            return renderer;
        }

        /// <summary>对渲染器执行绑定字段校验，字段不存在时标记错误。</summary>
        public static bool ValidateBinding(Renderer renderer, FeatureClass target)
        {
            if (renderer == null || target == null) return true;
            string field = renderer.BoundField;
            if (string.IsNullOrEmpty(field)) return true;
            if (target.Fields != null && target.Fields.FindField(field) >= 0) return true;
            MarkBindingError(renderer, field);
            return false;
        }

        private static void MarkBindingError(Renderer renderer, string field)
        {
            renderer.SetBindingError(field);
        }

        private static Symbol ReadSymbol(List<string> block, GeometryTypeConstant geometryType)
        {
            if (geometryType == GeometryTypeConstant.Point || geometryType == GeometryTypeConstant.MultiPoint)
            {
                var marker = new SimpleMarkerSymbol();
                marker.Style = ParseMarkerStyle(ValueOf(block, "形状", "圆形"));
                marker.Size = ParseDouble(ValueOf(block, "大小", "3"), 3);
                marker.Color = ParseColor(ValueOf(block, "颜色", "255,255,255,255"), Color.White);
                marker.OutlineColor = ParseColor(ValueOf(block, "边框颜色", "0,0,0,0"), Color.Transparent);
                marker.OutlineWidth = ParseDouble(ValueOf(block, "边框宽度", "0.3"), 0.3);
                return marker;
            }
            if (geometryType == GeometryTypeConstant.LineString || geometryType == GeometryTypeConstant.MultiLineString)
                return ReadLineSymbol(block, "");

            var fill = new SimpleFillSymbol();
            fill.Color = ParseColor(ValueOf(block, "填充颜色", "216,231,207,255"), Color.LightPink);
            fill.Outlines.Clear();
            int count = (int)ParseDouble(ValueOf(block, "边界数", "1"), 1);
            for (int i = 0; i < count; i++)
            {
                string bp = "边界" + i;
                double offset = ParseDouble(ValueOf(block, bp + "偏移", "0"), 0);
                fill.Outlines.Add(new FillOutline(offset, ReadLineSymbol(block, bp)));
            }
            if (fill.Outlines.Count == 0) fill.Outlines.Add(new FillOutline(0, new SimpleLineSymbol { Color = Color.DarkGray }));
            return fill;
        }

        private static SimpleLineSymbol ReadLineSymbol(List<string> block, string prefix)
        {
            var line = new SimpleLineSymbol();
            string kind = ValueOf(block, prefix + "线符号", "简单符号");
            line.Color = ParseColor(ValueOf(block, prefix + "颜色", "0,0,0,255"), Color.Black);
            line.Size = ParseDouble(ValueOf(block, prefix + "线宽", "0.35"), 0.35);
            line.DashElements.Clear();
            line.Offsets.Clear();
            if (kind == "自定义虚线")
            {
                int count = (int)ParseDouble(ValueOf(block, prefix + "虚线元素数", "0"), 0);
                for (int i = 0; i < count; i++)
                {
                    string[] parts = ValueOf(block, prefix + "虚线元素" + i, "").Split(';');
                    var element = new LineDashElement();
                    if (parts.Length >= 7)
                    {
                        element.Length = ParseDouble(parts[0], 5);
                        element.Color = ParseColor(parts[1], Color.Black);
                        element.Width = ParseDouble(parts[2], 0.5);
                        element.OutlineColor = ParseColor(parts[3], Color.Transparent);
                        element.OutlineWidth = ParseDouble(parts[4], 0);
                        element.TickLength = ParseDouble(parts[5], 0);
                        element.TickColor = ParseColor(parts[6], Color.Transparent);
                    }
                    // 后 8 项为可选特性（旧文件没有这些字段，缺省即“未启用”）
                    if (parts.Length >= 15)
                    {
                        element.OffsetEnabled = parts[7] == "1";
                        element.Offset = ParseDouble(parts[8], 0);
                        element.ExtendEnabled = parts[9] == "1";
                        element.ExtendLeft = ParseDouble(parts[10], 0);
                        element.ExtendRight = ParseDouble(parts[11], 0);
                        element.ArcEnabled = parts[12] == "1";
                        element.ArcAmplitude = ParseDouble(parts[13], 1);
                        element.ArcHalfPeriods = ParseDouble(parts[14], 1);
                    }
                    line.DashElements.Add(element);
                }
                line.Style = SimpleLineSymbolStyleConstant.Solid;
            }
            else
            {
                line.Style = ParseLineStyle(ValueOf(block, prefix + "样式", "实线"));
            }
            int offsets = (int)ParseDouble(ValueOf(block, prefix + "偏移线数", "0"), 0);
            for (int i = 0; i < offsets; i++)
            {
                string op = prefix + "偏移" + i;
                double offset = ParseDouble(ValueOf(block, op + "量", "0"), 0);
                line.Offsets.Add(new LineOffset(offset, ReadLineSymbol(block, op)));
            }
            return line;
        }

        #endregion

        #region 文本工具

        private static string ValueOf(List<string> block, string key, string fallback)
        {
            string prefix = key + "=";
            foreach (string line in block)
                if (line.StartsWith(prefix, StringComparison.Ordinal))
                    return line.Substring(prefix.Length);
            return fallback;
        }

        private static string Num(double value)
        {
            return value.ToString("0.####", CultureInfo.InvariantCulture);
        }

        private static double ParseDouble(string text, double fallback)
        {
            double v;
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : fallback;
        }

        private static string ColorText(Color color)
        {
            return color.R + "," + color.G + "," + color.B + "," + color.A;
        }

        private static Color ParseColor(string text, Color fallback)
        {
            string[] parts = (text ?? "").Split(',');
            if (parts.Length < 4) return fallback;
            int r, g, b, a;
            if (!int.TryParse(parts[0], out r)) return fallback;
            if (!int.TryParse(parts[1], out g)) return fallback;
            if (!int.TryParse(parts[2], out b)) return fallback;
            if (!int.TryParse(parts[3], out a)) return fallback;
            return Color.FromArgb(Clamp(a), Clamp(r), Clamp(g), Clamp(b));
        }

        private static int Clamp(int v) { return v < 0 ? 0 : (v > 255 ? 255 : v); }

        private static string MarkerStyleName(SimpleMarkerSymbolStyleConstant style)
        {
            switch (style)
            {
                case SimpleMarkerSymbolStyleConstant.Square: return "方形";
                case SimpleMarkerSymbolStyleConstant.Triangle: return "三角形";
                case SimpleMarkerSymbolStyleConstant.Cross: return "十字";
                case SimpleMarkerSymbolStyleConstant.Exclamation: return "感叹号";
                default: return "圆形";
            }
        }

        private static SimpleMarkerSymbolStyleConstant ParseMarkerStyle(string name)
        {
            switch (name)
            {
                case "方形": return SimpleMarkerSymbolStyleConstant.Square;
                case "三角形": return SimpleMarkerSymbolStyleConstant.Triangle;
                case "十字": return SimpleMarkerSymbolStyleConstant.Cross;
                case "感叹号": return SimpleMarkerSymbolStyleConstant.Exclamation;
                default: return SimpleMarkerSymbolStyleConstant.Circle;
            }
        }

        private static string LineStyleName(SimpleLineSymbolStyleConstant style)
        {
            switch (style)
            {
                case SimpleLineSymbolStyleConstant.Dash: return "虚线";
                case SimpleLineSymbolStyleConstant.Dot: return "点线";
                default: return "实线";
            }
        }

        private static SimpleLineSymbolStyleConstant ParseLineStyle(string name)
        {
            switch (name)
            {
                case "虚线": return SimpleLineSymbolStyleConstant.Dash;
                case "点线": return SimpleLineSymbolStyleConstant.Dot;
                default: return SimpleLineSymbolStyleConstant.Solid;
            }
        }

        private static GeometryTypeConstant ParseGeometryName(string name)
        {
            switch (name)
            {
                case "点": return GeometryTypeConstant.Point;
                case "线": return GeometryTypeConstant.LineString;
                default: return GeometryTypeConstant.Polygon;
            }
        }

        #endregion
    }
}
