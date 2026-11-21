using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using VisionPlatform.Core;
using VisionPlatform.Flow;

namespace VisionPlatform.Communication
{
    /// <summary>
    /// 常驻通讯任务引擎：心跳、结果推送、触发启动、流程 Busy/Ready 映射。
    /// <para>
    /// 【为什么放在流程外】心跳/Ready/Busy 必须与流程是否运行无关；结果推送要等
    /// 多条流程同批结果。把这些放进流程节点会导致"流程停了心跳也停""等待 ack 阻塞流程线程"。
    /// </para>
    /// <para>所有动作都在线程池执行，单个任务卡住不会阻塞流程发布或其它任务。</para>
    /// </summary>
    public static class CommJobEngine
    {
        private static readonly object Sync = new object();
        private static readonly Dictionary<string, FlowResultSnapshot> ResultsById =
            new Dictionary<string, FlowResultSnapshot>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, FlowResultSnapshot> ResultsByName =
            new Dictionary<string, FlowResultSnapshot>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, JobRuntime> Runtimes =
            new Dictionary<string, JobRuntime>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, bool> ToggleStates =
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, bool> EdgeStates =
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, BindingState> BindingStates =
            new Dictionary<string, BindingState>(StringComparer.OrdinalIgnoreCase);

        private static List<CommJobConfig> _jobs = new List<CommJobConfig>();
        private static List<CommFlowBindingConfig> _bindings = new List<CommFlowBindingConfig>();

        private static Thread _timerThread;
        private static Thread _edgeThread;
        private static Thread _bindingThread;
        private static volatile bool _stop;
        private static bool _initialized;
        private static bool _startupDone;

        /// <summary>任务状态快照（界面诊断用）。</summary>
        public sealed class JobStatus
        {
            public string Name;
            public string Trigger;
            public bool Running;
            public long RunCount;
            public DateTime LastRun;
            public string LastError;
        }

        // =====================================================================
        //  生命周期
        // =====================================================================

        public static void Initialize()
        {
            if (_initialized) return;

            lock (Sync)
            {
                if (_initialized) return;
                _initialized = true;
                _stop = false;
            }

            Reload();
            FlowOutputChannel.OutputReady += OnFlowOutput;
            ConfigStore.Changed += OnConfigChanged;

            _timerThread = new Thread(TimerLoop) { IsBackground = true, Name = "CommJobTimer" };
            _edgeThread = new Thread(EdgeLoop) { IsBackground = true, Name = "CommJobEdge" };
            _bindingThread = new Thread(BindingLoop) { IsBackground = true, Name = "CommFlowBinding" };
            _timerThread.Start();
            _edgeThread.Start();
            _bindingThread.Start();

            // Startup 任务不在这里跑：软件刚打开时通讯还不该动，
            // 由 CommRuntime.Activate() 调 OnActivated() 触发（见该类注释）。
        }

        /// <summary>
        /// 进入运行态时调用：执行一次 Startup 触发类型的一次性任务（只执行一次）。
        /// <para>放在这里而不是 <see cref="Initialize"/>：软件打开即初始化通讯对象，
        /// 但"运行"才代表产线开始工作。</para>
        /// </summary>
        public static void OnActivated()
        {
            lock (Sync)
            {
                if (_startupDone) return;
                _startupDone = true;
            }

            if (!CommRuntime.IsActive) return;
            RunStartupJobs();
        }

        public static void Reload()
        {
            CommSettings settings = ConfigStore.Current.Comm;
            CommSettings.UpgradeToCurrent(settings);

            lock (Sync)
            {
                // 配置变了：旧结果/边沿/绑定缓存都作废，避免用上一套配置的残留触发新任务
                ResultsById.Clear();
                ResultsByName.Clear();
                EdgeStates.Clear();
                BindingStates.Clear();

                _jobs = new List<CommJobConfig>();
                if (settings.Jobs != null)
                    foreach (CommJobConfig j in settings.Jobs)
                        if (j != null && j.Enabled && !string.IsNullOrEmpty(j.Name))
                            _jobs.Add(j.Clone());

                _bindings = new List<CommFlowBindingConfig>();
                if (settings.FlowBindings != null)
                    foreach (CommFlowBindingConfig b in settings.FlowBindings)
                        if (b != null && b.Enabled && !string.IsNullOrEmpty(b.Flow))
                            _bindings.Add(b.Clone());

                // 保留运行统计；新增任务补运行时
                var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (CommJobConfig j in _jobs) keep.Add(j.Name);

                var dead = new List<string>();
                foreach (string name in Runtimes.Keys) if (!keep.Contains(name)) dead.Add(name);
                foreach (string name in dead) Runtimes.Remove(name);

                foreach (CommJobConfig j in _jobs)
                {
                    if (!Runtimes.ContainsKey(j.Name))
                        Runtimes[j.Name] = new JobRuntime { Config = j };
                }
            }
        }

