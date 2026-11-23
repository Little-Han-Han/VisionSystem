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

        /// <summary>未运行时的卡尺布局预览（浅青，虚线框）：与"运行结果"区分开，不冒充命中。</summary>
        public static readonly Color ColorCaliperLayout = Color.FromArgb(150, 160, 235);

        /// <summary>位置修正：基准坐标系（淡青，只作参考）。</summary>
        public static readonly Color ColorPoseRef = Color.FromArgb(120, 200, 220);

        /// <summary>位置修正：实测坐标系（亮橙，跟工件一起跑）。</summary>
        public static readonly Color ColorPoseNow = Color.FromArgb(255, 176, 60);

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
        /// <summary>
        /// 搜索区域 / ROI 轮廓颜色：<b>品红</b>。
        /// <para>【为什么不沿用青色】检测结果框（Blob / 匹配框）本身是青的，ROI 再用青色就分不清
        /// "哪里是搜索范围、哪里是找到的目标"。虚线的方案成本高（形状类要加 DashStyle 并改渲染器），
        /// 先用颜色区分：ROI 品红、目标框青、最小外接矩形琥珀。</para>
        /// </summary>
        public static readonly Color ColorRoi = Color.FromArgb(236, 112, 255);

        /// <summary>拟合结果（直线 / 圆）的颜色。</summary>
        public static readonly Color ColorFit = Color.DeepSkyBlue;

        /// <summary>测量连线（点—点、点—垂足）的颜色（青绿）。</summary>
        public static readonly Color ColorMeasure = Color.FromArgb(120, 235, 190);

        /// <summary>边缘对 / 交点标记的颜色（橙粉，与卡尺的洋红、边缘点的黄区分开）。</summary>
        public static readonly Color ColorEdgePair = Color.FromArgb(255, 150, 120);

        /// <summary>字符识别字符框（橙色）。</summary>
        public static readonly Color ColorOcr = Color.FromArgb(255, 152, 0);
        /// <summary>OCR 作用区域框（暗橙，虚线；只标"看的是哪一块"，不抢字符框的注意力）。</summary>
        public static readonly Color ColorOcrRegion = Color.FromArgb(200, 150, 90, 30);

        /// <summary>OCR 合并字符串框（亮黄绿；一行一个框 + 整行文本）。</summary>
        public static readonly Color ColorOcrString = Color.FromArgb(190, 255, 110);

        /// <summary>条码 / 二维码框：品红，与 NCC（琥珀）、OCR（橙）都能一眼分开。</summary>
        public static readonly Color ColorBarcode = Color.FromArgb(236, 120, 255);

        /// <summary>
        /// 辅助图形的 <see cref="IGeometryShape.Name"/> 标记：<strong>受「轮廓」开关控制</strong>的那一类
        /// （ROI 轮廓、卡尺窗口）。OCR 的区域框/串框不是轮廓，因此不打这个标记 ——
        /// 否则取消勾选「轮廓」会把"OCR 在识别哪一块"一起关掉。
        /// </summary>
        public const string AuxContourName = "AuxContour";

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

        /// <summary>
        /// 卡尺投影窗口：<strong>每个卡尺画成一个矩形</strong>（长度 = 投影长度，宽度 = 2×滤波半宽）。
        /// <para>
        /// 【为什么从"一条虚线"改成矩形】一根线只能表示"朝这个方向找边"，看不出：
        /// ① 投影有多长（可能戳到邻边的边）；② 窗口有多宽；③ 一共有几个卡尺。
        /// 用户在界面上只能看到一条直线，根本判断不了 ROI 划得对不对。
        /// </para>
        /// </summary>
        /// <param name="points">native 返回的卡尺点（P1→P2 是投影方向的两个端点）。</param>
        /// <param name="halfWidth">滤波半宽（像素）；&lt;=0 时按 2px 兜底。</param>
        public static List<IGeometryShape> CaliperWindows(IEnumerable<CaliperPoint> points, double halfWidth)
        {
            var list = new List<IGeometryShape>();
            if (points == null) return list;

            float w = (float)Math.Max(1.0, halfWidth * 2.0);

            foreach (CaliperPoint p in points)
            {
                if (p == null) continue;

                IGeometryShape s = WindowRect(p.Px1, p.Py1, p.Px2, p.Py2, w,
                                              p.Valid ? ColorWindowHit : ColorWindowMiss);
                if (s != null) list.Add(s);
            }
            return list;
        }

        /// <summary>
        /// 未运行时的卡尺布局预览（直线）：扫描线 + N 个投影矩形。
        /// <para>
        /// 选中卡尺节点、还没运行的时候就要能看见"卡尺画在哪、有几个、投影多长"——
        /// 否则调 X1/Y1/X2/Y2 只能盲调。
        /// </para>
        /// </summary>
        public static List<IGeometryShape> CaliperLayoutLine(double x1, double y1, double x2, double y2,
                                                             int count, double projLength, double halfWidth)
        {
            var list = new List<IGeometryShape>();

            // 扫描线本身（虚线，浅色）：卡尺沿它等分
            LineSegmentShape axis = Line(x1, y1, x2, y2, ColorCaliperLayout, 2f, true);
            axis.Name = "CaliperAxis";
            list.Add(axis);

            float w = (float)Math.Max(1.0, halfWidth * 2.0);
            float len = (float)Math.Max(1.0, projLength);

            int n = Math.Max(1, count);
            for (int i = 0; i < n; i++)
            {
                double t = (i + 0.5) / n;
                double cx = x1 + (x2 - x1) * t;
                double cy = y1 + (y2 - y1) * t;

                // 投影方向 = 扫描线法向（垂直于扫描线，向两侧各 projLength/2）
                double dx = x2 - x1, dy = y2 - y1;
                double len0 = Math.Sqrt(dx * dx + dy * dy);
                if (len0 < 1e-6) { dx = 1; dy = 0; len0 = 1; }
                double nx = -dy / len0, ny = dx / len0;

                double px1 = cx - nx * len / 2, py1 = cy - ny * len / 2;
                double px2 = cx + nx * len / 2, py2 = cy + ny * len / 2;

                IGeometryShape s = WindowRect(px1, py1, px2, py2, w, ColorCaliperLayout);
                if (s != null) list.Add(s);
            }

            return list;
        }

        /// <summary>
        /// 未运行时的卡尺布局预览（圆）：内外径参考圆 + N 个径向投影矩形。
        /// </summary>
        public static List<IGeometryShape> CaliperLayoutCircle(double cx, double cy,
                                                               double innerR, double outerR,
                                                               double startAngle, double spanAngle,
                                                               int count, double projLength, double halfWidth)
        {
            var list = new List<IGeometryShape>();

            double rMid = (innerR + outerR) / 2.0;
            foreach (double r in new double[] { innerR, outerR })
            {
                if (r <= 0) continue;
                var c = new CircleShape(new PointF((float)cx, (float)cy), (float)r);
                c.StrokeColor = ColorCaliperLayout;
                c.LineWidth = ThinLine;
                c.IsEditable = false;
                c.Name = "CaliperRing";
                list.Add(c);
            }

            float w = (float)Math.Max(1.0, halfWidth * 2.0);
            float len = (float)Math.Max(1.0, projLength);

            int n = Math.Max(1, count);
            double span = (spanAngle <= 0.0 || spanAngle > 360.0) ? 360.0 : spanAngle;

            for (int i = 0; i < n; i++)
            {
                double ang = (startAngle + span * (i + 0.5) / n) * Math.PI / 180.0;
                double ux = Math.Cos(ang), uy = Math.Sin(ang);

                double px1 = cx + ux * (rMid - len / 2), py1 = cy + uy * (rMid - len / 2);
                double px2 = cx + ux * (rMid + len / 2), py2 = cy + uy * (rMid + len / 2);

                IGeometryShape s = WindowRect(px1, py1, px2, py2, w, ColorCaliperLayout);
                if (s != null) list.Add(s);
            }

            return list;
        }

        /// <summary>
        /// 由"投影方向两端点 + 窗口宽度"构造一个矩形（长 = P1→P2，宽 = w）。
        /// 旋转矩形比 AABB 更贴合真实投影窗口：斜扫描时 AABB 会胖一圈。
        /// </summary>
        private static IGeometryShape WindowRect(double px1, double py1, double px2, double py2,
                                                 float w, Color color)
        {
            double dx = px2 - px1, dy = py2 - py1;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-6) return null;

            var center = new PointF((float)((px1 + px2) / 2.0), (float)((py1 + py2) / 2.0));
            float angle = (float)(Math.Atan2(dy, dx) * 180.0 / Math.PI);

            var r = new RotatedRectShape(center, (float)len, Math.Max(1f, w), angle);
            r.StrokeColor = color;
            // 2px：卡尺窗口在整幅图上只有二十来个像素，1px 在缩放后基本看不见
            r.LineWidth = 2f;
            r.IsFilled = false;
            r.IsEditable = false;
            r.Name = "CaliperWindow";
            return r;
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
        /// <param name="label">框标签（一般为 <c>#序号 分数</c>）；空 = 不画。</param>
        public static RotatedRectShape MatchBox(double cx, double cy, double width, double height,
                                                double angle, bool primary, string label = null)
        {
            var r = new RotatedRectShape(new PointF((float)cx, (float)cy),
                                         (float)Math.Max(1.0, width),
                                         (float)Math.Max(1.0, height),
                                         (float)angle);
            r.StrokeColor = primary ? ColorShapeMatch : Color.FromArgb(150, ColorShapeMatch);
            r.LineWidth = primary ? BoldLine : ThinLine;
            r.IsEditable = false;
            if (!string.IsNullOrEmpty(label)) r.Label = label;
            return r;
        }

        /// <summary>
        /// 条码 / 二维码结果框（旋转矩形）。
        /// <para>码本身是矩形，透视畸变一般很小时用旋转矩形比四点多边形更稳（不会因一个角点抖动变形）。</para>
        /// </summary>
        /// <param name="cx">中心 X（图像坐标）。</param>
        /// <param name="cy">中心 Y。</param>
        /// <param name="width">框宽。</param>
        /// <param name="height">框高。</param>
        /// <param name="angle">角度（度，顺时针为正）。</param>
        /// <param name="primary">是否为第 1 个码。</param>
        /// <param name="label">框标签（一般为 <c>码制 文本</c>）；空 = 不画。</param>
        public static RotatedRectShape BarcodeBox(double cx, double cy, double width, double height,
                                                  double angle, bool primary, string label = null)
        {
            var r = new RotatedRectShape(new PointF((float)cx, (float)cy),
                                         (float)Math.Max(1.0, width),
                                         (float)Math.Max(1.0, height),
                                         (float)angle);
            r.StrokeColor = primary ? ColorBarcode : Color.FromArgb(150, ColorBarcode);
            r.LineWidth = primary ? BoldLine : ThinLine;
            r.IsEditable = false;
            if (!string.IsNullOrEmpty(label)) r.Label = label;
            return r;
        }

        /// <summary>模板匹配 NCC 结果框（轴对齐矩形，仅平移匹配）。</summary>
        /// <param name="rect">结果框。</param>
        /// <param name="primary">是否为第 1 目标。</param>
        /// <param name="label">框标签（一般为 <c>#序号 得分</c>）；空 = 不画。</param>
        public static RectangleShape NccBox(Rectangle rect, bool primary, string label = null)
        {
            var r = new RectangleShape(Math.Max(1f, rect.X), Math.Max(1f, rect.Y),
                                       Math.Max(1f, rect.Width), Math.Max(1f, rect.Height));
            r.StrokeColor = primary ? ColorNccMatch : Color.FromArgb(150, ColorNccMatch);
            r.LineWidth = primary ? BoldLine : ThinLine;
            r.IsEditable = false;
            if (!string.IsNullOrEmpty(label)) r.Label = label;
            return r;
        }

        /// <summary>
        /// 模板匹配 NCC 结果框（**旋转矩形**，支持角度 / 缩放搜索）。
        /// <para>与 <see cref="MatchBox"/> 的区别只有配色：NCC 用彩色区分，一眼能看出是哪个算法出的框。</para>
        /// </summary>
        /// <param name="cx">匹配框中心 X（图像坐标）。</param>
        /// <param name="cy">匹配框中心 Y。</param>
        /// <param name="width">框宽（图像像素）。</param>
        /// <param name="height">框高。</param>
        /// <param name="angle">角度（度，顺时针为正）。</param>
        /// <param name="primary">是否为第 1 目标。</param>
        /// <param name="label">框标签；空 = 不画。</param>
        /// <summary>
        /// NCC 结果的旋转框尺寸 = 训练模板尺寸 × 匹配缩放。
        /// <para>
        /// <strong>不能</strong>直接拿 <c>VA_MatchResult.Width/Height</c>：原生层那两个字段是旋转后的
        /// <em>轴对齐外接框</em>（见 va_types.h 的契约），45° 时比模板大 1.41 倍、90° 时横竖互换。
        /// 画旋转矩形必须用未旋转的模板尺寸 × scale，与形状匹配的口径一致。
        /// </para>
        /// </summary>
        public static SizeF NccRotatedBoxSize(double templateWidth, double templateHeight, double scale)
        {
            double s = (scale > 1e-9) ? scale : 1.0;
            return new SizeF((float)Math.Max(1.0, templateWidth * s),
                             (float)Math.Max(1.0, templateHeight * s));
        }

        public static RotatedRectShape NccMatchBox(double cx, double cy, double width, double height,
                                                   double angle, bool primary, string label = null)
        {
            var r = new RotatedRectShape(new PointF((float)cx, (float)cy),
                                         (float)Math.Max(1.0, width),
                                         (float)Math.Max(1.0, height),
                                         (float)angle);
            r.StrokeColor = primary ? ColorNccMatch : Color.FromArgb(150, ColorNccMatch);
            r.LineWidth = primary ? BoldLine : ThinLine;
            r.IsEditable = false;
            if (!string.IsNullOrEmpty(label)) r.Label = label;
            return r;
        }

        /// <summary>Blob 标记：以最小外接矩形（轴对齐）绘制，替代原先的面积等效圆。</summary>
        /// <param name="label">框标签（一般为 <c>#序号 面积px² · 圆度</c>）；空 = 不画。</param>
        public static RectangleShape BlobRect(double cx, double cy, double width, double height,
                                              string label = null)
        {
            float w = (float)Math.Max(1.0, width);
            float h = (float)Math.Max(1.0, height);
            var r = new RectangleShape((float)(cx - w / 2.0), (float)(cy - h / 2.0), w, h);
            r.StrokeColor = ColorBlob;
            r.LineWidth = NormalLine;
            r.IsEditable = false;
            if (!string.IsNullOrEmpty(label)) r.Label = label;
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
            return CoordinateSystem(x, y, angleDeg, ColorAxisX, ColorAxisY, NormalLine, arm);
        }

        /// <summary>
        /// 彩色可定制的坐标系：位置修正里用两种颜色区分「基准坐标系」与「实测坐标系」，
        /// 否则两个十字叠在一起，用户只看得到一个点（"没有显示更新后的点"）。
        /// </summary>
        public static List<IGeometryShape> CoordinateSystem(double x, double y, double angleDeg,
                                                           Color axisX, Color axisY, float width,
                                                           double arm)
        {
            var list = new List<IGeometryShape>(8);
            double a = angleDeg * Math.PI / 180.0;
            double cos = Math.Cos(a), sin = Math.Sin(a);
            double head = arm * 0.28;

            // X 轴：沿角度方向（红），末端两条短箭头线表方向
            double xx = x + cos * arm, xy = y + sin * arm;
            double aX = Math.Atan2(sin, cos);
            list.Add(Line(x, y, xx, xy, axisX, width, false));
            list.Add(Line(xx, xy, xx + Math.Cos(aX + Math.PI * 0.75) * head, xy + Math.Sin(aX + Math.PI * 0.75) * head, axisX, width, false));
            list.Add(Line(xx, xy, xx + Math.Cos(aX - Math.PI * 0.75) * head, xy + Math.Sin(aX - Math.PI * 0.75) * head, axisX, width, false));

            // Y 轴：X 方向顺时针转 90°（y 向下坐标系，绿）
            double yx = x - sin * arm, yy = y + cos * arm;
            double aY = Math.Atan2(cos, -sin);
            list.Add(Line(x, y, yx, yy, axisY, width, false));
            list.Add(Line(yx, yy, yx + Math.Cos(aY + Math.PI * 0.75) * head, yy + Math.Sin(aY + Math.PI * 0.75) * head, axisY, width, false));
            list.Add(Line(yx, yy, yx + Math.Cos(aY - Math.PI * 0.75) * head, yy + Math.Sin(aY - Math.PI * 0.75) * head, axisY, width, false));

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

            // 打上"轮廓"标记：预览区的「轮廓」开关按它整组显隐
            foreach (IGeometryShape s in list) s.Name = AuxContourName;
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
        public static RectangleShape DetectBox(double x, double y, double width, double height, bool primary, string label)
        {
            float w = (float)Math.Max(1.0, width);
            float h = (float)Math.Max(1.0, height);
            var r = new RectangleShape((float)x, (float)y, w, h);
            r.StrokeColor = primary ? ColorDetect : Color.FromArgb(150, ColorDetect);
            r.LineWidth = primary ? BoldLine : ThinLine;
            r.IsEditable = false;
            r.Name = "Detection";
            if (!string.IsNullOrEmpty(label)) r.Label = label;
            return r;
        }

        /// <summary>字符识别的字符外接框（轴对齐矩形，左上角 + 宽高，均为图像坐标）。</summary>
        /// <param name="x">左上角 X（图像坐标）。</param>
        /// <param name="y">左上角 Y（图像坐标）。</param>
        /// <param name="width">宽（图像像素）。</param>
        /// <param name="height">高（图像像素）。</param>
        /// <param name="labelText">框标签（一般为「字符 + 置信度」）。</param>
        public static RectangleShape OcrBox(double x, double y, double width, double height, string labelText)
        {
            float w = (float)Math.Max(1.0, width);
            float h = (float)Math.Max(1.0, height);
            var r = new RectangleShape((float)x, (float)y, w, h);
            r.StrokeColor = ColorOcr;
            r.LineWidth = NormalLine;
            r.IsEditable = false;
            r.Name = "OcrChar";
            if (!string.IsNullOrEmpty(labelText)) r.Label = labelText;
            return r;
        }

        /// <summary>
        /// 字符识别的字符框（<strong>旋转矩形</strong>：中心 + 尺寸 + 角度）。
        /// <para>
        /// 与 <see cref="OcrBox"/> 的分工：ROI / 位姿带角度时，字符框在整帧上就是斜的 ——
        /// 此时必须用本方法。沿用轴对齐版本会"中心对、四角全错"，
        /// 现场看到的就是「分割出来的字符框位置和形状都不对」。
        /// </para>
        /// </summary>
        /// <param name="center">框中心（图像坐标）。</param>
        /// <param name="angleDeg">角度（度，顺时针为正）。</param>
        public static RotatedRectShape OcrCharRotBox(PointF center, double width, double height,
                                                     double angleDeg, string labelText)
        {
            var r = new RotatedRectShape(center,
                                         (float)Math.Max(1.0, width),
                                         (float)Math.Max(1.0, height),
                                         (float)angleDeg);
            r.StrokeColor = ColorOcr;
            r.LineWidth = NormalLine;
            r.IsEditable = false;
            r.Name = "OcrChar";
            if (!string.IsNullOrEmpty(labelText)) r.Label = labelText;
            return r;
        }

        /// <summary>
        /// OCR 合并字符串框（<strong>旋转矩形</strong>）：一行一个框 + 整行文本。
        /// 旋转 ROI 下与 <see cref="OcrStringBox"/> 同职责，只是框本身跟着转。
        /// </summary>
        public static RotatedRectShape OcrStringRotBox(PointF center, double width, double height,
                                                       double angleDeg, string text)
        {
            var r = new RotatedRectShape(center,
                                         (float)Math.Max(1.0, width),
                                         (float)Math.Max(1.0, height),
                                         (float)angleDeg);
            r.StrokeColor = ColorOcrString;
            r.LineWidth = BoldLine;
            r.IsEditable = false;
            r.Name = "OcrString";
            if (!string.IsNullOrEmpty(text)) r.Label = text;
            return r;
        }

        /// <summary>
        /// OCR 作用区域框（虚线暗橙）：标出"这一帧里 OCR 到底在看哪一块"。
        /// <para>它属于"输入几何"，调用方应放进 <c>AuxShapes</c> —— 进 <c>Shapes</c> 会让结果区
        /// 多出一条"区域框"目标行，与字符数永久对不上。</para>
        /// </summary>
        public static RectangleShape OcrRegionBox(Rectangle region, string labelText)
        {
            if (region.Width <= 0 || region.Height <= 0) return null;

            var r = new RectangleShape(Math.Max(0f, region.X), Math.Max(0f, region.Y),
                                       Math.Max(1f, region.Width), Math.Max(1f, region.Height));
            r.StrokeColor = ColorOcrRegion;
            r.LineWidth = ThinLine;
            r.IsEditable = false;
            r.Name = "OcrRegion";
            if (!string.IsNullOrEmpty(labelText)) r.Label = labelText;
            return r;
        }

        /// <summary>
        /// OCR 合并字符串框：一行字符折成<strong>一个</strong>框 + 整行文本。
        /// <para>逐字符框是噪声（一行 30 个框）；"一行一个框 + 一行字"才是工程师看结果的方式。</para>
        /// <para>T04（旋转 ROI）交付前几何只能是轴对齐，旋转串框归 T04，此处不得自行实现。</para>
        /// </summary>
        public static RectangleShape OcrStringBox(Rectangle rect, string text)
        {
            if (rect.Width <= 0 || rect.Height <= 0) return null;

            var r = new RectangleShape(Math.Max(0f, rect.X), Math.Max(0f, rect.Y),
                                       Math.Max(1f, rect.Width), Math.Max(1f, rect.Height));
            r.StrokeColor = ColorOcrString;
            r.LineWidth = BoldLine;
            r.IsEditable = false;
            r.Name = "OcrString";
            if (!string.IsNullOrEmpty(text)) r.Label = text;
            return r;
        }
    }
}
