using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using VisionPlatform.Camera;
using VisionPlatform.Core;
using VisionPlatform.Flow;

namespace VisionPlatform.UI
{
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
        private RoiGroup _roiGroup;     // 本次正在画的是哪一组 ROI（多组并存时由用户选）

        /// <summary>
        /// 每个节点上次画的是哪一组 ROI：key = 节点 Id，value = 该组的 X 参数键（组标识）。
        /// 必须带节点身份，否则切到别的节点后会拿旧组去查新节点的组，可能查错组甚至画错参数。
        /// </summary>
        private readonly Dictionary<string, string> _roiLastGroup = new Dictionary<string, string>();

        public FlowForm()
        {
            InitializeComponent();

            // 工具箱改 TreeView 后拖拽走 ItemDrag：分类节点 Tag=null 不可拖，
            // 仍只传 FlowModuleInfo.Key 字符串，FlowCanvasControl.OnDragDrop 零改动。
            treeModules.ItemDrag += OnModuleItemDrag;
            lstFlows.SelectedIndexChanged += OnFlowSelectionChanged;
            cmbCamera.SelectedIndexChanged += OnCameraBindingChanged;

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

            InitModules();
            RefreshPortTree(null);
            LoadFlows();

            // 统一深色工程配色（canvas / preview / nodeParamPanel 自绘，已跳过）
            UiStyle.ApplyForm(this);

            // 语义色：运行/连续运行=绿，单步=蓝，停止/删除=红；其余中性
            btnRun.Kind = UiButtonKind.Start;
            btnRunLoop.Kind = UiButtonKind.Start;
            btnStep.Kind = UiButtonKind.Grab;
            btnRunNode.Kind = UiButtonKind.Grab;
            btnStop.Kind = UiButtonKind.Stop;
            btnDeleteFlow.Kind = UiButtonKind.Stop;

            // 「画 ROI」按钮在预览区顶条（FlowPreviewPanel 内，与「载入图片」并排在其左侧）：
            // 它作用于预览图，放在预览区语义更贴切，也避免挤在「绑定与运行」分组里。
            flowPreview.RoiPicked += OnRoiPicked;
            flowPreview.RoiPickRequested += OnRoiPickRequested;
            flowPreview.RoiPickCancelled += OnRoiPickCancelled;
        }

        #region 初始化

        /// <summary>
        /// 工具箱按类别建树：分类=根节点（<c>Tag=null</c> ⇒ 不可拖），模块=叶子（<c>Tag=FlowModuleInfo</c>）。
        /// 分类顺序由 <see cref="FlowModule.GroupByCategory"/> 给出（采集→处理→定位→测量→拟合→输出，= 数据流方向）。
        /// </summary>
        private void InitModules()
        {
            treeModules.BeginUpdate();
            treeModules.Nodes.Clear();

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
                    root.Nodes.Add(leaf);
                }

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
            if (treePorts == null) return;

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
            nodeParamPanel.SetNode(null);
            RefreshPortTree(null);          // 换流程：端子树回到「未选中节点」
            flowPreview.SetSelectedNodeId("");
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

        private void OnCanvasNodeSelected(object sender, FlowNodeEventArgs e)
        {
            _selectedNode = (e != null) ? e.Node : null;

            nodeParamPanel.SetNode(e.Node);

            // 预览「选中节点」模式据此过滤该节点自身的输出
            flowPreview.SetSelectedNodeId(e != null && e.Node != null ? e.Node.Id : "");

            RefreshPortTree(_selectedNode);
            UpdateRoiButton();
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
        }

        /// <summary>
        /// 当前编辑节点是「图像源」且取图方式为「本地图片文件」、路径有效时，
        /// 立即把该图显示在预览区（不运行流程）。
        /// </summary>
        private void PreviewImageSourceFile()
        {
            FlowNode node = nodeParamPanel.Node;
            if (!IsFileImageSource(node)) return;

            string path = node.GetString("FilePath", "");
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;

            string error;
            Bitmap bmp = FlowPreviewPanel.LoadImageFromFile(path, out error);
            if (bmp == null) return;

            // 交给视图后不得再 Dispose（OwnsImage=true）
            flowPreview.ShowStaticImage(bmp, "图像源：" + Path.GetFileName(path));
            SetStatus("已载入图像源图片：" + path);
        }

