using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using VisionPlatform.Flow;
using VisionSystem.Controls.Geometry;

namespace VisionPlatform.UI
{
    /// <summary>
    /// 流程编辑右栏「输出结果」面板：位于「编辑输入参数」下方，运行期实时显示当前节点的
    /// 状态 / 耗时 / 摘要与其产出的每一个图形（表格形式），点击某行会在预览图像里高亮对应图形。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>【为什么不用设计器】本项目历史上出现过 VS 重序列化把事件订阅弄丢的事故
    /// （见 <c>FlowPreviewPanel.cs</c> 中 <c>pullTimer.Tick</c> 的注释）。本控件<strong>全部纯代码
    /// 构建、不使用设计器序列化</strong>，也就不需要 <c>FlowResultPanel.Designer.cs</c>。</item>
    /// <item>【为什么是普通 UserControl 而不是自绘】自绘控件要同步维护三份白名单
    /// （<c>UiStyle.IsSelfDrawn</c>、<c>Core/Lang.cs</c> 的第二份拷贝、<c>UiThemeManager.ReapplySelfDrawn</c>）。
    /// 本控件走普通控件路径，由 <c>UiStyle</c> / <c>Lang</c> 自动遍历，配色仍显式套一次
    /// <see cref="UiGrid.Apply"/> 与 <see cref="UiTheme"/> 保证一致。</item>
    /// <item>【取数为什么通用】行直接来自 <see cref="FlowProgressSnapshot.Shapes"/>，
    /// 不按节点类型分支，因此新增节点无需改动本类。</item>
    /// </list>
    /// </remarks>
    public sealed class FlowResultPanel : UserControl
    {
        private Panel panelHead;
        private Label lblTitle;
        private Label lblNode;
        private DataGridView grid;
        private Panel panelFoot;
        private Label lblVerdict;
        private Label lblChain;

        private bool _suppressSelection;   // 抑制 Rows.Clear() 触发的 SelectionChanged
        private UiVerdict _verdict = UiVerdict.None;
        private string _chainText = "";

        // 上一次 Apply 的快照节点 Id。图形每运行周期都是新实例，高亮只能靠「节点 Id + 序号」定位。
        private string _nodeId = "";
        // 与 grid.Rows 逐行对齐的「图形序号」：图形行记它在 snap.Shapes 内的序号，非图形行（节点/状态/摘要）记 -1。
        // 表格里混着节点级行，行号 ≠ 图形序号，故必须单独记一份。
        private readonly List<int> _rowShapeIndex = new List<int>();

        /// <summary>
        /// 点击结果行时触发；参数为（节点 Id、该节点 Shapes 列表内的图形序号）。
        /// 序号 &lt; 0 或节点 Id 为空表示取消高亮。
        /// </summary>
        public event Action<string, int> ShapeSelected;

        public FlowResultPanel()
        {
            BuildUi();
            ApplyTheme();
            Clear();
        }

        // =====================================================================
        //  界面构建（纯代码）
        // =====================================================================

