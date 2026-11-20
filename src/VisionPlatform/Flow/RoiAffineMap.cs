using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using VisionSystem.Controls.Geometry;

namespace VisionPlatform.Flow
{
    /// <summary>
    /// <strong>「ROI 局部坐标 ↔ 整帧坐标」的唯一换算口径</strong>（含 ROI 自身角度 + 位置修正角度）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 【为什么必须只有一个口径】同一块 ROI 会同时出现在三个地方：运行期裁剪（像素）、
    /// 运行期叠加框（图形）、界面上的 ROI 轮廓（图形）。三处各自写一遍三角函数，
    /// 迟早出现"框画在一处、图裁在另一处"——现场表现就是
    /// 「ROI 没跟随旋转」「分割出来的字符框位置和形状不对」。
    /// 所以：**裁剪、局部点映射、局部框映射全部走本类**。
    /// </para>
    /// <para>
    /// <strong>生效角度</strong> <c>A = RoiAngle + dAngle</c>：
    /// ROI 自身的角度（示教时画出来的倾斜）与位置修正带来的旋转叠加。
    /// ROi 中心先经 <c>FlowRunTransform</c> 映射到整帧，再以 <c>A</c> 旋转 —— 两者不可拆开算。
    /// </para>
    /// <para>
    /// 【坐标约定】图像坐标 y 向下，<c>A</c> 顺时针为正（与 <see cref="RotatedRectShape.AngleDeg"/>
    /// 及全库角度约定一致）。局部点 <c>(u,v)</c> 是"以 ROI 左上角为 (0,0)"的裁剪图像素坐标。
    /// </para>
    /// </remarks>
    public static class RoiAffineMap
    {
        /// <summary>角度判定阈值（度）：小于它的角度视为"没转"，仍走整数裁剪（快且像素完全一致）。</summary>
        public const double AngleEpsilon = 0.01;

        /// <summary>
        /// 生效角度（度，顺时针为正）= ROI 自身角度 + 位置修正角度。
        /// </summary>
        public static double EffectiveAngle(FlowRunTransform t, double roiAngleDeg)
        {
            double d = (t != null && t.IsValid) ? t.DAngle : 0.0;
            return roiAngleDeg + d;
        }

        /// <summary>
        /// 是否必须走仿射（ROI 自身带角度，或位置修正带旋转）。
        /// <para>返回 false 时调用方应使用整数裁剪 + 左上角偏移，以获得逐像素一致的结果。</para>
        /// </summary>
        public static bool NeedsAffine(FlowRunTransform t, double roiAngleDeg)
        {
            return Math.Abs(EffectiveAngle(t, roiAngleDeg)) > AngleEpsilon;
        }

        /// <summary>
        /// 仿射分支下 ROI 中心在<strong>整帧</strong>上的位置。
        /// </summary>
        /// <param name="roi">用户坐标（示教坐标）的 ROI，未做任何变换。</param>
        public static PointF FrameCenter(FlowRunTransform t, Rectangle roi)
        {
            double cx = roi.X + roi.Width / 2.0;
            double cy = roi.Y + roi.Height / 2.0;
            if (t == null) return new PointF((float)cx, (float)cy);
            return t.ApplyToPoint(cx, cy);
        }

        /// <summary>
        /// ROI 局部坐标 <c>(u,v)</c> → 整帧坐标。三种情形<strong>不能混</strong>：
        /// <list type="bullet">
        /// <item>仿射（ROI 或位姿带角度）：<c>Tc + R(A)·((u,v) − (w/2,h/2))</c>；</item>
        /// <item>轴对齐裁剪：ROI 已含平移，只需加左上角；</item>
        /// <item>整帧（无 ROI）：坐标本身还要套一次位姿 —— 位置修正改的是<strong>坐标</strong>、
        /// 不是像素，漏了这一步表现为"上游做了位置修正，字符框还在原地不动"。</item>
        /// </list>
        /// </summary>
        /// <param name="roiUsed">轴对齐分支 = 实际裁剪矩形（已含平移）；
        /// 仿射分支 = <strong>用户坐标</strong>的 ROI（变换由本方法施加）。</param>
        public static PointF MapPoint(FlowRunTransform t, Rectangle roiUsed, double roiAngleDeg,
                                      bool affine, double u, double v)
        {
            if (!affine)
            {
                if (roiUsed.IsEmpty)
                    return (t != null) ? t.ApplyToPoint(u, v) : new PointF((float)u, (float)v);
                return new PointF((float)(u + roiUsed.X), (float)(v + roiUsed.Y));
            }

            PointF c = FrameCenter(t, roiUsed);
            double a = EffectiveAngle(t, roiAngleDeg) * Math.PI / 180.0;
            double du = u - roiUsed.Width / 2.0;
            double dv = v - roiUsed.Height / 2.0;
            double cos = Math.Cos(a);
            double sin = Math.Sin(a);

            return new PointF((float)(c.X + cos * du - sin * dv),
                              (float)(c.Y + sin * du + cos * dv));
        }

