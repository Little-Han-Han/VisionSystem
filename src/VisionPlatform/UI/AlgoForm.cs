using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using VisionPlatform.Camera;
using VisionPlatform.Core;
using VisionPlatform.Native;
using VisionPlatform.Recipe;
using VisionPlatform.Vision;
using VisionSystem.Controls;
using VisionSystem.Controls.Geometry;
using RecipeModel = VisionPlatform.Recipe.Recipe;

namespace VisionPlatform.UI
{
    /// <summary>
    /// 算法参数与离线测试窗体。
    /// 支持两种图像来源：本地图片文件（离线调试）与在线相机取帧。
    /// </summary>
    public partial class AlgoForm : Form
    {
        private ImageViewer _viewer;
        private InspectionEngine _engine;
        private InspectionResult _lastResult;

        public AlgoForm()
        {
            InitializeComponent();

            CreateViewer();
            if (cmbMorph.SelectedIndex < 0) cmbMorph.SelectedIndex = 0;

            ApplyConfigFromRecipe();
            RecipeStore.CurrentChanged += (s, e) => ApplyConfigFromRecipe();
        }

        /// <summary>显示控件在构造函数中创建（ImageViewer 是自定义控件，需在代码中实例化）。</summary>
        private void CreateViewer()
        {
            _viewer = new ImageViewer();
            _viewer.Dock = DockStyle.Fill;
            _viewer.ShowHud = true;
            _viewer.Options.ShowCrosshair = true;

            panelRight.Controls.Add(_viewer);
            // Fill 控件要放在集合最前（z-order 顶层），才会最后参与 dock 并占据剩余区域
            panelRight.Controls.SetChildIndex(_viewer, 0);
        }

        #region 参数

        private InspectionConfig BuildConfig()
        {
            var cfg = new InspectionConfig();

            cfg.Preprocess = new PreprocessParam
            {
                ToGray = true,
                BlurKsize = (int)numBlur.Value,
                Threshold = (int)numThreshold.Value,
                MorphOp = cmbMorph.SelectedIndex,
                MorphKsize = 3
            };

            cfg.EnableBlob = chkBlob.Checked;
            cfg.EnableSharpness = chkSharp.Checked;
            cfg.EnableTemplate = chkTemplate.Checked;
            cfg.EnableCircleFit = chkCircle.Checked;

            cfg.MinBlobArea = (double)numMinArea.Value;
            cfg.MaxBlobArea = (double)numMaxArea.Value;
            cfg.ExpectedBlobCount = (int)numExpected.Value;
            cfg.MinMatchScore = (double)numMinScore.Value;
            cfg.MinSharpness = (double)numMinSharp.Value;

            return cfg;
        }

        private void ApplyConfigFromRecipe()
        {
            RecipeModel r = RecipeStore.Current;
            if (r == null || r.Inspection == null) return;

            InspectionConfig c = r.Inspection;
            if (c.Preprocess != null)
            {
                numBlur.Value = Clamp(numBlur, c.Preprocess.BlurKsize);
                numThreshold.Value = Clamp(numThreshold, c.Preprocess.Threshold);
                cmbMorph.SelectedIndex = Math.Max(0, Math.Min(2, c.Preprocess.MorphOp));
            }

            chkBlob.Checked = c.EnableBlob;
            chkSharp.Checked = c.EnableSharpness;
            chkTemplate.Checked = c.EnableTemplate;
            chkCircle.Checked = c.EnableCircleFit;

            numMinArea.Value = Clamp(numMinArea, (decimal)c.MinBlobArea);
            numMaxArea.Value = Clamp(numMaxArea, (decimal)c.MaxBlobArea);
            numExpected.Value = Clamp(numExpected, c.ExpectedBlobCount);
            numMinScore.Value = Clamp(numMinScore, (decimal)c.MinMatchScore);
            numMinSharp.Value = Clamp(numMinSharp, (decimal)c.MinSharpness);
        }

        private static decimal Clamp(NumericUpDown n, decimal v)
        {
            if (v < n.Minimum) return n.Minimum;
            if (v > n.Maximum) return n.Maximum;
            return v;
        }

        #endregion

        #region 图像来源

        /// <summary>打开本地图片进行离线测试（无需相机）。</summary>
        private void OnOpenImage(object sender, EventArgs e)
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "选择测试图片";
                dlg.Filter = "图像文件|*.bmp;*.jpg;*.jpeg;*.png;*.tif;*.tiff|所有文件|*.*";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    // 先读入内存再解码：不占用文件句柄，超大图也更稳
                    byte[] bytes = File.ReadAllBytes(dlg.FileName);
                    Bitmap src;
                    using (MemoryStream ms = new MemoryStream(bytes))
                    {
                        src = new Bitmap(ms);
                    }

                    Logger.Info("载入本地图片：" + dlg.FileName +
                                string.Format("（{0}x{1}）", src.Width, src.Height));

