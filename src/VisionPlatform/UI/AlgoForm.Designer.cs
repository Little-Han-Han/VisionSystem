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
            this.lblSectionPre = new System.Windows.Forms.Label();
            this.lblBlur = new System.Windows.Forms.Label();
            this.numBlur = new System.Windows.Forms.NumericUpDown();
            this.lblThreshold = new System.Windows.Forms.Label();
            this.numThreshold = new System.Windows.Forms.NumericUpDown();
            this.lblMorph = new System.Windows.Forms.Label();
            this.cmbMorph = new System.Windows.Forms.ComboBox();
            this.lblSectionItems = new System.Windows.Forms.Label();
            this.chkBlob = new System.Windows.Forms.CheckBox();
            this.chkSharp = new System.Windows.Forms.CheckBox();
            this.chkTemplate = new System.Windows.Forms.CheckBox();
            this.chkCircle = new System.Windows.Forms.CheckBox();
            this.lblSectionThresh = new System.Windows.Forms.Label();
            this.lblMinArea = new System.Windows.Forms.Label();
            this.numMinArea = new System.Windows.Forms.NumericUpDown();
            this.lblMaxArea = new System.Windows.Forms.Label();
            this.numMaxArea = new System.Windows.Forms.NumericUpDown();
            this.lblExpected = new System.Windows.Forms.Label();
            this.numExpected = new System.Windows.Forms.NumericUpDown();
            this.lblMinScore = new System.Windows.Forms.Label();
            this.numMinScore = new System.Windows.Forms.NumericUpDown();
            this.lblMinSharp = new System.Windows.Forms.Label();
            this.numMinSharp = new System.Windows.Forms.NumericUpDown();

            this.btnOpenImage = new System.Windows.Forms.Button();
            this.btnGrabRun = new System.Windows.Forms.Button();
            this.btnTrain = new System.Windows.Forms.Button();
            this.btnSaveRecipe = new System.Windows.Forms.Button();
            this.btnFit = new System.Windows.Forms.Button();

            this.panelRight = new System.Windows.Forms.Panel();
            this.lblVerdict = new System.Windows.Forms.Label();
            this.listResults = new System.Windows.Forms.ListBox();

            ((System.ComponentModel.ISupportInitialize)(this.numBlur)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numThreshold)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numMinArea)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numMaxArea)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numExpected)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numMinScore)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numMinSharp)).BeginInit();
            this.panelLeft.SuspendLayout();
            this.tableParams.SuspendLayout();
            this.panelRight.SuspendLayout();
            this.SuspendLayout();

            // ---------- panelLeft ----------
            this.panelLeft.AutoScroll = true;
            this.panelLeft.BackColor = System.Drawing.Color.FromArgb(45, 45, 52);
            this.panelLeft.Controls.Add(this.tableParams);
            this.panelLeft.Controls.Add(this.btnOpenImage);
            this.panelLeft.Controls.Add(this.btnGrabRun);
            this.panelLeft.Controls.Add(this.btnTrain);
            this.panelLeft.Controls.Add(this.btnSaveRecipe);
            this.panelLeft.Controls.Add(this.btnFit);
            this.panelLeft.Dock = System.Windows.Forms.DockStyle.Left;
            this.panelLeft.Location = new System.Drawing.Point(0, 0);
            this.panelLeft.Name = "panelLeft";
            this.panelLeft.Padding = new System.Windows.Forms.Padding(8);
            this.panelLeft.Size = new System.Drawing.Size(310, 620);
            this.panelLeft.TabIndex = 0;

            // ---------- tableParams ----------
            this.tableParams.ColumnCount = 2;
            this.tableParams.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 118F));
            this.tableParams.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tableParams.Dock = System.Windows.Forms.DockStyle.Top;
            this.tableParams.Location = new System.Drawing.Point(8, 8);
            this.tableParams.Name = "tableParams";
            this.tableParams.RowCount = 15;
            this.tableParams.Size = new System.Drawing.Size(294, 400);
            this.tableParams.TabIndex = 0;

            for (int i = 0; i < 15; i++)
                this.tableParams.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 26F));

            // 行 0：预处理
            this.lblSectionPre.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblSectionPre.ForeColor = System.Drawing.Color.DeepSkyBlue;
            this.lblSectionPre.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F, System.Drawing.FontStyle.Bold);
            this.lblSectionPre.Text = "预处理";
            this.lblSectionPre.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.tableParams.Controls.Add(this.lblSectionPre, 0, 0);
            this.tableParams.SetColumnSpan(this.lblSectionPre, 2);

            this.lblBlur.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblBlur.ForeColor = System.Drawing.Color.Silver;
            this.lblBlur.Text = "高斯核";
            this.lblBlur.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.numBlur.Dock = System.Windows.Forms.DockStyle.Fill;
            this.numBlur.Minimum = new decimal(new int[] { 0, 0, 0, 0 });
            this.numBlur.Maximum = new decimal(new int[] { 31, 0, 0, 0 });
            this.numBlur.Value = new decimal(new int[] { 3, 0, 0, 0 });
            this.tableParams.Controls.Add(this.lblBlur, 0, 1);
            this.tableParams.Controls.Add(this.numBlur, 1, 1);

            this.lblThreshold.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblThreshold.ForeColor = System.Drawing.Color.Silver;
            this.lblThreshold.Text = "二值阈值";
            this.lblThreshold.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.numThreshold.Dock = System.Windows.Forms.DockStyle.Fill;
            this.numThreshold.Minimum = new decimal(new int[] { 1, 0, 0, -2147483648 });
            this.numThreshold.Maximum = new decimal(new int[] { 255, 0, 0, 0 });
            this.numThreshold.Value = new decimal(new int[] { 128, 0, 0, 0 });
            this.tableParams.Controls.Add(this.lblThreshold, 0, 2);
            this.tableParams.Controls.Add(this.numThreshold, 1, 2);

            this.lblMorph.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblMorph.ForeColor = System.Drawing.Color.Silver;
            this.lblMorph.Text = "形态学";
            this.lblMorph.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.cmbMorph.Dock = System.Windows.Forms.DockStyle.Fill;
            this.cmbMorph.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbMorph.Items.AddRange(new object[] { "无", "开运算", "闭运算" });
            this.tableParams.Controls.Add(this.lblMorph, 0, 3);
            this.tableParams.Controls.Add(this.cmbMorph, 1, 3);

            // 行 4：检测项
            this.lblSectionItems.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblSectionItems.ForeColor = System.Drawing.Color.DeepSkyBlue;
            this.lblSectionItems.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F, System.Drawing.FontStyle.Bold);
            this.lblSectionItems.Text = "检测项";
            this.lblSectionItems.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.tableParams.Controls.Add(this.lblSectionItems, 0, 4);
            this.tableParams.SetColumnSpan(this.lblSectionItems, 2);

            this.chkBlob.Dock = System.Windows.Forms.DockStyle.Fill;
            this.chkBlob.ForeColor = System.Drawing.Color.Gainsboro;
            this.chkBlob.Text = "连通域";
            this.chkBlob.Checked = true;
            this.tableParams.Controls.Add(this.chkBlob, 0, 5);
            this.tableParams.SetColumnSpan(this.chkBlob, 2);

            this.chkSharp.Dock = System.Windows.Forms.DockStyle.Fill;
            this.chkSharp.ForeColor = System.Drawing.Color.Gainsboro;
            this.chkSharp.Text = "清晰度";
            this.tableParams.Controls.Add(this.chkSharp, 0, 6);
            this.tableParams.SetColumnSpan(this.chkSharp, 2);

            this.chkTemplate.Dock = System.Windows.Forms.DockStyle.Fill;
            this.chkTemplate.ForeColor = System.Drawing.Color.Gainsboro;
            this.chkTemplate.Text = "模板匹配";
            this.tableParams.Controls.Add(this.chkTemplate, 0, 7);
            this.tableParams.SetColumnSpan(this.chkTemplate, 2);

            this.chkCircle.Dock = System.Windows.Forms.DockStyle.Fill;
            this.chkCircle.ForeColor = System.Drawing.Color.Gainsboro;
            this.chkCircle.Text = "圆拟合";
            this.tableParams.Controls.Add(this.chkCircle, 0, 8);
            this.tableParams.SetColumnSpan(this.chkCircle, 2);

            // 行 9：判定阈值
            this.lblSectionThresh.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblSectionThresh.ForeColor = System.Drawing.Color.DeepSkyBlue;
            this.lblSectionThresh.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F, System.Drawing.FontStyle.Bold);
            this.lblSectionThresh.Text = "判定阈值";
            this.lblSectionThresh.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.tableParams.Controls.Add(this.lblSectionThresh, 0, 9);
            this.tableParams.SetColumnSpan(this.lblSectionThresh, 2);

            this.lblMinArea.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblMinArea.ForeColor = System.Drawing.Color.Silver;
            this.lblMinArea.Text = "最小面积";
            this.lblMinArea.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.numMinArea.Dock = System.Windows.Forms.DockStyle.Fill;
            this.numMinArea.Maximum = new decimal(new int[] { 1000000, 0, 0, 0 });
            this.numMinArea.Value = new decimal(new int[] { 100, 0, 0, 0 });
            this.tableParams.Controls.Add(this.lblMinArea, 0, 10);
            this.tableParams.Controls.Add(this.numMinArea, 1, 10);

            this.lblMaxArea.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblMaxArea.ForeColor = System.Drawing.Color.Silver;
            this.lblMaxArea.Text = "最大面积";
            this.lblMaxArea.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.numMaxArea.Dock = System.Windows.Forms.DockStyle.Fill;
            this.numMaxArea.Maximum = new decimal(new int[] { 1000000, 0, 0, 0 });
            this.tableParams.Controls.Add(this.lblMaxArea, 0, 11);
            this.tableParams.Controls.Add(this.numMaxArea, 1, 11);

            this.lblExpected.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblExpected.ForeColor = System.Drawing.Color.Silver;
            this.lblExpected.Text = "期望数量";
            this.lblExpected.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.numExpected.Dock = System.Windows.Forms.DockStyle.Fill;
            this.numExpected.Maximum = new decimal(new int[] { 100, 0, 0, 0 });
            this.tableParams.Controls.Add(this.lblExpected, 0, 12);
            this.tableParams.Controls.Add(this.numExpected, 1, 12);

            this.lblMinScore.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblMinScore.ForeColor = System.Drawing.Color.Silver;
            this.lblMinScore.Text = "匹配下限";
            this.lblMinScore.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.numMinScore.Dock = System.Windows.Forms.DockStyle.Fill;
            this.numMinScore.DecimalPlaces = 2;
            this.numMinScore.Increment = new decimal(new int[] { 5, 0, 0, 131072 });
            this.numMinScore.Maximum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numMinScore.Value = new decimal(new int[] { 80, 0, 0, 131072 });
            this.tableParams.Controls.Add(this.lblMinScore, 0, 13);
            this.tableParams.Controls.Add(this.numMinScore, 1, 13);

            this.lblMinSharp.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblMinSharp.ForeColor = System.Drawing.Color.Silver;
            this.lblMinSharp.Text = "清晰度下限";
            this.lblMinSharp.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.numMinSharp.Dock = System.Windows.Forms.DockStyle.Fill;
            this.numMinSharp.Maximum = new decimal(new int[] { 100000, 0, 0, 0 });
            this.numMinSharp.Value = new decimal(new int[] { 50, 0, 0, 0 });
            this.tableParams.Controls.Add(this.lblMinSharp, 0, 14);
            this.tableParams.Controls.Add(this.numMinSharp, 1, 14);

            // ---------- 按钮 ----------
            this.btnOpenImage.Location = new System.Drawing.Point(8, 414);
            this.btnOpenImage.Name = "btnOpenImage";
            this.btnOpenImage.Size = new System.Drawing.Size(286, 32);
            this.btnOpenImage.TabIndex = 1;
            this.btnOpenImage.Text = "打开本地图片并检测";
            this.btnOpenImage.Click += new System.EventHandler(this.OnOpenImage);

            this.btnGrabRun.Location = new System.Drawing.Point(8, 452);
            this.btnGrabRun.Name = "btnGrabRun";
            this.btnGrabRun.Size = new System.Drawing.Size(286, 32);
            this.btnGrabRun.TabIndex = 2;
            this.btnGrabRun.Text = "从相机取帧并检测";
            this.btnGrabRun.Click += new System.EventHandler(this.OnGrabAndRun);

            this.btnTrain.Location = new System.Drawing.Point(8, 490);
            this.btnTrain.Name = "btnTrain";
            this.btnTrain.Size = new System.Drawing.Size(286, 32);
            this.btnTrain.TabIndex = 3;
            this.btnTrain.Text = "用当前图片训练模板";
            this.btnTrain.Click += new System.EventHandler(this.OnTrain);

            this.btnSaveRecipe.Location = new System.Drawing.Point(8, 528);
            this.btnSaveRecipe.Name = "btnSaveRecipe";
            this.btnSaveRecipe.Size = new System.Drawing.Size(286, 32);
            this.btnSaveRecipe.TabIndex = 4;
            this.btnSaveRecipe.Text = "保存为当前配方";
            this.btnSaveRecipe.Click += new System.EventHandler(this.OnSaveRecipe);

            this.btnFit.Location = new System.Drawing.Point(8, 566);
            this.btnFit.Name = "btnFit";
            this.btnFit.Size = new System.Drawing.Size(286, 32);
            this.btnFit.TabIndex = 5;
            this.btnFit.Text = "适应窗口";
            this.btnFit.Click += new System.EventHandler(this.OnFit);

            // ---------- panelRight ----------
            this.panelRight.BackColor = System.Drawing.Color.FromArgb(38, 38, 46);
            this.panelRight.Dock = System.Windows.Forms.DockStyle.Fill;
            this.panelRight.Location = new System.Drawing.Point(310, 0);
            this.panelRight.Name = "panelRight";
            this.panelRight.Padding = new System.Windows.Forms.Padding(4);
            this.panelRight.Size = new System.Drawing.Size(730, 620);
            this.panelRight.TabIndex = 1;

            this.lblVerdict.Dock = System.Windows.Forms.DockStyle.Top;
            this.lblVerdict.Font = new System.Drawing.Font("Microsoft YaHei UI", 12F, System.Drawing.FontStyle.Bold);
            this.lblVerdict.ForeColor = System.Drawing.Color.Silver;
            this.lblVerdict.Location = new System.Drawing.Point(4, 4);
            this.lblVerdict.Name = "lblVerdict";
            this.lblVerdict.Size = new System.Drawing.Size(722, 40);
            this.lblVerdict.TabIndex = 0;
            this.lblVerdict.Text = "尚未检测";
            this.lblVerdict.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;

            this.listResults.BackColor = System.Drawing.Color.FromArgb(28, 28, 34);
            this.listResults.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.listResults.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.listResults.ForeColor = System.Drawing.Color.Gainsboro;
            this.listResults.IntegralHeight = false;
            this.listResults.ItemHeight = 15;
            this.listResults.Location = new System.Drawing.Point(4, 476);
            this.listResults.Name = "listResults";
            this.listResults.Size = new System.Drawing.Size(722, 140);
            this.listResults.TabIndex = 1;

            // 显示控件由 AlgoForm 构造函数创建并加入 panelRight
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
            this.Text = "算法参数与离线测试";

            ((System.ComponentModel.ISupportInitialize)(this.numBlur)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numThreshold)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numMinArea)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numMaxArea)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numExpected)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numMinScore)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numMinSharp)).EndInit();
            this.panelLeft.ResumeLayout(false);
            this.panelLeft.PerformLayout();
            this.tableParams.ResumeLayout(false);
            this.panelRight.ResumeLayout(false);
            this.ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.Panel panelLeft;
        private System.Windows.Forms.TableLayoutPanel tableParams;

        private System.Windows.Forms.Label lblSectionPre;
        private System.Windows.Forms.Label lblBlur;
        private System.Windows.Forms.NumericUpDown numBlur;
        private System.Windows.Forms.Label lblThreshold;
        private System.Windows.Forms.NumericUpDown numThreshold;
        private System.Windows.Forms.Label lblMorph;
        private System.Windows.Forms.ComboBox cmbMorph;

        private System.Windows.Forms.Label lblSectionItems;
        private System.Windows.Forms.CheckBox chkBlob;
        private System.Windows.Forms.CheckBox chkSharp;
        private System.Windows.Forms.CheckBox chkTemplate;
        private System.Windows.Forms.CheckBox chkCircle;

        private System.Windows.Forms.Label lblSectionThresh;
        private System.Windows.Forms.Label lblMinArea;
        private System.Windows.Forms.NumericUpDown numMinArea;
        private System.Windows.Forms.Label lblMaxArea;
        private System.Windows.Forms.NumericUpDown numMaxArea;
        private System.Windows.Forms.Label lblExpected;
        private System.Windows.Forms.NumericUpDown numExpected;
        private System.Windows.Forms.Label lblMinScore;
        private System.Windows.Forms.NumericUpDown numMinScore;
        private System.Windows.Forms.Label lblMinSharp;
        private System.Windows.Forms.NumericUpDown numMinSharp;

        private System.Windows.Forms.Button btnOpenImage;
        private System.Windows.Forms.Button btnGrabRun;
        private System.Windows.Forms.Button btnTrain;
        private System.Windows.Forms.Button btnSaveRecipe;
        private System.Windows.Forms.Button btnFit;

        private System.Windows.Forms.Panel panelRight;
        private System.Windows.Forms.Label lblVerdict;
        private System.Windows.Forms.ListBox listResults;
    }
}
