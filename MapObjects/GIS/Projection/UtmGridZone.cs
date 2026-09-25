using System;
using System.Globalization;

namespace GIS
{
    /// <summary>
    /// UTM 纬度行（投影带的横行）。
    /// UTM 把北纬 84° 与南纬 80° 之间的地球表面按经差 6° 分为 60 个投影带（自 180° 起向东编号 1~60），
    /// 每个投影带再按纬差 8° 划分为若干四边形：自南纬 80° 起用字母 C 至 X（不含 I 和 O）依次标记，
    /// 依次为 C D E F G H J K L M N P Q R S T U V W X，共 20 行；
    /// 其中最后一行 X 覆盖北纬 72°~84°，跨度为 12°。
    /// 每个四边形以“带号 + 行字母”标记（自参考格网向右、向上读取），例如北京为 50S。
    /// </summary>
    public static class UtmGridZone
    {
        /// <summary>行字母，自南向北（不含 I 和 O）。</summary>
        public const string RowLetters = "CDEFGHJKLMNPQRSTUVWX";

        /// <summary>行数（20 行）。</summary>
        public const int RowCount = 20;

        /// <summary>UTM 适用的最南纬度。</summary>
        public const double MinLatitude = -80.0;

        /// <summary>UTM 适用的最北纬度。</summary>
        public const double MaxLatitude = 84.0;

        /// <summary>普通行的纬度跨度（8°）；最后一行 X 为 12°。</summary>
        public const double RowSpan = 8.0;

        /// <summary>取指定行的字母。</summary>
        public static char LetterAt(int index)
        {
            return RowLetters[ClampIndex(index)];
        }

        /// <summary>取字母对应的行序号，非法字母返回 -1。</summary>
        public static int IndexOfLetter(char letter)
        {
            char upper = char.ToUpperInvariant(letter);
            return RowLetters.IndexOf(upper);
        }

        /// <summary>按纬度求行序号（0 为最南的 C 行），超出 UTM 范围时钳制到两端。</summary>
        public static int IndexOfLatitude(double latitude)
        {
            if (latitude < MinLatitude) return 0;
            if (latitude >= 72.0) return RowCount - 1;      // 72°N~84°N 全部属于 X 行
            int index = (int)Math.Floor((latitude - MinLatitude) / RowSpan);
            return ClampIndex(index);
        }

        /// <summary>按纬度求行字母。</summary>
        public static char LetterOfLatitude(double latitude)
        {
            return LetterAt(IndexOfLatitude(latitude));
        }

        /// <summary>该行的南边界纬度。</summary>
        public static double MinOf(int index)
        {
            index = ClampIndex(index);
            return MinLatitude + RowSpan * index;
        }

        /// <summary>该行的北边界纬度（X 行为 84°）。</summary>
        public static double MaxOf(int index)
        {
            index = ClampIndex(index);
            if (index >= RowCount - 1) return MaxLatitude;
            return MinLatitude + RowSpan * (index + 1);
        }

        /// <summary>该行是否位于北半球（N 行及其以北）。</summary>
        public static bool IsNorthern(int index)
        {
            return LetterAt(index) >= 'N';
        }

        /// <summary>格式化纬度，如 32°N。</summary>
        public static string LatitudeText(double latitude)
        {
            if (Math.Abs(latitude) < 1e-9) return "0°";
            return (latitude > 0 ? latitude : -latitude).ToString("0.#", CultureInfo.InvariantCulture) +
                (latitude > 0 ? "°N" : "°S");
        }

        /// <summary>该行的中文说明，如“S（32°N~40°N）”。</summary>
        public static string RowText(int index)
        {
            index = ClampIndex(index);
            return LetterAt(index) + "（" + LatitudeText(MinOf(index)) + "~" + LatitudeText(MaxOf(index)) + "）";
        }

        /// <summary>四边形的标记，如“50S”。</summary>
        public static string Designator(int zone, int rowIndex)
        {
            return zone.ToString(CultureInfo.InvariantCulture) + LetterAt(rowIndex);
        }

        /// <summary>由经纬度取四边形标记。</summary>
        public static string Designator(double longitude, double latitude)
        {
            return Designator(ProjUTM.GetZone(longitude), IndexOfLatitude(latitude));
        }

        private static int ClampIndex(int index)
        {
            if (index < 0) return 0;
            if (index > RowCount - 1) return RowCount - 1;
            return index;
        }
    }
}
