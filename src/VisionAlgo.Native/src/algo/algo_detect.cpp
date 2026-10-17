// YOLO 目标检测：OpenVINO 推理（Ultralytics 导出 ONNX，输出 [1, 4+nc, anchors]）。
// 状态挂在 va::Context 上（一个句柄 = 一条流程）。无额外第三方依赖（仅 OpenVINO + OpenCV）。
#include "va_internal.h"
#include "va_error.h"

#include <algorithm>
#include <cmath>
#include <fstream>
#include <sstream>
#include <vector>

namespace va {

namespace {

// 类别名不再内嵌于源码：必须提供与模型同名的 .txt 侧车文件（每行一个类别名）。
// 缺失时 DetCreate 直接报错，避免静默使用错误类别。

struct Box {
    float x, y, w, h;   // 原图像素坐标（左上角 + 宽高）
    int   cls;
    float score;
};

// 读侧车标签文件：<model 去扩展名>.txt，每行一个类名
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

// letterbox 缩放 + BGR→RGB + /255 + HWC→NCHW，写入 blob（容量 3*inW*inH）。
// 同时回传 scale 与 pad，供推理后把目标框还原到原图坐标。
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
    cv::cvtColor(padded, padded, cv::COLOR_BGR2RGB);   // 转 RGB

    blob.assign((size_t)3 * inW * inH, 0.0f);
    const unsigned char* p = padded.data;
    const int wh = inW * inH;
    for (int i = 0; i < wh; ++i) {
        // 原图 BGR，padded 已是 RGB：R=p[i*3+0], G=p[i*3+1], B=p[i*3+2]
        blob[(size_t)0 * wh + i] = (float)p[i * 3 + 0] / 255.0f;
        blob[(size_t)1 * wh + i] = (float)p[i * 3 + 1] / 255.0f;
        blob[(size_t)2 * wh + i] = (float)p[i * 3 + 2] / 255.0f;
    }
}

float IoU(const Box& a, const Box& b)
{
    float xa = std::max(a.x, b.x), ya = std::max(a.y, b.y);
    float xb = std::min(a.x + a.w, b.x + b.w), yb = std::min(a.y + a.h, b.y + b.h);
    float iw = std::max(0.0f, xb - xa), ih = std::max(0.0f, yb - ya);
    float inter = iw * ih;
    float uni = a.w * a.h + b.w * b.h - inter;
    return (uni > 0.0f) ? (inter / uni) : 0.0f;
}

// 按类别分组、组内按 score 降序做贪心 NMS
void NMS(const std::vector<Box>& boxes, float iouThresh, std::vector<Box>& out)
{
    std::vector<Box> sorted = boxes;
    std::sort(sorted.begin(), sorted.end(),
              [](const Box& a, const Box& b) { return a.score > b.score; });

    std::vector<bool> suppressed(sorted.size(), false);
    for (size_t i = 0; i < sorted.size(); ++i) {
        if (suppressed[i]) continue;
        out.push_back(sorted[i]);
        for (size_t j = i + 1; j < sorted.size(); ++j) {
            if (suppressed[j]) continue;
            if (sorted[j].cls != sorted[i].cls) continue;
            if (IoU(sorted[i], sorted[j]) > iouThresh) suppressed[j] = true;
        }
    }
}

} // namespace

