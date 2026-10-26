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

        /// <summary>日志数据库文件（SQLite，落盘全部运行日志与判定结果）。</summary>
        public static string LogDbFile
        {
            get { return Path.Combine(LogDir, "logs.db"); }
        }

        /// <summary>
        /// 模板资产目录（<c>%AppData%\VisionPlatform\Templates\&lt;FlowId&gt;\&lt;NodeId&gt;\</c> 的根）。
        /// <para>
        /// 【为什么放这里而不放配方目录】本系统的配方（<c>Recipe</c>）只装 <c>InspectionConfig</c>，
        /// 流程与节点挂在 <c>appconfig.json</c> 的 <c>Flows</c> 段 —— "随配方目录存放"在本系统里没有落点。
        /// 按 <c>FlowId/NodeId</c> 存放后：换配方 → 流程不变 → 模板自动仍就绪；换流程 → 模板天然隔离
        /// （A/B 两套不串用）；整个目录拷到另一台同版本机器即就绪。
        /// </para>
        /// </summary>
        public static string TemplateDir { get { return Path.Combine(Root, "Templates"); } }

        /// <summary>
        /// OCR 字符模板库的默认目录（<c>%AppData%\VisionPlatform\OcrChars</c>）。
        /// <para>
        /// 【只是"新建节点时的默认值"】老节点的 <c>TemplateDir</c> 参数一个字都不改 ——
        /// 早期版本把模板目录指向程序目录下的 <c>OCRModel</c>，那些配置文件里的值必须继续生效
        /// （见 <c>FlowModule.CreateNewParams</c> 与 <c>CreateDefaultParams</c> 的分工）。
        /// </para>
        /// </summary>
        public static string OcrCharsDir { get { return Path.Combine(Root, "OcrChars"); } }

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
                if (!Directory.Exists(TemplateDir)) Directory.CreateDirectory(TemplateDir);
            }
            catch (Exception)
            {
                // 目录创建失败由调用方处理
            }
        }
    }
}
