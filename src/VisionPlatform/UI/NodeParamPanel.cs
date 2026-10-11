using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using VisionPlatform.Flow;

namespace VisionPlatform.UI
{
    /// <summary>
    /// 通用节点参数面板（UserControl）：按 <see cref="NodeParamTable"/> 动态生成控件，
    /// 读写 <see cref="FlowNode.Params"/>。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>12 类节点共用这一个面板，避免为每个节点手写一套 UI（本期新增 4 类节点后
    /// 手写面板会爆炸）。新增/调整参数只需改 <see cref="NodeParamSchema"/>。</item>
    /// <item>全部使用 WinForms 自带控件，<strong>零图标、零第三方依赖</strong>；
    /// 颜色与字体一律取自 <see cref="UiTheme"/>，不在本文件里硬写色值。</item>
    /// <item>改动即时写入节点并抛出 <see cref="ParamsChanged"/>（由 FlowForm 持久化）。</item>
    /// </list>
    /// </remarks>
    public sealed partial class NodeParamPanel : UserControl
    {
        private readonly List<Control> _inputs = new List<Control>();

        private FlowNode _node;
        private NodeParamTable _table;
        private bool _updating;

        /// <summary>参数被用户修改时触发（FlowForm 据此保存配置）。</summary>
        public event EventHandler ParamsChanged;

        public NodeParamPanel()
        {
            InitializeComponent();
            ApplyTheme();
            SetNode(null);
        }

        /// <summary>当前绑定的节点（未绑定时为 null）。</summary>
        public FlowNode Node { get { return _node; } }

        // =====================================================================
        //  绑定节点
        // =====================================================================

        /// <summary>绑定要编辑的节点；null 或无参数表的节点显示空态。</summary>
        public void SetNode(FlowNode node)
        {
            _node = null;
            _table = null;

            if (node == null)
            {
                ShowEmpty("请选择节点");
                return;
            }

            FlowNodeType type;
            if (!node.TryGetType(out type))
            {
                ShowEmpty("未知节点类型：" + node.Type);
                return;
            }

            NodeParamTable table = NodeParamSchema.For(type);
            if (table == null || table.Fields == null || table.Fields.Count == 0)
            {
                ShowEmpty("该节点没有可配置参数");
                return;
            }

            _node = node;
            _table = table;

            lblTitle.Text = string.IsNullOrEmpty(table.Title) ? node.Name : (table.Title + "　" + node.Name);

            Build(table);
            LoadValues(node);

            body.Visible = true;
            empty.Visible = false;
        }

        private void ShowEmpty(string text)
        {
            body.Visible = false;
            empty.Visible = true;
            empty.Text = text;
            lblTitle.Text = "节点参数";
            body.Controls.Clear();
            _inputs.Clear();
        }

        // =====================================================================
        //  界面构建
        // =====================================================================

        private void ApplyTheme()
        {
            BackColor = UiTheme.ColorPanelBg;
            ForeColor = UiTheme.ColorTextPrimary;

            UiTheme.StyleGroupTitle(lblTitle);
            lblTitle.Height = 26;

            body.BackColor = UiTheme.ColorPanelBg;

            empty.ForeColor = UiTheme.ColorTextSecondary;
            empty.Font = UiTheme.FontBody;
        }

