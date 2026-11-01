using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using VisionPlatform.Camera;
using VisionPlatform.Core;
using VisionPlatform.Flow;
using VisionSystem.Controls;
using VisionSystem.Controls.Geometry;

namespace VisionPlatform.UI
{
    // DrawShapeKind / DrawSpec 已移至 FlowDrawSpec.cs——VS 要求「可设计类必须是文件中的第一个类型」，
    // 这两个类型原先排在 FlowForm 之前会导致设计器无法打开本窗体。

    /// <summary>
    /// 流程编排页：工具箱拖拽源 / 画布 / 节点参数面板 / 底部流程管理与绑定。
    /// 所有编辑即时写回 <see cref="ConfigStore"/>；运行单链流程并把结果经
    /// <see cref="FlowOutputChannel"/> 回传主界面。
    /// </summary>
    public partial class FlowForm : Form
    {
        private readonly FlowRunner _runner;
        private FlowRunController _controller;   // 单帧 / 单步 / 连续运行的线程编排

        private bool _loading;          // 界面回填期间抑制事件
        private bool _running;
        private volatile bool _cancel;
        private Thread _runThread;      // 当前运行线程（关闭时需 Join，避免与 _runner.Dispose 并发）
        private bool _closeWhenIdle;    // 关窗时运行未结束：先取消关闭，待运行收尾后自动关闭
        private bool _looping;          // 连续运行进行中（btnRunLoop 文案在「连续运行 / 停止连续」间切换）

        /// <summary>画 ROI / 取消后叠加图形被清除时的统一提示语（必须让用户知道不是结果丢了）。</summary>
        private const string OverlayClearedHint = "叠加图形已清除，重新运行可再次显示。";

        private FlowNode _selectedNode; // 画布当前选中节点（画 ROI / 回填图像源都优先用它）
        private DrawSpec _drawSpec;     // 本次正在画的是哪一组几何（多组并存时由用户选）

        // 右栏「输出结果」面板：纯代码构建、Dock=Bottom，放在「编辑输入参数」下方。
        // 刻意不改 FlowForm.Designer.cs——本项目历史上出现过 VS 重序列化把事件订阅弄丢的事故，
        // 这里走运行时 Controls.Add，既有订阅零风险。
        private FlowResultPanel resultPanel;

        /// <summary>
        /// 每个节点上次画的是哪一组 ROI：key = 节点 Id，value = 该组的 X 参数键（组标识）。
        /// 必须带节点身份，否则切到别的节点后会拿旧组去查新节点的组，可能查错组甚至画错参数。
        /// </summary>
        private readonly Dictionary<string, string> _roiLastGroup = new Dictionary<string, string>();

        // 模板状态卡片（ShapeMatch / TemplateMatch 才有）：
        //   _templateCardNodeId 记录"卡片当前绑的是哪个节点"，只有节点变了才 Dispose 重建。
        //   参数面板在旋转 ROI 编辑态下每次松手都会 SetNode，无条件重建 = 拖一次手柄漏一张缩略图。
        private TemplateStatusCard _templateCard;
        private string _templateCardNodeId;

        // OCR 的字符模板库卡片（数据源是整目录扫描，与匹配类的单模板资产完全不同）
        private OcrTemplateCard _ocrCard;
        private string _ocrCardNodeId;

        // ---------------------------------------------------------------------
        //  右栏结构（UI-R01）：全部代码构建，**不动 FlowForm.Designer.cs**
        //  该工程有"VS 重序列化吃掉事件订阅"的事故史（DEF-01 就是这么坏的两个按钮），
        //  所以设计器里的 nodeParamPanel 在构造期
        //  从 panelRight 摘下来，重新装进这里新建的容器。
        // ---------------------------------------------------------------------
        /// <summary>上次已落盘的示教基准键（节点 Id + 三个值），用于去重，避免每帧写盘。</summary>
        private string _baselineSavedKey = "";

        private Panel panelRightSplit;      // 右栏左缘的竖直分隔条（可拖宽右栏）
        private bool _draggingRightSplit;
        private int _dragStartX;
        private int _dragStartWidth;
        private Panel panelRightTop;        // 上：参数标题 + 端子标题 + 端子树 + 参数面板(Fill)
        private Panel panelSplitter;        // 中：6px 可拖动分隔条
        private int _resultHeightLogical;      // 结果区高度（96-DPI 逻辑像素，见 UiSettings）
        private bool _draggingSplitter;
        private int _dragStartScreenY;
        private int _dragStartHeight;

        public FlowForm()
        {
            InitializeComponent();

            // 工具箱改 TreeView 后拖拽走 ItemDrag：分类节点 Tag=null 不可拖，
            // 仍只传 FlowModuleInfo.Key 字符串，FlowCanvasControl.OnDragDrop 零改动。
            treeModules.ItemDrag += OnModuleItemDrag;
            lstFlows.SelectedIndexChanged += OnFlowSelectionChanged;
            cmbCamera.SelectedIndexChanged += OnCameraBindingChanged;
            // 相机管理里打开/关闭相机时自动刷新绑定下拉，无需手动点「刷新」
            CameraManager.Instance.CurrentChanged += OnCameraManagerChanged;

            canvas.NodeSelected += OnCanvasNodeSelected;
            canvas.NodeAdded += OnCanvasChanged;
            canvas.NodeMoved += OnCanvasChanged;
            canvas.LinkAdded += OnCanvasLinkChanged;
            canvas.LinkRemoved += OnCanvasLinkChanged;
            canvas.Changed += OnCanvasEdited;
            // 连线被拒 / 端子重连：画布只弹 2.8 秒 toast，会漏看，故同时进日志与状态条
            canvas.LinkRejected += OnLinkRejected;
            canvas.LinkReplaced += OnLinkReplaced;

            nodeParamPanel.ParamsChanged += OnParamsChanged;
            // 参数面板里的动作行（「创建模板…」等）只抛 Key，语义由本窗体解释
            nodeParamPanel.ActionInvoked += OnParamAction;

            try
            {
                _runner = new FlowRunner();
                _runner.OutputReady += OnRunnerOutput;

                _controller = new FlowRunController(_runner);
                _controller.ProgressReady += OnControllerProgress;
                _controller.OutputReady += OnRunnerOutput;
                _controller.Completed += OnControllerCompleted;
            }
            catch (Exception ex)
            {
                _runner = null;
                _controller = null;
                Logger.Error("流程执行器初始化失败（算法库不可用？）：" + ex.Message);
            }

            flowPreview.SetController(_controller);
            flowPreview.ImageLoaded += OnPreviewImageLoaded;
            lblRunStatus.Text = "就绪";

            // 统一深色工程配色（canvas / preview / nodeParamPanel 自绘，已跳过）。
            // ★ 必须在 InitModules 之前：UiTree.Apply 在此定稿树字体（雅黑 10pt），
            //   InitModules 的分类加粗 NodeFont 从 treeModules.Font 派生——若树字体此时
            //   还是设计器里的宋体 8pt，分类节点会渲染成宋体 8pt、叶子是雅黑 10pt，两级不一致。
            UiStyle.ApplyForm(this);

            // 输出结果面板 + 右栏结构重整：见 BuildRightColumn 的注释
            // （放在 UiStyle.ApplyForm 之后：本面板自建配色是唯一来源，
            //   不希望再被通用遍历覆盖掉语义色）
            BuildRightColumn();

            InitModules();
            RefreshPortTree(null);
            LoadFlows();

            // 语义色：运行/连续运行=绿，单步=蓝，停止/删除=红；其余中性
            btnRun.Kind = UiButtonKind.Start;
            btnRunLoop.Kind = UiButtonKind.Start;
            btnStep.Kind = UiButtonKind.Grab;
            btnRunNode.Kind = UiButtonKind.Grab;
            btnStop.Kind = UiButtonKind.Stop;
            btnDeleteFlow.Kind = UiButtonKind.Stop;

            // 「画 ROI」按钮在预览区顶条（FlowPreviewPanel 内，与「载入图片」并排在其左侧）：
            // 它作用于预览图，放在预览区语义更贴切，也避免挤在「绑定与运行」分组里。
            // 画几何（矩形 ROI / 直线 / 圆环）统一走 ShapePicked；RoiPickRequested / RoiPickCancelled 保留兼容
            flowPreview.ShapePicked += OnShapePicked;
            flowPreview.RoiPickRequested += OnDrawRequested;
            flowPreview.RoiPickCancelled += OnRoiPickCancelled;

            // 自绘深色标题栏：与顶部工具栏同色，系统原生标题栏不再出现
            UiTitleBar.Attach(this);
        }

        #region 右栏结构（UI-R01 / R10）

        /// <summary>
        /// 重建右栏：参数区、端子区（可收起）、6px 可拖动分隔条、结果区（高度可记忆）。
        /// <para>
        /// 【为什么不用设计器】见字段注释：设计器重序列化会吃掉事件订阅。
        /// 这里把设计器里的四个子控件<strong>摘下来改名</strong>（<c>Controls.Clear</c> 不会 Dispose，对象仍在），
        /// 重新装进新容器；<c>FlowForm.Designer.cs</c> 一个字节都不动。
        /// </para>
        /// <para>
        /// 【停靠顺序】WinForms 按 Controls 集合<strong>逆序</strong>停靠：最后加的最先贴边。
        /// 所以添加顺序是 (Fill) → (Bottom 分隔条) → (Bottom 结果区)，
        /// 最终自上而下才是：参数/端子区 → 分隔条 → 结果区。
        /// </para>
        /// </summary>
        private void BuildRightColumn()
        {
            // 读偏好（老配置缺字段 → 归一化成默认值，见 UiSettings.Normalize*）
            _resultHeightLogical = UiSettings.NormalizeResultPanelHeight(
                ConfigStore.Current != null && ConfigStore.Current.Ui != null
                    ? ConfigStore.Current.Ui.FlowResultPanelHeight
                    : 0);
            // 摘下来（不 Dispose），重新装进新容器
            panelRight.Controls.Clear();

            panelRightTop = new Panel();
            panelRightTop.Dock = DockStyle.Fill;
            panelRightTop.Margin = new Padding(0);

            // 【右栏只放两件事：参数 + 结果】
            // 原来的「输入/输出端子」树（lblPorts + treePorts）已按用户要求整体去掉：
            // 它占掉右栏顶部 300+px，而参数区/结果区才是每天要看的东西。
            // 端子契约仍在 FlowPortSchema / FlowValidator 里生效，只是不再用树展示。
            // 参数面板自带标题（"节点参数　节点名"），外层不再重复一块"节点参数"
            panelRightTop.Controls.Add(nodeParamPanel);

            panelSplitter = new Panel();
            panelSplitter.Dock = DockStyle.Bottom;
            panelSplitter.Height = 6;
            panelSplitter.Cursor = Cursors.SizeNS;
            panelSplitter.MouseDown += OnSplitterMouseDown;
            panelSplitter.MouseMove += OnSplitterMouseMove;
            panelSplitter.MouseUp += OnSplitterMouseUp;

            resultPanel = new FlowResultPanel();
            resultPanel.Dock = DockStyle.Bottom;
            resultPanel.ShapeSelected += OnResultShapeSelected;
            // 缩略帧来源：预览区当前显示的图像（不转移所有权，内部只读）
            resultPanel.SetFrameProvider(() => flowPreview != null ? flowPreview.DisplayedImage : null);

            // 右栏左缘的竖直分隔条：拖它调右栏宽度（结果表格三列需要空间）
            panelRightSplit = new Panel();
            panelRightSplit.Dock = DockStyle.Left;
            panelRightSplit.Width = 5;
            panelRightSplit.Cursor = Cursors.SizeWE;
            panelRightSplit.MouseDown += OnRightSplitDown;
            panelRightSplit.MouseMove += OnRightSplitMove;
            panelRightSplit.MouseUp += OnRightSplitUp;

            panelRight.Controls.Add(panelRightTop);
            panelRight.Controls.Add(panelSplitter);
            panelRight.Controls.Add(resultPanel);
            panelRight.Controls.Add(panelRightSplit);   // 最后加 → 停靠在最左

            ApplyRightWidth();
            ApplyPortsState();
            ApplyResultHeight();

            // 窗口缩放时按逻辑值重算（150% DPI 下若存物理像素，换显示器就会"结果区突然变一半高"）
            Resize += (s, e) => ApplyResultHeight();
        }

        /// <summary>96-DPI 逻辑像素 → 物理像素。</summary>
        private int ScaleByDpi(int logical)
        {
            double scale = DeviceDpi <= 0 ? 1.0 : DeviceDpi / 96.0;
            return (int)Math.Round(logical * scale);
        }

        /// <summary>把记忆的结果区高度应用到控件（受"至少给参数区留 160px"约束）。</summary>
        private void ApplyResultHeight()
        {
            if (resultPanel == null || panelRight == null) return;

            int minPx = ScaleByDpi(UiSettings.MinResultPanelHeight);

            // 【默认 = 占右栏 1/3】用户要求"结果用表格显示、占比 1/3"。
            // 只有"没被用户拖过"（配置里还是默认值）时才按 1/3 算；拖过就尊重记忆值。
            // 默认占比 45%：参数表常常填不满（尤其选中简单节点时），把余量给结果表
            int px = (_resultHeightLogical == UiSettings.DefaultResultPanelHeight)
                ? Math.Max(ScaleByDpi(240), panelRight.Height * 45 / 100)
                : ScaleByDpi(UiSettings.NormalizeResultPanelHeight(_resultHeightLogical));

            int available = panelRight.Height - ScaleByDpi(160);
            if (available > 0 && px > available) px = available;
            if (px < minPx) px = minPx;

            resultPanel.Height = px;
        }

