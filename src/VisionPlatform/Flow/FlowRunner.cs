using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using VisionPlatform.Camera;
using VisionPlatform.Communication;
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

        // OCR 字符模板缓存：
        //   原生模板挂在算法上下文上、没有 save/load，且 VisionAlgo 句柄可能被重建。
        //   这里记录「已灌入的模板目录指纹」（路径 + 文件数 + 最后写入时间），
        //   指纹变化或原生模板数为 0（句柄是新的）时，清空后重新灌入。
        private string _ocrTemplateDir;
        private string _ocrTemplateFingerprint;

        /// <summary>上一轮模板加载的结构化结果（指纹未变时直接复用，避免每帧重建失败清单）。</summary>
        private OcrTemplateLoadResult _ocrTemplateResult;

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
        public FlowRunSession Begin(FlowDefinition flow, ICamera camera, bool captureFrames = false, bool clearAutoRefs = true)
        {
            // 每次「运行」开始前清空位置修正 Auto 基准缓存，避免上一轮示教的基准泄漏到
            // 本轮，造成仿射变换基准错位（图像偏移 / 修正缺失）。
            // 连续运行由调用方在循环外清空一次、循环内传 false，保证单轮连续运行的
            // 「首次示教」基准不被每帧清空（见 FlowRunController.LoopCore）。
            if (clearAutoRefs) ClearAutoReferences();

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
                // 完整异常（含原生 OpenCV 报错里的文件:行号与断言内容）写入日志，便于排查「显示不全」的报错
                Logger.Error("节点「" + node.Name + "」(" + type + ") 执行异常：" + ex);
            }
            sw.Stop();

            // 每个节点执行完后刷新彩色通道：节点可能换过工作帧（灰度化 / 保持彩色 / 像素级变换），
            // 统一在外部按当前帧的实际格式刷新，不依赖各节点自觉汇报，杜绝野引用。
            session.SyncColorInput();

            p.ElapsedMs = (int)sw.ElapsedMilliseconds;

            // 节点内部已显式设置 Skipped / Warn 时保持原状，不要覆盖成 Ok。
            // ⚠️ 这是 Warn 唯一的"让位点"：此后任何覆盖 p.Status 的代码都必须先检查
            //    是不是 Warn（Warn 只影响显示，绝不等于 NG，也不得被 Ok 抹平）。
            //     判据见 Architecture「T07 红线②」。
            if (p.Status == FlowNodeStatus.Running)
                p.Status = ok ? FlowNodeStatus.Ok : FlowNodeStatus.Ng;

            if (!ok) session.Passed = false;

            // 节点内（改动1）已克隆预览帧时不再重复克隆，避免重复分配与泄漏
            if (session.CaptureFrames && session.Current != null && p.Frame == null)
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
                case FlowNodeType.ImageFilter:  return RunImageFilter(s, node, p);
                case FlowNodeType.ColorExtractor: return RunColorExtractor(s, node, p);
                case FlowNodeType.ObjectDetect:  return RunObjectDetect(s, node, p);
                case FlowNodeType.Ocr:           return RunOcr(s, node, p);
                case FlowNodeType.SendData:      return RunSendData(s, node, p);
                case FlowNodeType.ReceiveData:   return RunReceiveData(s, node, p);
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

                // 预览帧：连续运行（CaptureFrames=false）时 StepNext 不会留帧，
                // 这里显式克隆当前帧，保证流程预览区能显示该节点的结果图（与位置修正同款）。
                if (s.CaptureFrames && s.Current != null)
                {
                    if (p.Frame != null) p.Frame.Dispose();
                    try { p.Frame = (Bitmap)s.Current.Clone(); }
                    catch (Exception) { p.Frame = null; }
                }

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

            // 预览帧：连续运行（CaptureFrames=false）时 StepNext 不会留帧，
            // 这里显式克隆当前帧，保证流程预览区能显示该节点的结果图（与位置修正同款）。
            if (s.CaptureFrames && s.Current != null)
            {
                if (p.Frame != null) p.Frame.Dispose();
                try { p.Frame = (Bitmap)s.Current.Clone(); }
                catch (Exception) { p.Frame = null; }
            }

            p.Summary = "预处理完成";
            return true;
        }

        // ---------------------- 图像滤波（原生 va_filter）----------------------

        private bool RunImageFilter(FlowRunSession s, FlowNode node, FlowNodeProgress p)
        {
            if (s.Current == null)
            {
                p.Status = FlowNodeStatus.Skipped;
                p.Summary = "图像滤波：无输入帧";
                return true;
            }

            var fp = new FilterParam
            {
                Type = ParseFilterType(node.GetString("FilterType", "Gauss")),
                Ksize = node.GetInt("KernelSize", 5),
                Sigma = node.GetDouble("Sigma", 0),
                Amount = node.GetDouble("Amount", 1.0),
                MorphOp = ParseFilterMorphOp(node.GetString("MorphOp", "None")),
                MorphShape = ParseMorphShape(node.GetString("MorphShape", "Rect")),
                MorphKsize = node.GetInt("MorphKernel", 3),
                Iterations = node.GetInt("Iterations", 1),
                KeepColor = node.GetBool("KeepColor", false)
            };

            // 彩色链路的前置条件：上游到本节点为止仍持有彩色帧（灰度 / 二值化之后就没了）。
            // 条件不满足时自动退回灰度链路，不判 NG —— 灰度滤波本身是合法用法。
            bool keepColor = fp.KeepColor && s.ColorInput != null;
            fp.KeepColor = keepColor;

            Bitmap input = keepColor ? s.ColorInput : s.Current;
            Rectangle roi = ResolveRoi(s, node, input.Width, input.Height);

            Bitmap output = null;
            Bitmap processed = null;
            bool handed = false;

            try
            {
                if (!roi.IsEmpty)
                {
                    // ROI 模式：只处理 ROI 内的一小块，再把结果按原位置贴回整帧副本
                    using (Bitmap crop = CropTemplate(input, roi))
                    {
                        OpResult<Bitmap> fr = _algo.Filter(crop, fp);
                        if (!fr.Success)
                        {
                            p.Summary = "图像滤波：" + Tail(fr.Message);
                            return false;
                        }

                        processed = fr.Value;
                        output = CloneWorkingFrame(input, keepColor);
                        BlitPatch(output, processed, roi.X, roi.Y);
                    }
                }
                else
                {
                    OpResult<Bitmap> fr = _algo.Filter(input, fp);
                    if (!fr.Success)
                    {
                        p.Summary = "图像滤波：" + Tail(fr.Message);
                        return false;
                    }
                    output = fr.Value;
                }

                if (output == null) { p.Summary = "图像滤波：输出为空"; return false; }

                s.ReplaceFrame(output);       // 所有权移交会话
                handed = true;

                // 预览帧：与预处理同款，显式克隆当前帧供流程预览区显示
                if (s.CaptureFrames && s.Current != null)
                {
                    if (p.Frame != null) p.Frame.Dispose();
                    try { p.Frame = (Bitmap)s.Current.Clone(); }
                    catch (Exception) { p.Frame = null; }
                }

                if (!roi.IsEmpty) p.AuxShapes.AddRange(FlowShapes.RoiOutline(roi));

                p.Summary = string.Format(CultureInfo.InvariantCulture, "滤波：{0} 核={1}{2}{3}",
                    FilterType.Name(fp.Type), fp.Ksize,
                    fp.MorphOp != FilterMorphOp.None ? " + 形态学" + FilterMorphOp.Name(fp.MorphOp) : "",
                    keepColor ? "（彩色）" : "");
                return true;
            }
            finally
            {
                // 未交出会话的帧由本方法释放；交出去的（Current）归会话释放
                if (!handed && output != null) output.Dispose();
                if (processed != null) processed.Dispose();
            }
        }

        // ---------------------- 颜色提取（原生 va_color_extract）----------------------

        private bool RunColorExtractor(FlowRunSession s, FlowNode node, FlowNodeProgress p)
        {
            if (s.Current == null)
            {
                p.Status = FlowNodeStatus.Skipped;
                p.Summary = "颜色提取：无输入帧";
                return true;
            }

            // 【彩色前置检查】颜色提取是全库唯一必须拿彩色像素的算法。
            // 无彩色帧时不抛异常、也不静默成功 —— 走正常失败路径（判 NG）并给出可操作的中文提示。
            if (s.ColorInput == null)
            {
                p.Summary = "颜色提取：需要彩色输入，请把本节点直接接在图像源之后，"
                          + "或在上游图像滤波节点勾选「输出彩色」";
                return false;
            }

            int space = string.Equals(node.GetString("ColorSpace", "HSV"), "BGR",
                                      StringComparison.OrdinalIgnoreCase)
                ? ColorSpace.Bgr : ColorSpace.Hsv;

            var cp = new ColorExtractParam
            {
                Space = space,
                Low = new[]
                {
                    node.GetInt("Low1", 0), node.GetInt("Low2", 0), node.GetInt("Low3", 0)
                },
                High = new[]
                {
                    node.GetInt("High1", space == ColorSpace.Hsv ? 179 : 255),
                    node.GetInt("High2", 255),
                    node.GetInt("High3", 255)
                },
                MorphOp = ParseColorMorphOp(node.GetString("CleanOp", "None")),
                MorphKsize = node.GetInt("CleanKernel", 3),
                MinArea = node.GetDouble("MinArea", 10),
                MaxArea = node.GetDouble("MaxArea", 0),
                MaxCount = node.GetInt("MaxCount", 50),
                OutputMask = node.GetBool("OutputMask", true)
            };

            OpResult<ColorExtractResult> cr = _algo.ExtractColor(s.ColorInput, cp);
            if (!cr.Success)
            {
                p.Summary = "颜色提取：" + Tail(cr.Message);
                return false;
            }

            ColorExtractResult r = cr.Value;

            // 可选 ROI：只保留中心落在 ROI 内的命中区域（与 Blob 的 ROI 口径一致）
            Rectangle roi = ResolveRoi(s, node, s.ColorInput.Width, s.ColorInput.Height);
            var kept = new List<BlobResult>();
            foreach (BlobResult b in r.Blobs)
            {
                if (!roi.IsEmpty && !roi.Contains((int)Math.Round(b.Cx), (int)Math.Round(b.Cy))) continue;
                kept.Add(b);
            }

            foreach (BlobResult b in kept)
                p.Shapes.Add(FlowShapes.BlobRect(b.Cx, b.Cy, b.Width, b.Height));

            if (!roi.IsEmpty) p.AuxShapes.AddRange(FlowShapes.RoiOutline(roi));

            // 输出：默认把 0/255 掩膜作为工作帧传给下游；关闭时保持原彩色帧继续往下传
            bool handed = false;
            if (cp.OutputMask && r.Mask != null)
            {
                try
                {
                    s.ReplaceFrame(r.Mask);
                    handed = true;
                }
                catch (Exception)
                {
                    handed = false;
                }
            }

            // 预览帧：输出掩膜时显示掩膜，否则显示原彩色帧
            if (s.CaptureFrames && s.Current != null)
            {
                if (p.Frame != null) p.Frame.Dispose();
                try { p.Frame = (Bitmap)s.Current.Clone(); }
                catch (Exception) { p.Frame = null; }
            }

            if (!handed && r.Mask != null) r.Mask.Dispose();

            p.Summary = string.Format(CultureInfo.InvariantCulture,
                "颜色提取：命中 {0} 个区域，面积占比 {1:F1}%，平均 BGR({2:F0},{3:F0},{4:F0})",
                kept.Count, r.Ratio * 100.0, r.MeanB, r.MeanG, r.MeanR);
            return true;
        }

        // ---------------------- 目标识别（OpenVINO YOLO，原生 va_det_*）----------------------

        private bool RunObjectDetect(FlowRunSession s, FlowNode node, FlowNodeProgress p)
        {
            if (s.Current == null)
            {
                p.Status = FlowNodeStatus.Skipped;
                p.Summary = "目标识别：无输入帧";
                return true;
            }

            // YOLO 需要彩色输入：优先用会话维护的彩色帧（ColorInput，接在图像源后即为原彩图），
            // 否则退回当前帧（原生侧会自动把灰度 / 4 通道转成 BGR 再推理，但精度不如直接喂彩图）。
            Bitmap input = (s.ColorInput != null) ? s.ColorInput : s.Current;

            var dp = new DetectParam
            {
                ModelPath = node.GetString("ModelPath", "models/yolo11n.onnx"),
                Device = node.GetString("Device", "CPU"),
                ScoreThresh = (float)node.GetDouble("ScoreThresh", 0.25),
                IouThresh = (float)node.GetDouble("IouThresh", 0.45),
                MaxCount = node.GetInt("MaxCount", 100)
            };

            OpResult<DetectOutput> dr = _algo.Detect(input, dp);
            if (!dr.Success)
            {
                p.Summary = "目标识别：" + Tail(dr.Message);
                return false;
            }

            DetectOutput outp = dr.Value;
            if (outp == null || outp.Detections == null) outp = new DetectOutput();

            // 画布叠加层：每个检测框（置信度最高的高亮），类别名写入形状 Name 供画布提示
            int idx = 0;
            foreach (DetectResult d in outp.Detections)
            {
                idx++;
                bool primary = (outp.Best != null) && object.ReferenceEquals(d, outp.Best);
                string label = string.Format(CultureInfo.InvariantCulture,
                    "#{0} {1} {2:0.00}", idx, d.ClassName, d.Score);
                var box = FlowShapes.DetectBox(d.X, d.Y, d.Width, d.Height, primary, label);
                if (box != null) box.Name = label;   // Name 供悬浮提示，Label 供画布绘制
                p.Shapes.Add(box);
            }

            p.Metrics.TargetCount = outp.Detections.Count;
            if (outp.Best != null)
            {
                p.Metrics.TopScore = outp.Best.Score;
                p.Metrics.TopCx = outp.Best.X + outp.Best.Width * 0.5;
                p.Metrics.TopCy = outp.Best.Y + outp.Best.Height * 0.5;
            }

            // 预览帧：克隆当前工作帧供流程预览区显示（检测不修改原图，叠加层由画布绘制）
            if (s.CaptureFrames && s.Current != null)
            {
                if (p.Frame != null) p.Frame.Dispose();
                try { p.Frame = (Bitmap)s.Current.Clone(); }
                catch (Exception) { p.Frame = null; }
            }

            // 摘要：检测数量 + 前若干类别（置信度）
            string top = "";
            int show = Math.Min(outp.Detections.Count, 5);
            for (int i = 0; i < show; i++)
            {
                DetectResult d = outp.Detections[i];
                top += (i == 0 ? "" : "、")
                     + d.ClassName + " " + d.Score.ToString("F2", CultureInfo.InvariantCulture);
            }
            p.Summary = string.Format(CultureInfo.InvariantCulture,
                "目标识别：检测到 {0} 个目标{1}", outp.Detections.Count,
                show > 0 ? "（" + top + (outp.Detections.Count > show ? "…" : "") + "）" : "");
            return true;
        }

        // ---------------------- 字符识别（传统算法 OCR，原生 va_ocr_*）----------------------

        private bool RunOcr(FlowRunSession s, FlowNode node, FlowNodeProgress p)
        {
            if (s.Current == null)
            {
                p.Status = FlowNodeStatus.Skipped;
                p.Summary = "字符识别：无输入帧";
                return true;
            }

            Bitmap frame = s.Current;
            Rectangle roi = ResolveRoi(s, node, frame.Width, frame.Height);

            Bitmap crop = null;
            Bitmap gray = null;
            bool grayOwned = false;

            try
            {
                // ① 取图：OCR 走灰度链路，ROI 非空时只处理 ROI 内的一块
                if (!roi.IsEmpty)
                {
                    crop = CropTemplate(frame, roi);
                    if (crop == null) { p.Summary = "字符识别：ROI 裁剪失败"; return false; }
                }
                Bitmap input = crop ?? frame;

                if (input.PixelFormat == PixelFormat.Format8bppIndexed)
                {
                    gray = input;
                }
                else
                {
                    gray = ToGrayBitmap(input);
                    grayOwned = true;
                }
                if (gray == null) { p.Summary = "字符识别：灰度转换失败"; return false; }

                // ② 读参数
                var op = new OcrParam
                {
                    Threshold = node.GetInt("Threshold", -1),
                    Invert = node.GetBool("Invert", false),
                    MinCharH = node.GetDouble("MinCharH", 0),
                    MaxCharH = node.GetDouble("MaxCharH", 0),
                    MinAspect = node.GetDouble("MinAspect", 0),
                    MaxAspect = node.GetDouble("MaxAspect", 0),
                    MaxGap = node.GetInt("MaxGap", 3),
                    NormW = node.GetInt("NormW", 16),
                    NormH = node.GetInt("NormH", 24),
                    AcceptScore = node.GetDouble("AcceptScore", 0.6)
                };
                int maxCount = node.GetInt("MaxCount", 200);
                // 回落默认值必须与 Schema 的默认值一致：NodeParamSchema.Ocr 里 ShowBoxes 默认 "0"。
                // 老配置里若显式写了 1，GetBool 会取到 1，行为不受影响。
                bool showBoxes = node.GetBool("ShowBoxes", false);
                // 区域框 / 合并字符串框默认开（清晰度靠"看得见识别区域与整串结果"，
                // 而不是靠把一行 30 个字符框全打开 —— 那是噪声）。
                bool showRegionBox = node.GetBool("ShowRegionBox", true);
                bool showStringBox = node.GetBool("ShowStringBox", true);

                // ③ 模板目录（相对程序基目录，默认 bin/x64/Release/OCRModel）
                string dirName = node.GetString("TemplateDir", "OCRModel");
                if (string.IsNullOrWhiteSpace(dirName)) dirName = "OCRModel";
                string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, dirName);

                OpResult<OcrTemplateLoadResult> tpl = EnsureOcrTemplates(dir);
                if (!tpl.Success)
                {
                    p.Summary = "字符识别：" + Tail(tpl.Message);
                    return false;
                }
                OcrTemplateLoadResult lib = tpl.Value;
                bool hasTemplate = lib != null && lib.HasTextTemplate;

                // ④ 识别：一次调用同时拿到整段文本 + 平均置信度 + 每个字符的真实标签
                OpResult<OcrResult> rr = _algo.RecognizeEx(gray, op, maxCount);
                if (!rr.Success)
                {
                    p.Summary = "字符识别：" + Tail(rr.Message);
                    return false;
                }

                string text = (rr.Value != null) ? (rr.Value.Text ?? "") : "";
                double meanScore = (rr.Value != null) ? rr.Value.MeanScore : 0.0;
                List<OcrChar> chars = (rr.Value != null && rr.Value.Chars != null)
                                    ? rr.Value.Chars : new List<OcrChar>();

                // ⑤ 主判据：文本 / 字符数 / 平均置信度（结果区第二行、画布卡片、预览覆盖层同源）
                int charCount = chars.Count;
                p.Metrics.Text = text;
                p.Metrics.CharCount = charCount;
                p.Metrics.MeanScore = meanScore;
                p.Metrics.TextOnly = !hasTemplate;
                p.Metrics.TextOnlyReason = hasTemplate ? null : (lib != null ? lib.Brief() : "未载入字符模板");
                p.Metrics.TargetCount = charCount;

                // ⑥ 区域框（OCR 作用区域，默认开）：即使识别失败也画，用户才知道"它在看哪一块"。
                //    走 AuxShapes —— 它是输入几何，不是检测目标，不能占结果区明细行。
                if (showRegionBox)
                {
                    Rectangle region = roi.IsEmpty
                        ? new Rectangle(0, 0, frame.Width, frame.Height)
                        : roi;
                    var regionBox = FlowShapes.OcrRegionBox(region,
                        "OCR 区域 " + region.Width + "×" + region.Height);
                    if (regionBox != null) p.AuxShapes.Add(regionBox);
                }

                // ⑦ 合并字符串框（默认开）：按行归并，一行一个框，标签就是该行的识别文本。
                //    逐字符框是另一个开关，默认关 —— "一行 30 个框"是噪声不是清晰。
                if (showStringBox && chars.Count > 0)
                {
                    foreach (OcrLine line in GroupOcrLines(chars))
                    {
                        var lineBox = FlowShapes.OcrStringBox(
                            new Rectangle((int)Math.Round(line.X + roi.X), (int)Math.Round(line.Y + roi.Y),
                                          Math.Max(1, (int)Math.Round(line.W)), Math.Max(1, (int)Math.Round(line.H))),
                            string.IsNullOrEmpty(line.Text) ? "?" : line.Text);
                        if (lineBox != null) p.AuxShapes.Add(lineBox);
                    }
                }

                // ⑧ 逐字符框（默认关；老配置显式写了 1 的保持开启）。
                //    数据源与文本同源（不再单独调 Segment，否则框上标签恒为 "? 0.00"）。
                //    图像坐标：ROI 模式下要从 ROI 局部坐标还原到整帧坐标。
                if (showBoxes)
                {
                    foreach (OcrChar c in chars)
                    {
                        string label = (string.IsNullOrEmpty(c.Label) ? "?" : c.Label)
                                     + " " + c.Score.ToString("F2", CultureInfo.InvariantCulture);
                        var box = FlowShapes.OcrBox(c.X + roi.X, c.Y + roi.Y, c.W, c.H, label);
                        if (box != null) box.Name = "OcrChar";
                        p.Shapes.Add(box);
                    }
                }

                // ⑨ 预览帧：OCR 不改图，只克隆当前帧供预览（不调 ReplaceFrame）。
                // 连续运行（CaptureFrames=false）下预览区不显示单节点帧，跳过以免每周期徒增一张整幅克隆（LOH 压力→卡顿）。
                if (s.CaptureFrames && s.Current != null)
                {
                    if (p.Frame != null) p.Frame.Dispose();
                    try { p.Frame = (Bitmap)s.Current.Clone(); }
                    catch (Exception) { p.Frame = null; }
                }

                // ⑩ 摘要
                string shown = text.Replace("\r", "").Replace("\n", "⏎");
                p.Summary = string.Format(CultureInfo.InvariantCulture,
                    "字符识别：\"{0}\"（{1} 字符，平均置信度 {2:F2}）{3}",
                    shown, charCount, meanScore,
                    hasTemplate ? "" : "；" + lib.Brief());

                // ⑪ 空库 / 全失败 / 部分失败 → 只黄不红（Warn 让位点见 StepNext）：
                //    OK 在本系统里是"检测通过"的语义，把"根本没识别"显示成 OK 会让操作员
                //    把未识别工件判为良品 —— 这是误判风险，不是界面美化问题。
                if (lib != null && lib.State != OcrTemplateState.Ok)
                    p.Status = FlowNodeStatus.Warn;

                return true;
            }
            finally
            {
                if (grayOwned && gray != null) gray.Dispose();
                if (crop != null) crop.Dispose();
            }
        }

        /// <summary>OCR 的一行归并结果（局部坐标）。</summary>
        private struct OcrLine
        {
            public double X;
            public double Y;
            public double W;
            public double H;
            public string Text;
        }

        /// <summary>
        /// 把逐字符结果按<strong>行</strong>归并（同一行的字符合成一个框，标签 = 该行文本）。
        /// <para>
        /// 【为什么要归并】用户说的"结果不够清楚"，一半来自"只画了一堆小框，看不出整串是什么"。
        /// 「合并字符串框」就是把这堆小框折成"一行一个框 + 一行文本"，看结果不必逐字拼。
        /// </para>
        /// <para>归并判据：垂直方向重叠占较矮者高度的 50% 以上视为同一行（对倾斜/高低不齐有容忍度）。</para>
        /// </summary>
        private static List<OcrLine> GroupOcrLines(List<OcrChar> chars)
        {
            var lines = new List<OcrLine>();
            if (chars == null || chars.Count == 0) return lines;

            var sorted = new List<OcrChar>(chars);
            sorted.Sort(delegate (OcrChar a, OcrChar b)
            {
                int byY = a.Y.CompareTo(b.Y);
                return byY != 0 ? byY : a.X.CompareTo(b.X);
            });

            var buffer = new List<OcrChar>();

            foreach (OcrChar c in sorted)
            {
                if (buffer.Count == 0)
                {
                    buffer.Add(c);
                    continue;
                }

                // 与当前行已累积区域的垂直重叠比例（占较矮者高度的 50% 以上算同一行）
                double top = double.MaxValue, bottom = double.MinValue;
                foreach (OcrChar b in buffer)
                {
                    if (b.Y < top) top = b.Y;
                    if (b.Y + b.H > bottom) bottom = b.Y + b.H;
                }

                double overlap = Math.Min(bottom, c.Y + c.H) - Math.Max(top, c.Y);
                double shorter = Math.Min(bottom - top, c.H);
                bool sameLine = shorter > 0 && overlap > 0 && overlap * 2 >= shorter;

                if (sameLine)
                {
                    buffer.Add(c);
                }
                else
                {
                    lines.Add(BuildLine(buffer));
                    buffer.Clear();
                    buffer.Add(c);
                }
            }

            if (buffer.Count > 0) lines.Add(BuildLine(buffer));
            return lines;
        }

        /// <summary>把同一行的字符压成一个外接框 + 拼接文本（按 X 排序，保证文本顺序正确）。</summary>
        private static OcrLine BuildLine(List<OcrChar> chars)
        {
            chars.Sort(delegate (OcrChar a, OcrChar b) { return a.X.CompareTo(b.X); });

            double left = double.MaxValue, top = double.MaxValue, right = double.MinValue, bottom = double.MinValue;
            var sb = new StringBuilder();
            foreach (OcrChar c in chars)
            {
                if (c.X < left) left = c.X;
                if (c.Y < top) top = c.Y;
                if (c.X + c.W > right) right = c.X + c.W;
                if (c.Y + c.H > bottom) bottom = c.Y + c.H;
                sb.Append(string.IsNullOrEmpty(c.Label) ? "?" : c.Label);
            }

            var line = new OcrLine();
            line.X = left;
            line.Y = top;
            line.W = Math.Max(1, right - left);
            line.H = Math.Max(1, bottom - top);
            line.Text = sb.ToString();
            return line;
        }

        // ---------------------- 发送数据 / 接收数据（绑定「通讯」界面里已配置的通讯）----------------------
        /// <summary>
        /// 发送数据：按名称绑定一条通讯，发固定文本 / 格式化文本，或写 Modbus 单个保持寄存器（FC06）。
        /// <para>不依赖图像：输入端子是可选直通，无输入帧也照常执行（纯通讯场景可独立成链）。</para>
        /// </summary>
        private bool RunSendData(FlowRunSession s, FlowNode node, FlowNodeProgress p)
        {
            string commName = ResolveComm(node, "发送数据", p);
            if (commName == null) return false;

            string mode = node.GetString("DataMode", "FixedText");
            bool autoConnect = node.GetBool("AutoConnect", true);

            string content;
            if (string.Equals(mode, "ModbusWrite", StringComparison.OrdinalIgnoreCase))
            {
                int addr = node.GetInt("MbAddress", 0);
                int val = node.GetInt("MbValue", 0);
                if (addr < 0 || addr > 65535 || val < 0 || val > 65535)
                {
                    p.Summary = "发送数据：Modbus 地址 / 写入值需在 0~65535（当前 "
                              + addr.ToString(CultureInfo.InvariantCulture) + " / "
                              + val.ToString(CultureInfo.InvariantCulture) + "）";
                    return false;
                }

                if (autoConnect && !EnsureCommConnected(commName, "发送数据", p)) return false;

                OpResult wr = CommRegistry.ModbusWrite(commName, (ushort)addr, (ushort)val);
                if (!wr.Success)
                {
                    p.Summary = "发送数据：" + Tail(wr.Message);
                    return false;
                }

                content = string.Format(CultureInfo.InvariantCulture, "寄存器 {0} = {1}", addr, val);
            }
            else
            {
                content = string.Equals(mode, "Format", StringComparison.OrdinalIgnoreCase)
                    ? ExpandCommFormat(node.GetString("Format", ""), s)
                    : node.GetString("Text", "");

                if (autoConnect && !EnsureCommConnected(commName, "发送数据", p)) return false;

                OpResult sr = CommRegistry.SendText(commName, content);
                if (!sr.Success)
                {
                    p.Summary = "发送数据：" + Tail(sr.Message);
                    return false;
                }
            }

            CaptureCommFrame(s, p);
            p.Summary = "发送数据：" + commName + " ← " + ClipText(content, 80);
            return true;
        }

        /// <summary>
        /// 接收数据：按名称绑定一条通讯，取最近一条文本，或读 Modbus 保持寄存器（FC03）拼成可读文本。
        /// </summary>
        private bool RunReceiveData(FlowRunSession s, FlowNode node, FlowNodeProgress p)
        {
            string commName = ResolveComm(node, "接收数据", p);
            if (commName == null) return false;

            string mode = node.GetString("Mode", "TcpText");
            bool autoConnect = node.GetBool("AutoConnect", true);
            if (autoConnect && !EnsureCommConnected(commName, "接收数据", p)) return false;

            string content;
            if (string.Equals(mode, "ModbusRead", StringComparison.OrdinalIgnoreCase))
            {
                int addr = node.GetInt("MbAddress", 0);
                int cnt = node.GetInt("MbCount", 1);
                if (addr < 0 || addr > 65535)
                {
                    p.Summary = "接收数据：Modbus 起始地址需在 0~65535（当前 "
                              + addr.ToString(CultureInfo.InvariantCulture) + "）";
                    return false;
                }
                if (cnt < 1) cnt = 1;
                if (cnt > 125) cnt = 125;      // FC03 单次上限

                OpResult<ushort[]> rr = CommRegistry.ModbusRead(commName, (ushort)addr, (ushort)cnt);
                if (!rr.Success)
                {
                    p.Summary = "接收数据：" + Tail(rr.Message);
                    return false;
                }

                ushort[] regs = rr.Value;
                if (regs == null) regs = new ushort[0];

                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < regs.Length; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(regs[i].ToString(CultureInfo.InvariantCulture));
                }
                content = sb.Length > 0 ? sb.ToString() : "(空)";
            }
            else
            {
                int timeout = node.GetInt("TimeoutMs", 2000);
                if (timeout < 0) timeout = 0;

                // CommRegistry 只保留「最近一条」文本，这里在超时窗口内轮询等它出现
                string text = CommRegistry.LastReceived(commName);
                var sw = Stopwatch.StartNew();
                while (string.IsNullOrEmpty(text) && sw.ElapsedMilliseconds < timeout)
                {
                    System.Threading.Thread.Sleep(20);
                    text = CommRegistry.LastReceived(commName);
                }
                sw.Stop();

                if (string.IsNullOrEmpty(text))
                {
                    // 收不到数据不是算法失败：判「跳过」并在摘要里说清，避免整条流程被误判 NG
                    CaptureCommFrame(s, p);
                    p.Status = FlowNodeStatus.Skipped;
                    p.Summary = "接收数据：" + commName + " → 无数据（"
                              + timeout.ToString(CultureInfo.InvariantCulture)
                              + "ms 内未收到内容，按跳过处理，不判 NG）";
                    return true;
                }
                content = text;
            }

            CaptureCommFrame(s, p);
            p.Summary = "接收数据：" + commName + " → " + ClipText(content, 80);
            return true;
        }

        /// <summary>取出节点绑定的通讯名并做存在性校验；失败时写好摘要并返回 null。</summary>
        private static string ResolveComm(FlowNode node, string prefix, FlowNodeProgress p)
        {
            string name = node.GetString("CommName", "");
            if (string.IsNullOrEmpty(name))
            {
                p.Summary = prefix + "：未绑定通讯（请到「通讯」界面添加并保存后，再在本节点选择）";
                return null;
            }
            if (CommRegistry.Config(name) == null)
            {
                p.Summary = prefix + "：没有名为「" + name + "」的通讯（请到「通讯」界面检查配置）";
                return null;
            }
            return name;
        }

        /// <summary>AutoConnect 时未连接就连一次；连接失败写摘要并返回 false（已连接直接 true）。</summary>
        private static bool EnsureCommConnected(string commName, string prefix, FlowNodeProgress p)
        {
            if (CommRegistry.IsConnected(commName)) return true;

            OpResult r = CommRegistry.Connect(commName);
            if (r.Success) return true;

            p.Summary = prefix + "：" + Tail(r.Message);
            return false;
        }

        /// <summary>克隆当前工作帧供预览区显示（通讯节点不改图，也不调 ReplaceFrame）。</summary>
        private static void CaptureCommFrame(FlowRunSession s, FlowNodeProgress p)
        {
            if (s.Current == null) return;

            // 连续运行（CaptureFrames=false）下不克隆单节点预览帧，避免每周期 LOH 压力导致卡顿
            if (s.CaptureFrames)
            {
                if (p.Frame != null) p.Frame.Dispose();
                try { p.Frame = (Bitmap)s.Current.Clone(); }
                catch (Exception) { p.Frame = null; }
            }
        }

        /// <summary>
        /// 展开格式化模板：{Summary} 上游摘要 / {OkNg} OK·NG / {Time} 时间。
        /// 取不到的占位符<strong>原样保留</strong>（不抛异常、不填空串）。
        /// </summary>
        private static string ExpandCommFormat(string format, FlowRunSession s)
        {
            string text = format ?? "";

            string summary = "";
            if (s != null && s.Summaries != null && s.Summaries.Count > 0)
                summary = s.Summaries[s.Summaries.Count - 1] ?? "";

            if (summary.Length > 0)
                text = text.Replace("{Summary}", summary);

            text = text.Replace("{OkNg}", (s != null && s.Passed) ? "OK" : "NG");
            text = text.Replace("{Time}", DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture));
            return text;
        }

        /// <summary>把收发内容压成一行：换行转空格，超长截断并附总长度。</summary>
        private static string ClipText(string text, int max)
        {
            if (string.IsNullOrEmpty(text)) return "(空)";

            string one = text.Replace("\r", " ").Replace("\n", " ");
            if (one.Length <= max) return one;

            return one.Substring(0, max) + "…（共 "
                 + text.Length.ToString(CultureInfo.InvariantCulture) + " 字符）";
        }

        /// <summary>
        /// 保证原生 OCR 上下文里已灌入 dir 目录下的字符模板，返回<strong>结构化加载结果</strong>。
        /// 目录不存在时自动创建。原生模板挂在算法上下文上且没有 save/load，
        /// 因此用「目录路径 + 逐文件指纹」+ 原生模板计数双重判断是否需要重载：
        /// 指纹变化（模板文件增删 / 改名 / 改内容）或原生计数为 0（句柄被重建）都会清空重灌。
        /// </summary>
        /// <remarks>
        /// 【与旧实现的三点差异，全部是"不再静默"的落地】
        /// ① 返回结构化结果（四态：空 / 全失败 / 部分失败 / 正常），不再返回裸 int；
        /// ② 单张模板读不出来<strong>不再中断整批加载</strong>，而是记入 Failures 清单后继续 ——
        ///    否则"1 张坏图"会让另外 11 张好图也用不上；
        /// ③ 读失败的原因逐条保留（0 字节 / 不是有效图像 / 尺寸异常 / 读盘异常），
        ///    只有"给不出原因"的失败才允许只报计数。
        /// </remarks>
        private OpResult<OcrTemplateLoadResult> EnsureOcrTemplates(string dir)
        {
            if (string.IsNullOrWhiteSpace(dir)) dir = "OCRModel";

            if (!Directory.Exists(dir))
            {
                try { Directory.CreateDirectory(dir); }
                catch (Exception ex)
                {
                    return OpResult<OcrTemplateLoadResult>.Fail(
                        "无法创建模板目录 " + dir + "：" + ex.Message);
                }
            }

            List<string> files = ListOcrTemplateFiles(dir);
            string fingerprint = OcrTemplateFingerprint(dir, files);

            OpResult<int> cur = _algo.OcrTemplateCount();
            if (!cur.Success) return OpResult<OcrTemplateLoadResult>.Fail(cur.Message);

            // 命中缓存：目录、指纹都没变，且原生侧确实挂着模板 → 直接复用上一轮结果
            if (cur.Value > 0
                && _ocrTemplateResult != null
                && string.Equals(dir, _ocrTemplateDir, StringComparison.OrdinalIgnoreCase)
                && string.Equals(fingerprint, _ocrTemplateFingerprint, StringComparison.Ordinal))
            {
                _ocrTemplateResult.FromCache = true;
                return OpResult<OcrTemplateLoadResult>.Ok(_ocrTemplateResult);
            }

            if (cur.Value > 0)
            {
                OpResult clr = _algo.ClearOcrTemplates();
                if (!clr.Success) return OpResult<OcrTemplateLoadResult>.Fail(clr.Message);
            }

            var result = new OcrTemplateLoadResult
            {
                Dir = dir,
                FileCount = files.Count,
                ScannedAt = DateTime.Now
            };

            foreach (string f in files)
            {
                string label = Path.GetFileNameWithoutExtension(f);
                if (string.IsNullOrEmpty(label))
                {
                    result.Failures.Add(new OcrTemplateFailure
                    {
                        FileName = Path.GetFileName(f),
                        Reason = "文件名为空"
                    });
                    continue;
                }

                string reason;
                using (Bitmap bmp = LoadTemplateBitmap(f, out reason))
                {
                    if (bmp == null)
                    {
                        result.Failures.Add(new OcrTemplateFailure
                        {
                            FileName = Path.GetFileName(f),
                            Reason = string.IsNullOrEmpty(reason) ? "不是有效图像" : reason
                        });
                        continue;
                    }

                    OpResult r = _algo.AddOcrTemplate(bmp, label);
                    if (!r.Success)
                    {
                        // 单张失败也只是"这张没进去"，不放弃整批（坏一张拖垮全库是最糟的体验）
                        result.Failures.Add(new OcrTemplateFailure
                        {
                            FileName = Path.GetFileName(f),
                            Reason = "原生加载失败（" + r.Message + "）"
                        });
                        continue;
                    }
                    result.LoadedCount++;
                }
            }

            // 一张都没成功时，下一帧必须重试（不能把"全失败"缓存成"已加载 0 个"）
            if (result.LoadedCount > 0)
            {
                _ocrTemplateDir = dir;
                _ocrTemplateFingerprint = fingerprint;
                _ocrTemplateResult = result;
            }
            else
            {
                _ocrTemplateDir = dir;
                _ocrTemplateFingerprint = "";
                _ocrTemplateResult = result;
            }

            result.FromCache = false;
            return OpResult<OcrTemplateLoadResult>.Ok(result);
        }

        /// <summary>
        /// 模板重载指纹：<strong>逐文件 <c>文件名 + 字节数 + mtimeTicks</c> 的累积哈希</strong>。
        /// <para>
        /// 【为什么必须含文件名】旧指纹是 <c>dir|文件数|最新mtime</c>。Windows 下**重命名保留
        /// <c>LastWriteTime</c>**，于是 <c>A.png → B.png</c> 之后"文件数不变、最新 mtime 不变"
        /// → 指纹不变 → 不重载 → <strong>运行时仍然用旧标签 A</strong>。
        /// 这是个静默错误：界面上（管理窗体自己扫目录）看着一切正常，只有识别结果不对。
        /// </para>
        /// <para>
        /// 【为什么可复现】<paramref name="files"/> 来自 <see cref="ListOcrTemplateFiles"/>，
        /// 它已按文件名 Ordinal 排序，所以同样的磁盘状态必然得到同样的哈希
        /// （指纹一旦引入不确定性，就会变成"每次运行都重灌模板"，性能抖动但结果不变 —— 更难查）。
        /// </para>
        /// </summary>
        private static string OcrTemplateFingerprint(string dir, List<string> files)
        {
            unchecked
            {
                long hash = 17;
                if (files != null)
                {
                    foreach (string f in files)
                    {
                        long length = 0;
                        long ticks = 0;
                        try
                        {
                            var fi = new FileInfo(f);
                            length = fi.Length;
                            ticks = fi.LastWriteTimeUtc.Ticks;
                        }
                        catch (Exception)
                        {
                            // 读不到属性也要参与哈希（用 0 占位），避免"文件消失"被当成"没变化"
                        }

                        string key = Path.GetFileName(f) + "|"
                                     + length.ToString(CultureInfo.InvariantCulture) + "|"
                                     + ticks.ToString(CultureInfo.InvariantCulture);

                        for (int i = 0; i < key.Length; i++)
                            hash = hash * 31 + key[i];
                        hash = hash * 31 + 0x9E3779B9;   // 分隔不同文件，防"拼接后同串"
                    }
                }
                return dir + "|" + (files != null ? files.Count : 0) + "|" + hash.ToString("X16");
            }
        }

        /// <summary>列出模板目录下的图片文件（按文件名排序，保证模板顺序稳定）。</summary>
        private static List<string> ListOcrTemplateFiles(string dir)
        {
            var list = new List<string>();
            string[] all;
            try { all = Directory.GetFiles(dir); }
            catch (Exception) { return list; }

            foreach (string f in all)
            {
                string ext = Path.GetExtension(f);
                if (string.IsNullOrEmpty(ext)) continue;
                ext = ext.ToLowerInvariant();
                if (ext == ".bmp" || ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".tif" || ext == ".tiff")
                    list.Add(f);
            }
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }

        /// <summary>
        /// 读一张模板图并复制一份（复制是为了立刻释放文件句柄，让用户可以随时替换模板文件）。
        /// 读取失败返回 null（跳过该文件，不判 NG）。
        /// </summary>
        private static Bitmap LoadTemplateBitmap(string path)
        {
            string reason;
            return LoadTemplateBitmap(path, out reason);
        }

        /// <summary>
        /// 读一张模板图并给出<strong>可读的失败原因</strong>（供"失败清单"逐条展示）。
        /// <para>
        /// 【为什么必须给出原因】只报"1 个文件读取失败"等于没报 —— 用户既不知道是哪个文件，
        /// 也不知道该删掉它还是换个格式。四类原因必须在文案上可区分：0 字节 / 不是图像 /
        /// 尺寸异常 / 读盘异常。
        /// </para>
        /// </summary>
        private static Bitmap LoadTemplateBitmap(string path, out string reason)
        {
            reason = "";
            try
            {
                var fi = new FileInfo(path);
                if (fi.Exists && fi.Length == 0)
                {
                    reason = "0 字节";
                    return null;
                }

                // 先读后拷（不锁文件）+ 读取侧归一化成身份灰度。
                // 【为什么读取侧也要归一化】GDI+ 的 PNG 解码不保真像素格式（8bpp 存盘可能解回
                // 32bppArgb），而手工放进目录的图（截图工具存的 24bpp PNG）压根不经过入库路径。
                // 只修写入侧等于只修一半：凡是从磁盘读回来交给算法库的，这里都必须转一次。
                Bitmap raw = TemplateStore.LoadBitmapNoLock(path);
                if (raw == null)
                {
                    reason = "不是有效图像";
                    return null;
                }

                if (!TemplateStore.IsIdentityGray(raw))
                {
                    Bitmap gray = TemplateStore.ConvertToIdentityGray(raw);
                    raw.Dispose();
                    raw = gray;
                }

                // 尺寸过小/过大的模板即便读出来也认不了字，按"读取失败"计入清单更诚实
                if (raw.Width < OcrCharStore.MinSide || raw.Height < OcrCharStore.MinSide)
                {
                    reason = "尺寸过小（" + raw.Width + "×" + raw.Height + "）";
                    raw.Dispose();
                    return null;
                }
                if (raw.Width > OcrCharStore.MaxSide * 4 || raw.Height > OcrCharStore.MaxSide * 4)
                {
                    reason = "尺寸异常（" + raw.Width + "×" + raw.Height + "）";
                    raw.Dispose();
                    return null;
                }

                return raw;
            }
            catch (Exception ex)
            {
                reason = "读取异常（" + ex.Message + "）";
                return null;
            }
        }

        // ---------------------- 位置修正（FR-ALG-08 / 09 / 10）----------------------

        private bool RunPositionCorrect(FlowRunSession s, FlowNode node, FlowNodeProgress p)
        {
            // 预览帧：连续运行（CaptureFrames=false）时 StepNext 不会留帧，
            // 这里显式克隆当前帧，保证流程预览区能显示该检测节点的结果图。
            if (s.CaptureFrames && s.Current != null)
            {
                if (p.Frame != null) p.Frame.Dispose();
                try { p.Frame = (Bitmap)s.Current.Clone(); }
                catch (Exception) { p.Frame = null; }
            }

            // ---- 位姿来源解析（端子连线优先）----
            // Pose 输入端子连到哪个匹配节点，就用【它】的位姿（端子级语义）；
            // 未连 Pose 线时保持历史语义：上游最近一个匹配节点的全局位姿。
            // 多条 Pose 线时取链上执行顺序最靠后的（位姿最新）。
            string poseSrc = FindPoseSourceNode(s, node);
            double curX = s.PoseCx, curY = s.PoseCy, curA = s.PoseAngle, curS = s.PoseScale;
            bool hasPose = s.HasPose;
            if (poseSrc != null && s.TryGetNodePose(poseSrc, out double[] pp))
            {
                curX = pp[0]; curY = pp[1]; curA = pp[2]; curS = pp[3];
                hasPose = true;
            }

            string refSource = node.GetString("RefSource", "Auto");
            bool autoFirstLatch = false;

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
                if (!hasPose)
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

                    // 归因提示：若链上存在匹配类节点却排在本节点【之后】执行，则点名该节点并
                    // 建议把本节点移到其下游；否则维持原始「上游无可用定位结果」文案。
                    // （SmokeAlgo T11 断言「上游无可用定位结果」子串，其链路无匹配节点，走 else 分支；
                    //   两个分支都 return true、都置 Skipped，遵循 P0-3「不判 NG」的裁决。）
                    FlowNode lateMatch = FindMatchNodeAfter(s, node);
                    if (lateMatch != null)
                    {
                        string mn = string.IsNullOrEmpty(lateMatch.Name) ? lateMatch.Id : lateMatch.Name;
                        p.Summary = "位置修正：匹配节点「" + mn
                            + "」排在本节点之后执行，取不到位姿；请将本节点移到其下游（画布右侧）";
                    }
                    else
                    {
                        p.Summary = "位置修正：上游无可用定位结果，本次跳过修正（未判 NG）";
                    }
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
                    rx = curX;
                    ry = curY;
                    ra = curA;
                    _autoRefs[node.Id] = new[] { rx, ry, ra };
                    autoFirstLatch = true;
                }
            }

            double dx = hasPose ? (curX - rx) : 0.0;
            double dy = hasPose ? (curY - ry) : 0.0;
            double dAngle = hasPose ? (curA - ra) : 0.0;
            double dScale = hasPose ? curS : 1.0;

            // EnableAngle = 0：只平移不旋转
            if (!node.GetBool("EnableAngle", true)) dAngle = 0.0;

            // 覆盖式写入（链内多个位置修正节点以最后一个为准，不累加）
            s.Transform.Set(rx, ry, ra, dx, dy, dAngle, dScale);

            // 可视化：基准坐标系（X 轴红、Y 轴绿、带箭头方向，随基准角度旋转）
            foreach (IGeometryShape g in FlowShapes.CoordinateSystem(rx, ry, ra, FlowShapes.DefaultCrossArm * 1.5))
                p.Shapes.Add(g);

            p.Summary = string.Format(CultureInfo.InvariantCulture,
                "位置修正：基准({0:F1},{1:F1},{2:F1}°) 实测({3:F1},{4:F1},{5:F1}°) dX={6:F1} dY={7:F1} dA={8:F2}°",
                rx, ry, ra, curX, curY, curA, dx, dy, dAngle);
            if (autoFirstLatch)
                p.Summary += "（Auto 首帧示教：基准=当前位姿，偏差 0 属预期）";

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

            // ---- 像素级位置修正：把工件对齐回基准姿态 ----
            // 此前本节点只写 s.Transform（坐标重映射），只有带 ROI 的下游节点能感知修正；
            // 修正节点之后预览与无 ROI 下游看到的仍是未修正原图（用户反馈『传入了匹配姿态
            // 却没有修正』）。现把偏差以像素级反变换烘进工作帧：
            //   绕【检测中心】旋转 -dAngle，再平移到【基准位置】——工件回到基准姿态。
            // 烘进像素后 s.Transform 复位为「无修正」，下游 ROI 不做二次叠加变换。
            double dAngleNorm = dAngle;
            if (dAngleNorm > 180.0) dAngleNorm -= 360.0;
            else if (dAngleNorm < -180.0) dAngleNorm += 360.0;

            bool needWarp = Math.Abs(dx) > 0.05 || Math.Abs(dy) > 0.05 || Math.Abs(dAngleNorm) > 0.05;
            if (needWarp && s.Current != null)
            {
                Bitmap warped = WarpToReference(s.Current, curX, curY, rx, ry, dAngleNorm);
                s.Current.Dispose();
                s.Current = warped;

                // 修正已烘进像素：复位坐标重映射，下游 ROI 不做二次变换
                s.Transform.Reset();

                // 预览帧刷新为修正后的图像。连续运行（CaptureFrames=false）下跳过克隆，避免每周期 LOH 压力导致卡顿
                if (s.CaptureFrames && s.Current != null)
                {
                    if (p.Frame != null) p.Frame.Dispose();
                    try { p.Frame = (Bitmap)s.Current.Clone(); }
                    catch (Exception) { p.Frame = null; }
                }

                p.Summary += "（已对齐回基准姿态）";
            }

            return true;
        }

        /// <summary>
        /// 找到连到 <paramref name="node"/> 的 Pose 输入端子的上游节点 Id（端子级位姿来源）；
        /// 多条 Pose 线时取链上执行顺序最靠后的一个（位姿最新）。未连接返回 null。
        /// </summary>
        private static string FindPoseSourceNode(FlowRunSession s, FlowNode node)
        {
            if (s?.Flow?.Links == null || node == null) return null;

            string best = null;
            int bestIdx = -1;
            foreach (FlowLink link in s.Flow.Links)
            {
                if (link == null || link.ToNodeId != node.Id) continue;
                if (!string.Equals(link.ToPort, "Pose", StringComparison.OrdinalIgnoreCase)) continue;

                string fromId = link.FromNodeId;
                if (string.IsNullOrEmpty(fromId)) continue;

                // 同一节点多条 Pose 线：取执行顺序最靠后的（位姿最新）
                int idx = -1;
                if (s.Chain != null)
                    for (int i = 0; i < s.Chain.Count; i++)
                        if (string.Equals(s.Chain[i]?.Id, fromId, StringComparison.Ordinal)) { idx = i; break; }

                if (idx >= bestIdx) { bestIdx = idx; best = fromId; }
            }
            return best;
        }

        /// <summary>
        /// 像素级位置修正：源图中位于 (curX, curY, curAngle) 的工件，绕检测中心旋回 -dAngle
        /// 并平移到基准位置 (refX, refY)——「检测中心落到基准位置、角度回到基准角度」。
        /// 输出与输入同尺寸；旋转露出的边角填黑。
        /// </summary>
        private static Bitmap WarpToReference(Bitmap src, double curX, double curY,
                                              double refX, double refY, double dAngle)
        {
            var dst = new Bitmap(src.Width, src.Height, PixelFormat.Format24bppRgb);
            using (Graphics g = Graphics.FromImage(dst))
            {
                g.Clear(Color.Black);
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.PixelOffsetMode = PixelOffsetMode.Half;

                // 变换复合：M = T(ref) · R(-dAngle) · T(-cur)
                // 源点 cur → 旋转回正 → 落到 ref（y 向下、顺时针为正，与全库角度约定一致）
                g.TranslateTransform((float)refX, (float)refY);
                g.RotateTransform(-(float)dAngle);
                g.TranslateTransform((float)-curX, (float)-curY);

                g.DrawImage(src, 0, 0);
            }
            return dst;
        }

        /// <summary>
        /// 在拓扑有序执行链 <see cref="FlowRunSession.Chain"/> 中，查找排在 <paramref name="node"/>
        /// <strong>之后</strong>执行的第一个匹配类节点（形状匹配 / 模板匹配）；找不到返回 null。
        /// </summary>
        /// <remarks>
        /// 用途：Auto 位置修正因 <c>!s.HasPose</c> 跳过时给出「可归因」的提示 ——
        /// 此时若还存在匹配节点，只可能是它排在了本节点之后（尚未产出位姿），
        /// 据此引导用户把位置修正节点移到匹配节点下游。
        /// </remarks>
        private static FlowNode FindMatchNodeAfter(FlowRunSession s, FlowNode node)
        {
            if (s == null || s.Chain == null || node == null) return null;

            int start = -1;
            for (int i = 0; i < s.Chain.Count; i++)
            {
                FlowNode c = s.Chain[i];
                if (c == null) continue;
                if (ReferenceEquals(c, node)
                    || string.Equals(c.Id, node.Id, StringComparison.Ordinal))
                {
                    start = i;
                    break;
                }
            }
            if (start < 0) return null;

            for (int i = start + 1; i < s.Chain.Count; i++)
            {
                FlowNodeType t;
                if (s.Chain[i] != null && s.Chain[i].TryGetType(out t)
                    && (t == FlowNodeType.ShapeMatch || t == FlowNodeType.TemplateMatch))
                {
                    return s.Chain[i];
                }
            }
            return null;
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

            // 预览帧：连续运行（CaptureFrames=false）时 StepNext 不会留帧，
            // 这里显式克隆当前帧，保证流程预览区能显示该节点的结果图（与位置修正同款）。
            if (s.CaptureFrames && s.Current != null)
            {
                if (p.Frame != null) p.Frame.Dispose();
                try { p.Frame = (Bitmap)s.Current.Clone(); }
                catch (Exception) { p.Frame = null; }
            }

            var train = new ShapeTrainParam
            {
                Contrast = node.GetDouble("Contrast", 30),
                MaxFeatures = node.GetInt("MaxFeatures", 1500),
                MinFeatureDist = node.GetDouble("MinFeatureDist", 2)
            };

            // 训练参数签名：<strong>只含三个训练参数</strong>，不含 ROI、不含匹配参数。
            // 旧实现把 ApplyRoi 映射后的 ROI 塞进签名，上游位置修正没触发 warp（偏差 < 0.05 阈值、
            // 但 Transform.IsValid 仍为 true）时映射结果每帧可能 ±1px 抖动 → 签名每帧变 → 每帧重训。
            // 现在模板来自落盘资产、ROI 变了由「模板戳 / 资产不匹配」判定，这条抖动路径被彻底切断。
            string flowId = (s.Flow != null) ? s.Flow.Id : "";
            Rectangle configuredRoi = ConfiguredRoi(node, "TemplateX", "TemplateY", "TemplateW", "TemplateH");
            string stamp = TemplateStore.StampOf(flowId, node.Id);
            string sig = BuildModelSignature(stamp, ShapeTrainSignature(train));

            TrainedModel cache;
            bool cached = _models.TryGetValue(node.Id, out cache);
            bool signatureSame = cached && cache != null
                                 && string.Equals(cache.Signature, sig, StringComparison.Ordinal)
                                 && cache.Template != null;
            bool slotMine = string.Equals(_shapeModelOwner, node.Id, StringComparison.Ordinal);

            int retrainMs = 0;
            bool retrained = false;   // 本轮是否真的重训过（Release 下重训可能 <1ms，不能靠 ms>0 判断）

            // 需要重训的三种情况：① 尚未训练；② 训练参数变了 / 模板资产被替换（戳变）；③ 原生槽位被抢占。
            if (!signatureSame || !slotMine)
            {
                if (!signatureSame)
                {
                    // 唯一训练来源 = 落盘资产。<strong>绝不再拿当前帧当示教帧</strong>（隐式示教的病根：
                    // 拿当前帧当模板又在同一帧上匹配 = 自匹配，得分恒 ≈1.0，MinScore 形同虚设）。
                    TemplateLoadError loadError;
                    string loadMessage;
                    TemplateAsset asset = TemplateStore.Load(flowId, node.Id, "ShapeMatch", configuredRoi,
                                                             out loadError, out loadMessage);
                    if (asset == null)
                    {
                        if (cache != null && cache.Template != null) cache.Template.Dispose();
                        _models.Remove(node.Id);

                        // DEF-03：无模板资产 → 节点 NG 且文案明确，流程其余节点继续执行
                        p.Summary = "形状匹配：" + DescribeTemplateFailure(loadError, loadMessage);
                        return false;
                    }

                    if (cache == null)
                    {
                        cache = new TrainedModel();
                        _models[node.Id] = cache;
                    }
                    if (cache.Template != null) cache.Template.Dispose();

                    cache.Template = asset.Template;   // 所有权移交缓存，随 ClearTrainedModels 释放
                    cache.Meta = asset.Meta;
                    cache.Signature = sig;
                }

                // 槽位上若已是别人的模型，先显式释放再训练，避免原生侧叠加占用（句柄内存泄漏）
                if (!string.IsNullOrEmpty(_shapeModelOwner) && !slotMine)
                    _algo.ClearShapeModel();

                var tsw = Stopwatch.StartNew();
                var cr = _algo.CreateShapeModel(cache.Template, Rectangle.Empty, train);
                tsw.Stop();
                retrainMs = (int)tsw.ElapsedMilliseconds;

                if (cr == null || !cr.Success)
                {
                    p.Summary = "形状匹配：训练失败(" + (cr != null ? cr.Message : "无返回") + ")"
                                + (retrainMs > 0 ? "（重训 " + retrainMs + " ms）" : "");
                    return false;
                }

                _shapeModelOwner = node.Id;
                retrained = true;
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
                // 框标签：序号 + 分数（UI-R04 的「标准」档；详细度切换在预览区做，
                // 这里只给出完整信息，简略档由预览区按 Label 前缀裁剪）
                string label = string.Format(CultureInfo.InvariantCulture, "#{0} {1:0.00}", i + 1, m.Score);
                p.Shapes.Add(FlowShapes.MatchBox(m.Cx, m.Cy, m.Width, m.Height, m.Angle, i == 0, label));
            }

            // 主判据：目标数 / 最高分 / 最高分目标坐标（结果区第二行与画布标签同源）
            p.Metrics.TargetCount = matches.Count;
            if (matches.Count > 0)
            {
                p.Metrics.TopScore = matches[0].Score;
                p.Metrics.TopCx = matches[0].Cx;
                p.Metrics.TopCy = matches[0].Cy;
                p.Metrics.AngleDeg = matches[0].Angle;
            }

            if (matches.Count == 0)
            {
                p.Summary = "形状匹配：未找到目标" + RetrainSuffix(retrained, retrainMs);
                return false;
            }

            // 记录实时位姿，供下游位置修正节点取基准
            s.SetPose(matches[0].Cx, matches[0].Cy, matches[0].Angle, matches[0].Scale);
            s.SetNodePose(node.Id, matches[0].Cx, matches[0].Cy, matches[0].Angle, matches[0].Scale);

            // 摘要里带重训耗时：多形状匹配节点共用原生单槽位时每帧会重训 N 次，
            // 现场只能靠这一项把"掉帧"归因到"重训"而不是"相机慢"。
            p.Summary = string.Format(CultureInfo.InvariantCulture, "形状匹配：{0} 个，最高 {1:F3}",
                                      matches.Count, matches[0].Score) + RetrainSuffix(retrained, retrainMs);
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

            // 预览帧：连续运行（CaptureFrames=false）时 StepNext 不会留帧，
            // 这里显式克隆当前帧，保证流程预览区能显示该节点的结果图（与位置修正同款）。
            if (s.CaptureFrames && s.Current != null)
            {
                if (p.Frame != null) p.Frame.Dispose();
                try { p.Frame = (Bitmap)s.Current.Clone(); }
                catch (Exception) { p.Frame = null; }
            }

            double minScore = node.GetDouble("MinScore", 0.8);

            // 训练来源 = 落盘资产（不再是当前帧）。NCC 没有训练参数，签名里只有模板戳。
            string flowId = (s.Flow != null) ? s.Flow.Id : "";
            Rectangle configuredRoi = ConfiguredRoi(node, "TemplateX", "TemplateY", "TemplateW", "TemplateH");
            string stamp = TemplateStore.StampOf(flowId, node.Id);
            string sig = BuildModelSignature(stamp, "NCC");

            TrainedModel cache;
            bool cached = _models.TryGetValue(node.Id, out cache);
            bool signatureSame = cached && cache != null
                                 && string.Equals(cache.Signature, sig, StringComparison.Ordinal)
                                 && cache.Template != null;
            bool slotMine = string.Equals(_nccModelOwner, node.Id, StringComparison.Ordinal);

            int retrainMs = 0;
            bool retrained = false;   // 本轮是否真的重训过（Release 下重训可能 <1ms，不能靠 ms>0 判断）

            if (!signatureSame || !slotMine)
            {
                if (!signatureSame)
                {
                    TemplateLoadError loadError;
                    string loadMessage;
                    TemplateAsset asset = TemplateStore.Load(flowId, node.Id, "TemplateMatch", configuredRoi,
                                                             out loadError, out loadMessage);
                    if (asset == null)
                    {
                        if (cache != null && cache.Template != null) cache.Template.Dispose();
                        _models.Remove(node.Id);

                        // DEF-03：无模板资产 → 节点 NG 且文案明确，流程其余节点继续执行
                        p.Summary = "模板匹配：" + DescribeTemplateFailure(loadError, loadMessage);
                        return false;
                    }

                    if (cache == null)
                    {
                        cache = new TrainedModel();
                        _models[node.Id] = cache;
                    }
                    if (cache.Template != null) cache.Template.Dispose();

                    cache.Template = asset.Template;
                    cache.Meta = asset.Meta;
                    cache.Signature = sig;
                }

                var tsw = Stopwatch.StartNew();
                OpResult tr = _algo.TrainTemplate(cache.Template, Rectangle.Empty);
                tsw.Stop();
                retrainMs = (int)tsw.ElapsedMilliseconds;

                if (tr == null || !tr.Success)
                {
                    p.Summary = "模板匹配：训练失败（" + (tr != null ? tr.Message : "无返回") + "）"
                                + RetrainSuffix(retrained, retrainMs);
                    return false;
                }

                _nccModelOwner = node.Id;
                retrained = true;
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
                    // 未达阈值也要把"最高得分"报出来：这是调阈值时唯一的依据
                    p.Metrics.TargetCount = 0;
                    p.Metrics.NccScore = score;
                    p.Summary = string.Format(CultureInfo.InvariantCulture,
                        "模板匹配：未找到目标，最高得分 {0:F3}", score) + RetrainSuffix(retrained, retrainMs);
                    return false;
                }

                // 裁剪搜索时把结果坐标加回搜索区左上角
                double bx = r.X + (useRoi ? search.X : 0);
                double by = r.Y + (useRoi ? search.Y : 0);

                var box = new Rectangle((int)Math.Round(bx), (int)Math.Round(by),
                                        Math.Max(1, (int)Math.Round(r.Width)),
                                        Math.Max(1, (int)Math.Round(r.Height)));
                p.Shapes.Add(FlowShapes.NccBox(box, true,
                    string.Format(CultureInfo.InvariantCulture, "#1 {0:0.00}", score)));

                // 主判据：得分 + 角度（NCC 只平移，角度恒 0）+ 目标数
                p.Metrics.TargetCount = 1;
                p.Metrics.NccScore = score;
                p.Metrics.AngleDeg = 0.0;

                // 记录实时位姿（NCC 不支持旋转 / 缩放，角度恒 0、缩放恒 1）
                s.SetPose(bx + r.Width * 0.5, by + r.Height * 0.5, 0.0, 1.0);
                s.SetNodePose(node.Id, bx + r.Width * 0.5, by + r.Height * 0.5, 0.0, 1.0);

                p.Summary = string.Format(CultureInfo.InvariantCulture,
                    "模板匹配：得分 {0:F3} @({1:F0},{2:F0})", score, bx, by) + RetrainSuffix(retrained, retrainMs);
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

            // 预览帧：连续运行（CaptureFrames=false）时 StepNext 不会留帧，
            // 这里显式克隆当前帧，保证流程预览区能显示该检测节点的结果图。
            if (s.CaptureFrames && s.Current != null)
            {
                if (p.Frame != null) p.Frame.Dispose();
                try { p.Frame = (Bitmap)s.Current.Clone(); }
                catch (Exception) { p.Frame = null; }
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
            Bitmap cropped = null;
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

                // 可选 ROI：限制分析区域。用户输入为图像坐标，与匹配节点同款顺序换算 ——
                // 先减去 ROI 裁剪偏移得到工作帧坐标，再应用位置修正变换（FR-ALG-09：让 Blob
                // 搜索区跟随工件修正后的位姿），最后夹到图像范围内；
                // ROI 无效（宽高<=0 或完全在图外）则退化为整帧分析。
                Bitmap analyzeOn = input;
                Rectangle usedRoi = Rectangle.Empty;

                int roiX = node.GetInt("RoiX", 0);
                int roiY = node.GetInt("RoiY", 0);
                int roiW = node.GetInt("RoiW", 0);
                int roiH = node.GetInt("RoiH", 0);

                if (roiW > 0 && roiH > 0)
                {
                    // 用户坐标 → 工作帧坐标（减裁剪偏移）→ 位置修正变换（与 ApplyRoi 一致）；
                    // 无上游定位时 Transform 未写入，ApplyToRect 原样返回，行为与改造前完全一致。
                    int wx = (int)Math.Round(roiX - s.OffsetX);
                    int wy = (int)Math.Round(roiY - s.OffsetY);
                    var userRect = s.Transform.ApplyToRect(new Rectangle(wx, wy, roiW, roiH));

                    int rx = Math.Max(0, Math.Min(userRect.X, input.Width - 1));
                    int ry = Math.Max(0, Math.Min(userRect.Y, input.Height - 1));
                    int rw = Math.Max(0, Math.Min(userRect.Width, input.Width - rx));
                    int rh = Math.Max(0, Math.Min(userRect.Height, input.Height - ry));
                    if (rw > 0 && rh > 0)
                    {
                        var rect = new Rectangle(rx, ry, rw, rh);
                        cropped = CropTemplate(input, rect);
                        analyzeOn = cropped;
                        usedRoi = rect;
                    }
                }

                // 先多取一些，本地按宽高 / 圆度过滤后再截断到 MaxCount
                int cap = maxCount > 0 ? Math.Min(maxCount * 4 + 8, 256) : 256;

                var br = _algo.AnalyzeBlobs(analyzeOn, minArea, maxArea, cap);
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

                // ROI 裁剪后，Blob 坐标相对裁剪图；加回裁剪区左上角还原到工作帧坐标
                // 标签 = 序号 + 面积 + 圆度（UI-R05）：三样是判断"这块是不是目标"的最小充分信息
                for (int i = 0; i < kept.Count; i++)
                {
                    BlobResult b = kept[i];
                    double cx = b.Cx + usedRoi.X;
                    double cy = b.Cy + usedRoi.Y;
                    string label = string.Format(CultureInfo.InvariantCulture,
                        "#{0} {1:0}px² · {2:0.00}", i + 1, b.Area, Circularity(b));
                    p.Shapes.Add(FlowShapes.BlobRect(cx, cy, b.Width, b.Height, label));
                }

                // 主判据：数量 + 最大面积（及该目标的圆度）。必须在任何 return false 之前填好，
                // 否则"期望个数不符"时结果区只剩一句文案、看不到实际数了几个。
                p.Metrics.TargetCount = kept.Count;
                if (kept.Count > 0)
                {
                    p.Metrics.MaxArea = kept[0].Area;
                    p.Metrics.MaxCircularity = Circularity(kept[0]);
                }

                // ROI 轮廓是「输入几何」而不是「检测目标」：走 AuxShapes，
                // 否则结果区会多出 4 条"边线"行，与"12 个 Blob"永久对不上（UI-R05）。
                if (!usedRoi.IsEmpty)
                    p.AuxShapes.AddRange(FlowShapes.RoiOutline(usedRoi));

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
                if (cropped != null) cropped.Dispose();
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

            // 预览帧：连续运行（CaptureFrames=false）时 StepNext 不会留帧，
            // 这里显式克隆当前帧，保证流程预览区能显示该检测节点的结果图。
            if (s.CaptureFrames && s.Current != null)
            {
                if (p.Frame != null) p.Frame.Dispose();
                try { p.Frame = (Bitmap)s.Current.Clone(); }
                catch (Exception) { p.Frame = null; }
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
            p.AuxShapes.AddRange(FlowShapes.CaliperWindows(r.Points));
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

            // 预览帧：连续运行（CaptureFrames=false）时 StepNext 不会留帧，
            // 这里显式克隆当前帧，保证流程预览区能显示该检测节点的结果图。
            if (s.CaptureFrames && s.Current != null)
            {
                if (p.Frame != null) p.Frame.Dispose();
                try { p.Frame = (Bitmap)s.Current.Clone(); }
                catch (Exception) { p.Frame = null; }
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

            p.AuxShapes.AddRange(FlowShapes.CaliperWindows(r.Points));
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

            // 预览帧：连续运行（CaptureFrames=false）时 StepNext 不会留帧，
            // 这里显式克隆当前帧，保证流程预览区能显示该检测节点的结果图。
            if (s.CaptureFrames && s.Current != null)
            {
                if (p.Frame != null) p.Frame.Dispose();
                try { p.Frame = (Bitmap)s.Current.Clone(); }
                catch (Exception) { p.Frame = null; }
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

            // 预览帧：连续运行（CaptureFrames=false）时 StepNext 不会留帧，
            // 这里显式克隆当前帧，保证流程预览区能显示该检测节点的结果图。
            if (s.CaptureFrames && s.Current != null)
            {
                if (p.Frame != null) p.Frame.Dispose();
                try { p.Frame = (Bitmap)s.Current.Clone(); }
                catch (Exception) { p.Frame = null; }
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

            // 预览帧：连续运行（CaptureFrames=false）时 StepNext 不会留帧，
            // 这里显式克隆当前帧，保证流程预览区能显示该检测节点的结果图。
            if (s.CaptureFrames && s.Current != null)
            {
                if (p.Frame != null) p.Frame.Dispose();
                try { p.Frame = (Bitmap)s.Current.Clone(); }
                catch (Exception) { p.Frame = null; }
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

        // ---------------------- 图像滤波 / 颜色提取的参数解析 ----------------------

        private static bool Eq(string a, string b)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>枚举参数（存的是 <c>NodeParamOption.Value</c>）→ 原生滤波类型编号。</summary>
        private static int ParseFilterType(string name)
        {
            if (Eq(name, "None")) return FilterType.None;
            if (Eq(name, "Box")) return FilterType.Box;
            if (Eq(name, "Median")) return FilterType.Median;
            if (Eq(name, "Bilateral")) return FilterType.Bilateral;
            if (Eq(name, "Usm")) return FilterType.Usm;
            if (Eq(name, "Custom")) return FilterType.Custom;
            return FilterType.Gauss;
        }

        /// <summary>枚举参数 → 原生形态学算子编号（va_filter 的 VA_MORPH_* 口径）。</summary>
        private static int ParseFilterMorphOp(string name)
        {
            if (Eq(name, "Dilate")) return FilterMorphOp.Dilate;
            if (Eq(name, "Erode")) return FilterMorphOp.Erode;
            if (Eq(name, "Open")) return FilterMorphOp.Open;
            if (Eq(name, "Close")) return FilterMorphOp.Close;
            if (Eq(name, "Gradient")) return FilterMorphOp.Gradient;
            if (Eq(name, "TopHat")) return FilterMorphOp.TopHat;
            if (Eq(name, "BlackHat")) return FilterMorphOp.BlackHat;
            return FilterMorphOp.None;
        }

        private static int ParseMorphShape(string name)
        {
            if (Eq(name, "Ellipse")) return FilterMorphShape.Ellipse;
            if (Eq(name, "Cross")) return FilterMorphShape.Cross;
            return FilterMorphShape.Rect;
        }

        private static int ParseColorMorphOp(string name)
        {
            if (Eq(name, "Open")) return ColorMorphOp.Open;
            if (Eq(name, "Close")) return ColorMorphOp.Close;
            return ColorMorphOp.None;
        }

        /// <summary>
        /// 用户按<strong>原始图坐标</strong>填写的 RoiX/Y/W/H → 工作帧坐标矩形，并夹到图像范围内。
        /// 宽高为 0 或完全落在图外时返回 <see cref="Rectangle.Empty"/>（= 整帧处理）。
        /// 换算顺序与 Blob 节点一致：减裁剪偏移 → 位置修正变换 → 夹取。
        /// </summary>
        private static Rectangle ResolveRoi(FlowRunSession s, FlowNode node, int width, int height)
        {
            int roiX = node.GetInt("RoiX", 0);
            int roiY = node.GetInt("RoiY", 0);
            int roiW = node.GetInt("RoiW", 0);
            int roiH = node.GetInt("RoiH", 0);
            if (roiW <= 0 || roiH <= 0) return Rectangle.Empty;

            int wx = (int)Math.Round(roiX - s.OffsetX);
            int wy = (int)Math.Round(roiY - s.OffsetY);
            Rectangle user = s.Transform.ApplyToRect(new Rectangle(wx, wy, roiW, roiH));

            int rx = Math.Max(0, Math.Min(user.X, width - 1));
            int ry = Math.Max(0, Math.Min(user.Y, height - 1));
            int rw = Math.Max(0, Math.Min(user.Width, width - rx));
            int rh = Math.Max(0, Math.Min(user.Height, height - ry));
            if (rw <= 0 || rh <= 0) return Rectangle.Empty;

            return new Rectangle(rx, ry, rw, rh);
        }

        /// <summary>
        /// 按滤波输出通道复制一份整帧底图（供 ROI 结果贴回）：彩色链路出 24bpp，
        /// 灰度链路出 8bpp 灰度。返回新位图，由调用方释放。
        /// </summary>
        private static Bitmap CloneWorkingFrame(Bitmap src, bool keepColor)
        {
            if (!keepColor) return ToGrayBitmap(src);
            if (src.PixelFormat == PixelFormat.Format24bppRgb) return (Bitmap)src.Clone();

            // 32bpp / 索引图等：先经 GDI+ 归一化到 24bpp，保证与滤波输出的像素格式一致
            var work = new Bitmap(src.Width, src.Height, PixelFormat.Format24bppRgb);
            using (Graphics g = Graphics.FromImage(work))
                g.DrawImage(src, 0, 0, src.Width, src.Height);
            return work;
        }

        /// <summary>
        /// 把 <paramref name="patch"/> 按原尺寸逐行拷进 <paramref name="dst"/> 的 (x, y) 处。
        /// 两者像素格式必须一致（8bpp 或 24bpp）—— 之所以不用 <c>Graphics.DrawImage</c>，
        /// 是因为 GDI+ <strong>不支持在 8bpp 索引位图上创建 Graphics</strong>，灰度链路会直接抛异常。
        /// </summary>
        private static void BlitPatch(Bitmap dst, Bitmap patch, int x, int y)
        {
            int w = patch.Width;
            int h = patch.Height;
            int bpp = Image.GetPixelFormatSize(patch.PixelFormat) / 8;
            int rowBytes = w * bpp;

            var srcRect = new Rectangle(0, 0, w, h);
            BitmapData sd = patch.LockBits(srcRect, ImageLockMode.ReadOnly, patch.PixelFormat);
            BitmapData dd = dst.LockBits(new Rectangle(x, y, w, h), ImageLockMode.WriteOnly, dst.PixelFormat);
            try
            {
                byte[] row = new byte[rowBytes];
                for (int r = 0; r < h; r++)
                {
                    Marshal.Copy(IntPtr.Add(sd.Scan0, r * sd.Stride), row, 0, rowBytes);
                    Marshal.Copy(row, 0, IntPtr.Add(dd.Scan0, r * dd.Stride), rowBytes);
                }
            }
            finally
            {
                dst.UnlockBits(dd);
                patch.UnlockBits(sd);
            }
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
        /// 一个匹配节点的训练缓存：<strong>模板图 + 训练签名 + 元数据</strong>。
        /// 模板图来自落盘的模板资产（<see cref="TemplateStore"/>），<strong>不再来自当前帧</strong> ——
        /// 这才叫"训练一次、匹配多次"：模板来自"当初示教的那一块像素"，而不是"这一帧长什么样"。
        /// </summary>
        private sealed class TrainedModel
        {
            /// <summary>训练签名：<c>"T@&lt;模板戳&gt;|&lt;训练参数签名&gt;"</c>（不含 ApplyRoi 的映射结果）。</summary>
            public string Signature = "";

            /// <summary>模板基准图（来自模板资产；释放时机见 <see cref="ClearTrainedModels"/>）。</summary>
            public Bitmap Template = null;

            /// <summary>模板元数据（状态卡片与日志用；可能为 null）。</summary>
            public TemplateMeta Meta = null;
        }

        /// <summary>
        /// 组装训练签名：<c>"T@" + 模板戳 + "|" + 训练参数签名</c>。
        /// <para>
        /// 模板戳是 <c>json + png</c> 的 <c>mtime + length</c>，所以"删除模板 / 重做模板 /
        /// 外部拷贝覆盖"三条路径都会让戳变化 → 签名变化 → 下一帧自动重训并重新加载。
        /// <strong>不引入任何跨对象通知机制</strong>。
        /// </para>
        /// </summary>
        private static string BuildModelSignature(string stamp, string trainSignature)
        {
            return "T@" + (string.IsNullOrEmpty(stamp) ? "0" : stamp) + "|"
                   + (string.IsNullOrEmpty(trainSignature) ? "-" : trainSignature);
        }

        /// <summary>形状匹配的训练参数签名：<c>"C=30|F=1500|D=2"</c>（只含训练参数，不含 ROI）。</summary>
        private static string ShapeTrainSignature(ShapeTrainParam train)
        {
            if (train == null) return "-";
            return string.Format(CultureInfo.InvariantCulture,
                "C={0:0.###}|F={1}|D={2:0.###}",
                train.Contrast, train.MaxFeatures, train.MinFeatureDist);
        }

        /// <summary>
        /// 读节点配置里的 ROI（<strong>用户坐标，原样</strong>，不做位置修正映射）。
        /// <para>
        /// 与模板资产比对"ROI 是否被改过"必须用配置值：<see cref="ApplyRoi"/> 会把
        /// <c>Offset</c> 与位置修正变换叠进去，那是"这一帧该看哪里"，不是"模板当初框的是哪一块"。
        /// 混淆两者就会出现"工件移动后模板被判失效"的误报。
        /// </para>
        /// </summary>
        private static Rectangle ConfiguredRoi(FlowNode node,
                                               string kx, string ky, string kw, string kh)
        {
            if (node == null) return Rectangle.Empty;

            int x = node.GetInt(kx, 0);
            int y = node.GetInt(ky, 0);
            int w = node.GetInt(kw, 0);
            int h = node.GetInt(kh, 0);
            return new Rectangle(x, y, w, h);
        }

        /// <summary>
        /// 模板不可用时给用户看的一句话（三类原因文案不同 —— PRD 6.3）。
        /// 「未配置模板，请先创建模板」是 DEF-03 的规定文案，改动前先看需求。
        /// </summary>
        private static string DescribeTemplateFailure(TemplateLoadError error, string message)
        {
            switch (error)
            {
                case TemplateLoadError.FileMissing:
                    return "未配置模板，请先创建模板";

                case TemplateLoadError.MismatchWithNode:
                    return "模板与当前 ROI 不匹配，请重新创建模板";

                case TemplateLoadError.CorruptedOrVersionMismatch:
                    return "模板文件损坏或版本不兼容，请重新创建模板（" + Tail(message) + "）";

                default:
                    return string.IsNullOrEmpty(message) ? "模板不可用，请重新创建模板" : message;
            }
        }

        /// <summary>重训耗时后缀：只在真的重训过时出现（否则每帧都写"重训 0ms"反而干扰判读）。</summary>
        private static string RetrainSuffix(int retrainMs)
        {
            return retrainMs > 0 ? "（重训 " + retrainMs + " ms）" : "";
        }

        /// <summary>
        /// 重训后缀：<strong>只要真的重训过就一定有标记</strong>。
        /// <para>
        /// 【为什么不能只看 <paramref name="retrainMs"/> &gt; 0】Release 下训练常在同一毫秒内完成，
        /// 于是"重训过但耗时 0"和"压根没重训"在摘要上是同一句话 —— 而这两件事的排查价值完全相反：
        /// 后者说明模型复用正常，前者可能意味着每帧都在重训（掉帧的真凶）。
        /// </para>
        /// </summary>
        private static string RetrainSuffix(bool retrained, int retrainMs)
        {
            if (!retrained) return "";
            return retrainMs > 0 ? "（重训 " + retrainMs + " ms）" : "（重训 <1 ms）";
        }

        /// <summary>
        /// 裁出模板 ROI 的副本；ROI 无效（宽高为 0 或完全在图外）时退化为整帧副本。
        /// 运行期已不再用它训练（模板来自资产），保留给"从当前帧取一块"的旧调用点与诊断。
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
