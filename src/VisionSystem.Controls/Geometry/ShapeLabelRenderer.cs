using System;
using System.Drawing;
using VisionSystem.Controls.Rendering;

namespace VisionSystem.Controls.Geometry
{
    /// <summary>
    /// 叠加层「结果标签」的<strong>唯一渲染实现</strong>（矩形 / 旋转矩形共用）。
    /// <para>
    /// 【为什么要抽出来】原先两个图形的 Draw 各写一段、各自 <c>new Font("Segoe UI", 9f, Bold)</c>：
    /// ① 每个图形每帧新建 Font（GDI 句柄抖动，几十个目标时明显）；
    /// ② 9pt 粗体在"一次 20 个 Blob"的场景里把画面糊满（用户反馈"字体过大、显示不全"）；
    /// ③ 标签只避让了上边界，靠右/靠下的框会把标签画到画面外 —— 看起来像结果丢了。
    /// </para>
    /// <para>
    /// 【字号是屏幕像素】标签固定 8pt，与缩放无关：放大看细节时标签不会跟着变成大字，
    /// 缩小看全局时也不会小到看不见。
    /// </para>
    /// </summary>
    internal static class ShapeLabelRenderer
    {
        /// <summary>标签底色（半透明黑，压住底图保证可读，又不至于糊成一块）。</summary>
        private static readonly Color BackColor = Color.FromArgb(150, 0, 0, 0);

        private static Font _cachedFont;
        private static float _cachedSize = float.NaN;

        /// <summary>按字号取缓存字体（同一字号只建一次；中文走系统字体链接）。</summary>
        private static Font GetFont(float size)
        {
            if (_cachedFont != null && Math.Abs(_cachedSize - size) < 0.01f) return _cachedFont;

            if (_cachedFont != null) _cachedFont.Dispose();

            Font f = null;
            try { f = new Font("Microsoft YaHei UI", size, FontStyle.Regular, GraphicsUnit.Point); }
            catch (Exception) { f = null; }
            if (f == null)
            {
                try { f = new Font(SystemFonts.DefaultFont.FontFamily, size, FontStyle.Regular); }
                catch (Exception) { }
            }
            if (f == null) f = SystemFonts.DefaultFont;

            _cachedFont = f;
            _cachedSize = size;
            return _cachedFont;
        }

        /// <summary>
        /// 画一个标签：默认贴在 <paramref name="anchor"/>（框的屏幕左上角）上方；
        /// 上方放不下就落到框内，右边越界就往左挪 —— <strong>永远落在可视区内</strong>。
        /// </summary>
        /// <param name="g">绘图面（已按屏幕坐标）。</param>
        /// <param name="text">标签文本；空 = 不画。</param>
        /// <param name="anchor">框的左上角（屏幕坐标）。</param>
        /// <param name="color">文字颜色（一般取框色，保证"哪个标签属于哪个框"一眼可辨）。</param>
        /// <param name="options">渲染选项（字号 / 裁剪区）；null 用默认。</param>
        public static void Draw(Graphics g, string text, PointF anchor, Color color, RenderOptions options)
        {
            if (g == null || string.IsNullOrEmpty(text)) return;

            float size = (options != null) ? options.LabelFontSize : 8f;
            if (size <= 0.0f) return;                      // 调用方选择不显示标签

            Font font = GetFont(size);

            // 用 MeasureString 的整串尺寸（含少量内边距），再加固定留白，避免不同字形高度不一
            SizeF sz = g.MeasureString(text, font);
            float w = sz.Width + 4f;
            float h = sz.Height;

            float bx = anchor.X;
            float by = anchor.Y - h - 2f;                  // 默认贴框上方
            if (by < 0f) by = anchor.Y + 2f;               // 顶部越界 → 落到框内顶部

            RectangleF clip = (options != null) ? options.LabelClipBounds : RectangleF.Empty;
            if (clip.Width > 0f && clip.Height > 0f)
            {
                if (by + h > clip.Bottom) by = Math.Max(clip.Top, anchor.Y - h - 2f);
                if (bx + w > clip.Right) bx = clip.Right - w;
                if (bx < clip.Left) bx = clip.Left;
                if (by < clip.Top) by = clip.Top;
            }

            using (var bg = new SolidBrush(BackColor))
                g.FillRectangle(bg, bx, by, w, h);

            using (var fg = new SolidBrush(Color.FromArgb(240, color.R, color.G, color.B)))
                g.DrawString(text, font, fg, bx + 2f, by);
        }

        /// <summary>释放缓存字体（进程退出/卸载控件库时调用；不调用也不会泄漏到下次启动）。</summary>
        public static void Release()
        {
            if (_cachedFont != null)
            {
                _cachedFont.Dispose();
                _cachedFont = null;
                _cachedSize = float.NaN;
            }
        }
    }
}
