using System;
using System.Collections.Generic;
using VisionPlatform.Core;

namespace VisionPlatform.Flow
{
    /// <summary>完整校验报告（Error + Warning 混排，另提供分类视图）。</summary>
    public sealed class FlowValidationResult
    {
        /// <summary>全部问题（插入顺序：结构 → 执行顺序 → 连线 → 节点输入 → 提示）。</summary>
        public List<FlowIssue> Issues { get; private set; }

        /// <summary>Error 级问题。</summary>
        public List<FlowIssue> Errors { get; private set; }

        /// <summary>Warning 级问题。</summary>
        public List<FlowIssue> Warnings { get; private set; }

        /// <summary>是否允许运行（无 Error 即可跑；仅有 Warning 照样跑）。</summary>
        public bool CanRun
        {
            get { return Errors.Count == 0; }
        }

        public FlowValidationResult()
        {
            Issues = new List<FlowIssue>();
            Errors = new List<FlowIssue>();
            Warnings = new List<FlowIssue>();
        }

        /// <summary>追加一条问题，同时进入分类列表。</summary>
        public void Add(FlowIssue issue)
        {
            if (issue == null) return;
            Issues.Add(issue);
            if (issue.IsError) Errors.Add(issue);
            else Warnings.Add(issue);
        }
    }

    /// <summary>
    /// 流程校验器（<strong>报告层</strong>）：按架构文档 §8.1 的规则表产出错误 / 警告。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 三层校验的职责划分见 §8.2：① 画布实时（R1~R7，T04 负责，不弹窗）；
    /// ②「校验」按钮（本类<strong>全量</strong>报告）；③ 启动流程（本类 Error 级判定，<see cref="HasError"/>）。
    /// 本类只做第 ②③ 层。
    /// </para>
    /// <para>
    /// <strong>校验绝不修改数据</strong>：端子缺失时按两种情况处理 ——
    /// （ⅰ）端子表里有默认端子 → <c>W004</c>，只写 <see cref="Logger.Warn"/>，不进报告；
    /// （ⅱ）连默认端子都没有（源无输出 / 目标无输入）→ <c>E007</c> 报错。
    /// 真正的补齐由 <see cref="FlowLink.Normalize"/> 在加载阶段做。
    /// 理由：校验会在按钮、预览、甚至画布交互里被反复触发，一旦带写副作用
    /// 就变成「看一眼配置就被改一次」。
    /// </para>
    /// <para>
    /// 本类对空流程、悬空连线、未知节点类型一律返回问题清单而<strong>不抛异常</strong>，
    /// 且输出的顺序是确定的（同结构必得同一份报告），便于界面与冒烟断言比对。
    /// </para>
    /// </remarks>
    public static class FlowValidator
    {
        // ---- 规则代码（严格对应 §8.1；中文文案逐字照搬，不要改写） ----
        private const string CodeNoNode = "E001";
        private const string CodeNoImageSource = "E002";
        private const string CodeInputUnconnected = "E003";
        private const string CodeTypeMismatch = "E004";
        private const string CodeMultiInput = "E006";
        private const string CodeInvalidLink = "E007";
        private const string CodeGhostNode = "E008";
        private const string CodeFanout = "W002";
        private const string CodeResultNoUpstream = "W003";
        private const string CodeNormalized = "W004";
        private const string CodePositionNoMatch = "W005";

        // ---------------------- 对外 API ----------------------

