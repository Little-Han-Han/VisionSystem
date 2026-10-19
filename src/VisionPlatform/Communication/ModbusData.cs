using System;
using System.Globalization;
using System.Text;
using VisionPlatform.Core;

namespace VisionPlatform.Communication
{
    /// <summary>
    /// Modbus 寄存器可承载的数据类型（对应界面下拉框的可选项）。
    /// <para>
    /// 【重要】本平台所有类型都<strong>只走保持寄存器</strong>（功能码 0x03 / 0x06 / 0x10），
    /// 不新增线圈（0x01 / 0x05）与离散输入（0x02）功能码。
    /// 其中 <see cref="ModbusDataType.Bool"/> 约定：寄存器值 <c>0 = false</c>，<c>非 0 = true</c>，
    /// 且只占 1 个寄存器（高字节补 0）。
    /// </para>
    /// </summary>
    public enum ModbusDataType
    {
        /// <summary>16 位有符号整数（-32768 ~ 32767），占 1 个寄存器。</summary>
        Int16 = 0,

        /// <summary>16 位无符号整数（0 ~ 65535），占 1 个寄存器。</summary>
        UInt16 = 1,

        /// <summary>32 位有符号整数，占 2 个寄存器。</summary>
        Int32 = 2,

        /// <summary>32 位无符号整数，占 2 个寄存器。</summary>
        UInt32 = 3,

        /// <summary>32 位单精度浮点（IEEE 754），占 2 个寄存器。</summary>
        Float32 = 4,

        /// <summary>64 位双精度浮点（IEEE 754），占 4 个寄存器。</summary>
        Float64 = 5,

        /// <summary>字符串：每寄存器存 2 个字符（高字节 = 前一个字符），不足补 0，占 ceil(字符数/2) 个寄存器。</summary>
        String = 6,

        /// <summary>布尔：0 = false，非 0 = true，占 1 个寄存器。</summary>
        Bool = 7,
    }

    /// <summary>
    /// 32 位 / 64 位数据在 Modbus 寄存器里的<strong>字序（字节序）</strong>。
    /// 命名沿用工控界 ABCD 惯例：把寄存器对的四个字节依次记作 A（最高字节）B C D（最低字节）。
    /// <para>
    /// 以一个 Float32 = 12.5（IEEE754 = 0x41480000）为例，两个寄存器依次为 r0、r1：
    /// <list type="table">
    /// <item><term>ABCD</term><description>大端、高字在前（默认）。r0 = 0x4148，r1 = 0x0000。字节流 = 41 48 00 00。</description></item>
    /// <item><term>CDAB</term><description>字交换（低字在前，很多 PLC / 仪表出厂默认就是这个顺序）。r0 = 0x0000，r1 = 0x4148。字节流 = 00 00 41 48。</description></item>
    /// <item><term>BADC</term><description>字内字节交换（字不换、每个字内部两字节互换）。r0 = 0x4841，r1 = 0x0000。字节流 = 48 41 00 00。</description></item>
    /// <item><term>DCBA</term><description>全交换（先字反转再每字字节交换，或反之，两者结果相同）。r0 = 0x0000，r1 = 0x4841。字节流 = 00 00 48 41。</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// 【对 64 位 / 字符串】<see cref="ModbusWordOrder.CDAB"/> 与 <see cref="ModbusWordOrder.DCBA"/>
    /// 的「字交换」是对<strong>整个寄存器窗口整体反转</strong>（4 个寄存器变成 r3 r2 r1 r0），
    /// 这与 pymodbus / j2mod 等主流实现一致；「字节交换」则是<strong>逐寄存器</strong>进行的。
    /// 单寄存器类型（Int16 / UInt16 / Bool）只有 BADC / DCBA 的字节交换有意义，
    /// CDAB / DCBA 的字交换对 1 个寄存器不起作用，故行为与 ABCD 一致。
    /// </para>
    /// </summary>
    public enum ModbusWordOrder
    {
        /// <summary>ABCD：大端、高字在前（默认，最通用）。</summary>
        ABCD = 0,

