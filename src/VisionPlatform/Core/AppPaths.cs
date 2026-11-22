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

        /// <summary>
        /// 配置目录 = <c>&lt;程序目录&gt;\Config</c>。
        /// <para>
        /// 【为什么把 appconfig.json 从程序根目录挪进来】它是<b>会被频繁改写的运行配置</b>
        /// （系统/算法/界面/流程四类参数），放在根目录会和 exe / DLL 混在一起：
        /// 现场升级只换二进制时容易连配置一起覆盖，清理目录时也容易误删。
        /// 独立成 Config\ 后，"升级保留 Config\"是一条明确的部署规则。
        /// </para>
        /// </summary>
        public static string ConfigDir { get { return Path.Combine(Root, "Config"); } }

        /// <summary>全局配置文件：<c>&lt;程序目录&gt;\Config\appconfig.json</c>。</summary>
        public static string ConfigFile { get { return Path.Combine(ConfigDir, "appconfig.json"); } }

        /// <summary>旧位置（1.0.9.x 及之前）：<c>&lt;程序目录&gt;\appconfig.json</c>，仅用于一次性迁移。</summary>
        public static string LegacyConfigFile { get { return Path.Combine(Root, "appconfig.json"); } }

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

        /// <summary>
        /// 许可证目录 = <c>&lt;程序目录&gt;\License</c>。
        /// <para>存放 <c>license.lic</c> 与时间回拨状态 <c>state.bin</c>：
        /// 独立成目录便于现场"换授权只丢一个文件"，升级时也不会被二进制覆盖。</para>
        /// </summary>
        public static string LicenseDir { get { return Path.Combine(Root, "License"); } }

        /// <summary>配方目录。</summary>
        public static string RecipeDir { get { return Path.Combine(Root, "Recipes"); } }

        /// <summary>日志目录。</summary>
        /// <summary>日志目录：直接在程序目录下（用户要求，便于现场直接看）。</summary>
        public static string LogDir { get { return Path.Combine(Root, "Logs"); } }

        /// <summary>单个方案的根目录：<c>Recipes\&lt;方案名&gt;</c>。</summary>
        public static string RecipePath(string recipeName)
        {
            string name = string.IsNullOrWhiteSpace(recipeName) ? "未命名方案" : recipeName.Trim();
            return Path.Combine(RecipeDir, name);
        }

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

        /// <summary>确保目录存在。</summary>
        public static void Ensure()
        {
            try
            {
                if (!Directory.Exists(Root)) Directory.CreateDirectory(Root);
                if (!Directory.Exists(RecipeDir)) Directory.CreateDirectory(RecipeDir);
                if (!Directory.Exists(LogDir)) Directory.CreateDirectory(LogDir);
                if (!Directory.Exists(TemplateDir)) Directory.CreateDirectory(TemplateDir);
                if (!Directory.Exists(ConfigDir)) Directory.CreateDirectory(ConfigDir);
                if (!Directory.Exists(LicenseDir)) Directory.CreateDirectory(LicenseDir);

                // 一次性迁移：老版本的 appconfig.json 在程序根目录，静默搬到 Config\。
                // 只在"新位置还没有文件"时搬，避免覆盖用户已经在用的新配置。
                if (!File.Exists(ConfigFile) && File.Exists(LegacyConfigFile))
                {
                    try { File.Move(LegacyConfigFile, ConfigFile); }
                    catch (Exception)
                    {
                        // 移动失败（占用 / 权限）就复制一份，旧文件留着不影响使用
                        try { File.Copy(LegacyConfigFile, ConfigFile, false); }
                        catch (Exception) { }
                    }
                }
            }
            catch (Exception)
            {
                // 目录创建失败由调用方处理
            }
        }
    }
}
