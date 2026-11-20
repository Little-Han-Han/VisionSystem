using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using VisionPlatform.Flow;

namespace VisionPlatform.UI
{
    /// <summary>
    /// 通用「参数列表」控件：按 <see cref="NodeParamTable"/> 的声明动态生成控件，
    /// 通过 <see cref="IParamValueStore"/> 读写值。
    /// <para>
    /// 【复用边界】本控件只认识参数表 + 值存储，不认识流程节点、相机、标定、通讯。
    /// 因此同一套参数界面可用于流程节点、配方参数、设置页，甚至独立测试宿主。
    /// </para>
    /// <para>
    /// 【按需生成】每次 SetModel 都按当前参数表重建控件（控件在构造期全部创建，
    /// 折叠只改 Visible —— 延迟创建会让读值 / 保存失效，参数被静默丢失）。
    /// </para>
    /// </summary>
    public sealed class ParamListControl : UserControl
    {
        private readonly List<Control> _inputs = new List<Control>();
        private readonly Label _title = new Label();
        private readonly Panel _body = new Panel();
        private readonly Label _empty = new Label();
        private readonly ToolTip _toolTip = new ToolTip();

        private Control _banner;
        private NodeParamTable _table;
        private IParamValueStore _store;
        private IParamOptionProvider _options;
        private bool _updating;

        /// <summary>参数被用户修改时触发。</summary>
        public event EventHandler ParamsChanged;

        /// <summary>动作行（<see cref="NodeParamKind.Action"/>）被点击时触发，参数是字段 Key。</summary>
        public event Action<string> ActionInvoked;

        public ParamListControl()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer, true);

            _title.Dock = DockStyle.Top;
            _title.Height = 26;
            _title.TextAlign = ContentAlignment.MiddleLeft;
            _title.Text = "参数";

            _body.Dock = DockStyle.Fill;
            _body.AutoScroll = true;

            _empty.Dock = DockStyle.Fill;
            _empty.TextAlign = ContentAlignment.MiddleCenter;
            _empty.Text = "请选择节点";

            Controls.Add(_body);
            Controls.Add(_empty);
            Controls.Add(_title);

