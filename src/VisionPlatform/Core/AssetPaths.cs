using System;
using System.Collections.Generic;
using System.IO;
using VisionPlatform.Flow;
using VisionPlatform.Recipe;

namespace VisionPlatform.Core
{
    /// <summary>
    /// 方案资产路径解析：模板 / OCR 字符库 / 深度学习模型按
    /// <c>&lt;方案目录&gt;\&lt;资产类型&gt;\&lt;流程名&gt;\&lt;节点名&gt;\</c> 归档。
    /// <para>
    /// 【回落规则】只要下面任一条件不成立，就返回空串，调用方用原来的全局目录：
    /// ① 当前有配方；② 该配方绑定了这条流程（<see cref="Recipe.FlowIds"/>）。
    /// 这样<br/>
    /// —— 老流程（没绑定）= 行为与升级前完全一致，不会因为改了路径就"模板全丢"；<br/>
    /// —— 新方案一旦勾了流程，资产自动进自己的目录，方案之间互不串。
    /// </para>
    /// <para>
    /// 【为什么按"流程名 + 节点名"而不是 Id】现场要能一眼认出目录，且节点改名后
    /// 靠 <c>template.json</c> 索引仍能对上；用 Id 的话谁也不知道 <c>1a28…</c> 是哪个节点。
    /// </para>
    /// </summary>
    public static class AssetPaths
    {
        /// <summary>目录名里不能出现的字符，统一替换成下划线。</summary>
        public static string Safe(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "未命名";

            char[] bad = Path.GetInvalidFileNameChars();
            var sb = new System.Text.StringBuilder(name.Trim().Length);
            for (int i = 0; i < name.Trim().Length; i++)
            {
                char c = name.Trim()[i];
                bool hit = false;
                for (int j = 0; j < bad.Length; j++) if (bad[j] == c) { hit = true; break; }
                sb.Append(hit ? '_' : c);
            }
            return sb.ToString();
        }

        /// <summary>当前方案名（无方案返回空串）。</summary>
        public static string CurrentRecipeName
        {
            get
            {
                Recipe.Recipe r = RecipeStore.Current;
                return r == null ? "" : (r.Name ?? "");
            }
        }

        /// <summary>当前方案根目录（无方案返回空串）。</summary>
        public static string CurrentRecipeDir
        {
            get
            {
                string name = CurrentRecipeName;
                return string.IsNullOrEmpty(name) ? "" : AppPaths.RecipePath(name);
            }
        }

        /// <summary>该流程是否被当前方案绑定（绑定才用方案目录）。</summary>
        public static bool IsBound(FlowDefinition flow)
        {
            if (flow == null) return false;

            Recipe.Recipe r = RecipeStore.Current;
            if (r == null || r.FlowIds == null) return false;

            for (int i = 0; i < r.FlowIds.Count; i++)
                if (string.Equals(r.FlowIds[i], flow.Id, StringComparison.Ordinal)) return true;

            return false;
        }

        /// <summary>
        /// 资产目录：<c>&lt;方案&gt;\&lt;kind&gt;\&lt;流程名&gt;\&lt;节点名&gt;</c>。
        /// 未绑定返回空串（调用方回落到全局目录）。
        /// </summary>
        public static string DirOf(string kind, FlowDefinition flow, FlowNode node)
        {
            string root = CurrentRecipeDir;
            if (string.IsNullOrEmpty(root)) return "";
            if (!IsBound(flow)) return "";

            string flowName = Safe(flow.Name);
            string nodeName = Safe(node != null ? node.Name : "");

            return Path.Combine(root, kind, flowName, nodeName);
        }

        /// <summary>优先用新路径；新路径不存在而旧路径存在时回退到旧路径（只读兼容，不迁移）。</summary>
        private static string PreferExisting(string preferred, string legacy)
        {
            try
            {
                if (System.IO.Directory.Exists(preferred)) return preferred;
                if (!string.IsNullOrEmpty(legacy) && System.IO.Directory.Exists(legacy)) return legacy;
            }
            catch (Exception)
            {
                // 路径不可访问时按"新路径"处理，由调用方创建
            }
            return preferred;
        }

        /// <summary>模板资产目录（模板匹配、圆/线查找等节点的模板图）。</summary>
        public static string TemplateDirOf(FlowDefinition flow, FlowNode node)
        {
            return DirOf("Templates", flow, node);
        }

        /// <summary>OCR 字符库目录（同一流程里不同 OCR 节点各自独立）。</summary>
        public static string OcrDirOf(FlowDefinition flow, FlowNode node)
        {
            // 【旧方案兼容】目录合并前 OCR 字符库在 <方案>\OCRModel\...，合并后在 <方案>\Models\OCR\...
            // 只在"新路径不存在、旧路径存在"时用旧的 —— 否则老方案一升级 OCR 模板就"丢失"。
            return PreferExisting(
                DirOf(System.IO.Path.Combine("Models", "OCR"), flow, node),
                DirOf("OCRModel", flow, node));
        }

        /// <summary>深度学习模型目录（按节点分，便于同一流程跑不同模型）。</summary>
        public static string ModelDirOf(FlowDefinition flow, FlowNode node)
        {
            // 同上：合并前是 <方案>\Models\...，合并后是 <方案>\Models\DL\...
            return PreferExisting(
                DirOf(System.IO.Path.Combine("Models", "DL"), flow, node),
                DirOf("Models", flow, node));
        }

        /// <summary>确保目录存在；返回最终可用路径（dir 为空时返回 fallback）。</summary>
        public static string Ensure(string dir, string fallback)
        {
            string target = string.IsNullOrEmpty(dir) ? fallback : dir;

            try
            {
                if (!string.IsNullOrEmpty(target) && !Directory.Exists(target))
                    Directory.CreateDirectory(target);
            }
            catch (Exception) { }

            return target;
        }
    }
}
