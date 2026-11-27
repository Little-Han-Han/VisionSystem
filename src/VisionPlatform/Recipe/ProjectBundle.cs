using System;
using System.Collections.Generic;
using System.IO;
using VisionPlatform.Core;
using VisionPlatform.Flow;

namespace VisionPlatform.Recipe
{
    /// <summary>
    /// 工程包：把「通讯配置 + 流程配置 + 算法参数 + 模板资产」打包进方案目录，支持整体换线。
    /// <para>
    /// 【为什么需要它】本系统的 <see cref="Recipe"/> 只装 <c>Inspection</c>（算法参数快照），
    /// 流程挂在 <c>appconfig.json</c> 的 <c>Flows</c> 段、通讯挂在 <c>Comm</c> 段、
    /// 模板按 <c>Templates\&lt;FlowId&gt;\&lt;NodeId&gt;</c> 存放 —— 三者都不在配方里。
    /// 于是"把 A 机的工程搬到 B 机"只能手工拷 appconfig + Templates，极易漏项。
    /// 工程包把这四类东西统一放进 <c>Recipes\&lt;方案名&gt;\</c>，一个目录拷走即可。
    /// </para>
    /// <para>
    /// 【目录布局】
    /// <code>
    /// Recipes\&lt;方案名&gt;\
    ///   recipe.json      算法参数 / 相机 / 绑定流程（既有格式，未改）
    ///   comm.json        通讯：端点 / 点位 / 消息模板 / 常驻任务 / 流程状态绑定
    ///   flows.json       流程：节点 / 连线 / 相机与视图绑定 / NG 策略 / 整帧超时
    ///   Templates\       模板资产（按 FlowId\NodeId 分层，与全局目录同构）
    ///   OCRModel\        字符库（可选，体积大，默认不自动复制）
    ///   Models\          深度学习模型（可选，体积大，默认不自动复制）
    ///   _backup\         导入前的 appconfig 与 Templates 备份（只保留最近若干份）
    /// </code>
    /// </para>
    /// <para>
    /// 【导入是破坏性操作】会替换当前工程的流程与通讯。因此导入前<strong>强制</strong>备份
    /// 当前 <c>appconfig.json</c> 与 <c>Templates</c> 到 <c>_backup\</c>，
    /// 出问题可以直接把备份文件拷回去，不需要重装软件。
    /// </para>
    /// </summary>
    public static class ProjectBundle
    {
        /// <summary>工程包内通讯配置文件名。</summary>
        public const string CommFile = "comm.json";

        /// <summary>工程包内流程配置文件名。</summary>
        public const string FlowsFile = "flows.json";

        /// <summary>
        /// 工程包内标定配置文件名。
        /// <para>【为什么归方案不归全局】标定跟"相机 + 镜头 + 工位"绑定：同一台机器换方案
        /// （不同产品、不同工位）时标定往往不同。放全局会导致换方案后把别的工位的标定带过来，
        /// 像素→机械坐标换算整条链全错，而且很难发现。</para>
        /// </summary>
        public const string CalibFile = "calibrations.json";

        /// <summary>工程包内模板资产子目录（与全局 <c>Templates</c> 同构）。</summary>
        public const string TemplateFolder = "Templates";

        /// <summary>导入前备份目录（不参与导出）。</summary>
        public const string BackupFolder = "_backup";

        /// <summary>备份保留份数上限，超出按时间删除最旧的。</summary>
        private const int MaxBackups = 5;

        /// <summary>工程包内容摘要（给界面与日志看，不含具体配置对象）。</summary>
        public sealed class Contents
        {
            public bool HasComm;
            public bool HasFlows;
            public int EndpointCount;
            public int PointCount;
            public int MessageCount;
            public int JobCount;
            public int FlowBindingCount;
            public int FlowCount;
            public int NodeCount;
            public int TemplateFileCount;

