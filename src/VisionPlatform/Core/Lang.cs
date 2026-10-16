using System;
using System.Collections.Generic;
using System.Windows.Forms;
using VisionPlatform.UI;

namespace VisionPlatform.Core
{
    /// <summary>
    /// 中英文语言切换基础设施（zh-CN / en），支持<strong>运行时热切换</strong>、无需重启软件。
    /// <para>
    /// 【为什么不用 .resx / 卫星程序集】本项目要求运行时热切换（工业现场重启成本高：
    /// 要停相机、停流程）。而 resx 方案靠 <c>InitializeComponent</c> 里的一次性
    /// <c>ApplyResources</c> 生效，已经实例化过的窗体不会被重新刷文本，只有重新 new 出来
    /// 的窗体才会变 —— 达不到热切换要求。且项目一贯风格是「纯代码、零资源文件」
    /// （见 <see cref="UiTheme"/> 注释），故这里用两张 C# 静态对照表。
    /// </para>
    /// <para>
    /// 【两张表】
    /// <list type="bullet">
    /// <item><description><see cref="_text"/>：运行时字符串表，key = 中文原文，
    /// value = 英文。主力 API 是 <see cref="T(string)"/>，调用点把
    /// <c>"打开图片失败"</c> 包成 <c>Lang.T("打开图片失败")</c> 即可，
    /// 查不到就原样返回中文，机械、安全、不会漏。</description></item>
    /// <item><description><see cref="_ui"/>：控件静态文本表，key = <c>"窗体名.控件名"</c>
    /// （可选后缀 <c>.ToolTipText</c>；DataGridView 列头为 <c>"窗体名.列名.HeaderText"</c>），
    /// value = <c>string[]{ 中文, 英文 }</c>。由 <see cref="ApplyTo"/> 递归套用。
    /// 双向幂等：表里有中英两版，切英文再切回中文都能对上。</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// 【不做的事】不改任何控件的 <c>Font</c> —— Designer 多为 <c>AutoScaleMode.Font</c>，
    /// 运行时改字体会触发整窗重新缩放，把设计好的布局撑变形（见 <see cref="UiStyle"/> 顶部警告）。
    /// </para>
    /// </summary>
    public static class Lang
    {
        /// <summary>语言代码：简体中文。</summary>
        public const string ZhCN = "zh-CN";

        /// <summary>语言代码：English。</summary>
        public const string En = "en";

        /// <summary>运行时字符串对照表：中文原文 → 英文。</summary>
        private static readonly Dictionary<string, string> _text;

        /// <summary>控件静态文本对照表："窗体名.控件名[.ToolTipText]" → { 中文, 英文 }。</summary>
        private static readonly Dictionary<string, string[]> _ui;

        private static string _current = ZhCN;

        /// <summary>切换过程中置位，防止事件回调里再次进入 <see cref="SetLanguage"/>。</summary>
        private static bool _switching;

        /// <summary>当前语言代码，取值恒为 <see cref="ZhCN"/> 或 <see cref="En"/>。</summary>
        public static string Current
        {
            get { return _current; }
        }

        /// <summary>当前是否为英文。</summary>
        public static bool IsEnglish
        {
            get { return _current == En; }
        }

        /// <summary>语言切换后触发（界面据此刷新文本）。主线程触发。</summary>
        public static event EventHandler LanguageChanged;

        static Lang()
        {
            _text = new Dictionary<string, string>();
            _ui = new Dictionary<string, string[]>();
            FillTables();
        }

        /// <summary>
        /// 切换语言。幂等：与当前语言相同则什么都不做（不触发事件）。合法值之外的输入
        /// （null / 空串 / 未知代码）归一化为 <see cref="ZhCN"/>。
        /// </summary>
        /// <param name="code">语言代码，<see cref="ZhCN"/> 或 <see cref="En"/>。</param>
        public static void SetLanguage(string code)
        {
            string next = Normalize(code);

            // 防重入：切换过程中触发的事件回调可能再次调用本方法
            if (_switching) return;
            if (next == _current) return;

            _switching = true;
            try
            {
                _current = next;
            }
            finally
            {
                _switching = false;
            }

            Logger.Info("界面语言切换为：" + _current);

            // 标准写法：先取快照，再判空，再触发（避免判空后到触发之间被 unsubscribe）
            EventHandler handler = LanguageChanged;
            if (handler != null) handler(null, EventArgs.Empty);
        }

