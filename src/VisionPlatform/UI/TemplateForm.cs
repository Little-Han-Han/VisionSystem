using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using VisionPlatform.Core;
using VisionPlatform.Flow;
using VisionPlatform.Native;
using VisionSystem.Controls;
using VisionSystem.Controls.Geometry;

namespace VisionPlatform.UI
{
    /// <summary>
    /// 模板制作窗（模态）：取图 / 画 ROI / 训练 / 保存。
    /// <para>
    /// <strong>训练与保存分离</strong>：先训练看效果（特征点数、模型尺寸、耗时、绿红结论），
    /// 满意再点「保存模板」。这样"训练一秒钟、保存一辈子"的错误决策不会发生 ——
    /// 模板一旦落盘，运行期就一直拿它当基准，错了要重新示教。
    /// </para>
    /// <para>
    /// 【为什么单文件手写 <c>InitializeComponent</c>、不建 <c>.Designer.cs</c>】
    /// 本项目已经发生过一次"VS 重序列化把事件订阅吃掉"，导致按钮点了没反应（DEF-01）。
    /// 手写在源码里，事件订阅与会话生命周期都在同一个文件内可审查，
    /// 也让"训练在后台线程、UI 不冻结"这种非设计器能表达的逻辑不必和设计器抢代码。
    /// </para>
    /// </summary>
    public sealed class TemplateForm : Form
    {
        // ---------------------- 入参 ----------------------
        private readonly string _algorithm;          // "ShapeMatch" / "TemplateMatch"
        private readonly string _flowId;
        private readonly string _nodeId;
        private readonly Func<Bitmap> _frameProvider; // 抓取"当前帧"的取帧器（可能返回 null）
        private readonly Rectangle _initialRoi;

        private readonly double _contrast;
        private readonly int _maxFeatures;
        private readonly double _minFeatureDist;

        // ---------------------- 控件 ----------------------
        private TableLayoutPanel root;
        private FlowLayoutPanel toolbar;
        private UiButton btnLoadImage;
        private UiButton btnGrabFrame;
        private UiButton btnDrawRoi;

        private ImageViewer viewer;

        private FlowLayoutPanel sidePanel;
        private NumericUpDown numContrast;
        private NumericUpDown numMaxFeatures;
        private NumericUpDown numMinDist;
        private Label lblAlgoValue;
        private UiButton btnTrain;
        private UiButton btnSave;
        private UiButton btnCancel;

        private TableLayoutPanel diagnostics;
        private Label lblPoints;
        private Label lblModel;
        private Label lblRoiInfo;
        private Label lblElapsed;
        private Label lblVerdict;

        // ---------------------- 状态 ----------------------
        private TemplateTrainer _trainer;
        private TemplateTrainResult _lastResult;
        private Bitmap _lastCrop;              // 待保存的模板裁剪图（本窗体持有）
        private Rectangle _lastRoi;            // 训练时实际使用的 ROI
        private int _lastFrameW;
        private int _lastFrameH;
        private double _trainedContrast;
        private int _trainedMaxFeatures;
        private double _trainedMinFeatureDist;
        private bool _busy;
        private bool _disposedLocal;

        /// <summary>本窗体对应的算法是不是模板匹配（NCC）：它没有特征点、也不吃训练三参数。</summary>
        private bool IsNcc
        {
            get { return string.Equals(_algorithm, "TemplateMatch", StringComparison.OrdinalIgnoreCase); }
        }

        /// <summary>保存成功后的模板戳（宿主据此立即刷新状态卡片，无需等下次启动）。</summary>
        public string SavedStamp { get; private set; }

        /// <summary>保存成功后的说明（含体积）。</summary>
        public string SavedMessage { get; private set; }

        /// <summary>
        /// 保存成功时用的模板 ROI（<strong>宽高为 0 表示整帧</strong>）。
        /// <para>
        /// 宿主必须把它写回节点的 <c>TemplateX/Y/W/H</c>：模板图正是这块 ROI 的像素，
        /// 若节点参数还是旧值（比如 0,0,0,0），运行期会立刻判「模板与当前 ROI 不匹配」——
        /// 用户明明刚保存成功却看到"不匹配"，是最难自查的一类不一致。
        /// </para>
        /// </summary>
        public Rectangle SavedRoi { get; private set; }

