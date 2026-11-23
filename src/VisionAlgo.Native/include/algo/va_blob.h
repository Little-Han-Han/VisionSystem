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

/* 扩展版：除连通域列表外，可选输出**标签图**（界面按连通域上色用）。
   out_labels : 调用方分配 width*height 个 int32；写入 1..k 表示该像素属于
                out_blobs[i-1]，0 表示背景或未保留（面积/数量被过滤）。
                传 NULL 表示不要标签图。
   与 va_blob_analyze 的区别：始终计算全部形状特征（周长/圆度/矩形度/凸度…），
   并额外做一次像素遍历写标签图（仅当 out_labels != NULL）。 */
VA_API int VA_CALL va_blob_analyze_ex(void* handle,
                                      const VA_Image* gray,
                                      double min_area,
                                      double max_area,
                                      VA_Blob* out_blobs,
                                      int max_count,
                                      int* out_count,
                                      int* out_labels);

#ifdef __cplusplus
}
#endif

#endif /* VA_BLOB_H */