            /// <summary>是否携带工程部分（通讯或流程）。只有 recipe.json 时返回 false。</summary>
            public bool HasProject { get { return HasComm || HasFlows; } }

            public override string ToString()
            {
                if (!HasProject) return "仅算法参数（普通配方）";

                var parts = new List<string>();
                if (HasComm)
                    parts.Add(string.Format("通讯 {0} 链路 / {1} 点位 / {2} 消息 / {3} 任务",
                        EndpointCount, PointCount, MessageCount, JobCount));
                if (HasFlows)
                    parts.Add(string.Format("流程 {0} 条 / 节点 {1} 个", FlowCount, NodeCount));
                if (TemplateFileCount > 0)
                    parts.Add("模板 " + TemplateFileCount + " 个文件");
                return string.Join("；", parts.ToArray());
            }
        }

        /// <summary>读取方案目录里的工程包摘要；目录或文件不存在时返回空摘要（不抛异常）。</summary>
        public static Contents Inspect(string recipeName)
        {
            var c = new Contents();
            string dir = AppPaths.RecipePath(recipeName);

            try
            {
                string commPath = Path.Combine(dir, CommFile);
                if (File.Exists(commPath))
                {
                    CommSettings comm = Json.LoadFromFile<CommSettings>(commPath);
                    if (comm != null)
                    {
                        c.HasComm = true;
                        c.EndpointCount = comm.Endpoints != null ? comm.Endpoints.Count : 0;
                        c.PointCount = comm.Points != null ? comm.Points.Count : 0;
                        c.MessageCount = comm.Messages != null ? comm.Messages.Count : 0;
                        c.JobCount = comm.Jobs != null ? comm.Jobs.Count : 0;
                        c.FlowBindingCount = comm.FlowBindings != null ? comm.FlowBindings.Count : 0;
                    }
                }

                string flowsPath = Path.Combine(dir, FlowsFile);
                if (File.Exists(flowsPath))
                {
                    FlowSettings fs = Json.LoadFromFile<FlowSettings>(flowsPath);
                    if (fs != null && fs.Flows != null)
                    {
                        c.HasFlows = true;
                        c.FlowCount = fs.Flows.Count;
                        foreach (FlowDefinition f in fs.Flows)
                        {
                            if (f != null && f.Nodes != null) c.NodeCount += f.Nodes.Count;
                        }
                    }
                }

                string tplDir = Path.Combine(dir, TemplateFolder);
                if (Directory.Exists(tplDir))
                    c.TemplateFileCount = Directory.GetFiles(tplDir, "*", SearchOption.AllDirectories).Length;
            }
            catch (Exception ex)
            {
                Logger.Warn("读取工程包摘要失败：" + ex.Message);
            }

            return c;
        }

        /// <summary>
        /// 把当前工程的通讯与流程写进方案目录（<c>comm.json</c> / <c>flows.json</c>）。
        /// <para>由 <see cref="RecipeStore.Save"/> 在保存配方后调用，因此「保存配方」即「保存整个工程」。</para>
        /// </summary>
        public static OpResult WriteProjectFiles(string recipeName)
        {
            return WriteProjectFiles(recipeName, ConfigStore.Current);
        }

        /// <summary>
        /// 把指定配置里的<strong>方案级</strong>数据写进方案目录（通讯 / 流程 / 标定）。
        /// <para>【为什么要传 cfg】迁移时 <c>ConfigStore.Current</c> 还没指向新对象，
        /// 用静态属性会写到旧数据上。</para>
        /// </summary>
        public static OpResult WriteProjectFiles(string recipeName, AppConfig cfg)
        {
            if (string.IsNullOrWhiteSpace(recipeName)) return OpResult.Fail("方案名为空");
            if (cfg == null) return OpResult.Fail("配置为空");

            try
            {
                string dir = AppPaths.RecipePath(recipeName);
                Directory.CreateDirectory(dir);
                Directory.CreateDirectory(Path.Combine(dir, TemplateFolder));

                if (cfg.Comm != null)
                    Json.SaveToFile(Path.Combine(dir, CommFile), cfg.Comm);
                if (cfg.Flows != null)
                    Json.SaveToFile(Path.Combine(dir, FlowsFile), cfg.Flows);
                if (cfg.Calibrations != null)
                    Json.SaveToFile(Path.Combine(dir, CalibFile), cfg.Calibrations);

                return OpResult.Ok("工程配置已写入 " + dir);
            }
            catch (Exception ex)
            {
                return OpResult.Fail("写入工程配置失败：" + ex.Message);
            }
        }

