using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;

namespace GIS
{
    /// <summary>色带节点与上一个节点之间的插值方式。</summary>
    public enum RampInterpolation
    {
        /// <summary>按 RGB 三通道线性插值</summary>
        Rgb = 0,
        /// <summary>按 HSV 色相/饱和度/明度插值</summary>
        Hsv = 1
    }

    /// <summary>
    /// 色带节点。色带用双向链表组织，每个节点保存颜色（含 Alpha）、位置（0~1）
    /// 以及与“上一个节点”之间的插值方式。
    /// </summary>
    public class ColorRampNode
    {
        private double _Position;
        private Color _Color = Color.Black;
        private RampInterpolation _Interpolation = RampInterpolation.Rgb;

        /// <summary>链表前驱节点（位置更小的节点），可为 null。</summary>
        public ColorRampNode Previous { get; internal set; }

        /// <summary>链表后继节点（位置更大的节点），可为 null。</summary>
        public ColorRampNode Next { get; internal set; }

        public ColorRampNode() { }

        public ColorRampNode(double position, Color color)
        {
            Position = position;
            _Color = color;
        }

        public ColorRampNode(double position, Color color, RampInterpolation interpolation)
        {
            Position = position;
            _Color = color;
            _Interpolation = interpolation;
        }

        /// <summary>节点位置，0~1。0 为色带起点，1 为终点。</summary>
        public double Position
        {
            get { return _Position; }
            set { _Position = value < 0 ? 0 : (value > 1 ? 1 : value); }
        }

        /// <summary>节点颜色（含 Alpha）。</summary>
        public Color Color
        {
            get { return _Color; }
            set { _Color = value; }
        }

        /// <summary>与上一个节点之间的插值方式（首个节点无意义）。</summary>
        public RampInterpolation Interpolation
        {
            get { return _Interpolation; }
            set { _Interpolation = value; }
        }

        /// <summary>插值方式中文名。</summary>
        public string InterpolationName
        {
            get { return _Interpolation == RampInterpolation.Hsv ? "HSV 插值" : "RGB 插值"; }
        }

