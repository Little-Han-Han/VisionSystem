using System;
using System.Net.Sockets;
using VisionPlatform.Core;

namespace VisionPlatform.Communication
{
    /// <summary>
    /// Modbus TCP 客户端（自行实现协议，不依赖任何第三方库）。
    /// 支持：0x03 读保持寄存器、0x06 写单个寄存器、0x10 写多个寄存器。
    ///
    /// 报文格式：MBAP 头(7 字节) + PDU
    ///   MBAP = 事务标识(2) + 协议标识(2, 固定 0) + 长度(2) + 单元标识(1)
    /// </summary>
    public sealed class ModbusTcpClient : IDisposable
    {
        private readonly object _sync = new object();
        private TcpClient _client;
        private NetworkStream _stream;
        private ushort _transactionId;
        private byte _slaveId = 1;

        public bool IsConnected { get { return _client != null && _client.Connected; } }

        /// <summary>从站（单元）标识，默认 1。</summary>
        public byte SlaveId
        {
            get { return _slaveId; }
            set { _slaveId = value; }
        }

        public int TimeoutMs { get; set; }

        public ModbusTcpClient(int timeoutMs = 2000)
        {
            TimeoutMs = timeoutMs;
        }

        public OpResult Connect(string host, int port = 502)
        {
            try
            {
                Disconnect();
                _client = new TcpClient();
                IAsyncResult ar = _client.BeginConnect(host, port, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(TimeoutMs))
                {
                    _client.Close();
                    _client = null;
                    return OpResult.Fail("Modbus 连接超时：{0}:{1}", host, port);
                }
                _client.EndConnect(ar);
                _stream = _client.GetStream();
                _stream.ReadTimeout = TimeoutMs;
                _stream.WriteTimeout = TimeoutMs;

                Logger.Info("Modbus TCP 已连接 " + host + ":" + port);
                return OpResult.Ok("已连接");
            }
            catch (Exception ex)
            {
                return OpResult.Fail("Modbus 连接失败：" + ex.Message);
            }
        }

        public void Disconnect()
        {
            try
            {
                if (_stream != null) { _stream.Close(); _stream = null; }
                if (_client != null) { _client.Close(); _client = null; }
            }
            catch (Exception)
            {
                // 忽略
            }
        }

        /// <summary>读保持寄存器（功能码 0x03）。</summary>
        public OpResult<ushort[]> ReadHoldingRegisters(ushort startAddress, ushort count)
        {
            if (!IsConnected) return OpResult<ushort[]>.Fail("未连接");
            if (count == 0 || count > 125) return OpResult<ushort[]>.Fail("寄存器数量需在 1~125 之间");

            lock (_sync)
            {
                try
                {
                    byte[] pdu = new byte[5];
                    pdu[0] = 0x03;
                    WriteUInt16(pdu, 1, startAddress);
                    WriteUInt16(pdu, 3, count);

                    byte[] response = Transact(pdu, 2 + 1 + count * 2); // FC + 字节数 + 数据
                    if (response == null) return OpResult<ushort[]>.Fail("无响应");

                    int byteCount = response[1];
                    if (byteCount != count * 2)
                        return OpResult<ushort[]>.Fail(
                            string.Format("响应长度不符：期望 {0}，实际 {1}", count * 2, byteCount));

                    ushort[] values = new ushort[count];
                    for (int i = 0; i < count; i++)
                        values[i] = ReadUInt16(response, 2 + i * 2);

                    return OpResult<ushort[]>.Ok(values);
                }
                catch (Exception ex)
                {
                    return OpResult<ushort[]>.Fail("读寄存器失败：" + ex.Message);
                }
            }
        }