        private void Build(NodeParamTable table)
        {
            body.Controls.Clear();
            _inputs.Clear();

            var grid = new TableLayoutPanel();
            grid.Dock = DockStyle.Top;
            grid.AutoSize = true;
            grid.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            grid.ColumnCount = 2;
            grid.Padding = new Padding(UiTheme.Margin);
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52f));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48f));

            foreach (NodeParamField f in table.Fields)
            {
                if (f == null) continue;

                if (f.Kind == NodeParamKind.Group)
                {
                    AddGroupRow(grid, f.Label);
                    continue;
                }
                if (f.Kind == NodeParamKind.Note)
                {
                    AddNoteRow(grid, f.Label);
                    continue;
                }

                AddLabelRow(grid, f);

                Control input = CreateInput(f);
                grid.Controls.Add(input, 1, grid.RowCount);
                grid.RowCount++;
            }

            if (!string.IsNullOrEmpty(table.FooterNote))
                AddFooterRow(grid, table.FooterNote);

            body.Controls.Add(grid);
        }

        private void AddGroupRow(TableLayoutPanel grid, string text)
        {
            var lbl = new Label();
            lbl.Text = text;
            lbl.AutoSize = true;
            UiTheme.StyleGroupTitle(lbl);
            lbl.Margin = new Padding(2, UiTheme.BlockGap + 2, 2, 2);
            grid.Controls.Add(lbl, 0, grid.RowCount);
            grid.SetColumnSpan(lbl, 2);
            grid.RowCount++;
        }

        private void AddNoteRow(TableLayoutPanel grid, string text)
        {
            var lbl = new Label();
            lbl.Text = text;
            lbl.ForeColor = UiTheme.ColorTextSecondary;
            lbl.Font = UiTheme.FontBody;
            lbl.AutoSize = true;
            lbl.MaximumSize = new Size(260, 0);   // 允许自动换行
            lbl.Margin = new Padding(2, 2, 2, 2);
            grid.Controls.Add(lbl, 0, grid.RowCount);
            grid.SetColumnSpan(lbl, 2);
            grid.RowCount++;
        }

        private void AddFooterRow(TableLayoutPanel grid, string text)
        {
            var lbl = new Label();
            lbl.Text = text;
            lbl.ForeColor = UiTheme.ColorWarn;
            lbl.Font = UiTheme.FontBody;
            lbl.AutoSize = true;
            lbl.MaximumSize = new Size(260, 0);
            lbl.Margin = new Padding(2, UiTheme.BlockGap, 2, UiTheme.Margin);
            grid.Controls.Add(lbl, 0, grid.RowCount);
            grid.SetColumnSpan(lbl, 2);
            grid.RowCount++;
        }

        private void AddLabelRow(TableLayoutPanel grid, NodeParamField f)
        {
            var cap = new Label();
            cap.Text = f.Label;
            cap.ForeColor = UiTheme.ColorTextSecondary;
            cap.Font = UiTheme.FontBody;
            cap.AutoSize = true;
            cap.Anchor = AnchorStyles.Left;
            cap.Margin = new Padding(2, 4, 2, 0);

            if (!string.IsNullOrEmpty(f.Hint))
                toolTip.SetToolTip(cap, f.Hint);

            grid.Controls.Add(cap, 0, grid.RowCount);
        }

        private Control CreateInput(NodeParamField f)
        {
            switch (f.Kind)
            {
                case NodeParamKind.Bool:
                {
                    var chk = new CheckBox();
                    chk.Text = "";
                    chk.AutoSize = true;
                    chk.ForeColor = UiTheme.ColorTextPrimary;
                    chk.Tag = f;
                    chk.CheckedChanged += OnBoolChanged;
                    _inputs.Add(chk);
                    return chk;
                }

                case NodeParamKind.Enum:
                {
                    var cmb = new ComboBox();
                    cmb.DropDownStyle = ComboBoxStyle.DropDownList;
                    cmb.BackColor = UiTheme.ColorCardBg;
                    cmb.ForeColor = UiTheme.ColorTextPrimary;
                    cmb.FlatStyle = FlatStyle.Flat;
                    cmb.Dock = DockStyle.Fill;
                    foreach (NodeParamOption o in f.Options) cmb.Items.Add(o);
                    cmb.Tag = f;
                    cmb.SelectedIndexChanged += OnEnumChanged;
                    _inputs.Add(cmb);
                    return cmb;
                }

                case NodeParamKind.Text:
                {
                    var txt = new TextBox();
                    StyleTextBox(txt);
                    txt.Tag = f;
                    txt.TextChanged += OnTextChanged;
                    _inputs.Add(txt);
                    return txt;
                }

                case NodeParamKind.File:
                {
                    // 文本框 + 纯文字「浏览」按钮（禁止图标素材）
                    var host = new Panel();
                    host.Dock = DockStyle.Fill;
                    host.Height = 24;
                    host.BackColor = UiTheme.ColorPanelBg;
                    host.Padding = new Padding(0);

                    var btn = new Button();
                    btn.Text = "浏览";
                    btn.Dock = DockStyle.Right;
                    btn.Width = 48;
                    btn.FlatStyle = FlatStyle.Flat;
                    btn.BackColor = UiTheme.ColorCardBg;
                    btn.ForeColor = UiTheme.ColorTextPrimary;
                    btn.Font = UiTheme.FontBody;

                    var txt = new TextBox();
                    StyleTextBox(txt);
                    txt.Dock = DockStyle.Fill;
                    txt.Tag = f;
                    txt.TextChanged += OnTextChanged;

                    btn.Tag = txt;
                    btn.Click += OnBrowseClick;

                    host.Controls.Add(txt);
                    host.Controls.Add(btn);
                    _inputs.Add(txt);
                    return host;
                }

                default:
                {
                    var num = new NumericUpDown();
                    num.Minimum = f.Min;
                    num.Maximum = f.Max;
                    num.DecimalPlaces = f.Decimals;
                    num.Increment = f.Increment;
                    num.Dock = DockStyle.Fill;
                    StyleTextBox(num);
                    num.Tag = f;
                    num.ValueChanged += OnNumberChanged;
                    _inputs.Add(num);
                    return num;
                }
            }
        }

        private static void StyleTextBox(Control box)
        {
            box.BackColor = UiTheme.ColorCardBg;
            box.ForeColor = UiTheme.ColorTextPrimary;
        }

        // =====================================================================
        //  读值 / 写值
        // =====================================================================

        /// <summary>把节点当前参数回填到控件。</summary>
        private void LoadValues(FlowNode node)
        {
            _updating = true;
            try
            {
                foreach (Control c in _inputs)
                {
                    NodeParamField f = c.Tag as NodeParamField;
                    if (f == null) continue;

                    string raw = node.FindParam(f.Key) != null ? node.GetString(f.Key) : f.Default;

                    switch (f.Kind)
                    {
                        case NodeParamKind.Bool:
                            ((CheckBox)c).Checked = ParseBool(raw, f.Default);
                            break;

                        case NodeParamKind.Enum:
                        {
                            var cmb = (ComboBox)c;
                            int idx = 0;
                            for (int i = 0; i < cmb.Items.Count; i++)
                            {
                                var o = cmb.Items[i] as NodeParamOption;
                                if (o != null && string.Equals(o.Value, raw, StringComparison.OrdinalIgnoreCase))
                                { idx = i; break; }
                            }
                            cmb.SelectedIndex = idx;
                            break;
                        }

                        case NodeParamKind.Text:
                        case NodeParamKind.File:
                            ((TextBox)c).Text = raw ?? "";
                            break;

                        default:
                        {
                            var num = (NumericUpDown)c;
                            decimal v;
                            if (!decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                                decimal.TryParse(f.Default, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
                            if (v < num.Minimum) v = num.Minimum;
                            if (v > num.Maximum) v = num.Maximum;
                            num.Value = v;
                            break;
                        }
                    }
                }
            }
            finally
            {
                _updating = false;
            }
        }

        private static bool ParseBool(string raw, string def)
        {
            if (raw == "1") return true;
            if (raw == "0") return false;
            if (string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase)) return false;
            return def == "1";
        }

        private void OnNumberChanged(object sender, EventArgs e)
        {
            if (_updating || _node == null) return;

            var box = sender as NumericUpDown;
            if (box == null) return;

            NodeParamField f = box.Tag as NodeParamField;
            if (f == null) return;

            if (f.Kind == NodeParamKind.Int) _node.Set(f.Key, (int)box.Value);
            else _node.Set(f.Key, (double)box.Value);

            RaiseParamsChanged();
        }

        private void OnBoolChanged(object sender, EventArgs e)
        {
            if (_updating || _node == null) return;

            var chk = sender as CheckBox;
            if (chk == null) return;

            NodeParamField f = chk.Tag as NodeParamField;
            if (f == null) return;

            _node.Set(f.Key, chk.Checked);
            RaiseParamsChanged();
        }

        private void OnTextChanged(object sender, EventArgs e)
        {
            if (_updating || _node == null) return;

            var txt = sender as TextBox;
            if (txt == null) return;

            NodeParamField f = txt.Tag as NodeParamField;
            if (f == null) return;

            _node.Set(f.Key, txt.Text);
            RaiseParamsChanged();
        }

        private void OnEnumChanged(object sender, EventArgs e)
        {
            if (_updating || _node == null) return;

            var cmb = sender as ComboBox;
            if (cmb == null) return;

            NodeParamField f = cmb.Tag as NodeParamField;
            if (f == null) return;

            var o = cmb.SelectedItem as NodeParamOption;
            if (o == null) return;

            _node.Set(f.Key, o.Value);
            RaiseParamsChanged();
        }

        private void OnBrowseClick(object sender, EventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;

            var txt = btn.Tag as TextBox;
            if (txt == null) return;

            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "选择图片文件";
                dlg.Filter = "图片文件|*.bmp;*.png;*.jpg;*.jpeg;*.tif;*.tiff|所有文件|*.*";
                dlg.CheckFileExists = true;

                if (!string.IsNullOrEmpty(txt.Text)) dlg.FileName = txt.Text;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                txt.Text = dlg.FileName;   // 触发 TextChanged → 写入节点并抛出 ParamsChanged
            }
        }

        private void RaiseParamsChanged()
        {
            EventHandler h = ParamsChanged;
            if (h != null) h(this, EventArgs.Empty);
        }
    }
}
