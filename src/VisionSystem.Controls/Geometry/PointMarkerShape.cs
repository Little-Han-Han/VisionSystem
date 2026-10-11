using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using VisionSystem.Controls.Rendering;

namespace VisionSystem.Controls.Geometry
{
    /// <summary>点标记的绘制形态。</summary>
    public enum MarkerKind
    {
        /// <summary>实心 / 空心方块（默认）。</summary>
        Square,

        /// <summary>十字线。</summary>
        Cross
    }

    /// <summary>
    /// 点标记：中心 <see cref="Center"/> 为<strong>图像坐标</strong>，
    /// 边长 <see cref="Size"/> 为<strong>屏幕像素</strong>（任意缩放下屏幕尺寸恒定）。
    /// 用于卡尺命中的边缘点、Blob 质心十字、基准点等「只表示位置、不表示大小」的标注。
    /// </summary>
    /// <remarks>
    /// 由于尺寸是屏幕像素而 <see cref="Bounds"/> 要求返回图像坐标矩形，
    /// 且该矩形与当前缩放相关（本图形不持有视口），故 <see cref="Bounds"/>
    /// 返回以中心为核心的 1×1 图像像素矩形——仅表示位置，不表示实际占位。
    /// 需要按屏幕尺寸做区域判定时请使用 <see cref="HitTest"/> 或自行按
    /// <see cref="Viewport.UnscaleLength"/> 换算。
    ///
    /// 外部直接改动 <see cref="Center"/> / <see cref="Size"/> 后，需调用所属
    /// <see cref="ShapeCollection.NotifyChanged"/> 才会重绘（全库既有约定）。
    /// </remarks>
    public class PointMarkerShape : IGeometryShape
    {
        /// <summary>允许的标记最小屏幕尺寸。</summary>
        public const float MinSize = 2f;

        /// <summary>默认屏幕尺寸（px）。</summary>
        public const float DefaultSize = 9f;

        /// <summary>创建空点标记（屏幕默认尺寸、方块）。</summary>
        public PointMarkerShape()
        {
            Name = "PointMarker";
        }

        /// <summary>以中心创建点标记。</summary>
        /// <param name="center">中心点（图像坐标）。</param>
        /// <param name="size">屏幕尺寸（px），可选。</param>
        /// <param name="kind">绘制形态，可选。</param>
        public PointMarkerShape(PointF center, float size = DefaultSize, MarkerKind kind = MarkerKind.Square)
        {
            Name = "PointMarker";
            Center = center;
            Size = size;
            Kind = kind;
        }

        /// <summary>中心点（图像坐标）。</summary>
        public PointF Center { get; set; }

        /// <summary>
        /// 标记尺寸（<strong>屏幕像素</strong>，方块的边长 / 十字的臂长），
        /// 设置时自动限制不小于 <see cref="MinSize"/>。
        /// </summary>
        public float Size
        {
            get { return _size; }
            set { _size = Math.Max(value, MinSize); }
        }
        private float _size = DefaultSize;

        /// <summary>绘制形态。</summary>
        public MarkerKind Kind { get; set; } = MarkerKind.Square;

        public string Name { get; set; }
        public object Tag { get; set; }
        public Color StrokeColor { get; set; } = Color.Yellow;
        public float LineWidth { get; set; } = 1.5f;
        public bool Visible { get; set; } = true;
        public bool IsSelected { get; set; }
        public bool IsFilled { get; set; } = true;
        public Color FillColor { get; set; } = Color.FromArgb(220, 255, 255, 0);
        public bool IsEditable { get; set; } = true;

        /// <summary>
        /// 仅表示位置的 1×1 图像像素矩形（尺寸为屏幕像素，无法脱离视口换算，见类注释）。
        /// </summary>
        public RectangleF Bounds
        {
            get { return new RectangleF(Center.X - 0.5f, Center.Y - 0.5f, 1f, 1f); }
        }

        public void Draw(Graphics g, Viewport vp, RenderOptions options)
        {
            if (!Visible || g == null || vp == null) return;

            PointF c = vp.ImageToControl(Center);
            float half = _size * 0.5f;   // 屏幕像素，不随缩放变化

            if (options != null && options.AntiAlias)
                g.SmoothingMode = SmoothingMode.AntiAlias;

            if (Kind == MarkerKind.Cross)
            {
                using (Pen pen = new Pen(StrokeColor, Math.Max(LineWidth, 0.5f)))
                {
                    g.DrawLine(pen, c.X - half, c.Y, c.X + half, c.Y);
                    g.DrawLine(pen, c.X, c.Y - half, c.X, c.Y + half);
                }
            }
            else
            {
                var rect = new RectangleF(c.X - half, c.Y - half, _size, _size);
                if (IsFilled)
                {
                    using (Brush brush = new SolidBrush(FillColor))
                        g.FillRectangle(brush, rect);
                }
                using (Pen pen = new Pen(StrokeColor, Math.Max(LineWidth, 0.5f)))
                {
                    g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
                }
            }

            if (IsSelected)
            {
                using (Pen pen = new Pen(Color.FromArgb(140, 255, 255, 255), 1f))
                {
                    g.DrawRectangle(pen, c.X - half - 2f, c.Y - half - 2f, _size + 4f, _size + 4f);
                }
            }
        }

        public bool HitTest(PointF imagePt, float tolerance)
        {
            // tolerance 已由控件从屏幕像素换算成图像坐标；标记的半尺寸是屏幕像素，
            // 这里按 1:1 保守折算后相加（命中区域宁可略大，不漏点）。
            float reach = tolerance + _size * 0.5f;
            float dx = imagePt.X - Center.X;
            float dy = imagePt.Y - Center.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy) <= reach;
        }

        public void Translate(float dx, float dy)
        {
            Center = new PointF(Center.X + dx, Center.Y + dy);
        }

        public IList<ShapeHandle> GetHandles()
        {
            return new List<ShapeHandle>(1)
            {
                new ShapeHandle(Center, HandleKind.Move, 0, "Center")
            };
        }

        public void SetHandlePosition(int handleIndex, PointF imagePt)
        {
            if (handleIndex == 0)
                Center = imagePt;
        }

        public IGeometryShape Clone()
        {
            var copy = new PointMarkerShape(Center, _size, Kind);
            CopyStyleTo(copy);
            return copy;
        }

        /// <summary>把显示样式复制到另一个点标记。</summary>
        protected void CopyStyleTo(PointMarkerShape target)
        {
            if (target == null) return;
            target.Name = Name;
            target.Tag = Tag;
            target.StrokeColor = StrokeColor;
            target.LineWidth = LineWidth;
            target.Visible = Visible;
            target.IsSelected = IsSelected;
            target.IsFilled = IsFilled;
            target.FillColor = FillColor;
            target.IsEditable = IsEditable;
        }

        public string ToGeometryString()
        {
            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "Point  ({0:F2}, {1:F2})  size={2:F1}px  {3}",
                Center.X, Center.Y, _size, Kind);
        }

        public override string ToString()
        {
            return ToGeometryString();
        }
    }
}