        /// <summary>
        /// 局部矩形（左上 + 宽高）→ 整帧上的图形。
        /// <para>
        /// <strong>仿射时必须返回旋转矩形</strong>：字符框在裁剪图里是正放的，
        /// 但工件转了 10°，它在整帧上就是斜着的。画成轴对齐框会"中心对、四角全错"，
        /// 也就是「分割字符的 ROI 位置和形状不对」。
        /// </para>
        /// </summary>
        public static RotatedRectShape MapBox(FlowRunTransform t, Rectangle roiUsed, double roiAngleDeg,
                                              bool affine, double lx, double ly, double w, double h)
        {
            PointF c = MapPoint(t, roiUsed, roiAngleDeg, affine, lx + w / 2.0, ly + h / 2.0);
            float angle = affine ? (float)EffectiveAngle(t, roiAngleDeg) : 0f;
            return new RotatedRectShape(c, (float)Math.Max(1.0, w), (float)Math.Max(1.0, h), angle);
        }

        /// <summary>
        /// <strong>仿射裁剪</strong>：把"用户坐标 ROI"按 <c>A = RoiAngle + dAngle</c> 采成轴对齐小块。
        /// </summary>
        /// <remarks>
        /// <para>映射关系：目标像素 <c>(u,v)</c> ← 整帧上的点 <c>MapPoint(u,v)</c>。</para>
        /// <para>
        /// 实现用 Graphics 的反变换（dst → src）采样：
        /// <c>dst(u,v) = R(−A)·(src − Tc) + (w/2, h/2)</c>，其中 <c>Tc = T(ROI 中心)</c>。
        /// 与 <see cref="MapPoint"/> 严格互为逆运算（<see cref="MapPoint"/> 由 dst 求 src，
        /// 这里由 src 求 dst），口径不可能分叉。
        /// </para>
        /// <para>
        /// 【为什么必须真仿射而不是取外接框】<c>ApplyToRect</c> 给的是旋转后的外接矩形：
        /// 工件转 10° 时 ROI 会"变胖"一圈，OCR 会把邻域杂边一起框进来。
        /// 真仿射拿到的是一块"跟着工件一起转"的图，识别结果与不旋转时完全等价。
        /// </para>
        /// </remarks>
        public static Bitmap Crop(Bitmap frame, Rectangle roi, double roiAngleDeg, FlowRunTransform t)
        {
            if (frame == null || roi.Width <= 0 || roi.Height <= 0) return null;

            int w = roi.Width;
            int h = roi.Height;

            var dst = new Bitmap(w, h, PixelFormat.Format24bppRgb);
            try
            {
                using (var g = Graphics.FromImage(dst))
                {
                    g.Clear(Color.Black);   // 旋转露出的边角填黑：与整图对齐（Warp）时的口径一致
                    g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                    PointF c = FrameCenter(t, roi);
                    float a = (float)EffectiveAngle(t, roiAngleDeg);

                    g.TranslateTransform(w / 2f, h / 2f);
                    g.RotateTransform(-a);
                    g.TranslateTransform(-c.X, -c.Y);
                    g.DrawImage(frame, new Rectangle(0, 0, frame.Width, frame.Height));
                }
            }
            catch (Exception)
            {
                dst.Dispose();
                return null;
            }

            return dst;
        }
    }
}
