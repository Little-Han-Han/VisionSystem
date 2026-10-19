using System;
using System.Drawing;
using System.Windows.Forms;
using VisionPlatform.Core;
using VisionPlatform.Models;

namespace VisionPlatform.UI
{
    /// <summary>登录窗体。</summary>
    public partial class LoginForm : Form
    {
        /// <summary>登录成功后得到的用户。</summary>
        public User User { get; private set; }

        public LoginForm()
        {
            InitializeComponent();

            txtUser.Text = "admin";
            txtUser.SelectAll();

            UiStyle.ApplyForm(this);

            // 自绘深色标题栏：本窗体是 FixedDialog，只给拖动热区、不给边缘拉伸。
            // 全绝对定位（无 Dock），Attach 内部会把客户区加高一个标题栏并把子控件整体下移避让。
            UiTitleBar.Attach(this);
        }

        private void OnLogin(object sender, EventArgs e)
        {
            User user = UserStore.Validate(txtUser.Text, txtPwd.Text);
            if (user == null)
            {
                lblHint.ForeColor = Color.OrangeRed;
                lblHint.Text = "用户名或密码错误";
                txtPwd.SelectAll();
                txtPwd.Focus();
                return;
            }

            User = user;
            Logger.Info("登录成功：" + user.Name);
            DialogResult = DialogResult.OK;
            Close();
        }

        private void OnCancel(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }
}
