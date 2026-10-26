using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Windows.Forms;
using VisionPlatform.Core;
using VisionPlatform.Flow;

namespace VisionPlatform.UI
{
    /// <summary>
    /// 参数面板顶部的「模板状态卡片」（挂在 <see cref="NodeParamPanel.SetBanner"/> 的插槽上）。
    /// <para>
    /// 一块卡片承担 FR-05 的全部状态显示：已就绪 / 未配置 / 加载失败（三类文案不同）、
    /// 缩略图、模板尺寸、创建时间、以及「重做 / 删除」两个动作。
    /// <strong>它是模板状态的唯一真源</strong> —— 参数表里刻意不再放一行 Note 显示状态，
    /// 否则两处显示同一状态、刷新时机一旦不同步就会互相矛盾。
    /// </para>
    /// <para>
    /// 【为什么所有颜色都显式取自 <see cref="UiTheme"/>】本卡片挂在 <c>NodeParamPanel</c> 的
    /// body 上，而那棵子树被 <see cref="UiStyle"/> <strong>整棵跳过</strong>（它自己管配色）。
    /// 指望 <c>UiStyle.ApplyForm</c> 把颜色传下来是不可能的；只能自己订阅主题变化重取色。
    /// </para>
    /// </summary>
    public sealed class TemplateStatusCard : Panel
    {
        /// <summary>卡片高度（<c>Dock=Top</c> 只吃高度，高度必须由卡片自己给）。</summary>
        public const int CardHeight = 108;

        // 版面常量（与 OcrTemplateCard 对齐：缩略图在左、文字在右、动作按钮单独一行）
        private const int ThumbW = 88;
        private const int ThumbH = 66;
        private const int TextLeft = 104;
        private const int TextRightPad = 8;
        private const int ButtonTop = 78;
        private const int ButtonH = 26;
        private const int BtnRedoW = 72;
        private const int BtnDeleteW = 64;
        private const int BtnGap = 6;

        private readonly PictureBox _thumb;
        private readonly Label _lblState;
        private readonly Label _lblDetail;
        private readonly Label _lblReason;
        private readonly UiButton _btnRedo;
        private readonly UiButton _btnDelete;

        private string _flowId = "";
        private string _nodeId = "";
        private string _algorithm = "ShapeMatch";
        private Rectangle _expectedRoi = Rectangle.Empty;

        // 缓存：戳不变就只刷文字，绝不重读 PNG / 重算缩略图
        private string _stamp = "";
        private string _stateText = "";
        private string _detailText = "";
        private string _reasonText = "";
        private Color _stateColor;

        /// <summary>点「重做」：宿主打开模板制作窗。</summary>
        public event EventHandler CreateRequested;

        /// <summary>点「删除」：宿主删模板并刷新卡片。</summary>
        public event EventHandler DeleteRequested;

        /// <summary>创建卡片。</summary>
        public TemplateStatusCard()
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
            _thumb.BackColor = UiTheme.ColorViewBg;   // 图像区在浅底上也要保持深色（与主界面同一条理由）
            _thumb.BorderStyle = BorderStyle.None;
            Controls.Add(_thumb);

            _lblState = MakeLabel(UiTheme.FontBody, true);
            _lblState.Location = new Point(TextLeft, 6);
            _lblState.Size = new Size(96, 22);
            Controls.Add(_lblState);

            // 明细/原因用 8.5pt 小字：FontBadgeSmall 是 14pt 的 OK/NG 徽标字体，
            // 拿它写明细会在 288px 右栏里被省略号吃掉（见 UiTheme.FontSmall 注释）。
            _lblDetail = MakeLabel(UiTheme.FontSmall, false);
            _lblDetail.Location = new Point(TextLeft, 28);
            _lblDetail.Size = new Size(150, 18);
            Controls.Add(_lblDetail);

            _lblReason = MakeLabel(UiTheme.FontSmall, false);
            _lblReason.Location = new Point(TextLeft, 46);
            _lblReason.Size = new Size(150, 30);
            Controls.Add(_lblReason);

            _btnRedo = new UiButton();
            _btnRedo.Text = "重做";
            _btnRedo.Kind = UiButtonKind.Neutral;
            _btnRedo.Size = new Size(BtnRedoW, ButtonH);
            _btnRedo.Location = new Point(8, ButtonTop);
            _btnRedo.Click += (s, e) =>
            {
                EventHandler h = CreateRequested;
                if (h != null) h(this, EventArgs.Empty);
            };
            Controls.Add(_btnRedo);

