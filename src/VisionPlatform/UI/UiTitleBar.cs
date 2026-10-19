using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace VisionPlatform.UI
{
    /// <summary>
    /// 深色自绘标题栏：替掉系统原生浅色非客户区，让「最小化 / 最大化 / 关闭」与
    /// 主界面顶部深色工具栏同色（<see cref="UiTheme.ColorHeaderBg"/>），纯 GDI+ 绘制，零第三方依赖。
    /// <para>
    /// 【为什么不用 UiButton 拼】<see cref="UiButton"/> 的 Neutral 态底是
    /// <see cref="UiTheme.ColorCardBg"/>（比标题栏浅），放在标题栏上会显出三颗"胶囊"；
    /// 且它是 <c>sealed</c> 只画文字不画图形。故本控件直接自绘矩形热区 + 矢量字形：
    /// 默认态完全透明（贴平标题栏），只在悬停 / 按下时才有底色。
    /// </para>
    /// <para>
    /// 【拖动 / 双击最大化 / 拉伸不手写鼠标逻辑】一律交给 <see cref="TitleBarFilter"/>
    /// 处理 <c>WM_NCHITTEST</c>：命中标题栏返回 <c>HTCAPTION</c>，系统的窗口管理器
    /// 免费提供拖动移动、双击最大化与系统菜单；命中边缘返回 <c>HTLEFT/HTTOP/…</c>
    /// 保留 Sizable 窗体的拉伸能力。
    /// </para>
    /// </summary>
    public sealed class UiTitleBar : Panel
    {
        // ---------------------- Win32 命中测试常量 ----------------------

        private const int WM_NCHITTEST = 0x0084;
        private const int WM_NCMOUSEMOVE = 0x00A0;
        private const int WM_NCLBUTTONDOWN = 0x00A1;
        private const int WM_NCLBUTTONUP = 0x00A2;
        private const int WM_NCLBUTTONDBLCLK = 0x00A3;
        private const int WM_NCMOUSELEAVE = 0x02A2;

        private const int HTCAPTION = 2;
        private const int HTLEFT = 10;
        private const int HTRIGHT = 11;
        private const int HTTOP = 12;
        private const int HTTOPLEFT = 13;
        private const int HTTOPRIGHT = 14;
        private const int HTBOTTOM = 15;
        private const int HTBOTTOMLEFT = 16;
        private const int HTBOTTOMRIGHT = 17;

        /// <summary>拉伸热区宽度（px）：窗体边缘这个范围内命中即为可拉伸边框。</summary>
        private const int ResizeGrip = 6;

        /// <summary>单个标题栏按钮宽度（px）。</summary>
        public const int ButtonWidth = 44;

        /// <summary>标题栏按钮：索引即自左向右的绘制顺序，Close 恒在最右（Windows 惯例）。</summary>
        private enum BarButton
        {
            /// <summary>无（未命中）。</summary>
            None = -1,

            /// <summary>最小化。</summary>
            Minimize = 0,

            /// <summary>最大化 / 还原。</summary>
            Maximize = 1,

            /// <summary>关闭。</summary>
            Close = 2
        }

        private Form _form;
        private TitleBarFilter _filter;
        private bool _resizable;
        private bool _showMinimize;
        private bool _showMaximize;
        private BarButton _hover = BarButton.None;
        private BarButton _pressed = BarButton.None;

        public UiTitleBar()
        {
            SetStyle(ControlStyles.UserPaint
                     | ControlStyles.AllPaintingInWmPaint
                     | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.ResizeRedraw
                     | ControlStyles.Selectable, true);

            Name = "uiTitleBar";
            TabStop = false;
            Dock = DockStyle.None;
            Height = UiTheme.TitleBarHeight;
            BackColor = UiTheme.ColorHeaderBg;
            ForeColor = UiTheme.ColorTextPrimary;
            Font = UiTheme.FontToolbar;
            Cursor = Cursors.Default;

            // 主题切换时自身重绘取新色（IsSelfDrawn 白名单之外再加一道保险）
            UiTheme.Changed += OnThemeChanged;
        }

        // ======================================================================
        //  统一入口
        // ======================================================================

        /// <summary>
        /// 给窗体挂上自绘标题栏。应在窗体构造函数末尾（<c>UiStyle.ApplyForm(this)</c> 之后）调用一次。
        /// <para>
        /// 流程：记下原边框 → 置 <see cref="FormBorderStyle.None"/>（去掉系统原生标题栏）
        /// → 未停靠的直属子控件整体下避让 → 最后 <c>Controls.Add</c> 标题栏
        /// （WinForms 停靠按集合逆序生效，最后添加才拿到最顶部）→ 挂 WM_NCHITTEST 过滤器。
        /// </para>
        /// </summary>
        /// <param name="form">目标窗体；为 null 时直接返回 null。</param>
        /// <returns>已挂载的标题栏；若此前已挂过则返回既有实例（幂等）。</returns>
        public static UiTitleBar Attach(Form form)
        {
            if (form == null) return null;

            // 幂等：重复挂载会叠出两条标题栏
            foreach (Control c in form.Controls)
            {
                UiTitleBar exist = c as UiTitleBar;
                if (exist != null) return exist;
            }

            // 只有真正可拉伸的窗体（Sizable）才保留边缘拉伸热区；
            // FixedDialog（系统设置 / 登录）只给标题栏拖动热区。
            bool resizable = (form.FormBorderStyle == FormBorderStyle.Sizable);

            // ★ 必须在置 None 之前读：无边框样式下这两个属性可能被框架改写，
            //   先取原值，按钮的显示与否就由原窗体的设计意图决定。
            bool showMinimize = form.MinimizeBox;
            bool showMaximize = form.MaximizeBox;

            form.FormBorderStyle = FormBorderStyle.None;

            // 客户区避让：停靠控件会被标题栏自动挤下去，只有绝对定位的直属子控件需要手动下移
            PushUndockedChildrenDown(form);

            UiTitleBar bar = new UiTitleBar();
            form.Controls.Add(bar);           // ★ 必须最后 Add，才能停靠在最顶部
            bar.Dock = DockStyle.Top;
            bar.Bind(form, resizable, showMinimize, showMaximize);
            return bar;
        }

        /// <summary>
        /// 把窗体客户区整体加高一个标题栏，并把所有「未停靠」的直属子控件下移同样距离。
        /// <para>登录窗 / 系统设置这类全绝对定位的窗体，标题栏必然压住内容，必须避让；
        /// 其余窗体的直属子控件都是 Dock 布局，标题栏停靠后系统自动收缩，无需处理。</para>
        /// </summary>
        private static void PushUndockedChildrenDown(Form form)
        {
            List<Control> floating = new List<Control>();
            foreach (Control c in form.Controls)
            {
                if (c.Dock == DockStyle.None) floating.Add(c);
            }
            if (floating.Count == 0) return;

            form.ClientSize = new Size(form.ClientSize.Width,
                                       form.ClientSize.Height + UiTheme.TitleBarHeight);

            foreach (Control c in floating)
            {
                c.Top += UiTheme.TitleBarHeight;
            }
        }

        /// <summary>绑定宿主窗体并挂上消息过滤器。</summary>
        private void Bind(Form form, bool resizable, bool showMinimize, bool showMaximize)
        {
            _form = form;
            _resizable = resizable;
            _showMinimize = showMinimize;
            _showMaximize = showMaximize;

            form.TextChanged += OnFormTextChanged;
            form.SizeChanged += OnFormSizeChanged;

            _filter = new TitleBarFilter(form, this, resizable);
            _filter.Attach();

            Invalidate(true);
        }

        // ======================================================================
        //  绘制
        // ======================================================================

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            Rectangle rect = ClientRectangle;
            if (rect.Width <= 0 || rect.Height <= 0) return;

            // 底色：与主界面顶部工具栏同色
            using (SolidBrush brush = new SolidBrush(UiTheme.ColorHeaderBg))
                g.FillRectangle(brush, rect);

            // 底部分隔线 1 px：把标题栏与下方客户区切开
            using (Pen pen = new Pen(UiTheme.ColorDivider))
                g.DrawLine(pen, rect.Left, rect.Bottom - 1, rect.Right, rect.Bottom - 1);

            // 标题文字：取自宿主窗体 Text（空则退回自身 Text）
            string title = (_form != null) ? _form.Text : Text;
            int textRight = LeftMostButtonX() - UiTheme.ControlGap;
            if (!string.IsNullOrEmpty(title) && textRight > rect.Left)
            {
                Rectangle textRect = new Rectangle(rect.Left + UiTheme.Margin, rect.Top,
                    textRight - rect.Left - UiTheme.Margin, rect.Height - 1);
                TextRenderer.DrawText(g, title, UiTheme.FontToolbar, textRect,
                    UiTheme.ColorTextPrimary,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            }

            DrawButton(g, BarButton.Minimize);
            DrawButton(g, BarButton.Maximize);
            DrawButton(g, BarButton.Close);
        }

        /// <summary>绘制单个按钮（默认态不画底色，与标题栏融为一体）。</summary>
        private void DrawButton(Graphics g, BarButton kind)
        {
            if (!IsVisible(kind)) return;

            Rectangle r = ButtonRect(kind);
            if (r.Width <= 0 || r.Height <= 0) return;

            bool hovered = (_hover == kind);
            bool pressed = (_pressed == kind) && hovered;

            Color fill = Color.Transparent;
            if (kind == BarButton.Close)
            {
                // 关闭：悬停即 NG 红，白字 ✕（危险动作，视觉上必须最强）
                if (pressed) fill = UiTheme.Darken(UiTheme.ColorNg, 0.22f);
                else if (hovered) fill = UiTheme.ColorNg;
            }
            else
            {
                // 其余：以标题栏底色为基准，悬停提亮 / 按下压暗，与 UiButton 的三态手感一致
                if (pressed) fill = UiTheme.Darken(UiTheme.ColorHeaderBg, 0.22f);
                else if (hovered) fill = UiTheme.Lighten(UiTheme.ColorHeaderBg, 0.16f);
            }

            if (fill != Color.Transparent)
            {
                using (SolidBrush brush = new SolidBrush(fill))
                    g.FillRectangle(brush, r);
            }

            Color glyphColor = ((kind == BarButton.Close) && (hovered || pressed))
                ? UiTheme.ColorTextOnFill
                : UiTheme.ColorTextPrimary;

            DrawGlyph(g, kind, r, glyphColor);
        }

        /// <summary>
        /// 矢量字形（─ / □ / ❐ / ✕）。
        /// <para>刻意不用字符绘制：U+2715、U+2750 这类符号在微软雅黑下可能缺字而显示成方框，
        /// 直接画线段与矩形则任何字体环境下都稳定。</para>
        /// </summary>
        private void DrawGlyph(Graphics g, BarButton kind, Rectangle r, Color color)
        {
            int cx = r.Left + r.Width / 2;
            int cy = (r.Top + (r.Height - 1) / 2);

            using (Pen pen = new Pen(color, 1.5f))
            {
                switch (kind)
                {
                    case BarButton.Minimize:
                        // ─
                        g.DrawLine(pen, cx - 5, cy + 4, cx + 5, cy + 4);
                        break;

                    case BarButton.Maximize:
                        if (IsMaximized)
                        {
                            // ❐ 还原：两个错位矩形（后一个压在前一个右上）
                            g.DrawRectangle(pen, cx - 6, cy + 1, 8, 6);
                            g.DrawRectangle(pen, cx - 2, cy - 4, 8, 6);
                        }
                        else
                        {
                            // □ 最大化
                            g.DrawRectangle(pen, cx - 5, cy - 4, 10, 8);
                        }
                        break;

                    case BarButton.Close:
                        // ✕
                        g.DrawLine(pen, cx - 4, cy - 4, cx + 5, cy + 5);
                        g.DrawLine(pen, cx + 5, cy - 4, cx - 4, cy + 5);
                        break;
                }
            }
        }

        // ======================================================================
        //  几何
        // ======================================================================

        /// <summary>按钮矩形（Close 恒在最右）。</summary>
        private Rectangle ButtonRect(BarButton kind)
        {
            Rectangle rect = ClientRectangle;
            int h = Math.Max(0, rect.Height - 1);
            int slotFromRight = 3 - (int)kind;      // Close=1、Maximize=2、Minimize=3
            return new Rectangle(rect.Right - ButtonWidth * slotFromRight, 0, ButtonWidth, h);
        }

        /// <summary>最左侧可见按钮的 x（标题文字的右边界）。</summary>
        private int LeftMostButtonX()
        {
            int maxSlot = 0;
            for (int i = 0; i < 3; i++)
            {
                BarButton kind = (BarButton)i;
                if (IsVisible(kind)) maxSlot = Math.Max(maxSlot, 3 - i);
            }
            return ClientRectangle.Right - ButtonWidth * maxSlot;
        }

        /// <summary>按钮是否绘制 / 启用：跟随挂载前窗体的 MinimizeBox / MaximizeBox。</summary>
        private bool IsVisible(BarButton kind)
        {
            if (kind == BarButton.Close) return true;
            if (_form == null) return false;
            if (kind == BarButton.Minimize) return _showMinimize;
            return _showMaximize;
        }

        /// <summary>是否允许最大化 / 还原（双击标题栏与最大化按钮共用）。</summary>
        internal bool CanMaximize
        {
            get { return _form != null && _showMaximize; }
        }

        /// <summary>宿主窗体当前是否最大化（决定画 □ 还是 ❐）。</summary>
        private bool IsMaximized
        {
            get { return _form != null && _form.WindowState == FormWindowState.Maximized; }
        }

        /// <summary>命中的按钮；<see cref="BarButton.None"/> 表示落在标题栏空白处。</summary>
        private BarButton HitButton(Point client)
        {
            for (int i = 0; i < 3; i++)
            {
                BarButton kind = (BarButton)i;
                if (IsVisible(kind) && ButtonRect(kind).Contains(client)) return kind;
            }
            return BarButton.None;
        }

        /// <summary>
        /// 该客户区坐标是否落在三个按钮上。供 <see cref="TitleBarFilter"/> 排除热区使用：
        /// 按钮所在区域必须留在客户区，否则按钮收不到鼠标消息（会变成拖动窗体）。
        /// </summary>
        internal bool IsOnButton(Point client)
        {
            return HitButton(client) != BarButton.None;
        }

        /// <summary>
        /// 由过滤器在非客户区消息里调用：清掉悬停 / 按下态（否则按钮会一直亮着）。
        /// <para>鼠标从按钮移到标题栏空白处时该点变成非客户区，控件收不到 MouseLeave，
        /// 只有这里能补上。</para>
        /// </summary>
        internal void ResetStates()
        {
            if (_hover == BarButton.None && _pressed == BarButton.None) return;

            _hover = BarButton.None;
            _pressed = BarButton.None;
            if (Cursor != Cursors.Default) Cursor = Cursors.Default;
            Invalidate();
        }

        // ======================================================================
        //  鼠标
        // ======================================================================

        protected override void OnMouseMove(MouseEventArgs e)
        {
            BarButton hit = HitButton(e.Location);
            if (hit != _hover)
            {
                _hover = hit;
                Cursor = (hit == BarButton.None) ? Cursors.Default : Cursors.Hand;
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            ResetStates();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                BarButton hit = HitButton(e.Location);
                if (hit != BarButton.None)
                {
                    _pressed = hit;
                    Invalidate();
                }
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && _pressed != BarButton.None)
            {
                BarButton hit = HitButton(e.Location);
                if (hit == _pressed) Execute(hit);

                _pressed = BarButton.None;
                _hover = hit;
                Invalidate();
            }
            base.OnMouseUp(e);
        }

        /// <summary>执行按钮动作。</summary>
        private void Execute(BarButton kind)
        {
            Form f = _form;
            if (f == null) return;

            switch (kind)
            {
                case BarButton.Minimize:
                    f.WindowState = FormWindowState.Minimized;
                    break;

                case BarButton.Maximize:
                    ToggleMaximize();
                    break;

                case BarButton.Close:
                    f.Close();
                    break;
            }
        }

        /// <summary>最大化 ↔ 还原。</summary>
        private void ToggleMaximize()
        {
            Form f = _form;
            if (f == null || !_showMaximize) return;

            f.WindowState = (f.WindowState == FormWindowState.Maximized)
                ? FormWindowState.Normal
                : FormWindowState.Maximized;
            Invalidate();
        }

        // ======================================================================
        //  事件与清理
        // ======================================================================

        /// <summary>主题切换后重绘取新色（<see cref="UiThemeManager"/> 也会点名调用）。</summary>
        public void ApplyTheme()
        {
            BackColor = UiTheme.ColorHeaderBg;
            ForeColor = UiTheme.ColorTextPrimary;
            Font = UiTheme.FontToolbar;
            Invalidate(true);
        }

        private void OnThemeChanged(object sender, EventArgs e)
        {
            if (IsHandleCreated && !IsDisposed) ApplyTheme();
        }

        private void OnFormTextChanged(object sender, EventArgs e)
        {
            Invalidate();
        }

        private void OnFormSizeChanged(object sender, EventArgs e)
        {
            // 最大化 / 还原后要重画 □ / ❐，宽度变化后按钮热区也要重算
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                UiTheme.Changed -= OnThemeChanged;

                if (_form != null)
                {
                    _form.TextChanged -= OnFormTextChanged;
                    _form.SizeChanged -= OnFormSizeChanged;
                }
                if (_filter != null)
                {
                    _filter.Detach();
                    _filter = null;
                }
                _form = null;
            }
            base.Dispose(disposing);
        }

        // ======================================================================
        //  WM_NCHITTEST 过滤器：拖动 / 双击最大化 / 边缘拉伸
        // ======================================================================

        /// <summary>
        /// 挂在宿主窗体上的消息过滤器：只处理 <c>WM_NCHITTEST</c> 与几个非客户区鼠标消息，
        /// 其余一律交回 <c>base.WndProc</c>（即 WinForms 原有的窗口过程）。
        /// <para>用 <see cref="NativeWindow"/> 而不是 P/Invoke SetWindowLong：
        /// 它是 .NET Framework 自带类，零第三方依赖，且自带句柄生命周期管理。</para>
        /// </summary>
        private sealed class TitleBarFilter : NativeWindow
        {
            private readonly Form _form;
            private readonly UiTitleBar _bar;
            private readonly bool _resizable;
            private bool _assigned;

            public TitleBarFilter(Form form, UiTitleBar bar, bool resizable)
            {
                _form = form;
                _bar = bar;
                _resizable = resizable;
            }

            /// <summary>挂上句柄（未创建时等 HandleCreated）。</summary>
            public void Attach()
            {
                _form.HandleCreated += OnHandleCreated;
                _form.HandleDestroyed += OnHandleDestroyed;
                if (_form.IsHandleCreated) Assign();
            }

            /// <summary>摘掉句柄与事件订阅。</summary>
            public void Detach()
            {
                _form.HandleCreated -= OnHandleCreated;
                _form.HandleDestroyed -= OnHandleDestroyed;
                Release();
            }

            private void OnHandleCreated(object sender, EventArgs e)
            {
                Assign();
            }

            private void OnHandleDestroyed(object sender, EventArgs e)
            {
                Release();
            }

            private void Assign()
            {
                if (_assigned || !_form.IsHandleCreated) return;
                try
                {
                    AssignHandle(_form.Handle);
                    _assigned = true;
                }
                catch (Exception)
                {
                    // 句柄不可用时不阻断窗体构造；最坏情况是失去拖动能力，功能不受影响
                    _assigned = false;
                }
            }

            private void Release()
            {
                if (!_assigned) return;
                try
                {
                    ReleaseHandle();
                }
                catch (Exception)
                {
                    // 窗体正在销毁时释放可能失败，忽略即可
                }
                _assigned = false;
            }

            protected override void WndProc(ref Message m)
            {
                switch (m.Msg)
                {
                    case WM_NCHITTEST:
                    {
                        int hit = HitTest(m.LParam);
                        if (hit != 0)
                        {
                            m.Result = (IntPtr)hit;
                            return;
                        }
                        break;
                    }

                    case WM_NCLBUTTONDBLCLK:
                    {
                        // 双击标题栏 → 最大化 / 还原。无边框窗体不带 WS_MAXIMIZEBOX，
                        // 系统默认不会替我们做，故显式处理。
                        if (m.WParam.ToInt32() == HTCAPTION && _bar.CanMaximize)
                        {
                            _bar.ToggleMaximize();
                            m.Result = IntPtr.Zero;
                            return;
                        }
                        break;
                    }

                    case WM_NCMOUSEMOVE:
                    case WM_NCMOUSELEAVE:
                    case WM_NCLBUTTONDOWN:
                    case WM_NCLBUTTONUP:
                    {
                        // 鼠标进入非客户区（标题栏空白处）时，按钮收不到 MouseLeave，
                        // 必须在这里把悬停态清掉，否则按钮会一直亮着。
                        _bar.ResetStates();
                        break;
                    }
                }

                base.WndProc(ref m);
            }

            /// <summary>
            /// 命中测试：<paramref name="lParam"/> 低 16 位为屏幕 x、高 16 位为屏幕 y。
            /// 返回 0 表示不接管，交回原窗口过程。
            /// </summary>
            private int HitTest(IntPtr lParam)
            {
                // 只有真正无边框（已换成自绘标题栏）的窗体才需要热区
                if (_form.FormBorderStyle != FormBorderStyle.None) return 0;

                long v = lParam.ToInt64();
                int x = (short)(v & 0xFFFF);
                int y = (short)((v >> 16) & 0xFFFF);

                Point p = _form.PointToClient(new Point(x, y));
                Size s = _form.ClientSize;
                if (s.Width <= 0 || s.Height <= 0) return 0;

                if (_resizable)
                {
                    bool left = p.X < ResizeGrip;
                    bool right = p.X >= s.Width - ResizeGrip;
                    bool top = p.Y < ResizeGrip;
                    bool bottom = p.Y >= s.Height - ResizeGrip;

                    if (left && top) return HTTOPLEFT;
                    if (right && top) return HTTOPRIGHT;
                    if (left && bottom) return HTBOTTOMLEFT;
                    if (right && bottom) return HTBOTTOMRIGHT;
                    if (left) return HTLEFT;
                    if (right) return HTRIGHT;
                    if (top) return HTTOP;
                    if (bottom) return HTBOTTOM;
                }

                // 标题栏（排除三个按钮矩形）：返回 HTCAPTION → 拖动移动由系统原生提供
                if (p.Y >= 0 && p.Y < UiTheme.TitleBarHeight && p.X >= 0 && p.X < s.Width)
                {
                    Point barPoint = _bar.PointToClient(new Point(x, y));
                    if (!_bar.IsOnButton(barPoint)) return HTCAPTION;
                }

                return 0;
            }
        }
    }
}
