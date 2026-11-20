using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace VisionPlatform.Core
{
    /// <summary>
    /// 运行时性能归因探针（默认关闭，只有设置环境变量 <c>VA_PERF_STATS=1</c> 才启动）。
    /// <para>
    /// 【为什么需要它】"连续运行 CPU 高"这句话里没有信息 —— 可能是算法、可能是相机 SDK 的回调线程、
    /// 可能是界面缩放、也可能是日志。改代码之前必须先把它拆开，否则只能靠猜，改十次也不一定中。
    /// 本探针每 5 秒往日志里写一行，把 CPU 拆成四块，并且<strong>按线程</strong>列出谁在烧：
    /// </para>
    /// <code>
    /// [性能] 5.0s｜进程 CPU 62.4%（≈1.25 核 / 8 核）｜产出 148 帧（29.6/s，算法均 34.2ms）
    ///       ｜界面重绘 74 次（14.8/s，建层均 14.9ms）｜日志 152 行｜GC 12/0/0 堆 82MB
    ///       ｜线程：流程:流程 1 48.1%｜界面 9.2%｜采集 4.4%｜日志 0.3%
    /// </code>
    /// <para>
    /// 【怎么读】"算法均 ms × 帧/s ÷ 1000 ≈ CPU 核数" 时，CPU 主要花在算法上，界面优化再多也没用；
    /// 只有"界面重绘次数 × 建层 ms"占比明显时，界面才是主因。
    /// </para>
    /// </summary>
    internal static class PerfProbe
    {
        // ---------------------- Win32：按线程取 CPU 时间 ----------------------

        private const uint ThreadQueryLimitedInformation = 0x0800;

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentThread();

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenThread(uint desiredAccess, bool inheritHandle, uint threadId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetThreadTimes(IntPtr handle,
            out long creationTime, out long exitTime, out long kernelTime, out long userTime);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        /// <summary>一个被跟踪的线程：名字 + 线程 Id + 上次采到的 CPU 时间。</summary>
        private sealed class TrackedThread
        {
            public string Name;
            public uint ThreadId;
            public long LastMs;
            public bool Alive = true;
        }

        // ---------------------- 状态 ----------------------

        private static readonly object Sync = new object();
        private static readonly List<TrackedThread> Threads = new List<TrackedThread>();

        private static bool _enabled;
        private static volatile bool _running;
        private static Thread _thread;
        private static int _intervalMs = 5000;

        private static TimeSpan _lastProcessCpu;
        private static int _lastStamp;

        private static long _frames;
        private static long _frameMs;
        private static long _paintSeen;
        private static long _logs;

        /// <summary>界面重绘的来源由宿主提供（控件在另一个程序集，探针不反向依赖）。</summary>
        private static Func<long> _paintCount;
        private static Func<long> _rebuildCount;
        private static Func<double> _rebuildMs;

        private static long _lastRebuildCount;
        private static double _lastRebuildMs;

        /// <summary>全部线程的上次 CPU 时间（毫秒），用于做区间增量普查。</summary>
        private static Dictionary<uint, long> _allThreadMs = new Dictionary<uint, long>();

        /// <summary>环境变量是否开启（宿主用来决定要不要挂计数器）。</summary>
        public static bool Enabled { get { return _enabled; } }

        // ---------------------- 对外接口 ----------------------

        /// <summary>
        /// 设置界面重绘的来源：重绘次数 + 建层次数 + 建层累计耗时（都是累计值，探针取区间增量）。
        /// 用"累计耗时增量 / 建层次数"得到的才是真实的平均单帧代价。
        /// </summary>
        public static void SetPaintSource(Func<long> paintCount, Func<long> rebuildCount, Func<double> rebuildTotalMs)
        {
            _paintCount = paintCount;
            _rebuildCount = rebuildCount;
            _rebuildMs = rebuildTotalMs;
        }

        /// <summary>登记"当前线程"，之后按线程统计 CPU 占比。</summary>
        public static void Mark(string name)
        {
            if (!_enabled || string.IsNullOrEmpty(name)) return;

            try
            {
                uint tid = GetCurrentThreadId();
                lock (Sync)
                {
                    for (int i = 0; i < Threads.Count; i++)
                    {
                        if (Threads[i].ThreadId == tid)
                        {
                            Threads[i].Name = name;   // 同一线程换名字（例如槽被复用）
                            return;
                        }
                    }

                    Threads.Add(new TrackedThread { Name = name, ThreadId = tid, LastMs = ReadThreadCpuMs(tid) });
                }
            }
            catch (Exception) { }
        }

        /// <summary>计数：产出一帧结果（带算法耗时）。</summary>
        public static void CountFrame(int elapsedMs)
        {
            if (!_enabled) return;
            Interlocked.Increment(ref _frames);
            Interlocked.Add(ref _frameMs, elapsedMs);
        }

        /// <summary>计数：写了一条日志。</summary>
        public static void CountLog()
        {
            if (!_enabled) return;
            Interlocked.Increment(ref _logs);
        }

        /// <summary>启动探针线程。</summary>
        public static void Start()
        {
            _enabled = Environment.GetEnvironmentVariable("VA_PERF_STATS") == "1";
            if (!_enabled || _running) return;

            string raw = Environment.GetEnvironmentVariable("VA_PERF_STATS_INTERVAL");
            int ms;
            if (int.TryParse(raw, out ms) && ms >= 1000 && ms <= 60000) _intervalMs = ms;

            _running = true;
            _lastProcessCpu = SafeTotalCpu();
            _lastStamp = Environment.TickCount;

            _thread = new Thread(Loop)
            {
                IsBackground = true,
                Name = "PerfProbe",
                Priority = ThreadPriority.BelowNormal
            };
            _thread.Start();

            Logger.Info("[性能] 归因探针已启动：每 " + (_intervalMs / 1000.0).ToString("F0") + " 秒一行（VA_PERF_STATS=1）");
        }

        /// <summary>停止探针（退出时调用）。</summary>
        public static void Stop()
        {
            _running = false;
            try { if (_thread != null) _thread.Join(500); }
            catch (Exception) { }
        }

        // ---------------------- 内部 ----------------------

        private static void Loop()
        {
            while (_running)
            {
                for (int i = 0; i < _intervalMs / 100 && _running; i++)
                    Thread.Sleep(100);

                if (!_running) break;

                try { Report(); }
                catch (Exception ex) { Logger.Warn("[性能] 统计异常：" + ex.Message); }
            }
        }

        private static void Report()
        {
            int now = Environment.TickCount;
            double seconds = unchecked(now - _lastStamp) / 1000.0;
            if (seconds <= 0.2) seconds = 0.2;
            _lastStamp = now;

            // ① 进程 CPU
            TimeSpan cpu = SafeTotalCpu();
            double cpuMs = (cpu - _lastProcessCpu).TotalMilliseconds;
            _lastProcessCpu = cpu;
            int cores = Environment.ProcessorCount;
            double cpuPercent = cpuMs / (seconds * 1000.0) / Math.Max(1, cores) * 100.0;
            double coreEquiv = cpuMs / (seconds * 1000.0);

            // ② 产出 / 算法 / 界面 / 日志
            long frames = Interlocked.Exchange(ref _frames, 0);
            long frameMs = Interlocked.Exchange(ref _frameMs, 0);
            long logs = Interlocked.Exchange(ref _logs, 0);

            long paints = 0;
            long paintDelta = 0;
            long rebuilds = 0;
            double rebuildMs = 0;
            double avgRebuildMs = 0;
            if (_paintCount != null)
            {
                try
                {
                    paints = _paintCount();
                    paintDelta = Math.Max(0, paints - _paintSeen);
                    _paintSeen = paints;

                    if (_rebuildCount != null && _rebuildMs != null)
                    {
                        long total = _rebuildCount();
                        double ms = _rebuildMs();
                        rebuilds = Math.Max(0, total - _lastRebuildCount);
                        rebuildMs = Math.Max(0, ms - _lastRebuildMs);
                        _lastRebuildCount = total;
                        _lastRebuildMs = ms;
                        if (rebuilds > 0) avgRebuildMs = rebuildMs / rebuilds;
                    }
                }
                catch (Exception) { }
            }

            // ③ 线程 CPU 普查：**枚举进程里所有线程**，不只登记过的。
            //    "CPU 高但不知道是谁" 时，这一步最能说明问题（往往是算法库/推理库自己开的线程池）。
            var threadText = new StringBuilder();
            int listed = 0;

            var named = new Dictionary<uint, string>();
            lock (Sync)
            {
                foreach (TrackedThread t in Threads) named[t.ThreadId] = t.Name;
            }

            var samples = new List<KeyValuePair<string, double>>();
            var seen = new HashSet<uint>();
            int threadCount = 0;
            Process proc = null;
            try { proc = Process.GetCurrentProcess(); }
            catch (Exception) { }

            if (proc != null)
            {
                foreach (ProcessThread pt in proc.Threads)
                {
                    uint tid = (uint)pt.Id;
                    seen.Add(tid);

                    long ms = ReadThreadCpuMs(tid);
                    if (ms < 0) continue;

                    long last;
                    if (!_allThreadMs.TryGetValue(tid, out last))
                    {
                        _allThreadMs[tid] = ms;      // 首次见到：记录基线，本次不报
                        continue;
                    }
                    _allThreadMs[tid] = ms;

                    long delta = ms - last;
                    if (delta <= 0) continue;

                    double pct = delta / (seconds * 1000.0) / Math.Max(1, cores) * 100.0;
                    if (pct < 0.8) continue;

                    threadCount++;
                    string name;
                    if (!named.TryGetValue(tid, out name))
                    {
                        // 没登记过的线程：用"启动地址所属模块"归类，
                        // 这样能一眼看出是 opencv / openvino / tbb 还是 CLR 自己开的
                        name = ModuleOf(proc, pt) + "#" + tid;
                    }
                    samples.Add(new KeyValuePair<string, double>(name, pct));
                }
            }

            // 清掉已退出线程的采样，避免字典无限增长
            if (_allThreadMs.Count > 512)
            {
                var alive = new Dictionary<uint, long>();
                foreach (var kv in _allThreadMs)
                    if (seen.Contains(kv.Key)) alive[kv.Key] = kv.Value;
                _allThreadMs = alive;
            }

            samples.Sort((a, b) => b.Value.CompareTo(a.Value));
            double namedSum = 0;
            for (int i = 0; i < samples.Count; i++) namedSum += samples[i].Value;
            int shown = 0;
            for (int i = 0; i < samples.Count && i < 8; i++)
            {
                if (shown++ > 0) threadText.Append('｜');
                threadText.Append(samples[i].Key).Append(' ').Append(samples[i].Value.ToString("F1")).Append('%');
            }
            if (listed >= 0)
            {
                threadText.Append("｜线程共 ").Append(threadCount).Append(" 个，合计 ")
                          .Append(namedSum.ToString("F1")).Append('%');
            }

            double avgAlgo = frames > 0 ? (double)frameMs / frames : 0;
            double algoCores = frames * avgAlgo / 1000.0 / seconds;
            double uiCores = rebuildMs / 1000.0 / seconds;

            string line = string.Format(CultureInfo.InvariantCulture,
                "[性能] {0:F1}s｜进程 CPU {1:F1}%（≈{2:F2} 核 / {3} 核）"
                + "｜产出 {4} 帧（{5:F1}/s，算法均 {6:F1}ms ⇒ 约 {7:F2} 核）"
                + "｜界面重绘 {8} 次（{9:F1}/s），重建层 {11} 次（均 {10:F1}ms ⇒ 约 {18:F2} 核）"
                + "｜日志 {12} 行｜GC {13}/{14}/{15} 堆 {16}MB{17}",
                seconds, cpuPercent, coreEquiv, cores,
                frames, frames / seconds, avgAlgo, algoCores,
                paintDelta, paintDelta / seconds, avgRebuildMs, rebuilds,
                logs,
                GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2),
                GC.GetTotalMemory(false) / (1024 * 1024),
                threadText.Length > 0 ? ("｜线程：" + threadText) : "",
                uiCores);

            Logger.Info(line);
        }

        /// <summary>线程启动地址落在哪个模块里（用于给"无名线程"归类）。取不到返回 "?"。</summary>
        private static string ModuleOf(Process proc, ProcessThread pt)
        {
            try
            {
                IntPtr start = pt.StartAddress;
                if (start == IntPtr.Zero) return "?";

                long addr = start.ToInt64();
                ProcessModuleCollection mods = proc.Modules;
                for (int i = 0; i < mods.Count; i++)
                {
                    ProcessModule m = mods[i];
                    long b = m.BaseAddress.ToInt64();
                    if (addr >= b && addr < b + m.ModuleMemorySize)
                    {
                        string n = m.ModuleName ?? "?";
                        return n.Replace(".dll", "").Replace(".DLL", "");
                    }
                }
            }
            catch (Exception) { }

            return "?";
        }

        private static TimeSpan SafeTotalCpu()
        {
            try { return Process.GetCurrentProcess().TotalProcessorTime; }
            catch (Exception) { return TimeSpan.Zero; }
        }

        /// <summary>读某线程累计 CPU 毫秒；线程已退出返回 -1。</summary>
        private static long ReadThreadCpuMs(uint threadId)
        {
            IntPtr handle = OpenThread(ThreadQueryLimitedInformation, false, threadId);
            if (handle == IntPtr.Zero) return -1;

            try
            {
                long creation, exit, kernel, user;
                if (!GetThreadTimes(handle, out creation, out exit, out kernel, out user)) return -1;
                return (kernel + user) / 10000;   // 100ns → ms
            }
            catch (Exception)
            {
                return -1;
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        /// <summary>供调用方判断：当前线程是否已被登记（避免重复 Mark 造成误判）。</summary>
        public static bool IsMarked
        {
            get
            {
                if (!_enabled) return true;
                uint tid = GetCurrentThreadId();
                lock (Sync)
                {
                    for (int i = 0; i < Threads.Count; i++)
                        if (Threads[i].ThreadId == tid) return true;
                }
                return false;
            }
        }
    }
}
