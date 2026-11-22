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
//    评分 —— 把模板点旋转到候选姿态后，与该处图像梯度求余弦相似度取平均。
//
//  性能设计（五项叠加）：
//    1) 姿态预计算：角度/缩放的旋转只按「姿态」算一次，位置循环里只做加法与采样，
//       避免在 (位置 × 角度 × 缩放) 三重循环里反复做三角函数与旋转。
//    2) 提前终止：①理论上界 —— 剩余点即使全满分也达不到阈值即放弃；
//                ②贪婪启发 —— 已评估点的平均分过低即放弃（greediness 控制强度）。
//    3) 位置级剪枝 + 单并行域：粗层把「本位置当前最优得分」当作理论上界阈值，
//       已找到高分姿态的位置在后续姿态上几十个点内就能退出；姿态预计算按块组织，
//       一个块只做一次 fork/join（原实现每个姿态 fork 一次，360+ 姿态时开销可观）。
//    4) 粗层「梯度方向响应图」查表（LINEMOD / Line2D 的经典做法，参考 meiqua/shape_based_matching）：
//       把每个像素的梯度方向量化成 kDirBins 档存成一张 uint8 图，模板点的方向档位随姿态
//       整体平移（旋转 = 档位加偏移），于是每个点的相似度只剩「读 1 字节 → 查 1 次 cos 表」。
//       原实现每个点要做双线性（8 次浮点读 + 加权）+ 开方 + 除法 + 钳位，粗层是它的主耗时。
//       ⚠️ 只有最粗层走查表（那里位置本身就是整数格点），精化层与最终重评仍走精确双线性，
//          所以亚像素精度与对外得分口径完全不变。
//    5) 粗层「边缘支持度」整型图剪枝：模板外接圆内的梯度像素数为 0 时，该位置所有姿态的
//       分子必然为 0（分数 0，必然低于粗层阈值），一次查表就能跳过整个位置的全部姿态。
//       这是**严格上界**（不是启发式），不会漏掉真目标；工业图大片平坦背景时收益最大。
//    6) 并行 + SIMD：粗扫按位置、精化按候选做 OpenMP 并行；精化层评分的累积段用 SSE2 四路。
//    7) 只读视图与免转换梯度：单通道输入直接引用（GrayView，不 clone 整图）；
//       Sobel 直接 8U→CV_32F，省掉每层一次整图 convertTo（8U 定点与 32F 浮点结果逐位相同）。
//    8) 金字塔只建「搜索区邻域」：搜索 ROI 外的像素根本不参与评分，却要为 pyrDown 与两层
//       Sobel 付全图代价。留边覆盖「模板外接半径 + 金字塔/Sobel 支撑域」后裁切，
//       结果与原图构建**逐位相同**（BENCH-K 断言中心/角度/得分一致）。
//    9) 姿态表复用：精化与亚像素段共用一个 Pose 缓冲，不再每个 (角度×缩放) 新建 4 个 vector。
//
//  稳健性设计（针对「定位准确率不足」）：
//    · 评分分母只统计「落在图像内」的模板点。目标贴边/部分出画时，出画的点根本无从
//      判断对错，计入分母会把分数上限压到 (1-出画比例)，贴边件因此被判低分甚至漏检。
//      图内的平坦点（梯度≈0）仍计入分母并按 0 分处理 —— 该有边缘却没有，属真实失配。
//    · 覆盖率下限：图内点不足总数一半直接判失败，避免「只剩两三个点恰好对上」的假高分。
//    · 非极大值抑制用「旋转外框的重叠面积比」而不是中心距，ROI 比工件大时不会把两个
//      真实相邻目标当成同一个。
//    · 粗层候选保留前做一次空间稀疏化，避免同一目标的分数平台占满候选名额而挤掉邻居。
//    · 输出分数在最细层用「不剪枝」的口径重算一次，下游拿它判 OK/NG 才不会偏悲观。
//
//  诊断开关（默认关闭，不影响算法行为）：
//    VA_SHAPE_DEBUG=1    粗扫统计打到 stderr（层数/位置数/姿态数/建表与粗扫耗时/前几名候选）
//    VA_SHAPE_PRECISE=1  强制关掉方向响应图、退回精确双线性，用于两条路径的 A/B 对比
// ============================================================================

#include "va_internal.h"
#include "va_error.h"

#include "algo/va_shape.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <vector>
#include <cstdio>
#include <cstdlib>
#include <chrono>

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
const size_t kMaxPointsPerLevel = 1024; // 粗层单层参与评分的最多点数（直接决定粗扫成本）
// 最细层的点数上限。最细层只服务「精化 / 亚像素 / 最终诚实重评」，只对 keep=8~300 个候选
// 执行，与粗扫的 (位置 × 姿态) 规模无关，所以可以比粗层宽得多。
// 原实现共用 1024 → 默认 1500 点的模型在最细层被 stride=2 抽成 750 点，
// 等于「训练了 1500 个特征点，最终打分只用一半」，对外 score 的稳定性被白丢。
const size_t kMaxPointsFinest = 4096;
const int    kDefaultMaxFeatures = 1500;    // 默认最多特征点数
const double kDefaultMinFeatureDist = 2.0;  // 默认特征点最小间距（模板像素）

// 评分的覆盖率下限：图内点数不足总数的这个比例时，本次评分直接判失败。
// 0.5 的含义是「目标至少有一半还在视野里」—— 再少的话分数由极少数点决定，不可信。
const float kMinCoverage = 0.5f;

// 粗层姿态轴的抽稀倍数：最粗层不需要按 angle_step / scale_step 逐一尝试。
// 金字塔下一层的精化会以 ±2 个步长重新搜索角度与缩放，因此抽稀 2 倍
// （最坏误差 = 1 个步长）一定能被精化覆盖；姿态总数直接少 4 倍。
const size_t kCoarsePoseStride = 2;

// 粗层姿态预计算的单块内存上限。块越大 fork/join 越少，但块超过 CPU 末级缓存后
// 每处理一个位置都要把整块姿态表重新流一遍，反而变慢。
const size_t kCoarsePoseChunkBytes = 8u << 20;

// 启用「方向响应图查表」的最小粗扫规模（位置数 × 姿态数）。
// 查表路径要先建方向图与边缘支持度图（一次性 1~2 ms），小规模搜索摊不回来。
// 门槛取自实测：21 万组走查表反而慢 2.7 ms，60 万组起开始有收益。
const size_t kFastPathMinWork = 400000;

// 粗层候选保留时的空间分散半径（原图像素，按「最粗层像素 × 2」取）。
const double kCandidateSepLevelFactor = 2.0;

// 某一层金字塔的梯度场
struct GradLevel {    cv::Mat gx;
    cv::Mat gy;
};

// ---------------------------------------------------------------------------
// 梯度方向量化查表（粗层专用）
//
//   kDirBins 档把 360° 均分，档位误差 ≤ 360/2N 度。N=32 时最坏 5.6°，
//   cos 误差 ≤ 0.005 —— 远小于粗层阈值（0.10~0.45）与姿态步长带来的差异，
//   而且只影响「粗层挑候选」，最终得分由最细层的精确双线性重算。
//   32 档的 cos 表 = 1024 float = 4KB，可常驻 L1。
// ---------------------------------------------------------------------------
const int kDirBins = 32;
const unsigned char kDirNone = 255;   // 该像素梯度≈0（平坦区），贡献 0 分但计入分母

struct DirCosTable {
    float v[kDirBins * kDirBins];
    DirCosTable()
    {
        for (int t = 0; t < kDirBins; ++t)
            for (int i = 0; i < kDirBins; ++i)
            {
                const double d = (double)(t - i) * (2.0 * kPi / (double)kDirBins);
                v[t * kDirBins + i] = (float)std::cos(d);
            }
    }
};
const DirCosTable g_dirCos;

// 某一层的梯度方向图（CV_8U：档位 0..kDirBins-1，kDirNone 表示平坦）
struct DirLevel {
    cv::Mat bins;
    bool valid;
    DirLevel() : valid(false) {}
};

// 某一层的「边缘支持度」整型图：(rows+1)×(cols+1) 的前缀和，用于 O(1) 数出
// 任意矩形内的梯度像素个数。只用来做「一个都没有 → 全姿态必然 0 分」的严格剪枝。
struct EdgeSupport {
    cv::Mat sum;
    bool valid;
    EdgeSupport() : valid(false) {}
};

// 某一层的模型点：坐标已按该层缩放，方向为单位向量
struct ModelLevel {
    std::vector<cv::Point2f> pts;
    std::vector<cv::Point2f> dir;
    std::vector<unsigned char> bin;   // dir 量化后的档位（供粗层查表）
};

