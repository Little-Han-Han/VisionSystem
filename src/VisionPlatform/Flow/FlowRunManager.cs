using System;
using System.Collections.Generic;
using System.Text;
using VisionPlatform.Camera;
using VisionPlatform.Core;

namespace VisionPlatform.Flow
{
    /// <summary>
    /// 流程并行运行管理器（静态，生命周期 = 应用级）。
    ///
    /// 【隔离】每条运行中的流程一个 <see cref="FlowRunSlot"/>，槽各自持有
    /// <see cref="FlowRunner"/> / 线程 / 会话，互不共享（共享知识 26）。
    /// 【并行只做流程维度隔离】拓扑（DAG）校验与文案一行不改。
    /// 【关窗铁律】<c>StopAll → JoinAll → Dispose 各槽 Runner → CloseAll()</c>（共享知识 30）。
    ///
    /// 状态字典读写一律加锁，<see cref="Snapshot"/> 返回副本（共享知识 29.3）。
    /// </summary>
    public static class FlowRunManager
    {
        private static readonly object Sync = new object();

        /// <summary>flowId → 执行槽。</summary>
        private static readonly Dictionary<string, FlowRunSlot> Slots =
            new Dictionary<string, FlowRunSlot>(StringComparer.Ordinal);

        /// <summary>
        /// 槽位状态变化（供界面刷新）。<strong>可能在非 UI 线程触发</strong>，订阅方需自行 BeginInvoke。
        /// </summary>
        public static event EventHandler StatusChanged;

        /// <summary>最大并行流程数（取自配置；缺失补默认 4，越界钳制 1~8）。</summary>
        public static int MaxParallelFlows
        {
            get
            {
                try
                {
                    return UiSettings.NormalizeMaxParallelFlows(ConfigStore.Current.Ui.MaxParallelFlows);
                }
                catch (Exception)
                {
                    return UiSettings.DefaultMaxParallelFlows;
                }
            }
        }

        /// <summary>处于「占用中」状态的槽数（Starting/Running/Paused/Stopping）。</summary>
        public static int RunningCount
        {
            get
            {
                lock (Sync)
                {
                    int n = 0;
                    foreach (FlowRunSlot s in Slots.Values)
                    {
                        if (s != null && s.IsActive) n++;
                    }
                    return n;
                }
            }
        }

        /// <summary>
        /// 正在跑某一轮（<see cref="FlowSlotStatus.Running"/>）的槽数。
        /// <para>触发式模式下用它区分「有空闲、在等触发」与「正在执行本轮」，
        /// 界面状态栏据此显示「等待触发」还是「运行中」。0 与 <see cref="RunningCount"/> 之差
        /// 就是空闲等触发的槽数（暂停 / 启动中的槽也计入后者）。</para>
        /// </summary>
        public static int ExecutingCount
        {
            get
            {
                lock (Sync)
                {
                    int n = 0;
                    foreach (FlowRunSlot s in Slots.Values)
                    {
                        if (s != null && s.Status == FlowSlotStatus.Running) n++;
                    }
                    return n;
                }
            }
        }

        /// <summary>线程安全快照（副本）。</summary>
        public static List<FlowRunSlot> Snapshot()
        {
            lock (Sync)
            {
                var list = new List<FlowRunSlot>(Slots.Values);
                list.Sort(delegate(FlowRunSlot a, FlowRunSlot b)
                {
                    if (a == null || b == null) return 0;
                    return string.Compare(a.FlowName, b.FlowName, StringComparison.Ordinal);
                });
                return list;
            }
        }

        /// <summary>取某流程的槽；不存在返回 null。</summary>
        public static FlowRunSlot Get(string flowId)
        {
            if (string.IsNullOrEmpty(flowId)) return null;

            lock (Sync)
            {
                FlowRunSlot s;
                return Slots.TryGetValue(flowId, out s) ? s : null;
            }
        }

        // =====================================================================
        //  单流程启停
        // =====================================================================

