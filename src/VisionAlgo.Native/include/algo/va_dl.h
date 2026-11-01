/* ---------------------------------------------------------------------------
 *  深度学习工具（OpenVINO 推理）：语义分割 / 姿态估计 / 深度估计。
 *
 *  与「目标检测」（va_detect.h）共用同一套预处理（letterbox → RGB → /255 → NCHW），
 *  区别只在输出解析：
 *    seg   : out0 = [1, 4+nc+32, A]（32 = mask 系数），out1 = proto [1,32,h,w]
 *    pose  : out0 = [1, 4+nc+3k, A]（3k = k 个关键点的 x,y,conf）
 *    depth : out0 = [1,1,H,W] 或 [1,H,W]，逐像素深度（归一化后返回）
 *
 *  模型：Ultralytics 导出的 YOLO26 ONNX（seg / pose），深度模型接受单通道输出。
 *  设备：device 传 "CPU" / "GPU" / "NPU"（OpenVINO 设备名）。
 *        CUDA 预留：见 VA_DlBackend，当前仅 OPENVINO 可用，CUDA 会返回 VA_ERR_ARG 并给出明确文案。
 *
 *  返回码沿用全局约定（VA_OK / VA_ERR_ARG / VA_ERR_SIZE / VA_ERR_NO_TEMPLATE / VA_ERR_INTERNAL）。
 * -------------------------------------------------------------------------*/
#ifndef VA_DL_H
#define VA_DL_H

#include "va_export.h"
#include "va_types.h"

#ifdef __cplusplus
extern "C" {
#endif

/* 任务编号（与 C# 侧 FlowNodeType 一一对应） */
#define VA_DL_TASK_SEG     1
#define VA_DL_TASK_POSE    2
#define VA_DL_TASK_DEPTH   3

/* 推理后端（CUDA 预留；当前只有 OpenVINO 可用） */
#define VA_DL_BACKEND_OPENVINO 0
#define VA_DL_BACKEND_CUDA     1

/* 单个目标（分割 / 姿态共用）：
 *   seg  : mask_area = 目标掩膜像素数（在框内按 sigmoid(coef·proto) > 0.5 统计）
 *   pose : 关键点通过 va_dl_pose_run 的 out_kps 返回 */
typedef struct VA_DlBox {
    float x, y, w, h;     /* 原图像素坐标（左上角 + 宽高） */
    int   class_id;
    float score;
    int   mask_area;      /* seg 专用；其他任务为 0 */
} VA_DlBox;

/* 关键点（pose）：坐标为原图像素 */
typedef struct VA_DlKeyPoint {
    float x, y, score;
} VA_DlKeyPoint;

/* ---------------- 语义分割 ---------------- */

/* 加载分割模型。device 为空时按 "CPU" 处理。 */
VA_API int VA_CALL va_dl_seg_create(void* handle, const char* model_path, const char* device);

/* 推理一帧：输出每个目标的框 + 类别 + 分数 + 掩膜面积。
 *   max_count 为数组容量；超过时只写前 max_count 个并返回 VA_ERR_SIZE。
 *
 *   out_label_map 可选（传 0 则不算逐像素掩膜，速度快）：
 *     写入 proto 分辨率（通常 160×160）的 **标签图**，0 = 背景，i = 第 i 个目标（1 起），
 *     与 out_boxes 的顺序一一对应。调用方负责按框尺寸缩放到原图后上色。
 *     label_w / label_h 由本函数回填实际尺寸。 */
VA_API int VA_CALL va_dl_seg_run(void* handle, const VA_Image* img,
                                 float score_thresh, float iou_thresh,
                                 VA_DlBox* out_boxes, int max_count, int* out_count,
                                 unsigned char* out_label_map, int label_cap,
                                 int* label_w, int* label_h);

/* ---------------- 姿态估计 ---------------- */

VA_API int VA_CALL va_dl_pose_create(void* handle, const char* model_path, const char* device);

/* 推理一帧：out_kps 按目标顺序紧密排列（第 i 个目标的关键点从 i*kps_per_box 开始）。
 *   kps_per_box 由 va_dl_pose_keypoints 给出（模型决定，COCO 人体为 17）。 */
VA_API int VA_CALL va_dl_pose_run(void* handle, const VA_Image* img,
                                  float score_thresh, float iou_thresh,
                                  VA_DlBox* out_boxes, int max_count, int* out_count,
                                  VA_DlKeyPoint* out_kps, int kps_capacity,
                                  int* out_kps_per_box, int* out_kps_written);

/* 取关键点数（模型推出来的 k；未创建返回 0）。 */
VA_API int VA_CALL va_dl_pose_keypoints(void* handle, int* out_k);

/* ---------------- 深度估计 ---------------- */

VA_API int VA_CALL va_dl_depth_create(void* handle, const char* model_path, const char* device);

/* 推理一帧并输出**缩放到调用方尺寸**的深度图（float，0~1 归一化）。
 *   out_map 长度须为 width*height；out_min/out_max 为原始值域（便于还原物理量）。 */
VA_API int VA_CALL va_dl_depth_run(void* handle, const VA_Image* img,
                                   float* out_map, int width, int height,
                                   float* out_min, float* out_max);

/* ---------------- 公共 ---------------- */

/* 释放指定任务的模型（task 取 VA_DL_TASK_*） */
VA_API int VA_CALL va_dl_clear(void* handle, int task);

/* 取模型解析结果（写 ASCII 摘要：输入尺寸、输出通道、类别数、关键点数）。
 * 用于现场排查"为什么解析不对"——把模型实际 I/O 形状摆出来，比猜快。
 *   buf 容量不足时截断；返回写入长度。未创建返回 0。 */
VA_API int VA_CALL va_dl_describe(void* handle, int task, char* buf, int cap);

/* 后端能力查询：backend 未实现时返回 0。
 * 供 UI 在"推理后端"下拉里只列出可用项（CUDA 目前恒为 0）。 */
VA_API int VA_CALL va_dl_backend_available(int backend);

#ifdef __cplusplus
}
#endif

#endif /* VA_DL_H */
