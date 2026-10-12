// 库内部头文件：不对外暴露，仅 src 下各编译单元之间共享。
// 对外接口一律以 C 风格声明在 include/ 下，这里只放 C++ 实现细节。
#ifndef VA_INTERNAL_H
#define VA_INTERNAL_H

#include <opencv2/opencv.hpp>

#include <mutex>
#include <string>
#include <vector>

#include "va_types.h"
// 形状匹配的参数与结果结构体声明在对外头文件里，内部实现直接复用
#include "algo/va_shape.h"
// 相机对外结构体与接口声明在对外头文件里，内部实现直接复用
#include "algo/va_camera.h"
// 卡尺的对外结构体（VA_CaliperParam / VA_CaliperPoint）声明在对外头文件里
#include "algo/va_caliper.h"

namespace va {

// 形状匹配模型：模板 ROI 内的边缘点（相对中心）与其单位梯度方向
struct ShapeModel {
    std::vector<cv::Point2f> pts;   // 相对模板中心
    std::vector<cv::Point2f> dir;   // 单位梯度方向
    double width;                   // 模板 ROI 宽
    double height;                  // 模板 ROI 高
    bool   valid;

    ShapeModel() : width(0.0), height(0.0), valid(false) {}
};

// 每个句柄对应一个上下文，模板等状态保存在这里
struct Context {
    std::string lastError;
    cv::Mat     tmpl;
    bool        hasTemplate;
    ShapeModel  shape;              // 形状匹配模型

    Context() : hasTemplate(false) {}
};

// 相机句柄：与算法 Context 平行、相互独立（见架构设计决策 2）。
// 生命周期随调用方，va_camera_close 负责释放。
struct CameraSource {
    cv::VideoCapture cap;
    std::mutex       mtx;         // 保护 cap，串行化 grab / close
    std::string      lastError;
    int              width;       // 打开后协商到的分辨率
    int              height;
    bool             opened;

    CameraSource() : width(0), height(0), opened(false) {}
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

// 取面积最大的轮廓；没有轮廓时填充 err 并返回 false
bool    LargestContour(const cv::Mat& gray, std::vector<cv::Point>& out, std::string& err);

// 修正为不小于 3 的奇数
int     OddAtLeast3(int v);

void    SetError(Context* ctx, const char* msg);
void    SetError(Context* ctx, const std::exception& e);

// ---------------------- 算法实现 ----------------------
// 由 src/vision_algo.cpp 中的导出函数转发调用，签名与导出函数一致，
// 只是把 void* 换成了 va::Context*。

int Preprocess(Context* ctx, const VA_Image* src, VA_Image* dst,
               const VA_PreprocessParam* param);

int BlobAnalyze(Context* ctx, const VA_Image* gray, double minArea, double maxArea,
                VA_Blob* outBlobs, int maxCount, int* outCount);

int Sharpness(Context* ctx, const VA_Image* gray, double* outScore);

int FitCircle(Context* ctx, const VA_Image* gray, VA_Circle* out);

int FitLine(Context* ctx, const VA_Image* gray, VA_Line* out);

int TrainTemplate(Context* ctx, const VA_Image* gray, const int* roi);

int MatchTemplate(Context* ctx, const VA_Image* gray, VA_MatchResult* out);

int EdgeCount(Context* ctx, const VA_Image* gray, int scanRow, int delta, int* outCount);

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

} // namespace va

#endif // VA_INTERNAL_H
