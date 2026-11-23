// ============================================================================
//  导出层：只负责句柄校验与转发。
//  具体算法实现在 src/algo/ 下按类别分文件，公共工具在 src/common/。
//  对外接口的契约见 include/ 下各头文件。
// ============================================================================

#include "vision_algo.h"

#include "va_internal.h"

#include <opencv2/core.hpp>

#ifdef _OPENMP
#include <omp.h>
#endif

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

int VA_CALL va_set_threads(int n)
{
    // n <= 0 → 关闭内部并行（只用一个核）；n > 0 → 上限 n。
    // 注意这是**进程级全局**设置：OpenCV 的 parallel_for 与 OpenVINO 的推理线程池共用这个预算。
    int want = n > 0 ? n : 0;

    va::g_vaThreads = want;       // 供 OpenVINO 侧（algo_dl / algo_ocrdl）在建 Core 时读取
    cv::setNumThreads(want);

    // 【关键】形状匹配用的是 `#pragma omp parallel for`（algo_shape.cpp），
    // 走的是 OpenMP 运行时，**不受 cv::setNumThreads 影响**。
    // 它的线程数默认按物理核数开满：实测单条流程跑 8 fps，OpenMP 8~11 个线程各烧满一个核，
    // 而算法本身只需要约 1 核 —— 全是并行开销。
    // omp_set_num_threads 是**按线程**生效的，所以宿主还必须在跑算法的那个线程里再调一次本函数。
#ifdef _OPENMP
    omp_set_dynamic(0);
    omp_set_num_threads(want > 0 ? want : 1);
#endif

    return cv::getNumThreads();
}

