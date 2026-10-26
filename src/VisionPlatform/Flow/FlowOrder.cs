using System;
using System.Collections.Generic;

namespace VisionPlatform.Flow
{
    /// <summary>问题级别。</summary>
    public enum FlowIssueLevel
    {
        /// <summary>阻止运行的错误。</summary>
        Error,

        /// <summary>可以运行，但值得提示用户。</summary>
        Warning
    }

    /// <summary>
    /// 一条校验问题（或执行顺序分析产生的提示）。
    /// <c>NodeId</c> / <c>PortKey</c> 用于画布定位高亮，某些全局问题（如 E001）为空串。
    /// </summary>
    public sealed class FlowIssue
    {
        /// <summary>级别。</summary>
        public FlowIssueLevel Level { get; private set; }

        /// <summary>稳定代码（E001~E008 / W001~W007，见架构文档 §7.6「校验规则码全表」）。</summary>
        public string Code { get; private set; }

        /// <summary>中文说明。</summary>
        public string Message { get; private set; }

        /// <summary>相关节点 Id；无则为 <c>""</c>。</summary>
        public string NodeId { get; private set; }

        /// <summary>相关端子 Key；无则为 <c>""</c>。</summary>
        public string PortKey { get; private set; }

        public FlowIssue(FlowIssueLevel level, string code, string message,
                         string nodeId = "", string portKey = "")
        {
            Level = level;
            Code = code ?? "";
            Message = message ?? "";
            NodeId = nodeId ?? "";
            PortKey = portKey ?? "";
        }

        /// <summary>是否为 Error 级。</summary>
        public bool IsError
        {
            get { return Level == FlowIssueLevel.Error; }
        }

        public override string ToString()
        {
            return "[" + Code + "] " + Message;
        }
    }

    /// <summary>执行顺序分析的结果。</summary>
    public sealed class FlowOrderResult
    {
        /// <summary>
        /// 拓扑序（Kahn，同层取 (X, Y, Id) 最小者）。孤立节点排在最末尾。
        /// 成环时只含「已经排出」的部分（环上的节点不在里面）。
        /// </summary>
        public List<FlowNode> Order { get; private set; }

        /// <summary>Error 级问题（目前只有 E005 成环）。</summary>
        public List<FlowIssue> Errors { get; private set; }

        /// <summary>Warning 级问题（目前只有 W001 孤立节点）。</summary>
        public List<FlowIssue> Warnings { get; private set; }

        /// <summary>是否没有 Error（可以进入执行阶段）。</summary>
        public bool Success
        {
            get { return Errors.Count == 0; }
        }

        public FlowOrderResult()
        {
            Order = new List<FlowNode>();
            Errors = new List<FlowIssue>();
            Warnings = new List<FlowIssue>();
        }
    }

    /// <summary>
    /// 端口级执行顺序：<strong>Kahn 拓扑排序</strong>，取代旧的节点级单链解析
    /// （旧实现见 <c>FlowRunner.TryBuildChain</c>，职责已迁移到本类）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>入度口径</strong>：某节点的入度 = 指向它的连线中<strong>去重后的上游节点数</strong>。
    /// 也就是说「同一上游的多个输出端子连到本节点的不同输入端子」只算 1 条入度
    /// （架构文档 §7.1 的注释），避免同一条依赖被计两次。
    /// </para>
    /// <para>
    /// <strong>确定性</strong>：每轮从「当前入度为 0 的候选集」里取 <c>(X 升序, Y 升序, Id 升序)</c>
    /// 的最小者输出。这个贪心策略是确定的，且与旧单链解析在「X 递增的链式布局」下
    /// 结果完全一致 —— 这是本轮「旧配置零回归」的底线。
    /// </para>
    /// <para>
    /// <strong>孤立节点策略</strong>：旧行为是「报错不让跑」，新行为是 <strong>W001 警告 + 仍排在最后执行</strong>。
    /// 理由：端子化后「先拖进来还没连线」是合法编辑中间态。
    /// </para>
    /// 本类的所有查询对空流程 / 悬空连线都安全，<strong>不抛异常</strong>。
    /// </remarks>
    public static class FlowOrder
    {
        /// <summary>E005 成环的代码。</summary>
        private const string CodeCycle = "E005";

        /// <summary>W001 孤立节点的代码。</summary>
        private const string CodeIsolated = "W001";

        // ---------------------- 对外 API ----------------------

