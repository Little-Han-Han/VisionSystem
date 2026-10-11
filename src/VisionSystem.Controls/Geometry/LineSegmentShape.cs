using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using VisionSystem.Controls.Rendering;

namespace VisionSystem.Controls.Geometry
{
    /// <summary>
    /// 线段：两端点 <see cref="P1"/> / <see cref="P2"/> 为<strong>图像坐标</strong>，
    /// 线宽 <see cref="LineWidth"/> 与虚线样式为<strong>屏幕像素</strong>语义
    /// （任意缩放下线宽恒定、虚线间隔恒定）。
    /// 用于拟合直线、卡尺投影窗口、位置修正基准十字、修正后的 ROI 轮廓等。
    /// </summary>
    /// <remarks>
    /// 线段没有内部区域，<see cref="IsFilled"/> 与 <see cref="FillColor"/> 对本图形无效；
    /// 命中测试恒按「点到线段的距离 ≤ 容差」判定。
    ///
    /// 外部直接改动 <see cref="P1"/> / <see cref="P2"/> 后，需调用所属
    /// <see cref="ShapeCollection.NotifyChanged"/> 才会重绘（全库既有约定）。
    /// </remarks>
    public class LineSegmentShape : IGeometryShape
    {
        /// <summary>移动手柄（线段中点）的控制点序号。</summary>
        public const int MoveHandleIndex = 2;

        /// <summary>创建空线段。</summary>
        public LineSegmentShape()
        {
            Name = "LineSegment";
        }

        /// <summary>以两端点创建线段。</summary>
        /// <param name="p1">起点（图像坐标）。</param>
        /// <param name="p2">终点（图像坐标）。</param>
        public LineSegmentShape(PointF p1, PointF p2)
        {
            Name = "LineSegment";
            P1 = p1;
            P2 = p2;
        }

        /// <summary>以四标量创建线段。</summary>
        public LineSegmentShape(float x1, float y1, float x2, float y2)
            : this(new PointF(x1, y1), new PointF(x2, y2))
        {
        }

        /// <summary>起点（图像坐标）。</summary>
        public PointF P1 { get; set; }

        /// <summary>终点（图像坐标）。</summary>
        public PointF P2 { get; set; }

        /// <summary>
        /// 虚线样式（屏幕像素语义）。<see cref="System.Drawing.Drawing2D.DashStyle.Solid"/> 为实线。
        /// 若同时设置了 <see cref="DashPattern"/>，则以自定义间隔为准。
        /// </summary>
        public DashStyle Dash { get; set; } = DashStyle.Solid;

        /// <summary>
        /// 自定义虚线间隔（单位：线宽的倍数，GDI+ 语义）。为 null 或空数组时
        /// 使用 <see cref="Dash"/> 内置的间隔。例如 <c>new float[] { 4f, 3f }</c>。
        /// </summary>
        public float[] DashPattern { get; set; }

        public string Name { get; set; }
        public object Tag { get; set; }
        public Color StrokeColor { get; set; } = Color.Magenta;
        public float LineWidth { get; set; } = 2f;
        public bool Visible { get; set; } = true;
        public bool IsSelected { get; set; }
        public bool IsFilled { get; set; }
        public Color FillColor { get; set; } = Color.FromArgb(60, 255, 0, 255);
        public bool IsEditable { get; set; } = true;

        /// <summary>线段长度（图像像素）。</summary>
        public float Length
        {
            get
            {
                float dx = P2.X - P1.X;
                float dy = P2.Y - P1.Y;
                return (float)Math.Sqrt(dx * dx + dy * dy);
            }
        }

        /// <summary>方向角（度）。图像坐标系下 0° = +X 轴，顺时针为正。</summary>
        public float AngleDeg
        {
            get { return (float)(Math.Atan2(P2.Y - P1.Y, P2.X - P1.X) * 180.0 / Math.PI); }
        }

        public RectangleF Bounds
        {
            get
            {
                float minX = Math.Min(P1.X, P2.X);
                float minY = Math.Min(P1.Y, P2.Y);
                float maxX = Math.Max(P1.X, P2.X);
                float maxY = Math.Max(P1.Y, P2.Y);
                return RectangleF.FromLTRB(minX, minY, maxX, maxY);
            }
        }

