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
    /// 流程编排右栏下部的「输出结果」面板：<strong>一个真正的表格</strong>（DataGridView）。
    /// <para>
    /// 【为什么从"自绘行"改成 DataGridView】自绘行只能拼出"表头 + 对齐文字"的样子，
    /// 没有列分隔、没有单元格边界、没有稳定滚动 —— 用户一句"你知道什么是表格吗"就说明白了。
    /// 表格的标准形态是：表头行、列线、隔行底色、整行选中、可拖动列宽。DataGridView 一次给全，
    /// 主题由 <see cref="UiGrid"/> 统一套深色。
    /// </para>
    /// <para>
    /// 【表的语义】一行 = 一个目标（BLOB 的一个斑点 / 形状匹配的一个匹配 / OCR 的一个字符）。
    /// 节点级摘要（数量 / 最高分 / 耗时）不占表格行，放在表头上方的两行标题里 ——
    /// 摘要与明细混在同一张表里，正是之前"12 条结果看起来是 13 行等价文字"的病根。
    /// </para>
    /// </summary>
    public sealed class FlowResultPanel : UserControl
    {
        /// <summary>最多显示的目标行数（数据侧已截断到 <see cref="NodeResultCache.MaxShapes"/>，这里再兜一层）。</summary>
        private const int MaxDetailRows = 200;

        /// <summary>同一节点缩略帧的重建最小间隔（毫秒）：连续运行时每帧重建会把 UI 拖垮。</summary>
        private const int ThumbRebuildIntervalMs = 400;

        // ---- 顶条：节点名 + 状态 + 耗时 / 一行主判据 ----
        private Panel panelHead;
        private Label lblNode;
        private Label lblBadge;
        private Label lblTime;
        private Label lblSummary;

        // ---- 表格 ----
        private DataGridView grid;

        // ---- 底条：整链结论（只在跑完整链时出现）----
        private Panel panelFoot;
        private Label lblVerdict;
        private Label lblChain;

        private UiVerdict _verdict = UiVerdict.None;
        private string _chainText = "";

        /// <summary>填表期间为 true：抑制 SelectionChanged 回灌（否则每帧刷新都会触发一次"选中"事件）。</summary>
        private bool _filling;

        // 结果缓存：key = 节点 Id。换流程清空、重跑按节点覆盖、切节点只读。
        private readonly Dictionary<string, NodeResultCache> _cache =
            new Dictionary<string, NodeResultCache>(StringComparer.Ordinal);

        // 当前显示的节点（"" = 跟随最新快照）
        private string _shownNodeId = "";
        private string _shownNodeName = "";

        private Func<Bitmap> _frameProvider;

        // 缩略帧只给"被点开过"的节点抓（每帧给 5 个节点降采样 5MP 会吃掉刷新预算）
        private readonly HashSet<string> _openedNodes = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, DateTime> _thumbAt =
            new Dictionary<string, DateTime>(StringComparer.Ordinal);

        /// <summary>点击表格某行时触发（节点 Id、该节点 Shapes 内的图形序号）；序号 &lt; 0 表示取消高亮。</summary>
        public event Action<string, int> ShapeSelected;

        public FlowResultPanel()
        {
            BuildUi();
            ApplyTheme();
            Clear();
        }

        // =====================================================================
        //  对外只读状态（宿主与探针用）
        // =====================================================================

        /// <summary>当前显示的节点 Id（"" = 跟随最新）。</summary>
        public string ShownNodeId { get { return _shownNodeId; } }

        /// <summary>当前表格中的数据行数（含"没有目标"/"仅显示前 N 条"这类说明行）。</summary>
        public int DetailRowCount
        {
            get { return grid != null ? grid.Rows.Count : 0; }
        }

        /// <summary>表格可视宽度（探针用）。</summary>
        public int RowWidth
        {
            get { return grid != null ? grid.ClientSize.Width : 0; }
        }

        /// <summary>表格是否出现横向滚动条（列被拖宽后才会）。</summary>
        public bool HasHorizontalScroll
        {
            get
            {
                if (grid == null) return false;
                // DataGridView.HorizontalScrollBar 是 protected，只能从子控件里找
                foreach (Control c in grid.Controls)
                {
                    var h = c as HScrollBar;
                    if (h != null) return h.Visible;
                }
                return false;
            }
        }

        /// <summary>取某节点的缓存（没有则 null）。</summary>
        public NodeResultCache GetCache(string nodeId)
        {
            NodeResultCache c;
            return (nodeId != null && _cache.TryGetValue(nodeId, out c)) ? c : null;
        }

        /// <summary>注入"当前帧"提供者（生成缩略帧用；返回的位图<strong>不转移所有权</strong>）。</summary>
        public void SetFrameProvider(Func<Bitmap> provider)
        {
            _frameProvider = provider;
        }

        // =====================================================================
        //  界面构建（纯代码）
        // =====================================================================

        private void BuildUi()
        {
            SuspendLayout();

            // ---------------- 顶条（两行，共 46px）----------------
            // 第一行：节点名 · 第 i/n 个 ｜ 状态 ｜ 耗时
            // 第二行：主判据（数量 / 最高分 / 最大面积…）
            panelHead = new Panel();
            panelHead.Dock = DockStyle.Top;
            panelHead.Height = 46;
            panelHead.Padding = new Padding(6, 2, 6, 0);

            // 两行显式排布（不用 Dock）：第一行 名称｜状态｜耗时，第二行 主判据。
            // Dock=Fill 的 Label 会铺满 46px，再与"固定 y=24 的判据行"重叠 —— 名字被压掉半行就是这个原因。
            lblNode = MakeLabel(UiTheme.FontGroupTitle, ContentAlignment.MiddleLeft);
            lblNode.AutoEllipsis = true;

            lblTime = MakeLabel(UiTheme.FontSmall, ContentAlignment.MiddleRight);
            lblTime.AutoEllipsis = false;

            lblBadge = MakeLabel(UiTheme.FontGroupTitle, ContentAlignment.MiddleCenter);

            lblSummary = MakeLabel(UiTheme.FontBody, ContentAlignment.MiddleLeft);
            lblSummary.AutoEllipsis = true;

            panelHead.Controls.Add(lblNode);
            panelHead.Controls.Add(lblSummary);
            panelHead.Controls.Add(lblBadge);
            panelHead.Controls.Add(lblTime);
            panelHead.Resize += (s2, e2) => LayoutHead();

            // ---------------- 表格 ----------------
            grid = new DataGridView();
            grid.Dock = DockStyle.Fill;
            grid.ReadOnly = true;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToOrderColumns = false;
            grid.AllowUserToResizeRows = false;
            grid.RowHeadersVisible = false;
            grid.MultiSelect = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.ScrollBars = ScrollBars.Vertical;
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            grid.ColumnHeadersHeight = 26;
            grid.RowTemplate.Height = 24;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.EditMode = DataGridViewEditMode.EditProgrammatically;

            var colNo = new DataGridViewTextBoxColumn();
            colNo.HeaderText = "序号";
            colNo.Name = "colNo";
            colNo.FillWeight = 16f;
            colNo.MinimumWidth = 44;
            colNo.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;

            var colMetric = new DataGridViewTextBoxColumn();
            colMetric.HeaderText = "判据";
            colMetric.Name = "colMetric";
            colMetric.FillWeight = 34f;
            colMetric.MinimumWidth = 70;

            var colGeo = new DataGridViewTextBoxColumn();
            colGeo.HeaderText = "位置 · 尺寸";
            colGeo.Name = "colGeo";
            colGeo.FillWeight = 50f;
            colGeo.MinimumWidth = 90;

            grid.Columns.Add(colNo);
            grid.Columns.Add(colMetric);
            grid.Columns.Add(colGeo);

            // 选中行 → 高亮对应图形（表格里一行就是一个目标，天然对应 Shapes 下标）
            grid.SelectionChanged += OnGridSelectionChanged;

            // ---------------- 底条：整链结论 ----------------
            panelFoot = new Panel();
            panelFoot.Dock = DockStyle.Bottom;
            panelFoot.Height = 26;
            panelFoot.Padding = new Padding(6, 0, 6, 0);
            panelFoot.Visible = false;

            lblVerdict = new Label();
            lblVerdict.Dock = DockStyle.Left;
            lblVerdict.Width = 44;
            lblVerdict.AutoSize = false;

            lblChain = MakeLabel(UiTheme.FontBody, ContentAlignment.MiddleLeft);
            lblChain.Dock = DockStyle.Fill;
            lblChain.AutoEllipsis = true;

            panelFoot.Controls.Add(lblChain);
            panelFoot.Controls.Add(lblVerdict);

            // 逆序停靠：grid(Fill) → panelFoot(Bottom) → panelHead(Top)
            Controls.Add(grid);
            Controls.Add(panelFoot);
            Controls.Add(panelHead);

            ResumeLayout(false);
        }

        /// <summary>
        /// 顶条两行的显式排布：右起 耗时(66) → 状态(46) → 名称占其余；第二行整宽放主判据。
        /// </summary>
        private void LayoutHead()
        {
            if (panelHead == null) return;

            int w = panelHead.ClientSize.Width;
            if (w <= 0) return;

            const int pad = 6;
            const int timeW = 66;
            const int badgeW = 46;
            const int lineH = 22;

            int timeLeft = Math.Max(pad, w - pad - timeW);
            int badgeLeft = Math.Max(pad + 60, timeLeft - badgeW - 4);
            int nameW = Math.Max(40, badgeLeft - pad - 4);

            lblNode.SetBounds(pad, 2, nameW, lineH);
            lblBadge.SetBounds(badgeLeft, 2, badgeW, lineH);
            lblTime.SetBounds(timeLeft, 2, timeW, lineH);
            lblSummary.SetBounds(pad, lineH + 2, Math.Max(60, w - pad * 2), 20);
        }

        /// <summary>节点状态 → 语义色（与画布/预览同一套：OK 绿、NG 红、警告橙、未运行灰）。</summary>
        private static Color StatusColor(FlowNodeStatus status)
        {
            switch (status)
            {
                case FlowNodeStatus.Ok: return UiTheme.ColorOk;
                case FlowNodeStatus.Ng: return UiTheme.ColorNg;
                case FlowNodeStatus.Warn: return UiTheme.ColorWarn;
                default: return UiTheme.ColorTextSecondary;
            }
        }

        private static Label MakeLabel(Font font, ContentAlignment align)
        {
            var lbl = new Label();
            lbl.AutoSize = false;
            lbl.Font = font;
            lbl.TextAlign = align;
            lbl.BackColor = Color.Transparent;
            lbl.ForeColor = UiTheme.ColorTextSecondary;
            return lbl;
        }

        /// <summary>运行时重套主题（切换深浅色板时由 <see cref="FlowForm"/> 调用）。</summary>
        public void ApplyTheme()
        {
            BackColor = UiTheme.ColorPanelBg;
            ForeColor = UiTheme.ColorTextPrimary;

            panelHead.BackColor = UiTheme.ColorHeaderBg;
            panelHead.Padding = new Padding(6, 2, 6, 0);

            lblNode.ForeColor = UiTheme.ColorTextPrimary;
            lblNode.Font = UiTheme.FontGroupTitle;

            lblSummary.ForeColor = UiTheme.ColorTextSecondary;
            lblSummary.Font = UiTheme.FontBody;

            lblTime.ForeColor = UiTheme.ColorTextSecondary;
            lblTime.Font = UiTheme.FontSmall;

            if (grid != null)
            {
                UiGrid.Apply(grid);
                // UiGrid 的默认行高/表头高是给"通讯端点"那种大表用的，这里要紧凑一档
                grid.ColumnHeadersHeight = 26;
                grid.RowTemplate.Height = 24;
                grid.ColumnHeadersDefaultCellStyle.Font = UiTheme.FontSmall;
                grid.DefaultCellStyle.Font = UiTheme.FontSmall;
                grid.AlternatingRowsDefaultCellStyle.Font = UiTheme.FontSmall;
                grid.DefaultCellStyle.Padding = new Padding(4, 0, 4, 0);
                grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(4, 0, 4, 0);
                grid.GridColor = UiTheme.ColorDivider;
            }

            if (panelFoot != null) panelFoot.BackColor = UiTheme.ColorPanelBg;
            lblChain.ForeColor = UiTheme.ColorTextSecondary;
            lblChain.Font = UiTheme.FontBody;

            // 结论徽标的语义色必须最后覆盖（UiStyle 会把普通 Label 统一成主文本色）
            UiTheme.StyleBadge(lblVerdict, _verdict);
            lblChain.Text = _chainText;

            LayoutHead();
            Invalidate(true);
        }

        // =====================================================================
        //  数据
        // =====================================================================

        /// <summary>
        /// 吃进一个节点快照：更新缓存（每节点只留最近一次），若当前正在显示该节点则刷新表格。
        /// <para>缩略帧按"每节点每 400ms 最多重建一次"节流：连续运行时每帧都重建会把 UI 拖垮。</para>
        /// </summary>
        public void Apply(FlowProgressSnapshot snap, FlowNodeType type = FlowNodeType.ResultOutput)
        {
            if (snap == null) { Clear(); return; }

            NodeResultCache c = GetCache(snap.NodeId);
            bool isNew = c == null;
            if (c == null)
            {
                c = new NodeResultCache();
                if (!string.IsNullOrEmpty(snap.NodeId)) _cache[snap.NodeId] = c;
            }

            c.NodeId = snap.NodeId ?? "";
            c.NodeName = snap.NodeName ?? "";
            c.Type = type;
            c.Index = snap.Index;
            c.Total = snap.Total;
            c.Status = snap.Status;
            c.ElapsedMs = snap.ElapsedMs;
            c.Summary = snap.Summary ?? "";
            c.RunAt = DateTime.Now;
            if (snap.Metrics != null) c.Metrics = snap.Metrics;

            // 辅助图形：只用于画布绘制（轮廓开关），永不进表格
            c.AuxShapes = (snap.AuxShapes != null)
                ? new List<IGeometryShape>(snap.AuxShapes)
                : new List<IGeometryShape>();

            // 图形：拷贝 + 截断（不能持有会话里那个列表的引用，它会被会话释放或复用）
            var copy = new List<IGeometryShape>();
            bool truncated = false;
            if (snap.Shapes != null)
            {
                foreach (IGeometryShape s in snap.Shapes)
                {
                    if (s == null) continue;
                    if (copy.Count >= NodeResultCache.MaxShapes) { truncated = true; break; }
                    copy.Add(s);
                }
            }
            c.Shapes = copy;
            c.ShapesTruncated = truncated;

            TryUpdateThumbnail(c, isNew);

            if (string.IsNullOrEmpty(_shownNodeId))
            {
                _shownNodeId = c.NodeId;
                _shownNodeName = c.NodeName;
                Render();
            }
            else if (string.Equals(_shownNodeId, c.NodeId, StringComparison.Ordinal))
            {
                Render();
            }

            if (snap.IsChainEnd) ApplyVerdict(snap);
        }

        /// <summary>只刷新底部整链结论（"看的是某个节点，但整链跑完了"时用）。</summary>
        public void ApplyVerdict(FlowProgressSnapshot snap)
        {
            if (snap == null) return;

            _verdict = snap.Passed ? UiVerdict.Ok : UiVerdict.Ng;
            _chainText = OneLine(snap.ChainSummary);

            UiTheme.StyleBadge(lblVerdict, _verdict);
            lblChain.Text = _chainText;

            // 有整链结论才占位（26px），没跑完整链时这块高度全部让给表格
            if (panelFoot != null)
                panelFoot.Visible = !string.IsNullOrEmpty(_chainText) || _verdict != UiVerdict.None;
        }

        /// <summary>切换到某个节点：直接从缓存回填（先清再填会闪一次空白）。</summary>
        public void ShowNode(string nodeId, string nodeName)
        {
            _shownNodeId = nodeId ?? "";
            _shownNodeName = nodeName ?? "";

            if (!string.IsNullOrEmpty(_shownNodeId)) _openedNodes.Add(_shownNodeId);

            Render();
        }

        /// <summary>清空全部内容与缓存（换流程时调用）。</summary>
        public void Clear()
        {
            foreach (NodeResultCache c in _cache.Values)
            {
                if (c != null) c.ReleaseThumbnail();
            }
            _cache.Clear();
            _thumbAt.Clear();
            _openedNodes.Clear();

            _shownNodeId = "";
            _shownNodeName = "";

            if (lblNode != null) lblNode.Text = "等待运行…";
            if (lblSummary != null) lblSummary.Text = "";
            if (lblBadge != null) { lblBadge.Text = ""; lblBadge.ForeColor = UiTheme.ColorTextSecondary; }
            if (lblTime != null) lblTime.Text = "";
            if (panelFoot != null) panelFoot.Visible = false;

            if (grid != null)
            {
                _filling = true;
                grid.Rows.Clear();
                _filling = false;
            }

            _verdict = UiVerdict.None;
            _chainText = "";
            if (lblVerdict != null) UiTheme.StyleBadge(lblVerdict, _verdict);
            if (lblChain != null) lblChain.Text = "";

            FireShapeSelected("", -1);
        }

        // =====================================================================
        //  渲染
        // =====================================================================

        private void Render()
        {
            NodeResultCache c = GetCache(_shownNodeId);

            if (c == null)
            {
                lblNode.Text = string.IsNullOrEmpty(_shownNodeName)
                    ? "等待运行…"
                    : _shownNodeName + "　尚未运行";
                lblSummary.Text = "";
                lblBadge.Text = "";
                lblTime.Text = "";
                FillGrid(null);
                return;
            }

            lblNode.Text = string.Format(CultureInfo.InvariantCulture,
                "{0}　第 {1}/{2} 个", c.NodeName, c.Index, c.Total);

            // 耗时单独占右侧一列，不挤在判据里（判据那行要留给数字）
            lblTime.Text = c.ElapsedMs >= 0
                ? c.ElapsedMs.ToString(CultureInfo.InvariantCulture) + " ms"
                : "";

            lblBadge.Text = NodeResultText.Badge(c.Status);
            lblBadge.ForeColor = StatusColor(c.Status);

            lblSummary.Text = NodeResultText.Primary(c.Type, c.Metrics, -1, c.Summary);

            FillGrid(c);
        }

        /// <summary>
        /// 把"当前节点的全部目标"填进表格：一行一个目标，三列（序号 / 判据 / 位置·尺寸）。
        /// <para>填表期间置 <see cref="_filling"/>，避免 SelectionChanged 回灌出假的"选中"事件。</para>
        /// </summary>
        private void FillGrid(NodeResultCache c)
        {
            if (grid == null) return;

            _filling = true;
            grid.SuspendLayout();
            try
            {
                grid.Rows.Clear();

                if (c == null)
                {
                    grid.Rows.Add("", "该节点尚未运行", "");
                    return;
                }

                int total = (c.Shapes != null) ? c.Shapes.Count : 0;
                if (total == 0)
                {
                    grid.Rows.Add("", "该节点没有可定位的目标", "");
                    return;
                }

                int limit = Math.Min(total, MaxDetailRows);
                for (int i = 0; i < limit; i++)
                {
                    string metric, geo;
                    DescribeShapeParts(c.Shapes[i], out metric, out geo);
                    grid.Rows.Add((i + 1).ToString(CultureInfo.InvariantCulture), metric, geo);
                }

                if (total > limit || c.ShapesTruncated)
                    grid.Rows.Add("", "（共 " + total + " 条，仅显示前 " + limit + " 条）", "");
            }
            finally
            {
                grid.ResumeLayout();
                _filling = false;
            }
        }

        /// <summary>
        /// 表格选中行变化 → 高亮预览里对应的那个目标。
        /// <para>说明行（"没有目标"/"仅显示前 N 条"）没有对应图形，落到 -1 取消高亮。</para>
        /// </summary>
        private void OnGridSelectionChanged(object sender, EventArgs e)
        {
            if (_filling || grid == null) return;

            int idx = grid.CurrentRow != null ? grid.CurrentRow.Index : -1;
            NodeResultCache c = GetCache(_shownNodeId);
            int total = (c != null && c.Shapes != null) ? c.Shapes.Count : 0;

            if (idx < 0 || idx >= total)
            {
                FireShapeSelected("", -1);
                return;
            }

            FireShapeSelected(c.NodeId, idx);
        }

        private void FireShapeSelected(string nodeId, int shapeIndex)
        {
            Action<string, int> h = ShapeSelected;
            if (h != null) h(nodeId, shapeIndex);
        }

        // =====================================================================
        //  缩略帧
        // =====================================================================

        private void TryUpdateThumbnail(NodeResultCache c, bool isNew)
        {
            if (c == null || _frameProvider == null) return;

            // 按需生成：只有被点开过的节点才抓（否则每帧 5 个节点 × 5MP 降采样，超预算）
            if (string.IsNullOrEmpty(c.NodeId) || !_openedNodes.Contains(c.NodeId)) return;

            DateTime last;
            bool hasAt = _thumbAt.TryGetValue(c.NodeId ?? "", out last);
            bool due = isNew || c.Thumbnail == null || !hasAt
                       || (DateTime.Now - last).TotalMilliseconds >= ThumbRebuildIntervalMs;
            if (!due) return;

            Bitmap frame = null;
            try
            {
                frame = _frameProvider();
            }
            catch (Exception)
            {
                frame = null;   // 取帧失败不影响结果区（缩略帧只是"锦上添花"）
            }
            if (frame == null) return;

            try
            {
                Bitmap thumb = ThumbGeometry.BuildThumbnail(frame);
                if (thumb == null) return;

                c.ReleaseThumbnail();
                c.Thumbnail = thumb;
                c.ThumbDivisor = ThumbGeometry.ThumbScale;
                c.SourceWidth = frame.Width;
                c.SourceHeight = frame.Height;
                _thumbAt[c.NodeId ?? ""] = DateTime.Now;
            }
            catch (Exception)
            {
                // 缩略帧失败不影响主流程；下一次快照会重试
            }
        }

        // =====================================================================
        //  取数辅助
        // =====================================================================

        /// <summary>
        /// 表格两列的内容：<c>判据</c>（分数 / 面积·圆度…）与 <c>位置·尺寸</c>。
        /// 序号由表格的"序号"列给，标签里重复的 <c>#1</c> 前缀会被去掉。
        /// </summary>
        private static void DescribeShapeParts(IGeometryShape s, out string metric, out string geo)
        {
            metric = "";
            geo = "";
            if (s == null) return;

            var rect = s as RectangleShape;
            if (rect != null)
            {
                metric = StripIndexPrefix(rect.Label);
                geo = string.Format(CultureInfo.InvariantCulture, "({0:0},{1:0}) {2:0}×{3:0}",
                    rect.X, rect.Y, rect.Width, rect.Height);
                return;
            }

            var rr = s as RotatedRectShape;
            if (rr != null)
            {
                metric = StripIndexPrefix(rr.Label);
                geo = string.Format(CultureInfo.InvariantCulture, "({0:0},{1:0}) {2:0}×{3:0} {4:0.0}°",
                    rr.Center.X, rr.Center.Y, rr.Width, rr.Height, rr.AngleDeg);
                return;
            }

            var line = s as LineSegmentShape;
            if (line != null)
            {
                geo = string.Format(CultureInfo.InvariantCulture, "({0:0},{1:0})→({2:0},{3:0})",
                    line.P1.X, line.P1.Y, line.P2.X, line.P2.Y);
                return;
            }

            var circle = s as CircleShape;
            if (circle != null)
            {
                geo = string.Format(CultureInfo.InvariantCulture, "C=({0:0},{1:0}) R={2:0}",
                    circle.Center.X, circle.Center.Y, circle.Radius);
                return;
            }

            geo = s.ToGeometryString();
        }

        /// <summary>去掉标签开头的 <c>#1 </c> 前缀（序号已经有独立一列）。</summary>
        private static string StripIndexPrefix(string label)
        {
            if (string.IsNullOrEmpty(label)) return "";

            if (label[0] != '#') return label;

            int sp = label.IndexOf(' ');
            return (sp > 0 && sp < label.Length - 1) ? label.Substring(sp + 1) : label;
        }

        /// <summary>摘要压成一行。</summary>
        private static string OneLine(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";

            string s = text.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');
            while (s.Contains("  ")) s = s.Replace("  ", " ");
            return s.Trim();
        }

        // =====================================================================
        //  探针支撑（SmokeUi 不点鼠标也能走完交互断言）
        // =====================================================================

        /// <summary>
        /// 兼容旧探针：表格化之后没有"展开/收起"，本方法等价于"点第一行"。
        /// <para>
        /// 【为什么直接发事件、不靠 grid 的 SelectionChanged】探针跑在**没有句柄**的面板上，
        /// DataGridView 在无句柄时不派发选中事件 —— 靠事件回灌会让探针永远收不到东西
        /// （表现为"点了没反应"，而真实界面是好的）。这里把"选中 → 发事件"显式走一遍。
        /// </para>
        /// </summary>
        public void SimulateMainRowClick()
        {
            SimulateDetailRowClick(0);
        }

        /// <summary>模拟点击表格第 <paramref name="index"/> 行（从 0 起，仅测试用）。</summary>
        public void SimulateDetailRowClick(int index)
        {
            NodeResultCache c = GetCache(_shownNodeId);
            int total = (c != null && c.Shapes != null) ? c.Shapes.Count : 0;

            // 改选中态时临时压制 SelectionChanged，否则会与本方法显式发的事件重复
            if (grid != null && index >= 0 && index < grid.Rows.Count)
            {
                _filling = true;
                try { grid.CurrentCell = grid.Rows[index].Cells[0]; }
                finally { _filling = false; }
            }

            if (index < 0 || index >= total)
            {
                FireShapeSelected("", -1);
                return;
            }

            FireShapeSelected(c.NodeId, index);
        }

        /// <summary>取表格单元格文本（探针用；越界返回 ""）。</summary>
        public string GridCellText(int rowIndex, int columnIndex)
        {
            if (grid == null || rowIndex < 0 || rowIndex >= grid.Rows.Count) return "";
            if (columnIndex < 0 || columnIndex >= grid.Columns.Count) return "";
            object v = grid.Rows[rowIndex].Cells[columnIndex].Value;
            return v == null ? "" : v.ToString();
        }
    }
}
