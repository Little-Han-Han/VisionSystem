using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using VisionSystem.Controls;
using VisionSystem.Controls.Geometry;
using VisionSystem.Controls.Interop;

namespace VisionSystem.Demo
{
    /// <summary>
    /// 演示窗体：展示 ImageViewer 的缩放、平移、几何图形绘制与拖拽，
    /// 以及与 OpenCV 数据互操作的完整链路。
    /// </summary>
    public class MainForm : Form
    {
        private ImageViewer _viewer;
        private ListBox _shapeList;

        private ToolStripStatusLabel _lblCoord;
        private ToolStripStatusLabel _lblPixel;
        private ToolStripStatusLabel _lblZoom;
        private ToolStripStatusLabel _lblCount;
        private ToolStripStatusLabel _lblSelection;

        private ToolStripButton[] _modeButtons;
        private bool _syncingSelection;
        private bool _transientMessage;
        private readonly Random _rnd = new Random(20260925);

        public MainForm()
        {
            Text = "VisionSystem 图像控件演示 — 缩放 / 平移 / 几何标注";
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(1280, 820);
            MinimumSize = new Size(900, 600);

            BuildUi();
            LoadDemoImage();
            AddSampleShapes();
        }

        #region 界面搭建

        private void BuildUi()
        {
            // ---------- 工具栏 ----------
            ToolStrip tools = new ToolStrip();
            tools.GripStyle = ToolStripGripStyle.Hidden;
            tools.Items.Add(MakeButton("打开图片", "从磁盘载入一幅图像", OnOpenImage));
            tools.Items.Add(MakeButton("生成测试图", "生成合成测试图像", OnGenerateImage));
            tools.Items.Add(MakeButton("OpenCV 往返", "Bitmap → CV_8UC3 数组 → Bitmap，验证互操作链路", OnCvRoundTrip));
            tools.Items.Add(new ToolStripSeparator());

            ToolStripButton btnSelect = MakeModeButton("选择", InteractionMode.Select, "选择并编辑图形（默认模式）");
            ToolStripButton btnPan = MakeModeButton("平移", InteractionMode.Pan, "拖拽平移画布");
            ToolStripButton btnCircle = MakeModeButton("画圆", InteractionMode.DrawCircle, "按下定圆心，拖动定半径");
            ToolStripButton btnRect = MakeModeButton("画矩形", InteractionMode.DrawRectangle, "拖出轴对齐矩形");
            ToolStripButton btnRot = MakeModeButton("画旋转矩形", InteractionMode.DrawRotatedRect, "拖出矩形后用金色手柄旋转");
            ToolStripButton btnRing = MakeModeButton("画圆环", InteractionMode.DrawRing, "按下定圆心，拖动定外半径，内半径默认取一半");
            _modeButtons = new[] { btnSelect, btnPan, btnCircle, btnRect, btnRot, btnRing };

            tools.Items.Add(btnSelect);
            tools.Items.Add(btnPan);
            tools.Items.Add(btnCircle);
            tools.Items.Add(btnRect);
            tools.Items.Add(btnRot);
            tools.Items.Add(btnRing);
            tools.Items.Add(new ToolStripSeparator());

            tools.Items.Add(MakeButton("适应窗口", "缩放至完整可见（快捷键 0）", (s, e) => _viewer.FitToWindow()));
            tools.Items.Add(MakeButton("1:1", "原始像素尺寸（快捷键 1）", (s, e) => _viewer.ZoomActual()));
            tools.Items.Add(MakeButton("放大", "放大一档（快捷键 +）", (s, e) => _viewer.ZoomIn()));
            tools.Items.Add(MakeButton("缩小", "缩小一档（快捷键 -）", (s, e) => _viewer.ZoomOut()));
            tools.Items.Add(new ToolStripSeparator());

            tools.Items.Add(MakeButton("删除选中", "删除选中图形（Delete）", (s, e) => _viewer.DeleteSelected()));
            tools.Items.Add(MakeButton("清空图形", "移除全部图形", (s, e) => _viewer.ClearShapes()));
            tools.Items.Add(MakeButton("导出参数", "把图形几何导出为文本", OnExportShapes));

            // ---------- 中央显示区 ----------
            _viewer = new ImageViewer();
            _viewer.Dock = DockStyle.Fill;
            _viewer.Mode = InteractionMode.Select;
            _viewer.Options.ShowCrosshair = true;

            _viewer.ZoomChanged += (s, e) => UpdateStatus();
            _viewer.ShapesChanged += (s, e) => { RefreshShapeList(); UpdateStatus(); };
            _viewer.SelectionChanged += (s, e) => { SyncListSelection(); UpdateStatus(); };
            _viewer.ShapeCreated += (s, e) =>
            {
                if (e.Shape != null)
                    SetTransientMessage("新建：" + CvInterop.ToCvParameterString(e.Shape));
            };
            _viewer.MouseImageMove += OnViewerMouseMove;

            // ---------- 右侧面板 ----------
            Panel side = new Panel();
            side.Dock = DockStyle.Right;
            side.Width = 280;
            side.BackColor = Color.FromArgb(45, 45, 52);
            side.Padding = new Padding(8);

            Label lblTitle = new Label();
            lblTitle.Text = "图形列表";
            lblTitle.ForeColor = Color.Gainsboro;
            lblTitle.Dock = DockStyle.Top;
            lblTitle.Height = 22;

            _shapeList = new ListBox();
            _shapeList.Dock = DockStyle.Fill;
            _shapeList.BackColor = Color.FromArgb(38, 38, 44);
            _shapeList.ForeColor = Color.Gainsboro;
            _shapeList.BorderStyle = BorderStyle.FixedSingle;
            _shapeList.SelectedIndexChanged += (s, e) =>
            {
                if (_syncingSelection) return;
                int idx = _shapeList.SelectedIndex;
                if (idx >= 0 && idx < _viewer.Shapes.Count)
                    _viewer.SelectShape(_viewer.Shapes[idx]);
            };

            Label lblHelp = new Label();
            lblHelp.Dock = DockStyle.Bottom;
            lblHelp.Height = 200;
            lblHelp.ForeColor = Color.FromArgb(170, 175, 185);
            lblHelp.Text =
                "操作说明" + Environment.NewLine +
                "─────────────────" + Environment.NewLine +
                "滚轮          以光标为锚点缩放" + Environment.NewLine +
                "Ctrl + 滚轮   快速缩放" + Environment.NewLine +
                "中键拖拽      平移画布" + Environment.NewLine +
                "右键拖拽      平移画布" + Environment.NewLine +
                "右键单击      弹出菜单" + Environment.NewLine +
                "              保存 / 自适应 / 十字线" + Environment.NewLine +
                "空格 + 左键   临时平移" + Environment.NewLine +
                "左键拖拽      移动图形/控制点" + Environment.NewLine +
                "Delete        删除选中图形" + Environment.NewLine +
                "Esc           取消绘制 / 取消选择" + Environment.NewLine +
                "0 / 1         适应窗口 / 原始尺寸" + Environment.NewLine +
                "─────────────────" + Environment.NewLine +
                "绘制模式下按住左键拖出图形，" + Environment.NewLine +
                "选中后用白色方块调整大小，" + Environment.NewLine +
                "金色方块调整旋转角度。";

            side.Controls.Add(_shapeList);
            side.Controls.Add(lblTitle);
            side.Controls.Add(lblHelp);

            // ---------- 中央容器 ----------
            Panel container = new Panel();
            container.Dock = DockStyle.Fill;
            container.Controls.Add(_viewer); // 先加 Fill，后加边缘停靠，保证布局正确
            container.Controls.Add(side);

            // ---------- 状态栏 ----------
            _lblCoord = new ToolStripStatusLabel("坐标: -") { Spring = false, AutoSize = false, Width = 190 };
            _lblPixel = new ToolStripStatusLabel("像素: -") { AutoSize = false, Width = 170 };
            _lblZoom = new ToolStripStatusLabel("缩放: 100%") { AutoSize = false, Width = 110 };
            _lblCount = new ToolStripStatusLabel("图形: 0") { AutoSize = false, Width = 90 };
            _lblSelection = new ToolStripStatusLabel("") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };

            StatusStrip status = new StatusStrip();
            status.Items.Add(_lblCoord);
            status.Items.Add(new ToolStripSeparator());
            status.Items.Add(_lblPixel);
            status.Items.Add(new ToolStripSeparator());
            status.Items.Add(_lblZoom);
            status.Items.Add(new ToolStripSeparator());
            status.Items.Add(_lblCount);
            status.Items.Add(new ToolStripSeparator());
            status.Items.Add(_lblSelection);

            // ---------- 组装主窗体 ----------
            Controls.Add(container);
            Controls.Add(tools);
            Controls.Add(status);

            btnSelect.Checked = true;
        }

        private static ToolStripButton MakeButton(string text, string tip, EventHandler onClick)
        {
            ToolStripButton btn = new ToolStripButton(text);
            btn.DisplayStyle = ToolStripItemDisplayStyle.Text;
            btn.ToolTipText = tip;
            btn.Click += onClick;
            return btn;
        }

        private ToolStripButton MakeModeButton(string text, InteractionMode mode, string tip)
        {
            ToolStripButton btn = new ToolStripButton(text);
            btn.DisplayStyle = ToolStripItemDisplayStyle.Text;
            btn.ToolTipText = tip;
            btn.Tag = mode;
            btn.Click += (s, e) => SetMode(mode);
            return btn;
        }

