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

            // Image 来自基类 Button（ButtonBase.Image）；设置了图标就画成"左图标 + 右文字"，
            // 整体水平居中。图标用原尺寸不缩放（16/24/32 三档按按钮高度挑），保证像素清晰。
            Image glyph = Image;
            if (glyph != null && glyph.Width > 0 && glyph.Height > 0)
            {
                const int gap = 6;
                // NoPadding：MeasureText 默认带额外内边距，会让"图标 + 间距 + 文字"整体偏宽、
                // 图标离文字看起来有空档；去掉 padding 后组合宽度与实际绘制一致。
                Size textSize = string.IsNullOrEmpty(Text)
                    ? Size.Empty
                    : TextRenderer.MeasureText(Text, Font, new Size(int.MaxValue, int.MaxValue),
                                               TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                int total = glyph.Width + (textSize.IsEmpty ? 0 : gap + textSize.Width);
                int ix = rect.Left + (rect.Width - total) / 2;
                int iy = rect.Top + (rect.Height - glyph.Height) / 2;
                Rectangle imgRect = new Rectangle(ix, iy, glyph.Width, glyph.Height);

                if (Enabled)
                {
                    g.DrawImage(glyph, imgRect);
                }
                else
                {
                    // 禁用态：图标一起压暗，避免亮色图标在灰按钮上显得"还能点"
                    using (var attrs = new System.Drawing.Imaging.ImageAttributes())
                    {
                        var cm = new System.Drawing.Imaging.ColorMatrix { Matrix44 = 0.35f };
                        attrs.SetColorMatrix(cm);
                        g.DrawImage(glyph, imgRect, 0, 0, glyph.Width, glyph.Height,
                            GraphicsUnit.Pixel, attrs);
                    }
                }

                if (!textSize.IsEmpty)
                {
                    var textRect = new Rectangle(ix + glyph.Width + gap, rect.Top,
                                                 textSize.Width + 2, rect.Height);
                    TextRenderer.DrawText(g, Text, Font, textRect, textColor,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                        | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                }
            }
            else
            {
                TextRenderer.DrawText(g, Text, Font, rect, textColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            }

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
