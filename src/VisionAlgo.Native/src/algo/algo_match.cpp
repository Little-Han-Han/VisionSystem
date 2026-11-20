// ============================================================================
//  模板匹配（NCC，灰度）：平移 + 角度 + 缩放，金字塔由粗到细。
//
//  和形状匹配的区别（别混用）：
//    · 形状匹配看**边缘梯度方向**，对光照/局部遮挡稳；
//    · 这里看**灰度相关系数**（TM_CCOEFF_NORMED），对纹理敏感的工件更准，
//      但对光照变化、遮挡敏感。
//
//  为什么这样实现：
//    1) 旋转必然产生 padding。若直接对「旋转外接框」做 TM_CCOEFF_NORMED，
//       padding 会让分母里的图像方差虚高，分数被系统性压低（10° 约 -5%~-15%），
//       于是**大角度的正确姿态会被小角度的错误姿态挤掉**。所以：
//         · 粗筛用填充了模板均值的近似分数（快，只用来排序，并按 √validFrac 补偿偏置）；
//         · 对外 score 一律由 ExactNcc（只统计有效像素）重算 —— 分数与角度无关、可比阈值。
//    2) 角度/缩放是**姿态**，位置是**位置**。姿态数 = 角度档 × 缩放档，由用户参数决定；
//       位置由 matchTemplate 一次全扫给出，不在上层写位置循环。
//    3) 只有 1 个姿态时强制走 1 层金字塔 —— 与旧实现（只平移）**逐位一致**，
//       既保证存量配方不受影响，也保证「角度=0/缩放=1」这条最常见路径不被金字塔拖慢精度。
// ============================================================================
#include "va_internal.h"
#include "va_error.h"

#include <algorithm>
#include <cmath>
#include <cstring>
#include <vector>

