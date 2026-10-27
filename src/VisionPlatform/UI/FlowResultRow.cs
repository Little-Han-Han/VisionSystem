using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using VisionPlatform.Flow;

namespace VisionPlatform.UI
{
    /// <summary>结果行的种类。</summary>
    public enum FlowResultRowKind
    {
        /// <summary>节点主行（两行高：节点名 + 状态 + 时间 / 主判据 + 耗时）。</summary>
        Main,

        /// <summary>明细行（一行高：第 k 个目标的键值串）。</summary>
        Detail
    }

    /// <summary>
    /// 结果区的一行（主行或明细行）。
    /// <para>
    /// 【为什么不用 DataGridView】它行高统一，做不到"主行 36 / 明细 22"；
    /// 而"一节点一行 + 展开明细"正是要靠这两种行高来区分层级。
    /// </para>
    /// <para>
    /// 【为什么只用 Label、不重写 OnPaint】自绘控件要同时维护三份白名单
    /// （<c>UiStyle.IsSelfDrawn</c>、<c>Core/Lang.cs</c> 的第二份拷贝、<c>UiThemeManager.ReapplySelfDrawn</c>），
    /// 漏一处就是"切主题后这块不自绘了/不跟主题了"。本控件内部全是标准 <see cref="Label"/>，
    /// 走普通控件路径，主题切换由 <c>UiStyle</c> 自动遍历即可 —— 但语义色（OK/NG）仍要显式设置，
    /// 因为通用遍历会把前景色统一成主文本色。
    /// </para>
    /// </summary>
    public sealed class FlowResultRow : UserControl
    {
        /// <summary>主行高度（96-DPI 逻辑值，由 <see cref="FlowResultPanel"/> 按 DPI 换算后设置）。</summary>
        public const int MainHeight = 36;

        /// <summary>明细行高度。</summary>
        public const int DetailHeight = 22;

        // 主行第一行：序号 / 节点名 / 状态 / 时间
        private readonly Label _lblIndex;
        private readonly Label _lblName;
        private readonly Label _lblBadge;
        private readonly Label _lblTime;
        // 主行第二行：主判据
        private readonly Label _lblPrimary;
        // 明细行：整行一个文本
        private readonly Label _lblDetail;

        private bool _expanded;

        /// <summary>行种类。</summary>
        public FlowResultRowKind Kind { get; private set; }

        /// <summary>明细行的目标序号（从 0 开始）；主行为 -1。</summary>
        public int DetailIndex { get; private set; }

        /// <summary>本行被点击（主行 / 明细行都用它）。</summary>
        public event EventHandler RowClicked;

        /// <summary>创建主行。</summary>
        public static FlowResultRow CreateMain()
        {
            return new FlowResultRow(FlowResultRowKind.Main, -1);
        }

        /// <summary>创建明细行。</summary>
        public static FlowResultRow CreateDetail(int index)
        {
            return new FlowResultRow(FlowResultRowKind.Detail, index);
        }

        private FlowResultRow(FlowResultRowKind kind, int detailIndex)
        {
            Kind = kind;
            DetailIndex = detailIndex;

            SetStyle(ControlStyles.Selectable, true);
            Cursor = Cursors.Hand;
            Margin = new Padding(0);
            Padding = new Padding(0);

            if (kind == FlowResultRowKind.Main)
            {
                Height = MainHeight;
                // 主行铺一层卡片底色：它和下面 N 条明细行必须"一眼分得开"，
                // 否则 12 条结果看起来就是 13 行等价的文字。
                BackColor = UiTheme.ColorCardBg;

                // 【字号层级】不要用 FontBadgeSmall（14pt 粗体，是视图右下角 OK/NG 徽标专用）：
                // 塞进 17~18px 高的行内 Label 会被裁切、两行互相压，现场看就是"结果区糊成一团"。
                _lblIndex = MakeLabel("", 20, 0, 18, 18, ContentAlignment.MiddleLeft, UiTheme.FontSmall);
                _lblName = MakeLabel("", 22, 0, 118, 18, ContentAlignment.MiddleLeft, UiTheme.FontGroupTitle);
                _lblName.AutoEllipsis = true;
                _lblBadge = MakeLabel("", 142, 0, 42, 18, ContentAlignment.MiddleLeft, UiTheme.FontGroupTitle);
                _lblTime = MakeLabel("", 0, 0, 96, 18, ContentAlignment.MiddleRight, UiTheme.FontSmall);
                // 位置/尺寸统一由 LayoutInner() 按实际行宽行高算，不用 Anchor（两者混用会互相抵消）

                _lblPrimary = MakeLabel("", 22, 18, 258, 17, ContentAlignment.MiddleLeft, UiTheme.FontBody);
                _lblPrimary.AutoEllipsis = true;

                Controls.Add(_lblIndex);
                Controls.Add(_lblName);
                Controls.Add(_lblBadge);
                Controls.Add(_lblTime);
                Controls.Add(_lblPrimary);
            }
            else
            {
                Height = DetailHeight;

                _lblDetail = MakeLabel("", 28, 0, 258, DetailHeight - 2, ContentAlignment.MiddleLeft,
                                       UiTheme.FontSmall);
                _lblDetail.AutoEllipsis = true;
                Controls.Add(_lblDetail);
            }

            // Label 会自己吃掉鼠标事件，导致"点在文字上没反应、点空白才对"。所以把每个
            // 子 Label 的 Click 都接到同一个转发上，保证整行任何位置都能点。
            foreach (Control c in Controls)
            {
                c.Cursor = Cursors.Hand;
                c.Click += OnChildClick;
            }

            ApplyTheme();
            LayoutInner();
        }

