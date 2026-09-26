// 图像质量评价与简单测量
#include "va_internal.h"
#include "va_error.h"

#include <cstdlib>
#include <cmath>

namespace va {

int Sharpness(Context* ctx, const VA_Image* gray, double* outScore)
{
    if (gray == 0 || outScore == 0) { SetError(ctx, "null argument"); return VA_ERR_ARG; }

    try
    {
        cv::Mat g;
        if (!ToMat(gray, g, ctx->lastError)) return VA_ERR_ARG;
        if (g.channels() != 1) {
            SetError(ctx, "sharpness requires single channel");
            return VA_ERR_TYPE;
        }

        cv::Mat lap;
        cv::Laplacian(g, lap, CV_64F);

        cv::Scalar mean, stddev;
        cv::meanStdDev(lap, mean, stddev);

        *outScore = stddev.val[0] * stddev.val[0]; // 方差
        return VA_OK;
    }
    catch (const cv::Exception& e) { SetError(ctx, e); return VA_ERR_INTERNAL; }
    catch (const std::exception& e) { SetError(ctx, e); return VA_ERR_INTERNAL; }
}

int EdgeCount(Context* ctx, const VA_Image* gray, int scanRow, int delta, int* outCount)
{
    if (gray == 0 || outCount == 0) { SetError(ctx, "null argument"); return VA_ERR_ARG; }

    try
    {
        cv::Mat g;
        if (!ToMat(gray, g, ctx->lastError)) return VA_ERR_ARG;
        if (g.channels() != 1) {
            SetError(ctx, "edge count requires single channel");
            return VA_ERR_TYPE;
        }
        if (scanRow < 0 || scanRow >= g.rows) {
            SetError(ctx, "scan row out of range");
            return VA_ERR_ARG;
        }

        int d = (delta <= 0) ? 30 : delta;
        const unsigned char* row = g.ptr<unsigned char>(scanRow);

        int count = 0;
        int prev = -1;
        for (int x = 0; x < g.cols; ++x) {
            int v = row[x];
            if (prev >= 0 && std::abs(v - prev) > d) ++count;
            prev = v;
        }

        *outCount = count;
        return VA_OK;
    }
    catch (const cv::Exception& e) { SetError(ctx, e); return VA_ERR_INTERNAL; }
    catch (const std::exception& e) { SetError(ctx, e); return VA_ERR_INTERNAL; }
}

} // namespace va
