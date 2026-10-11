namespace VisionPlatform.UI
{
    partial class CameraForm
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
            this.listCameras = new System.Windows.Forms.ListBox();
            this.panelButtons = new System.Windows.Forms.FlowLayoutPanel();
            this.btnRefresh = new VisionPlatform.UI.UiButton();
            this.btnOpen = new VisionPlatform.UI.UiButton();
            this.btnClose = new VisionPlatform.UI.UiButton();
            this.btnStart = new VisionPlatform.UI.UiButton();
            this.btnStop = new VisionPlatform.UI.UiButton();
            this.btnGrab = new VisionPlatform.UI.UiButton();
            this.lblStatus = new System.Windows.Forms.Label();

            this.panelRight = new System.Windows.Forms.Panel();

            this.panelLeft.SuspendLayout();
            this.panelButtons.SuspendLayout();
            this.SuspendLayout();

            // ---------- panelLeft ----------
            this.panelLeft.BackColor = System.Drawing.Color.FromArgb(45, 45, 52);
            this.panelLeft.Controls.Add(this.listCameras);
            this.panelLeft.Controls.Add(this.panelButtons);
            this.panelLeft.Controls.Add(this.lblStatus);
            this.panelLeft.Dock = System.Windows.Forms.DockStyle.Left;
            this.panelLeft.Location = new System.Drawing.Point(0, 0);
            this.panelLeft.Name = "panelLeft";
            this.panelLeft.Padding = new System.Windows.Forms.Padding(8);
            this.panelLeft.Size = new System.Drawing.Size(250, 560);
            this.panelLeft.TabIndex = 0;

            this.listCameras.BackColor = System.Drawing.Color.FromArgb(38, 38, 44);
            this.listCameras.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.listCameras.Dock = System.Windows.Forms.DockStyle.Fill;
            this.listCameras.ForeColor = System.Drawing.Color.Gainsboro;
            this.listCameras.IntegralHeight = false;
            this.listCameras.ItemHeight = 15;
            this.listCameras.Location = new System.Drawing.Point(8, 8);
            this.listCameras.Name = "listCameras";
            this.listCameras.Size = new System.Drawing.Size(234, 340);
            this.listCameras.TabIndex = 0;
            this.listCameras.SelectedIndexChanged += new System.EventHandler(this.OnSelectionChanged);

            this.panelButtons.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.panelButtons.FlowDirection = System.Windows.Forms.FlowDirection.TopDown;
            this.panelButtons.Location = new System.Drawing.Point(8, 348);
            this.panelButtons.Name = "panelButtons";
            this.panelButtons.Size = new System.Drawing.Size(234, 208);
            this.panelButtons.TabIndex = 1;
            this.panelButtons.WrapContents = false;

            this.btnRefresh.Location = new System.Drawing.Point(3, 3);
            this.btnRefresh.Name = "btnRefresh";
            this.btnRefresh.Size = new System.Drawing.Size(220, 28);
            this.btnRefresh.TabIndex = 0;
            this.btnRefresh.Text = "刷新列表";
            this.btnRefresh.Click += new System.EventHandler(this.OnRefresh);

            this.btnOpen.Location = new System.Drawing.Point(3, 37);
            this.btnOpen.Name = "btnOpen";
            this.btnOpen.Size = new System.Drawing.Size(220, 28);
            this.btnOpen.TabIndex = 1;
            this.btnOpen.Text = "打开";
            this.btnOpen.Click += new System.EventHandler(this.OnOpen);

            this.btnClose.Location = new System.Drawing.Point(3, 71);
            this.btnClose.Name = "btnClose";
            this.btnClose.Size = new System.Drawing.Size(220, 28);
            this.btnClose.TabIndex = 2;
            this.btnClose.Text = "关闭";
            this.btnClose.Click += new System.EventHandler(this.OnClose);

            this.btnStart.Location = new System.Drawing.Point(3, 105);
            this.btnStart.Name = "btnStart";
            this.btnStart.Size = new System.Drawing.Size(220, 28);
            this.btnStart.TabIndex = 3;
            this.btnStart.Text = "开始采集";
            this.btnStart.Click += new System.EventHandler(this.OnStart);

            this.btnStop.Location = new System.Drawing.Point(3, 139);
            this.btnStop.Name = "btnStop";
            this.btnStop.Size = new System.Drawing.Size(220, 28);
            this.btnStop.TabIndex = 4;
            this.btnStop.Text = "停止采集";
            this.btnStop.Click += new System.EventHandler(this.OnStop);

            this.btnGrab.Location = new System.Drawing.Point(3, 173);
            this.btnGrab.Name = "btnGrab";
            this.btnGrab.Size = new System.Drawing.Size(220, 28);
            this.btnGrab.TabIndex = 5;
            this.btnGrab.Text = "抓一帧";
            this.btnGrab.Click += new System.EventHandler(this.OnGrab);

            this.panelButtons.Controls.Add(this.btnRefresh);
            this.panelButtons.Controls.Add(this.btnOpen);
            this.panelButtons.Controls.Add(this.btnClose);
            this.panelButtons.Controls.Add(this.btnStart);
            this.panelButtons.Controls.Add(this.btnStop);
            this.panelButtons.Controls.Add(this.btnGrab);

            this.lblStatus.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.lblStatus.ForeColor = System.Drawing.Color.Silver;
            this.lblStatus.Location = new System.Drawing.Point(8, 516);
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Size = new System.Drawing.Size(234, 36);
            this.lblStatus.TabIndex = 2;
            this.lblStatus.Text = "未选择相机";

            // ---------- panelRight ----------
            this.panelRight.BackColor = System.Drawing.Color.FromArgb(38, 38, 46);
            this.panelRight.Dock = System.Windows.Forms.DockStyle.Fill;
            this.panelRight.Location = new System.Drawing.Point(250, 0);
            this.panelRight.Name = "panelRight";
            this.panelRight.Padding = new System.Windows.Forms.Padding(4);
            this.panelRight.Size = new System.Drawing.Size(750, 560);
            this.panelRight.TabIndex = 1;

            // ---------- CameraForm ----------
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(38, 38, 46);
            this.ClientSize = new System.Drawing.Size(1000, 560);
            this.Controls.Add(this.panelRight);
            this.Controls.Add(this.panelLeft);
            this.ForeColor = System.Drawing.Color.Gainsboro;
            this.MinimumSize = new System.Drawing.Size(880, 480);
            this.Name = "CameraForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "相机管理";

            this.panelLeft.ResumeLayout(false);
            this.panelButtons.ResumeLayout(false);
            this.ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.Panel panelLeft;
        private System.Windows.Forms.ListBox listCameras;
        private System.Windows.Forms.FlowLayoutPanel panelButtons;
        private VisionPlatform.UI.UiButton btnRefresh;
        private VisionPlatform.UI.UiButton btnOpen;
        private VisionPlatform.UI.UiButton btnClose;
        private VisionPlatform.UI.UiButton btnStart;
        private VisionPlatform.UI.UiButton btnStop;
        private VisionPlatform.UI.UiButton btnGrab;
        private System.Windows.Forms.Label lblStatus;
        private System.Windows.Forms.Panel panelRight;
    }
}
