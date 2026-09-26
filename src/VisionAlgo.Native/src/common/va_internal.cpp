// 公共工具实现：图像结构转换、灰度化、二值化、轮廓提取、错误记录。
#include "common/va_internal.h"

#include <cstring>
#include <cmath>

namespace va {

void SetError(Context* ctx, const char* msg)
{
    if (ctx != 0) ctx->lastError = msg;
}

void SetError(Context* ctx, const std::exception& e)
{
    if (ctx != 0) ctx->lastError = e.what();
}

int OddAtLeast3(int v)
{
    int k = (v < 3) ? 3 : v;
    return (k % 2 == 0) ? k + 1 : k;
}

bool ToMat(const VA_Image* img, cv::Mat& out, std::string& err)
{
    if (img == 0) { err = "VA_Image is null"; return false; }
    if (img->data == 0) { err = "image data is null"; return false; }
    if (img->width <= 0 || img->height <= 0) { err = "invalid image size"; return false; }

    int type;
    switch (img->channels) {
        case 1:  type = CV_8UC1; break;
        case 3:  type = CV_8UC3; break;
        case 4:  type = CV_8UC4; break;
        default: err = "unsupported channel count (need 1, 3 or 4)"; return false;
    }

    size_t minStride = (size_t)img->width * (size_t)img->channels;
    size_t stride = (img->stride > 0) ? (size_t)img->stride : minStride;
    if (stride < minStride) { err = "stride smaller than one row"; return false; }

    out = cv::Mat(img->height, img->width, type, img->data, stride);
    return true;
}

bool CopyOut(const cv::Mat& src, VA_Image* dst, std::string& err)
{
    if (dst == 0 || dst->data == 0) { err = "dst buffer is null"; return false; }
    if (dst->width != src.cols || dst->height != src.rows) { err = "dst size mismatch"; return false; }
    if (dst->channels != src.channels()) { err = "dst channel count mismatch"; return false; }

    size_t rowBytes = (size_t)src.cols * src.elemSize();
    size_t stride = (dst->stride > 0) ? (size_t)dst->stride : rowBytes;
    if (stride < rowBytes) { err = "dst stride too small"; return false; }

    for (int y = 0; y < src.rows; ++y) {
        std::memcpy(dst->data + (size_t)y * stride, src.ptr(y), rowBytes);
    }
    return true;
}

cv::Mat AsGray(const cv::Mat& input)
{
    if (input.channels() == 1) return input.clone();

    cv::Mat gray;
    if (input.channels() == 4)
        cv::cvtColor(input, gray, cv::COLOR_BGRA2GRAY);
    else
        cv::cvtColor(input, gray, cv::COLOR_BGR2GRAY);
    return gray;
}

cv::Mat Binarize(const cv::Mat& gray)
{
    cv::Mat bin;
    cv::threshold(gray, bin, 128, 255, cv::THRESH_BINARY);
    return bin;
}

bool LargestContour(const cv::Mat& gray, std::vector<cv::Point>& out, std::string& err)
{
    cv::Mat bin = Binarize(gray);

    std::vector<std::vector<cv::Point> > contours;
    cv::findContours(bin, contours, cv::RETR_EXTERNAL, cv::CHAIN_APPROX_SIMPLE);

    if (contours.empty()) { err = "no contour found"; return false; }

    size_t best = 0;
    double bestArea = 0.0;
    for (size_t i = 0; i < contours.size(); ++i) {
        double a = cv::contourArea(contours[i]);
        if (a > bestArea) { bestArea = a; best = i; }
    }

    out = contours[best];
    return true;
}

} // namespace va
