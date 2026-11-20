using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using VisionPlatform.Core;
using VisionPlatform.Communication;

namespace VisionPlatform.UI
{
    /// <summary>任务动作编辑器：下拉选点位/消息/流程，避免手写 <c>SetPoint:xxx=true</c> 格式。</summary>
    public sealed class CommActionEditorForm : Form
    {
        private readonly DataGridView _grid = new DataGridView();
        private readonly List<string> _points;
        private readonly List<string> _messages;
        private readonly List<string> _flows;
        private readonly List<string> _targets = new List<string>();

        /// <summary>确定后的动作列表（按表格顺序）。</summary>
        public List<CommActionConfig> Actions { get; private set; }

        public CommActionEditorForm(List<CommActionConfig> actions,
                                    List<string> points, List<string> messages, List<string> flows)
        {
            _points = points ?? new List<string>();
            _messages = messages ?? new List<string>();
            _flows = flows ?? new List<string>();
            Actions = new List<CommActionConfig>();

            Text = "编辑任务动作";
            ClientSize = new Size(820, 460);
            MinimumSize = new Size(680, 360);
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.Sizable;

            BuildTargets();
            BuildUi();
            LoadActions(actions);

            UiStyle.ApplyForm(this);
            UiGrid.Apply(_grid);
            UiTitleBar.Attach(this);
        }

        private void BuildTargets()
        {
            AddTargets(_points);
            AddTargets(_messages);
            AddTargets(_flows);
        }

        private void AddTargets(List<string> values)
        {
            foreach (string v in values)
            {
                if (string.IsNullOrWhiteSpace(v)) continue;
                if (!_targets.Contains(v)) _targets.Add(v);
            }
        }

        private void BuildUi()
        {
            _grid.Dock = DockStyle.Fill;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.RowHeadersVisible = false;
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            _grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
            _grid.EditMode = DataGridViewEditMode.EditOnEnter;
            _grid.BackgroundColor = UiTheme.ColorPanelBg;
            _grid.BorderStyle = BorderStyle.None;
            _grid.DataError += delegate(object s, DataGridViewDataErrorEventArgs e) { e.ThrowException = false; };

            var kind = new DataGridViewComboBoxColumn();
            kind.HeaderText = "动作";
            kind.Width = 130;
            kind.FlatStyle = FlatStyle.Flat;
            foreach (string k in new[] { "SetPoint", "SendMessage", "WaitPoint", "Sleep", "StartFlow", "Log" })
                kind.Items.Add(k);
            _grid.Columns.Add(kind);

            var target = new DataGridViewComboBoxColumn();
            target.HeaderText = "目标（点位 / 消息 / 流程）";
            target.Width = 300;
            target.FlatStyle = FlatStyle.Flat;
            target.Items.Add("");
            foreach (string t in _targets) target.Items.Add(t);
            _grid.Columns.Add(target);

            var value = new DataGridViewTextBoxColumn();
            value.HeaderText = "值（true / 文本 / 毫秒）";
            value.Width = 200;
            _grid.Columns.Add(value);

            var timeout = new DataGridViewTextBoxColumn();
            timeout.HeaderText = "超时 ms";
            timeout.Width = 90;
            _grid.Columns.Add(timeout);

            _grid.CellValueChanged += OnCellValueChanged;

            var bar = new FlowLayoutPanel();
            bar.Dock = DockStyle.Bottom;
            bar.Height = 48;
            bar.FlowDirection = FlowDirection.LeftToRight;
            bar.Padding = new Padding(8, 8, 8, 8);
            bar.WrapContents = false;

            Button add = NewButton("新增动作", 90);
            add.Click += delegate { AddRow(DefaultAction()); };

            Button del = NewButton("删除", 70);
            del.Click += delegate { DeleteCurrent(); };

            Button up = NewButton("上移", 70);
            up.Click += delegate { MoveCurrent(-1); };

            Button down = NewButton("下移", 70);
            down.Click += delegate { MoveCurrent(1); };

            Button ok = NewButton("确定", 80);
            ok.Click += delegate { if (Save()) { DialogResult = DialogResult.OK; Close(); } };

            Button cancel = NewButton("取消", 80);
            cancel.DialogResult = DialogResult.Cancel;

            bar.Controls.Add(add);
            bar.Controls.Add(del);
            bar.Controls.Add(up);
            bar.Controls.Add(down);
            bar.Controls.Add(ok);
            bar.Controls.Add(cancel);

            Controls.Add(_grid);
            Controls.Add(bar);
            AcceptButton = ok;
            CancelButton = cancel;
        }

        private void LoadActions(List<CommActionConfig> actions)
        {
            if (actions != null)
            {
                foreach (CommActionConfig a in actions)
                {
                    if (a == null) continue;
                    AddRow(new object[]
                    {
                        string.IsNullOrEmpty(a.Kind) ? "SetPoint" : a.Kind,
                        a.Target ?? "",
                        a.Value ?? "",
                        a.TimeoutMs > 0 ? a.TimeoutMs.ToString(CultureInfo.InvariantCulture) : "1000"
                    });
                }
            }

            if (_grid.Rows.Count == 0) AddRow(DefaultAction());
        }

        private object[] DefaultAction()
        {
            string target = _points.Count > 0 ? _points[0] : "";
            return new object[] { "SetPoint", target, "true", "1000" };
        }

