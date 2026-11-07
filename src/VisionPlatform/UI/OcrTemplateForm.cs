using System;
using System.Collections.Generic;
using System.ComponentModel;
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
    /// <para>
    /// 【界面代码在 <c>OcrTemplateForm.Designer.cs</c>】设计器只认"字段 + 属性赋值 + 具名事件处理器"：
    /// lambda、局部变量、辅助方法调用都不可序列化。运行期逻辑（扫描 / 主题 / 按钮语义色）
    /// 一律放在构造函数里，并用 <see cref="LicenseManager"/> 跳过设计期，避免设计器执行 IO。
    /// </para>
    /// </summary>
    public sealed partial class OcrTemplateForm : Form
    {
        private readonly string _initialDir;

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

        /// <summary>供 VS 设计器实例化用的无参构造（设计期不读目录、不做任何 IO）。</summary>
        public OcrTemplateForm() : this("") { }

        /// <summary>创建窗体。</summary>
        /// <param name="dir">节点当前的 <c>TemplateDir</c> 参数值。</param>
        public OcrTemplateForm(string dir)
        {
            _initialDir = dir ?? "";

            InitializeComponent();

            // 设计视图只搭界面：不绑原生控件事件、不套运行时主题、不扫描磁盘。
            // （设计器会真的执行构造函数，任何 IO / 静态服务调用都可能让它报
            //   "未能分析 InitializeComponent" 之类的错误。）
            if (IsDesignTime()) return;

            viewer.ShapeCreated += OnViewerShapeCreated;
            ApplyButtonKinds();
            ApplyThemeColors();

            // 初始目录只写进输入框，不触发 TextChanged（否则构造期会额外扫一次）
            _loading = true;
            try { txtDir.Text = _initialDir; }
            finally { _loading = false; }

            UiStyle.ApplyForm(this);

            // ApplyForm 之后再做显式配色（面板与状态文字）
            lblStatus.ForeColor = UiTheme.ColorTextSecondary;
            lblFailures.ForeColor = UiTheme.ColorTextSecondary;

            ScanAsync();
        }

        /// <summary>设计器实例化（VS 设计视图）时为 true。</summary>
        private static bool IsDesignTime()
        {
            return LicenseManager.UsageMode == LicenseUsageMode.Designtime;
        }

        /// <summary>
        /// 按钮语义色。放在运行期而不是 InitializeComponent：
        /// 自定义枚举属性（<see cref="UiButtonKind"/>）写进设计器代码容易被反序列化器丢弃。
        /// </summary>
        private void ApplyButtonKinds()
        {
            btnAdd.Kind = UiButtonKind.Start;
            btnDelete.Kind = UiButtonKind.Stop;
            btnClearAll.Kind = UiButtonKind.Stop;
            btnDrawRoi.Kind = UiButtonKind.Grab;
        }

        /// <summary>依赖主题静态值的颜色，运行期统一套；设计器代码只保留中性默认色。</summary>
        private void ApplyThemeColors()
        {
            lblPreviewInfo.ForeColor = UiTheme.ColorTextSecondary;
            picPreview.BackColor = UiTheme.ColorViewBg;
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

        /// <summary>「重扫」按钮：设计器只认具名处理器，lambda 无法被序列化。</summary>
        private void OnRescan(object sender, EventArgs e)
        {
            ScanAsync();
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
