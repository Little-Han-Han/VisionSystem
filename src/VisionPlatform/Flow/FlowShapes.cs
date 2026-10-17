using System;
using System.Collections.Generic;
using System.Drawing;
using VisionPlatform.Native;
using VisionSystem.Controls.Geometry;

namespace VisionPlatform.Flow
{
    /// <summary>
    /// 各节点叠加图形的构造函数。
    /// </summary>
    /// <remarks>
    /// 【坐标约定】几何属性（端点、圆心、半径、十字臂长）一律使用<strong>图像坐标</strong>；
    /// 线宽与标记尺寸一律使用<strong>屏幕像素</strong>（不随缩放变化）。
    /// 这里的颜色是<strong>图像叠加层</strong>的语义色，与窗体 chrome 用的
    /// <c>VisionPlatform.UI.UiTheme</c> 是两套：Flow 层不反向依赖 UI 层。
    /// </remarks>
    public static class FlowShapes
    {
        /// <summary>命中点标记边长（屏幕像素）。</summary>
        public const float MarkerSize = 9f;

        /// <summary>细线宽（屏幕像素）。</summary>
        public const float ThinLine = 1.5f;

        /// <summary>常规线宽（屏幕像素）。</summary>
        public const float NormalLine = 2f;

        /// <summary>粗线宽（屏幕像素）。</summary>
        public const float BoldLine = 2.5f;

        /// <summary>拟合直线（洋红）。</summary>
        public static readonly Color ColorCaliperLine = Color.FromArgb(255, 80, 200);

        /// <summary>拟合圆（青绿）。</summary>
        public static readonly Color ColorCaliperCircle = Color.FromArgb(80, 220, 220);

        /// <summary>卡尺投影窗口（未命中：灰；命中：淡青）。</summary>
        public static readonly Color ColorWindowMiss = Color.FromArgb(140, 140, 148);

        /// <summary>卡尺投影窗口（命中）。</summary>
        public static readonly Color ColorWindowHit = Color.FromArgb(120, 220, 255);

        /// <summary>命中边缘点（黄）。</summary>
        public static readonly Color ColorEdgeHit = Color.FromArgb(255, 235, 59);

        /// <summary>形状匹配框（绿）。</summary>
        public static readonly Color ColorShapeMatch = Color.Lime;

        /// <summary>模板匹配 NCC 框（琥珀）。</summary>
        public static readonly Color ColorNccMatch = Color.FromArgb(255, 193, 7);

        /// <summary>Blob 标记（橙）。</summary>
        public static readonly Color ColorBlob = Color.Orange;

        /// <summary>目标识别检测框（绿）。</summary>
        public static readonly Color ColorDetect = Color.FromArgb(0, 230, 118);

        /// <summary>位置修正基准十字（青）。</summary>
        public static readonly Color ColorRefCross = Color.Cyan;

        /// <summary>变换后的 ROI 轮廓（青，虚线）。</summary>
        public static readonly Color ColorRoi = Color.Cyan;

        /// <summary>拟合圆 / 拟合直线结果（天蓝）。</summary>
        public static readonly Color ColorFit = Color.DeepSkyBlue;

        /// <summary>默认十字臂长（图像像素）。</summary>
        public const float DefaultCrossArm = 24f;

        /// <summary>构造一条线段（图像坐标端点 + 屏幕像素线宽）。</summary>
        /// <param name="x1">起点 X（图像坐标）。</param>
        /// <param name="y1">起点 Y（图像坐标）。</param>
        /// <param name="x2">终点 X（图像坐标）。</param>
        /// <param name="y2">终点 Y（图像坐标）。</param>
        /// <param name="color">线条颜色。</param>
        /// <param name="width">线宽（屏幕像素）。</param>
        /// <param name="dashed">是否虚线。</param>
        public static LineSegmentShape Line(double x1, double y1, double x2, double y2,
                                            Color color, float width, bool dashed = false)
        {
            var s = new LineSegmentShape((float)x1, (float)y1, (float)x2, (float)y2);
            s.StrokeColor = color;
            s.LineWidth = width;
            s.Dash = dashed ? System.Drawing.Drawing2D.DashStyle.Dash
                            : System.Drawing.Drawing2D.DashStyle.Solid;
            s.IsEditable = false;
            return s;
        }

        /// <summary>卡尺投影窗口（每个卡尺一条虚线；未命中用灰色区分）。</summary>
        public static List<IGeometryShape> CaliperWindows(IEnumerable<CaliperPoint> points)
        {
            var list = new List<IGeometryShape>();
            if (points == null) return list;

            foreach (CaliperPoint p in points)
            {
                if (p == null) continue;
                list.Add(Line(p.Px1, p.Py1, p.Px2, p.Py2,
                              p.Valid ? ColorWindowHit : ColorWindowMiss,
                              ThinLine, true));
            }
            return list;
        }

