// 公共工具实现：图像结构转换、灰度化、二值化、轮廓提取、错误记录。
#include "va_internal.h"

#include <cstring>
#include <cmath>
#include <chrono>

namespace va {

// ---------------------------------------------------------------------------
//  节点级软超时
//  【thread_local 而不是全局】宿主是"每条流程一个线程"，若用全局量，
//  A 流程设了 500ms 会让正在跑的 B 流程也在 500ms 处被砍 —— 两条流程互相干扰。
// ---------------------------------------------------------------------------
namespace {

struct DeadlineState
{
    long long untilMs;   // steady_clock 毫秒，0 = 不限
    unsigned  calls;     // 节流计数：每 8 次检查才读一次时钟
};

thread_local DeadlineState g_deadline = { 0, 0 };

inline long long NowMs()
{
    return (long long)std::chrono::duration_cast<std::chrono::milliseconds>(
        std::chrono::steady_clock::now().time_since_epoch()).count();
}

} // namespace

void DeadlineSet(int ms)
{
    g_deadline.calls = 0;
    g_deadline.untilMs = (ms > 0) ? (NowMs() + (long long)ms) : 0;
}

int DeadlineRemainMs()
{
    if (g_deadline.untilMs == 0) return -1;
    long long left = g_deadline.untilMs - NowMs();
    return left > 0 ? (int)left : 0;
}

bool DeadlineHit()
{
    if (g_deadline.untilMs == 0) return false;          // 未设超时：零成本快速返回
    if ((++g_deadline.calls & 7u) != 0u) return false;  // 每 8 次才真读时钟
    return NowMs() >= g_deadline.untilMs;
}

// 全局并行预算：0 = 用 OpenCV / OpenVINO 自己的默认（吃满所有核）
int g_vaThreads = 0;

void ApplyThreadBudget(ov::Core& core)
{
    if (g_vaThreads <= 0) return;

    // set_property 对不支持的属性会抛，不能让它影响模型加载
    try {
        core.set_property("CPU", ov::inference_num_threads(g_vaThreads));
        // 每个流程一条 infer stream：多条 stream 会各自再开一份线程，等于把预算乘起来
        core.set_property("CPU", ov::num_streams(1));
    } catch (...) {
        // 老版本 OpenVINO 或属性名变化：退化成库默认，不影响功能
    }
}

void SetError(Context* ctx, const char* msg)
{
    if (ctx == 0) return;
    ctx->lastError = (msg != 0) ? msg : "";
    // 结构化字段一并清掉：否则上层可能读到"上一条错误的模块名 + 这一条的消息"这种拼错的组合
    ctx->lastCode = VA_OK;
    ctx->lastModule.clear();
    ctx->lastParam.clear();
    ctx->lastValue.clear();
    ctx->lastExpect.clear();
}

int SetErrorEx(Context* ctx, int code, const char* module, const char* param,
               const char* value, const char* expect, const char* message)
{
    if (ctx == 0) return code;

    ctx->lastCode = code;
    ctx->lastModule = (module != 0) ? module : "";
    ctx->lastParam  = (param  != 0) ? param  : "";
    ctx->lastValue  = (value  != 0) ? value  : "";
    ctx->lastExpect = (expect != 0) ? expect : "";

    if (message != 0)
    {
        ctx->lastError = message;
        return code;
    }

    // 没给整句就按结构化字段拼一句，保证 lastError 永远不会是空串
    std::string m = ctx->lastModule;
    if (!ctx->lastParam.empty())
    {
        if (!m.empty()) m += "：";
        m += ctx->lastParam;
        if (!ctx->lastValue.empty()) m += "=" + ctx->lastValue;
        if (!ctx->lastExpect.empty()) m += "（期望：" + ctx->lastExpect + "）";
    }
    if (m.empty()) m = "算法调用失败（未提供详情）";
    ctx->lastError = m;
    return code;
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

bool LargestContour(const cv::Mat& gray, std::vector<cv::Point>& out, std::string& err,
                    bool preBinarized)
{
    cv::Mat bin = preBinarized ? gray : Binarize(gray);

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
