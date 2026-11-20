using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;
using VisionPlatform.Core;
using VisionPlatform.Communication;
using VisionPlatform.Flow;

namespace VisionPlatform.UI
{
    /// <summary>
    /// 通讯点位 / 消息 / 任务 / 流程 Busy-Ready 绑定 / 诊断。
    /// <para>纯代码构建，不依赖 Designer：字段少、随配置结构演进不用改设计器。</para>
    /// </summary>
    public sealed class CommAutomationForm : Form
    {
        private readonly TabControl _tabs = new TabControl();
        private readonly DataGridView _gridPoints = NewGrid();
        private readonly DataGridView _gridMessages = NewGrid();
        private readonly DataGridView _gridJobs = NewGrid();
        private readonly DataGridView _gridBindings = NewGrid();
        private readonly TextBox _diagnostics = new TextBox();
        private readonly Label _status = new Label();
        private readonly Timer _diagTimer = new Timer();

        public CommAutomationForm()
        {
            this.Text = "通讯点位与任务";
            ClientSize = new Size(1180, 720);
            MinimumSize = new Size(900, 560);
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            KeyPreview = true;

            BuildUi();
            _gridJobs.CellDoubleClick += OnJobsCellDoubleClick;
            LoadFromConfig();

            UiStyle.ApplyForm(this);
            UiGrid.Apply(_gridPoints);
            UiGrid.Apply(_gridMessages);
            UiGrid.Apply(_gridJobs);
            UiGrid.Apply(_gridBindings);
            UiTitleBar.Attach(this);

            _diagTimer.Interval = 1000;
            _diagTimer.Tick += delegate { RefreshDiagnostics(); };
            _diagTimer.Start();
            RefreshDiagnostics();
        }

        // =====================================================================
        //  界面
        // =====================================================================

        private void BuildUi()
        {
            var bottom = new FlowLayoutPanel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = 48;
            bottom.FlowDirection = FlowDirection.LeftToRight;
            bottom.Padding = new Padding(8, 8, 8, 8);
            bottom.WrapContents = false;

            var save = NewButton("保存", 96);
            save.Click += delegate { SaveToConfig(); };
            var reload = NewButton("重新载入", 96);
            reload.Click += delegate { LoadFromConfig(); RefreshDiagnostics(); };
            var refresh = NewButton("刷新下拉", 96);
            refresh.Click += delegate { RefreshLookups(); _status.Text = "下拉选项已刷新"; };
            var close = NewButton("关闭", 96);
            close.Click += delegate { Close(); };

            _status.AutoSize = false;
            _status.Size = new Size(620, 30);
            _status.TextAlign = ContentAlignment.MiddleLeft;
            _status.ForeColor = UiTheme.ColorTextSecondary;

            bottom.Controls.Add(save);
            bottom.Controls.Add(reload);
            bottom.Controls.Add(refresh);
            bottom.Controls.Add(close);
            bottom.Controls.Add(_status);

            _tabs.Dock = DockStyle.Fill;
            _tabs.TabPages.Add(BuildPointsPage());
            _tabs.TabPages.Add(BuildMessagesPage());
            _tabs.TabPages.Add(BuildJobsPage());
            _tabs.TabPages.Add(BuildBindingsPage());
            _tabs.TabPages.Add(BuildDiagnosticsPage());

            Controls.Add(_tabs);
            Controls.Add(bottom);
        }

