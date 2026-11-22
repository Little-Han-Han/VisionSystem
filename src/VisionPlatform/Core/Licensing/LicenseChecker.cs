using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Xml;

namespace VisionPlatform.Core.Licensing
{
    /// <summary>
    /// 授权校验入口（VisionSystem 侧）。对应 LicenseTool README §4.3 的验签流程。
    /// <para>
    /// <b>约定：<see cref="Check"/> 是纯函数</b> —— 只读文件、不写文件、不抛异常，
    /// 一律通过 <see cref="LicenseStatus"/> 表达结论。时间回拨的"写盘"由
    /// <see cref="TouchState"/> 单独负责，调用点自己决定要不要写。
    /// </para>
    /// <para>
    /// 【顺序】拆分令牌 → <b>验签（对原始字节）</b> → 反序列化 → 产品码 → 机器码 → 有效期。
    /// 反序列化只在验签通过之后做，畸形数据进不了解析器。
    /// </para>
    /// </summary>
    public static class LicenseChecker
    {
        /// <summary>
        /// 校验程序目录下的许可证。
        /// </summary>
        /// <param name="root">程序目录（通常传 <see cref="AppPaths.Root"/>）。</param>
        /// <param name="info">成功解析出的许可内容；失败时为 <c>null</c>（过期时仍有值，便于展示）。</param>
        public static LicenseStatus Check(string root, out LicenseInfo info)
        {
            info = null;

            try
            {
                string text = LicenseStore.ReadText(root);
                if (text == null)
                {
                    // 没有许可证：看是否在试用期内
                    return EvaluateTrial(root, out info);
                }

                string detail;
                LicenseStatus status = ValidateToken(text, out info, out detail);

                if (status == LicenseStatus.Valid || status == LicenseStatus.Expired
                    || status == LicenseStatus.NotYetValid)
                {
                    // 有效期判定要用"回拨保护后的当前时间"，这里再做一次（ValidateToken 用的是系统时间）
                    DateTime now = EffectiveNow(root);
                    if (info != null)
                    {
                        if (info.IssuedTime != DateTime.MinValue && info.IssuedTime > now.AddMinutes(5))
                            return LicenseStatus.NotYetValid;

                        if (!info.IsPerpetual && info.ExpireTime < now)
                            return LicenseStatus.Expired;
                    }
                }

                return status;
            }
            catch (Exception)
            {
                return LicenseStatus.Error;
            }
        }

        /// <summary>
        /// 只校验一段令牌文本（不读文件、不看试用）——导入前预检用。
        /// </summary>
        /// <param name="detail">失败原因（中文，可直接给用户看）。</param>
        public static LicenseStatus ValidateToken(string token, out LicenseInfo info, out string detail)
        {
            info = null;
            detail = "";

            byte[] payload;
            byte[] signature;

            string splitError;
            if (!LicenseToken.TrySplit(token, out payload, out signature, out splitError))
            {
                detail = splitError;
                return splitError.IndexOf("版本", StringComparison.Ordinal) >= 0
                    ? LicenseStatus.TokenVersionUnsupported
                    : LicenseStatus.Malformed;
            }

            // ① 先验签（针对原始字节）
            if (!LicenseVerifier.Verify(payload, signature))
            {
                detail = LicensePublicKeys.IsConfigured
                    ? "签名校验失败：文件被修改过，或不是本产品签发的许可证"
                    : "本版本尚未内置许可证公钥（签发工具就绪后才能启用验签）";
                return LicenseStatus.SignatureInvalid;
            }

            // ② 验签通过后才反序列化
            LicenseInfo parsed;
            if (!TryParsePayload(payload, out parsed, out detail))
            {
                return LicenseStatus.Malformed;
            }

            // ③ 产品码
            if (!string.Equals(parsed.Product, LicenseDefaults.ProductCode, StringComparison.OrdinalIgnoreCase))
            {
                detail = "许可证产品码为「" + (parsed.Product ?? "") + "」，本产品为「"
                       + LicenseDefaults.ProductCode + "」";
                return LicenseStatus.ProductMismatch;
            }

            // ④ 机器码（≥N/5 指纹匹配）
            int min = (ConfigStore.Current != null && ConfigStore.Current.System != null)
                ? ConfigStore.Current.System.LicenseMinFingerprintMatch : 0;
            int matches = MachineFingerprint.CountMatches(parsed.Fingerprints);
            if (!MachineFingerprint.Matches(parsed.Fingerprints, min))
            {
                detail = string.Format(CultureInfo.InvariantCulture,
                    "机器码不匹配：许可证绑定 {0}，本机匹配 {1}/{2} 项（需 ≥{3}）",
                    parsed.MachineCode ?? "(未填)", matches, MachineFingerprint.SlotCount,
                    min <= 0 ? LicenseDefaults.MinFingerprintMatch : min);
                return LicenseStatus.MachineMismatch;
            }

            // ⑤ 有效期（用系统时间，回拨保护在 Check 里叠加）
            DateTime now = DateTime.UtcNow;
            if (parsed.IssuedTime != DateTime.MinValue && parsed.IssuedTime > now.AddMinutes(5))
            {
                detail = "许可证尚未生效（签发时间晚于当前时间，请检查系统时间）";
                return LicenseStatus.NotYetValid;
            }
            if (!parsed.IsPerpetual && parsed.ExpireTime < now)
            {
                detail = "许可证已于 " + parsed.ExpireTime.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " 到期";
                info = parsed;              // 过期也带上内容：界面要显示编号/客户
                return LicenseStatus.Expired;
            }

            info = parsed;
            return LicenseStatus.Valid;
        }

