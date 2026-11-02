using System;
using System.IO;

namespace VisionPlatform.Core
{
    /// <summary>应用程序数据目录与文件路径。</summary>
    public static class AppPaths
    {
        /// <summary>
        /// 用户数据根目录 = <strong>程序所在目录</strong>（如 <c>bin\x64\Release</c>）。
        /// <para>
        /// 【为什么从 %AppData% 挪到程序目录】现场是"整个 bin 目录拷走即部署"，
        /// 配置/配方/模板/模型跟着程序走才不用二次搬运；模型本来就放在 <c>models\</c> 下，
        /// 之前 Root 指向 AppData 导致深度学习节点去 AppData 找 models 而找不到。
        /// </para>
        /// <para>
        /// 日志仍留在 %AppData%（见 <see cref="LogDir"/>）：日志是运行产物，
        /// 不该混进要打包拷贝的程序目录。
        /// </para>
        /// </summary>
        public static string Root
        {
            get { return AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
        }

        /// <summary>用户配置的旧位置（仅日志与兼容迁移用）：<c>%AppData%\VisionPlatform</c>。</summary>
        public static string LegacyRoot
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
        // 日志刻意留在 %AppData%：它属于运行产物，不该跟着程序目录被打包/覆盖
        public static string LogDir { get { return Path.Combine(LegacyRoot, "Logs"); } }

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
        /// OCR 字符模板库的默认目录：<strong>程序目录下的 <c>OCRModel</c></strong>
        /// （即 <c>bin\x64\Release\OCRModel</c>）。
        /// <para>
        /// 【为什么回到程序目录】用户明确要求模板放在 <c>bin\x64\Release\OCRModel</c>：
        /// 现场排查时直接进程序目录就能看到样本，拷机器时随程序一起搬走，不必去翻 AppData。
        /// 哈希/时间戳都无关紧要，这里只是"新建节点时的默认值"——
        /// 老节点配置里的 <c>TemplateDir</c> 一个字都不改（见 <c>FlowModule.CreateNewParams</c>）。
        /// </para>
        /// <para>【注意】节点参数里存的是<strong>相对路径</strong> <c>"OCRModel"</c>，
        /// 运行时按 <c>AppDomain.BaseDirectory</c> 解析；本属性只给"管理窗体 / 迁移目标"用绝对值。</para>
        /// </summary>
        public static string OcrCharsDir
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "OCRModel"); }
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
                if (!Directory.Exists(TemplateDir)) Directory.CreateDirectory(TemplateDir);
            }
            catch (Exception)
            {
                // 目录创建失败由调用方处理
            }
        }
    }
}