// 某个姿态（角度 + 缩放）预计算好的模板点，供所有位置复用
struct Pose {
    std::vector<float> ox;   // 旋转 + 缩放后的 X 偏移
    std::vector<float> oy;   // 旋转 + 缩放后的 Y 偏移
    std::vector<float> rx;   // 旋转后的单位方向 X
    std::vector<float> ry;   // 旋转后的单位方向 Y
    std::vector<unsigned char> bin;   // 旋转后的方向档位（查表用；旋转 = 档位整体平移）
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
    // 直接 8U → CV_32F。ksize=3 的 Sobel 权重全是整数（±1/±2），8U 定点路径与
    // 32F 浮点路径算出的和逐位相同，因此这一步只是省掉一次「整图 convertTo(CV_32F)」
    // 的分配 + 全图写入（每层一张 float 图，5MP 图约 20 MB 内存写）。
    cv::Sobel(gray, gx, CV_32F, 1, 0, 3, 1.0, 0.0, cv::BORDER_REPLICATE);
    cv::Sobel(gray, gy, CV_32F, 0, 1, 3, 1.0, 0.0, cv::BORDER_REPLICATE);
}

// 只读灰度视图：单通道输入直接返回引用，不做 clone。
// 训练 / 匹配两条路径对 gray 全程只读（Canny、Sobel、pyrDown 都不写 src），
// 所以没必要为了「连续性」把整图拷一遍 —— 单色 12MP 图这一次拷贝就是 12 MB。
// ⚠️ 与 va::AsGray 的分工：AsGray 会 clone，是给 preprocess 那种「原地滤波」路径用的，
//    这里绝不能用 AsGray，否则白拷一次；同理将来若有原地写操作，也不能用 GrayView。
cv::Mat GrayView(const cv::Mat& input)
{
    if (input.channels() == 1) return input;
    return va::AsGray(input);
}

// 建立某层的梯度方向图（档位表）与边缘支持度整型图。
// 两件事共用一趟扫描：平坦像素（梯度≈0）同时是 kDirNone 与支持度 0。
void BuildDirMap(const cv::Mat& gx, const cv::Mat& gy, DirLevel& dir, EdgeSupport& sup)
{
    const int w = gx.cols;
    const int h = gx.rows;

    dir.bins.create(h, w, CV_8U);
    cv::Mat mask(h, w, CV_8U);

    // cv::phase 用快速 atan2（向量化），比逐像素 std::atan2 快一个量级。
    // ⚠️ 第 4 参 = angleInDegrees，必须为 true：下面按「度 → 档位」换算，
    //    传 false 会拿到弧度，全部像素都会落到档位 0（曾经踩过）。
    cv::Mat ang;
    cv::phase(gx, gy, ang, true);       // 度，[0, 360)

    const float kBinPerDeg = (float)kDirBins / 360.0f;

    for (int y = 0; y < h; ++y)
    {
        const float* px = gx.ptr<float>(y);
        const float* py = gy.ptr<float>(y);
        const float* pa = ang.ptr<float>(y);
        unsigned char* pd = dir.bins.ptr<unsigned char>(y);
        unsigned char* pm = mask.ptr<unsigned char>(y);

        for (int x = 0; x < w; ++x)
        {
            const float vx = px[x];
            const float vy = py[x];

            // 与 ScorePose 的「平坦判据」必须一致（mag <= 1e-6 记 0 分），
            // 否则查表路径与精确路径对同一点的认定会不同。
            if (vx * vx + vy * vy < 1e-12f)
            {
                pd[x] = kDirNone;
                pm[x] = 0;
                continue;
            }

            int b = (int)(pa[x] * kBinPerDeg);
            if (b < 0) b = 0;
            if (b >= kDirBins) b = kDirBins - 1;

            pd[x] = (unsigned char)b;
            pm[x] = 1;
        }
    }

    cv::integral(mask, sup.sum, CV_32S);
    dir.valid = true;
    sup.valid = true;
}

// 双线性采样梯度（float 版）；越界返回 false，调用方不计入分母
bool SampleGrad(const cv::Mat& gx, const cv::Mat& gy,
                float x, float y, float& outGx, float& outGy){
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
// 诊断用：整个 FindShapeModel 调用里累计「被旋转缩放的模板点数」。
// 用来判断「姿态表能不能靠预计算提速」——见 docs/算法/算法更新记录.md 的成本结构。
// 只有开诊断（VA_SHAPE_DEBUG=1）时才累加；正常路径上是一次 relaxed load，可忽略。
static std::atomic<bool>      g_poseCountOn(false);
static std::atomic<long long> g_posePoints(0);

void BuildPose(const ModelLevel& ml, float angleDeg, float scale, Pose& out)
{
    if (g_poseCountOn.load(std::memory_order_relaxed))
        g_posePoints.fetch_add((long long)ml.pts.size(), std::memory_order_relaxed);

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

    // 方向档位：旋转一个角度 = 档位整体平移，不必重新求 atan2。
    // 注意 bin 是按 kDirBins 取模的整数偏移，与 BuildDirMap 的量化口径一致。
    if (!ml.bin.empty())
    {
        out.bin.resize(n);
        const int shift = (int)std::lround((double)angleDeg * (double)kDirBins / 360.0);
        const int base = ((shift % kDirBins) + kDirBins) % kDirBins;

        for (size_t i = 0; i < n; ++i)
        {
            int b = (int)ml.bin[i] + base;
            if (b >= kDirBins) b -= kDirBins;
            out.bin[i] = (unsigned char)b;
        }
    }
}

// ---------------------------------------------------------------------------
// 评分：模板点旋转到候选姿态后与该处图像梯度求余弦相似度取平均。
//   threshold   —— 理论上界阈值：剩余点即使全满分也达不到就放弃
//   abandonAvg  —— 贪婪阈值：已评估点的平均分低于它就放弃（<=0 表示不启用）
// 返回 false 表示被提前淘汰（或覆盖率不足）。
//
// 【分母语义】分母是「落在图像内」的模板点数，不是模板点总数：目标贴边 / 部分出画时
//   出画的点无从判断对错，若计入分母，分数上限会被压成 (1 - 出画比例)，贴边件必然低分。
//   图内的平坦点（梯度≈0）仍计入分母并按 0 分处理 —— 那里本该有边缘却没有，是真实失配。
//
// 【理论上界的写法】剩余点全部图内且全满分时，分子最多 sum + rest、分母最多
//   inFrame + rest，所以 (sum + rest) / (inFrame + rest) 才是真正的上界。
//   全部点都在图内时它退化成旧的 (sum + rest) / total —— 与原来完全一致，剪枝强度不变。
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

    // 至少评估这么多「图内」点才允许用贪婪启发式淘汰：既要避免只看前几点就误杀，
    // 又要让「小模型」能尽早剪枝。实测（360 姿态 × 76800 位置）绝大多数
    // (位置, 姿态) 组合都死在第一次检查上，所以这个值直接决定粗扫耗时 ——
    // 取 n/8 且不超过 8（原实现 n/4、上限 24，对 55 点的粗层模型要把 16 个点跑满）。
    size_t mEval = n / 8;
    if (mEval < 4) mEval = 4;
    if (mEval > 8) mEval = 8;
    if (mEval > n) mEval = n;
    const size_t minEval = mEval;

    float sum = 0.0f;
    size_t inFrame = 0;      // 落在图像内的采样点数（= 分母）
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
        // 采样无法向量化（地址发散），先标量取回 4 个点的梯度；
        // 顺带统计「图内点数」—— 越界时 SampleGrad 返回 false，gx/gy 保持 0。
        for (int k = 0; k < 4; ++k)
        {
            float gx = 0.0f, gy = 0.0f;
            if (SampleGrad(lv.gx, lv.gy, cx + pOx[i + k], cy + pOy[i + k], gx, gy))
                ++inFrame;
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

        // 平坦区（mag≈0）按 0 分计入：用掩码清零，同时避免除零
        const __m128 vmask = _mm_cmpgt_ps(vmag, vEps);
        __m128 vcos = _mm_and_ps(_mm_div_ps(vdot, vmag), vmask);
        vcos = _mm_max_ps(_mm_min_ps(vcos, vOne), vNegOne);

        vSum = _mm_add_ps(vSum, vcos);

        // 每 4 个点检查一次是否可以提前放弃
        const float done4 = (float)(i + 4);
        const float rest4 = total - done4;
        const float partial4 = HSum128(vSum);
        const float denomMax4 = (float)inFrame + rest4;
        if (denomMax4 > 0.0f && (partial4 + rest4) / denomMax4 < threshold)
        { outScore = 0.0f; return false; }
        if (abandonAvg > 0.0f && (i + 4) >= minEval && inFrame >= minEval
            && partial4 / (float)inFrame < abandonAvg)
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
        if (SampleGrad(lv.gx, lv.gy, cx + pOx[i], cy + pOy[i], gx, gy)) ++inFrame;

        const float mag = std::sqrt(gx * gx + gy * gy);
        if (mag > 1e-6f)
        {
            float cosv = (pRx[i] * gx + pRy[i] * gy) / mag;
            if (cosv > 1.0f) cosv = 1.0f;
            if (cosv < -1.0f) cosv = -1.0f;
            sum += cosv;
        }

        const float done = (float)(i + 1);
        const float rest = total - done;
        const float denomMax = (float)inFrame + rest;
        if (denomMax > 0.0f && (sum + rest) / denomMax < threshold)
        { outScore = 0.0f; return false; }
        if (abandonAvg > 0.0f && (i + 1) >= minEval && inFrame >= minEval
            && sum / (float)inFrame < abandonAvg)
        { outScore = 0.0f; return false; }
    }

    // 覆盖率不足：得分只由极少数图内点决定，不可信 → 直接判失败
    const float denom = (float)inFrame;
    if (denom < total * kMinCoverage) { outScore = 0.0f; return false; }

    float s = sum / denom;
    if (s < 0.0f) s = 0.0f;      // 归一到 0~1
    if (s > 1.0f) s = 1.0f;
    outScore = s;
    return true;
}