int DetCreate(Context* ctx, const char* model_path, const char* device)
{
    if (model_path == 0 || model_path[0] == '\0') {
        SetError(ctx, "null model path"); return VA_ERR_ARG;
    }
    std::string dev = (device && device[0] != '\0') ? device : "CPU";

    try
    {
        std::shared_ptr<ov::Model> model = ctx->det.core.read_model(std::string(model_path));

        // 输入形状：Ultralytics 默认导出为动态形状（dynamic=True），直接 get_shape() 会抛异常。
        // 若能读到静态空间维度就沿用；否则回退到标准 YOLO 输入尺寸 640（imgsz 默认 640）。
        ov::Shape inShape = { 1, 3, 640, 640 };
        try {
            auto ps = model->input().get_partial_shape();
            if (ps.rank().is_static() && ps.size() == 4) {
                bool allStatic = true;
                for (size_t i = 0; i < 4; ++i) if (!ps[i].is_static()) { allStatic = false; break; }
                if (allStatic) {
                    inShape[2] = (size_t)ps[2].get_length();
                    inShape[3] = (size_t)ps[3].get_length();
                }
            }
        } catch (...) { /* 保留默认 640 */ }

        // 把动态形状固定为具体尺寸，便于后续读取静态形状并编译
        try { model->reshape(ov::PartialShape({ (int64_t)inShape[0], (int64_t)inShape[1],
                                                (int64_t)inShape[2], (int64_t)inShape[3] })); }
        catch (const std::exception&) { /* 已是静态则忽略 */ }

        // 输入形状：期望 {1, 3, H, W}（reshape 后已是静态）
        auto in_shape = model->input().get_shape();
        int inW = 0, inH = 0;
        if (in_shape.size() == 4) {
            inH = (int)in_shape[2];
            inW = (int)in_shape[3];
        }

        // 输出形状：最后一个维度是 4 + num_classes 或 anchors；Ultralytics 导出为
        // [1, 4+nc, anchors]（通道在前）或 [1, anchors, 4+nc]（通道在后）。
        // anchors(约 8400) 远大于 4+nc(约 84)，取较小者作为 4+nc。
        auto out_shape = model->output().get_shape();
        if (out_shape.size() < 2) {
            SetError(ctx, "unexpected YOLO output rank");
            return VA_ERR_ARG;
        }
        int od0 = (int)out_shape[out_shape.size() - 2];
        int od1 = (int)out_shape[out_shape.size() - 1];
        int numClasses = std::min(od0, od1) - 4;

        if (inW <= 0 || inH <= 0 || numClasses <= 0) {
            SetError(ctx, "unexpected YOLO model I/O shape (need [1,3,H,W] in / [1,4+nc,A] out)");
            return VA_ERR_ARG;
        }

        ov::CompiledModel compiled = ctx->det.core.compile_model(model, dev);
        ov::InferRequest infer = compiled.create_infer_request();

        // 类别名：必须提供与模型同名的 .txt 侧车文件（每行一个类别名）。缺失即报错。
        std::vector<std::string> names;
        if (!LoadNames(model_path, names)) {
            SetError(ctx, ("类别文件未找到：请在模型同目录放置 " + std::string(model_path) +
                           " 去扩展名后的 .txt（每行一个类别名）").c_str());
            return VA_ERR_ARG;
        }
        // 类别数不足时按 class_N 补齐，避免越界
        while ((int)names.size() < numClasses)
            names.push_back("class_" + std::to_string(names.size()));

        ctx->det.compiled    = std::move(compiled);
        ctx->det.infer       = std::move(infer);
        ctx->det.input_w     = inW;
        ctx->det.input_h     = inH;
        ctx->det.num_classes = numClasses;
        ctx->det.class_names = std::move(names);
        ctx->det.created     = true;
        return VA_OK;
    }
    catch (const std::exception& e) { SetError(ctx, e); return VA_ERR_INTERNAL; }
}

