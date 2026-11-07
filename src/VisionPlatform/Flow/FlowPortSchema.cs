using System;
using System.Collections.Generic;

namespace VisionPlatform.Flow
{
    /// <summary>
    /// 端子表查询门面：端子数据在模块包里（<c>VisionPlatform.Modules.dll</c>），
    /// 本类只转发到 <see cref="Framework.Modules.FlowModuleRegistry"/>。
    /// <para>
    /// 画布（绘制与拉线）、校验器（必填与类型）、执行器（按端子取输入写输出）
    /// 三方共用本表 —— 任何一处都不许硬编码端子名。
    /// </para>
    /// <para>
    /// 【约定】<see cref="OutputsOf"/> 的第 1 项是「主输出」，
    /// <see cref="InputsOf"/> 中第 1 个必填项（无必填则第 1 项）是「主输入」；
    /// 未知类型一律返回空列表 / 空串，不抛异常。
    /// </para>
    /// </summary>
    public static class FlowPortSchema
    {
        /// <summary>该类型的输入端子列表；未知类型返回空列表。</summary>
        public static List<FlowPortDef> InputsOf(FlowNodeType type)
        {
            Framework.Modules.IFlowNodeModule m = Framework.Modules.FlowModuleRegistry.Find(type);
            return (m != null) ? Clone(m.Inputs) : new List<FlowPortDef>();
        }

        /// <summary>该类型的输出端子列表（第 1 项为主输出）；未知类型返回空列表。</summary>
        public static List<FlowPortDef> OutputsOf(FlowNodeType type)
        {
            Framework.Modules.IFlowNodeModule m = Framework.Modules.FlowModuleRegistry.Find(type);
            return (m != null) ? Clone(m.Outputs) : new List<FlowPortDef>();
        }

        /// <summary>按方向取端子列表；未知类型或未知方向返回空列表。</summary>
        public static List<FlowPortDef> PortsOf(FlowNodeType type, FlowPortDir dir)
        {
            return (dir == FlowPortDir.In) ? InputsOf(type) : OutputsOf(type);
        }

        /// <summary>按方向 + Key 查端子；找不到返回 null。</summary>
        public static FlowPortDef Find(FlowNodeType type, FlowPortDir dir, string key)
        {
            if (string.IsNullOrEmpty(key)) return null;

            List<FlowPortDef> list = PortsOf(type, dir);
            foreach (FlowPortDef d in list)
            {
                if (d != null && string.Equals(d.Key, key, StringComparison.Ordinal)) return d;
            }
            return null;
        }

        /// <summary>该类型在该方向上是否存在指定 Key 的端子。</summary>
        public static bool HasPort(FlowNodeType type, FlowPortDir dir, string key)
        {
            return Find(type, dir, key) != null;
        }

        /// <summary>主输出 Key（第 1 个输出端子）；无输出端子时返回 <c>""</c>。</summary>
        public static string PrimaryOutput(FlowNodeType type)
        {
            List<FlowPortDef> outs = OutputsOf(type);
            return (outs.Count > 0 && outs[0] != null) ? outs[0].Key : "";
        }

        /// <summary>主输入 Key：第 1 个必填输入；无必填则第 1 个输入；无输入时返回 <c>""</c>。</summary>
        public static string PrimaryInput(FlowNodeType type)
        {
            List<FlowPortDef> ins = InputsOf(type);
            if (ins.Count == 0) return "";

            foreach (FlowPortDef d in ins)
            {
                if (d != null && d.Required) return d.Key;
            }
            return (ins[0] != null) ? ins[0].Key : "";
        }

        /// <summary>
        /// 两个端子的类型是否可连。规则：<c>from == to || from == Any || to == Any</c>，
        /// 另加「点 ↔ 位姿互通」这一个例外（与 <see cref="FlowPortDef.Accepts"/> 同规则）。
        /// </summary>
        public static bool IsCompatible(FlowPortKind from, FlowPortKind to)
        {
            if (from == to || from == FlowPortKind.Any || to == FlowPortKind.Any) return true;
            if (to == FlowPortKind.Pose && from == FlowPortKind.Point) return true;
            if (to == FlowPortKind.Point && from == FlowPortKind.Pose) return true;
            return false;
        }

        /// <summary>连线合法性判定：源输出端子 → 目标输入端子。</summary>
        public static bool CanConnect(FlowNodeType fromType, string fromPort,
                                      FlowNodeType toType, string toPort)
        {
            FlowPortDef src = Find(fromType, FlowPortDir.Out, fromPort);
            FlowPortDef dst = Find(toType, FlowPortDir.In, toPort);
            if (src == null || dst == null) return false;
            return dst.Accepts(src.Kind);
        }

        // ---------------------- 文档别名（保留既有调用点） ----------------------

        /// <summary>别名：<see cref="InputsOf"/>。</summary>
        public static List<FlowPortDef> Inputs(FlowNodeType type) { return InputsOf(type); }

        /// <summary>别名：<see cref="OutputsOf"/>。</summary>
        public static List<FlowPortDef> Outputs(FlowNodeType type) { return OutputsOf(type); }

        /// <summary>别名：<see cref="PrimaryOutput"/>。</summary>
        public static string DefaultOutputKey(FlowNodeType type) { return PrimaryOutput(type); }

        /// <summary>别名：<see cref="PrimaryInput"/>。</summary>
        public static string DefaultInputKey(FlowNodeType type) { return PrimaryInput(type); }

        private static List<FlowPortDef> Clone(List<FlowPortDef> src)
        {
            var list = new List<FlowPortDef>();
            if (src == null) return list;
            foreach (FlowPortDef d in src) list.Add(d);
            return list;
        }
    }
}