bool ScoreBetter(const Candidate& a, const Candidate& b) { return a.score > b.score; }

// ---------------------------------------------------------------------------
// 诊断开关（只在排查现场问题时用，默认全关，不影响任何算法行为 / 测试口径）
//   VA_SHAPE_DEBUG=1  粗扫统计打到 stderr：金字塔层数、位置数（预筛前后）、姿态数、
//                     建表 / 粗扫耗时、前几名候选。用来回答"为什么慢 / 为什么没命中"。
//   VA_SHAPE_PRECISE=1 强制关掉方向响应图，退回精确双线性路径。
//                     用来做 A/B：把「同一张图、同一组参数」的两条路径耗时与命中直接对比。
// ---------------------------------------------------------------------------
// getenv 只读环境变量、不写缓冲区，C4996 是命名安全检查的误报（此处不接受 _dupenv_s 的堆分配）
#pragma warning(push)
#pragma warning(disable: 4996)
bool EnvFlag(const char* name)
{
    const char* e = std::getenv(name);
    return e != 0 && e[0] == '1';
}
#pragma warning(pop)

bool DebugTrace()
{
    static const bool on = EnvFlag("VA_SHAPE_DEBUG");
    return on;
}

bool ForcePrecise()
{
    static const bool on = EnvFlag("VA_SHAPE_PRECISE");
    return on;
}

// ---------------------------------------------------------------------------
// 粗层评分：梯度方向响应图查表版（与 ScorePose 同一套剪枝 / 分母 / 覆盖率语义）
//
//   每个点只做三件事：算整数像素坐标 → 读 1 字节方向档位 → 查 1 次 cos 表。
//   相比 ScorePose 的双线性（8 次浮点读 + 加权）+ 开方 + 除法 + 钳位，
//   Debug（未开优化、不内联）下差距最明显 —— 粗层正是这条路径的主耗时。
//
//   ⚠️ 口径必须与 ScorePose 逐条对齐，否则粗层挑出的候选与最终得分脱节：
//     · 越界点不计入分母（return false 效果相同：不加分也不计数）
//     · 图内平坦点计入分母但贡献 0
//     · 理论上界 / 贪婪下限 / 覆盖率下限三处判据公式完全一致
//   ⚠️ 采样用最近像素（位置本身是整数格点，模板点偏移才带小数）；这是粗层专用的近似，
//      最细层的亚像素与最终重评仍走精确双线性，故不影响对外精度。
// ---------------------------------------------------------------------------
bool ScorePoseFast(const DirLevel& lv, const Pose& pose,
                   float cx, float cy, float threshold, float abandonAvg,
                   float& outScore)
{
    const size_t n = pose.bin.size();
    if (n == 0) { outScore = 0.0f; return false; }

    const int w = lv.bins.cols;
    const int h = lv.bins.rows;

    // 原始指针 + 下标：Debug 下 std::vector::operator[] 带迭代器检查（每次都是函数调用）
    const float* pOx = &pose.ox[0];
    const float* pOy = &pose.oy[0];
    const unsigned char* pBin = &pose.bin[0];
    const unsigned char* pMap = lv.bins.ptr<unsigned char>(0);
    const float* pCos = g_dirCos.v;

    const float total = (float)n;

    size_t mEval = n / 8;
    if (mEval < 4) mEval = 4;
    if (mEval > 8) mEval = 8;
    if (mEval > n) mEval = n;
    const size_t minEval = mEval;

    float sum = 0.0f;
    size_t inFrame = 0;

    for (size_t i = 0; i < n; ++i)
    {
        // 最近像素：先向下取整再加 0.5 判定（不能用 (int) 截断 —— 负数会朝 0 取整）
        const float fx = cx + pOx[i];
        const float fy = cy + pOy[i];
        int ix = (int)fx;
        if (fx < 0.0f) --ix;
        if (fx - (float)ix >= 0.5f) ++ix;
        int iy = (int)fy;
        if (fy < 0.0f) --iy;
        if (fy - (float)iy >= 0.5f) ++iy;

        if (ix >= 0 && iy >= 0 && ix < w && iy < h)
        {
            ++inFrame;      // 图内点一律计入分母（平坦点贡献 0，与 ScorePose 一致）

            const unsigned char b = pMap[(size_t)iy * (size_t)w + ix];
            if (b != kDirNone)
                sum += pCos[(int)pBin[i] * kDirBins + (int)b];
        }

        // 每 4 点一次剪枝，与 SSE 版同频（粗层点数少，太稀会失去剪枝效果）
        if ((i & 3) == 3)
        {
            const float done = (float)(i + 1);
            const float rest = total - done;
            const float denomMax = (float)inFrame + rest;
            if (denomMax > 0.0f && (sum + rest) / denomMax < threshold)
            { outScore = 0.0f; return false; }
            if (abandonAvg > 0.0f && (i + 1) >= minEval && inFrame >= minEval
                && sum / (float)inFrame < abandonAvg)
            { outScore = 0.0f; return false; }
        }
    }

    const float denom = (float)inFrame;
    if (denom < total * kMinCoverage) { outScore = 0.0f; return false; }

    float s = sum / denom;
    if (s < 0.0f) s = 0.0f;
    if (s > 1.0f) s = 1.0f;
    outScore = s;
    return true;
}

// ---------------------------------------------------------------------------
// 反向边缘覆盖（Symmetric 选项专用）
//
// 原 ScorePose 只回答「模板边缘在图像里有没有被支持」，目标上多出来的边缘不扣分。
// 对纯圆模板，带十字/四耳的圆会把圆边缘全部支持，得分 0.98 —— 与真圆几乎不可分。
// 反向评分反过来问：候选框内的图像边缘点，有多少能被某条模板边缘解释？
// 多出来的内部线条在这里没有对应模板点，覆盖率会显著下降。
//
// 只在精修后的 top-N 候选上做，粗扫 / 金字塔精化路径完全不变；默认关闭。
// ---------------------------------------------------------------------------
const float kReverseRadiusPx = 3.0f;        // 允许的训练/匹配边缘定位误差（像素）
const float kReverseMinCos = 0.70f;         // 梯度方向近似一致（约 46° 以内）
const float kReverseMinGrad = 20.0f;        // 只把明显梯度像素当"图像边缘"，与 Canny 低阈值同量级
const int   kReverseMaxSamples = 20000;     // 单个候选最多采样多少图像边缘像素

struct TemplatePointGrid {
    int minX, minY, cols, rows;
    float cell;
    std::vector<int> start;   // CSR 行偏移，长度 = cols*rows+1
    std::vector<int> ids;

    TemplatePointGrid() : minX(0), minY(0), cols(0), rows(0), cell(1.0f) {}