        /// <summary>
        /// 翻译一段<strong>代码内</strong>的字符串（主力 API）。
        /// 中文模式下原样返回；英文模式下查 <see cref="_text"/>，查不到仍返回中文原文。
        /// </summary>
        /// <param name="zh">中文原文（同时作为查表 key）。</param>
        /// <returns>当前语言下的文本。</returns>
        public static string T(string zh)
        {
            if (!IsEnglish) return zh;
            if (string.IsNullOrEmpty(zh)) return zh;

            string en;
            return _text.TryGetValue(zh, out en) && !string.IsNullOrEmpty(en) ? en : zh;
        }

        /// <summary>
        /// 翻译一段<strong>代码内</strong>的字符串，并允许调用点内联一份英文兜底。
        /// 表里<strong>有</strong>条目时以表为准（方便后续集中修正），表里没有时才用
        /// <paramref name="en"/>；两者都没有则退回中文原文。
        /// </summary>
        /// <param name="zh">中文原文（同时作为查表 key）。</param>
        /// <param name="en">内联英文兜底。</param>
        /// <returns>当前语言下的文本。</returns>
        public static string T(string zh, string en)
        {
            if (!IsEnglish) return zh;

            string mapped;
            if (_text.TryGetValue(zh, out mapped) && !string.IsNullOrEmpty(mapped)) return mapped;
            return string.IsNullOrEmpty(en) ? zh : en;
        }

        /// <summary>
        /// 把控件静态文本对照表套用到整个窗体（递归含子容器、ToolStrip 项、DataGridView 列头）。
        /// 应在窗体构造函数末尾调用一次；之后由 <see cref="LanguageChanged"/> 事件驱动再次调用。
        /// </summary>
        /// <param name="form">目标窗体。</param>
        public static void ApplyTo(Form form)
        {
            if (form == null) return;

            // 表名前缀取窗体 Name（Designer 里形如 this.Name = "MainForm"）
            string scope = form.Name;
            if (string.IsNullOrEmpty(scope)) scope = form.GetType().Name;

            ApplyTree(form, scope);
        }

        // ====================================================================
        //  内部实现
        // ====================================================================

        private static void ApplyTree(Control root, string scope)
        {
            if (root == null) return;

            foreach (Control c in root.Controls)
            {
                if (IsSelfDrawn(c)) continue;   // 自绘控件整棵子树跳过

                ApplyOne(c, scope);

                // 容器继续下探（TabControl.TabPages / SplitContainer.Panel1,2 /
                // GroupBox / Panel 都在 Controls 里，标准递归即可）
                if (c.HasChildren) ApplyTree(c, scope);
            }
        }

        private static void ApplyOne(Control c, string scope)
        {
            if (!string.IsNullOrEmpty(c.Name))
            {
                string s = Lookup(scope + "." + c.Name);
                if (s != null) c.Text = s;
            }

            // ToolStripItem（菜单项 / 工具条按钮 / 状态条标签）不是 Control，
            // 不在 Controls 集合里，必须单独遍历 Items —— 与 UiStyle.cs 的
            // ToolStrip 分支同一处理思路。MenuStrip / StatusStrip 都是 ToolStrip 子类。
            ToolStrip strip = c as ToolStrip;
            if (strip != null) ApplyStripItems(strip.Items, scope);

            DataGridView grid = c as DataGridView;
            if (grid != null) ApplyGridColumns(grid, scope);

            // 刻意不动 ComboBox / ListBox 的 Items 集合：那些是运行时填充的业务数据，
            // 改了会误触发 SelectedIndexChanged。只改 Text / ToolTipText。
        }