        /// <summary>CDAB：字交换，低字在前（西门子 / 多数国产 PLC 默认）。</summary>
        CDAB = 1,

        /// <summary>BADC：字内字节交换（寄存器不互换，每个寄存器内部两字节互换）。</summary>
        BADC = 2,

        /// <summary>DCBA：字与字节全交换。</summary>
        DCBA = 3,
    }

    /// <summary>
    /// 数据类型转换失败时抛出（替代 <c>FormatException</c> / <c>OverflowException</c> 冒泡）。
    /// <see cref="Message"/> 始终是<strong>可直接显示给用户的中文</strong>说明，
    /// 例如「abc 不是合法的 Float32 值」。
    /// </summary>
    public sealed class ModbusDataException : Exception
    {
        public ModbusDataException(string message) : base(message) { }

        public ModbusDataException(string message, Exception innerException)
            : base(message, innerException) { }
    }

    /// <summary>
    /// Modbus 数据类型编解码工具（静态类，<strong>纯计算、不做任何 IO</strong>）。
    /// <para>
    /// 【职责边界】本类只负责「文本 ↔ 寄存器数组」以及「寄存器数组 → 文本」的换算，
    /// 真正的收发由 <see cref="ModbusTcpClient.ReadAs"/> / <see cref="ModbusTcpClient.WriteAs"/> 完成。
    /// </para>
    /// <para>
    /// 【两套 API】
    /// <list type="bullet">
    /// <item><description><see cref="Encode"/> / <see cref="Decode"/>：失败时抛 <see cref="ModbusDataException"/>。</description></item>
    /// <item><description><see cref="TryEncode"/> / <see cref="TryDecode"/>：<strong>永不抛异常</strong>，
    /// 失败返回 <see cref="OpResult{T}.Fail"/>，界面层与上层的 <c>ReadAs</c> / <c>WriteAs</c> 一律用这两个。</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// 【字序实现】先按 <see cref="ModbusWordOrder"/> 对寄存器窗口做「整体字反转」与 / 或
    /// 「逐寄存器字节交换」归一化，再用 <see cref="BitConverter"/> 转换；
    /// 转换时用 <see cref="BitConverter.IsLittleEndian"/> 判断本机字节序再决定是否反转，绝不写死。
    /// 归一化与反归一化是同一个操作（两次调用互为逆运算），故编解码共用一份代码。
    /// </para>
    /// </summary>
    public static class ModbusData
    {
        /// <summary>功能码 0x03 单次可读寄存器上限。</summary>
        public const int MaxReadRegisters = 125;

        /// <summary>功能码 0x10 单次可写寄存器上限。</summary>
        public const int MaxWriteRegisters = 123;

        /// <summary>字符串单次可读写字符数上限（= MaxReadRegisters * 2）。</summary>
        public const int MaxStringChars = MaxReadRegisters * 2;

        #region 元数据（界面下拉框用）

        /// <summary>按显示顺序列出全部数据类型。</summary>
        public static ModbusDataType[] AllTypes()
        {
            return new ModbusDataType[]
            {
                ModbusDataType.Int16,
                ModbusDataType.UInt16,
                ModbusDataType.Int32,
                ModbusDataType.UInt32,
                ModbusDataType.Float32,
                ModbusDataType.Float64,
                ModbusDataType.String,
                ModbusDataType.Bool,
            };
        }

        /// <summary>按显示顺序列出全部字序。</summary>
        public static ModbusWordOrder[] AllWordOrders()
        {
            return new ModbusWordOrder[]
            {
                ModbusWordOrder.ABCD,
                ModbusWordOrder.CDAB,
                ModbusWordOrder.BADC,
                ModbusWordOrder.DCBA,
            };
        }