    void Build(const Pose& pose)
    {
        start.clear();
        ids.clear();
        if (pose.ox.empty()) return;

        float loX = pose.ox[0], hiX = pose.ox[0];
        float loY = pose.oy[0], hiY = pose.oy[0];
        for (size_t i = 1; i < pose.ox.size(); ++i)
        {
            loX = std::min(loX, pose.ox[i]); hiX = std::max(hiX, pose.ox[i]);
            loY = std::min(loY, pose.oy[i]); hiY = std::max(hiY, pose.oy[i]);
        }

        minX = (int)std::floor(loX);
        minY = (int)std::floor(loY);
        cell = std::max(4.0f, kReverseRadiusPx * 3.0f);
        cols = (int)std::ceil((hiX - (float)minX) / cell) + 1;
        rows = (int)std::ceil((hiY - (float)minY) / cell) + 1;
        if (cols < 1) cols = 1;
        if (rows < 1) rows = 1;

        start.assign((size_t)cols * (size_t)rows + 1, 0);
        for (size_t i = 0; i < pose.ox.size(); ++i)
        {
            int gx = (int)std::floor((pose.ox[i] - (float)minX) / cell);
            int gy = (int)std::floor((pose.oy[i] - (float)minY) / cell);
            gx = gx < 0 ? 0 : (gx >= cols ? cols - 1 : gx);
            gy = gy < 0 ? 0 : (gy >= rows ? rows - 1 : gy);
            ++start[(size_t)gy * (size_t)cols + (size_t)gx + 1];
        }
        for (size_t i = 1; i < start.size(); ++i) start[i] += start[i - 1];

        ids.resize(pose.ox.size());
        std::vector<int> cursor(start.begin(), start.end() - 1);
        for (size_t i = 0; i < pose.ox.size(); ++i)
        {
            int gx = (int)std::floor((pose.ox[i] - (float)minX) / cell);
            int gy = (int)std::floor((pose.oy[i] - (float)minY) / cell);
            gx = gx < 0 ? 0 : (gx >= cols ? cols - 1 : gx);
            gy = gy < 0 ? 0 : (gy >= rows ? rows - 1 : gy);
            const size_t c = (size_t)gy * (size_t)cols + (size_t)gx;
            ids[(size_t)cursor[c]++] = (int)i;
        }
    }

    // 查询候选点附近是否有模板点，返回方向余弦的最大值
    float BestCos(const cv::Mat& gx, const cv::Mat& gy, const Pose& pose,
                  float qx, float qy, int ix, int iy) const
    {
        if (cols <= 0 || rows <= 0 || start.empty()) return 0.0f;

        int cx0 = (int)std::floor((qx - (float)minX) / cell) - 1;
        int cy0 = (int)std::floor((qy - (float)minY) / cell) - 1;
        int cx1 = cx0 + 2;   // cell >= 2*r + 1，3×3 格一定覆盖半径 r
        int cy1 = cy0 + 2;

        const float vx = gx.ptr<float>(iy)[ix];
        const float vy = gy.ptr<float>(iy)[ix];
        const float mag = std::sqrt(vx * vx + vy * vy);
        if (mag <= 1e-6f) return 0.0f;

        const float r2 = kReverseRadiusPx * kReverseRadiusPx;
        float best = 0.0f;

        for (int yy = cy0; yy <= cy1; ++yy)
        {
            if (yy < 0 || yy >= rows) continue;
            for (int xx = cx0; xx <= cx1; ++xx)
            {
                if (xx < 0 || xx >= cols) continue;
                const size_t c = (size_t)yy * (size_t)cols + (size_t)xx;
                for (int k = start[c]; k < start[c + 1]; ++k)
                {
                    const int i = ids[(size_t)k];
                    const float dx = pose.ox[(size_t)i] - qx;
                    const float dy = pose.oy[(size_t)i] - qy;
                    if (dx * dx + dy * dy > r2) continue;

                    float cosv = (pose.rx[(size_t)i] * vx + pose.ry[(size_t)i] * vy) / mag;
                    if (cosv > best) best = cosv > 1.0f ? 1.0f : cosv;
                }
            }
        }
        return best;
    }
};

float ReverseCoverage(const GradLevel& lv, const Pose& pose,
                      float cx, float cy, double halfW, double halfH, double angleDeg)
{
    const int iw = lv.gx.cols;
    const int ih = lv.gy.rows;
    if (iw <= 0 || ih <= 0 || pose.ox.empty()) return 0.0f;

    TemplatePointGrid grid;
    grid.Build(pose);

    const double a = angleDeg * kDeg2Rad;
    const float ca = (float)std::cos(a);
    const float sa = (float)std::sin(a);
    const float r = kReverseRadiusPx;

    const double rad = std::sqrt(halfW * halfW + halfH * halfH) + r;
    int x0 = (int)std::floor((double)cx - rad);
    int y0 = (int)std::floor((double)cy - rad);
    int x1 = (int)std::ceil((double)cx + rad);
    int y1 = (int)std::ceil((double)cy + rad);
    if (x0 < 0) x0 = 0;
    if (y0 < 0) y0 = 0;
    if (x1 >= iw) x1 = iw - 1;
    if (y1 >= ih) y1 = ih - 1;
    if (x0 > x1 || y0 > y1) return 0.0f;

    const long long area = (long long)(x1 - x0 + 1) * (long long)(y1 - y0 + 1);
    int step = 1;
    if (area > kReverseMaxSamples)
        step = (int)std::ceil(std::sqrt((double)area / (double)kReverseMaxSamples));
    if (step < 1) step = 1;

    const float hw = (float)halfW;
    const float hh = (float)halfH;
    int total = 0;
    int explained = 0;

    for (int yy = y0; yy <= y1; yy += step)
    {
        const float* px = lv.gx.ptr<float>(yy);
        const float* py = lv.gy.ptr<float>(yy);
        for (int xx = x0; xx <= x1; xx += step)
        {
            const float vx = px[xx];
            const float vy = py[xx];
            // 与训练侧 Canny 的低阈值同量级：只评价真正的边缘像素。
            // 若用"非平坦"判据，压缩噪声/抗锯齿梯度会撑大分母，覆盖率被稀释到 0.1 以下。
            if (vx * vx + vy * vy < kReverseMinGrad * kReverseMinGrad) continue;

            const float dx = (float)xx - cx;
            const float dy = (float)yy - cy;

            // 世界坐标 → 模板局部坐标（BuildPose 用的旋转的逆变换）
            const float lx = dx * ca + dy * sa;
            const float ly = -dx * sa + dy * ca;
            if (lx < -hw - r || lx > hw + r || ly < -hh - r || ly > hh + r) continue;

            ++total;
            if (grid.BestCos(lv.gx, lv.gy, pose, dx, dy, xx, yy) >= kReverseMinCos)
                ++explained;
        }
    }

    const float rev = total > 0 ? (float)explained / (float)total : 0.0f;

    // 仅诊断：VA_SHAPE_SYM_DEBUG=1 时打印反向覆盖的采样规模与坐标口径。
    // 正常路径完全不进入（静态布尔只初始化一次）。
    static const bool dbg = EnvFlag("VA_SHAPE_SYM_DEBUG");
    if (dbg)
    {
        std::fprintf(stderr, "[sym] c=(%.1f,%.1f) a=%.1f pts=%zu grid=(%d,%d %dx%d) "
                             "sample=%d/%d rev=%.3f p0=(%.1f,%.1f)\n",
                     cx, cy, angleDeg, pose.ox.size(), grid.minX, grid.minY, grid.cols, grid.rows,
                     explained, total, rev,
                     pose.ox.empty() ? 0.0f : pose.ox[0],
                     pose.oy.empty() ? 0.0f : pose.oy[0]);
    }

    return rev;
}

} // anonymous namespace

