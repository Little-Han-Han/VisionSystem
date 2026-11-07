namespace VisionPlatform.UI
{
    partial class OcrTemplateForm
    {
        #region Windows Form Designer generated code

        /// <summary>
        /// 设计器生成代码：只允许"字段 + 属性赋值 + Controls.Add + 具名事件处理器"。
        /// <para>
        /// 这里<strong>不能</strong>出现 lambda / 局部变量 / 辅助方法调用（如旧版的
        /// <c>MakeButton(...)</c>），否则 VS 设计器无法反序列化，会报
        /// "无法处理 ... 事件" 或直接清空控件。
        /// </para>
        /// </summary>
        private void InitializeComponent()
        {
            this.top = new System.Windows.Forms.FlowLayoutPanel();
            this.lblDir = new System.Windows.Forms.Label();
            this.txtDir = new System.Windows.Forms.TextBox();
            this.btnBrowseDir = new VisionPlatform.UI.UiButton();
            this.btnOpenDir = new VisionPlatform.UI.UiButton();
            this.btnRescan = new VisionPlatform.UI.UiButton();
            this.bottom = new System.Windows.Forms.FlowLayoutPanel();
            this.lblTag = new System.Windows.Forms.Label();
            this.txtLabel = new System.Windows.Forms.TextBox();
            this.lblLabelBytes = new System.Windows.Forms.Label();
            this.rdoAppend = new System.Windows.Forms.RadioButton();
            this.rdoReplace = new System.Windows.Forms.RadioButton();
            this.btnAdd = new VisionPlatform.UI.UiButton();
            this.btnDelete = new VisionPlatform.UI.UiButton();
            this.btnClearAll = new VisionPlatform.UI.UiButton();
            this.btnMigrate = new VisionPlatform.UI.UiButton();
            this.left = new System.Windows.Forms.Panel();
            this.listChars = new System.Windows.Forms.ListView();
            this.colTag = new System.Windows.Forms.ColumnHeader();
            this.colSamples = new System.Windows.Forms.ColumnHeader();
            this.colSize = new System.Windows.Forms.ColumnHeader();
            this.colTime = new System.Windows.Forms.ColumnHeader();
            this.lblFailures = new System.Windows.Forms.Label();
            this.right = new System.Windows.Forms.Panel();
            this.rightTop = new System.Windows.Forms.FlowLayoutPanel();
            this.btnLoadImage = new VisionPlatform.UI.UiButton();
            this.btnDrawRoi = new VisionPlatform.UI.UiButton();
            this.lblPreviewInfo = new System.Windows.Forms.Label();
            this.viewer = new VisionSystem.Controls.ImageViewer();
            this.picPreview = new System.Windows.Forms.PictureBox();
            this.lblStatus = new System.Windows.Forms.Label();
            this.top.SuspendLayout();
            this.bottom.SuspendLayout();
            this.left.SuspendLayout();
            this.right.SuspendLayout();
            this.rightTop.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.picPreview)).BeginInit();
            this.SuspendLayout();
            // 
            // top
            // 
            this.top.Dock = System.Windows.Forms.DockStyle.Top;
            this.top.FlowDirection = System.Windows.Forms.FlowDirection.LeftToRight;
            this.top.Height = 44;
            this.top.Padding = new System.Windows.Forms.Padding(8);
            this.top.WrapContents = false;
            // 
            // lblDir
            // 
            this.lblDir.AutoSize = false;
            this.lblDir.Margin = new System.Windows.Forms.Padding(0, 2, 4, 0);
            this.lblDir.Size = new System.Drawing.Size(64, 26);
            this.lblDir.Text = "模板目录";
            this.lblDir.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // txtDir
            // 
            this.txtDir.Margin = new System.Windows.Forms.Padding(0, 2, 8, 0);
            this.txtDir.Size = new System.Drawing.Size(560, 26);
            this.txtDir.TextChanged += new System.EventHandler(this.OnDirTextChanged);
            // 
            // btnBrowseDir
            // 
            this.btnBrowseDir.Margin = new System.Windows.Forms.Padding(0, 2, 6, 2);
            this.btnBrowseDir.Size = new System.Drawing.Size(76, 28);
            this.btnBrowseDir.Text = "浏览…";
            this.btnBrowseDir.Click += new System.EventHandler(this.OnBrowseDir);
            // 
            // btnOpenDir
            // 
            this.btnOpenDir.Margin = new System.Windows.Forms.Padding(0, 2, 6, 2);
            this.btnOpenDir.Size = new System.Drawing.Size(60, 28);
            this.btnOpenDir.Text = "打开";
            this.btnOpenDir.Click += new System.EventHandler(this.OnOpenDir);
            // 
            // btnRescan
            // 
            this.btnRescan.Margin = new System.Windows.Forms.Padding(0, 2, 6, 2);
            this.btnRescan.Size = new System.Drawing.Size(60, 28);
            this.btnRescan.Text = "重扫";
            this.btnRescan.Click += new System.EventHandler(this.OnRescan);
            // 
            // bottom
            // 
            this.bottom.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.bottom.FlowDirection = System.Windows.Forms.FlowDirection.LeftToRight;
            this.bottom.Height = 96;
            this.bottom.Padding = new System.Windows.Forms.Padding(8, 4, 8, 4);
            this.bottom.WrapContents = false;
            // 
            // lblTag
            // 
            this.lblTag.AutoSize = false;
            this.lblTag.Margin = new System.Windows.Forms.Padding(0, 2, 4, 0);
            this.lblTag.Size = new System.Drawing.Size(40, 26);
            this.lblTag.Text = "标签";
            this.lblTag.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // txtLabel
            // 
            this.txtLabel.Margin = new System.Windows.Forms.Padding(0, 2, 6, 0);
            this.txtLabel.Size = new System.Drawing.Size(110, 26);
            this.txtLabel.TextChanged += new System.EventHandler(this.OnLabelTextChanged);
            // 
            // lblLabelBytes
            // 
            this.lblLabelBytes.AutoSize = false;
            this.lblLabelBytes.Margin = new System.Windows.Forms.Padding(0, 2, 10, 0);
            this.lblLabelBytes.Size = new System.Drawing.Size(112, 26);
            this.lblLabelBytes.Text = "0 字符";
            this.lblLabelBytes.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // rdoAppend
            // 
            this.rdoAppend.AutoSize = true;
            this.rdoAppend.Checked = true;
            this.rdoAppend.Margin = new System.Windows.Forms.Padding(0, 4, 10, 0);
            this.rdoAppend.Text = "追加样本";
            // 
            // rdoReplace
            // 
            this.rdoReplace.AutoSize = true;
            this.rdoReplace.Margin = new System.Windows.Forms.Padding(0, 4, 10, 0);
            this.rdoReplace.Text = "覆盖主样本";
            // 
            // btnAdd
            // 
            this.btnAdd.Margin = new System.Windows.Forms.Padding(0, 2, 6, 2);
            this.btnAdd.Size = new System.Drawing.Size(72, 28);
            this.btnAdd.Text = "入库";
            this.btnAdd.Click += new System.EventHandler(this.OnAdd);
            // 
            // btnDelete
            // 
            this.btnDelete.Margin = new System.Windows.Forms.Padding(0, 2, 6, 2);
            this.btnDelete.Size = new System.Drawing.Size(104, 28);
            this.btnDelete.Text = "删除该字符";
            this.btnDelete.Click += new System.EventHandler(this.OnDelete);
            // 
            // btnClearAll
            // 
            this.btnClearAll.Margin = new System.Windows.Forms.Padding(0, 2, 6, 2);
            this.btnClearAll.Size = new System.Drawing.Size(88, 28);
            this.btnClearAll.Text = "清空目录";
            this.btnClearAll.Click += new System.EventHandler(this.OnClearAll);
            // 
            // btnMigrate
            // 
            this.btnMigrate.Margin = new System.Windows.Forms.Padding(0, 2, 6, 2);
            this.btnMigrate.Size = new System.Drawing.Size(88, 28);
            this.btnMigrate.Text = "迁移到…";
            this.btnMigrate.Click += new System.EventHandler(this.OnMigrate);
            // 
            // left
            // 
            this.left.Dock = System.Windows.Forms.DockStyle.Left;
            this.left.Padding = new System.Windows.Forms.Padding(8, 4, 4, 4);
            this.left.Width = 430;
            // 
            // listChars
            // 
            this.listChars.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.colTag,
            this.colSamples,
            this.colSize,
            this.colTime});
            this.listChars.Dock = System.Windows.Forms.DockStyle.Fill;
            this.listChars.FullRowSelect = true;
            this.listChars.HideSelection = false;
            this.listChars.MultiSelect = false;
            this.listChars.View = System.Windows.Forms.View.Details;
            this.listChars.SelectedIndexChanged += new System.EventHandler(this.OnCharSelected);
            // 
            // colTag
            // 
            this.colTag.Text = "标签";
            this.colTag.Width = 110;
            // 
            // colSamples
            // 
            this.colSamples.Text = "样本";
            this.colSamples.Width = 50;
            // 
            // colSize
            // 
            this.colSize.Text = "尺寸";
            this.colSize.Width = 90;
            // 
            // colTime
            // 
            this.colTime.Text = "时间";
            this.colTime.Width = 120;
            // 
            // lblFailures
            // 
            this.lblFailures.AutoEllipsis = false;
            this.lblFailures.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.lblFailures.Height = 84;
            this.lblFailures.TextAlign = System.Drawing.ContentAlignment.TopLeft;
            // 
            // right
            // 
            this.right.Dock = System.Windows.Forms.DockStyle.Fill;
            this.right.Padding = new System.Windows.Forms.Padding(4, 4, 8, 4);
            // 
            // rightTop
            // 
            this.rightTop.Dock = System.Windows.Forms.DockStyle.Top;
            this.rightTop.FlowDirection = System.Windows.Forms.FlowDirection.LeftToRight;
            this.rightTop.Height = 40;
            this.rightTop.WrapContents = false;
            // 
            // btnLoadImage
            // 
            this.btnLoadImage.Margin = new System.Windows.Forms.Padding(0, 2, 6, 2);
            this.btnLoadImage.Size = new System.Drawing.Size(96, 28);
            this.btnLoadImage.Text = "载入图片";
            this.btnLoadImage.Click += new System.EventHandler(this.OnLoadImage);
            // 
            // btnDrawRoi
            // 
            this.btnDrawRoi.Margin = new System.Windows.Forms.Padding(0, 2, 6, 2);
            this.btnDrawRoi.Size = new System.Drawing.Size(96, 28);
            this.btnDrawRoi.Text = "框选字符";
            this.btnDrawRoi.Click += new System.EventHandler(this.OnDrawRoi);
            // 
            // lblPreviewInfo
            // 
            this.lblPreviewInfo.AutoSize = false;
            this.lblPreviewInfo.Margin = new System.Windows.Forms.Padding(8, 4, 0, 0);
            this.lblPreviewInfo.Size = new System.Drawing.Size(360, 26);
            this.lblPreviewInfo.Text = "在图上框选一个字符 → 填标签 → 入库";
            this.lblPreviewInfo.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // viewer
            // 
            this.viewer.Dock = System.Windows.Forms.DockStyle.Fill;
            // 
            // picPreview
            // 
            this.picPreview.Dock = System.Windows.Forms.DockStyle.Right;
            this.picPreview.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.picPreview.Width = 140;
            // 
            // lblStatus
            // 
            this.lblStatus.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.lblStatus.Height = 26;
            this.lblStatus.Padding = new System.Windows.Forms.Padding(8, 0, 8, 0);
            this.lblStatus.Text = "就绪：在右侧图上框选【单个字符】→ 填标签 → 入库（一个模板只装一个字符）";
            this.lblStatus.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // OcrTemplateForm
            // 
            this.ClientSize = new System.Drawing.Size(1080, 720);
            this.Controls.Add(this.right);
            this.Controls.Add(this.left);
            this.Controls.Add(this.top);
            this.Controls.Add(this.bottom);
            this.Controls.Add(this.lblStatus);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.Sizable;
            this.KeyPreview = true;
            this.MinimumSize = new System.Drawing.Size(900, 600);
            this.Name = "OcrTemplateForm";
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "字符模板库";
            this.top.ResumeLayout(false);
            this.top.PerformLayout();
            this.bottom.ResumeLayout(false);
            this.bottom.PerformLayout();
            this.left.ResumeLayout(false);
            this.right.ResumeLayout(false);
            this.rightTop.ResumeLayout(false);
            this.rightTop.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.picPreview)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private System.Windows.Forms.FlowLayoutPanel top;
        private System.Windows.Forms.Label lblDir;
        private System.Windows.Forms.TextBox txtDir;
        private VisionPlatform.UI.UiButton btnBrowseDir;
        private VisionPlatform.UI.UiButton btnOpenDir;
        private VisionPlatform.UI.UiButton btnRescan;
        private System.Windows.Forms.FlowLayoutPanel bottom;
        private System.Windows.Forms.Label lblTag;
        private System.Windows.Forms.TextBox txtLabel;
        private System.Windows.Forms.Label lblLabelBytes;
        private System.Windows.Forms.RadioButton rdoAppend;
        private System.Windows.Forms.RadioButton rdoReplace;
        private VisionPlatform.UI.UiButton btnAdd;
        private VisionPlatform.UI.UiButton btnDelete;
        private VisionPlatform.UI.UiButton btnClearAll;
        private VisionPlatform.UI.UiButton btnMigrate;
        private System.Windows.Forms.Panel left;
        private System.Windows.Forms.ListView listChars;
        private System.Windows.Forms.ColumnHeader colTag;
        private System.Windows.Forms.ColumnHeader colSamples;
        private System.Windows.Forms.ColumnHeader colSize;
        private System.Windows.Forms.ColumnHeader colTime;
        private System.Windows.Forms.Label lblFailures;
        private System.Windows.Forms.Panel right;
        private System.Windows.Forms.FlowLayoutPanel rightTop;
        private VisionPlatform.UI.UiButton btnLoadImage;
        private VisionPlatform.UI.UiButton btnDrawRoi;
        private System.Windows.Forms.Label lblPreviewInfo;
        private VisionSystem.Controls.ImageViewer viewer;
        private System.Windows.Forms.PictureBox picPreview;
        private System.Windows.Forms.Label lblStatus;
    }
}
