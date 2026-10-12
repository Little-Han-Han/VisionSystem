namespace VisionPlatform.UI
{
    partial class FlowForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null)) components.Dispose();
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();

            this.panelLeft = new System.Windows.Forms.Panel();
            this.lblModules = new System.Windows.Forms.Label();
            this.treeModules = new System.Windows.Forms.TreeView();

            this.panelRight = new System.Windows.Forms.Panel();
            this.lblParams = new System.Windows.Forms.Label();
            this.lblPorts = new System.Windows.Forms.Label();
            this.treePorts = new System.Windows.Forms.TreeView();
            this.nodeParamPanel = new VisionPlatform.UI.NodeParamPanel();

            this.panelBottom = new System.Windows.Forms.Panel();
            this.grpFlows = new System.Windows.Forms.GroupBox();
            this.lstFlows = new System.Windows.Forms.ListBox();
            this.btnNewFlow = new VisionPlatform.UI.UiButton();
            this.btnRenameFlow = new VisionPlatform.UI.UiButton();
            this.btnDeleteFlow = new VisionPlatform.UI.UiButton();

            this.grpBind = new System.Windows.Forms.GroupBox();
            this.lblCamera = new System.Windows.Forms.Label();
            this.cmbCamera = new System.Windows.Forms.ComboBox();
            this.btnRefreshBinding = new VisionPlatform.UI.UiButton();
            this.lblView = new System.Windows.Forms.Label();
            this.numViewIndex = new System.Windows.Forms.NumericUpDown();
            this.btnValidate = new VisionPlatform.UI.UiButton();
            this.btnRun = new VisionPlatform.UI.UiButton();
            this.btnStep = new VisionPlatform.UI.UiButton();
            this.btnRunLoop = new VisionPlatform.UI.UiButton();
            this.btnRunNode = new VisionPlatform.UI.UiButton();
            this.btnStop = new VisionPlatform.UI.UiButton();
            this.btnAutoLayout = new VisionPlatform.UI.UiButton();
            this.lblRunStatus = new System.Windows.Forms.Label();

            this.splitMain = new System.Windows.Forms.SplitContainer();
            this.canvas = new VisionPlatform.UI.FlowCanvasControl();
            this.flowPreview = new VisionPlatform.UI.FlowPreviewPanel();

            this.panelLeft.SuspendLayout();
            this.panelRight.SuspendLayout();
            this.panelBottom.SuspendLayout();
            this.grpFlows.SuspendLayout();
            this.grpBind.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numViewIndex)).BeginInit();
            this.splitMain.BeginInit();
            this.splitMain.Panel1.SuspendLayout();
            this.splitMain.Panel2.SuspendLayout();
            this.splitMain.SuspendLayout();
            this.SuspendLayout();

            // ---------- panelLeft（工具箱）----------
            this.panelLeft.BackColor = System.Drawing.Color.FromArgb(38, 38, 46);
            this.panelLeft.Controls.Add(this.treeModules);
            this.panelLeft.Controls.Add(this.lblModules);
            this.panelLeft.Dock = System.Windows.Forms.DockStyle.Left;
            this.panelLeft.Location = new System.Drawing.Point(0, 0);
            this.panelLeft.Name = "panelLeft";
            this.panelLeft.Padding = new System.Windows.Forms.Padding(6);
            this.panelLeft.Size = new System.Drawing.Size(200, 720);
            this.panelLeft.TabIndex = 0;

            this.lblModules.AutoSize = false;
            this.lblModules.Dock = System.Windows.Forms.DockStyle.Top;
            this.lblModules.ForeColor = System.Drawing.Color.DeepSkyBlue;
            this.lblModules.Name = "lblModules";
            this.lblModules.Size = new System.Drawing.Size(188, 26);
            this.lblModules.Text = "工具箱（拖拽到画布）";
            this.lblModules.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            // 工具箱：按类别分组的树（分类=根节点，模块=叶子）
            // Dock=Fill，故必须【先】Add（集合下标 0 最后参与布局，占据剩余区域）。
            this.treeModules.BackColor = System.Drawing.Color.FromArgb(30, 30, 38);
            this.treeModules.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.treeModules.Dock = System.Windows.Forms.DockStyle.Fill;
            this.treeModules.ForeColor = System.Drawing.Color.Gainsboro;
            this.treeModules.Name = "treeModules";

            // ---------- panelRight（参数面板 + 输入输出端子面板）----------
            this.panelRight.BackColor = System.Drawing.Color.FromArgb(42, 42, 50);
            // Add 顺序 = 自下而上的堆叠顺序（WinForms 逆序 Dock）：
            // nodeParamPanel(Fill) 先加 → 最后布局占满剩余；其余 Top 控件后加的排在上面。
            // 视觉自上而下：lblParams → lblPorts → treePorts → nodeParamPanel
            this.panelRight.Controls.Add(this.nodeParamPanel);
            this.panelRight.Controls.Add(this.treePorts);
            this.panelRight.Controls.Add(this.lblPorts);
            this.panelRight.Controls.Add(this.lblParams);
            this.panelRight.Dock = System.Windows.Forms.DockStyle.Right;
            this.panelRight.Location = new System.Drawing.Point(900, 0);
            this.panelRight.Name = "panelRight";
            this.panelRight.Padding = new System.Windows.Forms.Padding(6);
            this.panelRight.Size = new System.Drawing.Size(300, 720);
            this.panelRight.TabIndex = 1;

            this.lblParams.Dock = System.Windows.Forms.DockStyle.Top;
            this.lblParams.ForeColor = System.Drawing.Color.DeepSkyBlue;
            this.lblParams.Name = "lblParams";
            this.lblParams.Size = new System.Drawing.Size(288, 26);
            this.lblParams.Text = "节点参数";
            this.lblParams.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            this.lblPorts.Dock = System.Windows.Forms.DockStyle.Top;
            this.lblPorts.ForeColor = System.Drawing.Color.DeepSkyBlue;
            this.lblPorts.Name = "lblPorts";
            this.lblPorts.Size = new System.Drawing.Size(288, 26);
            this.lblPorts.Text = "Inputs/Outputs";
            this.lblPorts.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            // 端子面板：[Inputs] / [Outputs] 两棵子树，逐端子按数据类型上色（UiTree 自绘）
            this.treePorts.BackColor = System.Drawing.Color.FromArgb(30, 30, 38);
            this.treePorts.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.treePorts.Dock = System.Windows.Forms.DockStyle.Top;
            this.treePorts.ForeColor = System.Drawing.Color.Gainsboro;
            this.treePorts.Name = "treePorts";
            this.treePorts.ShowNodeToolTips = true;
            this.treePorts.Size = new System.Drawing.Size(288, 168);

            this.nodeParamPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.nodeParamPanel.Name = "nodeParamPanel";

            // ---------- panelBottom（流程管理 + 绑定 + 运行）----------
            this.panelBottom.BackColor = System.Drawing.Color.FromArgb(34, 34, 42);
            this.panelBottom.Controls.Add(this.grpBind);
            this.panelBottom.Controls.Add(this.grpFlows);
            this.panelBottom.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.panelBottom.Location = new System.Drawing.Point(0, 570);
            this.panelBottom.Name = "panelBottom";
            this.panelBottom.Size = new System.Drawing.Size(1180, 150);
            this.panelBottom.TabIndex = 2;

            // -- 流程管理 --
            this.grpFlows.ForeColor = System.Drawing.Color.DeepSkyBlue;
            this.grpFlows.Location = new System.Drawing.Point(8, 8);
            this.grpFlows.Name = "grpFlows";
            this.grpFlows.Size = new System.Drawing.Size(362, 134);
            this.grpFlows.TabIndex = 0;
            this.grpFlows.TabStop = false;
            this.grpFlows.Text = "流程管理";
            // 子控件必须逐个 Add：漏掉的话控件没有 Parent，永不显示也不响应事件
            this.grpFlows.Controls.Add(this.lstFlows);
            this.grpFlows.Controls.Add(this.btnNewFlow);
            this.grpFlows.Controls.Add(this.btnRenameFlow);
            this.grpFlows.Controls.Add(this.btnDeleteFlow);

            this.lstFlows.BackColor = System.Drawing.Color.FromArgb(30, 30, 38);
            this.lstFlows.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.lstFlows.ForeColor = System.Drawing.Color.Gainsboro;
            this.lstFlows.IntegralHeight = false;
            this.lstFlows.ItemHeight = 18;
            this.lstFlows.Location = new System.Drawing.Point(10, 22);
            this.lstFlows.Name = "lstFlows";
            this.lstFlows.Size = new System.Drawing.Size(200, 100);
            this.lstFlows.TabIndex = 0;

            this.btnNewFlow.Location = new System.Drawing.Point(220, 22);
            this.btnNewFlow.Name = "btnNewFlow";
            this.btnNewFlow.Size = new System.Drawing.Size(130, 28);
            this.btnNewFlow.TabIndex = 1;
            this.btnNewFlow.Text = "新建流程";
            this.btnNewFlow.Click += new System.EventHandler(this.OnNewFlow);

            this.btnRenameFlow.Location = new System.Drawing.Point(220, 56);
            this.btnRenameFlow.Name = "btnRenameFlow";
            this.btnRenameFlow.Size = new System.Drawing.Size(130, 28);
            this.btnRenameFlow.TabIndex = 2;
            this.btnRenameFlow.Text = "重命名";
            this.btnRenameFlow.Click += new System.EventHandler(this.OnRenameFlow);

            this.btnDeleteFlow.Location = new System.Drawing.Point(220, 90);
            this.btnDeleteFlow.Name = "btnDeleteFlow";
            this.btnDeleteFlow.Size = new System.Drawing.Size(130, 28);
            this.btnDeleteFlow.TabIndex = 3;
            this.btnDeleteFlow.Text = "删除流程";
            this.btnDeleteFlow.Click += new System.EventHandler(this.OnDeleteFlow);

            // -- 绑定 + 运行 --
            this.grpBind.ForeColor = System.Drawing.Color.DeepSkyBlue;
            this.grpBind.Location = new System.Drawing.Point(380, 8);
            this.grpBind.Name = "grpBind";
            this.grpBind.Size = new System.Drawing.Size(760, 134);
            this.grpBind.TabIndex = 1;
            this.grpBind.TabStop = false;
            this.grpBind.Text = "绑定与运行";
            // 子控件必须逐个 Add：漏掉的话控件没有 Parent，永不显示也不响应事件
            this.grpBind.Controls.Add(this.lblCamera);
            this.grpBind.Controls.Add(this.cmbCamera);
            this.grpBind.Controls.Add(this.btnRefreshBinding);
            this.grpBind.Controls.Add(this.lblView);
            this.grpBind.Controls.Add(this.numViewIndex);
            this.grpBind.Controls.Add(this.btnValidate);
            this.grpBind.Controls.Add(this.btnRun);
            this.grpBind.Controls.Add(this.btnStep);
            this.grpBind.Controls.Add(this.btnRunLoop);
            this.grpBind.Controls.Add(this.btnRunNode);
            this.grpBind.Controls.Add(this.btnStop);
            this.grpBind.Controls.Add(this.btnAutoLayout);
            this.grpBind.Controls.Add(this.lblRunStatus);

            this.lblCamera.AutoSize = true;
            this.lblCamera.ForeColor = System.Drawing.Color.Silver;
            this.lblCamera.Location = new System.Drawing.Point(12, 30);
            this.lblCamera.Name = "lblCamera";
            this.lblCamera.Text = "相机：";

            this.cmbCamera.BackColor = System.Drawing.Color.FromArgb(30, 30, 38);
            this.cmbCamera.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbCamera.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.cmbCamera.ForeColor = System.Drawing.Color.Gainsboro;
            this.cmbCamera.Location = new System.Drawing.Point(62, 27);
            this.cmbCamera.Name = "cmbCamera";
            this.cmbCamera.Size = new System.Drawing.Size(200, 25);
            this.cmbCamera.TabIndex = 0;

            this.btnRefreshBinding.Location = new System.Drawing.Point(270, 26);
            this.btnRefreshBinding.Name = "btnRefreshBinding";
            this.btnRefreshBinding.Size = new System.Drawing.Size(60, 27);
            this.btnRefreshBinding.TabIndex = 1;
            this.btnRefreshBinding.Text = "刷新";
            this.btnRefreshBinding.Click += new System.EventHandler(this.OnRefreshCameraList);

            this.lblView.AutoSize = true;
            this.lblView.ForeColor = System.Drawing.Color.Silver;
            this.lblView.Location = new System.Drawing.Point(344, 30);
            this.lblView.Name = "lblView";
            this.lblView.Text = "显示视图：";

            this.numViewIndex.BackColor = System.Drawing.Color.FromArgb(30, 30, 38);
            this.numViewIndex.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.numViewIndex.ForeColor = System.Drawing.Color.Gainsboro;
            this.numViewIndex.Location = new System.Drawing.Point(412, 28);
            this.numViewIndex.Maximum = new decimal(new int[] { 9, 0, 0, 0 });
            this.numViewIndex.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numViewIndex.Name = "numViewIndex";
            this.numViewIndex.Size = new System.Drawing.Size(56, 23);
            this.numViewIndex.TabIndex = 2;
            this.numViewIndex.Value = new decimal(new int[] { 1, 0, 0, 0 });
            this.numViewIndex.ValueChanged += new System.EventHandler(this.OnViewIndexChanged);

            this.btnValidate.Location = new System.Drawing.Point(496, 26);
            this.btnValidate.Name = "btnValidate";
            this.btnValidate.Size = new System.Drawing.Size(80, 30);
            this.btnValidate.TabIndex = 3;
            this.btnValidate.Text = "校验";
            this.btnValidate.Click += new System.EventHandler(this.OnValidate);

            this.btnRun.Location = new System.Drawing.Point(496, 62);
            this.btnRun.Name = "btnRun";
            this.btnRun.Size = new System.Drawing.Size(80, 30);
            this.btnRun.TabIndex = 4;
            this.btnRun.Text = "运行";
            this.btnRun.Click += new System.EventHandler(this.OnRun);

            this.btnStep.Location = new System.Drawing.Point(590, 26);
            this.btnStep.Name = "btnStep";
            this.btnStep.Size = new System.Drawing.Size(80, 30);
            this.btnStep.TabIndex = 6;
            this.btnStep.Text = "单步";
            this.btnStep.Click += new System.EventHandler(this.OnStep);

            this.btnRunLoop.Location = new System.Drawing.Point(590, 62);
            this.btnRunLoop.Name = "btnRunLoop";
            this.btnRunLoop.Size = new System.Drawing.Size(80, 30);
            this.btnRunLoop.TabIndex = 7;
            this.btnRunLoop.Text = "连续运行";
            this.btnRunLoop.Click += new System.EventHandler(this.OnRunLoop);

            // 「运行选中节点」：只跑到画布当前选中节点为止（单节点调试），不跑下游
            this.btnRunNode.Location = new System.Drawing.Point(682, 26);
            this.btnRunNode.Name = "btnRunNode";
            this.btnRunNode.Size = new System.Drawing.Size(68, 30);
            this.btnRunNode.TabIndex = 9;
            this.btnRunNode.Text = "运行节点";
            this.btnRunNode.Click += new System.EventHandler(this.OnRunNode);

            this.btnStop.Enabled = false;
            this.btnStop.Location = new System.Drawing.Point(682, 62);
            this.btnStop.Name = "btnStop";
            this.btnStop.Size = new System.Drawing.Size(68, 30);
            this.btnStop.TabIndex = 5;
            this.btnStop.Text = "停止";
            this.btnStop.Click += new System.EventHandler(this.OnStop);

            // 位置选在「刷新」正下方：该处 y=60..88 空闲，且不触碰 lblRunStatus(y=104)
            this.btnAutoLayout.Location = new System.Drawing.Point(270, 60);
            this.btnAutoLayout.Name = "btnAutoLayout";
            this.btnAutoLayout.Size = new System.Drawing.Size(110, 28);
            this.btnAutoLayout.TabIndex = 8;
            this.btnAutoLayout.Text = "自动整理布局";
            this.btnAutoLayout.Click += new System.EventHandler(this.OnAutoLayout);

            this.lblRunStatus.AutoSize = false;
            this.lblRunStatus.ForeColor = System.Drawing.Color.Silver;
            this.lblRunStatus.Location = new System.Drawing.Point(12, 104);
            this.lblRunStatus.Name = "lblRunStatus";
            this.lblRunStatus.Size = new System.Drawing.Size(740, 22);
            this.lblRunStatus.Text = "就绪";

            // ---------- 画布 / 预览（SplitContainer，上画布下预览）----------
            this.splitMain.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitMain.Location = new System.Drawing.Point(200, 0);
            this.splitMain.Name = "splitMain";
            this.splitMain.Orientation = System.Windows.Forms.Orientation.Horizontal;
            this.splitMain.SplitterDistance = 340;      // 初始约 60%（随窗口按比例由 FixedPanel 决定）
            this.splitMain.TabIndex = 3;

            this.canvas.Dock = System.Windows.Forms.DockStyle.Fill;
            this.canvas.Location = new System.Drawing.Point(0, 0);
            this.canvas.Name = "canvas";
            this.canvas.TabIndex = 0;

            this.flowPreview.Dock = System.Windows.Forms.DockStyle.Fill;
            this.flowPreview.Location = new System.Drawing.Point(0, 0);
            this.flowPreview.Name = "flowPreview";
            this.flowPreview.TabIndex = 0;

            this.splitMain.Panel1.Controls.Add(this.canvas);
            this.splitMain.Panel2.Controls.Add(this.flowPreview);

            // ---------- FlowForm ----------
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(38, 38, 46);
            this.ClientSize = new System.Drawing.Size(1280, 800);
            // 先加 Fill，再加 Bottom/Right/Left，保证 Fill 最后参与布局占据剩余区域
            this.Controls.Add(this.splitMain);
            this.Controls.Add(this.panelBottom);
            this.Controls.Add(this.panelRight);
            this.Controls.Add(this.panelLeft);
            this.ForeColor = System.Drawing.Color.Gainsboro;
            this.MinimumSize = new System.Drawing.Size(1280, 800);
            this.Name = "FlowForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "流程编排";

            this.panelLeft.ResumeLayout(false);
            this.panelRight.ResumeLayout(false);
            this.panelBottom.ResumeLayout(false);
            this.grpFlows.ResumeLayout(false);
            this.grpBind.ResumeLayout(false);
            this.grpBind.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numViewIndex)).EndInit();
            this.splitMain.Panel1.ResumeLayout(false);
            this.splitMain.Panel2.ResumeLayout(false);
            this.splitMain.EndInit();
            this.splitMain.ResumeLayout(false);
            this.ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.Panel panelLeft;
        private System.Windows.Forms.Label lblModules;
        private System.Windows.Forms.TreeView treeModules;

        private System.Windows.Forms.Panel panelRight;
        private System.Windows.Forms.Label lblParams;
        private System.Windows.Forms.Label lblPorts;
        private System.Windows.Forms.TreeView treePorts;
        private NodeParamPanel nodeParamPanel;

        private System.Windows.Forms.Panel panelBottom;
        private System.Windows.Forms.GroupBox grpFlows;
        private System.Windows.Forms.ListBox lstFlows;
        private VisionPlatform.UI.UiButton btnNewFlow;
        private VisionPlatform.UI.UiButton btnRenameFlow;
        private VisionPlatform.UI.UiButton btnDeleteFlow;

        private System.Windows.Forms.GroupBox grpBind;
        private System.Windows.Forms.Label lblCamera;
        private System.Windows.Forms.ComboBox cmbCamera;
        private VisionPlatform.UI.UiButton btnRefreshBinding;
        private System.Windows.Forms.Label lblView;
        private System.Windows.Forms.NumericUpDown numViewIndex;
        private VisionPlatform.UI.UiButton btnValidate;
        private VisionPlatform.UI.UiButton btnRun;
        private VisionPlatform.UI.UiButton btnStep;
        private VisionPlatform.UI.UiButton btnRunLoop;
        private VisionPlatform.UI.UiButton btnRunNode;
        private VisionPlatform.UI.UiButton btnStop;
        private VisionPlatform.UI.UiButton btnAutoLayout;
        private System.Windows.Forms.Label lblRunStatus;

        private System.Windows.Forms.SplitContainer splitMain;
        private FlowCanvasControl canvas;
        private FlowPreviewPanel flowPreview;
    }
}
