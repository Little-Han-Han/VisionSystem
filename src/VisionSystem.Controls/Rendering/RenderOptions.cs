using System.Drawing;

namespace VisionSystem.Controls.Rendering
{
    /// <summary>
    /// 控件的渲染选项（背景、网格、控制点外观等）。
    /// 该对象由 <see cref="ImageViewer"/> 持有，可直接在属性窗口中修改。
    /// </summary>
    public class RenderOptions
    {
        /// <summary>控件背景色（图像之外的区域）。</summary>
        public Color BackdropColor { get; set; } = Color.FromArgb(32, 32, 36);

        /// <summary>图像边框颜色。</summary>
        public Color ImageBorderColor { get; set; } = Color.FromArgb(90, 90, 100);

        /// <summary>是否绘制图像边框。</summary>
        public bool ShowImageBorder { get; set; } = true;

        /// <summary>放大到该倍率以上时开始绘制像素网格。</summary>
        public float PixelGridMinZoom { get; set; } = 8f;

        /// <summary>像素网格颜色。</summary>
        public Color PixelGridColor { get; set; } = Color.FromArgb(60, 120, 120, 130);

        /// <summary>是否绘制像素网格。</summary>
        public bool ShowPixelGrid { get; set; } = true;

        /// <summary>是否绘制鼠标十字线。</summary>
        public bool ShowCrosshair { get; set; } = false;

        /// <summary>十字线颜色。</summary>
        public Color CrosshairColor { get; set; } = Color.FromArgb(120, 0, 200, 255);

        /// <summary>控制点（手柄）边长，单位为屏幕像素，不随缩放变化。</summary>
        public float HandleSize { get; set; } = 7f;

        /// <summary>控制点填充色。</summary>
        public Color HandleFillColor { get; set; } = Color.White;

        /// <summary>控制点边框色。</summary>
        public Color HandleBorderColor { get; set; } = Color.FromArgb(30, 30, 30);

        /// <summary>选中图形的高亮色（用于绘制包围盒虚线）。</summary>
        public Color SelectionColor { get; set; } = Color.FromArgb(0, 200, 255);

        /// <summary>绘制图形时是否使用抗锯齿。</summary>
        public bool AntiAlias { get; set; } = true;

        /// <summary>是否在右下角显示鼠标位置面板（图像坐标 + 灰度值或 RGB 值）。</summary>
        public bool ShowHud { get; set; } = true;

        /// <summary>
        /// 结果标签的字号（<strong>屏幕像素</strong>，不随缩放变化）。<c>&lt;= 0</c> 表示不画标签。
        /// <para>
        /// 【为什么可调】同一份结果在不同场景要不同密度：单个目标时标签大一点好读，
        /// 而 Blob 一次 20 个框时，9pt 粗体的标签会把画面糊满（用户反馈的"字体过大、显示不全"）。
        /// </para>
        /// </summary>
        public float LabelFontSize { get; set; } = 8f;

        /// <summary>
        /// 标签可绘制区域（<strong>控件坐标</strong>）。<see cref="RectangleF.Empty"/> = 不裁剪。
        /// <para>
        /// 【为什么需要它】标签画在框的左上角外侧，靠右/靠下的框会把标签画到画面外 ——
        /// 看起来像"结果丢了"。由 <see cref="ImageViewer"/> 每帧填入客户区矩形，标签自动往内挪。
        /// </para>
        /// </summary>
        public RectangleF LabelClipBounds { get; set; } = RectangleF.Empty;

        /// <summary>HUD 面板背景色。</summary>
        public Color HudBackColor { get; set; } = Color.FromArgb(190, 18, 18, 22);

        /// <summary>HUD 面板文字颜色。</summary>
        public Color HudForeColor { get; set; } = Color.Gainsboro;

        /// <summary>HUD 面板边框色。</summary>
        public Color HudBorderColor { get; set; } = Color.FromArgb(90, 130, 140, 150);

        /// <summary>放大时使用的插值模式（默认邻近采样，保持像素清晰）。</summary>
        public System.Drawing.Drawing2D.InterpolationMode ZoomInInterpolation { get; set; }
            = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;

        /// <summary>缩小（Zoom &lt; 1）时使用的插值模式。</summary>
        public System.Drawing.Drawing2D.InterpolationMode ZoomOutInterpolation { get; set; }
            = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;

        /// <summary>恢复默认值。</summary>
        public void Reset()
        {
            BackdropColor = Color.FromArgb(32, 32, 36);
            ImageBorderColor = Color.FromArgb(90, 90, 100);
            ShowImageBorder = true;
            PixelGridMinZoom = 8f;
            PixelGridColor = Color.FromArgb(60, 120, 120, 130);
            ShowPixelGrid = true;
            ShowCrosshair = false;
            CrosshairColor = Color.FromArgb(120, 0, 200, 255);
            HandleSize = 7f;
            HandleFillColor = Color.White;
            HandleBorderColor = Color.FromArgb(30, 30, 30);
            SelectionColor = Color.FromArgb(0, 200, 255);
            AntiAlias = true;
            ShowHud = true;
            HudBackColor = Color.FromArgb(190, 18, 18, 22);
            HudForeColor = Color.Gainsboro;
            HudBorderColor = Color.FromArgb(90, 130, 140, 150);
            ZoomInInterpolation = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            ZoomOutInterpolation = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        }
    }
}
