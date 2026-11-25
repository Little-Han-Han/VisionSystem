using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using VisionPlatform.Flow;
using VisionPlatform.Framework.Modules;
using VisionPlatform.Native;
using VisionSystem.Controls;
using VisionSystem.Controls.Geometry;

namespace VisionPlatform.UI
{
    /// <summary>
    /// 无监督缺陷检测的「样本 / 训练」窗体。
    /// <para>
    /// 三件事合成一个窗体，因为它们是同一条工作流上的先后步骤：
    /// <list type="number">
    /// <item>一次加载多张图片并同屏排开（缩略图墙），逐张手标 OK / NG；</item>
    /// <item>框出检测区域（ROI），对整批样本保持一致；</item>
    /// <item>用 OK 样本训练 PaDiM，或拿现有模型对整批已标注样本跑一遍，看漏检 / 过杀。</item>
    /// </list>
    /// </para>
    /// <para>
    /// 【为什么必须能标注 NG】无监督训练只用 OK 样本，但"模型到底能不能用"只能靠 NG 样本验证。
    /// 没有 NG 的话，阈值调松调紧都无从判断 —— 这是这类算法落地时最常见的一步缺失。
    /// </para>
    /// <para>
    /// 【为什么窗体独占一个算法上下文】与算法调试页同理：训练/验证会重新加载模型，
    /// 借用主界面那个上下文会把正在连续运行的流程的模型顶掉（表现为"跑着跑着第一帧失败"）。
    /// </para>
    /// </summary>
    public sealed class AnomalyTrainForm : Form
    {
        private const int LabelNone = 0;
        private const int LabelOk = 1;
        private const int LabelNg = 2;

        /// <summary>一张样本：图 + 人工标注 + 最近一次预测结果。</summary>
        private sealed class Sample
        {
            public string Path;
            public Bitmap Image;
            public int Label = LabelNone;
            public double Score = double.NaN;      // 0~1 良品得分（越高越好，0.5 = 模型自带阈值线）
            public bool? Predicted;                // 模型判定：true = OK
            public ListViewItem Item;
        }

        private readonly List<Sample> _samples = new List<Sample>();
        private readonly ImageList _thumbs = new ImageList();
        private readonly VisionAlgo _algo = new VisionAlgo();

        private readonly ImageViewer _viewer = new ImageViewer();
        private readonly ListView _list = new ListView();
        private readonly TextBox _txtModel = new TextBox();
        private readonly ComboBox _cmbInput = new ComboBox();
        private readonly NumericUpDown _numThreshold = new NumericUpDown();
        private readonly NumericUpDown _numMinArea = new NumericUpDown();
        private readonly NumericUpDown _numAreaLimit = new NumericUpDown();
        private readonly TextBox _log = new TextBox();
        private readonly Label _lblCount = new Label();
        private readonly Label _lblVerdict = new Label();
        private readonly ToolTip _tip = new ToolTip();

        private bool _busy;

        /// <summary>训练摘要（由后台线程写入，UI 线程读出 —— 不在后台线程碰控件）。</summary>
        private string _lastTrainSummary = "";

        private Rectangle _roi = Rectangle.Empty;

        /// <summary>训练产出的模型名（关窗时由调用方写回节点参数）。</summary>
        public string ModelName { get; private set; }

        /// <summary>检测区域（原图坐标；宽高 0 = 整图）。</summary>
        public Rectangle Roi { get { return _roi; } }

        public double Threshold { get { return (double)_numThreshold.Value; } }
        public int MinArea { get { return (int)_numMinArea.Value; } }
        public double AreaLimit { get { return (double)_numAreaLimit.Value; } }

        /// <summary>参数被改过（调用方据此决定要不要落盘）。</summary>
        public bool ParamsChanged { get; private set; }

        /// <summary>训练完成 / 参数应用后触发，参数是模型名（供宿主刷新卡片）。</summary>
        public event EventHandler ModelChanged;

        public AnomalyTrainForm(string modelName, Rectangle roi, double threshold,
                                int minArea, double areaLimit)
        {
            ModelName = (modelName ?? "").Trim();
            _roi = roi;

            Text = "异常检测 · 样本标注与训练";
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(1000, 660);
            Size = new Size(1200, 780);
            Font = UiTheme.FontBody;
            BackColor = UiTheme.ColorAppBg;      // 窗体外框用 AppBg，内容面板仍是 PanelBg
            ForeColor = UiTheme.ColorTextPrimary;
            KeyPreview = true;

            BuildUi();
            InitSettings(threshold, minArea, areaLimit);

            LoadSamplesFromModel(ModelName);

            KeyDown += OnKeyDown;
        }

        // =====================================================================
        //  版面
        // =====================================================================

        private void BuildUi()
        {
            // ---- 顶部工具条 ----
            var top = new Panel { Dock = DockStyle.Top, Height = 46, BackColor = UiTheme.ColorPanelBg };

            int x = 8;
            x = AddButton(top, "添加图片…", UiButtonKind.Neutral, 90, x, OnAddImages);
            x = AddButton(top, "添加文件夹…", UiButtonKind.Neutral, 104, x, OnAddFolder);
            x = AddButton(top, "移除选中", UiButtonKind.Neutral, 80, x, OnRemoveSelected);
            x = AddButton(top, "清空", UiButtonKind.Neutral, 62, x, OnClearAll);

            x += 14;
            x = AddButton(top, "标为 OK", UiButtonKind.Start, 80, x, delegate { OnMark(LabelOk); });
            x = AddButton(top, "标为 NG", UiButtonKind.Stop, 80, x, delegate { OnMark(LabelNg); });
            x = AddButton(top, "取消标注", UiButtonKind.Neutral, 84, x, delegate { OnMark(LabelNone); });

            x += 14;
            x = AddButton(top, "绘制 ROI", UiButtonKind.Grab, 84, x, OnDrawRoi);
            x = AddButton(top, "清除 ROI", UiButtonKind.Neutral, 84, x, OnClearRoi);

            _lblCount.AutoSize = true;
            _lblCount.Location = new Point(x + 16, 15);
            _lblCount.ForeColor = UiTheme.ColorTextSecondary;
            _lblCount.Text = "样本 0";
            top.Controls.Add(_lblCount);
            Controls.Add(top);

            // ---- 底部：设置 + 动作 + 日志 ----
            // 高度按内容算，不再拍 210：标签 12 / 输入 32 / 按钮 68 / 结论 100 / 日志 126，
            // 日志框高度 = 面板高 - 126 - 14。写死 210 时日志(108起)会把结论行(102~124)压掉半行，
            // 现场看到的就是"日志框上沿有一行被切掉一半的字"。
            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 236, BackColor = UiTheme.ColorPanelBg };
            bottom.Paint += delegate(object s, PaintEventArgs e)
            {
                using (var pen = new Pen(UiTheme.ColorDivider, 1))
                    e.Graphics.DrawLine(pen, 0, 0, bottom.Width, 0);
            };

