/* ---------------------------------------------------------------------------
 *  YOLO 目标检测：基于 OpenVINO 的深度学习推理（CPU / GPU 等设备）。
 *
 *  模型：Ultralytics 导出的 YOLO ONNX，输出布局固定为 [1, 4 + num_classes, anchors]。
 *       其中 4 = 边界框 (cx, cy, w, h)，单位为目标输入尺寸（如 640）下的像素；
 *       num_classes 个值为各类原始 logits（需经 sigmoid 得到置信度）。
 *       v8 / v11 / v26 等 Ultralytics 系列导出格式一致，本实现通用。
 *
 *  典型调用顺序：
 *      va_det_create      加载并编译模型（device 默认 "CPU"）
 *      va_det_run         对一帧推理：letterbox 缩放 → BGR→RGB → /255 → NCHW →
 *                         推理 → 解析 + NMS → 写回原图像素坐标的结果数组
 *      va_det_class_count / va_det_class_name  取类别名（COCO 或侧车 <model>.txt）
 *      va_det_clear       释放模型
 *
 *  坐标：返回结果为原图（输入 VA_Image）像素坐标系下的左上角 (x, y) 与宽高 (w, h)。
 *
 *  类别名：优先读取与模型同名的 .txt（每行一个类名，COCO 风格）；
 *          侧车缺失时回退内嵌 COCO-80 名称（恰好 80 类时）或 class_N，
 *          保证仅有 .onnx 也能正常运行。
 *
 *  返回码：VA_OK / VA_ERR_ARG / VA_ERR_TYPE / VA_ERR_SIZE
 *          / VA_ERR_NO_TEMPLATE（未 create）/ VA_ERR_INTERNAL
 * -------------------------------------------------------------------------*/
#ifndef VA_DETECT_H
#define VA_DETECT_H

#include "va_export.h"
#include "va_types.h"

#ifdef __cplusplus
extern "C" {
#endif

/* 单个检测结果：原图像素坐标系下的边界框 + 类别 + 置信度 */
typedef struct VA_DetectResult {
    float x;         /* 左上角 x（原图像素坐标） */
    float y;         /* 左上角 y */
    float w;         /* 宽 */
    float h;         /* 高 */
    int   class_id;  /* 类别索引（COCO 0~79 等） */
    float score;     /* 置信度 0~1 */
} VA_DetectResult;

/* 创建 / 重置检测器：加载 ONNX 模型并编译到指定设备，缓存类别名表。
 *   handle     : va_create 得到的句柄
 *   model_path : UTF-8 模型路径（指向 YOLO ONNX）
 *   device     : 推理设备名（"CPU" / "GPU" 等）；NULL 或空串按 "CPU" 处理
 *   输入尺寸固定为模型导出时的尺寸（如 640），无需调用方指定。
 *   返回 VA_OK / VA_ERR_ARG（路径空或形状非法）/ VA_ERR_INTERNAL（加载/编译失败） */
VA_API int VA_CALL va_det_create(void* handle, const char* model_path, const char* device);

/* 推理一张图：内部完成预处理、推理、解析、NMS，结果写入调用方缓冲区。
 *   img         : 输入图（1/3/4 通道均可，自动转 BGR 三通道）
 *   score_thresh: 类别置信度阈值（0~1），如 0.25
 *   iou_thresh  : NMS IoU 阈值（0~1），如 0.45
 *   out_results : 调用方预分配的 VA_DetectResult 数组
 *   max_count   : 数组容量（<=0 视为非法）
 *   out_count   : 实际检测数；若超过 max_count，仅写前 max_count 个并返回 VA_ERR_SIZE
 *   返回 VA_OK / VA_ERR_ARG / VA_ERR_TYPE / VA_ERR_SIZE / VA_ERR_NO_TEMPLATE / VA_ERR_INTERNAL */
VA_API int VA_CALL va_det_run(void* handle, const VA_Image* img,
                              float score_thresh, float iou_thresh,
                              VA_DetectResult* out_results, int max_count, int* out_count);

/* 取类别数（= 模型输出通道 - 4） */
VA_API int VA_CALL va_det_class_count(void* handle, int* out_count);

/* 取第 idx 个类别名（UTF-8），写入 out_buf（容量 buf_size）。
 *   返回写入长度（不含结尾 \0）；参数非法或越界返回 0。 */
VA_API int VA_CALL va_det_class_name(void* handle, int idx, char* out_buf, int buf_size);

/* 释放模型与编译结果（句柄销毁时也会清理，可不显式调用） */
VA_API int VA_CALL va_det_clear(void* handle);

#ifdef __cplusplus
}
#endif

#endif /* VA_DETECT_H */
