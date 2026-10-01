// ============================================================================
//  基于边缘轮廓的形状匹配（Shape-based Matching）
//
//  思路：
//    训练 —— 在模板 ROI 内用 Canny 取细化边缘，再用 Sobel 求每个边缘点的梯度方向；
//           以 ROI 中心为原点保存「点坐标 + 单位梯度方向」，并按梯度幅值从大到小排序
//           （强边缘优先，配合提前终止能更快淘汰错误候选）。
//    匹配 —— 构建图像金字塔，由粗到细搜索：
//           最粗层做全局扫描（每个位置取最优角度/缩放），保留得分最高的若干候选；
//           逐层向下在候选邻域内精化位置 / 角度 / 缩放；
//           最细层再做亚像素位置与角度精化；
//           最后按得分排序 + 非极大值抑制输出多目标。
//    评分 —— 把模板点的梯度方向旋转到候选姿态后，与该处图像梯度求余弦相似度取平均。
//
//  性能设计（三项叠加）：
//    1) 姿态预计算：角度/缩放的旋转只按「姿态」算一次，位置循环里只做加法与采样，
//       避免在 (位置 × 角度 × 缩放) 三重循环里反复做三角函数与旋转。
//    2) 提前终止：①理论上界 —— 剩余点全满分也达不到阈值即放弃；
//                ②贪婪启发 —— 已评估的强边缘平均分过低即放弃（greediness 控制强度）。
//    3) 并行 + SIMD：粗扫按位置、精化按候选做 OpenMP 并行；评分的累积段用 SSE2 四路。
// ============================================================================

#include "va_internal.h"
#include "va_error.h"

#include "algo/va_shape.h"

#include <algorithm>
#include <cmath>
#include <vector>

#ifdef _OPENMP
#  include <omp.h>
#endif

// SSE2 是 x64 / x86 的基线指令集，无需运行时检测
#if defined(_M_X64) || defined(_M_IX86) || defined(__SSE2__)
#  define VA_USE_SSE2 1
#  include <emmintrin.h>
#endif

