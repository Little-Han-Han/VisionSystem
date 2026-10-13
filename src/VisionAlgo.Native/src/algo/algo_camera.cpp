// 本机摄像头实现：用 OpenCV VideoCapture 封装枚举 / 打开 / 抓帧 / 关闭。
// 枚举为探测式（逐个尝试 DShow 打开并读一帧）；打开时 MSMF 优先、失败回退 DShow。
// 所有异常统一转错误码并写入 CameraSource::lastError。
#include "va_internal.h"
#include "va_error.h"

#include <new>

namespace va {

// ---------------------- 枚举 / 打开原因（进程级，供无句柄时取文本）--------------------
// 仅在本编译单元内可见；对外经 va_camera_get_last_error(handle=NULL,...) 暴露，
// 不改动任何导出函数签名 / ABI。
static std::mutex  g_reasonMtx;
static std::string g_reason;

void SetCameraReason(const std::string& reason)
{
    std::lock_guard<std::mutex> lk(g_reasonMtx);
    g_reason = reason;
}

std::string CameraReason()
{
    std::lock_guard<std::mutex> lk(g_reasonMtx);
    return g_reason;
}

// 探测打开结果分类：用于枚举阶段区分「打不开」与「打开了但读不到帧（被占用特征）」。
enum ProbeResult { ProbeOk = 0, ProbeOpenFailed = 1, ProbeNoFrame = 2 };

// 慢热 / 短暂被占用的相机可能要连续读多帧才出图；探测期多读几帧再下结论，
// 避免把「刚上电、正在预热」或「瞬间被占用」的相机误判为不存在（RC2 假阴性）。
static const int kProbeWarmupReads = 10;

// 判为「可用后端」前需【连续】读到的帧数。有的后端（典型 MSMF）能打开、也能读一两帧，
// 但随后 read 持续失败——只读一帧就判 OK 会让「自动」选到这个不稳后端，直到连续取帧才
// 暴露 "read frame failed or timed out"。要求连续稳定读数，可在打开阶段就筛掉它，
// 让自动模式回退到 DShow（RC：usb 相机连续取帧超时的根因）。
static const int kProbeStableReads = 3;

// 尝试用指定后端打开 index 并确认能读到一帧；按结果分类返回。
// outW/outH 回填探测到的真实帧分辨率（比打开后查询属性更可靠）。
static ProbeResult ProbeOpen(cv::VideoCapture& cap, int index, int backend,
                             int* outW = 0, int* outH = 0)
{
    try {
        if (!cap.open(index, backend)) return ProbeOpenFailed;
        if (!cap.isOpened()) return ProbeOpenFailed;

        cv::Mat probe;
        int streak = 0;   // 连续成功读数；中途一旦读失败即清零，避免不稳后端混过
        for (int i = 0; i < kProbeWarmupReads; ++i) {
            if (cap.read(probe) && !probe.empty()) {
                if (outW) *outW = probe.cols;
                if (outH) *outH = probe.rows;
                if (++streak >= kProbeStableReads) return ProbeOk;
            } else {
                streak = 0;
            }
        }
        cap.release();
        return ProbeNoFrame;
    } catch (...) {
        try { cap.release(); } catch (...) {}
        return ProbeOpenFailed;
    }
}

int CameraEnum(int* outCount)
{
    if (outCount == 0) return VA_ERR_ARG;
    *outCount = 0;

    int count = 0;
    bool anyOpenedNoFrame = false;   // 是否有「能打开但读不到帧」的序号（被占用特征）
    const int kMaxProbe = 8;         // 探测上限，避免无限循环

    try {
        for (int i = 0; i < kMaxProbe; ++i) {
            cv::VideoCapture cap;

            // 与 CameraOpen 的「自动」顺序一致：MSMF 优先，失败回退 DShow。
            // 仅用 DShow 探测会漏掉 MSMF-only 的现代 UVC/USB 摄像头（Win10/11 常见）。
            ProbeResult r = ProbeOpen(cap, i, cv::CAP_MSMF);
            if (r != ProbeOk) {
                cap.release();
                r = ProbeOpen(cap, i, cv::CAP_DSHOW);
            }

            if (r == ProbeOk) { cap.release(); ++count; }
            else if (r == ProbeNoFrame) anyOpenedNoFrame = true;
            // 不在首个失败时中断：某些系统 index 0 是失效/被占用设备，
            // 中断会掩盖 index 1+ 的真实摄像头。
        }
    } catch (...) {
        // 枚举异常按已探测到的数量返回
    }

    // 记录可读原因：一台都没枚举到时，区分「被占用（能打开却读不到帧）」与「无设备 / 驱动」，
    // 供上层在无句柄时经 va_camera_get_last_error(NULL, ...) 取用（C2）。
    if (count == 0) {
        SetCameraReason(anyOpenedNoFrame
            ? "some devices opened but returned no frame (possibly occupied by another app)"
            : "no device could be opened (USB camera not connected or driver unavailable)");
    } else {
        SetCameraReason("");   // 有设备：清空旧原因，避免残留干扰
    }

    *outCount = count;
    return VA_OK;
}

void* CameraOpen(int index, const VA_CameraOption* opt)
{
    CameraSource* cam = new (std::nothrow) CameraSource();
    if (cam == 0) return 0;

    int width   = opt ? opt->width   : 0;
    int height  = opt ? opt->height  : 0;
    int fps     = opt ? opt->fps     : 0;
    int backend = opt ? opt->backend : 0;

    try {
        ProbeResult pr = ProbeOpenFailed;
        int pw = 0, ph = 0;   // 探测帧真实分辨率（比属性查询更可靠）

        if (backend == 2) {            // 仅 MSMF
            pr = ProbeOpen(cam->cap, index, cv::CAP_MSMF, &pw, &ph);
        } else if (backend == 1) {     // 仅 DShow
            pr = ProbeOpen(cam->cap, index, cv::CAP_DSHOW, &pw, &ph);
        } else {                       // 自动：MSMF 优先，失败回退 DShow
            pr = ProbeOpen(cam->cap, index, cv::CAP_MSMF, &pw, &ph);
            if (pr != ProbeOk) {
                cam->cap.release();
                pr = ProbeOpen(cam->cap, index, cv::CAP_DSHOW, &pw, &ph);
            }
        }

        if (pr != ProbeOk || !cam->cap.isOpened()) {
            cam->lastError = (pr == ProbeNoFrame)
                ? "camera opened but no frame within warmup reads (possibly occupied by another app)"
                : "cannot open camera device (index may not exist, driver unavailable, or occupied)";
            SetCameraReason(cam->lastError);   // Open 失败时无句柄，供上层取原因
            delete cam;
            return 0;
        }

        if (width  > 0) cam->cap.set(cv::CAP_PROP_FRAME_WIDTH,  (double)width);
        if (height > 0) cam->cap.set(cv::CAP_PROP_FRAME_HEIGHT, (double)height);
        if (fps    > 0) cam->cap.set(cv::CAP_PROP_FPS,          (double)fps);

        // 优先采用探测帧测得的真实分辨率；属性查询仅作兜底
        int w = pw > 0 ? pw : (int)cam->cap.get(cv::CAP_PROP_FRAME_WIDTH);
        int h = ph > 0 ? ph : (int)cam->cap.get(cv::CAP_PROP_FRAME_HEIGHT);
        cam->width  = w > 0 ? w : width;
        cam->height = h > 0 ? h : height;

        cam->opened = true;
        cam->lastError.clear();
        return cam;
    } catch (const std::exception& e) {
        cam->lastError = e.what();
        try { cam->cap.release(); } catch (...) {}
        delete cam;
        return 0;
    } catch (...) {
        cam->lastError = "unknown exception while opening camera";
        try { cam->cap.release(); } catch (...) {}
        delete cam;
        return 0;
    }
}

int CameraGrab(CameraSource* cam, VA_Image* dst, int timeoutMs)
{
    if (cam == 0) return VA_ERR_HANDLE;
    if (dst == 0 || dst->data == 0) { cam->lastError = "dst buffer is null"; return VA_ERR_ARG; }
    if (dst->channels != 3) { cam->lastError = "camera grab requires BGR (channels=3) dst"; return VA_ERR_TYPE; }

    std::lock_guard<std::mutex> lk(cam->mtx);
    if (!cam->cap.isOpened()) { cam->lastError = "camera not opened"; return VA_ERR_CAMERA; }

    // VideoCapture::read 为阻塞读，无法逐帧设置超时；timeoutMs 作为兼容占位。
    (void)timeoutMs;

    try {
        cv::Mat frame;
        if (!cam->cap.read(frame) || frame.empty()) {
            cam->lastError = "read frame failed or timed out";
            return VA_ERR_TIMEOUT;
        }

        // 统一为 BGR 8UC3（VideoCapture 默认即 BGR）
        if (frame.channels() == 1)
            cv::cvtColor(frame, frame, cv::COLOR_GRAY2BGR);
        else if (frame.channels() == 4)
            cv::cvtColor(frame, frame, cv::COLOR_BGRA2BGR);

        if (!frame.isContinuous())
            frame = frame.clone();

        cam->width  = frame.cols;
        cam->height = frame.rows;

        std::string err;
        if (!CopyOut(frame, dst, err)) {
            cam->lastError = err;
            return VA_ERR_SIZE;
        }
        return VA_OK;
    } catch (const std::exception& e) {
        cam->lastError = e.what();
        return VA_ERR_CAMERA;
    } catch (...) {
        cam->lastError = "unknown exception while grabbing";
        return VA_ERR_CAMERA;
    }
}

int CameraGetSize(CameraSource* cam, int* outW, int* outH)
{
    if (cam == 0) return VA_ERR_HANDLE;
    if (outW == 0 || outH == 0) return VA_ERR_ARG;

    std::lock_guard<std::mutex> lk(cam->mtx);
    if (!cam->cap.isOpened()) { *outW = 0; *outH = 0; return VA_ERR_CAMERA; }

    // 优先返回打开时探测到的真实分辨率（更稳定）；属性查询仅作兜底
    *outW = cam->width;
    *outH = cam->height;
    if (*outW <= 0 || *outH <= 0) {
        try {
            *outW = (int)cam->cap.get(cv::CAP_PROP_FRAME_WIDTH);
            *outH = (int)cam->cap.get(cv::CAP_PROP_FRAME_HEIGHT);
        } catch (...) {
            *outW = cam->width;
            *outH = cam->height;
        }
    }
    return VA_OK;
}

int CameraClose(CameraSource* cam)
{
    if (cam == 0) return VA_ERR_HANDLE;

    try {
        {
            std::lock_guard<std::mutex> lk(cam->mtx);
            if (cam->cap.isOpened()) cam->cap.release();
            cam->opened = false;
        }   // 先释放锁，再析构（避免 lock_guard 使用已销毁的 mutex）
    } catch (...) {
    }

    delete cam;
    return VA_OK;
}

} // namespace va
