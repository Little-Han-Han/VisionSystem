using System;
using System.Windows.Forms;
using VisionPlatform.Communication;
using VisionPlatform.Core;

namespace VisionPlatform.UI
{
    /// <summary>
    /// 系统设置：图片保存路径 / 保存格式 / 保留天数，以及循环运行的前置条件。
    /// 布局在 SettingsForm.Designer.cs 中，可在 VS 设计器里直接调整。
    /// 所有改动都落到全局配置的三段式模型中的「系统参数」(ConfigStore.Current.System)。
    /// </summary>
    public partial class SettingsForm : Form
    {
        public SettingsForm()
        {
            InitializeComponent();

            LoadFrom(ConfigStore.Current.System);
            lblCommState.Text = "当前：" + CommState.Describe();
        }

        // ---------- 界面 ↔ 设置 ----------

        private void LoadFrom(SystemSettings s)
        {
            if (s == null) s = SystemSettings.CreateDefault();

            txtSavePath.Text = s.ImageSavePath;
            SelectFormat(s.ImageFormat);
            numDays.Value = ClampDays(s.RetentionDays);
            chkAutoSave.Checked = s.AutoSaveEnabled;
            chkNgOnly.Checked = s.SaveNgOnly;
            chkRequireComm.Checked = s.RequireCommBeforeRun;
        }

        private SystemSettings CollectFromUi()
        {
            SystemSettings s = ConfigStore.Current.System.Clone();

            s.ImageSavePath = txtSavePath.Text.Trim();
            s.ImageFormat = (cmbFormat.SelectedItem as string) ?? "PNG";
            s.RetentionDays = (int)numDays.Value;
            s.AutoSaveEnabled = chkAutoSave.Checked;
            s.SaveNgOnly = chkNgOnly.Checked;
            s.RequireCommBeforeRun = chkRequireComm.Checked;

            return s;
        }

        /// <summary>把界面上的系统设置落地到全局配置并保存。</summary>
        private void ApplyToConfig()
        {
            ConfigStore.Current.System = CollectFromUi();
            ConfigStore.Save();
        }

        private static decimal ClampDays(int days)
        {
            if (days < 0) days = 0;
            if (days > 3650) days = 3650;
            return days;
        }

        private void SelectFormat(string format)
        {
            string want = (format ?? "PNG").Trim().ToUpperInvariant();
            if (want == "JPG") want = "JPEG";
            if (want == "TIF") want = "TIFF";

            for (int i = 0; i < cmbFormat.Items.Count; i++)
            {
                string item = cmbFormat.Items[i] as string;
                if (string.Equals(item, want, StringComparison.OrdinalIgnoreCase))
                {
                    cmbFormat.SelectedIndex = i;
                    return;
                }
            }

            if (cmbFormat.Items.Count > 0) cmbFormat.SelectedIndex = 0;
        }

        // ---------- 事件 ----------

        private void OnBrowse(object sender, EventArgs e)
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "选择图片保存目录";

                string current = txtSavePath.Text.Trim();
                if (!string.IsNullOrEmpty(current) && System.IO.Directory.Exists(current))
                    dlg.SelectedPath = current;

                if (dlg.ShowDialog(this) == DialogResult.OK)
                    txtSavePath.Text = dlg.SelectedPath;
            }
        }

        private void OnCleanupNow(object sender, EventArgs e)
        {
            // 清理按界面上当前的天数执行，因此先落地一次设置
            ApplyToConfig();

            int deleted = ImageStore.Cleanup();
            MessageBox.Show(this,
                deleted > 0 ? ("已清理 " + deleted + " 个过期图片。") : "没有需要清理的图片。",
                "清理完成",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void OnOk(object sender, EventArgs e)
        {
            string path = txtSavePath.Text.Trim();
            if (string.IsNullOrWhiteSpace(path))
            {
                MessageBox.Show(this, "请指定图片保存路径。", "系统设置",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                if (!System.IO.Directory.Exists(path))
                    System.IO.Directory.CreateDirectory(path);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "保存路径不可用：" + ex.Message, "系统设置",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            ApplyToConfig();

            DialogResult = DialogResult.OK;
            Close();
        }

        private void OnCancel(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        private void OnRestoreDefault(object sender, EventArgs e)
        {
            LoadFrom(SystemSettings.CreateDefault());
        }
    }
}
