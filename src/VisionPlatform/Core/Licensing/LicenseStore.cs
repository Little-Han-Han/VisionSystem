using System;
using System.IO;
using System.Text;

namespace VisionPlatform.Core.Licensing
{
    /// <summary>
    /// 许可证文件与状态文件的读写（目录：<c>&lt;程序目录&gt;\License</c>）。
    /// <para>
    /// 【为什么单独一个目录】现场只需要把 <c>license.lic</c> 丢进 <c>License\</c> 就能换授权，
    /// 不必和 Config / Recipes 混在一起；升级只换二进制时该目录天然保留。
    /// </para>
    /// <para>
    /// 【写入必须原子】先写 <c>*.tmp</c> 再 <see cref="File.Replace(string,string,string)"/>，
    /// 中途断电不会留下半截许可证（半截文件的后果是"软件起不来"且看不出原因）。
    /// </para>
    /// </summary>
    public static class LicenseStore
    {
        /// <summary>许可证目录：<c>&lt;root&gt;\License</c>。</summary>
        public static string GetDirectory(string root)
        {
            string baseDir = string.IsNullOrEmpty(root) ? AppPaths.Root : root;
            return Path.Combine(baseDir, LicenseDefaults.FolderName);
        }

        /// <summary>许可证文件：<c>&lt;root&gt;\License\license.lic</c>。</summary>
        public static string GetFilePath(string root)
        {
            return Path.Combine(GetDirectory(root), LicenseDefaults.FileName);
        }

        /// <summary>时间回拨状态文件：<c>&lt;root&gt;\License\state.bin</c>。</summary>
        public static string GetStatePath(string root)
        {
            return Path.Combine(GetDirectory(root), LicenseDefaults.StateFileName);
        }

        /// <summary>确保 <c>License\</c> 目录存在（启动时调用；失败不抛，由后续读取给出状态）。</summary>
        public static void EnsureDirectory(string root)
        {
            try
            {
                string dir = GetDirectory(root);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            }
            catch (Exception)
            {
                // 目录建不出来时 Check 会走到 Missing / Error，不在这里打断启动
            }
        }

        /// <summary>读许可证文本；文件不存在返回 <c>null</c>（与"内容为空"区分开）。</summary>
        public static string ReadText(string root)
        {
            try
            {
                string path = GetFilePath(root);
                if (!File.Exists(path)) return null;
                return File.ReadAllText(path, Encoding.UTF8);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>原子写入许可证文本。</summary>
        public static bool SaveText(string root, string token, out string error)
        {
            error = "";
            try
            {
                EnsureDirectory(root);
                string path = GetFilePath(root);
                string tmp = path + ".tmp";

                File.WriteAllText(tmp, token ?? "", new UTF8Encoding(false));

                if (File.Exists(path))
                {
                    // 备份一份便于回退（现场误导入错误的许可证时能马上找回上一份）
                    string backup = path + ".bak";
                    try
                    {
                        if (File.Exists(backup)) File.Delete(backup);
                        File.Replace(tmp, path, backup);
                    }
                    catch (Exception)
                    {
                        File.Copy(tmp, path, true);
                        File.Delete(tmp);
                    }
                }
                else
                {
                    File.Move(tmp, path);
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// 从任意路径导入许可证：<b>先校验签名/产品/机器码</b>，通过才落盘。
        /// <para>已过期的许可证也允许导入（用户可能只是补录台账），状态栏会显示"已过期"。</para>
        /// </summary>
        public static bool ImportFrom(string root, string sourcePath, out LicenseInfo info, out string error)
        {
            info = null;
            error = "";

            string token;
            try
            {
                if (!File.Exists(sourcePath))
                {
                    error = "文件不存在：" + sourcePath;
                    return false;
                }
                token = File.ReadAllText(sourcePath, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                error = "读取文件失败：" + ex.Message;
                return false;
            }

            LicenseStatus status = LicenseChecker.ValidateToken(token, out info, out error);
            if (status != LicenseStatus.Valid && status != LicenseStatus.Expired)
            {
                // 过期可以导入；签名/机器码/产品不符一律拒绝
                if (string.IsNullOrEmpty(error)) error = status.Describe();
                return false;
            }

            if (!SaveText(root, token.Trim(), out error)) return false;
            return true;
        }
    }
}