        /// <summary>
        /// 启动一条流程。校验顺序：Enabled → 拓扑（DAG） → 相机已打开 → 相机租约 →
        /// 并行上限 → 未重复启动；任一失败返回中文原因且不启动。
        /// </summary>
        public static OpResult StartOne(string flowId)
        {
            return StartOne(flowId, false, false);
        }

        /// <summary>
        /// 启动一条流程。<paramref name="ignoreLimit"/>=true 时跳过并行上限检查——
        /// 仅供 <see cref="StartAll"/> 使用，确保「当前产品 &amp; Enabled」的全部流程都能并行跑起来
        /// （满足主界面连续运行「所有流程同步运行」的诉求），而单条启动仍受上限约束。
        /// </summary>
        public static OpResult StartOne(string flowId, bool ignoreLimit)
        {
            return StartOne(flowId, ignoreLimit, false);
        }

        /// <summary>
        /// 启动一条流程（可指定触发式）。
        /// <para><paramref name="triggered"/>=true：线程起来后<strong>等触发</strong>，触发一次跑一轮；
        /// false：连续运行（原行为）。</para>
        /// </summary>
        public static OpResult StartOne(string flowId, bool ignoreLimit, bool triggered)
        {
            FlowDefinition flow = FindFlow(flowId);
            if (flow == null) return OpResult.Fail("流程不存在（Id=" + (flowId ?? "") + "），请刷新流程列表");

            // 1) 启用开关
            if (!flow.Enabled)
                return OpResult.Fail("流程「" + flow.Name + "」已停用，请在流程编排中启用后再启动");

            // 2) 拓扑（DAG）
            string chainError;
            if (!FlowRunner.Validate(flow, out chainError))
                return OpResult.Fail("流程「" + flow.Name + "」拓扑校验未通过：" + chainError);

            // 3) 相机必须已打开
            if (FlowRunner.NeedsCamera(flow))
            {
                if (string.IsNullOrEmpty(flow.CameraId))
                    return OpResult.Fail("流程「" + flow.Name + "」未绑定相机，请先在流程编排中绑定并打开相机");

                if (!IsCameraOpened(flow.CameraId))
                    return OpResult.Fail("相机 " + flow.CameraId + " 未打开，请先在「相机管理」中打开该相机");

                // 4) 相机租约（默认严格独占；System.AllowCameraShare=true 时才放行共享，
                //    共享下由 CameraGrabGate 按相机串行化取帧，不会并发打原生句柄）
                if (!AllowCameraShare())
                {
                    string owner = CameraLease.OwnerOf(flow.CameraId);
                    if (owner != null && !string.Equals(owner, flowId, StringComparison.Ordinal))
                    {
                        return OpResult.Fail(string.Format("相机 {0} 已被流程「{1}」占用，请先停止该流程",
                            flow.CameraId, NameOfFlow(owner)));
                    }
                }
            }

            // 5) 并行上限（超限直接拒绝，不排队）；批量 StartAll 显式 ignoreLimit 以"跑全部流程"
            if (!ignoreLimit)
            {
                int limit = MaxParallelFlows;
                if (RunningCount >= limit)
                    return OpResult.Fail("已达最大并行数 " + limit + "，请先停止其它流程");
            }

            // 6) 未重复启动
            FlowRunSlot existing = Get(flowId);
            if (existing != null && existing.IsActive)
                return OpResult.Fail("流程「" + flow.Name + "」已在运行中");

            // 真正启动：槽各自持有执行器与线程
            FlowRunSlot slot = null;
            try
            {
                slot = new FlowRunSlot(flow, triggered);

                lock (Sync)
                {
                    FlowRunSlot old;
                    if (Slots.TryGetValue(flowId, out old) && old != null)
                    {
                        try { old.Dispose(); }
                        catch (Exception) { }
                    }
                    Slots[flowId] = slot;
                }

                string err;
                if (!slot.Start(out err))
                {
                    // 启动失败：把槽撤下，避免留下占位的死槽
                    lock (Sync)
                    {
                        FlowRunSlot cur;
                        if (Slots.TryGetValue(flowId, out cur) && ReferenceEquals(cur, slot))
                            Slots.Remove(flowId);
                    }

                    try { slot.Dispose(); }
                    catch (Exception) { }

                    return OpResult.Fail(err ?? ("流程「" + flow.Name + "」启动失败"));
                }
            }
            catch (Exception ex)
            {
                if (slot != null)
                {
                    try { slot.Dispose(); }
                    catch (Exception) { }
                }

                lock (Sync) { Slots.Remove(flowId); }

                return OpResult.Fail("流程「" + flow.Name + "」启动异常：" + ex.Message);
            }

            Logger.Info(flow.Name, "流程已启动");
            RaiseStatusChanged();
            return OpResult.Ok("流程「" + flow.Name + "」已启动");
        }

