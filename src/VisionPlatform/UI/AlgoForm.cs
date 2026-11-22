using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using VisionPlatform.Camera;
using VisionPlatform.Core;
using VisionPlatform.Native;
using VisionSystem.Controls;
using VisionSystem.Controls.Geometry;

namespace VisionPlatform.UI
{
    /// <summary>
    /// 形状匹配（基于边缘轮廓）算法页面。
    /// 流程：载入图像 → 框选模板区域 → 训练模型 → 设置角度 / 缩放 / 金字塔 / 搜索区域 → 执行匹配。
    /// 其余算法（Blob、清晰度、拟合等）后续再并入。
    /// </summary>
    public partial class AlgoForm : Form
    {
        private ImageViewer _viewer;
        private Bitmap _source;                 // 当前参与匹配的图像（窗体持有所有权）

        // 本窗体独占的算法上下文。不能用 AppContext.Algo：
        // 启动后主界面的循环检测/NCC 模板训练会复用那个上下文，形状模型是单槽位，
        // 调试页训练完到点「执行匹配」之间只要它被人用过，槽位里就不一定是刚训练的模型，
        // 现场表现为「软件启动后第一次匹配 0 个目标，再点一次又正常」。
        private VisionAlgo _algo;
        private ShapeModelInfo _modelInfo;
        private List<ShapeModelPoint> _features;   // 模板特征点（独立图层用）
        private DataTable _resultTable;            // 结果表格数据源（支持点列头排序）

        // 框选状态：0 = 未框选，1 = 正在框选模板区域，2 = 正在框选搜索区域
        private int _picking;

        public AlgoForm()
        {
            InitializeComponent();

            // 参数表的动态构建（含方法调用 / 循环 / 算术，设计器无法解析）已从 InitializeComponent
            // 移出；必须早于下面读取这些控件的 SyncParamToUi / UpdatePoseHint / 按钮 Kind 设置。
            BuildParamTable();

            CreateViewer();
            InitResultGrid();
            SyncParamToUi();
            UpdatePoseHint();

            // 统一深色工程配色（放在最后，业务语义色在运行时覆盖）
            UiStyle.ApplyForm(this);

            // 自绘深色标题栏：与顶部工具栏同色，系统原生标题栏不再出现
            UiTitleBar.Attach(this);

            // 语义色：取帧=蓝、执行匹配=绿、清除模型=红；其余保持中性（次要动作）
            btnOpenImage.Kind = UiButtonKind.Grab;
            btnGrab.Kind = UiButtonKind.Grab;
            btnMatch.Kind = UiButtonKind.Start;
            btnClearModel.Kind = UiButtonKind.Stop;

            ApplyButtonIcons();
        }

        /// <summary>
        /// 算法调试界面的按钮图标（左图标 + 右文字，由 <see cref="UiButton"/> 自绘）。
        /// 图标名见 <c>docs/设计/界面图标清单.md</c>；缺失时保持纯文字，不影响功能。
        /// </summary>
        private void ApplyButtonIcons()
        {
            btnOpenImage.Image = UiIcons.Get("ui-nav-algo", 16);      // 载入图像（工作台入口语义）
            btnGrab.Image = UiIcons.Get("ui-dev-camera-on", 16);      // 从相机取帧
            btnPickTemplate.Image = UiIcons.Get("ui-common-edit", 16); // 框选模板
            btnTemplateFull.Image = UiIcons.Get("ui-common-expand", 16);
            btnTrain.Image = UiIcons.Get("ui-train-start", 16);
            btnPickSearch.Image = UiIcons.Get("ui-common-search", 16);
            btnSearchFull.Image = UiIcons.Get("ui-common-expand", 16);
            btnMatch.Image = UiIcons.Get("ui-run-start", 16);
            btnClearModel.Image = UiIcons.Get("ui-common-delete", 16);
            btnFit.Image = UiIcons.Get("ui-flow-fit", 16);
        }