        private void SetMode(InteractionMode mode)
        {
            _viewer.Mode = mode;
            if (_modeButtons == null) return;
            foreach (ToolStripButton b in _modeButtons)
            {
                if (b.Tag is InteractionMode)
                    b.Checked = ((InteractionMode)b.Tag == mode);
            }
        }

        #endregion

        #region 图像与图形

        private void LoadDemoImage()
        {
            int w = 960, h = 720;
            Bitmap bmp = new Bitmap(w, h);

            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.FromArgb(38, 38, 46));

                // 棋盘格底纹
                const int cell = 48;
                using (Brush checker = new SolidBrush(Color.FromArgb(52, 52, 64)))
                {
                    for (int y = 0; y < h; y += cell)
                    {
                        for (int x = 0; x < w; x += cell)
                        {
                            if (((x / cell) + (y / cell)) % 2 == 0)
                                g.FillRectangle(checker, x, y, cell, cell);
                        }
                    }
                }

                // 斜向渐变
                using (LinearGradientBrush grad = new LinearGradientBrush(
                    new Point(0, 0), new Point(w, h),
                    Color.FromArgb(70, 0, 120, 200), Color.FromArgb(70, 210, 120, 20)))
                {
                    g.FillRectangle(grad, 0, 0, w, h);
                }

                // 随机色块目标
                for (int i = 0; i < 14; i++)
                {
                    int cx = _rnd.Next(70, w - 70);
                    int cy = _rnd.Next(70, h - 70);
                    int r = _rnd.Next(18, 62);
                    Color c = Color.FromArgb(_rnd.Next(70, 230), _rnd.Next(70, 230), _rnd.Next(70, 230));
                    using (Brush b = new SolidBrush(c))
                        g.FillEllipse(b, cx - r, cy - r, r * 2, r * 2);
                }

                // 细线纹理，放大后可用于检验像素网格
                using (Pen linePen = new Pen(Color.FromArgb(90, 255, 255, 255), 1f))
                {
                    for (int x = 0; x < w; x += 16)
                        g.DrawLine(linePen, x, 0, x, 6);
                    for (int y = 0; y < h; y += 16)
                        g.DrawLine(linePen, 0, y, 6, y);
                }

