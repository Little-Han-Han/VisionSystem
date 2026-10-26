using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using VisionPlatform.Flow;
using VisionSystem.Controls;
using VisionSystem.Controls.Geometry;

namespace VisionPlatform.UI
{
    /// <summary>
    /// OCR 字符模板库窗体：查看目录内容 / 框选入库 / 删除 / 清空 / 迁移。
    /// <para>
    /// 【唯一真源是节点参数】本窗体改目录时<strong>立即抛 <see cref="DirChanged"/></strong>，
    /// 由宿主（<c>FlowForm</c>）写回节点参数并 <c>SaveFlows()</c>；目录不可写时本窗体会把输入框
    /// 恢复成原值并说明原因 —— 否则就是"在库里选好目录、加了一批字符，回主界面一跑还是读老目录"。
    /// </para>
    /// <para>
    /// 【禁止直接 File.Copy / File.Delete】所有磁盘写操作一律走 <see cref="OcrCharStore"/>，
    /// 这样"迁移只读源目录"这条硬约束才守得住（在窗体里散着写，漏一处就破功）。
    /// </para>
    /// <para>单文件手写 <c>InitializeComponent</c>，不建 Designer —— 与 <c>TemplateForm</c> 同一理由。</para>
    /// </summary>
    public sealed class OcrTemplateForm : Form
    {
        private readonly string _initialDir;

        private TextBox txtDir;
        private UiButton btnBrowseDir;
        private UiButton btnOpenDir;
        private UiButton btnRescan;

        private ListView listChars;
        private Label lblFailures;
        private PictureBox picPreview;
        private Label lblPreviewInfo;

        private ImageViewer viewer;
        private UiButton btnLoadImage;
        private UiButton btnDrawRoi;

        private TextBox txtLabel;
        private Label lblLabelBytes;
        private RadioButton rdoAppend;
        private RadioButton rdoReplace;
        private UiButton btnAdd;
        private UiButton btnDelete;
        private UiButton btnClearAll;
        private UiButton btnMigrate;
        private Label lblStatus;

        private OcrCharLibrary _lib = new OcrCharLibrary();
        private ImageList _thumbs;
        private Bitmap _sourceFrame;
        private int _scanToken;
        private bool _loading;

        /// <summary>目录被用户改动（宿主负责写回节点参数 + SaveFlows）。</summary>
        public event EventHandler DirChanged;

        /// <summary>当前目录（绝对路径或用户填的相对值，见 <see cref="ResolvedDir"/>）。</summary>
        public string CurrentDir { get { return txtDir != null ? txtDir.Text : ""; } }

        /// <summary>解析后的绝对路径。</summary>
        public string ResolvedDir { get { return ResolveDir(CurrentDir); } }

        /// <summary>创建窗体。</summary>
        /// <param name="dir">节点当前的 <c>TemplateDir</c> 参数值。</param>
        public OcrTemplateForm(string dir)
        {
            _initialDir = dir ?? "";

            InitializeComponent();

            txtDir.Text = _initialDir;
            UiStyle.ApplyForm(this);

            // ApplyForm 之后再做显式配色（面板与状态文字）
            lblStatus.ForeColor = UiTheme.ColorTextSecondary;
            lblFailures.ForeColor = UiTheme.ColorTextSecondary;

            ScanAsync();
        }

        // =====================================================================
        //  界面
        // =====================================================================