        /// <summary>
        /// 构建左侧参数表（动态部分）：所有 NumericUpDown / UiButton / Label / CheckBox 的创建、
        /// tableParams 的 RowCount / RowStyles、按行 AddSection / AddFull / AddRow 填充，
        /// 以及这些动态控件的属性与事件接线。
        /// </summary>
        /// <remarks>
        /// 这些代码含方法调用 / 循环 / 算术，VS WinForms 设计器无法解析，故从 InitializeComponent
        /// 移出，由构造函数在 InitializeComponent() 之后调用——控制创建顺序、加入 tableParams 的
        /// 顺序、属性与事件接线均与原 InitializeComponent 完全一致，运行期行为不变。
        /// </remarks>
        private void BuildParamTable()
        {
            // ---------- 图像来源 ----------
            this.btnOpenImage = new VisionPlatform.UI.UiButton();
            this.btnGrab = new VisionPlatform.UI.UiButton();

            // ---------- 模板区域 ----------
            this.numTplX = MakeNum(0, 65535, 0, 0);
            this.numTplY = MakeNum(0, 65535, 0, 0);
            this.numTplW = MakeNum(0, 65535, 0, 0);
            this.numTplH = MakeNum(0, 65535, 0, 0);
            this.btnPickTemplate = new VisionPlatform.UI.UiButton();
            this.btnTemplateFull = new VisionPlatform.UI.UiButton();

            // ---------- 模型训练 ----------
            this.numContrast = MakeNum(5, 200, 30, 0);
            this.numMaxFeatures = MakeNum(50, 8000, 1500, 0);
            this.numMinFeatureDist = MakeNum(1, 20, 2, 1);
            this.btnTrain = new VisionPlatform.UI.UiButton();
            this.lblModelInfo = new System.Windows.Forms.Label();
            this.chkShowFeatures = new System.Windows.Forms.CheckBox();

            // ---------- 搜索参数 ----------
            this.numAngleStart = MakeNum(-360, 360, -10, 1);
            this.numAngleEnd = MakeNum(-360, 360, 10, 1);
            this.numAngleStep = MakeNum(0.1m, 45, 1, 1);
            this.numScaleMin = MakeNum(0.1m, 10, 1, 2);
            this.numScaleMax = MakeNum(0.1m, 10, 1, 2);
            this.numScaleStep = MakeNum(0, 2, 0, 2);
            this.numPyramid = MakeNum(1, 8, 4, 0);
            this.numMinScore = MakeNum(0, 1, 0.7m, 2);
            this.numMaxMatches = MakeNum(1, 100, 1, 0);
            this.numOverlap = MakeNum(0, 2, 0.5m, 2);
            this.numGreediness = MakeNum(0, 1, 0.9m, 2);
            this.chkSubpixel = new System.Windows.Forms.CheckBox();
            this.chkSymmetric = new System.Windows.Forms.CheckBox();
            this.lblPoseHint = new System.Windows.Forms.Label();

            // ---------- 搜索区域 ----------
            this.numSrchX = MakeNum(0, 65535, 0, 0);
            this.numSrchY = MakeNum(0, 65535, 0, 0);
            this.numSrchW = MakeNum(0, 65535, 0, 0);
            this.numSrchH = MakeNum(0, 65535, 0, 0);
            this.btnPickSearch = new VisionPlatform.UI.UiButton();
            this.btnSearchFull = new VisionPlatform.UI.UiButton();

            // ---------- 执行 ----------
            this.btnMatch = new VisionPlatform.UI.UiButton();
            this.btnClearModel = new VisionPlatform.UI.UiButton();
            this.btnFit = new VisionPlatform.UI.UiButton();

            this.tableParams.SuspendLayout();
            this.tableParams.RowCount = kParamRows;
            for (int i = 0; i < kParamRows; i++)
                this.tableParams.RowStyles.Add(new System.Windows.Forms.RowStyle(
                    System.Windows.Forms.SizeType.Absolute, (float)kRowHeight));

            int r = 0;

            // ---------- 图像来源 ----------
            AddSection("图像来源", r++);
            AddFull(this.btnOpenImage, r++);
            AddFull(this.btnGrab, r++);

            // ---------- 模板区域 ----------
            AddSection("模板区域（训练用）", r++);
            AddRow("X", this.numTplX, r++);
            AddRow("Y", this.numTplY, r++);
            AddRow("宽度", this.numTplW, r++);
            AddRow("高度", this.numTplH, r++);
            AddFull(this.btnPickTemplate, r++);
            AddFull(this.btnTemplateFull, r++);

            // ---------- 模型训练 ----------
            AddSection("模型训练", r++);
            AddRow("边缘对比度", this.numContrast, r++);
            AddRow("最多特征点数", this.numMaxFeatures, r++);
            AddRow("特征点最小间距", this.numMinFeatureDist, r++);
            AddFull(this.btnTrain, r++);
            AddFull(this.lblModelInfo, r++);
            AddFull(this.chkShowFeatures, r++);

            // ---------- 搜索参数 ----------
            AddSection("搜索参数", r++);
            AddRow("起始角度(°)", this.numAngleStart, r++);
            AddRow("结束角度(°)", this.numAngleEnd, r++);
            AddRow("角度步长(°)", this.numAngleStep, r++);
            AddRow("最小缩放", this.numScaleMin, r++);
            AddRow("最大缩放", this.numScaleMax, r++);
            AddRow("缩放步长(0=不缩放)", this.numScaleStep, r++);
            AddRow("金字塔层数", this.numPyramid, r++);
            AddRow("最小得分", this.numMinScore, r++);
            AddRow("最多目标数", this.numMaxMatches, r++);
            AddRow("重叠抑制阈值", this.numOverlap, r++);
            AddRow("贪婪系数(大=快)", this.numGreediness, r++);
            AddFull(this.chkSubpixel, r++);
            AddFull(this.chkSymmetric, r++);
            AddFull(this.lblPoseHint, r++);

            // ---------- 搜索区域 ----------
            AddSection("搜索区域（限制中心）", r++);
            AddRow("X", this.numSrchX, r++);
            AddRow("Y", this.numSrchY, r++);
            AddRow("宽度(0=整图)", this.numSrchW, r++);
            AddRow("高度(0=整图)", this.numSrchH, r++);
            AddFull(this.btnPickSearch, r++);
            AddFull(this.btnSearchFull, r++);

            // ---------- 执行 ----------
            AddSection("执行", r++);
            AddFull(this.btnMatch, r++);
            AddFull(this.btnClearModel, r++);
            AddFull(this.btnFit, r++);

            // ---------- 控件属性 ----------
            this.btnOpenImage.Text = "打开本地图片";
            this.btnOpenImage.Click += new System.EventHandler(this.OnOpenImage);

            this.btnGrab.Text = "从相机取帧";
            this.btnGrab.Click += new System.EventHandler(this.OnGrab);

            this.btnPickTemplate.Text = "在图上框选模板";
            this.btnPickTemplate.Click += new System.EventHandler(this.OnPickTemplate);

            this.btnTemplateFull.Text = "使用整图作为模板";
            this.btnTemplateFull.Click += new System.EventHandler(this.OnTemplateFull);

            this.btnTrain.Text = "训练形状模型";
            this.btnTrain.Click += new System.EventHandler(this.OnTrain);

            this.lblModelInfo.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblModelInfo.ForeColor = System.Drawing.Color.Gold;
            this.lblModelInfo.Text = "尚未训练模型";
            this.lblModelInfo.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            this.chkShowFeatures.Text = "显示模板特征点（橙色图层）";
            this.chkShowFeatures.Checked = true;
            this.chkShowFeatures.CheckedChanged += new System.EventHandler(this.OnToggleFeatures);

            this.chkSubpixel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.chkSubpixel.ForeColor = System.Drawing.Color.Gainsboro;
            this.chkSubpixel.Text = "亚像素精化（更慢更准）";
            this.chkSubpixel.Checked = true;

            this.chkSymmetric.Dock = System.Windows.Forms.DockStyle.Fill;
            this.chkSymmetric.ForeColor = System.Drawing.Color.Gainsboro;
            this.chkSymmetric.Text = "对称双向评分（类似形状更稳，更慢）";
            this.chkSymmetric.Checked = false;

            this.lblPoseHint.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblPoseHint.ForeColor = System.Drawing.Color.Gray;
            this.lblPoseHint.Text = "";
            this.lblPoseHint.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            this.btnPickSearch.Text = "在图上框选搜索区域";
            this.btnPickSearch.Click += new System.EventHandler(this.OnPickSearch);

            this.btnSearchFull.Text = "搜索整图";
            this.btnSearchFull.Click += new System.EventHandler(this.OnSearchFull);

            this.btnMatch.Text = "执行匹配";
            this.btnMatch.Click += new System.EventHandler(this.OnMatch);

            this.btnClearModel.Text = "清除模型";
            this.btnClearModel.Click += new System.EventHandler(this.OnClearModel);

            this.btnFit.Text = "适应窗口";
            this.btnFit.Click += new System.EventHandler(this.OnFit);

            this.tableParams.ResumeLayout(false);
        }

