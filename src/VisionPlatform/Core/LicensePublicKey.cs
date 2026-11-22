namespace VisionPlatform.Core
{
    /// <summary>
    /// 客户端内置的许可证验证公钥。
    /// <para>
    /// 来源：LicenseTool →「密钥」页 → <c>keys\vendor_public.txt</c> 第二行（Base64）。
    /// 公钥不是秘密，随客户端发布；<b>私钥永远只在发证机</b>（DPAPI + 离线备份）。
    /// </para>
    /// <para>
    /// 【换密钥】把新公钥粘到这里、重新编译发版。老许可证会因验签失败被拒 ——
    /// 所以换密钥前要先把存量客户全部重新签发，或让 Contract 支持多公钥并存。
    /// </para>
    /// </summary>
    public static class LicensePublicKey
    {
        /// <summary>当前生效的公钥（Base64，由 <c>LicenseTool</c> 生成）。</summary>
        public const string Value =
            "RUNTMSAAAACupZ0f2S42cRdMvgrJLfwyxO0ucz2NSJBmG7qJCsydr7c582r/21Kn5NiZV4mr0YKUfOkMRav39GydMPAgToV6";
    }
}
