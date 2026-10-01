/* ============================================================================
 *  基于边缘轮廓的形状匹配（Shape-based Matching）
 *
 *  与 va_match.h 的灰度归一化相关匹配不同，本算法以「边缘点的梯度方向」为特征：
 *    - 对光照变化、局部遮挡、轻微对比度差异更鲁棒
 *    - 支持角度搜索与缩放搜索
 *    - 用图像金字塔由粗到细搜索，兼顾速度与精度
 *    - 支持搜索区域 ROI、多目标输出与非极大值抑制
 *
 *  典型用法：
 *    1) va_create_shape_model   用模板 ROI 训练一次，模型保存在句柄上下文里
 *    2) va_find_shape_model     在待检图上搜索，可反复调用
 *    3) va_clear_shape_model    释放模型
 * ==========================================================================*/
#ifndef VA_SHAPE_H
#define VA_SHAPE_H

#include "va_export.h"
#include "va_types.h"

#ifdef __cplusplus
extern "C" {
#endif

/* ---------------------------------------------------------------------------
 * 匹配参数。角度单位为度，缩放为相对模板尺寸的比例。
 * 各字段的取值会在算法内部做合法性收敛（如角度区间反了会自动交换）。
 * -------------------------------------------------------------------------*/
typedef struct VA_ShapeParam {
    double angle_start;     /* 起始角度（度） */
    double angle_end;       /* 结束角度（度） */
    double angle_step;      /* 角度步长（度）；<=0 时按 1.0 处理 */
    double scale_min;       /* 最小缩放；<=0 时按 1.0 处理 */
    double scale_max;       /* 最大缩放；< scale_min 时按 scale_min 处理 */
    double scale_step;      /* 缩放步长；<=0 表示只在 scale_min 处搜索 */
    int    pyramid_levels;  /* 金字塔层数 1~8，越大越快但越易漏检小目标 */
    double min_score;       /* 最小得分 0~1，低于该值的候选被丢弃 */
    int    max_matches;     /* 最多返回几个目标；<=0 时按 1 处理 */
    double max_overlap;     /* 抑制阈值：两中心距 < 该比例 × 模板最小边 视为同一目标 */
    int    subpixel;        /* 1 = 对最终结果做亚像素精化（更慢但更准） */
    double greediness;      /* 贪婪系数 0~1，默认 0.9：越大越早淘汰差候选、越快，
                               但极端遮挡时可能漏检；设 0 表示关闭该启发式（最慢最稳） */
} VA_ShapeParam;

/* ---------------------------------------------------------------------------
 * 单个匹配结果。(cx, cy) 是匹配目标中心在原图坐标系中的位置。
 * -------------------------------------------------------------------------*/
typedef struct VA_ShapeMatch {
    double cx;              /* 中心 X（原图坐标） */
    double cy;              /* 中心 Y（原图坐标） */
    double angle;           /* 旋转角度（度） */
    double scale;           /* 实际缩放 */
    double score;           /* 得分 0~1，1 为完全一致 */
    double width;           /* 模板宽 × scale */
    double height;          /* 模板高 × scale */
} VA_ShapeMatch;

/* ---------------------------------------------------------------------------
 * 模型特征点：模板边缘点的位置（相对模板中心）与单位梯度方向。
 * 供上层把「模板特征结构」画出来做可视化调试。
 * -------------------------------------------------------------------------*/
typedef struct VA_ShapePoint {
    double x;               /* 相对模板中心的 X */
    double y;               /* 相对模板中心的 Y */
    double dx;              /* 单位梯度方向 X 分量 */
    double dy;              /* 单位梯度方向 Y 分量 */
} VA_ShapePoint;

/* ---------------------------------------------------------------------------
 * 训练参数。特征点越多越稳但越慢；空间稀疏化可以在「点数」和「分布均匀度」之间取平衡。
 * -------------------------------------------------------------------------*/
typedef struct VA_ShapeTrainParam {
    double contrast;         /* 边缘提取低阈值（梯度幅值下限），典型 20~60；<=0 时取 30 */
    int    max_features;     /* 最多保留的特征点数；<=0 时取 1500。越少越快 */
    double min_feature_dist; /* 特征点最小间距（模板像素）；<=0 时取 2.0。
                                空间稀疏化：同一网格内只保留梯度最强的一点，
                                保证特征点均匀分布（比按幅值等间隔抽取更稳）。 */
} VA_ShapeTrainParam;

/* ---------------------------------------------------------------------------
 * 用 ROI 区域训练形状模型（默认参数版本）。
 *   roi      : [x, y, w, h]，传 NULL 或 w/h <= 0 表示整图
 *   contrast : 边缘提取的低阈值；<=0 时取 30
 * 返回 VA_OK / VA_ERR_ARG / VA_ERR_INTERNAL。
 * -------------------------------------------------------------------------*/
VA_API int VA_CALL va_create_shape_model(void* handle,
                                          const VA_Image* gray,
                                          const int* roi,
                                          double contrast);

/* 可指定特征点数与稀疏间距的训练版本。param 为 NULL 时等价于默认参数。 */
VA_API int VA_CALL va_create_shape_model_ex(void* handle,
                                             const VA_Image* gray,
                                             const int* roi,
                                             const VA_ShapeTrainParam* param);

/* 查询当前模型信息；尚未训练时返回 VA_ERR_NO_TEMPLATE。 */
VA_API int VA_CALL va_get_shape_model_info(void* handle,
                                            int* out_point_count,
                                            double* out_width,
                                            double* out_height);

/* 释放形状模型。 */
VA_API int VA_CALL va_clear_shape_model(void* handle);

/* 导出模型的特征点（边缘点 + 梯度方向），用于界面显示模板特征结构。
   out_points 由调用方分配，max_count 为其容量；out_count 返回实际写入个数。
   特征点按梯度幅值从大到小排列（与匹配时的评估顺序一致）。 */
VA_API int VA_CALL va_get_shape_model_points(void* handle,
                                              VA_ShapePoint* out_points,
                                              int max_count,
                                              int* out_count);

/* ---------------------------------------------------------------------------
 * 在 gray 中搜索形状模型。
 *   search_roi  : [x, y, w, h] 限制「模板中心」的可出现范围，传 NULL 或 w/h<=0 表示整图
 *   out_matches : 调用方分配的数组
 *   max_count   : 数组容量
 *   out_count   : 实际写入个数
 * 返回 VA_OK（即使找到 0 个也返回 OK，用 out_count 判断）/ VA_ERR_NO_TEMPLATE 等。
 * -------------------------------------------------------------------------*/
VA_API int VA_CALL va_find_shape_model(void* handle,
                                        const VA_Image* gray,
                                        const VA_ShapeParam* param,
                                        const int* search_roi,
                                        VA_ShapeMatch* out_matches,
                                        int max_count,
                                        int* out_count);

#ifdef __cplusplus
}
#endif

#endif /* VA_SHAPE_H */