        /// <summary>
        /// 导出当前工程到方案目录：配置 + 模板资产。
        /// </summary>
        /// <param name="recipeName">方案名（目录名）。</param>
        /// <param name="includeTemplates">
        /// 是否复制全局 <c>Templates</c>（按 FlowId/NodeId 分层，通常体积可控）。
        /// OCRModel / Models 体积可能上百 MB，<strong>不在本方法内复制</strong>，
        /// 需要随包迁移时请手工拷对应目录（方案说明里已写明）。
        /// </param>
        public static OpResult Export(string recipeName, bool includeTemplates = true)
        {
            OpResult w = WriteProjectFiles(recipeName);
            if (!w.Success) return w;

            if (!includeTemplates) return OpResult.Ok("工程配置已导出（未复制模板）");

            try
            {
                string dst = Path.Combine(AppPaths.RecipePath(recipeName), TemplateFolder);
                int copied = CopyDirectory(AppPaths.TemplateDir, dst);
                return OpResult.Ok(string.Format("工程配置已导出，模板 {0} 个文件", copied));
            }
            catch (Exception ex)
            {
                return OpResult.Fail("导出模板失败：" + ex.Message);
            }
        }

        /// <summary>
        /// 从方案目录导入整工程：备份当前配置 → 应用通讯与流程 → 保存 → 覆盖模板。
        /// <para>
        /// 只应用工程包里<strong>存在</strong>的段：只有 <c>recipe.json</c> 的普通配方
        /// 不会动当前流程与通讯（与升级前行为一致）。
        /// </para>
        /// </summary>
        /// <param name="recipeName">方案名。</param>
        /// <param name="includeTemplates">是否用包内模板覆盖全局模板（只覆盖同名文件，不删除多余项）。</param>
        /// <param name="save">
        /// 是否立即落盘。默认 true；回归测试传 false 只验证内存切换，避免污染开发用的 appconfig。
        /// </param>
        public static OpResult Import(string recipeName, bool includeTemplates = true, bool save = true)
        {
            if (string.IsNullOrWhiteSpace(recipeName)) return OpResult.Fail("方案名为空");

            Contents c = Inspect(recipeName);
            if (!c.HasProject)
                return OpResult.Ok("该方案只含算法参数，无需导入工程配置");

            try
            {
                string dir = AppPaths.RecipePath(recipeName);
                AppConfig cfg = ConfigStore.Current;

                // ① 先备份：导入失败/不满意时可以原样还原
                string backupDir = BackupCurrent(dir);

                // ② 通讯
                string commPath = Path.Combine(dir, CommFile);
                if (File.Exists(commPath))
                {
                    CommSettings comm = Json.LoadFromFile<CommSettings>(commPath);
                    if (comm == null) return OpResult.Fail("通讯配置解析失败：" + CommFile);
                    CommSettings.UpgradeToCurrent(comm);
                    cfg.Comm = comm;
                }

                // ③ 流程
                string flowsPath = Path.Combine(dir, FlowsFile);
                if (File.Exists(flowsPath))
                {
                    FlowSettings fs = Json.LoadFromFile<FlowSettings>(flowsPath);
                    if (fs == null) return OpResult.Fail("流程配置解析失败：" + FlowsFile);
                    FlowSettings.UpgradeToCurrent(fs);
                    if (fs.Flows == null || fs.Flows.Count == 0)
                        return OpResult.Fail("流程配置里没有任何流程，已放弃导入（当前工程未改动）");
                    cfg.Flows = fs;
                }

                // ③b 标定
                string calibPath = Path.Combine(dir, CalibFile);
                if (File.Exists(calibPath))
                {
                    var cal = Json.LoadFromFile<VisionPlatform.Calibration.CalibSettings>(calibPath);
                    if (cal != null)
                    {
                        VisionPlatform.Calibration.CalibSettings.Normalize(cal);
                        cfg.Calibrations = cal;
                    }
                }

                // ④ 落盘：ConfigStore.Changed 会让 CommRegistry 按新端点重建连接
                if (save) ConfigStore.Save();

                // ⑤ 模板资产（只覆盖同名，不删除——宁可留旧文件也不误删现场资产）
                int copied = 0;
                string srcTpl = Path.Combine(dir, TemplateFolder);
                if (includeTemplates && Directory.Exists(srcTpl))
                    copied = CopyDirectory(srcTpl, AppPaths.TemplateDir);

                Logger.Info(string.Format("已导入工程包「{0}」：{1}；模板 {2} 个文件；备份 {3}",
                    recipeName, c.ToString(), copied, backupDir ?? "(无)"));

                return OpResult.Ok(string.Format("已导入：{0}（原配置备份在 {1}）", c, backupDir ?? "无"));
            }
            catch (Exception ex)
            {
                return OpResult.Fail("导入工程包失败：" + ex.Message);
            }
        }

