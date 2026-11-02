using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace VisionPlatform.Calibration
{
    /// <summary>一个标定采样点：图像像素坐标 ↔ 机械坐标（mm）的一一对应。</summary>
    public struct CalibPoint
    {
        /// <summary>像素 X（px）。</summary>
        public double PixelX;

        /// <summary>像素 Y（px）。</summary>
        public double PixelY;

        /// <summary>机械 X（mm）。</summary>
        public double MachineX;

        /// <summary>机械 Y（mm）。</summary>
        public double MachineY;

        /// <summary>构造一个对应点。</summary>
        public CalibPoint(double pixelX, double pixelY, double machineX, double machineY)
        {
            PixelX = pixelX;
            PixelY = pixelY;
            MachineX = machineX;
            MachineY = machineY;
        }

        /// <summary>四个分量是否都是有限数（NaN / Infinity 视为无效点）。</summary>
        public bool IsValid
        {
            get
            {
                return IsFinite(PixelX) && IsFinite(PixelY) && IsFinite(MachineX) && IsFinite(MachineY);
            }
        }

        private static bool IsFinite(double v)
        {
            return !double.IsNaN(v) && !double.IsInfinity(v);
        }
    }

    /// <summary>
    /// N 点标定的解算结果。
    /// <para>
    /// 【模型】二维相似变换（Helmert）：机械坐标 = R(θ)·s·像素坐标 + T，
    /// 即
    /// <code>
    /// 机械X = a * 像素X - b * 像素Y + Tx
    /// 机械Y = b * 像素X + a * 像素Y + Ty     其中 a = s·cosθ，b = s·sinθ
    /// </code>
    /// 这是手眼标定里最常见的 4 自由度模型（旋转 + 尺度 + 平移），
    /// 不引入错切 / 透视，因此 3 个点即可定解、多点时参数不会被噪声放大。
    /// </para>
    /// </summary>
    public sealed class CalibResult
    {
        /// <summary>解算是否成功。</summary>
        public bool Success { get; set; }

        /// <summary>失败原因 / 成功提示（可直接显示给用户）。</summary>
        public string Message { get; set; }

        /// <summary>变换矩阵 A11（像素X 对 机械X 的系数）。</summary>
        public double A11 { get; set; }

        /// <summary>变换矩阵 A12（像素Y 对 机械X 的系数，相似变换下 = -b）。</summary>
        public double A12 { get; set; }

        /// <summary>变换矩阵 A21（像素X 对 机械Y 的系数，相似变换下 = b）。</summary>
        public double A21 { get; set; }

        /// <summary>变换矩阵 A22（像素Y 对 机械Y 的系数，相似变换下 = a）。</summary>
        public double A22 { get; set; }

        /// <summary>平移 Tx（mm）。</summary>
        public double Tx { get; set; }

        /// <summary>平移 Ty（mm）。</summary>
        public double Ty { get; set; }

        /// <summary>旋转角（度，逆时针为正，像素坐标系 → 机械坐标系）。</summary>
        public double RotationDeg { get; set; }

        /// <summary>尺度（mm / px）。</summary>
        public double ScaleMmPerPx { get; set; }

        /// <summary>RMS 残差（mm）：sqrt(mean(e_i²))。</summary>
        public double RmsError { get; set; }

        /// <summary>平均偏移（mm）：mean(e_i)。</summary>
        public double MeanOffset { get; set; }

        /// <summary>最大偏移（mm）：max(e_i)。</summary>
        public double MaxOffset { get; set; }

        /// <summary>精度（1σ，mm）：残差的样本标准差 sqrt(Σ(e_i-mean)²/(n-1))。</summary>
        public double Precision { get; set; }

        /// <summary>参与解算的点数。</summary>
        public int PointCount { get; set; }

        /// <summary>把解算结果打包成 <see cref="CalibRecord.Affine"/> 的 6 元组。</summary>
        public double[] ToAffine6()
        {
            return new double[CalibRecord.AffineLength] { A11, A12, A21, A22, Tx, Ty };
        }

        /// <summary>把「结果」文本区要显示的完整报告拼出来（等宽字体对齐）。</summary>
        public string Describe()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("旋转角    : " + RotationDeg.ToString("F4", CultureInfo.InvariantCulture) + " °");
            sb.AppendLine("尺度      : " + ScaleMmPerPx.ToString("F6", CultureInfo.InvariantCulture) + " mm/px");
            sb.AppendLine("平移      : X " + Tx.ToString("F4", CultureInfo.InvariantCulture)
                          + "   Y " + Ty.ToString("F4", CultureInfo.InvariantCulture) + " mm");
            sb.AppendLine();
            sb.AppendLine("RMS 残差  : " + RmsError.ToString("F4", CultureInfo.InvariantCulture) + " mm");
            sb.AppendLine("平均偏移  : " + MeanOffset.ToString("F4", CultureInfo.InvariantCulture) + " mm");
            sb.AppendLine("最大偏移  : " + MaxOffset.ToString("F4", CultureInfo.InvariantCulture) + " mm");
            sb.AppendLine("精度 1σ   : " + Precision.ToString("F4", CultureInfo.InvariantCulture) + " mm");
            sb.AppendLine("参与点数  : " + PointCount.ToString(CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        /// <summary>用解算出的变换把像素坐标换算成机械坐标。</summary>
        public bool TryTransform(double pixelX, double pixelY, out double machineX, out double machineY)
        {
            machineX = A11 * pixelX + A12 * pixelY + Tx;
            machineY = A21 * pixelX + A22 * pixelY + Ty;
            return Success;
        }

        /// <summary>造一个失败结果（绝不抛异常，调用方只看 Success / Message）。</summary>
        internal static CalibResult Fail(string message, int pointCount)
        {
            CalibResult r = new CalibResult();
            r.Success = false;
            r.Message = message ?? "";
            r.PointCount = pointCount;
            return r;
        }
    }

    /// <summary>
    /// N 点标定求解器：最小二乘拟合二维相似变换（旋转 + 尺度 + 平移）。
    /// <para>
    /// 【数学】把每点写成
    /// <code>
    /// 机械X_i = a·px_i - b·py_i + Tx
    /// 机械Y_i = b·px_i + a·py_i + Ty
    /// </code>
    /// 先减去形心（去掉平移，解耦），再对 (a, b) 建正规方程：
    /// <code>
    /// [ Σ(dx²+dy²)      0     ] [a]   [ Σ(dx·dX + dy·dY) ]
    /// [      0     Σ(dx²+dy²) ] [b] = [ Σ(dx·dY - dy·dX) ]
    /// </code>
    /// 用带部分主元的高斯消元解这个 2 阶方程组（零第三方依赖），
    /// 再回代求平移。这是相似变换的闭式最小二乘解，等价于 SVD 解，但没有矩阵分解依赖。
    /// </para>
    /// <para>
    /// 【为什么不用透视 / 仿射】像素 → 机械 的物理成因是「镜头尺度 + 安装旋转 + 平台平移」，
    /// 相似变换的 4 个自由度正对应这 4 个物理量；错切 / 透视多出来的自由度会把镜头畸变、
    /// 对点误差一起「拟合掉」，表现为 RMS 很好看但复现精度差。畸变应当由下一轮的
    /// 相机内参标定单独处理，而不是塞进手眼矩阵里。
    /// </para>
    /// </summary>
    public static class CalibSolver
    {
        /// <summary>相似变换至少需要的点数（4 个参数 → ≥3 点才能给出残差）。</summary>
        public const int MinPointCount = 3;

        /// <summary>判定「所有像素点重合」的阈值（px²）。</summary>
        private const double DegenerateThreshold = 1e-12;

        /// <summary>高斯消元的主元下限。</summary>
        private const double PivotEpsilon = 1e-15;

        /// <summary>
        /// 解算。点数不足 / 点退化 / 方程奇异时返回 <c>Success = false</c> 与可读中文原因，
        /// <b>绝不抛异常</b>（界面直接显示 Message 即可）。
        /// </summary>
        /// <param name="points">对应点集合；null 或空按「点数不足」处理。</param>
        public static CalibResult Solve(IList<CalibPoint> points)
        {
            int total = (points == null) ? 0 : points.Count;

            // 过滤掉无效点（NaN / Infinity），但点数判定用过滤后的有效点数
            List<CalibPoint> valid = new List<CalibPoint>(total);
            for (int i = 0; i < total; i++)
            {
                if (points[i].IsValid) valid.Add(points[i]);
            }

            int n = valid.Count;
            if (n < MinPointCount)
            {
                return CalibResult.Fail(
                    "至少需要 " + MinPointCount.ToString(CultureInfo.InvariantCulture) + " 个有效点才能解算（当前 "
                    + n.ToString(CultureInfo.InvariantCulture) + " 个）", n);
            }

            // ---------- 1. 形心 ----------
            double pixelCx = 0.0, pixelCy = 0.0, machineCx = 0.0, machineCy = 0.0;
            for (int i = 0; i < n; i++)
            {
                pixelCx += valid[i].PixelX;
                pixelCy += valid[i].PixelY;
                machineCx += valid[i].MachineX;
                machineCy += valid[i].MachineY;
            }
            pixelCx /= n; pixelCy /= n; machineCx /= n; machineCy /= n;

            // ---------- 2. 正规方程的系数 ----------
            double sumPixelSpread = 0.0;   // Σ(dx² + dy²)
            double rhsA = 0.0;             // Σ(dx·dX + dy·dY)
            double rhsB = 0.0;             // Σ(dx·dY - dy·dX)

            for (int i = 0; i < n; i++)
            {
                double dx = valid[i].PixelX - pixelCx;
                double dy = valid[i].PixelY - pixelCy;
                double dX = valid[i].MachineX - machineCx;
                double dY = valid[i].MachineY - machineCy;

                sumPixelSpread += dx * dx + dy * dy;
                rhsA += dx * dX + dy * dY;
                rhsB += dx * dY - dy * dX;
            }

            if (sumPixelSpread <= DegenerateThreshold)
            {
                return CalibResult.Fail("所有像素点几乎重合（或共点），无法解算旋转与尺度；请让标定点尽量分散", n);
            }

            // ---------- 3. 高斯消元解 2 阶正规方程 ----------
            double[,] matrix = new double[2, 2];
            matrix[0, 0] = sumPixelSpread;
            matrix[0, 1] = 0.0;
            matrix[1, 0] = 0.0;
            matrix[1, 1] = sumPixelSpread;

            double[] rhs = new double[2] { rhsA, rhsB };
            double[] solution = new double[2];

            if (!SolveGaussian(matrix, rhs, solution))
            {
                // 注意：4 自由度的相似变换对「共线点」是唯一可解的（转+尺+平移），
                // 只有所有点几乎重合才会奇异 —— 文案别写"共线"，否则读代码的人会以为有保护。
                return CalibResult.Fail("标定点几乎全部重合，无法解算旋转与尺度；请让标定点尽量分散", n);
            }

            double a = solution[0];
            double b = solution[1];

            double scale = Math.Sqrt(a * a + b * b);
            if (scale <= DegenerateThreshold)
            {
                return CalibResult.Fail("解算出的尺度为 0（像素点与机械点无有效对应），请检查输入数据", n);
            }

            double rotationDeg = Math.Atan2(b, a) * 180.0 / Math.PI;

            // ---------- 4. 回代求平移（用形心，避免逐点累加误差） ----------
            double tx = machineCx - (a * pixelCx - b * pixelCy);
            double ty = machineCy - (b * pixelCx + a * pixelCy);

            CalibResult result = new CalibResult();
            result.Success = true;
            result.Message = "解算成功";
            result.A11 = a;
            result.A12 = -b;
            result.A21 = b;
            result.A22 = a;
            result.Tx = tx;
            result.Ty = ty;
            result.RotationDeg = rotationDeg;
            result.ScaleMmPerPx = scale;
            result.PointCount = n;

            // ---------- 5. 残差统计（偏移量 = 预测点与实测点的欧氏距离，单位 mm） ----------
            double sumSquare = 0.0;
            double sumOffset = 0.0;
            double maxOffset = 0.0;
            double[] offsets = new double[n];

            for (int i = 0; i < n; i++)
            {
                double predictX = a * valid[i].PixelX - b * valid[i].PixelY + tx;
                double predictY = b * valid[i].PixelX + a * valid[i].PixelY + ty;

                double ex = predictX - valid[i].MachineX;
                double ey = predictY - valid[i].MachineY;
                double offset = Math.Sqrt(ex * ex + ey * ey);

                offsets[i] = offset;
                sumSquare += offset * offset;
                sumOffset += offset;
                if (offset > maxOffset) maxOffset = offset;
            }

            double meanOffset = sumOffset / n;
            result.RmsError = Math.Sqrt(sumSquare / n);
            result.MeanOffset = meanOffset;
            result.MaxOffset = maxOffset;

            // 精度取残差的样本标准差（n-1 自由度）；n<2 时无意义，给 0
            if (n > 1)
            {
                double variance = 0.0;
                for (int i = 0; i < n; i++)
                {
                    double d = offsets[i] - meanOffset;
                    variance += d * d;
                }
                result.Precision = Math.Sqrt(variance / (n - 1));
            }
            else
            {
                result.Precision = 0.0;
            }

            return result;
        }

        /// <summary>
        /// 带部分主元的高斯消元求解 A·x = b（原地改写传入的矩阵与右端向量）。
        /// 判据：某列主元绝对值 ≤ <see cref="PivotEpsilon"/> 视为奇异，返回 false。
        /// </summary>
        private static bool SolveGaussian(double[,] a, double[] b, double[] x)
        {
            int n = b.Length;

            for (int col = 0; col < n; col++)
            {
                // 选主元
                int pivotRow = col;
                double bestValue = Math.Abs(a[col, col]);
                for (int row = col + 1; row < n; row++)
                {
                    double v = Math.Abs(a[row, col]);
                    if (v > bestValue) { bestValue = v; pivotRow = row; }
                }

                if (bestValue <= PivotEpsilon) return false;

                if (pivotRow != col)
                {
                    for (int c = col; c < n; c++)
                    {
                        double tmp = a[col, c];
                        a[col, c] = a[pivotRow, c];
                        a[pivotRow, c] = tmp;
                    }
                    double tmpB = b[col];
                    b[col] = b[pivotRow];
                    b[pivotRow] = tmpB;
                }

                // 向前消元
                double diagonal = a[col, col];
                for (int row = col + 1; row < n; row++)
                {
                    double factor = a[row, col] / diagonal;
                    if (factor == 0.0) continue;

                    for (int c = col; c < n; c++) a[row, c] -= factor * a[col, c];
                    b[row] -= factor * b[col];
                }
            }

            // 回代
            for (int row = n - 1; row >= 0; row--)
            {
                double sum = b[row];
                for (int c = row + 1; c < n; c++) sum -= a[row, c] * x[c];
                x[row] = sum / a[row, row];
            }

            return true;
        }
    }
}
