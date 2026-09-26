using System;
using System.Collections.Generic;
using System.Drawing;
using VisionSystem.Controls.Rendering;

namespace VisionSystem.Controls.Geometry
{
    /// <summary>
    /// 旋转矩形：中心 + 宽高 + 旋转角度（度，顺时针为正，X 轴为 0°）。
    /// 这是 OpenCV 中 <c>RotatedRect</c> 的等价表示。
    /// </summary>
    public class RotatedRectShape : IGeometryShape
    {
        /// <summary>允许的最小边长（图像像素）。</summary>
        public const float MinSize = 1f;

        /// <summary>旋转手柄的控制点序号。</summary>
        public const int RotateHandleIndex = 4;

        /// <summary>创建空旋转矩形。</summary>
        public RotatedRectShape()
        {
            Name = "RotatedRect";
        }

        /// <summary>以中心、宽高、角度创建旋转矩形。</summary>
        /// <param name="center">中心点（图像坐标）。</param>
        /// <param name="width">沿局部 X 轴的长度。</param>
        /// <param name="height">沿局部 Y 轴的长度。</param>
        /// <param name="angleDeg">旋转角度（度）。</param>
        public RotatedRectShape(PointF center, float width, float height, float angleDeg)
        {
            Name = "RotatedRect";
            Center = center;
            Width = width;
            Height = height;
            AngleDeg = angleDeg;
        }

        /// <summary>中心点（图像坐标）。</summary>
        public PointF Center { get; set; }

        /// <summary>宽度（沿旋转后的局部 X 轴）。</summary>
        public float Width
        {
            get { return _width; }
            set { _width = Math.Max(Math.Abs(value), MinSize); }
        }
        private float _width = 20f;

        /// <summary>高度（沿旋转后的局部 Y 轴）。</summary>
        public float Height
        {
            get { return _height; }
            set { _height = Math.Max(Math.Abs(value), MinSize); }
        }
        private float _height = 10f;

        /// <summary>旋转角度（度）。设置时自动归一化到 (-180, 180]。</summary>
        public float AngleDeg
        {
            get { return _angleDeg; }
            set { _angleDeg = NormalizeAngle(value); }
        }
        private float _angleDeg;

        /// <summary>旋转角度（弧度）。</summary>
        public double AngleRad
        {
            get { return _angleDeg * Math.PI / 180.0; }
            set { AngleDeg = (float)(value * 180.0 / Math.PI); }
        }

        public string Name { get; set; }
        public object Tag { get; set; }
        public Color StrokeColor { get; set; } = Color.DeepSkyBlue;
        public float LineWidth { get; set; } = 2f;
        public bool Visible { get; set; } = true;
        public bool IsSelected { get; set; }
        public bool IsFilled { get; set; }
        public Color FillColor { get; set; } = Color.FromArgb(60, 0, 191, 255);
        public bool IsEditable { get; set; } = true;

        public RectangleF Bounds
        {
            get
            {
                PointF[] pts = GetCorners();
                float minX = float.MaxValue, minY = float.MaxValue;
                float maxX = float.MinValue, maxY = float.MinValue;
                for (int i = 0; i < pts.Length; i++)
                {
                    if (pts[i].X < minX) minX = pts[i].X;
                    if (pts[i].Y < minY) minY = pts[i].Y;
                    if (pts[i].X > maxX) maxX = pts[i].X;
                    if (pts[i].Y > maxY) maxY = pts[i].Y;
                }
                return new RectangleF(minX, minY, maxX - minX, maxY - minY);
            }
        }

        /// <summary>
        /// 计算四个角点（图像坐标）。
        /// 顺序为：0=左上、1=右上、2=右下、3=左下（相对于旋转后的局部坐标系）。
        /// </summary>
        public PointF[] GetCorners()
        {
            double a = _angleDeg * Math.PI / 180.0;
            float cos = (float)Math.Cos(a);
            float sin = (float)Math.Sin(a);
            float hw = _width * 0.5f;
            float hh = _height * 0.5f;

            var local = new[]
            {
                new PointF(-hw, -hh),
                new PointF( hw, -hh),
                new PointF( hw,  hh),
                new PointF(-hw,  hh)
            };

            var world = new PointF[4];
            for (int i = 0; i < 4; i++)
            {
                world[i] = new PointF(
                    Center.X + cos * local[i].X - sin * local[i].Y,
                    Center.Y + sin * local[i].X + cos * local[i].Y);
            }
            return world;
        }

        /// <summary>把图像坐标点转换到以中心为原点、已消除旋转的局部坐标。</summary>
        public PointF ToLocal(PointF imagePt)
        {
            double a = -_angleDeg * Math.PI / 180.0;
            float cos = (float)Math.Cos(a);
            float sin = (float)Math.Sin(a);
            float dx = imagePt.X - Center.X;
            float dy = imagePt.Y - Center.Y;
            return new PointF(cos * dx - sin * dy, sin * dx + cos * dy);
        }

