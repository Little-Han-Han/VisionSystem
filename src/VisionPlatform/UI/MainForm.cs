using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using VisionPlatform.Camera;
using VisionPlatform.Communication;
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
    /// 主运行界面：多相机、多显示视图（可切换布局、可把相机绑定到指定视图）、
    /// 循环运行 / 暂停、数据统计、运行日志。
    /// 相机管理、算法参数、通讯、配方、系统设置通过工具栏按钮打开各自的子窗体。
    /// 布局全部由 MainForm.Designer.cs 生成，可在 VS 设计器中直接调整。
    /// </summary>
    public partial class MainForm : Form
    {
        /// <summary>布局预设，每项为「行数, 列数」。</summary>
        private static readonly int[,] LayoutPresets = new int[,]
        {
            { 1, 1 },   // 1 视图
            { 1, 2 },   // 2 视图（横向）
            { 2, 1 },   // 2 视图（纵向）
            { 2, 2 },   // 4 视图
            { 3, 2 },   // 6 视图
            { 3, 3 }    // 9 视图
        };

        private readonly List<ImageViewer> _views = new List<ImageViewer>();
        private readonly List<Label> _captions = new List<Label>();
        private readonly List<ComboBox> _bindCombos = new List<ComboBox>();

        private int _rows;
        private int _cols;
        private bool _initializing;
        private bool _rebinding;

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
            _initializing = true;

            InitializeComponent();

            InitLayoutCombo();

            // 按保存的布局索引重建视图（InitLayoutCombo 设置下拉框会触发 OnLayoutChanged，
            // 这里再显式构建一次以保证使用保存的布局）
            int startIdx = ConfigStore.Current.Ui.LayoutIndex;
            if (startIdx < 0 || startIdx >= LayoutPresets.GetLength(0)) startIdx = 0;
            BuildViews(LayoutPresets[startIdx, 0], LayoutPresets[startIdx, 1]);

            RefreshCameraList();
            Logger.Logged += OnLogged;
            RecipeStore.CurrentChanged += (s, e) => UpdateStatus();
            CameraManager.Instance.CurrentChanged += (s, e) => RefreshCameraList();
            ConfigStore.Changed += (s, e) => UpdateStatus();

            _initializing = false;

            UpdateStatus();
            UpdateStats();
        }

        /// <summary>视图绑定下拉框的条目；CameraId 为空表示「自动分配」。</summary>
        private sealed class BindItem
        {
            public readonly string CameraId;
            public readonly string Name;

            public BindItem(string cameraId, string name)
            {
                CameraId = cameraId;
                Name = name;
            }

            public override string ToString() { return Name; }
        }

        #region 视图与布局

        private void InitLayoutCombo()
        {
            tsComboLayout.BeginUpdate();
            tsComboLayout.Items.Clear();
            tsComboLayout.Items.Add("1 x 1");
            tsComboLayout.Items.Add("1 x 2");
            tsComboLayout.Items.Add("2 x 1");
            tsComboLayout.Items.Add("2 x 2");
            tsComboLayout.Items.Add("3 x 2");
            tsComboLayout.Items.Add("3 x 3");

            int idx = ConfigStore.Current.Ui.LayoutIndex;
            if (idx < 0 || idx >= tsComboLayout.Items.Count) idx = 0;
            tsComboLayout.SelectedIndex = idx;
            tsComboLayout.EndUpdate();
        }

        /// <summary>重建显示视图网格（rows 行 × cols 列），并尽量恢复原有的相机绑定。</summary>
        private void BuildViews(int rows, int cols)
        {
            if (rows <= 0) rows = 1;
            if (cols <= 0) cols = 1;

            if (_rows == rows && _cols == cols && _views.Count == rows * cols && _views.Count > 0)
                return;

            // 重建前把当前各视图的相机绑定同步进配置，保证切换布局 / 重启后不丢失
            if (_bindCombos.Count > 0)
            {
                _rebinding = true;
                SyncViewBindingsToConfig();
                _rebinding = false;
            }

            tableViews.SuspendLayout();
            tableViews.Controls.Clear();

            foreach (ImageViewer v in _views)
            {
                if (v != null) v.Dispose();
            }
            _views.Clear();
            _captions.Clear();
            _bindCombos.Clear();

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

            for (int i = 0; i < rows * cols; i++)
            {
                var cell = new Panel();
                cell.Dock = DockStyle.Fill;
                cell.Margin = new Padding(2);
                cell.BackColor = Color.FromArgb(24, 24, 30);

                // 顶部条：视图名 + 相机绑定下拉框
                var header = new Panel();
                header.Dock = DockStyle.Top;
                header.Height = 27;
                header.BackColor = Color.FromArgb(24, 24, 30);

                var caption = new Label();
                caption.Dock = DockStyle.Left;
                caption.Width = 62;
                caption.ForeColor = Color.Silver;
                caption.TextAlign = ContentAlignment.MiddleLeft;
                caption.Text = "  视图 " + (i + 1);

                var bind = new ComboBox();
                bind.Dock = DockStyle.Fill;
                bind.DropDownStyle = ComboBoxStyle.DropDownList;
                bind.BackColor = Color.FromArgb(30, 30, 38);
                bind.ForeColor = Color.Gainsboro;
                bind.FlatStyle = FlatStyle.Flat;
                bind.SelectedIndexChanged += OnBindComboChanged;

                // Fill 控件先加，Left 控件后加，保证左侧标签先占位
                header.Controls.Add(bind);
                header.Controls.Add(caption);

                var viewer = new ImageViewer();
                viewer.Dock = DockStyle.Fill;
                viewer.ShowHud = true;
                viewer.Options.ShowCrosshair = true;

                // 同样：Fill 控件先加，Top 控件后加
                cell.Controls.Add(viewer);
                cell.Controls.Add(header);

                _views.Add(viewer);
                _captions.Add(caption);
                _bindCombos.Add(bind);

                tableViews.Controls.Add(cell, i % cols, i / cols);
            }

            _rows = rows;
            _cols = cols;

            RefreshBindCombos();

            // 从配置（UiSettings.ViewBindings）恢复每个视图的相机绑定
            List<string> bindings = ConfigStore.Current.Ui.ViewBindings ?? new List<string>();
            _rebinding = true;
            for (int i = 0; i < _bindCombos.Count; i++)
            {
                string id = i < bindings.Count ? bindings[i] : "";
                SetBindId(_bindCombos[i], id);
            }
            _rebinding = false;

            SyncViewBindingsToConfig();

            tableViews.ResumeLayout(true);
            UpdateStatus();
        }

        private void OnLayoutChanged(object sender, EventArgs e)
        {
            int idx = tsComboLayout.SelectedIndex;
            if (idx < 0) return;
            if (idx >= LayoutPresets.GetLength(0)) idx = 0;

            BuildViews(LayoutPresets[idx, 0], LayoutPresets[idx, 1]);

            if (_initializing) return;

            ConfigStore.Current.Ui.LayoutIndex = idx;
            ConfigStore.Save();
        }

        private static string GetBindId(ComboBox cb)
        {
            BindItem item = cb.SelectedItem as BindItem;
            return item != null ? item.CameraId : "";
        }

        private static void SetBindId(ComboBox cb, string cameraId)
        {
            if (string.IsNullOrEmpty(cameraId))
            {
                if (cb.Items.Count > 0) cb.SelectedIndex = 0;
                return;
            }

            for (int i = 0; i < cb.Items.Count; i++)
            {
                BindItem item = cb.Items[i] as BindItem;
                if (item != null && item.CameraId == cameraId)
                {
                    cb.SelectedIndex = i;
                    return;
                }
            }

            if (cb.Items.Count > 0) cb.SelectedIndex = 0;
        }

        /// <summary>刷新每个视图的相机绑定下拉框（只列出已打开的相机）。</summary>
        private void RefreshBindCombos()
        {
            _rebinding = true;
            try
            {
                List<ICamera> opened = CameraManager.Instance.OpenedCameras;

                foreach (ComboBox cb in _bindCombos)
                {
                    string current = GetBindId(cb);

                    cb.BeginUpdate();
                    cb.Items.Clear();
                    cb.Items.Add(new BindItem("", "(自动)"));

                    foreach (ICamera cam in opened)
                    {
                        if (cam == null || cam.Info == null) continue;
                        cb.Items.Add(new BindItem(cam.Info.Id, cam.Info.Name));
                    }

                    SetBindId(cb, current);
                    cb.EndUpdate();
                }
            }
            finally
            {
                _rebinding = false;
            }
        }

        /// <summary>把当前各视图的相机绑定写回配置（UiSettings.ViewBindings），保留超出现有视图数量的旧绑定。</summary>
        private void SyncViewBindingsToConfig()
        {
            if (_bindCombos.Count == 0) return;

            List<string> vb = ConfigStore.Current.Ui.ViewBindings;
            if (vb == null) { vb = new List<string>(); ConfigStore.Current.Ui.ViewBindings = vb; }

            for (int i = 0; i < _bindCombos.Count; i++)
            {
                string id = GetBindId(_bindCombos[i]);
                if (i < vb.Count) vb[i] = id; else vb.Add(id);
            }
        }

        /// <summary>用户在视图绑定下拉框中切换相机时，立即持久化绑定关系。</summary>
        private void OnBindComboChanged(object sender, EventArgs e)
        {
            if (_rebinding) return;
            SyncViewBindingsToConfig();
            ConfigStore.Save();
        }

        #endregion

        #region 相机

        private void RefreshCameraList()
        {
            CameraInfo previous = tsComboCameras.SelectedItem as CameraInfo;
            string previousId = previous != null ? previous.Id : null;

            tsComboCameras.BeginUpdate();
            tsComboCameras.Items.Clear();

            List<CameraInfo> infos = CameraManager.Instance.Enumerate();
            foreach (CameraInfo info in infos)
                tsComboCameras.Items.Add(info);

            int select = 0;
            if (previousId != null)
            {
                for (int i = 0; i < infos.Count; i++)
                {
                    if (infos[i].Id == previousId) { select = i; break; }
                }
            }

            if (tsComboCameras.Items.Count > 0)
                tsComboCameras.SelectedIndex = select;

            tsComboCameras.EndUpdate();

            RefreshBindCombos();

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
            RunInspection(frameResult.Value, 0, info != null ? info.Name : "相机0");

            RefreshBindCombos();
        }

        private void OnSyncGrab(object sender, EventArgs e)
        {
            string message;
            if (!SyncGrabCore(out message))
            {
                Logger.Warn(message);
                MessageBox.Show(this, message, "无法采集", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>
        /// 同步采集：先确定每个视图对应的相机，再一次性把帧抓到手，最后逐视图显示与检测。
        /// 失败时通过 message 返回原因，不弹窗——循环运行时由调用方决定如何处理。
        /// </summary>
        private bool SyncGrabCore(out string message)
        {
            message = null;

            var usable = new List<ICamera>();
            foreach (ICamera cam in CameraManager.Instance.OpenedCameras)
            {
                if (cam != null && cam.IsOpen) usable.Add(cam);
            }

            if (usable.Count == 0)
            {
                message = "没有已打开的相机，请先在「相机管理」中打开相机。";
                return false;
            }

            // 1) 为每个视图确定目标相机：先满足显式绑定，再把剩余相机按顺序分给「自动」视图
            var targets = new ICamera[_views.Count];
            var usedCameras = new List<ICamera>();

            for (int i = 0; i < _views.Count; i++)
            {
                string bindId = GetBindId(_bindCombos[i]);
                if (string.IsNullOrEmpty(bindId)) continue;

                ICamera bound = usable.Find(c => c.Info != null && c.Info.Id == bindId);
                if (bound == null)
                {
                    Logger.Warn("视图" + (i + 1) + " 绑定的相机当前未打开，已跳过");
                    continue;
                }

                targets[i] = bound;
                if (!usedCameras.Contains(bound)) usedCameras.Add(bound);
            }

            int cursor = 0;
            for (int i = 0; i < _views.Count; i++)
            {
                if (targets[i] != null) continue;
                if (!string.IsNullOrEmpty(GetBindId(_bindCombos[i]))) continue;

                while (cursor < usable.Count && usedCameras.Contains(usable[cursor])) cursor++;
                if (cursor >= usable.Count) break;

                targets[i] = usable[cursor];
                usedCameras.Add(usable[cursor]);
                cursor++;
            }

            // 2) 先把用到的相机的帧都抓到手（同一相机被多个视图绑定时只抓一次）
            var frames = new Dictionary<ICamera, Bitmap>();
            foreach (ICamera cam in usedCameras)
            {
                var fr = cam.GrabOne();
                if (fr.Success && fr.Value != null) frames[cam] = fr.Value;
                else Logger.Warn("相机取帧失败：" + fr.Message);
            }

            // 3) 逐视图显示；每台相机只做一次检测与统计
            var detected = new List<ICamera>();
            int shown = 0;

            for (int i = 0; i < _views.Count; i++)
            {
                ICamera cam = targets[i];
                if (cam == null) continue;

                Bitmap bmp;
                if (!frames.TryGetValue(cam, out bmp) || bmp == null) continue;

                ShowFrame(i, cam.Info != null ? cam.Info.Name : "相机", bmp);
                shown++;

                if (!detected.Contains(cam))
                {
                    detected.Add(cam);
                    RunInspection(bmp, i, cam.Info != null ? cam.Info.Name : ("视图" + (i + 1)));
                }
            }

            Logger.Info("同步采集完成：显示 " + shown + " 路 / 检测 " + detected.Count + " 路");
            return true;
        }

        private void ShowFrame(int viewIndex, string cameraName, Bitmap frame)
        {
            if (viewIndex < 0 || viewIndex >= _views.Count) return;

            if (viewIndex < _captions.Count && _captions[viewIndex] != null)
                _captions[viewIndex].Text = "  " + cameraName;

            _views[viewIndex].SetImage(frame, true);
        }

        private void RunInspection(Bitmap frame, int viewIndex, string tag)
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

                // 按系统设置自动保存图像
                SystemSettings s = ConfigStore.Current.System;
                if (s.AutoSaveEnabled && (!s.SaveNgOnly || !result.Value.Passed))
                {
                    string path = ImageStore.Save(frame, tag, result.Value.Passed);
                    if (path != null) Logger.Info("图像已保存：" + path);
                }

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

        /// <summary>
        /// 循环运行的前置条件：至少一个相机已打开且处于连接状态；
        /// 若设置中开启了通讯校验，还要求至少有一条通讯链路可用。
        /// </summary>
        private bool CanRunLoop(out string reason)
        {
            reason = null;

            int opened = 0;
            foreach (ICamera cam in CameraManager.Instance.OpenedCameras)
            {
                if (cam != null && cam.IsOpen) opened++;
            }

            if (opened == 0)
            {
                reason = "没有已打开的相机，请先在「相机管理」中打开相机，然后再启动循环运行。";
                return false;
            }

            if (ConfigStore.Current.System.RequireCommBeforeRun && !CommState.HasActiveChannel)
            {
                reason = "通讯未连接（TCP 客户端 / TCP 服务端 / Modbus 均未就绪）。\r\n\r\n"
                       + "请先在「通讯」中建立连接，或在「系统设置」中关闭「循环运行前要求通讯已连接」。";
                return false;
            }

            return true;
        }

        private void OnStartLoop(object sender, EventArgs e)
        {
            string reason;
            if (!CanRunLoop(out reason))
            {
                Logger.Warn("无法启动循环运行：" + reason);
                MessageBox.Show(this, reason, "无法启动循环运行",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                UpdateStatus();
                return;
            }

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
                string reason;

                // 运行期间若前置条件不再满足（例如相机掉线），自动停止并只记一条日志，不弹窗
                if (!CanRunLoop(out reason))
                {
                    loopTimer.Stop();
                    _running = false;
                    _paused = false;
                    Logger.Warn("循环运行已自动停止：" + reason);
                    UpdateStatus();
                    return;
                }

                SyncGrabCore(out reason);
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

        private void OnOpenSettings(object sender, EventArgs e)
        {
            using (var dlg = new SettingsForm())
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    Logger.Info("系统设置已更新");
                    UpdateStatus();
                }
            }
        }

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

            statusLabel.Text = string.Format("状态：{0}    视图：{1}    统计：{2} 件（OK {3} / NG {4}）    通讯：{5}",
                runState,
                _views.Count,
                _statTotal, _statOk, _statNg,
                CommState.HasActiveChannel ? "正常" : "未连接");
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
            // 退出前确保视图绑定与布局被持久化
            SyncViewBindingsToConfig();
            ConfigStore.Save();
            Logger.Logged -= OnLogged;
            CameraManager.Instance.CloseAll();
        }
    }
}
