using System;
using System.Globalization;

namespace VisionPlatform.Flow
{
    /// <summary>
    /// 结果文案的<strong>唯一真源</strong>：五处界面（结果区主行 / 画布卡片 / 预览覆盖层 / 预览顶条 / 状态栏）
    /// 都必须调这里，禁止各写一套。
    /// <para>
    /// 【为什么要"一处生成"】旧实现里同一节点在三处显示三套文案（顶条给耗时、覆盖层给链摘要、
    /// 状态栏又一句），改了一处另两处不动，于是"三处不一致"是常态。只要文案只有这一个出口，
    /// 想让它们不一致都做不到。
    /// </para>
    /// </summary>
    public static class NodeResultText
    {
        /// <summary>
        /// 结果区主行第二行（含耗时）。
        /// </summary>
        /// <param name="type">节点类型（决定用哪些字段）。</param>
        /// <param name="m">结构化判据；null 或"全不适用"时回落 <paramref name="fallbackSummary"/>。</param>
        /// <param name="elapsedMs">节点耗时（毫秒）；&lt; 0 表示未知，不显示。</param>
        /// <param name="fallbackSummary">节点自身摘要（用于没有结构化判据的节点类型）。</param>
        public static string Primary(FlowNodeType type, FlowNodeMetrics m, int elapsedMs,
                                     string fallbackSummary = null)
        {
            string cost = elapsedMs >= 0
                ? elapsedMs.ToString(CultureInfo.InvariantCulture) + " ms"
                : "";

            if (m == null || !m.HasAny)
                return ComposeFallback(fallbackSummary, cost);

            switch (type)
            {
                case FlowNodeType.ShapeMatch:
                    return string.Format(CultureInfo.InvariantCulture,
                        "{0} 个{1}{2}{3}",
                        m.TargetCount,
                        Num(" · 最高 {0:0.00}", m.TopScore),
                        Point(m.TopCx, m.TopCy),
                        Cost(cost));

                case FlowNodeType.TemplateMatch:
                    return string.Format(CultureInfo.InvariantCulture,
                        "{0}{1}{2}{3}",
                        Num("{0:0.00}", m.NccScore),
                        Num(" · 角度 {0:0.0}°", m.AngleDeg),
                        m.TargetCount >= 0 ? " · " + m.TargetCount.ToString(CultureInfo.InvariantCulture) + " 个" : "",
                        Cost(cost));

                case FlowNodeType.Ocr:
                    if (m.TextOnly)
                    {
                        // 原因来自加载结果（空库 / 全失败是两件事），不允许在这里写死一句
                        string why = string.IsNullOrEmpty(m.TextOnlyReason) ? "未载入字符模板" : m.TextOnlyReason;
                        return string.Format(CultureInfo.InvariantCulture,
                            "\"{0}\" {1} 字符 · {2}{3}",
                            Quote(m.Text), m.CharCount, why, Cost(cost));
                    }
                    return string.Format(CultureInfo.InvariantCulture,
                        "\"{0}\"{1}{2}{3}",
                        Quote(m.Text),
                        m.CharCount >= 0 ? " · " + m.CharCount.ToString(CultureInfo.InvariantCulture) + " 字符" : "",
                        Num(" · 置信 {0:0.00}", m.MeanScore),
                        Cost(cost));

                case FlowNodeType.Blob:
                    return string.Format(CultureInfo.InvariantCulture,
                        "{0} 个{1}{2}{3}",
                        m.TargetCount,
                        Num(" · 最大面积 {0:0} px²", m.MaxArea),
                        Num(" · 圆度 {0:0.00}", m.MaxCircularity),
                        Cost(cost));

                case FlowNodeType.ObjectDetect:
                    return string.Format(CultureInfo.InvariantCulture,
                        "{0} 个{1}{2}{3}",
                        m.TargetCount,
                        Num(" · 最高 {0:0.00}", m.TopScore),
                        Point(m.TopCx, m.TopCy),
                        Cost(cost));

                default:
                    return ComposeFallback(fallbackSummary, cost);
            }
        }

        /// <summary>画布卡片摘要（更短；卡片宽约 150px，超过会被省略号截断）。</summary>
        public static string Compact(FlowNodeType type, FlowNodeMetrics m)
        {
            if (m == null || !m.HasAny) return "";

            switch (type)
            {
                case FlowNodeType.ShapeMatch:
                    return string.Format(CultureInfo.InvariantCulture, "{0} 个{1}",
                        m.TargetCount, Num(" · {0:0.00}", m.TopScore));

                case FlowNodeType.TemplateMatch:
                    return string.Format(CultureInfo.InvariantCulture, "{0}{1}",
                        Num("{0:0.00}", m.NccScore), Num(" · {0:0.0}°", m.AngleDeg));

                case FlowNodeType.Ocr:
                    if (m.TextOnly) return "仅分割 " + m.CharCount + " 字符";
                    return string.Format(CultureInfo.InvariantCulture, "\"{0}\" · {1} 字",
                        Quote(m.Text), m.CharCount);

                case FlowNodeType.Blob:
                    return string.Format(CultureInfo.InvariantCulture, "{0} 个{1}",
                        m.TargetCount, Num(" · {0:0} px²", m.MaxArea));

                case FlowNodeType.ObjectDetect:
                    return string.Format(CultureInfo.InvariantCulture, "{0} 个{1}",
                        m.TargetCount, Num(" · {0:0.00}", m.TopScore));

                default:
                    return "";
            }
        }

        /// <summary>状态徽标文本（一处生成，五处共用）。</summary>
        public static string Badge(FlowNodeStatus status)
        {
            switch (status)
            {
                case FlowNodeStatus.Running: return "运行中";
                case FlowNodeStatus.Ok: return "OK";
                case FlowNodeStatus.Ng: return "NG";
                case FlowNodeStatus.Skipped: return "跳过";
                // 警示态：执行成功但结果不可用于判良品（如 OCR 模板库为空）
                case FlowNodeStatus.Warn: return "警告";
                default: return "未运行";
            }
        }

        // =====================================================================
        //  内部
        // =====================================================================

        private static string ComposeFallback(string summary, string cost)
        {
            string s = OneLine(summary);
            if (string.IsNullOrEmpty(s))
                return string.IsNullOrEmpty(cost) ? "" : cost;
            return string.IsNullOrEmpty(cost) ? s : s + " · " + cost;
        }

        /// <summary>格式化数值；<c>NaN</c> 返回空串（"不适用"就不该出现在文案里）。</summary>
        private static string Num(string format, double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return "";
            return string.Format(CultureInfo.InvariantCulture, format, value);
        }

        private static string Point(double x, double y)
        {
            if (double.IsNaN(x) || double.IsNaN(y)) return "";
            return string.Format(CultureInfo.InvariantCulture, " · ({0:0},{1:0})", x, y);
        }

        private static string Cost(string cost)
        {
            return string.IsNullOrEmpty(cost) ? "" : " · " + cost;
        }

        /// <summary>文本里可能出现引号 / 换行：引号换成单引号，换行压平，避免把主行撑断。</summary>
        private static string Quote(string text)
        {
            if (text == null) return "";
            string s = OneLine(text).Replace('"', '\'');
            return s;
        }

        private static string OneLine(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            string s = text.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');
            while (s.Contains("  ")) s = s.Replace("  ", " ");
            return s.Trim();
        }
    }
}
