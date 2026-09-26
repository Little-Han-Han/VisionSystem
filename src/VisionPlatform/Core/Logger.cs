using System;
using System.IO;
using System.Text;

namespace VisionPlatform.Core
{
    /// <summary>全局日志：同时写文件与向 UI 推送（订阅 <see cref="Logged"/>）。</summary>
    public static class Logger
    {
        private static readonly object Sync = new object();

        /// <summary>新日志产生时触发（参数为已格式化的整行文本）。</summary>
        public static event EventHandler<string> Logged;

        public static void Info(string message) { Write("INFO", message); }
        public static void Warn(string message) { Write("WARN", message); }
        public static void Error(string message) { Write("ERROR", message); }

        public static void Error(string message, Exception ex)
        {
            Write("ERROR", message + (ex != null ? (" | " + ex.Message) : ""));
        }

        private static void Write(string level, string message)
        {
            string line = string.Format("[{0:HH:mm:ss.fff}] [{1}] {2}", DateTime.Now, level, message);

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

            EventHandler<string> handler = Logged;
            if (handler != null) handler(null, line);
        }
    }
}
