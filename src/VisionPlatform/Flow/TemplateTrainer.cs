using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using VisionPlatform.Core;
using VisionPlatform.Native;

namespace VisionPlatform.Flow
{
    /// <summary>模板训练的结果分类（每一类都必须给出可行动的中文原因）。</summary>
    public enum TemplateTrainStatus
    {
        /// <summary>训练成功。</summary>
        Ok = 0,

        /// <summary>ROI 过小：小于 8 像素，或小于 3 倍最小特征间距。</summary>
        RoiTooSmall = 1,

        /// <summary>训练成功但有效特征点过少（&lt; 8）。</summary>
        NoFeature = 2,

        /// <summary>参数互相冲突（对比度 &lt;= 0、缩放下限 &gt; 上限、角度下限 &gt; 上限）。</summary>
        ParamConflict = 3,

        /// <summary>训练耗时超过 <see cref="TemplateTrainer.TimeoutMs"/>。</summary>
        Timeout = 4,

        /// <summary>原生库返回错误（消息带中文前缀 + 原生原文）。</summary>
        NativeError = 5
    }

    /// <summary>一次训练的结果。</summary>
    public sealed class TemplateTrainResult
    {
        /// <summary>结果分类。</summary>
        public TemplateTrainStatus Status = TemplateTrainStatus.NativeError;

        /// <summary>中文、可行动的原因或结论（成功时是"训练成功"的一句话）。</summary>
        public string Message = "";

        /// <summary>特征点数；NCC 恒 0。</summary>
        public int FeatureCount;

        /// <summary>训练耗时（毫秒）。</summary>
        public int ElapsedMs;

        /// <summary>实际参与训练的 ROI（已夹到图像范围内；宽高为 0 表示整图）。</summary>
        public Rectangle EffectiveRoi = Rectangle.Empty;

        /// <summary>形状匹配特征点（坐标相对模板中心，给 <c>ModelFeatureLayer</c> 叠加用）；NCC 为 null。</summary>
        public List<ShapeModelPoint> Points;

        /// <summary>模型尺寸信息；NCC 为 null。</summary>
        public ShapeModelInfo ModelInfo;

        /// <summary>是否成功。</summary>
        public bool Success { get { return Status == TemplateTrainStatus.Ok; } }
    }

    /// <summary>
    /// 模板训练器：<strong>独占一个 <see cref="VisionAlgo"/> 上下文</strong>，<see cref="Dispose"/> 时释放。
    /// <para>
    /// 【为什么必须独占】原生库的形状模型是"每个上下文一个槽位"的单槽设计。若训练借用
    /// <c>FlowRunner</c> 或 <c>AppContext.Algo</c> 的上下文，用户在"一边训练一边跑流程"时
    /// 会互相踢掉对方的模型 —— 表现为"训练完点保存，结果流程里的匹配莫名其妙失效"。
    /// 独占一个上下文后，训练期间对流程零影响，关闭窗体即归还。
    /// </para>
    /// </summary>
    public sealed class TemplateTrainer : IDisposable
    {
        /// <summary>训练超时阈值（毫秒）。超过即判定 <see cref="TemplateTrainStatus.Timeout"/>。</summary>
        public const int TimeoutMs = 3000;

        /// <summary>ROI 的最小边长（像素）。</summary>
        public const int MinRoiPx = 8;

        /// <summary>有效特征点下限：少于这个数说明"这块 ROI 里没有可匹配的形状"。</summary>
        public const int MinFeaturePoints = 8;

        private readonly VisionAlgo _algo;
        private bool _disposed;

        /// <summary>创建训练器（内部 <c>new VisionAlgo()</c>，独占上下文）。</summary>
        public TemplateTrainer()
        {
            _algo = new VisionAlgo();
        }

        /// <summary>独占的算法上下文是否可用（原生库缺失时为 false）。</summary>
        public bool IsValid { get { return _algo != null && _algo.IsValid; } }

