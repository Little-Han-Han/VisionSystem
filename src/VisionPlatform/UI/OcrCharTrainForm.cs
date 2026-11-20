using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using VisionPlatform.Core;
using VisionPlatform.Flow;
using VisionPlatform.Native;
using VisionSystem.Controls;
using VisionSystem.Controls.Geometry;

namespace VisionPlatform.UI
{
    /// <summary>
    /// 字符模板**训练**窗体（传统 OCR）：框选区域 → 自动分割 → 拖拽调整 → 一次性命名 → 存进库。
    ///
    /// 【字符框是可编辑的】每个框都能拖、能拉角、能按 Delete 删、能在图上补画新的。
    /// 框的几何**以画布上的图形为准**（<see cref="CharBox.Shape"/>）：用户松手后我们从图形回读矩形，
    /// 而不是反过来"用模型覆盖视图" —— 后者一旦某次没同步上，就是把用户刚挪好的框弹回原位。
    ///
    /// 【保存的不是图片】走 <see cref="OcrCharLibraryFile"/> 的单文件格式（.ocrtpl），
    /// 追加样本就是往同一个文件里加条目；默认按流程分文件，因此不同流程天然用不同库。
    ///
    /// 【界面全部用代码搭】布局简单，而设计器一旦参与，自定义控件（ImageViewer / UiButton）
    /// 的运行时属性容易被序列化器丢掉。
    /// </summary>
    public sealed class OcrCharTrainForm : Form
    {
        /// <summary>一个字符框：画布图形 + 标签（标签跟着"这个框"走，不跟序号走）。</summary>
        private sealed class CharBox
        {
            public RectangleShape Shape;
            public string Label = "?";
        }

        private const string RoiShapeName = "ocrRoi";

        private readonly string _flowId;
        private readonly string _initialLib;

        /// <summary>宿主提供的"抓当前画面"回调（每次都现场取，不是一次性快照）。</summary>
        private readonly Func<Bitmap> _frameProvider;

        /// <summary>参数回写回调：把训练用的分割参数直接灌进节点的"高级参数"，两边永远一致。</summary>
        private readonly Action<OcrParam> _paramSink;

        /// <summary>节点上配置的搜索区域（RoiX/Y/W/H）；空 = 整图。进来就按它摆好框。</summary>
        private readonly Rectangle _initialRoi;

        /// <summary>
        /// 训练区域是否<strong>禁止改动</strong>。
        /// <para>
        /// 【什么时候锁】节点 ROI 带角度（<c>RoiAngle</c> 或位姿 dA 非零）时，宿主交给本窗体的
        /// 「画面」已经是 <c>RoiAffineMap.Crop</c> 矫正过的整块 ROI —— 这张图里的坐标与整帧没有
        /// 一一对应关系，在这里框一个子区域无法反算回节点参数。与其"框了不生效"或"框完把 ROI 改坏"，
        /// 不如把区域入口关掉并说明原因。
        /// </para>
        /// </summary>
        private readonly bool _roiLocked;

        private bool _loadingParams;

        private VisionAlgo _algo;
        private Bitmap _frame;

        private RectangleShape _roiShape;
        private readonly List<CharBox> _boxes = new List<CharBox>();

        /// <summary>重建图形期间置位，避免"程序改视图"被当成"用户改视图"再同步回来。</summary>
        private bool _syncing;

        /// <summary>
        /// 用户是否<strong>真的动过</strong>这一轮训练（分割 / 改框 / 换图 / 入库）。
        /// <para>
        /// 只有动过才在关窗时写"上次训练现场"：只是点开看一眼就关掉，
        /// 不该覆盖掉上一次真正训到一半的框。
        /// </para>
        /// </summary>
        private bool _dirty;

        /// <summary>当前库里的全部样本（与「字符库（全部）」清单一一对应）。</summary>
        private List<OcrCharSample> _libSamples = new List<OcrCharSample>();

        /// <summary>当前等待用户画框的用途。</summary>
        private enum PickMode { None, Roi, Char }

        private PickMode _pick = PickMode.None;

        // 控件
        private ImageViewer viewer;
        private TextBox txtLib;
        private NumericUpDown numMinW, numMaxW, numMinH, numMaxH, numMinArea, numMaxArea, numThreshold;
        private CheckBox chkOtsu;
        private TextBox txtLabels;
        private ListView lvChars;
        private Label lblRoi, lblStatus, lblAssign, lblLibInfo;
        private UiButton btnLoad, btnFrame, btnRoi, btnSplit, btnPreset, btnApply, btnDel, btnClear, btnAddBox, btnSave;

        // 字符库（全部）页
        private TabControl tabs;
        private ListView lvLib;
        private PictureBox picLib;
        private Label lblLibSel;
        private UiButton btnLibDel, btnLibRefresh, btnLibClear;

        /// <summary>训练完成后写回节点的库文件路径（相对程序目录或绝对路径）。</summary>
        public string SelectedLibPath { get; private set; }

        /// <summary>训练用的区域（画布上的区域框，图像坐标）；空 = 整图。</summary>
        public Rectangle TrainedRoi
        {
            get
            {
                if (_roiShape == null) return Rectangle.Empty;
                return Rectangle.Round(ClampToImage(_roiShape.Bounds));
            }
        }

        /// <summary>训练区域是否被锁定（ROI 带角度时宿主不应把 <see cref="TrainedRoi"/> 写回节点）。</summary>
        public bool RoiLocked
        {
            get { return _roiLocked; }
        }

        /// <summary>训练用的分割参数（写回节点，保证"训练时的分割"与"运行时的分割"一致）。</summary>
        public OcrParam SplitParam { get; private set; }

        /// <summary>用户点过「自动分割」或「保存到库」，参数需要写回。</summary>
        public bool ParamTouched { get; private set; }

        /// <param name="frameProvider">抓"当前画面"（预览区 / 运行帧）；null = 不提供该按钮。</param>
        /// <param name="paramSink">分割参数变更时回写节点（保证与"高级参数"一致）。</param>
        public OcrCharTrainForm(string flowId, string libValue, OcrParam current,
                                Func<Bitmap> frameProvider, Action<OcrParam> paramSink)
            : this(flowId, libValue, current, frameProvider, paramSink, Rectangle.Empty)
        { }

        /// <param name="initialRoi">节点上的 RoiX/Y/W/H；非空时训练区域直接按它摆好，
        /// 避免"训练时框的范围"和"运行时真正处理的范围"不是同一块。</param>
        public OcrCharTrainForm(string flowId, string libValue, OcrParam current,
                                Func<Bitmap> frameProvider, Action<OcrParam> paramSink,
                                Rectangle initialRoi)
            : this(flowId, libValue, current, frameProvider, paramSink, initialRoi, false)
        { }

        /// <param name="roiLocked">为 true 时区域固定为 <paramref name="initialRoi"/> 且禁用「框选区域」，
        /// 用于 ROI 带角度（训练图已是矫正后的整块 ROI）的场合。</param>
        public OcrCharTrainForm(string flowId, string libValue, OcrParam current,
                                Func<Bitmap> frameProvider, Action<OcrParam> paramSink,
                                Rectangle initialRoi, bool roiLocked)
        {
            _flowId = flowId ?? "";
            _initialLib = libValue ?? "";
            _frameProvider = frameProvider;
            _paramSink = paramSink;
            _initialRoi = initialRoi;
            _roiLocked = roiLocked;
            SplitParam = current ?? OcrParam.Default();

            BuildUi();
            LoadParamIntoUi(SplitParam);
            UpdateLibInfo();
            HookParamChanges();

            // 进来就摆上一张图（预览区当前画面）：上一次结束时的画面而不是空白，
            // 否则每次都要重新载图才能接着调框。
            Bitmap first = GrabFrame();
            if (first != null) SetFrame(first, "当前画面");
            else btnFrame.Visible = _frameProvider != null;   // 没有画面时按钮仍在（点了给提示）

            // 再叠上"上次训到哪了"：画面 + 区域 + 每个字符框。
            // 顺序不能反 —— 先把当前画面摆好，恢复时才有"尺寸对不上的旧现场"可判断。
            RestoreSession();

            FormClosed += (s, e) => Cleanup();
        }