        public ColorRampNode Clone()
        {
            return new ColorRampNode(_Position, _Color, _Interpolation);
        }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:F1}%  A{1} R{2} G{3} B{4}",
                _Position * 100, _Color.A, _Color.R, _Color.G, _Color.B);
        }
    }

    /// <summary>
    /// 色带：由若干颜色节点组成的双向链表，按位置升序排列（头节点位置最小）。
    /// 支持 RGB / HSV 两种插值方式，可按位置取色、均匀重排、存为自定义色带文件等。
    /// </summary>
    public class ColorRamp
    {
        private ColorRampNode _Head;
        private ColorRampNode _Tail;
        private int _Count;

        public ColorRamp() { Name = "自定义色带"; }

        public ColorRamp(string name) { Name = name; }

        /// <summary>色带名称。</summary>
        public string Name { get; set; }

        /// <summary>头节点（位置最小），空色带为 null。</summary>
        public ColorRampNode Head { get { return _Head; } }

        /// <summary>尾节点（位置最大），空色带为 null。</summary>
        public ColorRampNode Tail { get { return _Tail; } }

        /// <summary>节点个数。</summary>
        public int Count { get { return _Count; } }

        /// <summary>按位置升序遍历所有节点。</summary>
        public IEnumerable<ColorRampNode> Nodes
        {
            get
            {
                ColorRampNode node = _Head;
                while (node != null)
                {
                    yield return node;
                    node = node.Next;
                }
            }
        }

        public ColorRampNode NodeAt(int index)
        {
            if (index < 0) return null;
            ColorRampNode node = _Head;
            while (node != null && index-- > 0) node = node.Next;
            return node;
        }

        public int IndexOf(ColorRampNode target)
        {
            int i = 0;
            foreach (ColorRampNode node in Nodes)
            {
                if (ReferenceEquals(node, target)) return i;
                i++;
            }
            return -1;
        }

        /// <summary>清空所有节点。</summary>
        public void Clear()
        {
            _Head = _Tail = null;
            _Count = 0;
        }

        /// <summary>按位置升序插入一个节点（链表始终保持有序）。</summary>
        public void AddNode(ColorRampNode node)
        {
            if (node == null) return;
            node.Previous = node.Next = null;
            if (_Head == null) { _Head = _Tail = node; _Count = 1; return; }

            ColorRampNode cur = _Head;
            while (cur != null && cur.Position <= node.Position) cur = cur.Next;

            if (cur == null)                                  // 追加到尾部
            {
                node.Previous = _Tail;
                _Tail.Next = node;
                _Tail = node;
            }
            else if (cur.Previous == null)                    // 插到头部之前
            {
                node.Next = _Head;
                _Head.Previous = node;
                _Head = node;
            }
            else                                              // 插到中间
            {
                node.Previous = cur.Previous;
                node.Next = cur;
                cur.Previous.Next = node;
                cur.Previous = node;
            }
            _Count++;
        }

        /// <summary>在指定位置插入一个新节点并返回它。</summary>
        public ColorRampNode InsertNode(double position, Color color, RampInterpolation interpolation)
        {
            ColorRampNode node = new ColorRampNode(position, color, interpolation);
            AddNode(node);
            return node;
        }

        /// <summary>删除指定节点。</summary>
        public bool RemoveNode(ColorRampNode node)
        {
            if (node == null || _Count == 0) return false;
            if (!ReferenceEquals(node, _Head) && IndexOf(node) < 0) return false;
            if (node.Previous != null) node.Previous.Next = node.Next;
            else _Head = node.Next;
            if (node.Next != null) node.Next.Previous = node.Previous;
            else _Tail = node.Previous;
            node.Previous = node.Next = null;
            _Count--;
            return true;
        }

        /// <summary>把节点位置均匀分布到 0~1（保持原有顺序与颜色）。</summary>
        public void DistributeEvenly()
        {
            int n = _Count;
            if (n == 0) return;
            if (n == 1) { _Head.Position = 0; return; }
            int i = 0;
            foreach (ColorRampNode node in Nodes) node.Position = (double)i++ / (n - 1);
            _Head.Interpolation = RampInterpolation.Rgb;
        }

        /// <summary>按位置重新排序并重建链表。</summary>
        public void Sort()
        {
            List<ColorRampNode> list = new List<ColorRampNode>(Nodes);
            list.Sort((a, b) => a.Position.CompareTo(b.Position));
            _Head = _Tail = null;
            _Count = 0;
            foreach (ColorRampNode node in list)
            {
                node.Previous = node.Next = null;
                AddNode(node);
            }
        }

        /// <summary>按位置取色（0~1），超出范围取端点颜色。</summary>
        public Color GetColor(double position)
        {
            if (_Head == null) return Color.Black;
            if (_Count == 1) return _Head.Color;
            if (position <= _Head.Position) return _Head.Color;
            if (position >= _Tail.Position) return _Tail.Color;
            ColorRampNode b = _Head;
            while (b != null && b.Position < position) b = b.Next;
            if (b == null) return _Tail.Color;
            ColorRampNode a = b.Previous;
            if (a == null) return b.Color;
            double span = b.Position - a.Position;
            double t = span <= 1e-12 ? 1.0 : (position - a.Position) / span;
            return b.Interpolation == RampInterpolation.Hsv ? LerpHsv(a.Color, b.Color, t) : LerpRgb(a.Color, b.Color, t);
        }

        /// <summary>等间隔取 count 个颜色（用于给分级渲染器的各级配色）。</summary>
        public Color[] Sample(int count)
        {
            if (count <= 0) return new Color[0];
            Color[] colors = new Color[count];
            if (count == 1) { colors[0] = GetColor(0.5); return colors; }
            for (int i = 0; i < count; i++) colors[i] = GetColor((double)i / (count - 1));
            return colors;
        }

        public ColorRamp Clone()
        {
            ColorRamp ramp = new ColorRamp(Name);
            foreach (ColorRampNode node in Nodes) ramp.AddNode(node.Clone());
            return ramp;
        }

        #region 插值

        /// <summary>RGB 线性插值。</summary>
        public static Color LerpRgb(Color a, Color b, double t)
        {
            if (t < 0) t = 0; else if (t > 1) t = 1;
            return Color.FromArgb(
                (int)Math.Round(a.A + t * (b.A - a.A)),
                (int)Math.Round(a.R + t * (b.R - a.R)),
                (int)Math.Round(a.G + t * (b.G - a.G)),
                (int)Math.Round(a.B + t * (b.B - a.B)));
        }

        /// <summary>HSV 插值（色相走最短弧）。</summary>
        public static Color LerpHsv(Color a, Color b, double t)
        {
            if (t < 0) t = 0; else if (t > 1) t = 1;
            double[] h1 = RgbToHsv(a), h2 = RgbToHsv(b);
            double dh = h2[0] - h1[0];
            if (dh > 180) dh -= 360;
            else if (dh < -180) dh += 360;
            double h = h1[0] + t * dh;
            if (h < 0) h += 360;
            if (h >= 360) h -= 360;
            double s = h1[1] + t * (h2[1] - h1[1]);
            double v = h1[2] + t * (h2[2] - h1[2]);
            Color rgb = HsvToRgb(h, s, v);
            int alpha = (int)Math.Round(a.A + t * (b.A - a.A));
            return Color.FromArgb(alpha, rgb.R, rgb.G, rgb.B);
        }

        /// <summary>RGB → HSV，返回 H(0~360) S(0~1) V(0~1)。</summary>
        public static double[] RgbToHsv(Color c)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double v = max, s = max <= 0 ? 0 : (max - min) / max, h;
            if (max - min < 1e-12) h = 0;
            else if (max == r) h = 60 * (((g - b) / (max - min)) % 6);
            else if (max == g) h = 60 * ((b - r) / (max - min) + 2);
            else h = 60 * ((r - g) / (max - min) + 4);
            if (h < 0) h += 360;
            return new[] { h, s, v };
        }

        /// <summary>HSV → RGB。</summary>
        public static Color HsvToRgb(double h, double s, double v)
        {
            if (s < 0) s = 0; else if (s > 1) s = 1;
            if (v < 0) v = 0; else if (v > 1) v = 1;
            h = h % 360; if (h < 0) h += 360;
            double c = v * s;
            double x = c * (1 - Math.Abs((h / 60) % 2 - 1));
            double m = v - c;
            double r, g, b;
            if (h < 60) { r = c; g = x; b = 0; }
            else if (h < 120) { r = x; g = c; b = 0; }
            else if (h < 180) { r = 0; g = c; b = x; }
            else if (h < 240) { r = 0; g = x; b = c; }
            else if (h < 300) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }
            return Color.FromArgb(
                (int)Math.Round((r + m) * 255), (int)Math.Round((g + m) * 255), (int)Math.Round((b + m) * 255));
        }

        #endregion

        #region 内置色带

        /// <summary>默认色带：红 → 白 → 蓝。</summary>
        public static ColorRamp CreateDefault()
        {
            ColorRamp ramp = new ColorRamp("红-白-蓝");
            ramp.AddNode(new ColorRampNode(0.0, Color.FromArgb(178, 24, 43)));
            ramp.AddNode(new ColorRampNode(0.5, Color.FromArgb(255, 255, 255), RampInterpolation.Hsv));
            ramp.AddNode(new ColorRampNode(1.0, Color.FromArgb(33, 102, 172), RampInterpolation.Hsv));
            return ramp;
        }

        private static ColorRamp Make(string name, params object[] stops)
        {
            ColorRamp ramp = new ColorRamp(name);
            for (int i = 0; i < stops.Length; i += 2)
                ramp.AddNode(new ColorRampNode((double)stops[i], (Color)stops[i + 1],
                    i == 0 ? RampInterpolation.Rgb : RampInterpolation.Hsv));
            return ramp;
        }

        /// <summary>内置色带集合。</summary>
        public static List<ColorRamp> BuiltIn()
        {
            var list = new List<ColorRamp>();
            list.Add(CreateDefault());
            list.Add(Make("红-黄-绿",
                0.0, Color.FromArgb(165, 0, 38), 0.5, Color.FromArgb(255, 255, 190), 1.0, Color.FromArgb(0, 104, 55)));
            list.Add(Make("蓝-青-绿-黄-红",
                0.0, Color.FromArgb(49, 54, 149), 0.25, Color.FromArgb(0, 160, 190),
                0.5, Color.FromArgb(120, 198, 121), 0.75, Color.FromArgb(255, 220, 90),
                1.0, Color.FromArgb(215, 48, 39)));
            list.Add(Make("浅黄-橙-红",
                0.0, Color.FromArgb(255, 247, 188), 0.5, Color.FromArgb(254, 196, 79), 1.0, Color.FromArgb(215, 48, 39)));
            list.Add(Make("浅蓝-深蓝",
                0.0, Color.FromArgb(239, 243, 255), 1.0, Color.FromArgb(8, 48, 107)));
            list.Add(Make("绿-黄-红",
                0.0, Color.FromArgb(26, 152, 80), 0.5, Color.FromArgb(255, 255, 191), 1.0, Color.FromArgb(215, 48, 39)));
            list.Add(Make("白-灰-黑",
                0.0, Color.FromArgb(255, 255, 255), 1.0, Color.FromArgb(30, 30, 30)));
            list.Add(Make("紫-白-绿",
                0.0, Color.FromArgb(84, 39, 143), 0.5, Color.FromArgb(247, 247, 247), 1.0, Color.FromArgb(0, 109, 44)));
            list.Add(Make("棕-黄-绿",
                0.0, Color.FromArgb(140, 81, 10), 0.5, Color.FromArgb(246, 232, 195), 1.0, Color.FromArgb(0, 104, 55)));
            return list;
        }

        #endregion

        #region 自定义色带文件（.gcr）

        /// <summary>自定义色带文件的扩展名。</summary>
        public const string FileExtension = ".gcr";

        /// <summary>文件对话框筛选器。</summary>
        public const string FileFilter = "自定义色带文件 (*.gcr)|*.gcr|所有文件 (*.*)|*.*";

        /// <summary>
        /// 写出为自定义色带文件：文件头为格式标识与节点数，随后按顺序写出每个节点的属性
        /// （位置、A、R、G、B、与上一个节点间的插值方式）。
        /// </summary>
        public void Save(string path)
        {
            File.WriteAllLines(path, ToLines(), new UTF8Encoding(false));
        }

        /// <summary>读取自定义色带文件。</summary>
        public static ColorRamp Load(string path)
        {
            return Parse(File.ReadAllLines(path, Encoding.UTF8));
        }

        public string[] ToLines()
        {
            var lines = new List<string>();
            lines.Add("GISCOLORRAMP/1");
            lines.Add("名称=" + (Name ?? ""));
            lines.Add("节点数=" + _Count);
            foreach (ColorRampNode node in Nodes)
                lines.Add(string.Format(CultureInfo.InvariantCulture, "{0:F6} {1} {2} {3} {4} {5}",
                    node.Position, node.Color.A, node.Color.R, node.Color.G, node.Color.B,
                    node.Interpolation == RampInterpolation.Hsv ? "HSV" : "RGB"));
            return lines.ToArray();
        }

        public static ColorRamp Parse(string[] lines)
        {
            if (lines == null || lines.Length == 0) throw new FormatException("色带文件内容为空。");
            if (!lines[0].Trim().StartsWith("GISCOLORRAMP", StringComparison.Ordinal))
                throw new FormatException("不是有效的自定义色带文件（缺少文件头 GISCOLORRAMP）。");

            ColorRamp ramp = new ColorRamp("自定义色带");
            int start = 1;
            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith("名称=", StringComparison.Ordinal)) { ramp.Name = line.Substring(3); continue; }
                if (line.StartsWith("节点数=", StringComparison.Ordinal))
                {
                    start = i + 1;
                    break;
                }
            }
            for (int i = start; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0) continue;
                string[] parts = line.Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 6) continue;
                double position;
                int a, r, g, b;
                if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out position)) continue;
                if (!int.TryParse(parts[1], out a)) continue;
                if (!int.TryParse(parts[2], out r)) continue;
                if (!int.TryParse(parts[3], out g)) continue;
                if (!int.TryParse(parts[4], out b)) continue;
                RampInterpolation interp = parts[5].Equals("HSV", StringComparison.OrdinalIgnoreCase)
                    ? RampInterpolation.Hsv : RampInterpolation.Rgb;
                ramp.AddNode(new ColorRampNode(position, Clamp(a, r, g, b), interp));
            }
            if (ramp.Count == 0) throw new FormatException("色带文件中没有有效的颜色节点。");
            return ramp;
        }

        private static Color Clamp(int a, int r, int g, int b)
        {
            return Color.FromArgb(ClampByte(a), ClampByte(r), ClampByte(g), ClampByte(b));
        }

        private static int ClampByte(int v) { return v < 0 ? 0 : (v > 255 ? 255 : v); }

        #endregion
    }
}
