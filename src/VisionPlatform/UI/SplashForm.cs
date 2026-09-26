using System;
using System.Threading;
using System.Windows.Forms;
using VisionPlatform.Core;
using VisionPlatform.Native;

namespace VisionPlatform.UI
{
    /// <summary>启动加载画面：显示进度并真正完成算法库加载、相机枚举等初始化。</summary>
    public partial class SplashForm : Form
    {
        public SplashForm()
        {
            InitializeComponent();
        }

        public void SetProgress(int percent, string text)
        {
            progressBar.Value = Math.Max(0, Math.Min(100, percent));
            lblStep.Text = text;
            lblStep.Refresh();
            progressBar.Refresh();
            Application.DoEvents();
        }

        /// <summary>按顺序执行初始化步骤。</summary>
        public void RunInitSteps()
        {
            SetProgress(10, "准备目录…");
            AppPaths.Ensure();
            Thread.Sleep(120);

            SetProgress(30, "加载 C++ 算法库…");
            LoadAlgo();

            SetProgress(60, "枚举相机…");
            try
            {
                Camera.CameraManager.Instance.Enumerate();
            }
            catch (Exception ex)
            {
                Logger.Warn("相机枚举失败：" + ex.Message);
            }
            Thread.Sleep(120);

            SetProgress(85, "加载配方…");
            try
            {
                Recipe.RecipeStore.List();
            }
            catch (Exception ex)
            {
                Logger.Warn("配方枚举失败：" + ex.Message);
            }
            Thread.Sleep(120);

            SetProgress(100, "就绪");
            Thread.Sleep(150);
        }

        private void LoadAlgo()
        {
            try
            {
                AppContext.Algo = new VisionAlgo();
                Logger.Info("算法库已加载，版本：" + VisionAlgo.LibraryVersion);
            }
            catch (Exception ex)
            {
                AppContext.Algo = null;
                Logger.Error("算法库加载失败：" + ex.Message +
                             "（请确认 VisionAlgo.Native.dll 与 opencv_world455 DLL 已在输出目录）");
            }
        }
    }
}
