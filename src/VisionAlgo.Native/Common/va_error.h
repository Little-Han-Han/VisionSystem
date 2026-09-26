/* 统一错误码定义。库内所有导出函数返回 int：0 成功，其余见下。 */
#ifndef VA_ERROR_H
#define VA_ERROR_H

#include "va_export.h"

#ifdef __cplusplus
extern "C" {
#endif

#define VA_OK              0   /* 成功 */
#define VA_ERR_HANDLE      1   /* 句柄为空（未调用 va_create 或已销毁） */
#define VA_ERR_ARG         2   /* 参数非法：空指针 / 尺寸为 0 */
#define VA_ERR_SIZE        3   /* 输出缓冲区尺寸与算法结果不匹配 */
#define VA_ERR_TYPE        4   /* 图像类型不支持（通道数不是 1/3/4，或要求单通道却给了彩色） */
#define VA_ERR_INTERNAL    5   /* OpenCV 内部异常，详情见 va_get_last_error */
#define VA_ERR_NO_TEMPLATE 6   /* 未训练模板就调用匹配 */

/* 错误码转可读文本（始终返回静态字符串，不会失败） */
VA_API const char* VA_CALL va_error_string(int code);

#ifdef __cplusplus
}
#endif

#endif /* VA_ERROR_H */