namespace va {

// ToMat 失败时细化错误码：通道数不是 1/3/4 属于「图像类型不支持」，其余按参数非法处理
static int BadImageError(Context* ctx, const VA_Image* img)
{
    if (img != 0 && img->channels != 1 && img->channels != 3 && img->channels != 4)
    {
        return SetErrorEx(ctx, VA_ERR_CHANNEL, "形状匹配", "图像通道数",
                          "非 1/3/4 通道", "1=灰度 / 3=BGR / 4=BGRA",
                          "形状匹配：图像通道数不支持（需要 1=灰度 / 3=BGR / 4=BGRA）");
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

    if (gray == 0)
        return SetErrorEx(ctx, VA_ERR_NO_INPUT, "形状匹配/训练", "gray",
                          "空指针", "必须传入一帧图像", "形状匹配训练：图像为空（未传入帧）");

    try
    {
        cv::Mat g;
        if (!ToMat(gray, g, ctx->lastError)) return BadImageError(ctx, gray);

        // 彩色输入统一转灰度：形状匹配只依赖边缘的梯度方向，与颜色无关。
        // 单通道走 GrayView（引用，不拷贝）—— 训练全程只读。
        cv::Mat grayImg = GrayView(g);

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
                char val[64], exp[64], msg[256];
                std::snprintf(val, sizeof(val), "(%d,%d,%d,%d)", roi[0], roi[1], roi[2], roi[3]);
                std::snprintf(exp, sizeof(exp), "与图像 %dx%d 有交集", grayImg.cols, grayImg.rows);
                std::snprintf(msg, sizeof(msg),
                              "形状匹配训练：模板 ROI 与图像无交集（ROI=(%d,%d,%d,%d)，图像=%dx%d）",
                              roi[0], roi[1], roi[2], roi[3], grayImg.cols, grayImg.rows);
                return SetErrorEx(ctx, VA_ERR_ROI, "形状匹配/训练", "TemplateX/Y/W/H", val, exp, msg);
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
            {
                char val[64], msg[320];
                std::snprintf(val, sizeof(val), "%.1f", contrast);
                std::snprintf(msg, sizeof(msg),
                              "形状匹配训练：模板 ROI 内没有边缘（对比度阈值 %.1f，ROI=%dx%d）；"
                              "请把对比度调低（典型 20~60）或把 ROI 框到有轮廓的区域", contrast, patch.cols, patch.rows);
                return SetErrorEx(ctx, VA_ERR_TEMPLATE_NO_FEATURE, "形状匹配/训练", "Contrast", val,
                                  "阈值过高会让边缘全被丢掉，典型 20~60", msg);
            }
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
        return SetErrorEx(ctx, VA_ERR_ARG, "形状匹配", "image / param / out / max_count",
                          "存在空指针或 max_count<=0", "全部必填且 max_count>=1",
                          "形状匹配：入参为空（图像 / 参数 / 输出缓冲区 / 容量必须全部提供，且容量 >= 1）");
    }

    *outCount = 0;

    // 诊断计数复位（见 BuildPose 处的注释）：只在 VA_SHAPE_DEBUG=1 时真正累加
    g_posePoints.store(0, std::memory_order_relaxed);
    g_poseCountOn.store(DebugTrace(), std::memory_order_relaxed);

    if (!ctx->shape.valid || ctx->shape.pts.empty())
    {
        return SetErrorEx(ctx, VA_ERR_NO_TEMPLATE, "形状匹配", "模板槽位",
                          "模型点数为 0", "先训练/加载模板资产",
                          "形状匹配：尚未训练模板（本节点还没加载到模板资产，或原生上下文被其它节点抢占后未重训）");
    }

    try
    {
        cv::Mat g;
        if (!ToMat(gray, g, ctx->lastError)) return BadImageError(ctx, gray);

        // 彩色输入统一转灰度（1 通道走 GrayView：只引用不拷贝，匹配全程只读）
        cv::Mat grayImg = GrayView(g);
        const int iw = grayImg.cols;
        const int ih = grayImg.rows;

        // ---------- 1. 搜索范围（原图坐标，限制中心可出现的位置）----------
        // 注意：这里的 ROI 限制的是「模板中心」可出现的位置，不是模板外接框。
        // 提前到金字塔之前解析：下面要靠它把金字塔的构建范围裁到「搜索区邻域」。
        int rx = 0, ry = 0, rw = iw, rh = ih;
        const bool hasRoi = (searchRoi != 0 && searchRoi[2] > 0 && searchRoi[3] > 0);
        if (hasRoi)
        {
            rx = std::max(0, searchRoi[0]);
            ry = std::max(0, searchRoi[1]);
            rw = std::min(searchRoi[2], iw - rx);
            rh = std::min(searchRoi[3], ih - ry);
            if (rw <= 0 || rh <= 0)
            {
                char val[64], exp[64], msg[256];
                std::snprintf(val, sizeof(val), "(%d,%d,%d,%d)", searchRoi[0], searchRoi[1], searchRoi[2], searchRoi[3]);
                std::snprintf(exp, sizeof(exp), "与图像 %dx%d 有交集", iw, ih);
                std::snprintf(msg, sizeof(msg),
                              "形状匹配：搜索 ROI 与图像无交集（ROI=(%d,%d,%d,%d)，图像=%dx%d）",
                              searchRoi[0], searchRoi[1], searchRoi[2], searchRoi[3], iw, ih);
                return SetErrorEx(ctx, VA_ERR_ROI, "形状匹配", "SearchX/Y/W/H", val, exp, msg);
            }
        }

        // ---------- 2. 金字塔层数 ----------
        // 约束取「最粗层模板 ≥ 8px、图像 ≥ 32px」。约束过严会让层数退化成 1，
        // 退化后等于全分辨率暴力扫描 —— 这是最常见的性能陷阱。
        // ⚠️ 用**未裁切**的整图尺寸算，裁切后不重算：层数是「用户参数与图像尺寸」的函数，
        //    不能因为搜索区小而变浅，否则同一套参数在 ROI 内外的金字塔深度不同、结果不可比。
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

        // ---------- 3. 角度 / 缩放序列 ----------
        // 也提前：裁切范围要按「最大缩放下模板的外接半径」留边。
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

        // 缩放区间与角度区间同样处理：min/max 填反时交换（原来是把 max 抬到 min，
        // 等于静默只搜一个点）；末尾不足一个步长的部分补齐（原来会漏掉 scale_max）。
        double sMin = param->scale_min;
        if (sMin <= 0.0) sMin = 1.0;
        double sMax = param->scale_max;
        if (sMax <= 0.0) sMax = sMin;
        if (sMax < sMin) { double t = sMin; sMin = sMax; sMax = t; }
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
            if (scales.back() < sMax - 1e-9) scales.push_back(sMax);
        }

        // ---------- 4. 只建「搜索区邻域」的金字塔 ----------
        // 金字塔与各层梯度原本按**整图**构建，与搜索区大小无关：5MP 的全图上搜一个
        // 500×500 的 ROI，光 pyrDown + 两层 Sobel 就要十几毫秒，而真正参与评分的像素
        // 只有 ROI 那一小块。这里把输入裁到「搜索区 ⊕ 模板外接半径 ⊕ 金字塔/Sobel 支撑域」，
        // 之后再平移回原图坐标。
        //
        // 【为什么结果不变，而不是"近似"】
        //   · 中心 ∈ ROI 时，任何模板点到中心的距离 ≤ 0.5*sqrt(tw²+th²)*maxScale，留边覆盖它
        //     ⇒ 每个中心的"图内/图外"判定与原图完全一致，分母不变，score 逐位不变。
        //   · 留边还要覆盖 pyrDown（半径 2·(2^L-1)）与 Sobel（半径 1）的采样支撑域，
        //     ⇒ 参与评分的像素值也与整图构建时相同。
        //   · 层数在裁切前用整图尺寸算好，裁切后强制把窗口撑到 32·2^(levels-1)，
        //     ⇒ 金字塔深度与粗扫网格分辨率跟不裁切时一致。
        int cropX = 0, cropY = 0, cropW = iw, cropH = ih;
        if (hasRoi)
        {
            double maxScaleAbs = scales[0];
            for (size_t i = 1; i < scales.size(); ++i)
                if (scales[i] > maxScaleAbs) maxScaleAbs = scales[i];

            const double halfDiag = 0.5 * std::sqrt(tw * tw + th * th) * maxScaleAbs;
            const int margin = (int)std::ceil(halfDiag) + 3 * (1 << (levels - 1)) + 8;

            int cx0 = rx - margin;
            int cy0 = ry - margin;
            int cx1 = rx + rw + margin;   // 右开
            int cy1 = ry + rh + margin;

            // 至少保住 levels 层所需的 32·2^(levels-1) 像素（撑不动就顶到图像边界）
            const int minSide = 32 << (levels - 1);
            if (cx1 - cx0 < minSide) { const int d = (minSide - (cx1 - cx0) + 1) / 2; cx0 -= d; cx1 += d; }
            if (cy1 - cy0 < minSide) { const int d = (minSide - (cy1 - cy0) + 1) / 2; cy0 -= d; cy1 += d; }

            if (cx0 < 0) cx0 = 0;
            if (cy0 < 0) cy0 = 0;
            if (cx1 > iw) cx1 = iw;
            if (cy1 > ih) cy1 = ih;

            // 贴边裁切后宽度可能又被削回不足 minSide（左右都被图像边界顶住），
            // 这时只能往图像内侧补足；iw >= minSide 由 maxLevel 的推导保证，补得回来。
            if (cx1 - cx0 < minSide && cx1 - cx0 < iw)
            {
                if (cx0 <= 0) cx1 = std::min(iw, minSide);
                else          cx0 = std::max(0, cx1 - minSide);
            }
            if (cy1 - cy0 < minSide && cy1 - cy0 < ih)
            {
                if (cy0 <= 0) cy1 = std::min(ih, minSide);
                else          cy0 = std::max(0, cy1 - minSide);
            }

            // 裁完省不下 10% 面积就不折腾（省的是 memcpy 与边界处理的零头，不值得引入坐标平移）
            const double cropArea = (double)(cx1 - cx0) * (double)(cy1 - cy0);
            if (cropArea <= 0.9 * (double)iw * (double)ih)
            {
                cropX = cx0; cropY = cy0;
                cropW = cx1 - cx0; cropH = cy1 - cy0;
            }
        }

        // 搜索范围平移到裁切坐标（hasRoi 时 ROI 必然完整落在窗口内，见上面的留边推导）
        rx -= cropX;
        ry -= cropY;

        // ---------- 5. 图像金字塔与梯度 ----------
        cv::Mat base = grayImg;
        if (cropW != iw || cropH != ih)
            base = grayImg(cv::Rect(cropX, cropY, cropW, cropH));

        std::vector<cv::Mat> pyr(levels);
        pyr[0] = base;
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
                {
                    char val[64], msg[256];
                    std::snprintf(val, sizeof(val), "%gx%g", ctx->shape.width, ctx->shape.height);
                    std::snprintf(msg, sizeof(msg),
                                  "形状匹配：模板 %gx%g 在第 %d 层缩到 %dx%d，小于 8x8（特征点一个也放不下）；"
                                  "请减少金字塔层数或放大模板",
                                  ctx->shape.width, ctx->shape.height, L,
                                  (int)(ctx->shape.width / (double)(1 << L)),
                                  (int)(ctx->shape.height / (double)(1 << L)));
                    return SetErrorEx(ctx, VA_ERR_TEMPLATE_TOO_SMALL, "形状匹配", "PyramidLevels",
                                      "该层模板 < 8x8", "每层模板边长 >= 8px", msg);
                }
                return VA_ERR_SIZE;
            }
        }