            _btnDelete = new UiButton();
            _btnDelete.Text = "删除";
            _btnDelete.Kind = UiButtonKind.Stop;
            _btnDelete.Size = new Size(BtnDeleteW, ButtonH);
            _btnDelete.Location = new Point(8 + BtnRedoW + BtnGap, ButtonTop);
            _btnDelete.Click += (s, e) =>
            {
                EventHandler h = DeleteRequested;
                if (h != null) h(this, EventArgs.Empty);
            };
            Controls.Add(_btnDelete);

            UiTheme.Changed += OnThemeChanged;
            ApplyColors();
        }

        private static Label MakeLabel(Font font, bool bold)
        {
            var l = new Label();
            l.AutoSize = false;
            l.Font = font;
            l.TextAlign = ContentAlignment.MiddleLeft;
            l.BackColor = Color.Transparent;
            l.ForeColor = UiTheme.ColorTextSecondary;
            if (bold) l.Font = new Font(font.FontFamily, font.Size, FontStyle.Bold);
            return l;
        }

        /// <summary>绑定节点：只有节点或算法变了才需要重新绑定（宿主负责"变了才 new"）。</summary>
        public void Bind(string flowId, string nodeId, string algorithm, Rectangle expectedRoi)
        {
            bool changed = !string.Equals(_nodeId, nodeId, StringComparison.Ordinal);
            _flowId = flowId ?? "";
            _nodeId = nodeId ?? "";
            _algorithm = string.IsNullOrEmpty(algorithm) ? "ShapeMatch" : algorithm;
            _expectedRoi = expectedRoi;

            if (changed) _stamp = "";     // 换节点必须重算，别拿上一个节点的缓存
            ApplyColors();
        }

        /// <summary>
        /// 刷新卡片内容。
        /// <para>
        /// <strong>戳没变就只贴文字</strong>：模板文件未被改动时，这里连 PNG 都不读 —— 参数面板在
        /// 旋转 ROI 编辑态下每次松手都会 <c>SetNode</c>，若每次都读图算缩略图，拖一次手柄就卡一下。
        /// </para>
        /// </summary>
        public void Refresh()
        {
            string stamp = TemplateStore.StampOf(_flowId, _nodeId);
            if (string.Equals(stamp, _stamp, StringComparison.Ordinal) && !string.IsNullOrEmpty(_stateText))
            {
                ApplyTexts();
                return;
            }

            _stamp = stamp;

            if (stamp == "0")
            {
                // 文件不存在：先看是不是"没做过"，也可能是"版本/损坏"（json 在 png 不在）
                TemplateLoadError err;
                string msg;
                TemplateAsset probe = TemplateStore.Load(_flowId, _nodeId, out err, out msg);
                if (probe == null && err == TemplateLoadError.CorruptedOrVersionMismatch)
                {
                    SetState("加载失败", UiTheme.ColorNg,
                        "模板：—",
                        msg + "\n请点「重做」重新创建模板。",
                        false);
                    return;
                }
                if (probe != null) probe.Template.Dispose();

                SetState("未配置", UiTheme.ColorWarn,
                    "模板：—",
                    string.Format(CultureInfo.InvariantCulture,
                        "还没有模板。点「重做」打开模板制作窗：取图 → 框选 ROI → 训练 → 保存。"),
                    false);
                return;
            }

            TemplateLoadError loadError;
            string loadMessage;
            TemplateAsset asset = TemplateStore.Load(_flowId, _nodeId, _algorithm, _expectedRoi,
                                                     out loadError, out loadMessage);
            if (asset == null)
            {
                if (loadError == TemplateLoadError.MismatchWithNode)
                {
                    // ROI 被改过 / 算法换成另一种：模板图已经不对应当前设置，必须重做
                    SetState("与设置不匹配", UiTheme.ColorWarn, "模板：需重做", loadMessage, false);
                }
                else
                {
                    SetState("加载失败", UiTheme.ColorNg, "模板：—", loadMessage, false);
                }
                return;
            }

            try
            {
                SetState("已就绪", UiTheme.ColorOk,
                    string.Format(CultureInfo.InvariantCulture, "{0}×{1}　{2}　{3}",
                        asset.Meta.ImageW, asset.Meta.ImageH,
                        asset.Meta.Algorithm == "TemplateMatch"
                            ? "NCC"
                            : string.Format(CultureInfo.InvariantCulture, "特征点 {0}", asset.Meta.FeatureCount),
                        FormatUtc(asset.Meta.CreatedUtc)),
                    string.Format(CultureInfo.InvariantCulture,
                        "训练参数 {0}　{1}",
                        string.IsNullOrEmpty(asset.Meta.TrainSignature) ? "—" : asset.Meta.TrainSignature,
                        string.IsNullOrEmpty(asset.Meta.ImagePixelFormat) ? "" : asset.Meta.ImagePixelFormat),
                    true);

                SetThumbnail(asset.Template);
            }
            finally
            {
                asset.Template.Dispose();
            }
        }

