using System;
using System.Collections.Generic;
using System.Drawing;
using System.Diagnostics;
using System.Globalization;
using System.IO;
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
    /// <item>⚠️ 本面板在 <see cref="UiStyle"/> 的自绘跳过名单里（整棵子树不会被自动套主题），
    /// 所以这里<strong>新建的每个控件都必须自己从 <see cref="UiTheme"/> 取色</strong>，
    /// 不能指望 <see cref="UiStyle.ApplyForm"/> 兜底。</item>
    /// <item>参数区顶部有一个可插拔的「横幅」槽位（<see cref="SetBanner"/>），
    /// 供调用方放模板状态卡片之类的一次性提示块。</item>
    /// </list>
    /// </remarks>
    public sealed partial class NodeParamPanel : UserControl
    {
        private readonly List<Control> _inputs = new List<Control>();

        /// <summary>参数区顶部的自定义控件槽位（null = 没有）。</summary>
        private Control _banner;

        private FlowNode _node;
        private NodeParamTable _table;
        private bool _updating;

        /// <summary>参数被用户修改时触发（FlowForm 据此保存配置）。</summary>
        public event EventHandler ParamsChanged;

        /// <summary>
        /// 动作行（<see cref="NodeParamKind.Action"/>）被点击时触发，参数是
        /// <see cref="NodeParamField.Key"/>（如 <c>RoiPick</c> / <c>CreateTemplate</c>）。
        /// <para>动作行没有参数值，不会触发 <see cref="ParamsChanged"/>。</para>
        /// </summary>
        public event Action<string> ActionInvoked;

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
        //  顶部横幅槽位
        // =====================================================================

        /// <summary>
        /// 在参数区<strong>顶部</strong>插入一个自定义控件（横幅），重复调用会替换并释放旧控件；
        /// 传 null 移除横幅。
        /// </summary>
        /// <remarks>
        /// <list type="bullet">
        /// <item>传进来的控件<strong>所有权转移给本面板</strong>：被替换时会被
        /// <see cref="Control.Dispose()"/>，调用方不要自己再 Dispose，也不要在外部复用它。</item>
        /// <item>本面板只会改它的 <see cref="Control.Dock"/>（强制 <see cref="DockStyle.Top"/>），
        /// <strong>不会</strong>改颜色与字体 —— 因为本面板整棵子树被 <see cref="UiStyle"/> 跳过，
        /// 横幅内部控件必须自己从 <see cref="UiTheme"/> 取色，否则在深色主题下会是系统浅色。
        /// 换主题时本面板会 <see cref="Control.Invalidate()"/> 横幅，但重取色仍需横幅自己做
        /// （可订阅 <see cref="UiTheme.Changed"/>）。高度也由横幅自己给（<c>Dock=Top</c> 只吃高度）。</item>
        /// <item><see cref="SetNode"/> <strong>不会</strong>清空横幅：显隐完全由调用方决定。
        /// 未选中节点（空态）时 <c>body</c> 整块隐藏，横幅随之不可见，但控件对象保留，
        /// 下次选中带参数的节点时自动恢复显示。</item>
        /// </list>
        /// </remarks>
        /// <param name="banner">要放在顶部的控件；null 表示移除当前横幅。</param>
        public void SetBanner(Control banner)
        {
            if (ReferenceEquals(_banner, banner))
            {
                // 同一个控件重复设置：只是把它重新顶到最上面，绝不 Dispose 自己
                if (banner != null) AttachBanner();
                return;
            }

            Control old = _banner;
            _banner = banner;

            if (old != null)
            {
                body.Controls.Remove(old);
                old.Dispose();
            }

            if (banner == null) return;

            // 调用方可能把一个已挂在别处的控件交进来，先摘下来再挂，避免出现两个父容器
            if (banner.Parent != null) banner.Parent.Controls.Remove(banner);

            AttachBanner();
        }

        /// <summary>把横幅挂到 <c>body</c> 的<strong>最后</strong>（Dock.Top 逆序 → 显示在最上面）。</summary>
        private void AttachBanner()
        {
            if (_banner == null) return;

            _banner.Dock = DockStyle.Top;
            body.Controls.Add(_banner);
        }

        // =====================================================================
        //  界面构建
        // =====================================================================

        /// <summary>运行时套用主题。切换深浅色板时由 <see cref="UiThemeManager"/> 重新调用。</summary>
        internal void ApplyTheme()
        {
            BackColor = UiTheme.ColorPanelBg;
            ForeColor = UiTheme.ColorTextPrimary;

            UiTheme.StyleGroupTitle(lblTitle);
            lblTitle.Height = 26;

            body.BackColor = UiTheme.ColorPanelBg;

            empty.ForeColor = UiTheme.ColorTextSecondary;
            empty.Font = UiTheme.FontBody;

            // 横幅由调用方自建（本面板不改它的配色），这里只让它重绘一次：
            // 若横幅内部也订阅了 UiTheme.Changed，颜色会在它自己的回调里更新。
            if (_banner != null) _banner.Invalidate(true);

            // 分组组头是构造期一次性 StyleGroupTitle 的，换主题不会自动重取色 —— 
            // 不重套的话浅色主题下会留着深色的组标题（看起来像"这组禁用"）。
            RestyleGroupHeaders(body);
        }

        /// <summary>递归重套分组组头的主题色（只认 <see cref="GroupHeaderTag"/> 标记的 Label）。</summary>
        private static void RestyleGroupHeaders(Control root)
        {
            if (root == null) return;

            var lbl = root as Label;
            if (lbl != null && lbl.Tag is GroupHeaderTag) UiTheme.StyleGroupTitle(lbl);

            foreach (Control c in root.Controls) RestyleGroupHeaders(c);
        }

        private void Build(NodeParamTable table)
        {
            body.Controls.Clear();
            _inputs.Clear();

            TableLayoutPanel grid = NewParamGrid();
            TableLayoutPanel advGrid = NewParamGrid();   // 「高级参数」区（默认折叠）
            int advancedCount = 0;

            // 当前分组容器：遇到 Group 行就换一个新的；null = 还没进分组，字段直接挂主表。
            // 【为什么用"组内独立表"而不是"给行打标记后统一隐藏"】行高由内容撑开，
            // 隐藏单个控件不会让它的行高度归零，会留一排空洞 —— 那看起来像"折叠坏了"。
            TableLayoutPanel currentGroup = null;

            foreach (NodeParamField f in table.Fields)
            {
                if (f == null) continue;

                // 分组标题：换成"可折叠组头 + 组内独立表"，之后本组的字段都进这张组内表。
                // ⚠️ 组内控件<strong>构造期就全部建好并登记 _inputs</strong>，折叠只改 Visible，
                //    禁止"展开时才创建"：那会让 LoadValues 读不到、保存也失效，参数被静默丢失。
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

                // 动作行：按钮文案本身就是说明文字，再加一列标题只会重复，故整行铺满。
                // 视觉仍是 UiButton（与 File 行的「浏览」按钮同一个控件、同一套取色）。
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
                // 高级区放在主表下方。WinForms 的 Dock 按 Controls 集合【逆序】停靠
                // （索引大的先停靠、排在上面），所以按「内部表格 → 标题 → 主表」的顺序 Add，
                // 最终自上而下才是：主表 / 标题 / 内部表格。
                // 三者都直接挂在 body 上（不再套一层 AutoSize Panel）：AutoSize + Dock 嵌套
                // 会让外层容器算不出高度而塌成 0。
                advGrid.Visible = false;                       // 默认折叠
                body.Controls.Add(advGrid);
                body.Controls.Add(NewAdvancedHeader(advGrid, advancedCount));
            }

            body.Controls.Add(grid);

            // banner 必须最后加：Dock.Top 是按 Controls 索引逆序停靠的，
            // 索引最大者排在最上面（同上面「主表 / 标题 / 内部表格」的道理）。
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

        /// <summary>
        /// 「高级参数」可折叠区的标题行（<strong>内部表格默认折叠</strong>，见 <see cref="Build"/>）。
        /// 三角沿用 MainForm 日志折叠的约定：折叠态 ▲（点它展开）、展开态 ▼（点它收起）。
        /// </summary>
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
            header.Tag = inner;                     // 点击时据此找到要展开/折叠的容器
            header.Click += OnAdvancedHeaderClick;
            toolTip.SetToolTip(header, "不常用的参数，点击展开/折叠");
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

        /// <summary>统计折叠区里的参数条数（用于标题「高级参数（n 项）」）。</summary>
        private static int AdvancedCount(Control inner)
        {
            var grid = inner as TableLayoutPanel;
            return grid != null ? grid.RowCount : 0;
        }

        /// <summary>
        /// 新增一个<strong>可折叠分组</strong>：组头（可点）+ 组内独立参数表，返回组内表供后续字段挂载。
        /// <para>
        /// 【为什么组内要独立一张 TableLayoutPanel】折叠靠 <c>Visible=false</c> 实现；
        /// 若字段和组头同在一张表里，隐藏控件不会让它的行高归零，折叠后仍留一排空洞。
        /// 组内独立表的 AutoSize 会在隐藏时把高度收成 0，折叠才是"真的收起来"。
        /// </para>
        /// <para>【红线】组内控件在<strong>构造期全部创建</strong>：延迟创建会让 LoadValues 读不到、
        /// 保存写不回，参数会静默丢失（这个 bug 的反馈周期是一天，不是一秒）。</para>
        /// </summary>
        private TableLayoutPanel AddGroupRow(TableLayoutPanel grid, string text)
        {
            var header = new Label();
            header.AutoSize = true;
            header.Text = GroupHeaderText(text, true);
            header.Cursor = Cursors.Hand;
            UiTheme.StyleGroupTitle(header);
            header.Margin = new Padding(2, UiTheme.BlockGap + 2, 2, 2);
            header.Click += OnGroupHeaderClick;
            toolTip.SetToolTip(header, "点击折叠 / 展开本组（默认展开，折叠状态不写入配置）");

            grid.Controls.Add(header, 0, grid.RowCount);
            grid.SetColumnSpan(header, 2);
            grid.RowCount++;

            TableLayoutPanel inner = NewParamGrid();
            inner.Padding = new Padding(0);          // 组内不再叠加边距，否则每组都会多一圈空白
            inner.Dock = DockStyle.Top;

            grid.Controls.Add(inner, 0, grid.RowCount);
            grid.SetColumnSpan(inner, 2);
            grid.RowCount++;

            header.Tag = new GroupHeaderTag { Title = text, Inner = inner };
            return inner;
        }

        /// <summary>组头与组内表的绑定（<c>Tag</c> 只能用 object，故用一个小的载体）。</summary>
        private sealed class GroupHeaderTag
        {
            public string Title = "";
            public TableLayoutPanel Inner;
        }

        private static string GroupHeaderText(string title, bool expanded)
        {
            // 与「高级参数」同一套约定：展开 ▼（点它收起）、折叠 ▲（点它展开）
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

            // 折叠后参数区总高变小，滚动位置可能越界 → 重算一次布局
            if (body != null) body.PerformLayout();
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
            // 禁用的参数连标题一起变暗：否则只有右侧控件灰、标签还是亮的，
            // 看起来像"这项正常，只是输入框坏了"。
            cap.ForeColor = f.IsDisabled ? UiTheme.ColorDisabled : UiTheme.ColorTextSecondary;
            cap.Font = UiTheme.FontBody;
            cap.AutoSize = true;
            cap.Anchor = AnchorStyles.Left;
            cap.Margin = new Padding(2, 4, 2, 0);

            if (!string.IsNullOrEmpty(f.Hint))
                toolTip.SetToolTip(cap, f.Hint);

            grid.Controls.Add(cap, 0, grid.RowCount);
        }

        /// <summary>
        /// 按字段的 <see cref="NodeParamField.DisabledReason"/> 把控件置为禁用态。
        /// <para>
        /// 【为什么用 <c>Enabled = false</c> 而不是 <c>ReadOnly = true</c>】<c>NumericUpDown</c>
        /// 的 <c>ReadOnly</c> 只挡键盘输入，上下箭头照样能改值 —— 那会变成"看起来只读、其实点得动"，
        /// 比不禁用更难查。禁用态则连箭头带文本一起变灰，且 WinForms 不会给禁用控件派发事件。
        /// </para>
        /// <para>tooltip 换成禁用原因（而不是原来的 Hint）：用户最想知道的是"为什么不能改、什么时候能改"。</para>
        /// </summary>
        private void ApplyDisabled(Control control, NodeParamField f)
        {
            if (control == null || f == null) return;

            if (!f.IsDisabled)
            {
                // 可用控件也要挂 Hint：本次新增的「显示合并字符串框」「显示区域框」这类开关，
                // 光看标题看不出"按行归并 / 只是标出识别区域"，鼠标停上去要说清楚。
                if (!string.IsNullOrEmpty(f.Hint)) toolTip.SetToolTip(control, f.Hint);
                return;
            }

            control.Enabled = false;
            toolTip.SetToolTip(control, f.DisabledReason);
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

                case NodeParamKind.Folder:
                {
                    // 与 File 行的结构一致（文本框 + 两个按钮），但浏览走 FolderBrowserDialog：
                    // 目录场景用 OpenFileDialog 会被图片过滤器误导（用户会以为"只能选文件"）。
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

                    // 两个按钮都需要拿到文本框：用 Tag 传（与 File 行同款约定）
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
                    // 与 File 行的「浏览」按钮同源：同样的 UiButton、同样的取色来源。
                    // UiButton 是自绘控件（在 UiStyle 的跳过名单里），颜色在 OnPaint 里
                    // 每次从 UiTheme 现取，因此深 / 浅色切换后无需重新赋值即可正确显示。
                    var act = new UiButton();
                    act.Kind = UiButtonKind.Neutral;
                    act.Text = f.Label;
                    act.Dock = DockStyle.Fill;
                    act.Height = 28;
                    act.MinimumSize = new Size(80, 28);
                    act.Margin = new Padding(2, 2, 2, 2);
                    act.Tag = f;
                    act.Click += OnActionClick;

                    // 禁用态不在这里做：统一由 ApplyDisabled 处理（见 Build 里的调用），
                    // 这样 Int / Dbl / Bool / Action 走同一条路，不必每个分支各写一遍。
                    if (!string.IsNullOrEmpty(f.Hint)) toolTip.SetToolTip(act, f.Hint);

                    // 刻意不进 _inputs：动作行没有参数值，不能参与 LoadValues / 写回节点
                    return act;
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
                        case NodeParamKind.Folder:      // 目录行也是一个 TextBox（另配两个按钮）
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

        /// <summary>「浏览」选目录：把选中目录写进文本框（触发 TextChanged → 写回节点 + ParamsChanged）。</summary>
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

                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                txt.Text = dlg.SelectedPath;   // 触发 TextChanged → 写入节点并抛出 ParamsChanged
            }
        }

        /// <summary>「打开」：用资源管理器打开该目录（目录不存在时先建出来，方便用户放文件）。</summary>
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
                MessageBox.Show(this, "无法打开目录：" + ex.Message, "模板目录",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>
        /// 把参数里的目录值解析成绝对路径。
        /// <para>参数语义是"相对程序运行目录的名字或相对路径"，而 <see cref="Path.Combine"/> 在
        /// 第二参是绝对路径时会直接返回它 —— 所以"相对/绝对"两种情况一条表达式即可覆盖。</para>
        /// </summary>
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

        /// <summary>
        /// 动作行按钮点击：把 <see cref="NodeParamField.Key"/> 抛给调用方（FlowForm 负责弹窗 / 进画布框选）。
        /// <para>没有绑定节点时不抛（此时面板处于空态，本不该有动作行）。</para>
        /// </summary>
        private void OnActionClick(object sender, EventArgs e)
        {
            if (_node == null) return;

            var c = sender as Control;
            if (c == null) return;

            NodeParamField f = c.Tag as NodeParamField;
            if (f == null || string.IsNullOrEmpty(f.Key)) return;

            Action<string> h = ActionInvoked;
            if (h != null) h(f.Key);
        }
    }
}
