/* 库与上下文的生命周期管理。 */
#ifndef VA_LIFECYCLE_H
#define VA_LIFECYCLE_H

#include "common/va_export.h"

#ifdef __cplusplus
extern "C" {
#endif

/* 库版本号：主版本 * 10000 + OpenCV 次版本记号。例如 10455 = v1.00 + OpenCV 4.5.5 */
VA_API int   VA_CALL va_get_version(void);

/* 创建算法上下文。每个上下文独立保存模板等状态，可多实例并发使用。
   失败返回 NULL。 */
VA_API void* VA_CALL va_create(void);

/* 销毁上下文。传入 NULL 是安全的。 */
VA_API void  VA_CALL va_destroy(void* handle);

/* 取最近一次错误的详细描述；写入 buffer 并返回字符数（不含结尾 0）。
   buffer 或 size 不合法时返回 0。 */
VA_API int   VA_CALL va_get_last_error(void* handle, char* buffer, int size);

#ifdef __cplusplus
}
#endif

#endif /* VA_LIFECYCLE_H */