        /// <summary>清空（切到没有模板的节点类型时用；宿主通常会直接 SetBanner(null) 而不是调它）。</summary>
        public void Clear()
        {
            _stamp = "";
            _stateText = "";
            SetThumbnail(null);
            _lblState.Text = "";
            _lblDetail.Text = "";
            _lblReason.Text = "";
        }

        // =====================================================================
        //  内部
        // =====================================================================

        private void SetState(string state, Color color, string detail, string reason, bool hasThumb)
        {
            _stateText = state;
            _stateColor = color;
            _detailText = detail;
            _reasonText = reason;

            if (!hasThumb) SetThumbnail(null);
            ApplyTexts();
        }

        private void ApplyTexts()
        {
            _lblState.Text = _stateText;
            _lblState.ForeColor = _stateColor;
            _lblDetail.Text = _detailText;
            _lblReason.Text = _reasonText;

            // 「重做」始终可用；「删除」只在真有模板时可用（没模板还让点删除是假动作）
            bool has = !string.IsNullOrEmpty(_stateText)
                       && (_stateText == "已就绪" || _stateText == "与设置不匹配" || _stateText == "加载失败");
            _btnDelete.Enabled = has;
            _btnDelete.Visible = has;
            _btnRedo.Visible = true;

            // 文字区宽度跟着卡片走；按钮位置在构造期已定（独立一行，不随宽度变化、也不会压住文字）
            int textW = Math.Max(60, Width - TextLeft - TextRightPad);
            _lblState.Width = textW;
            _lblDetail.Width = textW;
            _lblReason.Width = textW;
        }

        /// <summary>
        /// 设置缩略图。
        /// <para>
        /// 【三个坑一次性处理掉】① 8bpp 索引位图不能直接给 <c>PictureBox</c>（GDI+ 无法在索引位图上
        /// 建 <c>Graphics</c>，显示时抛异常）→ 统一转 24bpp；② 不能用 <c>new Bitmap(path)</c> 直接塞
        /// （会长期锁文件）→ 由 <see cref="TemplateStore"/> 先读后拷；③ 每次换图先 Dispose 旧图，
        /// 否则切节点就是一次漏一张。
        /// </para>
        /// <para>
        /// 第 4 个坑是"放大糊成一团"：20×8 的小模板丢给 <c>SizeMode=Zoom</c> 会被双线性插值抹平，
        /// 看不出特征结构。所以<strong>上采样时先用 NearestNeighbor 预放大</strong>到不小于显示框，
        /// 再交给 <c>PictureBox</c>（此时 Zoom 的比例≈1，不会再插值）。
        /// </para>
        /// </summary>
        private void SetThumbnail(Bitmap source)
        {
            Image old = _thumb.Image;
            _thumb.Image = null;
            if (old != null) old.Dispose();

            if (source == null) return;

            _thumb.Image = MakeThumbnail(source, new Size(ThumbW, ThumbH));
        }

        private static Bitmap MakeThumbnail(Bitmap src, Size box)
        {
            if (src == null || src.Width <= 0 || src.Height <= 0) return null;

            double sx = (double)box.Width / src.Width;
            double sy = (double)box.Height / src.Height;
            double scale = Math.Min(sx, sy);
            if (scale <= 0) scale = 1.0;

            int w = Math.Max(1, (int)Math.Round(src.Width * scale));
            int h = Math.Max(1, (int)Math.Round(src.Height * scale));

            // 目标一律 24bpp：8bpp 索引图给 PictureBox 会抛 "A Graphics object cannot be created
            // from an image that has an indexed pixel format"
            var dst = new Bitmap(w, h, PixelFormat.Format24bppRgb);
            try
            {
                using (var g = Graphics.FromImage(dst))
                {
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.InterpolationMode = scale > 1.0
                        ? InterpolationMode.NearestNeighbor      // 放大：保结构，别糊
                        : InterpolationMode.HighQualityBicubic;  // 缩小：抗锯齿更耐看
                    g.DrawImage(src, new Rectangle(0, 0, w, h));
                }
                return dst;
            }
            catch (Exception)
            {
                dst.Dispose();
                throw;
            }
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
            _lblReason.ForeColor = UiTheme.ColorTextSecondary;
            _lblState.ForeColor = _stateText.Length > 0 ? _stateColor : UiTheme.ColorTextSecondary;
        }

        private static string FormatUtc(string iso)
        {
            DateTime t;
            if (!string.IsNullOrEmpty(iso)
                && DateTime.TryParse(iso, CultureInfo.InvariantCulture,
                       DateTimeStyles.RoundtripKind, out t))
            {
                return t.ToLocalTime().ToString("MM-dd HH:mm", CultureInfo.InvariantCulture);
            }
            return iso ?? "";
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