        /// <summary>
        /// 完整校验一条流程。
        /// </summary>
        /// <param name="flow">流程；为 null 或没有节点时返回 E001。</param>
        public static FlowValidationResult Validate(FlowDefinition flow)
        {
            var result = new FlowValidationResult();

            List<FlowNode> nodes = (flow != null && flow.Nodes != null)
                ? flow.Nodes
                : new List<FlowNode>();
            Dictionary<string, FlowNode> byId = CollectNodes(nodes);

            // ---- ① 结构层 ----
            if (byId.Count == 0)
            {
                result.Add(new FlowIssue(FlowIssueLevel.Error, CodeNoNode,
                    "流程没有任何节点，请先从左侧工具箱拖入算法块。"));
                return result;
            }

            bool hasImageSource = false;
            foreach (FlowNode n in nodes)
            {
                if (n == null) continue;
                FlowNodeType t;
                if (n.TryGetType(out t) && t == FlowNodeType.ImageSource) { hasImageSource = true; break; }
            }
            if (!hasImageSource)
            {
                result.Add(new FlowIssue(FlowIssueLevel.Error, CodeNoImageSource,
                    "流程缺少「图像源」节点，没有图像输入无法运行。"));
            }

            // ---- ② 执行顺序层：E005 成环 / W001 孤立节点（来源 FlowOrder，避免两处实现） ----
            FlowOrderResult order = FlowOrder.Analyze(flow);
            foreach (FlowIssue e in order.Errors) result.Add(e);
            foreach (FlowIssue w in order.Warnings) result.Add(w);

            // ---- ③ 连线层 ----
            Dictionary<string, PortSlot> inputs = new Dictionary<string, PortSlot>(StringComparer.Ordinal);
            Dictionary<string, PortSlot> fanouts = new Dictionary<string, PortSlot>(StringComparer.Ordinal);
            List<string> fanoutOrder = new List<string>();

            if (flow != null && flow.Links != null)
            {
                foreach (FlowLink l in flow.Links)
                {
                    if (l == null) continue;

                    FlowNode from = FindNode(byId, l.FromNodeId);
                    FlowNode to = FindNode(byId, l.ToNodeId);
                    if (from == null || to == null)
                    {
                        result.Add(new FlowIssue(FlowIssueLevel.Error, CodeGhostNode,
                            "连线指向了不存在的节点，请删除该连线。"));
                        continue;
                    }

                    FlowNodeType fromType, toType;
                    if (!from.TryGetType(out fromType) || !to.TryGetType(out toType)) continue;   // 未知类型：跳过

                    string effFrom = EffectivePort(fromType, FlowPortDir.Out, l.FromPort);
                    string effTo = EffectivePort(toType, FlowPortDir.In, l.ToPort);

                    // ⚠️ 判空必须同时看 null 和 ""：EffectivePort 在「该类型一个输出/输入端子都没有」时
                    // 返回 ""（不是 null），这种情况正是 E007 要报的「没有可用的输出/输入端子」。
                    if (string.IsNullOrEmpty(effFrom) || string.IsNullOrEmpty(effTo))
                    {
                        result.Add(new FlowIssue(FlowIssueLevel.Error, CodeInvalidLink,
                            "连线无效：「" + FlowOrder.Label(from) + "」没有可用的输出端子（或「"
                            + FlowOrder.Label(to) + "」没有输入端子）。", to.Id));
                        continue;
                    }

                    // 端子名缺失 / 非法但能补出默认值 → W004，只记日志，不进报告
                    if (!string.Equals(effFrom, l.FromPort ?? "", StringComparison.Ordinal)
                        || !string.Equals(effTo, l.ToPort ?? "", StringComparison.Ordinal))
                    {
                        Logger.Warn("[" + CodeNormalized + "] 连线端子缺失，已按默认端子补全（旧配置升级）："
                            + FlowOrder.Label(from) + "." + effFrom + " → " + FlowOrder.Label(to) + "." + effTo);
                    }

                    string inputKey = to.Id + "|" + effTo;
                    Count(inputs, inputKey, to.Id, effTo);

                    string fanKey = from.Id + "|" + effFrom;
                    if (!fanouts.ContainsKey(fanKey)) fanoutOrder.Add(fanKey);
                    Count(fanouts, fanKey, from.Id, effFrom);

                    // 类型兼容性
                    FlowPortDef srcDef = FlowPortSchema.Find(fromType, FlowPortDir.Out, effFrom);
                    FlowPortDef dstDef = FlowPortSchema.Find(toType, FlowPortDir.In, effTo);
                    if (srcDef != null && dstDef != null && !dstDef.Accepts(srcDef.Kind))
                    {
                        result.Add(new FlowIssue(FlowIssueLevel.Error, CodeTypeMismatch,
                            "端子类型不匹配：「" + FlowOrder.Label(from) + "." + srcDef.DisplayName + "」（"
                            + KindName(srcDef.Kind) + "）不能连到「" + FlowOrder.Label(to) + "."
                            + dstDef.DisplayName + "」（" + KindName(dstDef.Kind) + "）。",
                            to.Id, effTo));
                    }
                }
            }

            // E006：一个输入端子接了多条连线（按 Key 排序 → 报告顺序确定）
            foreach (string key in SortedKeys(inputs))
            {
                PortSlot slot = inputs[key];
                if (slot.Count <= 1) continue;

                FlowNode node = FindNode(byId, slot.NodeId);
                result.Add(new FlowIssue(FlowIssueLevel.Error, CodeMultiInput,
                    "输入端子「" + FlowOrder.Label(node) + "." + PortLabel(node, FlowPortDir.In, slot.PortKey)
                    + "」连接了多条连线，一个输入端子只能接一条。", slot.NodeId, slot.PortKey));
            }

            // ---- ④ 节点层：E003 必填输入未连接 ----
            foreach (FlowNode node in nodes)
            {
                if (node == null) continue;

                FlowNodeType type;
                if (!node.TryGetType(out type)) continue;

                foreach (FlowPortDef def in FlowPortSchema.InputsOf(type))
                {
                    if (def == null || !def.Required) continue;

                    if (inputs.ContainsKey(node.Id + "|" + def.Key)) continue;

                    // 【特例豁免，不是漏判 —— team-lead 拍板】
                    // 「结果输出」的必填 Any 端子未连接时，豁免 E003，交由 W003 报警告。
                    // 理由：① ResultOutput 的 Any 只是「依赖表达」，不承载真实数据需求；
                    //      ② 「刚拖进来还没连线」在端口化后是合法编辑中间态，不该让整条流程跑不起来；
                    //      ③ §7.1 已把孤立节点从 Error 放宽为 Warning，这里保持同一精神。
                    // 其它节点类型的必填输入未连接仍然照常报 E003。
                    if (type == FlowNodeType.ResultOutput) continue;

                    result.Add(new FlowIssue(FlowIssueLevel.Error, CodeInputUnconnected,
                        "节点「" + FlowOrder.Label(node) + "」的输入端子「" + def.DisplayName + "」未连接。",
                        node.Id, def.Key));
                }
            }

            // ---- ⑤ 提示层：W002 扇出 / W003 结果输出无上游 ----
            fanoutOrder.Sort(StringComparer.Ordinal);
            foreach (string key in fanoutOrder)
            {
                PortSlot slot = fanouts[key];
                if (slot.Count < 2) continue;

                FlowNode node = FindNode(byId, slot.NodeId);
                FlowNodeType type;
                if (!node.TryGetType(out type)) continue;

                FlowPortDef def = FlowPortSchema.Find(type, FlowPortDir.Out, slot.PortKey);
                if (def == null || def.Kind != FlowPortKind.Image) continue;   // 只提示 Image 扇出

                result.Add(new FlowIssue(FlowIssueLevel.Warning, CodeFanout,
                    "「" + FlowOrder.Label(node) + "." + def.DisplayName + "」同时连接了 " + slot.Count
                    + " 个下游。本期执行器按拓扑顺序串行执行并共用同一帧，建议先只用单路。",
                    slot.NodeId, slot.PortKey));
            }

            foreach (FlowNode node in nodes)
            {
                if (node == null) continue;

                FlowNodeType type;
                if (!node.TryGetType(out type) || type != FlowNodeType.ResultOutput) continue;
                if (HasAnyIncoming(flow, node.Id)) continue;

                result.Add(new FlowIssue(FlowIssueLevel.Warning, CodeResultNoUpstream,
                    "「结果输出」节点未接入任何上游，不会汇总到有效结论。", node.Id, ""));
            }

            // ---- ⑥ W005：位置修正上游没有匹配类节点（P0-3 的校验期提示）----
            foreach (FlowNode node in nodes)
            {
                if (node == null) continue;

                FlowNodeType type;
                if (!node.TryGetType(out type) || type != FlowNodeType.PositionCorrect) continue;
                if (!HasAnyIncoming(flow, node.Id)) continue;      // 完全没连线：W001 已经报过，不重复
                if (HasPoseUpstream(flow, byId, node.Id)) continue;

                result.Add(new FlowIssue(FlowIssueLevel.Warning, CodePositionNoMatch,
                    "位置修正节点「" + FlowOrder.Label(node) + "」的上游没有匹配类节点，本次运行不生效。",
                    node.Id, ""));
            }

            return result;
        }