int DetRun(Context* ctx, const VA_Image* img, float score_thresh, float iou_thresh,
           VA_DetectResult* out_results, int max_count, int* out_count)
{
    if (out_results == 0 || max_count <= 0 || out_count == 0) {
        SetError(ctx, "null argument"); return VA_ERR_ARG;
    }
    if (!ctx->det.created) {
        SetError(ctx, "detector not created"); return VA_ERR_NO_TEMPLATE;
    }
    if (img == 0) { SetError(ctx, "null image"); return VA_ERR_ARG; }

    try
    {
        cv::Mat bgr;
        if (!ToMat(img, bgr, ctx->lastError)) return VA_ERR_ARG;
        if (bgr.channels() == 1)       cv::cvtColor(bgr, bgr, cv::COLOR_GRAY2BGR);
        else if (bgr.channels() == 4)  cv::cvtColor(bgr, bgr, cv::COLOR_BGRA2BGR);

        int inW = ctx->det.input_w, inH = ctx->det.input_h;
        std::vector<float> blob;
        float scale = 1.0f; int padX = 0, padY = 0;
        Preprocess(bgr, inW, inH, blob, scale, padX, padY);

        // 设置输入张量（引用 blob 内存，推断期间 blob 保持存活）
        auto input_port = ctx->det.compiled.input();
        ov::Tensor input_tensor(input_port.get_element_type(), input_port.get_shape(), blob.data());
        ctx->det.infer.set_input_tensor(input_tensor);

        ctx->det.infer.infer();

        auto out_tensor = ctx->det.infer.get_output_tensor();
        const float* data = out_tensor.data<float>();
        auto out_shape = out_tensor.get_shape();   // [1, C, A] 或 [1, A, C]
        int rank = (int)out_shape.size();
        if (rank < 2) { SetError(ctx, "unexpected YOLO output rank"); return VA_ERR_INTERNAL; }

        // 两个尾部维度：一个是 4 + numClasses（通道），一个是 anchors。
        // anchors(约 8400) 远大于 4+nc(约 84)，取较小者为 4+nc。
        int dimA = (int)out_shape[rank - 2];
        int dimB = (int)out_shape[rank - 1];
        int C = std::min(dimA, dimB);     // 4 + numClasses
        int A = std::max(dimA, dimB);     // anchors
        int nc = C - 4;
        int anchors = A;
        bool channelsFirst = ((int)out_shape[rank - 2] == C);  // [.., C, A] 否则 [.., A, C]

        if (nc <= 0 || anchors <= 0) { SetError(ctx, "unexpected YOLO output shape"); return VA_ERR_INTERNAL; }

        // 按布局求 (anchor a, channel c) 的扁平下标（C 序：通道在前时为 c*A+a，通道在后时为 a*C+c）
        auto idx = [&](int a, int c) -> size_t {
            return channelsFirst ? ((size_t)c * (size_t)A + (size_t)a)
                                 : ((size_t)a * (size_t)C + (size_t)c);
        };

        // ===== 一次性诊断：把输出张量格式样本写到 exe 同目录 detect_diag.txt =====
        // 本环境无法离线加载 onnx，用一次真实推理落盘，定位坐标 / 类别解析问题。
        // 若运行后该文件未生成，说明正在加载的是旧版（未含本段）原生 DLL。
        {
            static bool s_diag = false;
            if (!s_diag) {
                s_diag = true;
                std::ofstream dg("detect_diag.txt");
                if (dg.is_open()) {
                    dg << "rank=" << rank << "\n";
                    dg << "dims=";
                    for (size_t z = 0; z < out_shape.size(); ++z) dg << (long long)out_shape[z] << " ";
                    dg << "\n";
                    dg << "C=" << C << " A=" << A << " nc=" << nc << " anchors=" << anchors
                       << " channelsFirst=" << (int)channelsFirst << "\n";
                    dg << "scale=" << scale << " padX=" << padX << " padY=" << padY
                       << " inW=" << inW << " inH=" << inH << "\n";
                    for (int a = 0; a < anchors && a < 2; ++a) {
                        dg << "--- anchor " << a << " channelsFirst(c*A+a) ---\n";
                        for (int c = 0; c < nc + 4 && c < 12; ++c)
                            dg << "  ch" << c << "=" << data[(size_t)c * (size_t)A + a] << "\n";
                        dg << "--- anchor " << a << " channelsLast(a*C+c) ---\n";
                        for (int c = 0; c < nc + 4 && c < 12; ++c)
                            dg << "  ch" << c << "=" << data[(size_t)a * (size_t)C + c] << "\n";
                    }
                    float bxMin = 1e30f, bxMax = -1e30f, scMax = -1e30f;
                    for (int a = 0; a < anchors; ++a) {
                        float cx = data[idx(a,0)], cy = data[idx(a,1)], w = data[idx(a,2)], h = data[idx(a,3)];
                        bxMin = std::min(bxMin, std::min(cx, std::min(cy, std::min(w, h))));
                        bxMax = std::max(bxMax, std::max(cx, std::max(cy, std::max(w, h))));
                        for (int c = 0; c < nc; ++c) scMax = std::max(scMax, data[idx(a, 4 + c)]);
                    }
                    dg << "boxValRange=[" << bxMin << "," << bxMax << "] maxClassRaw=" << scMax << "\n";
                    dg.close();
                }
            }
        }

        // 解析：每个 anchor 取最大类，超过阈值则视为候选
        std::vector<Box> candidates;
        for (int a = 0; a < anchors; ++a) {
            float cx = data[idx(a, 0)], cy = data[idx(a, 1)], w = data[idx(a, 2)], h = data[idx(a, 3)];

            // 退化框（宽高非正）直接丢弃，不参与后续阈值 / NMS
            if (w <= 0.0f || h <= 0.0f) continue;

            int bestCls = 0; float bestScore = -1e30f;
            for (int c = 0; c < nc; ++c) {
                float s = 1.0f / (1.0f + std::exp(-data[idx(a, 4 + c)]));   // sigmoid
                if (s > bestScore) { bestScore = s; bestCls = c; }
            }
            if (bestScore < score_thresh) continue;

            // 目标输入空间 → 原图坐标（letterbox 逆变换）
            Box b;
            b.x = (cx - w * 0.5f - (float)padX) / scale;
            b.y = (cy - h * 0.5f - (float)padY) / scale;
            b.w = w / scale;
            b.h = h / scale;
            b.cls = bestCls;
            b.score = bestScore;
            candidates.push_back(b);
        }

        std::vector<Box> kept;
        NMS(candidates, iou_thresh, kept);

        int n = (int)kept.size();
        *out_count = n;
        int writeN = (n > max_count) ? max_count : n;
        for (int i = 0; i < writeN; ++i) {
            out_results[i].x = kept[i].x;
            out_results[i].y = kept[i].y;
            out_results[i].w = kept[i].w;
            out_results[i].h = kept[i].h;
            out_results[i].class_id = kept[i].cls;
            out_results[i].score = kept[i].score;
        }
        return (writeN < n) ? VA_ERR_SIZE : VA_OK;
    }
    catch (const std::exception& e) { SetError(ctx, e); return VA_ERR_INTERNAL; }
}

int DetClassCount(Context* ctx, int* out_count)
{
    if (out_count == 0) { SetError(ctx, "null argument"); return VA_ERR_ARG; }
    *out_count = ctx->det.num_classes;
    return VA_OK;
}

int DetClassName(Context* ctx, int idx, char* out_buf, int buf_size)
{
    if (out_buf == 0 || buf_size <= 0) return 0;
    if (idx < 0 || idx >= (int)ctx->det.class_names.size()) return 0;

    const std::string& s = ctx->det.class_names[idx];
    int n = (int)s.size();
    if (n > buf_size - 1) n = buf_size - 1;
    std::memcpy(out_buf, s.c_str(), (size_t)n);
    out_buf[n] = '\0';
    return n;
}

int DetClear(Context* ctx)
{
    ctx->det.compiled = ov::CompiledModel();
    ctx->det.infer    = ov::InferRequest();
    ctx->det.class_names.clear();
    ctx->det.num_classes = 0;
    ctx->det.input_w = ctx->det.input_h = 0;
    ctx->det.created = false;
    return VA_OK;
}

} // namespace va
