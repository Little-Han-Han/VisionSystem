using System;
using System.IO;
using System.Windows.Forms;
using VisionPlatform.Communication;
using VisionPlatform.Core;
using VisionPlatform.Data;

namespace VisionPlatform.UI
{
    /// <summary>
    /// 系统设置：图片保存路径 / 保存格式 / 保留天数，以及循环运行的前置条件。
    /// 布局在 SettingsForm.Designer.cs 中，可在 VS 设计器里直接调整。
    /// 所有改动都落到全局配置的三段式模型中的「系统参数」(ConfigStore.Current.System)。
    /// </summary>
    public partial class SettingsForm : Form
    {
        /// <summary>打开本窗体时的主题模式：点「取消」/ 直接关窗时用它还原（预览不该变永久）。</summary>
        private readonly UiThemeMode _themeOnOpen;

        public SettingsForm()
        {
            InitializeComponent();

            // 主题下拉的事件订阅刻意写在构造函数里：
            // 【项目铁律】VS 重序列化 Designer 会悄悄丢掉手写的 `xxx.Event += handler`，
            // 本项目因此出过一次严重 bug，故 InitializeComponent 里不放动态代码。
            cmbTheme.SelectedIndexChanged += OnThemeChanged;
            btnBrowseDb.Click += OnBrowseDb;
            btnCleanupLog.Click += OnCleanupLog;

            LoadFrom(ConfigStore.Current.System);
            _themeOnOpen = UiTheme.Mode;
            lblCommState.Text = "当前：" + CommState.Describe();

            UiStyle.ApplyForm(this);

            // 自绘深色标题栏：本窗体是 FixedDialog，只给拖动热区、不给边缘拉伸。
            // 三个 GroupBox 是绝对定位，Attach 内部会自动把客户区加高并整体下移避让。
            UiTitleBar.Attach(this);

            // 语义色：确定=绿，清理历史（删除文件）=红；其余中性
            btnOk.Kind = UiButtonKind.Start;
            btnCleanup.Kind = UiButtonKind.Stop;
            btnCleanupLog.Kind = UiButtonKind.Stop;
        }

        // ---------- 界面 ↔ 设置 ----------

        private void LoadFrom(SystemSettings s)
        {
            if (s == null) s = SystemSettings.CreateDefault();

            // 主题属于 UiSettings（不是 SystemSettings），但同一个窗体里一并回填：
            // 选中项决定「保存」时写入哪个模式，缺失即深色。
            UiSettings ui = ConfigStore.Current.Ui;
            SelectTheme(ui != null ? ui.Theme : null);

            txtSavePath.Text = s.ImageSavePath;
            SelectFormat(s.ImageFormat);
            numDays.Value = ClampDays(s.RetentionDays);
            chkAutoSave.Checked = s.AutoSaveEnabled;
            chkNgOnly.Checked = s.SaveNgOnly;
            chkRequireComm.Checked = s.RequireCommBeforeRun;
            numYieldAlarm.Value = ClampPercent(s.YieldAlarmPercent);

            // 日志数据库：路径为空表示使用默认（AppPaths.LogDbFile）；保留天数 0 = 不清理
            txtDbPath.Text = s.DbPath ?? "";
            numLogDays.Value = ClampDays(s.LogRetentionDays);
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
            s.YieldAlarmPercent = (double)numYieldAlarm.Value;

            s.DbPath = txtDbPath.Text.Trim();
            s.LogRetentionDays = (int)numLogDays.Value;

            return s;
        }

        /// <summary>把界面上的系统设置落地到全局配置并保存。</summary>
        private void ApplyToConfig()
        {
            ConfigStore.Current.System = CollectFromUi();
            ConfigStore.Save();
        }

        /// <summary>把配置里的良率告警下限夹到 [0,100]，缺失或非法时取默认 95。</summary>
        private static decimal ClampPercent(double percent)
        {
            if (double.IsNaN(percent) || double.IsInfinity(percent) || percent <= 0.0)
                percent = SystemSettings.DefaultYieldAlarmPercent;
            if (percent > 100.0) percent = 100.0;
            return (decimal)percent;
        }

