using System;
using System.Collections.Generic;

namespace VisionPlatform.Flow
{
    /// <summary>
    /// 端子的数据/语义类型：决定两个端子之间能不能连线（见 <see cref="FlowPortSchema.IsCompatible"/>）。
    /// </summary>
    /// <remarks>
    /// <strong>只能在枚举末尾追加</strong>：本枚举只在内存中用于判定，但新增成员会改变既有
    /// 「类型兼容矩阵」的形状，追加时请同步更新 docs\架构\增量架构设计.md §4（端口化改造）
    /// 与 §7（公共约定：端子模型）。
    /// </remarks>
    public enum FlowPortKind
    {
        /// <summary>图像帧（各算法块的主体数据流）。</summary>
        Image,

        /// <summary>位姿：平移 + 旋转 + 缩放（对应运行期 <c>FlowRunTransform</c> 的端子化镜像）。</summary>
        Pose,

        /// <summary>标量数值（分数 / 个数 / RMS / 直径等）。</summary>
        Number,

        /// <summary>Blob 列表。</summary>
        BlobList,

        /// <summary>几何量（直线 / 圆）。</summary>
        Geometry,

        /// <summary>通配：接受一切，也能连向一切（目前只出现在「结果输出」的输入端）。</summary>
        Any,

        /// <summary>
        /// 文本摘要（一行结论文本）。
        /// 追加在末尾，不改既有成员的序号；两个按 Kind 分支的 switch
        /// （<c>FlowValidator.KindName</c> / <c>FlowForm.PortKindColor</c>）都有 default 分支兜底。
        /// </summary>
        Text,

        /// <summary>像素坐标点 (x, y)。</summary>
        Point,

        /// <summary>角度（度）。</summary>
        Angle,

        /// <summary>布尔判定（OK / NG）。</summary>
        Bool,

        /// <summary>矩形（x, y, w, h）。</summary>
        Rect
    }

    /// <summary>端子方向。</summary>
    public enum FlowPortDir
    {
        /// <summary>输入端子（画在节点左侧）。</summary>
        In,

        /// <summary>输出端子（画在节点右侧）。</summary>
        Out
    }

    /// <summary>
    /// 单个端子的声明：稳定 Key + 中文显示名 + 数据类型 + 是否必填。
    /// 实例由 <see cref="FlowPortSchema"/> 统一构造，调用方只读。
    /// </summary>
    public sealed class FlowPortDef
    {
        /// <summary>稳定标识（英文，落盘进 <c>FlowLink.FromPort</c> / <c>ToPort</c>；一旦发布不得改名）。</summary>
        public string Key { get; private set; }

        /// <summary>界面显示名（中文，画在节点两侧；可随时改）。</summary>
        public string DisplayName { get; private set; }

        /// <summary>数据类型，决定连线兼容性。</summary>
        public FlowPortKind Kind { get; private set; }

        /// <summary>是否必填（true 的端子未连接时校验器报 E003）。</summary>
        public bool Required { get; private set; }

        /// <summary>鼠标悬停提示；可空。</summary>
        public string Hint { get; private set; }

        /// <summary>
        /// 是否允许<strong>多条线</strong>同时接入（扇入）。
        /// <para>
        /// 默认 false：输入端子语义上是"唯一数据源"，再连一条就替换旧的（换数据源）。
        /// 汇总类端子（脚本的 Any、结果输出的 Any/Image）要允许并行分支同时接进来 ——
        /// 这不是"换源"，而是"多路汇入同一处"。
        /// </para>
        /// </summary>
        public bool MultiIn { get; private set; }

        public FlowPortDef(string key, string displayName, FlowPortKind kind, bool required,
                           string hint = "", bool multiIn = false)
        {
            Key = key ?? "";
            DisplayName = displayName ?? "";
            Kind = kind;
            Required = required;
            Hint = hint ?? "";
            MultiIn = multiIn;
        }

        /// <summary>本端子是否接受来自 <paramref name="source"/> 类型的数据。</summary>
        /// <remarks>
        /// 规则：<c>源类型 == 自身类型 || 源为 Any || 自身为 Any</c>。
        /// <para>
        /// <strong>唯一例外：点 ↔ 位姿互通</strong>。本库的「统一坐标」约定下，
        /// 位姿 = 点 + 角度/尺度（<c>Coord</c> 输出就是 Pose），两者在"一个 X/Y 位置"上语义重叠：
        /// 若判成不兼容，用户把「交点」(Point) 接到「标定转换.坐标」(Pose) 会被拒，
        /// 只能靠"碰巧同名"的端口才连得上。故允许双向连通，但仍<strong>拒绝</strong>图像 / 数值 / 列表。
        /// </para>
        /// </remarks>
        public bool Accepts(FlowPortKind source)
        {
            if (source == Kind || source == FlowPortKind.Any || Kind == FlowPortKind.Any) return true;
            if (Kind == FlowPortKind.Pose && source == FlowPortKind.Point) return true;
            if (Kind == FlowPortKind.Point && source == FlowPortKind.Pose) return true;
            return false;
        }

        public override string ToString()
        {
            return DisplayName;
        }
    }
}
