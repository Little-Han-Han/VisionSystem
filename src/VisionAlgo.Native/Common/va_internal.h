// 库内部头文件：不对外暴露，仅 src 下各编译单元之间共享。
// 对外接口一律以 C 风格声明在 include/ 下，这里只放 C++ 实现细节。
#ifndef VA_INTERNAL_H
#define VA_INTERNAL_H

#include <opencv2/opencv.hpp>

#include <cstring>
#include <mutex>
#include <string>
#include <vector>

#include "va_types.h"
// 错误码（VA_ERR_*）与结构化错误：内部实现到处要用，统一在这里引入
#include "va_error.h"
// OpenVINO C++ 推理运行时（YOLO 目标检测用）
#include <openvino/openvino.hpp>
// 形状匹配的参数与结果结构体声明在对外头文件里，内部实现直接复用
#include "algo/va_shape.h"
// 模板匹配（NCC）的参数结构体（VA_MatchParam）
#include "algo/va_match.h"
// 条码 / 二维码识别的参数与结果结构体（VA_BarcodeParam / VA_Barcode）
#include "algo/va_barcode.h"
// 相机对外结构体与接口声明在对外头文件里，内部实现直接复用
#include "algo/va_camera.h"
// 卡尺的对外结构体（VA_CaliperParam / VA_CaliperPoint）声明在对外头文件里
#include "algo/va_caliper.h"
// 图像滤波的参数结构体（VA_FilterParam）声明在对外头文件里
#include "algo/va_filter.h"
// 颜色提取的参数 / 统计结构体（VA_ColorParam / VA_ColorStat）声明在对外头文件里
#include "algo/va_color.h"
// 分类器的参数结构体（VA_ClassParam）声明在对外头文件里
#include "algo/va_classify.h"
// OCR 的参数 / 结果结构体（VA_OcrParam / VA_OcrChar）声明在对外头文件里
#include "algo/va_ocr.h"
// YOLO 目标检测的参数 / 结果结构体（VA_DetectResult）声明在对外头文件里
#include "algo/va_detect.h"
#include "algo/va_dl.h"
#include "algo/va_ocrdl.h"
// 无监督缺陷检测（PaDiM / EfficientAD）的对外结构体（VA_AnomStat）
#include "algo/va_anomaly.h"

namespace va {

// ---------------------------------------------------------------------------
//  全局并行预算（由 va_set_threads 设置，0 = 不限制/用库默认）
//  【为什么要一个全局量】CPU 上真正开线程池的有两套东西：
//    ① OpenCV 的 parallel_for（cv::setNumThreads 控制）；
//    ② OpenVINO 推理（ov::Core 默认按物理核数开自己的线程池，**不受** setNumThreads 影响）。
//  宿主本来就是"每条流程一个线程"并行，两套再各自开满核 ⇒ N 条流程 × 核数 个线程抢核，
//  实测 3 条流程吃满 10.9 核却只有 3 fps。这里让两者共用同一个预算。
// ---------------------------------------------------------------------------
extern int g_vaThreads;

// 把并行预算应用到 OpenVINO 的 CPU 插件（建 ov::Core 之后、compile_model 之前调用）。
// 预算是 0（未设置）时保持库默认，不做任何改动。
void ApplyThreadBudget(ov::Core& core);

// 形状匹配模型：模板 ROI 内的边缘点（相对中心）与其单位梯度方向
struct ShapeModel {
    std::vector<cv::Point2f> pts;   // 相对模板中心
    std::vector<cv::Point2f> dir;   // 单位梯度方向
    double width;                   // 模板 ROI 宽
    double height;                  // 模板 ROI 高
    bool   valid;

    ShapeModel() : width(0.0), height(0.0), valid(false) {}
};

// 分类器状态：累积的训练样本 + 训练好的模型，挂在 Context 上
struct ClassifierState {
    VA_ClassParam                     param;      // va_clf_create 时保存
    std::vector<std::vector<float> >  features;   // 每个样本一条特征向量
    std::vector<int>                  labels;     // 与 features 一一对应
    cv::Ptr<cv::ml::StatModel>        model;      // SVM 或 KNearest
    bool                              created;    // 已 create（参数有效）
    bool                              trained;    // 已训练（可用于预测）

