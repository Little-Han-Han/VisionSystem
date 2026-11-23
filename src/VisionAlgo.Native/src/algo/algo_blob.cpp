// 连通域分析（Blob）：面积过滤 + 形状特征 + 可选标签图
#include "va_internal.h"
#include "va_error.h"

#include <algorithm>
#include <cmath>
#include <cstring>
#include <vector>

namespace va {

namespace {

/* 在连通域的轴对齐 bbox 内取轮廓，算形状特征。
   【为什么只算 bbox 内】labels==i 的全图比较在 2M 像素 × 50 个连通域时会到 100M 次，
   现场直接掉帧；bbox 内比较既准又快（轮廓不会超出 bbox）。 */
void FillShapeFeatures(const cv::Mat& labels, int labelId,
                       int bboxX, int bboxY, int bboxW, int bboxH, VA_Blob& b)
{
    double area = b.area;

    cv::Rect roi(bboxX, bboxY, bboxW, bboxH);
    roi &= cv::Rect(0, 0, labels.cols, labels.rows);
    if (roi.width <= 0 || roi.height <= 0) return;

    cv::Mat sub = labels(roi);
    cv::Mat mask = (sub == labelId);                 // CV_8U，0/255

    std::vector<std::vector<cv::Point> > contours;
    cv::findContours(mask, contours, cv::RETR_EXTERNAL, cv::CHAIN_APPROX_SIMPLE);

    double perimeter = 0.0;
    std::vector<cv::Point> all;
    for (size_t i = 0; i < contours.size(); ++i) {
        perimeter += cv::arcLength(contours[i], true);
        all.insert(all.end(), contours[i].begin(), contours[i].end());
    }
    if (all.empty()) return;

    // 轮廓点相对 bbox，minAreaRect 结果需加回偏移才能与图像坐标一致
    cv::RotatedRect rr = cv::minAreaRect(all);
    rr.center.x += roi.x;
    rr.center.y += roi.y;

    double w1 = rr.size.width;
    double h1 = rr.size.height;
    double longSide  = std::max(w1, h1);
    double shortSide = std::min(w1, h1);
    double rectArea  = longSide * shortSide;

    double hullArea = area;
    std::vector<cv::Point> hull;
    cv::convexHull(all, hull);
    if (!hull.empty()) hullArea = cv::contourArea(hull);

    const double eps = 1e-6;
    b.perimeter      = perimeter;
    b.roundness      = perimeter > eps ? std::min(1.0, 4.0 * CV_PI * area / (perimeter * perimeter)) : 0.0;
    b.rectangularity = rectArea  > eps ? std::min(1.0, area / rectArea) : 0.0;
    b.solidity       = hullArea  > eps ? std::min(1.0, area / hullArea) : 0.0;
    b.compactness    = area > eps ? (perimeter * perimeter) / (4.0 * CV_PI * area) : 0.0;
    b.aspect_ratio   = shortSide > eps ? longSide / shortSide : 0.0;

    double bboxArea = (double)bboxW * bboxH;
    b.fill_rate = bboxArea > eps ? std::min(1.0, area / bboxArea) : 0.0;

    b.min_cx = rr.center.x;
    b.min_cy = rr.center.y;
    b.min_w  = longSide;
    b.min_h  = shortSide;
    b.angle  = rr.angle;      // 兼容原字段：改为真实的最小外接矩形角度
}

} // namespace

int BlobAnalyzeEx(Context* ctx, const VA_Image* gray, double minArea, double maxArea,
                  VA_Blob* outBlobs, int maxCount, int* outCount, int* outLabels)
{
    if (gray == 0 || outBlobs == 0 || outCount == 0 || maxCount <= 0) {
        return SetErrorEx(ctx, VA_ERR_ARG, "Blob", "image / out / max_count",
                          "存在空指针或 max_count<=0", "全部必填且 max_count>=1",
                          "Blob：入参为空（图像 / 输出缓冲区 / 容量必须全部提供，且容量 >= 1）");
    }

    *outCount = 0;

    try
    {
        cv::Mat g;
        if (!ToMat(gray, g, ctx->lastError)) return VA_ERR_ARG;
        if (g.channels() != 1) {
            SetErrorEx(ctx, VA_ERR_CHANNEL, "Blob", "图像通道数", "彩色图", "单通道灰度",
                        "Blob：只接受单通道灰度图（上游请先接「预处理」转灰度）");
            return VA_ERR_TYPE;
        }

        cv::Mat bin = Binarize(g);

        cv::Mat labels, stats, centroids;
        int n = cv::connectedComponentsWithStats(bin, labels, stats, centroids);

        std::vector<int> keptLabel;          // outBlobs[i] 对应的原始连通域编号
        keptLabel.reserve((size_t)maxCount);

        int written = 0;
        for (int i = 1; i < n && written < maxCount; ++i) { // 0 号是背景
            double area = (double)stats.at<int>(i, cv::CC_STAT_AREA);
            if (area < minArea) continue;
            if (maxArea > 0.0 && area > maxArea) continue;

            int bx = stats.at<int>(i, cv::CC_STAT_LEFT);
            int by = stats.at<int>(i, cv::CC_STAT_TOP);
            int bw = stats.at<int>(i, cv::CC_STAT_WIDTH);
            int bh = stats.at<int>(i, cv::CC_STAT_HEIGHT);

            VA_Blob b;
            memset(&b, 0, sizeof(b));
            b.cx     = centroids.at<double>(i, 0);
            b.cy     = centroids.at<double>(i, 1);
            b.area   = area;
            b.width  = (double)bw;
            b.height = (double)bh;

            FillShapeFeatures(labels, i, bx, by, bw, bh, b);

            outBlobs[written] = b;
            keptLabel.push_back(i);
            ++written;
        }

        *outCount = written;

        // 标签图：重映射成 1..written（其余像素 0），供界面逐连通域上色
        if (outLabels != 0 && written > 0) {
            std::vector<int> remap((size_t)n, 0);
            for (int k = 0; k < written; ++k) remap[(size_t)keptLabel[k]] = k + 1;

            for (int y = 0; y < labels.rows; ++y) {
                const int* src = labels.ptr<int>(y);
                int* dst = outLabels + (size_t)y * labels.cols;
                for (int x = 0; x < labels.cols; ++x) dst[x] = remap[(size_t)src[x]];
            }
        }

        return VA_OK;
    }
    catch (const cv::Exception& e) { SetError(ctx, e); return VA_ERR_INTERNAL; }
    catch (const std::exception& e) { SetError(ctx, e); return VA_ERR_INTERNAL; }
}

int BlobAnalyze(Context* ctx, const VA_Image* gray, double minArea, double maxArea,
                VA_Blob* outBlobs, int maxCount, int* outCount)
{
    return BlobAnalyzeEx(ctx, gray, minArea, maxArea, outBlobs, maxCount, outCount, 0);
}

} // namespace va