namespace {

const double kPi = 3.14159265358979323846;
const double kDeg2Rad = kPi / 180.0;

const size_t kMaxModelPoints = 8000;    // 特征点数量的硬上限
const size_t kMaxPointsPerLevel = 1024; // 单层参与评分的最多点数
const int    kDefaultMaxFeatures = 1500;    // 默认最多特征点数
const double kDefaultMinFeatureDist = 2.0;  // 默认特征点最小间距（模板像素）

// 某一层金字塔的梯度场
struct GradLevel {
    cv::Mat gx;
    cv::Mat gy;
};

// 某一层的模型点：坐标已按该层缩放，方向为单位向量
struct ModelLevel {
    std::vector<cv::Point2f> pts;
    std::vector<cv::Point2f> dir;
};

// 某个姿态（角度 + 缩放）预计算好的模板点，供所有位置复用
struct Pose {
    std::vector<float> ox;   // 旋转 + 缩放后的 X 偏移
    std::vector<float> oy;   // 旋转 + 缩放后的 Y 偏移
    std::vector<float> rx;   // 旋转后的单位方向 X
    std::vector<float> ry;   // 旋转后的单位方向 Y
};

// 候选位姿。位置一律使用「原图坐标系」，角度与缩放本身与层无关
struct Candidate {
    double cx;
    double cy;
    double angle;
    double scale;
    double score;
};

double ClampD(double v, double lo, double hi)
{
    if (lo > hi) { double t = lo; lo = hi; hi = t; }
    return v < lo ? lo : (v > hi ? hi : v);
}

void BuildGradients(const cv::Mat& gray, cv::Mat& gx, cv::Mat& gy)
{
    cv::Mat f;
    gray.convertTo(f, CV_32F);
    cv::Sobel(f, gx, CV_32F, 1, 0, 3, 1.0, 0.0, cv::BORDER_REPLICATE);
    cv::Sobel(f, gy, CV_32F, 0, 1, 3, 1.0, 0.0, cv::BORDER_REPLICATE);
}

// 双线性采样梯度（float 版）；越界返回 false，调用方按 0 分计入
bool SampleGrad(const cv::Mat& gx, const cv::Mat& gy,
                float x, float y, float& outGx, float& outGy)
{
    const int w = gx.cols;
    const int h = gy.rows;
    if (x < 0.0f || y < 0.0f || x > (float)w - 1.0f || y > (float)h - 1.0f) return false;

    const int x0 = (int)x;                      // x >= 0，直接截断即 floor
    const int y0 = (int)y;
    int x1 = x0 + 1; if (x1 > w - 1) x1 = w - 1;
    int y1 = y0 + 1; if (y1 > h - 1) y1 = h - 1;

    const float fx = x - (float)x0;
    const float fy = y - (float)y0;

    const float* ax = gx.ptr<float>(y0);
    const float* bx = gx.ptr<float>(y1);
    const float* ay = gy.ptr<float>(y0);
    const float* by = gy.ptr<float>(y1);

    const float w00 = (1.0f - fx) * (1.0f - fy);
    const float w01 = fx * (1.0f - fy);
    const float w10 = (1.0f - fx) * fy;
    const float w11 = fx * fy;

    outGx = ax[x0] * w00 + ax[x1] * w01 + bx[x0] * w10 + bx[x1] * w11;
    outGy = ay[x0] * w00 + ay[x1] * w01 + by[x0] * w10 + by[x1] * w11;
    return true;
}

#if VA_USE_SSE2
// 四路浮点水平求和
static inline float HSum128(__m128 v)
{
    __m128 t = _mm_add_ps(v, _mm_movehl_ps(v, v));
    t = _mm_add_ss(t, _mm_shuffle_ps(t, t, 0x1));
    return _mm_cvtss_f32(t);
}
#endif

// 为某个姿态预计算旋转 / 缩放后的模板点
void BuildPose(const ModelLevel& ml, float angleDeg, float scale, Pose& out)
{
    const double a = (double)angleDeg * kDeg2Rad;
    const float ca = (float)std::cos(a);
    const float sa = (float)std::sin(a);

    const size_t n = ml.pts.size();
    out.ox.resize(n);
    out.oy.resize(n);
    out.rx.resize(n);
    out.ry.resize(n);

    for (size_t i = 0; i < n; ++i)
    {
        const float px = ml.pts[i].x * scale;
        const float py = ml.pts[i].y * scale;
        out.ox[i] = px * ca - py * sa;
        out.oy[i] = px * sa + py * ca;

        const float ux = ml.dir[i].x;
        const float uy = ml.dir[i].y;
        out.rx[i] = ux * ca - uy * sa;
        out.ry[i] = ux * sa + uy * ca;
    }
}

// ---------------------------------------------------------------------------
// 评分：模板点旋转到候选姿态后与该处图像梯度求余弦相似度取平均。
//   threshold   —— 理论上界阈值：剩余点即使全满分也达不到就放弃
//   abandonAvg  —— 贪婪阈值：已评估点的平均分低于它就放弃（<=0 表示不启用）
// 返回 false 表示被提前淘汰。
// ---------------------------------------------------------------------------
bool ScorePose(const GradLevel& lv, const Pose& pose,
               float cx, float cy, float threshold, float abandonAvg,
               float& outScore)
{
    const size_t n = pose.ox.size();
    if (n == 0) { outScore = 0.0f; return false; }

    const float* pOx = &pose.ox[0];
    const float* pOy = &pose.oy[0];
    const float* pRx = &pose.rx[0];
    const float* pRy = &pose.ry[0];

    const float total = (float)n;
    // 至少评估这么多点才允许用贪婪启发式淘汰：既要避免只看前几点就误杀，
    // 又要让「小模型」能尽早剪枝（固定 16 会让 28 点的模型跑满全程，稀疏化因此不提速）。
    size_t mEval = n / 4;
    if (mEval < 6) mEval = 6;
    if (mEval > 24) mEval = 24;
    if (mEval > n) mEval = n;
    const size_t minEval = mEval;

    float sum = 0.0f;
    size_t i = 0;

#if VA_USE_SSE2
    const __m128 vOne = _mm_set1_ps(1.0f);
    const __m128 vNegOne = _mm_set1_ps(-1.0f);
    const __m128 vEps = _mm_set1_ps(1e-6f);
    __m128 vSum = _mm_setzero_ps();
    float gxv[4];
    float gyv[4];

    for (; i + 4 <= n; i += 4)
    {
        // 采样无法向量化（地址发散），先标量取回 4 个点的梯度
        for (int k = 0; k < 4; ++k)
        {
            float gx = 0.0f, gy = 0.0f;
            SampleGrad(lv.gx, lv.gy, cx + pOx[i + k], cy + pOy[i + k], gx, gy);
            gxv[k] = gx;
            gyv[k] = gy;
        }

        // 之后全是纯算术：幅值、点积、归一化、钳位、累加
        const __m128 vgx = _mm_loadu_ps(gxv);
        const __m128 vgy = _mm_loadu_ps(gyv);
        const __m128 vrx = _mm_loadu_ps(pRx + i);
        const __m128 vry = _mm_loadu_ps(pRy + i);

        const __m128 vmag = _mm_sqrt_ps(_mm_add_ps(_mm_mul_ps(vgx, vgx), _mm_mul_ps(vgy, vgy)));
        const __m128 vdot = _mm_add_ps(_mm_mul_ps(vrx, vgx), _mm_mul_ps(vry, vgy));

        // 平坦区（mag≈0）或越界点按 0 分计入：用掩码清零，同时避免除零
        const __m128 vmask = _mm_cmpgt_ps(vmag, vEps);
        __m128 vcos = _mm_and_ps(_mm_div_ps(vdot, vmag), vmask);
        vcos = _mm_max_ps(_mm_min_ps(vcos, vOne), vNegOne);

        vSum = _mm_add_ps(vSum, vcos);

        // 每 4 个点检查一次是否可以提前放弃
        const float done4 = (float)(i + 4);
        const float partial4 = HSum128(vSum);
        if ((partial4 + (total - done4)) / total < threshold) { outScore = 0.0f; return false; }
        if (abandonAvg > 0.0f && (i + 4) >= minEval && partial4 / done4 < abandonAvg)
        { outScore = 0.0f; return false; }
    }

    // 关键：把向量部分的累加结果并入 sum，
    // 否则下面的尾部循环只会看到尾部的少量贡献，误判「上界不足」而把正确候选丢掉
    sum += HSum128(vSum);
#endif

    // 尾部（不足 4 个）或未启用 SSE2 时走标量
    for (; i < n; ++i)
    {
        float gx = 0.0f, gy = 0.0f;
        SampleGrad(lv.gx, lv.gy, cx + pOx[i], cy + pOy[i], gx, gy);

        const float mag = std::sqrt(gx * gx + gy * gy);
        if (mag > 1e-6f)
        {
            float cosv = (pRx[i] * gx + pRy[i] * gy) / mag;
            if (cosv > 1.0f) cosv = 1.0f;
            if (cosv < -1.0f) cosv = -1.0f;
            sum += cosv;
        }

        const float done = (float)(i + 1);
        if ((sum + (total - done)) / total < threshold) { outScore = 0.0f; return false; }
        if (abandonAvg > 0.0f && (i + 1) >= minEval && sum / done < abandonAvg)
        { outScore = 0.0f; return false; }
    }

    float s = sum / total;
    if (s < 0.0f) s = 0.0f;      // 归一到 0~1
    if (s > 1.0f) s = 1.0f;
    outScore = s;
    return true;
}

bool ScoreBetter(const Candidate& a, const Candidate& b) { return a.score > b.score; }

} // anonymous namespace

