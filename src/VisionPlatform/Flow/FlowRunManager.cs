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
            return StartOne(flowId, false);
        }

        /// <summary>
        /// 启动一条流程。<paramref name="ignoreLimit"/>=true 时跳过并行上限检查——
        /// 仅供 <see cref="StartAll"/> 使用，确保「当前产品 &amp; Enabled」的全部流程都能并行跑起来
        /// （满足主界面连续运行「所有流程同步运行」的诉求），而单条启动仍受上限约束。
        /// </summary>
        public static OpResult StartOne(string flowId, bool ignoreLimit)
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
                slot = new FlowRunSlot(flow);

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
        /// 启动「当前产品过滤后 &amp; Enabled=true」的全部流程。
        /// <strong>部分失败不回滚</strong>：能跑的先跑起来，失败原因汇总返回并记 WARN（共享知识 37）。
        /// </summary>
        public static OpResult StartAll()
        {
            List<FlowDefinition> targets = EnabledFlowsOfCurrentProduct();

            if (targets.Count == 0)
                return OpResult.Fail("当前没有可启动的流程（流程被停用或不属于当前产品）");

            int started = 0;
            var failures = new StringBuilder();

            foreach (FlowDefinition f in targets)
            {
                if (f == null) continue;

                OpResult r = StartOne(f.Id, true);
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
        public static void PauseAll()
        {
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
