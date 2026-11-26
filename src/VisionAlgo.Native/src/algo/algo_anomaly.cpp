// 无监督缺陷检测（PaDiM / EfficientAD）：OpenVINO 推理 + PaDiM 原生训练。
//
// 【为什么两类模型放在一个编译单元】它们的加载路径、预处理、特征图后处理高度重合，
// 拆成两个文件就要共享一套"Net 加载 / 前向 / 形状校验"辅助函数，反而更绕。
// 真正的差别只有两段：PaDiM 算逐位置马氏距离，EfficientAD 算学生-教师 + AE 的重建差。
//
// 【PaDiM 为什么可以原生训练】它的"训练"是纯统计：每张 OK 图过一遍特征提取器，
// 逐位置累加均值与协方差，最后 Cholesky 分解一次。没有反向传播，不需要 DL 框架。
// 【EfficientAD 为什么不能原生训练】学生网络要靠蒸馏损失反向传播，自编码器同理；
// 本库只做**推理**（加载导出的 IR 三件套），导出见 docs/算法/异常检测-模型目录与训练.md。
//
// 【维数必须随样本数收缩】逐位置马氏距离隐含假设"协方差估得准"。n 张样本拟合 d 维时，
// 只有 n ≫ d 才成立；n 只比 d 多一点时，训练集自评很好、新图整幅超阈值（假阳性）。
// 因此 d 在 finish 时按 n/3 再压一次，阈值再乘 Hotelling 预测修正 sqrt((n-1)(n+1)/(n(n-d)))。
// 排查现场时开 VA_ANOM_DEBUG=1 看最大分位置的逐维贡献占比。
#include "va_internal.h"
#include "va_error.h"

#include <algorithm>
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <fstream>
#include <sstream>
#include <string>
#include <vector>

