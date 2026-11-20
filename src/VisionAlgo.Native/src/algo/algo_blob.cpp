// 连通域分析
#include "va_internal.h"
#include "va_error.h"

namespace va {

int BlobAnalyze(Context* ctx, const VA_Image* gray, double minArea, double maxArea,
                VA_Blob* outBlobs, int maxCount, int* outCount)
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

        int written = 0;
        for (int i = 1; i < n && written < maxCount; ++i) { // 0 号是背景
            double area = (double)stats.at<int>(i, cv::CC_STAT_AREA);
            if (area < minArea) continue;
            if (maxArea > 0.0 && area > maxArea) continue;

            VA_Blob b;
            b.cx     = centroids.at<double>(i, 0);
            b.cy     = centroids.at<double>(i, 1);
            b.area   = area;
            b.width  = (double)stats.at<int>(i, cv::CC_STAT_WIDTH);
            b.height = (double)stats.at<int>(i, cv::CC_STAT_HEIGHT);
            b.angle  = 0.0;

            outBlobs[written++] = b;
        }

        *outCount = written;
        return VA_OK;
    }
    catch (const cv::Exception& e) { SetError(ctx, e); return VA_ERR_INTERNAL; }
    catch (const std::exception& e) { SetError(ctx, e); return VA_ERR_INTERNAL; }
}

} // namespace va
