namespace VisionPlatform.UI
{
    partial class SettingsForm
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
            this.groupSave = new System.Windows.Forms.GroupBox();
            this.lblPath = new System.Windows.Forms.Label();
            this.txtSavePath = new System.Windows.Forms.TextBox();
            this.btnBrowse = new VisionPlatform.UI.UiButton();
            this.lblFormat = new System.Windows.Forms.Label();
            this.cmbFormat = new System.Windows.Forms.ComboBox();
            this.lblDays = new System.Windows.Forms.Label();
            this.numDays = new System.Windows.Forms.NumericUpDown();
            this.lblDaysUnit = new System.Windows.Forms.Label();
            this.chkAutoSave = new System.Windows.Forms.CheckBox();
            this.chkNgOnly = new System.Windows.Forms.CheckBox();
            this.btnCleanup = new VisionPlatform.UI.UiButton();
            this.lblHint = new System.Windows.Forms.Label();

            this.groupRun = new System.Windows.Forms.GroupBox();
            this.chkRequireComm = new System.Windows.Forms.CheckBox();
            this.lblCommState = new System.Windows.Forms.Label();
            this.lblYieldAlarm = new System.Windows.Forms.Label();
            this.numYieldAlarm = new System.Windows.Forms.NumericUpDown();
            this.lblYieldAlarmUnit = new System.Windows.Forms.Label();

            this.groupTheme = new System.Windows.Forms.GroupBox();
            this.lblTheme = new System.Windows.Forms.Label();
            this.cmbTheme = new System.Windows.Forms.ComboBox();

            this.panelButtons = new System.Windows.Forms.Panel();
            this.btnDefault = new VisionPlatform.UI.UiButton();
            this.btnOk = new VisionPlatform.UI.UiButton();
            this.btnCancel = new VisionPlatform.UI.UiButton();

