using System;
using VisionPlatform.Flow;

namespace VisionPlatform.UI
{
    /// <summary>节点事件参数（新增 / 移动 / 选中）。</summary>
    public sealed class FlowNodeEventArgs : EventArgs
    {
        public FlowNode Node { get; private set; }
        public FlowNodeEventArgs(FlowNode node) { Node = node; }
    }

    /// <summary>连线事件参数（新增 / 删除）。</summary>
    public sealed class FlowLinkEventArgs : EventArgs
    {
        public FlowLink Link { get; private set; }
        public FlowLinkEventArgs(FlowLink link) { Link = link; }
    }

    /// <summary>
    /// 连线被拒事件参数（画布 toast 只显示 2.8 秒，容易漏看，因此同时发事件供日志区回看）。
    /// 端子上下文可能为空（例如方向错误时尚未解析出「输出 → 输入」两端）。
    /// </summary>
    public sealed class FlowLinkRejectedEventArgs : EventArgs
    {
        /// <summary>中文拒绝原因（可直接进日志）。</summary>
        public string Reason { get; private set; }

        public string FromNodeId { get; private set; }
        public string FromPort { get; private set; }
        public string ToNodeId { get; private set; }
        public string ToPort { get; private set; }

        public FlowLinkRejectedEventArgs(string reason,
                                         string fromNodeId, string fromPort,
                                         string toNodeId, string toPort)
        {
            Reason = reason ?? "";
            FromNodeId = fromNodeId ?? "";
            FromPort = fromPort ?? "";
            ToNodeId = toNodeId ?? "";
            ToPort = toPort ?? "";
        }

        public override string ToString() { return Reason; }
    }

    /// <summary>
    /// 连线被替换事件参数：同一输入端子重复连接时，旧边被新边替换（端口级唯一入边语义）。
    /// 旧边同时会走 <c>LinkRemoved</c> 事件，两者都发是为了让日志区能区分「删线」与「换线」。
    /// </summary>
    public sealed class FlowLinkReplacedEventArgs : EventArgs
    {
        /// <summary>被替换掉的旧连线。</summary>
        public FlowLink OldLink { get; private set; }

        /// <summary>替换后的新连线。</summary>
        public FlowLink NewLink { get; private set; }

        public FlowLinkReplacedEventArgs(FlowLink oldLink, FlowLink newLink)
        {
            OldLink = oldLink;
            NewLink = newLink;
        }
    }

    /// <summary>
    /// 画布节点的运行状态（FR-ALG-23）：等待（灰）/ 运行中（蓝）/ 成功（绿）/ 警告（黄）/ 失败（红）。
    /// 由执行期 <see cref="FlowNodeStatus"/> 映射而来，画布只认这五个视觉态。
    /// <para>
    /// <c>Warn</c> 是"执行成功但结果不能用于判良品"（如 OCR 模板库为空只做了分割）。
    /// 它<strong>必须与 Ok 视觉可辨</strong>，否则"没识别"会被读成"检测通过"。
    /// </para>
    /// </summary>
    public enum FlowNodeRunState
    {
        /// <summary>未执行 / 已复位。</summary>
        Waiting,

        /// <summary>正在执行（高亮）。</summary>
        Running,

        /// <summary>执行成功。</summary>
        Ok,

        /// <summary>执行成功但有警示（结果不可直接用于判良品）。</summary>
        Warn,

        /// <summary>执行失败（判定 NG 或执行异常）。</summary>
        Failed
    }

    /// <summary>单个节点的运行状态、耗时与结果摘要。</summary>
    public sealed class FlowNodeRunInfo
    {
        /// <summary>视觉状态。</summary>
        public FlowNodeRunState State = FlowNodeRunState.Waiting;

        /// <summary>该节点耗时（整数毫秒）；未执行时为 -1（不显示）。</summary>
        public int ElapsedMs = -1;

        /// <summary>
        /// 一行结果摘要（如 <c>3 个 · 0.93</c> / <c>"ABC-1234" · 8 字</c> / <c>12 个 · 1842 px²</c>）。
        /// 空串 = 该节点没有可压缩的判据（节点卡片上显示"未运行"而不是 <c>0</c>）。
        /// </summary>
        public string Summary = "";
    }
}