        /// <summary>数据类型的中文名（含位宽提示），供下拉框直接绑定。</summary>
        public static string DisplayNameOf(ModbusDataType type)
        {
            switch (type)
            {
                case ModbusDataType.Int16:   return "Int16（16位有符号）";
                case ModbusDataType.UInt16:  return "UInt16（16位无符号）";
                case ModbusDataType.Int32:   return "Int32（32位有符号）";
                case ModbusDataType.UInt32:  return "UInt32（32位无符号）";
                case ModbusDataType.Float32: return "Float32（单精度浮点）";
                case ModbusDataType.Float64: return "Float64（双精度浮点）";
                case ModbusDataType.String:  return "String（字符串）";
                case ModbusDataType.Bool:    return "Bool（0/非0）";
                default:                     return type.ToString();
            }
        }

        /// <summary>字序的中文名（含语义提示），供下拉框直接绑定。</summary>
        public static string OrderTextOf(ModbusWordOrder order)
        {
            switch (order)
            {
                case ModbusWordOrder.ABCD: return "ABCD（大端，高字在前）";
                case ModbusWordOrder.CDAB: return "CDAB（字交换，低字在前）";
                case ModbusWordOrder.BADC: return "BADC（字内字节交换）";
                case ModbusWordOrder.DCBA: return "DCBA（全交换）";
                default:                   return order.ToString();
            }
        }

        /// <summary>
        /// 单个值需要占用多少个寄存器。
        /// </summary>
        /// <param name="type">数据类型。</param>
        /// <param name="stringCharLength">仅 <see cref="ModbusDataType.String"/> 使用的字符数（每寄存器 2 字符）。</param>
        /// <returns>
        /// Bool / Int16 / UInt16 = 1；Int32 / UInt32 / Float32 = 2；Float64 = 4；
        /// String = ceil(stringCharLength / 2)；参数非法返回 0。
        /// </returns>
        public static int RegisterCountOf(ModbusDataType type, int stringCharLength)
        {
            switch (type)
            {
                case ModbusDataType.Int16:
                case ModbusDataType.UInt16:
                case ModbusDataType.Bool:
                    return 1;

                case ModbusDataType.Int32:
                case ModbusDataType.UInt32:
                case ModbusDataType.Float32:
                    return 2;

                case ModbusDataType.Float64:
                    return 4;

                case ModbusDataType.String:
                    if (stringCharLength <= 0) return 0;
                    return (stringCharLength + 1) / 2;

                default:
                    return 0;
            }
        }

        #endregion

        #region 编码（文本 → 寄存器）

        /// <summary>
        /// 把用户输入的文本编码成寄存器数组（<strong>失败抛异常</strong>版本）。
        /// </summary>
        /// <param name="type">目标类型。</param>
        /// <param name="text">用户输入文本，例如 <c>"12.5"</c> / <c>"true"</c> / <c>"hello"</c>。</param>
        /// <param name="order">字序。</param>
        /// <returns>待写入的寄存器数组。</returns>
        /// <exception cref="ModbusDataException">文本无法解析为该类型的值，或超出该类型范围。</exception>
        public static ushort[] Encode(ModbusDataType type, string text, ModbusWordOrder order)
        {
            OpResult<ushort[]> r = TryEncode(type, text, order);
            if (!r.Success) throw new ModbusDataException(r.Message);
            return r.Value;
        }

