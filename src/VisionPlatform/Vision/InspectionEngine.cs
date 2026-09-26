using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using VisionPlatform.Core;
using VisionPlatform.Native;

namespace VisionPlatform.Vision
{
    /// <summary>
    /// 检测流程编排：取帧 → 预处理 → 各检测项 → 判定 OK/NG。
    /// 算法本体在 C++ 侧（VisionAlgo.Native），这里只做流程与判定。
    /// </summary>
    public sealed class InspectionEngine
    {
        private readonly VisionAlgo _algo;
        private bool _templateTrained;

        public InspectionEngine(VisionAlgo algo)
        {
            if (algo == null) throw new ArgumentNullException("algo");
            _algo = algo;
        }

        /// <summary>模板是否已训练。</summary>
        public bool TemplateTrained { get { return _templateTrained; } }

        /// <summary>用当前帧训练模板（roi 为空表示整图）。</summary>
        public OpResult TrainTemplate(Bitmap frame, Rectangle roi)
        {
            PreprocessParam p = PreprocessParam.Default();

            OpResult<Bitmap> grayResult = _algo.Preprocess(frame, p);
            if (!grayResult.Success) return OpResult.Fail(grayResult.Message);

            using (Bitmap gray = grayResult.Value)
            {
                OpResult r = _algo.TrainTemplate(gray, roi);
                if (r.Success) _templateTrained = true;
                return r;
            }
        }

        /// <summary>对一帧图像执行完整检测。</summary>
        public OpResult<InspectionResult> Run(Bitmap frame, InspectionConfig cfg)
        {
            if (frame == null) return OpResult<InspectionResult>.Fail("输入帧为空");
            if (cfg == null) cfg = new InspectionConfig();

            var sw = Stopwatch.StartNew();
            var result = new InspectionResult();
            bool pass = true;

            // ---------- 1. 预处理 ----------
            PreprocessParam preParam = cfg.Preprocess ?? PreprocessParam.Default();
            OpResult<Bitmap> pre = _algo.Preprocess(frame, preParam);
            if (!pre.Success)
                return OpResult<InspectionResult>.Fail("预处理失败：" + pre.Message);

            Bitmap gray = pre.Value;           // 由 result 持有，调用方负责释放
            result.GrayImage = gray;

            try
            {
                // ---------- 2. 清晰度 ----------
                if (cfg.EnableSharpness)
                {
                    OpResult<double> sr = _algo.Sharpness(gray);
                    if (sr.Success)
                    {
                        result.Sharpness = sr.Value;
                        bool ok = sr.Value >= cfg.MinSharpness;
                        result.Messages.Add(string.Format("清晰度 {0:F1}（下限 {1:F1}）{2}",
                            sr.Value, cfg.MinSharpness, ok ? "OK" : "NG"));
                        if (!ok) pass = false;
                    }
                    else
                    {
                        result.Messages.Add("清晰度检测失败：" + sr.Message);
                        pass = false;
                    }
                }

                // ---------- 3. 连通域 ----------
                if (cfg.EnableBlob)
                {
                    OpResult<List<BlobResult>> br = _algo.AnalyzeBlobs(gray, cfg.MinBlobArea, cfg.MaxBlobArea);
                    if (br.Success)
                    {
                        result.Blobs = br.Value;
                        result.Messages.Add(string.Format("找到 {0} 个目标", br.Value.Count));

                        if (cfg.ExpectedBlobCount > 0 && br.Value.Count != cfg.ExpectedBlobCount)
                        {
                            result.Messages.Add(string.Format("数量不符：期望 {0}，实际 {1}",
                                cfg.ExpectedBlobCount, br.Value.Count));
                            pass = false;
                        }
                    }
                    else
                    {
                        result.Messages.Add("Blob 分析失败：" + br.Message);
                        pass = false;
                    }
                }

                // ---------- 4. 模板匹配 ----------
                if (cfg.EnableTemplate)
                {
                    if (!_templateTrained)
                    {
                        result.Messages.Add("模板未训练");
                        pass = false;
                    }
                    else
                    {
                        OpResult<MatchResult> mr = _algo.MatchTemplate(gray);
                        if (mr.Success)
                        {
                            result.Match = mr.Value;
                            bool ok = mr.Value.Score >= cfg.MinMatchScore;
                            result.Messages.Add(string.Format("匹配得分 {0:F3}（下限 {1:F3}）{2}",
                                mr.Value.Score, cfg.MinMatchScore, ok ? "OK" : "NG"));
                            if (!ok) pass = false;
                        }
                        else
                        {
                            result.Messages.Add("模板匹配失败：" + mr.Message);
                            pass = false;
                        }
                    }
                }

                // ---------- 5. 圆拟合 ----------
                if (cfg.EnableCircleFit)
                {
                    OpResult<CircleResult> cr = _algo.FitCircle(gray);
                    if (cr.Success)
                    {
                        result.Circle = cr.Value;
                        result.Messages.Add(string.Format("圆拟合 {0}", cr.Value));

                        if (cfg.NominalRadius > 0 &&
                            Math.Abs(cr.Value.Radius - cfg.NominalRadius) > cfg.RadiusTolerance)
                        {
                            result.Messages.Add(string.Format("半径超差：实际 {0:F2}，标称 {1:F2}±{2:F2}",
                                cr.Value.Radius, cfg.NominalRadius, cfg.RadiusTolerance));
                            pass = false;
                        }
                    }
                    else
                    {
                        result.Messages.Add("圆拟合失败：" + cr.Message);
                        pass = false;
                    }
                }

                if (result.Messages.Count == 0)
                    result.Messages.Add("未启用任何检测项");
            }
            catch (Exception ex)
            {
                pass = false;
                result.Messages.Add("检测异常：" + ex.Message);
            }

            sw.Stop();
            result.ElapsedMs = sw.ElapsedMilliseconds;
            result.Passed = pass;

            // 汇总：优先展示第一个失败原因
            string failed = null;
            foreach (string m in result.Messages)
            {
                if (m.EndsWith("NG") || m.Contains("失败") || m.Contains("超差") || m.Contains("不符") || m.Contains("异常"))
                {
                    failed = m;
                    break;
                }
            }
            result.Summary = pass ? "全部检测项通过" : (failed ?? "存在不合格项");

            return OpResult<InspectionResult>.Ok(result);
        }
    }
}
