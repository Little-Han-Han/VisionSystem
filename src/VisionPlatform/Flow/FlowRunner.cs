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
using VisionPlatform.Calibration;
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

        /// <summary>
        /// 辅助几何（搜索区域轮廓 / 卡尺窗口 / OCR 区域…，全链汇总）。
        /// <para>
        /// 【为什么必须单独汇总】它是"输入条件"不是"检测结果"，所以不能进结果明细；
        /// 但主界面必须看得到 —— 否则现场无从判断"算法到底在哪块区域里干活"。
        /// 之前 <c>PublishChainEnd</c> 只汇总了 <see cref="Shapes"/>，注释却写着"已复制 AuxShapes"，
        /// 实际 <c>snap.AuxShapes</c> 一直是空的，于是界面上一个搜索框都看不到，
        /// "缺陷跑到搜索区域外"就变成了无法自证的错觉。
        /// </para>
        /// </summary>
        public List<IGeometryShape> AuxShapes { get; set; }

        /// <summary>逐节点进度（供画布状态标注与预览区回看）。</summary>
        public List<FlowNodeProgress> Progress { get; set; }

        /// <summary>本帧是否写入了有效的位置修正。</summary>
        public bool TransformValid { get; set; }

        /// <summary>位置修正的平移量 X（帧坐标，像素）。</summary>
        public double TransformDx { get; set; }

        /// <summary>位置修正的平移量 Y（帧坐标，像素）。</summary>
        public double TransformDy { get; set; }

        /// <summary>位置修正的旋转量（度，顺时针为正）。</summary>
        public double TransformDAngle { get; set; }

        /// <summary>修正是否已经烘进像素（整图对齐模式）。此时 ROI 用原坐标即可。</summary>
        public bool TransformWarped { get; set; }

        /// <summary>
        /// 本帧的位姿变换对象（含基准点，可做正/逆变换）。
        /// <para>界面画 ROI 时要用它把"画面坐标"换回"示教坐标"（<c>ApplyInverseToRect</c>），
        /// 否则运行期会再变换一次，正好差一个位移。</para>
        /// </summary>
        public FlowRunTransform Transform { get; set; }

        /// <summary>总耗时（毫秒）。</summary>
        public int ElapsedMs { get; set; }

        /// <summary>
        /// 本次运行是否"示教了位置修正基准"（自动基准首次运行）。
        /// <para>宿主据此把基准写盘 —— 否则下次运行又会重新示教，dX/dY 永远是 0。</para>
        /// </summary>
        public bool BaselineTaught { get; set; }

        /// <summary>示教了基准的节点 Id（空 = 没有）。宿主可据此刷新参数面板。</summary>
        public string BaselineNodeId { get; set; }

        /// <summary>本轮各节点的输出台账（脚本编辑器用它展示"上游能读到什么"）。</summary>
        public List<NodeOutput> NodeOutputs { get; set; }

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

        /// <summary>最近一次运行的节点输出台账（脚本编辑器读它展示上游数据）。</summary>
        public List<NodeOutput> LastOutputs { get; private set; }


        /// <summary>本轮是否示教了位置修正基准（宿主据此落盘，避免下次又示教成 0 偏差）。</summary>
        private bool _baselineTaught;

        /// <summary>本轮刚示教过基准的节点 Id（空 = 没有）。宿主据此刷新参数面板里的基准值。</summary>
        private string _baselineNodeId = "";
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

        // 单文件字符库（.ocrtpl）的缓存：与目录缓存分开记，避免两套口径互相把对方的指纹当"没变"。
        private string _ocrLibPath;
        private string _ocrLibStamp;
        private List<OcrCharSample> _ocrLibSamples;
        private OcrTemplateLoadResult _ocrLibResult;

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
        /// 建立一次运行会话：校验拓扑（DAG）并准备上下文。
        /// 失败时返回 <see cref="FlowRunSession.Aborted"/> = true 的会话，原因见
        /// <see cref="FlowRunSession.ErrorMessage"/>。
        /// </summary>
        /// <param name="flow">待执行流程。</param>
        /// <param name="camera">取图相机；图像源为「本地图片」时允许为 null。</param>
        /// <param name="captureFrames">是否为每个节点保存输出帧副本（单步预览需要）。</param>
        /// <summary>
        /// 多流程地址偏移 = 流程序号 × <c>MbFlowStride</c>。
        /// <para>步长 0 / 序号未知（-1）/ 序号 0 时一律返回 0 —— 单流程配置完全不受影响。</para>
        /// </summary>
        private static int FlowAddressShift(FlowRunSession s, FlowNode node)
        {
            if (s == null || node == null) return 0;

            int stride = node.GetInt("MbFlowStride", 0);
            if (stride <= 0) return 0;
            if (s.FlowIndex <= 0) return 0;

            return stride * s.FlowIndex;
        }

        /// <summary>
        /// 流程在「流程管理」里的序号（0 起）。多流程结果分区用它：
        /// 与实际触发位用的是同一张顺序表，改顺序即改分区（见 FlowForm 的上移/下移）。
        /// </summary>
        private static int FlowIndexOf(FlowDefinition flow)
        {
            if (flow == null) return -1;

            try
            {
                FlowSettings fs = ConfigStore.Current.Flows;
                if (fs == null || fs.Flows == null) return -1;

                for (int i = 0; i < fs.Flows.Count; i++)
                {
                    FlowDefinition f = fs.Flows[i];
                    if (f == null) continue;
                    if (ReferenceEquals(f, flow)) return i;
                    if (!string.IsNullOrEmpty(flow.Id)
                        && string.Equals(f.Id, flow.Id, StringComparison.OrdinalIgnoreCase)) return i;
                }
            }
            catch (Exception) { }

            return -1;
        }

        public FlowRunSession Begin(FlowDefinition flow, ICamera camera, bool captureFrames = false,
                                    bool clearAutoRefs = true, bool captureSourceFrame = false)
        {
            // 每次「运行」开始前清空位置修正 Auto 基准缓存，避免上一轮示教的基准泄漏到
            // 本轮，造成仿射变换基准错位（图像偏移 / 修正缺失）。
            // 连续运行由调用方在循环外清空一次、循环内传 false，保证单轮连续运行的
            // 「首次示教」基准不被每帧清空（见 FlowRunController.LoopCore）。
            if (clearAutoRefs) ClearAutoReferences();
            _baselineTaught = false;
            _baselineNodeId = "";

            var s = new FlowRunSession();
            s.Flow = flow;
            s.FlowIndex = FlowIndexOf(flow);
            s.Camera = camera;
            s.CaptureFrames = captureFrames;
            s.CaptureSourceFrame = captureSourceFrame;
            s.CaptureProgressFrames = true;
            s.StopOnNg = (flow == null) || flow.StopOnNg;   // 未配置时按"遇 NG 停止"
            s.FrameTimeoutMs = (flow != null) ? flow.FrameTimeoutMs : 0;   // 0 = 不限

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

            // 整帧超时：只在这里（节点之间）判一次。
            // 【为什么不放在节点内部】节点内部有自己的 TimeoutMs，两层职责不同：
            // 这里管"多个都不慢的节点加起来超了"，那里管"单个算法算太久"。
            // 【为什么不是 0 就跳过】0 = 不限，绝大多数流程不设；不设时零开销。
            if (session.FrameTimeoutMs > 0 && session.Watch != null
                && session.Watch.ElapsedMilliseconds > session.FrameTimeoutMs)
            {
                StopChainByFrameTimeout(session);
                return null;
            }

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

            // 节点级软超时：给本线程设截止时间，算法在自己的检查点主动退出。
            // 0 / 负数 = 不限（老配置或用户显式关闭）。
            // 【只对带这个参数的节点判】图像源/通讯/脚本那几类不参与（判据在 FlowNodeTraits，
            // 与参数表同源）。否则脚本节点会被 500ms 默认值误杀 —— 用户代码跑 600ms 是正常的。
            int timeoutMs = FlowNodeTraits.HasAlgoTimeout(type) ? node.GetInt("TimeoutMs", 500) : 0;

            bool ok;
            try
            {
                VisionAlgo.SetDeadline(timeoutMs);
                ok = ExecuteNode(session, node, type, p);
            }
            catch (Exception ex)
            {
                ok = false;
                p.Summary = node.Name + "：执行异常（" + ex.Message + "）";
                // 完整异常（含原生 OpenCV 报错里的文件:行号与断言内容）写入日志，便于排查「显示不全」的报错
                Logger.Error("节点「" + node.Name + "」(" + type + ") 执行异常：" + ex);
            }
            finally
            {
                // 【必须清】deadline 是 thread_local：不清掉，下一个节点会继承上一帧的截止时间，
                // 表现为"刚报过一次超时，后面所有节点都秒超时"。
                VisionAlgo.SetDeadline(0);
            }
            sw.Stop();

            // 每个节点执行完后刷新彩色通道：节点可能换过工作帧（灰度化 / 保持彩色 / 像素级变换），
            // 统一在外部按当前帧的实际格式刷新，不依赖各节点自觉汇报，杜绝野引用。
            session.SyncColorInput();

            p.ElapsedMs = (int)sw.ElapsedMilliseconds;

            // 节点级超时的**兜底判据**：算法自己的检查点只能停在"两次 OpenCV 调用之间"，
            // 单次 cv::matchTemplate / OpenVINO 推理内部是拦不住的。所以这里再按实际耗时判一次：
            // 只要过了线，不管算法自己怎么报，都按超时判 NG —— 否则现场会看到
            // "设了 500ms，实际跑了 1.2s 还判 OK"。
            if (timeoutMs > 0 && p.ElapsedMs > timeoutMs)
            {
                ok = false;
                if (p.Status != FlowNodeStatus.Ng) p.Status = FlowNodeStatus.Ng;

                string detail = string.IsNullOrEmpty(p.Summary) ? "" : ("：" + p.Summary);
                p.Summary = "超时（" + p.ElapsedMs + "ms > " + timeoutMs + "ms）" + detail;
            }

            // 节点内部已显式设置 Skipped / Warn 时保持原状，不要覆盖成 Ok。
            // ⚠️ 这是 Warn 唯一的"让位点"：此后任何覆盖 p.Status 的代码都必须先检查
            //    是不是 Warn（Warn 只影响显示，绝不等于 NG，也不得被 Ok 抹平）。
            //     判据见 Architecture「T07 红线②」。
            if (p.Status == FlowNodeStatus.Running)
                p.Status = ok ? FlowNodeStatus.Ok : FlowNodeStatus.Ng;

            // 记下"最后一个有结论的节点"：NG 继续跑模式下的流程判定就取它的值。
            // Skipped 不算结论（被跳过 = 没有判决），Warn 算"通过但有话说"（Warn 永不等于 NG）。
            if (p.Status == FlowNodeStatus.Ok || p.Status == FlowNodeStatus.Ng
                || p.Status == FlowNodeStatus.Warn)
            {
                session.LastVerdict = p.Status;
            }

            // 遇 NG：Stop 模式累积失败并停止；Continue 模式只记进度，最终判定在 Finish 里按 LastVerdict 算。
            if (!ok && session.StopOnNg) session.Passed = false;

            // 记录本次节点产出（脚本节点与后续数据联动从这里读）
            RecordOutput(session, node, type, p);

            // 节点内（改动1）已克隆预览帧时不再重复克隆，避免重复分配与泄漏
            if (session.CaptureFrames && session.Current != null && p.Frame == null)
            {
                try { p.Frame = (Bitmap)session.Current.Clone(); }
                catch (Exception) { p.Frame = null; }
            }

            if (!string.IsNullOrEmpty(p.Summary)) session.Summaries.Add(p.Summary);
            session.Cursor++;

            // 「遇 NG 立即停止」：把剩余节点记成 Skipped 再收链。
            // 【为什么要补这些条目】不补的话，结果区只显示"跑到第 3 个就没了"，
            // 用户无法区分"策略停止"与"流程只有 3 个节点 / 程序崩了"。
            if (!ok && session.StopOnNg) StopChainByNg(session, node);

            return p;
        }

        /// <summary>
        /// 「遇 NG 立即停止」时收尾：剩余节点全部记一条 Skipped，并把游标推到末尾。
        /// </summary>
        private static void StopChainByNg(FlowRunSession session, FlowNode failedNode)
        {
            if (session == null || session.Chain == null) return;

            string reason = "前序节点「" + (failedNode != null ? failedNode.Name : "?")
                          + "」NG，按「遇 NG 立即停止」策略跳过";

            while (session.Cursor < session.Chain.Count)
            {
                FlowNode n = session.Chain[session.Cursor];
                var skip = new FlowNodeProgress();
                skip.NodeId = n != null ? n.Id : "";
                skip.NodeName = n != null ? n.Name : "";
                skip.Index = session.Cursor + 1;
                skip.Total = session.Chain.Count;
                skip.Status = FlowNodeStatus.Skipped;
                skip.Summary = reason;
                skip.ElapsedMs = 0;
                session.Progress.Add(skip);
                session.Summaries.Add(skip.Summary);
                session.Cursor++;
            }
        }

        /// <summary>
        /// 整帧超时收尾：剩余节点记 Skipped、游标推到末尾，并标记本帧被截断。
        /// </summary>
        /// <remarks>
        /// 【文案要点】必须报出<strong>实际耗时</strong>与<strong>在哪一步被截断</strong>：
        /// 现场看到"第 4 个节点没跑"时，第一反应会是"流程少配了节点"，
        /// 而真实原因是前面 3 个节点加起来已经超了。
        /// </remarks>
        private static void StopChainByFrameTimeout(FlowRunSession session)
        {
            if (session == null || session.Chain == null) return;

            session.FrameTimedOut = true;
            session.Passed = false;

            long used = session.Watch != null ? session.Watch.ElapsedMilliseconds : 0;
            string reason = "整帧超时（已用 " + used + "ms > " + session.FrameTimeoutMs
                          + "ms），剩余节点未执行";

            bool first = true;
            while (session.Cursor < session.Chain.Count)
            {
                FlowNode n = session.Chain[session.Cursor];
                var skip = new FlowNodeProgress();
                skip.NodeId = n != null ? n.Id : "";
                skip.NodeName = n != null ? n.Name : "";
                skip.Index = session.Cursor + 1;
                skip.Total = session.Chain.Count;
                skip.Status = FlowNodeStatus.Skipped;
                // 只在第一条写明细，后面统一写"整帧超时未执行" —— 否则结果区会刷一屏同样的长句
                skip.Summary = first ? reason : "整帧超时，未执行";
                skip.ElapsedMs = 0;
                session.Progress.Add(skip);
                session.Summaries.Add(skip.Summary);
                session.Cursor++;
                first = false;
            }
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
            result.AuxShapes = CollectAuxShapes(session);

            // 把本帧的位姿修正留在结果里：界面据此把"选中节点的 ROI 轮廓"画到工件实际所在的位置，
            // 否则位置修正生效后，预览上的 ROI 框还停在示教位、看起来像"没跟着工件走"。
            result.TransformValid = session.Transform.IsValid;
            result.TransformDx = session.Transform.DX;
            result.TransformDy = session.Transform.DY;
            result.TransformDAngle = session.Transform.DAngle;
            result.TransformWarped = session.Transform.Warped;
            result.Transform = session.Transform;

            if (session.Aborted && !string.IsNullOrEmpty(session.ErrorMessage))
            {
                result.Message = session.ErrorMessage;
                result.Summary = string.Join("；", session.Summaries);
                return result;
            }

            result.Success = true;

            // 流程判定：
            //   Stop 模式（默认）—— 任一节点 NG 即整条失败（Passed 在 StepNext 里累积）。
            //   Continue 模式     —— 不看"有没有人 NG 过"，只取**最后一个有结论的节点**
            //                        （对应现场："按最后一个插件或脚本的返回值判 OK/NG"）。
            //                        一个都没跑成（全 Skipped / 空链）时沿用 Passed，避免"空流程算 NG"。
            result.Passed = session.StopOnNg
                ? session.Passed
                : (session.LastVerdict == FlowNodeStatus.Pending
                    ? session.Passed
                    : session.LastVerdict != FlowNodeStatus.Ng);

            // 整帧超时是"这一轮没跑完"，跟末节点判了什么无关 —— 必须无条件压成 NG。
            // （否则 Continue 模式下末节点恰好 OK，会把一个只跑了半个流程的帧判成良品。）
            if (session.FrameTimedOut) result.Passed = false;

            result.Summary = string.Join("；", session.Summaries);
            result.BaselineTaught = _baselineTaught;
            result.BaselineNodeId = _baselineNodeId ?? "";
            result.NodeOutputs = new List<NodeOutput>(session.Outputs);
            LastOutputs = result.NodeOutputs;
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
                        Product = flow != null ? (flow.Product ?? "") : "",

                        // 3D：本周期产出过点云时一并带出（引用，无释放手续）
                        Cloud = session.CurrentCloud
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

        /// <summary>汇总全链的辅助几何（搜索区域轮廓等）。与 <see cref="CollectShapes"/> 同构。</summary>
        private static List<IGeometryShape> CollectAuxShapes(FlowRunSession session)
        {
            var all = new List<IGeometryShape>();
            foreach (FlowNodeProgress p in session.Progress)
            {
                if (p == null || p.AuxShapes == null) continue;
                all.AddRange(p.AuxShapes);
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
                case FlowNodeType.Script:        return RunScript(s, node, p);
                case FlowNodeType.DlSeg:         return RunDl(s, node, p, NativeDl.TaskSeg);
                case FlowNodeType.DlPose:        return RunDl(s, node, p, NativeDl.TaskPose);
                case FlowNodeType.DlDepth:       return RunDl(s, node, p, NativeDl.TaskDepth);
                case FlowNodeType.OcrDl:         return RunOcrDl(s, node, p);
                case FlowNodeType.CalibTransform: return RunCalibTransform(s, node, p);
                case FlowNodeType.DistortionCorrect: return RunDistortionCorrect(s, node, p);
                case FlowNodeType.LineIntersect: return RunLineIntersect(s, node, p);
                case FlowNodeType.EdgePair:      return RunEdgePair(s, node, p);
                case FlowNodeType.PointPointMeasure: return RunPointPointMeasure(s, node, p);
                case FlowNodeType.PointLineMeasure:  return RunPointLineMeasure(s, node, p);
                case FlowNodeType.LineLineMeasure:   return RunLineLineMeasure(s, node, p);
                case FlowNodeType.BarcodeRead:       return RunBarcode(s, node, p);
                case FlowNodeType.PointCloudSource:  return RunPointCloudSource(s, node, p);
                case FlowNodeType.AnomalyDetect:     return RunAnomaly(s, node, p);
                default:
                    p.Status = FlowNodeStatus.Skipped;
                    p.Summary = node.Name + "：暂不支持的节点类型";
                    return true;
            }
        }

        /// <summary>
        /// 把一个节点的执行结果收进 <see cref="FlowRunSession.Outputs"/>，
        /// 供脚本节点 / 结果输出 / 后续数据联动读取。
        /// </summary>
        private static void RecordOutput(FlowRunSession s, FlowNode node, FlowNodeType type, FlowNodeProgress p)
        {
            if (s == null || node == null || p == null) return;

            var o = new NodeOutput();
            o.Id = node.Id ?? "";
            o.Name = string.IsNullOrEmpty(node.Name) ? o.Id : node.Name;
            o.Type = type.ToString();
            o.Status = p.Status;
            o.Summary = p.Summary ?? "";
            o.ElapsedMs = p.ElapsedMs;
            o.ShapeCount = p.Shapes != null ? p.Shapes.Count : 0;

            FlowNodeMetrics m = p.Metrics;
            if (m != null)
            {
                o.TargetCount = m.TargetCount;
                o.TopScore = m.TopScore;
                o.MaxArea = m.MaxArea;
                o.MaxCircularity = m.MaxCircularity;
                if (!double.IsNaN(m.TopCx)) o.Cx = m.TopCx;
                if (!double.IsNaN(m.TopCy)) o.Cy = m.TopCy;
                if (!double.IsNaN(m.AngleDeg)) o.Angle = m.AngleDeg;
                if (!double.IsNaN(m.Value)) o.Value = m.Value;
            }

            // 位姿：优先节点自己的位姿表（匹配/卡尺会写），否则用 Metrics 里的坐标
            double[] pose;
            if (s.TryGetNodePose(node.Id, out pose) && pose != null && pose.Length >= 4)
            {
                o.HasPose = true;
                o.Cx = pose[0];
                o.Cy = pose[1];
                o.Angle = pose[2];
                o.Scale = pose[3];
            }

            // 文本：OCR / 收发数据把文本塞在 Metrics.Text 或摘要里
            if (m != null && !string.IsNullOrEmpty(m.Text)) o.Text = m.Text;

            // 类型化输出：判定 / 数值 / 角度 / 坐标 / 文本（结果显示与脚本共用这一份）
            o.Values = NodeOutputSchema.Build(type, m, o.Ok);

            // 节点自己上报的（更细：长度 / 角度 / 有效点数 / 剔除数 / 逐目标几何）优先并入
            if (p.Outputs != null)
            {
                foreach (PortValue v in p.Outputs)
                {
                    if (v == null || string.IsNullOrEmpty(v.Key)) continue;
                    o.Values.RemoveAll(x => x != null && string.Equals(x.Key, v.Key, StringComparison.OrdinalIgnoreCase));
                    o.Values.Add(v);
                }
            }
            if (p.Regions != null) o.Regions = new List<RegionInfo>(p.Regions);

            // 统一坐标输出：凡是填了 TopCx/TopCy 的节点（模板匹配/Blob/圆卡尺/圆拟合/直线拟合…）
            // 一律额外给出 X / Y / R 三个键，标定转换节点按 Key 取，不必逐个节点写适配
            if (!double.IsNaN(o.Cx) && !double.IsNaN(o.Cy))
            {
                double r = double.IsNaN(o.Angle) ? 0 : o.Angle;
                o.Values.RemoveAll(x => x != null &&
                    (string.Equals(x.Key, "X", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(x.Key, "Y", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(x.Key, "R", StringComparison.OrdinalIgnoreCase)));
                o.Values.Add(new PortValue("X", "X", FlowPortKind.Number, o.Cx));
                o.Values.Add(new PortValue("Y", "Y", FlowPortKind.Number, o.Cy));
                o.Values.Add(new PortValue("R", "R", FlowPortKind.Number, r));
            }

            s.Outputs.Add(o);
        }

        // ---------------------- 脚本节点 ----------------------

        /// <summary>
        /// 脚本节点：把<strong>所有已执行节点</strong>的输出交给用户脚本，脚本返回字符串作结果摘要。
        /// <para>
        /// 脚本失败（编译错误 / 运行异常）→ 节点 NG，摘要里带前几条错误，方便现场直接改。
        /// </para>
        /// </summary>
        private bool RunScript(FlowRunSession s, FlowNode node, FlowNodeProgress p)
        {
            string script = node.GetString("Source", "");

            // 空脚本 = "还没写"：按未实现处理（返回 false → 本节点 NG），不当编译错误报
            if (string.IsNullOrWhiteSpace(script))
            {
                p.Summary = "脚本：未编写（在大括号里写代码后再运行）";
                return false;
            }

            var api = new ScriptApi(
                new List<NodeOutput>(s.Outputs),
                s.NamedValues,
                s.Current != null ? s.Current.Width : 0,
                s.Current != null ? s.Current.Height : 0);

            string error;
            bool okScript = ScriptEngine.Run(script, api, out error);

            if (!string.IsNullOrEmpty(error))
            {
                // 编译/运行错误原样带出：里面有「第 N 行：xxx」，Tail 截断会把行号吃掉
                p.Summary = "脚本：" + error;
                return false;
            }

            string text = api.Text;
            if (string.IsNullOrEmpty(text)) text = "(无输出)";

            p.Summary = "脚本：" + OneLineText(text);

            // 把"读到几个上游节点"写进摘要：读不到数据时，用户第一眼就能判断是脚本写错
            // 还是执行顺序不对（脚本没排在链尾）。
            p.Summary += string.Format(CultureInfo.InvariantCulture, "（上游 {0} 个节点）", s.Outputs.Count);
            if (api.SetNames.Count > 0)
                // 【带值】只报变量名等于没报：现场要的就是 PosX=512.3 这类具体结果。
                p.Summary += "（输出：" + DescribeNamedValues(api) + "）";

            // 声明的输出变量必须都写过，否则下游拿到的是空值 —— 在摘要里点名
            string missing = MissingOutputs(node.GetString("OutDefs", ""), api.SetNames);
            if (missing.Length > 0)
                p.Summary += "（缺少输出：" + missing + "）";
            if (Math.Abs(api.Number) > 1e-12)
                p.Summary += string.Format(CultureInfo.InvariantCulture, "（数值 {0:0.####}）", api.Number);

            // 返回值与 api.Ok 任一为 false → 本节点判 NG
            if (!okScript || !api.Ok)
            {
                p.Summary += "（脚本判定不通过）";
                return false;
            }

            return true;
        }

        /// <summary>
        /// 把脚本写出的命名输出拼成 <c>PosX=512.3、PosY=349.8</c>。
        /// <para>数值统一保留 4 位小数（坐标 / 角度够用，且不会因为 512.30000000001 这种浮点尾巴刷屏）；
        /// 非数值原样输出；空值写 <c>--</c>，一眼能区分"没算出来"和"算出来是 0"。</para>
        /// </summary>
        private static string DescribeNamedValues(ScriptApi api)
        {
            var parts = new List<string>();
            foreach (string name in api.SetNames)
            {
                object v = api.GetValue(name);
                parts.Add(name + "=" + DescribeValue(v));
            }
            return string.Join("、", parts.ToArray());
        }

        private static string DescribeValue(object v)
        {
            if (v == null) return "--";
            if (v is bool) return ((bool)v) ? "true" : "false";
            if (v is string) return (string)v;

            if (v is double || v is float || v is decimal || v is int || v is long || v is short)
            {
                double d = Convert.ToDouble(v, CultureInfo.InvariantCulture);
                return d.ToString("0.####", CultureInfo.InvariantCulture);
            }

            return v.ToString();
        }

        /// <summary>OutDefs 里声明了、但脚本没有 api.Set 的输出名（逗号分隔）。</summary>
        private static string MissingOutputs(string defs, List<string> setNames)
        {
            if (string.IsNullOrWhiteSpace(defs) || setNames == null) return "";

            var missing = new List<string>();
            foreach (string part in defs.Split(new[] { ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string name = part.Trim();
                int i = name.IndexOf(':');
                if (i > 0) name = name.Substring(0, i).Trim();
                if (name.Length == 0) continue;

                bool found = false;
                foreach (string s2 in setNames)
                    if (string.Equals(s2, name, StringComparison.Ordinal)) { found = true; break; }

                if (!found) missing.Add(name);
            }
            return string.Join("、", missing.ToArray());
        }

        private static string OneLineText(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            string v = text.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ').Trim();
            return v.Length > 200 ? v.Substring(0, 200) + "…" : v;
        }

        // ---------------------- 深度学习（OpenVINO：分割 / 姿态 / 深度）----------------------

        /// <summary>
        /// 深度学习节点统一入口。模型编译在 <see cref="Native.VisionAlgo"/> 里按
        /// "任务|路径|设备"缓存，连续运行时只编译一次。
        /// </summary>
        private bool RunDl(FlowRunSession s, FlowNode node, FlowNodeProgress p, int task)
        {
            if (s.Current == null)
            {
                p.Status = FlowNodeStatus.Skipped;
                p.Summary = "深度学习：没有输入图像（请接在图像源之后）";
                return true;
            }

            string modelPath = node.GetString("ModelPath", "");
            if (string.IsNullOrWhiteSpace(modelPath))
                modelPath = DefaultDlModel(s, node, task);
            if (string.IsNullOrEmpty(modelPath))
            {
                p.Summary = "深度学习：未找到模型（请在节点里指定 .onnx，或放到 models"
                          + System.IO.Path.DirectorySeparatorChar + DlFolderOf(task) + " 目录下）";
                return false;
            }

            string device = node.GetString("Device", "CPU");
            if (string.Equals(node.GetString("Backend", "OpenVINO"), "CUDA", StringComparison.OrdinalIgnoreCase))
                device = "CUDA";

            // 首次加载要编译模型（本机实测 3~5 秒），不说明会被当成"卡死"
            var loadWatch = Stopwatch.StartNew();

            // 彩色输入优先（与目标识别一致）
            Bitmap input = s.ColorInput != null ? s.ColorInput : s.Current;

            if (task == NativeDl.TaskDepth)
            {
                float mn, mx;
                var dr = _algo.DlDepth(input, modelPath, device, out mn, out mx);
                // 不能用 Tail：它会把"模型文件不存在："这类前缀截掉，只剩一个路径
                if (!dr.Success) { p.Summary = "深度估计：" + dr.Message; return false; }

                float[] map = dr.Value;
                double sum = 0;
                for (int i = 0; i < map.Length; i++) sum += map[i];
                double mean = map.Length > 0 ? sum / map.Length : 0;

                p.Metrics.TargetCount = 1;
                p.Outputs.Add(new PortValue("Min", "最小值", FlowPortKind.Number, (double)mn));
                p.Outputs.Add(new PortValue("Max", "最大值", FlowPortKind.Number, (double)mx));
                p.Outputs.Add(new PortValue("Mean", "均值", FlowPortKind.Number, mean));
                // mean 是**归一化后**（0~1）的均值，mn/mx 是**模型原始输出**；
                // 混在一句话里会让人以为"均值怎么不在范围里"，这里分开写。
                // 深度伪彩叠加层：没有它只能看到两行数字，看不到"哪里近哪里远"
                p.Overlay = NodeOverlayFactory.DepthHeat(map, input.Width, input.Height,
                                                         input.Width, input.Height);
                EnsureNodeFrame(s, p);
                DumpOverlay(p.Overlay, node.Name, task);

                p.Summary = string.Format(CultureInfo.InvariantCulture,
                    "深度估计：原始范围 {0:F3}~{1:F3} · 归一化均值 {2:F3}", mn, mx, mean);
                p.Summary += LoadHint(loadWatch) + DescribeHint(task, loadWatch.ElapsedMilliseconds);
                return true;
            }

            float score = (float)node.GetDouble("ScoreThresh", 0.25);
            float iou = (float)node.GetDouble("IouThresh", 0.45);
            int maxCount = node.GetInt("MaxCount", 100);

            var r = _algo.DlDetect(task, input, score, iou, maxCount, modelPath, device);
            if (!r.Success)
            {
                p.Summary = (task == NativeDl.TaskSeg ? "语义分割：" : "姿态估计：") + r.Message;
                return false;
            }

            VisionAlgo.DlResult dl = r.Value ?? new VisionAlgo.DlResult();
            List<NativeDl.DlTarget> targets = dl.Targets ?? new List<NativeDl.DlTarget>();

            // 分割：把逐像素标签图上色成叠加层（这才是"像素级分割效果"，
            // 光有矩形框看不出边界在哪）
            if (task == NativeDl.TaskSeg && dl.LabelMap != null && dl.MapW > 0 && dl.MapH > 0)
            {
                p.Overlay = NodeOverlayFactory.SegMask(dl.LabelMap, dl.MapW, dl.MapH,
                                                       input.Width, input.Height, targets.Count);
                EnsureNodeFrame(s, p);      // 该节点自己的帧（不是 s.Current 的引用）
                DumpOverlay(p.Overlay, node.Name, task);
            }
            p.Metrics.TargetCount = targets.Count;

            double best = 0;
            foreach (NativeDl.DlTarget t in targets) if (t.Score > best) best = t.Score;
            if (targets.Count > 0) p.Metrics.TopScore = best;

            for (int i = 0; i < targets.Count; i++)
            {
                NativeDl.DlTarget t = targets[i];
                double cx = t.X + t.Width / 2;
                double cy = t.Y + t.Height / 2;

                if (task == NativeDl.TaskSeg)
                {
                    p.Shapes.Add(FlowShapes.BlobRect(cx, cy, t.Width, t.Height,
                        string.Format(CultureInfo.InvariantCulture,
                            "#{0} {1:F2} · {2}px²", i + 1, t.Score, t.MaskArea)));

                    p.Regions.Add(new RegionInfo
                    {
                        Index = i + 1,
                        Cx = cx, Cy = cy,
                        Width = t.Width, Height = t.Height,
                        Area = t.MaskArea
                    });
                }
                else
                {
                    p.Shapes.Add(FlowShapes.BlobRect(cx, cy, t.Width, t.Height,
                        string.Format(CultureInfo.InvariantCulture, "#{0} {1:F2}", i + 1, t.Score)));

                    // 关键点：小方块；若关键点数正好 17（COCO 人体），再按骨架连接画线
                    bool coco = NodeOverlayFactory.IsCoco17(t.KeyPoints.Count);
                    Color skelColor = NodeOverlayFactory.ColorOf(i);

                    if (coco)
                    {
                        for (int e = 0; e < NodeOverlayFactory.CocoSkeleton.GetLength(0); e++)
                        {
                            int a1 = NodeOverlayFactory.CocoSkeleton[e, 0];
                            int b1 = NodeOverlayFactory.CocoSkeleton[e, 1];
                            if (a1 >= t.KeyPoints.Count || b1 >= t.KeyPoints.Count) continue;
                            if (t.KeyPointScores.Count > a1 && t.KeyPointScores[a1] < 0.3f) continue;
                            if (t.KeyPointScores.Count > b1 && t.KeyPointScores[b1] < 0.3f) continue;

                            p.Shapes.Add(FlowShapes.Line(t.KeyPoints[a1].X, t.KeyPoints[a1].Y,
                                                         t.KeyPoints[b1].X, t.KeyPoints[b1].Y,
                                                         skelColor, 2.5f, false));
                        }
                    }

                    for (int k = 0; k < t.KeyPoints.Count; k++)
                    {
                        if (t.KeyPointScores.Count > k && t.KeyPointScores[k] < 0.3f) continue;
                        p.Shapes.Add(FlowShapes.BlobRect(t.KeyPoints[k].X, t.KeyPoints[k].Y, 5, 5, null));
                    }
                }
            }

            EnsureNodeFrame(s, p);          // 姿态没有位图叠加，但点开也该看到当时的画面

            int kps = task == NativeDl.TaskPose ? _algo.DlPoseKeypoints() : 0;
            p.Outputs.Add(new PortValue("Count", "目标数", FlowPortKind.Number, (double)targets.Count));
            p.Outputs.Add(new PortValue("Score", "最高分", FlowPortKind.Number, best));
            if (task == NativeDl.TaskPose && kps > 0)
                p.Outputs.Add(new PortValue("KeyPoints", "关键点/目标", FlowPortKind.Number, (double)kps));

            p.Summary = string.Format(CultureInfo.InvariantCulture,
                "{0}：{1} 个目标 · 最高分 {2:F3}",
                task == NativeDl.TaskSeg ? "语义分割" : "姿态估计", targets.Count, best)
                + (task == NativeDl.TaskPose && kps > 0 ? "（" + kps + " 关键点）" : "")
                + LoadHint(loadWatch)
                + DescribeHint(task, loadWatch.ElapsedMilliseconds);
            return targets.Count > 0 || node.GetInt("MinCount", 0) <= 0;
        }

        // ---------------------- 无监督缺陷检测（PaDiM / EfficientAD）---------------------

        /// <summary>
        /// 异常检测：整块检测区域送进模型，输出异常热图 + 缺陷连通域统计。
        /// <para>
        /// 【与目标识别的区别】那边是"找已知类别的东西"，这边是"说不清哪里不对，只知道和正常不一样"：
        /// 结论不是"有几个目标"，而是"最大偏离多少 + 偏了多少面积"。
        /// 因此判定同时看 <c>max_score &gt; 1</c>（相对阈值）与可选的面积占比上限。
        /// </para>
        /// </summary>
        private bool RunAnomaly(FlowRunSession s, FlowNode node, FlowNodeProgress p)
        {
            if (s.Current == null)
            {
                p.Status = FlowNodeStatus.Skipped;
                p.Summary = "异常检测：没有输入图像（请接在图像源之后）";
                return true;
            }

            string modelName = node.GetString("Model", "");
            string dir = Framework.Modules.AnomalyModelCatalog.Resolve(modelName);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            {
                p.Summary = string.IsNullOrEmpty(modelName)
                    ? "异常检测：Models\\Anomaly 下还没有模型（点参数区的「训练 / 标注样本…」建一个）"
                    : "异常检测：模型目录不存在 —— " + dir;
                return false;
            }

            string device = node.GetString("Device", "CPU");

            // 彩色优先：OpenVINO 特征提取器按 3 通道训练；内置描述子自己会灰度化。
            Bitmap frame = (s.ColorInput != null) ? s.ColorInput : s.Current;

            // ---- ROI 必须"跟着工件一起转"，不能只取外接框 ----
            // 异常检测是逐位置比对，前提是"同一网格位置 = 工件上的同一部位"。
            // 若走 ResolveRoi（= FlowRunTransform.ApplyToRect，返回旋转后的轴对齐外接框），
            // 框里的图仍然是斜的：实测残余 5° 就让 256 网格上的边缘错位 1.4 个 patch（patch=8），
            // 30° 时外接框面积还放大 1.87 倍、背景占比跟着上去 —— 逐位置统计直接失效。
            // 所以改走 CropRoi：有旋转时真仿射把工件旋正；只有平移时仍用整数裁剪（快且逐像素一致）。
            Rectangle roi = Rectangle.Empty;
            bool roiAffine = false;
            Bitmap crop = null;
            double roiAngle = RoiAngleOf(node);
            bool hasRoi = node.GetInt("RoiW", 0) > 0 && node.GetInt("RoiH", 0) > 0;
            if (hasRoi)
            {
                crop = CropRoi(frame, s, node, out roi, out roiAffine);
                if (crop == null)
                {
                    p.Summary = "异常检测：ROI 裁剪失败（检查 ROI 参数与位置修正输入）";
                    return false;
                }
            }

            Bitmap input = (crop != null) ? crop : frame;
            // 送检 ROI：裁过就送整张裁剪图（坐标原点已随仿射移动）；没裁就送整图
            Rectangle detectRoi = (crop != null)
                ? new Rectangle(0, 0, crop.Width, crop.Height)
                : Rectangle.Empty;

            // 判定阈值一律传 0：让原生库用**模型自带阈值**归一化，于是 max_score 恒等于
            // "raw / 模型阈值"，界面上的 0~1 得分才能与具体模型无关地稳定（见 AnomalyScore）。
            // 用户填的「判定得分」只移动判定线，不改变热图与面积统计的口径 —— 面积占比
            // 必须钉在一个固定阈值上才有跨批次可比性，跟着判定线漂就没法定 AreaLimit。
            int minArea = node.GetInt("MinArea", 20);
            bool wantHeat = node.GetBool("ShowHeat", true);

            OpResult<VisionAlgo.AnomalyFrame> r =
                _algo.AnomalyDetect(input, dir, device, detectRoi, 0f, minArea, wantHeat);
            if (!r.Success)
            {
                if (crop != null) crop.Dispose();
                // 不用 Tail：会把"模型目录不存在："这类前缀截掉，只剩一个路径
                p.Summary = "异常检测：" + r.Message;
                return false;
            }

            VisionAlgo.AnomalyFrame f = r.Value;
            NativeAnomaly.VA_AnomStat st = f.Stat;

            // ratio：相对**模型自带阈值**的倍数（1.0 = 阈值线）。
            // score：压到 0~1 的**良品得分**（越高越好，0.5 = 阈值线），界面与端口一律用它 ——
            // 原始倍数的量纲随模型走（实测有 7.35、62.05 这种数），现场没法判断"填多少合适"。
            double ratio = st.max_score;
            double score = AnomalyScore.FromRatio(ratio);
            double areaRatio = st.area_ratio;
            bool hasBox = st.defect_count > 0 && st.box_w > 0 && st.box_h > 0;

            // 判定得分（及格线）：参数里存的是 0~1；老方案存的是绝对马氏距离（>1），
            // 用当帧的 st.threshold（此时就是模型自带阈值）折回等价得分。
            double scoreThr = AnomalyScore.NormalizeThreshold(
                node.GetDouble("Threshold", AnomalyScore.DefaultThreshold), st.threshold);

            // 局部坐标 → 整帧坐标。整图检测（crop==null）时原生给的已经是整帧坐标，不能再映射，
            // 否则位置修正的平移会被算两遍。
            Func<double, double, PointF> toFrame = delegate(double u, double v)
            {
                return (crop == null)
                    ? new PointF((float)u, (float)v)
                    : RoiAffineMap.MapPoint(s.Transform, roi, roiAngle, roiAffine, u, v);
            };

            // 热图：先生成在"送检图"的坐标系里；裁过的话还要贴回整帧（含旋转）
            if (wantHeat && f.Heat != null && f.MapW > 0 && f.MapH > 0)
            {
                Bitmap layer = NodeOverlayFactory.AnomalyHeat(f.Heat, f.MapW, f.MapH,
                                                              input.Width, input.Height,
                                                              st.roi_x, st.roi_y);
                if (layer != null)
                {
                    if (crop == null)
                    {
                        p.Overlay = layer;      // 整图检测：图层本来就是整帧尺寸
                    }
                    else
                    {
                        p.Overlay = WarpRoiLayerBack(layer, frame.Size, roi, roiAngle,
                                                     s.Transform, roiAffine);
                        layer.Dispose();
                    }
                }
            }
            EnsureNodeFrame(s, p);

            // ROI 轮廓进辅助层（输入条件，不占结果明细）；缺陷框进主结果层
            if (hasRoi)
                AddRegionOutline(p, s, SearchRegion.FromNode(node, s.OffsetX, s.OffsetY),
                                 frame.Width, frame.Height, roiAffine, "异常检测区域");

            double boxCx = st.box_x + st.box_w / 2.0;
            double boxCy = st.box_y + st.box_h / 2.0;
            PointF boxCenter = toFrame(boxCx, boxCy);

            if (hasBox)
            {
                p.Shapes.Add(BlobShapeAt(s, roi, roiAngle, roiAffine, boxCx, boxCy,
                                         st.box_w, st.box_h, "缺陷"));

                p.Regions.Add(new RegionInfo
                {
                    Index = 1,
                    Cx = boxCenter.X, Cy = boxCenter.Y,
                    Width = st.box_w, Height = st.box_h,
                    Area = st.area_px
                });
            }

            PointF maxPt = toFrame(st.max_x, st.max_y);

            p.Metrics.TargetCount = st.defect_count;
            p.Metrics.TopScore = score;
            p.Metrics.AreaRatio = areaRatio;
            p.Metrics.MaxArea = st.area_px;
            p.Metrics.TopCx = maxPt.X;
            p.Metrics.TopCy = maxPt.Y;

            p.Outputs.Add(new PortValue("Score", "良品得分", FlowPortKind.Number, score));
            p.Outputs.Add(new PortValue("Area", "面积占比", FlowPortKind.Number, areaRatio));
            p.Outputs.Add(new PortValue("Count", "缺陷数", FlowPortKind.Number, (double)st.defect_count));
            p.Outputs.Add(new PortValue("Point", "最大分点位", FlowPortKind.Point, maxPt));

            if (hasBox)
            {
                p.Outputs.Add(new PortValue("Box", "最大缺陷框", FlowPortKind.Rect,
                    new RectangleF((float)boxCenter.X - (float)st.box_w / 2f,
                                   (float)boxCenter.Y - (float)st.box_h / 2f,
                                   st.box_w, st.box_h)));
            }

            // ---- 判定 ----
            // 主判据：良品得分是否跌破「判定得分」（及格线，默认 0.5 = 模型自带阈值线）。
            // 面积上限是可选加严项：有些缺陷"每一处都不算突出但整片都有"（例如镀层发花），
            // 只看峰值会漏。面积占比恒按模型阈值统计，与判定得分解耦。
            bool pass = !AnomalyScore.IsNg(score, scoreThr);

            double areaLimit = node.GetDouble("AreaLimit", 0);
            bool areaNg = areaLimit > 0 && areaRatio > areaLimit;
            if (areaNg) pass = false;

            p.Summary = string.Format(CultureInfo.InvariantCulture,
                "异常检测：良品得分 {0:0.000}（及格线 {1:0.000}）· 面积 {2:P2} · 缺陷 {3} 个",
                score, scoreThr, areaRatio, st.defect_count);

            if (areaNg)
                p.Summary += string.Format(CultureInfo.InvariantCulture, "（面积超上限 {0:P2}）", areaLimit);

            // 模型信息只在"首帧"或异常时附上：每帧刷屏会盖掉真正的读数
            if (!_anomalyInfoLogged)
            {
                string info = _algo.AnomalyInfo();
                if (!string.IsNullOrEmpty(info)) p.Summary += "　[" + info + "]";

                // 首帧把"实际检测区域"打出来：现场说"缺陷框跑到 ROI 外面"时，
                // 这个数一对比就知道是 ROI 被位置修正挪走了、还是框没映射回来。
                p.Summary += string.Format(CultureInfo.InvariantCulture,
                    "　[检测区 {0},{1} {2}×{3}{4}]",
                    hasRoi ? roi.X : 0, hasRoi ? roi.Y : 0,
                    input.Width, input.Height,
                    roiAffine ? " 已旋正" : "");
                _anomalyInfoLogged = true;
            }

            if (crop != null) crop.Dispose();
            return pass;
        }

        /// <summary>异常检测的模型信息是否已经在本执行器里打印过一次。</summary>
        private bool _anomalyInfoLogged;

        /// <summary>
        /// 深度学习 OCR（PaddleOCR）：DB 检测 + CRNN 识别。
        /// 两个模型 + 字典都留空时，按 models/ocr/ 下的约定名自动找（det.onnx / rec.onnx / ppocr_keys.txt）。
        /// </summary>
        private bool RunOcrDl(FlowRunSession s, FlowNode node, FlowNodeProgress p)
        {
            if (s.Current == null)
            {
                p.Status = FlowNodeStatus.Skipped;
                p.Summary = "OCR(深度学习)：没有输入图像（请接在图像源之后）";
                return true;
            }

            string det = node.GetString("DetModel", "");
            string rec = node.GetString("RecModel", "");
            string dict = node.GetString("DictPath", "");
            OcrDlDefaults(ref det, ref rec, ref dict);

            // 检测模型可选：没有 det 时整图当一个文本行识别（一个 rec 模型即可）
            if (string.IsNullOrEmpty(rec) || string.IsNullOrEmpty(dict))
            {
                p.Summary = "OCR(深度学习)：缺少识别模型或字典（models/ocr 下需 rec.onnx / ppocr_keys.txt；"
                          + "det.onnx 可选）";
                return false;
            }
            bool wholeImage = string.IsNullOrEmpty(det);

            string device = node.GetString("Device", "CPU");
            if (string.Equals(node.GetString("Backend", "OpenVINO"), "CUDA", StringComparison.OrdinalIgnoreCase))
                device = "CUDA";

            Bitmap input = s.ColorInput != null ? s.ColorInput : s.Current;

            // 搜索区域：只在框内识别；框为空 = 整幅，与旧行为一致。
            // 识别框都在裁剪图坐标系里，必须加回 usedRoi 偏移才是整帧坐标。
            SearchRegion region = SearchRegion.FromNode(node, s.OffsetX, s.OffsetY);
            Rectangle usedRoi = Rectangle.Empty;
            bool roiAffine = false;
            Bitmap cropOcr = region.Crop(input, s.Transform, out usedRoi, out roiAffine);
            AddRegionOutline(p, s, region, input.Width, input.Height, roiAffine, "OCR 区域");
            double offX = (cropOcr != null) ? usedRoi.X : 0.0;
            double offY = (cropOcr != null) ? usedRoi.Y : 0.0;

            float boxThresh = (float)node.GetDouble("BoxThresh", 0.3);
            float unclip = (float)node.GetDouble("UnclipRatio", 1.5);
            int maxLines = node.GetInt("MaxLines", 100);

            string fullText;
            var r = _algo.OcrDl(cropOcr ?? input, det, rec, dict, device, boxThresh, unclip, maxLines, out fullText);
            if (!r.Success)
            {
                p.Summary = "OCR(深度学习)：" + r.Message;   // 不用 Tail：会把"检测模型不存在："截掉
                return false;
            }

            List<NativeDl.OcrDlLine> lines = r.Value ?? new List<NativeDl.OcrDlLine>();
            for (int i = 0; i < lines.Count; i++)
            {
                NativeDl.OcrDlLine l = lines[i];
                p.Shapes.Add(FlowShapes.BlobRect(l.X + offX + l.Width / 2, l.Y + offY + l.Height / 2,
                                                 l.Width, l.Height, l.Text));
            }

            p.Metrics.TargetCount = lines.Count;
            p.Metrics.CharCount = fullText != null ? fullText.Length : 0;
            p.Metrics.Text = fullText ?? "";

            p.Outputs.Add(new PortValue("Count", "文本行", FlowPortKind.Number, (double)lines.Count));
            p.Outputs.Add(new PortValue("Text", "识别文本", FlowPortKind.Text, fullText ?? ""));
            if (lines.Count > 0)
                p.Outputs.Add(new PortValue("FirstBox", "首行位置", FlowPortKind.Point,
                    new PointF((float)lines[0].X, (float)lines[0].Y)));

            // 行文本也进 Regions，脚本可逐行取（Text 放进去）
            for (int i = 0; i < lines.Count; i++)
            {
                p.Regions.Add(new RegionInfo
                {
                    Index = i + 1,
                    Cx = lines[i].X + offX + lines[i].Width / 2,
                    Cy = lines[i].Y + lines[i].Height / 2,
                    Width = lines[i].Width,
                    Height = lines[i].Height,
                    Text = lines[i].Text ?? ""
                });
            }

            p.Summary = string.Format(CultureInfo.InvariantCulture,
                "OCR(深度学习){3}：{0} 行 · {1} 字 · \"{2}\"",
                lines.Count,
                p.Metrics.CharCount,
                OneLineText(Shorten(fullText, 40)),
                wholeImage ? "（整图识别）" : "");
            if (cropOcr != null) cropOcr.Dispose();
            return lines.Count > 0 || node.GetInt("MinLines", 0) <= 0;
        }

        /// <summary>models/ocr 下的约定名（节点没填时用）。</summary>
        private static void OcrDlDefaults(ref string det, ref string rec, ref string dict)
        {
            try
            {
                string dir = Core.AppPaths.OcrModelDir;
                if (!System.IO.Directory.Exists(dir)) return;

                if (string.IsNullOrWhiteSpace(det))
                {
                    string f = System.IO.Path.Combine(dir, "det.onnx");
                    if (!System.IO.File.Exists(f))
                    {
                        string[] c = System.IO.Directory.GetFiles(dir, "*det*.onnx");
                        if (c.Length > 0) f = c[0];
                    }
                    if (System.IO.File.Exists(f)) det = f;
                }
                if (string.IsNullOrWhiteSpace(rec))
                {
                    string f = System.IO.Path.Combine(dir, "rec.onnx");
                    if (!System.IO.File.Exists(f))
                    {
                        string[] c = System.IO.Directory.GetFiles(dir, "*rec*.onnx");
                        if (c.Length > 0) f = c[0];
                    }
                    if (System.IO.File.Exists(f)) rec = f;
                }
                if (string.IsNullOrWhiteSpace(dict))
                {
                    string f = System.IO.Path.Combine(dir, "ppocr_keys.txt");
                    if (!System.IO.File.Exists(f))
                    {
                        string[] c = System.IO.Directory.GetFiles(dir, "*.txt");
                        if (c.Length > 0) f = c[0];
                    }
                    if (System.IO.File.Exists(f)) dict = f;
                }
            }
            catch (Exception) { }
        }

        /// <summary>
        /// 把一次算法失败的文案拼成「<前缀>[错误码名] 具体原因」。
        /// <para>
        /// 【为什么要带错误码】原来只拼一句人读文本，操作员看到"形状匹配失败"却不知道是
        /// 配置错（ROI 越界）、还是模板没特征、还是真没找到目标 —— 处理动作完全不同。
        /// 码名进摘要后，一眼能分到"该去改参数"还是"该去看料"。
        /// </para>
        /// </summary>
        private string Fail(string prefix, OpResult r)
        {
            ErrorInfo info = _algo.LastErrorInfo;
            string tag = (info != null && info.Code > 0) ? "[" + VaResult.ToString(info.Code) + "] " : "";
            string msg = (info != null && !string.IsNullOrEmpty(info.Message))
                         ? info.Message
                         : (r != null ? r.Message : "");
            return prefix + tag + msg;
        }

        private static string Shorten(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            // 换行必须压平：二维码/条码内容常带换行，直接塞进标签会把行撑断
            string t = s.Replace("\r", " ").Replace("\n", " ").Trim();
            return t.Length <= max ? t : t.Substring(0, Math.Max(1, max - 1)) + "…";
        }

        /// <summary>
        /// 模型解析摘要（只在首次加载那一帧附上，避免每帧刷屏）：
        /// 现场"框位置不对/识别乱"时，先看模型实际 I/O 形状与程序的理解是否一致。
        /// </summary>
        private string DescribeHint(int task, long loadMs)
        {
            if (loadMs <= 1500) return "";
            string d = _algo.DlDescribe(task);
            return string.IsNullOrEmpty(d) ? "" : "（" + d + "）";
        }

        /// <summary>
        /// 让节点留下一份<strong>属于自己的帧</strong>（连续运行默认不留帧）。
        /// <para>
        /// 用途：点开该节点时能看到"它当时处理的画面"。
        /// ⚠️ 必须克隆，绝不能把 <c>s.Current</c> 直接交出去 —— 那是会话的工作帧，
        ///    后续节点会继续改写它，交出去等于把"处理后的图"当成该节点的画面（踩过的坑）。
        /// </para>
        /// </summary>
        private static void EnsureNodeFrame(FlowRunSession s, FlowNodeProgress p)
        {
            if (p == null || p.Frame != null || s == null || s.Current == null) return;
            if (!s.CaptureProgressFrames) return;   // 并行槽不看节点帧：不克隆
            try { p.Frame = (Bitmap)s.Current.Clone(); }
            catch (Exception) { p.Frame = null; }
        }

        // =====================================================================
        //  标定类节点
        // =====================================================================

        /// <summary>
        /// 标定转换：像素 → 机械。三类输入彼此独立，接哪个算哪个：
        /// <list type="bullet">
        /// <item><c>Coord</c>（点）→ 走 N 点标定的 6 参数仿射 / 旋转中心，出机械 X/Y；</item>
        /// <item><c>Distance</c>（像素距离）→ 乘 mm/px（由标定尺度项推导或手填），出机械距离；</item>
        /// <item><c>Angle</c>（像素角度）→ 叠加标定旋转角（+ 回转角），出机械角度。</item>
        /// </list>
        /// <para>
        /// 【只吃两类标定记录】<see cref="CalibKind.NPoint"/>（相似变换矩阵）与
        /// <see cref="CalibKind.RotationCenter"/>（圆心 + 半径）。
        /// 畸变矫正记录在这里被<strong>明确排除</strong>——它没有坐标映射关系，
        /// 混进来只会算出垃圾数值（用户要求"插件只连自己那一类数据"）。
        /// </para>
        /// <para>
        /// 【没有坐标输入时不再吐 (0,0)】旧实现把上游没给坐标的情况按 <c>Cx=0</c> 处理，
        /// 于是"接了个不发坐标的节点"会静默输出机械原点。现在只有真的取到点才输出坐标，
        /// 否则只输出距离 / 角度两条量，摘要里明确写"无坐标输入"。
        /// </para>
        /// </summary>
        private bool RunCalibTransform(FlowRunSession s, FlowNode node, FlowNodeProgress p)
        {
            // ---- ① 点输入：优先「坐标」端子；端子没接才用手填定点 ----
            double px = node.GetDouble("SourceX", 0);
            double py = node.GetDouble("SourceY", 0);
            string pointNote = "手填定点";
            bool hasPoint = false;

            PointF upPoint;
            string upNote;
            if (TryGetInputPoint(s, node, "Coord", out upPoint, out upNote))
            {
                px = upPoint.X;
                py = upPoint.Y;
                hasPoint = true;
                pointNote = string.IsNullOrEmpty(upNote) ? "上游坐标" : upNote + " 坐标";
            }
            else if (Math.Abs(px) > 1e-9 || Math.Abs(py) > 1e-9)
            {
                hasPoint = true;   // 参数里手填了非零定点，视为有意为之
            }

            // ---- ② 距离 / 角度输入（测量节点的直接出口）----
            double distPx, anglePx;
            string distNote, angleNote;
            bool hasDist = TryGetInputNumber(s, node, "Distance", out distPx, out distNote);
            bool hasAngle = TryGetInputNumber(s, node, "Angle", out anglePx, out angleNote);

            // ---- ③ 标定记录 ----
            CalibRecord npt = ResolveCalibRecord(node, CalibKind.NPoint, "CalibRecordId");
            CalibRecord rot = ResolveCalibRecord(node, CalibKind.RotationCenter, "RotCenterId");

            if (npt != null && (npt.Affine == null || npt.Affine.Length < CalibRecord.AffineLength))
                npt = null;      // 记录存在但没算过：当作没有，别让 NullReference 冒到上层

            // ---- ④ 比例 mm/px：手填优先，其次由 N 点标定的尺度项推导 ----
            bool manualScale = string.Equals(node.GetString("ScaleSource", "Calib"), "Manual",
                                             StringComparison.OrdinalIgnoreCase);
            double mmPerPx = 0.0;
            string scaleNote = "未标定";

            if (manualScale)
            {
                mmPerPx = node.GetDouble("MmPerPx", 0);
                scaleNote = mmPerPx > 0
                    ? string.Format(CultureInfo.InvariantCulture, "手填 {0:F6} mm/px", mmPerPx)
                    : "手填比例未设置";
            }
            else if (npt != null)
            {
                double[] a = npt.Affine;
                // 相似变换：A = s·R，两列的模都是尺度 s；取 |det| 的平方根更稳（与旋转方向无关）
                double det = Math.Abs(a[0] * a[3] - a[1] * a[2]);
                if (det > 1e-12) mmPerPx = Math.Sqrt(det);
                scaleNote = mmPerPx > 0
                    ? string.Format(CultureInfo.InvariantCulture, "标定 {0:F6} mm/px", mmPerPx)
                    : "标定尺度无效";
            }

            if (npt == null && rot == null && mmPerPx <= 0)
            {
                p.Status = FlowNodeStatus.Warn;
                p.Summary = "标定转换：既没选标定记录，也没有可用的 mm/px（参数里至少给一个）";
                return false;
            }

            // ---- ⑤ 坐标转换 ----
            // 【只有真的取到点才转换】没接坐标、参数又是 0 时，硬转出来的 (0,0) 机械坐标
            // 会被下游当成"原点处的检测结果"（旧实现就是这样静默吐原点）。
            // 有 N 点标定 → 走仿射；只有旋转中心 → 在原坐标上绕圆周转（回转 / 极坐标场景，保持历史行为）。
            bool canCoord = hasPoint && (npt != null || rot != null);

            double mx = px, my = py;
            string stage = "原始";

            if (canCoord && npt != null)
            {
                double[] a = npt.Affine;
                mx = a[0] * px + a[1] * py + a[4];
                my = a[2] * px + a[3] * py + a[5];
                stage = "N点";
            }

            double rotateDeg = 0.0;
            double radius = double.NaN, polar = double.NaN;

            // ---- ⑥ 旋转中心：绕圆心旋转，并给出半径 / 极角 ----
            if (canCoord && rot != null && rot.Affine != null && rot.Affine.Length >= 2)
            {
                double cx = rot.Affine[0], cy = rot.Affine[1];
                rotateDeg = node.GetDouble("RotateAngle", 0);
                double rad = rotateDeg * Math.PI / 180.0;
                double dx = mx - cx, dy = my - cy;
                radius = Math.Sqrt(dx * dx + dy * dy);
                polar = Math.Atan2(dy, dx) * 180.0 / Math.PI;

                mx = cx + dx * Math.Cos(rad) - dy * Math.Sin(rad);
                my = cx + dx * Math.Sin(rad) + dy * Math.Cos(rad);

                p.Outputs.Add(new PortValue("Radius", "旋转半径", FlowPortKind.Number, radius));
                p.Outputs.Add(new PortValue("Polar", "相对角度", FlowPortKind.Angle, polar));
                p.Outputs.Add(new PortValue("CenterX", "圆心X", FlowPortKind.Number, cx));
                p.Outputs.Add(new PortValue("CenterY", "圆心Y", FlowPortKind.Number, cy));
            }

            bool hasCoordOut = canCoord;

            // ---- ⑦ 距离 / 角度换算 ----
            // 距离：像素 × mm/px；没有比例时原样输出像素值，并在摘要里注明（不冒充 mm）
            double distOut = double.NaN;
            bool distIsMm = false;
            if (hasDist)
            {
                if (mmPerPx > 0)
                {
                    distOut = distPx * mmPerPx;
                    distIsMm = true;
                    p.Outputs.Add(new PortValue("Distance", "机械距离", FlowPortKind.Number, distOut));
                }
                else
                {
                    distOut = distPx;
                    p.Outputs.Add(new PortValue("Distance", "距离(px)", FlowPortKind.Number, distOut));
                }
            }

            // 角度：像素角度 + 标定旋转角（A = s·R 的旋转量）+ 回转角
            double angleOut = double.NaN;
            if (hasAngle)
            {
                double offset = 0.0;
                if (npt != null)
                {
                    double[] a = npt.Affine;
                    offset = Math.Atan2(a[2], a[0]) * 180.0 / Math.PI;   // 标定自身的旋转
                }
                angleOut = FlowGeometryMath.NormalizeAngle180(anglePx + offset + rotateDeg);
                p.Outputs.Add(new PortValue("Angle", "机械角度", FlowPortKind.Angle, angleOut));
            }

            // ---- ⑧ 坐标输出 ----
            if (hasCoordOut)
            {
                var mp = new PointF((float)mx, (float)my);
                p.Outputs.Add(new PortValue("Coord", "机械坐标", FlowPortKind.Pose, mp));
                p.Metrics.TargetCount = 1;
                p.Metrics.TopCx = mx;
                p.Metrics.TopCy = my;
            }

            // 比例本身也作为输出：下游脚本 / 发送数据要直接引用
            p.Outputs.Add(new PortValue("Scale", "比例", FlowPortKind.Number, mmPerPx));

            // ---- ⑨ 摘要 ----
            var sb = new StringBuilder();
            sb.Append("标定转换");
            if (hasCoordOut)
            {
                sb.AppendFormat(CultureInfo.InvariantCulture,
                    "[{0}]：({1:F2},{2:F2}) px → ({3:F3},{4:F3}) mm", stage, px, py, mx, my);
                if (rot != null) sb.AppendFormat(CultureInfo.InvariantCulture, "（转 {0:F1}°）", rotateDeg);
                if (npt != null) sb.AppendFormat(CultureInfo.InvariantCulture, " RMS {0:F4}", npt.RmsError);
            }
            else
            {
                sb.Append("：无坐标输入");
            }

            if (hasDist)
            {
                sb.AppendFormat(CultureInfo.InvariantCulture, " · 距离 {0:F3} {1}（{2}）",
                    distOut, distIsMm ? "mm" : "px", distIsMm ? scaleNote : "未标定");
            }
            if (hasAngle)
            {
                sb.AppendFormat(CultureInfo.InvariantCulture, " · 角度 {0:F2}°（{1}）", angleOut, angleNote);
            }

            p.Summary = sb.ToString();
            return true;
        }

        /// <summary>
        /// 畸变矫正：用「畸变矫正」记录里的 K/dist 把当前图拉正。
        /// <para>只吃 <see cref="CalibKind.Distortion"/>，N 点/旋转中心记录会被拒绝。</para>
        /// </summary>
        private bool RunDistortionCorrect(FlowRunSession s, FlowNode node, FlowNodeProgress p)
        {
            if (s.Current == null)
            {
                p.Status = FlowNodeStatus.Warn;
                p.Summary = "畸变矫正：没有输入图像";
                return false;
            }

            CalibRecord rec = ResolveCalibRecord(node, CalibKind.Distortion);
            if (rec == null)
            {
                p.Status = FlowNodeStatus.Warn;
                p.Summary = "畸变矫正：没有可用的畸变矫正记录（请先在相机标定里做畸变标定并设为当前）";
                return false;
            }

            double fx, fy, cx, cy, k1, k2, p1, p2, k3;
            if (!TryParseDistortion(rec.ExtraJson, out fx, out fy, out cx, out cy,
                                    out k1, out k2, out p1, out p2, out k3))
            {
                p.Status = FlowNodeStatus.Warn;
                p.Summary = "畸变矫正：记录里没有内参（可能是旧数据，请重新标定一次）";
                return false;
            }

            Bitmap src = s.Current;
            Bitmap dst;
            try
            {
                dst = Undistort(src, fx, fy, cx, cy, k1, k2, p1, p2, k3);
            }
            catch (Exception ex)
            {
                p.Status = FlowNodeStatus.Warn;
                p.Summary = "畸变矫正失败：" + ex.Message;
                return false;
            }

            s.Current = dst;
            try { src.Dispose(); } catch (Exception) { }

            p.Summary = string.Format(CultureInfo.InvariantCulture,
                "畸变矫正：fx={0:F1} fy={1:F1} k1={2:F5} k2={3:F5}", fx, fy, k1, k2);

            // 输出：新图已替换 s.Current；指标里记录矫正参数与输出尺寸，便于下游/日志追溯
            p.Metrics.TargetCount = 1;
            p.Metrics.AngleDeg = double.NaN;
            p.Metrics.Text = string.Format(CultureInfo.InvariantCulture,
                "undistorted {0}x{1}", dst.Width, dst.Height);
            return true;
        }

        /// <summary>取标定记录：参数指定优先，其次用该相机的「当前」记录；类型不符一律拒绝。</summary>
        private static CalibRecord ResolveCalibRecord(FlowNode node, CalibKind want)
        {
            return ResolveCalibRecord(node, want, "CalibRecordId");
        }

        private static CalibRecord ResolveCalibRecord(FlowNode node, CalibKind want, string paramKey)
        {
            CalibSettings all = ConfigStore.Current.Calibrations;
            if (all == null || node == null) return null;

            string id = node.GetString(paramKey, "");
            CalibRecord rec = null;

            if (!string.IsNullOrEmpty(id))
            {
                rec = all.Find(id);
                if (rec == null) return null;
            }
            else
            {
                // 没指定就取第一条该类型的记录（不跨类型串用）
                for (int i = 0; i < all.Records.Count; i++)
                {
                    CalibRecord r = all.Records[i];
                    if (r != null && r.Kind == want) { rec = r; break; }
                }
            }

            if (rec == null || rec.Kind != want) return null;
            return rec;
        }

        /// <summary>从记录里取出 fx/fy/cx/cy 与 5 个畸变系数（ExtraJson 是标定窗体写的键值对）。</summary>
        private static bool TryParseDistortion(string json, out double fx, out double fy,
                                               out double cx, out double cy,
                                               out double k1, out double k2,
                                               out double p1, out double p2, out double k3)
        {
            fx = fy = cx = cy = k1 = k2 = p1 = p2 = k3 = 0;
            if (string.IsNullOrEmpty(json)) return false;

            bool ok = true;
            ok &= TryPick(json, "fx", out fx);
            ok &= TryPick(json, "fy", out fy);
            ok &= TryPick(json, "cx", out cx);
            ok &= TryPick(json, "cy", out cy);
            ok &= TryPick(json, "k1", out k1);
            ok &= TryPick(json, "k2", out k2);
            ok &= TryPick(json, "p1", out p1);
            ok &= TryPick(json, "p2", out p2);
            ok &= TryPick(json, "k3", out k3);
            return ok && fx > 0 && fy > 0;
        }

        private static bool TryPick(string json, string key, out double value)
        {
            value = 0;
            int at = json.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
            if (at < 0) return false;

            int colon = json.IndexOf(':', at);
            if (colon < 0) return false;

            int start = colon + 1;
            while (start < json.Length && (json[start] == ' ' || json[start] == '\t')) start++;

            int end = start;
            while (end < json.Length && (char.IsDigit(json[end]) || json[end] == '-' || json[end] == '.'
                                         || json[end] == 'e' || json[end] == 'E' || json[end] == '+'))
                end++;

            return double.TryParse(json.Substring(start, end - start), NumberStyles.Float,
                                   CultureInfo.InvariantCulture, out value);
        }

        /// <summary>
        /// 去畸变（纯 C#，双线性和迭代反解）。
        /// <para>
        /// 为什么不用 C++：这一步只依赖 K/dist 的算术，不需要 OpenCV；
        /// 放 C# 侧避免为一件事再去动原生工程与 P/Invoke（回归面小）。
        /// 每帧全图重映射约几十毫秒，标定/检测低频场景够用。
        /// </para>
        /// </summary>
        private static Bitmap Undistort(Bitmap src, double fx, double fy, double cx, double cy,
                                        double k1, double k2, double p1, double p2, double k3)
        {
            int w = src.Width, h = src.Height;
            var dst = new Bitmap(w, h, PixelFormat.Format24bppRgb);

            BitmapData sd = null, dd = null;
            try
            {
                sd = src.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                dd = dst.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);

                int stride = sd.Stride;
                var srcBuf = new byte[stride * h];
                var dstBuf = new byte[dd.Stride * h];
                Marshal.Copy(sd.Scan0, srcBuf, 0, srcBuf.Length);

                int dstStride = dd.Stride;

                for (int y = 0; y < h; y++)
                {
                    double yd = (y - cy) / fy;

                    for (int x = 0; x < w; x++)
                    {
                        double xd = (x - cx) / fx;

                        // 迭代反解：已知畸变后的归一化坐标，求理想坐标
                        double xu = xd, yu = yd;
                        for (int it = 0; it < 5; it++)
                        {
                            double r2 = xu * xu + yu * yu;
                            double radial = 1 + k1 * r2 + k2 * r2 * r2 + k3 * r2 * r2 * r2;
                            double dx = 2 * p1 * xu * yu + p2 * (r2 + 2 * xu * xu);
                            double dy = p1 * (r2 + 2 * yu * yu) + 2 * p2 * xu * yu;

                            xu = (xd - dx) / radial;
                            yu = (yd - dy) / radial;
                        }

                        double sx = xu * fx + cx;
                        double sy = yu * fy + cy;

                        int o = y * dstStride + x * 3;
                        if (sx < 0 || sy < 0 || sx >= w - 1 || sy >= h - 1)
                        {
                            dstBuf[o] = 0; dstBuf[o + 1] = 0; dstBuf[o + 2] = 0;
                            continue;
                        }

                        // 双线性
                        int x0 = (int)sx, y0 = (int)sy;
                        double fxr = sx - x0, fyr = sy - y0;
                        int i00 = y0 * stride + x0 * 3;
                        int i10 = i00 + 3;
                        int i01 = i00 + stride;
                        int i11 = i01 + 3;

                        for (int c = 0; c < 3; c++)
                        {
                            double v = srcBuf[i00 + c] * (1 - fxr) * (1 - fyr)
                                     + srcBuf[i10 + c] * fxr * (1 - fyr)
                                     + srcBuf[i01 + c] * (1 - fxr) * fyr
                                     + srcBuf[i11 + c] * fxr * fyr;
                            int iv = (int)(v + 0.5);
                            dstBuf[o + c] = (byte)(iv < 0 ? 0 : iv > 255 ? 255 : iv);
                        }
                    }
                }

                Marshal.Copy(dstBuf, 0, dd.Scan0, dstBuf.Length);
            }
            finally
            {
                if (sd != null) src.UnlockBits(sd);
                if (dd != null) dst.UnlockBits(dd);
            }

            return dst;
        }

        /// <summary>首次加载（编译模型）超过 1.5 秒时在摘要里注明，避免误判成卡死。</summary>
        private static string LoadHint(Stopwatch sw)
        {
            return (sw != null && sw.ElapsedMilliseconds > 1500)
                ? string.Format(CultureInfo.InvariantCulture, "（首次加载 {0} ms）", sw.ElapsedMilliseconds)
                : "";
        }

        /// <summary>
        /// 调试用：把节点生成的效果图（掩膜/深度伪彩）存成 PNG。
        /// 只有设了环境变量 <c>VA_DL_DUMP=&lt;目录&gt;</c> 才写，平时无开销。
        /// </summary>
        private static void DumpOverlay(Bitmap overlay, string nodeName, int task)
        {
            if (overlay == null) return;

            try
            {
                string dir = Environment.GetEnvironmentVariable("VA_DL_DUMP");
                if (string.IsNullOrEmpty(dir) || !System.IO.Directory.Exists(dir)) return;

                string tag = task == NativeDl.TaskSeg ? "seg" : task == NativeDl.TaskPose ? "pose" : "depth";
                string file = System.IO.Path.Combine(dir, "dl_" + tag + ".png");
                overlay.Save(file, System.Drawing.Imaging.ImageFormat.Png);
            }
            catch (Exception) { }
        }

        /// <summary>
        /// 深度学习模型的默认位置：<strong>方案目录优先</strong>
        /// （<c>&lt;方案&gt;\Models\&lt;流程名&gt;\&lt;节点名&gt;\*.onnx</c>），其次全局 <c>models\&lt;任务&gt;\</c>。
        /// <para>
        /// 按节点分目录是为了让"同一流程里两个节点跑不同模型"成为可能 ——
        /// 全局一个 models\seg 目录做不到这件事。
        /// </para>
        /// </summary>
        private static string DefaultDlModel(FlowRunSession s, FlowNode node, int task)
        {
            // ① 方案内该节点的模型目录
            string scoped = Core.AssetPaths.ModelDirOf(s.Flow, node);
            string hit = FirstOnnx(scoped);
            if (!string.IsNullOrEmpty(hit)) return hit;

            // 2) 全局 models/<任务>/
            return FirstOnnx(System.IO.Path.Combine(Core.AppPaths.DlModelDir, DlFolderOf(task)));
        }

        private static string FirstOnnx(string dir)
        {
            try
            {
                if (string.IsNullOrEmpty(dir) || !System.IO.Directory.Exists(dir)) return "";

                string[] files = System.IO.Directory.GetFiles(dir, "*.onnx");
                if (files.Length == 0) return "";

                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                return files[0];
            }
            catch (Exception) { return ""; }
        }

        private static string DlFolderOf(int task)
        {
            return task == NativeDl.TaskSeg ? "seg" : task == NativeDl.TaskPose ? "pose" : "depth";
        }

        // ---------------------- 图像源（FR-ALG-01）----------------------

        private bool RunImageSource(FlowRunSession s, FlowNode node, FlowNodeProgress p)
        {
            string source = node.GetString("Source", "Camera");


            // 文件夹模式要写进摘要的「第 i/N 张 · 文件名」；其它模式为空
            string frameNote = "";

            Bitmap raw = null;

            Bitmap work = null;
            bool workIsClone = false;

            try
            {
                if (IsFileImageSource(source))
                {
                    string path;

                    if (string.Equals(source, "Folder", StringComparison.OrdinalIgnoreCase))
                    {
                        string folder = node.GetString("FolderPath", "");
                        string listError;
                        string[] files = ImageFolderSource.List(folder, out listError);
                        if (files.Length == 0)
                        {
                            p.Summary = "图像源：" + (listError ?? "文件夹内没有图片");
                            return false;
                        }

                        // 键带上「流程 / 节点 / 文件夹」：同链两个图像源各自计数，换目录自动从第一张开始
                        string cursorKey = ImageFolderCursor.Key(s.Flow, node, folder);

                        int idx = ImageFolderCursor.Next(cursorKey, files.Length,
                                                        node.GetBool("FolderPersist", false),
                                                        node.GetInt("FolderHoldMs", 0));
                        path = files[idx];

                        frameNote = string.Format(CultureInfo.InvariantCulture, "第 {0}/{1} 张 · {2}",
                                                  idx + 1, files.Length, System.IO.Path.GetFileName(path));
                    }
                    else
                    {
                        path = node.GetString("FilePath", "");
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
                    }

                    string loadError;
                    raw = LoadImageFile(path, out loadError);
                    if (raw == null)
                    {
                        p.Summary = "图像源：读取图片失败（" + loadError + "）";
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

                // 原图副本只在"流程预览要显示原图"时保存；生产并行运行不需要，
                // 否则每帧白白多一次整幅克隆（5MP 约 15MB）。
                if (s.CaptureSourceFrame)
                {
                    if (s.SourceFrame != null) { s.SourceFrame.Dispose(); s.SourceFrame = null; }
                    s.SourceFrame = (Bitmap)work.Clone();
                }

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

                p.Summary = string.IsNullOrEmpty(frameNote)
                    ? string.Format(CultureInfo.InvariantCulture, "取图 {0}x{1}", work.Width, work.Height)
                    : string.Format(CultureInfo.InvariantCulture, "{0} · 取图 {1}x{2}",
                                    frameNote, work.Width, work.Height);

                // 留一份"刚采进来的原图"：点开图像源必须看到原图。
                // ⚠️ 克隆，不能把 s.Current 交出去（后续节点会改写它）。
                EnsureNodeFrame(s, p);
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

        /// <summary>
        /// 该「取图方式」是否走本地文件（单文件 / 文件夹）。两者都不需要相机，
        /// 也共用同一套解码与 ROI/旋转/镜像处理，区别只在「路径从哪来」。
        /// </summary>
        private static bool IsFileImageSource(string source)
        {
            return string.Equals(source, "File", StringComparison.OrdinalIgnoreCase)
                || string.Equals(source, "Folder", StringComparison.OrdinalIgnoreCase);
        }

        // =====================================================================
        //  读取点云（3D）
        // =====================================================================

        /// <summary>点云文件夹模式接受的扩展名（点云本身 + 深度图）。</summary>
        private static readonly string[] PointCloudExts =
        {
            ".ply", ".pcd", ".png", ".bmp", ".jpg", ".jpeg", ".tif", ".tiff"
        };

        /// <summary>
        /// 读取点云节点：PLY / PCD 文件（单文件或文件夹轮播）或深度图（内参反投影）→ 点云。
        /// 产出写到 <see cref="FlowRunSession.CurrentCloud"/>（主界面显示）与
        /// <see cref="FlowNodeProgress.Cloud"/>（流程预览显示）；两者都是引用移交，无释放手续。
        /// </summary>
        private bool RunPointCloudSource(FlowRunSession s, FlowNode node, FlowNodeProgress p)
        {
            string source = node.GetString("Source", "File");
            string frameNote = "";
            string path;

            try
            {
                if (string.Equals(source, "Folder", StringComparison.OrdinalIgnoreCase))
                {
                    string folder = node.GetString("FolderPath", "");
                    string listError;
                    string[] files = ImageFolderSource.List(folder, PointCloudExts, "点云", out listError);
                    if (files.Length == 0)
                    {
                        p.Summary = "点云源：" + (listError ?? "文件夹内没有点云");
                        return false;
                    }

                    // 与图像源同一套游标：同链两个点云源各自计数，换目录自动回到第一个
                    string cursorKey = ImageFolderCursor.Key(s.Flow, node, folder);
                    int idx = ImageFolderCursor.Next(cursorKey, files.Length,
                                                     node.GetBool("FolderPersist", false),
                                                     node.GetInt("FolderHoldMs", 0));
                    path = files[idx];

                    frameNote = string.Format(CultureInfo.InvariantCulture, "第 {0}/{1} 个 · {2}",
                                              idx + 1, files.Length, System.IO.Path.GetFileName(path));
                }
                else
                {
                    path = node.GetString("FilePath", "");
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        p.Summary = "点云源：未选择点云文件";
                        return false;
                    }
                    if (!System.IO.File.Exists(path))
                    {
                        p.Summary = "点云源：文件不存在（" + path + "）";
                        return false;
                    }
                }

                double fx = node.GetDouble("Fx", 1000);
                double fy = node.GetDouble("Fy", 1000);
                double cx = node.GetDouble("Cx", 320);
                double cy = node.GetDouble("Cy", 240);
                double zScale = node.GetDouble("ZScale", 1);

                string loadError;
                VisionSystem.Controls.PointCloud.PointCloudData cloud =
                    VisionSystem.Controls.PointCloud.PointCloudFileReader.Load(path, fx, fy, cx, cy, zScale, out loadError);
                if (cloud == null)
                {
                    p.Summary = "点云源：" + (loadError ?? "读取失败");
                    return false;
                }

                // 会话留最新点云（主界面 OutputReady 用）；预览按节点带一份引用
                s.CurrentCloud = cloud;
                if (s.CaptureFrames) p.Cloud = cloud;

                p.Outputs.Add(new PortValue("Count", "点数", FlowPortKind.Number, (double)cloud.Count));
                if (p.Metrics != null) p.Metrics.TargetCount = cloud.Count;   // 结果区类型化输出同源

                p.Summary = (frameNote.Length > 0 ? frameNote + " · " : "")
                          + "点数 " + cloud.Count.ToString("N0", CultureInfo.InvariantCulture)
                          + (cloud.HasColor ? " · 含颜色" : "");
                return true;
            }
            catch (Exception ex)
            {
                p.Summary = "点云源：读取失败（" + ex.Message + "）";
                return false;
            }
        }

        /// <summary>
        /// 读图片到内存位图；失败返回 null 并给出原因。
        /// <para>先 <c>Image.FromFile</c> 再拷贝：避免 Bitmap 长期持有文件句柄（否则用户想删/换图会被占住）。</para>
        /// </summary>
        private static Bitmap LoadImageFile(string path, out string error)
        {
            error = "";
            try
            {
                using (var probe = Image.FromFile(path))
                    return new Bitmap(probe);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
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

            // 搜索区域：只对框内做预处理，再把结果贴回整帧（框为空 = 整幅，与旧行为一致）。
            // 统一走 SearchRegion，参数读取与旋正口径全库只有一处实现。
            SearchRegion region = SearchRegion.FromNode(node, s.OffsetX, s.OffsetY);
            Rectangle usedRoi = Rectangle.Empty;
            bool roiAffine = false;
            Bitmap crop = region.Crop(s.Current, s.Transform, out usedRoi, out roiAffine);
            AddRegionOutline(p, s, region, s.Current.Width, s.Current.Height, roiAffine, "预处理区域");
            try
            {
                var pr = _algo.Preprocess(crop ?? s.Current, BuildPreprocess(node));
                if (!pr.Success)
                {
                    p.Summary = Fail("预处理失败：", pr);
                    return false;
                }

                if (crop == null)
                {
                    s.ReplaceFrame(pr.Value);
                }
                else
                {
                    // 只替换框内那一块，框外像素保持原样（否则等于把整帧丢了）
                    Bitmap merged = (Bitmap)s.Current.Clone();
                    BlitPatch(merged, pr.Value, usedRoi.X, usedRoi.Y);
                    pr.Value.Dispose();
                    s.ReplaceFrame(merged);
                }
            }
            finally
            {
                if (crop != null) crop.Dispose();
            }

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
            // 【必须提前取宽高】下面 s.ReplaceFrame(output) 会把旧的 s.Current 释放掉，
            // 之后再用 input.Width 就会抛 ArgumentException("参数无效")，堆栈停在 Image.get_Width，
            // 完全看不出是"帧已被释放"。
            int frameW = input.Width;
            int frameH = input.Height;
            Rectangle roi = SearchRegion.FromNode(node, s.OffsetX, s.OffsetY)
                            .BoundsToFrame(s.Transform, frameW, frameH);

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

                AddRegionOutline(p, s, SearchRegion.FromNode(node, s.OffsetX, s.OffsetY),
                                 frameW, frameH, false, "图像滤波区域");

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
            SearchRegion region = SearchRegion.FromNode(node, s.OffsetX, s.OffsetY);
            var kept = new List<BlobResult>();
            foreach (BlobResult b in r.Blobs)
            {
                if (!region.ContainsPoint(s.Transform, b.Cx, b.Cy)) continue;
                kept.Add(b);
            }

            foreach (BlobResult b in kept)
                p.Shapes.Add(FlowShapes.BlobRect(b.Cx, b.Cy, b.Width, b.Height));

            AddRegionOutline(p, s, region, s.Current.Width, s.Current.Height, false, "颜色提取区域");

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

            // 搜索区域：只在框内检测；框为空 = 整幅，与旧行为一致。
            // 检测结果（框 / 中心点）都在裁剪图坐标系里，必须加回 usedRoi 偏移才是整帧坐标。
            SearchRegion region = SearchRegion.FromNode(node, s.OffsetX, s.OffsetY);
            Rectangle usedRoi = Rectangle.Empty;
            bool roiAffine = false;
            Bitmap crop = region.Crop(input, s.Transform, out usedRoi, out roiAffine);
            AddRegionOutline(p, s, region, input.Width, input.Height, roiAffine, "目标识别区域");
            double offX = (crop != null) ? usedRoi.X : 0.0;
            double offY = (crop != null) ? usedRoi.Y : 0.0;

            var dp = new DetectParam
            {
                ModelPath = node.GetString("ModelPath", "models/yolo11n.onnx"),
                Device = node.GetString("Device", "CPU"),
                ScoreThresh = (float)node.GetDouble("ScoreThresh", 0.25),
                IouThresh = (float)node.GetDouble("IouThresh", 0.45),
                MaxCount = node.GetInt("MaxCount", 100)
            };

            OpResult<DetectOutput> dr = _algo.Detect(crop ?? input, dp);
            if (!dr.Success)
            {
                p.Summary = "目标识别：" + Tail(dr.Message);
                if (crop != null) crop.Dispose();
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
                var box = FlowShapes.DetectBox(d.X + offX, d.Y + offY, d.Width, d.Height, primary, label);
                if (box != null) box.Name = label;   // Name 供悬浮提示，Label 供画布绘制
                p.Shapes.Add(box);
            }

            p.Metrics.TargetCount = outp.Detections.Count;
            if (outp.Best != null)
            {
                p.Metrics.TopScore = outp.Best.Score;
                p.Metrics.TopCx = outp.Best.X + offX + outp.Best.Width * 0.5;
                p.Metrics.TopCy = outp.Best.Y + offY + outp.Best.Height * 0.5;
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
            if (crop != null) crop.Dispose();
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

            // ① 取图：ROI 非空时只处理 ROI 内的一块；
            //    位置修正带旋转时走**仿射裁剪**（拿到的图跟着工件一起转），否则原样整数裁剪。
            Rectangle roi = Rectangle.Empty;
            bool roiAffine = false;
            double roiAngle = RoiAngleOf(node);   // ROI 自身角度（示教时画出来的倾斜）
            Bitmap crop = null;
            Bitmap gray = null;
            bool grayOwned = false;

            try
            {
                if (RawRoi(s, node) != Rectangle.Empty)
                {
                    crop = CropRoi(frame, s, node, out roi, out roiAffine);
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
                    MinCharW = node.GetDouble("MinCharW", 0),
                    MaxCharW = node.GetDouble("MaxCharW", 0),
                    MinArea = node.GetDouble("MinArea", 0),
                    MaxArea = node.GetDouble("MaxArea", 0),
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

                // ③ 模板目录：
                //    默认值（"MODELS\OCR"）→ 优先用「MODELS\OCR\流程名\节点名」（同一流程里的
                //    不同 OCR 节点各用各的字库）；方案没绑定该流程时自动回落到全局 MODELS\OCR。
                //    用户显式填了别的目录 → 原样尊重，不动。
                string dirName = node.GetString("TemplateDir", DefaultOcrDirName);
                if (string.IsNullOrWhiteSpace(dirName)) dirName = DefaultOcrDirName;

                string dir;
                if (string.Equals(dirName.Trim(), DefaultOcrDirName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(dirName.Trim(), "OCRModel", StringComparison.OrdinalIgnoreCase))
                {
                    // 旧值 "OCRModel" 也走这条路：目录已合并到 MODELS\OCR
                    string global = Core.AppPaths.OcrModelDir;
                    string scoped = Core.AssetPaths.OcrDirOf(s.Flow, node);
                    dir = Core.AssetPaths.Ensure(scoped, global);
                }
                else
                {
                    dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, dirName);
                }

                // ③ **必须先设参、再灌模板**：
                //   模板在 native 侧要"二值化 + 归一化"后才进库，用的正是 ctx->ocr.param。
                //   之前这里先灌模板、后设参，结果模板按"上一次的参数甚至全 0"处理，
                //   与候选（按节点参数）尺寸/极性对不上 —— 表现就是"有模板却大量字认不出来"。
                OpResult sp = _algo.SetOcrParam(op);
                if (!sp.Success)
                {
                    p.Summary = "字符识别：" + Tail(sp.Message);
                    return false;
                }

                // 这几个参数会改变模板的二进制表示，改了就**必须重灌模板**，否则库里还是旧样子
                string normKey = op.Threshold.ToString(CultureInfo.InvariantCulture) + "|"
                               + (op.Invert ? "1" : "0") + "|"
                               + op.NormW.ToString(CultureInfo.InvariantCulture) + "|"
                               + op.NormH.ToString(CultureInfo.InvariantCulture);

                // ③' 字符源：训练出来的 .ocrtpl 优先；没有库时回落到旧目录（一个字符一张图）。
                string libPath = OcrCharLibraryFile.ResolveActive(node.GetString("LibFile", ""),
                                                                  (s.Flow != null) ? s.Flow.Id : "");
                OpResult<OcrTemplateLoadResult> tpl = (libPath != null)
                    ? EnsureOcrLibrary(libPath, normKey)
                    : EnsureOcrTemplates(dir, normKey);

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

                // ⑤-a 认出来几个：label 不是 "?" 的才算（natvie 侧未采纳时 label 一定是 "?"）。
                // 这个数与 charCount 的差值就是"模板不匹配"的直接证据：
                // 分割出 4 个、认出 0 个 → 不是分割问题，是模板/方向/阈值问题。
                int matched = 0;
                double maxRawScore = 0.0;
                foreach (OcrChar c in chars)
                {
                    if (!string.IsNullOrEmpty(c.Label) && c.Label != "?") matched++;
                    if (c.Score > maxRawScore) maxRawScore = c.Score;
                }

                p.Metrics.Text = text;
                p.Metrics.CharCount = charCount;
                p.Metrics.MeanScore = meanScore;
                p.Metrics.MatchedCount = matched;
                p.Metrics.TopScore = maxRawScore;          // OCR 里它的语义是"最高原始匹配分"
                p.Metrics.TextOnly = !hasTemplate;
                p.Metrics.TextOnlyReason = hasTemplate ? null : (lib != null ? lib.Brief() : "未载入字符模板");
                p.Metrics.TargetCount = charCount;

                // ⑥ 区域框（OCR 作用区域，默认开）：即使识别失败也画，用户才知道"它在看哪一块"。
                //    走 AuxShapes —— 它是输入几何，不是检测目标，不能占结果区明细行。
                if (showRegionBox)
                {
                    if (roiAffine)
                    {
                        // 仿射：区域框本身也要跟着转（绕 ROI 中心 = RoiAngle + dAngle），
                        // 否则看起来像"框没跟随"——画成轴对齐外接框时尤其明显：
                        // 界面上的轮廓转了，运行期的框没转，用户看到的就是"有一个 ROI 一直不变"。
                        double effAngle = RoiAffineMap.EffectiveAngle(s.Transform, roiAngle);
                        PointF c = RoiAffineMap.FrameCenter(s.Transform, roi);
                        var regionRot = new RotatedRectShape(c, roi.Width, roi.Height, (float)effAngle);
                        regionRot.StrokeColor = FlowShapes.ColorOcrRegion;
                        regionRot.LineWidth = 1.5f;
                        regionRot.IsEditable = false;
                        regionRot.Name = "OcrRegion";
                        regionRot.Label = "OCR 区域 " + roi.Width + "×" + roi.Height
                                          + "（旋转 " + effAngle.ToString("F1") + "°）";
                        p.AuxShapes.Add(regionRot);
                    }
                    else
                    {
                        Rectangle region = roi.IsEmpty
                            ? new Rectangle(0, 0, frame.Width, frame.Height)
                            : roi;
                        var regionBox = FlowShapes.OcrRegionBox(region,
                            "OCR 区域 " + region.Width + "×" + region.Height);
                        if (regionBox != null) p.AuxShapes.Add(regionBox);
                    }
                }

                // ⑧ 之前的公共量：仿射下字符框/串框的角度就是生效角度
                double boxAngle = roiAffine ? RoiAffineMap.EffectiveAngle(s.Transform, roiAngle) : 0.0;

                // ⑦ 合并字符串框（默认开）：按行归并，一行一个框，标签就是该行的识别文本。
                //    逐字符框是另一个开关，默认关 —— "一行 30 个框"是噪声不是清晰。
                if (showStringBox && chars.Count > 0)
                {
                    foreach (OcrLine line in GroupOcrLines(chars))
                    {
                        PointF lineC = MapRoiPoint(s, roi, roiAngle, roiAffine,
                            line.X + line.W / 2.0, line.Y + line.H / 2.0);
                        string txt = string.IsNullOrEmpty(line.Text) ? "?" : line.Text;

                        IGeometryShape lineBox = roiAffine
                            ? (IGeometryShape)FlowShapes.OcrStringRotBox(lineC, line.W, line.H, boxAngle, txt)
                            : FlowShapes.OcrStringBox(
                                new Rectangle((int)Math.Round(lineC.X - line.W / 2.0),
                                              (int)Math.Round(lineC.Y - line.H / 2.0),
                                              Math.Max(1, (int)Math.Round(line.W)),
                                              Math.Max(1, (int)Math.Round(line.H))),
                                txt);
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
                        PointF cc = MapRoiPoint(s, roi, roiAngle, roiAffine,
                            c.X + c.W / 2.0, c.Y + c.H / 2.0);

                        IGeometryShape box = roiAffine
                            ? (IGeometryShape)FlowShapes.OcrCharRotBox(cc, c.W, c.H, boxAngle, label)
                            : FlowShapes.OcrBox(cc.X - c.W / 2.0, cc.Y - c.H / 2.0, c.W, c.H, label);
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
                string tail = "";
                if (!hasTemplate)
                {
                    tail = "；" + lib.Brief();
                }
                else if (charCount > 0 && matched == 0)
                {
                    // 【最容易误判的一种失败】分割正常、模板也加载了，但一个都没认出来。
                    // 不说清原因，用户只会觉得"OCR 坏了"；真实原因通常是
                    // 模板不是单个字符 / 方向不一致 / 接受阈值太高。
                    tail = string.Format(CultureInfo.InvariantCulture,
                        "；{0} 个字符全部低于接受阈值（最高 {1:F2}）—— "
                        + "请确认模板是【单个字符】、方向与现场一致，或调低「接受阈值」",
                        charCount, maxRawScore);
                }

                p.Summary = string.Format(CultureInfo.InvariantCulture,
                    "字符识别：\"{0}\"（{1} 字符，认出 {2}，平均置信度 {3:F2}）{4}",
                    shown, charCount, matched, meanScore, tail)
                    + PoseInputNote(s, node);

                // ⑪ 空库 / 全失败 / 部分失败 / 有模板但一个都没认出来 → 只黄不红
                //    （Warn 让位点见 StepNext）：
                //    OK 在本系统里是"检测通过"的语义，把"根本没识别"显示成 OK 会让操作员
                //    把未识别工件判为良品 —— 这是误判风险，不是界面美化问题。
                if ((lib != null && lib.State != OcrTemplateState.Ok)
                    || (hasTemplate && charCount > 0 && matched == 0))
                {
                    p.Status = FlowNodeStatus.Warn;
                }

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
            string upstream = FindUpstreamText(s);

            string content;
            if (string.Equals(mode, "ModbusWrite", StringComparison.OrdinalIgnoreCase))
            {
                // 批量映射优先：把上游数值（坐标 / 角度 / 结论）一次写进多个寄存器。
                // 单寄存器那套（MbAddress + MbValue）只能写固定值，产线上根本不够用。
                string map = node.GetString("MbMap", "");
                if (!string.IsNullOrWhiteSpace(map))
                    return RunModbusMap(s, node, p, commName, map, autoConnect);

                // 多流程分区：实际地址 = 配置地址 + 流程序号 × 步长（步长 0 = 关闭，老行为不变）
                int flowShift = FlowAddressShift(s, node);

                int addr = node.GetInt("MbAddress", 0) + flowShift;
                int val = node.GetInt("MbValue", 0);

                // 高频笔误：把 Modbus 端口 502 填进了"寄存器地址"。502 本身是合法寄存器号，
                // 光看范围查不出来，但写进 PLC 的 502 号寄存器不会有任何业务效果 —— 提前点破。
                if (addr == 502)
                {
                    p.Summary = "发送数据：寄存器地址填的是 502 —— 那是 Modbus 的端口号，不是寄存器地址；"
                              + "这里要填 PLC 的保持寄存器号（0 起，例如结果位 20）";
                    return false;
                }

                if (addr < 0 || addr > 65535 || val < 0 || val > 65535)
                {
                    p.Summary = "发送数据：Modbus 地址 / 写入值需在 0~65535（当前 "
                              + addr.ToString(CultureInfo.InvariantCulture) + " / "
                              + val.ToString(CultureInfo.InvariantCulture) + "）";
                    return false;
                }

                if (autoConnect && !EnsureCommConnected(commName, "发送数据", p))
                {
                    if (IsTransferUnavailable(p.Summary))
                    {
                        // 链路未就绪（PLC 掉线 / 监听没起来 / 自动连接冷却）不是检测失败：
                        // 判 Warn（只影响显示、永不等于 NG），否则整帧 NG、产线永远不是良品。
                        p.Status = FlowNodeStatus.Warn;
                        p.Summary = p.Summary + "（本次未发送，不判 NG）";
                        CaptureCommFrame(s, p);
                        return true;
                    }
                    return false;
                }

                OpResult wr = CommRegistry.ModbusWrite(commName, (ushort)addr, (ushort)val);
                if (!wr.Success)
                {
                    if (IsTransferUnavailable(wr.Message))
                    {
                        p.Status = FlowNodeStatus.Warn;
                        p.Summary = "发送数据：" + commName + " → 未写入（" + Tail(wr.Message) + "，不判 NG）";
                        CaptureCommFrame(s, p);
                        return true;
                    }

                    p.Summary = "发送数据：" + Tail(wr.Message);
                    return false;
                }

                content = string.Format(CultureInfo.InvariantCulture, "寄存器 {0} = {1}", addr, val);
            }
            else
            {
                // 【配置错误兜底】目标是 Modbus 却选了文本模式：直接给出可执行的改法。
                // 否则会落到 CommRegistry.SendText 的"不支持文本发送"，现场看不出该改什么，
                // 表现就是"点了发送一点反应都没有、模拟器那边什么都没收到"。
                CommEndpointConfig ep = CommRegistry.Config(commName);
                if (ep != null && CommKind.Normalize(ep.Kind) == CommKind.ModbusTcp)
                {
                    p.Summary = "发送数据：「" + commName + "」是 Modbus TCP，只能写寄存器 —— "
                              + "请把「发送方式」改成「Modbus 写寄存器」（并填寄存器地址 / 写入值），"
                              + "或把通讯换成 TCP 客户端 / 服务端";
                    return false;
                }

                content = string.Equals(mode, "Format", StringComparison.OrdinalIgnoreCase)
                    ? ExpandCommFormat(node.GetString("Format", ""), s).Replace("{Input}", upstream)
                    : node.GetString("Text", "");

                // 固定文本留空时自动取上游文本输出：Text 输入端子真正可用
                // （脚本 / OCR → 发送数据直连即可发上游文本，不必把内容抄进参数）
                if (content.Length == 0 && upstream.Length > 0) content = upstream;

                if (autoConnect && !EnsureCommConnected(commName, "发送数据", p))
                {
                    if (IsTransferUnavailable(p.Summary))
                    {
                        p.Status = FlowNodeStatus.Warn;
                        p.Summary = p.Summary + "（本次未发送，不判 NG）";
                        CaptureCommFrame(s, p);
                        return true;
                    }
                    return false;
                }

                OpResult sr = CommRegistry.SendText(commName, content);
                if (!sr.Success)
                {
                    if (IsTransferUnavailable(sr.Message))
                    {
                        // 典型场景：MES 是 TCP 服务端但还没有客户端接入 → Broadcast 返回"没有客户端连接"。
                        // 视觉侧没有可发的对象，不是检测失败，判 Warn 并在摘要里说清（与「接收数据无数据判跳过」同口径）。
                        p.Status = FlowNodeStatus.Warn;
                        p.Summary = "发送数据：" + commName + " → 未发出（" + Tail(sr.Message) + "，不判 NG）";
                        CaptureCommFrame(s, p);
                        return true;
                    }

                    p.Summary = "发送数据：" + Tail(sr.Message);
                    return false;
                }
            }

            // Text 输出端子承载实际发出的内容：下游脚本 / 结果输出按端子读取
            if (p.Metrics != null) p.Metrics.Text = content;

            CaptureCommFrame(s, p);
            p.Summary = "发送数据：" + commName + " ← " + ClipText(content, 80);
            return true;
        }

        /// <summary>
        /// Modbus 批量写：按 <c>名:地址:类型</c> 把上游数值一次写进多个寄存器。
        /// <para>【为什么需要】单寄存器模式只能写固定值，坐标 / 角度 / OK-NG 都进不了 PLC；
        /// 产线要的是"这一轮的 X/Y/角度/结论"按约定地址落到 PLC 寄存器里。
        /// 源名取脚本节点的命名输出（<c>api.Set("PosX", …)</c>），
        /// <c>OkNg</c> / <c>Passed</c> 是内置源：本帧到目前为止没有 NG 就写 1。</para>
        /// <para>中途某一项失败即停止并报出是哪一项，不吞错 —— 半套坐标比没有坐标更危险。</para>
        /// </summary>
        private bool RunModbusMap(FlowRunSession s, FlowNode node, FlowNodeProgress p, string commName,
                                  string map, bool autoConnect)
        {
            var items = new List<string[]>();
            foreach (string raw in map.Split(new[] { ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] cols = raw.Split(':');
                if (cols.Length < 2)
                {
                    p.Summary = "发送数据：批量写格式应为「名:地址:类型」，例如 PosX:30:Int32；当前解析不了：" + raw.Trim();
                    return false;
                }

                string name = cols[0].Trim();
                int addr;
                if (!int.TryParse(cols[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out addr)
                    || addr < 0 || addr > 65535)
                {
                    p.Summary = "发送数据：批量写里「" + name + "」的地址需为 0~65535，当前：" + cols[1].Trim();
                    return false;
                }

                string type = cols.Length > 2 && cols[2].Trim().Length > 0 ? cols[2].Trim() : "UInt16";
                if (!TryParseModbusType(type, out ModbusDataType _))
                {
                    p.Summary = "发送数据：批量写里「" + name + "」的类型「" + type
                              + "」不认识（可用 Int16 / UInt16 / Int32 / UInt32 / Float32 / Float64）";
                    return false;
                }

                items.Add(new[] { name, addr.ToString(CultureInfo.InvariantCulture), type });
            }

            if (items.Count == 0)
            {
                p.Summary = "发送数据：批量写没有任何有效项";
                return false;
            }

            if (autoConnect && !EnsureCommConnected(commName, "发送数据", p))
            {
                if (IsTransferUnavailable(p.Summary))
                {
                    p.Status = FlowNodeStatus.Warn;
                    p.Summary = p.Summary + "（本次未写入，不判 NG）";
                    CaptureCommFrame(s, p);
                    return true;
                }
                return false;
            }

            int shift = FlowAddressShift(s, node);
            var done = new List<string>();
            foreach (string[] it in items)
            {
                string name = it[0];
                int addr = int.Parse(it[1], CultureInfo.InvariantCulture) + shift;
                ModbusDataType mdt;
                TryParseModbusType(it[2], out mdt);

                string text;
                if (string.Equals(name, "OkNg", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, "Passed", StringComparison.OrdinalIgnoreCase))
                {
                    text = FramePassedSoFar(s) ? "1" : "0";
                }
                else
                {
                    double value;
                    if (!TryGetNamedNumber(s, name, out value))
                    {
                        p.Summary = "发送数据：上游没有名为「" + name + "」的数值输出"
                                  + "（脚本节点要先 Set(\"" + name + "\", 值)；当前已有："
                                  + DescribeNamedValueNames(s) + "）";
                        return false;
                    }
                    text = FormatForModbus(mdt, value);
                }

                OpResult wr = CommRegistry.ModbusWriteAs(commName, mdt, (ushort)addr, text,
                                                         ModbusWordOrder.ABCD);
                if (!wr.Success)
                {
                    if (IsTransferUnavailable(wr.Message))
                    {
                        p.Status = FlowNodeStatus.Warn;
                        p.Summary = "发送数据：" + commName + " → 未写入（" + Tail(wr.Message) + "，不判 NG）";
                        CaptureCommFrame(s, p);
                        return true;
                    }

                    p.Summary = "发送数据：" + name + "@" + addr + " 写入失败：" + Tail(wr.Message);
                    return false;
                }

                done.Add(name + "@" + addr + "=" + text);
            }

            if (p.Metrics != null) p.Metrics.Text = string.Join("、", done.ToArray());
            CaptureCommFrame(s, p);
            p.Summary = "发送数据：" + commName + " ← " + ClipText(string.Join("、", done.ToArray()), 120);
            return true;
        }

        /// <summary>帧内到目前为止没有 NG 节点即视为通过（发送节点通常排在链尾，能看到全部上游结论）。</summary>
        private static bool FramePassedSoFar(FlowRunSession s)
        {
            if (s == null || s.Outputs == null) return true;
            foreach (NodeOutput o in s.Outputs)
                if (o != null && o.Status == FlowNodeStatus.Ng) return false;
            return true;
        }

        private static bool TryGetNamedNumber(FlowRunSession s, string name, out double value)
        {
            value = 0;
            if (s == null || s.NamedValues == null || string.IsNullOrEmpty(name)) return false;

            object v;
            if (!s.NamedValues.TryGetValue(name, out v) || v == null) return false;

            try
            {
                value = Convert.ToDouble(v, CultureInfo.InvariantCulture);
                return !double.IsNaN(value) && !double.IsInfinity(value);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string DescribeNamedValueNames(FlowRunSession s)
        {
            if (s == null || s.NamedValues == null || s.NamedValues.Count == 0) return "无";
            var names = new List<string>();
            foreach (string k in s.NamedValues.Keys) names.Add(k);
            names.Sort(StringComparer.Ordinal);
            return string.Join("、", names.ToArray());
        }

        private static bool TryParseModbusType(string text, out ModbusDataType type)
        {
            type = ModbusDataType.UInt16;
            if (string.IsNullOrWhiteSpace(text)) return true;

            foreach (ModbusDataType t in ModbusData.AllTypes())
            {
                if (string.Equals(t.ToString(), text.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    type = t;
                    return true;
                }
            }
            return false;
        }

        /// <summary>按数据类型把数值格式化成 <see cref="ModbusTcpClient.WriteAs"/> 能解析的文本。</summary>
        private static string FormatForModbus(ModbusDataType type, double value)
        {
            if (type == ModbusDataType.Float32 || type == ModbusDataType.Float64)
                return value.ToString("0.######", CultureInfo.InvariantCulture);

            // 整数类型：四舍五入后写整数字面量（写成 512.0 会被 Encode 判为非法整数）
            double rounded = Math.Round(value, MidpointRounding.AwayFromZero);
            return ((long)rounded).ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>取上游最近一个非空文本输出（OCR / 脚本 / 接收数据都会写 Text）；没有返回空串。</summary>
        private static string FindUpstreamText(FlowRunSession s)
        {
            if (s == null || s.Outputs == null) return "";

            for (int i = s.Outputs.Count - 1; i >= 0; i--)
            {
                NodeOutput o = s.Outputs[i];
                if (o != null && !string.IsNullOrEmpty(o.Text)) return o.Text;
            }
            return "";
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

            // Text 输出端子承载收到的文本，Number 端子承载长度（端子表与实际输出一致）
            if (p.Metrics != null) p.Metrics.Text = content;
            p.Outputs.Add(new PortValue("Length", "长度", FlowPortKind.Number, content.Length));

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

        /// <summary>AutoConnect 时未连接就自动连一次（带失败冷却）；连接失败写摘要并返回 false（已连接直接 true）。</summary>
        private static bool EnsureCommConnected(string commName, string prefix, FlowNodeProgress p)
        {
            if (CommRegistry.IsConnected(commName)) return true;

            // 自动路径必须带冷却：流程每帧都可能走到这里，对掉线的 PLC
            // 不加冷却就是"每帧一次秒级连接"，节点耗时被连接超时吃掉。
            OpResult r = CommRegistry.ConnectAuto(commName);
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
        /// 判断一次通讯失败是否属于「链路 / 对端未就绪」——这类失败<strong>不判 NG</strong>，
        /// 只判 <see cref="FlowNodeStatus.Warn"/> 并在摘要里写清。
        /// <para>
        /// 【为什么】"输出信号"节点在 <c>MES 是 TCP 服务端但还没客户端接入</c>、
        /// <c>PLC 掉线（自动连接冷却中）</c>时必然发不出去，这是运行条件不足，不是检测结论；
        /// 判 NG 会让整帧 NG，现场看到的却是"检测明明过了却一直 NG"。
        /// 与「接收数据收不到数据判 Skipped」是同一口径：链路问题不冒充算法结论。
        /// </para>
        /// <para>真正的失败（端口占用、插件未装、寄存器地址非法、队列超时）仍判 NG。</para>
        /// </summary>
        private static bool IsTransferUnavailable(string message)
        {
            if (string.IsNullOrEmpty(message)) return false;

            return message.IndexOf("没有客户端", StringComparison.Ordinal) >= 0
                || message.IndexOf("冷却中", StringComparison.Ordinal) >= 0
                || message.IndexOf("未连接", StringComparison.Ordinal) >= 0
                || message.IndexOf("未启动", StringComparison.Ordinal) >= 0
                || message.IndexOf("正在连接中", StringComparison.Ordinal) >= 0
                || message.IndexOf("未处于运行状态", StringComparison.Ordinal) >= 0;
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
        /// <summary>
        /// 装载单文件字符库（<c>.ocrtpl</c>）。
        /// <para>
        /// 与 <see cref="EnsureOcrTemplates"/> 的关系：两者产出的 <see cref="OcrTemplateLoadResult"/>
        /// 完全同构（四态 / 失败清单 / Brief 都复用），上层无需分支；区别只在"从哪读样本"。
        /// </para>
        /// <para>
        /// 【库坏了不当场抛】读不出来时返回"0 个模板 + 失败清单"，让流程继续做字符分割 ——
        /// 现场一条库文件损坏不应该让整条产线停摆，但必须把原因带到结果摘要里。
        /// </para>
        /// </summary>
        private OpResult<OcrTemplateLoadResult> EnsureOcrLibrary(string path, string normKey)
        {
            string stamp;
            try
            {
                var fi = new FileInfo(path);
                stamp = fi.Exists ? (fi.Length.ToString(CultureInfo.InvariantCulture) + "|" +
                                     fi.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture))
                                  : "missing";
                stamp += "|" + (normKey ?? "");     // 归一化参数变了 → 视为换了源，必须重灌
            }
            catch (Exception ex)
            {
                return OpResult<OcrTemplateLoadResult>.Fail("无法读取字符库状态 " + path + "：" + ex.Message);
            }

            OpResult<int> cur = _algo.OcrTemplateCount();
            if (!cur.Success) return OpResult<OcrTemplateLoadResult>.Fail(cur.Message);

            bool sameSource = string.Equals(path, _ocrLibPath, StringComparison.OrdinalIgnoreCase)
                              && string.Equals(stamp, _ocrLibStamp, StringComparison.Ordinal);
            if (cur.Value > 0 && sameSource && _ocrLibSamples != null && _ocrLibResult != null)
            {
                _ocrLibResult.FromCache = true;
                return OpResult<OcrTemplateLoadResult>.Ok(_ocrLibResult);
            }

            if (cur.Value > 0)
            {
                OpResult clr = _algo.ClearOcrTemplates();
                if (!clr.Success) return OpResult<OcrTemplateLoadResult>.Fail(clr.Message);
            }

            List<OcrCharSample> samples;
            bool missing;
            string err;
            if (!OcrCharLibraryFile.Load(path, out samples, out missing, out err))
            {
                var broken = new OcrTemplateLoadResult
                {
                    Dir = path,
                    FileCount = missing ? 0 : 1,
                    ScannedAt = DateTime.Now
                };
                if (!missing)
                {
                    broken.Failures.Add(new OcrTemplateFailure
                    {
                        FileName = Path.GetFileName(path),
                        Reason = err
                    });
                }

                _ocrLibPath = path;
                _ocrLibStamp = stamp;
                _ocrLibSamples = null;
                _ocrLibResult = broken;
                return OpResult<OcrTemplateLoadResult>.Ok(broken);
            }

            var result = new OcrTemplateLoadResult
            {
                Dir = path,
                FileCount = samples.Count,
                ScannedAt = DateTime.Now
            };

            for (int i = 0; i < samples.Count; i++)
            {
                OcrCharSample s = samples[i];
                if (s == null) continue;

                using (Bitmap bmp = OcrCharLibraryFile.ToBitmap(s))
                {
                    if (bmp == null)
                    {
                        result.Failures.Add(new OcrTemplateFailure
                        {
                            FileName = s.Label,
                            Reason = "样本无法还原成图像"
                        });
                        continue;
                    }

                    OpResult r = _algo.AddOcrTemplate(bmp, s.Label);
                    if (!r.Success)
                    {
                        result.Failures.Add(new OcrTemplateFailure
                        {
                            FileName = s.Label,
                            Reason = "原生加载失败（" + r.Message + "）"
                        });
                        continue;
                    }
                    result.LoadedCount++;
                }
            }

            _ocrLibPath = path;
            _ocrLibStamp = stamp;
            _ocrLibSamples = samples;
            _ocrLibResult = result;
            result.FromCache = false;
            return OpResult<OcrTemplateLoadResult>.Ok(result);
        }

        /// <summary>OCR 字符库默认目录（相对程序目录；节点参数里存的就是这个名字）。</summary>
        private const string DefaultOcrDirName = "Models\\OCR";

        private OpResult<OcrTemplateLoadResult> EnsureOcrTemplates(string dir, string normKey)
        {
            if (string.IsNullOrWhiteSpace(dir)) dir = DefaultOcrDirName;

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
            // 归一化参数也进指纹：模板是按这些参数二值化/归一化后进库的，改一个就得重灌
            string fingerprint = OcrTemplateFingerprint(dir, files) + "|" + (normKey ?? "");

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

            // ---- 基准：自动填入 + 落盘（VisionMaster/HALCON 语义）----
            //   · 从没示教过（RefSet=0 且三个基准值都是 0）→ 用当前位姿示教，写回 RefX/RefY/RefAngle；
            //   · 示教过（RefSet=1）或用户手填过（任一非 0）→ 直接用这几个值比较。
            // 这样"点一次跑一次"也能算出真实偏差，而且参数面板上能看到基准到底是多少 ——
            // 之前把基准藏在 AutoRef* 三个隐藏字段里，用户既看不见也就没法确认。
            bool refSet = node.GetBool("RefSet", false);
            double refXParam = node.GetDouble("RefX", 0);
            double refYParam = node.GetDouble("RefY", 0);
            double refAParam = node.GetDouble("RefAngle", 0);
            bool manualLooksSet = Math.Abs(refXParam) > 1e-9
                                  || Math.Abs(refYParam) > 1e-9
                                  || Math.Abs(refAParam) > 1e-9;

            double rx, ry, ra;
            if (refSet || manualLooksSet)
            {
                // 基准按【原始图坐标】存，换算到工作帧坐标
                rx = refXParam - s.OffsetX;
                ry = refYParam - s.OffsetY;
                ra = refAParam;
            }
            else if (!hasPose)
            {
                // 既没示教过、上游也没位姿：跳过修正（不判 NG），并说清原因。
                // （校验期由 FlowValidator 的 W005 提前提示，运行期不拦。）
                p.Status = FlowNodeStatus.Skipped;

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
            else
            {
                // 首次示教：把当前位姿写回节点（Auto 与"Manual 但值为 0"都走这里，
                // 避免用户手填 0 得到"偏移量 = 绝对坐标"这种荒谬结果）。
                rx = curX;
                ry = curY;
                ra = curA;

                node.Set("RefX", (rx + s.OffsetX).ToString("F3", CultureInfo.InvariantCulture));
                node.Set("RefY", (ry + s.OffsetY).ToString("F3", CultureInfo.InvariantCulture));
                node.Set("RefAngle", ra.ToString("F3", CultureInfo.InvariantCulture));
                node.Set("RefSet", "1");

                _autoRefs[node.Id] = new[] { rx, ry, ra };
                autoFirstLatch = true;
                _baselineTaught = true;
                _baselineNodeId = node.Id;

                // 会话级也记一笔：单步执行没有链尾快照，宿主只能从节点快照里看到这件事
                if (s.BaselineTaughtNodes != null) s.BaselineTaughtNodes.Add(node.Id);
            }

            double dx = hasPose ? (curX - rx) : 0.0;
            double dy = hasPose ? (curY - ry) : 0.0;
            double dAngle = hasPose ? (curA - ra) : 0.0;
            double dScale = hasPose ? curS : 1.0;

            // EnableAngle = 0：只平移不旋转
            if (!node.GetBool("EnableAngle", true)) dAngle = 0.0;

            // 偏移量"大到不可能"时给出明确提示：最常见的原因是基准填成了 0（或填了别的坐标），
            // 于是 dX 直接等于绝对坐标 —— 下游 ROI 会被推到画面外，看起来像"显示错了"。
            bool shiftSuspicious = s.Current != null
                && (Math.Abs(dx) > s.Current.Width * 0.5 || Math.Abs(dy) > s.Current.Height * 0.5);

            // 覆盖式写入（链内多个位置修正节点以最后一个为准，不累加）
            s.Transform.Set(rx, ry, ra, dx, dy, dAngle, dScale);

            // 【必须写】位置修正原来只往 Summary 里塞一句话，Metrics/Outputs 全是空的：
            // 结果① 标定转换的参数面板拿不到坐标（一直显示 0,0 或不更新）；
            // 结果② 下游「结果输出」「发送数据」也引用不到修正位姿。
            // 这里把修正后的实际位置与偏移量都作为正式输出交出去。
            p.Metrics.TargetCount = 1;
            p.Metrics.TopCx = curX;
            p.Metrics.TopCy = curY;
            p.Metrics.AngleDeg = curA;

            p.Outputs.Add(new PortValue("X", "X", FlowPortKind.Number, curX));
            p.Outputs.Add(new PortValue("Y", "Y", FlowPortKind.Number, curY));
            p.Outputs.Add(new PortValue("R", "R", FlowPortKind.Number, curA));
            p.Outputs.Add(new PortValue("dX", "偏移X", FlowPortKind.Number, dx));
            p.Outputs.Add(new PortValue("dY", "偏移Y", FlowPortKind.Number, dy));
            p.Outputs.Add(new PortValue("dAngle", "角度偏差", FlowPortKind.Angle, dAngle));
            p.Outputs.Add(new PortValue("RefX", "基准X", FlowPortKind.Number, rx));
            p.Outputs.Add(new PortValue("RefY", "基准Y", FlowPortKind.Number, ry));

            // 可视化：**两个**坐标系 + 一条偏差箭头
            //   淡青 = 基准（示教时的位置）
            //   亮橙 = 实测（本帧工件的位置）
            // 只画基准的话，用户看到的永远是一个固定十字，会以为"没有显示更新后的点"。
            foreach (IGeometryShape g in FlowShapes.CoordinateSystem(
                         rx, ry, ra, FlowShapes.ColorPoseRef, FlowShapes.ColorPoseRef,
                         FlowShapes.NormalLine, FlowShapes.DefaultCrossArm * 1.2))
            {
                g.Name = FlowShapes.AuxContourName;   // 坐标系属"参考几何"，随「轮廓」开关显隐
                p.AuxShapes.Add(g);
            }

            if (hasPose)
            {
                foreach (IGeometryShape g in FlowShapes.CoordinateSystem(
                             curX, curY, curA, FlowShapes.ColorPoseNow, FlowShapes.ColorPoseNow,
                             FlowShapes.BoldLine, FlowShapes.DefaultCrossArm * 1.5))
                {
                    g.Name = FlowShapes.AuxContourName;
                    p.AuxShapes.Add(g);
                }

                // 偏差箭头：基准 → 实测（虚线，两端各一个小三角由坐标系箭头代劳）
                if (Math.Abs(dx) > 0.5 || Math.Abs(dy) > 0.5)
                {
                    LineSegmentShape arrow = FlowShapes.Line(rx, ry, curX, curY,
                                                             FlowShapes.ColorPoseNow, 2f, true);
                    arrow.Name = FlowShapes.AuxContourName;
                    p.AuxShapes.Add(arrow);

                    // 偏差数值贴在箭头中点（1×1 的标记点 + 标签；LineSegment 本身没有 Label）
                    var tag = new RectangleShape((float)((rx + curX) / 2.0), (float)((ry + curY) / 2.0), 1f, 1f);
                    tag.StrokeColor = FlowShapes.ColorPoseNow;
                    tag.LineWidth = 1f;
                    tag.IsEditable = false;
                    tag.Name = FlowShapes.AuxContourName;
                    tag.Label = string.Format(CultureInfo.InvariantCulture, "dX={0:F1} dY={1:F1}", dx, dy);
                    p.AuxShapes.Add(tag);
                }
            }

            p.Summary = string.Format(CultureInfo.InvariantCulture,
                "位置修正：基准({0:F1},{1:F1},{2:F1}°) 实测({3:F1},{4:F1},{5:F1}°) dX={6:F1} dY={7:F1} dA={8:F2}°"
                + "　· 下游 ROI 按此差值偏移",
                rx, ry, ra, curX, curY, curA, dx, dy, dAngle);
            if (autoFirstLatch)
                p.Summary += "（Auto 首帧示教：基准=当前位姿，偏差 0 属预期）";

            if (shiftSuspicious)
            {
                p.Status = FlowNodeStatus.Warn;
                p.Summary += string.Format(CultureInfo.InvariantCulture,
                    "　⚠ 偏移异常大（dX={0:F1} dY={1:F1}），请检查「基准」是否填错（0 会被当作未示教自动示教）",
                    dx, dy);
            }

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

            // 【默认路径：只算差值，不碰像素】
            // 下游每个节点的 ROI 通过 s.Transform.ApplyToRect/ApplyToPoint 偏移，
            // 画面上看到的就是"框跟着工件跑"——这正是用户要的语义，也没有重采样损失。
            // 只有显式选择「整图对齐」时才把偏差烘进像素（Warp）：那条路会让图动、框不动，
            // 用户很容易误判成"位置修正没生效"。
            string follow = node.GetString("Follow", "Roi");
            bool warpWhole = string.Equals(follow, "Warp", StringComparison.OrdinalIgnoreCase);

            bool needWarp = warpWhole
                            && (Math.Abs(dx) > 0.05 || Math.Abs(dy) > 0.05 || Math.Abs(dAngleNorm) > 0.05);
            if (needWarp && s.Current != null)
            {
                Bitmap warped = WarpToReference(s.Current, curX, curY, rx, ry, dAngleNorm);
                s.Current.Dispose();
                s.Current = warped;

                // 修正已烘进像素：复位坐标重映射，下游 ROI 不做二次变换；
                // 但要留下"本帧已对齐"的标记，下游摘要才能说清 ROI 到底改没改。
                s.Transform.MarkWarped();

                // 预览帧刷新为修正后的图像。连续运行（CaptureFrames=false）下跳过克隆，避免每周期 LOH 压力导致卡顿
                if (s.CaptureFrames && s.Current != null)
                {
                    if (p.Frame != null) p.Frame.Dispose();
                    try { p.Frame = (Bitmap)s.Current.Clone(); }
                    catch (Exception) { p.Frame = null; }
                }

                p.Summary = p.Summary.Replace("　· 下游 ROI 按此差值偏移", "")
                            + "（整图已对齐回基准姿态：下游 ROI 保持原坐标）";
            }

            return true;
        }

        /// <summary>
        /// 给"用 ROI 的节点"生成位置修正状态说明。
        /// <para>
        /// 【为什么还要查 Pose 连线】Pose 输入端子目前<strong>只表达依赖关系，不参与坐标计算</strong>
        /// （坐标修正由链级『位置修正』节点统一给出）。若用户连了 Pose 却没放位置修正节点，
        /// 就会觉得"定位位姿没反应" —— 这里把原因直接写进摘要，而不是让他去猜。
        /// </para>
        /// </summary>
        private static string PoseInputNote(FlowRunSession s, FlowNode node)
        {
            if (s.Transform.Warped || s.Transform.IsValid) return s.Transform.PoseNote();

            return FindPoseSourceNode(s, node) != null
                ? "　· 已接入定位位姿，但链上没有『位置修正』节点 —— 本次 ROI 不偏移"
                : "";
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
                Greediness = node.GetDouble("Greediness", 0.9),
                Symmetric = node.GetBool("Symmetric", false)
            };

            // 搜索区域统一走 SearchRegion（Roi*/Search* 两种历史命名都认）
            SearchRegion region = SearchRegion.FromNode(node, s.OffsetX, s.OffsetY, true);   // 历史命名：Search*
            Rectangle search = region.BoundsToFrame(s.Transform, s.Current.Width, s.Current.Height);
            AddRegionOutline(p, s, region, s.Current.Width, s.Current.Height, false, "形状匹配区域");

            // 诊断：姿态数 = 角度档 × 缩放档。用户抱怨"慢"时，第一个要看的数就是它 ——
            // ±180°/步长 1° 就是 361 个姿态，比 ±10°/步长 1° 慢近 20 倍。
            int angleCount = CountSteps(param.AngleStart, param.AngleEnd, param.AngleStep);
            int scaleCount = (param.ScaleStep > 1e-9)
                ? CountSteps(param.ScaleMin, param.ScaleMax, param.ScaleStep) : 1;
            int poseCount = angleCount * scaleCount;

            // 诊断里要区分"搜全图"与"搜 ROI"：W/H 未设置就是全图
            bool searchAll = node.GetInt("SearchW", 0) <= 0 || node.GetInt("SearchH", 0) <= 0;
            Rectangle searchRect = searchAll ? Rectangle.Empty : search;

            var fr = _algo.FindShapeModel(s.Current, param, search);
            if (!fr.Success)
            {
                p.Summary = Fail("形状匹配失败：", fr);
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
                // 0 命中时补一次"不设阈值"的匹配，把**最高分**捞出来：
                // "差多少没到阈值"是调参唯一的依据，只报"未找到目标"等于让人闭眼拧。
                // 只在失败路径多花一次匹配时间，正常路径零成本。
                double best = double.NaN;
                if (param.MinScore > 0.0)
                {
                    ShapeParam probe = new ShapeParam
                    {
                        AngleStart = param.AngleStart,
                        AngleEnd = param.AngleEnd,
                        AngleStep = param.AngleStep,
                        ScaleMin = param.ScaleMin,
                        ScaleMax = param.ScaleMax,
                        ScaleStep = param.ScaleStep,
                        PyramidLevels = param.PyramidLevels,
                        MinScore = 0.0,
                        MaxMatches = 1,
                        MaxOverlap = param.MaxOverlap,
                        Subpixel = param.Subpixel,
                        Greediness = param.Greediness
                    };
                    OpResult<List<ShapeMatchResult>> diag = _algo.FindShapeModel(s.Current, probe, search);
                    if (diag.Success && diag.Value != null && diag.Value.Count > 0)
                    {
                        best = diag.Value[0].Score;
                        p.Metrics.TopScore = best;
                        p.Metrics.TopCx = diag.Value[0].Cx;
                        p.Metrics.TopCy = diag.Value[0].Cy;
                    }
                }

                p.Summary = double.IsNaN(best)
                    ? "形状匹配：未找到目标" + RetrainSuffix(retrained, retrainMs)
                    : string.Format(CultureInfo.InvariantCulture,
                        "形状匹配：未找到目标（最高得分 {0:F3} < 阈值 {1:F2}，已搜 {2} 姿态、金字塔 {3} 层）"
                        + "；可降阈值 / 放宽角度范围 / 检查模板 ROI 是否框到了工件",
                        best, param.MinScore, param.PoseCount, param.PyramidLevels)
                      + RetrainSuffix(retrained, retrainMs);
                return false;
            }

            // 记录实时位姿，供下游位置修正节点取基准
            s.SetPose(matches[0].Cx, matches[0].Cy, matches[0].Angle, matches[0].Scale);
            s.SetNodePose(node.Id, matches[0].Cx, matches[0].Cy, matches[0].Angle, matches[0].Scale);

            // 摘要里带重训耗时：多形状匹配节点共用原生单槽位时每帧会重训 N 次，
            // 现场只能靠这一项把"掉帧"归因到"重训"而不是"相机慢"。
            p.Summary = string.Format(CultureInfo.InvariantCulture,
                "形状匹配：{0} 个，最高 {1:F3}",
                matches.Count, matches[0].Score)
                + ShapeDiagnostics(poseCount, param, searchRect, cache)
                + RetrainSuffix(retrained, retrainMs);

            // 模板边缘点太少 → 定位必然抖：这不是"算法不准"，是模板本身没信息量
            int features = cache != null && cache.Meta != null ? cache.Meta.FeatureCount : 0;
            if (features > 0 && features < LowFeatureThreshold)
            {
                p.Status = FlowNodeStatus.Warn;
                p.Summary += string.Format(CultureInfo.InvariantCulture,
                    "　模板边缘点仅 {0} 个（建议 ≥{1}）：请扩大模板 ROI 或降低「对比度」重新训练，否则分数会抖",
                    features, LowFeatureThreshold);
            }

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

            // 匹配参数（角度 / 缩放 / 金字塔 / 多目标）。默认只搜平移 —— 与旧版行为一致。
            var param = new MatchParam
            {
                AngleStart = node.GetDouble("AngleStart", 0),
                AngleEnd = node.GetDouble("AngleEnd", 0),
                AngleStep = node.GetDouble("AngleStep", 1),
                ScaleMin = node.GetDouble("ScaleMin", 1),
                ScaleMax = node.GetDouble("ScaleMax", 1),
                ScaleStep = node.GetDouble("ScaleStep", 0),
                PyramidLevels = node.GetInt("PyramidLevels", 3),
                MinScore = node.GetDouble("MinScore", 0.8),
                MaxMatches = node.GetInt("MaxMatches", 1),
                MaxOverlap = node.GetDouble("MaxOverlap", 0.5),
                Subpixel = node.GetBool("Subpixel", true)
            };
            double minScore = param.MinScore;

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
            // 搜索区域统一走 SearchRegion（Roi*/Search* 两种历史命名都认）
            SearchRegion region = SearchRegion.FromNode(node, s.OffsetX, s.OffsetY, true);   // 历史命名：Search*
            Rectangle search = region.BoundsToFrame(s.Transform, s.Current.Width, s.Current.Height);
            AddRegionOutline(p, s, region, s.Current.Width, s.Current.Height, false, "模板匹配区域");
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

                OpResult<List<MatchResult>> mr = _algo.MatchTemplate(probe, param);
                if (!mr.Success)
                {
                    p.Summary = Fail("模板匹配失败：", mr);
                    return false;
                }

                List<MatchResult> all = mr.Value ?? new List<MatchResult>();
                MatchResult r = all.Count > 0 ? all[0] : null;
                double score = r != null ? r.Score : 0.0;

                // 阈值在 C# 侧判：未达标时仍要把"最高得分"报出来，这是调阈值唯一的依据
                var hits = new List<MatchResult>();
                for (int i = 0; i < all.Count; i++)
                {
                    if (all[i].Score >= minScore) hits.Add(all[i]);
                }

                p.Metrics.NccScore = score;
                p.Metrics.TopScore = score;
                p.Metrics.AngleDeg = r != null ? r.Angle : 0.0;
                // 缩放只在真的搜了缩放（≠1）时才写，避免结果区恒定挂一个 "1.000×"
                p.Metrics.ScaleFactor = (r != null && Math.Abs(r.Scale - 1.0) > 1e-6) ? r.Scale : double.NaN;

                if (hits.Count == 0)
                {
                    p.Metrics.TargetCount = 0;
                    p.Summary = string.Format(CultureInfo.InvariantCulture,
                        "模板匹配：未找到目标，最高得分 {0:F3}", score) + RetrainSuffix(retrained, retrainMs);
                    return false;
                }

                // 裁剪搜索时把结果坐标加回搜索区左上角
                double offX = useRoi ? search.X : 0.0;
                double offY = useRoi ? search.Y : 0.0;

                // 多目标：叠加层最多画 20 个，避免 MaxMatches 拉到 1000 时把画面糊死
                int drawCount = Math.Min(hits.Count, 20);
                for (int i = 0; i < drawCount; i++)
                {
                    MatchResult m = hits[i];
                    // 原生 Width/Height 是旋转后的轴对齐外接框，不能直接当旋转矩形宽高，
                    // 否则大角度下框会被放大（45° ≈1.41×）、90° 时横竖互换 —— 看起来像
                    // "scale=1 还在搜缩放"。旋转框应使用训练模板原尺寸 × 匹配缩放。
                    double tplW = (cache.Template != null && cache.Template.Width > 0)
                        ? cache.Template.Width : m.Width;
                    double tplH = (cache.Template != null && cache.Template.Height > 0)
                        ? cache.Template.Height : m.Height;
                    SizeF box = FlowShapes.NccRotatedBoxSize(tplW, tplH, m.Scale);
                    p.Shapes.Add(FlowShapes.NccMatchBox(
                        m.Cx + offX, m.Cy + offY, box.Width, box.Height, m.Angle, i == 0,
                        string.Format(CultureInfo.InvariantCulture, "#{0} {1:0.00}", i + 1, m.Score)));
                }

                // 主判据：目标数 + 得分 + 角度 +（搜了缩放时的）缩放
                p.Metrics.TargetCount = hits.Count;
                p.Metrics.TopCx = r.Cx + offX;
                p.Metrics.TopCy = r.Cy + offY;

                // 记录实时位姿（角度/缩放现在都来自匹配结果，下游 ROI 可据此旋转）
                s.SetPose(r.Cx + offX, r.Cy + offY, r.Angle, r.Scale);
                s.SetNodePose(node.Id, r.Cx + offX, r.Cy + offY, r.Angle, r.Scale);

                string extra = string.Format(CultureInfo.InvariantCulture, " · 角度 {0:F1}°", r.Angle);
                if (Math.Abs(r.Scale - 1.0) > 1e-6)
                    extra += string.Format(CultureInfo.InvariantCulture, " · 缩放 {0:F3}×", r.Scale);

                p.Summary = string.Format(CultureInfo.InvariantCulture,
                    "模板匹配：{0} 个 · 最高 {1:F3} @({2:F0},{3:F0}){4}",
                    hits.Count, score, r.Cx + offX, r.Cy + offY, extra) + RetrainSuffix(retrained, retrainMs);
                return true;
            }
            finally
            {
                if (cropped != null) cropped.Dispose();
            }
        }

        // ---------------------- 条码 / 二维码（zxing-cpp）----------------------

        /// <summary>
        /// 条码 / 二维码识别：免模板，一次可识别多个码。
        /// <para>
        /// 【为什么没识别到要单独写一段文案】barcode 失败最常见的原因不是"算法不行"，而是
        /// 参数把自己坑了（ROI 贴着码边、开了降采样、码制限死、勾了纯码模式）。
        /// 只说"未找到目标"会让人去怀疑算法，所以这里按实际参数逐条点出可疑项。
        /// </para>
        /// </summary>
        private bool RunBarcode(FlowRunSession s, FlowNode node, FlowNodeProgress p)
        {
            if (s.Current == null)
            {
                p.Status = FlowNodeStatus.Skipped;
                p.Summary = "条码/二维码：无输入帧";
                return true;
            }

            if (s.CaptureFrames && s.Current != null)
            {
                if (p.Frame != null) p.Frame.Dispose();
                try { p.Frame = (Bitmap)s.Current.Clone(); }
                catch (Exception) { p.Frame = null; }
            }

            // 码类型：单选（一种码一个节点）。选具体类型既快，也不会把近亲码认错。
            int codeType = node.GetInt("CodeType", BarcodeCodeType.Auto);
            if (codeType < BarcodeCodeType.Auto || codeType > BarcodeCodeType.MaxType)
                codeType = BarcodeCodeType.Auto;

            var param = new BarcodeParam
            {
                CodeType = codeType,
                MaxCount = Math.Max(1, Math.Min(255, node.GetInt("MaxCount", 8))),
                TryHarder = node.GetBool("TryHarder", false),
                TryRotate = node.GetBool("TryRotate", true),
                TryDownscale = node.GetBool("TryDownscale", false),
                TryInvert = node.GetBool("TryInvert", true),
                IsPure = node.GetBool("IsPure", false)
            };
            int minCount = Math.Max(1, node.GetInt("MinCount", 1));

            // 搜索区域统一走 SearchRegion（解码器自己会处理旋转，这里保持外接框即可）
            SearchRegion region = SearchRegion.FromNode(node, s.OffsetX, s.OffsetY);
            Rectangle roi = region.BoundsToFrame(s.Transform, s.Current.Width, s.Current.Height);
            AddRegionOutline(p, s, region, s.Current.Width, s.Current.Height, false, "条码区域");
            bool useRoi = roi.Width > 0 && roi.Height > 0
                          && (roi.X != 0 || roi.Y != 0
                              || roi.Width != s.Current.Width || roi.Height != s.Current.Height);

            Bitmap probe = s.Current;
            Bitmap cropped = null;
            try
            {
                if (useRoi)
                {
                    Rectangle real = Rectangle.Intersect(roi, new Rectangle(0, 0, s.Current.Width, s.Current.Height));
                    if (real.Width > 0 && real.Height > 0)
                    {
                        cropped = s.Current.Clone(real, s.Current.PixelFormat);
                        probe = cropped;
                        roi = real;
                    }
                    else
                    {
                        useRoi = false;
                    }
                }

                OpResult<List<BarcodeResult>> rr = _algo.ReadBarcodes(probe, param);
                if (!rr.Success)
                {
                    p.Status = FlowNodeStatus.Ng;
                    p.Summary = Fail("条码/二维码：", rr);
                    return false;
                }

                List<BarcodeResult> all = rr.Value ?? new List<BarcodeResult>();
                double offX = useRoi ? roi.X : 0.0;
                double offY = useRoi ? roi.Y : 0.0;

                int drawCount = Math.Min(all.Count, 20);
                for (int i = 0; i < drawCount; i++)
                {
                    BarcodeResult b = all[i];
                    p.Shapes.Add(FlowShapes.BarcodeBox(
                        b.Cx + offX, b.Cy + offY, b.Width, b.Height, b.Angle, i == 0,
                        string.Format(CultureInfo.InvariantCulture, "{0} {1}", b.Format, Shorten(b.Text, 14))));
                }

                p.Metrics.TargetCount = all.Count;

                if (all.Count == 0)
                {
                    p.Status = FlowNodeStatus.Ng;
                    p.Summary = "条码/二维码：" + BarcodeMissReason(param, probe.Width, probe.Height, useRoi);
                    return false;
                }

                BarcodeResult first = all[0];
                p.Metrics.TopCx = first.Cx + offX;
                p.Metrics.TopCy = first.Cy + offY;
                p.Metrics.Text = first.Text;
                p.Metrics.AngleDeg = first.Angle;

                // 记进执行期位姿：下游 ROI / 标定转换可直接用码中心与码的旋转角
                s.SetPose(first.Cx + offX, first.Cy + offY, first.Angle, 1.0);
                s.SetNodePose(node.Id, first.Cx + offX, first.Cy + offY, first.Angle, 1.0);

                string head = all.Count == 1
                    ? string.Format(CultureInfo.InvariantCulture, "{0} \"{1}\"", first.Format, Shorten(first.Text, 24))
                    : string.Format(CultureInfo.InvariantCulture, "{0} 个（首个 {1} \"{2}\"）",
                                    all.Count, first.Format, Shorten(first.Text, 18));

                if (all.Count < minCount)
                {
                    p.Status = FlowNodeStatus.Ng;
                    p.Summary = string.Format(CultureInfo.InvariantCulture,
                        "条码/二维码：{0} · 少于要求 {1} 个", head, minCount);
                    return false;
                }

                p.Summary = "条码/二维码：" + head;
                return true;
            }
            finally
            {
                if (cropped != null) cropped.Dispose();
            }
        }

        /// <summary>
        /// 没识别到码时的**可行动诊断**：按当前参数逐条点出最可能的坑，而不是只说"未找到"。
        /// 依据都来自本次实际生效的参数，所以不会给出与现场无关的建议。
        /// </summary>
        private static string BarcodeMissReason(BarcodeParam p, int w, int h, bool useRoi)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("未识别到码");
            sb.Append("（码类型：").Append(BarcodeTypeName(p.CodeType)).Append('）');
            sb.Append("；区域 ").Append(w).Append('x').Append(h);
            if (!useRoi) sb.Append("（全图）");

            var tips = new List<string>();
            if (p.CodeType != BarcodeCodeType.Auto)
                tips.Add("确认码类型选对了（选错一个也读不出）");
            if (p.TryDownscale) tips.Add("关掉「降采样加速」（会让小码直接消失）");
            if (p.IsPure) tips.Add("关掉「纯码模式」（它要求只有 1 个正对镜头的码）");
            if (!p.TryHarder) tips.Add("勾上「更努力」");
            if (!p.TryRotate) tips.Add("勾上「多方向」");
            if (w < 40 || h < 40) tips.Add("ROI 太小，放宽到码外留 10~20 像素");
            tips.Add("一维码两侧要留静区（左右别贴着码条）");

            sb.Append("；建议：");
            for (int i = 0; i < tips.Count; i++)
            {
                if (i > 0) sb.Append(" / ");
                sb.Append(tips[i]);
            }
            return sb.ToString();
        }

        /// <summary>码类型名字（只用于运行期诊断文案；界面上的选项文字在参数表里）。</summary>
        private static string BarcodeTypeName(int t)
        {
            switch (t)
            {
                case BarcodeCodeType.Auto: return "自动（不限）";
                case BarcodeCodeType.Ean13: return "EAN-13";
                case BarcodeCodeType.UpcA: return "UPC-A";
                case BarcodeCodeType.Codabar: return "Codabar";
                case BarcodeCodeType.Code39: return "CODE 39";
                case BarcodeCodeType.Code128: return "CODE 128";
                case BarcodeCodeType.Itf: return "ITF";
                case BarcodeCodeType.QrCode: return "QR Code";
                case BarcodeCodeType.DataMatrix: return "Data Matrix";
                case BarcodeCodeType.Aztec: return "Aztec";
                case BarcodeCodeType.Pdf417: return "PDF 417";
                case BarcodeCodeType.MaxiCode: return "MaxiCode";
                case BarcodeCodeType.Ean8: return "EAN-8";
                case BarcodeCodeType.UpcE: return "UPC-E";
                case BarcodeCodeType.Code93: return "CODE 93";
                case BarcodeCodeType.MicroQrCode: return "Micro QR Code";
                case BarcodeCodeType.RmQrCode: return "RMQR Code";
                case BarcodeCodeType.DataBar: return "GS1 DataBar";
                case BarcodeCodeType.DataBarExpanded: return "GS1 DataBar Expanded";
                case BarcodeCodeType.DataBarLimited: return "GS1 DataBar Limited";
                case BarcodeCodeType.DxFilmEdge: return "DX Film Edge";
                default: return "第 " + t + " 项（未知）";
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

            // 显示与修正开关（v1.0.13）
            bool displayMask      = node.GetBool("DisplayMask", true);
            bool displayMinRect   = node.GetBool("DisplayMinRect", true);
            bool showFeatureLabel = node.GetBool("ShowFeatureLabel", false);
            bool applyCorrection  = node.GetBool("ApplyCorrection", true);

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

                // 可选搜索区域：限制分析区域。统一走 SearchRegion ——
                // 参数读取（Roi*/Search* 兼容）、位置修正变换、旋正裁剪、边界夹取只有一处实现。
                // 区域无效（宽高 ≤ 0 或完全在图外）退化为整帧分析，与改造前一致。
                Bitmap analyzeOn = input;
                Rectangle usedRoi = Rectangle.Empty;
                bool blobAffine = false;

                {
                    SearchRegion region = SearchRegion.FromNode(node, s.OffsetX, s.OffsetY);
                    if (!region.IsEmpty)
                    {
                        cropped = region.Crop(input, applyCorrection ? s.Transform : null,
                                              out usedRoi, out blobAffine);
                        if (cropped != null) analyzeOn = cropped;
                    }
                }

                // 先多取一些，本地按宽高 / 圆度过滤后再截断到 MaxCount
                int cap = maxCount > 0 ? Math.Min(maxCount * 4 + 8, 256) : 256;

                int[] blobLabels = null;
                var br = displayMask
                    ? _algo.AnalyzeBlobsEx(analyzeOn, minArea, maxArea, cap, out blobLabels)
                    : _algo.AnalyzeBlobs(analyzeOn, minArea, maxArea, cap);
                if (!br.Success)
                {
                    p.Summary = Fail("Blob 失败：", br);
                    return false;
                }

                // Mask 叠加：标签图对应「分析图」（可能被 ROI 裁过），按 ROI 左上角贴回整帧
                if (blobLabels != null)
                {
                    int ox = usedRoi.IsEmpty ? 0 : usedRoi.X;
                    int oy = usedRoi.IsEmpty ? 0 : usedRoi.Y;
                    p.Overlay = NodeOverlayFactory.BlobMask(
                        blobLabels, analyzeOn.Width, analyzeOn.Height,
                        input.Width, input.Height, ox, oy);
                }

                int allCount = br.Value != null ? br.Value.Count : 0;
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
                var blobShapeIndex = new List<int>(kept.Count);   // 每个目标对应的 BlobRect 下标（结果表选中高亮用）
                for (int i = 0; i < kept.Count; i++)
                {
                    BlobResult b = kept[i];
                    PointF bc = MapRoiPoint(s, usedRoi, 0.0, blobAffine, b.Cx, b.Cy);
                    double cx = bc.X;
                    double cy = bc.Y;
                    string label = showFeatureLabel
                        ? string.Format(CultureInfo.InvariantCulture,
                            "#{0} {1:0}px² · 圆{2:0.00} 矩{3:0.00} 比{4:0.00}",
                            i + 1, b.Area, b.Roundness, b.Rectangularity, b.AspectRatio)
                        : string.Format(CultureInfo.InvariantCulture,
                            "#{0} {1:0}px² · {2:0.00}", i + 1, b.Area, Circularity(b));
                    p.Shapes.Add(FlowShapes.BlobRect(cx, cy, b.Width, b.Height, label));
                    blobShapeIndex.Add(p.Shapes.Count - 1);

                    // 最小外接矩形（带角度）：轴对齐框看不出斜放工件的真实姿态，
                    // 琥珀色与青色 Blob 框区分开，勾选才画。
                    if (displayMinRect && b.MinRectWidth > 0 && b.MinRectHeight > 0)
                    {
                        PointF mc = MapRoiPoint(s, usedRoi, 0.0, blobAffine, b.MinRectCx, b.MinRectCy);
                        var minRect = new RotatedRectShape(mc,
                                                           (float)b.MinRectWidth,
                                                           (float)b.MinRectHeight,
                                                           (float)b.Angle);
                        minRect.StrokeColor = Color.FromArgb(255, 214, 92);
                        minRect.LineWidth = 1.2f;
                        p.Shapes.Add(minRect);
                    }
                }

                // 主判据：数量 + 最大面积（及该目标的圆度）。必须在任何 return false 之前填好，
                // 否则"期望个数不符"时结果区只剩一句文案、看不到实际数了几个。
                p.Metrics.TargetCount = kept.Count;
                if (kept.Count > 0)
                {
                    p.Metrics.MaxArea = kept[0].Area;
                    p.Metrics.MaxCircularity = Circularity(kept[0]);
                }

                // 逐个区域上报：中心 / 面积 / 宽 / 高 / 角度 / 圆度
                // （只看"最大面积"不够 —— 分级判定、按序号取坐标都要用到每一个）
                for (int i = 0; i < kept.Count; i++)
                {
                    BlobResult b2 = kept[i];
                    PointF bc2 = MapRoiPoint(s, usedRoi, 0.0, blobAffine, b2.Cx, b2.Cy);
                    p.Regions.Add(new RegionInfo
                    {
                        Index = i + 1,
                        Cx = bc2.X,
                        Cy = bc2.Y,
                        Area = b2.Area,
                        Width = b2.Width,
                        Height = b2.Height,
                        Angle = b2.Angle,
                        Circularity = b2.Roundness,
                        Perimeter = b2.Perimeter,
                        Rectangularity = b2.Rectangularity,
                        Solidity = b2.Solidity,
                        AspectRatio = b2.AspectRatio,
                        FillRate = b2.FillRate,
                        MinRectWidth = b2.MinRectWidth,
                        MinRectHeight = b2.MinRectHeight,
                        ShapeIndex = i < blobShapeIndex.Count ? blobShapeIndex[i] : -1
                    });
                }

                // 被筛选掉的个数（面积/宽高/圆度不达标）也上报，便于调参
                p.Outputs.Add(new PortValue("RejectedCount", "剔除数", FlowPortKind.Number,
                    (double)Math.Max(0, allCount - kept.Count)));

                // ROI 轮廓是「输入几何」而不是「检测目标」：走 AuxShapes，
                // 否则结果区会多出 4 条"边线"行，与"12 个 Blob"永久对不上（UI-R05）。
                if (!usedRoi.IsEmpty)
                {
                    if (blobAffine)
                    {
                        // 与 OCR 同一口径（RoiAffineMap）：区域轮廓必须连同角度一起画，
                        // 画成轴对齐外接框会让人以为"ROI 没跟着转"。
                        PointF rc = RoiAffineMap.FrameCenter(s.Transform, usedRoi);
                        var roiRot = new RotatedRectShape(rc, usedRoi.Width, usedRoi.Height,
                                                          (float)RoiAffineMap.EffectiveAngle(s.Transform, 0.0));
                        roiRot.StrokeColor = FlowShapes.ColorRoi;
                        roiRot.LineWidth = 1.5f;
                        roiRot.IsEditable = false;
                        roiRot.Name = FlowShapes.AuxContourName;
                        p.AuxShapes.Add(roiRot);
                    }
                    else
                    {
                        p.AuxShapes.AddRange(FlowShapes.RoiOutline(usedRoi));
                    }
                }

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
            // 【v1.0.13 起用真实圆度】旧实现是 A/(W·H)（那是"占空比"，圆会算出 0.785），
            // 原生现在按 4πA/P² 用真实轮廓周长算，正圆 ≈1.0。
            return b.Roundness;
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
            // 投影窗口按"长度 × 2×滤波半宽"画成矩形（只给一条线看不出宽度/个数）
            p.AuxShapes.AddRange(FlowShapes.CaliperWindows(r.Points,
                node.GetDouble("FilterHalfWidth", 2)));
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

            int calTotal = r.Points != null ? r.Points.Count : 0;
            int calRejected = Math.Max(0, calTotal - r.ValidCount);

            // 主判据：线中点作为该节点的「坐标」（Coord 端子的实际取值来源）
            if (r.Line != null)
            {
                p.Metrics.TargetCount = 1;
                p.Metrics.TopCx = (r.Line.X1 + r.Line.X2) * 0.5;
                p.Metrics.TopCy = (r.Line.Y1 + r.Line.Y2) * 0.5;
                p.Metrics.AngleDeg = r.AngleDeg;
            }

            // 直线卡尺的运行参数全部上报（长度/角度/直线度/有效点/剔除数）
            p.Outputs.Add(new PortValue("Length", "长度", FlowPortKind.Number, r.Length));
            p.Outputs.Add(new PortValue("Angle", "角度", FlowPortKind.Angle, r.AngleDeg));
            p.Outputs.Add(new PortValue("Rms", "直线度", FlowPortKind.Number, r.Rms));
            p.Outputs.Add(new PortValue("ValidCount", "有效点", FlowPortKind.Number, (double)r.ValidCount));
            p.Outputs.Add(new PortValue("CaliperCount", "卡尺数", FlowPortKind.Number, (double)calTotal));
            p.Outputs.Add(new PortValue("RejectedCount", "剔除数", FlowPortKind.Number, (double)calRejected));
            // 几何端子值：下游「交点卡尺 / 点直线测量 / 线线测量」靠它求交、求垂距。
            // 只报 Length/Angle 这类标量是算不出交点的（端子表里声明了 Geometry 就必须真给值）。
            if (r.Line != null && !double.IsNaN(r.Line.X1) && !double.IsNaN(r.Line.X2))
            {
                p.Outputs.Add(new PortValue("Geometry", "直线", FlowPortKind.Geometry,
                    FlowGeometryValue.Line(r.Line.X1, r.Line.Y1, r.Line.X2, r.Line.Y2)));
            }
            if (r.Line != null)
            {
                p.Outputs.Add(new PortValue("Start", "起点", FlowPortKind.Point,
                    new PointF((float)r.Line.X1, (float)r.Line.Y1)));
                p.Outputs.Add(new PortValue("End", "终点", FlowPortKind.Point,
                    new PointF((float)r.Line.X2, (float)r.Line.Y2)));
            }

            p.Summary = string.Format(CultureInfo.InvariantCulture,
                "直线卡尺：长度 {0:F1} 角度 {1:F2}° RMS {2:F2} 有效点 {3}/{4}",
                r.Length, r.AngleDeg, r.Rms, r.ValidCount, calTotal)
                + PoseInputNote(s, node);   // 让用户一眼看到 ROI 到底有没有跟着修正
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

            // 投影窗口按"长度 × 2×滤波半宽"画成矩形（只给一条线看不出宽度/个数）
            p.AuxShapes.AddRange(FlowShapes.CaliperWindows(r.Points,
                node.GetDouble("FilterHalfWidth", 2)));
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

            int circTotal = r.Points != null ? r.Points.Count : 0;
            double radius = r.Circle.Radius;

            // 主判据：圆心即该节点的「坐标」（Coord 端子的实际取值来源）
            p.Metrics.TargetCount = 1;
            p.Metrics.TopCx = r.Circle.Cx;
            p.Metrics.TopCy = r.Circle.Cy;

            p.Outputs.Add(new PortValue("Diameter", "直径", FlowPortKind.Number, diameter));
            p.Outputs.Add(new PortValue("Radius", "半径", FlowPortKind.Number, radius));
            p.Outputs.Add(new PortValue("Center", "圆心", FlowPortKind.Point,
                new PointF((float)r.Circle.Cx, (float)r.Circle.Cy)));
            // 几何端子值（下游求交 / 求距用；只给数值是算不出几何关系的）
            p.Outputs.Add(new PortValue("Geometry", "圆", FlowPortKind.Geometry,
                FlowGeometryValue.Circle(r.Circle.Cx, r.Circle.Cy, radius)));
            p.Outputs.Add(new PortValue("Rms", "圆度", FlowPortKind.Number, r.Rms));
            p.Outputs.Add(new PortValue("ValidCount", "有效点", FlowPortKind.Number, (double)r.ValidCount));
            p.Outputs.Add(new PortValue("CaliperCount", "卡尺数", FlowPortKind.Number, (double)circTotal));
            p.Outputs.Add(new PortValue("RejectedCount", "剔除数", FlowPortKind.Number,
                (double)Math.Max(0, circTotal - r.ValidCount)));

            p.Summary = string.Format(CultureInfo.InvariantCulture,
                "圆卡尺：直径 {0:F1} 圆心 ({1:F1},{2:F1}) RMS {3:F2} 有效点 {4}/{5}",
                diameter, r.Circle.Cx, r.Circle.Cy, r.Rms, r.ValidCount, circTotal)
                + PoseInputNote(s, node);
            return true;
        }

        // =====================================================================
        //  测量类节点：交点卡尺 / 边缘对 / 点点 / 点直线 / 线线
        // =====================================================================
        //
        //  【共同约定】
        //   · 几何一律**先按端子取**（FindInputPort：输入端子 → 连线 → 上游输出端子），
        //     取不到就明确报「哪个端子没接 / 上游没给几何」，绝不退化成 (0,0) 继续算 ——
        //     0 坐标在图上"看起来有结果"，比直接报错更难查。
        //   · 输出的坐标点同时写进 Metrics（TopCx/TopCy），这样既进结果区，
        //     又能被「标定转换」按既有 X/Y 口径读到，不需要为每个新节点写适配。

        /// <summary>一次"按输入端子取上游值"的命中信息（用于给摘要带上来源节点名）。</summary>
        private sealed class InputPortHit
        {
            /// <summary>上游节点的输出台账。</summary>
            public NodeOutput Upstream;

            /// <summary>该输出端子上真正挂着的类型化值；可能为 null（上游没发这个键）。</summary>
            public PortValue Value;

            /// <summary>命中的连线。</summary>
            public FlowLink Link;
        }

        /// <summary>
        /// 按「输入端子 → 反查连线 → 上游输出端子」取上游值。
        /// <para>
        /// 多条线时取<strong>最后一条</strong>（后连的覆盖先连的，与「换数据源」的直觉一致）。
        /// 上游节点存在但该端子没有对应 PortValue 时返回的 <see cref="InputPortHit.Value"/> 为 null ——
        /// 调用方据此回退到 X/Y 或报"上游未提供该量"。
        /// </para>
        /// </summary>
        private static InputPortHit FindInputPort(FlowRunSession s, FlowNode node, string inPortKey)
        {
            if (s == null || node == null || s.Flow == null || string.IsNullOrEmpty(inPortKey)) return null;

            List<FlowLink> links = s.Flow.FindLinksTo(node.Id, inPortKey);
            if (links == null || links.Count == 0) return null;

            FlowLink link = links[links.Count - 1];
            if (link == null || string.IsNullOrEmpty(link.FromNodeId)) return null;

            NodeOutput up = null;
            if (s.Outputs != null)
            {
                for (int i = s.Outputs.Count - 1; i >= 0; i--)
                {
                    NodeOutput o = s.Outputs[i];
                    if (o != null && string.Equals(o.Id, link.FromNodeId, StringComparison.Ordinal)) { up = o; break; }
                }
            }
            if (up == null) return null;

            var hit = new InputPortHit { Upstream = up, Link = link };
            if (!string.IsNullOrEmpty(link.FromPort)) hit.Value = up.Port(link.FromPort);
            return hit;
        }

        /// <summary>来源节点的一句说明（摘要里用）。</summary>
        private static string UpstreamLabel(InputPortHit hit)
        {
            if (hit == null || hit.Upstream == null) return "";
            string name = string.IsNullOrEmpty(hit.Upstream.Name) ? hit.Upstream.Id : hit.Upstream.Name;
            return string.IsNullOrEmpty(name) ? "" : "上游「" + name + "」";
        }

        /// <summary>
        /// 取某输入端子上的<strong>点</strong>。
        /// <para>
        /// 取值顺序：① 端口值本身就是 PointF（Point / Pose 端子）；② 上游的 X / Y 两个数值键
        /// （由 <c>RecordOutput</c> 统一摊平，覆盖匹配 / Blob / 卡尺 / 拟合等所有节点）；
        /// ③ 上游位姿表里的中心（<see cref="NodeOutput.HasPose"/>）。
        /// </para>
        /// </summary>
        private static bool TryGetInputPoint(FlowRunSession s, FlowNode node, string portKey,
                                             out PointF point, out string note)
        {
            point = PointF.Empty;
            note = "";

            InputPortHit hit = FindInputPort(s, node, portKey);
            if (hit == null) return false;

            note = UpstreamLabel(hit);

            if (hit.Value != null && hit.Value.Raw is PointF)
            {
                point = (PointF)hit.Value.Raw;
                return !float.IsNaN(point.X) && !float.IsNaN(point.Y);
            }

            if (hit.Upstream != null)
            {
                PortValue xv = hit.Upstream.Port("X");
                PortValue yv = hit.Upstream.Port("Y");
                if (xv != null && yv != null)
                {
                    point = new PointF((float)xv.AsNumber(0.0), (float)yv.AsNumber(0.0));
                    return true;
                }

                if (hit.Upstream.HasPose)
                {
                    point = new PointF((float)hit.Upstream.Cx, (float)hit.Upstream.Cy);
                    return true;
                }
            }

            note = "";
            return false;
        }

        /// <summary>取某输入端子上的<strong>几何</strong>值（直线 / 圆）；上游没发几何时返回 false。</summary>
        private static bool TryGetInputGeometry(FlowRunSession s, FlowNode node, string portKey,
                                                out FlowGeometryValue geometry, out string note)
        {
            geometry = null;
            note = "";

            InputPortHit hit = FindInputPort(s, node, portKey);
            if (hit == null) return false;

            note = UpstreamLabel(hit);
            if (hit.Value != null) geometry = hit.Value.Raw as FlowGeometryValue;

            if (geometry == null) note = "";
            return geometry != null;
        }

        /// <summary>取某输入端子上的<strong>数值 / 角度</strong>；上游没发该键时返回 false。</summary>
        private static bool TryGetInputNumber(FlowRunSession s, FlowNode node, string portKey,
                                              out double value, out string note)
        {
            value = double.NaN;
            note = "";

            InputPortHit hit = FindInputPort(s, node, portKey);
            if (hit == null) return false;

            note = UpstreamLabel(hit);
            if (hit.Value != null)
            {
                double v = hit.Value.AsNumber(double.NaN);
                if (!double.IsNaN(v)) { value = v; return true; }
            }

            note = "";
            return false;
        }

        // ---------------------- 交点卡尺 ----------------------

        /// <summary>
        /// 交点卡尺：两条直线各自由「上游 Geometry」或「内联卡尺」取得，再求延长线交点。
        /// <para>
        /// 优先级：端子连了就用上游直线（对应一侧的内联参数不生效）；没连才现场找边。
        /// 因此"两条都接上游"时本节点是纯计算节点，连图像都不用接。
        /// </para>
        /// </summary>
        private bool RunLineIntersect(FlowRunSession s, FlowNode node, FlowNodeProgress p)
        {
            // 预览帧：连续运行时 StepNext 不留帧，这里显式留一份（只为预览，不影响计算）
            if (s.CaptureFrames && s.Current != null)
            {
                if (p.Frame != null) p.Frame.Dispose();
                try { p.Frame = (Bitmap)s.Current.Clone(); }
                catch (Exception) { p.Frame = null; }
            }

            FlowGeometryValue g1, g2;
            string note1, note2;
            TryGetInputGeometry(s, node, "Line1", out g1, out note1);
            TryGetInputGeometry(s, node, "Line2", out g2, out note2);

            double rms1 = 0.0, rms2 = 0.0;
            string src1, src2;

            if (g1 == null)
            {
                if (s.Current == null)
                {
                    p.Status = FlowNodeStatus.Warn;
                    p.Summary = "交点卡尺：直线1 未接上游几何，且没有输入图像可做内联卡尺";
                    return false;
                }

                CaliperLineResult r1;
                string err1;
                if (!TryRunInlineCaliper(s, node, "Line1", out r1, out err1))
                {
                    p.Summary = "交点卡尺（直线1）：" + err1;
                    return false;
                }

                g1 = FlowGeometryValue.Line(r1.Line.X1, r1.Line.Y1, r1.Line.X2, r1.Line.Y2);
                rms1 = r1.Rms;
                src1 = "内联卡尺1";
                AddCaliperOverlay(p, r1.Points, node.GetDouble("FilterHalfWidth", 2),
                                  r1.Line, FlowShapes.ColorCaliperLine);
            }
            else
            {
                src1 = string.IsNullOrEmpty(note1) ? "上游直线1" : note1 + " 直线1";
            }

            if (g2 == null)
            {
                if (s.Current == null)
                {
                    p.Status = FlowNodeStatus.Warn;
                    p.Summary = "交点卡尺：直线2 未接上游几何，且没有输入图像可做内联卡尺";
                    return false;
                }

                CaliperLineResult r2;
                string err2;
                if (!TryRunInlineCaliper(s, node, "Line2", out r2, out err2))
                {
                    p.Summary = "交点卡尺（直线2）：" + err2;
                    return false;
                }

                g2 = FlowGeometryValue.Line(r2.Line.X1, r2.Line.Y1, r2.Line.X2, r2.Line.Y2);
                rms2 = r2.Rms;
                src2 = "内联卡尺2";
                AddCaliperOverlay(p, r2.Points, node.GetDouble("FilterHalfWidth", 2),
                                  r2.Line, FlowShapes.ColorCaliperLine);
            }
            else
            {
                src2 = string.IsNullOrEmpty(note2) ? "上游直线2" : note2 + " 直线2";
            }

            if (g1 == null || g2 == null || g1.IsDegenerateLine || g2.IsDegenerateLine)
            {
                p.Summary = "交点卡尺：取到的直线无效（长度为 0）";
                return false;
            }

            double angle = FlowGeometryMath.AngleBetweenLines(g1, g2);

            PointF cross;
            if (!FlowGeometryMath.TryIntersect(g1, g2, out cross))
            {
                p.Summary = string.Format(CultureInfo.InvariantCulture,
                    "交点卡尺：两条线接近平行（夹角 {0:F2}°），无交点", angle);
                return false;
            }

            // 近似平行时交点会飞到极远处（数值上"算得出"，工程上没有意义）
            if (Math.Abs(cross.X) > 1e6 || Math.Abs(cross.Y) > 1e6)
            {
                p.Summary = string.Format(CultureInfo.InvariantCulture,
                    "交点卡尺：两条线接近平行（夹角 {0:F2}°），交点超出可用范围", angle);
                return false;
            }

            // 可视化：两线（上游来的也画出来，否则用户看不到"到底用的哪条线"）+ 交点十字
            p.Shapes.Add(FlowShapes.Line(g1.X1, g1.Y1, g1.X2, g1.Y2, FlowShapes.ColorMeasure, 1.6f));
            p.Shapes.Add(FlowShapes.Line(g2.X1, g2.Y1, g2.X2, g2.Y2, FlowShapes.ColorMeasure, 1.6f));
            p.Shapes.AddRange(FlowShapes.Crosshair(cross.X, cross.Y));
            p.Shapes.Add(FlowShapes.Marker(cross.X, cross.Y, FlowShapes.ColorEdgePair));

            p.Outputs.Add(new PortValue("Coord", "交点坐标", FlowPortKind.Pose, cross));
            p.Outputs.Add(new PortValue("Intersection", "交点", FlowPortKind.Point, cross));
            p.Outputs.Add(new PortValue("Angle", "夹角", FlowPortKind.Angle, angle));
            p.Outputs.Add(new PortValue("Rms", "直线度", FlowPortKind.Number, Math.Max(rms1, rms2)));

            p.Metrics.TargetCount = 1;
            p.Metrics.TopCx = cross.X;
            p.Metrics.TopCy = cross.Y;
            p.Metrics.AngleDeg = angle;

            // 判定：直线度上限（0 = 不限）
            double maxRms = node.GetDouble("MaxRms", 0);
            if (maxRms > 0 && Math.Max(rms1, rms2) > maxRms)
            {
                p.Summary = string.Format(CultureInfo.InvariantCulture,
                    "交点卡尺：交点 ({0:F2},{1:F2}) 夹角 {2:F2}°，直线度 {3:F2} 超出上限 {4:F2}",
                    cross.X, cross.Y, angle, Math.Max(rms1, rms2), maxRms);
                return false;
            }

            p.Summary = string.Format(CultureInfo.InvariantCulture,
                "交点卡尺：交点 ({0:F2},{1:F2}) 夹角 {2:F2}° 直线度 {3:F2}/{4:F2}（{5} · {6}）",
                cross.X, cross.Y, angle, rms1, rms2, src1, src2);
            return true;
        }

        /// <summary>
        /// 用节点内置的一组扫描线找一条直线（键名 = <paramref name="prefix"/> + X1/Y1/X2/Y2 …）。
        /// 与「直线卡尺」节点同一条路径：先减 ROI 裁剪偏移，再应用位置修正变换。
        /// </summary>
        private bool TryRunInlineCaliper(FlowRunSession s, FlowNode node, string prefix,
                                         out CaliperLineResult result, out string error)
        {
            result = null;
            error = "";

            PointF a = s.Transform.ApplyToPoint(node.GetDouble(prefix + "X1", 0) - s.OffsetX,
                                                node.GetDouble(prefix + "Y1", 0) - s.OffsetY);
            PointF b = s.Transform.ApplyToPoint(node.GetDouble(prefix + "X2", 0) - s.OffsetX,
                                                node.GetDouble(prefix + "Y2", 0) - s.OffsetY);

            var cp = new CaliperParam
            {
                X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y,
                CaliperCount = node.GetInt(prefix + "CaliperCount", 10),
                ProjectionLength = node.GetDouble(prefix + "ProjectionLength", 20),
                Polarity = node.GetInt(prefix + "Polarity", 0),
                EdgeThreshold = node.GetDouble(prefix + "EdgeThreshold", 30),
                FilterHalfWidth = node.GetInt("FilterHalfWidth", 2),
                SelectMode = node.GetInt("SelectMode", 0),
                MinEdgeCount = node.GetInt("MinEdgeCount", 3),
                RejectOutliers = node.GetInt("RejectOutliers", 1),
                OutlierTolerance = node.GetDouble("OutlierTolerance", 2)
            };

            OpResult<CaliperLineResult> lr = _algo.LineCaliper(s.Current, cp);
            if (!lr.Success || lr.Value == null)
            {
                error = string.IsNullOrEmpty(lr.Message) ? "未找到直线" : Tail(lr.Message);
                return false;
            }

            result = lr.Value;
            return true;
        }

        /// <summary>卡尺可视化：投影窗口（辅助层）+ 命中点 + 拟合直线（目标层）。</summary>
        private static void AddCaliperOverlay(FlowNodeProgress p, List<CaliperPoint> points,
                                              double halfWidth, LineResult line, Color color)
        {
            if (p == null) return;

            p.AuxShapes.AddRange(FlowShapes.CaliperWindows(points, halfWidth));
            p.Shapes.AddRange(FlowShapes.CaliperHits(points));

            if (line != null)
            {
                p.Shapes.Add(FlowShapes.Line(line.X1, line.Y1, line.X2, line.Y2, color, 1.6f));
            }
        }

        // ---------------------- 边缘对查找 ----------------------

        /// <summary>
        /// 边缘对查找（线宽）：沿扫描线法向逐格取样 → 一维找边 → 按极性配对 → 输出对宽。
        /// </summary>
        private bool RunEdgePair(FlowRunSession s, FlowNode node, FlowNodeProgress p)
        {
            if (s.Current == null)
            {
                p.Status = FlowNodeStatus.Skipped;
                p.Summary = "边缘对：无输入帧";
                return true;
            }

            if (s.CaptureFrames && s.Current != null)
            {
                if (p.Frame != null) p.Frame.Dispose();
                try { p.Frame = (Bitmap)s.Current.Clone(); }
                catch (Exception) { p.Frame = null; }
            }

            // 扫描线段是几何属性（图像坐标），与直线卡尺同口径：先减偏移，再应用位置修正
            PointF a = s.Transform.ApplyToPoint(node.GetDouble("X1", 0) - s.OffsetX,
                                                node.GetDouble("Y1", 0) - s.OffsetY);
            PointF b = s.Transform.ApplyToPoint(node.GetDouble("X2", 0) - s.OffsetX,
                                                node.GetDouble("Y2", 0) - s.OffsetY);

            double minWidth = node.GetDouble("MinWidth", 0);
            double maxWidth = node.GetDouble("MaxWidth", 0);
            int filterHw = node.GetInt("FilterHalfWidth", 2);

            EdgePairScanResult scan;
            string error;
            bool any = EdgePairScanner.TryScan(s.Current,
                a.X, a.Y, b.X, b.Y,
                node.GetInt("CaliperCount", 10),
                node.GetDouble("ProjectionLength", 30),
                node.GetDouble("EdgeThreshold", 20),
                filterHw,
                node.GetInt("PairPolarity", 0),
                minWidth, maxWidth,
                out scan, out error);

            // 窗口一律画出来（未命中的窗口是灰的）：用户据此判断"扫到了没有、扫歪了没有"
            if (scan != null)
            {
                p.AuxShapes.AddRange(FlowShapes.CaliperWindows(scan.Windows, filterHw));

                for (int i = 0; i < scan.Pairs.Count; i++)
                {
                    EdgePair pair = scan.Pairs[i];
                    p.Shapes.Add(FlowShapes.Line(pair.A.X, pair.A.Y, pair.B.X, pair.B.Y,
                                                 FlowShapes.ColorEdgePair, 2.0f));
                }
            }

            if (!any)
            {
                p.Summary = "边缘对：" + error;
                return false;
            }

            // 均值口径：全部有效对的平均宽度（单对抖动大，均值才是"线宽"）
            double sum = 0.0;
            for (int i = 0; i < scan.Pairs.Count; i++) sum += scan.Pairs[i].Width;
            double meanWidth = sum / scan.Pairs.Count;

            int selectMode = node.GetInt("SelectMode", 0);
            EdgePair main = EdgePairScanner.Select(scan.Pairs, selectMode);
            PointF center = main != null ? main.Center : scan.Pairs[0].Center;

            p.Shapes.Add(FlowShapes.Marker(center.X, center.Y, FlowShapes.ColorMeasure));

            p.Outputs.Add(new PortValue("Width", "宽度", FlowPortKind.Number, meanWidth));
            p.Outputs.Add(new PortValue("Count", "对数", FlowPortKind.Number, (double)scan.Pairs.Count));
            p.Outputs.Add(new PortValue("Center", "中心", FlowPortKind.Point, center));
            p.Outputs.Add(new PortValue("Coord", "坐标", FlowPortKind.Pose, center));

            p.Metrics.TargetCount = scan.Pairs.Count;
            p.Metrics.TopCx = center.X;
            p.Metrics.TopCy = center.Y;
            p.Metrics.Value = meanWidth;

            int expectMin = node.GetInt("ExpectMin", 0);
            int expectMax = node.GetInt("ExpectMax", 0);
            double widthMin = node.GetDouble("WidthMin", 0);
            double widthMax = node.GetDouble("WidthMax", 0);

            string measure = string.Format(CultureInfo.InvariantCulture,
                "边缘对：宽度 {0:F2} px（{1} 对，命中 {2}/{3} 格，中心 ({4:F1},{5:F1})）",
                meanWidth, scan.Pairs.Count, scan.HitCalipers, scan.CaliperCount, center.X, center.Y);

            if (expectMin > 0 && scan.Pairs.Count < expectMin)
            {
                p.Summary = measure + string.Format(CultureInfo.InvariantCulture, "，对数少于下限 {0}", expectMin);
                return false;
            }
            if (expectMax > 0 && scan.Pairs.Count > expectMax)
            {
                p.Summary = measure + string.Format(CultureInfo.InvariantCulture, "，对数多于上限 {0}", expectMax);
                return false;
            }
            if (widthMax > 0 && meanWidth > widthMax)
            {
                p.Summary = measure + string.Format(CultureInfo.InvariantCulture, "，宽度超出上限 {0:F2}", widthMax);
                return false;
            }
            if (widthMin > 0 && meanWidth < widthMin)
            {
                p.Summary = measure + string.Format(CultureInfo.InvariantCulture, "，宽度小于下限 {0:F2}", widthMin);
                return false;
            }

            p.Summary = measure + PoseInputNote(s, node);
            return true;
        }

        // ---------------------- 点点测量 ----------------------

        /// <summary>点点测量：两点距离 / 方向角 / ΔX / ΔY / 中点。</summary>
        private bool RunPointPointMeasure(FlowRunSession s, FlowNode node, FlowNodeProgress p)
        {
            PointF a, b;
            string noteA, noteB;
            if (!TryGetInputPoint(s, node, "Point1", out a, out noteA))
            {
                p.Summary = "点点测量：点1 未取到坐标（请把坐标输出接到「点1」端子）";
                return false;
            }
            if (!TryGetInputPoint(s, node, "Point2", out b, out noteB))
            {
                p.Summary = "点点测量：点2 未取到坐标（请把坐标输出接到「点2」端子）";
                return false;
            }

            double distance = FlowGeometryMath.Distance(a, b);
            double angle = FlowGeometryMath.DirectionDeg(a, b);
            double dx = b.X - a.X, dy = b.Y - a.Y;
            var mid = new PointF((a.X + b.X) * 0.5f, (a.Y + b.Y) * 0.5f);

            p.Shapes.Add(FlowShapes.Line(a.X, a.Y, b.X, b.Y, FlowShapes.ColorMeasure, 1.6f));
            p.Shapes.Add(FlowShapes.Marker(a.X, a.Y, FlowShapes.ColorEdgeHit));
            p.Shapes.Add(FlowShapes.Marker(b.X, b.Y, FlowShapes.ColorEdgeHit));
            p.Shapes.Add(FlowShapes.Marker(mid.X, mid.Y, FlowShapes.ColorMeasure));

            p.Outputs.Add(new PortValue("Distance", "距离", FlowPortKind.Number, distance));
            p.Outputs.Add(new PortValue("Angle", "角度", FlowPortKind.Angle, angle));
            p.Outputs.Add(new PortValue("Dx", "ΔX", FlowPortKind.Number, dx));
            p.Outputs.Add(new PortValue("Dy", "ΔY", FlowPortKind.Number, dy));
            p.Outputs.Add(new PortValue("Coord", "中点", FlowPortKind.Pose, mid));

            p.Metrics.TargetCount = 1;
            p.Metrics.TopCx = mid.X;
            p.Metrics.TopCy = mid.Y;
            p.Metrics.AngleDeg = angle;
            p.Metrics.Value = distance;

            p.Summary = string.Format(CultureInfo.InvariantCulture,
                "点点测量：距离 {0:F3} px 角度 {1:F2}° Δ({2:F1},{3:F1})（{4} → {5}）",
                distance, angle, dx, dy, noteA, noteB);

            double min = node.GetDouble("DistMin", 0);
            double max = node.GetDouble("DistMax", 0);
            if (max > 0 && distance > max)
            {
                p.Summary = string.Format(CultureInfo.InvariantCulture,
                    "点点测量：距离 {0:F3} px 超出上限 {1:F3}", distance, max);
                return false;
            }
            if (min > 0 && distance < min)
            {
                p.Summary = string.Format(CultureInfo.InvariantCulture,
                    "点点测量：距离 {0:F3} px 小于下限 {1:F3}", distance, min);
                return false;
            }

            return true;
        }

        // ---------------------- 点直线测量 ----------------------

        /// <summary>点直线测量：点到直线的垂距与垂足。</summary>
        private bool RunPointLineMeasure(FlowRunSession s, FlowNode node, FlowNodeProgress p)
        {
            PointF pt;
            string notePt;
            if (!TryGetInputPoint(s, node, "Point", out pt, out notePt))
            {
                p.Summary = "点直线测量：点 未取到坐标（请把坐标输出接到「点」端子）";
                return false;
            }

            FlowGeometryValue line;
            string noteLine;
            if (!TryGetInputGeometry(s, node, "Line", out line, out noteLine))
            {
                p.Summary = "点直线测量：直线 未取到几何（请把「直线卡尺 / 直线拟合」的几何输出接到「直线」端子）";
                return false;
            }
            if (line.IsDegenerateLine)
            {
                p.Summary = "点直线测量：上游直线长度为 0，无法求垂距";
                return false;
            }

            PointF foot;
            double distance = FlowGeometryMath.PointLineDistance(pt, line, out foot);
            if (double.IsNaN(distance))
            {
                p.Summary = "点直线测量：直线无效（无法求垂距）";
                return false;
            }

            double lineAngle = line.AngleDeg;

            p.Shapes.Add(FlowShapes.Line(line.X1, line.Y1, line.X2, line.Y2, FlowShapes.ColorMeasure, 1.4f));
            p.Shapes.Add(FlowShapes.Line(pt.X, pt.Y, foot.X, foot.Y, FlowShapes.ColorEdgePair, 1.6f, true));
            p.Shapes.Add(FlowShapes.Marker(pt.X, pt.Y, FlowShapes.ColorEdgeHit));
            p.Shapes.Add(FlowShapes.Marker(foot.X, foot.Y, FlowShapes.ColorMeasure));

            p.Outputs.Add(new PortValue("Distance", "垂距", FlowPortKind.Number, distance));
            p.Outputs.Add(new PortValue("Foot", "垂足", FlowPortKind.Point, foot));
            p.Outputs.Add(new PortValue("Angle", "线角", FlowPortKind.Angle, lineAngle));
            p.Outputs.Add(new PortValue("Coord", "垂足坐标", FlowPortKind.Pose, foot));

            p.Metrics.TargetCount = 1;
            p.Metrics.TopCx = foot.X;
            p.Metrics.TopCy = foot.Y;
            p.Metrics.AngleDeg = lineAngle;
            p.Metrics.Value = distance;

            p.Summary = string.Format(CultureInfo.InvariantCulture,
                "点直线测量：垂距 {0:F3} px 垂足 ({1:F2},{2:F2}) 线角 {3:F2}°（{4} · {5}）",
                distance, foot.X, foot.Y, lineAngle, notePt, noteLine);

            double min = node.GetDouble("DistMin", 0);
            double max = node.GetDouble("DistMax", 0);
            if (max > 0 && distance > max)
            {
                p.Summary = string.Format(CultureInfo.InvariantCulture,
                    "点直线测量：垂距 {0:F3} px 超出上限 {1:F3}", distance, max);
                return false;
            }
            if (min > 0 && distance < min)
            {
                p.Summary = string.Format(CultureInfo.InvariantCulture,
                    "点直线测量：垂距 {0:F3} px 小于下限 {1:F3}", distance, min);
                return false;
            }

            return true;
        }

        // ---------------------- 线线测量 ----------------------

        /// <summary>线线测量：夹角 / 交点 / 平行间距。</summary>
        private bool RunLineLineMeasure(FlowRunSession s, FlowNode node, FlowNodeProgress p)
        {
            FlowGeometryValue l1, l2;
            string note1, note2;
            if (!TryGetInputGeometry(s, node, "Line1", out l1, out note1))
            {
                p.Summary = "线线测量：直线1 未取到几何（请接「直线卡尺 / 直线拟合」的几何输出）";
                return false;
            }
            if (!TryGetInputGeometry(s, node, "Line2", out l2, out note2))
            {
                p.Summary = "线线测量：直线2 未取到几何（请接「直线卡尺 / 直线拟合」的几何输出）";
                return false;
            }
            if (l1.IsDegenerateLine || l2.IsDegenerateLine)
            {
                p.Summary = "线线测量：上游直线长度为 0，无法测量";
                return false;
            }

            double angle = FlowGeometryMath.AngleBetweenLines(l1, l2);
            double parallelTol = node.GetDouble("ParallelTol", 0.5);
            bool parallel = !double.IsNaN(angle) && angle <= parallelTol;

            p.Shapes.Add(FlowShapes.Line(l1.X1, l1.Y1, l1.X2, l1.Y2, FlowShapes.ColorMeasure, 1.4f));
            p.Shapes.Add(FlowShapes.Line(l2.X1, l2.Y1, l2.X2, l2.Y2, FlowShapes.ColorMeasure, 1.4f));

            p.Outputs.Add(new PortValue("Angle", "夹角", FlowPortKind.Angle, angle));
            p.Metrics.TargetCount = 1;
            p.Metrics.AngleDeg = angle;

            double spacing = double.NaN;
            PointF cross = PointF.Empty;
            bool hasCross = false;

            if (parallel)
            {
                spacing = FlowGeometryMath.ParallelSpacing(l1, l2);
                p.Outputs.Add(new PortValue("Distance", "间距", FlowPortKind.Number, spacing));
                p.Metrics.Value = spacing;
            }
            else if (FlowGeometryMath.TryIntersect(l1, l2, out cross))
            {
                hasCross = true;
                p.Outputs.Add(new PortValue("Intersection", "交点", FlowPortKind.Point, cross));
                p.Outputs.Add(new PortValue("Coord", "交点坐标", FlowPortKind.Pose, cross));
                p.Shapes.AddRange(FlowShapes.Crosshair(cross.X, cross.Y));
                p.Shapes.Add(FlowShapes.Marker(cross.X, cross.Y, FlowShapes.ColorEdgePair));

                p.Metrics.TopCx = cross.X;
                p.Metrics.TopCy = cross.Y;
            }

            // 判定：夹角范围（0/0 = 不限）与平行间距范围（仅平行时）
            double angleMin = node.GetDouble("AngleMin", 0);
            double angleMax = node.GetDouble("AngleMax", 0);
            double distMin = node.GetDouble("DistMin", 0);
            double distMax = node.GetDouble("DistMax", 0);

            string measure = parallel
                ? string.Format(CultureInfo.InvariantCulture,
                    "线线测量：平行（夹角 {0:F2}° ≤ {1:F2}°）间距 {2:F3} px（{3} · {4}）",
                    angle, parallelTol, spacing, note1, note2)
                : string.Format(CultureInfo.InvariantCulture,
                    "线线测量：夹角 {0:F2}° 交点 ({1:F2},{2:F2})（{3} · {4}）",
                    angle, hasCross ? cross.X : double.NaN, hasCross ? cross.Y : double.NaN, note1, note2);

            p.Summary = measure;

            if (angleMax > 0 && angle > angleMax)
            {
                p.Summary = measure + string.Format(CultureInfo.InvariantCulture, "，夹角超出上限 {0:F2}°", angleMax);
                return false;
            }
            if (angleMin > 0 && angle < angleMin)
            {
                p.Summary = measure + string.Format(CultureInfo.InvariantCulture, "，夹角小于下限 {0:F2}°", angleMin);
                return false;
            }

            if (parallel && !double.IsNaN(spacing))
            {
                if (distMax > 0 && spacing > distMax)
                {
                    p.Summary = measure + string.Format(CultureInfo.InvariantCulture, "，间距超出上限 {0:F3}", distMax);
                    return false;
                }
                if (distMin > 0 && spacing < distMin)
                {
                    p.Summary = measure + string.Format(CultureInfo.InvariantCulture, "，间距小于下限 {0:F3}", distMin);
                    return false;
                }
            }

            if (!parallel && !hasCross)
            {
                p.Summary = measure + "，但交点求解失败";
                return false;
            }

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

            // 搜索区域：只评价框内的清晰度（框为空 = 整幅，与旧行为一致）。
            // 走 SearchRegion 而不是自己 GetInt("RoiW")：参数读取与旋正口径全库只有一处实现。
            SearchRegion region = SearchRegion.FromNode(node, s.OffsetX, s.OffsetY);
            Rectangle usedRoi = Rectangle.Empty;
            bool roiAffine = false;
            Bitmap crop = region.Crop(s.Current, s.Transform, out usedRoi, out roiAffine);
            AddRegionOutline(p, s, region, s.Current.Width, s.Current.Height, roiAffine, "清晰度区域");
            try
            {
                var sr = _algo.Sharpness(crop ?? s.Current);
                if (!sr.Success)
                {
                    p.Summary = "清晰度评价失败";
                    return false;
                }

                double min = node.GetDouble("MinSharpness", 100);
                p.Summary = string.Format(CultureInfo.InvariantCulture, "清晰度 {0:F1}", sr.Value);
                return sr.Value >= min;
            }
            finally
            {
                if (crop != null) crop.Dispose();
            }
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

            p.Outputs.Add(new PortValue("Geometry", "圆", FlowPortKind.Geometry,
                FlowGeometryValue.Circle(cr.Value.Cx, cr.Value.Cy, cr.Value.Radius)));
            p.Outputs.Add(new PortValue("Radius", "半径", FlowPortKind.Number, cr.Value.Radius));

            // 圆心即「坐标」（Coord 端子的取值来源；不写这里，下游标定只能拿到 0,0）
            p.Metrics.TargetCount = 1;
            p.Metrics.TopCx = cr.Value.Cx;
            p.Metrics.TopCy = cr.Value.Cy;

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

            p.Outputs.Add(new PortValue("Geometry", "直线", FlowPortKind.Geometry,
                FlowGeometryValue.Line(lr.Value.X1, lr.Value.Y1, lr.Value.X2, lr.Value.Y2)));
            p.Outputs.Add(new PortValue("Length", "长度", FlowPortKind.Number, lr.Value.Length));
            p.Outputs.Add(new PortValue("Angle", "角度", FlowPortKind.Angle,
                FlowGeometryMath.NormalizeLineAngle(
                    Math.Atan2(lr.Value.Y2 - lr.Value.Y1, lr.Value.X2 - lr.Value.X1) * 180.0 / Math.PI)));

            // 线中点即「坐标」（Coord 端子的取值来源）
            p.Metrics.TargetCount = 1;
            p.Metrics.TopCx = (lr.Value.X1 + lr.Value.X2) * 0.5;
            p.Metrics.TopCy = (lr.Value.Y1 + lr.Value.Y2) * 0.5;
            p.Metrics.AngleDeg = FlowGeometryMath.NormalizeLineAngle(
                Math.Atan2(lr.Value.Y2 - lr.Value.Y1, lr.Value.X2 - lr.Value.X1) * 180.0 / Math.PI);

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
        /// <summary>本帧位置修正是否带旋转（带旋转时矩形 ROI 必须走仿射裁剪，否则只是外接框）。</summary>
        private static bool HasRoiRotation(FlowRunSession s)
        {
            return RoiAffineMap.NeedsAffine(s.Transform, 0.0);
        }

        /// <summary>
        /// 本帧是否需要仿射裁剪：<strong>ROI 自身角度</strong>或<strong>位姿旋转</strong>任一非零。
        /// <para>
        /// 只看 <c>DAngle</c> 是不够的：用户在画布上拖旋转手柄转出来的 ROI（<c>RoiAngle</c>）
        /// 会在"界面轮廓"里显示成斜的，但运行期按轴对齐裁剪 —— 于是现场看到
        /// 「有一个 ROI 是一直不变的」。两者必须一起判定。
        /// </para>
        /// </summary>
        private static bool HasRoiRotation(FlowRunSession s, double roiAngleDeg)
        {
            return RoiAffineMap.NeedsAffine(s.Transform, roiAngleDeg);
        }

        /// <summary>节点配置的 ROI 角度（度，绕 ROI 中心，顺时针为正）。</summary>
        private static double RoiAngleOf(FlowNode node)
        {
            return (node != null) ? node.GetDouble("RoiAngle", 0.0) : 0.0;
        }

        /// <summary>
        /// 节点里配置的 ROI（<strong>用户坐标</strong>，未做位置修正、未减裁剪偏移）。
        /// 仿射裁剪需要用它 —— 变换由裁剪函数自己施加一次，不能先用 ApplyToRect（那是外接框）。
        /// </summary>
        private static Rectangle RawRoi(FlowRunSession s, FlowNode node)
        {
            int w = node.GetInt("RoiW", 0);
            int h = node.GetInt("RoiH", 0);
            if (w <= 0 || h <= 0) return Rectangle.Empty;

            return new Rectangle((int)Math.Round(node.GetInt("RoiX", 0) - s.OffsetX),
                                 (int)Math.Round(node.GetInt("RoiY", 0) - s.OffsetY), w, h);
        }

        /// <summary>
        /// <strong>仿射裁剪</strong>：把"用户坐标 ROI"按 <c>RoiAngle + dAngle</c> 采成轴对齐小块。
        /// <para>实现与坐标映射同源，见 <see cref="RoiAffineMap.Crop"/> / <see cref="RoiAffineMap.MapPoint"/>。</para>
        /// </summary>
        private static Bitmap CropAffine(Bitmap frame, Rectangle roi, double roiAngleDeg, FlowRunTransform t)
        {
            return RoiAffineMap.Crop(frame, roi, roiAngleDeg, t);
        }

        /// <summary>
        /// 裁剪 ROI 取图：<strong>无旋转走原来的整数裁剪</strong>（快、像素完全一致），
        /// 有旋转走 <see cref="CropAffine"/>。
        /// </summary>
        /// <param name="roiUsed">实际用于"局部坐标 → 整帧坐标"换算的矩形（无旋转时=修正后的 AABB；
        /// 有旋转时=用户坐标 ROI，坐标由 <see cref="MapRoiPoint"/> 走仿射）。</param>
        private static Bitmap CropRoi(Bitmap frame, FlowRunSession s, FlowNode node,
                                      out Rectangle roiUsed, out bool affine)
        {
            // 统一到 SearchRegion：参数读取（Roi*/Search* 兼容）与旋正口径只有一处实现
            SearchRegion region = SearchRegion.FromNode(node, s.OffsetX, s.OffsetY);
            return region.Crop(frame, s.Transform, out roiUsed, out affine);
        }

        /// <summary>
        /// ROI 局部坐标 (u,v) → 整帧坐标（三种情形见 <see cref="RoiAffineMap.MapPoint"/>）。
        /// </summary>
        private static PointF MapRoiPoint(FlowRunSession s, Rectangle roiUsed, double roiAngleDeg,
                                          bool affine, double u, double v)
        {
            return RoiAffineMap.MapPoint(s.Transform, roiUsed, roiAngleDeg, affine, u, v);
        }

        /// <summary>
        /// 缺陷框：仿射时给**旋转矩形**（框跟着工件一起转），否则用轴对齐框。
        /// <para>【为什么不能一律用轴对齐框】旋正裁剪后，框在局部坐标里是正放的，
        /// 但工件在整帧上是斜的 —— 画成轴对齐框会"中心对、四角全错"，</para>
        /// </summary>
        /// <param name="lx">框中心在**送检图局部坐标**里的 X。</param>
        private static IGeometryShape BlobShapeAt(FlowRunSession s, Rectangle roiUsed, double roiAngleDeg,
                                                  bool affine, double lx, double ly,
                                                  double w, double h, string label)
        {
            if (!affine || s.Transform == null)
                return FlowShapes.BlobRect(lx, ly, w, h, label);

            PointF c = RoiAffineMap.MapPoint(s.Transform, roiUsed, roiAngleDeg, true, lx, ly);
            var rr = new RotatedRectShape(c, (float)Math.Max(1.0, w), (float)Math.Max(1.0, h),
                                          (float)RoiAffineMap.EffectiveAngle(s.Transform, roiAngleDeg));
            rr.StrokeColor = FlowShapes.ColorBlob;
            rr.LineWidth = FlowShapes.NormalLine;
            rr.IsEditable = false;
            if (!string.IsNullOrEmpty(label)) rr.Label = label;
            return rr;
        }

        /// <summary>
        /// 把搜索区域画到辅助层（受预览区「轮廓」开关整组显隐）。
        /// <para>
        /// 【为什么必须每个节点都画】改造前只有异常检测 / 颜色提取 / Blob 会画，
        /// 形状匹配、模板匹配、条码、清晰度、预处理、目标识别、OCR-DL 一个都不画 ——
        /// 现场看到"缺陷框落在搜索区域外"时，根本无从判断那个框是哪个节点的、
        /// 算法实际用的是哪一块。画出来的必须是<b>实际生效范围</b>（见 SearchRegion.Outline）。
        /// </para>
        /// </summary>
        private static void AddRegionOutline(FlowNodeProgress p, FlowRunSession s,
                                             SearchRegion region, int frameW, int frameH,
                                             bool affine, string label)
        {
            if (p == null || region == null || region.IsEmpty) return;
            p.AuxShapes.AddRange(region.Outline(s.Transform, frameW, frameH, affine, label));
        }

        /// <summary>ROI 轮廓：仿射时画一个旋转矩形，否则沿用四条边线。</summary>
        private static List<IGeometryShape> RoiOutlineShapes(FlowRunSession s, Rectangle roiUsed,
                                                             double roiAngleDeg, bool affine)
        {
            if (!affine || s.Transform == null)
                return FlowShapes.RoiOutline(roiUsed);

            var list = new List<IGeometryShape>(1);
            PointF c = RoiAffineMap.FrameCenter(s.Transform, roiUsed);
            var rr = new RotatedRectShape(c, roiUsed.Width, roiUsed.Height,
                                          (float)RoiAffineMap.EffectiveAngle(s.Transform, roiAngleDeg));
            rr.StrokeColor = FlowShapes.ColorRoi;
            rr.LineWidth = FlowShapes.ThinLine;
            rr.IsEditable = true;
            rr.Name = FlowShapes.AuxContourName;
            list.Add(rr);
            return list;
        }

        /// <summary>
        /// 把"ROI 局部坐标系"的叠加层贴回整帧尺寸的透明图层。
        /// <para>与 <see cref="RoiAffineMap.Crop"/> 严格互逆：那边用 <c>Rotate(-A)</c> 采样进来，
        /// 这边用 <c>Rotate(+A)</c> 画回去，否则热图会和底图错位。</para>
        /// </summary>
        private static Bitmap WarpRoiLayerBack(Bitmap layer, Size frameSize, Rectangle roiUsed,
                                               double roiAngleDeg, FlowRunTransform t, bool affine)
        {
            if (layer == null || frameSize.Width <= 0 || frameSize.Height <= 0) return null;

            Bitmap dst = null;
            try
            {
                dst = new Bitmap(frameSize.Width, frameSize.Height, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(dst))
                {
                    g.Clear(Color.Transparent);
                    g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                    if (!affine || t == null)
                    {
                        // 纯平移：裁剪图左上角对应 ROI 在整帧上的位置
                        g.DrawImage(layer, roiUsed.X, roiUsed.Y, roiUsed.Width, roiUsed.Height);
                    }
                    else
                    {
                        PointF c = RoiAffineMap.FrameCenter(t, roiUsed);
                        float a = (float)RoiAffineMap.EffectiveAngle(t, roiAngleDeg);
                        g.TranslateTransform(c.X, c.Y);
                        g.RotateTransform(a);
                        g.TranslateTransform(-roiUsed.Width / 2f, -roiUsed.Height / 2f);
                        g.DrawImage(layer, 0, 0, roiUsed.Width, roiUsed.Height);
                    }
                }
                return dst;
            }
            catch (Exception)
            {
                if (dst != null) dst.Dispose();
                return null;
            }
        }

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

                // 读取点云全部走本地文件（PLY/PCD/深度图），整链不需要相机
                if (t == FlowNodeType.PointCloudSource) return false;

                if (t != FlowNodeType.ImageSource) continue;

                // 只要有一个图像源节点选了「本地图片 / 本地图片文件夹」，整链就不需要相机
                if (IsFileImageSource(n.GetString("Source", "Camera")))
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
        /// <summary>模板边缘点少于这个数就该警告：几十个点的模型对噪声/光照毫无鲁棒性。</summary>
        private const int LowFeatureThreshold = 40;

        /// <summary>闭区间内的档数（步长 &lt;= 0 视为 1 档）。</summary>
        private static int CountSteps(double start, double end, double step)
        {
            if (step <= 1e-9) return 1;
            double span = Math.Abs(end - start);
            int n = (int)Math.Floor(span / step + 1e-9) + 1;
            return n < 1 ? 1 : n;
        }

        /// <summary>
        /// 形状匹配的诊断后缀：姿态数 / 搜索区 / 金字塔层数。
        /// <para>
        /// 【为什么放进摘要】"慢"和"不稳"在界面上必须能归因：姿态数决定搜索成本，
        /// 搜索区决定扫描面积，金字塔层数决定粗扫分辨率。只给一个总耗时，用户无从下手。
        /// </para>
        /// </summary>
        private static string ShapeDiagnostics(int poseCount, ShapeParam param,
                                               Rectangle search, TrainedModel cache)
        {
            int features = cache != null && cache.Meta != null ? cache.Meta.FeatureCount : 0;

            StringBuilder sb = new StringBuilder();
            if (features > 0) sb.Append(" · 边缘 ").Append(features);
            sb.Append(" · 姿态 ").Append(poseCount);
            sb.Append(" · ").Append(search.IsEmpty ? "搜索全图" : "搜索 " + search.Width + "×" + search.Height);
            sb.Append(" · 金字塔 ").Append(param.PyramidLevels);
            return sb.ToString();
        }

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