        // 各点的方向档位：只算一次，姿态阶段按角度整体平移（旋转不改变相对档位）
        for (int L = 0; L < levels; ++L)
        {
            const size_t n = mls[L].dir.size();
            mls[L].bin.resize(n);
            for (size_t i = 0; i < n; ++i)
            {
                const cv::Point2f d = mls[L].dir[i];
                double ang = std::atan2((double)d.y, (double)d.x);
                if (ang < 0.0) ang += 2.0 * kPi;
                int b = (int)(ang * (double)kDirBins / (2.0 * kPi));
                if (b < 0) b = 0;
                if (b >= kDirBins) b = kDirBins - 1;
                mls[L].bin[i] = (unsigned char)b;
            }
        }

        // ---------- 3b. 诚实重评专用点集（最细层全量）----------
        // 粗层 1024 点的上限是为了压住 (位置 × 姿态) 的乘法成本；但步骤 9 的「诚实重评」
        // 只对 keep 个候选各跑一遍，成本与粗扫无关。原实现两步共用 mls[0]：
        // 模型训练了 1500 点（默认 max_features）却在最细层被 stride=2 抽成 750 点，
        // 等于「对外 score 只用了一半特征证据」。这里单独建一份全量点集，
        // 只给重评用 —— 精化 / 亚像素仍走抽稀集，速度一点不加。
        ModelLevel mlScore;
        {
            size_t stride = 1;
            if (pointCount > kMaxPointsFinest) stride = pointCount / kMaxPointsFinest + 1;
            mlScore.pts.reserve(pointCount / stride + 1);
            mlScore.dir.reserve(pointCount / stride + 1);
            for (size_t i = 0; i < pointCount; i += stride)
            {
                mlScore.pts.push_back(ctx->shape.pts[i]);
                mlScore.dir.push_back(ctx->shape.dir[i]);
            }
        }

        // 贪婪系数：越大越激进（越快）；<=0 表示只用理论上界，不启用启发式
        double greediness = param->greediness;
        if (greediness < 0.0) greediness = 0.0;
        if (greediness > 1.0) greediness = 1.0;

        // Symmetric 是可选精评：默认关闭，绝不改变粗扫 / 精化路径。
        const bool symmetric = (param->symmetric != 0);

        // ---------- 6. 最粗层全局扫描 ----------
        // 循环次序：位置在外、姿态在内 —— 这样才能拿「本位置当前最优」当剪枝阈值，
        // 也能让同一位置的所有姿态共处一个并行域（原实现每个姿态 fork/join 一次）。
        // 姿态仍然只预计算一次（按块预计算，块内所有位置复用）。
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

        // 粗层姿态轴：按 kCoarsePoseStride 抽稀，首末值一定保留（保证搜索区间端点是覆盖的）。
        // 金字塔只有一层时没有「下一层精化」来兜住抽稀带来的量化误差，此时不抽稀。
        const size_t poseStride = (L0 >= 1) ? kCoarsePoseStride : 1;
        std::vector<size_t> aiList;
        std::vector<size_t> siList;
        aiList.reserve(angles.size());
        siList.reserve(scales.size());
        for (size_t ai = 0; ai < angles.size(); ++ai)
            if (ai % poseStride == 0 || ai + 1 == angles.size()) aiList.push_back(ai);
        for (size_t si = 0; si < scales.size(); ++si)
            if (si % poseStride == 0 || si + 1 == scales.size()) siList.push_back(si);

        const size_t nPoses = aiList.size() * siList.size();

        // ---------- 6b. 粗层查表加速的两张前置表 ----------
        // 只有金字塔 ≥ 2 层时才启用：此时最粗层只是「挑候选」，近似采样不会传到最终得分上
        //（最细层的亚像素精化与最终重评仍走精确双线性）。单层金字塔即最终层，一律走精确路径。
        DirLevel dirLv;
        EdgeSupport edgeSup;
        const bool trace = DebugTrace();

        // 建方向图 + 位置预筛是一次性固定开销（本机约 1~2 ms）。只有当「位置数 × 姿态数」
        // 足够大时才摊得回来：小规模搜索（如 ±10° 共 21 个姿态）走精确路径反而更快。
        // 阈值来自实测：BENCH-A（19200 位置 × 11 姿态 = 21 万）走查表反而慢 2.7 ms，
        // BENCH-B（× 31 姿态 = 60 万）开始转正。
        const size_t coarseWork = (size_t)nPos * nPoses;
        const bool useFast = (L0 >= 1) && (coarseWork >= kFastPathMinWork) && !ForcePrecise();
        std::chrono::steady_clock::time_point tBuild = std::chrono::steady_clock::now();
        if (useFast)
            BuildDirMap(grads[L0].gx, grads[L0].gy, dirLv, edgeSup);
        const double msBuild = trace
            ? std::chrono::duration<double, std::milli>(std::chrono::steady_clock::now() - tBuild).count()
            : 0.0;

        // 位置预筛：模板外接圆覆盖的矩形里一个梯度像素都没有 → 该位置所有姿态的分子都是 0，
        // 分数 0 必然低于粗层阈值，整位置可跳过。这是**严格上界**（不是启发式）：只要一个像素
        // 带梯度落在了这个矩形外，就意味着模板点也落不进去（矩形是按半对角线取的，必然包住全部点）。
        // 工业图大片平坦背景时，这一步能把参与扫描的位置数砍掉一个量级。
        std::vector<unsigned char> posKeep((size_t)nPos, 1);
        if (useFast && edgeSup.valid)
        {
            double maxScale = scales[0];
            for (size_t i = 1; i < scales.size(); ++i)
                if (scales[i] > maxScale) maxScale = scales[i];

            const double rad = 0.5 * std::sqrt(tw * tw + th * th) * maxScale * f0;
            const cv::Mat& S = edgeSup.sum;

            // 从「边缘像素数 > 0」加强到「边缘像素数够不够撑到粗层阈值」：
            //   任何被采纳的位姿都必须通过覆盖率下限 inFrame >= n*kMinCoverage，
            //   而 sum <= cnt（每个模板点至多贡献 1，且贡献点必定落在某个带梯度的像素上），
            //   所以 score = sum/inFrame <= cnt/(n*kMinCoverage)。
            //   cnt 达不到 thrCoarse*n*kMinCoverage 的位置，**所有**姿态都必然低于粗层阈值，
            //   整块跳过 —— 这是严格上界，不是启发式，不会漏掉真目标。
            const double nPts = (double)mls[L0].pts.size();
            const double needCnt = (double)coarseThr * nPts * (double)kMinCoverage;

            for (int p = 0; p < nPos; ++p)
            {
                int bx0 = (int)std::floor((double)posX[(size_t)p] - rad);
                int by0 = (int)std::floor((double)posY[(size_t)p] - rad);
                int bx1 = (int)std::ceil((double)posX[(size_t)p] + rad);
                int by1 = (int)std::ceil((double)posY[(size_t)p] + rad);

                if (bx1 < 0 || by1 < 0 || bx0 > lw - 1 || by0 > lh - 1)
                { posKeep[(size_t)p] = 0; continue; }

                if (bx0 < 0) bx0 = 0;
                if (by0 < 0) by0 = 0;
                if (bx1 > lw - 1) bx1 = lw - 1;
                if (by1 > lh - 1) by1 = lh - 1;

                const int* r0 = S.ptr<int>(by0);
                const int* r1 = S.ptr<int>(by1 + 1);
                const int cnt = r1[bx1 + 1] - r1[bx0] - r0[bx1 + 1] + r0[bx0];
                if ((double)cnt < needCnt) posKeep[(size_t)p] = 0;
            }
        }

