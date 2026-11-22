using System;
using System.Text;

namespace VisionPlatform.Core.Licensing
{
    /// <summary>
    /// 令牌编解码：<c>VLIC1.&lt;base64url(payload JSON)&gt;.&lt;base64url(签名)&gt;</c>。
    /// <para>
    /// 【先验签再解析】本类只负责"拆段 + 解 Base64"，**不做 JSON 反序列化** ——
    /// 反序列化必须等 <see cref="LicenseVerifier"/> 验签通过之后再做，
    /// 不给畸形数据进反序列化器的机会（对应 LicenseTool README §4.3 的验签流程）。
    /// </para>
    /// <para>落盘的 <c>.lic</c> 文件就是这个单行令牌，<strong>不允许有任何注释或多余字符</strong>。</para>
    /// </summary>
    public static class LicenseToken
    {
        /// <summary>当前支持的令牌前缀。将来换曲线/加字段时用 <c>VLIC2</c>，老客户端会明确拒绝。</summary>
        public const string Prefix = "VLIC1";

        /// <summary>单行令牌的最大长度（防御性上限：正常 <c>.lic</c> 不到 4 KB）。</summary>
        private const int MaxTokenLength = 64 * 1024;

        /// <summary>
        /// 拆分令牌。<paramref name="payloadBytes"/> 是**原始 JSON 字节**（验签要对它做哈希，
        /// 不能先反序列化再重新序列化，那样字节会变、签名必失败）。
        /// </summary>
        public static bool TrySplit(string token, out byte[] payloadBytes, out byte[] signature,
                                    out string error)
        {
            payloadBytes = null;
            signature = null;
            error = "";

            if (string.IsNullOrWhiteSpace(token))
            {
                error = "令牌为空";
                return false;
            }

            // 允许用户从微信/邮件粘过来时带换行，但内容必须只有一个令牌
            string text = token.Trim();
            if (text.Length > MaxTokenLength)
            {
                error = "令牌过大（超过 64 KB），不是有效的许可证文件";
                return false;
            }

            string[] parts = text.Split('.');
            if (parts.Length != 3)
            {
                error = "令牌应为三段（前缀.内容.签名），实际 " + parts.Length + " 段";
                return false;
            }

            if (!string.Equals(parts[0], Prefix, StringComparison.Ordinal))
            {
                if (parts[0].StartsWith("VLIC", StringComparison.OrdinalIgnoreCase))
                {
                    error = "令牌版本 " + parts[0] + " 暂不支持，请升级软件";
                    return false;
                }
                error = "令牌前缀不是 " + Prefix;
                return false;
            }

            if (!TryFromBase64Url(parts[1], out payloadBytes))
            {
                error = "令牌内容段不是合法的 Base64Url";
                return false;
            }
            if (!TryFromBase64Url(parts[2], out signature))
            {
                error = "令牌签名段不是合法的 Base64Url";
                return false;
            }

            if (payloadBytes.Length == 0 || signature.Length == 0)
            {
                error = "令牌内容或签名为空";
                return false;
            }

            return true;
        }

        /// <summary>把 payload 字节与签名组装成单行令牌（签发侧/测试用）。</summary>
        public static string Build(byte[] payloadBytes, byte[] signature)
        {
            if (payloadBytes == null) throw new ArgumentNullException("payloadBytes");
            if (signature == null) throw new ArgumentNullException("signature");

            return Prefix + "." + ToBase64Url(payloadBytes) + "." + ToBase64Url(signature);
        }

        /// <summary>Base64Url 编码（去掉 <c>=</c> 填充，<c>+</c>/<c>/</c> 换成 <c>-</c>/<c>_</c>）。</summary>
        public static string ToBase64Url(byte[] data)
        {
            if (data == null || data.Length == 0) return "";
            return Convert.ToBase64String(data)
                          .TrimEnd('=')
                          .Replace('+', '-')
                          .Replace('/', '_');
        }

        /// <summary>Base64Url 解码（自动补回填充并还原字符）。</summary>
        public static bool TryFromBase64Url(string text, out byte[] data)
        {
            data = null;
            if (string.IsNullOrEmpty(text)) return false;

            string s = text.Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4)
            {
                case 2: s += "=="; break;
                case 3: s += "="; break;
                case 1: return false;      // 余 1 位不可能是合法 Base64
            }

            try
            {
                data = Convert.FromBase64String(s);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }
    }
}
