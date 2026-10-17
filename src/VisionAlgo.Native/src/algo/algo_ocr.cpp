// 传统算法 OCR：连通域分割 + 归一化 + 模板匹配识别。
// 无 contrib（没有 cv::text），这里自搭流水线：
//   二值化 → 连通域 → 尺寸/宽高比过滤 → 水平间隙合并 → 分行排序 → 模板匹配。
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

// 二值化：阈值 < 0 走 Otsu；invert 控制反色
cv::Mat MakeBinary(const cv::Mat& gray, const VA_OcrParam* p)
{
    cv::Mat bin;
    if (p->threshold < 0) {
        int type = p->invert ? (cv::THRESH_BINARY_INV | cv::THRESH_OTSU)
                             : (cv::THRESH_BINARY     | cv::THRESH_OTSU);
        cv::threshold(gray, bin, 0.0, 255.0, type);
    } else {
        int type = p->invert ? cv::THRESH_BINARY_INV : cv::THRESH_BINARY;
        cv::threshold(gray, bin, (double)p->threshold, 255.0, type);
    }
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

// 把字符 ROI 归一化到统一尺寸；空 ROI 返回空 Mat
cv::Mat NormalizeChar(const cv::Mat& roi, int w, int h)
{
    cv::Mat out;
    if (roi.empty() || w <= 0 || h <= 0) return out;
    cv::resize(roi, out, cv::Size(w, h), 0.0, 0.0, cv::INTER_AREA);
    return out;
}

// 与模板库比对，返回最佳标签与分数；模板库为空时返回 ("?", 0)
void MatchAgainstTemplates(const cv::Mat& normChar, const OcrState& st,
                           std::string& outLabel, double& outScore)
{
    outLabel = "?";
    outScore = 0.0;
    if (normChar.empty() || st.templates.empty()) return;

    for (size_t t = 0; t < st.templates.size(); ++t) {
        const cv::Mat& tpl = st.templates[t];
        if (tpl.empty() || tpl.size() != normChar.size()) continue;

        cv::Mat res;
        cv::matchTemplate(normChar, tpl, res, cv::TM_CCOEFF_NORMED);

        double mn = 0.0, mx = 0.0;
        cv::Point mnLoc, mxLoc;
        cv::minMaxLoc(res, &mn, &mx, &mnLoc, &mxLoc);
        if (mx > outScore) { outScore = mx; outLabel = st.labels[t]; }
    }
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

        cv::Mat norm = NormalizeChar(one, w, h);
        if (norm.empty()) { SetError(ctx, "empty template image"); return VA_ERR_ARG; }

        std::string lb = (label != 0) ? std::string(label) : std::string("?");
        if (lb.empty()) lb = "?";
        if (lb.size() >= (size_t)VA_OCR_LABEL_BYTES) lb.resize(VA_OCR_LABEL_BYTES - 1);

        ctx->ocr.templates.push_back(norm);
        ctx->ocr.labels.push_back(lb);
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

int OcrRecognize(Context* ctx, const VA_Image* gray, const VA_OcrParam* param,
                 char* outText, int textCap, double* outMeanScore)
{
    if (gray == 0 || outText == 0) { SetError(ctx, "null argument"); return VA_ERR_ARG; }
    if (textCap <= 0) { SetError(ctx, "invalid text capacity"); return VA_ERR_ARG; }

    outText[0] = '\0';
    if (outMeanScore != 0) *outMeanScore = 0.0;

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
        double accept = (p->accept_score > 0.0) ? p->accept_score : 0.0;

        std::string text;
        double scoreSum = 0.0;
        int    scoreCnt = 0;

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

                cv::Rect r = row[ci] & cv::Rect(0, 0, bin.cols, bin.rows);
                cv::Mat  roi  = (r.width > 0 && r.height > 0) ? bin(r) : cv::Mat();
                cv::Mat  norm = NormalizeChar(roi, w, h);

                std::string label;
                double      score = 0.0;
                MatchAgainstTemplates(norm, ctx->ocr, label, score);

                if (!ctx->ocr.templates.empty() && score >= accept) {
                    text += label;
                    scoreSum += score;
                    ++scoreCnt;
                } else {
                    text += "?";
                }
            }
        }

        if (outMeanScore != 0 && scoreCnt > 0) *outMeanScore = scoreSum / (double)scoreCnt;

        // 边界安全拷贝：始终以 '\0' 结尾，绝不越界
        int ncopy = (int)text.size();
        if (ncopy > textCap - 1) ncopy = textCap - 1;
        if (ncopy > 0) std::memcpy(outText, text.c_str(), (size_t)ncopy);
        outText[ncopy] = '\0';

        return VA_OK;
    }
    catch (const cv::Exception& e) { SetError(ctx, e); return VA_ERR_INTERNAL; }
    catch (const std::exception& e) { SetError(ctx, e); return VA_ERR_INTERNAL; }
}

} // namespace va