        /// <summary>
        /// 端子区的显隐（<strong>已废弃</strong>：右栏不再显示「输入/输出端子」树，见 BuildRightColumn）。
        /// 保留空实现只为兼容旧调用点；<c>FlowPortsState</c> 配置项仍照旧写入（老配置不改结构）。
        /// </summary>
        /// <summary>应用右栏宽度（逻辑值 → 物理像素，夹在 320~680 逻辑像素）。</summary>
        private void ApplyRightWidth()
        {
            if (panelRight == null) return;

            int logical = UiSettings.NormalizeRightWidth(
                ConfigStore.Current != null && ConfigStore.Current.Ui != null
                    ? ConfigStore.Current.Ui.FlowRightWidth
                    : 0);

            panelRight.Width = ScaleByDpi(logical);
        }

        private void OnRightSplitDown(object sender, MouseEventArgs e)
        {
            if (e == null || e.Button != MouseButtons.Left) return;
            _draggingRightSplit = true;
            _dragStartX = Control.MousePosition.X;
            _dragStartWidth = panelRight.Width;
        }

        private void OnRightSplitMove(object sender, MouseEventArgs e)
        {
            if (!_draggingRightSplit) return;

            // 向左拖 = 右栏变宽
            int dx = _dragStartX - Control.MousePosition.X;
            int w = _dragStartWidth + dx;

            int minPx = ScaleByDpi(UiSettings.MinRightWidth);
            int maxPx = ScaleByDpi(UiSettings.MaxRightWidth);
            if (w < minPx) w = minPx;
            if (w > maxPx) w = maxPx;

            panelRight.Width = w;
        }

