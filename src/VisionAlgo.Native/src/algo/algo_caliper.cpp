// ============================================================================
//  卡尺工具实现：直线卡尺 / 圆卡尺
//
//  严格对应架构文档 §3.1「实现要点」十一歩：
//    ① 输入校验    —— 空指针 / 通道数 / 卡尺个数 / 输出缓冲容量 / 线段退化
//    ② 等分投放    —— 直线 (i+0.5)/N 沿线段；圆 θ=(start + (i+0.5)/N·span)
//    ③ 投影采样    —— 沿 ±n 方向双线性采样，越界按边缘钳制（不补 0）
//    ④ 一维滤波    —— [-1,0,1] 差分得梯度，再做半宽 filter_half_width 均值滤波
//    ⑤ 找边        —— 按 polarity 过滤符号，按 select_mode 选点，幅值 < 阈值记未命中
//    ⑥ 亚像素      —— 抛物线插值 δ = 0.5(f[j-1]-f[j+1]) / (f[j-1]-2f[j]+f[j+1])
//    ⑦ 拟合        —— 直线用总最小二乘（协方差主方向）；圆用代数最小二乘（去均值后 SVD）
//    ⑧ 残差        —— 点到直线距离 / |dist-R| 的 RMS
//    ⑨ 离群剔除    —— reject_outliers 时按 outlier_tolerance 剔除并重新拟合【仅一轮】
//    ⑩ 端点/圆     —— 直线取扫描线段在拟合直线上的投影；圆输出圆心/半径，residual=RMS
//    ⑪ 错误        —— 有效点 < min_edge_count → VA_ERR_NO_EDGE +「有效边缘点 N < M」
//
//  边界安全：ROI 越界由 SampleGray 钳制；拟合前检查点数与退化（除零）；
//  所有 OpenCV / STL 异常在导出层之前就在此转码为 VA_ERR_INTERNAL。
// ============================================================================

#include "va_internal.h"
#include "va_error.h"

#include <cmath>
#include <cstdio>
#include <sstream>
#include <vector>

