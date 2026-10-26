using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
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

        /// <summary>
        /// 旋转手柄的控制点序号。<strong>8 = 「4 角(0..3) + 4 边中点(4..7) + 旋转(8)」</strong>；
        /// 原为 4，加入边中点手柄后 4..7 让给上下左右，旋转挪到 8。
        /// </summary>
        public const int RotateHandleIndex = 8;

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

        /// <summary>
        /// 可选标签文本：非空时在<strong>旋转后的左上角</strong>外侧绘制（文字保持水平）。
        /// <para>空值不画，保证既有生产用法（<c>FlowShapes.MatchBox</c> 的结果框）视觉零变化。</para>
        /// </summary>
        public string Label { get; set; }

        /// <summary>
        /// 旋转吸附步长（度）；0 = 不吸附。由宿主（<c>ImageViewer</c> 读 Shift）按需设置，
        /// 几何类不引用 WinForms。
        /// </summary>
        public float SnapStepDeg { get; set; }

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

            // 选中高亮：与 RectangleShape 同款的半透明白外框（向外扩 2 屏幕像素）。
            // 形状匹配的结果框（FlowShapes.MatchBox）正是本类型，缺了它「点结果表格行」在图像上没反应。
            if (IsSelected)
            {
                using (Pen pen = new Pen(Color.FromArgb(120, 255, 255, 255), 2f))
                    g.DrawPolygon(pen, OffsetOutward(screen, 2f));
            }

            // 绘制局部 X 轴方向指示线，便于判断朝向
            PointF axisEnd = ToWorld(new PointF(_width * 0.5f, 0f));
            using (Pen axisPen = new Pen(Color.FromArgb(150, StrokeColor), 1f))
            {
                g.DrawLine(axisPen, vp.ImageToControl(Center), vp.ImageToControl(axisEnd));
            }

            // 标签：样式照抄 RectangleShape.Draw 的标签段（9pt Bold + 半透明黑底 + 顶部越界落框内），
            // 但**文字保持水平**（大角度下旋转文字不可读），定位在旋转后的左上角顶点外侧。
            // g.Save/Restore 包裹：让本段改成任何绘制方式都不会把 Graphics 状态泄漏给同帧的其它图形。
            if (!string.IsNullOrEmpty(Label))
            {
                GraphicsState state = g.Save();
                try
                {
                    using (var font = new Font("Segoe UI", 9f, FontStyle.Bold))
                    using (var textBrush = new SolidBrush(Color.FromArgb(235, StrokeColor.R, StrokeColor.G, StrokeColor.B)))
                    using (var bgBrush = new SolidBrush(Color.FromArgb(170, 0, 0, 0)))
                    {
                        PointF anchor = screen[0];      // 旋转后的左上角（屏幕坐标）
                        SizeF sz = g.MeasureString(Label, font);

                        float bx = anchor.X;
                        float by = anchor.Y - sz.Height - 3f;
                        if (by < 0f) by = anchor.Y + 3f;     // 顶部越界则画在框内

                        g.FillRectangle(bgBrush, bx, by, sz.Width + 6f, sz.Height + 3f);
                        g.DrawString(Label, font, textBrush, bx + 3f, by + 1f);
                    }
                }
                finally
                {
                    g.Restore(state);
                }
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
            var list = new List<ShapeHandle>(9);

            // 0..3 四个角点：索引与顺序保持现状不动（与 RectangleShape 的 0..3 对齐）
            for (int i = 0; i < 4; i++)
                list.Add(new ShapeHandle(corners[i], HandleKind.Resize, i, "C" + i));

            // 4..7 四个边中点，顺序 T / R / B / L（与 RectangleShape 的 4..7 同序同语义）
            float hw = _width * 0.5f;
            float hh = _height * 0.5f;
            list.Add(new ShapeHandle(ToWorld(new PointF(0f, -hh)), HandleKind.Resize, 4, "T"));
            list.Add(new ShapeHandle(ToWorld(new PointF(hw, 0f)), HandleKind.Resize, 5, "R"));
            list.Add(new ShapeHandle(ToWorld(new PointF(0f, hh)), HandleKind.Resize, 6, "B"));
            list.Add(new ShapeHandle(ToWorld(new PointF(-hw, 0f)), HandleKind.Resize, 7, "L"));

            // 8 = 旋转手柄：位于局部 +X 轴外侧
            float margin = Math.Max(_width, _height) * 0.18f;
            PointF rotatePos = ToWorld(new PointF(hw + margin, 0f));
            list.Add(new ShapeHandle(rotatePos, HandleKind.Rotate, RotateHandleIndex, "Rotate"));

            return list;
        }

        public void SetHandlePosition(int handleIndex, PointF imagePt)
        {
            if (handleIndex == RotateHandleIndex)
            {
                float deg = (float)(Math.Atan2(imagePt.Y - Center.Y, imagePt.X - Center.X) * 180.0 / Math.PI);

                // 吸附（Shift）：只在旋转分支里做；步长由宿主按当前修饰键设置
                if (SnapStepDeg > 0.5f)
                    deg = (float)Math.Round(deg / SnapStepDeg) * SnapStepDeg;

                AngleDeg = deg;      // setter 负责归一化到 (-180, 180]
                return;
            }

            if (handleIndex >= 4 && handleIndex <= 7)
            {
                ResizeFromEdge(handleIndex, imagePt);
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

        /// <summary>
        /// 边中点手柄（4=T 5=R 6=B 7=L）：<strong>只改一个维度</strong>、对边固定、角度不变。
        /// <para>
        /// 必须先把拖动点反旋转到<strong>局部坐标</strong>再算：在图像坐标里直接加减，
        /// 带角度时会算到另一个轴上（矩形会"斜着变形"）。
        /// </para>
        /// </summary>
        private void ResizeFromEdge(int handleIndex, PointF imagePt)
        {
            PointF local = ToLocal(imagePt);        // 相对当前中心、已消除旋转
            float hw = _width * 0.5f;
            float hh = _height * 0.5f;

            float newHw = hw;
            float newHh = hh;
            var centerLocal = new PointF(0f, 0f);

            switch (handleIndex)
            {
                case 4:   // T：局部上边（y = -hh）动，下边（y = +hh）固定
                    newHh = Math.Abs(hh - local.Y) * 0.5f;
                    centerLocal.Y = (hh + local.Y) * 0.5f;
                    break;

                case 5:   // R：局部右边（x = +hw）动，左边（x = -hw）固定
                    newHw = Math.Abs(local.X + hw) * 0.5f;
                    centerLocal.X = (local.X - hw) * 0.5f;
                    break;

                case 6:   // B：局部下边（y = +hh）动，上边（y = -hh）固定
                    newHh = Math.Abs(local.Y + hh) * 0.5f;
                    centerLocal.Y = (local.Y - hh) * 0.5f;
                    break;

                default:  // 7 = L：局部左边（x = -hw）动，右边（x = +hw）固定
                    newHw = Math.Abs(hw - local.X) * 0.5f;
                    centerLocal.X = (hw + local.X) * 0.5f;
                    break;
            }

            Width = newHw * 2f;
            Height = newHh * 2f;
            Center = ToWorld(centerLocal);          // 角度不变，只把新中心放回世界坐标
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
                "RotatedRect  C=({0:F2}, {1:F2})  {2:F2} x {3:F2}  A={4:F2}deg",
                Center.X, Center.Y, _width, _height, _angleDeg);
        }

        public override string ToString()
        {
            return ToGeometryString();
        }

        /// <summary>
        /// 把多边形顶点沿「远离中心」的方向整体外扩 <paramref name="dist"/>（屏幕像素），
        /// 用于绘制选中高亮外框（轴对齐矩形可以直接 -2/+4，旋转矩形只能按顶点推）。
        /// </summary>
        private static PointF[] OffsetOutward(PointF[] pts, float dist)
        {
            if (pts == null || pts.Length == 0) return pts;

            float cx = 0f, cy = 0f;
            for (int i = 0; i < pts.Length; i++) { cx += pts[i].X; cy += pts[i].Y; }
            cx /= pts.Length;
            cy /= pts.Length;

            var result = new PointF[pts.Length];
            for (int i = 0; i < pts.Length; i++)
            {
                float dx = pts[i].X - cx;
                float dy = pts[i].Y - cy;
                float len = (float)Math.Sqrt(dx * dx + dy * dy);
                if (len < 0.0001f) { result[i] = pts[i]; continue; }
                result[i] = new PointF(pts[i].X + dx / len * dist, pts[i].Y + dy / len * dist);
            }
            return result;
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
