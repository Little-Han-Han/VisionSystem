/* 连通域（Blob）分析。 */
#ifndef VA_BLOB_H
#define VA_BLOB_H

#include "va_export.h"
#include "va_types.h"

#ifdef __cplusplus
extern "C" {
#endif

/* 输入建议为二值图；若不是，内部先按 128 阈值化。
   min_area : 面积下限，小于它的连通域被丢弃
   max_area : 面积上限，<= 0 表示不限
   out_blobs: 调用方提供的数组；max_count 为其容量
   out_count: 实际写入数量（背景不计入） */
VA_API int VA_CALL va_blob_analyze(void* handle,
                                   const VA_Image* gray,
                                   double min_area,
                                   double max_area,
                                   VA_Blob* out_blobs,
                                   int max_count,
                                   int* out_count);

#ifdef __cplusplus
}
#endif

#endif /* VA_BLOB_H */