        /// <summary>
        /// 按<strong>实际行宽/行高</strong>排布内部标签。
        /// <para>
        /// 【为什么不能写死 18 / 17 px】行高在不同 DPI 下会被父容器缩放（36 → 54@150%），
        /// 而写死的标签高度不会跟着变：结果是"字变大了、格子没变大"，文字被裁掉半行 ——
        /// 这正是用户反馈的"字体过大、显示不全"。这里一律按 <see cref="Control.Height"/> 等分，
        /// 任何 DPI 下都不会裁。
        /// </para>
        /// </summary>
        private void LayoutInner()
        {
            if (Width <= 0 || Height <= 0) return;

            // 【必须判空】构造函数里 `Height = MainHeight` 就会触发 OnResize，
            // 那一刻下面的 Label 还没 new 出来（同 OcrTemplateCard.ApplyLayout 那枚雷）。
            // ⚠️ 只查本行真正拥有的那几个：主行没有 _lblDetail、明细行没有其它四个，
            //    一次性全查会让 LayoutInner 永远提前返回（表现为"名字消失、时间挤到最左"）。
            if (Kind == FlowResultRowKind.Main)
            {
                if (_lblIndex == null || _lblName == null || _lblBadge == null
                    || _lblTime == null || _lblPrimary == null) return;
            }
            else
            {
                if (_lblDetail == null) return;
            }

            if (Kind == FlowResultRowKind.Main)
            {
                int h1 = Math.Max(14, (Height - 1) / 2);
                int h2 = Math.Max(14, Height - h1);

                _lblIndex.Top = 0; _lblIndex.Height = h1;
                _lblBadge.Top = 0; _lblBadge.Height = h1;
                _lblName.Top = 0;  _lblName.Height = h1;
                _lblTime.Top = 0;  _lblTime.Height = h1;

                // 右起排布：耗时(64) → 状态(42) → 剩下全给节点名。
                // 右栏只有 300px，固定 x（原来 badge 钉在 142）会让名字永远只有 108px。
                int timeW = 64;
                _lblTime.Width = timeW;
                _lblTime.Left = Math.Max(120, Width - timeW - 6);

                _lblBadge.Width = 42;
                _lblBadge.Left = Math.Max(96, _lblTime.Left - _lblBadge.Width - 6);

                _lblName.Width = Math.Max(48, _lblBadge.Left - 22 - 6);

                _lblPrimary.Top = h1;
                _lblPrimary.Height = h2;
                _lblPrimary.Width = Math.Max(60, Width - 24);
            }
            else
            {
                _lblDetail.Top = 0;
                _lblDetail.Height = Height;
                _lblDetail.Width = Math.Max(60, Width - 30);
            }
        }

        /// <summary>行尺寸变化（含 DPI 缩放 / 结果区宽度变化）时重排。</summary>
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutInner();
        }

        private Label MakeLabel(string text, int left, int top, int width, int height,
                                ContentAlignment align, Font font)
        {
            var lbl = new Label();
            lbl.Text = text;
            lbl.AutoSize = false;
            lbl.Location = new Point(left, top);
            lbl.Size = new Size(width, height);
            lbl.TextAlign = align;
            lbl.Font = font;
            lbl.BackColor = Color.Transparent;
            lbl.ForeColor = UiTheme.ColorTextSecondary;
            return lbl;
        }

        // =====================================================================
        //  填充
        // =====================================================================