        /// <summary>等价于文档里的 <c>List&lt;FlowIssue&gt; Validate(flow)</c>：返回扁平问题清单。</summary>
        public static List<FlowIssue> Collect(FlowDefinition flow)
        {
            return Validate(flow).Issues;
        }

        /// <summary>是否允许运行（等价于 <see cref="FlowValidationResult.CanRun"/>）。</summary>
        public static bool CanRun(FlowDefinition flow)
        {
            return Validate(flow).CanRun;
        }

        /// <summary>清单里是否存在 Error 级问题。</summary>
        public static bool HasError(List<FlowIssue> issues)
        {
            if (issues == null) return false;
            foreach (FlowIssue i in issues)
            {
                if (i != null && i.IsError) return true;
            }
            return false;
        }

        /// <summary>把清单渲染成多行中文报告（「校验」按钮弹窗用）。</summary>
        public static string ToReport(List<FlowIssue> issues)
        {
            if (issues == null || issues.Count == 0) return "流程校验通过，没有发现问题。";

            int errors = 0, warnings = 0;
            foreach (FlowIssue i in issues)
            {
                if (i == null) continue;
                if (i.IsError) errors++; else warnings++;
            }

            var sb = new System.Text.StringBuilder();
            sb.Append("共 ").Append(errors).Append(" 个错误、").Append(warnings).Append(" 个警告：");

            foreach (FlowIssue i in issues)
            {
                if (i == null) continue;
                sb.Append(Environment.NewLine).Append(i.IsError ? "[错误][" : "[警告][")
                  .Append(i.Code).Append("] ").Append(i.Message);
            }
            return sb.ToString();
        }

