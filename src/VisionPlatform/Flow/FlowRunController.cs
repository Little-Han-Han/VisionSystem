using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using VisionPlatform.Camera;
using VisionPlatform.Core;
using VisionSystem.Controls.Geometry;

namespace VisionPlatform.Flow
{
    /// <summary>
    /// 一次节点产出在预览链路上的<strong>不可变快照</strong>。
    /// 快照与会话解耦：帧的所有权随快照转移，会话 Dispose 不会连带释放预览正在显示的帧。
    /// </summary>
    public sealed class FlowProgressSnapshot
    {
        /// <summary>节点 Id（用于画布高亮与「选中节点」模式过滤）。</summary>
        public string NodeId = "";

        /// <summary>节点名。</summary>
        public string NodeName = "";

        /// <summary>链内序号（从 1 开始）。</summary>
        public int Index;

        /// <summary>链内节点总数。</summary>
        public int Total;

        /// <summary>节点执行状态。</summary>
        public FlowNodeStatus Status = FlowNodeStatus.Pending;

        /// <summary>
        /// 节点类型。结果文案（<see cref="NodeResultText"/>）要靠它决定用哪些判据字段，
        /// 由投递方填好，消费方（预览顶条 / 结果区 / 状态栏）不必各自去流程里反查。
        /// </summary>
        public FlowNodeType Type = FlowNodeType.ResultOutput;

        /// <summary>该节点自身耗时（整数毫秒）。</summary>
        public int ElapsedMs;

        /// <summary>该节点摘要（已去掉前缀）。</summary>
        public string Summary = "";

        /// <summary>该节点的输出帧；可能为 null（无留帧时）。<strong>消费方负责释放</strong>。</summary>
        public Bitmap Frame;

        /// <summary>节点生成的位图叠加层（分割掩膜 / 深度伪彩）；与 Frame 同尺寸，带 alpha。</summary>
        public Bitmap Overlay;

        /// <summary>
        /// 该快照的帧必须留给 UI 异步使用（深度学习效果图）。
        /// <para>
        /// 默认快照广播后帧立即释放（内存只有 2 个快照的水位），但 UI 是 BeginInvoke 异步消费的，
        /// 释放后再用就是"已销毁的 Bitmap"—— 表现正是效果图闪一下/画不出来。
        /// 带合成帧的节点快照数量很少（每帧 1~2 个），保留它换正确显示是划算的。
        /// </para>
        /// </summary>
        public bool KeepFrameForUi;

        /// <summary>该节点自身产出的叠加图形（图像坐标）。</summary>
        public List<IGeometryShape> Shapes = new List<IGeometryShape>();

        /// <summary>该节点产出的<strong>辅助</strong>图形（ROI 轮廓 / 卡尺窗口，不进结果区明细）。</summary>
        public List<IGeometryShape> AuxShapes = new List<IGeometryShape>();

        /// <summary>该节点的主判据（结果区/画布/预览共用的唯一真源）；未填时各字段为"不适用"。</summary>
        public FlowNodeMetrics Metrics = new FlowNodeMetrics();

        /// <summary>全链叠加图形；仅链尾快照非空（用于「链尾结果」显示模式）。</summary>
        public List<IGeometryShape> AllShapes = new List<IGeometryShape>();

        /// <summary>是否为一次完整运行的链尾快照。</summary>
        public bool IsChainEnd;

        /// <summary>
        /// 本次运行是否「首次示教了位置修正基准」。
        /// <para>宿主看到它要立刻把流程写盘 —— 不落盘的话下次运行又会重新示教，
        /// dX/dY 恒为 0（现场表现："位置修正完全没反应"）。</para>
        /// </summary>
        public bool BaselineTaught;

        /// <summary>示教了基准的节点 Id（宿主据此刷新参数面板里的基准值）。</summary>
        public string BaselineNodeId = "";

        /// <summary>
        /// 本周期最慢的节点（名称）与耗时。
        /// <para>
        /// 【为什么要暴露它】用户问"CPU 为什么这么高"时，没有归因就只能猜"是不是形状匹配"。
        /// 直接把最慢节点摆到状态栏，一眼看出瓶颈在哪。
        /// </para>
        /// </summary>
        public string SlowestNodeName = "";
        /// <summary>最慢节点耗时（毫秒）。</summary>
        public int SlowestNodeMs = -1;

        /// <summary>整链结论（仅链尾快照有效）。</summary>
        public bool Passed;

        /// <summary>整链摘要（仅链尾快照有效）。</summary>
        public string ChainSummary = "";

        /// <summary>整链耗时（毫秒，仅链尾快照有效）。</summary>
        public int CycleMs;

        /// <summary>连续运行实测帧率（fps，仅连续模式有效）。</summary>
        public double Fps;

