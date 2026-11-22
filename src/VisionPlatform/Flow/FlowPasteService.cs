using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using VisionPlatform.Core;

namespace VisionPlatform.Flow
{
    /// <summary>
    /// 节点剪贴板：复制选中的节点（含内部连线与模板资产），可粘贴到**任意流程**。
    /// <para>【为什么要单独一层】粘贴不是"复制对象"这么简单，有三件事必须一起做对：
    /// ① 新节点要有新 Id（沿用旧 Id 会与源流程冲突）；② 节点名在目标流程里必须唯一
    /// （模板目录是按「流程名/节点名」建的，重名会共用目录、互相覆盖）；
    /// ③ 资产（Templates / OCRModel / Models）要跟着复制，否则粘出来的节点"有参数没模板"。</para>
    /// <para>剪贴板是进程级静态的：从一个流程 Ctrl+C、切到另一个流程 Ctrl+V 就能用，
    /// 也支持同一个流程内粘贴（节点名自动加序号，资产目录各存一份）。</para>
    /// </summary>
    public static class FlowPasteService
    {
        private static readonly List<FlowNode> Buffer = new List<FlowNode>();
        private static readonly List<FlowLink> BufferLinks = new List<FlowLink>();
        private static string _sourceFlowName = "";

        /// <summary>
        /// 资产根目录覆盖（默认取 <see cref="AssetPaths.CurrentRecipeDir"/>）。
        /// 用于测试注入临时目录，或将来做"跨方案粘贴"时指定源方案根目录。
        /// </summary>
        public static string AssetRootOverride { get; set; }

        /// <summary>剪贴板里的节点数（0 = 空）。</summary>
        public static int Count { get { return Buffer.Count; } }

        /// <summary>剪贴板里是否有可粘贴内容。</summary>
        public static bool HasContent { get { return Buffer.Count > 0; } }

        /// <summary>复制来源的流程名（资产目录用；空串表示未知）。</summary>
        public static string SourceFlowName { get { return _sourceFlowName; } }

        /// <summary>
        /// 把 <paramref name="nodes"/> 复制进剪贴板；只带「两端都在选中集内」的连线，
        /// 粘出来才是一段完整的小流程而不是一堆孤立节点。
        /// </summary>
        public static OpResult<int> Copy(FlowDefinition sourceFlow, IList<FlowNode> nodes)
        {
            if (sourceFlow == null) return OpResult<int>.Fail("没有可复制的流程");
            if (nodes == null || nodes.Count == 0) return OpResult<int>.Fail("请先选中要复制的节点");

            Buffer.Clear();
            BufferLinks.Clear();

            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (FlowNode n in nodes)
            {
                if (n == null) continue;
                Buffer.Add(n.Clone());
                ids.Add(n.Id ?? "");
            }

            if (Buffer.Count == 0) return OpResult<int>.Fail("请先选中要复制的节点");

            if (sourceFlow.Links != null)
            {
                foreach (FlowLink l in sourceFlow.Links)
                {
                    if (l == null) continue;
                    if (!ids.Contains(l.FromNodeId ?? "") || !ids.Contains(l.ToNodeId ?? "")) continue;
                    BufferLinks.Add(l.Clone());
                }
            }

            _sourceFlowName = sourceFlow.Name ?? "";
            return OpResult<int>.Ok(Buffer.Count,
                "已复制 " + Buffer.Count + " 个节点" + (BufferLinks.Count > 0 ? "（含 " + BufferLinks.Count + " 条连线）" : ""));
        }

        /// <summary>
        /// 把剪贴板内容粘贴到 <paramref name="targetFlow"/>，让整组节点左上角落在 (<paramref name="x"/>, <paramref name="y"/>)。
        /// <para>节点 Id 重新生成、名称在目标流程内去重、资产目录整份复制（目标目录已存在时不覆盖）。</para>
        /// </summary>
        public static OpResult<int> Paste(FlowDefinition targetFlow, float x, float y)
        {
            if (targetFlow == null) return OpResult<int>.Fail("没有可粘贴的流程");
            if (Buffer.Count == 0) return OpResult<int>.Fail("剪贴板为空，请先复制节点");

            if (targetFlow.Nodes == null) targetFlow.Nodes = new List<FlowNode>();
            if (targetFlow.Links == null) targetFlow.Links = new List<FlowLink>();

            // 整组平移：以左上角对齐到目标点，避免"粘到屏幕外"
            float minX = float.MaxValue, minY = float.MaxValue;
            foreach (FlowNode n in Buffer)
            {
                if (n == null) continue;
                if (n.X < minX) minX = n.X;
                if (n.Y < minY) minY = n.Y;
            }
            if (minX == float.MaxValue) { minX = 0f; minY = 0f; }

            float dx = x - minX;
            float dy = y - minY;

            var idMap = new Dictionary<string, string>(StringComparer.Ordinal);
            var created = new List<FlowNode>();

            foreach (FlowNode src in Buffer)
            {
                if (src == null) continue;

                FlowNode copy = src.Clone();
                copy.Id = Guid.NewGuid().ToString("N");
                copy.Name = UniqueNodeName(targetFlow, src.Name);
                copy.X = src.X + dx;
                copy.Y = src.Y + dy;

                // 资产按「流程名/节点名」存放：必须在改名之后复制，否则会写回源目录
                CopyAssets(_sourceFlowName, src.Name, targetFlow.Name, copy.Name);

                targetFlow.Nodes.Add(copy);
                if (!string.IsNullOrEmpty(src.Id)) idMap[src.Id] = copy.Id;
                created.Add(copy);
            }

            if (created.Count == 0) return OpResult<int>.Fail("剪贴板为空，请先复制节点");

            foreach (FlowLink l in BufferLinks)
            {
                if (l == null) continue;

                string from, to;
                if (!idMap.TryGetValue(l.FromNodeId ?? "", out from)) continue;
                if (!idMap.TryGetValue(l.ToNodeId ?? "", out to)) continue;

                targetFlow.Links.Add(new FlowLink(from, l.FromPort ?? "", to, l.ToPort ?? ""));
            }

            string msg = "已粘贴 " + created.Count + " 个节点";
            if (BufferLinks.Count > 0) msg += "（含 " + BufferLinks.Count + " 条连线）";
            if (!string.Equals(_sourceFlowName, targetFlow.Name, StringComparison.Ordinal))
                msg += "，来源流程「" + _sourceFlowName + "」";

            return OpResult<int>.Ok(created.Count, msg);
        }