        private TabPage BuildPointsPage()
        {
            var page = NewPage("点位");
            _gridPoints.Columns.Add(TextColumn("名称", 150));
            _gridPoints.Columns.Add(ComboColumn("端点", 120, true));
            _gridPoints.Columns.Add(TextColumn("地址", 100));
            _gridPoints.Columns.Add(ComboColumn("方向", 80, false));
            _gridPoints.Columns.Add(ComboColumn("类型", 80, false));
            _gridPoints.Columns.Add(TextColumn("长度", 50));
            _gridPoints.Columns.Add(ComboColumn("字序", 70, false));
            _gridPoints.Columns.Add(TextColumn("Scale", 60));
            _gridPoints.Columns.Add(TextColumn("Offset", 60));
            _gridPoints.Columns.Add(TextColumn("默认值", 80));
            _gridPoints.Columns.Add(TextColumn("说明", 220));
            page.Controls.Add(_gridPoints);
            var pointTip = new Label();
            pointTip.Dock = DockStyle.Bottom;
            pointTip.Height = 44;
            pointTip.ForeColor = UiTheme.ColorTextSecondary;
            pointTip.Text = "多流程只需配第 0 条流程的地址：第 i 条流程的寄存器 = 基址 + i × 步长（步长在「任务」页填）。\r\n" +
                            "例：步长 100、触发点位 HR:0 → 第 1 条读 HR:0、第 2 条读 HR:100；结果 MbMap 基址 20 → 第 2 条写 HR:120。";
            page.Controls.Add(pointTip);

            page.Controls.Add(BuildToolbar(_gridPoints, "点位名（如 Line1.Ready）", DefaultPointRow));
            return page;
        }

        private TabPage BuildMessagesPage()
        {
            var page = NewPage("消息");
            _gridMessages.Columns.Add(TextColumn("名称", 140));
            _gridMessages.Columns.Add(ComboColumn("端点", 120, true));
            _gridMessages.Columns.Add(ComboColumn("模式", 80, false));
            _gridMessages.Columns.Add(TextColumn("模板 / 点位对", 620));
            _gridMessages.Columns.Add(TextColumn("结束符", 80));
            page.Controls.Add(_gridMessages);
            page.Controls.Add(BuildToolbar(_gridMessages, "消息名（如 ResultLine1）", DefaultMessageRow));
            return page;
        }

        private TabPage BuildJobsPage()
        {
            var page = NewPage("任务");
            _gridJobs.Columns.Add(Check("启用", 50));
            _gridJobs.Columns.Add(TextColumn("名称", 130));
            _gridJobs.Columns.Add(ComboColumn("触发", 100, false));
            _gridJobs.Columns.Add(TextColumn("周期ms", 70));
            _gridJobs.Columns.Add(TextColumn("流程(;分隔)", 160));
            _gridJobs.Columns.Add(ComboColumn("模式", 90, false));
            _gridJobs.Columns.Add(TextColumn("窗口ms", 70));
            _gridJobs.Columns.Add(ComboColumn("触发点位", 130, true));
            _gridJobs.Columns.Add(TextColumn("期望值", 70));
            _gridJobs.Columns.Add(TextColumn("多流程步长", 90));
            _gridJobs.Columns.Add(TextColumn("动作", 520));

            var tip = new Label();
            tip.Dock = DockStyle.Bottom;
            tip.Height = 46;
            tip.ForeColor = UiTheme.ColorTextSecondary;
            tip.Height = 62;
            tip.Text = "动作格式：SetPoint:Line1.Busy=true;  SendMessage:ResultLine1;  " +
                       "WaitPoint:Line1.Ack=true@500;  Sleep:1000;  StartFlow:F1;  Log:msg\r\n" +
                       "多流程步长：0=只读上面那一个触发点位（老行为）。填 100 表示按流程顺序分区——" +
                       "第 0 条读「触发点位」原地址，第 1 条读 +100，第 2 条读 +200（序号见「流程编排 → 流程管理」）";
            page.Controls.Add(_gridJobs);
            page.Controls.Add(tip);
            page.Controls.Add(BuildToolbar(_gridJobs, "双击「流程」或「动作」列可编辑", DefaultJobRow,
                NewActionButton("选择流程…", 96, PickFlows),
                NewActionButton("编辑动作…", 96, EditActions)));
            return page;
        }

        private TabPage BuildBindingsPage()
        {
            var page = NewPage("流程信号");
            _gridBindings.Columns.Add(Check("启用", 50));
            _gridBindings.Columns.Add(ComboColumn("流程", 180, false));
            _gridBindings.Columns.Add(ComboColumn("端点", 120, true));
            _gridBindings.Columns.Add(ComboColumn("Busy 点位", 160, true));
            _gridBindings.Columns.Add(ComboColumn("Ready 点位", 160, true));
            _gridBindings.Columns.Add(ComboColumn("Fault 点位", 160, true));
            page.Controls.Add(_gridBindings);
            page.Controls.Add(BuildToolbar(_gridBindings, "流程 → Busy/Ready/Fault 点位", DefaultBindingRow));
            return page;
        }

