using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using VisionPlatform.Camera;
using VisionPlatform.Core;
using VisionPlatform.Native;
using VisionPlatform.Recipe;
using VisionPlatform.Vision;
// 命名空间 VisionPlatform.Recipe 与类 Recipe 同名，用别名避免歧义
using RecipeModel = VisionPlatform.Recipe.Recipe;
using VisionSystem.Controls;
using VisionSystem.Controls.Geometry;

namespace VisionPlatform.UI
{
    /// <summary>
    /// 主运行界面：多相机、多显示视图、循环运行 / 暂停、数据统计、运行日志。
    /// 相机管理、算法参数、通讯、配方通过工具栏按钮打开各自的子窗体。
    /// 布局全部由 MainForm.Designer.cs 生成，可在 VS 设计器中直接调整。
    /// </summary>
    public partial class MainForm : Form
    {
        private readonly List<ImageViewer> _views = new List<ImageViewer>();
        private readonly List<Label> _captions = new List<Label>();

        private bool _running;
        private bool _paused;
        private int _statTotal;
        private int _statOk;
        private int _statNg;
        private double _lastElapsedMs;

        private InspectionEngine _engine;

        private CameraForm _cameraForm;
        private AlgoForm _algoForm;
        private CommForm _commForm;
        private RecipeForm _recipeForm;

        public MainForm()
        {
            InitializeComponent();

            BuildViews(1);
            tsBtnView1.Checked = true;

            RefreshCameraList();
            Logger.Logged += OnLogged;
            RecipeStore.CurrentChanged += (s, e) => UpdateStatus();

            UpdateStatus();
            UpdateStats();
        }

        #region 视图

        /// <summary>重建显示视图：1 个 / 2 个 / 4 个（2x2）。</summary>
        private void BuildViews(int count)
        {
            int cols, rows;
            if (count <= 1) { cols = 1; rows = 1; }
            else if (count == 2) { cols = 2; rows = 1; }
            else { cols = 2; rows = 2; }

            tableViews.SuspendLayout();
            tableViews.Controls.Clear();

            foreach (ImageViewer v in _views)
            {
                if (v != null) v.Dispose();
            }
            _views.Clear();
            _captions.Clear();

            tableViews.ColumnCount = cols;
            tableViews.RowCount = rows;
            tableViews.ColumnStyles.Clear();
            tableViews.RowStyles.Clear();

            float colPct = 100f / cols;
            float rowPct = 100f / rows;
            for (int i = 0; i < cols; i++)
                tableViews.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, colPct));
            for (int i = 0; i < rows; i++)
                tableViews.RowStyles.Add(new RowStyle(SizeType.Percent, rowPct));

            for (int i = 0; i < cols * rows; i++)
            {
                var cell = new Panel();
                cell.Dock = DockStyle.Fill;
                cell.Margin = new Padding(2);
                cell.BackColor = Color.FromArgb(24, 24, 30);

                var caption = new Label();
                caption.Dock = DockStyle.Top;
                caption.Height = 20;
                caption.ForeColor = Color.Silver;
                caption.TextAlign = ContentAlignment.MiddleLeft;
                caption.Text = "  视图 " + (i + 1);

                var viewer = new ImageViewer();
                viewer.Dock = DockStyle.Fill;
                viewer.ShowHud = true;
                viewer.Options.ShowCrosshair = true;

                cell.Controls.Add(viewer);
                cell.Controls.Add(caption);

                _views.Add(viewer);
                _captions.Add(caption);

                tableViews.Controls.Add(cell, i % cols, i / cols);
            }

