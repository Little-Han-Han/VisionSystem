using System;
using System.IO;

namespace VisionPlatform.Core
{
    /// <summary>应用程序数据目录与文件路径。</summary>
    public static class AppPaths
    {
        /// <summary>数据根目录（%AppData%\VisionPlatform）。</summary>
        public static string Root
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "VisionPlatform");
            }
        }

        /// <summary>配方目录。</summary>
        public static string RecipeDir { get { return Path.Combine(Root, "Recipes"); } }

        /// <summary>日志目录。</summary>
        public static string LogDir { get { return Path.Combine(Root, "Logs"); } }

        /// <summary>当日日志文件。</summary>
        public static string LogFile
        {
            get { return Path.Combine(LogDir, DateTime.Now.ToString("yyyy-MM-dd") + ".log"); }
        }

        /// <summary>全局配置文件。</summary>
        public static string ConfigFile { get { return Path.Combine(Root, "appconfig.json"); } }

        /// <summary>确保目录存在。</summary>
        public static void Ensure()
        {
            try
            {
                if (!Directory.Exists(Root)) Directory.CreateDirectory(Root);
                if (!Directory.Exists(RecipeDir)) Directory.CreateDirectory(RecipeDir);
                if (!Directory.Exists(LogDir)) Directory.CreateDirectory(LogDir);
            }
            catch (Exception)
            {
                // 目录创建失败由调用方处理
            }
        }
    }
}