        /// <summary>
        /// 结果表格：用 DataTable 做数据源，列头可点击排序（数值列按数值大小排，不是字符串）。
        /// </summary>
        private void InitResultGrid()
        {
            _resultTable = new DataTable();
            _resultTable.Columns.Add("序号", typeof(int));
            _resultTable.Columns.Add("X", typeof(double));
            _resultTable.Columns.Add("Y", typeof(double));
            _resultTable.Columns.Add("角度(°)", typeof(double));
            _resultTable.Columns.Add("缩放", typeof(double));
            _resultTable.Columns.Add("得分", typeof(double));

            gridResults.DataSource = _resultTable;

            foreach (DataGridViewColumn col in gridResults.Columns)
                col.SortMode = DataGridViewColumnSortMode.Automatic;
        }

        /// <summary>显示控件在构造函数中创建（ImageViewer 是自定义控件，需在代码中实例化）。</summary>
        private void CreateViewer()
        {
            _viewer = new ImageViewer();
            _viewer.Dock = DockStyle.Fill;
            _viewer.ShowHud = true;
            _viewer.Options.ShowCrosshair = true;
            _viewer.ShapeCreated += OnViewerShapeCreated;

            panelRight.Controls.Add(_viewer);
            // Fill 控件要放在集合最前（z-order 顶层），才会最后参与 dock 并占据剩余区域
            panelRight.Controls.SetChildIndex(_viewer, 0);
        }