        private TabPage BuildDiagnosticsPage()
        {
            var page = NewPage("诊断");
            _diagnostics.Dock = DockStyle.Fill;
            _diagnostics.Multiline = true;
            _diagnostics.ReadOnly = true;
            _diagnostics.ScrollBars = ScrollBars.Both;
            _diagnostics.WordWrap = false;
            _diagnostics.Font = new Font("Consolas", 9.5f);
            _diagnostics.BackColor = UiTheme.ColorCardBg;
            _diagnostics.ForeColor = UiTheme.ColorTextPrimary;
            page.Controls.Add(_diagnostics);
            return page;
        }

        private static TabPage NewPage(string text)
        {
            var page = new TabPage(text);
            page.BackColor = UiTheme.ColorPanelBg;
            return page;
        }

        private static DataGridView NewGrid()
        {
            var grid = new DataGridView();
            grid.Dock = DockStyle.Fill;
            grid.AllowUserToAddRows = true;
            grid.AllowUserToDeleteRows = true;
            grid.RowHeadersVisible = false;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
            grid.EditMode = DataGridViewEditMode.EditOnEnter;
            grid.BackgroundColor = UiTheme.ColorPanelBg;
            grid.BorderStyle = BorderStyle.None;
            // 下拉列遇到"配置里有、下拉项还没刷新"的值时不要弹异常框
            grid.DataError += delegate(object s, DataGridViewDataErrorEventArgs e) { e.ThrowException = false; };
            return grid;
        }

        private static DataGridViewTextBoxColumn TextColumn(string header, int width)
        {
            var col = new DataGridViewTextBoxColumn();
            col.HeaderText = header;
            col.Width = width;
            return col;
        }

        private static DataGridViewComboBoxColumn ComboColumn(string header, int width, bool allowEmpty)
        {
            var col = new DataGridViewComboBoxColumn();
            col.HeaderText = header;
            col.Width = width;
            col.FlatStyle = FlatStyle.Flat;
            col.DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton;
            if (allowEmpty) col.Items.Add("");
            return col;
        }

        private static DataGridViewCheckBoxColumn Check(string header, int width)
        {
            var col = new DataGridViewCheckBoxColumn();
            col.HeaderText = header;
            col.Width = width;
            return col;
        }

        private Control BuildToolbar(DataGridView grid, string hint, Func<object[]> defaultRow, params Button[] extras)
        {
            var bar = new FlowLayoutPanel();
            bar.Dock = DockStyle.Top;
            bar.Height = 40;
            bar.Padding = new Padding(4, 4, 4, 4);
            bar.WrapContents = false;

            var add = NewButton("新增行", 80);
            add.Click += delegate
            {
                object[] values = defaultRow != null ? defaultRow() : new object[0];
                int index = grid.Rows.Add(values);
                if (index >= 0)
                    grid.CurrentCell = grid.Rows[index].Cells[0];
            };

            var del = NewButton("删除选中", 90);
            del.Click += delegate
            {
                if (grid.SelectedCells.Count == 0) return;
                var rows = new HashSet<int>();
                foreach (DataGridViewCell c in grid.SelectedCells) rows.Add(c.RowIndex);
                foreach (int r in rows)
                    if (r >= 0 && r < grid.Rows.Count && !grid.Rows[r].IsNewRow) grid.Rows.RemoveAt(r);
            };

            var note = new Label();
            note.AutoSize = true;
            note.Margin = new Padding(10, 8, 0, 0);
            note.ForeColor = UiTheme.ColorTextSecondary;
            note.Text = hint;

            bar.Controls.Add(add);
            bar.Controls.Add(del);
            if (extras != null)
            {
                foreach (Button b in extras)
                    if (b != null) bar.Controls.Add(b);
            }
            bar.Controls.Add(note);
            return bar;
        }

        private static Button NewButton(string text, int width)
        {
            var b = new Button();
            b.Text = text;
            b.Width = width;
            b.Height = 28;
            b.FlatStyle = FlatStyle.Flat;
            b.BackColor = UiTheme.ColorCardBg;
            b.ForeColor = UiTheme.ColorTextPrimary;
            b.Margin = new Padding(0, 0, 6, 0);
            return b;
        }

