using System;
using System.Collections.Generic;
using System.Windows.Forms;
using VisionPlatform.Core;
using VisionLicense;
using VisionPlatform.Data;
using VisionPlatform.Recipe;
using VisionPlatform.UI;
// 命名空间 VisionPlatform.Recipe 与类 Recipe 同名，用别名避免歧义
using RecipeModel = VisionPlatform.Recipe.Recipe;

namespace VisionPlatform
{
    /// <summary>全局运行时状态（登录用户等）。</summary>
    internal static class AppContext
    {
        public static Models.User CurrentUser { get; set; }

        /// <summary>
        /// 是否拥有全部权限（唯一判定口径）。
        /// <para>软件启动不登录，<c>CurrentUser</c> 是「访客 / Guest」→ 本属性 false，
        /// 界面上只有日志可看；管理员在工具栏点「登录」通过校验后 <c>CurrentUser.IsAdmin</c>
        /// 为 true → 本属性 true，全部功能解锁。</para>
        /// <para>判定一律走这里，不要在界面各处重复写 <c>Role == "Admin"</c>，
        /// 否则以后加角色（如班长 / 工程师）要改一堆地方。</para>
        /// </summary>
        public static bool HasFullAccess
        {
            get { return CurrentUser != null && CurrentUser.IsAdmin; }
        }

        /// <summary>算法库实例，全局共享（模板训练状态保存在其中）。</summary>
        public static Native.VisionAlgo Algo { get; set; }

        /// <summary>
        /// 当前许可证（<c>LicenseMode=Off</c> 或未读到许可证时为 <c>null</c>）。
        /// <para>功能位判断统一走 <see cref="HasFeature"/> / <see cref="CanUseDeepLearning"/>，
        /// 不要在界面各处硬编码字符串。</para>
        /// </summary>
        public static VisionLicense.LicenseInfo License { get; set; }

        /// <summary>许可证是否包含某功能位；无许可证（未启用校验）时返回 <c>true</c>，不限制。</summary>
        public static bool HasFeature(string key)
        {
            if (License == null) return true;
            return License.HasFeature(key);
        }

        /// <summary>
        /// 是否允许使用深度学习模块（功能位 <c>deep</c>）。
        /// <para>传统算法（<c>classic</c>）不在这里判断 —— 它永远包含在许可证里。</para>
        /// </summary>
        public static bool CanUseDeepLearning
        {
            get { return HasFeature("deep"); }
        }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            // 【第一件事：目录布局迁移】老版本目录名是小写/旧名（Config / OCRModel / models），
            // 新版本统一大写并把两类模型收进 MODELS\。必须在 NativeDllPath.Register 与
            // AppPaths.Ensure 之前做：前者要找 THIRDPARTY，后者会创建新目录让迁移失效。
            // 迁移细节在日志就绪后补记（此时 Logger 还不可用）。
            List<string> layoutMoves = Core.PathMigration.EnsureLayout(Core.AppPaths.Root);

            // 第二行：把 THIRDPARTY\* 各子目录加进本进程 DLL 搜索路径。
            // 必须早于任何 P/Invoke（VisionAlgo.Native / sqlite3 都是首次调用时加载）。
            Core.NativeDllPath.Register();

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            
            // 全局异常兜底：把 UI 线程 / 非 UI 线程的未处理异常落到日志，方便定位
            // （例如用户报告「画 ROI 报错」却看不到具体堆栈时，能从日志拿到精确异常）。
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) =>
                Logger.Error("", "UI 线程未处理异常", e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                var ex = e.ExceptionObject as Exception;
                Logger.Error("", "未处理异常", ex ?? new Exception(e.ExceptionObject != null ? e.ExceptionObject.ToString() : "(null)"));
            };

            AppPaths.Ensure();
            ConfigStore.Load();

            // 日志数据库：把运行日志（含 OK/NG 判定与软件报错/异常堆栈）持久化，支持按日期查看。
            // 必须在首个 Logger.Info 之前初始化，确保启动日志也入库。
            LogDb.Initialize(ConfigStore.Current.System.DbPath, ConfigStore.Current.System.LogRetentionDays);
            Logger.EntryLogged += (s, e) => LogDb.Enqueue(e);

