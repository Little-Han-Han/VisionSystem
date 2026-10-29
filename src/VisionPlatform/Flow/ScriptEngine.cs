using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.CSharp;
using System.CodeDom.Compiler;

namespace VisionPlatform.Flow
{
    /// <summary>
    /// 一个节点在本轮运行中的"输出快照"，供脚本节点读取。
    /// <para>
    /// 【为什么要单独一张表】流水线里节点之间原先只靠 <c>session.Current</c>（图像）与
    /// <c>NodePoses</c>（位姿）传值，其它结果（数量 / 分数 / 文本 / 几何）都散在
    /// <c>FlowNodeProgress</c> 里，脚本无从读取。这里把"每个节点产出了什么"统一成一张表，
    /// 脚本、结果输出、后续的数据联动都可以从这里取，不必各写一套。
    /// </para>
    /// </summary>
    public sealed class NodeOutput
    {
        /// <summary>节点 Id。</summary>
        public string Id = "";
        /// <summary>节点名（用户可改；脚本里优先用 Id，名字可能重名）。</summary>
        public string Name = "";
        /// <summary>节点类型名（如 ShapeMatch / Ocr）。</summary>
        public string Type = "";

        /// <summary>执行状态。</summary>
        public FlowNodeStatus Status = FlowNodeStatus.Pending;

        /// <summary>结果摘要原文（与结果区同源）。</summary>
        public string Summary = "";

        /// <summary>本节点耗时（毫秒）。</summary>
        public int ElapsedMs;

        // ---- 常用主判据（摊平，脚本里 api.Get("x").TargetCount 直接可读）----
        /// <summary>目标数量（BLOB / 匹配 / OCR 字符数）。</summary>
        public int TargetCount;
        /// <summary>最高分（形状匹配 / 模板匹配）。</summary>
        public double TopScore;
        /// <summary>最大面积（BLOB）。</summary>
        public double MaxArea;
        /// <summary>最大圆度（BLOB）。</summary>
        public double MaxCircularity;
        /// <summary>标量值（卡尺长度 / 直径 / 清晰度 / 颜色占比）。</summary>
        public double Value;
        /// <summary>文本（OCR 识别结果 / 收发数据）。</summary>
        public string Text = "";

        /// <summary>是否有位姿（匹配类 / 卡尺输出）。</summary>
        public bool HasPose;
        /// <summary>位姿 X。</summary>
        public double Cx;
        /// <summary>位姿 Y。</summary>
        public double Cy;
        /// <summary>位姿角度（度）。</summary>
        public double Angle;
        /// <summary>位姿缩放。</summary>
        public double Scale = 1.0;

        /// <summary>产出图形数量（叠加层里的目标框 / 边线等）。</summary>
        public int ShapeCount;

        /// <summary>是否成功（Ok 或 Warn；Ng / Skipped 为 false）。</summary>
        public bool Ok
        {
            get { return Status == FlowNodeStatus.Ok || Status == FlowNodeStatus.Warn; }
        }
    }

    /// <summary>
    /// 脚本节点的运行时上下文。<strong>脚本可以读取所有上游节点的输出</strong>，并把结果写回。
    /// <para>
    /// 用法（脚本就是方法体，直接写 C#）：
    /// <code>
    /// var m = api.Get("形状匹配1");
    /// if (m == null || !m.Ok) { api.Ok = false; return "未定位"; }
    /// api.Number = m.TopScore;
    /// api.Text = "X=" + m.Cx.ToString("F1");
    /// return api.Text;
    /// </code>
    /// </para>
    /// </summary>
    public sealed class ScriptApi
    {
        private readonly List<NodeOutput> _nodes;

        internal ScriptApi(List<NodeOutput> nodes, int imageWidth, int imageHeight)
        {
            _nodes = nodes ?? new List<NodeOutput>();
            ImageWidth = imageWidth;
            ImageHeight = imageHeight;
        }