namespace va {

// ToMat 失败时细化错误码：通道数不是 1/3/4 属于「图像类型不支持」，其余按参数非法处理
static int BadImageError(Context* ctx, const VA_Image* img)
{
    if (img != 0 && img->channels != 1 && img->channels != 3 && img->channels != 4)
    {
        SetError(ctx, "unsupported image type: channels must be 1, 3 or 4");
        return VA_ERR_TYPE;
    }
    return VA_ERR_ARG;
}

// ---------------------------------------------------------------------------
// 训练：提取模板 ROI 内的边缘点及其梯度方向
// ---------------------------------------------------------------------------
// 训练核心：Canny 取边缘 → Sobel 求方向 → 空间稀疏化筛选特征点
static int TrainCore(Context* ctx, const VA_Image* gray, const int* roi,
                     double contrast, int maxFeatures, double minFeatureDist)
{
    if (maxFeatures <= 0) maxFeatures = kDefaultMaxFeatures;
    if (maxFeatures > (int)kMaxModelPoints) maxFeatures = (int)kMaxModelPoints;
    if (minFeatureDist <= 0.0) minFeatureDist = kDefaultMinFeatureDist;

    if (gray == 0) { SetError(ctx, "null argument"); return VA_ERR_ARG; }

    try
    {
        cv::Mat g;
        if (!ToMat(gray, g, ctx->lastError)) return BadImageError(ctx, gray);

        // 彩色输入统一转灰度：形状匹配只依赖边缘的梯度方向，与颜色无关
        cv::Mat grayImg = AsGray(g);

        cv::Rect r(0, 0, grayImg.cols, grayImg.rows);
        if (roi != 0 && roi[2] > 0 && roi[3] > 0)
        {
            const int x = std::max(0, roi[0]);
            const int y = std::max(0, roi[1]);
            const int w = std::max(1, roi[2]);
            const int h = std::max(1, roi[3]);
            r = cv::Rect(x, y, w, h) & cv::Rect(0, 0, grayImg.cols, grayImg.rows);
            if (r.width <= 0 || r.height <= 0)
            {
                SetError(ctx, "invalid roi");
                return VA_ERR_ARG;
            }
        }

        cv::Mat patch = grayImg(r);

        // 1) Canny 取细化边缘（带迟滞，抗噪）
        double low = contrast;
        if (low < 5.0) low = 5.0;
        if (low > 200.0) low = 200.0;
        double high = low * 2.5;
        if (high > 255.0) high = 255.0;

        cv::Mat edges;
        cv::Canny(patch, edges, low, high, 3, true);

        // 2) Sobel 求梯度方向
        cv::Mat dx, dy;
        BuildGradients(patch, dx, dy);

        // 3) 收集边缘点：坐标相对 ROI 中心，方向为单位向量
        struct RawPoint { float x, y, dx, dy, mag; };
        std::vector<RawPoint> raw;

        const double cx0 = patch.cols * 0.5;
        const double cy0 = patch.rows * 0.5;

        for (int yy = 0; yy < edges.rows; ++yy)
        {
            const unsigned char* pe = edges.ptr<unsigned char>(yy);
            const float* pdx = dx.ptr<float>(yy);
            const float* pdy = dy.ptr<float>(yy);

            for (int xx = 0; xx < edges.cols; ++xx)
            {
                if (pe[xx] == 0) continue;

                const float vx = pdx[xx];
                const float vy = pdy[xx];
                const float mag = (float)std::sqrt((double)vx * vx + (double)vy * vy);
                if (mag < 1e-6f) continue;

                RawPoint p;
                p.x = (float)(xx - cx0);
                p.y = (float)(yy - cy0);
                p.dx = vx / mag;
                p.dy = vy / mag;
                p.mag = mag;
                raw.push_back(p);
            }
        }

        if (raw.empty())
        {
            SetError(ctx, "no edge found in template roi");
            return VA_ERR_NO_TEMPLATE;
        }

        // 强边缘优先：配合提前终止能更快淘汰错误候选
        std::sort(raw.begin(), raw.end(),
                  [](const RawPoint& a, const RawPoint& b) { return a.mag > b.mag; });

        // 空间稀疏化：按网格去重，同一网格只保留梯度最强的一点。
        // 相比「按幅值等间隔抽取」，分布更均匀 —— 点数更少（更快），
        // 也避免特征点成团导致姿态估计抖动（更稳）。
        int cell = (int)std::ceil(minFeatureDist);
        if (cell < 1) cell = 1;

        const int gw = (patch.cols + cell - 1) / cell;
        const int gh = (patch.rows + cell - 1) / cell;
        std::vector<unsigned char> taken((size_t)gw * (size_t)gh, 0);

        ctx->shape.pts.clear();
        ctx->shape.dir.clear();

        for (size_t i = 0; i < raw.size() && (int)ctx->shape.pts.size() < maxFeatures; ++i)
        {
            // raw 里的坐标是相对模板中心的，加回中心得到 ROI 内坐标后再算网格
            int gx = (int)((raw[i].x + (float)cx0) / (float)cell);
            int gy = (int)((raw[i].y + (float)cy0) / (float)cell);
            if (gx < 0) gx = 0;
            if (gy < 0) gy = 0;
            if (gx >= gw) gx = gw - 1;
            if (gy >= gh) gy = gh - 1;

            const size_t key = (size_t)gy * (size_t)gw + (size_t)gx;
            if (taken[key]) continue;
            taken[key] = 1;

            ctx->shape.pts.push_back(cv::Point2f(raw[i].x, raw[i].y));
            ctx->shape.dir.push_back(cv::Point2f(raw[i].dx, raw[i].dy));
        }

        // 理论上不会为空（raw 非空必留至少一点），兜底避免极端参数下丢模型
        if (ctx->shape.pts.empty())
        {
            const size_t n = std::min(raw.size(), (size_t)maxFeatures);
            for (size_t i = 0; i < n; ++i)
            {
                ctx->shape.pts.push_back(cv::Point2f(raw[i].x, raw[i].y));
                ctx->shape.dir.push_back(cv::Point2f(raw[i].dx, raw[i].dy));
            }
        }

        ctx->shape.width = (double)patch.cols;
        ctx->shape.height = (double)patch.rows;
        ctx->shape.valid = true;
        return VA_OK;
    }
    catch (const cv::Exception& e) { SetError(ctx, e); return VA_ERR_INTERNAL; }
    catch (const std::exception& e) { SetError(ctx, e); return VA_ERR_INTERNAL; }
}

int CreateShapeModel(Context* ctx, const VA_Image* gray, const int* roi, double contrast)
{
    return TrainCore(ctx, gray, roi, contrast, kDefaultMaxFeatures, kDefaultMinFeatureDist);
}

int CreateShapeModelEx(Context* ctx, const VA_Image* gray, const int* roi,
                       const VA_ShapeTrainParam* param)
{
    double contrast = (param != 0) ? param->contrast : 30.0;
    int maxFeatures = (param != 0) ? param->max_features : 0;
    double minDist = (param != 0) ? param->min_feature_dist : 0.0;

    return TrainCore(ctx, gray, roi, contrast, maxFeatures, minDist);
}

int GetShapeModelInfo(Context* ctx, int* outPointCount, double* outWidth, double* outHeight)
{
    if (outPointCount == 0 && outWidth == 0 && outHeight == 0)
    {
        SetError(ctx, "null argument");
        return VA_ERR_ARG;
    }
    if (!ctx->shape.valid)
    {
        SetError(ctx, "shape model not trained");
        return VA_ERR_NO_TEMPLATE;
    }

    if (outPointCount) *outPointCount = (int)ctx->shape.pts.size();
    if (outWidth)      *outWidth = ctx->shape.width;
    if (outHeight)     *outHeight = ctx->shape.height;
    return VA_OK;
}

int GetShapeModelPoints(Context* ctx, VA_ShapePoint* outPoints, int maxCount, int* outCount)
{
    if (outPoints == 0 || outCount == 0 || maxCount <= 0)
    {
        SetError(ctx, "null argument");
        return VA_ERR_ARG;
    }

    *outCount = 0;

    if (!ctx->shape.valid)
    {
        SetError(ctx, "shape model not trained");
        return VA_ERR_NO_TEMPLATE;
    }

    const size_t n = ctx->shape.pts.size();
    const int count = (int)std::min((size_t)maxCount, n);

    for (int i = 0; i < count; ++i)
    {
        outPoints[i].x = ctx->shape.pts[i].x;
        outPoints[i].y = ctx->shape.pts[i].y;
        outPoints[i].dx = ctx->shape.dir[i].x;
        outPoints[i].dy = ctx->shape.dir[i].y;
    }

    *outCount = count;
    return VA_OK;
}

int ClearShapeModel(Context* ctx)
{
    ctx->shape.pts.clear();
    ctx->shape.dir.clear();
    ctx->shape.width = 0.0;
    ctx->shape.height = 0.0;
    ctx->shape.valid = false;
    return VA_OK;
}

// ---------------------------------------------------------------------------
// 匹配：由粗到细的金字塔搜索
// ---------------------------------------------------------------------------
int FindShapeModel(Context* ctx, const VA_Image* gray, const VA_ShapeParam* param,
                   const int* searchRoi, VA_ShapeMatch* outMatches,
                   int maxCount, int* outCount)
{
    if (gray == 0 || param == 0 || outMatches == 0 || outCount == 0 || maxCount <= 0)
    {
        SetError(ctx, "null argument");
        return VA_ERR_ARG;
    }

    *outCount = 0;

    if (!ctx->shape.valid || ctx->shape.pts.empty())
    {
        SetError(ctx, "shape model not trained");
        return VA_ERR_NO_TEMPLATE;
    }

    try
    {
        cv::Mat g;
        if (!ToMat(gray, g, ctx->lastError)) return BadImageError(ctx, gray);

        // 彩色输入统一转灰度（1 通道则直接复用，3/4 通道走 cvtColor）
        cv::Mat grayImg = AsGray(g);
        const int iw = grayImg.cols;
        const int ih = grayImg.rows;

        // ---------- 1. 金字塔层数 ----------
        // 约束取「最粗层模板 ≥ 8px、图像 ≥ 32px」。约束过严会让层数退化成 1，
        // 退化后等于全分辨率暴力扫描 —— 这是最常见的性能陷阱。
        const double tw = ctx->shape.width;
        const double th = ctx->shape.height;

        int maxLevel = 1;
        while (maxLevel < 8)
        {
            const int next = maxLevel + 1;
            const double f = 1.0 / (double)(1 << next);
            if (tw * f < 8.0 || th * f < 8.0) break;   // 模板太小，没有可辨识结构
            if (iw * f < 32.0 || ih * f < 32.0) break; // 图像太小
            maxLevel = next;
        }

        int levels = param->pyramid_levels;
        if (levels < 1) levels = 1;
        if (levels > 8) levels = 8;
        if (levels > maxLevel) levels = maxLevel;

        // ---------- 2. 图像金字塔与梯度 ----------
        std::vector<cv::Mat> pyr(levels);
        pyr[0] = grayImg;
        for (int L = 1; L < levels; ++L)
            cv::pyrDown(pyr[L - 1], pyr[L]);

        std::vector<GradLevel> grads(levels);
        for (int L = 0; L < levels; ++L)
            BuildGradients(pyr[L], grads[L].gx, grads[L].gy);

        // ---------- 3. 各层模型点 ----------
        std::vector<ModelLevel> mls(levels);
        const size_t pointCount = ctx->shape.pts.size();

        for (int L = 0; L < levels; ++L)
        {
            size_t stride = ((size_t)1 << L);
            if (pointCount / stride > kMaxPointsPerLevel)
                stride = pointCount / kMaxPointsPerLevel + 1;

            const double f = 1.0 / (double)(1 << L);
            for (size_t i = 0; i < pointCount; i += stride)
            {
                mls[L].pts.push_back(cv::Point2f((float)((double)ctx->shape.pts[i].x * f),
                                                 (float)((double)ctx->shape.pts[i].y * f)));
                mls[L].dir.push_back(ctx->shape.dir[i]);
            }

            if (mls[L].pts.empty())
            {
                SetError(ctx, "template too small for pyramid level");
                return VA_ERR_SIZE;
            }
        }

        // ---------- 4. 角度 / 缩放序列 ----------
        double aStart = param->angle_start;
        double aEnd = param->angle_end;
        if (aEnd < aStart) { double t = aStart; aStart = aEnd; aEnd = t; }
        double aStep = param->angle_step;
        if (aStep <= 0.0) aStep = 1.0;

        std::vector<double> angles;
        {
            int count = (int)std::floor((aEnd - aStart) / aStep + 1e-9) + 1;
            if (count < 1) count = 1;
            if (count > 3600) count = 3600;
            for (int i = 0; i < count; ++i)
                angles.push_back(ClampD(aStart + i * aStep, aStart, aEnd));
            if (angles.back() < aEnd - 1e-9) angles.push_back(aEnd);
        }

        double sMin = param->scale_min;
        if (sMin <= 0.0) sMin = 1.0;
        double sMax = param->scale_max;
        if (sMax < sMin) sMax = sMin;
        double sStep = param->scale_step;
        if (sStep <= 0.0) sStep = 0.0;

        std::vector<double> scales;
        if (sStep <= 0.0 || (sMax - sMin) < 1e-9)
        {
            scales.push_back(sMin);
        }
        else
        {
            int count = (int)std::floor((sMax - sMin) / sStep + 1e-9) + 1;
            if (count < 1) count = 1;
            if (count > 64) count = 64;
            for (int i = 0; i < count; ++i)
                scales.push_back(ClampD(sMin + i * sStep, sMin, sMax));
        }

        // 贪婪系数：越大越激进（越快）；<=0 表示只用理论上界，不启用启发式
        double greediness = param->greediness;
        if (greediness < 0.0) greediness = 0.0;
        if (greediness > 1.0) greediness = 1.0;

        // ---------- 5. 搜索范围（原图坐标，限制中心可出现的位置）----------
        int rx = 0, ry = 0, rw = iw, rh = ih;
        // 注意：这里的 ROI 限制的是「模板中心」可出现的位置，不是模板外接框
        if (searchRoi != 0 && searchRoi[2] > 0 && searchRoi[3] > 0)
        {
            rx = std::max(0, searchRoi[0]);
            ry = std::max(0, searchRoi[1]);
            rw = std::min(searchRoi[2], iw - rx);
            rh = std::min(searchRoi[3], ih - ry);
            if (rw <= 0 || rh <= 0)
            {
                SetError(ctx, "invalid search roi");
                return VA_ERR_ARG;
            }
        }

        // ---------- 6. 最粗层全局扫描 ----------
        // 循环次序：姿态在外、位置在内 —— 姿态的旋转只算一次并供全部位置复用；
        // 位置维度再做 OpenMP 并行（各位置互不依赖，无竞争）。
        const int L0 = levels - 1;
        const double f0 = 1.0 / (double)(1 << L0);
        const int lw = pyr[L0].cols;
        const int lh = pyr[L0].rows;

        const int x0 = (int)ClampD(std::floor(rx * f0), 0.0, (double)(lw - 1));
        const int x1 = (int)ClampD(std::ceil((rx + rw - 1) * f0), 0.0, (double)(lw - 1));
        const int y0 = (int)ClampD(std::floor(ry * f0), 0.0, (double)(lh - 1));
        const int y1 = (int)ClampD(std::ceil((ry + rh - 1) * f0), 0.0, (double)(lh - 1));

        const size_t gridW = (size_t)(x1 - x0 + 1);
        const size_t gridH = (size_t)(y1 - y0 + 1);
        std::vector<int> posX;
        std::vector<int> posY;
        posX.reserve(gridW * gridH);
        posY.reserve(gridW * gridH);

        for (int cy = y0; cy <= y1; ++cy)
        {
            for (int cx = x0; cx <= x1; ++cx)
            {
                posX.push_back(cx);
                posY.push_back(cy);
            }
        }

        const int nPos = (int)posX.size();
        std::vector<float> bestScore((size_t)nPos, -1.0f);
        std::vector<float> bestAngle((size_t)nPos, 0.0f);
        std::vector<float> bestScale((size_t)nPos, 1.0f);

        // 粗层只用来「筛出可能的位置」，阈值放宽，避免真正的目标在粗层被误杀。
        // 系数取 0.4（原 0.5 偏激进，粗层子采样会让真实目标得分偏低，导致漏检、结果跳动）。
        double coarseThr = param->min_score * 0.4;
        if (coarseThr < 0.10) coarseThr = 0.10;
        if (coarseThr > 0.45) coarseThr = 0.45;
        const float thrCoarse = (float)coarseThr;
        const float avgCoarse = (float)(coarseThr * greediness);

        Pose pose;
        for (size_t si = 0; si < scales.size(); ++si)
        {
            for (size_t ai = 0; ai < angles.size(); ++ai)
            {
                BuildPose(mls[L0], (float)angles[ai], (float)scales[si], pose);
                const float a = (float)angles[ai];
                const float sc = (float)scales[si];

#ifdef _OPENMP
                #pragma omp parallel for schedule(dynamic, 512)
#endif
                for (int p = 0; p < nPos; ++p)
                {
                    float s = 0.0f;
                    if (ScorePose(grads[L0], pose, (float)posX[(size_t)p], (float)posY[(size_t)p],
                                  thrCoarse, avgCoarse, s))
                    {
                        if (s > bestScore[(size_t)p])
                        {
                            bestScore[(size_t)p] = s;
                            bestAngle[(size_t)p] = a;
                            bestScale[(size_t)p] = sc;
                        }
                    }
                }
            }
        }

        std::vector<Candidate> cands;
        cands.reserve(1024);
        for (int p = 0; p < nPos; ++p)
        {
            if (bestScore[(size_t)p] < 0.0f) continue;

            Candidate c;
            c.cx = (double)posX[(size_t)p] / f0;      // 层坐标 → 原图坐标
            c.cy = (double)posY[(size_t)p] / f0;
            c.angle = bestAngle[(size_t)p];
            c.scale = bestScale[(size_t)p];
            c.score = bestScore[(size_t)p];
            cands.push_back(c);
        }

        if (cands.empty())
        {
            *outCount = 0;
            return VA_OK;
        }

        int maxMatches = param->max_matches;
        if (maxMatches <= 0) maxMatches = 1;

        int keep = maxMatches * 8;
        if (keep < 8) keep = 8;
        if (keep > 300) keep = 300;

        std::sort(cands.begin(), cands.end(), ScoreBetter);
        if ((int)cands.size() > keep) cands.resize((size_t)keep);

        // ---------- 7. 逐层向下精化（候选之间互不依赖，可并行）----------
        const float thrFine = (float)param->min_score;
        const float avgFine = (float)(param->min_score * greediness);

        for (int L = L0 - 1; L >= 0; --L)
        {
            const double f = 1.0 / (double)(1 << L);
            const int win = (L == 0) ? 2 : 1;
            // 最细层把角度 / 缩放的搜索窗口放宽到 ±2 步：
            // 粗层定位可能差 1~2 个步长，只搜 ±1 步会收敛不到位
            const int aWin = (L == 0) ? 2 : 1;
            const int sWin = (L == 0) ? 2 : 1;

#ifdef _OPENMP
            #pragma omp parallel for schedule(dynamic)
#endif
            for (int ci = 0; ci < (int)cands.size(); ++ci)
            {
                const Candidate c = cands[(size_t)ci];

                double bestScore = 0.0;
                double bestCx = c.cx;
                double bestCy = c.cy;
                double bestAngle = c.angle;
                double bestScale = c.scale;

                // 先用当前位姿在该层评分，作为后续比较的基准
                {
                    Pose p0;
                    BuildPose(mls[L], (float)c.angle, (float)c.scale, p0);
                    float s0 = 0.0f;
                    if (ScorePose(grads[L], p0, (float)(c.cx * f), (float)(c.cy * f),
                                  0.0f, 0.0f, s0))
                        bestScore = s0;
                }

                for (int ao = -aWin; ao <= aWin; ++ao)
                {
                    const double a = ClampD(c.angle + ao * aStep, aStart, aEnd);

                    for (int so = -sWin; so <= sWin; ++so)
                    {
                        double s = c.scale;
                        if (sStep > 0.0) s = ClampD(c.scale + so * sStep, sMin, sMax);
                        else if (so != 0) continue;

                        Pose pl;
                        BuildPose(mls[L], (float)a, (float)s, pl);

                        for (int dy = -win; dy <= win; ++dy)
                        {
                            for (int dx = -win; dx <= win; ++dx)
                            {
                                const float ccx = (float)(c.cx * f) + (float)dx;
                                const float ccy = (float)(c.cy * f) + (float)dy;

                                float sc = 0.0f;
                                if (ScorePose(grads[L], pl, ccx, ccy,
                                              (float)bestScore, avgFine, sc))
                                {
                                    if (sc > bestScore)
                                    {
                                        bestScore = sc;
                                        bestCx = (double)ccx / f;
                                        bestCy = (double)ccy / f;
                                        bestAngle = a;
                                        bestScale = s;
                                    }
                                }
                            }
                        }
                    }
                }

                cands[(size_t)ci].cx = bestCx;
                cands[(size_t)ci].cy = bestCy;
                cands[(size_t)ci].angle = bestAngle;
                cands[(size_t)ci].scale = bestScale;
                cands[(size_t)ci].score = bestScore;
            }
        }

        // ---------- 8. 亚像素精化（最细层）----------
        if (param->subpixel != 0)
        {
#ifdef _OPENMP
            #pragma omp parallel for schedule(dynamic)
#endif
            for (int ci = 0; ci < (int)cands.size(); ++ci)
            {
                const Candidate c = cands[(size_t)ci];

                Pose pl;
                BuildPose(mls[0], (float)c.angle, (float)c.scale, pl);

                // 位置：±1 像素，步长 0.25
                double bestScore = c.score;
                double bestCx = c.cx;
                double bestCy = c.cy;

                for (double dy = -1.0; dy <= 1.001; dy += 0.25)
                {
                    for (double dx = -1.0; dx <= 1.001; dx += 0.25)
                    {
                        float sc = 0.0f;
                        if (ScorePose(grads[0], pl, (float)(c.cx + dx), (float)(c.cy + dy),
                                      (float)bestScore, avgFine, sc))
                        {
                            if (sc > bestScore)
                            {
                                bestScore = sc;
                                bestCx = c.cx + dx;
                                bestCy = c.cy + dy;
                            }
                        }
                    }
                }

                // 角度 / 缩放联合精化：各覆盖 ±1 个搜索步长、分 8 档。
                // 金字塔粗定位可能落到相邻网格点上，只搜 ±0.5 档会够不到真实值。
                double bestAngle = c.angle;
                double bestScale = c.scale;
                const double subA = aStep * 0.25;                       // 8 档 = ±aStep
                const bool  useS = (sStep > 0.0);
                const double subS = useS ? sStep * 0.25 : 0.0;
                const int sLo = useS ? -4 : 0;
                const int sHi = useS ? 4 : 0;

                for (int ai = -4; ai <= 4; ++ai)
                {
                    const double a = ClampD(c.angle + ai * subA, aStart, aEnd);

                    for (int si = sLo; si <= sHi; ++si)
                    {
                        const double s = useS ? ClampD(c.scale + si * subS, sMin, sMax) : c.scale;

                        Pose pa;
                        BuildPose(mls[0], (float)a, (float)s, pa);

                        float sc = 0.0f;
                        if (ScorePose(grads[0], pa, (float)bestCx, (float)bestCy,
                                      (float)bestScore, avgFine, sc))
                        {
                            if (sc > bestScore)
                            {
                                bestScore = sc;
                                bestAngle = a;
                                bestScale = s;
                            }
                        }
                    }
                }

                cands[(size_t)ci].cx = bestCx;
                cands[(size_t)ci].cy = bestCy;
                cands[(size_t)ci].angle = bestAngle;
                cands[(size_t)ci].scale = bestScale;
                cands[(size_t)ci].score = bestScore;
            }
        }

        // ---------- 9. 排序 + 非极大值抑制 + 输出 ----------
        std::sort(cands.begin(), cands.end(), ScoreBetter);

        double overlap = param->max_overlap;
        if (overlap <= 0.0) overlap = 0.5;

        const double minScore = param->min_score;
        std::vector<Candidate> accepted;

        for (size_t i = 0; i < cands.size() && (int)accepted.size() < maxMatches; ++i)
        {
            const Candidate& c = cands[i];
            if (c.score < minScore) break;

            const double w = ctx->shape.width * c.scale;
            const double h = ctx->shape.height * c.scale;
            const double minSide = std::min(w, h);
            const double distThr = overlap * minSide;
            if (distThr <= 0.0) { accepted.push_back(c); continue; }

            bool dup = false;
            for (size_t j = 0; j < accepted.size(); ++j)
            {
                const double ddx = c.cx - accepted[j].cx;
                const double ddy = c.cy - accepted[j].cy;
                if (std::sqrt(ddx * ddx + ddy * ddy) < distThr) { dup = true; break; }
            }
            if (!dup) accepted.push_back(c);
        }

        int n = (int)accepted.size();
        if (n > maxCount) n = maxCount;

        for (int i = 0; i < n; ++i)
        {
            outMatches[i].cx = accepted[i].cx;
            outMatches[i].cy = accepted[i].cy;
            outMatches[i].angle = accepted[i].angle;
            outMatches[i].scale = accepted[i].scale;
            outMatches[i].score = accepted[i].score;
            outMatches[i].width = ctx->shape.width * accepted[i].scale;
            outMatches[i].height = ctx->shape.height * accepted[i].scale;
        }

        *outCount = n;
        return VA_OK;
    }
    catch (const cv::Exception& e) { SetError(ctx, e); return VA_ERR_INTERNAL; }
    catch (const std::exception& e) { SetError(ctx, e); return VA_ERR_INTERNAL; }
}

} // namespace va
