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
    /// 脚本编辑器（参考 VisionMaster 脚本窗：左侧变量、右侧全高代码区、底部预编译 / 运行 / 确定）。
    /// <para>
    /// 【为什么不再用 SplitContainer】SplitContainer 在窗体还没尺寸时设置
    /// <c>SplitterDistance</c> / <c>PanelMinSize</c> 会抛异常；上一版被 try/catch 吞掉后
    /// 停在默认 50px，代码区只剩一条缝 —— 用户看到的是"只读签名 + 一条黑边"，自然说"不能编辑"。
    /// 这里改成纯 Dock 布局，尺寸由窗体自己算，不再有这个问题。
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
        private TextBox txtResult;
        private ListView lstResultVals;

        private static readonly string[] OutTypes = { "double", "int", "string", "bool" };

        private readonly List<NodeOutput> _upstream = new List<NodeOutput>();
        private readonly List<string> _upstreamNames = new List<string>();

        public ScriptEditorForm(FlowNode node, List<NodeOutput> upstream, List<string> upstreamNames)
        {
            _node = node;
            if (upstream != null) _upstream.AddRange(upstream);
            if (upstreamNames != null) _upstreamNames.AddRange(upstreamNames);

            BuildUi();
            LoadFromNode();

            UiStyle.ApplyForm(this);

            // 主题会把所有 TextBox 统一成 9pt 雅黑；代码区必须等宽，否则缩进一塌糊涂
            txtBody.Font = new Font("Consolas", 10f);
            txtBody.BackColor = UiTheme.ColorViewBg;
            txtBody.ForeColor = UiTheme.ColorTextPrimary;
            txtResult.Font = new Font("Consolas", 9.5f);
            txtResult.BackColor = UiTheme.ColorViewBg;
        }

        // =====================================================================
        //  界面（纯 Dock，无 SplitContainer）
        // =====================================================================

        private void BuildUi()
        {
            Text = "脚本编辑器";
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(1100, 700);
            MinimumSize = new Size(860, 520);
            BackColor = UiTheme.ColorPanelBg;
            ForeColor = UiTheme.ColorTextPrimary;
            Font = UiTheme.FontBody;

            // ---------- 底部按钮 ----------
            var panelBottom = new Panel { Dock = DockStyle.Bottom, Height = 46 };

            lblStatus = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = UiTheme.ColorTextSecondary,
                Padding = new Padding(8, 0, 0, 0),
                Text = "返回 false = 本节点 NG；api.Set(\"名称\", 值) 就是本节点的输出"
            };

            var btnOk = new Button { Text = "确定", Dock = DockStyle.Right, Width = 92 };
            btnOk.Click += OnSave;

            var btnCancel = new Button { Text = "取消", Dock = DockStyle.Right, Width = 92 };
            btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

            var btnRun = new Button { Text = "运行", Dock = DockStyle.Right, Width = 92 };
            btnRun.Click += OnRunScript;

            var btnCheck = new Button { Text = "预编译", Dock = DockStyle.Right, Width = 92 };
            btnCheck.Click += OnCompileOnly;

            panelBottom.Controls.Add(lblStatus);
            panelBottom.Controls.Add(btnCheck);
            panelBottom.Controls.Add(btnRun);
            panelBottom.Controls.Add(btnCancel);
            panelBottom.Controls.Add(btnOk);

            // ---------- 左栏：输入变量（上） + 输出变量（下） ----------
            var panelLeft = new Panel { Dock = DockStyle.Left, Width = 300, Padding = new Padding(6) };

            var grpOut = new GroupBox
            {
                Text = "输出变量（本节点的输出）",
                Dock = DockStyle.Bottom,
                Height = 240,
                ForeColor = UiTheme.ColorTextSecondary,
                Padding = new Padding(8, 6, 8, 8)
            };

            var panelAdd = new Panel { Dock = DockStyle.Top, Height = 34 };
            txtOutName = new TextBox { Width = 96, Location = new Point(0, 5), Text = "Score" };
            cmbOutType = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 66,
                Location = new Point(100, 5)
            };
            cmbOutType.Items.AddRange(OutTypes);
            cmbOutType.SelectedIndex = 0;

            var btnAdd = new Button { Text = "添加", Width = 50, Location = new Point(170, 4) };
            btnAdd.Click += OnAddOutput;
            var btnDel = new Button { Text = "删除", Width = 50, Location = new Point(224, 4) };
            btnDel.Click += OnDeleteOutput;

            panelAdd.Controls.Add(txtOutName);
            panelAdd.Controls.Add(cmbOutType);
            panelAdd.Controls.Add(btnAdd);
            panelAdd.Controls.Add(btnDel);

            lstOutputs = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = UiTheme.ColorPanelBg,
                ForeColor = UiTheme.ColorTextPrimary,
                Font = UiTheme.FontSmall
            };
            lstOutputs.Columns.Add("名称", 144);
            lstOutputs.Columns.Add("类型", 76);

            grpOut.Controls.Add(lstOutputs);
            grpOut.Controls.Add(panelAdd);

            var grpUp = new GroupBox
            {
                Text = "输入变量（上游节点输出，双击插入代码）",
                Dock = DockStyle.Fill,
                ForeColor = UiTheme.ColorTextSecondary,
                Padding = new Padding(8, 6, 8, 8)
            };

            treeUpstream = new TreeView
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = UiTheme.ColorPanelBg,
                ForeColor = UiTheme.ColorTextPrimary,
                Font = UiTheme.FontSmall,
                HideSelection = false
            };
            treeUpstream.DoubleClick += OnUpstreamDoubleClick;
            grpUp.Controls.Add(treeUpstream);

            panelLeft.Controls.Add(grpUp);
            panelLeft.Controls.Add(grpOut);

            // ---------- 右栏：签名(Top) / 结果(Bottom) / 代码(Fill) ----------
            var panelRight = new Panel { Dock = DockStyle.Fill };

            var panelResult = new Panel { Dock = DockStyle.Bottom, Height = 180 };

            var lblResultTitle = new Label
            {
                Dock = DockStyle.Top,
                Height = 22,
                Text = "运行结果",
                ForeColor = UiTheme.ColorTextSecondary,
                BackColor = UiTheme.ColorHeaderBg,
                Padding = new Padding(8, 3, 8, 0)
            };

            lstResultVals = new ListView
            {
                Dock = DockStyle.Bottom,
                Height = 70,
                View = View.Details,
                FullRowSelect = true,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = UiTheme.ColorPanelBg,
                ForeColor = UiTheme.ColorTextPrimary,
                Font = UiTheme.FontSmall
            };
            lstResultVals.Columns.Add("输出名", 140);
            lstResultVals.Columns.Add("值", 260);

            txtResult = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 9.5f),
                BackColor = UiTheme.ColorViewBg,
                ForeColor = UiTheme.ColorTextPrimary
            };

            panelResult.Controls.Add(txtResult);
            panelResult.Controls.Add(lstResultVals);
            panelResult.Controls.Add(lblResultTitle);

            var lblHead = new Label
            {
                Dock = DockStyle.Top,
                Height = 40,
                AutoSize = false,
                Font = new Font("Consolas", 9.5f),
                ForeColor = UiTheme.ColorTextSecondary,
                BackColor = UiTheme.ColorHeaderBg,
                Padding = new Padding(10, 3, 10, 2),
                Text = "public class ScriptNode\r\n    public bool Run(ScriptApi api)   // 下面的大括号里写代码\r\n    {"
            };

            txtBody = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                WordWrap = false,
                AcceptsReturn = true,
                AcceptsTab = true,
                ScrollBars = ScrollBars.Both,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 10f),
                BackColor = UiTheme.ColorViewBg,
                ForeColor = UiTheme.ColorTextPrimary,
                ReadOnly = false,
                Enabled = true
            };

            var lblTail = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 20,
                AutoSize = false,
                Font = new Font("Consolas", 9.5f),
                ForeColor = UiTheme.ColorTextSecondary,
                BackColor = UiTheme.ColorHeaderBg,
                Padding = new Padding(10, 0, 10, 0),
                Text = "    }"
            };

            // Dock 处理顺序 = 集合倒序：先加的 Fill 最后停靠，正好吃满剩余空间
            panelRight.Controls.Add(txtBody);
            panelRight.Controls.Add(lblTail);
            panelRight.Controls.Add(lblHead);
            panelRight.Controls.Add(panelResult);

            Controls.Add(panelRight);
            Controls.Add(panelLeft);
            Controls.Add(panelBottom);
        }

        /// <summary>双击左侧的输入变量 → 把取值语句插入代码区（少打字、少打错）。</summary>
        private void OnUpstreamDoubleClick(object sender, EventArgs e)
        {
            TreeNode n = treeUpstream.SelectedNode;
            if (n == null || n.Tag == null) return;

            string snippet = n.Tag as string;
            if (string.IsNullOrEmpty(snippet)) return;

            int pos = txtBody.SelectionStart;
            txtBody.Text = txtBody.Text.Insert(pos, snippet);
            txtBody.SelectionStart = pos + snippet.Length;
            txtBody.Focus();
        }

        // =====================================================================
        //  数据
        // =====================================================================

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
                foreach (string name in _upstreamNames)
                    treeUpstream.Nodes.Add(new TreeNode(name) { ForeColor = UiTheme.ColorTextSecondary });

                treeUpstream.Nodes.Add(new TreeNode("运行一次流程后，这里会显示每个节点的实际输出值")
                {
                    ForeColor = UiTheme.ColorTextSecondary
                });

                treeUpstream.EndUpdate();
                return;
            }

            foreach (NodeOutput o in _upstream)
            {
                var root = new TreeNode(o.Name + "  [" + o.Type + "]");

                foreach (PortValue v in o.Values)
                {
                    if (v == null) continue;

                    string code;
                    if (v.Kind == FlowPortKind.Bool)
                        code = "api.Flag(\"" + o.Name + "\", \"" + v.Key + "\", false)";
                    else if (v.Kind == FlowPortKind.Point)
                        code = "api.X(\"" + o.Name + "\", \"" + v.Key + "\", 0)";
                    else if (v.Kind == FlowPortKind.Text)
                        code = "api.Get(\"" + o.Name + "\", \"" + v.Key + "\").Format()";
                    else
                        code = "api.Num(\"" + o.Name + "\", \"" + v.Key + "\", 0)";

                    root.Nodes.Add(new TreeNode(string.Format(CultureInfo.InvariantCulture,
                        "{0}（{1}）= {2}", v.DisplayName, v.KindName, v.Format())) { Tag = code });
                }

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

        // =====================================================================
        //  编译 / 运行
        // =====================================================================

        private ScriptApi CreateApi()
        {
            return new ScriptApi(new List<NodeOutput>(_upstream),
                                 new Dictionary<string, object>(StringComparer.Ordinal), 0, 0);
        }

        private void OnCompileOnly(object sender, EventArgs e)
        {
            string error;
            ScriptEngine.Run(txtBody.Text ?? "", CreateApi(), out error);

            if (string.IsNullOrEmpty(error))
            {
                txtResult.ForeColor = UiTheme.ColorOk;
                txtResult.Text = "编译通过。";
            }
            else
            {
                txtResult.ForeColor = UiTheme.ColorNg;
                txtResult.Text = "编译失败：\r\n" + error;
            }
        }

        private void OnRunScript(object sender, EventArgs e)
        {
            lstResultVals.Items.Clear();

            var api = CreateApi();
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

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);

            // 打开就把光标放进代码区，用户不用先猜"哪里能写"
            try
            {
                txtBody.Focus();
                txtBody.SelectionStart = txtBody.TextLength;
                txtBody.ScrollToCaret();
            }
            catch (Exception) { }
        }
    }
}
