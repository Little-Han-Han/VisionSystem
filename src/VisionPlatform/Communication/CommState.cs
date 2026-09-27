using System;
using System.Text;

namespace VisionPlatform.Communication
{
    /// <summary>
    /// 通讯模块的全局状态快照。
    /// 通讯窗体在连接 / 断开 / 出错时更新这里，主界面据此判断"通讯是否正常"，
    /// 避免各窗体之间互相持有引用。
    /// </summary>
    public static class CommState
    {
        /// <summary>TCP 客户端是否已连接。</summary>
        public static bool TcpClientConnected { get; set; }

        /// <summary>TCP 服务端是否在运行。</summary>
        public static bool TcpServerRunning { get; set; }

        /// <summary>Modbus TCP 是否已连接。</summary>
        public static bool ModbusConnected { get; set; }

        /// <summary>最近一次通讯异常；null 表示无异常。</summary>
        public static string LastError { get; set; }

        /// <summary>至少有一条通讯链路处于可用状态。</summary>
        public static bool HasActiveChannel
        {
            get { return TcpClientConnected || ModbusConnected || TcpServerRunning; }
        }

        /// <summary>记录一次通讯异常。</summary>
        public static void SetError(string message)
        {
            LastError = message;
        }

        /// <summary>清除异常标记。</summary>
        public static void ClearError()
        {
            LastError = null;
        }

        /// <summary>用于界面展示的状态文本。</summary>
        public static string Describe()
        {
            var sb = new StringBuilder();
            sb.Append("TCP客户端:").Append(TcpClientConnected ? "已连接" : "未连接");
            sb.Append("   TCP服务端:").Append(TcpServerRunning ? "运行中" : "未启动");
            sb.Append("   Modbus:").Append(ModbusConnected ? "已连接" : "未连接");

            if (!string.IsNullOrEmpty(LastError))
                sb.Append("   最近异常:").Append(LastError);

            return sb.ToString();
        }
    }
}
