namespace VisionPlatform.UI
{
    partial class MainForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// 控件树与布局。
        /// 【铁律】本文件只写结构与尺寸，颜色 / 字体一律在 MainForm.ApplyTheme() 里
        /// 统一套用（主题单一来源 UiTheme），因此设计器中看不到真实配色（已知代价）。
        /// 右栏自下而上四区块：①判定徽标 ②统计卡片 ③运行控制大按钮 ④运行状态（Fill 自动收缩）。
        /// 注意 WinForms 停靠按集合逆序生效：先加的控件最终位于下方。
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.toolStrip = new System.Windows.Forms.ToolStrip();
            this.tsLabelCamera = new System.Windows.Forms.ToolStripLabel();
            this.tsComboCameras = new System.Windows.Forms.ToolStripComboBox();
            this.tsBtnRefresh = new System.Windows.Forms.ToolStripButton();
            this.tsBtnGrabOnce = new System.Windows.Forms.ToolStripButton();
            this.tsBtnLive = new System.Windows.Forms.ToolStripButton();
            this.tsBtnSyncGrab = new System.Windows.Forms.ToolStripButton();
            this.tsSep1 = new System.Windows.Forms.ToolStripSeparator();
            this.tsLabelLayout = new System.Windows.Forms.ToolStripLabel();
            this.tsComboLayout = new System.Windows.Forms.ToolStripComboBox();
            this.tsBtnRightPanel = new System.Windows.Forms.ToolStripButton();
            this.tsSep2 = new System.Windows.Forms.ToolStripSeparator();
            this.tsLabelFlow = new System.Windows.Forms.ToolStripLabel();
            this.tsComboFlow = new System.Windows.Forms.ToolStripComboBox();
            this.tsBtnCameraMgr = new System.Windows.Forms.ToolStripButton();
            this.tsBtnAlgo = new System.Windows.Forms.ToolStripButton();
            this.tsBtnFlow = new System.Windows.Forms.ToolStripButton();
            this.tsBtnComm = new System.Windows.Forms.ToolStripButton();
            this.tsBtnRecipe = new System.Windows.Forms.ToolStripButton();
            this.tsBtnSettings = new System.Windows.Forms.ToolStripButton();
            this.tsSep3 = new System.Windows.Forms.ToolStripSeparator();
            this.tsLabelStatus = new System.Windows.Forms.ToolStripLabel();

            this.panelCenter = new System.Windows.Forms.Panel();
            this.tableViews = new System.Windows.Forms.TableLayoutPanel();

            this.panelRight = new System.Windows.Forms.Panel();
            this.panelVerdict = new System.Windows.Forms.Panel();
            this.lblVerdict = new System.Windows.Forms.Label();
            this.groupStats = new System.Windows.Forms.GroupBox();
            this.tableStats = new System.Windows.Forms.TableLayoutPanel();
            this.lblTotalCap = new System.Windows.Forms.Label();
            this.lblTotalValue = new System.Windows.Forms.Label();
            this.lblOkCap = new System.Windows.Forms.Label();
            this.lblOkValue = new System.Windows.Forms.Label();
            this.lblNgCap = new System.Windows.Forms.Label();
            this.lblNgValue = new System.Windows.Forms.Label();
            this.lblYieldCap = new System.Windows.Forms.Label();
            this.lblYieldValue = new System.Windows.Forms.Label();
            this.lblCycleCap = new System.Windows.Forms.Label();
            this.lblCycleValue = new System.Windows.Forms.Label();
            this.groupFlowStats = new System.Windows.Forms.GroupBox();
            this.flowScroll = new System.Windows.Forms.Panel();
            this.tableFlowStats = new System.Windows.Forms.TableLayoutPanel();
            this.lblFlowHdrName = new System.Windows.Forms.Label();
            this.lblFlowHdrOk = new System.Windows.Forms.Label();
            this.lblFlowHdrNg = new System.Windows.Forms.Label();
            this.lblFlowHdrRate = new System.Windows.Forms.Label();
            this.panelRunCtrl = new System.Windows.Forms.Panel();
            this.btnLoop = new VisionPlatform.UI.UiButton();
            this.btnPause = new VisionPlatform.UI.UiButton();
            this.btnStop = new VisionPlatform.UI.UiButton();
            this.btnGrabOnce = new VisionPlatform.UI.UiButton();
            this.btnResetStats = new VisionPlatform.UI.UiButton();
            this.panelRunState = new System.Windows.Forms.Panel();
            this.tableRunState = new System.Windows.Forms.TableLayoutPanel();
            this.lblRunStateCap = new System.Windows.Forms.Label();
            this.lblRunStateValue = new System.Windows.Forms.Label();
            this.lblCameraCap = new System.Windows.Forms.Label();
            this.lblCameraValue = new System.Windows.Forms.Label();
            this.lblFlowCap = new System.Windows.Forms.Label();
            this.lblFlowValue = new System.Windows.Forms.Label();
            this.lblLastCap = new System.Windows.Forms.Label();
            this.lblLastValue = new System.Windows.Forms.Label();
            this.lblConsecutiveNg = new System.Windows.Forms.Label();