        /// <summary>
        /// 创建模板窗。
        /// </summary>
        /// <param name="algorithm">算法类型：<c>"ShapeMatch"</c> 或 <c>"TemplateMatch"</c>。</param>
        /// <param name="flowId">所属流程 Id（模板落盘目录的第一层）。</param>
        /// <param name="nodeId">节点 Id（第二层）。</param>
        /// <param name="templateRoi">节点当前的模板 ROI（宽高为 0 表示整帧）。</param>
        /// <param name="contrast">形状匹配训练参数：对比度。</param>
        /// <param name="maxFeatures">形状匹配训练参数：最多特征点。</param>
        /// <param name="minFeatureDist">形状匹配训练参数：最小特征间距。</param>
        /// <param name="frameProvider">"抓取当前帧"用的取帧器；返回的位图<strong>所有权归本窗体</strong>，调用方不要 Dispose。</param>
        public TemplateForm(string algorithm, string flowId, string nodeId,
                            Rectangle templateRoi,
                            double contrast, int maxFeatures, double minFeatureDist,
                            Func<Bitmap> frameProvider)
        {
            _algorithm = string.IsNullOrEmpty(algorithm) ? "ShapeMatch" : algorithm;
            _flowId = flowId ?? "";
            _nodeId = nodeId ?? "";
            _initialRoi = templateRoi;
            _contrast = contrast;
            _maxFeatures = maxFeatures;
            _minFeatureDist = minFeatureDist;
            _frameProvider = frameProvider;

            _trainer = new TemplateTrainer();

            InitializeComponent();

            LoadExistingTemplate();   // 已有模板时先把它的图显示出来（便于"看一眼再决定要不要重做"）
            ApplyInitialRoi();
            UpdateTrainButton();

            UiStyle.ApplyForm(this);

            // ApplyForm 之后再做少量显式配色：ApplyForm 会把 Panel 统一成 ColorPanelBg，
            // 而这两块要更强/更弱的层级（工具条与诊断区）。
            toolbar.BackColor = UiTheme.ColorHeaderBg;
            diagnostics.BackColor = UiTheme.ColorCardBg;
            lblAlgoValue.ForeColor = UiTheme.ColorAccent;
            SetVerdict("尚未训练。请先取图、框选模板区域，再点「训练」。", false);
        }

        // =====================================================================
        //  界面构建（手写，不依赖设计器）
        // =====================================================================