        /// <summary>卡尺命中边缘点（实心小方块，屏幕像素恒定尺寸）。</summary>
        public static List<IGeometryShape> CaliperHits(IEnumerable<CaliperPoint> points)
        {
            var list = new List<IGeometryShape>();
            if (points == null) return list;

            foreach (CaliperPoint p in points)
            {
                if (p == null || !p.Valid) continue;
                list.Add(Marker(p.X, p.Y, ColorEdgeHit));
            }
            return list;
        }

        /// <summary>点标记（方块，屏幕像素尺寸）。</summary>
        /// <param name="x">中心 X（图像坐标）。</param>
        /// <param name="y">中心 Y（图像坐标）。</param>
        /// <param name="color">颜色。</param>
        /// <param name="size">边长（屏幕像素）。</param>
        public static PointMarkerShape Marker(double x, double y, Color color, float size = MarkerSize)
        {
            var m = new PointMarkerShape(new PointF((float)x, (float)y), size, MarkerKind.Square);
            m.StrokeColor = color;
            m.FillColor = Color.FromArgb(230, color);
            m.LineWidth = ThinLine;
            m.IsFilled = true;
            m.IsEditable = false;
            return m;
        }

        /// <summary>拟合直线（洋红实线，两端为图像坐标）。</summary>
        public static LineSegmentShape CaliperLine(LineResult line)
        {
            if (line == null) return null;
            var s = Line(line.X1, line.Y1, line.X2, line.Y2, ColorCaliperLine, BoldLine, false);
            s.Name = "LineCaliper";
            return s;
        }

        /// <summary>拟合圆（青绿圆周）。</summary>
        public static CircleShape CaliperCircle(CircleResult circle)
        {
            if (circle == null) return null;
            var c = new CircleShape(new PointF((float)circle.Cx, (float)circle.Cy),
                                    (float)Math.Max(1.0, circle.Radius));
            c.StrokeColor = ColorCaliperCircle;
            c.LineWidth = BoldLine;
            c.IsEditable = false;
            c.Name = "CircleCaliper";
            return c;
        }

        /// <summary>形状匹配结果框（旋转矩形，第 1 目标用高亮，其余浅色）。</summary>
        /// <param name="cx">中心 X（图像坐标）。</param>
        /// <param name="cy">中心 Y（图像坐标）。</param>
        /// <param name="width">宽（图像像素）。</param>
        /// <param name="height">高（图像像素）。</param>
        /// <param name="angle">角度（度，顺时针为正）。</param>
        /// <param name="primary">是否为第 1 目标。</param>
        public static RotatedRectShape MatchBox(double cx, double cy, double width, double height,
                                                double angle, bool primary)
        {
            var r = new RotatedRectShape(new PointF((float)cx, (float)cy),
                                         (float)Math.Max(1.0, width),
                                         (float)Math.Max(1.0, height),
                                         (float)angle);
            r.StrokeColor = primary ? ColorShapeMatch : Color.FromArgb(150, ColorShapeMatch);
            r.LineWidth = primary ? BoldLine : ThinLine;
            r.IsEditable = false;
            return r;
        }

        /// <summary>模板匹配 NCC 结果框（轴对齐矩形，仅平移匹配）。</summary>
        public static RectangleShape NccBox(Rectangle rect, bool primary)
        {
            var r = new RectangleShape(Math.Max(1f, rect.X), Math.Max(1f, rect.Y),
                                       Math.Max(1f, rect.Width), Math.Max(1f, rect.Height));
            r.StrokeColor = primary ? ColorNccMatch : Color.FromArgb(150, ColorNccMatch);
            r.LineWidth = primary ? BoldLine : ThinLine;
            r.IsEditable = false;
            return r;
        }

        /// <summary>Blob 标记：以最小外接矩形（轴对齐）绘制，替代原先的面积等效圆。</summary>
        public static RectangleShape BlobRect(double cx, double cy, double width, double height)
        {
            float w = (float)Math.Max(1.0, width);
            float h = (float)Math.Max(1.0, height);
            var r = new RectangleShape((float)(cx - w / 2.0), (float)(cy - h / 2.0), w, h);
            r.StrokeColor = ColorBlob;
            r.LineWidth = NormalLine;
            r.IsEditable = false;
            return r;
        }

        /// <summary>基准十字（位置修正用；臂长为图像像素，随缩放移动）。</summary>
        public static List<IGeometryShape> Crosshair(double x, double y, double arm = DefaultCrossArm)
        {
            var list = new List<IGeometryShape>(2);
            list.Add(Line(x - arm, y, x + arm, y, ColorRefCross, ThinLine, false));
            list.Add(Line(x, y - arm, x, y + arm, ColorRefCross, ThinLine, false));
            return list;
        }

        /// <summary>位置修正坐标系 X 轴（红）。</summary>
        public static readonly Color ColorAxisX = Color.FromArgb(255, 96, 96);