        private void InitializeComponent()
        {
            SuspendLayout();

            Text = "字符模板库";
            ClientSize = new Size(1080, 720);
            MinimumSize = new Size(900, 600);
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.Sizable;
            KeyPreview = true;

            // ---------------- 顶条：目录 ----------------
            var top = new FlowLayoutPanel();
            top.Dock = DockStyle.Top;
            top.Height = 44;
            top.FlowDirection = FlowDirection.LeftToRight;
            top.WrapContents = false;
            top.Padding = new Padding(8, 8, 8, 8);

            var lblDir = new Label();
            lblDir.Text = "模板目录";
            lblDir.AutoSize = false;
            lblDir.Size = new Size(64, 26);
            lblDir.TextAlign = ContentAlignment.MiddleLeft;
            lblDir.Margin = new Padding(0, 2, 4, 0);

            txtDir = new TextBox();
            txtDir.Size = new Size(560, 26);
            txtDir.Margin = new Padding(0, 2, 8, 0);
            txtDir.TextChanged += OnDirTextChanged;

            btnBrowseDir = MakeButton("浏览…", 76);
            btnBrowseDir.Click += OnBrowseDir;
            btnOpenDir = MakeButton("打开", 60);
            btnOpenDir.Click += OnOpenDir;
            btnRescan = MakeButton("重扫", 60);
            btnRescan.Click += (s, e) => ScanAsync();

            top.Controls.Add(lblDir);
            top.Controls.Add(txtDir);
            top.Controls.Add(btnBrowseDir);
            top.Controls.Add(btnOpenDir);
            top.Controls.Add(btnRescan);

            // ---------------- 底部：入库区 ----------------
            var bottom = new FlowLayoutPanel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = 96;
            bottom.FlowDirection = FlowDirection.LeftToRight;
            bottom.WrapContents = false;
            bottom.Padding = new Padding(8, 4, 8, 4);

            var lblTag = new Label();
            lblTag.Text = "标签";
            lblTag.AutoSize = false;
            lblTag.Size = new Size(40, 26);
            lblTag.TextAlign = ContentAlignment.MiddleLeft;
            lblTag.Margin = new Padding(0, 2, 4, 0);

            txtLabel = new TextBox();
            txtLabel.Size = new Size(110, 26);
            txtLabel.Margin = new Padding(0, 2, 6, 0);
            txtLabel.TextChanged += OnLabelTextChanged;

            lblLabelBytes = new Label();
            lblLabelBytes.Text = "0 字符";
            lblLabelBytes.AutoSize = false;
            lblLabelBytes.Size = new Size(112, 26);
            lblLabelBytes.TextAlign = ContentAlignment.MiddleLeft;
            lblLabelBytes.Margin = new Padding(0, 2, 10, 0);

            rdoAppend = new RadioButton();
            rdoAppend.Text = "追加样本";
            rdoAppend.Checked = true;
            rdoAppend.AutoSize = true;
            rdoAppend.Margin = new Padding(0, 4, 10, 0);

            rdoReplace = new RadioButton();
            rdoReplace.Text = "覆盖主样本";
            rdoReplace.AutoSize = true;
            rdoReplace.Margin = new Padding(0, 4, 10, 0);

            btnAdd = MakeButton("入库", 72);
            btnAdd.Kind = UiButtonKind.Start;
            btnAdd.Click += OnAdd;
            btnDelete = MakeButton("删除该字符", 104);
            btnDelete.Kind = UiButtonKind.Stop;
            btnDelete.Click += OnDelete;
            btnClearAll = MakeButton("清空目录", 88);
            btnClearAll.Kind = UiButtonKind.Stop;
            btnClearAll.Click += OnClearAll;
            btnMigrate = MakeButton("迁移到…", 88);
            btnMigrate.Click += OnMigrate;

            bottom.Controls.Add(lblTag);
            bottom.Controls.Add(txtLabel);
            bottom.Controls.Add(lblLabelBytes);
            bottom.Controls.Add(rdoAppend);
            bottom.Controls.Add(rdoReplace);
            bottom.Controls.Add(btnAdd);
            bottom.Controls.Add(btnDelete);
            bottom.Controls.Add(btnClearAll);
            bottom.Controls.Add(btnMigrate);

            // ---------------- 左：列表 + 失败清单 ----------------
            var left = new Panel();
            left.Dock = DockStyle.Left;
            left.Width = 430;
            left.Padding = new Padding(8, 4, 4, 4);

            listChars = new ListView();
            listChars.Dock = DockStyle.Fill;
            listChars.View = View.Details;
            listChars.FullRowSelect = true;
            listChars.MultiSelect = false;
            listChars.HideSelection = false;
            listChars.Columns.Add("标签", 110);
            listChars.Columns.Add("样本", 50);
            listChars.Columns.Add("尺寸", 90);
            listChars.Columns.Add("时间", 120);
            listChars.SelectedIndexChanged += OnCharSelected;

            lblFailures = new Label();
            lblFailures.Dock = DockStyle.Bottom;
            lblFailures.Height = 84;
            lblFailures.TextAlign = ContentAlignment.TopLeft;
            lblFailures.AutoEllipsis = false;

            left.Controls.Add(listChars);
            left.Controls.Add(lblFailures);

            // ---------------- 右：图像区 + 提取预览 ----------------
            var right = new Panel();
            right.Dock = DockStyle.Fill;
            right.Padding = new Padding(4, 4, 8, 4);

            var rightTop = new FlowLayoutPanel();
            rightTop.Dock = DockStyle.Top;
            rightTop.Height = 40;
            rightTop.FlowDirection = FlowDirection.LeftToRight;
            rightTop.WrapContents = false;

            btnLoadImage = MakeButton("载入图片", 96);
            btnLoadImage.Click += OnLoadImage;
            btnDrawRoi = MakeButton("框选字符", 96);
            btnDrawRoi.Kind = UiButtonKind.Grab;
            btnDrawRoi.Click += OnDrawRoi;

            lblPreviewInfo = new Label();
            lblPreviewInfo.Text = "在图上框选一个字符 → 填标签 → 入库";
            lblPreviewInfo.AutoSize = false;
            lblPreviewInfo.Size = new Size(360, 26);
            lblPreviewInfo.TextAlign = ContentAlignment.MiddleLeft;
            lblPreviewInfo.Margin = new Padding(8, 4, 0, 0);
            lblPreviewInfo.ForeColor = UiTheme.ColorTextSecondary;

            rightTop.Controls.Add(btnLoadImage);
            rightTop.Controls.Add(btnDrawRoi);
            rightTop.Controls.Add(lblPreviewInfo);

            viewer = new ImageViewer();
            viewer.Dock = DockStyle.Fill;
            viewer.OwnsImage = true;
            viewer.ShapeCreated += OnViewerShapeCreated;

            picPreview = new PictureBox();
            picPreview.Dock = DockStyle.Right;
            picPreview.Width = 140;
            picPreview.SizeMode = PictureBoxSizeMode.Zoom;
            picPreview.BackColor = UiTheme.ColorViewBg;

            right.Controls.Add(viewer);
            right.Controls.Add(picPreview);
            right.Controls.Add(rightTop);

            // ---------------- 状态条 ----------------
            lblStatus = new Label();
            lblStatus.Dock = DockStyle.Bottom;
            lblStatus.Height = 26;
            lblStatus.TextAlign = ContentAlignment.MiddleLeft;
            lblStatus.Padding = new Padding(8, 0, 8, 0);
            lblStatus.Text = "就绪：在右侧图上框选【单个字符】→ 填标签 → 入库（一个模板只装一个字符）";

            Controls.Add(right);
            Controls.Add(left);
            Controls.Add(top);
            Controls.Add(bottom);
            Controls.Add(lblStatus);

            ResumeLayout(false);
            PerformLayout();
        }