            var lblModel = new Label
            {
                Text = "模型名", AutoSize = true, Location = new Point(12, 12),
                ForeColor = UiTheme.ColorTextSecondary
            };
            bottom.Controls.Add(lblModel);

            _txtModel.Location = new Point(12, 32);
            _txtModel.Width = 210;
            // 深色主题下 TextBox / ComboBox 不会自动跟着窗体变，必须显式上色
            // （用主题色而不是写死 ARGB：浅色主题下硬编码的深底会变成"黑框白字孤岛"）
            _txtModel.BackColor = UiTheme.ColorCardBg;
            _txtModel.ForeColor = UiTheme.ColorTextPrimary;
            _txtModel.BorderStyle = BorderStyle.FixedSingle;
            _txtModel.Text = ModelName;
            _txtModel.TextChanged += delegate { ParamsChanged = true; };
            bottom.Controls.Add(_txtModel);
            _tip.SetToolTip(_txtModel, "模型会存到 Models\\Anomaly\\<模型名>\\；重名即覆盖（相当于重训）");

            var lblInput = new Label
            {
                Text = "分析分辨率", AutoSize = true, Location = new Point(238, 12),
                ForeColor = UiTheme.ColorTextSecondary
            };
            bottom.Controls.Add(lblInput);

            _cmbInput.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbInput.FlatStyle = FlatStyle.Flat;
            _cmbInput.BackColor = UiTheme.ColorCardBg;
            _cmbInput.ForeColor = UiTheme.ColorTextPrimary;
            _cmbInput.Location = new Point(238, 32);
            _cmbInput.Width = 92;
            _cmbInput.Items.AddRange(new object[] { "128", "192", "256", "320" });
            _cmbInput.SelectedIndex = 2;
            bottom.Controls.Add(_cmbInput);
            _tip.SetToolTip(_cmbInput, "训练与推理都把 ROI 缩放到这个边长；越大越细、越慢。改这个值必须重新训练");

            var lblThr = new Label
            {
                Text = "判定得分", AutoSize = true, Location = new Point(346, 12),
                ForeColor = UiTheme.ColorTextSecondary
            };
            bottom.Controls.Add(lblThr);

            SetupNum(_numThreshold, 346, 32, 84, 0.02m, 1m, 3, 0.01m);
            _tip.SetToolTip(_numThreshold,
                "0~1 的良品得分，越高越好：1 = 完全正常，0.5 = 模型自带阈值线。"
                + "得分低于它判 NG（及格线）；往右更严、往左更松");

            var lblArea = new Label
            {
                Text = "最小缺陷面积", AutoSize = true, Location = new Point(440, 12),
                ForeColor = UiTheme.ColorTextSecondary
            };
            bottom.Controls.Add(lblArea);
            SetupNum(_numMinArea, 440, 32, 80, 1, 1000000, 0, 1m);

            var lblLimit = new Label
            {
                Text = "面积占比上限", AutoSize = true, Location = new Point(530, 12),
                ForeColor = UiTheme.ColorTextSecondary
            };
            bottom.Controls.Add(lblLimit);
            SetupNum(_numAreaLimit, 530, 32, 80, 0, 1, 4, 0.001m);
            _tip.SetToolTip(_numAreaLimit, "0 = 不用面积判；>0 时超阈值像素占比超过它判 NG");

            int bx = 8, by = 68;
            bx = AddBottomButton(bottom, "开始训练（用 OK 样本）", UiButtonKind.Start, 176, bx, by, OnTrain);
            bx = AddBottomButton(bottom, "验证全部样本", UiButtonKind.Grab, 124, bx, by, OnValidateAll);
            bx = AddBottomButton(bottom, "应用到节点", UiButtonKind.Neutral, 108, bx, by, OnApplyToNode);
            bx = AddBottomButton(bottom, "关闭", UiButtonKind.Neutral, 76, bx, by, delegate { Close(); });

            _log.Location = new Point(12, 126);
            _log.Multiline = true;
            _log.ScrollBars = ScrollBars.Vertical;
            _log.ReadOnly = true;
            _log.Height = 96;
            _log.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top | AnchorStyles.Bottom;
            _log.BackColor = UiTheme.ColorViewBg;
            // 日志框底是图像区的恒深色，字必须是"图像区色"，不能跟主题变 ——
            // 浅色主题下 ColorTextPrimary 是 #1B1F26，写在 #181820 上就是"看不见字"。
            _log.ForeColor = UiTheme.ColorTextOnView;
            _log.Font = new Font("Consolas", 8.5f);
            _log.Text = "";
            bottom.Controls.Add(_log);

            bottom.Resize += delegate
            {
                if (_log != null) _log.Width = Math.Max(200, bottom.Width - _log.Left - 14);
                if (_lblVerdict != null) _lblVerdict.Width = Math.Max(120, bottom.Width - _lblVerdict.Left - 14);
            };

            _lblVerdict.AutoSize = false;
            _lblVerdict.Height = 22;
            _lblVerdict.Location = new Point(8, 100);
            _lblVerdict.Width = 300;
            _lblVerdict.Font = UiTheme.FontSmall;
            _lblVerdict.AutoEllipsis = true;
            _lblVerdict.ForeColor = UiTheme.ColorTextSecondary;
            _lblVerdict.Text = "先加图片 → 标 OK/NG → 框 ROI → 训练";
            bottom.Controls.Add(_lblVerdict);

            Controls.Add(bottom);

            // ---- 左侧样本墙 + 右侧预览 ----
            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterDistance = 360,
                BackColor = UiTheme.ColorPanelBg
            };
            split.Panel1.BackColor = UiTheme.ColorPanelBg;
            split.Panel2.BackColor = UiTheme.ColorViewBg;

            _thumbs.ImageSize = new Size(96, 72);
            _thumbs.ColorDepth = ColorDepth.Depth32Bit;

