using System.Drawing;

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
            this.panelLeft = new System.Windows.Forms.Panel();
            this.treeModules = new System.Windows.Forms.TreeView();
            this.lblModules = new System.Windows.Forms.Label();
            this.panelRight = new System.Windows.Forms.Panel();
            this.treePorts = new System.Windows.Forms.TreeView();
            this.lblPorts = new System.Windows.Forms.Label();
            this.lblParams = new System.Windows.Forms.Label();
            this.panelBottom = new System.Windows.Forms.Panel();
            this.grpBind = new System.Windows.Forms.GroupBox();
            this.lblCamera = new System.Windows.Forms.Label();
            this.cmbCamera = new System.Windows.Forms.ComboBox();
            this.lblView = new System.Windows.Forms.Label();
            this.numViewIndex = new System.Windows.Forms.NumericUpDown();
            this.lblRunStatus = new System.Windows.Forms.TextBox();
            this.grpFlows = new System.Windows.Forms.GroupBox();
            this.lstFlows = new System.Windows.Forms.ListBox();
            this.splitMain = new System.Windows.Forms.SplitContainer();
            this.canvas = new VisionPlatform.UI.FlowCanvasControl();
            this.flowPreview = new VisionPlatform.UI.FlowPreviewPanel();
            this.btnRefreshBinding = new VisionPlatform.UI.UiButton();
            this.btnValidate = new VisionPlatform.UI.UiButton();
            this.btnRun = new VisionPlatform.UI.UiButton();
            this.btnStep = new VisionPlatform.UI.UiButton();
            this.btnRunLoop = new VisionPlatform.UI.UiButton();
            this.btnRunNode = new VisionPlatform.UI.UiButton();
            this.btnStop = new VisionPlatform.UI.UiButton();
            this.btnAutoLayout = new VisionPlatform.UI.UiButton();
            this.btnNewFlow = new VisionPlatform.UI.UiButton();
            this.btnRenameFlow = new VisionPlatform.UI.UiButton();
            this.btnDeleteFlow = new VisionPlatform.UI.UiButton();
            this.nodeParamPanel = new VisionPlatform.UI.NodeParamPanel();
            this.panelLeft.SuspendLayout();
            this.panelRight.SuspendLayout();
            this.panelBottom.SuspendLayout();
            this.grpBind.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numViewIndex)).BeginInit();
            this.grpFlows.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.splitMain)).BeginInit();
            this.splitMain.Panel1.SuspendLayout();
            this.splitMain.Panel2.SuspendLayout();
            this.splitMain.SuspendLayout();
            this.SuspendLayout();
            // 
            // panelLeft
            // 
            this.panelLeft.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(38)))), ((int)(((byte)(38)))), ((int)(((byte)(46)))));
            this.panelLeft.Controls.Add(this.treeModules);
            this.panelLeft.Controls.Add(this.lblModules);
            this.panelLeft.Dock = System.Windows.Forms.DockStyle.Left;
            this.panelLeft.Location = new System.Drawing.Point(0, 0);
            this.panelLeft.Name = "panelLeft";
            this.panelLeft.Padding = new System.Windows.Forms.Padding(6);
            this.panelLeft.Size = new System.Drawing.Size(200, 637);
            this.panelLeft.TabIndex = 0;
            // 
            // treeModules
            // 
            this.treeModules.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(30)))), ((int)(((byte)(30)))), ((int)(((byte)(38)))));
            this.treeModules.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.treeModules.Dock = System.Windows.Forms.DockStyle.Fill;
            this.treeModules.Font = new System.Drawing.Font("Times New Roman", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.treeModules.ForeColor = System.Drawing.Color.Gainsboro;
            this.treeModules.Location = new System.Drawing.Point(6, 32);
            this.treeModules.Name = "treeModules";
            this.treeModules.ShowNodeToolTips = true;
            this.treeModules.Size = new System.Drawing.Size(188, 599);
            this.treeModules.TabIndex = 0;
            // 
            // lblModules
            // 
            this.lblModules.Dock = System.Windows.Forms.DockStyle.Top;
            this.lblModules.ForeColor = System.Drawing.Color.DeepSkyBlue;
            this.lblModules.Location = new System.Drawing.Point(6, 6);
            this.lblModules.Name = "lblModules";
            this.lblModules.Size = new System.Drawing.Size(188, 26);
            this.lblModules.TabIndex = 1;
            this.lblModules.Text = "工具箱（拖拽到画布）";
            this.lblModules.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // panelRight
            // 
            this.panelRight.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(42)))), ((int)(((byte)(42)))), ((int)(((byte)(50)))));
            this.panelRight.Controls.Add(this.nodeParamPanel);
            this.panelRight.Controls.Add(this.treePorts);
            this.panelRight.Controls.Add(this.lblPorts);
            this.panelRight.Controls.Add(this.lblParams);
            this.panelRight.Dock = System.Windows.Forms.DockStyle.Right;
            this.panelRight.Location = new System.Drawing.Point(755, 0);
            this.panelRight.Name = "panelRight";
            this.panelRight.Padding = new System.Windows.Forms.Padding(6);
            this.panelRight.Size = new System.Drawing.Size(400, 637);
            this.panelRight.TabIndex = 1;
            // 
            // treePorts
            // 
            this.treePorts.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(30)))), ((int)(((byte)(30)))), ((int)(((byte)(38)))));
            this.treePorts.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.treePorts.Dock = System.Windows.Forms.DockStyle.Top;
            this.treePorts.ForeColor = System.Drawing.Color.Gainsboro;
            this.treePorts.Location = new System.Drawing.Point(6, 58);
            this.treePorts.Name = "treePorts";
            this.treePorts.ShowNodeToolTips = true;
            this.treePorts.Size = new System.Drawing.Size(388, 168);
            this.treePorts.TabIndex = 1;
            // 
            // lblPorts
            // 
            this.lblPorts.Dock = System.Windows.Forms.DockStyle.Top;
            this.lblPorts.ForeColor = System.Drawing.Color.DeepSkyBlue;
            this.lblPorts.Location = new System.Drawing.Point(6, 32);
            this.lblPorts.Name = "lblPorts";
            this.lblPorts.Size = new System.Drawing.Size(388, 26);
            this.lblPorts.TabIndex = 2;
            this.lblPorts.Text = "Inputs/Outputs";
            this.lblPorts.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // lblParams
            // 
            this.lblParams.Dock = System.Windows.Forms.DockStyle.Top;
            this.lblParams.ForeColor = System.Drawing.Color.DeepSkyBlue;
            this.lblParams.Location = new System.Drawing.Point(6, 6);
            this.lblParams.Name = "lblParams";
            this.lblParams.Size = new System.Drawing.Size(388, 26);
            this.lblParams.TabIndex = 3;
            this.lblParams.Text = "节点参数";
            this.lblParams.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // panelBottom
            // 
            this.panelBottom.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(34)))), ((int)(((byte)(34)))), ((int)(((byte)(42)))));
            this.panelBottom.Controls.Add(this.grpBind);
            this.panelBottom.Controls.Add(this.grpFlows);
            this.panelBottom.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.panelBottom.Location = new System.Drawing.Point(200, 487);
            this.panelBottom.Name = "panelBottom";
            this.panelBottom.Size = new System.Drawing.Size(555, 150);
            this.panelBottom.TabIndex = 2;
            // 
            // grpBind
            // 
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
            this.grpBind.ForeColor = System.Drawing.Color.DeepSkyBlue;
            this.grpBind.Location = new System.Drawing.Point(380, 8);
            this.grpBind.Name = "grpBind";
            this.grpBind.Size = new System.Drawing.Size(760, 134);
            this.grpBind.TabIndex = 1;
            this.grpBind.TabStop = false;
            this.grpBind.Text = "绑定与运行";
            // 
            // lblCamera
            // 
            this.lblCamera.AutoSize = true;
            this.lblCamera.ForeColor = System.Drawing.Color.Silver;
            this.lblCamera.Location = new System.Drawing.Point(12, 30);
            this.lblCamera.Name = "lblCamera";
            this.lblCamera.Size = new System.Drawing.Size(41, 12);
            this.lblCamera.TabIndex = 0;
            this.lblCamera.Text = "相机：";
            // 
            // cmbCamera
            // 
            this.cmbCamera.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(30)))), ((int)(((byte)(30)))), ((int)(((byte)(38)))));
            this.cmbCamera.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbCamera.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.cmbCamera.ForeColor = System.Drawing.Color.Gainsboro;
            this.cmbCamera.Location = new System.Drawing.Point(62, 27);
            this.cmbCamera.Name = "cmbCamera";
            this.cmbCamera.Size = new System.Drawing.Size(200, 20);
            this.cmbCamera.TabIndex = 0;
            // 
            // lblView
            // 
            this.lblView.AutoSize = true;
            this.lblView.ForeColor = System.Drawing.Color.Silver;
            this.lblView.Location = new System.Drawing.Point(344, 30);
            this.lblView.Name = "lblView";
            this.lblView.Size = new System.Drawing.Size(65, 12);
            this.lblView.TabIndex = 2;
            this.lblView.Text = "显示视图：";
            // 
            // numViewIndex
            // 
            this.numViewIndex.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(30)))), ((int)(((byte)(30)))), ((int)(((byte)(38)))));
            this.numViewIndex.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.numViewIndex.ForeColor = System.Drawing.Color.Gainsboro;
            this.numViewIndex.Location = new System.Drawing.Point(412, 28);
            this.numViewIndex.Maximum = new decimal(new int[] {
            9,
            0,
            0,
            0});
            this.numViewIndex.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.numViewIndex.Name = "numViewIndex";
            this.numViewIndex.Size = new System.Drawing.Size(56, 21);
            this.numViewIndex.TabIndex = 2;
            this.numViewIndex.Value = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.numViewIndex.ValueChanged += new System.EventHandler(this.OnViewIndexChanged);
            // 
            // lblRunStatus
            // 
            this.lblRunStatus.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(30)))), ((int)(((byte)(30)))), ((int)(((byte)(38)))));
            this.lblRunStatus.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.lblRunStatus.ForeColor = System.Drawing.Color.Silver;
            this.lblRunStatus.Location = new System.Drawing.Point(12, 104);
            this.lblRunStatus.Multiline = true;
            this.lblRunStatus.Name = "lblRunStatus";
            this.lblRunStatus.ReadOnly = true;
            this.lblRunStatus.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.lblRunStatus.Size = new System.Drawing.Size(740, 28);
            this.lblRunStatus.TabIndex = 10;
            this.lblRunStatus.Text = "就绪";
            // 
            // grpFlows
            // 
            this.grpFlows.Controls.Add(this.lstFlows);
            this.grpFlows.Controls.Add(this.btnNewFlow);
            this.grpFlows.Controls.Add(this.btnRenameFlow);
            this.grpFlows.Controls.Add(this.btnDeleteFlow);
            this.grpFlows.ForeColor = System.Drawing.Color.DeepSkyBlue;
            this.grpFlows.Location = new System.Drawing.Point(8, 8);
            this.grpFlows.Name = "grpFlows";
            this.grpFlows.Size = new System.Drawing.Size(362, 134);
            this.grpFlows.TabIndex = 0;
            this.grpFlows.TabStop = false;
            this.grpFlows.Text = "流程管理";
            // 
            // lstFlows
            // 
            this.lstFlows.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(30)))), ((int)(((byte)(30)))), ((int)(((byte)(38)))));
            this.lstFlows.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.lstFlows.ForeColor = System.Drawing.Color.Gainsboro;
            this.lstFlows.IntegralHeight = false;
            this.lstFlows.ItemHeight = 12;
            this.lstFlows.Location = new System.Drawing.Point(10, 22);
            this.lstFlows.Name = "lstFlows";
            this.lstFlows.Size = new System.Drawing.Size(200, 100);
            this.lstFlows.TabIndex = 0;
            // 
            // splitMain
            // 
            this.splitMain.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitMain.Location = new System.Drawing.Point(200, 0);
            this.splitMain.Name = "splitMain";
            this.splitMain.Orientation = System.Windows.Forms.Orientation.Horizontal;
            // 
            // splitMain.Panel1
            // 
            this.splitMain.Panel1.Controls.Add(this.canvas);
            // 
            // splitMain.Panel2
            // 
            this.splitMain.Panel2.Controls.Add(this.flowPreview);
            this.splitMain.Size = new System.Drawing.Size(555, 487);
            this.splitMain.SplitterDistance = 345;
            this.splitMain.TabIndex = 3;
            // 
            // canvas
            // 
            this.canvas.AllowDrop = true;
            this.canvas.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(24)))), ((int)(((byte)(24)))), ((int)(((byte)(32)))));
            this.canvas.Dock = System.Windows.Forms.DockStyle.Fill;
            this.canvas.Flow = null;
            this.canvas.Location = new System.Drawing.Point(0, 0);
            this.canvas.Name = "canvas";
            this.canvas.Size = new System.Drawing.Size(555, 345);
            this.canvas.TabIndex = 0;
            // 
            // flowPreview
            // 
            this.flowPreview.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(42)))), ((int)(((byte)(42)))), ((int)(((byte)(50)))));
            this.flowPreview.Dock = System.Windows.Forms.DockStyle.Fill;
            this.flowPreview.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(220)))), ((int)(((byte)(224)))), ((int)(((byte)(232)))));
            this.flowPreview.Location = new System.Drawing.Point(0, 0);
            this.flowPreview.Name = "flowPreview";
            this.flowPreview.ShowContour = true;
            this.flowPreview.Size = new System.Drawing.Size(555, 138);
            this.flowPreview.TabIndex = 0;
            // 
            // btnRefreshBinding
            // 
            this.btnRefreshBinding.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(52)))), ((int)(((byte)(52)))), ((int)(((byte)(62)))));
            this.btnRefreshBinding.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnRefreshBinding.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnRefreshBinding.Font = new System.Drawing.Font("微软雅黑", 11F, System.Drawing.FontStyle.Bold);
            this.btnRefreshBinding.ForeColor = System.Drawing.Color.White;
            this.btnRefreshBinding.Location = new System.Drawing.Point(270, 26);
            this.btnRefreshBinding.Name = "btnRefreshBinding";
            this.btnRefreshBinding.Size = new System.Drawing.Size(60, 27);
            this.btnRefreshBinding.TabIndex = 1;
            this.btnRefreshBinding.Text = "刷新";
            this.btnRefreshBinding.UseVisualStyleBackColor = false;
            this.btnRefreshBinding.Click += new System.EventHandler(this.OnRefreshCameraList);
            // 
            // btnValidate
            // 
            this.btnValidate.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(52)))), ((int)(((byte)(52)))), ((int)(((byte)(62)))));
            this.btnValidate.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnValidate.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnValidate.Font = new System.Drawing.Font("微软雅黑", 11F, System.Drawing.FontStyle.Bold);
            this.btnValidate.ForeColor = System.Drawing.Color.White;
            this.btnValidate.Location = new System.Drawing.Point(496, 26);
            this.btnValidate.Name = "btnValidate";
            this.btnValidate.Size = new System.Drawing.Size(80, 30);
            this.btnValidate.TabIndex = 3;
            this.btnValidate.Text = "校验";
            this.btnValidate.UseVisualStyleBackColor = false;
            this.btnValidate.Click += new System.EventHandler(this.OnValidate);
            // 
            // btnRun
            // 
            this.btnRun.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(52)))), ((int)(((byte)(52)))), ((int)(((byte)(62)))));
            this.btnRun.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnRun.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnRun.Font = new System.Drawing.Font("微软雅黑", 11F, System.Drawing.FontStyle.Bold);
            this.btnRun.ForeColor = System.Drawing.Color.White;
            this.btnRun.Location = new System.Drawing.Point(496, 62);
            this.btnRun.Name = "btnRun";
            this.btnRun.Size = new System.Drawing.Size(80, 30);
            this.btnRun.TabIndex = 4;
            this.btnRun.Text = "运行";
            this.btnRun.UseVisualStyleBackColor = false;
            this.btnRun.Click += new System.EventHandler(this.OnRun);
            // 
            // btnStep
            // 
            this.btnStep.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(52)))), ((int)(((byte)(52)))), ((int)(((byte)(62)))));
            this.btnStep.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnStep.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnStep.Font = new System.Drawing.Font("微软雅黑", 11F, System.Drawing.FontStyle.Bold);
            this.btnStep.ForeColor = System.Drawing.Color.White;
            this.btnStep.Location = new System.Drawing.Point(590, 26);
            this.btnStep.Name = "btnStep";
            this.btnStep.Size = new System.Drawing.Size(80, 30);
            this.btnStep.TabIndex = 6;
            this.btnStep.Text = "单步";
            this.btnStep.UseVisualStyleBackColor = false;
            this.btnStep.Click += new System.EventHandler(this.OnStep);
            // 
            // btnRunLoop
            // 
            this.btnRunLoop.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(52)))), ((int)(((byte)(52)))), ((int)(((byte)(62)))));
            this.btnRunLoop.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnRunLoop.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnRunLoop.Font = new System.Drawing.Font("微软雅黑", 11F, System.Drawing.FontStyle.Bold);
            this.btnRunLoop.ForeColor = System.Drawing.Color.White;
            this.btnRunLoop.Location = new System.Drawing.Point(590, 62);
            this.btnRunLoop.Name = "btnRunLoop";
            this.btnRunLoop.Size = new System.Drawing.Size(80, 30);
            this.btnRunLoop.TabIndex = 7;
            this.btnRunLoop.Text = "连续运行";
            this.btnRunLoop.UseVisualStyleBackColor = false;
            this.btnRunLoop.Click += new System.EventHandler(this.OnRunLoop);
            // 
            // btnRunNode
            // 
            this.btnRunNode.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(52)))), ((int)(((byte)(52)))), ((int)(((byte)(62)))));
            this.btnRunNode.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnRunNode.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnRunNode.Font = new System.Drawing.Font("微软雅黑", 11F, System.Drawing.FontStyle.Bold);
            this.btnRunNode.ForeColor = System.Drawing.Color.White;
            this.btnRunNode.Location = new System.Drawing.Point(682, 26);
            this.btnRunNode.Name = "btnRunNode";
            this.btnRunNode.Size = new System.Drawing.Size(68, 30);
            this.btnRunNode.TabIndex = 9;
            this.btnRunNode.Text = "运行节点";
            this.btnRunNode.UseVisualStyleBackColor = false;
            this.btnRunNode.Click += new System.EventHandler(this.OnRunNode);
            // 
            // btnStop
            // 
            this.btnStop.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(52)))), ((int)(((byte)(52)))), ((int)(((byte)(62)))));
            this.btnStop.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnStop.Enabled = false;
            this.btnStop.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnStop.Font = new System.Drawing.Font("微软雅黑", 11F, System.Drawing.FontStyle.Bold);
            this.btnStop.ForeColor = System.Drawing.Color.White;
            this.btnStop.Location = new System.Drawing.Point(682, 62);
            this.btnStop.Name = "btnStop";
            this.btnStop.Size = new System.Drawing.Size(68, 30);
            this.btnStop.TabIndex = 5;
            this.btnStop.Text = "停止";
            this.btnStop.UseVisualStyleBackColor = false;
            this.btnStop.Click += new System.EventHandler(this.OnStop);
            // 
            // btnAutoLayout
            // 
            this.btnAutoLayout.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(52)))), ((int)(((byte)(52)))), ((int)(((byte)(62)))));
            this.btnAutoLayout.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnAutoLayout.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnAutoLayout.Font = new System.Drawing.Font("微软雅黑", 11F, System.Drawing.FontStyle.Bold);
            this.btnAutoLayout.ForeColor = System.Drawing.Color.White;
            this.btnAutoLayout.Location = new System.Drawing.Point(270, 60);
            this.btnAutoLayout.Name = "btnAutoLayout";
            this.btnAutoLayout.Size = new System.Drawing.Size(110, 28);
            this.btnAutoLayout.TabIndex = 8;
            this.btnAutoLayout.Text = "自动整理布局";
            this.btnAutoLayout.UseVisualStyleBackColor = false;
            this.btnAutoLayout.Click += new System.EventHandler(this.OnAutoLayout);
            // 
            // btnNewFlow
            // 
            this.btnNewFlow.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(52)))), ((int)(((byte)(52)))), ((int)(((byte)(62)))));
            this.btnNewFlow.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnNewFlow.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnNewFlow.Font = new System.Drawing.Font("微软雅黑", 11F, System.Drawing.FontStyle.Bold);
            this.btnNewFlow.ForeColor = System.Drawing.Color.White;
            this.btnNewFlow.Location = new System.Drawing.Point(220, 22);
            this.btnNewFlow.Name = "btnNewFlow";
            this.btnNewFlow.Size = new System.Drawing.Size(130, 28);
            this.btnNewFlow.TabIndex = 1;
            this.btnNewFlow.Text = "新建流程";
            this.btnNewFlow.UseVisualStyleBackColor = false;
            this.btnNewFlow.Click += new System.EventHandler(this.OnNewFlow);
            // 
            // btnRenameFlow
            // 
            this.btnRenameFlow.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(52)))), ((int)(((byte)(52)))), ((int)(((byte)(62)))));
            this.btnRenameFlow.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnRenameFlow.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnRenameFlow.Font = new System.Drawing.Font("微软雅黑", 11F, System.Drawing.FontStyle.Bold);
            this.btnRenameFlow.ForeColor = System.Drawing.Color.White;
            this.btnRenameFlow.Location = new System.Drawing.Point(220, 56);
            this.btnRenameFlow.Name = "btnRenameFlow";
            this.btnRenameFlow.Size = new System.Drawing.Size(130, 28);
            this.btnRenameFlow.TabIndex = 2;
            this.btnRenameFlow.Text = "重命名";
            this.btnRenameFlow.UseVisualStyleBackColor = false;
            this.btnRenameFlow.Click += new System.EventHandler(this.OnRenameFlow);
            // 
            // btnDeleteFlow
            // 
            this.btnDeleteFlow.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(52)))), ((int)(((byte)(52)))), ((int)(((byte)(62)))));
            this.btnDeleteFlow.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnDeleteFlow.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnDeleteFlow.Font = new System.Drawing.Font("微软雅黑", 11F, System.Drawing.FontStyle.Bold);
            this.btnDeleteFlow.ForeColor = System.Drawing.Color.White;
            this.btnDeleteFlow.Location = new System.Drawing.Point(220, 90);
            this.btnDeleteFlow.Name = "btnDeleteFlow";
            this.btnDeleteFlow.Size = new System.Drawing.Size(130, 28);
            this.btnDeleteFlow.TabIndex = 3;
            this.btnDeleteFlow.Text = "删除流程";
            this.btnDeleteFlow.UseVisualStyleBackColor = false;
            this.btnDeleteFlow.Click += new System.EventHandler(this.OnDeleteFlow);
            // 
            // nodeParamPanel
            // 
            this.nodeParamPanel.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(42)))), ((int)(((byte)(42)))), ((int)(((byte)(50)))));
            this.nodeParamPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.nodeParamPanel.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(220)))), ((int)(((byte)(224)))), ((int)(((byte)(232)))));
            this.nodeParamPanel.Location = new System.Drawing.Point(6, 226);
            this.nodeParamPanel.Name = "nodeParamPanel";
            this.nodeParamPanel.Size = new System.Drawing.Size(388, 405);
            this.nodeParamPanel.TabIndex = 0;
            // 
            // FlowForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(38)))), ((int)(((byte)(38)))), ((int)(((byte)(46)))));
            this.ClientSize = new System.Drawing.Size(1155, 637);
            this.Controls.Add(this.splitMain);
            this.Controls.Add(this.panelBottom);
            this.Controls.Add(this.panelRight);
            this.Controls.Add(this.panelLeft);
            this.Font = new System.Drawing.Font("宋体", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.ForeColor = System.Drawing.Color.Gainsboro;
            this.MinimumSize = new System.Drawing.Size(1155, 637);
            this.Name = "FlowForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = " ";
            this.panelLeft.ResumeLayout(false);
            this.panelRight.ResumeLayout(false);
            this.panelBottom.ResumeLayout(false);
            this.grpBind.ResumeLayout(false);
            this.grpBind.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numViewIndex)).EndInit();
            this.grpFlows.ResumeLayout(false);
            this.splitMain.Panel1.ResumeLayout(false);
            this.splitMain.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.splitMain)).EndInit();
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
        private System.Windows.Forms.TextBox lblRunStatus;

        private System.Windows.Forms.SplitContainer splitMain;
        private FlowCanvasControl canvas;
        private FlowPreviewPanel flowPreview;
    }
}