    ClassifierState() : created(false), trained(false) {
        std::memset(&param, 0, sizeof(param));
    }
};

// OCR 状态：字符模板库（已归一化）+ 默认参数，挂在 Context 上
struct OcrState {
    VA_OcrParam             param;      // va_ocr_set_param 时保存，作为缺省值
    std::vector<cv::Mat>    templates;  // 归一化后的单通道字符模板
    std::vector<std::string> labels;    // 与 templates 一一对应
    // 每个模板「中心 (W-2)x(H-2) 子区」的标准差，训练时算一次。
    // 识别阶段一张图要跑 N(模板) x M(字符) 次相关，每次都重算模板标准差纯属浪费。
    std::vector<double>     tplStd;

    OcrState() { std::memset(&param, 0, sizeof(param)); }
};

// YOLO 目标检测状态：OpenVINO 编译模型 + 类别名表，挂在 Context 上（一个句柄 = 一条流程）
struct DetectorState {
    ov::Core                  core;        // OpenVINO 核心（管理设备插件，可跨 create 复用）
    ov::CompiledModel         compiled;    // 编译后的模型（已绑定设备）
    ov::InferRequest          infer;       // 推理请求（复用，避免每次重建）
    int                       input_w;     // 模型输入宽（导出时固定，如 640）
    int                       input_h;     // 模型输入高
    int                       num_classes; // 类别数（= 输出通道 - 4，从模型读取）
    std::vector<std::string>  class_names; // 类别名（COCO 80 或侧车 <model>.txt）
    bool                      created;     // 已成功加载并编译模型

    DetectorState() : input_w(0), input_h(0), num_classes(0), created(false) {}
};

// 通用深度学习状态：语义分割 / 姿态估计 / 深度估计共用（模型不同、后处理不同）。
// 与 DetectorState 的区别：输出解析按 task 分支，且可能有多个输出（seg 的 proto）。
struct DlState {
    ov::Core                  core;
    ov::CompiledModel         compiled;
    ov::InferRequest          infer;
    int                       input_w;
    int                       input_h;
    int                       num_classes;
    int                       task;          // VA_DL_TASK_*（1=seg 2=pose 3=depth）
    int                       keypoints;     // pose：关键点数
    int                       out_channels;  // 模型输出通道数（4+nc[+32|+3k]），排查解析用
    std::vector<std::string>  class_names;
    bool                      created;

    DlState() : input_w(0), input_h(0), num_classes(0), task(0), keypoints(0),
                out_channels(0), created(false) {}
};

// 深度学习 OCR（PaddleOCR）：DB 检测 + CRNN 识别，两个模型 + 字典
struct OcrDlState {
    ov::CompiledModel        det_compiled;
    ov::CompiledModel        rec_compiled;
    ov::InferRequest         det_infer;
    ov::InferRequest         rec_infer;
    int                      det_w, det_h;
    int                      rec_w, rec_h;
    std::vector<std::string> chars;      // 字典（索引 0 由 CTC blank 占用）
    bool                     has_det;    // false = 只有识别模型（整图识别，跳过检测）
    bool                     created;

    OcrDlState() : det_w(0), det_h(0), rec_w(0), rec_h(0), has_det(false), created(false) {}
};

// ---------------------------------------------------------------------------
//  无监督缺陷检测（PaDiM / EfficientAD）
//  【为什么一个 Net 结构体够用】两类模型的子网络都只是「单输入单输出前向」，
//  差别全在"输出怎么用"（PaDiM 取特征图算马氏距离、EfficientAD 取学生-教师差）。
//  把加载 / 形状校验收敛到一处，新增第三类模型时只需再加一段后处理。
// ---------------------------------------------------------------------------
struct AnomNet {
    ov::CompiledModel compiled;
    ov::InferRequest  infer;
    int  in_w, in_h;      // 期望的输入尺寸（模型静态形状）
    int  out_c, out_h, out_w;  // 输出特征图尺寸（4D 输出时有效）
    bool ok;