            ApplyTheme();
            ShowEmpty("请选择节点");
        }

        /// <summary>动态选项提供者（可空）。</summary>
        public IParamOptionProvider OptionProvider
        {
            get { return _options; }
            set { _options = value; }
        }

        /// <summary>当前参数表（未绑定时为 null）。</summary>
        public NodeParamTable Table { get { return _table; } }

        // =====================================================================
        //  绑定 / 空态
        // =====================================================================

        /// <summary>
        /// 绑定参数表与值存储；<paramref name="titleSuffix"/> 追加在标题后（一般是节点名）。
        /// 表为空时显示空态。
        /// </summary>
        public void SetModel(NodeParamTable table, IParamValueStore store, string titleSuffix)
        {
            _table = table;
            _store = store;

            if (table == null || table.Fields == null || table.Fields.Count == 0)
            {
                ShowEmpty("该节点没有可配置参数");
                return;
            }

            _title.Text = string.IsNullOrEmpty(table.Title)
                ? (titleSuffix ?? "")
                : (table.Title + (string.IsNullOrEmpty(titleSuffix) ? "" : "　" + titleSuffix));

            Build(table);
            LoadValues();

            _body.Visible = true;
            _empty.Visible = false;
        }

        /// <summary>显示空态（隐藏参数区）。</summary>
        /// <summary>
        /// 从 store 重新读一遍值填进现有控件（**不重建**控件）。
        /// <para>供"外部改了节点参数"的场景：例如字符模板训练窗体把分割参数实时写回节点，
        /// 这边的高级参数要立刻显示同一组数，否则用户会以为没生效。</para>
        /// </summary>
        public void ReloadValues()
        {
            LoadValues();
        }

        public void ShowEmpty(string text)
        {
            _body.Visible = false;
            _empty.Visible = true;
            _empty.Text = text ?? "";
            _title.Text = "节点参数";
            _body.Controls.Clear();
            _inputs.Clear();
        }

        /// <summary>
        /// 在参数区顶部插入横幅控件；重复调用替换并释放旧控件，传 null 移除。
        /// <para>所有权转移给本控件：调用方不要再 Dispose / 复用。</para>
        /// </summary>
        public void SetBanner(Control banner)
        {
            if (ReferenceEquals(_banner, banner))
            {
                if (banner != null) AttachBanner();
                return;
            }

            Control old = _banner;
            _banner = banner;

            if (old != null)
            {
                _body.Controls.Remove(old);
                old.Dispose();
            }

            if (banner == null) return;
            if (banner.Parent != null) banner.Parent.Controls.Remove(banner);
            AttachBanner();
        }

        private void AttachBanner()
        {
            if (_banner == null) return;
            _banner.Dock = DockStyle.Top;
            _body.Controls.Add(_banner);
        }

        // =====================================================================
        //  主题
        // =====================================================================

        /// <summary>套用当前主题（切换深浅色时由宿主调用）。</summary>
        public void ApplyTheme()
        {
            BackColor = UiTheme.ColorPanelBg;
            ForeColor = UiTheme.ColorTextPrimary;

            UiTheme.StyleGroupTitle(_title);
            _title.Height = 26;

            _body.BackColor = UiTheme.ColorPanelBg;

            _empty.ForeColor = UiTheme.ColorTextSecondary;
            _empty.Font = UiTheme.FontBody;

            if (_banner != null) _banner.Invalidate(true);
            RestyleGroupHeaders(_body);
        }

        private static void RestyleGroupHeaders(Control root)
        {
            if (root == null) return;

            var lbl = root as Label;
            if (lbl != null && lbl.Tag is GroupHeaderTag) UiTheme.StyleGroupTitle(lbl);

            foreach (Control c in root.Controls) RestyleGroupHeaders(c);
        }

        // =====================================================================
        //  界面构建
        // =====================================================================

        private void Build(NodeParamTable table)
        {
            _body.Controls.Clear();
            _inputs.Clear();

            TableLayoutPanel grid = NewParamGrid();
            TableLayoutPanel advGrid = NewParamGrid();
            int advancedCount = 0;
            TableLayoutPanel currentGroup = null;

            foreach (NodeParamField f in table.Fields)
            {
                if (f == null) continue;

                if (f.Kind == NodeParamKind.Group)
                {
                    currentGroup = AddGroupRow(grid, f.Label);
                    continue;
                }
                if (f.Kind == NodeParamKind.Note)
                {
                    AddNoteRow(currentGroup ?? grid, f.Label);
                    continue;
                }

                TableLayoutPanel host = f.Advanced ? advGrid : (currentGroup ?? grid);

                if (f.Kind == NodeParamKind.Action)
                {
                    Control action = CreateInput(f);
                    ApplyDisabled(action, f);
                    host.Controls.Add(action, 0, host.RowCount);
                    host.SetColumnSpan(action, 2);
                    host.RowCount++;
                    continue;
                }

                AddLabelRow(host, f);

                Control input = CreateInput(f);
                ApplyDisabled(input, f);
                host.Controls.Add(input, 1, host.RowCount);
                host.RowCount++;

                if (f.Advanced) advancedCount++;
            }

            if (!string.IsNullOrEmpty(table.FooterNote))
                AddFooterRow(grid, table.FooterNote);

            if (advancedCount > 0)
            {
                advGrid.Visible = false;
                _body.Controls.Add(advGrid);
                _body.Controls.Add(NewAdvancedHeader(advGrid, advancedCount));
            }

            _body.Controls.Add(grid);
            AttachBanner();
        }

        private static TableLayoutPanel NewParamGrid()
        {
            var grid = new TableLayoutPanel();
            grid.Dock = DockStyle.Top;
            grid.AutoSize = true;
            grid.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            grid.ColumnCount = 2;
            grid.Padding = new Padding(UiTheme.Margin);
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52f));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48f));
            return grid;
        }

        private Label NewAdvancedHeader(Control inner, int count)
        {
            var header = new Label();
            header.Dock = DockStyle.Top;
            header.Height = 26;
            header.TextAlign = ContentAlignment.MiddleLeft;
            header.Text = AdvancedHeaderText(false, count);
            header.Cursor = Cursors.Hand;
            header.ForeColor = UiTheme.ColorAccent;
            header.Font = UiTheme.FontGroupTitle;
            header.Tag = inner;
            header.Click += OnAdvancedHeaderClick;
            _toolTip.SetToolTip(header, "不常用的参数，点击展开/折叠");
            return header;
        }

        private static string AdvancedHeaderText(bool expanded, int count)
        {
            return string.Format("高级参数（{0} 项）  {1}", count, expanded ? "▼" : "▲");
        }

        private void OnAdvancedHeaderClick(object sender, EventArgs e)
        {
            var header = sender as Label;
            if (header == null) return;

            Control inner = header.Tag as Control;
            if (inner == null) return;

            inner.Visible = !inner.Visible;
            header.Text = AdvancedHeaderText(inner.Visible, AdvancedCount(inner));
        }

        private static int AdvancedCount(Control inner)
        {
            var grid = inner as TableLayoutPanel;
            return grid != null ? grid.RowCount : 0;
        }

        private TableLayoutPanel AddGroupRow(TableLayoutPanel grid, string text)
        {
            var header = new Label();
            header.AutoSize = true;
            header.Text = GroupHeaderText(text, true);
            header.Cursor = Cursors.Hand;
            UiTheme.StyleGroupTitle(header);
            header.Margin = new Padding(2, UiTheme.BlockGap + 2, 2, 2);
            header.Click += OnGroupHeaderClick;
            _toolTip.SetToolTip(header, "点击折叠 / 展开本组（默认展开，折叠状态不写入配置）");

            grid.Controls.Add(header, 0, grid.RowCount);
            grid.SetColumnSpan(header, 2);
            grid.RowCount++;

            TableLayoutPanel inner = NewParamGrid();
            inner.Padding = new Padding(0);
            inner.Dock = DockStyle.Top;

            grid.Controls.Add(inner, 0, grid.RowCount);
            grid.SetColumnSpan(inner, 2);
            grid.RowCount++;

            header.Tag = new GroupHeaderTag { Title = text, Inner = inner };
            return inner;
        }

        private sealed class GroupHeaderTag
        {
            public string Title = "";
            public TableLayoutPanel Inner;
        }

        private static string GroupHeaderText(string title, bool expanded)
        {
            return string.Format("{0}  {1}", title, expanded ? "▼" : "▲");
        }

        private void OnGroupHeaderClick(object sender, EventArgs e)
        {
            var header = sender as Label;
            if (header == null) return;

            var tag = header.Tag as GroupHeaderTag;
            if (tag == null || tag.Inner == null) return;

            bool expand = !tag.Inner.Visible;
            tag.Inner.Visible = expand;
            header.Text = GroupHeaderText(tag.Title, expand);

            if (_body != null) _body.PerformLayout();
        }

        private void AddNoteRow(TableLayoutPanel grid, string text)
        {
            var lbl = new Label();
            lbl.Text = text;
            lbl.ForeColor = UiTheme.ColorTextSecondary;
            lbl.Font = UiTheme.FontBody;
            lbl.AutoSize = true;
            lbl.MaximumSize = new Size(260, 0);
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
            cap.ForeColor = f.IsDisabled ? UiTheme.ColorDisabled : UiTheme.ColorTextSecondary;
            cap.Font = UiTheme.FontBody;
            cap.AutoSize = true;
            cap.Anchor = AnchorStyles.Left;
            cap.Margin = new Padding(2, 4, 2, 0);

            if (!string.IsNullOrEmpty(f.Hint))
                _toolTip.SetToolTip(cap, f.Hint);

            grid.Controls.Add(cap, 0, grid.RowCount);
        }

        private void ApplyDisabled(Control control, NodeParamField f)
        {
            if (control == null || f == null) return;

            if (!f.IsDisabled)
            {
                if (!string.IsNullOrEmpty(f.Hint)) _toolTip.SetToolTip(control, f.Hint);
                return;
            }

            control.Enabled = false;
            _toolTip.SetToolTip(control, f.DisabledReason);
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

                    List<NodeParamOption> opts = f.Options;
                    if (!string.IsNullOrEmpty(f.OptionsSource) && _options != null)
                    {
                        List<NodeParamOption> dyn = _options.GetOptions(f);
                        if (dyn != null) opts = dyn;
                    }
                    foreach (NodeParamOption o in opts) cmb.Items.Add(o);

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

                case NodeParamKind.Code:
                {
                    var code = new TextBox();
                    StyleTextBox(code);
                    code.Multiline = true;
                    code.AcceptsReturn = true;
                    code.AcceptsTab = true;
                    code.ScrollBars = ScrollBars.Vertical;
                    code.WordWrap = false;
                    code.Font = new Font("Consolas", 9.5f);
                    code.Height = f.Height > 0 ? f.Height : 160;
                    code.Tag = f;
                    code.TextChanged += OnTextChanged;
                    _inputs.Add(code);
                    return code;
                }

                case NodeParamKind.File:
                {
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

                case NodeParamKind.Folder:
                {
                    var host = new Panel();
                    host.Dock = DockStyle.Fill;
                    host.Height = 24;
                    host.BackColor = UiTheme.ColorPanelBg;
                    host.Padding = new Padding(0);

                    var btnOpen = new Button();
                    btnOpen.Text = "打开";
                    btnOpen.Dock = DockStyle.Right;
                    btnOpen.Width = 44;
                    btnOpen.FlatStyle = FlatStyle.Flat;
                    btnOpen.BackColor = UiTheme.ColorCardBg;
                    btnOpen.ForeColor = UiTheme.ColorTextPrimary;
                    btnOpen.Click += OnOpenFolderClick;

                    var btnBrowse = new Button();
                    btnBrowse.Text = "浏览";
                    btnBrowse.Dock = DockStyle.Right;
                    btnBrowse.Width = 44;
                    btnBrowse.FlatStyle = FlatStyle.Flat;
                    btnBrowse.BackColor = UiTheme.ColorCardBg;
                    btnBrowse.ForeColor = UiTheme.ColorTextPrimary;
                    btnBrowse.Click += OnBrowseFolderClick;

                    var txt = new TextBox();
                    StyleTextBox(txt);
                    txt.Dock = DockStyle.Fill;
                    txt.Tag = f;
                    txt.TextChanged += OnTextChanged;

                    btnBrowse.Tag = txt;
                    btnOpen.Tag = txt;

                    host.Controls.Add(txt);
                    host.Controls.Add(btnBrowse);
                    host.Controls.Add(btnOpen);
                    _inputs.Add(txt);
                    return host;
                }

                case NodeParamKind.Action:
                {
                    var act = new UiButton();
                    act.Kind = UiButtonKind.Neutral;
                    act.Text = f.Label;
                    act.Dock = DockStyle.Fill;
                    act.Height = 28;
                    act.MinimumSize = new Size(80, 28);
                    act.Margin = new Padding(2, 2, 2, 2);
                    act.Tag = f;
                    act.Click += OnActionClick;

                    if (!string.IsNullOrEmpty(f.Hint)) _toolTip.SetToolTip(act, f.Hint);
                    return act;
                }

                default:
                {
                    var num = new NumericUpDown();
                    num.Minimum = f.Min;
                    num.Maximum = f.Max;
                    num.DecimalPlaces = f.Decimals;
                    // f.Increment 是参数表的建议步长，最终统一由 NumericInputPolicy 按值语义覆盖
                    // （int → 1 / float → 0.1 / double → 0.01）。保留赋值是为了让参数表
                    // 单独复用时不至于退回系统默认步长 1。
                    num.Increment = f.Increment;
                    num.Dock = DockStyle.Fill;
                    StyleTextBox(num);
                    num.Tag = f;
                    // 本控件在 UiStyle.IsSelfDrawn 名单里，主题遍历会跳过整棵子树，
                    // 必须在这里直接套用，否则动态生成的数值框滚轮仍会误改值。
                    NumericInputPolicy.ApplyTo(num);
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

        private void LoadValues()
        {
            if (_store == null) return;

            _updating = true;
            try
            {
                foreach (Control c in _inputs)
                {
                    NodeParamField f = c.Tag as NodeParamField;
                    if (f == null) continue;

                    string raw = _store.Has(f.Key) ? _store.Get(f.Key) : f.Default;

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
                            if (cmb.Items.Count > 0) cmb.SelectedIndex = idx;
                            break;
                        }

                        case NodeParamKind.Text:
                        case NodeParamKind.Code:
                        case NodeParamKind.File:
                        case NodeParamKind.Folder:
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

            ApplyLiveValues();
        }

        /// <summary>
        /// 实时值覆盖（如「标定转换」的坐标 X/Y 接上游时显示实际传入值）；
        /// 由参数表声明 <see cref="NodeParamField.LiveSource"/>，控件不认识具体节点。
        /// </summary>
        private void ApplyLiveValues()
        {
            if (_store == null) return;

            foreach (Control c in _inputs)
            {
                var num = c as NumericUpDown;
                if (num == null) continue;

                var f = num.Tag as NodeParamField;
                if (f == null || string.IsNullOrEmpty(f.LiveSource)) continue;

                string value; bool readOnly; string tooltip;
                if (!_store.TryGetLive(f.LiveSource, out value, out readOnly, out tooltip)) continue;

                double parsed;
                if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)) continue;

                _updating = true;
                try
                {
                    decimal v = (decimal)parsed;
                    if (v < num.Minimum) v = num.Minimum;
                    if (v > num.Maximum) v = num.Maximum;
                    num.Value = v;
                }
                finally { _updating = false; }

                if (readOnly) num.Enabled = false;
                _toolTip.SetToolTip(num, string.IsNullOrEmpty(tooltip) ? "运行时实时值" : tooltip);
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
            if (_updating || _store == null) return;

            var box = sender as NumericUpDown;
            if (box == null) return;

            NodeParamField f = box.Tag as NodeParamField;
            if (f == null) return;

            if (f.Kind == NodeParamKind.Int) _store.SetInt(f.Key, (int)box.Value);
            else _store.SetDouble(f.Key, (double)box.Value);

            RaiseParamsChanged();
        }

        private void OnBoolChanged(object sender, EventArgs e)
        {
            if (_updating || _store == null) return;

            var chk = sender as CheckBox;
            if (chk == null) return;

            NodeParamField f = chk.Tag as NodeParamField;
            if (f == null) return;

            _store.SetBool(f.Key, chk.Checked);
            RaiseParamsChanged();
        }

        private void OnTextChanged(object sender, EventArgs e)
        {
            if (_updating || _store == null) return;

            var txt = sender as TextBox;
            if (txt == null) return;

            NodeParamField f = txt.Tag as NodeParamField;
            if (f == null) return;

            _store.SetText(f.Key, txt.Text);
            RaiseParamsChanged();
        }

        private void OnEnumChanged(object sender, EventArgs e)
        {
            if (_updating || _store == null) return;

            var cmb = sender as ComboBox;
            if (cmb == null) return;

            NodeParamField f = cmb.Tag as NodeParamField;
            if (f == null) return;

            var o = cmb.SelectedItem as NodeParamOption;
            if (o == null) return;

            _store.SetText(f.Key, o.Value);
            RaiseParamsChanged();
        }

        private static string FileDialogKind(NodeParamField f)
        {
            if (f == null || string.IsNullOrEmpty(f.Key)) return "any";
            if (!string.IsNullOrEmpty(f.FileFilter)) return "custom";   // 参数表直接声明了过滤器

            string k = f.Key.ToLowerInvariant();
            if (k.Contains("model") || k.Contains("onnx")) return "model";
            if (k.Contains("dict") || k.Contains("label") || k.Contains("keys") || k.Contains("txt"))
                return "text";
            if (k.Contains("path") || k.Contains("file") || k.Contains("image") || k.Contains("template"))
                return "image";
            return "any";
        }

        private static string FileDialogTitle(NodeParamField f)
        {
            switch (FileDialogKind(f))
            {
                case "custom": return "选择 " + (f != null ? f.Label : "文件");
                case "model": return "选择模型文件（.onnx）";
                case "text": return "选择文本文件（字典 / 标签）";
                case "image": return "选择图片文件";
                default: return "选择文件";
            }
        }

        private static string FileDialogFilter(NodeParamField f)
        {
            switch (FileDialogKind(f))
            {
                case "custom": return f.FileFilter;
                case "model":
                    return "ONNX 模型|*.onnx|OpenVINO IR|*.xml|所有文件|*.*";
                case "text":
                    return "文本文件|*.txt;*.yml;*.yaml;*.vocab|所有文件|*.*";
                case "image":
                    return "图片文件|*.bmp;*.png;*.jpg;*.jpeg;*.tif;*.tiff|所有文件|*.*";
                default:
                    return "所有文件|*.*";
            }
        }

        private static string FileDialogDefaultExt(NodeParamField f)
        {
            switch (FileDialogKind(f))
            {
                case "custom": return (f != null && !string.IsNullOrEmpty(f.FileExt)) ? f.FileExt : "";
                case "model": return "onnx";
                case "text": return "txt";
                case "image": return "bmp";
                default: return "";
            }
        }

        private void OnBrowseClick(object sender, EventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;

            var txt = btn.Tag as TextBox;
            if (txt == null) return;

            var field = txt.Tag as NodeParamField;

            string chosen;
            if (field != null && field.FileAllowCreate)
            {
                // 允许"先定名字、保存时才创建"（字符库 / 配置类文件）。
                // 用 SaveFileDialog 但**不提示覆盖**：这里只是在选路径，不是在写文件。
                using (var dlg = new SaveFileDialog())
                {
                    dlg.Title = FileDialogTitle(field);
                    dlg.Filter = FileDialogFilter(field);
                    dlg.DefaultExt = FileDialogDefaultExt(field);
                    dlg.OverwritePrompt = false;
                    dlg.CheckPathExists = false;

                    string cur = (txt.Text ?? "").Trim();
                    if (cur.Length > 0)
                    {
                        dlg.FileName = Path.GetFileName(cur);
                        string dir = Path.GetDirectoryName(cur);
                        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) dlg.InitialDirectory = dir;
                    }

                    if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
                    chosen = dlg.FileName;
                }
            }
            else
            {
                using (var dlg = new OpenFileDialog())
                {
                    dlg.Title = FileDialogTitle(field);
                    dlg.Filter = FileDialogFilter(field);
                    dlg.DefaultExt = FileDialogDefaultExt(field);
                    dlg.CheckFileExists = true;

                    if (!string.IsNullOrEmpty(txt.Text)) dlg.FileName = txt.Text;
                    if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
                    chosen = dlg.FileName;
                }
            }

            // 补后缀：输入 line1 → line1.ocrtpl（否则下次按扩展名识别失败）
            string ext = (field != null) ? field.FileExt : null;
            if (!string.IsNullOrEmpty(ext)
                && !string.Equals(Path.GetExtension(chosen), "." + ext, StringComparison.OrdinalIgnoreCase))
            {
                chosen += "." + ext;
            }

            txt.Text = chosen;
        }

        private void OnBrowseFolderClick(object sender, EventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;

            var txt = btn.Tag as TextBox;
            if (txt == null) return;

            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "选择模板目录（也可以直接在上面的输入框里填相对路径）";

                string cur = ResolveFolderPath(txt.Text);
                if (!string.IsNullOrEmpty(cur) && Directory.Exists(cur)) dlg.SelectedPath = cur;

                if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;

                txt.Text = dlg.SelectedPath;
            }
        }

        private void OnOpenFolderClick(object sender, EventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;

            var txt = btn.Tag as TextBox;
            if (txt == null) return;

            string path = ResolveFolderPath(txt.Text);
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                if (!Directory.Exists(path)) Directory.CreateDirectory(path);
                Process.Start("explorer.exe", "\"" + path + "\"");
            }
            catch (Exception ex)
            {
                MessageBox.Show(FindForm(), "无法打开目录：" + ex.Message, "模板目录",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private static string ResolveFolderPath(string value)
        {
            string v = (value ?? "").Trim();
            if (v.Length == 0) v = "OCRModel";
            try
            {
                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, v);
            }
            catch (Exception)
            {
                return "";
            }
        }

        private void RaiseParamsChanged()
        {
            EventHandler h = ParamsChanged;
            if (h != null) h(this, EventArgs.Empty);
        }

        private void OnActionClick(object sender, EventArgs e)
        {
            var c = sender as Control;
            if (c == null) return;

            NodeParamField f = c.Tag as NodeParamField;
            if (f == null || string.IsNullOrEmpty(f.Key)) return;

            Action<string> h = ActionInvoked;
            if (h != null) h(f.Key);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_toolTip != null) _toolTip.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