        /// <summary>
        /// 完整分析：拓扑序 + 成环错误 + 孤立节点警告。
        /// </summary>
        /// <param name="flow">流程；为 null 时返回空结果。</param>
        public static FlowOrderResult Analyze(FlowDefinition flow)
        {
            var result = new FlowOrderResult();
            if (flow == null) return result;

            List<FlowNode> nodes = new List<FlowNode>();
            Dictionary<string, FlowNode> byId = new Dictionary<string, FlowNode>(StringComparer.Ordinal);

            if (flow.Nodes != null)
            {
                foreach (FlowNode n in flow.Nodes)
                {
                    if (n == null || string.IsNullOrEmpty(n.Id)) continue;
                    if (byId.ContainsKey(n.Id)) continue;          // Id 重复：保留第一个
                    byId.Add(n.Id, n);
                    nodes.Add(n);
                }
            }
            if (nodes.Count == 0) return result;

            // ---- 建图：只认两端都在节点表里的连线 ----
            Dictionary<string, HashSet<string>> upstream = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            Dictionary<string, List<string>> downstream = new Dictionary<string, List<string>>(StringComparer.Ordinal);

            foreach (KeyValuePair<string, FlowNode> kv in byId)
            {
                upstream[kv.Key] = new HashSet<string>(StringComparer.Ordinal);
                downstream[kv.Key] = new List<string>();
            }

            if (flow.Links != null)
            {
                foreach (FlowLink l in flow.Links)
                {
                    if (l == null) continue;
                    if (!byId.ContainsKey(l.FromNodeId) || !byId.ContainsKey(l.ToNodeId)) continue;

                    upstream[l.ToNodeId].Add(l.FromNodeId);
                    downstream[l.FromNodeId].Add(l.ToNodeId);
                }
            }

            Dictionary<string, int> indegree = new Dictionary<string, int>(StringComparer.Ordinal);
            HashSet<string> inGraph = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> remaining = new HashSet<string>(StringComparer.Ordinal);
            List<FlowNode> isolated = new List<FlowNode>();

            foreach (FlowNode n in nodes)
            {
                int indeg = upstream[n.Id].Count;
                indegree[n.Id] = indeg;

                bool linked = downstream[n.Id].Count > 0 || indeg > 0;
                if (linked)
                {
                    inGraph.Add(n.Id);
                    remaining.Add(n.Id);
                }
                else
                {
                    isolated.Add(n);                              // 孤立：不进图，末尾排
                }
            }

            // ---- Kahn：每轮取 (X, Y, Id) 最小的入度 0 节点 ----
            while (remaining.Count > 0)
            {
                FlowNode best = null;
                foreach (string id in remaining)
                {
                    if (indegree[id] != 0) continue;
                    FlowNode n = byId[id];
                    if (best == null || CompareNodes(n, best) < 0) best = n;
                }

                if (best == null) break;                          // 剩下的全在环上

                remaining.Remove(best.Id);
                result.Order.Add(best);

                // 同一下游可能因多条边重复出现，这里每个下游节点只减一次
                HashSet<string> decremented = new HashSet<string>(StringComparer.Ordinal);
                foreach (string to in downstream[best.Id])
                {
                    if (!remaining.Contains(to)) continue;
                    if (!decremented.Add(to)) continue;
                    indegree[to] = indegree[to] - 1;
                }
            }

            // ---- 剩余节点 = 环 ----
            if (remaining.Count > 0)
            {
                result.Errors.Add(new FlowIssue(
                    FlowIssueLevel.Error, CodeCycle,
                    "流程存在环：" + DescribeCycle(byId, downstream, remaining) + "。"));
            }

            // ---- 孤立节点：Warning + 排末尾 ----
            if (isolated.Count > 0)
            {
                isolated.Sort(CompareNodes);
                foreach (FlowNode n in isolated)
                {
                    // 文案由 team-lead 拍板改动（原文档 §8.1 写的是「运行时会被跳过」）：
                    // 行为按 §7.1「Warning + 仍执行」，孤立节点排在拓扑序末尾照常跑一遍，
                    // 所以文案改成「排到最后执行」，与行为保持一致。
                    result.Warnings.Add(new FlowIssue(
                        FlowIssueLevel.Warning, CodeIsolated,
                        "节点「" + Label(n) + "」没有任何连线，运行时会排到最后执行。", n.Id, ""));
                    result.Order.Add(n);
                }
            }

            return result;
        }