        // 单块姿态表的内存上限 → 每块最多装多少个姿态
        const size_t perPoseBytes = mls[L0].pts.size() * sizeof(float) * 4;
        size_t chunkMax = kCoarsePoseChunkBytes / (perPoseBytes > 0 ? perPoseBytes : 1);
        if (chunkMax < 1) chunkMax = 1;
        if (chunkMax > nPoses) chunkMax = nPoses;

        std::chrono::steady_clock::time_point tCoarse = std::chrono::steady_clock::now();
        // 姿态表（旋转/缩放后的点集）构建耗时：单独累计，便于回答
        // 「把这部分预计算/落盘能不能提速」——实测占比只有粗扫的百分之一量级。
        double msPoseBuild = 0.0;
        for (size_t p0 = 0; p0 < nPoses; p0 += chunkMax)
        {
            // 本块参与比较的姿态表（预计算一次，块内所有位置复用）
            std::vector<Pose> poses;
            std::vector<float> poseA;
            std::vector<float> poseS;
            poses.reserve(chunkMax);
            poseA.reserve(chunkMax);
            poseS.reserve(chunkMax);

            std::chrono::steady_clock::time_point tPose = std::chrono::steady_clock::now();

            for (size_t k = p0; k < nPoses && poses.size() < chunkMax; ++k)
            {
                const size_t si = k / aiList.size();
                const size_t ai = k % aiList.size();
                const float a = (float)angles[aiList[ai]];
                const float sc = (float)scales[siList[si]];

                Pose ps;
                BuildPose(mls[L0], a, sc, ps);
                poses.push_back(std::move(ps));   // Pose 只有四个 vector<float>，移动即转移缓冲区
                poseA.push_back(a);
                poseS.push_back(sc);
            }

            msPoseBuild += std::chrono::duration<double, std::milli>(
                std::chrono::steady_clock::now() - tPose).count();

            const size_t nChunk = poses.size();
            const Pose* __restrict pPoses = poses.empty() ? 0 : &poses[0];
            const float* pPoseA = poseA.empty() ? 0 : &poseA[0];
            const float* pPoseS = poseS.empty() ? 0 : &poseS[0];

#ifdef _OPENMP
            #pragma omp parallel for schedule(dynamic, 64)
#endif
            for (int p = 0; p < nPos; ++p)
            {
                if (DeadlineHit()) continue;         // 软超时：本位置直接跳过（并行区内不能 break）
                if (!posKeep[(size_t)p]) continue;   // 6b 的严格位置预筛：必然 0 分

                const float px = (float)posX[(size_t)p];
                const float py = (float)posY[(size_t)p];

                float best = bestScore[(size_t)p];
                for (size_t k = 0; k < nChunk; ++k)
                {
                    // 位置级剪枝：本位置已找到更高分的姿态时，用「当前最优」当理论上界阈值。
                    // ScorePose 的 threshold 语义是「上界达不到就退出」，所以这是数学等价的
                    // （不会漏掉更好的姿态），只是把已失败姿态的评估点数量压到最少。
                    const float thr = (best > thrCoarse) ? best : thrCoarse;

                    float s = 0.0f;
                    const bool ok = useFast
                        ? ScorePoseFast(dirLv, pPoses[k], px, py, thr, avgCoarse, s)
                        : ScorePose(grads[L0], pPoses[k], px, py, thr, avgCoarse, s);

                    if (ok && s > best)
                    {
                        best = s;
                        bestAngle[(size_t)p] = pPoseA[k];
                        bestScale[(size_t)p] = pPoseS[k];
                    }
                }
                bestScore[(size_t)p] = best;
            }
        }

        const double msCoarse = trace
            ? std::chrono::duration<double, std::milli>(std::chrono::steady_clock::now() - tCoarse).count()
            : 0.0;

        // 软超时：粗扫已被打断（每个位置都在 continue，循环很快空转完），这里直接收工。
        // 【为什么在粗扫之后判一次】精化（最细层 × 25 位置 × 25 姿态 × 全点）与粗扫同量级，
        // 不在这里挡住，超时后还要再白烧一遍同样长的时间。
        if (DeadlineHit())
            return SetErrorEx(ctx, VA_ERR_ALGO_TIMEOUT, "形状匹配", "TimeoutMs",
                              "超时", "在超时前完成",
                              "算法在超时时间内未完成搜索（粗扫被中断）；请缩小搜索 ROI / 减少角度或缩放档位，或调大「超时(ms)」");

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

