using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using VisionPlatform.Camera;
using VisionPlatform.Core;
using VisionPlatform.Native;
using VisionSystem.Controls.Geometry;

namespace VisionPlatform.Flow
{
    /// <summary>一次流程运行的结果。</summary>
    public sealed class FlowRunResult
    {
        /// <summary>流程是否成功执行（与检测结论 Passed 不同）。</summary>
        public bool Success { get; set; }

        /// <summary>失败原因（Success=false 时有效）。</summary>
        public string Message { get; set; }

        /// <summary>检测结论。</summary>
        public bool Passed { get; set; }

        /// <summary>结论摘要。</summary>
        public string Summary { get; set; }

        /// <summary>叠加图形（图像坐标，全链汇总）。</summary>
        public List<IGeometryShape> Shapes { get; set; }

        /// <summary>逐节点进度（供画布状态标注与预览区回看）。</summary>
        public List<FlowNodeProgress> Progress { get; set; }

        /// <summary>总耗时（毫秒）。</summary>
        public int ElapsedMs { get; set; }

        public FlowRunResult()
        {
            Message = "";
            Summary = "";
            Shapes = new List<IGeometryShape>();
            Progress = new List<FlowNodeProgress>();
        }
    }

    /// <summary>
    /// 流程执行器：会话式内核。
    /// <para>三种运行模式共用一套内核：<c>Begin()</c> 建立会话 → 反复 <c>StepNext()</c>
    /// 逐节点执行 → <c>Finish()</c> 产出结果；<see cref="Run"/> 是这三者的组合，
    /// <strong>对外签名与 FlowOutputChannel 链路完全不动</strong>。</para>
    /// </summary>
    /// <remarks>
    /// 自建独立算法上下文（与 AppContext.Algo 隔离，避免污染循环检测正在用的模板）。
    /// </remarks>
    public sealed class FlowRunner : IDisposable
    {
        private readonly VisionAlgo _algo;
        private readonly Dictionary<string, double[]> _autoRefs;
        private bool _disposed;

        // 匹配模型训练缓存（P0-1 / P0-2）：
        //   _models          按节点 Id 缓存「模板图 + 训练签名」，实现「训练一次、匹配多次」；
        //   _shapeModelOwner 当前占用原生「形状模型」槽位的节点 Id（槽位唯一，被抢需先释放再重训）；
        //   _nccModelOwner   当前占用原生「模板」槽位的节点 Id（train 即覆盖，仅用于判断是否需重训）。
        private readonly Dictionary<string, TrainedModel> _models = new Dictionary<string, TrainedModel>();
        private string _shapeModelOwner;
        private string _nccModelOwner;

        /// <summary>结果产出事件（FlowForm 转发给 FlowOutputChannel）。</summary>
        public event EventHandler<FlowOutputEventArgs> OutputReady;

        public FlowRunner()
        {
            _algo = new VisionAlgo();
            _autoRefs = new Dictionary<string, double[]>();
        }

        /// <summary>
        /// 清空全部「Auto 基准」缓存。位置修正节点的 <c>RefSource=Auto</c> 基准按节点 Id
        /// 缓存在执行器内存中（<strong>不写回节点参数</strong>），换型号 / 重新示教时调用本方法。
        /// </summary>
        public void ClearAutoReferences()
        {
            _autoRefs.Clear();
        }

        // =====================================================================
        //  会话式内核
        // =====================================================================

        /// <summary>
        /// 建立一次运行会话：校验单链拓扑并准备上下文。
        /// 失败时返回 <see cref="FlowRunSession.Aborted"/> = true 的会话，原因见
        /// <see cref="FlowRunSession.ErrorMessage"/>。
        /// </summary>
        /// <param name="flow">待执行流程。</param>
        /// <param name="camera">取图相机；图像源为「本地图片」时允许为 null。</param>
        /// <param name="captureFrames">是否为每个节点保存输出帧副本（单步预览需要）。</param>
        public FlowRunSession Begin(FlowDefinition flow, ICamera camera, bool captureFrames = false)
        {
            var s = new FlowRunSession();
            s.Flow = flow;
            s.Camera = camera;
            s.CaptureFrames = captureFrames;

            if (flow == null)
            {
                s.Aborted = true;
                s.ErrorMessage = "流程为空";
                return s;
            }

            if (camera == null && NeedsCamera(flow))
            {
                s.Aborted = true;
                s.ErrorMessage = "未绑定相机或相机不可用";
                return s;
            }

            // 【端口化 T05 接线】换成端口级拓扑排序 FlowOrder.Build：
            //   ① 不再要求「单链」——输出扇出、多路汇聚都能跑；
            //   ② 孤立节点不再是致命错误，排在末尾照常执行；
            //   ③ 错误文案由 FlowOrder 给出（成环会带上环上的节点名），这里原样透传。
            // 旧的节点级单链解析 TryBuildChain 已删除；static Validate 也已改走 FlowValidator（同一套 Error 级口径）。
            List<FlowNode> chain;
            string chainError;
            if (!FlowOrder.Build(flow, out chain, out chainError))
            {
                s.Aborted = true;
                s.ErrorMessage = chainError;
                return s;
            }

            s.Chain = chain;
            s.Watch = Stopwatch.StartNew();
            return s;
        }

        /// <summary>
        /// 执行下一个节点并返回其进度（含自身耗时与<strong>自身</strong>叠加图形）。
        /// 链已跑完或会话已中止时返回 null。
        /// </summary>
        public FlowNodeProgress StepNext(FlowRunSession session)
        {
            if (session == null) return null;
            if (session.Aborted) return null;
            if (session.Chain == null || session.Cursor >= session.Chain.Count) return null;

            FlowNode node = session.Chain[session.Cursor];

            var p = new FlowNodeProgress();
            p.NodeId = node.Id;
            p.NodeName = node.Name;
            p.Index = session.Cursor + 1;
            p.Total = session.Chain.Count;
            p.Status = FlowNodeStatus.Running;
            session.Progress.Add(p);

            FlowNodeType type;
            if (!node.TryGetType(out type))
            {
                // 未知节点类型：跳过而不是让整条流程失败（旧配置 / 后续版本兼容）
                p.Status = FlowNodeStatus.Skipped;
                p.Summary = "未知节点类型：" + node.Type + "（已跳过）";
                session.Cursor++;
                return p;
            }

            var sw = Stopwatch.StartNew();
            bool ok;
            try
            {
                ok = ExecuteNode(session, node, type, p);
            }
            catch (Exception ex)
            {
                ok = false;
                p.Summary = node.Name + "：执行异常（" + ex.Message + "）";
            }
            sw.Stop();

            p.ElapsedMs = (int)sw.ElapsedMilliseconds;

            // 节点内部已显式设置 Skipped 时保持原状，不要覆盖成 Ok
            if (p.Status == FlowNodeStatus.Running)
                p.Status = ok ? FlowNodeStatus.Ok : FlowNodeStatus.Ng;

            if (!ok) session.Passed = false;

            if (session.CaptureFrames && session.Current != null)
            {
                try { p.Frame = (Bitmap)session.Current.Clone(); }
                catch (Exception) { p.Frame = null; }
            }

            if (!string.IsNullOrEmpty(p.Summary)) session.Summaries.Add(p.Summary);
            session.Cursor++;
            return p;
        }

        /// <summary>收尾并产出结果。不释放会话（帧所有权仍归会话，由调用方决定何时释放）。</summary>
        public FlowRunResult Finish(FlowRunSession session)
        {
            var result = new FlowRunResult();
            if (session == null)
            {
                result.Message = "会话为空";
                return result;
            }

            if (session.Watch != null) session.Watch.Stop();
            result.ElapsedMs = session.Watch != null ? (int)session.Watch.ElapsedMilliseconds : 0;
            result.Progress = session.Progress;
            result.Shapes = CollectShapes(session);

            if (session.Aborted && !string.IsNullOrEmpty(session.ErrorMessage))
            {
                result.Message = session.ErrorMessage;
                result.Summary = string.Join("；", session.Summaries);
                return result;
            }

            result.Success = true;
            result.Passed = session.Passed;
            result.Summary = string.Join("；", session.Summaries);
            return result;
        }

