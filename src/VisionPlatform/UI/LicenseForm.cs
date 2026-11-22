using System;
using System.Drawing;
using System.Windows.Forms;
using VisionPlatform.Core;
using VisionPlatform.Core.Licensing;

namespace VisionPlatform.UI
{
    /// <summary>
    /// 授权状态窗口：显示机器码 / 当前许可信息，并提供「导入许可证」。
    /// <para>
    /// 【两种进入方式】
    /// ① 启动校验失败时由 <c>Program.Main</c> 弹出（关闭窗口 = 退出软件）；
    /// ② 已授权用户从「系统设置」里查看（关闭窗口 = 返回主界面）。
    /// </para>
    /// <para>
    /// 【界面用代码构建】本项目铁律：能不用 Designer 就不用 —— VS 打开就重写
    /// <c>*.Designer.cs</c>，会把手工加的 <c>+=</c> 订阅和控件属性冲掉。
    /// </para>
    /// </summary>
    public sealed class LicenseForm : Form
    {
        private readonly LicenseInfo _info;
        private readonly string _detail;

        private TextBox txtMachineCode;
        private Label lblState;
        private Label lblDetail;
        private Label lblLicenseInfo;
        private UiButton btnImport;
        private UiButton btnCopy;
        private UiButton btnClose;

        /// <summary>窗口关闭时是否已经成功导入并校验通过（调用方可据此直接继续启动）。</summary>
        public bool Imported { get; private set; }

        public LicenseForm(LicenseStatus status, LicenseInfo info, string detail)
        {
            _info = info;
            _detail = detail ?? "";

            Text = "软件授权";
            ClientSize = new Size(660, 430);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;

            BuildUi();
            UiStyle.ApplyForm(this);
            UiTitleBar.Attach(this);
            RebindState(status);
        }

