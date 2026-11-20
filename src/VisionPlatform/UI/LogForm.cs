using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using VisionPlatform.Data;

namespace VisionPlatform.UI
{
    /// <summary>
    /// 日志查看：按日期范围 + 级别筛选，查看 NG（不良品）、软件报错（异常堆栈）与每日良率汇总。
    /// 数据来自 SQLite 日志库（<see cref="LogDb"/>）。
    /// <para>
    /// 【界面代码在 <c>LogForm.Designer.cs</c>】设计器只认"字段 + 属性赋值 + 具名事件处理器"；
    /// 旧版把手写控件树和 <c>OnQuery(...)</c> 都放在 <c>InitializeComponent</c> 里，
    /// 设计器执行构造期查询时没有日志库 → 报
    /// "未能分析 InitializeComponent，未将对象引用设置到对象的实例"。
    /// </para>
    /// <para>
    /// 【运行期初始化】日期默认值、主题、首次查询都在构造函数里、且在
    /// <see cref="LicenseManager"/> 判定为非设计期之后执行；设计视图只看到静态界面。
    /// </para>
    /// </summary>
    public partial class LogForm : Form
    {
        /// <summary>级别下拉显示文本 ↔ 日志库 level 值（固定集合，非用户输入，可安全拼接 SQL）。</summary>
        private static readonly string[] LevelValues = { "", "OK", "NG", "ERROR", "INFO", "WARN" };

        public LogForm()
        {
            InitializeComponent();
            _dgvDetail.CellDoubleClick += OnDetailDoubleClick;

            // 设计器实例化窗体时会执行构造函数：跳过数据库查询与运行时主题绑定，
            // 否则设计视图里没有 LogDb / ConfigStore，会直接抛在 InitializeComponent 之后。
            if (LicenseManager.UsageMode == LicenseUsageMode.Designtime) return;

            _dtpFrom.Value = DateTime.Today.AddDays(-7);
            _dtpTo.Value = DateTime.Today;

            UiStyle.ApplyForm(this);
            UiTitleBar.Attach(this);

            // 首次打开默认查最近 7 天（保留"构造即查询"的既有行为）
            OnQuery(this, EventArgs.Empty);
        }

        /// <summary>双击明细行：完整消息 + 异常堆栈用大窗口查看/复制，避免列宽不够。</summary>
        private void OnDetailDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _dgvDetail.Rows.Count) return;

            DataGridViewRow row = _dgvDetail.Rows[e.RowIndex];
            string message = Convert.ToString(row.Cells[3].Value, CultureInfo.InvariantCulture);
            string detail = Convert.ToString(row.Cells[4].Value, CultureInfo.InvariantCulture);
            if (string.IsNullOrEmpty(message) && string.IsNullOrEmpty(detail)) return;

            using (var dlg = new Form())
            {
                dlg.Text = "日志详情";
                dlg.ClientSize = new Size(860, 560);
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.ShowInTaskbar = false;

                var box = new TextBox();
                box.Dock = DockStyle.Fill;
                box.Multiline = true;
                box.ReadOnly = true;
                box.ScrollBars = ScrollBars.Both;
                box.WordWrap = false;
                box.Font = new Font("Consolas", 9.5f);
                box.BackColor = Color.FromArgb(24, 24, 30);
                box.ForeColor = Color.Gainsboro;
                box.Text = string.IsNullOrEmpty(detail) ? message : (message + Environment.NewLine + Environment.NewLine + detail);

                var bar = new FlowLayoutPanel();
                bar.Dock = DockStyle.Bottom;
                bar.Height = 44;
                bar.FlowDirection = FlowDirection.RightToLeft;
                bar.Padding = new Padding(8, 8, 8, 8);

                var close = new Button { Text = "关闭", DialogResult = DialogResult.OK, Width = 80, Height = 28 };
                var copy = new Button { Text = "复制", Width = 80, Height = 28 };
                copy.Click += delegate
                {
                    try { Clipboard.SetText(box.Text ?? ""); } catch (Exception) { }
                };
                bar.Controls.Add(close);
                bar.Controls.Add(copy);

                dlg.Controls.Add(box);
                dlg.Controls.Add(bar);
                dlg.AcceptButton = close;
                dlg.CancelButton = close;
                dlg.ShowDialog(this);
            }
        }

        private void OnQuery(object sender, EventArgs e)
        {
            DateTime from = _dtpFrom.Value.Date;
            DateTime to = _dtpTo.Value.Date;
            if (to < from)
            {
                var t = from; from = to; to = t;
            }

            int levelIndex = _cmbLevel.SelectedIndex;
            string level = (levelIndex >= 0 && levelIndex < LevelValues.Length) ? LevelValues[levelIndex] : "";

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
