using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using VisionPlatform.Core;

namespace VisionPlatform.Communication
{
    /// <summary>
    /// TCP 客户端：连接远端（通常是 PLC 或上位机），支持文本收发与事件通知。
    /// 接收在后台线程完成，回调通过事件抛出（调用方若更新 UI 需自行 Invoke）。
    /// </summary>
    public sealed class TcpClientSession : IDisposable
    {
        private readonly object _sync = new object();
        private TcpClient _client;
        private NetworkStream _stream;
        private Thread _recvThread;
        private volatile bool _stopFlag;

        public bool IsConnected
        {
            get { return _client != null && _client.Connected; }
        }

        public string RemoteEndPoint { get; private set; }

        /// <summary>收到文本数据。</summary>
        public event EventHandler<string> Received;

        /// <summary>状态变化（已连接 / 已断开 / 错误信息）。</summary>
        public event EventHandler<string> StatusChanged;

        public OpResult Connect(string host, int port, int timeoutMs = 3000)
        {
            try
            {
                Disconnect();

                _client = new TcpClient();
                IAsyncResult ar = _client.BeginConnect(host, port, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(timeoutMs))
                {
                    _client.Close();
                    _client = null;
                    return OpResult.Fail("连接超时：{0}:{1}", host, port);
                }

                _client.EndConnect(ar);
                _stream = _client.GetStream();
                RemoteEndPoint = host + ":" + port;
                _stopFlag = false;

                _recvThread = new Thread(ReceiveLoop);
                _recvThread.IsBackground = true;
                _recvThread.Name = "TcpClientRecv";
                _recvThread.Start();

                RaiseStatus("已连接 " + RemoteEndPoint);
                Logger.Info("TCP 客户端已连接 " + RemoteEndPoint);
                return OpResult.Ok("已连接 " + RemoteEndPoint);
            }
            catch (Exception ex)
            {
                return OpResult.Fail("连接失败：" + ex.Message);
            }
        }

        public void Disconnect()
        {
            _stopFlag = true;

            try
            {
                if (_stream != null) { _stream.Close(); _stream = null; }
                if (_client != null) { _client.Close(); _client = null; }
            }
            catch (Exception)
            {
                // 关闭异常可忽略
            }

            RaiseStatus("已断开");
        }

        public OpResult Send(string text)
        {
            if (string.IsNullOrEmpty(text)) return OpResult.Ok();
            return Send(Encoding.UTF8.GetBytes(text));
        }

        public OpResult Send(byte[] data)
        {
            lock (_sync)
            {
                if (!IsConnected || _stream == null) return OpResult.Fail("未连接");

                try
                {
                    _stream.Write(data, 0, data.Length);
                    _stream.Flush();
                    return OpResult.Ok();
                }
                catch (Exception ex)
                {
                    return OpResult.Fail("发送失败：" + ex.Message);
                }
            }
        }

        private void ReceiveLoop()
        {
            byte[] buffer = new byte[8192];
            while (!_stopFlag)
            {
                try
                {
                    int n = _stream.Read(buffer, 0, buffer.Length);
                    if (n <= 0) break;

                    string text = Encoding.UTF8.GetString(buffer, 0, n);
                    EventHandler<string> handler = Received;
                    if (handler != null) handler(this, text);
                }
                catch (Exception)
                {
                    break;
                }
            }

            if (!_stopFlag) RaiseStatus("连接已断开");
        }

        private void RaiseStatus(string text)
        {
            EventHandler<string> handler = StatusChanged;
            if (handler != null) handler(this, text);
        }

        public void Dispose()
        {
            Disconnect();
        }
    }
}
