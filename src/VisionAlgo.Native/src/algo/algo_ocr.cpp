// 传统算法 OCR：连通域分割 + 归一化 + 模板匹配识别。
// 无 contrib（没有 cv::text），这里自搭流水线：
//   二值化（含极性判定） → 连通域 → 尺寸/宽高比过滤 → 水平间隙合并 → 分行排序 → 模板匹配。
//
// 【表示统一】模板与待识别 ROI 必须经过**同一套** MakeBinary + NormalizeChar，
// 否则两边一个是「黑字白底原始灰度」、一个是「白字黑底二值图」，TM_CCOEFF_NORMED 对反相输入
// 给出 ≈ -1 的分数 —— 这就是历史缺陷「结果全为 ? 0.00」的根源所在。
#include "va_internal.h"
#include "va_error.h"

#include <algorithm>
#include <cmath>
#include <cstring>
#include <string>
#include <vector>

namespace va {

namespace {

const int  DEF_MAX_GAP = 3;     // 默认同行字符最大水平间隙
const int  DEF_NORM_W  = 16;    // 默认归一化宽
const int  DEF_NORM_H  = 24;    // 默认归一化高
const double DEF_ACCEPT = 0.6;  // 默认接受阈值

// 二值化：threshold < 0 走 Otsu；invert 语义见 va_ocr.h。
//
// invert == 0（默认）= **自动判定极性**：二值化后统计前景像素占比，超过一半说明
// 「背景」被当成了前景（典型场景：白底黑字），于是整体反色。这样不论白底黑字还是
// 黑底白字，输出恒为「字 = 255、底 = 0」，上层不必理解极性。
// invert != 0 = 强制反色（保留旧语义，便于极端成像下手动干预）。
cv::Mat MakeBinary(const cv::Mat& gray, const VA_OcrParam* p)
{
    cv::Mat bin;

    int    type   = (p->threshold < 0) ? (cv::THRESH_BINARY | cv::THRESH_OTSU)
                                       : cv::THRESH_BINARY;
    double thresh = (p->threshold < 0) ? 0.0 : (double)p->threshold;
    cv::threshold(gray, bin, thresh, 255.0, type);

    bool needInvert = (p->invert != 0);
    if (!needInvert) {
        double total = (double)bin.total();
        if (total < 1.0) total = 1.0;
        double fgRatio = (double)cv::countNonZero(bin) / total;
        needInvert = (fgRatio > 0.5);
    }
    if (needInvert) cv::bitwise_not(bin, bin);

    return bin;
}

// 两个框在垂直方向上的重叠是否足够（判定为同一行）
bool VerticallyOverlap(const cv::Rect& a, const cv::Rect& b)
{
    int top = std::max(a.y, b.y);
    int bot = std::min(a.y + a.height, b.y + b.height);
    int ov  = bot - top;
    if (ov <= 0) return false;
    int minH = std::min(a.height, b.height);
    return (minH > 0) && (ov * 2 >= minH); // 重叠 >= 较小框高度的一半
}

// 把水平间隙 <= maxGap 且同一行的框两两合并（迭代到稳定，带次数上限）
void MergeByGap(std::vector<cv::Rect>& boxes, int maxGap)
{
    if (boxes.size() < 2) return;

    for (int pass = 0; pass < 8; ++pass) {
        // 先按 x 排序，保证每次只与「左邻居」比较
        std::sort(boxes.begin(), boxes.end(),
                  [](const cv::Rect& a, const cv::Rect& b) { return a.x < b.x; });

        std::vector<cv::Rect> out;
        bool changed = false;

        for (size_t i = 0; i < boxes.size(); ++i) {
            if (out.empty()) { out.push_back(boxes[i]); continue; }

            cv::Rect& prev = out.back();
            int gap = boxes[i].x - (prev.x + prev.width);
            if (gap <= maxGap && VerticallyOverlap(prev, boxes[i])) {
                prev = prev | boxes[i];   // 合并为并集外接矩形
                changed = true;
            } else {
                out.push_back(boxes[i]);
            }
        }

        boxes.swap(out);
        if (!changed) break;
    }
}

// 按 y 中心分行：相邻框 y 中心差 > 0.6 * 参考高度 时换行
std::vector<std::vector<cv::Rect> > GroupLines(const std::vector<cv::Rect>& boxes)
{
    std::vector<std::vector<cv::Rect> > lines;
    if (boxes.empty()) return lines;

    // 参考高度取所有框高度的中位数
    std::vector<int> hs;
    hs.reserve(boxes.size());
    for (size_t i = 0; i < boxes.size(); ++i) hs.push_back(boxes[i].height);
    std::sort(hs.begin(), hs.end());
    int medH = hs[hs.size() / 2];
    if (medH < 1) medH = 1;

    std::vector<std::pair<double, size_t> > order;
    order.reserve(boxes.size());
    for (size_t i = 0; i < boxes.size(); ++i)
        order.push_back(std::make_pair(boxes[i].y + boxes[i].height * 0.5, i));
    std::sort(order.begin(), order.end());

    std::vector<cv::Rect> cur;
    double lastCy = order[0].first;
    for (size_t i = 0; i < order.size(); ++i) {
        double cy = order[i].first;
        if (i > 0 && (cy - lastCy) > 0.6 * (double)medH) {
            lines.push_back(cur);
            cur.clear();
        }
        cur.push_back(boxes[order[i].second]);
        lastCy = cy;
    }
    if (!cur.empty()) lines.push_back(cur);

    // 行内按 x 排序
    for (size_t i = 0; i < lines.size(); ++i) {
        std::sort(lines[i].begin(), lines[i].end(),
                  [](const cv::Rect& a, const cv::Rect& b) { return a.x < b.x; });
    }
    return lines;
}

// 完整的分割流水线：二值化 → 连通域 → 过滤 → 合并 → 分行排序。
// 同时把二值图通过 binOut 带出，供识别阶段裁剪 ROI 复用。
std::vector<std::vector<cv::Rect> > SegmentLines(const cv::Mat& gray,
                                                 const VA_OcrParam* p,
                                                 cv::Mat& binOut)
{
    binOut = MakeBinary(gray, p);

    cv::Mat labels, stats, centroids;
    int n = cv::connectedComponentsWithStats(binOut, labels, stats, centroids);

    std::vector<cv::Rect> boxes;
    boxes.reserve((n > 1) ? (size_t)(n - 1) : 0);

    for (int i = 1; i < n; ++i) { // 0 号是背景
        double h = (double)stats.at<int>(i, cv::CC_STAT_HEIGHT);
        double w = (double)stats.at<int>(i, cv::CC_STAT_WIDTH);

        if (p->min_char_h > 0.0 && h < p->min_char_h) continue;
        if (p->max_char_h > 0.0 && h > p->max_char_h) continue;

        double aspect = (h > 1e-6) ? (w / h) : 0.0;
        if (p->min_aspect > 0.0 && aspect < p->min_aspect) continue;
        if (p->max_aspect > 0.0 && aspect > p->max_aspect) continue;

        boxes.push_back(cv::Rect((int)stats.at<int>(i, cv::CC_STAT_LEFT),
                                 (int)stats.at<int>(i, cv::CC_STAT_TOP),
                                 (int)w, (int)h));
    }

    int gap = (p->max_gap < 0) ? DEF_MAX_GAP : p->max_gap;
    MergeByGap(boxes, gap);

    return GroupLines(boxes);
}

// 字符归一化：紧裁留白 → 保持长宽比缩放 → 居中贴进 w×h 全 0 画布。
//
// 为什么必须「紧裁 + 保比例 + 居中」：
//   · 直接 resize 到 16×24 会把 1/I/l/| 与 0/O 的长宽比抹平，模板与 ROI 的
//     「图形占画布比例」也不一致 → 相关系数被系统性压低；
//   · 紧裁消掉调用方截图时带的留白差异；
//   · 居中 + 四周留 >=1px 背景，避免字形贴边导致滑窗越界。
// 模板与识别必须走同一个函数，否则又会对不齐。
cv::Mat NormalizeChar(const cv::Mat& bin, int w, int h)
{
    cv::Mat out;
    if (bin.empty() || w <= 0 || h <= 0) return out;

    cv::Mat src = bin;
    if (src.channels() != 1) src = AsGray(src);

    // 1) 紧裁：取非零像素（字 = 255）的外接框，消掉留白差异
    cv::Mat mask;
    cv::compare(src, 0, mask, cv::CMP_GT);
    std::vector<cv::Point> pts;
    cv::findNonZero(mask, pts);
    if (pts.empty()) return cv::Mat::zeros(h, w, CV_8UC1); // 空 ROI：全 0 画布
    cv::Mat glyph = src(cv::boundingRect(pts));

    // 2) 画布内可用区（四周各留 1px 背景）
    int innerW = (w > 2) ? (w - 2) : w;
    int innerH = (h > 2) ? (h - 2) : h;
    if (innerW < 1) innerW = 1;
    if (innerH < 1) innerH = 1;

    double s = std::min((double)innerW / (double)glyph.cols,
                        (double)innerH / (double)glyph.rows);

    int nw = (int)std::lround((double)glyph.cols * s);
    int nh = (int)std::lround((double)glyph.rows * s);
    if (nw < 1) nw = 1;
    if (nh < 1) nh = 1;
    if (nw > innerW) nw = innerW;
    if (nh > innerH) nh = innerH;

    cv::Mat scaled;
    if (nw == glyph.cols && nh == glyph.rows) {
        scaled = glyph.clone();
    } else {
        // 缩小用 INTER_AREA（避免混叠），放大用 INTER_LINEAR（避免块状台阶）
        int interp = (s < 1.0) ? cv::INTER_AREA : cv::INTER_LINEAR;
        cv::resize(glyph, scaled, cv::Size(nw, nh), 0.0, 0.0, interp);
    }

    // 3) 居中贴入全 0 画布
    out = cv::Mat::zeros(h, w, CV_8UC1);
    int ox = (w - nw) / 2;
    int oy = (h - nh) / 2;
    scaled.copyTo(out(cv::Rect(ox, oy, nw, nh)));
    return out;
}

// 图像的方差保护：二值图若全 0 / 全 255（方差 ≈ 0），相关系数没有意义
double StdDevOf(const cv::Mat& m)
{
    if (m.empty()) return 0.0;
    cv::Scalar mean, sd;
    cv::meanStdDev(m, mean, sd);
    return sd[0];
}

// 单个模板的打分：抗 ±1px 对位抖动 + 方差保护。
//
// 两边都归一化到同一画布 W×H 后，matchTemplate 只会输出 1×1，对 1px 抖动零容忍。
// 因此取模板**中心 (W-2)×(H-2) 子区**当滑动窗，在候选画布上滑 → 输出 3×3 → 取最大值。
// 缩放比例不变（两边已是同一画布尺寸），只是把「对位误差」吸收掉。
double MatchScore(const cv::Mat& tpl, const cv::Mat& canvas)
{
    if (tpl.empty() || canvas.empty()) return 0.0;
    if (tpl.size() != canvas.size()) return 0.0;
    if (tpl.channels() != 1 || canvas.channels() != 1) return 0.0;
    if (tpl.depth() != CV_8U || canvas.depth() != CV_8U) return 0.0;

    const int W = tpl.cols, H = tpl.rows;
    const int sw = (W > 2) ? (W - 2) : W;   // 滑动窗宽
    const int sh = (H > 2) ? (H - 2) : H;   // 滑动窗高
    const int x0 = (W - sw) / 2;
    const int y0 = (H - sh) / 2;

    cv::Mat win = tpl(cv::Rect(x0, y0, sw, sh));

    // 方差保护：任一方差 ≈ 0 时 TM_CCOEFF_NORMED 无意义（全 255 / 全 0）
    if (StdDevOf(win) < 1e-6 || StdDevOf(canvas) < 1e-6) return 0.0;

    cv::Mat res;
    cv::matchTemplate(canvas, win, res, cv::TM_CCOEFF_NORMED);

    double mn = 0.0, mx = 0.0;
    cv::Point mnLoc, mxLoc;
    cv::minMaxLoc(res, &mn, &mx, &mnLoc, &mxLoc);

    if (!std::isfinite(mx)) return 0.0;  // NaN / ±inf
    if (mx <= 0.0) return 0.0;           // 负相关对模板匹配毫无价值，也不允许 0 分被当成功
    if (mx > 1.0) return 1.0;
    return mx;
}

// 与模板库比对，返回最佳标签与分数；模板库为空时返回 ("?", 0)
void MatchAgainstTemplates(const cv::Mat& normChar, const OcrState& st,
                           std::string& outLabel, double& outScore)
{
    outLabel = "?";
    outScore = 0.0;
    if (normChar.empty() || st.templates.empty()) return;

    for (size_t t = 0; t < st.templates.size(); ++t) {
        if (t >= st.labels.size()) break;

        const cv::Mat& tpl = st.templates[t];
        if (tpl.empty()) continue;

        // 兜底：模板尺寸与本次归一化尺寸不一致时（用户改了 NormW / NormH），
        // 把候选缩放到模板尺寸再比，而不是直接跳过 —— 否则「改个参数就全问号」且没有任何报错。
        cv::Mat cand = normChar;
        if (tpl.size() != cand.size()) {
            cv::Mat resized;
            int interp = (tpl.cols <= cand.cols && tpl.rows <= cand.rows)
                       ? cv::INTER_AREA : cv::INTER_LINEAR;
            cv::resize(cand, resized, tpl.size(), 0.0, 0.0, interp);
            cand = resized;
        }

        double s = MatchScore(tpl, cand);
        if (s > outScore) { outScore = s; outLabel = st.labels[t]; }
    }
}

// 把 UTF-8 字符串安全拷进定长 buffer：不越界，且**不切断多字节字符**。
// 按字节硬截断会把中文标签拦腰切开 → 上层 UTF-8 解码得到乱码（「显示异常」之一）。
void CopyLabelSafe(const std::string& s, char* dst, int cap)
{
    if (dst == 0 || cap <= 0) return;
    std::memset(dst, 0, (size_t)cap);

    size_t n = s.size();
    if (n >= (size_t)cap) {
        n = (size_t)cap - 1;
        // 回退到最近的 UTF-8 字符起始字节（0b10xxxxxx 是续字节）
        while (n > 0 && (((unsigned char)s[n]) & 0xC0) == 0x80) --n;
    }
    if (n > 0) std::memcpy(dst, s.data(), n);
}

} // namespace

// ---------------------------------------------------------------------------

int OcrSetParam(Context* ctx, const VA_OcrParam* param)
{
    if (param == 0) { SetError(ctx, "null argument"); return VA_ERR_ARG; }
    ctx->ocr.param = *param;
    return VA_OK;
}

int OcrAddTemplate(Context* ctx, const VA_Image* gray, const char* label)
{
    if (gray == 0) { SetError(ctx, "null argument"); return VA_ERR_ARG; }

    try
    {
        cv::Mat g;
        if (!ToMat(gray, g, ctx->lastError)) return VA_ERR_ARG;

        cv::Mat one = AsGray(g);

        int w = (ctx->ocr.param.norm_w > 0) ? ctx->ocr.param.norm_w : DEF_NORM_W;
        int h = (ctx->ocr.param.norm_h > 0) ? ctx->ocr.param.norm_h : DEF_NORM_H;

        // 模板与识别必须用同一套表示：先二值化（含同一套极性判定），再归一化。
        // threshold <= 0 一律按 Otsu（哨兵 0 来自 OcrState 的 memset 零初始化，
        // 表示「没设过参数」，不能当成固定阈值 0）。
        VA_OcrParam tp = ctx->ocr.param;
        if (tp.threshold <= 0) tp.threshold = -1;

        cv::Mat bin  = MakeBinary(one, &tp);
        cv::Mat norm = NormalizeChar(bin, w, h);
        if (norm.empty()) { SetError(ctx, "empty template image"); return VA_ERR_ARG; }

        std::string lb = (label != 0) ? std::string(label) : std::string("?");
        if (lb.empty()) lb = "?";

        // 定长标签：按 UTF-8 字符边界截断（不切断中文）
        char fixed[VA_OCR_LABEL_BYTES];
        CopyLabelSafe(lb, fixed, VA_OCR_LABEL_BYTES);
        std::string kept(fixed);           // 以 '\0' 截断后的实际内容
        if (kept.empty()) kept = "?";

        ctx->ocr.templates.push_back(norm);
        ctx->ocr.labels.push_back(kept);
        return VA_OK;
    }
    catch (const cv::Exception& e) { SetError(ctx, e); return VA_ERR_INTERNAL; }
    catch (const std::exception& e) { SetError(ctx, e); return VA_ERR_INTERNAL; }
}

int OcrTemplateCount(Context* ctx, int* outCount)
{
    if (outCount == 0) { SetError(ctx, "null argument"); return VA_ERR_ARG; }
    *outCount = (int)ctx->ocr.templates.size();
    return VA_OK;
}

int OcrClearTemplates(Context* ctx)
{
    ctx->ocr.templates.clear();
    ctx->ocr.labels.clear();
    return VA_OK;
}

// ---------------------------------------------------------------------------

int OcrSegment(Context* ctx, const VA_Image* gray, const VA_OcrParam* param,
               VA_OcrChar* outChars, int maxCount, int* outCount)
{
    if (gray == 0 || outCount == 0) { SetError(ctx, "null argument"); return VA_ERR_ARG; }

    *outCount = 0;

    const VA_OcrParam* p = (param != 0) ? param : &ctx->ocr.param;

    try
    {
        cv::Mat g;
        if (!ToMat(gray, g, ctx->lastError)) return VA_ERR_ARG;
        if (g.channels() != 1) {
            SetError(ctx, "ocr requires single channel gray image");
            return VA_ERR_TYPE;
        }

        cv::Mat bin;
        std::vector<std::vector<cv::Rect> > lines = SegmentLines(g, p, bin);

        // 展平成「行优先、行内按 x 升序」的最终顺序
        std::vector<cv::Rect> ordered;
        for (size_t li = 0; li < lines.size(); ++li)
            ordered.insert(ordered.end(), lines[li].begin(), lines[li].end());

        int written = 0;
        for (size_t i = 0; i < ordered.size(); ++i) {
            if (outChars != 0 && written >= maxCount) break;  // 容量不足则截断

            const cv::Rect& r = ordered[i];
            if (outChars != 0) {
                VA_OcrChar c;
                c.x = (double)r.x;
                c.y = (double)r.y;
                c.w = (double)r.width;
                c.h = (double)r.height;
                std::memset(c.label, 0, sizeof(c.label));
                c.label[0] = '?';
                c.score = 0.0;
                outChars[written] = c;
            }
            ++written;
        }

        *outCount = written;
        return VA_OK;
    }
    catch (const cv::Exception& e) { SetError(ctx, e); return VA_ERR_INTERNAL; }
    catch (const std::exception& e) { SetError(ctx, e); return VA_ERR_INTERNAL; }
}

// 分割 + 识别，一次调用同时输出文本与每个字符的标签 / 置信度。
//
// 为什么必须一次调用出结果：历史实现里上层「先 Recognize 拿文本、再独立调一次 Segment 拿框」，
// 而 Segment 只会填 "?" / 0 —— 于是画面上每个字符框的标签必然是 "? 0.00"，
// 结果表格的「值」列也是。这里把识别结果直接带进 VA_OcrChar，框与标签从此同源。
//
// out_chars 与文本严格对齐：文本按「行优先、行内 x 升序 + 词间插入空格 + 行间插入 '\n'」生成，
// 而 chars 只装真实字符（不含插入的空格 / 换行），所以调用方按顺序即可把 chars 与文本对齐。
int OcrRecognizeEx(Context* ctx, const VA_Image* gray, const VA_OcrParam* param,
                   VA_OcrChar* outChars, int charsCap, int* outCharCount,
                   char* outText, int textCap, double* outMeanScore)
{
    if (gray == 0 || outCharCount == 0) { SetError(ctx, "null argument"); return VA_ERR_ARG; }

    *outCharCount = 0;
    if (outMeanScore != 0) *outMeanScore = 0.0;
    if (outText != 0) {
        if (textCap <= 0) { SetError(ctx, "invalid text capacity"); return VA_ERR_ARG; }
        outText[0] = '\0';
    }

    const VA_OcrParam* p = (param != 0) ? param : &ctx->ocr.param;

    try
    {
        cv::Mat g;
        if (!ToMat(gray, g, ctx->lastError)) return VA_ERR_ARG;
        if (g.channels() != 1) {
            SetError(ctx, "ocr requires single channel gray image");
            return VA_ERR_TYPE;
        }

        cv::Mat bin;
        std::vector<std::vector<cv::Rect> > lines = SegmentLines(g, p, bin);

        int w = (p->norm_w > 0) ? p->norm_w : DEF_NORM_W;
        int h = (p->norm_h > 0) ? p->norm_h : DEF_NORM_H;

        // accept_score 语义塌陷的修复：0（memset 默认）表示「没设过」，必须回落到 0.6，
        // 否则「接受一切」会让 0 分的垃圾匹配被当成识别成功。
        double accept = (p->accept_score > 0.0 && p->accept_score <= 1.0)
                      ? p->accept_score : DEF_ACCEPT;

        std::string text;
        std::vector<VA_OcrChar> chars;
        double scoreSum = 0.0;
        int    scoreCnt = 0;

        const cv::Rect bounds(0, 0, bin.cols, bin.rows);

        for (size_t li = 0; li < lines.size(); ++li) {
            if (li > 0) text += "\n";

            const std::vector<cv::Rect>& row = lines[li];

            // 行内平均字符宽，用于判断词间空格
            double avgW = 0.0;
            for (size_t i = 0; i < row.size(); ++i) avgW += (double)row[i].width;
            if (!row.empty()) avgW /= (double)row.size();

            for (size_t ci = 0; ci < row.size(); ++ci) {
                // 与前一个字符间隙过大 → 插入一个空格
                if (ci > 0) {
                    int gapPx = row[ci].x - (row[ci - 1].x + row[ci - 1].width);
                    if (avgW > 0.0 && (double)gapPx > 1.5 * avgW) text += " ";
                }

                cv::Rect r = row[ci] & bounds;
                cv::Mat  roi  = (r.width > 0 && r.height > 0) ? bin(r) : cv::Mat();
                cv::Mat  norm = NormalizeChar(roi, w, h);

                std::string label;
                double      score = 0.0;
                MatchAgainstTemplates(norm, ctx->ocr, label, score);

                // score <= 0 一律不采纳（0 分不是「低置信度」，是「完全不像」）
                bool accepted = !ctx->ocr.templates.empty() && score > 0.0 && score >= accept;

                text += accepted ? label : std::string("?");
                if (accepted) {
                    scoreSum += score;
                    ++scoreCnt;
                }

                VA_OcrChar c;
                c.x = (double)row[ci].x;
                c.y = (double)row[ci].y;
                c.w = (double)row[ci].width;
                c.h = (double)row[ci].height;
                std::memset(c.label, 0, sizeof(c.label));
                if (accepted) CopyLabelSafe(label, c.label, VA_OCR_LABEL_BYTES);
                else          c.label[0] = '?';
                c.score = accepted ? score : 0.0;

                chars.push_back(c);
            }
        }

        if (outMeanScore != 0 && scoreCnt > 0) *outMeanScore = scoreSum / (double)scoreCnt;

        // 字符数组：容量不足按容量截断，但**实际总数**必须如实写进 outCharCount
        const int total = (int)chars.size();
        *outCharCount = total;
        if (outChars != 0 && charsCap > 0) {
            int n = (total < charsCap) ? total : charsCap;
            for (int i = 0; i < n; ++i) outChars[i] = chars[i];
        }

        // 文本：边界安全拷贝，始终以 '\0' 结尾，绝不越界
        if (outText != 0) {
            int ncopy = (int)text.size();
            if (ncopy > textCap - 1) ncopy = textCap - 1;
            if (ncopy > 0) std::memcpy(outText, text.c_str(), (size_t)ncopy);
            outText[ncopy] = '\0';
        }

        return VA_OK;
    }
    catch (const cv::Exception& e) { SetError(ctx, e); return VA_ERR_INTERNAL; }
    catch (const std::exception& e) { SetError(ctx, e); return VA_ERR_INTERNAL; }
}

// 只要文本的旧入口：内部转调 OcrRecognizeEx 并丢弃字符数组（文本口径完全一致）。
int OcrRecognize(Context* ctx, const VA_Image* gray, const VA_OcrParam* param,
                 char* outText, int textCap, double* outMeanScore)
{
    if (gray == 0 || outText == 0) { SetError(ctx, "null argument"); return VA_ERR_ARG; }
    if (textCap <= 0) { SetError(ctx, "invalid text capacity"); return VA_ERR_ARG; }

    int charCount = 0;
    return OcrRecognizeEx(ctx, gray, param, 0, 0, &charCount,
                          outText, textCap, outMeanScore);
}

} // namespace va
