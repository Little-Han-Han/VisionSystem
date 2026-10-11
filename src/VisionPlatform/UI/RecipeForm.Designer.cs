namespace VisionPlatform.UI
{
    partial class RecipeForm
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
            this.listRecipes = new System.Windows.Forms.ListBox();
            this.panelButtons = new System.Windows.Forms.FlowLayoutPanel();
            this.btnRefresh = new VisionPlatform.UI.UiButton();
            this.btnNew = new VisionPlatform.UI.UiButton();
            this.btnLoad = new VisionPlatform.UI.UiButton();
            this.btnSave = new VisionPlatform.UI.UiButton();
            this.btnDelete = new VisionPlatform.UI.UiButton();

            this.panelRight = new System.Windows.Forms.Panel();
            this.tableEdit = new System.Windows.Forms.TableLayoutPanel();
            this.lblName = new System.Windows.Forms.Label();
            this.txtName = new System.Windows.Forms.TextBox();
            this.lblProduct = new System.Windows.Forms.Label();
            this.txtProduct = new System.Windows.Forms.TextBox();
            this.lblCamera = new System.Windows.Forms.Label();
            this.txtCamera = new System.Windows.Forms.TextBox();
            this.lblNote = new System.Windows.Forms.Label();
            this.txtNote = new System.Windows.Forms.TextBox();
            this.lblInfo = new System.Windows.Forms.Label();
            this.lblHint = new System.Windows.Forms.Label();

            this.panelLeft.SuspendLayout();
            this.panelButtons.SuspendLayout();
            this.panelRight.SuspendLayout();
            this.tableEdit.SuspendLayout();
            this.SuspendLayout();

            // ---------- panelLeft ----------
            this.panelLeft.BackColor = System.Drawing.Color.FromArgb(45, 45, 52);
            this.panelLeft.Controls.Add(this.listRecipes);
            this.panelLeft.Controls.Add(this.panelButtons);
            this.panelLeft.Dock = System.Windows.Forms.DockStyle.Left;
            this.panelLeft.Location = new System.Drawing.Point(0, 0);
            this.panelLeft.Name = "panelLeft";
            this.panelLeft.Padding = new System.Windows.Forms.Padding(8);
            this.panelLeft.Size = new System.Drawing.Size(270, 520);
            this.panelLeft.TabIndex = 0;

            this.listRecipes.BackColor = System.Drawing.Color.FromArgb(38, 38, 44);
            this.listRecipes.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.listRecipes.Dock = System.Windows.Forms.DockStyle.Fill;
            this.listRecipes.ForeColor = System.Drawing.Color.Gainsboro;
            this.listRecipes.IntegralHeight = false;
            this.listRecipes.ItemHeight = 15;
            this.listRecipes.Location = new System.Drawing.Point(8, 8);
            this.listRecipes.Name = "listRecipes";
            this.listRecipes.Size = new System.Drawing.Size(254, 336);
            this.listRecipes.TabIndex = 0;
            this.listRecipes.SelectedIndexChanged += new System.EventHandler(this.OnSelectionChanged);

            this.panelButtons.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.panelButtons.FlowDirection = System.Windows.Forms.FlowDirection.TopDown;
            this.panelButtons.Location = new System.Drawing.Point(8, 344);
            this.panelButtons.Name = "panelButtons";
            this.panelButtons.Size = new System.Drawing.Size(254, 168);
            this.panelButtons.TabIndex = 1;
            this.panelButtons.WrapContents = false;

            int w = 240;
            this.btnRefresh.Location = new System.Drawing.Point(3, 3);
            this.btnRefresh.Name = "btnRefresh";
            this.btnRefresh.Size = new System.Drawing.Size(w, 30);
            this.btnRefresh.TabIndex = 0;
            this.btnRefresh.Text = "刷新列表";
            this.btnRefresh.Click += new System.EventHandler(this.OnRefresh);

            this.btnNew.Location = new System.Drawing.Point(3, 39);
            this.btnNew.Name = "btnNew";
            this.btnNew.Size = new System.Drawing.Size(w, 30);
            this.btnNew.TabIndex = 1;
            this.btnNew.Text = "新建";
            this.btnNew.Click += new System.EventHandler(this.OnNew);

            this.btnLoad.Location = new System.Drawing.Point(3, 75);
            this.btnLoad.Name = "btnLoad";
            this.btnLoad.Size = new System.Drawing.Size(w, 30);
            this.btnLoad.TabIndex = 2;
            this.btnLoad.Text = "加载选中";
            this.btnLoad.Click += new System.EventHandler(this.OnLoad);

            this.btnSave.Location = new System.Drawing.Point(3, 111);
            this.btnSave.Name = "btnSave";
            this.btnSave.Size = new System.Drawing.Size(w, 30);
            this.btnSave.TabIndex = 3;
            this.btnSave.Text = "保存";
            this.btnSave.Click += new System.EventHandler(this.OnSave);

            this.btnDelete.Location = new System.Drawing.Point(3, 147);
            this.btnDelete.Name = "btnDelete";
            this.btnDelete.Size = new System.Drawing.Size(w, 30);
            this.btnDelete.TabIndex = 4;
            this.btnDelete.Text = "删除选中";
            this.btnDelete.Click += new System.EventHandler(this.OnDelete);

            this.panelButtons.Controls.Add(this.btnRefresh);
            this.panelButtons.Controls.Add(this.btnNew);
            this.panelButtons.Controls.Add(this.btnLoad);
            this.panelButtons.Controls.Add(this.btnSave);
            this.panelButtons.Controls.Add(this.btnDelete);

            // ---------- panelRight ----------
            this.panelRight.BackColor = System.Drawing.Color.FromArgb(38, 38, 46);
            this.panelRight.Controls.Add(this.tableEdit);
            this.panelRight.Controls.Add(this.lblInfo);
            this.panelRight.Controls.Add(this.lblHint);
            this.panelRight.Dock = System.Windows.Forms.DockStyle.Fill;
            this.panelRight.Location = new System.Drawing.Point(270, 0);
            this.panelRight.Name = "panelRight";
            this.panelRight.Padding = new System.Windows.Forms.Padding(14);
            this.panelRight.Size = new System.Drawing.Size(690, 520);
            this.panelRight.TabIndex = 1;

            this.tableEdit.ColumnCount = 2;
            this.tableEdit.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 100F));
            this.tableEdit.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tableEdit.Dock = System.Windows.Forms.DockStyle.Top;
            this.tableEdit.Location = new System.Drawing.Point(14, 14);
            this.tableEdit.Name = "tableEdit";
            this.tableEdit.RowCount = 4;
            this.tableEdit.Size = new System.Drawing.Size(662, 140);
            this.tableEdit.TabIndex = 0;
            for (int i = 0; i < 4; i++)
                this.tableEdit.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 34F));

            this.lblName.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblName.ForeColor = System.Drawing.Color.Silver;
            this.lblName.Text = "配方名称";
            this.lblName.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.txtName.Dock = System.Windows.Forms.DockStyle.Fill;
            this.txtName.Font = new System.Drawing.Font("Microsoft YaHei UI", 10F);
            this.tableEdit.Controls.Add(this.lblName, 0, 0);
            this.tableEdit.Controls.Add(this.txtName, 1, 0);

            this.lblProduct.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblProduct.ForeColor = System.Drawing.Color.Silver;
            this.lblProduct.Text = "产品型号";
            this.lblProduct.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.txtProduct.Dock = System.Windows.Forms.DockStyle.Fill;
            this.txtProduct.Font = new System.Drawing.Font("Microsoft YaHei UI", 10F);
            this.tableEdit.Controls.Add(this.lblProduct, 0, 1);
            this.tableEdit.Controls.Add(this.txtProduct, 1, 1);

            this.lblCamera.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblCamera.ForeColor = System.Drawing.Color.Silver;
            this.lblCamera.Text = "相机 Id";
            this.lblCamera.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.txtCamera.Dock = System.Windows.Forms.DockStyle.Fill;
            this.txtCamera.Font = new System.Drawing.Font("Microsoft YaHei UI", 10F);
            this.tableEdit.Controls.Add(this.lblCamera, 0, 2);
            this.tableEdit.Controls.Add(this.txtCamera, 1, 2);

            this.lblNote.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblNote.ForeColor = System.Drawing.Color.Silver;
            this.lblNote.Text = "备注";
            this.lblNote.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.txtNote.Dock = System.Windows.Forms.DockStyle.Fill;
            this.txtNote.Font = new System.Drawing.Font("Microsoft YaHei UI", 10F);
            this.tableEdit.Controls.Add(this.lblNote, 0, 3);
            this.tableEdit.Controls.Add(this.txtNote, 1, 3);

            this.lblInfo.Dock = System.Windows.Forms.DockStyle.Top;
            this.lblInfo.ForeColor = System.Drawing.Color.Silver;
            this.lblInfo.Location = new System.Drawing.Point(14, 154);
            this.lblInfo.Name = "lblInfo";
            this.lblInfo.Size = new System.Drawing.Size(662, 90);
            this.lblInfo.TabIndex = 1;
            this.lblInfo.Text = "当前配方：无";

            this.lblHint.Dock = System.Windows.Forms.DockStyle.Top;
            this.lblHint.ForeColor = System.Drawing.Color.Gray;
            this.lblHint.Location = new System.Drawing.Point(14, 244);
            this.lblHint.Name = "lblHint";
            this.lblHint.Size = new System.Drawing.Size(662, 130);
            this.lblHint.TabIndex = 2;
            this.lblHint.Text = "说明\r\n────────────────────────────\r\n· 配方以 JSON 存放于 %AppData%\\VisionPlatform\\Recipes\r\n· 检测参数在「算法」窗体编辑后，点「保存为当前配方」写回\r\n· 加载配方后，算法窗体的参数会自动同步\r\n· 相机 Id 填 SIM-0 使用模拟相机";

            // ---------- RecipeForm ----------
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(38, 38, 46);
            this.ClientSize = new System.Drawing.Size(960, 520);
            this.Controls.Add(this.panelRight);
            this.Controls.Add(this.panelLeft);
            this.ForeColor = System.Drawing.Color.Gainsboro;
            this.MinimumSize = new System.Drawing.Size(820, 440);
            this.Name = "RecipeForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "配方管理";

            this.panelLeft.ResumeLayout(false);
            this.panelButtons.ResumeLayout(false);
            this.panelRight.ResumeLayout(false);
            this.tableEdit.ResumeLayout(false);
            this.tableEdit.PerformLayout();
            this.ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.Panel panelLeft;
        private System.Windows.Forms.ListBox listRecipes;
        private System.Windows.Forms.FlowLayoutPanel panelButtons;
        private VisionPlatform.UI.UiButton btnRefresh;
        private VisionPlatform.UI.UiButton btnNew;
        private VisionPlatform.UI.UiButton btnLoad;
        private VisionPlatform.UI.UiButton btnSave;
        private VisionPlatform.UI.UiButton btnDelete;

        private System.Windows.Forms.Panel panelRight;
        private System.Windows.Forms.TableLayoutPanel tableEdit;
        private System.Windows.Forms.Label lblName;
        private System.Windows.Forms.TextBox txtName;
        private System.Windows.Forms.Label lblProduct;
        private System.Windows.Forms.TextBox txtProduct;
        private System.Windows.Forms.Label lblCamera;
        private System.Windows.Forms.TextBox txtCamera;
        private System.Windows.Forms.Label lblNote;
        private System.Windows.Forms.TextBox txtNote;
        private System.Windows.Forms.Label lblInfo;
        private System.Windows.Forms.Label lblHint;
    }
}
