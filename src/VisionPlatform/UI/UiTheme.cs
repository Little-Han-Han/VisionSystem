using System;
using System.Drawing;
using System.Windows.Forms;
using VisionPlatform.Core;

namespace VisionPlatform.UI
{
    // ==========================================================================
    //  工业 HMI 主题常量（纯代码，零资源文件、零第三方依赖）
    //
    //  【颜色语义遵循工业通用约定】OK=绿、NG=红、告警=橙、进行中=蓝。
    //  本项目为工业检测软件，不采用任何金融配色语义；任何结论表达都必须同时
    //  带文本（OK / NG 字面），不得仅靠颜色（色弱兜底，PRD C8）。
    //
    //  【单一来源】颜色 / 字体 / 尺寸常量只允许出现在本文件；业务窗体与面板
    //  一律引用这里的常量，禁止再硬写 Color.FromArgb(...) 与 new Font(...)。
    //  改一处即全局生效（PRD FR-UI-01 验收②）。
    //
    //  【Designer 分工】Designer 只写控件树与布局，颜色与字体在各窗体的
    //  ApplyTheme() 里统一套用，因此设计器中看不到真实配色（已知代价，接受）。
    // ==========================================================================

    /// <summary>按钮语义：决定 <see cref="UiTheme.StyleButton"/> 使用的配色。</summary>
    public enum UiButtonKind
    {
        /// <summary>启动运行（绿）。</summary>
        Start,

        /// <summary>暂停 / 继续（橙）。</summary>
        Pause,

        /// <summary>停止（红）。</summary>
        Stop,

        /// <summary>单帧采集（蓝）。</summary>
        Grab,

        /// <summary>清空统计（灰）。</summary>
        Reset,

        /// <summary>普通按钮（卡片底色 + 主文本色）。</summary>
        Neutral
    }

    /// <summary>结论三态：未判定 / OK / NG。</summary>
    public enum UiVerdict
    {
        /// <summary>未开始检测，显示灰底 "--"。</summary>
        None,

        /// <summary>合格，绿底白字 "OK"。</summary>
        Ok,

        /// <summary>不合格，红底白字 "NG"。</summary>
        Ng
    }

    /// <summary>统计数值的语义着色。</summary>
    public enum UiStatKind
    {
        /// <summary>普通数值（主文本色）。</summary>
        Normal,

        /// <summary>良品率（强调色 / 告警时橙或红）。</summary>
        Yield,

        /// <summary>OK 计数（绿）。</summary>
        Ok,

        /// <summary>NG 计数（红）。</summary>
        Ng,

        /// <summary>告警数值（橙）。</summary>
        Warn
    }

    /// <summary>
    /// 静态主题常量：13+ 语义色、7 个字体、4 px 栅格与关键尺寸，
    /// 以及 <c>StyleButton / StyleBadge / StyleStatValue / StyleGroupTitle</c> 等小工具。
    /// 字体均为懒构造的静态实例，避免大量 <see cref="Font"/> 对象；本类不持有
    /// 任何非托管资源的生命周期语义（Font 随进程存活，不 Dispose）。
    /// </summary>
    public static class UiTheme
    {
        // ---------------------- 语义色（13 项核心 + 2 项补充）----------------------

        /// <summary>应用底色（窗体背景）#26262E。</summary>
        public static readonly Color ColorAppBg = FromHex(0x26, 0x26, 0x2E);

        /// <summary>面板背景（右栏 / 参数面板）#2A2A32。</summary>
        public static readonly Color ColorPanelBg = FromHex(0x2A, 0x2A, 0x32);

        /// <summary>卡片背景（统计卡、输入区）#34343E。</summary>
        public static readonly Color ColorCardBg = FromHex(0x34, 0x34, 0x3E);

        /// <summary>标题栏 / 工具栏 / 分组标题底色 #1E1E26。</summary>
        public static readonly Color ColorHeaderBg = FromHex(0x1E, 0x1E, 0x26);

        /// <summary>图像区底色 #181820。</summary>
        public static readonly Color ColorViewBg = FromHex(0x18, 0x18, 0x20);

        /// <summary>分隔线 / 区块边框 #46464E。</summary>
        public static readonly Color ColorDivider = FromHex(0x46, 0x46, 0x4E);

        /// <summary>主文本（数值、正文）#DCE0E8。</summary>
        public static readonly Color ColorTextPrimary = FromHex(0xDC, 0xE0, 0xE8);

