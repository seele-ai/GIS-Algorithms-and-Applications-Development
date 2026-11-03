using System;
using System.Collections.Generic;

namespace GIS
{
    /// <summary>
    /// 一组数值的统计量：最小、最大、均值、中位数、标准差（总体标准差，除以 n）。
    /// 连续渲染的 KDE 分布图与特殊值虚线都用它。
    /// </summary>
    public sealed class ContinuousStatistics
    {
        /// <summary>有效数值个数。</summary>
        public int Count { get; private set; }

        public double Min { get; private set; }
        public double Max { get; private set; }
        public double Mean { get; private set; }
        public double Median { get; private set; }
        public double Sigma { get; private set; }

        /// <summary>区间跨度（全同值时为 0）。</summary>
        public double Span
        {
            get { return Max - Min; }
        }

        /// <summary>计算统计量；没有有效数值时返回 Count = 0 的对象。</summary>
        public static ContinuousStatistics Compute(IEnumerable<double> values)
        {
            var statistics = new ContinuousStatistics();
            if (values == null) return statistics;
            var list = new List<double>();
            foreach (double v in values)
                if (!double.IsNaN(v) && !double.IsInfinity(v)) list.Add(v);
            if (list.Count == 0) return statistics;

            list.Sort();
            int n = list.Count;
            statistics.Count = n;
            statistics.Min = list[0];
            statistics.Max = list[n - 1];
            double sum = 0;
            for (int i = 0; i < n; i++) sum += list[i];
            statistics.Mean = sum / n;
            statistics.Median = n % 2 == 1 ? list[n / 2] : (list[n / 2 - 1] + list[n / 2]) / 2.0;
            double sq = 0;
            for (int i = 0; i < n; i++)
            {
                double d = list[i] - statistics.Mean;
                sq += d * d;
            }
            statistics.Sigma = Math.Sqrt(sq / n);
            return statistics;
        }
    }

    /// <summary>连续渲染分布图上要标出的特殊值。</summary>
    public enum ContinuousSpecialValue
    {
        /// <summary>零</summary>
        Zero = 0,
        /// <summary>最小值</summary>
        Minimum = 1,
        /// <summary>最大值</summary>
        Maximum = 2,
        /// <summary>均值</summary>
        Mean = 3,
        /// <summary>中位数</summary>
        Median = 4,
        /// <summary>μ − 1σ</summary>
        Minus1Sigma = 5,
        /// <summary>μ − 2σ</summary>
        Minus2Sigma = 6,
        /// <summary>μ − 3σ</summary>
        Minus3Sigma = 7,
        /// <summary>μ + 1σ</summary>
        Plus1Sigma = 8,
        /// <summary>μ + 2σ</summary>
        Plus2Sigma = 9,
        /// <summary>μ + 3σ</summary>
        Plus3Sigma = 10
    }

    /// <summary>
    /// 分布图上的一条特殊值虚线：是哪个特殊值、原始数值、在拉伸区间里的位置（0~1）、显示文字。
    /// 数值相同（在容差内）的多个特殊值会合并成一条线，文字用“/”连接，避免几条线叠在一起。
    /// </summary>
    public sealed class ContinuousMark
    {
        public ContinuousMark(ContinuousSpecialValue value, double dataValue, double position, string label)
        {
            Value = value;
            DataValue = dataValue;
            Position = position;
            Label = label;
        }

        /// <summary>主特殊值（合并时取第一个）。</summary>
        public ContinuousSpecialValue Value { get; private set; }

        /// <summary>原始数值。</summary>
        public double DataValue { get; private set; }

        /// <summary>在拉伸区间里的位置，0~1（0 = 最小值端，1 = 最大值端）。</summary>
        public double Position { get; private set; }

        /// <summary>显示文字，例如“均值/中位数 12.3”。</summary>
        public string Label { get; set; }

        /// <summary>
        /// 该特殊值是否在 [最小, 最大] 之外。区间外的特殊值（常见于 μ±3σ、零）
        /// 仍会画在最近的端点并标注“超出范围”，这样用户始终能看到全部特殊值的位置。
        /// </summary>
        public bool Outside { get; set; }

        /// <summary>合并进来的全部特殊值。</summary>
        public List<ContinuousSpecialValue> Values { get; } = new List<ContinuousSpecialValue>();

