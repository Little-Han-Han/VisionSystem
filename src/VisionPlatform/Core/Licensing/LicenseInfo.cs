using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.Serialization;
using System.Text;

namespace VisionPlatform.Core.Licensing
{
    /// <summary>
    /// 许可证内容（令牌里的 payload）。
    /// <para>
    /// 【字段名必须与 LicenseTool 的签发侧逐字一致】它们直接决定 JSON 键名
    /// （<c>v / no / prod / ...</c>），改名会让已签发的许可证全部失效。
    /// 对应《LicenseTool README §4.2 许可内容》。
    /// </para>
    /// </summary>
    [DataContract]
    public sealed class LicenseInfo
    {
        /// <summary>令牌格式版本（当前固定 1）。</summary>
        [DataMember(Name = "v", EmitDefaultValue = false)]
        public int Version { get; set; }

        /// <summary>许可证编号，台账主键，人工可读（如 <c>VP-20261122-0001</c>）。</summary>
        [DataMember(Name = "no", EmitDefaultValue = false)]
        public string Number { get; set; }

        /// <summary>产品码。客户端校验必须等于 <see cref="LicenseDefaults.ProductCode"/>。</summary>
        [DataMember(Name = "prod", EmitDefaultValue = false)]
        public string Product { get; set; }

        /// <summary>版本：Trial / Standard / Pro。</summary>
        [DataMember(Name = "edition", EmitDefaultValue = false)]
        public string Edition { get; set; }

        /// <summary>客户名称（仅展示，不参与校验）。</summary>
        [DataMember(Name = "cust", EmitDefaultValue = false)]
        public string Customer { get; set; }

        /// <summary>签发时绑定的机器码（分组显示用，如 <c>7K3M-9QZP-2XLV-8WDB</c>）。</summary>
        [DataMember(Name = "machine", EmitDefaultValue = false)]
        public string MachineCode { get; set; }

        /// <summary>5 项硬件指纹短哈希；验签通过后按"≥N/5 匹配"放行。</summary>
        [DataMember(Name = "fp", EmitDefaultValue = false)]
        public List<string> Fingerprints { get; set; }

        /// <summary>签发时间（ISO-8601 UTC，如 <c>2026-11-22T08:00:00Z</c>）。</summary>
        [DataMember(Name = "issued", EmitDefaultValue = false)]
        public string IssuedUtc { get; set; }

        /// <summary>到期时间（ISO-8601 UTC）；<c>null</c> 或空 = 永久授权。</summary>
        [DataMember(Name = "expire", EmitDefaultValue = false)]
        public string ExpireUtc { get; set; }

        /// <summary>功能位（如 flow / comm / ocr / 3d），由入口处二次判断。</summary>
        [DataMember(Name = "features", EmitDefaultValue = false)]
        public List<string> Features { get; set; }

        /// <summary>允许的最大相机数（≤0 表示不限）。</summary>
        [DataMember(Name = "maxCameras", EmitDefaultValue = false)]
        public int MaxCameras { get; set; }

        /// <summary>允许的最大流程数（≤0 表示不限）。</summary>
        [DataMember(Name = "maxFlows", EmitDefaultValue = false)]
        public int MaxFlows { get; set; }

        /// <summary>备注（仅展示）。</summary>
        [DataMember(Name = "note", EmitDefaultValue = false)]
        public string Note { get; set; }

        public LicenseInfo()
        {
            Number = "";
            Product = "";
            Edition = "";
            Customer = "";
            MachineCode = "";
            Fingerprints = new List<string>();
            IssuedUtc = "";
            ExpireUtc = "";
            Features = new List<string>();
            Note = "";
        }

        /// <summary>是否永久有效（令牌里 expire 为空）。</summary>
        public bool IsPerpetual
        {
            get { return string.IsNullOrWhiteSpace(ExpireUtc); }
        }

        /// <summary>解析签发时间；解析不了返回 <see cref="DateTime.MinValue"/>。</summary>
        public DateTime IssuedTime
        {
            get { return ParseUtc(IssuedUtc); }
        }

        /// <summary>解析到期时间；无到期（永久）或解析失败返回 <see cref="DateTime.MaxValue"/>。</summary>
        public DateTime ExpireTime
        {
            get
            {
                if (IsPerpetual) return DateTime.MaxValue;
                DateTime t = ParseUtc(ExpireUtc);
                return t == DateTime.MinValue ? DateTime.MaxValue : t;
            }
        }

        /// <summary>是否包含某个功能位（大小写不敏感）。</summary>
        public bool HasFeature(string feature)
        {
            if (string.IsNullOrWhiteSpace(feature) || Features == null) return false;
            foreach (string f in Features)
            {
                if (string.Equals(f, feature, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>生成给人看的摘要（日志 / 窗体标题用，避免各处自己拼）。</summary>
        public string Summary()
        {
            var sb = new StringBuilder();
            sb.Append(string.IsNullOrEmpty(Number) ? "(无编号)" : Number);
            if (!string.IsNullOrEmpty(Customer)) sb.Append(" · ").Append(Customer);
            if (!string.IsNullOrEmpty(Edition)) sb.Append(" · ").Append(Edition);
            sb.Append(" · ");
            sb.Append(IsPerpetual
                ? "永久"
                : "至 " + ExpireTime.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        /// <summary>
        /// 解析 ISO-8601 时间。<strong>按 UTC 处理</strong>：带 Z 后缀的按 Z，
        /// 不带后缀的按 UTC（避免本机时区把有效期整体推迟 8 小时）。
        /// </summary>
        private static DateTime ParseUtc(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return DateTime.MinValue;

            DateTime dt;
            if (DateTime.TryParse(text, CultureInfo.InvariantCulture,
                                  DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                                  out dt))
            {
                return dt;
            }
            return DateTime.MinValue;
        }
    }
}