        /// <summary>填充主行内容。</summary>
        public void ApplyMain(int index, string nodeName, FlowNodeStatus status, string primary,
                              int elapsedMs, DateTime runAt, bool expanded, bool hasResult)
        {
            _expanded = expanded;

            _lblIndex.Text = index.ToString(CultureInfo.InvariantCulture);

            // 收起时显示 ▶、展开时显示 ▼，与端子区的 ▲/▼ 同一套视觉语言
            _lblName.Text = (expanded ? "▼ " : "▶ ") + (nodeName ?? "");

            string badge = NodeResultText.Badge(status);
            _lblBadge.Text = badge;
            _lblBadge.ForeColor = StatusColor(status);

            // 第一行右端 = 耗时（毫秒）。调参时每天都要看这个数；
            // "几点跑的" 在日志库里能查，不值得占主判据那一行的宽度。
            _lblTime.Text = hasResult && elapsedMs >= 0
                ? elapsedMs.ToString(CultureInfo.InvariantCulture) + " ms"
                : "";

            _lblPrimary.Text = hasResult ? (primary ?? "") : "该节点尚未运行";
            _lblPrimary.ForeColor = hasResult ? UiTheme.ColorTextPrimary : UiTheme.ColorDisabled;
        }

        /// <summary>填充明细行内容（键值对压成一行，绝不换行 —— 换行会把固定行高撑破）。</summary>
        public void ApplyDetail(int displayIndex, string text)
        {
            _lblDetail.Text = displayIndex.ToString(CultureInfo.InvariantCulture) + "　" + (text ?? "");
            _lblDetail.ForeColor = UiTheme.ColorTextPrimary;
        }

        /// <summary>本行是否为当前高亮行（视觉反馈：左缘强调色 + 主文本色）。</summary>
        public void SetAccent(bool on)
        {
            BackColor = on ? UiTheme.ColorHeaderBg
                            : (Kind == FlowResultRowKind.Main ? UiTheme.ColorCardBg : Color.Transparent);
            if (Kind == FlowResultRowKind.Main && _lblName != null)
                _lblName.ForeColor = on ? UiTheme.ColorAccent : UiTheme.ColorTextPrimary;
        }

        /// <summary>重套主题（语义色必须最后覆盖，否则会被通用遍历冲掉）。</summary>
        public void ApplyTheme()
        {
            BackColor = Kind == FlowResultRowKind.Main ? UiTheme.ColorCardBg : Color.Transparent;
            ForeColor = UiTheme.ColorTextPrimary;

            if (Kind == FlowResultRowKind.Main)
            {
                _lblIndex.ForeColor = UiTheme.ColorTextSecondary;
                _lblName.ForeColor = UiTheme.ColorTextPrimary;
                _lblTime.ForeColor = UiTheme.ColorTextSecondary;
                _lblPrimary.ForeColor = UiTheme.ColorTextPrimary;
                _lblBadge.ForeColor = StatusColor(FlowNodeStatus.Pending);
            }
            else
            {
                _lblDetail.ForeColor = UiTheme.ColorTextPrimary;
            }
        }

        /// <summary>状态 → 语义色（一处定义，主行徽标与其它地方共用）。</summary>
        internal static Color StatusColor(FlowNodeStatus status)
        {
            switch (status)
            {
                case FlowNodeStatus.Ok: return UiTheme.ColorOk;
                case FlowNodeStatus.Ng: return UiTheme.ColorNg;
                case FlowNodeStatus.Running: return UiTheme.ColorRunning;
                case FlowNodeStatus.Warn: return UiTheme.ColorWarn;
                case FlowNodeStatus.Skipped: return UiTheme.ColorWarn;
                default: return UiTheme.ColorTextSecondary;
            }
        }

        /// <summary>本行是否处于展开态（仅主行有意义）。</summary>
        public bool Expanded { get { return _expanded; } }

        /// <summary>
        /// 显示 / 隐藏时间戳。窄宽度下（150% DPI，右栏内容宽约 228px）优先牺牲时间戳，
        /// 保住"节点名 + 状态 + 主判据"这三样真正决定要不要点开看的东西。
        /// </summary>
        public void ShowTimestamp(bool show)
        {
            if (_lblTime != null) _lblTime.Visible = show;
        }

        // =====================================================================
        //  点击转发
        // =====================================================================

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            RaiseClicked();
        }

        private void OnChildClick(object sender, EventArgs e)
        {
            RaiseClicked();
        }

        private void RaiseClicked()
        {
            EventHandler h = RowClicked;
            if (h != null) h(this, EventArgs.Empty);
        }
    }
}
