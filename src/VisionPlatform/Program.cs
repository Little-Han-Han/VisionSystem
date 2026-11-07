using System;
using System.Windows.Forms;
using VisionPlatform.Core;
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
    }

    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
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

            Logger.Info("===== VisionPlatform 启动 =====");
            Logger.Info("软件版本：" + Application.ProductVersion);

            // 模块包（算法模块的声明：端子 / 参数表 / 界面提示）按需从 Modules 目录动态加载。
            // 宿主不编译期引用模块包：少一个可选模块不会让软件起不来，日志里能看到原因。
            Framework.Modules.FlowModuleRegistry.Log = delegate(string msg) { Logger.Info(msg); };
            Framework.Modules.FlowModuleRegistry.EnsureLoaded();
            Logger.Info(string.Format("模块包：已注册 {0} 个模块，加载失败 {1} 项（目录 {2}）",
                Framework.Modules.FlowModuleRegistry.Count,
                Framework.Modules.FlowModuleRegistry.LoadErrors,
                Framework.Modules.FlowModuleRegistry.ModuleDirectory));

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
            LogDb.Shutdown();
            Logger.Info("===== VisionPlatform 退出 =====");
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