namespace va {
namespace {

const double kPi = 3.14159265358979323846;

// ---------------------------------------------------------------------------
// ③ 投影采样：双线性插值。越界坐标按【边缘钳制】而不是补 0，
//    否则图像边界会被当成一条强黑边而误命中。
// ---------------------------------------------------------------------------
double SampleGray(const cv::Mat& g, double x, double y)
{
    const int w = g.cols;
    const int h = g.rows;

    if (x < 0.0) x = 0.0;
    if (y < 0.0) y = 0.0;
    if (x > (double)(w - 1)) x = (double)(w - 1);
    if (y > (double)(h - 1)) y = (double)(h - 1);

    int x0 = (int)std::floor(x);
    int y0 = (int)std::floor(y);
    int x1 = x0 + 1;
    int y1 = y0 + 1;
    if (x1 > w - 1) x1 = w - 1;
    if (y1 > h - 1) y1 = h - 1;

    const double fx = x - (double)x0;
    const double fy = y - (double)y0;

    const unsigned char* r0 = g.ptr<unsigned char>(y0);
    const unsigned char* r1 = g.ptr<unsigned char>(y1);

    const double v00 = (double)r0[x0];
    const double v10 = (double)r0[x1];
    const double v01 = (double)r1[x0];
    const double v11 = (double)r1[x1];

    return v00 * (1.0 - fx) * (1.0 - fy)
         + v10 * fx * (1.0 - fy)
         + v01 * (1.0 - fx) * fy
         + v11 * fx * fy;
}

// 一条投影窗口：起点（内侧）+ 单位方向 + 长度
struct CaliperWindow {
    double sx, sy;
    double nx, ny;
    double length;
};

// ---------------------------------------------------------------------------
// ④⑤⑥ 在一条投影线上完成「采样 → 滤波 → 差分 → 极性过滤 → 选边 → 亚像素」。
// 无论是否命中都会填写 out 的窗口两端，便于上层画出未命中的卡尺。
// 返回是否命中。
// ---------------------------------------------------------------------------
bool FindEdgeOnProfile(const cv::Mat& g,
                       const CaliperWindow& cw,
                       int filterHalfWidth,
                       int polarity,
                       int selectMode,
                       double threshold,
                       VA_CaliperPoint& out)
{
    double len = cw.length;
    if (!(len > 0.0)) len = 1.0;

    int m = (int)std::ceil(len) + 1;
    if (m < 3) m = 3;
    const double step = len / (double)(m - 1);

    // ③ 采样
    std::vector<double> prof(m, 0.0);
    for (int j = 0; j < m; ++j) {
        const double t = step * (double)j;
        prof[j] = SampleGray(g, cw.sx + cw.nx * t, cw.sy + cw.ny * t);
    }

    // ④ 一阶差分（[-1,0,1] 卷积）：幅值即「跨 2 px 的灰度跳变」，与阈值同量纲
    std::vector<double> grad(m, 0.0);
    for (int j = 1; j < m - 1; ++j)
        grad[j] = prof[j + 1] - prof[j - 1];
    // 两端无法做中心差分，镜像填充，避免窗口边界被当成边缘
    grad[0] = grad[1];
    grad[m - 1] = grad[m - 2];

    // ④ 均值滤波（半宽 filter_half_width）；两步都是线性卷积，顺序不影响结果
    std::vector<double> sm(m, 0.0);
    if (filterHalfWidth < 1) {
        sm = grad;
    } else {
        const int hw = filterHalfWidth;
        for (int j = 0; j < m; ++j) {
            int a = j - hw; if (a < 0) a = 0;
            int b = j + hw; if (b > m - 1) b = m - 1;
            double sum = 0.0;
            for (int k = a; k <= b; ++k) sum += grad[k];
            sm[j] = sum / (double)(b - a + 1);
        }
    }

    // 窗口两端（可视化用，未命中也写）
    out.px1 = cw.sx;
    out.py1 = cw.sy;
    out.px2 = cw.sx + cw.nx * len;
    out.py2 = cw.sy + cw.ny * len;
    out.x = 0.0;
    out.y = 0.0;
    out.strength = 0.0;
    out.valid = 0;

    // ⑤ 极性过滤 + 选边
    const double thr = (threshold > 0.0) ? threshold : 0.0;
    int best = -1;
    double bestMag = 0.0;

    for (int j = 0; j < m; ++j) {
        const double d = sm[j];
        bool ok = false;
        switch (polarity) {
            case VA_POLARITY_DARK_TO_LIGHT: ok = (d >= thr); break;
            case VA_POLARITY_LIGHT_TO_DARK: ok = (-d >= thr); break;
            default:                        ok = (std::fabs(d) >= thr); break;
        }
        if (!ok) continue;

        if (selectMode == VA_SELECT_FIRST) { best = j; break; }
        if (selectMode == VA_SELECT_LAST)  { best = j; continue; }

        const double mag = std::fabs(d);
        if (best < 0 || mag > bestMag) { best = j; bestMag = mag; }
    }

    if (best < 0) return false;

    // ⑥ 抛物线插值求亚像素：对极大/极小值同样适用（顶点公式与符号无关）
    double delta = 0.0;
    if (best > 0 && best < m - 1) {
        const double f0 = sm[best - 1];
        const double f1 = sm[best];
        const double f2 = sm[best + 1];
        const double den = f0 - 2.0 * f1 + f2;
        if (std::fabs(den) > 1e-12) {
            delta = 0.5 * (f0 - f2) / den;
            if (delta > 0.5) delta = 0.5;
            if (delta < -0.5) delta = -0.5;
        }
    }

    const double t = step * ((double)best + delta);
    out.x = cw.sx + cw.nx * t;
    out.y = cw.sy + cw.ny * t;
    out.strength = std::fabs(sm[best]);
    out.valid = 1;
    return true;
}

// ---------------------------------------------------------------------------
// ⑦ 直线：总最小二乘（最小化正交距离平方）。
//     解 = 去均值后协方差矩阵 [[sxx,sxy],[sxy,syy]] 的主特征方向，
//     直线必过质心 (mx, my)。等价于 cv::fitLine(DIST_L2)，但不依赖其深度约束。
// ---------------------------------------------------------------------------
bool FitLineTls(const std::vector<cv::Point2d>& pts,
                double& vx, double& vy, double& cx, double& cy)
{
    const size_t n = pts.size();
    if (n < 2) return false;

    double mx = 0.0, my = 0.0;
    for (size_t i = 0; i < n; ++i) { mx += pts[i].x; my += pts[i].y; }
    mx /= (double)n;
    my /= (double)n;

    double sxx = 0.0, sxy = 0.0, syy = 0.0;
    for (size_t i = 0; i < n; ++i) {
        const double u = pts[i].x - mx;
        const double v = pts[i].y - my;
        sxx += u * u;
        sxy += u * v;
        syy += v * v;
    }
    if (sxx + syy <= 1e-12) return false;   // 全部重合，方向无定义

    const double theta = 0.5 * std::atan2(2.0 * sxy, sxx - syy);
    vx = std::cos(theta);
    vy = std::sin(theta);
    cx = mx;
    cy = my;
    return std::isfinite(vx) && std::isfinite(vy);
}

// 点到直线的垂距（方向已归一化）
double PointLineDist(const cv::Point2d& p, double vx, double vy, double cx, double cy)
{
    const double ux = p.x - cx;
    const double uy = p.y - cy;
    return std::fabs(-ux * vy + uy * vx);
}

// ⑧ 直线度残差 RMS
double LineRms(const std::vector<cv::Point2d>& pts, double vx, double vy, double cx, double cy)
{
    if (pts.empty()) return 0.0;
    double sum = 0.0;
    for (size_t i = 0; i < pts.size(); ++i) {
        const double d = PointLineDist(pts[i], vx, vy, cx, cy);
        sum += d * d;
    }
    return std::sqrt(sum / (double)pts.size());
}

// ---------------------------------------------------------------------------
// ⑦ 圆：代数最小二乘。解 x²+y² = A·x + B·y + C（先去均值保证数值稳定），
//     再还原 center=(A/2, B/2)、R=√(C+(A²+B²)/4)。用 SVD 解以应对近退化。
// ---------------------------------------------------------------------------
bool FitCircleAls(const std::vector<cv::Point2d>& pts,
                  double& ox, double& oy, double& radius)
{
    const size_t n = pts.size();
    if (n < 3) return false;

    double mx = 0.0, my = 0.0;
    for (size_t i = 0; i < n; ++i) { mx += pts[i].x; my += pts[i].y; }
    mx /= (double)n;
    my /= (double)n;

    double suu = 0.0, suv = 0.0, svv = 0.0;
    double su = 0.0, sv = 0.0;
    double sur = 0.0, svr = 0.0, sr = 0.0;

    for (size_t i = 0; i < n; ++i) {
        const double u = pts[i].x - mx;
        const double v = pts[i].y - my;
        const double r2 = u * u + v * v;
        suu += u * u;
        suv += u * v;
        svv += v * v;
        su += u;
        sv += v;
        sur += u * r2;
        svr += v * r2;
        sr += r2;
    }

    cv::Mat A(3, 3, CV_64F);
    cv::Mat B(3, 1, CV_64F);
    cv::Mat X(3, 1, CV_64F);

    A.at<double>(0, 0) = suu; A.at<double>(0, 1) = suv; A.at<double>(0, 2) = su;
    A.at<double>(1, 0) = suv; A.at<double>(1, 1) = svv; A.at<double>(1, 2) = sv;
    A.at<double>(2, 0) = su;  A.at<double>(2, 1) = sv;  A.at<double>(2, 2) = (double)n;

    B.at<double>(0, 0) = sur;
    B.at<double>(1, 0) = svr;
    B.at<double>(2, 0) = sr;

    if (!cv::solve(A, B, X, cv::DECOMP_SVD)) return false;

    const double a = X.at<double>(0, 0);
    const double b = X.at<double>(1, 0);
    const double c = X.at<double>(2, 0);
    if (!std::isfinite(a) || !std::isfinite(b) || !std::isfinite(c)) return false;

    const double r2 = c + (a * a + b * b) * 0.25;
    if (!std::isfinite(r2) || r2 <= 0.0) return false;

    ox = mx + a * 0.5;
    oy = my + b * 0.5;
    radius = std::sqrt(r2);

    return std::isfinite(ox) && std::isfinite(oy) && std::isfinite(radius) && radius > 0.0;
}

// ⑧ 圆度残差 RMS：|点到圆心距离 - R| 的均方根
double CircleRms(const std::vector<cv::Point2d>& pts, double cx, double cy, double r)
{
    if (pts.empty()) return 0.0;
    double sum = 0.0;
    for (size_t i = 0; i < pts.size(); ++i) {
        const double dx = pts[i].x - cx;
        const double dy = pts[i].y - cy;
        const double d = std::sqrt(dx * dx + dy * dy) - r;
        sum += d * d;
    }
    return std::sqrt(sum / (double)pts.size());
}

// ⑪ 统一的「有效边缘点不足」错误文本，C# 侧直接取来作摘要
int ReportNoEdge(Context* ctx, int found, int required)
{
    std::ostringstream oss;
    oss << "有效边缘点 " << found << " < " << required;
    SetError(ctx, oss.str().c_str());
    return VA_ERR_NO_EDGE;
}

} // namespace (anonymous)

// ---------------------------------------------------------------------------
// 直线卡尺
// ---------------------------------------------------------------------------
int LineCaliper(Context* ctx,
                const VA_Image* gray,
                const VA_CaliperParam* param,
                VA_CaliperPoint* out_points,
                int max_count,
                int* out_count,
                VA_Line* out_line,
                double* out_rms)
{
    // ① 输入校验（先写安全默认值，任何失败路径上输出都是确定的）
    if (out_count != 0) *out_count = 0;
    if (out_rms != 0) *out_rms = 0.0;
    if (out_line != 0) { out_line->x1 = 0.0; out_line->y1 = 0.0; out_line->x2 = 0.0; out_line->y2 = 0.0; }

    if (gray == 0 || param == 0 || out_points == 0 || out_count == 0 || out_line == 0 || out_rms == 0) {
        return SetErrorEx(ctx, VA_ERR_ARG, "卡尺", "image / param / out",
                          "存在空指针", "全部必填",
                          "卡尺：入参为空（图像 / 参数 / 输出缓冲区必须全部提供）");
    }

    const int n = param->caliper_count;
    if (n < 1)
    {
        char val[32], msg[160];
        std::snprintf(val, sizeof(val), "%d", n);
        std::snprintf(msg, sizeof(msg), "卡尺：卡尺个数必须 >= 1（当前 %d）", n);
        return SetErrorEx(ctx, VA_ERR_PARAM_RANGE, "卡尺", "Spec.Count", val, ">= 1", msg);
    }
    if (max_count < n)
    {
        char val[32], msg[192];
        std::snprintf(val, sizeof(val), "%d", max_count);
        std::snprintf(msg, sizeof(msg),
                      "卡尺：输出缓冲区太小（容量 %d < 卡尺个数 %d），请把容量按卡尺个数分配", max_count, n);
        return SetErrorEx(ctx, VA_ERR_BUFFER_TOO_SMALL, "卡尺", "max_count", val, ">= caliper_count", msg);
    }

    try
    {
        cv::Mat g;
        if (!ToMat(gray, g, ctx->lastError)) return VA_ERR_ARG;
        if (g.channels() != 1) {
            SetErrorEx(ctx, VA_ERR_CHANNEL, "卡尺/直线", "图像通道数", "彩色图", "单通道灰度",
                        "直线卡尺：只接受单通道灰度图（上游请先接「预处理」转灰度）");
            return VA_ERR_TYPE;
        }

        const double dx = param->x2 - param->x1;
        const double dy = param->y2 - param->y1;
        const double segLen = std::sqrt(dx * dx + dy * dy);
        if (segLen < 1e-6)
        {
            SetErrorEx(ctx, VA_ERR_SCAN_DEGENERATE, "卡尺/直线", "起点/终点", "两点重合", "两点距离 > 0",
                        "直线卡尺：扫描线退化（起点与终点重合），请检查 X/Y 起点与终点参数");
            return VA_ERR_ARG;
        }

        // ② 等分投放：法线 = 扫描方向顺时针旋转 90°（图像 y 轴向下）
        const double ux = dx / segLen;
        const double uy = dy / segLen;
        const double nx = -uy;
        const double ny = ux;

        const double projLen = (param->projection_length > 0.0) ? param->projection_length : 20.0;

        std::vector<cv::Point2d> pts;
        pts.reserve((size_t)n);

        for (int i = 0; i < n; ++i) {
            const double f = ((double)i + 0.5) / (double)n;
            const double px = param->x1 + dx * f;
            const double py = param->y1 + dy * f;

            CaliperWindow cw;
            cw.sx = px - nx * projLen * 0.5;
            cw.sy = py - ny * projLen * 0.5;
            cw.nx = nx;
            cw.ny = ny;
            cw.length = projLen;

            VA_CaliperPoint op;
            op.px1 = 0.0; op.py1 = 0.0;
            op.px2 = 0.0; op.py2 = 0.0;
            op.x = 0.0; op.y = 0.0;
            op.strength = 0.0; op.valid = 0;

            const bool hit = FindEdgeOnProfile(g, cw,
                                               param->filter_half_width,
                                               param->polarity,
                                               param->select_mode,
                                               param->edge_threshold,
                                               op);
            out_points[i] = op;               // 未命中也写入，供可视化区分
            if (hit) pts.push_back(cv::Point2d(op.x, op.y));
        }

        *out_count = n;

        const int minForFit = 2;
        int required = (param->min_edge_count > 0) ? param->min_edge_count : minForFit;
        if (required < minForFit) required = minForFit;

        if ((int)pts.size() < required)
            return ReportNoEdge(ctx, (int)pts.size(), required);

        // ⑦ 拟合 + ⑧ 残差
        double vx = 0.0, vy = 0.0, cx = 0.0, cy = 0.0;
        if (!FitLineTls(pts, vx, vy, cx, cy)) {
            SetErrorEx(ctx, VA_ERR_FIT_DEGENERATE, "卡尺/直线", "拟合点集", "点几乎全部重合", "至少 2 个不同位置的点",
                        "直线卡尺：拟合失败（有效边缘点几乎全部重合，方向无定义）；请调低边缘阈值或放宽卡尺间距");
            return VA_ERR_INTERNAL;
        }
        double rms = LineRms(pts, vx, vy, cx, cy);

        // ⑨ 离群剔除一轮（只在确实剔除了点、且剩余点仍满足下限时才重新拟合）
        const double tol = param->outlier_tolerance;
        if (param->reject_outliers != 0 && tol > 0.0 && (int)pts.size() > required) {
            std::vector<cv::Point2d> cand;
            cand.reserve(pts.size());
            for (size_t i = 0; i < pts.size(); ++i) {
                if (PointLineDist(pts[i], vx, vy, cx, cy) <= tol)
                    cand.push_back(pts[i]);
            }
            if ((int)cand.size() >= required && (int)cand.size() != (int)pts.size()) {
                double v2x = 0.0, v2y = 0.0, c2x = 0.0, c2y = 0.0;
                if (FitLineTls(cand, v2x, v2y, c2x, c2y)) {
                    pts.swap(cand);
                    vx = v2x; vy = v2y; cx = c2x; cy = c2y;
                    rms = LineRms(pts, vx, vy, cx, cy);
                }
            }
        }

        // ⑩ 端点：把扫描线段两端投影到拟合直线上。
        //    等价于「参与点范围向两侧各外推半个卡尺间距」，消除 (i+0.5)/N 投放
        //    带来的 1/N 系统性偏短，使量测长度贴合 ROI 定义的量测跨度。
        const double t1 = (param->x1 - cx) * vx + (param->y1 - cy) * vy;
        const double t2 = (param->x2 - cx) * vx + (param->y2 - cy) * vy;
        const double ta = (t1 < t2) ? t1 : t2;
        const double tb = (t1 < t2) ? t2 : t1;

        out_line->x1 = cx + vx * ta;
        out_line->y1 = cy + vy * ta;
        out_line->x2 = cx + vx * tb;
        out_line->y2 = cy + vy * tb;

        // 角度由 C# 侧按 atan2(dy,dx) 归一化到 (-90°, 90°]，原生层保持精简
        *out_rms = rms;
        return VA_OK;
    }
    catch (const cv::Exception& e) { SetError(ctx, e); return VA_ERR_INTERNAL; }
    catch (const std::exception& e) { SetError(ctx, e); return VA_ERR_INTERNAL; }
}

// ---------------------------------------------------------------------------
// 圆卡尺
// ---------------------------------------------------------------------------
int CircleCaliper(Context* ctx,
                  const VA_Image* gray,
                  const VA_CaliperParam* param,
                  VA_CaliperPoint* out_points,
                  int max_count,
                  int* out_count,
                  VA_Circle* out_circle,
                  double* out_rms)
{
    if (out_count != 0) *out_count = 0;
    if (out_rms != 0) *out_rms = 0.0;
    if (out_circle != 0) { out_circle->cx = 0.0; out_circle->cy = 0.0; out_circle->radius = 0.0; out_circle->residual = 0.0; }

    if (gray == 0 || param == 0 || out_points == 0 || out_count == 0 || out_circle == 0 || out_rms == 0) {
        return SetErrorEx(ctx, VA_ERR_ARG, "卡尺", "image / param / out",
                          "存在空指针", "全部必填",
                          "卡尺：入参为空（图像 / 参数 / 输出缓冲区必须全部提供）");
    }

    const int n = param->caliper_count;
    if (n < 1)
    {
        char val[32], msg[160];
        std::snprintf(val, sizeof(val), "%d", n);
        std::snprintf(msg, sizeof(msg), "卡尺：卡尺个数必须 >= 1（当前 %d）", n);
        return SetErrorEx(ctx, VA_ERR_PARAM_RANGE, "卡尺", "Spec.Count", val, ">= 1", msg);
    }
    if (max_count < n)
    {
        char val[32], msg[192];
        std::snprintf(val, sizeof(val), "%d", max_count);
        std::snprintf(msg, sizeof(msg),
                      "卡尺：输出缓冲区太小（容量 %d < 卡尺个数 %d），请把容量按卡尺个数分配", max_count, n);
        return SetErrorEx(ctx, VA_ERR_BUFFER_TOO_SMALL, "卡尺", "max_count", val, ">= caliper_count", msg);
    }

    try
    {
        cv::Mat g;
        if (!ToMat(gray, g, ctx->lastError)) return VA_ERR_ARG;
        if (g.channels() != 1) {
            SetErrorEx(ctx, VA_ERR_CHANNEL, "卡尺/圆", "图像通道数", "彩色图", "单通道灰度",
                        "圆卡尺：只接受单通道灰度图（上游请先接「预处理」转灰度）");
            return VA_ERR_TYPE;
        }

        // ② 角度跨度：>360 截断为 360；<=0 视为整圆
        double span = param->angle_span;
        if (span > 360.0) span = 360.0;
        if (span <= 0.0) span = 360.0;

        // 径向投影窗口：内径 → 外径；未给外径时退化为 内径 + projection_length
        const double projLen = (param->projection_length > 0.0) ? param->projection_length : 20.0;
        double r0 = param->inner_radius;
        double r1 = param->outer_radius;
        if (r1 <= r0) r1 = r0 + projLen;
        if (r0 < 0.0) { r1 = r1 - r0; r0 = 0.0; }
        double winLen = r1 - r0;
        if (!(winLen > 0.0)) { r1 = r0 + projLen; winLen = projLen; }

        std::vector<cv::Point2d> pts;
        pts.reserve((size_t)n);

        for (int i = 0; i < n; ++i) {
            const double f = ((double)i + 0.5) / (double)n;
            const double theta = (param->start_angle + span * f) * kPi / 180.0;
            const double ct = std::cos(theta);
            const double st = std::sin(theta);

            CaliperWindow cw;
            cw.sx = param->cx + ct * r0;
            cw.sy = param->cy + st * r0;
            cw.nx = ct;                 // 径向朝外
            cw.ny = st;
            cw.length = winLen;

            VA_CaliperPoint op;
            op.px1 = 0.0; op.py1 = 0.0;
            op.px2 = 0.0; op.py2 = 0.0;
            op.x = 0.0; op.y = 0.0;
            op.strength = 0.0; op.valid = 0;

            const bool hit = FindEdgeOnProfile(g, cw,
                                               param->filter_half_width,
                                               param->polarity,
                                               param->select_mode,
                                               param->edge_threshold,
                                               op);
            out_points[i] = op;
            if (hit) pts.push_back(cv::Point2d(op.x, op.y));
        }

        *out_count = n;

        const int minForFit = 3;
        int required = (param->min_edge_count > 0) ? param->min_edge_count : minForFit;
        if (required < minForFit) required = minForFit;

        if ((int)pts.size() < required)
            return ReportNoEdge(ctx, (int)pts.size(), required);

        // ⑦ 拟合 + ⑧ 残差
        double cx = 0.0, cy = 0.0, r = 0.0;
        if (!FitCircleAls(pts, cx, cy, r)) {
            SetErrorEx(ctx, VA_ERR_FIT_DEGENERATE, "卡尺/圆", "拟合点集", "点共线或集中在小段圆弧", "角度跨度覆盖 > 90°",
                        "圆卡尺：拟合失败（有效边缘点共线或集中于同一小段圆弧）；请增大角度跨度或放宽半径");
            return VA_ERR_INTERNAL;
        }
        double rms = CircleRms(pts, cx, cy, r);

        // ⑨ 离群剔除一轮
        const double tol = param->outlier_tolerance;
        if (param->reject_outliers != 0 && tol > 0.0 && (int)pts.size() > required) {
            std::vector<cv::Point2d> cand;
            cand.reserve(pts.size());
            for (size_t i = 0; i < pts.size(); ++i) {
                const double dx = pts[i].x - cx;
                const double dy = pts[i].y - cy;
                const double d = std::fabs(std::sqrt(dx * dx + dy * dy) - r);
                if (d <= tol) cand.push_back(pts[i]);
            }
            if ((int)cand.size() >= required && (int)cand.size() != (int)pts.size()) {
                double c2x = 0.0, c2y = 0.0, r2 = 0.0;
                if (FitCircleAls(cand, c2x, c2y, r2)) {
                    pts.swap(cand);
                    cx = c2x; cy = c2y; r = r2;
                    rms = CircleRms(pts, cx, cy, r);
                }
            }
        }

        // ⑩ 圆心 / 半径；residual 回填圆度 RMS（与 out_rms 同值）
        out_circle->cx = cx;
        out_circle->cy = cy;
        out_circle->radius = r;
        out_circle->residual = rms;

        // 直径由 C# 侧按 Radius*2 计算，原生层保持精简
        *out_rms = rms;
        return VA_OK;
    }
    catch (const cv::Exception& e) { SetError(ctx, e); return VA_ERR_INTERNAL; }
    catch (const std::exception& e) { SetError(ctx, e); return VA_ERR_INTERNAL; }
}

} // namespace va