        /// <summary>
        /// 在整幅帧上按 ROI 训练（<c>TemplateForm</c> 的常规路径）。
        /// </summary>
        /// <param name="frame">源帧。</param>
        /// <param name="roi">模板 ROI（源帧坐标；宽高为 0 表示整帧）。</param>
        /// <param name="algorithm"><c>"ShapeMatch"</c> 或 <c>"TemplateMatch"</c>。</param>
        public TemplateTrainResult Train(Bitmap frame, Rectangle roi, string algorithm,
                                         double contrast, int maxFeatures, double minFeatureDist)
        {
            if (frame == null)
                return Fail(TemplateTrainStatus.NativeError, "请先载入图像或抓取当前帧，再训练。");

            Rectangle effective = ClipRoi(frame, roi);
            return TrainCore(frame, effective, algorithm, contrast, maxFeatures, minFeatureDist,
                             frame.Width, frame.Height);
        }

        /// <summary>
        /// 在"已裁剪好的模板图"上训练（运行期从落盘资产重训、保存后回读重训都走这里）。
        /// </summary>
        public TemplateTrainResult TrainFromTemplate(Bitmap template, string algorithm,
                                                     double contrast, int maxFeatures, double minFeatureDist)
        {
            if (template == null)
                return Fail(TemplateTrainStatus.NativeError, "模板图为空，无法训练。");

            return TrainCore(template, Rectangle.Empty, algorithm, contrast, maxFeatures, minFeatureDist,
                             template.Width, template.Height);
        }

        /// <summary>
        /// 训练参数校验（按 <see cref="ShapeTrainParam"/> 的三个参数）。失败返回 false 并给出可行动的中文。
        /// </summary>
        public static bool CheckTrainParams(double contrast, int maxFeatures, double minFeatureDist,
                                           out string message)
        {
            message = null;

            if (double.IsNaN(contrast) || contrast <= 0.0)
            {
                message = string.Format(CultureInfo.InvariantCulture,
                    "对比度必须大于 0（当前 {0:F1}）。对比度太低会把噪声当边缘，太高会漏掉真实轮廓，典型 20~60。",
                    contrast);
                return false;
            }
            if (maxFeatures < 10)
            {
                message = string.Format(CultureInfo.InvariantCulture,
                    "最大特征点数过小（当前 {0}），至少 10 个；建议 800~2000。", maxFeatures);
                return false;
            }
            if (double.IsNaN(minFeatureDist) || minFeatureDist <= 0.0)
            {
                message = string.Format(CultureInfo.InvariantCulture,
                    "最小特征间距必须大于 0（当前 {0:F1} px），建议 1~3 px。", minFeatureDist);
                return false;
            }
            return true;
        }

        /// <summary>
        /// 匹配参数校验：<c>ScaleMin &gt; ScaleMax</c> / <c>AngleStart &gt; AngleEnd</c> 属参数冲突。
        /// <para>
        /// 这两个是<strong>匹配</strong>参数、不是训练参数，所以不在 <see cref="Train"/> 里判；
        /// 但用户在模板窗里改它们再点训练时，必须当场指出冲突，否则会得到"训练成功、匹配永远找不到"的怪现象。
        /// </para>
        /// </summary>
        public static bool CheckMatchParamConflict(double scaleMin, double scaleMax,
                                                   double angleStart, double angleEnd,
                                                   out string message)
        {
            message = null;

            if (scaleMin > scaleMax)
            {
                message = string.Format(CultureInfo.InvariantCulture,
                    "参数冲突：最小缩放 {0:F2} 大于最大缩放 {1:F2}，请把最小缩放调小或把最大缩放调大。",
                    scaleMin, scaleMax);
                return false;
            }
            if (angleStart > angleEnd)
            {
                message = string.Format(CultureInfo.InvariantCulture,
                    "参数冲突：起始角度 {0:F1}° 大于结束角度 {1:F1}°，请把起始角度调小或把结束角度调大。",
                    angleStart, angleEnd);
                return false;
            }
            return true;
        }