        /// <summary>
        /// 触发一条流程执行<strong>一轮</strong>（产线握手的核心入口）。
        /// <para>
        /// 【三种情况】
        /// ① 槽已按触发式运行 → 只放行一轮（正在跑则丢弃本次触发，不排队）；
        /// ② 槽还没起来（首次触发）→ 启动一条触发式槽并立即放行；
        /// ③ 槽是连续运行（调试模式）→ 忽略触发（它本来就在跑），返回成功避免刷错误日志。
        /// </para>
        /// <para>【为什么不能再用 StartOne】旧实现里「外部触发」直接 <c>StartOne</c> 起连续运行的槽，
        /// 结果一次触发就让流程永久循环 —— 这正是"不需要触发也在跑"的根因。</para>
        /// </summary>
        public static OpResult Trigger(string flowIdOrName)
        {
            FlowDefinition flow = FindFlow(flowIdOrName);
            if (flow == null) return OpResult.Fail("找不到流程：" + (flowIdOrName ?? ""));

            FlowRunSlot slot = Get(flow.Id);
            if (slot != null && slot.IsActive)
            {
                if (!slot.TriggeredMode)
                    return OpResult.Ok("流程「" + flow.Name + "」正在连续运行，忽略触发");

                return slot.Trigger()
                    ? OpResult.Ok("已触发流程「" + flow.Name + "」")
                    : OpResult.Fail("流程「" + flow.Name + "」正忙或已暂停，本次触发被丢弃");
            }

            // 首次触发：槽还不存在 → 起一条触发式槽，再放行一轮
            OpResult started = StartOne(flow.Id, false, true);
            if (started == null || !started.Success) return started;

            slot = Get(flow.Id);
            if (slot == null) return OpResult.Fail("流程「" + flow.Name + "」启动后未找到运行槽");

            return slot.Trigger()
                ? OpResult.Ok("已启动并触发流程「" + flow.Name + "」")
                : OpResult.Fail("流程「" + flow.Name + "」启动后触发失败");
        }

        /// <summary>停止一条流程（置停止标志 → Join → 释放租约 + Dispose 专属执行器）。</summary>
        public static OpResult StopOne(string flowId)
        {
            FlowRunSlot slot = Get(flowId);
            if (slot == null) return OpResult.Ok("流程未在运行");

            slot.RequestStop();
            bool finished = slot.Join(2000);

            lock (Sync) { Slots.Remove(flowId); }

            try { slot.Dispose(); }
            catch (Exception) { }

            if (!finished) Logger.Warn(slot.FlowName, "流程线程在 2 s 内未结束，已强制释放其资源");

            Logger.Info(slot.FlowName, "流程已停止");
            RaiseStatusChanged();
            return OpResult.Ok("流程「" + slot.FlowName + "」已停止");
        }

        /// <summary>暂停 / 继续一条流程（P1）。</summary>
        public static OpResult PauseOne(string flowId, bool paused)
        {
            FlowRunSlot slot = Get(flowId);
            if (slot == null) return OpResult.Fail("流程未在运行");

            slot.SetPaused(paused);
            RaiseStatusChanged();
            return OpResult.Ok(paused ? "流程「" + slot.FlowName + "」已暂停" : "流程「" + slot.FlowName + "」已继续");
        }

