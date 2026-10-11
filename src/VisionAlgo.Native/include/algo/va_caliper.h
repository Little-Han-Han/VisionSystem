/* ---------------------------------------------------------------------------
 * 卡尺工具（直线 / 圆）：沿扫描路径等分投放投影窗口 → 一维滤波 + 差分找边 →
 * 抛物线亚像素 → 最小二乘拟合 → 离群点剔除一轮 → 残差 RMS。
 *
 * 坐标与角度约定（与全库一致，见架构文档共享知识 14）：
 *   · 所有几何量（线段端点、圆心、内外径、投影长度）均为【图像坐标 / 图像像素】；
 *   · 角度单位为度，0° = +X 轴，顺时针为正（图像 y 轴向下）；
 *   · 输出点数组中的 px1/py1/px2/py2 是投影窗口两端，供上层可视化直接绘制，
 *     因此【未命中的卡尺也会写出窗口】，只有 valid=0 表示这一格没找到边。
 *
 * 极性定义（架构文档 B7）：沿【投影正方向】观察灰度变化。
 *   · 直线：投影正方向 = 扫描方向 (P1→P2) 顺时针旋转 90° 得到的法线；
 *   · 圆  ：投影正方向 = 径向朝外（由内径指向外径）。
 * 沿该方向灰度【由暗变亮】为 DarkToLight，反之为 LightToDark，Any 表示两者都要。
 * -------------------------------------------------------------------------*/
#ifndef VA_CALIPER_H
#define VA_CALIPER_H

#include "va_export.h"
#include "va_types.h"

#ifdef __cplusplus
extern "C" {
#endif

/* 边缘极性：沿投影正方向的灰度跳变方向 */
#define VA_POLARITY_ANY           0   /* 不过滤极性 */
#define VA_POLARITY_DARK_TO_LIGHT 1   /* 由暗变亮（梯度为正） */
#define VA_POLARITY_LIGHT_TO_DARK 2   /* 由亮变暗（梯度为负） */

/* 单个卡尺命中多个候选边缘时的选边策略 */
#define VA_SELECT_STRONGEST 0   /* 取跳变幅值最大的一个 */
#define VA_SELECT_FIRST     1   /* 取沿投影方向最靠前的一个 */
#define VA_SELECT_LAST      2   /* 取沿投影方向最靠后的一个 */

/* ---------------------------------------------------------------------------
 * 卡尺公共参数。
 * 直线模式只用「线段」字段（x1/y1/x2/y2），圆模式只用「圆环」字段
 * （cx/cy/inner_radius/outer_radius/start_angle/angle_span），另一组字段忽略。
 * -------------------------------------------------------------------------*/
typedef struct VA_CaliperParam {
    /* --- 直线模式 ROI --- */
    double x1, y1;              /* 扫描线段起点（图像坐标） */
    double x2, y2;              /* 扫描线段终点（图像坐标） */

    /* --- 圆模式 ROI --- */
    double cx, cy;              /* 圆心（图像坐标） */
    double inner_radius;        /* 内径；<=0 时按投影长度退化处理 */
    double outer_radius;        /* 外径；<= 内径 时取 内径 + projection_length */
    double start_angle;         /* 起始角（度，0=+X 轴，顺时针为正） */
    double angle_span;          /* 角度跨度（度）；>360 截断为 360，<=0 视为 360 */

    /* --- 公共 --- */
    int    caliper_count;       /* 卡尺个数（默认：直线 10 / 圆 20），必须 >= 1 */
    double projection_length;   /* 投影窗口长度（沿法线 / 径向），px，默认 20 */
    int    polarity;            /* 极性：见 VA_POLARITY_*，默认 0(Any) */
    double edge_threshold;      /* 边缘跳变幅值阈值（灰度差），默认 30 */
    int    filter_half_width;   /* 一维均值滤波半宽，<1 表示不滤波，默认 2 */
    int    select_mode;         /* 选边策略：见 VA_SELECT_*，默认 0(Strongest) */
    int    min_edge_count;      /* 有效边缘点下限（直线 3 / 圆 5）；<1 时按拟合下限 */
    int    reject_outliers;     /* 1=首轮拟合后按残差剔除离群点一轮，默认 1 */
    double outlier_tolerance;   /* 残差剔除阈值 px，<=0 表示不剔除，默认 2.0 */
} VA_CaliperParam;

/* ---------------------------------------------------------------------------
 * 单个卡尺的输出：投影窗口两端（供可视化）+ 命中边缘点。
 * 未命中时 valid=0，x/y/strength 置 0，但窗口两端仍然有效。
 * -------------------------------------------------------------------------*/
typedef struct VA_CaliperPoint {
    double px1, py1;    /* 投影窗口起点（内侧端点，图像坐标） */
    double px2, py2;    /* 投影窗口终点（外侧端点，图像坐标） */
    double x, y;        /* 命中边缘点（亚像素，图像坐标） */
    double strength;    /* 边缘强度（平滑后梯度幅值，与 edge_threshold 同量纲） */
    int    valid;       /* 1=命中，0=未命中 */
} VA_CaliperPoint;

/* ---------------------------------------------------------------------------
 * 直线卡尺：沿扫描线段法向等分投放 caliper_count 个卡尺 → 找边 → 拟合直线。
 *
 * out_points : 调用方分配，容量 >= max_count；实际写入 caliper_count 个
 *              （未命中也写入，仅 valid=0）
 * max_count  : out_points 容量；小于 caliper_count 时返回 VA_ERR_SIZE
 * out_count  : 实际写入个数（成功与 VA_ERR_NO_EDGE 时均等于 caliper_count）
 * out_line   : 拟合线段两端（图像坐标），为扫描线段在拟合直线上的投影
 * out_rms    : 直线度残差 RMS（px）
 *
 * 返回 VA_OK / VA_ERR_ARG / VA_ERR_TYPE / VA_ERR_SIZE / VA_ERR_NO_EDGE / VA_ERR_INTERNAL
 * 有效边缘点数 < min_edge_count 时返回 VA_ERR_NO_EDGE，
 * 并把 lastError 写成「有效边缘点 N < M」供 C# 侧直接作为摘要文本。
 * -------------------------------------------------------------------------*/
VA_API int VA_CALL va_line_caliper(void* handle,
                                   const VA_Image* gray,
                                   const VA_CaliperParam* param,
                                   VA_CaliperPoint* out_points,
                                   int max_count,
                                   int* out_count,
                                   VA_Line* out_line,
                                   double* out_rms);

/* ---------------------------------------------------------------------------
 * 圆卡尺：沿圆周等分投放 caliper_count 个径向卡尺 → 找边 → 代数最小二乘拟合圆。
 *
 * out_circle : 圆心 / 半径；residual 字段回填圆度 RMS（px）
 * out_rms    : 圆度残差 RMS（px），与 out_circle->residual 同值
 *
 * 返回码与失败语义同 va_line_caliper（拟合下限为 3 点）。
 * -------------------------------------------------------------------------*/
VA_API int VA_CALL va_circle_caliper(void* handle,
                                     const VA_Image* gray,
                                     const VA_CaliperParam* param,
                                     VA_CaliperPoint* out_points,
                                     int max_count,
                                     int* out_count,
                                     VA_Circle* out_circle,
                                     double* out_rms);

#ifdef __cplusplus
}
#endif

#endif /* VA_CALIPER_H */