        /// <summary>位置修正坐标系 Y 轴（绿）。</summary>
        public static readonly Color ColorAxisY = Color.FromArgb(96, 220, 96);

        /// <summary>
        /// 基准坐标系（位置修正用）：在 (x, y) 处按 angleDeg 画出带箭头的 X/Y 轴，
        /// 直观显示基准位置与方向（y 向下、顺时针为正，与全库角度约定一致）。
        /// 臂长 arm 为图像像素；X 轴红、Y 轴绿，原点带标记。
        /// </summary>
        public static List<IGeometryShape> CoordinateSystem(double x, double y, double angleDeg, double arm = DefaultCrossArm)
        {
            var list = new List<IGeometryShape>(8);
            double a = angleDeg * Math.PI / 180.0;
            double cos = Math.Cos(a), sin = Math.Sin(a);
            double head = arm * 0.28;

            // X 轴：沿角度方向（红），末端两条短箭头线表方向
            double xx = x + cos * arm, xy = y + sin * arm;
            double aX = Math.Atan2(sin, cos);
            list.Add(Line(x, y, xx, xy, ColorAxisX, NormalLine, false));
            list.Add(Line(xx, xy, xx + Math.Cos(aX + Math.PI * 0.75) * head, xy + Math.Sin(aX + Math.PI * 0.75) * head, ColorAxisX, NormalLine, false));
            list.Add(Line(xx, xy, xx + Math.Cos(aX - Math.PI * 0.75) * head, xy + Math.Sin(aX - Math.PI * 0.75) * head, ColorAxisX, NormalLine, false));

            // Y 轴：X 方向顺时针转 90°（y 向下坐标系，绿）
            double yx = x - sin * arm, yy = y + cos * arm;
            double aY = Math.Atan2(cos, -sin);
            list.Add(Line(x, y, yx, yy, ColorAxisY, NormalLine, false));
            list.Add(Line(yx, yy, yx + Math.Cos(aY + Math.PI * 0.75) * head, yy + Math.Sin(aY + Math.PI * 0.75) * head, ColorAxisY, NormalLine, false));
            list.Add(Line(yx, yy, yx + Math.Cos(aY - Math.PI * 0.75) * head, yy + Math.Sin(aY - Math.PI * 0.75) * head, ColorAxisY, NormalLine, false));

            // 原点标记
            list.Add(Marker(x, y, ColorRefCross));
            return list;
        }

        /// <summary>变换后的 ROI 轮廓（青色虚线矩形）。</summary>
        public static List<IGeometryShape> RoiOutline(Rectangle rect)
        {
            var list = new List<IGeometryShape>(4);
            if (rect.Width <= 0 || rect.Height <= 0) return list;

            float l = rect.Left, t = rect.Top, r = rect.Right, b = rect.Bottom;
            list.Add(Line(l, t, r, t, ColorRoi, ThinLine, true));
            list.Add(Line(r, t, r, b, ColorRoi, ThinLine, true));
            list.Add(Line(r, b, l, b, ColorRoi, ThinLine, true));
            list.Add(Line(l, b, l, t, ColorRoi, ThinLine, true));
            return list;
        }

        /// <summary>拟合圆 / 拟合直线结果图形。</summary>
        public static IGeometryShape FitResult(CircleResult circle)
        {
            if (circle == null) return null;
            var c = new CircleShape(new PointF((float)circle.Cx, (float)circle.Cy),
                                    (float)Math.Max(1.0, circle.Radius));
            c.StrokeColor = ColorFit;
            c.LineWidth = NormalLine;
            c.IsEditable = false;
            return c;
        }

        /// <summary>拟合直线结果图形。</summary>
        public static IGeometryShape FitResult(LineResult line)
        {
            if (line == null) return null;
            var s = Line(line.X1, line.Y1, line.X2, line.Y2, ColorFit, NormalLine, false);
            s.Name = "FitLine";
            return s;
        }

        /// <summary>目标识别检测框（轴对齐矩形，左上角 + 宽高，均为图像坐标）。</summary>
        /// <param name="x">左上角 X（图像坐标）。</param>
        /// <param name="y">左上角 Y（图像坐标）。</param>
        /// <param name="width">宽（图像像素）。</param>
        /// <param name="height">高（图像像素）。</param>
        /// <param name="primary">是否为置信度最高的目标（用更粗的线）。</param>
        public static RectangleShape DetectBox(double x, double y, double width, double height, bool primary)
        {
            float w = (float)Math.Max(1.0, width);
            float h = (float)Math.Max(1.0, height);
            var r = new RectangleShape((float)x, (float)y, w, h);
            r.StrokeColor = primary ? ColorDetect : Color.FromArgb(150, ColorDetect);
            r.LineWidth = primary ? BoldLine : ThinLine;
            r.IsEditable = false;
            r.Name = "Detection";
            return r;
        }
    }
}