            ((System.ComponentModel.ISupportInitialize)(this.numDays)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numYieldAlarm)).BeginInit();
            this.groupSave.SuspendLayout();
            this.groupRun.SuspendLayout();
            this.groupTheme.SuspendLayout();
            this.panelButtons.SuspendLayout();
            this.SuspendLayout();

            // ---------- groupSave ----------
            this.groupSave.Controls.Add(this.lblHint);
            this.groupSave.Controls.Add(this.btnCleanup);
            this.groupSave.Controls.Add(this.chkNgOnly);
            this.groupSave.Controls.Add(this.chkAutoSave);
            this.groupSave.Controls.Add(this.lblDaysUnit);
            this.groupSave.Controls.Add(this.numDays);
            this.groupSave.Controls.Add(this.lblDays);
            this.groupSave.Controls.Add(this.cmbFormat);
            this.groupSave.Controls.Add(this.lblFormat);
            this.groupSave.Controls.Add(this.btnBrowse);
            this.groupSave.Controls.Add(this.txtSavePath);
            this.groupSave.Controls.Add(this.lblPath);
            this.groupSave.ForeColor = System.Drawing.Color.DeepSkyBlue;
            this.groupSave.Location = new System.Drawing.Point(12, 12);
            this.groupSave.Name = "groupSave";
            this.groupSave.Size = new System.Drawing.Size(496, 190);
            this.groupSave.TabIndex = 0;
            this.groupSave.TabStop = false;
            this.groupSave.Text = "图片保存";

            this.lblPath.AutoSize = true;
            this.lblPath.ForeColor = System.Drawing.Color.Silver;
            this.lblPath.Location = new System.Drawing.Point(14, 28);
            this.lblPath.Name = "lblPath";
            this.lblPath.Size = new System.Drawing.Size(67, 15);
            this.lblPath.TabIndex = 0;
            this.lblPath.Text = "保存路径：";

            this.txtSavePath.BackColor = System.Drawing.Color.FromArgb(30, 30, 38);
            this.txtSavePath.ForeColor = System.Drawing.Color.Gainsboro;
            this.txtSavePath.Location = new System.Drawing.Point(88, 25);
            this.txtSavePath.Name = "txtSavePath";
            this.txtSavePath.Size = new System.Drawing.Size(286, 23);
            this.txtSavePath.TabIndex = 1;

            this.btnBrowse.Location = new System.Drawing.Point(380, 23);
            this.btnBrowse.Name = "btnBrowse";
            this.btnBrowse.Size = new System.Drawing.Size(94, 26);
            this.btnBrowse.TabIndex = 2;
            this.btnBrowse.Text = "浏览...";
            this.btnBrowse.UseVisualStyleBackColor = true;
            this.btnBrowse.Click += new System.EventHandler(this.OnBrowse);

            this.lblFormat.AutoSize = true;
            this.lblFormat.ForeColor = System.Drawing.Color.Silver;
            this.lblFormat.Location = new System.Drawing.Point(14, 62);
            this.lblFormat.Name = "lblFormat";
            this.lblFormat.Size = new System.Drawing.Size(67, 15);
            this.lblFormat.TabIndex = 3;
            this.lblFormat.Text = "保存格式：";

            this.cmbFormat.BackColor = System.Drawing.Color.FromArgb(30, 30, 38);
            this.cmbFormat.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbFormat.ForeColor = System.Drawing.Color.Gainsboro;
            this.cmbFormat.FormattingEnabled = true;
            this.cmbFormat.Items.AddRange(new object[] { "PNG", "JPEG", "BMP", "TIFF" });
            this.cmbFormat.Location = new System.Drawing.Point(88, 59);
            this.cmbFormat.Name = "cmbFormat";
            this.cmbFormat.Size = new System.Drawing.Size(90, 23);
            this.cmbFormat.TabIndex = 4;

            this.lblDays.AutoSize = true;
            this.lblDays.ForeColor = System.Drawing.Color.Silver;
            this.lblDays.Location = new System.Drawing.Point(196, 62);
            this.lblDays.Name = "lblDays";
            this.lblDays.Size = new System.Drawing.Size(67, 15);
            this.lblDays.TabIndex = 5;
            this.lblDays.Text = "保留天数：";

            this.numDays.BackColor = System.Drawing.Color.FromArgb(30, 30, 38);
            this.numDays.ForeColor = System.Drawing.Color.Gainsboro;
            this.numDays.Location = new System.Drawing.Point(268, 59);
            this.numDays.Maximum = new decimal(new int[] { 3650, 0, 0, 0 });
            this.numDays.Name = "numDays";
            this.numDays.Size = new System.Drawing.Size(70, 23);
            this.numDays.TabIndex = 6;

            this.lblDaysUnit.AutoSize = true;
            this.lblDaysUnit.ForeColor = System.Drawing.Color.Gray;
            this.lblDaysUnit.Location = new System.Drawing.Point(344, 62);
            this.lblDaysUnit.Name = "lblDaysUnit";
            this.lblDaysUnit.Size = new System.Drawing.Size(95, 15);
            this.lblDaysUnit.TabIndex = 7;
            this.lblDaysUnit.Text = "天（0 = 不清理）";

            this.chkAutoSave.AutoSize = true;
            this.chkAutoSave.ForeColor = System.Drawing.Color.Gainsboro;
            this.chkAutoSave.Location = new System.Drawing.Point(88, 96);
            this.chkAutoSave.Name = "chkAutoSave";
            this.chkAutoSave.Size = new System.Drawing.Size(150, 19);
            this.chkAutoSave.TabIndex = 8;
            this.chkAutoSave.Text = "检测后自动保存图像";
            this.chkAutoSave.UseVisualStyleBackColor = true;

            this.chkNgOnly.AutoSize = true;
            this.chkNgOnly.ForeColor = System.Drawing.Color.Gainsboro;
            this.chkNgOnly.Location = new System.Drawing.Point(280, 96);
            this.chkNgOnly.Name = "chkNgOnly";
            this.chkNgOnly.Size = new System.Drawing.Size(120, 19);
            this.chkNgOnly.TabIndex = 9;
            this.chkNgOnly.Text = "仅保存 NG 图像";
            this.chkNgOnly.UseVisualStyleBackColor = true;

            this.btnCleanup.Location = new System.Drawing.Point(88, 128);
            this.btnCleanup.Name = "btnCleanup";
            this.btnCleanup.Size = new System.Drawing.Size(180, 28);
            this.btnCleanup.TabIndex = 10;
            this.btnCleanup.Text = "立即清理过期图片";
            this.btnCleanup.UseVisualStyleBackColor = true;
            this.btnCleanup.Click += new System.EventHandler(this.OnCleanupNow);

            this.lblHint.AutoSize = true;
            this.lblHint.ForeColor = System.Drawing.Color.Gray;
            this.lblHint.Location = new System.Drawing.Point(14, 164);
            this.lblHint.Name = "lblHint";
            this.lblHint.Size = new System.Drawing.Size(360, 15);
            this.lblHint.TabIndex = 11;
            this.lblHint.Text = "目录结构：{保存路径}\\{日期}\\{时间}_{标签}_{OK或NG}{扩展名}";

            // ---------- groupRun ----------
            this.groupRun.Controls.Add(this.lblYieldAlarmUnit);
            this.groupRun.Controls.Add(this.numYieldAlarm);
            this.groupRun.Controls.Add(this.lblYieldAlarm);
            this.groupRun.Controls.Add(this.lblCommState);
            this.groupRun.Controls.Add(this.chkRequireComm);
            this.groupRun.ForeColor = System.Drawing.Color.DeepSkyBlue;
            this.groupRun.Location = new System.Drawing.Point(12, 210);
            this.groupRun.Name = "groupRun";
            this.groupRun.Size = new System.Drawing.Size(496, 124);
            this.groupRun.TabIndex = 1;
            this.groupRun.TabStop = false;
            this.groupRun.Text = "运行前置条件";

            this.chkRequireComm.AutoSize = true;
            this.chkRequireComm.ForeColor = System.Drawing.Color.Gainsboro;
            this.chkRequireComm.Location = new System.Drawing.Point(14, 28);
            this.chkRequireComm.Name = "chkRequireComm";
            this.chkRequireComm.Size = new System.Drawing.Size(340, 19);
            this.chkRequireComm.TabIndex = 0;
            this.chkRequireComm.Text = "循环运行前要求通讯已连接（TCP 客户端 / Modbus / 服务端）";
            this.chkRequireComm.UseVisualStyleBackColor = true;

            this.lblCommState.AutoSize = true;
            this.lblCommState.ForeColor = System.Drawing.Color.Gray;
            this.lblCommState.Location = new System.Drawing.Point(14, 58);
            this.lblCommState.Name = "lblCommState";
            this.lblCommState.Size = new System.Drawing.Size(50, 15);
            this.lblCommState.TabIndex = 1;
            this.lblCommState.Text = "当前：";

            this.lblYieldAlarm.AutoSize = true;
            this.lblYieldAlarm.ForeColor = System.Drawing.Color.Silver;
            this.lblYieldAlarm.Location = new System.Drawing.Point(14, 86);
            this.lblYieldAlarm.Name = "lblYieldAlarm";
            this.lblYieldAlarm.Size = new System.Drawing.Size(88, 15);
            this.lblYieldAlarm.TabIndex = 2;
            this.lblYieldAlarm.Text = "良率告警下限：";

            this.numYieldAlarm.BackColor = System.Drawing.Color.FromArgb(30, 30, 38);
            this.numYieldAlarm.DecimalPlaces = 1;
            this.numYieldAlarm.ForeColor = System.Drawing.Color.Gainsboro;
            this.numYieldAlarm.Increment = new decimal(new int[] { 5, 0, 0, 0 });
            this.numYieldAlarm.Location = new System.Drawing.Point(108, 83);
            this.numYieldAlarm.Maximum = new decimal(new int[] { 100, 0, 0, 0 });
            this.numYieldAlarm.Minimum = new decimal(new int[] { 0, 0, 0, 0 });
            this.numYieldAlarm.Name = "numYieldAlarm";
            this.numYieldAlarm.Size = new System.Drawing.Size(70, 23);
            this.numYieldAlarm.TabIndex = 3;
            this.numYieldAlarm.Value = new decimal(new int[] { 95, 0, 0, 0 });

            this.lblYieldAlarmUnit.AutoSize = true;
            this.lblYieldAlarmUnit.ForeColor = System.Drawing.Color.Gray;
            this.lblYieldAlarmUnit.Location = new System.Drawing.Point(184, 86);
            this.lblYieldAlarmUnit.Name = "lblYieldAlarmUnit";
            this.lblYieldAlarmUnit.Size = new System.Drawing.Size(260, 15);
            this.lblYieldAlarmUnit.TabIndex = 4;
            this.lblYieldAlarmUnit.Text = "%（低于此值良率转橙，低于一半转红；0 = 不告警）";

            // ---------- groupTheme ----------
            this.groupTheme.Controls.Add(this.cmbTheme);
            this.groupTheme.Controls.Add(this.lblTheme);
            this.groupTheme.ForeColor = System.Drawing.Color.DeepSkyBlue;
            this.groupTheme.Location = new System.Drawing.Point(12, 342);
            this.groupTheme.Name = "groupTheme";
            this.groupTheme.Size = new System.Drawing.Size(496, 64);
            this.groupTheme.TabIndex = 2;
            this.groupTheme.TabStop = false;
            this.groupTheme.Text = "界面主题";

            this.lblTheme.AutoSize = true;
            this.lblTheme.ForeColor = System.Drawing.Color.Silver;
            this.lblTheme.Location = new System.Drawing.Point(14, 28);
            this.lblTheme.Name = "lblTheme";
            this.lblTheme.Size = new System.Drawing.Size(67, 15);
            this.lblTheme.TabIndex = 0;
            this.lblTheme.Text = "主题模式：";

            this.cmbTheme.BackColor = System.Drawing.Color.FromArgb(30, 30, 38);
            this.cmbTheme.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbTheme.ForeColor = System.Drawing.Color.Gainsboro;
            this.cmbTheme.FormattingEnabled = true;
            this.cmbTheme.Items.AddRange(new object[] { "深色", "浅色" });
            this.cmbTheme.Location = new System.Drawing.Point(88, 25);
            this.cmbTheme.Name = "cmbTheme";
            this.cmbTheme.Size = new System.Drawing.Size(120, 23);
            this.cmbTheme.TabIndex = 1;

            // ---------- panelButtons ----------
            this.panelButtons.Controls.Add(this.btnCancel);
            this.panelButtons.Controls.Add(this.btnOk);
            this.panelButtons.Controls.Add(this.btnDefault);
            this.panelButtons.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.panelButtons.Location = new System.Drawing.Point(0, 410);
            this.panelButtons.Name = "panelButtons";
            this.panelButtons.Size = new System.Drawing.Size(520, 60);
            this.panelButtons.TabIndex = 3;

            this.btnDefault.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.btnDefault.Location = new System.Drawing.Point(12, 16);
            this.btnDefault.Name = "btnDefault";
            this.btnDefault.Size = new System.Drawing.Size(100, 30);
            this.btnDefault.TabIndex = 0;
            this.btnDefault.Text = "恢复默认";
            this.btnDefault.UseVisualStyleBackColor = true;
            this.btnDefault.Click += new System.EventHandler(this.OnRestoreDefault);

            this.btnOk.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnOk.Location = new System.Drawing.Point(310, 16);
            this.btnOk.Name = "btnOk";
            this.btnOk.Size = new System.Drawing.Size(90, 30);
            this.btnOk.TabIndex = 1;
            this.btnOk.Text = "保存";
            this.btnOk.UseVisualStyleBackColor = true;
            this.btnOk.Click += new System.EventHandler(this.OnOk);

            this.btnCancel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnCancel.Location = new System.Drawing.Point(408, 16);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(90, 30);
            this.btnCancel.TabIndex = 2;
            this.btnCancel.Text = "取消";
            this.btnCancel.UseVisualStyleBackColor = true;
            this.btnCancel.Click += new System.EventHandler(this.OnCancel);

            // ---------- SettingsForm ----------
            this.AcceptButton = this.btnOk;
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(38, 38, 46);
            this.CancelButton = this.btnCancel;
            this.ClientSize = new System.Drawing.Size(520, 470);
            this.Controls.Add(this.groupSave);
            this.Controls.Add(this.groupRun);
            this.Controls.Add(this.groupTheme);
            this.Controls.Add(this.panelButtons);
            this.ForeColor = System.Drawing.Color.Gainsboro;
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "SettingsForm";
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "系统设置";

            ((System.ComponentModel.ISupportInitialize)(this.numDays)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numYieldAlarm)).EndInit();
            this.groupSave.ResumeLayout(false);
            this.groupSave.PerformLayout();
            this.groupRun.ResumeLayout(false);
            this.groupRun.PerformLayout();
            this.groupTheme.ResumeLayout(false);
            this.groupTheme.PerformLayout();
            this.panelButtons.ResumeLayout(false);
            this.ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.GroupBox groupSave;
        private System.Windows.Forms.Label lblPath;
        private System.Windows.Forms.TextBox txtSavePath;
        private VisionPlatform.UI.UiButton btnBrowse;
        private System.Windows.Forms.Label lblFormat;
        private System.Windows.Forms.ComboBox cmbFormat;
        private System.Windows.Forms.Label lblDays;
        private System.Windows.Forms.NumericUpDown numDays;
        private System.Windows.Forms.Label lblDaysUnit;
        private System.Windows.Forms.CheckBox chkAutoSave;
        private System.Windows.Forms.CheckBox chkNgOnly;
        private VisionPlatform.UI.UiButton btnCleanup;
        private System.Windows.Forms.Label lblHint;

        private System.Windows.Forms.GroupBox groupRun;
        private System.Windows.Forms.CheckBox chkRequireComm;
        private System.Windows.Forms.Label lblCommState;
        private System.Windows.Forms.Label lblYieldAlarm;
        private System.Windows.Forms.NumericUpDown numYieldAlarm;
        private System.Windows.Forms.Label lblYieldAlarmUnit;

        private System.Windows.Forms.GroupBox groupTheme;
        private System.Windows.Forms.Label lblTheme;
        private System.Windows.Forms.ComboBox cmbTheme;

        private System.Windows.Forms.Panel panelButtons;
        private VisionPlatform.UI.UiButton btnDefault;
        private VisionPlatform.UI.UiButton btnOk;
        private VisionPlatform.UI.UiButton btnCancel;
    }
}
