using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace VisionPlatform.Core.Licensing
{
    /// <summary>
    /// 许可证验签（ECDSA P-256 + SHA-256），对应 LicenseTool README §4.3。
    /// <para>
    /// 【顺序不能变】<b>先验签，再解析 JSON</b>：验签针对的是 payload 的**原始字节**，
    /// 反序列化只在签名通过之后做 —— 否则畸形数据会先进反序列化器。
    /// </para>
    /// <para>
    /// 【签名格式】.NET Framework 的 <see cref="ECDsaCng"/> 使用 CNG 默认的
    /// IEEE P1363 格式（<c>r‖s</c> 定长），P-256 即 64 字节，与签发端一致。
    /// </para>
    /// </summary>
    public static class LicenseVerifier
    {
        /// <summary>P-256 的签名长度（r 32 + s 32）。</summary>
        private const int P256SignatureLength = 64;

        /// <summary>
        /// 用内置公钥逐个尝试验签。<paramref name="signature"/> 长度不符或全部公钥都验不过
        /// 都返回 false（不抛异常）。
        /// </summary>
        public static bool Verify(byte[] payloadBytes, byte[] signature)
        {
            if (payloadBytes == null || payloadBytes.Length == 0) return false;
            if (signature == null || signature.Length != P256SignatureLength) return false;

            string[] keys = LicensePublicKeys.Get();
            if (keys.Length == 0) return false;      // 还没内置公钥：一律判签名无效

            foreach (string base64Spki in keys)
            {
                try
                {
                    using (var ecdsa = CreateFromSpki(base64Spki))
                    {
                        if (ecdsa != null && ecdsa.VerifyData(payloadBytes, signature, HashAlgorithmName.SHA256))
                            return true;
                    }
                }
                catch (Exception)
                {
                    // 单个公钥解析/验签异常不影响其它公钥（轮换期可能新旧并存）
                }
            }
            return false;
        }

        /// <summary>
        /// 从 Base64 的 SubjectPublicKeyInfo 构造 P-256 验证器。
        /// <para>
        /// .NET Framework 4.8 没有 <c>ImportSubjectPublicKeyInfo</c>（那是 .NET Core 3.0+），
        /// 所以这里自己解 DER：SPKI = SEQUENCE{ AlgorithmIdentifier, BIT STRING{ 04‖X‖Y } }。
        /// 只支持 prime256v1（P-256）——本产品只用这一条曲线。
        /// </para>
        /// </summary>
        public static ECDsa CreateFromSpki(string base64Spki)
        {
            if (string.IsNullOrWhiteSpace(base64Spki)) return null;

            byte[] der = Convert.FromBase64String(base64Spki.Trim());
            int pos = 0;

            // 外层 SEQUENCE
            if (!ExpectTag(der, ref pos, 0x30)) return null;
            int outerLen;
            if (!ReadLength(der, ref pos, out outerLen)) return null;
            int outerEnd = Math.Min(der.Length, pos + outerLen);

            // 内层 SEQUENCE（AlgorithmIdentifier）：整段跳过
            if (!ExpectTag(der, ref pos, 0x30)) return null;
            int algLen;
            if (!ReadLength(der, ref pos, out algLen)) return null;
            pos += algLen;

            // BIT STRING：首字节是 unused bits（应为 0），随后 04‖X(32)‖Y(32)
            if (pos >= outerEnd || !ExpectTag(der, ref pos, 0x03)) return null;
            int bitLen;
            if (!ReadLength(der, ref pos, out bitLen)) return null;
            // BIT STRING 内容 = 1 字节 unused bits + 1 字节 0x04 标记 + 64 字节 X‖Y = 66
            if (bitLen < 66 || pos + bitLen > der.Length) return null;

            if (der[pos] != 0x00) return null;        // unused bits 必须是 0
            pos += 1;

            if (der[pos] != 0x04) return null;        // 未压缩点标记
            pos += 1;

            var x = new byte[32];
            var y = new byte[32];
            Buffer.BlockCopy(der, pos, x, 0, 32);
            Buffer.BlockCopy(der, pos + 32, y, 0, 32);

            var parameters = new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = x, Y = y }
            };

            var ecdsa = new ECDsaCng();
            ecdsa.ImportParameters(parameters);
            return ecdsa;
        }

        private static bool ExpectTag(byte[] der, ref int pos, byte tag)
        {
            if (pos >= der.Length || der[pos] != tag) return false;
            pos++;
            return true;
        }

        /// <summary>读 DER 长度（短格式 / 0x81 / 0x82 长格式，足够覆盖 SPKI）。</summary>
        private static bool ReadLength(byte[] der, ref int pos, out int length)
        {
            length = 0;
            if (pos >= der.Length) return false;

            byte first = der[pos++];
            if (first < 0x80)
            {
                length = first;
                return true;
            }

            int count = first & 0x7F;
            if (count == 0 || count > 2 || pos + count > der.Length) return false;

            int value = 0;
            for (int i = 0; i < count; i++) value = (value << 8) | der[pos++];
            length = value;
            return true;
        }

        /// <summary>
        /// 开发自测用：用给定公钥校验（不读内置公钥表）。
        /// <para>SmokeUi 会在运行时生成临时密钥对，签一个令牌走完整链路 —— 这样即使
        /// 正式公钥还没填，验签逻辑也能被回归覆盖。</para>
        /// </summary>
        public static bool VerifyWith(ECDsa publicKey, byte[] payloadBytes, byte[] signature)
        {
            if (publicKey == null || payloadBytes == null || signature == null) return false;
            if (signature.Length != P256SignatureLength) return false;

            try
            {
                return publicKey.VerifyData(payloadBytes, signature, HashAlgorithmName.SHA256);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