            _list.Dock = DockStyle.Fill;
            _list.View = View.LargeIcon;
            _list.LargeImageList = _thumbs;
            _list.MultiSelect = true;
            _list.HideSelection = false;
            _list.BackColor = UiTheme.ColorViewBg;
            _list.ForeColor = UiTheme.ColorTextOnView;
            _list.BorderStyle = BorderStyle.None;
            // 【必须自绘】LargeIcon 视图的标签文字**不读 ForeColor**，用 SystemColors.WindowText 画：
            // 深色底(#181820)上就是黑字黑底，选中时又被系统高亮刷成白底浅字 —— 两头都看不清。
            // 自绘后底色/字色/标注色全由 UiTheme 决定，与缩略图边框一一对应。
            _list.OwnerDraw = true;
            _list.DrawItem += OnListDrawItem;
            _list.SelectedIndexChanged += OnListSelectionChanged;
            _list.MouseDoubleClick += delegate { OnMark(NextLabelOfSelection()); };
            split.Panel1.Controls.Add(_list);

            _viewer.Dock = DockStyle.Fill;
            _viewer.Mode = InteractionMode.Select;
            _viewer.OwnsImage = false;
            _viewer.ShapeCreated += OnViewerShapeCreated;
            split.Panel2.Controls.Add(_viewer);

            Controls.Add(split);
            split.BringToFront();

