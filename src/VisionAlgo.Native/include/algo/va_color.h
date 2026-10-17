/* ---------------------------------------------------------------------------
 *  颜色提取：在指定颜色空间内做阈值分割 → 形态学清理 → 连通域统计 → 区域过滤。
 *
 *  这是全库唯一要求彩色输入的算法：src 必须为 3 通道（BGR 字节序）或 4 通道
 *  （BGRA，内部自动丢弃 Alpha）。传入 1 通道灰度图会返回 VA_ERR_TYPE。
 *
 *  颜色空间与分量范围：
 *   · space = 0（BGR）：low/high 的 [0..2] 依次是 B、G、R，取值 0~255；
 *   · space = 1（HSV）：low/high 的 [0..2] 依次是 H、S、V。
 *     ⚠ OpenCV 的 H 通道范围是 0~179（不是 0~360），S / V 是 0~255。
 *       若上层按 0~360 给 H，本库会自动除以 2 换算（见实现中的换算规则）；
 *       建议上层直接按 0~179 给，避免歧义。
 *     当 low[0] > high[0] 时按 H 通道环绕处理（跨 0° 的红色区间），
 *     内部拆成 [low[0], 179] 与 [0, high[0]] 两段再取并集。
 *   · low[3] / high[3] 未使用，保留以便后续扩展。
 *
 *  处理顺序固定：颜色空间转换 → inRange → 形态学清理 → 连通域统计 → 面积过滤。
 * -------------------------------------------------------------------------*/
#ifndef VA_COLOR_H
#define VA_COLOR_H

#include "va_export.h"
#include "va_types.h"

#ifdef __cplusplus
extern "C" {
#endif

/* 颜色空间 */
#define VA_COLOR_SPACE_BGR 0    /* BGR 三通道独立阈值 */
#define VA_COLOR_SPACE_HSV 1    /* HSV 阈值（H: 0~179, S/V: 0~255） */

/* 分割后的形态学清理算子 */
#define VA_COLOR_MORPH_NONE  0
#define VA_COLOR_MORPH_OPEN  1  /* 去掉孤立小点 */
#define VA_COLOR_MORPH_CLOSE 2  /* 填补内部空洞 */

/* ---------------------------------------------------------------------------
 * 颜色提取参数。哨兵值语义：morph_ksize < 3 或 morph_op = 0 → 不做形态学。
 * -------------------------------------------------------------------------*/
typedef struct VA_ColorParam {
    int space;        /* 颜色空间：见 VA_COLOR_SPACE_* */
    int low[4];       /* 下界：B/H, G/S, R/V, (未用) */
    int high[4];      /* 上界：B/H, G/S, R/V, (未用) */
    int morph_op;     /* 形态学清理算子：见 VA_COLOR_MORPH_* */
    int morph_ksize;  /* 形态学核边长；<3 则不做 */
} VA_ColorParam;

/* 命中区域的统计信息 */
typedef struct VA_ColorStat {
    double pixel_count;             /* 命中像素数 */
    double ratio;                   /* 命中像素 / 总像素，0~1 */
    double mean_b, mean_g, mean_r;  /* 命中区域内的平均颜色（BGR 字节序） */
} VA_ColorStat;

/* ---------------------------------------------------------------------------
 * 颜色提取。
 *
 * src       : 输入图像，channels = 3 (BGR) 或 4 (BGRA)；1 通道返回 VA_ERR_TYPE
 * dst_mask  : 输出的 0/255 单通道掩膜；可为 NULL 表示只要统计与 blob
 * param     : 颜色阈值与形态学参数
 * min_area  : 连通域面积下限（像素）
 * max_area  : 连通域面积上限（像素）；<= 0 表示不设上限
 * out_blobs : 调用方分配的 blob 数组；可为 NULL
 * max_count : out_blobs 容量；为 0 时只做统计
 * out_count : 实际写入 out_blobs 的个数；可为 NULL
 * out_stat  : 命中统计；可为 NULL
 *
 * 返回 VA_OK / VA_ERR_ARG / VA_ERR_TYPE / VA_ERR_SIZE / VA_ERR_INTERNAL
 * -------------------------------------------------------------------------*/
VA_API int VA_CALL va_color_extract(void* handle,
                                    const VA_Image* src,
                                    VA_Image* dst_mask,
                                    const VA_ColorParam* param,
                                    double min_area,
                                    double max_area,
                                    VA_Blob* out_blobs,
                                    int max_count,
                                    int* out_count,
                                    VA_ColorStat* out_stat);

#ifdef __cplusplus
}
#endif

#endif /* VA_COLOR_H */
