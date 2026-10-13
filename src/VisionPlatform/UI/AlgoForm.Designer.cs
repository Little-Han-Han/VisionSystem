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

            // 说明：以下参数控件（NumericUpDown / UiButton / Label / CheckBox）的创建、
            // tableParams 行定义与按行填充、以及它们的事件接线，均含方法调用 / 循环 / 算术，
            // VS WinForms 设计器无法解析，已全部移入 BuildParamTable()（由构造函数在
            // InitializeComponent() 之后调用），以此保证本窗体能在设计器中正常打开。

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
            this.tableParams.TabIndex = 0;
            // 说明：RowCount / RowStyles 及各参数控件的按行填充已移入 BuildParamTable()（构造函数调用）。

            // 说明：以下「按行填充参数表 + 动态控件属性 / 事件接线」已移入 BuildParamTable()
            // （构造函数在 InitializeComponent() 之后调用）——含方法调用 / 循环 / 算术，设计器无法解析。

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