        /// <summary>是否包含某个特殊值。</summary>
        public bool Contains(ContinuousSpecialValue value)
        {
            return Values.Contains(value);
        }
    }

    /// <summary>把统计量转换成分布图上的特殊值虚线（含合并、越界剔除）。</summary>
    public static class ContinuousMarks
    {
        /// <summary>特殊值的中文名。</summary>
        public static string NameOf(ContinuousSpecialValue value)
        {
            switch (value)
            {
                case ContinuousSpecialValue.Zero: return "零";
                case ContinuousSpecialValue.Minimum: return "最小";
                case ContinuousSpecialValue.Maximum: return "最大";
                case ContinuousSpecialValue.Mean: return "均值";
                case ContinuousSpecialValue.Median: return "中位数";
                case ContinuousSpecialValue.Minus1Sigma: return "μ−1σ";
                case ContinuousSpecialValue.Minus2Sigma: return "μ−2σ";
                case ContinuousSpecialValue.Minus3Sigma: return "μ−3σ";
                case ContinuousSpecialValue.Plus1Sigma: return "μ+1σ";
                case ContinuousSpecialValue.Plus2Sigma: return "μ+2σ";
                case ContinuousSpecialValue.Plus3Sigma: return "μ+3σ";
                default: return value.ToString();
            }
        }

        /// <summary>
        /// 生成特殊值虚线（按数值升序）。
        /// 超出 [最小, 最大] 的特殊值（例如 μ±3σ）没有对应的横坐标，直接不显示；
        /// 数值相差小于跨度万分之一的特殊值合并成同一条线（例如数据对称时均值 = 中位数）。
        /// </summary>
        public static List<ContinuousMark> Build(ContinuousStatistics statistics)
        {
            var marks = new List<ContinuousMark>();
            if (statistics == null || statistics.Count == 0) return marks;

            var candidates = new List<KeyValuePair<ContinuousSpecialValue, double>>();
            candidates.Add(new KeyValuePair<ContinuousSpecialValue, double>(ContinuousSpecialValue.Minimum, statistics.Min));
            candidates.Add(new KeyValuePair<ContinuousSpecialValue, double>(ContinuousSpecialValue.Maximum, statistics.Max));
            candidates.Add(new KeyValuePair<ContinuousSpecialValue, double>(ContinuousSpecialValue.Mean, statistics.Mean));
            candidates.Add(new KeyValuePair<ContinuousSpecialValue, double>(ContinuousSpecialValue.Median, statistics.Median));
            // 正态分布的 ±1σ / ±2σ / ±3σ 三组
            for (int k = 1; k <= 3; k++)
            {
                ContinuousSpecialValue minus = k == 1 ? ContinuousSpecialValue.Minus1Sigma
                    : (k == 2 ? ContinuousSpecialValue.Minus2Sigma : ContinuousSpecialValue.Minus3Sigma);
                ContinuousSpecialValue plus = k == 1 ? ContinuousSpecialValue.Plus1Sigma
                    : (k == 2 ? ContinuousSpecialValue.Plus2Sigma : ContinuousSpecialValue.Plus3Sigma);
                candidates.Add(new KeyValuePair<ContinuousSpecialValue, double>(minus, statistics.Mean - k * statistics.Sigma));
                candidates.Add(new KeyValuePair<ContinuousSpecialValue, double>(plus, statistics.Mean + k * statistics.Sigma));
            }
            candidates.Add(new KeyValuePair<ContinuousSpecialValue, double>(ContinuousSpecialValue.Zero, 0));

            double span = statistics.Span;
            double tolerance = Math.Max(span * 1e-4, Math.Abs(statistics.Max) * 1e-9 + 1e-12);
            double low = statistics.Min - tolerance, high = statistics.Max + tolerance;

            var kept = new List<KeyValuePair<ContinuousSpecialValue, double>>();
            foreach (KeyValuePair<ContinuousSpecialValue, double> candidate in candidates)
            {
                if (double.IsNaN(candidate.Value) || double.IsInfinity(candidate.Value)) continue;
                kept.Add(candidate);   // 区间外的特殊值也保留：画在端点并标注“超出范围”
            }
            kept.Sort((a, b) => a.Value.CompareTo(b.Value));

            foreach (KeyValuePair<ContinuousSpecialValue, double> candidate in kept)
            {
                ContinuousMark last = marks.Count == 0 ? null : marks[marks.Count - 1];
                if (last != null && Math.Abs(last.DataValue - candidate.Value) <= tolerance)
                {
                    last.Values.Add(candidate.Key);
                    last.Label = LabelFor(last);
                    continue;
                }
                bool outside = candidate.Value < low || candidate.Value > high;
                double position = span <= 0
                    ? (outside ? (candidate.Value < statistics.Min ? 0 : 1) : 0.5)
                    : (candidate.Value - statistics.Min) / span;
                if (position < 0) position = 0;
                if (position > 1) position = 1;
                var mark = new ContinuousMark(candidate.Key, candidate.Value, position, "") { Outside = outside };
                mark.Values.Add(candidate.Key);
                mark.Label = LabelFor(mark);
                marks.Add(mark);
            }
            return marks;
        }

