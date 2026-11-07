using System;
using System.Drawing;

namespace VisionPlatform.Flow
{
    /// <summary>
    /// 几何运算（点在图像坐标、角度单位为度、顺时针为正，与全库约定一致）。
    /// <para>
    /// 【为什么单独一个静态类】"求交、求垂足、求夹角"被<strong>多个节点共用</strong>
    /// （交点卡尺 / 点点测量 / 点直线测量 / 线线测量），各写一份必然出现
    /// "同一对直线在三个节点里算出三个交点"。这里只放纯函数：不碰位图、不读写会话，便于单测。
    /// </para>
    /// <para>所有方法对退化输入（零长直线 / NaN / 平行）<strong>返回 false 而不是抛异常</strong>，
    /// 由调用方决定报 Warn 还是判 NG。</para>
    /// </summary>
    public static class FlowGeometryMath
    {
        /// <summary>两点距离。</summary>
        public static double Distance(PointF a, PointF b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>由两点求方向角（度，归一化到 (-180, 180]，与 <c>atan2(dy,dx)</c> 一致）。</summary>
        public static double DirectionDeg(PointF from, PointF to)
        {
            double a = Math.Atan2(to.Y - from.Y, to.X - from.X) * 180.0 / Math.PI;
            if (a <= -180.0) a += 360.0;
            if (a > 180.0) a -= 360.0;
            return a;
        }

        /// <summary>
        /// 把"直线方向角"归一化到 (-90, 90]（直线无向：180° 与 0° 是同一条线）。
        /// </summary>
        public static double NormalizeLineAngle(double deg)
        {
            double a = deg;
            while (a <= -90.0) a += 180.0;
            while (a > 90.0) a -= 180.0;
            return a;
        }

        /// <summary>
        /// 两条直线的夹角（度，0~90）。任一条退化时返回 <c>NaN</c>。
        /// </summary>
        public static double AngleBetweenLines(FlowGeometryValue a, FlowGeometryValue b)
        {
            if (a == null || b == null || a.IsDegenerateLine || b.IsDegenerateLine) return double.NaN;

            double da = Math.Atan2(a.Y2 - a.Y1, a.X2 - a.X1);
            double db = Math.Atan2(b.Y2 - b.Y1, b.X2 - b.X1);

            double diff = Math.Abs(da - db) * 180.0 / Math.PI;
            diff = diff % 180.0;                      // 直线无向，折到 [0,180)
            if (diff > 90.0) diff = 180.0 - diff;     // 取锐角侧
            return diff;
        }

        /// <summary>
        /// 求两条直线的交点（延长线求交，不要求交点落在线段内）。
        /// </summary>
        /// <param name="a">直线 1。</param>
        /// <param name="b">直线 2。</param>
        /// <param name="point">交点；失败时为 <see cref="PointF.Empty"/>。</param>
        /// <param name="parallelTolDeg">
        /// 平行判定的角度容差（度）：夹角小于该值即视为平行，不给出交点。
        /// 传 0 时用 1e-9 的数值容差（只有真正数学平行才失败）。
        /// </param>
        /// <returns>是否成功求出交点。</returns>
        public static bool TryIntersect(FlowGeometryValue a, FlowGeometryValue b,
                                        out PointF point, double parallelTolDeg = 0.0)
        {
            point = PointF.Empty;
            if (a == null || b == null || a.IsDegenerateLine || b.IsDegenerateLine) return false;

            double ax = a.X2 - a.X1, ay = a.Y2 - a.Y1;
            double bx = b.X2 - b.X1, by = b.Y2 - b.Y1;

            double den = ax * by - ay * bx;
            double na = Math.Sqrt(ax * ax + ay * ay);
            double nb = Math.Sqrt(bx * bx + by * by);
            if (na < 1e-12 || nb < 1e-12) return false;

            // |sin(θ)| = |den| / (|a||b|)：用夹角容差判定平行，比裸 den 阈值更直观
            double sinTheta = Math.Abs(den) / (na * nb);
            double tolSin = (parallelTolDeg > 0.0)
                ? Math.Sin(parallelTolDeg * Math.PI / 180.0)
                : 1e-9;
            if (sinTheta <= tolSin) return false;

            double t = ((b.X1 - a.X1) * by - (b.Y1 - a.Y1) * bx) / den;
            point = new PointF((float)(a.X1 + t * ax), (float)(a.Y1 + t * ay));
            return true;
        }

        /// <summary>
        /// 点到直线（延长线）的垂直距离，并给出垂足。
        /// </summary>
        /// <returns>直线退化时返回 <c>NaN</c>，垂足为 <see cref="PointF.Empty"/>。</returns>
        public static double PointLineDistance(PointF p, FlowGeometryValue line, out PointF foot)
        {
            foot = PointF.Empty;
            if (line == null || line.IsDegenerateLine) return double.NaN;

            double dx = line.X2 - line.X1, dy = line.Y2 - line.Y1;
            double len2 = dx * dx + dy * dy;

            double t = ((p.X - line.X1) * dx + (p.Y - line.Y1) * dy) / len2;
            foot = new PointF((float)(line.X1 + t * dx), (float)(line.Y1 + t * dy));

            double ex = p.X - foot.X, ey = p.Y - foot.Y;
            return Math.Sqrt(ex * ex + ey * ey);
        }

        /// <summary>
        /// 两条直线"平行时的间距"：取直线 2 的中点到直线 1 的距离。
        /// <para>不平行时该值没有物理意义（两线终会相交），调用方应先判平行。</para>
        /// </summary>
        public static double ParallelSpacing(FlowGeometryValue a, FlowGeometryValue b)
        {
            if (a == null || b == null || a.IsDegenerateLine || b.IsDegenerateLine) return double.NaN;
            PointF mid = new PointF((float)((b.X1 + b.X2) * 0.5), (float)((b.Y1 + b.Y2) * 0.5));
            PointF foot;
            return PointLineDistance(mid, a, out foot);
        }

        /// <summary>
        /// 角度归一化到 (-180, 180]。
        /// </summary>
        public static double NormalizeAngle180(double deg)
        {
            double a = deg;
            while (a <= -180.0) a += 360.0;
            while (a > 180.0) a -= 360.0;
            return a;
        }
    }
}
