using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using VisionPlatform.Core;

namespace VisionPlatform.Communication
{
    /// <summary>
    /// TCP 服务端：监听端口，接受多个客户端连接，支持广播与按连接回发。
    /// 常用于把检测结果主动推送给 MES / 上位机。
    /// </summary>
    public sealed class TcpServerSession : IDisposable
    {
        private readonly object _sync = new object();
        private readonly List<TcpClient> _clients = new List<TcpClient>();

        private TcpListener _listener;
        private Thread _acceptThread;
        private volatile bool _stopFlag;

        public bool IsRunning { get; private set; }
        public int Port { get; private set; }

        /// <summary>客户端数量。</summary>
        public int ClientCount { get { lock (_sync) { return _clients.Count; } } }

        /// <summary>收到客户端数据（参数为文本）。</summary>
        public event EventHandler<string> Received;

        /// <summary>状态变化。</summary>
        public event EventHandler<string> StatusChanged;

        public OpResult Start(int port)
        {
            if (IsRunning) return OpResult.Ok("已在监听");

            try
            {
                _stopFlag = false;
                Port = port;
                _listener = new TcpListener(IPAddress.Any, port);
                _listener.Start();
                IsRunning = true;

                _acceptThread = new Thread(AcceptLoop);
                _acceptThread.IsBackground = true;
                _acceptThread.Name = "TcpServerAccept";
                _acceptThread.Start();

                RaiseStatus("开始监听 " + port);
                Logger.Info("TCP 服务端开始监听端口 " + port);
                return OpResult.Ok("监听端口 " + port);
            }
            catch (Exception ex)
            {
                IsRunning = false;
                return OpResult.Fail("启动监听失败：" + ex.Message);
            }
        }

        public void Stop()
        {
            _stopFlag = true;
            IsRunning = false;

            try
            {
                if (_listener != null) { _listener.Stop(); _listener = null; }
            }
            catch (Exception)
            {
                // 忽略
            }

            lock (_sync)
            {
                foreach (TcpClient c in _clients)
                {
                    try { c.Close(); } catch (Exception) { }
                }
                _clients.Clear();
            }

            RaiseStatus("已停止");
        }

        /// <summary>向所有已连接客户端广播文本。</summary>
        public OpResult Broadcast(string text)
        {
            if (string.IsNullOrEmpty(text)) return OpResult.Ok();
            return Broadcast(Encoding.UTF8.GetBytes(text));
        }

        public OpResult Broadcast(byte[] data)
        {
            lock (_sync)
            {
                if (_clients.Count == 0) return OpResult.Fail("没有客户端连接");

                int ok = 0;
                for (int i = _clients.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        NetworkStream s = _clients[i].GetStream();
                        s.Write(data, 0, data.Length);
                        s.Flush();
                        ok++;
                    }
                    catch (Exception)
                    {
                        try { _clients[i].Close(); } catch (Exception) { }
                        _clients.RemoveAt(i);
                    }
                }

                return OpResult.Ok("已广播到 " + ok + " 个客户端");
            }
        }

        private void AcceptLoop()
        {
            while (!_stopFlag)
            {
                try
                {
                    TcpClient client = _listener.AcceptTcpClient();
                    lock (_sync) { _clients.Add(client); }

                    RaiseStatus("客户端接入：" + client.Client.RemoteEndPoint);

                    Thread t = new Thread(() => ClientLoop(client));
                    t.IsBackground = true;
                    t.Start();
                }
                catch (Exception)
                {
                    if (!_stopFlag) Thread.Sleep(100);
                }
            }
        }

        private void ClientLoop(TcpClient client)
        {
            byte[] buffer = new byte[8192];
            try
            {
                NetworkStream stream = client.GetStream();
                while (!_stopFlag)
                {
                    int n = stream.Read(buffer, 0, buffer.Length);
                    if (n <= 0) break;

                    string text = Encoding.UTF8.GetString(buffer, 0, n);
                    EventHandler<string> handler = Received;
                    if (handler != null) handler(this, text);
                }
            }
            catch (Exception)
            {
                // 断开
            }
            finally
            {
                lock (_sync) { _clients.Remove(client); }
                try { client.Close(); } catch (Exception) { }
                RaiseStatus("客户端断开");
            }
        }

        private void RaiseStatus(string text)
        {
            EventHandler<string> handler = StatusChanged;
            if (handler != null) handler(this, text);
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
