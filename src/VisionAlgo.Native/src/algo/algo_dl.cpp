// 深度学习工具（语义分割 / 姿态估计 / 深度估计）：OpenVINO 推理。
// 预处理与 NMS 与 algo_detect.cpp 同源（letterbox → RGB → /255 → NCHW），
// 差异只在"输出怎么解析"，因此三任务共用一份推理骨架、各自解析。
#include "va_internal.h"
#include "va_error.h"

#include <algorithm>
#include <cmath>
#include <fstream>
#include <string>
#include <vector>

namespace va {

namespace {

struct DlBox {
    float x, y, w, h;
    int   cls;
    float score;
    int   maskArea;
    std::vector<float> coef;   // seg：32 个 mask 系数
    std::vector<float> kps;    // pose：3*k（x,y,conf）
};

void Preprocess(const cv::Mat& bgr, int inW, int inH, std::vector<float>& blob,
                float& scale, int& padX, int& padY)
{
    float s = std::min((float)inW / (float)bgr.cols, (float)inH / (float)bgr.rows);
    int newW = (int)std::lround(bgr.cols * s);
    int newH = (int)std::lround(bgr.rows * s);
    int pX = (inW - newW) / 2;
    int pY = (inH - newH) / 2;
    scale = s; padX = pX; padY = pY;

    cv::Mat resized, padded;
    cv::resize(bgr, resized, cv::Size(newW, newH), 0, 0, cv::INTER_LINEAR);
    cv::copyMakeBorder(resized, padded, pY, inH - newH - pY, pX, inW - newW - pX,
                       cv::BORDER_CONSTANT, cv::Scalar(114, 114, 114));
    cv::cvtColor(padded, padded, cv::COLOR_BGR2RGB);

    blob.assign((size_t)3 * inW * inH, 0.0f);
    const unsigned char* p = padded.data;
    const int wh = inW * inH;
    for (int i = 0; i < wh; ++i) {
        blob[(size_t)0 * wh + i] = (float)p[i * 3 + 0] / 255.0f;
        blob[(size_t)1 * wh + i] = (float)p[i * 3 + 1] / 255.0f;
        blob[(size_t)2 * wh + i] = (float)p[i * 3 + 2] / 255.0f;
    }
}

float IoU(const DlBox& a, const DlBox& b)
{
    float xa = std::max(a.x, b.x), ya = std::max(a.y, b.y);
    float xb = std::min(a.x + a.w, b.x + b.w), yb = std::min(a.y + a.h, b.y + b.h);
    float iw = std::max(0.0f, xb - xa), ih = std::max(0.0f, yb - ya);
    float inter = iw * ih;
    float uni = a.w * a.h + b.w * b.h - inter;
    return (uni > 0.0f) ? (inter / uni) : 0.0f;
}

void NMS(std::vector<DlBox>& boxes, float iouThresh, std::vector<DlBox>& out)
{
    std::sort(boxes.begin(), boxes.end(),
              [](const DlBox& a, const DlBox& b) { return a.score > b.score; });

    std::vector<bool> suppressed(boxes.size(), false);
    for (size_t i = 0; i < boxes.size(); ++i) {
        if (suppressed[i]) continue;
        out.push_back(boxes[i]);
        for (size_t j = i + 1; j < boxes.size(); ++j) {
            if (suppressed[j]) continue;
            if (boxes[j].cls != boxes[i].cls) continue;
            if (IoU(boxes[i], boxes[j]) > iouThresh) suppressed[j] = true;
        }
    }
}

// 类别名：侧车 <model>.txt；缺失时 80 类回退 COCO 名，其余 class_N
const std::vector<std::string>& Coco80()
{
    static const std::vector<std::string> k = {
        "person","bicycle","car","motorcycle","airplane","bus","train","truck","boat",
        "traffic light","fire hydrant","stop sign","parking meter","bench","bird","cat","dog",
        "horse","sheep","cow","elephant","bear","zebra","giraffe","backpack","umbrella",
        "handbag","tie","suitcase","frisbee","skis","snowboard","sports ball","kite",
        "baseball bat","baseball glove","skateboard","surfboard","tennis racket","bottle",
        "wine glass","cup","fork","knife","spoon","bowl","banana","apple","sandwich","orange",
        "broccoli","carrot","hot dog","pizza","donut","cake","chair","couch","potted plant",
        "bed","dining table","toilet","tv","laptop","mouse","remote","keyboard","cell phone",
        "microwave","oven","toaster","sink","refrigerator","book","clock","vase","scissors",
        "teddy bear","hair drier","toothbrush"
    };
    return k;
}

bool LoadNames(const std::string& modelPath, std::vector<std::string>& names)
{
    std::string txt = modelPath;
    size_t dot = txt.find_last_of('.');
    if (dot != std::string::npos) txt = txt.substr(0, dot);
    txt += ".txt";

    std::ifstream f(txt);
    if (!f.is_open()) return false;

    std::string line;
    while (std::getline(f, line)) {
        if (!line.empty() && line.back() == '\r') line.pop_back();
        if (!line.empty()) names.push_back(line);
    }
    return !names.empty();
}

// 建 DlState（三任务共用）：读模型 I/O 形状 → 编译 → 缓存类别名
int CreateCommon(Context* ctx, DlState& st, int task, const char* model_path, const char* device)
{
    if (model_path == 0 || model_path[0] == '\0') {
        SetError(ctx, "model_path empty");
        return VA_ERR_ARG;
    }

    const char* dev = (device != 0 && device[0] != '\0') ? device : "CPU";

    try
    {
        // CUDA 预留：显式声明 CUDA 时给出明确文案，而不是让 OpenVINO 报设备名非法
        if (_stricmp(dev, "CUDA") == 0) {
            SetError(ctx, "CUDA backend not implemented yet (reserved); use CPU/GPU/NPU for now");
            return VA_ERR_ARG;
        }

        ov::Core core;
        std::shared_ptr<ov::Model> model = core.read_model(model_path);

        auto in_shape = model->input().get_shape();
        int inW = 0, inH = 0;
        if (in_shape.size() == 4) {
            inH = (int)in_shape[2];
            inW = (int)in_shape[3];
            if (inW <= 0 || inH <= 0) {
                // 动态形状：按 640×640 固定（Ultralytics 默认）
                inW = inH = 640;
                model->reshape(ov::PartialShape({ 1, 3, 640, 640 }));
                model = model->clone();
            }
        }
        if (inW <= 0 || inH <= 0) {
            SetError(ctx, "unexpected model input shape (need [1,3,H,W])");
            return VA_ERR_ARG;
        }

        st.compiled = core.compile_model(model, dev);
        st.infer = st.compiled.create_infer_request();
        st.core = std::move(core);
        st.input_w = inW;
        st.input_h = inH;
        st.task = task;

        // 输出通道数：seg = 4+nc+32，pose = 4+nc+3k，depth 另有分支
        if (task != VA_DL_TASK_DEPTH) {
            auto out_shape = st.compiled.output(0).get_shape();
            if (out_shape.size() < 2) {
                SetError(ctx, "unexpected model output rank");
                return VA_ERR_ARG;
            }
            int d0 = (int)out_shape[out_shape.size() - 2];
            int d1 = (int)out_shape[out_shape.size() - 1];
            int C = std::min(d0, d1);

            if (task == VA_DL_TASK_SEG) {
                st.num_classes = C - 4 - 32;
            } else {
                // pose：关键点数由通道数反推（(C-4-nc)/3，先按 nc=1 估算再校正）
                st.num_classes = 1;
                int k = (C - 4 - st.num_classes) / 3;
                if (k <= 0) { st.num_classes = C - 4 - 51; k = 17; }
                st.keypoints = k > 0 ? k : 17;
            }
            if (st.num_classes <= 0) {
                SetError(ctx, "unexpected model output channels (need 4+nc[+32|+3k])");
                return VA_ERR_ARG;
            }

            std::vector<std::string> names;
            if (!LoadNames(model_path, names)) {
                if (st.num_classes == 80) names = Coco80();
            }
            while ((int)names.size() < st.num_classes)
                names.push_back("class_" + std::to_string(names.size()));
            st.class_names = std::move(names);
        }

        st.created = true;
        return VA_OK;
    }
    catch (const std::exception& e) { SetError(ctx, e); return VA_ERR_INTERNAL; }
}

// 跑一次前向，返回输出张量指针（blob 必须活到解析结束）
const float* Forward(Context* ctx, DlState& st, const VA_Image* img,
                     std::vector<float>& blob, cv::Mat& bgr,
                     float& scale, int& padX, int& padY, std::vector<size_t>& shape, int outputIndex)
{
    if (img == 0) { SetError(ctx, "null image"); return 0; }
    if (!st.created) { SetError(ctx, "model not created"); return 0; }

    if (!ToMat(img, bgr, ctx->lastError)) return 0;
    if (bgr.channels() == 1)      cv::cvtColor(bgr, bgr, cv::COLOR_GRAY2BGR);
    else if (bgr.channels() == 4) cv::cvtColor(bgr, bgr, cv::COLOR_BGRA2BGR);

    Preprocess(bgr, st.input_w, st.input_h, blob, scale, padX, padY);

    auto input_port = st.compiled.input();
    ov::Tensor input_tensor(input_port.get_element_type(), input_port.get_shape(), blob.data());
    st.infer.set_input_tensor(input_tensor);
    st.infer.infer();

    auto out = st.infer.get_output_tensor(outputIndex);
    auto s = out.get_shape();
    shape.assign(s.begin(), s.end());
    return out.data<float>();
}

// 把 letterbox 坐标还原到原图
inline float UnpadX(float x, float scale, int padX) { return (x - padX) / scale; }
inline float UnpadY(float y, float scale, int padY) { return (y - padY) / scale; }

} // namespace

// ---------------------- 语义分割 ----------------------

int DlSegCreate(Context* ctx, const char* model_path, const char* device)
{
    return CreateCommon(ctx, ctx->seg, VA_DL_TASK_SEG, model_path, device);
}

int DlSegRun(Context* ctx, const VA_Image* img, float score_thresh, float iou_thresh,
             VA_DlBox* out_boxes, int max_count, int* out_count)
{
    if (out_boxes == 0 || out_count == 0 || max_count <= 0) { SetError(ctx, "null argument"); return VA_ERR_ARG; }
    *out_count = 0;

    DlState& st = ctx->seg;
    std::vector<float> blob;
    cv::Mat bgr;
    float scale = 1.0f; int padX = 0, padY = 0;
    std::vector<size_t> shape;

    try
    {
        const float* data = Forward(ctx, st, img, blob, bgr, scale, padX, padY, shape, 0);
        if (data == 0) return ctx->lastError.empty() ? VA_ERR_INTERNAL : VA_ERR_ARG;

        int rank = (int)shape.size();
        int d0 = (int)shape[rank - 2], d1 = (int)shape[rank - 1];
        int C = std::min(d0, d1);
        int A = std::max(d0, d1);
        bool channelsFirst = (d0 == C);
        int nc = st.num_classes;
        int coefCount = C - 4 - nc;             // 32
        if (coefCount < 0) { SetError(ctx, "seg output channels mismatch"); return VA_ERR_INTERNAL; }

        auto idx = [&](int a, int c) -> size_t {
            return channelsFirst ? ((size_t)c * A + a) : ((size_t)a * C + c);
        };

        std::vector<DlBox> cands;
        for (int a = 0; a < A; ++a)
        {
            float best = 0.0f; int bestC = -1;
            for (int c = 0; c < nc; ++c) {
                float s = data[idx(a, 4 + c)];
                if (s > best) { best = s; bestC = c; }
            }
            if (bestC < 0 || best < score_thresh) continue;

            float cx = data[idx(a, 0)], cy = data[idx(a, 1)];
            float w = data[idx(a, 2)], h = data[idx(a, 3)];
            if (w <= 1.0f || h <= 1.0f) continue;

            DlBox b;
            b.x = UnpadX(cx - w * 0.5f, scale, padX);
            b.y = UnpadY(cy - h * 0.5f, scale, padY);
            b.w = w / scale;
            b.h = h / scale;
            b.cls = bestC;
            b.score = best;
            b.maskArea = 0;

            b.coef.resize(coefCount);
            for (int k = 0; k < coefCount; ++k) b.coef[k] = data[idx(a, 4 + nc + k)];
            cands.push_back(b);
        }

        std::vector<DlBox> kept;
        NMS(cands, iou_thresh, kept);

        // 掩膜面积：sigmoid(coef · proto) > 0.5 的像素数（在框内、按 2px 步长抽样，够用且省时）
        if (!kept.empty() && st.compiled.outputs().size() > 1)
        {
            auto proto = st.infer.get_output_tensor(1);
            auto ps = proto.get_shape();     // [1, 32, ph, pw]
            if (ps.size() == 4 && (int)ps[1] == coefCount)
            {
                const float* pd = proto.data<float>();
                int ph = (int)ps[2], pw = (int)ps[3];
                float sx = (float)pw / (float)st.input_w;
                float sy = (float)ph / (float)st.input_h;

                for (size_t i = 0; i < kept.size(); ++i)
                {
                    DlBox& b = kept[i];
                    int x0 = std::max(0, (int)((b.x * scale + padX) * sx));
                    int y0 = std::max(0, (int)((b.y * scale + padY) * sy));
                    int x1 = std::min(pw - 1, (int)(((b.x + b.w) * scale + padX) * sx));
                    int y1 = std::min(ph - 1, (int)(((b.y + b.h) * scale + padY) * sy));

                    int count = 0;
                    for (int py = y0; py <= y1; py += 2) {
                        for (int px = x0; px <= x1; px += 2) {
                            float acc = 0.0f;
                            for (int k = 0; k < coefCount; ++k)
                                acc += b.coef[k] * pd[(size_t)k * ph * pw + (size_t)py * pw + px];
                            if (acc > 0.0f) count += 4;   // 2×2 抽样折算
                        }
                    }
                    b.maskArea = count;
                }
            }
        }

        int n = (int)kept.size();
        int wrote = std::min(n, max_count);
        for (int i = 0; i < wrote; ++i)
        {
            out_boxes[i].x = kept[i].x;
            out_boxes[i].y = kept[i].y;
            out_boxes[i].w = kept[i].w;
            out_boxes[i].h = kept[i].h;
            out_boxes[i].class_id = kept[i].cls;
            out_boxes[i].score = kept[i].score;
            out_boxes[i].mask_area = kept[i].maskArea;
        }
        *out_count = wrote;
        return (n > max_count) ? VA_ERR_SIZE : VA_OK;
    }
    catch (const std::exception& e) { SetError(ctx, e); return VA_ERR_INTERNAL; }
}

// ---------------------- 姿态估计 ----------------------

int DlPoseCreate(Context* ctx, const char* model_path, const char* device)
{
    return CreateCommon(ctx, ctx->pose, VA_DL_TASK_POSE, model_path, device);
}

int DlPoseRun(Context* ctx, const VA_Image* img, float score_thresh, float iou_thresh,
              VA_DlBox* out_boxes, int max_count, int* out_count,
              VA_DlKeyPoint* out_kps, int kps_capacity,
              int* out_kps_per_box, int* out_kps_written)
{
    if (out_boxes == 0 || out_count == 0 || max_count <= 0) { SetError(ctx, "null argument"); return VA_ERR_ARG; }
    *out_count = 0;
    if (out_kps_per_box) *out_kps_per_box = 0;
    if (out_kps_written) *out_kps_written = 0;

    DlState& st = ctx->pose;
    std::vector<float> blob;
    cv::Mat bgr;
    float scale = 1.0f; int padX = 0, padY = 0;
    std::vector<size_t> shape;

    try
    {
        const float* data = Forward(ctx, st, img, blob, bgr, scale, padX, padY, shape, 0);
        if (data == 0) return ctx->lastError.empty() ? VA_ERR_INTERNAL : VA_ERR_ARG;

        int rank = (int)shape.size();
        int d0 = (int)shape[rank - 2], d1 = (int)shape[rank - 1];
        int C = std::min(d0, d1);
        int A = std::max(d0, d1);
        bool channelsFirst = (d0 == C);
        int nc = st.num_classes;
        int k = st.keypoints;
        if (C < 4 + nc + 3 * k) { SetError(ctx, "pose output channels mismatch"); return VA_ERR_INTERNAL; }

        auto idx = [&](int a, int c) -> size_t {
            return channelsFirst ? ((size_t)c * A + a) : ((size_t)a * C + c);
        };

        std::vector<DlBox> cands;
        for (int a = 0; a < A; ++a)
        {
            float best = 0.0f; int bestC = -1;
            for (int c = 0; c < nc; ++c) {
                float s = data[idx(a, 4 + c)];
                if (s > best) { best = s; bestC = c; }
            }
            if (bestC < 0 || best < score_thresh) continue;

            float cx = data[idx(a, 0)], cy = data[idx(a, 1)];
            float w = data[idx(a, 2)], h = data[idx(a, 3)];
            if (w <= 1.0f || h <= 1.0f) continue;

            DlBox b;
            b.x = UnpadX(cx - w * 0.5f, scale, padX);
            b.y = UnpadY(cy - h * 0.5f, scale, padY);
            b.w = w / scale;
            b.h = h / scale;
            b.cls = bestC;
            b.score = best;
            b.maskArea = 0;

            b.kps.resize((size_t)3 * k);
            for (int i = 0; i < k; ++i)
            {
                b.kps[(size_t)3 * i + 0] = UnpadX(data[idx(a, 4 + nc + 3 * i + 0)], scale, padX);
                b.kps[(size_t)3 * i + 1] = UnpadY(data[idx(a, 4 + nc + 3 * i + 1)], scale, padY);
                b.kps[(size_t)3 * i + 2] = data[idx(a, 4 + nc + 3 * i + 2)];
            }
            cands.push_back(b);
        }

        std::vector<DlBox> kept;
        NMS(cands, iou_thresh, kept);

        int n = (int)kept.size();
        int wrote = std::min(n, max_count);
        int kpCapPerBox = (out_kps != 0 && kps_capacity > 0) ? (kps_capacity / std::max(1, wrote)) : 0;
        int kpWritten = 0;

        for (int i = 0; i < wrote; ++i)
        {
            out_boxes[i].x = kept[i].x;
            out_boxes[i].y = kept[i].y;
            out_boxes[i].w = kept[i].w;
            out_boxes[i].h = kept[i].h;
            out_boxes[i].class_id = kept[i].cls;
            out_boxes[i].score = kept[i].score;
            out_boxes[i].mask_area = 0;

            if (out_kps != 0 && kpCapPerBox >= k && kpWritten + k <= kps_capacity)
            {
                for (int j = 0; j < k; ++j)
                {
                    out_kps[kpWritten + j].x = kept[i].kps[(size_t)3 * j + 0];
                    out_kps[kpWritten + j].y = kept[i].kps[(size_t)3 * j + 1];
                    out_kps[kpWritten + j].score = kept[i].kps[(size_t)3 * j + 2];
                }
                kpWritten += k;
            }
        }

        *out_count = wrote;
        if (out_kps_per_box) *out_kps_per_box = k;
        if (out_kps_written) *out_kps_written = kpWritten;
        return (n > max_count) ? VA_ERR_SIZE : VA_OK;
    }
    catch (const std::exception& e) { SetError(ctx, e); return VA_ERR_INTERNAL; }
}

int DlPoseKeypoints(Context* ctx, int* out_k)
{
    if (out_k == 0) { SetError(ctx, "null argument"); return VA_ERR_ARG; }
    *out_k = ctx->pose.keypoints;
    return VA_OK;
}

// ---------------------- 深度估计 ----------------------

int DlDepthCreate(Context* ctx, const char* model_path, const char* device)
{
    return CreateCommon(ctx, ctx->depth, VA_DL_TASK_DEPTH, model_path, device);
}

int DlDepthRun(Context* ctx, const VA_Image* img, float* out_map, int width, int height,
               float* out_min, float* out_max)
{
    if (out_map == 0 || width <= 0 || height <= 0) { SetError(ctx, "null argument"); return VA_ERR_ARG; }

    DlState& st = ctx->depth;
    std::vector<float> blob;
    cv::Mat bgr;
    float scale = 1.0f; int padX = 0, padY = 0;
    std::vector<size_t> shape;

    try
    {
        const float* data = Forward(ctx, st, img, blob, bgr, scale, padX, padY, shape, 0);
        if (data == 0) return ctx->lastError.empty() ? VA_ERR_INTERNAL : VA_ERR_ARG;

        // 输出可能是 [1,1,H,W] 或 [1,H,W]
        int h = 0, w = 0;
        if (shape.size() == 4) { h = (int)shape[2]; w = (int)shape[3]; }
        else if (shape.size() == 3) { h = (int)shape[1]; w = (int)shape[2]; }
        else if (shape.size() == 2) { h = (int)shape[0]; w = (int)shape[1]; }
        if (h <= 0 || w <= 0) { SetError(ctx, "unexpected depth output shape"); return VA_ERR_INTERNAL; }

        float mn = data[0], mx = data[0];
        for (int i = 1; i < w * h; ++i)
        {
            float v = data[i];
            if (v < mn) mn = v;
            if (v > mx) mx = v;
        }
        float span = (mx - mn) > 1e-6f ? (mx - mn) : 1.0f;

        // 双线性缩放到调用方尺寸（最近邻足够，且不会引入插值伪影）
        for (int y = 0; y < height; ++y)
        {
            int sy = (int)((float)y * h / height);
            if (sy >= h) sy = h - 1;
            for (int x = 0; x < width; ++x)
            {
                int sx = (int)((float)x * w / width);
                if (sx >= w) sx = w - 1;
                out_map[(size_t)y * width + x] = (data[(size_t)sy * w + sx] - mn) / span;
            }
        }

        if (out_min) *out_min = mn;
        if (out_max) *out_max = mx;
        return VA_OK;
    }
    catch (const std::exception& e) { SetError(ctx, e); return VA_ERR_INTERNAL; }
}

// ---------------------- 公共 ----------------------

int DlClear(Context* ctx, int task)
{
    DlState* st = 0;
    if (task == VA_DL_TASK_SEG) st = &ctx->seg;
    else if (task == VA_DL_TASK_POSE) st = &ctx->pose;
    else if (task == VA_DL_TASK_DEPTH) st = &ctx->depth;
    else { SetError(ctx, "unknown dl task"); return VA_ERR_ARG; }

    st->infer = ov::InferRequest();
    st->compiled = ov::CompiledModel();
    st->created = false;
    return VA_OK;
}

int DlBackendAvailable(int backend)
{
    // CUDA 预留：接口先固定下来，实现（cuDNN/TensorRT）后续补；
    // 现在只承认 OpenVINO，UI 的下拉据此只列可用项。
    return backend == VA_DL_BACKEND_OPENVINO ? 1 : 0;
}

} // namespace va
