/* 图像质量与简单测量。 */
#ifndef VA_MEASURE_H
#define VA_MEASURE_H

#include "common/va_export.h"
#include "common/va_types.h"

#ifdef __cplusplus
extern "C" {
#endif

/* 清晰度评价：拉普拉斯算子的方差。值越大越清晰，
   同一场景下可用于判断失焦 / 运动模糊。 */
VA_API int VA_CALL va_sharpness(void* handle, const VA_Image* gray, double* out_score);

/* 沿指定行扫描，统计灰度跳变超过 threshold_delta 的次数。
   可用于粗略计数（如数齿、数线）。 */
VA_API int VA_CALL va_edge_count(void* handle,
                                 const VA_Image* gray,
                                 int scan_row,
                                 int threshold_delta,
                                 int* out_count);

#ifdef __cplusplus
}
#endif

#endif /* VA_MEASURE_H */