        /// <summary>暂停 / 继续一条流程（切换）。</summary>
        public static OpResult PauseOne(string flowId)
        {
            FlowRunSlot slot = Get(flowId);
            if (slot == null) return OpResult.Fail("流程未在运行");
            return PauseOne(flowId, slot.Status != FlowSlotStatus.Paused);
        }

        // =====================================================================
        //  批量启停
        // =====================================================================

        /// <summary>
        /// 按"要并行跑几条流程"给算法内部的 OpenCV 并行线程分配额度。
        /// <para>
        /// 【为什么必须做】OpenCV 默认吃满所有核。宿主本来就是每条流程一个线程并行，
        /// 每条流程内部再开满核 ⇒ N 条流程 = N×核数 个线程抢 核数 个核。
        /// 实测（3 条流程 / 12 核）：不限制时进程吃满 **10.5 核**，单圈算法 0.6~1.1 秒，
        /// 总产出只有 3 fps —— 全耗在互相抢锁和缓存颠簸上，比串行还慢。
        /// 按 核数 / 流程数 分配后，同样的活 CPU 反而降下来、节拍更稳。
        /// </para>
        /// <para>可用环境变量 <c>VA_ALGO_THREADS=n</c> 强制指定（n=0 表示串行），便于现场对比。</para>
        /// </summary>
        /// <summary>
        /// 当前分配给"单条流程"的算法并行度（0 = 未设置）。
        /// <para>OpenMP 的线程数设置是<strong>按线程</strong>生效的（set_num_threads 是线程局部状态），
        /// 所以每条流程线程启动时还要再调一次 <see cref="Native.VisionAlgo.SetThreads"/>，
        /// 否则算法里的 `#pragma omp parallel for` 仍按物理核数开满。</para>
        /// </summary>
        public static int AlgoThreads { get; private set; }

        private static void ApplyAlgoThreadBudget(int flowCount)
        {
            int threads;

            // 优先级：环境变量（现场临时对比）> appconfig 的 Algorithm.MaxThreads > 自动
            string raw = Environment.GetEnvironmentVariable("VA_ALGO_THREADS");
            int forced;
            if (!string.IsNullOrEmpty(raw) && int.TryParse(raw, out forced) && forced >= 0 && forced <= 64)
            {
                threads = forced;
            }
            else
            {
                int configured = 0;
                try
                {
                    if (ConfigStore.Current != null && ConfigStore.Current.Algorithm != null)
                        configured = ConfigStore.Current.Algorithm.MaxThreads;
                }
                catch (Exception) { }

                if (configured > 0)
                {
                    threads = Math.Min(configured, 64);
                }
                else
                {
                    // 自动：先定"整个软件可用多少核"，再按流程数平分 ——
                    // 而不是"每条流程都能拿一半核"。后者在 4 条流程时仍然会吃满整机。
                    //   总预算 = 核数/2（上限 8，至少 2）；单流程 = 总预算 / 流程数（至少 1）
                    // 依据：小图（1280×960）上并行收益很快见顶，多开的线程只在同步上空烧，
                    // 实测同一流程 12 线程 8.3fps/11.5 核 与 2 线程 3.4fps/2.14 核，
                    // 每帧 CPU 成本差 2.2 倍。
                    int cores = Environment.ProcessorCount;
                    int budget = Math.Max(2, Math.Min(8, cores / 2));
                    threads = Math.Max(1, budget / Math.Max(1, flowCount));
                }
            }

            AlgoThreads = threads;
            int applied = Native.VisionAlgo.SetThreads(threads);
            if (applied >= 0)
            {
                Logger.Info(string.Format(
                    "算法并行度：按 {0} 条流程分配 → OpenCV 线程数 {1}（生效 {2}，本机 {3} 核）",
                    flowCount, threads, applied, Environment.ProcessorCount));
            }
        }