        // =====================================================================
        //  下拉选项 / 默认行
        // =====================================================================

        private void RefreshLookups()
        {
            _gridPoints.EndEdit();
            _gridMessages.EndEdit();
            _gridJobs.EndEdit();
            _gridBindings.EndEdit();

            List<string> endpoints = EndpointNames();

            SetComboItems(_gridPoints, 1, endpoints, true);
            SetComboItems(_gridPoints, 3, new[] { "Read", "Write", "ReadWrite" }, false);
            SetComboItems(_gridPoints, 4, new[] { "Bool", "Int16", "UInt16", "Int32", "UInt32", "Float", "Double", "String" }, false);
            SetComboItems(_gridPoints, 6, new[] { "ABCD", "CDAB", "BADC", "DCBA" }, false);

            SetComboItems(_gridMessages, 1, endpoints, true);
            SetComboItems(_gridMessages, 2, new[] { "Text", "Json", "Points" }, false);

            SetComboItems(_gridJobs, 2, new[] { "Timer", "ResultGroup", "PointEdge", "Startup" }, false);
            SetComboItems(_gridJobs, 5, new[] { "Latest", "AllFresh" }, false);
            SetComboItems(_gridJobs, 7, PointNames(), true);

            SetComboItems(_gridBindings, 1, FlowNames(), false);
            SetComboItems(_gridBindings, 2, endpoints, true);
            SetComboItems(_gridBindings, 3, PointNames(), true);
            SetComboItems(_gridBindings, 4, PointNames(), true);
            SetComboItems(_gridBindings, 5, PointNames(), true);
        }

        private static void SetComboItems(DataGridView grid, int column, IEnumerable<string> values, bool allowEmpty)
        {
            if (grid == null || column < 0 || column >= grid.Columns.Count) return;

            var combo = grid.Columns[column] as DataGridViewComboBoxColumn;
            if (combo == null) return;

            // 只补不删：清空 Items 会把正在编辑、尚未保存的单元格值置空；
            // 已删除的名称留在下拉里最多是多余选项，比丢用户输入安全。
            if (allowEmpty && !combo.Items.Contains("")) combo.Items.Add("");

            if (values == null) return;
            foreach (string v in values)
            {
                if (string.IsNullOrWhiteSpace(v)) continue;
                if (!combo.Items.Contains(v)) combo.Items.Add(v);
            }
        }

        private List<string> EndpointNames()
        {
            var list = new List<string>();
            CommSettings settings = ConfigStore.Current.Comm;
            if (settings != null && settings.Endpoints != null)
            {
                foreach (CommEndpointConfig ep in settings.Endpoints)
                    if (ep != null && !string.IsNullOrEmpty(ep.Name) && !list.Contains(ep.Name))
                        list.Add(ep.Name);
            }
            return list;
        }

        private List<string> PointNames()
        {
            var list = new List<string>();
            CommSettings settings = ConfigStore.Current.Comm;
            if (settings != null && settings.Points != null)
            {
                foreach (CommPointConfig p in settings.Points)
                    if (p != null && !string.IsNullOrEmpty(p.Name) && !list.Contains(p.Name))
                        list.Add(p.Name);
            }
            return list;
        }

        private List<string> FlowNames()
        {
            var list = new List<string>();
            FlowSettings fs = ConfigStore.Current.Flows;
            if (fs != null && fs.Flows != null)
            {
                foreach (FlowDefinition f in fs.Flows)
                {
                    if (f == null) continue;
                    string name = string.IsNullOrEmpty(f.Name) ? f.Id : f.Name;
                    if (!string.IsNullOrEmpty(name) && !list.Contains(name)) list.Add(name);
                }
            }
            return list;
        }

        private string FirstEndpoint()
        {
            List<string> endpoints = EndpointNames();
            return endpoints.Count > 0 ? endpoints[0] : "";
        }

        private string FirstPoint()
        {
            List<string> points = PointNames();
            return points.Count > 0 ? points[0] : "";
        }

