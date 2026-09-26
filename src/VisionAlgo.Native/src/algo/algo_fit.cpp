// 几何拟合：对最大轮廓做圆 / 直线拟合
#include "va_internal.h"
#include "va_error.h"

#include <cfloat>
#include <cmath>

namespace va {

int FitCircle(Context* ctx, const VA_Image* gray, VA_Circle* out)
{
    if (gray == 0 || out == 0) { SetError(ctx, "null argument"); return VA_ERR_ARG; }

    try
    {
        cv::Mat g;
        if (!ToMat(gray, g, ctx->lastError)) return VA_ERR_ARG;
        if (g.channels() != 1) {
            SetError(ctx, "fit circle requires single channel");
            return VA_ERR_TYPE;
        }

        std::vector<cv::Point> contour;
        if (!LargestContour(g, contour, ctx->lastError)) return VA_ERR_INTERNAL;

        cv::Point2f center;
        float radius = 0.0f;
        cv::minEnclosingCircle(contour, center, radius);

        // 残差：轮廓点到圆周距离的平均绝对偏差
        double sum = 0.0;
        for (size_t i = 0; i < contour.size(); ++i) {
            double dx = contour[i].x - center.x;
            double dy = contour[i].y - center.y;
            sum += std::fabs(std::sqrt(dx * dx + dy * dy) - radius);
        }

        out->cx = center.x;
        out->cy = center.y;
        out->radius = radius;
        out->residual = contour.empty() ? 0.0 : sum / (double)contour.size();
        return VA_OK;
    }
    catch (const cv::Exception& e) { SetError(ctx, e); return VA_ERR_INTERNAL; }
    catch (const std::exception& e) { SetError(ctx, e); return VA_ERR_INTERNAL; }
}

int FitLine(Context* ctx, const VA_Image* gray, VA_Line* out)
{
    if (gray == 0 || out == 0) { SetError(ctx, "null argument"); return VA_ERR_ARG; }

    try
    {
        cv::Mat g;
        if (!ToMat(gray, g, ctx->lastError)) return VA_ERR_ARG;
        if (g.channels() != 1) {
            SetError(ctx, "fit line requires single channel");
            return VA_ERR_TYPE;
        }

        std::vector<cv::Point> contour;
        if (!LargestContour(g, contour, ctx->lastError)) return VA_ERR_INTERNAL;

        cv::Vec4f line;
        cv::fitLine(contour, line, cv::DIST_L2, 0, 0.01, 0.01);

        // line = (vx, vy, x0, y0)
        double vx = line[0], vy = line[1];
        double x0 = line[2], y0 = line[3];

        // 把轮廓点投影到直线上，取两端作为线段端点
        double tmin = DBL_MAX, tmax = -DBL_MAX;
        for (size_t i = 0; i < contour.size(); ++i) {
            double t = (contour[i].x - x0) * vx + (contour[i].y - y0) * vy;
            if (t < tmin) tmin = t;
            if (t > tmax) tmax = t;
        }

        out->x1 = x0 + vx * tmin;
        out->y1 = y0 + vy * tmin;
        out->x2 = x0 + vx * tmax;
        out->y2 = y0 + vy * tmax;
        return VA_OK;
    }
    catch (const cv::Exception& e) { SetError(ctx, e); return VA_ERR_INTERNAL; }
    catch (const std::exception& e) { SetError(ctx, e); return VA_ERR_INTERNAL; }
}

} // namespace va