        private static string LabelFor(ContinuousMark mark)
        {
            var names = new List<string>();
            foreach (ContinuousSpecialValue value in mark.Values) names.Add(NameOf(value));
            return string.Join("/", names.ToArray()) + " " + FormatValue(mark.DataValue)
                + (mark.Outside ? "（超出范围）" : "");
        }

        /// <summary>数值显示：按数量级自动保留 0~4 位小数。</summary>
        public static string FormatValue(double value)
        {
            double abs = Math.Abs(value);
            if (abs >= 1000) return value.ToString("0");
            if (abs >= 1) return value.ToString("0.##");
            if (abs >= 0.001) return value.ToString("0.####");
            return value.ToString("0.######");
        }
    }

    /// <summary>
    /// 核密度估计（高斯核）。用于连续渲染页面上方的数值分布曲线：
    /// 曲线只与要素数值有关，与色带无关；色带只决定曲线下方填充的颜色。
    /// </summary>
    public static class KernelDensity
    {
        /// <summary>最多使用的样本数（要素很多时等间隔抽样，保证绘制速度）。</summary>
        private const int MaxSamples = 2048;

        /// <summary>
        /// 在 [min, max] 上等间隔取 samples 个点，返回每个点的密度值（未归一化到面积 1 之外的处理见下）。
        /// 带宽用 Silverman 经验公式；σ = 0（全同值）时退化为一条窄峰。
        /// </summary>
        public static double[] Estimate(double[] values, int samples, double min, double max)
        {
            if (samples < 2) samples = 2;
            var result = new double[samples];
            if (values == null || values.Length == 0) return result;
            if (max <= min)
            {
                // 全部数值相同：在中间画一个窄峰，便于用户看出数据集中在一处
                result[samples / 2] = 1;
                return result;
            }

            int stride = Math.Max(1, values.Length / MaxSamples);
            int used = 0;
            for (int i = 0; i < values.Length; i += stride) used++;
            double mean = 0, sq = 0;
            foreach (double v in values) mean += v;
            mean /= values.Length;
            foreach (double v in values) sq += (v - mean) * (v - mean);
            double sigma = Math.Sqrt(sq / values.Length);

            // Silverman 带宽：h = 0.9 * min(σ, IQR/1.34) * n^(-1/5)
            double h = 0.9 * sigma * Math.Pow(values.Length, -0.2);
            if (h <= 0 || double.IsNaN(h)) h = (max - min) / 50.0;
            if (h <= 0) h = 1e-6;

            double dx = (max - min) / (samples - 1);
            double norm = 1.0 / (h * Math.Sqrt(2 * Math.PI));
            double peak = 0;
            for (int s = 0; s < samples; s++)
            {
                double x = min + s * dx;
                double sum = 0;
                int index = 0;
                for (int i = 0; i < values.Length; i += stride)
                {
                    if (index++ >= MaxSamples) break;
                    double u = (x - values[i]) / h;
                    sum += Math.Exp(-0.5 * u * u);
                }
                double density = sum * norm / Math.Max(1, used);
                result[s] = density;
                if (density > peak) peak = density;
            }
            if (peak > 0)
                for (int s = 0; s < samples; s++) result[s] /= peak;   // 归一化到 0~1，绘制时直接乘高度
            return result;
        }
    }
}