                    // 一份交给控件显示（控件会接管所有权），一份用于检测
                    _viewer.SetImage((Bitmap)src.Clone(), true);
                    RunInspection(src);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "打开图片失败：" + ex.Message, "错误",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void OnGrabAndRun(object sender, EventArgs e)
        {
            ICamera cam = CameraManager.Instance.Current;
            if (cam == null || !cam.IsOpen)
            {
                MessageBox.Show(this, "没有已打开的相机。请先用主界面的「相机管理」打开相机，或直接用「打开本地图片」。");
                return;
            }

            var fr = cam.GrabOne();
            if (!fr.Success) { MessageBox.Show(this, "取帧失败：" + fr.Message); return; }

            _viewer.SetImage((Bitmap)fr.Value.Clone(), true);
            RunInspection(fr.Value);
        }

        private void OnFit(object sender, EventArgs e)
        {
            if (_viewer != null) _viewer.FitToWindow();
        }

        #endregion

        #region 检测

        private VisionAlgo EnsureAlgo()
        {
            if (AppContext.Algo != null) return AppContext.Algo;

            try
            {
                AppContext.Algo = new VisionAlgo();
                return AppContext.Algo;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    "算法库不可用：" + ex.Message + Environment.NewLine +
                    "请确认 VisionAlgo.Native.dll 与 opencv_world455 DLL 已复制到输出目录。",
                    "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }
        }

        private void RunInspection(Bitmap frame)
        {
            VisionAlgo algo = EnsureAlgo();
            if (algo == null) { frame.Dispose(); return; }

            if (_engine == null) _engine = new InspectionEngine(algo);

            ReleaseLastResult();

            // 没有加载配方时用默认参数，方便离线调试
            RecipeModel recipe = RecipeStore.Current;
            InspectionConfig cfg = (recipe != null && recipe.Inspection != null)
                ? recipe.Inspection
                : BuildConfig();

            using (frame)
            {
                var r = _engine.Run(frame, cfg);
                if (!r.Success)
                {
                    lblVerdict.ForeColor = Color.OrangeRed;
                    lblVerdict.Text = "检测失败：" + r.Message;
                    return;
                }

                _lastResult = r.Value;
                ShowResult(r.Value);
            }
        }

        private void OnTrain(object sender, EventArgs e)
        {
            VisionAlgo algo = EnsureAlgo();
            if (algo == null) return;

            if (_viewer.Image == null)
            {
                MessageBox.Show(this, "请先打开本地图片或从相机取一帧。");
                return;
            }

            if (_engine == null) _engine = new InspectionEngine(algo);

            // 用当前显示的图像训练（整图）
            using (Bitmap frame = (Bitmap)_viewer.Image.Clone())
            {
                var r = _engine.TrainTemplate(frame, new Rectangle());
                MessageBox.Show(this, r.Success ? "模板训练完成" : r.Message);
                if (r.Success) chkTemplate.Checked = true;
            }
        }

        private void OnSaveRecipe(object sender, EventArgs e)
        {
            RecipeModel r = RecipeStore.Current;
            if (r == null)
            {
                MessageBox.Show(this, "当前没有加载配方，请先在「配方」窗体中新建或加载一个。");
                return;
            }

            r.Inspection = BuildConfig();
            var res = RecipeStore.Save(r);
            MessageBox.Show(this, res.Success ? ("已保存到配方：" + r.Name) : res.Message);
        }

        private void ReleaseLastResult()
        {
            if (_lastResult != null)
            {
                if (_lastResult.GrayImage != null)
                {
                    _lastResult.GrayImage.Dispose();
                    _lastResult.GrayImage = null;
                }
                _lastResult = null;
            }
        }

        private void ShowResult(InspectionResult result)
        {
            // 中间结果：预处理后的灰度图
            if (result.GrayImage != null)
                _viewer.SetImage((Bitmap)result.GrayImage.Clone(), false);

            _viewer.ClearShapes();

            foreach (BlobResult b in result.Blobs)
            {
                var c = new CircleShape(new PointF((float)b.Cx, (float)b.Cy),
                                        Math.Max(3f, (float)Math.Sqrt(b.Area / Math.PI)));
                c.StrokeColor = result.Passed ? Color.Lime : Color.OrangeRed;
                c.LineWidth = 2f;
                _viewer.Shapes.Add(c);
            }

            if (result.Circle != null)
            {
                var fit = new CircleShape(new PointF((float)result.Circle.Cx, (float)result.Circle.Cy),
                                          (float)Math.Max(1.0, result.Circle.Radius));
                fit.StrokeColor = Color.DeepSkyBlue;
                _viewer.Shapes.Add(fit);
            }

            if (result.Match != null)
            {
                var rect = new RectangleShape((float)result.Match.X, (float)result.Match.Y,
                                              (float)result.Match.Width, (float)result.Match.Height);
                rect.StrokeColor = Color.Gold;
                _viewer.Shapes.Add(rect);
            }

            listResults.BeginUpdate();
            listResults.Items.Clear();
            listResults.Items.Add(string.Format("耗时 {0} ms    结果 {1}",
                result.ElapsedMs, result.Passed ? "OK" : "NG"));
            foreach (string m in result.Messages) listResults.Items.Add("  " + m);
            listResults.EndUpdate();

            lblVerdict.ForeColor = result.Passed ? Color.LimeGreen : Color.OrangeRed;
            lblVerdict.Text = (result.Passed ? "OK" : "NG") + "  —  " + result.Summary;

            Logger.Info("算法窗体检测：" + result);
        }

        #endregion

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            ReleaseLastResult();
        }
    }
}
