using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using VisionPlatform.Native;

namespace VisionPlatform.Flow
{
    /// <summary>一组边缘对（一条亮线 / 暗线的两条边）。</summary>
    public sealed class EdgePair
    {
        /// <summary>沿投影方向在前的边缘点（亚像素，图像坐标）。</summary>
        public PointF A;

        /// <summary>沿投影方向在后的边缘点（亚像素，图像坐标）。</summary>
        public PointF B;

        /// <summary>对宽（像素）：A、B 沿投影方向的距离。</summary>
        public double Width;

        /// <summary>对中心（像素，图像坐标）。</summary>
        public PointF Center;

        /// <summary>较弱那条边的梯度幅值（与 EdgeThreshold 同量纲）。</summary>
        public double Strength;

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "宽 {0:F2}px @ ({1:F1},{2:F1})",
                Width, Center.X, Center.Y);
        }
    }

    /// <summary>一次边缘对扫描的结果。</summary>
    public sealed class EdgePairScanResult
    {
        /// <summary>命中的边缘对（已按 MinWidth / MaxWidth 过滤，按扫描顺序排列）。</summary>
        public List<EdgePair> Pairs = new List<EdgePair>();

        /// <summary>每个卡尺的投影窗口（含未命中，供可视化复用卡尺的绘制函数）。</summary>
        public List<CaliperPoint> Windows = new List<CaliperPoint>();

        /// <summary>投放的卡尺个数。</summary>
        public int CaliperCount;

        /// <summary>至少命中一对的卡尺个数。</summary>
        public int HitCalipers;
    }

    /// <summary>
    /// 边缘对查找（"线宽"测量）：沿扫描线法向逐格取样 → 一维平滑 → 求梯度极值 →
    /// 按极性把「一升一降」配成对 → 输出对宽。
    /// <para>
    /// 【为什么不走原生卡尺】卡尺的语义是"每格只取一条边并拟合成线/圆"，
    /// 而边缘对要的是<strong>同一格里两条边的间距</strong>，两者输出结构不同；
    /// 强行用两个不同极性的卡尺相减还会被各自的选边策略与离群剔除干扰。
    /// 这里把一维取样做在托管侧（与 <c>FlowRunner.Undistort</c> 同类做法），
    /// 不新增原生导出、不需要重新编译 C++ 算法库。
    /// </para>
    /// <para>
    /// 【极性定义】沿<strong>投影正方向</strong>（扫描方向顺时针旋转 90°，与卡尺一致）观察：
    /// 亮线 = 先升后降（暗→亮→暗）；暗线 = 先降后升（亮→暗→亮）。
    /// 这个定义与"从哪一头开始扫"无关，因此沿法线正反扫都成立。
    /// </para>
    /// </summary>
    public static class EdgePairScanner
    {
        /// <summary>极性：亮线（先升后降）。</summary>
        public const int PolarityBright = 0;

        /// <summary>极性：暗线（先降后升）。</summary>
        public const int PolarityDark = 1;

        /// <summary>选对策略：最强（两条边中较弱者最大）。</summary>
        public const int SelectStrongest = 0;

        /// <summary>选对策略：最宽。</summary>
        public const int SelectWidest = 1;

        /// <summary>选对策略：最窄。</summary>
        public const int SelectNarrowest = 2;

        /// <summary>选对策略：最靠前（沿投影方向第一对）。</summary>
        public const int SelectFirst = 3;

        /// <summary>取样步长（像素）。0.5 兼顾亚像素精度与耗时。</summary>
        private const double SampleStep = 0.5;

        /// <summary>
        /// 扫描一组边缘对。
        /// </summary>
        /// <param name="frame">待测帧（任意常见像素格式）。</param>
        /// <param name="x1">扫描线段起点 X（图像坐标）。</param>
        /// <param name="y1">扫描线段起点 Y。</param>
        /// <param name="x2">扫描线段终点 X。</param>
        /// <param name="y2">扫描线段终点 Y。</param>
        /// <param name="caliperCount">卡尺个数（沿扫描线等分投放）。</param>
        /// <param name="projectionLength">每格的投影长度（沿法向，px）。</param>
        /// <param name="edgeThreshold">边缘梯度阈值（灰度 / px）。</param>
        /// <param name="filterHalfWidth">一维均值滤波半宽（0 = 不滤波）。</param>
        /// <param name="polarity">极性，见 <see cref="PolarityBright"/> / <see cref="PolarityDark"/>。</param>
        /// <param name="minWidth">对宽下限（px，0 = 不限）。</param>
        /// <param name="maxWidth">对宽上限（px，0 = 不限）。</param>
        /// <param name="result">扫描结果；失败时为 null。</param>
        /// <param name="error">失败原因（中文，可直接进摘要）；成功时为空串。</param>
        /// <returns>是否至少找到一对。</returns>
        public static bool TryScan(Bitmap frame,
                                   double x1, double y1, double x2, double y2,
                                   int caliperCount, double projectionLength, double edgeThreshold,
                                   int filterHalfWidth, int polarity, double minWidth, double maxWidth,
                                   out EdgePairScanResult result, out string error)
        {
            result = null;
            error = "";

            if (frame == null) { error = "没有输入图像"; return false; }
            if (caliperCount < 1) caliperCount = 1;
            if (caliperCount > 512) caliperCount = 512;
            if (projectionLength < 1.0) projectionLength = 1.0;
            if (projectionLength > 4096.0) projectionLength = 4096.0;
            if (filterHalfWidth < 0) filterHalfWidth = 0;
            if (filterHalfWidth > 20) filterHalfWidth = 20;

            double sx = x2 - x1, sy = y2 - y1;
            double scanLen = Math.Sqrt(sx * sx + sy * sy);
            if (scanLen < 1e-6) { error = "扫描线段长度为 0"; return false; }

            // 投影正方向 = 扫描方向顺时针旋转 90°（图像坐标 y 向下）：(dx,dy) → (-dy,dx)
            double nx = -sy / scanLen, ny = sx / scanLen;

            GrayFrame gray = GrayFrame.Create(frame);
            if (gray == null) { error = "图像格式不受支持（无法取灰度）"; return false; }

            var scan = new EdgePairScanResult { CaliperCount = caliperCount };

            int sampleCount = (int)Math.Round(projectionLength / SampleStep);
            if (sampleCount < 4) sampleCount = 4;
            if (sampleCount > 8192) sampleCount = 8192;

            var raw = new double[sampleCount + 1];
            var smooth = new double[sampleCount + 1];
            var grad = new double[sampleCount + 1];
            var peaks = new List<EdgePeak>();

            for (int c = 0; c < caliperCount; c++)
            {
                double t = (c + 0.5) / caliperCount;
                double cx = x1 + sx * t;
                double cy = y1 + sy * t;

                // 窗口两端（沿法向对称展开），未命中也写出去，供叠加层区分"扫了但没找到"
                double half = projectionLength * 0.5;
                var win = new CaliperPoint
                {
                    Px1 = cx - nx * half, Py1 = cy - ny * half,
                    Px2 = cx + nx * half, Py2 = cy + ny * half,
                    Valid = false
                };

                for (int k = 0; k <= sampleCount; k++)
                {
                    double off = -half + k * SampleStep;
                    raw[k] = gray.Sample(cx + nx * off, cy + ny * off);
                }

                // 一维均值滤波：抑制噪声，避免把一个梯度峰劈成两个
                if (filterHalfWidth > 0)
                {
                    int hw = filterHalfWidth;
                    for (int k = 0; k <= sampleCount; k++)
                    {
                        double sum = 0.0;
                        int n = 0;
                        for (int q = k - hw; q <= k + hw; q++)
                        {
                            int kk = q < 0 ? 0 : (q > sampleCount ? sampleCount : q);
                            sum += raw[kk];
                            n++;
                        }
                        smooth[k] = sum / n;
                    }
                }
                else
                {
                    Array.Copy(raw, smooth, raw.Length);
                }

                // 中心差分梯度（幅值 + 符号）
                for (int k = 0; k <= sampleCount; k++)
                {
                    int k0 = k - 1 < 0 ? 0 : k - 1;
                    int k1 = k + 1 > sampleCount ? sampleCount : k + 1;
                    grad[k] = (smooth[k1] - smooth[k0]) * 0.5;
                }

                peaks.Clear();
                for (int k = 1; k < sampleCount; k++)
                {
                    double g = Math.Abs(grad[k]);
                    if (g < edgeThreshold) continue;
                    double gl = Math.Abs(grad[k - 1]);
                    double gr = Math.Abs(grad[k + 1]);

                    // 局部极大（左严右宽：平台只取一次）
                    if (g < gl || g < gr) continue;

                    // 抛物线亚像素：把 |grad| 的峰顶拟合到采样点之间
                    double den = gl - 2.0 * g + gr;
                    double d = Math.Abs(den) > 1e-12 ? 0.5 * (gl - gr) / den : 0.0;
                    if (d > 0.5) d = 0.5;
                    if (d < -0.5) d = -0.5;

                    peaks.Add(new EdgePeak
                    {
                        Pos = k + d,
                        Sign = grad[k] >= 0 ? 1 : -1,
                        Magnitude = g
                    });
                }

                int wantFirst = (polarity == PolarityDark) ? -1 : 1;
                int wantSecond = -wantFirst;

                int j = 0;
                while (j < peaks.Count)
                {
                    if (peaks[j].Sign != wantFirst) { j++; continue; }

                    int m = j + 1;
                    while (m < peaks.Count && peaks[m].Sign != wantSecond) m++;
                    if (m >= peaks.Count) break;

                    double posA = peaks[j].Pos;
                    double posB = peaks[m].Pos;
                    double width = (posB - posA) * SampleStep;

                    bool okWidth = width > 1e-6
                        && (minWidth <= 0.0 || width >= minWidth)
                        && (maxWidth <= 0.0 || width <= maxWidth);

                    if (okWidth)
                    {
                        double offA = -half + posA * SampleStep;
                        double offB = -half + posB * SampleStep;

                        var pa = new PointF((float)(cx + nx * offA), (float)(cy + ny * offA));
                        var pb = new PointF((float)(cx + nx * offB), (float)(cy + ny * offB));

                        var pair = new EdgePair
                        {
                            A = pa,
                            B = pb,
                            Width = width,
                            Center = new PointF((pa.X + pb.X) * 0.5f, (pa.Y + pb.Y) * 0.5f),
                            Strength = Math.Min(peaks[j].Magnitude, peaks[m].Magnitude)
                        };
                        scan.Pairs.Add(pair);

                        if (!win.Valid)
                        {
                            win.X = pa.X;
                            win.Y = pa.Y;
                            win.Strength = pair.Strength;
                            win.Valid = true;
                        }
                    }

                    j = m + 1;    // 一对用两条边，不重叠配对
                }

                if (win.Valid) scan.HitCalipers++;
                scan.Windows.Add(win);
            }

            result = scan;

            if (scan.Pairs.Count == 0)
            {
                error = string.Format(CultureInfo.InvariantCulture,
                    "未找到边缘对（阈值 {0:0.#}，宽度 {1:0.#}~{2}）",
                    edgeThreshold, minWidth, maxWidth > 0 ? maxWidth.ToString("0.#", CultureInfo.InvariantCulture) : "∞");
                return false;
            }
            return true;
        }

        /// <summary>按 <paramref name="selectMode"/> 选出一组"主对"（仅用于输出中心点等展示量）。</summary>
        public static EdgePair Select(List<EdgePair> pairs, int selectMode)
        {
            if (pairs == null || pairs.Count == 0) return null;

            EdgePair best = pairs[0];
            for (int i = 1; i < pairs.Count; i++)
            {
                EdgePair p = pairs[i];
                if (p == null) continue;

                switch (selectMode)
                {
                    case SelectWidest:
                        if (p.Width > best.Width) best = p;
                        break;
                    case SelectNarrowest:
                        if (p.Width < best.Width) best = p;
                        break;
                    case SelectFirst:
                        // 列表本就按扫描顺序，第 1 对即"最靠前"
                        break;
                    default:
                        if (p.Strength > best.Strength) best = p;
                        break;
                }
            }
            return best;
        }

        /// <summary>按策略排序（用于"最宽/最窄/最强"的展示顺序，第 1 对即主对）。</summary>
        public static List<EdgePair> Sort(List<EdgePair> pairs, int selectMode)
        {
            var copy = new List<EdgePair>();
            if (pairs == null) return copy;
            foreach (EdgePair p in pairs) if (p != null) copy.Add(p);

            switch (selectMode)
            {
                case SelectWidest:
                    copy.Sort(delegate(EdgePair a, EdgePair b) { return b.Width.CompareTo(a.Width); });
                    break;
                case SelectNarrowest:
                    copy.Sort(delegate(EdgePair a, EdgePair b) { return a.Width.CompareTo(b.Width); });
                    break;
                case SelectStrongest:
                    copy.Sort(delegate(EdgePair a, EdgePair b) { return b.Strength.CompareTo(a.Strength); });
                    break;
                default:
                    break;      // 保持扫描顺序
            }
            return copy;
        }

        // =====================================================================
        //  内部：一维梯度峰
        // =====================================================================

        private struct EdgePeak
        {
            /// <summary>峰位（采样点为单位，含亚像素小数）。</summary>
            public double Pos;
            /// <summary>梯度符号：+1 上升沿 / -1 下降沿。</summary>
            public int Sign;
            /// <summary>梯度幅值。</summary>
            public double Magnitude;
        }

        // =====================================================================
        //  内部：灰度取样（任意像素格式 → 双线性取样）
        // =====================================================================

        /// <summary>把一帧转成 8bpp 灰度缓冲，并按双线性取样（越界按边界钳制）。</summary>
        private sealed class GrayFrame
        {
            private int _w, _h;
            private byte[] _pixels;

            public static GrayFrame Create(Bitmap src)
            {
                if (src == null || src.Width <= 0 || src.Height <= 0) return null;

                int w = src.Width, h = src.Height;
                var g = new GrayFrame { _w = w, _h = h, _pixels = new byte[w * h] };

                Bitmap work = src;
                bool own = false;

                // 统一到 24bppRgb 读：8bpp 走调色板映射（GDI 直接转 24bpp 会丢灰度精度，没必要），
                // 其余格式（1/4/16bpp…）交给 GDI 转一次，避免为罕见格式写一堆分支。
                if (src.PixelFormat != PixelFormat.Format8bppIndexed &&
                    src.PixelFormat != PixelFormat.Format24bppRgb &&
                    src.PixelFormat != PixelFormat.Format32bppArgb &&
                    src.PixelFormat != PixelFormat.Format32bppRgb)
                {
                    try
                    {
                        work = new Bitmap(w, h, PixelFormat.Format24bppRgb);
                        using (Graphics gr = Graphics.FromImage(work))
                        {
                            gr.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                            gr.DrawImage(src, 0, 0, w, h);
                        }
                        own = true;
                    }
                    catch (Exception)
                    {
                        return null;
                    }
                }

                BitmapData bd = null;
                try
                {
                    // 锁定位图自己的格式：请求"另一种 32bpp"时 GDI+ 会做一次隐式转换，
                    // 既慢又可能失败；这里只在已确认支持的三种格式里分支。
                    PixelFormat fmt = work.PixelFormat;
                    bd = work.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, fmt);
                    int stride = bd.Stride;
                    var buf = new byte[stride * h];
                    Marshal.Copy(bd.Scan0, buf, 0, buf.Length);

                    if (fmt == PixelFormat.Format8bppIndexed)
                    {
                        // 调色板：灰度图 R=G=B，直接取 R；彩色调色板按亮度换算
                        Color[] pal = work.Palette.Entries;
                        for (int y = 0; y < h; y++)
                        {
                            int row = y * stride;
                            int dst = y * w;
                            for (int x = 0; x < w; x++)
                            {
                                int idx = buf[row + x];
                                Color c = (pal != null && idx < pal.Length) ? pal[idx] : Color.FromArgb(idx, idx, idx);
                                g._pixels[dst + x] = Luma(c.R, c.G, c.B);
                            }
                        }
                    }
                    else
                    {
                        // 32bppRgb 同样是 4 字节/像素（BGRA），按 4 字节跨度读
                        int bpp = (fmt == PixelFormat.Format32bppArgb || fmt == PixelFormat.Format32bppRgb) ? 4 : 3;
                        for (int y = 0; y < h; y++)
                        {
                            int row = y * stride;
                            int dst = y * w;
                            for (int x = 0; x < w; x++)
                            {
                                int o = row + x * bpp;
                                g._pixels[dst + x] = Luma(buf[o + 2], buf[o + 1], buf[o]);   // BGR(A)
                            }
                        }
                    }
                }
                catch (Exception)
                {
                    return null;
                }
                finally
                {
                    if (bd != null)
                    {
                        try { work.UnlockBits(bd); } catch (Exception) { }
                    }
                    if (own && work != null) work.Dispose();
                }

                return g;
            }

            private static byte Luma(int r, int g, int b)
            {
                int v = (r * 77 + g * 151 + b * 28) >> 8;      // 0.299 / 0.587 / 0.114
                if (v < 0) v = 0;
                if (v > 255) v = 255;
                return (byte)v;
            }

            /// <summary>双线性取样（越界按最近边界钳制，避免扫描线贴边时整格丢失）。</summary>
            public double Sample(double x, double y)
            {
                if (x < 0) x = 0;
                if (y < 0) y = 0;
                if (x > _w - 1) x = _w - 1;
                if (y > _h - 1) y = _h - 1;

                int x0 = (int)x, y0 = (int)y;
                int x1 = x0 + 1 < _w ? x0 + 1 : x0;
                int y1 = y0 + 1 < _h ? y0 + 1 : y0;

                double fx = x - x0, fy = y - y0;
                double v00 = _pixels[y0 * _w + x0];
                double v10 = _pixels[y0 * _w + x1];
                double v01 = _pixels[y1 * _w + x0];
                double v11 = _pixels[y1 * _w + x1];

                return v00 * (1 - fx) * (1 - fy) + v10 * fx * (1 - fy)
                     + v01 * (1 - fx) * fy + v11 * fx * fy;
            }
        }
    }
}