        // =====================================================================
        //  内部实现
        // =====================================================================

        private TemplateTrainResult TrainCore(Bitmap image, Rectangle roi, string algorithm,
                                              double contrast, int maxFeatures, double minFeatureDist,
                                              int sourceWidth, int sourceHeight)
        {
            string paramError;
            if (!CheckTrainParams(contrast, maxFeatures, minFeatureDist, out paramError))
                return Fail(TemplateTrainStatus.ParamConflict, paramError);

            bool isNcc = string.Equals(algorithm, "TemplateMatch", StringComparison.OrdinalIgnoreCase);
            bool isShape = !isNcc && string.Equals(algorithm, "ShapeMatch", StringComparison.OrdinalIgnoreCase);
            if (!isNcc && !isShape)
                return Fail(TemplateTrainStatus.ParamConflict,
                    "未知的模板算法「" + (algorithm ?? "(空)") + "」，只支持形状匹配与模板匹配(NCC)。");

            // ROI 过小：< 8px，或 < 3 × 最小特征间距。
            // 后者是"特征点根本排不下"的判据 —— 200×5 的细长条看着有 1000 像素，实际一个特征点都放不进去。
            if (roi.Width > 0 && roi.Height > 0)
            {
                double minSide = 3.0 * Math.Max(1.0, minFeatureDist);
                if (roi.Width < MinRoiPx || roi.Height < MinRoiPx
                    || roi.Width < minSide || roi.Height < minSide)
                {
                    return Fail(TemplateTrainStatus.RoiTooSmall, string.Format(CultureInfo.InvariantCulture,
                        "ROI 区域过小（{0}×{1} 像素，宽高需 ≥ {2} 且 ≥ 3×最小间距 {3:F1}），请扩大 ROI 或缩小最小间距。",
                        roi.Width, roi.Height, MinRoiPx, minSide));
                }
            }
            else if (image.Width < MinRoiPx || image.Height < MinRoiPx)
            {
                return Fail(TemplateTrainStatus.RoiTooSmall, string.Format(CultureInfo.InvariantCulture,
                    "图像区域过小（{0}×{1} 像素），至少需要 {2}×{2}。", image.Width, image.Height, MinRoiPx));
            }

            var result = new TemplateTrainResult();
            result.EffectiveRoi = roi;

            var sw = Stopwatch.StartNew();
            try
            {
                if (isNcc)
                {
                    OpResult tr = _algo.TrainTemplate(image, roi);
                    sw.Stop();
                    result.ElapsedMs = (int)sw.ElapsedMilliseconds;

                    if (tr == null || !tr.Success)
                    {
                        result.Status = TemplateTrainStatus.NativeError;
                        result.Message = "模板匹配训练失败：" + (tr != null ? tr.Message : "原生库无返回");
                        return result;
                    }

                    result.FeatureCount = 0;
                    result.Points = null;
                    result.ModelInfo = null;

                    if (result.ElapsedMs > TimeoutMs)
                    {
                        result.Status = TemplateTrainStatus.Timeout;
                        result.Message = string.Format(CultureInfo.InvariantCulture,
                            "训练超时（{0} ms，上限 {1} ms），请缩小 ROI 或减少最大特征点数。",
                            result.ElapsedMs, TimeoutMs);
                        return result;
                    }

                    result.Status = TemplateTrainStatus.Ok;
                    result.Message = string.Format(CultureInfo.InvariantCulture,
                        "训练成功（NCC）：模板已提取，尺寸 {0}×{1}，耗时 {2} ms。",
                        roi.Width > 0 ? roi.Width : image.Width,
                        roi.Height > 0 ? roi.Height : image.Height,
                        result.ElapsedMs);
                    return result;
                }

                var train = new ShapeTrainParam
                {
                    Contrast = contrast,
                    MaxFeatures = maxFeatures,
                    MinFeatureDist = minFeatureDist
                };

                OpResult<ShapeModelInfo> cr = _algo.CreateShapeModel(image, roi, train);
                sw.Stop();
                result.ElapsedMs = (int)sw.ElapsedMilliseconds;

                if (cr == null || !cr.Success)
                {
                    result.Status = TemplateTrainStatus.NativeError;
                    result.Message = "形状模型训练失败：" + (cr != null ? cr.Message : "原生库无返回");
                    return result;
                }

                result.ModelInfo = cr.Value;
                result.FeatureCount = cr.Value != null ? cr.Value.PointCount : 0;

                // 训练成功但点太少 = "这块 ROI 里没有可匹配的形状"，这是最常见的空结果，
                // 必须单独成一类并给出"扩大 ROI / 降低对比度"这种能立刻照做的建议。
                if (result.FeatureCount < MinFeaturePoints)
                {
                    result.Status = TemplateTrainStatus.NoFeature;
                    result.Message = string.Format(CultureInfo.InvariantCulture,
                        "ROI 区域内有效特征过少（{0} 个，至少需要 {1} 个），请扩大 ROI 或降低对比度阈值（当前 {2:F0}）。",
                        result.FeatureCount, MinFeaturePoints, contrast);
                    return result;
                }

                OpResult<List<ShapeModelPoint>> pts = _algo.GetShapeModelPoints(8000);
                result.Points = (pts != null && pts.Success && pts.Value != null)
                    ? pts.Value
                    : new List<ShapeModelPoint>();

                if (result.ElapsedMs > TimeoutMs)
                {
                    result.Status = TemplateTrainStatus.Timeout;
                    result.Message = string.Format(CultureInfo.InvariantCulture,
                        "训练超时（{0} ms，上限 {1} ms），请缩小 ROI 或减少最大特征点数（当前 {2}）。",
                        result.ElapsedMs, TimeoutMs, maxFeatures);
                    return result;
                }

                result.Status = TemplateTrainStatus.Ok;
                result.Message = string.Format(CultureInfo.InvariantCulture,
                    "训练成功：特征点 {0} 个，模型 {1:F0}×{2:F0}，耗时 {3} ms。",
                    result.FeatureCount,
                    result.ModelInfo != null ? result.ModelInfo.Width : 0.0,
                    result.ModelInfo != null ? result.ModelInfo.Height : 0.0,
                    result.ElapsedMs);
                return result;
            }
            catch (Exception ex)
            {
                sw.Stop();
                result.ElapsedMs = (int)sw.ElapsedMilliseconds;
                result.Status = TemplateTrainStatus.NativeError;
                result.Message = "训练异常（" + ex.GetType().Name + "）：" + ex.Message;
                return result;
            }
        }

        /// <summary>把 ROI 夹到图像范围内；宽高 ≤ 0 或完全在图外时退化为"整图"（<see cref="Rectangle.Empty"/>）。</summary>
        private static Rectangle ClipRoi(Bitmap image, Rectangle roi)
        {
            if (image == null) return Rectangle.Empty;
            if (roi.Width <= 0 || roi.Height <= 0) return Rectangle.Empty;

            Rectangle bounds = new Rectangle(0, 0, image.Width, image.Height);
            Rectangle real = Rectangle.Intersect(roi, bounds);
            return (real.Width > 0 && real.Height > 0) ? real : Rectangle.Empty;
        }

        private static TemplateTrainResult Fail(TemplateTrainStatus status, string message)
        {
            return new TemplateTrainResult { Status = status, Message = message ?? "" };
        }

        /// <summary>释放独占的算法上下文（连带释放其上的原生形状模型 / 模板）。</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_algo != null) _algo.Dispose();
        }
    }
}