        /// <summary>本轮所有已执行节点的输出（按执行顺序）。</summary>
        public List<NodeOutput> Nodes { get { return _nodes; } }

        /// <summary>当前工作帧宽（无图像时 0）。</summary>
        public int ImageWidth { get; private set; }

        /// <summary>当前工作帧高（无图像时 0）。</summary>
        public int ImageHeight { get; private set; }

        // ---- 脚本写回 ----
        /// <summary>结果文本（进结果区摘要）。</summary>
        public string Text = "";
        /// <summary>结果数值（进主判据）。</summary>
        public double Number;
        /// <summary>是否通过（默认 true；写 false 会让节点判 NG）。</summary>
        public bool Ok = true;

        /// <summary>按 Id 或名称取节点输出；找不到返回 null（脚本里必须判空）。</summary>
        public NodeOutput Get(string idOrName)
        {
            if (string.IsNullOrEmpty(idOrName)) return null;

            // Id 精确匹配优先，其次名称（重名时取最后一个执行的）
            for (int i = _nodes.Count - 1; i >= 0; i--)
                if (string.Equals(_nodes[i].Id, idOrName, StringComparison.Ordinal)) return _nodes[i];

            for (int i = _nodes.Count - 1; i >= 0; i--)
                if (string.Equals(_nodes[i].Name, idOrName, StringComparison.Ordinal)) return _nodes[i];

            return null;
        }

        /// <summary>按类型取第一个匹配的节点输出（如 "Ocr"）；没有返回 null。</summary>
        public NodeOutput First(string nodeType)
        {
            if (string.IsNullOrEmpty(nodeType)) return null;

            for (int i = 0; i < _nodes.Count; i++)
                if (string.Equals(_nodes[i].Type, nodeType, StringComparison.OrdinalIgnoreCase))
                    return _nodes[i];

            return null;
        }

        /// <summary>取节点数值字段；节点不存在返回 <paramref name="fallback"/>。</summary>
        public double NumberOf(string idOrName, double fallback)
        {
            NodeOutput o = Get(idOrName);
            return o == null ? fallback : o.Value;
        }
    }

    /// <summary>
    /// 脚本编译器：把用户脚本（C# 方法体）编译成可复用的委托，按脚本内容缓存。
    /// <para>
    /// 【为什么用 CodeDom 而不是引 Roslyn】本工程是 .NET Framework 4.8 + 传统 csproj，
    /// CodeDom（<c>CSharpCodeProvider</c>）随框架自带，不需要额外 NuGet；代价是首次编译慢
    /// （几十~几百毫秒），所以按脚本哈希缓存 Assembly，改一次才重编一次。
    /// </para>
    /// <para>
    /// <strong>安全边界</strong>：脚本以当前用户权限在进程内执行，可访问文件/网络。
    /// 这是"用户自己的配方"，不是沙箱；不要在不可信来源的流程包里使用未知脚本。
    /// </para>
    /// </summary>
    public static class ScriptEngine
    {
        private const string WrapperHead =
            "using System;\n" +
            "using System.Collections.Generic;\n" +
            "using System.Linq;\n" +
            "using System.Text;\n" +
            "using VisionPlatform.Flow;\n" +
            "public static class __VsScript {\n" +
            "  public static string Run(ScriptApi api) {\n";

        private const string WrapperTail = "\n  }\n}";

        private sealed class Compiled
        {
            public MethodInfo Entry;
            public string Error;
        }

        private static readonly object _sync = new object();
        private static readonly Dictionary<string, Compiled> _cache =
            new Dictionary<string, Compiled>(StringComparer.Ordinal);

        /// <summary>清空编译缓存（改代码/排查时用）。</summary>
        public static void ClearCache()
        {
            lock (_sync) _cache.Clear();
        }

