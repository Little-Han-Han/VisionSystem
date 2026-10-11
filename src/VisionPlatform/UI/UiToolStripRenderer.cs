using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace VisionPlatform.UI
{
    /// <summary>
    /// 工具栏（导航栏）渲染器：把条目画成"胶囊按钮"而非扁平文字，
    /// 并配合加粗大字号 + 悬停/按下/选中高亮 + 可见分隔线，解决"导航栏上的内容不明显"。
    /// 纯 GDI+，零第三方依赖。
    /// </summary>
    public sealed class UiToolStripRenderer : ToolStripProfessionalRenderer
    {
        /// <summary>
        /// 给条目指定语义色（存放于 <see cref="ToolStripItem.Tag"/>，由 <see cref="DrawChip"/> 读取）。
        /// 只给「启动 / 暂停 / 采集」这类动作上色，其余保持中性卡片底，避免整条花掉。
        /// </summary>
        public static void SetChipKind(ToolStripItem item, UiButtonKind kind)
        {
            if (item != null) item.Tag = kind;
        }

        /// <summary>语义色 → 胶囊底色（与 <see cref="UiButton.FillColor"/> 同一套色板）。</summary>
        private static Color ChipFill(UiButtonKind kind)
        {
            switch (kind)
            {
                case UiButtonKind.Start: return UiTheme.ColorOk;
                case UiButtonKind.Pause: return UiTheme.ColorWarn;
                case UiButtonKind.Stop:  return UiTheme.ColorNg;
                case UiButtonKind.Grab:  return UiTheme.ColorRunning;
                case UiButtonKind.Reset: return UiTheme.ColorNeutral;
                default:                 return UiTheme.ColorCardBg;
            }
        }

        // ---------------------- 整条背景 ----------------------

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            Graphics g = e.Graphics;

            using (SolidBrush brush = new SolidBrush(UiTheme.ColorHeaderBg))
                g.FillRectangle(brush, e.AffectedBounds);

            // 底部一条分隔线：与下方图像区明确分层
            using (Pen pen = new Pen(UiTheme.ColorDivider))
                g.DrawLine(pen, 0, e.ToolStrip.Height - 1, e.ToolStrip.Width, e.ToolStrip.Height - 1);
        }

        /// <summary>不画外框（底部分隔线已承担分层），避免双线。</summary>
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
        }

        // ---------------------- 条目：按钮 / 下拉 / 组合框 ----------------------

        protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
        {
            DrawChip(e);
        }

        protected override void OnRenderDropDownButtonBackground(ToolStripItemRenderEventArgs e)
        {
            DrawChip(e);
        }

        protected override void OnRenderSplitButtonBackground(ToolStripItemRenderEventArgs e)
        {
            DrawChip(e);
        }

        /// <summary>
        /// 承载型条目（组合框 / 文本框）没有对应的 <c>OnRenderXxxBackground</c> 可重写，
        /// 只能在通用入口里按类型判断后画胶囊底；其余类型交回基类，避免重复绘制。
        /// </summary>
        protected override void OnRenderItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (e.Item is ToolStripComboBox || e.Item is ToolStripTextBox || e.Item is ToolStripProgressBar)
                DrawChip(e);
            else
                base.OnRenderItemBackground(e);
        }

        /// <summary>
        /// 胶囊底：默认态即有卡片底色（不再是"字直接贴在深色条上"），
        /// 悬停提亮、按下/选中为主题蓝 + 白字、禁用降灰。
        /// </summary>
        private static void DrawChip(ToolStripItemRenderEventArgs e)
        {
            Rectangle rect = new Rectangle(Point.Empty, e.Item.Size);
            rect.Inflate(-1, -3);
            if (rect.Width <= 0 || rect.Height <= 0) return;

            UiButtonKind kind = (e.Item.Tag is UiButtonKind) ? (UiButtonKind)e.Item.Tag : UiButtonKind.Neutral;
            bool semantic = (kind != UiButtonKind.Neutral);

            Color fill = ChipFill(kind);
            Color text = semantic ? Color.White : UiTheme.ColorTextPrimary;

            ToolStripButton btn = e.Item as ToolStripButton;
            bool isChecked = (btn != null && btn.Checked);

            if (!e.Item.Enabled)
            {
                fill = UiTheme.ColorDisabled;
                text = UiTheme.ColorTextSecondary;
            }
            else if (e.Item.Pressed || isChecked)
            {
                // 语义色按下变深（保持色相可辨），中性色按下用主题蓝（选中态）
                fill = semantic ? UiTheme.Darken(fill, 0.22f) : UiTheme.ColorAccent;
                text = Color.White;
            }
            else if (e.Item.Selected)
            {
                fill = semantic ? UiTheme.Lighten(fill, 0.16f) : UiTheme.Lighten(UiTheme.ColorCardBg, 0.18f);
            }

            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (GraphicsPath path = UiButton.RoundedPath(rect, UiTheme.RadiusButton))
            {
                using (SolidBrush brush = new SolidBrush(fill))
                    g.FillPath(brush, path);

                using (Pen pen = new Pen(e.Item.Enabled ? UiTheme.ColorDivider : UiTheme.ColorDisabled))
                    g.DrawPath(pen, path);
            }

            e.Item.ForeColor = text;
        }

        // ---------------------- 文本：加粗大字号 ----------------------

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            Color color;
            if (!e.Item.Enabled)
                color = UiTheme.ColorTextSecondary;
            else if (e.Item.Pressed || (e.Item is ToolStripButton && ((ToolStripButton)e.Item).Checked))
                color = Color.White;
            else
                color = e.TextColor;

            Rectangle rect = e.TextRectangle;
            if (rect.Width <= 0 || rect.Height <= 0) rect = new Rectangle(Point.Empty, e.Item.Size);

            TextRenderer.DrawText(e.Graphics, e.Text, UiTheme.FontToolbar, rect, color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }

        // ---------------------- 标签：不画底，纯文字（与按钮形成对比）----------------------

        protected override void OnRenderLabelBackground(ToolStripItemRenderEventArgs e)
        {
            e.Graphics.Clear(UiTheme.ColorHeaderBg);
        }

        // ---------------------- 分隔线：可见竖线 ----------------------

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            Graphics g = e.Graphics;
            using (Pen pen = new Pen(UiTheme.ColorDivider))
            {
                if (e.Vertical)
                {
                    int x = e.Item.Width / 2;
                    g.DrawLine(pen, x, 4, x, e.Item.Height - 4);
                }
                else
                {
                    int y = e.Item.Height / 2;
                    g.DrawLine(pen, 2, y, e.Item.Width - 2, y);
                }
            }
        }

        // ---------------------- 溢出按钮 / 状态背景 ----------------------

        protected override void OnRenderOverflowButtonBackground(ToolStripItemRenderEventArgs e)
        {
            DrawChip(e);
        }

        protected override void OnRenderStatusStripSizingGrip(ToolStripRenderEventArgs e)
        {
            // 不画尺寸手柄，保持底部整洁
        }
    }
}
