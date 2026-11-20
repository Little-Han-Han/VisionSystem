// 分类器：轮廓特征（Hu 矩 / 几何）+ OpenCV ml 的 SVM / KNN。
// 无 contrib、无额外依赖，样本与模型挂在 va::Context 上（一个句柄 = 一条流程）。
#include "va_internal.h"
#include "va_error.h"

#include <cmath>
#include <set>
#include <sstream>

namespace va {

namespace {

const int HU_DIM  = 7;   // Hu 矩维数
const int GEOM_DIM = 5;  // 几何特征维数

// 统一前景极性：以图像四条边的平均灰度判断背景明暗。
// Otsu 只给出「把直方图分成两类的阈值」，不保证前景是 255；
// 若目标是「暗物体 + 亮背景」，二值化后最大轮廓会是背景边框，
// 取最大轮廓就失去意义。这里把前景统一成 255。
//
// 【前置条件】bin 必须已经是 threshold(gray, thresh, 255, BINARY) 的结果。
// 原实现内部又做了一次同样的阈值化 —— 每帧多一次全图扫描 + 一次分配，结果完全相同。
void NormalizePolarity(const cv::Mat& gray, cv::Mat& bin, int thresh)
{
    double sum = 0.0;
    int n = 0;
    for (int x = 0; x < gray.cols; ++x) {
        sum += (double)gray.at<unsigned char>(0, x);
        sum += (double)gray.at<unsigned char>(gray.rows - 1, x);
        n += 2;
    }
    for (int y = 0; y < gray.rows; ++y) {
        sum += (double)gray.at<unsigned char>(y, 0);
        sum += (double)gray.at<unsigned char>(y, gray.cols - 1);
        n += 2;
    }
    double borderMean = (n > 0) ? (sum / n) : 0.0;

    // 边框明显偏亮 → 背景是亮的 → 前景（目标）是暗的 → 反色
    if (borderMean > (double)thresh) cv::bitwise_not(bin, bin);
}

// Hu 矩：数值跨度常达 1e-7 量级，必须做对数变换后再用于距离度量
void AppendHu(const std::vector<cv::Point>& contour, std::vector<float>& feat)
{
    cv::Moments m = cv::moments(contour);
    double hu[HU_DIM];
    cv::HuMoments(m, hu);

    for (int i = 0; i < HU_DIM; ++i) {
        double v = hu[i];
        double sign = (v >= 0.0) ? 1.0 : -1.0;
        double a = std::fabs(v);
        if (a < 1e-30) a = 1e-30;
        feat.push_back((float)(-sign * std::log10(a)));
    }
}

// 几何特征：面积 / 周长 / 圆度 / 长宽比 / 填充度
void AppendGeom(const std::vector<cv::Point>& contour, std::vector<float>& feat)
{
    double area  = std::fabs(cv::contourArea(contour));
    double peri  = cv::arcLength(contour, true);
    cv::Rect bb  = cv::boundingRect(contour);
    double bbArea = (double)bb.width * (double)bb.height;

    double circularity = (area > 1e-6) ? (peri * peri) / (4.0 * CV_PI * area) : 0.0;
    double aspect      = (bb.height > 0) ? ((double)bb.width / (double)bb.height) : 0.0;
    double fill        = (bbArea > 1e-6) ? (area / bbArea) : 0.0;

    feat.push_back((float)area);
    feat.push_back((float)peri);
    feat.push_back((float)circularity);
    feat.push_back((float)aspect);
    feat.push_back((float)fill);
}

int FeatureDim(int featureType)
{
    switch (featureType) {
        case VA_FEAT_HU:     return HU_DIM;
        case VA_FEAT_GEOM:   return GEOM_DIM;
        case VA_FEAT_HYBRID: return HU_DIM + GEOM_DIM;
        default:             return -1;
    }
}

// 从灰度图提取特征；失败时填充 err 并返回 false
bool ExtractFeatures(const cv::Mat& gray, int featureType, std::vector<float>& feat, std::string& err)
{
    // Otsu 自动阈值；返回值即实际使用的阈值
    cv::Mat bin;
    double otsu = cv::threshold(gray, bin, 0.0, 255.0, cv::THRESH_BINARY | cv::THRESH_OTSU);

    // 用 Otsu 阈值做极性归一化，保证前景（目标）恒为 255
    // （bin 已经是同一个阈值的结果，这里只判极性、必要时反色）
    NormalizePolarity(gray, bin, (int)otsu);

    std::vector<cv::Point> contour;
    if (!LargestContour(bin, contour, err, true)) return false;   // bin 已是二值图，不再重复阈值化

    feat.clear();
    if (featureType == VA_FEAT_HU) {
        AppendHu(contour, feat);
    } else if (featureType == VA_FEAT_GEOM) {
        AppendGeom(contour, feat);
    } else {
        AppendHu(contour, feat);
        AppendGeom(contour, feat);
    }
    return true;
}

// 把累积样本组装成训练矩阵
void BuildTrainData(const ClassifierState& st, int dim, cv::Mat& samples, cv::Mat& responses)
{
    int n = (int)st.features.size();
    samples.create(n, dim, CV_32F);
    responses.create(n, 1, CV_32S);

    for (int i = 0; i < n; ++i) {
        for (int j = 0; j < dim; ++j) {
            samples.at<float>(i, j) = (j < (int)st.features[i].size()) ? st.features[i][j] : 0.0f;
        }
        responses.at<int>(i, 0) = st.labels[i];
    }
}

} // namespace

int ClfCreate(Context* ctx, const VA_ClassParam* param)
{
    if (param == 0) { SetError(ctx, "null argument"); return VA_ERR_ARG; }
    if (param->model_type != VA_CLF_SVM && param->model_type != VA_CLF_KNN) {
        SetError(ctx, "unknown model type");
        return VA_ERR_ARG;
    }
    if (FeatureDim(param->feature_type) < 0) {
        SetError(ctx, "unknown feature type");
        return VA_ERR_ARG;
    }

    ctx->clf.features.clear();
    ctx->clf.labels.clear();
    ctx->clf.model.release();
    ctx->clf.param   = *param;
    ctx->clf.created = true;
    ctx->clf.trained = false;
    return VA_OK;
}

int ClfAddSample(Context* ctx, const VA_Image* gray, int label)
{
    if (gray == 0) { SetError(ctx, "null argument"); return VA_ERR_ARG; }
    if (!ctx->clf.created) { SetError(ctx, "classifier not created"); return VA_ERR_ARG; }

    try
    {
        cv::Mat g;
        if (!ToMat(gray, g, ctx->lastError)) return VA_ERR_ARG;
        if (g.channels() != 1) {
            SetError(ctx, "classifier requires single channel gray image");
            return VA_ERR_TYPE;
        }

        std::vector<float> feat;
        if (!ExtractFeatures(g, ctx->clf.param.feature_type, feat, ctx->lastError)) {
            return VA_ERR_ARG;
        }

        ctx->clf.features.push_back(feat);
        ctx->clf.labels.push_back(label);
        // 新增样本后旧模型已过期
        ctx->clf.trained = false;
        return VA_OK;
    }
    catch (const cv::Exception& e) { SetError(ctx, e); return VA_ERR_INTERNAL; }
    catch (const std::exception& e) { SetError(ctx, e); return VA_ERR_INTERNAL; }
}

int ClfSampleCount(Context* ctx, int* outCount)
{
    if (outCount == 0) { SetError(ctx, "null argument"); return VA_ERR_ARG; }
    *outCount = (int)ctx->clf.features.size();
    return VA_OK;
}

int ClfTrain(Context* ctx)
{
    if (!ctx->clf.created) { SetError(ctx, "classifier not created"); return VA_ERR_ARG; }

    int dim = FeatureDim(ctx->clf.param.feature_type);
    if (dim < 0) { SetError(ctx, "unknown feature type"); return VA_ERR_ARG; }

    int n = (int)ctx->clf.features.size();
    if (n < 2) {
        std::ostringstream os;
        os << "not enough samples for training: " << n << " (need at least 2)";
        SetError(ctx, os.str().c_str());
        return VA_ERR_ARG;
    }

    std::set<int> classes(ctx->clf.labels.begin(), ctx->clf.labels.end());
    if (classes.size() < 2) {
        std::ostringstream os;
        os << "not enough classes for training: " << classes.size() << " (need at least 2)";
        SetError(ctx, os.str().c_str());
        return VA_ERR_ARG;
    }

    try
    {
        cv::Mat samples, responses;
        BuildTrainData(ctx->clf, dim, samples, responses);

        if (ctx->clf.param.model_type == VA_CLF_SVM) {
            cv::Ptr<cv::ml::SVM> svm = cv::ml::SVM::create();
            svm->setType(cv::ml::SVM::C_SVC);
            switch (ctx->clf.param.svm_kernel) {
                case 1:  svm->setKernel(cv::ml::SVM::POLY);   break;
                case 2:  svm->setKernel(cv::ml::SVM::RBF);    break;
                default: svm->setKernel(cv::ml::SVM::LINEAR); break;
            }
            if (ctx->clf.param.C > 0.0)     svm->setC(ctx->clf.param.C);
            if (ctx->clf.param.gamma > 0.0) svm->setGamma(ctx->clf.param.gamma);

            if (!svm->train(samples, cv::ml::ROW_SAMPLE, responses)) {
                SetError(ctx, "svm training failed");
                return VA_ERR_INTERNAL;
            }
            ctx->clf.model = svm;
        } else {
            cv::Ptr<cv::ml::KNearest> knn = cv::ml::KNearest::create();
            int k = (ctx->clf.param.knn_k < 1) ? 5 : ctx->clf.param.knn_k;
            if (k > n) k = n;
            knn->setDefaultK(k);
            knn->setIsClassifier(true);

            if (!knn->train(samples, cv::ml::ROW_SAMPLE, responses)) {
                SetError(ctx, "knn training failed");
                return VA_ERR_INTERNAL;
            }
            ctx->clf.model = knn;
        }

        ctx->clf.trained = true;
        return VA_OK;
    }
    catch (const cv::Exception& e) { SetError(ctx, e); return VA_ERR_INTERNAL; }
    catch (const std::exception& e) { SetError(ctx, e); return VA_ERR_INTERNAL; }
}

int ClfPredict(Context* ctx, const VA_Image* gray, int* outLabel, double* outScore)
{
    if (gray == 0 || outLabel == 0) { SetError(ctx, "null argument"); return VA_ERR_ARG; }
    if (!ctx->clf.trained || ctx->clf.model.empty()) {
        SetError(ctx, "classifier not trained");
        return VA_ERR_NO_TEMPLATE;
    }

    try
    {
        cv::Mat g;
        if (!ToMat(gray, g, ctx->lastError)) return VA_ERR_ARG;
        if (g.channels() != 1) {
            SetError(ctx, "classifier requires single channel gray image");
            return VA_ERR_TYPE;
        }

        int dim = FeatureDim(ctx->clf.param.feature_type);
        if (dim < 0) { SetError(ctx, "unknown feature type"); return VA_ERR_ARG; }

        std::vector<float> feat;
        if (!ExtractFeatures(g, ctx->clf.param.feature_type, feat, ctx->lastError)) {
            return VA_ERR_ARG;
        }

        cv::Mat sample(1, dim, CV_32F);
        for (int j = 0; j < dim; ++j) {
            sample.at<float>(0, j) = (j < (int)feat.size()) ? feat[j] : 0.0f;
        }

        if (ctx->clf.param.model_type == VA_CLF_SVM) {
            cv::Ptr<cv::ml::SVM> svm = ctx->clf.model.dynamicCast<cv::ml::SVM>();
            if (svm.empty()) { SetError(ctx, "model type mismatch"); return VA_ERR_INTERNAL; }

            float label = svm->predict(sample);
            // RAW_OUTPUT 给出决策函数值（有符号距离），作为置信度返回
            float raw = svm->predict(sample, cv::noArray(), cv::ml::StatModel::RAW_OUTPUT);

            *outLabel = (int)label;
            if (outScore != 0) *outScore = (double)raw;
        } else {
            cv::Ptr<cv::ml::KNearest> knn = ctx->clf.model.dynamicCast<cv::ml::KNearest>();
            if (knn.empty()) { SetError(ctx, "model type mismatch"); return VA_ERR_INTERNAL; }

            int k = (ctx->clf.param.knn_k < 1) ? 5 : ctx->clf.param.knn_k;
            int ntot = (int)ctx->clf.features.size();
            if (k < 1) k = 1;
            if (k > ntot) k = ntot;

            cv::Mat results, neighbors, dists;
            float r = knn->findNearest(sample, k, results, neighbors, dists);

            int label = (int)r;
            int votes = 0;
            int total = neighbors.rows * neighbors.cols;
            for (int i = 0; i < total; ++i) {
                if ((int)neighbors.at<float>(i) == label) ++votes;
            }

            *outLabel = label;
            if (outScore != 0) {
                *outScore = (total > 0) ? ((double)votes / (double)total) : 0.0;
            }
        }

        return VA_OK;
    }
    catch (const cv::Exception& e) { SetError(ctx, e); return VA_ERR_INTERNAL; }
    catch (const std::exception& e) { SetError(ctx, e); return VA_ERR_INTERNAL; }
}

int ClfSave(Context* ctx, const char* path)
{
    if (path == 0) { SetError(ctx, "null argument"); return VA_ERR_ARG; }
    if (!ctx->clf.trained || ctx->clf.model.empty()) {
        SetError(ctx, "classifier not trained");
        return VA_ERR_NO_TEMPLATE;
    }

    try
    {
        if (ctx->clf.param.model_type == VA_CLF_SVM) {
            cv::Ptr<cv::ml::SVM> svm = ctx->clf.model.dynamicCast<cv::ml::SVM>();
            if (svm.empty()) { SetError(ctx, "model type mismatch"); return VA_ERR_INTERNAL; }
            svm->save(cv::String(path));
        } else {
            cv::Ptr<cv::ml::KNearest> knn = ctx->clf.model.dynamicCast<cv::ml::KNearest>();
            if (knn.empty()) { SetError(ctx, "model type mismatch"); return VA_ERR_INTERNAL; }
            knn->save(cv::String(path));
        }
        return VA_OK;
    }
    catch (const cv::Exception& e) { SetError(ctx, e); return VA_ERR_INTERNAL; }
    catch (const std::exception& e) { SetError(ctx, e); return VA_ERR_INTERNAL; }
}

int ClfLoad(Context* ctx, const char* path)
{
    if (path == 0) { SetError(ctx, "null argument"); return VA_ERR_ARG; }

    try
    {
        // 先按当前 model_type 试；读不出再试另一种，这样调用方不必预先知道文件里是哪种模型
        int order[2];
        if (ctx->clf.created && ctx->clf.param.model_type == VA_CLF_KNN) {
            order[0] = VA_CLF_KNN; order[1] = VA_CLF_SVM;
        } else {
            order[0] = VA_CLF_SVM; order[1] = VA_CLF_KNN;
        }

        for (int i = 0; i < 2; ++i) {
            if (order[i] == VA_CLF_SVM) {
                cv::Ptr<cv::ml::SVM> svm;
                try { svm = cv::ml::SVM::load(cv::String(path)); }
                catch (...) { svm.release(); }
                if (!svm.empty()) {
                    ctx->clf.model = svm;
                    ctx->clf.param.model_type = VA_CLF_SVM;
                    ctx->clf.created = true;
                    ctx->clf.trained = true;
                    return VA_OK;
                }
            } else {
                cv::Ptr<cv::ml::KNearest> knn;
                try { knn = cv::ml::KNearest::load(cv::String(path)); }
                catch (...) { knn.release(); }
                if (!knn.empty()) {
                    ctx->clf.model = knn;
                    ctx->clf.param.model_type = VA_CLF_KNN;
                    ctx->clf.created = true;
                    ctx->clf.trained = true;
                    return VA_OK;
                }
            }
        }

        SetError(ctx, "failed to load classifier model (not an OpenCV SVM/KNN xml?)");
        return VA_ERR_ARG;
    }
    catch (const cv::Exception& e) { SetError(ctx, e); return VA_ERR_INTERNAL; }
    catch (const std::exception& e) { SetError(ctx, e); return VA_ERR_INTERNAL; }
}

int ClfClear(Context* ctx)
{
    ctx->clf.features.clear();
    ctx->clf.labels.clear();
    ctx->clf.model.release();
    ctx->clf.created = false;
    ctx->clf.trained = false;
    return VA_OK;
}

} // namespace va