        private void BuildUi()
        {
            int pad = 18;

            // ---- 状态标题 ----
            lblState = new Label
            {
                Location = new Point(pad, 14),
                Size = new Size(ClientSize.Width - pad * 2, 30),
                Font = new Font(UiTheme.FamilyBody, 14f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            };
            Controls.Add(lblState);

            // ---- 状态说明 ----
            lblDetail = new Label
            {
                Location = new Point(pad, 46),
                Size = new Size(ClientSize.Width - pad * 2, 40),
                ForeColor = UiTheme.ColorTextSecondary
            };
            Controls.Add(lblDetail);

            // ---- 机器码 ----
            var lblMc = new Label
            {
                Location = new Point(pad, 92),
                Size = new Size(80, 24),
                Text = "本机机器码",
                TextAlign = ContentAlignment.MiddleLeft
            };
            Controls.Add(lblMc);

            txtMachineCode = new TextBox
            {
                Location = new Point(pad + 84, 92),
                Size = new Size(ClientSize.Width - pad * 2 - 84 - 84, 26),
                ReadOnly = true,
                Text = MachineFingerprint.GetMachineCode(),
                TextAlign = HorizontalAlignment.Center,
                Font = new Font(UiTheme.FamilyMono, 11f, FontStyle.Bold)
            };
            Controls.Add(txtMachineCode);

            btnCopy = new UiButton
            {
                Location = new Point(ClientSize.Width - pad - 78, 92),
                Size = new Size(78, 26),
                Text = "复制",
                Kind = UiButtonKind.Neutral
            };
            btnCopy.Click += OnCopyMachineCode;
            Controls.Add(btnCopy);

            if (MachineFingerprint.IsDegraded())
            {
                var lblDeg = new Label
                {
                    Location = new Point(pad + 84, 120),
                    Size = new Size(ClientSize.Width - pad * 2 - 84, 20),
                    Text = "（部分硬件信息读取不到，机器指纹已降级 —— 请把此机器码发给供应商）",
                    ForeColor = UiTheme.ColorWarn
                };
                Controls.Add(lblDeg);
            }

            // ---- 许可信息 ----
            lblLicenseInfo = new Label
            {
                Location = new Point(pad, 148),
                Size = new Size(ClientSize.Width - pad * 2, 120),
                ForeColor = UiTheme.ColorTextPrimary
            };
            Controls.Add(lblLicenseInfo);

            // ---- 按钮 ----
            btnImport = new UiButton
            {
                Location = new Point(pad, ClientSize.Height - 52),
                Size = new Size(140, 34),
                Text = "导入许可证…",
                Kind = UiButtonKind.Grab
            };
            btnImport.Click += OnImport;
            Controls.Add(btnImport);

            btnClose = new UiButton
            {
                Location = new Point(ClientSize.Width - pad - 100, ClientSize.Height - 52),
                Size = new Size(100, 34),
                Text = "关闭",
                Kind = UiButtonKind.Neutral
            };
            btnClose.Click += delegate { Close(); };
            Controls.Add(btnClose);
        }

        /// <summary>按当前状态刷新文案与配色。</summary>
        private void RebindState(LicenseStatus status)
        {
            lblState.Text = "  " + status.Describe();
            lblState.ForeColor = status == LicenseStatus.Valid ? UiTheme.ColorOk
                               : status == LicenseStatus.Trial ? UiTheme.ColorWarn
                               : UiTheme.ColorNg;

            string detail = _detail;
            if (string.IsNullOrEmpty(detail))
            {
                switch (status)
                {
                    case LicenseStatus.Valid:
                        detail = "授权正常，可以正常使用本软件。";
                        break;
                    case LicenseStatus.Missing:
                        detail = "未在 License\\ 目录找到许可证（license.lic）。"
                               + "请把供应商发来的许可证文件放到该目录，或点下方「导入许可证…」。";
                        break;
                    case LicenseStatus.Trial:
                        detail = "当前处于试用期。导入正式许可证后不再受试用期限制。";
                        break;
                    default:
                        detail = "授权不可用，请导入有效的许可证文件（.lic）。";
                        break;
                }
            }
            lblDetail.Text = detail;

            if (_info != null)
            {
                lblLicenseInfo.Text =
                    "许可证编号：" + (string.IsNullOrEmpty(_info.Number) ? "—" : _info.Number) + Environment.NewLine
                  + "客户：" + (string.IsNullOrEmpty(_info.Customer) ? "—" : _info.Customer) + Environment.NewLine
                  + "版本：" + (string.IsNullOrEmpty(_info.Edition) ? "—" : _info.Edition) + Environment.NewLine
                  + "有效期：" + (_info.IsPerpetual
                        ? "永久"
                        : _info.ExpireTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm")) + Environment.NewLine
                  + "绑定机器码：" + (string.IsNullOrEmpty(_info.MachineCode) ? "—" : _info.MachineCode);
            }
            else
            {
                lblLicenseInfo.Text = "许可证文件位置：" + Environment.NewLine
                                    + "    " + LicenseStore.GetFilePath(AppPaths.Root);
            }
        }

        private void OnCopyMachineCode(object sender, EventArgs e)
        {
            try
            {
                Clipboard.SetText(txtMachineCode.Text);
                btnCopy.Text = "已复制";
            }
            catch (Exception)
            {
                // 剪贴板被其它进程占用时忽略
            }
        }

        private void OnImport(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog
            {
                Title = "选择许可证文件",
                Filter = "许可证文件 (*.lic)|*.lic|所有文件 (*.*)|*.*",
                CheckFileExists = true
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                LicenseInfo imported;
                string error;
                if (!LicenseStore.ImportFrom(AppPaths.Root, dlg.FileName, out imported, out error))
                {
                    MessageBox.Show(this, "许可证导入失败：" + Environment.NewLine + Environment.NewLine
                                        + (string.IsNullOrEmpty(error) ? "未知原因" : error),
                        "导入失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // 导入后立刻重新校验，成功则允许继续启动（不必强迫用户重启）
                LicenseInfo current;
                LicenseStatus after = LicenseChecker.Check(AppPaths.Root, out current);

                if (after.IsUsable())
                {
                    Imported = true;
                    MessageBox.Show(this, "许可证导入成功。" + Environment.NewLine + Environment.NewLine
                                        + current.Summary(),
                        "导入成功", MessageBoxButtons.OK, MessageBoxIcon.Information);

                    DialogResult = DialogResult.OK;
                    Close();
                    return;
                }

                // 文件能导入（签名/机器码 OK）但仍不可用：多半是已过期
                MessageBox.Show(this, "许可证已导入，但当前状态为「" + after.Describe() + "」。" + Environment.NewLine
                                    + "请联系供应商确认有效期。",
                    "授权仍不可用", MessageBoxButtons.OK, MessageBoxIcon.Warning);

                RebindState(after);
            }
        }
    }
}
