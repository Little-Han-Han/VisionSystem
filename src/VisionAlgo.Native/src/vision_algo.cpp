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
        case VA_ERR_TIMEOUT:     return "timeout";
        case VA_ERR_CAMERA:      return "camera error";
        case VA_ERR_NO_EDGE:     return "no edge found";
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
                          VA_DlBox* out_boxes, int max_count, int* out_count)
{
    VA_CTX();
    return va::DlSegRun(ctx, img, score_thresh, iou_thresh, out_boxes, max_count, out_count);
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
