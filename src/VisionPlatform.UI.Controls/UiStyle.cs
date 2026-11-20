using System;
using System.Drawing;
using System.Windows.Forms;

namespace VisionPlatform.UI
{
    /// <summary>
    /// 整窗体主题套用：递归遍历控件树，把标准 WinForms 控件的配色换成
    /// <see cref="UiTheme"/> 的深色工程配色。
    /// <para>
    /// 【为什么需要它】MainForm 有手写的 <c>ApplyTheme()</c>，但 AlgoForm / CameraForm /
    /// SettingsForm / FlowForm / RecipeForm / CommForm / LoginForm 完全没有接入主题，
    /// 仍是系统浅色控件，与深色主界面放在一起非常突兀。
    /// 逐窗体手写 ApplyTheme 成本高且易漏，故做一个递归兜底：一行调用即整窗体统一。
    /// </para>
    /// <para>
    /// 【不做的事】自绘控件（<see cref="UiButton"/>、ImageViewer、FlowCanvasControl、
    /// FlowPreviewPanel、NodeParamPanel、<see cref="UiTitleBar"/>）自己管理配色，
    /// 本类一律跳过，避免覆盖；
    /// 语义色（OK/NG 徽标、统计数值、分组标题）由各窗体在调用之后自行覆盖，
    /// 后写者生效，因此调用点必须放在构造函数末尾、业务样式之前。
    /// </para>
    /// </summary>
    public static class UiStyle
    {
        /// <summary>
        /// 是否<strong>无条件</strong>覆盖控件底色（忽略 <c>IsSystemColor</c> 门）。
        /// <para>默认 false —— 深色模式下行为与历史版本逐位相同（只改系统默认色，
        /// Designer 写死的深色一律保留）。</para>
        /// <para>【为什么必须有它】浅色主题下，Designer 里写死的深色（如
        /// <c>Color.FromArgb(30,30,38)</c> 的输入框）在浅底上会变成一块黑斑，
        /// 只有强制覆盖才能把它们一起翻成浅色。深色模式严禁置 true。</para>
        /// </summary>
        public static bool ForceOverride { get; set; }

        /// <summary>套用整窗体（含所有子控件）。应在构造函数末尾调用一次。</summary>
        public static void ApplyForm(Form form)
        {
            if (form == null) return;

            form.BackColor = UiTheme.ColorAppBg;
            form.ForeColor = UiTheme.ColorTextPrimary;
            // 刻意不改 form.Font：Designer 多为 AutoScaleMode.Font，运行时改字体会
            // 触发整窗重新缩放，把设计好的布局撑变形。子控件字体逐个覆盖即可。

            ApplyTree(form);
        }

        /// <summary>只套用一个容器及其子树（用于动态加载的面板）。</summary>
        public static void ApplyTree(Control root)
        {
            if (root == null) return;

            foreach (Control c in root.Controls)
            {
                // TabControl 自身的标签条由 UiTabControl 自绘（或系统绘制），但其分页
                // （TabPage）内的子控件仍是普通 WinForms 控件（输入框 / 数值框 / 按钮等），
                // 必须继续下探套用主题，否则分页里的控件会残留系统浅色。
                // 注意：UiTabControl 被 IsSelfDrawn 整棵跳过，故在此特判，不要让它走到 continue。
                TabControl tc = c as TabControl;
                if (tc != null)
                {
                    foreach (TabPage page in tc.TabPages)
                    {
                        UiTabControl.StylePage(page);
                        ApplyTree(page);
                    }
                    continue;
                }

                if (IsSelfDrawn(c)) continue;

                ApplyOne(c);

                // 容器继续下探（无论是否命中样式白名单，子树都要走到）
                if (c.HasChildren) ApplyTree(c);
            }
        }

