using System;
using System.Collections.Generic;
using System.Drawing;
using VisionSystem.Controls.Rendering;

namespace VisionSystem.Controls.Geometry
{
    /// <summary>圆形：圆心 + 半径。</summary>
    public class CircleShape : IGeometryShape
    {
        /// <summary>允许的最小半径（图像像素）。</summary>
        public const float MinRadius = 1f;

        /// <summary>创建空圆。</summary>
        public CircleShape()
        {
            Name = "Circle";
        }

        /// <summary>以圆心和半径创建圆。</summary>
        public CircleShape(PointF center, float radius)
        {
            Name = "Circle";
            Center = center;
            Radius = Math.Max(radius, MinRadius);
        }

        /// <summary>圆心（图像坐标）。</summary>
        public PointF Center { get; set; }

        /// <summary>半径（图像像素），设置时自动限制不小于 <see cref="MinRadius"/>。</summary>
        public float Radius
        {
            get { return _radius; }
            set { _radius = Math.Max(value, MinRadius); }
        }
        private float _radius = 10f;

        /// <summary>直径。</summary>
        public float Diameter
        {
            get { return _radius * 2f; }
            set { Radius = value * 0.5f; }
        }

        public string Name { get; set; }
        public object Tag { get; set; }
        public Color StrokeColor { get; set; } = Color.Lime;
        public float LineWidth { get; set; } = 2f;
        public bool Visible { get; set; } = true;
        public bool IsSelected { get; set; }
        public bool IsFilled { get; set; }
        public Color FillColor { get; set; } = Color.FromArgb(60, 0, 255, 0);
        public bool IsEditable { get; set; } = true;

        public RectangleF Bounds
        {
            get { return new RectangleF(Center.X - _radius, Center.Y - _radius, _radius * 2f, _radius * 2f); }
        }

        public void Draw(Graphics g, Viewport vp, RenderOptions options)
        {
            if (!Visible || g == null || vp == null) return;

            PointF c = vp.ImageToControl(Center);
            float r = vp.ScaleLength(_radius);
            if (r <= 0.1f) return;

            if (options != null && options.AntiAlias)
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            if (IsFilled)
            {
                using (Brush brush = new SolidBrush(FillColor))
                    g.FillEllipse(brush, c.X - r, c.Y - r, r * 2f, r * 2f);
            }

            using (Pen pen = new Pen(StrokeColor, Math.Max(LineWidth, 0.5f)))
            {
                g.DrawEllipse(pen, c.X - r, c.Y - r, r * 2f, r * 2f);
            }

            if (IsSelected)
            {
                using (Pen pen = new Pen(Color.FromArgb(120, 255, 255, 255), 1f))
                    g.DrawEllipse(pen, c.X - r - 2f, c.Y - r - 2f, r * 2f + 4f, r * 2f + 4f);
            }
        }

        public bool HitTest(PointF imagePt, float tolerance)
        {
            float dx = imagePt.X - Center.X;
            float dy = imagePt.Y - Center.Y;
            float d = (float)Math.Sqrt(dx * dx + dy * dy);

            if (IsFilled) return d <= _radius + tolerance;
            return Math.Abs(d - _radius) <= tolerance;
        }

        public void Translate(float dx, float dy)
        {
            Center = new PointF(Center.X + dx, Center.Y + dy);
        }

        public IList<ShapeHandle> GetHandles()
        {
            var list = new List<ShapeHandle>(5);

            // 0：圆心，用于整体移动
            list.Add(new ShapeHandle(Center, HandleKind.Move, 0, "Center"));

            // 1~4：圆周四个方向点，用于调整半径
            list.Add(new ShapeHandle(new PointF(Center.X + _radius, Center.Y), HandleKind.Resize, 1, "E"));
            list.Add(new ShapeHandle(new PointF(Center.X, Center.Y + _radius), HandleKind.Resize, 2, "S"));
            list.Add(new ShapeHandle(new PointF(Center.X - _radius, Center.Y), HandleKind.Resize, 3, "W"));
            list.Add(new ShapeHandle(new PointF(Center.X, Center.Y - _radius), HandleKind.Resize, 4, "N"));

            return list;
        }

        public void SetHandlePosition(int handleIndex, PointF imagePt)
        {
            if (handleIndex == 0)
            {
                Center = imagePt;
                return;
            }

            float dx = imagePt.X - Center.X;
            float dy = imagePt.Y - Center.Y;
            Radius = (float)Math.Sqrt(dx * dx + dy * dy);
        }

        public IGeometryShape Clone()
        {
            var copy = new CircleShape(Center, _radius);
            CopyStyleTo(copy);
            return copy;
        }

        /// <summary>把显示样式复制到另一个圆。</summary>
        protected void CopyStyleTo(CircleShape target)
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
                "Circle  C=({0:F2}, {1:F2})  R={2:F2}",
                Center.X, Center.Y, _radius);
        }

        public override string ToString()
        {
            return ToGeometryString();
        }
    }
}
