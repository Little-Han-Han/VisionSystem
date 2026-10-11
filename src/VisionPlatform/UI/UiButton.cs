using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace VisionPlatform.UI
{
    /// <summary>
    /// 工程软件风格按钮：纯 GDI+ 自绘 —— 圆角、语义色、悬停 / 按下 / 禁用四态、聚焦虚框。
    /// 颜色与字体一律取自 <see cref="UiTheme"/>，零第三方依赖。
    /// <para>
    /// 与 <see cref="UiTheme.StyleButton"/> 的区别：后者只改 Flat 配色（仍由系统绘制，
    /// 无圆角、无状态过渡，观感偏"丑"）；本控件接管绘制，因而有圆角与状态反馈。
    /// </para>
    /// </summary>
    public sealed class UiButton : Button
    {
        private UiButtonKind _kind = UiButtonKind.Neutral;
        private bool _hover;
        private bool _pressed;

        public UiButton()
        {
            SetStyle(ControlStyles.UserPaint
                     | ControlStyles.AllPaintingInWmPaint
                     | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.ResizeRedraw
                     | ControlStyles.SupportsTransparentBackColor
                     | ControlStyles.Selectable, true);

            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Font = UiTheme.FontButton;
            ForeColor = Color.White;
            BackColor = UiTheme.ColorCardBg;
            Cursor = Cursors.Hand;
            TextAlign = ContentAlignment.MiddleCenter;
            UseVisualStyleBackColor = false;
        }

        /// <summary>语义色；变更后立即重绘。</summary>
        [DefaultValue(UiButtonKind.Neutral)]
        public UiButtonKind Kind
        {
            get { return _kind; }
            set
            {
                if (_kind == value) return;
                _kind = value;
                Invalidate();
            }
        }

        /// <summary>当前语义色对应的填充色（取自 <see cref="UiTheme"/>）。</summary>
        public Color FillColor
        {
            get
            {
                switch (_kind)
                {
                    case UiButtonKind.Start:   return UiTheme.ColorOk;
                    case UiButtonKind.Pause:   return UiTheme.ColorWarn;
                    case UiButtonKind.Stop:    return UiTheme.ColorNg;
                    case UiButtonKind.Grab:    return UiTheme.ColorRunning;
                    case UiButtonKind.Reset:   return UiTheme.ColorNeutral;
                    default:                   return UiTheme.ColorCardBg;
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);   // 仅触发 Paint 事件；绘制由本方法接管

            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            Rectangle rect = ClientRectangle;
            rect.Width -= 1;
            rect.Height -= 1;
            if (rect.Width <= 0 || rect.Height <= 0) return;

            bool isNeutral = (_kind == UiButtonKind.Neutral);

            Color fill = FillColor;
            if (!Enabled)            fill = UiTheme.ColorDisabled;
            else if (_pressed)       fill = UiTheme.Darken(fill, 0.22f);
            else if (_hover)         fill = UiTheme.Lighten(fill, 0.16f);

            // 圆角填充 + 描边（描边让默认态按钮在深底上也有明确边界）
            using (GraphicsPath path = RoundedPath(rect, UiTheme.RadiusButton))
            {
                using (SolidBrush brush = new SolidBrush(fill))
                    g.FillPath(brush, path);

                using (Pen pen = new Pen(Enabled ? UiTheme.ColorDivider : UiTheme.ColorDisabled))
                    g.DrawPath(pen, path);
            }

            // 文本：语义色态用白字保证对比度，默认态用主文本色
            Color textColor;
            if (!Enabled) textColor = UiTheme.ColorTextSecondary;
            else if (isNeutral) textColor = UiTheme.ColorTextPrimary;
            else textColor = Color.White;

            TextRenderer.DrawText(g, Text, Font, rect, textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);

            // 键盘聚焦虚框（保留可达性）
            if (Focused && ShowFocusCues)
            {
                Rectangle fr = rect;
                fr.Inflate(-3, -3);
                using (Pen pen = new Pen(UiTheme.ColorAccent))
                {
                    pen.DashStyle = DashStyle.Dot;
                    g.DrawRectangle(pen, fr);
                }
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            _pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) _pressed = true;
            Invalidate();
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _pressed = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            Invalidate();
            base.OnEnabledChanged(e);
        }

        /// <summary>生成圆角矩形路径（供本控件与 <see cref="UiToolStripRenderer"/> 共用）。</summary>
        internal static GraphicsPath RoundedPath(Rectangle r, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            if (radius <= 0 || r.Width <= 0 || r.Height <= 0)
            {
                path.AddRectangle(r);
                return path;
            }

            int d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