        /// <summary>
        /// 执行一条流程。成功时返回结果并触发 <see cref="OutputReady"/>（携带图像，所有权移交订阅者）。
        /// 对外签名与行为保持不变，等价于 Begin + 循环 StepNext + Finish。
        /// </summary>
        public FlowRunResult Run(FlowDefinition flow, ICamera camera)
        {
            FlowRunSession session = null;

            try
            {
                session = Begin(flow, camera);

                while (!session.IsDone)
                {
                    if (StepNext(session) == null) break;
                }

                FlowRunResult result = Finish(session);

                if (result.Success)
                {
                    // 发布结果：图像所有权移交订阅者
                    var args = new FlowOutputEventArgs
                    {
                        ViewIndex = flow != null ? flow.ViewIndex : 1,
                        Frame = session.Current,
                        Shapes = result.Shapes,
                        Passed = result.Passed,
                        Summary = result.Summary,
                        CameraName = (camera != null && camera.Info != null) ? camera.Info.Name : "本地图片",

                        // 【本轮追加】多流程并行时用于区分结果归属（FR-PAR-04）
                        FlowId = flow != null ? (flow.Id ?? "") : "",
                        FlowName = flow != null ? (flow.Name ?? "") : "",
                        Product = flow != null ? (flow.Product ?? "") : ""
                    };

                    EventHandler<FlowOutputEventArgs> handler = OutputReady;
                    if (handler != null)
                    {
                        handler(this, args);
                        session.Current = null;   // 已交出，会话不再释放
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                return new FlowRunResult { Message = "流程执行异常：" + ex.Message };
            }
            finally
            {
                if (session != null) session.Dispose();
            }
        }

        /// <summary>汇总全链叠加图形。</summary>
        private static List<IGeometryShape> CollectShapes(FlowRunSession session)
        {
            var all = new List<IGeometryShape>();
            foreach (FlowNodeProgress p in session.Progress)
            {
                if (p == null || p.Shapes == null) continue;
                all.AddRange(p.Shapes);
            }
            return all;
        }

        // =====================================================================
        //  节点执行
        // =====================================================================

        /// <summary>执行单个节点。返回该节点是否通过（false 会让整链结论为 NG）。</summary>
        private bool ExecuteNode(FlowRunSession s, FlowNode node, FlowNodeType type, FlowNodeProgress p)
        {
            switch (type)
            {
                case FlowNodeType.ImageSource:  return RunImageSource(s, node, p);
                case FlowNodeType.Preprocess:   return RunPreprocess(s, node, p);
                case FlowNodeType.PositionCorrect: return RunPositionCorrect(s, node, p);
                case FlowNodeType.ShapeMatch:   return RunShapeMatch(s, node, p);
                case FlowNodeType.TemplateMatch: return RunTemplateMatch(s, node, p);
                case FlowNodeType.Blob:         return RunBlob(s, node, p);
                case FlowNodeType.LineCaliper:  return RunLineCaliper(s, node, p);
                case FlowNodeType.CircleCaliper: return RunCircleCaliper(s, node, p);
                case FlowNodeType.Sharpness:    return RunSharpness(s, node, p);
                case FlowNodeType.FitCircle:    return RunFitCircle(s, node, p);
                case FlowNodeType.FitLine:      return RunFitLine(s, node, p);
                case FlowNodeType.ResultOutput: return RunResultOutput(s, node, p);
                default:
                    p.Status = FlowNodeStatus.Skipped;
                    p.Summary = node.Name + "：暂不支持的节点类型";
                    return true;
            }
        }

        // ---------------------- 图像源（FR-ALG-01）----------------------

        private bool RunImageSource(FlowRunSession s, FlowNode node, FlowNodeProgress p)
        {
            string source = node.GetString("Source", "Camera");
            Bitmap raw = null;

            Bitmap work = null;
            bool workIsClone = false;

            try
            {
                if (string.Equals(source, "File", StringComparison.OrdinalIgnoreCase))
                {
                    string path = node.GetString("FilePath", "");
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        p.Summary = "图像源：未选择本地图片文件";
                        return false;
                    }
                    if (!System.IO.File.Exists(path))
                    {
                        p.Summary = "图像源：文件不存在（" + path + "）";
                        return false;
                    }

                    try
                    {
                        // 先读后拷：避免 Bitmap 长期持有文件句柄导致文件被占用
                        using (var probe = Image.FromFile(path))
                            raw = new Bitmap(probe);
                    }
                    catch (Exception ex)
                    {
                        p.Summary = "图像源：读取图片失败（" + ex.Message + "）";
                        return false;
                    }
                }
                else
                {
                    if (s.Camera == null)
                    {
                        p.Summary = "图像源：未绑定相机";
                        s.Aborted = true;
                        s.ErrorMessage = "未绑定相机或相机不可用";
                        return false;
                    }

                    int timeout = node.GetInt("TimeoutMs", 3000);
                    if (timeout <= 0) timeout = 3000;

                    // 【并行铁律】取帧必须走 CameraGrabGate（FR-MC-04）：
                    // 相机 SDK 的原生句柄不是线程安全的，多流程并行时并发 GrabOne 会崩在厂商 DLL 里。
                    string flowName = (s.Flow != null && !string.IsNullOrEmpty(s.Flow.Name)) ? s.Flow.Name : "";
                    OpResult<Bitmap> fr = CameraGrabGate.GrabOne(s.Camera, timeout, flowName);
                    if (!fr.Success || fr.Value == null)
                    {
                        // 取帧失败是致命错误：后续节点全部无输入，直接中止
                        p.Summary = "取帧失败：" + fr.Message;
                        s.Aborted = true;
                        s.ErrorMessage = "取帧失败：" + fr.Message;
                        return false;
                    }
                    raw = fr.Value;
                }

                // --- ROI 裁剪（宽高为 0 = 全图）---
                int roiX = node.GetInt("RoiX", 0);
                int roiY = node.GetInt("RoiY", 0);
                int roiW = node.GetInt("RoiW", 0);
                int roiH = node.GetInt("RoiH", 0);

                work = raw;
                workIsClone = false;
                bool cropped = false;

                if (roiW > 0 && roiH > 0)
                {
                    Rectangle want = new Rectangle(roiX, roiY, roiW, roiH);
                    Rectangle real = Rectangle.Intersect(want, new Rectangle(0, 0, raw.Width, raw.Height));
                    if (real.Width > 0 && real.Height > 0)
                    {
                        work = raw.Clone(real, raw.PixelFormat);
                        workIsClone = true;
                        cropped = true;
                        // 记录实际裁剪偏移（可能已被图像边界裁剪过），下游 ROI 换算要用
                        roiX = real.X;
                        roiY = real.Y;
                    }
                }

                // --- 旋转 0 / 90 / 180 / 270 ---
                int rotate = node.GetInt("Rotate", 0);
                if (rotate == 90) work.RotateFlip(RotateFlipType.Rotate90FlipNone);
                else if (rotate == 180) work.RotateFlip(RotateFlipType.Rotate180FlipNone);
                else if (rotate == 270) work.RotateFlip(RotateFlipType.Rotate270FlipNone);

                // --- 水平镜像 ---
                if (node.GetBool("Mirror", false))
                    work.RotateFlip(RotateFlipType.RotateNoneFlipX);

                s.ReplaceFrame(work);          // 所有权移交会话
                workIsClone = false;           // 已交出，不再由本方法释放

                if (s.SourceFrame != null) { s.SourceFrame.Dispose(); s.SourceFrame = null; }
                s.SourceFrame = (Bitmap)work.Clone();

                s.OffsetX = cropped ? roiX : 0.0;
                s.OffsetY = cropped ? roiY : 0.0;

                p.Summary = string.Format(CultureInfo.InvariantCulture, "取图 {0}x{1}",
                                          work.Width, work.Height);
                return true;
            }
            catch
            {
                // 异常路径：只释放本方法自己造出来的裁剪副本
                if (workIsClone && work != null) work.Dispose();
                throw;
            }
            finally
            {
                // 未裁剪时 raw 就是交给会话的工作帧，绝不能释放；
                // 裁剪过则 raw 已完成使命，必须释放，否则每帧泄漏一张原图。
                if (raw != null && !object.ReferenceEquals(raw, work)) raw.Dispose();
            }
        }

        // ---------------------- 预处理 ----------------------

        private bool RunPreprocess(FlowRunSession s, FlowNode node, FlowNodeProgress p)
        {
            if (s.Current == null)
            {
                p.Status = FlowNodeStatus.Skipped;
                p.Summary = "预处理：无输入帧";
                return true;
            }

            var pr = _algo.Preprocess(s.Current, BuildPreprocess(node));
            if (!pr.Success)
            {
                p.Summary = "预处理失败：" + pr.Message;
                return false;
            }

            s.ReplaceFrame(pr.Value);
            p.Summary = "预处理完成";
            return true;
        }

        // ---------------------- 位置修正（FR-ALG-08 / 09 / 10）----------------------

        private bool RunPositionCorrect(FlowRunSession s, FlowNode node, FlowNodeProgress p)
        {
            string refSource = node.GetString("RefSource", "Auto");

            double rx, ry, ra;
            if (string.Equals(refSource, "Manual", StringComparison.OrdinalIgnoreCase))
            {
                // 手动基准由用户按【原始图坐标】填写，换算到工作帧坐标
                rx = node.GetDouble("RefX", 0) - s.OffsetX;
                ry = node.GetDouble("RefY", 0) - s.OffsetY;
                ra = node.GetDouble("RefAngle", 0);
            }
            else
            {
                if (!s.HasPose)
                {
                    // 【P0-3 修复】Auto 且上游无定位结果：Warning + 跳过修正，不再判 NG。
                    //
                    // 为什么改：位置修正不读 s.Current，端口化 + 拓扑排序上线后，
                    // 它的执行顺序完全由 (X, Y, Id) 决定 —— 排在匹配节点【前面】是合法且常见的布局，
                    // 此时 s.HasPose 必然为 false。旧逻辑直接判 NG，等于「用户摆放顺序不同就 NG」，
                    // 这是本次改造引入的回归风险。
                    //
                    // 为什么不是「给 Image 加必填输入端子」来强制它排在后面：
                    // 那会让所有没连该端子的旧配置集体报 E001 级错误 ——
                    // 和 PositionCorrect 的 Pose 那次是完全同一个坑，不重蹈覆辙。
                    //
                    // 于是分工是：校验期由 FlowValidator 的 W005 提前提示用户（不是拦他），
                    // 运行期这里跳过本次修正、不判 NG，让流程继续跑。
                    p.Status = FlowNodeStatus.Skipped;
                    p.Summary = "位置修正：上游无可用定位结果，本次跳过修正（未判 NG）";
                    return true;
                }

                // Auto 基准按节点 Id 缓存在执行器内存里：首次取上游结果，之后保持不变
                // （等价于「示教一次」），这样后续帧才能得到有意义的偏差量。
                double[] latched;
                if (_autoRefs.TryGetValue(node.Id, out latched))
                {
                    rx = latched[0];
                    ry = latched[1];
                    ra = latched[2];
                }
                else
                {
                    rx = s.PoseCx;
                    ry = s.PoseCy;
                    ra = s.PoseAngle;
                    _autoRefs[node.Id] = new[] { rx, ry, ra };
                }
            }

            double dx = s.HasPose ? (s.PoseCx - rx) : 0.0;
            double dy = s.HasPose ? (s.PoseCy - ry) : 0.0;
            double dAngle = s.HasPose ? (s.PoseAngle - ra) : 0.0;
            double dScale = s.HasPose ? s.PoseScale : 1.0;

            // EnableAngle = 0：只平移不旋转
            if (!node.GetBool("EnableAngle", true)) dAngle = 0.0;

            // 覆盖式写入（链内多个位置修正节点以最后一个为准，不累加）
            s.Transform.Set(rx, ry, ra, dx, dy, dAngle, dScale);

            // 可视化：基准十字（青色）
            foreach (IGeometryShape g in FlowShapes.Crosshair(rx, ry)) p.Shapes.Add(g);

            p.Summary = string.Format(CultureInfo.InvariantCulture,
                "位置修正：dX={0:F1} dY={1:F1} dA={2:F2}°", dx, dy, dAngle);

            // 超差判定（0 = 不限）
            double maxShiftX = node.GetDouble("MaxShiftX", 0);
            double maxShiftY = node.GetDouble("MaxShiftY", 0);
            double maxAngle = node.GetDouble("MaxAngle", 0);
            double maxScaleDev = node.GetDouble("MaxScaleDev", 0);

            if (maxShiftX > 0 && Math.Abs(dx) > maxShiftX)
            {
                p.Summary = string.Format(CultureInfo.InvariantCulture,
                    "位置修正：偏移超差 dX={0:F1} > {1:F1}", dx, maxShiftX);
                return false;
            }
            if (maxShiftY > 0 && Math.Abs(dy) > maxShiftY)
            {
                p.Summary = string.Format(CultureInfo.InvariantCulture,
                    "位置修正：偏移超差 dY={0:F1} > {1:F1}", dy, maxShiftY);
                return false;
            }
            if (maxAngle > 0 && Math.Abs(dAngle) > maxAngle)
            {
                p.Summary = string.Format(CultureInfo.InvariantCulture,
                    "位置修正：角度超差 dA={0:F2} > {1:F2}", dAngle, maxAngle);
                return false;
            }
            if (maxScaleDev > 0 && Math.Abs(dScale - 1.0) > maxScaleDev)
            {
                p.Summary = string.Format(CultureInfo.InvariantCulture,
                    "位置修正：缩放超差 dS={0:F3} > {1:F3}", dScale, maxScaleDev);
                return false;
            }

            return true;
        }

        // ---------------------- 形状匹配 ----------------------

        private bool RunShapeMatch(FlowRunSession s, FlowNode node, FlowNodeProgress p)
        {
            if (s.Current == null)
            {
                p.Status = FlowNodeStatus.Skipped;
                p.Summary = "形状匹配：无输入帧";
                return true;
            }

            var train = new ShapeTrainParam
            {
                Contrast = node.GetDouble("Contrast", 30),
                MaxFeatures = node.GetInt("MaxFeatures", 1500),
                MinFeatureDist = node.GetDouble("MinFeatureDist", 2)
            };

            Rectangle template = ApplyRoi(s, node, "TemplateX", "TemplateY", "TemplateW", "TemplateH");
            string sig = ShapeSignature(template, train);

            TrainedModel cache;
            bool cached = _models.TryGetValue(node.Id, out cache);
            bool signatureSame = cached && cache != null
                                 && string.Equals(cache.Signature, sig, StringComparison.Ordinal)
                                 && cache.Template != null;
            bool slotMine = string.Equals(_shapeModelOwner, node.Id, StringComparison.Ordinal);

            // 需要重训的三种情况：① 尚未训练；② 训练参数 / 模板 ROI 变了；③ 原生槽位被别的匹配节点抢占。
            if (!signatureSame || !slotMine)
            {
                // 决定「用哪一帧训练」——这是 P0-2 的关键：
                //   · 首次训练 / 参数或 ROI 变了 → 用【当前帧】的模板 ROI（等同「示教一次」）
                //   · 只是槽位被别人抢走 → 用【缓存的模板图】重训
                // ⚠️ 绝不能拿当前帧当模板再在同一帧上匹配：那是自匹配，得分恒≈1.0，
                //    MinScore 形同虚设，等于这个算法根本没在工作。
                Bitmap trainFrame = signatureSame ? cache.Template : s.Current;
                Rectangle trainRoi = signatureSame ? Rectangle.Empty : template;

                // 槽位上若已是别人的模型，先显式释放再训练，避免原生侧叠加占用（句柄内存泄漏）
                if (!string.IsNullOrEmpty(_shapeModelOwner) && !slotMine)
                    _algo.ClearShapeModel();

                var cr = _algo.CreateShapeModel(trainFrame, trainRoi, train);
                if (cr == null || !cr.Success)
                {
                    p.Summary = "形状匹配：训练失败(" + (cr != null ? cr.Message : "无返回") + ")";
                    return false;
                }

                // 缓存训练基准：只在「首次 / 参数变更」这两种情况下用当前帧刷新模板图
                if (!signatureSame)
                {
                    if (cache == null)
                    {
                        cache = new TrainedModel();
                        _models[node.Id] = cache;
                    }
                    if (cache.Template != null) cache.Template.Dispose();
                    cache.Template = CropTemplate(s.Current, template);
                    cache.Signature = sig;
                }

                _shapeModelOwner = node.Id;
            }

            var param = new ShapeParam
            {
                AngleStart = node.GetDouble("AngleStart", -10),
                AngleEnd = node.GetDouble("AngleEnd", 10),
                AngleStep = node.GetDouble("AngleStep", 1),
                ScaleMin = node.GetDouble("ScaleMin", 1),
                ScaleMax = node.GetDouble("ScaleMax", 1),
                ScaleStep = node.GetDouble("ScaleStep", 0),
                PyramidLevels = node.GetInt("PyramidLevels", 4),
                MinScore = node.GetDouble("MinScore", 0.7),
                MaxMatches = node.GetInt("MaxMatches", 1),
                MaxOverlap = node.GetDouble("MaxOverlap", 0.5),
                Subpixel = node.GetBool("Subpixel", true),
                Greediness = node.GetDouble("Greediness", 0.9)
            };

            Rectangle search = ApplyRoi(s, node, "SearchX", "SearchY", "SearchW", "SearchH");

            var fr = _algo.FindShapeModel(s.Current, param, search);
            if (!fr.Success)
            {
                p.Summary = "形状匹配失败：" + fr.Message;
                return false;
            }

            List<ShapeMatchResult> matches = fr.Value ?? new List<ShapeMatchResult>();
            for (int i = 0; i < matches.Count; i++)
            {
                ShapeMatchResult m = matches[i];
                p.Shapes.Add(FlowShapes.MatchBox(m.Cx, m.Cy, m.Width, m.Height, m.Angle, i == 0));
            }

            if (matches.Count == 0)
            {
                p.Summary = "形状匹配：未找到目标";
                return false;
            }

            // 记录实时位姿，供下游位置修正节点取基准
            s.SetPose(matches[0].Cx, matches[0].Cy, matches[0].Angle, matches[0].Scale);

            p.Summary = string.Format(CultureInfo.InvariantCulture, "形状匹配：{0} 个，最高 {1:F3}",
                                      matches.Count, matches[0].Score);
            return true;
        }

        // ---------------------- 模板匹配 NCC（FR-ALG-04）----------------------

        private bool RunTemplateMatch(FlowRunSession s, FlowNode node, FlowNodeProgress p)
        {
            if (s.Current == null)
            {
                p.Status = FlowNodeStatus.Skipped;
                p.Summary = "模板匹配：无输入帧";
                return true;
            }

            double minScore = node.GetDouble("MinScore", 0.8);

            // 训练：在整帧上按模板 ROI 训练（不裁剪，坐标无需换算）
            Rectangle template = ApplyRoi(s, node, "TemplateX", "TemplateY", "TemplateW", "TemplateH");
            string sig = TemplateSignature(template);

            TrainedModel cache;
            bool cached = _models.TryGetValue(node.Id, out cache);
            bool signatureSame = cached && cache != null
                                 && string.Equals(cache.Signature, sig, StringComparison.Ordinal)
                                 && cache.Template != null;
            bool slotMine = string.Equals(_nccModelOwner, node.Id, StringComparison.Ordinal);

            // 【P0-1 修复】模板【训练一次、匹配多次】。
            // 旧代码每帧都拿【当前帧】当模板再去同一帧上匹配 → 得分恒≈1.0，MinScore 过滤形同虚设，
            // 等于这个算法根本没在工作。重训只在这三种情况下发生：
            //   ① 尚未训练；② 模板 ROI 变了；③ 原生模板槽位被别的模板匹配节点抢占。
            if (!signatureSame || !slotMine)
            {
                // 用缓存的模板图重训（槽位被抢时），否则用当前帧做「示教帧」
                Bitmap trainFrame = signatureSame ? cache.Template : s.Current;
                Rectangle trainRoi = signatureSame ? Rectangle.Empty : template;

                OpResult tr = _algo.TrainTemplate(trainFrame, trainRoi);
                if (tr == null || !tr.Success)
                {
                    p.Summary = "模板匹配：未训练模板（" + (tr != null ? tr.Message : "无返回") + "）";
                    return false;
                }

                if (!signatureSame)
                {
                    if (cache == null)
                    {
                        cache = new TrainedModel();
                        _models[node.Id] = cache;
                    }
                    if (cache.Template != null) cache.Template.Dispose();
                    cache.Template = CropTemplate(s.Current, template);
                    cache.Signature = sig;
                }

                _nccModelOwner = node.Id;
            }

            // 搜索：有搜索 ROI 时先裁剪，结果坐标【必须加回左上角偏移】再画图形
            Rectangle search = ApplyRoi(s, node, "SearchX", "SearchY", "SearchW", "SearchH");
            bool useRoi = search.Width > 0 && search.Height > 0
                          && (search.X != 0 || search.Y != 0
                              || search.Width != s.Current.Width || search.Height != s.Current.Height);

            Bitmap probe = s.Current;
            Bitmap cropped = null;
            try
            {
                if (useRoi)
                {
                    Rectangle real = Rectangle.Intersect(search, new Rectangle(0, 0, s.Current.Width, s.Current.Height));
                    if (real.Width > 0 && real.Height > 0)
                    {
                        cropped = s.Current.Clone(real, s.Current.PixelFormat);
                        probe = cropped;
                        search = real;
                    }
                    else
                    {
                        useRoi = false;
                    }
                }

                OpResult<MatchResult> mr = _algo.MatchTemplate(probe);
                if (!mr.Success)
                {
                    p.Summary = "模板匹配失败：" + mr.Message;
                    return false;
                }

                MatchResult r = mr.Value;
                double score = r != null ? r.Score : 0.0;

                if (r == null || score < minScore)
                {
                    p.Summary = string.Format(CultureInfo.InvariantCulture,
                        "模板匹配：未找到目标，最高得分 {0:F3}", score);
                    return false;
                }

                // 裁剪搜索时把结果坐标加回搜索区左上角
                double bx = r.X + (useRoi ? search.X : 0);
                double by = r.Y + (useRoi ? search.Y : 0);

                var box = new Rectangle((int)Math.Round(bx), (int)Math.Round(by),
                                        Math.Max(1, (int)Math.Round(r.Width)),
                                        Math.Max(1, (int)Math.Round(r.Height)));
                p.Shapes.Add(FlowShapes.NccBox(box, true));

                // 记录实时位姿（NCC 不支持旋转 / 缩放，角度恒 0、缩放恒 1）
                s.SetPose(bx + r.Width * 0.5, by + r.Height * 0.5, 0.0, 1.0);

                p.Summary = string.Format(CultureInfo.InvariantCulture,
                    "模板匹配：得分 {0:F3} @({1:F0},{2:F0})", score, bx, by);
                return true;
            }
            finally
            {
                if (cropped != null) cropped.Dispose();
            }
        }

        // ---------------------- Blob 分析（FR-ALG-12 / 13）----------------------

        private bool RunBlob(FlowRunSession s, FlowNode node, FlowNodeProgress p)
        {
            if (s.Current == null)
            {
                p.Status = FlowNodeStatus.Skipped;
                p.Summary = "Blob：无输入帧";
                return true;
            }

            string binMode = node.GetString("BinMode", "Inherit");
            string polarity = node.GetString("Polarity", "Bright");
            bool dark = string.Equals(polarity, "Dark", StringComparison.OrdinalIgnoreCase);

            int threshold = node.GetInt("Threshold", 128);
            double minArea = node.GetDouble("MinArea", 100);
            double maxArea = node.GetDouble("MaxArea", 0);
            double minWidth = node.GetDouble("MinWidth", 0);
            double minHeight = node.GetDouble("MinHeight", 0);
            double minCircularity = node.GetDouble("MinCircularity", 0);
            int maxCount = node.GetInt("MaxCount", 50);

            Bitmap work = null;
            try
            {
                Bitmap input = s.Current;

                if (string.Equals(binMode, "Otsu", StringComparison.OrdinalIgnoreCase))
                {
                    work = TransformGray(input, true, OtsuThreshold(input), dark);
                    input = work;
                }
                else if (string.Equals(binMode, "Manual", StringComparison.OrdinalIgnoreCase))
                {
                    work = TransformGray(input, true, threshold, dark);
                    input = work;
                }
                else if (dark)
                {
                    // Inherit 且统计暗目标：整帧取反后再交给原生按 128 二值化
                    work = TransformGray(input, false, 0, true);
                    input = work;
                }

                // 先多取一些，本地按宽高 / 圆度过滤后再截断到 MaxCount
                int cap = maxCount > 0 ? Math.Min(maxCount * 4 + 8, 256) : 256;

                var br = _algo.AnalyzeBlobs(input, minArea, maxArea, cap);
                if (!br.Success)
                {
                    p.Summary = "Blob 失败：" + br.Message;
                    return false;
                }

                var kept = new List<BlobResult>();
                foreach (BlobResult b in br.Value)
                {
                    if (minWidth > 0 && b.Width < minWidth) continue;
                    if (minHeight > 0 && b.Height < minHeight) continue;
                    if (minCircularity > 0 && Circularity(b) < minCircularity) continue;
                    kept.Add(b);
                }

                // 按面积降序（SortBy 目前只支持 Area）
                kept.Sort(delegate (BlobResult a, BlobResult b) { return b.Area.CompareTo(a.Area); });
                if (maxCount > 0 && kept.Count > maxCount) kept.RemoveRange(maxCount, kept.Count - maxCount);

                foreach (BlobResult b in kept)
                    p.Shapes.Add(FlowShapes.BlobMarker(b.Cx, b.Cy, b.Area));

                // 判定：期望个数范围 + 单个面积范围（0 = 不限）
                double expectMin = node.GetDouble("ExpectMin", 0);
                double expectMax = node.GetDouble("ExpectMax", 0);
                double areaMin = node.GetDouble("AreaMin", 0);
                double areaMax = node.GetDouble("AreaMax", 0);

                string text = "Blob：" + kept.Count + " 个";

                if (expectMax > 0 && kept.Count > expectMax)
                {
                    p.Summary = string.Format(CultureInfo.InvariantCulture,
                        "Blob：{0} 个，期望 {1}~{2}", kept.Count, expectMin, expectMax);
                    return false;
                }
                if (expectMin > 0 && kept.Count < expectMin)
                {
                    p.Summary = string.Format(CultureInfo.InvariantCulture,
                        "Blob：{0} 个，期望 {1}~{2}", kept.Count, expectMin, expectMax);
                    return false;
                }

                foreach (BlobResult b in kept)
                {
                    if (areaMin > 0 && b.Area < areaMin)
                    {
                        p.Summary = string.Format(CultureInfo.InvariantCulture,
                            "Blob：面积 {0:F0} 小于下限 {1:F0}", b.Area, areaMin);
                        return false;
                    }
                    if (areaMax > 0 && b.Area > areaMax)
                    {
                        p.Summary = string.Format(CultureInfo.InvariantCulture,
                            "Blob：面积 {0:F0} 超出上限 {1:F0}", b.Area, areaMax);
                        return false;
                    }
                }

                p.Summary = text;
                return true;
            }
            finally
            {
                if (work != null) work.Dispose();
            }
        }

        /// <summary>
        /// 圆度（填充度口径）：<c>面积 / (外接矩形宽 × 高)</c>，取值 0~1。
        /// 圆≈0.785，正方形=1.0。原生 VA_Blob 只给外接矩形宽高、不给周长，
        /// 因此用填充度作为圆度的可计算近似（面板上的提示文本同样按此口径说明）。
        /// </summary>
        private static double Circularity(BlobResult b)
        {
            double wh = b.Width * b.Height;
            if (wh <= 0) return 0.0;
            double v = b.Area / wh;
            if (v < 0.0) v = 0.0;
            if (v > 1.0) v = 1.0;
            return v;
        }

        // ---------------------- 直线卡尺（FR-ALG-14 / 15）----------------------

        private bool RunLineCaliper(FlowRunSession s, FlowNode node, FlowNodeProgress p)
        {
            if (s.Current == null)
            {
                p.Status = FlowNodeStatus.Skipped;
                p.Summary = "直线卡尺：无输入帧";
                return true;
            }

            // 扫描线段是几何属性（图像坐标），需先经位置修正变换
            PointF a = s.Transform.ApplyToPoint(node.GetDouble("X1", 100) - s.OffsetX,
                                                node.GetDouble("Y1", 100) - s.OffsetY);
            PointF b = s.Transform.ApplyToPoint(node.GetDouble("X2", 300) - s.OffsetX,
                                                node.GetDouble("Y2", 100) - s.OffsetY);

            var cp = new CaliperParam
            {
                X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y,
                CaliperCount = node.GetInt("CaliperCount", 10),
                ProjectionLength = node.GetDouble("ProjectionLength", 20),
                Polarity = node.GetInt("Polarity", 0),
                EdgeThreshold = node.GetDouble("EdgeThreshold", 30),
                FilterHalfWidth = node.GetInt("FilterHalfWidth", 2),
                SelectMode = node.GetInt("SelectMode", 0),
                MinEdgeCount = node.GetInt("MinEdgeCount", 3),
                RejectOutliers = node.GetInt("RejectOutliers", 1),
                OutlierTolerance = node.GetDouble("OutlierTolerance", 2)
            };

            OpResult<CaliperLineResult> lr = _algo.LineCaliper(s.Current, cp);
            if (!lr.Success)
            {
                // OpResult.Message 形如「有效边缘点不足：有效边缘点 2 < 3」，取冒号后段
                p.Summary = "直线卡尺：" + Tail(lr.Message);
                return false;
            }

            CaliperLineResult r = lr.Value;

            // 可视化：投影窗口（未命中灰）+ 命中点 + 拟合直线
            p.Shapes.AddRange(FlowShapes.CaliperWindows(r.Points));
            p.Shapes.AddRange(FlowShapes.CaliperHits(r.Points));
            IGeometryShape fit = FlowShapes.CaliperLine(r.Line);
            if (fit != null) p.Shapes.Add(fit);

            // 判定（0 = 不限）
            double lengthMin = node.GetDouble("LengthMin", 0);
            double lengthMax = node.GetDouble("LengthMax", 0);
            double angleMin = node.GetDouble("AngleMin", 0);
            double angleMax = node.GetDouble("AngleMax", 0);
            double maxRms = node.GetDouble("MaxRms", 0);

            if (lengthMax > 0 && r.Length > lengthMax)
            {
                p.Summary = string.Format(CultureInfo.InvariantCulture,
                    "直线卡尺：长度 {0:F1} 超出上限 {1:F1}", r.Length, lengthMax);
                return false;
            }
            if (lengthMin > 0 && r.Length < lengthMin)
            {
                p.Summary = string.Format(CultureInfo.InvariantCulture,
                    "直线卡尺：长度 {0:F1} 小于下限 {1:F1}", r.Length, lengthMin);
                return false;
            }
            if (angleMin != 0 || angleMax != 0)
            {
                if (r.AngleDeg < angleMin || r.AngleDeg > angleMax)
                {
                    p.Summary = string.Format(CultureInfo.InvariantCulture,
                        "直线卡尺：角度 {0:F2}° 超出 [{1:F2}, {2:F2}]", r.AngleDeg, angleMin, angleMax);
                    return false;
                }
            }
            if (maxRms > 0 && r.Rms > maxRms)
            {
                p.Summary = string.Format(CultureInfo.InvariantCulture,
                    "直线卡尺：直线度 {0:F2} 超出上限 {1:F2}", r.Rms, maxRms);
                return false;
            }

            p.Summary = string.Format(CultureInfo.InvariantCulture,
                "直线卡尺：长度 {0:F1} 角度 {1:F2}° RMS {2:F2} 有效点 {3}/{4}",
                r.Length, r.AngleDeg, r.Rms, r.ValidCount,
                r.Points != null ? r.Points.Count : 0);
            return true;
        }

        // ---------------------- 圆卡尺（FR-ALG-16 / 17）----------------------

        private bool RunCircleCaliper(FlowRunSession s, FlowNode node, FlowNodeProgress p)
        {
            if (s.Current == null)
            {
                p.Status = FlowNodeStatus.Skipped;
                p.Summary = "圆卡尺：无输入帧";
                return true;
            }

            double roiCx = node.GetDouble("Cx", 320) - s.OffsetX;
            double roiCy = node.GetDouble("Cy", 240) - s.OffsetY;

            PointF c = s.Transform.ApplyToPoint(roiCx, roiCy);

            var cp = new CaliperParam
            {
                Cx = c.X, Cy = c.Y,
                InnerRadius = node.GetDouble("InnerRadius", 80),
                OuterRadius = node.GetDouble("OuterRadius", 120),
                StartAngle = s.Transform.ApplyToAngle(node.GetDouble("StartAngle", 0)),
                AngleSpan = node.GetDouble("AngleSpan", 360),
                CaliperCount = node.GetInt("CaliperCount", 20),
                ProjectionLength = node.GetDouble("ProjectionLength", 20),
                Polarity = node.GetInt("Polarity", 0),
                EdgeThreshold = node.GetDouble("EdgeThreshold", 30),
                FilterHalfWidth = node.GetInt("FilterHalfWidth", 2),
                SelectMode = node.GetInt("SelectMode", 0),
                MinEdgeCount = node.GetInt("MinEdgeCount", 5),
                RejectOutliers = node.GetInt("RejectOutliers", 1),
                OutlierTolerance = node.GetDouble("OutlierTolerance", 2)
            };

            OpResult<CaliperCircleResult> cr = _algo.CircleCaliper(s.Current, cp);
            if (!cr.Success)
            {
                p.Summary = "圆卡尺：" + Tail(cr.Message);
                return false;
            }

            CaliperCircleResult r = cr.Value;

            p.Shapes.AddRange(FlowShapes.CaliperWindows(r.Points));
            p.Shapes.AddRange(FlowShapes.CaliperHits(r.Points));
            IGeometryShape fit = FlowShapes.CaliperCircle(r.Circle);
            if (fit != null) p.Shapes.Add(fit);

            double diameterMin = node.GetDouble("DiameterMin", 0);
            double diameterMax = node.GetDouble("DiameterMax", 0);
            double roundnessMax = node.GetDouble("RoundnessMax", 0);
            double tolX = node.GetDouble("CenterToleranceX", 0);
            double tolY = node.GetDouble("CenterToleranceY", 0);
            double diameter = r.Diameter;

            if (diameterMax > 0 && diameter > diameterMax)
            {
                p.Summary = string.Format(CultureInfo.InvariantCulture,
                    "圆卡尺：直径 {0:F1} 超出 [{1:F1}, {2:F1}]", diameter, diameterMin, diameterMax);
                return false;
            }
            if (diameterMin > 0 && diameter < diameterMin)
            {
                p.Summary = string.Format(CultureInfo.InvariantCulture,
                    "圆卡尺：直径 {0:F1} 超出 [{1:F1}, {2}]", diameter, diameterMin, "∞");
                return false;
            }
            if (roundnessMax > 0 && r.Rms > roundnessMax)
            {
                p.Summary = string.Format(CultureInfo.InvariantCulture,
                    "圆卡尺：圆度 {0:F2} 超出上限 {1:F2}", r.Rms, roundnessMax);
                return false;
            }
            if (tolX > 0 && Math.Abs(r.Circle.Cx - c.X) > tolX)
            {
                p.Summary = string.Format(CultureInfo.InvariantCulture,
                    "圆卡尺：圆心 X 偏差 {0:F1} 超出 ±{1:F1}", r.Circle.Cx - c.X, tolX);
                return false;
            }
            if (tolY > 0 && Math.Abs(r.Circle.Cy - c.Y) > tolY)
            {
                p.Summary = string.Format(CultureInfo.InvariantCulture,
                    "圆卡尺：圆心 Y 偏差 {0:F1} 超出 ±{1:F1}", r.Circle.Cy - c.Y, tolY);
                return false;
            }

            p.Summary = string.Format(CultureInfo.InvariantCulture,
                "圆卡尺：直径 {0:F1} 圆心 ({1:F1},{2:F1}) RMS {3:F2} 有效点 {4}/{5}",
                diameter, r.Circle.Cx, r.Circle.Cy, r.Rms, r.ValidCount,
                r.Points != null ? r.Points.Count : 0);
            return true;
        }

        // ---------------------- 清晰度 / 拟合 / 结果输出 ----------------------

        private bool RunSharpness(FlowRunSession s, FlowNode node, FlowNodeProgress p)
        {
            if (s.Current == null)
            {
                p.Status = FlowNodeStatus.Skipped;
                p.Summary = "清晰度：无输入帧";
                return true;
            }

            var sr = _algo.Sharpness(s.Current);
            if (!sr.Success)
            {
                p.Summary = "清晰度评价失败";
                return false;
            }

            double min = node.GetDouble("MinSharpness", 100);
            p.Summary = string.Format(CultureInfo.InvariantCulture, "清晰度 {0:F1}", sr.Value);
            return sr.Value >= min;
        }

        private bool RunFitCircle(FlowRunSession s, FlowNode node, FlowNodeProgress p)
        {
            if (s.Current == null)
            {
                p.Status = FlowNodeStatus.Skipped;
                p.Summary = "圆拟合：无输入帧";
                return true;
            }

            var cr = _algo.FitCircle(s.Current);
            if (!cr.Success || cr.Value == null)
            {
                p.Summary = "圆拟合失败";
                return false;
            }

            if (node.GetBool("DrawShape", true))
            {
                IGeometryShape g = FlowShapes.FitResult(cr.Value);
                if (g != null) p.Shapes.Add(g);
            }

            // 最小轮廓面积：按拟合圆等效面积做下限判定（0 = 不限）
            double minArea = node.GetDouble("MinContourArea", 0);
            double area = Math.PI * cr.Value.Radius * cr.Value.Radius;
            if (minArea > 0 && area < minArea)
            {
                p.Summary = string.Format(CultureInfo.InvariantCulture,
                    "圆拟合：面积 {0:F0} 小于下限 {1:F0}", area, minArea);
                return false;
            }

            p.Summary = string.Format(CultureInfo.InvariantCulture, "圆拟合 R={0:F1}", cr.Value.Radius);
            return true;
        }

        private bool RunFitLine(FlowRunSession s, FlowNode node, FlowNodeProgress p)
        {
            if (s.Current == null)
            {
                p.Status = FlowNodeStatus.Skipped;
                p.Summary = "直线拟合：无输入帧";
                return true;
            }

            var lr = _algo.FitLine(s.Current);
            if (!lr.Success || lr.Value == null)
            {
                p.Summary = "直线拟合失败";
                return false;
            }

            if (node.GetBool("DrawShape", true))
            {
                IGeometryShape g = FlowShapes.FitResult(lr.Value);
                if (g != null) p.Shapes.Add(g);
            }

            // 最小轮廓面积：拟合直线没有面积概念，用线段长度平方做同口径下限判定
            double minArea = node.GetDouble("MinContourArea", 0);
            if (minArea > 0)
            {
                double len = lr.Value.Length;
                if (len * len < minArea)
                {
                    p.Summary = string.Format(CultureInfo.InvariantCulture,
                        "直线拟合：长度 {0:F1} 短于下限（√{1:F0}）", len, minArea);
                    return false;
                }
            }

            p.Summary = string.Format(CultureInfo.InvariantCulture, "直线拟合 长度 {0:F1}", lr.Value.Length);
            return true;
        }

        private bool RunResultOutput(FlowRunSession s, FlowNode node, FlowNodeProgress p)
        {
            // 汇总：把已执行节点的 NG 项列出来（PRD FR-ALG-26）
            var failed = new List<string>();
            foreach (FlowNodeProgress q in s.Progress)
            {
                if (q == null || q.Status != FlowNodeStatus.Ng) continue;
                failed.Add(string.IsNullOrEmpty(q.NodeName) ? q.NodeId : q.NodeName);
            }

            if (failed.Count == 0)
            {
                p.Summary = "结果汇总：OK";
                return true;
            }

            p.Summary = "结果汇总：NG（" + string.Join("、", failed) + "）";
            return false;
        }

        // =====================================================================
        //  ROI 与坐标换算
        // =====================================================================

        /// <summary>
        /// 读取节点里的 ROI 参数，换算成<strong>工作帧坐标</strong>后再应用位置修正变换。
        /// 顺序：用户坐标 → 减去 ROI 裁剪偏移 → 应用 RunTransform。
        /// </summary>
        private static Rectangle ApplyRoi(FlowRunSession s, FlowNode node,
                                          string kx, string ky, string kw, string kh)
        {
            int x = node.GetInt(kx, 0);
            int y = node.GetInt(ky, 0);
            int w = node.GetInt(kw, 0);
            int h = node.GetInt(kh, 0);

            var roi = new Rectangle((int)Math.Round(x - s.OffsetX), (int)Math.Round(y - s.OffsetY), w, h);
            return s.Transform.ApplyToRect(roi);
        }

        /// <summary>取 OpResult 消息中最后一个冒号之后的部分（用于把「模块名：详情」还原成详情）。</summary>
        private static string Tail(string message)
        {
            if (string.IsNullOrEmpty(message)) return "";

            int i = message.LastIndexOf('：');     // 全角冒号（中文消息）
            if (i < 0) i = message.LastIndexOf(':' );

            return (i >= 0 && i + 1 < message.Length) ? message.Substring(i + 1).Trim() : message;
        }

        private static PreprocessParam BuildPreprocess(FlowNode node)
        {
            var p = new PreprocessParam();
            p.ToGray = node.GetBool("ToGray", true);
            p.BlurKsize = node.GetInt("BlurKsize", 3);
            p.Threshold = node.GetInt("Threshold", 128);
            p.Invert = node.GetBool("Invert", false);
            p.MorphOp = node.GetInt("MorphOp", 0);
            p.MorphKsize = node.GetInt("MorphKsize", 3);
            return p;
        }

        // =====================================================================
        //  灰度 / 二值化（纯 GDI+，不引入第三方依赖）
        // =====================================================================

        /// <summary>转成 8bpp 灰度图（自带灰度调色板）。返回新位图，由调用方释放。</summary>
        private static Bitmap ToGrayBitmap(Bitmap src)
        {
            int w = src.Width;
            int h = src.Height;

                BitmapData dstData = null;
                Bitmap dst = VisionAlgo.CreateGrayBitmap(w, h, out dstData);
                try
                {
                    // 统一先画到 24bpp，避免处理索引 / 16bpp / 32bpp 等格式的分支

                using (var work = new Bitmap(w, h, PixelFormat.Format24bppRgb))
                {
                    using (Graphics g = Graphics.FromImage(work))
                        g.DrawImage(src, 0, 0, w, h);

                    BitmapData sd = work.LockBits(new Rectangle(0, 0, w, h),
                                                  ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                    try
                    {
                        byte[] row = new byte[sd.Stride];
                        byte[] outRow = new byte[dstData.Stride];

                        for (int y = 0; y < h; y++)
                        {
                            Marshal.Copy(IntPtr.Add(sd.Scan0, y * sd.Stride), row, 0, sd.Stride);
                            for (int x = 0; x < w; x++)
                            {
                                int i = x * 3;
                                // Rec.601 亮度：Y = 0.299R + 0.587G + 0.114B（BGR 存储顺序）
                                outRow[x] = (byte)((row[i] * 114 + row[i + 1] * 587 + row[i + 2] * 299) / 1000);
                            }
                            Marshal.Copy(outRow, 0, IntPtr.Add(dstData.Scan0, y * dstData.Stride), dstData.Stride);
                        }
                    }
                    finally
                    {
                        work.UnlockBits(sd);
                    }
                }
                return dst;
            }
            catch
            {
                // 先解锁再释放，避免 finally 对已释放的位图再次 UnlockBits
                if (dstData != null) { dst.UnlockBits(dstData); dstData = null; }
                dst.Dispose();
                throw;
            }
            finally
            {
                if (dstData != null) dst.UnlockBits(dstData);
            }
        }

        /// <summary>
        /// 灰度变换：<paramref name="binarize"/> = true 时按阈值二值化，否则做灰度取反。
        /// <paramref name="invert"/> 用于把「统计暗目标」翻成「统计亮目标」。
        /// </summary>
        private static Bitmap TransformGray(Bitmap src, bool binarize, int threshold, bool invert)
        {
            using (Bitmap gray = ToGrayBitmap(src))
            {
                int w = gray.Width;
                int h = gray.Height;

                BitmapData sd = gray.LockBits(new Rectangle(0, 0, w, h),
                                              ImageLockMode.ReadOnly, PixelFormat.Format8bppIndexed);
                BitmapData dd = null;
                Bitmap dst = null;
                try
                {
                    dst = VisionAlgo.CreateGrayBitmap(w, h, out dd);

                    byte[] row = new byte[sd.Stride];
                    byte[] outRow = new byte[dd.Stride];
                    byte hi = invert ? (byte)0 : (byte)255;
                    byte lo = invert ? (byte)255 : (byte)0;

                    for (int y = 0; y < h; y++)
                    {
                        Marshal.Copy(IntPtr.Add(sd.Scan0, y * sd.Stride), row, 0, sd.Stride);
                        for (int x = 0; x < w; x++)
                        {
                            byte v = row[x];
                            outRow[x] = binarize ? ((v >= threshold) ? hi : lo) : (byte)(255 - v);
                        }
                        Marshal.Copy(outRow, 0, IntPtr.Add(dd.Scan0, y * dd.Stride), dd.Stride);
                    }
                    return dst;
                }
                catch
                {
                    // 先解锁再释放，避免 finally 对已释放的位图再次 UnlockBits
                    if (dd != null && dst != null) { dst.UnlockBits(dd); dd = null; }
                    if (dst != null) dst.Dispose();
                    throw;
                }
                finally
                {
                    if (dd != null && dst != null) dst.UnlockBits(dd);
                    gray.UnlockBits(sd);
                }
            }
        }

        /// <summary>Otsu 自动阈值（直方图 + 最大类间方差）。</summary>
        private static int OtsuThreshold(Bitmap src)
        {
            using (Bitmap gray = ToGrayBitmap(src))
            {
                int w = gray.Width;
                int h = gray.Height;

                BitmapData d = gray.LockBits(new Rectangle(0, 0, w, h),
                                             ImageLockMode.ReadOnly, PixelFormat.Format8bppIndexed);
                try
                {
                    int[] hist = new int[256];
                    byte[] row = new byte[d.Stride];
                    for (int y = 0; y < h; y++)
                    {
                        Marshal.Copy(IntPtr.Add(d.Scan0, y * d.Stride), row, 0, d.Stride);
                        for (int x = 0; x < w; x++) hist[row[x]]++;
                    }
                    return OtsuFromHistogram(hist, w * h);
                }
                finally
                {
                    gray.UnlockBits(d);
                }
            }
        }

        /// <summary>由灰度直方图求 Otsu 阈值；全黑 / 全白等退化情形返回 128。</summary>
        private static int OtsuFromHistogram(int[] hist, int total)
        {
            if (hist == null || total <= 0) return 128;

            double sum = 0.0;
            for (int i = 0; i < 256; i++) sum += (double)i * hist[i];

            double wB = 0.0;
            double sumB = 0.0;
            double maxVar = -1.0;
            int threshold = 128;

            for (int t = 0; t < 256; t++)
            {
                wB += hist[t];
                if (wB <= 0.0) continue;

                double wF = total - wB;
                if (wF <= 0.0) break;

                sumB += (double)t * hist[t];
                double mB = sumB / wB;
                double mF = (sum - sumB) / wF;
                double between = wB * wF * (mB - mF) * (mB - mF);

                if (between > maxVar)
                {
                    maxVar = between;
                    threshold = t;
                }
            }

            return threshold;
        }

        // =====================================================================
        //  单链拓扑解析（旧实现）—— 已于端口化 T05 第二轮整体删除
        // =====================================================================
        //
        // 【已删除】旧 TryBuildChain（约 65 行，节点级单链解析）此处已移除，职责拆分如下：
        //   · 拓扑排序 / 成环 / 孤立节点 → Flow\FlowOrder.cs（FlowOrder.Build）
        //   · 必填端子 / 类型兼容 / 重复连线 / 结构 → Flow\FlowValidator.cs（FlowValidator.Validate）
        // 依赖它的唯一入口 static Validate(FlowDefinition, out string) 也已改走 FlowValidator，
        // 对外签名保持不变，因此 FlowForm / FlowRunManager 的调用点零改动。
        // 若将来发现「存在分支 / 存在汇聚」这类旧文案，说明还有残留引用，应当一并清理。

        /// <summary>
        /// 校验（供界面「校验」按钮 <c>FlowForm.OnValidate</c> 与启动前检查
        /// <c>FlowRunManager.StartOne</c> 调用）。成功返回 true。
        /// </summary>
        /// <remarks>
        /// <strong>对外签名一个字都不要动</strong>：FlowForm / FlowRunManager 是别的同事在改的文件，
        /// 签名不变他们那边零改动。
        /// <para>
        /// 组成 = <c>① 运行前置检查（保留原样）</c> + <c>② 拓扑 / 端子校验（端口化后改走 FlowValidator 的 Error 级）</c>。
        /// </para>
        /// <para>
        /// ⚠️ 相机是否打开<strong>不在这里判断</strong>：那是运行前置<strong>状态</strong>，
        /// 分别由 <c>StartOne</c> 的第 3 步「相机必须已打开」与 <c>Begin</c> 负责。
        /// 放进静态校验会让「编辑中间态」动不动就跑不起来，
        /// 也会和 <c>Begin</c> 的实际行为不一致（Begin 才是真正要启动的那条路径）。
        /// </para>
        /// </remarks>
        public static bool Validate(FlowDefinition flow, out string error)
        {
            error = null;

            // ---- ① 运行前置检查（保留原样：与拓扑无关，删了会让用户点了启动才发现）----
            if (flow == null) { error = "流程为空"; return false; }
            if (flow.Nodes == null || flow.Nodes.Count == 0) { error = "流程没有任何节点"; return false; }

            // ---- ② 拓扑 / 端子校验：委托 FlowValidator，只取 Error 级，文案原样透传 ----
            FlowValidationResult vr = FlowValidator.Validate(flow);
            if (vr.Errors.Count == 0) return true;

            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < vr.Errors.Count; i++)
            {
                if (i > 0) sb.Append(Environment.NewLine);
                sb.Append(vr.Errors[i].Message);
            }
            error = sb.ToString();
            return false;
        }

        /// <summary>
        /// 该流程是否需要相机：只要链上有一个图像源节点选择「本地图片」，就不需要相机
        /// （FR-ALG-01：无相机也能跑通整链）。解析不出图像源时按「需要相机」处理。
        /// </summary>
        public static bool NeedsCamera(FlowDefinition flow)
        {
            if (flow == null || flow.Nodes == null) return true;

            foreach (FlowNode n in flow.Nodes)
            {
                if (n == null) continue;

                FlowNodeType t;
                if (!n.TryGetType(out t)) continue;
                if (t != FlowNodeType.ImageSource) continue;

                // 只要有一个图像源节点选了「本地图片」，整链就不需要相机
                if (string.Equals(n.GetString("Source", "Camera"), "File", StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            // 找不到「本地图片」图像源：保持原有行为，需要相机
            return true;
        }

        // =====================================================================

        /// <summary>
        /// 释放全部匹配模型训练缓存：托管模板位图 + 原生形状模型槽位。
        /// 必须在 <see cref="Dispose"/> 里、且早于 <c>_algo.Dispose()</c> 调用（原生句柄销毁后清理会悬垂）。
        /// </summary>
        private void ClearTrainedModels()
        {
            foreach (TrainedModel m in _models.Values)
            {
                if (m != null && m.Template != null)
                    m.Template.Dispose();
            }
            _models.Clear();

            // 原生形状模型槽位：仅在确实训练过（有 owner）时释放，避免对空上下文发起调用。
            if (!string.IsNullOrEmpty(_shapeModelOwner))
                _algo.ClearShapeModel();

            _shapeModelOwner = null;
            _nccModelOwner = null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // 先释放训练缓存（含原生形状模型句柄 + 托管模板位图），再销毁算法上下文，
            // 顺序不能反：_algo.Dispose() 之后 ClearShapeModel 会打在已释放的句柄上。
            ClearTrainedModels();
            _algo.Dispose();
        }

        // =====================================================================
        //  匹配模型训练缓存（P0-1 / P0-2）
        // =====================================================================

        /// <summary>
        /// 一个匹配节点的训练缓存：<strong>模板图 + 训练参数签名</strong>。
        /// 模板图是「第一次训练那一帧」的模板 ROI 副本（示教帧），
        /// 之后所有帧都拿它当基准去匹配 —— 这就是「训练一次、匹配多次」。
        /// </summary>
        private sealed class TrainedModel
        {
            /// <summary>训练参数签名（变则重训）。</summary>
            public string Signature = "";

            /// <summary>训练基准图（模板 ROI 副本；释放时机见 <see cref="ClearTrainedModels"/>）。</summary>
            public Bitmap Template = null;
        }

        /// <summary>形状匹配的训练参数签名（模板 ROI + 训练三参数）。</summary>
        private static string ShapeSignature(Rectangle roi, ShapeTrainParam train)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "{0},{1},{2},{3}|C={4}|F={5}|D={6}",
                roi.X, roi.Y, roi.Width, roi.Height,
                train.Contrast, train.MaxFeatures, train.MinFeatureDist);
        }

        /// <summary>模板匹配（NCC）的训练参数签名（只有模板 ROI）。</summary>
        private static string TemplateSignature(Rectangle roi)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "{0},{1},{2},{3}", roi.X, roi.Y, roi.Width, roi.Height);
        }

        /// <summary>
        /// 裁出模板 ROI 的副本作为训练基准图；ROI 无效（宽高为 0 或完全在图外）时退化为整帧副本。
        /// </summary>
        private static Bitmap CropTemplate(Bitmap src, Rectangle roi)
        {
            if (src == null) return null;

            Rectangle bounds = new Rectangle(0, 0, src.Width, src.Height);
            if (roi.Width <= 0 || roi.Height <= 0) return (Bitmap)src.Clone();

            Rectangle real = Rectangle.Intersect(roi, bounds);
            if (real.Width <= 0 || real.Height <= 0) return (Bitmap)src.Clone();

            return src.Clone(real, src.PixelFormat);
        }
    }
}
