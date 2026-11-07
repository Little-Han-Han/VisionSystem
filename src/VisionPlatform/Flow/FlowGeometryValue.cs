using System;
using System.Drawing;
using System.Globalization;

namespace VisionPlatform.Flow
{
    /// <summary>几何端子值的种类。</summary>
    public enum FlowGeometryKind
    {
        /// <summary>直线段（两端点，图像坐标）。</summary>
        Line = 1,

        /// <summary>圆（圆心 + 半径，图像坐标）。</summary>
        Circle = 2
    }

    /// <summary>
    /// 几何端子值：<see cref="FlowPortKind.Geometry"/> 端子上真正传递的对象。
    /// <para>
    /// 【为什么要一个专门的载体】端子的数据类型只是"能不能连线"的判定依据，
    /// 真正让下游算得出结果的是<strong>几何本身</strong>（直线的两个端点 / 圆的圆心半径）。
    /// 若只传 Length / Angle 这类标量，"线线测量"就无从求交点，"点直线测量"也无从求垂足。
    /// </para>
    /// <para>
    /// 【坐标口径】一律<strong>图像坐标（工作帧）</strong>，与叠加层、参数里的 ROI 同一套口径；
    /// 位置修正已经由各卡尺节点在取 ROI 时应用过，本值不再做二次变换。
    /// </para>
    /// <para>本类型只承载数据、不做算法；几何运算见 <see cref="FlowGeometryMath"/>。</para>
    /// </summary>
    public sealed class FlowGeometryValue
    {
        /// <summary>种类（决定下面哪些字段有效）。</summary>
        public FlowGeometryKind Kind;

        // ---- 直线（Kind = Line）----
        /// <summary>起点 X。</summary>
        public double X1;
        /// <summary>起点 Y。</summary>
        public double Y1;
        /// <summary>终点 X。</summary>
        public double X2;
        /// <summary>终点 Y。</summary>
        public double Y2;

        // ---- 圆（Kind = Circle）----
        /// <summary>圆心 X。</summary>
        public double Cx;
        /// <summary>圆心 Y。</summary>
        public double Cy;
        /// <summary>半径。</summary>
        public double Radius;

        /// <summary>是否为直线。</summary>
        public bool IsLine { get { return Kind == FlowGeometryKind.Line; } }

        /// <summary>是否为圆。</summary>
        public bool IsCircle { get { return Kind == FlowGeometryKind.Circle; } }

        /// <summary>直线长度（圆返回 0）。</summary>
        public double Length
        {
            get
            {
                if (!IsLine) return 0.0;
                double dx = X2 - X1, dy = Y2 - Y1;
                return Math.Sqrt(dx * dx + dy * dy);
            }
        }

        /// <summary>直线方向角（度，归一化到 (-90, 90]；圆返回 0）。</summary>
        public double AngleDeg
        {
            get
            {
                if (!IsLine) return 0.0;
                double a = Math.Atan2(Y2 - Y1, X2 - X1) * 180.0 / Math.PI;
                while (a <= -90.0) a += 180.0;
                while (a > 90.0) a -= 180.0;
                return a;
            }
        }

        /// <summary>直线中点 / 圆心。</summary>
        public PointF Center
        {
            get
            {
                if (IsLine) return new PointF((float)((X1 + X2) * 0.5), (float)((Y1 + Y2) * 0.5));
                return new PointF((float)Cx, (float)Cy);
            }
        }

        /// <summary>构造一条直线。</summary>
        public static FlowGeometryValue Line(double x1, double y1, double x2, double y2)
        {
            return new FlowGeometryValue
            {
                Kind = FlowGeometryKind.Line,
                X1 = x1, Y1 = y1, X2 = x2, Y2 = y2
            };
        }

        /// <summary>构造一个圆。</summary>
        public static FlowGeometryValue Circle(double cx, double cy, double radius)
        {
            return new FlowGeometryValue
            {
                Kind = FlowGeometryKind.Circle,
                Cx = cx, Cy = cy, Radius = radius
            };
        }

        /// <summary>直线退化判定：两端点重合 / 坐标为 NaN 时不可用于求交、求距。</summary>
        public bool IsDegenerateLine
        {
            get
            {
                if (!IsLine) return true;
                if (double.IsNaN(X1) || double.IsNaN(Y1) || double.IsNaN(X2) || double.IsNaN(Y2)) return true;
                double dx = X2 - X1, dy = Y2 - Y1;
                return (dx * dx + dy * dy) < 1e-12;
            }
        }

        public override string ToString()
        {
            if (IsLine)
            {
                return string.Format(CultureInfo.InvariantCulture, "直线 ({0:F1},{1:F1})→({2:F1},{3:F1}) 长 {4:F1}",
                    X1, Y1, X2, Y2, Length);
            }
            return string.Format(CultureInfo.InvariantCulture, "圆 ({0:F1},{1:F1}) R={2:F1}", Cx, Cy, Radius);
        }
    }
}
