/* ============================================================================
 *  VisionAlgo.Native —— 视觉算法原生库（C++ / OpenCV）聚合头文件
 *
 *  只包含这一个头文件即可使用全部功能。也可按需单独包含子头文件：
 *    common/va_export.h      导出宏与调用约定
 *    common/va_error.h       错误码
 *    common/va_types.h       公共结构体
 *    common/va_lifecycle.h   库与上下文生命周期
 *    algo/va_preprocess.h    预处理
 *    algo/va_blob.h          连通域分析
 *    algo/va_fit.h           圆 / 直线拟合
 *    algo/va_match.h         模板训练与匹配
 *    algo/va_measure.h       清晰度与边缘计数
 *
 *  设计约定（C# 侧 P/Invoke 依赖这些约定，改动需同步）：
 *   1. 全部 extern "C" 导出，无名称修饰
 *   2. 调用约定 __stdcall（x64 下被忽略，但保持一致）
 *   3. 图像用裸指针 + 尺寸 + 通道 + 行字节数描述，对应 cv::Mat 布局
 *   4. 输出缓冲区由调用方分配，库内不做跨模块分配
 * ==========================================================================*/
#ifndef VISION_ALGO_H
#define VISION_ALGO_H

#include "va_export.h"
#include "va_error.h"
#include "va_types.h"
#include "va_lifecycle.h"

#include "algo/va_preprocess.h"
#include "algo/va_blob.h"
#include "algo/va_fit.h"
#include "algo/va_match.h"
#include "algo/va_measure.h"

#endif /* VISION_ALGO_H */
