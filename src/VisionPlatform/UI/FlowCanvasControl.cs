using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using VisionPlatform.Flow;

namespace VisionPlatform.UI
{
    // 事件参数 / 运行状态等辅助类型已移至 FlowCanvasTypes.cs——VS 要求「可设计类必须是文件中的第一个类型」，
    // 它们原先排在 FlowCanvasControl 之前会导致设计器无法打开本控件。

    /// <summary>
    /// 自研 GDI+ 流程画布控件（零第三方依赖）：节点绘制 / 端子绘制 / 命中测试 / 拖动移动 /
    /// 端子级连线建立与删除 / 工具箱拖入 / Delete 删除。
    ///
    /// 【端子化（参考 VisionPro QuickBuild）】每个节点按 <see cref="FlowPortSchema"/>
    /// 在左边界自上而下画输入端子、右边界画输出端子，端子带中文显示名；
    /// 连线是「某输出端子 → 某输入端子」的贝塞尔曲线，箭头落在目标输入端子上。
    /// 节点高度随端子行数自适应（<see cref="NodeHeight"/>）。
    ///
    /// 坐标约定：本控件无图像、无缩放，**画布坐标 = 屏幕像素**（与图形几何用图像坐标的约定不同，
    /// 此处统一屏幕坐标，阈值如 PortGlyph/HitTol 均按屏幕像素）。
    /// </summary>
    public sealed class FlowCanvasControl : Control
    {
        // 画布几何常量（屏幕像素）
        private const int NodeW = 180;           // 节点宽度（上/下边缘要排 3~4 个端子名）
        private const int TitleH = 26;           // 节点标题行高度
        private const int RowH = 18;             // 端子行高
        private const int SummaryRowH = 13;      // 结果摘要每行高度（摘要 + 耗时/状态共两行）
        private const int SummaryH = SummaryRowH * 2 + 2;   // 卡片底部结果区总高（常驻，未运行也占位）
        private const int NodePadBottom = 4;     // 端子区下方留白

        // ---- 视图变换（画布坐标 → 屏幕坐标：screen = canvas * zoom + pan）----
        private float _viewZoom = 1f;
        private float _panX;
        private float _panY;
        private bool _panning;
        private PointF _panStart;
        private float _panStartX, _panStartY;

        /// <summary>最小/最大缩放倍率（太小看不清、太大失去全局感）。</summary>
        public const float MinViewZoom = 0.25f;
        /// <summary>最大缩放倍率。</summary>
        public const float MaxViewZoom = 3.0f;
        private const int NominalNodeH = TitleH + PortLabelH + SummaryH + PortLabelH + NodePadBottom;   // 节点标准高度
        private const float PortGlyph = 9f;      // 端子方形符号边长
        private const float PortHitRadius = 10f; // 端子命中半径（端子命中优先于节点本体）
        private const int HitTol = 6;            // 连线命中容差
        private const int ArrowSize = 8;         // 箭头长度
        private const float MinBezierCx = 40f;   // 贝塞尔控制点最小水平距离
        private const int BezierSamples = 24;    // 贝塞尔采样点数（连线命中测试用）

        /// <summary>工具箱拖拽数据格式（DataObject 中的字符串为模块 Key）。</summary>
        public const string DragFormat = "FlowModuleKey";

        private FlowDefinition _flow;
        private string _selectedNodeId;
        private int _selectedLinkIndex = -1;

        /// <summary>节点运行状态表（节点 Id → 状态 / 耗时）。仅在有运行记录后才写入。</summary>
        private readonly Dictionary<string, FlowNodeRunInfo> _runStatus =
            new Dictionary<string, FlowNodeRunInfo>();

        // 拖拽状态
        private enum DragKind { None, MoveNode, DrawLink }
        private DragKind _dragKind = DragKind.None;
        private FlowNode _dragNode;
        private PointF _dragOffset;

        /// <summary>拉线的源端子（输出或输入端子均可作为起点，见 <see cref="FinishLinkDrag"/>）。</summary>
        private PortHit _linkSource;
        private PointF _linkPreviewEnd;

        // 悬停跟踪（端子 tooltip）
        private bool _hoverValid;
        private PortHit _hoverPort;
        private PointF _hoverPt;

        // 画布底部临时提示（连线被拒等原因说明；短暂显示后自动消失）
        private string _toast;
        private bool _toastError;
        private readonly Timer _toastTimer;

        /// <summary>端子命中结果：节点 + 端子声明 + 方向 + 行号 + 端子中心坐标。</summary>
        private struct PortHit
        {
            public FlowNode Node;
            public FlowPortDef Port;
            public FlowPortDir Dir;
            public int Index;
            public PointF Center;
        }

        public FlowCanvasControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint
                     | ControlStyles.UserPaint
                     | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.ResizeRedraw
                     | ControlStyles.Selectable, true);
            TabStop = true;
            AllowDrop = true;
            BackColor = UiTheme.ColorViewBg;