        /// <summary>
        /// 从流程里第一个「本地图片」图像源节点载图到预览区（当前编辑节点不是图像源时的兜底）。
        /// 载图成功返回 true。
        /// </summary>
        private bool LoadFirstFileImageSource()
        {
            FlowDefinition f = CurrentFlow;
            if (f == null || f.Nodes == null) return false;

            foreach (FlowNode n in f.Nodes)
            {
                if (n == null || !IsFileImageSource(n)) continue;

                string path = n.GetString("FilePath", "");
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) continue;

                string error;
                Bitmap bmp = FlowPreviewPanel.LoadImageFromFile(path, out error);
                if (bmp == null) continue;

                flowPreview.ShowStaticImage(bmp, "图像源：" + Path.GetFileName(path));
                SetStatus("已载入图像源图片：" + path);
                return true;
            }
            return false;
        }

        #region 画 ROI（B）

        /// <summary>一组「矩形 ROI」四参数键（X / Y / W / H）及其显示名（取自参数表分组标题）。</summary>
        private sealed class RoiGroup
        {
            public readonly string X;
            public readonly string Y;
            public readonly string W;
            public readonly string H;
            public readonly string Label;

            public RoiGroup(string x, string y, string w, string h, string label)
            {
                X = x; Y = y; W = w; H = h; Label = label;
            }
        }

        /// <summary>
        /// 候选矩形 ROI 参数组。<strong>不硬编码节点类型</strong>：按参数表实际字段匹配，
        /// 哪个节点真的有这四个键就对它可用（ImageSource = ROI 裁剪；
        /// ShapeMatch / TemplateMatch = 模板区域 + 搜索区域，两组并存时由用户选）。
        /// 显示名优先取参数表里最近的分组标题（去掉括号里的补充说明），取不到才用这里的兜底名。
        /// <para>
        /// 【数组顺序 = 优先级】模板/训练区域 &gt; ROI 裁剪 &gt; 搜索区域。
        /// 理由：ShapeMatch / TemplateMatch 的训练区域是算法的「输入定义」，没画就没法建模，
        /// 是第一步必做的事；搜索区域默认整图即可，属于后续调优。故无历史选择时取第 0 项。
        /// </para>
        /// <para>
        /// 【圆环 ROI 不做】圆卡尺 / 圆环卡尺参数表里的「环形 ROI」（Cx / Cy / 内径 / 外径）
        /// 是圆环带而非矩形，暂不支持；当前矩形即搜索区域 —— 与 VisionPro FindCircle 语义一致
        /// （在矩形范围内找圆 / 圆环），因此不是缺陷、也不是遗漏。
        /// </para>
        /// </summary>
        private static readonly RoiGroup[] RoiCandidates =
        {
            new RoiGroup("TemplateX", "TemplateY", "TemplateW", "TemplateH", "模板区域"),
            new RoiGroup("RoiX", "RoiY", "RoiW", "RoiH", "ROI 裁剪"),
            new RoiGroup("SearchX", "SearchY", "SearchW", "SearchH", "搜索区域")
        };

        /// <summary>候选组在 <see cref="RoiCandidates"/> 中的序号（即优先级）；不在表中时排最后。</summary>
        private static int RoiCandidateOrder(RoiGroup g)
        {
            for (int i = 0; i < RoiCandidates.Length; i++)
                if (string.Equals(RoiCandidates[i].X, g.X, StringComparison.Ordinal)) return i;
            return int.MaxValue;
        }

        /// <summary>分组标题去掉括号补充说明，得到菜单里的短名（「搜索区域（宽或高为 0 = 整图）」→「搜索区域」）。</summary>
        private static string ShortGroupName(string groupTitle)
        {
            if (string.IsNullOrEmpty(groupTitle)) return "";

            int i = groupTitle.IndexOf('（');
            if (i < 0) i = groupTitle.IndexOf('(');
            string s = i >= 0 ? groupTitle.Substring(0, i) : groupTitle;
            return s.Trim();
        }

        /// <summary>
        /// 按参数表实际字段找出该节点的<strong>所有</strong>矩形 ROI 参数组（顺序与参数面板一致）。
        /// 没有则返回空列表。
        /// </summary>
        private static List<RoiGroup> FindRoiGroups(FlowNode node)
        {
            var result = new List<RoiGroup>();
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

                foreach (RoiGroup g in RoiCandidates)
                {
                    if (!string.Equals(f.Key, g.X, StringComparison.Ordinal)) continue;
                    if (!HasKey(table, g.Y) || !HasKey(table, g.W) || !HasKey(table, g.H)) continue;

                    string label = string.IsNullOrEmpty(groupTitle) ? g.Label : groupTitle;
                    // 同一节点不会重复，但兜底去重，避免菜单出现同名项
                    if (!result.Exists(x => string.Equals(x.X, g.X, StringComparison.Ordinal)))
                        result.Add(new RoiGroup(g.X, g.Y, g.W, g.H, label));
                }
            }

            // 按候选优先级排序：模板/训练区域在前，搜索区域在后（第 0 项即默认组）
            result.Sort(delegate (RoiGroup a, RoiGroup b)
            {
                return RoiCandidateOrder(a).CompareTo(RoiCandidateOrder(b));
            });
            return result;
        }

        private static bool HasKey(NodeParamTable table, string key)
        {
            return table.Fields.Exists(x => x != null && string.Equals(x.Key, key, StringComparison.Ordinal));
        }

        /// <summary>菜单项文字：组名 + 当前值，例如「搜索区域（120,80 240×160）」；未设置显示「（未设置）」。</summary>
        private static string DescribeRoiGroup(FlowNode node, RoiGroup g)
        {
            RectangleF r = ReadRoiRect(node, g);
            string value = (r.Width > 0f && r.Height > 0f)
                ? string.Format("{0},{1} {2}×{3}",
                    (int)Math.Round(r.X), (int)Math.Round(r.Y),
                    (int)Math.Round(r.Width), (int)Math.Round(r.Height))
                : "未设置";

            return g.Label + "（" + value + "）";
        }

        /// <summary>读出节点当前的 ROI 矩形（图像坐标；未设置时宽高为 0）。</summary>
        private static RectangleF ReadRoiRect(FlowNode node, RoiGroup k)
        {
            return new RectangleF(
                (float)node.GetDouble(k.X, 0.0),
                (float)node.GetDouble(k.Y, 0.0),
                (float)node.GetDouble(k.W, 0.0),
                (float)node.GetDouble(k.H, 0.0));
        }

        /// <summary>「画 ROI」按钮可用态：仅当选中节点确实有矩形 ROI 参数时可用。</summary>
        private void UpdateRoiButton()
        {
            flowPreview.SetRoiEnabled(FindRoiGroups(_selectedNode ?? nodeParamPanel.Node).Count > 0);
        }

        /// <summary>
        /// 点预览区顶条的「画 ROI」：单组直接进拖框；<strong>多组每次都弹菜单</strong>
        /// （不沿用历史直接进，否则用户想换组只能重选节点或重启，等于功能废一半）。
        /// 历史只用来把上次选的组排到第一项并加 ✓，让「沿用」退化为一次点击。
        /// </summary>
        private void OnRoiPickRequested(object sender, EventArgs e)
        {
            FlowNode node = _selectedNode ?? nodeParamPanel.Node;
            if (node == null)
            {
                MessageBox.Show(this, "请先在画布上选中一个节点，再画 ROI。", "画 ROI",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            List<RoiGroup> groups = FindRoiGroups(node);
            if (groups.Count == 0)
            {
                MessageBox.Show(this, "节点「" + node.Name + "」没有可绘制的 ROI 参数，无需画 ROI。", "画 ROI",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // 单组直接进拖框；≥2 组每次都弹菜单（随时可换组）
            if (groups.Count == 1) { StartRoiPick(node, groups[0]); return; }

            ShowRoiGroupMenu(node, groups);
        }

        /// <summary>
        /// 在「画 ROI」按钮下方弹出 ROI 组选择菜单。
        /// 该节点上次选过的组排在第一项并加 <c>✓</c> 前缀，其余保持原顺序。
        /// </summary>
        private void ShowRoiGroupMenu(FlowNode node, List<RoiGroup> groups)
        {
            Control anchor = flowPreview.RoiPickAnchor;
            if (anchor == null) { StartRoiPick(node, groups[0]); return; }

            // 把上次选的组提到最前（保持其余组的相对顺序，故不用 Sort）
            var ordered = new List<RoiGroup>();
            string last;
            _roiLastGroup.TryGetValue(node.Id, out last);

            if (!string.IsNullOrEmpty(last))
            {
                RoiGroup first = groups.Find(g => string.Equals(g.X, last, StringComparison.Ordinal));
                if (first != null) ordered.Add(first);
            }
            foreach (RoiGroup g in groups)
                if (!ordered.Contains(g)) ordered.Add(g);

            var menu = new ContextMenuStrip();
            for (int i = 0; i < ordered.Count; i++)
            {
                RoiGroup picked = ordered[i];   // 闭包捕获
                bool isLast = !string.IsNullOrEmpty(last)
                              && string.Equals(picked.X, last, StringComparison.Ordinal);

                var item = new ToolStripMenuItem((isLast ? "✓ " : "") + DescribeRoiGroup(node, picked));
                item.Click += delegate { StartRoiPick(node, picked); };
                menu.Items.Add(item);
            }

            // ⚠️ 菜单是一次性的，但【不要】在 Closed 里立刻 Dispose：
            // 点菜单项会走 StartRoiPick → BeginRoiPick → viewer.Focus()，Focus 抢走焦点会让
            // 菜单在它自身的「点击→关闭」流程里被关闭，此时立刻 Dispose 会与 ToolStrip 内部的
            // 句柄访问（SetVisibleCore→ReparentToDropDownOwnerWindow→get_Handle）并发，
            // 抛 ObjectDisposedException「无法访问已释放的对象」（已从线上日志真实抓到）。
            // 这里交给 GC 回收即可（菜单连同子项都是短命对象，无泄漏风险）。
            menu.Show(anchor, new Point(0, anchor.Height));
        }

        /// <summary>进入拖框（先确保有图，再记住本次画的是哪一组）。</summary>
        private void StartRoiPick(FlowNode node, RoiGroup group)
        {
            // 没有图像就拖不了框：先按「图像源」节点的本地图片自动载入一张
            if (!flowPreview.HasImage) PreviewImageSourceFile();

            // 兜底：当前编辑的不是图像源节点时，改从流程里任意一个「本地图片」图像源载图。
            // 否则用户在选中「模板匹配」这类节点时点「画 ROI」会卡在「预览区还没有图像」。
            if (!flowPreview.HasImage) LoadFirstFileImageSource();

            if (!flowPreview.HasImage)
            {
                MessageBox.Show(this,
                    "预览区还没有图像。请先点预览区的「载入图片」或运行一次流程，再画 ROI。",
                    "画 ROI", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _roiGroup = group;   // 记住当前画的是哪一组，OnRoiPicked 据此写回

            // 记住该节点的选择：下次点「画 ROI」直接沿用，不再弹菜单
            if (!string.IsNullOrEmpty(node.Id)) _roiLastGroup[node.Id] = group.X;
            flowPreview.BeginRoiPick(ReadRoiRect(node, group), "画 ROI：" + group.Label);
            SetStatus("请在预览区图像上拖动绘制「" + group.Label + "」，按 Esc 取消");
        }

        /// <summary>拖框完成：把图像坐标的矩形写回用户选中的那一组参数并持久化。</summary>
        private void OnRoiPicked(object sender, RectangleF rect)
        {
            FlowNode node = _selectedNode ?? nodeParamPanel.Node;
            if (node == null) return;

            RoiGroup keys = _roiGroup;
            if (keys == null)
            {
                // 兜底：没有记录组时取该节点的第一组
                List<RoiGroup> groups = FindRoiGroups(node);
                if (groups.Count == 0) return;
                keys = groups[0];
            }

            int x = (int)Math.Round(rect.X);
            int y = (int)Math.Round(rect.Y);
            int w = (int)Math.Round(rect.Width);
            int h = (int)Math.Round(rect.Height);
            if (x < 0) x = 0;
            if (y < 0) y = 0;
            if (w < 0) w = 0;
            if (h < 0) h = 0;

            node.Set(keys.X, x);
            node.Set(keys.Y, y);
            node.Set(keys.W, w);
            node.Set(keys.H, h);

            // 参数面板正显示该节点时同步回填，避免界面与配置不一致
            FlowNode shown = nodeParamPanel.Node;
            if (shown != null && shown.Id == node.Id) nodeParamPanel.SetNode(node);

            SaveFlows();
            SetStatus(string.Format("已更新 ROI（{0}）：X={1}, Y={2}, {3}×{4}；{5}",
                keys.Label, x, y, w, h, OverlayClearedHint));

            _roiGroup = null;
        }

        /// <summary>
        /// 清掉已删除节点的 ROI 组历史，避免字典无限增长。
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

        /// <summary>取消画 ROI：叠加图形同样被清掉了，提示一句免得用户以为是 bug。</summary>
        private void OnRoiPickCancelled(object sender, EventArgs e)
        {
            _roiGroup = null;
            SetStatus("已取消画 ROI；" + OverlayClearedHint);
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
            if (shown != null && shown.Id == src.Id) nodeParamPanel.SetNode(src);

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

        /// <summary>节点是「图像源」且取图方式为「本地图片文件」时返回 true。</summary>
        private static bool IsFileImageSource(FlowNode node)
        {
            if (!IsImageSource(node)) return false;

            return string.Equals(node.GetString("Source", "Camera"), "File",
                                 StringComparison.OrdinalIgnoreCase);
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

                var opened = CameraManager.Instance.OpenedCameras;
                bool found = string.IsNullOrEmpty(want);
                foreach (ICamera cam in opened)
                {
                    if (cam == null || cam.Info == null) continue;
                    cmbCamera.Items.Add(new BindItem(cam.Info.Id, cam.Info.ToString()));
                    if (cam.Info.Id == want) found = true;
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

            MessageBox.Show(this, sb.ToString(), "流程校验",
                MessageBoxButtons.OK,
                (chainOk && camErr == null) ? MessageBoxIcon.Information : MessageBoxIcon.Warning);

            SetStatus(chainOk ? "校验通过" : ("校验未通过：" + chainErr));
        }

        /// <summary>
        /// 运行前置检查：流程存在 → 执行器可用 → 单链拓扑合法 → 相机就绪（本地图片源可无相机）。
        /// 通过时返回流程并给出相机；不通过时已提示用户并返回 null。
        /// </summary>
        private FlowDefinition PrepareRun(out ICamera camera)
        {
            camera = null;

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

            ICamera cam;
            FlowDefinition f = PrepareRun(out cam);
            if (f == null) return;

            _running = true;
            _cancel = false;
            canvas.ClearNodeStatus();
            SetRunButtons(true, false);
            SetStatus("运行中…");

            _controller.RunOnce(f, cam);
        }

        /// <summary>
        /// 「运行选中节点」：只跑到画布当前选中节点为止，预览该节点的处理结果，不跑下游。
        /// 未选中节点时按整链运行（等同「运行」）。
        /// </summary>
        private void OnRunNode(object sender, EventArgs e)
        {
            if (_running || _looping) return;

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

                    canvas.SetNodeStatus(snap.NodeId,
                                         FlowCanvasControl.ToRunState(snap.Status),
                                         snap.IsChainEnd ? snap.CycleMs : snap.ElapsedMs);

                    if (snap.IsChainEnd)
                    {
                        SetStatus(string.Format("运行完成（{0} ms）：{1}　{2}{3}",
                            snap.CycleMs,
                            snap.Passed ? "OK" : "NG",
                            snap.ChainSummary,
                            snap.Fps > 0.0 ? string.Format("　|　{0:F1} fps", snap.Fps) : ""));
                    }
                    else
                    {
                        SetStatus(string.Format("第 {0}/{1} 个节点：{2}　{3} ms",
                            snap.Index, snap.Total, snap.NodeName, snap.ElapsedMs));
                    }
                });
            }
            catch (Exception)
            {
                // 窗体已销毁：忽略
            }
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

        /// <summary>流程结果回传：取消时丢弃帧，否则转发给通道（由主界面消费并释放帧）。</summary>
        private void OnRunnerOutput(object sender, FlowOutputEventArgs e)
        {
            if (_cancel)
            {
                if (e.Frame != null) e.Frame.Dispose();
                return;
            }

            // 无订阅者（主界面已关闭）时由发布方释放，避免帧泄漏
            if (!FlowOutputChannel.Publish(e) && e.Frame != null)
                e.Frame.Dispose();
        }

        #endregion

        #region 辅助

        private void SaveFlows()
        {
            try { ConfigStore.Save(); }
            catch (Exception ex) { Logger.Warn("保存流程配置失败：" + ex.Message); }
        }

        private void SetStatus(string text)
        {
            if (lblRunStatus != null) lblRunStatus.Text = text;
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
