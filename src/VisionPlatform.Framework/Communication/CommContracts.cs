using System;
using System.Collections.Generic;

namespace VisionPlatform.Framework.Communication
{
    /// <summary>点位数据类型。</summary>
    public enum CommPointType
    {
        Bool,
        Int16,
        UInt16,
        Int32,
        UInt32,
        Float,
        Double,
        String
    }

    /// <summary>点位方向。</summary>
    public enum CommPointDirection
    {
        Read,
        Write,
        ReadWrite
    }

    /// <summary>
    /// 一个点位（协议无关）：名称 → 地址 / 类型 / 方向 / 缩放。
    /// <para>协议插件只认本对象，不认识配置文件与流程。</para>
    /// </summary>
    public sealed class CommPointOptions
    {
        public string Name { get; set; }
        public string Endpoint { get; set; }

        /// <summary>协议地址：Modbus 用 <c>HR:100</c> / <c>CO:0</c>，OPC UA 用 NodeId，TCP 可留空。</summary>
        public string Address { get; set; }

        public CommPointDirection Direction { get; set; }
        public CommPointType Type { get; set; }

        /// <summary>寄存器个数 / 字符串字符数；1 表示单值。</summary>
        public int Length { get; set; }

        /// <summary>字序（Modbus 多寄存器）：ABCD / CDAB / BADC / DCBA。</summary>
        public string WordOrder { get; set; }

        /// <summary>工程值 = 原始值 × Scale + Offset。</summary>
        public double Scale { get; set; }
        public double Offset { get; set; }

        /// <summary>初始值 / 断线回落值（文本形式）。</summary>
        public string Default { get; set; }

        public string Description { get; set; }

        public CommPointOptions()
        {
            Name = "";
            Endpoint = "";
            Address = "";
            Direction = CommPointDirection.ReadWrite;
            Type = CommPointType.Bool;
            Length = 1;
            WordOrder = "ABCD";
            Scale = 1.0;
            Offset = 0.0;
            Default = "";
            Description = "";
        }
    }

    /// <summary>协议无关的值。</summary>
    public sealed class CommValue
    {
        public CommPointType Type { get; set; }
        public bool Bool { get; set; }
        public double Number { get; set; }
        public string Text { get; set; }

        public CommValue()
        {
            Type = CommPointType.Bool;
            Text = "";
        }

        public static CommValue FromBool(bool value)
        {
            return new CommValue { Type = CommPointType.Bool, Bool = value, Number = value ? 1 : 0, Text = value ? "true" : "false" };
        }