            // 画布临时提示的自动消失计时器
            _toastTimer = new Timer { Interval = 2800 };
            _toastTimer.Tick += OnToastTick;
        }

        /// <summary>切换深浅色板时由 <see cref="UiThemeManager"/> 调用：重取图像区底色并重绘。</summary>
        public void ApplyTheme()
        {
            BackColor = UiTheme.ColorViewBg;
            Invalidate();
        }

        /// <summary>释放计时器等自有资源。</summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing && _toastTimer != null)
            {
                _toastTimer.Tick -= OnToastTick;
                _toastTimer.Dispose();
            }
            base.Dispose(disposing);
        }

        #region 属性与事件

        /// <summary>当前编辑的流程（set 后清空选择并重绘）。</summary>
        public FlowDefinition Flow
        {
            get { return _flow; }
            set
            {
                _flow = value;
                _selectedNodeId = null;
                _selectedLinkIndex = -1;
                _runStatus.Clear();     // 换流程即复位画布状态，避免残留上一次运行的颜色
                Invalidate();
            }
        }

        /// <summary>当前选中的节点 Id（无选中为 null）。</summary>
        public string SelectedNodeId { get { return _selectedNodeId; } }

        /// <summary>当前选中的连线序号（无选中为 -1）。</summary>
        public int SelectedLinkIndex { get { return _selectedLinkIndex; } }

        /// <summary>当前选中的节点（无选中为 null）。</summary>
        public FlowNode SelectedNode
        {
            get { return _flow != null ? _flow.FindNode(_selectedNodeId) : null; }
        }

        public event EventHandler<FlowNodeEventArgs> NodeAdded;
        public event EventHandler<FlowNodeEventArgs> NodeMoved;
        public event EventHandler<FlowNodeEventArgs> NodeSelected;
        public event EventHandler<FlowLinkEventArgs> LinkAdded;
        public event EventHandler<FlowLinkEventArgs> LinkRemoved;

        /// <summary>连线被拒绝（类型不兼容 / 成环 / 自连 / 方向错 / 重复连接）。原因串可直接进日志区。</summary>
        public event EventHandler<FlowLinkRejectedEventArgs> LinkRejected;

        /// <summary>同一输入端子被重新连接：旧边被新边替换。</summary>
        public event EventHandler<FlowLinkReplacedEventArgs> LinkReplaced;

        public event EventHandler Changed;

        #endregion

        #region 对外操作

        /// <summary>在指定画布坐标处按模块 Key 新增节点；返回新节点（flow 为空或 Key 无效时返回 null）。</summary>
        public FlowNode AddNode(string moduleKey, float x, float y)
        {
            FlowNode node = FlowModule.CreateNodeByKey(moduleKey, x, y);
            if (node == null || _flow == null) return null;

            _flow.Nodes.Add(node);
            _selectedNodeId = node.Id;
            _selectedLinkIndex = -1;

            RaiseNodeAdded(node);
            RaiseNodeSelected(node);
            RaiseChanged();
            Invalidate();
            return node;
        }

        /// <summary>删除当前选中的节点（含其连线）或选中的连线。</summary>
        public void DeleteSelected()
        {
            if (_flow == null) return;

            if (!string.IsNullOrEmpty(_selectedNodeId))
            {
                FlowNode node = _flow.FindNode(_selectedNodeId);
                if (node != null)
                {
                    // 先移除与该节点相关的连线
                    for (int i = _flow.Links.Count - 1; i >= 0; i--)
                    {
                        FlowLink l = _flow.Links[i];
                        if (l == null) continue;
                        if (l.FromNodeId == node.Id || l.ToNodeId == node.Id)
                        {
                            _flow.Links.RemoveAt(i);
                            RaiseLinkRemoved(l);
                        }
                    }

                    _flow.Nodes.Remove(node);
                    _selectedNodeId = null;
                    RaiseChanged();
                    Invalidate();
                }
                return;
            }

            if (_selectedLinkIndex >= 0 && _selectedLinkIndex < _flow.Links.Count)
            {
                FlowLink l = _flow.Links[_selectedLinkIndex];
                _flow.Links.RemoveAt(_selectedLinkIndex);
                _selectedLinkIndex = -1;
                RaiseLinkRemoved(l);
                RaiseChanged();
                Invalidate();
            }
        }

        /// <summary>清空选择并重绘（外部改绑流程后调用）。</summary>
        public void RefreshView() { Invalidate(); }

        // ---------------------- 自动整理布局（供 FlowForm 按钮调用）----------------------

        /// <summary>自动整理布局的列间距（固定）。</summary>
        private const int AutoLayoutColGap = 210;   // 列间距 = 节点宽 180 + 30 间隙（改宽必须同步）

        /// <summary>自动整理布局的列内行距（在节点实际高度之外再加这么多）。</summary>
        private const int AutoLayoutRowGap = 12;

        /// <summary>自动整理布局的外边距。</summary>
        private const int AutoLayoutMargin = 24;

        /// <summary>
        /// 自动整理布局：按拓扑序分层，从左到右排列；列内按当前 Y 升序堆叠，
        /// 行距 = 节点<strong>当前实际高度</strong> + 12 px（端子化后节点变高，不能再用旧的固定 44 px）；
        /// 孤立节点与环上节点排在最右列。
        /// 用途：老配置按旧节点高度存的坐标在端子化后可能重叠，由用户点按钮显式整理。
        /// ⚠️ <strong>只应在用户显式调用时使用</strong>，加载配置时不得自动调用——
        /// 静默改写用户坐标是本项目的红线（参考 MaxParallelFlows 的教训）。
        /// </summary>
        public void AutoLayout()
        {
            if (_flow == null || _flow.Nodes == null || _flow.Nodes.Count == 0) return;

            List<FlowNode> order;
            string error;
            FlowOrder.Build(_flow, out order, out error);

            // 上游表（去重，与 FlowOrder 的入度口径一致）+ 是否有出边
            var upstream = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var hasOut = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (FlowNode n in _flow.Nodes)
            {
                if (n == null || string.IsNullOrEmpty(n.Id)) continue;
                if (!upstream.ContainsKey(n.Id)) upstream[n.Id] = new List<string>();
                if (!hasOut.ContainsKey(n.Id)) hasOut[n.Id] = false;
            }
            if (_flow.Links != null)
            {
                foreach (FlowLink l in _flow.Links)
                {
                    if (l == null) continue;
                    if (!upstream.ContainsKey(l.ToNodeId) || !hasOut.ContainsKey(l.FromNodeId)) continue;
                    if (!upstream[l.ToNodeId].Contains(l.FromNodeId)) upstream[l.ToNodeId].Add(l.FromNodeId);
                    hasOut[l.FromNodeId] = true;
                }
            }

            // 分层：rank = 1 + max(上游 rank)
            var rank = new Dictionary<string, int>(StringComparer.Ordinal);
            var inOrder = new HashSet<string>(StringComparer.Ordinal);
            foreach (FlowNode n in order)
            {
                if (n == null || string.IsNullOrEmpty(n.Id)) continue;
                inOrder.Add(n.Id);

                int r = 0;
                List<string> ups;
                if (upstream.TryGetValue(n.Id, out ups) && ups != null)
                {
                    foreach (string up in ups)
                        r = Math.Max(r, rank.ContainsKey(up) ? rank[up] + 1 : 1);
                }
                rank[n.Id] = r;
            }

            int maxRank = 0;
            foreach (KeyValuePair<string, int> kv in rank)
                if (kv.Value > maxRank) maxRank = kv.Value;

            // 孤立节点 / 不在拓扑序里的节点（环上）→ 最右列
            int lastRank = maxRank + 1;
            foreach (FlowNode n in _flow.Nodes)
            {
                if (n == null || string.IsNullOrEmpty(n.Id)) continue;
                if (!inOrder.Contains(n.Id)) { rank[n.Id] = lastRank; continue; }

                List<string> ups;
                bool noIn = !upstream.TryGetValue(n.Id, out ups) || ups == null || ups.Count == 0;
                bool noOut = !hasOut.ContainsKey(n.Id) || !hasOut[n.Id];
                if (noIn && noOut) rank[n.Id] = lastRank;
            }

            // 按列落位：列内按当前 Y 升序（Y 相同再按 X / Id），纵向按实际高度累加
            var columns = new SortedDictionary<int, List<FlowNode>>();
            foreach (FlowNode n in _flow.Nodes)
            {
                if (n == null || string.IsNullOrEmpty(n.Id)) continue;

                int r;
                if (!rank.TryGetValue(n.Id, out r)) r = lastRank;

                List<FlowNode> list;
                if (!columns.TryGetValue(r, out list)) { list = new List<FlowNode>(); columns[r] = list; }
                list.Add(n);
            }

            // ---- 自上而下排布（VisionPro 风格）：每层一行，行内按重心排 ----
            // 原来按"旧 Y"排序，重排一次线就乱一次（旧 Y 是上一次的结果，越排越斜）。
            // 改成：行内按**上游节点的平均中心 X** 排序，父子相对位置稳定、连线交叉显著减少；
            // 源节点无上游时仍按旧 X（保留用户手工摆放的意向）。
            var centerX = new Dictionary<string, float>(StringComparer.Ordinal);
            float yTop = AutoLayoutMargin;

            foreach (KeyValuePair<int, List<FlowNode>> kv in columns)
            {
                List<FlowNode> row = kv.Value;

                row.Sort(delegate(FlowNode a, FlowNode b)
                {
                    float wa = BarycenterX(a, upstream, centerX);
                    float wb = BarycenterX(b, upstream, centerX);
                    int c = wa.CompareTo(wb);
                    return c != 0 ? c : FlowOrder.CompareNodes(a, b);
                });

                float rowH = 0f;
                foreach (FlowNode n in row)
                {
                    float h = NodeHeight(n);
                    if (h > rowH) rowH = h;
                }

                float x = AutoLayoutMargin;
                foreach (FlowNode n in row)
                {
                    n.X = x;
                    n.Y = yTop + (rowH - NodeHeight(n)) / 2f;   // 行内垂直居中
                    centerX[n.Id] = x + NodeW / 2f;
                    x += NodeW + AutoLayoutColGap;
                }

                // 竖直方向要留够连线空间：行高 + 2 倍行间距
                yTop += rowH + AutoLayoutRowGap * 2;
            }

            RaiseChanged();     // 坐标变了，通知外部保存
            Invalidate();
        }

        /// <summary>
        /// 节点的"重心 X"：上游节点中心 X 的平均值（无上游时用自身 X）。
        /// 自上而下排布时用它决定"行内左右顺序"——父节点在左，子节点尽量排在它上方。
        /// </summary>
        private static float BarycenterX(FlowNode node,
                                         Dictionary<string, List<string>> upstream,
                                         Dictionary<string, float> centerX)
        {
            if (node == null) return 0f;

            List<string> ups;
            if (upstream == null || centerX == null
                || !upstream.TryGetValue(node.Id, out ups) || ups == null || ups.Count == 0)
            {
                return node.X;
            }

            float sum = 0f;
            int n = 0;
            foreach (string up in ups)
            {
                float v;
                if (centerX.TryGetValue(up, out v)) { sum += v; n++; }
            }
            return n > 0 ? sum / n : node.X;
        }

        /// <summary>
        /// 节点的"重心 Y"：上游节点中心 Y 的平均值（无上游时用自身 Y）。
        /// 列内按它排序能把父子关系摆正，避免连线交叉。
        /// </summary>
        private static float Barycenter(FlowNode node,
                                        Dictionary<string, List<string>> upstream,
                                        Dictionary<string, float> centerY)
        {
            if (node == null) return 0f;

            List<string> ups;
            if (upstream == null || centerY == null
                || !upstream.TryGetValue(node.Id, out ups) || ups == null || ups.Count == 0)
            {
                return node.Y;
            }

            float sum = 0f;
            int n = 0;
            foreach (string up in ups)
            {
                float v;
                if (centerY.TryGetValue(up, out v)) { sum += v; n++; }
            }
            return n > 0 ? sum / n : node.Y;
        }

        // ---------------------- 运行状态（FR-ALG-23）----------------------

        /// <summary>把执行期状态映射为画布视觉态。</summary>
        public static FlowNodeRunState ToRunState(FlowNodeStatus status)
        {
            switch (status)
            {
                case FlowNodeStatus.Running: return FlowNodeRunState.Running;
                case FlowNodeStatus.Ok:      return FlowNodeRunState.Ok;
                case FlowNodeStatus.Warn:    return FlowNodeRunState.Warn;
                case FlowNodeStatus.Ng:      return FlowNodeRunState.Failed;
                default:                     return FlowNodeRunState.Waiting;
            }
        }

        /// <summary>
        /// 设置节点运行状态与耗时并重绘。耗时传负数表示不显示耗时文本。
        /// </summary>
        public void SetNodeStatus(string nodeId, FlowNodeRunState state, int elapsedMs)
        {
            SetNodeStatus(nodeId, state, elapsedMs, null);
        }

        /// <summary>
        /// 设置节点运行状态、耗时与<strong>一行结果摘要</strong>并重绘。
        /// <para><paramref name="summary"/> 为 null 表示"不改变已有摘要"（如仅切到运行中时）；
        /// 传空串表示"明确没有摘要"（未运行节点显示"未运行"）。</para>
        /// </summary>
        public void SetNodeStatus(string nodeId, FlowNodeRunState state, int elapsedMs, string summary)
        {
            if (string.IsNullOrEmpty(nodeId)) return;

            FlowNodeRunInfo info;
            if (!_runStatus.TryGetValue(nodeId, out info))
            {
                info = new FlowNodeRunInfo();
                _runStatus[nodeId] = info;
            }

            info.State = state;
            info.ElapsedMs = elapsedMs;
            if (summary != null) info.Summary = summary;
            Invalidate();
        }

        /// <summary>高亮某个节点为「运行中」（只改状态，不动耗时）。</summary>
        public void HighlightRunning(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId)) return;
            SetNodeStatus(nodeId, FlowNodeRunState.Running, -1);
        }

        /// <summary>清空全部运行状态（换新流程 / 复位单步时调用）。</summary>
        public void ClearNodeStatus()
        {
            _runStatus.Clear();
            Invalidate();
        }

        /// <summary>取节点当前运行状态（无记录时返回 null）。</summary>
        public FlowNodeRunInfo GetNodeStatus(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId)) return null;

            FlowNodeRunInfo info;
            return _runStatus.TryGetValue(nodeId, out info) ? info : null;
        }

        #endregion

        #region 几何与命中测试

        private static RectangleF NodeRect(FlowNode n)
        {
            return new RectangleF(n.X, n.Y, NodeW, NodeHeight(n));
        }

        /// <summary>节点输入端子表（未知类型返回空表，不抛异常）。</summary>
        private static List<FlowPortDef> NodeInputs(FlowNode n)
        {
            FlowNodeType t;
            if (n == null || !n.TryGetType(out t)) return new List<FlowPortDef>();
            return FlowPortSchema.InputsOf(t);
        }

        /// <summary>节点输出端子表（未知类型返回空表，不抛异常）。</summary>
        private static List<FlowPortDef> NodeOutputs(FlowNode n)
        {
            FlowNodeType t;
            if (n == null || !n.TryGetType(out t)) return new List<FlowPortDef>();
            return FlowPortSchema.OutputsOf(t);
        }

        /// <summary>
        /// 端子行数 = max(输入端子数, 输出端子数)；
        /// 节点高度 = 标题行 + 行数 × 行高 + <strong>结果区（常驻两行）</strong> + 底部留白。
        /// <para>
        /// 结果区高度<strong>恒定预留</strong>（不随是否运行变化）：高度若跟着运行状态变，
        /// 连续运行时卡片会上下跳动、连线端点跟着抖，比"未运行时多一块空白"难看得多。
        /// 未运行时该区域显示灰字「未运行」。
        /// </para>
        /// </summary>
        private static int PortRowCount(FlowNode n)
        {
            return Math.Max(NodeInputs(n).Count, NodeOutputs(n).Count);
        }

        /// <summary>端子名行高（上/下边缘各一行）。</summary>
        private const int PortLabelH = 16;

        private static float NodeHeight(FlowNode n)
        {
            // 上入下出：标题 + 输入名行 + 结果区 + 输出名行
            return TitleH + PortLabelH + SummaryH + PortLabelH + NodePadBottom;
        }

        /// <summary>
        /// 端子中心坐标：输入端子沿左边界、输出端子沿右边界，自上而下按行排列。
        /// </summary>
        private static PointF PortPoint(FlowNode n, FlowPortDir dir, int index)
        {
            // VisionPro 作业树式：输入端子沿【左边缘】、输出端子沿【右边缘】，一个端子一行。
            // 名字画在同一行的内侧，方块与名字严格对齐 —— 不会再出现"名字在上面、方块在别处"。
            // 上入下出（用户明确要求）：输入沿上边、输出沿下边，按数量均分
            int count = PortCount(n, dir);
            if (count < 1) count = 1;

            float step = NodeW / (count + 1f);
            float x = n.X + step * (index + 1);
            float y = dir == FlowPortDir.In ? n.Y : n.Y + NodeHeight(n);
            return new PointF(x, y);
        }

        /// <summary>某方向的端子数量（用于沿边均分）。</summary>
        private static int PortCount(FlowNode n, FlowPortDir dir)
        {
            FlowNodeType t;
            if (n == null || !n.TryGetType(out t)) return 0;

            List<FlowPortDef> list = dir == FlowPortDir.In
                ? FlowPortSchema.InputsOf(t)
                : FlowPortSchema.OutputsOf(t);
            return list != null ? list.Count : 0;
        }

        /// <summary>
        /// 按端子 Key 求行号；Key 缺失或在端子表中查不到时，按 <see cref="FlowLink.Normalize"/>
        /// 的语义回落到主端子（源取主输出、目标取主输入）。
        /// ⚠️ 只用于画布定位显示，<strong>不在画布里回写配置</strong>（归一化由
        /// <c>FlowSettings.UpgradeToCurrent</c> 在加载期统一完成）。
        /// </summary>
        private static int PortIndexOf(FlowNode n, FlowPortDir dir, string key)
        {
            FlowNodeType t;
            if (n == null || !n.TryGetType(out t)) return -1;

            List<FlowPortDef> list = (dir == FlowPortDir.In)
                ? FlowPortSchema.InputsOf(t)
                : FlowPortSchema.OutputsOf(t);

            if (!string.IsNullOrEmpty(key))
            {
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i] != null && string.Equals(list[i].Key, key, StringComparison.Ordinal))
                        return i;
                }
            }

            string fallback = (dir == FlowPortDir.In)
                ? FlowPortSchema.PrimaryInput(t)
                : FlowPortSchema.PrimaryOutput(t);
            if (!string.IsNullOrEmpty(fallback))
            {
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i] != null && string.Equals(list[i].Key, fallback, StringComparison.Ordinal))
                        return i;
                }
            }
            return -1;
        }

        private static bool Near(PointF a, PointF b, float tol)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y;
            return dx * dx + dy * dy <= tol * tol;
        }

        private FlowNode HitTestNode(PointF pt)
        {
            if (_flow == null || _flow.Nodes == null) return null;
            for (int i = _flow.Nodes.Count - 1; i >= 0; i--)
            {
                FlowNode n = _flow.Nodes[i];
                if (n != null && NodeRect(n).Contains(pt)) return n;
            }
            return null;
        }

        /// <summary>
        /// 端子命中测试（输入 / 输出都查）。返回 false 时 <paramref name="hit"/> 为默认值。
        /// 调用方必须先于节点本体命中调用（端子命中优先，否则点端子会变成拖节点）。
        /// </summary>
        private bool HitTestPort(PointF pt, out PortHit hit)
        {
            hit = default(PortHit);
            if (_flow == null || _flow.Nodes == null) return false;

            for (int i = _flow.Nodes.Count - 1; i >= 0; i--)
            {
                FlowNode n = _flow.Nodes[i];
                if (n == null) continue;

                List<FlowPortDef> ins = NodeInputs(n);
                for (int k = 0; k < ins.Count; k++)
                {
                    PointF c = PortPoint(n, FlowPortDir.In, k);
                    if (Near(c, pt, PortHitRadius))
                    {
                        hit = new PortHit { Node = n, Port = ins[k], Dir = FlowPortDir.In, Index = k, Center = c };
                        return true;
                    }
                }

                List<FlowPortDef> outs = NodeOutputs(n);
                for (int k = 0; k < outs.Count; k++)
                {
                    PointF c = PortPoint(n, FlowPortDir.Out, k);
                    if (Near(c, pt, PortHitRadius))
                    {
                        hit = new PortHit { Node = n, Port = outs[k], Dir = FlowPortDir.Out, Index = k, Center = c };
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool SamePort(PortHit a, PortHit b)
        {
            return a.Node == b.Node && a.Dir == b.Dir && a.Index == b.Index;
        }

        private int HitTestLink(PointF pt)
        {
            if (_flow == null || _flow.Links == null) return -1;
            for (int i = _flow.Links.Count - 1; i >= 0; i--)
            {
                FlowLink l = _flow.Links[i];
                if (l == null) continue;

                PointF a, b;
                if (!LinkEndPoints(l, out a, out b)) continue;
                if (DistanceToPolyline(pt, OrthoVertical(a, b)) <= HitTol) return i;
            }
            return -1;
        }

        /// <summary>按连线的端子 Key 求曲线两端坐标；端子缺失时按主端子回落，再缺则返回 false。</summary>
        private bool LinkEndPoints(FlowLink link, out PointF a, out PointF b)
        {
            a = PointF.Empty;
            b = PointF.Empty;

            FlowNode from = _flow.FindNode(link.FromNodeId);
            FlowNode to = _flow.FindNode(link.ToNodeId);
            if (from == null || to == null) return false;

            int ia = PortIndexOf(from, FlowPortDir.Out, link.FromPort ?? "");
            int ib = PortIndexOf(to, FlowPortDir.In, link.ToPort ?? "");
            if (ia < 0 || ib < 0) return false;

            a = PortPoint(from, FlowPortDir.Out, ia);
            b = PortPoint(to, FlowPortDir.In, ib);
            return true;
        }

        // ---------------------- 贝塞尔几何 ----------------------

        /// <summary>
        /// 三次贝塞尔控制点：起终点各向外拉一段水平距离，得到「先水平出、再水平进」的
        /// 平滑曲线（VisionPro / 节点编辑器通用画法）。
        /// </summary>
        private static void BezierControls(PointF a, PointF b, out PointF c1, out PointF c2)
        {
            // 自上而下连线：控制点沿竖直方向拉开，曲线才不会贴成折线
            float cy = Math.Max(MinBezierCx, Math.Abs(b.Y - a.Y) * 0.5f);
            c1 = new PointF(a.X, a.Y + cy);
            c2 = new PointF(b.X, b.Y - cy);
        }

        private static PointF BezierEval(PointF a, PointF c1, PointF c2, PointF b, float t)
        {
            float u = 1f - t;
            float w0 = u * u * u;
            float w1 = 3f * u * u * t;
            float w2 = 3f * u * t * t;
            float w3 = t * t * t;
            return new PointF(
                w0 * a.X + w1 * c1.X + w2 * c2.X + w3 * b.X,
                w0 * a.Y + w1 * c1.Y + w2 * c2.Y + w3 * b.Y);
        }

        /// <summary>把贝塞尔曲线采样成折线（连线命中测试用）。</summary>
        /// <summary>
        /// 数据连线的正交折线（<strong>上入下出</strong>）：源底部 → 竖直 → 水平 → 竖直进目标顶部。
        /// 命中测试与绘制共用同一组点。
        /// </summary>
        private static PointF[] OrthoVertical(PointF a, PointF b)
        {
            float midY = (a.Y + b.Y) * 0.5f;
            if (midY < a.Y + 14f) midY = a.Y + 14f;   // 太近时也给一段竖直，避免退化成斜线

            return new[]
            {
                a,
                new PointF(a.X, midY),
                new PointF(b.X, midY),
                b
            };
        }

        private static PointF[] BezierPolyline(PointF a, PointF b)
        {
            PointF c1, c2;
            BezierControls(a, b, out c1, out c2);

            var pts = new PointF[BezierSamples + 1];
            for (int i = 0; i <= BezierSamples; i++)
                pts[i] = BezierEval(a, c1, c2, b, i / (float)BezierSamples);
            return pts;
        }

        private static float DistanceToPolyline(PointF p, PointF[] pts)
        {
            float best = float.MaxValue;
            for (int i = 0; i + 1 < pts.Length; i++)
                best = Math.Min(best, DistanceToSegment(p, pts[i], pts[i + 1]));
            return best;
        }

        private static float DistanceToSegment(PointF p, PointF a, PointF b)
        {
            float dx = b.X - a.X, dy = b.Y - a.Y;
            float len2 = dx * dx + dy * dy;
            if (len2 <= 1e-6f) return Dist(p, a);

            float t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2;
            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;
            return Dist(p, new PointF(a.X + t * dx, a.Y + t * dy));
        }

        private static float Dist(PointF a, PointF b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        #endregion

        #region 鼠标交互

        /// <summary>屏幕坐标 → 画布坐标。</summary>
        private PointF ToCanvas(Point p)
        {
            float z = _viewZoom <= 0f ? 1f : _viewZoom;
            return new PointF((p.X - _panX) / z, (p.Y - _panY) / z);
        }

        /// <summary>画布坐标 → 屏幕坐标。</summary>
        private PointF ToScreen(PointF p)
        {
            return new PointF(p.X * _viewZoom + _panX, p.Y * _viewZoom + _panY);
        }

        /// <summary>屏幕坐标 → 画布坐标（宿主/探针可用）。</summary>
        public PointF ScreenToCanvas(Point p) { return ToCanvas(p); }

        /// <summary>画布坐标 → 屏幕坐标（宿主/探针可用）。</summary>
        public PointF CanvasToScreen(PointF p) { return ToScreen(p); }

        /// <summary>当前缩放倍率（探针用）。</summary>
        public float ViewZoom { get { return _viewZoom; } }

        /// <summary>当前平移量（探针用）。</summary>
        public PointF ViewPan { get { return new PointF(_panX, _panY); } }

        /// <summary>设置缩放（以控件中心为锚点），并夹到 Min/MaxViewZoom。</summary>
        public void SetViewZoom(float zoom)
        {
            ZoomAt(new PointF(Width / 2f, Height / 2f), zoom);
        }

        /// <summary>以某个屏幕点为锚点缩放（滚轮缩放的语义：光标下的那个节点不动）。</summary>
        private void ZoomAt(PointF screenAnchor, float zoom)
        {
            if (zoom < MinViewZoom) zoom = MinViewZoom;
            if (zoom > MaxViewZoom) zoom = MaxViewZoom;
            if (Math.Abs(zoom - _viewZoom) < 1e-4f) return;

            // 锚点在画布上的位置保持不动：pan' = anchor - (anchor - pan) * (z'/z)
            float k = zoom / _viewZoom;
            _panX = screenAnchor.X - (screenAnchor.X - _panX) * k;
            _panY = screenAnchor.Y - (screenAnchor.Y - _panY) * k;
            _viewZoom = zoom;
            Invalidate();
        }

        /// <summary>自适应：把所有节点放进可视区（留 40px 边距），并复位平移。</summary>
        public void FitToView()
        {
            _viewZoom = 1f;
            _panX = 0f;
            _panY = 0f;

            if (_flow == null || _flow.Nodes.Count == 0) { Invalidate(); return; }

            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            foreach (FlowNode n in _flow.Nodes)
            {
                float h = NodeHeight(n);
                if (n.X < minX) minX = n.X;
                if (n.Y < minY) minY = n.Y;
                if (n.X + NodeW > maxX) maxX = n.X + NodeW;
                if (n.Y + h > maxY) maxY = n.Y + h;
            }

            const float margin = 40f;
            float cw = Width - margin * 2f;
            float ch = Height - margin * 2f;
            if (cw <= 10f || ch <= 10f) { Invalidate(); return; }

            float spanX = Math.Max(1f, maxX - minX);
            float spanY = Math.Max(1f, maxY - minY);
            float z = Math.Min(cw / spanX, ch / spanY);
            if (z < MinViewZoom) z = MinViewZoom;
            if (z > 1.5f) z = 1.5f;      // 只有一两个节点时别放得太大

            _viewZoom = z;
            _panX = margin - minX * z + (cw - spanX * z) / 2f;
            _panY = margin - minY * z + (ch - spanY * z) / 2f;
            Invalidate();
        }

        /// <summary>滚轮缩放（以光标为锚点）；Ctrl+滚轮同效。</summary>
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (e == null) return;

            float factor = e.Delta > 0 ? 1.12f : 1f / 1.12f;
            ZoomAt(new PointF(e.X, e.Y), _viewZoom * factor);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();

            // 中键 / 右键：拖动画布（任何位置都能拖，不用先找空白）
            if (e.Button == MouseButtons.Middle || e.Button == MouseButtons.Right)
            {
                _panning = true;
                _panStart = new PointF(e.X, e.Y);
                _panStartX = _panX;
                _panStartY = _panY;
                Cursor = Cursors.SizeAll;
                return;
            }

            if (e.Button != MouseButtons.Left || _flow == null) return;

            var pt = ToCanvas(e.Location);

            // 1) 端子（输入或输出）→ 开始拉端子级连线（端子命中优先于节点本体）
            PortHit port;
            if (HitTestPort(pt, out port))
            {
                _dragKind = DragKind.DrawLink;
                _linkSource = port;
                _linkPreviewEnd = pt;
                Invalidate();
                return;
            }

            // 2) 节点体 → 选中并开始拖动
            FlowNode node = HitTestNode(pt);
            if (node != null)
            {
                _selectedNodeId = node.Id;
                _selectedLinkIndex = -1;
                _dragKind = DragKind.MoveNode;
                _dragNode = node;
                _dragOffset = new PointF(pt.X - node.X, pt.Y - node.Y);
                RaiseNodeSelected(node);
                Invalidate();
                return;
            }

            // 3) 连线 → 选中连线
            int linkIdx = HitTestLink(pt);
            if (linkIdx >= 0)
            {
                _selectedLinkIndex = linkIdx;
                _selectedNodeId = null;
                RaiseNodeSelected(null);
                Invalidate();
                return;
            }

            // 4) 空白 → 清空选择，并允许"按住左键拖空白"平移画布
            _selectedNodeId = null;
            _selectedLinkIndex = -1;
            RaiseNodeSelected(null);

            _panning = true;
            _panStart = new PointF(e.X, e.Y);
            _panStartX = _panX;
            _panStartY = _panY;

            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            // 画布平移优先
            if (_panning)
            {
                _panX = _panStartX + (e.X - _panStart.X);
                _panY = _panStartY + (e.Y - _panStart.Y);
                Invalidate();
                return;
            }

            PointF canvas = ToCanvas(e.Location);

            if (_dragKind == DragKind.MoveNode && _dragNode != null)
            {
                // 不再 clamp 到 0：画布可以平移，负坐标照样能看到；
                // 之前卡在 0 会让"往左/往上拖"完全没反应（用户描述为"拖不动"）。
                _dragNode.X = canvas.X - _dragOffset.X;
                _dragNode.Y = canvas.Y - _dragOffset.Y;
                Invalidate();
            }
            else if (_dragKind == DragKind.DrawLink)
            {
                _linkPreviewEnd = canvas;
                Invalidate();       // 预览曲线 + 目标端子兼容高亮 + 拖拽提示都要刷新
            }
            else
            {
                // 空闲态：跟踪端子悬停（tooltip），仅在悬停端子变化时重绘
                PointF pt = canvas;
                PortHit hit;
                bool hasPort = HitTestPort(pt, out hit);

                bool changed = (hasPort != _hoverValid)
                    || (hasPort && !SamePort(hit, _hoverPort));
                _hoverValid = hasPort;
                _hoverPort = hasPort ? hit : default(PortHit);
                _hoverPt = pt;

                Cursor = hasPort ? Cursors.Hand : Cursors.Default;
                if (changed) Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hoverValid)
            {
                _hoverValid = false;
                _hoverPort = default(PortHit);
                Invalidate();
            }
        }

        /// <summary>双击空白 → 自适应全图（等价于很多工具里的"缩放到合适大小"）。</summary>
        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button != MouseButtons.Left || _flow == null) return;

            if (HitTestNode(ToCanvas(e.Location)) == null) FitToView();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);

            if (_panning)
            {
                _panning = false;
                Cursor = Cursors.Default;
                Invalidate();
                return;
            }

            if (_dragKind == DragKind.MoveNode && _dragNode != null)
            {
                RaiseNodeMoved(_dragNode);
                RaiseChanged();
            }
            else if (_dragKind == DragKind.DrawLink)
            {
                FinishLinkDrag(ToCanvas(e.Location));
            }

            _dragKind = DragKind.None;
            _dragNode = null;
            _linkSource = default(PortHit);
            Invalidate();
        }

        /// <summary>
        /// 拉线落手：按「输出端子 → 输入端子」建链。支持两个起始方向：
        /// 从输出端子起拖（正向）与从输入端子起拖（反向，落到兼容输出上补全连线）。
        /// </summary>
        private void FinishLinkDrag(PointF pt)
        {
            PortHit target;
            if (_flow == null || _linkSource.Node == null || !HitTestPort(pt, out target))
                return;                                     // 松手不在端子上：取消，不提示

            // ---- 方向归一：只允许 输出端子 → 输入端子 ----
            FlowNode fromNode, toNode;
            FlowPortDef fromPort, toPort;
            if (_linkSource.Dir == FlowPortDir.Out && target.Dir == FlowPortDir.In)
            {
                fromNode = _linkSource.Node; fromPort = _linkSource.Port;
                toNode = target.Node;        toPort = target.Port;
            }
            else if (_linkSource.Dir == FlowPortDir.In && target.Dir == FlowPortDir.Out)
            {
                fromNode = target.Node;      fromPort = target.Port;
                toNode = _linkSource.Node;   toPort = _linkSource.Port;
            }
            else
            {
                RejectLink("连线只能从输出端子（右侧）连到输入端子（左侧）", true,
                           _linkSource.Node, _linkSource.Port, target.Node, target.Port);
                return;
            }

            if (fromNode == toNode)
            {
                RejectLink("不能连接到节点自身", true, fromNode, fromPort, toNode, toPort);
                return;
            }

            // ---- 类型兼容（FlowPortSchema）----
            if (!toPort.Accepts(fromPort.Kind))
            {
                RejectLink("类型不兼容：" + fromPort.DisplayName + "(" + fromPort.Kind
                           + ") 不能连到 " + toPort.DisplayName + "(" + toPort.Kind + ")",
                           true, fromNode, fromPort, toNode, toPort);
                return;
            }

            // ---- 成环预检（FlowOrder）----
            if (FlowOrder.WouldCreateCycle(_flow, fromNode.Id, toNode.Id))
            {
                RejectLink("该连线会形成环路，已拒绝", true, fromNode, fromPort, toNode, toPort);
                return;
            }

            // ---- 重复连线：幂等处理，不再重复建链 / 发事件 ----
            if (LinkExists(fromNode.Id, fromPort.Key, toNode.Id, toPort.Key))
            {
                RejectLink("该连线已存在", false, fromNode, fromPort, toNode, toPort);
                return;
            }

            // ---- 多入端子（脚本 / 结果输出的汇总口）：追加，不替换 ----
            // 并行分支要能同时汇入同一个口（例如两个分支都接脚本汇总），
            // 这与"换数据源"是两件事，靠 FlowPortDef.MultiIn 区分。
            bool multiIn = toPort != null && toPort.MultiIn;

            // ---- 端口级唯一入边：替换旧边 ----
            // 理由：端口化后一个输入端子语义上只有一个数据来源（执行器按端子取唯一输入），
            // 端子上已有连线时再连新线，用户意图明确是「换数据源」——按 VisionPro 的行为
            // 用「替换」而不是「拒绝」，省去先删旧线再连新线的两步操作。
            // 被替换的旧边照常发 LinkRemoved，并额外发 LinkReplaced（让日志区能区分「删线」与「换线」）。
            var replaced = new List<FlowLink>();
            if (!multiIn)
            {
                for (int i = _flow.Links.Count - 1; i >= 0; i--)
                {
                    FlowLink l = _flow.Links[i];
                    if (l == null) continue;
                    if (l.ToNodeId == toNode.Id && (l.ToPort ?? "") == toPort.Key)
                    {
                        _flow.Links.RemoveAt(i);
                        RaiseLinkRemoved(l);
                        replaced.Add(l);
                    }
                }
            }

            var link = new FlowLink(fromNode.Id, fromPort.Key, toNode.Id, toPort.Key);
            _flow.Links.Add(link);
            _selectedLinkIndex = _flow.Links.Count - 1;
            _selectedNodeId = null;
            RaiseLinkAdded(link);
            foreach (FlowLink old in replaced) RaiseLinkReplaced(old, link);
            RaiseChanged();
            Invalidate();
        }

        /// <summary>
        /// 拒绝建链的统一出口：画布 toast（短暂）+ <see cref="LinkRejected"/> 事件（可进日志长期可看）。
        /// 两个出口都走，避免 2.8 秒后原因消失、用户在日志里查不到。
        /// </summary>
        private void RejectLink(string reason, bool error,
                                FlowNode fromNode, FlowPortDef fromPort,
                                FlowNode toNode, FlowPortDef toPort)
        {
            ShowToast(reason, error);

            string fromNodeId = fromNode != null ? fromNode.Id : "";
            string toNodeId = toNode != null ? toNode.Id : "";
            string fromPortKey = fromPort != null ? fromPort.Key : "";
            string toPortKey = toPort != null ? toPort.Key : "";
            RaiseLinkRejected(reason, fromNodeId, fromPortKey, toNodeId, toPortKey);
        }

        /// <summary>连线是否已存在（同两端节点 + 同两端端子）。</summary>
        private bool LinkExists(string fromNodeId, string fromPort, string toNodeId, string toPort)
        {
            foreach (FlowLink l in _flow.Links)
            {
                if (l == null) continue;
                if (l.FromNodeId == fromNodeId && (l.FromPort ?? "") == (fromPort ?? "")
                    && l.ToNodeId == toNodeId && (l.ToPort ?? "") == (toPort ?? ""))
                    return true;
            }
            return false;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (e.KeyCode == Keys.Delete)
            {
                DeleteSelected();
                e.Handled = true;
                return;
            }

            // 方向键微调选中节点（1px，Shift 10px）：鼠标拖不动时的兜底手段
            if (_selectedNodeId != null)
            {
                int step = e.Shift ? 10 : 1;
                float dx = 0f, dy = 0f;
                if (e.KeyCode == Keys.Left) dx = -step;
                else if (e.KeyCode == Keys.Right) dx = step;
                else if (e.KeyCode == Keys.Up) dy = -step;
                else if (e.KeyCode == Keys.Down) dy = step;

                if (Math.Abs(dx) > 0.01f || Math.Abs(dy) > 0.01f)
                {
                    FlowNode n = _flow != null ? _flow.FindNode(_selectedNodeId) : null;
                    if (n != null)
                    {
                        n.X += dx;
                        n.Y += dy;
                        RaiseNodeMoved(n);
                        RaiseChanged();
                        Invalidate();
                    }
                    e.Handled = true;
                }
            }
        }

        #endregion

        #region 工具箱拖拽

        protected override void OnDragEnter(DragEventArgs e)
        {
            base.OnDragEnter(e);
            e.Effect = e.Data != null && e.Data.GetDataPresent(DragFormat)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
        }

        protected override void OnDragOver(DragEventArgs e)
        {
            base.OnDragOver(e);
            e.Effect = e.Data != null && e.Data.GetDataPresent(DragFormat)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
        }

        protected override void OnDragDrop(DragEventArgs e)
        {
            base.OnDragDrop(e);
            if (_flow == null || e.Data == null || !e.Data.GetDataPresent(DragFormat)) return;

            string key = e.Data.GetData(DragFormat) as string;
            if (string.IsNullOrEmpty(key)) return;

            Point client = PointToClient(new Point(e.X, e.Y));
            // 以落点为中心放置节点，避免超出右/下边界
            AddNode(key, client.X - NodeW * 0.5f, client.Y - NominalNodeH * 0.5f);
        }

        #endregion

        #region 绘制

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // 视图变换：之后所有绘制一律用「画布坐标」，屏幕 = 画布 * zoom + pan
            g.TranslateTransform(_panX, _panY);
            g.ScaleTransform(_viewZoom, _viewZoom);

            DrawGrid(g);

            if (_flow == null) { g.ResetTransform(); return; }

            // 数据连线（彩色正交折线，压在节点下面）
            for (int i = 0; i < _flow.Links.Count; i++)
                DrawLink(g, _flow.Links[i], i == _selectedLinkIndex);

            // 连线拖拽预览（贝塞尔虚线，从源端子到光标）
            if (_dragKind == DragKind.DrawLink && _linkSource.Node != null)
            {
                PointF c1, c2;
                BezierControls(_linkSource.Center, _linkPreviewEnd, out c1, out c2);
                using (Pen pen = new Pen(UiTheme.ColorWarn, 2f))
                {
                    pen.DashStyle = DashStyle.Dash;
                    g.DrawBezier(pen, _linkSource.Center, c1, c2, _linkPreviewEnd);
                }
            }

            // 节点（含端子）
            foreach (FlowNode n in _flow.Nodes)
                DrawNode(g, n, n.Id == _selectedNodeId);

            // 覆盖层用屏幕坐标（提示气泡不该跟着缩放变大变小）
            g.ResetTransform();

            // 覆盖层：拖拽提示 / 端子 tooltip / 临时提示（自下而上依次压顶）
            DrawViewHint(g);
            DrawDragHint(g);
            DrawPortTooltip(g);
            DrawToast(g);
        }

        private void DrawGrid(Graphics g)
        {
            // 网格铺在画布坐标上：缩放时网格跟着放大，平移时跟着移动（否则像"背景贴纸"）
            float z = _viewZoom <= 0f ? 1f : _viewZoom;
            float left = -_panX / z, top = -_panY / z;
            float right = (Width - _panX) / z, bottom = (Height - _panY) / z;

            using (Pen pen = new Pen(WithAlpha(UiTheme.ColorTextSecondary, 26), 1f / z))
            {
                const int step = 20;
                float x0 = (float)Math.Floor(left / step) * step;
                float y0 = (float)Math.Floor(top / step) * step;

                for (float x = x0; x <= right; x += step) g.DrawLine(pen, x, top, x, bottom);
                for (float y = y0; y <= bottom; y += step) g.DrawLine(pen, left, y, right, y);
            }
        }

        private void DrawLink(Graphics g, FlowLink link, bool selected)
        {
            if (link == null) return;

            PointF a, b;
            if (!LinkEndPoints(link, out a, out b)) return;

            // 线色跟着"源端子的数据类型"走（图像=青、位姿=紫、数值=绿…），
            // 这样不用悬停也能看出这根线传的是图还是数。
            Color color = selected ? UiTheme.ColorWarn : LinkKindColor(link);
            // 同一个输入口有多条线（汇总口）时，沿端口左右错开，避免完全重叠成一条
            float shift = IncomingShift(link);
            if (Math.Abs(shift) > 0.01f)
            {
                a.X += shift;
                b.X += shift;
            }

            PointF[] pts = OrthoVertical(a, b);

            using (Pen pen = new Pen(color, selected ? 2.8f : 2f))
            {
                pen.LineJoin = LineJoin.Round;
                g.DrawLines(pen, pts);
            }

            // 箭头沿折线最后一段的方向（通常是从右往左进目标端子）
            DrawArrow(g, pts[pts.Length - 2], b, color, ArrowSize);
        }

        /// <summary>
        /// 同一输入口上第几条线（0 起），换算成水平偏移：-6/0/+6，三条以内都能看清。
        /// 只对多入端子生效；普通端子永远只有一条线。
        /// </summary>
        private float IncomingShift(FlowLink link)
        {
            if (_flow == null || _flow.Links == null || link == null) return 0f;

            var peers = new List<FlowLink>();
            foreach (FlowLink l in _flow.Links)
            {
                if (l == null) continue;
                if (l.ToNodeId == link.ToNodeId && (l.ToPort ?? "") == (link.ToPort ?? "")) peers.Add(l);
            }
            if (peers.Count <= 1) return 0f;

            int idx = peers.IndexOf(link);
            if (idx < 0) return 0f;

            return (idx - (peers.Count - 1) * 0.5f) * 6f;
        }

        /// <summary>在 <paramref name="tip"/> 处画一个朝 <paramref name="from"/>→tip 方向的箭头。</summary>
        private static void DrawArrow(Graphics g, PointF from, PointF tip, Color color, float size)
        {
            float dx = tip.X - from.X, dy = tip.Y - from.Y;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-3f) return;

            float ux = dx / len, uy = dy / len;
            var p1 = new PointF(tip.X - ux * size - uy * size * 0.55f,
                                tip.Y - uy * size + ux * size * 0.55f);
            var p2 = new PointF(tip.X - ux * size + uy * size * 0.55f,
                                tip.Y - uy * size - ux * size * 0.55f);

            using (Brush brush = new SolidBrush(color))
                g.FillPolygon(brush, new[] { tip, p1, p2 });
        }

        /// <summary>连线颜色 = 源端子的数据类型色（找不到时用强调色）。</summary>
        private Color LinkKindColor(FlowLink link)
        {
            if (_flow == null || link == null) return UiTheme.ColorAccent;

            FlowNode from = _flow.FindNode(link.FromNodeId);
            if (from == null) return UiTheme.ColorAccent;

            int idx = PortIndexOf(from, FlowPortDir.Out, link.FromPort ?? "");
            List<FlowPortDef> outs = NodeOutputs(from);
            if (idx >= 0 && idx < outs.Count && outs[idx] != null) return KindColor(outs[idx].Kind);

            return UiTheme.ColorAccent;
        }

        private void DrawNode(Graphics g, FlowNode n, bool selected)
        {
            RectangleF r = NodeRect(n);

            FlowNodeRunInfo run = GetNodeStatus(n.Id);
            FlowNodeRunState state = run != null ? run.State : FlowNodeRunState.Waiting;

            Color fill = UiTheme.ColorCardBg;
            Color border = BorderColor(state, selected);
            float borderWidth = selected ? 2.4f : 1.6f;
            if (state != FlowNodeRunState.Waiting) borderWidth = selected ? 3.0f : 2.4f;

            using (GraphicsPath path = RoundedRect(r, 8f))
            using (Brush brush = new SolidBrush(fill))
            using (Pen pen = new Pen(border, borderWidth))
            {
                g.FillPath(brush, path);
                g.DrawPath(pen, path);
            }

            // 标题行（节点名）
            string title = string.IsNullOrEmpty(n.Name) ? n.Type : n.Name;
            var titleRect = new RectangleF(r.X, r.Y, r.Width, TitleH);
            var sfTitle = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            using (Brush b = new SolidBrush(UiTheme.ColorTextPrimary))
                g.DrawString(title, UiTheme.FontGroupTitle, b, titleRect, sfTitle);

            // 端子（输入沿左边界、输出沿右边界，带显示名）
            List<FlowPortDef> ins = NodeInputs(n);
            List<FlowPortDef> outs = NodeOutputs(n);

            var sfNear = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };
            var sfFar = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };

            for (int k = 0; k < ins.Count; k++)
            {
                if (ins[k] == null) continue;
                PointF c = PortPoint(n, FlowPortDir.In, k);
                bool hovered = _hoverValid && _hoverPort.Node == n
                    && _hoverPort.Dir == FlowPortDir.In && _hoverPort.Index == k;
                DrawPortGlyph(g, c, PortColor(n, ins[k], FlowPortDir.In, k, hovered));

                // 端子名画在标题下方一行，以端口 x 为中心（用户要求"名称不要是空的"）
                DrawPortLabel(g, ins[k].DisplayName, c.X, r.Y + TitleH,
                              r, hovered ? UiTheme.ColorTextPrimary : UiTheme.ColorTextSecondary,
                              NodeW / (ins.Count + 1f) - 2f, StringAlignment.Center);
            }

            for (int k = 0; k < outs.Count; k++)
            {
                if (outs[k] == null) continue;
                PointF c = PortPoint(n, FlowPortDir.Out, k);
                bool hovered = _hoverValid && _hoverPort.Node == n
                    && _hoverPort.Dir == FlowPortDir.Out && _hoverPort.Index == k;
                DrawPortGlyph(g, c, PortColor(n, outs[k], FlowPortDir.Out, k, hovered));

                DrawPortLabel(g, outs[k].DisplayName, c.X, r.Bottom - NodePadBottom - PortLabelH,
                              r, hovered ? UiTheme.ColorTextPrimary : UiTheme.ColorTextSecondary,
                              NodeW / (outs.Count + 1f) - 2f, StringAlignment.Center);
            }

            // 运行状态附加信息：节点下方「xx ms」+ 失败标记（纯文本，FR-ALG-23）
            DrawRunOverlay(g, r, state, run);
        }

        /// <summary>
        /// 画端子名：以端口 x 为中心、宽度上限 = 相邻端子的间距，超长自动省略。
        /// 上下边缘的端子名放不下时宁可能省，也不要把相邻两个名字压在一起。
        /// </summary>
        private static void DrawPortLabel(Graphics g, string text, float anchorX, float top,
                                          RectangleF nodeRect, Color color, float maxWidth,
                                          StringAlignment align)
        {
            if (string.IsNullOrEmpty(text)) return;

            float w = Math.Min(200f, Math.Max(24f, maxWidth));
            float x = align == StringAlignment.Far ? anchorX - w
                    : (align == StringAlignment.Center ? anchorX - w * 0.5f : anchorX);
            var rect = new RectangleF(x, top, w, PortLabelH);
            using (Brush b = new SolidBrush(color))
            using (var sf = new StringFormat
            {
                Alignment = align,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap
            })
            using (Font f = new Font(UiTheme.FontSmall.FontFamily, 7.5f))
            {
                g.DrawString(text, f, b, rect, sf);
            }
        }

        /// <summary>
        /// 端子**数据类型**颜色（与连线状态色分开）：
        /// 图像=青、位姿=紫、坐标=蓝、角度=橙、数值=绿、判定=红、文本=灰。
        /// 一眼能看出"这根线传的是图还是数"。
        /// </summary>
        private static Color KindColor(FlowPortKind kind)
        {
            switch (kind)
            {
                case FlowPortKind.Image: return Color.FromArgb(110, 210, 235);
                case FlowPortKind.Pose: return Color.FromArgb(190, 150, 245);
                case FlowPortKind.Point: return Color.FromArgb(130, 180, 255);
                case FlowPortKind.Angle: return Color.FromArgb(255, 185, 95);
                case FlowPortKind.Number: return Color.FromArgb(140, 220, 150);
                case FlowPortKind.Bool: return Color.FromArgb(245, 130, 130);
                case FlowPortKind.Rect: return Color.FromArgb(150, 205, 255);
                case FlowPortKind.Geometry: return Color.FromArgb(200, 220, 120);
                case FlowPortKind.BlobList: return Color.FromArgb(255, 210, 120);
                case FlowPortKind.Text: return Color.FromArgb(190, 195, 205);
                default: return UiTheme.ColorTextSecondary;
            }
        }

        /// <summary>节点头部左侧的类型色条（由模块声明提供，画布不认识具体节点类型）。</summary>
        private static Color TypeStripeColor(FlowNode n)
        {
            FlowNodeType t;
            if (n == null || !n.TryGetType(out t)) return UiTheme.ColorTextSecondary;

            FlowModuleInfo module = FlowModule.FindByType(t);
            if (module == null || module.Module == null) return UiTheme.ColorTextSecondary;

            Color c = module.Module.Ui.StripeColor;
            return (c.A == 0 && c.R == 0 && c.G == 0 && c.B == 0) ? UiTheme.ColorTextSecondary : c;
        }

        /// <summary>端子符号：小方块（VisionPro 风格），方块中心即连线端点。</summary>
        private static void DrawPortGlyph(Graphics g, PointF c, Color color)
        {
            var rect = new RectangleF(c.X - PortGlyph * 0.5f, c.Y - PortGlyph * 0.5f, PortGlyph, PortGlyph);
            using (Brush b = new SolidBrush(color)) g.FillRectangle(b, rect);
            using (Pen p = new Pen(UiTheme.ColorHeaderBg, 1f))
                g.DrawRectangle(p, rect.X, rect.Y, rect.Width, rect.Height);
        }

        /// <summary>
        /// 端子符号颜色。拉线中：源端子橙、兼容目标绿、其余（含不兼容）灰禁用；
        /// 空闲态：悬停提亮，其余强调色。
        /// </summary>
        private Color PortColor(FlowNode node, FlowPortDef port, FlowPortDir dir, int index, bool hovered)
        {
            if (_dragKind == DragKind.DrawLink && _linkSource.Node != null)
            {
                if (node == _linkSource.Node && dir == _linkSource.Dir && index == _linkSource.Index)
                    return UiTheme.ColorWarn;                            // 拉线源端子
                return IsDragTargetCompatible(node, port, dir)
                    ? UiTheme.ColorOk                                    // 兼容目标
                    : UiTheme.ColorDisabled;                             // 不兼容 / 同侧：禁用
            }
            return hovered ? UiTheme.Lighten(UiTheme.ColorAccent, 0.45f) : UiTheme.ColorAccent;
        }

        /// <summary>端子名称文字颜色：拉线中不兼容目标淡化，悬停提亮，其余次文本色。</summary>
        private Color PortTextColor(FlowNode node, FlowPortDef port, FlowPortDir dir, int index, bool hovered)
        {
            if (_dragKind == DragKind.DrawLink && _linkSource.Node != null
                && dir != _linkSource.Dir
                && !IsDragTargetCompatible(node, port, dir))
            {
                return WithAlpha(UiTheme.ColorTextSecondary, 80);
            }
            return hovered ? UiTheme.ColorTextPrimary : UiTheme.ColorTextSecondary;
        }

        /// <summary>
        /// 拉线中，端子（作为潜在目标）与源端子是否可连。含自连 / 成环 / 同侧拒绝。
        /// 仅在 <see cref="PortColor"/> / 提示文案处使用，不影响 <see cref="FinishLinkDrag"/> 的最终判定。
        /// </summary>
        private bool IsDragTargetCompatible(FlowNode node, FlowPortDef port, FlowPortDir dir)
        {
            if (_linkSource.Node == null || _linkSource.Port == null) return false;
            if (dir == _linkSource.Dir) return false;                   // 只允许 输出 → 输入

            FlowNode fromNode, toNode;
            FlowPortDef fromPort, toPort;
            if (_linkSource.Dir == FlowPortDir.Out)
            {
                fromNode = _linkSource.Node; fromPort = _linkSource.Port;
                toNode = node;               toPort = port;
            }
            else
            {
                fromNode = node;             fromPort = port;
                toNode = _linkSource.Node;   toPort = _linkSource.Port;
            }

            if (fromNode == toNode) return false;                       // 自连视为环
            if (!toPort.Accepts(fromPort.Kind)) return false;           // 类型不兼容
            if (_flow == null) return false;
            return !FlowOrder.WouldCreateCycle(_flow, fromNode.Id, toNode.Id);
        }

        /// <summary>按运行状态取边框色（灰 / 蓝 / 绿 / 黄 / 红）；选中态优先用强调色描边。</summary>
        private static Color BorderColor(FlowNodeRunState state, bool selected)
        {
            switch (state)
            {
                case FlowNodeRunState.Running: return UiTheme.ColorRunning;
                case FlowNodeRunState.Ok:      return UiTheme.ColorOk;
                case FlowNodeRunState.Warn:    return UiTheme.ColorWarn;
                case FlowNodeRunState.Failed:  return UiTheme.ColorNg;
                default:
                    return selected ? UiTheme.ColorAccent : UiTheme.ColorDisabled;
            }
        }

        /// <summary>
        /// 卡片底部的结果区（常驻两行，见 <see cref="NodeHeight"/>）：
        /// 第一行 = 结果摘要（如 <c>3 个 · 0.93</c>），第二行 = 耗时 + 状态标记。
        /// <para>
        /// 未运行节点第一行显示灰字「未运行」、第二行留空 —— 不能显示 <c>0</c>：
        /// "0 个目标"和"还没跑过"是两件事，用同一个数字表示会让人把未运行读成检测为空。
        /// </para>
        /// <para>摘要行在 NG / 警告时变色（红 / 黄），边框同色 —— 这是"哪个节点出事了一眼看出来"的唯一出口。</para>
        /// </summary>
        private static void DrawRunOverlay(Graphics g, RectangleF r, FlowNodeRunState state, FlowNodeRunInfo run)
        {
            float x = r.X + 10f;
            float w = r.Width - 20f;
            // 端子逐行排在标题下方，摘要在它们之后（NodeHeight 已含行数）
            float y1 = r.Bottom - NodePadBottom - PortLabelH - SummaryRowH * 2f - 1f;
            float y2 = y1 + SummaryRowH;

            var sfNear = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center,
                                            Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
            var sfFar = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };

            // ---- 第一行：结果摘要 ----
            string mark = "";
            Color markColor = UiTheme.ColorTextSecondary;
            if (state == FlowNodeRunState.Failed) { mark = "NG"; markColor = UiTheme.ColorNg; }
            else if (state == FlowNodeRunState.Warn) { mark = "警告"; markColor = UiTheme.ColorWarn; }
            else if (state == FlowNodeRunState.Running) { mark = "运行中"; markColor = UiTheme.ColorRunning; }

            float markW = string.IsNullOrEmpty(mark) ? 0f : 34f;

            string summary = (run != null) ? (run.Summary ?? "") : "";
            if (state == FlowNodeRunState.Waiting || string.IsNullOrEmpty(summary))
            {
                if (state == FlowNodeRunState.Waiting)
                {
                    var rectWait = new RectangleF(x, y1, w, SummaryRowH);
                    // ColorDisabled 在深色卡片上几乎看不见，这里用次要文字色降一点透明度
                    using (Brush b = new SolidBrush(WithAlpha(UiTheme.ColorTextSecondary, 150)))
                        g.DrawString("未运行", UiTheme.FontMono, b, rectWait, sfNear);
                }
            }
            else
            {
                Color sumColor = state == FlowNodeRunState.Failed ? UiTheme.ColorNg
                               : state == FlowNodeRunState.Warn ? UiTheme.ColorWarn
                               : UiTheme.ColorTextPrimary;
                var rectSum = new RectangleF(x, y1, Math.Max(20f, w - markW), SummaryRowH);
                using (Brush b = new SolidBrush(sumColor))
                    g.DrawString(summary, UiTheme.FontMono, b, rectSum, sfNear);
            }

            if (!string.IsNullOrEmpty(mark))
            {
                var rectMark = new RectangleF(r.Right - 10f - markW, y1, markW, SummaryRowH);
                using (Brush b = new SolidBrush(markColor))
                    g.DrawString(mark, UiTheme.FontMono, b, rectMark, sfFar);
            }

            // ---- 第二行：耗时（仅在有结果时显示）----
            if (state != FlowNodeRunState.Waiting && run != null && run.ElapsedMs >= 0)
            {
                var rectMs = new RectangleF(x, y2, w, SummaryRowH);
                using (Brush b = new SolidBrush(UiTheme.ColorTextSecondary))
                    g.DrawString(run.ElapsedMs + " ms", UiTheme.FontMono, b, rectMs, sfNear);
            }
        }

        // ---------------------- 覆盖层：拖拽提示 / tooltip / toast ----------------------

        /// <summary>拉线中在画布左下角显示实时判定（兼容 / 不兼容原因 / 将替换 / 成环）。</summary>
        private void DrawDragHint(Graphics g)
        {
            if (_dragKind != DragKind.DrawLink || _linkSource.Node == null) return;

            string text;
            Color color;
            BuildDragHint(out text, out color);

            SizeF size = g.MeasureString(text, UiTheme.FontBody);
            var rect = new RectangleF(8f, Height - size.Height - 12f, size.Width + 16f, size.Height + 8f);
            using (Brush b = new SolidBrush(UiTheme.ColorHeaderBg)) g.FillRectangle(b, rect);
            using (Pen p = new Pen(color, 1f)) g.DrawRectangle(p, rect.X, rect.Y, rect.Width, rect.Height);
            using (Brush b = new SolidBrush(UiTheme.ColorTextPrimary))
                g.DrawString(text, UiTheme.FontBody, b,
                    new RectangleF(rect.X + 8f, rect.Y, rect.Width - 10f, rect.Height),
                    new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center });
        }

        /// <summary>拼拉线实时提示文案。</summary>
        private void BuildDragHint(out string text, out Color color)
        {
            text = (_linkSource.Dir == FlowPortDir.Out)
                ? "拖到输入端子上建立连接（类型相同才可连）"
                : "拖到输出端子上建立连接（类型相同才可连）";
            color = UiTheme.ColorTextSecondary;

            PortHit target;
            if (!HitTestPort(_linkPreviewEnd, out target)) return;

            FlowNode fromNode, toNode;
            FlowPortDef fromPort, toPort;
            if (_linkSource.Dir == FlowPortDir.Out && target.Dir == FlowPortDir.In)
            {
                fromNode = _linkSource.Node; fromPort = _linkSource.Port;
                toNode = target.Node;        toPort = target.Port;
            }
            else if (_linkSource.Dir == FlowPortDir.In && target.Dir == FlowPortDir.Out)
            {
                fromNode = target.Node;      fromPort = target.Port;
                toNode = _linkSource.Node;   toPort = _linkSource.Port;
            }
            else
            {
                text = "连线必须从输出端子（右）连到输入端子（左）";
                color = UiTheme.ColorNg;
                return;
            }

            string fromName = FlowOrder.Label(fromNode) + "." + fromPort.DisplayName;
            string toName = FlowOrder.Label(toNode) + "." + toPort.DisplayName;

            if (fromNode == toNode)
            {
                text = "不能连接到节点自身";
                color = UiTheme.ColorNg;
                return;
            }
            if (!toPort.Accepts(fromPort.Kind))
            {
                text = "类型不兼容：" + fromPort.Kind + " → " + toPort.Kind
                     + "（" + fromName + " → " + toName + "）";
                color = UiTheme.ColorNg;
                return;
            }
            if (FlowOrder.WouldCreateCycle(_flow, fromNode.Id, toNode.Id))
            {
                text = "会形成环路，已禁止：" + fromName + " → " + toName;
                color = UiTheme.ColorNg;
                return;
            }

            bool replace = _flow.FindLinksTo(toNode.Id, toPort.Key).Count > 0;
            text = (replace ? "松开替换原连线：" : "松开建立连接：") + fromName + " → " + toName;
            color = replace ? UiTheme.ColorWarn : UiTheme.ColorOk;
        }

        /// <summary>端子悬停 tooltip：端子名 + 类型（+ Hint）。</summary>
        private void DrawPortTooltip(Graphics g)
        {
            if (!_hoverValid || _dragKind != DragKind.None || _hoverPort.Port == null) return;

            string line1 = _hoverPort.Port.DisplayName + "  (" + _hoverPort.Port.Kind + ")";
            string line2 = _hoverPort.Port.Hint;
            bool hasHint = !string.IsNullOrEmpty(line2);

            SizeF s1 = g.MeasureString(line1, UiTheme.FontBody);
            SizeF s2 = hasHint ? g.MeasureString(line2, UiTheme.FontMono) : SizeF.Empty;
            float w = Math.Max(s1.Width, s2.Width) + 14f;
            float h = s1.Height + (hasHint ? s2.Height : 0f) + 10f;

            float x = _hoverPt.X + 14f;
            float y = _hoverPt.Y + 16f;
            if (x + w > Width - 2f) x = _hoverPt.X - w - 8f;
            if (y + h > Height - 2f) y = _hoverPt.Y - h - 8f;
            if (x < 2f) x = 2f;
            if (y < 2f) y = 2f;

            var rect = new RectangleF(x, y, w, h);
            using (Brush b = new SolidBrush(UiTheme.ColorHeaderBg)) g.FillRectangle(b, rect);
            using (Pen p = new Pen(UiTheme.ColorDivider, 1f))
                g.DrawRectangle(p, rect.X, rect.Y, rect.Width, rect.Height);

            float curY = rect.Y + 5f;
            using (Brush b = new SolidBrush(UiTheme.ColorTextPrimary))
                g.DrawString(line1, UiTheme.FontBody, b, new PointF(rect.X + 7f, curY));
            curY += s1.Height;
            if (hasHint)
            {
                using (Brush b = new SolidBrush(UiTheme.ColorTextSecondary))
                    g.DrawString(line2, UiTheme.FontMono, b, new PointF(rect.X + 7f, curY));
            }
        }

        /// <summary>画布底部居中的临时提示（连线被拒等原因）。</summary>
        /// <summary>左下角常驻操作提示：滚轮缩放 / 中键拖动平移 / 双击空白自适应。</summary>
        private void DrawViewHint(Graphics g)
        {
            string text = "滚轮缩放 " + (_viewZoom * 100f).ToString("0") + "%　·　中键/右键拖动平移　·　双击空白自适应";
            using (Font f = new Font(UiTheme.FontSmall.FontFamily, 8.5f))
            using (var brush = new SolidBrush(WithAlpha(UiTheme.ColorTextSecondary, 150)))
            {
                SizeF sz = g.MeasureString(text, f);
                g.DrawString(text, f, brush, 8f, Height - sz.Height - 6f);
            }
        }

        private void DrawToast(Graphics g)
        {
            if (string.IsNullOrEmpty(_toast)) return;

            SizeF size = g.MeasureString(_toast, UiTheme.FontBody);
            float w = size.Width + 20f;
            float h = size.Height + 10f;
            var rect = new RectangleF((Width - w) * 0.5f, Height - h - 14f, w, h);

            using (Brush b = new SolidBrush(UiTheme.ColorHeaderBg)) g.FillRectangle(b, rect);
            using (Pen p = new Pen(_toastError ? UiTheme.ColorNg : UiTheme.ColorOk, 1.4f))
                g.DrawRectangle(p, rect.X, rect.Y, rect.Width, rect.Height);
            using (Brush b = new SolidBrush(UiTheme.ColorTextPrimary))
                g.DrawString(_toast, UiTheme.FontBody, b, rect,
                    new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
        }

        private void ShowToast(string text, bool error)
        {
            _toast = text;
            _toastError = error;
            _toastTimer.Stop();
            _toastTimer.Start();
            Invalidate();
        }

        private void OnToastTick(object sender, EventArgs e)
        {
            _toastTimer.Stop();
            _toast = null;
            Invalidate();
        }

        private static GraphicsPath RoundedRect(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            float d = radius * 2f;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        /// <summary>把主题色按给定 alpha 生成半透明变体（画布内不出现颜色字面量）。</summary>
        private static Color WithAlpha(Color c, int alpha)
        {
            return Color.FromArgb(alpha, c);
        }

        #endregion

        #region 事件触发

        private void RaiseNodeAdded(FlowNode node)
        {
            EventHandler<FlowNodeEventArgs> h = NodeAdded;
            if (h != null) h(this, new FlowNodeEventArgs(node));
        }

        private void RaiseNodeMoved(FlowNode node)
        {
            EventHandler<FlowNodeEventArgs> h = NodeMoved;
            if (h != null) h(this, new FlowNodeEventArgs(node));
        }

        private void RaiseNodeSelected(FlowNode node)
        {
            EventHandler<FlowNodeEventArgs> h = NodeSelected;
            if (h != null) h(this, new FlowNodeEventArgs(node));
        }

        private void RaiseLinkAdded(FlowLink link)
        {
            EventHandler<FlowLinkEventArgs> h = LinkAdded;
            if (h != null) h(this, new FlowLinkEventArgs(link));
        }

        private void RaiseLinkRemoved(FlowLink link)
        {
            EventHandler<FlowLinkEventArgs> h = LinkRemoved;
            if (h != null) h(this, new FlowLinkEventArgs(link));
        }

        private void RaiseLinkRejected(string reason,
                                       string fromNodeId, string fromPort,
                                       string toNodeId, string toPort)
        {
            EventHandler<FlowLinkRejectedEventArgs> h = LinkRejected;
            if (h != null) h(this, new FlowLinkRejectedEventArgs(reason, fromNodeId, fromPort, toNodeId, toPort));
        }

        private void RaiseLinkReplaced(FlowLink oldLink, FlowLink newLink)
        {
            EventHandler<FlowLinkReplacedEventArgs> h = LinkReplaced;
            if (h != null) h(this, new FlowLinkReplacedEventArgs(oldLink, newLink));
        }

        private void RaiseChanged()
        {
            EventHandler h = Changed;
            if (h != null) h(this, EventArgs.Empty);
        }

        #endregion
    }
}