                // 噪声
                using (Brush noise = new SolidBrush(Color.FromArgb(55, 255, 255, 255)))
                {
                    for (int i = 0; i < 2500; i++)
                        g.FillRectangle(noise, _rnd.Next(w), _rnd.Next(h), 1, 1);
                }
            }

            _viewer.SetImage(bmp, true);
        }

        private void AddSampleShapes()
        {
            CircleShape circle = new CircleShape(new PointF(220f, 200f), 70f);
            circle.Name = "Circle-1";
            circle.IsFilled = true;

            RectangleShape rect = new RectangleShape(560f, 120f, 220f, 150f);
            rect.Name = "Rect-1";

            RotatedRectShape rotated = new RotatedRectShape(new PointF(520f, 480f), 260f, 120f, 28f);
            rotated.Name = "RotatedRect-1";

            RingShape ring = new RingShape(new PointF(760f, 300f), 95f, 58f);
            ring.Name = "Ring-1";
            ring.IsFilled = true;

            _viewer.Shapes.AddRange(new IGeometryShape[] { circle, rect, rotated, ring });
            _viewer.SelectShape(rotated);
        }

        #endregion

        #region 事件处理

        private void OnOpenImage(object sender, EventArgs e)
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "选择图像";
                dlg.Filter = "图像文件|*.bmp;*.jpg;*.jpeg;*.png;*.tif;*.tiff|所有文件|*.*";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    // 先读入内存再解码：避免 Bitmap 长期占用文件句柄，
                    // 同时省掉一次全尺寸拷贝（8192x4000 那种图上，那次拷贝非常贵）
                    byte[] bytes = File.ReadAllBytes(dlg.FileName);
                    using (MemoryStream ms = new MemoryStream(bytes))
                    {
                        Bitmap bmp = new Bitmap(ms);
                        _viewer.SetImage(bmp, true);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "无法打开图像：" + ex.Message, "错误",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void OnGenerateImage(object sender, EventArgs e)
        {
            LoadDemoImage();
        }

        private void OnCvRoundTrip(object sender, EventArgs e)
        {
            if (_viewer.Image == null)
            {
                SetTransientMessage("当前没有图像");
                return;
            }

            int w = _viewer.Image.Width;
            int h = _viewer.Image.Height;

            // 模拟 OpenCV 侧：Bitmap -> CV_8UC3 连续数组 -> 构造 Mat -> 再取回显示
            byte[] bgrBytes = CvInterop.ToBgrBytes(_viewer.Image);
            Bitmap roundTrip = CvInterop.FromPixels(bgrBytes, w, h, CvPixelLayout.Bgr24);

            _viewer.SetImage(roundTrip, false);

            SetTransientMessage(string.Format(
                "OpenCV 往返完成：{0}x{1}，BGR 数组 {2} 字节（等价 CV_8UC3，step={3}）",
                w, h, bgrBytes.Length, w * 3));
        }

        private void OnExportShapes(object sender, EventArgs e)
        {
            string text = _viewer.ExportShapesToCvText();
            if (string.IsNullOrEmpty(text))
            {
                MessageBox.Show(this, "当前没有可导出的图形。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (SaveFileDialog dlg = new SaveFileDialog())
            {
                dlg.Title = "导出图形几何参数";
                dlg.Filter = "文本文件|*.txt|所有文件|*.*";
                dlg.FileName = "shapes.txt";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                File.WriteAllText(dlg.FileName, text);
                SetTransientMessage("已导出到 " + dlg.FileName);
            }
        }

        private void OnViewerMouseMove(object sender, ImagePointEventArgs e)
        {
            _lblCoord.Text = string.Format("坐标: X={0:F1}  Y={1:F1}", e.ImagePoint.X, e.ImagePoint.Y);

            if (e.PixelColor.HasValue)
            {
                Color c = e.PixelColor.Value;
                _lblPixel.Text = string.Format("像素: ({0},{1},{2})", c.R, c.G, c.B);
            }
            else
            {
                _lblPixel.Text = "像素: —";
            }
        }

        #endregion

        #region 状态同步

        private void UpdateStatus()
        {
            _lblZoom.Text = string.Format("缩放: {0:F0}%", _viewer.Zoom * 100f);
            _lblCount.Text = "图形: " + _viewer.Shapes.Count;

            // 只要存在选中图形，就实时反映它的几何参数（拖拽时随之变化），
            // 并顶掉此前显示的临时提示
            IGeometryShape selected = _viewer.GetSelectedShape();
            if (selected != null)
            {
                _lblSelection.Text = selected.ToGeometryString();
                _transientMessage = false;
            }
            else if (!_transientMessage)
            {
                _lblSelection.Text = "";
            }
        }

        /// <summary>显示一条临时提示；下一次几何更新时会被覆盖。</summary>
        private void SetTransientMessage(string text)
        {
            _lblSelection.Text = text;
            _transientMessage = true;
        }

        private void RefreshShapeList()
        {
            // 列表项文本取自 shape.ToString()（即几何参数），
            // 因此拖动图形时必须整表重建，否则显示的数值不会跟着变。
            // 图形通常只有个位数，重建开销可忽略；重建前后保持选中项不变。
            int selectedIndex = _shapeList.SelectedIndex;

            _syncingSelection = true;
            try
            {
                _shapeList.BeginUpdate();
                _shapeList.Items.Clear();
                foreach (IGeometryShape s in _viewer.Shapes)
                {
                    if (s != null) _shapeList.Items.Add(s);
                }
                _shapeList.EndUpdate();

                if (selectedIndex >= 0 && selectedIndex < _shapeList.Items.Count)
                    _shapeList.SelectedIndex = selectedIndex;
            }
            finally
            {
                _syncingSelection = false;
            }

        }

        private void SyncListSelection()
        {
            _syncingSelection = true;
            try
            {
                IGeometryShape selected = _viewer.GetSelectedShape();
                if (selected == null)
                {
                    _shapeList.SelectedIndex = -1;
                }
                else
                {
                    int idx = _shapeList.Items.IndexOf(selected);
                    if (idx >= 0) _shapeList.SelectedIndex = idx;
                }
            }
            finally
            {
                _syncingSelection = false;
            }
        }

        #endregion

        private void InitializeComponent()
        {
            this.SuspendLayout();
            // 
            // MainForm
            // 
            this.ClientSize = new System.Drawing.Size(284, 261);
            this.Name = "MainForm";
            this.ResumeLayout(false);

        }
    }
}
