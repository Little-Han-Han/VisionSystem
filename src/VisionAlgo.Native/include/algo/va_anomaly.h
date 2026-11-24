/* ---------------------------------------------------------------------------
 *  无监督缺陷检测（异常检测）：PaDiM / EfficientAD 两类代表模型，OpenVINO 推理。
 *
 *  【整体约定】一个「模型目录」= 一个可加载的异常检测模型，目录结构固定：
 *
 *      <模型目录>\
 *          model.json          元数据（本库写，本库读；C# 只读目录名）
 *          padim.bin           PaDiM 统计量（均值 + Cholesky 因子）
 *          extractor.xml/.bin  PaDiM 特征提取器（可选：OpenVINO IR；缺省用内置描述子）
 *          teacher.xml/.bin    EfficientAD 教师网络（必填）
 *          student.xml/.bin    EfficientAD 学生网络（必填）
 *          ae.xml/.bin         EfficientAD 自编码器（可选：缺省只用学生-教师分支）
 *          ead_norm.bin        EfficientAD 归一化统计量（训练集上的均值/标准差）
 *
 *  【为什么模型目录是"数据"而不是"路径参数"】现场部署时模型目录随软件一起发，
 *  参数里只存一个目录名；换型号在界面上换下拉项即可，不需要重新选路径。
 *
 *  【两类模型的分工】
 *    PaDiM        —— 本库可**在软件内训练**（逐位置多元高斯，无需反向传播），
 *                    也可用外部导出的特征提取器（IR）提升表达能力。
 *    EfficientAD  —— 学生-教师 + 自编码器，训练需要反向传播，**不在本库内训练**；
 *                    本库负责加载导出的 IR 三件套并推理。
 *                    导出见 docs/算法/异常检测-模型目录与训练.md（第 4 节）。
 *
 *  返回码沿用全局约定（VA_OK / VA_ERR_ARG / VA_ERR_SIZE / VA_ERR_NO_TEMPLATE / VA_ERR_INTERNAL）。
 * -------------------------------------------------------------------------*/
#ifndef VA_ANOMALY_H
#define VA_ANOMALY_H

#include "va_export.h"
#include "va_types.h"

