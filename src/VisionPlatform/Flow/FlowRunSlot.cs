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
    /// <summary>单条流程的运行状态。</summary>
    public enum FlowSlotStatus
    {
        /// <summary>未运行。</summary>
        Idle,

        /// <summary>正在申请租约 / 起线程。</summary>
        Starting,

        /// <summary>运行中。</summary>
        Running,

        /// <summary>已暂停。</summary>
        Paused,

        /// <summary>停止中（等待线程退出）。</summary>
        Stopping,

        /// <summary>因致命错误中止（取帧失败 / 执行异常）。</summary>
        Faulted
    }

    /// <summary>
    /// 一条流程的执行槽：独占一个 <see cref="FlowRunner"/>（因而独占一个原生 <c>VisionAlgo</c> 上下文）
    /// 与一个线程、一个会话（隔离铁律 1:1:1:1:1，共享知识 26）。
    ///
    /// 【绝不复用】<c>FlowRunner</c> 内部按节点 Id 缓存位置修正的 Auto 基准（<c>_autoRefs</c>），
    /// 复用会串号；原生句柄也不是线程安全的；且每个 runner 必须各自 Dispose。
    ///
    /// 【致命错误只中止自己】取帧失败 / 执行异常时只把本槽置 <see cref="FlowSlotStatus.Faulted"/>
    /// 并释放租约后退线程，不影响其它槽（FR-PAR-03）。
    /// </summary>
    public sealed class FlowRunSlot : IDisposable
    {
        /// <summary>连续运行最小周期（ms）；0 = 不限制，由 UiSettings.MaxFlowFps 换算。</summary>
        private int _minCycleMs;

        private readonly object _sync = new object();

        private FlowRunner _runner;
        private Thread _thread;
        private volatile bool _stop;
        private volatile bool _pause;
        private volatile FlowSlotStatus _status = FlowSlotStatus.Idle;

        private FlowDefinition _flow;        // 启动时抓取的快照（Clone），运行期不受界面编辑影响
        private bool? _lastVerdict;
        private int _lastElapsedMs;
        private string _lastSummary = "";
        private string _errorMessage = "";
        private int _okCount;
        private int _ngCount;
        private int _cycles;
        private Stopwatch _runWatch;

        private bool _leaseHeld;
        private bool _disposed;

        /// <summary>流程 Id。</summary>
        public string FlowId { get; private set; }

        /// <summary>流程名（流程名四处一致的真值源，FR-UI2-03）。</summary>
        public string FlowName { get; private set; }

        /// <summary>产品型号（"" = 通用）。</summary>
        public string Product { get; private set; }

        /// <summary>绑定相机 Id（"" = 本地图片模式，不占租约）。</summary>
        public string CameraId { get; private set; }

        /// <summary>绑定视图序号（从 1 开始）。</summary>
        public int ViewIndex { get; private set; }

        public FlowRunSlot(FlowDefinition flow)
        {
            if (flow == null) throw new ArgumentNullException("flow");

            FlowId = flow.Id ?? "";
            FlowName = string.IsNullOrEmpty(flow.Name) ? "未命名流程" : flow.Name;
            Product = flow.Product ?? "";
            CameraId = flow.CameraId ?? "";
            ViewIndex = flow.ViewIndex;
        }

        // ---------------------- 状态读取（一律走锁）----------------------

        /// <summary>当前状态。</summary>
        public FlowSlotStatus Status
        {
            get { lock (_sync) { return _status; } }
        }

        /// <summary>最近一次结论（null = 尚未出结论）。</summary>
        public bool? LastVerdict
        {
            get { lock (_sync) { return _lastVerdict; } }
        }

        /// <summary>最近一次整链耗时（ms）。</summary>
        public int LastElapsedMs
        {
            get { lock (_sync) { return _lastElapsedMs; } }
        }

        /// <summary>最近一次整链摘要。</summary>
        public string LastSummary
        {
            get { lock (_sync) { return _lastSummary; } }
        }

        /// <summary>累计 OK 数。</summary>
        public int OkCount
        {
            get { lock (_sync) { return _okCount; } }
        }

        /// <summary>累计 NG 数。</summary>
        public int NgCount
        {
            get { lock (_sync) { return _ngCount; } }
        }

        /// <summary>实测帧率（fps）；未运行时为 0。</summary>
        public double Fps
        {
            get
            {
                lock (_sync)
                {
                    if (_runWatch == null || _cycles <= 0) return 0.0;
                    double sec = _runWatch.Elapsed.TotalSeconds;
                    return sec > 0.001 ? (_cycles / sec) : 0.0;
                }
            }
        }

        /// <summary>致命错误原因（仅 Faulted 时有效）。</summary>
        public string ErrorMessage
        {
            get { lock (_sync) { return _errorMessage; } }
        }

        /// <summary>状态是否属于「占用中」（计入并行上限）。</summary>
        public bool IsActive
        {
            get
            {
                FlowSlotStatus s = Status;
                return s == FlowSlotStatus.Starting || s == FlowSlotStatus.Running
                       || s == FlowSlotStatus.Paused || s == FlowSlotStatus.Stopping;
            }
        }

        // ---------------------- 生命周期 ----------------------

        /// <summary>
        /// 启动本槽：申请相机租约 → 创建专属 FlowRunner → 起线程。
        /// 相机被占用 / 未打开 / 创建执行器失败时返回 false 并给出中文原因。
        /// </summary>
        public bool Start(out string error)
        {
            error = null;

            lock (_sync)
            {
                if (_status != FlowSlotStatus.Idle)
                {
                    error = "流程「" + FlowName + "」已在运行中";
                    return false;
                }
                _status = FlowSlotStatus.Starting;
            }

            // 1) 相机租约（本地图片模式不需要；共享开关打开时不占租约，由取帧门串行化）
            if (!string.IsNullOrEmpty(CameraId) && !FlowRunManager.AllowCameraShare())
            {
                string owner;
                if (!CameraLease.TryAcquire(CameraId, FlowId, out owner))
                {
                    SetFaulted(null);
                    error = string.Format("相机 {0} 已被流程「{1}」占用，请先停止该流程",
                        CameraId, NameOfFlow(owner));
                    return false;
                }
                _leaseHeld = true;
            }

            // 2) 专属执行器（失败即放弃，不占着租约）
            try
            {
                _runner = new FlowRunner();
            }
            catch (Exception ex)
            {
                ReleaseLease();
                SetFaulted("流程执行器创建失败：" + ex.Message);
                error = "流程「" + FlowName + "」执行器创建失败（算法库不可用）：" + ex.Message;
                return false;
            }

            // 3) 抓取流程快照，避免运行期被界面编辑改动
            try
            {
                FlowDefinition live = FindFlow(FlowId);
                _flow = live != null ? live.Clone() : null;
            }
            catch (Exception)
            {
                _flow = null;
            }

            if (_flow == null)
            {
                ReleaseLease();
                DisposeRunner();
                SetFaulted("找不到流程定义");
                error = "流程「" + FlowName + "」已不存在，请刷新流程列表";
                return false;
            }

            FlowName = string.IsNullOrEmpty(_flow.Name) ? FlowName : _flow.Name;
            Product = _flow.Product ?? "";
            CameraId = _flow.CameraId ?? "";
            ViewIndex = _flow.ViewIndex;

            _stop = false;
            _pause = false;
            _cycles = 0;
            _runWatch = Stopwatch.StartNew();

            var th = new Thread(Loop);
            th.IsBackground = true;
            th.Name = "FlowSlot-" + FlowName;
            _thread = th;

            lock (_sync) { _status = FlowSlotStatus.Running; }

            th.Start();
            return true;
        }

        /// <summary>请求停止（异步；用 <see cref="Join"/> 等待线程真正退出）。</summary>
        public void RequestStop()
        {
            _stop = true;
            _pause = false;

            lock (_sync)
            {
                if (_status == FlowSlotStatus.Running || _status == FlowSlotStatus.Paused
                    || _status == FlowSlotStatus.Starting)
                {
                    _status = FlowSlotStatus.Stopping;
                }
            }
        }

        /// <summary>暂停 / 继续（P1）。</summary>
        public void SetPaused(bool paused)
        {
            _pause = paused;

            lock (_sync)
            {
                if (paused && _status == FlowSlotStatus.Running) _status = FlowSlotStatus.Paused;
                else if (!paused && _status == FlowSlotStatus.Paused) _status = FlowSlotStatus.Running;
            }
        }

        /// <summary>等待线程结束；返回是否在超时前结束。</summary>
        public bool Join(int timeoutMs)
        {
            Thread t = _thread;
            if (t == null) return true;

            try { return t.Join(timeoutMs); }
            catch (Exception) { return false; }
        }

        /// <summary>释放本槽资源：停止 → Join → 释放租约 → Dispose 专属执行器。</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            RequestStop();
            Join(3000);

            ReleaseLease();
            DisposeRunner();

            lock (_sync) { _status = FlowSlotStatus.Idle; }
        }

        // ---------------------- 线程主体 ----------------------

        private void Loop()
        {
            var cycle = new Stopwatch();
            _minCycleMs = FlowRateLimit.ResolveMinCycleMs();

            try
            {
                while (!_stop)
                {
                    if (_pause)
                    {
                        Thread.Sleep(50);
                        continue;
                    }

                    cycle.Restart();

                    FlowRunSession session = null;
                    try
                    {
                        ICamera camera = ResolveCamera();

                        // FR-PAR-09：连续运行不逐节点存帧
                        session = _runner.Begin(_flow, camera, false);
                        // 并行生产槽不看"每个节点当时的画面"：关掉节点帧克隆，
                        // 省掉每帧 N 次整幅拷贝与随之而来的大对象内存压力。
                        session.CaptureProgressFrames = false;

                        if (session.Aborted)
                        {
                            Logger.Error(FlowName, "流程无法运行：" + session.ErrorMessage);
                            SetFaulted(session.ErrorMessage);
                            break;
                        }

                        while (!session.IsDone && !_stop)
                        {
                            if (_runner.StepNext(session) == null) break;
                        }
                        if (_stop) break;

                        FlowRunResult result = _runner.Finish(session);

                        if (result != null && result.Success)
                        {
                            Publish(result, session);
                            RecordVerdict(result.Passed, result.ElapsedMs, result.Summary);
                            Logger.Result(FlowName, result.Passed,
                                (result.Summary ?? "") + "  " + result.ElapsedMs + "ms");
                        }
                        else
                        {
                            string msg = result != null ? (result.Message ?? "流程执行失败") : "流程执行失败";
                            Logger.Error(FlowName, "流程执行失败：" + msg);
                            SetFaulted(msg);
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(FlowName, "流程执行异常：" + ex.Message);
                        SetFaulted("流程执行异常：" + ex.Message);
                        break;
                    }
                    finally
                    {
                        // 会话内未交出的帧在此释放
                        if (session != null)
                        {
                            try { session.Dispose(); }
                            catch (Exception) { }
                        }
                    }

                    // 限帧：按配置的目标帧率补等待；0 = 不限制（只让出时间片）
                    cycle.Stop();
                    FlowRateLimit.Wait((int)cycle.ElapsedMilliseconds, _minCycleMs);
                }
            }
            finally
            {
                // 正常退出 / 致命错误都会走到这里：释放租约，回到 Idle
                ReleaseLease();

                lock (_sync)
                {
                    if (_status != FlowSlotStatus.Faulted) _status = FlowSlotStatus.Idle;
                    _thread = null;
                }
            }
        }

        /// <summary>取本槽绑定的已打开相机；本地图片模式返回 null。</summary>
        private ICamera ResolveCamera()
        {
            if (_flow == null) return null;
            if (!FlowRunner.NeedsCamera(_flow)) return null;

            string id = _flow.CameraId;
            if (string.IsNullOrEmpty(id)) return null;

            try
            {
                foreach (ICamera cam in CameraManager.Instance.OpenedCameras)
                {
                    if (cam != null && cam.Info != null && cam.Info.Id == id && cam.IsOpen) return cam;
                }
            }
            catch (Exception)
            {
                // 相机枚举异常时按「无相机」处理，由流程节点自己报错
            }

            return null;
        }

        /// <summary>
        /// 发布一次结果到全局通道（帧所有权随事件移交订阅者；无订阅者时本槽释放）。
        /// </summary>
        private void Publish(FlowRunResult result, FlowRunSession session)
        {
            Bitmap frame = session != null ? session.Current : null;

            var args = new FlowOutputEventArgs
            {
                ViewIndex = ViewIndex,
                Frame = frame,
                Shapes = result != null ? result.Shapes : null,
                Passed = result != null && result.Passed,
                Summary = result != null ? (result.Summary ?? "") : "",
                CameraName = CameraNameOf(_flow),
                FlowId = FlowId,
                FlowName = FlowName,
                Product = Product
            };

            if (session != null) session.Current = null;   // 已交出，会话不再释放

            if (!FlowOutputChannel.Publish(args) && frame != null)
            {
                try { frame.Dispose(); }
                catch (Exception) { }
            }
        }

        private void RecordVerdict(bool passed, int elapsedMs, string summary)
        {
            lock (_sync)
            {
                _lastVerdict = passed;
                _lastElapsedMs = elapsedMs;
                _lastSummary = summary ?? "";
                _cycles++;

                if (passed) _okCount++;
                else _ngCount++;
            }
        }

        private void SetFaulted(string message)
        {
            lock (_sync)
            {
                _status = FlowSlotStatus.Faulted;
                _errorMessage = message ?? "";
            }
        }

        private void ReleaseLease()
        {
            if (!_leaseHeld) return;
            _leaseHeld = false;

            try { CameraLease.ReleaseAll(FlowId); }
            catch (Exception) { }
        }

        private void DisposeRunner()
        {
            FlowRunner r = _runner;
            _runner = null;

            if (r == null) return;
            try { r.Dispose(); }
            catch (Exception) { }
        }

        // ---------------------- 小工具 ----------------------

        /// <summary>按 Id 在配置中查找流程定义。</summary>
        private static FlowDefinition FindFlow(string flowId)
        {
            if (string.IsNullOrEmpty(flowId)) return null;

            try
            {
                FlowSettings fs = ConfigStore.Current.Flows;
                if (fs == null || fs.Flows == null) return null;
                return fs.Flows.Find(x => x != null && x.Id == flowId);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>按流程 Id 取流程名（用于「被谁占用」的中文提示）。</summary>
        private static string NameOfFlow(string flowId)
        {
            FlowDefinition f = FindFlow(flowId);
            if (f != null && !string.IsNullOrEmpty(f.Name)) return f.Name;
            return string.IsNullOrEmpty(flowId) ? "其它流程" : flowId;
        }

        private static string CameraNameOf(FlowDefinition flow)
        {
            if (flow == null || string.IsNullOrEmpty(flow.CameraId)) return "本地图片";

            try
            {
                foreach (ICamera cam in CameraManager.Instance.OpenedCameras)
                {
                    if (cam != null && cam.Info != null && cam.Info.Id == flow.CameraId)
                        return cam.Info.Name;
                }
            }
            catch (Exception)
            {
                // 相机已关闭：退回 Id，避免抛异常打断发布
            }

            return flow.CameraId;
        }

        /// <summary>供界面显示的一行状态文本（纯文字，无图标）。</summary>
        public string StatusText
        {
            get
            {
                switch (Status)
                {
                    case FlowSlotStatus.Starting: return "启动中";
                    case FlowSlotStatus.Running:  return "运行中";
                    case FlowSlotStatus.Paused:   return "已暂停";
                    case FlowSlotStatus.Stopping: return "停止中";
                    case FlowSlotStatus.Faulted:  return "已中止";
                    default:                      return "已停止";
                }
            }
        }
    }
}