        /// <summary>端子数据类型的中文名（用于 E004 文案）。</summary>
        public static string KindName(FlowPortKind kind)
        {
            switch (kind)
            {
                case FlowPortKind.Image:    return "图像";
                case FlowPortKind.Pose:     return "位姿";
                case FlowPortKind.Number:   return "数值";
                case FlowPortKind.BlobList: return "Blob 列表";
                case FlowPortKind.Geometry: return "几何";
                case FlowPortKind.Any:      return "任意";
                default:                    return kind.ToString();
            }
        }

        // ---------------------- 内部辅助 ----------------------

        /// <summary>被计入并去重后的节点表。</summary>
        private static Dictionary<string, FlowNode> CollectNodes(List<FlowNode> nodes)
        {
            var byId = new Dictionary<string, FlowNode>(StringComparer.Ordinal);
            foreach (FlowNode n in nodes)
            {
                if (n == null || string.IsNullOrEmpty(n.Id)) continue;
                if (byId.ContainsKey(n.Id)) continue;
                byId.Add(n.Id, n);
            }
            return byId;
        }

        private static FlowNode FindNode(Dictionary<string, FlowNode> byId, string nodeId)
        {
            if (byId == null || string.IsNullOrEmpty(nodeId)) return null;
            FlowNode n;
            return byId.TryGetValue(nodeId, out n) ? n : null;
        }

        /// <summary>
        /// 取这条连线「实际」使用的端子 Key：表里查得到就原样用，
        /// 查不到（null / 空 / 非法）就用默认端子；默认端子也没有则返回 null。
        /// </summary>
        private static string EffectivePort(FlowNodeType type, FlowPortDir dir, string portKey)
        {
            if (string.IsNullOrEmpty(portKey) || FlowPortSchema.Find(type, dir, portKey) == null)
                return (dir == FlowPortDir.Out)
                    ? FlowPortSchema.PrimaryOutput(type)
                    : FlowPortSchema.PrimaryInput(type);
            return portKey;
        }

        private static void Count(Dictionary<string, PortSlot> slots, string key, string nodeId, string portKey)
        {
            PortSlot slot;
            if (!slots.TryGetValue(key, out slot))
            {
                slot = new PortSlot(nodeId, portKey);
                slots.Add(key, slot);
            }
            slot.Count++;
        }

        private static List<string> SortedKeys(Dictionary<string, PortSlot> slots)
        {
            var keys = new List<string>(slots.Keys);
            keys.Sort(StringComparer.Ordinal);
            return keys;
        }

        private static bool HasAnyIncoming(FlowDefinition flow, string nodeId)
        {
            if (flow == null || flow.Links == null || string.IsNullOrEmpty(nodeId)) return false;
            foreach (FlowLink l in flow.Links)
            {
                if (l != null && string.Equals(l.ToNodeId, nodeId, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        /// <summary>
        /// 该节点的<strong>任意上游</strong>（沿连线往上递归）是否包含匹配类节点
        /// （形状匹配 / 模板匹配）——只有它们会产出位姿，位置修正在 Auto 模式下才有基准可用。
        /// </summary>
        private static bool HasPoseUpstream(FlowDefinition flow, Dictionary<string, FlowNode> byId, string nodeId)
        {
            if (flow == null || flow.Links == null || byId == null || string.IsNullOrEmpty(nodeId)) return false;

            Stack<string> stack = new Stack<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            stack.Push(nodeId);

            while (stack.Count > 0)
            {
                string cur = stack.Pop();
                if (!seen.Add(cur)) continue;

                foreach (FlowLink l in flow.Links)
                {
                    if (l == null) continue;
                    if (!string.Equals(l.ToNodeId, cur, StringComparison.Ordinal)) continue;

                    FlowNode up = FindNode(byId, l.FromNodeId);
                    if (up == null) continue;

                    FlowNodeType t;
                    if (up.TryGetType(out t)
                        && (t == FlowNodeType.ShapeMatch || t == FlowNodeType.TemplateMatch)) return true;

                    stack.Push(l.FromNodeId);
                }
            }
            return false;
        }

        private static string PortLabel(FlowNode node, FlowPortDir dir, string portKey)
        {
            FlowNodeType type;
            if (node != null && node.TryGetType(out type))
            {
                FlowPortDef def = FlowPortSchema.Find(type, dir, portKey);
                if (def != null) return def.DisplayName;
            }
            return portKey ?? "";
        }

        /// <summary>一个端子位置的统计槽（同一 {(节点,端子)} 被几条连线连到）。</summary>
        private sealed class PortSlot
        {
            public string NodeId;
            public string PortKey;
            public int Count;

            public PortSlot(string nodeId, string portKey)
            {
                NodeId = nodeId ?? "";
                PortKey = portKey ?? "";
                Count = 0;
            }
        }
    }
}
