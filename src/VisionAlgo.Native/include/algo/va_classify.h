/* ---------------------------------------------------------------------------
 *  分类器：基于轮廓特征的传统机器学习分类器（OpenCV ml 模块）。
 *
 *  无第三方依赖，无 contrib：特征提取走「Otsu 二值化 → 取最大轮廓 → 计算特征」，
 *  模型用 cv::ml::SVM（C_SVC，支持线性 / 多项式 / RBF 核）或 cv::ml::KNearest。
 *
 *  典型调用顺序：
 *      va_clf_create       设置模型类型与特征类型
 *      va_clf_add_sample   反复累积带标签样本（每样本一张灰度 ROI 图）
 *      va_clf_train        训练（样本数 < 2 或类别数 < 2 时返回 VA_ERR_ARG）
 *      va_clf_predict      预测
 *      va_clf_save / load  模型持久化
 *      va_clf_clear        清空样本与模型
 *
 *  特征说明：
 *   · feature_type = 0：Hu 不变矩 7 维。Hu 矩原始值跨度可达 1e-7 量级，
 *     直接送进 SVM / KNN 会完全被大尺度分量支配，因此内部统一做对数变换
 *     -sign(h) * log10(|h|) 后再送入模型。
 *   · feature_type = 1：几何特征 5 维 = 面积、周长、圆度、长宽比、填充度。
 *   · feature_type = 2：上述两组拼接，共 12 维。
 *
 *  状态挂载：样本与模型保存在 va::Context 上（一个句柄 = 一条流程）。
 *
 *  out_score 语义：
 *   · SVM：决策函数值（有符号距离，绝对值越大越可信，符号对应二类的一侧）；
 *   · KNN：K 个近邻中最高票类别的占比，0~1。
 *
 *  ⚠ va_clf_save / va_clf_load 路径为 UTF-8 字符串，格式为 OpenCV 默认的 XML。
 *    Windows 下建议路径使用 ASCII 字符，避免 OpenCV FileStorage 的编码问题。
 *
 *  返回码：VA_OK / VA_ERR_ARG / VA_ERR_TYPE / VA_ERR_SIZE / VA_ERR_NO_TEMPLATE
 *          / VA_ERR_INTERNAL
 * -------------------------------------------------------------------------*/
#ifndef VA_CLASSIFY_H
#define VA_CLASSIFY_H

#include "va_export.h"
#include "va_types.h"

#ifdef __cplusplus
extern "C" {
#endif

/* 模型类型 */
#define VA_CLF_SVM 0
#define VA_CLF_KNN 1

/* 特征类型 */
#define VA_FEAT_HU     0    /* Hu 矩，7 维 */
#define VA_FEAT_GEOM   1    /* 几何特征，5 维 */
#define VA_FEAT_HYBRID 2    /* Hu + 几何，12 维 */

/* ---------------------------------------------------------------------------
 * 分类器参数。哨兵值语义：C <= 0 / gamma <= 0 → 用 OpenCV 默认值；
 * knn_k < 1 → 用 5。
 * -------------------------------------------------------------------------*/
typedef struct VA_ClassParam {
    int    model_type;    /* 0 = SVM, 1 = KNN：见 VA_CLF_* */
    int    feature_type;  /* 0 = Hu, 1 = 几何, 2 = 混合：见 VA_FEAT_* */
    int    svm_kernel;    /* 0 LINEAR, 1 POLY, 2 RBF（对应 cv::ml::SVM::KernelTypes） */
    double C, gamma;      /* SVM 参数；<=0 用 OpenCV 默认 */
    int    knn_k;         /* KNN 的 K；<1 用 5 */
} VA_ClassParam;

/* 创建 / 重设分类器：清空已有样本与模型，并保存参数供 va_clf_train 使用 */
VA_API int VA_CALL va_clf_create(void* handle, const VA_ClassParam* param);

/* 累积一个训练样本：从灰度图取最大轮廓并提取特征；label 可为任意整数 */
VA_API int VA_CALL va_clf_add_sample(void* handle, const VA_Image* gray, int label);

/* 取当前累积的样本数 */
VA_API int VA_CALL va_clf_sample_count(void* handle, int* out_count);

/* 训练；样本不足时返回 VA_ERR_ARG，详情见 va_get_last_error */
VA_API int VA_CALL va_clf_train(void* handle);

/* 预测；未训练时返回 VA_ERR_NO_TEMPLATE */
VA_API int VA_CALL va_clf_predict(void* handle, const VA_Image* gray,
                                  int* out_label, double* out_score);

/* 保存模型到 XML 文件（OpenCV FileStorage 默认格式） */
VA_API int VA_CALL va_clf_save(void* handle, const char* path);

/* 从 XML 文件载入模型；会同时更新内部的模型类型 */
VA_API int VA_CALL va_clf_load(void* handle, const char* path);

/* 清空样本与模型 */
VA_API int VA_CALL va_clf_clear(void* handle);

#ifdef __cplusplus
}
#endif

#endif /* VA_CLASSIFY_H */