        private static UiButton MakeButton(string text, int width)
        {
            var b = new UiButton();
            b.Text = text;
            b.Kind = UiButtonKind.Neutral;
            b.Size = new Size(width, 28);
            b.Margin = new Padding(0, 2, 6, 2);
            return b;
        }

        // =====================================================================
        //  目录
        // =====================================================================

        /// <summary>参数值 → 绝对路径（<c>Path.Combine</c> 遇绝对路径会直接返回它）。</summary>
        private static string ResolveDir(string value)
        {
            string v = (value ?? "").Trim();
            if (v.Length == 0) v = "OCRModel";
            try { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, v); }
            catch (Exception) { return ""; }
        }

        private void OnDirTextChanged(object sender, EventArgs e)
        {
            if (_loading) return;

            string dir = ResolvedDir;
            string reason;
            if (!TryEnsureWritable(dir, out reason))
            {
                // 目录不可写 → 不写回，把输入框恢复成原值（避免"看起来改了、其实没生效"）
                _loading = true;
                try { txtDir.Text = _initialDir; }
                finally { _loading = false; }

                SetStatus("目录不可用：" + reason, false);
                return;
            }

            EventHandler h = DirChanged;
            if (h != null) h(this, EventArgs.Empty);

            ScanAsync();
        }

