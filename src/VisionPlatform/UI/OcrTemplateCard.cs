using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using VisionPlatform.Flow;

namespace VisionPlatform.UI
{
    /// <summary>
    /// 参数面板顶部的「OCR 字符模板库」状态卡片（挂在 <see cref="NodeParamPanel.SetBanner"/> 的插槽上）。
    /// <para>
    /// 与 <see cref="TemplateStatusCard"/> <strong>视觉常量对齐、但不复用同一个类</strong>：
    /// 两者的数据源完全不同（一个是 <c>TemplateStore</c> 的单个模板资产，一个是整目录扫描结果），
    /// 硬塞进一个类会变成"两套状态机的并集"，改一处必然影响另一处。
    /// </para>
    /// <para>
    /// 【为什么扫描放后台】一个目录可能有上百个文件，逐个 <c>new Bitmap</c> 读尺寸在 UI 线程上会明显卡顿。
    /// 扫描在 <c>Task</c> 里跑，结果回投时用序号丢弃过期结果（连点两次"重扫"不会出现旧结果覆盖新结果）。
    /// </para>
    /// </summary>
    public sealed class OcrTemplateCard : Panel
    {
        /// <summary>卡片高度（与 <see cref="TemplateStatusCard.CardHeight"/> 对齐的视觉常量）。</summary>
        public const int CardHeight = 108;

        private const int ThumbW = 104;
        private const int ThumbH = 78;

        private readonly PictureBox _thumb;
        private readonly Label _lblState;
        private readonly Label _lblDetail;
        private readonly Label _lblPath;
        private readonly UiButton _btnManage;
        private readonly UiButton _btnRescan;
        private readonly UiButton _btnOpen;

        private string _dir = "";
        private int _scanToken;
        private bool _scanning;

        /// <summary>点「管理模板库…」。</summary>
        public event EventHandler ManageRequested;

        /// <summary>点「重新扫描」。</summary>
        public event EventHandler RescanRequested;

        /// <summary>点「打开目录」（宿主决定怎么打开，卡片不直接调 explorer）。</summary>
        public event EventHandler OpenFolderRequested;

        /// <summary>创建卡片。</summary>
        public OcrTemplateCard()
        {
            Dock = DockStyle.Top;
            Height = CardHeight;
            MinimumSize = new Size(120, CardHeight);
            Padding = new Padding(0);

            _thumb = new PictureBox();
            _thumb.Location = new Point(8, 10);
            _thumb.Size = new Size(ThumbW, ThumbH);
            _thumb.SizeMode = PictureBoxSizeMode.Zoom;
            _thumb.TabStop = false;
            _thumb.BackColor = UiTheme.ColorViewBg;
            _thumb.BorderStyle = BorderStyle.None;
            Controls.Add(_thumb);

            _lblState = MakeLabel(UiTheme.FontBody, true);
            _lblState.Location = new Point(120, 8);
            _lblState.Size = new Size(150, 22);
            Controls.Add(_lblState);

            _lblDetail = MakeLabel(UiTheme.FontBadgeSmall, false);
            _lblDetail.Location = new Point(120, 32);
            _lblDetail.Size = new Size(220, 20);
            Controls.Add(_lblDetail);

            _lblPath = MakeLabel(UiTheme.FontBadgeSmall, false);
            _lblPath.Location = new Point(120, 52);
            _lblPath.Size = new Size(220, 46);
            Controls.Add(_lblPath);

            _btnManage = MakeButton("管理…", UiButtonKind.Neutral);
            _btnManage.Click += (s, e) => Raise(ManageRequested);
            Controls.Add(_btnManage);

            _btnRescan = MakeButton("重扫", UiButtonKind.Neutral);
            _btnRescan.Click += (s, e) =>
            {
                BeginScan(force: true);
                Raise(RescanRequested);
            };
            Controls.Add(_btnRescan);

            _btnOpen = MakeButton("打开", UiButtonKind.Neutral);
            _btnOpen.Click += (s, e) => Raise(OpenFolderRequested);
            Controls.Add(_btnOpen);

            UiTheme.Changed += OnThemeChanged;
            ApplyColors();
            SetState("未扫描", UiTheme.ColorTextSecondary, "—", "—", null);
        }

        private static Label MakeLabel(Font font, bool bold)
        {
            var l = new Label();
            l.AutoSize = false;
            l.Font = bold ? new Font(font.FontFamily, font.Size, FontStyle.Bold) : font;
            l.TextAlign = ContentAlignment.MiddleLeft;
            l.BackColor = Color.Transparent;
            l.ForeColor = UiTheme.ColorTextSecondary;
            l.AutoEllipsis = true;
            return l;
        }

        private static UiButton MakeButton(string text, UiButtonKind kind)
        {
            var b = new UiButton();
            b.Text = text;
            b.Kind = kind;
            b.Size = new Size(52, 26);
            b.Location = new Point(212, 8);
            return b;
        }

        /// <summary>绑定目录并立即扫描（首次挂载用）。</summary>
        public void Bind(string dir)
        {
            bool changed = !string.Equals(_dir, dir, StringComparison.OrdinalIgnoreCase);
            _dir = dir ?? "";
            ApplyColors();
            if (changed) BeginScan(false);
        }

        /// <summary>强制重扫（目录被外部改动后调用）。</summary>
        public void Rescan()
        {
            BeginScan(true);
        }

        // =====================================================================
        //  扫描（后台线程）
        // =====================================================================