        /// <summary>
        /// 启动「当前产品过滤后 &amp; Enabled=true」的全部流程。
        /// <strong>部分失败不回滚</strong>：能跑的先跑起来，失败原因汇总返回并记 WARN（共享知识 37）。
        /// </summary>
        /// <param name="triggered">
        /// true = 触发式（起来后等外部触发，触发一次跑一轮）——产线模式；
        /// false = 连续运行（原有行为）——调试模式。
        /// </param>
        public static OpResult StartAll(bool triggered = false)
        {
            List<FlowDefinition> targets = EnabledFlowsOfCurrentProduct();

            // 【诊断用】VA_ONLY_FLOW=<流程名或Id>：只启动指定流程，便于现场把 CPU 定位到某一条流程。
            // 不设该变量时不改变任何行为。
            string only = Environment.GetEnvironmentVariable("VA_ONLY_FLOW");
            if (!string.IsNullOrEmpty(only))
            {
                List<FlowDefinition> filtered = targets.FindAll(
                    x => x != null && (x.Name == only || x.Id == only));
                if (filtered.Count > 0)
                {
                    Logger.Info("[诊断] VA_ONLY_FLOW=" + only + "：只启动该流程（其余已启用流程本次跳过）");
                    targets = filtered;
                }
            }

            if (targets.Count == 0)
                return OpResult.Fail("当前没有可启动的流程（流程被停用或不属于当前产品）");

            ApplyAlgoThreadBudget(targets.Count);

            int started = 0;
            var failures = new StringBuilder();

            foreach (FlowDefinition f in targets)
            {
                if (f == null) continue;

                OpResult r = StartOne(f.Id, true, triggered);
                if (r != null && r.Success) { started++; continue; }

                string why = r != null ? (r.Message ?? "") : "";
                if (failures.Length > 0) failures.Append("；");
                failures.Append("「").Append(f.Name).Append("」").Append(why);
            }

            RaiseStatusChanged();

            if (failures.Length == 0)
                return OpResult.Ok("已启动 " + started + " 条流程");

            string summary = "已启动 " + started + "/" + targets.Count + " 条流程，失败：" + failures;
            Logger.Warn("全部启动：" + summary);

            // 部分成功也算成功（工业现场"能跑的先跑起来"优于"全有或全无"）
            return new OpResult(started > 0, summary);
        }

        /// <summary>暂停全部运行中的流程。</summary>
        public static void PauseAll()        {
            List<FlowRunSlot> slots = Snapshot();
            foreach (FlowRunSlot s in slots)
            {
                if (s == null) continue;
                try { s.SetPaused(true); }
                catch (Exception) { }
            }
            RaiseStatusChanged();
        }

        /// <summary>继续全部已暂停的流程（PauseAll 的逆操作）。</summary>
        public static void ResumeAll()
        {
            List<FlowRunSlot> slots = Snapshot();
            foreach (FlowRunSlot s in slots)
            {
                if (s == null) continue;
                try { s.SetPaused(false); }
                catch (Exception) { }
            }
            RaiseStatusChanged();
        }

        /// <summary>停止全部运行中的流程（并行 Join，各自释放租约与执行器）。</summary>
        public static void StopAll()
        {
            List<FlowRunSlot> slots = Snapshot();

            // 先全部置停止标志，再统一 Join —— 并行停止，不要串行等待
            foreach (FlowRunSlot s in slots)
            {
                if (s == null) continue;
                try { s.RequestStop(); }
                catch (Exception) { }
            }

            foreach (FlowRunSlot s in slots)
            {
                if (s == null) continue;
                try { s.Join(2000); }
                catch (Exception) { }
            }

            lock (Sync)
            {
                foreach (FlowRunSlot s in slots)
                {
                    if (s == null) continue;
                    try { s.Dispose(); }
                    catch (Exception) { }
                }
                Slots.Clear();
            }

            RaiseStatusChanged();
        }

