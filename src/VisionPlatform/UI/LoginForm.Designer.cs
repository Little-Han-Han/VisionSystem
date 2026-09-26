namespace VisionPlatform.UI
{
    partial class LoginForm
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
            this.lblUser = new System.Windows.Forms.Label();
            this.txtUser = new System.Windows.Forms.TextBox();
            this.lblPwd = new System.Windows.Forms.Label();
            this.txtPwd = new System.Windows.Forms.TextBox();
            this.btnOk = new System.Windows.Forms.Button();
            this.btnCancel = new System.Windows.Forms.Button();
            this.lblHint = new System.Windows.Forms.Label();

            this.SuspendLayout();

            // ---------- lblTitle ----------
            this.lblTitle.Font = new System.Drawing.Font("Microsoft YaHei UI", 16F, System.Drawing.FontStyle.Bold);
            this.lblTitle.ForeColor = System.Drawing.Color.DeepSkyBlue;
            this.lblTitle.Location = new System.Drawing.Point(0, 20);
            this.lblTitle.Name = "lblTitle";
            this.lblTitle.Size = new System.Drawing.Size(380, 44);
            this.lblTitle.TabIndex = 0;
            this.lblTitle.Text = "VisionPlatform";
            this.lblTitle.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;

            // ---------- 用户名 ----------
            this.lblUser.ForeColor = System.Drawing.Color.Silver;
            this.lblUser.Location = new System.Drawing.Point(30, 84);
            this.lblUser.Name = "lblUser";
            this.lblUser.Size = new System.Drawing.Size(70, 24);
            this.lblUser.TabIndex = 1;
            this.lblUser.Text = "用户名";
            this.lblUser.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            this.txtUser.Font = new System.Drawing.Font("Microsoft YaHei UI", 10F);
            this.txtUser.Location = new System.Drawing.Point(106, 82);
            this.txtUser.Name = "txtUser";
            this.txtUser.Size = new System.Drawing.Size(238, 23);
            this.txtUser.TabIndex = 2;

            // ---------- 密码 ----------
            this.lblPwd.ForeColor = System.Drawing.Color.Silver;
            this.lblPwd.Location = new System.Drawing.Point(30, 118);
            this.lblPwd.Name = "lblPwd";
            this.lblPwd.Size = new System.Drawing.Size(70, 24);
            this.lblPwd.TabIndex = 3;
            this.lblPwd.Text = "密码";
            this.lblPwd.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            this.txtPwd.Font = new System.Drawing.Font("Microsoft YaHei UI", 10F);
            this.txtPwd.Location = new System.Drawing.Point(106, 116);
            this.txtPwd.Name = "txtPwd";
            this.txtPwd.PasswordChar = '●';
            this.txtPwd.Size = new System.Drawing.Size(238, 23);
            this.txtPwd.TabIndex = 4;

            // ---------- 提示 ----------
            this.lblHint.ForeColor = System.Drawing.Color.Orange;
            this.lblHint.Location = new System.Drawing.Point(30, 150);
            this.lblHint.Name = "lblHint";
            this.lblHint.Size = new System.Drawing.Size(320, 22);
            this.lblHint.TabIndex = 5;
            this.lblHint.Text = "演示账号：admin/123456、operator/111111";

            // ---------- 按钮 ----------
            this.btnOk.Location = new System.Drawing.Point(148, 186);
            this.btnOk.Name = "btnOk";
            this.btnOk.Size = new System.Drawing.Size(90, 30);
            this.btnOk.TabIndex = 6;
            this.btnOk.Text = "登录";
            this.btnOk.Click += new System.EventHandler(this.OnLogin);

            this.btnCancel.Location = new System.Drawing.Point(254, 186);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(90, 30);
            this.btnCancel.TabIndex = 7;
            this.btnCancel.Text = "取消";
            this.btnCancel.Click += new System.EventHandler(this.OnCancel);

            // ---------- LoginForm ----------
            this.AcceptButton = this.btnOk;
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(45, 45, 52);
            this.CancelButton = this.btnCancel;
            this.ClientSize = new System.Drawing.Size(380, 240);
            this.Controls.Add(this.lblTitle);
            this.Controls.Add(this.lblUser);
            this.Controls.Add(this.txtUser);
            this.Controls.Add(this.lblPwd);
            this.Controls.Add(this.txtPwd);
            this.Controls.Add(this.lblHint);
            this.Controls.Add(this.btnOk);
            this.Controls.Add(this.btnCancel);
            this.ForeColor = System.Drawing.Color.Gainsboro;
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "LoginForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "登录 — VisionPlatform";

            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private System.Windows.Forms.Label lblTitle;
        private System.Windows.Forms.Label lblUser;
        private System.Windows.Forms.TextBox txtUser;
        private System.Windows.Forms.Label lblPwd;
        private System.Windows.Forms.TextBox txtPwd;
        private System.Windows.Forms.Button btnOk;
        private System.Windows.Forms.Button btnCancel;
        private System.Windows.Forms.Label lblHint;
    }
}
