using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using VisionPlatform.Flow;

namespace VisionPlatform.UI
{
    /// <summary>节点事件参数（新增 / 移动 / 选中）。</summary>
    public sealed class FlowNodeEventArgs : EventArgs
    {
        public FlowNode Node { get; private set; }
        public FlowNodeEventArgs(FlowNode node) { Node = node; }
    }

    /// <summary>连线事件参数（新增 / 删除）。</summary>
    public sealed class FlowLinkEventArgs : EventArgs
    {
        public FlowLink Link { get; private set; }
        public FlowLinkEventArgs(FlowLink link) { Link = link; }
    }

    /// <summary>
    /// 画布节点的运行状态（FR-ALG-23）：等待（灰）/ 运行中（蓝）/ 成功（绿）/ 失败（红）。
    /// 由执行期 <see cref="FlowNodeStatus"/> 映射而来，画布只认这四个视觉态。
    /// </summary>
    public enum FlowNodeRunState
    {
        /// <summary>未执行 / 已复位。</summary>
        Waiting,

        /// <summary>正在执行（高亮）。</summary>
        Running,

        /// <summary>执行成功。</summary>
        Ok,

        /// <summary>执行失败（判定 NG 或执行异常）。</summary>
        Failed
    }

    /// <summary>单个节点的运行状态与耗时。</summary>
    public sealed class FlowNodeRunInfo
    {
        /// <summary>视觉状态。</summary>
        public FlowNodeRunState State = FlowNodeRunState.Waiting;

        /// <summary>该节点耗时（整数毫秒）；未执行时为 -1（不显示）。</summary>
        public int ElapsedMs = -1;
    }

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
        private const int NodeW = 150;           // 节点宽度
        private const int TitleH = 26;           // 节点标题行高度
        private const int RowH = 18;             // 端子行高
        private const int NodePadBottom = 4;     // 端子区下方留白
        private const int NominalNodeH = 48;     // 工具箱拖入落点居中用的名义高度（1 行端子的实际高度）
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

        // ---------------------- 运行状态（FR-ALG-23）----------------------

        /// <summary>把执行期状态映射为画布视觉态。</summary>
        public static FlowNodeRunState ToRunState(FlowNodeStatus status)
        {
            switch (status)
            {
                case FlowNodeStatus.Running: return FlowNodeRunState.Running;
                case FlowNodeStatus.Ok:      return FlowNodeRunState.Ok;
                case FlowNodeStatus.Ng:      return FlowNodeRunState.Failed;
                default:                     return FlowNodeRunState.Waiting;
            }
        }

        /// <summary>
        /// 设置节点运行状态与耗时并重绘。耗时传负数表示不显示耗时文本。
        /// </summary>
        public void SetNodeStatus(string nodeId, FlowNodeRunState state, int elapsedMs)
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
        /// 端子行数 = max(输入端子数, 输出端子数)；节点高度 = 标题行 + 行数 × 行高 + 底部留白。
        /// </summary>
        private static int PortRowCount(FlowNode n)
        {
            return Math.Max(NodeInputs(n).Count, NodeOutputs(n).Count);
        }

        private static float NodeHeight(FlowNode n)
        {
            return TitleH + PortRowCount(n) * RowH + NodePadBottom;
        }