        #region 参数读写

        private void SyncParamToUi()
        {
            ShapeParam p = ShapeParam.Default();
            numAngleStart.Value = (decimal)p.AngleStart;
            numAngleEnd.Value = (decimal)p.AngleEnd;
            numAngleStep.Value = (decimal)p.AngleStep;
            numScaleMin.Value = (decimal)p.ScaleMin;
            numScaleMax.Value = (decimal)p.ScaleMax;
            numScaleStep.Value = (decimal)p.ScaleStep;
            numPyramid.Value = p.PyramidLevels;
            numMinScore.Value = (decimal)p.MinScore;
            numMaxMatches.Value = p.MaxMatches;
            numOverlap.Value = (decimal)p.MaxOverlap;
            numGreediness.Value = (decimal)p.Greediness;
            chkSubpixel.Checked = p.Subpixel;
            chkSymmetric.Checked = p.Symmetric;
        }

        private ShapeParam BuildParam()
        {
            var p = new ShapeParam();
            p.AngleStart = (double)numAngleStart.Value;
            p.AngleEnd = (double)numAngleEnd.Value;
            p.AngleStep = (double)numAngleStep.Value;
            p.ScaleMin = (double)numScaleMin.Value;
            p.ScaleMax = (double)numScaleMax.Value;
            p.ScaleStep = (double)numScaleStep.Value;
            p.PyramidLevels = (int)numPyramid.Value;
            p.MinScore = (double)numMinScore.Value;
            p.MaxMatches = (int)numMaxMatches.Value;
            p.MaxOverlap = (double)numOverlap.Value;
            p.Subpixel = chkSubpixel.Checked;
            p.Greediness = (double)numGreediness.Value;
            p.Symmetric = chkSymmetric.Checked;
            return p;
        }

        /// <summary>模板区域；宽高为 0 时表示整图。</summary>
        private Rectangle TemplateRect
        {
            get
            {
                return new Rectangle((int)numTplX.Value, (int)numTplY.Value,
                                     (int)numTplW.Value, (int)numTplH.Value);
            }
        }

