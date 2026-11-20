// 预处理：灰度化 → 高斯模糊 → 二值化 → 形态学
#include "va_internal.h"
#include "va_error.h"

namespace va {

int Preprocess(Context* ctx, const VA_Image* src, VA_Image* dst,
               const VA_PreprocessParam* param)
{
    if (src == 0 || dst == 0 || param == 0)
        return SetErrorEx(ctx, VA_ERR_ARG, "预处理", "src / dst / param", "存在空指针", "全部必填",
                          "预处理：入参为空（输入图 / 输出图 / 参数必须全部提供）");

    try
    {
        cv::Mat input;
        if (!ToMat(src, input, ctx->lastError)) return VA_ERR_ARG;

        cv::Mat gray = AsGray(input);

        if (param->blur_ksize >= 3) {
            int k = OddAtLeast3(param->blur_ksize);
            cv::GaussianBlur(gray, gray, cv::Size(k, k), 0);
        }

        if (param->threshold >= 0) {
            int type = param->invert ? cv::THRESH_BINARY_INV : cv::THRESH_BINARY;
            cv::threshold(gray, gray, (double)param->threshold, 255.0, type);
        }

        if (param->morph_op != 0 && param->morph_ksize >= 3) {
            int k = OddAtLeast3(param->morph_ksize);
            cv::Mat kernel = cv::getStructuringElement(cv::MORPH_RECT, cv::Size(k, k));
            int op = (param->morph_op == 1) ? cv::MORPH_OPEN : cv::MORPH_CLOSE;
            cv::morphologyEx(gray, gray, op, kernel);
        }

        if (!CopyOut(gray, dst, ctx->lastError)) return VA_ERR_SIZE;
        return VA_OK;
    }
    catch (const cv::Exception& e) { SetError(ctx, e); return VA_ERR_INTERNAL; }
    catch (const std::exception& e) { SetError(ctx, e); return VA_ERR_INTERNAL; }
}

} // namespace va