        /// <summary>
        /// 端子中心坐标：输入端子沿左边界、输出端子沿右边界，自上而下按行排列。
        /// </summary>
        private static PointF PortPoint(FlowNode n, FlowPortDir dir, int index)
        {
            float y = n.Y + TitleH + index * RowH + RowH * 0.5f;
            return new PointF(dir == FlowPortDir.In ? n.X : n.X + NodeW, y);
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
                if (DistanceToPolyline(pt, BezierPolyline(a, b)) <= HitTol) return i;
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
            float cx = Math.Max(MinBezierCx, Math.Abs(b.X - a.X) * 0.5f);
            c1 = new PointF(a.X + cx, a.Y);
            c2 = new PointF(b.X - cx, b.Y);
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

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();

            if (e.Button != MouseButtons.Left || _flow == null) return;

            var pt = new PointF(e.X, e.Y);

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

            // 4) 空白 → 清空选择
            _selectedNodeId = null;
            _selectedLinkIndex = -1;
            RaiseNodeSelected(null);
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if (_dragKind == DragKind.MoveNode && _dragNode != null)
            {
                float nx = e.X - _dragOffset.X;
                float ny = e.Y - _dragOffset.Y;
                if (nx < 0f) nx = 0f;
                if (ny < 0f) ny = 0f;
                _dragNode.X = nx;
                _dragNode.Y = ny;
                Invalidate();
            }
            else if (_dragKind == DragKind.DrawLink)
            {
                _linkPreviewEnd = new PointF(e.X, e.Y);
                Invalidate();       // 预览曲线 + 目标端子兼容高亮 + 拖拽提示都要刷新
            }
            else
            {
                // 空闲态：跟踪端子悬停（tooltip），仅在悬停端子变化时重绘
                var pt = new PointF(e.X, e.Y);
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

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);

            if (_dragKind == DragKind.MoveNode && _dragNode != null)
            {
                RaiseNodeMoved(_dragNode);
                RaiseChanged();
            }
            else if (_dragKind == DragKind.DrawLink)
            {
                FinishLinkDrag(new PointF(e.X, e.Y));
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
                ShowToast("连线只能从输出端子（右侧）连到输入端子（左侧）", true);
                return;
            }

            if (fromNode == toNode)
            {
                ShowToast("不能连接到节点自身", true);
                return;
            }

            // ---- 类型兼容（FlowPortSchema）----
            if (!toPort.Accepts(fromPort.Kind))
            {
                ShowToast("类型不兼容：" + fromPort.DisplayName + "(" + fromPort.Kind
                          + ") 不能连到 " + toPort.DisplayName + "(" + toPort.Kind + ")", true);
                return;
            }

            // ---- 成环预检（FlowOrder）----
            if (FlowOrder.WouldCreateCycle(_flow, fromNode.Id, toNode.Id))
            {
                ShowToast("该连线会形成环路，已拒绝", true);
                return;
            }

            // ---- 重复连线：幂等处理，不再重复建链 / 发事件 ----
            if (LinkExists(fromNode.Id, fromPort.Key, toNode.Id, toPort.Key))
            {
                ShowToast("该连线已存在", false);
                return;
            }

            // ---- 端口级唯一入边：替换旧边 ----
            // 理由：端口化后一个输入端子语义上只有一个数据来源（执行器按端子取唯一输入），
            // 端子上已有连线时再连新线，用户意图明确是「换数据源」——按 VisionPro 的行为
            // 用「替换」而不是「拒绝」，省去先删旧线再连新线的两步操作。
            // 被替换的旧边照常发 LinkRemoved，FlowForm 侧无需特殊处理。
            for (int i = _flow.Links.Count - 1; i >= 0; i--)
            {
                FlowLink l = _flow.Links[i];
                if (l == null) continue;
                if (l.ToNodeId == toNode.Id && (l.ToPort ?? "") == toPort.Key)
                {
                    _flow.Links.RemoveAt(i);
                    RaiseLinkRemoved(l);
                }
            }

            var link = new FlowLink(fromNode.Id, fromPort.Key, toNode.Id, toPort.Key);
            _flow.Links.Add(link);
            _selectedLinkIndex = _flow.Links.Count - 1;
            _selectedNodeId = null;
            RaiseLinkAdded(link);
            RaiseChanged();
            Invalidate();
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

            DrawGrid(g);

            if (_flow == null) return;

            // 连线（先画，压在节点下面）
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

            // 覆盖层：拖拽提示 / 端子 tooltip / 临时提示（自下而上依次压顶）
            DrawDragHint(g);
            DrawPortTooltip(g);
            DrawToast(g);
        }

        private void DrawGrid(Graphics g)
        {
            using (Pen pen = new Pen(WithAlpha(UiTheme.ColorTextSecondary, 26), 1f))
            {
                const int step = 20;
                for (int x = 0; x < Width; x += step) g.DrawLine(pen, x, 0, x, Height);
                for (int y = 0; y < Height; y += step) g.DrawLine(pen, 0, y, Width, y);
            }
        }

