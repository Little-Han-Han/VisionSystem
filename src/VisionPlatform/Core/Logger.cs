using System;
using System.IO;
using System.Text;

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
            Write(LogLevel.Error, "", message + (ex != null ? (" | " + ex.Message) : ""));
        }

        // ---------------------- 本轮新增：带流程名 ----------------------

        /// <summary>带归属流程名的 INFO。</summary>
        public static void Info(string flowName, string message) { Write(LogLevel.Info, flowName, message); }

        /// <summary>带归属流程名的 WARN。</summary>
        public static void Warn(string flowName, string message) { Write(LogLevel.Warn, flowName, message); }

        /// <summary>带归属流程名的 ERROR。</summary>
        public static void Error(string flowName, string message) { Write(LogLevel.Error, flowName, message); }

        /// <summary>带归属流程名的 ERROR（附异常）。</summary>
        public static void Error(string flowName, string message, Exception ex)
        {
            Write(LogLevel.Error, flowName, message + (ex != null ? (" | " + ex.Message) : ""));
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
            var entry = new LogEntry(level, flowName, message);
            string line = entry.Format();

            try
            {
                AppPaths.Ensure();
                lock (Sync)
                {
                    File.AppendAllText(AppPaths.LogFile, line + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch (Exception)
            {
                // 日志写失败不应影响主流程
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
    }
}
