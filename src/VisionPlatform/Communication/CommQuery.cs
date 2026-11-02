using System;
using System.Threading;
using VisionPlatform.Core;

namespace VisionPlatform.Communication
{
    /// <summary>
    /// 「发一条指令并等一条回复」的同步请求辅助类（<see cref="CommRegistry"/> 的薄封装）。
    /// <para>
    /// 【为什么单独一个类，而不改 CommRegistry】<see cref="CommRegistry"/> 的既有语义是
    /// 「配置 + 活连接 + 最近一条收到的文本」，流程节点按事件异步消费；
    /// 标定窗体需要的是「发出去 → 等到回复 → 解析」这种一问一答的同步节奏。
    /// 在注册表里加同步阻塞方法会污染它的线程模型（它所有共享状态都在 <c>lock (Sync)</c> 里，
    /// 阻塞等待期间若持锁就会把并行流程全部堵死）。因此这里<b>只用它的公开 API</b>
    /// （<see cref="CommRegistry.IsConnected"/> / <see cref="CommRegistry.Connect"/> /
    /// <see cref="CommRegistry.SendText"/> / <see cref="CommRegistry.LastReceived"/> /
    /// <see cref="CommRegistry.Changed"/>）拼装，一行既有代码都没改。
    /// </para>
    /// <para>
    /// 【线程】事件由后台线程抛出，等待在调用线程（UI 线程）上进行；
    /// 只要没有「新数据」就按时超时返回，绝不无限阻塞界面。
    /// </para>
    /// </summary>
    public static class CommQuery
    {
        /// <summary>默认等待回复的时长（毫秒）。</summary>
        public const int DefaultTimeoutMs = 2000;

        /// <summary>
        /// 向指定名称的通讯发送一条文本指令，并等待「一条与发送前不同的新数据」。
        /// </summary>
        /// <param name="name">通讯名称（见 <see cref="CommRegistry.Names"/>）。</param>
        /// <param name="command">要发送的文本指令（如 <c>POS?</c>）。</param>
        /// <param name="timeoutMs">等待回复的超时毫秒数；≤0 时用 <see cref="DefaultTimeoutMs"/>。</param>
        /// <returns>成功时 <c>Value</c> 为收到的原始文本；失败时 <c>Message</c> 可直接显示。</returns>
        public static OpResult<string> SendAndWaitReply(string name, string command, int timeoutMs)
        {
            if (string.IsNullOrEmpty(name)) return OpResult<string>.Fail("请先选择一个通讯");
            if (string.IsNullOrWhiteSpace(command)) return OpResult<string>.Fail("发送指令为空");

            int wait = timeoutMs > 0 ? timeoutMs : DefaultTimeoutMs;

            // 未连接时先尝试连接一次（与 ModbusRead 的「用前自动连」行为一致）
            if (!CommRegistry.IsConnected(name))
            {
                OpResult connectResult = CommRegistry.Connect(name);
                if (!connectResult.Success)
                    return OpResult<string>.Fail("通讯「" + name + "」不可用：" + connectResult.Message);
            }

            // 发送前的基准文本：只把「变化」当成回复，避免把历史数据当成本次回复
            string baseline = CommRegistry.LastReceived(name) ?? "";

            string captured = null;
            object gate = new object();

            using (ManualResetEventSlim arrived = new ManualResetEventSlim(false))
            {
                EventHandler onChanged = delegate
                {
                    string current = CommRegistry.LastReceived(name) ?? "";
                    if (string.Equals(current, baseline, StringComparison.Ordinal)) return;  // 只是连接状态变化

                    lock (gate)
                    {
                        if (captured == null) captured = current;
                    }
                    arrived.Set();
                };

                CommRegistry.Changed += onChanged;
                try
                {
                    OpResult sent = CommRegistry.SendText(name, command);
                    if (!sent.Success) return OpResult<string>.Fail("发送失败：" + sent.Message);

                    if (!arrived.Wait(wait))
                    {
                        // 超时边界：万一事件在 Wait 返回前刚好到过，这里再取一次，避免误判超时
                        string current = CommRegistry.LastReceived(name) ?? "";
                        if (!string.IsNullOrEmpty(current) && !string.Equals(current, baseline, StringComparison.Ordinal))
                            return OpResult<string>.Ok(current, "已收到回复");

                        return OpResult<string>.Fail("等待「" + name + "」回复超时（" + wait + " ms）");
                    }
                }
                finally
                {
                    CommRegistry.Changed -= onChanged;
                }
            }

            lock (gate)
            {
                if (string.IsNullOrEmpty(captured))
                    return OpResult<string>.Fail("通讯「" + name + "」回复为空");
                return OpResult<string>.Ok(captured, "已收到回复");
            }
        }

        /// <summary>用默认超时发送指令并等待回复。</summary>
        public static OpResult<string> SendAndWaitReply(string name, string command)
        {
            return SendAndWaitReply(name, command, DefaultTimeoutMs);
        }
    }
}
