using System;
using System.Collections.Generic;

namespace VisionPlatform.Calibration
{
    /// <summary>
    /// 旋转中心（回转中心）计算：把一组绕同一轴心旋转得到的点拟合成圆，圆心即旋转中心。
    /// <para>
    /// 【为什么用圆拟合而不是"取三点求外心"】现场通常采 6~12 个角度点，三点法对单点误差
    /// 极其敏感（一个点偏 0.05mm，圆心可能偏 0.3mm）。最小二乘圆拟合把所有点都用上，
    /// 并且能给出残差 RMS 作为"这次采点可不可信"的判据。
    /// </para>
    /// <para>
    /// 算法用 Kåsa 代数拟合：最小化 Σ(x²+y²+Dx+Ey+F)²，对 D/E/F 是线性最小二乘，
    /// 3×3 正规方程 + 高斯消元即可求解，零第三方依赖。
    /// </para>
    /// </summary>
    public static class RotationCenterSolver
    {
        /// <summary>拟合结果。</summary>
        public sealed class Result
        {
            public bool Success;
            public string Message = "";

            /// <summary>圆心 X（旋转中心）。</summary>
            public double CenterX;
            /// <summary>圆心 Y。</summary>
            public double CenterY;
            /// <summary>拟合半径。</summary>
            public double Radius;
            /// <summary>残差 RMS（越小越好；一般 &lt; 0.05 视为可用）。</summary>
            public double Rms;
            /// <summary>最大单点偏差。</summary>
            public double MaxError;
            /// <summary>参与拟合的点数。</summary>
            public int PointCount;

            public static Result Fail(string message)
            {
                return new Result { Success = false, Message = message ?? "" };
            }
        }

        /// <summary>
        /// 用一组点拟合圆。
        /// </summary>
        /// <param name="xs">X 坐标。</param>
        /// <param name="ys">Y 坐标。</param>
        /// <param name="count">有效点数（&lt; 3 直接失败）。</param>
        public static Result Fit(double[] xs, double[] ys, int count)
        {
            if (xs == null || ys == null) return Result.Fail("没有数据点");
            if (count < 3) return Result.Fail("至少需要 3 个点才能拟合旋转中心（当前 " + count + " 个）");
            if (xs.Length < count || ys.Length < count) return Result.Fail("点数与数组长度不一致");

            // 归一化：把点平移到形心、按尺度缩放，避免 x²+y² 量级过大导致正规方程病态
            double mx = 0, my = 0;
            for (int i = 0; i < count; i++) { mx += xs[i]; my += ys[i]; }
            mx /= count; my /= count;

            double scale = 0;
            for (int i = 0; i < count; i++)
            {
                double dx = xs[i] - mx, dy = ys[i] - my;
                scale += Math.Sqrt(dx * dx + dy * dy);
            }
            scale /= count;
            if (scale < 1e-9) return Result.Fail("所有点几乎重合，无法拟合旋转中心");
            double inv = 1.0 / scale;

            // 正规方程： [Σx² Σx Σ1] [D]   [-Σx(x²+y²)]
            //            [Σx  Σy Σ1]? 这里按 Kåsa 标准形式组装 3×3
            double sxx = 0, syy = 0, sxy = 0, sx = 0, sy = 0, sxz = 0, syz = 0, sz = 0;
            for (int i = 0; i < count; i++)
            {
                double x = (xs[i] - mx) * inv;
                double y = (ys[i] - my) * inv;
                double z = x * x + y * y;

                sxx += x * x; syy += y * y; sxy += x * y;
                sx += x; sy += y;
                sxz += x * z; syz += y * z; sz += z;
            }

            var a = new double[3, 3];
            var b = new double[3];
            a[0, 0] = sxx; a[0, 1] = sxy; a[0, 2] = sx;  b[0] = -sxz;
            a[1, 0] = sxy; a[1, 1] = syy; a[1, 2] = sy;  b[1] = -syz;
            a[2, 0] = sx;  a[2, 1] = sy;  a[2, 2] = count; b[2] = -sz;

            double[] sol;
            if (!Solve(a, b, out sol)) return Result.Fail("方程退化（点近似共线），无法拟合旋转中心");

            double d = sol[0], e = sol[1], f = sol[2];
            double cx = -d * 0.5;
            double cy = -e * 0.5;
            double r2 = cx * cx + cy * cy - f;
            if (r2 <= 0) return Result.Fail("拟合失败：半径计算出现负值（点分布异常）");

            double r = Math.Sqrt(r2);

            // 残差
            double sumSq = 0, maxErr = 0;
            for (int i = 0; i < count; i++)
            {
                double x = (xs[i] - mx) * inv;
                double y = (ys[i] - my) * inv;
                double dx = x - cx, dy = y - cy;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                double err = dist - r;

                sumSq += err * err;
                if (Math.Abs(err) > maxErr) maxErr = Math.Abs(err);
            }

            // 还原到原始量纲
            var result = new Result
            {
                Success = true,
                CenterX = cx * scale + mx,
                CenterY = cy * scale + my,
                Radius = r * scale,
                Rms = Math.Sqrt(sumSq / count) * scale,
                MaxError = maxErr * scale,
                PointCount = count,
                Message = ""
            };
            return result;
        }

        /// <summary>高斯消元（带部分主元）。系数矩阵会被就地修改。</summary>
        private static bool Solve(double[,] a, double[] b, out double[] x)
        {
            int n = b.Length;
            x = new double[n];

            for (int col = 0; col < n; col++)
            {
                int pivot = col;
                double best = Math.Abs(a[col, col]);
                for (int r2 = col + 1; r2 < n; r2++)
                {
                    double v = Math.Abs(a[r2, col]);
                    if (v > best) { best = v; pivot = r2; }
                }

                if (best < 1e-12) return false;

                if (pivot != col)
                {
                    for (int c2 = 0; c2 < n; c2++) { double t = a[col, c2]; a[col, c2] = a[pivot, c2]; a[pivot, c2] = t; }
                    double tb = b[col]; b[col] = b[pivot]; b[pivot] = tb;
                }

                double p = a[col, col];
                for (int r2 = col + 1; r2 < n; r2++)
                {
                    double factor = a[r2, col] / p;
                    if (factor == 0) continue;
                    for (int c2 = col; c2 < n; c2++) a[r2, c2] -= factor * a[col, c2];
                    b[r2] -= factor * b[col];
                }
            }

            for (int r2 = n - 1; r2 >= 0; r2--)
            {
                double sum = b[r2];
                for (int c2 = r2 + 1; c2 < n; c2++) sum -= a[r2, c2] * x[c2];
                x[r2] = sum / a[r2, r2];
            }
            return true;
        }
    }
}