        /// <summary>次文本（标签、单位）#A0A6B2。</summary>
        public static readonly Color ColorTextSecondary = FromHex(0xA0, 0xA6, 0xB2);

        /// <summary>强调色（分区标题、选中态，深天蓝）#00A0E6。</summary>
        public static readonly Color ColorAccent = FromHex(0x00, 0xA0, 0xE6);

        /// <summary>OK：徽标 / OK 计数 / 成功（绿）#2ECC71 = RGB(46,204,113)。</summary>
        public static readonly Color ColorOk = FromHex(0x2E, 0xCC, 0x71);

        /// <summary>NG：徽标 / NG 计数 / 失败（红）#E74C3C = RGB(231,76,60)。</summary>
        public static readonly Color ColorNg = FromHex(0xE7, 0x4C, 0x3C);

        /// <summary>暂停 / 告警（橙）#F39C12 = RGB(243,156,18)。</summary>
        public static readonly Color ColorWarn = FromHex(0xF3, 0x9C, 0x12);

        /// <summary>运行中（蓝）#3478DC = RGB(52,120,220)。</summary>
        public static readonly Color ColorRunning = FromHex(0x34, 0x78, 0xDC);

        /// <summary>未开始 / 禁用（灰）#3A3A44 = RGB(58,58,68)。</summary>
        public static readonly Color ColorDisabled = FromHex(0x3A, 0x3A, 0x44);

        /// <summary>中性按钮底色（清空统计等）#484854 = RGB(72,72,84)。</summary>
        public static readonly Color ColorNeutral = FromHex(0x48, 0x48, 0x54);

        /// <summary>填充色块上的文字色（徽标 / 警示条 / 彩色按钮上的前景），纯白。</summary>
        public static readonly Color ColorTextOnFill = Color.White;

        // ---------------------- 字体族 ----------------------

        /// <summary>界面正文字体族（微软雅黑，缺失时退回无衬线）。</summary>
        public static readonly FontFamily FamilyBody =
            PickFamily("微软雅黑", "Microsoft YaHei", "微软雅黑 UI");

        /// <summary>等宽数字字体族（Consolas，缺失时退回等宽）。</summary>
        public static readonly FontFamily FamilyMono = PickFamily("Consolas", "Courier New");

        // ---------------------- 字体（7 项，懒构造）----------------------

        private static Font _fontBody;
        private static Font _fontGroupTitle;
        private static Font _fontBadge;
        private static Font _fontYield;
        private static Font _fontStat;
        private static Font _fontMono;
        private static Font _fontButton;
        private static Font _fontBadgeSmall;

        /// <summary>界面正文、普通按钮：微软雅黑 9 pt。</summary>
        public static Font FontBody
        {
            get { return _fontBody ?? (_fontBody = new Font(FamilyBody, 9f, FontStyle.Regular)); }
        }

        /// <summary>分组标题：微软雅黑 9.5 pt Bold，前景用 <see cref="ColorAccent"/>。</summary>
        public static Font FontGroupTitle
        {
            get { return _fontGroupTitle ?? (_fontGroupTitle = new Font(FamilyBody, 9.5f, FontStyle.Bold)); }
        }

        /// <summary>OK/NG 徽标：微软雅黑 44 pt Bold。</summary>
        public static Font FontBadge
        {
            get { return _fontBadge ?? (_fontBadge = new Font(FamilyBody, 44f, FontStyle.Bold)); }
        }

        /// <summary>良品率数值：Consolas 28 pt Bold。</summary>
        public static Font FontYield
        {
            get { return _fontYield ?? (_fontYield = new Font(FamilyMono, 28f, FontStyle.Bold)); }
        }

        /// <summary>其它统计数值：Consolas 15 pt Bold。</summary>
        public static Font FontStat
        {
            get { return _fontStat ?? (_fontStat = new Font(FamilyMono, 15f, FontStyle.Bold)); }
        }

        /// <summary>状态栏 / 日志：Consolas 9 pt。</summary>
        public static Font FontMono
        {
            get { return _fontMono ?? (_fontMono = new Font(FamilyMono, 9f, FontStyle.Regular)); }
        }

        /// <summary>运行控制按钮：微软雅黑 11 pt Bold。</summary>
        public static Font FontButton
        {
            get { return _fontButton ?? (_fontButton = new Font(FamilyBody, 11f, FontStyle.Bold)); }
        }