        public static CommValue FromNumber(double value, CommPointType type)
        {
            return new CommValue { Type = type, Number = value, Bool = value != 0, Text = value.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        }

        public static CommValue FromText(string value)
        {
            return new CommValue { Type = CommPointType.String, Text = value ?? "", Bool = !string.IsNullOrEmpty(value), Number = 0 };
        }

        /// <summary>按目标类型解析文本（配置文件 / 模板占位符都走这里）。</summary>
        public static CommValue Parse(string text, CommPointType type)
        {
            string s = (text ?? "").Trim();

            switch (type)
            {
                case CommPointType.Bool:
                {
                    bool b = s == "1" || string.Equals(s, "true", StringComparison.OrdinalIgnoreCase)
                             || string.Equals(s, "on", StringComparison.OrdinalIgnoreCase)
                             || string.Equals(s, "yes", StringComparison.OrdinalIgnoreCase);
                    return FromBool(b);
                }

                case CommPointType.String:
                    return FromText(s);

                default:
                {
                    double d;
                    if (!double.TryParse(s, System.Globalization.NumberStyles.Float,
                                         System.Globalization.CultureInfo.InvariantCulture, out d)) d = 0;
                    return FromNumber(d, type);
                }
            }
        }

        public bool AsBool()
        {
            if (Type == CommPointType.Bool) return Bool;
            if (Type == CommPointType.String) return !string.IsNullOrEmpty(Text) && Text != "0";
            return Number != 0;
        }

        public double AsDouble()
        {
            if (Type == CommPointType.Bool) return Bool ? 1 : 0;
            if (Type == CommPointType.String)
            {
                double d;
                double.TryParse(Text, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out d);
                return d;
            }
            return Number;
        }

        public string ToText()
        {
            if (Type == CommPointType.Bool) return Bool ? "true" : "false";
            if (Type == CommPointType.String) return Text ?? "";
            return Number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    /// <summary>协议无关的连接配置（由宿主配置映射而来）。</summary>
    public sealed class CommEndpointOptions
    {
        public string Name { get; set; }
        public string Kind { get; set; }
        public string Host { get; set; }
        public int Port { get; set; }
        public int SlaveId { get; set; }
        public int TimeoutMs { get; set; }
        public string Framing { get; set; }

        /// <summary>协议私有参数（例如 OPC UA 的安全策略、超时、证书路径）。</summary>
        public Dictionary<string, string> Properties { get; private set; }

        public CommEndpointOptions()
        {
            Name = "";
            Kind = "";
            Host = "127.0.0.1";
            Port = 0;
            SlaveId = 1;
            TimeoutMs = 1000;
            Framing = "None";
            Properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>一帧原始/文本数据。</summary>
    public sealed class CommFrame
    {
        public bool IsText { get; set; }
        public string Text { get; set; }
        public byte[] Data { get; set; }
        public DateTime Time { get; set; }

        public CommFrame()
        {
            Text = "";
            Data = new byte[0];
            Time = DateTime.Now;
        }

        public static CommFrame FromText(string text)
        {
            return new CommFrame { IsText = true, Text = text ?? "", Time = DateTime.Now };
        }

        public static CommFrame FromBytes(byte[] data)
        {
            return new CommFrame { IsText = false, Data = data ?? new byte[0], Time = DateTime.Now };
        }
    }

    /// <summary>协议无关的调用结果。</summary>
    public class CommResult
    {
        public bool Success { get; protected set; }
        public string Message { get; protected set; }

        public static CommResult Ok(string message)
        {
            return new CommResult { Success = true, Message = message ?? "" };
        }

        public static CommResult Fail(string message)
        {
            return new CommResult { Success = false, Message = message ?? "" };
        }
    }

    /// <summary>带返回值的调用结果。</summary>
    public sealed class CommResult<T> : CommResult
    {
        public T Value { get; private set; }

        public static CommResult<T> Ok(T value, string message)
        {
            return new CommResult<T> { Success = true, Value = value, Message = message ?? "" };
        }

        public static new CommResult<T> Fail(string message)
        {
            return new CommResult<T> { Success = false, Value = default(T), Message = message ?? "" };
        }
    }

    public sealed class CommStateEventArgs : EventArgs
    {
        public string Endpoint { get; private set; }
        public bool Connected { get; private set; }
        public string Message { get; private set; }

        public CommStateEventArgs(string endpoint, bool connected, string message)
        {
            Endpoint = endpoint ?? "";
            Connected = connected;
            Message = message ?? "";
        }
    }

    public sealed class CommFrameEventArgs : EventArgs
    {
        public string Endpoint { get; private set; }
        public CommFrame Frame { get; private set; }

        public CommFrameEventArgs(string endpoint, CommFrame frame)
        {
            Endpoint = endpoint ?? "";
            Frame = frame;
        }
    }

    public sealed class CommPointValueEventArgs : EventArgs
    {
        public string Endpoint { get; private set; }
        public string Point { get; private set; }
        public CommValue Value { get; private set; }

        public CommPointValueEventArgs(string endpoint, string point, CommValue value)
        {
            Endpoint = endpoint ?? "";
            Point = point ?? "";
            Value = value;
        }
    }

    /// <summary>
    /// 协议通道：TCP / Modbus / OPC UA 都实现这一套。
    /// <para>实现方只依赖 Framework，不得引用宿主窗体或流程。</para>
    /// </summary>
    public interface ICommChannel : IDisposable
    {
        string Kind { get; }
        bool IsConnected { get; }
        string StatusText { get; }

        event EventHandler<CommStateEventArgs> StateChanged;
        event EventHandler<CommFrameEventArgs> FrameReceived;
        event EventHandler<CommPointValueEventArgs> PointChanged;

        CommResult Connect();
        void Disconnect();

        CommResult Send(CommFrame frame);
        CommResult<CommValue> Read(CommPointOptions point);
        CommResult Write(CommPointOptions point, CommValue value);

        /// <summary>订阅点位变化（不支持订阅的协议返回失败，由宿主轮询）。</summary>
        CommResult Subscribe(IEnumerable<CommPointOptions> points);
    }

    /// <summary>协议工厂：放在 <c>Communication\*.dll</c> 里由宿主反射加载。</summary>
    public interface ICommProtocolFactory
    {
        string Kind { get; }
        string DisplayName { get; }
        ICommChannel Create(CommEndpointOptions endpoint);
    }
}