            tableViews.ResumeLayout(true);
        }

        private void OnLayout1(object sender, EventArgs e)
        {
            SetLayoutChecked(tsBtnView1);
            BuildViews(1);
        }

        private void OnLayout2(object sender, EventArgs e)
        {
            SetLayoutChecked(tsBtnView2);
            BuildViews(2);
        }

        private void OnLayout4(object sender, EventArgs e)
        {
            SetLayoutChecked(tsBtnView4);
            BuildViews(4);
        }

        private void SetLayoutChecked(ToolStripButton active)
        {
            tsBtnView1.Checked = ReferenceEquals(tsBtnView1, active);
            tsBtnView2.Checked = ReferenceEquals(tsBtnView2, active);
            tsBtnView4.Checked = ReferenceEquals(tsBtnView4, active);
        }

        #endregion

        #region 相机

        private void RefreshCameraList()
        {
            tsComboCameras.BeginUpdate();
            tsComboCameras.Items.Clear();

            List<CameraInfo> infos = CameraManager.Instance.Enumerate();
            foreach (CameraInfo info in infos)
                tsComboCameras.Items.Add(info);

            if (tsComboCameras.Items.Count > 0)
                tsComboCameras.SelectedIndex = 0;

            tsComboCameras.EndUpdate();
            Logger.Info("相机枚举完成，共 " + infos.Count + " 个");
        }

        private void OnRefreshCameras(object sender, EventArgs e)
        {
            RefreshCameraList();
        }

        private void OnCameraSelectionChanged(object sender, EventArgs e)
        {
            UpdateStatus();
        }

        private CameraInfo SelectedCameraInfo
        {
            get { return tsComboCameras.SelectedItem as CameraInfo; }
        }

        #endregion

        #region 采集与检测

        private void OnGrabOnce(object sender, EventArgs e)
        {
            CameraInfo info = SelectedCameraInfo;
            if (info == null) { MessageBox.Show(this, "请先选择相机。"); return; }

            var r = CameraManager.Instance.Open(info.Id);
            if (!r.Success) { MessageBox.Show(this, r.Message); return; }

            var frameResult = r.Value.GrabOne();
            if (!frameResult.Success) { MessageBox.Show(this, frameResult.Message); return; }

            ShowFrame(0, r.Value.Info.Name, frameResult.Value);
            RunInspection(frameResult.Value, 0);
        }

        /// <summary>同步采集：对所有已打开相机同时取一帧，分别显示与检测。</summary>
        private void OnSyncGrab(object sender, EventArgs e)
        {
            List<ICamera> cameras = CameraManager.Instance.OpenedCameras;
            if (cameras.Count == 0)
            {
                MessageBox.Show(this, "没有已打开的相机，请先在「相机管理」中打开。");
                return;
            }

            // 第一步：先把所有相机的帧都抓到手，尽量缩短相机之间的时间差
            var frames = new List<Bitmap>();
            var names = new List<string>();
            foreach (ICamera cam in cameras)
            {
                if (cam == null || !cam.IsOpen) continue;

                var fr = cam.GrabOne();
                if (!fr.Success)
                {
                    Logger.Warn("相机取帧失败：" + fr.Message);
                    frames.Add(null);
                }
                else
                {
                    frames.Add(fr.Value);
                }
                names.Add(cam.Info != null ? cam.Info.Name : "相机");
            }

            // 第二步：逐路显示与检测
            int count = Math.Min(frames.Count, _views.Count);
            for (int i = 0; i < count; i++)
            {
                if (frames[i] == null) continue;
                ShowFrame(i, names[i], frames[i]);
                RunInspection(frames[i], i);
            }

            Logger.Info("同步采集完成：" + count + " 路");

            // 未被显示占用的帧直接释放
            for (int i = count; i < frames.Count; i++)
            {
                if (frames[i] != null) frames[i].Dispose();
            }
        }

        private void ShowFrame(int viewIndex, string cameraName, Bitmap frame)
        {
            if (viewIndex < 0 || viewIndex >= _views.Count) return;

            if (viewIndex < _captions.Count && _captions[viewIndex] != null)
                _captions[viewIndex].Text = "  " + cameraName;

            _views[viewIndex].SetImage(frame, true);
        }

        private void RunInspection(Bitmap frame, int viewIndex)
        {
            VisionAlgo algo = AppContext.Algo;
            RecipeModel recipe = RecipeStore.Current;

            if (algo == null || recipe == null)
            {
                // 没有配方或算法库时只显示图像，不做判定
                using (frame) { }
                return;
            }

            if (_engine == null) _engine = new InspectionEngine(algo);

            using (frame)
            {
                var result = _engine.Run(frame, recipe.Inspection);
                if (!result.Success) return;

                ShowResultOnView(viewIndex, result.Value);

                _statTotal++;
                if (result.Value.Passed) _statOk++; else _statNg++;
                _lastElapsedMs = result.Value.ElapsedMs;

                UpdateStats();

                Logger.Info(string.Format("视图{0} 检测 {1}：{2}（{3} ms）",
                    viewIndex + 1,
                    result.Value.Passed ? "OK" : "NG",
                    result.Value.Summary,
                    result.Value.ElapsedMs));
            }
        }

        private void ShowResultOnView(int viewIndex, InspectionResult result)
        {
            if (viewIndex < 0 || viewIndex >= _views.Count) return;

            ImageViewer view = _views[viewIndex];
            view.ClearShapes();

            foreach (BlobResult b in result.Blobs)
            {
                var c = new CircleShape(new PointF((float)b.Cx, (float)b.Cy),
                                        Math.Max(3f, (float)Math.Sqrt(b.Area / Math.PI)));
                c.StrokeColor = result.Passed ? Color.Lime : Color.OrangeRed;
                c.LineWidth = 2f;
                view.Shapes.Add(c);
            }

            if (result.Circle != null)
            {
                var fit = new CircleShape(new PointF((float)result.Circle.Cx, (float)result.Circle.Cy),
                                          (float)Math.Max(1.0, result.Circle.Radius));
                fit.StrokeColor = Color.DeepSkyBlue;
                view.Shapes.Add(fit);
            }

            if (result.Match != null)
            {
                view.Shapes.Add(new RectangleShape((float)result.Match.X, (float)result.Match.Y,
                                                   (float)result.Match.Width, (float)result.Match.Height));
            }
        }

        #endregion

        #region 运行控制

        private void OnStartLoop(object sender, EventArgs e)
        {
            if (_running)
            {
                _paused = false;
                loopTimer.Start();
                Logger.Info("继续循环运行");
            }
            else
            {
                _running = true;
                _paused = false;
                loopTimer.Start();
                Logger.Info("开始循环运行");
            }
            UpdateStatus();
        }

        private void OnPause(object sender, EventArgs e)
        {
            if (!_running) return;

            _paused = !_paused;
            if (_paused) loopTimer.Stop(); else loopTimer.Start();

            Logger.Info(_paused ? "已暂停" : "已继续");
            UpdateStatus();
        }

        private void OnStop(object sender, EventArgs e)
        {
            loopTimer.Stop();
            _running = false;
            _paused = false;
            Logger.Info("停止循环运行");
            UpdateStatus();
        }

        private void OnLoopTick(object sender, EventArgs e)
        {
            if (!_running || _paused) return;

            try
            {
                OnSyncGrab(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Logger.Error("循环运行异常：" + ex.Message);
            }
        }

        private void OnResetStats(object sender, EventArgs e)
        {
            _statTotal = 0;
            _statOk = 0;
            _statNg = 0;
            _lastElapsedMs = 0;
            UpdateStats();
        }

        #endregion

        #region 子窗体

        private void OnOpenCameraForm(object sender, EventArgs e) { ShowChild(ref _cameraForm); }
        private void OnOpenAlgoForm(object sender, EventArgs e) { ShowChild(ref _algoForm); }
        private void OnOpenCommForm(object sender, EventArgs e) { ShowChild(ref _commForm); }
        private void OnOpenRecipeForm(object sender, EventArgs e) { ShowChild(ref _recipeForm); }

        private void ShowChild<T>(ref T form) where T : Form, new()
        {
            if (form == null || form.IsDisposed)
                form = new T();

            if (form.Visible)
            {
                form.BringToFront();
                form.Focus();
            }
            else
            {
                form.Show(this);
            }
        }

        #endregion

        #region 状态与日志

        private void UpdateStats()
        {
            lblTotalValue.Text = _statTotal.ToString();
            lblOkValue.Text = _statOk.ToString();
            lblNgValue.Text = _statNg.ToString();

            if (_statTotal > 0)
                lblYieldValue.Text = (_statOk * 100.0 / _statTotal).ToString("F1") + " %";
            else
                lblYieldValue.Text = "--";

            lblCycleValue.Text = _lastElapsedMs > 0 ? _lastElapsedMs.ToString("F0") + " ms" : "--";
        }

        private void UpdateStatus()
        {
            string user = AppContext.CurrentUser != null ? AppContext.CurrentUser.ToString() : "未登录";
            RecipeModel recipe = RecipeStore.Current;

            string runState = _running ? (_paused ? "已暂停" : "运行中") : "已停止";

            tsLabelStatus.Text = string.Format("{0}    |    配方：{1}    |    算法库：{2}",
                user,
                recipe != null ? recipe.Name : "未加载",
                AppContext.Algo != null ? "已加载" : "未加载");

            statusLabel.Text = string.Format("状态：{0}    视图：{1}    统计：{2} 件（OK {3} / NG {4}）",
                runState, _views.Count, _statTotal, _statOk, _statNg);
        }

        private void OnLogged(object sender, string line)
        {
            if (IsDisposed || !IsHandleCreated) return;

            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    listLog.Items.Add(line);
                    if (listLog.Items.Count > 1000) listLog.Items.RemoveAt(0);
                    listLog.TopIndex = listLog.Items.Count - 1;
                });
            }
            catch (Exception)
            {
                // 关闭过程中忽略
            }
        }

        #endregion

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            loopTimer.Stop();
            Logger.Logged -= OnLogged;
            CameraManager.Instance.CloseAll();
        }
    }
}
