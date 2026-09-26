namespace VisionPlatform.UI
{
    partial class SplashForm
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

            this.lblTitle = new System.Windows.Forms.Label();
            this.lblStep = new System.Windows.Forms.Label();
            this.progressBar = new System.Windows.Forms.ProgressBar();

            this.SuspendLayout();

            // ---------- lblTitle ----------
            this.lblTitle.Font = new System.Drawing.Font("Microsoft YaHei UI", 20F, System.Drawing.FontStyle.Bold);
            this.lblTitle.ForeColor = System.Drawing.Color.DeepSkyBlue;
            this.lblTitle.Location = new System.Drawing.Point(0, 30);
            this.lblTitle.Name = "lblTitle";
            this.lblTitle.Size = new System.Drawing.Size(520, 60);
            this.lblTitle.TabIndex = 0;
            this.lblTitle.Text = "VisionPlatform";
            this.lblTitle.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;

            // ---------- lblStep ----------
            this.lblStep.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);
            this.lblStep.ForeColor = System.Drawing.Color.Gainsboro;
            this.lblStep.Location = new System.Drawing.Point(0, 104);
            this.lblStep.Name = "lblStep";
            this.lblStep.Size = new System.Drawing.Size(520, 26);
            this.lblStep.TabIndex = 1;
            this.lblStep.Text = "正在初始化…";
            this.lblStep.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;

            // ---------- progressBar ----------
            this.progressBar.Location = new System.Drawing.Point(40, 140);
            this.progressBar.Name = "progressBar";
            this.progressBar.Size = new System.Drawing.Size(440, 24);
            this.progressBar.TabIndex = 2;

            // ---------- SplashForm ----------
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(38, 38, 46);
            this.ClientSize = new System.Drawing.Size(520, 200);
            this.Controls.Add(this.lblTitle);
            this.Controls.Add(this.lblStep);
            this.Controls.Add(this.progressBar);
            this.ForeColor = System.Drawing.Color.Gainsboro;
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            this.Name = "SplashForm";
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "启动中";

            this.ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.Label lblTitle;
        private System.Windows.Forms.Label lblStep;
        private System.Windows.Forms.ProgressBar progressBar;
    }
}