void VA_CALL va_set_deadline_ms(int ms)
{
    // 只影响**调用线程**：每条流程一个线程，互不干扰（见 va_lifecycle.h 的说明）。
    va::DeadlineSet(ms);
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

// 错误码 → 分类名。
//
// 【只给类别，不带原因】原因在 VA_ErrorInfo（模块 / 参数 / 实际值 / 期望）里，
// 由 va_get_error_info 单独取。这样"要不要处理"看 code，"具体哪里错了"看结构体，
// 不用再从一句拼接文本里猜。
const char* VA_CALL va_error_string(int code)
{
    switch (code) {
        case VA_OK:                     return "成功";

        // 通用 / 历史码
        case VA_ERR_HANDLE:             return "句柄无效";
        case VA_ERR_ARG:                return "参数非法";
        case VA_ERR_SIZE:               return "尺寸不匹配";
        case VA_ERR_TYPE:               return "图像类型不支持";
        case VA_ERR_INTERNAL:           return "算法内部异常";
        case VA_ERR_NO_TEMPLATE:        return "未训练模板";
        case VA_ERR_TIMEOUT:            return "读帧超时";
        case VA_ERR_CAMERA:             return "相机错误";
        case VA_ERR_NO_EDGE:            return "边缘点不足";

        // 输入 / 参数
        case VA_ERR_NO_INPUT:           return "无输入帧";
        case VA_ERR_ROI:                return "ROI 非法";
        case VA_ERR_IMAGE_TOO_SMALL:    return "图像太小";
        case VA_ERR_CHANNEL:            return "通道数不支持";
        case VA_ERR_PARAM_RANGE:        return "参数越界";
        case VA_ERR_BUFFER_TOO_SMALL:   return "输出缓冲区不足";

        // 模板 / 模型
        case VA_ERR_TEMPLATE_TOO_SMALL:  return "模板过小";
        case VA_ERR_TEMPLATE_NO_FEATURE: return "模板无特征";
        case VA_ERR_MODEL_NOT_LOADED:    return "模型未加载";
        case VA_ERR_MODEL_INFER:         return "推理失败";

        // 几何 / 测量
        case VA_ERR_SCAN_DEGENERATE:    return "扫描线退化";
        case VA_ERR_FIT_DEGENERATE:     return "拟合退化";
        case VA_ERR_NO_EDGE_IN_ROI:     return "区域内无边缘";

        // 业务
        case VA_ERR_BARCODE_TYPE:       return "码类型非法";
        case VA_ERR_CALIB_NOT_READY:    return "标定未就绪";
        case VA_ERR_CAMERA_OPEN:        return "相机打开失败";
        case VA_ERR_CAMERA_FRAME:       return "相机读帧失败";

        // 运行控制
        case VA_ERR_ALGO_TIMEOUT:       return "算法超时";

        default:                        return "未知错误码";
    }
}

namespace {

// 把 std::string 按 UTF-8 边界安全地拷进定长缓冲（不切断多字节字符）
void CopyField(const std::string& s, char* dst, int cap)
{
    if (dst == 0 || cap <= 0) return;
    size_t n = s.size();
    if (n >= (size_t)cap)
    {
        n = (size_t)cap - 1;
        while (n > 0 && (((unsigned char)s[n]) & 0xC0) == 0x80) --n;
    }
    if (n > 0) std::memcpy(dst, s.data(), n);
    dst[n] = '\0';
}

} // namespace

int VA_CALL va_get_error_info(void* handle, VA_ErrorInfo* out)
{
    va::Context* ctx = static_cast<va::Context*>(handle);
    if (ctx == 0 || out == 0) return VA_ERR_HANDLE;

    std::memset(out, 0, sizeof(*out));
    out->code = ctx->lastCode;
    CopyField(ctx->lastModule, out->module, (int)sizeof(out->module));
    CopyField(ctx->lastParam,  out->param,  (int)sizeof(out->param));
    CopyField(ctx->lastValue,  out->value,  (int)sizeof(out->value));
    CopyField(ctx->lastExpect, out->expect, (int)sizeof(out->expect));
    CopyField(ctx->lastError,  out->message, (int)sizeof(out->message));
    return VA_OK;
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
int VA_CALL va_blob_analyze_ex(void* handle,
                                     const VA_Image* gray,
                                     double min_area,
                                     double max_area,
                                     VA_Blob* out_blobs,
                                     int max_count,
                                     int* out_count,
                                     int* out_labels)
{
    VA_CTX();
    return va::BlobAnalyzeEx(ctx, gray, min_area, max_area, out_blobs, max_count, out_count, out_labels);
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

int VA_CALL va_match_template_ex(void* handle, const VA_Image* gray, const VA_MatchParam* param,
                                 VA_MatchResult* out_results, int max_count, int* out_count)
{
    VA_CTX();
    return va::MatchTemplateEx(ctx, gray, param, out_results, max_count, out_count);
}

// ---------------------- 条码 / 二维码 ----------------------

int VA_CALL va_barcode_read(void* handle, const VA_Image* gray, const VA_BarcodeParam* param,
                            VA_Barcode* out, int max_count, int* out_count)
{
    VA_CTX();
    return va::BarcodeRead(ctx, gray, param, out, max_count, out_count);
}

const char* VA_CALL va_barcode_version(void)
{
    return va::BarcodeVersion();
}

// ---------------------- 形状匹配（基于边缘轮廓）----------------------

int VA_CALL va_create_shape_model(void* handle,
                                   const VA_Image* gray,
                                   const int* roi,
                                   double contrast)
{
    VA_CTX();
    return va::CreateShapeModel(ctx, gray, roi, contrast);
}

int VA_CALL va_create_shape_model_ex(void* handle,
                                      const VA_Image* gray,
                                      const int* roi,
                                      const VA_ShapeTrainParam* param)
{
    VA_CTX();
    return va::CreateShapeModelEx(ctx, gray, roi, param);
}

int VA_CALL va_get_shape_model_info(void* handle,
                                     int* out_point_count,
                                     double* out_width,
                                     double* out_height)
{
    VA_CTX();
    return va::GetShapeModelInfo(ctx, out_point_count, out_width, out_height);
}

int VA_CALL va_get_shape_model_points(void* handle,
                                       VA_ShapePoint* out_points,
                                       int max_count,
                                       int* out_count)
{
    VA_CTX();
    return va::GetShapeModelPoints(ctx, out_points, max_count, out_count);
}

int VA_CALL va_clear_shape_model(void* handle)
{
    VA_CTX();
    return va::ClearShapeModel(ctx);
}

int VA_CALL va_find_shape_model(void* handle,
                                 const VA_Image* gray,
                                 const VA_ShapeParam* param,
                                 const int* search_roi,
                                 VA_ShapeMatch* out_matches,
                                 int max_count,
                                 int* out_count)
{
    VA_CTX();
    return va::FindShapeModel(ctx, gray, param, search_roi, out_matches, max_count, out_count);
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

// ---------------------- 卡尺（直线 / 圆）----------------------

int VA_CALL va_line_caliper(void* handle,
                             const VA_Image* gray,
                             const VA_CaliperParam* param,
                             VA_CaliperPoint* out_points,
                             int max_count,
                             int* out_count,
                             VA_Line* out_line,
                             double* out_rms)
{
    VA_CTX();
    return va::LineCaliper(ctx, gray, param, out_points, max_count, out_count, out_line, out_rms);
}

int VA_CALL va_circle_caliper(void* handle,
                               const VA_Image* gray,
                               const VA_CaliperParam* param,
                               VA_CaliperPoint* out_points,
                               int max_count,
                               int* out_count,
                               VA_Circle* out_circle,
                               double* out_rms)
{
    VA_CTX();
    return va::CircleCaliper(ctx, gray, param, out_points, max_count, out_count, out_circle, out_rms);
}

// ---------------------- 图像滤波 ----------------------

int VA_CALL va_filter(void* handle,
                      const VA_Image* src,
                      VA_Image* dst,
                      const VA_FilterParam* param)
{
    VA_CTX();
    return va::Filter(ctx, src, dst, param);
}

// ---------------------- 颜色提取 ----------------------

int VA_CALL va_color_extract(void* handle,
                             const VA_Image* src,
                             VA_Image* dst_mask,
                             const VA_ColorParam* param,
                             double min_area,
                             double max_area,
                             VA_Blob* out_blobs,
                             int max_count,
                             int* out_count,
                             VA_ColorStat* out_stat)
{
    VA_CTX();
    return va::ColorExtract(ctx, src, dst_mask, param, min_area, max_area,
                            out_blobs, max_count, out_count, out_stat);
}

// ---------------------- 分类器（轮廓特征 + SVM / KNN）----------------------

int VA_CALL va_clf_create(void* handle, const VA_ClassParam* param)
{
    VA_CTX();
    return va::ClfCreate(ctx, param);
}

int VA_CALL va_clf_add_sample(void* handle, const VA_Image* gray, int label)
{
    VA_CTX();
    return va::ClfAddSample(ctx, gray, label);
}

int VA_CALL va_clf_sample_count(void* handle, int* out_count)
{
    VA_CTX();
    return va::ClfSampleCount(ctx, out_count);
}

int VA_CALL va_clf_train(void* handle)
{
    VA_CTX();
    return va::ClfTrain(ctx);
}

int VA_CALL va_clf_predict(void* handle, const VA_Image* gray,
                           int* out_label, double* out_score)
{
    VA_CTX();
    return va::ClfPredict(ctx, gray, out_label, out_score);
}

int VA_CALL va_clf_save(void* handle, const char* path)
{
    VA_CTX();
    return va::ClfSave(ctx, path);
}

int VA_CALL va_clf_load(void* handle, const char* path)
{
    VA_CTX();
    return va::ClfLoad(ctx, path);
}

int VA_CALL va_clf_clear(void* handle)
{
    VA_CTX();
    return va::ClfClear(ctx);
}

// ---------------------- 传统算法 OCR ----------------------

int VA_CALL va_ocr_set_param(void* handle, const VA_OcrParam* param)
{
    VA_CTX();
    return va::OcrSetParam(ctx, param);
}

int VA_CALL va_ocr_add_template(void* handle, const VA_Image* gray, const char* label)
{
    VA_CTX();
    return va::OcrAddTemplate(ctx, gray, label);
}

int VA_CALL va_ocr_template_count(void* handle, int* out_count)
{
    VA_CTX();
    return va::OcrTemplateCount(ctx, out_count);
}

int VA_CALL va_ocr_clear_templates(void* handle)
{
    VA_CTX();
    return va::OcrClearTemplates(ctx);
}

int VA_CALL va_ocr_segment(void* handle, const VA_Image* gray, const VA_OcrParam* param,
                           VA_OcrChar* out_chars, int max_count, int* out_count)
{
    VA_CTX();
    return va::OcrSegment(ctx, gray, param, out_chars, max_count, out_count);
}

int VA_CALL va_ocr_recognize(void* handle, const VA_Image* gray, const VA_OcrParam* param,
                             char* out_text, int text_cap, double* out_mean_score)
{
    VA_CTX();
    return va::OcrRecognize(ctx, gray, param, out_text, text_cap, out_mean_score);
}

int VA_CALL va_ocr_recognize_ex(void* handle, const VA_Image* gray, const VA_OcrParam* param,
                                VA_OcrChar* out_chars, int chars_cap, int* out_char_count,
                                char* out_text, int text_cap, double* out_mean_score)
{
    VA_CTX();
    return va::OcrRecognizeEx(ctx, gray, param, out_chars, chars_cap, out_char_count,
                              out_text, text_cap, out_mean_score);
}

// ---------------------- YOLO 目标检测（OpenVINO 推理）----------------------

int VA_CALL va_det_create(void* handle, const char* model_path, const char* device)
{
    VA_CTX();
    return va::DetCreate(ctx, model_path, device);
}

int VA_CALL va_det_run(void* handle, const VA_Image* img, float score_thresh, float iou_thresh,
                      VA_DetectResult* out_results, int max_count, int* out_count)
{
    VA_CTX();
    return va::DetRun(ctx, img, score_thresh, iou_thresh, out_results, max_count, out_count);
}

int VA_CALL va_det_class_count(void* handle, int* out_count)
{
    VA_CTX();
    return va::DetClassCount(ctx, out_count);
}

int VA_CALL va_det_class_name(void* handle, int idx, char* out_buf, int buf_size)
{
    VA_CTX();
    return va::DetClassName(ctx, idx, out_buf, buf_size);
}

int VA_CALL va_det_clear(void* handle)
{
    VA_CTX();
    return va::DetClear(ctx);
}

// ---------------------- 深度学习（seg / pose / depth）---------------------

int VA_CALL va_dl_seg_create(void* handle, const char* model_path, const char* device)
{
    VA_CTX();
    return va::DlSegCreate(ctx, model_path, device);
}

int VA_CALL va_dl_seg_run(void* handle, const VA_Image* img, float score_thresh, float iou_thresh,
                          VA_DlBox* out_boxes, int max_count, int* out_count,
                          unsigned char* out_label_map, int label_cap,
                          int* label_w, int* label_h)
{
    VA_CTX();
    return va::DlSegRun(ctx, img, score_thresh, iou_thresh, out_boxes, max_count, out_count,
                        out_label_map, label_cap, label_w, label_h);
}

int VA_CALL va_dl_pose_create(void* handle, const char* model_path, const char* device)
{
    VA_CTX();
    return va::DlPoseCreate(ctx, model_path, device);
}

int VA_CALL va_dl_pose_run(void* handle, const VA_Image* img, float score_thresh, float iou_thresh,
                           VA_DlBox* out_boxes, int max_count, int* out_count,
                           VA_DlKeyPoint* out_kps, int kps_capacity,
                           int* out_kps_per_box, int* out_kps_written)
{
    VA_CTX();
    return va::DlPoseRun(ctx, img, score_thresh, iou_thresh, out_boxes, max_count, out_count,
                         out_kps, kps_capacity, out_kps_per_box, out_kps_written);
}

int VA_CALL va_dl_pose_keypoints(void* handle, int* out_k)
{
    VA_CTX();
    return va::DlPoseKeypoints(ctx, out_k);
}

int VA_CALL va_dl_depth_create(void* handle, const char* model_path, const char* device)
{
    VA_CTX();
    return va::DlDepthCreate(ctx, model_path, device);
}

int VA_CALL va_dl_depth_run(void* handle, const VA_Image* img, float* out_map, int width, int height,
                            float* out_min, float* out_max)
{
    VA_CTX();
    return va::DlDepthRun(ctx, img, out_map, width, height, out_min, out_max);
}

int VA_CALL va_dl_clear(void* handle, int task)
{
    VA_CTX();
    return va::DlClear(ctx, task);
}

int VA_CALL va_calib_find_chessboard(void* handle, const VA_Image* gray, int cols, int rows,
                                      int pattern, double* outXY, int cap, int* outCount)
{
    VA_CTX();
    return va::CalibFindChessboard(ctx, gray, cols, rows, pattern, outXY, cap, outCount);
}

int VA_CALL va_calib_calibrate(void* handle, const double* xy, const int* counts, int nImages,
                               int cols, int rows, int width, int height, int flags,
                               double* outK, double* outDist, double* outRms)
{
    VA_CTX();
    return va::CalibCalibrate(ctx, xy, counts, nImages, cols, rows, width, height, flags,
                              outK, outDist, outRms);
}

int VA_CALL va_dl_describe(void* handle, int task, char* buf, int cap)
{
    VA_CTX();
    return va::DlDescribe(ctx, task, buf, cap);
}

int VA_CALL va_dl_backend_available(int backend)
{
    return va::DlBackendAvailable(backend);
}

// ---------------------- 深度学习 OCR（PaddleOCR）---------------------

int VA_CALL va_ocrdl_create(void* handle, const char* det_model, const char* rec_model,
                            const char* dict_path, const char* device)
{
    VA_CTX();
    return va::OcrDlCreate(ctx, det_model, rec_model, dict_path, device);
}

int VA_CALL va_ocrdl_run(void* handle, const VA_Image* img, float box_thresh, float unclip_ratio,
                         VA_OcrDlLine* out_lines, int max_count, int* out_count,
                         char* out_text, int text_cap, int* out_text_len)
{
    VA_CTX();
    return va::OcrDlRun(ctx, img, box_thresh, unclip_ratio, out_lines, max_count, out_count,
                        out_text, text_cap, out_text_len);
}

int VA_CALL va_ocrdl_clear(void* handle)
{
    VA_CTX();
    return va::OcrDlClear(ctx);
}

// ---------------------- 摄像头（独立句柄，平行于算法 Context）----------------------

/* 相机句柄取用：空句柄直接返回 VA_ERR_HANDLE */
#define VA_CAM()                                                   \
    va::CameraSource* cam = static_cast<va::CameraSource*>(handle); \
    if (cam == 0) return VA_ERR_HANDLE;

int VA_CALL va_camera_enum(int* out_count)
{
    return va::CameraEnum(out_count);
}

void* VA_CALL va_camera_open(int index, const VA_CameraOption* opt)
{
    return va::CameraOpen(index, opt);
}

int VA_CALL va_camera_grab(void* handle, VA_Image* dst, int timeout_ms)
{
    VA_CAM();
    return va::CameraGrab(cam, dst, timeout_ms);
}

int VA_CALL va_camera_get_size(void* handle, int* out_w, int* out_h)
{
    VA_CAM();
    return va::CameraGetSize(cam, out_w, out_h);
}

int VA_CALL va_camera_close(void* handle)
{
    VA_CAM();
    return va::CameraClose(cam);
}

int VA_CALL va_camera_get_last_error(void* handle, char* buffer, int size)
{
    if (buffer == 0 || size <= 0) return 0;

    // handle 为空时返回最近一次「枚举 / 打开」的原因（CameraEnum / CameraOpen 写入），
    // 供上层在 Open 失败（无句柄）或枚举为 0 时取到具体理由；有句柄时行为不变。
    std::string s = (handle == 0) ? va::CameraReason()
                                  : static_cast<va::CameraSource*>(handle)->lastError;

    int n = (int)s.size();
    if (n > size - 1) n = size - 1;
    if (n > 0) std::memcpy(buffer, s.c_str(), (size_t)n);
    buffer[n] = '\0';
    return n;
}

} // extern "C"