    AnomNet() : in_w(0), in_h(0), out_c(0), out_h(0), out_w(0), ok(false) {}

    void Reset() {
        infer = ov::InferRequest();
        compiled = ov::CompiledModel();
        in_w = in_h = out_c = out_h = out_w = 0;
        ok = false;
    }
};

// 已加载的模型：PaDiM 的逐位置高斯统计，或 EfficientAD 的三个子网络
struct AnomalyState {
    int   kind;          // VA_ANOM_KIND_*
    int   feat;          // VA_ANOM_FEAT_*（PaDiM 专用）
    int   input_size;    // 分析分辨率
    int   patch;         // 内置描述子的 patch 边长
    int   gw, gh;        // 特征图网格
    int   dim;           // 实际使用的特征维（= sel.size()）
    int   src_dim;       // 特征原始维（OpenVINO 输出的通道数 / 内置描述子维数）
    int   samples;       // 训练样本数
    float threshold;     // 模型自带阈值（原始值刻度）
    std::string dir;     // 模型目录
    std::string device;  // 编译设备

    std::vector<int>   sel;    // 维度选择表（src_dim → dim）
    std::vector<float> mean;   // P*dim
    std::vector<float> chol;   // P*dim*dim（下三角，未使用位置为 0）

    AnomNet extractor;         // PaDiM 特征提取器（feat=HANDCRAFT 时 ok=false）
    AnomNet teacher;
    AnomNet student;
    AnomNet ae;

    float st_mean, st_std;     // EfficientAD：学生-教师分支的归一化统计
    float ae_mean, ae_std;     // EfficientAD：自编码器分支

    bool created;

    AnomalyState()
        : kind(0), feat(0), input_size(256), patch(8), gw(0), gh(0), dim(0), src_dim(0),
          samples(0), threshold(0.0f), st_mean(0.0f), st_std(1.0f),
          ae_mean(0.0f), ae_std(1.0f), created(false) {}

    void Reset() {
        kind = feat = 0;
        input_size = 256; patch = 8;
        gw = gh = dim = src_dim = samples = 0;
        threshold = 0.0f;
        dir.clear(); device.clear();
        sel.clear(); mean.clear(); chol.clear();
        extractor.Reset(); teacher.Reset(); student.Reset(); ae.Reset();
        st_mean = 0.0f; st_std = 1.0f; ae_mean = 0.0f; ae_std = 1.0f;
        created = false;
    }
};

// PaDiM 训练会话：begin 建统计量累积器，add 逐张累加，finish 分解并落盘
struct AnomalyTrainState {
    bool      active;
    int       feat, input_size, patch;
    int       gw, gh, dim, src_dim;
    long long n;              // 已 add 的样本数
    bool      retain;         // 还在保留样本特征（算阈值用）；超出预算后置 false
    long long retained;       // 已保留的 float 数
    std::string out_dir;
    std::string extractor;    // IR 相对路径（feat=OPENVINO 时非空）

    std::vector<int>    sel;      // 维度选择表
    std::vector<double> sum;      // P*dim
    std::vector<double> outer;    // P*dim*dim
    std::vector<float>  samples;  // n*P*dim（pos-major）

    AnomNet net;                 // feat=OPENVINO 时加载的提取器

    AnomalyTrainState()
        : active(false), feat(0), input_size(256), patch(8), gw(0), gh(0), dim(0), src_dim(0),
          n(0), retain(true), retained(0) {}