        /// <summary>标记"这一轮真的动过"（关窗时才会写训练现场）。</summary>
        private void MarkDirty()
        {
            _dirty = true;
        }

        // ==================================================================
        //  训练现场：上次训到哪了
        // ==================================================================

        /// <summary>
        /// 恢复上次训练现场（画面 / 区域 / 字符框）。没有现场文件时静默返回。
        /// </summary>
        /// <remarks>
        /// 【为什么以"上次那张图"为准】字符框是照着那张图画/调出来的。
        /// 若改用当前实时画面，只要视野挪了一点，框就全错位了 —— 恢复出来的东西反而误导人。
        /// 要换画面，用户有「取当前帧」（换完框子会清空，这是对的）。
        /// </remarks>
        private void RestoreSession()
        {
            string lib = ResolvedLibPath();

            OcrTrainSession ses;
            Bitmap img;
            bool missing;
            string err;

            if (!OcrTrainSession.Load(lib, out ses, out img, out missing, out err))
            {
                // 首次训练（没有现场）是正常状态；文件坏了才需要说出来
                if (!missing && !string.IsNullOrEmpty(err)) SetStatus("上次训练现场读取失败：" + err, true);
                return;
            }

            // 【过期现场整份丢弃】节点 ROI 放不进上次那张图，说明画面已经换了
            // （换相机 / 改了分辨率 / 改了 ROI）。这时恢复出来只会是"训练区域跑到画面外"，
            // 比不恢复更难解释。判据用"容器包含"而不是"相等"：
            // 用户把 ROI 收小一点是完全正常的，只要还在画面里就继续恢复。
            if (_initialRoi.Width > 0 && _initialRoi.Height > 0)
            {
                Rectangle fit = Rectangle.Intersect(_initialRoi, new Rectangle(0, 0, ses.ImageWidth, ses.ImageHeight));
                if (fit.Width < _initialRoi.Width || fit.Height < _initialRoi.Height)
                {
                    if (img != null) img.Dispose();
                    SetStatus(string.Format(CultureInfo.InvariantCulture,
                        "上次的训练现场（{0}×{1}）与当前 ROI（{2},{3},{4},{5}）对不上，已忽略；"
                        + "从当前画面重新开始。", ses.ImageWidth, ses.ImageHeight,
                        _initialRoi.X, _initialRoi.Y, _initialRoi.Width, _initialRoi.Height), false);
                    return;
                }
            }

            if (img != null) SetFrame(img, "上次训练画面");   // 所有权移交给本窗体
            if (_frame == null) return;

            // 区域：带角度的 ROI 是"整块锁定"的，不用恢复（画面本身就是那块）；
            // 节点给了显式 ROI 时也以节点为准 —— 那是"刚在参数面板改过的值"，
            // 用旧现场把它盖回去，用户会以为参数没存上。（正常一轮训练里两者本来就相等，
            // 因为关窗时已经把 TrainedRoi 写回节点。）
            bool nodeRoiGiven = _initialRoi.Width > 0 && _initialRoi.Height > 0;
            if (!_roiLocked && !nodeRoiGiven && ses.Roi.Width > 0 && ses.Roi.Height > 0)
            {
                Rectangle r = Rectangle.Intersect(ses.Roi, new Rectangle(0, 0, _frame.Width, _frame.Height));
                if (r.Width > 3 && r.Height > 3)
                {
                    _roiShape = new RectangleShape(r);
                    lblRoi.Text = Describe(r);
                }
            }

            _boxes.Clear();
            for (int i = 0; i < ses.Boxes.Count; i++)
            {
                OcrTrainBox b = ses.Boxes[i];
                if (b == null) continue;

                Rectangle r = Rectangle.Intersect(new Rectangle(b.X, b.Y, b.W, b.H),
                                                  new Rectangle(0, 0, _frame.Width, _frame.Height));
                if (r.Width < 1 || r.Height < 1) continue;

                _boxes.Add(new CharBox
                {
                    Shape = new RectangleShape(r),
                    Label = string.IsNullOrEmpty(b.Label) ? "?" : b.Label
                });
            }

            ReorderBoxes();
            RebuildShapes();
            RefreshList();

            int named = 0;
            for (int i = 0; i < _boxes.Count; i++) if (IsNamed(_boxes[i])) named++;

            SetStatus(string.Format(CultureInfo.InvariantCulture,
                "已恢复上次训练现场：{0} 个字符框（其中 {1} 个已命名）。"
                + "接着拖框 / 补名字即可；点「取当前帧」可换成最新画面（会清空现有框）。",
                _boxes.Count, named), false);
        }

        /// <summary>关窗时把现场写盘（只在真的动过时写）。</summary>
        private void SaveSession()
        {
            if (!_dirty || _frame == null) return;

            var boxes = new List<OcrTrainBox>(_boxes.Count);
            for (int i = 0; i < _boxes.Count; i++)
            {
                CharBox cb = _boxes[i];
                if (cb == null || cb.Shape == null) continue;

                Rectangle r = Rectangle.Round(ClampToImage(cb.Shape.Bounds));
                if (r.Width < 1 || r.Height < 1) continue;

                boxes.Add(new OcrTrainBox { X = r.X, Y = r.Y, W = r.Width, H = r.Height, Label = cb.Label });
            }

            string err;
            // 写不进去不是错误（目录只读 / 库在网络盘）：只在状态栏提示，不打断关窗
            if (!OcrTrainSession.Save(ResolvedLibPath(), _frame, TrainedRoi, boxes, out err)
                && !string.IsNullOrEmpty(err))
            {
                // 关窗过程中状态栏可能已经不可见，这里只吞掉；下次开窗会用旧现场
            }
        }

        /// <summary>现场取一张"当前画面"的副本（调用方负责释放）。</summary>
        private Bitmap GrabFrame()
        {
            if (_frameProvider == null) return null;
            try
            {
                Bitmap b = _frameProvider();
                return (b == null) ? null : new Bitmap(b);
            }
            catch (Exception) { return null; }
        }

        private void HookParamChanges()
        {
            EventHandler h = (s, e) => PublishParams();
            numMinW.ValueChanged += h;
            numMaxW.ValueChanged += h;
            numMinH.ValueChanged += h;
            numMaxH.ValueChanged += h;
            numMinArea.ValueChanged += h;
            numMaxArea.ValueChanged += h;
            numThreshold.ValueChanged += h;
            chkOtsu.CheckedChanged += h;
        }

        /// <summary>
        /// 把当前分割参数同步给节点（"高级参数"里立刻能看到同一组数）。
        /// <para>
        /// 【为什么要实时同步】这些值决定"训练时分出哪些框"，运行时又决定"识别时切哪些字符"。
        /// 两边只要有一次不一致（比如训练完忘了点保存、或事后在高级参数里改了高度），
        /// 就会出现"训练时明明分对了、跑起来字符被切断"的怪现象，而且极难查。
        /// </para>
        /// </summary>
        private void PublishParams()
        {
            if (_loadingParams) return;

            OcrParam p = CollectParam();
            SplitParam = p;
            ParamTouched = true;

            Action<OcrParam> sink = _paramSink;
            if (sink == null) return;
            try { sink(p); } catch (Exception) { }
        }

        // ==================================================================
        //  界面
        // ==================================================================

        private void BuildUi()
        {
            SuspendLayout();

            Text = "制作字符模板（传统 OCR）";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(1024, 720);
            MinimumSize = new Size(900, 620);
            Font = new Font("Microsoft YaHei UI", 9F);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 3,
                BackColor = Color.Transparent
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 384F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62F));

            Control top = BuildTopBar();
            root.Controls.Add(top, 0, 0);
            root.SetColumnSpan(top, 2);

            viewer = new ImageViewer
            {
                Dock = DockStyle.Fill,
                AutoFitOnResize = true,
                NewShapeColor = Color.FromArgb(255, 196, 0),
                ShowCrosshair = true
            };
            viewer.ShapeCreated += OnShapeCreated;
            viewer.ShapesChanged += OnViewerShapesChanged;
            viewer.ShapeCommitted += OnViewerShapeCommitted;
            viewer.ZoomChanged += (s, e) => RenumberShapes();   // 放大后标签可能放得下了
            root.Controls.Add(viewer, 0, 1);

