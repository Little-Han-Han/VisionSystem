using System;
using System.Collections.Generic;
using System.IO;

namespace VisionPlatform.Core
{
    /// <summary>
    /// 许可证文件名的兼容处理。
    /// <para>
    /// 【为什么需要】签发工具把文件名固定成 <c>license.lic</c>（美式拼写），
    /// 而现场/我方习惯写成 <c>licence.lic</c>（英式）。两者只差一个字母，
    /// 但验签端只认一个名字 —— 找不到就判"许可证不存在"，现场极难看出来。
    /// </para>
    /// <para>
    /// 处理方式：启动时如果规范名不存在、而常见变体存在，就**复制**一份到规范名
    /// （保留原文件，避免用户疑惑"我的文件去哪了"），并记日志。
    /// </para>
    /// </summary>
    public static class LicenseFileCompat
    {
        /// <summary>规范文件名（与 VisionLicense.Contract 的常量一致）。</summary>
        public const string CanonicalName = "license.lic";

        /// <summary>现场常见的其它拼写（按优先级）。</summary>
        private static readonly string[] Aliases =
        {
            "licence.lic",
            // 随仓库/发布包分发的试用证模板：现场直接丢进 License\ 即可，
            // 程序会复制成 license.lic 再激活 —— 模板本身保持未激活，可反复分发。
            "licence_30days.lic",
            "license_30days.lic",
            "License.lic",
            "Licence.lic",
            "license.txt",
            "licence.txt"
        };

        /// <summary>规范文件完整路径。</summary>
        public static string CanonicalPath(string licenseDir)
        {
            return Path.Combine(licenseDir, CanonicalName);
        }

        /// <summary>
        /// 确保 <paramref name="licenseDir"/> 下存在规范名的许可证文件。
        /// </summary>
        /// <returns>做过迁移时返回说明文本（供日志），否则返回 <c>null</c>。</returns>
        public static string EnsureCanonical(string licenseDir)
        {
            try
            {
                if (string.IsNullOrEmpty(licenseDir)) return null;
                if (!Directory.Exists(licenseDir)) Directory.CreateDirectory(licenseDir);

                string canonical = CanonicalPath(licenseDir);
                if (File.Exists(canonical)) return null;

                foreach (string alias in Aliases)
                {
                    string path = Path.Combine(licenseDir, alias);
                    if (!File.Exists(path)) continue;

                    File.Copy(path, canonical, false);
                    return "许可证文件名已规范：把 " + alias + " 复制为 " + CanonicalName
                         + "（原文件保留，不影响使用）";
                }
                return null;
            }
            catch (Exception)
            {
                // 迁移失败不影响后续判定：Check 会返回"未找到许可证"
                return null;
            }
        }

        /// <summary>列出目录下的候选许可证文件（导入对话框默认目录用）。</summary>
        public static List<string> ListCandidates(string licenseDir)
        {
            var list = new List<string>();
            try
            {
                if (!Directory.Exists(licenseDir)) return list;

                foreach (string file in Directory.GetFiles(licenseDir, "*.lic"))
                    list.Add(file);

                foreach (string file in Directory.GetFiles(licenseDir, "*.txt"))
                {
                    string name = Path.GetFileName(file);
                    if (name != null && name.IndexOf("licen", StringComparison.OrdinalIgnoreCase) >= 0)
                        list.Add(file);
                }
            }
            catch (Exception)
            {
                // 目录不可读时返回已有部分
            }
            return list;
        }
    }
}