        /// <summary>连续运行单周期平均耗时（ms，仅连续模式有效）。</summary>
        public double AvgCycleMs;

        /// <summary>会话级错误（拓扑不合法 / 相机不可用等）。</summary>
        public string ErrorMessage = "";

        /// <summary>
        /// 工作帧相对原图的裁剪偏移 X（预览要在<strong>原图</strong>上画用户 ROI 时需加回；
        /// 叠加图形本身已是原图坐标，无需再加）。
        /// </summary>
        public double OffsetX;

        /// <summary>工作帧相对原图的裁剪偏移 Y。</summary>
        public double OffsetY;

        /// <summary>释放快照持有的帧（未持有时无副作用）。</summary>
        public void ReleaseFrame()
        {
            if (Frame != null)
            {
                try { Frame.Dispose(); }
                catch (Exception) { }
                Frame = null;
            }

            if (Overlay != null)
            {
                try { Overlay.Dispose(); }
                catch (Exception) { }
                Overlay = null;
            }
        }
    }

    /// <summary>节点进度事件参数。</summary>
    public sealed class FlowProgressEventArgs : EventArgs
    {
        /// <summary>本次产出的快照（供画布状态与统计使用；帧仍由预览消费方取走）。</summary>
        public FlowProgressSnapshot Snapshot { get; private set; }

        public FlowProgressEventArgs(FlowProgressSnapshot snapshot) { Snapshot = snapshot; }
    }

    /// <summary>
    /// 流程运行编排器（单帧 / 单步 / 连续）：后台线程执行、覆盖式投递、限帧 ≤30 fps、
    /// 帧率与平均耗时统计。
    ///
    /// 【线程约定】<see cref="ProgressReady"/> 与 <see cref="OutputReady"/> 均在<strong>工作线程</strong>
    /// 触发，订阅者需自行 <c>BeginInvoke</c> 切回 UI 线程。
    /// 【帧所有权】<see cref="OutputReady"/> 的 <see cref="FlowOutputEventArgs.Frame"/> 所有权
    /// 移交订阅者；无订阅者时由本类释放。预览快照的帧必须经 <see cref="TryTakePending"/> 取走后释放。
    /// </summary>
    public sealed class FlowRunController : IDisposable
    {
        /// <summary>运行模式。</summary>
        public enum RunMode
        {
            /// <summary>空闲。</summary>
            Idle,

            /// <summary>单帧运行（整链一次）。</summary>
            Once,

            /// <summary>单步运行（每点一次前进一个节点）。</summary>
            Step,

            /// <summary>连续运行（≤30 fps 循环整链）。</summary>
            Loop
        }

        /// <summary>连续运行的最小周期（ms），保证 ≤30 fps。</summary>
        public const int MinCycleMs = 33;

        private readonly FlowRunner _runner;
        private readonly object _sync = new object();

        private Thread _thread;
        private volatile bool _stop;
        private volatile RunMode _mode = RunMode.Idle;

        // 单步模式：会话跨多次点击存活，直到链尾
        private FlowRunSession _stepSession;

        // 覆盖式投递：预览由 33 ms Timer 拉取，绝不用 BeginInvoke 逐帧推送
        private FlowProgressSnapshot _pending;
        private Bitmap _original;          // 「原始图像」显示模式的相机原图（覆盖式，旧图即时释放）

        // 「选中节点」模式专用槽：单帧链 ~9ms 就在后台线程跑完，而预览每 33ms 才拉一次；
        // 唯一的 _pending 槽被每个节点快照逐一覆盖（旧快照立即释放）→ UI 拉取时只剩链尾快照，
        // 选中非末节点时被 Apply 的节点过滤丢弃 → 永远无图。为此给选中节点单开 _nodeSlot：
        // PublishNode 命中选中节点时写入（旧快照立即释放），其它节点快照直接丢弃——
        // 内存峰值仍是 2 个快照，与旧实现同水位，无逐节点累积。
        private volatile string _selectedNodeId = "";
        private FlowProgressSnapshot _nodeSlot;

        // 连续运行统计
        private int _cycles;
        private long _totalCycleMs;
        private Stopwatch _loopWatch;

        private bool _disposed;

        /// <summary>节点级进度（工作线程触发）。</summary>
        public event EventHandler<FlowProgressEventArgs> ProgressReady;

        /// <summary>一次完整运行结束后回传主界面（帧所有权移交订阅者）。</summary>
        public event EventHandler<FlowOutputEventArgs> OutputReady;

        /// <summary>
        /// 一次启动（单帧 / 单步 / 连续）彻底结束、模式回到 Idle 时触发（工作线程）。
        /// 订阅者借此复位按钮状态；连续运行时即「循环线程已退出」。
        /// </summary>
        public event EventHandler Completed;

