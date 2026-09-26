// 模板训练与匹配（归一化相关系数）
#include "va_internal.h"
#include "va_error.h"

#include <algorithm>

namespace va {

int TrainTemplate(Context* ctx, const VA_Image* gray, const int* roi)
{
    if (gray == 0) { SetError(ctx, "null argument"); return VA_ERR_ARG; }

    try
    {
        cv::Mat g;
        if (!ToMat(gray, g, ctx->lastError)) return VA_ERR_ARG;

        cv::Mat grayImg = AsGray(g);

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

int MatchTemplate(Context* ctx, const VA_Image* gray, VA_MatchResult* out)
{
    if (gray == 0 || out == 0) { SetError(ctx, "null argument"); return VA_ERR_ARG; }
    if (!ctx->hasTemplate || ctx->tmpl.empty()) {
        SetError(ctx, "template not trained");
        return VA_ERR_NO_TEMPLATE;
    }

    try
    {
        cv::Mat g;
        if (!ToMat(gray, g, ctx->lastError)) return VA_ERR_ARG;

        cv::Mat grayImg = AsGray(g);

        if (ctx->tmpl.cols > grayImg.cols || ctx->tmpl.rows > grayImg.rows) {
            SetError(ctx, "template larger than input image");
            return VA_ERR_SIZE;
        }

        cv::Mat result;
        cv::matchTemplate(grayImg, ctx->tmpl, result, cv::TM_CCOEFF_NORMED);

        double minVal = 0, maxVal = 0;
        cv::Point minLoc, maxLoc;
        cv::minMaxLoc(result, &minVal, &maxVal, &minLoc, &maxLoc);

        out->x      = (double)maxLoc.x;
        out->y      = (double)maxLoc.y;
        out->width  = (double)ctx->tmpl.cols;
        out->height = (double)ctx->tmpl.rows;
        out->score  = maxVal;
        out->angle  = 0.0; // 当前实现不支持旋转匹配
        return VA_OK;
    }
    catch (const cv::Exception& e) { SetError(ctx, e); return VA_ERR_INTERNAL; }
    catch (const std::exception& e) { SetError(ctx, e); return VA_ERR_INTERNAL; }
}

} // namespace va
