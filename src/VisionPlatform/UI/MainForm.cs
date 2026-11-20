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

        /// <summary>
        /// 当前是否拥有全部权限（唯一权限位，由 <see cref="ApplyPermissions"/> 从
        /// <see cref="AppContext.HasFullAccess"/> 同步）。
        /// <para>软件启动不登录 = 访客 = false：界面上只有「日志查看」可用；
        /// 管理员经工具栏「登录」校验通过后变 true，全部功能解锁。</para>
        /// <para>运行控制按钮的可用态<b>必须</b>与本位相与（见 <see cref="UpdateRunButtons"/>），
        /// 否则 <see cref="UpdateStatus"/> 每次刷新都会把它们重新置为启用。</para>
        /// </summary>
        private bool _fullAccess;

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

        /// <summary>统计数字变更标记：仅当 OK/NG 计数或节拍真正变化时才刷新统计卡片与状态栏，
        /// 避免连续运行逐帧重建表格/重绘导致的卡顿（数值只在判定事件产生时变化，而非每渲染帧）。</summary>
        private volatile bool _statsDirty = false;

        /// <summary>
        /// 状态栏 / 统计卡片的最小刷新间隔（毫秒）。
        /// <para>【为什么需要】连续运行时每帧都会 <see cref="RecordFlowVerdict"/> 把 _statsDirty 置 true，
        /// 于是"只在变化时刷新"退化成"每帧刷新"：两行长 string.Format + 5 个按钮态 + 右栏 4 个标签，
        /// 30fps 下纯属白烧 CPU。数字仍然是按件精确累计，只是把<strong>显示</strong>合并到 5Hz；
        /// 停止/暂停/切配方等状态变化仍走 <see cref="UpdateStatus"/> 立即刷新，不会滞后。</para>
        /// </summary>
        private const int StatusUiMinIntervalMs = 200;

        /// <summary>上次刷新状态栏的时间戳（Environment.TickCount，仅用于间隔比较）。</summary>
        private int _lastStatusUiTick;

        /// <summary>连续运行时的 UI 计数（仅在 VA_UI_STATS=1 时每 5s 落一条日志，用于定位"谁在刷"）。</summary>
        private int _uiFrameCount;
        private int _uiStatusRefreshCount;
        private int _uiStatsRefreshCount;
        private int _uiStatTickBase;
        private readonly bool _uiStatsProbe =
            Environment.GetEnvironmentVariable("VA_UI_STATS") == "1";

        /// <summary>流程名解析缓存（见 <see cref="ActiveFlowName"/>）：配置对象或 ActiveFlowId 变了才重算。</summary>
        private FlowSettings _activeFlowSettings;
        private string _activeFlowIdStamp;
        private string _activeFlowName = "--";

        /// <summary>单流程 OK/NG 累计（仅供 <see cref="_flowStats"/> 使用）。</summary>
        private sealed class FlowOkNg
        {
            public string Name = "";
            public int Ok;
            public int Ng;
            public int Total { get { return Ok + Ng; } }
        }

        /// <summary>「各流程统计」表中某一流程对应的 4 个单元格标签（流程名/OK/NG/成功率）。</summary>
        private sealed class FlowRowCtl
        {
            public Label Name;
            public Label Ok;
            public Label Ng;
            public Label Rate;
            public Label Status;
        }

        /// <summary>流程标识 → 该行标签集合的缓存，避免每帧重建控件（连续运行卡死的根因）。</summary>
        private readonly Dictionary<string, FlowRowCtl> _flowRowLabels =
            new Dictionary<string, FlowRowCtl>(StringComparer.Ordinal);

        /// <summary>逐流程表格行高（像素）。固定行高可避免数值刷新触发行高重算与布局抖动。</summary>
        private const int FlowRowHeight = 22;

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
        private LogForm _logForm;

        // ---- 连续实时预览（主界面 live preview）----
        // 复用 CameraForm / LocalCamera 的成熟链路：StartGrab → FrameReceived → 覆盖式合帧 →
        // 单次 BeginInvoke 上 UI 线程 → 主视图 ImageViewer。与循环运行(loopTimer)通过
        // 「live 开启时跳过 loopTimer 取帧」协调，避免同一相机被双重取帧。
        private bool _liveRunning;               // 连续实时预览是否进行中
        private ICamera _liveCamera;             // 当前实时预览绑定的相机（退订 + StopGrab 用）
        private Bitmap _pendingLiveFrame;        // 覆盖式：后台线程只保留「最新一帧」
        private int _pendingLivePosted;          // 0=无待显示帧；1=已投递一帧待处理
        private bool _liveViewerFitted;          // 首个实时帧做一次 FitToWindow
        private int _lastLiveRenderTick;         // 上次实时预览渲染时刻（显示节流用）

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
            public Bitmap Frame;                 // 输出图像（原图；所有权在此，替换时释放）
            public Bitmap DisplayFrame;          // 显示用合成帧（掩膜/伪彩）；为空则显示 Frame
            public List<IGeometryShape> Shapes;  // 最新叠加图形（引用，不克隆）
            public VisionSystem.Controls.PointCloud.PointCloudData Cloud;  // 3D 点云（引用，无释放手续）；非空时优先显示
            public bool Passed;
            public string Summary = "";
            public string CameraName = "";
            public int Posted;                   // 0=无待处理；1=已投递一次 BeginInvoke
            public int LastRenderTick;           // 上次真正渲染该视图的时刻（Environment.TickCount）
        }

        /// <summary>
        /// 视图渲染的最小间隔（毫秒，默认 66ms ≈ 15fps）。
        /// <para>【为什么限】每渲染一帧都要把 5MP 原图重采样到视图尺寸（GDI+ 软件光栅下约 14ms），
        /// 30fps 时单视图就吃掉半个核，多视图并行更惨。人眼对 15fps 的实时画面几乎没有区别，
        /// 算法侧的运行节拍完全不受影响（本限制只作用于显示）。</para>
        /// <para>需要更高刷新率时设环境变量 <c>VA_UI_RENDER_FPS=30</c>。</para>
        /// </summary>
        private static readonly int MinRenderIntervalMs = ResolveMinRenderIntervalMs();

        private static int ResolveMinRenderIntervalMs()
        {
            string raw = Environment.GetEnvironmentVariable("VA_UI_RENDER_FPS");
            int fps;
            if (int.TryParse(raw, out fps) && fps >= 1 && fps <= 240)
                return Math.Max(1, 1000 / fps);
            return 66;   // 15fps
        }

        // ---- 日志批量拉取（诉求 2 回归修复）----
        // Logger 在后台线程同步触发 Logged，原 OnLogged 每行一次 BeginInvoke；并行下 RESULT 行可达
        // 90 行/秒，同样淹没 UI 线程。改为缓冲 + UI 线程定时器批量拉取（与 LogPanel 设计意图一致）。
        private List<LogEntry> _logBuffer = new List<LogEntry>();

        /// <summary>日志区横向滚动范围（按已出现的最长行累计，只增不减）。</summary>
        private int _logMaxWidth;
        private readonly List<LogEntry> _logHistory = new List<LogEntry>();
        private readonly object _logSync = new object();
        private System.Windows.Forms.Timer _logTimer;
        private ToolStripComboBox _tsLogFilter;
        private const int LogFilterAll = 0;
        private const int LogFilterResult = 1;
        private const int LogFilterError = 2;
        private const int LogFilterReal = 3;
        private const int LogFilterSim = 4;

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

            // 性能归因：把 UI 线程登记进去，并把"重绘次数 / 建层耗时"的来源交给探针
            PerfProbe.Mark("界面");
            if (PerfProbe.Enabled)
            {
                PerfProbe.SetPaintSource(SumViewPaintCount, SumViewRebuildCount, SumViewRebuildMs);
            }

            // 主题在 InitializeComponent 之后统一套用：Designer 只写结构，颜色 / 字体单点来自 UiTheme
            ApplyTheme();

            // 权限位必须在主题套用后立刻落一次：启动是访客态，要保证首个可见帧就是「只有日志可用」。
            // 事件在这里手写绑定（Designer 重序列化会丢手写事件，见 MainForm.Designer.cs 的 tsBtnLogin 段）。
            tsBtnLogin.Click += OnLoginButton;
            ApplyPermissions();

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
            InitLogFilter();
            Logger.EntryLogged += OnEntryLogged;
            RecipeStore.CurrentChanged += OnRecipeChanged;
            CameraManager.Instance.CurrentChanged += OnCameraManagerChanged;

            // 日志批量拉取定时器（UI 线程，200ms 一次）：OnLogged 仅入缓冲，由它批量刷到 listLog，
            // 避免并行运行下海量日志行逐条 BeginInvoke 淹没 UI 线程（见 OnLogged / OnLogTimerTick）。
            _logTimer = new System.Windows.Forms.Timer { Interval = 200 };
            _logTimer.Tick += OnLogTimerTick;
            _logTimer.Start();

            // 双击日志行 → 弹窗显示完整内容（原本长行被控件宽度裁掉，只能看前半截）。
            // 事件在构造函数里挂（Designer 重序列化会丢掉手写的 += ，见项目铁律）。
            listLog.DoubleClick += OnLogDoubleClick;
            ConfigStore.Changed += OnConfigChanged;
            FlowOutputChannel.OutputReady += OnFlowOutput;
            FlowRunManager.StatusChanged += OnFlowManagerStatusChanged;

            // 静态事件的退订不能只靠 OnFormClosing：程序化 Dispose（不走 Close）时该事件不触发，
            // 静态事件会一直持有本窗体，Dispose 后每次 ConfigStore.Save() 都会回调到已释放控件上。
            this.Disposed += OnFormDisposed;

            // 主界面流程下拉：初始化自有执行器/控制器并回填流程列表（算法库不可用时降级禁用）
            InitFlowRunner();
            RefreshFlowCombo();

            _initializing = false;

            UpdateStatus();
            UpdateStats();
            UpdateRunButtons();
            SetVerdict(null);
        }

        /// <summary>
        /// 窗体首次显示后再刷一次相机下拉。
        /// <para>【勿删】构造函数里调 <see cref="RefreshCameraList"/> 时句柄尚未创建，
        /// 该方法首行的 <c>!IsHandleCreated</c> 守卫会直接早退；而启动阶段
        /// <c>CameraManager.OpenAll()</c> 是在 Splash 里（早于本窗体构造）就已把相机全部打开，
        /// 之后不会再有 <c>CurrentChanged</c> 触发刷新。缺了这一步，工具栏相机下拉恒为空、
        /// 状态栏恒显示「相机：--」，用户看不到「启动已打开全部相机」这个效果。</para>
        /// <para>用 OnShown 而不是 OnHandleCreated：句柄可能被重建（RecreateHandle）导致重复刷新，
        /// OnShown 只在窗体<strong>首次</strong>显示时触发一次（Hide 后再 Show 不会重触发）。</para>
        /// <para><b>用缓存，勿改成无参调用</b>：<see cref="RefreshCameraListFromCache"/> 复用 Splash 阶段
        /// <c>OpenAll()</c> 已经枚举出来的结果；改成 <see cref="RefreshCameraList"/> 会在 UI 线程重新做一次
        /// DirectShow 枚举（实测 ≈1.1s），把主窗体首帧冻住。</para>
        /// <para>这里<strong>不</strong>做任何权限判断：<see cref="RefreshCameraList"/> 内部会调
        /// <see cref="RefreshBindCombos"/>，后者已按 <c>_fullAccess</c> 决定绑定下拉是否可编辑，
        /// 权限口径保持单一来源。</para>
        /// </summary>
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            RefreshCameraListFromCache();   // 走枚举缓存：不要在首帧同步重新枚举（见方法注释）

            // 【默认运行模式】窗体首次显示后自动进入运行态（工位开机即生产）：
            //   · 通讯连接 / 心跳 / 触发轮询由 CommRuntime.Activate 在「运行」时建立，此处即触发；
            //   · 静默启动：前置条件不满足（无启用流程 / 无相机 / 通讯没起来）只记日志，不弹窗；
            //   · 访客权限下照样运行（只读看板），暂停 / 停止需管理员登录后操作 —— 这是权限位既有口径。
            // 延迟 800ms 让布局/相机下拉先就位，避免和首帧渲染抢时间。
            var autoStart = new Timer { Interval = 800 };
            autoStart.Tick += delegate
            {
                autoStart.Stop();
                autoStart.Dispose();

                try
                {
                    Logger.Info("[启动] 默认运行模式：自动执行「运行」");
                    StartLoop(true);
                }
                catch (Exception ex)
                {
                    Logger.Warn("[启动] 自动运行失败：" + ex.Message);
                }
            };
            autoStart.Start();
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
            if (!_fullAccess) return;   // 访客可切视图，但不落盘（与「只能查看日志」自洽，口径同 _bindCombos）

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

                    // 视图绑定的相机下拉属于「改配置」（OnBindComboChanged 会立即 ConfigStore.Save），
                    // 访客态必须禁用；BuildViews 新建的 combo 也要在这里吃到权限位，不能只靠 ApplyPermissions。
                    cb.Enabled = _fullAccess;
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
            if (!_fullAccess) return;          // 访客态不可改绑定（双保险，正常路径下拉已被禁用）
            SyncViewBindingsToConfig();
            ConfigStore.Save();
        }

        #endregion

        #region 相机

        /// <summary>
        /// 真实重新枚举并刷新工具栏相机下拉（并连带刷新各视图的绑定下拉）。
        /// <para>「刷新」按钮、相机管理器变更等<b>需要看到最新硬件</b>的场合用这个无参版本。</para>
        /// <para>启动首帧请不要用本方法，改用 <see cref="RefreshCameraListFromCache"/>：
        /// DirectShow 枚举实测单台相机 ≈1.1s，会冻住主窗体首帧。</para>
        /// </summary>
        private void RefreshCameraList()
        {
            RefreshCameraListCore(false);
        }

        /// <summary>
        /// 优先复用 <see cref="CameraManager.LastEnumerated"/> 刷新下拉；缓存未就绪才回落到真实枚举。
        /// <para>【专供启动首帧（<see cref="OnShown"/>）】启动期 Splash 的 <c>OpenAll()</c> 已经付过
        /// 一次枚举代价（≈1.1s），这里复用那份结果即可，不必在 UI 线程再冻一次。</para>
        /// </summary>
        private void RefreshCameraListFromCache()
        {
            RefreshCameraListCore(true);
        }

        /// <summary>
        /// 刷新相机下拉的公共实现。
        /// </summary>
        /// <param name="preferCachedEnumeration">
        /// true = 优先用 <see cref="CameraManager.LastEnumerated"/>；false = 真实枚举。
        /// </param>
        private void RefreshCameraListCore(bool preferCachedEnumeration)
        {
            if (_closing || IsDisposed || !IsHandleCreated) return;

            try
            {

                    CameraInfo previous = tsComboCameras.SelectedItem as CameraInfo;
                    string previousId = previous != null ? previous.Id : null;

                    tsComboCameras.BeginUpdate();
                    tsComboCameras.Items.Clear();

                    // 启动补刷走缓存，避免在 UI 线程同步做 DirectShow 枚举（实测 ≈1.1s）；
                    // 缓存未就绪（如 Splash 阶段枚举曾异常）才回落到真实枚举，此时慢一次但功能不丢。
                    List<CameraInfo> infos = null;
                    bool fromCache = false;
                    if (preferCachedEnumeration)
                    {
                        infos = CameraManager.Instance.LastEnumerated;
                        fromCache = infos != null;
                    }
                    if (infos == null) infos = CameraManager.Instance.Enumerate();

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

                    Logger.Info("相机列表就绪，共 " + infos.Count + " 个"
                                + (fromCache ? "（复用启动期枚举结果，未重新枚举）" : "（已重新枚举）"));
                
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

        /// <summary>
        /// 相机单帧采集（<strong>当前没有按钮挂它</strong>：主界面的「单帧采集」已改为「单步执行」）。
        /// 保留实现是因为相机链路排查时还要用；要在界面上恢复，把某个按钮的 Click 接回这里即可。
        /// </summary>
        private void GrabOnceFromCamera(object sender, EventArgs e)
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

                    // 与流程结果同一套显示节流：实时预览也不必逐帧重采样
                    int liveTick = Environment.TickCount;
                    if (_lastLiveRenderTick != 0
                        && unchecked(liveTick - _lastLiveRenderTick) < MinRenderIntervalMs)
                    {
                        bmp.Dispose();
                        return;
                    }
                    _lastLiveRenderTick = liveTick;

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

        /// <summary>
        /// 同步连续预览开关按钮文字（运行中显示「停止预览」，否则「连续预览」）。
        /// 可用态与权限位相与：访客（未登录）不可用（本方法被 <see cref="UpdateRunButtons"/> 反复调用，
        /// 原来是硬编码 <c>= true</c>，会绕过权限位）。
        /// </summary>
        private void UpdateLiveButton()
        {
            if (tsBtnLive == null) return;
            tsBtnLive.Text = _liveRunning ? "停止预览" : "连续预览";
            tsBtnLive.Enabled = _fullAccess;
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
        /// <para>【缓存】连续运行时每帧都要用它拼标题条文案，而原实现每次都 new 一个 lambda 把
        /// 整个流程列表扫一遍 —— 30fps × N 视图纯属白烧 CPU 和 GC。这里缓存解析结果，
        /// 只有当"配置对象换了"或"ActiveFlowId 变了"才重算，语义完全一致。</para>
        /// </summary>
        private string ActiveFlowName
        {
            get
            {
                FlowSettings fs = null;
                try { fs = ConfigStore.Current.Flows; }
                catch (Exception) { }

                if (ReferenceEquals(fs, _activeFlowSettings)
                    && (_activeFlowSettings == null || _activeFlowIdStamp == _activeFlowSettings.ActiveFlowId))
                {
                    return _activeFlowName;
                }

                _activeFlowSettings = fs;
                _activeFlowIdStamp = fs != null ? fs.ActiveFlowId : null;

                string name = "--";
                try
                {
                    if (fs != null && fs.Flows != null && fs.Flows.Count > 0)
                    {
                        FlowDefinition active = fs.Flows.Find(x => x != null && x.Id == fs.ActiveFlowId);
                        if (active != null && !string.IsNullOrEmpty(active.Name)) name = active.Name;
                        else if (fs.Flows[0] != null && !string.IsNullOrEmpty(fs.Flows[0].Name)) name = fs.Flows[0].Name;
                    }
                }
                catch (Exception)
                {
                    // 配置损坏时不应影响界面刷新
                }

                _activeFlowName = name;
                return name;
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
            _statsDirty = true;
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
        /// <param name="waitComm">
        /// 通讯未就绪时是否短暂等待（仅用户点「运行」时为 true）。
        /// <para>【为什么需要】通讯连接现在由「运行」触发（<see cref="CommRuntime.Activate"/>），
        /// 刚点下运行的那一瞬间链路还在建立：不等待就会误判成"通讯未连接"而拒绝启动。
        /// 但运行中的周期性复查（<see cref="OnLoopTick"/>）<b>不能</b>等 —— 那会把 UI 线程每拍卡 1.5s。</para>
        /// </param>
        private bool CanRunLoop(out string reason, bool waitComm = false)
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
                if (waitComm && WaitCommReady(1500)) return true;

                reason = "通讯未连接（TCP 客户端 / TCP 服务端 / Modbus 均未就绪）。\r\n\r\n"
                       + "请先在「通讯」中建立连接，或在「系统设置」中关闭「循环运行前要求通讯已连接」。";
                return false;
            }

            return true;
        }

        /// <summary>
        /// 等待通讯就绪（最多 <paramref name="timeoutMs"/> 毫秒）。
        /// <para>只在用户点「运行」时用：链路是在那一刻才开始建立的，必须给它一点时间。</para>
        /// </summary>
        private static bool WaitCommReady(int timeoutMs)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (CommState.HasActiveChannel) return true;
                System.Threading.Thread.Sleep(50);
            }
            return CommState.HasActiveChannel;
        }

        /// <summary>
        /// 当前是否为「触发式运行」（默认）。
        /// <para>触发式：流程线程起来后停在 <see cref="FlowSlotStatus.Waiting"/>，只有外部信号
        /// （<see cref="FlowRunManager.Trigger"/>，通常经通讯「触发流程」任务）才跑一轮，
        /// 跑完回到等待。连续式：起手就按帧率循环，仅供调试。</para>
        /// </summary>
        private static bool IsTriggeredRunMode
        {
            get
            {
                try
                {
                    SystemSettings s = ConfigStore.Current.System;
                    return s == null || s.IsTriggeredRun;
                }
                catch (Exception)
                {
                    return true;   // 读不到配置也按触发式（安全侧：宁可不动，不要乱跑）
                }
            }
        }

        private void OnStartLoop(object sender, EventArgs e)
        {
            StartLoop(false);
        }

        /// <summary>
        /// 启动 / 继续循环运行。
        /// <para>【步骤顺序】先 <see cref="CommRuntime.Activate"/>（运行态才建立通讯连接），
        /// 再校验前置条件，最后启动流程 —— 顺序颠倒会因"通讯还没连"而拒绝启动。</para>
        /// </summary>
        /// <param name="silent">静默模式：失败只记日志不弹窗（启动首帧自动运行时用）。</param>
        private bool StartLoop(bool silent)
        {
            // 运行态：建立通讯（后台线程连接，不冻 UI）+ 启动心跳/轮询任务
            try { CommRuntime.Activate(); }
            catch (Exception ex) { Logger.Warn("进入运行态失败：" + ex.Message); }

            string reason;
            if (!CanRunLoop(out reason, !_running))
            {
                Logger.Warn("无法启动循环运行：" + reason);
                if (!silent)
                {
                    MessageBox.Show(this, reason, "无法启动循环运行",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                UpdateStatus();
                return false;
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

                // 【运行模式分支】触发式（默认）：线程起来后停在 Waiting，等外部信号；
                // 连续式：起手就跑（调试 / 离线验证用）。二者的区别只在 StartOne 的 triggered 形参。
                bool triggered = IsTriggeredRunMode;
                var r = FlowRunManager.StartAll(triggered);
                if (r == null || !r.Success)
                {
                    _running = false;
                    string msg = r != null ? r.Message : "启动流程失败";
                    Logger.Warn("循环运行启动失败：" + msg);
                    if (!silent)
                    {
                        MessageBox.Show(this, msg, "循环运行启动失败",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
                else
                {
                    Logger.Info((triggered ? "进入运行态，等待触发（全部流程）：" : "开始连续运行（全部流程）：")
                        + (r.Message ?? ""));

                    // 部分流程未能启动（相机未打开 / 拓扑校验未过 / 产品不匹配 / 已被占用等）：
                    // 逐条列出原因，避免「看似没并行」却无从查证。完全成功时 r.Message 不含「失败」。
                    if (!string.IsNullOrEmpty(r.Message) && r.Message.IndexOf("失败") >= 0)
                    {
                        Logger.Warn("循环运行部分启动失败：" + r.Message);
                        if (!silent)
                        {
                            MessageBox.Show(this, r.Message, "部分流程未启动",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        }
                    }
                }
            }
            UpdateStatus();
            return _running;
        }

        private void OnPause(object sender, EventArgs e)
        {
            if (!_running) return;

            _paused = !_paused;
            // 诉求 2：暂停/继续映射到全部流程的 PauseAll/ResumeAll（旧 loopTimer 已弃用）
            try
            {
                if (_paused)
                {
                    FlowRunManager.PauseAll();
                    // 通讯一起停：心跳 / 触发轮询 / Busy·Ready 写入全部挂起（连接保活，继续时不重连）
                    CommRuntime.Suspend();
                }
                else
                {
                    CommRuntime.Activate();
                    FlowRunManager.ResumeAll();
                }
            }
            catch (Exception ex) { Logger.Warn("暂停/继续流程失败：" + ex.Message); }

            Logger.Info(_paused ? "已暂停（流程与通讯任务均已挂起）" : "已继续");
            UpdateStatus();
        }

        private void OnStop(object sender, EventArgs e)
        {
            // 诉求 2：停止全部并行流程（并行 Join + 释放各自执行器）；旧 loopTimer 一并停掉
            try { FlowRunManager.StopAll(); }
            catch (Exception ex) { Logger.Warn("停止流程失败：" + ex.Message); }
            // 停止 = 退出运行态：通讯任务与自动连接一并停下，不再空转刷线程
            try { CommRuntime.Suspend(); }
            catch (Exception ex) { Logger.Warn("停止通讯任务失败：" + ex.Message); }
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
            }
            _statTotal = 0;
            _statOk = 0;
            _statNg = 0;
            _lastElapsedMs = 0;
            _consecutiveNg = 0;
            _statsDirty = false;
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

        /// <summary>
        /// 打开「相机标定」窗体（模态，Owner = this）。
        /// <para>用模态而不是 <c>ShowChild</c>：标定会改相机→机械的换算参数，</para>
        /// <para>标定期间不应同时跑流程 / 连续预览，避免"边标定边被别的采集抢相机"。</para>
        /// </summary>
        private void OnOpenCalibForm(object sender, EventArgs e)
        {
            using (var dlg = new CalibForm())
            {
                dlg.ShowDialog(this);
            }
        }

        private void OnOpenRecipeForm(object sender, EventArgs e) { ShowChild(ref _recipeForm); }

        /// <summary>打开日志查看窗体（按日期查看 NG 与软件报错）。</summary>
        private void OnOpenLogForm(object sender, EventArgs e) { ShowChild(ref _logForm); }

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
        /// 按「流程标识」聚合，使主界面能同时列出每个流程各自的 OK/NG/成功率。
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
                t.Name = name;
                if (passed) t.Ok++; else t.Ng++;
                _statsDirty = true;   // 计数变化，下一帧 UI 刷新一次即可（不再逐帧重建）
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
        /// 刷新统计卡片（FR-UI-04 / FR-UI-05）。顶部「数据统计」卡片汇总全部分支流程的合计
        /// OK/NG/良率；下方「各流程统计」表再逐流程列出 OK/NG 与成功率（诉求 F：所有流程同时静态显示）。
        /// 良品率低于告警下限转橙、低于下限一半转红；无数据时显示 "--"。
        /// </summary>
        private void UpdateStats()
        {
            int ok = 0, ng = 0, total = 0;

            lock (_flowStatSync)
            {
                foreach (FlowOkNg v in _flowStats.Values) { ok += v.Ok; ng += v.Ng; }
                total = ok + ng;
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

            lblOkCap.Text = "OK";
            lblNgCap.Text = "NG";

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

            RefreshFlowStatTable();
        }

        /// <summary>
        /// 刷新「各流程统计」表：列出每一个曾产生过输出的流程及其 OK / NG / 成功率，
        /// 所有流程同时静态常驻显示（满足「把所有流程的 OK/NG 同时显示，不是反复刷新」诉求）。
        /// 必须在 UI 线程调用（直接操作 tableFlowStats 的子控件）。
        /// <para>性能要点：行结构只在「流程集合」发生变化时重建（新增流程 / 清零），连续运行逐帧
        /// 只更新既有标签的 Text（且仅在值真正变化时写入），避免每帧重建控件引发的布局风暴与卡死。</para>
        /// </summary>
        private void RefreshFlowStatTable()
        {
            if (tableFlowStats == null) return;

            // 锁内快照，不持锁操作 UI。合并「已产出判定的流程」与「当前运行槽」，
            // 保证 运行中 / 已中止 但尚无判定的流程也显示出来（否则 Faulted 流程完全不可见）。
            var slots = FlowRunManager.Snapshot();
            var slotById = new Dictionary<string, FlowRunSlot>(StringComparer.Ordinal);
            foreach (FlowRunSlot s in slots) if (s != null) slotById[s.FlowId] = s;

            var snap = new List<KeyValuePair<string, FlowOkNg>>();
            lock (_flowStatSync)
            {
                foreach (var kv in _flowStats)
                    snap.Add(new KeyValuePair<string, FlowOkNg>(kv.Key, kv.Value));
            }
            foreach (FlowRunSlot s in slots)
            {
                if (s == null) continue;
                bool hasStat;
                lock (_flowStatSync) { hasStat = _flowStats.ContainsKey(s.FlowId); }
                if (!hasStat)
                    snap.Add(new KeyValuePair<string, FlowOkNg>(s.FlowId, new FlowOkNg { Name = s.FlowName }));
            }

            // ① 仅当「流程集合」变化时才重建行结构（新增流程 / 清零后变空）
            bool structureChanged = snap.Count != _flowRowLabels.Count;
            if (!structureChanged)
            {
                foreach (var kv in snap)
                    if (!_flowRowLabels.ContainsKey(kv.Key)) { structureChanged = true; break; }
            }

            if (structureChanged)
            {
                // 清掉表头(row 0)以外的动态行，并释放旧标签句柄
                for (int i = tableFlowStats.Controls.Count - 1; i >= 0; i--)
                {
                    var c = tableFlowStats.Controls[i];
                    if (tableFlowStats.GetRow(c) > 0)
                    {
                        tableFlowStats.Controls.RemoveAt(i);
                        c.Dispose();
                    }
                }
                _flowRowLabels.Clear();
                tableFlowStats.RowCount = 1;
                while (tableFlowStats.RowStyles.Count > 1)
                    tableFlowStats.RowStyles.RemoveAt(tableFlowStats.RowStyles.Count - 1);

                int r = 1;
                foreach (var kv in snap)
                {
                    FlowOkNg v = kv.Value;
                    tableFlowStats.RowStyles.Add(new RowStyle(SizeType.Absolute, FlowRowHeight));
                    var cName = MakeFlowCell(ContentAlignment.MiddleLeft, UiTheme.ColorTextPrimary);
                    var cOk = MakeFlowCell(ContentAlignment.MiddleRight, UiTheme.ColorOk);
                    var cNg = MakeFlowCell(ContentAlignment.MiddleRight, UiTheme.ColorNg);
                    var cRate = MakeFlowCell(ContentAlignment.MiddleRight, UiTheme.ColorTextSecondary);
                    var cStatus = MakeFlowCell(ContentAlignment.MiddleCenter, UiTheme.ColorTextSecondary);
                    tableFlowStats.Controls.Add(cName, 0, r);
                    tableFlowStats.Controls.Add(cOk, 1, r);
                    tableFlowStats.Controls.Add(cNg, 2, r);
                    tableFlowStats.Controls.Add(cRate, 3, r);
                    tableFlowStats.Controls.Add(cStatus, 4, r);
                    _flowRowLabels[kv.Key] = new FlowRowCtl { Name = cName, Ok = cOk, Ng = cNg, Rate = cRate, Status = cStatus };
                    r++;
                }
                tableFlowStats.RowCount = Math.Max(1, r);
                tableFlowStats.Height = tableFlowStats.RowCount * FlowRowHeight;
                tableFlowStats.PerformLayout();
                if (tableFlowStats.Parent != null) tableFlowStats.Parent.PerformLayout();
            }

            // ② 数值每次同步；仅「值变化」时才写文本，避免无谓刷新/重绘
            foreach (var kv in snap)
            {
                FlowRowCtl ctl;
                if (!_flowRowLabels.TryGetValue(kv.Key, out ctl)) continue;
                FlowOkNg v = kv.Value;
                string name = string.IsNullOrEmpty(v.Name) ? kv.Key : v.Name;
                int total = v.Total;
                string rate = total > 0 ? (v.Ok * 100.0 / total).ToString("F1") + " %" : "--";
                SetFlowText(ctl.Name, TrimFlowName(name, 16));
                SetFlowText(ctl.Ok, v.Ok.ToString());
                SetFlowText(ctl.Ng, v.Ng.ToString());
                SetFlowText(ctl.Rate, rate);

                // 状态列：优先取运行槽实时状态；无槽（已停止 / 从未启动）显示「已停止」
                FlowRunSlot slot;
                slotById.TryGetValue(kv.Key, out slot);
                string statusText; Color statusColor;
                if (slot != null)
                {
                    switch (slot.Status)
                    {
                        case FlowSlotStatus.Running:  statusText = "运行中"; statusColor = UiTheme.ColorOk; break;
                        case FlowSlotStatus.Paused:   statusText = "已暂停"; statusColor = UiTheme.ColorTextSecondary; break;
                        case FlowSlotStatus.Starting: statusText = "启动中"; statusColor = UiTheme.ColorTextSecondary; break;
                        case FlowSlotStatus.Stopping: statusText = "停止中"; statusColor = UiTheme.ColorTextSecondary; break;
                        case FlowSlotStatus.Faulted:  statusText = "已中止"; statusColor = UiTheme.ColorNg; break;
                        default:                      statusText = "已停止"; statusColor = UiTheme.ColorTextSecondary; break;
                    }
                }
                else { statusText = "已停止"; statusColor = UiTheme.ColorTextSecondary; }
                SetFlowText(ctl.Status, statusText);
                if (ctl.Status != null) ctl.Status.ForeColor = statusColor;
            }
        }

        /// <summary>构造一个固定高度、随单元格填满的表格单元格标签。</summary>
        private static Label MakeFlowCell(ContentAlignment align, Color color)
        {
            return new Label
            {
                AutoSize = false,
                Dock = DockStyle.Fill,
                TextAlign = align,
                ForeColor = color,
                UseMnemonic = false
            };
        }

        /// <summary>仅当文本真正变化时写入，避免每帧重复赋值触发重绘。</summary>
        private static void SetFlowText(Label label, string text)
        {
            if (label != null && label.Text != text) label.Text = text;
        }

        /// <summary>全部视图的累计重绘次数（性能探针用，只读）。</summary>
        private long SumViewPaintCount()
        {
            long sum = 0;
            for (int i = 0; i < _views.Count; i++)
            {
                ImageViewer v = _views[i];
                if (v != null) sum += v.PaintCount;
            }
            return sum;
        }

        /// <summary>全部视图累计"重建缓存层"的次数（性能探针用）。</summary>
        private long SumViewRebuildCount()
        {
            long sum = 0;
            for (int i = 0; i < _views.Count; i++)
            {
                ImageViewer v = _views[i];
                if (v != null) sum += v.LayerRebuildCount;
            }
            return sum;
        }

        /// <summary>全部视图累计"重建缓存层"的耗时（毫秒，性能探针用）。</summary>
        private double SumViewRebuildMs()
        {
            double sum = 0;
            for (int i = 0; i < _views.Count; i++)
            {
                ImageViewer v = _views[i];
                if (v != null) sum += v.LayerRebuildTotalMs;
            }
            return sum;
        }

        /// <summary>流程运行状态变化 → 刷新各流程状态列（见 OnFlowManagerStatusChanged）。</summary>
        private void OnFlowManagerStatusChanged(object sender, EventArgs e)
        {
            if (IsDisposed || !IsHandleCreated) return;
            BeginInvoke(new System.Action(RefreshFlowStatTable));
        }

        #region 权限（访客低权限 / 管理员解锁）

        /// <summary>
        /// 按当前登录身份统一刷全部权限位（唯一入口，改权限只改这里）。
        /// <para>访客（未登录）：只有「日志查看」可用，其余工具栏动作与下拉全部禁用。</para>
        /// <para>管理员：全部解锁，运行控制按钮再由 <see cref="UpdateRunButtons"/> 按运行三态细分。</para>
        /// <para>本方法内部会调 <see cref="UpdateRunButtons"/>，因为运行按钮的可用态必须与权限位相与。</para>
        /// </summary>
        private void ApplyPermissions()
        {
            _fullAccess = AppContext.HasFullAccess;

            // ---- 需要全权限：采集 / 相机 / 流程 / 通讯 / 配方 / 设置 ----
            tsBtnRefresh.Enabled = _fullAccess;
            tsBtnGrabOnce.Enabled = _fullAccess;
            tsBtnLive.Enabled = _fullAccess;
            tsBtnSyncGrab.Enabled = _fullAccess;
            tsBtnCameraMgr.Enabled = _fullAccess;
            tsBtnFlow.Enabled = _fullAccess;
            // 「相机标定」与「流程编排」同权：改的是定位/换算参数，访客一律不可用。
            // 【必须每次刷新都重设】本方法是权限的唯一入口，漏一处会被 UpdateStatus 静默打回。
            tsBtnCalib.Enabled = _fullAccess;
            tsBtnComm.Enabled = _fullAccess;
            tsBtnRecipe.Enabled = _fullAccess;
            tsBtnSettings.Enabled = _fullAccess;
            tsComboCameras.Enabled = _fullAccess;
            tsComboFlow.Enabled = _fullAccess;

            // 各视图上的「绑定相机」下拉：属于改配置（切换即 ConfigStore.Save），访客不可动。
            // 注意 BuildViews 之后新建的 combo 由 RefreshBindCombos 再吃一次权限位。
            for (int i = 0; i < _bindCombos.Count; i++)
            {
                if (_bindCombos[i] != null) _bindCombos[i].Enabled = _fullAccess;
            }

            // ---- 始终可用：日志（低权限唯一入口）、登录、右栏折叠、布局下拉、各分组标签 ----
            // tsBtnAlgo 保持 Visible=false 不动（隐藏项不参与权限判定）
            tsBtnLog.Enabled = true;
            tsBtnLogin.Enabled = true;
            tsBtnRightPanel.Enabled = true;
            tsComboLayout.Enabled = true;
            tsLabelCamera.Enabled = true;
            tsLabelLayout.Enabled = true;
            tsLabelFlow.Enabled = true;
            tsLabelStatus.Enabled = true;

            tsBtnLogin.Text = _fullAccess
                ? ("注销 " + (AppContext.CurrentUser != null ? AppContext.CurrentUser.Name : ""))
                : "登录";
            tsBtnLogin.ToolTipText = _fullAccess
                ? "注销并恢复访客低权限（仅可查看日志）"
                : "管理员登录后解锁全部功能（未登录仅可查看日志）";

            // 运行控制按钮必须一并与权限位同步，否则 UpdateStatus() 每次刷新都会把它们打回启用
            UpdateRunButtons();
        }

        /// <summary>
        /// 工具栏「登录 / 注销」：
        /// 访客态点击 → 弹登录框，<b>只有管理员</b>校验通过才把当前用户换成管理员并解锁；
        /// 已是全权限 → 点击即注销，恢复访客低权限（只读日志）。
        /// </summary>
        private void OnLoginButton(object sender, EventArgs e)
        {
            if (!AppContext.HasFullAccess)
            {
                using (var lf = new LoginForm())
                {
                    if (lf.ShowDialog(this) == DialogResult.OK)
                    {
                        if (lf.User != null && lf.User.IsAdmin)
                        {
                            AppContext.CurrentUser = lf.User;
                            Logger.Info("管理员登录：" + lf.User.Name);
                        }
                        else
                        {
                            // 内置 operator 账号能通过密码校验，但不是管理员：明确拒绝，不保留该身份
                            Logger.Warn("非管理员账号登录被拒绝："
                                        + (lf.User != null ? lf.User.Name : "(空)"));
                            MessageBox.Show(this,
                                "该账号无操作权限，仅管理员可解锁全部功能。",
                                "登录", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                    }
                }
            }
            else
            {
                AppContext.CurrentUser = new Models.User
                {
                    Name = "访客",
                    Role = "Guest",
                    LoginTime = DateTime.Now
                };
                Logger.Info("已注销，恢复访客低权限");
            }

            ApplyPermissions();
            UpdateStatus();
        }

        #endregion

        /// <summary>
        /// 刷新状态栏 / 工具栏状态位 / 右栏运行状态区，并同步运行控制按钮的可用态
        /// （单一状态源：工具栏与右栏大按钮共用 <see cref="UpdateRunButtons"/>）。
        /// </summary>
        private void UpdateStatus()
        {
            string user = AppContext.CurrentUser != null ? AppContext.CurrentUser.ToString() : "未登录";
            RecipeModel recipe = RecipeStore.Current;
            CameraInfo cam = SelectedCameraInfo;

            string runState;
            if (!_running) runState = "已停止";
            else if (_paused) runState = "已暂停";
            else if (IsTriggeredRunMode)
            {
                // 触发式：没有槽在跑就是「等触发」；有槽在跑说明本轮正在执行。
                // 文案写全：现场曾把"等待触发时画面不动"误判成"图像采集控件坏了"。
                runState = FlowRunManager.ExecutingCount > 0
                    ? "运行中（触发）"
                    : "等待触发（等 PLC 信号，画面不动是正常的）";
            }
            else runState = "运行中";

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
        /// <para>每个可用态都与权限位 <see cref="_fullAccess"/> 相与：访客（未登录）一律不可用。
        /// 这一步<b>不能</b>漏，因为 <see cref="UpdateStatus"/> 每次刷新都会调本方法。</para>
        /// </summary>
        private void UpdateRunButtons()
        {
            bool stopped = !_running;
            bool running = _running && !_paused;
            bool paused = _running && _paused;

            // 右栏大按钮（三态互斥 + 权限位）
            UiTheme.SetButtonEnabled(btnLoop, _fullAccess && stopped, UiButtonKind.Start);
            UiTheme.SetButtonEnabled(btnPause, _fullAccess && !stopped, UiButtonKind.Pause);
            UiTheme.SetButtonEnabled(btnStop, _fullAccess && !stopped, UiButtonKind.Stop);
            UiTheme.SetButtonEnabled(btnGrabOnce, _fullAccess && (stopped || paused), UiButtonKind.Grab);
            UiTheme.SetButtonEnabled(btnResetStats, _fullAccess && stopped, UiButtonKind.Reset);

            // 「暂停」在暂停态显示「继续」：颜色语义不变（橙），仅在文字上区分
            if (btnPause != null && btnPause.Enabled) btnPause.Text = paused ? "继续" : "暂停";

            // 工具栏不再重复放「启动 / 暂停」（用户要求：运行控制只在右栏），
            // 这里只同步工具栏保留的「单帧采集」可用态（同样与权限位相与）。
            tsBtnGrabOnce.Enabled = _fullAccess && (stopped || paused);

            // 连续预览开关：文字随运行态切换（连续预览 / 停止预览）
            UpdateLiveButton();
        }

        /// <summary>
        /// 设置主徽标三态（FR-UI-03）：OK 绿 / NG 红 / "--" 灰，结论保持到下次判定。
        /// 同时维护连续 NG 计数与橙色警示条（FR-UI-05）。
        /// </summary>
        private void SetVerdict(bool? passed)
        {
            // 结论没变就不要重新配一次徽标（配色/字体/内边距都写一遍 → 控件重绘）。
            // 连续运行时同一结论会连着几千帧，跳过这一下等于省掉几千次无关重绘。
            bool changed = !Nullable.Equals(_verdict, passed);
            _verdict = passed;

            if (!passed.HasValue)
            {
                if (changed) UiTheme.StyleBadge(lblVerdict, UiVerdict.None);
            }
            else if (passed.Value)
            {
                if (changed) UiTheme.StyleBadge(lblVerdict, UiVerdict.Ok);
                _consecutiveNg = 0;
            }
            else
            {
                if (changed) UiTheme.StyleBadge(lblVerdict, UiVerdict.Ng);
                _consecutiveNg++;
            }

            // 连续 NG 警示条：纯文本、橙底、无弹窗不阻塞
            bool alarm = _consecutiveNg >= ConsecutiveNgAlarm;
            lblConsecutiveNg.Visible = alarm;
            if (alarm) lblConsecutiveNg.Text = "连续 NG：" + _consecutiveNg;
        }

        private void OnEntryLogged(object sender, LogEntry entry)
        {
            if (IsDisposed || !IsHandleCreated || entry == null) return;

            // 仅入缓冲，由 UI 线程的 _logTimer 批量拉取（见 OnLogTimerTick）。
            // 不在后台线程逐行 BeginInvoke —— 并行运行下 RESULT 行可达 90 行/秒，逐条投递会淹没 UI 线程。
            lock (_logSync)
            {
                _logBuffer.Add(entry);
            }
        }

        /// <summary>主界面日志过滤下拉（只影响显示；文件与 SQLite 日志仍记录全部）。</summary>
        private void InitLogFilter()
        {
            _tsLogFilter = new ToolStripComboBox();
            _tsLogFilter.AutoSize = false;
            _tsLogFilter.Width = 118;
            _tsLogFilter.DropDownStyle = ComboBoxStyle.DropDownList;
            _tsLogFilter.BackColor = Color.FromArgb(30, 30, 38);
            _tsLogFilter.ForeColor = Color.Gainsboro;
            _tsLogFilter.ToolTipText = "只影响主界面显示；完整日志仍写入文件与日志库";
            _tsLogFilter.Items.AddRange(new object[]
            {
                "日志：全部", "日志：结果", "日志：仅异常", "日志：实拍", "日志：模拟"
            });
            _tsLogFilter.SelectedIndex = LogFilterResult;
            _tsLogFilter.SelectedIndexChanged += delegate { RebuildLogList(); };

            toolStrip.Items.Add(new ToolStripSeparator());
            toolStrip.Items.Add(new ToolStripLabel("日志"));
            toolStrip.Items.Add(_tsLogFilter);
        }

        private int CurrentLogFilter
        {
            get { return _tsLogFilter != null ? _tsLogFilter.SelectedIndex : LogFilterResult; }
        }

        private bool IsSimulatedEntry(LogEntry e)
        {
            if (e == null) return false;

            string flow = e.FlowName ?? "";
            if (flow.StartsWith("[模拟]", StringComparison.Ordinal)) return true;
            if (flow.StartsWith("[实拍]", StringComparison.Ordinal)) return false;

            string msg = e.Message ?? "";
            return msg.IndexOf("SIM-0", StringComparison.OrdinalIgnoreCase) >= 0
                   || msg.IndexOf("模拟", StringComparison.Ordinal) >= 0
                   || msg.IndexOf("本地图片", StringComparison.Ordinal) >= 0;
        }

        private bool MatchLogFilter(LogEntry e)
        {
            if (e == null) return false;

            switch (CurrentLogFilter)
            {
                case LogFilterAll:
                    return true;

                case LogFilterError:
                    return e.Level == LogLevel.Warn || e.Level == LogLevel.Error || e.Level == LogLevel.ResultNg;

                case LogFilterReal:
                    return !IsSimulatedEntry(e);

                case LogFilterSim:
                    return IsSimulatedEntry(e);

                default: // Result：隐藏普通 INFO，保留 OK/NG/WARN/ERROR
                    if (e.Level != LogLevel.Info) return true;
                    // 兼容主界面旧的「视图N 检测 OK/NG」INFO 行
                    string msg = e.Message ?? "";
                    return msg.IndexOf("检测", StringComparison.Ordinal) >= 0
                           && (msg.IndexOf("OK", StringComparison.Ordinal) >= 0
                               || msg.IndexOf("NG", StringComparison.Ordinal) >= 0);
            }
        }

        /// <summary>
        /// 日志批量拉取（UI 线程，200ms 一次）：把缓冲中的日志行一次性追加到 listLog 并裁剪到上限，
        /// 把并行运行下 90+ 行/秒的日志洪流从「逐行 BeginInvoke」压成「每秒约 5 次批量追加」。
        /// </summary>
        /// <summary>切换过滤后立即用历史重绘（不改变已落盘日志）。</summary>
        private void RebuildLogList()
        {
            if (IsDisposed || !IsHandleCreated) return;

            try
            {
                listLog.BeginUpdate();
                listLog.Items.Clear();
                _logMaxWidth = 0;
                listLog.HorizontalExtent = 0;

                int start = Math.Max(0, _logHistory.Count - 2000);
                for (int i = start; i < _logHistory.Count; i++)
                {
                    LogEntry e = _logHistory[i];
                    if (e == null || !MatchLogFilter(e)) continue;
                    string rebuilt = e.Format();
                    listLog.Items.Add(rebuilt);
                    GrowLogHorizontalExtent(rebuilt);
                    while (listLog.Items.Count > UiTheme.LogMaxLines) listLog.Items.RemoveAt(0);
                }

                listLog.EndUpdate();
                if (listLog.Items.Count > 0) listLog.TopIndex = listLog.Items.Count - 1;
            }
            catch (Exception) { }
        }

        /// <summary>
        /// UI 刷新计数探针（仅在环境变量 <c>VA_UI_STATS=1</c> 时输出，默认不产生任何日志）。
        /// <para>连续运行时"CPU 高"最常见的来源不是算法，而是界面被无节制地重刷。
        /// 打开后每 5 秒落一条：帧回调 = 流程回传的显示帧数；状态栏刷新 = 真正执行
        /// <see cref="UpdateStatus"/> 的次数（应远小于帧数）；日志行 = 追加到 listLog 的行数。
        /// 三个数字一对比就知道是谁在刷。</para>
        /// </summary>
        private void ReportUiRefreshStats()
        {
            if (!_uiStatsProbe) return;

            int now = Environment.TickCount;
            if (_uiStatTickBase == 0) { _uiStatTickBase = now; return; }

            int elapsed = unchecked(now - _uiStatTickBase);
            if (elapsed < 5000) return;

            int frames = _uiFrameCount;
            int status = _uiStatusRefreshCount;
            int stats = _uiStatsRefreshCount;

            _uiFrameCount = 0;
            _uiStatusRefreshCount = 0;
            _uiStatsRefreshCount = 0;
            _uiStatTickBase = now;

            double sec = elapsed / 1000.0;
            Logger.Info(string.Format(
                "[UI统计] {0:F1}s 内：帧显示 {1} 次（{2:F1}/s）｜状态栏刷新 {3} 次（{4:F1}/s）｜统计卡片 {5} 次｜日志行 {6} 行",
                sec, frames, frames / sec, status, status / sec, stats, listLog.Items.Count));
        }

        /// <summary>
        /// 按最长一行维护横向滚动范围。
        /// <para>【为什么需要】ListBox 单行不换行，长日志（整串节点摘要 / 完整路径）会被控件宽度直接裁掉，
        /// 现场只能看到前半截。只增不减，避免每批日志都全量重测。</para>
        /// </summary>
        private void GrowLogHorizontalExtent(string line)
        {
            if (string.IsNullOrEmpty(line)) return;

            int width;
            try { width = TextRenderer.MeasureText(line, UiTheme.FontMono).Width; }
            catch (Exception) { return; }

            if (width + 16 <= _logMaxWidth) return;

            _logMaxWidth = width + 16;
            listLog.HorizontalExtent = _logMaxWidth;
        }

        /// <summary>双击日志行：弹窗显示完整内容（可选中复制）。</summary>
        private void OnLogDoubleClick(object sender, EventArgs e)
        {
            int i = listLog.SelectedIndex;
            if (i < 0 || i >= listLog.Items.Count) return;

            string text = listLog.Items[i] as string ?? "";
            if (text.Length == 0) return;

            ShowTextDialog("日志详情", text);
        }

        /// <summary>只读文本弹窗（深色主题、等宽字体、支持打断行查看与复制）。</summary>
        private void ShowTextDialog(string title, string text)
        {
            try
            {
                using (var f = new Form())
                {
                    f.Text = title;
                    f.StartPosition = FormStartPosition.CenterParent;
                    f.Size = new Size(940, 300);
                    f.MinimizeBox = false;
                    f.ShowInTaskbar = false;
                    f.BackColor = UiTheme.ColorAppBg;
                    f.ForeColor = UiTheme.ColorTextPrimary;

                    var box = new TextBox
                    {
                        Multiline = true,
                        ReadOnly = true,
                        WordWrap = false,
                        ScrollBars = ScrollBars.Both,
                        Dock = DockStyle.Fill,
                        Font = UiTheme.FontMono,
                        Text = text.Replace("\r\n", " ").Replace('\n', ' '),
                        BackColor = UiTheme.ColorViewBg,
                        ForeColor = UiTheme.ColorTextPrimary,
                        BorderStyle = BorderStyle.FixedSingle
                    };

                    f.Controls.Add(box);
                    UiStyle.ApplyForm(f);
                    f.ShowDialog(this);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("打开日志详情失败：" + ex.Message);
            }
        }

        private void OnLogTimerTick(object sender, EventArgs e)
        {            if (IsDisposed || !IsHandleCreated) return;

            ReportUiRefreshStats();

            List<LogEntry> batch;
            lock (_logSync)
            {
                if (_logBuffer.Count == 0) return;
                batch = _logBuffer;
                _logBuffer = new List<LogEntry>();

                _logHistory.AddRange(batch);
                int overflow = _logHistory.Count - 2000;
                if (overflow > 0) _logHistory.RemoveRange(0, overflow);
            }

            try
            {
                listLog.BeginUpdate();
                foreach (LogEntry entry in batch)
                {
                    if (!MatchLogFilter(entry)) continue;

                    string line = entry.Format();
                    listLog.Items.Add(line);
                    GrowLogHorizontalExtent(line);
                    // FR-UI-09：日志上限 500 行，超出移除最旧一行（控制 5 分钟连续运行的内存增长）
                    while (listLog.Items.Count > UiTheme.LogMaxLines) listLog.Items.RemoveAt(0);
                }
                listLog.EndUpdate();

                if (listLog.Items.Count > 0) listLog.TopIndex = listLog.Items.Count - 1;
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
            // 主界面没走 UiStyle.ApplyForm（手写 ApplyTheme），数值框策略要在这里单独补上。
            // ApplyTree 幂等，构造与主题切换重复调用无副作用。
            NumericInputPolicy.ApplyTree(this);

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

            // 性能归因：算法侧的产出速率与单帧耗时（探针关闭时是一次空判断）
            PerfProbe.CountFrame(e.ElapsedMs);

            // 每收到一条流程输出即计入对应流程的 OK/NG（独立于下方帧显示节流，保证计数精确，
            // 且合帧跳过显示时数字仍实时累加）
            RecordFlowVerdict(e.FlowId, e.FlowName, e.Passed);

            // 【耗时必须在这里更新】_lastElapsedMs 原来只在旧的 RunInspection（配方检测链路）
            // 里赋值；走流程并行槽之后没有任何地方写它，于是状态栏「耗时」永远是 "--"。
            _lastElapsedMs = e.ElapsedMs;
            _statsDirty = true;

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
                if (e.DisplayFrame != null) e.DisplayFrame.Dispose();
                return;
            }

            if (IsDisposed || !IsHandleCreated)
            {
                if (e.Frame != null) e.Frame.Dispose();
                if (e.DisplayFrame != null) e.DisplayFrame.Dispose();
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
                if (slot.DisplayFrame != null) slot.DisplayFrame.Dispose();

                // 显示用合成帧（深度学习掩膜/伪彩）；没有就用原图帧。
                // 两者都接管所有权：Frame 是"输出图像"，DisplayFrame 只用于看。
                slot.DisplayFrame = e.DisplayFrame;
                slot.Frame = e.Frame;            // 取得帧所有权（原事件持有）
                e.DisplayFrame = null;
                slot.Shapes = e.Shapes;
                slot.Cloud = e.Cloud;            // 引用即可（托管数据）
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
                    Bitmap displayFrame;
                    List<IGeometryShape> shapes;
                    VisionSystem.Controls.PointCloud.PointCloudData cloud;
                    bool passed;
                    string summary;
                    string cameraName;
                    // 所有权标记：交给视图的那一份不能在 finally 里再释放（否则显示中的位图被销毁）
                    bool frameHanded = false;
                    bool displayFrameHanded = false;

                    lock (_pendingOutputsSync)
                    {
                        if (!_pendingOutputs.TryGetValue(idx, out s) || s == null) return;
                        System.Threading.Interlocked.Exchange(ref s.Posted, 0);   // 允许下次投递
                        // 取出最新快照后释放锁，再操作 UI
                        frame = s.Frame;
                        displayFrame = s.DisplayFrame;
                        cloud = s.Cloud;
                        s.Cloud = null;
                        shapes = s.Shapes;
                        passed = s.Passed;
                        summary = s.Summary;
                        cameraName = s.CameraName;
                        s.Frame = null;   // 取出，下方交由视图或释放处理
                        s.DisplayFrame = null;
                        s.Shapes = null;
                    }

                    if (IsDisposed || !IsHandleCreated)
                    {
                        if (frame != null) frame.Dispose();
                        if (displayFrame != null) displayFrame.Dispose();
                        return;
                    }

                    // 显示节流（默认 15fps）：中间帧直接丢掉，只保留"最新画面"。
                    // 算法按自己的节拍跑，画面不需要跟到 30fps —— 每渲染一帧就是一次
                    // 5MP 重采样（约 14ms 纯 CPU），多视图并行时这就是主要占用。
                    int tickNow = Environment.TickCount;
                    if (s.LastRenderTick != 0
                        && unchecked(tickNow - s.LastRenderTick) < MinRenderIntervalMs)
                    {
                        if (frame != null) frame.Dispose();
                        if (displayFrame != null) displayFrame.Dispose();
                        return;
                    }
                    s.LastRenderTick = tickNow;

                    try
                    {
                        ImageViewer view = _views[idx];
                        // 3D：本周期产出过点云就进 3D 模式（与 2D 共用查看器），位图帧走常规释放
                        if (cloud != null && cloud.Count > 0)
                        {
                            view.SetPointCloud(cloud);
                        }
                        // 有合成帧就显示它（掩膜/伪彩），否则显示原图帧。
                        // 【不再 Clone】视图自身持有所有权（详见 ImageViewer.OwnsImage），
                        // 原先每帧先克隆一份 5MP 位图（15MB/帧）纯属白拷贝 —— 30fps 下约 450MB/s 的无用流量。
                        // 两张帧谁被交给视图就由视图负责释放，剩下的在 finally 里放掉（不能两个都放）。
                        else if (displayFrame != null)
                        {
                            view.SetImage(displayFrame, true);
                            displayFrameHanded = true;
                        }
                        else if (frame != null)
                        {
                            view.SetImage(frame, true);
                            frameHanded = true;
                        }

                        // 标题条文案：只在真的变化时赋值（Label.Text 每次都写会触发一次重绘）
                        if (idx < _captions.Count && _captions[idx] != null)
                        {
                            string caption = "  " + (string.IsNullOrEmpty(cameraName) ? "流程结果" : cameraName)
                                             + " · " + ActiveFlowName;
                            if (_captions[idx].Text != caption) _captions[idx].Text = caption;
                        }

                        // 流程结论同样驱动右栏徽标、视图角标与状态栏（与配方检测链路同一套口径）
                        _lastSummary = summary ?? "";
                        SetVerdict(passed);
                        SetViewVerdict(idx, passed);

                        // 状态栏 / 统计卡片合并到 5Hz 刷新（见 StatusUiMinIntervalMs 的说明）：
                        // 每帧都刷两行长文本 + 按钮态是连续运行 CPU 偏高的固定开销之一，
                        // 而人眼对 30fps 的数字跳动并没有需求。计数本身仍然按件精确累加。
                        _uiFrameCount++;
                        if (_statsDirty
                            && unchecked(Environment.TickCount - _lastStatusUiTick) >= StatusUiMinIntervalMs)
                        {
                            _lastStatusUiTick = Environment.TickCount;
                            _statsDirty = false;
                            _uiStatsRefreshCount++;
                            _uiStatusRefreshCount++;
                            UpdateStats();
                            UpdateStatus();
                        }

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
                        // 交给视图的那一份由视图负责释放（换图/销毁时），这里只放没交出去的
                        if (!frameHanded && frame != null) frame.Dispose();
                        if (!displayFrameHanded && displayFrame != null) displayFrame.Dispose();
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

        /// <summary>
        /// 下拉框选中流程：<strong>只做准备，不再自动运行</strong>。
        /// <para>
        /// 原实现"一选中就弹选图对话框并运行"，用「本地图片文件夹」做调试时每次换流程都被拦一次；
        /// 现在由「单步执行」按钮触发（见 <see cref="OnStepRunSelectedFlow"/>）。
        /// </para>
        /// </summary>
        private void OnFlowComboSelected(object sender, EventArgs e)
        {
            if (_loadingFlowCombo) return;

            FlowDefinition selected = tsComboFlow.SelectedItem as FlowDefinition;
            if (selected != null)
                tsLabelStatus.Text = "已选中流程：" + (selected.Name ?? "") + "（点「单步执行」运行一次）";
        }

        /// <summary>
        /// 「单步执行」：按上方「流程」下拉框选中的流程执行<strong>一次</strong>（取图 + 跑完整链）。
        /// <para>
        /// 取代原来的「单帧采集」：以前只能对相机抓一帧再跑，现在直接按流程配置取图 ——
        /// 图像源是「本地图片文件夹」时，每点一次自动换下一张，天然就是逐张调试。
        /// </para>
        /// <para>未选流程必须明确提示，不能静默什么都不做。</para>
        /// </summary>
        private void OnStepRunSelectedFlow(object sender, EventArgs e)
        {
            FlowDefinition selected = tsComboFlow.SelectedItem as FlowDefinition;
            if (selected == null)
            {
                MessageBox.Show(this,
                    "请先在上方「流程」下拉框选择要执行的流程，再点「单步执行」。",
                    "未选择流程", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                tsComboFlow.Focus();
                return;
            }

            RunSelectedFlow(false);
        }

        /// <summary>
        /// 运行下拉框当前选中的流程：选一张本地图片 → 把克隆里<strong>所有</strong>图像源节点
        /// 改为该图片（保证无需相机）→ 校验 → 经本窗体自有的 FlowRunController 以 camera=null 运行；
        /// 结果经 FlowOutputChannel 既有链路显示到主视图并驱动 OK/NG 徽标。
        /// </summary>
        /// <summary>兼容旧调用：等价于「运行」。</summary>
        private void RunSelectedFlow()
        {
            RunSelectedFlow(false);
        }

        /// <summary>
        /// 运行 / 单步执行下拉框选中的流程。
        /// <para>
        /// <strong>图像源已是「本地文件 / 文件夹」时不再弹选图对话框</strong>，直接按流程配置跑 ——
        /// 文件夹模式会按「每张停留(ms)」自动换图，单步/运行都能一张一张看效果。
        /// 只有流程确实需要相机时，才回落到"选一张本地图片代替相机"的旧行为。
        /// </para>
        /// </summary>
        /// <param name="step">true = 单步（每点一次一个节点）；false = 整链运行一次。</param>
        private void RunSelectedFlow(bool step)
        {
            if (_flowRunner == null) { MessageBox.Show(this, "算法库不可用，无法运行流程。"); return; }
            if (_flowCtrl == null || _flowCtrl.IsBusy) return;

            FlowDefinition selected = tsComboFlow.SelectedItem as FlowDefinition;
            if (selected == null) { tsLabelStatus.Text = "请先在下拉框选择一个流程"; return; }

            // 坑④：绝不改 ConfigStore 里的原定义——直接改会把 FlowForm 里配置的图片路径持久化覆盖，
            // 且可能与并行运行的流程并发改参数。统一跑克隆。
            FlowDefinition clone = selected.Clone();

            if (FlowRunner.NeedsCamera(clone))
            {
                if (step)
                {
                    MessageBox.Show(this,
                        "该流程的图像源是「流程绑定相机」，单步调试请把图像源改为「本地图片文件」或「本地图片文件夹」。",
                        "单步不支持相机取图", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                // 旧行为兜底：选一张本地图片顶替相机
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

                if (FlowRunner.NeedsCamera(clone))
                {
                    MessageBox.Show(this, "该流程仍需相机（存在未改为本地图片的图像源）。", "无法运行",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
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
            Logger.Info("主界面" + (step ? "单步" : "运行") + "流程：" + (selected.Name ?? ""));

            if (step) _flowCtrl.StepOnce(clone, null);   // 图像源为文件/文件夹 → 不需要相机
            else _flowCtrl.RunOnce(clone, null);
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
                    if (!IsDisposed && tsComboFlow != null) tsComboFlow.Enabled = _fullAccess;
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
            DetachStaticEvents();
            CameraManager.Instance.CloseAll();
        }

        /// <summary>
        /// 退订本窗体挂到<strong>静态</strong>事件上的全部处理器。
        /// <para>两个调用点：<see cref="OnFormClosing"/>（正常 Close）与 <see cref="OnFormDisposed"/>
        /// （程序化 Dispose，不走 Close）。<b>两处都会被调到，方法必须幂等</b>——
        /// 事件 <c>-=</c> 对不存在的处理器是安全空操作，重复调用无副作用。</para>
        /// <para>【为什么不写在 Designer 的 Dispose(bool) 里】那里是 VS 生成的，重新序列化会丢手写内容。</para>
        /// </summary>
        private void DetachStaticEvents()
        {
            Logger.EntryLogged -= OnEntryLogged;
            RecipeStore.CurrentChanged -= OnRecipeChanged;
            CameraManager.Instance.CurrentChanged -= OnCameraManagerChanged;
            ConfigStore.Changed -= OnConfigChanged;
            FlowOutputChannel.OutputReady -= OnFlowOutput;
            FlowRunManager.StatusChanged -= OnFlowManagerStatusChanged;
        }

        /// <summary>
        /// 窗体被 Dispose（未走 Close，如程序化释放 / 异常兜底）时也要退订静态事件。
        /// <para>【勿删】只在 OnFormClosing 退订是不够的：静态事件（ConfigStore.Changed 等）
        /// 持有本窗体实例，Dispose 后订阅者依然存活，之后每次 ConfigStore.Save() 都会
        /// 回调到已释放控件的处理器上抛 NullReferenceException。</para>
        /// </summary>
        private void OnFormDisposed(object sender, EventArgs e)
        {
            DetachStaticEvents();
        }
    }
}