        /// <summary>
        /// 把用户输入的文本编码成寄存器数组（<strong>永不抛异常</strong>版本，推荐界面使用）。
        /// </summary>
        /// <param name="type">目标类型。</param>
        /// <param name="text">用户输入文本。</param>
        /// <param name="order">字序。</param>
        /// <returns>成功带寄存器数组；失败消息是可直接显示的中文。</returns>
        public static OpResult<ushort[]> TryEncode(ModbusDataType type, string text, ModbusWordOrder order)
        {
            string source = text == null ? "" : text;

            byte[] payload;                       // 自然（ABCD）顺序的大端字节负载，长度恒为偶数

            switch (type)
            {
                case ModbusDataType.Int16:
                {
                    long v;
                    if (!TryParseInteger(source, out v))
                        return WrongNumber("Int16", source, "-32768 ~ 32767");
                    if (v < short.MinValue || v > short.MaxValue)
                        return OutOfRange("Int16", source, "-32768 ~ 32767");
                    payload = ToBigEndian(BitConverter.GetBytes((short)v));
                    break;
                }

                case ModbusDataType.UInt16:
                {
                    long v;
                    if (!TryParseInteger(source, out v))
                        return WrongNumber("UInt16", source, "0 ~ 65535");
                    if (v < ushort.MinValue || v > ushort.MaxValue)
                        return OutOfRange("UInt16", source, "0 ~ 65535");
                    payload = ToBigEndian(BitConverter.GetBytes((ushort)v));
                    break;
                }

                case ModbusDataType.Int32:
                {
                    long v;
                    if (!TryParseInteger(source, out v))
                        return WrongNumber("Int32", source, "-2147483648 ~ 2147483647");
                    if (v < int.MinValue || v > int.MaxValue)
                        return OutOfRange("Int32", source, "-2147483648 ~ 2147483647");
                    payload = ToBigEndian(BitConverter.GetBytes((int)v));
                    break;
                }

                case ModbusDataType.UInt32:
                {
                    long v;
                    if (!TryParseInteger(source, out v))
                        return WrongNumber("UInt32", source, "0 ~ 4294967295");
                    if (v < uint.MinValue || v > uint.MaxValue)
                        return OutOfRange("UInt32", source, "0 ~ 4294967295");
                    payload = ToBigEndian(BitConverter.GetBytes((uint)v));
                    break;
                }

                case ModbusDataType.Float32:
                {
                    float f;
                    if (!float.TryParse(source.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out f))
                        return WrongNumber("Float32", source, "形如 12.5 的小数");
                    payload = ToBigEndian(BitConverter.GetBytes(f));
                    break;
                }

                case ModbusDataType.Float64:
                {
                    double d;
                    if (!double.TryParse(source.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                        return WrongNumber("Float64", source, "形如 3.14159 的小数");
                    payload = ToBigEndian(BitConverter.GetBytes(d));
                    break;
                }

                case ModbusDataType.Bool:
                {
                    bool b;
                    if (!TryParseBool(source, out b))
                        return OpResult<ushort[]>.Fail(
                            "「" + source + "」不是合法的 Bool 值（可用 true/false、1/0）");
                    payload = new byte[] { 0x00, b ? (byte)0x01 : (byte)0x00 };
                    break;
                }

                case ModbusDataType.String:
                {
                    OpResult<byte[]> str = EncodeStringBytes(source);
                    if (!str.Success) return OpResult<ushort[]>.Fail(str.Message);
                    payload = str.Value;
                    break;
                }

                default:
                    return OpResult<ushort[]>.Fail("不支持的数据类型：" + type);
            }

            ushort[] regs = Pack(payload, order);

            if (regs.Length > MaxWriteRegisters)
                return OpResult<ushort[]>.Fail(
                    string.Format(CultureInfo.InvariantCulture,
                        "{0} 需要写入 {1} 个寄存器，超出单次写入上限 {2}（功能码 0x10 限制）",
                        type, regs.Length, MaxWriteRegisters));

            return OpResult<ushort[]>.Ok(regs, "已编码 " + regs.Length + " 个寄存器");
        }

        #endregion

        #region 解码（寄存器 → 文本）

        /// <summary>
        /// 把寄存器窗口按类型解码成<strong>可直接显示的字符串</strong>（失败抛异常版本）。
        /// 这是界面显示的主入口；寄存器不足会抛 <see cref="ModbusDataException"/>。
        /// </summary>
        /// <param name="type">数据类型。</param>
        /// <param name="regs">寄存器数组（通常是 0x03 读回的原始结果）。</param>
        /// <param name="startIndex">窗口起始下标。</param>
        /// <param name="regCount">窗口内寄存器个数；超过单个值所需时按多个值解码并用「, 」连接。</param>
        /// <param name="order">字序。</param>
        /// <returns>可显示的字符串。</returns>
        /// <exception cref="ModbusDataException">寄存器数组为空或数量不足。</exception>
        public static string Decode(ModbusDataType type, ushort[] regs, int startIndex, int regCount, ModbusWordOrder order)
        {
            OpResult<string> r = TryDecode(type, regs, startIndex, regCount, order);
            if (!r.Success) throw new ModbusDataException(r.Message);
            return r.Value;
        }

        /// <summary>
        /// 把寄存器窗口按类型解码成可显示字符串（<strong>永不抛异常</strong>版本，推荐界面使用）。
        /// </summary>
        /// <param name="type">数据类型。</param>
        /// <param name="regs">寄存器数组。</param>
        /// <param name="startIndex">窗口起始下标。</param>
        /// <param name="regCount">窗口内寄存器个数。</param>
        /// <param name="order">字序。</param>
        /// <returns>成功带可显示字符串；失败消息是可直接显示的中文。</returns>
        public static OpResult<string> TryDecode(
            ModbusDataType type, ushort[] regs, int startIndex, int regCount, ModbusWordOrder order)
        {
            try
            {
                if (regs == null) return OpResult<string>.Fail("没有读到寄存器数据");
                if (startIndex < 0 || startIndex > regs.Length)
                    return OpResult<string>.Fail("起始下标 " + startIndex + " 越界（共 " + regs.Length + " 个寄存器）");
                if (regCount <= 0)
                    return OpResult<string>.Fail("要解码的寄存器个数必须大于 0，当前 " + regCount);
                if (regCount > regs.Length - startIndex)
                    return OpResult<string>.Fail(string.Format(CultureInfo.InvariantCulture,
                        "{0} 需要 {1} 个寄存器，实际只有 {2} 个", type, regCount, regs.Length - startIndex));

                // String 整窗就是一条字符串；其余类型按「单值所需寄存器数」切片，
                // 多余部分按多个值解码并用「, 」连接（例如读 4 个寄存器按 Float32 得到两个浮点数）。
                int unit = type == ModbusDataType.String ? regCount : RegisterCountOf(type, 0);
                if (unit <= 0) return OpResult<string>.Fail("不支持的数据类型：" + type);
                if (regCount < unit)
                    return OpResult<string>.Fail(string.Format(CultureInfo.InvariantCulture,
                        "{0} 至少需要 {1} 个寄存器，实际只有 {2} 个", type, unit, regCount));

                int valueCount = regCount / unit;
                if (valueCount <= 0) valueCount = 1;

                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < valueCount; i++)
                {
                    if (i > 0) sb.Append(", ");

                    int offset = startIndex + i * unit;
                    switch (type)
                    {
                        case ModbusDataType.Int16:
                            sb.Append(ToInt16(regs, offset, order).ToString(CultureInfo.InvariantCulture));
                            break;
                        case ModbusDataType.UInt16:
                            sb.Append(ToUInt16(regs, offset, order).ToString(CultureInfo.InvariantCulture));
                            break;
                        case ModbusDataType.Int32:
                            sb.Append(ToInt32(regs, offset, order).ToString(CultureInfo.InvariantCulture));
                            break;
                        case ModbusDataType.UInt32:
                            sb.Append(ToUInt32(regs, offset, order).ToString(CultureInfo.InvariantCulture));
                            break;
                        case ModbusDataType.Float32:
                            sb.Append(ToSingle(regs, offset, order).ToString(CultureInfo.InvariantCulture));
                            break;
                        case ModbusDataType.Float64:
                            sb.Append(ToDouble(regs, offset, order).ToString(CultureInfo.InvariantCulture));
                            break;
                        case ModbusDataType.Bool:
                            // 显示小写是为了「读出来的文本能原样写回去」：写 true 读 true，不会变成 True
                            sb.Append(ToBoolean(regs, offset, order) ? "true" : "false");
                            break;
                        case ModbusDataType.String:
                            sb.Append(ToString(regs, startIndex, unit * 2, order));
                            break;
                        default:
                            return OpResult<string>.Fail("不支持的数据类型：" + type);
                    }

                    if (type == ModbusDataType.String) break;   // 字符串整窗只有一个值
                }

                return OpResult<string>.Ok(sb.ToString(), "解码成功");
            }
            catch (ModbusDataException ex)
            {
                return OpResult<string>.Fail(ex.Message);
            }
            catch (Exception ex)
            {
                return OpResult<string>.Fail(type + " 解码失败：" + ex.Message);
            }
        }

        #endregion

        #region 强类型取值（内部复用，也可被其它模块直接使用）

        /// <summary>取 16 位有符号整数（1 个寄存器）。</summary>
        public static short ToInt16(ushort[] regs, int startIndex, ModbusWordOrder order)
        {
            byte[] b = NormalizedBytes(regs, startIndex, 1, order, "Int16");
            return BitConverter.ToInt16(ToHostOrder(b), 0);
        }

        /// <summary>取 16 位无符号整数（1 个寄存器）。</summary>
        public static ushort ToUInt16(ushort[] regs, int startIndex, ModbusWordOrder order)
        {
            byte[] b = NormalizedBytes(regs, startIndex, 1, order, "UInt16");
            return BitConverter.ToUInt16(ToHostOrder(b), 0);
        }

        /// <summary>取 32 位有符号整数（2 个寄存器）。</summary>
        public static int ToInt32(ushort[] regs, int startIndex, ModbusWordOrder order)
        {
            byte[] b = NormalizedBytes(regs, startIndex, 2, order, "Int32");
            return BitConverter.ToInt32(ToHostOrder(b), 0);
        }

        /// <summary>取 32 位无符号整数（2 个寄存器）。</summary>
        public static uint ToUInt32(ushort[] regs, int startIndex, ModbusWordOrder order)
        {
            byte[] b = NormalizedBytes(regs, startIndex, 2, order, "UInt32");
            return BitConverter.ToUInt32(ToHostOrder(b), 0);
        }

        /// <summary>取 32 位单精度浮点（2 个寄存器）。</summary>
        public static float ToSingle(ushort[] regs, int startIndex, ModbusWordOrder order)
        {
            byte[] b = NormalizedBytes(regs, startIndex, 2, order, "Float32");
            return BitConverter.ToSingle(ToHostOrder(b), 0);
        }

        /// <summary>取 64 位双精度浮点（4 个寄存器）。</summary>
        public static double ToDouble(ushort[] regs, int startIndex, ModbusWordOrder order)
        {
            byte[] b = NormalizedBytes(regs, startIndex, 4, order, "Float64");
            return BitConverter.ToDouble(ToHostOrder(b), 0);
        }

        /// <summary>取布尔值（1 个寄存器）：0 = false，非 0 = true。</summary>
        public static bool ToBoolean(ushort[] regs, int startIndex, ModbusWordOrder order)
        {
            CheckWindow(regs, startIndex, 1, "Bool");
            // 布尔只看「是否非零」，不受字序影响，故直接读原始寄存器值。
            return regs[startIndex] != 0;
        }

        /// <summary>
        /// 取字符串（每寄存器 2 个字符，高字节 = 前一个字符）。
        /// </summary>
        /// <param name="regs">寄存器数组。</param>
        /// <param name="startIndex">起始下标。</param>
        /// <param name="charLength">要读取的字符数（不是寄存器数）。</param>
        /// <param name="order">字序。</param>
        /// <returns>去掉尾部 \0 后的字符串。</returns>
        public static string ToString(ushort[] regs, int startIndex, int charLength, ModbusWordOrder order)
        {
            int regCount = (charLength + 1) / 2;
            if (charLength <= 0) return "";

            CheckWindow(regs, startIndex, regCount, "String");

            ushort[] window = new ushort[regCount];
            Array.Copy(regs, startIndex, window, 0, regCount);
            ApplyOrder(window, order);

            StringBuilder sb = new StringBuilder(regCount * 2);
            for (int i = 0; i < regCount; i++)
            {
                sb.Append((char)((window[i] >> 8) & 0xFF));   // 高字节 = 前一个字符（ABCD 惯例）
                if (sb.Length >= charLength) break;

                sb.Append((char)(window[i] & 0xFF));          // 低字节 = 后一个字符
                if (sb.Length >= charLength) break;
            }
            if (sb.Length > charLength) sb.Length = charLength;

            // 去掉尾部填充的 '\0'
            int end = sb.Length;
            while (end > 0 && sb[end - 1] == '\0') end--;
            return sb.ToString(0, end);
        }

        #endregion

        #region 内部实现

        /// <summary>把自然大端字节负载按<strong>偶数长度</strong>打包成寄存器，并按字序去归一化。</summary>
        private static ushort[] Pack(byte[] payload, ModbusWordOrder order)
        {
            int count = (payload.Length + 1) / 2;
            ushort[] regs = new ushort[count];

            for (int i = 0; i < count; i++)
            {
                int hi = i * 2 < payload.Length ? payload[i * 2] : 0;
                int lo = i * 2 + 1 < payload.Length ? payload[i * 2 + 1] : 0;
                regs[i] = (ushort)((hi << 8) | lo);
            }

            ApplyOrder(regs, order);
            return regs;
        }

        /// <summary>
        /// 归一化：把寄存器窗口转换回「自然大端（ABCD）」字节序列。
        /// 与 <see cref="Pack"/> 里的 <see cref="ApplyOrder"/> 是同一个操作，故编码 / 解码共用。
        /// </summary>
        private static byte[] NormalizedBytes(
            ushort[] regs, int startIndex, int count, ModbusWordOrder order, string typeName)
        {
            CheckWindow(regs, startIndex, count, typeName);

            ushort[] window = new ushort[count];
            Array.Copy(regs, startIndex, window, 0, count);
            ApplyOrder(window, order);

            byte[] bytes = new byte[count * 2];
            for (int i = 0; i < count; i++)
            {
                bytes[i * 2] = (byte)((window[i] >> 8) & 0xFF);
                bytes[i * 2 + 1] = (byte)(window[i] & 0xFF);
            }
            return bytes;
        }

        /// <summary>
        /// 应用字序变换。该操作是它自己的逆运算（两次调用回到原值），
        /// 因此可以直接用于「编码时去归一化」与「解码时归一化」两个方向。
        /// </summary>
        private static void ApplyOrder(ushort[] regs, ModbusWordOrder order)
        {
            bool swapWords = order == ModbusWordOrder.CDAB || order == ModbusWordOrder.DCBA;
            bool swapBytes = order == ModbusWordOrder.BADC || order == ModbusWordOrder.DCBA;

            if (swapWords) Array.Reverse(regs);

            if (swapBytes)
            {
                for (int i = 0; i < regs.Length; i++)
                {
                    regs[i] = (ushort)(((regs[i] & 0x00FF) << 8) | ((regs[i] >> 8) & 0x00FF));
                }
            }
        }

        /// <summary>
        /// 大端字节序列 ⇄ 主机字节序列。
        /// 小端机上做一次反转（反转是逆运算，两个方向都正确）；
        /// 大端机上原样返回，避免写死。
        /// </summary>
        private static byte[] ToHostOrder(byte[] bigEndian)
        {
            if (!BitConverter.IsLittleEndian) return bigEndian;

            byte[] copy = new byte[bigEndian.Length];
            for (int i = 0; i < bigEndian.Length; i++)
                copy[i] = bigEndian[bigEndian.Length - 1 - i];
            return copy;
        }

        /// <summary>把 <see cref="BitConverter.GetBytes"/> 输出的主机序字节转成大端序。</summary>
        private static byte[] ToBigEndian(byte[] hostOrder)
        {
            return ToHostOrder(hostOrder);   // 反转即互换，同一函数即可
        }

        /// <summary>检查寄存器窗口是否够用，不足抛中文异常。</summary>
        private static void CheckWindow(ushort[] regs, int startIndex, int count, string typeName)
        {
            if (regs == null)
                throw new ModbusDataException(typeName + " 解码失败：寄存器数组为空");

            if (startIndex < 0 || startIndex > regs.Length)
                throw new ModbusDataException(string.Format(CultureInfo.InvariantCulture,
                    "{0} 解码失败：起始下标 {1} 越界（共 {2} 个寄存器）", typeName, startIndex, regs.Length));

            if (count < 0 || count > regs.Length - startIndex)
                throw new ModbusDataException(string.Format(CultureInfo.InvariantCulture,
                    "{0} 需要 {1} 个寄存器，实际只有 {2} 个", typeName, count, regs.Length - startIndex));
        }

        /// <summary>
        /// 解析整数，支持十进制（可带正负号）与 0x / 0X 前缀的十六进制。
        /// 解析失败不抛异常，返回 false。
        /// </summary>
        private static bool TryParseInteger(string text, out long value)
        {
            value = 0;
            if (string.IsNullOrEmpty(text)) return false;

            string s = text.Trim();
            if (s.Length == 0) return false;

            if (s.Length > 2 && s[0] == '0' && (s[1] == 'x' || s[1] == 'X'))
            {
                ulong hex;
                if (!ulong.TryParse(s.Substring(2), NumberStyles.AllowHexSpecifier,
                        CultureInfo.InvariantCulture, out hex))
                    return false;
                if (hex > long.MaxValue) return false;
                value = (long)hex;
                return true;
            }

            return long.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
        }

        /// <summary>解析布尔：接受 true / false / 1 / 0（忽略大小写与首尾空格）。</summary>
        private static bool TryParseBool(string text, out bool value)
        {
            value = false;
            if (string.IsNullOrEmpty(text)) return false;

            string s = text.Trim();
            if (string.Equals(s, "true", StringComparison.OrdinalIgnoreCase) || s == "1")
            {
                value = true;
                return true;
            }

            if (string.Equals(s, "false", StringComparison.OrdinalIgnoreCase) || s == "0")
            {
                value = false;
                return true;
            }

            return false;
        }

        /// <summary>
        /// 字符串 → 字节负载（每字符 1 字节，补偶数长度）。
        /// Modbus 字符串寄存器每个字符只占 1 字节，因此对无法用单字节表示的字符（如中文）
        /// 给出明确的中文报错，而不是悄悄截断。
        /// </summary>
        private static OpResult<byte[]> EncodeStringBytes(string text)
        {
            if (string.IsNullOrEmpty(text))
                return OpResult<byte[]>.Fail("字符串内容不能为空");

            if (text.Length > MaxStringChars)
                return OpResult<byte[]>.Fail(string.Format(CultureInfo.InvariantCulture,
                    "字符串长度 {0} 超出上限 {1}（每寄存器 2 字符，单次最多 {2} 个寄存器）",
                    text.Length, MaxStringChars, MaxReadRegisters));

            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] > 0xFF)
                    return OpResult<byte[]>.Fail(string.Format(CultureInfo.InvariantCulture,
                        "字符串第 {0} 个字符「{1}」无法用单字节表示（Modbus 字符串每字符只占 1 字节，请改用 ASCII 字符）",
                        i + 1, text[i]));
            }

            int even = ((text.Length + 1) / 2) * 2;
            byte[] payload = new byte[even];
            for (int i = 0; i < text.Length; i++) payload[i] = (byte)(text[i] & 0xFF);

            return OpResult<byte[]>.Ok(payload, "OK");
        }

        /// <summary>构造「值格式不对」的中文失败结果。</summary>
        private static OpResult<ushort[]> WrongNumber(string typeName, string text, string hint)
        {
            return OpResult<ushort[]>.Fail(
                string.Format(CultureInfo.InvariantCulture, "「{0}」不是合法的 {1} 值（应为 {2}）", text, typeName, hint));
        }

        /// <summary>构造「超出范围」的中文失败结果。</summary>
        private static OpResult<ushort[]> OutOfRange(string typeName, string text, string range)
        {
            return OpResult<ushort[]>.Fail(
                string.Format(CultureInfo.InvariantCulture, "值 {0} 超出 {1} 范围（{2}）", text, typeName, range));
        }

        #endregion
    }
}
