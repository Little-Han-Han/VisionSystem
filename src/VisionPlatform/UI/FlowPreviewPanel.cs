using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using VisionPlatform.Core;
using VisionPlatform.Flow;
using VisionSystem.Controls;
using VisionSystem.Controls.Geometry;

namespace VisionPlatform.UI
{
    // FlowPreviewMode 枚举已移到本文件末尾——VS 要求「可设计类必须是文件中的第一个类型」，
    // 它原先排在 FlowPreviewPanel 之前会导致设计器无法打开本控件。

    /// <summary>
    /// 流程预览区：顶条（显示模式下拉 / 载入图片 / 节点名 / 第 n/m / 耗时 / 暂停自动刷新）+
    /// 图像视图 + 左上角 OK/NG 文本覆盖标签 + 33 ms 拉取定时器（覆盖式，旧帧即时释放）。
    ///
    /// 除运行时拉帧外，还提供「载入图片」入口与 <see cref="ShowStaticImage"/>：
    /// 不运行流程也能看图（静态图同样交给视图持有，见下）。
    ///
    /// 【帧所有权】从控制器 <c>TryTakePending()</c> 取到的帧归本控件所有：交给
    /// <see cref="ImageViewer.SetImage"/> 后由视图持有（<c>OwnsImage=true</c>），
    /// 视图换图时自行释放旧帧，本控件不再重复释放。
    /// </summary>
    public sealed partial class FlowPreviewPanel : UserControl
    {
        /// <summary>拉取周期（ms）：预览由定时器拉取，绝不用 BeginInvoke 逐帧推送。</summary>
        /// <remarks>InitializeComponent 中 pullTimer.Interval 用字面量 33（设计器不解析 const 标识符），改此值需同步。</remarks>
        private const int PullIntervalMs = 33;

        /// <summary>ROI 最小边长（图像像素）：小于此值视为误操作。</summary>
        public const float MinRoiSizePx = 4f;

        private FlowRunController _controller;
        private string _selectedNodeId = "";
        private bool _paused;
        private bool _hasRun;
        private bool _staticShown;      // 当前显示的是「载入图片」的静态图（切换模式时不被 Reset 清掉）

        private bool _roiPicking;                                   // 是否处于「画几何」拖框状态
        private InteractionMode _modeBeforeRoi = InteractionMode.Select;
        private string _infoBeforeRoi = "";                         // 进入拖框前的顶条文本，退出时还原
        private DrawSpec _drawSpec;                                 // 当前要画的几何（矩形/直线/圆环）；null 视为矩形（兼容旧 BeginRoiPick）

        // 旋转矩形的「编辑态」：画完不退出，用户可继续拖 8 个手柄改尺寸/角度（FR-07）。
        // 编辑期间必须保持 _roiPicking = true —— 否则 OnPullTick 会用运行帧把 ROI 冲掉。
        private bool _editingRotated;
        private RotatedRectShape _editShape;                        // 编辑中的那个实例（就在 viewer.Shapes 里）

        // FR-12：选中节点时叠加的 ROI 轮廓。ShowFrame 每 33 ms ClearShapes + 重新 Add，
        // 因此这里存的是「模板」，真图形每帧重放（与 _hlNodeId 的高亮机制同理）。
        private RotatedRectShape _roiOutline;
        private string _roiOutlineLabel = "";
        private RotatedRectShape _roiOutlineShape;                  // 当前已加进 viewer 的那一个

        // 结果表格点行后要突出的目标。ShowFrame 每 33ms 会 ClearShapes + 重新 Add，
        // 高亮会被冲掉，故记在这里、每次 ShowFrame 末尾重新施加（见 ShowFrame）。
        //
        // 【为什么不是图形实例】每个运行周期 FlowNodeProgress.Shapes 都被重建：
        // FlowShapes.MatchBox → new RotatedRectShape、OcrBox → new RectangleShape，每周期都是新实例，
        // 存实例引用必然在下一帧失效（连续运行下高亮只闪一下就没了）。
        // 故高亮身份改用「节点 Id + 该节点 Shapes 列表内的序号」：同一节点在同一流程里
        // 每周期产出图形的顺序是确定的（检测框按分数、字符按行列排序），序号跨周期稳定。
        private string _hlNodeId = "";    // 高亮所属节点 Id；空 = 无高亮
        private int _hlIndex = -1;        // 在该节点 Shapes 列表内的序号；-1 = 无高亮
        private string _curNodeId = "";   // 当前快照的节点 Id
        private bool _curNodeOwnShapes;   // 当前帧画的是「该节点自身图形」；false = 链尾全链图形，序号与该节点对不上

        private Panel panelTop;
        private ComboBox cmbMode;
        private Label lblInfo;
        private CheckBox chkPauseAuto;
        private CheckBox chkContour;
        private UiButton btnDrawRoi;
        private UiButton btnLabelDetail;
        private UiButton btnLoadImage;

        /// <summary>当前帧要画的两组图形：检测目标 + 辅助几何（ROI 轮廓 / OCR 区域）。</summary>
        private List<IGeometryShape> _curShapes = new List<IGeometryShape>();

        // 节点参数推导出的「布局预览」（卡尺扫描线 + 投影窗口等）：
        // 与运行结果分开存，运行开始时清掉，避免把"示意"当成"实测"。
        private List<IGeometryShape> _layoutShapes = new List<IGeometryShape>();
        private List<IGeometryShape> _curAux = new List<IGeometryShape>();

        /// <summary>「轮廓」开关：只控制标记为 <see cref="FlowShapes.AuxContourName"/> 的辅助几何。</summary>
        private bool _showContour = true;

        private Panel panelView;
        private ImageViewer viewer;
        private Label lblEmpty;
        private Label lblVerdict;
        private Label lblSummary;
        private System.ComponentModel.IContainer components;
        private ToolTip toolTip;
        private Timer pullTimer;

        public FlowPreviewPanel()
        {
            InitializeComponent();
            ApplyTheme();

            // 标注详细度是全局偏好：启动时从配置取一次，按钮文案跟着走
            ShapeLabelStyle.Reload();
            UpdateLabelDetailButton();
            _showContour = (chkContour == null) || chkContour.Checked;
        }

        #region 控件构建