        /// <summary>
        /// 解析执行顺序（<c>FlowRunner.Begin</c> 的调用形态）。
        /// </summary>
        /// <param name="flow">流程。</param>
        /// <param name="order">拓扑序；失败时为已排出的部分。</param>
        /// <param name="error">失败原因（第一条 Error 的中文说明）；成功为 null。</param>
        /// <returns>是否有合法执行顺序（无成环）。</returns>
        public static bool Build(FlowDefinition flow, out List<FlowNode> order, out string error)
        {
            FlowOrderResult result = Analyze(flow);
            order = result.Order;
            error = (result.Errors.Count > 0) ? result.Errors[0].Message : null;
            return result.Success;
        }

        /// <summary>
        /// 加边前的成环预检（画布拉线时实时调用，§6.6 R6）：若加上
        /// <paramref name="fromNodeId"/> → <paramref name="toNodeId"/> 会成环则返回 true。
        /// </summary>
        /// <remarks>算法：从 <paramref name="toNodeId"/> 沿现有出边 DFS，能到达源节点即成环。自连直接判定为环。</remarks>
        public static bool WouldCreateCycle(FlowDefinition flow, string fromNodeId, string toNodeId)
        {
            if (flow == null || flow.Links == null) return false;
            if (string.IsNullOrEmpty(fromNodeId) || string.IsNullOrEmpty(toNodeId)) return false;
            if (string.Equals(fromNodeId, toNodeId, StringComparison.Ordinal)) return true;

            Stack<string> stack = new Stack<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            stack.Push(toNodeId);

            while (stack.Count > 0)
            {
                string cur = stack.Pop();
                if (string.Equals(cur, fromNodeId, StringComparison.Ordinal)) return true;
                if (!seen.Add(cur)) continue;

                foreach (FlowLink l in flow.Links)
                {
                    if (l == null) continue;
                    if (string.Equals(l.FromNodeId, cur, StringComparison.Ordinal)) stack.Push(l.ToNodeId);
                }
            }
            return false;
        }

        /// <summary>
        /// 顺序比较器：<c>X 升序 → Y 升序 → Id 升序（Ordinal）</c>。
        /// 画布坐标即屏幕坐标，因此这就是「从左到右、从上到下」的直觉顺序。
        /// </summary>
        public static int CompareNodes(FlowNode a, FlowNode b)
        {
            if (ReferenceEquals(a, b)) return 0;
            if (a == null) return 1;
            if (b == null) return -1;

            int c = a.X.CompareTo(b.X);
            if (c != 0) return c;

            c = a.Y.CompareTo(b.Y);
            if (c != 0) return c;

            return string.CompareOrdinal(a.Id ?? "", b.Id ?? "");
        }

        /// <summary>节点显示名：优先 <see cref="FlowNode.Name"/>，缺省回退 Id。</summary>
        public static string Label(FlowNode n)
        {
            if (n == null) return "未命名节点";
            if (!string.IsNullOrEmpty(n.Name)) return n.Name;
            if (!string.IsNullOrEmpty(n.Id)) return n.Id;
            return "未命名节点";
        }

        // ---------------------- 内部实现 ----------------------

        /// <summary>从剩余（环上）节点里走出一条环路并拼成 "A → B → A"。</summary>
        private static string DescribeCycle(Dictionary<string, FlowNode> byId,
                                            Dictionary<string, List<string>> downstream,
                                            HashSet<string> remaining)
        {
            List<string> path = new List<string>();
            HashSet<string> onPath = new HashSet<string>(StringComparer.Ordinal);

            string start = null;
            foreach (string id in remaining) { start = id; break; }
            if (start == null) return "";

            string cur = start;
            while (cur != null)
            {
                path.Add(Label(byId.ContainsKey(cur) ? byId[cur] : null));

                if (!onPath.Add(cur)) break;                        // 回到已经在路径上的点 → 闭环

                string next = null;
                List<string> outs;
                if (downstream.TryGetValue(cur, out outs) && outs != null)
                {
                    foreach (string candidate in outs)
                    {
                        if (remaining.Contains(candidate)) { next = candidate; break; }
                    }
                }
                cur = next;
            }

            if (path.Count > 0 && !string.Equals(path[path.Count - 1], path[0], StringComparison.Ordinal))
                path.Add(path[0]);                                  // 补一圈：A → B → A

            return string.Join(" → ", path);
        }
    }
}