        public FlowRunController(FlowRunner runner)
        {
            if (runner == null) throw new ArgumentNullException("runner");
            _runner = runner;
        }

        /// <summary>当前运行模式。</summary>
        public RunMode Mode { get { return _mode; } }

        /// <summary>是否正在运行（单帧 / 单步 / 连续）。</summary>
        public bool IsBusy { get { return _mode != RunMode.Idle; } }

        /// <summary>连续运行是否处于循环线程中。</summary>
        public bool IsLooping { get { return _mode == RunMode.Loop; } }

        // =====================================================================
        //  对外操作
        // =====================================================================

        /// <summary>单帧运行：整链执行一次，逐节点发布进度，结束后回传主界面。</summary>
        public void RunOnce(FlowDefinition flow, ICamera camera)
        {
            Start(RunMode.Once, delegate { RunOnceCore(flow, camera); });
        }

        /// <summary>单步运行：推进一个节点；已在链尾时自动复位，下一次点击从头开始。</summary>
        public void StepOnce(FlowDefinition flow, ICamera camera)
        {
            Start(RunMode.Step, delegate { StepOnceCore(flow, camera); });
        }

        /// <summary>
        /// 单节点调试：只运行到指定节点为止（含该节点），不跑下游节点；
        /// 用于「选中某个算法块、单独看它的处理结果」。
        /// </summary>
        /// <param name="nodeId">目标节点 Id；为空或找不到时退化为整链运行。</param>
        public void RunTo(FlowDefinition flow, ICamera camera, string nodeId)
        {
            Start(RunMode.Once, delegate { RunToCore(flow, camera, nodeId); });
        }

        /// <summary>连续运行：以 ≤30 fps 循环执行整链，每周期结束投递一次快照。</summary>
        public void StartLoop(FlowDefinition flow, ICamera camera)
        {
            lock (_sync)
            {
                if (_mode == RunMode.Loop) return;
                if (_mode == RunMode.Step && _stepSession != null)
                {
                    // 单步会话与连续运行互斥：先收尾单步会话
                    try { _stepSession.Dispose(); }
                    catch (Exception) { }
                    _stepSession = null;
                }
            }

            _stop = false;
            _cycles = 0;
            _totalCycleMs = 0;
            _loopWatch = Stopwatch.StartNew();

            Start(RunMode.Loop, delegate { LoopCore(flow, camera); });
        }

        /// <summary>请求停止连续运行（当前周期跑完后退出）。单帧 / 单步不可中断，仅作记录。</summary>
        public void StopLoop()
        {
            _stop = true;
        }

        /// <summary>停止一切并等待线程结束；返回是否在超时前结束。</summary>
        public bool Stop(int timeoutMs)
        {
            _stop = true;

            Thread t;
            lock (_sync) { t = _thread; }
            if (t == null) return true;

            try { return t.Join(timeoutMs); }
            catch (Exception) { return false; }
        }

        /// <summary>等待当前运行结束（关窗前 Join，避免与原生上下文 Dispose 并发）。</summary>
        public bool Join(int timeoutMs)
        {
            Thread t;
            lock (_sync) { t = _thread; }
            if (t == null) return true;

            try { return t.Join(timeoutMs); }
            catch (Exception) { return false; }
        }

        /// <summary>
        /// 取走待显示的快照（覆盖式：取到即清空槽位）。返回 null 表示无新快照。
        /// 取到后<strong>调用方拥有 <see cref="FlowProgressSnapshot.Frame"/></strong>，替换显示时须释放旧帧。
        /// </summary>
        /// <param name="selectedNodeMode">
        /// true =「选中节点」模式：有选中节点时从节点专用槽取（null 时调用方保持当前显示不变）；
        /// 未选中任何节点则回落既有 _pending 槽——直接显示最后一次运行的最终结果
        ///（用户「点运行看结果」的最自然预期；此模式下 PushNode 会丢弃节点快照，仅链尾入槽）。
        /// false = 链尾等模式：从既有 _pending 槽取（链尾快照仍在其中，模式切换即可回看最后一次运行）。
        /// </param>
        public FlowProgressSnapshot TryTakePending(bool selectedNodeMode)
        {
            if (selectedNodeMode && _selectedNodeId != "")
            {
                FlowProgressSnapshot sel = Interlocked.Exchange(ref _nodeSlot, null);
                if (sel != null) return sel;
                // 连续运行只发布链尾快照、不写节点专用槽：选中节点时回落到链尾帧，
                // 否则（实测）预览会整帧静止。单帧/单步仍走节点槽，不受影响。
                return Interlocked.Exchange(ref _pending, null);
            }

            return Interlocked.Exchange(ref _pending, null);
        }