        /// <summary>视图右下角 OK/NG 小徽标：微软雅黑 14 pt Bold（FR-UI-08 要求 ≥14 pt）。</summary>
        public static Font FontBadgeSmall
        {
            get { return _fontBadgeSmall ?? (_fontBadgeSmall = new Font(FamilyBody, 14f, FontStyle.Bold)); }
        }

        private static Font _fontToolbar;

        /// <summary>工具栏（导航栏）条目：微软雅黑 10.5 pt Bold。
        /// 比正文大一号并加粗，是"导航栏内容更明显"的主要手段。</summary>
        public static Font FontToolbar
        {
            get { return _fontToolbar ?? (_fontToolbar = new Font(FamilyBody, 10.5f, FontStyle.Bold)); }
        }

        // ---------------------- 栅格（所有尺寸以 4 px 为步进）----------------------

        /// <summary>外边距 8 px。</summary>
        public const int Margin = 8;

        /// <summary>区块间距 8 px。</summary>
        public const int BlockGap = 8;

        /// <summary>控件水平间距 6 px。</summary>
        public const int ControlGap = 6;

        /// <summary>尺寸步进 4 px。</summary>
        public const int Step = 4;

        /// <summary>按钮 / 工具栏条目圆角半径 8 px（自绘控件使用）。</summary>
        public const int RadiusButton = 8;

        // ---------------------- 关键尺寸 ----------------------

        /// <summary>右栏宽度 300 px。</summary>
        public const int RightPanelWidth = 300;

        /// <summary>OK/NG 徽标高度 96 px（≥88 要求）。</summary>
        public const int BadgeHeight = 96;

        /// <summary>统计卡片区高度 152 px。</summary>
        public const int StatsHeight = 152;

        /// <summary>视图标题条高度 22 px。</summary>
        public const int ViewHeaderHeight = 22;

        /// <summary>「启动运行」按钮高度 56 px。</summary>
        public const int BtnRunH = 56;

        /// <summary>「暂停 / 继续」按钮高度 56 px。</summary>
        public const int BtnPauseH = 56;

        /// <summary>「停止」按钮高度 56 px。</summary>
        public const int BtnStopH = 56;

        /// <summary>「单帧采集」按钮高度 48 px。</summary>
        public const int BtnGrabH = 48;

        /// <summary>「清空统计」按钮高度 40 px。</summary>
        public const int BtnResetH = 40;

        /// <summary>日志区高度 120 px（可折叠）。</summary>
        public const int LogHeight = 120;

        /// <summary>工具栏高度 40 px。</summary>
        public const int ToolbarHeight = 40;

        /// <summary>状态栏高度 26 px。</summary>
        public const int StatusBarHeight = 26;

        /// <summary>
        /// 日志保留上限 2000 行（超出移除最旧一行）。
        /// <para>【为什么从 500 提到 2000】并行后每条流程每帧都会产一行 RESULT：
        /// 4 条流程 × 30 fps ≈ 90 行/秒，500 行只够存 5 秒，排障时看不到历史。
        /// 2000 行约 22 秒，配合 200 ms 批量刷新足以填满日志区。</para>
        /// </summary>
        public const int LogMaxLines = 2000;

        /// <summary>主窗口最小尺寸宽（PRD FR-UI-11）。</summary>
        public const int MinFormWidth = 1280;

        /// <summary>主窗口最小尺寸高（PRD FR-UI-11）。</summary>
        public const int MinFormHeight = 800;

        // ---------------------- 工具方法 ----------------------

        /// <summary>按 <see cref="UiButtonKind"/> 套用按钮配色与字体（纯文字、无图标）。</summary>
        public static void StyleButton(Button btn, UiButtonKind kind)
        {
            if (btn == null) return;

            btn.Font = FontButton;
            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderSize = 0;
            btn.ForeColor = Color.White;
            btn.TextAlign = ContentAlignment.MiddleCenter;
            btn.UseVisualStyleBackColor = false;

            switch (kind)
            {
                case UiButtonKind.Start:   btn.BackColor = ColorOk;      break;
                case UiButtonKind.Pause:   btn.BackColor = ColorWarn;    break;
                case UiButtonKind.Stop:    btn.BackColor = ColorNg;      break;
                case UiButtonKind.Grab:    btn.BackColor = ColorRunning; break;
                case UiButtonKind.Reset:   btn.BackColor = ColorNeutral; break;
                default:
                    btn.BackColor = ColorCardBg;
                    btn.ForeColor = ColorTextPrimary;
                    break;
            }
        }