        public static void Shutdown()
        {
            if (!_initialized) return;

            _stop = true;
            try { FlowOutputChannel.OutputReady -= OnFlowOutput; } catch (Exception) { }
            try { ConfigStore.Changed -= OnConfigChanged; } catch (Exception) { }

            Join(_timerThread); Join(_edgeThread); Join(_bindingThread);
            _initialized = false;
        }

        public static List<JobStatus> Snapshot()
        {
            var list = new List<JobStatus>();
            lock (Sync)
            {
                foreach (JobRuntime rt in Runtimes.Values)
                {
                    list.Add(new JobStatus
                    {
                        Name = rt.Config.Name,
                        Trigger = rt.Config.Trigger,
                        Running = rt.Running != 0,
                        RunCount = rt.RunCount,
                        LastRun = rt.LastRun,
                        LastError = rt.LastError
                    });
                }
            }
            return list;
        }

        /// <summary>手动执行一次任务（界面"试运行"用）。</summary>
        public static void RunOnce(string jobName)
        {
            CommJobConfig job = null;
            lock (Sync)
            {
                foreach (CommJobConfig j in _jobs)
                    if (string.Equals(j.Name, jobName, StringComparison.OrdinalIgnoreCase)) { job = j; break; }
            }
            if (job != null) RunJob(job, BuildContext(job));
        }

        // =====================================================================
        //  流程结果 → 组合任务
        // =====================================================================

        private static void OnFlowOutput(object sender, FlowOutputEventArgs e)
        {
            if (e == null) return;

            var snap = new FlowResultSnapshot();
            snap.FlowId = e.FlowId ?? "";
            snap.FlowName = e.FlowName ?? "";
            snap.Passed = e.Passed;
            snap.ElapsedMs = e.ElapsedMs;
            snap.Summary = e.Summary ?? "";
            snap.CycleId = e.CycleId;
            snap.Time = DateTime.Now;

            FlowRunSlot slot = string.IsNullOrEmpty(snap.FlowId) ? null : FlowRunManager.Get(snap.FlowId);
            if (slot != null)
            {
                snap.OkCount = slot.OkCount;
                snap.NgCount = slot.NgCount;
                snap.Fps = slot.Fps;
            }

            lock (Sync)
            {
                if (snap.FlowId.Length > 0) ResultsById[snap.FlowId] = snap;
                if (snap.FlowName.Length > 0) ResultsByName[snap.FlowName] = snap;

                EvaluateResultJobs(snap);
            }
        }

        private static void EvaluateResultJobs(FlowResultSnapshot current)
        {
            foreach (CommJobConfig job in _jobs)
            {
                if (job == null || !job.Enabled) continue;
                if (!string.Equals(job.Trigger, "ResultGroup", StringComparison.OrdinalIgnoreCase)) continue;

                if (!JobContainsFlow(job, current)) continue;

                JobRuntime rt;
                if (!Runtimes.TryGetValue(job.Name, out rt) || rt.Running != 0) continue;

                List<FlowResultSnapshot> members = (job.Flows == null || job.Flows.Count == 0)
                    ? new List<FlowResultSnapshot> { current }
                    : ResolveMembers(job);
                if (members == null || members.Count == 0) continue;

                bool ready;
                if (string.Equals(job.Mode, "AllFresh", StringComparison.OrdinalIgnoreCase))
                {
                    DateTime newest = DateTime.MinValue, oldest = DateTime.MaxValue;
                    ready = true;
                    foreach (FlowResultSnapshot s in members)
                    {
                        if (s == null || s.Time <= rt.LastTrigger) { ready = false; break; }
                        if (s.Time > newest) newest = s.Time;
                        if (s.Time < oldest) oldest = s.Time;
                    }
                    if (ready && (newest - oldest).TotalMilliseconds > Math.Max(50, job.WindowMs)) ready = false;
                }
                else
                {
                    ready = current.Time > rt.LastTrigger;
                }

                if (!ready) continue;

                rt.LastTrigger = DateTime.Now;
                RunJob(job, BuildContext(job), current);
            }
        }