        /// <summary>把局部坐标点转换回图像坐标。</summary>
        public PointF ToWorld(PointF localPt)
        {
            double a = _angleDeg * Math.PI / 180.0;
            float cos = (float)Math.Cos(a);
            float sin = (float)Math.Sin(a);
            return new PointF(
                Center.X + cos * localPt.X - sin * localPt.Y,
                Center.Y + sin * localPt.X + cos * localPt.Y);
        }

        public void Draw(Graphics g, Viewport vp, RenderOptions options)
        {
            if (!Visible || g == null || vp == null) return;

            PointF[] corners = GetCorners();
            var screen = new PointF[4];
            for (int i = 0; i < 4; i++)
                screen[i] = vp.ImageToControl(corners[i]);

            if (options != null && options.AntiAlias)
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            if (IsFilled)
            {
                using (Brush brush = new SolidBrush(FillColor))
                    g.FillPolygon(brush, screen);
            }

            using (Pen pen = new Pen(StrokeColor, Math.Max(LineWidth, 0.5f)))
            {
                g.DrawPolygon(pen, screen);
            }

            // 绘制局部 X 轴方向指示线，便于判断朝向
            PointF axisEnd = ToWorld(new PointF(_width * 0.5f, 0f));
            using (Pen axisPen = new Pen(Color.FromArgb(150, StrokeColor), 1f))
            {
                g.DrawLine(axisPen, vp.ImageToControl(Center), vp.ImageToControl(axisEnd));
            }
        }

        public bool HitTest(PointF imagePt, float tolerance)
        {
            PointF local = ToLocal(imagePt);
            float hw = _width * 0.5f;
            float hh = _height * 0.5f;

            bool outer = Math.Abs(local.X) <= hw + tolerance && Math.Abs(local.Y) <= hh + tolerance;
            if (!outer) return false;
            if (IsFilled) return true;

            bool inner = Math.Abs(local.X) < hw - tolerance && Math.Abs(local.Y) < hh - tolerance;
            return !inner;
        }

        public void Translate(float dx, float dy)
        {
            Center = new PointF(Center.X + dx, Center.Y + dy);
        }

        public IList<ShapeHandle> GetHandles()
        {
            PointF[] corners = GetCorners();
            var list = new List<ShapeHandle>(5);

            for (int i = 0; i < 4; i++)
                list.Add(new ShapeHandle(corners[i], HandleKind.Resize, i, "C" + i));

            // 旋转手柄：位于局部 +X 轴外侧
            float margin = Math.Max(_width, _height) * 0.18f;
            PointF rotatePos = ToWorld(new PointF(_width * 0.5f + margin, 0f));
            list.Add(new ShapeHandle(rotatePos, HandleKind.Rotate, RotateHandleIndex, "Rotate"));

            return list;
        }

        public void SetHandlePosition(int handleIndex, PointF imagePt)
        {
            if (handleIndex == RotateHandleIndex)
            {
                AngleDeg = (float)(Math.Atan2(imagePt.Y - Center.Y, imagePt.X - Center.X) * 180.0 / Math.PI);
                return;
            }

            if (handleIndex < 0 || handleIndex > 3) return;

            // 对角固定的缩放：拖动的角变到新位置，对角保持不动
            PointF[] corners = GetCorners();
            PointF fixedCorner = corners[(handleIndex + 2) % 4];

            float dx = imagePt.X - fixedCorner.X;
            float dy = imagePt.Y - fixedCorner.Y;

            double a = _angleDeg * Math.PI / 180.0;
            double cos = Math.Cos(a), sin = Math.Sin(a);
            float lx = (float)(cos * dx + sin * dy);   // R^T * d 的 X 分量
            float ly = (float)(-sin * dx + cos * dy);  // R^T * d 的 Y 分量

            Width = Math.Abs(lx);
            Height = Math.Abs(ly);
            Center = new PointF((fixedCorner.X + imagePt.X) * 0.5f, (fixedCorner.Y + imagePt.Y) * 0.5f);
        }

        public IGeometryShape Clone()
        {
            var copy = new RotatedRectShape(Center, _width, _height, _angleDeg);
            CopyStyleTo(copy);
            return copy;
        }

        /// <summary>把显示样式复制到另一个旋转矩形。</summary>
        protected void CopyStyleTo(RotatedRectShape target)
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
                "RotatedRect  C=({0:F2}, {1:F2})  {2:F2} x {3:F2}  A={4:F2}deg",
                Center.X, Center.Y, _width, _height, _angleDeg);
        }

        public override string ToString()
        {
            return ToGeometryString();
        }

        private static float NormalizeAngle(float deg)
        {
            if (float.IsNaN(deg) || float.IsInfinity(deg)) return 0f;
            double d = deg % 360.0;
            if (d > 180.0) d -= 360.0;
            if (d <= -180.0) d += 360.0;
            return (float)d;
        }
    }
}