        private void OnRightSplitUp(object sender, MouseEventArgs e)
        {
            if (!_draggingRightSplit) return;
            _draggingRightSplit = false;

            // 落盘（存逻辑值，换 DPI 不变形）
            try
            {
                AppConfig cfg = ConfigStore.Current;
                if (cfg != null && cfg.Ui != null)
                {
                    double scale = DeviceDpi <= 0 ? 1.0 : DeviceDpi / 96.0;
                    cfg.Ui.FlowRightWidth = UiSettings.NormalizeRightWidth(
                        (int)Math.Round(panelRight.Width / scale));
                    ConfigStore.Save(cfg);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("保存右栏宽度失败：" + ex.Message);
            }
        }

        private void ApplyPortsState()
        {
        }

        private void OnSplitterMouseDown(object sender, MouseEventArgs e)
        {
            if (e == null || e.Button != MouseButtons.Left) return;

            _draggingSplitter = true;
            _dragStartScreenY = Control.MousePosition.Y;
            _dragStartHeight = resultPanel.Height;
        }

        private void OnSplitterMouseMove(object sender, MouseEventArgs e)
        {
            if (!_draggingSplitter) return;

            // 向上拖 = 结果区变大
            int dy = _dragStartScreenY - Control.MousePosition.Y;
            int h = _dragStartHeight + dy;

            int minPx = ScaleByDpi(UiSettings.MinResultPanelHeight);
            int maxPx = ScaleByDpi(UiSettings.MaxResultPanelHeight);
            int available = panelRight.Height - ScaleByDpi(160);
            if (available > 0 && available < maxPx) maxPx = available;
            if (maxPx < minPx) maxPx = minPx;

            if (h < minPx) h = minPx;
            if (h > maxPx) h = maxPx;

            resultPanel.Height = h;
        }

        private void OnSplitterMouseUp(object sender, MouseEventArgs e)
        {
            if (!_draggingSplitter) return;
            _draggingSplitter = false;

            // 物理像素 → 逻辑值再存（跨 DPI 的稳定性靠这一步）
            double scale = DeviceDpi <= 0 ? 1.0 : DeviceDpi / 96.0;
            _resultHeightLogical = UiSettings.NormalizeResultPanelHeight(
                (int)Math.Round(resultPanel.Height / scale));

            SaveUiPrefs();
        }

        /// <summary>「放大 / 恢复」结果区（UI-R10）：放大前的高度要记着，恢复时才能回到原位。</summary>
        /// <summary>持久化右栏偏好（高度存逻辑值、端子区存三态；不碰其它字段）。</summary>
        private void SaveUiPrefs()
        {
            try
            {
                AppConfig cfg = ConfigStore.Current;
                if (cfg == null || cfg.Ui == null) return;

                cfg.Ui.FlowResultPanelHeight = UiSettings.NormalizeResultPanelHeight(_resultHeightLogical);
                // 端子树已移除：恒写"收起"，保留字段只为老配置结构不变
                cfg.Ui.FlowPortsState = UiSettings.PortsStateCollapsed;

                ConfigStore.Save();
            }
            catch (Exception ex)
            {
                Logger.Warn("保存右栏布局偏好失败：" + ex.Message);
            }
        }

        /// <summary>探针用：当前结果区高度（物理像素）。</summary>
        public int ResultPanelHeightPx { get { return resultPanel != null ? resultPanel.Height : 0; } }

        /// <summary>探针用：参数区当前可视高度（物理像素）。</summary>
        public int ParamsHeightPx { get { return nodeParamPanel != null ? nodeParamPanel.Height : 0; } }

        /// <summary>探针用：右栏当前宽度（物理像素）。</summary>
        public int RightColumnWidthPx { get { return panelRight != null ? panelRight.Width : 0; } }

        /// <summary>探针用：设置右栏宽度（逻辑像素，模拟拖分隔条松手）。</summary>
        public void SetRightWidthLogical(int logical)
        {
            if (panelRight == null) return;
            int v = UiSettings.NormalizeRightWidth(logical);
            panelRight.Width = ScaleByDpi(v);
            try
            {
                AppConfig cfg = ConfigStore.Current;
                if (cfg != null && cfg.Ui != null) { cfg.Ui.FlowRightWidth = v; ConfigStore.Save(cfg); }
            }
            catch (Exception) { }
        }

        /// <summary>探针用：右栏总高。</summary>
        public int RightColumnHeightPx { get { return panelRight != null ? panelRight.Height : 0; } }

        /// <summary>探针用：结果区高度记忆值（逻辑像素）。</summary>
        public int ResultHeightLogical { get { return _resultHeightLogical; } }

        /// <summary>探针用：直接设置结果区高度逻辑值（模拟拖分隔条后松手）。</summary>
        public void SetResultHeightLogical(int logical)
        {
            _resultHeightLogical = UiSettings.NormalizeResultPanelHeight(logical);
            ApplyResultHeight();
            SaveUiPrefs();
        }

        #endregion

        #region 初始化

        /// <summary>
        /// 工具箱按类别建树：分类=根节点（<c>Tag=null</c> ⇒ 不可拖），模块=叶子（<c>Tag=FlowModuleInfo</c>）。
        /// 分类顺序由 <see cref="FlowModule.GroupByCategory"/> 给出（采集→处理→定位→测量→拟合→输出，= 数据流方向）。
        /// </summary>
        private void InitModules()
        {
            treeModules.BeginUpdate();
            treeModules.Nodes.Clear();
            Font font = new Font("Microsoft YaHei UI", 9F);
            Font leaffont = new Font("Microsoft YaHei UI", 9F);
            int maxWidth;
            foreach (FlowModuleCategory cat in FlowModule.GroupByCategory())
            {
                var root = new TreeNode(cat.DisplayName);
                root.Tag = null;                       // 分类节点：不参与拖拽
                root.ForeColor = UiTheme.ColorTextSecondary;

                foreach (FlowModuleInfo m in cat.Modules)
                {
                    var leaf = new TreeNode(m.DisplayName);
                    leaf.Tag = m;
                    leaf.ForeColor = UiTheme.ColorTextPrimary;
                    leaf.NodeFont = leaffont;
                    root.Nodes.Add(leaf);
                   
                }
                root.NodeFont = font;
                treeModules.Nodes.Add(root);
                

                root.Expand();                         // 12 项不多，默认全展开
            }
            
            treeModules.EndUpdate();
        }

        private void OnModuleItemDrag(object sender, ItemDragEventArgs e)
        {
            var node = e.Item as TreeNode;
            var m = (node != null) ? (node.Tag as FlowModuleInfo) : null;
            if (m == null) return;                     // 分类节点：不拖

            treeModules.DoDragDrop(new DataObject(FlowCanvasControl.DragFormat, m.Key),
                                   DragDropEffects.Copy);
        }

        #endregion

        #region 输入输出端子面板（Inputs/Outputs）

        /// <summary>
        /// 按当前选中节点刷新端子树：两个根 <c>[Inputs]</c> / <c>[Outputs]</c>，
        /// 叶子来自 <see cref="FlowPortSchema"/>（端子名的唯一真源，禁止在此硬编码）。
        /// 未选中节点时清空并显示「未选中节点」。
        /// </summary>
        private void RefreshPortTree(FlowNode node)
        {
            if (treePorts == null || treePorts.Parent == null) return;   // 端子树已不在界面里

            treePorts.BeginUpdate();
            treePorts.Nodes.Clear();

            FlowNodeType type;
            if (node == null || !node.TryGetType(out type))
            {
                var none = new TreeNode("未选中节点");
                none.ForeColor = UiTheme.ColorTextSecondary;
                treePorts.Nodes.Add(none);
                treePorts.EndUpdate();
                return;
            }

            TreeNode inputs = BuildPortRoot("[Inputs]", FlowPortSchema.InputsOf(type));
            TreeNode outputs = BuildPortRoot("[Outputs]", FlowPortSchema.OutputsOf(type));

            treePorts.Nodes.Add(inputs);
            treePorts.Nodes.Add(outputs);
            inputs.Expand();
            outputs.Expand();

            treePorts.EndUpdate();
        }

        /// <summary>构造一个端子根节点；该方向无端子时挂一个「（无）」占位，避免看起来像没刷新。</summary>
        private static TreeNode BuildPortRoot(string title, List<FlowPortDef> ports)
        {
            var root = new TreeNode(title);
            root.ForeColor = UiTheme.ColorTextSecondary;

            if (ports != null)
            {
                foreach (FlowPortDef def in ports)
                {
                    if (def == null) continue;
                    root.Nodes.Add(BuildPortNode(def));
                }
            }

            if (root.Nodes.Count == 0)
            {
                var none = new TreeNode("（无）");
                none.ForeColor = UiTheme.ColorTextSecondary;
                root.Nodes.Add(none);
            }

            return root;
        }

        /// <summary>
        /// 构造单个端子节点：显示名 + 类型中文名（<see cref="FlowValidator.KindName"/>），
        /// 必填端子名称后加 <c>*</c>；颜色按数据类型取自 <see cref="UiTheme"/>。
        /// 逐行上色靠 <c>TreeNode.ForeColor</c>：<see cref="UiTree"/> 的 <c>OnDrawNode</c>
        /// 优先读它，因此不需要 TreeNode 子类。
        /// </summary>
        private static TreeNode BuildPortNode(FlowPortDef def)
        {
            string text = def.DisplayName + "（" + FlowValidator.KindName(def.Kind) + "）";
            if (def.Required) text += " *";

            var node = new TreeNode(text);
            node.Tag = def;
            node.ForeColor = PortKindColor(def.Kind);

            string tip = string.IsNullOrEmpty(def.Hint) ? def.DisplayName : def.Hint;
            if (def.Required) tip += "　[必填：未连接时校验报 E003]";
            node.ToolTipText = tip;

            return node;
        }

        /// <summary>
        /// 端子数据类型 → 显示色。全部取自 <see cref="UiTheme"/>（禁止硬写 Color.FromArgb）。
        /// 六类各一色，便于在树上区分「图像 / 位姿 / 数值 / Blob / 几何 / 任意」。
        /// </summary>
        private static Color PortKindColor(FlowPortKind kind)
        {
            switch (kind)
            {
                case FlowPortKind.Image:    return UiTheme.ColorAccent;
                case FlowPortKind.Pose:     return UiTheme.ColorWarn;
                case FlowPortKind.Number:   return UiTheme.ColorOk;
                case FlowPortKind.BlobList: return UiTheme.ColorRunning;
                case FlowPortKind.Geometry: return UiTheme.ColorTextPrimary;
                default:                    return UiTheme.ColorTextSecondary;   // Any
            }
        }

        #endregion

        #region 流程列表与画布

        private FlowDefinition CurrentFlow
        {
            get
            {
                FlowSettings fs = ConfigStore.Current.Flows;
                if (fs == null || fs.Flows == null) return null;
                return fs.Flows.Find(x => x != null && x.Id == fs.ActiveFlowId);
            }
        }

        /// <summary>
        /// 就地升级流程配置 Schema：补齐版本号与新增参数键（纯追加，不删不改名）。
        /// 旧配置（只有 8 类节点、参数键较少）加载后仍可正常打开与运行。
        /// </summary>
        private void UpgradeFlowSchema(FlowSettings fs)
        {
            try
            {
                if (FlowSettings.UpgradeToCurrent(fs)) SaveFlows();
            }
            catch (Exception ex)
            {
                Logger.Warn("流程配置升级失败：" + ex.Message);
            }
        }

        private void LoadFlows()
        {
            FlowSettings fs = ConfigStore.Current.Flows;
            UpgradeFlowSchema(fs);

            _loading = true;
            try
            {
                lstFlows.BeginUpdate();
                lstFlows.Items.Clear();
                foreach (FlowDefinition f in fs.Flows)
                    lstFlows.Items.Add(f);

                int sel = 0;
                for (int i = 0; i < fs.Flows.Count; i++)
                    if (fs.Flows[i].Id == fs.ActiveFlowId) { sel = i; break; }

                if (lstFlows.Items.Count > 0) lstFlows.SelectedIndex = sel;
                lstFlows.EndUpdate();
            }
            finally
            {
                _loading = false;
            }

            LoadFlowToCanvas();
        }

        private void LoadFlowToCanvas()
        {
            FlowDefinition f = CurrentFlow;
            canvas.Flow = f;

            _loading = true;
            try
            {
                if (f != null)
                    numViewIndex.Value = Math.Max(numViewIndex.Minimum, Math.Min(numViewIndex.Maximum, f.ViewIndex));
            }
            finally
            {
                _loading = false;
            }

            _selectedNode = null;
            ShowNodeInPanel(null);
            resultPanel?.Clear();           // 换流程：上一次运行的结果已不对应当前流程
            flowPreview.ClearHighlight();   // 同上：别让旧节点的高亮残留到新流程上
            RefreshPortTree(null);          // 换流程：端子树回到「未选中节点」
            flowPreview.SetSelectedNodeId("");
            _controller?.SetSelectedNode("");   // 同步清空控制器的「选中节点」专用槽
            flowPreview.Reset();
            RefreshCameraBindings();
            PruneRoiHistory();
            UpdateRoiButton();

            SetStatus(f != null ? ("已加载流程：" + f.Name) : "无流程");
        }

        private void OnFlowSelectionChanged(object sender, EventArgs e)
        {
            if (_loading) return;

            var f = lstFlows.SelectedItem as FlowDefinition;
            if (f == null) return;

            ConfigStore.Current.Flows.ActiveFlowId = f.Id;
            SaveFlows();
            LoadFlowToCanvas();
        }

        private void OnNewFlow(object sender, EventArgs e)
        {
            FlowSettings fs = ConfigStore.Current.Flows;
            FlowDefinition f = FlowDefinition.CreateEmpty(UniqueName("流程"));
            fs.Flows.Add(f);
            fs.ActiveFlowId = f.Id;

            SaveFlows();
            ReloadFlowList();
            SetStatus("已新建流程：" + f.Name);
        }

        /// <summary>
        /// 复制当前流程（含节点/连线/模板资产）。
        /// <para>
        /// 三个必须做对的地方，否则复制出来的流程"看着像、跑不了"：
        /// ① 流程 Id 与**所有节点 Id** 都要换新 —— 模板资产按 <c>flowId\nodeId</c> 存，
        ///    沿用旧 Id 会让两条流程共用同一份模板，改一个另一个跟着变；
        /// ② 连线里的 From/To 必须按 Id 映射改写，否则全部指向旧节点 → 校验报"引用了不存在的节点"；
        /// ③ 模板目录要整份拷过去，否则新流程里所有匹配节点报"模板资产不存在"。
        /// </para>
        /// </summary>
        private void OnCopyFlow(object sender, EventArgs e)
        {
            FlowSettings fs = ConfigStore.Current.Flows;
            FlowDefinition src = CurrentFlow;
            if (src == null) { SetStatus("请先选择一个流程"); return; }

            Dictionary<string, string> idMap;
            FlowDefinition copy = src.Duplicate(out idMap);
            copy.Name = UniqueName(copy.Name);

            int assets = TemplateStore.CopyFlowAssets(src.Id, copy.Id, idMap);

            fs.Flows.Add(copy);
            fs.ActiveFlowId = copy.Id;

            SaveFlows();
            ReloadFlowList();
            LoadFlowToCanvas();

            SetStatus("已复制流程：" + copy.Name
                      + (assets > 0 ? "（含 " + assets + " 个模板资产）" : ""));
        }

        private void OnRenameFlow(object sender, EventArgs e)
        {
            FlowDefinition f = CurrentFlow;
            if (f == null) return;

            string name = PromptText(this, "重命名流程", f.Name);
            if (string.IsNullOrWhiteSpace(name)) return;

            f.Name = name.Trim();
            SaveFlows();
            ReloadFlowList();
            SetStatus("流程已重命名：" + f.Name);
        }

        private void OnDeleteFlow(object sender, EventArgs e)
        {
            FlowSettings fs = ConfigStore.Current.Flows;
            FlowDefinition f = CurrentFlow;
            if (f == null) return;

            if (MessageBox.Show(this, "确定删除流程「" + f.Name + "」？", "删除流程",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            fs.Flows.Remove(f);

            // 删空后自动补一条空流程
            if (fs.Flows.Count == 0)
                fs.Flows.Add(FlowDefinition.CreateEmpty("流程 1"));

            fs.ActiveFlowId = fs.Flows[0].Id;
            SaveFlows();
            ReloadFlowList();
            SetStatus("流程已删除");
        }

        /// <summary>重建流程列表并保持当前流程选中。</summary>
        private void ReloadFlowList()
        {
            FlowSettings fs = ConfigStore.Current.Flows;

            _loading = true;
            try
            {
                lstFlows.BeginUpdate();
                lstFlows.Items.Clear();
                foreach (FlowDefinition f in fs.Flows)
                    lstFlows.Items.Add(f);

                int sel = 0;
                for (int i = 0; i < fs.Flows.Count; i++)
                    if (fs.Flows[i].Id == fs.ActiveFlowId) { sel = i; break; }
                if (lstFlows.Items.Count > 0) lstFlows.SelectedIndex = sel;
                lstFlows.EndUpdate();
            }
            finally
            {
                _loading = false;
            }

            LoadFlowToCanvas();
        }

        private static string UniqueName(string prefix)
        {
            FlowSettings fs = ConfigStore.Current.Flows;
            var used = new HashSet<string>();
            foreach (FlowDefinition f in fs.Flows)
                if (f != null && f.Name != null) used.Add(f.Name);

            for (int i = 1; i < 100000; i++)
            {
                string n = prefix + " " + i;
                if (!used.Contains(n)) return n;
            }
            return prefix + " " + Guid.NewGuid().ToString("N").Substring(0, 4);
        }

        #endregion

        #region 画布与参数事件

        /// <summary>
        /// 构建配置提示：Debug 版算法比 Release 慢约 6 倍（形状匹配实测 154ms vs 24ms），
        /// 现场看到"CPU 高、卡顿"时先把这条摆出来，省得去查算法。
        /// </summary>
        private static string BuildHint
        {
            get
            {
#if DEBUG
                return "　|　Debug 构建（算法约慢 6 倍，产线请用 Release）";
#else
                return "";
#endif
            }
        }

        private void OnCanvasNodeSelected(object sender, FlowNodeEventArgs e)
        {
            _selectedNode = (e != null) ? e.Node : null;

            ShowNodeInPanel(e.Node);
            // 换节点：**不清空再填**（会闪一下空白），直接从缓存回填该节点的最近一次结果；
            // 从未运行过的节点由面板显示"该节点尚未运行"空态（UI-R08 / S6）。
            resultPanel?.ShowNode(e != null && e.Node != null ? e.Node.Id : "",
                                  e != null && e.Node != null ? e.Node.Name : "");
            flowPreview.ClearHighlight();   // 同上：旧高亮属于别的节点

            // 预览「选中节点」模式据此过滤该节点自身的输出
            flowPreview.SetSelectedNodeId(e != null && e.Node != null ? e.Node.Id : "");
            _controller?.SetSelectedNode(e != null && e.Node != null ? e.Node.Id : "");   // 控制器侧专用槽同步切换

            RefreshPortTree(_selectedNode);
            UpdateRoiButton();
            UpdateRoiOutline(_selectedNode);   // FR-12：选中的 OCR 节点常显 ROI 轮廓，切走即隐藏
        }

        private void OnCanvasChanged(object sender, FlowNodeEventArgs e)
        {
            SaveFlows();
        }

        private void OnCanvasLinkChanged(object sender, FlowLinkEventArgs e)
        {
            SaveFlows();
        }

        private void OnCanvasEdited(object sender, EventArgs e)
        {
            SaveFlows();
            PruneRoiHistory();   // 可能刚删了节点：顺手清掉它的 ROI 组历史

            // 删节点后画布不会再发 NodeSelected（选择被清空），端子树要跟着回到「未选中节点」，
            // 否则会一直显示一个已经不存在的节点的端子。
            if (_selectedNode != null && !NodeStillExists(_selectedNode))
            {
                _selectedNode = null;
                RefreshPortTree(null);
                UpdateRoiOutline(null);   // 节点被删：轮廓不能留在图上指向一个不存在的节点
            }
        }

        /// <summary>节点是否仍在当前流程里（删除 / 换流程后为 false）。</summary>
        private bool NodeStillExists(FlowNode node)
        {
            FlowDefinition f = CurrentFlow;
            return (f != null && node != null && f.FindNode(node.Id) != null);
        }

        // ---------------------- 画布连线事件（进日志，避免只靠 2.8 秒 toast）----------------------

        private void OnLinkRejected(object sender, FlowLinkRejectedEventArgs e)
        {
            if (e == null || string.IsNullOrEmpty(e.Reason)) return;

            // 画布上的 toast 只有 2.8 秒，容易被漏看：同时写日志区与底部状态条
            Logger.Warn("连线被拒绝：" + e.Reason);
            SetStatus("连线未建立：" + e.Reason);
        }

        private void OnLinkReplaced(object sender, FlowLinkReplacedEventArgs e)
        {
            if (e == null || e.NewLink == null) return;

            // 落盘由 canvas.Changed → OnCanvasEdited 负责，这里只做可追溯的日志
            string oldDesc = DescribeLink(e.OldLink);
            string newDesc = DescribeLink(e.NewLink);
            Logger.Info(string.Format("端子重连：{0} → {1}", oldDesc, newDesc));
            SetStatus("输入端子已重连（旧连线被替换）");
        }

        /// <summary>把一条连线描述成「源.输出端子 → 目标.输入端子」，两端缺失时显示「?」。</summary>
        private string DescribeLink(FlowLink link)
        {
            if (link == null) return "?";

            FlowDefinition f = CurrentFlow;
            string from = "?", to = "?";
            if (f != null)
            {
                FlowNode a = f.FindNode(link.FromNodeId);
                FlowNode b = f.FindNode(link.ToNodeId);
                if (a != null) from = a.Name;
                if (b != null) to = b.Name;
            }
            return string.Format("{0}.{1} → {2}.{3}", from, link.FromPort, to, link.ToPort);
        }

        /// <summary>
        /// 自动整理布局：老配置按旧节点高度存的坐标在端子化后可能重叠。
        /// ⚠️ 只在用户点按钮时调用 —— 加载配置时自动改坐标是本项目的红线。
        /// </summary>
        private void OnAutoLayout(object sender, EventArgs e)
        {
            if (_running) { SetStatus("运行中不支持整理布局"); return; }

            canvas.AutoLayout();
            canvas.FitToView();          // 整理完顺手缩放到全图可见
            canvas.RefreshView();
            SaveFlows();                 // 坐标是数据，必须落盘
            SetStatus("已按拓扑序整理布局并保存");
        }

        private void OnParamsChanged(object sender, EventArgs e)
        {
            SaveFlows();
            SetStatus("节点参数已更新");

            // 参数面板改了「图像源 → 图片路径」：立刻出图，不必点运行
            PreviewImageSourceFile();

            // 改了模板 ROI / 训练参数后，卡片的状态可能从「已就绪」变成「与设置不匹配」
            RefreshTemplateCard();

            // 卡尺参数一改，预览上的布局要跟着动（X1/Y1/投影长度/个数都是调参的输入）
            RefreshNodeLayoutPreview(nodeParamPanel.CurrentNode);
        }

        /// <summary>
        /// 当前编辑节点是「图像源」且取图方式为「本地图片文件 / 本地图片文件夹」时，
        /// 立即把该图显示在预览区（不运行流程）。
        /// <para>
        /// 文件夹模式取的是<strong>当前这一张</strong>（<see cref="ImageFolderCursor.Peek"/>，不推进），
        /// 状态栏同时写出「第 i/N 张」—— 用户不必先跑一遍才知道跑到哪儿了。
        /// </para>
        /// </summary>
        private void PreviewImageSourceFile()
        {
            FlowNode node = nodeParamPanel.Node;
            if (!IsFileImageSource(node)) return;

            string path, note, whyNot;
            if (!TryResolveImageSourceFile(node, out path, out note, out whyNot))
            {
                if (!string.IsNullOrEmpty(whyNot)) SetStatus("图像源：" + whyNot);
                return;
            }

            string error;
            Bitmap bmp = FlowPreviewPanel.LoadImageFromFile(path, out error);
            if (bmp == null) return;

            // 交给视图后不得再 Dispose（OwnsImage=true）
            flowPreview.ShowStaticImage(bmp, "图像源：" + note);
            SetStatus("已载入图像源图片：" + path);
        }

        /// <summary>
        /// 解析「图像源」节点当前应显示/应取的图片路径。
        /// <para>
        /// 文件模式 = <c>FilePath</c>；文件夹模式 = 游标<strong>当前</strong>指向的那一张
        /// （<see cref="ImageFolderCursor.Peek"/>，只读不推进 —— 预览不该消耗播放位置）。
        /// </para>
        /// </summary>
        /// <param name="note">给预览顶条/状态栏用的短文案（文件名，或「第 i/N 张 · 文件名」）。</param>
        /// <param name="whyNot">解析失败的原因；成功时为空。</param>
        private bool TryResolveImageSourceFile(FlowNode node, out string path, out string note, out string whyNot)
        {
            path = "";
            note = "";
            whyNot = "";

            if (!IsFileImageSource(node)) return false;

            if (IsFolderImageSource(node))
            {
                string folder = node.GetString("FolderPath", "");
                if (string.IsNullOrWhiteSpace(folder))
                {
                    whyNot = "未选择图片文件夹";
                    return false;
                }

                string listError;
                string[] files = ImageFolderSource.List(folder, out listError);
                if (files.Length == 0)
                {
                    whyNot = string.IsNullOrEmpty(listError) ? "文件夹内没有图片" : listError;
                    return false;
                }

                int idx = ImageFolderCursor.Peek(ImageFolderCursor.Key(CurrentFlow, node, folder), files.Length);
                path = files[idx];
                note = string.Format(CultureInfo.InvariantCulture, "第 {0}/{1} 张 · {2}",
                                     idx + 1, files.Length, Path.GetFileName(path));
                return true;
            }

            path = node.GetString("FilePath", "");
            if (string.IsNullOrWhiteSpace(path)) { whyNot = "未选择本地图片文件"; return false; }
            if (!File.Exists(path)) { whyNot = "文件不存在（" + path + "）"; return false; }

            note = Path.GetFileName(path);
            return true;
        }

        /// <summary>
        /// 从流程里第一个「本地图片 / 本地图片文件夹」图像源节点载图到预览区
        /// （当前编辑节点不是图像源时的兜底）。载图成功返回 true。
        /// </summary>
        private bool LoadFirstFileImageSource()
        {
            FlowDefinition f = CurrentFlow;
            if (f == null || f.Nodes == null) return false;

            foreach (FlowNode n in f.Nodes)
            {
                if (n == null || !IsFileImageSource(n)) continue;

                string path, note, whyNot;
                if (!TryResolveImageSourceFile(n, out path, out note, out whyNot)) continue;

                string error;
                Bitmap bmp = FlowPreviewPanel.LoadImageFromFile(path, out error);
                if (bmp == null) continue;

                flowPreview.ShowStaticImage(bmp, "图像源：" + note);
                SetStatus("已载入图像源图片：" + path);
                return true;
            }
            return false;
        }

        #region 画几何（ROI / 直线 / 圆环）

        /// <summary>分组标题去掉括号补充说明，得到菜单里的短名（「搜索区域（宽或高为 0 = 整图）」→「搜索区域」）。</summary>
        private static string ShortGroupName(string groupTitle)
        {
            if (string.IsNullOrEmpty(groupTitle)) return "";

            int i = groupTitle.IndexOf('（');
            if (i < 0) i = groupTitle.IndexOf('(');
            string s = i >= 0 ? groupTitle.Substring(0, i) : groupTitle;
            return s.Trim();
        }

        /// <summary>参数表是否含某个键。</summary>
        private static bool HasKey(NodeParamTable table, string key)
        {
            return table.Fields.Exists(x => x != null && string.Equals(x.Key, key, StringComparison.Ordinal));
        }

        /// <summary>
        /// 候选「矩形 ROI」参数组（模板区域 / ROI 裁剪 / 搜索区域）。仅作字段匹配模板，
        /// 实际是否可用由 <see cref="FindDrawSpecs"/> 按参数表字段判定。
        /// <para>
        /// 每行前 4 项是 X/Y/W/H 键，第 5 项是显示名，<strong>可选的</strong>第 6 项是「角度键」：
        /// 有角度键就把这一组画成<strong>旋转矩形</strong>（<see cref="DrawShapeKind.RotatedRect"/>）。
        /// 本期只给 OCR 的 RoiX..RoiAngle 打开；TemplateX / SearchX 仍走轴对齐矩形 —— 这是增量，不是替换。
        /// </para>
        /// </summary>
        private static readonly string[][] RectCandidates =
        {
            new[] { "TemplateX", "TemplateY", "TemplateW", "TemplateH", "模板区域" },
            new[] { "RoiX", "RoiY", "RoiW", "RoiH", "ROI 裁剪", "RoiAngle" },
            new[] { "SearchX", "SearchY", "SearchW", "SearchH", "搜索区域" }
        };

        /// <summary>
        /// 按参数表实际字段找出该节点的<strong>所有</strong>可绘制几何组（矩形 / 直线 / 圆环），
        /// 顺序与参数面板一致。没有则返回空列表。
        /// <para>
        /// 【优先级】模板/训练区域 &gt; ROI 裁剪 &gt; 搜索区域 &gt; 直线卡尺 &gt; 圆卡尺。
        /// 模板/训练区域是算法「输入定义」，没画就没法建模，故排最前；直线/圆环通常各自独立成节点，顺序无影响。
        /// </para>
        /// </summary>
        private static List<DrawSpec> FindDrawSpecs(FlowNode node)
        {
            var result = new List<DrawSpec>();
            if (node == null) return result;

            FlowNodeType type;
            if (!node.TryGetType(out type)) return result;

            NodeParamTable table = NodeParamSchema.For(type);
            if (table == null || table.Fields == null) return result;

            string groupTitle = "";   // 扫描过程中最近遇到的分组标题
            foreach (NodeParamField f in table.Fields)
            {
                if (f == null) continue;

                if (f.Kind == NodeParamKind.Group)
                {
                    groupTitle = ShortGroupName(f.Label);
                    continue;
                }

                if (string.IsNullOrEmpty(f.Key)) continue;

                // 矩形 ROI：模板区域 / ROI 裁剪 / 搜索区域
                for (int i = 0; i < RectCandidates.Length; i++)
                {
                    string[] c = RectCandidates[i];
                    if (!string.Equals(f.Key, c[0], StringComparison.Ordinal)) continue;
                    if (!HasKey(table, c[1]) || !HasKey(table, c[2]) || !HasKey(table, c[3])) continue;
                    if (result.Exists(x => string.Equals(x.PrimaryKey, c[0], StringComparison.Ordinal))) continue;

                    string label = string.IsNullOrEmpty(groupTitle) ? c[4] : groupTitle;

                    // 有角度键 → 旋转矩形；没有（旧配置 / 其余两组）→ 轴对齐矩形
                    string angleKey = (c.Length > 5) ? c[5] : null;
                    if (!string.IsNullOrEmpty(angleKey) && HasKey(table, angleKey))
                        result.Add(DrawSpec.MakeRotatedRect(label, c[0], c[1], c[2], c[3], angleKey));
                    else
                        result.Add(DrawSpec.MakeRect(label, c[0], c[1], c[2], c[3]));
                }

                // 直线卡尺：扫描线段 X1 / Y1 / X2 / Y2
                if (f.Key == "X1" && HasKey(table, "Y1") && HasKey(table, "X2") && HasKey(table, "Y2")
                    && !result.Exists(x => x.Kind == DrawShapeKind.Line))
                {
                    result.Add(DrawSpec.MakeLine(string.IsNullOrEmpty(groupTitle) ? "扫描线段" : groupTitle));
                }

                // 圆卡尺：环形 ROI Cx / Cy / InnerRadius / OuterRadius
                if (f.Key == "Cx" && HasKey(table, "Cy") && HasKey(table, "InnerRadius") && HasKey(table, "OuterRadius")
                    && !result.Exists(x => x.Kind == DrawShapeKind.Ring))
                {
                    result.Add(DrawSpec.MakeRing(string.IsNullOrEmpty(groupTitle) ? "环形 ROI" : groupTitle));
                }
            }

            result.Sort((a, b) => SpecOrder(a).CompareTo(SpecOrder(b)));
            return result;
        }

        /// <summary>几何组在菜单中的优先级（越小越靠前）。</summary>
        private static int SpecOrder(DrawSpec s)
        {
            switch (s.Kind)
            {
                case DrawShapeKind.Rect:
                    if (s.PrimaryKey == "TemplateX") return 0;
                    if (s.PrimaryKey == "RoiX") return 1;
                    if (s.PrimaryKey == "SearchX") return 2;
                    return 2;
                case DrawShapeKind.Line: return 3;
                case DrawShapeKind.Ring: return 4;
                case DrawShapeKind.RotatedRect: return 1;   // 与 RoiX 组同优先级，排在模板区域之后、搜索区域之前
                default: return 9;
            }
        }

        /// <summary>读出节点当前的几何（图像坐标），用于菜单预览与进入绘制时的已有值显示；未设置时返回零几何。</summary>
        private static IGeometryShape ReadCurrentGeometry(FlowNode node, DrawSpec spec)
        {
            if (node == null) return null;

            if (spec.Kind == DrawShapeKind.Rect)
                return new RectangleShape(
                    (float)node.GetDouble(spec.X, 0.0),
                    (float)node.GetDouble(spec.Y, 0.0),
                    (float)node.GetDouble(spec.W, 0.0),
                    (float)node.GetDouble(spec.H, 0.0));

            if (spec.Kind == DrawShapeKind.RotatedRect)
            {
                // 参数是「未旋转时的左上角 + 尺寸 + 绕中心的角度」；几何是「中心 + 尺寸 + 角度」
                float x = (float)node.GetDouble(spec.X, 0.0);
                float y = (float)node.GetDouble(spec.Y, 0.0);
                float w = (float)node.GetDouble(spec.W, 0.0);
                float h = (float)node.GetDouble(spec.H, 0.0);
                float a = (float)node.GetDouble(spec.Angle, 0.0);
                return new RotatedRectShape(new PointF(x + w * 0.5f, y + h * 0.5f), w, h, a);
            }

            if (spec.Kind == DrawShapeKind.Line)
                return new LineSegmentShape(
                    (float)node.GetDouble(spec.X1, 0.0),
                    (float)node.GetDouble(spec.Y1, 0.0),
                    (float)node.GetDouble(spec.X2, 0.0),
                    (float)node.GetDouble(spec.Y2, 0.0));

            float cx = (float)node.GetDouble(spec.Cx, 0.0);
            float cy = (float)node.GetDouble(spec.Cy, 0.0);
            float inner = (float)node.GetDouble(spec.InnerRadius, 0.0);
            float outer = (float)node.GetDouble(spec.OuterRadius, 0.0);
            return new RingShape(new PointF(cx, cy), outer, inner);
        }

        /// <summary>菜单项文字：组名 + 当前值，例如「搜索区域（120,80 240×160）」「扫描线段（100,100->300,100）」「环形 ROI（C=(320,240) 80~120）」。</summary>
        private static string DescribeDrawSpec(FlowNode node, DrawSpec s)
        {
            IGeometryShape cur = ReadCurrentGeometry(node, s);
            string value = "未设置";

            if (s.Kind == DrawShapeKind.Rect)
            {
                RectangleF r = ((RectangleShape)cur).Rect;
                if (r.Width > 0f && r.Height > 0f)
                    value = string.Format("{0},{1} {2}×{3}",
                        (int)Math.Round(r.X), (int)Math.Round(r.Y),
                        (int)Math.Round(r.Width), (int)Math.Round(r.Height));
            }
            else if (s.Kind == DrawShapeKind.Line)
            {
                LineSegmentShape ls = (LineSegmentShape)cur;
                if (ls.Length > 0f)
                    value = string.Format("({0},{1})->({2},{3})",
                        (int)Math.Round(ls.P1.X), (int)Math.Round(ls.P1.Y),
                        (int)Math.Round(ls.P2.X), (int)Math.Round(ls.P2.Y));
            }
            else if (s.Kind == DrawShapeKind.RotatedRect)
            {
                // 直接读参数而不是从 cur 反推：RotatedRectShape 的宽高会被钳到 ≥1，
                // 未配置的 ROI 会显示成「1×1」而不是「未设置」。
                double x = node.GetDouble(s.X, 0.0), y = node.GetDouble(s.Y, 0.0);
                double w = node.GetDouble(s.W, 0.0), h = node.GetDouble(s.H, 0.0);
                double a = node.GetDouble(s.Angle, 0.0);
                if (w > 0.0 && h > 0.0)
                    value = string.Format("{0},{1} {2}×{3} {4}°",
                        (int)Math.Round(x), (int)Math.Round(y), (int)Math.Round(w), (int)Math.Round(h),
                        a.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
            }
            else
            {
                RingShape ring = (RingShape)cur;
                if (ring.OuterRadius > 0f)
                    value = string.Format("C=({0},{1}) {2}~{3}",
                        (int)Math.Round(ring.Center.X), (int)Math.Round(ring.Center.Y),
                        (int)Math.Round(ring.InnerRadius), (int)Math.Round(ring.OuterRadius));
            }

            return s.Label + "（" + value + "）";
        }

        /// <summary>
        /// 把节点交给参数面板，并顺手刷新模板状态卡片。
        /// <para>
        /// 刻意做成唯一入口（不再零散调用 <see cref="NodeParamPanel.SetNode"/>）：
        /// 卡片挂在面板 body 上，任何"换了节点却不刷卡片"的漏刷都会表现为
        /// "切到形状匹配节点却还显示上一个节点的模板状态"，这类 bug 极难复现。
        /// </para>
        /// </summary>
        private void ShowNodeInPanel(FlowNode node)
        {
            nodeParamPanel.SetNode(node);
            RefreshTemplateCard();
            RefreshNodeLayoutPreview(node);
        }

        /// <summary>
        /// 把"参数推导出来的 ROI 布局"画到预览上（卡尺扫描线 + 每个卡尺的投影矩形）。
        /// <para>
        /// 【为什么选中节点就画、不等运行】调 X1/Y1/投影长度/卡尺个数时，用户必须看得见
        /// "卡尺画在哪、有几个、投影多长"，否则只能盲调；运行后的实测窗口会覆盖这一层。
        /// </para>
        /// </summary>
        private void RefreshNodeLayoutPreview(FlowNode node)
        {
            if (flowPreview == null) return;

            FlowNodeType type;
            if (node == null || !node.TryGetType(out type))
            {
                flowPreview.ClearNodeLayout();
                return;
            }

            if (type == FlowNodeType.LineCaliper)
            {
                int count = node.GetInt("CaliperCount", 10);
                double len = node.GetDouble("ProjectionLength", 20);
                double hw = node.GetDouble("FilterHalfWidth", 2);

                List<IGeometryShape> shapes = FlowShapes.CaliperLayoutLine(
                    node.GetDouble("X1", 100), node.GetDouble("Y1", 100),
                    node.GetDouble("X2", 300), node.GetDouble("Y2", 100),
                    count, len, hw);

                flowPreview.ShowNodeLayout(shapes, string.Format(CultureInfo.InvariantCulture,
                    "卡尺 {0} 个 · 投影 {1:0.#}px · 宽度 {2:0.#}px（未运行：示意布局）",
                    count, len, Math.Max(1.0, hw * 2)));
                return;
            }

            if (type == FlowNodeType.CircleCaliper)
            {
                int count = node.GetInt("CaliperCount", 20);
                double len = node.GetDouble("ProjectionLength", 20);
                double hw = node.GetDouble("FilterHalfWidth", 2);

                List<IGeometryShape> shapes = FlowShapes.CaliperLayoutCircle(
                    node.GetDouble("Cx", 320), node.GetDouble("Cy", 240),
                    node.GetDouble("InnerRadius", 80), node.GetDouble("OuterRadius", 120),
                    node.GetDouble("StartAngle", 0), node.GetDouble("AngleSpan", 360),
                    count, len, hw);

                flowPreview.ShowNodeLayout(shapes, string.Format(CultureInfo.InvariantCulture,
                    "环形卡尺 {0} 个 · 投影 {1:0.#}px · 宽度 {2:0.#}px（未运行：示意布局）",
                    count, len, Math.Max(1.0, hw * 2)));
                return;
            }

            flowPreview.ClearNodeLayout();
        }

        // =====================================================================
        //  模板状态卡片与模板制作窗（FR-01/03/05，DEF-02/03）
        // =====================================================================

        /// <summary>节点对应的模板算法键；不是匹配类节点返回 null。</summary>
        private static string TemplateAlgorithmOf(FlowNode node)
        {
            FlowNodeType type;
            if (node == null || !node.TryGetType(out type)) return null;

            if (type == FlowNodeType.ShapeMatch) return "ShapeMatch";
            if (type == FlowNodeType.TemplateMatch) return "TemplateMatch";
            return null;
        }

        /// <summary>
        /// 节点配置里的模板 ROI（<strong>用户坐标原样</strong>，不做位置修正映射）。
        /// 与模板资产比对"ROI 是否被改过"必须用它 —— 映射后的 ROI 是"这一帧该看哪里"，
        /// 不是"模板当初框的是哪一块"。
        /// </summary>
        private static Rectangle ConfiguredTemplateRoi(FlowNode node)
        {
            if (node == null) return Rectangle.Empty;
            return new Rectangle(
                node.GetInt("TemplateX", 0), node.GetInt("TemplateY", 0),
                node.GetInt("TemplateW", 0), node.GetInt("TemplateH", 0));
        }

        /// <summary>卸掉当前 banner 卡片（面板负责 Dispose 旧控件）。</summary>
        private void ClearBannerCard()
        {
            if (_templateCard == null && _ocrCard == null) return;

            nodeParamPanel.SetBanner(null);
            _templateCard = null;
            _templateCardNodeId = null;
            _ocrCard = null;
            _ocrCardNodeId = null;
        }

        /// <summary>
        /// 解析节点的 OCR 模板目录为绝对路径。
        /// <para>解析<strong>只有一个入口</strong>：参数语义是"相对程序运行目录的名字或相对路径"，
        /// <c>Path.Combine</c> 遇绝对路径会直接返回它，所以两种情况一条表达式覆盖。
        /// <b>不存在"新目录优先"的第二套逻辑</b> —— 那会让老 <c>TemplateDir</c> 被静默忽略（C2 红线）。</para>
        /// </summary>
        private static string ResolveOcrDir(FlowNode node)
        {
            string v = node != null ? node.GetString("TemplateDir", "OCRModel") : "OCRModel";
            v = (v ?? "").Trim();
            if (v.Length == 0) v = "OCRModel";

            try { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, v); }
            catch (Exception) { return v; }
        }

        private void OnOcrCardManage(object sender, EventArgs e)
        {
            OpenOcrTemplateEditor();
        }

        private void OnOcrCardOpenFolder(object sender, EventArgs e)
        {
            FlowNode node = _selectedNode ?? nodeParamPanel.Node;
            if (node == null) return;

            string dir = ResolveOcrDir(node);
            try
            {
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                System.Diagnostics.Process.Start("explorer.exe", "\"" + dir + "\"");
            }
            catch (Exception ex)
            {
                SetStatus("无法打开模板目录：" + ex.Message);
            }
        }

        /// <summary>
        /// 打开字符模板库窗体（模态）。
        /// <para>窗体里改目录会抛 <c>DirChanged</c>，在这里立刻写回节点参数并落盘 ——
        /// 不写回就是"在库里加了一批字符，回主界面一跑还是读老目录"。</para>
        /// </summary>
        private void OpenOcrTemplateEditor()
        {
            FlowNode node = _selectedNode ?? nodeParamPanel.Node;
            if (node == null)
            {
                MessageBox.Show(this, "请先在画布上选中一个「字符识别(OCR)」节点。", "字符模板库",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            FlowNodeType type;
            if (!node.TryGetType(out type) || type != FlowNodeType.Ocr)
            {
                MessageBox.Show(this, "节点「" + node.Name + "」不是字符识别节点，没有字符模板库。", "字符模板库",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var dlg = new OcrTemplateForm(node.GetString("TemplateDir", "OCRModel")))
            {
                dlg.DirChanged += (s, e) =>
                {
                    string value = dlg.CurrentDir;
                    if (string.IsNullOrEmpty(value)) return;
                    if (string.Equals(value, node.GetString("TemplateDir", "OCRModel"), StringComparison.Ordinal)) return;

                    node.Set("TemplateDir", value);
                    SaveFlows();
                    SetStatus("模板目录已更新：" + value);
                };

                dlg.ShowDialog(this);
            }

            // 关窗后强制重扫一次：可能刚入库 / 刚迁移
            if (_ocrCard != null) _ocrCard.Rescan();
            RefreshTemplateCard();
        }

        /// <summary>
        /// 按当前选中节点维护模板状态卡片。
        /// <para>
        /// 【生命周期铁律】<strong>只有节点 Id 变了才 Dispose 旧卡片、构造新卡片</strong>；
        /// 节点没变时只更新文字，缩略图由卡片自己按"模板戳是否变化"决定要不要重算。
        /// 理由见 <c>_templateCardNodeId</c> 的注释（旋转 ROI 编辑态是高频 SetNode 路径）。
        /// </para>
        /// </summary>
        private void RefreshTemplateCard()
        {
            FlowNode node = nodeParamPanel.Node;
            string algorithm = TemplateAlgorithmOf(node);

            FlowNodeType nodeType;
            bool isOcr = node != null && node.TryGetType(out nodeType) && nodeType == FlowNodeType.Ocr;

            if (algorithm == null && !isOcr)
            {
                ClearBannerCard();
                return;
            }

            // ---- OCR：字符模板库卡片（整目录扫描）----
            if (isOcr)
            {
                string dir = ResolveOcrDir(node);

                if (_ocrCard == null || !string.Equals(_ocrCardNodeId, node.Id, StringComparison.Ordinal))
                {
                    ClearBannerCard();

                    _ocrCard = new OcrTemplateCard();
                    _ocrCard.ManageRequested += OnOcrCardManage;
                    _ocrCard.OpenFolderRequested += OnOcrCardOpenFolder;
                    _ocrCardNodeId = node.Id;

                    nodeParamPanel.SetBanner(_ocrCard);
                    _ocrCard.Bind(dir);
                }
                else
                {
                    // 同一节点：只在目录变化时重扫（Bind 内部判等）
                    _ocrCard.Bind(dir);
                }
                return;
            }

            // ---- 匹配类：单模板资产卡片 ----
            if (_ocrCard != null)
            {
                ClearBannerCard();
            }

            FlowDefinition flow = CurrentFlow;
            string flowId = (flow != null) ? flow.Id : "";

            if (_templateCard == null
                || !string.Equals(_templateCardNodeId, node.Id, StringComparison.Ordinal))
            {
                nodeParamPanel.SetBanner(null);
                _templateCard = new TemplateStatusCard();
                _templateCard.CreateRequested += OnCardCreateRequested;
                _templateCard.DeleteRequested += OnCardDeleteRequested;
                _templateCardNodeId = node.Id;
                nodeParamPanel.SetBanner(_templateCard);
            }

            _templateCard.Bind(flowId, node.Id, algorithm, ConfiguredTemplateRoi(node));
            _templateCard.Refresh();
        }

        /// <summary>参数面板的动作行：目前只有匹配类节点的「创建模板…」。</summary>
        private void OnParamAction(string key)
        {
            if (string.IsNullOrEmpty(key)) return;

            if (string.Equals(key, "CreateTemplate", StringComparison.Ordinal))
            {
                OpenTemplateEditor();
                return;
            }

            if (string.Equals(key, "ManageOcrTemplates", StringComparison.Ordinal))
            {
                OpenOcrTemplateEditor();
                return;
            }

            if (string.Equals(key, "EditScript", StringComparison.Ordinal))
            {
                OpenScriptEditor();
                return;
            }

            if (string.Equals(key, "RetractBaseline", StringComparison.Ordinal))
            {
                FlowNode node = _selectedNode ?? nodeParamPanel.Node;
                if (node == null) return;

                node.Set("RefSet", "0");
                SaveFlows();
                nodeParamPanel.SetNode(node);      // 刷新面板（隐藏标记已变）
                SetStatus("已清除基准：下一次运行会以当前位姿重新示教");
                return;
            }

            if (string.Equals(key, "OcrRescanTemplates", StringComparison.Ordinal))
            {
                FlowNode node = _selectedNode ?? nodeParamPanel.Node;
                string dir = ResolveOcrDir(node);
                if (_ocrCard != null) _ocrCard.Rescan();
                SetStatus("已重新扫描模板目录：" + dir);
                return;
            }

            // RoiPick / RoiPreview 由预览区顶条与 T04 承接，这里不重复实现，也不静默吞掉
            Logger.Info("参数面板动作未在本窗体实现：" + key);
        }

        /// <summary>
        /// 打开脚本编辑器：把"排在脚本之前的节点"和最近一次运行的输出一起带过去，
        /// 左上角就能看到每个上游节点的真实数据（没跑过则只列节点名）。
        /// </summary>
        private void OpenScriptEditor()
        {
            FlowNode node = nodeParamPanel.CurrentNode;
            if (node == null) { SetStatus("请先选中脚本节点"); return; }

            // 按执行顺序取"脚本之前"的节点
            var names = new List<string>();
            List<FlowNode> order;
            string err;
            if (CurrentFlow != null && FlowOrder.Build(CurrentFlow, out order, out err) && order != null)
            {
                foreach (FlowNode n in order)
                {
                    if (n == null) continue;
                    if (string.Equals(n.Id, node.Id, StringComparison.Ordinal)) break;
                    names.Add(string.IsNullOrEmpty(n.Name) ? n.Id : n.Name);
                }
            }

            // 最近一次运行的输出（只取上游的）
            var outputs = new List<NodeOutput>();
            if (_runner != null && _runner.LastOutputs != null)
            {
                foreach (NodeOutput o in _runner.LastOutputs)
                {
                    if (o == null) continue;
                    if (string.Equals(o.Id, node.Id, StringComparison.Ordinal)) break;
                    outputs.Add(o);
                }
            }

            using (var dlg = new ScriptEditorForm(node, outputs, names))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    SaveFlows();
                    nodeParamPanel.SetNode(node);
                    SetStatus("脚本已保存");
                }
            }
        }

        private void OnCardCreateRequested(object sender, EventArgs e)
        {
            OpenTemplateEditor();
        }

        private void OnCardDeleteRequested(object sender, EventArgs e)
        {
            FlowNode node = nodeParamPanel.Node;
            FlowDefinition flow = CurrentFlow;
            if (node == null || flow == null) return;

            DialogResult ask = MessageBox.Show(this,
                "确定删除节点「" + node.Name + "」的模板吗？删除后该节点运行时会判定 NG（未配置模板），\n" +
                "需要重新制作模板才能恢复。",
                "删除模板", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
            if (ask != DialogResult.OK) return;

            string message;
            TemplateStore.Delete(flow.Id, node.Id, out message);

            // 删完立刻刷卡片：不必等下一次运行（模板戳已变，运行期会在下一帧自动重训并改判 NG）
            RefreshTemplateCard();
            SetStatus(message);
            Logger.Info(string.Format("删除模板：flow={0} node={1} → {2}", flow.Id, node.Id, message));
        }

        /// <summary>
        /// 打开模板制作窗（模态）。训练用的是窗体独占的算法上下文，与正在跑的流程互不干扰。
        /// </summary>
        private void OpenTemplateEditor()
        {
            FlowNode node = _selectedNode ?? nodeParamPanel.Node;
            if (node == null)
            {
                MessageBox.Show(this, "请先在画布上选中一个「形状匹配」或「模板匹配」节点。", "创建模板",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string algorithm = TemplateAlgorithmOf(node);
            if (algorithm == null)
            {
                MessageBox.Show(this, "节点「" + node.Name + "」不需要模板（只有形状匹配 / 模板匹配才有）。",
                    "创建模板", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            FlowDefinition flow = CurrentFlow;
            if (flow == null || string.IsNullOrEmpty(flow.Id))
            {
                MessageBox.Show(this, "当前没有打开的流程，模板无法关联到流程。", "创建模板",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var dlg = new TemplateForm(
                algorithm, flow.Id, node.Id,
                ConfiguredTemplateRoi(node),
                node.GetDouble("Contrast", 30),
                node.GetInt("MaxFeatures", 1500),
                node.GetDouble("MinFeatureDist", 2),
                GrabPreviewFrame))
            {
                dlg.ShowDialog(this);

                if (!string.IsNullOrEmpty(dlg.SavedStamp))
                {
                    // 模板图正是"这块 ROI"的像素 → 必须把 ROI 写回节点，否则运行期立刻判
                    // 「模板与当前 ROI 不匹配」（用户刚点完保存就看到不匹配，最难自查）
                    Rectangle savedRoi = dlg.SavedRoi;
                    node.Set("TemplateX", savedRoi.Width > 0 ? savedRoi.X : 0);
                    node.Set("TemplateY", savedRoi.Height > 0 ? savedRoi.Y : 0);
                    node.Set("TemplateW", savedRoi.Width > 0 ? savedRoi.Width : 0);
                    node.Set("TemplateH", savedRoi.Height > 0 ? savedRoi.Height : 0);

                    SaveFlows();
                    SetStatus(dlg.SavedMessage);
                }
            }

            // 无论保存成功 / 取消 / 删除，都重刷一次：状态卡片的唯一真源是磁盘
            RefreshTemplateCard();
        }

        /// <summary>
        /// 把预览区当前显示的图像复制一份给模板窗（"抓取当前帧"）。
        /// <para>必须 Clone：视图对图像持有所有权，直接把它的位图交出去，
        /// 一旦预览区换图就会把模板窗正在用的图 Dispose 掉（表现为随机 GDI+ 崩溃）。</para>
        /// </summary>
        private Bitmap GrabPreviewFrame()
        {
            Bitmap cur = flowPreview.DisplayedImage;
            if (cur == null) return null;

            try { return (Bitmap)cur.Clone(); }
            catch (Exception ex)
            {
                Logger.Error("抓取当前帧失败：" + ex.Message);
                return null;
            }
        }

        /// <summary>「画几何」按钮可用态：仅当选中节点确实有可绘制的几何参数时可用。</summary>
        private void UpdateRoiButton()
        {
            flowPreview.SetRoiEnabled(FindDrawSpecs(_selectedNode ?? nodeParamPanel.Node).Count > 0);
        }

        /// <summary>
        /// FR-12：选中节点时在预览图上<strong>常显</strong>它自己的 ROI 轮廓 + 角度/尺寸标签，
        /// 切到别的节点即隐藏（<see cref="OnCanvasNodeSelected"/> 里每次都会重算）。
        /// <para>
        /// 只对「带角度的 RoiX 组」（= OCR 旋转 ROI）生效；模板区域 / 搜索区域仍是轴对齐，
        /// 保持原有交互不变。复用 <see cref="RotatedRectShape"/>（<c>IsEditable=false</c>），不新造类型。
        /// </para>
        /// </summary>
        private void UpdateRoiOutline(FlowNode node)
        {
            if (flowPreview == null) return;

            DrawSpec spec = null;
            if (node != null)
            {
                foreach (DrawSpec s in FindDrawSpecs(node))
                {
                    if (s.Kind == DrawShapeKind.RotatedRect) { spec = s; break; }
                }
            }

            if (spec == null) { flowPreview.ClearRoiOutline(); return; }

            // 未配置 ROI（宽或高为 0）时不画：0 的语义是「整帧」，画个 0 尺寸框只会误导
            double w = node.GetDouble(spec.W, 0.0);
            double h = node.GetDouble(spec.H, 0.0);
            if (w <= 0.0 || h <= 0.0) { flowPreview.ClearRoiOutline(); return; }

            RotatedRectShape rr = ReadCurrentGeometry(node, spec) as RotatedRectShape;
            if (rr == null) { flowPreview.ClearRoiOutline(); return; }

            flowPreview.SetRoiOutline(rr, FlowPreviewPanel.FormatRotatedRoiLabel(
                (float)w, (float)h, (float)node.GetDouble(spec.Angle, 0.0)));
        }

        /// <summary>
        /// 点预览区顶条的「画几何」：单组直接进绘制；<strong>多组每次都弹菜单</strong>
        /// （不沿用历史直接进，否则用户想换组只能重选节点或重启，等于功能废一半）。
        /// 历史只用来把上次选的组排到第一项并加 ✓，让「沿用」退化为一次点击。
        /// </summary>
        private void OnDrawRequested(object sender, EventArgs e)
        {
            FlowNode node = _selectedNode ?? nodeParamPanel.Node;
            if (node == null)
            {
                MessageBox.Show(this, "请先在画布上选中一个节点，再画几何。", "画几何",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            List<DrawSpec> specs = FindDrawSpecs(node);
            if (specs.Count == 0)
            {
                MessageBox.Show(this, "节点「" + node.Name + "」没有可绘制的几何参数，无需画几何。", "画几何",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // 单组直接进绘制；≥2 组每次都弹菜单（随时可换组）
            if (specs.Count == 1) { StartDraw(node, specs[0]); return; }

            ShowDrawSpecMenu(node, specs);
        }

        /// <summary>
        /// 在「画几何」按钮下方弹出几何组选择菜单。
        /// 该节点上次选过的组排在第一项并加 <c>✓</c> 前缀，其余保持原顺序。
        /// </summary>
        private void ShowDrawSpecMenu(FlowNode node, List<DrawSpec> specs)
        {
            Control anchor = flowPreview.RoiPickAnchor;
            if (anchor == null) { StartDraw(node, specs[0]); return; }

            // 把上次选的组提到最前（保持其余组的相对顺序，故不用 Sort）
            var ordered = new List<DrawSpec>();
            string last;
            _roiLastGroup.TryGetValue(node.Id, out last);

            if (!string.IsNullOrEmpty(last))
            {
                DrawSpec first = specs.Find(g => string.Equals(g.PrimaryKey, last, StringComparison.Ordinal));
                if (first != null) ordered.Add(first);
            }
            foreach (DrawSpec g in specs)
                if (!ordered.Contains(g)) ordered.Add(g);

            var menu = new ContextMenuStrip();
            for (int i = 0; i < ordered.Count; i++)
            {
                DrawSpec picked = ordered[i];   // 闭包捕获
                bool isLast = !string.IsNullOrEmpty(last)
                              && string.Equals(picked.PrimaryKey, last, StringComparison.Ordinal);

                var item = new ToolStripMenuItem((isLast ? "✓ " : "") + DescribeDrawSpec(node, picked));
                item.Click += delegate { StartDraw(node, picked); };
                menu.Items.Add(item);
            }

            // ⚠️ 菜单是一次性的，但【不要】在 Closed 里立刻 Dispose：
            // 点菜单项会走 StartDraw → BeginDraw → viewer.Focus()，Focus 抢走焦点会让
            // 菜单在它自身的「点击→关闭」流程里被关闭，此时立刻 Dispose 会与 ToolStrip 内部的
            // 句柄访问（SetVisibleCore→ReparentToDropDownOwnerWindow→get_Handle）并发，
            // 抛 ObjectDisposedException「无法访问已释放的对象」（已从线上日志真实抓到）。
            // 这里交给 GC 回收即可（菜单连同子项都是短命对象，无泄漏风险）。
            menu.Show(anchor, new Point(0, anchor.Height));
        }

        /// <summary>进入绘制（先确保有图，再记住本次画的是哪一组几何）。</summary>
        private void StartDraw(FlowNode node, DrawSpec spec)
        {
            // 没有图像就拖不了：先按「图像源」节点的本地图片自动载入一张
            if (!flowPreview.HasImage) PreviewImageSourceFile();

            // 兜底：当前编辑的不是图像源节点时，改从流程里任意一个「本地图片」图像源载图。
            // 否则用户在选中「模板匹配」这类节点时点「画几何」会卡在「预览区还没有图像」。
            if (!flowPreview.HasImage) LoadFirstFileImageSource();

            if (!flowPreview.HasImage)
            {
                MessageBox.Show(this,
                    "预览区还没有图像。请先点预览区的「载入图片」或运行一次流程，再画几何。",
                    "画几何", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            spec.Current = ReadCurrentGeometry(node, spec);   // 进入时预览已有几何
            _drawSpec = spec;   // 记住当前画的是哪一组，OnShapePicked 据此写回

            // 记住该节点的选择：下次点「画几何」直接沿用，不再弹菜单
            if (!string.IsNullOrEmpty(node.Id)) _roiLastGroup[node.Id] = spec.PrimaryKey;
            flowPreview.BeginDraw(spec);
            SetStatus("请在预览区图像上拖动绘制「" + spec.Label + "」，按 Esc 取消");
        }

        /// <summary>
        /// 绘制完成：把图像坐标的几何写回用户选中的那一组参数并持久化。
        /// 按 <see cref="_drawSpec.Kind"/> 分流：矩形→X/Y/W/H；直线→X1/Y1/X2/Y2；圆环→Cx/Cy/InnerRadius/OuterRadius。
        /// </summary>
        private void OnShapePicked(object sender, ShapeEventArgs e)
        {
            FlowNode node = _selectedNode ?? nodeParamPanel.Node;
            if (node == null) { _drawSpec = null; return; }

            DrawSpec spec = _drawSpec;
            if (spec == null || e == null || e.Shape == null) { _drawSpec = null; return; }

            if (spec.Kind == DrawShapeKind.Rect)
            {
                RectangleShape rs = e.Shape as RectangleShape;
                if (rs == null) { _drawSpec = null; return; }

                RectangleF r = rs.Rect;
                int x = (int)Math.Round(r.X);
                int y = (int)Math.Round(r.Y);
                int w = (int)Math.Round(r.Width);
                int h = (int)Math.Round(r.Height);
                if (x < 0) x = 0;
                if (y < 0) y = 0;
                if (w < 0) w = 0;
                if (h < 0) h = 0;

                node.Set(spec.X, x);
                node.Set(spec.Y, y);
                node.Set(spec.W, w);
                node.Set(spec.H, h);
                SyncAndReport(node, spec, string.Format("X={0}, Y={1}, {2}×{3}", x, y, w, h));
            }
            else if (spec.Kind == DrawShapeKind.Line)
            {
                LineSegmentShape ls = e.Shape as LineSegmentShape;
                if (ls == null) { _drawSpec = null; return; }

                int x1 = (int)Math.Round(ls.P1.X);
                int y1 = (int)Math.Round(ls.P1.Y);
                int x2 = (int)Math.Round(ls.P2.X);
                int y2 = (int)Math.Round(ls.P2.Y);

                node.Set(spec.X1, x1);
                node.Set(spec.Y1, y1);
                node.Set(spec.X2, x2);
                node.Set(spec.Y2, y2);
                SyncAndReport(node, spec, string.Format("({0},{1})->({2},{3})", x1, y1, x2, y2));
            }
            else if (spec.Kind == DrawShapeKind.RotatedRect)
            {
                RotatedRectShape rr = e.Shape as RotatedRectShape;
                if (rr == null) { _drawSpec = null; return; }

                int w = (int)Math.Round(rr.Width);
                int h = (int)Math.Round(rr.Height);
                if (w < 1) w = 1;
                if (h < 1) h = 1;

                // 几何（中心）→ 参数（未旋转时的左上角）：left = center − W/2，
                // 用取整后的 w/h 反算，保证「读→写」往返不漂移（中心恒为 .0 / .5）。
                int x = (int)Math.Round(rr.Center.X - w * 0.5);
                int y = (int)Math.Round(rr.Center.Y - h * 0.5);
                if (x < 0) x = 0;
                if (y < 0) y = 0;

                double angle = Math.Round(rr.AngleDeg, 2);   // 已是 (-180, 180]

                node.Set(spec.X, x);
                node.Set(spec.Y, y);
                node.Set(spec.W, w);
                node.Set(spec.H, h);
                node.Set(spec.Angle, angle);

                SyncAndReport(node, spec, string.Format("X={0}, Y={1}, {2}×{3}, {4}°",
                    x, y, w, h, angle.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)));

                // ⚠️ 旋转矩形是「编辑态」：用户松手会再提交一次，所以这里要把当前组还回去
                //    （SyncAndReport 末尾会把它清空，那是给「一次画完就结束」的矩形/直线/圆环用的）。
                _drawSpec = spec;

                UpdateRoiOutline(node);   // FR-12：轮廓跟着新值走
            }
            else // Ring
            {
                RingShape ring = e.Shape as RingShape;
                if (ring == null) { _drawSpec = null; return; }

                int cx = (int)Math.Round(ring.Center.X);
                int cy = (int)Math.Round(ring.Center.Y);
                int inner = (int)Math.Round(ring.InnerRadius);
                int outer = (int)Math.Round(ring.OuterRadius);
                if (inner < 0) inner = 0;
                if (outer < inner + 1) outer = inner + 1;   // 保证内径 < 外径

                node.Set(spec.Cx, cx);
                node.Set(spec.Cy, cy);
                node.Set(spec.InnerRadius, inner);
                node.Set(spec.OuterRadius, outer);
                SyncAndReport(node, spec, string.Format("C=({0},{1}) 内={2} 外={3}", cx, cy, inner, outer));
            }
        }

        /// <summary>写回后同步参数面板、持久化并给出状态提示。</summary>
        private void SyncAndReport(FlowNode node, DrawSpec spec, string valueText)
        {
            // 参数面板正显示该节点时同步回填，避免界面与配置不一致
            FlowNode shown = nodeParamPanel.Node;
            if (shown != null && shown.Id == node.Id) ShowNodeInPanel(node);

            SaveFlows();
            SetStatus(string.Format("已更新「{0}」：{1}；{2}", spec.Label, valueText, OverlayClearedHint));
            _drawSpec = null;
        }

        /// <summary>
        /// 清掉已删除节点的几何组历史，避免字典无限增长。
        /// 画布没有 NodeRemoved 事件，故在「加载流程」与「画布被编辑」后统一清理。
        /// </summary>
        private void PruneRoiHistory()
        {
            if (_roiLastGroup.Count == 0) return;

            FlowDefinition f = CurrentFlow;
            if (f == null || f.Nodes == null) { _roiLastGroup.Clear(); return; }

            var alive = new HashSet<string>();
            foreach (FlowNode n in f.Nodes)
                if (n != null && !string.IsNullOrEmpty(n.Id)) alive.Add(n.Id);

            var dead = new List<string>();
            foreach (string id in _roiLastGroup.Keys)
                if (!alive.Contains(id)) dead.Add(id);

            foreach (string id in dead) _roiLastGroup.Remove(id);
        }

        /// <summary>取消画几何：叠加图形同样被清掉了，提示一句免得用户以为是 bug。</summary>
        private void OnRoiPickCancelled(object sender, EventArgs e)
        {
            _drawSpec = null;
            SetStatus("已取消画几何；" + OverlayClearedHint);
        }

        #endregion

        /// <summary>
        /// 预览区「载入图片」读图成功后：把路径回填到「图像源」节点
        /// （FilePath = 该路径，Source = "File"）并持久化。
        /// 优先回填画布当前选中节点（若它本身就是图像源），否则回填流程里第一个图像源节点；
        /// 画布上没有图像源节点时只显示图片并提示。
        /// </summary>
        private void OnPreviewImageLoaded(object sender, string path)
        {
            if (string.IsNullOrEmpty(path)) return;

            FlowDefinition f = CurrentFlow;
            FlowNode src = IsImageSource(_selectedNode) ? _selectedNode : FindImageSourceNode(f);

            if (src == null)
            {
                SetStatus("画布上还没有「图像源」节点，请从左侧工具箱拖入");
                return;
            }

            src.Set("Source", "File");
            src.Set("FilePath", path);

            // 参数面板正显示该节点时同步回填，避免界面与配置不一致
            FlowNode shown = nodeParamPanel.Node;
            if (shown != null && shown.Id == src.Id) ShowNodeInPanel(src);

            SaveFlows();
            SetStatus("已载入图片并绑定到图像源节点：" + Path.GetFileName(path));
        }

        /// <summary>节点是「图像源」时返回 true。</summary>
        private static bool IsImageSource(FlowNode node)
        {
            if (node == null) return false;

            FlowNodeType type;
            return node.TryGetType(out type) && type == FlowNodeType.ImageSource;
        }

        /// <summary>节点是「图像源」且取图方式为「本地图片文件 / 本地图片文件夹」时返回 true。</summary>
        private static bool IsFileImageSource(FlowNode node)
        {
            if (!IsImageSource(node)) return false;

            string s = node.GetString("Source", "Camera");
            return string.Equals(s, "File", StringComparison.OrdinalIgnoreCase)
                || string.Equals(s, "Folder", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>节点是「图像源」且取图方式为「本地图片文件夹」时返回 true。</summary>
        private static bool IsFolderImageSource(FlowNode node)
        {
            if (!IsImageSource(node)) return false;
            return string.Equals(node.GetString("Source", "Camera"), "Folder", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>在流程中查找第一个「图像源」节点；没有则返回 null。</summary>
        private static FlowNode FindImageSourceNode(FlowDefinition flow)
        {
            if (flow == null || flow.Nodes == null) return null;

            foreach (FlowNode n in flow.Nodes)
            {
                if (n == null) continue;

                FlowNodeType t;
                if (n.TryGetType(out t) && t == FlowNodeType.ImageSource) return n;
            }
            return null;
        }

        #endregion

        #region 绑定

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

        private void RefreshCameraBindings()
        {
            FlowDefinition f = CurrentFlow;
            string want = f != null ? f.CameraId : "";

            _loading = true;
            try
            {
                cmbCamera.BeginUpdate();
                cmbCamera.Items.Clear();
                cmbCamera.Items.Add(new BindItem("", "（未绑定）"));

                // 读取「相机管理」的完整相机列表（含未打开的），让流程能直接绑定任意已登记相机，
                // 再到相机管理里打开；不再只看「已打开」列表，避免绑定下拉读不到相机管理列表。
                var infos = CameraManager.Instance.Enumerate();
                bool found = string.IsNullOrEmpty(want);
                foreach (CameraInfo info in infos)
                {
                    if (info == null) continue;
                    cmbCamera.Items.Add(new BindItem(info.Id, info.ToString()));
                    if (info.Id == want) found = true;
                }

                // 绑定存在但相机未打开：仍显示出来，便于保留绑定关系
                if (!found && !string.IsNullOrEmpty(want))
                    cmbCamera.Items.Add(new BindItem(want, want + "（未打开）"));

                int sel = 0;
                for (int i = 0; i < cmbCamera.Items.Count; i++)
                {
                    var it = cmbCamera.Items[i] as BindItem;
                    if (it != null && it.CameraId == want) { sel = i; break; }
                }
                cmbCamera.SelectedIndex = sel;
                cmbCamera.EndUpdate();
            }
            finally
            {
                _loading = false;
            }
        }

        private void OnRefreshCameraList(object sender, EventArgs e)
        {
            RefreshCameraBindings();
            SetStatus("相机列表已刷新");
        }

        private void OnCameraBindingChanged(object sender, EventArgs e)
        {
            if (_loading) return;

            FlowDefinition f = CurrentFlow;
            if (f == null) return;

            var it = cmbCamera.SelectedItem as BindItem;
            f.CameraId = it != null ? it.CameraId : "";
            SaveFlows();
            SetStatus("相机绑定：" + (string.IsNullOrEmpty(f.CameraId) ? "未绑定" : f.CameraId));
        }

        private void OnCameraManagerChanged(object sender, EventArgs e)
        {
            if (IsDisposed || Disposing) return;
            // 相机管理列表/打开状态变化后，立即刷新绑定下拉，避免「读不到相机管理列表、未刷新」
            RefreshCameraBindings();
        }

        private void OnViewIndexChanged(object sender, EventArgs e)
        {
            if (_loading) return;

            FlowDefinition f = CurrentFlow;
            if (f == null) return;

            f.ViewIndex = (int)numViewIndex.Value;
            SaveFlows();
            SetStatus("视图绑定：" + f.ViewIndex);
        }

        #endregion

        #region 校验与运行

        private static ICamera FindOpenedCamera(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (ICamera cam in CameraManager.Instance.OpenedCameras)
            {
                if (cam != null && cam.Info != null && cam.Info.Id == id) return cam;
            }
            return null;
        }

        /// <summary>视图序号越界提示（A7：跳过显示并提示，不自动改写绑定）；合法返回 null。</summary>
        private static string CheckView(FlowDefinition f)
        {
            int total = MainForm.ActiveViewCount;
            if (total < 1) return null;   // 无法得知视图数时放行

            if (f.ViewIndex < 1 || f.ViewIndex > total)
            {
                return string.Format(
                    "流程绑定的显示视图（{0}）超出当前布局视图数（{1}）。\r\n本次将跳过结果显示，且不会自动改写绑定；" +
                    "请在下方把「显示视图」改为 1~{1} 之间的序号。", f.ViewIndex, total);
            }
            return null;
        }

        private void OnValidate(object sender, EventArgs e)
        {
            FlowDefinition f = CurrentFlow;
            if (f == null) { MessageBox.Show(this, "没有可校验的流程。"); return; }

            string chainErr;
            bool chainOk = FlowRunner.Validate(f, out chainErr);

            string camErr = string.IsNullOrEmpty(f.CameraId)
                ? "未绑定相机"
                : (FindOpenedCamera(f.CameraId) == null ? ("绑定的相机（" + f.CameraId + "）未打开") : null);

            string viewErr = CheckView(f);

            var sb = new StringBuilder();
            sb.AppendLine(chainOk ? "单链拓扑：通过" : ("单链拓扑：不通过 —— " + chainErr));
            sb.AppendLine("相机：" + (camErr == null ? "正常（已打开）" : camErr));
            sb.AppendLine("视图：" + (viewErr == null ? "正常" : ("越界，运行时会跳过显示（视图 " + f.ViewIndex + "）")));

            // A3：把 FlowValidator 的 Warning 级也一并展示（如 W005「位置修正上游无匹配节点」），
            //     让「校验能过、但运行不生效」的问题在点运行时之前就暴露。Error 级判定不变：
            //     无警告时图标逻辑与改造前逐字一致。
            List<FlowIssue> warnings = FlowValidator.Validate(f).Warnings;
            if (warnings.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("警告（不阻断运行，但对应节点可能不生效）：");
                foreach (FlowIssue w in warnings)
                {
                    if (w == null) continue;
                    sb.AppendLine("  · " + w.Message);
                }
            }

            MessageBox.Show(this, sb.ToString(), "流程校验",
                MessageBoxButtons.OK,
                (chainOk && camErr == null && warnings.Count == 0)
                    ? MessageBoxIcon.Information : MessageBoxIcon.Warning);

            SetStatus(chainOk ? "校验通过" : ("校验未通过：" + chainErr));
        }

        /// <summary>
        /// 运行前置检查：流程存在 → 执行器可用 → 单链拓扑合法 → 相机就绪（本地图片源可无相机）。
        /// 通过时返回流程并给出相机；不通过时已提示用户并返回 null。
        /// </summary>
        private FlowDefinition PrepareRun(out ICamera camera)
        {
            camera = null;

            // 开始运行：把"参数推导的示意布局"撤掉，避免和实测结果叠在一起分不清
            if (flowPreview != null) flowPreview.ClearNodeLayout();

            FlowDefinition f = CurrentFlow;
            if (f == null) { MessageBox.Show(this, "请先选择或新建一个流程。"); return null; }

            if (_runner == null || _controller == null)
            {
                MessageBox.Show(this, "流程执行器不可用（算法库未就绪），无法运行。", "无法运行",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }

            string err;
            if (!FlowRunner.Validate(f, out err))
            {
                MessageBox.Show(this, "无法运行：" + err, "流程校验", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                SetStatus("校验未通过：" + err);
                return null;
            }

            ICamera cam = FindOpenedCamera(f.CameraId);

            // 图像源选了「本地图片文件」时无需相机也能跑通整链（FR-ALG-01 验收①）
            if (cam == null && FlowRunner.NeedsCamera(f))
            {
                string msg = string.IsNullOrEmpty(f.CameraId)
                    ? "流程未绑定相机。请先在「绑定」处选择相机，并确保该相机已在「相机管理」中打开。"
                    : ("流程绑定的相机（" + f.CameraId + "）当前未打开。请先在「相机管理」中打开该相机。");
                MessageBox.Show(this, msg, "无法运行", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                SetStatus(msg);
                return null;
            }

            string viewWarn = CheckView(f);
            if (viewWarn != null)
                MessageBox.Show(this, viewWarn, "视图绑定越界", MessageBoxButtons.OK, MessageBoxIcon.Warning);

            camera = cam;
            return f;
        }

        /// <summary>运行按钮可用态：运行期间禁用「运行 / 单步 / 校验」，启用「停止」。</summary>
        private void SetRunButtons(bool busy, bool looping)
        {
            btnRun.Enabled = !busy;
            btnStep.Enabled = !busy;
            btnRunNode.Enabled = !busy;
            btnValidate.Enabled = !busy;
            btnStop.Enabled = busy;
            btnRunLoop.Text = looping ? "停止连续" : "连续运行";
        }

        private void OnRun(object sender, EventArgs e)
        {
            if (_running || _looping) return;

            // 运行与「画 ROI」互斥：处于画框状态时预览拉帧被 _roiPicking 挡住，运行结果
            // 永远不会显示（实测故障）。启动运行前先退出画框状态（未画完的框作废）。
            flowPreview.CancelRoiPick();

            ICamera cam;
            FlowDefinition f = PrepareRun(out cam);
            if (f == null) return;

            _running = true;
            _cancel = false;
            canvas.ClearNodeStatus();
            SetRunButtons(true, false);
            SetStatus("运行中…");

            // [预览诊断][临时] 定位「点运行预览无图」——定位后移除
            Logger.Info("[预览诊断] OnRun flow=" + f.Name + " cam=" + (cam != null ? cam.Info.Id : "null") +
                        " 节点数=" + f.Nodes.Count + " sel='" + (_selectedNode != null ? _selectedNode.Id : "") + "'");

            _controller.RunOnce(f, cam);
        }

        /// <summary>
        /// 「运行选中节点」：只跑到画布当前选中节点为止，预览该节点的处理结果，不跑下游。
        /// 未选中节点时按整链运行（等同「运行」）。
        /// </summary>
        private void OnRunNode(object sender, EventArgs e)
        {
            if (_running || _looping) return;

            // 同 OnRun：运行前退出画 ROI 状态，避免预览拉帧被挡
            flowPreview.CancelRoiPick();

            ICamera cam;
            FlowDefinition f = PrepareRun(out cam);
            if (f == null) return;

            string target = (_selectedNode != null) ? _selectedNode.Id : null;

            _running = true;
            _cancel = false;
            canvas.ClearNodeStatus();
            SetRunButtons(true, false);

            if (string.IsNullOrEmpty(target))
                SetStatus("未选中节点，按整链运行…");
            else
                SetStatus("运行到选中节点「" + (_selectedNode.Name ?? "") + "」…");

            _controller.RunTo(f, cam, target);
        }

        private void OnStep(object sender, EventArgs e)
        {
            if (_running || _looping) return;

            // 同 OnRun：单步前退出画 ROI 状态，避免预览拉帧被挡
            flowPreview.CancelRoiPick();

            ICamera cam;
            FlowDefinition f = PrepareRun(out cam);
            if (f == null) return;

            _running = true;
            _cancel = false;
            SetRunButtons(true, false);
            SetStatus("单步执行…");

            _controller.StepOnce(f, cam);
        }

        private void OnRunLoop(object sender, EventArgs e)
        {
            if (_looping) { StopLoop(); return; }
            if (_running) return;

            // 同 OnRun：连续运行前退出画 ROI 状态，避免预览拉帧被挡
            flowPreview.CancelRoiPick();

            ICamera cam;
            FlowDefinition f = PrepareRun(out cam);
            if (f == null) return;

            _looping = true;
            _cancel = false;
            canvas.ClearNodeStatus();
            SetRunButtons(true, true);
            SetStatus("连续运行中…");

            _controller.StartLoop(f, cam);
        }

        private void StopLoop()
        {
            if (_controller != null) _controller.StopLoop();
            _looping = false;
            btnRunLoop.Text = "连续运行";
            SetStatus("已请求停止连续运行（当前周期结束后停止）");
        }

        private void OnStop(object sender, EventArgs e)
        {
            if (_controller != null) _controller.StopLoop();

            _cancel = true;
            _looping = false;
            btnRunLoop.Text = "连续运行";
            SetStatus("已请求停止（单次运行在当前帧处理完成后结束）");
        }

        /// <summary>节点进度：刷新画布节点状态边框与耗时（工作线程 → BeginInvoke 切 UI）。</summary>
        /// <summary>按 Id 找当前流程里的节点（找不到返回 null）。</summary>
        private FlowNode FindNodeById(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId) || CurrentFlow == null || CurrentFlow.Nodes == null) return null;

            foreach (FlowNode n in CurrentFlow.Nodes)
            {
                if (n != null && string.Equals(n.Id, nodeId, StringComparison.Ordinal)) return n;
            }
            return null;
        }

        private void OnControllerProgress(object sender, FlowProgressEventArgs e)
        {
            FlowProgressSnapshot snap = e != null ? e.Snapshot : null;
            if (snap == null) return;
            if (IsDisposed || !IsHandleCreated) return;

            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    if (IsDisposed) return;

                    // 位置修正示教了基准 → 必须立刻落盘，否则下次运行又示教成 0 偏差
                    //（用户视角就是"位置修正时好时坏、坐标有时不变"）。
                    // 节点级快照也会带这个标志（单步执行没有链尾快照），所以按"值"去重：
                    // 连续运行时同一节点每帧都会报，不去重会每帧写一次配置。
                    if (snap.BaselineTaught)
                    {
                        FlowNode taught = FindNodeById(snap.BaselineNodeId);
                        string key = snap.BaselineNodeId + "|"
                                     + (taught != null ? taught.GetString("RefX", "") + "," +
                                                          taught.GetString("RefY", "") + "," +
                                                          taught.GetString("RefAngle", "") : "");

                        if (!string.Equals(key, _baselineSavedKey, StringComparison.Ordinal))
                        {
                            _baselineSavedKey = key;
                            SaveFlows();
                            SetStatus("已自动示教位置修正基准（基准 X/Y/角 已填入本节点）");

                            // 面板正显示这个节点 → 立刻刷新，让用户看到新填入的基准值
                            FlowNode showNode = nodeParamPanel.CurrentNode;
                            if (showNode != null && taught != null
                                && string.Equals(showNode.Id, taught.Id, StringComparison.Ordinal))
                            {
                                nodeParamPanel.SetNode(showNode);
                            }
                        }
                    }

                    // 【文案唯一出口】画布卡片摘要、状态栏、预览顶条、预览覆盖层、结果区
                    // 全部走 NodeResultText，任何一处都不要就地拼字符串 —— 否则"三处不一致"
                    // 会随着下一次改动重新长出来（见 Architecture §3.3 D3）。
                    int elapsed = snap.IsChainEnd ? snap.CycleMs : snap.ElapsedMs;
                    string primary = NodeResultText.Primary(snap.Type, snap.Metrics, elapsed, snap.Summary);
                    string compact = NodeResultText.Compact(snap.Type, snap.Metrics);

                    canvas.SetNodeStatus(snap.NodeId,
                                         FlowCanvasControl.ToRunState(snap.Status),
                                         elapsed,
                                         compact);

                    FeedResultPanel(snap);

                    if (snap.IsChainEnd)
                    {
                        // 状态栏带上"本周期最慢节点"：CPU 高时用户一眼看出瓶颈是谁，
                        // 不用猜"是不是形状匹配"。
                        string slow = "";
                        if (snap.SlowestNodeMs > 0 && !string.IsNullOrEmpty(snap.SlowestNodeName))
                            slow = string.Format("　|　瓶颈 {0} {1} ms", snap.SlowestNodeName, snap.SlowestNodeMs);

                        SetStatus(string.Format("运行完成（{0} ms）：{1}　{2}{3}{4}{5}",
                            snap.CycleMs,
                            snap.Passed ? "OK" : "NG",
                            snap.ChainSummary,
                            snap.Fps > 0.0 ? string.Format("　|　{0:F1} fps", snap.Fps) : "",
                            slow,
                            BuildHint));
                    }
                    else
                    {
                        // 与预览顶条逐字同源：节点名/状态/主判据/耗时都来自同一个 primary
                        SetStatus(string.Format("第 {0}/{1} 个节点：{2}　{3} · {4}",
                            snap.Index, snap.Total, snap.NodeName,
                            NodeResultText.Badge(snap.Status), primary));
                    }
                });
            }
            catch (Exception)
            {
                // 窗体已销毁：忽略
            }
        }

        /// <summary>
        /// 把运行快照喂给右栏「输出结果」面板。
        /// 右栏上半部分编辑的是<strong>当前选中节点</strong>的输入参数，故结果也优先跟随它：
        /// 选中了节点就只刷该节点的输出（同样避免一次运行里逐节点重建表格造成抖动）；
        /// 没选中节点则每个快照都刷，最终停在链尾结果上。
        /// </summary>
        private void FeedResultPanel(FlowProgressSnapshot snap)
        {
            if (resultPanel == null || snap == null) return;

            FlowNodeType type = NodeTypeOf(snap.NodeId);

            string sel = _selectedNode != null ? _selectedNode.Id : "";
            if (!string.IsNullOrEmpty(sel) && !string.Equals(snap.NodeId, sel, StringComparison.Ordinal))
            {
                // 看的是某个节点，但整链跑完了：连续运行下仍要实时刷新完整结果（矩形/阈值/类别等）
                if (snap.IsChainEnd) { resultPanel.Apply(snap, type); return; }
                return;
            }

            resultPanel.Apply(snap, type);
        }

        /// <summary>按节点 Id 查节点类型（结果区主判据文案要用它选字段）。</summary>
        private FlowNodeType NodeTypeOf(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId)) return FlowNodeType.ResultOutput;

            FlowDefinition f = CurrentFlow;
            if (f != null && f.Nodes != null)
            {
                foreach (FlowNode n in f.Nodes)
                {
                    if (n == null || !string.Equals(n.Id, nodeId, StringComparison.Ordinal)) continue;
                    FlowNodeType t;
                    if (n.TryGetType(out t)) return t;
                }
            }
            return FlowNodeType.ResultOutput;
        }

        /// <summary>
        /// 点击结果表格的某一行 → 在预览图像里突出对应图形。
        /// 用「节点 Id + 图形序号」而不是图形实例：连续运行时图形每周期都重建，实例引用撑不过下一帧。
        /// </summary>
        private void OnResultShapeSelected(string nodeId, int shapeIndex)
        {
            if (string.IsNullOrEmpty(nodeId) || shapeIndex < 0)
            {
                flowPreview.ClearHighlight();
                return;
            }

            // 预览里现在画的就是该节点的图形 → 直接高亮（这是"实时"路径，最常用）
            if (string.Equals(flowPreview.CurrentNodeId, nodeId, StringComparison.Ordinal)
                && flowPreview.CurrentOwnShapes)
            {
                flowPreview.SetHighlight(nodeId, shapeIndex);
                return;
            }

            // 否则该节点的帧已被后续节点覆盖（连续运行不留历史帧）：用 1/4 缩略帧回看。
            // 坐标换算是 ThumbGeometry 的事，这里只传原图坐标的图形。
            NodeResultCache c = resultPanel != null ? resultPanel.GetCache(nodeId) : null;
            if (c != null && c.Thumbnail != null)
            {
                flowPreview.ShowHistoricalFrame(c.Thumbnail, c.Shapes, nodeId, shapeIndex,
                    "历史帧（1/4 缩略）：" + c.NodeName);
                SetStatus("该节点的图像已被后续节点覆盖，正在用 1/4 缩略帧回看；要看全分辨率请点「运行本节点」。");
                return;
            }

            // 连缩略帧都没有：不许静默失败，给一句能照做的提示。
            // 两种原因：① 该节点本次运行没被点开过（缩略帧按需生成）；② 它还没跑过。
            flowPreview.SetHighlight(nodeId, shapeIndex);
            SetStatus("该节点本次运行未抓取缩略图，先在画布上选中它并点「运行本节点」即可回看。");
        }

        /// <summary>一次启动彻底结束（回到 Idle）：复位按钮；若关窗被挂起则自动关闭。</summary>
        private void OnControllerCompleted(object sender, EventArgs e)
        {
            if (IsDisposed || !IsHandleCreated) return;

            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    if (IsDisposed) return;

                    bool wasLooping = _looping;
                    _running = false;
                    _looping = false;
                    SetRunButtons(false, false);

                    if (wasLooping) SetStatus("连续运行已停止");

                    if (_closeWhenIdle)
                    {
                        try { Close(); }
                        catch (Exception) { }
                    }
                });
            }
            catch (Exception)
            {
                // 窗体已销毁：忽略
            }
        }

        /// <summary>
        /// 流程结果回传：把流程编排页发起的运行（运行 / 单步 / 连续）的最终结果转发到
        /// <see cref="FlowOutputChannel"/>，使主界面图像视图也能叠加显示矩形框 / 阈值 / 类别等结果。
        /// 帧释放契约与 MainForm.OnFlowRunnerOutput 完全一致：有订阅者时由主界面 OnFlowOutput
        /// 克隆帧并显示、在 finally 中释放原帧；无订阅者（主界面已关闭）时由本方法就地释放，避免泄漏。
        /// 连续运行每周期经此桥接一次，主视图即可实时刷新 YOLO 等节点产出（Issue 2）。
        /// </summary>
        private void OnRunnerOutput(object sender, FlowOutputEventArgs e)
        {
            if (e == null) return;

            // 预览面板走独立快照路径（_pending / _nodeSlot 持有链尾克隆帧），此处只负责把最终结果
            // 桥接给主视图。主视图会克隆帧、故转发方不可重复释放——与 Publish 的返回值契约一致。
            if (!FlowOutputChannel.Publish(e))
            {
                if (e.Frame != null) e.Frame.Dispose();
                if (e.DisplayFrame != null) e.DisplayFrame.Dispose();
            }
        }

        #endregion

        #region 辅助

        private void SaveFlows()
        {
            try { ConfigStore.Save(); }
            catch (Exception ex) { Logger.Warn("保存流程配置失败：" + ex.Message); }
        }

        /// <summary>
        /// 切换深浅色板后由 <see cref="UiThemeManager"/> 调用：重套本窗体。
        /// 画布 / 预览 / 参数面板三个自绘控件在 UiStyle 的跳过名单里，必须逐个点名。
        /// </summary>
        public void RefreshTheme()
        {
            UiStyle.ApplyForm(this);

            canvas.ApplyTheme();
            flowPreview.ApplyTheme();
            nodeParamPanel.ApplyTheme();
            if (resultPanel != null) resultPanel.ApplyTheme();

            // 语义色按钮：Kind 决定自绘填充色，重设一次即取到新色板
            btnRun.Kind = UiButtonKind.Start;
            btnRunLoop.Kind = UiButtonKind.Start;
            btnStep.Kind = UiButtonKind.Grab;
            btnRunNode.Kind = UiButtonKind.Grab;
            btnStop.Kind = UiButtonKind.Stop;
            btnDeleteFlow.Kind = UiButtonKind.Stop;

            Invalidate(true);
        }

        private void SetStatus(string text)
        {
            if (lblRunStatus == null) return;
            lblRunStatus.Text = text ?? "";
            // 多行报错（如原生 OpenCV 异常）可能很长：滚到顶部并显示完整文本
            lblRunStatus.SelectionStart = 0;
            lblRunStatus.ScrollToCaret();
        }

        /// <summary>简易文本输入对话框（避免引入额外依赖）。取消返回 null。</summary>
        private static string PromptText(IWin32Window owner, string title, string defaultValue)
        {
            using (var dlg = new Form())
            {
                dlg.Text = title;
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.ClientSize = new Size(320, 108);
                dlg.MinimizeBox = false;
                dlg.MaximizeBox = false;
                dlg.ShowInTaskbar = false;

                var lbl = new Label { Text = "名称：", Location = new Point(12, 20), AutoSize = true };
                var txt = new TextBox { Text = defaultValue ?? "", Location = new Point(64, 17), Width = 240 };
                var ok = new Button { Text = "确定", DialogResult = DialogResult.OK, Location = new Point(140, 60), Width = 70 };
                var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Location = new Point(226, 60), Width = 70 };

                dlg.Controls.Add(lbl);
                dlg.Controls.Add(txt);
                dlg.Controls.Add(ok);
                dlg.Controls.Add(cancel);
                dlg.AcceptButton = ok;
                dlg.CancelButton = cancel;

                return dlg.ShowDialog(owner) == DialogResult.OK ? txt.Text : null;
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);

            // 退订相机管理变更事件，避免本窗体被 CameraManager 静态单例持有而无法回收
            CameraManager.Instance.CurrentChanged -= OnCameraManagerChanged;

            // 先请求取消
            _cancel = true;

            // 连续运行 / 单步线程由控制器统一编排：先请求停止并 Join（共享知识 22.2）。
            // 仍不结束就取消本次关闭，等运行收尾后自动关闭，
            // 从而保证「运行线程未结束时绝不调用 _runner.Dispose()」。
            if (_controller != null)
            {
                _controller.StopLoop();

                bool finished;
                try { finished = _controller.Join(2000); }
                catch (Exception) { finished = false; }

                if (!finished)
                {
                    _closeWhenIdle = true;
                    e.Cancel = true;
                    SetStatus("正在结束当前运行，完成后将自动关闭…");
                    return;   // 不碰 _controller / _runner：不退订、不 Dispose
                }
            }

            Thread t = _runThread;
            if (t != null)
            {
                bool finished;
                try { finished = t.Join(2000); }
                catch (Exception) { finished = false; }

                if (!finished)
                {
                    _closeWhenIdle = true;
                    e.Cancel = true;
                    SetStatus("正在结束当前运行，完成后将自动关闭…");
                    return;   // 不碰 _runner：不退订、不 Dispose
                }

                _runThread = null;
            }

            // 预览区：停掉拉取定时器（帧由 ImageViewer 自身释放）
            flowPreview.Shutdown();

            if (_controller != null)
            {
                _controller.ProgressReady -= OnControllerProgress;
                _controller.OutputReady -= OnRunnerOutput;
                _controller.Completed -= OnControllerCompleted;
                _controller.Dispose();
                _controller = null;
            }

            if (_runner != null)
            {
                _runner.OutputReady -= OnRunnerOutput;
                _runner.Dispose();
            }
        }

        #endregion
    }
}
