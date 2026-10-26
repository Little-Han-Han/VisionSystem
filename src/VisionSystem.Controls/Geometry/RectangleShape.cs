using System;
using System.Collections.Generic;
using System.Drawing;
using VisionSystem.Controls.Rendering;

namespace VisionSystem.Controls.Geometry
{
    /// <summary>
    /// 轴对齐矩形（不旋转）。以左上角 + 宽高表示，
    /// 内部自动保证 Width/Height 为正。
    /// </summary>
    public class RectangleShape : IGeometryShape
    {
        /// <summary>允许的最小边长（图像像素）。</summary>
        public const float MinSize = 1f;

        /// <summary>创建空矩形。</summary>
        public RectangleShape()
        {
            Name = "Rectangle";
        }

        /// <summary>以左上角和尺寸创建矩形。</summary>
        public RectangleShape(float x, float y, float width, float height)
        {
            Name = "Rectangle";
            X = x; Y = y;
            Width = width; Height = height;
        }

        /// <summary>以 <see cref="RectangleF"/> 创建矩形。</summary>
        public RectangleShape(RectangleF rect)
        {
            Name = "Rectangle";
            X = rect.X; Y = rect.Y;
            Width = rect.Width; Height = rect.Height;
        }

        /// <summary>左上角 X（图像坐标）。</summary>
        public float X { get; set; }

        /// <summary>左上角 Y（图像坐标）。</summary>
        public float Y { get; set; }

        /// <summary>宽度，自动取绝对值并限制不小于 <see cref="MinSize"/>。</summary>
        public float Width
        {
            get { return _width; }
            set { _width = Math.Max(Math.Abs(value), MinSize); }
        }
        private float _width = 10f;

        /// <summary>高度，自动取绝对值并限制不小于 <see cref="MinSize"/>。</summary>
        public float Height
        {
            get { return _height; }
            set { _height = Math.Max(Math.Abs(value), MinSize); }
        }
        private float _height = 10f;

        /// <summary>以 <see cref="RectangleF"/> 形式获取/设置包围矩形。</summary>
        public RectangleF Rect
        {
            get { return new RectangleF(X, Y, _width, _height); }
            set { X = value.X; Y = value.Y; Width = value.Width; Height = value.Height; }
        }

        public string Name { get; set; }
        /// <summary>可选标签文本：非空时在框左上角绘制（目标识别用其显示类别 + 置信度）。</summary>
        public string Label { get; set; }
        public object Tag { get; set; }
        public Color StrokeColor { get; set; } = Color.Orange;
        public float LineWidth { get; set; } = 2f;
        public bool Visible { get; set; } = true;
        public bool IsSelected { get; set; }
        public bool IsFilled { get; set; }
        public Color FillColor { get; set; } = Color.FromArgb(60, 255, 165, 0);
        public bool IsEditable { get; set; } = true;

        public RectangleF Bounds
        {
            get { return Rect; }
        }

        public void Draw(Graphics g, Viewport vp, RenderOptions options)
        {
            if (!Visible || g == null || vp == null) return;

            RectangleF r = vp.ImageToControl(Rect);
            if (r.Width <= 0f || r.Height <= 0f) return;

            if (options != null && options.AntiAlias)
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            if (IsFilled)
            {
                using (Brush brush = new SolidBrush(FillColor))
                    g.FillRectangle(brush, r);
            }

            using (Pen pen = new Pen(StrokeColor, Math.Max(LineWidth, 0.5f)))
            {
                g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
            }

            if (IsSelected)
            {
                using (Pen pen = new Pen(Color.FromArgb(120, 255, 255, 255), 1f))
                    g.DrawRectangle(pen, r.X - 2f, r.Y - 2f, r.Width + 4f, r.Height + 4f);
            }

            // 标签文本（类别名 + 置信度等）：统一走 ShapeLabelRenderer
            // （共享字体 + 8pt + 四边都避让，靠右/靠下的框不会再画到画面外）
            if (!string.IsNullOrEmpty(Label))
                ShapeLabelRenderer.Draw(g, Label, new PointF(r.X, r.Y), StrokeColor, options);
        }

        public bool HitTest(PointF imagePt, float tolerance)
        {
            float left = X, top = Y, right = X + _width, bottom = Y + _height;

            if (IsFilled)
            {
                return imagePt.X >= left - tolerance && imagePt.X <= right + tolerance
                    && imagePt.Y >= top - tolerance && imagePt.Y <= bottom + tolerance;
            }

            bool inner = imagePt.X > left + tolerance && imagePt.X < right - tolerance
                      && imagePt.Y > top + tolerance && imagePt.Y < bottom - tolerance;
            if (inner) return false;

            return imagePt.X >= left - tolerance && imagePt.X <= right + tolerance
                && imagePt.Y >= top - tolerance && imagePt.Y <= bottom + tolerance;
        }

        public void Translate(float dx, float dy)
        {
            X += dx;
            Y += dy;
        }

        public IList<ShapeHandle> GetHandles()
        {
            float l = X, t = Y, r = X + _width, b = Y + _height;
            float mx = X + _width * 0.5f, my = Y + _height * 0.5f;

            return new List<ShapeHandle>(8)
            {
                new ShapeHandle(new PointF(l, t), HandleKind.Resize, 0, "TL"),
                new ShapeHandle(new PointF(r, t), HandleKind.Resize, 1, "TR"),
                new ShapeHandle(new PointF(r, b), HandleKind.Resize, 2, "BR"),
                new ShapeHandle(new PointF(l, b), HandleKind.Resize, 3, "BL"),
                new ShapeHandle(new PointF(mx, t), HandleKind.Resize, 4, "T"),
                new ShapeHandle(new PointF(r, my), HandleKind.Resize, 5, "R"),
                new ShapeHandle(new PointF(mx, b), HandleKind.Resize, 6, "B"),
                new ShapeHandle(new PointF(l, my), HandleKind.Resize, 7, "L")
            };
        }

        public void SetHandlePosition(int handleIndex, PointF imagePt)
        {
            float left = X, top = Y, right = X + _width, bottom = Y + _height;

            switch (handleIndex)
            {
                case 0: left = imagePt.X; top = imagePt.Y; break;
                case 1: right = imagePt.X; top = imagePt.Y; break;
                case 2: right = imagePt.X; bottom = imagePt.Y; break;
                case 3: left = imagePt.X; bottom = imagePt.Y; break;
                case 4: top = imagePt.Y; break;
                case 5: right = imagePt.X; break;
                case 6: bottom = imagePt.Y; break;
                case 7: left = imagePt.X; break;
                default: return;
            }

            // 归一化：允许拖拽时越过对角，自动翻转成正尺寸
            X = Math.Min(left, right);
            Y = Math.Min(top, bottom);
            Width = Math.Abs(right - left);
            Height = Math.Abs(bottom - top);
        }

        public IGeometryShape Clone()
        {
            var copy = new RectangleShape(X, Y, _width, _height);
            CopyStyleTo(copy);
            return copy;
        }

        /// <summary>把显示样式复制到另一个矩形。</summary>
        protected void CopyStyleTo(RectangleShape target)
        {
            if (target == null) return;
            target.Name = Name;
            target.Label = Label;
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
                "Rect  ({0:F2}, {1:F2})  {2:F2} x {3:F2}",
                X, Y, _width, _height);
        }

        public override string ToString()
        {
            return ToGeometryString();
        }
    }
}