    void Reset() {
        active = false; feat = 0; input_size = 256; patch = 8;
        gw = gh = dim = src_dim = 0;
        n = 0; retain = true; retained = 0;
        out_dir.clear(); extractor.clear();
        sel.clear(); sum.clear(); outer.clear(); samples.clear();
        net.Reset();
    }
};


// 每个句柄对应一个上下文，模板等状态保存在这里
struct Context {
    std::string lastError;
    // ---- 结构化错误（va_get_error_info 的数据源）----
    // 只保留"最近一次"；每次 SetError 都会整体重置，避免上一次的模块名/参数名残留。
    int         lastCode;      // VA_ERR_*；0 = 没出过错
    std::string lastModule;    // 哪个模块/步骤
    std::string lastParam;     // 哪个参数
    std::string lastValue;     // 实际值
    std::string lastExpect;    // 期望口径
    cv::Mat     tmpl;
    bool        hasTemplate;
    ShapeModel  shape;              // 形状匹配模型
    ClassifierState clf;            // 分类器（样本 + 模型）
    OcrState        ocr;            // 传统算法 OCR（模板库）
    DetectorState   det;             // YOLO 目标检测器
    DlState         seg;             // YOLO 语义分割
    DlState         pose;            // YOLO 姿态估计
    DlState         depth;           // 深度估计
    OcrDlState      ocrdl;           // 深度学习 OCR（PaddleOCR）
    // 无监督缺陷检测：一个 ov::Core 共用给所有子网络（每建一个 Core 都会重载设备插件）
    ov::Core          anomCore;
    bool              anomCoreReady;
    AnomalyState      anom;          // 已加载的异常检测模型
    AnomalyTrainState anomTrain;     // PaDiM 训练会话

    Context() : lastCode(0), hasTemplate(false), anomCoreReady(false) {}
};

// 相机句柄：与算法 Context 平行、相互独立（见架构设计决策 2）。
// 生命周期随调用方，va_camera_close 负责释放。
struct CameraSource {
    cv::VideoCapture cap;
    std::mutex       mtx;         // 保护 cap，串行化 grab / close
    std::string      lastError;
    int              index;       // 本句柄对应的设备序号（关闭时用于注销「已打开注册表」）
    int              width;       // 打开后协商到的分辨率
    int              height;
    bool             opened;

