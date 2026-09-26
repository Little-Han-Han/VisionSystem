// ============================================================================
//  导出层：只负责句柄校验与转发。
//  具体算法实现在 src/algo/ 下按类别分文件，公共工具在 src/common/。
//  对外接口的契约见 include/ 下各头文件。
// ============================================================================

#include "vision_algo.h"

#include "va_internal.h"

#include <new>
#include <cstring>

extern "C" {

/* 统一的句柄取用：空句柄直接返回 VA_ERR_HANDLE */
#define VA_CTX()                                        \
    va::Context* ctx = static_cast<va::Context*>(handle); \
    if (ctx == 0) return VA_ERR_HANDLE;

// ---------------------- 生命周期 ----------------------

int VA_CALL va_get_version(void)
{
    return 10000 + 455; // v1.00，基于 OpenCV 4.5.5
}

void* VA_CALL va_create(void)
{
    return new (std::nothrow) va::Context();
}

void VA_CALL va_destroy(void* handle)
{
    delete static_cast<va::Context*>(handle);
}

int VA_CALL va_get_last_error(void* handle, char* buffer, int size)
{
    if (handle == 0 || buffer == 0 || size <= 0) return 0;

    va::Context* ctx = static_cast<va::Context*>(handle);
    const std::string& s = ctx->lastError;

    int n = (int)s.size();
    if (n > size - 1) n = size - 1;
    if (n > 0) std::memcpy(buffer, s.c_str(), (size_t)n);
    buffer[n] = '\0';
    return n;
}

const char* VA_CALL va_error_string(int code)
{
    switch (code) {
        case VA_OK:              return "OK";
        case VA_ERR_HANDLE:      return "invalid handle";
        case VA_ERR_ARG:         return "invalid argument";
        case VA_ERR_SIZE:        return "size mismatch";
        case VA_ERR_TYPE:        return "unsupported image type";
        case VA_ERR_INTERNAL:    return "internal error";
        case VA_ERR_NO_TEMPLATE: return "template not trained";
        default:                 return "unknown error";
    }
}

// ---------------------- 算法分发 ----------------------

int VA_CALL va_preprocess(void* handle,
                          const VA_Image* src,
                          VA_Image* dst,
                          const VA_PreprocessParam* param)
{
    VA_CTX();
    return va::Preprocess(ctx, src, dst, param);
}

int VA_CALL va_blob_analyze(void* handle,
                            const VA_Image* gray,
                            double min_area,
                            double max_area,
                            VA_Blob* out_blobs,
                            int max_count,
                            int* out_count)
{
    VA_CTX();
    return va::BlobAnalyze(ctx, gray, min_area, max_area, out_blobs, max_count, out_count);
}

int VA_CALL va_sharpness(void* handle, const VA_Image* gray, double* out_score)
{
    VA_CTX();
    return va::Sharpness(ctx, gray, out_score);
}

int VA_CALL va_fit_circle(void* handle, const VA_Image* gray, VA_Circle* out)
{
    VA_CTX();
    return va::FitCircle(ctx, gray, out);
}

int VA_CALL va_fit_line(void* handle, const VA_Image* gray, VA_Line* out)
{
    VA_CTX();
    return va::FitLine(ctx, gray, out);
}

int VA_CALL va_train_template(void* handle, const VA_Image* gray, const int* roi)
{
    VA_CTX();
    return va::TrainTemplate(ctx, gray, roi);
}

int VA_CALL va_match_template(void* handle, const VA_Image* gray, VA_MatchResult* out)
{
    VA_CTX();
    return va::MatchTemplate(ctx, gray, out);
}

int VA_CALL va_edge_count(void* handle,
                          const VA_Image* gray,
                          int scan_row,
                          int threshold_delta,
                          int* out_count)
{
    VA_CTX();
    return va::EdgeCount(ctx, gray, scan_row, threshold_delta, out_count);
}

} // extern "C"
