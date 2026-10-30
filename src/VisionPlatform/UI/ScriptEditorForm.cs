using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;
using VisionPlatform.Flow;

namespace VisionPlatform.UI
{
    /// <summary>
    /// 脚本编辑器（Notepad++ 风格的三栏布局）。
    /// <para>
    /// 左上：上游节点及其输出（运行过一次就显示真实值，没跑过显示字段说明）；
    /// 左下：脚本的输出变量（名称 + 类型），作为本节点的对外输出；
    /// 右侧：只读的"头文件 + 函数签名"，下面是可编辑的方法体。
    /// </para>
    /// </summary>
    public sealed class ScriptEditorForm : Form
    {
        private readonly FlowNode _node;

        private TreeView treeUpstream;
        private TextBox txtBody;
        private ListView lstOutputs;
        private TextBox txtOutName;
        private ComboBox cmbOutType;
        private Label lblStatus;
        private SplitContainer splitMain;
        private SplitContainer splitLeft;
        private SplitContainer splitRight;
        private TextBox txtResult;
        private ListView lstResultVals;

        private static readonly string[] OutTypes = { "double", "int", "string", "bool" };

        /// <summary>上游节点快照（可为空：没跑过就只有节点清单）。</summary>
        private readonly List<NodeOutput> _upstream = new List<NodeOutput>();

        /// <summary>流程里排在脚本之前的节点（没有运行数据时也要能列出名字）。</summary>
        private readonly List<string> _upstreamNames = new List<string>();

        public ScriptEditorForm(FlowNode node, List<NodeOutput> upstream, List<string> upstreamNames)
        {
            _node = node;
            if (upstream != null) _upstream.AddRange(upstream);
            if (upstreamNames != null) _upstreamNames.AddRange(upstreamNames);

            BuildUi();
            LoadFromNode();

            UiStyle.ApplyForm(this);

            // 主题会把所有 TextBox 统一成 9pt 雅黑；代码区必须是等宽字体，否则缩进一塌糊涂
            txtBody.Font = new Font("Consolas", 10f);
            txtBody.BackColor = UiTheme.ColorViewBg;
            txtBody.ForeColor = UiTheme.ColorTextPrimary;
            txtResult.Font = new Font("Consolas", 9.5f);
            txtResult.BackColor = UiTheme.ColorViewBg;
        }

        // =====================================================================
        //  界面
        // =====================================================================

