/* ============================================================================
 *  本机摄像头接口（基于 OpenCV VideoCapture）
 *
 *  与算法 Context 平行：va_camera_open 返回独立句柄（void*），配 va_camera_close 释放。
 *  枚举为「探测式」：逐个尝试打开序号 0..N-1，成功即计数。拿不到设备友好名，
 *  故 VA_CameraInfo.name 留空，由上层生成「本机摄像头 n」。
 *
 *  帧约定：va_camera_grab 把 BGR 8UC3 逐行拷进调用方给的 VA_Image（channels 必须=3）。
 *  超时返回 VA_ERR_TIMEOUT，设备丢失 / 读帧失败返回 VA_ERR_CAMERA。
 * ==========================================================================*/
#ifndef VA_CAMERA_H
#define VA_CAMERA_H

#include "va_export.h"
#include "va_types.h"

#ifdef __cplusplus
extern "C" {
#endif

/* 本机设备描述。当前仅 index 有效（枚举阶段不保留句柄、取不到名字）。 */
typedef struct VA_CameraInfo {
    int  index;          /* 本机设备序号 0..count-1 */
    char name[128];      /* 预留设备名（UTF-8）；OpenCV 取不到时置空 */
    char path[256];      /* 预留唯一路径（当前置空） */
} VA_CameraInfo;

/* 打开选项。全 0 表示使用设备默认。 */
typedef struct VA_CameraOption {
    int width;      /* 期望宽，0=设备默认 */
    int height;     /* 期望高，0=设备默认 */
    int fps;        /* 期望帧率，0=设备默认 */
    int backend;    /* 0=自动(MSMF 优先,失败回退 DShow), 1=DShow, 2=MSMF */
} VA_CameraOption;

/* 枚举本机摄像头：out_count 返回设备数；返回 VA_OK。只探测不保留句柄。 */
VA_API int   VA_CALL va_camera_enum(int* out_count);

/* 打开序号 index 的本机摄像头，opt 可为 NULL（全默认）。成功返回句柄，失败返回 NULL。 */
VA_API void* VA_CALL va_camera_open(int index, const VA_CameraOption* opt);

/* 抓一帧 BGR 到 dst（channels 必须=3，data 由调用方分配，stride 由调用方给出）。成功 VA_OK。 */
VA_API int   VA_CALL va_camera_grab(void* cam, VA_Image* dst, int timeout_ms);

/* 打开后有效，返回协商分辨率。 */
VA_API int   VA_CALL va_camera_get_size(void* cam, int* out_w, int* out_h);

/* 关闭并释放句柄。 */
VA_API int   VA_CALL va_camera_close(void* cam);

/* 取最近错误文本（写入调用方缓冲，返回写入字节数）。 */
VA_API int   VA_CALL va_camera_get_last_error(void* cam, char* buffer, int size);

#ifdef __cplusplus
}
#endif

#endif /* VA_CAMERA_H */