        private void InitializeComponent()
        {
            SuspendLayout();

            Text = "创建模板 —— " + TemplateStore.DisplayAlgorithm(_algorithm);
            ClientSize = new Size(1180, 760);
            MinimumSize = new Size(960, 640);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            KeyPreview = true;

            root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 2;
            root.RowCount = 3;
            root.Padding = new Padding(8);
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 348f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46f));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 116f));

            // ---------- 工具条（跨两列）----------
            toolbar = new FlowLayoutPanel();
            toolbar.Dock = DockStyle.Fill;
            toolbar.FlowDirection = FlowDirection.LeftToRight;
            toolbar.WrapContents = false;
            toolbar.Padding = new Padding(6, 8, 6, 8);
            toolbar.Margin = new Padding(0, 0, 0, 6);

            btnLoadImage = MakeButton("载入图片", UiButtonKind.Neutral, 112);
            btnLoadImage.Click += OnLoadImage;
            btnGrabFrame = MakeButton("抓取当前帧", UiButtonKind.Neutral, 118);
            btnGrabFrame.Click += OnGrabFrame;
            btnDrawRoi = MakeButton("绘制 ROI", UiButtonKind.Grab, 112);
            btnDrawRoi.Click += OnDrawRoi;

            toolbar.Controls.Add(btnLoadImage);
            toolbar.Controls.Add(btnGrabFrame);
            toolbar.Controls.Add(btnDrawRoi);

            root.Controls.Add(toolbar, 0, 0);
            root.SetColumnSpan(toolbar, 2);

            // ---------- 左侧图像区 ----------
            viewer = new ImageViewer();
            viewer.Dock = DockStyle.Fill;
            viewer.Margin = new Padding(0, 0, 6, 6);
            viewer.OwnsImage = true;
            viewer.ShapeCreated += OnViewerShapeCreated;
            root.Controls.Add(viewer, 0, 1);

            // ---------- 右侧参数与动作 ----------
            sidePanel = new FlowLayoutPanel();
            sidePanel.Dock = DockStyle.Fill;
            sidePanel.FlowDirection = FlowDirection.TopDown;
            sidePanel.WrapContents = false;
            sidePanel.AutoScroll = true;
            sidePanel.Padding = new Padding(10, 6, 6, 6);
            sidePanel.Margin = new Padding(0, 0, 0, 6);

            sidePanel.Controls.Add(MakeSectionTitle("算法"));
            lblAlgoValue = MakeValueLabel(TemplateStore.DisplayAlgorithm(_algorithm));
            sidePanel.Controls.Add(lblAlgoValue);

            sidePanel.Controls.Add(MakeSectionTitle("训练参数"));
            numContrast = AddNumericRow(sidePanel, "对比度", 1m, 255m, 1m, (decimal)_contrast,
                "边缘提取低阈值，典型 20~60；偏低会引入噪声，偏高会漏掉弱边缘");
            numMaxFeatures = AddNumericRow(sidePanel, "最大特征数", 10m, 20000m, 50m, _maxFeatures,
                "越少匹配越快；典型 800~2000");
            numMinDist = AddNumericRow(sidePanel, "最小特征间距(px)", 0.5m, 50m, 0.5m, (decimal)_minFeatureDist,
                "越大特征越稀疏、匹配越快；典型 1~3");
            numContrast.ValueChanged += OnTrainParamChanged;
            numMaxFeatures.ValueChanged += OnTrainParamChanged;
            numMinDist.ValueChanged += OnTrainParamChanged;

            // NCC 只用"像素副本"做模板，没有边缘特征点，这三个参数对它毫无作用。
            // 留着可改=让用户以为"我调了参数应该有用"，是纯粹的误导，所以按节点类型裁掉。
            if (IsNcc)
            {
                numContrast.Enabled = false;
                numMaxFeatures.Enabled = false;
                numMinDist.Enabled = false;
            }

            sidePanel.Controls.Add(MakeSectionTitle("动作"));
            btnTrain = MakeButton("训练", UiButtonKind.Start, 300);
            btnTrain.Click += OnTrain;
            btnSave = MakeButton("保存模板", UiButtonKind.Start, 300);
            btnSave.Click += OnSave;
            btnCancel = MakeButton("关闭", UiButtonKind.Neutral, 300);
            btnCancel.Click += OnCancel;
            sidePanel.Controls.Add(btnTrain);
            sidePanel.Controls.Add(btnSave);
            sidePanel.Controls.Add(btnCancel);

            root.Controls.Add(sidePanel, 1, 1);

            // ---------- 底部诊断区（跨两列）----------
            diagnostics = new TableLayoutPanel();
            diagnostics.Dock = DockStyle.Fill;
            diagnostics.ColumnCount = 2;
            diagnostics.RowCount = 3;
            diagnostics.Padding = new Padding(10, 6, 10, 6);
            diagnostics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            diagnostics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            diagnostics.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));
            diagnostics.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));
            diagnostics.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            lblPoints = MakeDiagLabel();
            lblModel = MakeDiagLabel();
            lblRoiInfo = MakeDiagLabel();
            lblElapsed = MakeDiagLabel();
            lblVerdict = MakeDiagLabel();
            lblVerdict.AutoSize = false;
            lblVerdict.Dock = DockStyle.Fill;

            diagnostics.Controls.Add(lblPoints, 0, 0);
            diagnostics.Controls.Add(lblModel, 1, 0);
            diagnostics.Controls.Add(lblRoiInfo, 0, 1);
            diagnostics.Controls.Add(lblElapsed, 1, 1);
            diagnostics.Controls.Add(lblVerdict, 0, 2);
            diagnostics.SetColumnSpan(lblVerdict, 2);

            root.Controls.Add(diagnostics, 0, 2);
            root.SetColumnSpan(diagnostics, 2);

            Controls.Add(root);

            ResumeLayout(false);
            PerformLayout();
        }

        private static UiButton MakeButton(string text, UiButtonKind kind, int width)
        {
            var b = new UiButton();
            b.Text = text;
            b.Kind = kind;
            b.Size = new Size(width, 34);
            b.Margin = new Padding(0, 2, 8, 2);
            return b;
        }

        private Label MakeSectionTitle(string text)
        {
            var l = new Label();
            l.Text = text;
            l.AutoSize = false;
            l.Size = new Size(300, 24);
            l.Margin = new Padding(0, 8, 0, 2);
            l.Font = UiTheme.FontGroupTitle;
            l.ForeColor = UiTheme.ColorTextSecondary;
            l.TextAlign = ContentAlignment.MiddleLeft;
            return l;
        }

        private Label MakeValueLabel(string text)
        {
            var l = new Label();
            l.Text = text;
            l.AutoSize = false;
            l.Size = new Size(300, 24);
            l.Margin = new Padding(4, 0, 0, 2);
            l.Font = UiTheme.FontBody;
            l.TextAlign = ContentAlignment.MiddleLeft;
            return l;
        }

        private Label MakeDiagLabel()
        {
            var l = new Label();
            l.AutoSize = true;
            l.Margin = new Padding(0, 2, 8, 2);
            l.Font = UiTheme.FontBody;
            l.Text = "—";
            return l;
        }

        /// <summary>
        /// 加一行「标题 + 数值框」，返回数值框。数值框的上下限与小数位在这里一次性定死，
        /// 避免每个调用点各写一遍（数值框的边界就是参数的有效域，散落各处必然漂移）。
        /// </summary>
        private NumericUpDown AddNumericRow(FlowLayoutPanel parent, string title,
                                            decimal min, decimal max, decimal step, decimal value,
                                            string hint)
        {
            var row = new FlowLayoutPanel();
            row.FlowDirection = FlowDirection.LeftToRight;
            row.WrapContents = false;
            row.AutoSize = true;
            row.Margin = new Padding(0, 0, 0, 0);

            var lbl = new Label();
            lbl.Text = title;
            lbl.AutoSize = false;
            lbl.Size = new Size(132, 26);
            lbl.TextAlign = ContentAlignment.MiddleLeft;
            lbl.Margin = new Padding(0, 2, 4, 0);
            lbl.Font = UiTheme.FontBody;

            var num = new NumericUpDown();
            num.Minimum = min;
            num.Maximum = max;
            num.Increment = step;
            num.DecimalPlaces = step < 1m ? 1 : 0;
            num.Value = Clamp(value, min, max);
            num.Size = new Size(110, 26);
            num.Margin = new Padding(0, 2, 0, 0);
            num.ThousandsSeparator = false;

            row.Controls.Add(lbl);
            row.Controls.Add(num);
            parent.Controls.Add(row);

            var tip = new Label();
            tip.Text = hint;
            tip.AutoSize = false;
            tip.Size = new Size(300, 32);
            tip.Margin = new Padding(4, 0, 0, 4);
            // 用 8.5pt 小字：FontBadgeSmall 是 14pt 的 OK/NG 徽标字体，提示文字会被截掉一半
            tip.Font = UiTheme.FontSmall;
            tip.ForeColor = UiTheme.ColorTextSecondary;
            parent.Controls.Add(tip);

            return num;
        }

        private static decimal Clamp(decimal v, decimal min, decimal max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        // =====================================================================
        //  取图
        // =====================================================================

        /// <summary>把"已有模板"读出来显示：只显示图，不改任何节点参数。</summary>
        private void LoadExistingTemplate()
        {
            TemplateLoadError err;
            string msg;
            TemplateAsset asset = TemplateStore.Load(_flowId, _nodeId, out err, out msg);
            if (asset == null)
            {
                if (err != TemplateLoadError.FileMissing)
                    SetVerdict("已有模板无法读取：" + msg, false);
                return;
            }

            try
            {
                viewer.SetImage(new Bitmap(asset.Template), true);
                lblPoints.Text = IsNcc
                    ? "特征点数：—（NCC 不提取特征点）"
                    : "特征点数：—（尚未在本窗训练）";
                lblModel.Text = string.Format(CultureInfo.InvariantCulture,
                    "已有模板：{0}×{1}，创建于 {2}", asset.Meta.ImageW, asset.Meta.ImageH,
                    FormatUtc(asset.Meta.CreatedUtc));
                SetVerdict("已载入磁盘上的模板图。若要重做，请重新取图并框选后训练。", true);
            }
            finally
            {
                asset.Template.Dispose();
            }
        }

        private void OnLoadImage(object sender, EventArgs e)
        {
            if (_busy) return;

            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "载入图片（用作训练帧）";
                dlg.Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp|所有文件|*.*";
                dlg.CheckFileExists = true;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                string error;
                Bitmap bmp = FlowPreviewPanel.LoadImageFromFile(dlg.FileName, out error);
                if (bmp == null)
                {
                    SetVerdict("载入图片失败：" + error, false);
                    return;
                }

                SetFrame(bmp, Path.GetFileName(dlg.FileName));
            }
        }

        private void OnGrabFrame(object sender, EventArgs e)
        {
            if (_busy) return;
            if (_frameProvider == null)
            {
                SetVerdict("当前没有可抓取的帧：请先在主界面跑一次流程或载入图片。", false);
                return;
            }

            Bitmap frame = null;
            try
            {
                frame = _frameProvider();
            }
            catch (Exception ex)
            {
                SetVerdict("抓取当前帧失败：" + ex.Message, false);
                return;
            }

            if (frame == null)
            {
                SetVerdict("当前没有可抓取的帧：预览区还没有图像。", false);
                return;
            }

            SetFrame(frame, "当前帧");
        }

        /// <summary>换训练帧（<paramref name="bmp"/> 的所有权转交预览区）。</summary>
        private void SetFrame(Bitmap bmp, string title)
        {
            if (bmp == null) return;

            viewer.SetImage(bmp, true);   // OwnsImage=true：视图负责释放旧帧
            ClearFeatureLayer();
            ResetTrainResult();
            RestoreRoiShape();

            lblModel.Text = string.Format(CultureInfo.InvariantCulture, "训练帧：{0}（{1}×{2}）",
                title ?? "", viewer.Image != null ? viewer.Image.Width : 0,
                viewer.Image != null ? viewer.Image.Height : 0);
            SetVerdict("已取到训练帧。请框选模板区域（按住左键拖一个框），再点「训练」。", true);
            UpdateTrainButton();
        }

        // =====================================================================
        //  ROI
        // =====================================================================

        private void OnDrawRoi(object sender, EventArgs e)
        {
            if (_busy) return;
            if (viewer.Image == null)
            {
                SetVerdict("请先载入图片或抓取当前帧，再框选模板区域。", false);
                return;
            }

            viewer.Mode = InteractionMode.DrawRectangle;
            btnDrawRoi.Text = "框选中…";
            SetVerdict("在图上按住左键拖一个矩形 —— 框住要作为模板的那块区域。", true);
        }

        private void OnViewerShapeCreated(object sender, ShapeEventArgs e)
        {
            if (e == null || e.Shape == null) return;

            var rect = e.Shape as RectangleShape;
            if (rect == null) return;

            // 画完立刻回到选择态：否则用户想微调手柄时会又画一个框
            viewer.Mode = InteractionMode.Select;
            btnDrawRoi.Text = "绘制 ROI";

            // 只保留最新的 ROI + 特征点层，避免每画一次就堆一个框
            RemoveRoiShapes();
            rect.Name = "模板ROI";
            rect.StrokeColor = UiTheme.ColorAccent;
            rect.LineWidth = 2f;
            rect.IsEditable = true;
            rect.IsSelected = false;
            viewer.Shapes.Add(rect);

            ResetTrainResult();
            UpdateRoiDiagnostics();
            SetVerdict("ROI 已选定。可拖角点微调，然后点「训练」。", true);
            UpdateTrainButton();
        }

        /// <summary>按节点当前的模板 ROI 还原一个框（宽高为 0 表示整帧，不画）。</summary>
        private void ApplyInitialRoi()
        {
            if (_initialRoi.Width <= 0 || _initialRoi.Height <= 0) return;
            if (viewer.Image == null) return;

            RemoveRoiShapes();
            var rect = new RectangleShape(new RectangleF(_initialRoi.X, _initialRoi.Y,
                                                         _initialRoi.Width, _initialRoi.Height));
            rect.Name = "模板ROI";
            rect.StrokeColor = UiTheme.ColorAccent;
            rect.LineWidth = 2f;
            rect.IsEditable = true;
            viewer.Shapes.Add(rect);
            UpdateRoiDiagnostics();
        }

        private void RestoreRoiShape()
        {
            ApplyInitialRoi();
        }

        /// <summary>取当前 ROI（图像坐标）；没有框时返回整帧。</summary>
        private Rectangle CurrentRoi()
        {
            if (viewer.Image == null) return Rectangle.Empty;

            foreach (IGeometryShape s in viewer.Shapes)
            {
                var r = s as RectangleShape;
                if (r == null) continue;

                RectangleF f = r.Rect;
                var roi = new Rectangle(
                    (int)Math.Round(f.X), (int)Math.Round(f.Y),
                    (int)Math.Round(f.Width), (int)Math.Round(f.Height));

                Rectangle real = Rectangle.Intersect(roi, new Rectangle(0, 0, viewer.Image.Width, viewer.Image.Height));
                if (real.Width > 0 && real.Height > 0) return real;
            }
            return Rectangle.Empty;
        }

        private void RemoveRoiShapes()
        {
            for (int i = viewer.Shapes.Count - 1; i >= 0; i--)
            {
                if (viewer.Shapes[i] is RectangleShape) viewer.Shapes.RemoveAt(i);
            }
            viewer.Shapes.NotifyChanged();
        }

        private void ClearFeatureLayer()
        {
            for (int i = viewer.Shapes.Count - 1; i >= 0; i--)
            {
                if (viewer.Shapes[i] is ModelFeatureLayer) viewer.Shapes.RemoveAt(i);
            }
            viewer.Shapes.NotifyChanged();
        }

        private void UpdateRoiDiagnostics()
        {
            Rectangle roi = CurrentRoi();
            lblRoiInfo.Text = roi.Width > 0 && roi.Height > 0
                ? string.Format(CultureInfo.InvariantCulture, "ROI：X={0} Y={1}，{2}×{3}，角度 0°",
                                roi.X, roi.Y, roi.Width, roi.Height)
                : "ROI：整帧（未框选）";
        }

        // =====================================================================
        //  训练
        // =====================================================================

        private void OnTrainParamChanged(object sender, EventArgs e)
        {
            // 训练参数一改，上次的训练结论就作废：不允许"用旧参数的结果"去保存
            if (_lastResult != null) ResetTrainResult();
            UpdateTrainButton();
        }

        private void OnTrain(object sender, EventArgs e)
        {
            if (_busy) return;
            if (viewer.Image == null)
            {
                SetVerdict("请先载入图片或抓取当前帧。", false);
                return;
            }

            double contrast = (double)numContrast.Value;
            int maxFeatures = (int)numMaxFeatures.Value;
            double minDist = (double)numMinDist.Value;

            string paramError;
            if (!TemplateTrainer.CheckTrainParams(contrast, maxFeatures, minDist, out paramError))
            {
                SetVerdict(paramError, false);
                return;
            }

            // 取帧与 ROI 必须在 UI 线程上取（后台线程不碰控件与 Shapes 集合）
            Bitmap frame = viewer.Image;
            Rectangle roi = CurrentRoi();
            if (frame == null) return;

            ResetTrainResult();
            ClearFeatureLayer();
            SetBusy(true);
            SetVerdict("训练中…（在后台线程执行，界面不会卡）", true);

            Task.Run(() =>
            {
                // 训练器独占原生上下文：与正在跑的流程互不影响
                return _trainer.Train(frame, roi, _algorithm, contrast, maxFeatures, minDist);
            }).ContinueWith(t =>
            {
                if (IsDisposed || _disposedLocal) return;
                try
                {
                    BeginInvoke(new Action(() => OnTrainCompleted(t, contrast, maxFeatures, minDist, roi, frame)));
                }
                catch (Exception)
                {
                    // 窗体已关闭：结果直接丢弃（窗体都关了，报错也没人看）
                }
            });
        }

        private void OnTrainCompleted(Task<TemplateTrainResult> task,
                                      double contrast, int maxFeatures, double minDist,
                                      Rectangle roi, Bitmap frame)
        {
            SetBusy(false);

            if (task.IsFaulted)
            {
                Exception ex = task.Exception != null ? task.Exception.GetBaseException() : null;
                SetVerdict("训练异常：" + (ex != null ? ex.Message : "未知原因"), false);
                UpdateTrainButton();
                return;
            }

            TemplateTrainResult r = task.Result;
            _lastResult = r;

            if (r == null || !r.Success)
            {
                lblPoints.Text = r != null ? "特征点数：—" : "特征点数：—";
                lblModel.Text = "模型：—";
                lblElapsed.Text = r != null
                    ? string.Format(CultureInfo.InvariantCulture, "耗时：{0} ms", r.ElapsedMs)
                    : "耗时：—";
                SetVerdict(r != null ? r.Message : "训练失败：原生库无返回。", false);
                UpdateTrainButton();
                return;
            }

            // 成功：记下"这次训练用的帧 / ROI / 参数"，保存时要用它们裁模板图
            _lastRoi = roi;
            _lastFrameW = frame != null ? frame.Width : 0;
            _lastFrameH = frame != null ? frame.Height : 0;
            _trainedContrast = contrast;
            _trainedMaxFeatures = maxFeatures;
            _trainedMinFeatureDist = minDist;

            ReplaceCrop(CropForTemplate(frame, roi));

            lblPoints.Text = IsNcc
                ? "特征点数：—（NCC 不提取特征点）"
                : string.Format(CultureInfo.InvariantCulture, "特征点数：{0} 个", r.FeatureCount);
            lblModel.Text = r.ModelInfo != null
                ? string.Format(CultureInfo.InvariantCulture, "模型尺寸：{0:F0}×{1:F0}", r.ModelInfo.Width, r.ModelInfo.Height)
                : string.Format(CultureInfo.InvariantCulture, "模板尺寸：{0}×{1}",
                    _lastCrop != null ? _lastCrop.Width : 0, _lastCrop != null ? _lastCrop.Height : 0);
            lblElapsed.Text = string.Format(CultureInfo.InvariantCulture, "耗时：{0} ms", r.ElapsedMs);

            if (!IsNcc && r.Points != null && r.Points.Count > 0)
            {
                PointF center = roi.Width > 0
                    ? new PointF(roi.X + roi.Width * 0.5f, roi.Y + roi.Height * 0.5f)
                    : new PointF((frame != null ? frame.Width : 0) * 0.5f, (frame != null ? frame.Height : 0) * 0.5f);
                var layer = new ModelFeatureLayer(r.Points, center);
                viewer.Shapes.Add(layer);
                viewer.Shapes.NotifyChanged();
            }

            SetVerdict("训练成功：" + r.Message + " 满意就点「保存模板」。", true);
            UpdateTrainButton();
        }

        private void ResetTrainResult()
        {
            _lastResult = null;
            _lastRoi = Rectangle.Empty;
            _lastFrameW = 0;
            _lastFrameH = 0;
            if (_lastCrop != null) { _lastCrop.Dispose(); _lastCrop = null; }
            btnSave.Text = "保存模板";
            UpdateTrainButton();
        }

        private void ReplaceCrop(Bitmap crop)
        {
            if (_lastCrop != null) { _lastCrop.Dispose(); _lastCrop = null; }
            _lastCrop = crop;
        }

        /// <summary>裁出模板图；ROI 无效（整帧）时直接复制整帧。</summary>
        private static Bitmap CropForTemplate(Bitmap frame, Rectangle roi)
        {
            if (frame == null) return null;

            if (roi.Width <= 0 || roi.Height <= 0) return new Bitmap(frame);

            Rectangle real = Rectangle.Intersect(roi, new Rectangle(0, 0, frame.Width, frame.Height));
            if (real.Width <= 0 || real.Height <= 0) return new Bitmap(frame);

            return frame.Clone(real, frame.PixelFormat);
        }

        private void UpdateTrainButton()
        {
            bool canTrain = !_busy && viewer != null && viewer.Image != null;
            btnTrain.Enabled = canTrain;
            btnSave.Enabled = !_busy && _lastResult != null && _lastResult.Success && _lastCrop != null;
            btnLoadImage.Enabled = !_busy;
            btnGrabFrame.Enabled = !_busy;
            btnDrawRoi.Enabled = !_busy;

            // NCC 的训练参数恒禁用（没有特征点模型，这三个参数对它无意义）
            numContrast.Enabled = !_busy && !IsNcc;
            numMaxFeatures.Enabled = !_busy && !IsNcc;
            numMinDist.Enabled = !_busy && !IsNcc;
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
            UpdateTrainButton();
        }

        // =====================================================================
        //  保存
        // =====================================================================

        private void OnSave(object sender, EventArgs e)
        {
            if (_busy) return;
            if (_lastResult == null || !_lastResult.Success || _lastCrop == null)
            {
                SetVerdict("请先训练成功，再保存模板。", false);
                return;
            }

            bool exists = TemplateStore.Exists(_flowId, _nodeId);

            // 第 2 层确认：已有模板时覆盖是"不可撤销"的操作（旧模板直接被替换），必须问一次
            if (exists)
            {
                DialogResult ask = MessageBox.Show(this,
                    "该节点已有模板，保存会覆盖它（旧模板无法恢复）。确定覆盖吗？",
                    "覆盖模板", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
                if (ask != DialogResult.OK) return;
            }

            var meta = TemplateMeta.CreateDefault();
            meta.Algorithm = _algorithm;
            meta.SourceFrameWidth = _lastFrameW;
            meta.SourceFrameHeight = _lastFrameH;
            meta.TemplateX = _lastRoi.Width > 0 ? _lastRoi.X : 0;
            meta.TemplateY = _lastRoi.Width > 0 ? _lastRoi.Y : 0;
            meta.TemplateW = _lastRoi.Width > 0 ? _lastRoi.Width : 0;
            meta.TemplateH = _lastRoi.Height > 0 ? _lastRoi.Height : 0;
            meta.TemplateAngle = 0.0;
            meta.Contrast = _trainedContrast;
            meta.MaxFeatures = _trainedMaxFeatures;
            meta.MinFeatureDist = _trainedMinFeatureDist;
            meta.TrainSignature = TrainSignature(_trainedContrast, _trainedMaxFeatures, _trainedMinFeatureDist);
            meta.FeatureCount = _lastResult.FeatureCount;
            meta.TrainElapsedMs = _lastResult.ElapsedMs;
            meta.CreatedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);

            string message;
            bool ok = TemplateStore.Save(_flowId, _nodeId, _lastCrop, meta, out message);
            if (!ok)
            {
                SetVerdict("保存失败：" + message, false);
                return;
            }

            // 第 3 层确认：回读 → 用读回的图重训一次。这一步才是"下次开机能不能用"的真凭据。
            string verifyError;
            if (!VerifyRoundTrip(out verifyError))
            {
                TemplateStore.Delete(_flowId, _nodeId, out message);
                SetVerdict("保存后校验失败，已回滚（模板未生效）：" + verifyError, false);
                return;
            }

            SavedStamp = TemplateStore.StampOf(_flowId, _nodeId);
            SavedMessage = message;
            SavedRoi = _lastRoi;               // 宿主要把它写回节点，否则"刚存完就判不匹配"
            btnSave.Text = "重新保存";
            SetVerdict("保存成功并已通过回读校验：" + message, true);
            Logger.Info(string.Format(CultureInfo.InvariantCulture,
                "模板已保存：flow={0} node={1} algo={2} {3}", _flowId, _nodeId, _algorithm, message));
        }

        /// <summary>
        /// 回读校验：从磁盘读回 → 用读回的图重训一次。训练成功才算"能用的模板"。
        /// <para>为什么重训也要做：PNG 无损但"字节一致"不等于"像素语义一致"——
        /// 调色板 / 位深被换掉时字节会变（已被 SHA-256 挡住），而尺寸、格式都对但内容被
        /// 别的东西覆盖时，只有真训一次才能发现。</para>
        /// </summary>
        private bool VerifyRoundTrip(out string error)
        {
            error = null;

            TemplateLoadError loadError;
            string loadMessage;
            TemplateAsset asset = TemplateStore.Load(_flowId, _nodeId, out loadError, out loadMessage);
            if (asset == null)
            {
                error = loadMessage ?? TemplateStore.DescribeError(loadError);
                return false;
            }

            try
            {
                TemplateTrainResult r = _trainer.TrainFromTemplate(asset.Template, _algorithm,
                    _trainedContrast, _trainedMaxFeatures, _trainedMinFeatureDist);
                if (r == null || !r.Success)
                {
                    error = r != null ? r.Message : "原生库无返回";
                    return false;
                }
                return true;
            }
            finally
            {
                asset.Template.Dispose();
            }
        }

        /// <summary>训练参数签名：<c>"C=30|F=1500|D=2"</c>。只含训练参数，<strong>不含 ROI</strong>。</summary>
        public static string TrainSignature(double contrast, int maxFeatures, double minFeatureDist)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "C={0:0.###}|F={1}|D={2:0.###}", contrast, maxFeatures, minFeatureDist);
        }

        // =====================================================================
        //  杂项
        // =====================================================================

        private void OnCancel(object sender, EventArgs e)
        {
            Close();
        }

        private void SetVerdict(string text, bool ok)
        {
            lblVerdict.Text = text;
            lblVerdict.ForeColor = ok ? UiTheme.ColorOk : UiTheme.ColorNg;
        }

        private static string FormatUtc(string iso)
        {
            DateTime t;
            if (!string.IsNullOrEmpty(iso)
                && DateTime.TryParse(iso, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out t))
            {
                return t.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            }
            return iso ?? "";
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // 训练还没结束就关窗：直接关会在线程里用到已释放的控件
            if (_busy)
            {
                MessageBox.Show(this, "训练正在进行，请等它结束再关闭。", "创建模板",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                e.Cancel = true;
                return;
            }

            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _disposedLocal = true;

                // 独占的原生上下文必须归还：模板窗是模态的，漏了就是"开一次漏一个 GPU/CPU 上下文"
                if (_trainer != null)
                {
                    _trainer.Dispose();
                    _trainer = null;
                }
                if (_lastCrop != null)
                {
                    _lastCrop.Dispose();
                    _lastCrop = null;
                }
            }

            base.Dispose(disposing);
        }
    }
}