        if (DebugTrace())
        {
            std::sort(cands.begin(), cands.end(), ScoreBetter);
            int keptPos = 0;
            for (int p = 0; p < nPos; ++p) if (posKeep[(size_t)p]) ++keptPos;
            int edgeTotal = 0;
            if (edgeSup.valid) edgeTotal = edgeSup.sum.at<int>(lh, lw);
            std::fprintf(stderr, "[shape] levels=%d L0=%d crop=(%d,%d %dx%d) nPos=%d keep=%d nPoses=%zu fast=%d cands=%zu "
                         "build=%.1fms pose=%.2fms(%lldpts) coarse=%.1fms edgePx=%d  best: ",
                         levels, L0, cropX, cropY, cropW, cropH, nPos, keptPos, nPoses, (int)useFast,
                         cands.size(), msBuild, msPoseBuild,
                         (long long)g_posePoints.load(std::memory_order_relaxed), msCoarse, edgeTotal);
            for (size_t i = 0; i < cands.size() && i < 6; ++i)
                std::fprintf(stderr, "(%.1f,%.1f a=%.0f s=%.3f) ",
                             cands[i].cx + (double)cropX, cands[i].cy + (double)cropY,
                             cands[i].angle, cands[i].score);
            std::fprintf(stderr, "\n");
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

        // 空间分散：同一目标的分数平台在粗层很宽（模板内部处处都能拿高分），
        // 直接按分数截断会让前 keep 名全落在它内部，第二个目标因此进不了精化。
        // 这里按中心距做一次贪心稀疏化：只保留彼此相距至少「2 个最粗层像素」的候选。
        {
            const double sep = kCandidateSepLevelFactor * (double)(1 << L0);
            const double sep2 = sep * sep;
            std::vector<Candidate> picked;
            picked.reserve((size_t)keep);
            for (size_t i = 0; i < cands.size() && (int)picked.size() < keep; ++i)
            {
                const Candidate& c = cands[i];
                bool near = false;
                for (size_t j = 0; j < picked.size(); ++j)
                {
                    const double ddx = c.cx - picked[j].cx;
                    const double ddy = c.cy - picked[j].cy;
                    if (ddx * ddx + ddy * ddy < sep2) { near = true; break; }
                }
                if (!near) picked.push_back(c);
            }
            cands.swap(picked);
        }

        // ---------- 7. 逐层向下精化（候选之间互不依赖，可并行）----------
        // 精化阶段只关心「能不能比当前候选更好」，所以阈值由 bestScore 动态给出，
        // 这里只需要贪婪下限
        const float avgFine = (float)(param->min_score * greediness);

        for (int L = L0 - 1; L >= 0; --L)
        {
            const double f = 1.0 / (double)(1 << L);
            const int win = (L == 0) ? 2 : 1;
            // 角度 / 缩放窗口取 ±2 步：粗层姿态抽稀后最坏偏差是 1 个步长，
            // 再叠加粗层定位误差会到 1~2 个步长，只搜 ±1 步会收敛不到位。
            const int aWin = 2;
            const int sWin = 2;

#ifdef _OPENMP
            #pragma omp parallel for schedule(dynamic)
#endif
            for (int ci = 0; ci < (int)cands.size(); ++ci)
            {
                if (DeadlineHit()) continue;      // 软超时：不再精化剩余候选
                const Candidate c = cands[(size_t)ci];

                double bestScore = 0.0;
                double bestCx = c.cx;
                double bestCy = c.cy;
                double bestAngle = c.angle;
                double bestScale = c.scale;

                // 复用同一个 Pose：BuildPose 内部是 resize（容量不变就不重新分配），
                // 原实现每个姿态都新建一个 Pose，等于每个 (候选 × 角度 × 缩放) 做 4 次堆分配 ——
                // 8 候选 × 25 姿态 × 2 层 ≈ 1600 次分配，纯属白给。
                Pose pose;

                // 先用当前位姿在该层评分，作为后续比较的基准
                {
                    BuildPose(mls[L], (float)c.angle, (float)c.scale, pose);
                    float s0 = 0.0f;
                    if (ScorePose(grads[L], pose, (float)(c.cx * f), (float)(c.cy * f),
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

                        BuildPose(mls[L], (float)a, (float)s, pose);

                        for (int dy = -win; dy <= win; ++dy)
                        {
                            for (int dx = -win; dx <= win; ++dx)
                            {
                                const float ccx = (float)(c.cx * f) + (float)dx;
                                const float ccy = (float)(c.cy * f) + (float)dy;

                                float sc = 0.0f;
                                if (ScorePose(grads[L], pose, ccx, ccy,
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
                if (DeadlineHit()) continue;      // 软超时：不再精化剩余候选
                const Candidate c = cands[(size_t)ci];

                // 同一个 Pose 走完「位置精化 → 角度/缩放精化」两段（容量复用，不重复分配）
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

                        BuildPose(mls[0], (float)a, (float)s, pl);

                        float sc = 0.0f;
                        if (ScorePose(grads[0], pl, (float)bestCx, (float)bestCy,
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

        // Symmetric 只重评前向分最高的少量候选：反向覆盖要遍历候选框内图像边缘，
        // 成本与 (候选数 × 模板面积) 成正比，不能对 keep 个候选全做。
        // 先按前向分排序，保证留下的是最可能进入最终结果的那些。
        int symmetricLimit = 0;
        if (symmetric)
        {
            std::sort(cands.begin(), cands.end(), ScoreBetter);
            symmetricLimit = maxMatches * 12;
            if (symmetricLimit < 8) symmetricLimit = 8;
            if (symmetricLimit > 64) symmetricLimit = 64;
            if (symmetricLimit > (int)cands.size()) symmetricLimit = (int)cands.size();
        }

        // ---------- 9. 最细层诚实重评 ----------
        // 上面每一级都带阈值剪枝，返回的分数只是「不低于真实分」的近似值（剪得越早越乐观）。
        // 下游拿 score 判 OK/NG，必须是真实分：这里用 threshold=0、abandonAvg=0
        // （即不剪枝）在最细层重算一次。代价 = 候选数 × 全点评估，相对粗扫可忽略。
        // 用 mlScore（全量特征点，见 3b）而不是 mls[0]（抽稀集）：重评是对外口径，
        // 应当吃掉训练时的全部证据，多出来的成本只有 keep 个候选各一遍。
        {
#ifdef _OPENMP
            #pragma omp parallel for schedule(dynamic)
#endif
            for (int ci = 0; ci < (int)cands.size(); ++ci)
            {
                if (DeadlineHit()) continue;      // 软超时：不再重评剩余候选
                Candidate& c = cands[(size_t)ci];
                Pose pl;
                BuildPose(mlScore, (float)c.angle, (float)c.scale, pl);

                float s = 0.0f;
                if (ScorePose(grads[0], pl, (float)c.cx, (float)c.cy, 0.0f, 0.0f, s))
                {
                    if (symmetric && ci < symmetricLimit)
                    {
                        // 反向覆盖：候选框内的图像边缘有多少能被模板边缘解释。
                        // 用几何平均合成，既保留自匹配的近似 1.0，又让"目标多出边缘"显著扣分。
                        const double halfW = 0.5 * ctx->shape.width * c.scale;
                        const double halfH = 0.5 * ctx->shape.height * c.scale;
                        float rev = ReverseCoverage(grads[0], pl, (float)c.cx, (float)c.cy,
                                                    halfW, halfH, c.angle);
                        c.score = (float)std::sqrt((double)s * (double)rev);
                    }
                    else
                    {
                        c.score = s;
                    }
                }
                else
                {
                    c.score = 0.0;   // 覆盖率不足（目标大半出画）→ 不予输出
                }
            }
        }

        // ---------- 10. 排序 + 非极大值抑制 + 输出 ----------
        std::sort(cands.begin(), cands.end(), ScoreBetter);

        double overlap = param->max_overlap;
        if (overlap <= 0.0) overlap = 0.5;
        if (overlap > 1.0) overlap = 1.0;

        const double minScore = param->min_score;
        std::vector<Candidate> accepted;
        std::vector<cv::RotatedRect> acceptedRect;

        // 复用同一个缓冲区，避免每次求交都分配
        std::vector<cv::Point2f> interPoly;

        for (size_t i = 0; i < cands.size() && (int)accepted.size() < maxMatches; ++i)
        {
            const Candidate& c = cands[i];
            if (c.score < minScore) break;

            const double w = ctx->shape.width * c.scale;
            const double h = ctx->shape.height * c.scale;
            const cv::RotatedRect ra(cv::Point2f((float)c.cx, (float)c.cy),
                                     cv::Size2f((float)w, (float)h), (float)c.angle);

            bool dup = false;
            for (size_t j = 0; j < acceptedRect.size(); ++j)
            {
                const cv::RotatedRect& rb = acceptedRect[j];
                const double wB = (double)rb.size.width;
                const double hB = (double)rb.size.height;

                // 便宜的外接圆排除：中心距超过两个半对角线之和，一定不相交
                const double reach = 0.5 * (std::sqrt(w * w + h * h) + std::sqrt(wB * wB + hB * hB));
                const double ddx = c.cx - (double)rb.center.x;
                const double ddy = c.cy - (double)rb.center.y;
                if (std::sqrt(ddx * ddx + ddy * ddy) >= reach) continue;

                // 用「旋转外框的交集面积 / 较小框面积」判重。
                // 中心距口径在 ROI 比工件大时会把两个真实相邻目标判成同一个。
                interPoly.clear();
                const int nv = cv::rotatedRectangleIntersection(ra, rb, interPoly);
                double interArea = 0.0;
                if (nv == cv::INTERSECT_FULL)
                {
                    // 一个框完全包住另一个 → 交集就是小框本身（不依赖是否回填多边形）
                    interArea = std::min(w * h, wB * hB);
                }
                else if (nv == cv::INTERSECT_PARTIAL && !interPoly.empty())
                {
                    interArea = std::abs(cv::contourArea(interPoly));
                }
                const double denom = std::min(w * h, wB * hB);
                if (denom > 0.0 && interArea / denom >= overlap) { dup = true; break; }
            }

            if (!dup)
            {
                accepted.push_back(c);
                acceptedRect.push_back(ra);
            }
        }

        int n = (int)accepted.size();
        if (n > maxCount) n = maxCount;

        for (int i = 0; i < n; ++i)
        {
            // 裁切坐标 → 原图坐标（未裁切时 cropX/cropY 恒为 0，行为与改动前完全一致）
            outMatches[i].cx = accepted[i].cx + (double)cropX;
            outMatches[i].cy = accepted[i].cy + (double)cropY;
            outMatches[i].angle = accepted[i].angle;
            outMatches[i].scale = accepted[i].scale;
            outMatches[i].score = accepted[i].score;
            outMatches[i].width = ctx->shape.width * accepted[i].scale;
            outMatches[i].height = ctx->shape.height * accepted[i].scale;
        }

        // 诊断：整次调用累计被旋转缩放的模板点数（粗层姿态表 + 各层精化 + 亚像素）。
        // 按实测 8~12 ns/点（4 张 float 表的写 + 读回）即可估出"姿态计算"在大盘里的占比，
        // 用来判断"把旋转缩放特征预存下来"值不值得做。
        if (trace)
        {
            const long long pts = g_posePoints.load(std::memory_order_relaxed);
            std::fprintf(stderr, "[shape] pose-total=%lldpts (~%.2f~%.2fms)\n",
                         pts, (double)pts * 8.0 / 1e6, (double)pts * 12.0 / 1e6);
        }

        *outCount = n;
        return VA_OK;
    }
    catch (const cv::Exception& e) { SetError(ctx, e); return VA_ERR_INTERNAL; }
    catch (const std::exception& e) { SetError(ctx, e); return VA_ERR_INTERNAL; }
}

} // namespace va