        /// <summary>搜索区域（限制模板中心）；宽高为 0 时表示整图。</summary>
        private Rectangle SearchRect
        {
            get
            {
                return new Rectangle((int)numSrchX.Value, (int)numSrchY.Value,
                                     (int)numSrchW.Value, (int)numSrchH.Value);
            }
        }

        private void UpdatePoseHint()
        {
            int poses = BuildParam().PoseCount;
            lblPoseHint.Text = string.Format("需评估 {0} 个姿态（角度 × 缩放）", poses);
            lblPoseHint.ForeColor = poses > 200 ? Color.Orange : Color.Gray;
        }

        private static void SetNum(NumericUpDown n, decimal v)
        {
            if (v < n.Minimum) v = n.Minimum;
            if (v > n.Maximum) v = n.Maximum;
            n.Value = v;
        }

        #endregion

        #region 图像来源

        /// <summary>打开本地图片进行离线调试（无需相机）。</summary>
        private void OnOpenImage(object sender, EventArgs e)
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "选择测试图片";
                dlg.Filter = "图像文件|*.bmp;*.jpg;*.jpeg;*.png;*.tif;*.tiff|所有文件|*.*";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    // 先读入内存再解码：不占用文件句柄，超大图也更稳
                    byte[] bytes = File.ReadAllBytes(dlg.FileName);
                    Bitmap src;
                    using (MemoryStream ms = new MemoryStream(bytes))
                    {
                        src = new Bitmap(ms);
                    }

                    Logger.Info("载入本地图片：" + dlg.FileName +
                                string.Format("（{0}x{1}）", src.Width, src.Height));
                    SetSource(src);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "打开图片失败：" + ex.Message, "错误",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void OnGrab(object sender, EventArgs e)
        {
            ICamera cam = CameraManager.Instance.Current;
            if (cam == null || !cam.IsOpen)
            {
                MessageBox.Show(this, "没有已打开的相机。请先用主界面的「相机管理」打开相机，或直接用「打开本地图片」。");
                return;
            }

            var fr = cam.GrabOne();
            if (!fr.Success) { MessageBox.Show(this, "取帧失败：" + fr.Message); return; }

            SetSource(fr.Value);
        }

        /// <summary>接管图像所有权：一份给控件显示，一份留给算法使用。</summary>
        private void SetSource(Bitmap bmp)
        {
            DisposeSource();

            // 换训练帧即作废旧模型：本地状态与原生槽位必须一起失效。
            // 只清 _modelInfo 不清原生槽位，会在「换图后没重训就点匹配」时拿到上一张图的模型。
            if (_algo != null && _modelInfo != null) _algo.ClearShapeModel();
            _modelInfo = null;
            _features = null;
            lblModelInfo.ForeColor = Color.Gold;
            lblModelInfo.Text = "尚未训练模型";

            _source = bmp;

            _viewer.SetImage((Bitmap)bmp.Clone(), true);

            // 默认模板 = 整图，搜索区域 = 整图，便于立刻训练
            SetNum(numTplX, 0);
            SetNum(numTplY, 0);
            SetNum(numTplW, bmp.Width);
            SetNum(numTplH, bmp.Height);
            SetNum(numSrchX, 0);
            SetNum(numSrchY, 0);
            SetNum(numSrchW, 0);
            SetNum(numSrchH, 0);

            bool color = IsColorImage(bmp);
            RedrawOverlays();
            lblVerdict.ForeColor = Color.Silver;
            lblVerdict.Text = string.Format("已载入{0} {1} x {2}{3}",
                color ? "彩色图" : "灰度图", bmp.Width, bmp.Height,
                color ? "（算法内部自动转灰度处理）" : "");
        }

        /// <summary>判断是否彩色图（算法库内部会统一转灰度，这里只用于界面提示）。</summary>
        private static bool IsColorImage(Bitmap bmp)
        {
            switch (bmp.PixelFormat)
            {
                case PixelFormat.Format24bppRgb:
                case PixelFormat.Format32bppRgb:
                case PixelFormat.Format32bppArgb:
                case PixelFormat.Format32bppPArgb:
                    return true;
                default:
                    return false;
            }
        }