            // 【换色板必须在第一个窗体创建之前】UiTheme 的颜色属性是静态的，
            // 窗体构造函数里 ApplyTheme 读的就是当时的色板值；晚于此处会让首个窗体
            // （启动画面）沿用深色、后续窗体才变浅色，出现「一半深一半浅」。
            UiTheme.SetMode(UiThemeManager.ParseMode(ConfigStore.Current.Ui.Theme));
            UiStyle.ForceOverride = UiTheme.IsLight;

            // 数值框全局策略：进程级滚轮过滤器，拦住任何窗体（含运行时动态创建、
            // 未走过主题遍历）里 NumericUpDown 的滚轮改值。必须在首个窗体创建之前装好。
            NumericInputPolicy.Install();

            Logger.Info("===== VisionPlatform 启动 =====");

            // 目录布局迁移结果（迁移本身发生在 Logger 就绪之前）
            if (layoutMoves != null)
            {
                foreach (string move in layoutMoves)
                {
                    // "跳过"是失败项（多为目录被资源管理器/杀软占用）：提级成 WARN，
                    // 并说明"下次启动会重试"，否则现场只看到名字没改却不知道原因。
                    if (move.IndexOf("[跳过]", StringComparison.Ordinal) >= 0)
                        Logger.Warn("[目录迁移] " + move + "（关闭占用该目录的窗口后，下次启动会自动重试）");
                    else
                        Logger.Info("[目录迁移] " + move);
                }
            }
            Logger.Info("软件版本：" + Application.ProductVersion);

            // ---------- 授权校验 ----------
            // 放在启动画面前（拦住就是拦住，不让用户先看到界面再被踢），
            // 且必须在 Logger/LogDb 就绪之后，授权失败也要留证据。
            // 模式见 SystemSettings.LicenseMode：Off（默认）/ Warn / Enforce。
            if (!EnsureLicense())
            {
                Logger.Info("授权校验未通过，软件退出");
                Logger.Shutdown();
                LogDb.Shutdown();
                return;
            }

            // 命令行 --license：弹一个只读信息框（现场查机器码 / 看当前授权，不启动整套流程）
            if (Environment.GetCommandLineArgs() != null
                && Array.IndexOf(Environment.GetCommandLineArgs(), "--license") >= 0)
            {
                ShowLicenseInfo(QueryLicense());
                Logger.Shutdown();
                LogDb.Shutdown();
                return;
            }

            // 性能归因探针（VA_PERF_STATS=1 才启动）：把连续运行的 CPU 拆到算法/界面/线程上。
            // 平时完全不启动线程、不产生日志。
            Core.PerfProbe.Start();

            // 模块包（算法模块的声明：端子 / 参数表 / 界面提示）按需从程序目录动态加载
            //（缺失时回退 Modules 子目录，见 FlowModuleRegistry.EnsureLoaded）。
            // 宿主不编译期引用模块包：少一个可选模块不会让软件起不来，日志里能看到原因。
            // 通讯：协议插件 + 点位/任务引擎（心跳、Ready/Busy、跨流程结果汇总）
            Communication.CommHub.Initialize();
            Communication.CommJobEngine.Initialize();

            Framework.Modules.FlowModuleRegistry.Log = delegate(string msg) { Logger.Info(msg); };
            Framework.Modules.FlowModuleRegistry.EnsureLoaded();
            Logger.Info(string.Format("模块包：已注册 {0} 个模块，加载失败 {1} 项（目录 {2}）",
                Framework.Modules.FlowModuleRegistry.Count,
                Framework.Modules.FlowModuleRegistry.LoadErrors,
                Framework.Modules.FlowModuleRegistry.LoadedFrom));

            // 恢复算法参数：优先加载上次使用的配方；无命名配方时直接用保存的检测参数快照
            RestoreAlgorithmConfig();

