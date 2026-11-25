using System;

namespace VisionPlatform.Flow
{
    /// <summary>
    /// 异常检测的「良品得分」：把内部的马氏距离压成 0~1，<b>越高越好</b>。
    /// <para>
    /// 【为什么是"越高越好"】现场把一个 0~1 的数叫「得分」，直觉上就是"质量分/及格分"——
    /// 分数高 = 产品好。如果做成"越高越异常"，看板上出现 0.87 时没人能一眼判断该放行还是该拦。
    /// 所以对外统一输出**良品得分**：1.0 = 完全正常，0.5 = 模型自带阈值线，趋近 0 = 极度异常。
    /// 判定线就是"及格线"，低于它判 NG —— 与"60 分及格"是同一个心智模型。
    /// </para>
    /// <para>
    /// 【为什么不用 <c>1 - ratio/(ratio+1)</c> 之类自己另立一套】本式与内部倍数严格互为反函数：
    /// <c>得分 = 1/(ratio+1)</c>、<c>ratio = 1/得分 - 1</c>。换模型、重训、改判定线都不需要重新理解刻度。
    /// 单调查、严格落在 (0,1]：ratio 0→1.0、0.333→0.75、1→0.5、3→0.25、548→0.0018。
    /// 刻意**不用**线性截断（<c>1-min(ratio,1)</c>）—— 那会把"超阈值 3 倍"和"超 500 倍"压成同一个 0。
    /// </para>
    /// <para>
    /// 【口径约定】<c>ratio</c> 指原生库输出的 <c>max_score</c>（相对**模型自带阈值**的倍数），
    /// 也就是调用 <c>AnomalyDetect</c> 时阈值传 0 得到的那个值。
    /// </para>
    /// </summary>
    public static class AnomalyScore
    {
        /// <summary>判定得分默认值 = 模型自带阈值线（与旧版"阈值填 0"完全等价）。</summary>
        public const double DefaultThreshold = 0.5;

        /// <summary>
        /// 判定得分的下限。低于它等于"要求完美无瑕才放行"，正常波动也会全判 NG。
        /// </summary>
        public const double MinThreshold = 0.02;

        /// <summary>判定得分的上限。到 1.0 对应 ratio = 0（只允许零偏离），物理上不可达。</summary>
        public const double MaxThreshold = 0.99;

        /// <summary>内部倍数 → 良品得分（0~1，越高越好）。非有限或负数返回 <c>NaN</c>。</summary>
        public static double FromRatio(double ratio)
        {
            if (double.IsNaN(ratio) || double.IsInfinity(ratio) || ratio < 0) return double.NaN;
            return 1.0 / (ratio + 1.0);
        }

        /// <summary>良品得分 → 内部倍数（ratio 超过它即低于判定线）。</summary>
        public static double ToRatio(double score)
        {
            double s = ClampThreshold(score);
            return 1.0 / s - 1.0;
        }

        /// <summary>把判定得分夹进可用区间。</summary>
        public static double ClampThreshold(double s)
        {
            if (double.IsNaN(s)) return DefaultThreshold;
            if (s < MinThreshold) return MinThreshold;
            if (s > MaxThreshold) return MaxThreshold;
            return s;
        }

        /// <summary>
        /// 归一化界面上的「判定得分」参数。
        /// <para>
        /// 【必须做这一步】1.0.14.4 之前该参数存的是**绝对马氏距离**（老方案里有 62.05 这种值），
        /// 直接拿老值当得分会变成"满分才放行"，一上线全判 NG。
        /// 这里按 <paramref name="modelThreshold"/> 把老值折回等价的良品得分：
        /// 老值 raw 等价于 ratio = raw/modelThreshold，得分即 1/(ratio+1)。
        /// </para>
        /// </summary>
        /// <param name="rawParam">参数里存的原值。</param>
        /// <param name="modelThreshold">模型自带阈值（原生 <c>st.threshold</c>，阈值传 0 时得到）。</param>
        public static double NormalizeThreshold(double rawParam, double modelThreshold)
        {
            if (double.IsNaN(rawParam) || rawParam <= 0) return DefaultThreshold;   // 0 / 未设置 = 模型推荐
            if (rawParam <= 1.0) return ClampThreshold(rawParam);                  // 已是新语义

            // 旧语义：绝对马氏距离
            if (modelThreshold > 0)
                return ClampThreshold(FromRatio(rawParam / modelThreshold));

            // 模型阈值拿不到（理论上不会发生）：旧值一律按"很松"处理，
            // 因为这批老值本来就远大于任何合理阈值，宽松至少不会误杀。
            return 0.10;
        }

        /// <summary>按「及格线」口径判一次：良品得分低于判定线即异常。</summary>
        public static bool IsNg(double score, double scoreThreshold)
        {
            if (double.IsNaN(score)) return false;
            return score < ClampThreshold(scoreThreshold);
        }
    }
}
