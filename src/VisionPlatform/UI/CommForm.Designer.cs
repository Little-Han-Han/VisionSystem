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
            this.lblRole = new System.Windows.Forms.Label();
            this.cboRole = new System.Windows.Forms.ComboBox();
            this.tabProtocol = new VisionPlatform.UI.UiTabControl();
            this.tabPageTcp = new System.Windows.Forms.TabPage();
            this.tabPageModbus = new System.Windows.Forms.TabPage();
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
            this.panelTcp = new System.Windows.Forms.Panel();
            this.lblSend = new System.Windows.Forms.Label();
            this.txtSend = new System.Windows.Forms.TextBox();
            this.btnSend = new VisionPlatform.UI.UiButton();
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
            this.txtValue = new System.Windows.Forms.TextBox();
            this.btnMbRead = new VisionPlatform.UI.UiButton();
            this.btnMbWrite = new VisionPlatform.UI.UiButton();
            this.lblMbType = new System.Windows.Forms.Label();
            this.cboMbType = new System.Windows.Forms.ComboBox();
            this.lblMbOrder = new System.Windows.Forms.Label();
            this.cboMbOrder = new System.Windows.Forms.ComboBox();
            this.lblMbTip = new System.Windows.Forms.Label();

            this.panelRecv = new System.Windows.Forms.Panel();
            this.lblRecv = new System.Windows.Forms.Label();
            this.txtRecv = new System.Windows.Forms.TextBox();

            this.groupLog = new System.Windows.Forms.GroupBox();
            this.listLog = new System.Windows.Forms.ListBox();

            ((System.ComponentModel.ISupportInitialize)(this.gridEndpoints)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numPort)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numSlaveId)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numAddr)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numCount)).BeginInit();
            this.panelLeft.SuspendLayout();
            this.panelListBtns.SuspendLayout();
            this.panelRight.SuspendLayout();
            this.groupProps.SuspendLayout();
            this.groupOps.SuspendLayout();
            this.tabProtocol.SuspendLayout();
            this.panelTcp.SuspendLayout();
            this.panelModbus.SuspendLayout();
            this.panelRecv.SuspendLayout();
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
            // 【高度账】标题栏占 32，panelRight Padding 上下各 8：
            //   620(ClientSize) - 32(标题栏) - 16(padding) = 572
            //   groupProps 142(Top) + groupOps 290(Fill) + groupLog 140(Bottom) = 572
            // 【停靠顺序】WinForms 按集合逆序停靠，Fill 的必须最先 Add（最后才布局，才能吃剩余）：
            //   Add(groupOps=Fill) → Add(groupProps=Top) → Add(groupLog=Bottom)
            // 这样窗体变高变矮时，伸缩的是「收发测试 + 接收显示」，
            // 日志区固定 140 稳定不动（改之前日志是 Fill，窗高变小全压在它身上，被压到只剩 40）。
            this.panelRight.Controls.Add(this.groupOps);
            this.panelRight.Controls.Add(this.groupProps);
            this.panelRight.Controls.Add(this.groupLog);
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
            this.groupProps.Size = new System.Drawing.Size(604, 142);
            this.groupProps.TabIndex = 0;
            this.groupProps.TabStop = false;
            this.groupProps.Text = "通讯属性";
            // 子控件必须逐个 Add：漏掉的话控件没有 Parent，永不显示也不响应事件
            this.groupProps.Controls.Add(this.cboRole);
            this.groupProps.Controls.Add(this.txtName);
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
            this.groupProps.Controls.Add(this.lblRole);
            this.groupProps.Controls.Add(this.lblHost);
            this.groupProps.Controls.Add(this.lblPort);
            this.groupProps.Controls.Add(this.lblSlaveId);

            this.lblName.AutoSize = true;
            this.lblName.Location = new System.Drawing.Point(12, 23);
            this.lblName.Name = "lblName";
            this.lblName.Size = new System.Drawing.Size(44, 15);
            this.lblName.TabIndex = 0;
            this.lblName.Text = "名称：";
            this.lblName.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            this.txtName.Location = new System.Drawing.Point(60, 20);
            this.txtName.Name = "txtName";
            this.txtName.Size = new System.Drawing.Size(150, 21);
            this.txtName.TabIndex = 1;

            // 协议类型改用 tabProtocol 分页选择；TCP 页的「客户端 / 服务端」角色在这里选
            this.lblRole.AutoSize = true;
            this.lblRole.Location = new System.Drawing.Point(240, 23);
            this.lblRole.Name = "lblRole";
            this.lblRole.Size = new System.Drawing.Size(44, 15);
            this.lblRole.TabIndex = 2;
            this.lblRole.Text = "角色：";
            this.lblRole.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            this.cboRole.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cboRole.Location = new System.Drawing.Point(288, 20);
            this.cboRole.Name = "cboRole";
            this.cboRole.Size = new System.Drawing.Size(160, 21);
            this.cboRole.TabIndex = 3;

            this.lblHost.AutoSize = true;
            this.lblHost.Location = new System.Drawing.Point(12, 49);
            this.lblHost.Name = "lblHost";
            this.lblHost.Size = new System.Drawing.Size(56, 15);
            this.lblHost.TabIndex = 4;
            this.lblHost.Text = "IP/主机：";
            this.lblHost.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            this.txtHost.Location = new System.Drawing.Point(74, 46);
            this.txtHost.Name = "txtHost";
            this.txtHost.Size = new System.Drawing.Size(120, 21);
            this.txtHost.TabIndex = 5;

            this.lblPort.AutoSize = true;
            this.lblPort.Location = new System.Drawing.Point(202, 49);
            this.lblPort.Name = "lblPort";
            this.lblPort.Size = new System.Drawing.Size(44, 15);
            this.lblPort.TabIndex = 6;
            this.lblPort.Text = "端口：";
            this.lblPort.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            this.numPort.Location = new System.Drawing.Point(250, 46);
            this.numPort.Maximum = new decimal(new int[] { 65535, 0, 0, 0 });
            this.numPort.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numPort.Name = "numPort";
            this.numPort.Size = new System.Drawing.Size(64, 21);
            this.numPort.TabIndex = 7;
            this.numPort.Value = new decimal(new int[] { 9000, 0, 0, 0 });

            this.lblSlaveId.AutoSize = true;
            this.lblSlaveId.Location = new System.Drawing.Point(322, 49);
            this.lblSlaveId.Name = "lblSlaveId";
            this.lblSlaveId.Size = new System.Drawing.Size(56, 15);
            this.lblSlaveId.TabIndex = 8;
            this.lblSlaveId.Text = "从站号：";
            this.lblSlaveId.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            // 从站号右边界 444：GroupBox 不是 ScrollableControl（没有 AutoScroll），
            // 窄到最小尺寸（分组内宽 458）时也必须整体放得下，故宽度收到 60 并左移。
            this.numSlaveId.Location = new System.Drawing.Point(384, 46);
            this.numSlaveId.Maximum = new decimal(new int[] { 247, 0, 0, 0 });
            this.numSlaveId.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numSlaveId.Name = "numSlaveId";
            this.numSlaveId.Size = new System.Drawing.Size(60, 21);
            this.numSlaveId.TabIndex = 9;
            this.numSlaveId.Value = new decimal(new int[] { 1, 0, 0, 0 });

            this.chkEnabled.AutoSize = true;
            this.chkEnabled.Location = new System.Drawing.Point(12, 74);
            this.chkEnabled.Name = "chkEnabled";
            this.chkEnabled.Size = new System.Drawing.Size(54, 19);
            this.chkEnabled.TabIndex = 10;
            this.chkEnabled.Text = "启用";
            this.chkEnabled.UseVisualStyleBackColor = true;

            this.chkAutoConnect.AutoSize = true;
            this.chkAutoConnect.Location = new System.Drawing.Point(90, 74);
            this.chkAutoConnect.Name = "chkAutoConnect";
            this.chkAutoConnect.Size = new System.Drawing.Size(126, 19);
            this.chkAutoConnect.TabIndex = 11;
            this.chkAutoConnect.Text = "启动时自动连接";
            this.chkAutoConnect.UseVisualStyleBackColor = true;

            this.btnSave.Location = new System.Drawing.Point(12, 98);
            this.btnSave.Name = "btnSave";
            this.btnSave.Size = new System.Drawing.Size(92, 32);
            this.btnSave.TabIndex = 12;
            this.btnSave.Text = "保存";
            this.btnSave.UseVisualStyleBackColor = true;

            this.btnConnect.Location = new System.Drawing.Point(112, 98);
            this.btnConnect.Name = "btnConnect";
            this.btnConnect.Size = new System.Drawing.Size(92, 32);
            this.btnConnect.TabIndex = 13;
            this.btnConnect.Text = "连接";
            this.btnConnect.UseVisualStyleBackColor = true;

            this.btnDisconnect.Location = new System.Drawing.Point(212, 98);
            this.btnDisconnect.Name = "btnDisconnect";
            this.btnDisconnect.Size = new System.Drawing.Size(92, 32);
            this.btnDisconnect.TabIndex = 14;
            this.btnDisconnect.Text = "断开";
            this.btnDisconnect.UseVisualStyleBackColor = true;

            this.btnTest.Location = new System.Drawing.Point(312, 98);
            this.btnTest.Name = "btnTest";
            this.btnTest.Size = new System.Drawing.Size(92, 32);
            this.btnTest.TabIndex = 15;
            this.btnTest.Text = "测试";
            this.btnTest.UseVisualStyleBackColor = true;

            // ---------- groupOps：收发测试（TCP 客户端 / 服务端已整合进同一页） ----------
            // Dock=Fill：吸收 groupProps(142) 与 groupLog(140) 之外的全部剩余高度，
            // 默认尺寸下 = 572 - 142 - 140 = 290（与改之前完全一致，观感不变）。
            // Dock 顺序（自下而上）：先加 panelRecv（Fill，吃剩余），再加 tabProtocol（Top）。
            this.groupOps.Dock = System.Windows.Forms.DockStyle.Fill;
            this.groupOps.Location = new System.Drawing.Point(8, 150);
            this.groupOps.Name = "groupOps";
            this.groupOps.Size = new System.Drawing.Size(604, 290);
            this.groupOps.TabIndex = 1;
            this.groupOps.TabStop = false;
            this.groupOps.Text = "收发测试";
            // 子控件必须逐个 Add：漏掉的话控件没有 Parent，永不显示也不响应事件
            this.groupOps.Controls.Add(this.panelRecv);
            this.groupOps.Controls.Add(this.tabProtocol);

            // ---- panelTcp：TCP 通信（客户端发送 + 服务端广播 / 监听，按角色启用）----
            // AutoScroll：Panel 是 ScrollableControl，窗口窄于内容时出滚动条，
            // 绝对定位的按钮仍可滚动触达，而不是被永久裁掉。
            this.panelTcp.AutoScroll = true;
            this.panelTcp.Dock = System.Windows.Forms.DockStyle.Fill;
            this.panelTcp.Name = "panelTcp";
            this.panelTcp.Size = new System.Drawing.Size(578, 114);
            this.panelTcp.TabIndex = 0;
            // 子控件必须逐个 Add：漏掉的话控件没有 Parent，永不显示也不响应事件
            this.panelTcp.Controls.Add(this.txtSend);
            this.panelTcp.Controls.Add(this.btnSend);
            this.panelTcp.Controls.Add(this.txtSrvSend);
            this.panelTcp.Controls.Add(this.btnBroadcast);
            this.panelTcp.Controls.Add(this.btnStartListen);
            this.panelTcp.Controls.Add(this.btnStopListen);
            this.panelTcp.Controls.Add(this.lblSend);
            this.panelTcp.Controls.Add(this.lblSrvSend);

            this.lblSend.AutoSize = true;
            this.lblSend.Location = new System.Drawing.Point(0, 2);
            this.lblSend.Name = "lblSend";
            this.lblSend.Size = new System.Drawing.Size(68, 15);
            this.lblSend.TabIndex = 0;
            this.lblSend.Text = "发送内容：";
            this.lblSend.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            // 【刻意不用 Anchor】Panel 开了 AutoScroll 后，锚定控件的右间距会按「显示矩形」算，
            // 一旦内容撑大过一次就再也收不回来（实测 txtSend.Right 会停在被撑大的 918 上）。
            // 因此页内一律绝对定位，窄宽度改由 AutoScroll 兜底（见 panelTcp.AutoScroll）。
            // 【宽度预算 520】页内最右边界压到 520（原 564），使新最小尺寸（页内 530）下
            // 不需要横向滚动条也能全部放下：发送按钮 508、停止监听 520、Modbus 写入 504。
            this.txtSend.Location = new System.Drawing.Point(0, 20);
            this.txtSend.Name = "txtSend";
            this.txtSend.Size = new System.Drawing.Size(400, 21);
            this.txtSend.TabIndex = 1;

            this.btnSend.Location = new System.Drawing.Point(412, 16);
            this.btnSend.Name = "btnSend";
            this.btnSend.Size = new System.Drawing.Size(96, 32);
            this.btnSend.TabIndex = 2;
            this.btnSend.Text = "发送";
            this.btnSend.UseVisualStyleBackColor = true;

            this.lblSrvSend.AutoSize = true;
            this.lblSrvSend.Location = new System.Drawing.Point(0, 52);
            this.lblSrvSend.Name = "lblSrvSend";
            this.lblSrvSend.Size = new System.Drawing.Size(68, 15);
            this.lblSrvSend.TabIndex = 3;
            this.lblSrvSend.Text = "广播内容：";
            this.lblSrvSend.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            this.txtSrvSend.Location = new System.Drawing.Point(0, 70);
            this.txtSrvSend.Name = "txtSrvSend";
            this.txtSrvSend.Size = new System.Drawing.Size(244, 21);
            this.txtSrvSend.TabIndex = 4;

            this.btnBroadcast.Location = new System.Drawing.Point(252, 66);
            this.btnBroadcast.Name = "btnBroadcast";
            this.btnBroadcast.Size = new System.Drawing.Size(84, 32);
            this.btnBroadcast.TabIndex = 5;
            this.btnBroadcast.Text = "广播";
            this.btnBroadcast.UseVisualStyleBackColor = true;

            // 「启动监听 / 停止监听」与「广播」同为服务端能力，放在同一行，
            // 三者按角色一起启用 / 禁用；页内可用高约 114，98px 的内容不再被裁切。
            this.btnStartListen.Location = new System.Drawing.Point(344, 66);
            this.btnStartListen.Name = "btnStartListen";
            this.btnStartListen.Size = new System.Drawing.Size(84, 32);
            this.btnStartListen.TabIndex = 6;
            this.btnStartListen.Text = "启动监听";
            this.btnStartListen.UseVisualStyleBackColor = true;

            this.btnStopListen.Location = new System.Drawing.Point(436, 66);
            this.btnStopListen.Name = "btnStopListen";
            this.btnStopListen.Size = new System.Drawing.Size(84, 32);
            this.btnStopListen.TabIndex = 7;
            this.btnStopListen.Text = "停止监听";
            this.btnStopListen.UseVisualStyleBackColor = true;

            // ---- panelModbus：Modbus TCP（多数据类型 + 四种字序）----
            this.panelModbus.AutoScroll = true;
            this.panelModbus.Dock = System.Windows.Forms.DockStyle.Fill;
            this.panelModbus.Name = "panelModbus";
            this.panelModbus.Size = new System.Drawing.Size(578, 114);
            this.panelModbus.TabIndex = 1;
            // 子控件必须逐个 Add：漏掉的话控件没有 Parent，永不显示也不响应事件
            this.panelModbus.Controls.Add(this.cboMbType);
            this.panelModbus.Controls.Add(this.cboMbOrder);
            this.panelModbus.Controls.Add(this.numAddr);
            this.panelModbus.Controls.Add(this.numCount);
            this.panelModbus.Controls.Add(this.txtValue);
            this.panelModbus.Controls.Add(this.btnMbRead);
            this.panelModbus.Controls.Add(this.btnMbWrite);
            this.panelModbus.Controls.Add(this.lblAddr);
            this.panelModbus.Controls.Add(this.lblCount);
            this.panelModbus.Controls.Add(this.lblValue);
            this.panelModbus.Controls.Add(this.lblMbType);
            this.panelModbus.Controls.Add(this.lblMbOrder);
            this.panelModbus.Controls.Add(this.lblMbTip);

            this.lblAddr.AutoSize = true;
            this.lblAddr.Location = new System.Drawing.Point(0, 0);
            this.lblAddr.Name = "lblAddr";
            this.lblAddr.Size = new System.Drawing.Size(44, 15);
            this.lblAddr.TabIndex = 0;
            this.lblAddr.Text = "地址：";
            this.lblAddr.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            this.numAddr.Location = new System.Drawing.Point(0, 17);
            this.numAddr.Maximum = new decimal(new int[] { 65535, 0, 0, 0 });
            this.numAddr.Name = "numAddr";
            this.numAddr.Size = new System.Drawing.Size(80, 21);
            this.numAddr.TabIndex = 1;

            this.lblCount.AutoSize = true;
            this.lblCount.Location = new System.Drawing.Point(92, 0);
            this.lblCount.Name = "lblCount";
            this.lblCount.Size = new System.Drawing.Size(80, 15);
            this.lblCount.TabIndex = 2;
            this.lblCount.Text = "数量（寄存器）：";
            this.lblCount.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            this.numCount.Location = new System.Drawing.Point(92, 17);
            this.numCount.Maximum = new decimal(new int[] { 125, 0, 0, 0 });
            this.numCount.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numCount.Name = "numCount";
            this.numCount.Size = new System.Drawing.Size(80, 21);
            this.numCount.TabIndex = 3;
            this.numCount.Value = new decimal(new int[] { 1, 0, 0, 0 });

            this.lblValue.AutoSize = true;
            this.lblValue.Location = new System.Drawing.Point(200, 0);
            this.lblValue.Name = "lblValue";
            this.lblValue.Size = new System.Drawing.Size(56, 15);
            this.lblValue.TabIndex = 4;
            this.lblValue.Text = "写入值：";
            this.lblValue.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            // 原 numValue 只能输 0~65535 的整数，无法输入 12.5 / HELLO / true，改为文本框。
            // 246 = 功能码 0x10 单次可写 123 个寄存器 × 每寄存器 2 字符。
            this.txtValue.Location = new System.Drawing.Point(200, 17);
            this.txtValue.MaxLength = 246;
            this.txtValue.Name = "txtValue";
            this.txtValue.Size = new System.Drawing.Size(110, 21);
            this.txtValue.TabIndex = 5;

            this.btnMbRead.Location = new System.Drawing.Point(320, 13);
            this.btnMbRead.Name = "btnMbRead";
            this.btnMbRead.Size = new System.Drawing.Size(88, 32);
            this.btnMbRead.TabIndex = 6;
            this.btnMbRead.Text = "读取";
            this.btnMbRead.UseVisualStyleBackColor = true;

            this.btnMbWrite.Location = new System.Drawing.Point(416, 13);
            this.btnMbWrite.Name = "btnMbWrite";
            this.btnMbWrite.Size = new System.Drawing.Size(88, 32);
            this.btnMbWrite.TabIndex = 7;
            this.btnMbWrite.Text = "写入";
            this.btnMbWrite.UseVisualStyleBackColor = true;

            this.lblMbType.AutoSize = true;
            this.lblMbType.Location = new System.Drawing.Point(0, 49);
            this.lblMbType.Name = "lblMbType";
            this.lblMbType.Size = new System.Drawing.Size(68, 15);
            this.lblMbType.TabIndex = 8;
            this.lblMbType.Text = "数据类型：";
            this.lblMbType.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            this.cboMbType.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cboMbType.Location = new System.Drawing.Point(0, 66);
            this.cboMbType.Name = "cboMbType";
            this.cboMbType.Size = new System.Drawing.Size(180, 21);
            this.cboMbType.TabIndex = 9;

            this.lblMbOrder.AutoSize = true;
            this.lblMbOrder.Location = new System.Drawing.Point(196, 49);
            this.lblMbOrder.Name = "lblMbOrder";
            this.lblMbOrder.Size = new System.Drawing.Size(44, 15);
            this.lblMbOrder.TabIndex = 10;
            this.lblMbOrder.Text = "字序：";
            this.lblMbOrder.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            this.cboMbOrder.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cboMbOrder.Location = new System.Drawing.Point(196, 66);
            this.cboMbOrder.Name = "cboMbOrder";
            this.cboMbOrder.Size = new System.Drawing.Size(180, 21);
            this.cboMbOrder.TabIndex = 11;

            this.lblMbTip.AutoSize = true;
            this.lblMbTip.Location = new System.Drawing.Point(0, 93);
            this.lblMbTip.Name = "lblMbTip";
            this.lblMbTip.Size = new System.Drawing.Size(120, 15);
            this.lblMbTip.TabIndex = 12;
            this.lblMbTip.Text = "地址从 0 开始（非 40001 编号）";
            this.lblMbTip.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            // ---------- tabProtocol：TCP 通信 / Modbus TCP 两页 ----------
            this.tabProtocol.Dock = System.Windows.Forms.DockStyle.Top;
            this.tabProtocol.Location = new System.Drawing.Point(3, 22);
            this.tabProtocol.Name = "tabProtocol";
            this.tabProtocol.SelectedIndex = 0;
            this.tabProtocol.Size = new System.Drawing.Size(598, 158);
            this.tabProtocol.TabIndex = 0;
            this.tabPageTcp.Text = "TCP 通信";
            this.tabPageModbus.Text = "Modbus TCP";
            this.tabPageTcp.Controls.Add(this.panelTcp);
            this.tabPageModbus.Controls.Add(this.panelModbus);
            this.tabProtocol.Controls.Add(this.tabPageTcp);
            this.tabProtocol.Controls.Add(this.tabPageModbus);
            this.tabProtocol.StyleAllPages();

            // ---- panelRecv：接收显示（TCP 与 Modbus 共用）----
            this.panelRecv.Dock = System.Windows.Forms.DockStyle.Fill;
            this.panelRecv.Location = new System.Drawing.Point(3, 180);
            this.panelRecv.Name = "panelRecv";
            this.panelRecv.Padding = new System.Windows.Forms.Padding(0, 2, 0, 0);
            this.panelRecv.Size = new System.Drawing.Size(598, 105);
            this.panelRecv.TabIndex = 1;
            // 子控件必须逐个 Add：漏掉的话控件没有 Parent，永不显示也不响应事件
            this.panelRecv.Controls.Add(this.txtRecv);
            this.panelRecv.Controls.Add(this.lblRecv);

            this.lblRecv.Dock = System.Windows.Forms.DockStyle.Top;
            this.lblRecv.Location = new System.Drawing.Point(0, 2);
            this.lblRecv.Name = "lblRecv";
            this.lblRecv.Size = new System.Drawing.Size(598, 18);
            this.lblRecv.TabIndex = 0;
            this.lblRecv.Text = "接收显示：";
            this.lblRecv.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            this.txtRecv.Dock = System.Windows.Forms.DockStyle.Fill;
            this.txtRecv.Location = new System.Drawing.Point(0, 20);
            this.txtRecv.Multiline = true;
            this.txtRecv.Name = "txtRecv";
            this.txtRecv.ReadOnly = true;
            this.txtRecv.ScrollBars = System.Windows.Forms.ScrollBars.Both;
            this.txtRecv.Size = new System.Drawing.Size(598, 85);
            this.txtRecv.TabIndex = 1;
            this.txtRecv.WordWrap = false;

            // ---------- groupLog：日志（Dock=Bottom + 固定 140，不再随窗高萎缩）----------
            this.groupLog.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.groupLog.Location = new System.Drawing.Point(8, 440);
            this.groupLog.Name = "groupLog";
            this.groupLog.Size = new System.Drawing.Size(604, 140);
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
            this.listLog.Size = new System.Drawing.Size(598, 119);
            this.listLog.TabIndex = 0;

            // ---------- CommForm ----------
            // 【最小尺寸】标题栏 UiTitleBar 支持边缘拉伸（WM_NCHITTEST），用户真能把它拖小：
            //   宽 1020 → panelRight 560 → 分组 544 → 页内 530（最宽需求：Modbus 写入按钮 504）
            //   高  580 → 可用 512，groupProps 142 + groupLog 140 之后，收发区仍有 230
            //             （其中 tabProtocol 158，接收显示 50，仍可滚动/可用）
            // 再窄再矮就有控件被裁或接收区归零，故以此为下限。
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1080, 620);
            this.Controls.Add(this.panelRight);
            this.Controls.Add(this.panelLeft);
            this.MinimumSize = new System.Drawing.Size(1020, 580);
            this.Name = "CommForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "通讯管理（TCP / Modbus TCP）";

            ((System.ComponentModel.ISupportInitialize)(this.gridEndpoints)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numPort)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numSlaveId)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numAddr)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numCount)).EndInit();
            this.panelLeft.ResumeLayout(false);
            this.panelLeft.PerformLayout();
            this.panelListBtns.ResumeLayout(false);
            this.panelRight.ResumeLayout(false);
            this.groupProps.ResumeLayout(false);
            this.groupProps.PerformLayout();
            this.tabProtocol.ResumeLayout(false);
            this.groupOps.ResumeLayout(false);
            this.panelTcp.ResumeLayout(false);
            this.panelTcp.PerformLayout();
            this.panelModbus.ResumeLayout(false);
            this.panelModbus.PerformLayout();
            this.panelRecv.ResumeLayout(false);
            this.panelRecv.PerformLayout();
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
        private System.Windows.Forms.Label lblRole;
        private System.Windows.Forms.ComboBox cboRole;
        private VisionPlatform.UI.UiTabControl tabProtocol;
        private System.Windows.Forms.TabPage tabPageTcp;
        private System.Windows.Forms.TabPage tabPageModbus;
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
        private System.Windows.Forms.Panel panelTcp;
        private System.Windows.Forms.Label lblSend;
        private System.Windows.Forms.TextBox txtSend;
        private VisionPlatform.UI.UiButton btnSend;
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
        private System.Windows.Forms.TextBox txtValue;
        private VisionPlatform.UI.UiButton btnMbRead;
        private VisionPlatform.UI.UiButton btnMbWrite;
        private System.Windows.Forms.Label lblMbType;
        private System.Windows.Forms.ComboBox cboMbType;
        private System.Windows.Forms.Label lblMbOrder;
        private System.Windows.Forms.ComboBox cboMbOrder;
        private System.Windows.Forms.Label lblMbTip;

        private System.Windows.Forms.Panel panelRecv;
        private System.Windows.Forms.Label lblRecv;
        private System.Windows.Forms.TextBox txtRecv;

        private System.Windows.Forms.GroupBox groupLog;
        private System.Windows.Forms.ListBox listLog;
    }
}
