/* 模板训练与匹配（归一化相关系数法，灰度）。
   除平移外支持**角度与缩放搜索**，用图像金字塔做由粗到细，输出前再用去掉旋转
   padding 的精确 NCC 重打一次分（对外 score 因此与旋转角无关，可比阈值）。

   典型用法：
    1) va_train_template            框选模板 ROI，训练一次
    2) va_match_template_ex         按 VA_MatchParam 搜索，可反复调用（参数每帧可变）
    3) va_match_template            兼容旧接口：等价于"只搜平移、只要 1 个结果"
       （nPoses == 1 时整条路径与旧实现逐位一致，不做金字塔） */
#ifndef VA_MATCH_H
#define VA_MATCH_H

#include "va_export.h"
#include "va_types.h"

#ifdef __cplusplus
extern "C" {
#endif

/* ---------------------------------------------------------------------------
 * 匹配参数。角度单位为度、缩放为相对模板尺寸的比例，越界会被内部收敛
 * （角度区间反了自动交换；缩放 min/max 反了自动交换）。
 * -------------------------------------------------------------------------*/
typedef struct VA_MatchParam {
    double angle_start;     /* 起始角度（度） */
    double angle_end;       /* 结束角度（度） */
    double angle_step;      /* 角度步长（度）；<=0 时按 1.0 处理 */
    double scale_min;       /* 最小缩放；<=0 时按 1.0 处理 */
    double scale_max;       /* 最大缩放；< scale_min 时按 scale_min 处理 */
    double scale_step;      /* 缩放步长；<=0 表示只在 scale_min 处搜索 */
    int    pyramid_levels;  /* 金字塔层数 1~4，越大越快但定位越依赖精化；
                               注意：只有 1 个姿态时一律按 1 层跑（与旧实现一致） */
    double min_score;       /* 最小得分 0~1，低于该值的候选不输出 */
    int    max_matches;     /* 最多返回几个目标；<=0 时按 1 处理 */
    double max_overlap;     /* 抑制阈值：外接框交集/较小框面积 > 该比例视为同一目标 */
    int    subpixel;        /* 1 = 对最终位置做抛物线亚像素精化 */
} VA_MatchParam;

/* 用 ROI 区域训练模板。roi 为 [x, y, w, h]，传 NULL 表示整图。
   模板保存在 handle 对应的上下文中。 */
VA_API int VA_CALL va_train_template(void* handle,
                                     const VA_Image* gray,
                                     const int* roi);

/* 兼容旧接口：只搜平移、只返回 1 个结果。等价于用默认 VA_MatchParam 调
   va_match_template_ex（angle=0/scale=1/levels=1）。 */
VA_API int VA_CALL va_match_template(void* handle,
                                     const VA_Image* gray,
                                     VA_MatchResult* out);

/* ---------------------------------------------------------------------------
 * 按参数搜索。out_results 由调用方分配，max_count 为容量，out_count 为实际写入数。
 * 返回 VA_OK（找到 0 个也返回 OK，用 out_count 判断）/ VA_ERR_NO_TEMPLATE 等。
 * 搜索区域由调用方裁剪图像决定（本层不做 ROI，避免与上层 ROI 语义打架）。
 * -------------------------------------------------------------------------*/
VA_API int VA_CALL va_match_template_ex(void* handle,
                                        const VA_Image* gray,
                                        const VA_MatchParam* param,
                                        VA_MatchResult* out_results,
                                        int max_count,
                                        int* out_count);

#ifdef __cplusplus
}
#endif

#endif /* VA_MATCH_H */