        private void AddRow(object[] values)
        {
            int index = _grid.Rows.Add(values);
            if (index >= 0) _grid.CurrentCell = _grid.Rows[index].Cells[0];
        }

        private void DeleteCurrent()
        {
            if (_grid.CurrentCell == null) return;
            int index = _grid.CurrentCell.RowIndex;
            if (index >= 0 && index < _grid.Rows.Count) _grid.Rows.RemoveAt(index);
        }

        private void MoveCurrent(int delta)
        {
            if (_grid.CurrentCell == null) return;
            int index = _grid.CurrentCell.RowIndex;
            int target = index + delta;
            if (index < 0 || target < 0 || target >= _grid.Rows.Count) return;

            object[] values = new object[_grid.Columns.Count];
            for (int c = 0; c < _grid.Columns.Count; c++)
                values[c] = _grid.Rows[index].Cells[c].Value;

            _grid.Rows.RemoveAt(index);
            _grid.Rows.Insert(target, values);
            _grid.CurrentCell = _grid.Rows[target].Cells[0];
        }

        private void OnCellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != 0) return;

            DataGridViewRow row = _grid.Rows[e.RowIndex];
            string kind = Convert.ToString(row.Cells[0].Value, CultureInfo.InvariantCulture);
            string target = Convert.ToString(row.Cells[1].Value, CultureInfo.InvariantCulture);

            if (string.Equals(kind, "Sleep", StringComparison.OrdinalIgnoreCase))
            {
                row.Cells[1].Value = "";
                if (string.IsNullOrEmpty(Convert.ToString(row.Cells[2].Value, CultureInfo.InvariantCulture)))
                    row.Cells[2].Value = "1000";
                return;
            }

            if (string.Equals(kind, "Log", StringComparison.OrdinalIgnoreCase))
            {
                row.Cells[1].Value = "";
                return;
            }

            if (!string.IsNullOrEmpty(target)) return;

            string first = FirstTargetFor(kind);
            if (!string.IsNullOrEmpty(first)) row.Cells[1].Value = first;

            string value = Convert.ToString(row.Cells[2].Value, CultureInfo.InvariantCulture);
            if (string.IsNullOrEmpty(value)
                && (string.Equals(kind, "SetPoint", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(kind, "WaitPoint", StringComparison.OrdinalIgnoreCase)))
                row.Cells[2].Value = "true";
        }

        private string FirstTargetFor(string kind)
        {
            if (string.Equals(kind, "SendMessage", StringComparison.OrdinalIgnoreCase))
                return _messages.Count > 0 ? _messages[0] : "";

            if (string.Equals(kind, "StartFlow", StringComparison.OrdinalIgnoreCase))
                return _flows.Count > 0 ? _flows[0] : "";

            return _points.Count > 0 ? _points[0] : "";
        }

        private bool Save()
        {
            Actions = new List<CommActionConfig>();

            foreach (DataGridViewRow row in _grid.Rows)
            {
                string kind = Cell(row, 0);
                if (kind.Length == 0) continue;

                var a = new CommActionConfig
                {
                    Kind = kind,
                    Target = Cell(row, 1),
                    Value = Cell(row, 2),
                    TimeoutMs = IntCell(row, 3, 1000)
                };

                if (string.Equals(kind, "Sleep", StringComparison.OrdinalIgnoreCase))
                {
                    // Sleep 的时长放 Value；Timeout 列只是界面占位
                    if (a.Value.Length == 0 && a.TimeoutMs > 0)
                        a.Value = a.TimeoutMs.ToString(CultureInfo.InvariantCulture);
                }

                a.Normalize();

                if (string.Equals(kind, "SetPoint", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(kind, "WaitPoint", StringComparison.OrdinalIgnoreCase))
                {
                    if (a.Target.Length == 0 || !_points.Contains(a.Target))
                    {
                        MessageBox.Show(this, "动作「" + kind + "」需要选择一个点位。", "动作不完整",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return false;
                    }
                }
                else if (string.Equals(kind, "SendMessage", StringComparison.OrdinalIgnoreCase))
                {
                    if (a.Target.Length == 0 || !_messages.Contains(a.Target))
                    {
                        MessageBox.Show(this, "动作「SendMessage」需要选择一条消息模板。", "动作不完整",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return false;
                    }
                }
                else if (string.Equals(kind, "StartFlow", StringComparison.OrdinalIgnoreCase))
                {
                    if (a.Target.Length == 0 || !_flows.Contains(a.Target))
                    {
                        MessageBox.Show(this, "动作「StartFlow」需要选择一条流程。", "动作不完整",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return false;
                    }
                }

                Actions.Add(a);
            }

            return true;
        }

        private static string Cell(DataGridViewRow row, int index)
        {
            if (row == null || index >= row.Cells.Count) return "";
            object v = row.Cells[index].Value;
            return v == null ? "" : Convert.ToString(v, CultureInfo.InvariantCulture).Trim();
        }

        private static int IntCell(DataGridViewRow row, int index, int fallback)
        {
            int v;
            return int.TryParse(Cell(row, index), NumberStyles.Integer, CultureInfo.InvariantCulture, out v)
                ? v : fallback;
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
    }
}