        /// <summary>
        /// 设置按钮的可用 / 禁用外观。禁用态底色降为 <see cref="ColorDisabled"/>、
        /// 前景降为 <see cref="ColorTextSecondary"/>，肉眼可分辨（PRD FR-UI-06 验收②）。
        /// </summary>
        public static void SetButtonEnabled(Button btn, bool enabled, UiButtonKind kind)
        {
            if (btn == null) return;

            btn.Enabled = enabled;
            if (enabled)
            {
                StyleButton(btn, kind);
            }
            else
            {
                btn.BackColor = ColorDisabled;
                btn.ForeColor = ColorTextSecondary;
            }
        }

        /// <summary>
        /// 套用 OK/NG 徽标。始终保持文本字面（OK / NG / --），不依赖颜色传达结论。
        /// </summary>
        public static void StyleBadge(Label label, UiVerdict verdict)
        {
            if (label == null) return;

            label.Font = FontBadge;
            label.TextAlign = ContentAlignment.MiddleCenter;
            label.AutoSize = false;
            label.BorderStyle = BorderStyle.None;

            switch (verdict)
            {
                case UiVerdict.Ok:
                    label.BackColor = ColorOk;
                    label.ForeColor = Color.White;
                    label.Text = "OK";
                    break;
                case UiVerdict.Ng:
                    label.BackColor = ColorNg;
                    label.ForeColor = Color.White;
                    label.Text = "NG";
                    break;
                default:
                    label.BackColor = ColorDisabled;
                    label.ForeColor = ColorTextSecondary;
                    label.Text = "--";
                    break;
            }
        }

        /// <summary>
        /// 套用视图右下角 OK/NG 小徽标（FR-UI-08）。未判定（<see cref="UiVerdict.None"/>）时
        /// 隐藏控件且不显示 "--"，避免把「未检测」误读为结论。
        /// </summary>
        public static void StyleViewBadge(Label label, UiVerdict verdict)
        {
            if (label == null) return;

            label.Font = FontBadgeSmall;
            label.TextAlign = ContentAlignment.MiddleCenter;
            label.AutoSize = false;
            label.BorderStyle = BorderStyle.None;

            switch (verdict)
            {
                case UiVerdict.Ok:
                    label.BackColor = ColorOk;
                    label.ForeColor = Color.White;
                    label.Text = "OK";
                    label.Visible = true;
                    break;
                case UiVerdict.Ng:
                    label.BackColor = ColorNg;
                    label.ForeColor = Color.White;
                    label.Text = "NG";
                    label.Visible = true;
                    break;
                default:
                    label.BackColor = ColorDisabled;
                    label.ForeColor = ColorTextSecondary;
                    label.Text = "";
                    label.Visible = false;
                    break;
            }
        }

        /// <summary>套用统计数值标签（等宽数字，右对齐便于纵向比对）。</summary>
        public static void StyleStatValue(Label label, UiStatKind kind, bool large)
        {
            if (label == null) return;

            label.Font = large ? FontYield : FontStat;
            label.AutoSize = false;
            label.TextAlign = ContentAlignment.MiddleRight;

            switch (kind)
            {
                case UiStatKind.Yield: label.ForeColor = ColorAccent;    break;
                case UiStatKind.Ok:    label.ForeColor = ColorOk;         break;
                case UiStatKind.Ng:    label.ForeColor = ColorNg;         break;
                case UiStatKind.Warn:  label.ForeColor = ColorWarn;       break;
                default:               label.ForeColor = ColorTextPrimary; break;
            }
        }

        /// <summary>把颜色向白色插值（amount 0~1），用于自绘控件的悬停态。</summary>
        public static Color Lighten(Color c, float amount)
        {
            if (amount <= 0f) return c;
            if (amount > 1f) amount = 1f;
            return Color.FromArgb(c.A,
                ClampByte(c.R + (int)((255 - c.R) * amount)),
                ClampByte(c.G + (int)((255 - c.G) * amount)),
                ClampByte(c.B + (int)((255 - c.B) * amount)));
        }

        /// <summary>把颜色向黑色插值（amount 0~1），用于自绘控件的按下态。</summary>
        public static Color Darken(Color c, float amount)
        {
            if (amount <= 0f) return c;
            if (amount > 1f) amount = 1f;
            return Color.FromArgb(c.A,
                ClampByte(c.R - (int)(c.R * amount)),
                ClampByte(c.G - (int)(c.G * amount)),
                ClampByte(c.B - (int)(c.B * amount)));
        }

