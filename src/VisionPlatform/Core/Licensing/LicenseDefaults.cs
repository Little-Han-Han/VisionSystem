using System;

namespace VisionPlatform.Core.Licensing
{
    /// <summary>
    /// 授权模块的固定约定。<strong>这些常量是与签发端（LicenseTool）的接口契约，
    /// 改动会导致已发出的许可证失效</strong>。
    /// </summary>
    public static class LicenseDefaults
    {
        /// <summary>产品码：令牌里的 <c>prod</c> 必须等于它，否则判 <see cref="LicenseStatus.ProductMismatch"/>。</summary>
        public const string ProductCode = "VisionPlatform";

        /// <summary>许可证目录名（程序目录下）：<c>&lt;程序目录&gt;\License</c>。</summary>
        public const string FolderName = "License";

        /// <summary>许可证文件名（放在 <see cref="FolderName"/> 目录内）。</summary>
        public const string FileName = "license.lic";

        /// <summary>时间回拨状态文件名（与许可证同目录，随目录一起备份）。</summary>
        public const string StateFileName = "state.bin";

        /// <summary>默认指纹匹配阈值（3/5：换一张网卡或换硬盘不锁死，整机更换必失败）。</summary>
        public const int MinFingerprintMatch = 3;

        /// <summary>默认试用天数（0 = 不给试用，缺证直接拦）。</summary>
        public const int DefaultTrialDays = 0;

        /// <summary>
        /// 授权校验的宽严模式（配置项 <c>System.LicenseMode</c>）。
        /// <para>
        /// 【框架阶段默认 Off】LicenseTool 还没出签发功能，此时若强制校验，
        /// 现有现场会立刻无法启动。等签发工具就绪后再切到 <c>Enforce</c>。
        /// </para>
        /// </summary>
        public static class Modes
        {
            /// <summary>完全不校验（仅记一条日志）。默认值。</summary>
            public const string Off = "Off";

            /// <summary>校验并把结果写日志 / 显示在徽标上，但不拦截启动。</summary>
            public const string Warn = "Warn";

            /// <summary>校验失败即拦截启动（正式发版用这个）。</summary>
            public const string Enforce = "Enforce";

            /// <summary>把任意配置值归一化到三种模式之一（空/未知一律 Off，兼容老配置）。</summary>
            public static string Normalize(string mode)
            {
                if (string.IsNullOrWhiteSpace(mode)) return Off;

                string m = mode.Trim();
                if (string.Equals(m, Warn, StringComparison.OrdinalIgnoreCase)) return Warn;
                if (string.Equals(m, Enforce, StringComparison.OrdinalIgnoreCase)) return Enforce;
                return Off;
            }
        }
    }

    /// <summary>
    /// 内置的许可证验证公钥（Base64 的 <b>SubjectPublicKeyInfo</b>，ECDSA P-256）。
    /// <para>
    /// 【怎么填】LicenseTool 的「密钥」页签会显示 <c>vendor_public.txt</c>（Base64 SPKI），
    /// 把内容原样粘到下面的数组里即可（可放 1~2 个，便于密钥轮换过渡）。
    /// </para>
    /// <para>
    /// 【当前为空】签发工具尚在 P1 阶段，还没有正式密钥 —— 此时任何令牌都会判
    /// <see cref="LicenseStatus.SignatureInvalid"/>，所以框架阶段把校验模式默认设为 Off。
    /// </para>
    /// </summary>
    public static class LicensePublicKeys
    {
        /// <summary>受信任的公钥（Base64 SPKI）。空数组 = 尚未内置公钥。</summary>
        private static readonly string[] Keys =
        {
            // ↓↓↓ 用 LicenseTool 生成的公钥替换下面这一行（保留引号与逗号）↓↓↓
            // "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE...（约 124 个字符）",
        };

        /// <summary>是否已经内置了至少一个公钥。</summary>
        public static bool IsConfigured
        {
            get { return Keys.Length > 0 && !string.IsNullOrWhiteSpace(Keys[0]); }
        }

        /// <summary>取全部公钥（跳过空白项）。</summary>
        public static string[] Get()
        {
            var list = new System.Collections.Generic.List<string>(Keys.Length);
            foreach (string k in Keys)
            {
                if (!string.IsNullOrWhiteSpace(k)) list.Add(k.Trim());
            }
            return list.ToArray();
        }
    }
}
