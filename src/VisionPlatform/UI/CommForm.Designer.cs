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
            this.groupClient = new System.Windows.Forms.GroupBox();
            this.lblHost = new System.Windows.Forms.Label();
            this.txtHost = new System.Windows.Forms.TextBox();
            this.lblClientPort = new System.Windows.Forms.Label();
            this.numClientPort = new System.Windows.Forms.NumericUpDown();
            this.lblSend = new System.Windows.Forms.Label();
            this.txtSend = new System.Windows.Forms.TextBox();
            this.btnClientConnect = new VisionPlatform.UI.UiButton();
            this.btnClientDisconnect = new VisionPlatform.UI.UiButton();
            this.btnSend = new VisionPlatform.UI.UiButton();
            this.groupServer = new System.Windows.Forms.GroupBox();
            this.lblServerPort = new System.Windows.Forms.Label();
            this.numServerPort = new System.Windows.Forms.NumericUpDown();
            this.btnListen = new VisionPlatform.UI.UiButton();
            this.btnStopListen = new VisionPlatform.UI.UiButton();
            this.btnBroadcast = new VisionPlatform.UI.UiButton();
            this.groupModbus = new System.Windows.Forms.GroupBox();
            this.lblMbHost = new System.Windows.Forms.Label();
            this.txtMbHost = new System.Windows.Forms.TextBox();
            this.lblMbPort = new System.Windows.Forms.Label();
            this.numMbPort = new System.Windows.Forms.NumericUpDown();
            this.lblAddr = new System.Windows.Forms.Label();
            this.numAddr = new System.Windows.Forms.NumericUpDown();
            this.lblCount = new System.Windows.Forms.Label();
            this.numCount = new System.Windows.Forms.NumericUpDown();
            this.lblValue = new System.Windows.Forms.Label();
            this.numValue = new System.Windows.Forms.NumericUpDown();
            this.btnMbConnect = new VisionPlatform.UI.UiButton();
            this.btnMbRead = new VisionPlatform.UI.UiButton();
            this.btnMbWrite = new VisionPlatform.UI.UiButton();

            this.panelRight = new System.Windows.Forms.Panel();
            this.listLog = new System.Windows.Forms.ListBox();

            ((System.ComponentModel.ISupportInitialize)(this.numClientPort)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numServerPort)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numMbPort)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numAddr)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numCount)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numValue)).BeginInit();
            this.panelLeft.SuspendLayout();
            this.groupClient.SuspendLayout();
            this.groupServer.SuspendLayout();
            this.groupModbus.SuspendLayout();
            this.panelRight.SuspendLayout();
            this.SuspendLayout();

            // ---------- panelLeft ----------
            this.panelLeft.AutoScroll = true;
            this.panelLeft.BackColor = System.Drawing.Color.FromArgb(45, 45, 52);
            this.panelLeft.Controls.Add(this.groupClient);
            this.panelLeft.Controls.Add(this.groupServer);
            this.panelLeft.Controls.Add(this.groupModbus);
            this.panelLeft.Dock = System.Windows.Forms.DockStyle.Left;
            this.panelLeft.Location = new System.Drawing.Point(0, 0);
            this.panelLeft.Name = "panelLeft";
            this.panelLeft.Padding = new System.Windows.Forms.Padding(8);
            this.panelLeft.Size = new System.Drawing.Size(420, 560);
            this.panelLeft.TabIndex = 0;

            // ---------- groupClient ----------
            this.groupClient.ForeColor = System.Drawing.Color.DeepSkyBlue;
            this.groupClient.Location = new System.Drawing.Point(8, 8);
            this.groupClient.Name = "groupClient";
            this.groupClient.Size = new System.Drawing.Size(390, 170);
            this.groupClient.TabIndex = 0;
            this.groupClient.TabStop = false;
            this.groupClient.Text = "TCP 客户端";
            // 子控件必须逐个 Add：漏掉的话控件没有 Parent，永不显示也不响应事件
            this.groupClient.Controls.Add(this.txtHost);
            this.groupClient.Controls.Add(this.numClientPort);
            this.groupClient.Controls.Add(this.txtSend);
            this.groupClient.Controls.Add(this.btnClientConnect);
            this.groupClient.Controls.Add(this.btnClientDisconnect);
            this.groupClient.Controls.Add(this.btnSend);
            this.groupClient.Controls.Add(this.lblHost);
            this.groupClient.Controls.Add(this.lblClientPort);
            this.groupClient.Controls.Add(this.lblSend);

            // 标签统一贴在输入框左侧（x=12），Y 比输入框 +3 以视觉居中；配色交给 UiStyle
            this.lblHost.AutoSize = true;
            this.lblHost.Location = new System.Drawing.Point(12, 27);
            this.lblHost.Name = "lblHost";
            this.lblHost.Size = new System.Drawing.Size(50, 15);
            this.lblHost.Text = "主机：";
            this.lblHost.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            this.txtHost.Location = new System.Drawing.Point(90, 24);
            this.txtHost.Name = "txtHost";
            this.txtHost.Size = new System.Drawing.Size(180, 21);
            this.txtHost.TabIndex = 0;
            this.txtHost.Text = "127.0.0.1";

            this.lblClientPort.AutoSize = true;
            this.lblClientPort.Location = new System.Drawing.Point(12, 55);
            this.lblClientPort.Name = "lblClientPort";
            this.lblClientPort.Size = new System.Drawing.Size(50, 15);
            this.lblClientPort.Text = "端口：";
            this.lblClientPort.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            this.numClientPort.Location = new System.Drawing.Point(90, 52);
            this.numClientPort.Maximum = new decimal(new int[] { 65535, 0, 0, 0 });
            this.numClientPort.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numClientPort.Name = "numClientPort";
            this.numClientPort.Size = new System.Drawing.Size(180, 21);
            this.numClientPort.TabIndex = 1;
            this.numClientPort.Value = new decimal(new int[] { 9000, 0, 0, 0 });

            this.lblSend.AutoSize = true;
            this.lblSend.Location = new System.Drawing.Point(12, 83);
            this.lblSend.Name = "lblSend";
            this.lblSend.Size = new System.Drawing.Size(72, 15);
            this.lblSend.Text = "发送内容：";
            this.lblSend.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            this.txtSend.Location = new System.Drawing.Point(90, 80);
            this.txtSend.Name = "txtSend";
            this.txtSend.Size = new System.Drawing.Size(180, 21);
            this.txtSend.TabIndex = 2;
            this.txtSend.Text = "hello";

            this.btnClientConnect.Location = new System.Drawing.Point(16, 118);
            this.btnClientConnect.Name = "btnClientConnect";
            this.btnClientConnect.Size = new System.Drawing.Size(110, 28);
            this.btnClientConnect.TabIndex = 3;
            this.btnClientConnect.Text = "连接";
            this.btnClientConnect.Click += new System.EventHandler(this.OnClientConnect);

            this.btnClientDisconnect.Location = new System.Drawing.Point(134, 118);
            this.btnClientDisconnect.Name = "btnClientDisconnect";
            this.btnClientDisconnect.Size = new System.Drawing.Size(110, 28);
            this.btnClientDisconnect.TabIndex = 4;
            this.btnClientDisconnect.Text = "断开";
            this.btnClientDisconnect.Click += new System.EventHandler(this.OnClientDisconnect);

            this.btnSend.Location = new System.Drawing.Point(252, 118);
            this.btnSend.Name = "btnSend";
            this.btnSend.Size = new System.Drawing.Size(110, 28);
            this.btnSend.TabIndex = 5;
            this.btnSend.Text = "发送";
            this.btnSend.Click += new System.EventHandler(this.OnClientSend);

            // ---------- groupServer ----------
            this.groupServer.ForeColor = System.Drawing.Color.DeepSkyBlue;
            this.groupServer.Location = new System.Drawing.Point(8, 186);
            this.groupServer.Name = "groupServer";
            this.groupServer.Size = new System.Drawing.Size(390, 100);
            this.groupServer.TabIndex = 1;
            this.groupServer.TabStop = false;
            this.groupServer.Text = "TCP 服务端";
            // 子控件必须逐个 Add：漏掉的话控件没有 Parent，永不显示也不响应事件
            this.groupServer.Controls.Add(this.numServerPort);
            this.groupServer.Controls.Add(this.btnListen);
            this.groupServer.Controls.Add(this.btnStopListen);
            this.groupServer.Controls.Add(this.btnBroadcast);
            this.groupServer.Controls.Add(this.lblServerPort);

            this.lblServerPort.AutoSize = true;
            this.lblServerPort.Location = new System.Drawing.Point(12, 29);
            this.lblServerPort.Name = "lblServerPort";
            this.lblServerPort.Size = new System.Drawing.Size(72, 15);
            this.lblServerPort.Text = "监听端口：";
            this.lblServerPort.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            this.numServerPort.Location = new System.Drawing.Point(90, 26);
            this.numServerPort.Maximum = new decimal(new int[] { 65535, 0, 0, 0 });
            this.numServerPort.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numServerPort.Name = "numServerPort";
            this.numServerPort.Size = new System.Drawing.Size(180, 21);
            this.numServerPort.TabIndex = 0;
            this.numServerPort.Value = new decimal(new int[] { 9100, 0, 0, 0 });

            this.btnListen.Location = new System.Drawing.Point(16, 60);
            this.btnListen.Name = "btnListen";
            this.btnListen.Size = new System.Drawing.Size(110, 28);
            this.btnListen.TabIndex = 1;
            this.btnListen.Text = "启动监听";
            this.btnListen.Click += new System.EventHandler(this.OnServerStart);

            this.btnStopListen.Location = new System.Drawing.Point(134, 60);
            this.btnStopListen.Name = "btnStopListen";
            this.btnStopListen.Size = new System.Drawing.Size(110, 28);
            this.btnStopListen.TabIndex = 2;
            this.btnStopListen.Text = "停止";
            this.btnStopListen.Click += new System.EventHandler(this.OnServerStop);

            this.btnBroadcast.Location = new System.Drawing.Point(252, 60);
            this.btnBroadcast.Name = "btnBroadcast";
            this.btnBroadcast.Size = new System.Drawing.Size(110, 28);
            this.btnBroadcast.TabIndex = 3;
            this.btnBroadcast.Text = "广播";
            this.btnBroadcast.Click += new System.EventHandler(this.OnServerBroadcast);

            // ---------- groupModbus ----------
            this.groupModbus.ForeColor = System.Drawing.Color.DeepSkyBlue;
            this.groupModbus.Location = new System.Drawing.Point(8, 294);
            this.groupModbus.Name = "groupModbus";
            this.groupModbus.Size = new System.Drawing.Size(390, 230);
            this.groupModbus.TabIndex = 2;
            this.groupModbus.TabStop = false;
            this.groupModbus.Text = "Modbus TCP";
            // 子控件必须逐个 Add：漏掉的话控件没有 Parent，永不显示也不响应事件
            this.groupModbus.Controls.Add(this.txtMbHost);
            this.groupModbus.Controls.Add(this.numMbPort);
            this.groupModbus.Controls.Add(this.numAddr);
            this.groupModbus.Controls.Add(this.numCount);
            this.groupModbus.Controls.Add(this.numValue);
            this.groupModbus.Controls.Add(this.btnMbConnect);
            this.groupModbus.Controls.Add(this.btnMbRead);
            this.groupModbus.Controls.Add(this.btnMbWrite);
            this.groupModbus.Controls.Add(this.lblMbHost);
            this.groupModbus.Controls.Add(this.lblMbPort);
            this.groupModbus.Controls.Add(this.lblAddr);
            this.groupModbus.Controls.Add(this.lblCount);
            this.groupModbus.Controls.Add(this.lblValue);

            this.lblMbHost.AutoSize = true;
            this.lblMbHost.Location = new System.Drawing.Point(12, 27);
            this.lblMbHost.Name = "lblMbHost";
            this.lblMbHost.Size = new System.Drawing.Size(72, 15);
            this.lblMbHost.Text = "从站 IP：";
            this.lblMbHost.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            this.txtMbHost.Location = new System.Drawing.Point(100, 24);
            this.txtMbHost.Name = "txtMbHost";
            this.txtMbHost.Size = new System.Drawing.Size(170, 21);
            this.txtMbHost.TabIndex = 0;
            this.txtMbHost.Text = "127.0.0.1";

            this.lblMbPort.AutoSize = true;
            this.lblMbPort.Location = new System.Drawing.Point(12, 55);
            this.lblMbPort.Name = "lblMbPort";
            this.lblMbPort.Size = new System.Drawing.Size(50, 15);
            this.lblMbPort.Text = "端口：";
            this.lblMbPort.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            this.numMbPort.Location = new System.Drawing.Point(100, 52);
            this.numMbPort.Maximum = new decimal(new int[] { 65535, 0, 0, 0 });
            this.numMbPort.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numMbPort.Name = "numMbPort";
            this.numMbPort.Size = new System.Drawing.Size(170, 21);
            this.numMbPort.TabIndex = 1;
            this.numMbPort.Value = new decimal(new int[] { 502, 0, 0, 0 });

            this.lblAddr.AutoSize = true;
            this.lblAddr.Location = new System.Drawing.Point(12, 83);
            this.lblAddr.Name = "lblAddr";
            this.lblAddr.Size = new System.Drawing.Size(72, 15);
            this.lblAddr.Text = "起始地址：";
            this.lblAddr.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            this.numAddr.Location = new System.Drawing.Point(100, 80);
            this.numAddr.Maximum = new decimal(new int[] { 65535, 0, 0, 0 });
            this.numAddr.Name = "numAddr";
            this.numAddr.Size = new System.Drawing.Size(170, 21);
            this.numAddr.TabIndex = 2;

            this.lblCount.AutoSize = true;
            this.lblCount.Location = new System.Drawing.Point(12, 111);
            this.lblCount.Name = "lblCount";
            this.lblCount.Size = new System.Drawing.Size(50, 15);
            this.lblCount.Text = "数量：";
            this.lblCount.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            this.numCount.Location = new System.Drawing.Point(100, 108);
            this.numCount.Maximum = new decimal(new int[] { 125, 0, 0, 0 });
            this.numCount.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numCount.Name = "numCount";
            this.numCount.Size = new System.Drawing.Size(170, 21);
            this.numCount.TabIndex = 3;
            this.numCount.Value = new decimal(new int[] { 4, 0, 0, 0 });

            this.lblValue.AutoSize = true;
            this.lblValue.Location = new System.Drawing.Point(12, 139);
            this.lblValue.Name = "lblValue";
            this.lblValue.Size = new System.Drawing.Size(72, 15);
            this.lblValue.Text = "写入值：";
            this.lblValue.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            this.numValue.Location = new System.Drawing.Point(100, 136);
            this.numValue.Maximum = new decimal(new int[] { 65535, 0, 0, 0 });
            this.numValue.Name = "numValue";
            this.numValue.Size = new System.Drawing.Size(170, 21);
            this.numValue.TabIndex = 4;
            this.numValue.Value = new decimal(new int[] { 1, 0, 0, 0 });

            this.btnMbConnect.Location = new System.Drawing.Point(16, 178);
            this.btnMbConnect.Name = "btnMbConnect";
            this.btnMbConnect.Size = new System.Drawing.Size(110, 28);
            this.btnMbConnect.TabIndex = 5;
            this.btnMbConnect.Text = "连接";
            this.btnMbConnect.Click += new System.EventHandler(this.OnModbusConnect);

            this.btnMbRead.Location = new System.Drawing.Point(134, 178);
            this.btnMbRead.Name = "btnMbRead";
            this.btnMbRead.Size = new System.Drawing.Size(110, 28);
            this.btnMbRead.TabIndex = 6;
            this.btnMbRead.Text = "读 0x03";
            this.btnMbRead.Click += new System.EventHandler(this.OnModbusRead);

            this.btnMbWrite.Location = new System.Drawing.Point(252, 178);
            this.btnMbWrite.Name = "btnMbWrite";
            this.btnMbWrite.Size = new System.Drawing.Size(110, 28);
            this.btnMbWrite.TabIndex = 7;
            this.btnMbWrite.Text = "写 0x06";
            this.btnMbWrite.Click += new System.EventHandler(this.OnModbusWrite);

            // ---------- panelRight ----------
            this.panelRight.BackColor = System.Drawing.Color.FromArgb(38, 38, 46);
            this.panelRight.Controls.Add(this.listLog);
            this.panelRight.Dock = System.Windows.Forms.DockStyle.Fill;
            this.panelRight.Location = new System.Drawing.Point(420, 0);
            this.panelRight.Name = "panelRight";
            this.panelRight.Padding = new System.Windows.Forms.Padding(8);
            this.panelRight.Size = new System.Drawing.Size(580, 560);
            this.panelRight.TabIndex = 1;

            this.listLog.BackColor = System.Drawing.Color.FromArgb(28, 28, 34);
            this.listLog.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.listLog.Dock = System.Windows.Forms.DockStyle.Fill;
            this.listLog.Font = new System.Drawing.Font("Consolas", 9F);
            this.listLog.ForeColor = System.Drawing.Color.Gainsboro;
            this.listLog.IntegralHeight = false;
            this.listLog.ItemHeight = 15;
            this.listLog.Location = new System.Drawing.Point(8, 8);
            this.listLog.Name = "listLog";
            this.listLog.Size = new System.Drawing.Size(564, 544);
            this.listLog.TabIndex = 0;

            // ---------- CommForm ----------
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(38, 38, 46);
            this.ClientSize = new System.Drawing.Size(1000, 560);
            this.Controls.Add(this.panelRight);
            this.Controls.Add(this.panelLeft);
            this.ForeColor = System.Drawing.Color.Gainsboro;
            this.MinimumSize = new System.Drawing.Size(880, 480);
            this.Name = "CommForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "通讯（TCP / Modbus TCP）";

            ((System.ComponentModel.ISupportInitialize)(this.numClientPort)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numServerPort)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numMbPort)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numAddr)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numCount)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numValue)).EndInit();
            this.panelLeft.ResumeLayout(false);
            this.panelLeft.PerformLayout();
            this.groupClient.ResumeLayout(false);
            this.groupClient.PerformLayout();
            this.groupServer.ResumeLayout(false);
            this.groupServer.PerformLayout();
            this.groupModbus.ResumeLayout(false);
            this.groupModbus.PerformLayout();
            this.panelRight.ResumeLayout(false);
            this.ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.Panel panelLeft;
        private System.Windows.Forms.GroupBox groupClient;
        private System.Windows.Forms.Label lblHost;
        private System.Windows.Forms.TextBox txtHost;
        private System.Windows.Forms.Label lblClientPort;
        private System.Windows.Forms.NumericUpDown numClientPort;
        private System.Windows.Forms.Label lblSend;
        private System.Windows.Forms.TextBox txtSend;
        private VisionPlatform.UI.UiButton btnClientConnect;
        private VisionPlatform.UI.UiButton btnClientDisconnect;
        private VisionPlatform.UI.UiButton btnSend;
        private System.Windows.Forms.GroupBox groupServer;
        private System.Windows.Forms.Label lblServerPort;
        private System.Windows.Forms.NumericUpDown numServerPort;
        private VisionPlatform.UI.UiButton btnListen;
        private VisionPlatform.UI.UiButton btnStopListen;
        private VisionPlatform.UI.UiButton btnBroadcast;
        private System.Windows.Forms.GroupBox groupModbus;
        private System.Windows.Forms.Label lblMbHost;
        private System.Windows.Forms.TextBox txtMbHost;
        private System.Windows.Forms.Label lblMbPort;
        private System.Windows.Forms.NumericUpDown numMbPort;
        private System.Windows.Forms.Label lblAddr;
        private System.Windows.Forms.NumericUpDown numAddr;
        private System.Windows.Forms.Label lblCount;
        private System.Windows.Forms.NumericUpDown numCount;
        private System.Windows.Forms.Label lblValue;
        private System.Windows.Forms.NumericUpDown numValue;
        private VisionPlatform.UI.UiButton btnMbConnect;
        private VisionPlatform.UI.UiButton btnMbRead;
        private VisionPlatform.UI.UiButton btnMbWrite;
        private System.Windows.Forms.Panel panelRight;
        private System.Windows.Forms.ListBox listLog;
    }
}
