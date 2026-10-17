/* ---------------------------------------------------------------------------
 *  图像滤波：平滑 / 锐化 / 自定义卷积核，后接可选形态学处理。
 *
 *  执行顺序固定为「先滤波 → 后形态学」，任一步骤都可以用哨兵值关掉。
 *
 *  输入输出约定：
 *   · 支持 1 通道（灰度）与 3 通道（BGR）输入，输出通道数必须与输入一致；
 *   · 输出图像缓冲由调用方按 src 的尺寸与通道数分配，库内用 CopyOut 逐行拷回；
 *   · 返回 VA_OK / VA_ERR_ARG / VA_ERR_TYPE / VA_ERR_SIZE / VA_ERR_INTERNAL。
 * -------------------------------------------------------------------------*/
#ifndef VA_FILTER_H
#define VA_FILTER_H

#include "va_export.h"
#include "va_types.h"

#ifdef __cplusplus
extern "C" {
#endif

/* 滤波类型 */
#define VA_FILTER_NONE      0   /* 原样输出（只做可选形态学） */
#define VA_FILTER_BOX       1   /* 均值滤波 */
#define VA_FILTER_GAUSS     2   /* 高斯滤波 */
#define VA_FILTER_MEDIAN    3   /* 中值滤波（去椒盐噪声） */
#define VA_FILTER_BILATERAL 4   /* 双边滤波（保边去噪） */
#define VA_FILTER_USM       5   /* 锐化：USM（高斯模糊后反相叠加） */
#define VA_FILTER_CUSTOM    6   /* 自定义 3x3 卷积核（固定锐化核） */

/* 形态学算子，对应 cv::MorphTypes */
#define VA_MORPH_NONE      0
#define VA_MORPH_DILATE    1
#define VA_MORPH_ERODE     2
#define VA_MORPH_OPEN      3
#define VA_MORPH_CLOSE     4
#define VA_MORPH_GRADIENT  5
#define VA_MORPH_TOPHAT    6
#define VA_MORPH_BLACKHAT  7

/* 结构化元素形状，对应 cv::MorphShapes */
#define VA_MORPH_SHAPE_RECT    0
#define VA_MORPH_SHAPE_ELLIPSE 1
#define VA_MORPH_SHAPE_CROSS   2

/* ---------------------------------------------------------------------------
 * 滤波参数。哨兵值语义：ksize < 3 → 用各算子的默认核；morph_ksize < 3 或
 * morph_op = 0 → 不做形态学；sigma <= 0 → 由 ksize 自动推导。
 * -------------------------------------------------------------------------*/
typedef struct VA_FilterParam {
    int    type;        /* 滤波类型：见 VA_FILTER_* */
    int    ksize;       /* 核边长；会被修正为 >=3 的奇数 */
    double sigma;       /* 高斯 / 双边的 sigma；<=0 时自动推导 */
    double amount;      /* 锐化强度（USM），典型 1.0；0 = 不锐化 */
    int    morph_op;    /* 形态学算子：见 VA_MORPH_* */
    int    morph_shape; /* 结构化元素形状：见 VA_MORPH_SHAPE_* */
    int    morph_ksize; /* 形态学核边长；<3 则不做形态学 */
    int    iterations;  /* 形态学迭代次数；<1 按 1 */
} VA_FilterParam;

/* ---------------------------------------------------------------------------
 * 图像滤波。
 *
 * src  : 输入图像，channels = 1 或 3
 * dst  : 输出图像，尺寸与通道数必须与 src 一致，缓冲由调用方分配
 * param: 滤波参数
 * -------------------------------------------------------------------------*/
VA_API int VA_CALL va_filter(void* handle,
                             const VA_Image* src,
                             VA_Image* dst,
                             const VA_FilterParam* param);

#ifdef __cplusplus
}
#endif

#endif /* VA_FILTER_H */