        /// <summary>
        /// 自绘控件白名单之外的判断：这些控件自己接管绘制，外部改 BackColor 无效
        /// 甚至会把画布底色改坏，必须整棵子树跳过。
        /// </summary>
        private static bool IsSelfDrawn(Control c)
        {
            if (c is UiButton) return true;

            // VisionSystem.Controls（ImageViewer 等）与 Flow 系列自绘面板
            string ns = c.GetType().Namespace;
            if (!string.IsNullOrEmpty(ns) && ns == "VisionSystem.Controls") return true;

            Type t = c.GetType();
            if (t.Namespace == "VisionPlatform.UI" &&
                (t.Name == "FlowCanvasControl" || t.Name == "FlowPreviewPanel" ||
                 t.Name == "NodeParamPanel" || t.Name == "ParamListControl" ||
                 t.Name == "UiTabControl" || t.Name == "UiTitleBar"))
                return true;

            return false;
        }

        private static void ApplyOne(Control c)
        {
            // ---------- 容器 ----------
            Panel panel = c as Panel;
            if (panel != null)
            {
                if (NeedOverride(panel.BackColor)) panel.BackColor = UiTheme.ColorPanelBg;
                panel.ForeColor = UiTheme.ColorTextPrimary;
                return;
            }

            GroupBox group = c as GroupBox;
            if (group != null)
            {
                if (NeedOverride(group.BackColor)) group.BackColor = UiTheme.ColorPanelBg;
                group.ForeColor = UiTheme.ColorTextPrimary;
                group.Font = UiTheme.FontBody;
                return;
            }

            SplitContainer split = c as SplitContainer;
            if (split != null)
            {
                split.BackColor = UiTheme.ColorAppBg;
                split.ForeColor = UiTheme.ColorTextPrimary;
                return;
            }

            TabControl tabs = c as TabControl;
            if (tabs != null)
            {
                foreach (TabPage page in tabs.TabPages) UiTabControl.StylePage(page);
                return;
            }

            // ---------- 文本 ----------
            Label label = c as Label;
            if (label != null)
            {
                label.ForeColor = UiTheme.ColorTextPrimary;
                label.Font = UiTheme.FontBody;
                // 只把「未显式设色」的标签底色统一；已设色（徽标 / 分组标题）保持不动
                if (NeedOverride(label.BackColor) || label.BackColor == Color.Transparent)
                    label.BackColor = UiTheme.ColorPanelBg;
                return;
            }

            // ---------- 输入 ----------
            TextBox text = c as TextBox;
            if (text != null)
            {
                if (NeedOverride(text.BackColor)) text.BackColor = UiTheme.ColorCardBg;
                text.ForeColor = UiTheme.ColorTextPrimary;
                text.Font = UiTheme.FontBody;
                text.BorderStyle = BorderStyle.FixedSingle;
                return;
            }

            ComboBox combo = c as ComboBox;
            if (combo != null)
            {
                if (NeedOverride(combo.BackColor)) combo.BackColor = UiTheme.ColorCardBg;
                combo.ForeColor = UiTheme.ColorTextPrimary;
                combo.Font = UiTheme.FontBody;
                if (combo.DropDownStyle != ComboBoxStyle.Simple)
                    combo.FlatStyle = FlatStyle.Flat;
                return;
            }

            NumericUpDown numeric = c as NumericUpDown;
            if (numeric != null)
            {
                if (NeedOverride(numeric.BackColor)) numeric.BackColor = UiTheme.ColorCardBg;
                numeric.ForeColor = UiTheme.ColorTextPrimary;
                numeric.Font = UiTheme.FontBody;
                numeric.BorderStyle = BorderStyle.FixedSingle;
                // 顺带套用数值框全局策略（禁滚轮改值 + 步长按值语义统一）。
                // 走主题遍历的窗体都在这里覆盖到；被 IsSelfDrawn 跳过的子树
                // （ParamListControl 等）在自己的控件创建点单独调用，滚轮另有全局过滤器兜底。
                NumericInputPolicy.ApplyTo(numeric);
                return;
            }

            CheckBox check = c as CheckBox;
            if (check != null)
            {
                check.ForeColor = UiTheme.ColorTextPrimary;
                check.Font = UiTheme.FontBody;
                if (NeedOverride(check.BackColor)) check.BackColor = UiTheme.ColorPanelBg;
                return;
            }

            RadioButton radio = c as RadioButton;
            if (radio != null)
            {
                radio.ForeColor = UiTheme.ColorTextPrimary;
                radio.Font = UiTheme.FontBody;
                if (NeedOverride(radio.BackColor)) radio.BackColor = UiTheme.ColorPanelBg;
                return;
            }

            // ---------- 按钮（普通 Button；UiButton 已跳过）----------
            Button btn = c as Button;
            if (btn != null)
            {
                btn.FlatStyle = FlatStyle.Flat;
                btn.FlatAppearance.BorderSize = 0;
                if (NeedOverride(btn.BackColor)) btn.BackColor = UiTheme.ColorCardBg;
                btn.ForeColor = UiTheme.ColorTextPrimary;
                btn.Font = UiTheme.FontButton;
                btn.UseVisualStyleBackColor = false;
                return;
            }

            // ---------- 列表 / 表格 ----------
            ListBox list = c as ListBox;
            if (list != null)
            {
                if (NeedOverride(list.BackColor)) list.BackColor = UiTheme.ColorViewBg;
                list.ForeColor = UiTheme.ColorTextPrimary;
                list.Font = UiTheme.FontBody;
                list.BorderStyle = BorderStyle.FixedSingle;
                return;
            }

            // TreeView：工具箱分类树 / 输入输出端子树（§11.2 §11.6）—— 交给 UiTree 统一自绘
            TreeView tree = c as TreeView;
            if (tree != null)
            {
                UiTree.Apply(tree);
                return;
            }

            DataGridView grid = c as DataGridView;
            if (grid != null)
            {
                UiGrid.Apply(grid);
                return;
            }

            // ---------- 工具条 / 菜单 / 状态条 ----------
            ToolStrip strip = c as ToolStrip;
            if (strip != null)
            {
                strip.Renderer = new UiToolStripRenderer();
                strip.BackColor = UiTheme.ColorHeaderBg;
                strip.ForeColor = UiTheme.ColorTextPrimary;
                strip.Font = UiTheme.FontToolbar;
                foreach (ToolStripItem item in strip.Items)
                {
                    item.ForeColor = UiTheme.ColorTextPrimary;
                    if (!(item is ToolStripLabel))
                        item.Font = UiTheme.FontToolbar;
                }
                return;
            }

            StatusStrip status = c as StatusStrip;
            if (status != null)
            {
                status.BackColor = UiTheme.ColorHeaderBg;
                status.ForeColor = UiTheme.ColorTextSecondary;
                return;
            }

            // ---------- 图像框：只补底色 ----------
            PictureBox picture = c as PictureBox;
            if (picture != null)
            {
                if (NeedOverride(picture.BackColor)) picture.BackColor = UiTheme.ColorViewBg;
                return;
            }
        }

        /// <summary>
        /// 是否需要覆盖该底色：<see cref="ForceOverride"/> 为真时一律覆盖，
        /// 否则只覆盖"设计器没指定、沿用系统默认"的颜色。
        /// </summary>
        private static bool NeedOverride(Color c)
        {
            return ForceOverride || IsSystemColor(c);
        }

        /// <summary>
        /// 判断是否为"设计器没指定、沿用系统默认"的颜色。
        /// 只有这类颜色才被主题覆盖 —— 设计器里刻意写死的深色（标题条、模块列表等）
        /// 一律保留，避免把已有的层次感抹平。
        /// </summary>
        private static bool IsSystemColor(Color c)
        {
            if (!c.IsKnownColor) return false;
            KnownColor k = c.ToKnownColor();
            return k == KnownColor.Control
                || k == KnownColor.ControlLight
                || k == KnownColor.ControlLightLight
                || k == KnownColor.ControlDark
                || k == KnownColor.Window
                || k == KnownColor.ButtonFace
                || k == KnownColor.Desktop;
        }
    }
}