        private void DisposeSource()
        {
            if (_source != null)
            {
                _source.Dispose();
                _source = null;
            }
        }

        #endregion

        #region ROI 框选

        private void StartPicking(int mode)
        {
            if (_source == null)
            {
                MessageBox.Show(this, "请先载入图像。");
                return;
            }

            _picking = mode;
            _viewer.Mode = InteractionMode.DrawRectangle;
            lblVerdict.ForeColor = Color.DeepSkyBlue;
            lblVerdict.Text = mode == 1 ? "请在图像上拖拽，框选模板区域" : "请在图像上拖拽，框选搜索区域";
        }

        private void OnPickTemplate(object sender, EventArgs e) { StartPicking(1); }

        private void OnPickSearch(object sender, EventArgs e) { StartPicking(2); }

        private void OnTemplateFull(object sender, EventArgs e)
        {
            if (_source == null) { MessageBox.Show(this, "请先载入图像。"); return; }
            SetNum(numTplX, 0);
            SetNum(numTplY, 0);
            SetNum(numTplW, _source.Width);
            SetNum(numTplH, _source.Height);
            RedrawOverlays();
        }

        private void OnSearchFull(object sender, EventArgs e)
        {
            SetNum(numSrchX, 0);
            SetNum(numSrchY, 0);
            SetNum(numSrchW, 0);
            SetNum(numSrchH, 0);
            RedrawOverlays();
        }

        private void OnViewerShapeCreated(object sender, ShapeEventArgs e)
        {
            int mode = _picking;
            if (mode == 0) return;              // 用户自由绘制的图形不处理

            _picking = 0;
            _viewer.Mode = InteractionMode.Select;

            RectangleShape rect = e.Shape as RectangleShape;
            if (rect == null) return;

            var r = new Rectangle((int)Math.Round(rect.X), (int)Math.Round(rect.Y),
                                  (int)Math.Round(rect.Width), (int)Math.Round(rect.Height));
            if (r.Width < 2 || r.Height < 2) return;

            if (mode == 1)
            {
                SetNum(numTplX, r.X);
                SetNum(numTplY, r.Y);
                SetNum(numTplW, r.Width);
                SetNum(numTplH, r.Height);
            }
            else
            {
                SetNum(numSrchX, r.X);
                SetNum(numSrchY, r.Y);
                SetNum(numSrchW, r.Width);
                SetNum(numSrchH, r.Height);
            }

            RedrawOverlays();
            lblVerdict.ForeColor = Color.Silver;
            lblVerdict.Text = mode == 1 ? "已设置模板区域" : "已设置搜索区域";
        }

        private void OnToggleFeatures(object sender, EventArgs e)
        {
            RedrawOverlays();
        }

        /// <summary>
        /// 重绘叠加层。各图层用不同颜色区分：
        /// 金色 = 模板 ROI，青色 = 搜索区域，橙色 = 模板特征点（独立图层），绿色 = 匹配结果。
        /// </summary>
        private void RedrawOverlays()
        {
            if (_viewer == null) return;
            _viewer.ClearShapes();

            Rectangle t = TemplateRect;

            // 1) 模板特征点（先画，作为底下的独立图层）
            if (chkShowFeatures.Checked && _features != null && _features.Count > 0
                && t.Width > 0 && t.Height > 0)
            {
                var center = new PointF(t.X + t.Width / 2f, t.Y + t.Height / 2f);
                var layer = new ModelFeatureLayer(_features, center);
                _viewer.Shapes.Add(layer);
            }

            // 2) 模板 ROI
            if (t.Width > 0 && t.Height > 0)
            {
                var rs = new RectangleShape(t.X, t.Y, t.Width, t.Height);
                rs.StrokeColor = Color.Gold;
                rs.LineWidth = 2f;
                _viewer.Shapes.Add(rs);
            }

            // 3) 搜索区域
            Rectangle s = SearchRect;
            if (s.Width > 0 && s.Height > 0)
            {
                var rs = new RectangleShape(s.X, s.Y, s.Width, s.Height);
                rs.StrokeColor = Color.Cyan;
                rs.LineWidth = 1.5f;
                _viewer.Shapes.Add(rs);
            }
        }

