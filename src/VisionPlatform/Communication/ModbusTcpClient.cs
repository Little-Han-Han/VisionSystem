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
    ///
    /// 【多数据类型】在三个原始功能码之上提供了 <see cref="ReadAs"/> / <see cref="WriteAs"/>，
    /// 可按 Int16/UInt16/Int32/UInt32/Float32/Float64/String/Bool 与四种字序读写字符串、整数、浮点、布尔，
    /// 换算逻辑全部在 <see cref="ModbusData"/> 里（纯计算，无 IO）。
    /// 原始三个方法签名保持不变，既有调用方式（含 FlowRunner）一行都不用改。
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

                    byte[] response = Transact(pdu);          // 响应 PDU = FC + 字节数 + 数据
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

                    // 0x06 正常响应 PDU 为回显的请求，长度同请求，Transact 只读不校验，这里放松为按 MBAP 长度读
                    byte[] response = Transact(pdu);
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

                    // 0x10 正常响应 PDU = FC + 起始地址(2) + 数量(2)
                    byte[] response = Transact(pdu);
                    if (response == null) return OpResult.Fail("无响应");

                    return OpResult.Ok("已写入 " + count + " 个寄存器");
                }
                catch (Exception ex)
                {
                    return OpResult.Fail("写多个寄存器失败：" + ex.Message);
                }
            }
        }

        #region 多数据类型读写（全部走保持寄存器，不新增线圈功能码）

        /// <summary>
        /// 读保持寄存器（功能码 0x03）。与 <see cref="ReadHoldingRegisters"/> 语义完全一致，
        /// 只是命名与新 API（<see cref="ReadAs"/>）保持一致；
        /// 旧方法签名保留不动，内部直接转调，行为零差异。
        /// </summary>
        /// <param name="startAddress">起始地址。</param>
        /// <param name="count">寄存器个数，1~125。</param>
        /// <returns>成功带 ushort 原始值数组。</returns>
        public OpResult<ushort[]> ReadRegisters(ushort startAddress, ushort count)
        {
            return ReadHoldingRegisters(startAddress, count);
        }

        /// <summary>
        /// 按指定数据类型读保持寄存器并解码成<strong>可直接显示的字符串</strong>（功能码 0x03）。
        /// </summary>
        /// <param name="type">目标数据类型。</param>
        /// <param name="startAddress">起始地址。</param>
        /// <param name="length">
        /// <see cref="ModbusDataType.String"/> 时为<strong>字符数</strong>；
        /// 其余类型时为<strong>寄存器个数</strong>（≤0 表示只取单个值所需的寄存器数）。
        /// 寄存器数超过单个值所需时按多个值解码，结果用「, 」连接。
        /// </param>
        /// <param name="order">字序，默认 ABCD（大端、高字在前）。</param>
        /// <returns>成功带可显示字符串（失败消息可直接显示给用户）。</returns>
        public OpResult<string> ReadAs(
            ModbusDataType type, ushort startAddress, int length,
            ModbusWordOrder order = ModbusWordOrder.ABCD)
        {
            int regCount;

            if (type == ModbusDataType.String)
            {
                if (length <= 0 || length > ModbusData.MaxStringChars)
                    return OpResult<string>.Fail(
                        "读取 String 时长度（字符数）需为 1~" + ModbusData.MaxStringChars + "，当前为 " + length);

                regCount = ModbusData.RegisterCountOf(type, length);
            }
            else
            {
                int unit = ModbusData.RegisterCountOf(type, 0);
                if (unit <= 0)
                    return OpResult<string>.Fail("不支持的数据类型：" + type);

                if (length <= 0)
                {
                    regCount = unit;                         // 只取一个值
                }
                else if (length < unit)
                {
                    return OpResult<string>.Fail(
                        type + " 至少需要 " + unit + " 个寄存器，当前长度为 " + length);
                }
                else
                {
                    regCount = length;
                }
            }

            if (regCount > ModbusData.MaxReadRegisters)
                return OpResult<string>.Fail(
                    "读取 " + regCount + " 个寄存器超出单次上限 " + ModbusData.MaxReadRegisters +
                    "（功能码 0x03 限制）");

            OpResult<ushort[]> read = ReadRegisters(startAddress, (ushort)regCount);
            if (!read.Success) return OpResult<string>.Fail(read.Message);

            OpResult<string> decoded = ModbusData.TryDecode(type, read.Value, 0, regCount, order);
            if (!decoded.Success) return OpResult<string>.Fail(decoded.Message);

            return OpResult<string>.Ok(decoded.Value, "读取成功（" + type + "，" + order + "）");
        }

        /// <summary>
        /// 把文本按指定数据类型编码后写入保持寄存器（单寄存器走 0x06，多寄存器走 0x10）。
        /// </summary>
        /// <param name="type">目标数据类型。</param>
        /// <param name="startAddress">起始地址。</param>
        /// <param name="text">
        /// 用户输入文本：<see cref="ModbusDataType.String"/> 直接作为字符串内容；
        /// <see cref="ModbusDataType.Bool"/> 接受 true/false/1/0；
        /// 整数类型接受十进制与 0x 前缀十六进制；浮点类型接受 12.5 / -0.75 之类的小数形式。
        /// </param>
        /// <param name="order">字序，默认 ABCD（大端、高字在前）。</param>
        /// <returns>失败消息可直接显示给用户（例如「abc 不是合法的 Float32 值」）。</returns>
        public OpResult WriteAs(
            ModbusDataType type, ushort startAddress, string text,
            ModbusWordOrder order = ModbusWordOrder.ABCD)
        {
            OpResult<ushort[]> enc = ModbusData.TryEncode(type, text, order);
            if (!enc.Success) return OpResult.Fail(enc.Message);

            ushort[] regs = enc.Value;
            if (regs == null || regs.Length == 0)
                return OpResult.Fail("没有要写入的数据");

            if (regs.Length > ModbusData.MaxWriteRegisters)
                return OpResult.Fail(
                    "写入 " + regs.Length + " 个寄存器超出单次上限 " + ModbusData.MaxWriteRegisters +
                    "（功能码 0x10 限制）");

            if (regs.Length == 1)
                return WriteSingleRegister(startAddress, regs[0]);

            return WriteMultipleRegisters(startAddress, regs);
        }

        #endregion

        #region 协议细节

        /// <summary>发送请求并读取响应（自动去掉 MBAP 头，只返回 PDU）。</summary>
        /// <remarks>
        /// 响应 PDU 长度由 MBAP 里的「长度」字段决定，发送端无需预知，
        /// 故这里不接收 expectedPduLength（历史上曾有个从未被使用的同名参数，已删除）。
        /// </remarks>
        private byte[] Transact(byte[] pdu)
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
