using System;
using System.Collections.Generic;
using System.Threading;
using VisionPlatform.Framework.Communication;

namespace VisionPlatform.Communication
{
    /// <summary>
    /// 每个端点一条发送队列：保证同一连接的报文按顺序、整帧写出，
    /// 避免多条流程/任务并发写同一 Socket 时字节交错。
    /// <para>优先级：0 = 报警/结果，1 = 普通，2 = 心跳；低优先级不会插到高优先级前面。</para>
    /// </summary>
    internal sealed class CommSendQueue : IDisposable
    {
        public const int PriorityHigh = 0;
        public const int PriorityNormal = 1;
        public const int PriorityLow = 2;

        private sealed class Work
        {
            public Func<CommResult> Action;
            public CommResult Result;
            public readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);
        }

        private readonly object _sync = new object();
        private readonly Queue<Work>[] _queues =
        {
            new Queue<Work>(), new Queue<Work>(), new Queue<Work>()
        };

        private readonly Thread _worker;
        private volatile bool _stop;
        private readonly string _name;

        public CommSendQueue(string name)
        {
            _name = name ?? "";
            _worker = new Thread(Loop) { IsBackground = true, Name = "CommSend-" + _name };
            _worker.Start();
        }

        /// <summary>入队并等待结果；timeoutMs ≤ 0 表示一直等。</summary>
        public CommResult Run(Func<CommResult> action, int priority, int timeoutMs)
        {
            if (action == null) return CommResult.Fail("发送动作为空");
            if (_stop) return CommResult.Fail("通讯队列已停止");

            if (priority < 0) priority = 0;
            if (priority > 2) priority = 2;

            var work = new Work { Action = action };

            lock (_sync)
            {
                _queues[priority].Enqueue(work);
                Monitor.Pulse(_sync);
            }

            if (!work.Done.Wait(timeoutMs > 0 ? timeoutMs : Timeout.Infinite))
                return CommResult.Fail("通讯「" + _name + "」发送超时（队列积压）");

            return work.Result ?? CommResult.Fail("通讯「" + _name + "」发送失败");
        }

        public void Dispose()
        {
            _stop = true;
            lock (_sync) { Monitor.PulseAll(_sync); }

            try { if (_worker.IsAlive) _worker.Join(1000); }
            catch (Exception) { }

            // 唤醒所有等待者，避免界面/任务线程永久挂起
            lock (_sync)
            {
                foreach (Queue<Work> q in _queues)
                {
                    while (q.Count > 0)
                    {
                        Work w = q.Dequeue();
                        w.Result = CommResult.Fail("通讯「" + _name + "」已停止");
                        w.Done.Set();
                    }
                }
            }
        }

        private void Loop()
        {
            while (true)
            {
                Work work = null;

                lock (_sync)
                {
                    while (!_stop && TotalCount() == 0)
                        Monitor.Wait(_sync, 200);

                    if (_stop) break;

                    for (int p = 0; p < _queues.Length; p++)
                    {
                        if (_queues[p].Count > 0) { work = _queues[p].Dequeue(); break; }
                    }
                }

                if (work == null) continue;

                try
                {
                    work.Result = work.Action();
                }
                catch (Exception ex)
                {
                    work.Result = CommResult.Fail("通讯异常：" + ex.Message);
                }
                finally
                {
                    try { work.Done.Set(); }
                    catch (Exception) { }
                }
            }
        }

        private int TotalCount()
        {
            int n = 0;
            foreach (Queue<Work> q in _queues) n += q.Count;
            return n;
        }
    }
}