        /// <summary>写单个寄存器（功能码 0x06）。</summary>
        public OpResult WriteSingleRegister(ushort address, ushort value)
        {
            if (!IsConnected) return OpResult.Fail("未连接");

            lock (_sync)
            {
                try
                {
                    byte[] pdu = new byte[5];
                    pdu[0] = 0x06;
                    WriteUInt16(pdu, 1, address);
                    WriteUInt16(pdu, 3, value);

                    byte[] response = Transact(pdu, 5);
                    if (response == null) return OpResult.Fail("无响应");

                    return OpResult.Ok("写入成功");
                }
                catch (Exception ex)
                {
                    return OpResult.Fail("写寄存器失败：" + ex.Message);
                }
            }
        }

        /// <summary>写多个寄存器（功能码 0x10）。</summary>
        public OpResult WriteMultipleRegisters(ushort startAddress, ushort[] values)
        {
            if (!IsConnected) return OpResult.Fail("未连接");
            if (values == null || values.Length == 0) return OpResult.Fail("没有要写入的数据");
            if (values.Length > 123) return OpResult.Fail("一次最多写 123 个寄存器");

            lock (_sync)
            {
                try
                {
                    byte count = (byte)values.Length;
                    byte[] pdu = new byte[6 + count * 2];
                    pdu[0] = 0x10;
                    WriteUInt16(pdu, 1, startAddress);
                    WriteUInt16(pdu, 3, count);
                    pdu[5] = (byte)(count * 2);
                    for (int i = 0; i < count; i++)
                        WriteUInt16(pdu, 6 + i * 2, values[i]);

                    byte[] response = Transact(pdu, 5);
                    if (response == null) return OpResult.Fail("无响应");

                    return OpResult.Ok("已写入 " + count + " 个寄存器");
                }
                catch (Exception ex)
                {
                    return OpResult.Fail("写多个寄存器失败：" + ex.Message);
                }
            }
        }

        #region 协议细节

        /// <summary>发送请求并读取响应（自动去掉 MBAP 头，只返回 PDU）。</summary>
        private byte[] Transact(byte[] pdu, int expectedPduLength)
        {
            byte[] request = BuildMbap(pdu);

            _stream.Write(request, 0, request.Length);
            _stream.Flush();

            // MBAP 头 7 字节
            byte[] header = ReadExact(7);
            int length = ReadUInt16(header, 4);
            int pduLength = length - 1;

            byte[] responsePdu = ReadExact(pduLength);

            if (responsePdu.Length > 0 && (responsePdu[0] & 0x80) != 0)
            {
                byte code = responsePdu.Length > 1 ? responsePdu[1] : (byte)0;
                throw new Exception("Modbus 异常响应：功能码 " + responsePdu[0] + "，异常码 " + code);
            }

            return responsePdu;
        }

        private byte[] BuildMbap(byte[] pdu)
        {
            _transactionId++;
            if (_transactionId == 0) _transactionId = 1;

            byte[] frame = new byte[6 + 1 + pdu.Length]; // MBAP(6) + unitId(1) + PDU
            WriteUInt16(frame, 0, _transactionId);
            WriteUInt16(frame, 2, 0);               // 协议标识，Modbus TCP 固定 0
            WriteUInt16(frame, 4, (ushort)(pdu.Length + 1));
            frame[6] = _slaveId;
            Array.Copy(pdu, 0, frame, 7, pdu.Length);
            return frame;
        }

        private byte[] ReadExact(int count)
        {
            byte[] buffer = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int n = _stream.Read(buffer, offset, count - offset);
                if (n <= 0) throw new Exception("连接已关闭");
                offset += n;
            }
            return buffer;
        }

        /// <summary>按大端（网络字节序）写入 ushort。</summary>
        private static void WriteUInt16(byte[] buffer, int offset, ushort value)
        {
            buffer[offset] = (byte)(value >> 8);
            buffer[offset + 1] = (byte)(value & 0xFF);
        }

        private static ushort ReadUInt16(byte[] buffer, int offset)
        {
            return (ushort)((buffer[offset] << 8) | buffer[offset + 1]);
        }

        #endregion

        public void Dispose()
        {
            Disconnect();
        }
    }
}
