/* 库与上下文的生命周期管理。 */
#ifndef VA_LIFECYCLE_H
#define VA_LIFECYCLE_H

#include "va_export.h"

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

/* 设置算法内部 OpenCV 的并行线程数（全局，进程内一次即可）。返回真正生效的线程数。
   n <= 0  表示"串行"（关闭 OpenCV 内部并行，只用一个核）；
   n >  0  表示最多用 n 个线程。
   【为什么必须有】OpenCV 默认用满所有核。宿主本来就是"多流程各占一个线程"并行，
   若每个流程内部再开满核，N 条流程 = N×核数 个线程抢 核数 个核，实测 3 条流程能吃满
   12 核却只有 3 fps —— 互相抢锁 + 缓存颠簸，比串行还慢。 */
VA_API int   VA_CALL va_set_threads(int n);

/* 设置**调用线程**的软超时截止时间（毫秒，<=0 表示不限）。
   【语义】协作式中断：算法在"检查点"（每个扫描位置 / 每个字符 / 每个姿态 / 每层金字塔）
   读一次时钟，到点就提前返回 VA_ERR_ALGO_TIMEOUT，而不是把整帧算完再说。
   【为什么不是硬超时】库内全部是同步 C++ 调用，宿主无法从外部杀死线程；
   真正能中途生效的只有"算法自己愿意停下来"。硬杀线程会把正在写的 Mat / 模板状态留在半路，
   下一次调用就是野指针。
   【粒度】单次 OpenCV 调用（cv::matchTemplate、OpenVINO 推理）内部不可中断，
   到点只能等它返回；调用方仍需按实际耗时兜底判超时。
   【作用域】thread_local：每条流程一个线程，互不影响；节点执行完请置 0，
   否则下一次调用的算法会继承上一帧的截止时间而"莫名超时"。 */
VA_API void  VA_CALL va_set_deadline_ms(int ms);

#ifdef __cplusplus
}
#endif

#endif /* VA_LIFECYCLE_H */