        public void Draw(Graphics g, Viewport vp, RenderOptions options)
        {
            if (!Visible || g == null || vp == null) return;

            PointF a = vp.ImageToControl(P1);
            PointF b = vp.ImageToControl(P2);

            if (options != null && options.AntiAlias)
                g.SmoothingMode = SmoothingMode.AntiAlias;

            // 选中态：先垫一条半透明粗线作为高亮，再画本体
            if (IsSelected)
            {
                using (Pen halo = new Pen(Color.FromArgb(90, 255, 255, 255), Math.Max(LineWidth, 0.5f) + 4f))
                {
                    g.DrawLine(halo, a, b);
                }
            }

            using (Pen pen = new Pen(StrokeColor, Math.Max(LineWidth, 0.5f)))
            {
                ApplyDash(pen);
                g.DrawLine(pen, a, b);
            }
        }

        public bool HitTest(PointF imagePt, float tolerance)
        {
            return DistanceToSegment(imagePt, P1, P2) <= tolerance;
        }

        public void Translate(float dx, float dy)
        {
            P1 = new PointF(P1.X + dx, P1.Y + dy);
            P2 = new PointF(P2.X + dx, P2.Y + dy);
        }

        public IList<ShapeHandle> GetHandles()
        {
            var list = new List<ShapeHandle>(3)
            {
                new ShapeHandle(P1, HandleKind.Resize, 0, "P1"),
                new ShapeHandle(P2, HandleKind.Resize, 1, "P2"),
                new ShapeHandle(MidPoint(), HandleKind.Move, MoveHandleIndex, "Move")
            };
            return list;
        }

        public void SetHandlePosition(int handleIndex, PointF imagePt)
        {
            switch (handleIndex)
            {
                case 0:
                    P1 = imagePt;
                    break;
                case 1:
                    P2 = imagePt;
                    break;
                case MoveHandleIndex:
                    {
                        PointF mid = MidPoint();
                        Translate(imagePt.X - mid.X, imagePt.Y - mid.Y);
                        break;
                    }
                default:
                    return;
            }
        }

        public IGeometryShape Clone()
        {
            var copy = new LineSegmentShape(P1, P2);
            CopyStyleTo(copy);
            return copy;
        }

        /// <summary>把显示样式复制到另一条线段。</summary>
        protected void CopyStyleTo(LineSegmentShape target)
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
            target.Dash = Dash;
            target.DashPattern = (DashPattern == null || DashPattern.Length == 0)
                ? null
                : (float[])DashPattern.Clone();
        }

        public string ToGeometryString()
        {
            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "Line  ({0:F2}, {1:F2})->({2:F2}, {3:F2})  len={4:F2}  A={5:F2}deg",
                P1.X, P1.Y, P2.X, P2.Y, Length, AngleDeg);
        }

        public override string ToString()
        {
            return ToGeometryString();
        }

        /// <summary>线段中点（图像坐标）。</summary>
        public PointF MidPoint()
        {
            return new PointF((P1.X + P2.X) * 0.5f, (P1.Y + P2.Y) * 0.5f);
        }

        /// <summary>把虚线设置应用到画笔；自定义间隔优先。</summary>
        private void ApplyDash(Pen pen)
        {
            float[] pattern = DashPattern;
            if (pattern != null && pattern.Length > 0)
            {
                bool usable = true;
                for (int i = 0; i < pattern.Length; i++)
                {
                    if (!(pattern[i] > 0f)) { usable = false; break; }
                }
                if (usable)
                {
                    pen.DashStyle = DashStyle.Custom;
                    pen.DashPattern = pattern;
                    return;
                }
            }
            pen.DashStyle = Dash;
        }

        /// <summary>点到线段的距离（图像坐标单位）。</summary>
        private static float DistanceToSegment(PointF p, PointF a, PointF b)
        {
            float vx = b.X - a.X;
            float vy = b.Y - a.Y;
            float wx = p.X - a.X;
            float wy = p.Y - a.Y;

            float len2 = vx * vx + vy * vy;
            if (len2 <= 1e-12f)
                return (float)Math.Sqrt(wx * wx + wy * wy);

            float t = (wx * vx + wy * vy) / len2;
            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;

            float dx = wx - vx * t;
            float dy = wy - vy * t;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
