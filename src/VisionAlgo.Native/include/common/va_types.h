/* 公共数据类型：与 C# 侧结构体一一对应（LayoutKind.Sequential）。 */
#ifndef VA_TYPES_H
#define VA_TYPES_H

#ifdef __cplusplus
extern "C" {
#endif

/* ---------------------------------------------------------------------------
 * 图像描述：直接对应 cv::Mat 的内存布局。
 * C# 侧 LockBits 后把 Scan0 交给 data，实现零拷贝传入。
 * channels: 1 = Gray, 3 = BGR, 4 = BGRA
 * stride  : 每行字节数，可含行尾填充；为 0 时按 width * channels 处理
 * -------------------------------------------------------------------------*/
typedef struct VA_Image {
    unsigned char* data;
    int   width;
    int   height;
    int   channels;
    int   stride;
} VA_Image;

/* ---------------------------------------------------------------------------
 * 预处理参数：按 灰度化 → 高斯 → 二值化 → 形态学 的顺序执行，
 * 各步骤可通过参数关闭（< 3 的核尺寸、< 0 的阈值等）。
 * -------------------------------------------------------------------------*/
typedef struct VA_PreprocessParam {
    int to_gray;        /* 1 = 彩色输入时转灰度 */
    int blur_ksize;     /* 高斯核边长（奇数）；< 3 表示不模糊 */
    int threshold;      /* 二值化阈值 0~255；< 0 表示不二值化 */
    int invert;         /* 1 = 二值结果反转 */
    int morph_op;       /* 0 无, 1 开运算, 2 闭运算 */
    int morph_ksize;    /* 形态学核边长（奇数） */
} VA_PreprocessParam;

/* 连通域描述 */
typedef struct VA_Blob {
    double cx;
    double cy;
    double area;
    double width;
    double height;
    double angle;
} VA_Blob;

/* 圆拟合结果 */
typedef struct VA_Circle {
    double cx;
    double cy;
    double radius;
    double residual;    /* 轮廓点到圆周的平均绝对偏差，越小越圆 */
} VA_Circle;

/* 直线拟合结果（已按轮廓投影裁剪为线段两端） */
typedef struct VA_Line {
    double x1, y1, x2, y2;
} VA_Line;

/* 模板匹配结果：归一化相关系数法 */
typedef struct VA_MatchResult {
    double x;           /* 匹配区域左上角 */
    double y;
    double width;
    double height;
    double score;       /* 0~1，越接近 1 越相似 */
    double angle;       /* 预留；当前实现只支持平移匹配，固定 0 */
} VA_MatchResult;

#ifdef __cplusplus
}
#endif

#endif /* VA_TYPES_H */
