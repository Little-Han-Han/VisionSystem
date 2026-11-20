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

        // 版面常量（改这里就够了，ApplyLayout 只按它们摆位）
        private const int ThumbW = 88;      // 缩略图宽（比 TemplateStatusCard 的 104 略窄：
        private const int ThumbH = 66;      //   本卡片多一行按钮，横向要把位置让出来）
        private const int TextLeft = 104;   // 文字列起点 = 缩略图右边距 8 + 宽 88 + 8
        private const int TextRightPad = 8;
        private const int ButtonTop = 78;   // 按钮单独一行：文字在上、动作在下，互不遮挡
        private const int ButtonH = 26;
        private const int BtnManageW = 76;
        private const int BtnSmallW = 52;
        private const int BtnGap = 6;

        private readonly PictureBox _thumb;
        private readonly Label _lblState;
        private readonly Label _lblDetail;
        private readonly Label _lblPath;
        private readonly UiButton _btnManage;
        private readonly UiButton _btnRescan;
        private readonly UiButton _btnOpen;

        private readonly ToolTip _tip;

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
            _thumb.Location = new Point(8, 8);
            _thumb.Size = new Size(ThumbW, ThumbH);
            _thumb.SizeMode = PictureBoxSizeMode.Zoom;
            _thumb.TabStop = false;
            _thumb.BackColor = UiTheme.ColorViewBg;
            _thumb.BorderStyle = BorderStyle.None;
            Controls.Add(_thumb);

            _lblState = MakeLabel(UiTheme.FontBody, true);
            _lblState.Location = new Point(TextLeft, 6);
            _lblState.Size = new Size(150, 22);
            Controls.Add(_lblState);

            _lblDetail = MakeLabel(UiTheme.FontSmall, false);
            _lblDetail.Location = new Point(TextLeft, 28);
            _lblDetail.Size = new Size(150, 18);
            Controls.Add(_lblDetail);

            _lblPath = MakeLabel(UiTheme.FontSmall, false);
            _lblPath.Location = new Point(TextLeft, 46);
            _lblPath.Size = new Size(150, 30);
            Controls.Add(_lblPath);

            _btnManage = MakeButton("管理…", UiButtonKind.Neutral, BtnManageW);
            _btnManage.Click += (s, e) => Raise(ManageRequested);
            Controls.Add(_btnManage);

            _btnRescan = MakeButton("重扫", UiButtonKind.Neutral, BtnSmallW);
            _btnRescan.Click += (s, e) =>
            {
                BeginScan(force: true);
                Raise(RescanRequested);
            };
            Controls.Add(_btnRescan);

            _btnOpen = MakeButton("打开", UiButtonKind.Neutral, BtnSmallW);
            _btnOpen.Click += (s, e) => Raise(OpenFolderRequested);
            Controls.Add(_btnOpen);

            _tip = new ToolTip();
            _tip.AutoPopDelay = 15000;      // 路径/原因较长，读的时间要够
            _tip.InitialDelay = 400;

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

        /// <summary>三个动作按钮的尺寸（位置由 <see cref="ApplyLayout"/> 统一摆）。</summary>
        private static UiButton MakeButton(string text, UiButtonKind kind, int width)
        {
            var b = new UiButton();
            b.Text = text;
            b.Kind = kind;
            b.Size = new Size(width, ButtonH);
            b.Location = new Point(8, ButtonTop);
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

            // 路径以 .ocrtpl 结尾 → 单文件字符库；否则仍是"目录 = 库"的老口径
            bool isLib = dir.EndsWith(OcrCharLibraryFile.Extension, StringComparison.OrdinalIgnoreCase);
            Task.Run(() => isLib ? OcrCharStore.ScanLibrary(dir) : OcrCharStore.Scan(dir)).ContinueWith(t =>
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
                // 文案按"库还是目录"区分：v1.0.7.2 起界面只支持单文件库，
                // 对着 .ocrtpl 说"模板目录为空"会让人去找一个不存在的目录。
                bool isLibrary = lib.Dir != null
                    && lib.Dir.EndsWith(OcrCharLibraryFile.Extension, StringComparison.OrdinalIgnoreCase);

                SetState("未配置", UiTheme.ColorWarn,
                    isLibrary ? "字符库为空（0 个）" : "模板目录为空（0 个）",
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
            _lblPath.Text = ShortenPath(path);
            SetThumbnail(thumbnail);
            ApplyLayout();

            // 右栏只有 ~288px，绝对路径与失败原因必然被省略号截断；
            // 悬停给出完整内容，避免"看不全 = 不知道读的是哪个目录"。
            if (_tip != null)
            {
                _tip.SetToolTip(_lblState, state ?? "");
                _tip.SetToolTip(_lblDetail, detail ?? "");
                _tip.SetToolTip(_lblPath, path ?? "");
            }
        }

        /// <summary>
        /// 路径显示用"末两段"代替绝对路径（<c>…\VisionPlatform\OcrChars</c>）。
        /// <para>右栏宽度有限，"C:\Users\admin\AppData\Roaming" 这段前缀对"读的是哪个目录"毫无信息量，
        /// 却会把真正有信息量的目录名挤到省略号后面。完整路径走 tooltip。</para>
        /// </summary>
        private static string ShortenPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";

            string[] lines = path.Replace("\r", "").Split('\n');
            string first = lines.Length > 0 ? lines[0].TrimEnd() : "";
            if (first.Length == 0) return path;

            string[] parts = first.TrimEnd('\\', '/').Split('\\', '/');
            string head = parts.Length >= 2
                ? "…\\" + parts[parts.Length - 2] + "\\" + parts[parts.Length - 1]
                : first;

            // 第二行（失败示例）原样保留：它本身就是"文件名 — 原因"，不含前缀
            if (lines.Length <= 1) return head;
            return head + "\n" + lines[1];
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

        /// <summary>
        /// 按当前宽度摆放文字与按钮。
        /// <para>
        /// 【为什么必须判空 —— 这里曾是一枚"选中 OCR 节点就报错"的雷】
        /// <see cref="ApplyLayout"/> 由 <see cref="OnResize"/> 调用，而构造函数里
        /// <c>Height = CardHeight</c> 就会改变尺寸并**立刻**触发一次 <c>OnResize</c> ——
        /// 那一刻 <c>_btnManage</c> 等字段还没 new 出来，直接访问 <c>.Location</c> 就是
        /// <c>NullReferenceException</c>。它不在任何 try 里，所以表现为"一拖 OCR 模块/一选中 OCR 节点就弹错"。
        /// </para>
        /// <para>
        /// 【为什么按钮单独占一行】右栏宽度只有 ~288 px，缩略图占掉左侧 88 px 后，
        /// 若把三个按钮（合计 192 px）也放在第一行右侧，文字区会被完全盖住 ——
        /// 现场看到的就是"状态卡只有三个按钮、一个字的模板信息都没有"。
        /// </para>
        /// </summary>
        private void ApplyLayout()
        {
            // ① 构造期保护：字段还没建好（OnResize 先于字段赋值触发）
            if (_btnManage == null || _btnRescan == null || _btnOpen == null) return;
            if (_lblState == null || _lblDetail == null || _lblPath == null) return;

            // ② 已释放保护：切换节点时卡片会被 Dispose，父容器重排仍可能再触发一次 OnResize
            if (IsDisposed) return;

            int w = Width;

            // 文字列：从缩略图右边距一直铺到卡片右边缘（按钮不在这条带上，永远不会压住）
            int textW = Math.Max(60, w - TextLeft - TextRightPad);
            _lblState.Width = textW;
            _lblDetail.Width = textW;
            _lblPath.Width = textW;

            // 按钮行：从左往右依次排列，与缩略图/文字分属两行
            int x = 8;
            _btnManage.Location = new Point(x, ButtonTop);
            x += BtnManageW + BtnGap;
            _btnRescan.Location = new Point(x, ButtonTop);
            x += BtnSmallW + BtnGap;
            _btnOpen.Location = new Point(x, ButtonTop);
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
                if (_tip != null) _tip.Dispose();
                Image img = _thumb.Image;
                _thumb.Image = null;
                if (img != null) img.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