    CameraSource() : index(-1), width(0), height(0), opened(false) {}
};

// ---------------------- 相机 ----------------------

// 探测可用设备数量（探测式枚举，只计数不保留句柄）。
int   CameraEnum(int* outCount);
// 打开序号 index 的设备；成功返回句柄，失败返回 NULL。
void* CameraOpen(int index, const VA_CameraOption* opt);
// 抓一帧 BGR 到调用方缓冲 dst。
int   CameraGrab(CameraSource* cam, VA_Image* dst, int timeoutMs);
// 取打开后协商到的分辨率。
int   CameraGetSize(CameraSource* cam, int* outW, int* outH);
// 关闭并释放句柄。
int   CameraClose(CameraSource* cam);

// 取/设最近一次本机摄像头「枚举 / 打开」的原因文本。
// 供 va_camera_get_last_error 在句柄为空（枚举为 0 / Open 失败）时兜底返回，
// 这样无需改动任何导出函数签名即可把原因透传给上层（C2）。
void        SetCameraReason(const std::string& reason);
std::string CameraReason();

// ---------------------- 公共工具 ----------------------

// 用调用方内存构造 cv::Mat（不拷贝）。失败时填充 err
bool    ToMat(const VA_Image* img, cv::Mat& out, std::string& err);

// 把结果逐行拷进调用方输出缓冲区（兼容 stride 差异）
bool    CopyOut(const cv::Mat& src, VA_Image* dst, std::string& err);

// 取单通道副本（彩色则灰度化）
cv::Mat AsGray(const cv::Mat& input);

// 按 128 阈值化
cv::Mat Binarize(const cv::Mat& gray);

// 取面积最大的轮廓；没有轮廓时填充 err 并返回 false。
// preBinarized=true 表示 gray 已经是 0/255 二值图，跳过 Binarize 的重复阈值化
// （识别链路已经把二值图算出来了，再按 128 阈值一遍只是白白多一次全图扫描）。
bool    LargestContour(const cv::Mat& gray, std::vector<cv::Point>& out, std::string& err,
                       bool preBinarized = false);

// 修正为不小于 3 的奇数
int     OddAtLeast3(int v);

void    SetError(Context* ctx, const char* msg);
void    SetError(Context* ctx, const std::exception& e);

// 结构化报错：写 code + 哪个模块 + 哪个参数 + 实际值 + 期望，并返回 code（便于 `return SetErrorEx(...)`）。
// message 传 NULL 时自动拼成「模块：参数=实际值（期望：…）」。
int     SetErrorEx(Context* ctx, int code, const char* module, const char* param,
                   const char* value, const char* expect, const char* message = 0);

// ---------------------- 算法实现 ----------------------
// 由 src/vision_algo.cpp 中的导出函数转发调用，签名与导出函数一致，
// 只是把 void* 换成了 va::Context*。

int Preprocess(Context* ctx, const VA_Image* src, VA_Image* dst,
               const VA_PreprocessParam* param);

int BlobAnalyze(Context* ctx, const VA_Image* gray, double minArea, double maxArea,
                VA_Blob* outBlobs, int maxCount, int* outCount);
// 扩展版连通域分析：额外算全形状特征，并可选输出标签图（outLabels 可为 0）。
// 导出层的 va_blob_analyze_ex / 托管的 AnalyzeBlobsEx 都走这里。
int BlobAnalyzeEx(Context* ctx, const VA_Image* gray, double minArea, double maxArea,
                  VA_Blob* outBlobs, int maxCount, int* outCount, int* outLabels);

int Sharpness(Context* ctx, const VA_Image* gray, double* outScore);

int FitCircle(Context* ctx, const VA_Image* gray, VA_Circle* out);

int FitLine(Context* ctx, const VA_Image* gray, VA_Line* out);

int TrainTemplate(Context* ctx, const VA_Image* gray, const int* roi);

int MatchTemplate(Context* ctx, const VA_Image* gray, VA_MatchResult* out);

// NCC 的多姿态版本（角度 / 缩放 / 金字塔 / 多目标）。
int MatchTemplateEx(Context* ctx, const VA_Image* gray, const VA_MatchParam* param,
                    VA_MatchResult* out, int maxCount, int* outCount);

int EdgeCount(Context* ctx, const VA_Image* gray, int scanRow, int delta, int* outCount);

// ---------------------- 条码 / 二维码 ----------------------
// zxing-cpp 只读识别；param 为 NULL 等价于「全部码制 + 多方向 + 反色」。
int BarcodeRead(Context* ctx, const VA_Image* gray, const VA_BarcodeParam* param,
                VA_Barcode* out, int maxCount, int* outCount);
const char* BarcodeVersion();

// ---------------------- 图像滤波 ----------------------
// 均值 / 高斯 / 中值 / 双边 / USM 锐化 / 自定义 3x3 核，后接可选形态学。
// 支持 1 通道与 3 通道输入。实现见 src/algo/algo_filter.cpp，导出见 va_filter。
int Filter(Context* ctx, const VA_Image* src, VA_Image* dst, const VA_FilterParam* param);

// ---------------------- 颜色提取 ----------------------
// BGR / HSV 阈值分割 + 形态学清理 + 连通域过滤，需要 3/4 通道彩色输入。
// 实现见 src/algo/algo_color.cpp，导出见 va_color_extract。
int ColorExtract(Context* ctx, const VA_Image* src, VA_Image* dstMask,
                 const VA_ColorParam* param, double minArea, double maxArea,
                 VA_Blob* outBlobs, int maxCount, int* outCount,
                 VA_ColorStat* outStat);

// ---------------------- 分类器 ----------------------
// 轮廓特征（Hu 矩 / 几何）+ OpenCV ml 的 SVM / KNN。
// 实现见 src/algo/algo_classify.cpp，导出见 va_clf_*。
int ClfCreate(Context* ctx, const VA_ClassParam* param);
int ClfAddSample(Context* ctx, const VA_Image* gray, int label);
int ClfSampleCount(Context* ctx, int* outCount);
int ClfTrain(Context* ctx);
int ClfPredict(Context* ctx, const VA_Image* gray, int* outLabel, double* outScore);
int ClfSave(Context* ctx, const char* path);
int ClfLoad(Context* ctx, const char* path);
int ClfClear(Context* ctx);

// ---------------------- YOLO 目标检测（OpenVINO 推理）----------------------
// 加载 Ultralytics 导出的 YOLO ONNX（输出布局 [1, 4+num_classes, anchors]），
// 编译到指定设备并缓存类别名表。实现见 src/algo/algo_detect.cpp，导出见 va_det_*。
int DetCreate(Context* ctx, const char* model_path, const char* device);

// ---------------------- 深度学习（seg / pose / depth）---------------------
int DlSegCreate(Context* ctx, const char* model_path, const char* device);
int DlSegRun(Context* ctx, const VA_Image* img, float score_thresh, float iou_thresh,
             VA_DlBox* out_boxes, int max_count, int* out_count,
             unsigned char* out_label_map, int label_cap, int* label_w, int* label_h);

int DlPoseCreate(Context* ctx, const char* model_path, const char* device);
int DlPoseRun(Context* ctx, const VA_Image* img, float score_thresh, float iou_thresh,
              VA_DlBox* out_boxes, int max_count, int* out_count,
              VA_DlKeyPoint* out_kps, int kps_capacity,
              int* out_kps_per_box, int* out_kps_written);
int DlPoseKeypoints(Context* ctx, int* out_k);

int DlDepthCreate(Context* ctx, const char* model_path, const char* device);
int DlDepthRun(Context* ctx, const VA_Image* img, float* out_map, int width, int height,
               float* out_min, float* out_max);

int DlClear(Context* ctx, int task);

// ---------------------- 无监督缺陷检测（PaDiM / EfficientAD）---------------------
// 模型目录是"数据"（model.json + 权重），加载 / 推理 / 训练都在 algo_anomaly.cpp。
int AnomTrainBegin(Context* ctx, const char* out_dir, const char* extractor,
                   int feat_kind, int input_size);
int AnomTrainAdd(Context* ctx, const VA_Image* img, const int* roi);
int AnomTrainFinish(Context* ctx, char* out_summary, int cap);
int AnomTrainCount(Context* ctx, int* out_count);
int AnomTrainCancel(Context* ctx);

int AnomCreate(Context* ctx, const char* model_dir, const char* device);
int AnomRun(Context* ctx, const VA_Image* img, const int* roi,
            float threshold, int min_area_px, VA_AnomStat* out_stat,
            float* out_map, int map_cap, int* out_w, int* out_h,
            unsigned char* out_mask, int mask_cap);
int AnomInfo(Context* ctx, char* buf, int cap);
int AnomPeek(const char* model_dir, char* buf, int cap);
int AnomClear(Context* ctx);

// ---------------------- 深度学习 OCR（PaddleOCR）---------------------
int OcrDlCreate(Context* ctx, const char* det_model, const char* rec_model,
                const char* dict_path, const char* device);
int OcrDlRun(Context* ctx, const VA_Image* img, float box_thresh, float unclip_ratio,
             VA_OcrDlLine* out_lines, int max_count, int* out_count,
             char* out_text, int text_cap, int* out_text_len);
int OcrDlClear(Context* ctx);
int DlBackendAvailable(int backend);
int DlDescribe(Context* ctx, int task, char* buf, int cap);

/* 相机标定（棋盘格角点 + 内参/畸变） */
int CalibFindChessboard(Context* ctx, const VA_Image* gray, int cols, int rows, int pattern,
                        double* outXY, int cap, int* outCount);
int CalibCalibrate(Context* ctx, const double* xy, const int* counts, int nImages,
                   int cols, int rows, int width, int height, int flags,
                   double* outK, double* outDist, double* outRms);
int DetRun(Context* ctx, const VA_Image* img, float score_thresh, float iou_thresh,
           VA_DetectResult* out_results, int max_count, int* out_count);
int DetClassCount(Context* ctx, int* out_count);
int DetClassName(Context* ctx, int idx, char* out_buf, int buf_size);
int DetClear(Context* ctx);

// ---------------------- 传统算法 OCR ----------------------
// 连通域分割 + 归一化 + 模板匹配识别（无 contrib，自搭）。
// 实现见 src/algo/algo_ocr.cpp，导出见 va_ocr_*。
int OcrSetParam(Context* ctx, const VA_OcrParam* param);
int OcrAddTemplate(Context* ctx, const VA_Image* gray, const char* label);
int OcrTemplateCount(Context* ctx, int* outCount);
int OcrClearTemplates(Context* ctx);
int OcrSegment(Context* ctx, const VA_Image* gray, const VA_OcrParam* param,
               VA_OcrChar* outChars, int maxCount, int* outCount);
int OcrRecognize(Context* ctx, const VA_Image* gray, const VA_OcrParam* param,
                 char* outText, int textCap, double* outMeanScore);
// 分割 + 识别一次调用：同时出文本与每个字符的标签 / 置信度（框与标签同源）
int OcrRecognizeEx(Context* ctx, const VA_Image* gray, const VA_OcrParam* param,
                   VA_OcrChar* outChars, int charsCap, int* outCharCount,
                   char* outText, int textCap, double* outMeanScore);

// ---------------------- 卡尺 ----------------------
// 沿路径等分投放投影卡尺找边 → 最小二乘拟合 → 离群剔除一轮 → RMS。
// 实现见 src/algo/algo_caliper.cpp，导出见 va_line_caliper / va_circle_caliper。
int LineCaliper(Context* ctx, const VA_Image* gray, const VA_CaliperParam* param,
                VA_CaliperPoint* out_points, int max_count, int* out_count,
                VA_Line* out_line, double* out_rms);

int CircleCaliper(Context* ctx, const VA_Image* gray, const VA_CaliperParam* param,
                  VA_CaliperPoint* out_points, int max_count, int* out_count,
                  VA_Circle* out_circle, double* out_rms);

// 形状匹配（基于边缘轮廓）
int CreateShapeModel(Context* ctx, const VA_Image* gray, const int* roi, double contrast);

int CreateShapeModelEx(Context* ctx, const VA_Image* gray, const int* roi,
                       const VA_ShapeTrainParam* param);

int GetShapeModelInfo(Context* ctx, int* outPointCount, double* outWidth, double* outHeight);

int GetShapeModelPoints(Context* ctx, VA_ShapePoint* outPoints, int maxCount, int* outCount);

int ClearShapeModel(Context* ctx);

int FindShapeModel(Context* ctx, const VA_Image* gray, const VA_ShapeParam* param,
                   const int* searchRoi, VA_ShapeMatch* outMatches,
                   int maxCount, int* outCount);

// ---------------------------------------------------------------------------
//  节点级软超时（协作式中断）
//  【放置位置】放在 namespace va 里而不是新建一个模块，是因为它要出现在**所有** algo_*.cpp
//  的循环里；单独一个头会让每个算法多一行 include，改这里一处即可。
//  实现在 Common/va_internal.cpp（同一份 thread_local，跨 TU 唯一）。
// ---------------------------------------------------------------------------

/* 设置/清除本线程的截止时间（毫秒，<=0 清除）。由导出函数 va_set_deadline_ms 调用。 */
void DeadlineSet(int ms);

/* 当前剩余毫秒（未设超时返回 -1）。仅用于诊断/摘要。 */
int DeadlineRemainMs();

/* 检查点：到点返回 true。
 * 【成本】每 8 次调用才读一次 steady_clock（~20ns），摊到每次 <3ns —— 比一次浮点乘还便宜，
 * 所以可以放心放进内层循环。 */
bool DeadlineHit();

} // namespace va

#endif // VA_INTERNAL_H
