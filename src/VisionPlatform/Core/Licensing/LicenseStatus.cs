using System;

namespace VisionPlatform.Core.Licensing
{
    /// <summary>
    /// 授权校验结果。<strong>只表达"结论"，不携带异常</strong> —— 与 LicenseTool 的
    /// <c>LicenseChecker.Check</c> 约定一致：验签/解析过程中的任何异常都归结到
    /// <see cref="Error"/>，绝不向调用方抛。
    /// </summary>
    public enum LicenseStatus
    {
        /// <summary>许可证有效。</summary>
        Valid = 0,

        /// <summary>没有许可证文件，但在试用期内（见 <c>LicenseSettings.TrialDays</c>）。</summary>
        Trial = 1,

        /// <summary>没有许可证文件，且未开启试用。</summary>
        Missing = 2,

        /// <summary>已过有效期。</summary>
        Expired = 3,

        /// <summary>尚未生效（签发时间晚于当前时间，通常是把系统时间调到了过去）。</summary>
        NotYetValid = 4,

        /// <summary>机器码不匹配（许可证是给别的机器签的）。</summary>
        MachineMismatch = 5,

        /// <summary>签名校验失败（文件被改过，或不是本产品签发的）。</summary>
        SignatureInvalid = 6,

        /// <summary>产品码不符（拿到了别的产品的许可证）。</summary>
        ProductMismatch = 7,

        /// <summary>令牌版本不支持（<c>VLIC2</c> 等新格式，需升级客户端）。</summary>
        TokenVersionUnsupported = 8,

        /// <summary>令牌格式错误（缺段、Base64 损坏、JSON 残缺）。</summary>
        Malformed = 9,

        /// <summary>校验过程本身出错（文件被占用、IO 失败等），属环境问题不是授权问题。</summary>
        Error = 10
    }

    /// <summary>状态的中文说明与"是否拦截启动"的判定。</summary>
    public static class LicenseStatusText
    {
        /// <summary>给用户看的一句话说明（界面标题 / 日志都用它，避免各处自己拼）。</summary>
        public static string Describe(this LicenseStatus status)
        {
            switch (status)
            {
                case LicenseStatus.Valid: return "授权有效";
                case LicenseStatus.Trial: return "试用期";
                case LicenseStatus.Missing: return "未找到许可证";
                case LicenseStatus.Expired: return "许可证已过期";
                case LicenseStatus.NotYetValid: return "许可证尚未生效（请检查系统时间）";
                case LicenseStatus.MachineMismatch: return "许可证与本机不匹配";
                case LicenseStatus.SignatureInvalid: return "许可证签名无效（文件被修改或非本产品签发）";
                case LicenseStatus.ProductMismatch: return "许可证不属于本产品";
                case LicenseStatus.TokenVersionUnsupported: return "许可证格式过新，请升级软件";
                case LicenseStatus.Malformed: return "许可证文件已损坏";
                default: return "授权校验失败";
            }
        }

        /// <summary>
        /// 该状态是否允许进入主界面。
        /// <para><see cref="LicenseStatus.Trial"/> 允许（试用期），其余非 Valid 一律拦。</para>
        /// </summary>
        public static bool IsUsable(this LicenseStatus status)
        {
            return status == LicenseStatus.Valid || status == LicenseStatus.Trial;
        }

        /// <summary>该状态是否属于"文件层面不可用"（需要用户重新导入许可证）。</summary>
        public static bool NeedsImport(this LicenseStatus status)
        {
            switch (status)
            {
                case LicenseStatus.Missing:
                case LicenseStatus.Expired:
                case LicenseStatus.MachineMismatch:
                case LicenseStatus.SignatureInvalid:
                case LicenseStatus.ProductMismatch:
                case LicenseStatus.TokenVersionUnsupported:
                case LicenseStatus.Malformed:
                    return true;
                default:
                    return false;
            }
        }
    }
}