        /// <summary>
        /// 执行脚本。<paramref name="error"/> 非空表示编译/运行失败（节点应判 NG）。
        /// </summary>
        public static string Run(string script, ScriptApi api, out string error)
        {
            error = "";
            if (string.IsNullOrWhiteSpace(script))
            {
                error = "脚本为空";
                return "";
            }

            MethodInfo entry;
            string compileError;
            if (!TryCompile(script, out entry, out compileError))
            {
                error = compileError;
                return "";
            }

            try
            {
                object ret = entry.Invoke(null, new object[] { api });
                return ret == null ? "" : ret.ToString();
            }
            catch (TargetInvocationException ex)
            {
                Exception inner = ex.InnerException ?? ex;
                error = "脚本运行异常：" + inner.GetType().Name + " " + inner.Message;
                return "";
            }
            catch (Exception ex)
            {
                error = "脚本运行异常：" + ex.GetType().Name + " " + ex.Message;
                return "";
            }
        }

        private static bool TryCompile(string script, out MethodInfo entry, out string error)
        {
            entry = null;
            error = "";

            string key = script;   // 脚本即键（内容相同就复用）

            lock (_sync)
            {
                Compiled c;
                if (_cache.TryGetValue(key, out c))
                {
                    if (c.Entry != null) { entry = c.Entry; return true; }
                    error = c.Error;
                    return false;
                }
            }

            try
            {
                using (var provider = new CSharpCodeProvider())
                {
                    var cp = new CompilerParameters
                    {
                        GenerateInMemory = true,
                        GenerateExecutable = false,
                        IncludeDebugInformation = false
                    };
                    cp.ReferencedAssemblies.Add("System.dll");
                    cp.ReferencedAssemblies.Add("System.Core.dll");
                    cp.ReferencedAssemblies.Add("System.Data.dll");
                    cp.ReferencedAssemblies.Add(typeof(ScriptEngine).Assembly.Location);

                    string source = WrapperHead + script + WrapperTail;
                    CompilerResults r = provider.CompileAssemblyFromSource(cp, source);

                    if (r.Errors.HasErrors)
                    {
                        var sb = new StringBuilder();
                        int shown = 0;
                        foreach (CompilerError ce in r.Errors)
                        {
                            if (ce.IsWarning) continue;
                            if (shown++ >= 3) { sb.Append("；…"); break; }
                            if (shown > 1) sb.Append("；");
                            sb.Append("第 ").Append(ce.Line - 6).Append(" 行：").Append(ce.ErrorText);
                        }
                        if (shown == 0) sb.Append("编译失败（无错误详情）");

                        lock (_sync) _cache[key] = new Compiled { Error = sb.ToString() };
                        error = sb.ToString();
                        return false;
                    }

                    Type t = r.CompiledAssembly.GetType("__VsScript");
                    entry = t == null ? null : t.GetMethod("Run", BindingFlags.Public | BindingFlags.Static);
                    if (entry == null)
                    {
                        error = "脚本编译成功但找不到入口（内部错误）";
                        lock (_sync) _cache[key] = new Compiled { Error = error };
                        return false;
                    }

                    lock (_sync) _cache[key] = new Compiled { Entry = entry };
                    return true;
                }
            }
            catch (Exception ex)
            {
                error = "脚本编译失败：" + ex.GetType().Name + " " + ex.Message;
                lock (_sync) _cache[key] = new Compiled { Error = error };
                return false;
            }
        }

        /// <summary>脚本编辑器里显示的示例（新建脚本节点时的默认内容）。</summary>
        public const string SampleScript =
            "// api.Nodes 是本轮所有已执行节点的输出；Get 支持 Id 或节点名\n" +
            "var m = api.First(\"ShapeMatch\");\n" +
            "if (m == null || !m.Ok) { api.Ok = false; return \"未定位\"; }\n" +
            "api.Number = m.TopScore;\n" +
            "return \"分数 \" + m.TopScore.ToString(\"F3\") + \" @ (\" + m.Cx.ToString(\"F1\") + \",\" + m.Cy.ToString(\"F1\") + \")\";";
    }
}
