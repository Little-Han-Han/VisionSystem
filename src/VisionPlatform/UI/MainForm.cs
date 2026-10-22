using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using VisionPlatform.Camera;
using VisionPlatform.Communication;
using VisionPlatform.Core;
using VisionPlatform.Flow;
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

        /// <summary>每个视图右下角的 OK/NG 小徽标（与 <see cref="_views"/> 同下标，未判定时隐藏）。</summary>
        private readonly List<Label> _badges = new List<Label>();

        private int _rows;
        private int _cols;
        private bool _initializing;
        private bool _rebinding;
        private bool _closing;

        /// <summary>关窗时「并行流程已收尾」的一次性标志（用于 join 超时后重试一次）。</summary>
        private bool _flowStopRequested;

        private bool _running;
        private bool _paused;
        private int _statTotal;
        private int _statOk;
        private int _statNg;
        private double _lastElapsedMs;

        /// <summary>最近一次结论（null = 尚未判定），结论保持到下次判定（FR-UI-03 验收④）。</summary>
        private bool? _verdict;

        /// <summary>最近一次结论摘要文本（用于状态栏与运行状态的「最近结论」）。</summary>
        private string _lastSummary = "";

        /// <summary>连续 NG 计数（≥3 时右栏显示橙色警示条，FR-UI-05）。</summary>
        private int _consecutiveNg;

        /// <summary>
        /// 按流程归一化的 OK/NG 计数（流程维度隔离，满足「OK/NG 按流程区分」诉求）。
        /// key = 流程 Id（缺失时回退流程名）；value = 该流程累计 OK/NG。
        /// </summary>
        private readonly object _flowStatSync = new object();
        private Dictionary<string, FlowOkNg> _flowStats = new Dictionary<string, FlowOkNg>(StringComparer.Ordinal);

        /// <summary>当前展示用的流程（最近一次产生输出的流程），用于把 OK/NG 数字归属到具体流程。</summary>
        private string _activeFlowId = "";
        private string _activeFlowName = "";

        /// <summary>单流程 OK/NG 累计（仅供 <see cref="_flowStats"/> 使用）。</summary>
        private sealed class FlowOkNg
        {
            public int Ok;
            public int Ng;
            public int Total { get { return Ok + Ng; } }
        }

        /// <summary>日志区是否折叠（持久化到 <c>UiSettings.LogCollapsed</c>）。</summary>
        private bool _logCollapsed;

        /// <summary>右栏是否折叠（FR-UI-11）。</summary>
        private bool _rightCollapsed;

        /// <summary>连续 NG 达到该数量时显示警示条。</summary>
        private const int ConsecutiveNgAlarm = 3;

        /// <summary>日志区折叠后保留的高度（仅折叠按钮条）。</summary>
        private const int LogFoldedHeight = 36;

        private InspectionEngine _engine;

        private CameraForm _cameraForm;
        private AlgoForm _algoForm;
        private FlowForm _flowForm;
        private CommForm _commForm;
        private RecipeForm _recipeForm;

        // ---- 连续实时预览（主界面 live preview）----
        // 复用 CameraForm / LocalCamera 的成熟链路：StartGrab → FrameReceived → 覆盖式合帧 →
        // 单次 BeginInvoke 上 UI 线程 → 主视图 ImageViewer。与循环运行(loopTimer)通过
        // 「live 开启时跳过 loopTimer 取帧」协调，避免同一相机被双重取帧。
        private bool _liveRunning;               // 连续实时预览是否进行中
        private ICamera _liveCamera;             // 当前实时预览绑定的相机（退订 + StopGrab 用）
        private Bitmap _pendingLiveFrame;        // 覆盖式：后台线程只保留「最新一帧」
        private int _pendingLivePosted;          // 0=无待显示帧；1=已投递一帧待处理
        private bool _liveViewerFitted;          // 首个实时帧做一次 FitToWindow

        // ---- 流程结果合并显示（诉求 2 回归修复）----
        // 多流程并行后，每条流程每帧都经 FlowOutputChannel 回传；若逐结果一次 BeginInvoke 做
        // 整幅帧克隆 + 重绘，单 UI 线程会被 N×30fps 的发布淹没，消息队列积压 → 界面卡死/无显示。
        // 照 OnLiveFrame 的「覆盖式合帧」范式：每个视图只保留最新一帧 + 单个待投递标志，丢弃中间帧。
        private readonly Dictionary<int, PendingFlowOutput> _pendingOutputs =
            new Dictionary<int, PendingFlowOutput>();
        private readonly object _pendingOutputsSync = new object();

        /// <summary>某视图当前待显示的最新结果（覆盖式合帧用）。</summary>
        private sealed class PendingFlowOutput
        {
            public Bitmap Frame;                 // 最新待显示帧（所有权在此，被替换时释放）
            public List<IGeometryShape> Shapes;  // 最新叠加图形（引用，不克隆）
            public bool Passed;
            public string Summary = "";
            public string CameraName = "";
            public int Posted;                   // 0=无待处理；1=已投递一次 BeginInvoke
        }

        // ---- 日志批量拉取（诉求 2 回归修复）----
        // Logger 在后台线程同步触发 Logged，原 OnLogged 每行一次 BeginInvoke；并行下 RESULT 行可达
        // 90 行/秒，同样淹没 UI 线程。改为缓冲 + UI 线程定时器批量拉取（与 LogPanel 设计意图一致）。
        private List<string> _logBuffer = new List<string>();
        private readonly object _logSync = new object();
        private System.Windows.Forms.Timer _logTimer;

        // ---- 主界面流程下拉运行（选流程 → 选本地图片 → 运行，结果经既有通道显示到主视图）----
        // 与流程编排页(FlowForm) / 并行槽(FlowRunManager) 各自持有独立的 FlowRunner，互不干扰。
        private FlowRunner _flowRunner;          // 流程执行器；算法库不可用时为 null（下拉框降级禁用）
        private FlowRunController _flowCtrl;     // 本窗体自有的运行编排器
        private bool _loadingFlowCombo;          // 回填下拉框期间抑制 SelectedIndexChanged 触发运行
        private string _lastImageDir;            // 上次选图的目录（FileDialog InitialDirectory 记忆）

        /// <summary>当前布局的视图总数（供流程编排页在运行前校验视图绑定是否越界）。</summary>
        public static int ActiveViewCount { get; private set; }

        public MainForm()
        {
            _initializing = true;

            InitializeComponent();

            // 主题在 InitializeComponent 之后统一套用：Designer 只写结构，颜色 / 字体单点来自 UiTheme
            ApplyTheme();

            // 自绘深色标题栏（最小化 / 最大化 / 关闭）：替掉系统原生浅色非客户区，
            // 拖动 / 双击最大化 / 边缘拉伸由 WM_NCHITTEST 交给系统原生行为。
            UiTitleBar.Attach(this);

            _logCollapsed = ConfigStore.Current.Ui.LogCollapsed;
            ApplyLogCollapsed();

            InitLayoutCombo();

            // 按保存的布局索引重建视图（InitLayoutCombo 设置下拉框会触发 OnLayoutChanged，
            // 这里再显式构建一次以保证使用保存的布局）
            int startIdx = ConfigStore.Current.Ui.LayoutIndex;
            if (startIdx < 0 || startIdx >= LayoutPresets.GetLength(0)) startIdx = 0;
            BuildViews(LayoutPresets[startIdx, 0], LayoutPresets[startIdx, 1]);

            RefreshCameraList();
            Logger.Logged += OnLogged;
            RecipeStore.CurrentChanged += OnRecipeChanged;
            CameraManager.Instance.CurrentChanged += OnCameraManagerChanged;

            // 日志批量拉取定时器（UI 线程，200ms 一次）：OnLogged 仅入缓冲，由它批量刷到 listLog，
            // 避免并行运行下海量日志行逐条 BeginInvoke 淹没 UI 线程（见 OnLogged / OnLogTimerTick）。
            _logTimer = new System.Windows.Forms.Timer { Interval = 200 };
            _logTimer.Tick += OnLogTimerTick;
            _logTimer.Start();
            ConfigStore.Changed += OnConfigChanged;
            FlowOutputChannel.OutputReady += OnFlowOutput;

            // 主界面流程下拉：初始化自有执行器/控制器并回填流程列表（算法库不可用时降级禁用）
            InitFlowRunner();
            RefreshFlowCombo();

            _initializing = false;

            UpdateStatus();
            UpdateStats();
            UpdateRunButtons();
            SetVerdict(null);
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
            foreach (Label b in _badges)
            {
                if (b != null) b.Dispose();
            }
            _views.Clear();
            _captions.Clear();
            _bindCombos.Clear();
            _badges.Clear();

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
                cell.BackColor = UiTheme.ColorViewBg;

                // 顶部条：视图名（相机名 · 流程名） + 相机绑定下拉框
                var header = new Panel();
                header.Dock = DockStyle.Top;
                header.Height = UiTheme.ViewHeaderHeight;
                header.BackColor = UiTheme.ColorHeaderBg;

                var caption = new Label();
                caption.Dock = DockStyle.Left;
                caption.Width = 190;
                caption.Font = UiTheme.FontBody;
                caption.ForeColor = UiTheme.ColorTextSecondary;
                caption.TextAlign = ContentAlignment.MiddleLeft;
                caption.Text = "  视图 " + (i + 1);

                var bind = new ComboBox();
                bind.Dock = DockStyle.Fill;
                bind.DropDownStyle = ComboBoxStyle.DropDownList;
                bind.BackColor = UiTheme.ColorCardBg;
                bind.ForeColor = UiTheme.ColorTextPrimary;
                bind.FlatStyle = FlatStyle.Flat;
                bind.SelectedIndexChanged += OnBindComboChanged;

                // Fill 控件先加，Left 控件后加，保证左侧标签先占位
                header.Controls.Add(bind);
                header.Controls.Add(caption);

                var viewer = new ImageViewer();
                viewer.Dock = DockStyle.Fill;
                viewer.ShowHud = true;
                viewer.Options.ShowCrosshair = true;

                // 视图右下角 OK/NG 小徽标：未判定时隐藏（FR-UI-08）
                var badge = new Label();
                badge.Dock = DockStyle.Bottom;
                badge.Height = 30;
                badge.Visible = false;
                badge.TextAlign = ContentAlignment.MiddleCenter;
                UiTheme.StyleViewBadge(badge, UiVerdict.None);

                // 停靠按集合逆序生效：Fill 先加 → 标题条（Top）→ 徽标（Bottom）
                cell.Controls.Add(viewer);
                cell.Controls.Add(badge);
                cell.Controls.Add(header);

                _views.Add(viewer);
                _captions.Add(caption);
                _bindCombos.Add(bind);
                _badges.Add(badge);

                tableViews.Controls.Add(cell, i % cols, i / cols);
            }

            _rows = rows;
            _cols = cols;
            ActiveViewCount = rows * cols;

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
            if (_closing || IsDisposed || !IsHandleCreated) return;

            try
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
            catch (Exception)
            {

                throw;
            }
            
        }

        private void OnRefreshCameras(object sender, EventArgs e)
        {
            RefreshCameraList();
        }

        private void OnCameraSelectionChanged(object sender, EventArgs e)
        {
            // 切相机收尾：若正在实时预览，而所选相机已不是预览那台，则停掉旧预览
            // （退订 FrameReceived + StopGrab），避免残留处理器 / 句柄错配。
            if (_liveRunning)
            {
                CameraInfo sel = SelectedCameraInfo;
                if (_liveCamera == null || _liveCamera.Info == null || sel == null ||
                    !string.Equals(_liveCamera.Info.Id, sel.Id, StringComparison.Ordinal))
                {
                    StopLive();
                }
            }

            UpdateStatus();
        }

        // 以下三个事件处理器在窗体关闭时通过 _closing 标志短路，
        // 避免 CloseAll() 触发的 CurrentChanged / ConfigStore.Changed 在销毁阶段再次刷新界面
        private void OnRecipeChanged(object sender, EventArgs e)
        {
            if (_closing) return;
            UpdateStatus();
        }

        private void OnCameraManagerChanged(object sender, EventArgs e)
        {
            if (_closing) return;
            RefreshCameraList();
        }

        private void OnConfigChanged(object sender, EventArgs e)
        {
            if (_closing) return;
            UpdateStatus();
            // 流程编排页是无模态窗体：它增删/保存流程后经 ConfigStore.Changed 到这里，
            // 主界面流程下拉框同步刷新（按 Id 保留原选中项，_loadingFlowCombo 抑制误触发运行）。
            RefreshFlowCombo();
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

            // 【并行铁律】取帧统一走 CameraGrabGate（FR-MC-04）：与流程线程共用同一把相机锁，
            // 否则手工单帧采集会和并行流程并发调相机原生句柄。
            var frameResult = CameraGrabGate.GrabOne(r.Value);
            if (!frameResult.Success) { MessageBox.Show(this, frameResult.Message); return; }

            ShowFrame(0, r.Value.Info.Name, frameResult.Value);
            RunInspection(frameResult.Value, 0, info != null ? info.Name : "相机0");

            RefreshBindCombos();
        }

        #region 连续实时预览

        /// <summary>连续实时预览开关：运行中则停止，否则启动。</summary>
        private void OnToggleLive(object sender, EventArgs e)
        {
            if (_liveRunning) StopLive();
            else StartLive();
        }

        /// <summary>
        /// 启动连续实时预览：打开并绑定所选相机 → 订阅 FrameReceived → StartGrab。
        /// 先订阅再 StartGrab，避免丢起始帧；StartGrab 失败则退订并明确告知（复用 C2 口径）。
        /// 仅 UI 线程调用，<c>_liveRunning</c> 前置判断保证幂等。
        /// </summary>
        private void StartLive()
        {
            if (_liveRunning) return;   // 幂等：已在预览则不重复启动

            CameraInfo info = SelectedCameraInfo;
            if (info == null) { MessageBox.Show(this, "请先选择相机。"); return; }

            var r = CameraManager.Instance.Open(info.Id);
            if (!r.Success) { MessageBox.Show(this, r.Message); return; }

            ICamera cam = r.Value;
            cam.FrameReceived += OnLiveFrame;   // 先订阅，避免丢起始帧

            var sr = cam.StartGrab();
            if (!sr.Success)
            {
                cam.FrameReceived -= OnLiveFrame;
                MessageBox.Show(this, "启动连续采集失败：" + sr.Message, "连续预览",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _liveCamera = cam;
            _pendingLiveFrame = null;
            _pendingLivePosted = 0;
            _liveViewerFitted = false;
            _liveRunning = true;

            // 主视图先清掉历史叠加图形，给一个干净的实时画面；标题预置为相机名
            if (_views.Count > 0 && _views[0] != null) _views[0].ClearShapes();
            if (_captions.Count > 0 && _captions[0] != null)
                _captions[0].Text = "  " + (cam.Info != null ? cam.Info.Name : info.Name) + " · " + ActiveFlowName;

            RefreshBindCombos();
            UpdateLiveButton();
            UpdateStatus();
            Logger.Info("开始连续实时预览：" + cam.Info.Id);
        }

        /// <summary>
        /// 停止连续实时预览：先退订 FrameReceived 再 StopGrab（与关窗 / 切相机同一收尾顺序），
        /// 并丢弃可能残留的待显示帧。StopGrab 内部 Join 有 1s 上限，不会卡死 UI。
        /// </summary>
        private void StopLive()
        {
            ICamera cam = _liveCamera;
            _liveCamera = null;
            _liveRunning = false;

            if (cam != null)
            {
                try { cam.FrameReceived -= OnLiveFrame; }
                catch (Exception) { }
                try { cam.StopGrab(); }
                catch (Exception) { }
            }

            Bitmap pending = System.Threading.Interlocked.Exchange(ref _pendingLiveFrame, null);
            if (pending != null) pending.Dispose();

            UpdateLiveButton();
            UpdateStatus();
            Logger.Info("停止连续实时预览");
        }

        /// <summary>
        /// 实时帧回调（镜像 <see cref="CameraForm.OnFrame"/>）：覆盖式合帧 + 单次 BeginInvoke 上
        /// UI 线程，渲染进主视图 <see cref="_views"/>[0]；首个实时帧做一次 FitToWindow。
        /// 每帧只保留最新一帧，绝不「来一帧投递一次 BeginInvoke」，避免渲染跟不上时消息队列积压。
        /// </summary>
        private void OnLiveFrame(object sender, Bitmap frame)
        {
            if (IsDisposed || !IsHandleCreated)
            {
                if (frame != null) frame.Dispose();
                return;
            }

            // 只保留最新一帧：新帧替换旧帧，被替换掉的待显示帧立即释放
            Bitmap old = System.Threading.Interlocked.Exchange(ref _pendingLiveFrame, frame);
            if (old != null) old.Dispose();

            // 队列里已有一帧待处理：丢弃本帧的投递（它已作为「最新帧」留在 _pendingLiveFrame）
            if (System.Threading.Interlocked.Exchange(ref _pendingLivePosted, 1) == 1) return;

            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    Bitmap bmp = System.Threading.Interlocked.Exchange(ref _pendingLiveFrame, null);
                    System.Threading.Interlocked.Exchange(ref _pendingLivePosted, 0);

                    if (bmp == null) return;
                    if (IsDisposed || !IsHandleCreated) { bmp.Dispose(); return; }
                    if (_views.Count == 0 || _views[0] == null) { bmp.Dispose(); return; }

                    try
                    {
                        _views[0].SetImage(bmp, false);
                        if (!_liveViewerFitted) { _views[0].FitToWindow(); _liveViewerFitted = true; }
                    }
                    catch (Exception ex)
                    {
                        bmp.Dispose();
                        Logger.Warn("显示实时帧失败：" + ex.Message);
                    }
                });
            }
            catch (Exception)
            {
                // 窗口已销毁：清空投递状态并释放待显示帧
                System.Threading.Interlocked.Exchange(ref _pendingLivePosted, 0);
                Bitmap bmp = System.Threading.Interlocked.Exchange(ref _pendingLiveFrame, null);
                if (bmp != null) bmp.Dispose();
            }
        }

        /// <summary>同步连续预览开关按钮文字（运行中显示「停止预览」，否则「连续预览」）。</summary>
        private void UpdateLiveButton()
        {
            if (tsBtnLive == null) return;
            tsBtnLive.Text = _liveRunning ? "停止预览" : "连续预览";
            tsBtnLive.Enabled = true;
        }

        #endregion

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
                // 同上：同步采集也必须过门，避免与并行流程抢同一相机句柄
                var fr = CameraGrabGate.GrabOne(cam);
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

        /// <summary>
        /// 当前生效的流程名（用于视图标题条「相机名 · 流程名」，FR-UI-08）。
        /// 取不到时返回 "--"，不抛异常、不留空白。
        /// </summary>
        private static string ActiveFlowName
        {
            get
            {
                try
                {
                    FlowSettings fs = ConfigStore.Current.Flows;
                    if (fs == null || fs.Flows == null || fs.Flows.Count == 0) return "--";

                    FlowDefinition active = fs.Flows.Find(x => x != null && x.Id == fs.ActiveFlowId);
                    if (active != null && !string.IsNullOrEmpty(active.Name)) return active.Name;
                    if (fs.Flows[0] != null && !string.IsNullOrEmpty(fs.Flows[0].Name)) return fs.Flows[0].Name;
                }
                catch (Exception)
                {
                    // 配置损坏时不应影响界面刷新
                }

                return "--";
            }
        }

        /// <summary>设置某个视图右下角的 OK/NG 小徽标（多视图各自独立，FR-UI-08 验收①）。</summary>
        private void SetViewVerdict(int viewIndex, bool? passed)
        {
            if (viewIndex < 0 || viewIndex >= _badges.Count) return;

            Label badge = _badges[viewIndex];
            if (badge == null) return;

            if (!passed.HasValue) UiTheme.StyleViewBadge(badge, UiVerdict.None);
            else UiTheme.StyleViewBadge(badge, passed.Value ? UiVerdict.Ok : UiVerdict.Ng);
        }

        private void ShowFrame(int viewIndex, string cameraName, Bitmap frame)
        {
            if (viewIndex < 0 || viewIndex >= _views.Count) return;

            if (viewIndex < _captions.Count && _captions[viewIndex] != null)
                _captions[viewIndex].Text = "  " + cameraName + " · " + ActiveFlowName;

            _views[viewIndex].SetImage(frame, true);
        }

        /// <summary>对当前帧跑配方检测并驱动徽标 / 统计 / 状态栏。</summary>
        /// <remarks>
        /// 帧所有权归视图：调用前 ShowFrame 已把 frame 交给 ImageViewer（OwnsImage=true），
        /// 此处绝不 Dispose——否则已显示位图被释放，下一次绘制在 DrawImage 抛『参数无效』。
        /// 帧由视图在换图 / 销毁时释放。
        /// </remarks>
        private void RunInspection(Bitmap frame, int viewIndex, string tag)
        {
            VisionAlgo algo = AppContext.Algo;
            RecipeModel recipe = RecipeStore.Current;

            if (algo == null || recipe == null)
            {
                // 没有配方或算法库时只显示图像，不做判定（帧已归视图，不在此释放）
                return;
            }

            if (_engine == null) _engine = new InspectionEngine(algo);

            var result = _engine.Run(frame, recipe.Inspection);
            if (!result.Success) return;

            ShowResultOnView(viewIndex, result.Value);

            _statTotal++;
            if (result.Value.Passed) _statOk++; else _statNg++;
            _lastElapsedMs = result.Value.ElapsedMs;
            _lastSummary = result.Value.Summary ?? "";

            // 徽标 / 视图角标 / 统计 / 状态栏共用一次判定结果
            SetVerdict(result.Value.Passed);
            SetViewVerdict(viewIndex, result.Value.Passed);
            UpdateStats();
            UpdateStatus();

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

        private void ShowResultOnView(int viewIndex, InspectionResult result)
        {
            if (viewIndex < 0 || viewIndex >= _views.Count) return;

            ImageViewer view = _views[viewIndex];
            view.ClearShapes();

            foreach (BlobResult b in result.Blobs)
            {
                var c = new CircleShape(new PointF((float)b.Cx, (float)b.Cy),
                                        Math.Max(3f, (float)Math.Sqrt(b.Area / Math.PI)));
                c.StrokeColor = result.Passed ? UiTheme.ColorOk : UiTheme.ColorNg;
                c.LineWidth = 2f;
                view.Shapes.Add(c);
            }

            if (result.Circle != null)
            {
                var fit = new CircleShape(new PointF((float)result.Circle.Cx, (float)result.Circle.Cy),
                                          (float)Math.Max(1.0, result.Circle.Radius));
                fit.StrokeColor = UiTheme.ColorAccent;
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
        /// 循环运行的前置条件（诉求 2 改写）：现在循环运行只启动「当前产品过滤 &amp; Enabled」的
        /// 全部流程（含本地图片源，无需相机），因此相机不再是硬性要求——流程自身会按需校验相机；
        /// 若设置中开启了通讯校验，仍要求至少有一条通讯链路可用。
        /// </summary>
        private bool CanRunLoop(out string reason)
        {
            reason = null;

            // 诉求 2：存在可启动（已启用且属于当前产品）的流程才允许启动；本地图片源流程无需相机。
            if (!FlowRunManager.HasEnabledFlowsOfCurrentProduct())
            {
                reason = "当前产品没有可启动（已启用）的流程，请先在「流程编排」中启用流程，再启动循环运行。";
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
                // 续跑（之前被暂停）：恢复全部已暂停的流程
                _paused = false;
                try { FlowRunManager.ResumeAll(); }
                catch (Exception ex) { Logger.Warn("恢复流程失败：" + ex.Message); }
                Logger.Info("继续循环运行（全部流程）");
            }
            else
            {
                // 诉求 2：启动「当前产品 & Enabled」的全部流程，各自独立线程并行运行（≤30fps），
                // 结果经 FlowOutputChannel 显示到主视图；本地图片源流程无需相机即可运行。
                // 旧配方检测（SyncGrabCore / loopTimer）已不再使用。
                _running = true;
                _paused = false;
                var r = FlowRunManager.StartAll();
                if (r == null || !r.Success)
                {
                    _running = false;
                    string msg = r != null ? r.Message : "启动流程失败";
                    Logger.Warn("循环运行启动失败：" + msg);
                    MessageBox.Show(this, msg, "循环运行启动失败",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                else
                {
                    Logger.Info("开始循环运行（全部流程）：" + (r.Message ?? ""));
                }
            }
            UpdateStatus();
        }

        private void OnPause(object sender, EventArgs e)
        {
            if (!_running) return;

            _paused = !_paused;
            // 诉求 2：暂停/继续映射到全部流程的 PauseAll/ResumeAll（旧 loopTimer 已弃用）
            try
            {
                if (_paused) FlowRunManager.PauseAll();
                else FlowRunManager.ResumeAll();
            }
            catch (Exception ex) { Logger.Warn("暂停/继续流程失败：" + ex.Message); }

            Logger.Info(_paused ? "已暂停" : "已继续");
            UpdateStatus();
        }

        private void OnStop(object sender, EventArgs e)
        {
            // 诉求 2：停止全部并行流程（并行 Join + 释放各自执行器）；旧 loopTimer 一并停掉
            try { FlowRunManager.StopAll(); }
            catch (Exception ex) { Logger.Warn("停止流程失败：" + ex.Message); }
            loopTimer.Stop();
            _running = false;
            _paused = false;
            Logger.Info("停止循环运行");
            UpdateStatus();
        }

        private void OnLoopTick(object sender, EventArgs e)
        {
            if (!_running || _paused) return;

            // 连续实时预览进行中：GrabLoop 已在持续喂帧，loopTimer 的按拍取帧属冗余，
            // 直接跳过以免同一相机被双重取帧（与 CameraGrabGate 串行化口径一致，避免抢占原生句柄）。
            if (_liveRunning) return;

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
            lock (_flowStatSync)
            {
                _flowStats.Clear();
                _activeFlowId = "";
                _activeFlowName = "";
            }
            _statTotal = 0;
            _statOk = 0;
            _statNg = 0;
            _lastElapsedMs = 0;
            _consecutiveNg = 0;
            lblConsecutiveNg.Visible = false;
            UpdateStats();
            UpdateStatus();
        }

        #endregion

        #region 子窗体

        private void OnOpenCameraForm(object sender, EventArgs e) { ShowChild(ref _cameraForm); }
        private void OnOpenAlgoForm(object sender, EventArgs e) { ShowChild(ref _algoForm); }
        private void OnOpenFlowForm(object sender, EventArgs e)
        {
            ShowChild(ref _flowForm);
            RefreshFlowCombo();   // 流程编排页可能新建/删除了流程，回来即刷新（无模态，双保险）
        }
        private void OnOpenCommForm(object sender, EventArgs e) { ShowChild(ref _commForm); }
        private void OnOpenRecipeForm(object sender, EventArgs e) { ShowChild(ref _recipeForm); }

        private void OnOpenSettings(object sender, EventArgs e)
        {
            using (var dlg = new SettingsForm())
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    Logger.Info("系统设置已更新");
                    UpdateStats();     // 良率告警下限可能已改变
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

        /// <summary>
        /// 记录一次流程判定结果到按流程的计数表。每收到一条流程输出事件调用一次
        /// （独立于帧显示的合帧节流，保证每个运行周期都精确计入 OK/NG）。
        /// 同时把该流程记为「当前展示流程」，使主界面 OK/NG 数字归属到具体流程。
        /// </summary>
        private void RecordFlowVerdict(string flowId, string flowName, bool passed)
        {
            string key = string.IsNullOrEmpty(flowId)
                ? (string.IsNullOrEmpty(flowName) ? "(未命名流程)" : flowName)
                : flowId;
            string name = string.IsNullOrEmpty(flowName) ? key : flowName;

            lock (_flowStatSync)
            {
                FlowOkNg t;
                if (!_flowStats.TryGetValue(key, out t))
                {
                    t = new FlowOkNg();
                    _flowStats[key] = t;
                }
                if (passed) t.Ok++; else t.Ng++;

                _activeFlowId = key;
                _activeFlowName = name;
            }
        }

        /// <summary>把流程名截断到指定长度（超出加省略号），防止标题栏被撑破。</summary>
        private static string TrimFlowName(string name, int max)
        {
            if (string.IsNullOrEmpty(name)) return "";
            if (name.Length <= max) return name;
            return name.Substring(0, Math.Max(1, max - 1)) + "…";
        }

        /// <summary>
        /// 刷新统计卡片（FR-UI-04 / FR-UI-05）。OK/NG 数字按「当前流程」独立统计，
        /// 标题写清流程名（满足「OK/NG 按流程区分」诉求）；无选中流程时回落全部流程合计。
        /// 良品率单独一行 28 pt 大数字，低于告警下限转橙、低于下限一半转红；无数据时显示 "--"。
        /// </summary>
        private void UpdateStats()
        {
            int ok = 0, ng = 0, total = 0;
            string scope = "";

            lock (_flowStatSync)
            {
                if (!string.IsNullOrEmpty(_activeFlowId) && _flowStats.TryGetValue(_activeFlowId, out FlowOkNg t))
                {
                    ok = t.Ok; ng = t.Ng; total = t.Total;
                    scope = _activeFlowName;
                }
                else if (_flowStats.Count > 0)
                {
                    foreach (FlowOkNg v in _flowStats.Values) { ok += v.Ok; ng += v.Ng; }
                    total = ok + ng;
                    scope = (_flowStats.Count > 1) ? "全部流程" : _activeFlowName;
                }
            }

            _statTotal = total;
            _statOk = ok;
            _statNg = ng;

            UiTheme.StyleStatValue(lblTotalValue, UiStatKind.Normal, false);
            UiTheme.StyleStatValue(lblOkValue, UiStatKind.Ok, false);
            UiTheme.StyleStatValue(lblNgValue, UiStatKind.Ng, false);
            UiTheme.StyleStatValue(lblCycleValue, UiStatKind.Normal, false);

            lblTotalValue.Text = total.ToString();
            lblOkValue.Text = ok.ToString();
            lblNgValue.Text = ng.ToString();

            // 按流程区分：把流程名带进 OK/NG 标题，明确数字归属
            string tag = string.IsNullOrEmpty(scope) ? "" : (" · " + TrimFlowName(scope, 12));
            lblOkCap.Text = "OK" + tag;
            lblNgCap.Text = "NG" + tag;

            double alarm = ConfigStore.Current.System.YieldAlarmPercent;

            if (total > 0)
            {
                double yield = ok * 100.0 / total;
                lblYieldValue.Text = yield.ToString("F1") + " %";
                UiTheme.StyleStatValue(lblYieldValue, UiStatKind.Yield, true);
                lblYieldValue.ForeColor = UiTheme.YieldColor(yield, alarm);
            }
            else
            {
                lblYieldValue.Text = "--";
                UiTheme.StyleStatValue(lblYieldValue, UiStatKind.Yield, true);
            }

            lblCycleValue.Text = _lastElapsedMs > 0 ? _lastElapsedMs.ToString("F0") + " ms" : "--";
        }

        /// <summary>
        /// 刷新状态栏 / 工具栏状态位 / 右栏运行状态区，并同步运行控制按钮的可用态
        /// （单一状态源：工具栏与右栏大按钮共用 <see cref="UpdateRunButtons"/>）。
        /// </summary>
        private void UpdateStatus()
        {
            string user = AppContext.CurrentUser != null ? AppContext.CurrentUser.ToString() : "未登录";
            RecipeModel recipe = RecipeStore.Current;
            CameraInfo cam = SelectedCameraInfo;

            string runState = _running ? (_paused ? "已暂停" : "运行中") : "已停止";

            tsLabelStatus.Text = string.Format("{0}    |    配方：{1}    |    算法库：{2}",
                user,
                recipe != null ? recipe.Name : "未加载",
                AppContext.Algo != null ? "已加载" : "未加载");

            // FR-UI-10：纯文本分段，字段以 " | " 分隔，无数据一律 "--"（不出现空白或 NaN）
            string verdictText = _verdict.HasValue ? (_verdict.Value ? "OK" : "NG") : "--";
            string costText = _lastElapsedMs > 0 ? (_lastElapsedMs.ToString("F0") + " ms") : "--";

            statusLabel.Text = string.Format(
                "相机：{0} | 状态：{1} | 视图：{2} | 最近：{3} | 耗时：{4} | 统计：{5} 件（OK {6} / NG {7}） | 流程：{8} | 通讯：{9}",
                cam != null ? cam.Name : "--",
                runState,
                _views.Count,
                verdictText,
                costText,
                _statTotal, _statOk, _statNg,
                ActiveFlowName,
                CommState.HasActiveChannel ? "正常" : "未连接");

            // 右栏 ④ 运行状态
            lblRunStateValue.Text = runState;
            lblCameraValue.Text = cam != null ? cam.Name : "--";
            lblFlowValue.Text = ActiveFlowName;
            lblLastValue.Text = _verdict.HasValue
                ? (verdictText + (string.IsNullOrEmpty(_lastSummary) ? "" : ("  " + _lastSummary)))
                : "--";

            UpdateRunButtons();
        }

        /// <summary>
        /// 运行控制三态互斥（FR-UI-06）：停止态 / 运行态 / 暂停态。
        /// 工具栏按钮与右栏大按钮共用本方法，保证行为严格同步（同一状态源）。
        /// </summary>
        private void UpdateRunButtons()
        {
            bool stopped = !_running;
            bool running = _running && !_paused;
            bool paused = _running && _paused;

            // 右栏大按钮
            UiTheme.SetButtonEnabled(btnLoop, stopped, UiButtonKind.Start);
            UiTheme.SetButtonEnabled(btnPause, !stopped, UiButtonKind.Pause);
            UiTheme.SetButtonEnabled(btnStop, !stopped, UiButtonKind.Stop);
            UiTheme.SetButtonEnabled(btnGrabOnce, stopped || paused, UiButtonKind.Grab);
            UiTheme.SetButtonEnabled(btnResetStats, stopped, UiButtonKind.Reset);

            // 「暂停」在暂停态显示「继续」：颜色语义不变（橙），仅在文字上区分
            if (btnPause != null && btnPause.Enabled) btnPause.Text = paused ? "继续" : "暂停";

            // 工具栏不再重复放「启动 / 暂停」（用户要求：运行控制只在右栏），
            // 这里只同步工具栏保留的「单帧采集」可用态。
            tsBtnGrabOnce.Enabled = stopped || paused;

            // 连续预览开关：文字随运行态切换（连续预览 / 停止预览）
            UpdateLiveButton();
        }

        /// <summary>
        /// 设置主徽标三态（FR-UI-03）：OK 绿 / NG 红 / "--" 灰，结论保持到下次判定。
        /// 同时维护连续 NG 计数与橙色警示条（FR-UI-05）。
        /// </summary>
        private void SetVerdict(bool? passed)
        {
            _verdict = passed;

            if (!passed.HasValue)
            {
                UiTheme.StyleBadge(lblVerdict, UiVerdict.None);
            }
            else if (passed.Value)
            {
                UiTheme.StyleBadge(lblVerdict, UiVerdict.Ok);
                _consecutiveNg = 0;
            }
            else
            {
                UiTheme.StyleBadge(lblVerdict, UiVerdict.Ng);
                _consecutiveNg++;
            }

            // 连续 NG 警示条：纯文本、橙底、无弹窗不阻塞
            bool alarm = _consecutiveNg >= ConsecutiveNgAlarm;
            lblConsecutiveNg.Visible = alarm;
            if (alarm) lblConsecutiveNg.Text = "连续 NG：" + _consecutiveNg;
        }

        private void OnLogged(object sender, string line)
        {
            if (IsDisposed || !IsHandleCreated) return;
            if (string.IsNullOrEmpty(line)) return;

            // 仅入缓冲，由 UI 线程的 _logTimer 批量拉取（见 OnLogTimerTick）。
            // 不在后台线程逐行 BeginInvoke —— 并行运行下 RESULT 行可达 90 行/秒，逐条投递会淹没 UI 线程。
            lock (_logSync)
            {
                _logBuffer.Add(line);
            }
        }

        /// <summary>
        /// 日志批量拉取（UI 线程，200ms 一次）：把缓冲中的日志行一次性追加到 listLog 并裁剪到上限，
        /// 把并行运行下 90+ 行/秒的日志洪流从「逐行 BeginInvoke」压成「每秒约 5 次批量追加」。
        /// </summary>
        private void OnLogTimerTick(object sender, EventArgs e)
        {
            if (IsDisposed || !IsHandleCreated) return;

            List<string> batch;
            lock (_logSync)
            {
                if (_logBuffer.Count == 0) return;
                batch = _logBuffer;
                _logBuffer = new List<string>();
            }

            try
            {
                foreach (string line in batch)
                {
                    listLog.Items.Add(line);
                    // FR-UI-09：日志上限 500 行，超出移除最旧一行（控制 5 分钟连续运行的内存增长）
                    while (listLog.Items.Count > UiTheme.LogMaxLines) listLog.Items.RemoveAt(0);
                }
                listLog.TopIndex = listLog.Items.Count - 1;
            }
            catch (Exception)
            {
                // 关闭过程中忽略
            }
        }

        #endregion

        #region 主题与外观

        /// <summary>
        /// 导航栏条目统一样式：胶囊底与状态高亮由 <see cref="UiToolStripRenderer"/> 绘制，
        /// 这里只负责字号（加粗大一号）、内边距与文字对比度。
        /// 分组标签（相机 / 布局）用次要色，状态位用主题蓝，按钮用主文本色。
        /// </summary>
        private void StyleToolbar()
        {
            foreach (ToolStripItem item in toolStrip.Items)
            {
                if (item == null) continue;

                item.Font = UiTheme.FontToolbar;

                ToolStripLabel label = item as ToolStripLabel;
                if (label != null)
                {
                    label.ForeColor = ReferenceEquals(label, tsLabelStatus)
                        ? UiTheme.ColorAccent
                        : UiTheme.ColorTextSecondary;
                    continue;
                }

                if (item is ToolStripSeparator) continue;

                item.ForeColor = UiTheme.ColorTextPrimary;
                item.Padding = new Padding(10, 4, 10, 4);

                // 语义色：只给"一看就要点"的动作上色，其余保持中性卡片底
                // （启动 / 暂停已从工具栏移除，运行控制统一在右栏）
                if (ReferenceEquals(item, tsBtnGrabOnce) || ReferenceEquals(item, tsBtnSyncGrab))
                    UiToolStripRenderer.SetChipKind(item, UiButtonKind.Grab);

                ToolStripComboBox combo = item as ToolStripComboBox;
                if (combo != null && combo.ComboBox != null)
                {
                    combo.ComboBox.BackColor = UiTheme.ColorCardBg;
                    combo.ComboBox.ForeColor = UiTheme.ColorTextPrimary;
                    combo.ComboBox.Font = UiTheme.FontToolbar;
                    combo.ComboBox.FlatStyle = FlatStyle.Flat;
                }
            }
        }

        /// <summary>
        /// 运行时统一套用 <see cref="UiTheme"/>（Designer 只写结构）。
        /// 颜色 / 字体 / 关键尺寸一律取自 UiTheme，本方法内不出现颜色与字体字面量。
        /// </summary>
        private void ApplyTheme()
        {
            // ---- 窗体与容器 ----
            BackColor = UiTheme.ColorAppBg;
            ForeColor = UiTheme.ColorTextPrimary;

            toolStrip.BackColor = UiTheme.ColorHeaderBg;
            toolStrip.ForeColor = UiTheme.ColorTextPrimary;
            toolStrip.Font = UiTheme.FontBody;
            // AutoSize 关掉后高度才由 UiTheme 说了算（否则设计器里的 32 px 会一直生效）
            toolStrip.AutoSize = false;
            toolStrip.Height = UiTheme.ToolbarHeight;
            tsLabelStatus.ForeColor = UiTheme.ColorAccent;

            // ---- 导航栏：自绘渲染器把条目画成胶囊按钮，解决"内容不明显" ----
            toolStrip.Renderer = new UiToolStripRenderer();
            StyleToolbar();

            panelCenter.BackColor = UiTheme.ColorAppBg;
            UiTheme.StylePanel(panelRight);
            UiTheme.StylePanel(panelVerdict);
            UiTheme.StylePanel(panelRunCtrl);
            UiTheme.StylePanel(panelRunState);
            UiTheme.StylePanel(panelBottom);

            statusStrip.BackColor = UiTheme.ColorHeaderBg;
            statusStrip.ForeColor = UiTheme.ColorTextSecondary;
            statusStrip.Font = UiTheme.FontMono;
            statusStrip.Height = UiTheme.StatusBarHeight;
            statusLabel.Font = UiTheme.FontMono;

            // ---- ① 徽标 ----
            UiTheme.StyleBadge(lblVerdict, UiVerdict.None);

            // ---- ② 统计卡片 ----
            UiTheme.StyleGroupBox(groupStats);
            groupStats.Font = UiTheme.FontGroupTitle;
            groupStats.ForeColor = UiTheme.ColorAccent;
            groupStats.Height = UiTheme.StatsHeight;
            tableStats.BackColor = UiTheme.ColorPanelBg;

            UiTheme.StyleStatLabel(lblTotalCap);
            UiTheme.StyleStatLabel(lblOkCap);
            UiTheme.StyleStatLabel(lblNgCap);
            UiTheme.StyleStatLabel(lblYieldCap);
            UiTheme.StyleStatLabel(lblCycleCap);

            UiTheme.StyleStatValue(lblTotalValue, UiStatKind.Normal, false);
            UiTheme.StyleStatValue(lblOkValue, UiStatKind.Ok, false);
            UiTheme.StyleStatValue(lblNgValue, UiStatKind.Ng, false);
            UiTheme.StyleStatValue(lblYieldValue, UiStatKind.Yield, true);
            UiTheme.StyleStatValue(lblCycleValue, UiStatKind.Normal, false);

            // ---- ③ 运行控制按钮 ----
            // 这五个已是 UiButton（自绘），语义色改由 Kind 决定；
            // StyleButton 只改 Flat 配色，对自绘控件无效，故不再调用。
            btnLoop.Kind = UiButtonKind.Start;
            btnPause.Kind = UiButtonKind.Pause;
            btnStop.Kind = UiButtonKind.Stop;
            btnGrabOnce.Kind = UiButtonKind.Grab;
            btnResetStats.Kind = UiButtonKind.Reset;

            // ---- ④ 运行状态 ----
            UiTheme.StyleStatLabel(lblRunStateCap);
            UiTheme.StyleStatLabel(lblCameraCap);
            UiTheme.StyleStatLabel(lblFlowCap);
            UiTheme.StyleStatLabel(lblLastCap);

            Label[] stateValues =
            {
                lblRunStateValue, lblCameraValue, lblFlowValue, lblLastValue
            };
            foreach (Label v in stateValues)
            {
                if (v == null) continue;
                v.Font = UiTheme.FontBody;
                v.ForeColor = UiTheme.ColorTextPrimary;
            }
            tableRunState.BackColor = UiTheme.ColorPanelBg;

            lblConsecutiveNg.Font = UiTheme.FontButton;
            lblConsecutiveNg.BackColor = UiTheme.ColorWarn;
            lblConsecutiveNg.ForeColor = UiTheme.ColorTextOnFill;

            // ---- 日志区 ----
            listLog.BackColor = UiTheme.ColorViewBg;
            listLog.ForeColor = UiTheme.ColorTextSecondary;
            listLog.Font = UiTheme.FontMono;
            panelBottom.Height = _logCollapsed ? LogFoldedHeight : UiTheme.LogHeight;
            UiTheme.StyleButton(btnFoldLog, UiButtonKind.Neutral);
            btnFoldLog.Font = UiTheme.FontBody;
        }

        /// <summary>
        /// 切换深浅色板后由 <see cref="UiThemeManager"/> 调用：重套本窗体。
        /// <para>【为什么不能只靠 UiStyle.ApplyTree】BuildViews 动态创建的视图 /
        /// 标题条 / 绑定下拉框不在这棵静态树里（且 ImageViewer 构造时把底色硬编码成
        /// #202024），ApplyTree 又对自绘控件整棵跳过，只有这里能补上。</para>
        /// </summary>
        internal void ReapplyTheme()
        {
            // ① 静态树 + 语义色：ApplyTheme 幂等，重跑即拿到新色板
            ApplyTheme();

            // ② 主徽标：ApplyTheme 里被重置成 "--"（灰），这里用 _verdict 原样还原。
            //    不走 SetVerdict —— 它会累加连续 NG 计数，主题切换不该动业务计数。
            UiTheme.StyleBadge(lblVerdict,
                !_verdict.HasValue ? UiVerdict.None : (_verdict.Value ? UiVerdict.Ok : UiVerdict.Ng));

            // ③ BuildViews 动态创建的控件
            foreach (ImageViewer v in _views)
            {
                if (v == null) continue;
                v.BackColor = UiTheme.ColorViewBg;
                // ImageViewer 的 BackColor 只管控件本身；外层 cell 面板是容器，要一起改
                if (v.Parent != null) v.Parent.BackColor = UiTheme.ColorViewBg;
            }

            foreach (Label caption in _captions)
            {
                if (caption == null) continue;
                caption.ForeColor = UiTheme.ColorTextSecondary;
                caption.BackColor = UiTheme.ColorHeaderBg;
                // 标题条本身同样被强制覆盖过，一起还原成标题栏底色
                if (caption.Parent != null) caption.Parent.BackColor = UiTheme.ColorHeaderBg;
            }

            foreach (ComboBox bind in _bindCombos)
            {
                if (bind == null) continue;
                bind.BackColor = UiTheme.ColorCardBg;
                bind.ForeColor = UiTheme.ColorTextPrimary;
            }

            // ④ 视图角标：浅色强制覆盖会把它们的语义底刷成面板色，这里按当前文本原样还原
            //    （文本就是结论字面 OK / NG，不需要额外缓存即可无损复原）。
            foreach (Label badge in _badges)
            {
                if (badge == null) continue;

                if (string.Equals(badge.Text, "OK", StringComparison.Ordinal))
                    UiTheme.StyleViewBadge(badge, UiVerdict.Ok);
                else if (string.Equals(badge.Text, "NG", StringComparison.Ordinal))
                    UiTheme.StyleViewBadge(badge, UiVerdict.Ng);
                else
                    UiTheme.StyleViewBadge(badge, UiVerdict.None);
            }

            Invalidate(true);
        }

        /// <summary>
        /// 日志行分级着色（FR-UI-09）：ERROR 行橙、含 NG 的行红、含 OK 的行绿、其余次文本色。
        /// <c>DrawMode=OwnerDrawFixed</c> 下必须自己画背景与文本，否则列表全黑。
        /// </summary>
        private void OnLogDrawItem(object sender, DrawItemEventArgs e)
        {
            if (e == null || e.Index < 0 || e.Index >= listLog.Items.Count) return;

            string line = listLog.Items[e.Index] as string ?? "";

            Color fore = UiTheme.ColorTextSecondary;
            if (line.IndexOf("[ERROR]", StringComparison.Ordinal) >= 0) fore = UiTheme.ColorWarn;
            else if (line.IndexOf("NG", StringComparison.Ordinal) >= 0) fore = UiTheme.ColorNg;
            else if (line.IndexOf("OK", StringComparison.Ordinal) >= 0) fore = UiTheme.ColorOk;

            try
            {
                using (var back = new SolidBrush(UiTheme.ColorViewBg))
                {
                    e.Graphics.FillRectangle(back, e.Bounds);
                }

                using (var brush = new SolidBrush(fore))
                {
                    e.Graphics.DrawString(line, UiTheme.FontMono, brush, e.Bounds.Left, e.Bounds.Top);
                }

                if ((e.State & DrawItemState.Focus) == DrawItemState.Focus)
                    e.DrawFocusRectangle();
            }
            catch (Exception)
            {
                // 绘制期间控件可能已释放，忽略即可（不影响下一行绘制）
            }
        }

        /// <summary>折叠 / 展开日志区（FR-UI-09），状态持久化到 <c>UiSettings.LogCollapsed</c>。</summary>
        private void OnFoldLog(object sender, EventArgs e)
        {
            _logCollapsed = !_logCollapsed;
            ApplyLogCollapsed();

            ConfigStore.Current.Ui.LogCollapsed = _logCollapsed;
            ConfigStore.Save();
        }

        private void ApplyLogCollapsed()
        {
            panelBottom.Height = _logCollapsed ? LogFoldedHeight : UiTheme.LogHeight;
            btnFoldLog.Text = _logCollapsed ? "日志  ▲" : "日志  ▼";
        }

        /// <summary>折叠 / 展开右栏（FR-UI-11），展开图像显示区；仅隐藏面板，不销毁内部控件。</summary>
        private void OnToggleRightPanel(object sender, EventArgs e)
        {
            _rightCollapsed = !_rightCollapsed;
            panelRight.Visible = !_rightCollapsed;
            tsBtnRightPanel.Text = _rightCollapsed ? "展开右栏" : "折叠右栏";
        }

        #endregion

        #region 流程结果

        /// <summary>
        /// 流程结果回传：按 <see cref="FlowOutputEventArgs.ViewIndex"/> 输出到对应视图。
        /// 视图序号越界时跳过并提示（不改写绑定）；帧的所有权在此消费并释放。
        /// </summary>
        private void OnFlowOutput(object sender, FlowOutputEventArgs e)
        {
            if (e == null) return;

            // 每收到一条流程输出即计入对应流程的 OK/NG（独立于下方帧显示节流，保证计数精确，
            // 且合帧跳过显示时数字仍实时累加）
            RecordFlowVerdict(e.FlowId, e.FlowName, e.Passed);

            int idx = e.ViewIndex - 1;

            // 越界：跳过显示 + 可读中文提示（状态栏 + 日志）
            if (e.ViewIndex < 1 || e.ViewIndex > _views.Count)
            {
                Logger.Warn(string.Format("流程绑定视图越界（视图 {0}，当前共 {1} 个视图），已跳过显示。",
                    e.ViewIndex, _views.Count));

                if (!IsDisposed && IsHandleCreated)
                {
                    try
                    {
                        BeginInvoke((MethodInvoker)delegate
                        {
                            statusLabel.Text = string.Format(
                                "流程结果视图越界（视图 {0} 超出当前 {1} 个视图），已跳过显示；请在「流程编排」中修改绑定。",
                                e.ViewIndex, _views.Count);
                        });
                    }
                    catch (Exception) { }
                }

                if (e.Frame != null) e.Frame.Dispose();
                return;
            }

            if (IsDisposed || !IsHandleCreated)
            {
                if (e.Frame != null) e.Frame.Dispose();
                return;
            }

            // —— 覆盖式合帧（诉求 2 回归修复）——
            // 多流程并行时每条流程每帧都回传结果。若逐结果一次 BeginInvoke 做整幅帧克隆 + 重绘，
            // 单 UI 线程会被 N×30fps 的发布淹没，消息队列积压 → 界面卡死/无显示。
            // 改为：每个视图只保留最新一帧（中间帧被替换即释放），且同一视图最多投递一次 BeginInvoke；
            // 渲染跟不上时天然丢弃中间帧（与 OnLiveFrame 同一范式）。
            PendingFlowOutput slot;
            lock (_pendingOutputsSync)
            {
                if (!_pendingOutputs.TryGetValue(idx, out slot) || slot == null)
                {
                    slot = new PendingFlowOutput();
                    _pendingOutputs[idx] = slot;
                }
                if (slot.Frame != null) slot.Frame.Dispose();   // 旧帧被替换，立即释放
                slot.Frame = e.Frame;            // 取得帧所有权（原事件持有）
                slot.Shapes = e.Shapes;
                slot.Passed = e.Passed;
                slot.Summary = e.Summary ?? "";
                slot.CameraName = e.CameraName ?? "";
                e.Frame = null;                  // 所有权已转移，防止重复释放
            }

            // 已有待处理投递则跳过本次投递（最新帧已留在 slot.Frame 中）
            if (System.Threading.Interlocked.Exchange(ref slot.Posted, 1) == 1) return;

            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    PendingFlowOutput s;
                    Bitmap frame;
                    List<IGeometryShape> shapes;
                    bool passed;
                    string summary;
                    string cameraName;

                    lock (_pendingOutputsSync)
                    {
                        if (!_pendingOutputs.TryGetValue(idx, out s) || s == null) return;
                        System.Threading.Interlocked.Exchange(ref s.Posted, 0);   // 允许下次投递
                        // 取出最新快照后释放锁，再操作 UI
                        frame = s.Frame;
                        shapes = s.Shapes;
                        passed = s.Passed;
                        summary = s.Summary;
                        cameraName = s.CameraName;
                        s.Frame = null;   // 取出，下方交由视图或释放处理
                        s.Shapes = null;
                    }

                    if (IsDisposed || !IsHandleCreated) { if (frame != null) frame.Dispose(); return; }

                    try
                    {
                        ImageViewer view = _views[idx];
                        Bitmap disp = frame != null ? (Bitmap)frame.Clone() : null;
                        if (disp != null) view.SetImage(disp, true);   // 克隆后由控件持有，原帧随后释放

                        if (idx < _captions.Count && _captions[idx] != null)
                            _captions[idx].Text = "  " + (string.IsNullOrEmpty(cameraName) ? "流程结果" : cameraName)
                                                  + " · " + ActiveFlowName;

                        // 流程结论同样驱动右栏徽标、视图角标与状态栏（与配方检测链路同一套口径）
                        _lastSummary = summary ?? "";
                        SetVerdict(passed);
                        SetViewVerdict(idx, passed);
                        UpdateStats();       // 按流程刷 OK/NG 数字（节流随帧显示节奏，计数已在事件层完成）
                        UpdateStatus();

                        view.ClearShapes();
                        if (shapes != null)
                        {
                            foreach (IGeometryShape sh in shapes)
                                if (sh != null) view.Shapes.Add(sh);
                        }
                        view.Shapes.NotifyChanged();   // 改几何后必须通知重绘
                    }
                    finally
                    {
                        if (frame != null) frame.Dispose();
                    }
                });
            }
            catch (Exception ex)
            {
                // BeginInvoke 投递失败：重置标志并释放帧，避免该视图再也无法刷新
                System.Threading.Interlocked.Exchange(ref slot.Posted, 0);
                lock (_pendingOutputsSync)
                {
                    if (slot.Frame != null) { slot.Frame.Dispose(); slot.Frame = null; }
                }
                Logger.Warn("显示流程结果失败：" + ex.Message);
            }
        }

        #endregion

        #region 流程下拉运行（选流程 → 选本地图片 → 运行，结果显示在主视图）

        /// <summary>
        /// 初始化本窗体自有的流程执行器与运行控制器（镜像 FlowForm 构造函数的降级策略）：
        /// 算法库不可用时置 null，流程下拉框禁用并提示，不影响其它功能。
        /// </summary>
        private void InitFlowRunner()
        {
            try
            {
                _flowRunner = new FlowRunner();
                _flowCtrl = new FlowRunController(_flowRunner);
                _flowCtrl.OutputReady += OnFlowRunnerOutput;
                _flowCtrl.Completed += OnFlowRunCompleted;
            }
            catch (Exception ex)
            {
                _flowRunner = null;
                _flowCtrl = null;
                Logger.Error("流程执行器初始化失败（算法库不可用？）：" + ex.Message);
            }

            if (_flowCtrl == null && tsComboFlow != null)
            {
                tsComboFlow.Enabled = false;
                tsComboFlow.ToolTipText = "算法库不可用";
            }
        }

        /// <summary>
        /// 回填流程下拉框（<see cref="ConfigStore"/>.Current.Flows）：保留原选中项（按 Id）。
        /// <see cref="_loadingFlowCombo"/> 期间抑制 SelectedIndexChanged 触发运行。
        /// </summary>
        private void RefreshFlowCombo()
        {
            if (tsComboFlow == null) return;

            _loadingFlowCombo = true;
            try
            {
                FlowDefinition previous = tsComboFlow.SelectedItem as FlowDefinition;
                string previousId = (previous != null) ? previous.Id : null;

                tsComboFlow.Items.Clear();

                FlowSettings fs = ConfigStore.Current.Flows;
                if (fs != null && fs.Flows != null)
                {
                    foreach (FlowDefinition f in fs.Flows)
                        if (f != null) tsComboFlow.Items.Add(f);
                }

                if (!string.IsNullOrEmpty(previousId))
                {
                    for (int i = 0; i < tsComboFlow.Items.Count; i++)
                    {
                        FlowDefinition f = tsComboFlow.Items[i] as FlowDefinition;
                        if (f != null && string.Equals(f.Id, previousId, StringComparison.Ordinal))
                        {
                            tsComboFlow.SelectedIndex = i;
                            break;
                        }
                    }
                }
            }
            finally
            {
                _loadingFlowCombo = false;
            }
        }

        /// <summary>下拉框选中流程即触发：选一张本地图片并运行该流程（回填期间抑制）。</summary>
        private void OnFlowComboSelected(object sender, EventArgs e)
        {
            if (_loadingFlowCombo) return;
            RunSelectedFlow();
        }

        /// <summary>
        /// 运行下拉框当前选中的流程：选一张本地图片 → 把克隆里<strong>所有</strong>图像源节点
        /// 改为该图片（保证无需相机）→ 校验 → 经本窗体自有的 FlowRunController 以 camera=null 运行；
        /// 结果经 FlowOutputChannel 既有链路显示到主视图并驱动 OK/NG 徽标。
        /// </summary>
        private void RunSelectedFlow()
        {
            if (_flowRunner == null) { MessageBox.Show(this, "算法库不可用，无法运行流程。"); return; }
            if (_flowCtrl == null || _flowCtrl.IsBusy) return;

            FlowDefinition selected = tsComboFlow.SelectedItem as FlowDefinition;
            if (selected == null) return;

            // 坑④：绝不改 ConfigStore 里的原定义——直接改会把 FlowForm 里配置的图片路径持久化覆盖，
            // 且可能与并行运行的流程并发改参数。统一跑克隆。
            FlowDefinition clone = selected.Clone();

            string imagePath;
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "选择要检测的本地图片";
                dlg.Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp|所有文件|*.*";
                dlg.CheckFileExists = true;
                if (!string.IsNullOrEmpty(_lastImageDir) && Directory.Exists(_lastImageDir))
                    dlg.InitialDirectory = _lastImageDir;

                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                imagePath = dlg.FileName;
                _lastImageDir = Path.GetDirectoryName(imagePath);
            }

            // 把克隆里所有图像源节点都指到这张图（写回键与 FlowForm.OnPreviewImageLoaded 一致）
            int imageSources = 0;
            if (clone.Nodes != null)
            {
                foreach (FlowNode node in clone.Nodes)
                {
                    if (!IsImageSourceNode(node)) continue;
                    node.Set("Source", "File");
                    node.Set("FilePath", imagePath);
                    imageSources++;
                }
            }

            if (imageSources == 0)
            {
                MessageBox.Show(this, "该流程没有图像源节点，无法用本地图片运行。", "无法运行",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 双保险：所有图像源已是 File 时 NeedsCamera 必为 false；仍为 true 说明存在别的取图路径
            if (FlowRunner.NeedsCamera(clone))
            {
                MessageBox.Show(this, "该流程仍需相机（存在未改为本地图片的图像源）。", "无法运行",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string err;
            if (!FlowRunner.Validate(clone, out err))
            {
                MessageBox.Show(this, "无法运行：" + err, "流程校验", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 坑⑦：OnFlowOutput 只显示 ViewIndex∈[1, 视图数] 的结果，越界直接跳过（帧被丢弃）。
            // 流程自身绑定在范围内则尊重，越界回落到第一个视图。
            if (clone.ViewIndex < 1 || clone.ViewIndex > _views.Count) clone.ViewIndex = 1;

            tsComboFlow.Enabled = false;   // 运行期间禁选，OnFlowRunCompleted 里恢复
            Logger.Info("主界面下拉运行流程：" + selected.Name + "（本地图片 " + Path.GetFileName(imagePath) + "）");
            _flowCtrl.RunOnce(clone, null);   // 图像源全部为 File → NeedsCamera=false，camera=null 合法
        }

        /// <summary>节点是「图像源」时返回 true（与 FlowForm.IsImageSource 同一口径）。</summary>
        private static bool IsImageSourceNode(FlowNode node)
        {
            if (node == null) return false;

            FlowNodeType type;
            return node.TryGetType(out type) && type == FlowNodeType.ImageSource;
        }

        /// <summary>
        /// 本窗体流程控制器的结果回传：与 FlowForm.OnRunnerOutput 同款桥接——
        /// 转发给 FlowOutputChannel（MainForm.OnFlowOutput 消费帧并显示）；
        /// 无订阅者时由发布方自行释放，避免帧泄漏。
        /// </summary>
        private void OnFlowRunnerOutput(object sender, FlowOutputEventArgs e)
        {
            if (e == null) return;

            if (!FlowOutputChannel.Publish(e) && e.Frame != null)
                e.Frame.Dispose();
        }

        /// <summary>
        /// 流程运行彻底结束（含单帧 RunOnce——控制器在工作线程 finally 里统一触发）：
        /// 切回 UI 线程恢复流程下拉框可选。
        /// </summary>
        private void OnFlowRunCompleted(object sender, EventArgs e)
        {
            if (IsDisposed || !IsHandleCreated) return;

            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    if (!IsDisposed && tsComboFlow != null) tsComboFlow.Enabled = true;
                });
            }
            catch (Exception) { }
        }

        #endregion

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            // 【关窗铁律】StopAll → JoinAll → Dispose 各槽 Runner → CameraManager.CloseAll()（共享知识 30）。
            // 顺序绝不可颠倒：先把相机关掉，流程线程下一帧就会取帧失败甚至崩在厂商 DLL 里。
            if (!_flowStopRequested)
            {
                _flowStopRequested = true;

                try { FlowRunManager.Shutdown(); }
                catch (Exception ex) { Logger.Warn("并行流程收尾失败：" + ex.Message); }

                if (FlowRunManager.RunningCount > 0)
                {
                    // 仍有线程没退出来：取消本次关闭并 800 ms 后重试，
                    // **绝不强制 Dispose** —— 强关会把原生句柄留在半初始化状态。
                    e.Cancel = true;

                    var retry = new Timer { Interval = 800 };
                    retry.Tick += delegate
                    {
                        retry.Stop();
                        retry.Dispose();
                        try { Close(); }
                        catch (Exception) { }
                    };
                    retry.Start();
                    return;
                }
            }

            _closing = true;
            loopTimer.Stop();
            if (_logTimer != null) { _logTimer.Stop(); _logTimer = null; }
            // 释放可能残留的待显示帧，避免关窗后 Bitmap 泄漏
            lock (_pendingOutputsSync)
            {
                foreach (var kv in _pendingOutputs)
                {
                    if (kv.Value != null && kv.Value.Frame != null) kv.Value.Frame.Dispose();
                }
                _pendingOutputs.Clear();
            }
            // 连续实时预览收尾：退订 FrameReceived + StopGrab，早于 CloseAll，避免关窗后残留处理器
            StopLive();

            // 本窗体自有的流程控制器收尾：先停 → Join → Dispose，再释放算法上下文；
            // 必须早于 CameraManager.CloseAll()（关窗铁律：先停流程线程，再关相机）。
            if (_flowCtrl != null)
            {
                try { _flowCtrl.StopLoop(); } catch (Exception) { }
                try { _flowCtrl.Join(2000); } catch (Exception) { }
                try { _flowCtrl.Dispose(); } catch (Exception) { }
                _flowCtrl = null;
            }
            if (_flowRunner != null)
            {
                try { _flowRunner.Dispose(); } catch (Exception) { }
                _flowRunner = null;
            }

            // 退出前确保视图绑定与布局被持久化
            SyncViewBindingsToConfig();
            ConfigStore.Save();
            Logger.Logged -= OnLogged;
            RecipeStore.CurrentChanged -= OnRecipeChanged;
            CameraManager.Instance.CurrentChanged -= OnCameraManagerChanged;
            ConfigStore.Changed -= OnConfigChanged;
            FlowOutputChannel.OutputReady -= OnFlowOutput;
            CameraManager.Instance.CloseAll();
        }
    }
}