        private static bool JobContainsFlow(CommJobConfig job, FlowResultSnapshot snap)
        {
            if (job.Flows == null || job.Flows.Count == 0) return true;

            foreach (string f in job.Flows)
            {
                if (string.IsNullOrEmpty(f)) continue;
                if (string.Equals(f, snap.FlowId, StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(f, snap.FlowName, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static List<FlowResultSnapshot> ResolveMembers(CommJobConfig job)
        {
            var list = new List<FlowResultSnapshot>();
            if (job.Flows == null || job.Flows.Count == 0) return list;

            foreach (string f in job.Flows)
            {
                FlowResultSnapshot s = FindResult(f);
                if (s == null) return null;   // 有成员从未出结果：本批不完整
                list.Add(s);
            }
            return list;
        }

        private static FlowResultSnapshot FindResult(string flowIdOrName)
        {
            if (string.IsNullOrEmpty(flowIdOrName)) return null;

            FlowResultSnapshot s;
            if (ResultsById.TryGetValue(flowIdOrName, out s)) return s;
            if (ResultsByName.TryGetValue(flowIdOrName, out s)) return s;
            return null;
        }

        // =====================================================================
        //  定时 / 触发
        // =====================================================================

        private static bool _timerLoopLogged;

        private static void TimerLoop()
        {
            while (!_stop)
            {
                try
                {
                    Thread.Sleep(50);

                    // 非运行态（未点运行 / 已暂停 / 已停止）：整段跳过，不读配置、不动点位
                    if (!CommRuntime.IsActive) continue;

                    if (!_timerLoopLogged)
                    {
                        _timerLoopLogged = true;
                        int n;
                        lock (Sync) { n = _jobs.Count; }
                        Logger.Info("[comm-job] 定时任务循环已就绪：任务 " + n + " 个");
                    }

                    List<CommJobConfig> jobs;
                    lock (Sync) { jobs = new List<CommJobConfig>(_jobs); }

                    DateTime now = DateTime.Now;
                    foreach (CommJobConfig job in jobs)
                    {
                        if (job == null || !job.Enabled) continue;
                        if (!string.Equals(job.Trigger, "Timer", StringComparison.OrdinalIgnoreCase)) continue;

                        JobRuntime rt;
                        lock (Sync) { Runtimes.TryGetValue(job.Name, out rt); }
                        if (rt == null || now < rt.NextDue || rt.Running != 0) continue;

                        rt.NextDue = now.AddMilliseconds(Math.Max(50, job.IntervalMs));
                        RunJob(job, BuildContext(job));
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn("[comm-job] 定时任务循环异常：" + ex.Message);
                }
            }
        }

        private static bool _edgeLoopLogged;

        private static void EdgeLoop()
        {
            while (!_stop)
            {
                try
                {
                    Thread.Sleep(100);

                    if (!_edgeLoopLogged && CommRuntime.IsActive)
                    {
                        _edgeLoopLogged = true;
                        int n;
                        lock (Sync) { n = _jobs.Count; }
                        Logger.Info("[comm-job] 边沿触发循环已就绪：任务 " + n + " 个");
                    }

                    // 非运行态：不轮询触发位（旧版这里每 100ms 读一次点位，暂停后仍在刷）
                    if (!CommRuntime.IsActive) continue;

                    List<CommJobConfig> jobs;
                    lock (Sync) { jobs = new List<CommJobConfig>(_jobs); }

                    foreach (CommJobConfig job in jobs)
                    {
                        if (job == null || !job.Enabled) continue;
                        if (!string.Equals(job.Trigger, "PointEdge", StringComparison.OrdinalIgnoreCase)) continue;
                        if (string.IsNullOrEmpty(job.Point)) continue;

                        // ---- 多流程模式：一条任务驱动 N 条流程，按流程顺序分区 ----
                        // 第 i 条流程 = Point 的地址 + i × FlowStride。这样现场只配「第一套点位」，
                        // 新增流程只要在「流程管理」里排好序，地址自动跟上（见通讯点位界面说明）。
                        if (job.FlowStride > 0)
                        {
                            List<FlowDefinition> flows = CurrentFlows();
                            for (int i = 0; i < flows.Count; i++)
                            {
                                FlowDefinition flow = flows[i];
                                if (flow == null) continue;

                                string key = job.Name + "#" + i.ToString(CultureInfo.InvariantCulture);
                                bool now = ReadEdge(job, i * job.FlowStride);

                                bool prevOne;
                                EdgeStates.TryGetValue(key, out prevOne);
                                EdgeStates[key] = now;

                                if (!now || prevOne) continue;

                                OpResult tr = FlowRunManager.Trigger(flow.Id);
                                if (tr.Success)
                                    Logger.Info("[comm-job] 触发第 " + i + " 条流程「" + flow.Name + "」（"
                                                + job.Point + " + " + (i * job.FlowStride) + "）");
                                else
                                    Logger.Warn("[comm-job] 触发第 " + i + " 条流程「" + flow.Name + "」失败：" + tr.Message);
                            }
                            continue;
                        }

                        bool current = false;
                        OpResult<Framework.Communication.CommValue> r = CommHub.ReadPoint(job.Point);
                        if (r.Success && r.Value != null)
                        {
                            string expect = string.IsNullOrEmpty(job.Expect) ? "true" : job.Expect;
                            current = Matches(r.Value, expect);
                        }

                        bool previous;
                        EdgeStates.TryGetValue(job.Name, out previous);
                        EdgeStates[job.Name] = current;

                        if (current && !previous)
                            RunJob(job, BuildContext(job));
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn("[comm-job] 触发循环异常：" + ex.Message);
                }
            }
        }

        /// <summary>当前流程表（顺序即流程序号，与「流程管理」列表显示一致）。</summary>
        private static List<FlowDefinition> CurrentFlows()
        {
            try
            {
                FlowSettings fs = ConfigStore.Current.Flows;
                if (fs != null && fs.Flows != null) return fs.Flows;
            }
            catch (Exception) { }
            return new List<FlowDefinition>();
        }

        /// <summary>按地址偏移读触发位（偏移 0 = 原点位）。</summary>
        private static bool ReadEdge(CommJobConfig job, int addressOffset)
        {
            OpResult<Framework.Communication.CommValue> r = CommHub.ReadPointAt(job.Point, addressOffset);
            if (!r.Success || r.Value == null) return false;

            string expect = string.IsNullOrEmpty(job.Expect) ? "true" : job.Expect;
            return Matches(r.Value, expect);
        }

        private static bool Matches(Framework.Communication.CommValue value, string expected)
        {
            if (value == null) return false;
            if (value.Type == Framework.Communication.CommPointType.String)
                return (value.Text ?? "").IndexOf(expected ?? "", StringComparison.OrdinalIgnoreCase) >= 0;

            bool want = expected == "1" || string.Equals(expected, "true", StringComparison.OrdinalIgnoreCase);
            return value.AsBool() == want;
        }

        private static void RunStartupJobs()
        {
            List<CommJobConfig> jobs;
            lock (Sync) { jobs = new List<CommJobConfig>(_jobs); }

            foreach (CommJobConfig job in jobs)
            {
                if (job != null && job.Enabled &&
                    string.Equals(job.Trigger, "Startup", StringComparison.OrdinalIgnoreCase))
                    RunJob(job, BuildContext(job));
            }
        }

        private static void RunJob(CommJobConfig job, Dictionary<string, string> context,
                                   FlowResultSnapshot result = null)
        {
            // 闸门兜底：暂停瞬间已经在队列里的任务不再执行（避免暂停后还写一次点位）
            if (!CommRuntime.IsActive) return;

            JobRuntime rt;
            lock (Sync) { Runtimes.TryGetValue(job.Name, out rt); }
            if (rt == null) return;
            if (Interlocked.CompareExchange(ref rt.Running, 1, 0) != 0) return;

            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    ExecuteActions(job, context, result);
                    rt.LastError = "";
                }
                catch (Exception ex)
                {
                    rt.LastError = ex.Message;
                    Logger.Warn("[comm-job] 任务「" + job.Name + "」失败：" + ex.Message);
                }
                finally
                {
                    rt.RunCount++;
                    rt.LastRun = DateTime.Now;
                    Interlocked.Exchange(ref rt.Running, 0);
                }
            });
        }

        // =====================================================================
        //  动作
        // =====================================================================

        private static void ExecuteActions(CommJobConfig job, Dictionary<string, string> context,
                                           FlowResultSnapshot result)
        {
            string firstFailure = null;   // 本次执行第一个失败的动作（形如 "SetPoint：xxxx"）

            foreach (CommActionConfig action in job.Actions)
            {
                if (action == null) continue;

                string kind = (action.Kind ?? "").Trim();
                string target = CommTemplate.Render(action.Target, context);
                string value = CommTemplate.Render(action.Value, context);
                OpResult r;

                switch (kind)
                {
                    case "SetPoint":
                        r = SetPointAction(job, target, value);
                        break;

                    case "SendMessage":
                        r = CommHub.SendMessage(target, context);
                        break;

                    case "WaitPoint":
                        r = CommHub.WaitPoint(target, value, action.TimeoutMs);
                        break;

                    case "Sleep":
                    {
                        int ms;
                        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out ms);
                        if (ms > 0) Thread.Sleep(Math.Min(ms, 60000));
                        r = OpResult.Ok();
                        break;
                    }

                    case "StartFlow":
                        r = StartFlowAction(target);
                        break;

                    case "Log":
                        Logger.Info("[comm-job] " + job.Name + "：" + target + " " + value);
                        r = OpResult.Ok();
                        break;

                    case "WriteResult":
                        // 把本轮流程结果按「名:地址:类型」批量写进 PLC 寄存器。
                        // Target = 通讯名（如 PLC），Value = 映射串：
                        //   OkNg:20:UInt16; PosX:30:Int32; PosY:32:Int32; PosA:34:Float32
                        // 只有 OkNg / Passed / OkCount / NgCount / ElapsedMs / Fps 有真值，
                        // 其余名字一律写 0 —— 这正是"仅检测流程"要的：结论回传、坐标清零，
                        // 不让 PLC 里残留上一轮的旧坐标。
                        r = WriteResultAction(target, value, result);
                        break;

                    default:
                        r = OpResult.Fail("未知动作：" + kind);
                        break;
                }

                if (!r.Success)
                {
                    if (firstFailure == null) firstFailure = kind + "：" + (r.Message ?? "未知原因");
                    if (!action.ContinueOnError) break;
                }
            }

            // 【按整条任务收尾，不要逐动作收尾】心跳任务是「SetPoint 成功 + SendMessage 失败」：
            // 若逐个动作判成败，SetPoint 成功就会清掉告警标记，SendMessage 下一秒又告警一次 ——
            // 实测每秒一条、刷了 390 条把日志界面淹掉。这里只在"整条任务全成功"时才算恢复。
            JobRuntime rt;
            lock (Sync) { Runtimes.TryGetValue(job.Name, out rt); }
            if (rt == null) return;

            if (firstFailure != null)
            {
                rt.LastError = firstFailure;

                // 同一条错误只告警一次（链路没连上时每秒重试都是同一句），恢复时补一条 Info。
                if (!string.Equals(rt.LastWarnedError, firstFailure, StringComparison.Ordinal))
                {
                    rt.LastWarnedError = firstFailure;

                    // 「没有客户端连接」是 TCP 服务端在等对端接入，属预期等待态、不是故障：
                    // 记 Info 即可（现场看到黄色 WARN 会误以为通讯坏了）。其余失败仍记 WARN。
                    bool waitingPeer = firstFailure.IndexOf("没有客户端连接", StringComparison.Ordinal) >= 0;
                    string text = "[comm-job] 任务「" + job.Name + "」"
                                  + (waitingPeer ? "暂不可发：" : "失败：") + firstFailure
                                  + "（相同信息不再重复记录，恢复后自动提示）";
                    if (waitingPeer) Logger.Info(text); else Logger.Warn(text);
                }
            }
            else if (rt.LastWarnedError.Length > 0)
            {
                rt.LastWarnedError = "";
                rt.LastError = "";
                Logger.Info("[comm-job] 任务「" + job.Name + "」已恢复正常");
            }
        }

        /// <summary>
        /// 把流程结果按「名:地址:类型」批量写进 PLC 寄存器 —— 流程里<b>不需要</b>放「发送数据」节点。
        /// <para>
        /// 场景：流程只做检测（无脚本、无输出节点）。结论仍要回 PLC，位置/角度没有就写 0，
        /// 避免 PLC 里残留上一轮的旧坐标。因此：<c>OkNg</c>/<c>Passed</c>/<c>OkCount</c>/
        /// <c>NgCount</c>/<c>ElapsedMs</c>/<c>Fps</c> 取真值，<b>其余名字一律写 0</b>。
        /// </para>
        /// <para>Target = 通讯名（如 PLC）；Value = 映射串，与「发送数据」的 MbMap 同构：
        /// <c>OkNg:20:UInt16; PosX:30:Int32; PosY:32:Int32; PosA:34:Float32</c>。</para>
        /// </summary>
        private static OpResult WriteResultAction(string commName, string map, FlowResultSnapshot result)
        {
            if (string.IsNullOrWhiteSpace(commName))
                return OpResult.Fail("WriteResult：Target 要填通讯名（如 PLC）");
            if (string.IsNullOrWhiteSpace(map))
                return OpResult.Fail("WriteResult：Value 要填映射「名:地址:类型」，"
                                   + "例如 OkNg:20:UInt16;PosX:30:Int32");
            if (result == null)
                return OpResult.Fail("WriteResult：本次任务没有流程结果（它只能挂在「结果推送」任务上）");

            var done = new List<string>();
            foreach (string raw in map.Split(new[] { ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] cols = raw.Split(':');
                if (cols.Length < 2)
                    return OpResult.Fail("WriteResult：解析不了「" + raw.Trim() + "」（应为 名:地址:类型）");

                string name = cols[0].Trim();
                int addr;
                if (!int.TryParse(cols[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out addr)
                    || addr < 0 || addr > 65535)
                {
                    return OpResult.Fail("WriteResult：「" + name + "」地址需为 0~65535，当前："
                                       + cols[1].Trim());
                }

                string type = cols.Length > 2 && cols[2].Trim().Length > 0 ? cols[2].Trim() : "UInt16";
                ModbusDataType mdt;
                if (!TryParseResultType(type, out mdt))
                {
                    return OpResult.Fail("WriteResult：「" + name + "」类型「" + type
                        + "」不认识（可用 Int16/UInt16/Int32/UInt32/Float32/Float64）");
                }

                double value = ValueOfResult(result, name);
                string text = FormatResultValue(mdt, value);

                OpResult wr = CommRegistry.ModbusWriteAs(commName, mdt, (ushort)addr, text,
                                                         ModbusWordOrder.ABCD);
                if (!wr.Success)
                    return OpResult.Fail("WriteResult：" + name + "@" + addr + " 写入失败：" + wr.Message);

                done.Add(name + "@" + addr + "=" + text);
            }

            if (done.Count == 0) return OpResult.Fail("WriteResult：映射里没有任何有效项");

            Logger.Info("[comm-job] 结果回写 " + commName + " ← " + string.Join("、", done.ToArray()));
            return OpResult.Ok();
        }

        /// <summary>结果快照里可取的名字；未提供的一律 0（"坐标清零"语义，覆盖 PLC 里的旧值）。</summary>
        private static double ValueOfResult(FlowResultSnapshot r, string name)
        {
            if (string.Equals(name, "OkNg", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "Passed", StringComparison.OrdinalIgnoreCase))
                return r.Passed ? 1 : 0;
            if (string.Equals(name, "OkCount", StringComparison.OrdinalIgnoreCase)) return r.OkCount;
            if (string.Equals(name, "NgCount", StringComparison.OrdinalIgnoreCase)) return r.NgCount;
            if (string.Equals(name, "Total", StringComparison.OrdinalIgnoreCase)) return r.OkCount + r.NgCount;
            if (string.Equals(name, "ElapsedMs", StringComparison.OrdinalIgnoreCase)) return r.ElapsedMs;
            if (string.Equals(name, "Fps", StringComparison.OrdinalIgnoreCase)) return Math.Round(r.Fps, 1);
            return 0;
        }

        private static bool TryParseResultType(string text, out ModbusDataType type)
        {
            type = ModbusDataType.UInt16;
            switch ((text ?? "").Trim().ToUpperInvariant())
            {
                case "": case "UINT16": type = ModbusDataType.UInt16; return true;
                case "INT16": type = ModbusDataType.Int16; return true;
                case "INT32": type = ModbusDataType.Int32; return true;
                case "UINT32": type = ModbusDataType.UInt32; return true;
                case "FLOAT32": case "FLOAT": type = ModbusDataType.Float32; return true;
                case "FLOAT64": case "DOUBLE": type = ModbusDataType.Float64; return true;
                default: return false;
            }
        }

        private static string FormatResultValue(ModbusDataType type, double value)
        {
            if (type == ModbusDataType.Float32 || type == ModbusDataType.Float64)
                return value.ToString("0.######", CultureInfo.InvariantCulture);

            double rounded = Math.Round(value, MidpointRounding.AwayFromZero);
            return ((long)rounded).ToString(CultureInfo.InvariantCulture);
        }

        private static OpResult SetPointAction(CommJobConfig job, string target, string value)
        {
            if (string.IsNullOrEmpty(target)) return OpResult.Fail("SetPoint 目标为空");

            if (string.Equals(value, "toggle", StringComparison.OrdinalIgnoreCase))
            {
                string key = job.Name + "|" + target;
                bool current;
                lock (Sync) { ToggleStates.TryGetValue(key, out current); }
                bool next = !current;

                OpResult r = CommHub.SetPoint(target, next ? "true" : "false");
                if (r.Success) { lock (Sync) { ToggleStates[key] = next; } }
                return r;
            }

            return CommHub.SetPoint(target, value);
        }

        private static OpResult StartFlowAction(string flowIdOrName)
        {
            FlowDefinition flow = FindFlow(flowIdOrName);
            if (flow == null) return OpResult.Fail("找不到流程：" + flowIdOrName);

            // 【关键】外部触发只跑"一轮"：走 Trigger 而不是 StartOne。
            // StartOne 起的是连续运行的槽，一次触发会让流程永久循环 —— 这正是
            // 「不需要触发也在跑、而且一直在跑」的根因（v1.0.8.3 修正）。
            return FlowRunManager.Trigger(flow.Id);
        }

        private static FlowDefinition FindFlow(string idOrName)
        {
            if (string.IsNullOrEmpty(idOrName)) return null;

            FlowSettings fs = ConfigStore.Current.Flows;
            if (fs == null || fs.Flows == null) return null;

            foreach (FlowDefinition f in fs.Flows)
            {
                if (f != null && string.Equals(f.Id, idOrName, StringComparison.OrdinalIgnoreCase)) return f;
            }
            foreach (FlowDefinition f in fs.Flows)
            {
                if (f != null && string.Equals(f.Name, idOrName, StringComparison.OrdinalIgnoreCase)) return f;
            }
            return null;
        }

        // =====================================================================
        //  流程 Busy / Ready 绑定（多相机多流程，一条流程一份）
        // =====================================================================

        private static bool _bindingLoopLogged;

        private static void BindingLoop()
        {
            while (!_stop)
            {
                try
                {
                    Thread.Sleep(100);

                    // 非运行态：不写 Busy/Ready/Fault（暂停后不该继续动 PLC 的状态位）
                    if (!CommRuntime.IsActive) continue;

                    if (!_bindingLoopLogged)
                    {
                        _bindingLoopLogged = true;
                        int n;
                        lock (Sync) { n = _bindings.Count; }
                        Logger.Info("[comm-job] 流程状态绑定循环已就绪：绑定 " + n + " 条");
                    }

                    List<CommFlowBindingConfig> bindings;
                    lock (Sync) { bindings = new List<CommFlowBindingConfig>(_bindings); }

                    foreach (CommFlowBindingConfig b in bindings)
                    {
                        FlowDefinition flow = FindFlow(b.Flow);
                        string flowId = flow != null ? flow.Id : b.Flow;

                        FlowRunSlot slot = FlowRunManager.Get(flowId);
                        FlowSlotStatus status = slot != null ? slot.Status : FlowSlotStatus.Idle;

                        bool busy = status == FlowSlotStatus.Starting || status == FlowSlotStatus.Running
                                    || status == FlowSlotStatus.Paused || status == FlowSlotStatus.Stopping;
                        bool fault = status == FlowSlotStatus.Faulted;
                        bool ready = flow != null && flow.Enabled && !busy && !fault;

                        BindingState st;
                        if (!BindingStates.TryGetValue(b.Flow, out st)) st = new BindingState();
                        if (st.Initialized && st.Busy == busy && st.Ready == ready && st.Fault == fault) continue;

                        // 写失败不更新缓存：下个 100ms 继续重试（断线恢复后自动补状态）
                        // 【必须三条都写】用 &= 而不是 &&：某个点位失败不该让另外两个不写，
                        // 否则 Fault=1 会因为 Busy 写失败而被漏掉。
                        string firstError = null;
                        bool ok = WriteBindingPoint(b.BusyPoint, busy, ref firstError);
                        ok &= WriteBindingPoint(b.ReadyPoint, ready, ref firstError);
                        ok &= WriteBindingPoint(b.FaultPoint, fault, ref firstError);

                        if (!ok)
                        {
                            if (!st.WriteFailed)
                            {
                                st.WriteFailed = true;
                                // 【必须带原因】以前只写"写入失败"，现场连是"点位不存在"还是"未连接"都看不出来。
                                Logger.Warn("[comm-job] 流程「" + b.Flow + "」Busy/Ready 点位写入失败："
                                            + (firstError ?? "未知原因") + "（每 100ms 重试，恢复后自动补写）");
                            }

                            // 【必须存回】不存的话下一拍又 new 一个 WriteFailed=false 的临时状态，
                            // 日志会退化成每秒 10 条刷屏（现场 1.0.8.2 就是这样刷的）。
                            BindingStates[b.Flow] = st;
                            continue;
                        }

                        if (st.WriteFailed)
                            Logger.Info("[comm-job] 流程「" + b.Flow + "」Busy/Ready 点位写入已恢复");

                        st.WriteFailed = false;
                        st.Initialized = true;
                        st.Busy = busy;
                        st.Ready = ready;
                        st.Fault = fault;
                        BindingStates[b.Flow] = st;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn("[comm-job] 流程状态映射异常：" + ex.Message);
                }
            }
        }

        /// <summary>
        /// 写一个状态位点位。<paramref name="firstError"/> 只保留**第一条**失败原因（ref 传同一个变量），
        /// 便于把"到底是哪个点位为什么写不进"带进日志。
        /// </summary>
        private static bool WriteBindingPoint(string pointName, bool value, ref string firstError)
        {
            if (string.IsNullOrEmpty(pointName)) return true;

            OpResult r = CommHub.SetPoint(pointName, value ? "true" : "false");
            if (r.Success) return true;

            if (firstError == null) firstError = r.Message;
            return false;
        }

        // =====================================================================
        //  上下文
        // =====================================================================

        private static Dictionary<string, string> BuildContext(CommJobConfig job)
        {
            var ctx = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            ctx["Time"] = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            ctx["Date"] = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            int ok = 0, ng = 0;
            long cycle = 0;
            bool allPassed = true;

            lock (Sync)
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (FlowResultSnapshot s in ResultsById.Values)
                {
                    if (s == null || !seen.Add(s.FlowId)) continue;
                    ok += s.OkCount;
                    ng += s.NgCount;
                    if (s.CycleId > cycle) cycle = s.CycleId;
                }
            }

            ctx["OkCount"] = ok.ToString(CultureInfo.InvariantCulture);
            ctx["NgCount"] = ng.ToString(CultureInfo.InvariantCulture);
            ctx["Yield"] = (ok + ng) > 0
                ? (ok * 100.0 / (ok + ng)).ToString("F2", CultureInfo.InvariantCulture)
                : "0.00";
            ctx["CycleId"] = cycle.ToString(CultureInfo.InvariantCulture);

            if (job.Flows == null || job.Flows.Count == 0) return ctx;

            for (int i = 0; i < job.Flows.Count; i++)
            {
                FlowResultSnapshot s;
                lock (Sync) { s = FindResult(job.Flows[i]); }
                if (s == null) { allPassed = false; continue; }

                string alias = "Flow" + (i + 1).ToString(CultureInfo.InvariantCulture);
                ctx[alias] = string.IsNullOrEmpty(s.FlowName) ? s.FlowId : s.FlowName;
                ctx[alias + ".FlowId"] = s.FlowId;
                ctx[alias + ".FlowName"] = s.FlowName;
                ctx[alias + ".Passed"] = s.Passed ? "true" : "false";
                ctx[alias + ".OkNg"] = s.Passed ? "OK" : "NG";
                ctx[alias + ".ElapsedMs"] = s.ElapsedMs.ToString(CultureInfo.InvariantCulture);
                ctx[alias + ".Summary"] = s.Summary ?? "";
                ctx[alias + ".OkCount"] = s.OkCount.ToString(CultureInfo.InvariantCulture);
                ctx[alias + ".NgCount"] = s.NgCount.ToString(CultureInfo.InvariantCulture);
                ctx[alias + ".Yield"] = (s.OkCount + s.NgCount) > 0
                    ? (s.OkCount * 100.0 / (s.OkCount + s.NgCount)).ToString("F2", CultureInfo.InvariantCulture)
                    : "0.00";
                ctx[alias + ".Fps"] = s.Fps.ToString("F1", CultureInfo.InvariantCulture);

                if (!s.Passed) allPassed = false;
                if (i == 0) ctx["Flow"] = ctx[alias];
            }

            ctx["AllPassed"] = allPassed ? "true" : "false";
            return ctx;
        }

        // =====================================================================
        //  内部类型
        // =====================================================================

        private sealed class FlowResultSnapshot
        {
            public string FlowId;
            public string FlowName;
            public bool Passed;
            public int ElapsedMs;
            public string Summary;
            public long CycleId;
            public DateTime Time;
            public int OkCount;
            public int NgCount;
            public double Fps;
        }

        private sealed class JobRuntime
        {
            public CommJobConfig Config;
            public int Running;
            public long RunCount;
            public DateTime LastRun;
            public string LastError = "";
            /// <summary>已经写进日志的错误文本：同一条错误只记一次，避免每秒刷屏。</summary>
            public string LastWarnedError = "";
            public DateTime LastTrigger = DateTime.MinValue;
            public DateTime NextDue = DateTime.MinValue;
        }

        private sealed class BindingState
        {
            public bool Initialized;
            public bool Busy;
            public bool Ready;
            public bool Fault;
            public bool WriteFailed;
        }

        private static void OnConfigChanged(object sender, EventArgs e)
        {
            Reload();
        }

        private static void Join(Thread thread)
        {
            try { if (thread != null && thread.IsAlive) thread.Join(1000); }
            catch (Exception) { }
        }
    }
}
