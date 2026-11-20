using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;

namespace VisionPlatform.Core
{
    /// <summary>
    /// 全局日志：同时写文件与向 UI 推送。
    ///
    /// 【零回归】既有 <see cref="Logged"/>（<c>EventHandler&lt;string&gt;</c>）与其既有订阅者
    /// （MainForm.OnLogged）**完整保留、行为不变**；本轮只是<strong>新增</strong>结构化的
    /// <see cref="EntryLogged"/> 供新的 <c>LogPanel</c> 消费。
    /// </summary>
    public static class Logger
    {
        private static readonly object Sync = new object();

        // ---- 异步文件写入（每帧 RESULT 日志不再同步开关文件）----
        // 同步 File.AppendAllText 实测 ~6.5ms/条（每次 open/close + 杀软扫描），
        // 30fps 下就是 20% 的帧时间；这里改为"入队 + 后台批量追加"，
        // 仍然保留 Logged / EntryLogged 同步事件（UI 与日志库行为不变）。
        private static readonly ConcurrentQueue<string> FileQueue = new ConcurrentQueue<string>();
        private static readonly AutoResetEvent FileSignal = new AutoResetEvent(false);
        private static readonly object FileSync = new object();
        private static readonly Thread FileThread;
        private static volatile bool _fileStop;

        static Logger()
        {
            FileThread = new Thread(delegate() { PerfProbe.Mark("日志写盘"); FileLoop(); })
            {
                IsBackground = true,
                Name = "LoggerFileWriter"
            };
            FileThread.Start();
        }

        /// <summary>新日志产生时触发（参数为已格式化的整行文本）。**既有事件，保留不动**。</summary>
        public static event EventHandler<string> Logged;

        /// <summary>新日志产生时触发（结构化条目，供 LogPanel 分级着色 / 过滤 / 导出）。</summary>
        public static event EventHandler<LogEntry> EntryLogged;

        // ---------------------- 既有签名（保留）----------------------

        public static void Info(string message) { Write(LogLevel.Info, "", message); }
        public static void Warn(string message) { Write(LogLevel.Warn, "", message); }
        public static void Error(string message) { Write(LogLevel.Error, "", message); }

        public static void Error(string message, Exception ex)
        {
            Write(LogLevel.Error, "", message, ex != null ? ex.ToString() : null);
        }

        // ---------------------- 本轮新增：带流程名 ----------------------

        /// <summary>带归属流程名的 INFO。</summary>
        public static void Info(string flowName, string message) { Write(LogLevel.Info, flowName, message); }

        /// <summary>带归属流程名的 WARN。</summary>
        public static void Warn(string flowName, string message) { Write(LogLevel.Warn, flowName, message); }

        /// <summary>带归属流程名的 ERROR。</summary>
        public static void Error(string flowName, string message) { Write(LogLevel.Error, flowName, message); }

        /// <summary>带归属流程名的 ERROR（附异常，完整堆栈写入 Detail 供日志库排查）。</summary>
        public static void Error(string flowName, string message, Exception ex)
        {
            Write(LogLevel.Error, flowName, message, ex != null ? ex.ToString() : null);
        }

        /// <summary>
        /// 一次判定结论。并行下 RESULT 行可达 90 行/秒，UI 侧必须批量拉取（见 LogPanel，共享知识 31）。
        /// </summary>
        /// <param name="flowName">归属流程名。</param>
        /// <param name="passed">true = OK，false = NG。</param>
        /// <param name="message">结论正文（建议含关键值与耗时）。</param>
        public static void Result(string flowName, bool passed, string message)
        {
            Write(passed ? LogLevel.ResultOk : LogLevel.ResultNg, flowName, message);
        }

        // ---------------------- 内部 ----------------------

        private static void Write(LogLevel level, string flowName, string message)
        {
            Write(level, flowName, message, null);
        }

        private static void Write(LogLevel level, string flowName, string message, string detail)
        {
            var entry = new LogEntry(level, flowName, message, detail);
            string line = entry.Format();

            // 性能探针只关心"写了多少行"，开销是一次自增
            PerfProbe.CountLog();

            // ERROR 必须尽快落盘（崩溃现场就靠它）：先把已有队列刷完，再同步追加。
            // 其余级别入队由后台线程批量写，单条成本从 ~6.5ms 降到微秒级。
            if (level == LogLevel.Error)
            {
                Flush();
                AppendText(line + Environment.NewLine);
            }
            else
            {
                FileQueue.Enqueue(line);
                FileSignal.Set();
            }

            // 既有订阅者（MainForm.OnLogged）保持收到过去完全一致的整行文本
            try
            {
                EventHandler<string> legacy = Logged;
                if (legacy != null) legacy(null, line);
            }
            catch (Exception)
            {
                // 单个订阅者异常不影响其它订阅者
            }

            try
            {
                EventHandler<LogEntry> structured = EntryLogged;
                if (structured != null) structured(null, entry);
            }
            catch (Exception)
            {
                // 单个订阅者异常不影响其它订阅者
            }
        }

        // ---------------------- 异步文件写入 ----------------------

        /// <summary>等待已入队的日志全部落盘（退出 / 崩溃前调用）。</summary>
        public static void Flush()
        {
            for (int i = 0; i < 200 && !FileQueue.IsEmpty; i++)
            {
                FileSignal.Set();
                Thread.Sleep(5);
            }
            lock (FileSync) { }   // 等正在进行的 Append 结束
        }

        /// <summary>停止后台写入线程并做最后一次落盘（程序退出时调用）。</summary>
        public static void Shutdown()
        {
            _fileStop = true;
            FileSignal.Set();

            try { if (FileThread != null) FileThread.Join(1500); }
            catch (Exception) { }

            DrainAll();
        }

        private static void FileLoop()
        {
            while (!_fileStop)
            {
                FileSignal.WaitOne(200);   // 200ms 一批：既低延迟又不频繁开关文件
                DrainAll();
            }
            DrainAll();
        }

        private static void DrainAll()
        {
            if (FileQueue.IsEmpty) return;

            var sb = new StringBuilder();
            string line;
            int n = 0;
            while (n < 2000 && FileQueue.TryDequeue(out line))
            {
                sb.AppendLine(line);
                n++;
            }
            if (n == 0) return;

            AppendText(sb.ToString());
        }

        private static void AppendText(string text)
        {
            try
            {
                AppPaths.Ensure();
                lock (FileSync)
                {
                    File.AppendAllText(AppPaths.LogFile, text, Encoding.UTF8);
                }
            }
            catch (Exception)
            {
                // 日志写失败不应影响主流程
            }
        }
    }
}