        private void BuildUi()
        {
            Text = "脚本编辑器";
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(1180, 720);
            MinimumSize = new Size(900, 560);
            BackColor = UiTheme.ColorPanelBg;
            ForeColor = UiTheme.ColorTextPrimary;
            Font = UiTheme.FontBody;

            splitMain = new SplitContainer();
            splitMain.Dock = DockStyle.Fill;
            splitMain.Orientation = Orientation.Vertical;
            // 约束先放宽：SplitContainer 在窗体还没尺寸时会用默认 SplitterDistance 布局，
            // 这时若 MinSize 已经很大，会直接抛 "SplitterDistance 必须..."。
            splitMain.Panel1MinSize = 25;
            splitMain.Panel2MinSize = 25;
            splitMain.BackColor = UiTheme.ColorDivider;

            // ---------------- 左侧 ----------------
            splitLeft = new SplitContainer();
            splitLeft.Dock = DockStyle.Fill;
            splitLeft.Orientation = Orientation.Horizontal;
            splitLeft.Panel1MinSize = 25;
            splitLeft.Panel2MinSize = 25;
            splitLeft.BackColor = UiTheme.ColorDivider;

            // 左上：上游输出
            var grpUp = new GroupBox();
            grpUp.Text = "上游节点的输出（本节点之前执行的全部节点）";
            grpUp.Dock = DockStyle.Fill;
            grpUp.ForeColor = UiTheme.ColorTextSecondary;
            grpUp.Padding = new Padding(8, 6, 8, 8);

            treeUpstream = new TreeView();
            treeUpstream.Dock = DockStyle.Fill;
            treeUpstream.BorderStyle = BorderStyle.None;
            treeUpstream.BackColor = UiTheme.ColorPanelBg;
            treeUpstream.ForeColor = UiTheme.ColorTextPrimary;
            treeUpstream.Font = UiTheme.FontSmall;
            treeUpstream.HideSelection = false;
            grpUp.Controls.Add(treeUpstream);

            // 左下：输出变量
            var grpOut = new GroupBox();
            grpOut.Text = "本脚本的输出变量（下游节点可按名字读取）";
            grpOut.Dock = DockStyle.Fill;
            grpOut.ForeColor = UiTheme.ColorTextSecondary;
            grpOut.Padding = new Padding(8, 6, 8, 8);

            var panelAdd = new Panel();
            panelAdd.Dock = DockStyle.Top;
            panelAdd.Height = 32;

            txtOutName = new TextBox();
            txtOutName.Width = 150;
            txtOutName.Location = new Point(0, 4);
            txtOutName.Text = "Score";

            cmbOutType = new ComboBox();
            cmbOutType.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbOutType.Items.AddRange(OutTypes);
            cmbOutType.SelectedIndex = 0;
            cmbOutType.Width = 96;
            cmbOutType.Location = new Point(158, 4);

            var btnAdd = new Button();
            btnAdd.Text = "添加";
            btnAdd.Width = 60;
            btnAdd.Location = new Point(262, 3);
            btnAdd.Click += OnAddOutput;

            var btnDel = new Button();
            btnDel.Text = "删除";
            btnDel.Width = 60;
            btnDel.Location = new Point(328, 3);
            btnDel.Click += OnDeleteOutput;

            panelAdd.Controls.Add(txtOutName);
            panelAdd.Controls.Add(cmbOutType);
            panelAdd.Controls.Add(btnAdd);
            panelAdd.Controls.Add(btnDel);

            lstOutputs = new ListView();
            lstOutputs.Dock = DockStyle.Fill;
            lstOutputs.View = View.Details;
            lstOutputs.FullRowSelect = true;
            lstOutputs.HideSelection = false;
            lstOutputs.BorderStyle = BorderStyle.None;
            lstOutputs.BackColor = UiTheme.ColorPanelBg;
            lstOutputs.ForeColor = UiTheme.ColorTextPrimary;
            lstOutputs.Font = UiTheme.FontSmall;
            lstOutputs.Columns.Add("名称", 180);
            lstOutputs.Columns.Add("类型", 90);

            grpOut.Controls.Add(lstOutputs);
            grpOut.Controls.Add(panelAdd);

            splitLeft.Panel1.Controls.Add(grpUp);
            splitLeft.Panel2.Controls.Add(grpOut);

            // ---------------- 右侧 ----------------
            var panelRight = new Panel();
            panelRight.Dock = DockStyle.Fill;

            // 只留两行（类 + 函数签名）：占太多会把代码区挤没，用户会以为"不能写代码"
            var lblHead = new Label();
            lblHead.Dock = DockStyle.Top;
            lblHead.Height = 44;
            lblHead.AutoSize = false;
            lblHead.Font = new Font("Consolas", 9.5f);
            lblHead.ForeColor = UiTheme.ColorTextSecondary;
            lblHead.BackColor = UiTheme.ColorHeaderBg;
            lblHead.Padding = new Padding(10, 4, 10, 2);
            lblHead.Text =
                "public class ScriptNode\r\n" +
                "    public bool Run(ScriptApi api)   // 在下面的大括号里写代码\r\n" +
                "    {";

            // 「结果」区：运行后在编辑器里直接看输出（不用切回流程页）
            var panelResult = new Panel();
            panelResult.Dock = DockStyle.Fill;
            panelResult.BackColor = UiTheme.ColorPanelBg;

            var lblResult = new Label();
            lblResult.Dock = DockStyle.Top;
            lblResult.Height = 24;
            lblResult.Text = "运行结果";
            lblResult.ForeColor = UiTheme.ColorTextSecondary;
            lblResult.BackColor = UiTheme.ColorHeaderBg;
            lblResult.Padding = new Padding(8, 4, 8, 0);

            lstResultVals = new ListView();
            lstResultVals.Dock = DockStyle.Bottom;
            lstResultVals.Height = 120;
            lstResultVals.View = View.Details;
            lstResultVals.FullRowSelect = true;
            lstResultVals.BorderStyle = BorderStyle.None;
            lstResultVals.BackColor = UiTheme.ColorPanelBg;
            lstResultVals.ForeColor = UiTheme.ColorTextPrimary;
            lstResultVals.Font = UiTheme.FontSmall;
            lstResultVals.Columns.Add("输出名", 150);
            lstResultVals.Columns.Add("值", 260);

            txtResult = new TextBox();
            txtResult.Dock = DockStyle.Fill;
            txtResult.Multiline = true;
            txtResult.ReadOnly = true;
            txtResult.ScrollBars = ScrollBars.Vertical;
            txtResult.BorderStyle = BorderStyle.None;
            txtResult.Font = new Font("Consolas", 9.5f);
            txtResult.BackColor = UiTheme.ColorViewBg;
            txtResult.ForeColor = UiTheme.ColorTextPrimary;

            panelResult.Controls.Add(txtResult);
            panelResult.Controls.Add(lstResultVals);
            panelResult.Controls.Add(lblResult);

            splitRight = new SplitContainer();
            splitRight.Dock = DockStyle.Fill;
            splitRight.Orientation = Orientation.Horizontal;
            splitRight.Panel1MinSize = 25;
            splitRight.Panel2MinSize = 25;
            splitRight.BackColor = UiTheme.ColorDivider;
            var lblTail = new Label();
            lblTail.Dock = DockStyle.Bottom;
            lblTail.Height = 18;
            lblTail.AutoSize = false;
            lblTail.Font = new Font("Consolas", 9.5f);
            lblTail.ForeColor = UiTheme.ColorTextSecondary;
            lblTail.BackColor = UiTheme.ColorHeaderBg;
            lblTail.Padding = new Padding(10, 0, 10, 0);
            lblTail.Text = "    }";

            splitRight.Panel1.Controls.Add(txtBody);
            splitRight.Panel1.Controls.Add(lblTail);
            splitRight.Panel1.Controls.Add(lblHead);
            splitRight.Panel2.Controls.Add(panelResult);

            txtBody = new TextBox();
            txtBody.Dock = DockStyle.Fill;
            txtBody.Multiline = true;
            txtBody.AcceptsReturn = true;
            txtBody.AcceptsTab = true;
            txtBody.ScrollBars = ScrollBars.Both;
            txtBody.WordWrap = false;
            txtBody.Font = new Font("Consolas", 10f);
            txtBody.BackColor = UiTheme.ColorViewBg;
            txtBody.ForeColor = UiTheme.ColorTextPrimary;
            txtBody.BorderStyle = BorderStyle.None;

            panelRight.Controls.Add(splitRight);

            splitMain.Panel1.Controls.Add(splitLeft);
            splitMain.Panel2.Controls.Add(panelRight);

            // ---------------- 底部按钮 ----------------
            var panelBottom = new Panel();
            panelBottom.Dock = DockStyle.Bottom;
            panelBottom.Height = 44;

            lblStatus = new Label();
            lblStatus.Dock = DockStyle.Fill;
            lblStatus.TextAlign = ContentAlignment.MiddleLeft;
            lblStatus.ForeColor = UiTheme.ColorTextSecondary;
            lblStatus.Padding = new Padding(8, 0, 0, 0);
            lblStatus.Text = "api.Nodes / api.Get(名称) / api.First(类型) · 输出用 api.Set(\"名字\", 值) · 返回 false 表示 NG";

            var btnRun = new Button();
            btnRun.Text = "运行";
            btnRun.Dock = DockStyle.Right;
            btnRun.Width = 96;
            btnRun.Click += OnRunScript;

            var btnOk = new Button();
            btnOk.Text = "保存";
            btnOk.Dock = DockStyle.Right;
            btnOk.Width = 96;
            btnOk.Click += OnSave;

            var btnCancel = new Button();
            btnCancel.Text = "取消";
            btnCancel.Dock = DockStyle.Right;
            btnCancel.Width = 96;
            btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

            panelBottom.Controls.Add(lblStatus);
            panelBottom.Controls.Add(btnRun);
            panelBottom.Controls.Add(btnOk);
            panelBottom.Controls.Add(btnCancel);

            Controls.Add(splitMain);
            Controls.Add(panelBottom);
        }