            // 配方切换时把算法参数（检测参数 + 配方名）同步回配置文件
            RecipeStore.CurrentChanged += (s, e) =>
            {
                var algo = ConfigStore.Current.Algorithm;
                algo.Inspection = (RecipeStore.Current != null && RecipeStore.Current.Inspection != null)
                    ? RecipeStore.Current.Inspection.Clone() : null;
                algo.CurrentRecipeName = (RecipeStore.Current != null
                    && !string.IsNullOrEmpty(RecipeStore.Current.Name)
                    && RecipeStore.Current.Name != "默认")
                    ? RecipeStore.Current.Name : "";
                ConfigStore.Save();
            };

            // 启动时按系统设置的保留天数清理一次历史图片
            int cleaned = ImageStore.Cleanup();
            if (cleaned > 0) Logger.Info("启动清理：删除过期图片 " + cleaned + " 个");

            // ---------- 启动画面 ----------
            SplashForm splash = new SplashForm();
            splash.Show();
            Application.DoEvents();

            try
            {
                splash.RunInitSteps();
            }
            catch (Exception ex)
            {
                Logger.Error("初始化异常：" + ex.Message);
            }

            // ---------- 启动免登录：默认访客低权限（仅可查看日志）----------
            // 需求：软件启动时不再弹登录框，默认低权限，只能查看日志；
            // 管理员在工具栏点「登录」通过校验后才解锁全部功能（见 MainForm.ApplyPermissions）。
            AppContext.CurrentUser = new Models.User
            {
                Name = "访客",
                Role = "Guest",
                LoginTime = DateTime.Now
            };
            Logger.Info("以访客身份启动（低权限，仅可查看日志）");

            splash.Close();

            // ---------- 主界面 ----------
            Application.Run(new MainForm());

            if (AppContext.Algo != null)
            {
                AppContext.Algo.Dispose();
                AppContext.Algo = null;
            }

            Camera.CameraManager.Instance.CloseAll();
            Communication.CommJobEngine.Shutdown();
            Communication.CommHub.Shutdown();
            Communication.CommRegistry.Shutdown();

