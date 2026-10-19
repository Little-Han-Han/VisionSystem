using System;
using System.Windows.Forms;
using VisionPlatform.Core;

namespace VisionPlatform.UI
{
    /// <summary>
    /// 主题切换的唯一入口：换色板 → 重套所有已打开窗体 →（可选）落盘。
    /// <para>
    /// 【为什么需要它】<see cref="UiTheme.SetMode"/> 只改静态色板，已经画在屏幕上的控件
    /// 不会自己变；而「该递归哪些控件、哪些自绘控件要单独点名」这套顺序只有一处该知道，
    /// 散在各窗体里必然漏。故收敛为 Apply / SaveAndApply 两个方法。
    /// </para>
    /// <para>
    /// 【顺序铁律】先换色板重套，<strong>最后</strong>才 <c>ConfigStore.Save()</c>：
    /// Save 会广播 <c>ConfigStore.Changed</c>（MainForm.OnConfigChanged 等），
    /// 反过来会再刷新一次界面，放在重套之后可保证只播一次、界面已是新色。
    /// </para>
    /// </summary>
    public static class UiThemeManager
    {
        /// <summary>
        /// 切换主题并立即重套所有已打开窗体，<strong>不落盘</strong>（用于设置窗体里的即时预览）。
        /// </summary>
        public static void Apply(UiThemeMode mode)
        {
            UiTheme.SetMode(mode);

            // 浅色必须强制覆盖：Designer 里写死的深色控件在浅底上会是一块黑斑。
            // 深色必须严格保持 false，否则会抹平 Designer 刻意写出的层次感（历史行为回归）。
            UiStyle.ForceOverride = UiTheme.IsLight;

            WriteToConfig(mode);

            ReapplyOpenForms();
        }

        /// <summary>
        /// 由配置字符串解析主题模式：只有 "Light"（大小写不敏感）算浅色，其余一律深色。
        /// 未知值不抛异常，保证手改坏配置也能正常启动。
        /// </summary>
        public static UiThemeMode ParseMode(string value)
        {
            return string.Equals(value, "Light", StringComparison.OrdinalIgnoreCase)
                ? UiThemeMode.Light
                : UiThemeMode.Dark;
        }

        /// <summary>切换主题 → 重套 → 持久化到配置文件（用户点「保存」时走这条）。</summary>
        public static void SaveAndApply(UiThemeMode mode)
        {
            Apply(mode);
            ConfigStore.Save();
        }

        /// <summary>把当前模式写进内存配置（不落盘，落盘交给 <see cref="SaveAndApply"/>）。</summary>
        private static void WriteToConfig(UiThemeMode mode)
        {
            try
            {
                AppConfig cfg = ConfigStore.Current;
                if (cfg == null || cfg.Ui == null) return;

                cfg.Ui.Theme = (mode == UiThemeMode.Light) ? "Light" : UiSettings.DefaultTheme;
            }
            catch (Exception)
            {
                // 配置不可用时也不应影响界面切换
            }
        }

        /// <summary>重套所有已打开窗体。遍历顺序即 Z 序，父窗体先于子窗体。</summary>
        private static void ReapplyOpenForms()
        {
            for (int i = 0; i < Application.OpenForms.Count; i++)
            {
                Form f = Application.OpenForms[i];
                if (f == null) continue;

                try
                {
                    Reapply(f);
                }
                catch (Exception ex)
                {
                    Logger.Warn("重套主题失败（" + f.Name + "）：" + ex.Message);
                }
            }
        }

        /// <summary>单个窗体的重套流程：递归兜底 → 各窗体的语义色 / 自绘控件补刀 → 重绘。</summary>
        private static void Reapply(Form f)
        {
            // ① 递归兜底：标准 WinForms 控件树（内部已覆盖 UiTree / UiGrid / ToolStrip 渲染器）
            UiStyle.ApplyTree(f);

            // ② MainForm：动态创建的视图 / 标题条 / 绑定下拉框不在静态树里，需单独补
            MainForm main = f as MainForm;
            if (main != null) main.ReapplyTheme();

            // ③ FlowForm：画布 / 预览 / 参数面板三个自绘控件在 IsSelfDrawn 跳过名单里
            FlowForm flow = f as FlowForm;
            if (flow != null) flow.RefreshTheme();

            // ④ 独立自绘控件兜底：跳过名单里的控件若不在上述两窗体内（例如嵌在别处）
            ReapplySelfDrawn(f);

            f.Invalidate(true);
            f.Refresh();
        }

        /// <summary>递归找出自绘控件并逐个点名重套（这些控件 ApplyTree 一律跳过）。</summary>
        private static void ReapplySelfDrawn(Control root)
        {
            foreach (Control c in root.Controls)
            {
                FlowCanvasControl canvas = c as FlowCanvasControl;
                if (canvas != null) { canvas.ApplyTheme(); continue; }

                FlowPreviewPanel preview = c as FlowPreviewPanel;
                if (preview != null) { preview.ApplyTheme(); continue; }

                NodeParamPanel param = c as NodeParamPanel;
                if (param != null) { param.ApplyTheme(); continue; }

                // 自绘标题栏：底色 / 文字色取自 UiTheme，换色板后必须重绘取新色
                UiTitleBar bar = c as UiTitleBar;
                if (bar != null) { bar.ApplyTheme(); continue; }

                if (c.HasChildren) ReapplySelfDrawn(c);
            }
        }
    }
}