namespace va {

namespace {

// ---------------------- 常量 ----------------------

// 内置描述子的 patch 边长（分析分辨率 256 → 32×32 网格）
const int kPatch = 8;

// 单张特征图的维度上限。上限存在的理由不是"算不动"，而是**内存**：
// 逐位置协方差是 P*d² 个浮点，d=64 / P=1024 时已达 16 MB（double 累计器再翻倍）；
// PaDiM 原论文也是先随机降到 d≈100 以下再用，降维不损失多少精度。
const int kMaxDim = 48;

// 协方差收缩强度（相对平均方差）。样本数少时样本协方差接近奇异，
// Cholesky 会失败；加一点对角正则比"再攒点样本"更可靠。
const double kShrink = 1e-3;

// 每张 OK 样本"养得起"几个特征维（**完整协方差**模式）。这条约束是本模块精度的关键：
// 每个位置要拟合 d×d 协方差，样本数 n 只比 d 略多时，样本协方差在新样本的偏离方向上
// 被系统性低估 → 同一个偏离量算出的马氏距离被放大好几倍。
// 实测 n=16 / d=15 时，训练集自评最大分只有 0.996，未见过的 OK 图却到 4.55（整幅超阈值）。
// PaDiM 原文也是 d≈100 配几百张样本，比例同为 3~5 倍，这里取 3。
const double kSamplesPerDim = 3.0;
const int kMinDim = 2;

// 样本数少于这个值时，改用**对角协方差**（每维只估自己的方差），维度上限从 n/3 放宽到 n。
// 理由：现场绝大多数数据集都只有十几张 OK，按 n/3 只能拿到 3~5 维，
// 而内置描述子本身有 15 维 —— 等于把 2/3 的信息直接丢掉。
// 对角模式的代价是忽略维间相关；样本够了（≥24 张）就切回完整协方差拿精度。
const int kDiagSwitchSamples = 24;

// 阈值余量：留出法"用 n-1 张拟合、去打分第 n 张"，得到的分数分布比自评宽，再留 50% 才够。
// 这个系数是标定量，不是精度标定 —— 节点参数里的「判定阈值」可以逐方案覆盖。
// 注意它现在乘的是**留出 P90**（不是 max），对样本少的数据集稳健得多，见阈值标定处的注释。
const float kThrHeldMargin = 1.50f;

// 训练时保留样本特征的预算（float 个数，16M ≈ 64 MB）。
// 保留样本只为了在 finish 时算出「训练集自身每张图的最大分」→ 得到有依据的阈值；
// 超出预算就退化为卡方近似阈值，不影响模型可用性。
const long long kRetainBudget = 16LL * 1024 * 1024;

// EfficientAD 缺省阈值（归一化后的 sigma 倍数）。模型目录里的 model.json 会覆盖它。
const float kEadDefaultThreshold = 6.0f;

// ---------------------- 诊断开关 ----------------------
//   VA_ANOM_DEBUG=1  推理时把「最大分位置的逐维贡献」打到 stderr。
//   用来回答"为什么 OK 图也超阈值"：是某一维方差被低估（该维占比畸高），
//   还是整体尺度被放大（各维占比均匀但总分高）——前者调特征，后者调维数/样本配比。
#pragma warning(push)
#pragma warning(disable: 4996)   // getenv 只读环境变量，C4996 是误报
bool AnomDebug()
{
    static const bool on = []() {
        const char* e = std::getenv("VA_ANOM_DEBUG");
        return e != 0 && e[0] == '1';
    }();
    return on;
}
#pragma warning(pop)

// ---------------------- 极简 JSON 读取 ----------------------
// 只服务本库自己写出的 model.json（扁平、无嵌套、无转义），
// 因此不引入 JSON 库：几百行依赖换 20 行解析，不划算。

std::string ReadAllText(const std::string& path)
{
    std::ifstream f(path.c_str(), std::ios::binary);
    if (!f.is_open()) return std::string();
    std::ostringstream ss;
    ss << f.rdbuf();
    return ss.str();
}

// 找 "key" 之后冒号后的第一个值起点。
const char* JsonValuePos(const std::string& json, const char* key)
{
    if (json.empty() || key == 0) return 0;
    std::string pat = "\"";
    pat += key;
    pat += "\"";

    size_t p = json.find(pat);
    if (p == std::string::npos) return 0;
    p = json.find(':', p + pat.size());
    if (p == std::string::npos) return 0;
    p++;
    while (p < json.size() && (json[p] == ' ' || json[p] == '\t' || json[p] == '\r' || json[p] == '\n')) p++;
    return json.c_str() + p;
}

int JsonInt(const std::string& json, const char* key, int def)
{
    const char* p = JsonValuePos(json, key);
    return p ? std::atoi(p) : def;
}

float JsonFloat(const std::string& json, const char* key, float def)
{
    const char* p = JsonValuePos(json, key);
    return p ? (float)std::atof(p) : def;
}

std::string JsonStr(const std::string& json, const char* key, const char* def)
{
    const char* p = JsonValuePos(json, key);
    if (p == 0) return std::string(def ? def : "");
    if (*p != '"') return std::string(def ? def : "");
    p++;
    std::string out;
    while (*p && *p != '"') out.push_back(*p++);
    return out;
}

// ---------------------- 目录 / 文件 ----------------------

std::string JoinPath(const std::string& dir, const std::string& name)
{
    if (dir.empty()) return name;
    char last = dir[dir.size() - 1];
    if (last == '\\' || last == '/') return dir + name;
    return dir + "\\" + name;
}

bool FileExists(const std::string& path)
{
    std::ifstream f(path.c_str(), std::ios::binary);
    return f.is_open();
}

// ---------------------- OpenVINO 网络 ----------------------

// 加载 IR / ONNX 并编译到设备。只接受单输入、单输出、4D 特征图输出的模型。
bool InitNet(ov::Core& core, const std::string& xmlPath, const std::string& device,
             AnomNet& net, std::string& err)
{
    try
    {
        std::shared_ptr<ov::Model> model = core.read_model(xmlPath);
        if (model->inputs().size() < 1 || model->outputs().size() < 1) {
            err = "模型缺少输入或输出：" + xmlPath;
            return false;
        }

        auto in_shape = model->input().get_shape();
        if (in_shape.size() != 4) {
            err = "模型输入不是 4D（需要 [1,3,H,W]）：" + xmlPath;
            return false;
        }

        net.in_h = (int)in_shape[2];
        net.in_w = (int)in_shape[3];
        if (net.in_w <= 0 || net.in_h <= 0) {
            err = "模型输入尺寸为动态，无法确定分析分辨率：" + xmlPath;
            return false;
        }

        net.compiled = core.compile_model(model, device);
        net.infer = net.compiled.create_infer_request();

        auto out_shape = net.compiled.output(0).get_shape();
        if (out_shape.size() == 4) {
            // 通道优先 [1,C,h,w] 还是通道在后 [1,h,w,C]：按"谁最小"判。
            // 特征图的 C 通常远小于 H/W，这个判据在实践里很稳。
            int d1 = (int)out_shape[1], d2 = (int)out_shape[2], d3 = (int)out_shape[3];
            if (d1 <= d3 && d1 <= 64) { net.out_c = d1; net.out_h = d2; net.out_w = d3; }
            else                      { net.out_c = d3; net.out_h = d2; net.out_w = d1; }
        } else if (out_shape.size() == 3) {
            net.out_c = (int)out_shape[0];
            net.out_h = (int)out_shape[1];
            net.out_w = (int)out_shape[2];
        }

        net.ok = true;
        return true;
    }
    catch (const std::exception& e)
    {
        err = std::string("加载模型失败：") + e.what();
        return false;
    }
}

// 把 ROI 图缩放到网络输入尺寸，转 RGB、/255、NCHW。
void MakeBlob(const cv::Mat& bgr, int inW, int inH, std::vector<float>& blob)
{
    cv::Mat resized;
    if (bgr.cols == inW && bgr.rows == inH) resized = bgr;
    else cv::resize(bgr, resized, cv::Size(inW, inH), 0, 0,
                    (bgr.cols > inW || bgr.rows > inH) ? cv::INTER_AREA : cv::INTER_LINEAR);

    cv::Mat rgb;
    if (resized.channels() == 1) cv::cvtColor(resized, rgb, cv::COLOR_GRAY2RGB);
    else if (resized.channels() == 4) cv::cvtColor(resized, rgb, cv::COLOR_BGRA2RGB);
    else cv::cvtColor(resized, rgb, cv::COLOR_BGR2RGB);

    blob.assign((size_t)3 * inW * inH, 0.0f);
    const int wh = inW * inH;
    const float kInv255 = 1.0f / 255.0f;
    const unsigned char* p = rgb.data;
    for (int i = 0; i < wh; ++i) {
        blob[(size_t)0 * wh + i] = (float)p[i * 3 + 0] * kInv255;
        blob[(size_t)1 * wh + i] = (float)p[i * 3 + 1] * kInv255;
        blob[(size_t)2 * wh + i] = (float)p[i * 3 + 2] * kInv255;
    }
}

// 跑一次前向，把**第 0 个输出**拷进 out（pos-major 的通道优先布局由调用方解释）。
bool ForwardNet(AnomNet& net, const cv::Mat& bgr, std::vector<float>& out, std::string& err)
{
    if (!net.ok) { err = "网络未加载"; return false; }
    try
    {
        std::vector<float> blob;
        MakeBlob(bgr, net.in_w, net.in_h, blob);

        auto input_port = net.compiled.input();
        ov::Tensor input_tensor(input_port.get_element_type(), input_port.get_shape(), blob.data());
        net.infer.set_input_tensor(input_tensor);
        net.infer.infer();

        auto t = net.infer.get_output_tensor(0);
        size_t n = t.get_size();
        const float* d = t.data<float>();
        out.assign(d, d + n);
        return true;
    }
    catch (const std::exception& e)
    {
        err = std::string("推理失败：") + e.what();
        return false;
    }
}

// 用外部张量当输入跑一次（EfficientAD 的 AE 分支：输入是教师输出）。
bool ForwardNetWithTensor(AnomNet& net, const std::vector<float>& in, std::vector<float>& out,
                          std::string& err)
{
    if (!net.ok) { err = "网络未加载"; return false; }
    try
    {
        auto input_port = net.compiled.input();
        auto shape = input_port.get_shape();
        size_t need = 1;
        for (size_t i = 0; i < shape.size(); ++i) need *= (size_t)shape[i];
        if (in.size() != need) {
            err = "自编码器输入维度与教师输出不一致（模型导出约定不符）";
            return false;
        }

        ov::Tensor input_tensor(input_port.get_element_type(), shape, const_cast<float*>(in.data()));
        net.infer.set_input_tensor(input_tensor);
        net.infer.infer();

        auto t = net.infer.get_output_tensor(0);
        const float* d = t.data<float>();
        out.assign(d, d + t.get_size());
        return true;
    }
    catch (const std::exception& e)
    {
        err = std::string("推理失败：") + e.what();
        return false;
    }
}

// ---------------------- 特征提取 ----------------------

// 内置描述子（feat = HANDCRAFT）：不需要任何预训练模型，开箱即用。
// 每个 patch 取 15 维：亮度均值/方差、梯度幅值均值、8 维梯度方向直方图、4 个象限均值。
// 【为什么这样取】划痕 / 脏污 / 缺失在"局部亮度和梯度方向分布"上都有体现；
// 这两类统计对光照整体变化不敏感（直方图归一化 + 方差），而对照度均匀性敏感，
// 恰好符合"工位打光固定、只检测表面异常"的现场条件。
void HandcraftFeature(const cv::Mat& gray, int inputSize, int patch,
                      int& gw, int& gh, std::vector<float>& out)
{
    gw = inputSize / patch;
    gh = gw;
    const int dim = 15;

    cv::Mat g32;
    gray.convertTo(g32, CV_32F);

    cv::Mat gx, gy;
    cv::Sobel(g32, gx, CV_32F, 1, 0, 3);
    cv::Sobel(g32, gy, CV_32F, 0, 1, 3);

    out.assign((size_t)gw * gh * dim, 0.0f);

    const int half = patch / 2;
    for (int cy = 0; cy < gh; ++cy)
    {
        for (int cx = 0; cx < gw; ++cx)
        {
            const int x0 = cx * patch;
            const int y0 = cy * patch;

            double sum = 0, sum2 = 0, gsum = 0;
            double hist[8] = { 0, 0, 0, 0, 0, 0, 0, 0 };
            double quad[4] = { 0, 0, 0, 0 };
            int quadN[4] = { 0, 0, 0, 0 };

            for (int y = 0; y < patch; ++y)
            {
                const float* rowG = g32.ptr<float>(y0 + y);
                const float* rowX = gx.ptr<float>(y0 + y);
                const float* rowY = gy.ptr<float>(y0 + y);
                for (int x = 0; x < patch; ++x)
                {
                    float v = rowG[x0 + x];
                    float ax = rowX[x0 + x];
                    float ay = rowY[x0 + x];
                    double mag = std::sqrt((double)ax * ax + (double)ay * ay);

                    sum += v;
                    sum2 += (double)v * v;
                    gsum += mag;

                    if (mag > 1e-6)
                    {
                        // atan2 ∈ [-π, π] → 8 个方向箱
                        double ang = std::atan2((double)ay, (double)ax);
                        int bin = (int)((ang + 3.14159265358979323846) / 6.28318530717958647692 * 8.0);
                        if (bin < 0) bin = 0;
                        if (bin > 7) bin = 7;
                        hist[bin] += mag;
                    }

                    int qi = (y >= half ? 2 : 0) + (x >= half ? 1 : 0);
                    quad[qi] += v;
                    quadN[qi]++;
                }
            }

            const int n = patch * patch;
            double mean = sum / n;
            double var = sum2 / n - mean * mean;
            if (var < 0) var = 0;
            double stdv = std::sqrt(var);

            double hsum = 0;
            for (int i = 0; i < 8; ++i) hsum += hist[i];
            if (hsum < 1e-9) hsum = 1.0;

            float* dst = &out[((size_t)cy * gw + cx) * dim];
            // 各维都缩到 0~1 量级：协方差矩阵的条件数才不会因为"某维量纲大 255 倍"而爆掉
            dst[0] = (float)(mean / 255.0);
            dst[1] = (float)(stdv / 64.0);
            dst[2] = (float)(gsum / n / 255.0);
            for (int i = 0; i < 8; ++i) dst[3 + i] = (float)(hist[i] / hsum);
            for (int i = 0; i < 4; ++i)
                dst[11 + i] = quadN[i] > 0 ? (float)(quad[i] / quadN[i] / 255.0) : 0.0f;
        }
    }
}

// 均匀取 dim 个通道（PaDiM 的随机通道选择；这里用等距选择，保证同样输入给同样结果）。
std::vector<int> PickDims(int srcDim, int& dim)
{
    if (srcDim <= kMaxDim)
    {
        dim = srcDim;
        std::vector<int> all(srcDim);
        for (int i = 0; i < srcDim; ++i) all[i] = i;
        return all;
    }

    dim = kMaxDim;
    std::vector<int> sel(dim);
    for (int i = 0; i < dim; ++i)
        sel[i] = (int)((long long)i * srcDim / dim);
    return sel;
}

// 把「通道优先的 4D 输出」按 sel 抽成 pos-major 特征。
void GatherFeature(const std::vector<float>& raw, int C, int H, int W,
                   const std::vector<int>& sel, std::vector<float>& out)
{
    const int d = (int)sel.size();
    const int P = H * W;
    out.assign((size_t)P * d, 0.0f);

    for (int i = 0; i < d; ++i)
    {
        int c = sel[i];
        if (c < 0 || c >= C) continue;
        const float* src = &raw[(size_t)c * P];
        for (int p = 0; p < P; ++p) out[(size_t)p * d + i] = src[p];
    }
}

// 从一张图（已裁到 ROI）提取特征图。net 为空时走内置描述子。
bool ExtractFeature(Context* ctx, AnomNet* net, const cv::Mat& roiBgr,
                    int inputSize, int patch,
                    int& gw, int& gh, int& srcDim,
                    const std::vector<int>* sel, std::vector<int>* selOut,
                    std::vector<float>& feat, std::string& err)
{
    if (net != 0 && net->ok)
    {
        std::vector<float> raw;
        if (!ForwardNet(*net, roiBgr, raw, err)) return false;

        int C = net->out_c, H = net->out_h, W = net->out_w;
        if (C <= 0 || H <= 0 || W <= 0) {
            err = "特征提取器输出不是 4D 特征图 [1,C,h,w]";
            return false;
        }
        if ((size_t)C * H * W != raw.size()) {
            err = "特征提取器输出维度与形状不一致";
            return false;
        }

        gw = W;
        gh = H;
        srcDim = C;

        if (sel != 0)
        {
            GatherFeature(raw, C, H, W, *sel, feat);
        }
        else
        {
            int dim = 0;
            std::vector<int> picked = PickDims(C, dim);
            if (selOut) *selOut = picked;
            GatherFeature(raw, C, H, W, picked, feat);
        }
        return true;
    }

    // 内置描述子：先灰度化并统一到分析分辨率
    cv::Mat gray;
    if (roiBgr.channels() == 1) gray = roiBgr;
    else if (roiBgr.channels() == 4) cv::cvtColor(roiBgr, gray, cv::COLOR_BGRA2GRAY);
    else cv::cvtColor(roiBgr, gray, cv::COLOR_BGR2GRAY);

    cv::Mat sized;
    if (gray.cols == inputSize && gray.rows == inputSize) sized = gray;
    else cv::resize(gray, sized, cv::Size(inputSize, inputSize), 0, 0,
                    (gray.cols > inputSize || gray.rows > inputSize) ? cv::INTER_AREA : cv::INTER_LINEAR);

    HandcraftFeature(sized, inputSize, patch, gw, gh, feat);
    srcDim = 15;

    // 内置描述子永远先算满 15 维，再按 sel 抽成紧凑的 d 维 ——
    // 模型里存的 d 可能小于 15（训练收尾会按样本数收缩维数），
    // 这里漏掉抽取就会让 feat 的步长和 mean/chol 的步长不一致，读到的是错位数据。
    if (sel != 0 && (int)sel->size() != 15)
    {
        const int P = gw * gh, d = (int)sel->size();
        std::vector<float> full;
        full.swap(feat);

        feat.assign((size_t)P * d, 0.0f);
        for (int i = 0; i < d; ++i)
        {
            const int c = (*sel)[(size_t)i];
            if (c < 0 || c >= 15) continue;
            for (int p = 0; p < P; ++p)
                feat[(size_t)p * d + i] = full[(size_t)p * 15 + c];
        }
    }

    if (selOut)
    {
        selOut->resize(15);
        for (int i = 0; i < 15; ++i) (*selOut)[i] = i;
    }
    (void)ctx;
    return true;
}

// ---------------------- 线性代数 ----------------------

// 原地 Cholesky（下三角）。失败（非正定）返回 false，由调用方加 jitter 重试。
bool Cholesky(float* A, int d)
{
    for (int i = 0; i < d; ++i)
    {
        for (int j = 0; j <= i; ++j)
        {
            double s = (double)A[(size_t)i * d + j];
            for (int k = 0; k < j; ++k)
                s -= (double)A[(size_t)i * d + k] * (double)A[(size_t)j * d + k];

            if (i == j)
            {
                if (s <= 0.0) return false;
                A[(size_t)i * d + j] = (float)std::sqrt(s);
            }
            else
            {
                double diag = (double)A[(size_t)j * d + j];
                if (diag <= 0.0) return false;
                A[(size_t)i * d + j] = (float)(s / diag);
            }
        }
        // 上三角清零，便于落盘后仍能直接按 d*d 解释
        for (int j = i + 1; j < d; ++j) A[(size_t)i * d + j] = 0.0f;
    }
    return true;
}

// 解 L z = diff，返回 sqrt(||z||²/d)（= 马氏距离 / sqrt(d)，与 d 无关，便于跨模型比阈值）
float CholScore(const float* L, const float* diff, int d)
{
    double acc = 0.0;
    // 逐元素前代，z 直接就地算在栈上的小数组里（d ≤ 48）
    float z[64];
    for (int i = 0; i < d; ++i)
    {
        double s = (double)diff[i];
        const float* row = L + (size_t)i * d;
        for (int k = 0; k < i; ++k) s -= (double)row[k] * (double)z[k];
        float diag = row[i];
        if (diag <= 1e-12f) diag = 1e-12f;
        z[i] = (float)(s / diag);
        acc += (double)z[i] * z[i];
    }
    return (float)std::sqrt(acc / std::max(1, d));
}

// ---------------------- 图像 ----------------------

// 把 VA_Image 裁到 ROI（越界自动收边），返回 BGR 或灰度（原通道数）。
bool CropRoi(Context* ctx, const VA_Image* img, const int* roi, cv::Mat& out, std::string& err,
             int* outX, int* outY)
{
    if (img == 0) { err = "输入图像为空"; return false; }

    cv::Mat src;
    if (!ToMat(img, src, err)) return false;

    int rx = 0, ry = 0, rw = src.cols, rh = src.rows;
    if (roi != 0 && roi[2] > 0 && roi[3] > 0)
    {
        rx = std::max(0, roi[0]);
        ry = std::max(0, roi[1]);
        rw = std::min(roi[2], src.cols - rx);
        rh = std::min(roi[3], src.rows - ry);
        if (rw <= 0 || rh <= 0) {
            SetErrorEx(ctx, VA_ERR_ROI, "异常检测", "ROI",
                       "", "ROI 必须与图像有交集");
            err = "ROI 与图像无交集";
            return false;
        }
    }

    out = src(cv::Rect(rx, ry, rw, rh)).clone();
    if (outX) *outX = rx;
    if (outY) *outY = ry;
    return true;
}

} // namespace

// =====================================================================
//  PaDiM 训练
// =====================================================================

int AnomTrainBegin(Context* ctx, const char* out_dir, const char* extractor,
                   int feat_kind, int input_size)
{
    if (out_dir == 0 || out_dir[0] == '\0') {
        SetError(ctx, "out_dir empty");
        return VA_ERR_ARG;
    }
    if (input_size < 64 || input_size > 1024) {
        char actual[32];
        std::snprintf(actual, sizeof(actual), "%d", input_size);
        SetErrorEx(ctx, VA_ERR_PARAM_RANGE, "异常检测/训练", "input_size", actual, "64~1024");
        return VA_ERR_PARAM_RANGE;
    }
    if (feat_kind != VA_ANOM_FEAT_HANDCRAFT && feat_kind != VA_ANOM_FEAT_OPENVINO) {
        SetError(ctx, "unknown feature kind");
        return VA_ERR_ARG;
    }

    AnomalyTrainState& st = ctx->anomTrain;
    st.Reset();

    st.feat = feat_kind;
    st.input_size = input_size;
    st.patch = kPatch;
    st.out_dir = out_dir;
    st.extractor = (extractor != 0) ? extractor : "";

    if (feat_kind == VA_ANOM_FEAT_OPENVINO)
    {
        if (st.extractor.empty() || !FileExists(st.extractor)) {
            SetError(ctx, "特征提取器模型不存在（feat=OpenVINO 时必须给 .xml 路径）");
            return VA_ERR_ARG;
        }

        try
        {
            if (!ctx->anomCoreReady) { ApplyThreadBudget(ctx->anomCore); ctx->anomCoreReady = true; }
            std::string err;
            if (!InitNet(ctx->anomCore, st.extractor, "CPU", st.net, err)) {
                SetError(ctx, err.c_str());
                return VA_ERR_MODEL_INFER;
            }
        }
        catch (const std::exception& e) { SetError(ctx, e); return VA_ERR_INTERNAL; }
    }

    st.active = true;
    return VA_OK;
}

int AnomTrainAdd(Context* ctx, const VA_Image* img, const int* roi)
{
    AnomalyTrainState& st = ctx->anomTrain;
    if (!st.active) { SetError(ctx, "训练会话未开始"); return VA_ERR_ARG; }

    cv::Mat patch;
    std::string err;
    int rx = 0, ry = 0;
    if (!CropRoi(ctx, img, roi, patch, err, &rx, &ry)) {
        if (ctx->lastError.empty()) SetError(ctx, err.c_str());
        return VA_ERR_ARG;
    }

    int gw = 0, gh = 0, srcDim = 0;
    std::vector<float> feat;
    if (!ExtractFeature(ctx, st.feat == VA_ANOM_FEAT_OPENVINO ? &st.net : 0,
                        patch, st.input_size, st.patch,
                        gw, gh, srcDim, 0, &st.sel, feat, err)) {
        SetError(ctx, err.c_str());
        return VA_ERR_MODEL_INFER;
    }

    const int d = (int)st.sel.size();
    if (d <= 0 || d > kMaxDim) { SetError(ctx, "特征维数异常"); return VA_ERR_INTERNAL; }

    const int P = gw * gh;
    if ((int)feat.size() != P * d) { SetError(ctx, "特征图尺寸与维数不一致"); return VA_ERR_INTERNAL; }

    // 第一张样本决定网格与维度，之后必须一致（换分辨率 / 换提取器要重新 begin）
    if (st.n == 0)
    {
        st.gw = gw; st.gh = gh; st.dim = d; st.src_dim = srcDim;
        st.sum.assign((size_t)P * d, 0.0);
        st.outer.assign((size_t)P * d * d, 0.0);
        st.samples.clear();
        st.retain = true;
        st.retained = 0;
    }
    else if (gw != st.gw || gh != st.gh || d != st.dim)
    {
        SetErrorEx(ctx, VA_ERR_SIZE, "异常检测/训练", "样本尺寸",
                   "", "所有样本必须同分辨率、同 ROI 形状");
        return VA_ERR_SIZE;
    }

    // 累加 mean 与二阶矩（一次循环同时完成）
    for (int p = 0; p < P; ++p)
    {
        const float* x = &feat[(size_t)p * d];
        double* s = &st.sum[(size_t)p * d];
        for (int i = 0; i < d; ++i) s[i] += (double)x[i];

        double* o = &st.outer[(size_t)p * d * d];
        for (int i = 0; i < d; ++i)
        {
            double xi = (double)x[i];
            double* row = o + (size_t)i * d;
            for (int j = 0; j < d; ++j) row[j] += xi * (double)x[j];
        }
    }

    if (st.retain)
    {
        long long add = (long long)feat.size();
        if (st.retained + add > kRetainBudget || st.n >= 200)
        {
            // 超出预算：释放已保留样本，阈值改用卡方近似（见 finish）
            st.retain = false;
            st.samples.clear();
        }
        else
        {
            st.samples.insert(st.samples.end(), feat.begin(), feat.end());
            st.retained += add;
        }
    }

    st.n++;
    return VA_OK;
}

int AnomTrainFinish(Context* ctx, char* out_summary, int cap)
{
    AnomalyTrainState& st = ctx->anomTrain;
    if (!st.active) { SetError(ctx, "训练会话未开始"); return VA_ERR_ARG; }
    if (st.n < 2) {
        SetError(ctx, "至少需要 2 张 OK 样本（建议 20 张以上）");
        return VA_ERR_ARG;
    }

    const int P = st.gw * st.gh, dFull = st.dim;
    const double n = (double)st.n;

    // ---- 协方差模式：样本少时改估对角，把维度从 n/3 放大到 n ----
    // 【为什么必须分两档】完整协方差每个位置要估 d×d 个参数，n 只比 d 多一点时
    // 样本协方差在新样本的偏离方向上被系统性低估，所以 d 被迫压到 n/3 ——
    // 现场 4 张 OK 只有 2 维、20 张只有 6 维，判别力就是这么没的。
    // 而"每维只估自己的方差"（对角）只需要 n≥2，n=20 时能直接用满 15 维内置描述子。
    // 代价是忽略维间相关：样本充足（n≥kDiagSwitchSamples）时仍走完整协方差拿精度。
    const bool diagMode = (st.n < kDiagSwitchSamples);

    // ---- 有效维数 ----
    // 维数是在 begin 时按 kMaxDim 定的（那时还不知道最终会有几张样本），
    // 因此在这里按实际样本数再压一次；压掉的维用等距抽样的方式保留，保证同样输入给同样结果。
    int d = dFull;
    {
        int cap = diagMode ? (int)n : (int)(n / kSamplesPerDim);
        if (cap < kMinDim) cap = kMinDim;
        if (cap < d) d = cap;
    }

    // keep[i] = 保留的第 i 维在原始 dFull 维里的下标
    std::vector<int> keep((size_t)d);
    for (int i = 0; i < d; ++i)
        keep[i] = (d >= dFull) ? i : (int)((long long)i * dFull / d);

    try
    {
        // ---------- 拟合器：由「累加的 sum / outer」产出紧凑 d 维的 mean + Cholesky ----------
        // 抽成一处是因为要跑两遍：① 全量样本 → 最终模型；② k 折留出，每折用剩下的样本拟合。
        auto fit = [&](const double* sumArr, const double* outerArr, double cnt,
                       std::vector<float>& outMean, std::vector<float>& outChol) -> int
        {
            int fail = 0;
            outMean.assign((size_t)P * d, 0.0f);
            outChol.assign((size_t)P * d * d, 0.0f);

            std::vector<float> cov((size_t)d * d, 0.0f);
            for (int p = 0; p < P; ++p)
            {
                const double* s = sumArr + (size_t)p * dFull;
                const double* o = outerArr + (size_t)p * dFull * dFull;

                float* mp = &outMean[(size_t)p * d];
                for (int i = 0; i < d; ++i) mp[i] = (float)(s[keep[i]] / cnt);

                // 样本协方差：E[xx'] - μμ'（只在保留维上取子阵）
                for (int i = 0; i < d; ++i)
                {
                    for (int j = 0; j < d; ++j)
                    {
                        cov[(size_t)i * d + j] =
                            (float)(o[(size_t)keep[i] * dFull + keep[j]] / cnt
                                    - (double)mp[i] * (double)mp[j]);
                    }
                }

                // 收缩：diag += (kShrink * 平均对角 + 1e-6)，非正定时再放大 10 倍重试
                double tr = 0.0;
                for (int i = 0; i < d; ++i) tr += (double)cov[(size_t)i * d + i];
                double lambda = kShrink * (tr / std::max(1, d)) + 1e-6;

                if (diagMode)
                {
                    // 对角模式：只把 sqrt(方差) 写到对角线，其余保持 0。
                    // 这样 CholScore 的前代公式退化成 Σ(diff_i² / var_i)，即对角马氏距离 ——
                    // 打分、留出校准、推理三条路径一行都不用改，
                    // 本质上只是"协方差矩阵恰好是个对角阵"。
                    float* Lp = &outChol[(size_t)p * d * d];
                    for (int i = 0; i < d; ++i)
                    {
                        double var = (double)cov[(size_t)i * d + i] + lambda;
                        if (var < 1e-6) var = 1e-6;
                        Lp[(size_t)i * d + i] = (float)std::sqrt(var);
                    }
                    continue;
                }

                bool ok = false;
                for (int attempt = 0; attempt < 4 && !ok; ++attempt)
                {
                    for (int i = 0; i < d; ++i) cov[(size_t)i * d + i] += (float)lambda;
                    std::vector<float> L(cov);
                    ok = Cholesky(&L[0], d);
                    if (ok) std::memcpy(&outChol[(size_t)p * d * d], &L[0], sizeof(float) * d * d);
                    else lambda *= 10.0;
                }
                if (!ok) fail++;
            }

            if (fail > 0)
            {
                // 个别位置失败（该位置在训练集里几乎恒定）→ 用单位阵兜底，不影响整体
                for (int p = 0; p < P; ++p)
                {
                    bool allZero = true;
                    for (int i = 0; i < d && allZero; ++i)
                        if (outChol[(size_t)p * d * d + (size_t)i * d + i] != 0.0f) allZero = false;
                    if (!allZero) continue;
                    for (int i = 0; i < d; ++i) outChol[(size_t)p * d * d + (size_t)i * d + i] = 1.0f;
                }
            }
            return fail;
        };

        // 一张图在给定 mean/chol 下的最大分（阈值校准与自评都用它）
        auto imageMax = [&](const float* f, const std::vector<float>& m,
                            const std::vector<float>& c) -> float
        {
            float mx = 0.0f;
            for (int p = 0; p < P; ++p)
            {
                const float* fp = f + (size_t)p * dFull;
                const float* mu = &m[(size_t)p * d];
                float diff[64];
                for (int i = 0; i < d; ++i) diff[i] = fp[keep[i]] - mu[i];
                float v = CholScore(&c[(size_t)p * d * d], diff, d);
                if (v > mx) mx = v;
            }
            return mx;
        };

        std::vector<float> mean, chol;
        const int cholFail = fit(&st.sum[0], &st.outer[0], n, mean, chol);

        // ---- 阈值：k 折留出校准 ----
        // 用"训练集自评"的分位数定阈值必然偏乐观：每张图都是自己均值/协方差的一份子，
        // 现场表现就是「训练样本全过、新的 OK 图整幅判 NG」（实测自评 1.6、留出 3~5）。
        // 因此把样本切成 K 折，轮流留一折出来打分，用这些"没见过"的分数定阈值；
        // 最终模型仍用全部样本拟合（留出只影响阈值，不影响权重）。
        float threshold = 1.0f;
        float selfMax = 0.0f, heldMax = 0.0f;

        if (st.retain && st.n >= 2)
        {
            for (long long k = 0; k < st.n; ++k)
                selfMax = std::max(selfMax, imageMax(&st.samples[(size_t)k * P * dFull], mean, chol));

            const int K = (int)std::min<long long>(4, st.n);
            std::vector<double> subSum((size_t)P * dFull, 0.0);
            std::vector<double> subOuter((size_t)P * dFull * dFull, 0.0);
            std::vector<float> heldScores;   // 每张样本在"没见过它"的模型下的最大分

            for (int f = 0; f < K; ++f)
            {
                std::fill(subSum.begin(), subSum.end(), 0.0);
                std::fill(subOuter.begin(), subOuter.end(), 0.0);

                int cnt = 0;
                for (long long k = 0; k < st.n; ++k)
                {
                    if ((int)(k % K) == f) continue;   // 本折留出，不参与拟合
                    const float* fp = &st.samples[(size_t)k * P * dFull];
                    for (int p = 0; p < P; ++p)
                    {
                        const float* x = fp + (size_t)p * dFull;
                        double* s = &subSum[(size_t)p * dFull];
                        for (int i = 0; i < dFull; ++i) s[i] += (double)x[i];

                        double* o = &subOuter[(size_t)p * dFull * dFull];
                        for (int i = 0; i < dFull; ++i)
                        {
                            const double xi = (double)x[i];
                            double* row = o + (size_t)i * dFull;
                            for (int j = 0; j < dFull; ++j) row[j] += xi * (double)x[j];
                        }
                    }
                    cnt++;
                }

                if (cnt < 2) continue;

                std::vector<float> mF, cF;
                fit(&subSum[0], &subOuter[0], (double)cnt, mF, cF);
                for (long long k = f; k < st.n; k += K)
                {
                    float hv = imageMax(&st.samples[(size_t)k * P * dFull], mF, cF);
                    heldScores.push_back(hv);
                    heldMax = std::max(heldMax, hv);
                }
            }

            // 【为什么用 P90 而不是 max】留出分里的最大值是个极值统计量：训练集里只要
            // 混进一两张"其实不是 OK"的样本（标错、或者姿态/光照偏离群体），它在留出时
            // 会被正确地判成异常，于是 heldMax 被拉高一个数量级，阈值随之上天，
            // 现场表现就是"所有图都超不过阈值 → 全部 OK、命中率 0"。
            // 实测一份 6 样本的现场数据集：heldMax=41.4，旧逻辑阈值=41.4x1.5=62.05，
            // 而真实检测分数只有 53~68 —— 正好卡在阈值两侧，于是"不是全 OK 就是全 NG"。
            // 取 P90 并夹逼到 [selfMax*1.3, selfMax*6]，一两张离群样本就不至于毁掉阈值。
            if (!heldScores.empty())
            {
                std::vector<float> sortedScores(heldScores);
                std::sort(sortedScores.begin(), sortedScores.end());
                size_t idx = (size_t)((double)(sortedScores.size() - 1) * 0.90 + 0.5);
                if (idx >= sortedScores.size()) idx = sortedScores.size() - 1;
                const float heldP90 = sortedScores[idx];

                threshold = heldP90 * kThrHeldMargin;

                if (selfMax > 0.0f)
                {
                    if (threshold < selfMax * 1.30f) threshold = selfMax * 1.30f;
                    if (threshold > selfMax * 6.00f) threshold = selfMax * 6.00f;
                }
            }
            else if (selfMax > 0.0f)
            {
                threshold = selfMax * 2.0f;   // 留出失败（样本全退化）时退一步给个大余量
            }
        }
        else
        {
            // 没保留样本（样张太多 / 维太高）：卡方近似 score ≈ sqrt(χ²(d)/d)，均值≈1、σ≈sqrt(2/d)
            double sigma = std::sqrt(2.0 / std::max(1, d));
            threshold = (float)(1.0 + 6.0 * sigma);
        }

        if (threshold < 1.0f) threshold = 1.0f;

        // ---- 落盘 ----
        // model.json 是"人和程序都能读"的那份；padim.bin 是真正的权重。
        // 分开的理由：界面只读 json 就能列出模型信息，不必加载几十 MB 的 bin。
        std::ostringstream js;
        js << "{\n";
        js << "  \"kind\": " << VA_ANOM_KIND_PADIM << ",\n";
        js << "  \"feat\": " << st.feat << ",\n";
        js << "  \"input\": " << st.input_size << ",\n";
        js << "  \"patch\": " << st.patch << ",\n";
        js << "  \"grid_w\": " << st.gw << ",\n";
        js << "  \"grid_h\": " << st.gh << ",\n";
        js << "  \"dim\": " << d << ",\n";
        js << "  \"src_dim\": " << st.src_dim << ",\n";
        js << "  \"samples\": " << st.n << ",\n";
        js << "  \"threshold\": " << threshold << ",\n";
        // 0 = 完整协方差（d = n/3），1 = 对角协方差（d = n，样本少时用）。
        // 落盘只为诊断与界面展示：padim.bin 里对角模式就是"只有对角线非零的 chol"，
        // 加载与打分的代码路径完全一致，不需要分支。
        js << "  \"cov\": " << (diagMode ? 1 : 0) << ",\n";
        js << "  \"extractor\": \""
           << (st.feat == VA_ANOM_FEAT_OPENVINO ? "extractor.xml" : "") << "\",\n";
        js << "  \"algo\": \"PaDiM\"\n";
        js << "}\n";

        std::string jsonPath = JoinPath(st.out_dir, "model.json");
        {
            std::ofstream f(jsonPath.c_str(), std::ios::binary);
            if (!f.is_open()) { SetError(ctx, ("无法写入 " + jsonPath).c_str()); return VA_ERR_INTERNAL; }
            std::string body = js.str();
            f.write(body.data(), (std::streamsize)body.size());
        }

        std::string binPath = JoinPath(st.out_dir, "padim.bin");
        {
            std::ofstream f(binPath.c_str(), std::ios::binary);
            if (!f.is_open()) { SetError(ctx, ("无法写入 " + binPath).c_str()); return VA_ERR_INTERNAL; }

            const char magic[8] = { 'V','A','P','A','D','I','M','1' };
            f.write(magic, 8);

            int hdr[6] = { d, st.gw, st.gh, st.src_dim, st.patch, (int)st.n };
            f.write((const char*)hdr, sizeof(hdr));
            f.write((const char*)&threshold, sizeof(threshold));

            int selN = d;
            f.write((const char*)&selN, sizeof(selN));
            // 写"保留维对原始通道的下标"，推理时按同一套下标抽通道，才能和训练时对齐
            if (!st.sel.empty())
            {
                std::vector<int> selOut((size_t)d);
                for (int i = 0; i < d; ++i) selOut[(size_t)i] = st.sel[(size_t)keep[i]];
                f.write((const char*)&selOut[0], sizeof(int) * (size_t)d);
            }

            f.write((const char*)&mean[0], sizeof(float) * mean.size());
            f.write((const char*)&chol[0], sizeof(float) * chol.size());
        }

        std::string summary;
        {
            char head[256];
            std::snprintf(head, sizeof(head),
                          "PaDiM 训练完成：%d 张 · %dx%d 网格 · d=%d/%d%s · 阈值 %.3f",
                          (int)st.n, st.gw, st.gh, d, dFull,
                          diagMode ? "（对角协方差）" : "", threshold);
            summary = head;
        }
        if (heldMax > 0.0f)
        {
            char tail[192];
            std::snprintf(tail, sizeof(tail),
                          "（留出最高 %.2f · 自评 %.2f）", heldMax, selfMax);
            summary += tail;
        }
        if (cholFail > 0)
            summary += " · " + std::to_string(cholFail) + " 个位置退化已兜底";

        // 两条现场最常踩的坑，直接写在训练结果里 —— 别让人对着"命中率 0"去猜。
        if (selfMax > 1e-6f && heldMax > selfMax * 4.0f)
        {
            char w[224];
            std::snprintf(w, sizeof(w),
                          " · [警告] 留出分是自评的 %.1f 倍：训练用的 OK 样本里可能混了缺陷品"
                          "（或姿态/光照差异过大），请改标注后重训", heldMax / selfMax);
            summary += w;
        }
        if (d < 8)
        {
            char w[192];
            std::snprintf(w, sizeof(w),
                          " · [提示] 只有 %d 张样本，特征被压到 %d 维，判别力有限"
                          "（建议 ≥ 15 张 OK）", (int)st.n, d);
            summary += w;
        }

        if (out_summary != 0 && cap > 0)
        {
            int n2 = (int)summary.size();
            if (n2 > cap - 1) n2 = cap - 1;
            std::memcpy(out_summary, summary.data(), (size_t)n2);
            out_summary[n2] = '\0';
        }

        st.Reset();
        return VA_OK;
    }
    catch (const std::exception& e)
    {
        st.Reset();
        SetError(ctx, e);
        return VA_ERR_INTERNAL;
    }
}

int AnomTrainCount(Context* ctx, int* out_count)
{
    if (out_count == 0) { SetError(ctx, "null argument"); return VA_ERR_ARG; }
    *out_count = ctx->anomTrain.active ? (int)ctx->anomTrain.n : 0;
    return VA_OK;
}

int AnomTrainCancel(Context* ctx)
{
    ctx->anomTrain.Reset();
    return VA_OK;
}

// =====================================================================
//  加载 / 推理
// =====================================================================

int AnomCreate(Context* ctx, const char* model_dir, const char* device)
{
    if (model_dir == 0 || model_dir[0] == '\0') {
        SetError(ctx, "model_dir empty");
        return VA_ERR_ARG;
    }

    const char* dev = (device != 0 && device[0] != '\0') ? device : "CPU";
    if (_stricmp(dev, "CUDA") == 0) {
        SetError(ctx, "CUDA backend not implemented yet (reserved); use CPU/GPU/NPU for now");
        return VA_ERR_ARG;
    }

    std::string dir = model_dir;
    std::string jsonPath = JoinPath(dir, "model.json");
    std::string json = ReadAllText(jsonPath);
    if (json.empty()) {
        SetError(ctx, ("模型目录缺少 model.json：" + dir).c_str());
        return VA_ERR_ARG;
    }

    ctx->anom.Reset();

    try
    {
        if (!ctx->anomCoreReady) { ApplyThreadBudget(ctx->anomCore); ctx->anomCoreReady = true; }

        AnomalyState& a = ctx->anom;
        a.dir = dir;
        a.device = dev;
        a.kind = JsonInt(json, "kind", VA_ANOM_KIND_PADIM);
        a.feat = JsonInt(json, "feat", VA_ANOM_FEAT_HANDCRAFT);
        a.input_size = JsonInt(json, "input", 256);
        a.patch = JsonInt(json, "patch", kPatch);
        a.gw = JsonInt(json, "grid_w", 0);
        a.gh = JsonInt(json, "grid_h", 0);
        a.dim = JsonInt(json, "dim", 0);
        a.src_dim = JsonInt(json, "src_dim", 0);
        a.samples = JsonInt(json, "samples", 0);
        a.threshold = JsonFloat(json, "threshold", 0.0f);

        if (a.kind == VA_ANOM_KIND_PADIM)
        {
            // ---- PaDiM：读 padim.bin + 可选特征提取器 ----
            std::string binPath = JoinPath(dir, "padim.bin");
            std::ifstream f(binPath.c_str(), std::ios::binary);
            if (!f.is_open()) {
                SetError(ctx, ("缺少 padim.bin：" + binPath).c_str());
                return VA_ERR_ARG;
            }

            char magic[8] = { 0 };
            f.read(magic, 8);
            if (std::memcmp(magic, "VAPADIM1", 8) != 0) {
                SetError(ctx, ("padim.bin 格式不符（magic 不匹配）：" + binPath).c_str());
                return VA_ERR_ARG;
            }

            int hdr[6] = { 0 };
            f.read((char*)hdr, sizeof(hdr));
            int d = hdr[0];
            a.gw = hdr[1];
            a.gh = hdr[2];
            a.src_dim = hdr[3];
            a.patch = hdr[4];
            a.samples = hdr[5];
            f.read((char*)&a.threshold, sizeof(a.threshold));

            int selN = 0;
            f.read((char*)&selN, sizeof(selN));
            if (selN <= 0 || selN > kMaxDim || d != selN) {
                SetError(ctx, "padim.bin 维度字段非法");
                return VA_ERR_ARG;
            }
            a.sel.resize((size_t)selN);
            f.read((char*)&a.sel[0], sizeof(int) * (size_t)selN);

            const int P = a.gw * a.gh;
            if (P <= 0) { SetError(ctx, "padim.bin 网格尺寸非法"); return VA_ERR_ARG; }

            a.dim = d;
            a.mean.assign((size_t)P * d, 0.0f);
            a.chol.assign((size_t)P * d * d, 0.0f);
            f.read((char*)&a.mean[0], sizeof(float) * a.mean.size());
            f.read((char*)&a.chol[0], sizeof(float) * a.chol.size());
            if (!f) { SetError(ctx, "padim.bin 内容不完整（文件被截断？）"); return VA_ERR_ARG; }

            if (a.feat == VA_ANOM_FEAT_OPENVINO)
            {
                std::string ex = JsonStr(json, "extractor", "extractor.xml");
                std::string exPath = JoinPath(dir, ex);
                if (!FileExists(exPath)) {
                    SetError(ctx, ("特征提取器不存在：" + exPath).c_str());
                    return VA_ERR_ARG;
                }
                std::string err;
                if (!InitNet(ctx->anomCore, exPath, dev, a.extractor, err)) {
                    SetError(ctx, err.c_str());
                    return VA_ERR_MODEL_INFER;
                }
                if (a.extractor.out_c != a.src_dim) {
                    SetError(ctx, "特征提取器的输出通道数与训练时不一致（换过模型？）");
                    return VA_ERR_SIZE;
                }
            }
        }
        else if (a.kind == VA_ANOM_KIND_EFFICIENTAD)
        {
            // ---- EfficientAD：教师 / 学生 / 可选 AE + 归一化统计 ----
            struct { const char* key; AnomNet* net; const char* def; bool required; } parts[] = {
                { "teacher", &a.teacher, "teacher.xml", true  },
                { "student", &a.student, "student.xml", true  },
                { "ae",      &a.ae,      "ae.xml",      false }
            };

            for (int i = 0; i < 3; ++i)
            {
                std::string name = JsonStr(json, parts[i].key, parts[i].def);
                if (name.empty()) continue;

                std::string path = JoinPath(dir, name);
                if (!FileExists(path)) {
                    if (parts[i].required) {
                        SetError(ctx, (std::string("缺少必需模型：") + path).c_str());
                        return VA_ERR_ARG;
                    }
                    continue;
                }

                std::string err;
                if (!InitNet(ctx->anomCore, path, dev, *parts[i].net, err)) {
                    SetError(ctx, err.c_str());
                    return VA_ERR_MODEL_INFER;
                }
            }

            if (!a.teacher.ok || !a.student.ok) {
                SetError(ctx, "EfficientAD 需要教师与学生两个模型");
                return VA_ERR_ARG;
            }
            if (a.teacher.out_c != a.student.out_c || a.teacher.out_h != a.student.out_h
                || a.teacher.out_w != a.student.out_w) {
                SetError(ctx, "教师与学生的输出特征图形状不一致（导出时应过同一个输入尺寸）");
                return VA_ERR_SIZE;
            }

            a.gw = a.teacher.out_w;
            a.gh = a.teacher.out_h;
            a.input_size = a.teacher.in_w;
            a.patch = 1;

            // 归一化统计：训练集上的均值 / 标准差，缺文件时按 0/1 处理（阈值需相应放大）
            a.st_mean = 0.0f; a.st_std = 1.0f; a.ae_mean = 0.0f; a.ae_std = 1.0f;
            std::string normPath = JoinPath(dir, "ead_norm.bin");
            std::ifstream nf(normPath.c_str(), std::ios::binary);
            if (nf.is_open()) {
                float v[4] = { 0, 1, 0, 1 };
                nf.read((char*)v, sizeof(v));
                a.st_mean = v[0]; a.st_std = (v[1] > 1e-6f) ? v[1] : 1.0f;
                a.ae_mean = v[2]; a.ae_std = (v[3] > 1e-6f) ? v[3] : 1.0f;
            }
            if (a.threshold <= 0.0f) a.threshold = kEadDefaultThreshold;
        }
        else
        {
            SetError(ctx, "model.json 的 kind 未知（0=PaDiM，1=EfficientAD）");
            return VA_ERR_ARG;
        }

        if (a.threshold <= 0.0f) a.threshold = 1.0f;
        a.created = true;
        return VA_OK;
    }
    catch (const std::exception& e)
    {
        ctx->anom.Reset();
        SetError(ctx, e);
        return VA_ERR_INTERNAL;
    }
}

// 把 gw×gh 的分数图放大到 ROI 尺寸并做轻度平滑。
static void UpscaleMap(const cv::Mat& small, cv::Mat& out, int w, int h)
{
    cv::Mat resized;
    cv::resize(small, resized, cv::Size(w, h), 0, 0,
               (small.cols < w) ? cv::INTER_LINEAR : cv::INTER_AREA);

    // 3×3 平滑：逐位置分数之间本来就有量化台阶，放大会出现方格；
    // 但核不能大 —— 小划痕只有几像素，糊过头会把它们抹掉。
    cv::GaussianBlur(resized, out, cv::Size(3, 3), 0);
}

int AnomRun(Context* ctx, const VA_Image* img, const int* roi,
            float threshold, int min_area_px, VA_AnomStat* out_stat,
            float* out_map, int map_cap, int* out_w, int* out_h,
            unsigned char* out_mask, int mask_cap)
{
    if (out_stat == 0) { SetError(ctx, "null argument"); return VA_ERR_ARG; }
    std::memset(out_stat, 0, sizeof(VA_AnomStat));
    if (out_w) *out_w = 0;
    if (out_h) *out_h = 0;

    AnomalyState& a = ctx->anom;
    if (!a.created) { SetError(ctx, "模型未加载（先 va_anom_create）"); return VA_ERR_MODEL_NOT_LOADED; }

    cv::Mat patch;
    std::string err;
    int rx = 0, ry = 0;
    if (!CropRoi(ctx, img, roi, patch, err, &rx, &ry)) {
        if (ctx->lastError.empty()) SetError(ctx, err.c_str());
        return VA_ERR_ARG;
    }

    const int rw = patch.cols, rh = patch.rows;
    if (rw < 8 || rh < 8) {
        char actual[64];
        std::snprintf(actual, sizeof(actual), "%dx%d", rw, rh);
        SetErrorEx(ctx, VA_ERR_IMAGE_TOO_SMALL, "异常检测", "ROI", actual, ">= 8x8");
        return VA_ERR_IMAGE_TOO_SMALL;
    }

    const float thr = (threshold > 0.0f) ? threshold : a.threshold;
    if (thr <= 0.0f) { SetError(ctx, "阈值非法"); return VA_ERR_PARAM_RANGE; }

    try
    {
        cv::Mat score;   // CV_32F，大小 = 特征图网格
        cv::Mat grayPatch = patch;
        if (patch.channels() == 4) cv::cvtColor(patch, grayPatch, cv::COLOR_BGRA2BGR);

        if (a.kind == VA_ANOM_KIND_PADIM)
        {
            int gw = 0, gh = 0, srcDim = 0;
            std::vector<float> feat;
            if (!ExtractFeature(ctx, a.feat == VA_ANOM_FEAT_OPENVINO ? &a.extractor : 0,
                                grayPatch, a.input_size, a.patch,
                                gw, gh, srcDim, &a.sel, 0, feat, err)) {
                SetError(ctx, err.c_str());
                return VA_ERR_MODEL_INFER;
            }

            if (gw != a.gw || gh != a.gh) {
                SetErrorEx(ctx, VA_ERR_SIZE, "异常检测", "特征图尺寸",
                           (std::to_string(gw) + "x" + std::to_string(gh)).c_str(),
                           (std::to_string(a.gw) + "x" + std::to_string(a.gh)).c_str());
                return VA_ERR_SIZE;
            }

            const int P = a.gw * a.gh, d = a.dim;
            score.create(a.gh, a.gw, CV_32F);

            float diff[64];
            for (int p = 0; p < P; ++p)
            {
                const float* x = &feat[(size_t)p * d];
                const float* mu = &a.mean[(size_t)p * d];
                for (int i = 0; i < d; ++i) diff[i] = x[i] - mu[i];
                score.at<float>(p / a.gw, p % a.gw) =
                    CholScore(&a.chol[(size_t)p * d * d], diff, d);
            }

            // 诊断（VA_ANOM_DEBUG=1）：最大分位置的逐维贡献占比。
            // 某一维占 90% → 该维方差被低估（特征不稳）；各维均匀 → 整体尺度问题（维数/样本配比）。
            if (AnomDebug())
            {
                float best = -1.0f;
                int bp = 0;
                for (int p = 0; p < P; ++p)
                {
                    float v = score.at<float>(p / a.gw, p % a.gw);
                    if (v > best) { best = v; bp = p; }
                }

                const float* L = &a.chol[(size_t)bp * d * d];
                const float* x = &feat[(size_t)bp * d];
                const float* mu = &a.mean[(size_t)bp * d];
                float z[64];
                double acc = 0.0;
                for (int i = 0; i < d; ++i)
                {
                    double sv = (double)x[i] - (double)mu[i];
                    for (int k = 0; k < i; ++k) sv -= (double)L[(size_t)i * d + k] * (double)z[k];
                    float dg = L[(size_t)i * d + i];
                    if (dg <= 1e-12f) dg = 1e-12f;
                    z[i] = (float)(sv / dg);
                    acc += (double)z[i] * (double)z[i];
                }
                std::fprintf(stderr, "[anom] max=(%d,%d) score=%.3f thr=%.3f d=%d share:",
                             bp % a.gw, bp / a.gw, (double)best, (double)thr, d);
                for (int i = 0; i < d; ++i)
                    std::fprintf(stderr, " %.0f%%", acc > 0.0 ? 100.0 * (double)z[i] * (double)z[i] / acc : 0.0);
                std::fprintf(stderr, "\n");

                if (d <= 8)
                {
                    for (int i = 0; i < d; ++i)
                    {
                        // 协方差对角 = Σ_k L(i,k)²（L 是下三角），用来判断"σ 是不是被估成了 0"
                        double sdi = 0.0;
                        for (int k = 0; k <= i; ++k)
                            sdi += (double)L[(size_t)i * d + k] * (double)L[(size_t)i * d + k];
                        std::fprintf(stderr, "[anom]   i=%d x=%.5f mu=%.5f sd=%.6f z=%.1f\n",
                                     i, (double)x[i], (double)mu[i], std::sqrt(sdi), (double)z[i]);
                    }
                }
            }
        }
        else
        {
            std::vector<float> fT, fS;
            if (!ForwardNet(a.teacher, grayPatch, fT, err)) { SetError(ctx, err.c_str()); return VA_ERR_MODEL_INFER; }
            if (!ForwardNet(a.student, grayPatch, fS, err)) { SetError(ctx, err.c_str()); return VA_ERR_MODEL_INFER; }

            const int C = a.teacher.out_c, H = a.teacher.out_h, W = a.teacher.out_w;
            if (fT.size() != fS.size() || (size_t)C * H * W != fT.size()) {
                SetError(ctx, "学生与教师输出不一致");
                return VA_ERR_SIZE;
            }

            // 学生-教师差异（局部异常 / 结构缺失）
            cv::Mat m1(H, W, CV_32F);
            for (int y = 0; y < H; ++y)
            {
                for (int x = 0; x < W; ++x)
                {
                    double acc = 0.0;
                    size_t idx = (size_t)y * W + x;
                    for (int c = 0; c < C; ++c)
                    {
                        double dv = (double)fS[(size_t)c * H * W + idx] - (double)fT[(size_t)c * H * W + idx];
                        acc += dv * dv;
                    }
                    m1.at<float>(y, x) = (float)(acc / std::max(1, C));
                }
            }

            cv::Mat comb;
            m1.convertTo(comb, CV_32F);
            comb = (comb - cv::Scalar(a.st_mean)) / std::max(1e-6f, a.st_std);

            if (a.ae.ok)
            {
                // 自编码器分支（全局异常 / 逻辑异常）：输入是教师特征，重建差异
                std::vector<float> rec;
                if (!ForwardNetWithTensor(a.ae, fT, rec, err)) {
                    SetError(ctx, err.c_str());
                    return VA_ERR_MODEL_INFER;
                }
                if (rec.size() != fT.size()) {
                    SetError(ctx, "自编码器输出维度与教师特征不一致");
                    return VA_ERR_SIZE;
                }

                cv::Mat m2(H, W, CV_32F);
                for (int y = 0; y < H; ++y)
                {
                    for (int x = 0; x < W; ++x)
                    {
                        double acc = 0.0;
                        size_t idx = (size_t)y * W + x;
                        for (int c = 0; c < C; ++c)
                        {
                            double dv = (double)rec[(size_t)c * H * W + idx] - (double)fT[(size_t)c * H * W + idx];
                            acc += dv * dv;
                        }
                        m2.at<float>(y, x) = (float)(acc / std::max(1, C));
                    }
                }
                m2 = (m2 - cv::Scalar(a.ae_mean)) / std::max(1e-6f, a.ae_std);

                comb = 0.5f * comb + 0.5f * m2;
            }

            score = comb;
        }

        cv::Mat full;
        UpscaleMap(score, full, rw, rh);
        if (full.type() != CV_32F) full.convertTo(full, CV_32F);

        // ---- 统计 + 热图 + 掩膜 ----
        double mn = 0, mx = -1e30;
        cv::minMaxLoc(full, &mn, &mx);

        cv::Mat mask = (full > thr);
        mask.convertTo(mask, CV_8U, 255);

        int areaPx = cv::countNonZero(mask);
        int defectCount = 0;
        float boxX = 0, boxY = 0, boxW = 0, boxH = 0;

        if (areaPx > 0)
        {
            cv::Mat labels, stats, centroids;
            int nLabels = cv::connectedComponentsWithStats(mask, labels, stats, centroids, 8, CV_32S);

            // 每个连通域的分数总和用一趟扫描累加（按标签索引），
            // 逐个连通域再扫全图是 O(标签数 × ROI)，缺陷多时会明显变慢。
            std::vector<double> labelSum((size_t)nLabels, 0.0);
            for (int y = 0; y < rh; ++y)
            {
                const int* lrow = labels.ptr<int>(y);
                const float* frow = full.ptr<float>(y);
                for (int x = 0; x < rw; ++x)
                {
                    int lb = lrow[x];
                    if (lb > 0) labelSum[(size_t)lb] += frow[x];
                }
            }

            int bestLabel = -1;
            double bestSum = -1;
            int keptArea = 0;
            const int minArea = std::max(1, min_area_px);

            for (int i = 1; i < nLabels; ++i)
            {
                int area = stats.at<int>(i, cv::CC_STAT_AREA);
                if (area < minArea) continue;

                defectCount++;
                keptArea += area;     // 面积只统计"够大的"连通域：不然一片噪点也能把占比刷满

                if (labelSum[(size_t)i] > bestSum)
                {
                    bestSum = labelSum[(size_t)i];
                    bestLabel = i;
                }
            }

            // 没有够大的连通域时，面积记 0（否则 max_score 判 NG 但面积为 0，读数自相矛盾）
            areaPx = (defectCount > 0) ? keptArea : 0;

            if (bestLabel > 0)
            {
                boxX = (float)(stats.at<int>(bestLabel, cv::CC_STAT_LEFT) + rx);
                boxY = (float)(stats.at<int>(bestLabel, cv::CC_STAT_TOP) + ry);
                boxW = (float)stats.at<int>(bestLabel, cv::CC_STAT_WIDTH);
                boxH = (float)stats.at<int>(bestLabel, cv::CC_STAT_HEIGHT);
            }
        }

        cv::Point maxLoc;
        cv::minMaxLoc(full, &mn, &mx, 0, &maxLoc);
        double meanAll = cv::mean(full)[0];

        out_stat->max_score = (float)(mx / thr);
        out_stat->mean_score = (float)(meanAll / thr);
        out_stat->raw_max = (float)mx;
        out_stat->threshold = thr;
        out_stat->area_ratio = (float)((double)areaPx / std::max(1, rw * rh));
        out_stat->area_px = areaPx;
        out_stat->defect_count = defectCount;
        out_stat->max_x = rx + maxLoc.x;
        out_stat->max_y = ry + maxLoc.y;
        out_stat->box_x = boxX; out_stat->box_y = boxY;
        out_stat->box_w = boxW; out_stat->box_h = boxH;
        out_stat->roi_x = rx; out_stat->roi_y = ry;
        out_stat->roi_w = rw; out_stat->roi_h = rh;

        // 热图：写"相对阈值倍数"，界面按 0~2 上色（1.0 = 阈值线）。
        if (out_map != 0 && map_cap >= rw * rh && out_w != 0 && out_h != 0)
        {
            const float inv = 1.0f / thr;
            for (int y = 0; y < rh; ++y)
            {
                const float* src = full.ptr<float>(y);
                float* dst = out_map + (size_t)y * rw;
                for (int x = 0; x < rw; ++x) dst[x] = src[x] * inv;
            }
            *out_w = rw;
            *out_h = rh;

            if (out_mask != 0 && mask_cap >= rw * rh)
            {
                for (int y = 0; y < rh; ++y)
                {
                    const unsigned char* src = mask.ptr<unsigned char>(y);
                    std::memcpy(out_mask + (size_t)y * rw, src, (size_t)rw);
                }
            }
        }

        return VA_OK;
    }
    catch (const std::exception& e)
    {
        SetError(ctx, e);
        return VA_ERR_INTERNAL;
    }
}

int AnomInfo(Context* ctx, char* buf, int cap)
{
    if (buf == 0 || cap <= 0) { SetError(ctx, "null argument"); return VA_ERR_ARG; }
    buf[0] = '\0';

    AnomalyState& a = ctx->anom;
    if (!a.created) return 0;

    char tmp[512];
    if (a.kind == VA_ANOM_KIND_PADIM)
        std::snprintf(tmp, sizeof(tmp),
                      "PaDiM | %d张 | %dx%d网格 | d=%d | 阈值 %.3f | %s",
                      a.samples, a.gw, a.gh, a.dim, a.threshold,
                      a.feat == VA_ANOM_FEAT_OPENVINO ? "OpenVINO特征" : "内置描述子");
    else
        std::snprintf(tmp, sizeof(tmp),
                      "EfficientAD | %dx%d特征 | 阈值 %.3f | AE %s",
                      a.gw, a.gh, a.threshold, a.ae.ok ? "有" : "无");

    int n = (int)std::strlen(tmp);
    if (n > cap - 1) n = cap - 1;
    std::memcpy(buf, tmp, (size_t)n);
    buf[n] = '\0';
    return n;
}

int AnomPeek(const char* model_dir, char* buf, int cap)
{
    if (buf == 0 || cap <= 0) return 0;
    buf[0] = '\0';
    if (model_dir == 0 || model_dir[0] == '\0') return 0;

    std::string json = ReadAllText(JoinPath(std::string(model_dir), "model.json"));
    if (json.empty()) return 0;

    int kind = JsonInt(json, "kind", -1);
    if (kind < 0) return 0;

    char tmp[384];
    if (kind == VA_ANOM_KIND_PADIM)
        std::snprintf(tmp, sizeof(tmp), "PaDiM · %d张 · %dx%d · 阈值 %.2f",
                      JsonInt(json, "samples", 0), JsonInt(json, "grid_w", 0),
                      JsonInt(json, "grid_h", 0), JsonFloat(json, "threshold", 0.0f));
    else
        std::snprintf(tmp, sizeof(tmp), "EfficientAD · 阈值 %.2f",
                      JsonFloat(json, "threshold", 0.0f));

    int n = (int)std::strlen(tmp);
    if (n > cap - 1) n = cap - 1;
    std::memcpy(buf, tmp, (size_t)n);
    buf[n] = '\0';
    return n;
}

int AnomClear(Context* ctx)
{
    ctx->anom.Reset();
    return VA_OK;
}

} // namespace va
