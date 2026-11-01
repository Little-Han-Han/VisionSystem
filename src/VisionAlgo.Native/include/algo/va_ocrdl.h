/* ---------------------------------------------------------------------------
 *  深度学习 OCR（PaddleOCR）：DB 文本检测 + CRNN/SVTR 识别，两阶段。
 *
 *  与传统 OCR（va_ocr_*，字符模板匹配）是两套完全独立的东西：
 *    · va_ocr_*    : 内置字符模板库，靠"分割 + 模板比对"，适合固定字体的喷码/铭牌
 *    · va_ocrdl_*  : 深度学习模型，直接给文本行 + 内容，适合自然场景 / 多字体
 *
 *  模型（ONNX，OpenVINO 推理）：
 *    det : PaddleOCR 检测模型，输入 [1,3,H,W]（H/W 可动态，会按模型形状 reshape），
 *          输出 [1,1,H,W] 的 0~1 概率图（DB 输出）
 *    rec : PaddleOCR 识别模型，输入 [1,3,48,W]，输出 [1,T,C]（CTC）
 *    dict: ppocr_keys.txt（每行一个字符；索引 0 恒为 CTC blank）
 *
 *  后处理说明（简化但可用）：
 *    检测框用"二值化 → 连通域 → 最小外接矩形 → 按 unclip_ratio 外扩"，
 *    没有实现 pyclipper 那种多边形偏移（需要额外依赖）；对横排文本足够。
 *    识别用 CTC 贪心解码（去 blank、去连续重复）。
 * -------------------------------------------------------------------------*/
#ifndef VA_OCRDL_H
#define VA_OCRDL_H

#include "va_export.h"
#include "va_types.h"

#ifdef __cplusplus
extern "C" {
#endif

/* 一行识别结果 */
typedef struct VA_OcrDlLine {
    float x, y, w, h;    /* 文本行外接矩形（原图像素坐标） */
    float score;         /* 检测置信度（框内概率均值，0~1） */
    char  text[192];     /* 识别文本（UTF-8；超长截断） */
} VA_OcrDlLine;

/* 加载两个模型 + 字典。device 为空按 "CPU" 处理。
 *   返回 VA_OK / VA_ERR_ARG（路径空、字典为空）/ VA_ERR_INTERNAL（加载失败） */
VA_API int VA_CALL va_ocrdl_create(void* handle, const char* det_model, const char* rec_model,
                                   const char* dict_path, const char* device);

/* 推理一帧：文本框写入 lines（容量 max_count），整段文本写入 out_text。
 *   box_thresh : 概率图二值化阈值（0.3 常用）
 *   unclip_ratio: 文本框外扩比例（1.5~2.0 常用，按框短边比例扩）
 *   返回 VA_OK / VA_ERR_ARG / VA_ERR_NO_TEMPLATE / VA_ERR_INTERNAL；结果多于容量返回 VA_ERR_SIZE */
VA_API int VA_CALL va_ocrdl_run(void* handle, const VA_Image* img,
                                float box_thresh, float unclip_ratio,
                                VA_OcrDlLine* out_lines, int max_count, int* out_count,
                                char* out_text, int text_cap, int* out_text_len);

/* 释放模型与字典 */
VA_API int VA_CALL va_ocrdl_clear(void* handle);

#ifdef __cplusplus
}
#endif

#endif /* VA_OCRDL_H */
