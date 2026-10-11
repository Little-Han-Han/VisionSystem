namespace VisionPlatform.UI
{
    partial class AlgoForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null)) components.Dispose();
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();

            this.panelLeft = new System.Windows.Forms.Panel();
            this.tableParams = new System.Windows.Forms.TableLayoutPanel();
            this.panelRight = new System.Windows.Forms.Panel();
            this.lblVerdict = new System.Windows.Forms.Label();
            this.gridResults = new System.Windows.Forms.DataGridView();

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

            this.panelLeft.SuspendLayout();
            this.tableParams.SuspendLayout();
            this.panelRight.SuspendLayout();
            this.SuspendLayout();

            // ---------- panelLeft ----------
            this.panelLeft.AutoScroll = true;
            this.panelLeft.BackColor = System.Drawing.Color.FromArgb(45, 45, 52);
            this.panelLeft.Controls.Add(this.tableParams);
            this.panelLeft.Dock = System.Windows.Forms.DockStyle.Left;
            this.panelLeft.Location = new System.Drawing.Point(0, 0);
            this.panelLeft.Name = "panelLeft";
            this.panelLeft.Padding = new System.Windows.Forms.Padding(8);
            this.panelLeft.Size = new System.Drawing.Size(340, 620);
            this.panelLeft.TabIndex = 0;

            // ---------- tableParams ----------
            this.tableParams.ColumnCount = 2;
            this.tableParams.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 132F));
            this.tableParams.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tableParams.Dock = System.Windows.Forms.DockStyle.Top;
            this.tableParams.Location = new System.Drawing.Point(8, 8);
            this.tableParams.Name = "tableParams";
            this.tableParams.RowCount = kParamRows;
            this.tableParams.Size = new System.Drawing.Size(324, kParamRows * kRowHeight);
            this.tableParams.TabIndex = 0;

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

            // ---------- panelRight ----------
            this.panelRight.BackColor = System.Drawing.Color.FromArgb(38, 38, 46);
            this.panelRight.Dock = System.Windows.Forms.DockStyle.Fill;
            this.panelRight.Location = new System.Drawing.Point(340, 0);
            this.panelRight.Name = "panelRight";
            this.panelRight.Padding = new System.Windows.Forms.Padding(4);
            this.panelRight.Size = new System.Drawing.Size(700, 620);
            this.panelRight.TabIndex = 1;
            // 子控件必须逐个 Add：漏掉的话控件没有 Parent，永不显示也不响应事件。
            // 停靠按集合逆序生效，故先加 Top 的判定条、再加 Bottom 的结果表；
            // ImageViewer 由 AlgoForm 构造函数 Add 后 SetChildIndex(0)，最后停靠并填满余量。
            this.panelRight.Controls.Add(this.lblVerdict);
            this.panelRight.Controls.Add(this.gridResults);

            this.lblVerdict.Dock = System.Windows.Forms.DockStyle.Top;
            this.lblVerdict.Font = new System.Drawing.Font("Microsoft YaHei UI", 12F, System.Drawing.FontStyle.Bold);
            this.lblVerdict.ForeColor = System.Drawing.Color.Silver;
            this.lblVerdict.Location = new System.Drawing.Point(4, 4);
            this.lblVerdict.Name = "lblVerdict";
            this.lblVerdict.Size = new System.Drawing.Size(692, 40);
            this.lblVerdict.TabIndex = 0;
            this.lblVerdict.Text = "请载入图像并训练形状模型";
            this.lblVerdict.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;

            // ---------- gridResults：结果表格（点列头可排序）----------
            this.gridResults.AllowUserToAddRows = false;
            this.gridResults.AllowUserToDeleteRows = false;
            this.gridResults.AllowUserToResizeRows = false;
            this.gridResults.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;
            this.gridResults.BackgroundColor = System.Drawing.Color.FromArgb(28, 28, 34);
            this.gridResults.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.gridResults.ColumnHeadersHeightSizeMode =
                System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.gridResults.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.gridResults.EnableHeadersVisualStyles = false;
            this.gridResults.ForeColor = System.Drawing.Color.Gainsboro;
            this.gridResults.GridColor = System.Drawing.Color.FromArgb(60, 60, 70);
            this.gridResults.Location = new System.Drawing.Point(4, 440);
            this.gridResults.MultiSelect = false;
            this.gridResults.Name = "gridResults";
            this.gridResults.ReadOnly = true;
            this.gridResults.RowHeadersVisible = false;
            this.gridResults.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.gridResults.Size = new System.Drawing.Size(692, 176);
            this.gridResults.TabIndex = 1;

            this.gridResults.ColumnHeadersDefaultCellStyle.BackColor = System.Drawing.Color.FromArgb(45, 45, 52);
            this.gridResults.ColumnHeadersDefaultCellStyle.ForeColor = System.Drawing.Color.DeepSkyBlue;
            this.gridResults.ColumnHeadersDefaultCellStyle.SelectionBackColor = System.Drawing.Color.FromArgb(45, 45, 52);
            this.gridResults.DefaultCellStyle.BackColor = System.Drawing.Color.FromArgb(28, 28, 34);
            this.gridResults.DefaultCellStyle.ForeColor = System.Drawing.Color.Gainsboro;
            this.gridResults.DefaultCellStyle.SelectionBackColor = System.Drawing.Color.FromArgb(0, 90, 130);
            this.gridResults.DefaultCellStyle.SelectionForeColor = System.Drawing.Color.White;

            // 显示控件 ImageViewer 由 AlgoForm 构造函数创建并加入 panelRight

            // ---------- AlgoForm ----------
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(38, 38, 46);
            this.ClientSize = new System.Drawing.Size(1040, 620);
            this.Controls.Add(this.panelRight);
            this.Controls.Add(this.panelLeft);
            this.ForeColor = System.Drawing.Color.Gainsboro;
            this.MinimumSize = new System.Drawing.Size(900, 500);
            this.Name = "AlgoForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "形状匹配（基于边缘轮廓）";

            this.panelLeft.ResumeLayout(false);
            this.panelLeft.PerformLayout();
            this.tableParams.ResumeLayout(false);
            this.panelRight.ResumeLayout(false);
            this.ResumeLayout(false);
        }

        #endregion

        private const int kParamRows = 42;
        private const int kRowHeight = 28;

        // ---------- 布局辅助（手工编写，便于按行追加参数）----------

        private static System.Windows.Forms.NumericUpDown MakeNum(decimal min, decimal max, decimal value, int decimals)
        {
            var n = new System.Windows.Forms.NumericUpDown();
            n.Dock = System.Windows.Forms.DockStyle.Fill;
            n.Minimum = min;
            n.Maximum = max;
            n.Value = value;
            n.DecimalPlaces = decimals;
            n.BackColor = System.Drawing.Color.FromArgb(30, 30, 38);
            n.ForeColor = System.Drawing.Color.Gainsboro;
            return n;
        }

        private void AddSection(string text, int row)
        {
            var l = new System.Windows.Forms.Label();
            l.Dock = System.Windows.Forms.DockStyle.Fill;
            l.ForeColor = System.Drawing.Color.DeepSkyBlue;
            l.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F, System.Drawing.FontStyle.Bold);
            l.Text = text;
            l.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.tableParams.Controls.Add(l, 0, row);
            this.tableParams.SetColumnSpan(l, 2);
        }

        private void AddFull(System.Windows.Forms.Control c, int row)
        {
            c.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tableParams.Controls.Add(c, 0, row);
            this.tableParams.SetColumnSpan(c, 2);
        }

        private void AddRow(string labelText, System.Windows.Forms.Control value, int row)
        {
            var l = new System.Windows.Forms.Label();
            l.Dock = System.Windows.Forms.DockStyle.Fill;
            l.ForeColor = System.Drawing.Color.Silver;
            l.Text = labelText;
            l.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.tableParams.Controls.Add(l, 0, row);
            this.tableParams.Controls.Add(value, 1, row);
        }

        private System.Windows.Forms.Panel panelLeft;
        private System.Windows.Forms.TableLayoutPanel tableParams;
        private System.Windows.Forms.Panel panelRight;
        private System.Windows.Forms.Label lblVerdict;
        private System.Windows.Forms.DataGridView gridResults;

        private VisionPlatform.UI.UiButton btnOpenImage;
        private VisionPlatform.UI.UiButton btnGrab;

        private System.Windows.Forms.NumericUpDown numTplX;
        private System.Windows.Forms.NumericUpDown numTplY;
        private System.Windows.Forms.NumericUpDown numTplW;
        private System.Windows.Forms.NumericUpDown numTplH;
        private VisionPlatform.UI.UiButton btnPickTemplate;
        private VisionPlatform.UI.UiButton btnTemplateFull;

        private System.Windows.Forms.NumericUpDown numContrast;
        private System.Windows.Forms.NumericUpDown numMaxFeatures;
        private System.Windows.Forms.NumericUpDown numMinFeatureDist;
        private VisionPlatform.UI.UiButton btnTrain;
        private System.Windows.Forms.Label lblModelInfo;
        private System.Windows.Forms.CheckBox chkShowFeatures;

        private System.Windows.Forms.NumericUpDown numAngleStart;
        private System.Windows.Forms.NumericUpDown numAngleEnd;
        private System.Windows.Forms.NumericUpDown numAngleStep;
        private System.Windows.Forms.NumericUpDown numScaleMin;
        private System.Windows.Forms.NumericUpDown numScaleMax;
        private System.Windows.Forms.NumericUpDown numScaleStep;
        private System.Windows.Forms.NumericUpDown numPyramid;
        private System.Windows.Forms.NumericUpDown numMinScore;
        private System.Windows.Forms.NumericUpDown numMaxMatches;
        private System.Windows.Forms.NumericUpDown numOverlap;
        private System.Windows.Forms.NumericUpDown numGreediness;
        private System.Windows.Forms.CheckBox chkSubpixel;
        private System.Windows.Forms.Label lblPoseHint;

        private System.Windows.Forms.NumericUpDown numSrchX;
        private System.Windows.Forms.NumericUpDown numSrchY;
        private System.Windows.Forms.NumericUpDown numSrchW;
        private System.Windows.Forms.NumericUpDown numSrchH;
        private VisionPlatform.UI.UiButton btnPickSearch;
        private VisionPlatform.UI.UiButton btnSearchFull;

        private VisionPlatform.UI.UiButton btnMatch;
        private VisionPlatform.UI.UiButton btnClearModel;
        private VisionPlatform.UI.UiButton btnFit;
    }
}