        private static void ApplyStripItems(ToolStripItemCollection items, string scope)
        {
            if (items == null) return;

            foreach (ToolStripItem item in items)
            {
                if (item == null) continue;

                if (!string.IsNullOrEmpty(item.Name))
                {
                    string s = Lookup(scope + "." + item.Name);
                    if (s != null) item.Text = s;

                    string tip = Lookup(scope + "." + item.Name + ".ToolTipText");
                    if (tip != null) item.ToolTipText = tip;
                }

                // 下拉子项（ToolStripDropDownButton / ToolStripSplitButton / 菜单项）
                ToolStripDropDownItem drop = item as ToolStripDropDownItem;
                if (drop != null && drop.HasDropDownItems) ApplyStripItems(drop.DropDownItems, scope);
            }
        }

        private static void ApplyGridColumns(DataGridView grid, string scope)
        {
            if (grid == null || grid.Columns == null) return;

            foreach (DataGridViewColumn col in grid.Columns)
            {
                if (col == null || string.IsNullOrEmpty(col.Name)) continue;

                string s = Lookup(scope + "." + col.Name + ".HeaderText");
                if (s != null) col.HeaderText = s;
            }
        }

        /// <summary>按 key 取当前语言下的控件文本；表里没有或缺项则返回 null（表示"不改"）。</summary>
        private static string Lookup(string key)
        {
            string[] pair;
            if (!_ui.TryGetValue(key, out pair)) return null;
            if (pair == null || pair.Length == 0) return null;

            int i = IsEnglish ? 1 : 0;
            if (i >= pair.Length) return null;

            string s = pair[i];
            return string.IsNullOrEmpty(s) ? null : s;
        }

        /// <summary>
        /// 归一化语言代码：null / 空白 / 未知代码一律回落 <see cref="ZhCN"/>；
        /// 任何以 en 开头（en、en-US、EN）都视为 <see cref="En"/>。
        /// </summary>
        private static string Normalize(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return ZhCN;

            string c = code.Trim().ToLowerInvariant();
            if (c.StartsWith("en")) return En;
            return ZhCN;
        }

        /// <summary>
        /// 自绘控件名单：这些控件自己接管绘制，遍历改 Text 没用甚至有害，整棵子树跳过。
        /// 名单与 <see cref="UiStyle.IsSelfDrawn"/> 保持一致。
        /// </summary>
        private static bool IsSelfDrawn(Control c)
        {
            if (c == null) return false;
            if (c is UiButton) return true;

            // VisionSystem.Controls（ImageViewer 等）与 Flow 系列自绘面板
            string ns = c.GetType().Namespace;
            if (!string.IsNullOrEmpty(ns) && ns == "VisionSystem.Controls") return true;

            Type t = c.GetType();
            if (t.Namespace == "VisionPlatform.UI" &&
                (t.Name == "FlowCanvasControl" || t.Name == "FlowPreviewPanel" ||
                 t.Name == "NodeParamPanel" || t.Name == "UiTabControl"))
                return true;

            return false;
        }

        // ====================================================================
        //  对照表（本轮仅放示例条目，后续批量填充）
        // --------------------------------------------------------------------
        //  填写约定：
        //    _text : key 必须是调用点里出现的中文原文，一字不差（含标点、空格）。
        //    _ui   : key = "窗体名.控件名"，value = new[]{ "中文", "English" } 两元素，
        //            顺序固定；某项留空表示该语言下"不改"，保持 Designer 原值。
        // ====================================================================

        private static void FillTables()
        {
            // ---- 运行时字符串（Lang.T 用）----
            _text["打开图片失败"] = "Failed to open image";
            _text["刷新"] = "Refresh";

            // ---- 控件静态文本（Lang.ApplyTo 用）----
            _ui["MainForm.tsBtnRefresh"] = new[] { "刷新", "Refresh" };
        }
    }
}