        private void BuildUi()
        {
            SuspendLayout();

            // ---- 顶条：标题 + 当前节点 / 状态 / 耗时 ----
            panelHead = new Panel();
            panelHead.Dock = DockStyle.Top;
            panelHead.Height = 26;
            panelHead.Padding = new Padding(6, 0, 6, 0);

            lblTitle = new Label();
            lblTitle.Dock = DockStyle.Left;
            lblTitle.Width = 68;
            lblTitle.AutoSize = false;
            lblTitle.Text = "输出结果";
            lblTitle.TextAlign = ContentAlignment.MiddleLeft;

            lblNode = new Label();
            lblNode.Dock = DockStyle.Fill;
            lblNode.AutoEllipsis = true;
            lblNode.TextAlign = ContentAlignment.MiddleLeft;

            panelHead.Controls.Add(lblNode);    // 先加 → 占满剩余
            panelHead.Controls.Add(lblTitle);   // 后加 → 逆序停靠，排到最左

            // ---- 中部：结果表格 ----
            grid = new DataGridView();
            grid.Dock = DockStyle.Fill;
            grid.ReadOnly = true;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToOrderColumns = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.MultiSelect = false;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.ScrollBars = ScrollBars.Both;
            grid.CellClick += OnGridCellClick;
            grid.SelectionChanged += OnGridSelectionChanged;

            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colItem", HeaderText = "项目", FillWeight = 22f });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colValue", HeaderText = "值", FillWeight = 34f });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colX", HeaderText = "X", FillWeight = 11f });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colY", HeaderText = "Y", FillWeight = 11f });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colW", HeaderText = "宽", FillWeight = 11f });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colH", HeaderText = "高", FillWeight = 11f });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colRemark", HeaderText = "备注", FillWeight = 18f });

            // ---- 底条：整链结论 OK/NG + 链尾摘要 ----
            panelFoot = new Panel();
            panelFoot.Dock = DockStyle.Bottom;
            panelFoot.Height = 48;
            panelFoot.Padding = new Padding(6, 4, 6, 4);

            lblVerdict = new Label();
            lblVerdict.Dock = DockStyle.Left;
            lblVerdict.Width = 44;
            lblVerdict.AutoSize = false;

            lblChain = new Label();
            lblChain.Dock = DockStyle.Fill;
            lblChain.AutoEllipsis = true;
            lblChain.TextAlign = ContentAlignment.MiddleLeft;

            panelFoot.Controls.Add(lblChain);
            panelFoot.Controls.Add(lblVerdict);

            // 逆序停靠：panelHead(Top) → panelFoot(Bottom) → grid(Fill)
            Controls.Add(grid);
            Controls.Add(panelFoot);
            Controls.Add(panelHead);

            ResumeLayout(false);
        }

        /// <summary>运行时重套主题（切换深浅色板时由 <c>FlowForm.RefreshTheme</c> 调用）。</summary>
        public void ApplyTheme()
        {
            BackColor = UiTheme.ColorPanelBg;
            ForeColor = UiTheme.ColorTextPrimary;

            panelHead.BackColor = UiTheme.ColorHeaderBg;
            lblTitle.ForeColor = UiTheme.ColorTextSecondary;
            lblTitle.Font = UiTheme.FontGroupTitle;
            lblTitle.BackColor = UiTheme.ColorHeaderBg;
            lblNode.ForeColor = UiTheme.ColorTextPrimary;
            lblNode.Font = UiTheme.FontBody;
            lblNode.BackColor = UiTheme.ColorHeaderBg;

            panelFoot.BackColor = UiTheme.ColorPanelBg;
            lblChain.ForeColor = UiTheme.ColorTextSecondary;
            lblChain.Font = UiTheme.FontBody;
            lblChain.BackColor = UiTheme.ColorPanelBg;

            UiGrid.Apply(grid);

            // 右栏只有 ~288px 宽，7 列按 Fill 挤会把数值压成「12…」，改为固定列宽 + 横向滚动
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            grid.ScrollBars = ScrollBars.Both;
            ColumnWidth("colItem", 96);
            ColumnWidth("colValue", 150);
            ColumnWidth("colX", 60);
            ColumnWidth("colY", 60);
            ColumnWidth("colW", 58);
            ColumnWidth("colH", 58);
            ColumnWidth("colRemark", 90);

            // 结论徽标的语义色必须最后覆盖：UiStyle 遍历普通 Label 时会把前景色统一成主文本色
            UiTheme.StyleBadge(lblVerdict, _verdict);
            lblChain.Text = _chainText;
        }

        private void ColumnWidth(string name, int width)
        {
            DataGridViewColumn col = grid.Columns[name];
            if (col != null) col.Width = width;
        }

        // =====================================================================
        //  数据刷新
        // =====================================================================

        /// <summary>用一次节点快照重建结果行。节点级信息与图形级信息都进同一张表。</summary>
        public void Apply(FlowProgressSnapshot snap)
        {
            if (snap == null) { Clear(); return; }

            _nodeId = snap.NodeId ?? "";

            lblNode.Text = string.Format(CultureInfo.InvariantCulture,
                "第 {0}/{1} 个节点：{2}　{3}　{4} ms",
                snap.Index, snap.Total, snap.NodeName, StatusText(snap.Status), snap.ElapsedMs);

            BeginRebuild();
            try
            {
                _rowShapeIndex.Clear();

                AddRow(null, -1, "节点", snap.NodeName, "", "", "", "",
                       string.Format(CultureInfo.InvariantCulture, "第 {0}/{1} 个", snap.Index, snap.Total));
                AddRow(null, -1, "状态", StatusText(snap.Status), "", "", "", "",
                       snap.ElapsedMs + " ms");
                if (!string.IsNullOrEmpty(snap.Summary))
                    AddRow(null, -1, "摘要", OneLine(snap.Summary), "", "", "", "", "");

                int n = 0;
                if (snap.Shapes != null)
                {
                    foreach (IGeometryShape s in snap.Shapes)
                    {
                        if (s == null) continue;
                        // 序号从 0 起，与预览区加入视图的顺序一致（两边同样跳过 null）
                        RectangleF b = s.Bounds;
                        AddRow(s, n, "图形 " + (n + 1), ValueOf(s),
                               Num(b.X), Num(b.Y), Num(b.Width), Num(b.Height),
                               string.IsNullOrEmpty(s.Name) ? s.GetType().Name : s.Name);
                        n++;
                    }
                }

                if (n == 0)
                    AddRow(null, -1, "（该节点没有图形输出）", "", "", "", "", "", "");
            }
            finally
            {
                EndRebuild();
            }

            if (snap.IsChainEnd) ApplyVerdict(snap);
        }

        /// <summary>只刷新底部整链结论（用于「当前看的是某个节点，但整链跑完了」的情况）。</summary>
        public void ApplyVerdict(FlowProgressSnapshot snap)
        {
            if (snap == null) return;

            _verdict = snap.Passed ? UiVerdict.Ok : UiVerdict.Ng;
            _chainText = OneLine(snap.ChainSummary);

            UiTheme.StyleBadge(lblVerdict, _verdict);
            lblChain.Text = _chainText;
        }

        /// <summary>清空全部内容（换流程 / 换选中节点时调用）。</summary>
        public void Clear()
        {
            lblNode.Text = "等待运行…";

            BeginRebuild();
            try { grid.Rows.Clear(); _rowShapeIndex.Clear(); }
            finally { EndRebuild(); }

            _nodeId = "";

            _verdict = UiVerdict.None;
            _chainText = "";
            UiTheme.StyleBadge(lblVerdict, _verdict);
            lblChain.Text = "";

            FireShapeSelected("", -1);      // 换流程 / 换节点：旧高亮一并撤掉，避免残留在别的节点上
        }

        private void BeginRebuild()
        {
            _suppressSelection = true;
        }

        private void EndRebuild()
        {
            _suppressSelection = false;
        }

        /// <summary>
        /// 加一行。<paramref name="shape"/> 仍挂到 <c>Row.Tag</c>（点选当帧可立刻按实例高亮），
        /// <paramref name="shapeIndex"/> 另记一份到 <see cref="_rowShapeIndex"/>（非图形行传 -1）。
        /// </summary>
        private void AddRow(IGeometryShape shape, int shapeIndex, string item, string value,
                            string x, string y, string w, string h, string remark)
        {
            int i = grid.Rows.Add(item, value, x, y, w, h, remark);
            grid.Rows[i].Tag = shape;
            _rowShapeIndex.Add(shapeIndex);
        }

        // =====================================================================
        //  行 → 图形高亮
        // =====================================================================

        private void OnGridCellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e == null || e.RowIndex < 0 || e.RowIndex >= grid.Rows.Count) return;
            RaiseShapeSelected(e.RowIndex);
        }

        private void OnGridSelectionChanged(object sender, EventArgs e)
        {
            if (_suppressSelection) return;                 // Rows.Clear() / 重建期间不响应

            DataGridViewRow row = grid.CurrentRow;
            RaiseShapeSelected(row != null ? row.Index : -1);
        }

        /// <summary>
        /// 按行号发高亮请求：图形行发（节点 Id、图形序号），非图形行 / 空行发（""、-1）表示取消。
        /// </summary>
        private void RaiseShapeSelected(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= _rowShapeIndex.Count) { FireShapeSelected("", -1); return; }

            int shapeIndex = _rowShapeIndex[rowIndex];
            if (shapeIndex >= 0 && _nodeId.Length > 0) FireShapeSelected(_nodeId, shapeIndex);
            else FireShapeSelected("", -1);
        }

        private void FireShapeSelected(string nodeId, int shapeIndex)
        {
            Action<string, int> h = ShapeSelected;
            if (h != null) h(nodeId, shapeIndex);
        }

        // =====================================================================
        //  取数辅助
        // =====================================================================

        /// <summary>图形的「值」列：有标签（检测框 / 字符框）用标签，否则用几何串。</summary>
        private static string ValueOf(IGeometryShape s)
        {
            var rect = s as RectangleShape;
            if (rect != null && !string.IsNullOrEmpty(rect.Label)) return rect.Label;

            string geo = s.ToGeometryString();
            return string.IsNullOrEmpty(geo) ? (string.IsNullOrEmpty(s.Name) ? s.GetType().Name : s.Name) : geo;
        }

        private static string StatusText(FlowNodeStatus status)
        {
            switch (status)
            {
                case FlowNodeStatus.Running: return "运行中";
                case FlowNodeStatus.Ok: return "OK";
                case FlowNodeStatus.Ng: return "NG";
                case FlowNodeStatus.Skipped: return "跳过";
                default: return "未执行";
            }
        }

        private static string Num(float v)
        {
            return v.ToString("F2", CultureInfo.InvariantCulture);
        }

        /// <summary>摘要压成一行（表格单元格不显示换行）。</summary>
        private static string OneLine(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";

            string s = text.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');
            while (s.Contains("  ")) s = s.Replace("  ", " ");
            return s.Trim();
        }
    }
}
