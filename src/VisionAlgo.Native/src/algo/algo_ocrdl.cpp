// 深度学习 OCR（PaddleOCR）：DB 检测 + CRNN 识别，OpenVINO 推理。
// 与传统 OCR（algo_ocr.cpp，模板匹配）完全独立：两套模型、两套状态、两套 API。
#include "va_internal.h"
#include "va_error.h"

#include <algorithm>
#include <cmath>
#include <cstring>
#include <fstream>
#include <string>
#include <vector>

namespace va {

namespace {

struct OcrDlLineBox {
    cv::Rect2f rect;
    float score;
    std::string text;
};

void DetPreprocess(const cv::Mat& bgr, int inW, int inH, std::vector<float>& blob)
{
    cv::Mat resized, rgb;
    cv::resize(bgr, resized, cv::Size(inW, inH), 0, 0, cv::INTER_LINEAR);
    cv::cvtColor(resized, rgb, cv::COLOR_BGR2RGB);

    blob.assign((size_t)3 * inW * inH, 0.0f);
    const unsigned char* p = rgb.data;
    const int wh = inW * inH;
    const float kInv255 = 1.0f / 255.0f;   // 乘倒数，避免 360 万次 divss
    for (int i = 0; i < wh; ++i) {
        blob[(size_t)0 * wh + i] = (float)p[i * 3 + 0] * kInv255;
        blob[(size_t)1 * wh + i] = (float)p[i * 3 + 1] * kInv255;
        blob[(size_t)2 * wh + i] = (float)p[i * 3 + 2] * kInv255;
    }
}

// 识别输入：按比例缩放到高 48，宽度不足补 0（右侧），最多 recW
void RecPreprocess(const cv::Mat& bgr, int recW, int recH, std::vector<float>& blob)
{
    float s = std::min((float)recH / (float)bgr.rows, (float)recW / (float)bgr.cols);
    int nw = std::max(1, (int)std::lround(bgr.cols * s));
    int nh = std::max(1, (int)std::lround(bgr.rows * s));
    nw = std::min(nw, recW);

    cv::Mat resized;
    cv::resize(bgr, resized, cv::Size(nw, nh), 0, 0, cv::INTER_LINEAR);

    cv::Mat canvas(recH, recW, CV_8UC3, cv::Scalar(0, 0, 0));
    resized.copyTo(canvas(cv::Rect(0, 0, nw, std::min(nh, recH))));

    cv::Mat rgb;
    cv::cvtColor(canvas, rgb, cv::COLOR_BGR2RGB);

    blob.assign((size_t)3 * recW * recH, 0.0f);
    const unsigned char* p = rgb.data;
    const int wh = recW * recH;
    const float kInv255 = 1.0f / 255.0f;
    for (int i = 0; i < wh; ++i) {
        blob[(size_t)0 * wh + i] = (float)p[i * 3 + 0] * kInv255;
        blob[(size_t)1 * wh + i] = (float)p[i * 3 + 1] * kInv255;
        blob[(size_t)2 * wh + i] = (float)p[i * 3 + 2] * kInv255;
    }
}

// UTF-8 追加一个码点
void AppendUtf8(std::string& out, int cp)
{
    if (cp < 0x80) out.push_back((char)cp);
    else if (cp < 0x800) {
        out.push_back((char)(0xC0 | (cp >> 6)));
        out.push_back((char)(0x80 | (cp & 0x3F)));
    }
    else if (cp < 0x10000) {
        out.push_back((char)(0xE0 | (cp >> 12)));
        out.push_back((char)(0x80 | ((cp >> 6) & 0x3F)));
        out.push_back((char)(0x80 | (cp & 0x3F)));
    }
    else {
        out.push_back((char)(0xF0 | (cp >> 18)));
        out.push_back((char)(0x80 | ((cp >> 12) & 0x3F)));
        out.push_back((char)(0x80 | ((cp >> 6) & 0x3F)));
        out.push_back((char)(0x80 | (cp & 0x3F)));
    }
}

// 读字典：每行一个字符（UTF-8），返回码点序列；索引 0 由调用方当作 blank 保留
bool LoadDict(const char* path, std::vector<std::string>& chars)
{
    std::ifstream f(path);
    if (!f.is_open()) return false;

    std::string line;
    while (std::getline(f, line)) {
        if (!line.empty() && line.back() == '\r') line.pop_back();
        chars.push_back(line);     // 允许空行（占位）
    }
    return !chars.empty();
}

} // namespace

int OcrDlCreate(Context* ctx, const char* det_model, const char* rec_model,
                const char* dict_path, const char* device)
{
    // det 允许为空：此时退化成"整图识别"（一个 rec 模型即可，适合一行文本 / 铭牌）
    if (rec_model == 0 || rec_model[0] == '\0') {
        SetError(ctx, "rec_model path empty");
        return VA_ERR_ARG;
    }
    bool hasDet = (det_model != 0 && det_model[0] != '\0');
    if (dict_path == 0 || dict_path[0] == '\0') {
        SetError(ctx, "dict path empty");
        return VA_ERR_ARG;
    }

    const char* dev = (device != 0 && device[0] != '\0') ? device : "CPU";
    if (_stricmp(dev, "CUDA") == 0) {
        SetError(ctx, "CUDA backend not implemented yet (reserved); use CPU/GPU/NPU for now");
        return VA_ERR_ARG;
    }

    try
    {
        std::vector<std::string> chars;
        if (!LoadDict(dict_path, chars)) {
            SetError(ctx, "dictionary file not readable");
            return VA_ERR_ARG;
        }

        ov::Core core;
        ApplyThreadBudget(core);
        std::shared_ptr<ov::Model> rec = core.read_model(rec_model);

        auto rs = rec->input().get_shape();
        int recW = (int)rs[3], recH = (int)rs[2];

        // 动态形状：按常用值固定（rec 48 高、320 宽），保证可推理
        if (recW <= 0 || recH <= 0) {
            recW = 320; recH = 48;
            rec->reshape(ov::PartialShape({ 1, 3, 48, 320 }));
        }

        int detW = 0, detH = 0;
        if (hasDet)
        {
            std::shared_ptr<ov::Model> det = core.read_model(det_model);
            auto ds = det->input().get_shape();
            detW = (int)ds[3]; detH = (int)ds[2];
            if (detW <= 0 || detH <= 0) {
                detW = detH = 960;
                det->reshape(ov::PartialShape({ 1, 3, 960, 960 }));
            }
            ctx->ocrdl.det_compiled = core.compile_model(det, dev);
            ctx->ocrdl.det_infer = ctx->ocrdl.det_compiled.create_infer_request();
        }

        ctx->ocrdl.rec_compiled = core.compile_model(rec, dev);
        ctx->ocrdl.rec_infer = ctx->ocrdl.rec_compiled.create_infer_request();
        ctx->ocrdl.has_det = hasDet;
        ctx->ocrdl.det_w = detW;
        ctx->ocrdl.det_h = detH;
        ctx->ocrdl.rec_w = recW;
        ctx->ocrdl.rec_h = recH;
        ctx->ocrdl.chars = chars;
        ctx->ocrdl.created = true;
        return VA_OK;
    }
    catch (const std::exception& e) { SetError(ctx, e); return VA_ERR_INTERNAL; }
}

int OcrDlRun(Context* ctx, const VA_Image* img, float box_thresh, float unclip_ratio,
             VA_OcrDlLine* out_lines, int max_count, int* out_count,
             char* out_text, int text_cap, int* out_text_len)
{
    if (out_lines == 0 || out_count == 0 || max_count <= 0) { SetError(ctx, "null argument"); return VA_ERR_ARG; }
    *out_count = 0;
    if (out_text && text_cap > 0) out_text[0] = '\0';
    if (out_text_len) *out_text_len = 0;

    if (!ctx->ocrdl.created) { SetError(ctx, "ocr-dl not created"); return VA_ERR_NO_TEMPLATE; }
    if (img == 0) { SetError(ctx, "null image"); return VA_ERR_ARG; }

    try
    {
        cv::Mat bgr;
        if (!ToMat(img, bgr, ctx->lastError)) return VA_ERR_ARG;
        if (bgr.channels() == 1)      cv::cvtColor(bgr, bgr, cv::COLOR_GRAY2BGR);
        else if (bgr.channels() == 4) cv::cvtColor(bgr, bgr, cv::COLOR_BGRA2BGR);

        std::vector<OcrDlLineBox> boxes;

        // ---------- 1) 文本检测（DB）----------
        // 没有检测模型时跳过：整张图当成一个文本行（铭牌 / 单行标签场景，只用一个 rec 模型）
        if (!ctx->ocrdl.has_det)
        {
            OcrDlLineBox whole;
            whole.rect = cv::Rect2f(0, 0, (float)bgr.cols, (float)bgr.rows);
            whole.score = 1.0f;
            boxes.push_back(whole);
        }
        else
        {
        std::vector<float> blob;
        DetPreprocess(bgr, ctx->ocrdl.det_w, ctx->ocrdl.det_h, blob);

        auto det_port = ctx->ocrdl.det_compiled.input();
        ov::Tensor det_in(det_port.get_element_type(), det_port.get_shape(), blob.data());
        ctx->ocrdl.det_infer.set_input_tensor(det_in);
        ctx->ocrdl.det_infer.infer();

        auto prob = ctx->ocrdl.det_infer.get_output_tensor();
        auto ps = prob.get_shape();
        int ph = 0, pw = 0;
        if (ps.size() == 4) { ph = (int)ps[2]; pw = (int)ps[3]; }
        else if (ps.size() == 3) { ph = (int)ps[1]; pw = (int)ps[2]; }
        if (ph <= 0 || pw <= 0) { SetError(ctx, "unexpected det output shape"); return VA_ERR_INTERNAL; }

        const float* pd = prob.data<float>();
        cv::Mat mask(ph, pw, CV_8UC1, cv::Scalar(0));
        for (int y = 0; y < ph; ++y)
            for (int x = 0; x < pw; ++x)
                mask.at<unsigned char>(y, x) = (pd[(size_t)y * pw + x] > box_thresh) ? 255 : 0;

        std::vector<std::vector<cv::Point>> contours;
        cv::findContours(mask, contours, cv::RETR_EXTERNAL, cv::CHAIN_APPROX_SIMPLE);

        float sx = (float)bgr.cols / (float)pw;
        float sy = (float)bgr.rows / (float)ph;

        for (size_t i = 0; i < contours.size(); ++i)
        {
            cv::RotatedRect rr = cv::minAreaRect(contours[i]);
            float w = rr.size.width, h = rr.size.height;
            if (w < 4.0f || h < 4.0f) continue;           // 噪点
            if (std::max(w, h) < 8.0f) continue;
            if (std::min(w, h) / std::max(w, h) > 0.9f && std::max(w, h) < 20.0f) continue;

            // 简化 unclip：按短边 × unclip_ratio 外扩
            float pad = std::min(w, h) * (unclip_ratio > 0.1f ? unclip_ratio : 1.5f) * 0.5f;

            OcrDlLineBox b;
            b.rect = cv::Rect2f((rr.center.x - w * 0.5f - pad) * sx,
                                (rr.center.y - h * 0.5f - pad) * sy,
                                (w + 2 * pad) * sx,
                                (h + 2 * pad) * sy);
            b.rect &= cv::Rect2f(0, 0, (float)bgr.cols, (float)bgr.rows);
            if (b.rect.width < 4 || b.rect.height < 4) continue;

            b.score = 1.0f;
            boxes.push_back(b);
        }

        // 按 y 再按 x 排序（阅读顺序）
        std::sort(boxes.begin(), boxes.end(), [](const OcrDlLineBox& a, const OcrDlLineBox& b) {
            if (std::abs(a.rect.y - b.rect.y) > a.rect.height * 0.5f) return a.rect.y < b.rect.y;
            return a.rect.x < b.rect.x;
        });
        }   // end if (has_det)

        // ---------- 2) 逐行识别（CRNN + CTC） ----------
        std::string allText;
        int wrote = 0;

        for (size_t i = 0; i < boxes.size() && wrote < max_count; ++i)
        {
            cv::Mat crop = bgr(cv::Rect((int)boxes[i].rect.x, (int)boxes[i].rect.y,
                                        (int)boxes[i].rect.width, (int)boxes[i].rect.height));
            if (crop.empty()) continue;

            std::vector<float> rblob;
            RecPreprocess(crop, ctx->ocrdl.rec_w, ctx->ocrdl.rec_h, rblob);

            auto rec_port = ctx->ocrdl.rec_compiled.input();
            ov::Tensor rec_in(rec_port.get_element_type(), rec_port.get_shape(), rblob.data());
            ctx->ocrdl.rec_infer.set_input_tensor(rec_in);
            ctx->ocrdl.rec_infer.infer();

            auto out = ctx->ocrdl.rec_infer.get_output_tensor();
            auto os = out.get_shape();     // [1, T, C]
            if (os.size() < 3) continue;

            int T = (int)os[1], C = (int)os[2];
            const float* od = out.data<float>();

            std::string text;
            int prev = -1;
            for (int t = 0; t < T; ++t)
            {
                int best = 0;
                float bestV = od[(size_t)t * C + 0];
                for (int c = 1; c < C; ++c)
                {
                    float v = od[(size_t)t * C + c];
                    if (v > bestV) { bestV = v; best = c; }
                }

                if (best == 0 || best == prev) { prev = best; continue; }   // blank / 重复
                prev = best;

                int idx = best - 1;                                        // 0 是 blank
                if (idx >= 0 && idx < (int)ctx->ocrdl.chars.size())
                    text += ctx->ocrdl.chars[idx];
            }

            if (text.empty()) continue;

            VA_OcrDlLine& line = out_lines[wrote];
            line.x = boxes[i].rect.x;
            line.y = boxes[i].rect.y;
            line.w = boxes[i].rect.width;
            line.h = boxes[i].rect.height;
            line.score = boxes[i].score;
            std::strncpy(line.text, text.c_str(), sizeof(line.text) - 1);
            line.text[sizeof(line.text) - 1] = '\0';
            wrote++;

            if (!allText.empty()) allText += " ";
            allText += text;
        }

        *out_count = wrote;

        if (out_text && text_cap > 0)
        {
            int n = (int)allText.size();
            if (n > text_cap - 1) n = text_cap - 1;
            std::memcpy(out_text, allText.data(), (size_t)n);
            out_text[n] = '\0';
            if (out_text_len) *out_text_len = n;
        }

        return ((int)boxes.size() > max_count) ? VA_ERR_SIZE : VA_OK;
    }
    catch (const std::exception& e) { SetError(ctx, e); return VA_ERR_INTERNAL; }
}

int OcrDlClear(Context* ctx)
{
    ctx->ocrdl.det_infer = ov::InferRequest();
    ctx->ocrdl.rec_infer = ov::InferRequest();
    ctx->ocrdl.det_compiled = ov::CompiledModel();
    ctx->ocrdl.rec_compiled = ov::CompiledModel();
    ctx->ocrdl.chars.clear();
    ctx->ocrdl.has_det = false;
    ctx->ocrdl.created = false;
    return VA_OK;
}

} // namespace va
