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
            this.txtHost = new System.Windows.Forms.TextBox();
            this.numClientPort = new System.Windows.Forms.NumericUpDown();
            this.txtSend = new System.Windows.Forms.TextBox();
            this.btnClientConnect = new System.Windows.Forms.Button();
            this.btnClientDisconnect = new System.Windows.Forms.Button();
            this.btnSend = new System.Windows.Forms.Button();
            this.groupServer = new System.Windows.Forms.GroupBox();
            this.numServerPort = new System.Windows.Forms.NumericUpDown();
            this.btnListen = new System.Windows.Forms.Button();
            this.btnStopListen = new System.Windows.Forms.Button();
            this.btnBroadcast = new System.Windows.Forms.Button();
            this.groupModbus = new System.Windows.Forms.GroupBox();
            this.txtMbHost = new System.Windows.Forms.TextBox();
            this.numMbPort = new System.Windows.Forms.NumericUpDown();
            this.numAddr = new System.Windows.Forms.NumericUpDown();
            this.numCount = new System.Windows.Forms.NumericUpDown();
            this.numValue = new System.Windows.Forms.NumericUpDown();
            this.btnMbConnect = new System.Windows.Forms.Button();
            this.btnMbRead = new System.Windows.Forms.Button();
            this.btnMbWrite = new System.Windows.Forms.Button();

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

            this.txtHost.Location = new System.Drawing.Point(90, 24);
            this.txtHost.Name = "txtHost";
            this.txtHost.Size = new System.Drawing.Size(180, 21);
            this.txtHost.TabIndex = 0;
            this.txtHost.Text = "127.0.0.1";

            this.numClientPort.Location = new System.Drawing.Point(90, 52);
            this.numClientPort.Maximum = new decimal(new int[] { 65535, 0, 0, 0 });
            this.numClientPort.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numClientPort.Name = "numClientPort";
            this.numClientPort.Size = new System.Drawing.Size(180, 21);
            this.numClientPort.TabIndex = 1;
            this.numClientPort.Value = new decimal(new int[] { 9000, 0, 0, 0 });

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

            this.txtMbHost.Location = new System.Drawing.Point(100, 24);
            this.txtMbHost.Name = "txtMbHost";
            this.txtMbHost.Size = new System.Drawing.Size(170, 21);
            this.txtMbHost.TabIndex = 0;
            this.txtMbHost.Text = "127.0.0.1";

            this.numMbPort.Location = new System.Drawing.Point(100, 52);
            this.numMbPort.Maximum = new decimal(new int[] { 65535, 0, 0, 0 });
            this.numMbPort.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numMbPort.Name = "numMbPort";
            this.numMbPort.Size = new System.Drawing.Size(170, 21);
            this.numMbPort.TabIndex = 1;
            this.numMbPort.Value = new decimal(new int[] { 502, 0, 0, 0 });

            this.numAddr.Location = new System.Drawing.Point(100, 80);
            this.numAddr.Maximum = new decimal(new int[] { 65535, 0, 0, 0 });
            this.numAddr.Name = "numAddr";
            this.numAddr.Size = new System.Drawing.Size(170, 21);
            this.numAddr.TabIndex = 2;

            this.numCount.Location = new System.Drawing.Point(100, 108);
            this.numCount.Maximum = new decimal(new int[] { 125, 0, 0, 0 });
            this.numCount.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numCount.Name = "numCount";
            this.numCount.Size = new System.Drawing.Size(170, 21);
            this.numCount.TabIndex = 3;
            this.numCount.Value = new decimal(new int[] { 4, 0, 0, 0 });

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
        private System.Windows.Forms.TextBox txtHost;
        private System.Windows.Forms.NumericUpDown numClientPort;
        private System.Windows.Forms.TextBox txtSend;
        private System.Windows.Forms.Button btnClientConnect;
        private System.Windows.Forms.Button btnClientDisconnect;
        private System.Windows.Forms.Button btnSend;
        private System.Windows.Forms.GroupBox groupServer;
        private System.Windows.Forms.NumericUpDown numServerPort;
        private System.Windows.Forms.Button btnListen;
        private System.Windows.Forms.Button btnStopListen;
        private System.Windows.Forms.Button btnBroadcast;
        private System.Windows.Forms.GroupBox groupModbus;
        private System.Windows.Forms.TextBox txtMbHost;
        private System.Windows.Forms.NumericUpDown numMbPort;
        private System.Windows.Forms.NumericUpDown numAddr;
        private System.Windows.Forms.NumericUpDown numCount;
        private System.Windows.Forms.NumericUpDown numValue;
        private System.Windows.Forms.Button btnMbConnect;
        private System.Windows.Forms.Button btnMbRead;
        private System.Windows.Forms.Button btnMbWrite;
        private System.Windows.Forms.Panel panelRight;
        private System.Windows.Forms.ListBox listLog;
    }
}
