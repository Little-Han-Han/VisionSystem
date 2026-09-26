/* 几何拟合：对二值化后面积最大的轮廓做圆 / 直线拟合。 */
#ifndef VA_FIT_H
#define VA_FIT_H

#include "common/va_export.h"
#include "common/va_types.h"

#ifdef __cplusplus
extern "C" {
#endif

/* 最小外接圆 + 平均半径残差 */
VA_API int VA_CALL va_fit_circle(void* handle, const VA_Image* gray, VA_Circle* out);

/* 最小二乘直线拟合，输出线段两端点 */
VA_API int VA_CALL va_fit_line(void* handle, const VA_Image* gray, VA_Line* out);

#ifdef __cplusplus
}
#endif

#endif /* VA_FIT_H */
