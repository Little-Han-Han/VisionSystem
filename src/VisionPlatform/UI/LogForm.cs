using System;
using System.Collections.Generic;
using System.Windows.Forms;
using VisionPlatform.Data;

namespace VisionPlatform.UI
{
    /// <summary>
    /// 日志查看：按日期范围 + 级别筛选，查看 NG（不良品）、软件报错（异常堆栈）与每日良率汇总。
    /// 数据来自 SQLite 日志库（<see cref="LogDb"/>）。控件全部在构造函数内创建并绑定事件，
    /// 不依赖 Designer 序列化，避免「重序列化丢失事件订阅」的已知坑。
    /// </summary>
    public partial class LogForm : Form
    {
        private DateTimePicker _dtpFrom;
        private DateTimePicker _dtpTo;
        private ComboBox _cmbLevel;
        private Button _btnQuery;
        private Label _lblSummary;
        private DataGridView _dgvDaily;
        private DataGridView _dgvDetail;

        /// <summary>级别下拉显示文本 ↔ 日志库 level 值（固定集合，非用户输入，可安全拼接 SQL）。</summary>
        private static readonly string[] LevelValues = { "", "OK", "NG", "ERROR", "INFO", "WARN" };

        public LogForm()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();

            this.Text = "日志查看";
            this.BackColor = System.Drawing.Color.FromArgb(38, 38, 46);
            this.ForeColor = System.Drawing.Color.Gainsboro;
            this.ClientSize = new System.Drawing.Size(900, 620);
            this.StartPosition = FormStartPosition.CenterParent;
            this.ShowInTaskbar = false;

            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4
            };
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            // ---------- 筛选条 ----------
            var panelFilter = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true,
                Padding = new Padding(4)
            };

            var lblFrom = new Label { Text = "起始日期：", AutoSize = true, ForeColor = System.Drawing.Color.Silver, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
            // DateTimePicker 不在 UiStyle 的主题白名单里（原生控件），显式给深色底，避免深色窗体上出现一块浅色。
            _dtpFrom = new DateTimePicker
            {
                Format = DateTimePickerFormat.Short,
                Value = DateTime.Today.AddDays(-7),
                Size = new System.Drawing.Size(110, 23),
                BackColor = System.Drawing.Color.FromArgb(44, 44, 54),
                ForeColor = System.Drawing.Color.Gainsboro
            };

            var lblTo = new Label { Text = "结束日期：", AutoSize = true, ForeColor = System.Drawing.Color.Silver, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
            _dtpTo = new DateTimePicker
            {
                Format = DateTimePickerFormat.Short,
                Value = DateTime.Today,
                Size = new System.Drawing.Size(110, 23),
                BackColor = System.Drawing.Color.FromArgb(44, 44, 54),
                ForeColor = System.Drawing.Color.Gainsboro
            };

            var lblLevel = new Label { Text = "级别：", AutoSize = true, ForeColor = System.Drawing.Color.Silver, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
            _cmbLevel = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Size = new System.Drawing.Size(110, 23) };
            _cmbLevel.Items.AddRange(new object[] { "全部", "OK（判定通过）", "NG（判定失败）", "软件报错", "信息", "警告" });
            _cmbLevel.SelectedIndex = 0;

            _btnQuery = new Button { Text = "查询", Size = new System.Drawing.Size(80, 26), UseVisualStyleBackColor = true };
            _btnQuery.Click += OnQuery;

            panelFilter.Controls.Add(lblFrom);
            panelFilter.Controls.Add(_dtpFrom);
            panelFilter.Controls.Add(lblTo);
            panelFilter.Controls.Add(_dtpTo);
            panelFilter.Controls.Add(lblLevel);
            panelFilter.Controls.Add(_cmbLevel);
            panelFilter.Controls.Add(_btnQuery);

            // ---------- 汇总标签 ----------
            _lblSummary = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = System.Drawing.Color.Gainsboro,
                Padding = new Padding(6, 2, 2, 2),
                Text = "（点击「查询」查看汇总）"
            };

            // ---------- 每日汇总表 ----------
            _dgvDaily = MakeGrid();
            _dgvDaily.Columns.Add(Col("日期", 110));
            _dgvDaily.Columns.Add(Col("检测数", 80));
            _dgvDaily.Columns.Add(Col("OK", 70));
            _dgvDaily.Columns.Add(Col("NG", 70));
            _dgvDaily.Columns.Add(Col("良率", 80));
            _dgvDaily.Columns.Add(Col("软件报错", 90));

            // ---------- 明细表 ----------
            _dgvDetail = MakeGrid();
            _dgvDetail.Columns.Add(Col("时间", 150));
            _dgvDetail.Columns.Add(Col("流程", 110));
            _dgvDetail.Columns.Add(Col("级别", 80));
            _dgvDetail.Columns.Add(Col("消息", 320));
            _dgvDetail.Columns.Add(Col("详情（异常堆栈）", 240));

            table.Controls.Add(panelFilter, 0, 0);
            table.Controls.Add(_lblSummary, 0, 1);
            table.Controls.Add(_dgvDaily, 0, 2);
            table.Controls.Add(_dgvDetail, 0, 3);

            this.Controls.Add(table);

            this.ResumeLayout(false);
            this.PerformLayout();

            UiStyle.ApplyForm(this);
            UiTitleBar.Attach(this);

            // 首次打开默认查最近 7 天
            OnQuery(this, EventArgs.Empty);
        }

        private static DataGridView MakeGrid()
        {
            var g = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                BackgroundColor = System.Drawing.Color.FromArgb(30, 30, 38),
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                Font = new System.Drawing.Font("Consolas", 9F)
            };
            return g;
        }

        private static DataGridViewColumn Col(string text, int width)
        {
            return new DataGridViewTextBoxColumn
            {
                HeaderText = text,
                Width = width,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                Resizable = DataGridViewTriState.True
            };
        }

        private void OnQuery(object sender, EventArgs e)
        {
            DateTime from = _dtpFrom.Value.Date;
            DateTime to = _dtpTo.Value.Date;
            if (to < from)
            {
                var t = from; from = to; to = t;
            }
            string level = LevelValues[_cmbLevel.SelectedIndex];

            List<LogDb.DailyRow> daily = LogDb.QueryDaily(from, to, level);
            List<LogDb.LogRow> detail = LogDb.QueryDetail(from, to, level);

            // 汇总
            int total = 0, ok = 0, ng = 0, err = 0;
            foreach (var d in daily) { total += d.Total; ok += d.Ok; ng += d.Ng; err += d.Error; }
            double yield = total > 0 ? Math.Round(ok * 100.0 / total, 1) : 0.0;
            _lblSummary.Text = string.Format(
                "区间 {0} ~ {1}：检测 {2}  |  OK {3}  |  NG {4}  |  良率 {5}%  |  软件报错 {6}  |  明细 {7} 条",
                from.ToString("yyyy-MM-dd"), to.ToString("yyyy-MM-dd"), total, ok, ng, yield, err, detail.Count);

            // 每日汇总表
            _dgvDaily.Rows.Clear();
            foreach (var d in daily)
            {
                _dgvDaily.Rows.Add(d.Day, d.Total, d.Ok, d.Ng, d.Yield.ToString("0.0") + "%", d.Error);
            }

            // 明细表
            _dgvDetail.Rows.Clear();
            foreach (var r in detail)
            {
                _dgvDetail.Rows.Add(r.Time, r.Source, r.Level, r.Message, r.Detail);
            }

            UiGrid.Apply(_dgvDaily);
            UiGrid.Apply(_dgvDetail);
        }
    }
}