        private void DrawLink(Graphics g, FlowLink link, bool selected)
        {
            if (link == null) return;

            PointF a, b;
            if (!LinkEndPoints(link, out a, out b)) return;

            Color color = selected ? UiTheme.ColorWarn : WithAlpha(UiTheme.ColorAccent, 215);

            PointF c1, c2;
            BezierControls(a, b, out c1, out c2);
            using (Pen pen = new Pen(color, selected ? 2.6f : 2f))
            {
                g.DrawBezier(pen, a, c1, c2, b);
            }

            // 箭头画在目标输入端子处，方向沿曲线终点切线（c2 → b）
            float dx = b.X - c2.X, dy = b.Y - c2.Y;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len > 1e-3f)
            {
                float ux = dx / len, uy = dy / len;
                var p1 = new PointF(b.X - ux * ArrowSize - uy * ArrowSize * 0.5f,
                                    b.Y - uy * ArrowSize + ux * ArrowSize * 0.5f);
                var p2 = new PointF(b.X - ux * ArrowSize + uy * ArrowSize * 0.5f,
                                    b.Y - uy * ArrowSize - ux * ArrowSize * 0.5f);
                using (Brush brush = new SolidBrush(color))
                    g.FillPolygon(brush, new[] { b, p1, p2 });
            }
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
                var labelRect = new RectangleF(r.X + 10f, c.Y - RowH * 0.5f, r.Width - 20f, RowH);
                using (Brush tb = new SolidBrush(PortTextColor(n, ins[k], FlowPortDir.In, k, hovered)))
                    g.DrawString(ins[k].DisplayName, UiTheme.FontBody, tb, labelRect, sfNear);
            }

            for (int k = 0; k < outs.Count; k++)
            {
                if (outs[k] == null) continue;
                PointF c = PortPoint(n, FlowPortDir.Out, k);
                bool hovered = _hoverValid && _hoverPort.Node == n
                    && _hoverPort.Dir == FlowPortDir.Out && _hoverPort.Index == k;
                DrawPortGlyph(g, c, PortColor(n, outs[k], FlowPortDir.Out, k, hovered));
                var labelRect = new RectangleF(r.X + 10f, c.Y - RowH * 0.5f, r.Width - 20f, RowH);
                using (Brush tb = new SolidBrush(PortTextColor(n, outs[k], FlowPortDir.Out, k, hovered)))
                    g.DrawString(outs[k].DisplayName, UiTheme.FontBody, tb, labelRect, sfFar);
            }

            // 运行状态附加信息：节点下方「xx ms」+ 失败标记（纯文本，FR-ALG-23）
            DrawRunOverlay(g, r, state, run);
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

        /// <summary>按运行状态取边框色（灰 / 蓝 / 绿 / 红）；选中态优先用强调色描边。</summary>
        private static Color BorderColor(FlowNodeRunState state, bool selected)
        {
            switch (state)
            {
                case FlowNodeRunState.Running: return UiTheme.ColorRunning;
                case FlowNodeRunState.Ok:      return UiTheme.ColorOk;
                case FlowNodeRunState.Failed:  return UiTheme.ColorNg;
                default:
                    return selected ? UiTheme.ColorAccent : UiTheme.ColorDisabled;
            }
        }

        /// <summary>画节点下方的耗时文本与失败标记（全部纯文本，不靠图标区分）。</summary>
        private static void DrawRunOverlay(Graphics g, RectangleF r, FlowNodeRunState state, FlowNodeRunInfo run)
        {
            if (state == FlowNodeRunState.Waiting) return;

            var msRect = new RectangleF(r.X, r.Bottom + 1f, r.Width * 0.55f, 13f);
            var markRect = new RectangleF(r.Right - r.Width * 0.35f, r.Bottom + 1f, r.Width * 0.35f, 13f);

            var sf = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Near };

            if (run != null && run.ElapsedMs >= 0)
            {
                using (Brush b = new SolidBrush(UiTheme.ColorTextSecondary))
                    g.DrawString(run.ElapsedMs + " ms", UiTheme.FontMono, b, msRect, sf);
            }

            if (state == FlowNodeRunState.Failed)
            {
                var sfMark = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Near };
                using (Brush b = new SolidBrush(UiTheme.ColorNg))
                    g.DrawString("NG", UiTheme.FontButton, b, markRect, sfMark);
            }
            else if (state == FlowNodeRunState.Running)
            {
                var sfMark = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Near };
                using (Brush b = new SolidBrush(UiTheme.ColorRunning))
                    g.DrawString("运行中", UiTheme.FontMono, b, markRect, sfMark);
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

        private void RaiseChanged()
        {
            EventHandler h = Changed;
            if (h != null) h(this, EventArgs.Empty);
        }

        #endregion
    }
}
