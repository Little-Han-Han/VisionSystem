/* 图像预处理：灰度化、滤波、二值化、形态学。 */
#ifndef VA_PREPROCESS_H
#define VA_PREPROCESS_H

#include "va_export.h"
#include "va_types.h"

#ifdef __cplusplus
extern "C" {
#endif

/* src -> dst。dst 缓冲区由调用方分配并保持单通道，宽高须与 src 一致。
   执行顺序：灰度化 → 高斯模糊 → 二值化 → 形态学。 */
VA_API int VA_CALL va_preprocess(void* handle,
                                 const VA_Image* src,
                                 VA_Image* dst,
                                 const VA_PreprocessParam* param);

#ifdef __cplusplus
}
#endif

#endif /* VA_PREPROCESS_H */