        private string FirstPointByDirection(string direction)
        {
            List<string> points = PointNamesByDirection(direction);
            return points.Count > 0 ? points[0] : "";
        }

        private List<string> PointNamesByDirection(string direction)
        {
            var list = new List<string>();
            CommSettings settings = ConfigStore.Current.Comm;
            if (settings == null || settings.Points == null) return list;

            foreach (CommPointConfig p in settings.Points)
            {
                if (p == null || string.IsNullOrEmpty(p.Name)) continue;
                string dir = CommPointConfig.NormalizeDirection(p.Direction);
                if (dir == direction || dir == "ReadWrite") list.Add(p.Name);
            }
            return list;
        }

        private object[] DefaultPointRow()
        {
            string ep = FirstEndpoint();
            string address = EndpointKind(ep) == CommKind.ModbusTcp ? "HR:0" : "";
            return new object[] { "", ep, address, "ReadWrite", "Bool", "1", "ABCD", "1", "0", "", "" };
        }

        private object[] DefaultMessageRow()
        {
            return new object[] { "", FirstEndpoint(), "Text", "", "\\r\\n" };
        }

        private object[] DefaultJobRow()
        {
            string point = FirstPointByDirection("Read");
            string writePoint = FirstPointByDirection("Write");
            if (writePoint.Length == 0) writePoint = FirstPoint();
            string actions = writePoint.Length > 0 ? "SetPoint:" + writePoint + "=true" : "";
            return new object[]
            {
                true,
                "任务" + (_gridJobs.Rows.Count + 1).ToString(CultureInfo.InvariantCulture),
                "Timer", "1000", "", "Latest", "500", point, "true", "0", actions
            };
        }

        private object[] DefaultBindingRow()
        {
            List<string> flows = FlowNames();
            List<string> points = PointNamesByDirection("Write");
            return new object[]
            {
                true,
                flows.Count > 0 ? flows[0] : "",
                FirstEndpoint(),
                points.Count > 0 ? points[0] : "",
                points.Count > 1 ? points[1] : "",
                points.Count > 2 ? points[2] : ""
            };
        }

        private string EndpointKind(string endpoint)
        {
            CommSettings settings = ConfigStore.Current.Comm;
            if (settings != null && settings.Endpoints != null)
            {
                foreach (CommEndpointConfig ep in settings.Endpoints)
                    if (ep != null && string.Equals(ep.Name, endpoint, StringComparison.OrdinalIgnoreCase))
                        return ep.Kind;
            }
            return "";
        }

        private Button NewActionButton(string text, int width, Action action)
        {
            Button b = NewButton(text, width);
            b.Click += delegate { action(); };
            return b;
        }

        // =====================================================================
        //  点位 / 任务辅助编辑
        // =====================================================================

        private void OnJobsCellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            if (e.ColumnIndex == 4) PickFlows();
            else if (e.ColumnIndex == 9) EditActions();
        }

        private DataGridViewRow CurrentJobRow()
        {
            if (_gridJobs.CurrentCell == null) return null;
            int index = _gridJobs.CurrentCell.RowIndex;
            if (index < 0 || index >= _gridJobs.Rows.Count) return null;
            return _gridJobs.Rows[index];
        }

        private void PickFlows()
        {
            DataGridViewRow row = CurrentJobRow();
            if (row == null || row.IsNewRow) return;

            List<string> flows = FlowNames();
            if (flows.Count == 0)
            {
                MessageBox.Show(this, "还没有流程可绑定。请先在流程编排里创建流程。", "选择流程",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string current = Str(row, 4);
            var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string s in Split(current)) selected.Add(s);

            using (var dlg = new Form())
            {
                dlg.Text = "选择流程（可多选）";
                dlg.ClientSize = new Size(360, 420);
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.MaximizeBox = false;
                dlg.MinimizeBox = false;
                dlg.ShowInTaskbar = false;

                var list = new CheckedListBox();
                list.Dock = DockStyle.Fill;
                list.CheckOnClick = true;
                foreach (string f in flows)
                    list.Items.Add(f, selected.Contains(f));

                var bar = new FlowLayoutPanel();
                bar.Dock = DockStyle.Bottom;
                bar.Height = 44;
                bar.FlowDirection = FlowDirection.RightToLeft;
                bar.Padding = new Padding(8, 8, 8, 8);

                var ok = new Button { Text = "确定", DialogResult = DialogResult.OK, Width = 80, Height = 28 };
                var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Width = 80, Height = 28 };
                bar.Controls.Add(ok);
                bar.Controls.Add(cancel);

                dlg.Controls.Add(list);
                dlg.Controls.Add(bar);
                dlg.AcceptButton = ok;
                dlg.CancelButton = cancel;

                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                var picked = new List<string>();
                foreach (object item in list.CheckedItems)
                    picked.Add(Convert.ToString(item, CultureInfo.InvariantCulture));

                row.Cells[4].Value = string.Join(";", picked.ToArray());
            }
        }