        private static decimal ClampDays(int days)
        {
            if (days < 0) days = 0;
            if (days > 3650) days = 3650;
            return days;
        }

        /// <summary>按配置里的主题模式回填下拉框（"Light" → 浅色，其余 → 深色）。</summary>
        private void SelectTheme(string theme)
        {
            if (cmbTheme == null) return;

            int index = (UiThemeManager.ParseMode(theme) == UiThemeMode.Light) ? 1 : 0;
            if (index >= cmbTheme.Items.Count) index = 0;

            cmbTheme.SelectedIndex = index;
        }

        /// <summary>下拉框当前选中的主题模式（0=深色，1=浅色）。</summary>
        private UiThemeMode SelectedThemeMode
        {
            get { return (cmbTheme != null && cmbTheme.SelectedIndex == 1) ? UiThemeMode.Light : UiThemeMode.Dark; }
        }

        /// <summary>
        /// 切换主题下拉框：立即预览（换色板 + 重套所有已打开窗体），<strong>不落盘</strong>。
        /// 用户点「取消」时不会留下痕迹 —— 但这只改内存，真正的持久化在 <see cref="OnOk"/> 里。
        /// </summary>
        private void OnThemeChanged(object sender, EventArgs e)
        {
            UiThemeManager.Apply(SelectedThemeMode);
        }

        /// <summary>切换主题后由 <see cref="UiThemeManager"/> 回调：重套本窗体（含新加的主题分组）。</summary>
        public void RefreshTheme()
        {
            UiStyle.ApplyTree(this);
            Invalidate(true);
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

        /// <summary>选择日志数据库文件路径（SQLite）。留空表示使用默认路径（AppPaths.LogDbFile）。
        /// 注意：改变路径需重启软件后才对新库生效——启动时会按新路径重新打开数据库。</summary>
        private void OnBrowseDb(object sender, EventArgs e)
        {
            using (var dlg = new SaveFileDialog())
            {
                dlg.Filter = "SQLite 数据库 (*.db)|*.db|所有文件 (*.*)|*.*";
                dlg.Title = "选择日志数据库文件路径";
                dlg.AddExtension = true;
                dlg.DefaultExt = "db";
                dlg.CheckFileExists = false;
                dlg.OverwritePrompt = false;

                string current = txtDbPath.Text.Trim();
                if (!string.IsNullOrEmpty(current))
                {
                    try
                    {
                        string dir = Path.GetDirectoryName(current);
                        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                            dlg.InitialDirectory = dir;
                        dlg.FileName = Path.GetFileName(current);
                    }
                    catch { }
                }
                else
                {
                    dlg.InitialDirectory = AppPaths.LogDir;
                }

                if (dlg.ShowDialog(this) == DialogResult.OK)
                    txtDbPath.Text = dlg.FileName;
            }
        }

        /// <summary>按界面上当前的保留天数，立即清理过期日志（不改动界面其它设置）。</summary>
        private void OnCleanupLog(object sender, EventArgs e)
        {
            ApplyToConfig();
            int days = ConfigStore.Current.System.LogRetentionDays;
            if (days <= 0)
            {
                MessageBox.Show(this, "日志保留天数为 0（不清理），无需清理。", "日志清理",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            LogDb.CleanupRetention(days);
            MessageBox.Show(this,
                "已按保留天数（" + days + " 天）清理过期日志。",
                "清理完成",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
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

            // 主题最后落地：先重套界面（含所有已打开窗体），再保存 ——
            // ConfigStore.Save() 会广播 Changed，放最后可保证只播一次且界面已是新色。
            UiThemeManager.SaveAndApply(SelectedThemeMode);

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

        /// <summary>
        /// 关窗收尾：主题只在点「保存」时才算数。取消 / ESC / 直接关窗都要还原成
        /// 打开时的模式，否则"下拉框里随便点一下浅色"就永久改掉了界面。
        /// </summary>
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (DialogResult != DialogResult.OK && UiTheme.Mode != _themeOnOpen)
                UiThemeManager.Apply(_themeOnOpen);

            base.OnFormClosing(e);
        }
    }
}