        /// <summary>目录可用性检查：能建出来 + 能写一个临时文件。</summary>
        private static bool TryEnsureWritable(string dir, out string reason)
        {
            reason = null;
            if (string.IsNullOrEmpty(dir)) { reason = "路径为空"; return false; }

            try
            {
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                string probe = Path.Combine(dir, ".write_probe_" + Guid.NewGuid().ToString("N") + ".tmp");
                using (var fs = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    fs.WriteByte(0);
                File.Delete(probe);
                return true;
            }
            catch (Exception ex)
            {
                reason = ex.Message;
                return false;
            }
        }

        private void OnBrowseDir(object sender, EventArgs e)
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "选择字符模板目录";
                string cur = ResolvedDir;
                if (!string.IsNullOrEmpty(cur) && Directory.Exists(cur)) dlg.SelectedPath = cur;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                txtDir.Text = dlg.SelectedPath;   // 触发 TextChanged → 写回 + 重扫
            }
        }

        private void OnOpenDir(object sender, EventArgs e)
        {
            string dir = ResolvedDir;
            try
            {
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                System.Diagnostics.Process.Start("explorer.exe", "\"" + dir + "\"");
            }
            catch (Exception ex)
            {
                SetStatus("无法打开目录：" + ex.Message, false);
            }
        }

        // =====================================================================
        //  扫描
        // =====================================================================

        private void ScanAsync()
        {
            string dir = ResolvedDir;
            int token = ++_scanToken;
            SetStatus("正在扫描 " + dir + " …", true);
            btnRescan.Enabled = false;

            Task.Run(() => OcrCharStore.Scan(dir)).ContinueWith(t =>
            {
                if (IsDisposed) return;
                try { BeginInvoke(new Action(() => OnScanCompleted(t, token))); }
                catch (Exception) { }
            });
        }

        private void OnScanCompleted(Task<OcrCharLibrary> task, int token)
        {
            if (token != _scanToken) return;   // 过期结果丢弃

            btnRescan.Enabled = true;

            if (task.IsFaulted || task.Result == null)
            {
                Exception ex = task.Exception != null ? task.Exception.GetBaseException() : null;
                SetStatus("扫描失败：" + (ex != null ? ex.Message : "未知原因"), false);
                return;
            }

            _lib = task.Result;
            FillList();

            // 失败清单：逐条列出（禁止静默）
            if (_lib.Failures.Count == 0)
            {
                lblFailures.Text = "读取失败：无";
            }
            else
            {
                var sb = new StringBuilder();
                sb.AppendLine("读取失败 " + _lib.Failures.Count + " 个：");
                int n = 0;
                foreach (OcrCharFailure f in _lib.Failures)
                {
                    if (n++ >= 4) { sb.AppendLine("…（其余 " + (_lib.Failures.Count - 4) + " 个见日志）"); break; }
                    sb.AppendLine("· " + f.FileName + " — " + f.Reason);
                }
                lblFailures.Text = sb.ToString();
            }

            if (_lib.IsEmpty) SetStatus("目录为空：当前只能做字符分割，不会输出文本。", false);
            else if (_lib.AllFailed) SetStatus(_lib.FileCount + " 个文件均读取失败 —— 请按下方清单检查文件。", false);
            else SetStatus(string.Format(CultureInfo.InvariantCulture,
                "共 {0} 个字符 · {1} 张样本" + (_lib.Failures.Count > 0 ? "（{2} 个读取失败）" : ""),
                _lib.LabelCount, _lib.SampleCount, _lib.Failures.Count), true);
        }

        private void FillList()
        {
            // 换列表必须先 Dispose 旧缩略图（否则每扫一次漏一批 GDI 句柄）
            if (_thumbs != null)
            {
                listChars.LargeImageList = null;
                listChars.SmallImageList = null;
                _thumbs.Dispose();
                _thumbs = null;
            }

            _thumbs = new ImageList();
            _thumbs.ImageSize = new Size(64, 48);
            _thumbs.ColorDepth = ColorDepth.Depth24Bit;
            listChars.SmallImageList = _thumbs;

            listChars.BeginUpdate();
            try
            {
                listChars.Items.Clear();
                foreach (OcrCharEntry e in _lib.Entries)
                {
                    OcrCharFile first = e.Files.Count > 0 ? e.Files[0] : null;

                    var item = new ListViewItem(e.Label);
                    item.SubItems.Add(e.SampleCount.ToString(CultureInfo.InvariantCulture));
                    item.SubItems.Add(first != null
                        ? first.PixelW + "×" + first.PixelH
                        : "—");
                    item.SubItems.Add(e.LastWrite == DateTime.MinValue
                        ? "—"
                        : e.LastWrite.ToString("MM-dd HH:mm", CultureInfo.InvariantCulture));
                    item.Tag = e;

                    // 缩略图懒加载：只在建行时取一次，取一张 Dispose 一张
                    Bitmap thumb = null;
                    try { thumb = OcrCharStore.LoadThumbnail(e.PrimaryPath); }
                    catch (Exception) { thumb = null; }
                    if (thumb != null)
                    {
                        try { _thumbs.Images.Add(e.Label, MakeThumb(thumb, _thumbs.ImageSize)); }
                        finally { thumb.Dispose(); }
                        item.ImageKey = e.Label;
                    }

                    listChars.Items.Add(item);
                }
            }
            finally
            {
                listChars.EndUpdate();
            }
        }

        private static Bitmap MakeThumb(Bitmap src, Size box)
        {
            int w = Math.Max(1, Math.Min(box.Width, src.Width));
            int h = Math.Max(1, Math.Min(box.Height, src.Height));
            var dst = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(dst))
                g.DrawImage(src, new Rectangle(0, 0, w, h));
            return dst;
        }

        // =====================================================================
        //  取图 / 框选
        // =====================================================================

        private void OnLoadImage(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "载入一张字符图片（用于框选入库）";
                dlg.Filter = "图片文件|*.bmp;*.png;*.jpg;*.jpeg;*.tif;*.tiff|所有文件|*.*";
                dlg.CheckFileExists = true;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                string error;
                Bitmap bmp = FlowPreviewPanel.LoadImageFromFile(dlg.FileName, out error);
                if (bmp == null)
                {
                    SetStatus("载入图片失败：" + error, false);
                    return;
                }

                if (_sourceFrame != null) { _sourceFrame.Dispose(); _sourceFrame = null; }
                _sourceFrame = new Bitmap(bmp);
                viewer.SetImage(bmp, true);
                SetStatus("已载入 " + Path.GetFileName(dlg.FileName) + "，请点「框选字符」后在图上拖框。", true);
            }
        }

        private void OnDrawRoi(object sender, EventArgs e)
        {
            if (viewer.Image == null)
            {
                SetStatus("请先载入图片。", false);
                return;
            }

            viewer.Mode = InteractionMode.DrawRectangle;
            SetStatus("在图上拖一个矩形框住单个字符。", true);
        }

        private void OnViewerShapeCreated(object sender, ShapeEventArgs e)
        {
            if (e == null || e.Shape == null) return;

            viewer.Mode = InteractionMode.Select;

            var rect = e.Shape as RectangleShape;
            if (rect == null) return;

            RectangleF r = rect.Rect;
            var roi = new Rectangle((int)Math.Round(r.X), (int)Math.Round(r.Y),
                                    (int)Math.Round(r.Width), (int)Math.Round(r.Height));

            if (viewer.Image != null)
                roi = Rectangle.Intersect(roi, new Rectangle(0, 0, viewer.Image.Width, viewer.Image.Height));

            if (roi.Width < OcrCharStore.MinSide || roi.Height < OcrCharStore.MinSide)
            {
                SetStatus("框选太小（至少 " + OcrCharStore.MinSide + "×" + OcrCharStore.MinSide + " 像素），请重新框。", false);
                return;
            }

            // 预览：把提取到的字符显示在右侧
            Bitmap crop = null;
            try
            {
                Bitmap src = _sourceFrame ?? viewer.Image;
                if (src != null) crop = src.Clone(roi, src.PixelFormat);
            }
            catch (Exception ex)
            {
                SetStatus("提取失败：" + ex.Message, false);
                return;
            }

            if (crop != null)
            {
                Image old = picPreview.Image;
                picPreview.Image = null;
                if (old != null) old.Dispose();
                picPreview.Image = crop;

                lblPreviewInfo.Text = string.Format(CultureInfo.InvariantCulture,
                    "已提取 {0}×{1}（原图 {2},{3}）→ 填标签后点「入库」",
                    roi.Width, roi.Height, roi.X, roi.Y);
            }

            UpdateAddButton();
        }

        // =====================================================================
        //  标签
        // =====================================================================

        private void OnLabelTextChanged(object sender, EventArgs e)
        {
            int bytes = OcrCharStore.LabelByteCount(txtLabel.Text);
            string err = OcrCharStore.ValidateLabel(txtLabel.Text);

            int elements = new StringInfo((txtLabel.Text ?? "").Trim()).LengthInTextElements;
            lblLabelBytes.Text = elements.ToString(CultureInfo.InvariantCulture) + " 字符 · "
                                 + bytes.ToString(CultureInfo.InvariantCulture)
                                 + "/" + OcrCharStore.MaxLabelBytes + " 字节";
            lblLabelBytes.ForeColor = (err != null) ? UiTheme.ColorNg : UiTheme.ColorTextSecondary;

            if (err != null) SetStatus(err, false);

            UpdateAddButton();
        }

        private void UpdateAddButton()
        {
            bool hasCrop = picPreview.Image != null;
            bool labelOk = OcrCharStore.ValidateLabel(txtLabel.Text) == null;
            btnAdd.Enabled = hasCrop && labelOk;
            btnDelete.Enabled = listChars.SelectedItems.Count > 0;
        }

        private void OnCharSelected(object sender, EventArgs e)
        {
            UpdateAddButton();

            if (listChars.SelectedItems.Count == 0) return;

            var entry = listChars.SelectedItems[0].Tag as OcrCharEntry;
            if (entry == null) return;

            txtLabel.Text = entry.Label;   // 方便"覆盖/追加"同一字符

            Bitmap thumb = null;
            try { thumb = OcrCharStore.LoadThumbnail(entry.PrimaryPath); }
            catch (Exception) { thumb = null; }

            Image old = picPreview.Image;
            picPreview.Image = null;
            if (old != null) old.Dispose();
            picPreview.Image = thumb;

            lblPreviewInfo.Text = string.Format(CultureInfo.InvariantCulture,
                "「{0}」已有 {1} 张样本（扩展名：{2}）",
                entry.Label, entry.SampleCount, ExtsOf(entry));
        }

        private static string ExtsOf(OcrCharEntry e)
        {
            var sb = new StringBuilder();
            foreach (OcrCharFile f in e.Files)
            {
                if (sb.Length > 0) sb.Append(" / ");
                sb.Append(f.Ext);
            }
            return sb.ToString();
        }

        // =====================================================================
        //  入库 / 删除 / 清空 / 迁移
        // =====================================================================

        private void OnAdd(object sender, EventArgs e)
        {
            if (picPreview.Image == null)
            {
                SetStatus("请先在图上框选一个字符。", false);
                return;
            }

            string label = (txtLabel.Text ?? "").Trim();
            string labelError = OcrCharStore.ValidateLabel(label);
            if (labelError != null) { SetStatus(labelError, false); return; }

            int existing = _lib.CountOf(label);
            if (existing > 0 && rdoAppend.Checked)
            {
                DialogResult ask = MessageBox.Show(this,
                    "「" + label + "」已存在 " + existing + " 张样本，继续追加为第 " + (existing + 1) + " 张？",
                    "追加样本", MessageBoxButtons.OKCancel, MessageBoxIcon.Information);
                if (ask != DialogResult.OK) return;
            }

            OcrCharAddMode mode = rdoReplace.Checked ? OcrCharAddMode.Replace : OcrCharAddMode.Append;

            // 入库用预览里那张（就是框选出来的原始灰度 ROI，不预二值化、不缩放）
            Bitmap sample = null;
            try
            {
                sample = new Bitmap(picPreview.Image);
            }
            catch (Exception ex)
            {
                SetStatus("读取待入库图像失败：" + ex.Message, false);
                return;
            }

            string reason;
            string path;
            try
            {
                path = OcrCharStore.Add(ResolvedDir, label, sample, mode, out reason);
            }
            finally
            {
                sample.Dispose();
            }

            if (path == null)
            {
                SetStatus("入库失败：" + reason, false);
                return;
            }

            SetStatus("已入库：" + Path.GetFileName(path) + (reason ?? ""), true);
            ScanAsync();
        }

        private void OnDelete(object sender, EventArgs e)
        {
            if (listChars.SelectedItems.Count == 0) return;

            var entry = listChars.SelectedItems[0].Tag as OcrCharEntry;
            if (entry == null) return;

            DialogResult ask = MessageBox.Show(this,
                "删除「" + entry.Label + "」的全部 " + entry.SampleCount + " 张样本？此操作不可撤销。",
                "删除字符", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
            if (ask != DialogResult.OK) return;

            string reason;
            bool ok = OcrCharStore.Delete(ResolvedDir, entry.Label, out reason);
            SetStatus(reason, ok);
            ScanAsync();
        }

        private void OnClearAll(object sender, EventArgs e)
        {
            DialogResult ask = MessageBox.Show(this,
                "清空目录里的全部模板文件（目录本身保留）？此操作不可撤销。\n目录：" + ResolvedDir,
                "清空目录", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
            if (ask != DialogResult.OK) return;

            string reason;
            int n = OcrCharStore.ClearAll(ResolvedDir, out reason);
            SetStatus(reason, n > 0);
            ScanAsync();
        }

        private void OnMigrate(object sender, EventArgs e)
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "选择迁移的目标目录（复制，不会移动原件）";
                // 默认落在程序的 OCRModel（<程序目录>\OCRModel）：模板跟着程序走，
                // 现场排查直接进程序目录就能看到样本
                try
                {
                    string def = VisionPlatform.Core.AppPaths.OcrCharsDir;
                    if (Directory.Exists(def)) dlg.SelectedPath = def;
                }
                catch (Exception) { }
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                string target = dlg.SelectedPath;
                bool deleteSource = false;

                DialogResult ask = MessageBox.Show(this,
                    "把「" + ResolvedDir + "」下的模板复制到：\n" + target
                    + "\n\n复制完成后是否删除源文件？（默认不删，宁留不丢）",
                    "迁移模板", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);

                if (ask == DialogResult.Cancel) return;
                deleteSource = (ask == DialogResult.Yes);

                if (deleteSource)
                {
                    DialogResult confirm = MessageBox.Show(this,
                        "确定删除源目录里已成功复制的模板文件？删除后无法恢复。",
                        "确认删除源文件", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
                    if (confirm != DialogResult.OK) deleteSource = false;
                }

                OcrCharMigrateResult r = OcrCharStore.Migrate(ResolvedDir, target, deleteSource);

                SetStatus(string.Format(CultureInfo.InvariantCulture,
                    "迁移完成：复制 {0}，跳过 {1}（目标已存在），失败 {2}",
                    r.Copied, r.Skipped, r.Failed), r.Failed == 0);

                if (r.Problems.Count > 0)
                {
                    var sb = new StringBuilder("以下文件未复制成功：\n");
                    int n = 0;
                    foreach (OcrCharFailure f in r.Problems)
                    {
                        if (n++ >= 8) { sb.AppendLine("…"); break; }
                        sb.AppendLine("· " + f.FileName + " — " + f.Reason);
                    }
                    MessageBox.Show(this, sb.ToString(), "迁移结果",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }

                ScanAsync();
            }
        }

        // =====================================================================
        //  杂项
        // =====================================================================

        private void SetStatus(string text, bool ok)
        {
            lblStatus.Text = text;
            lblStatus.ForeColor = ok ? UiTheme.ColorOk : UiTheme.ColorNg;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_thumbs != null) { _thumbs.Dispose(); _thumbs = null; }
                if (_sourceFrame != null) { _sourceFrame.Dispose(); _sourceFrame = null; }
                Image img = picPreview != null ? picPreview.Image : null;
                if (picPreview != null) picPreview.Image = null;
                if (img != null) img.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