namespace va {

namespace {

const double kPi = 3.14159265358979323846;

// 一个姿态（角度 + 缩放）下的模板：旋转缩放后的图 + 有效像素掩码。
struct WarpedTemplate {
    cv::Mat img;        // 8U，尺寸 = 旋转外接框
    cv::Mat mask;       // 8U 0/255，非 padding 的有效像素
    double  validFrac;  // 有效像素占比（0~1）
};

// 候选（位置统一用 level-0 坐标，跟层无关）
struct Cand {
    double cx, cy;      // 匹配框中心
    double angle;
    double scale;
    double aw, ah;      // 该姿态下的外接框宽高（level-0）
    double score;
};

bool BuildWarped(const cv::Mat& tpl, double angleDeg, double scale, double fill,
                 WarpedTemplate& out)
{
    out.validFrac = 0.0;
    if (tpl.empty() || !(scale > 0.0)) return false;

    const double a = angleDeg * kPi / 180.0;
    const double c = std::fabs(std::cos(a));
    const double s = std::fabs(std::sin(a));

    const int ow = (int)std::lround((double)tpl.cols * scale * c + (double)tpl.rows * scale * s);
    const int oh = (int)std::lround((double)tpl.cols * scale * s + (double)tpl.rows * scale * c);
    if (ow < 2 || oh < 2) return false;

    // getRotationMatrix2D 的角度是「逆时针为正」，而本库的 angle 口径是图像坐标下
    // 「顺时针为正」（与形状匹配、RotatedRectShape 一致）→ 这里取负号对齐。
    cv::Mat M = cv::getRotationMatrix2D(cv::Point2f(tpl.cols * 0.5f, tpl.rows * 0.5f),
                                        -angleDeg, scale);
    M.at<double>(0, 2) += ow * 0.5 - tpl.cols * 0.5;
    M.at<double>(1, 2) += oh * 0.5 - tpl.rows * 0.5;

    // padding 填模板均值：TM_CCOEFF 会先减均值，填充区因此既不贡献分子、也不贡献
    // 模板能量，只剩分母里那点图像方差会把分数拉低 —— 这部分误差由 ExactNcc 兜底。
    cv::warpAffine(tpl, out.img, M, cv::Size(ow, oh), cv::INTER_LINEAR,
                   cv::BORDER_CONSTANT, cv::Scalar(fill));

    cv::Mat ones(tpl.size(), CV_8U, cv::Scalar(255));
    cv::warpAffine(ones, out.mask, M, cv::Size(ow, oh), cv::INTER_NEAREST,
                   cv::BORDER_CONSTANT, cv::Scalar(0));

    const int valid = cv::countNonZero(out.mask);
    if (valid < 16) return false;
    out.validFrac = (double)valid / ((double)ow * (double)oh);
    return true;
}

// 去掉 padding 的精确 NCC（单点，O(外接框面积)）。返回 [-1,1]；无效返回 -2。
// 与 TM_CCOEFF_NORMED 同一个公式，只是求和范围限定在 mask 内。
double ExactNcc(const cv::Mat& img, const WarpedTemplate& w, int x, int y)
{
    if (w.img.empty()) return -2.0;
    const int ww = w.img.cols, wh = w.img.rows;
    if (x < 0 || y < 0 || x + ww > img.cols || y + wh > img.rows) return -2.0;

    double n = 0.0, st = 0.0, st2 = 0.0, si = 0.0, si2 = 0.0, sti = 0.0;
    for (int r = 0; r < wh; ++r)
    {
        const unsigned char* pT = w.img.ptr<unsigned char>(r);
        const unsigned char* pM = w.mask.ptr<unsigned char>(r);
        const unsigned char* pI = img.ptr<unsigned char>(y + r) + x;
        for (int cc = 0; cc < ww; ++cc)
        {
            if (pM[cc] == 0) continue;
            const double t = (double)pT[cc];
            const double v = (double)pI[cc];
            n   += 1.0;
            st  += t;
            st2 += t * t;
            si  += v;
            si2 += v * v;
            sti += t * v;
        }
    }
    if (n < 4.0) return -2.0;

    const double num = sti - st * si / n;
    const double dT  = st2 - st * st / n;
    const double dI  = si2 - si * si / n;
    if (dT <= 1e-9 || dI <= 1e-9) return -2.0;   // 全平（模板或图内窗口无方差）

    double v = num / std::sqrt(dT * dI);
    if (v < -1.0) v = -1.0;
    if (v > 1.0) v = 1.0;
    return v;
}

// 角度序列：区间反了交换；步长 <=0 按 1°；末尾不足一步补齐
void BuildAngles(const VA_MatchParam& p, std::vector<double>& out)
{
    double a0 = p.angle_start;
    double a1 = p.angle_end;
    if (a1 < a0) { double t = a0; a0 = a1; a1 = t; }

    double step = p.angle_step;
    if (!(step > 0.0)) step = 1.0;

    int count = (int)std::floor((a1 - a0) / step + 1e-9) + 1;
    if (count < 1) count = 1;
    if (count > 3600) count = 3600;
    for (int i = 0; i < count; ++i) out.push_back(a0 + i * step);
    if (out.back() < a1 - 1e-9) out.push_back(a1);
}

// 缩放序列：min/max 反了交换；步长 <=0 表示只搜一个点
void BuildScales(const VA_MatchParam& p, std::vector<double>& out)
{
    double s0 = p.scale_min;
    if (!(s0 > 0.0)) s0 = 1.0;
    double s1 = p.scale_max;
    if (!(s1 > 0.0)) s1 = s0;
    if (s1 < s0) { double t = s0; s0 = s1; s1 = t; }

    double step = p.scale_step;
    if (!(step > 0.0) || (s1 - s0) < 1e-9) { out.push_back(s0); return; }

    int count = (int)std::floor((s1 - s0) / step + 1e-9) + 1;
    if (count < 1) count = 1;
    if (count > 64) count = 64;
    for (int i = 0; i < count; ++i) out.push_back(s0 + i * step);
    if (out.back() < s1 - 1e-9) out.push_back(s1);
}

} // namespace

// ---------------------------------------------------------------------------

int TrainTemplate(Context* ctx, const VA_Image* gray, const int* roi)
{
    if (gray == 0) { SetError(ctx, "null argument"); return VA_ERR_ARG; }

    try
    {
        cv::Mat g;
        if (!ToMat(gray, g, ctx->lastError)) return VA_ERR_ARG;

        // 单通道直接用引用：下面本来就要对 ROI 做一次 clone，
        // AsGray 在这里会白白多拷一整幅图（12MP 单色图 = 12 MB）。
        cv::Mat grayImg = (g.channels() == 1) ? g : AsGray(g);

        cv::Rect r(0, 0, grayImg.cols, grayImg.rows);
        if (roi != 0) {
            int x = std::max(0, roi[0]);
            int y = std::max(0, roi[1]);
            int w = std::max(1, roi[2]);
            int h = std::max(1, roi[3]);

            r = cv::Rect(x, y, w, h) & cv::Rect(0, 0, grayImg.cols, grayImg.rows);
            if (r.width <= 0 || r.height <= 0) { SetError(ctx, "invalid roi"); return VA_ERR_ARG; }
        }

        ctx->tmpl = grayImg(r).clone();
        ctx->hasTemplate = true;
        return VA_OK;
    }
    catch (const cv::Exception& e) { SetError(ctx, e); return VA_ERR_INTERNAL; }
    catch (const std::exception& e) { SetError(ctx, e); return VA_ERR_INTERNAL; }
}

int MatchTemplateEx(Context* ctx, const VA_Image* gray, const VA_MatchParam* param,
                    VA_MatchResult* out, int maxCount, int* outCount)
{
    if (outCount != 0) *outCount = 0;
    if (gray == 0 || out == 0 || outCount == 0 || maxCount <= 0) {
        SetError(ctx, "null argument");
        return VA_ERR_ARG;
    }
    if (!ctx->hasTemplate || ctx->tmpl.empty()) {
        SetError(ctx, "template not trained");
        return VA_ERR_NO_TEMPLATE;
    }

    try
    {
        cv::Mat g;
        if (!ToMat(gray, g, ctx->lastError)) return VA_ERR_ARG;

        // 只读；单通道不拷贝
        cv::Mat img = (g.channels() == 1) ? g : AsGray(g);

        if (ctx->tmpl.cols > img.cols || ctx->tmpl.rows > img.rows) {
            SetError(ctx, "template larger than input image");
            return VA_ERR_SIZE;
        }

        VA_MatchParam p;
        std::memset(&p, 0, sizeof(p));
        if (param != 0) p = *param;

        const int maxMatches = (p.max_matches > 0) ? p.max_matches : 1;
        double minScore = p.min_score;
        if (!(minScore > 0.0)) minScore = 0.0;
        if (minScore > 1.0) minScore = 1.0;

        double maxOverlap = p.max_overlap;
        if (!(maxOverlap > 0.0)) maxOverlap = 0.5;
        if (maxOverlap > 1.0) maxOverlap = 1.0;

        std::vector<double> angles;
        std::vector<double> scales;
        BuildAngles(p, angles);
        BuildScales(p, scales);
        const size_t nPoses = angles.size() * scales.size();

        // ---------- 金字塔层数 ----------
        // 单姿态强制 1 层：这条路径必须与旧实现（只平移）逐位一致。
        int levels = p.pyramid_levels;
        if (levels < 1) levels = 1;
        if (levels > 4) levels = 4;
        if (nPoses <= 1) levels = 1;

        while (levels > 1)
        {
            const int lw = img.cols >> (levels - 1);
            const int lh = img.rows >> (levels - 1);
            const int tw = ctx->tmpl.cols >> (levels - 1);
            const int th = ctx->tmpl.rows >> (levels - 1);
            if (lw >= 32 && lh >= 32 && tw >= 8 && th >= 8) break;
            --levels;
        }

        std::vector<cv::Mat> pyr(levels);
        pyr[0] = img;
        for (int L = 1; L < levels; ++L) cv::pyrDown(pyr[L - 1], pyr[L]);

        // 各层的模板（与图像金字塔同比例缩小；角度/缩放留到 BuildWarped 里做）
        std::vector<cv::Mat> tpls(levels);
        std::vector<double>   fills(levels, 0.0);
        tpls[0] = ctx->tmpl;
        for (int L = 1; L < levels; ++L)
        {
            cv::resize(ctx->tmpl, tpls[L],
                       cv::Size(std::max(1, ctx->tmpl.cols >> L),
                                std::max(1, ctx->tmpl.rows >> L)),
                       0, 0, cv::INTER_AREA);
        }
        for (int L = 0; L < levels; ++L) fills[L] = cv::mean(tpls[L])[0];

        // ---------- 粗层：全部姿态各扫一遍 ----------
        const int L0 = levels - 1;
        const double f0 = 1.0 / (double)(1 << L0);
        const cv::Mat& imgL0 = pyr[L0];

        std::vector<Cand> cands;
        cands.reserve(nPoses * (size_t)maxMatches);

        for (size_t si = 0; si < scales.size(); ++si)
        {
            for (size_t ai = 0; ai < angles.size(); ++ai)
            {
                // 软超时：这里的开销 = 缩放下标 × 角度下标 × 一次整幅 matchTemplate，
                // 是 NCC 里唯一会跑爆的地方。到点直接跳出双层循环，转去收尾。
                if (DeadlineHit()) { si = scales.size(); break; }
                WarpedTemplate w;
                if (!BuildWarped(tpls[L0], angles[ai], scales[si], fills[L0], w)) continue;
                if (w.img.cols > imgL0.cols || w.img.rows > imgL0.rows) continue;

                cv::Mat res;
                cv::matchTemplate(imgL0, w.img, res, cv::TM_CCOEFF_NORMED);

                // 同一姿态可能有多个目标：取至多 maxMatches 个局部极大
                // （抑制窗取外接框的一半；跨姿态的去重交给最后的 NMS）
                const int supW = std::max(1, w.img.cols / 2);
                const int supH = std::max(1, w.img.rows / 2);
                // 旋转 padding 会把分数按 ≈√validFrac 压低，排序时补回去，
                // 否则大角度的正确姿态会被小角度的错误姿态挤掉。
                const double biasFix = 1.0 / std::sqrt(std::max(0.15, w.validFrac));

                cv::Mat work = res.clone();
                for (int k = 0; k < maxMatches; ++k)
                {
                    double mn = 0.0, mx = -1.0;
                    cv::Point mnLoc, mxLoc;
                    cv::minMaxLoc(work, &mn, &mx, &mnLoc, &mxLoc);
                    if (!(mx > 0.0)) break;

                    Cand c;
                    c.cx = ((double)mxLoc.x + w.img.cols * 0.5) / f0;
                    c.cy = ((double)mxLoc.y + w.img.rows * 0.5) / f0;
                    c.angle = angles[ai];
                    c.scale = scales[si];
                    c.aw = (double)w.img.cols / f0;
                    c.ah = (double)w.img.rows / f0;
                    c.score = mx * biasFix;          // 仅用于排序
                    cands.push_back(c);

                    cv::Rect sup(mxLoc.x - supW / 2, mxLoc.y - supH / 2, supW, supH);
                    sup &= cv::Rect(0, 0, work.cols, work.rows);
                    if (sup.width > 0 && sup.height > 0) work(sup).setTo(-1.0f);
                }
            }
        }

        if (DeadlineHit())
            return SetErrorEx(ctx, VA_ERR_ALGO_TIMEOUT, "模板匹配", "TimeoutMs",
                              "超时", "在超时前完成搜索",
                              "角度/缩放档位太多，超时前没扫完；请缩小搜索 ROI、加大角度/缩放步长，或调大「超时(ms)」");

        if (cands.empty())
        {
            *outCount = 0;
            return VA_OK;
        }

        // 只精化最有可能的一批：宁可多留几个，也不要在粗筛阶段就把真目标剪掉
        size_t keep = (size_t)std::max(4, maxMatches * 8);
        if (keep < 8) keep = 8;
        if (keep > 64) keep = 64;
        if (keep > cands.size()) keep = cands.size();
        std::partial_sort(cands.begin(), cands.begin() + keep, cands.end(),
                          [](const Cand& a, const Cand& b) { return a.score > b.score; });
        cands.resize(keep);

        // ---------- 逐层向下精化位置（角度/缩放沿用粗层结果）----------
        // 角度/缩放已经是用户步长的整数倍，不需要再细分；需要补的是位置精度
        // （粗层 1 px = 原图 2^L0 px）。
        for (int L = L0 - 1; L >= 0; --L)
        {
            const double f = 1.0 / (double)(1 << L);
            const cv::Mat& imgL = pyr[L];

            for (size_t ci = 0; ci < cands.size(); ++ci)
            {
                if (DeadlineHit()) break;       // 软超时：剩余候选不再精化
                Cand& c = cands[ci];

                WarpedTemplate w;
                if (!BuildWarped(tpls[L], c.angle, c.scale, fills[L], w)) continue;
                if (w.img.cols > imgL.cols || w.img.rows > imgL.rows) continue;

                const int rad = 2;   // 搜索半径：±2 个 level-L 像素
                int x0 = (int)std::lround(c.cx * f - w.img.cols * 0.5) - rad;
                int y0 = (int)std::lround(c.cy * f - w.img.rows * 0.5) - rad;
                int x1 = x0 + w.img.cols + 2 * rad;
                int y1 = y0 + w.img.rows + 2 * rad;

                // 贴边时整体平移窗口，保证「原来的位置」仍在窗口内
                if (x1 > imgL.cols) { const int d = x1 - imgL.cols; x0 -= d; x1 = imgL.cols; }
                if (y1 > imgL.rows) { const int d = y1 - imgL.rows; y0 -= d; y1 = imgL.rows; }
                if (x0 < 0) x0 = 0;
                if (y0 < 0) y0 = 0;
                if (x1 - x0 < w.img.cols || y1 - y0 < w.img.rows) continue;

                cv::Mat crop = imgL(cv::Rect(x0, y0, x1 - x0, y1 - y0));
                cv::Mat res;
                cv::matchTemplate(crop, w.img, res, cv::TM_CCOEFF_NORMED);

                double mn = 0.0, mx = -1.0;
                cv::Point mnLoc, mxLoc;
                cv::minMaxLoc(res, &mn, &mx, &mnLoc, &mxLoc);
                if (!(mx > 0.0)) continue;

                c.cx = ((double)(x0 + mxLoc.x) + w.img.cols * 0.5) / f;
                c.cy = ((double)(y0 + mxLoc.y) + w.img.rows * 0.5) / f;
                c.score = mx;
            }
        }

        // ---------- 最细层：精确打分（去 padding）+ 亚像素 + NMS ----------
        std::vector<Cand> final;
        final.reserve(cands.size());

        for (size_t ci = 0; ci < cands.size(); ++ci)
        {
            if (DeadlineHit()) break;           // 软超时：剩余候选不再重评
            Cand c = cands[ci];

            WarpedTemplate w;
            if (!BuildWarped(ctx->tmpl, c.angle, c.scale, fills[0], w)) continue;
            if (w.img.cols > img.cols || w.img.rows > img.rows) continue;

            int bx = (int)std::lround(c.cx - w.img.cols * 0.5);
            int by = (int)std::lround(c.cy - w.img.rows * 0.5);
            if (bx < 0) bx = 0;
            if (by < 0) by = 0;
            if (bx + w.img.cols > img.cols) bx = img.cols - w.img.cols;
            if (by + w.img.rows > img.rows) by = img.rows - w.img.rows;

            double subX = 0.0, subY = 0.0;

            // 亚像素：在最细层分数图的 3×3 邻域上做抛物线（与卡尺同一套顶点公式）
            if (p.subpixel != 0)
            {
                const int x0 = std::max(0, bx - 1);
                const int y0 = std::max(0, by - 1);
                const int x1 = std::min(img.cols, bx + w.img.cols + 1);
                const int y1 = std::min(img.rows, by + w.img.rows + 1);

                if (x1 - x0 >= w.img.cols && y1 - y0 >= w.img.rows)
                {
                    cv::Mat res;
                    cv::matchTemplate(img(cv::Rect(x0, y0, x1 - x0, y1 - y0)), w.img,
                                      res, cv::TM_CCOEFF_NORMED);
                    double mn = 0.0, mx = -1.0;
                    cv::Point mnLoc, mxLoc;
                    cv::minMaxLoc(res, &mn, &mx, &mnLoc, &mxLoc);
                    if (mx > 0.0)
                    {
                        bx = x0 + mxLoc.x;
                        by = y0 + mxLoc.y;

                        const int rx = mxLoc.x, ry = mxLoc.y;
                        if (rx > 0 && rx + 1 < res.cols)
                        {
                            const double f0v = res.at<float>(ry, rx - 1);
                            const double f1v = res.at<float>(ry, rx);
                            const double f2v = res.at<float>(ry, rx + 1);
                            const double den = f0v - 2.0 * f1v + f2v;
                            if (std::fabs(den) > 1e-12) {
                                subX = 0.5 * (f0v - f2v) / den;
                                if (subX > 0.5) subX = 0.5;
                                if (subX < -0.5) subX = -0.5;
                            }
                        }
                        if (ry > 0 && ry + 1 < res.rows)
                        {
                            const double f0v = res.at<float>(ry - 1, rx);
                            const double f1v = res.at<float>(ry, rx);
                            const double f2v = res.at<float>(ry + 1, rx);
                            const double den = f0v - 2.0 * f1v + f2v;
                            if (std::fabs(den) > 1e-12) {
                                subY = 0.5 * (f0v - f2v) / den;
                                if (subY > 0.5) subY = 0.5;
                                if (subY < -0.5) subY = -0.5;
                            }
                        }
                    }
                }
            }

            const double v = ExactNcc(img, w, bx, by);
            if (v <= -1.5) continue;       // 越界 / 全平窗口 → 该姿态无效

            c.score = (v < 0.0) ? 0.0 : v;
            c.cx = (double)bx + subX + w.img.cols * 0.5;
            c.cy = (double)by + subY + w.img.rows * 0.5;
            c.aw = (double)w.img.cols;
            c.ah = (double)w.img.rows;
            final.push_back(c);
        }

        std::sort(final.begin(), final.end(),
                  [](const Cand& a, const Cand& b) { return a.score > b.score; });

        // NMS：外接框交集 / 较小框面积 > max_overlap 视为同一目标
        std::vector<Cand> accepted;
        accepted.reserve((size_t)maxMatches);
        for (size_t i = 0; i < final.size(); ++i)
        {
            const Cand& c = final[i];
            if (minScore > 0.0 && c.score < minScore) break;

            bool dup = false;
            for (size_t j = 0; j < accepted.size(); ++j)
            {
                const Cand& a = accepted[j];
                const double ax0 = a.cx - a.aw * 0.5, ax1 = a.cx + a.aw * 0.5;
                const double ay0 = a.cy - a.ah * 0.5, ay1 = a.cy + a.ah * 0.5;
                const double bx0 = c.cx - c.aw * 0.5, bx1 = c.cx + c.aw * 0.5;
                const double by0 = c.cy - c.ah * 0.5, by1 = c.cy + c.ah * 0.5;

                const double ix = std::max(0.0, std::min(ax1, bx1) - std::max(ax0, bx0));
                const double iy = std::max(0.0, std::min(ay1, by1) - std::max(ay0, by0));
                const double inter = ix * iy;
                const double smaller = std::min(a.aw * a.ah, c.aw * c.ah);
                if (smaller > 0.0 && inter / smaller > maxOverlap) { dup = true; break; }
            }
            if (dup) continue;

            accepted.push_back(c);
            if ((int)accepted.size() >= maxMatches) break;
        }

        int n = (int)accepted.size();
        if (n > maxCount) n = maxCount;
        for (int i = 0; i < n; ++i)
        {
            const Cand& c = accepted[(size_t)i];
            out[i].cx = c.cx;
            out[i].cy = c.cy;
            out[i].width = c.aw;
            out[i].height = c.ah;
            out[i].x = c.cx - c.aw * 0.5;
            out[i].y = c.cy - c.ah * 0.5;
            out[i].score = c.score;
            out[i].angle = c.angle;
            out[i].scale = c.scale;
        }

        *outCount = n;
        return VA_OK;
    }
    catch (const cv::Exception& e) { SetError(ctx, e); return VA_ERR_INTERNAL; }
    catch (const std::exception& e) { SetError(ctx, e); return VA_ERR_INTERNAL; }
}

// 兼容旧接口：只平移、只要 1 个结果、不做亚像素（与旧实现逐位一致）。
int MatchTemplate(Context* ctx, const VA_Image* gray, VA_MatchResult* out)
{
    if (out == 0) { SetError(ctx, "null argument"); return VA_ERR_ARG; }

    VA_MatchParam p;
    std::memset(&p, 0, sizeof(p));
    p.angle_step = 1.0;
    p.scale_min = 1.0;
    p.scale_max = 1.0;
    p.pyramid_levels = 1;
    p.max_matches = 1;
    p.max_overlap = 0.5;
    p.subpixel = 0;

    int n = 0;
    return MatchTemplateEx(ctx, gray, &p, out, 1, &n);
}

} // namespace va
