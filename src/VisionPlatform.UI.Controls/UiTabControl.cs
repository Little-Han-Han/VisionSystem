using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace VisionPlatform.UI
{
    /// <summary>
    /// 工程软件风格选项卡：自绘标签 —— 选中态主题蓝底白字，未选中态深色底，
    /// 纯 GDI+ 实现，零第三方依赖。颜色与字体取自 <see cref="UiTheme"/>。
    /// <para>
    /// 用法：设 <see cref="TabControl.DrawMode"/> 由本控件自动置为
    /// <see cref="TabDrawMode.OwnerDrawFixed"/>；页区底色请用
    /// <see cref="StylePage"/> 逐页套用（页区由系统绘制，不接管以免破坏子控件）。
    /// </para>
    /// </summary>
    public sealed class UiTabControl : TabControl
    {
        /// <summary>标签默认高度 32 px。</summary>
        public const int TabHeight = 32;

        public UiTabControl()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            DrawMode = TabDrawMode.OwnerDrawFixed;
            SizeMode = TabSizeMode.Fixed;
            ItemSize = new Size(108, TabHeight);
            Padding = new Point(10, 4);
            Font = UiTheme.FontBody;
            Alignment = TabAlignment.Top;
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= TabPages.Count) return;

            TabPage page = TabPages[e.Index];
            Rectangle rect = GetTabRect(e.Index);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            bool selected = (e.Index == SelectedIndex);

            Color fill = selected ? UiTheme.ColorAccent : UiTheme.ColorHeaderBg;
            if (!page.Enabled) fill = UiTheme.ColorDisabled;

            using (GraphicsPath path = UiButton.RoundedPath(rect, UiTheme.RadiusButton))
            {
                using (SolidBrush brush = new SolidBrush(fill))
                    g.FillPath(brush, path);

                using (Pen pen = new Pen(selected ? UiTheme.ColorAccent : UiTheme.ColorDivider))
                    g.DrawPath(pen, path);
            }

            Color textColor = selected
                ? Color.White
                : (page.Enabled ? UiTheme.ColorTextPrimary : UiTheme.ColorTextSecondary);

            TextRenderer.DrawText(g, page.Text,
                selected ? UiTheme.FontButton : UiTheme.FontBody, rect, textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }

        /// <summary>把某一页套成主题外观（页区底色 + 前景 + 字体）。</summary>
        public static void StylePage(TabPage page)
        {
            if (page == null) return;
            page.BackColor = UiTheme.ColorPanelBg;
            page.ForeColor = UiTheme.ColorTextPrimary;
            page.Font = UiTheme.FontBody;
        }

        /// <summary>一次性套用所有页。</summary>
        public void StyleAllPages()
        {
            foreach (TabPage page in TabPages) StylePage(page);
        }
    }
}