        /// <summary>取走「原始图像」（相机原图，覆盖式）。调用方负责释放返回的帧。</summary>
        public Bitmap TryTakeOriginal()
        {
            return Interlocked.Exchange(ref _original, null);
        }

        /// <summary>
        /// 设置「选中节点」模式的当前节点 Id（预览区「选中节点」模式据此过滤）。
        /// 选中变化时立即清空节点专用槽——旧选中节点的快照已不是想要的内容。
        /// </summary>
        public void SetSelectedNode(string nodeId)
        {
            if (nodeId == null) nodeId = "";
            if (string.Equals(nodeId, _selectedNodeId, StringComparison.Ordinal)) return;

            _selectedNodeId = nodeId;


            FlowProgressSnapshot old = Interlocked.Exchange(ref _nodeSlot, null);
            if (old != null) old.ReleaseFrame();
        }

        /// <summary>释放尚未被取走的挂起帧（关窗 / 停止时调用，避免残留）。两个快照槽都清。</summary>
        public void DropPending()
        {
            FlowProgressSnapshot p = Interlocked.Exchange(ref _pending, null);
            if (p != null) p.ReleaseFrame();

            FlowProgressSnapshot n = Interlocked.Exchange(ref _nodeSlot, null);
            if (n != null) n.ReleaseFrame();

            Bitmap b = Interlocked.Exchange(ref _original, null);
            if (b != null)
            {
                try { b.Dispose(); }
                catch (Exception) { }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _stop = true;
            Join(2000);
            DropPending();

            lock (_sync)
            {
                if (_stepSession != null)
                {
                    try { _stepSession.Dispose(); }
                    catch (Exception) { }
                    _stepSession = null;
                }
            }
        }

        // =====================================================================
        //  线程启动
        // =====================================================================

        private void Start(RunMode mode, ThreadStart body)
        {
            lock (_sync)
            {
                if (_mode == RunMode.Loop) return;      // 连续运行中不接受其它模式
                Thread alive = _thread;
                if (alive != null && alive.IsAlive) return;
                _mode = mode;
            }

            var th = new Thread(delegate()
            {
                try { body(); }
                catch (Exception ex)
                {
                    Logger.Error("流程运行异常：" + ex.Message);
                }
                finally
                {
                    lock (_sync)
                    {
                        _thread = null;
                        _mode = RunMode.Idle;
                    }


                    EventHandler done = Completed;
                    if (done != null)
                    {
                        try { done(this, EventArgs.Empty); }
                        catch (Exception ex) { Logger.Warn("运行结束通知失败：" + ex.Message); }
                    }
                }
            });

            lock (_sync) { _thread = th; }

            th.IsBackground = true;
            th.Name = "FlowRunCtl-" + mode;
            th.Start();
        }

        // =====================================================================
        //  单帧 / 单步 / 连续
        // =====================================================================

        private void RunOnceCore(FlowDefinition flow, ICamera camera)
        {
            FlowRunSession s = null;

            try
            {
                s = _runner.Begin(flow, camera, true);


                if (s.Aborted)
                {
                    PublishError(s.ErrorMessage, flow);
                    return;
                }

                FlowNodeProgress p;
                while ((p = _runner.StepNext(s)) != null)
                {
                    PublishNode(s, p, false, 0.0, 0.0);
                    if (_stop) { return; }
                }

                FlowRunResult res = _runner.Finish(s);
                Bitmap previewFrame = CloneOrNull(s.Current);
                PublishChainEnd(s, res, previewFrame, 0.0, 0.0);

                // 仅在整链正常收尾时回传主界面（与 FlowRunner.Run 口径一致）；
                // 帧所有权移交订阅者，无人订阅时由 PublishOutput 释放
                if (res != null && res.Success)
                {
                    Bitmap outFrame = s.Current;
                    s.Current = null;

                    // 主界面看的也是"结果画面"：把本周期的分割掩膜 / 深度伪彩叠上去，
                    // 否则主界面永远只有原图（用户反馈"两个界面都看不到效果图"）。
                    if (_cycleOverlay != null && outFrame != null)
                    {
                        try
                        {
                            var composed = new Bitmap(outFrame.Width, outFrame.Height,
                                                      System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                            using (var g = Graphics.FromImage(composed))
                            {
                                g.DrawImage(outFrame, 0, 0, composed.Width, composed.Height);
                                g.DrawImage(_cycleOverlay, 0, 0, composed.Width, composed.Height);
                            }
                            outFrame.Dispose();
                            outFrame = composed;
                        }
                        catch (Exception) { }
                    }
                    // 注意：这里不能 DropCycleOverlay —— 链尾快照已经用过它，
                    // 但同一周期内可能还有订阅者在使用；统一放到下一周期开始时释放。
                    PublishOutput(flow, camera, outFrame, res);
                }
            }
            finally
            {
                if (s != null)
                {
                    try { s.Dispose(); }
                    catch (Exception) { }
                }
            }
        }

        private void RunToCore(FlowDefinition flow, ICamera camera, string nodeId)
        {
            FlowRunSession s = null;
            try
            {
                s = _runner.Begin(flow, camera, true);


                if (s.Aborted)
                {
                    PublishError(s.ErrorMessage, flow);
                    return;
                }

                FlowNodeProgress p;
                while ((p = _runner.StepNext(s)) != null)
                {
                    PublishNode(s, p, false, 0.0, 0.0);
                    if (_stop) return;

                    // 跑到目标节点即停，不跑下游（单节点调试）
                    if (!string.IsNullOrEmpty(nodeId) && string.Equals(p.NodeId, nodeId, StringComparison.Ordinal))
                        return;
                }
            }
            finally
            {
                if (s != null)
                {
                    try { s.Dispose(); }
                    catch (Exception) { }
                }
            }
        }

        private void StepOnceCore(FlowDefinition flow, ICamera camera)
        {
            FlowRunSession s;

            lock (_sync)
            {
                if (_stepSession == null || _stepSession.IsDone)
                {
                    if (_stepSession != null)
                    {
                        try { _stepSession.Dispose(); }
                        catch (Exception) { }
                    }
                    _stepSession = _runner.Begin(flow, camera, true);   // 单步必须留帧
                }
                s = _stepSession;
            }

            if (s.Aborted)
            {
                PublishError(s.ErrorMessage, flow);
                ResetStepSession();
                return;
            }

            FlowNodeProgress p = _runner.StepNext(s);
            if (p != null)
            {
                PublishNode(s, p, false, 0.0, 0.0);

                // 还有后续节点：会话保留，等待下一次点击
                if (!s.IsDone) return;
            }

            // 链尾：收尾 → 回传主界面 → 复位，下一次点击从头开始（FR-ALG-20 验收②）
            FlowRunResult res = _runner.Finish(s);
            Bitmap previewFrame = CloneOrNull(s.Current);
            PublishChainEnd(s, res, previewFrame, 0.0, 0.0);

            if (res != null && res.Success)
            {
                Bitmap outFrame = s.Current;
                s.Current = null;
                PublishOutput(flow, camera, outFrame, res);
            }

            ResetStepSession();
        }

        private void LoopCore(FlowDefinition flow, ICamera camera)
        {
            var cycleWatch = new Stopwatch();

            // 连续运行整体算一次「运行」：在循环外清空位置修正 Auto 基准缓存，
            // 保证本轮首帧示教得到的基准能在后续帧复用（不被每帧 Begin 清空）。
            _runner.ClearAutoReferences();

            while (!_stop)
            {
                cycleWatch.Restart();

                FlowRunSession s = null;
                try
                {
                    // 连续运行关掉逐节点留帧：避免每周期 N 份 Bitmap 堆积
                    DropCycleOverlay();     // 上一周期的叠加层作废

                    // 循环内 Begin 传 false：不清空 Auto 基准（已在上面清过一次）
                    s = _runner.Begin(flow, camera, false, false);

                    if (s.Aborted)
                    {
                        PublishError(s.ErrorMessage, flow);
                        break;
                    }

                    // 【必须发节点快照】连续运行原来只发链尾快照，导致"点节点看不到该节点结果"：
                    // 预览面板在「选中节点」模式下会把链尾快照过滤掉，于是画面停在上一次内容。
                    // 这里只对**带位图叠加层**的节点（分割/深度）发快照，其余节点跳过 ——
                    // 既能让用户点开看到效果图，又不会每帧刷 N 次 UI。
                    FlowNodeProgress step;
                    while ((step = _runner.StepNext(s)) != null)
                    {
                        if (step.Overlay != null) PublishNode(s, step, false, 0.0, 0.0);
                        if (_stop) break;
                    }
                    if (_stop) break;      // 半途停止：本周期不投递、不回传

                    FlowRunResult res = _runner.Finish(s);

                    // 预览帧：克隆一份（1 份/周期，不是 N 份）
                    Bitmap previewFrame = CloneOrNull(s.Current);

                    // 原始帧：直接把所有权从会话转移到本类（零额外克隆）
                    Bitmap raw = s.SourceFrame;
                    s.SourceFrame = null;
                    PublishOriginal(raw);

                    _cycles++;
                    _totalCycleMs += res.ElapsedMs;

                    double seconds = _loopWatch != null ? _loopWatch.Elapsed.TotalSeconds : 0.0;
                    double fps = seconds > 0.001 ? (_cycles / seconds) : 0.0;
                    double avg = _cycles > 0 ? (_totalCycleMs / (double)_cycles) : 0.0;

                    PublishChainEnd(s, res, previewFrame, fps, avg);

                    // 主界面回传：每周期一次（架构 B5）；帧所有权移交
                    if (res != null && res.Success)
                    {
                        Bitmap outFrame = s.Current;
                        s.Current = null;
                        PublishOutput(flow, camera, outFrame, res);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error("连续运行周期异常：" + ex.Message);
                }
                finally
                {
                    if (s != null)
                    {
                        try { s.Dispose(); }
                        catch (Exception) { }
                    }
                }

                // 限帧：补到 ≥33 ms（≤30 fps）
                cycleWatch.Stop();
                int wait = MinCycleMs - (int)cycleWatch.ElapsedMilliseconds;
                if (wait > 0)
                {
                    try { Thread.Sleep(wait); }
                    catch (Exception) { }
                }
            }
        }

        private void ResetStepSession()
        {
            lock (_sync)
            {
                if (_stepSession != null)
                {
                    try { _stepSession.Dispose(); }
                    catch (Exception) { }
                    _stepSession = null;
                }
            }
        }

        // =====================================================================
        //  快照投递（覆盖式）
        // =====================================================================

        /// <summary>按节点 Id 求节点类型（快照要用它选主判据字段）；查不到按结果输出处理。</summary>
        private static FlowNodeType ResolveNodeType(FlowRunSession s, string nodeId)
        {
            if (s == null || s.Flow == null || s.Flow.Nodes == null || string.IsNullOrEmpty(nodeId))
                return FlowNodeType.ResultOutput;

            foreach (FlowNode n in s.Flow.Nodes)
            {
                if (n == null || !string.Equals(n.Id, nodeId, StringComparison.Ordinal)) continue;
                FlowNodeType t;
                if (n.TryGetType(out t)) return t;
            }
            return FlowNodeType.ResultOutput;
        }

        /// <summary>
        /// 本周期最后一个带叠加层的节点产物（分割掩膜 / 深度伪彩）。
        /// 主界面显示的是"输出帧"，它不经过节点快照 —— 要让它也有效果图，
        /// 就得把这一份叠加上去。每周期开始时释放上一份。
        /// </summary>
        private Bitmap _cycleOverlay;

        private void DropCycleOverlay()
        {
            if (_cycleOverlay != null)
            {
                try { _cycleOverlay.Dispose(); }
                catch (Exception) { }
                _cycleOverlay = null;
            }
        }

        private void PublishNode(FlowRunSession s, FlowNodeProgress p, bool chainEnd, double fps, double avgMs)
        {            var snap = new FlowProgressSnapshot();
            snap.NodeId = p.NodeId ?? "";
            snap.NodeName = p.NodeName ?? "";
            snap.Index = p.Index;
            snap.Total = p.Total;
            snap.Status = p.Status;
            snap.Type = ResolveNodeType(s, p.NodeId);
            snap.ElapsedMs = p.ElapsedMs;
            snap.Summary = p.Summary ?? "";
            snap.Fps = fps;
            snap.AvgCycleMs = avgMs;

            // 帧所有权转移：从会话转移到快照，避免会话 Dispose 时连带释放预览正在显示的帧
            snap.Frame = p.Frame;
            p.Frame = null;

            // ---- 位图叠加层（分割掩膜 / 深度伪彩）立即合成进帧 ----
            // 【为什么在这里合成】连续运行不保留逐节点帧（p.Frame 为空），预览面板拿不到底图，
            // 点击节点就只剩空白或上一帧 —— 表现为"点了没效果图 / 被原图刷掉"。
            // 这里用「节点自己的帧」或「当前工作帧」作底图，合成成一张新帧交给 UI，
            // UI 端就只需要正常显示 snap.Frame，不必关心叠加层的存在。
            if (p.Overlay != null)
            {
                Bitmap baseFrame = snap.Frame;
                if (baseFrame == null && s != null) baseFrame = s.Current;

                if (baseFrame != null)
                {
                    try
                    {
                        var composed = new Bitmap(baseFrame.Width, baseFrame.Height,
                                                  System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                        using (var g = Graphics.FromImage(composed))
                        {
                            g.DrawImage(baseFrame, 0, 0, composed.Width, composed.Height);
                            g.DrawImage(p.Overlay, 0, 0, composed.Width, composed.Height);
                        }

                        if (snap.Frame != null) snap.Frame.Dispose();   // 原 p.Frame 已被替换
                        snap.Frame = composed;
                        snap.KeepFrameForUi = true;   // UI 异步消费，不要广播后立刻释放
                    }
                    catch (Exception) { /* 合成失败就退回原帧，不影响其他显示 */ }
                }

                // 保留一份给主界面的输出帧用（同周期内有效，下一周期开始前释放）
                DropCycleOverlay();
                _cycleOverlay = p.Overlay;
                p.Overlay = null;
            }

            if (p.Shapes != null) snap.Shapes = new List<IGeometryShape>(p.Shapes);
            if (p.AuxShapes != null) snap.AuxShapes = new List<IGeometryShape>(p.AuxShapes);
            if (p.Metrics != null) snap.Metrics = p.Metrics;

            SnapOffsetInfo(snap, s);

            // 单步/连续都要让宿主知道"这个节点刚示教了基准"，否则基准永远落不了盘
            if (s != null && s.BaselineTaughtNodes != null
                && !string.IsNullOrEmpty(p.NodeId) && s.BaselineTaughtNodes.Contains(p.NodeId))
            {
                snap.BaselineTaught = true;
                snap.BaselineNodeId = p.NodeId;
            }

            PushNode(snap);
        }

        private void PublishChainEnd(FlowRunSession s, FlowRunResult res, Bitmap previewFrame, double fps, double avgMs)
        {
            var snap = new FlowProgressSnapshot();
            snap.IsChainEnd = true;
            snap.Fps = fps;
            snap.AvgCycleMs = avgMs;

            if (res != null)
            {
                snap.Passed = res.Success && res.Passed;
                snap.ChainSummary = res.Summary ?? "";
                snap.CycleMs = res.ElapsedMs;
                snap.BaselineTaught = res.BaselineTaught;
                snap.BaselineNodeId = res.BaselineNodeId ?? "";
                if (res.Shapes != null) snap.AllShapes = new List<IGeometryShape>(res.Shapes);

                // 找本周期最慢的节点（瓶颈归因）
                if (res.Progress != null)
                {
                    int maxMs = -1;
                    string maxName = "";
                    foreach (FlowNodeProgress np in res.Progress)
                    {
                        if (np == null || np.ElapsedMs <= maxMs) continue;
                        maxMs = np.ElapsedMs;
                        maxName = string.IsNullOrEmpty(np.NodeName) ? np.NodeId : np.NodeName;
                    }
                    snap.SlowestNodeMs = maxMs;
                    snap.SlowestNodeName = maxName ?? "";
                }
            }

            if (s != null && s.Progress.Count > 0)
            {
                FlowNodeProgress last = s.Progress[s.Progress.Count - 1];
                if (last != null)
                {
                    snap.NodeId = last.NodeId ?? "";
                    snap.NodeName = last.NodeName ?? "";
                    snap.Index = last.Index;
                    snap.Total = last.Total;
                    snap.Type = ResolveNodeType(s, last.NodeId);
                    // ⚠️ Warn 不得被结论覆盖：末节点是"只分割未识别"这类警示态时，
                    //    用整链 Passed 改写会把它变成 Ok，界面上再也看不出"没识别"。
                    //    整链结论只反映 OK/NG，节点自身的警示态优先保住。
                    snap.Status = last.Status == FlowNodeStatus.Warn
                        ? FlowNodeStatus.Warn
                        : (snap.Passed ? FlowNodeStatus.Ok : FlowNodeStatus.Ng);
                    snap.ElapsedMs = last.ElapsedMs;
                    snap.Summary = last.Summary ?? "";
                    if (last.Metrics != null) snap.Metrics = last.Metrics;
                    if (last.AuxShapes != null) snap.AuxShapes = new List<IGeometryShape>(last.AuxShapes);
                    if (snap.Shapes.Count == 0 && last.Shapes != null)
                        snap.Shapes = new List<IGeometryShape>(last.Shapes);
                }
            }

            // 链尾帧同样叠加本周期的效果图：
            // 否则「链尾结果」模式（或未选中任何节点时）会用纯原图把刚显示的掩膜/伪彩顶掉，
            // 表现就是"效果图闪一下又被原图刷新"。
            if (_cycleOverlay != null && previewFrame != null)
            {
                try
                {
                    var composed = new Bitmap(previewFrame.Width, previewFrame.Height,
                                              System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                    using (var g = Graphics.FromImage(composed))
                    {
                        g.DrawImage(previewFrame, 0, 0, composed.Width, composed.Height);
                        g.DrawImage(_cycleOverlay, 0, 0, composed.Width, composed.Height);
                    }
                    previewFrame.Dispose();
                    previewFrame = composed;
                }
                catch (Exception) { }
            }

            snap.Frame = previewFrame;
            SnapOffsetInfo(snap, s);
            Push(snap);
        }

        private void PublishError(string message, FlowDefinition flow)
        {
            var snap = new FlowProgressSnapshot();
            snap.IsChainEnd = true;
            snap.Passed = false;
            snap.ErrorMessage = message ?? "流程无法运行";
            snap.ChainSummary = snap.ErrorMessage;
            snap.NodeName = flow != null ? flow.Name : "";
            Push(snap);
        }

        /// <summary>把裁剪偏移记进快照（预览在原图上画用户 ROI 时需要加回）。</summary>
        private static void SnapOffsetInfo(FlowProgressSnapshot snap, FlowRunSession s)
        {
            if (s == null) return;
            snap.OffsetX = s.OffsetX;
            snap.OffsetY = s.OffsetY;
        }

        /// <summary>
        /// 节点级快照投递：「选中节点」模式下选中节点的那份写入 <see cref="_nodeSlot"/>
        /// （旧快照立即释放），其余节点快照不占槽——单帧链 ~9ms 跑完而预览 33ms 才拉一次，
        /// 唯一的 _pending 槽会被逐节点覆盖到只剩链尾，选中非末节点将永远无图（应用实测确诊）。
        /// <strong>节点级进度事件照常逐节点广播</strong>：画布节点状态高亮与冒烟统计都依赖
        /// <see cref="ProgressReady"/>；订阅者只读状态字段、不得囤积快照帧——未保留的快照
        /// 在广播返回后立即释放其帧，内存峰值仍是 2 个快照，与旧实现同水位。
        /// </summary>
        private void PushNode(FlowProgressSnapshot snap)
        {
            if (snap == null) return;

            bool keep;
            if (snap.KeepFrameForUi)
            {
                // 效果图快照：帧交给 UI（它在 BeginInvoke 里异步使用），这里不占 _nodeSlot
                keep = true;
            }
            else if (_selectedNodeId != "" && snap.NodeId == _selectedNodeId)
            {
                FlowProgressSnapshot old = Interlocked.Exchange(ref _nodeSlot, snap);
                if (old != null) old.ReleaseFrame();
                keep = true;
            }
            else
            {
                keep = false;   // 无槽可去（未选中 / 非选中节点）：广播完进度立即释放帧
            }


            EventHandler<FlowProgressEventArgs> h = ProgressReady;
            if (h != null)
            {
                try { h(this, new FlowProgressEventArgs(snap)); }
                catch (Exception ex) { Logger.Warn("进度通知失败：" + ex.Message); }
            }

            if (!keep) snap.ReleaseFrame();
        }

        /// <summary>覆盖式投递：被顶掉的旧快照立即释放其帧（共享知识 17.2）。</summary>
        private void Push(FlowProgressSnapshot snap)
        {
            if (snap == null) return;

            FlowProgressSnapshot old = Interlocked.Exchange(ref _pending, snap);
            if (old != null) old.ReleaseFrame();


            EventHandler<FlowProgressEventArgs> h = ProgressReady;
            if (h != null)
            {
                try { h(this, new FlowProgressEventArgs(snap)); }
                catch (Exception ex) { Logger.Warn("进度通知失败：" + ex.Message); }
            }
        }

        /// <summary>覆盖式更新「原始图像」。</summary>
        private void PublishOriginal(Bitmap raw)
        {
            if (raw == null) return;

            Bitmap old = Interlocked.Exchange(ref _original, raw);
            if (old != null && !ReferenceEquals(old, raw))
            {
                try { old.Dispose(); }
                catch (Exception) { }
            }
        }

        /// <summary>
        /// 回传主界面（沿用既有链路字段）。有订阅者时帧所有权移交，无订阅者时本类释放。
        /// </summary>
        private void PublishOutput(FlowDefinition flow, ICamera camera, Bitmap frame, FlowRunResult res)
        {
            var args = new FlowOutputEventArgs
            {
                ViewIndex = flow != null ? flow.ViewIndex : 1,
                Frame = frame,
                Shapes = res != null ? res.Shapes : null,
                Passed = res != null && res.Success && res.Passed,
                Summary = res != null ? (res.Summary ?? "") : "",
                CameraName = (camera != null && camera.Info != null) ? camera.Info.Name : "本地图片",
                FlowId = flow != null ? flow.Id : "",
                FlowName = flow != null ? flow.Name : ""
            };

            EventHandler<FlowOutputEventArgs> h = OutputReady;
            if (h != null)
            {
                try { h(this, args); }
                catch (Exception ex)
                {
                    Logger.Warn("流程结果回传失败：" + ex.Message);
                    if (frame != null)
                    {
                        try { frame.Dispose(); }
                        catch (Exception) { }
                    }
                }
            }
            else if (frame != null)
            {
                try { frame.Dispose(); }
                catch (Exception) { }
            }
        }

        private static Bitmap CloneOrNull(Bitmap src)
        {
            if (src == null) return null;
            try { return (Bitmap)src.Clone(); }
            catch (Exception) { return null; }
        }
    }
}
