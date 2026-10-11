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
        private ShapeModelInfo _modelInfo;
        private List<ShapeModelPoint> _features;   // 模板特征点（独立图层用）
        private DataTable _resultTable;            // 结果表格数据源（支持点列头排序）

        // 框选状态：0 = 未框选，1 = 正在框选模板区域，2 = 正在框选搜索区域
        private int _picking;

        public AlgoForm()
        {
            InitializeComponent();

            CreateViewer();
            InitResultGrid();
            SyncParamToUi();
            UpdatePoseHint();

            // 统一深色工程配色（放在最后，业务语义色在运行时覆盖）
            UiStyle.ApplyForm(this);

            // 语义色：取帧=蓝、执行匹配=绿、清除模型=红；其余保持中性（次要动作）
            btnOpenImage.Kind = UiButtonKind.Grab;
            btnGrab.Kind = UiButtonKind.Grab;
            btnMatch.Kind = UiButtonKind.Start;
            btnClearModel.Kind = UiButtonKind.Stop;
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

        private VisionAlgo EnsureAlgo()
        {
            if (AppContext.Algo != null) return AppContext.Algo;

            try
            {
                AppContext.Algo = new VisionAlgo();
                return AppContext.Algo;
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
        }
    }
}