            root.Controls.Add(BuildSideTabs(), 1, 1);

            lblStatus = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.Gainsboro,
                Padding = new Padding(10, 6, 10, 6),
                Text = "先载入一张字符图（或取当前帧），再框选区域。"
            };
            root.Controls.Add(lblStatus, 0, 2);
            root.SetColumnSpan(lblStatus, 2);

            Controls.Add(root);

            ResumeLayout(false);
            PerformLayout();

            UiStyle.ApplyForm(this);
        }

        private Control BuildTopBar()
        {
            var bar = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 8, 8, 6) };

            btnLoad = NewButton("载入图片…", 8, 6, 96, 30);
            btnLoad.Click += OnLoadImage;

            btnFrame = NewButton("取当前帧", 110, 6, 96, 30);
            btnFrame.Click += OnUseFrame;

            var lbl = new Label
            {
                Text = "库文件：",
                Location = new Point(214, 12),
                AutoSize = true,
                ForeColor = Color.Gainsboro
            };

            txtLib = new TextBox
            {
                Location = new Point(272, 9),
                Width = 560,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = Color.FromArgb(45, 45, 52),
                ForeColor = Color.Gainsboro,
                BorderStyle = BorderStyle.FixedSingle
            };
            txtLib.Text = _initialLib;
            txtLib.TextChanged += (s, e) => UpdateLibInfo();

            var btnBrowse = NewButton("浏览…", 840, 6, 78, 30);
            btnBrowse.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnBrowse.Click += OnBrowseLib;

            var btnOpen = NewButton("打开目录", 924, 6, 88, 30);
            btnOpen.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnOpen.Click += OnOpenLibDir;

            bar.Controls.AddRange(new Control[] { btnLoad, btnFrame, lbl, txtLib, btnBrowse, btnOpen });
            return bar;
        }

        /// <summary>
        /// 右侧两页：<strong>本次训练</strong>（框选 / 分割 / 命名 / 清单）与
        /// <strong>字符库（全部）</strong>（库里已有的每一个样本，可多选删除）。
        /// </summary>
        /// <remarks>
        /// 【为什么分页而不是往下堆】侧栏高约 610 px，「本次训练」已经用满；
        /// 再把"库内全部样本"塞进同一列只能各占一半，两边都不够看。
        /// 分页后各自都能有几行清单，且切页不改变左边画布的宽度。
        /// </remarks>
        private Control BuildSideTabs()
        {
            tabs = new TabControl
            {
                Dock = DockStyle.Fill,
                Font = Font,
                Padding = new Point(14, 4)
            };

            var pageTrain = new TabPage("本次训练") { BackColor = Color.FromArgb(32, 32, 38), Padding = new Padding(0) };
            pageTrain.Controls.Add(BuildSidePanel());

            var pageLib = new TabPage("字符库（全部）") { BackColor = Color.FromArgb(32, 32, 38), Padding = new Padding(0) };
            BuildLibPage(pageLib);

            tabs.TabPages.Add(pageTrain);
            tabs.TabPages.Add(pageLib);
            tabs.SelectedIndexChanged += (s, e) =>
            {
                // 切到库页就重新读盘：另一个窗口/另一个流程可能刚往同一个库里加过样本
                if (tabs.SelectedIndex == 1) RefreshLibList(false);
            };
            return tabs;
        }

        /// <summary>「字符库（全部）」页：库里所有样本 + 预览 + 删除。</summary>
        private void BuildLibPage(TabPage page)
        {
            int y = 6;

            page.Controls.Add(NewLabel("库内全部样本（Ctrl / Shift 可多选；删除立即写盘）", 8, y, 348));
            y += 22;

            lvLib = new ListView
            {
                Location = new Point(8, y),
                Width = 348,
                Height = 330,
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = true,
                HideSelection = false,
                BackColor = Color.FromArgb(40, 40, 46),
                ForeColor = Color.Gainsboro,
                BorderStyle = BorderStyle.FixedSingle
            };
            lvLib.Columns.Add("#", 44);
            lvLib.Columns.Add("字符", 64);
            lvLib.Columns.Add("样本尺寸", 108);
            lvLib.Columns.Add("重复", 108);
            lvLib.SelectedIndexChanged += (s, e) => ShowLibSelection();
            page.Controls.Add(lvLib);
            y += 338;

            picLib = new PictureBox
            {
                Location = new Point(8, y),
                Size = new Size(112, 88),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.FromArgb(24, 24, 28),
                SizeMode = PictureBoxSizeMode.Zoom
            };
            page.Controls.Add(picLib);

            lblLibSel = new Label
            {
                Location = new Point(128, y),
                Width = 228,
                Height = 88,
                ForeColor = Color.Gainsboro
            };
            page.Controls.Add(lblLibSel);
            y += 96;

            btnLibDel = NewButton("删除选中", 8, y, 110, 30);
            btnLibDel.Kind = UiButtonKind.Stop;
            btnLibDel.Click += (s, e) => DeleteLibSelected();
            btnLibRefresh = NewButton("刷新", 126, y, 90, 30);
            btnLibRefresh.Click += (s, e) => RefreshLibList(true);
            btnLibClear = NewButton("清空库", 224, y, 110, 30);
            btnLibClear.Click += (s, e) => ClearLibrary();

            page.Controls.Add(btnLibDel);
            page.Controls.Add(btnLibRefresh);
            page.Controls.Add(btnLibClear);
        }

        private Control BuildSidePanel()
        {
            // 侧栏高度预算：窗体 720 - 顶栏 48 - 状态栏 62 = 610，再减页签头 ~26 与面板内边距 16，
            // 可用约 568 px。**必须在这个预算内排完**：一旦超一点点，AutoScroll 就冒出竖向滚动条，
            // 滚动条又吃掉 17 px 宽度 → 横向滚动条跟着出现，右下角的「库内已有…」直接被挤出可视区。
            // 每一行都留出"标题 20 + 行距"，**参数标签与输入框并排**而不是上下叠，
            // 否则标签会压到上一节的标题上（这就是上一版看起来"重叠覆盖"的原因）。
            var p = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(10, 8, 10, 8) };

            int y = 4;

            p.Controls.Add(NewLabel("① 框选字符区域（拖框后仍可拖动 / 拉角改大小）", 8, y, 348));
            y += 20;

            btnRoi = NewButton("框选区域", 8, y, 110, 30);
            btnRoi.Click += (s, e) => StartPick(PickMode.Roi);
            if (_roiLocked)
            {
                // 区域带角度时训练图本身就是"矫正后的整块 ROI"，在这里再框子区域无法反算回
                // 节点参数 —— 关掉入口并说明原因，好过"框了不生效"。
                btnRoi.Enabled = false;
                btnRoi.Text = "区域已锁定";
            }
            lblRoi = new Label
            {
                Text = "未框选（默认整图）",
                Location = new Point(126, y + 7),
                AutoSize = true,
                ForeColor = Color.Gainsboro
            };
            p.Controls.Add(btnRoi);
            p.Controls.Add(lblRoi);
            y += 34;

            p.Controls.Add(NewLabel("② 自动分割（按宽 / 高 / 面积过滤噪点）", 8, y, 348));
            y += 24;

            numMinW = AddNumRow(p, y, "最小宽", "最大宽", 0, 10000);
            numMaxW = _lastNum2;
            y += 30;
            numMinH = AddNumRow(p, y, "最小高", "最大高", 0, 10000);
            numMaxH = _lastNum2;
            y += 30;
            numMinArea = AddNumRow(p, y, "最小面积", "最大面积", 0, 100000000);
            numMaxArea = _lastNum2;
            y += 30;

            var tagTh = NewTag("阈值", 8, y + 4);
            numThreshold = NewNum(62, y, -1, 255);
            chkOtsu = new CheckBox
            {
                Text = "自动(Otsu)",
                Location = new Point(158, y + 3),
                AutoSize = true,
                ForeColor = Color.Gainsboro
            };
            chkOtsu.CheckedChanged += (s, e) => numThreshold.Enabled = !chkOtsu.Checked;
            p.Controls.Add(tagTh);
            p.Controls.Add(numThreshold);
            p.Controls.Add(chkOtsu);
            y += 34;

            btnSplit = NewButton("自动分割", 8, y, 150, 32);
            btnSplit.Kind = UiButtonKind.Start;
            btnSplit.Click += OnAutoSplit;
            btnPreset = NewButton("按区域预置", 166, y, 190, 32);
            btnPreset.Click += (s, e) => PresetFromRoi(true);
            p.Controls.Add(btnSplit);
            p.Controls.Add(btnPreset);
            y += 36;

            p.Controls.Add(NewLabel("③ 一次性命名（按阅读顺序逐字填入）", 8, y, 348));
            y += 22;

            txtLabels = new TextBox
            {
                Location = new Point(8, y),
                Width = 348,
                Height = 44,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.FromArgb(45, 45, 52),
                ForeColor = Color.Gainsboro,
                BorderStyle = BorderStyle.FixedSingle
            };
            p.Controls.Add(txtLabels);
            y += 50;

            btnApply = NewButton("按顺序填入", 8, y, 120, 28);
            btnApply.Click += OnApplyLabels;
            lblAssign = new Label
            {
                Text = "如：A1B23",
                Location = new Point(136, y + 6),
                AutoSize = true,
                ForeColor = Color.Gray
            };
            p.Controls.Add(btnApply);
            p.Controls.Add(lblAssign);
            y += 34;

            lvChars = new ListView
            {
                Location = new Point(8, y),
                Width = 348,
                Height = 88,
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = true,
                HideSelection = false,
                BackColor = Color.FromArgb(40, 40, 46),
                ForeColor = Color.Gainsboro,
                BorderStyle = BorderStyle.FixedSingle
            };
            lvChars.Columns.Add("#", 34);
            lvChars.Columns.Add("字符", 60);
            lvChars.Columns.Add("框（x,y,w,h）", 176);
            lvChars.SelectedIndexChanged += (s, e) => HighlightSelection();
            p.Controls.Add(lvChars);
            y += 96;

            btnDel = NewButton("删除选中", 8, y, 100, 28);
            btnDel.Kind = UiButtonKind.Stop;
            btnDel.Click += (s, e) => DeleteSelected();
            btnClear = NewButton("清空", 114, y, 80, 28);
            btnClear.Click += (s, e) => ClearBoxes();
            btnAddBox = NewButton("补画一个框", 200, y, 120, 28);
            btnAddBox.Kind = UiButtonKind.Grab;
            btnAddBox.Click += (s, e) => StartPick(PickMode.Char);
            p.Controls.Add(btnDel); p.Controls.Add(btnClear); p.Controls.Add(btnAddBox);
            y += 34;

            btnSave = NewButton("保存到库（追加）", 8, y, 348, 34);
            btnSave.Kind = UiButtonKind.Start;
            btnSave.Click += OnSave;
            p.Controls.Add(btnSave);
            y += 40;

            lblLibInfo = new Label
            {
                Location = new Point(8, y),
                Width = 348,
                Height = 28,
                ForeColor = Color.Gray
            };
            p.Controls.Add(lblLibInfo);

            return p;
        }

        private NumericUpDown _lastNum2;

        /// <summary>"标签 + 输入框"并排的一行（左列 / 右列各一组），返回左列输入框。</summary>
        private NumericUpDown AddNumRow(Panel host, int y, string leftTag, string rightTag,
                                        decimal min, decimal max)
        {
            host.Controls.Add(NewTag(leftTag, 8, y + 4));
            NumericUpDown left = NewNum(62, y, min, max);
            host.Controls.Add(left);

            host.Controls.Add(NewTag(rightTag, 160, y + 4));
            NumericUpDown right = NewNum(214, y, min, max);
            host.Controls.Add(right);

            _lastNum2 = right;
            return left;
        }

        private static Label NewLabel(string text, int x, int y, int w)
        {
            return new Label
            {
                Text = text,
                Location = new Point(x, y),
                Width = w,
                AutoSize = false,
                Height = 20,
                ForeColor = Color.Gainsboro
            };
        }

        private static Label NewTag(string text, int x, int y)
        {
            return new Label
            {
                Text = text,
                Location = new Point(x, y),
                AutoSize = true,
                ForeColor = Color.Gray
            };
        }

        private static NumericUpDown NewNum(int x, int y, decimal min, decimal max)
        {
            return new NumericUpDown
            {
                Location = new Point(x, y),
                Width = 86,
                Minimum = min,
                Maximum = max,
                BackColor = Color.FromArgb(45, 45, 52),
                ForeColor = Color.Gainsboro,
                BorderStyle = BorderStyle.FixedSingle
            };
        }

        private static UiButton NewButton(string text, int x, int y, int w, int h)
        {
            return new UiButton { Text = text, Location = new Point(x, y), Size = new Size(w, h) };
        }

        // ==================================================================
        //  参数
        // ==================================================================

        private void LoadParamIntoUi(OcrParam p)
        {
            if (p == null) p = OcrParam.Default();

            _loadingParams = true;
            try
            {

            numMinW.Value = Clamp(p.MinCharW, numMinW);
            numMaxW.Value = Clamp(p.MaxCharW, numMaxW);
            numMinH.Value = Clamp(p.MinCharH, numMinH);
            numMaxH.Value = Clamp(p.MaxCharH, numMaxH);
            numMinArea.Value = Clamp(p.MinArea, numMinArea);
            numMaxArea.Value = Clamp(p.MaxArea, numMaxArea);

            chkOtsu.Checked = p.Threshold < 0;
            numThreshold.Value = p.Threshold < 0 ? 128 : p.Threshold;
            numThreshold.Enabled = !chkOtsu.Checked;
            }
            finally { _loadingParams = false; }
        }

        private static decimal Clamp(double v, NumericUpDown n)
        {
            if (double.IsNaN(v) || v < (double)n.Minimum) return n.Minimum;
            if (v > (double)n.Maximum) return n.Maximum;
            return (decimal)v;
        }

        private OcrParam CollectParam()
        {
            OcrParam p = SplitParam ?? OcrParam.Default();
            p.MinCharW = (double)numMinW.Value;
            p.MaxCharW = (double)numMaxW.Value;
            p.MinCharH = (double)numMinH.Value;
            p.MaxCharH = (double)numMaxH.Value;
            p.MinArea = (double)numMinArea.Value;
            p.MaxArea = (double)numMaxArea.Value;
            p.Threshold = chkOtsu.Checked ? -1 : (int)numThreshold.Value;
            return p;
        }

        // ==================================================================
        //  取图
        // ==================================================================

        private void OnLoadImage(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "载入一张字符图片（用于框选训练）";
                dlg.Filter = "图片|*.png;*.bmp;*.jpg;*.jpeg;*.tif;*.tiff|所有文件|*.*";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                SetFrame(new Bitmap(dlg.FileName), Path.GetFileName(dlg.FileName));
                MarkDirty();
            }
        }

        private void OnUseFrame(object sender, EventArgs e)
        {
            Bitmap b = GrabFrame();
            if (b == null)
            {
                SetStatus("预览区里还没有画面：先运行一次流程（或「载入图片…」）。", true);
                return;
            }
            SetFrame(b, "当前帧");
            MarkDirty();
        }

        private void SetFrame(Bitmap bmp, string source)
        {
            if (_frame != null) _frame.Dispose();
            _frame = bmp;

            _roiShape = null;
            _boxes.Clear();

            viewer.OwnsImage = false;
            viewer.SetImage(_frame, true);
            RebuildShapes();
            RefreshList();

            // 有节点 ROI 就按它摆好：训练必须和运行时处理**同一块区域**，
            // 否则"训练时框住的范围"和"运行期真正裁剪的范围"对不上，字符会被切边。
            if (_initialRoi.Width > 0 && _initialRoi.Height > 0)
            {
                Rectangle r = Rectangle.Intersect(_initialRoi, new Rectangle(0, 0, _frame.Width, _frame.Height));
                if (r.Width > 3 && r.Height > 3)
                {
                    _roiShape = new RectangleShape(r);
                    lblRoi.Text = Describe(r);
                    if (_roiLocked)
                    {
                        SetStatus("已载入 " + source + "（" + _frame.Width + "×" + _frame.Height
                                  + "）：ROI 带角度，画面已按角度矫正，训练区域固定为整块 ROI（" + Describe(r)
                                  + "）。要换范围请回到节点参数里改 ROI。", false);
                    }
                    else
                    {
                        SetStatus("已载入 " + source + "（" + _frame.Width + "×" + _frame.Height
                                  + "）：训练区域已按节点 ROI 摆好（" + Describe(r) + "），可直接「自动分割」。", false);
                    }
                    return;
                }
            }

            lblRoi.Text = "未框选（默认整图）";
            SetStatus("已载入 " + source + "（" + _frame.Width + "×" + _frame.Height + "）：请点「框选区域」在图上拖框。", false);
        }

        // ==================================================================
        //  框选 / 分割
        // ==================================================================

        private void StartPick(PickMode mode)
        {
            if (_frame == null) { SetStatus("请先载入图片。", true); return; }

            _pick = mode;
            viewer.Mode = InteractionMode.DrawRectangle;
            SetStatus(mode == PickMode.Roi
                ? "在图上拖一个框，圈住整行字符区域。"
                : "在图上拖一个框，补上一个漏掉的字符。", false);
        }

        private void OnShapeCreated(object sender, ShapeEventArgs e)
        {
            var rect = e != null ? e.Shape as RectangleShape : null;
            if (rect == null) return;

            viewer.Mode = InteractionMode.Select;
            PickMode mode = _pick;
            _pick = PickMode.None;

            RectangleF b = ClampToImage(rect.Bounds);
            if (b.Width < 3 || b.Height < 3)
            {
                // 太小的框没有意义，直接从画布上撤掉
                _syncing = true;
                try { viewer.Shapes.Remove(rect); } finally { _syncing = false; }
                SetStatus("框太小（至少 3×3），已忽略。", true);
                return;
            }

            _syncing = true;
            try
            {
                rect.Rect = b;

                if (mode == PickMode.Roi)
                {
                    _roiShape = rect;
                    rect.Name = RoiShapeName;
                    // 区域框**不画标签**：它就在 #1 字符框左上角，标签必然压住 #1 的标签；
                    // 区域尺寸在旁边按钮右侧一直显示着，画布上不需要再标一遍。
                    StyleShape(rect, Color.FromArgb(90, 200, 255), 1.5f, "");
                    lblRoi.Text = Describe(Rectangle.Round(b));
                    SetStatus("区域已选定：" + lblRoi.Text + "（可拖动 / 拉角），点「自动分割」。", false);
                }
                else
                {
                    var cb = new CharBox { Shape = rect, Label = "?" };
                    rect.Tag = cb;
                    _boxes.Add(cb);
                    ReorderBoxes();
                    SetStatus("已补一个字符框：" + Describe(Rectangle.Round(b)) + "。", false);
                }
            }
            finally { _syncing = false; }

            RenumberShapes();
            RefreshList();
            viewer.Invalidate();
            MarkDirty();

            // 刚框完区域就按这个区域的尺寸预置分割参数：默认全 0 等于"什么都不过滤"，
            // 一跑就是几十个噪点框，反而要手工收拾。预置值只是起点，仍可再调。
            if (mode == PickMode.Roi) PresetFromRoi(true);
        }

        /// <summary>
        /// 按"框选区域"预置分割过滤参数。
        /// <para>
        /// 【为什么不能默认全 0】全 0 = 什么都不过滤，一张带噪点的图会分出几十个碎框，
        /// 用户还得一个个删。这里先**关掉所有过滤跑一次探测分割**，用实际分出来的字符尺寸
        /// 统计中位数，再据此给一组 min/max —— 是"从这张图里学出来的"，比按比例硬猜准得多。
        /// </para>
        /// <para>
        /// 组件太少（&lt; 3 个）时无从统计，退回"区域里一行字"的常见比例估算，并在状态栏说明，
        /// 避免让用户以为参数是凭空来的。
        /// </para>
        /// </summary>
        private void PresetFromRoi(bool announce)
        {
            if (_frame == null) return;

            Rectangle roi = _roiShape != null
                ? Rectangle.Round(ClampToImage(_roiShape.Bounds))
                : new Rectangle(0, 0, _frame.Width, _frame.Height);
            if (roi.Width < 8 || roi.Height < 8) return;

            if (_algo == null) _algo = new VisionAlgo();

            try
            {
                using (Bitmap crop = _frame.Clone(roi, _frame.PixelFormat))
                {
                    // 探测：只保留阈值 / 极性，其余过滤全关 —— 要的就是"未经筛选的分割结果"
                    OcrParam probe = CollectParam();
                    probe.MinCharH = 0; probe.MaxCharH = 0;
                    probe.MinCharW = 0; probe.MaxCharW = 0;
                    probe.MinArea = 0; probe.MaxArea = 0;

                    OpResult<List<OcrChar>> r = _algo.Segment(crop, probe, 4000);
                    List<OcrChar> chars = (r.Success && r.Value != null) ? r.Value : new List<OcrChar>();

                    double medH, medW;
                    bool measured = chars.Count >= 3;
                    if (measured)
                    {
                        var hs = new List<double>(chars.Count);
                        var ws = new List<double>(chars.Count);
                        for (int i = 0; i < chars.Count; i++)
                        {
                            if (chars[i].H > 0 && chars[i].W > 0) { hs.Add(chars[i].H); ws.Add(chars[i].W); }
                        }
                        measured = hs.Count >= 3;
                        medH = measured ? Median(hs) : roi.Height * 0.6;
                        medW = measured ? Median(ws) : medH * 0.55;
                    }
                    else
                    {
                        // 区域里几乎没分出东西：按"一行字"估，字符高 ≈ 区域高的 60%
                        medH = roi.Height * 0.6;
                        medW = medH * 0.55;
                    }

                    if (medH < 3) medH = 3;
                    if (medW < 2) medW = 2;

                    double minH = Math.Max(3, Math.Round(medH * 0.55));
                    double maxH = Math.Max(minH + 1, Math.Round(medH * 1.70));
                    double minW = Math.Max(2, Math.Round(medW * 0.45));
                    double maxW = Math.Max(minW + 1, Math.Round(medW * 2.20));
                    double minArea = Math.Max(1, Math.Round(medW * medH * 0.10));
                    double maxArea = Math.Max(minArea + 1, Math.Round(maxW * maxH * 0.85));

                    numMinH.Value = Clamp(minH, numMinH);
                    numMaxH.Value = Clamp(maxH, numMaxH);
                    numMinW.Value = Clamp(minW, numMinW);
                    numMaxW.Value = Clamp(maxW, numMaxW);
                    numMinArea.Value = Clamp(minArea, numMinArea);
                    numMaxArea.Value = Clamp(maxArea, numMaxArea);

                    if (announce)
                    {
                        SetStatus(string.Format(CultureInfo.InvariantCulture,
                            "已{0}预置分割参数：高 {1:0}~{2:0} / 宽 {3:0}~{4:0} / 面积 {5:0}~{6:0}"
                            + "（中位字符 {7:0}×{8:0}），可再调，然后点「自动分割」。",
                            measured ? "按区域实测" : "按区域比例估算",
                            minH, maxH, minW, maxW, minArea, maxArea, medW, medH), false);
                    }
                }
            }
            catch (Exception ex)
            {
                if (announce) SetStatus("预置参数失败：" + ex.Message, true);
            }
        }

        private static double Median(List<double> values)
        {
            if (values == null || values.Count == 0) return 0;
            var copy = new List<double>(values);
            copy.Sort();
            int n = copy.Count;
            return (n % 2 == 1) ? copy[n / 2] : (copy[n / 2 - 1] + copy[n / 2]) * 0.5;
        }

        private void OnAutoSplit(object sender, EventArgs e)
        {
            if (_frame == null) { SetStatus("请先载入图片。", true); return; }

            Rectangle roi = _roiShape != null
                ? Rectangle.Round(ClampToImage(_roiShape.Bounds))
                : new Rectangle(0, 0, _frame.Width, _frame.Height);
            if (roi.Width < 3 || roi.Height < 3) { SetStatus("区域太小，请重新框选。", true); return; }

            OcrParam p = CollectParam();
            SplitParam = p;
            ParamTouched = true;

            Bitmap crop = null;
            try
            {
                crop = _frame.Clone(roi, _frame.PixelFormat);

                if (_algo == null) _algo = new VisionAlgo();
                OpResult<List<OcrChar>> r = _algo.Segment(crop, p, 2000);
                if (!r.Success)
                {
                    SetStatus("分割失败：" + r.Message, true);
                    return;
                }

                List<OcrChar> chars = r.Value ?? new List<OcrChar>();
                _boxes.Clear();
                for (int i = 0; i < chars.Count; i++)
                {
                    var b = new RectangleF((float)(chars[i].X + roi.X), (float)(chars[i].Y + roi.Y),
                                           Math.Max(1f, (float)chars[i].W), Math.Max(1f, (float)chars[i].H));
                    _boxes.Add(new CharBox { Shape = new RectangleShape(b), Label = "?" });
                }

                ReorderBoxes();
                RebuildShapes();
                RefreshList();
                MarkDirty();

                SetStatus(chars.Count == 0
                    ? "没分到字符：放宽「最小宽/高/面积」，或检查「阈值」「极性反转」。"
                    : "已分割出 " + chars.Count + " 个字符：可直接拖动 / 拉角修正，再录入字符名。", chars.Count == 0);
            }
            catch (Exception ex)
            {
                SetStatus("分割异常：" + ex.Message, true);
            }
            finally
            {
                if (crop != null) crop.Dispose();
            }
        }

        /// <summary>
        /// 按"先分行、行内从左到右"排序（与原生分行口径一致，保证命名顺序可预期）。
        /// 拖框后重排，序号即命名顺序 —— 这是"一次性命名"能对上的前提。
        /// </summary>
        private void ReorderBoxes()
        {
            if (_boxes.Count <= 1) return;

            var heights = new int[_boxes.Count];
            for (int i = 0; i < _boxes.Count; i++)
                heights[i] = Math.Max(1, (int)Math.Round(_boxes[i].Shape.Bounds.Height));
            Array.Sort(heights);
            double medH = Math.Max(1, heights[heights.Length / 2]);

            _boxes.Sort((a, b) =>
            {
                RectangleF ra = a.Shape.Bounds, rb = b.Shape.Bounds;
                int la = (int)Math.Round((ra.Y + ra.Height * 0.5) / (medH * 0.6));
                int lb = (int)Math.Round((rb.Y + rb.Height * 0.5) / (medH * 0.6));
                if (la != lb) return la.CompareTo(lb);
                return ra.X.CompareTo(rb.X);
            });
        }

        // ==================================================================
        //  画布 ↔ 模型 同步
        // ==================================================================

        /// <summary>用户拖动 / 改大小时：只回读几何，不重排（拖动中重排会让序号跳来跳去）。</summary>
        private void OnViewerShapesChanged(object sender, EventArgs e)
        {
            if (_syncing) return;
            SyncFromViewer(false);
        }

        /// <summary>松手：这一次调整结束，按新位置重排顺序并重编号。</summary>
        private void OnViewerShapeCommitted(object sender, ShapeEventArgs e)
        {
            if (_syncing) return;
            SyncFromViewer(true);
        }

        private void SyncFromViewer(bool reorder)
        {
            var alive = new HashSet<CharBox>();

            for (int i = viewer.Shapes.Count - 1; i >= 0; i--)
            {
                IGeometryShape s = viewer.Shapes[i];
                if (s == null) continue;

                if (string.Equals(s.Name, RoiShapeName, StringComparison.Ordinal))
                {
                    _roiShape = s as RectangleShape;
                    if (_roiShape != null && lblRoi != null)
                        lblRoi.Text = Describe(Rectangle.Round(ClampToImage(_roiShape.Bounds)));
                    continue;
                }

                var cb = s.Tag as CharBox;
                if (cb == null) continue;

                cb.Shape = s as RectangleShape;
                if (cb.Shape == null) { viewer.Shapes.RemoveAt(i); continue; }
                alive.Add(cb);
            }

            // 画布上被 Delete 键删掉的框，模型里也要消失，否则"看不见却会被保存"
            int before = _boxes.Count;
            _boxes.RemoveAll(b => !alive.Contains(b));
            bool deleted = _boxes.Count != before;

            if (reorder) ReorderBoxes();
            RenumberShapes();
            RefreshList();
            MarkDirty();

            if (deleted) SetStatus("已删除 " + (before - _boxes.Count) + " 个框，剩余 " + _boxes.Count + " 个。", false);
        }

        private void RebuildShapes()
        {
            _syncing = true;
            try
            {
                viewer.Shapes.Clear();

                if (_roiShape != null)
                {
                    _roiShape.Name = RoiShapeName;
                    StyleShape(_roiShape, Color.FromArgb(90, 200, 255), 1.5f, "");
                    viewer.Shapes.Add(_roiShape);
                }

                for (int i = 0; i < _boxes.Count; i++)
                {
                    CharBox cb = _boxes[i];
                    cb.Shape.Tag = cb;
                    ApplyBoxStyle(cb, i);
                    viewer.Shapes.Add(cb.Shape);
                }
            }
            finally { _syncing = false; }

            viewer.Invalidate();
        }

        private static void StyleShape(RectangleShape s, Color color, float width, string label)
        {
            s.StrokeColor = color;
            s.LineWidth = width;
            s.IsEditable = true;      // 可拖动 / 可拉角
            s.Label = label;
        }

        private void RenumberShapes()
        {
            for (int i = 0; i < _boxes.Count; i++) ApplyBoxStyle(_boxes[i], i);
            viewer.Invalidate();
        }

        /// <summary>
        /// 标签是否画得下：标签从框左上角开始画，宽度若超过"到下一个框的距离"就会盖住邻居。
        /// 这里按**屏幕像素**判断（字符框只有十几像素宽，缩小时标签必然比框宽）。
        /// 放不下就不画 —— 画上去互相覆盖，比不画更糟：人会以为标签对应了错误的框。
        /// 右侧清单永远有完整信息，所以"不画"不会丢信息。
        /// </summary>
        private bool LabelFits(int index)
        {
            CharBox cb = _boxes[index];
            if (cb == null || cb.Shape == null) return false;

            float zoom = viewer.Zoom;
            if (zoom <= 0f) zoom = 1f;

            // 8pt 字号下：单个字符约 12px，序号约 8px（中文/全角会宽一点，保守取 14/10）
            float need = IsNamed(cb) ? 14f : 10f;

            if (index + 1 >= _boxes.Count) return true;      // 最后一个框右边没人，随便画

            CharBox next = _boxes[index + 1];
            float gap = (next.Shape.Bounds.Left - cb.Shape.Bounds.Left) * zoom;
            return gap >= need;
        }

        /// <summary>
        /// 框的显示文本与配色。
        /// <para>
        /// 【为什么标签只放一个字符】字符框通常只有 10~30 px 宽，而 "＃12 ?" 这类长标签比框本身宽得多，
        /// 相邻两个框的标签会互相盖住（后画的压在先画的上面），结果就是"只能看到一堆数字、
        /// 不知道每个数字对应哪个字符"。这里改成：**已命名的框只显示那个字符**（A、7、中），
        /// **未命名的框显示序号**（1、2、3），并用颜色区分 —— 琥珀=未命名，绿=已命名。
        /// </para>
        /// </summary>
        private void ApplyBoxStyle(CharBox cb, int index)
        {
            if (cb == null || cb.Shape == null) return;

            bool named = IsNamed(cb);
            cb.Shape.Label = LabelFits(index)
                ? (named ? cb.Label : (index + 1).ToString(CultureInfo.InvariantCulture))
                : "";
            cb.Shape.StrokeColor = named ? Color.FromArgb(0, 230, 118) : Color.FromArgb(255, 196, 0);
            cb.Shape.LineWidth = 1.2f;
            cb.Shape.IsEditable = true;
        }

        private static bool IsNamed(CharBox cb)
        {
            return cb != null && !string.IsNullOrEmpty(cb.Label)
                   && !string.Equals(cb.Label, "?", StringComparison.Ordinal);
        }

        private RectangleF ClampToImage(RectangleF r)
        {
            if (_frame == null) return r;

            float l = Math.Max(0, r.Left);
            float t = Math.Max(0, r.Top);
            float rt = Math.Min(_frame.Width, r.Right);
            float b = Math.Min(_frame.Height, r.Bottom);
            if (rt <= l || b <= t) return new RectangleF(0, 0, 0, 0);
            return new RectangleF(l, t, rt - l, b - t);
        }

        // ==================================================================
        //  命名
        // ==================================================================

        private void OnApplyLabels(object sender, EventArgs e)
        {
            if (_boxes.Count == 0) { SetStatus("还没有字符框：先「自动分割」或「补画一个框」。", true); return; }

            List<string> chars = SplitTextElements(txtLabels.Text);
            if (chars.Count == 0) { SetStatus("请先在输入框里输入字符序列（如 A1B23）。", true); return; }

            int n = Math.Min(chars.Count, _boxes.Count);
            for (int i = 0; i < n; i++) _boxes[i].Label = chars[i];

            RenumberShapes();
            RefreshList();
            MarkDirty();

            if (chars.Count == _boxes.Count)
            {
                lblAssign.Text = "已填入 " + n + " 个";
                SetStatus("已按顺序填入 " + n + " 个字符，核对无误后「保存到库」。", false);
            }
            else
            {
                lblAssign.Text = "填了 " + n + " / " + _boxes.Count;
                SetStatus(string.Format(CultureInfo.InvariantCulture,
                    "字符数（{0}）与框数（{1}）不一致：前 {2} 个已填入，其余仍是 ?。"
                    + "请核对是漏输还是多框。", chars.Count, _boxes.Count, n), true);
            }
        }

        /// <summary>按"文本元素"拆分（代理对/组合字符算一个），并跳过空白。</summary>
        private static List<string> SplitTextElements(string text)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(text)) return list;

            TextElementEnumerator it = StringInfo.GetTextElementEnumerator(text);
            while (it.MoveNext())
            {
                string s = it.GetTextElement();
                if (string.IsNullOrEmpty(s)) continue;
                if (char.IsWhiteSpace(s[0])) continue;
                list.Add(s);
            }
            return list;
        }

        // ==================================================================
        //  保存
        // ==================================================================

        private void OnSave(object sender, EventArgs e)
        {
            if (_frame == null) { SetStatus("请先载入图片。", true); return; }
            if (_boxes.Count == 0) { SetStatus("没有可保存的字符框。", true); return; }

            for (int i = 0; i < _boxes.Count; i++)
            {
                string why = OcrCharStore.ValidateLabel(_boxes[i].Label);
                if (why != null)
                {
                    SetStatus("第 " + (i + 1) + " 个框：" + why, true);
                    return;
                }
            }

            string path = ResolvedLibPath();
            if (string.IsNullOrWhiteSpace(path)) { SetStatus("库文件路径为空。", true); return; }

            var add = new List<OcrCharSample>(_boxes.Count);
            for (int i = 0; i < _boxes.Count; i++)
            {
                Rectangle r = Rectangle.Round(ClampToImage(_boxes[i].Shape.Bounds));
                if (r.Width < 1 || r.Height < 1) continue;

                OcrCharSample s = OcrCharLibraryFile.FromBitmap(_frame, r, _boxes[i].Label);
                if (s != null) add.Add(s);
            }

            if (add.Count == 0) { SetStatus("框内没有有效像素。", true); return; }

            int total;
            string err;
            if (!OcrCharLibraryFile.Append(path, add, out total, out err))
            {
                SetStatus("保存失败：" + err, true);
                return;
            }

            SelectedLibPath = path;
            txtLib.Text = path;
            RefreshLibList(false);

            int saved = add.Count;
            _boxes.Clear();
            txtLabels.Clear();
            RebuildShapes();
            RefreshList();
            MarkDirty();   // 框已入库并清空 → 现场也要跟着变空，否则下次开窗又把已存的框恢复出来

            SetStatus(string.Format(CultureInfo.InvariantCulture,
                "已保存 {0} 个样本到 {1}；库内共 {2} 条。可继续载入其它图片往同一个库里追加。",
                saved, Path.GetFileName(path), total), false);
        }

        private string ResolvedLibPath()
        {
            string v = (txtLib.Text ?? "").Trim();
            return OcrCharLibraryFile.ResolvePath(v, _flowId);
        }

        private void UpdateLibInfo()
        {
            RefreshLibList(false);
        }

        /// <summary>
        /// 重新读库并刷新「库内 xx 个字符 / yy 个样本」与「字符库（全部）」清单。
        /// </summary>
        /// <param name="announce">true = 在状态栏说一句（用户点了「刷新」才需要）。</param>
        private void RefreshLibList(bool announce)
        {
            string path = ResolvedLibPath();

            List<OcrCharSample> samples;
            bool missing;
            string err;

            if (!OcrCharLibraryFile.Load(path, out samples, out missing, out err))
            {
                _libSamples = new List<OcrCharSample>();
                FillLibList();

                if (lblLibInfo != null)
                {
                    lblLibInfo.Text = missing
                        ? "库文件尚不存在（保存后创建）：" + path
                        : "无法读取库：" + err;
                }
                if (announce) SetStatus(missing ? "库文件尚不存在：" + path : "无法读取库：" + err, !missing);
                return;
            }

            _libSamples = samples;

            int labels, total;
            OcrCharLibraryFile.CountByLabel(samples, out labels, out total);
            if (lblLibInfo != null)
            {
                lblLibInfo.Text = string.Format(CultureInfo.InvariantCulture,
                    "库内已有 {0} 个字符 / {1} 个样本\n{2}", labels, total, path);
            }

            FillLibList();
            if (announce) SetStatus("已刷新：库内 " + labels + " 个字符 / " + total + " 个样本。", false);
        }

        /// <summary>把 <see cref="_libSamples"/> 填进清单（序号与库内顺序严格一致，删除用的就是它）。</summary>
        private void FillLibList()
        {
            if (lvLib == null) return;

            // 同一标签出现几次 → "重复 2/3" 这种提示，帮用户判断"多出来的那张是不是该删"
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < _libSamples.Count; i++)
            {
                OcrCharSample s = _libSamples[i];
                if (s == null) continue;
                string k = s.Label ?? "";
                int c;
                counts.TryGetValue(k, out c);
                counts[k] = c + 1;
            }

            var seen = new Dictionary<string, int>(StringComparer.Ordinal);

            lvLib.BeginUpdate();
            try
            {
                lvLib.Items.Clear();
                for (int i = 0; i < _libSamples.Count; i++)
                {
                    OcrCharSample s = _libSamples[i];
                    if (s == null) continue;

                    string k = s.Label ?? "";
                    int n;
                    seen.TryGetValue(k, out n);
                    seen[k] = n + 1;

                    var item = new ListViewItem((i + 1).ToString(CultureInfo.InvariantCulture));
                    item.SubItems.Add(string.IsNullOrEmpty(k) ? "(空)" : k);
                    item.SubItems.Add(s.Width.ToString(CultureInfo.InvariantCulture) + "×"
                                      + s.Height.ToString(CultureInfo.InvariantCulture));
                    item.SubItems.Add((n + 1).ToString(CultureInfo.InvariantCulture) + "/"
                                      + counts[k].ToString(CultureInfo.InvariantCulture));
                    item.Tag = i;                       // ← 库内序号，删除时按它定位
                    lvLib.Items.Add(item);
                }
            }
            finally { lvLib.EndUpdate(); }

            ShowLibSelection();
        }

        /// <summary>选中某条样本 → 在右侧小图里显示它到底长什么样。</summary>
        private void ShowLibSelection()
        {
            if (lblLibSel == null || picLib == null) return;

            if (picLib.Image != null)
            {
                Image old = picLib.Image;
                picLib.Image = null;
                old.Dispose();
            }

            if (lvLib == null || lvLib.SelectedItems.Count == 0)
            {
                lblLibSel.Text = "未选中。\n选中左侧任一条可看它长什么样；\n"
                               + "「删除选中」直接从库里移除（不可撤销）。";
                return;
            }

            if (lvLib.SelectedItems.Count > 1)
            {
                lblLibSel.Text = "已选中 " + lvLib.SelectedItems.Count + " 条。\n"
                               + "点「删除选中」把它们一起从库里移除。";
                return;
            }

            int index = (int)lvLib.SelectedItems[0].Tag;
            if (index < 0 || index >= _libSamples.Count) return;

            OcrCharSample s = _libSamples[index];
            if (s == null) return;

            try
            {
                Bitmap bmp = OcrCharLibraryFile.ToBitmap(s);
                if (bmp != null) picLib.Image = bmp;
            }
            catch (Exception) { }

            lblLibSel.Text = string.Format(CultureInfo.InvariantCulture,
                "第 {0} 条：'{1}'\n原始尺寸 {2}×{3}\n（存的是裁出来时的原始灰度，\n识别时才归一化）",
                index + 1, string.IsNullOrEmpty(s.Label) ? "(空)" : s.Label, s.Width, s.Height);
        }

        /// <summary>
        /// 删除库里选中的样本（立即写盘）。
        /// <para>
        /// 【为什么要二次确认】库是<b>积累了很久</b>的资产，删掉一条就少一个真实样本，
        /// 而且没有回收站；但确认框只报"删几条"，不逐个列，避免堆一屏。
        /// </para>
        /// </summary>
        private void DeleteLibSelected()
        {
            if (lvLib == null || lvLib.SelectedItems.Count == 0)
            {
                SetStatus("请先在「字符库（全部）」里选中要删除的样本。", true);
                return;
            }

            var indices = new List<int>(lvLib.SelectedItems.Count);
            foreach (ListViewItem it in lvLib.SelectedItems)
            {
                if (it.Tag is int) indices.Add((int)it.Tag);
            }
            if (indices.Count == 0) return;

            DialogResult ok = MessageBox.Show(this,
                "从库里删除选中的 " + indices.Count + " 个样本？\n\n"
                + "只删库文件里的条目，不改当前画面的字符框；删除后无法撤销。",
                "删除字符样本", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
            if (ok != DialogResult.OK) return;

            RemoveLibSamples(indices);
        }

        /// <summary>
        /// 删除指定序号的库内样本并刷新清单（<strong>不含确认框</strong>）。
        /// </summary>
        /// <param name="indices">库内序号（与清单的 Tag 一致，来自最近一次 <see cref="RefreshLibList"/>）。</param>
        /// <returns>是否已成功写盘。</returns>
        private bool RemoveLibSamples(IList<int> indices)
        {
            int removed, total;
            string err;

            if (!OcrCharLibraryFile.RemoveRange(ResolvedLibPath(), indices, out removed, out total, out err))
            {
                SetStatus("删除失败：" + err, true);
                return false;
            }

            RefreshLibList(false);
            SetStatus(string.Format(CultureInfo.InvariantCulture,
                "已删除 {0} 个样本，库内剩余 {1} 个。", removed, total), false);
            return true;
        }

        /// <summary>清空整个库（保留文件）。同样要二次确认 —— 这是不可撤销的。</summary>
        private void ClearLibrary()
        {
            if (_libSamples.Count == 0) { SetStatus("库里没有样本，无需清空。", true); return; }

            DialogResult ok = MessageBox.Show(this,
                "清空整个字符库？共 " + _libSamples.Count + " 个样本，不可撤销。\n\n"
                + "建议先确认这些字符确实都要重做。",
                "清空字符库", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
            if (ok != DialogResult.OK) return;

            string err;
            if (!OcrCharLibraryFile.Clear(ResolvedLibPath(), out err))
            {
                SetStatus("清空失败：" + err, true);
                return;
            }

            RefreshLibList(false);
            SetStatus("库已清空（文件保留，条数归零）。", false);
        }

        private void OnBrowseLib(object sender, EventArgs e)
        {
            using (var dlg = new SaveFileDialog())
            {
                dlg.Title = "选择字符库文件（不存在会新建）";
                dlg.Filter = "字符库|*" + OcrCharLibraryFile.Extension + "|所有文件|*.*";
                dlg.FileName = Path.GetFileName(ResolvedLibPath());
                string dir = Path.GetDirectoryName(ResolvedLibPath());
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) dlg.InitialDirectory = dir;

                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                txtLib.Text = dlg.FileName;
                UpdateLibInfo();
            }
        }

        private void OnOpenLibDir(object sender, EventArgs e)
        {
            try
            {
                string dir = Path.GetDirectoryName(ResolvedLibPath());
                if (string.IsNullOrEmpty(dir)) return;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                System.Diagnostics.Process.Start("explorer.exe", "\"" + dir + "\"");
            }
            catch (Exception ex) { SetStatus("无法打开目录：" + ex.Message, true); }
        }

        // ==================================================================
        //  清单 / 高亮 / 删除
        // ==================================================================

        private void RefreshList()
        {
            if (lvChars == null) return;

            lvChars.BeginUpdate();
            try
            {
                lvChars.Items.Clear();
                for (int i = 0; i < _boxes.Count; i++)
                {
                    RectangleF b = _boxes[i].Shape.Bounds;
                    var item = new ListViewItem((i + 1).ToString(CultureInfo.InvariantCulture));
                    item.SubItems.Add(_boxes[i].Label);
                    item.SubItems.Add(Describe(Rectangle.Round(b)));
                    item.Tag = _boxes[i];
                    lvChars.Items.Add(item);
                }
            }
            finally { lvChars.EndUpdate(); }
        }

        /// <summary>选中清单行 → 画布上对应框变绿并选中（选中后就能看到 8 个控制点）。</summary>
        private void HighlightSelection()
        {
            if (lvChars == null || viewer == null || _syncing) return;

            var selected = new HashSet<CharBox>();
            foreach (ListViewItem it in lvChars.SelectedItems)
            {
                var cb = it.Tag as CharBox;
                if (cb != null) selected.Add(cb);
            }

            _syncing = true;
            try
            {
                for (int i = 0; i < viewer.Shapes.Count; i++)
                {
                    IGeometryShape s = viewer.Shapes[i];
                    if (s == null || string.Equals(s.Name, RoiShapeName, StringComparison.Ordinal)) continue;

                    var cb = s.Tag as CharBox;
                    bool on = cb != null && selected.Contains(cb);
                    s.IsSelected = on;
                    s.LineWidth = on ? 2.2f : 1.2f;
                    s.StrokeColor = on ? Color.Lime
                                        : (IsNamed(cb) ? Color.FromArgb(0, 230, 118) : Color.FromArgb(255, 196, 0));
                }
            }
            finally { _syncing = false; }

            viewer.Invalidate();
        }

        private void DeleteSelected()
        {
            if (lvChars.SelectedItems.Count == 0) { SetStatus("请先在清单里选中要删除的框。", true); return; }

            var doomed = new List<CharBox>();
            foreach (ListViewItem it in lvChars.SelectedItems)
            {
                var cb = it.Tag as CharBox;
                if (cb != null) doomed.Add(cb);
            }

            for (int i = 0; i < doomed.Count; i++) _boxes.Remove(doomed[i]);

            RebuildShapes();
            RefreshList();
            MarkDirty();
            SetStatus("已删除 " + doomed.Count + " 个框，剩余 " + _boxes.Count + " 个。", false);
        }

        private void ClearBoxes()
        {
            _boxes.Clear();
            RebuildShapes();
            RefreshList();
            MarkDirty();
            SetStatus("已清空全部字符框。", false);
        }

        private static string Describe(Rectangle r)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0},{1},{2},{3}", r.X, r.Y, r.Width, r.Height);
        }

        private void SetStatus(string text, bool warn)
        {
            if (lblStatus == null) return;
            lblStatus.Text = text;
            lblStatus.ForeColor = warn ? Color.FromArgb(255, 160, 120) : Color.Gainsboro;
        }

        private void Cleanup()
        {
            // 先存现场再释放画面：SaveSession 要读 _frame 的像素
            try { SaveSession(); } catch (Exception) { }

            try { if (_algo != null) { _algo.Dispose(); _algo = null; } } catch (Exception) { }
            try { if (_frame != null) { _frame.Dispose(); _frame = null; } } catch (Exception) { }

            // 预览小图的像素是 ToBitmap 新造的，必须自己释放（否则每次开窗漏一张）
            try { if (picLib != null && picLib.Image != null) { picLib.Image.Dispose(); picLib.Image = null; } }
            catch (Exception) { }
        }
    }
}