        /// <summary>清空剪贴板（仅测试/诊断用）。</summary>
        public static void Clear()
        {
            Buffer.Clear();
            BufferLinks.Clear();
            _sourceFlowName = "";
        }

        /// <summary>
        /// 目标流程内唯一的节点名。模板目录按名字建，重名会让两个节点共用一份模板图。
        /// </summary>
        private static string UniqueNodeName(FlowDefinition flow, string name)
        {
            string baseName = string.IsNullOrWhiteSpace(name) ? "节点" : name.Trim();
            if (!NodeNameExists(flow, baseName)) return baseName;

            for (int i = 2; i < 1000; i++)
            {
                string candidate = baseName + " " + i;
                if (!NodeNameExists(flow, candidate)) return candidate;
            }

            return baseName + " " + Guid.NewGuid().ToString("N").Substring(0, 4);
        }

        private static bool NodeNameExists(FlowDefinition flow, string name)
        {
            if (flow == null || flow.Nodes == null) return false;

            foreach (FlowNode n in flow.Nodes)
            {
                if (n == null) continue;
                if (string.Equals((n.Name ?? "").Trim(), name, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        /// <summary>
        /// 复制节点的资产目录：<c>&lt;方案&gt;\&lt;kind&gt;\&lt;流程名&gt;\&lt;节点名&gt;</c>。
        /// <para>目标目录已存在时跳过（不覆盖已有文件）；同流程同名的极端情况也跳过（源=目标）。</para>
        /// </summary>
        private static void CopyAssets(string srcFlowName, string srcNodeName, string dstFlowName, string dstNodeName)
        {
            string root = string.IsNullOrEmpty(AssetRootOverride)
                ? AssetPaths.CurrentRecipeDir
                : AssetRootOverride;
            if (string.IsNullOrEmpty(root)) return;                       // 未绑定方案：没有资产
            if (string.IsNullOrEmpty(srcFlowName) || string.IsNullOrEmpty(dstFlowName)) return;

            string srcSafeFlow = AssetPaths.Safe(srcFlowName);
            string dstSafeFlow = AssetPaths.Safe(dstFlowName);
            string srcSafeNode = AssetPaths.Safe(srcNodeName);
            string dstSafeNode = AssetPaths.Safe(dstNodeName);

            if (string.Equals(srcSafeFlow, dstSafeFlow, StringComparison.Ordinal)
                && string.Equals(srcSafeNode, dstSafeNode, StringComparison.Ordinal)) return;

            string[] kinds = { "Templates", System.IO.Path.Combine("Models", "OCR"), System.IO.Path.Combine("Models", "DL") };
            foreach (string kind in kinds)
            {
                string src = Path.Combine(root, kind, srcSafeFlow, srcSafeNode);
                string dst = Path.Combine(root, kind, dstSafeFlow, dstSafeNode);

                try
                {
                    if (!Directory.Exists(src) || Directory.Exists(dst)) continue;
                    CopyDirectory(src, dst);
                }
                catch (Exception ex)
                {
                    Logger.Warn("复制节点资产失败（" + kind + "）：" + ex.Message);
                }
            }
        }

        private static void CopyDirectory(string src, string dst)
        {
            Directory.CreateDirectory(dst);

            foreach (string file in Directory.GetFiles(src))
                File.Copy(file, Path.Combine(dst, Path.GetFileName(file)), false);

            foreach (string dir in Directory.GetDirectories(src))
                CopyDirectory(dir, Path.Combine(dst, Path.GetFileName(dir)));
        }
    }
}