#ifdef __cplusplus
extern "C" {
#endif

/* 模型类型（写入 model.json 的 kind 字段） */
#define VA_ANOM_KIND_PADIM       0
#define VA_ANOM_KIND_EFFICIENTAD 1

/* 特征提取后端（写入 model.json 的 feat 字段） */
#define VA_ANOM_FEAT_HANDCRAFT   0   /* 内置描述子：无需任何预训练模型 */
#define VA_ANOM_FEAT_OPENVINO    1   /* OpenVINO IR：输出 4D 特征图 [1,C,h,w] */

/* 单次推理的缺陷统计（坐标均为**原图像素**，已从 ROI 还原）。
 *
 * 【分数口径】score 全部是「相对阈值」的倍数：
 *   1.0 = 恰好等于判定阈值，>1 即判 NG。这样换模型后阈值不同也不影响读数习惯。
 *   原始马氏距离 / 重建误差保留在 raw_* 里，供调阈值时对照。 */
typedef struct VA_AnomStat {
    float max_score;        /* ROI 内最大异常分（相对阈值倍数） */
    float mean_score;       /* ROI 内平均异常分（相对阈值倍数） */
    float raw_max;          /* 最大异常原始值（PaDiM = 马氏距离；EfficientAD = 归一化后误差） */
    float threshold;        /* 本次判定实际使用的阈值（原始值刻度） */

    float area_ratio;       /* 超阈值像素占 ROI 的比例 0~1 */
    int   area_px;          /* 超阈值像素数 */
    int   defect_count;     /* 缺陷连通域个数（已按最小面积过滤） */

    int   max_x;            /* 最大分点位（原图 X） */
    int   max_y;            /* 最大分点位（原图 Y） */

    float box_x;            /* 最大缺陷的外接框（原图坐标；无缺陷时全 0） */
    float box_y;
    float box_w;
    float box_h;

    int   roi_x;            /* 实际分析区域（原图坐标） */
    int   roi_y;
    int   roi_w;
    int   roi_h;
} VA_AnomStat;

/* ---------------- 训练（仅 PaDiM）----------------
 *
 * 【流程】begin → add（每张 OK 图一次）→ finish
 *   finish 时算逐位置均值 / 协方差 / Cholesky 因子 + 阈值，写 <out_dir>\model.json 与 padim.bin。
 *   add 的 ROI 建议全部一致（同一工位同一视野）；不一致时统计量会被几何差异污染，
 *   这不是 bug 而是无监督方法的固有前提（见 docs/算法/无监督缺陷检测技术方案.md）。
 *
 *   feat_kind = VA_ANOM_FEAT_OPENVINO 时 extractor 必须是 IR 的 .xml 路径；
 *   HANDCRAFT 时 extractor 传空。
 */

/* 开始训练。input_size = 分析分辨率（建议 256，必须是 16 的倍数）。 */
VA_API int VA_CALL va_anom_train_begin(void* handle, const char* out_dir, const char* extractor,
                                       int feat_kind, int input_size);

/* 追加一张 OK 样本。roi 传 0 表示整图（int[4] = x,y,w,h）。 */
VA_API int VA_CALL va_anom_train_add(void* handle, const VA_Image* img, const int* roi);

/* 结束训练并落盘。out_summary 可为 0。 */
VA_API int VA_CALL va_anom_train_finish(void* handle, char* out_summary, int cap);

/* 已累计样本数（训练中可查）。 */
VA_API int VA_CALL va_anom_train_count(void* handle, int* out_count);

/* 放弃本次训练（清空已累计统计量，不落盘）。 */
VA_API int VA_CALL va_anom_train_cancel(void* handle);

/* ---------------- 推理 ---------------- */

/* 加载模型目录（读 model.json + 权重）。device 为空按 "CPU"。 */
VA_API int VA_CALL va_anom_create(void* handle, const char* model_dir, const char* device);

/* 推理一帧。
 *   roi         : int[4] = x,y,w,h（原图坐标）；传 0 = 整图
 *   threshold   : 原始值刻度的判定阈值；<= 0 表示用模型自带阈值
 *   out_stat    : 缺陷统计（必填）
 *   out_map     : 可选热图缓冲（长度 = roi_w × roi_h），写入**相对阈值倍数**（1.0 = 阈值），
 *                 调用方负责按 0~2 上色；传 0 则不算热图（只出统计，快）
 *   out_w/out_h : 回填热图实际尺寸
 *   out_mask    : 可选缺陷掩膜（长度 = 同上，0/1），与热图同尺寸
 *   min_area_px : 缺陷连通域最小面积（小于它的连通域不计入 defect_count，也不画框）
 */
VA_API int VA_CALL va_anom_run(void* handle, const VA_Image* img, const int* roi,
                               float threshold, int min_area_px,
                               VA_AnomStat* out_stat,
                               float* out_map, int map_cap, int* out_w, int* out_h,
                               unsigned char* out_mask, int mask_cap);

/* 读取模型信息（写 ASCII 摘要：类型 / 分辨率 / 特征维 / 样本数 / 阈值）。
 * 供界面下拉项与现场排障使用；未加载返回 0。 */
VA_API int VA_CALL va_anom_info(void* handle, char* buf, int cap);

/* 不加载权重、只看 model.json 就能给出的摘要（界面列表用）。
 * 目录不存在 / 不是模型目录时返回 0 且 buf 写空串。 */
VA_API int VA_CALL va_anom_peek(const char* model_dir, char* buf, int cap);

/* 释放当前模型。 */
VA_API int VA_CALL va_anom_clear(void* handle);

#ifdef __cplusplus
}
#endif

#endif /* VA_ANOMALY_H */