            // 先写退出日志并刷盘，再停日志库：保证最后一行一定落到文件里。
            Core.PerfProbe.Stop();
            Logger.Info("===== VisionPlatform 退出 =====");
            Logger.Shutdown();
            LogDb.Shutdown();
        }

        /// <summary>
        /// 启动授权校验。返回 false 表示"不允许启动"，调用方应退出。
        /// <para>
        /// 三种模式（<c>System.LicenseMode</c>）：<c>Off</c>（只记日志）/ <c>Warn</c>（不拦截）/
        /// <c>Enforce</c>（默认，不可用即弹窗并退出）。
        /// </para>
        /// <para>
        /// 【验签逻辑不在这里】全部走 <c>VisionLicense.Contract</c> —— 与签发工具共用同一份实现，
        /// 避免"签发端改了字段、客户端还在按旧格式验"这类只有现场才会暴露的漂移。
        /// </para>
        /// </summary>
        private static bool EnsureLicense()
        {
            SystemSettings sys = ConfigStore.Current != null ? ConfigStore.Current.System : null;
            string mode = SystemSettings.NormalizeLicenseMode(sys != null ? sys.LicenseMode : null);

            // 文件名兼容：现场习惯写 licence.lic，Contract 只认 license.lic
            string migrated = LicenseFileCompat.EnsureCanonical(AppPaths.LicenseDir);
            if (!string.IsNullOrEmpty(migrated)) Logger.Info(migrated);

            // 内置公钥 + 指纹阈值（Contract 的静态配置，每次启动设一次）
            LicenseChecker.SetPublicKey(LicensePublicKey.Value);
            LicenseChecker.FingerprintThreshold = ProductCatalog.DefaultFingerprintThreshold;

            if (string.Equals(mode, "Off", StringComparison.Ordinal))
            {
                // 关闭态不拦截，但仍读一次许可证：功能位（如深度学习）照样按证限制，
                // 免得开发机"关掉授权"后以为功能位也不生效。
                LicenseCheckResult probe = QueryLicense();
                if (probe.IsValid) AppContext.License = probe.Info;

                Logger.Info("授权校验已关闭（LicenseMode=Off）｜机器码 " + SafeMachineCode()
                            + "｜许可证 " + LicenseFileCompat.CanonicalPath(AppPaths.LicenseDir)
                            + "｜功能位 " + DescribeFeatures(probe.Info));
                return true;
            }

            LicenseCheckResult result = QueryLicense();
            Logger.Info("授权校验（" + mode + "）：" + result.Status
                        + (string.IsNullOrEmpty(result.Message) ? "" : "｜" + result.Message)
                        + "｜机器码 " + SafeMachineCode());

            if (result.IsValid)
            {
                AppContext.License = result.Info;          // 功能位判断的唯一数据源
                LicenseChecker.TouchState(AppPaths.LicenseDir, DateTime.UtcNow);
                Logger.Info("授权功能位：" + DescribeFeatures(result.Info));
                return true;
            }

            if (string.Equals(mode, "Warn", StringComparison.Ordinal))
            {
                Logger.Warn("授权不可用，但当前为 Warn 模式，继续启动：" + result.Status);
                return true;
            }

            // Enforce：提示原因 + 本机机器码，然后退出（不提供自助导入，换证统一走供应商）
            ShowLicenseBlocked(result);
            return false;
        }

        /// <summary>
        /// 查询当前授权（含"首次绑定模式自动激活"）。
        /// <para>
        /// 【自动激活的取舍】现场把供应商给的文件丢进 License\ 就该能用，
        /// 不该要求操作员再点一次"激活"。代价是：文件被复制到第二台机器时，
        /// Contract 会因为激活段与本机指纹不符判 MachineMismatch（正是我们要的）。
        /// </para>
        /// </summary>
        private static LicenseCheckResult QueryLicense()
        {
            LicenseChecker.SetPublicKey(LicensePublicKey.Value);

            LicenseInfo info;
            LicenseCheckResult r = LicenseChecker.Check(AppPaths.LicenseDir, out info);

            if (r.Status == LicenseStatus.NeedActivation)
            {
                Logger.Info("许可证为「首次启动绑定」模式，正在激活本机…");
                r = LicenseChecker.Activate(AppPaths.LicenseDir, out info);
                if (r.IsValid) Logger.Info("本机激活成功，许可证已与本机绑定");
            }
            return r;
        }

        /// <summary>
        /// 授权不可用时的提示（系统 MessageBox，无自定义窗体）。
        /// <para>
        /// 必须把**本机机器码**一起显示：现场唯一需要它做的事就是"把它报给供应商换证"。
        /// 不给"导入许可证"按钮是刻意的 —— 换证属于商务动作，避免现场自行塞文件绕过。
        /// </para>
        /// </summary>
        private static void ShowLicenseBlocked(LicenseCheckResult result)
        {
            string reason = result != null
                ? (string.IsNullOrEmpty(result.Message) ? result.Status.ToString() : result.Message)
                : "未找到许可证";

            string text =
                "软件无法启动：" + LicenseStatusText(result) + Environment.NewLine + Environment.NewLine
                + "原因：" + reason + Environment.NewLine + Environment.NewLine
                + "本机机器码：" + SafeMachineCode() + Environment.NewLine
                + "许可证文件：" + LicenseFileCompat.CanonicalPath(AppPaths.LicenseDir) + Environment.NewLine + Environment.NewLine
                + "请把「本机机器码」发给供应商获取许可证文件；" + Environment.NewLine
                + "拿到 license.lic 后放入上面的许可证目录，再重新启动软件。";

            MessageBox.Show(text, "软件授权", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        /// <summary>只读展示当前授权状态（<c>--license</c> 参数用）。</summary>
        private static void ShowLicenseInfo(LicenseCheckResult result)
        {
            if (result == null)
            {
                MessageBox.Show("未获取到授权信息。", "软件授权", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string text = "";

            if (result.IsValid && result.Info != null)
            {
                LicenseInfo info = result.Info;
                string features = DescribeFeatures(info);

                text = "授权有效" + Environment.NewLine + Environment.NewLine
                     + "许可证编号：" + (string.IsNullOrEmpty(info.LicenseNo) ? "—" : info.LicenseNo) + Environment.NewLine
                     + "客户：" + (string.IsNullOrEmpty(info.Customer) ? "—" : info.Customer) + Environment.NewLine
                     + "版本：" + (string.IsNullOrEmpty(info.Edition) ? "—" : info.Edition) + Environment.NewLine
                     + "有效期：" + (info.IsPerpetual ? "永久"
                            : info.ExpireUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm")) + Environment.NewLine
                     + "包含模块：" + (features.Length > 0 ? features : "—") + Environment.NewLine + Environment.NewLine
                     + "本机机器码：" + SafeMachineCode();
            }
            else
            {
                text = "授权不可用：" + LicenseStatusText(result) + Environment.NewLine + Environment.NewLine
                     + "原因：" + (string.IsNullOrEmpty(result.Message) ? "—" : result.Message) + Environment.NewLine + Environment.NewLine
                     + "本机机器码：" + SafeMachineCode() + Environment.NewLine
                     + "许可证文件：" + LicenseFileCompat.CanonicalPath(AppPaths.LicenseDir);
            }

            MessageBox.Show(text, "软件授权", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        /// <summary>状态码 → 中文（无自定义窗体后，提示文案由这里统一给）。</summary>
        private static string LicenseStatusText(LicenseCheckResult result)
        {
            if (result == null) return "未找到许可证";

            switch (result.Status)
            {
                case LicenseStatus.Valid: return "授权有效";
                case LicenseStatus.NotFound: return "未找到许可证";
                case LicenseStatus.Malformed: return "许可证文件已损坏";
                case LicenseStatus.SignatureInvalid: return "许可证签名无效";
                case LicenseStatus.VersionUnsupported: return "许可证格式过新，请升级软件";
                case LicenseStatus.ProductMismatch: return "许可证不属于本产品";
                case LicenseStatus.MachineMismatch: return "许可证与本机不匹配";
                case LicenseStatus.Expired: return "许可证已过期";
                case LicenseStatus.ClockTampered: return "系统时间异常（检测到回拨）";
                case LicenseStatus.Revoked: return "许可证已吊销";
                case LicenseStatus.NeedActivation: return "许可证待激活";
                case LicenseStatus.ActivationFailed: return "许可证激活失败";
                default: return "授权校验失败";
            }
        }

        /// <summary>功能位 → 中文串（日志与提示框共用）。</summary>
        private static string DescribeFeatures(LicenseInfo info)
        {
            if (info == null || info.Features == null || info.Features.Length == 0) return "—";

            string names = "";
            foreach (KeyValuePair<string, string> kv in ProductCatalog.FeatureKeys)
            {
                if (!info.HasFeature(kv.Key)) continue;
                if (names.Length > 0) names += "、";
                names += kv.Value;
            }
            return names.Length > 0 ? names : "—";
        }

        /// <summary>采集机器码（只用于日志，失败返回 "(采集失败)"）。</summary>
        private static string SafeMachineCode()
        {
            try
            {
                MachineFingerprint fp = MachineFingerprint.Collect();
                return fp != null ? fp.Code : "(采集失败)";
            }
            catch (Exception)
            {
                return "(采集失败)";
            }
        }

        /// <summary>根据配置文件中的算法参数，恢复上次使用的配方或检测参数快照。</summary>
        private static void RestoreAlgorithmConfig()
        {
            AlgorithmSettings algo = ConfigStore.Current.Algorithm;

            if (!string.IsNullOrEmpty(algo.CurrentRecipeName))
            {
                var r = RecipeStore.Load(algo.CurrentRecipeName);
                if (r.Success) return;

                Logger.Warn("上次配方不存在：" + algo.CurrentRecipeName + "，改用保存的检测参数快照");
            }

            if (algo.Inspection != null)
            {
                RecipeModel def = RecipeModel.CreateDefault("默认");
                def.Inspection = algo.Inspection;
                RecipeStore.SetCurrent(def);
            }
        }
    }
}
