using System;
using System.Windows.Forms;
using VisionPlatform.Core;
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

            AppPaths.Ensure();
            ConfigStore.Load();
            Logger.Info("===== VisionPlatform 启动 =====");

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

            // ---------- 登录 ----------
            using (LoginForm login = new LoginForm())
            {
                if (login.ShowDialog(splash) != DialogResult.OK)
                {
                    Logger.Info("用户取消登录，程序退出");
                    splash.Close();
                    return;
                }
                AppContext.CurrentUser = login.User;
            }

            Logger.Info("用户登录：" + AppContext.CurrentUser);
            splash.Close();

            // ---------- 主界面 ----------
            Application.Run(new MainForm());

            if (AppContext.Algo != null)
            {
                AppContext.Algo.Dispose();
                AppContext.Algo = null;
            }

            Camera.CameraManager.Instance.CloseAll();
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
