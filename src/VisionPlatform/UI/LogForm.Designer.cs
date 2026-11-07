namespace VisionPlatform.UI
{
    partial class LogForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null)) components.Dispose();
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// 设计器生成代码：只允许"字段 + 属性赋值 + Controls.Add + 具名事件处理器"。
        /// <para>
        /// 【为什么不能放业务调用】VS 打开设计视图时会真的执行构造函数与 InitializeComponent。
        /// 旧版把 <c>OnQuery(...)</c> 放在这里，设计期没有日志数据库 → 未处理异常，
        /// 设计器报 "未能分析 InitializeComponent，未将对象引用设置到对象的实例"。
        /// 查询已移到构造函数中的运行期分支（用 <see cref="System.ComponentModel.LicenseManager"/> 跳过设计期）。
        /// </para>
        /// </summary>
        private void InitializeComponent()
        {
            this.table = new System.Windows.Forms.TableLayoutPanel();
            this.panelFilter = new System.Windows.Forms.FlowLayoutPanel();
            this.lblFrom = new System.Windows.Forms.Label();
            this._dtpFrom = new System.Windows.Forms.DateTimePicker();
            this.lblTo = new System.Windows.Forms.Label();
            this._dtpTo = new System.Windows.Forms.DateTimePicker();
            this.lblLevel = new System.Windows.Forms.Label();
            this._cmbLevel = new System.Windows.Forms.ComboBox();
            this._btnQuery = new System.Windows.Forms.Button();
            this._lblSummary = new System.Windows.Forms.Label();
            this._dgvDaily = new System.Windows.Forms.DataGridView();
            this.colDay = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colTotal = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colOk = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colNg = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colYield = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colError = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this._dgvDetail = new System.Windows.Forms.DataGridView();
            this.colTime = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colSource = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colLevel = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colMessage = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colDetail = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.table.SuspendLayout();
            this.panelFilter.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this._dgvDaily)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this._dgvDetail)).BeginInit();
            this.SuspendLayout();
            // 
            // table
            // 
            this.table.ColumnCount = 1;
            this.table.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.table.Controls.Add(this.panelFilter, 0, 0);
            this.table.Controls.Add(this._lblSummary, 0, 1);
            this.table.Controls.Add(this._dgvDaily, 0, 2);
            this.table.Controls.Add(this._dgvDetail, 0, 3);
            this.table.Dock = System.Windows.Forms.DockStyle.Fill;
            this.table.RowCount = 4;
            this.table.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.table.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.table.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 150F));
            this.table.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            // 
            // panelFilter
            // 
            this.panelFilter.AutoSize = true;
            this.panelFilter.Dock = System.Windows.Forms.DockStyle.Fill;
            this.panelFilter.FlowDirection = System.Windows.Forms.FlowDirection.LeftToRight;
            this.panelFilter.Padding = new System.Windows.Forms.Padding(4);
            this.panelFilter.WrapContents = false;
            // 
            // lblFrom
            // 
            this.lblFrom.AutoSize = true;
            this.lblFrom.ForeColor = System.Drawing.Color.Silver;
            this.lblFrom.Text = "起始日期：";
            this.lblFrom.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // _dtpFrom
            // 
            this._dtpFrom.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(44)))), ((int)(((byte)(44)))), ((int)(((byte)(54)))));
            this._dtpFrom.ForeColor = System.Drawing.Color.Gainsboro;
            this._dtpFrom.Format = System.Windows.Forms.DateTimePickerFormat.Short;
            this._dtpFrom.Size = new System.Drawing.Size(110, 23);
            // 
            // lblTo
            // 
            this.lblTo.AutoSize = true;
            this.lblTo.ForeColor = System.Drawing.Color.Silver;
            this.lblTo.Text = "结束日期：";
            this.lblTo.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // _dtpTo
            // 
            this._dtpTo.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(44)))), ((int)(((byte)(44)))), ((int)(((byte)(54)))));
            this._dtpTo.ForeColor = System.Drawing.Color.Gainsboro;
            this._dtpTo.Format = System.Windows.Forms.DateTimePickerFormat.Short;
            this._dtpTo.Size = new System.Drawing.Size(110, 23);
            // 
            // lblLevel
            // 
            this.lblLevel.AutoSize = true;
            this.lblLevel.ForeColor = System.Drawing.Color.Silver;
            this.lblLevel.Text = "级别：";
            this.lblLevel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // _cmbLevel
            // 
            this._cmbLevel.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this._cmbLevel.Items.AddRange(new object[] {
            "全部",
            "OK（判定通过）",
            "NG（判定失败）",
            "软件报错",
            "信息",
            "警告"});
            this._cmbLevel.SelectedIndex = 0;
            this._cmbLevel.Size = new System.Drawing.Size(110, 23);
            // 
            // _btnQuery
            // 
            this._btnQuery.Size = new System.Drawing.Size(80, 26);
            this._btnQuery.Text = "查询";
            this._btnQuery.UseVisualStyleBackColor = true;
            this._btnQuery.Click += new System.EventHandler(this.OnQuery);
            // 
            // _lblSummary
            // 
            this._lblSummary.Dock = System.Windows.Forms.DockStyle.Fill;
            this._lblSummary.ForeColor = System.Drawing.Color.Gainsboro;
            this._lblSummary.Padding = new System.Windows.Forms.Padding(6, 2, 2, 2);
            this._lblSummary.Text = "（点击「查询」查看汇总）";
            // 
            // _dgvDaily
            // 
            this._dgvDaily.AllowUserToAddRows = false;
            this._dgvDaily.AllowUserToDeleteRows = false;
            this._dgvDaily.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;
            this._dgvDaily.BackgroundColor = System.Drawing.Color.FromArgb(((int)(((byte)(30)))), ((int)(((byte)(30)))), ((int)(((byte)(38)))));
            this._dgvDaily.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.colDay,
            this.colTotal,
            this.colOk,
            this.colNg,
            this.colYield,
            this.colError});
            this._dgvDaily.Dock = System.Windows.Forms.DockStyle.Fill;
            this._dgvDaily.Font = new System.Drawing.Font("Consolas", 9F);
            this._dgvDaily.ReadOnly = true;
            this._dgvDaily.RowHeadersVisible = false;
            this._dgvDaily.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            // 
            // colDay
            // 
            this.colDay.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.None;
            this.colDay.HeaderText = "日期";
            this.colDay.Resizable = System.Windows.Forms.DataGridViewTriState.True;
            this.colDay.Width = 110;
            // 
            // colTotal
            // 
            this.colTotal.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.None;
            this.colTotal.HeaderText = "检测数";
            this.colTotal.Resizable = System.Windows.Forms.DataGridViewTriState.True;
            this.colTotal.Width = 80;
            // 
            // colOk
            // 
            this.colOk.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.None;
            this.colOk.HeaderText = "OK";
            this.colOk.Resizable = System.Windows.Forms.DataGridViewTriState.True;
            this.colOk.Width = 70;
            // 
            // colNg
            // 
            this.colNg.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.None;
            this.colNg.HeaderText = "NG";
            this.colNg.Resizable = System.Windows.Forms.DataGridViewTriState.True;
            this.colNg.Width = 70;
            // 
            // colYield
            // 
            this.colYield.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.None;
            this.colYield.HeaderText = "良率";
            this.colYield.Resizable = System.Windows.Forms.DataGridViewTriState.True;
            this.colYield.Width = 80;
            // 
            // colError
            // 
            this.colError.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.None;
            this.colError.HeaderText = "软件报错";
            this.colError.Resizable = System.Windows.Forms.DataGridViewTriState.True;
            this.colError.Width = 90;
            // 
            // _dgvDetail
            // 
            this._dgvDetail.AllowUserToAddRows = false;
            this._dgvDetail.AllowUserToDeleteRows = false;
            this._dgvDetail.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;
            this._dgvDetail.BackgroundColor = System.Drawing.Color.FromArgb(((int)(((byte)(30)))), ((int)(((byte)(30)))), ((int)(((byte)(38)))));
            this._dgvDetail.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.colTime,
            this.colSource,
            this.colLevel,
            this.colMessage,
            this.colDetail});
            this._dgvDetail.Dock = System.Windows.Forms.DockStyle.Fill;
            this._dgvDetail.Font = new System.Drawing.Font("Consolas", 9F);
            this._dgvDetail.ReadOnly = true;
            this._dgvDetail.RowHeadersVisible = false;
            this._dgvDetail.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            // 
            // colTime
            // 
            this.colTime.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.None;
            this.colTime.HeaderText = "时间";
            this.colTime.Resizable = System.Windows.Forms.DataGridViewTriState.True;
            this.colTime.Width = 150;
            // 
            // colSource
            // 
            this.colSource.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.None;
            this.colSource.HeaderText = "流程";
            this.colSource.Resizable = System.Windows.Forms.DataGridViewTriState.True;
            this.colSource.Width = 110;
            // 
            // colLevel
            // 
            this.colLevel.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.None;
            this.colLevel.HeaderText = "级别";
            this.colLevel.Resizable = System.Windows.Forms.DataGridViewTriState.True;
            this.colLevel.Width = 80;
            // 
            // colMessage
            // 
            this.colMessage.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.None;
            this.colMessage.HeaderText = "消息";
            this.colMessage.Resizable = System.Windows.Forms.DataGridViewTriState.True;
            this.colMessage.Width = 320;
            // 
            // colDetail
            // 
            this.colDetail.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.None;
            this.colDetail.HeaderText = "详情（异常堆栈）";
            this.colDetail.Resizable = System.Windows.Forms.DataGridViewTriState.True;
            this.colDetail.Width = 240;
            // 
            // LogForm
            // 
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(38)))), ((int)(((byte)(38)))), ((int)(((byte)(46)))));
            this.ClientSize = new System.Drawing.Size(900, 620);
            this.Controls.Add(this.table);
            this.ForeColor = System.Drawing.Color.Gainsboro;
            this.Name = "LogForm";
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "日志查看";
            this.table.ResumeLayout(false);
            this.table.PerformLayout();
            this.panelFilter.ResumeLayout(false);
            this.panelFilter.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this._dgvDaily)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this._dgvDetail)).EndInit();
            this.ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.TableLayoutPanel table;
        private System.Windows.Forms.FlowLayoutPanel panelFilter;
        private System.Windows.Forms.Label lblFrom;
        private System.Windows.Forms.DateTimePicker _dtpFrom;
        private System.Windows.Forms.Label lblTo;
        private System.Windows.Forms.DateTimePicker _dtpTo;
        private System.Windows.Forms.Label lblLevel;
        private System.Windows.Forms.ComboBox _cmbLevel;
        private System.Windows.Forms.Button _btnQuery;
        private System.Windows.Forms.Label _lblSummary;
        private System.Windows.Forms.DataGridView _dgvDaily;
        private System.Windows.Forms.DataGridViewTextBoxColumn colDay;
        private System.Windows.Forms.DataGridViewTextBoxColumn colTotal;
        private System.Windows.Forms.DataGridViewTextBoxColumn colOk;
        private System.Windows.Forms.DataGridViewTextBoxColumn colNg;
        private System.Windows.Forms.DataGridViewTextBoxColumn colYield;
        private System.Windows.Forms.DataGridViewTextBoxColumn colError;
        private System.Windows.Forms.DataGridView _dgvDetail;
        private System.Windows.Forms.DataGridViewTextBoxColumn colTime;
        private System.Windows.Forms.DataGridViewTextBoxColumn colSource;
        private System.Windows.Forms.DataGridViewTextBoxColumn colLevel;
        private System.Windows.Forms.DataGridViewTextBoxColumn colMessage;
        private System.Windows.Forms.DataGridViewTextBoxColumn colDetail;
    }
}