        /// <summary>
        /// 关窗收尾：StopAll → JoinAll(3000) → Dispose 各槽执行器 → 清空租约表。
        /// 顺序不可颠倒；本方法<strong>不</strong>调用 CameraManager.CloseAll()（由调用方按铁律最后执行）。
        /// </summary>
        public static void Shutdown()
        {
            StopAll();

            // StopAll 已逐槽 Join(2000)；这里再兜一次，保证没有漏网的线程
            List<FlowRunSlot> slots;
            lock (Sync) { slots = new List<FlowRunSlot>(Slots.Values); }

            foreach (FlowRunSlot s in slots)
            {
                if (s == null) continue;
                try { s.Join(3000); }
                catch (Exception) { }
            }

            lock (Sync)
            {
                foreach (FlowRunSlot s in Slots.Values)
                {
                    if (s == null) continue;
                    try { s.Dispose(); }
                    catch (Exception) { }
                }
                Slots.Clear();
            }

            try { CameraLease.Clear(); }
            catch (Exception) { }

            RaiseStatusChanged();
        }

        // =====================================================================
        //  内部
        // =====================================================================

        /// <summary>当前产品过滤后、且 Enabled=true 的流程列表。</summary>
        private static List<FlowDefinition> EnabledFlowsOfCurrentProduct()
        {
            var result = new List<FlowDefinition>();

            try
            {
                FlowSettings fs = ConfigStore.Current.Flows;
                if (fs == null || fs.Flows == null) return result;

                string product = ConfigStore.Current.Ui.CurrentProduct ?? "";
                List<FlowDefinition> filtered = FlowModule.FilterByProduct(fs.Flows, product);

                foreach (FlowDefinition f in filtered)
                {
                    if (f != null && f.Enabled) result.Add(f);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("取可启动流程列表失败：" + ex.Message);
            }

            return result;
        }

        /// <summary>
        /// 当前产品过滤后是否存在 Enabled=true 的流程。
        /// 供主界面「全部启动」按钮可用性判断——无可启动流程时禁用该按钮（共享知识 37）。
        /// </summary>
        public static bool HasEnabledFlowsOfCurrentProduct()
        {
            try
            {
                FlowSettings fs = ConfigStore.Current.Flows;
                if (fs == null || fs.Flows == null) return false;

                string product = ConfigStore.Current.Ui.CurrentProduct ?? "";
                List<FlowDefinition> filtered = FlowModule.FilterByProduct(fs.Flows, product);
                foreach (FlowDefinition f in filtered)
                {
                    if (f != null && f.Enabled) return true;
                }
            }
            catch (Exception)
            {
                // 读取失败按"无可启动流程"兜底，避免误启用按钮
            }

            return false;
        }

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

        private static string NameOfFlow(string flowId)
        {
            FlowDefinition f = FindFlow(flowId);
            if (f != null && !string.IsNullOrEmpty(f.Name)) return f.Name;
            return string.IsNullOrEmpty(flowId) ? "其它流程" : flowId;
        }

        /// <summary>
        /// 是否允许多条流程共享同一台相机（P2 开关，默认 false = 严格独占）。
        /// 读配置失败时按<strong>严格独占</strong>兜底（安全侧优先）。
        /// </summary>
        public static bool AllowCameraShare()
        {
            try
            {
                AppConfig cfg = ConfigStore.Current;
                return cfg != null && cfg.System != null && cfg.System.AllowCameraShare;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>判断相机是否已打开（只看 CameraManager 的已打开列表，不改 CameraManager）。</summary>
        private static bool IsCameraOpened(string cameraId)
        {
            if (string.IsNullOrEmpty(cameraId)) return false;

            try
            {
                foreach (ICamera cam in CameraManager.Instance.OpenedCameras)
                {
                    if (cam != null && cam.Info != null && cam.Info.Id == cameraId && cam.IsOpen) return true;
                }
            }
            catch (Exception)
            {
                return false;
            }

            return false;
        }

        private static void RaiseStatusChanged()
        {
            try
            {
                EventHandler h = StatusChanged;
                if (h != null) h(null, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Logger.Warn("状态变更通知失败：" + ex.Message);
            }
        }
    }
}