        private static int ClampByte(int v)
        {
            return v < 0 ? 0 : (v > 255 ? 255 : v);
        }

        /// <summary>
        /// 取日志级别对应的前景色。
        /// <para>分级配色：INFO=主文本、WARN=橙、ERROR=红、RESULT_OK=绿、RESULT_NG=红。
        /// RESULT_NG 与 ERROR 同为红色但层级不同：NG 是「检出来了不合格」，
        /// ERROR 是「流程跑崩了」，面板会再按级别前缀区分。</para>
        /// </summary>
        public static Color LogLevelColor(LogLevel level)
        {
            switch (level)
            {
                case LogLevel.Warn:     return ColorWarn;
                case LogLevel.Error:    return ColorNg;
                case LogLevel.ResultOk: return ColorOk;
                case LogLevel.ResultNg: return ColorNg;
                default:                return ColorTextPrimary;   // Info
            }
        }

        /// <summary>套用统计项名称标签（9 pt 次文本色）。</summary>
        public static void StyleStatLabel(Label label)
        {
            if (label == null) return;

            label.Font = FontBody;
            label.ForeColor = ColorTextSecondary;
            label.AutoSize = false;
            label.TextAlign = ContentAlignment.MiddleLeft;
        }

        /// <summary>套用分组标题（9.5 pt Bold + 强调色）。</summary>
        public static void StyleGroupTitle(Label label)
        {
            if (label == null) return;

            label.Font = FontGroupTitle;
            label.ForeColor = ColorAccent;
            label.BackColor = ColorHeaderBg;
            label.AutoSize = false;
            label.TextAlign = ContentAlignment.MiddleLeft;
        }

        /// <summary>套用分组容器（GroupBox）：卡片底色 + 主文本色。</summary>
        public static void StyleGroupBox(GroupBox box)
        {
            if (box == null) return;

            box.BackColor = ColorPanelBg;
            box.ForeColor = ColorTextPrimary;
            box.Font = FontBody;
        }

        /// <summary>套用面板容器（右栏 / 参数面板等）。</summary>
        public static void StylePanel(Panel panel)
        {
            if (panel == null) return;

            panel.BackColor = ColorPanelBg;
            panel.ForeColor = ColorTextPrimary;
        }

        /// <summary>套用只读结果区（卡片底色 + 等宽字体）。</summary>
        public static void StyleReadonlyBox(Control control)
        {
            if (control == null) return;

            control.BackColor = ColorCardBg;
            control.ForeColor = ColorTextPrimary;
            control.Font = FontMono;
        }

        /// <summary>
        /// 按良品率与告警下限取良品率的显示色：低于下限一半转红，低于下限转橙，
        /// 否则用强调色（PRD FR-UI-05 / 架构 B11）。
        /// </summary>
        /// <param name="yieldPercent">良品率百分数（0~100）。</param>
        /// <param name="alarmPercent">告警下限（默认 95）；小于等于 0 时不着色告警。</param>
        public static Color YieldColor(double yieldPercent, double alarmPercent)
        {
            if (alarmPercent > 0.0 && yieldPercent >= 0.0)
            {
                if (yieldPercent < alarmPercent * 0.5) return ColorNg;
                if (yieldPercent < alarmPercent) return ColorWarn;
            }
            return ColorAccent;
        }

        /// <summary>由 RGB 分量构造颜色（本类是唯一允许出现颜色字面量的地方）。</summary>
        private static Color FromHex(int r, int g, int b)
        {
            return Color.FromArgb(r, g, b);
        }

        /// <summary>在给定候选字体族名中挑选第一个已安装的，全部缺失时退回通用族。</summary>
        private static FontFamily PickFamily(params string[] names)
        {
            try
            {
                FontFamily[] installed = FontFamily.Families;
                for (int i = 0; i < names.Length; i++)
                {
                    for (int k = 0; k < installed.Length; k++)
                    {
                        if (string.Equals(installed[k].Name, names[i], StringComparison.OrdinalIgnoreCase))
                            return installed[k];
                    }
                }
            }
            catch (Exception)
            {
                // 字体枚举失败时退回通用族，不影响主题常量的可用性
            }

            return FontFamily.GenericSansSerif;
        }
    }
}
