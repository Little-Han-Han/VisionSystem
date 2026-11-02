namespace VisionPlatform.UI
{
    partial class CalibForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null)) components.Dispose();
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// 控件树与布局（左侧参数 / 右侧图像预览）。
        /// 【铁律】本文件只写结构与尺寸，颜色 / 字体一律由构造函数末尾的
        /// <c>UiStyle.ApplyForm(this)</c> + <c>ApplySemanticColors()</c> 统一套用（主题单一来源 UiTheme）。
        /// <para>
        /// 【左栏为什么用 TableLayoutPanel】四个分区（列表 / N 点 / 结果 / 占位）按比例分配高度，
        /// 缩放时不需要任何手写 OnResize 计算，也就没有"构造期字段还没 new 就改子控件位置"的空引用风险。
        /// </para>
        /// <para>
        /// 【停靠顺序】同一容器内 Dock 的生效顺序与 Controls 集合逆序相关：
        /// 本文件统一按「Fill 先 Add、Bottom 后 Add」的既有范式（见 CommForm / CameraForm），
        /// 保证列表/表格占据剩余空间、按钮行贴在底部。
        /// </para>
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();

            this.panelTop = new System.Windows.Forms.Panel();
            this.lblCamera = new System.Windows.Forms.Label();
            this.cboCamera = new System.Windows.Forms.ComboBox();
            this.btnRefreshCam = new VisionPlatform.UI.UiButton();
            this.chkLive = new System.Windows.Forms.CheckBox();
            this.btnGrabFrame = new VisionPlatform.UI.UiButton();

            this.panelStatus = new System.Windows.Forms.Panel();
            this.lblStatus = new System.Windows.Forms.Label();

            this.panelLeft = new System.Windows.Forms.Panel();
            this.tableLeft = new System.Windows.Forms.TableLayoutPanel();
            this.groupList = new System.Windows.Forms.GroupBox();
            this.listRecords = new System.Windows.Forms.ListView();
            this.colRecName = new System.Windows.Forms.ColumnHeader();
            this.colRecKind = new System.Windows.Forms.ColumnHeader();
            this.colRecRms = new System.Windows.Forms.ColumnHeader();
            this.colRecTime = new System.Windows.Forms.ColumnHeader();
            this.panelRecordBtns = new System.Windows.Forms.Panel();
            this.btnNewRecord = new VisionPlatform.UI.UiButton();
            this.btnRenameRecord = new VisionPlatform.UI.UiButton();
            this.btnDeleteRecord = new VisionPlatform.UI.UiButton();
            this.btnSetCurrent = new VisionPlatform.UI.UiButton();

            this.groupPoints = new System.Windows.Forms.GroupBox();
            this.gridPoints = new System.Windows.Forms.DataGridView();
            this.panelPointOps = new System.Windows.Forms.Panel();
            this.lblComm = new System.Windows.Forms.Label();
            this.cboComm = new System.Windows.Forms.ComboBox();
            this.lblCmd = new System.Windows.Forms.Label();
            this.txtCmd = new System.Windows.Forms.TextBox();
            this.btnReadCoord = new VisionPlatform.UI.UiButton();
            this.btnAddPoint = new VisionPlatform.UI.UiButton();
            this.btnDelPoint = new VisionPlatform.UI.UiButton();
            this.btnCalc = new VisionPlatform.UI.UiButton();

            this.groupResult = new System.Windows.Forms.GroupBox();
            this.txtResult = new System.Windows.Forms.TextBox();

            this.groupTodo = new System.Windows.Forms.GroupBox();
            this.lblTodo = new System.Windows.Forms.Label();

            this.panelRight = new System.Windows.Forms.Panel();
            this.imageViewer = new VisionSystem.Controls.ImageViewer();

            this.panelTop.SuspendLayout();
            this.panelStatus.SuspendLayout();
            this.panelLeft.SuspendLayout();
            this.tableLeft.SuspendLayout();
            this.groupList.SuspendLayout();
            this.panelRecordBtns.SuspendLayout();
            this.groupPoints.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.gridPoints)).BeginInit();
            this.panelPointOps.SuspendLayout();
            this.groupResult.SuspendLayout();
            this.groupTodo.SuspendLayout();
            this.panelRight.SuspendLayout();
            this.SuspendLayout();

            // ---------- panelTop：相机选择 + 实时 / 手动触发 ----------
            this.panelTop.Controls.Add(this.lblCamera);
            this.panelTop.Controls.Add(this.cboCamera);
            this.panelTop.Controls.Add(this.btnRefreshCam);
            this.panelTop.Controls.Add(this.chkLive);
            this.panelTop.Controls.Add(this.btnGrabFrame);
            this.panelTop.Dock = System.Windows.Forms.DockStyle.Top;
            this.panelTop.Location = new System.Drawing.Point(0, 0);
            this.panelTop.Name = "panelTop";
            this.panelTop.Size = new System.Drawing.Size(1180, 46);
            this.panelTop.TabIndex = 0;

            this.lblCamera.AutoSize = false;
            this.lblCamera.Location = new System.Drawing.Point(12, 13);
            this.lblCamera.Name = "lblCamera";
            this.lblCamera.Size = new System.Drawing.Size(42, 22);
            this.lblCamera.TabIndex = 0;
            this.lblCamera.Text = "相机:";
            this.lblCamera.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            this.cboCamera.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cboCamera.Location = new System.Drawing.Point(56, 11);
            this.cboCamera.Name = "cboCamera";
            this.cboCamera.Size = new System.Drawing.Size(210, 25);
            this.cboCamera.TabIndex = 1;
            this.cboCamera.SelectedIndexChanged += new System.EventHandler(this.OnCameraSelectionChanged);

            this.btnRefreshCam.Location = new System.Drawing.Point(274, 10);
            this.btnRefreshCam.Name = "btnRefreshCam";
            this.btnRefreshCam.Size = new System.Drawing.Size(80, 27);
            this.btnRefreshCam.TabIndex = 2;
            this.btnRefreshCam.Text = "刷新";
            this.btnRefreshCam.Click += new System.EventHandler(this.OnRefreshCameras);

            this.chkLive.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.chkLive.AutoSize = true;
            this.chkLive.Location = new System.Drawing.Point(978, 14);
            this.chkLive.Name = "chkLive";
            this.chkLive.Size = new System.Drawing.Size(66, 20);
            this.chkLive.TabIndex = 3;
            this.chkLive.Text = "实时";
            this.chkLive.UseVisualStyleBackColor = true;
            this.chkLive.CheckedChanged += new System.EventHandler(this.OnToggleLive);

            this.btnGrabFrame.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnGrabFrame.Location = new System.Drawing.Point(1062, 10);
            this.btnGrabFrame.Name = "btnGrabFrame";
            this.btnGrabFrame.Size = new System.Drawing.Size(104, 27);
            this.btnGrabFrame.TabIndex = 4;
            this.btnGrabFrame.Text = "手动触发";
            this.btnGrabFrame.Click += new System.EventHandler(this.OnManualGrab);

            // ---------- panelStatus：状态栏（取帧 / 通讯 / 解析结果统一在这里给反馈） ----------
            this.panelStatus.Controls.Add(this.lblStatus);
            this.panelStatus.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.panelStatus.Location = new System.Drawing.Point(0, 694);
            this.panelStatus.Name = "panelStatus";
            this.panelStatus.Padding = new System.Windows.Forms.Padding(8, 0, 8, 0);
            this.panelStatus.Size = new System.Drawing.Size(1180, 26);
            this.panelStatus.TabIndex = 3;

            this.lblStatus.AutoSize = false;
            this.lblStatus.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblStatus.Location = new System.Drawing.Point(8, 0);
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Size = new System.Drawing.Size(1164, 26);
            this.lblStatus.TabIndex = 0;
            this.lblStatus.Text = "";
            this.lblStatus.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            // ---------- panelLeft：左侧参数区 ----------
            this.panelLeft.Controls.Add(this.tableLeft);
            this.panelLeft.Dock = System.Windows.Forms.DockStyle.Left;
            this.panelLeft.Location = new System.Drawing.Point(0, 46);
            this.panelLeft.Name = "panelLeft";
            this.panelLeft.Padding = new System.Windows.Forms.Padding(8);
            this.panelLeft.Size = new System.Drawing.Size(440, 648);
            this.panelLeft.TabIndex = 1;

            this.tableLeft.ColumnCount = 1;
            this.tableLeft.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tableLeft.Controls.Add(this.groupList, 0, 0);
            this.tableLeft.Controls.Add(this.groupPoints, 0, 1);
            this.tableLeft.Controls.Add(this.groupResult, 0, 2);
            this.tableLeft.Controls.Add(this.groupTodo, 0, 3);
            this.tableLeft.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tableLeft.Location = new System.Drawing.Point(8, 8);
            this.tableLeft.Name = "tableLeft";
            this.tableLeft.RowCount = 4;
            this.tableLeft.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 34F));
            this.tableLeft.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 42F));
            this.tableLeft.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 24F));
            this.tableLeft.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 88F));
            this.tableLeft.Size = new System.Drawing.Size(424, 632);
            this.tableLeft.TabIndex = 0;

            // ---------- groupList：标定列表（同一相机支持多条记录） ----------
            this.groupList.Controls.Add(this.listRecords);
            this.groupList.Controls.Add(this.panelRecordBtns);
            this.groupList.Dock = System.Windows.Forms.DockStyle.Fill;
            this.groupList.Location = new System.Drawing.Point(3, 3);
            this.groupList.Name = "groupList";
            this.groupList.Padding = new System.Windows.Forms.Padding(8, 6, 8, 8);
            this.groupList.Size = new System.Drawing.Size(418, 179);
            this.groupList.TabIndex = 0;
            this.groupList.TabStop = false;
            this.groupList.Text = "标定列表";

            this.listRecords.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.listRecords.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
                this.colRecName,
                this.colRecKind,
                this.colRecRms,
                this.colRecTime});
            this.listRecords.Dock = System.Windows.Forms.DockStyle.Fill;
            this.listRecords.FullRowSelect = true;
            this.listRecords.GridLines = true;
            this.listRecords.HideSelection = false;
            this.listRecords.Location = new System.Drawing.Point(8, 26);
            this.listRecords.MultiSelect = false;
            this.listRecords.Name = "listRecords";
            this.listRecords.Size = new System.Drawing.Size(402, 111);
            this.listRecords.TabIndex = 0;
            this.listRecords.UseCompatibleStateImageBehavior = false;
            this.listRecords.View = System.Windows.Forms.View.Details;
            this.listRecords.SelectedIndexChanged += new System.EventHandler(this.OnRecordSelectionChanged);

            this.colRecName.Text = "名称";
            this.colRecName.Width = 148;

            this.colRecKind.Text = "类型";
            this.colRecKind.Width = 76;

            this.colRecRms.Text = "RMS";
            this.colRecRms.Width = 70;

            this.colRecTime.Text = "时间";
            this.colRecTime.Width = 96;

            this.panelRecordBtns.Controls.Add(this.btnNewRecord);
            this.panelRecordBtns.Controls.Add(this.btnRenameRecord);
            this.panelRecordBtns.Controls.Add(this.btnDeleteRecord);
            this.panelRecordBtns.Controls.Add(this.btnSetCurrent);
            this.panelRecordBtns.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.panelRecordBtns.Location = new System.Drawing.Point(8, 137);
            this.panelRecordBtns.Name = "panelRecordBtns";
            this.panelRecordBtns.Size = new System.Drawing.Size(402, 34);
            this.panelRecordBtns.TabIndex = 1;

            this.btnNewRecord.Location = new System.Drawing.Point(0, 4);
            this.btnNewRecord.Name = "btnNewRecord";
            this.btnNewRecord.Size = new System.Drawing.Size(94, 26);
            this.btnNewRecord.TabIndex = 0;
            this.btnNewRecord.Text = "新建";
            this.btnNewRecord.Click += new System.EventHandler(this.OnNewRecord);

            this.btnRenameRecord.Location = new System.Drawing.Point(98, 4);
            this.btnRenameRecord.Name = "btnRenameRecord";
            this.btnRenameRecord.Size = new System.Drawing.Size(94, 26);
            this.btnRenameRecord.TabIndex = 1;
            this.btnRenameRecord.Text = "重命名";
            this.btnRenameRecord.Click += new System.EventHandler(this.OnRenameRecord);

            this.btnDeleteRecord.Location = new System.Drawing.Point(196, 4);
            this.btnDeleteRecord.Name = "btnDeleteRecord";
            this.btnDeleteRecord.Size = new System.Drawing.Size(94, 26);
            this.btnDeleteRecord.TabIndex = 2;
            this.btnDeleteRecord.Text = "删除";
            this.btnDeleteRecord.Click += new System.EventHandler(this.OnDeleteRecord);

            this.btnSetCurrent.Location = new System.Drawing.Point(294, 4);
            this.btnSetCurrent.Name = "btnSetCurrent";
            this.btnSetCurrent.Size = new System.Drawing.Size(100, 26);
            this.btnSetCurrent.TabIndex = 3;
            this.btnSetCurrent.Text = "设为当前";
            this.btnSetCurrent.Click += new System.EventHandler(this.OnSetCurrentRecord);

            // ---------- groupPoints：N 点标定（像素 ↔ 机械 对应点表 + 通讯取点） ----------
            this.groupPoints.Controls.Add(this.gridPoints);
            this.groupPoints.Controls.Add(this.panelPointOps);
            this.groupPoints.Dock = System.Windows.Forms.DockStyle.Fill;
            this.groupPoints.Location = new System.Drawing.Point(3, 188);
            this.groupPoints.Name = "groupPoints";
            this.groupPoints.Padding = new System.Windows.Forms.Padding(8, 6, 8, 8);
            this.groupPoints.Size = new System.Drawing.Size(418, 229);
            this.groupPoints.TabIndex = 1;
            this.groupPoints.TabStop = false;
            this.groupPoints.Text = "N 点标定（像素 ↔ 机械）";

            this.gridPoints.AllowUserToAddRows = false;
            this.gridPoints.AllowUserToResizeRows = true;
            this.gridPoints.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;
            this.gridPoints.BackgroundColor = System.Drawing.Color.FromArgb(((int)(((byte)(38)))), ((int)(((byte)(38)))), ((int)(((byte)(46)))));
            this.gridPoints.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.gridPoints.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            this.gridPoints.ColumnHeadersHeight = 30;   // 表头带 "(mm)/(px)" 单位，默认高度会把单位裁掉
            this.gridPoints.Dock = System.Windows.Forms.DockStyle.Fill;
            this.gridPoints.EditMode = System.Windows.Forms.DataGridViewEditMode.EditOnKeystrokeOrF2;
            this.gridPoints.Location = new System.Drawing.Point(8, 26);
            this.gridPoints.MultiSelect = false;
            this.gridPoints.Name = "gridPoints";
            this.gridPoints.RowHeadersVisible = false;
            this.gridPoints.RowTemplate.Height = 24;
            this.gridPoints.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.CellSelect;
            this.gridPoints.Size = new System.Drawing.Size(402, 95);
            this.gridPoints.TabIndex = 0;

            this.panelPointOps.Controls.Add(this.lblComm);
            this.panelPointOps.Controls.Add(this.cboComm);
            this.panelPointOps.Controls.Add(this.lblCmd);
            this.panelPointOps.Controls.Add(this.txtCmd);
            this.panelPointOps.Controls.Add(this.btnReadCoord);
            this.panelPointOps.Controls.Add(this.btnAddPoint);
            this.panelPointOps.Controls.Add(this.btnDelPoint);
            this.panelPointOps.Controls.Add(this.btnCalc);
            this.panelPointOps.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.panelPointOps.Location = new System.Drawing.Point(8, 121);
            this.panelPointOps.Name = "panelPointOps";
            this.panelPointOps.Size = new System.Drawing.Size(402, 100);
            this.panelPointOps.TabIndex = 1;

            this.lblComm.AutoSize = false;
            this.lblComm.Location = new System.Drawing.Point(0, 4);
            this.lblComm.Name = "lblComm";
            this.lblComm.Size = new System.Drawing.Size(42, 22);
            this.lblComm.TabIndex = 0;
            this.lblComm.Text = "通讯";
            this.lblComm.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            this.cboComm.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cboComm.Location = new System.Drawing.Point(46, 3);
            this.cboComm.Name = "cboComm";
            this.cboComm.Size = new System.Drawing.Size(170, 25);
            this.cboComm.TabIndex = 1;

            this.lblCmd.AutoSize = false;
            this.lblCmd.Location = new System.Drawing.Point(0, 34);
            this.lblCmd.Name = "lblCmd";
            this.lblCmd.Size = new System.Drawing.Size(42, 22);
            this.lblCmd.TabIndex = 2;
            this.lblCmd.Text = "指令";
            this.lblCmd.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            this.txtCmd.Location = new System.Drawing.Point(46, 33);
            this.txtCmd.Name = "txtCmd";
            this.txtCmd.Size = new System.Drawing.Size(192, 25);
            this.txtCmd.TabIndex = 3;

            this.btnReadCoord.Location = new System.Drawing.Point(248, 32);
            this.btnReadCoord.Name = "btnReadCoord";
            this.btnReadCoord.Size = new System.Drawing.Size(132, 27);
            this.btnReadCoord.TabIndex = 4;
            this.btnReadCoord.Text = "读取坐标";
            this.btnReadCoord.Click += new System.EventHandler(this.OnReadCoord);

            this.btnAddPoint.Location = new System.Drawing.Point(0, 68);
            this.btnAddPoint.Name = "btnAddPoint";
            this.btnAddPoint.Size = new System.Drawing.Size(88, 27);
            this.btnAddPoint.TabIndex = 5;
            this.btnAddPoint.Text = "添加行";
            this.btnAddPoint.Click += new System.EventHandler(this.OnAddPointRow);

            this.btnDelPoint.Location = new System.Drawing.Point(94, 68);
            this.btnDelPoint.Name = "btnDelPoint";
            this.btnDelPoint.Size = new System.Drawing.Size(88, 27);
            this.btnDelPoint.TabIndex = 6;
            this.btnDelPoint.Text = "删除行";
            this.btnDelPoint.Click += new System.EventHandler(this.OnDeletePointRow);

            this.btnCalc.Location = new System.Drawing.Point(272, 68);
            this.btnCalc.Name = "btnCalc";
            this.btnCalc.Size = new System.Drawing.Size(108, 27);
            this.btnCalc.TabIndex = 7;
            this.btnCalc.Text = "计算";
            this.btnCalc.Click += new System.EventHandler(this.OnCalculate);

            // ---------- groupResult：结果（只读文本） ----------
            this.groupResult.Controls.Add(this.txtResult);
            this.groupResult.Dock = System.Windows.Forms.DockStyle.Fill;
            this.groupResult.Location = new System.Drawing.Point(3, 423);
            this.groupResult.Name = "groupResult";
            this.groupResult.Padding = new System.Windows.Forms.Padding(8, 6, 8, 8);
            this.groupResult.Size = new System.Drawing.Size(418, 131);
            this.groupResult.TabIndex = 2;
            this.groupResult.TabStop = false;
            this.groupResult.Text = "结果";

            this.txtResult.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.txtResult.Dock = System.Windows.Forms.DockStyle.Fill;
            this.txtResult.Location = new System.Drawing.Point(8, 26);
            this.txtResult.Multiline = true;
            this.txtResult.Name = "txtResult";
            this.txtResult.ReadOnly = true;
            this.txtResult.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.txtResult.Size = new System.Drawing.Size(402, 97);
            this.txtResult.TabIndex = 0;
            this.txtResult.WordWrap = false;

            // ---------- groupTodo：本轮不做的两项（占位，不放假 UI） ----------
            this.groupTodo.Controls.Add(this.lblTodo);
            this.groupTodo.Dock = System.Windows.Forms.DockStyle.Fill;
            this.groupTodo.Location = new System.Drawing.Point(3, 560);
            this.groupTodo.Name = "groupTodo";
            this.groupTodo.Padding = new System.Windows.Forms.Padding(8, 6, 8, 8);
            this.groupTodo.Size = new System.Drawing.Size(418, 69);
            this.groupTodo.TabIndex = 3;
            this.groupTodo.TabStop = false;
            this.groupTodo.Text = "畸变矫正 / 旋转中心";

            this.lblTodo.AutoSize = false;
            this.lblTodo.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblTodo.Location = new System.Drawing.Point(8, 26);
            this.lblTodo.Name = "lblTodo";
            this.lblTodo.Size = new System.Drawing.Size(402, 35);
            this.lblTodo.TabIndex = 0;
            this.lblTodo.Text = "相机畸变矫正（棋盘格 / 圆点）与旋转中心标定：下一轮实现。";
            this.lblTodo.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            // ---------- panelRight：图像预览（直接复用 VisionSystem.Controls.ImageViewer） ----------
            this.panelRight.Controls.Add(this.imageViewer);
            this.panelRight.Dock = System.Windows.Forms.DockStyle.Fill;
            this.panelRight.Location = new System.Drawing.Point(440, 46);
            this.panelRight.Name = "panelRight";
            this.panelRight.Padding = new System.Windows.Forms.Padding(6);
            this.panelRight.Size = new System.Drawing.Size(740, 648);
            this.panelRight.TabIndex = 2;

            this.imageViewer.Dock = System.Windows.Forms.DockStyle.Fill;
            this.imageViewer.Location = new System.Drawing.Point(6, 6);
            this.imageViewer.Name = "imageViewer";
            this.imageViewer.Size = new System.Drawing.Size(728, 636);
            this.imageViewer.TabIndex = 0;

            // ---------- CalibForm ----------
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1180, 720);
            this.Controls.Add(this.panelRight);
            this.Controls.Add(this.panelLeft);
            this.Controls.Add(this.panelStatus);
            this.Controls.Add(this.panelTop);
            this.MinimumSize = new System.Drawing.Size(1040, 620);
            this.Name = "CalibForm";
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "相机标定";

            this.panelTop.ResumeLayout(false);
            this.panelTop.PerformLayout();
            this.panelStatus.ResumeLayout(false);
            this.panelLeft.ResumeLayout(false);
            this.tableLeft.ResumeLayout(false);
            this.groupList.ResumeLayout(false);
            this.panelRecordBtns.ResumeLayout(false);
            this.groupPoints.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.gridPoints)).EndInit();
            this.panelPointOps.ResumeLayout(false);
            this.groupResult.ResumeLayout(false);
            this.groupResult.PerformLayout();
            this.groupTodo.ResumeLayout(false);
            this.panelRight.ResumeLayout(false);
            this.ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.Panel panelTop;
        private System.Windows.Forms.Label lblCamera;
        private System.Windows.Forms.ComboBox cboCamera;
        private VisionPlatform.UI.UiButton btnRefreshCam;
        private System.Windows.Forms.CheckBox chkLive;
        private VisionPlatform.UI.UiButton btnGrabFrame;

        private System.Windows.Forms.Panel panelStatus;
        private System.Windows.Forms.Label lblStatus;

        private System.Windows.Forms.Panel panelLeft;
        private System.Windows.Forms.TableLayoutPanel tableLeft;

        private System.Windows.Forms.GroupBox groupList;
        private System.Windows.Forms.ListView listRecords;
        private System.Windows.Forms.ColumnHeader colRecName;
        private System.Windows.Forms.ColumnHeader colRecKind;
        private System.Windows.Forms.ColumnHeader colRecRms;
        private System.Windows.Forms.ColumnHeader colRecTime;
        private System.Windows.Forms.Panel panelRecordBtns;
        private VisionPlatform.UI.UiButton btnNewRecord;
        private VisionPlatform.UI.UiButton btnRenameRecord;
        private VisionPlatform.UI.UiButton btnDeleteRecord;
        private VisionPlatform.UI.UiButton btnSetCurrent;

        private System.Windows.Forms.GroupBox groupPoints;
        private System.Windows.Forms.DataGridView gridPoints;
        private System.Windows.Forms.Panel panelPointOps;
        private System.Windows.Forms.Label lblComm;
        private System.Windows.Forms.ComboBox cboComm;
        private System.Windows.Forms.Label lblCmd;
        private System.Windows.Forms.TextBox txtCmd;
        private VisionPlatform.UI.UiButton btnReadCoord;
        private VisionPlatform.UI.UiButton btnAddPoint;
        private VisionPlatform.UI.UiButton btnDelPoint;
        private VisionPlatform.UI.UiButton btnCalc;

        private System.Windows.Forms.GroupBox groupResult;
        private System.Windows.Forms.TextBox txtResult;

        private System.Windows.Forms.GroupBox groupTodo;
        private System.Windows.Forms.Label lblTodo;

        private System.Windows.Forms.Panel panelRight;
        private VisionSystem.Controls.ImageViewer imageViewer;
    }
}
