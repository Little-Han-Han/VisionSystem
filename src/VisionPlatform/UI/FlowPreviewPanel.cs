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

        private Panel panelTop;
        private ComboBox cmbMode;
        private Label lblInfo;
        private CheckBox chkPauseAuto;
        private UiButton btnDrawRoi;
        private UiButton btnLoadImage;
        private Panel panelView;
        private ImageViewer viewer;
        private Label lblEmpty;
        private Label lblVerdict;
        private Label lblSummary;
        private System.ComponentModel.IContainer components;
        private Timer pullTimer;
        private int _diagTick;   // [预览诊断][临时] 拉取入口节流计数——定位后随诊断日志一起移除

        public FlowPreviewPanel()
        {
            InitializeComponent();
            ApplyTheme();
        }

        #region 控件构建

        /// <summary>构建控件树（纯代码，不使用设计器序列化）。</summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.panelTop = new System.Windows.Forms.Panel();
            this.lblInfo = new System.Windows.Forms.Label();
            this.chkPauseAuto = new System.Windows.Forms.CheckBox();
            this.btnDrawRoi = new VisionPlatform.UI.UiButton();
            this.btnLoadImage = new VisionPlatform.UI.UiButton();
            this.cmbMode = new System.Windows.Forms.ComboBox();
            this.panelView = new System.Windows.Forms.Panel();
            this.viewer = new VisionSystem.Controls.ImageViewer();
            this.lblEmpty = new System.Windows.Forms.Label();
            this.lblVerdict = new System.Windows.Forms.Label();
            this.lblSummary = new System.Windows.Forms.Label();
            this.pullTimer = new System.Windows.Forms.Timer(this.components);
            this.panelTop.SuspendLayout();
            this.panelView.SuspendLayout();
            this.SuspendLayout();
            // 
            // panelTop
            // 
            this.panelTop.Controls.Add(this.lblInfo);
            this.panelTop.Controls.Add(this.chkPauseAuto);
            this.panelTop.Controls.Add(this.btnDrawRoi);
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
            this.chkPauseAuto.Size = new System.Drawing.Size(116, 24);
            this.chkPauseAuto.TabIndex = 1;
            this.chkPauseAuto.Text = "暂停自动刷新";
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

            // btnLoadImage 是 UiButton（GDI+ 自绘）：填充 / 字体 / 前景全部由控件自身取自
            // UiTheme（FillColor + FontButton），此处不再覆盖，避免双重着色。

            // 新建图形（含 ROI 预览矩形）的线色同样取自主题
            viewer.NewShapeColor = UiTheme.ColorAccent;

            lblInfo.ForeColor = UiTheme.ColorTextPrimary;
            lblInfo.Font = UiTheme.FontBody;

            lblEmpty.BackColor = UiTheme.ColorViewBg;
            lblEmpty.ForeColor = UiTheme.ColorTextSecondary;
            lblEmpty.Font = UiTheme.FontBody;

            lblSummary.ForeColor = UiTheme.ColorTextPrimary;
            lblSummary.Font = UiTheme.FontMono;

            UiTheme.StyleViewBadge(lblVerdict, UiVerdict.None);
        }

        #endregion

        #region 对外接口

        /// <summary>绑定运行控制器（预览从中覆盖式拉取快照）。</summary>
        public void SetController(FlowRunController controller)
        {
            _controller = controller;
        }

        /// <summary>设置当前选中节点 Id（「选中节点」模式下只显示该节点的输出）。</summary>
        public void SetSelectedNodeId(string nodeId)
        {
            _selectedNodeId = nodeId ?? "";
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
            _modeBeforeRoi = viewer.Mode;
            _infoBeforeRoi = lblInfo.Text;

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
            _modeBeforeRoi = viewer.Mode;
            _infoBeforeRoi = lblInfo.Text;

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
                default: return "在图像上拖动绘制 ROI";
            }
        }

        /// <summary>
        /// 退出「画 ROI」拖框状态：恢复原交互模式并清掉临时图形（不写回任何参数），
        /// 同时抛出 <see cref="RoiPickCancelled"/> 让宿主给出提示。
        /// </summary>
        public void CancelRoiPick()
        {
            if (!_roiPicking) return;

            EndRoiPickSilently();
            viewer.ClearShapes();
            viewer.Shapes.NotifyChanged();
            lblInfo.Text = _infoBeforeRoi;

            EventHandler handler = RoiPickCancelled;
            if (handler != null) handler(this, EventArgs.Empty);
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

        /// <summary>只画一条线段叠加（直线卡尺扫描线）。</summary>
        private void ShowLine(LineSegmentShape line)
        {
            viewer.ClearShapes();

            var shape = new LineSegmentShape(line.P1, line.P2);
            shape.Name = "扫描线段";
            shape.StrokeColor = UiTheme.ColorAccent;
            viewer.Shapes.Add(shape);
            viewer.Shapes.NotifyChanged();
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
            viewer.Mode = _modeBeforeRoi;
            _infoBeforeRoi = "";
        }

        /// <summary>清空预览（切换流程 / 重新加载时调用）。</summary>
        public void Reset()
        {
            EndRoiPickSilently();

            _hasRun = false;
            _staticShown = false;

            viewer.ClearShapes();
            viewer.Shapes.NotifyChanged();
            viewer.ClearImage();

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

            // [预览诊断][临时] 每 ~3 秒记录一次拉取入口状态（定位「被挡住不拉帧」）——定位后移除
            if (++_diagTick % 90 == 0)
                Logger.Info("[预览诊断] tick roiPicking=" + _roiPicking + " mode=" + Mode + " paused=" + _paused);

            // 拖框期间不拉帧：否则定时器会用运行帧覆盖用户正在画的 ROI
            if (_roiPicking) return;

            // 原始图像：独立槽位，覆盖式；当前模式不是它时立即释放
            Bitmap raw = ctl.TryTakeOriginal();
            if (raw != null)
            {
                if (Mode == FlowPreviewMode.Original && !_paused) ShowFrame(raw, null);
                else raw.Dispose();
            }

            // 「选中节点」模式从节点专用槽取（选中节点的快照不会再被链尾覆盖掉）；
            // 其它模式从既有 _pending 槽取（链尾快照仍在，切模式即可回看最后一次运行）。
            FlowProgressSnapshot snap = ctl.TryTakePending(Mode == FlowPreviewMode.SelectedNode);
            if (snap == null) return;

            // [预览诊断][临时] 定位「点运行预览无图」——定位后移除
            Logger.Info("[预览诊断] Pull mode=" + Mode + " sel='" + _selectedNodeId +
                        "' node=" + snap.NodeId + " chainEnd=" + snap.IsChainEnd +
                        " hasFrame=" + (snap.Frame != null));

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
                // [预览诊断][临时] 定位「点运行预览无图」——定位后移除
                Logger.Info("[预览诊断] Apply ERROR " + snap.ErrorMessage);
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
                    // [预览诊断][临时] 定位「点运行预览无图」——定位后移除
                    Logger.Info("[预览诊断] Apply FILTERED-SEL node=" + snap.NodeId + " sel='" + _selectedNodeId + "'");
                    snap.ReleaseFrame();
                    return;
                }
            }

            if (mode == FlowPreviewMode.ChainEnd && !snap.IsChainEnd)
            {
                // [预览诊断][临时] 定位「点运行预览无图」——定位后移除
                Logger.Info("[预览诊断] Apply FILTERED-CHAIN node=" + snap.NodeId);
                snap.ReleaseFrame();
                return;
            }

            // [预览诊断][临时] 定位「点运行预览无图」——定位后移除
            Logger.Info("[预览诊断] Apply SHOW node=" + snap.NodeId + " frame=" + (snap.Frame != null) +
                        " shapes=" + (snap.Shapes != null ? snap.Shapes.Count : 0));

            _hasRun = true;
            lblEmpty.Visible = false;

            // 图形：链尾模式画全链图形，否则只画该节点自身图形（FR-ALG-19 验收①②）
            List<IGeometryShape> shapes = (mode == FlowPreviewMode.ChainEnd && snap.AllShapes.Count > 0)
                ? snap.AllShapes
                : snap.Shapes;

            ShowFrame(snap.Frame, shapes);
            snap.Frame = null;      // 已交给视图持有（OwnsImage=true），此后不得再释放

            string head = string.Format("第 {0}/{1} 个节点：{2}　{3} ms",
                snap.Index, snap.Total, snap.NodeName, snap.ElapsedMs);
            if (snap.IsChainEnd && snap.Fps > 0.0)
                head += string.Format("　|　{0:F1} fps　平均 {1:F0} ms", snap.Fps, snap.AvgCycleMs);
            lblInfo.Text = head;

            // 左上角 OK/NG 文本覆盖：仅在有一次完整结论时显示（FR-ALG-21）
            if (snap.IsChainEnd)
            {
                UiTheme.StyleViewBadge(lblVerdict, snap.Passed ? UiVerdict.Ok : UiVerdict.Ng);
                lblVerdict.Visible = true;

                string first = FirstLine(snap.ChainSummary);
                lblSummary.Text = string.IsNullOrEmpty(first) ? (snap.Passed ? "OK" : "NG") : first;
                lblSummary.Visible = true;
            }
        }

        /// <summary>显示帧并替换叠加图形（视图持有帧所有权，换图时自行释放旧帧）。</summary>
        private void ShowFrame(Bitmap frame, List<IGeometryShape> shapes)
        {
            if (frame != null)
            {
                viewer.SetImage(frame, true);
                lblEmpty.Visible = false;
            }

            viewer.ClearShapes();
            if (shapes != null)
            {
                foreach (IGeometryShape s in shapes)
                    if (s != null) viewer.Shapes.Add(s);
            }
            viewer.Shapes.NotifyChanged();
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
            if (!_roiPicking) return;
            if (e == null || e.Shape == null) return;

            DrawShapeKind kind = (_drawSpec != null) ? _drawSpec.Kind : DrawShapeKind.Rect;

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

        /// <summary>Esc 取消画 ROI（不写回参数）。</summary>
        private void OnViewerKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Escape || !_roiPicking) return;

            CancelRoiPick();
            e.Handled = true;
        }

        /// <summary>顶条「画 ROI」：只发请求，画哪一组由宿主决定（可能先弹菜单让用户选）。</summary>
        private void OnDrawRoiClick(object sender, EventArgs e)
        {
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