        // =====================================================================
        //  数据
        // =====================================================================

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);

            // 打开就把光标放进代码区，用户不用先猜"哪里能写"
            try { txtBody.Focus(); txtBody.SelectionStart = txtBody.TextLength; } catch (Exception) { }

            // 尺寸确定后再定分割位置与最小宽高（构造期设会越界抛异常）
            try
            {
                splitMain.Panel1MinSize = 220;
                splitMain.Panel2MinSize = 320;
                splitMain.SplitterDistance = 380;
            }
            catch (Exception) { }

            try
            {
                splitLeft.Panel1MinSize = 120;
                splitLeft.Panel2MinSize = 120;
                splitLeft.SplitterDistance = Math.Max(180, splitLeft.Height / 2);
            }
            catch (Exception) { }

            try
            {
                splitRight.Panel1MinSize = 160;
                splitRight.Panel2MinSize = 120;
                splitRight.SplitterDistance = Math.Max(200, (int)(splitRight.Height * 0.62));
            }
            catch (Exception) { }
        }

        private void LoadFromNode()
        {
            txtBody.Text = _node != null ? _node.GetString("Source", "") : "";
            if (string.IsNullOrWhiteSpace(txtBody.Text)) txtBody.Text = ScriptEngine.SampleScript;

            lstOutputs.Items.Clear();
            foreach (KeyValuePair<string, string> kv in ParseOutputs(_node != null ? _node.GetString("OutDefs", "") : ""))
            {
                var item = new ListViewItem(kv.Key);
                item.SubItems.Add(kv.Value);
                lstOutputs.Items.Add(item);
            }

            BuildUpstreamTree();
        }

        private void BuildUpstreamTree()
        {
            treeUpstream.BeginUpdate();
            treeUpstream.Nodes.Clear();

            if (_upstream.Count == 0)
            {
                // 还没运行过：列出"本节点之前有哪些节点"，最后补一句提示
                foreach (string name in _upstreamNames)
                    treeUpstream.Nodes.Add(new TreeNode(name) { ForeColor = UiTheme.ColorTextSecondary });

                var hint = new TreeNode("运行一次流程后，这里会显示每个节点的实际输出值")
                {
                    ForeColor = UiTheme.ColorTextSecondary
                };
                treeUpstream.Nodes.Add(hint);

                treeUpstream.EndUpdate();
                return;
            }

            foreach (NodeOutput o in _upstream)
            {
                var root = new TreeNode(o.Name + "  [" + o.Type + "]  " + o.Status);

                // 按"类型 + 键名 + 格式化值"展示：角度带 °、坐标带括号、判定 OK/NG
                foreach (PortValue v in o.Values)
                {
                    if (v == null) continue;
                    root.Nodes.Add(string.Format(CultureInfo.InvariantCulture,
                        "{0}（{1}） = {2}　→ api.{3}(\"{4}\", \"{5}\")",
                        v.DisplayName, v.KindName, v.Format(),
                        v.Kind == FlowPortKind.Bool ? "Flag" : "Num", o.Name, v.Key));
                }

                if (!string.IsNullOrEmpty(o.Summary)) root.Nodes.Add("摘要 = " + o.Summary);

                treeUpstream.Nodes.Add(root);
            }

            treeUpstream.ExpandAll();
            treeUpstream.EndUpdate();
        }

        private static List<KeyValuePair<string, string>> ParseOutputs(string raw)
        {
            var list = new List<KeyValuePair<string, string>>();
            if (string.IsNullOrWhiteSpace(raw)) return list;

            foreach (string part in raw.Split(new[] { ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string s = part.Trim();
                if (s.Length == 0) continue;

                string name = s, type = "double";
                int i = s.IndexOf(':');
                if (i > 0)
                {
                    name = s.Substring(0, i).Trim();
                    type = s.Substring(i + 1).Trim();
                }
                if (name.Length == 0) continue;
                list.Add(new KeyValuePair<string, string>(name, type));
            }
            return list;
        }

        private static string JoinOutputs(IEnumerable<KeyValuePair<string, string>> items)
        {
            var sb = new StringBuilder();
            foreach (KeyValuePair<string, string> kv in items)
            {
                if (sb.Length > 0) sb.Append(';');
                sb.Append(kv.Key).Append(':').Append(kv.Value);
            }
            return sb.ToString();
        }

        private void OnAddOutput(object sender, EventArgs e)
        {
            string name = (txtOutName.Text ?? "").Trim();
            if (name.Length == 0) { MessageBox.Show(this, "请输入输出变量名。"); return; }

            foreach (ListViewItem it in lstOutputs.Items)
            {
                if (string.Equals(it.Text, name, StringComparison.Ordinal))
                {
                    MessageBox.Show(this, "已存在同名输出：" + name);
                    return;
                }
            }

            var item = new ListViewItem(name);
            item.SubItems.Add(cmbOutType.SelectedItem as string ?? "double");
            lstOutputs.Items.Add(item);
            txtOutName.Focus();
            txtOutName.SelectAll();
        }

        private void OnDeleteOutput(object sender, EventArgs e)
        {
            if (lstOutputs.SelectedItems.Count == 0) return;
            lstOutputs.Items.Remove(lstOutputs.SelectedItems[0]);
        }

        /// <summary>
        /// 在编辑器里直接跑当前脚本：用"最近一次运行的上游输出"构造 api，
        /// 结果写进下方结果区（文本 + 命名输出表），不用切回流程页。
        /// </summary>
        private void OnRunScript(object sender, EventArgs e)
        {
            lstResultVals.Items.Clear();
            txtResult.Text = "";

            var api = new ScriptApi(new List<NodeOutput>(_upstream), new Dictionary<string, object>(StringComparer.Ordinal),
                                    0, 0);

            string error;
            bool ok = ScriptEngine.Run(txtBody.Text ?? "", api, out error);

            if (!string.IsNullOrEmpty(error))
            {
                txtResult.Text = "运行失败：\r\n" + error;
                txtResult.ForeColor = UiTheme.ColorNg;
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("返回值：" + (ok ? "true（OK）" : "false（NG）"));
            if (!string.IsNullOrEmpty(api.Text)) sb.AppendLine("结果文本：" + api.Text);
            if (Math.Abs(api.Number) > 1e-12)
                sb.AppendLine("数值：" + api.Number.ToString("0.####", CultureInfo.InvariantCulture));
            sb.AppendLine("上游节点：" + api.Nodes.Count + " 个");
            sb.AppendLine();
            sb.AppendLine("提示：这里用的是最近一次运行的上游数据；真要刷新数据请先回流程页运行一次。");

            txtResult.Text = sb.ToString();
            txtResult.ForeColor = UiTheme.ColorTextPrimary;

            foreach (string name in api.SetNames)
            {
                object v = api.GetValue(name);
                var item = new ListViewItem(name);
                item.SubItems.Add(v == null ? "" : Convert.ToString(v, CultureInfo.InvariantCulture));
                lstResultVals.Items.Add(item);
            }
        }

        private void OnSave(object sender, EventArgs e)
        {
            if (_node != null)
            {
                _node.Set("Source", txtBody.Text ?? "");

                var items = new List<KeyValuePair<string, string>>();
                foreach (ListViewItem it in lstOutputs.Items)
                {
                    items.Add(new KeyValuePair<string, string>(
                        it.Text, it.SubItems.Count > 1 ? it.SubItems[1].Text : "double"));
                }
                _node.Set("OutDefs", JoinOutputs(items));
            }

            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
