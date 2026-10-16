using System;
using System.Collections.Generic;
using System.Windows.Forms;

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
                if (IsSelfDrawn(c))
                {
                    // 自绘控件的静态文本由它自己重绘，改 Text 没用，整棵子树跳过。
                    // 但它身上挂的右键菜单是普通 ToolStrip，必须单独补一趟
                    // （见 ApplySelfDrawnMenu 的说明）。
                    ApplySelfDrawnMenu(c);
                    continue;
                }

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

            // 右键菜单是 Component，同样不在 Controls 里，也要单独翻一趟
            if (c.ContextMenuStrip != null)
                ApplyStripItems(c.ContextMenuStrip.Items, scope);

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
        /// <para>
        /// ⚠ 本名单与 <c>UiStyle</c>（<c>UI/UiStyle.cs</c>）里的同名判断是<strong>两份拷贝</strong>，
        /// 将来新增/移除自绘控件时<strong>必须同步改两处</strong>，否则会出现
        /// 「主题套上了但语言没翻」或反之的诡异现象。
        /// 之所以不直接调用 <c>UiStyle.IsSelfDrawn</c>：它是 private，且 Core 层
        /// 不应反向依赖 UI 层（本项目 Core 是 AppPaths / Logger / AppSettings 这类基础层）。
        /// </para>
        /// </summary>
        private static bool IsSelfDrawn(Control c)
        {
            if (c == null) return false;

            Type t = c.GetType();
            string ns = t.Namespace;

            // VisionSystem.Controls（ImageViewer 等）：整个命名空间都是自绘
            if (ns == "VisionSystem.Controls") return true;

            // VisionPlatform.UI 里的自绘控件与自绘面板
            // （按类型名比对而非硬引用 UiButton，同样是为了不产生 Core → UI 的依赖）
            if (ns == "VisionPlatform.UI" &&
                (t.Name == "UiButton" || t.Name == "FlowCanvasControl" ||
                 t.Name == "FlowPreviewPanel" || t.Name == "NodeParamPanel" ||
                 t.Name == "UiTabControl"))
                return true;

            return false;
        }

        /// <summary>
        /// 自绘控件身上的右键菜单（<c>ContextMenuStrip</c>）补翻一趟。
        /// <para>
        /// 【为什么需要它】<see cref="ApplyTree"/> 在 <see cref="IsSelfDrawn"/> 处直接
        /// <c>continue</c>，不会下探，所以自绘控件挂的菜单项永远翻不到。而
        /// <c>ImageViewer</c>（<c>MainForm</c> 的 4 个视图 / <c>AlgoForm</c> /
        /// <c>CameraForm</c> / <c>FlowPreviewPanel</c> 共 7 个实例）自带 4 条中文菜单
        /// ——保存图像、保存视图截图、自适应图像、显示辅助十字线。
        /// </para>
        /// <para>
        /// 【为什么用反射而不是硬引用】<c>ImageViewer</c> <strong>从不给基类
        /// <c>Control.ContextMenuStrip</c> 赋值</strong>，它自己在
        /// <c>ImageViewer.cs</c> 的右键处理里手动 <c>_contextMenu.Show(...)</c>，
        /// 因此通用遍历（<see cref="ApplyOne"/> 里那个）拿不到它。
        /// 这里退一步：对自绘控件反射找一个返回 <c>ContextMenuStrip</c> 的公开
        /// 实例属性（<c>ImageViewer.ViewerContextMenu</c> 正是这种），
        /// 既避免了 Core → VisionSystem.Controls 的反向依赖，将来新增自绘控件
        /// 只要也按这个约定暴露属性即可，无需再改 Lang。
        /// </para>
        /// <para>
        /// 【scope】用控件类型名（如 "ImageViewer"），而不是窗体名 —— 这类菜单是
        /// 控件库自带、跨窗体共享的，按控件归类更合理，也只需要在表里写一遍。
        /// </para>
        /// </summary>
        private static void ApplySelfDrawnMenu(Control c)
        {
            if (c == null) return;

            ContextMenuStrip menu = FindContextMenu(c);
            if (menu != null) ApplyStripItems(menu.Items, c.GetType().Name);
        }

        /// <summary>
        /// 在自绘控件上找它自带的右键菜单：优先用基类 <c>Control.ContextMenuStrip</c>，
        /// 没挂的话再反射找一个返回 <c>ContextMenuStrip</c> 的公开实例属性
        /// （如 <c>ImageViewer.ViewerContextMenu</c>）。都找不到返回 null。
        /// <para>
        /// 只在自绘控件上调用（数量很少），且只在语言切换时走一次，反射开销可忽略。
        /// </para>
        /// </summary>
        private static ContextMenuStrip FindContextMenu(Control c)
        {
            if (c.ContextMenuStrip != null) return c.ContextMenuStrip;

            try
            {
                foreach (System.Reflection.PropertyInfo p in c.GetType().GetProperties(
                             System.Reflection.BindingFlags.Public |
                             System.Reflection.BindingFlags.Instance))
                {
                    if (p.PropertyType != typeof(ContextMenuStrip)) continue;
                    if (!p.CanRead) continue;
                    if (p.GetIndexParameters().Length != 0) continue;   // 跳过索引器

                    // 必须判非空再返回：继承来的 Control.ContextMenuStrip 也是
                    // ContextMenuStrip 类型但值是 null，若直接 return 会漏掉
                    // 控件自己暴露的那个菜单（枚举顺序不保证）。
                    ContextMenuStrip m = p.GetValue(c, null) as ContextMenuStrip;
                    if (m != null) return m;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("读取自绘控件右键菜单失败：" + ex.Message);
            }

            return null;
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

            // ---- 自绘控件自带右键菜单（ApplySelfDrawnMenu 第二趟用）----
            // key 前缀取控件类型名而非窗体名：ImageViewer 的菜单是控件库自带、
            // 跨窗体共享（MainForm 四视图 / AlgoForm / CameraForm / FlowPreviewPanel），
            // 按控件归类只需在表里写一遍。菜单项 Name 在 ImageViewer.cs 里显式指定。
            _ui["ImageViewer._menuSaveImage"] = new[] { "保存图像(&S)", "Save Image(&S)" };
            _ui["ImageViewer._menuSaveImage.ToolTipText"] =
                new[] { "把当前图像（不含图形叠加）保存为文件", "Save the current image (without overlays) to a file" };
            _ui["ImageViewer._menuSaveSnapshot"] = new[] { "保存视图截图(&V)", "Save Snapshot(&V)" };
            _ui["ImageViewer._menuFitImage"] = new[] { "自适应图像(&F)", "Fit Image(&F)" };
            _ui["ImageViewer._menuCrosshair"] = new[] { "显示辅助十字线(&C)", "Show Crosshair(&C)" };
        }
    }
}
