using System;

namespace VisionPlatform.Flow
{
    /// <summary>
    /// 节点"主判据"的<strong>结构化</strong>取值。
    /// <para>
    /// 【为什么要有这个类】现状是"结果区从图形反推数值"：框里有 X/Y/宽高，但没有分数/置信度/面积，
    /// 于是同一件事在三处界面各写一套文案（结果区、画布叠加、状态栏），必然三处不一致。
    /// 把这些值在<strong>节点执行完的那一刻</strong>就记下来，文案只由 <see cref="NodeResultText"/> 生成一次，
    /// "三处不一致"就从"靠人核对"变成"结构上不可能"。
    /// </para>
    /// <para>
    /// 【"不适用"一律用 -1 / <c>NaN</c> / <c>null</c>，禁止用 0 冒充】0.0 是合法分数、0 是合法目标数，
    /// 用 0 表示"没有这个字段"会让下游无法区分"真的是 0 个目标"和"这个节点不产出目标数"。
    /// </para>
    /// </summary>
    public sealed class FlowNodeMetrics
    {
        /// <summary>目标个数（形状匹配 / NCC / Blob / Detect）；-1 = 不适用。</summary>
        public int TargetCount = -1;

        /// <summary>匹配最高分；<c>NaN</c> = 不适用。</summary>
        public double TopScore = double.NaN;

        /// <summary>最高分目标的中心 X（图像坐标）；<c>NaN</c> = 不适用。</summary>
        public double TopCx = double.NaN;

        /// <summary>最高分目标的中心 Y；<c>NaN</c> = 不适用。</summary>
        public double TopCy = double.NaN;

        /// <summary>NCC 得分；<c>NaN</c> = 不适用。</summary>
        public double NccScore = double.NaN;

        /// <summary>角度（度）：NCC / 位置修正 / OCR 的 ROI 角；<c>NaN</c> = 不适用。</summary>
        public double AngleDeg = double.NaN;

        /// <summary>
        /// 匹配缩放（1 = 模板原尺寸）：仅 NCC 搜索了缩放时才有值；<c>NaN</c> = 不适用。
        /// <para>刻意不放进 <see cref="HasAny"/>：它只是"角度之外的附加量"，
        /// 单靠它不足以让结果区从 Summary 回落到指标行。</para>
        /// </summary>
        public double ScaleFactor = double.NaN;

        /// <summary>OCR 识别出的文本；null = 不适用。</summary>
        public string Text = null;

        /// <summary>字符数；-1 = 不适用。</summary>
        public int CharCount = -1;

        /// <summary>OCR 平均置信度；<c>NaN</c> = 不适用。</summary>
        public double MeanScore = double.NaN;

        /// <summary>
        /// OCR 真正匹配上的字符数（label 不是 "?" 的个数）；-1 = 不适用。
        /// <para>与 <see cref="CharCount"/> 的区别：后者是"分割出几个"，
        /// 本字段是"其中几个认出来了"。两者不等时才是"模板/方向有问题"的直接证据。</para>
        /// </summary>
        public int MatchedCount = -1;

        /// <summary>是否"只做了字符分割、没有模板库"（此时文本是占位符）。</summary>
        public bool TextOnly = false;

        /// <summary>
        /// "只分割没识别"的<strong>具体原因</strong>（如"字符模板库为空" / "4 个文件均读取失败"）。
        /// <para>【为什么不能写死一句话】"目录为空"和"全部读不出来"是两件事，
        /// 在结果区显示成同一句会让用户去建模板，而真因是那批图坏了。</para>
        /// </summary>
        public string TextOnlyReason = null;

        /// <summary>Blob 最大面积（像素²）；<c>NaN</c> = 不适用。</summary>
        public double MaxArea = double.NaN;

        /// <summary>Blob 最大面积目标的圆度；<c>NaN</c> = 不适用。</summary>
        public double MaxCircularity = double.NaN;

        /// <summary>
        /// 主测量值（像素）：点点 / 点直线测量的距离、边缘对的宽度、线线测量的间距…
        /// <c>NaN</c> = 本节点不产出单一测量值。
        /// <para>
        /// 【为什么单列一个字段】测量类节点的"结论"就是一个数（距离 / 宽度），
        /// 塞进 <see cref="TopScore"/> 会把"匹配分数"和"毫米"混成同一个字段，
        /// 结果区与脚本都会读错语义。
        /// </para>
        /// </summary>
        public double Value = double.NaN;

        /// <summary>是否有任何可用判据（全"不适用"则返回 false，文案层据此回落 <c>Summary</c>）。</summary>
        public bool HasAny
        {
            get
            {
                return TargetCount >= 0
                       || !double.IsNaN(TopScore)
                       || !double.IsNaN(NccScore)
                       || !double.IsNaN(AngleDeg)
                       || Text != null
                       || CharCount >= 0
                       || !double.IsNaN(Value)
                       || !double.IsNaN(MaxArea);
            }
        }
    }
}