        /// <summary>构建控件树（纯代码，不使用设计器序列化）。</summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.panelTop = new System.Windows.Forms.Panel();
            this.lblInfo = new System.Windows.Forms.Label();
            this.chkPauseAuto = new System.Windows.Forms.CheckBox();
            this.chkContour = new System.Windows.Forms.CheckBox();
            this.btnDrawRoi = new VisionPlatform.UI.UiButton();
            this.btnLabelDetail = new VisionPlatform.UI.UiButton();
            this.btnLoadImage = new VisionPlatform.UI.UiButton();
            this.cmbMode = new System.Windows.Forms.ComboBox();
            this.panelView = new System.Windows.Forms.Panel();
            this.viewer = new VisionSystem.Controls.ImageViewer();
            this.lblEmpty = new System.Windows.Forms.Label();
            this.lblVerdict = new System.Windows.Forms.Label();
            this.lblSummary = new System.Windows.Forms.Label();
            this.toolTip = new System.Windows.Forms.ToolTip(this.components);
            this.pullTimer = new System.Windows.Forms.Timer(this.components);
            this.panelTop.SuspendLayout();
            this.panelView.SuspendLayout();
            this.SuspendLayout();
            // 
            // panelTop
            // 
            this.panelTop.Controls.Add(this.lblInfo);
            this.panelTop.Controls.Add(this.chkPauseAuto);
            this.panelTop.Controls.Add(this.chkContour);
            this.panelTop.Controls.Add(this.btnDrawRoi);
            this.panelTop.Controls.Add(this.btnLabelDetail);
            this.panelTop.Controls.Add(this.btnLoadImage);
            this.panelTop.Controls.Add(this.cmbMode);
            this.panelTop.Dock = System.Windows.Forms.DockStyle.Top;
            this.panelTop.Location = new System.Drawing.Point(0, 0);
            this.panelTop.Name = "panelTop";
            this.panelTop.Padding = new System.Windows.Forms.Padding(4, 2, 4, 2);
            this.panelTop.Size = new System.Drawing.Size(640, 28);
            this.panelTop.TabIndex = 3;
            // 
            // lblInfo
            // 
            this.lblInfo.AutoEllipsis = true;
            this.lblInfo.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblInfo.Location = new System.Drawing.Point(112, 2);
            this.lblInfo.Name = "lblInfo";
            this.lblInfo.Size = new System.Drawing.Size(232, 24);
            this.lblInfo.TabIndex = 0;
            this.lblInfo.Text = "未运行";
            this.lblInfo.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // chkPauseAuto
            // 
            this.chkPauseAuto.Dock = System.Windows.Forms.DockStyle.Right;
            this.chkPauseAuto.Location = new System.Drawing.Point(344, 2);
            this.chkPauseAuto.Name = "chkPauseAuto";
            // 文案压到 4 个字：顶条现在要多放「轮廓」「标注·标准」两个开关，
            // 保留 116px 会把中间的节点信息（lblInfo）挤到只剩几十像素。
            this.chkPauseAuto.Size = new System.Drawing.Size(88, 24);
            this.chkPauseAuto.TabIndex = 1;
            this.chkPauseAuto.Text = "暂停刷新";
            // 
            // chkContour
            // 
            // 「轮廓」开关：只控制 ROI 轮廓 / 卡尺窗口这类"输入几何"。
            // OCR 的区域框与合并字符串框不受它影响 —— 否则取消勾选会把"在识别哪一块"一起关掉。
            this.chkContour.Checked = true;
            this.chkContour.Dock = System.Windows.Forms.DockStyle.Right;
            this.chkContour.Location = new System.Drawing.Point(280, 2);
            this.chkContour.Name = "chkContour";
            this.chkContour.Size = new System.Drawing.Size(64, 24);
            this.chkContour.TabIndex = 6;
            this.chkContour.Text = "轮廓";
            // 
            // btnLabelDetail
            // 
            // 标注详细度：点一下循环 简 → 标准 → 详细（全局记忆，见 UiSettings.FlowLabelDetail）
            this.btnLabelDetail.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(52)))), ((int)(((byte)(52)))), ((int)(((byte)(62)))));
            this.btnLabelDetail.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnLabelDetail.Dock = System.Windows.Forms.DockStyle.Right;
            this.btnLabelDetail.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnLabelDetail.Font = new System.Drawing.Font("微软雅黑", 10F, System.Drawing.FontStyle.Bold);
            this.btnLabelDetail.ForeColor = System.Drawing.Color.White;
            this.btnLabelDetail.Kind = VisionPlatform.UI.UiButtonKind.Grab;
            this.btnLabelDetail.Location = new System.Drawing.Point(200, 2);
            this.btnLabelDetail.Name = "btnLabelDetail";
            this.btnLabelDetail.Size = new System.Drawing.Size(80, 24);
            this.btnLabelDetail.TabIndex = 7;
            this.btnLabelDetail.Text = "标注·标准";
            // 
            // btnDrawRoi
            // 
            this.btnDrawRoi.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(52)))), ((int)(((byte)(52)))), ((int)(((byte)(62)))));
            this.btnDrawRoi.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnDrawRoi.Dock = System.Windows.Forms.DockStyle.Right;
            this.btnDrawRoi.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnDrawRoi.Font = new System.Drawing.Font("微软雅黑", 11F, System.Drawing.FontStyle.Bold);
            this.btnDrawRoi.ForeColor = System.Drawing.Color.White;
            this.btnDrawRoi.Kind = VisionPlatform.UI.UiButtonKind.Grab;
            this.btnDrawRoi.Location = new System.Drawing.Point(460, 2);
            this.btnDrawRoi.Name = "btnDrawRoi";
            this.btnDrawRoi.Size = new System.Drawing.Size(88, 24);
            this.btnDrawRoi.TabIndex = 2;
            this.btnDrawRoi.Text = "画 ROI";
            this.btnDrawRoi.UseVisualStyleBackColor = false;
            // 
            // btnLoadImage
            // 
            this.btnLoadImage.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(52)))), ((int)(((byte)(52)))), ((int)(((byte)(62)))));
            this.btnLoadImage.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnLoadImage.Dock = System.Windows.Forms.DockStyle.Right;
            this.btnLoadImage.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnLoadImage.Font = new System.Drawing.Font("微软雅黑", 11F, System.Drawing.FontStyle.Bold);
            this.btnLoadImage.ForeColor = System.Drawing.Color.White;
            this.btnLoadImage.Kind = VisionPlatform.UI.UiButtonKind.Grab;
            this.btnLoadImage.Location = new System.Drawing.Point(548, 2);
            this.btnLoadImage.Name = "btnLoadImage";
            this.btnLoadImage.Size = new System.Drawing.Size(88, 24);
            this.btnLoadImage.TabIndex = 3;
            this.btnLoadImage.Text = "载入图片";
            this.btnLoadImage.UseVisualStyleBackColor = false;
            // 
            // cmbMode
            // 
            this.cmbMode.Dock = System.Windows.Forms.DockStyle.Left;
            this.cmbMode.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbMode.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.cmbMode.Items.AddRange(new object[] {
            "选中节点",
            "链尾结果",
            "原始图像"});
            this.cmbMode.Location = new System.Drawing.Point(4, 2);
            this.cmbMode.Name = "cmbMode";
            this.cmbMode.Size = new System.Drawing.Size(108, 20);
            this.cmbMode.TabIndex = 4;
            this.cmbMode.SelectedIndex = 0;   // 默认「选中节点」：显式初始化，不再依赖 Mode getter 的 default 分支
            // 
            // panelView
            // 
            this.panelView.Controls.Add(this.viewer);
            this.panelView.Controls.Add(this.lblEmpty);
            this.panelView.Dock = System.Windows.Forms.DockStyle.Fill;
            this.panelView.Location = new System.Drawing.Point(0, 28);
            this.panelView.Name = "panelView";
            this.panelView.Size = new System.Drawing.Size(640, 292);
            this.panelView.TabIndex = 2;
            // 
            // viewer
            // 
            this.viewer.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(32)))), ((int)(((byte)(32)))), ((int)(((byte)(36)))));
            this.viewer.Cursor = System.Windows.Forms.Cursors.Default;
            this.viewer.Dock = System.Windows.Forms.DockStyle.Fill;
            this.viewer.Location = new System.Drawing.Point(0, 0);
            this.viewer.Name = "viewer";
            this.viewer.NewShapeColor = System.Drawing.Color.Lime;
            this.viewer.ShowHud = true;
            this.viewer.Size = new System.Drawing.Size(640, 292);
            this.viewer.TabIndex = 0;
            // 拖框画 ROI：图形先被控件加入 Shapes 再触发事件，事件里接管渲染
            this.viewer.ShapeCreated += this.OnViewerShapeCreated;
            // Esc 取消拖框（控件自身只取消当前拖拽，不退出我们的取框状态）
            this.viewer.KeyDown += this.OnViewerKeyDown;
            // ★ 顶条三个控件的订阅也曾被 VS 重序列化吞掉（同 pullTimer.Tick 那次）：
            //   丢了的表现是「点『画 ROI』毫无反应」——RoiPickRequested 没有订阅者，
            //   就永远进不了 BeginDraw，_roiPicking 也就永远为 false（DEF-01 的根因）。
            this.btnDrawRoi.Click += this.OnDrawRoiClick;
            this.btnLoadImage.Click += this.OnLoadImageClick;
            this.chkPauseAuto.CheckedChanged += this.OnPauseAutoChanged;
            this.chkContour.CheckedChanged += this.OnContourChanged;
            this.btnLabelDetail.Click += this.OnLabelDetailClick;
            // 旋转矩形编辑态：拖动过程只刷标签（ShapesChanged），松手才落盘一次（ShapeCommitted）。
            this.viewer.ShapesChanged += this.OnViewerShapesChanged;
            this.viewer.ShapeCommitted += this.OnViewerShapeCommitted;
            this.viewer.MouseUp += this.OnViewerMouseUp;
            // 
            // lblEmpty
            // 
            this.lblEmpty.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblEmpty.Location = new System.Drawing.Point(0, 0);
            this.lblEmpty.Name = "lblEmpty";
            this.lblEmpty.Size = new System.Drawing.Size(640, 292);
            this.lblEmpty.TabIndex = 1;
            this.lblEmpty.Text = "运行流程后可在此查看各节点处理结果";
            this.lblEmpty.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // lblVerdict
            // 
            this.lblVerdict.Location = new System.Drawing.Point(8, 36);
            this.lblVerdict.Name = "lblVerdict";
            this.lblVerdict.Size = new System.Drawing.Size(96, 32);
            this.lblVerdict.TabIndex = 1;
            this.lblVerdict.Visible = false;
            // 
            // lblSummary
            // 
            this.lblSummary.Location = new System.Drawing.Point(112, 40);
            this.lblSummary.Name = "lblSummary";
            this.lblSummary.Size = new System.Drawing.Size(560, 22);
            this.lblSummary.TabIndex = 0;
            // 覆盖层与顶条同源：长文本必须省略号收尾，不能撑出画面（UI-R09 三处文案一致）
            this.lblSummary.AutoEllipsis = true;
            this.lblSummary.Visible = false;
            // 
            // pullTimer
            // 
            this.pullTimer.Enabled = true;
            this.pullTimer.Interval = 33;
            // ★ 事件订阅与 Start 曾在 VS 重序列化 FlowPreviewPanel 时丢失（75ed7fb），
            //   导致 OnPullTick 永不执行、运行预览全断——恢复并留此注释防再丢。
            this.pullTimer.Tick += this.OnPullTick;
            this.pullTimer.Start();
            // 
            // FlowPreviewPanel
            // 
            this.Controls.Add(this.lblSummary);
            this.Controls.Add(this.lblVerdict);
            this.Controls.Add(this.panelView);
            this.Controls.Add(this.panelTop);
            this.Name = "FlowPreviewPanel";
            this.Size = new System.Drawing.Size(640, 320);
            this.panelTop.ResumeLayout(false);
            this.panelView.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        /// <summary>运行时套用主题（构建代码只写结构，颜色 / 字体单点来自 UiTheme）。</summary>
        public void ApplyTheme()
        {
            BackColor = UiTheme.ColorPanelBg;
            ForeColor = UiTheme.ColorTextPrimary;

            panelTop.BackColor = UiTheme.ColorHeaderBg;
            panelView.BackColor = UiTheme.ColorViewBg;

            cmbMode.BackColor = UiTheme.ColorCardBg;
            cmbMode.ForeColor = UiTheme.ColorTextPrimary;
            cmbMode.Font = UiTheme.FontBody;

            chkPauseAuto.ForeColor = UiTheme.ColorTextSecondary;
            chkPauseAuto.Font = UiTheme.FontBody;

            chkContour.ForeColor = UiTheme.ColorTextSecondary;
            chkContour.Font = UiTheme.FontBody;

            // btnLoadImage 是 UiButton（GDI+ 自绘）：填充 / 字体 / 前景全部由控件自身取自
            // UiTheme（FillColor + FontButton），此处不再覆盖，避免双重着色。

            // 新建图形（含 ROI 预览矩形）的线色同样取自主题
            viewer.NewShapeColor = UiTheme.ColorAccent;
            // 切换主题时重取图像区底色：Designer 里写死了 #202024，不重取会停在旧色板上
            viewer.BackColor = UiTheme.ColorViewBg;

            lblInfo.ForeColor = UiTheme.ColorTextPrimary;
            lblInfo.Font = UiTheme.FontBody;

            lblEmpty.BackColor = UiTheme.ColorViewBg;
            lblEmpty.ForeColor = UiTheme.ColorTextSecondary;
            lblEmpty.Font = UiTheme.FontBody;

            lblSummary.ForeColor = UiTheme.ColorTextPrimary;
            lblSummary.Font = UiTheme.FontMono;

            // 自绘叠加层（ROI 轮廓 / 编辑中的旋转矩形）用的是固定 StrokeColor，换色板后要重取；
            // 轮廓图形本身每帧会重建，这里主要管「正在编辑」和「已加进视图」的那两个。
            if (_roiOutlineShape != null) _roiOutlineShape.StrokeColor = UiTheme.ColorAccent;
            if (_editShape != null) _editShape.StrokeColor = UiTheme.ColorAccent;

            UiTheme.StyleViewBadge(lblVerdict, UiVerdict.None);
        }

        #endregion

        #region 对外接口

        /// <summary>绑定运行控制器（预览从中覆盖式拉取快照）。</summary>
        public void SetController(FlowRunController controller)
        {
            _controller = controller;
        }

        /// <summary>
        /// 「轮廓」开关状态：控制 ROI 轮廓 / 卡尺投影窗口这类<strong>输入几何</strong>的显隐。
        /// <para>OCR 的区域框与合并字符串框<strong>不受它影响</strong>（它们的开关在节点参数里），
        /// 否则取消勾选「轮廓」会把"OCR 在识别哪一块"一起关掉 —— 那正好是本次要治的"看不清结果"。</para>
        /// </summary>
        public bool ShowContour
        {
            get { return _showContour; }
            set
            {
                if (_showContour == value) return;
                _showContour = value;
                if (chkContour != null && chkContour.Checked != value)
                {
                    // 直接改 Checked 会再触发一次 CheckedChanged，用标记避免递归重绘
                    chkContour.CheckedChanged -= OnContourChanged;
                    chkContour.Checked = value;
                    chkContour.CheckedChanged += OnContourChanged;
                }
                RebuildShapes();
            }
        }

        /// <summary>设置标注详细度（1 简 / 2 标准 / 3 详细）并立即重绘。</summary>
        public void SetLabelDetail(int level)
        {
            ShapeLabelStyle.Current = UiSettings.NormalizeLabelDetail(level);
            UpdateLabelDetailButton();
            RebuildShapes();
        }

        /// <summary>当前标注详细度。</summary>
        public int LabelDetail { get { return ShapeLabelStyle.Current; } }

        /// <summary>设置当前选中节点 Id（「选中节点」模式下只显示该节点的输出）。</summary>
        public void SetSelectedNodeId(string nodeId)
        {
            _selectedNodeId = nodeId ?? "";
        }

        /// <summary>
        /// 显示"由节点参数推导的布局"（卡尺扫描线与投影窗口、ROI 轮廓等），<strong>不需要先运行</strong>。
        /// <para>
        /// 【为什么单独一层】它不是运行结果，不能混进 <c>_curShapes</c>（那会被当成实测目标计数），
        /// 也不能跟着「轮廓」开关一起消失 —— 调 X1/Y1/投影长度时它必须一直在。
        /// </para>
        /// </summary>
        /// <param name="shapes">布局图形；null/空 = 清除。</param>
        /// <param name="info">顶条提示（如"卡尺 10 个 · 投影 20px · 半宽 2px"）；null = 不改。</param>
        public void ShowNodeLayout(List<IGeometryShape> shapes, string info)
        {
            _layoutShapes = shapes ?? new List<IGeometryShape>();

            if (info != null && lblInfo != null)
            {
                lblInfo.Text = info;
                lblInfo.Visible = info.Length > 0;
            }

            RebuildShapes();
            viewer.Invalidate();
        }

        /// <summary>清掉布局预览（切到别的节点 / 开始运行 / 清空时调用）。</summary>
        public void ClearNodeLayout()
        {
            if (_layoutShapes.Count == 0) return;
            _layoutShapes = new List<IGeometryShape>();
            RebuildShapes();
            viewer.Invalidate();
        }

        /// <summary>当前显示模式。</summary>
        public FlowPreviewMode Mode
        {
            get
            {
                switch (cmbMode.SelectedIndex)
                {
                    case 1: return FlowPreviewMode.ChainEnd;
                    case 2: return FlowPreviewMode.Original;
                    default: return FlowPreviewMode.SelectedNode;
                }
            }
        }

        /// <summary>用户在预览区「载入图片」成功读入一张本地图片后触发（参数为文件路径）。</summary>
        public event EventHandler<string> ImageLoaded;

        /// <summary>
        /// 用户在预览区拖完一个矩形后触发（参数是<strong>图像坐标</strong>的矩形）。
        /// 宽或高小于 <see cref="MinRoiSizePx"/> 视为误操作，不触发本事件。
        /// </summary>
        public event EventHandler<RectangleF> RoiPicked;

        /// <summary>
        /// 用户在预览区画完一个几何后触发（参数是对应图形：矩形/<see cref="LineSegmentShape"/>/<see cref="RingShape"/>）。
        /// 几何太小（短于 <see cref="MinRoiSizePx"/>）视为误操作，不触发本事件。
        /// 矩形仍兼容触发 <see cref="RoiPicked"/>，新代码统一订阅本事件按 <c>Shape</c> 类型分流即可。
        /// </summary>
        public event EventHandler<ShapeEventArgs> ShapePicked;

        /// <summary>预览区当前是否有图像（无图时无法拖框）。</summary>
        public bool HasImage
        {
            get { return !viewer.ImageSize.IsEmpty; }
        }

        /// <summary>
        /// 预览区当前显示的图像（供"抓取当前帧做模板"取用）。
        /// <para>
        /// <strong>不转移所有权</strong>：位图仍归视图，换图 / 清图时会被释放。
        /// 调用方要用请自己 <c>Clone()</c> —— 直接把引用拿走并在别处长期持有，
        /// 会得到"随机 GDI+ 对象当前正在使用"这类崩溃。
        /// </para>
        /// </summary>
        public Bitmap DisplayedImage
        {
            get { return viewer != null ? viewer.Image : null; }
        }

        /// <summary>
        /// 当前画面所属的节点 Id（<see cref="ShowFrame"/> 时写入，历史缩略帧时写该节点）。
        /// <para>结果区据此判断"点行能不能直接高亮"，还是必须先用缩略帧回看。</para>
        /// </summary>
        public string CurrentNodeId
        {
            get { return _curNodeId ?? ""; }
        }

        /// <summary>当前画面画的图形是否属于该节点自身（false = 链尾全链图形，序号对不上）。</summary>
        public bool CurrentOwnShapes
        {
            get { return _curNodeOwnShapes; }
        }

        /// <summary>是否正处于「画 ROI」拖框状态。</summary>
        public bool IsRoiPicking
        {
            get { return _roiPicking; }
        }

        /// <summary>
        /// 用户点了顶条的「画 ROI」按钮。由宿主（FlowForm）决定画哪一组 ROI：
        /// 只有一组就直接进入拖框，有多组则先弹菜单让用户选。
        /// </summary>
        public event EventHandler RoiPickRequested;

        /// <summary>用户按 Esc（或调用 <see cref="CancelRoiPick"/>）取消了画 ROI。</summary>
        public event EventHandler RoiPickCancelled;

        /// <summary>「画 ROI」按钮本体（宿主弹选择菜单时用作锚点）。</summary>
        public Control RoiPickAnchor
        {
            get { return btnDrawRoi; }
        }

        /// <summary>设置「画 ROI」按钮可用态（选中节点没有 ROI 参数时置灰）。</summary>
        public void SetRoiEnabled(bool enabled)
        {
            UiTheme.SetButtonEnabled(btnDrawRoi, enabled, UiButtonKind.Grab);
        }

        /// <summary>
        /// 进入「画 ROI」拖框状态：视图切到 <see cref="InteractionMode.DrawRectangle"/>，
        /// 顶条给出操作提示；若传入了有效的已有 ROI，先画出来让用户看到当前值。
        /// </summary>
        /// <param name="current">已有 ROI（图像坐标）；宽或高为 0 表示尚无 ROI。</param>
        /// <param name="title">提示前缀，一般为节点名。</param>
        public void BeginRoiPick(RectangleF current, string title)
        {
            if (_roiPicking) return;

            _roiPicking = true;
            _drawSpec = null;     // 旧矩形路径：路由时按 Rect 处理
            _editingRotated = false;
            _editShape = null;
            SetDrawButtonText(false);
            _modeBeforeRoi = viewer.Mode;
            _infoBeforeRoi = lblInfo.Text;

            // 绘制期间右键是「取消」语义，别让与当前操作无关的右键菜单弹出来。
            // 置位跟着 _roiPicking 走（而不是 _editingRotated）：用户刚点完按钮、还没画完
            // 那段窗口里右键同样不该弹菜单 —— 绑 _editingRotated 会漏掉这一段。
            viewer.SuppressContextMenu = true;

            // 屏幕→图像坐标换算由 ImageViewer 内部完成，这里只需切模式
            viewer.Mode = InteractionMode.DrawRectangle;

            if (current.Width > 0f && current.Height > 0f) ShowRoiRect(current);
            else { viewer.ClearShapes(); viewer.Shapes.NotifyChanged(); }

            lblEmpty.Visible = false;
            lblVerdict.Visible = false;
            lblSummary.Visible = false;
            lblInfo.Text = (string.IsNullOrEmpty(title) ? "画 ROI" : title)
                           + "：请在图像上拖动绘制 ROI，Esc 取消";

            viewer.Focus();   // 让后续 Esc 落在视图上而不是按钮上
        }

        /// <summary>
        /// 进入「画几何」拖拽状态（矩形 / 直线 / 圆环统一入口）。按 <paramref name="spec"/> 的
        /// <see cref="DrawSpec.Mode"/> 切交互模式；若 <see cref="DrawSpec.Current"/> 非空，先把它作为
        /// 已有几何画出来让用户看到当前值；顶条给出操作提示。
        /// </summary>
        public void BeginDraw(DrawSpec spec)
        {
            if (_roiPicking) return;
            if (spec == null) return;

            _drawSpec = spec;
            _roiPicking = true;
            _editingRotated = false;      // 画完第一个图形后才进入编辑态（见 OnViewerShapeCreated）
            _editShape = null;
            SetDrawButtonText(false);
            _modeBeforeRoi = viewer.Mode;
            _infoBeforeRoi = lblInfo.Text;

            // 同 BeginRoiPick：绘制期间抑制右键菜单（置位跟 _roiPicking 走）
            viewer.SuppressContextMenu = true;

            // 屏幕→图像坐标换算由 ImageViewer 内部完成，这里只需切模式
            viewer.Mode = spec.Mode;

            viewer.ClearShapes();
            if (spec.Current != null)
            {
                viewer.Shapes.Add(spec.Current);
                viewer.Shapes.NotifyChanged();
            }

            lblEmpty.Visible = false;
            lblVerdict.Visible = false;
            lblSummary.Visible = false;
            lblInfo.Text = (string.IsNullOrEmpty(spec.Label) ? "画几何" : spec.Label)
                           + "：" + PromptFor(spec.Kind) + "，Esc 取消";

            viewer.Focus();   // 让后续 Esc 落在视图上而不是按钮上
        }

        /// <summary>按几何类型给出拖拽提示语。</summary>
        private static string PromptFor(DrawShapeKind kind)
        {
            switch (kind)
            {
                case DrawShapeKind.Line: return "在图像上拖动绘制线段（直线卡尺扫描线）";
                case DrawShapeKind.Ring: return "在图像上拖动绘制圆环（圆卡尺环形 ROI）";
                case DrawShapeKind.RotatedRect:
                    return "在图像上拖动绘制 ROI（画完可拖 8 个手柄调尺寸 / 角度，按住 Shift 吸附 15°）";
                default: return "在图像上拖动绘制 ROI";
            }
        }

        /// <summary>
        /// 旋转矩形的标签文本：「12.4°  400×100」。编辑态与 FR-12 轮廓共用同一口径，
        /// 所以放在这里而不是各调用点各写一份 format。
        /// </summary>
        public static string FormatRotatedRoiLabel(float width, float height, float angleDeg)
        {
            return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0:0.0}°  {1:0}×{2:0}", angleDeg, width, height);
        }

        /// <summary>顶条按钮在「画 ROI」↔「完成」之间切换（编辑态时点它是结束编辑，不再请求新几何）。</summary>
        private void SetDrawButtonText(bool editing)
        {
            btnDrawRoi.Text = editing ? "完成" : "画 ROI";
            btnDrawRoi.Kind = editing ? UiButtonKind.Start : UiButtonKind.Grab;
            btnDrawRoi.Invalidate();
        }

        /// <summary>
        /// 退出「画 ROI」拖框状态：恢复原交互模式并清掉临时图形（不写回任何参数），
        /// 同时抛出 <see cref="RoiPickCancelled"/> 让宿主给出提示。
        /// </summary>
        public void CancelRoiPick()
        {
            if (!_roiPicking) return;

            bool wasEditing = _editingRotated;
            string info = _infoBeforeRoi;

            EndRoiPickSilently();
            viewer.ClearShapes();
            viewer.Shapes.NotifyChanged();

            // 编辑态取消：值在拖动过程中已经逐次落过盘，这里只退出编辑，绝不谎报"已取消"
            lblInfo.Text = wasEditing ? "已结束画 ROI（本次修改已保存）" : info;

            EventHandler handler = RoiPickCancelled;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        /// <summary>
        /// 结束旋转矩形编辑态（点「完成」/ Esc / 右键）：恢复原交互模式与顶条文本，
        /// 把最终 ROI 留作叠加层。<strong>不回滚</strong>已保存的参数（每次松手都已经落盘）。
        /// </summary>
        public void FinishRotatedRoiEdit()
        {
            if (!_editingRotated) return;

            RotatedRectShape keep = _editShape;
            string info = _infoBeforeRoi;

            EndRoiPickSilently();
            lblInfo.Text = string.IsNullOrEmpty(info) ? "ROI 已更新" : info;

            if (keep != null) ShowRotatedRoi(keep);
            viewer.Focus();
        }

        /// <summary>
        /// 显示一张静态图片（不运行流程也能看图）——「载入图片」按钮与参数面板改路径后共用。
        /// <para>
        /// 【帧所有权】<paramref name="bmp"/> 交给视图持有（<c>OwnsImage=true</c>），
        /// 调用方设置后绝不能再 Dispose，视图换图 / 清图时自行释放旧帧。
        /// </para>
        /// </summary>
        /// <param name="bmp">要显示的位图；null 时忽略。</param>
        /// <param name="title">顶条信息文本（一般传文件名）；空则显示「本地图片」。</param>
        public void ShowStaticImage(Bitmap bmp, string title)
        {
            if (bmp == null) return;

            // 第二个参数是 autoFit（缩放至完整可见）；所有权由 OwnsImage 控制，默认 true，
            // 这里显式写一次，保证「交给视图后调用方绝不 Dispose」的约定不依赖控件默认值。
            viewer.OwnsImage = true;
            viewer.SetImage(bmp, true);

            // 静态图没有叠加图形：清空上一次运行留下的 ROI / 卡尺等
            viewer.ClearShapes();
            viewer.Shapes.NotifyChanged();

            _staticShown = true;
            lblEmpty.Visible = false;
            lblInfo.Text = string.IsNullOrEmpty(title) ? "本地图片" : title;
            lblVerdict.Visible = false;
            lblSummary.Visible = false;
        }

        /// <summary>
        /// 从本地文件读图：先读后拷（<c>using (var probe = Image.FromFile(path)) new Bitmap(probe)</c>），
        /// 避免 Bitmap 长期持有文件句柄导致文件被占用、无法删除或覆盖。
        /// </summary>
        /// <param name="path">图片文件路径。</param>
        /// <param name="error">失败原因；成功时为 null。</param>
        /// <returns>成功返回新位图（调用方负责释放，或转交视图持有）；失败返回 null。</returns>
        public static Bitmap LoadImageFromFile(string path, out string error)
        {
            error = null;

            if (string.IsNullOrWhiteSpace(path)) { error = "图片路径为空"; return null; }
            if (!File.Exists(path)) { error = "文件不存在：" + path; return null; }

            try
            {
                using (Image probe = Image.FromFile(path))
                    return new Bitmap(probe);
            }
            catch (Exception ex)
            {
                error = "读取图片失败：" + ex.Message;
                return null;
            }
        }

        /// <summary>
        /// 只画一个 ROI 矩形（接管渲染：先清空再添加，避免叠加运行结果的图形）。
        /// 注：这里只处理<strong>轴对齐矩形</strong> ROI；圆卡尺的「环形 ROI」（Cx / Cy / 内径 / 外径）
        /// 不是矩形，待后续扩展（需改用 RingShape 与另一套参数键），此处不是遗漏。
        /// </summary>
        private void ShowRoiRect(RectangleF rect)
        {
            viewer.ClearShapes();

            var shape = new RectangleShape(rect);
            shape.Name = "ROI";
            shape.StrokeColor = UiTheme.ColorAccent;
            viewer.Shapes.Add(shape);
            viewer.Shapes.NotifyChanged();
        }

        /// <summary>只画一个线段叠加（直线卡尺扫描线）。</summary>
        private void ShowLine(LineSegmentShape line)
        {
            viewer.ClearShapes();

            var shape = new LineSegmentShape(line.P1, line.P2);
            shape.Name = "扫描线段";
            shape.StrokeColor = UiTheme.ColorAccent;
            viewer.Shapes.Add(shape);
            viewer.Shapes.NotifyChanged();
        }

        /// <summary>
        /// 只画一个旋转矩形 ROI 叠加（带角度标签）。与 <see cref="ShowRoiRect"/> 同口径：
        /// 先清空再添加，避免和运行结果的图形叠在一起。
        /// </summary>
        private void ShowRotatedRoi(RotatedRectShape rect)
        {
            if (rect == null) return;

            viewer.ClearShapes();

            var shape = new RotatedRectShape(rect.Center, rect.Width, rect.Height, rect.AngleDeg);
            shape.Name = "ROI";
            shape.StrokeColor = UiTheme.ColorAccent;
            shape.Label = FormatRotatedRoiLabel(rect.Width, rect.Height, rect.AngleDeg);
            viewer.Shapes.Add(shape);
            viewer.Shapes.NotifyChanged();
        }

        // =====================================================================
        //  FR-12：选中节点常显 ROI 轮廓
        // =====================================================================

        /// <summary>
        /// 设置「选中节点常显的 ROI 轮廓」（传 null 清除）。只保存模板，
        /// 真正的图形在每帧 <see cref="ShowFrame"/> 末尾重放 —— 否则会被 33 ms 的
        /// <c>ClearShapes</c> 立刻冲掉（与结果行高亮同一个机制）。
        /// </summary>
        public void SetRoiOutline(RotatedRectShape outline, string label)
        {
            _roiOutline = outline;
            _roiOutlineLabel = label ?? "";

            ReapplyRoiOutline();
            viewer.Invalidate();
        }

        /// <summary>清除 ROI 轮廓叠加（切到别的节点 / 换流程时调用）。</summary>
        public void ClearRoiOutline()
        {
            if (_roiOutline == null && _roiOutlineShape == null) return;

            _roiOutline = null;
            _roiOutlineLabel = "";

            ReapplyRoiOutline();
            viewer.Invalidate();
        }

        /// <summary>
        /// 把 ROI 轮廓重放到视图里：先摘掉上一帧那个（避免越积越多），再加一个新的。
        /// 绘制 / 编辑态不画 —— 那时用户手上已经有一个正在编辑的 ROI，叠一层只会互相干扰。
        /// </summary>
        private void ReapplyRoiOutline()
        {
            if (_roiOutlineShape != null)
            {
                viewer.Shapes.Remove(_roiOutlineShape);
                _roiOutlineShape = null;
            }

            if (_roiOutline == null || _roiPicking) return;

            var shape = new RotatedRectShape(_roiOutline.Center, _roiOutline.Width,
                                             _roiOutline.Height, _roiOutline.AngleDeg);
            shape.Name = "ROI轮廓";
            shape.IsEditable = false;                 // 只读叠加：鼠标选不中，不会误拖
            shape.StrokeColor = UiTheme.ColorAccent;
            shape.Label = _roiOutlineLabel;

            viewer.Shapes.Add(shape);
            _roiOutlineShape = shape;
        }

        /// <summary>只画一个圆环叠加（圆卡尺环形 ROI）。</summary>
        private void ShowRing(RingShape ring)
        {
            viewer.ClearShapes();

            var shape = new RingShape(ring.Center, ring.OuterRadius, ring.InnerRadius);
            shape.Name = "环形ROI";
            shape.StrokeColor = UiTheme.ColorAccent;
            viewer.Shapes.Add(shape);
            viewer.Shapes.NotifyChanged();
        }

        /// <summary>结束拖框状态但不还原界面（Reset / 彻底清空时用）。</summary>
        private void EndRoiPickSilently()
        {
            if (!_roiPicking) return;

            _roiPicking = false;
            _drawSpec = null;
            _editingRotated = false;
            _editShape = null;
            SetDrawButtonText(false);
            viewer.Mode = _modeBeforeRoi;

            // 复位点必须是这里 —— 它是三条退出路径（完成 / Esc / 右键取消）的公共出口。
            // 只在 FinishRotatedRoiEdit 里复位的话，Esc 与右键取消这两条路径不复位 →
            // 右键菜单从此永久消失（不报错、不崩溃，最难查）。
            viewer.SuppressContextMenu = false;

            _infoBeforeRoi = "";
        }

        /// <summary>清空预览（切换流程 / 重新加载时调用）。</summary>
        public void Reset()
        {
            EndRoiPickSilently();

            _hasRun = false;
            _staticShown = false;

            // 换流程 / 重载：ROI 轮廓属于上一个流程的节点，一并清掉（ClearShapes 会移除图形本体）
            _roiOutline = null;
            _roiOutlineLabel = "";
            _roiOutlineShape = null;

            viewer.ClearShapes();
            viewer.Shapes.NotifyChanged();
            viewer.ClearImage();

            // 叠加层模板一并清空：否则此刻点「轮廓」/「标注」会把上一个流程的图形重新画出来
            _curShapes = new List<IGeometryShape>();
            _layoutShapes = new List<IGeometryShape>();
            _curAux = new List<IGeometryShape>();
            _hlNodeId = "";
            _hlIndex = -1;
            _curNodeOwnShapes = false;

            lblEmpty.Visible = true;
            lblEmpty.Text = "运行流程后可在此查看各节点处理结果";
            lblInfo.Text = "未运行";
            lblVerdict.Visible = false;
            lblSummary.Visible = false;
        }

        #endregion

        #region 拉取与显示

        private void OnPullTick(object sender, EventArgs e)
        {
            FlowRunController ctl = _controller;
            if (ctl == null) return;

            // 拖框期间不拉帧：否则定时器会用运行帧覆盖用户正在画的 ROI
            if (_roiPicking) return;

            // 原始图像：独立槽位，覆盖式；当前模式不是它时立即释放
            Bitmap raw = ctl.TryTakeOriginal();
            if (raw != null)
            {
                if (Mode == FlowPreviewMode.Original && !_paused) ShowFrame(raw, null, null);
                else raw.Dispose();
            }

            // 「选中节点」模式从节点专用槽取（选中节点的快照不会再被链尾覆盖掉）；
            // 其它模式从既有 _pending 槽取（链尾快照仍在，切模式即可回看最后一次运行）。
            FlowProgressSnapshot snap = ctl.TryTakePending(Mode == FlowPreviewMode.SelectedNode);
            if (snap == null) return;

            if (_paused)
            {
                snap.ReleaseFrame();     // 暂停刷新：丢弃帧，但运行与统计仍在继续
                return;
            }

            Apply(snap);
        }

        /// <summary>按显示模式决定是否消费该快照；不消费时立即释放其帧。</summary>
        private void Apply(FlowProgressSnapshot snap)
        {
            if (snap == null) return;

            if (!string.IsNullOrEmpty(snap.ErrorMessage))
            {
                lblInfo.Text = snap.ErrorMessage;
                snap.ReleaseFrame();
                return;
            }

            FlowPreviewMode mode = Mode;

            // 原始图像模式只消费「原始帧」槽位
            if (mode == FlowPreviewMode.Original)
            {
                snap.ReleaseFrame();
                return;
            }

            // 「选中节点」模式：链尾（最终结果）快照同样按节点过滤——否则它总是最后一个
            // 被拉取并覆盖，导致选中非末节点时预览仍停留在最终结果图（用户反馈的 bug）。
            // 仅当选中节点恰好是链尾节点时，链尾快照才会通过过滤并显示（含 OK/NG 结论）。
            if (mode == FlowPreviewMode.SelectedNode)
            {
                if (!string.IsNullOrEmpty(_selectedNodeId) && snap.NodeId != _selectedNodeId)
                {
                    snap.ReleaseFrame();
                    return;
                }
            }

            if (mode == FlowPreviewMode.ChainEnd && !snap.IsChainEnd)
            {
                snap.ReleaseFrame();
                return;
            }

            _hasRun = true;
            lblEmpty.Visible = false;

            // 图形：链尾模式画全链图形，否则只画该节点自身图形（FR-ALG-19 验收①②）
            List<IGeometryShape> shapes = (mode == FlowPreviewMode.ChainEnd && snap.AllShapes.Count > 0)
                ? snap.AllShapes
                : snap.Shapes;

            // 辅助几何（ROI 轮廓 / 卡尺窗口 / OCR 区域与串框）：链尾模式下没有归属节点，
            // 只画该节点时才有意义，故只在"节点视图"里带上。
            List<IGeometryShape> aux = (mode == FlowPreviewMode.ChainEnd && snap.AllShapes.Count > 0)
                ? null
                : snap.AuxShapes;

            // 记下本帧的「高亮身份上下文」：只有画的是该节点自身图形时，
            // 结果表格里的图形序号才能对上（链尾全链图形是各节点图形的拼接，序号不可比）。
            _curNodeId = snap.NodeId ?? "";
            _curNodeOwnShapes = !ReferenceEquals(shapes, snap.AllShapes);

            // 位图叠加层（分割掩膜 / 深度伪彩）：先与帧合成再交给视图，
            // 这样 ImageViewer 不用知道叠加层的存在，矢量图形仍画在最上面。
            Bitmap frameToShow = snap.Frame;
            if (frameToShow != null && snap.Overlay != null)
            {
                try
                {
                    var composed = new Bitmap(frameToShow.Width, frameToShow.Height,
                                              System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                    using (var g = Graphics.FromImage(composed))
                    {
                        g.DrawImage(frameToShow, 0, 0, frameToShow.Width, frameToShow.Height);
                        g.DrawImage(snap.Overlay, 0, 0, frameToShow.Width, frameToShow.Height);
                    }
                    frameToShow.Dispose();
                    frameToShow = composed;
                }
                catch (Exception) { /* 合成失败就用原帧，不影响其他显示 */ }

                // 合成完就把叠加层释放掉，否则每帧泄漏一张 32bpp 图
                try { snap.Overlay.Dispose(); }
                catch (Exception) { }
                snap.Overlay = null;
            }

            ShowFrame(frameToShow, shapes, aux);
            snap.Frame = null;      // 已交给视图持有（OwnsImage=true），此后不得再释放

            // 【三处文案同源（UI-R09）】顶条、覆盖层、状态栏都走 NodeResultText，
            // 谁也不再就地拼字符串 —— 否则"同一个节点三个说法"会随下次改动长回来。
            string primary = NodeResultText.Primary(snap.Type, snap.Metrics, snap.ElapsedMs, snap.Summary);
            string badge = NodeResultText.Badge(snap.Status);

            string head = string.Format("第 {0}/{1} 个节点：{2}　{3}",
                snap.Index, snap.Total, snap.NodeName, badge);
            if (!string.IsNullOrEmpty(primary)) head += " · " + primary;
            if (snap.IsChainEnd && snap.Fps > 0.0)
                head += string.Format("　|　{0:F1} fps　平均 {1:F0} ms", snap.Fps, snap.AvgCycleMs);

            // 一眼看出"这帧带没带效果图"：没看到彩色时，先看这行有没有标记，
            // 有标记=绘制问题、没标记=合成没发生，省一轮来回。
            if (snap.KeepFrameForUi) head += "　|　含效果层";
            lblInfo.Text = head;

            // 左上角结论徽标 + 一行摘要：链尾显示整链结论，节点视图显示该节点自身的结论。
            if (snap.IsChainEnd)
            {
                UiTheme.StyleViewBadge(lblVerdict, snap.Passed ? UiVerdict.Ok : UiVerdict.Ng);
                lblVerdict.Visible = true;

                string first = FirstLine(snap.ChainSummary);
                lblSummary.Text = string.IsNullOrEmpty(first) ? (snap.Passed ? "OK" : "NG") : first;
                lblSummary.Visible = true;
            }
            else
            {
                UiVerdict v = UiVerdict.None;
                if (snap.Status == FlowNodeStatus.Ok) v = UiVerdict.Ok;
                else if (snap.Status == FlowNodeStatus.Ng) v = UiVerdict.Ng;
                else if (snap.Status == FlowNodeStatus.Warn) v = UiVerdict.Warn;

                UiTheme.StyleViewBadge(lblVerdict, v);
                lblVerdict.Visible = v != UiVerdict.None;
                lblSummary.Text = string.IsNullOrEmpty(primary) ? "" : primary;
                lblSummary.Visible = lblSummary.Text.Length > 0;
            }
        }

        /// <summary>显示帧并替换叠加图形（视图持有帧所有权，换图时自行释放旧帧）。</summary>
        private void ShowFrame(Bitmap frame, List<IGeometryShape> shapes, List<IGeometryShape> aux)
        {
            if (frame != null)
            {
                viewer.SetImage(frame, true);
                lblEmpty.Visible = false;
            }

            _curShapes = shapes ?? new List<IGeometryShape>();
            _curAux = aux ?? new List<IGeometryShape>();
            RebuildShapes();
        }

        /// <summary>
        /// 重建叠加层（不碰图像）：检测目标按标注详细度渲染，辅助几何按「轮廓」开关过滤。
        /// <para>
        /// 【为什么单独一个方法】「轮廓」开关与标注详细度都只该重画图形：
        /// 若走 <c>ShowFrame</c> 会重新 <c>SetImage</c> 一次，连续运行时表现为整幅闪一下。
        /// </para>
        /// </summary>
        private void RebuildShapes()
        {
            if (viewer == null) return;

            int level = ShapeLabelStyle.Current;
            viewer.ClearShapes();

            for (int i = 0; i < _curShapes.Count; i++)
            {
                IGeometryShape s = ShapeLabelStyle.Render(_curShapes[i], level);
                if (s != null) viewer.Shapes.Add(s);
            }

            // 布局预览层：与「轮廓」开关无关（它就是给调参看的，关掉等于瞎调）
            for (int i = 0; i < _layoutShapes.Count; i++)
            {
                IGeometryShape s = _layoutShapes[i];
                if (s != null) viewer.Shapes.Add(s);
            }

            for (int i = 0; i < _curAux.Count; i++)
            {
                IGeometryShape s = _curAux[i];
                if (s == null) continue;

                // 「轮廓」只管输入几何；OCR 区域框 / 串框不打 AuxContourName，因此永远可见
                if (!_showContour && string.Equals(s.Name, FlowShapes.AuxContourName, StringComparison.Ordinal))
                    continue;

                viewer.Shapes.Add(s);
            }

            // 重新施加「点结果表格行」的高亮：上面的 ClearShapes + Add 已经把选中态清掉了。
            // 这里按「节点 Id + 序号」在新实例上重新定位，因此连续运行时高亮不会被刷新冲掉。
            ReapplyHighlight();

            // 顺序不能反：ReapplyHighlight 用的是「序号」定位，必须先按原始图形列表定好高亮，
            // 再把 ROI 轮廓追加到末尾，否则轮廓会顶掉真正该高亮的那一个。
            ReapplyRoiOutline();

            viewer.Shapes.NotifyChanged();
        }

        /// <summary>
        /// 按 <c>_hlNodeId + _hlIndex</c> 在当前已加入视图的图形上重新落高亮。
        /// 结果图形 <c>IsEditable=false</c>，鼠标不会误选，程序化设置选中态是安全的。
        /// </summary>
        private void ReapplyHighlight()
        {
            IGeometryShape target = null;

            // 节点 Id 对不上（切了节点 / 换了流程）或本帧画的是全链图形时，序号无意义 → 不高亮
            if (_hlIndex >= 0 && _hlNodeId.Length > 0
                && string.Equals(_hlNodeId, _curNodeId, StringComparison.Ordinal)
                && _curNodeOwnShapes
                && _hlIndex < viewer.Shapes.Count)
            {
                target = viewer.Shapes[_hlIndex];
            }

            if (target != null) viewer.SelectShape(target);
            else viewer.Shapes.ClearSelection();
        }

        /// <summary>
        /// 突出显示某个结果图形（由「输出结果」表格点行驱动）。
        /// <paramref name="shapeIndex"/> 是该图形在<paramref name="nodeId"/> 节点 <c>Shapes</c> 列表内的序号；
        /// 传 &lt; 0 或空节点 Id 表示取消高亮。
        /// </summary>
        public void SetHighlight(string nodeId, int shapeIndex)
        {
            _hlNodeId = nodeId ?? "";
            _hlIndex = (_hlNodeId.Length > 0 && shapeIndex >= 0) ? shapeIndex : -1;

            ReapplyHighlight();     // 立刻给反馈；后续每帧由 ShowFrame 继续重施加
            viewer.Invalidate();
        }

        /// <summary>取消结果行高亮（切节点 / 换流程时调用，避免高亮残留到别的节点）。</summary>
        public void ClearHighlight()
        {
            SetHighlight("", -1);
        }

        /// <summary>
        /// 显示某个历史节点的<strong>1/4 缩略帧</strong>并高亮指定目标（结果区"切回历史节点点行"用）。
        /// <para>
        /// 【为什么图形要在这里缩】结果图形一律存<strong>原图坐标</strong>，而这里显示的是 1/4 缩略图；
        /// 换算方向错了框会飞出画面且不报错。换算只有 <see cref="ThumbGeometry.ToThumb(RectangleF)"/> 一处实现，
        /// 传进来的 <paramref name="shapes"/> 保持原图坐标即可，缩放由本方法做。
        /// </para>
        /// <para>本方法只用于"回看"，<strong>不参与任何检测计算</strong>；下一次运行帧到达时会被正常覆盖。</para>
        /// </summary>
        /// <param name="thumbnail">缩略帧（1/4 尺寸）；本方法会克隆一份，调用方仍持有原图。</param>
        /// <param name="shapes">该节点的结果图形（<strong>原图坐标</strong>）；可为 null。</param>
        /// <param name="nodeId">节点 Id（用于高亮的节点匹配）。</param>
        /// <param name="highlightIndex">要高亮的目标序号；&lt; 0 表示不高亮。</param>
        /// <param name="title">顶条文案。</param>
        public void ShowHistoricalFrame(Bitmap thumbnail, IList<IGeometryShape> shapes,
                                        string nodeId, int highlightIndex, string title)
        {
            if (thumbnail == null) return;

            _roiPicking = false;
            _editingRotated = false;
            _editShape = null;
            viewer.SuppressContextMenu = false;

            viewer.OwnsImage = true;
            viewer.SetImage(new Bitmap(thumbnail), true);
            viewer.ClearShapes();

            // 缩略帧的图形同样走 _curShapes：这样「轮廓」/「标注」开关在当前画面上依然可用
            // （否则一按开关，缩略图上的框会整片消失，看起来像"回看坏了"）。
            var scaledList = new List<IGeometryShape>();
            if (shapes != null)
            {
                foreach (IGeometryShape s in shapes)
                {
                    if (s == null) continue;
                    IGeometryShape scaled = ThumbGeometry.ScaleShape(s);
                    if (scaled == null) continue;
                    scaled.IsEditable = false;
                    scaledList.Add(scaled);
                }
            }
            _curShapes = scaledList;
            _curAux = new List<IGeometryShape>();

            // 直接重建（不经过 ReapplyHighlight —— 此刻 _curNodeId 还没更新，序号校验会全部落空）
            int level = ShapeLabelStyle.Current;
            foreach (IGeometryShape s in _curShapes)
            {
                IGeometryShape shown = ShapeLabelStyle.Render(s, level);
                if (shown != null) viewer.Shapes.Add(shown);
            }
            if (viewer.Shapes.Count > 0) viewer.Shapes.NotifyChanged();

            // 让 SetHighlight 的节点/序号校验通过：当前画面就是"该节点的自有图形"
            _curNodeId = nodeId ?? "";
            _curNodeOwnShapes = true;
            _hasRun = false;
            _staticShown = true;

            lblEmpty.Visible = false;
            lblVerdict.Visible = false;
            lblSummary.Visible = false;
            lblInfo.Text = string.IsNullOrEmpty(title) ? "历史帧（1/4 缩略）" : title;

            SetHighlight(nodeId ?? "", highlightIndex);
        }

        /// <summary>取摘要首行（以「；」分隔），避免覆盖标签过长。</summary>
        private static string FirstLine(string summary)
        {
            if (string.IsNullOrEmpty(summary)) return "";

            int i = summary.IndexOf('；');
            string s = i >= 0 ? summary.Substring(0, i) : summary;
            if (s.Length > 60) s = s.Substring(0, 60) + "…";
            return s;
        }

        #endregion

        #region 事件

        private void OnModeChanged(object sender, EventArgs e)
        {
            if (Mode == FlowPreviewMode.Original)
            {
                viewer.ClearShapes();
                viewer.Shapes.NotifyChanged();
                viewer.ClearImage();
                lblEmpty.Visible = true;
                lblEmpty.Text = "原始图像：连续运行后显示相机原图";
            }
            else if (!_hasRun && !_staticShown)
            {
                Reset();
            }
            else
            {
                lblEmpty.Visible = false;
            }
        }

        private void OnPauseAutoChanged(object sender, EventArgs e)
        {
            _paused = chkPauseAuto.Checked;
        }

        /// <summary>「轮廓」开关：只重画叠加层，不动图像（不重建帧，连续运行时也不会闪一下）。</summary>
        private void OnContourChanged(object sender, EventArgs e)
        {
            _showContour = chkContour.Checked;
            RebuildShapes();
        }

        /// <summary>标注详细度：简 → 标准 → 详细 → 简 循环，并落盘记忆（Q17：全局，不按节点）。</summary>
        private void OnLabelDetailClick(object sender, EventArgs e)
        {
            int next;
            switch (UiSettings.NormalizeLabelDetail(ShapeLabelStyle.Current))
            {
                case UiSettings.LabelDetailSimple: next = UiSettings.LabelDetailStandard; break;
                case UiSettings.LabelDetailDetailed: next = UiSettings.LabelDetailSimple; break;
                default: next = UiSettings.LabelDetailDetailed; break;
            }

            ShapeLabelStyle.Current = next;
            UpdateLabelDetailButton();
            RebuildShapes();

            // 全局偏好落盘：换流程 / 重启后仍是这一档
            try
            {
                AppConfig cfg = ConfigStore.Current;
                if (cfg != null && cfg.Ui != null)
                {
                    cfg.Ui.FlowLabelDetail = next;
                    ConfigStore.Save(cfg);
                }
            }
            catch (Exception ex)
            {
                // 落盘失败不影响本次观感，但必须留痕（否则"设置记不住"查不到原因）
                Logger.Warn("保存标注详细度失败：" + ex.Message);
            }
        }

        /// <summary>按钮文案与 tooltip 跟着档位变（"标注·简"这种写法比三态下拉更省地方）。</summary>
        private void UpdateLabelDetailButton()
        {
            if (btnLabelDetail == null) return;
            btnLabelDetail.Text = "标注·" + ShapeLabelStyle.Name(ShapeLabelStyle.Current);
            if (toolTip != null)
            {
                toolTip.SetToolTip(btnLabelDetail,
                    "画布叠加标签的详细度：简=只标序号，标准=序号+分数，详细=再补坐标与尺寸（点击循环）");
            }
        }

        /// <summary>
        /// 用户拖完一个矩形。控件已把图形加进 Shapes 才触发本事件，故此处接管渲染：
        /// 取完数值后清掉、只保留这一个 ROI 矩形。
        /// <para>
        /// 【坐标系】图形几何属性（X / Y / Width / Height）一律是<strong>图像坐标</strong>，
        /// zoom / offset 换算由 ImageViewer 内部完成，因此可直接回写节点参数。
        /// </para>
        /// </summary>
        private void OnViewerShapeCreated(object sender, ShapeEventArgs e)
        {
            if (!_roiPicking)
            {
                // 非绘制态收到图形：说明视图被别处切到了绘制模式，或用户没点「画 ROI」就框选。
                // 静默 return 会让人以为"点了没反应"，所以给一句话（下一帧运行信息会覆盖它）。
                lblInfo.Text = "请先点顶条的「画 ROI」再框选（当前不在绘制状态）";
                return;
            }
            if (e == null || e.Shape == null) return;

            DrawShapeKind kind = (_drawSpec != null) ? _drawSpec.Kind : DrawShapeKind.Rect;

            // 旋转矩形：画完**不退出**，进入「编辑态」让用户拖 8 个手柄（FR-07），
            // 松手才落盘（ShapeCommitted）。这一支必须排在 Rect 之前 ——
            // 落进 Rect 分支会被 e.Shape.Bounds 静默压成轴对齐外接框，角度直接丢且不报错。
            if (kind == DrawShapeKind.RotatedRect)
            {
                RotatedRectShape rr = e.Shape as RotatedRectShape;
                if (rr == null) return;

                if (rr.Width < MinRoiSizePx || rr.Height < MinRoiSizePx)
                {
                    viewer.ClearShapes();
                    viewer.Shapes.NotifyChanged();

                    MessageBox.Show(this,
                        string.Format("ROI 太小（{0:F0}×{1:F0} 像素，宽或高需 ≥ {2} 像素），已忽略，请重新拖动。",
                            rr.Width, rr.Height, MinRoiSizePx),
                        "画几何", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                _editingRotated = true;
                _editShape = rr;
                rr.StrokeColor = UiTheme.ColorAccent;
                rr.Label = FormatRotatedRoiLabel(rr.Width, rr.Height, rr.AngleDeg);
                viewer.Shapes.NotifyChanged();

                SetDrawButtonText(true);
                lblInfo.Text = "拖 8 个手柄调尺寸 / 角度（Shift 吸附 15°），点「完成」结束";

                // 画完即落盘一次；之后每次松手由 OnViewerShapeCommitted 再落盘一次。
                // _roiPicking 保持 true：否则 OnPullTick 会用运行帧把 ROI 冲掉。
                CommitRotatedRoi(rr);
                return;
            }

            if (kind == DrawShapeKind.Rect)
            {
                RectangleShape rectShape = e.Shape as RectangleShape;
                RectangleF r = rectShape != null ? rectShape.Rect : e.Shape.Bounds;

                if (r.Width < MinRoiSizePx || r.Height < MinRoiSizePx)
                {
                    viewer.ClearShapes();
                    viewer.Shapes.NotifyChanged();

                    MessageBox.Show(this,
                        string.Format("ROI 太小（{0:F0}×{1:F0} 像素，宽或高需 ≥ {2} 像素），已忽略，请重新拖动。",
                            r.Width, r.Height, MinRoiSizePx),
                        "画几何", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                // 退出拖框：恢复原交互模式（给 Mode 赋值会顺带取消控件内部的绘制态）
                _roiPicking = false;
                viewer.Mode = _modeBeforeRoi;
                lblInfo.Text = _infoBeforeRoi;

                ShowRoiRect(r);

                EventHandler<RectangleF> hRoi = RoiPicked;
                if (hRoi != null) hRoi(this, r);
                EventHandler<ShapeEventArgs> hShape = ShapePicked;
                if (hShape != null) hShape(this, new ShapeEventArgs(new RectangleShape(r)));
                return;
            }

            if (kind == DrawShapeKind.Line)
            {
                LineSegmentShape lineShape = e.Shape as LineSegmentShape;
                if (lineShape == null) return;

                if (lineShape.Length < MinRoiSizePx)
                {
                    viewer.ClearShapes();
                    viewer.Shapes.NotifyChanged();

                    MessageBox.Show(this,
                        string.Format("线段太短（{0:F0} 像素，需 ≥ {1} 像素），已忽略，请重新拖动。",
                            lineShape.Length, MinRoiSizePx),
                        "画几何", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                _roiPicking = false;
                viewer.Mode = _modeBeforeRoi;
                lblInfo.Text = _infoBeforeRoi;

                ShowLine(lineShape);

                EventHandler<ShapeEventArgs> hShape = ShapePicked;
                if (hShape != null) hShape(this, new ShapeEventArgs(new LineSegmentShape(lineShape.P1, lineShape.P2)));
                return;
            }

            // Ring
            RingShape ringShape = e.Shape as RingShape;
            if (ringShape == null) return;

            if (ringShape.OuterRadius < MinRoiSizePx)
            {
                viewer.ClearShapes();
                viewer.Shapes.NotifyChanged();

                MessageBox.Show(this,
                    string.Format("圆环外半径太小（{0:F0} 像素，需 ≥ {1} 像素），已忽略，请重新拖动。",
                        ringShape.OuterRadius, MinRoiSizePx),
                    "画几何", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _roiPicking = false;
            viewer.Mode = _modeBeforeRoi;
            lblInfo.Text = _infoBeforeRoi;

            ShowRing(ringShape);

            EventHandler<ShapeEventArgs> hRing = ShapePicked;
            if (hRing != null) hRing(this, new ShapeEventArgs(new RingShape(ringShape.Center, ringShape.OuterRadius, ringShape.InnerRadius)));
        }

        /// <summary>
        /// 拖动过程中只刷新标签文本（形如「12.4°  400×100」）——
        /// <strong>不写节点参数、不 SaveFlows</strong>（红线①：拖动路径绝不落盘）。
        /// </summary>
        private void OnViewerShapesChanged(object sender, EventArgs e)
        {
            if (!_roiPicking || !_editingRotated || _editShape == null) return;

            _editShape.Label = FormatRotatedRoiLabel(_editShape.Width, _editShape.Height, _editShape.AngleDeg);
        }

        /// <summary>松手 = 用户调整完了：把当前几何交给宿主落盘一次。</summary>
        private void OnViewerShapeCommitted(object sender, ShapeEventArgs e)
        {
            if (!_roiPicking || !_editingRotated) return;

            RotatedRectShape rr = (e != null) ? e.Shape as RotatedRectShape : null;
            if (rr == null) return;

            // 拖太小（比如把高度拖到 1px）视为误操作：不落盘，也不改标签
            if (rr.Width < MinRoiSizePx || rr.Height < MinRoiSizePx) return;

            _editShape = rr;
            rr.Label = FormatRotatedRoiLabel(rr.Width, rr.Height, rr.AngleDeg);
            viewer.Shapes.NotifyChanged();

            CommitRotatedRoi(rr);
        }

        /// <summary>编辑态右键 = 退出编辑（与 Esc 等价；已在拖动的右键仍归视图的平移/菜单处理）。</summary>
        private void OnViewerMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right && _editingRotated) FinishRotatedRoiEdit();
        }

        /// <summary>把旋转矩形交给宿主（FlowForm）写回 RoiX/RoiY/RoiW/RoiH + RoiAngle 并持久化。</summary>
        private void CommitRotatedRoi(RotatedRectShape shape)
        {
            if (shape == null) return;

            EventHandler<ShapeEventArgs> handler = ShapePicked;
            if (handler != null) handler(this, new ShapeEventArgs(shape));
        }

        /// <summary>Esc：编辑态先退出编辑（值已保存），未画完的则整体取消。</summary>
        private void OnViewerKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Escape || !_roiPicking) return;

            if (_editingRotated) FinishRotatedRoiEdit();
            else CancelRoiPick();

            e.Handled = true;
        }

        /// <summary>顶条「画 ROI」：只发请求，画哪一组由宿主决定（可能先弹菜单让用户选）。</summary>
        private void OnDrawRoiClick(object sender, EventArgs e)
        {
            // 编辑态下这个按钮已经变成「完成」：点它只结束编辑，不再请求新的几何组
            if (_editingRotated)
            {
                FinishRotatedRoiEdit();
                return;
            }

            EventHandler handler = RoiPickRequested;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        /// <summary>「载入图片」：选一张本地图片直接显示在预览区（不运行流程）。</summary>
        private void OnLoadImageClick(object sender, EventArgs e)
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "载入图片到预览区";
                dlg.Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp|所有文件|*.*";
                dlg.CheckFileExists = true;

                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                string path = dlg.FileName;
                string error;
                Bitmap bmp = LoadImageFromFile(path, out error);
                if (bmp == null)
                {
                    MessageBox.Show(this, error, "载入图片失败",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // 交给视图后不得再 Dispose（OwnsImage=true）
                ShowStaticImage(bmp, "本地图片：" + Path.GetFileName(path));

                EventHandler<string> handler = ImageLoaded;
                if (handler != null) handler(this, path);
            }
        }

        #endregion

        /// <summary>关窗时停止定时器（帧由视图自身释放）。</summary>
        public void Shutdown()
        {
            try { pullTimer.Stop(); }
            catch (Exception) { }
        }
    }

    /// <summary>预览显示模式（FR-ALG-24）。</summary>
    public enum FlowPreviewMode
    {
        /// <summary>只显示当前选中节点的输出（默认）。</summary>
        SelectedNode,

        /// <summary>显示链尾（整链）结果。</summary>
        ChainEnd,

        /// <summary>显示未经任何处理的相机原图。</summary>
        Original
    }
}