        private void EditActions()
        {
            DataGridViewRow row = CurrentJobRow();
            if (row == null || row.IsNewRow) return;

            List<CommActionConfig> actions = ParseActions(Str(row, 9));
            using (var dlg = new CommActionEditorForm(actions, PointNames(), MessageNames(), FlowNames()))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                row.Cells[9].Value = ActionsToString(dlg.Actions);
            }
        }

        private List<string> MessageNames()
        {
            var list = new List<string>();
            CommSettings settings = ConfigStore.Current.Comm;
            if (settings != null && settings.Messages != null)
            {
                foreach (CommMessageConfig m in settings.Messages)
                    if (m != null && !string.IsNullOrEmpty(m.Name) && !list.Contains(m.Name))
                        list.Add(m.Name);
            }
            return list;
        }

        // =====================================================================
        //  配置 ↔ 表格
        // =====================================================================

        private void LoadFromConfig()
        {
            CommSettings settings = ConfigStore.Current.Comm;
            CommSettings.UpgradeToCurrent(settings);

            RefreshLookups();
            _gridPoints.Rows.Clear();
            foreach (CommPointConfig p in settings.Points)
            {
                if (p == null) continue;
                _gridPoints.Rows.Add(p.Name, p.Endpoint, p.Address, p.Direction, p.Type,
                    p.Length.ToString(CultureInfo.InvariantCulture), p.WordOrder,
                    p.Scale.ToString("G", CultureInfo.InvariantCulture),
                    p.Offset.ToString("G", CultureInfo.InvariantCulture),
                    p.Default, p.Description);
            }

            _gridMessages.Rows.Clear();
            foreach (CommMessageConfig m in settings.Messages)
            {
                if (m == null) continue;
                _gridMessages.Rows.Add(m.Name, m.Endpoint, m.Mode, m.Format, m.Terminator);
            }

            _gridJobs.Rows.Clear();
            foreach (CommJobConfig j in settings.Jobs)
            {
                if (j == null) continue;
                _gridJobs.Rows.Add(j.Enabled, j.Name, j.Trigger,
                    j.IntervalMs.ToString(CultureInfo.InvariantCulture),
                    Join(j.Flows), j.Mode,
                    j.WindowMs.ToString(CultureInfo.InvariantCulture),
                    j.Point, j.Expect, j.FlowStride.ToString(CultureInfo.InvariantCulture),
                    ActionsToString(j.Actions));
            }

            _gridBindings.Rows.Clear();
            foreach (CommFlowBindingConfig b in settings.FlowBindings)
            {
                if (b == null) continue;
                _gridBindings.Rows.Add(b.Enabled, b.Flow, b.Endpoint, b.BusyPoint, b.ReadyPoint, b.FaultPoint);
            }

            _status.Text = "已载入：点位 " + settings.Points.Count
                         + " · 消息 " + settings.Messages.Count
                         + " · 任务 " + settings.Jobs.Count
                         + " · 流程绑定 " + settings.FlowBindings.Count;
        }

        private void SaveToConfig()
        {
            try
            {
                CommSettings settings = ConfigStore.Current.Comm;

                var points = new List<CommPointConfig>();
                foreach (DataGridViewRow row in _gridPoints.Rows)
                {
                    if (row.IsNewRow) continue;
                    string name = Str(row, 0);
                    if (name.Length == 0) continue;

                    var p = new CommPointConfig
                    {
                        Name = name,
                        Endpoint = Str(row, 1),
                        Address = Str(row, 2),
                        Direction = Str(row, 3),
                        Type = Str(row, 4),
                        Length = Int(row, 5, 1),
                        WordOrder = Str(row, 6),
                        Scale = Double(row, 7, 1),
                        Offset = Double(row, 8, 0),
                        Default = Str(row, 9),
                        Description = Str(row, 10)
                    };
                    p.Normalize();
                    points.Add(p);
                }

                var messages = new List<CommMessageConfig>();
                foreach (DataGridViewRow row in _gridMessages.Rows)
                {
                    if (row.IsNewRow) continue;
                    string name = Str(row, 0);
                    if (name.Length == 0) continue;

                    var m = new CommMessageConfig
                    {
                        Name = name,
                        Endpoint = Str(row, 1),
                        Mode = Str(row, 2),
                        Format = Str(row, 3),
                        Terminator = Str(row, 4),
                        Points = new List<string>()
                    };
                    m.Normalize();
                    messages.Add(m);
                }

                var jobs = new List<CommJobConfig>();
                foreach (DataGridViewRow row in _gridJobs.Rows)
                {
                    if (row.IsNewRow) continue;
                    string name = Str(row, 1);
                    if (name.Length == 0) continue;

                    var j = new CommJobConfig
                    {
                        Enabled = Bool(row, 0, true),
                        Name = name,
                        Trigger = Str(row, 2),
                        IntervalMs = Int(row, 3, 1000),
                        Flows = Split(Str(row, 4)),
                        Mode = Str(row, 5),
                        WindowMs = Int(row, 6, 500),
                        Point = Str(row, 7),
                        Expect = Str(row, 8),
                        FlowStride = Int(row, 9, 0),
                        Actions = ParseActions(Str(row, 10))
                    };
                    j.Normalize();
                    jobs.Add(j);
                }

                var bindings = new List<CommFlowBindingConfig>();
                foreach (DataGridViewRow row in _gridBindings.Rows)
                {
                    if (row.IsNewRow) continue;
                    string flow = Str(row, 1);
                    if (flow.Length == 0) continue;

                    var b = new CommFlowBindingConfig
                    {
                        Enabled = Bool(row, 0, true),
                        Flow = flow,
                        Endpoint = Str(row, 2),
                        BusyPoint = Str(row, 3),
                        ReadyPoint = Str(row, 4),
                        FaultPoint = Str(row, 5)
                    };
                    b.Normalize();
                    bindings.Add(b);
                }

                settings.Points = points;
                settings.Messages = messages;
                settings.Jobs = jobs;
                settings.FlowBindings = bindings;

                ConfigStore.Save();
                CommHub.Reload();
                CommJobEngine.Reload();

                // 保存后重载一次：新建的点位/消息/流程立刻出现在下拉里
                LoadFromConfig();
                _status.Text = "已保存：" + DateTime.Now.ToString("HH:mm:ss")
                             + " · 点位 " + points.Count + " · 任务 " + jobs.Count;
                RefreshDiagnostics();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "保存失败：" + ex.Message, "通讯点位与任务",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void RefreshDiagnostics()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== 端点 ===");

            IReadOnlyList<string> names = CommHub.Names();
            foreach (string n in names)
            {
                CommEndpointConfig cfg = CommHub.Config(n);
                sb.AppendLine(string.Format("{0,-20} {1,-12} {2}", n,
                    cfg != null ? cfg.Kind : "?", CommHub.StatusText(n)));
            }

            sb.AppendLine();
            sb.AppendLine("=== 点位 ===");
            foreach (string p in CommHub.PointNames())
            {
                CommPointConfig pc = CommHub.PointConfig(p);
                if (pc == null) continue;
                sb.AppendLine(string.Format("{0,-24} {1,-12} {2,-10} {3}", p, pc.Endpoint, pc.Type, pc.Address));
            }

            sb.AppendLine();
            sb.AppendLine("=== 任务 ===");
            foreach (CommJobEngine.JobStatus s in CommJobEngine.Snapshot())
            {
                sb.AppendLine(string.Format("{0,-24} {1,-10} run={2} last={3:HH:mm:ss} {4}",
                    s.Name, s.Trigger, s.RunCount, s.LastRun,
                    string.IsNullOrEmpty(s.LastError) ? "OK" : ("ERR " + s.LastError)));
            }

            _diagnostics.Text = sb.ToString();
        }

        // =====================================================================
        //  动作串 <-> 配置
        // =====================================================================

        private static string ActionsToString(List<CommActionConfig> actions)
        {
            if (actions == null) return "";
            var parts = new List<string>();

            foreach (CommActionConfig a in actions)
            {
                if (a == null) continue;

                string kind = a.Kind ?? "";
                if (string.Equals(kind, "Sleep", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(kind, "Log", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(kind, "StartFlow", StringComparison.OrdinalIgnoreCase))
                {
                    parts.Add(kind + ":" + (a.Value ?? a.Target ?? ""));
                }
                else if (string.Equals(kind, "SendMessage", StringComparison.OrdinalIgnoreCase))
                {
                    parts.Add(kind + ":" + (a.Target ?? ""));
                }
                else
                {
                    string s = kind + ":" + (a.Target ?? "") + "=" + (a.Value ?? "");
                    if (a.TimeoutMs > 0)
                        s += "@" + a.TimeoutMs.ToString(CultureInfo.InvariantCulture);
                    parts.Add(s);
                }
            }

            return string.Join(";", parts.ToArray());
        }

        private static List<CommActionConfig> ParseActions(string text)
        {
            var list = new List<CommActionConfig>();
            if (string.IsNullOrWhiteSpace(text)) return list;

            string[] parts = text.Split(new[] { ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string raw in parts)
            {
                string s = raw.Trim();
                if (s.Length == 0) continue;

                int colon = s.IndexOf(':');
                string kind = colon > 0 ? s.Substring(0, colon).Trim() : s;
                string rest = colon > 0 ? s.Substring(colon + 1).Trim() : "";

                var a = new CommActionConfig { Kind = kind };

                if (string.Equals(kind, "Sleep", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(kind, "Log", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(kind, "StartFlow", StringComparison.OrdinalIgnoreCase))
                {
                    a.Value = rest;
                }
                else
                {
                    int eq = rest.IndexOf('=');
                    if (eq >= 0)
                    {
                        a.Target = rest.Substring(0, eq).Trim();
                        string v = rest.Substring(eq + 1).Trim();
                        int at = v.LastIndexOf('@');
                        if (at > 0)
                        {
                            int timeout;
                            if (int.TryParse(v.Substring(at + 1), out timeout)) a.TimeoutMs = timeout;
                            v = v.Substring(0, at);
                        }
                        a.Value = v;
                    }
                    else
                    {
                        a.Target = rest;
                    }
                }

                a.Normalize();
                list.Add(a);
            }

            return list;
        }

        // =====================================================================
        //  小工具
        // =====================================================================

        private static string Str(DataGridViewRow row, int index)
        {
            if (row == null || index >= row.Cells.Count) return "";
            object v = row.Cells[index].Value;
            return v == null ? "" : Convert.ToString(v, CultureInfo.InvariantCulture).Trim();
        }

        private static int Int(DataGridViewRow row, int index, int fallback)
        {
            int v;
            return int.TryParse(Str(row, index), NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : fallback;
        }

        private static double Double(DataGridViewRow row, int index, double fallback)
        {
            double v;
            return double.TryParse(Str(row, index), NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : fallback;
        }

        private static bool Bool(DataGridViewRow row, int index, bool fallback)
        {
            if (row == null || index >= row.Cells.Count) return fallback;
            object v = row.Cells[index].Value;
            if (v == null) return fallback;
            if (v is bool) return (bool)v;
            bool b;
            return bool.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), out b) ? b : fallback;
        }

        private static string Join(List<string> values)
        {
            return values == null ? "" : string.Join(";", values.ToArray());
        }

        private static List<string> Split(string text)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(text)) return list;

            foreach (string s in text.Split(new[] { ';', ',', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string v = s.Trim();
                if (v.Length > 0) list.Add(v);
            }
            return list;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { _diagTimer.Stop(); _diagTimer.Dispose(); }
                catch (Exception) { }
            }
            base.Dispose(disposing);
        }
    }
}
