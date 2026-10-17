// 图像滤波：均值 / 高斯 / 中值 / 双边 / USM 锐化 / 自定义 3x3 核，后接可选形态学。
// 支持 1 通道与 3 通道输入，输出通道数与输入保持一致。
#include "va_internal.h"
#include "va_error.h"

#include <vector>

namespace va {

namespace {

// 按形状取结构化元素
cv::Mat MakeKernel(int shape, int k)
{
    int s = (shape == VA_MORPH_SHAPE_ELLIPSE) ? (int)cv::MORPH_ELLIPSE
          : (shape == VA_MORPH_SHAPE_CROSS)   ? (int)cv::MORPH_CROSS
                                              : (int)cv::MORPH_RECT;
    return cv::getStructuringElement(s, cv::Size(k, k));
}

// morph_op -> cv::MorphTypes；返回 <0 表示不做形态学
int MorphType(int op)
{
    switch (op) {
        case VA_MORPH_DILATE:   return (int)cv::MORPH_DILATE;
        case VA_MORPH_ERODE:    return (int)cv::MORPH_ERODE;
        case VA_MORPH_OPEN:     return (int)cv::MORPH_OPEN;
        case VA_MORPH_CLOSE:    return (int)cv::MORPH_CLOSE;
        case VA_MORPH_GRADIENT: return (int)cv::MORPH_GRADIENT;
        case VA_MORPH_TOPHAT:   return (int)cv::MORPH_TOPHAT;
        case VA_MORPH_BLACKHAT: return (int)cv::MORPH_BLACKHAT;
        default:                return -1;
    }
}

// 中值滤波安全封装。
// cv::medianBlur 对多通道输入只保证小核（ksize <= 5）全深度可用，大核路径仅支持
// CV_8U 且不接受子矩阵（带 stride 填充的 Mat 就是子矩阵）。这里统一先取连续副本，
// 大核多通道再拆成单通道逐个处理，避免直接抛异常。
void MedianBlurSafe(const cv::Mat& src, cv::Mat& dst, int k)
{
    cv::Mat cont = src.isContinuous() ? src : src.clone();

    if (cont.channels() == 1 || k <= 5) {
        cv::medianBlur(cont, dst, k);
        return;
    }

    std::vector<cv::Mat> ch;
    cv::split(cont, ch);
    for (size_t i = 0; i < ch.size(); ++i) cv::medianBlur(ch[i], ch[i], k);
    cv::merge(ch, dst);
}

// USM 锐化：out = src * (1 + amount) - blur * amount
void UnsharpMask(const cv::Mat& src, cv::Mat& dst, int k, double sigma, double amount)
{
    cv::Mat blur;
    cv::GaussianBlur(src, blur, cv::Size(k, k), sigma > 0.0 ? sigma : 0.0);
    cv::addWeighted(src, 1.0 + amount, blur, -amount, 0.0, dst);
}

} // namespace

int Filter(Context* ctx, const VA_Image* src, VA_Image* dst, const VA_FilterParam* param)
{
    if (src == 0 || dst == 0 || param == 0) { SetError(ctx, "null argument"); return VA_ERR_ARG; }

    try
    {
        cv::Mat m;
        if (!ToMat(src, m, ctx->lastError)) return VA_ERR_ARG;
        if (m.channels() != 1 && m.channels() != 3) {
            SetError(ctx, "filter supports 1 or 3 channels only");
            return VA_ERR_TYPE;
        }

        int k = OddAtLeast3(param->ksize);
        double sigma = param->sigma;

        cv::Mat out;
        switch (param->type)
        {
        case VA_FILTER_NONE:
            out = m.clone();
            break;

        case VA_FILTER_BOX:
            cv::blur(m, out, cv::Size(k, k));
            break;

        case VA_FILTER_GAUSS:
            // sigma <= 0 时 OpenCV 会按 0.3*((k-1)*0.5-1)+0.8 自动推导
            cv::GaussianBlur(m, out, cv::Size(k, k), sigma > 0.0 ? sigma : 0.0);
            break;

        case VA_FILTER_MEDIAN:
            MedianBlurSafe(m, out, k);
            break;

        case VA_FILTER_BILATERAL: {
            double sc = (sigma > 0.0) ? sigma : 75.0;
            cv::bilateralFilter(m, out, k, sc, sc);
            break;
        }

        case VA_FILTER_USM: {
            int bk = (param->ksize >= 3) ? k : 5;
            UnsharpMask(m, out, bk, sigma, param->amount);
            break;
        }

        case VA_FILTER_CUSTOM: {
            // 固定 3x3 锐化核，接口上不引入自定义系数指针
            cv::Mat kernel = (cv::Mat_<float>(3, 3) <<
                              0.0f, -1.0f,  0.0f,
                             -1.0f,  5.0f, -1.0f,
                              0.0f, -1.0f,  0.0f);
            cv::filter2D(m, out, -1, kernel);
            break;
        }

        default:
            SetError(ctx, "unknown filter type");
            return VA_ERR_ARG;
        }

        // 形态学后处理
        int op = MorphType(param->morph_op);
        if (op >= 0 && param->morph_ksize >= 3) {
            int mk = OddAtLeast3(param->morph_ksize);
            int it = (param->iterations < 1) ? 1 : param->iterations;
            cv::Mat kernel = MakeKernel(param->morph_shape, mk);
            cv::morphologyEx(out, out, op, kernel, cv::Point(-1, -1), it);
        }

        if (!CopyOut(out, dst, ctx->lastError)) return VA_ERR_SIZE;
        return VA_OK;
    }
    catch (const cv::Exception& e) { SetError(ctx, e); return VA_ERR_INTERNAL; }
    catch (const std::exception& e) { SetError(ctx, e); return VA_ERR_INTERNAL; }
}

} // namespace va
