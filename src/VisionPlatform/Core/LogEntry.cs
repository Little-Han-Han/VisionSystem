using System;
using System.Globalization;

namespace VisionPlatform.Core
{
    /// <summary>
    /// 日志级别。较第一轮的字符串匹配（<c>IndexOf("NG")</c>）改为<strong>枚举判定</strong>，
    /// 避免把「NG 出现在正文里」误判为结论行（共享知识 24′）。
    /// </summary>
    public enum LogLevel
    {
        /// <summary>普通信息。</summary>
        Info,

        /// <summary>警告（可继续运行）。</summary>
        Warn,

        /// <summary>错误（该条流程已无法继续）。</summary>
        Error,

        /// <summary>一次判定结论：OK。</summary>
        ResultOk,

        /// <summary>一次判定结论：NG。</summary>
        ResultNg
    }

    /// <summary>
    /// 一条结构化日志：时间 + 级别 + 归属流程名 + 内容。
    /// 流程名是本轮新增的关键维度——并行下必须能一眼看出这行日志属于哪条流程（FR-UI2-03）。
    /// </summary>
    public sealed class LogEntry
    {
        /// <summary>产生时间（本地时间）。</summary>
        public DateTime Time { get; set; }

        /// <summary>级别。</summary>
        public LogLevel Level { get; set; }

        /// <summary>归属流程名；"" = 全局 / 无流程归属。</summary>
        public string FlowName { get; set; }

        /// <summary>正文。</summary>
        public string Message { get; set; }

        public LogEntry()
        {
            Time = DateTime.Now;
            Level = LogLevel.Info;
            FlowName = "";
            Message = "";
        }

        public LogEntry(LogLevel level, string flowName, string message)
        {
            Time = DateTime.Now;
            Level = level;
            FlowName = flowName ?? "";
            Message = message ?? "";
        }

        /// <summary>级别的固定文本（INFO / WARN / ERROR / OK / NG），用于显示与导出。</summary>
        public string LevelText
        {
            get
            {
                switch (Level)
                {
                    case LogLevel.Warn:     return "WARN";
                    case LogLevel.Error:    return "ERROR";
                    case LogLevel.ResultOk: return "OK";
                    case LogLevel.ResultNg: return "NG";
                    default:                return "INFO";
                }
            }
        }

        /// <summary>
        /// 格式化为一行文本：<c>[HH:mm:ss][级别][流程名] 内容</c>。
        /// 流程名为空时省略该段（<c>[HH:mm:ss][级别] 内容</c>），避免出现空的方括号。
        /// </summary>
        public string Format()
        {
            string flow = FlowName ?? "";
            string body = Message ?? "";

            if (flow.Length == 0)
                return string.Format(CultureInfo.InvariantCulture, "[{0:HH:mm:ss}][{1}] {2}", Time, LevelText, body);

            return string.Format(CultureInfo.InvariantCulture, "[{0:HH:mm:ss}][{1}][{2}] {3}",
                Time, LevelText, flow, body);
        }

        public override string ToString()
        {
            return Format();
        }
    }
}
