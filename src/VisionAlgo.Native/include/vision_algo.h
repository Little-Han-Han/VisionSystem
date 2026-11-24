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
 *    algo/va_match.h         模板训练与匹配（灰度归一化相关，支持角度 / 缩放 / 多目标）
 *    algo/va_barcode.h       条码 / 二维码识别与定位（zxing-cpp，支持多种码制、一次多个）
 *    algo/va_shape.h         基于边缘轮廓的形状匹配（角度 / 缩放 / 金字塔 / ROI）
 *    algo/va_measure.h       清晰度与边缘计数
 *    algo/va_caliper.h       直线 / 圆卡尺（投影找边 + 拟合 + 离群剔除）
 *    algo/va_camera.h        本机摄像头（枚举 / 打开 / 抓帧 / 关闭）
 *    algo/va_filter.h        图像滤波（平滑 / 锐化 / 形态学后处理）
 *    algo/va_color.h         颜色提取（BGR / HSV 阈值分割 + 连通域）
 *    algo/va_classify.h      分类器（轮廓特征 + SVM / KNN，OpenCV ml）
 *    algo/va_ocr.h           传统算法 OCR（连通域分割 + 模板匹配）
 *    algo/va_anomaly.h       无监督缺陷检测（PaDiM 训练+推理 / EfficientAD 推理）
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

#include "algo/va_calib.h"
#include "algo/va_preprocess.h"
#include "algo/va_blob.h"
#include "algo/va_fit.h"
#include "algo/va_match.h"
#include "algo/va_barcode.h"
#include "algo/va_shape.h"
#include "algo/va_measure.h"
#include "algo/va_caliper.h"
#include "algo/va_camera.h"
#include "algo/va_filter.h"
#include "algo/va_color.h"
#include "algo/va_classify.h"
#include "algo/va_ocr.h"
#include "algo/va_anomaly.h"

#endif /* VISION_ALGO_H */
