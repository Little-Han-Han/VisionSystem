using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;

namespace VisionPlatform.Core
{
    /// <summary>
    /// 按系统设置保存检测图像，并清理超过保留天数的历史图片。
    /// 目录结构：{保存路径}\{yyyy-MM-dd}\{HHmmss_fff}_{标签}_{OK|NG}.{扩展名}
    /// </summary>
    public static class ImageStore
    {
        /// <summary>当前生效的保存目录（不存在则创建）。</summary>
        public static string EnsureDirectory()
        {
            string dir = ConfigStore.Current.System.ImageSavePath;
            if (string.IsNullOrWhiteSpace(dir))
                dir = Path.Combine(AppPaths.Root, "Images");

            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            return dir;
        }

        /// <summary>规范化格式名（JPG→JPEG、TIF→TIFF）。</summary>
        private static string NormalizeFormat(string format)
        {
            if (string.IsNullOrWhiteSpace(format)) return "PNG";

            string f = format.Trim().ToUpperInvariant();
            if (f == "JPG") f = "JPEG";
            if (f == "TIF") f = "TIFF";
            return f;
        }

        /// <summary>当前格式对应的文件扩展名（含点）。</summary>
        public static string FileExtension
        {
            get
            {
                switch (NormalizeFormat(ConfigStore.Current.System.ImageFormat))
                {
                    case "JPEG": return ".jpg";
                    case "BMP": return ".bmp";
                    case "TIFF": return ".tif";
                    default: return ".png";
                }
            }
        }

        private static ImageFormat GetImageFormat()
        {
                switch (NormalizeFormat(ConfigStore.Current.System.ImageFormat))
                {
                    case "JPEG": return ImageFormat.Jpeg;
                    case "BMP": return ImageFormat.Bmp;
                    case "TIFF": return ImageFormat.Tiff;
                    default: return ImageFormat.Png;
                }
        }

        /// <summary>保存一张图像，返回完整路径；失败返回 null。</summary>
        /// <param name="image">要保存的位图（内部会复制一份，调用方可继续持有）。</param>
        /// <param name="tag">文件名标签，如相机名 / 视图名。</param>
        /// <param name="passed">检测结果，用于文件名中的 OK / NG。</param>
        public static string Save(Bitmap image, string tag, bool passed)
        {
            if (image == null) return null;

            try
            {
                string root = EnsureDirectory();
                string dayDir = Path.Combine(root, DateTime.Now.ToString("yyyy-MM-dd"));
                if (!Directory.Exists(dayDir)) Directory.CreateDirectory(dayDir);

                string safeTag = string.IsNullOrWhiteSpace(tag) ? "frame" : Sanitize(tag);
                string name = string.Format("{0:HHmmss_fff}_{1}_{2}{3}",
                    DateTime.Now, safeTag, passed ? "OK" : "NG", FileExtension);

                string path = Path.Combine(dayDir, name);

                // 复制一份再保存：避免与原图的 PixelFormat / 生命周期相互干扰
                using (Bitmap copy = new Bitmap(image))
                {
                    copy.Save(path, GetImageFormat());
                }

                return path;
            }
            catch (Exception ex)
            {
                Logger.Error("保存图像失败：" + ex.Message);
                return null;
            }
        }

        /// <summary>把任意字符串整理成合法的文件名片段。</summary>
        private static string Sanitize(string text)
        {
            var sb = new StringBuilder();
            foreach (char c in text)
            {
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_') sb.Append(c);
                else sb.Append('_');
            }
            return sb.ToString();
        }

        /// <summary>清理超过保留天数的历史图片，返回删除的文件数。</summary>
        public static int Cleanup()
        {
            SystemSettings s = ConfigStore.Current.System;
            if (s.RetentionDays <= 0) return 0;

            if (string.IsNullOrWhiteSpace(s.ImageSavePath) || !Directory.Exists(s.ImageSavePath))
                return 0;

            DateTime deadline = DateTime.Now.Date.AddDays(-s.RetentionDays);
            int deleted = 0;

            try
            {
                foreach (string file in Directory.GetFiles(s.ImageSavePath, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        var fi = new FileInfo(file);
                        if (fi.LastWriteTime < deadline)
                        {
                            fi.Delete();
                            deleted++;
                        }
                    }
                    catch (Exception)
                    {
                        // 单个文件被占用时跳过，不影响其余文件
                    }
                }

                // 清掉空掉的日期目录
                foreach (string dir in Directory.GetDirectories(s.ImageSavePath))
                {
                    try
                    {
                        if (Directory.GetFileSystemEntries(dir).Length == 0)
                            Directory.Delete(dir);
                    }
                    catch (Exception)
                    {
                        // 目录非空或无权删除时忽略
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("清理历史图片失败：" + ex.Message);
            }

            if (deleted > 0)
                Logger.Info("清理过期图片 " + deleted + " 个（保留 " + s.RetentionDays + " 天）");

            return deleted;
        }
    }
}