            this.panelBottom = new System.Windows.Forms.Panel();
            this.btnFoldLog = new System.Windows.Forms.Button();
            this.listLog = new System.Windows.Forms.ListBox();

            this.statusStrip = new System.Windows.Forms.StatusStrip();
            this.statusLabel = new System.Windows.Forms.ToolStripStatusLabel();

            this.loopTimer = new System.Windows.Forms.Timer(this.components);

            this.toolStrip.SuspendLayout();
            this.panelCenter.SuspendLayout();
            this.panelRight.SuspendLayout();
            this.panelVerdict.SuspendLayout();
            this.groupStats.SuspendLayout();
            this.tableStats.SuspendLayout();
            this.groupFlowStats.SuspendLayout();
            this.flowScroll.SuspendLayout();
            this.tableFlowStats.SuspendLayout();
            this.panelRunCtrl.SuspendLayout();
            this.panelRunState.SuspendLayout();
            this.tableRunState.SuspendLayout();
            this.panelBottom.SuspendLayout();
            this.statusStrip.SuspendLayout();
            this.SuspendLayout();

            // ---------- toolStrip ----------
            // 分组：采集组 | 运行组 | 布局组 | 功能组 | 右侧状态位；组间以分隔符区分。
            // 运行组保留在工具栏，用于与右栏大按钮「同一状态源、行为严格同步」。
            this.toolStrip.BackColor = System.Drawing.Color.FromArgb(30, 30, 38);
            this.toolStrip.ForeColor = System.Drawing.Color.Gainsboro;
            this.toolStrip.GripStyle = System.Windows.Forms.ToolStripGripStyle.Hidden;
            this.toolStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
                this.tsLabelCamera,
                this.tsComboCameras,
                this.tsBtnRefresh,
                this.tsBtnGrabOnce,
                this.tsBtnLive,
                this.tsBtnSyncGrab,
                this.tsSep1,
                this.tsLabelLayout,
                this.tsComboLayout,
                this.tsBtnRightPanel,
                this.tsSep2,
                this.tsLabelFlow,
                this.tsComboFlow,
                this.tsBtnCameraMgr,
                this.tsBtnAlgo,
                this.tsBtnFlow,
                this.tsBtnComm,
                this.tsBtnRecipe,
                this.tsBtnSettings,
                this.tsSep3,
                this.tsLabelStatus});
            this.toolStrip.Location = new System.Drawing.Point(0, 0);
            this.toolStrip.Name = "toolStrip";
            this.toolStrip.Size = new System.Drawing.Size(1440, 40);
            this.toolStrip.TabIndex = 0;

            this.tsLabelCamera.Name = "tsLabelCamera";
            this.tsLabelCamera.Text = "相机:";

            this.tsComboCameras.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.tsComboCameras.Name = "tsComboCameras";
            this.tsComboCameras.Size = new System.Drawing.Size(150, 32);
            this.tsComboCameras.SelectedIndexChanged += new System.EventHandler(this.OnCameraSelectionChanged);

            this.tsBtnRefresh.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.tsBtnRefresh.Name = "tsBtnRefresh";
            this.tsBtnRefresh.Text = "刷新";
            this.tsBtnRefresh.Click += new System.EventHandler(this.OnRefreshCameras);

            this.tsBtnGrabOnce.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.tsBtnGrabOnce.Name = "tsBtnGrabOnce";
            this.tsBtnGrabOnce.Text = "单帧采集";
            this.tsBtnGrabOnce.ToolTipText = "对当前相机采集一帧并检测";
            this.tsBtnGrabOnce.Click += new System.EventHandler(this.OnGrabOnce);

            this.tsBtnLive.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.tsBtnLive.Name = "tsBtnLive";
            this.tsBtnLive.Text = "连续预览";
            this.tsBtnLive.ToolTipText = "连续实时预览当前相机（再点一次停止）";
            this.tsBtnLive.Click += new System.EventHandler(this.OnToggleLive);

            this.tsBtnSyncGrab.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.tsBtnSyncGrab.Name = "tsBtnSyncGrab";
            this.tsBtnSyncGrab.Text = "同步采集";
            this.tsBtnSyncGrab.ToolTipText = "对所有已打开相机同时取帧（同步拍摄）";
            this.tsBtnSyncGrab.Click += new System.EventHandler(this.OnSyncGrab);

            this.tsSep1.Name = "tsSep1";

            this.tsLabelLayout.Name = "tsLabelLayout";
            this.tsLabelLayout.Text = "布局:";

            this.tsComboLayout.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.tsComboLayout.Name = "tsComboLayout";
            this.tsComboLayout.Size = new System.Drawing.Size(90, 32);
            this.tsComboLayout.ToolTipText = "显示控件布局（行 x 列），切换后每个视图可单独绑定相机";
            this.tsComboLayout.SelectedIndexChanged += new System.EventHandler(this.OnLayoutChanged);

            this.tsBtnRightPanel.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.tsBtnRightPanel.Name = "tsBtnRightPanel";
            this.tsBtnRightPanel.Text = "折叠右栏";
            this.tsBtnRightPanel.ToolTipText = "折叠 / 展开右侧栏以扩大图像显示区";
            this.tsBtnRightPanel.Click += new System.EventHandler(this.OnToggleRightPanel);

            this.tsSep2.Name = "tsSep2";

            this.tsLabelFlow.Name = "tsLabelFlow";
            this.tsLabelFlow.Text = "流程:";

            this.tsComboFlow.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.tsComboFlow.Name = "tsComboFlow";
            this.tsComboFlow.Size = new System.Drawing.Size(160, 32);
            this.tsComboFlow.ToolTipText = "选择流程后：选一张本地图片运行该流程，结果显示在主视图";
            this.tsComboFlow.SelectedIndexChanged += new System.EventHandler(this.OnFlowComboSelected);

            this.tsBtnCameraMgr.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.tsBtnCameraMgr.Name = "tsBtnCameraMgr";
            this.tsBtnCameraMgr.Text = "相机管理";
            this.tsBtnCameraMgr.Click += new System.EventHandler(this.OnOpenCameraForm);

            this.tsBtnAlgo.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.tsBtnAlgo.Name = "tsBtnAlgo";
            this.tsBtnAlgo.Text = "形状匹配调试";
            this.tsBtnAlgo.ToolTipText = "离线单图形状匹配工作台（载入本地图片 / 取帧调试）";
            this.tsBtnAlgo.Visible = false;   // 用户要求隐藏（保留字段 / 工具栏项 / 事件，随时可恢复）
            this.tsBtnAlgo.Click += new System.EventHandler(this.OnOpenAlgoForm);

            this.tsBtnFlow.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.tsBtnFlow.Name = "tsBtnFlow";
            this.tsBtnFlow.Text = "流程编排";
            this.tsBtnFlow.ToolTipText = "算法流程编排：拖拽节点、连线、绑定相机与视图、运行";
            this.tsBtnFlow.Click += new System.EventHandler(this.OnOpenFlowForm);

            this.tsBtnComm.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.tsBtnComm.Name = "tsBtnComm";
            this.tsBtnComm.Text = "通讯";
            this.tsBtnComm.Click += new System.EventHandler(this.OnOpenCommForm);

            this.tsBtnRecipe.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.tsBtnRecipe.Name = "tsBtnRecipe";
            this.tsBtnRecipe.Text = "配方";
            this.tsBtnRecipe.Click += new System.EventHandler(this.OnOpenRecipeForm);

            this.tsBtnSettings.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.tsBtnSettings.Name = "tsBtnSettings";
            this.tsBtnSettings.Text = "系统设置";
            this.tsBtnSettings.ToolTipText = "图片保存路径 / 格式 / 保留天数、良率告警下限，以及运行前置条件";
            this.tsBtnSettings.Click += new System.EventHandler(this.OnOpenSettings);

            this.tsSep3.Name = "tsSep3";

            this.tsLabelStatus.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            this.tsLabelStatus.Name = "tsLabelStatus";
            this.tsLabelStatus.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            this.tsLabelStatus.ForeColor = System.Drawing.Color.DeepSkyBlue;

            // ---------- panelCenter ----------
            this.panelCenter.BackColor = System.Drawing.Color.FromArgb(24, 24, 30);
            this.panelCenter.Controls.Add(this.tableViews);
            this.panelCenter.Dock = System.Windows.Forms.DockStyle.Fill;
            this.panelCenter.Location = new System.Drawing.Point(0, 40);
            this.panelCenter.Name = "panelCenter";
            this.panelCenter.Padding = new System.Windows.Forms.Padding(4);
            this.panelCenter.Size = new System.Drawing.Size(1140, 632);
            this.panelCenter.TabIndex = 1;

            this.tableViews.ColumnCount = 1;
            this.tableViews.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tableViews.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tableViews.Location = new System.Drawing.Point(4, 4);
            this.tableViews.Name = "tableViews";
            this.tableViews.RowCount = 1;
            this.tableViews.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tableViews.Size = new System.Drawing.Size(1132, 624);
            this.tableViews.TabIndex = 0;

            // ---------- panelRight ----------
            // 停靠按集合逆序生效：先加的在下方。此处顺序 = 运行状态 / 运行控制 / 统计 / 徽标，
            // 最终自上而下为：徽标 → 统计 → 运行控制 → 运行状态（Fill，空间不足时自动收缩）。
            this.panelRight.Controls.Add(this.panelRunState);
            this.panelRight.Controls.Add(this.panelRunCtrl);
            this.panelRight.Controls.Add(this.groupFlowStats);
            this.panelRight.Controls.Add(this.groupStats);
            this.panelRight.Controls.Add(this.panelVerdict);
            this.panelRight.Dock = System.Windows.Forms.DockStyle.Right;
            this.panelRight.AutoScroll = true;
            this.panelRight.Location = new System.Drawing.Point(1140, 40);
            this.panelRight.Name = "panelRight";
            this.panelRight.Padding = new System.Windows.Forms.Padding(8);
            this.panelRight.Size = new System.Drawing.Size(300, 632);
            this.panelRight.TabIndex = 2;

            // ---------- ① 判定徽标 ----------
            this.panelVerdict.Controls.Add(this.lblVerdict);
            this.panelVerdict.Dock = System.Windows.Forms.DockStyle.Top;
            this.panelVerdict.Location = new System.Drawing.Point(8, 8);
            this.panelVerdict.Name = "panelVerdict";
            this.panelVerdict.Padding = new System.Windows.Forms.Padding(0);
            this.panelVerdict.Size = new System.Drawing.Size(284, 96);
            this.panelVerdict.TabIndex = 0;

            this.lblVerdict.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblVerdict.Location = new System.Drawing.Point(0, 0);
            this.lblVerdict.Name = "lblVerdict";
            this.lblVerdict.Size = new System.Drawing.Size(284, 96);
            this.lblVerdict.TabIndex = 0;
            this.lblVerdict.Text = "--";
            this.lblVerdict.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;

            // ---------- ② 统计卡片 ----------
            this.groupStats.Controls.Add(this.tableStats);
            this.groupStats.Dock = System.Windows.Forms.DockStyle.Top;
            this.groupStats.Location = new System.Drawing.Point(8, 104);
            this.groupStats.Margin = new System.Windows.Forms.Padding(0, 0, 0, 8);
            this.groupStats.Name = "groupStats";
            this.groupStats.Padding = new System.Windows.Forms.Padding(3);
            this.groupStats.Size = new System.Drawing.Size(284, 152);
            this.groupStats.TabIndex = 1;
            this.groupStats.TabStop = false;
            this.groupStats.Text = "数据统计";

            this.tableStats.ColumnCount = 2;
            this.tableStats.RowCount = 5;
            this.tableStats.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 45F));
            this.tableStats.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 55F));
            this.tableStats.Controls.Add(this.lblTotalCap, 0, 0);
            this.tableStats.Controls.Add(this.lblTotalValue, 1, 0);
            this.tableStats.Controls.Add(this.lblOkCap, 0, 1);
            this.tableStats.Controls.Add(this.lblOkValue, 1, 1);
            this.tableStats.Controls.Add(this.lblNgCap, 0, 2);
            this.tableStats.Controls.Add(this.lblNgValue, 1, 2);
            this.tableStats.Controls.Add(this.lblYieldCap, 0, 3);
            this.tableStats.Controls.Add(this.lblYieldValue, 1, 3);
            this.tableStats.Controls.Add(this.lblCycleCap, 0, 4);
            this.tableStats.Controls.Add(this.lblCycleValue, 1, 4);
            this.tableStats.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tableStats.Location = new System.Drawing.Point(3, 18);
            this.tableStats.Name = "tableStats";
            // 良品率行更高（28 pt 大数字单独一行），其余 4 行等分余量
            this.tableStats.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 17F));
            this.tableStats.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 17F));
            this.tableStats.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 17F));
            this.tableStats.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 32F));
            this.tableStats.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 17F));
            this.tableStats.Size = new System.Drawing.Size(278, 131);
            this.tableStats.TabIndex = 0;

            // ---------- 各流程统计（固定高度 GroupBox + 内部滚动面板；行结构仅随流程集合变化重建，避免连续运行逐帧重建/布局风暴）----------
            this.flowScroll.Dock = System.Windows.Forms.DockStyle.Fill;
            this.flowScroll.AutoScroll = true;
            this.flowScroll.Location = new System.Drawing.Point(3, 18);
            this.flowScroll.Name = "flowScroll";
            this.flowScroll.Size = new System.Drawing.Size(278, 154);
            this.flowScroll.TabIndex = 0;

            this.tableFlowStats.ColumnCount = 4;
            this.tableFlowStats.RowCount = 1;
            this.tableFlowStats.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 46F));
            this.tableFlowStats.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 18F));
            this.tableFlowStats.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 18F));
            this.tableFlowStats.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 18F));
            this.tableFlowStats.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 22F));
            this.tableFlowStats.Dock = System.Windows.Forms.DockStyle.Top;
            this.tableFlowStats.AutoSize = false;
            this.tableFlowStats.Location = new System.Drawing.Point(0, 0);
            this.tableFlowStats.Name = "tableFlowStats";
            this.tableFlowStats.Size = new System.Drawing.Size(278, 22);
            this.tableFlowStats.TabIndex = 0;

            this.lblFlowHdrName.AutoSize = false;
            this.lblFlowHdrName.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblFlowHdrName.Name = "lblFlowHdrName";
            this.lblFlowHdrName.Text = "流程";
            this.lblFlowHdrName.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.lblFlowHdrOk.AutoSize = false;
            this.lblFlowHdrOk.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblFlowHdrOk.Name = "lblFlowHdrOk";
            this.lblFlowHdrOk.Text = "OK";
            this.lblFlowHdrOk.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            this.lblFlowHdrNg.AutoSize = false;
            this.lblFlowHdrNg.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblFlowHdrNg.Name = "lblFlowHdrNg";
            this.lblFlowHdrNg.Text = "NG";
            this.lblFlowHdrNg.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            this.lblFlowHdrRate.AutoSize = false;
            this.lblFlowHdrRate.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblFlowHdrRate.Name = "lblFlowHdrRate";
            this.lblFlowHdrRate.Text = "成功率";
            this.lblFlowHdrRate.TextAlign = System.Drawing.ContentAlignment.MiddleRight;

            this.tableFlowStats.Controls.Add(this.lblFlowHdrName, 0, 0);
            this.tableFlowStats.Controls.Add(this.lblFlowHdrOk, 1, 0);
            this.tableFlowStats.Controls.Add(this.lblFlowHdrNg, 2, 0);
            this.tableFlowStats.Controls.Add(this.lblFlowHdrRate, 3, 0);

            this.flowScroll.Controls.Add(this.tableFlowStats);
            this.groupFlowStats.Controls.Add(this.flowScroll);
            this.groupFlowStats.Dock = System.Windows.Forms.DockStyle.Top;
            this.groupFlowStats.AutoSize = false;
            this.groupFlowStats.Size = new System.Drawing.Size(284, 178);
            this.groupFlowStats.Margin = new System.Windows.Forms.Padding(0, 0, 0, 8);
            this.groupFlowStats.Name = "groupFlowStats";
            this.groupFlowStats.Padding = new System.Windows.Forms.Padding(3);
            this.groupFlowStats.TabIndex = 2;
            this.groupFlowStats.TabStop = false;
            this.groupFlowStats.Text = "各流程统计";

            this.lblTotalCap.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblTotalCap.Text = "总数";
            this.lblTotalCap.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            this.lblTotalValue.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblTotalValue.Text = "0";
            this.lblTotalValue.TextAlign = System.Drawing.ContentAlignment.MiddleRight;

            this.lblOkCap.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblOkCap.Text = "OK";
            this.lblOkCap.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            this.lblOkValue.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblOkValue.Text = "0";
            this.lblOkValue.TextAlign = System.Drawing.ContentAlignment.MiddleRight;

            this.lblNgCap.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblNgCap.Text = "NG";
            this.lblNgCap.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            this.lblNgValue.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblNgValue.Text = "0";
            this.lblNgValue.TextAlign = System.Drawing.ContentAlignment.MiddleRight;

            this.lblYieldCap.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblYieldCap.Text = "良品率";
            this.lblYieldCap.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            this.lblYieldValue.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblYieldValue.Text = "--";
            this.lblYieldValue.TextAlign = System.Drawing.ContentAlignment.MiddleRight;

            this.lblCycleCap.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblCycleCap.Text = "节拍";
            this.lblCycleCap.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            this.lblCycleValue.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblCycleValue.Text = "--";
            this.lblCycleValue.TextAlign = System.Drawing.ContentAlignment.MiddleRight;

            // ---------- ③ 运行控制大按钮 ----------
            // 停靠逆序：先加的在下方 → 自上而下为 启动 / 暂停 / 停止 / 单帧 / 清空
            this.panelRunCtrl.Controls.Add(this.btnResetStats);
            this.panelRunCtrl.Controls.Add(this.btnGrabOnce);
            this.panelRunCtrl.Controls.Add(this.btnStop);
            this.panelRunCtrl.Controls.Add(this.btnPause);
            this.panelRunCtrl.Controls.Add(this.btnLoop);
            this.panelRunCtrl.Dock = System.Windows.Forms.DockStyle.Top;
            this.panelRunCtrl.Location = new System.Drawing.Point(8, 264);
            this.panelRunCtrl.Margin = new System.Windows.Forms.Padding(0, 0, 0, 8);
            this.panelRunCtrl.Name = "panelRunCtrl";
            this.panelRunCtrl.Padding = new System.Windows.Forms.Padding(0);
            this.panelRunCtrl.Size = new System.Drawing.Size(284, 286);
            this.panelRunCtrl.TabIndex = 2;

            this.btnLoop.Dock = System.Windows.Forms.DockStyle.Top;
            this.btnLoop.Location = new System.Drawing.Point(0, 0);
            this.btnLoop.Margin = new System.Windows.Forms.Padding(0, 0, 0, 6);
            this.btnLoop.Name = "btnLoop";
            this.btnLoop.Size = new System.Drawing.Size(284, 56);
            this.btnLoop.TabIndex = 0;
            this.btnLoop.Text = "启动运行";
            this.btnLoop.Click += new System.EventHandler(this.OnStartLoop);

            this.btnPause.Dock = System.Windows.Forms.DockStyle.Top;
            this.btnPause.Location = new System.Drawing.Point(0, 62);
            this.btnPause.Margin = new System.Windows.Forms.Padding(0, 0, 0, 6);
            this.btnPause.Name = "btnPause";
            this.btnPause.Size = new System.Drawing.Size(284, 56);
            this.btnPause.TabIndex = 1;
            this.btnPause.Text = "暂停";
            this.btnPause.Click += new System.EventHandler(this.OnPause);

            this.btnStop.Dock = System.Windows.Forms.DockStyle.Top;
            this.btnStop.Location = new System.Drawing.Point(0, 124);
            this.btnStop.Margin = new System.Windows.Forms.Padding(0, 0, 0, 6);
            this.btnStop.Name = "btnStop";
            this.btnStop.Size = new System.Drawing.Size(284, 56);
            this.btnStop.TabIndex = 2;
            this.btnStop.Text = "停止";
            this.btnStop.Click += new System.EventHandler(this.OnStop);

            this.btnGrabOnce.Dock = System.Windows.Forms.DockStyle.Top;
            this.btnGrabOnce.Location = new System.Drawing.Point(0, 186);
            this.btnGrabOnce.Margin = new System.Windows.Forms.Padding(0, 0, 0, 6);
            this.btnGrabOnce.Name = "btnGrabOnce";
            this.btnGrabOnce.Size = new System.Drawing.Size(284, 48);
            this.btnGrabOnce.TabIndex = 3;
            this.btnGrabOnce.Text = "单帧采集";
            this.btnGrabOnce.Click += new System.EventHandler(this.OnGrabOnce);

            this.btnResetStats.Dock = System.Windows.Forms.DockStyle.Top;
            this.btnResetStats.Location = new System.Drawing.Point(0, 240);
            this.btnResetStats.Margin = new System.Windows.Forms.Padding(0, 0, 0, 6);
            this.btnResetStats.Name = "btnResetStats";
            this.btnResetStats.Size = new System.Drawing.Size(284, 40);
            this.btnResetStats.TabIndex = 4;
            this.btnResetStats.Text = "清空统计";
            this.btnResetStats.Click += new System.EventHandler(this.OnResetStats);

            // ---------- ④ 运行状态（Fill：空间不足时自动收缩，不挤压上面三区）----------
            this.panelRunState.Controls.Add(this.tableRunState);
            this.panelRunState.Controls.Add(this.lblConsecutiveNg);
            this.panelRunState.Dock = System.Windows.Forms.DockStyle.Top;
            this.panelRunState.Location = new System.Drawing.Point(8, 558);
            this.panelRunState.Name = "panelRunState";
            this.panelRunState.Padding = new System.Windows.Forms.Padding(3);
            this.panelRunState.Size = new System.Drawing.Size(284, 66);
            this.panelRunState.TabIndex = 3;

            this.tableRunState.ColumnCount = 2;
            this.tableRunState.RowCount = 4;
            this.tableRunState.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 42F));
            this.tableRunState.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 58F));
            this.tableRunState.Controls.Add(this.lblRunStateCap, 0, 0);
            this.tableRunState.Controls.Add(this.lblRunStateValue, 1, 0);
            this.tableRunState.Controls.Add(this.lblCameraCap, 0, 1);
            this.tableRunState.Controls.Add(this.lblCameraValue, 1, 1);
            this.tableRunState.Controls.Add(this.lblFlowCap, 0, 2);
            this.tableRunState.Controls.Add(this.lblFlowValue, 1, 2);
            this.tableRunState.Controls.Add(this.lblLastCap, 0, 3);
            this.tableRunState.Controls.Add(this.lblLastValue, 1, 3);
            this.tableRunState.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tableRunState.Location = new System.Drawing.Point(3, 3);
            this.tableRunState.Name = "tableRunState";
            this.tableRunState.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 25F));
            this.tableRunState.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 25F));
            this.tableRunState.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 25F));
            this.tableRunState.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 25F));
            this.tableRunState.Size = new System.Drawing.Size(278, 60);
            this.tableRunState.TabIndex = 0;

            this.lblRunStateCap.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblRunStateCap.Text = "运行状态";
            this.lblRunStateCap.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            this.lblRunStateValue.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblRunStateValue.Text = "--";
            this.lblRunStateValue.TextAlign = System.Drawing.ContentAlignment.MiddleRight;

            this.lblCameraCap.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblCameraCap.Text = "相机";
            this.lblCameraCap.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            this.lblCameraValue.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblCameraValue.Text = "--";
            this.lblCameraValue.TextAlign = System.Drawing.ContentAlignment.MiddleRight;

            this.lblFlowCap.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblFlowCap.Text = "当前流程";
            this.lblFlowCap.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            this.lblFlowValue.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblFlowValue.Text = "--";
            this.lblFlowValue.TextAlign = System.Drawing.ContentAlignment.MiddleRight;

            this.lblLastCap.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblLastCap.Text = "最近结论";
            this.lblLastCap.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            this.lblLastValue.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblLastValue.Text = "--";
            this.lblLastValue.TextAlign = System.Drawing.ContentAlignment.MiddleRight;

            this.lblConsecutiveNg.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.lblConsecutiveNg.Location = new System.Drawing.Point(3, 35);
            this.lblConsecutiveNg.Name = "lblConsecutiveNg";
            this.lblConsecutiveNg.Size = new System.Drawing.Size(278, 28);
            this.lblConsecutiveNg.TabIndex = 1;
            this.lblConsecutiveNg.Text = "连续 NG：0";
            this.lblConsecutiveNg.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.lblConsecutiveNg.Visible = false;

            // ---------- panelBottom（日志区，可折叠）----------
            this.panelBottom.Controls.Add(this.listLog);
            this.panelBottom.Controls.Add(this.btnFoldLog);
            this.panelBottom.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.panelBottom.Location = new System.Drawing.Point(0, 672);
            this.panelBottom.Name = "panelBottom";
            this.panelBottom.Padding = new System.Windows.Forms.Padding(6);
            this.panelBottom.Size = new System.Drawing.Size(1440, 120);
            this.panelBottom.TabIndex = 3;

            this.listLog.BackColor = System.Drawing.Color.FromArgb(24, 24, 30);
            this.listLog.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.listLog.Dock = System.Windows.Forms.DockStyle.Fill;
            this.listLog.DrawMode = System.Windows.Forms.DrawMode.OwnerDrawFixed;
            this.listLog.IntegralHeight = false;
            this.listLog.ItemHeight = 16;
            this.listLog.Location = new System.Drawing.Point(6, 30);
            this.listLog.Name = "listLog";
            this.listLog.Size = new System.Drawing.Size(1428, 84);
            this.listLog.TabIndex = 1;
            this.listLog.DrawItem += new System.Windows.Forms.DrawItemEventHandler(this.OnLogDrawItem);

            this.btnFoldLog.Dock = System.Windows.Forms.DockStyle.Top;
            this.btnFoldLog.Location = new System.Drawing.Point(6, 6);
            this.btnFoldLog.Name = "btnFoldLog";
            this.btnFoldLog.Size = new System.Drawing.Size(1428, 24);
            this.btnFoldLog.TabIndex = 0;
            this.btnFoldLog.Text = "日志  ▼";
            this.btnFoldLog.Click += new System.EventHandler(this.OnFoldLog);

            // ---------- statusStrip ----------
            this.statusStrip.BackColor = System.Drawing.Color.FromArgb(30, 30, 38);
            this.statusStrip.ForeColor = System.Drawing.Color.Silver;
            this.statusStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
                this.statusLabel});
            this.statusStrip.Location = new System.Drawing.Point(0, 792);
            this.statusStrip.Name = "statusStrip";
            this.statusStrip.Size = new System.Drawing.Size(1440, 26);
            this.statusStrip.TabIndex = 4;

            this.statusLabel.Name = "statusLabel";
            this.statusLabel.Size = new System.Drawing.Size(0, 19);
            this.statusLabel.Spring = true;
            this.statusLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            // ---------- loopTimer ----------
            this.loopTimer.Interval = 500;
            this.loopTimer.Tick += new System.EventHandler(this.OnLoopTick);

            // ---------- MainForm ----------
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(38, 38, 46);
            this.ClientSize = new System.Drawing.Size(1440, 818);
            this.Controls.Add(this.panelCenter);
            this.Controls.Add(this.panelRight);
            this.Controls.Add(this.panelBottom);
            this.Controls.Add(this.statusStrip);
            this.Controls.Add(this.toolStrip);
            this.ForeColor = System.Drawing.Color.Gainsboro;
            this.MinimumSize = new System.Drawing.Size(1280, 800);
            this.Name = "MainForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "VisionPlatform";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.OnFormClosing);

            this.toolStrip.ResumeLayout(false);
            this.toolStrip.PerformLayout();
            this.panelCenter.ResumeLayout(false);
            this.panelRight.ResumeLayout(false);
            this.panelVerdict.ResumeLayout(false);
            this.groupStats.ResumeLayout(false);
            this.tableStats.ResumeLayout(false);
            this.groupFlowStats.ResumeLayout(false);
            this.groupFlowStats.PerformLayout();
            this.flowScroll.ResumeLayout(false);
            this.flowScroll.PerformLayout();
            this.tableFlowStats.ResumeLayout(false);
            this.tableFlowStats.PerformLayout();
            this.panelRunCtrl.ResumeLayout(false);
            this.panelRunState.ResumeLayout(false);
            this.tableRunState.ResumeLayout(false);
            this.panelBottom.ResumeLayout(false);
            this.statusStrip.ResumeLayout(false);
            this.statusStrip.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private System.Windows.Forms.ToolStrip toolStrip;
        private System.Windows.Forms.ToolStripLabel tsLabelCamera;
        private System.Windows.Forms.ToolStripComboBox tsComboCameras;
        private System.Windows.Forms.ToolStripButton tsBtnRefresh;
        private System.Windows.Forms.ToolStripButton tsBtnGrabOnce;
        private System.Windows.Forms.ToolStripButton tsBtnLive;
        private System.Windows.Forms.ToolStripButton tsBtnSyncGrab;
        private System.Windows.Forms.ToolStripSeparator tsSep1;
        private System.Windows.Forms.ToolStripLabel tsLabelLayout;
        private System.Windows.Forms.ToolStripComboBox tsComboLayout;
        private System.Windows.Forms.ToolStripLabel tsLabelFlow;
        private System.Windows.Forms.ToolStripComboBox tsComboFlow;
        private System.Windows.Forms.ToolStripButton tsBtnRightPanel;
        private System.Windows.Forms.ToolStripSeparator tsSep2;
        private System.Windows.Forms.ToolStripButton tsBtnCameraMgr;
        private System.Windows.Forms.ToolStripButton tsBtnAlgo;
        private System.Windows.Forms.ToolStripButton tsBtnFlow;
        private System.Windows.Forms.ToolStripButton tsBtnComm;
        private System.Windows.Forms.ToolStripButton tsBtnRecipe;
        private System.Windows.Forms.ToolStripButton tsBtnSettings;
        private System.Windows.Forms.ToolStripSeparator tsSep3;
        private System.Windows.Forms.ToolStripLabel tsLabelStatus;

        private System.Windows.Forms.Panel panelCenter;
        private System.Windows.Forms.TableLayoutPanel tableViews;

        private System.Windows.Forms.Panel panelRight;
        private System.Windows.Forms.Panel panelVerdict;
        private System.Windows.Forms.Label lblVerdict;
        private System.Windows.Forms.GroupBox groupStats;
        private System.Windows.Forms.TableLayoutPanel tableStats;
        private System.Windows.Forms.Label lblTotalCap;
        private System.Windows.Forms.Label lblTotalValue;
        private System.Windows.Forms.Label lblOkCap;
        private System.Windows.Forms.Label lblOkValue;
        private System.Windows.Forms.Label lblNgCap;
        private System.Windows.Forms.Label lblNgValue;
        private System.Windows.Forms.Label lblYieldCap;
        private System.Windows.Forms.Label lblYieldValue;
        private System.Windows.Forms.Label lblCycleCap;
        private System.Windows.Forms.Label lblCycleValue;
        private System.Windows.Forms.GroupBox groupFlowStats;
        private System.Windows.Forms.Panel flowScroll;
        private System.Windows.Forms.TableLayoutPanel tableFlowStats;
        private System.Windows.Forms.Label lblFlowHdrName;
        private System.Windows.Forms.Label lblFlowHdrOk;
        private System.Windows.Forms.Label lblFlowHdrNg;
        private System.Windows.Forms.Label lblFlowHdrRate;
        private System.Windows.Forms.Panel panelRunCtrl;
        private VisionPlatform.UI.UiButton btnLoop;
        private VisionPlatform.UI.UiButton btnPause;
        private VisionPlatform.UI.UiButton btnStop;
        private VisionPlatform.UI.UiButton btnGrabOnce;
        private VisionPlatform.UI.UiButton btnResetStats;
        private System.Windows.Forms.Panel panelRunState;
        private System.Windows.Forms.TableLayoutPanel tableRunState;
        private System.Windows.Forms.Label lblRunStateCap;
        private System.Windows.Forms.Label lblRunStateValue;
        private System.Windows.Forms.Label lblCameraCap;
        private System.Windows.Forms.Label lblCameraValue;
        private System.Windows.Forms.Label lblFlowCap;
        private System.Windows.Forms.Label lblFlowValue;
        private System.Windows.Forms.Label lblLastCap;
        private System.Windows.Forms.Label lblLastValue;
        private System.Windows.Forms.Label lblConsecutiveNg;

        private System.Windows.Forms.Panel panelBottom;
        private System.Windows.Forms.Button btnFoldLog;
        private System.Windows.Forms.ListBox listLog;

        private System.Windows.Forms.StatusStrip statusStrip;
        private System.Windows.Forms.ToolStripStatusLabel statusLabel;

        private System.Windows.Forms.Timer loopTimer;
    }
}
