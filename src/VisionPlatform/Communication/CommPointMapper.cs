using System;
using System.Globalization;
using VisionPlatform.Core;
using VisionPlatform.Framework.Communication;

namespace VisionPlatform.Communication
{
    /// <summary>配置模型 ↔ 协议契约的映射；Modbus 地址/类型转换也收在这里。</summary>
    internal static class CommPointMapper
    {
        public static CommEndpointOptions ToOptions(CommEndpointConfig cfg)
        {
            var o = new CommEndpointOptions();
            if (cfg == null) return o;

            o.Name = cfg.Name ?? "";
            o.Kind = cfg.Kind ?? "";
            o.Host = cfg.Host ?? "";
            o.Port = cfg.Port;
            o.SlaveId = cfg.SlaveId;
            o.TimeoutMs = cfg.TimeoutMs > 0 ? cfg.TimeoutMs : 1000;
            o.Framing = string.IsNullOrWhiteSpace(cfg.Framing) ? "None" : cfg.Framing;
            return o;
        }

        public static CommPointOptions ToOptions(CommPointConfig cfg)
        {
            var o = new CommPointOptions();
            if (cfg == null) return o;

            o.Name = cfg.Name ?? "";
            o.Endpoint = cfg.Endpoint ?? "";
            o.Address = cfg.Address ?? "";
            o.Direction = ParseDirection(cfg.Direction);
            o.Type = ParseType(cfg.Type);
            o.Length = cfg.Length > 0 ? cfg.Length : 1;
            o.WordOrder = string.IsNullOrWhiteSpace(cfg.WordOrder) ? "ABCD" : cfg.WordOrder;
            o.Scale = cfg.Scale == 0 ? 1.0 : cfg.Scale;
            o.Offset = cfg.Offset;
            o.Default = cfg.Default ?? "";
            o.Description = cfg.Description ?? "";
            return o;
        }

        public static CommPointDirection ParseDirection(string value)
        {
            string v = CommPointConfig.NormalizeDirection(value);
            if (v == "Read") return CommPointDirection.Read;
            if (v == "Write") return CommPointDirection.Write;
            return CommPointDirection.ReadWrite;
        }

        public static CommPointType ParseType(string value)
        {
            string v = CommPointConfig.NormalizeType(value);
            switch (v)
            {
                case "Int16": return CommPointType.Int16;
                case "UInt16": return CommPointType.UInt16;
                case "Int32": return CommPointType.Int32;
                case "UInt32": return CommPointType.UInt32;
                case "Float": return CommPointType.Float;
                case "Double": return CommPointType.Double;
                case "String": return CommPointType.String;
                default: return CommPointType.Bool;
            }
        }

        public static ModbusDataType ToModbusType(CommPointType type)
        {
            switch (type)
            {
                case CommPointType.Int16: return ModbusDataType.Int16;
                case CommPointType.UInt16: return ModbusDataType.UInt16;
                case CommPointType.Int32: return ModbusDataType.Int32;
                case CommPointType.UInt32: return ModbusDataType.UInt32;
                case CommPointType.Float: return ModbusDataType.Float32;
                case CommPointType.Double: return ModbusDataType.Float64;
                case CommPointType.String: return ModbusDataType.String;
                default: return ModbusDataType.Bool;
            }
        }

        public static ModbusWordOrder ParseWordOrder(string value)
        {
            string v = (value ?? "").Trim().ToUpperInvariant();
            if (v == "CDAB") return ModbusWordOrder.CDAB;
            if (v == "BADC") return ModbusWordOrder.BADC;
            if (v == "DCBA") return ModbusWordOrder.DCBA;
            return ModbusWordOrder.ABCD;
        }

        /// <summary>
        /// 解析 Modbus 地址：<c>HR:100</c> / <c>IR:100</c> / <c>CO:0</c> / <c>DI:0</c>；
        /// 纯数字按 HR。当前驱动只实现保持寄存器（FC03/06/16），其它区给出明确提示。
        /// </summary>
        public static bool TryParseModbusAddress(string address, out ushort offset, out string area, out string error)
        {
            offset = 0;
            area = "HR";
            error = null;

            string s = (address ?? "").Trim();
            if (s.Length == 0)
            {
                error = "点位地址为空（Modbus 需填 HR:100 这类地址）";
                return false;
            }

            int colon = s.IndexOf(':');
            if (colon > 0)
            {
                area = s.Substring(0, colon).Trim().ToUpperInvariant();
                s = s.Substring(colon + 1).Trim();
            }

            if (area != "HR" && area != "IR" && area != "CO" && area != "DI")
            {
                error = "未知 Modbus 区：" + area + "（可用 HR / IR / CO / DI）";
                return false;
            }

            int v;
            if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) || v < 0 || v > 65535)
            {
                error = "Modbus 地址需为 0~65535：" + address;
                return false;
            }

            offset = (ushort)v;
            return true;
        }

        /// <summary>把读回的原始值按点位类型/缩放转换成 CommValue。</summary>
        public static CommValue Decode(CommPointOptions point, string raw)
        {
            if (point == null) return CommValue.FromText(raw);

            switch (point.Type)
            {
                case CommPointType.String:
                    return CommValue.FromText(raw ?? "");

                case CommPointType.Bool:
                {
                    string s = (raw ?? "").Trim();
                    bool b = s == "1" || string.Equals(s, "true", StringComparison.OrdinalIgnoreCase);
                    return CommValue.FromBool(b);
                }

                default:
                {
                    double d;
                    if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                        return CommValue.FromNumber(0, point.Type);

                    d = d * point.Scale + point.Offset;
                    return CommValue.FromNumber(d, point.Type);
                }
            }
        }

        /// <summary>把工程值编码成 Modbus 写入用的原始文本（逆缩放）。</summary>
        public static string Encode(CommPointOptions point, CommValue value)
        {
            if (point == null || value == null) return "";

            if (point.Type == CommPointType.String) return value.ToText();
            if (point.Type == CommPointType.Bool) return value.AsBool() ? "1" : "0";

            double raw = (value.AsDouble() - point.Offset) / (point.Scale == 0 ? 1.0 : point.Scale);
            return raw.ToString(CultureInfo.InvariantCulture);
        }
    }
}