        #endregion

        #region 训练与匹配

        /// <summary>
        /// 取本窗体独占的算法上下文（<see cref="FlowRunManager"/>/主界面循环检测用的上下文不能共用）。
        /// </summary>
        private VisionAlgo EnsureAlgo()
        {
            if (_algo != null) return _algo;

            try
            {
                _algo = new VisionAlgo();
                return _algo;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    "算法库不可用：" + ex.Message + Environment.NewLine +
                    "请确认 VisionAlgo.Native.dll 与 opencv_world455 DLL 已复制到输出目录。",
                    "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }
        }

        private void OnTrain(object sender, EventArgs e)
        {
            VisionAlgo algo = EnsureAlgo();
            if (algo == null) return;

            if (_source == null) { MessageBox.Show(this, "请先载入图像。"); return; }

            // 特征点数与稀疏间距直接决定速度与稳定性，交给界面可调
            var train = new ShapeTrainParam
            {
                Contrast = (double)numContrast.Value,
                MaxFeatures = (int)numMaxFeatures.Value,
                MinFeatureDist = (double)numMinFeatureDist.Value
            };

            // 清空本上下文旧模型再训练：单槽位不允许"上一次训练残留"参与这次匹配。
            // 同时清掉本线程可能残留的算法软超时（流程节点设置的是它自己的线程，这里不影响流程）。
            VisionAlgo.SetDeadline(0);
            algo.ClearShapeModel();

            var r = algo.CreateShapeModel(_source, TemplateRect, train);
            if (!r.Success)
            {
                _modelInfo = null;
                lblModelInfo.ForeColor = Color.OrangeRed;
                lblModelInfo.Text = "训练失败";
                MessageBox.Show(this, "训练失败：" + r.Message, "形状模型",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _modelInfo = r.Value;

            // 取回特征点用于「模板特征」图层
            var fp = algo.GetShapeModelPoints();
            _features = fp.Success ? fp.Value : null;

            lblModelInfo.ForeColor = Color.Gold;
            lblModelInfo.Text = (_features != null)
                ? string.Format("{0}，特征点 {1} 个", r.Value, _features.Count)
                : r.Value.ToString();

            lblVerdict.ForeColor = Color.LimeGreen;
            lblVerdict.Text = "模型训练完成 —— " + r.Value;

            RedrawOverlays();
            Logger.Info("形状模型训练完成：" + r.Value);
        }

        private void OnClearModel(object sender, EventArgs e)
        {
            VisionAlgo algo = EnsureAlgo();
            if (algo == null) return;

            var r = algo.ClearShapeModel();
            _modelInfo = null;
            _features = null;
            lblModelInfo.ForeColor = Color.Gold;
            lblModelInfo.Text = "尚未训练模型";
            lblVerdict.ForeColor = Color.Silver;
            lblVerdict.Text = r.Success ? "已清除形状模型" : r.Message;
        }

        private void OnMatch(object sender, EventArgs e)
        {
            VisionAlgo algo = EnsureAlgo();
            if (algo == null) return;

            if (_source == null) { MessageBox.Show(this, "请先载入图像。"); return; }

            // 未训练时给出提示（句柄里没有模型会直接返回 VA_ERR_NO_TEMPLATE）
            if (_modelInfo == null)
            {
                MessageBox.Show(this, "尚未训练形状模型。请先框选模板区域并点击「训练形状模型」。",
                    "形状匹配", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // 与训练侧对称：清掉本线程残留软超时，并复查原生槽位确实还装着刚训练的模型。
            // 复查失败时把本地状态一并作废，避免"界面显示已训练、原生却是空槽"的假成功。
            VisionAlgo.SetDeadline(0);
            var nativeInfo = algo.GetShapeModelInfo();
            if (!nativeInfo.Success || nativeInfo.Value == null || nativeInfo.Value.PointCount <= 0)
            {
                _modelInfo = null;
                _features = null;
                lblModelInfo.ForeColor = Color.OrangeRed;
                lblModelInfo.Text = "模型已失效";
                lblVerdict.ForeColor = Color.OrangeRed;
                lblVerdict.Text = "匹配失败：原生槽位里没有有效模型，请重新训练形状模型";
                return;
            }

            ShapeParam p = BuildParam();

            var sw = Stopwatch.StartNew();
            var r = algo.FindShapeModel(_source, p, SearchRect);
            sw.Stop();

            RedrawOverlays();

            if (!r.Success)
            {
                lblVerdict.ForeColor = Color.OrangeRed;
                lblVerdict.Text = "匹配失败：" + r.Message;
                return;
            }

            List<ShapeMatchResult> matches = r.Value;

            // 结果叠加：绿色旋转矩形（与金色模板 ROI 明显区分）+ 中心十字标记
            foreach (ShapeMatchResult m in matches)
            {
                var rr = new RotatedRectShape(new PointF((float)m.Cx, (float)m.Cy),
                                              (float)m.Width, (float)m.Height, (float)m.Angle);
                rr.StrokeColor = Color.Lime;
                rr.LineWidth = 2.5f;
                _viewer.Shapes.Add(rr);

                // 中心十字：用两个细长旋转矩形拼出，标记匹配中心
                var crossH = new RotatedRectShape(new PointF((float)m.Cx, (float)m.Cy), 12f, 0.8f, (float)m.Angle);
                crossH.StrokeColor = Color.Lime;
                crossH.LineWidth = 1.5f;
                _viewer.Shapes.Add(crossH);

                var crossV = new RotatedRectShape(new PointF((float)m.Cx, (float)m.Cy), 0.8f, 12f, (float)m.Angle);
                crossV.StrokeColor = Color.Lime;
                crossV.LineWidth = 1.5f;
                _viewer.Shapes.Add(crossV);
            }

            // 结果表格（可点列头排序）
            _resultTable.Rows.Clear();
            for (int i = 0; i < matches.Count; i++)
            {
                ShapeMatchResult m = matches[i];
                _resultTable.Rows.Add(
                    i + 1,
                    Math.Round(m.Cx, 2),
                    Math.Round(m.Cy, 2),
                    Math.Round(m.Angle, 2),
                    Math.Round(m.Scale, 3),
                    Math.Round(m.Score, 4));
            }

            const string legend = "　绿=匹配结果　金=模板ROI　青=搜索区　橙=特征点";

            if (matches.Count > 0)
            {
                lblVerdict.ForeColor = Color.LimeGreen;
                lblVerdict.Text = string.Format("匹配成功：{0} 个目标，最高得分 {1:F3}，耗时 {2} ms{3}",
                    matches.Count, matches[0].Score, sw.ElapsedMilliseconds, legend);
            }
            else
            {
                lblVerdict.ForeColor = Color.Orange;
                lblVerdict.Text = string.Format("未找到满足得分 {0:F2} 的目标（耗时 {1} ms）{2}",
                    p.MinScore, sw.ElapsedMilliseconds, legend);
            }

            Logger.Info("形状匹配：" + p + " → " + matches.Count + " 个目标，" + sw.ElapsedMilliseconds + " ms");
        }

        private void OnFit(object sender, EventArgs e)
        {
            if (_viewer != null) _viewer.FitToWindow();
        }

        #endregion

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            DisposeSource();

            // 关闭调试页即归还独占上下文；不要碰 AppContext.Algo。
            ReleaseAlgoContext();
        }

        /// <summary>
        /// 释放本窗体独占的算法上下文（幂等）。两条路径都会走到：
        /// <list type="bullet">
        /// <item>用户点关闭 → <see cref="OnFormClosing"/>；</item>
        /// <item>程序化 Dispose（窗体从未 Show 时 Close 即 Dispose）→ Designer 的 Dispose(bool)。</item>
        /// </list>
        /// </summary>
        private void ReleaseAlgoContext()
        {
            if (_algo == null) return;

            try
            {
                _algo.Dispose();
            }
            catch (Exception)
            {
                // 原生上下文释放失败不阻断关闭流程
            }
            _algo = null;
        }
    }
}
