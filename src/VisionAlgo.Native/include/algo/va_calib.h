/* 相机标定：棋盘格/圆点角点检测 + 内参与畸变系数标定。
 *
 * 只做"算法"这一段，采集多少张图、怎么提示用户由 C# 侧负责。
 * 典型流程：
 *   1) 对每张图调 va_calib_find_chessboard 拿到内角点（行数×列数）→ 失败就换图
 *   2) 把所有图的内角点一起交给 va_calib_calibrate，得到 K / dist / RMS
 */
#ifndef VA_CALIB_H
#define VA_CALIB_H

#include "va_types.h"

#ifdef __cplusplus
extern "C" {
#endif

/* VA_CALIB_FLAG_* ：传给 va_calib_calibrate 的 flags（可与 OpenCV 的 CALIB_* 对齐） */
#define VA_CALIB_FLAG_ASYMMETRIC_GRID  (1 << 0)   /* 非对称圆点网格（如 4x11） */

/* 在灰度图上找棋盘格内角点。
 *   cols, rows  内角点列数/行数（如 9x6 棋盘 = 8x5 个内角点）
 *   out_xy      输出 [x0,y0,x1,y1,...]，按行优先
 *   cap         输出数组能放多少个点（个数，不是 double 个数）
 *   out_count   实际找到的点数；找不到时为 0 并返回 VA_ERR_NO_EDGE
 * 返回：VA_OK / VA_ERR_ARG / VA_ERR_NO_EDGE / VA_ERR_SIZE */
VA_API int VA_CALL va_calib_find_chessboard(void* handle, const VA_Image* gray, int cols, int rows,
                                            int pattern, double* out_xy, int cap, int* out_count);

/* pattern：0 = 棋盘格，1 = 对称圆点网格，2 = 非对称圆点网格 */

/* 用多张图的角点标定相机。
 *   xy         所有点首尾相接（第 i 张图占 counts[i] 个点，每点 2 个 double）
 *   counts     每张图的点数
 *   n_images   张数（>= 3 才有意义）
 *   width/height 图像分辨率
 *   cols/rows  每张图的内角点列数/行数（点顺序必须与该网格一致）
 *   flags      VA_CALIB_FLAG_*
 *   out_k      内参矩阵 3x3，按行 9 个 double
 *   out_dist   畸变系数，至少 5 个（k1,k2,p1,p2,k3）
 *   out_rms    重投影误差 RMS（像素）
 * 返回：VA_OK / VA_ERR_ARG / VA_ERR_INTERNAL（标定失败） */
VA_API int VA_CALL va_calib_calibrate(void* handle, const double* xy, const int* counts, int n_images,
                                      int cols, int rows, int width, int height, int flags,
                                      double* out_k, double* out_dist, double* out_rms);

#ifdef __cplusplus
}
#endif

#endif /* VA_CALIB_H */
