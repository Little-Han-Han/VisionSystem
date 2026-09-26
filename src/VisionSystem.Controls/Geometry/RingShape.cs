using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using VisionSystem.Controls.Rendering;

namespace VisionSystem.Controls.Geometry
{
    /// <summary>
    /// 圆环（同心圆环带）：圆心 + 内半径 + 外半径。
    /// 内半径为 0 时退化为实心圆，常用于环形检测区、同心度测量等场景。
    /// </summary>
    public class RingShape : IGeometryShape
    {
        /// <summary>允许的最小外半径（图像像素）。</summary>
        public const float MinRadius = 1f;

        /// <summary>环带的最小厚度（图像像素），保证内半径始终小于外半径。</summary>
        public const float MinGap = 1f;

        /// <summary>创建空圆环。</summary>
        public RingShape()
        {
            Name = "Ring";
        }

        /// <summary>以圆心和内外半径创建圆环。</summary>
        public RingShape(PointF center, float outerRadius, float innerRadius)
        {
            Name = "Ring";
            Center = center;
            OuterRadius = outerRadius;
            InnerRadius = innerRadius;
        }

        /// <summary>圆心（图像坐标）。</summary>
        public PointF Center { get; set; }

        /// <summary>外半径。设置时自动保证不小于内半径 + <see cref="MinGap"/>。</summary>
        public float OuterRadius
        {
            get { return _outer; }
            set
            {
                _outer = Math.Max(value, MinRadius);
                float maxInner = _outer - MinGap;
                if (_inner > maxInner) _inner = Math.Max(0f, maxInner);
            }
        }
        private float _outer = 20f;

        /// <summary>内半径。设置时自动裁剪到 [0, 外半径 - MinGap]，为 0 时即为实心圆。</summary>
        public float InnerRadius
        {
            get { return _inner; }
            set
            {
                float maxInner = Math.Max(0f, _outer - MinGap);
                _inner = Math.Max(0f, Math.Min(value, maxInner));
            }
        }
        private float _inner = 10f;

        /// <summary>环带厚度（外半径 - 内半径）。设置时保持外半径不变，反推内半径。</summary>
        public float Thickness
        {
            get { return _outer - _inner; }
            set { InnerRadius = _outer - value; }
        }

        public string Name { get; set; }
        public object Tag { get; set; }
        public Color StrokeColor { get; set; } = Color.Violet;
        public float LineWidth { get; set; } = 2f;
        public bool Visible { get; set; } = true;
        public bool IsSelected { get; set; }
        public bool IsFilled { get; set; }
        public Color FillColor { get; set; } = Color.FromArgb(60, 238, 130, 238);
        public bool IsEditable { get; set; } = true;

        public RectangleF Bounds
        {
            get { return new RectangleF(Center.X - _outer, Center.Y - _outer, _outer * 2f, _outer * 2f); }
        }

        public void Draw(Graphics g, Viewport vp, RenderOptions options)
        {
            if (!Visible || g == null || vp == null) return;

            PointF c = vp.ImageToControl(Center);
            float ro = vp.ScaleLength(_outer);
            float ri = vp.ScaleLength(_inner);
            if (ro <= 0.1f) return;

            if (options != null && options.AntiAlias)
                g.SmoothingMode = SmoothingMode.AntiAlias;

            // 填充环带：两个同心圆路径按 Alternate 填充，重叠区域自动镂空
            if (IsFilled && ri > 0.5f)
            {
                using (GraphicsPath path = new GraphicsPath())
                {
                    path.AddEllipse(c.X - ro, c.Y - ro, ro * 2f, ro * 2f);
                    path.AddEllipse(c.X - ri, c.Y - ri, ri * 2f, ri * 2f);
                    using (Brush brush = new SolidBrush(FillColor))
                        g.FillPath(brush, path);
                }
            }

            using (Pen pen = new Pen(StrokeColor, Math.Max(LineWidth, 0.5f)))
            {
                g.DrawEllipse(pen, c.X - ro, c.Y - ro, ro * 2f, ro * 2f);
                if (ri > 0.5f)
                    g.DrawEllipse(pen, c.X - ri, c.Y - ri, ri * 2f, ri * 2f);
            }

            if (IsSelected)
            {
                using (Pen pen = new Pen(Color.FromArgb(120, 255, 255, 255), 1f))
                    g.DrawEllipse(pen, c.X - ro - 2f, c.Y - ro - 2f, ro * 2f + 4f, ro * 2f + 4f);
            }
        }

        public bool HitTest(PointF imagePt, float tolerance)
        {
            float dx = imagePt.X - Center.X;
            float dy = imagePt.Y - Center.Y;
            float d = (float)Math.Sqrt(dx * dx + dy * dy);

            if (IsFilled)
            {
                // 填充模式：命中整个环带
                return d <= _outer + tolerance && d >= _inner - tolerance;
            }

            // 空心模式：只命中内外两条圆周
            return Math.Abs(d - _outer) <= tolerance
                || (_inner > 0f && Math.Abs(d - _inner) <= tolerance);
        }

        public void Translate(float dx, float dy)
        {
            Center = new PointF(Center.X + dx, Center.Y + dy);
        }

        public IList<ShapeHandle> GetHandles()
        {
            var list = new List<ShapeHandle>(9);

            // 0：圆心，整体移动
            list.Add(new ShapeHandle(Center, HandleKind.Move, 0, "Center"));

            // 1~4：外圆四个方向点
            list.Add(new ShapeHandle(new PointF(Center.X + _outer, Center.Y), HandleKind.Resize, 1, "Outer-E"));
            list.Add(new ShapeHandle(new PointF(Center.X, Center.Y + _outer), HandleKind.Resize, 2, "Outer-S"));
            list.Add(new ShapeHandle(new PointF(Center.X - _outer, Center.Y), HandleKind.Resize, 3, "Outer-W"));
            list.Add(new ShapeHandle(new PointF(Center.X, Center.Y - _outer), HandleKind.Resize, 4, "Outer-N"));

            // 5~8：内圆四个方向点
            list.Add(new ShapeHandle(new PointF(Center.X + _inner, Center.Y), HandleKind.Resize, 5, "Inner-E"));
            list.Add(new ShapeHandle(new PointF(Center.X, Center.Y + _inner), HandleKind.Resize, 6, "Inner-S"));
            list.Add(new ShapeHandle(new PointF(Center.X - _inner, Center.Y), HandleKind.Resize, 7, "Inner-W"));
            list.Add(new ShapeHandle(new PointF(Center.X, Center.Y - _inner), HandleKind.Resize, 8, "Inner-N"));

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
            float d = (float)Math.Sqrt(dx * dx + dy * dy);

            if (handleIndex >= 1 && handleIndex <= 4)
            {
                OuterRadius = d;
            }
            else if (handleIndex >= 5 && handleIndex <= 8)
            {
                InnerRadius = d;
            }
        }

        public IGeometryShape Clone()
        {
            var copy = new RingShape(Center, _outer, _inner);
            CopyStyleTo(copy);
            return copy;
        }

        /// <summary>把显示样式复制到另一个圆环。</summary>
        protected void CopyStyleTo(RingShape target)
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
                "Ring  C=({0:F2}, {1:F2})  Inner={2:F2}  Outer={3:F2}",
                Center.X, Center.Y, _inner, _outer);
        }

        public override string ToString()
        {
            return ToGeometryString();
        }
    }
}
