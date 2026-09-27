using System;
using System.Windows.Forms;
using VisionPlatform.Core;
using VisionPlatform.UI;

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
            SettingsStore.Load();
            Logger.Info("===== VisionPlatform 启动 =====");

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
    }
}
