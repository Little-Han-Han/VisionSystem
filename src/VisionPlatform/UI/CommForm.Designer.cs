namespace VisionPlatform.UI
{
    partial class CommForm
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
            this.lblListTitle = new System.Windows.Forms.Label();
            this.gridEndpoints = new System.Windows.Forms.DataGridView();
            this.panelListBtns = new System.Windows.Forms.Panel();
            this.btnAdd = new VisionPlatform.UI.UiButton();
            this.btnCopy = new VisionPlatform.UI.UiButton();
            this.btnDelete = new VisionPlatform.UI.UiButton();

            this.panelRight = new System.Windows.Forms.Panel();
            this.groupProps = new System.Windows.Forms.GroupBox();
            this.lblName = new System.Windows.Forms.Label();
            this.txtName = new System.Windows.Forms.TextBox();
            this.lblKind = new System.Windows.Forms.Label();
            this.cboKind = new System.Windows.Forms.ComboBox();
            this.lblHost = new System.Windows.Forms.Label();
            this.txtHost = new System.Windows.Forms.TextBox();
            this.lblPort = new System.Windows.Forms.Label();
            this.numPort = new System.Windows.Forms.NumericUpDown();
            this.lblSlaveId = new System.Windows.Forms.Label();
            this.numSlaveId = new System.Windows.Forms.NumericUpDown();
            this.chkEnabled = new System.Windows.Forms.CheckBox();
            this.chkAutoConnect = new System.Windows.Forms.CheckBox();
            this.btnSave = new VisionPlatform.UI.UiButton();
            this.btnConnect = new VisionPlatform.UI.UiButton();
            this.btnDisconnect = new VisionPlatform.UI.UiButton();
            this.btnTest = new VisionPlatform.UI.UiButton();

            this.groupOps = new System.Windows.Forms.GroupBox();
            this.panelTcpClient = new System.Windows.Forms.Panel();
            this.lblSend = new System.Windows.Forms.Label();
            this.txtSend = new System.Windows.Forms.TextBox();
            this.btnSend = new VisionPlatform.UI.UiButton();
            this.panelTcpServer = new System.Windows.Forms.Panel();
            this.lblSrvSend = new System.Windows.Forms.Label();
            this.txtSrvSend = new System.Windows.Forms.TextBox();
            this.btnBroadcast = new VisionPlatform.UI.UiButton();
            this.btnStartListen = new VisionPlatform.UI.UiButton();
            this.btnStopListen = new VisionPlatform.UI.UiButton();
            this.panelModbus = new System.Windows.Forms.Panel();
            this.lblAddr = new System.Windows.Forms.Label();
            this.numAddr = new System.Windows.Forms.NumericUpDown();
            this.lblCount = new System.Windows.Forms.Label();
            this.numCount = new System.Windows.Forms.NumericUpDown();
            this.lblValue = new System.Windows.Forms.Label();
            this.numValue = new System.Windows.Forms.NumericUpDown();
            this.btnMbRead = new VisionPlatform.UI.UiButton();
            this.btnMbWrite = new VisionPlatform.UI.UiButton();

            this.groupLog = new System.Windows.Forms.GroupBox();
            this.listLog = new System.Windows.Forms.ListBox();

            ((System.ComponentModel.ISupportInitialize)(this.gridEndpoints)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numPort)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numSlaveId)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numAddr)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numCount)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numValue)).BeginInit();
            this.panelLeft.SuspendLayout();
            this.panelListBtns.SuspendLayout();
            this.panelRight.SuspendLayout();
            this.groupProps.SuspendLayout();
            this.groupOps.SuspendLayout();
            this.panelTcpClient.SuspendLayout();
            this.panelTcpServer.SuspendLayout();
            this.panelModbus.SuspendLayout();
            this.groupLog.SuspendLayout();
            this.SuspendLayout();

            // ---------- panelLeft：通讯列表 ----------
            this.panelLeft.Controls.Add(this.gridEndpoints);
            this.panelLeft.Controls.Add(this.panelListBtns);
            this.panelLeft.Controls.Add(this.lblListTitle);
            this.panelLeft.Dock = System.Windows.Forms.DockStyle.Left;
            this.panelLeft.Location = new System.Drawing.Point(0, 0);
            this.panelLeft.Name = "panelLeft";
            this.panelLeft.Padding = new System.Windows.Forms.Padding(8);
            this.panelLeft.Size = new System.Drawing.Size(460, 620);
            this.panelLeft.TabIndex = 0;

            this.lblListTitle.Dock = System.Windows.Forms.DockStyle.Top;
            this.lblListTitle.Location = new System.Drawing.Point(8, 8);
            this.lblListTitle.Name = "lblListTitle";
            this.lblListTitle.Size = new System.Drawing.Size(444, 30);
            this.lblListTitle.TabIndex = 0;
            this.lblListTitle.Text = "  通讯列表";
            this.lblListTitle.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            this.gridEndpoints.Dock = System.Windows.Forms.DockStyle.Fill;
            this.gridEndpoints.Location = new System.Drawing.Point(8, 38);
            this.gridEndpoints.Name = "gridEndpoints";
            this.gridEndpoints.Size = new System.Drawing.Size(444, 526);
            this.gridEndpoints.TabIndex = 1;

            this.panelListBtns.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.panelListBtns.Location = new System.Drawing.Point(8, 564);
            this.panelListBtns.Name = "panelListBtns";
            this.panelListBtns.Size = new System.Drawing.Size(444, 48);
            this.panelListBtns.TabIndex = 2;
            // 子控件必须逐个 Add：漏掉的话控件没有 Parent，永不显示也不响应事件
            this.panelListBtns.Controls.Add(this.btnAdd);
            this.panelListBtns.Controls.Add(this.btnCopy);
            this.panelListBtns.Controls.Add(this.btnDelete);

            this.btnAdd.Location = new System.Drawing.Point(0, 8);
            this.btnAdd.Name = "btnAdd";
            this.btnAdd.Size = new System.Drawing.Size(96, 32);
            this.btnAdd.TabIndex = 0;
            this.btnAdd.Text = "新增";
            this.btnAdd.UseVisualStyleBackColor = true;

            this.btnCopy.Location = new System.Drawing.Point(104, 8);
            this.btnCopy.Name = "btnCopy";
            this.btnCopy.Size = new System.Drawing.Size(96, 32);
            this.btnCopy.TabIndex = 1;
            this.btnCopy.Text = "复制";
            this.btnCopy.UseVisualStyleBackColor = true;

            this.btnDelete.Location = new System.Drawing.Point(208, 8);
            this.btnDelete.Name = "btnDelete";
            this.btnDelete.Size = new System.Drawing.Size(96, 32);
            this.btnDelete.TabIndex = 2;
            this.btnDelete.Text = "删除";
            this.btnDelete.UseVisualStyleBackColor = true;

            // ---------- panelRight：属性编辑 + 操作 + 日志 ----------
            this.panelRight.Controls.Add(this.groupLog);
            this.panelRight.Controls.Add(this.groupOps);
            this.panelRight.Controls.Add(this.groupProps);
            this.panelRight.Dock = System.Windows.Forms.DockStyle.Fill;
            this.panelRight.Location = new System.Drawing.Point(460, 0);
            this.panelRight.Name = "panelRight";
            this.panelRight.Padding = new System.Windows.Forms.Padding(8);
            this.panelRight.Size = new System.Drawing.Size(620, 620);
            this.panelRight.TabIndex = 1;

            // ---------- groupProps：通讯属性 ----------
            this.groupProps.Dock = System.Windows.Forms.DockStyle.Top;
            this.groupProps.Location = new System.Drawing.Point(8, 8);
            this.groupProps.Name = "groupProps";
            this.groupProps.Size = new System.Drawing.Size(604, 176);
            this.groupProps.TabIndex = 0;
            this.groupProps.TabStop = false;
            this.groupProps.Text = "通讯属性";
            // 子控件必须逐个 Add：漏掉的话控件没有 Parent，永不显示也不响应事件
            this.groupProps.Controls.Add(this.txtName);
            this.groupProps.Controls.Add(this.cboKind);
            this.groupProps.Controls.Add(this.txtHost);
            this.groupProps.Controls.Add(this.numPort);
            this.groupProps.Controls.Add(this.numSlaveId);
            this.groupProps.Controls.Add(this.chkEnabled);
            this.groupProps.Controls.Add(this.chkAutoConnect);
            this.groupProps.Controls.Add(this.btnSave);
            this.groupProps.Controls.Add(this.btnConnect);
            this.groupProps.Controls.Add(this.btnDisconnect);
            this.groupProps.Controls.Add(this.btnTest);
            this.groupProps.Controls.Add(this.lblName);
            this.groupProps.Controls.Add(this.lblKind);
            this.groupProps.Controls.Add(this.lblHost);
            this.groupProps.Controls.Add(this.lblPort);
            this.groupProps.Controls.Add(this.lblSlaveId);

            this.lblName.AutoSize = true;
            this.lblName.Location = new System.Drawing.Point(12, 27);
            this.lblName.Name = "lblName";
            this.lblName.Size = new System.Drawing.Size(44, 15);
            this.lblName.TabIndex = 0;
            this.lblName.Text = "名称：";
            this.lblName.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            this.txtName.Location = new System.Drawing.Point(60, 24);
            this.txtName.Name = "txtName";
            this.txtName.Size = new System.Drawing.Size(150, 21);
            this.txtName.TabIndex = 1;

            this.lblKind.AutoSize = true;
            this.lblKind.Location = new System.Drawing.Point(226, 27);
            this.lblKind.Name = "lblKind";
            this.lblKind.Size = new System.Drawing.Size(44, 15);
            this.lblKind.TabIndex = 2;
            this.lblKind.Text = "类型：";
            this.lblKind.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            this.cboKind.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cboKind.FormattingEnabled = true;
            this.cboKind.Items.AddRange(new object[] { "TCP 客户端", "TCP 服务端", "Modbus TCP" });
            this.cboKind.Location = new System.Drawing.Point(274, 24);
            this.cboKind.Name = "cboKind";
            this.cboKind.Size = new System.Drawing.Size(130, 20);
            this.cboKind.TabIndex = 3;

            this.lblHost.AutoSize = true;
            this.lblHost.Location = new System.Drawing.Point(12, 57);
            this.lblHost.Name = "lblHost";
            this.lblHost.Size = new System.Drawing.Size(56, 15);
            this.lblHost.TabIndex = 4;
            this.lblHost.Text = "IP/主机：";
            this.lblHost.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            this.txtHost.Location = new System.Drawing.Point(74, 54);
            this.txtHost.Name = "txtHost";
            this.txtHost.Size = new System.Drawing.Size(136, 21);
            this.txtHost.TabIndex = 5;

            this.lblPort.AutoSize = true;
            this.lblPort.Location = new System.Drawing.Point(226, 57);
            this.lblPort.Name = "lblPort";
            this.lblPort.Size = new System.Drawing.Size(44, 15);
            this.lblPort.TabIndex = 6;
            this.lblPort.Text = "端口：";
            this.lblPort.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            this.numPort.Location = new System.Drawing.Point(274, 54);
            this.numPort.Maximum = new decimal(new int[] { 65535, 0, 0, 0 });
            this.numPort.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numPort.Name = "numPort";
            this.numPort.Size = new System.Drawing.Size(80, 21);
            this.numPort.TabIndex = 7;
            this.numPort.Value = new decimal(new int[] { 9000, 0, 0, 0 });

            this.lblSlaveId.AutoSize = true;
            this.lblSlaveId.Location = new System.Drawing.Point(370, 57);
            this.lblSlaveId.Name = "lblSlaveId";
            this.lblSlaveId.Size = new System.Drawing.Size(56, 15);
            this.lblSlaveId.TabIndex = 8;
            this.lblSlaveId.Text = "从站号：";
            this.lblSlaveId.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            this.numSlaveId.Location = new System.Drawing.Point(432, 54);
            this.numSlaveId.Maximum = new decimal(new int[] { 247, 0, 0, 0 });
            this.numSlaveId.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numSlaveId.Name = "numSlaveId";
            this.numSlaveId.Size = new System.Drawing.Size(70, 21);
            this.numSlaveId.TabIndex = 9;
            this.numSlaveId.Value = new decimal(new int[] { 1, 0, 0, 0 });

            this.chkEnabled.AutoSize = true;
            this.chkEnabled.Location = new System.Drawing.Point(12, 88);
            this.chkEnabled.Name = "chkEnabled";
            this.chkEnabled.Size = new System.Drawing.Size(54, 19);
            this.chkEnabled.TabIndex = 10;
            this.chkEnabled.Text = "启用";
            this.chkEnabled.UseVisualStyleBackColor = true;

            this.chkAutoConnect.AutoSize = true;
            this.chkAutoConnect.Location = new System.Drawing.Point(90, 88);
            this.chkAutoConnect.Name = "chkAutoConnect";
            this.chkAutoConnect.Size = new System.Drawing.Size(126, 19);
            this.chkAutoConnect.TabIndex = 11;
            this.chkAutoConnect.Text = "启动时自动连接";
            this.chkAutoConnect.UseVisualStyleBackColor = true;

            this.btnSave.Location = new System.Drawing.Point(12, 122);
            this.btnSave.Name = "btnSave";
            this.btnSave.Size = new System.Drawing.Size(92, 32);
            this.btnSave.TabIndex = 12;
            this.btnSave.Text = "保存";
            this.btnSave.UseVisualStyleBackColor = true;

            this.btnConnect.Location = new System.Drawing.Point(112, 122);
            this.btnConnect.Name = "btnConnect";
            this.btnConnect.Size = new System.Drawing.Size(92, 32);
            this.btnConnect.TabIndex = 13;
            this.btnConnect.Text = "连接";
            this.btnConnect.UseVisualStyleBackColor = true;

            this.btnDisconnect.Location = new System.Drawing.Point(212, 122);
            this.btnDisconnect.Name = "btnDisconnect";
            this.btnDisconnect.Size = new System.Drawing.Size(92, 32);
            this.btnDisconnect.TabIndex = 14;
            this.btnDisconnect.Text = "断开";
            this.btnDisconnect.UseVisualStyleBackColor = true;

            this.btnTest.Location = new System.Drawing.Point(312, 122);
            this.btnTest.Name = "btnTest";
            this.btnTest.Size = new System.Drawing.Size(92, 32);
            this.btnTest.TabIndex = 15;
            this.btnTest.Text = "测试";
            this.btnTest.UseVisualStyleBackColor = true;

            // ---------- groupOps：按类型切换的操作区 ----------
            this.groupOps.Dock = System.Windows.Forms.DockStyle.Top;
            this.groupOps.Location = new System.Drawing.Point(8, 184);
            this.groupOps.Name = "groupOps";
            this.groupOps.Size = new System.Drawing.Size(604, 148);
            this.groupOps.TabIndex = 1;
            this.groupOps.TabStop = false;
            this.groupOps.Text = "收发测试";
            // 子控件必须逐个 Add：漏掉的话控件没有 Parent，永不显示也不响应事件
            this.groupOps.Controls.Add(this.panelModbus);
            this.groupOps.Controls.Add(this.panelTcpServer);
            this.groupOps.Controls.Add(this.panelTcpClient);

            // ---- panelTcpClient ----
            this.panelTcpClient.Location = new System.Drawing.Point(12, 24);
            this.panelTcpClient.Name = "panelTcpClient";
            this.panelTcpClient.Size = new System.Drawing.Size(576, 106);
            this.panelTcpClient.TabIndex = 0;
            // 子控件必须逐个 Add：漏掉的话控件没有 Parent，永不显示也不响应事件
            this.panelTcpClient.Controls.Add(this.txtSend);
            this.panelTcpClient.Controls.Add(this.btnSend);
            this.panelTcpClient.Controls.Add(this.lblSend);

            this.lblSend.AutoSize = true;
            this.lblSend.Location = new System.Drawing.Point(0, 4);
            this.lblSend.Name = "lblSend";
            this.lblSend.Size = new System.Drawing.Size(68, 15);
            this.lblSend.TabIndex = 0;
            this.lblSend.Text = "发送内容：";
            this.lblSend.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            this.txtSend.Location = new System.Drawing.Point(0, 26);
            this.txtSend.Name = "txtSend";
            this.txtSend.Size = new System.Drawing.Size(420, 21);
            this.txtSend.TabIndex = 1;

            this.btnSend.Location = new System.Drawing.Point(432, 22);
            this.btnSend.Name = "btnSend";
            this.btnSend.Size = new System.Drawing.Size(96, 32);
            this.btnSend.TabIndex = 2;
            this.btnSend.Text = "发送";
            this.btnSend.UseVisualStyleBackColor = true;

            // ---- panelTcpServer ----
            this.panelTcpServer.Location = new System.Drawing.Point(12, 24);
            this.panelTcpServer.Name = "panelTcpServer";
            this.panelTcpServer.Size = new System.Drawing.Size(576, 106);
            this.panelTcpServer.TabIndex = 1;
            this.panelTcpServer.Visible = false;
            // 子控件必须逐个 Add：漏掉的话控件没有 Parent，永不显示也不响应事件
            this.panelTcpServer.Controls.Add(this.txtSrvSend);
            this.panelTcpServer.Controls.Add(this.btnBroadcast);
            this.panelTcpServer.Controls.Add(this.btnStartListen);
            this.panelTcpServer.Controls.Add(this.btnStopListen);
            this.panelTcpServer.Controls.Add(this.lblSrvSend);

            this.lblSrvSend.AutoSize = true;
            this.lblSrvSend.Location = new System.Drawing.Point(0, 4);
            this.lblSrvSend.Name = "lblSrvSend";
            this.lblSrvSend.Size = new System.Drawing.Size(68, 15);
            this.lblSrvSend.TabIndex = 0;
            this.lblSrvSend.Text = "广播内容：";
            this.lblSrvSend.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            this.txtSrvSend.Location = new System.Drawing.Point(0, 26);
            this.txtSrvSend.Name = "txtSrvSend";
            this.txtSrvSend.Size = new System.Drawing.Size(420, 21);
            this.txtSrvSend.TabIndex = 1;

            this.btnBroadcast.Location = new System.Drawing.Point(432, 22);
            this.btnBroadcast.Name = "btnBroadcast";
            this.btnBroadcast.Size = new System.Drawing.Size(96, 32);
            this.btnBroadcast.TabIndex = 2;
            this.btnBroadcast.Text = "广播";
            this.btnBroadcast.UseVisualStyleBackColor = true;

            this.btnStartListen.Location = new System.Drawing.Point(0, 66);
            this.btnStartListen.Name = "btnStartListen";
            this.btnStartListen.Size = new System.Drawing.Size(120, 32);
            this.btnStartListen.TabIndex = 3;
            this.btnStartListen.Text = "启动监听";
            this.btnStartListen.UseVisualStyleBackColor = true;

            this.btnStopListen.Location = new System.Drawing.Point(128, 66);
            this.btnStopListen.Name = "btnStopListen";
            this.btnStopListen.Size = new System.Drawing.Size(120, 32);
            this.btnStopListen.TabIndex = 4;
            this.btnStopListen.Text = "停止监听";
            this.btnStopListen.UseVisualStyleBackColor = true;

            // ---- panelModbus ----
            this.panelModbus.Location = new System.Drawing.Point(12, 24);
            this.panelModbus.Name = "panelModbus";
            this.panelModbus.Size = new System.Drawing.Size(576, 106);
            this.panelModbus.TabIndex = 2;
            this.panelModbus.Visible = false;
            // 子控件必须逐个 Add：漏掉的话控件没有 Parent，永不显示也不响应事件
            this.panelModbus.Controls.Add(this.numAddr);
            this.panelModbus.Controls.Add(this.numCount);
            this.panelModbus.Controls.Add(this.numValue);
            this.panelModbus.Controls.Add(this.btnMbRead);
            this.panelModbus.Controls.Add(this.btnMbWrite);
            this.panelModbus.Controls.Add(this.lblAddr);
            this.panelModbus.Controls.Add(this.lblCount);
            this.panelModbus.Controls.Add(this.lblValue);

            this.lblAddr.AutoSize = true;
            this.lblAddr.Location = new System.Drawing.Point(0, 4);
            this.lblAddr.Name = "lblAddr";
            this.lblAddr.Size = new System.Drawing.Size(44, 15);
            this.lblAddr.TabIndex = 0;
            this.lblAddr.Text = "地址：";
            this.lblAddr.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            this.numAddr.Location = new System.Drawing.Point(0, 26);
            this.numAddr.Maximum = new decimal(new int[] { 65535, 0, 0, 0 });
            this.numAddr.Name = "numAddr";
            this.numAddr.Size = new System.Drawing.Size(90, 21);
            this.numAddr.TabIndex = 1;

            this.lblCount.AutoSize = true;
            this.lblCount.Location = new System.Drawing.Point(104, 4);
            this.lblCount.Name = "lblCount";
            this.lblCount.Size = new System.Drawing.Size(44, 15);
            this.lblCount.TabIndex = 2;
            this.lblCount.Text = "数量：";
            this.lblCount.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            this.numCount.Location = new System.Drawing.Point(104, 26);
            this.numCount.Maximum = new decimal(new int[] { 125, 0, 0, 0 });
            this.numCount.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numCount.Name = "numCount";
            this.numCount.Size = new System.Drawing.Size(80, 21);
            this.numCount.TabIndex = 3;
            this.numCount.Value = new decimal(new int[] { 1, 0, 0, 0 });

            this.lblValue.AutoSize = true;
            this.lblValue.Location = new System.Drawing.Point(198, 4);
            this.lblValue.Name = "lblValue";
            this.lblValue.Size = new System.Drawing.Size(56, 15);
            this.lblValue.TabIndex = 4;
            this.lblValue.Text = "写入值：";
            this.lblValue.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            this.numValue.Location = new System.Drawing.Point(198, 26);
            this.numValue.Maximum = new decimal(new int[] { 65535, 0, 0, 0 });
            this.numValue.Name = "numValue";
            this.numValue.Size = new System.Drawing.Size(90, 21);
            this.numValue.TabIndex = 5;

            this.btnMbRead.Location = new System.Drawing.Point(320, 22);
            this.btnMbRead.Name = "btnMbRead";
            this.btnMbRead.Size = new System.Drawing.Size(104, 32);
            this.btnMbRead.TabIndex = 6;
            this.btnMbRead.Text = "读 0x03";
            this.btnMbRead.UseVisualStyleBackColor = true;

            this.btnMbWrite.Location = new System.Drawing.Point(432, 22);
            this.btnMbWrite.Name = "btnMbWrite";
            this.btnMbWrite.Size = new System.Drawing.Size(104, 32);
            this.btnMbWrite.TabIndex = 7;
            this.btnMbWrite.Text = "写 0x06";
            this.btnMbWrite.UseVisualStyleBackColor = true;

            // ---------- groupLog：日志 ----------
            this.groupLog.Dock = System.Windows.Forms.DockStyle.Fill;
            this.groupLog.Location = new System.Drawing.Point(8, 332);
            this.groupLog.Name = "groupLog";
            this.groupLog.Size = new System.Drawing.Size(604, 280);
            this.groupLog.TabIndex = 2;
            this.groupLog.TabStop = false;
            this.groupLog.Text = "通讯日志";
            // 子控件必须逐个 Add：漏掉的话控件没有 Parent，永不显示也不响应事件
            this.groupLog.Controls.Add(this.listLog);

            this.listLog.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.listLog.Dock = System.Windows.Forms.DockStyle.Fill;
            this.listLog.IntegralHeight = false;
            this.listLog.ItemHeight = 15;
            this.listLog.Location = new System.Drawing.Point(3, 18);
            this.listLog.Name = "listLog";
            this.listLog.Size = new System.Drawing.Size(598, 259);
            this.listLog.TabIndex = 0;

            // ---------- CommForm ----------
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1080, 620);
            this.Controls.Add(this.panelRight);
            this.Controls.Add(this.panelLeft);
            this.MinimumSize = new System.Drawing.Size(940, 520);
            this.Name = "CommForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "通讯管理（TCP / Modbus TCP）";

            ((System.ComponentModel.ISupportInitialize)(this.gridEndpoints)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numPort)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numSlaveId)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numAddr)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numCount)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numValue)).EndInit();
            this.panelLeft.ResumeLayout(false);
            this.panelLeft.PerformLayout();
            this.panelListBtns.ResumeLayout(false);
            this.panelRight.ResumeLayout(false);
            this.groupProps.ResumeLayout(false);
            this.groupProps.PerformLayout();
            this.groupOps.ResumeLayout(false);
            this.panelTcpClient.ResumeLayout(false);
            this.panelTcpClient.PerformLayout();
            this.panelTcpServer.ResumeLayout(false);
            this.panelTcpServer.PerformLayout();
            this.panelModbus.ResumeLayout(false);
            this.panelModbus.PerformLayout();
            this.groupLog.ResumeLayout(false);
            this.ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.Panel panelLeft;
        private System.Windows.Forms.Label lblListTitle;
        private System.Windows.Forms.DataGridView gridEndpoints;
        private System.Windows.Forms.Panel panelListBtns;
        private VisionPlatform.UI.UiButton btnAdd;
        private VisionPlatform.UI.UiButton btnCopy;
        private VisionPlatform.UI.UiButton btnDelete;

        private System.Windows.Forms.Panel panelRight;
        private System.Windows.Forms.GroupBox groupProps;
        private System.Windows.Forms.Label lblName;
        private System.Windows.Forms.TextBox txtName;
        private System.Windows.Forms.Label lblKind;
        private System.Windows.Forms.ComboBox cboKind;
        private System.Windows.Forms.Label lblHost;
        private System.Windows.Forms.TextBox txtHost;
        private System.Windows.Forms.Label lblPort;
        private System.Windows.Forms.NumericUpDown numPort;
        private System.Windows.Forms.Label lblSlaveId;
        private System.Windows.Forms.NumericUpDown numSlaveId;
        private System.Windows.Forms.CheckBox chkEnabled;
        private System.Windows.Forms.CheckBox chkAutoConnect;
        private VisionPlatform.UI.UiButton btnSave;
        private VisionPlatform.UI.UiButton btnConnect;
        private VisionPlatform.UI.UiButton btnDisconnect;
        private VisionPlatform.UI.UiButton btnTest;

        private System.Windows.Forms.GroupBox groupOps;
        private System.Windows.Forms.Panel panelTcpClient;
        private System.Windows.Forms.Label lblSend;
        private System.Windows.Forms.TextBox txtSend;
        private VisionPlatform.UI.UiButton btnSend;
        private System.Windows.Forms.Panel panelTcpServer;
        private System.Windows.Forms.Label lblSrvSend;
        private System.Windows.Forms.TextBox txtSrvSend;
        private VisionPlatform.UI.UiButton btnBroadcast;
        private VisionPlatform.UI.UiButton btnStartListen;
        private VisionPlatform.UI.UiButton btnStopListen;
        private System.Windows.Forms.Panel panelModbus;
        private System.Windows.Forms.Label lblAddr;
        private System.Windows.Forms.NumericUpDown numAddr;
        private System.Windows.Forms.Label lblCount;
        private System.Windows.Forms.NumericUpDown numCount;
        private System.Windows.Forms.Label lblValue;
        private System.Windows.Forms.NumericUpDown numValue;
        private VisionPlatform.UI.UiButton btnMbRead;
        private VisionPlatform.UI.UiButton btnMbWrite;

        private System.Windows.Forms.GroupBox groupLog;
        private System.Windows.Forms.ListBox listLog;
    }
}