        private void BeginScan(bool force)
        {
            if (_scanning && !force) return;
            _scanning = true;
            _btnRescan.Enabled = false;
            SetState("扫描中…", UiTheme.ColorTextSecondary, "正在统计字符模板…", _dir, null);

            string dir = _dir;
            int token = ++_scanToken;

            Task.Run(() => OcrCharStore.Scan(dir)).ContinueWith(t =>
            {
                if (IsDisposed) return;
                try
                {
                    BeginInvoke(new Action(() => OnScanCompleted(t, token)));
                }
                catch (Exception)
                {
                    // 窗体已销毁：结果丢弃
                }
            });
        }

        private void OnScanCompleted(Task<OcrCharLibrary> task, int token)
        {
            // 过期结果直接丢弃（连点"重扫"时只认最后一次）
            if (token != _scanToken) return;

            _scanning = false;
            _btnRescan.Enabled = true;

            if (task.IsFaulted || task.Result == null)
            {
                Exception ex = task.Exception != null ? task.Exception.GetBaseException() : null;
                SetState("扫描失败", UiTheme.ColorNg, ex != null ? ex.Message : "未知原因", _dir, null);
                return;
            }

            OcrCharLibrary lib = task.Result;

            // 文案四态：空库 / 全失败 / 部分失败 / 正常。
            // ★ 全失败时严禁出现"为空"或"0 个"—— 那是"读不出来"被说成"没有"，会让人去查错方向。
            if (lib.IsEmpty)
            {
                SetState("未配置", UiTheme.ColorWarn,
                    "模板目录为空（0 个）",
                    lib.Dir,
                    null);
                return;
            }

            if (lib.AllFailed)
            {
                SetState("全部读取失败", UiTheme.ColorNg,
                    lib.FileCount + " 个文件均读取失败",
                    lib.Dir + "\n" + FirstFailure(lib),
                    null);
                return;
            }

            string detail = string.Format(CultureInfo.InvariantCulture,
                "{0} 个字符 · {1} 张样本", lib.LabelCount, lib.SampleCount);
            if (lib.Failures.Count > 0)
                detail += "（" + lib.Failures.Count + " 个读取失败）";

            Bitmap thumb = null;
            if (lib.Entries.Count > 0)
            {
                string path = lib.Entries[0].PrimaryPath;
                if (!string.IsNullOrEmpty(path))
                {
                    try { thumb = OcrCharStore.LoadThumbnail(path); }
                    catch (Exception) { thumb = null; }
                }
            }

            SetState("已就绪", lib.Failures.Count > 0 ? UiTheme.ColorWarn : UiTheme.ColorOk,
                detail, lib.Dir + "\n" + (lib.Failures.Count > 0 ? FirstFailure(lib) : "改动会立即对下次运行生效"),
                thumb);
        }

        private static string FirstFailure(OcrCharLibrary lib)
        {
            if (lib == null || lib.Failures.Count == 0) return "";
            OcrCharFailure f = lib.Failures[0];
            return "例：" + f.FileName + " — " + f.Reason;
        }

        // =====================================================================
        //  显示
        // =====================================================================

        private void SetState(string state, Color color, string detail, string path, Bitmap thumbnail)
        {
            _lblState.Text = state;
            _lblState.ForeColor = color;
            _lblDetail.Text = detail;
            _lblPath.Text = path;
            SetThumbnail(thumbnail);
            ApplyLayout();
        }

        /// <summary>缩略图：24bpp 落地、旧图先 Dispose（照抄 TemplateStatusCard 的三个坑处理）。</summary>
        private void SetThumbnail(Bitmap source)
        {
            Image old = _thumb.Image;
            _thumb.Image = null;
            if (old != null) old.Dispose();

            if (source == null) return;

            try
            {
                int w = Math.Max(1, Math.Min(ThumbW, source.Width));
                int h = Math.Max(1, Math.Min(ThumbH, source.Height));
                var dst = new Bitmap(source.Width <= ThumbW ? source.Width : w,
                                     source.Height <= ThumbH ? source.Height : h,
                                     System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                using (var g = Graphics.FromImage(dst))
                    g.DrawImage(source, new Rectangle(0, 0, dst.Width, dst.Height));
                _thumb.Image = dst;
            }
            catch (Exception)
            {
                _thumb.Image = null;
            }
            finally
            {
                source.Dispose();
            }
        }

        private void ApplyLayout()
        {
            int w = Width;
            int right = Math.Max(120, w - 168);
            _btnManage.Location = new Point(right, 8);
            _btnRescan.Location = new Point(right + 56, 8);
            _btnOpen.Location = new Point(right + 112, 8);

            _lblDetail.Width = Math.Max(60, w - 132);
            _lblPath.Width = Math.Max(60, w - 132);
            _lblState.Width = Math.Max(60, right - 124);
        }

        private static void Raise(EventHandler h)
        {
            if (h != null) h(null, EventArgs.Empty);
        }

        private void OnThemeChanged(object sender, EventArgs e)
        {
            ApplyColors();
            Invalidate(true);
        }

        private void ApplyColors()
        {
            BackColor = UiTheme.ColorCardBg;
            ForeColor = UiTheme.ColorTextPrimary;
            _thumb.BackColor = UiTheme.ColorViewBg;
            _lblDetail.ForeColor = UiTheme.ColorTextSecondary;
            _lblPath.ForeColor = UiTheme.ColorTextSecondary;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ApplyLayout();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                UiTheme.Changed -= OnThemeChanged;
                Image img = _thumb.Image;
                _thumb.Image = null;
                if (img != null) img.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