        /// <summary>把当前 appconfig 与全局 Templates 备份到方案目录的 _backup\，返回备份位置。</summary>
        private static string BackupCurrent(string recipeDir)
        {
            string dir = Path.Combine(recipeDir, BackupFolder);
            Directory.CreateDirectory(dir);

            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string cfg = AppPaths.ConfigFile;
            if (File.Exists(cfg))
                File.Copy(cfg, Path.Combine(dir, "appconfig-" + stamp + ".json"), true);

            if (Directory.Exists(AppPaths.TemplateDir))
                CopyDirectory(AppPaths.TemplateDir, Path.Combine(dir, "Templates-" + stamp));

            TrimBackups(dir);
            return dir;
        }

        /// <summary>只保留最近 <see cref="MaxBackups"/> 份备份，避免反复导入把磁盘塞满。</summary>
        private static void TrimBackups(string backupDir)
        {
            try
            {
                var items = new List<FileSystemInfo>();
                items.AddRange(new DirectoryInfo(backupDir).GetFiles("appconfig-*.json"));
                items.AddRange(new DirectoryInfo(backupDir).GetDirectories("Templates-*"));
                items.Sort(delegate(FileSystemInfo a, FileSystemInfo b)
                {
                    return b.LastWriteTime.CompareTo(a.LastWriteTime);
                });

                for (int i = MaxBackups; i < items.Count; i++)
                {
                    try
                    {
                        var f = items[i] as FileInfo;
                        if (f != null) { f.Delete(); continue; }
                        var d = items[i] as DirectoryInfo;
                        if (d != null) d.Delete(true);
                    }
                    catch (Exception) { }
                }
            }
            catch (Exception) { }
        }

        /// <summary>递归复制目录，返回复制的文件数。目标目录不存在时自动创建。</summary>
        private static int CopyDirectory(string src, string dst)
        {
            if (!Directory.Exists(src)) return 0;
            Directory.CreateDirectory(dst);

            int count = 0;
            foreach (string file in Directory.GetFiles(src))
            {
                string target = Path.Combine(dst, Path.GetFileName(file));
                try { File.Copy(file, target, true); count++; }
                catch (IOException ex) { Logger.Warn("复制文件失败：" + target + " → " + ex.Message); }
            }

            foreach (string sub in Directory.GetDirectories(src))
                count += CopyDirectory(sub, Path.Combine(dst, Path.GetFileName(sub)));

            return count;
        }
    }
}
