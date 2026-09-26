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

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.toolStrip = new System.Windows.Forms.ToolStrip();
            this.tsLabelCamera = new System.Windows.Forms.ToolStripLabel();
            this.tsComboCameras = new System.Windows.Forms.ToolStripComboBox();
            this.tsBtnRefresh = new System.Windows.Forms.ToolStripButton();
            this.tsSep1 = new System.Windows.Forms.ToolStripSeparator();
            this.tsLabelLayout = new System.Windows.Forms.ToolStripLabel();
            this.tsBtnView1 = new System.Windows.Forms.ToolStripButton();
            this.tsBtnView2 = new System.Windows.Forms.ToolStripButton();
            this.tsBtnView4 = new System.Windows.Forms.ToolStripButton();
            this.tsSep2 = new System.Windows.Forms.ToolStripSeparator();
            this.tsBtnGrabOnce = new System.Windows.Forms.ToolStripButton();
            this.tsBtnSyncGrab = new System.Windows.Forms.ToolStripButton();
            this.tsBtnStartLoop = new System.Windows.Forms.ToolStripButton();
            this.tsBtnPause = new System.Windows.Forms.ToolStripButton();
            this.tsSep3 = new System.Windows.Forms.ToolStripSeparator();
            this.tsBtnCameraMgr = new System.Windows.Forms.ToolStripButton();
            this.tsBtnAlgo = new System.Windows.Forms.ToolStripButton();
            this.tsBtnComm = new System.Windows.Forms.ToolStripButton();
            this.tsBtnRecipe = new System.Windows.Forms.ToolStripButton();
            this.tsLabelStatus = new System.Windows.Forms.ToolStripLabel();

            this.panelCenter = new System.Windows.Forms.Panel();
            this.tableViews = new System.Windows.Forms.TableLayoutPanel();

            this.panelRight = new System.Windows.Forms.Panel();
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
            this.btnLoop = new System.Windows.Forms.Button();
            this.btnPause = new System.Windows.Forms.Button();
            this.btnStop = new System.Windows.Forms.Button();
            this.btnResetStats = new System.Windows.Forms.Button();

            this.panelBottom = new System.Windows.Forms.Panel();
            this.listLog = new System.Windows.Forms.ListBox();

            this.statusStrip = new System.Windows.Forms.StatusStrip();
            this.statusLabel = new System.Windows.Forms.ToolStripStatusLabel();

            this.loopTimer = new System.Windows.Forms.Timer(this.components);

            this.toolStrip.SuspendLayout();
            this.panelCenter.SuspendLayout();
            this.panelRight.SuspendLayout();
            this.groupStats.SuspendLayout();
            this.tableStats.SuspendLayout();
            this.panelBottom.SuspendLayout();
            this.statusStrip.SuspendLayout();
            this.SuspendLayout();

            // ---------- toolStrip ----------
            this.toolStrip.BackColor = System.Drawing.Color.FromArgb(30, 30, 38);
            this.toolStrip.ForeColor = System.Drawing.Color.Gainsboro;
            this.toolStrip.GripStyle = System.Windows.Forms.ToolStripGripStyle.Hidden;
            this.toolStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
                this.tsLabelCamera,
                this.tsComboCameras,
                this.tsBtnRefresh,
                this.tsSep1,
                this.tsLabelLayout,
                this.tsBtnView1,
                this.tsBtnView2,
                this.tsBtnView4,
                this.tsSep2,
                this.tsBtnGrabOnce,
                this.tsBtnSyncGrab,
                this.tsBtnStartLoop,
                this.tsBtnPause,
                this.tsSep3,
                this.tsBtnCameraMgr,
                this.tsBtnAlgo,
                this.tsBtnComm,
                this.tsBtnRecipe,
                this.tsLabelStatus});
            this.toolStrip.Location = new System.Drawing.Point(0, 0);
            this.toolStrip.Name = "toolStrip";
            this.toolStrip.Size = new System.Drawing.Size(1440, 32);
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

            this.tsSep1.Name = "tsSep1";

            this.tsLabelLayout.Name = "tsLabelLayout";
            this.tsLabelLayout.Text = "布局:";

            this.tsBtnView1.CheckOnClick = true;
            this.tsBtnView1.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.tsBtnView1.Name = "tsBtnView1";
            this.tsBtnView1.Text = "1";
            this.tsBtnView1.Click += new System.EventHandler(this.OnLayout1);

            this.tsBtnView2.CheckOnClick = true;
            this.tsBtnView2.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.tsBtnView2.Name = "tsBtnView2";
            this.tsBtnView2.Text = "2";
            this.tsBtnView2.Click += new System.EventHandler(this.OnLayout2);

            this.tsBtnView4.CheckOnClick = true;
            this.tsBtnView4.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.tsBtnView4.Name = "tsBtnView4";
            this.tsBtnView4.Text = "4";
            this.tsBtnView4.Click += new System.EventHandler(this.OnLayout4);

            this.tsSep2.Name = "tsSep2";

            this.tsBtnGrabOnce.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.tsBtnGrabOnce.Name = "tsBtnGrabOnce";
            this.tsBtnGrabOnce.Text = "单帧采集";
            this.tsBtnGrabOnce.ToolTipText = "对当前相机采集一帧并检测";
            this.tsBtnGrabOnce.Click += new System.EventHandler(this.OnGrabOnce);

            this.tsBtnSyncGrab.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.tsBtnSyncGrab.Name = "tsBtnSyncGrab";
            this.tsBtnSyncGrab.Text = "同步采集";
            this.tsBtnSyncGrab.ToolTipText = "对所有已打开相机同时取帧（同步拍摄）";
            this.tsBtnSyncGrab.Click += new System.EventHandler(this.OnSyncGrab);

            this.tsBtnStartLoop.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.tsBtnStartLoop.Name = "tsBtnStartLoop";
            this.tsBtnStartLoop.Text = "循环运行";
            this.tsBtnStartLoop.Click += new System.EventHandler(this.OnStartLoop);

            this.tsBtnPause.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.tsBtnPause.Name = "tsBtnPause";
            this.tsBtnPause.Text = "暂停";
            this.tsBtnPause.Click += new System.EventHandler(this.OnPause);

            this.tsSep3.Name = "tsSep3";

            this.tsBtnCameraMgr.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.tsBtnCameraMgr.Name = "tsBtnCameraMgr";
            this.tsBtnCameraMgr.Text = "相机管理";
            this.tsBtnCameraMgr.Click += new System.EventHandler(this.OnOpenCameraForm);

            this.tsBtnAlgo.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.tsBtnAlgo.Name = "tsBtnAlgo";
            this.tsBtnAlgo.Text = "算法";
            this.tsBtnAlgo.Click += new System.EventHandler(this.OnOpenAlgoForm);

            this.tsBtnComm.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.tsBtnComm.Name = "tsBtnComm";
            this.tsBtnComm.Text = "通讯";
            this.tsBtnComm.Click += new System.EventHandler(this.OnOpenCommForm);

            this.tsBtnRecipe.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.tsBtnRecipe.Name = "tsBtnRecipe";
            this.tsBtnRecipe.Text = "配方";
            this.tsBtnRecipe.Click += new System.EventHandler(this.OnOpenRecipeForm);

            this.tsLabelStatus.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            this.tsLabelStatus.Name = "tsLabelStatus";
            this.tsLabelStatus.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            this.tsLabelStatus.ForeColor = System.Drawing.Color.DeepSkyBlue;

            // ---------- panelCenter ----------
            this.panelCenter.BackColor = System.Drawing.Color.FromArgb(24, 24, 30);
            this.panelCenter.Controls.Add(this.tableViews);
            this.panelCenter.Dock = System.Windows.Forms.DockStyle.Fill;
            this.panelCenter.Location = new System.Drawing.Point(0, 32);
            this.panelCenter.Name = "panelCenter";
            this.panelCenter.Padding = new System.Windows.Forms.Padding(4);
            this.panelCenter.Size = new System.Drawing.Size(1196, 640);
            this.panelCenter.TabIndex = 1;

            this.tableViews.ColumnCount = 1;
            this.tableViews.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tableViews.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tableViews.Location = new System.Drawing.Point(4, 4);
            this.tableViews.Name = "tableViews";
            this.tableViews.RowCount = 1;
            this.tableViews.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tableViews.Size = new System.Drawing.Size(1188, 632);
            this.tableViews.TabIndex = 0;

            // ---------- panelRight ----------
            this.panelRight.BackColor = System.Drawing.Color.FromArgb(42, 42, 50);
            this.panelRight.Controls.Add(this.btnResetStats);
            this.panelRight.Controls.Add(this.btnStop);
            this.panelRight.Controls.Add(this.btnPause);
            this.panelRight.Controls.Add(this.btnLoop);
            this.panelRight.Controls.Add(this.groupStats);
            this.panelRight.Dock = System.Windows.Forms.DockStyle.Right;
            this.panelRight.Location = new System.Drawing.Point(1196, 32);
            this.panelRight.Name = "panelRight";
            this.panelRight.Padding = new System.Windows.Forms.Padding(8);
            this.panelRight.Size = new System.Drawing.Size(244, 640);
            this.panelRight.TabIndex = 2;

            this.groupStats.Dock = System.Windows.Forms.DockStyle.Top;
            this.groupStats.ForeColor = System.Drawing.Color.DeepSkyBlue;
            this.groupStats.Location = new System.Drawing.Point(8, 8);
            this.groupStats.Name = "groupStats";
            this.groupStats.Size = new System.Drawing.Size(228, 200);
            this.groupStats.TabIndex = 0;
            this.groupStats.TabStop = false;
            this.groupStats.Text = "数据统计";

            this.tableStats.ColumnCount = 2;
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
            this.tableStats.RowCount = 5;
            this.tableStats.Size = new System.Drawing.Size(222, 179);
            this.tableStats.TabIndex = 0;

            this.lblTotalCap.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblTotalCap.ForeColor = System.Drawing.Color.Silver;
            this.lblTotalCap.Text = "总数";
            this.lblTotalCap.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            this.lblTotalValue.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblTotalValue.Font = new System.Drawing.Font("Consolas", 11F, System.Drawing.FontStyle.Bold);
            this.lblTotalValue.ForeColor = System.Drawing.Color.Gainsboro;
            this.lblTotalValue.Text = "0";
            this.lblTotalValue.TextAlign = System.Drawing.ContentAlignment.MiddleRight;

            this.lblOkCap.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblOkCap.ForeColor = System.Drawing.Color.Silver;
            this.lblOkCap.Text = "OK";
            this.lblOkCap.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            this.lblOkValue.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblOkValue.Font = new System.Drawing.Font("Consolas", 11F, System.Drawing.FontStyle.Bold);
            this.lblOkValue.ForeColor = System.Drawing.Color.LimeGreen;
            this.lblOkValue.Text = "0";
            this.lblOkValue.TextAlign = System.Drawing.ContentAlignment.MiddleRight;

            this.lblNgCap.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblNgCap.ForeColor = System.Drawing.Color.Silver;
            this.lblNgCap.Text = "NG";
            this.lblNgCap.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            this.lblNgValue.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblNgValue.Font = new System.Drawing.Font("Consolas", 11F, System.Drawing.FontStyle.Bold);
            this.lblNgValue.ForeColor = System.Drawing.Color.OrangeRed;
            this.lblNgValue.Text = "0";
            this.lblNgValue.TextAlign = System.Drawing.ContentAlignment.MiddleRight;

            this.lblYieldCap.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblYieldCap.ForeColor = System.Drawing.Color.Silver;
            this.lblYieldCap.Text = "良率";
            this.lblYieldCap.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            this.lblYieldValue.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblYieldValue.Font = new System.Drawing.Font("Consolas", 11F, System.Drawing.FontStyle.Bold);
            this.lblYieldValue.ForeColor = System.Drawing.Color.DeepSkyBlue;
            this.lblYieldValue.Text = "--";
            this.lblYieldValue.TextAlign = System.Drawing.ContentAlignment.MiddleRight;

            this.lblCycleCap.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblCycleCap.ForeColor = System.Drawing.Color.Silver;
            this.lblCycleCap.Text = "节拍";
            this.lblCycleCap.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            this.lblCycleValue.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblCycleValue.Font = new System.Drawing.Font("Consolas", 11F, System.Drawing.FontStyle.Bold);
            this.lblCycleValue.ForeColor = System.Drawing.Color.Gainsboro;
            this.lblCycleValue.Text = "--";
            this.lblCycleValue.TextAlign = System.Drawing.ContentAlignment.MiddleRight;

            this.btnLoop.Dock = System.Windows.Forms.DockStyle.Top;
            this.btnLoop.Height = 34;
            this.btnLoop.Location = new System.Drawing.Point(8, 208);
            this.btnLoop.Name = "btnLoop";
            this.btnLoop.Size = new System.Drawing.Size(228, 34);
            this.btnLoop.TabIndex = 1;
            this.btnLoop.Text = "循环运行";
            this.btnLoop.Click += new System.EventHandler(this.OnStartLoop);

            this.btnPause.Dock = System.Windows.Forms.DockStyle.Top;
            this.btnPause.Height = 34;
            this.btnPause.Location = new System.Drawing.Point(8, 242);
            this.btnPause.Name = "btnPause";
            this.btnPause.Size = new System.Drawing.Size(228, 34);
            this.btnPause.TabIndex = 2;
            this.btnPause.Text = "暂停 / 继续";
            this.btnPause.Click += new System.EventHandler(this.OnPause);

            this.btnStop.Dock = System.Windows.Forms.DockStyle.Top;
            this.btnStop.Height = 34;
            this.btnStop.Location = new System.Drawing.Point(8, 276);
            this.btnStop.Name = "btnStop";
            this.btnStop.Size = new System.Drawing.Size(228, 34);
            this.btnStop.TabIndex = 3;
            this.btnStop.Text = "停止";
            this.btnStop.Click += new System.EventHandler(this.OnStop);

            this.btnResetStats.Dock = System.Windows.Forms.DockStyle.Top;
            this.btnResetStats.Height = 34;
            this.btnResetStats.Location = new System.Drawing.Point(8, 310);
            this.btnResetStats.Name = "btnResetStats";
            this.btnResetStats.Size = new System.Drawing.Size(228, 34);
            this.btnResetStats.TabIndex = 4;
            this.btnResetStats.Text = "清空统计";
            this.btnResetStats.Click += new System.EventHandler(this.OnResetStats);

            // ---------- panelBottom ----------
            this.panelBottom.BackColor = System.Drawing.Color.FromArgb(28, 28, 34);
            this.panelBottom.Controls.Add(this.listLog);
            this.panelBottom.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.panelBottom.Location = new System.Drawing.Point(0, 672);
            this.panelBottom.Name = "panelBottom";
            this.panelBottom.Padding = new System.Windows.Forms.Padding(6);
            this.panelBottom.Size = new System.Drawing.Size(1440, 170);
            this.panelBottom.TabIndex = 3;

            this.listLog.BackColor = System.Drawing.Color.FromArgb(24, 24, 30);
            this.listLog.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.listLog.Dock = System.Windows.Forms.DockStyle.Fill;
            this.listLog.Font = new System.Drawing.Font("Consolas", 9F);
            this.listLog.ForeColor = System.Drawing.Color.FromArgb(190, 195, 205);
            this.listLog.IntegralHeight = false;
            this.listLog.ItemHeight = 15;
            this.listLog.Location = new System.Drawing.Point(6, 6);
            this.listLog.Name = "listLog";
            this.listLog.Size = new System.Drawing.Size(1428, 158);
            this.listLog.TabIndex = 0;

            // ---------- statusStrip ----------
            this.statusStrip.BackColor = System.Drawing.Color.FromArgb(30, 30, 38);
            this.statusStrip.ForeColor = System.Drawing.Color.Silver;
            this.statusStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
                this.statusLabel});
            this.statusStrip.Location = new System.Drawing.Point(0, 842);
            this.statusStrip.Name = "statusStrip";
            this.statusStrip.Size = new System.Drawing.Size(1440, 24);
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
            this.ClientSize = new System.Drawing.Size(1440, 866);
            this.Controls.Add(this.panelCenter);
            this.Controls.Add(this.panelRight);
            this.Controls.Add(this.panelBottom);
            this.Controls.Add(this.statusStrip);
            this.Controls.Add(this.toolStrip);
            this.ForeColor = System.Drawing.Color.Gainsboro;
            this.MinimumSize = new System.Drawing.Size(1100, 700);
            this.Name = "MainForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "VisionPlatform";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.OnFormClosing);

            this.toolStrip.ResumeLayout(false);
            this.toolStrip.PerformLayout();
            this.panelCenter.ResumeLayout(false);
            this.panelRight.ResumeLayout(false);
            this.groupStats.ResumeLayout(false);
            this.tableStats.ResumeLayout(false);
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
        private System.Windows.Forms.ToolStripSeparator tsSep1;
        private System.Windows.Forms.ToolStripLabel tsLabelLayout;
        private System.Windows.Forms.ToolStripButton tsBtnView1;
        private System.Windows.Forms.ToolStripButton tsBtnView2;
        private System.Windows.Forms.ToolStripButton tsBtnView4;
        private System.Windows.Forms.ToolStripSeparator tsSep2;
        private System.Windows.Forms.ToolStripButton tsBtnGrabOnce;
        private System.Windows.Forms.ToolStripButton tsBtnSyncGrab;
        private System.Windows.Forms.ToolStripButton tsBtnStartLoop;
        private System.Windows.Forms.ToolStripButton tsBtnPause;
        private System.Windows.Forms.ToolStripSeparator tsSep3;
        private System.Windows.Forms.ToolStripButton tsBtnCameraMgr;
        private System.Windows.Forms.ToolStripButton tsBtnAlgo;
        private System.Windows.Forms.ToolStripButton tsBtnComm;
        private System.Windows.Forms.ToolStripButton tsBtnRecipe;
        private System.Windows.Forms.ToolStripLabel tsLabelStatus;

        private System.Windows.Forms.Panel panelCenter;
        private System.Windows.Forms.TableLayoutPanel tableViews;

        private System.Windows.Forms.Panel panelRight;
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
        private System.Windows.Forms.Button btnLoop;
        private System.Windows.Forms.Button btnPause;
        private System.Windows.Forms.Button btnStop;
        private System.Windows.Forms.Button btnResetStats;

        private System.Windows.Forms.Panel panelBottom;
        private System.Windows.Forms.ListBox listLog;

        private System.Windows.Forms.StatusStrip statusStrip;
        private System.Windows.Forms.ToolStripStatusLabel statusLabel;

        private System.Windows.Forms.Timer loopTimer;
    }
}
