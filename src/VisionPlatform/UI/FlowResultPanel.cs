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
    /// 流程编辑右栏「输出结果」面板：按节点缓存最近一次运行结果，一节点一行 + 可展开明细，
    /// 点击行在预览图像里高亮对应图形。
    /// <para>
    /// 【为什么从 DataGridView 改成 FlowLayoutPanel + 行控件】旧表 7 列合计 572px，而右栏内容宽只有
    /// 288px，看 X/Y/宽高必须横向拖；且表格行高统一，做不到"主行 36 / 明细 22"的层级区分。
    /// </para>
    /// <para>
    /// 【为什么纯代码构建、不建 .Designer.cs】本项目历史上出现过 VS 重序列化把事件订阅吃掉的事故
    /// （DEF-01 就是这么坏的两个按钮）。本控件全部代码构建。
    /// </para>
    /// <para>
    /// 【为什么内部只用标准 Label】自绘控件要同步维护三份白名单，漏一处就"切主题后不自绘了"。
    /// 本控件与 <see cref="FlowResultRow"/> 都走普通控件路径。
    /// </para>
    /// </remarks>
    public sealed class FlowResultPanel : UserControl
    {
        /// <summary>明细行最多渲染条数（数据侧截断到 <see cref="NodeResultCache.MaxShapes"/>，界面再兜一层）。</summary>
        private const int MaxDetailRows = 200;

        /// <summary>同一节点缩略帧的重建最小间隔（毫秒）：连续运行时每帧重建会把 UI 拖垮。</summary>
        private const int ThumbRebuildIntervalMs = 400;

        private Panel panelHead;
        private Label lblNode;
        private UiButton btnZoom;
        private FlowLayoutPanel rows;
        private Panel panelFoot;
        private Label lblVerdict;
        private Label lblChain;

        private UiVerdict _verdict = UiVerdict.None;
        private string _chainText = "";

        // 结果缓存：key = 节点 Id。换流程清空、重跑按节点覆盖、切节点只读。
        private readonly Dictionary<string, NodeResultCache> _cache =
            new Dictionary<string, NodeResultCache>(StringComparer.Ordinal);

        // 当前显示的节点（"" = 跟随最新快照，用于"没选中节点时看链尾结果"）
        private string _shownNodeId = "";
        private string _shownNodeName = "";

        // 当前展开的节点（展开只对一个节点有效，避免多节点同时展开把面板撑爆）
        private string _expandedNodeId = "";
        private bool _zoomed;

        private Func<Bitmap> _frameProvider;

        // 被用户"点开过"的节点（ShowNode 调用过）。缩略帧只给这些节点抓：
        // 若每帧给每个节点都降采样一次 5MP，一周期要多做 5 次缩放，直接吃掉 16ms 刷新预算。
        private readonly HashSet<string> _openedNodes = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, DateTime> _thumbAt =
            new Dictionary<string, DateTime>(StringComparer.Ordinal);

        /// <summary>点击结果行时触发（节点 Id、该节点 Shapes 内的图形序号）；序号 &lt; 0 表示取消高亮。</summary>
        public event Action<string, int> ShapeSelected;

        /// <summary>点「放大 / 恢复」（UI-R10）；高度切换由宿主负责。</summary>
        public event EventHandler ZoomClicked;

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

        /// <summary>是否处于放大态。</summary>
        public bool IsZoomed { get { return _zoomed; } }

        /// <summary>当前渲染的明细行数。</summary>
        public int DetailRowCount
        {
            get
            {
                int n = 0;
                foreach (Control c in rows.Controls)
                {
                    var r = c as FlowResultRow;
                    if (r != null && r.Kind == FlowResultRowKind.Detail) n++;
                }
                return n;
            }
        }

        /// <summary>首行宽度（用于断言 288px 下不出横向滚动）。</summary>
        public int RowWidth
        {
            get { return rows.Controls.Count > 0 ? rows.Controls[0].Width : 0; }
        }

        /// <summary>行容器是否出现横向滚动条。</summary>
        public bool HasHorizontalScroll
        {
            get { return rows.HorizontalScroll.Visible && rows.HorizontalScroll.Maximum > 0; }
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

            // ---- 顶条：标题 + 当前节点 + 放大按钮 ----
            panelHead = new Panel();
            panelHead.Dock = DockStyle.Top;
            panelHead.Height = 26;
            panelHead.Padding = new Padding(6, 0, 6, 0);

            // 【为什么去掉「输出结果」四个字】右栏只有 300px，标题白吃 60px，
            // 挤得节点名只剩 100px、耗时被省略号截成 "7…"。这块区域本身有边框和底色，
            // 不需要标题自证身份 —— 宽度全部还给节点名。
            btnZoom = new UiButton();
            btnZoom.Dock = DockStyle.Right;
            btnZoom.Width = 46;
            btnZoom.Text = "放大";
            btnZoom.Kind = UiButtonKind.Neutral;
            btnZoom.Click += OnZoomClicked;

            lblNode = new Label();
            lblNode.Dock = DockStyle.Fill;
            lblNode.AutoEllipsis = true;
            lblNode.TextAlign = ContentAlignment.MiddleLeft;

            // 逆序停靠：先加 Fill，再加右侧按钮
            panelHead.Controls.Add(lblNode);
            panelHead.Controls.Add(btnZoom);

            // ---- 中部：行容器（一节点一行 + 展开明细）----
            rows = new FlowLayoutPanel();
            rows.Dock = DockStyle.Fill;
            rows.FlowDirection = FlowDirection.TopDown;
            rows.WrapContents = false;
            rows.AutoScroll = true;
            rows.Padding = new Padding(0);
            rows.Margin = new Padding(0);
            rows.Resize += OnRowsResize;

            // ---- 底条：整链结论 OK/NG + 链尾摘要 ----
            panelFoot = new Panel();
            panelFoot.Dock = DockStyle.Bottom;
            panelFoot.Height = 26;          // 单行即可；44px 在窄面板里白吃两条明细
            panelFoot.Padding = new Padding(6, 0, 6, 0);
            panelFoot.Visible = false;      // 没跑完整链之前不占地方（见 UpdateFoot）

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

            // 逆序停靠：rows(Fill) → panelFoot(Bottom) → panelHead(Top)
            Controls.Add(rows);
            Controls.Add(panelFoot);
            Controls.Add(panelHead);

            ResumeLayout(false);
        }

        /// <summary>运行时重套主题（切换深浅色板时由 <see cref="FlowForm"/> 调用）。</summary>
        public void ApplyTheme()
        {
            BackColor = UiTheme.ColorPanelBg;
            ForeColor = UiTheme.ColorTextPrimary;

            panelHead.BackColor = UiTheme.ColorHeaderBg;
            // 顶条只剩"节点名 + 放大"，配色统一取主文本色
            lblNode.ForeColor = UiTheme.ColorTextPrimary;
            lblNode.Font = UiTheme.FontGroupTitle;
            lblNode.ForeColor = UiTheme.ColorTextPrimary;
            lblNode.Font = UiTheme.FontBody;
            lblNode.BackColor = UiTheme.ColorHeaderBg;

            rows.BackColor = UiTheme.ColorPanelBg;

            panelFoot.BackColor = UiTheme.ColorPanelBg;
            lblChain.ForeColor = UiTheme.ColorTextSecondary;
            lblChain.Font = UiTheme.FontBody;
            lblChain.BackColor = UiTheme.ColorPanelBg;

            // 结论徽标的语义色必须最后覆盖：UiStyle 遍历普通 Label 时会把前景色统一成主文本色
            UiTheme.StyleBadge(lblVerdict, _verdict);
            lblChain.Text = _chainText;

            foreach (Control c in rows.Controls)
            {
                var row = c as FlowResultRow;
                if (row != null) row.ApplyTheme();
            }
        }

        /// <summary>切换放大态（宿主改完高度后调用；这里只更新按钮文案）。</summary>
        public void SetZoomed(bool zoomed)
        {
            _zoomed = zoomed;
            btnZoom.Text = zoomed ? "恢复" : "放大";
        }

        // =====================================================================
        //  数据
        // =====================================================================

        /// <summary>
        /// 吃进一个节点快照：更新缓存（每节点只留最近一次），若当前正在显示该节点则刷新界面。
        /// <para>缩略帧按"每节点每 400ms 最多重建一次"节流：连续运行时每帧都重建会把 UI 拖垮。</para>
        /// </summary>
        /// <param name="type">节点类型；决定主判据文案（T07 填好 Metrics 后此处才真正生效）。</param>
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

            // 辅助图形：只用于画布绘制（轮廓开关），永不进明细行
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

            // 显示规则：正在看这个节点 → 刷新；没选中节点（跟随最新）→ 切到最新快照的节点
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

            // 【为什么按需显示】右栏只有 300px 宽、结果区通常 240~300px 高，
            // 一条空底栏（26px）白吃一整条明细；没跑完整链之前不该占位。
            if (panelFoot != null)
                panelFoot.Visible = !string.IsNullOrEmpty(_chainText) || _verdict != UiVerdict.None;
        }

        /// <summary>
        /// 切换到某个节点：<strong>直接从缓存回填，不清空再填</strong>
        /// （先 Clear 再填会有一次可见的空白闪烁，PRD 5.2 明令禁止）。
        /// </summary>
        public void ShowNode(string nodeId, string nodeName)
        {
            _shownNodeId = nodeId ?? "";
            _shownNodeName = nodeName ?? "";

            // 记录"被点开过"：只有这些节点才值得为它抓一张缩略帧（见 _openedNodes 注释）
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
            _expandedNodeId = "";

            if (lblNode != null) lblNode.Text = "等待运行…";
            if (panelFoot != null) panelFoot.Visible = false;   // 底条只在有整链结论时出现

            if (rows != null)
            {
                while (rows.Controls.Count > 0)
                {
                    Control c = rows.Controls[0];
                    rows.Controls.RemoveAt(0);
                    c.Dispose();
                }
            }

            _verdict = UiVerdict.None;
            _chainText = "";
            if (lblVerdict != null) UiTheme.StyleBadge(lblVerdict, _verdict);
            if (lblChain != null) lblChain.Text = "";

            FireShapeSelected("", -1);      // 换流程：旧高亮一并撤掉
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
                RebuildRows(null);
                return;
            }

            // 顶条只放"这是谁"：节点名 + 第几个。耗时交给主行右端（那里每天都要看）
            lblNode.Text = string.Format(CultureInfo.InvariantCulture,
                "{0}　第 {1}/{2} 个", c.NodeName, c.Index, c.Total);

            RebuildRows(c);
        }

        private void RebuildRows(NodeResultCache c)
        {
            SuspendLayout();
            rows.SuspendLayout();

            while (rows.Controls.Count > 0)
            {
                Control old = rows.Controls[0];
                rows.Controls.RemoveAt(0);
                old.Dispose();
            }

            bool hasResult = c != null;
            bool expanded = c != null && !string.IsNullOrEmpty(_expandedNodeId)
                            && string.Equals(_expandedNodeId, c.NodeId, StringComparison.Ordinal);

            FlowResultRow main = FlowResultRow.CreateMain();
            main.RowClicked += OnMainRowClicked;

            // -1 = 不要耗时：耗时已在主行右端单独显示，判据行要留宽度给数字
            //（"12 个 · 最大面积 1842 px² · 圆度 0.86 · 7 ms" 在 288px 下会截掉最后两位）
            string primary = c != null
                ? NodeResultText.Primary(c.Type, c.Metrics, -1, c.Summary)
                : "";

            main.ApplyMain(c != null ? c.Index : 0,
                           c != null ? c.NodeName
                                     : (string.IsNullOrEmpty(_shownNodeName) ? "（未选择节点）" : _shownNodeName),
                           c != null ? c.Status : FlowNodeStatus.Pending,
                           primary,
                           c != null ? c.ElapsedMs : -1,
                           c != null ? c.RunAt : DateTime.MinValue,
                           expanded,
                           hasResult);
            rows.Controls.Add(main);

            if (expanded && c != null && c.Shapes != null && c.Shapes.Count > 0)
            {
                int limit = Math.Min(c.Shapes.Count, MaxDetailRows);
                for (int i = 0; i < limit; i++)
                {
                    FlowResultRow detail = FlowResultRow.CreateDetail(i);
                    detail.ApplyDetail(i + 1, DescribeShape(c.Shapes[i]));
                    detail.RowClicked += OnDetailRowClicked;
                    rows.Controls.Add(detail);
                }

                if (c.Shapes.Count > limit || c.ShapesTruncated)
                {
                    FlowResultRow more = FlowResultRow.CreateDetail(-1);
                    more.ApplyDetail(0, "（共 " + c.Shapes.Count + " 条，仅显示前 " + limit + " 条）");
                    rows.Controls.Add(more);
                }
            }
            else if (expanded && c != null)
            {
                FlowResultRow none = FlowResultRow.CreateDetail(-1);
                none.ApplyDetail(0, "该节点没有可定位的目标");
                rows.Controls.Add(none);
            }

            LayoutRows();

            rows.ResumeLayout(true);
            ResumeLayout(true);
        }

        private void OnRowsResize(object sender, EventArgs e)
        {
            LayoutRows();
        }

        /// <summary>
        /// 统一设置行宽：<strong>行宽 = 容器可用宽 - 竖直滚动条</strong>。
        /// 这是"288px 内容宽不出现横向滚动"的落地手段 —— 行从不比容器宽，自然没有横向滚动。
        /// </summary>
        private void LayoutRows()
        {
            if (rows == null) return;

            // 【为什么"总是"预留竖直滚动条宽度】若按 VerticalScroll.Visible 动态扣，
            // 会出现"先按无滚动条算宽 → 行变宽 → 撑出横向滚动条 → 宽度又变 → 滚动条消失"的来回抖动，
            // 结果就是横向滚动条一会儿有一会儿没有（探针里表现为 60 行时冒出来）。
            // 恒定预留 17px，代价是行略窄，收益是"永远不出现横向滚动"这个硬约束成立。
            int avail = rows.ClientSize.Width - SystemInformation.VerticalScrollBarWidth;
            if (avail < 60) avail = rows.ClientSize.Width;
            if (avail < 60) return;

            // 右端现在是"耗时"（约 5 个字符），不再是 8 位时间戳 ——
            // 门槛从 280 降到 210：300px 右栏（avail≈271）必须看得见耗时，
            // 否则"这节点跑了几毫秒"在主行上彻底消失（原来就是这样丢的）。
            bool showTime = avail >= 210;
            foreach (Control c in rows.Controls)
            {
                c.Width = Math.Max(60, avail - 2);
                var row = c as FlowResultRow;
                if (row != null) row.ShowTimestamp(showTime);
            }
        }

        // =====================================================================
        //  行 → 高亮（C5 四条断言的落点）
        // =====================================================================

        /// <summary>
        /// 主行点击：<strong>有目标就高亮第 1 个</strong>（C5-a），无目标则清除高亮（C5-d）。
        /// 同时切换展开态。
        /// </summary>
        private void OnMainRowClicked(object sender, EventArgs e)
        {
            NodeResultCache c = GetCache(_shownNodeId);

            if (c == null || c.Shapes == null || c.Shapes.Count == 0)
            {
                // C5-d：无结果节点 —— 清除所有高亮，不弹异常、不写错误日志
                FireShapeSelected("", -1);
                return;
            }

            _expandedNodeId = string.Equals(_expandedNodeId, c.NodeId, StringComparison.Ordinal)
                ? "" : c.NodeId;
            Render();

            // C5-a：点主行 → 只高亮第 1 个（序号 0）
            FireShapeSelected(c.NodeId, 0);
        }

        /// <summary>
        /// 明细行点击：高亮第 k 个目标（C5-b / C5-c）。
        /// 每次都以新序号重设高亮，而 <c>ImageViewer.SelectShape</c> 会先取消其它图形 ——
        /// 所以"越点越多"在结构上不可能发生。
        /// </summary>
        private void OnDetailRowClicked(object sender, EventArgs e)
        {
            var row = sender as FlowResultRow;
            if (row == null || row.DetailIndex < 0) return;

            NodeResultCache c = GetCache(_shownNodeId);
            if (c == null || c.Shapes == null || row.DetailIndex >= c.Shapes.Count)
            {
                FireShapeSelected("", -1);
                return;
            }

            FireShapeSelected(c.NodeId, row.DetailIndex);
        }

        private void OnZoomClicked(object sender, EventArgs e)
        {
            EventHandler h = ZoomClicked;
            if (h != null) h(this, EventArgs.Empty);
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

        /// <summary>明细行文本：目标的关键几何/数值，压成一行（288px 内不换行）。</summary>
        private static string DescribeShape(IGeometryShape s)
        {
            if (s == null) return "";

            var rect = s as RectangleShape;
            if (rect != null)
            {
                string label = string.IsNullOrEmpty(rect.Label) ? "" : rect.Label + "　";
                return string.Format(CultureInfo.InvariantCulture, "{0}({1:0},{2:0}) {3:0}×{4:0}",
                    label, rect.X, rect.Y, rect.Width, rect.Height);
            }

            var rr = s as RotatedRectShape;
            if (rr != null)
            {
                string label = string.IsNullOrEmpty(rr.Label) ? "" : rr.Label + "　";
                return string.Format(CultureInfo.InvariantCulture,
                    "{0}({1:0},{2:0}) {3:0}×{4:0} {5:0.0}°",
                    label, rr.Center.X, rr.Center.Y, rr.Width, rr.Height, rr.AngleDeg);
            }

            var line = s as LineSegmentShape;
            if (line != null)
            {
                return string.Format(CultureInfo.InvariantCulture, "({0:0},{1:0})→({2:0},{3:0})",
                    line.P1.X, line.P1.Y, line.P2.X, line.P2.Y);
            }

            var circle = s as CircleShape;
            if (circle != null)
            {
                return string.Format(CultureInfo.InvariantCulture, "C=({0:0},{1:0}) R={2:0}",
                    circle.Center.X, circle.Center.Y, circle.Radius);
            }

            string geo = s.ToGeometryString();
            if (!string.IsNullOrEmpty(geo)) return geo;
            return string.IsNullOrEmpty(s.Name) ? s.GetType().Name : s.Name;
        }

        /// <summary>摘要压成一行（行控件不显示换行）。</summary>
        private static string OneLine(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";

            string s = text.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');
            while (s.Contains("  ")) s = s.Replace("  ", " ");
            return s.Trim();
        }

        // =====================================================================
        //  探针支撑：让 SmokeUi 不点鼠标也能走完 C5
        // =====================================================================

        /// <summary>模拟点击主行（仅测试用）。</summary>
        public void SimulateMainRowClick()
        {
            OnMainRowClicked(this, EventArgs.Empty);
        }

        /// <summary>模拟点击明细第 <paramref name="index"/> 条（从 0 起，仅测试用）。</summary>
        public void SimulateDetailRowClick(int index)
        {
            FlowResultRow row = FlowResultRow.CreateDetail(index);
            try
            {
                OnDetailRowClicked(row, EventArgs.Empty);
            }
            finally
            {
                row.Dispose();
            }
        }
    }
}