            AcceptButton = null;
            CancelButton = null;
        }

        private int AddButton(Panel host, string text, UiButtonKind kind, int w, int x, EventHandler onClick)
        {
            // 必须是 UiButton（自绘）：FlatStyle.System 的按钮配色由系统主题决定，
            // 深色主题下车间的表现就是"浅灰底 + 灰字"，与右侧面板格格不入。
            var b = new UiButton
            {
                Text = text, Left = x, Top = 10, Width = w, Height = 26,
                Kind = kind, Font = UiTheme.FontSmall
            };
            b.Click += onClick;
            host.Controls.Add(b);
            return x + w + 6;
        }

        private int AddBottomButton(Panel host, string text, UiButtonKind kind, int w, int x, int y,
                                    EventHandler onClick)
        {
            var b = new UiButton
            {
                Text = text, Left = x, Top = y, Width = w, Height = 30,
                Kind = kind, Font = UiTheme.FontSmall
            };
            b.Click += onClick;
            host.Controls.Add(b);
            return x + w + 6;
        }

        private static void SetupNum(NumericUpDown n, int x, int y, int w,
                                     decimal min, decimal max, int decimals, decimal inc)
        {
            n.Location = new Point(x, y);
            n.Width = w;
            n.Minimum = min;
            n.Maximum = max;
            n.DecimalPlaces = decimals;
            n.Increment = inc;
            n.Value = n.Minimum;
        }

        private void InitSettings(double threshold, int minArea, double areaLimit)
        {
            // 判定阈值在 1.0.14.4 改成 0~1 的「得分」。老方案里存的是绝对马氏距离（62.05 这种），
            // 直接 Clamp 会压成 1.0（最松），用户一点「应用」就把原设置改坏了 ——
            // 所以按构造时传进来的模型名读出模型自带阈值，折回等价得分再显示。
            double modelThr = 0;
            AnomalyModelInfo mi = AnomalyModelCatalog.Describe(ModelName);
            if (mi != null && mi.Threshold > 0) modelThr = mi.Threshold;

            _numThreshold.Value = Clamp((decimal)AnomalyScore.NormalizeThreshold(threshold, modelThr),
                                        _numThreshold.Minimum, _numThreshold.Maximum);
            _numMinArea.Value = Clamp(minArea, _numMinArea.Minimum, _numMinArea.Maximum);
            _numAreaLimit.Value = Clamp((decimal)areaLimit, _numAreaLimit.Minimum, _numAreaLimit.Maximum);

            _numThreshold.ValueChanged += delegate { ParamsChanged = true; };
            _numMinArea.ValueChanged += delegate { ParamsChanged = true; };
            _numAreaLimit.ValueChanged += delegate { ParamsChanged = true; };

            RescanModelsHint();
        }

        private static decimal Clamp(decimal v, decimal min, decimal max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        // =====================================================================
        //  样本管理
        // =====================================================================

        private static readonly string[] ImageExts =
            { ".bmp", ".jpg", ".jpeg", ".png", ".tif", ".tiff", ".gif" };

        private void OnAddImages(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "选择样本图片（可多选）";
                dlg.Multiselect = true;
                dlg.Filter = "图片|*.bmp;*.jpg;*.jpeg;*.png;*.tif;*.tiff;*.gif|全部文件|*.*";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                AddFiles(dlg.FileNames);
            }
        }

        private void OnAddFolder(object sender, EventArgs e)
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "选择样本文件夹（一次性加入其中所有图片）";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    string[] files = Directory.GetFiles(dlg.SelectedPath);
                    Array.Sort(files, StringComparer.OrdinalIgnoreCase);

                    var list = new List<string>();
                    foreach (string f in files)
                        if (Array.IndexOf(ImageExts, System.IO.Path.GetExtension(f).ToLowerInvariant()) >= 0)
                            list.Add(f);

                    if (list.Count == 0)
                    {
                        Log("文件夹里没有图片：" + dlg.SelectedPath);
                        return;
                    }
                    AddFiles(list.ToArray());
                }
                catch (Exception ex) { Log("读文件夹失败：" + ex.Message); }
            }
        }

        private void AddFiles(string[] files)
        {
            if (files == null) return;

            int added = 0, failed = 0;
            foreach (string f in files)
            {
                if (string.IsNullOrEmpty(f) || !File.Exists(f)) { failed++; continue; }
                if (FindSample(f) != null) continue;    // 同一张图不重复加

                try
                {
                    // 整图读入（推理要用原分辨率）；缩略图另建，不共享像素数据
                    var bmp = new Bitmap(f);
                    var s = new Sample { Path = f, Image = bmp, Label = LabelNone };

                    _thumbs.Images.Add(MakeThumb(bmp));
                    s.Item = new ListViewItem(System.IO.Path.GetFileName(f), _thumbs.Images.Count - 1);
                    s.Item.Tag = s;
                    _list.Items.Add(s.Item);

                    _samples.Add(s);
                    added++;
                }
                catch (Exception ex)
                {
                    failed++;
                    Log("读图失败：" + System.IO.Path.GetFileName(f) + " —— " + ex.Message);
                }
            }

            Log(string.Format(CultureInfo.InvariantCulture, "加入 {0} 张（跳过重复/失败 {1} 张），共 {2} 张",
                              added, failed, _samples.Count));

            UpdateCount();
            if (added > 0 && _list.SelectedItems.Count == 0 && _samples.Count > 0)
            {
                _samples[0].Item.Selected = true;
                _samples[0].Item.Focused = true;
            }
        }

        private Bitmap MakeThumb(Bitmap src)
        {
            int w = _thumbs.ImageSize.Width, h = _thumbs.ImageSize.Height;
            var bmp = new Bitmap(w, h);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(UiTheme.ColorViewBg);
                float s = Math.Min((float)w / src.Width, (float)h / src.Height);
                int dw = Math.Max(1, (int)(src.Width * s));
                int dh = Math.Max(1, (int)(src.Height * s));
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
                g.DrawImage(src, (w - dw) / 2, (h - dh) / 2, dw, dh);
            }
            return bmp;
        }

        private Sample FindSample(string path)
        {
            foreach (Sample s in _samples)
                if (string.Equals(s.Path, path, StringComparison.OrdinalIgnoreCase)) return s;

            return null;
        }

        private void OnRemoveSelected(object sender, EventArgs e)
        {
            var remove = new List<Sample>();
            foreach (ListViewItem item in _list.SelectedItems)
            {
                var s = item.Tag as Sample;
                if (s != null) remove.Add(s);
            }

            foreach (Sample s in remove)
            {
                _list.Items.Remove(s.Item);
                _samples.Remove(s);
                try { if (s.Image != null) s.Image.Dispose(); } catch (Exception) { }
            }

            if (_samples.Count == 0) _viewer.ClearImage();
            UpdateCount();
        }

        private void OnClearAll(object sender, EventArgs e)
        {
            if (_samples.Count > 0)
            {
                if (MessageBox.Show(this, "清空已加载的 " + _samples.Count + " 张样本？（不影响已训练的模型）",
                        "异常检测", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                    return;
            }

            _list.Items.Clear();
            _thumbs.Images.Clear();
            foreach (Sample s in _samples)
                try { if (s.Image != null) s.Image.Dispose(); } catch (Exception) { }
            _samples.Clear();
            _viewer.ClearImage();
            UpdateCount();
        }

        private void OnMark(int label)
        {
            ListView.SelectedListViewItemCollection sel = _list.SelectedItems;
            if (sel == null || sel.Count == 0)
            {
                Log("先在上面的样本墙里选中要标注的图片（可按住 Ctrl / Shift 多选）");
                return;
            }

            foreach (ListViewItem item in sel)
            {
                var s = item.Tag as Sample;
                if (s == null) continue;
                s.Label = label;
                s.Score = double.NaN;
                s.Predicted = null;
                UpdateItemText(s);
            }

            // 标完自动跳到下一张，连着标几十张时不用一直点鼠标
            AdvanceSelection();
            UpdateCount();
        }

        /// <summary>双击时按"未标 → OK → NG → 未标"循环，单手就能标完一批。</summary>
        private int NextLabelOfSelection()
        {
            if (_list.SelectedItems.Count == 0) return LabelOk;

            var s = _list.SelectedItems[0].Tag as Sample;
            if (s == null) return LabelOk;

            return s.Label == LabelNone ? LabelOk : (s.Label == LabelOk ? LabelNg : LabelNone);
        }

        private void AdvanceSelection()
        {
            if (_list.SelectedItems.Count != 1) return;

            int idx = _list.SelectedIndices[0];
            if (idx < 0 || idx + 1 >= _list.Items.Count) return;

            _list.SelectedItems.Clear();
            _list.Items[idx + 1].Selected = true;
            _list.Items[idx + 1].Focused = true;
            _list.EnsureVisible(idx + 1);
        }

        private void UpdateItemText(Sample s)
        {
            if (s == null || s.Item == null) return;

            string flag = s.Label == LabelOk ? "OK" : (s.Label == LabelNg ? "NG" : "未标");
            string pred = "";
            if (s.Predicted.HasValue && !double.IsNaN(s.Score))
                pred = string.Format(CultureInfo.InvariantCulture, " · {0} {1:F2}",
                                     s.Predicted.Value ? "判OK" : "判NG", s.Score);

            // 名称压到 14 字：缩略图列的可用宽度只有 ~100px，长名字会被 ListView 折成 4 行，
            // 一屏能看的样本数直接砍半，而且每行都短得读不出关键差别。
            string name = System.IO.Path.GetFileName(s.Path);
            if (name.Length > 14) name = name.Substring(0, 13) + "…";

            s.Item.Text = "[" + flag + "] " + name + pred;

            // 颜色区分：标注正确绿、判错红、未标灰 —— 一屏就能看出哪里有问题。
            // 注意 LargeIcon 视图不看 ForeColor，真正生效的是 OnListDrawItem 里的自绘；
            // 这里仍写一遍是为了 OwnerDraw 被关掉时不至于没有语义色。
            s.Item.ForeColor = ItemTextColor(s);
        }

        /// <summary>
        /// 样本墙的标签色：判对绿 / 判错红 / 已标未验主文本色 / 未标次要色。
        /// <para>【一律取 OnView 系列】缩略图墙底是 <see cref="UiTheme.ColorViewBg"/>（恒深色），
        /// 主题色里的 Ok/Ng 在浅色板下偏暗、TextPrimary 更是深灰，写在深底上读不出来。</para>
        /// </summary>
        private static Color ItemTextColor(Sample s)
        {
            if (s == null) return UiTheme.ColorTextOnViewDim;

            if (s.Label != LabelNone && s.Predicted.HasValue)
            {
                bool right = (s.Label == LabelOk) == s.Predicted.Value;
                return right ? UiTheme.ColorOkOnView : UiTheme.ColorNgOnView;
            }
            return s.Label != LabelNone ? UiTheme.ColorTextOnView : UiTheme.ColorTextOnViewDim;
        }

        /// <summary>缩略图边框色 = 人工标注（绿 OK / 红 NG / 暗灰 未标），同样不随主题。</summary>
        private static Color ItemEdgeColor(Sample s)
        {
            if (s == null || s.Label == LabelNone) return Color.FromArgb(0x50, 0x50, 0x5C);
            return s.Label == LabelOk ? UiTheme.ColorOkOnView : UiTheme.ColorNgOnView;
        }

        /// <summary>
        /// 缩略图墙自绘。
        /// <para>
        /// 【为什么非自绘不可】<see cref="View.LargeIcon"/> 的标签文字不走 <c>ForeColor</c>，
        /// 用 <c>SystemColors.WindowText</c>（黑）画在 <c>ColorViewBg</c>(#181820) 上 —— 未选中时
        /// 黑字黑底，选中时又被系统高亮刷成白底浅字，两头都"看不清"。自绘后：底色随主题、
        /// 字色带语义、缩略图外框标出 OK/NG，选中态用强调色描边（不再是系统高亮那种糊成一片的白块）。
        /// </para>
        /// </summary>
        private void OnListDrawItem(object sender, DrawListViewItemEventArgs e)
        {
            if (e.Item == null) return;

            ListViewItem item = e.Item;
            var s = item.Tag as Sample;
            bool selected = (e.State & ListViewItemStates.Selected) != 0;

            Graphics g = e.Graphics;
            Rectangle rc = e.Bounds;
            if (rc.Width <= 2 || rc.Height <= 2) return;

            // ① 底色（选中 = 略亮的固定深色 + 强调色描边，未选中 = 画布色）。
            //    选中底色不能用 ColorCardBg：浅色板下它是 #EAECF0，会把深色缩略图墙刷出一块白板。
            Color selBg = Color.FromArgb(0x2E, 0x2E, 0x38);
            using (var b = new SolidBrush(selected ? selBg : UiTheme.ColorViewBg))
                g.FillRectangle(b, rc);
            if (selected)
            {
                using (var p = new Pen(UiTheme.ColorAccent))
                    g.DrawRectangle(p, rc.X, rc.Y, rc.Width - 1, rc.Height - 1);
            }

            // ② 缩略图：水平居中贴顶
            const int pad = 5;
            int iw = _thumbs.ImageSize.Width, ih = _thumbs.ImageSize.Height;
            var imgRect = new Rectangle(rc.X + Math.Max(0, (rc.Width - iw) / 2), rc.Y + pad, iw, ih);

            Image img = null;
            int idx = item.ImageIndex;
            if (idx >= 0 && idx < _thumbs.Images.Count) img = _thumbs.Images[idx];
            if (img != null) g.DrawImage(img, imgRect);

            // ③ 缩略图外框 = 标注：绿 OK / 红 NG / 灰 未标
            using (var pen = new Pen(ItemEdgeColor(s), 2f))
                g.DrawRectangle(pen, imgRect.X - 1, imgRect.Y - 1, imgRect.Width + 1, imgRect.Height + 1);

            // ④ 标签文字（缩略图下方，自动换行、末行省略号）
            int textTop = imgRect.Bottom + 4;
            int textH = rc.Bottom - textTop - 2;
            if (textH < 12 || rc.Width < 20) return;

            var textRect = new Rectangle(rc.X + 3, textTop, rc.Width - 6, textH);
            TextRenderer.DrawText(g, item.Text, UiTheme.FontSmall, textRect, ItemTextColor(s),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak
                | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        private void UpdateCount()
        {
            int ok = 0, ng = 0;
            foreach (Sample s in _samples)
            {
                if (s.Label == LabelOk) ok++;
                else if (s.Label == LabelNg) ng++;
            }

            _lblCount.Text = string.Format(CultureInfo.InvariantCulture,
                "样本 {0} · OK {1} · NG {2} · 未标 {3}", _samples.Count, ok, ng, _samples.Count - ok - ng);
        }

        private void OnListSelectionChanged(object sender, EventArgs e)
        {
            if (_list.SelectedItems.Count == 0) return;

            var s = _list.SelectedItems[0].Tag as Sample;
            if (s == null || s.Image == null) return;

            _viewer.SetImage(s.Image, true);
            RestoreRoiShape();
        }

        // =====================================================================
        //  ROI
        // =====================================================================

        private void OnDrawRoi(object sender, EventArgs e)
        {
            if (_viewer.Image == null)
            {
                Log("先选中一张样本，再框检测区域");
                return;
            }
            _viewer.Mode = InteractionMode.DrawRectangle;
            Log("在图上按住左键拖一个框 —— 框住产品表面那块（别把夹具、背景框进来）");
        }

        private void OnClearRoi(object sender, EventArgs e)
        {
            _roi = Rectangle.Empty;
            ParamsChanged = true;
            RemoveRoiShapes();
            Log("检测区域已清除（运行期按整图处理）");
        }

        private void OnViewerShapeCreated(object sender, ShapeEventArgs e)
        {
            var rect = e != null ? e.Shape as RectangleShape : null;
            if (rect == null) return;

            _viewer.Mode = InteractionMode.Select;

            RemoveRoiShapes();
            rect.Name = "检测区域";
            rect.StrokeColor = UiTheme.ColorAccent;
            rect.LineWidth = 2f;
            rect.IsEditable = true;
            rect.IsSelected = false;
            _viewer.Shapes.Add(rect);

            _roi = ToRect(rect);
            ParamsChanged = true;
            Log(string.Format(CultureInfo.InvariantCulture, "检测区域：X={0} Y={1}，{2}×{3}",
                              _roi.X, _roi.Y, _roi.Width, _roi.Height));
        }

        /// <summary>按当前 ROI 在预览上摆一个框（没有 ROI 时不画）。</summary>
        private void RestoreRoiShape()
        {
            RemoveRoiShapes();
            if (_roi.Width <= 0 || _roi.Height <= 0) return;
            if (_viewer.Image == null) return;

            var rect = new RectangleShape(new RectangleF(_roi.X, _roi.Y, _roi.Width, _roi.Height))
            {
                Name = "检测区域",
                StrokeColor = UiTheme.ColorAccent,
                LineWidth = 2f,
                IsEditable = true
            };
            _viewer.Shapes.Add(rect);
        }

        private void RemoveRoiShapes()
        {
            for (int i = _viewer.Shapes.Count - 1; i >= 0; i--)
                if (_viewer.Shapes[i] is RectangleShape) _viewer.Shapes.RemoveAt(i);
        }

        private Rectangle ToRect(RectangleShape r)
        {
            if (r == null) return Rectangle.Empty;

            RectangleF f = r.Rect;
            var roi = new Rectangle((int)Math.Round(f.X), (int)Math.Round(f.Y),
                                    (int)Math.Round(f.Width), (int)Math.Round(f.Height));
            if (_viewer.Image == null) return roi;

            Rectangle real = Rectangle.Intersect(roi, new Rectangle(0, 0, _viewer.Image.Width, _viewer.Image.Height));
            return (real.Width > 0 && real.Height > 0) ? real : Rectangle.Empty;
        }

        // =====================================================================
        //  训练
        // =====================================================================

        private async void OnTrain(object sender, EventArgs e)
        {
            if (_busy) return;

            string name = AnomalyModelCatalog.SanitizeName(_txtModel.Text);
            if (name.Length == 0)
            {
                MessageBox.Show(this, "请先填模型名（会建成 Models\\Anomaly\\<模型名>\\）。",
                    "异常检测", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var okSamples = new List<Sample>();
            foreach (Sample s in _samples) if (s.Label == LabelOk) okSamples.Add(s);

            if (okSamples.Count < 2)
            {
                MessageBox.Show(this, "训练至少需要 2 张标为 OK 的样本（建议 20 张以上，覆盖正常的外观波动）。",
                    "异常检测", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (okSamples.Count < 15)
            {
                // 维数收缩规则是「每 3 张样本养 1 维」（算法侧 kSamplesPerDim），
                // 样本不足时维度会被压得很低 —— 提前讲清楚，别让人训完才发现模型没判别力。
                int dimCap = Math.Max(2, okSamples.Count / 3);
                Log("提示：OK 样本 " + okSamples.Count + " 张 → 特征维度最多 " + dimCap
                    + " 维（每 3 张养 1 维）。维度这么低，正常与缺陷往往分不开；"
                    + "建议补到 15 张以上（20~30 张最稳）。");
            }

            string root = AnomalyModelCatalog.EnsureRoot();
            string dir = System.IO.Path.Combine(root, name);

            int inputSize;
            if (!int.TryParse(Convert.ToString(_cmbInput.SelectedItem), out inputSize) || inputSize < 64)
                inputSize = 256;

            // 特征提取器可选：放了 Models\Anomaly\_extractor\extractor.xml 就用它（表达能力更强），
            // 没有就用内置描述子 —— 不需要任何预训练模型，开箱能用。
            string extractor = System.IO.Path.Combine(
                System.IO.Path.Combine(root, "_extractor"), "extractor.xml");
            int featKind = File.Exists(extractor) ? NativeAnomaly.FeatOpenVino : NativeAnomaly.FeatHandcraft;

            try
            {
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "无法创建模型目录：\n" + dir + "\n\n" + ex.Message,
                    "异常检测", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            SetBusy(true);
            Log("");
            Log("开始训练：" + name + " · " + okSamples.Count + " 张 OK · "
                + inputSize + "px · " + (featKind == NativeAnomaly.FeatOpenVino ? "OpenVINO 特征" : "内置描述子"));

            Rectangle roi = _roi;
            int total = okSamples.Count;
            var images = new List<Bitmap>();
            foreach (Sample s in okSamples) images.Add(s.Image);

            try
            {
                string error = await Task.Run(() => TrainCore(dir, extractor, featKind, inputSize, roi, images));

                if (!string.IsNullOrEmpty(error))
                {
                    Log("训练失败：" + error);
                    SetVerdict("训练失败：" + error, false);
                    return;
                }

                ModelName = name;
                ParamsChanged = true;
                if (_lastTrainSummary.Length > 0)
                {
                    // 摘要是一整行（" · " 分隔若干段）。拆开输出，否则告警会被挤在
                    // 一行末尾、折行后还夹在中间，现场根本不看。
                    foreach (string seg in _lastTrainSummary.Split(
                                 new string[] { " · " }, StringSplitOptions.None))
                    {
                        string t = seg.Trim();
                        if (t.Length > 0) Log(t);
                    }
                }
                Log("训练成功：" + dir);
                Log("注意：训练集里的缺陷学不进去 —— 标成 OK 的图若本身有缺陷，检测时也会被一并放行。"
                    + "请点「验证全部样本」复核；若两组分数区间重叠，日志里会直接告警。");
                SetVerdict("训练完成 —— 建议点「验证全部样本」看漏检 / 过杀", true);

                SaveSampleList(dir);
                RescanModelsHint();

                Raise(ModelChanged);
            }
            catch (Exception ex)
            {
                Log("训练异常：" + ex.Message);
                SetVerdict("训练异常：" + ex.Message, false);
            }
            finally
            {
                SetBusy(false);
            }
        }

        /// <summary>真正的训练循环（后台线程跑；只在里面碰原生句柄，不碰任何控件）。</summary>
        private string TrainCore(string dir, string extractor, int featKind, int inputSize,
                                 Rectangle roi, List<Bitmap> images)
        {
            var begin = _algo.AnomalyTrainBegin(dir, extractor, featKind, inputSize);
            if (!begin.Success) return begin.Message;

            try
            {
                for (int i = 0; i < images.Count; i++)
                {
                    var r = _algo.AnomalyTrainAdd(images[i], roi);
                    if (!r.Success) return "第 " + (i + 1) + " 张失败：" + r.Message;
                }

                var fin = _algo.AnomalyTrainFinish();
                if (!fin.Success) return fin.Message;

                // 不在这里 Log：本方法跑在后台线程，碰控件会抛跨线程异常
                _lastTrainSummary = fin.Value ?? "";

                // 训练完立刻重新加载，后续"验证"用的是新模型
                _algo.AnomalyInvalidate();
                return "";
            }
            finally
            {
                // 中途失败也要把会话收掉，否则下次 begin 会看到残留统计量
                _algo.AnomalyTrainCancel();
            }
        }

        private void RescanModelsHint()
        {
            List<AnomalyModelInfo> all = AnomalyModelCatalog.List();
            if (all.Count == 0) return;

            if (_txtModel.Text.Trim().Length == 0)
                _txtModel.Text = all[0].Name;
        }

        // =====================================================================
        //  验证
        // =====================================================================

        private async void OnValidateAll(object sender, EventArgs e)
        {
            if (_busy) return;

            string name = AnomalyModelCatalog.SanitizeName(_txtModel.Text);
            string dir = name.Length > 0 ? System.IO.Path.Combine(AnomalyModelCatalog.EnsureRoot(), name) : "";
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            {
                MessageBox.Show(this, "还没有可验证的模型：先训练，或在「模型名」里填一个已存在的模型。",
                    "异常检测", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var labeled = new List<Sample>();
            foreach (Sample s in _samples) if (s.Label != LabelNone) labeled.Add(s);

            if (labeled.Count == 0)
            {
                MessageBox.Show(this, "先把样本标成 OK / NG —— 没有人工标注就没有可比对的结论。",
                    "异常检测", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            SetBusy(true);
            Log("");
            Log("验证：" + dir + " · " + labeled.Count + " 张已标注样本");

            Rectangle roi = _roi;
            double scoreThr = (double)_numThreshold.Value;    // 0~1 得分
            int minArea = (int)_numMinArea.Value;
            double areaLimit = (double)_numAreaLimit.Value;

            try
            {
                string err = await Task.Run(() => ValidateCore(dir, roi, minArea, areaLimit, scoreThr, labeled));
                if (!string.IsNullOrEmpty(err))
                {
                    Log("验证失败：" + err);
                    SetVerdict("验证失败：" + err, false);
                    return;
                }

                ReportConfusion(labeled, areaLimit, scoreThr);
                foreach (Sample s in labeled) UpdateItemText(s);
            }
            catch (Exception ex)
            {
                Log("验证异常：" + ex.Message);
                SetVerdict("验证异常：" + ex.Message, false);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private string ValidateCore(string dir, Rectangle roi, int minArea,
                                    double areaLimit, double scoreThr, List<Sample> samples)
        {
            foreach (Sample s in samples)
            {
                // 阈值传 0：让原生库按**模型自带阈值**归一化，得分口径与运行期节点完全一致。
                // 判定线（scoreThr）只在 C# 侧参与比较，不改变热图与面积统计的口径。
                var r = _algo.AnomalyDetect(s.Image, dir, "CPU", roi, 0f, minArea, false);
                if (!r.Success) return r.Message;

                double score = AnomalyScore.FromRatio(r.Value.Stat.max_score);
                bool ok = !AnomalyScore.IsNg(score, scoreThr);
                if (ok && areaLimit > 0 && r.Value.Stat.area_ratio > areaLimit) ok = false;

                s.Score = score;
                s.Predicted = ok;
            }
            return "";
        }

        /// <summary>
        /// 输出混淆矩阵 + <b>得分分布的可分性诊断</b>。
        /// <para>
        /// 【为什么必须诊断可分性】只报"检出率 20% / 过杀率 50%"会把现场引向错误结论：
        /// 用户会以为"阈值没调好"。实测过一组现场数据 —— NG 组最低分比 OK 组最高分还低，
        /// 两组区间完全重叠，这时阈值填任何值都只能得到"全过"或"全杀"。
        /// 唯一正确的提示是"模型没学到能区分的特征，换参数无用"，而不是继续让人去调阈值。
        /// </para>
        /// </summary>
        /// <param name="scoreThr">用户当前的判定得分（0~1）。</param>
        private void ReportConfusion(List<Sample> samples, double areaLimit, double scoreThr)
        {
            int tp = 0, tn = 0, fp = 0, fn = 0;
            var okScores = new List<double>();
            var ngScores = new List<double>();

            foreach (Sample s in samples)
            {
                bool predOk = s.Predicted.HasValue && s.Predicted.Value;
                if (s.Label == LabelNg)
                {
                    if (predOk) fn++;      // 漏检：NG 判成 OK
                    else tp++;             // 抓到了
                    if (!double.IsNaN(s.Score)) ngScores.Add(s.Score);
                }
                else if (s.Label == LabelOk)
                {
                    if (predOk) tn++;      // 正常放行
                    else fp++;             // 过杀：OK 判成 NG
                    if (!double.IsNaN(s.Score)) okScores.Add(s.Score);
                }
            }

            int ng = tp + fn, ok = tn + fp;
            double recall = ng > 0 ? (double)tp / ng : double.NaN;
            double overkill = ok > 0 ? (double)fp / ok : double.NaN;

            Log(string.Format(CultureInfo.InvariantCulture,
                "结果：NG 命中 {0}/{1}{2} · OK 过杀 {3}/{4}{5}",
                tp, ng, ng > 0 ? string.Format(CultureInfo.InvariantCulture, "（检出率 {0:P1}）", recall) : "",
                fp, ok, ok > 0 ? string.Format(CultureInfo.InvariantCulture, "（过杀率 {0:P1}）", overkill) : ""));

            if (ng == 0)
                Log("注意：这批样本里没有 NG，只能验证过杀，看不出漏检。建议补几张真实缺陷图。");
            if (ok == 0)
                Log("注意：这批样本里没有 OK，只能验证检出，看不出过杀。");

            bool overlap = false;
            if (okScores.Count > 0 && ngScores.Count > 0)
                overlap = DiagnoseSeparability(okScores, ngScores, scoreThr);

            // 面积上限造成的过杀：得分本身达标了，改及格线没用，得改面积上限
            if (fp > 0 && areaLimit > 0 && okScores.Count > 0
                && okScores[0] >= scoreThr)
            {
                Log("提示：过杀里有 " + fp + " 张的良品得分并未跌破及格线，是「面积上限」判的；"
                    + "把面积上限调大（或填 0 关掉）再看。");
            }

            bool good = ng == 0 || recall >= 0.9;
            if (ok > 0 && overkill > 0.2) good = false;
            if (overlap) good = false;

            string hint = "";
            if (overlap) hint = "（得分区间重叠：模型无区分力，换及格线无用，详见日志）";
            else if (ng > 0 && recall < 0.9) hint = "（漏检偏多：把及格线调高或缩小 ROI）";

            SetVerdict(string.Format(CultureInfo.InvariantCulture,
                "验证完成：检出 {0}/{1} · 过杀 {2}/{3}{4}", tp, ng, fp, ok, hint), good);
        }

        /// <summary>
        /// 打印 OK / NG 两组的良品得分分布，判断"挪及格线能不能分开"。
        /// </summary>
        /// <returns>true = 两组区间重叠（模型无区分力）。</returns>
        private bool DiagnoseSeparability(List<double> okScores, List<double> ngScores,
                                          double scoreThr)
        {
            okScores.Sort();
            ngScores.Sort();

            // 良品得分越高越好 → OK 组整体偏上、NG 组偏下。
            // 所以"最像 NG 的 OK"是 okScores 的最小值，"最像 OK 的 NG"是 ngScores 的最大值。
            double maxOk = okScores[okScores.Count - 1];
            double minOk = okScores[0];
            double maxNg = ngScores[ngScores.Count - 1];
            double minNg = ngScores[0];
            double midOk = okScores[okScores.Count / 2];
            double midNg = ngScores[ngScores.Count / 2];

            Log(string.Format(CultureInfo.InvariantCulture,
                "良品得分分布（0~1，越高越正常；0.5 = 模型阈值线，当前及格线 {0:0.000}）：", scoreThr));
            Log(string.Format(CultureInfo.InvariantCulture,
                "  OK {0,2} 张：{1:0.000} ~ {2:0.000}   中位 {3:0.000}", okScores.Count, minOk, maxOk, midOk));
            Log(string.Format(CultureInfo.InvariantCulture,
                "  NG {0,2} 张：{1:0.000} ~ {2:0.000}   中位 {3:0.000}", ngScores.Count, minNg, maxNg, midNg));

            if (maxNg >= minOk)
            {
                Log(string.Format(CultureInfo.InvariantCulture,
                    "[警告] 两组区间重叠 {0:0.000} —— 模型没有区分力。", maxNg - minOk));
                Log(string.Format(CultureInfo.InvariantCulture,
                    "       NG 里最「正常」的一张（{0:0.000}）比 OK 里最「异常」的一张（{1:0.000}）还高，",
                    maxNg, minOk));
                Log("       两簇叠在一起，及格线放在任何位置都只能得到「全部 OK」或「全部 NG」。");
                Log("       这不是及格线高低的问题，按出现概率排查：");
                Log("         1) 训练用的 OK 样本里混进了缺陷品 —— 缺陷被当成「正常」学进去了；");
                Log("         2) OK 样本之间的姿态 / 光照 / 位置不一致 —— 逐位置统计要求同一部位对齐，");
                Log("            转个角度过去，「同一网格位置」对应的已是工件上的另一个部位；");
                Log("         3) OK 样本太少（< 15 张）—— 特征维度被压缩，判别力接近 0；");
                Log("         4) ROI 没收紧 —— 背景占了大半画面，背景波动盖过缺陷信号。");
                Log("       顺序：改标注 → 补样本 → 收紧 ROI → 固定姿态，再重训。及格线先别动。");
                return true;
            }

            Log(string.Format(CultureInfo.InvariantCulture,
                "[提示] 两组间隔 {0:0.000} —— 有区分力，及格线可调。", minOk - maxNg));

            // 建议及格线：两簇之间取**几何中点**。得分是比值压出来的、对数尺度，
            // 算术中点会贴到某一侧；再用 5% 余量夹一下，防止两簇几乎贴在一起时建议值撞车。
            double sug = Math.Sqrt(maxNg * minOk);
            if (sug < maxNg * 1.05) sug = maxNg * 1.05;
            if (sug > minOk * 0.95) sug = minOk * 0.95;
            if (sug < maxNg * 1.05) sug = (maxNg + minOk) * 0.5;   // 贴太近时退回算术中点

            Log(string.Format(CultureInfo.InvariantCulture,
                "       建议及格线填 {0:0.000}（当前 {1:0.000}）", sug, scoreThr));
            return false;
        }

        // =====================================================================
        //  参数回写 / 其它
        // =====================================================================

        private void OnApplyToNode(object sender, EventArgs e)
        {
            string name = AnomalyModelCatalog.SanitizeName(_txtModel.Text);
            if (name.Length > 0) ModelName = name;

            ParamsChanged = true;
            Log("已应用到节点参数：模型=" + (ModelName.Length > 0 ? ModelName : "(空)")
                + " · 判定得分=" + _numThreshold.Value.ToString(CultureInfo.InvariantCulture)
                + " · 最小面积=" + _numMinArea.Value.ToString(CultureInfo.InvariantCulture)
                + " · 面积上限=" + _numAreaLimit.Value.ToString(CultureInfo.InvariantCulture));

            Raise(ModelChanged);
        }

        private void Raise(EventHandler h)
        {
            if (h != null) h(this, EventArgs.Empty);
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
            UseWaitCursor = busy;

            // 整窗禁用：训练 / 验证期间不允许改样本或再次点按钮
            // （原生训练会话是单例的，重入会把统计量搅乱）
            foreach (Control c in Controls) c.Enabled = !busy;
        }

        private void SetVerdict(string text, bool good)
        {
            _lblVerdict.Text = text ?? "";
            _lblVerdict.ForeColor = good ? UiTheme.ColorOk : UiTheme.ColorNg;
        }

        private void Log(string line)
        {
            if (string.IsNullOrEmpty(line)) return;

            string stamp = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            _log.AppendText("[" + stamp + "] " + line + Environment.NewLine);
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (_busy) return;

            if (e.KeyCode == Keys.D1 || e.KeyCode == Keys.O) { OnMark(LabelOk); e.Handled = true; }
            else if (e.KeyCode == Keys.D2 || e.KeyCode == Keys.N) { OnMark(LabelNg); e.Handled = true; }
            else if (e.KeyCode == Keys.D0) { OnMark(LabelNone); e.Handled = true; }
        }

        // =====================================================================
        //  样本清单持久化（只存路径 + 标注；图片本身不进模型目录）
        // =====================================================================

        private string SampleListPath(string dir)
        {
            return System.IO.Path.Combine(dir, "samples.txt");
        }

        private void SaveSampleList(string dir)
        {
            try
            {
                var sb = new StringBuilder();
                foreach (Sample s in _samples)
                {
                    if (s.Label == LabelNone) continue;
                    sb.AppendLine((s.Label == LabelOk ? "OK" : "NG") + "\t" + s.Path);
                }
                File.WriteAllText(SampleListPath(dir), sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception ex) { Log("样本清单保存失败：" + ex.Message); }
        }

        /// <summary>打开窗体时按模型目录里的 samples.txt 还原上次的样本与标注。</summary>
        private void LoadSamplesFromModel(string modelName)
        {
            string name = AnomalyModelCatalog.SanitizeName(modelName);
            if (name.Length == 0) return;

            string dir = System.IO.Path.Combine(AnomalyModelCatalog.EnsureRoot(), name);
            string listPath = SampleListPath(dir);
            if (!File.Exists(listPath)) return;

            try
            {
                var files = new List<string>();
                var labels = new List<int>();

                foreach (string raw in File.ReadAllLines(listPath, Encoding.UTF8))
                {
                    string line = (raw ?? "").Trim();
                    if (line.Length == 0) continue;

                    string[] parts = line.Split('\t');
                    if (parts.Length < 2) continue;
                    if (!File.Exists(parts[1])) continue;

                    files.Add(parts[1]);
                    labels.Add(string.Equals(parts[0], "OK", StringComparison.OrdinalIgnoreCase) ? LabelOk : LabelNg);
                }

                if (files.Count == 0) return;

                AddFiles(files.ToArray());
                for (int i = 0; i < files.Count && i < _samples.Count; i++)
                {
                    Sample s = FindSample(files[i]);
                    if (s == null) continue;
                    s.Label = labels[i];
                    UpdateItemText(s);
                }

                UpdateCount();
                Log("已恢复上次的样本清单：" + files.Count + " 张（来自 " + listPath + "）");
            }
            catch (Exception ex) { Log("样本清单读取失败：" + ex.Message); }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);

            try { _algo.AnomalyTrainCancel(); } catch (Exception) { }
            try { _algo.Dispose(); } catch (Exception) { }

            foreach (Sample s in _samples)
                try { if (s.Image != null) s.Image.Dispose(); } catch (Exception) { }
            _samples.Clear();

            try { _thumbs.Dispose(); } catch (Exception) { }
            try { _tip.Dispose(); } catch (Exception) { }
        }
    }
}