        /// <summary>
        /// 记录"最近一次成功校验的 UTC 时间"（时间回拨检测的下限基准）。
        /// <para>与许可证同目录（<c>License\state.bin</c>），带 HMAC —— 手改即失效。</para>
        /// </summary>
        public static void TouchState(string root)
        {
            try
            {
                long firstSeen, lastCheck;
                ReadState(root, out firstSeen, out lastCheck);

                long now = DateTime.UtcNow.Ticks;
                if (firstSeen <= 0) firstSeen = now;
                if (now > lastCheck) lastCheck = now;

                WriteState(root, firstSeen, lastCheck);
            }
            catch (Exception)
            {
                // 状态写不进去不影响授权本身（只影响回拨检测）
            }
        }

        /// <summary>
        /// 防回拨的"当前时间"：取 <c>max(系统时间, 上次记录时间)</c>。
        /// <para>把系统时间调回过去不会让过期许可证复活 —— 因为基准取更大的那个。</para>
        /// </summary>
        public static DateTime EffectiveNow(string root)
        {
            DateTime system = DateTime.UtcNow;

            long firstSeen, lastCheck;
            if (ReadState(root, out firstSeen, out lastCheck) && lastCheck > 0)
            {
                var recorded = new DateTime(lastCheck, DateTimeKind.Utc);
                if (recorded > system) return recorded;
            }
            return system;
        }

        // =====================================================================
        //  试用期
        // =====================================================================

        /// <summary>
        /// 没有许可证文件时的判定：在试用期内返回 <see cref="LicenseStatus.Trial"/>，
        /// 否则 <see cref="LicenseStatus.Missing"/> / <see cref="LicenseStatus.Expired"/>。
        /// </summary>
        private static LicenseStatus EvaluateTrial(string root, out LicenseInfo info)
        {
            info = null;

            int trialDays = (ConfigStore.Current != null && ConfigStore.Current.System != null)
                ? ConfigStore.Current.System.LicenseTrialDays : 0;
            if (trialDays <= 0) return LicenseStatus.Missing;

            long firstSeen, lastCheck;
            if (!ReadState(root, out firstSeen, out lastCheck) || firstSeen <= 0)
            {
                // 首次运行：本次算试用第一天（TouchState 会把 firstSeen 写下来）
                return LicenseStatus.Trial;
            }

            double days = (DateTime.UtcNow - new DateTime(firstSeen, DateTimeKind.Utc)).TotalDays;
            return days <= trialDays ? LicenseStatus.Trial : LicenseStatus.Expired;
        }

        // =====================================================================
        //  状态文件（License\state.bin）
        // =====================================================================

        private static string StateHmacKey()
        {
            // 密钥与机器绑定：把 state 拷到别的机器上验不过，也就无法"借用"延长的试用期
            return "VisionPlatform.License.State|" + MachineFingerprint.GetMachineCode();
        }

        private static bool ReadState(string root, out long firstSeen, out long lastCheck)
        {
            firstSeen = 0;
            lastCheck = 0;

            try
            {
                string path = LicenseStore.GetStatePath(root);
                if (!File.Exists(path)) return false;

                string[] parts = File.ReadAllText(path, Encoding.UTF8).Trim().Split('|');
                if (parts.Length != 3) return false;

                long f, l;
                if (!long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out f)) return false;
                if (!long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out l)) return false;

                string expected = Hmac(parts[0] + "|" + parts[1]);
                if (!string.Equals(expected, parts[2], StringComparison.Ordinal)) return false;   // 被改过 → 视为无状态

                firstSeen = f;
                lastCheck = l;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void WriteState(string root, long firstSeen, long lastCheck)
        {
            LicenseStore.EnsureDirectory(root);

            string a = firstSeen.ToString(CultureInfo.InvariantCulture);
            string b = lastCheck.ToString(CultureInfo.InvariantCulture);
            string content = a + "|" + b + "|" + Hmac(a + "|" + b);

            string path = LicenseStore.GetStatePath(root);
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, content, new UTF8Encoding(false));

            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        private static string Hmac(string text)
        {
            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(StateHmacKey())))
            {
                byte[] mac = hmac.ComputeHash(Encoding.UTF8.GetBytes(text));

                var sb = new StringBuilder(mac.Length * 2);
                foreach (byte b in mac) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        // =====================================================================
        //  JSON
        // =====================================================================

        /// <summary>把 payload 字节反序列化成 <see cref="LicenseInfo"/>（仅验签通过后调用）。</summary>
        private static bool TryParsePayload(byte[] payload, out LicenseInfo info, out string detail)
        {
            info = null;
            detail = "";

            try
            {
                var serializer = new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(LicenseInfo));
                using (var ms = new MemoryStream(payload))
                using (XmlDictionaryReader reader = System.Runtime.Serialization.Json.JsonReaderWriterFactory.CreateJsonReader(
                           ms, XmlDictionaryReaderQuotas.Max))
                {
                    info = serializer.ReadObject(reader) as LicenseInfo;
                }

                if (info == null)
                {
                    detail = "许可证内容为空";
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                detail = "许可证内容解析失败：" + ex.Message;
                return false;
            }
        }
    }
}
