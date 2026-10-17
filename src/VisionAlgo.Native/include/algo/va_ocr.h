/* ---------------------------------------------------------------------------
 *  传统算法 OCR：连通域分割 + 尺寸归一化 + 模板匹配识别。
 *
 *  本库未链接 opencv_contrib（没有 cv::text、没有 xfeatures2d），因此这里自搭一套
 *  传统流水线：
 *
 *      灰度图 → 二值化（固定阈值或 Otsu，可选反色）
 *            → connectedComponentsWithStats 取所有连通域
 *            → 按字符高度 / 宽高比过滤
 *            → 按 max_gap 合并水平相邻过近的框（修复左右结构字符被切成两半）
 *            → 按 y 中心分行、行内按 x 排序
 *            → （识别）逐字符归一化 → 与模板库做 TM_CCOEFF_NORMED 匹配
 *
 *  模板库：由调用方裁剪好单通道灰度字符 ROI，通过 va_ocr_add_template 累积，
 *  内部立即按 norm_w × norm_h 归一化保存；识别时用同样的归一化尺寸比对。
 *
 *  无模板时 va_ocr_recognize 仍会完成分割、用 '?' 填充字符、out_mean_score 给 0，
 *  并返回 VA_OK——这是刻意的，方便上层单独调试分割效果。
 *
 *  状态挂载：模板库与默认参数保存在 va::Context 上（一个句柄 = 一条流程）。
 *
 *  返回码：VA_OK / VA_ERR_ARG / VA_ERR_TYPE / VA_ERR_SIZE / VA_ERR_INTERNAL
 * -------------------------------------------------------------------------*/
#ifndef VA_OCR_H
#define VA_OCR_H

#include "va_export.h"
#include "va_types.h"

#ifdef __cplusplus
extern "C" {
#endif

/* 单字符标签的最大字节数（含结尾 '\0'） */
#define VA_OCR_LABEL_BYTES 8

/* ---------------------------------------------------------------------------
 * OCR 参数。哨兵值语义：threshold < 0 → Otsu 自动阈值；
 * min/max_char_h、min/max_aspect <= 0 → 不限制；max_gap < 0 → 用 3；
 * norm_w / norm_h <= 0 → 用 16 / 24。
 * -------------------------------------------------------------------------*/
typedef struct VA_OcrParam {
    int    threshold;                 /* 二值化阈值 0~255；<0 用 Otsu */
    int    invert;                    /* 1 = 反色（白底黑字时） */
    double min_char_h, max_char_h;    /* 字符高度过滤，px；<=0 不限制 */
    double min_aspect, max_aspect;    /* 宽高比 (w/h) 过滤；<=0 不限制 */
    int    max_gap;                   /* 同行字符最大水平间隙，px；<0 用 3 */
    int    norm_w, norm_h;            /* 识别前归一化尺寸；<=0 用 16x24 */
    double accept_score;              /* 接受阈值 0~1，典型 0.6 */
} VA_OcrParam;

/* 单个字符的分割 / 识别结果 */
typedef struct VA_OcrChar {
    double x, y, w, h;      /* 字符外接框（原图坐标） */
    char   label[VA_OCR_LABEL_BYTES];  /* 识别出的字符（UTF-8，通常单个 ASCII）；未识别为 "?" */
    double score;           /* 匹配置信度 0~1 */
} VA_OcrChar;

/* 设置默认参数；之后 va_ocr_segment / va_ocr_recognize 若传 NULL 则用它 */
VA_API int VA_CALL va_ocr_set_param(void* handle, const VA_OcrParam* param);

/* 添加一个字符模板：输入单通道灰度字符 ROI，内部按 norm_w×norm_h 归一化保存。
   label 为 UTF-8 字符串，超长会被截断到 VA_OCR_LABEL_BYTES-1 字节。 */
VA_API int VA_CALL va_ocr_add_template(void* handle, const VA_Image* gray, const char* label);

/* 取当前模板个数 */
VA_API int VA_CALL va_ocr_template_count(void* handle, int* out_count);

/* 清空模板库 */
VA_API int VA_CALL va_ocr_clear_templates(void* handle);

/* ---------------------------------------------------------------------------
 * 字符分割。只填 VA_OcrChar 的 x/y/w/h，label 置 "?"、score 置 0。
 *
 * param     : 可为 NULL，表示用 va_ocr_set_param 设置的默认参数
 * out_chars : 调用方分配，容量 >= max_count；可为 NULL 表示只要计数
 * max_count : out_chars 容量
 * out_count : 实际写入个数（已按行、列排序）
 * -------------------------------------------------------------------------*/
VA_API int VA_CALL va_ocr_segment(void* handle,
                                  const VA_Image* gray,
                                  const VA_OcrParam* param,
                                  VA_OcrChar* out_chars,
                                  int max_count,
                                  int* out_count);

/* ---------------------------------------------------------------------------
 * 分割 + 识别，输出整段文本。
 *
 * out_text       : UTF-8 输出缓冲，由调用方分配
 * text_cap       : out_text 的字节容量（含结尾 '\0'）
 * out_mean_score : 已接受字符的平均置信度；无模板时为 0
 *
 * 行与行之间用 '\n' 分隔；同一行内相邻字符 x 间距 > 1.5 倍平均字符宽时插入一个空格。
 * -------------------------------------------------------------------------*/
VA_API int VA_CALL va_ocr_recognize(void* handle,
                                   const VA_Image* gray,
                                   const VA_OcrParam* param,
                                   char* out_text,
                                   int text_cap,
                                   double* out_mean_score);

#ifdef __cplusplus
}
#endif

#endif /* VA_OCR_H */
