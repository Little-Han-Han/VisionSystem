// 颜色提取：颜色空间阈值分割 → 形态学清理 → 连通域统计 → 面积过滤。
// 要求彩色输入（3 通道 BGR 或 4 通道 BGRA），是全库唯一需要彩色的算法。
#include "va_internal.h"
#include "va_error.h"

#include <cmath>
#include <vector>

namespace va {

namespace {

// 把 0~360 的色相换算到 OpenCV 的 0~179。
// 规则：只把「明显超出 0~179」的值当作 360 制输入除 2，其余原样保留，
// 这样上层按 0~179 直接给值（推荐做法）时不会被误伤。
int ToOpencvHue(int h)
{
    if (h > 179) return h / 2;
    if (h < 0)   return 0;
    return h;
}

int Clamp255(int v)
{
    return (v < 0) ? 0 : (v > 255 ? 255 : v);
}

// 在指定空间内做阈值分割，返回 0/255 单通道掩膜
cv::Mat MakeMask(const cv::Mat& bgr, const VA_ColorParam* p)
{
    if (p->space == VA_COLOR_SPACE_HSV) {
        cv::Mat hsv;
        cv::cvtColor(bgr, hsv, cv::COLOR_BGR2HSV);

        int h1 = ToOpencvHue(p->low[0]);
        int h2 = ToOpencvHue(p->high[0]);
        int s1 = Clamp255(p->low[1]);
        int s2 = Clamp255(p->high[1]);
        int v1 = Clamp255(p->low[2]);
        int v2 = Clamp255(p->high[2]);

        cv::Mat mask;
        if (h1 > h2) {
            // 色相跨 0°（典型场景：红色），拆成两段再取并集
            cv::Mat m1, m2;
            cv::inRange(hsv, cv::Scalar(h1, s1, v1), cv::Scalar(179, s2, v2), m1);
            cv::inRange(hsv, cv::Scalar(0,  s1, v1), cv::Scalar(h2,  s2, v2), m2);
            cv::bitwise_or(m1, m2, mask);
        } else {
            cv::inRange(hsv, cv::Scalar(h1, s1, v1), cv::Scalar(h2, s2, v2), mask);
        }
        return mask;
    }

    cv::Mat mask;
    cv::inRange(bgr,
                cv::Scalar(Clamp255(p->low[0]),  Clamp255(p->low[1]),  Clamp255(p->low[2])),
                cv::Scalar(Clamp255(p->high[0]), Clamp255(p->high[1]), Clamp255(p->high[2])),
                mask);
    return mask;
}

// 用二阶中心矩估计连通域主轴方向（度，0 = +X 轴，顺时针为正）。
// 输入是各 label 的原始累加量，n>0 时才有意义。
double OrientationFromSums(double n, double sx, double sy, double sxx, double syy, double sxy)
{
    if (n <= 0.0) return 0.0;

    double mx = sx / n;
    double my = sy / n;
    double mu20 = sxx / n - mx * mx;
    double mu02 = syy / n - my * my;
    double mu11 = sxy / n - mx * my;

    if (mu20 + mu02 <= 1e-12) return 0.0;
    return 0.5 * std::atan2(2.0 * mu11, mu20 - mu02) * 180.0 / CV_PI;
}

} // namespace

int ColorExtract(Context* ctx, const VA_Image* src, VA_Image* dstMask,
                 const VA_ColorParam* param, double minArea, double maxArea,
                 VA_Blob* outBlobs, int maxCount, int* outCount,
                 VA_ColorStat* outStat)
{
    if (src == 0 || param == 0) { SetError(ctx, "null argument"); return VA_ERR_ARG; }
    if (outCount != 0) *outCount = 0;

    try
    {
        cv::Mat m;
        if (!ToMat(src, m, ctx->lastError)) return VA_ERR_ARG;
        if (m.channels() != 3 && m.channels() != 4) {
            SetError(ctx, "color extract requires 3 (BGR) or 4 (BGRA) channels");
            return VA_ERR_TYPE;
        }

        // 统一到 3 通道 BGR，后续按 BGR 字节序处理
        cv::Mat bgr;
        if (m.channels() == 4)
            cv::cvtColor(m, bgr, cv::COLOR_BGRA2BGR);
        else
            bgr = m;

        cv::Mat mask = MakeMask(bgr, param);

        // 形态学清理
        if (param->morph_op != VA_COLOR_MORPH_NONE && param->morph_ksize >= 3) {
            int k = OddAtLeast3(param->morph_ksize);
            cv::Mat kernel = cv::getStructuringElement(cv::MORPH_RECT, cv::Size(k, k));
            int op = (param->morph_op == VA_COLOR_MORPH_CLOSE) ? cv::MORPH_CLOSE : cv::MORPH_OPEN;
            cv::morphologyEx(mask, mask, op, kernel);
        }

        // 统计信息（基于清理后的掩膜）
        if (outStat != 0) {
            double nz = (double)cv::countNonZero(mask);
            double total = (double)mask.rows * (double)mask.cols;
            cv::Scalar mean = cv::mean(bgr, mask);

            outStat->pixel_count = nz;
            outStat->ratio       = (total > 0.0) ? (nz / total) : 0.0;
            outStat->mean_b      = mean.val[0];
            outStat->mean_g      = mean.val[1];
            outStat->mean_r      = mean.val[2];
        }

        // 连通域统计
        cv::Mat labels, stats, centroids;
        int n = cv::connectedComponentsWithStats(mask, labels, stats, centroids);

        // 一次遍历累加各 label 的原始矩，避免对每个 blob 单独做全图掩膜
        std::vector<double> cnt(n, 0.0), sx(n, 0.0), sy(n, 0.0),
                            sxx(n, 0.0), syy(n, 0.0), sxy(n, 0.0);
        for (int y = 0; y < labels.rows; ++y) {
            const int* row = labels.ptr<int>(y);
            for (int x = 0; x < labels.cols; ++x) {
                int lb = row[x];
                if (lb <= 0) continue;
                double fx = (double)x, fy = (double)y;
                cnt[lb] += 1.0;
                sx[lb]  += fx;
                sy[lb]  += fy;
                sxx[lb] += fx * fx;
                syy[lb] += fy * fy;
                sxy[lb] += fx * fy;
            }
        }

        bool collect = (outBlobs != 0 && maxCount > 0);
        int written = 0;

        for (int i = 1; i < n; ++i) { // 0 号是背景
            double area = (double)stats.at<int>(i, cv::CC_STAT_AREA);
            if (area < minArea) continue;
            if (maxArea > 0.0 && area > maxArea) continue;

            if (!collect) continue;             // 只统计不返回列表
            if (written >= maxCount) break;

            VA_Blob b;
            b.cx     = centroids.at<double>(i, 0);
            b.cy     = centroids.at<double>(i, 1);
            b.area   = area;
            b.width  = (double)stats.at<int>(i, cv::CC_STAT_WIDTH);
            b.height = (double)stats.at<int>(i, cv::CC_STAT_HEIGHT);
            b.angle  = OrientationFromSums(cnt[i], sx[i], sy[i], sxx[i], syy[i], sxy[i]);

            outBlobs[written++] = b;
        }

        if (outCount != 0) *outCount = written;

        if (dstMask != 0) {
            if (!CopyOut(mask, dstMask, ctx->lastError)) return VA_ERR_SIZE;
        }

        return VA_OK;
    }
    catch (const cv::Exception& e) { SetError(ctx, e); return VA_ERR_INTERNAL; }
    catch (const std::exception& e) { SetError(ctx, e); return VA_ERR_INTERNAL; }
}

} // namespace va
